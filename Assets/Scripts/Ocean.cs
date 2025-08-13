// Ocean.cs — naturalized Gerstner + pairwise interactions + opaque toggle at Transparency=1
// Includes: preset blending, live updates, fixed-size shader arrays, material sync, correct mesh/CPU normals
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class Ocean : MonoBehaviour
{
    public enum PresetMode { Custom, Normal, Choppy, Storm }

    [Header("Wave Data (Custom Mode)")]
    public OceanWaveSettings settings;     // used when Selected Preset = Custom

    [Header("Presets")]
    public OceanWaveSettings presetNormal;
    public OceanWaveSettings presetChoppy;
    public OceanWaveSettings presetStorm;

    [Header("Preset Switch")]
    public PresetMode selectedPreset = PresetMode.Custom;
    [Tooltip("Seconds to blend when you change 'Selected Preset'.")]
    public float blendDuration = 3f;
    public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Material / Physics")]
    public Material oceanMaterial;
    [Tooltip("Gravity used for deep-water dispersion (omega = sqrt(g*k)) when wave speed is 0.")]
    public float gravity = 9.81f;

    [Header("Mesh (camera-centered grid)")]
    [Min(4)] public int xVerts = 256;
    [Min(4)] public int zVerts = 256;
    [Min(0.01f)] public float spacing = 2f;

    [Header("Misc")]
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
    private const int MAX_WAVES = 32;                  // Keep in sync with shader
    private Vector4[] _dirTmp = new Vector4[MAX_WAVES]; // fixed-size scratch buffers
    private Vector4[] _wlTmp = new Vector4[MAX_WAVES];
    private bool _arraysPrimed = false;                // have we established fixed array sizes on the material?

    private struct WaveCPU
    {
        public float A, k, w, S, phi; // amplitude, wavenumber, angular frequency, steepness (effective), phase offset
        public Vector2 D;             // unit dir (x,z)
    }

    private WaveCPU[] _cpuWaves = Array.Empty<WaveCPU>();
    private static Ocean _instance;

    private Material _appliedMat;                 // track swaps so we can re-push uniforms
    private OceanWaveSettings _lastSettings;      // for Custom mode subscription
    private PresetMode _activePreset;             // where we are now (start of blend)
    private OceanWaveSettings _fromAsset, _toAsset; // blend endpoints
    private float _blendStartTime = -1f;
    private PresetMode _lastSelectedPreset;       // to detect inspector changes

    // ===== Unity lifecycle =====
    void OnEnable()
    {
        _instance = this;

        _appliedMat = oceanMaterial;
        SyncRendererMaterial();
        PrimeArrayLengths();                      // establish fixed-size arrays on the material

        // subscribe to Custom asset changes
        ResubscribeSettings(settings);

        // initialize preset state
        _activePreset = selectedPreset;
        _lastSelectedPreset = selectedPreset;
        _fromAsset = GetAssetForPreset(selectedPreset);
        _toAsset = _fromAsset; // no blend yet

        EnsureMesh();
        RecomputeAndApplyEffectiveWaves(0f);      // build arrays & CPU waves
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

        if (selectedPreset != _lastSelectedPreset)
        {
            BeginBlendToPreset(selectedPreset);
            _lastSelectedPreset = selectedPreset;
        }

        EnsureMesh();
        RecomputeAndApplyEffectiveWaves(CurrentBlendAlpha());
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
    }

    void Update()
    {
        if (!oceanMaterial) return;

        // material hot-swap
        if (_appliedMat != oceanMaterial)
        {
            _appliedMat = oceanMaterial;
            SyncRendererMaterial();
            _arraysPrimed = false;       // new material -> prime again
            PrimeArrayLengths();
            RecomputeAndApplyEffectiveWaves(CurrentBlendAlpha());
        }

        // settings reference swap at runtime (Custom mode edits)
        if (settings != _lastSettings)
        {
            ResubscribeSettings(settings);
            RecomputeAndApplyEffectiveWaves(CurrentBlendAlpha());
        }

        // drive time/origin
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();

        // recompute during blends
        if (IsBlending())
            RecomputeAndApplyEffectiveWaves(CurrentBlendAlpha());
    }

    // ===== Fixed-size array priming to avoid Unity "cap to previous size" warnings =====
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
            // Geometry = 2000; Transparent = 3000
            int target = (a >= 0.999f) ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            if (mr.sharedMaterial.renderQueue != target)
                mr.sharedMaterial.renderQueue = target;
        }
    }

    // ===== Preset blend control =====
    void BeginBlendToPreset(PresetMode target)
    {
        _fromAsset = GetAssetForPreset(_activePreset);
        _toAsset = GetAssetForPreset(target);
        _blendStartTime = GetTimeSeconds();
        _activePreset = target;
    }

    bool IsBlending()
    {
        if (_blendStartTime < 0f) return false;
        if (blendDuration <= 0f) return false;
        return CurrentBlendAlpha() < 1f;
    }

    float CurrentBlendAlpha()
    {
        if (_blendStartTime < 0f || blendDuration <= 0f) return 1f;
        float t = Mathf.Clamp01((GetTimeSeconds() - _blendStartTime) / Mathf.Max(0.0001f, blendDuration));
        return (blendCurve != null) ? Mathf.Clamp01(blendCurve.Evaluate(t)) : t;
    }

    OceanWaveSettings GetAssetForPreset(PresetMode mode)
    {
        switch (mode)
        {
            case PresetMode.Normal: return presetNormal ? presetNormal : settings;
            case PresetMode.Choppy: return presetChoppy ? presetChoppy : settings;
            case PresetMode.Storm: return presetStorm ? presetStorm : settings;
            default: return settings;
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
        RecomputeAndApplyEffectiveWaves(CurrentBlendAlpha());
    }

    // ===== Building & applying wave data =====
    void RecomputeAndApplyEffectiveWaves(float alpha)
    {
        PrimeArrayLengths(); // ensure arrays exist at fixed size on the material

        var A = _fromAsset ? _fromAsset : settings;
        var B = _toAsset ? _toAsset : settings;

        int countA; Vector4[] dirA, wlA; WaveCPU[] cpuA; float chopA;
        int countB; Vector4[] dirB, wlB; WaveCPU[] cpuB; float chopB;

        BakeFromAsset(A, out countA, out dirA, out wlA, out cpuA, out chopA, 0xA1B2C3D ^ randomSeed);
        BakeFromAsset(B, out countB, out dirB, out wlB, out cpuB, out chopB, 0xD3C2B1A ^ randomSeed);

        int count = Mathf.Max(countA, countB);
        count = Mathf.Min(count, MAX_WAVES);

        var dir_amp_steep = new Vector4[count];
        var wl_omega_phi = new Vector4[count];
        var cpu = new WaveCPU[count];

        for (int i = 0; i < count; i++)
        {
            Vector4 da = (i < countA) ? dirA[i] : Vector4.zero; // Dx, Dz, A, S_eff
            Vector4 wa = (i < countA) ? wlA[i] : Vector4.zero; // WL, omega, phi
            Vector4 db = (i < countB) ? dirB[i] : Vector4.zero;
            Vector4 wb = (i < countB) ? wlB[i] : Vector4.zero;

            // Direction blend (lerp then renormalize)
            Vector2 D_a = new Vector2(da.x, da.y);
            Vector2 D_b = new Vector2(db.x, db.y);
            Vector2 D_l = (D_a == Vector2.zero && D_b == Vector2.zero) ? Vector2.right : Vector2.Lerp(D_a, D_b, alpha);
            if (D_l.sqrMagnitude < 1e-6f) D_l = (D_a.sqrMagnitude > 1e-6f ? D_a : Vector2.right);
            D_l.Normalize();

            // Scalars linear; phi needs shortest-angle lerp
            float Aamp = Mathf.Lerp(da.z, db.z, alpha);
            float S = Mathf.Lerp(da.w, db.w, alpha);
            float WL = Mathf.Lerp(wa.x, wb.x, alpha);
            float omg = Mathf.Lerp(wa.y, wb.y, alpha);
            float phiA = wa.z;
            float phiB = wb.z;
            float phiL = LerpAngleRad(phiA, phiB, alpha);

            dir_amp_steep[i] = new Vector4(D_l.x, D_l.y, Aamp, S);
            wl_omega_phi[i] = new Vector4(Mathf.Max(0.001f, WL), omg, phiL, 0f);

            float k = 2f * Mathf.PI / Mathf.Max(0.001f, WL);
            cpu[i] = new WaveCPU { A = Aamp, k = k, w = omg, S = S, D = D_l, phi = phiL };
        }

        // Copy into fixed-size scratch arrays and push to material
        Array.Clear(_dirTmp, 0, MAX_WAVES);
        Array.Clear(_wlTmp, 0, MAX_WAVES);
        if (count > 0)
        {
            Array.Copy(dir_amp_steep, _dirTmp, count);
            Array.Copy(wl_omega_phi, _wlTmp, count);
        }

        oceanMaterial?.SetVectorArray("_DirAmpSteep", _dirTmp);
        oceanMaterial?.SetVectorArray("_WlOmegaPad", _wlTmp); // z = phi
        oceanMaterial?.SetInt("_WaveCount", count);
        oceanMaterial?.SetVector("_OceanOrigin", transform.position);

        // Apply to CPU sampler
        _cpuWaves = cpu;
    }

    // Hash-based deterministic "random" in [0,1)
    static float Rand01(int key)
    {
        unchecked
        {
            uint x = (uint)key;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x & 0xFFFFFF) / 16777216f; // 24-bit mantissa
        }
    }
    static float RandSigned(int key) => Rand01(key) * 2f - 1f;

    float LerpAngleRad(float a, float b, float t)
    {
        float delta = Mathf.Repeat((b - a) + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        return a + delta * t;
    }

    void BakeFromAsset(OceanWaveSettings asset,
        out int count, out Vector4[] dir_amp_steep, out Vector4[] wl_omega_phi, out WaveCPU[] cpu, out float assetChop, int seed)
    {
        assetChop = (asset ? asset.choppiness : 1f);
        var waves = (asset && asset.waves != null) ? asset.waves : Array.Empty<OceanWaveSettings.Wave>();

        // Precompute weights for splitting
        float[] weights = componentsPerWave == 3 ? new float[] { 0.5f, 0.3f, 0.2f }
                          : componentsPerWave == 2 ? new float[] { 0.6f, 0.4f }
                                                   : new float[] { 1f };

        var dirList = new List<Vector4>(Mathf.Min(waves.Length * Mathf.Max(1, componentsPerWave), MAX_WAVES));
        var wlList = new List<Vector4>(dirList.Capacity);
        var cpuList = new List<WaveCPU>(dirList.Capacity);

        // Generate base/naturalized components
        for (int i = 0; i < waves.Length; i++)
        {
            var wv = waves[i];

            // Base direction (unit)
            float theta = wv.directionDegrees * Mathf.Deg2Rad;
            Vector2 Dbase = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;

            for (int s = 0; s < Mathf.Max(1, componentsPerWave); s++)
            {
                if (dirList.Count >= MAX_WAVES) break;

                // Deterministic keys
                int key = seed ^ (i * 73856093) ^ (s * 19349663);

                // Spread angle
                float spreadSign = Mathf.Sign(RandSigned(key + 11));
                float spread = directionSpreadDeg * Mathf.Abs(RandSigned(key + 23)); // 0..dirSpread
                float angle = (spread * spreadSign) * Mathf.Deg2Rad;

                // Rotated direction
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                Vector2 D = new Vector2(Dbase.x * ca - Dbase.y * sa, Dbase.x * sa + Dbase.y * ca).normalized;

                // Jitters
                float ampJ = 1f + amplitudeJitter * RandSigned(key + 101);
                float wlJ = 1f + wavelengthJitter * RandSigned(key + 202);
                float omgJ = 1f + omegaJitter * RandSigned(key + 303);

                float share = weights[Mathf.Min(s, weights.Length - 1)];
                float A = wv.amplitude * share * ampJ;
                float WL = Mathf.Max(0.001f, wv.wavelength * wlJ);

                float k = 2f * Mathf.PI / WL;
                float omg = (wv.speed > 0f) ? k * wv.speed : Mathf.Sqrt(gravity * k);
                omg *= omgJ; // de-sync repetition slightly

                // Effective steepness includes multipliers
                float S_eff = wv.steepness * choppiness * assetChop;

                // Random initial phase 0..2pi
                float phi = Rand01(key + 404) * Mathf.PI * 2f;

                dirList.Add(new Vector4(D.x, D.y, A, S_eff));
                wlList.Add(new Vector4(WL, omg, phi, 0f));
                cpuList.Add(new WaveCPU { A = A, k = k, w = omg, S = S_eff, D = D, phi = phi });
            }
        }

        // Add pairwise interaction components (sum & difference), capped
        if (enableInteractions && dirList.Count < MAX_WAVES && maxInteractionComponents > 0)
        {
            int n = dirList.Count;
            int added = 0;

            // Precompute k-vectors and omegas for existing comps
            Vector2[] kvec = new Vector2[n];
            float[] omg = new float[n];
            float[] Aamp = new float[n];
            float[] phi = new float[n];
            Vector2[] Ddir = new Vector2[n];
            float[] WL = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector4 dir = dirList[i];
                Vector4 wl = wlList[i];
                Vector2 D = new Vector2(dir.x, dir.y);
                float k = 2f * Mathf.PI / Mathf.Max(0.001f, wl.x);
                kvec[i] = D * k;
                omg[i] = wl.y;
                Aamp[i] = dir.z;
                phi[i] = wl.z;
                Ddir[i] = D;
                WL[i] = wl.x;
            }

            for (int i = 0; i < n && added < maxInteractionComponents && dirList.Count < MAX_WAVES; i++)
                for (int j = i + 1; j < n && added < maxInteractionComponents && dirList.Count < MAX_WAVES; j++)
                {
                    // sum
                    TryAddInteraction(kvec[i] + kvec[j], omg[i] + omg[j], Aamp[i], Aamp[j], phi[i] + phi[j],
                                      dirList, wlList, cpuList, ref added);
                    if (added >= maxInteractionComponents || dirList.Count >= MAX_WAVES) break;

                    // difference
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

        // Small amplitude from product
        float A = interactionStrength * 0.5f * (Ai * Aj);
        if (A <= 1e-4f) return;

        // Use a conservative steepness (avoid self-intersection)
        float S_eff = 0.5f; // moderate
        float k = kmag;

        dirList.Add(new Vector4(D.x, D.y, A, S_eff));
        wlList.Add(new Vector4(WL, omega, phi, 0f));
        cpuList.Add(new WaveCPU { A = A, k = k, w = omega, S = S_eff, D = D, phi = phi });
        added++;
    }

    // ===== Push per-frame props =====
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

        normal = Vector3.Normalize(Vector3.Cross(dPdZ, dPdX));                 // up on flat patch
        height = _instance.transform.position.y + disp.y;                       // world height
    }

#if UNITY_EDITOR
    [ContextMenu("Regenerate Ocean Mesh")]
    void RegenerateMeshContext() => EnsureMesh();
#endif
}

static class OceanVecExt
{
    public static Vector2 XZ(this Vector3 v) => new Vector2(v.x, v.z);
}
