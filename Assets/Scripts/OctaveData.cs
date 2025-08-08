using Unity.Mathematics;

/// <summary>
/// Stores parameters for a single wave octave used in water simulation.
/// Used by the WavesJob to calculate mesh vertex heights.
/// </summary>
public struct OctaveData
{
    /// <summary>Speed of wave movement for this octave.</summary>
    public float2 speed;
    /// <summary>Scale of the wave pattern for this octave.</summary>
    public float2 scale;
    /// <summary>Amplitude (height) of the wave for this octave.</summary>
    public float height;
    /// <summary>Blend factor between sine/cosine and perlin noise (0 = sine, 1 = perlin).</summary>
    public float perlinBlend;
    /// <summary>Multiplier for base scale (used for first octaves).</summary>
    public float baseScaleMultiplier;
    /// <summary>If true, this octave is active in the simulation.</summary>
    public bool active;
    /// <summary>How much this octave responds to wind (0 = none, 1 = full).</summary>
    public float windResponse;
    /// <summary>How much this octave responds to current (0 = none, 1 = full).</summary>
    public float currentResponse;
}
