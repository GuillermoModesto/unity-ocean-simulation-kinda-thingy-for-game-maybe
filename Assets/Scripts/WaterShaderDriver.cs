using UnityEngine;

/// <summary>
/// Pushes dynamic parameters to the water material:
/// - _WindDir / _CurrentDir (for optional effects)
/// - _WaveTime
/// - _SeaLevel (from Waves transform.y)
/// - _HeightRange auto-scaled to current wave amplitude (prevents monochrome when seas get big)
/// </summary>
[RequireComponent(typeof(Renderer))]
public class WaterShaderDriver : MonoBehaviour
{
    // Shader property IDs
    static readonly int ID_WindDir = Shader.PropertyToID("_WindDir");
    static readonly int ID_CurrentDir = Shader.PropertyToID("_CurrentDir");
    static readonly int ID_WaveTime = Shader.PropertyToID("_WaveTime");
    static readonly int ID_SeaLevel = Shader.PropertyToID("_SeaLevel");
    static readonly int ID_HeightRange = Shader.PropertyToID("_HeightRange");

    [Tooltip("If left null, will auto-find a Waves in the scene.")]
    public Waves waves;

    [Header("Color Ramp Auto-Range")]
    [Tooltip("If enabled, _HeightRange is adapted to wave amplitude so colors stay varied at any sea state.")]
    public bool autoColorRange = true;

    [Tooltip("Final _HeightRange ≈ amplitude * multiplier (use ~1.0–1.4).")]
    [Min(0.01f)] public float colorRangeMultiplier = 1.1f;

    [Tooltip("Seconds to smoothly catch up to sea changes (0 = instant).")]
    [Min(0f)] public float colorRangeSmoothing = 0.5f;

    Renderer rend;
    MaterialPropertyBlock mpb;
    float heightRangeCurrent = -1f;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        if (!waves) waves = FindFirstObjectByType<Waves>();
    }

    void OnValidate()
    {
        colorRangeMultiplier = Mathf.Max(0.01f, colorRangeMultiplier);
        colorRangeSmoothing = Mathf.Max(0f, colorRangeSmoothing);
    }

    void LateUpdate()
    {
        if (!waves) { waves = FindFirstObjectByType<Waves>(); if (!waves) return; }

        rend.GetPropertyBlock(mpb);

        // Wind/current vectors (normalized dir in xy, strength in w)
        Vector2 wDir = waves.windDirection.sqrMagnitude > 0 ? waves.windDirection.normalized : Vector2.zero;
        Vector2 cDir = waves.currentDirection.sqrMagnitude > 0 ? waves.currentDirection.normalized : Vector2.zero;

        mpb.SetVector(ID_WindDir, new Vector4(wDir.x, wDir.y, 0f, waves.windStrength));
        mpb.SetVector(ID_CurrentDir, new Vector4(cDir.x, cDir.y, 0f, waves.currentStrength));
        mpb.SetFloat(ID_WaveTime, Time.time);

        // Keep shader sea level in sync with actual water object Y
        mpb.SetFloat(ID_SeaLevel, waves.transform.position.y);

        // Auto-scale the color ramp range to current sea amplitude
        if (autoColorRange)
        {
            float amp = EstimateAmplitudeLikeJob(waves);                       // ≈ ± amplitude
            float targetRange = Mathf.Max(0.1f, amp * colorRangeMultiplier);   // safety

            if (heightRangeCurrent < 0f) heightRangeCurrent = targetRange;     // initialize

            if (colorRangeSmoothing <= 0f)
                heightRangeCurrent = targetRange;
            else
                heightRangeCurrent = Mathf.Lerp(
                    heightRangeCurrent, targetRange,
                    Time.deltaTime / Mathf.Max(1e-4f, colorRangeSmoothing));

            mpb.SetFloat(ID_HeightRange, heightRangeCurrent);
        }

        rend.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// Mirrors the amplitude logic used in WavesJob so visuals match physics.
    /// Sums active octave heights with wind/current response.
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

        // total ≈ ±range around sea level
        return Mathf.Max(0.1f, total);
    }
}
