// Ocean.cs — minimal: single settings asset, no presets/slider/phase anchor.
// Keeps: naturalized Gerstner, optional interactions, fixed-size arrays,
// opaque-at-1 transparency, CPU sampling, and camera-centered mesh.

using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

[ExecuteAlways, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class Ocean : MonoBehaviour
{
    [Header("Wave Settings")]
    public OceanWaveSettings settings;     // The single source of truth

    [Header("Material / Physics")]
    public Material oceanMaterial;
    [Tooltip("Gravity used for deep-water dispersion (omega = sqrt(g*k)) when wave speed is 0.")]
    public float gravity = 9.81f;

    [Header("Mesh (camera-centered grid)")]
    [Min(4)] public int xVerts = 256;
    [Min(4)] public int zVerts = 256;
    [Min(0.01f)] public float spacing = 2f;

    [Header("Choppiness")]
    [Tooltip("Global choppiness multiplier (scales steepness for both GPU & CPU).")]
    public float choppiness = 1f;

    [Header("Natural Variation")]
    [Tooltip("Enable extra realism: split each wave into several components with slight dir/freq/phase differences.")]
    public bool naturalize = true;
    [Range(1, 3)] public int componentsPerWave = 2;
    [Range(0f, 45f)] public float directionSpreadDeg = 18f;
    [Tooltip("Random amplitude variance (± %)")]
    [Range(0f, 0.5f)] public float amplitudeJitter = 0.18f;
    [Tooltip("Random wavelength variance (± %)")]
    [Range(0f, 0.3f)] public float wavelengthJitter = 0.10f;
    [Tooltip("Random frequency variance (± %) applied to omega (prevents synchronized repetition).")]
    [Range(0f, 0.1f)] public float omegaJitter = 0.03f;
    [Tooltip("Seed for deterministic randomization. Changing it reshuffles the sea pattern.")]
    public int randomSeed = 1337;

    [Header("Wave Interactions")]
    [Tooltip("Add bound-harmonic sum/difference components from pairs of waves to create visible interference patterns.")]
    public bool enableInteractions = true;
    [Range(0, 16)] public int maxInteractionComponents = 8;
    [Tooltip("Scales interaction amplitude (A_i * A_j * strength). Keep small to avoid explosions.")]
    [Range(0f, 1f)] public float interactionStrength = 0.35f;

    [Header("Transparency Control")]
    [Tooltip("If true, when _Transparency >= 0.999 the renderer is pushed to the Opaque render queue.")]
    public bool forceOpaqueAtFull = true;

    // ===== Internals =====
    private const int MAX_WAVES = 32;                   // Keep in sync with shader
    private Vector4[] _dirTmp = new Vector4[MAX_WAVES]; // (Dx, Dz, A, S)
    private Vector4[] _wlTmp = new Vector4[MAX_WAVES]; // (WL, omega, phi, _)
    private bool _arraysPrimed = false;

    private struct WaveCPU
    {
        public float A, k, w, S, phi; // amplitude, wavenumber, angular frequency, steepness, phase offset
        public Vector2 D;             // unit dir (x,z)
    }

    private WaveCPU[] _cpuWaves = Array.Empty<WaveCPU>();
    private static Ocean _instance;

    private Material _appliedMat;
    private OceanWaveSettings _lastSettings;

    // ===== Unity lifecycle =====
    void OnEnable()
    {
        _instance = this;

        _appliedMat = oceanMaterial;
        SyncRendererMaterial();
        PrimeArrayLengths();

        ResubscribeSettings(settings);

        EnsureMesh();
        RecomputeAndApplyEffectiveWaves();
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
    }

    void OnDisable()
    {
        if (_instance == this) _instance = null;
        ResubscribeSettings(null);
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;

        SyncRendererMaterial();
        PrimeArrayLengths();

        if (settings != _lastSettings)
            ResubscribeSettings(settings);

        EnsureMesh();
        RecomputeAndApplyEffectiveWaves();
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
    }

    void Update()
    {
        if (!oceanMaterial) return;

        if (_appliedMat != oceanMaterial)
        {
            _appliedMat = oceanMaterial;
            SyncRendererMaterial();
            _arraysPrimed = false;
            PrimeArrayLengths();
            RecomputeAndApplyEffectiveWaves();
        }

        if (settings != _lastSettings)
        {
            ResubscribeSettings(settings);
            RecomputeAndApplyEffectiveWaves();
        }

        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
    }

    // ===== Fixed-size array priming (prevents Unity "cap to previous size" warnings) =====
    private void PrimeArrayLengths()
    {
        if (!oceanMaterial || _arraysPrimed) return;

        for (int i = 0; i < MAX_WAVES; i++) { _dirTmp[i] = Vector4.zero; _wlTmp[i] = Vector4.zero; }
        oceanMaterial.SetVectorArray("_DirAmpSteep", _dirTmp);
        oceanMaterial.SetVectorArray("_WlOmegaPad", _wlTmp);
        oceanMaterial.SetInt("_WaveCount", 0);

        _arraysPrimed = true;
    }

    // ===== Renderer material sync =====
    private void SyncRendererMaterial()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr && oceanMaterial && mr.sharedMaterial != oceanMaterial)
            mr.sharedMaterial = oceanMaterial;
    }

    private void AdjustRenderQueueForTransparency()
    {
        if (!forceOpaqueAtFull || oceanMaterial == null) return;
        float a = oceanMaterial.HasProperty("_Transparency") ? oceanMaterial.GetFloat("_Transparency") : 1f;
        var mr = GetComponent<MeshRenderer>();
        if (mr && mr.sharedMaterial == oceanMaterial)
        {
            int target = (a >= 0.999f) ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            if (mr.sharedMaterial.renderQueue != target)
                mr.sharedMaterial.renderQueue = target;
        }
    }

    // ===== Settings live updates (Custom asset edits) =====
    void ResubscribeSettings(OceanWaveSettings newSettings)
    {
        if (_lastSettings != null)
            _lastSettings.OnChanged -= OnSettingsChanged;

        _lastSettings = newSettings;

        if (_lastSettings != null)
            _lastSettings.OnChanged += OnSettingsChanged;
    }

    void OnSettingsChanged()
    {
        RecomputeAndApplyEffectiveWaves();
    }

    // ===== Build & apply wave data =====
    void RecomputeAndApplyEffectiveWaves()
    {
        PrimeArrayLengths();

        int count; Vector4[] dir_amp_steep; Vector4[] wl_omega_phi; WaveCPU[] cpu; float assetChop;
        BakeFromAsset(settings, out count, out dir_amp_steep, out wl_omega_phi, out cpu, out assetChop, 0xA1B2C3D ^ randomSeed);

        // Clamp to fixed buffer
        count = Mathf.Min(count, MAX_WAVES);

        // Copy into fixed-size arrays
        System.Array.Clear(_dirTmp, 0, MAX_WAVES);
        System.Array.Clear(_wlTmp, 0, MAX_WAVES);
        if (count > 0)
        {
            System.Array.Copy(dir_amp_steep, _dirTmp, count);
            System.Array.Copy(wl_omega_phi, _wlTmp, count);
        }

        // Push to material
        oceanMaterial?.SetVectorArray("_DirAmpSteep", _dirTmp);
        oceanMaterial?.SetVectorArray("_WlOmegaPad", _wlTmp); // z = phi
        oceanMaterial?.SetInt("_WaveCount", count);
        oceanMaterial?.SetVector("_OceanOrigin", transform.position);

        // CPU sampler
        _cpuWaves = cpu;
    }

    // ===== Baking (naturalization + optional pairwise interactions) =====
    static float Rand01(int key)
    {
        unchecked
        {
            uint x = (uint)key;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }
    static float RandSigned(int key) => Rand01(key) * 2f - 1f;

    void BakeFromAsset(OceanWaveSettings asset,
        out int count, out Vector4[] dir_amp_steep, out Vector4[] wl_omega_phi, out WaveCPU[] cpu, out float assetChop, int seed)
    {
        assetChop = (asset ? asset.choppiness : 1f);
        var waves = (asset && asset.waves != null) ? asset.waves : Array.Empty<OceanWaveSettings.Wave>();

        int splits = naturalize ? Mathf.Clamp(componentsPerWave, 1, 3) : 1;
        float[] weights = splits == 3 ? new float[] { 0.5f, 0.3f, 0.2f }
                        : splits == 2 ? new float[] { 0.6f, 0.4f }
                                      : new float[] { 1f };

        // Jitter toggles
        float dirSpread = naturalize ? directionSpreadDeg : 0f;
        float ampJit = naturalize ? amplitudeJitter : 0f;
        float wlJit = naturalize ? wavelengthJitter : 0f;
        float omgJit = naturalize ? omegaJitter : 0f;

        var dirList = new List<Vector4>(Mathf.Min(waves.Length * splits, MAX_WAVES));
        var wlList = new List<Vector4>(dirList.Capacity);
        var cpuList = new List<WaveCPU>(dirList.Capacity);

        for (int i = 0; i < waves.Length; i++)
        {
            var wv = waves[i];

            float theta = wv.directionDegrees * Mathf.Deg2Rad;
            Vector2 Dbase = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;

            for (int s = 0; s < splits; s++)
            {
                if (dirList.Count >= MAX_WAVES) break;

                int key = seed ^ (i * 73856093) ^ (s * 19349663);

                float spreadSign = Mathf.Sign(RandSigned(key + 11));
                float spread = dirSpread * Mathf.Abs(RandSigned(key + 23));
                float angle = (spread * spreadSign) * Mathf.Deg2Rad;

                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                Vector2 D = new Vector2(Dbase.x * ca - Dbase.y * sa, Dbase.x * sa + Dbase.y * ca).normalized;

                float ampMul = 1f + ampJit * RandSigned(key + 101);
                float wlMul = 1f + wlJit * RandSigned(key + 202);
                float omgMul = 1f + omgJit * RandSigned(key + 303);

                float share = weights[Mathf.Min(s, weights.Length - 1)];
                float A = wv.amplitude * share * ampMul;
                float WL = Mathf.Max(0.001f, wv.wavelength * wlMul);

                float k = 2f * Mathf.PI / WL;
                float omg = (wv.speed > 0f) ? k * wv.speed : Mathf.Sqrt(gravity * k);
                omg *= omgMul;

                float S_eff = wv.steepness * choppiness * assetChop;

                float phi = Rand01(key + 404) * Mathf.PI * 2f;

                dirList.Add(new Vector4(D.x, D.y, A, S_eff));
                wlList.Add(new Vector4(WL, omg, phi, 0f));
                cpuList.Add(new WaveCPU { A = A, k = k, w = omg, S = S_eff, D = D, phi = phi });
            }
        }

        // Pairwise interactions (optional)
        if (enableInteractions && dirList.Count < MAX_WAVES && maxInteractionComponents > 0)
        {
            int n = dirList.Count;
            int added = 0;

            Vector2[] kvec = new Vector2[n];
            float[] omg = new float[n];
            float[] Aamp = new float[n];
            float[] phi = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector4 dir = dirList[i];
                Vector4 wl = wlList[i];
                Vector2 D = new Vector2(dir.x, dir.y);
                float k = 2f * Mathf.PI / Mathf.Max(0.001f, wl.x);
                kvec[i] = D * k; omg[i] = wl.y; Aamp[i] = dir.z; phi[i] = wl.z;
            }

            for (int i = 0; i < n && added < maxInteractionComponents && dirList.Count < MAX_WAVES; i++)
                for (int j = i + 1; j < n && added < maxInteractionComponents && dirList.Count < MAX_WAVES; j++)
                {
                    TryAddInteraction(kvec[i] + kvec[j], omg[i] + omg[j], Aamp[i], Aamp[j], phi[i] + phi[j],
                                      dirList, wlList, cpuList, ref added);
                    if (added >= maxInteractionComponents || dirList.Count >= MAX_WAVES) break;

                    TryAddInteraction(kvec[i] - kvec[j], Mathf.Abs(omg[i] - omg[j]), Aamp[i], Aamp[j], phi[i] - phi[j],
                                      dirList, wlList, cpuList, ref added);
                }
        }

        count = dirList.Count;
        dir_amp_steep = dirList.ToArray();
        wl_omega_phi = wlList.ToArray();
        cpu = cpuList.ToArray();
    }

    void TryAddInteraction(Vector2 kvec, float omega, float Ai, float Aj, float phi,
                           List<Vector4> dirList, List<Vector4> wlList, List<WaveCPU> cpuList, ref int added)
    {
        if (dirList.Count >= MAX_WAVES) return;
        float kmag = kvec.magnitude;
        if (kmag < 1e-4f) return;

        Vector2 D = kvec / kmag;
        float WL = 2f * Mathf.PI / kmag;

        float A = interactionStrength * 0.5f * (Ai * Aj);
        if (A <= 1e-4f) return;

        float S_eff = 0.5f; // conservative
        float k = kmag;

        dirList.Add(new Vector4(D.x, D.y, A, S_eff));
        wlList.Add(new Vector4(WL, omega, phi, 0f));
        cpuList.Add(new WaveCPU { A = A, k = k, w = omega, S = S_eff, D = D, phi = phi });
        added++;
    }

    // ===== Per-frame uniforms =====
    void UpdateMaterialProps(float tSeconds)
    {
        oceanMaterial?.SetFloat("_TimeSeconds", tSeconds);
        oceanMaterial?.SetVector("_OceanOrigin", transform.position);
    }

    // ===== Mesh generation (CW, faces up) =====
    void EnsureMesh()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(mf.sharedMesh);
#else
            Destroy(mf.sharedMesh);
#endif
        }
        mf.sharedMesh = GenerateGrid(xVerts, zVerts, spacing);
    }

    Mesh GenerateGrid(int xVerts, int zVerts, float spacing)
    {
        var mesh = new Mesh { name = "OceanGrid_CW" };

        int vCount = xVerts * zVerts;
        var verts = new Vector3[vCount];
        var uv = new Vector2[vCount];
        var idx = new int[(xVerts - 1) * (zVerts - 1) * 6];

        for (int z = 0; z < zVerts; z++)
        {
            for (int x = 0; x < xVerts; x++)
            {
                int i = z * xVerts + x;
                verts[i] = new Vector3((x - xVerts * 0.5f) * spacing, 0f, (z - zVerts * 0.5f) * spacing);
                uv[i] = new Vector2((float)x / (xVerts - 1), (float)z / (zVerts - 1));
            }
        }

        int t = 0;
        for (int z = 0; z < zVerts - 1; z++)
        {
            for (int x = 0; x < xVerts - 1; x++)
            {
                int i = z * xVerts + x;

                idx[t++] = i;
                idx[t++] = i + xVerts;
                idx[t++] = i + 1;

                idx[t++] = i + 1;
                idx[t++] = i + xVerts;
                idx[t++] = i + xVerts + 1;
            }
        }

        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = idx;

        mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return mesh;
    }

    // ===== Time helper (edit + play) =====
    float GetTimeSeconds()
    {
#if UNITY_EDITOR
        return Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.time;
#endif
    }

    // ===== Public CPU sampling =====
    /// <summary>Sample ocean at world XZ; returns WORLD water height and normal at time t (defaults to now).</summary>
    public static void Sample(Vector2 worldXZ, out float height, out Vector3 normal, float t = -1f)
    {
        height = 0f; normal = Vector3.up;
        if (_instance == null || _instance._cpuWaves == null) return;
        if (t < 0f) t = _instance.GetTimeSeconds();

        Vector2 xz = worldXZ - _instance.transform.position.XZ();

        Vector3 disp = Vector3.zero;
        Vector3 dPdX = new Vector3(1f, 0f, 0f);
        Vector3 dPdZ = new Vector3(0f, 0f, 1f);

        var waves = _instance._cpuWaves;
        for (int i = 0; i < waves.Length; i++)
        {
            var w = waves[i];
            float dot = w.D.x * xz.x + w.D.y * xz.y;
            float phase = w.k * dot - w.w * t + w.phi;
            float c = Mathf.Cos(phase);
            float s = Mathf.Sin(phase);

            float QA = w.S * w.A;

            disp.x += QA * w.D.x * c;
            disp.y += w.A * s;
            disp.z += QA * w.D.y * c;

            float dxp = w.k * w.D.x;
            float dzp = w.k * w.D.y;

            dPdX.x += -QA * w.D.x * dxp * s;
            dPdX.y += w.A * dxp * c;
            dPdX.z += -QA * w.D.y * dxp * s;

            dPdZ.x += -QA * w.D.x * dzp * s;
            dPdZ.y += w.A * dzp * c;
            dPdZ.z += -QA * w.D.y * dzp * s;
        }

        normal = Vector3.Normalize(Vector3.Cross(dPdZ, dPdX)); // up on flat patch
        height = _instance.transform.position.y + disp.y;      // world height
    }
}

static class OceanVecExt
{
    public static Vector2 XZ(this Vector3 v) => new Vector2(v.x, v.z);
}
