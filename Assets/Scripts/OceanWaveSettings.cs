// OceanWaveSettings.cs
using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Ocean/Gerstner Wave Settings")]
public class OceanWaveSettings : ScriptableObject
{
    [Serializable]
    public struct Wave
    {
        [Range(0f, 10f)] public float amplitude;      // meters
        [Range(0f, 1f)] public float steepness;      // 0..1 (keep combined steepness sane)
        public float wavelength;                      // meters
        public float speed;                           // m/s (0 = auto from wavelength)
        [Range(0f, 360f)] public float directionDegrees; // travel heading (deg)
    }

    [Tooltip("World-space offset for tiling different patches (optional).")]
    public Vector2 uvOffset;

    [Tooltip("Global choppiness multiplier for this profile (optional; your Ocean.cs can also have one).")]
    public float choppiness = 1f;

    [Tooltip("Keep 4–12 waves for performance.")]
    public Wave[] waves;

    // === Live update support ===
    public event Action OnChanged;

    // Fires whenever you edit values in the Inspector (Edit or Play mode)
    private void OnValidate() => OnChanged?.Invoke();

    /// <summary>Call this after runtime changes if you're modifying fields via code.</summary>
    public void NotifyChanged() => OnChanged?.Invoke();
}
