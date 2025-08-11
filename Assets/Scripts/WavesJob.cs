using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct WavesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<OctaveData> octaves;
    public NativeArray<float3> vertices;

    public float time;

    public float windStrength;
    public float2 windDirection;
    public float currentStrength;
    public float2 currentDirection;

    public void Execute(int index)
    {
        const float EPS = 1e-4f;
        const float TAU = 6.28318530718f; // 2π

        float3 v = vertices[index];
        float wx = v.x;
        float wz = v.z;

        float2 windDir = math.lengthsq(windDirection) > 0f ? math.normalize(windDirection) : float2.zero;
        float2 curDir = math.lengthsq(currentDirection) > 0f ? math.normalize(currentDirection) : float2.zero;

        float height = 0f;
        int lastOctaveIndex = octaves.Length - 1;

        for (int i = 0; i < octaves.Length; i++)
        {
            var oc = octaves[i];
            if (!oc.active) continue;

            // base scale (cycles/m), allow early-octave base multiplier
            float baseMul = (i < 2) ? math.max(oc.baseScaleMultiplier, EPS) : 1f;

            float2 sBase = new float2(
                math.max(math.abs(oc.scale.x) * baseMul, EPS),
                math.max(math.abs(oc.scale.y) * baseMul, EPS)
            );

            // per-octave frequency boost
            float boost = math.max(0.01f, oc.scaleFrequencyBoost);
            float2 k = TAU * sBase * boost; // radians per meter

            // environmental flow (same logic as before)
            float windSpeedInfluence = (i == lastOctaveIndex) ? 0.25f : 0.05f;
            float currentSpeedInfluence = 0.05f;

            float2 flowVec = windDir * windStrength * oc.windResponse * windSpeedInfluence
                           + curDir * currentStrength * oc.currentResponse * currentSpeedInfluence;

            // NEW: separate direction + scalar speed
            float2 ownVelDir = math.lengthsq(oc.direction) > 0f ? math.normalize(oc.direction) : new float2(1f, 0f);
            float2 ownVel = ownVelDir * math.max(0f, oc.moveSpeed);

            float2 phaseVel = ownVel + flowVec;
            float2 tvec = phaseVel * (0.01f * time);

            float2 phase = new float2(wx, wz) * k + tvec;

            float sine = math.sin(phase.x) + math.cos(phase.y);
            float perlin = noise.cnoise(phase);
            float wave = math.lerp(sine, perlin, math.clamp(oc.perlinBlend, 0f, 1f));

            float ampFromEnv = 1f;
            if (i == 0)
                ampFromEnv += currentStrength * oc.currentResponse * 1.2f;
            else
                ampFromEnv += (windStrength * oc.windResponse + currentStrength * oc.currentResponse) * 0.3f;

            height += wave * (oc.height * ampFromEnv);
        }

        v.y = height;
        vertices[index] = v;
    }
}
