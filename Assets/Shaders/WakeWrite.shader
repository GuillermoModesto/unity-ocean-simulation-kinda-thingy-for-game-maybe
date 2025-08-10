Shader "Hidden/WakeWrite"
{
    Properties {}
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Center; // uv
            float  _Radius; // uv units
            float  _Intensity;
            float  _Hardness;

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(uint id:SV_VertexID)
            {
                v2f o;
                float2 pos = float2((id==2)?3:-1, (id==1)?3:-1);
                o.pos = float4(pos, 0, 1);
                o.uv = 0.5f * (pos + 1.0f);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;

                float2 d = i.uv - _Center.xy;
                // Wrap so splats across RT edges stay continuous
                d = frac(d + 0.5) - 0.5;

                float dist = length(d) / max(_Radius, 1e-4);
                float a = saturate(1.0 - dist);
                a = pow(a, _Hardness);
                float outv = saturate(src + a * _Intensity); // additive

                return float4(outv, outv, outv, 1);
            }
            ENDHLSL
        }
    } FallBack Off
}
