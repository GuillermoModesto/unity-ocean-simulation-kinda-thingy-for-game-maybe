using UnityEngine;

/// <summary>
/// WaterShaderDriver
/// Pushes dynamic values into the water material used by "Custom/Water_SimpleZWrite".
/// - Wind & current vectors (dir.xy normalized, w=strength)
/// - Time (_WaveTime) and optional _UseExternalTime flag
/// - Sea level (renderer transform.y)
/// - Adaptive color ramp range (_HeightRange) that tracks wave amplitude
/// - Binds wake map/UV so the material actually reads the wake RenderTexture
///
/// Drop this on the water mesh (same Renderer that uses the water shader).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class WaterShaderDriver : MonoBehaviour
{
    // Shader property IDs
    static readonly int ID_WindDir = Shader.PropertyToID("_WindDir");
    static readonly int ID_CurrentDir = Shader.PropertyToID("_CurrentDir");
    static readonly int ID_WaveTime = Shader.PropertyToID("_WaveTime");
    static readonly int ID_UseExternal = Shader.PropertyToID("_UseExternalTime");
    static readonly int ID_SeaLevel = Shader.PropertyToID("_SeaLevel");
    static readonly int ID_HeightRange = Shader.PropertyToID("_HeightRange");
    static readonly int ID_WakeMap = Shader.PropertyToID("_WakeMap");
    static readonly int ID_WakeUV = Shader.PropertyToID("_WakeUV");

    [Tooltip("If left null, will auto-find a Waves component in the scene.")]
    public Waves waves;

    [Header("Time")]
    [Tooltip("When true, the shader will use _WaveTime instead of Unity's built-in _Time.")]
    public bool useExternalTime = true;

    [Header("Color Ramp Auto-Range")]
    [Tooltip("If enabled, _HeightRange is adapted to wave amplitude so colors stay varied at any sea state.")]
    public bool autoColorRange = true;
    [Tooltip("Final _HeightRange ≈ amplitude * multiplier (use ~1.0–1.4).")]
    [Min(0.01f)] public float colorRangeMultiplier = 1.1f;
    [Tooltip("Seconds to smoothly catch up to sea changes (0 = instant).")]
    [Min(0f)] public float colorRangeSmoothing = 0.5f;

    [Header("Wake Binding")]
    [Tooltip("If assigned, uses this WakePainter's RT and mapping. Otherwise reads the global _WakeMap/_WakeUV set by a WakePainter.")]
    public WakePainter wakeSource;
    [Tooltip("Optional explicit wake texture override (takes precedence over wakeSource and globals).")]
    public Texture explicitWakeMap;

    Renderer _renderer;
    MaterialPropertyBlock _mpb;
    float _heightRangeCurrent = -1f;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        if (!waves) waves = FindFirstObjectByType<Waves>();
    }

    void OnEnable()
    {
        // Prime MPB so static params are present even in edit mode.
        Apply(now: 0f);
    }

    void OnValidate()
    {
        colorRangeMultiplier = Mathf.Max(0.01f, colorRangeMultiplier);
        colorRangeSmoothing = Mathf.Max(0f, colorRangeSmoothing);
    }

    void LateUpdate()
    {
        Apply(Time.time);
    }

    void Apply(float now)
    {
        if (!_renderer) _renderer = GetComponent<Renderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        if (!waves) waves = FindFirstObjectByType<Waves>();

        _renderer.GetPropertyBlock(_mpb);

        // Wind/current vectors (normalized dir in xy, strength in w)
        Vector2 wDir = Vector2.zero;
        Vector2 cDir = Vector2.zero;
        float wStr = 0f, cStr = 0f;

        if (waves)
        {
            if (waves.windDirection.sqrMagnitude > 0f) wDir = waves.windDirection.normalized;
            if (waves.currentDirection.sqrMagnitude > 0f) cDir = waves.currentDirection.normalized;
            wStr = Mathf.Max(0f, waves.windStrength);
            cStr = Mathf.Max(0f, waves.currentStrength);
        }

        _mpb.SetVector(ID_WindDir, new Vector4(wDir.x, wDir.y, 0f, wStr));
        _mpb.SetVector(ID_CurrentDir, new Vector4(cDir.x, cDir.y, 0f, cStr));

        // Time control
        _mpb.SetFloat(ID_WaveTime, now);
        _mpb.SetFloat(ID_UseExternal, useExternalTime ? 1f : 0f);

        // Sea level = water object's Y
        if (waves)
            _mpb.SetFloat(ID_SeaLevel, waves.transform.position.y);
        else
            _mpb.SetFloat(ID_SeaLevel, transform.position.y);

        // Auto-scale the color ramp range to current sea amplitude
        if (autoColorRange && waves)
        {
            float amp = EstimateAmplitudeLikeJob(waves);                     // ≈ ± amplitude
            float targetRange = Mathf.Max(0.1f, amp * colorRangeMultiplier); // safety

            if (_heightRangeCurrent < 0f) _heightRangeCurrent = targetRange; // initialize

            if (colorRangeSmoothing <= 0f)
                _heightRangeCurrent = targetRange;
            else
                _heightRangeCurrent = Mathf.Lerp(
                    _heightRangeCurrent,
                    targetRange,
                    Time.deltaTime / Mathf.Max(1e-4f, colorRangeSmoothing));

            _mpb.SetFloat(ID_HeightRange, _heightRangeCurrent);
        }

        // Bind wake map & mapping (either explicit, from WakePainter, or fall back to global)
        Texture wakeTex = explicitWakeMap;
        Vector4 wakeUV = new Vector4(0.02f, 0f, 0f, 0f); // sensible defaults

        if (!wakeTex && wakeSource != null)
        {
            wakeTex = wakeSource.GetWakeRT();
            wakeUV = new Vector4(wakeSource.uvScale, wakeSource.uvOffset.x, wakeSource.uvOffset.y, 0f);
        }

        if (!wakeTex)
        {
            // Pull from globals if someone (WakePainter) set them.
            var globalWake = Shader.GetGlobalTexture(ID_WakeMap);
            if (globalWake) wakeTex = globalWake;
            wakeUV = Shader.GetGlobalVector(ID_WakeUV);
        }

        if (wakeTex)
        {
            _mpb.SetTexture(ID_WakeMap, wakeTex);
            _mpb.SetVector(ID_WakeUV, wakeUV);
        }

        _renderer.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Mirrors the amplitude logic used in WavesJob so visuals match physics.
    /// Sums active octave heights with wind/current response and returns an
    /// approximate ± amplitude around sea level (half-range).
    /// </summary>
    static float EstimateAmplitudeLikeJob(Waves w)
    {
        if (!w || w.octaves == null || w.octaves.Length == 0) return 1f;

        float total = 0f;
        for (int i = 0; i < w.octaves.Length; i++)
        {
            var oc = w.octaves[i];
            if (!oc.active) continue;

            // Same environmental influence pattern as WavesJob
            float ampFromEnv = 1f;
            if (i == 0) // first octave gets bigger current effect
                ampFromEnv += w.currentStrength * oc.currentResponse * 1.2f;
            else
                ampFromEnv += (w.windStrength * oc.windResponse + w.currentStrength * oc.currentResponse) * 0.3f;

            total += Mathf.Abs(oc.height * ampFromEnv);
        }

        // total ≈ ±range around sea level (half-range)
        return Mathf.Max(0.1f, total);
    }
}
