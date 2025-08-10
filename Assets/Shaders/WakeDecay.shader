Shader "Hidden/WakeDecay"
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
            float  _Decay;       // 0..1 multiply
            float  _BlurRadius;  // in texels

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
                float2 texel = float2(ddx(i.uv.x), ddy(i.uv.y)); // approx; ok for fullscreen
                texel = abs(texel);                               // make positive
                // fallback if derivatives are tiny:
                texel = max(texel, 1.0/1024.0);

                float r = _BlurRadius;
                float w0 = 0.2941176; // 5-tap approx Gaussian weights sum=1
                float w1 = 0.2352941;
                float w2 = 0.1176470;

                float s =
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv                 ).r * w0 +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2( r*texel.x, 0)).r * w1 +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2( r*texel.x, 0)).r * w1 +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2( 0, r*texel.y)).r * w2 +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2( 0, r*texel.y)).r * w2;

                s *= _Decay;
                return float4(s,s,s,1);
            }
            ENDHLSL
        }
    } FallBack Off
}
