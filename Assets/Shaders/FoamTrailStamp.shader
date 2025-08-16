Shader "Hidden/FoamTrailStamp"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct VIn  { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct VOut { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            VOut vert(VIn v) { VOut o; o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; return o; }

            // Previous trail
            TEXTURE2D(_PrevTex); SAMPLER(sampler_PrevTex);

            // Decay per second and dt
            float _TrailDecay;     // 0..10  (higher = faster fade)
            float _DeltaTime;      // seconds since last stamp

            // World-space mapping (same as in ocean shader)
            float4 _TrailTransform; // (originX, originZ, invSizeX, invSizeZ)

            // Polygon data (world XZ)
            #define MAX_POLY 256
            int    _PolyCount;
            float4 _Poly[MAX_POLY]; // use .xy

            // Stamp shape controls
            float _StampWidth;      // band width around polygon boundary (units)
            float _StampIntensity;  // added intensity for this frame

            // Signed distance helpers
            float2 ClosestPointOnSegment(float2 a, float2 b, float2 p)
            {
                float2 ab = b - a;
                float t = dot(p - a, ab) / max(dot(ab, ab), 1e-6);
                t = saturate(t);
                return a + t * ab;
            }
            float DistToPolyline(float2 p, int count)
            {
                float d = 1e6;
                [loop] for (int i=0;i<count;i++)
                {
                    int j = (i+1)%count;
                    float2 a=_Poly[i].xy, b=_Poly[j].xy;
                    float2 c = ClosestPointOnSegment(a,b,p);
                    d = min(d, length(p - c));
                }
                return d;
            }

            float stampPolygon(float2 worldXZ)
            {
                if (_PolyCount < 3) return 0.0;
                float d = DistToPolyline(worldXZ, _PolyCount); // unsigned distance to boundary
                // Soft band around the boundary (width in world units)
                float w = max(_StampWidth, 1e-4);
                float m = saturate(1.0 - d / w);
                // ease
                m = m * m * (3 - 2*m);
                return m * _StampIntensity;
            }

            float4 frag(VOut i) : SV_Target
            {
                // Sample previous
                float prev = SAMPLE_TEXTURE2D(_PrevTex, sampler_PrevTex, i.uv).r;

                // Decay: exp(-decay * dt)
                float decay = exp(-_TrailDecay * _DeltaTime);
                float acc = prev * decay;

                // Convert UV -> world XZ for stamping
                float2 worldXZ = (i.uv - 0.5) / max(_TrailTransform.zw, float2(1e-6,1e-6)) + _TrailTransform.xy;

                // New stamp
                float s = stampPolygon(worldXZ);

                // Max-blend (prevents repeated stamping from dimming)
                acc = max(acc, s);

                return float4(acc, acc, acc, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
