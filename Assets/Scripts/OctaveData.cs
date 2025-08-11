using Unity.Mathematics;

/// <summary>Stores parameters for a single wave octave used in water simulation.</summary>
public struct OctaveData
{
    public float2 direction;          // NEW: normalized travel direction
    public float moveSpeed;          // NEW: scalar speed along 'direction'

    public float2 scale;
    public float height;
    public float perlinBlend;
    public float baseScaleMultiplier;
    public bool active;
    public float windResponse;
    public float currentResponse;

    // How strongly Scale affects frequency for this octave (1 = raw, 4 = punchy)
    public float scaleFrequencyBoost;
}
