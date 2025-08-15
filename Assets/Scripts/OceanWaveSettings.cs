/*
 Summary: ScriptableObject asset that defines the base set of Gerstner waves (amplitude, steepness, wavelength, speed, direction) and global choppiness.

 Usage:
   - Create via Assets ▶ Create ▶ Ocean ▶ Gerstner Wave Settings.
   - Populate 'waves' with 4–12 components for performance and variety.
   - Changing values at runtime raises OnChanged for hot-reload in Ocean.
*/

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

    [Tooltip("Keep 412 waves for performance.")]
    public Wave[] waves;

    public event Action OnChanged;

    private void OnValidate() => OnChanged?.Invoke();

    public void NotifyChanged() => OnChanged?.Invoke();
}
