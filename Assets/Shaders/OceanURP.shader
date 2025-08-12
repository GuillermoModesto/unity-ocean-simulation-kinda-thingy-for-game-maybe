// OceanURP.shader — Transparent water, correct normal, foam toggle
Shader "Ocean/GerstnerURP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.03,0.12,0.2,1)
        _FoamColor("Foam Color", Color) = (0.9,0.95,1,1)

        _Opacity("Opacity", Range(0,1)) = 0.85

        // Foam controls
        _FoamEnabled("Foam Enabled (0/1)", Range(0,1)) = 0
        _FoamThreshold("Foam Threshold", Range(0,1)) = 0.5
        _FoamIntensity("Foam Intensity", Range(0,3)) = 1.5

        // Simple lighting params
        _Specular("Specular", Range(0,1)) = 0.08
        _Smoothness("Smoothness", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f {
                float4 posCS    : SV_POSITION;
                float3 normalW  : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float  foam     : TEXCOORD2; // 0..1
            };

            float4 _BaseColor, _FoamColor;
            float  _Opacity;

            float  _FoamEnabled;
            float  _FoamThreshold, _FoamIntensity;

            float  _Specular, _Smoothness;

            // Wave params (set from Ocean.cs)
            int    _WaveCount;
            float4 _DirAmpSteep[32]; // (Dx, Dz, A, S)
            float4 _WlOmegaPad[32];  // (wavelength, omega, _, _)
            float2 _UVOffset;
            float3 _OceanOrigin;
            float  _TimeSeconds;
            float  _Choppiness;

            v2f vert (appdata v)
            {
                v2f o;

                float3 wp = TransformObjectToWorld(v.vertex.xyz);
                float2 xz = wp.xz - _OceanOrigin.xz;

                float3 disp = 0;
                float3 dPdX = float3(1,0,0);
                float3 dPdZ = float3(0,0,1);

                [loop]
                for (int i = 0; i < _WaveCount; i++)
                {
                    float2 D = normalize(_DirAmpSteep[i].xy);
                    float  A = _DirAmpSteep[i].z;
                    float  S = _DirAmpSteep[i].w;
                    float  wl = _WlOmegaPad[i].x;
                    float  w  = _WlOmegaPad[i].y;

                    float k = 2.0 * PI / max(0.001, wl);
                    float phase = k * dot(D, xz) - w * _TimeSeconds;
                    float c = cos(phase), s = sin(phase);
                    float QA = S * A;

                    // Displacement (Gerstner with choppiness)
                    disp.x += QA * D.x * c;
                    disp.y += A  * s;
                    disp.z += QA * D.y * c;

                    // Partials for analytic normal
                    float dxp = k * D.x;
                    float dzp = k * D.y;

                    dPdX += float3(-QA * D.x * dxp * s,  A * dxp * c, -QA * D.y * dxp * s);
                    dPdZ += float3(-QA * D.x * dzp * s,  A * dzp * c, -QA * D.y * dzp * s);
                }

                wp += disp;

                // Correct orientation: up on a flat patch
                float3 n = normalize(cross(dPdZ, dPdX));

                o.posCS    = TransformWorldToHClip(wp);
                o.worldPos = wp;
                o.normalW  = n;

                // Crest-only foam mask (computed in vertex)
                float slope = 1.0 - saturate(n.y);             // 0 (flat/up) .. 1 (vertical)
                float crest = step(_FoamThreshold, slope) * slope;
                o.foam = _FoamEnabled * crest * _FoamIntensity;

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 V = SafeNormalize(GetWorldSpaceViewDir(i.worldPos));
                float3 N = normalize(i.normalW);

                // Main directional light (URP)
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = SafeNormalize(L + V);

                float  NdotL   = saturate(dot(N, L));
                float  specPow = lerp(32.0, 256.0, _Smoothness);
                float  spec    = pow(saturate(dot(N, H)), specPow) * _Specular;

                float3 baseCol = _BaseColor.rgb;
                float3 lit     = baseCol * (0.25 + 0.75 * NdotL) * mainLight.color + spec;

                // Mix foam (toggleable)
                float  foamMask = saturate(i.foam);
                float3 col      = lerp(lit, _FoamColor.rgb, foamMask);

                return half4(col, _Opacity);
            }
            ENDHLSL
        }
    }
}
