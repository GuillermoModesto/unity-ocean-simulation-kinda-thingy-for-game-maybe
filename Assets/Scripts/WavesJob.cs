using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Parallel job for updating mesh vertices to simulate dynamic water waves.
/// Each vertex's height is calculated using multiple octaves, wind, and current influences.
/// </summary>
[BurstCompile]
public struct WavesJob : IJobParallelFor
{
    /// <summary>Array of octave parameters controlling wave layers.</summary>
    [ReadOnly] public NativeArray<OctaveData> octaves;
    /// <summary>Array of mesh vertices to be updated.</summary>
    public NativeArray<float3> vertices;
    /// <summary>Mesh grid dimension (number of vertices per side minus one).</summary>
    public int dimensions;
    /// <summary>Current simulation time.</summary>
    public float time;
    /// <summary>Strength of wind affecting the waves.</summary>
    public float windStrength;
    /// <summary>Direction of wind as a 2D vector.</summary>
    public float2 windDirection;
    /// <summary>Strength of water current affecting the waves.</summary>
    public float currentStrength;
    /// <summary>Direction of water current as a 2D vector.</summary>
    public float2 currentDirection;

    /// <summary>
    /// Calculates the new height for each vertex based on all active octaves and environmental influences.
    /// </summary>
    /// <param name="index">Index of the vertex in the mesh array.</param>
    public void Execute(int index)
    {
        // Convert flat index to grid coordinates
        int x = index / (dimensions + 1);
        int y = index % (dimensions + 1);

        float height = 0f;
        float xCoord = (float)x / dimensions;
        float yCoord = (float)y / dimensions;

        // Accumulate height from all active octaves
        for (int i = 0; i < octaves.Length; i++)
        {
            var octave = octaves[i];
            if (!octave.active) continue;

            // For first two octaves, apply base scale multiplier
            float2 scale = octave.scale * (i < 2 ? octave.baseScaleMultiplier : 1f);

            float2 pos = new float2(x * scale.x, y * scale.y);

            // Calculate wind and current influence factors
            float windFactor = math.clamp(dimensions / 100f, 1f, 10f);
            float currentFactor = windFactor * 0.5f;
            float2 windOffset = math.normalize(windDirection) * windStrength * windFactor;
            float2 currentOffset = math.normalize(currentDirection) * currentStrength * currentFactor;
            float2 totalInfluence = octave.speed + windOffset + currentOffset;

            // Time-based offset for animation
            float timeScale = 0.01f;
            float2 timeVec = time * totalInfluence * timeScale;

            // Sine and cosine wave for base shape
            float sine = math.sin(pos.x + timeVec.x) + math.cos(pos.y + timeVec.y);

            // Perlin noise for natural randomness
            float2 noiseInput = new float2(xCoord * scale.x + timeVec.x, yCoord * scale.y + timeVec.y);
            float perlin = noise.cnoise(noiseInput); // Range: -1 to 1

            // Blend between sine/cosine and perlin noise
            float blend = math.clamp(octave.perlinBlend, 0f, 1f);
            float wave = math.lerp(sine, perlin, blend);

            // Amplify height based on wind and current response
            float windAmp = math.pow(math.clamp(windStrength, 0f, 1f), 1.5f) * octave.windResponse;
            float currentAmp = math.pow(math.clamp(currentStrength, 0f, 1f), 1.3f) * octave.currentResponse;

            float dynamicHeight = octave.height * (1f + windAmp + currentAmp);
            height += wave * dynamicHeight;
        }

        // Update vertex height
        float3 vertex = vertices[index];
        vertex.y = height;
        vertices[index] = vertex;
    }
}
