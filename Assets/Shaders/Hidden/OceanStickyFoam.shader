Shader "Hidden/OceanStickyFoam"
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

            VOut vert(VIn v){ VOut o; o.pos=float4(v.pos.xy,0,1); o.uv=v.uv; return o; }

            TEXTURE2D(_PrevTex); SAMPLER(sampler_PrevTex);
            float  _DecayPerSec;
            float  _DeltaTime;
            float4 _StickyTransform;

            #define MAX_POLY 256
            int    _PolyCount;
            float4 _Poly[MAX_POLY];

            float _BandWidth;
            float _AddAmount;

            float2 ClosestPointOnSegment(float2 a,float2 b,float2 p)
            { float2 ab=b-a; float t=dot(p-a,ab)/max(dot(ab,ab),1e-6); t=saturate(t); return a+t*ab; }

            float DistToPolyline(float2 p, int count)
            {
                float d=1e6;
                [loop] for(int i=0;i<count;i++){int j=(i+1)%count; float2 c=ClosestPointOnSegment(_Poly[i].xy,_Poly[j].xy,p); d=min(d,length(p-c));}
                return d;
            }

            float StampMask(float2 worldXZ)
            {
                if (_PolyCount<3) return 0.0;
                float d = DistToPolyline(worldXZ, _PolyCount);
                float w = max(_BandWidth, 1e-4);
                float m = saturate(1.0 - d / w);
                return m*m*(3-2*m);
            }

            float4 frag(VOut i) : SV_Target
            {
                float prev = SAMPLE_TEXTURE2D(_PrevTex, sampler_PrevTex, i.uv).r;
                float keep = exp(-_DecayPerSec * max(_DeltaTime, 0.0));
                float acc  = prev * keep;

                float2 worldXZ = (i.uv - 0.5) / max(_StickyTransform.zw, float2(1e-6,1e-6)) + _StickyTransform.xy;
                float s = StampMask(worldXZ) * _AddAmount;

                acc = max(acc, s);
                return float4(acc, acc, acc, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
