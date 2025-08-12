// Ocean.cs — preset blending + live updates + fixed-size shader arrays + material sync + correct mesh/CPU normals
using UnityEngine;
using System;

[ExecuteAlways, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
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

    // ===== Internals =====
    private const int MAX_WAVES = 32;                  // Keep in sync with shader
    private Vector4[] _dirTmp = new Vector4[MAX_WAVES]; // fixed-size scratch buffers
    private Vector4[] _wlTmp = new Vector4[MAX_WAVES];
    private bool _arraysPrimed = false;                // have we established fixed array sizes on the material?

    private struct WaveCPU
    {
        public float A, k, w, S; // amplitude, wavenumber, angular frequency, steepness (effective)
        public Vector2 D;        // unit dir (x,z)
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

        BakeFromAsset(A, out countA, out dirA, out wlA, out cpuA, out chopA);
        BakeFromAsset(B, out countB, out dirB, out wlB, out cpuB, out chopB);

        int count = Mathf.Max(countA, countB);
        count = Mathf.Min(count, MAX_WAVES);

        var dir_amp_steep = new Vector4[count];
        var wl_omega_pad = new Vector4[count];
        var cpu = new WaveCPU[count];

        for (int i = 0; i < count; i++)
        {
            Vector4 da = (i < countA) ? dirA[i] : Vector4.zero; // Dx, Dz, A, S_eff
            Vector4 wa = (i < countA) ? wlA[i] : Vector4.zero; // WL, omega
            Vector4 db = (i < countB) ? dirB[i] : Vector4.zero;
            Vector4 wb = (i < countB) ? wlB[i] : Vector4.zero;

            Vector2 D_a = new Vector2(da.x, da.y);
            Vector2 D_b = new Vector2(db.x, db.y);
            Vector2 D_l = (D_a == Vector2.zero && D_b == Vector2.zero) ? Vector2.right : Vector2.Lerp(D_a, D_b, alpha);
            if (D_l.sqrMagnitude < 1e-6f) D_l = (D_a.sqrMagnitude > 1e-6f ? D_a : Vector2.right);
            D_l.Normalize();

            float Aamp = Mathf.Lerp(da.z, db.z, alpha);
            float S = Mathf.Lerp(da.w, db.w, alpha);
            float WL = Mathf.Lerp(wa.x, wb.x, alpha);
            float omg = Mathf.Lerp(wa.y, wb.y, alpha);

            dir_amp_steep[i] = new Vector4(D_l.x, D_l.y, Aamp, S);
            wl_omega_pad[i] = new Vector4(Mathf.Max(0.001f, WL), omg, 0f, 0f);

            float k = 2f * Mathf.PI / Mathf.Max(0.001f, WL);
            cpu[i] = new WaveCPU { A = Aamp, k = k, w = omg, S = S, D = D_l };
        }

        // Copy into fixed-size scratch arrays and push to material
        Array.Clear(_dirTmp, 0, MAX_WAVES);
        Array.Clear(_wlTmp, 0, MAX_WAVES);
        if (count > 0)
        {
            Array.Copy(dir_amp_steep, _dirTmp, count);
            Array.Copy(wl_omega_pad, _wlTmp, count);
        }

        oceanMaterial?.SetVectorArray("_DirAmpSteep", _dirTmp);
        oceanMaterial?.SetVectorArray("_WlOmegaPad", _wlTmp);
        oceanMaterial?.SetInt("_WaveCount", count);
        oceanMaterial?.SetVector("_OceanOrigin", transform.position);

        // Apply to CPU sampler
        _cpuWaves = cpu;
    }

    void BakeFromAsset(OceanWaveSettings asset, out int count, out Vector4[] dir_amp_steep, out Vector4[] wl_omega_pad, out WaveCPU[] cpu, out float assetChop)
    {
        assetChop = (asset ? asset.choppiness : 1f);
        var waves = (asset && asset.waves != null) ? asset.waves : Array.Empty<OceanWaveSettings.Wave>();

        count = Mathf.Min(waves.Length, MAX_WAVES);
        dir_amp_steep = new Vector4[count];
        wl_omega_pad = new Vector4[count];
        cpu = new WaveCPU[count];

        for (int i = 0; i < count; i++)
        {
            var wv = waves[i];

            float theta = wv.directionDegrees * Mathf.Deg2Rad;
            Vector2 D = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;

            float WL = Mathf.Max(0.001f, wv.wavelength);
            float k = 2f * Mathf.PI / WL;
            float omega = (wv.speed > 0f) ? k * wv.speed : Mathf.Sqrt(gravity * k);

            float S_eff = wv.steepness * choppiness * assetChop;

            dir_amp_steep[i] = new Vector4(D.x, D.y, wv.amplitude, S_eff);
            wl_omega_pad[i] = new Vector4(WL, omega, 0f, 0f);

            cpu[i] = new WaveCPU { A = wv.amplitude, k = k, w = omega, S = S_eff, D = D };
        }
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
            float phase = w.k * dot - w.w * t;
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
