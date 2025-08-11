using UnityEngine;

/// <summary>
/// One-slider ocean control. Direction of travel is 100% driven by wind/current (env),
/// so this script never edits octave.direction.
/// </summary>
[ExecuteAlways]
public class SeaStateController : MonoBehaviour
{
    [Range(0f, 1f)] public float seaState = 0f; // 0 = calm, 1 = storm

    [Header("Environment (angles in degrees)")]
    public float windAngleDeg = 0f;
    public float currentAngleDeg = 0f;

    [Header("Strength ranges at seaState = 1")]
    public float maxWindStrength = 8f;
    public float maxCurrentStrength = 3f;

    [Header("What SeaState affects")]
    public bool affectAmplitude = true;
    public bool affectChoppiness = true;
    public bool affectWavelength = true;
    public bool affectMoveSpeed = false; // IMPORTANT: off by default to avoid “direction feel” changes

    [Tooltip("Global scalar for octave moveSpeed when affectMoveSpeed = true")]
    public float maxSpeedMul = 2.0f;

    [Tooltip("If true, apply every Update(). Turn off if you drive it from code.")]
    public bool liveUpdate = true;

    public Waves waves;

    // Baseline snapshot
    private Waves.Octave[] baseline;

    void OnEnable()
    {
        if (!waves) waves = GetComponent<Waves>();
        CaptureBaseline();
        Apply();
    }

    void OnValidate()
    {
        if (!waves) waves = GetComponent<Waves>();
        if (baseline == null || baseline.Length == 0) CaptureBaseline();
        Apply();
    }

    void Update()
    {
        if (liveUpdate) Apply();
    }

    public void SetSeaState(float t)
    {
        seaState = Mathf.Clamp01(t);
        Apply();
    }

    void CaptureBaseline()
    {
        if (!waves || waves.octaves == null) return;
        var src = waves.octaves;
        baseline = new Waves.Octave[src.Length];
        for (int i = 0; i < src.Length; i++) baseline[i] = src[i];
    }

    void Apply()
    {
        if (!waves || baseline == null || baseline.Length == 0) return;

        float t = Mathf.Clamp01(seaState);

        // Environment direction & strengths (these DO define travel direction in WavesJob)
        waves.windDirection = AngleToDir(windAngleDeg);
        waves.currentDirection = AngleToDir(currentAngleDeg);
        waves.windStrength = Mathf.Lerp(0f, maxWindStrength, EaseExp(t, 1.3f));
        waves.currentStrength = Mathf.Lerp(0f, maxCurrentStrength, EaseExp(t, 1.0f));

        // Per-octave shaping (no writes to o.direction!)
        var dst = waves.octaves;
        int n = baseline.Length;

        for (int i = 0; i < n; i++)
        {
            float octave01 = (n <= 1) ? 0f : i / (float)(n - 1);
            var o = baseline[i];

            if (affectAmplitude)
            {
                float ampWeight = Mathf.Lerp(0.6f, 1.2f, octave01); // more growth on higher bands
                float ampMul = Mathf.Lerp(0.08f, 1.5f, EaseExp(t, 1.25f) * ampWeight);
                o.height = baseline[i].height * ampMul;
            }

            if (affectChoppiness)
            {
                float basePerlin = baseline[i].perlinBlend;
                float perlinTarget = Mathf.Lerp(basePerlin * 0.6f, 1f,
                    Mathf.Clamp01(t * (0.5f + 0.7f * octave01)));
                o.perlinBlend = Mathf.Clamp01(perlinTarget);
            }

            if (affectMoveSpeed)
            {
                float spMul = Mathf.Lerp(0.6f, maxSpeedMul, EaseExp(t, 1.0f));
                o.moveSpeed = Mathf.Max(0f, baseline[i].moveSpeed) * spMul;
            }

            if (affectWavelength)
            {
                // Longer swell, slightly shorter detail as sea grows
                float swellBias = 1f - octave01; // early octaves
                float detailBias = octave01;      // later octaves
                float swellScaleMul = Mathf.Lerp(1.0f, 0.4f, t * swellBias);
                float detailScaleMul = Mathf.Lerp(1.0f, 1.5f, t * detailBias);
                float xMul = Mathf.Lerp(swellScaleMul, detailScaleMul, octave01);

                o.scale = new Vector2(baseline[i].scale.x * xMul,
                                      baseline[i].scale.y * xMul);

                o.scaleFrequencyBoost = Mathf.Max(0.01f,
                    baseline[i].scaleFrequencyBoost + 0.02f * t * detailBias);
            }

            // DO NOT TOUCH o.direction — WavesJob computes direction from wind/current.

            dst[i] = o;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(waves);
#endif
    }

    static Vector2 AngleToDir(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r)).normalized;
    }

    static float EaseExp(float x, float p)
    {
        x = Mathf.Clamp01(x);
        return Mathf.Pow(x, p);
    }
}

