/*
 Summary: Applies directional wind shaping on top of a baseline OceanWaveSettings at runtime without modifying the original asset. Reversible and stable.

 Usage:
   - Place on the same GameObject as Ocean (or assign refs).
   - Set 'windLevel' 0..1 and 'windDirection' to taste; baseline restored when disabled.
   - Tuning cutoffs/gains shapes short chop vs long swell distinctly.
*/

﻿// OceanWindDirectional.cs — Obvious, reversible wind shaping over baseline (no Ocean.cs changes).

using UnityEngine;
using System;

[ExecuteAlways]
[DisallowMultipleComponent]
public class OceanWindDirectional : MonoBehaviour
{
    [Header("Refs")]
    public Ocean ocean;                          // auto-found if null
    public OceanWaveSettings baseSettings;       // if null, uses ocean.settings

    [Header("Wind Controls")]
    [Tooltip("0 = no wind shaping, 1 = max shaping (obvious & reversible).")]
    [Range(0f, 1f)] public float windLevel = 0f;

    [Tooltip("Wind direction in world space (XZ used).")]
    public Vector3 windDirection = new Vector3(1, 0, 0);

    [Header("Look (obvious but stable)")]
    [Tooltip("Short-chop cutoff wavelength (m). Shorter gets more wind effect.")]
    [Min(0.5f)] public float lambdaCutoffShort = 12f;

    [Tooltip("Max rotation of each wave toward wind at windLevel=1 (degrees). Shorter waves rotate more of this.")]
    [Range(0f, 90f)] public float maxDirLockDeg = 35f;

    [Tooltip("Amplitude gain at windLevel=1 for short waves (≤ cutoff). 1 = no gain.")]
    [Min(1f)] public float ampGainShortAt1 = 2.2f;

    [Tooltip("Amplitude gain at windLevel=1 for long waves (> cutoff). 1 = no gain.")]
    [Min(1f)] public float ampGainLongAt1 = 1.15f;

    [Tooltip("Steepness gain at windLevel=1 for short waves.")]
    [Min(1f)] public float steepGainShortAt1 = 1.6f;

    [Tooltip("Steepness gain at windLevel=1 for long waves.")]
    [Min(1f)] public float steepGainLongAt1 = 1.10f;

    [Header("Safety")]
    [Tooltip("Stability margin for S·k·A < margin (keeps Gerstner stable).")]
    [Range(0.2f, 0.98f)] public float stabilityMargin = 0.95f;

    OceanWaveSettings _baseline;   // frozen snapshot of your original look
    OceanWaveSettings _working;    // runtime copy we write into and assign to Ocean
    float _baselineChoppiness = 1f;

    void OnEnable()
    {
        if (!ocean) ocean = GetComponent<Ocean>();
        if (!ocean) { Debug.LogWarning("[OceanWindDirectional] No Ocean found."); enabled = false; return; }

        var src = baseSettings ? baseSettings : ocean.settings;
        if (!src) { Debug.LogWarning("[OceanWindDirectional] No OceanWaveSettings available."); enabled = false; return; }

        _baselineChoppiness = ocean.choppiness;

        _baseline = ScriptableObject.CreateInstance<OceanWaveSettings>();
        DeepCopy(src, _baseline);

        _working = ScriptableObject.CreateInstance<OceanWaveSettings>();
#if UNITY_EDITOR
        _working.name = _baseline.name + " (WindWorking)";
#endif
        DeepCopy(_baseline, _working);
        ocean.settings = _working;

        ApplyShaping();
        NotifyChanged(_working);
    }

    void OnDisable()
    {

        if (ocean && _baseline)
        {
            ocean.settings = _baseline;
            ocean.choppiness = _baselineChoppiness;
            NotifyChanged(_baseline);
        }
#if UNITY_EDITOR

        if (_working && !UnityEditor.AssetDatabase.Contains(_working))
            DestroyImmediate(_working);
#endif
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled || ocean == null || _baseline == null || _working == null) return;
        ApplyShaping();
        NotifyChanged(_working);
    }

    void Update()
    {
        if (!ocean || _baseline == null || _working == null) return;
        ApplyShaping();
        NotifyChanged(_working);
    }

    void ApplyShaping()
    {
        float f = Mathf.Clamp01(windLevel);

        if (f <= 0.0001f)
        {
            CopyInto(_baseline, _working);      // byte-for-byte match (waves + choppiness)
            ocean.choppiness = _baselineChoppiness;
            return;
        }

        Vector2 W = new Vector2(windDirection.x, windDirection.z);
        if (W.sqrMagnitude < 1e-9f) W = Vector2.right;
        W.Normalize();
        float windDeg = Mathf.Atan2(W.y, W.x) * Mathf.Rad2Deg;

        var src = _baseline.waves ?? Array.Empty<OceanWaveSettings.Wave>();
        if (_working.waves == null || _working.waves.Length != src.Length)
            _working.waves = new OceanWaveSettings.Wave[src.Length];

        for (int i = 0; i < src.Length; i++)
        {
            var b = src[i];
            var w = b; // start from baseline

            float lambda = Mathf.Max(0.001f, b.wavelength);
            float shortW = Mathf.Clamp01((lambdaCutoffShort - lambda) / Mathf.Max(1e-3f, lambdaCutoffShort));
            float longW = 1f - shortW;

            float bias = 0.3f + 0.7f * shortW; // short waves turn more
            float delta = Mathf.DeltaAngle(b.directionDegrees, windDeg);
            float limit = maxDirLockDeg * f * bias;
            float clamped = Mathf.Clamp(delta, -limit, limit);
            w.directionDegrees = b.directionDegrees + clamped;

            float ampGain = shortW * (ampGainShortAt1 - 1f) + longW * (ampGainLongAt1 - 1f);
            w.amplitude = b.amplitude * (1f + f * ampGain);

            float steepGain = shortW * (steepGainShortAt1 - 1f) + longW * (steepGainLongAt1 - 1f);
            float Sgoal = b.steepness * (1f + f * steepGain);

            float k = 2f * Mathf.PI / lambda;
            float Smax = stabilityMargin / Mathf.Max(1e-6f, k * Mathf.Max(1e-6f, w.amplitude));
            w.steepness = Mathf.Min(Sgoal, Smax);

            _working.waves[i] = w;
        }

        ocean.choppiness = _baselineChoppiness;

        _working.choppiness = _baseline.choppiness;
    }

    static void NotifyChanged(OceanWaveSettings s)
    {
        if (!s) return;
        try
        {
            var mi = typeof(OceanWaveSettings).GetMethod("NotifyChanged",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mi != null) mi.Invoke(s, null);
        }
        catch { }
    }

    static void DeepCopy(OceanWaveSettings src, OceanWaveSettings dst)
    {
        if (!src || !dst) return;
        dst.choppiness = src.choppiness;
        if (src.waves == null) { dst.waves = Array.Empty<OceanWaveSettings.Wave>(); return; }
        dst.waves = new OceanWaveSettings.Wave[src.waves.Length];
        Array.Copy(src.waves, dst.waves, src.waves.Length);
        NotifyChanged(dst);
    }

    static void CopyInto(OceanWaveSettings src, OceanWaveSettings dst)
    {
        if (!src || !dst) return;
        dst.choppiness = src.choppiness;
        if (dst.waves == null || dst.waves.Length != (src.waves?.Length ?? 0))
            dst.waves = new OceanWaveSettings.Wave[src.waves?.Length ?? 0];
        if (src.waves != null && src.waves.Length > 0)
            Array.Copy(src.waves, dst.waves, src.waves.Length);
    }
}
