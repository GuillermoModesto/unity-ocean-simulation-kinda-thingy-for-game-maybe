// Ocean.cs — single-patch endless ocean with long-swell storminess shaping.
// Mesh: one boat/camera-centered grid (no LOD rings), rebuilt when the snapped center moves.
// NOW: innerResolution is auto-calculated from innerSize to keep cell size ~ constant.

using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class Ocean : MonoBehaviour
{
    [Header("Wave Settings")]
    public OceanWaveSettings settings;

    [Header("Material / Physics")]
    public Material oceanMaterial;
    [Tooltip("Gravity used for deep-water dispersion (omega = sqrt(g*k)) when wave speed is 0.")]
    public float gravity = 9.81f;

    // ───────────────────────────────────────────────────────────────────────────
    // SINGLE PATCH MESH (boat/camera-centered)
    // ───────────────────────────────────────────────────────────────────────────

    // ===== Patch performance presets (presets change innerSize; resolution is derived) =====
    public enum PatchPreset { VeryLowEnd, LowEnd, MidRange, HighEnd, Custom }

    [Header("Patch Preset")]
    [Tooltip("Preset sets innerSize only. innerResolution is auto from targetCellSize. Editing innerSize switches to Custom.")]
    public PatchPreset patchPreset = PatchPreset.MidRange;

    PatchPreset _lastPreset = (PatchPreset)(-1);
    float _lastUserInnerSize;

    [Header("Endless Patch")]
    [Tooltip("Meters across the single high-resolution square.")]
    [Min(1f)] public float innerSize = 650f;

    [Tooltip("Quads per side (auto). Derived from innerSize/targetCellSize to keep cell size consistent.")]
    [Min(1)] public int innerResolution = 100; // auto-managed; shown for reference

    [Header("Patch Cells")]
    [Tooltip("Desired meters per quad (kept approximately constant by deriving innerResolution).")]
    [Min(0.5f)] public float targetCellSize = 6f;

    [Tooltip("Clamp for auto-calculated resolution.")]
    [Range(8, 2048)] public int minResolution = 64;
    [Range(8, 4096)] public int maxResolution = 256;

    [Tooltip("If true, rebuild the mesh when the snapped center moves.")]
    public bool rebuildAtRuntime = true;

    [Tooltip("Min world movement (meters) before recentering/rebuilding. Usually >= cell size.")]
    [Min(0.01f)] public float rebuildDistanceThreshold = 3f;

    [Tooltip("If set, the patch centers on this transform (e.g., your boat). Otherwise camera/main.")]
    public Transform lodFollowTarget;

    [Tooltip("Fallback camera if lodFollowTarget is not assigned.")]
    public Camera followCamera;

    [Header("Choppiness")]
    [Tooltip("Global choppiness multiplier (scales steepness for both GPU & CPU).")]
    public float choppiness = 1f;

    // ---- Storminess ----------------------------------------------------
    [Header("Storminess (0 = calm / 1 = storm)")]
    [Range(0f, 1f)]
    [Tooltip("Scales amplitudes (wave height) and steepness with stability enforcement (S·k·A < 1).")]
    public float storminess = 0f;

    [Tooltip("Amplitude multiplier when Storminess = 1. Larger = much taller waves at full storm.")]
    [Min(1f)] public float stormAmplitudeAt1 = 12f;

    [Tooltip("Steepness multiplier when Storminess = 1. Use <1 to make storm waves less sharp.")]
    [Range(0.2f, 2f)] public float stormSteepnessAt1 = 0.6f;

    [Header("Storminess shaping")]
    [Tooltip("Extra boost for long wavelengths at storms (1 = off).")]
    [Min(1f)] public float swellWaveBoostAt1 = 2.0f;

    [Tooltip("How strongly the boost prefers long waves (0.3 subtle ←→ 1.0 aggressive).")]
    [Range(0.2f, 1.2f)] public float swellWaveBoostPower = 0.7f;
    // -------------------------------------------------------------------

    [Header("Natural Variation")]
    [Tooltip("Split each wave into subcomponents with slight dir/freq/phase differences.")]
    public bool naturalize = true;
    [Range(1, 3)] public int componentsPerWave = 2;
    [Range(0f, 45f)] public float directionSpreadDeg = 18f;
    [Range(0f, 0.5f)] public float amplitudeJitter = 0.18f;
    [Range(0f, 0.3f)] public float wavelengthJitter = 0.10f;
    [Range(0f, 0.1f)] public float omegaJitter = 0.03f;
    [Tooltip("Seed for deterministic randomization. Changing it reshuffles the sea pattern.")]
    public int randomSeed = 1337;

    [Header("Wave Interactions")]
    [Tooltip("Add bound-harmonic sum/difference components from pairs of waves to create interference patterns.")]
    public bool enableInteractions = true;
    [Range(0, 16)] public int maxInteractionComponents = 8;
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
        public float A, k, w, S, phi;
        public Vector2 D;
    }

    private WaveCPU[] _cpuWaves = Array.Empty<WaveCPU>();
    private static Ocean _instance;

    private Material _appliedMat;
    private OceanWaveSettings _lastSettings;
    private float _lastStorminess = -1f;

    // mesh buffers/state
    Vector3[] _verts;
    Vector2[] _uvs;
    int[] _tris;
    Vector3 _lastCenter;   // snapped center (world)
    float _lastInnerSize;
    int _lastInnerRes;

    // ===== Unity lifecycle =====
    void OnEnable()
    {
        _instance = this;

        _appliedMat = oceanMaterial;
        SyncRendererMaterial();
        PrimeArrayLengths();

        ResubscribeSettings(settings);

        ApplyPresetIfChanged(force: true);
        RecomputeResolutionFromSize();   // keep cell size constant
        EnsureMesh();
        RegeneratePatch();               // build once
        RecomputeAndApplyEffectiveWaves();
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
        _lastStorminess = storminess;
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

        ApplyPresetIfChanged();
        TrackUserOverridesToCustom();
        RecomputeResolutionFromSize();   // recalc res after any size/preset edit

        EnsureMesh();
        RegeneratePatch();
        RecomputeAndApplyEffectiveWaves();
        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
        _lastStorminess = storminess;
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

        if (!Mathf.Approximately(_lastStorminess, storminess))
        {
            _lastStorminess = storminess;
            RecomputeAndApplyEffectiveWaves();
        }

        UpdateMaterialProps(GetTimeSeconds());
        AdjustRenderQueueForTransparency();
    }

    void LateUpdate()
    {
        if (rebuildAtRuntime && ShouldRebuildPatch())
            RegeneratePatch();
    }

    // ===== Fixed-size array priming =====
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

    // ===== Settings live updates =====
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

        count = Mathf.Min(count, MAX_WAVES);

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

        _cpuWaves = cpu;
    }

    // ===== Baking (naturalization + interactions) =====
    static float Rand01(int key)
    {
        unchecked { uint x = (uint)key; x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (x & 0xFFFFFF) / 16777216f; }
    }
    static float RandSigned(int key) => Rand01(key) * 2f - 1f;

    void BakeFromAsset(
        OceanWaveSettings asset,
        out int count,
        out Vector4[] dir_amp_steep,
        out Vector4[] wl_omega_phi,
        out WaveCPU[] cpu,
        out float assetChop,
        int seed)
    {
        assetChop = (asset ? asset.choppiness : 1f);
        var waves = (asset && asset.waves != null) ? asset.waves : Array.Empty<OceanWaveSettings.Wave>();

        int splits = naturalize ? Mathf.Clamp(componentsPerWave, 1, 3) : 1;
        float[] weights = splits == 3 ? new float[] { 0.5f, 0.3f, 0.2f }
                        : splits == 2 ? new float[] { 0.6f, 0.4f }
                                      : new float[] { 1f };

        float dirSpread = naturalize ? directionSpreadDeg : 0f;
        float ampJit = naturalize ? amplitudeJitter : 0f;
        float wlJit = naturalize ? wavelengthJitter : 0f;
        float omgJit = naturalize ? omegaJitter : 0f;

        float minWL = float.MaxValue, maxWL = 0f;
        if (asset && asset.waves != null)
        {
            for (int i = 0; i < asset.waves.Length; i++)
            {
                float wl = Mathf.Max(0.001f, asset.waves[i].wavelength);
                if (wl < minWL) minWL = wl;
                if (wl > maxWL) maxWL = wl;
            }
        }
        if (minWL > maxWL) { minWL = 1f; maxWL = 1f; }

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

                float share = weights[Mathf.Min(s, weights.Length - 1)];
                float ampMulJ = 1f + ampJit * RandSigned(key + 101);
                float wlMul = 1f + wlJit * RandSigned(key + 202);
                float omgMul = 1f + omgJit * RandSigned(key + 303);

                float A = wv.amplitude * share * ampMulJ;
                float WL = Mathf.Max(0.001f, wv.wavelength * wlMul);
                float k = 2f * Mathf.PI / WL;

                float ampMulMax = Mathf.Max(1f, stormAmplitudeAt1);
                float ampMul = Mathf.Lerp(1f, ampMulMax, Mathf.Clamp01(storminess));

                float long01 = (maxWL > minWL) ? Mathf.InverseLerp(minWL, maxWL, WL) : 0f;
                float small01 = 1f - long01;

                float longBoost = Mathf.Lerp(
                    1f,
                    swellWaveBoostAt1,
                    Mathf.Pow(Mathf.Clamp01(storminess), 1f) * Mathf.Pow(long01, swellWaveBoostPower)
                );

                A *= (ampMul * longBoost);

                float omega = (wv.speed > 0f) ? (k * wv.speed) : Mathf.Sqrt(Mathf.Max(0.0f, gravity) * k);
                omega *= omgMul;

                float S_eff = wv.steepness * choppiness * assetChop;
                float steepMul = Mathf.Lerp(1f, Mathf.Max(0.01f, stormSteepnessAt1), Mathf.Clamp01(storminess));
                float steepDampShort = Mathf.Lerp(1f, 0.7f, small01 * Mathf.Clamp01(storminess));
                S_eff *= (steepMul * steepDampShort);

                float limit = 0.95f / Mathf.Max(1e-6f, k * Mathf.Max(1e-6f, A));
                if (S_eff > limit) S_eff = limit;

                float phi = Rand01(key + 404) * Mathf.PI * 2f;

                dirList.Add(new Vector4(D.x, D.y, A, S_eff));
                wlList.Add(new Vector4(WL, omega, phi, 0f));
                cpuList.Add(new WaveCPU { A = A, k = k, w = omega, S = S_eff, D = D, phi = phi });
            }
        }

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
            {
                for (int j = i + 1; j < n && added < maxInteractionComponents && dirList.Count < MAX_WAVES; j++)
                {
                    TryAddInteraction(kvec[i] + kvec[j], omg[i] + omg[j], Aamp[i], Aamp[j], phi[i] + phi[j],
                                      dirList, wlList, cpuList, ref added);
                    if (added >= maxInteractionComponents || dirList.Count >= MAX_WAVES) break;

                    TryAddInteraction(kvec[i] - kvec[j], Mathf.Abs(omg[i] - omg[j]), Aamp[i], Aamp[j], phi[i] - phi[j],
                                      dirList, wlList, cpuList, ref added);
                }
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

        float S_eff = 0.5f;
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

    // ───────────────────────────────────────────────────────────────────────────
    // Single patch mesh generation (no transform motion)
    // ───────────────────────────────────────────────────────────────────────────
    void EnsureMesh()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null)
        {
            var mesh = new Mesh { name = "Ocean_PatchMesh", indexFormat = IndexFormat.UInt32 };
            mf.sharedMesh = mesh;
        }
    }

    bool ShouldRebuildPatch()
    {
        Vector3 center = GetSnappedCenter();
        float moved = Vector3.Distance(center, _lastCenter);
        bool movedEnough = moved >= Mathf.Max(GetInnerCellSize(), rebuildDistanceThreshold);
        bool paramsChanged = (innerResolution != _lastInnerRes) || !Mathf.Approximately(innerSize, _lastInnerSize);
        return movedEnough || paramsChanged;
    }

    void RegeneratePatch()
    {
        var mf = GetComponent<MeshFilter>();
        var mesh = mf.sharedMesh;
        if (!mesh) return;

        // Make sure resolution matches size at runtime changes too
        RecomputeResolutionFromSize();

        Vector3 centerWorld = GetSnappedCenter();
        Vector3 origin = transform.position;

        int H = Mathf.Max(1, innerResolution);
        float step = innerSize / H;
        float half = innerSize * 0.5f;

        int vertsCount = (H + 1) * (H + 1);
        int trisCount = H * H * 6;

        if (_verts == null || _verts.Length != vertsCount) _verts = new Vector3[vertsCount];
        if (_uvs == null || _uvs.Length != vertsCount) _uvs = new Vector2[vertsCount];
        if (_tris == null || _tris.Length != trisCount) _tris = new int[trisCount];

        int v = 0;
        for (int j = 0; j <= H; j++)
        {
            float zw = (centerWorld.z - half) + j * step;  // world Z
            for (int i = 0; i <= H; i++)
            {
                float xw = (centerWorld.x - half) + i * step; // world X

                _verts[v] = new Vector3(xw - origin.x, 0f, zw - origin.z);

                // stable UVs relative to current center (0..1 across the patch)
                _uvs[v] = new Vector2(
                    (xw - centerWorld.x) / innerSize + 0.5f,
                    (zw - centerWorld.z) / innerSize + 0.5f
                );
                v++;
            }
        }

        int t = 0;
        int stride = H + 1;
        for (int j = 0; j < H; j++)
        {
            for (int i = 0; i < H; i++)
            {
                int a = j * stride + i;
                int b = a + 1;
                int c = a + stride;
                int d = c + 1;

                _tris[t++] = a; _tris[t++] = d; _tris[t++] = b;
                _tris[t++] = a; _tris[t++] = c; _tris[t++] = d;
            }
        }

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(_verts);
        mesh.SetUVs(0, _uvs);
        mesh.SetTriangles(_tris, 0);

        // huge bounds so GPU displacement isn't culled
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        _lastCenter = centerWorld;
        _lastInnerSize = innerSize;
        _lastInnerRes = innerResolution;
    }

    // Derive resolution so cell size stays ~ targetCellSize.
    void RecomputeResolutionFromSize()
    {
        // Ideal quads per side
        float ideal = Mathf.Max(1f, innerSize / Mathf.Max(0.0001f, targetCellSize));
        int res = Mathf.RoundToInt(ideal);

        // Snap to even so (res+1) is odd — symmetric center
        if ((res & 1) == 1) res++;

        innerResolution = Mathf.Clamp(res, minResolution, maxResolution);
    }

    float GetInnerCellSize()
    {
        int H = Mathf.Max(1, innerResolution);
        return innerSize / H;
    }

    Vector3 GetSnappedCenter()
    {
        Vector3 srcPos;
        if (lodFollowTarget) srcPos = lodFollowTarget.position;
        else if (followCamera) srcPos = followCamera.transform.position;
        else if (Camera.main) srcPos = Camera.main.transform.position;
        else srcPos = transform.position;

        float cell = GetInnerCellSize();
        float x = Mathf.Round(srcPos.x / cell) * cell;
        float z = Mathf.Round(srcPos.z / cell) * cell;
        return new Vector3(x, transform.position.y, z);
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

    // ===== Presets (set size only; res is derived) =====
    void ApplyPresetIfChanged(bool force = false)
    {
        if (!force && patchPreset == _lastPreset) return;

        switch (patchPreset)
        {
            case PatchPreset.VeryLowEnd: innerSize = 320f; break; // mobile / very low-end
            case PatchPreset.LowEnd: innerSize = 480f; break; // low-end PC baseline
            case PatchPreset.MidRange: innerSize = 650f; break; // recommended default
            case PatchPreset.HighEnd: innerSize = 900f; break; // cinematic/high-end
            case PatchPreset.Custom: /* keep current */ break;
        }

        _lastPreset = patchPreset;
        _lastUserInnerSize = innerSize;
    }

    void TrackUserOverridesToCustom()
    {
        if (patchPreset == PatchPreset.Custom) { _lastUserInnerSize = innerSize; return; }

        if (!Mathf.Approximately(innerSize, _lastUserInnerSize))
        {
            patchPreset = PatchPreset.Custom;
            _lastPreset = PatchPreset.Custom;
        }
        else
        {
            _lastUserInnerSize = innerSize;
        }
    }

    // ===== Public CPU sampling =====
    public static void Sample(Vector2 worldXZ, out float height, out Vector3 normal, float t = -1f, int iterations = 4)
    {
        height = 0f; normal = Vector3.up;
        if (_instance == null || _instance._cpuWaves == null) return;
        if (t < 0f) t = _instance.GetTimeSeconds();

        Vector2 xzTarget = worldXZ - _instance.transform.position.XZ();

        // Inverse horiz displacement: solve x0 so x0 + dispH(x0) == xzTarget
        Vector2 x0 = xzTarget;
        for (int it = 0; it < Mathf.Max(0, iterations); it++)
        {
            Vector2 dispH = Vector2.zero;
            var waves = _instance._cpuWaves;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                float dot = w.D.x * x0.x + w.D.y * x0.y;
                float phase = w.k * dot - w.w * t + w.phi;
                float c = Mathf.Cos(phase);
                float QA = w.S * w.A;
                dispH.x += QA * w.D.x * c;
                dispH.y += QA * w.D.y * c;
            }
            Vector2 xNew = xzTarget - dispH;
            if ((xNew - x0).sqrMagnitude < 1e-7f) { x0 = xNew; break; }
            x0 = xNew;
        }

        // Evaluate displacement + derivatives at x0
        Vector3 disp = Vector3.zero;
        Vector3 dPdX = new Vector3(1f, 0f, 0f);
        Vector3 dPdZ = new Vector3(0f, 0f, 1f);

        {
            var waves = _instance._cpuWaves;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                float dot = w.D.x * x0.x + w.D.y * x0.y;
                float phase = w.k * dot - w.w * t + w.phi;
                float c = Mathf.Cos(phase), s = Mathf.Sin(phase);
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
        }

        normal = Vector3.Normalize(Vector3.Cross(dPdZ, dPdX));
        height = _instance.transform.position.y + disp.y;
    }
}

static class OceanVecExt
{
    public static Vector2 XZ(this Vector3 v) => new Vector2(v.x, v.z);
}
