// OceanWaveSettings.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Ocean/Gerstner Wave Settings")]
public class OceanWaveSettings : ScriptableObject
{
    [System.Serializable]
    public struct Wave
    {
        [Range(0f, 10f)] public float amplitude;  // meters
        [Range(0f, 1f)] public float steepness;  // 0..1 (keep <= 0.9/Nwaves)
        public float wavelength;                  // meters
        public float speed;                       // m/s phase speed override (0 = derive from wavelength)
        [Range(0f, 360f)] public float directionDegrees; // heading the wave travels towards
    }

    [Tooltip("World-space offset for tiling different patches.")]
    public Vector2 uvOffset;

    [Tooltip("Global choppiness multiplier (scales steepness).")]
    public float choppiness = 1f;

    [Tooltip("Wind-aligned wave set. Keep 4-12 waves for performance.")]
    public Wave[] waves;
}
