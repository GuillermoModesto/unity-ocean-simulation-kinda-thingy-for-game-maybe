using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct WavesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<OctaveData> octaves;
    public NativeArray<float3> vertices;
    [ReadOnly] public NativeArray<float2> uvs;

    public int virtualDimensions;
    public float time;

    public float windStrength;
    public float2 windDirection;
    public float currentStrength;
    public float2 currentDirection;

    public void Execute(int index)
    {
        float3 v = vertices[index];
        float3 outV = v;

        float2 windDir = math.lengthsq(windDirection) > 0f ? math.normalize(windDirection) : float2.zero;
        float2 curDir = math.lengthsq(currentDirection) > 0f ? math.normalize(currentDirection) : float2.zero;

        const float EPS = 1e-4f;
        int dims = math.max(1, virtualDimensions);

        float2 uv = uvs[index];
        float xCoord = uv.x;
        float yCoord = uv.y;
        float gx = uv.x * dims;
        float gy = uv.y * dims;

        float height = 0f;
        int lastOctaveIndex = octaves.Length - 1;

        for (int i = 0; i < octaves.Length; i++)
        {
            var oc = octaves[i];
            if (!oc.active) continue;

            float baseMul = (i < 2) ? math.max(oc.baseScaleMultiplier, EPS) : 1f;

            float2 s = new float2(
                math.max(math.abs(oc.scale.x) * baseMul, EPS),
                math.max(math.abs(oc.scale.y) * baseMul, EPS)
            );

            float2 pos = new float2(gx * s.x, gy * s.y);

            // --- SPEED INFLUENCE ---
            float windSpeedInfluence = 0.05f; // default small effect
            if (i == lastOctaveIndex) // last octave gets big wind speed boost
                windSpeedInfluence = 0.25f;

            float currentSpeedInfluence = 0.05f; // minimal effect from current on speed

            float2 flowVec = windDir * windStrength * oc.windResponse * windSpeedInfluence
                           + curDir * currentStrength * oc.currentResponse * currentSpeedInfluence;

            float2 phaseVel = oc.speed + flowVec;

            // --- TIME OFFSET ---
            float2 tvec = phaseVel * (0.01f * time);

            // --- SHAPE ---
            float sine = math.sin(pos.x + tvec.x) + math.cos(pos.y + tvec.y);

            float2 noiseInput = new float2(xCoord * s.x + tvec.x, yCoord * s.y + tvec.y);
            float perlin = noise.cnoise(noiseInput);

            float blend = math.clamp(oc.perlinBlend, 0f, 1f);
            float wave = math.lerp(sine, perlin, blend);

            // --- HEIGHT INFLUENCE ---
            float ampFromEnv = 1f;
            if (i == 0) // first octave gets big current strength effect on height
                ampFromEnv += currentStrength * oc.currentResponse * 1.2f;
            else
                ampFromEnv += (windStrength * oc.windResponse + currentStrength * oc.currentResponse) * 0.3f;

            float dynH = oc.height * ampFromEnv;

            height += wave * dynH;
        }

        outV.y = height;
        vertices[index] = outV;
    }
}