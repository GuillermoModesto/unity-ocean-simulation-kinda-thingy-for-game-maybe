// Ocean/GerstnerURP_Simplified_Green.shader
// Gerstner surface + simplified tropical shading (adds Jade/Emerald greens, smooth transitions, no sand/wake)
Shader "Ocean/GerstnerURP_Simplified_Green"
{
    Properties
    {
        // -------- Height color ramp (with greens) --------
        _AbyssColor     ("Abyss", Color)              = (0.005, 0.06, 0.12, 1)
        _DeepColor      ("Deep", Color)               = (0.01,  0.11, 0.20, 1)
        _MidColor       ("Mid / Teal", Color)         = (0.06,  0.35, 0.55, 1)
        _JadeColor      ("Jade", Color)               = (0.02,  0.47, 0.50, 1)
        _EmeraldColor   ("Emerald", Color)            = (0.02,  0.55, 0.46, 1)
        _ShallowColor   ("Shallow", Color)            = (0.50,  0.86, 0.88, 1)
        _RimColor       ("Rim (Fresnel) Color", Color)= (0.80, 0.97, 1.00, 1)

        _SeaLevel       ("Sea Level (world Y)", Float) = 0
        _HeightRange    ("Height Range (+/- m around sea)", Float) = 4.0
        _RampSoftness   ("Ramp Softness", Range(0,1)) = 0.6
        _RampShift      ("Ramp Shift (bias)", Range(-0.3,0.3)) = 0.0

        // -------- Detail normals (two layers, world-space UV) --------
        _NormalA        ("Normal A", 2D) = "bump" {}
        _NormalB        ("Normal B", 2D) = "bump" {}
        _NormalATiling  ("Normal A Tiling (x,y)", Vector) = (0.08, 0.08, 0, 0)
        _NormalBTiling  ("Normal B Tiling (x,y)", Vector) = (0.24, 0.24, 0, 0)
        _NormalASpeed   ("Normal A Scroll (x,y)", Vector) = (0.03, 0.01, 0, 0)
        _NormalBSpeed   ("Normal B Scroll (x,y)", Vector) = (-0.02, 0.00, 0, 0)
        _NormalAStr     ("Normal A Strength", Range(0, 2)) = 0.9
        _NormalBStr     ("Normal B Strength", Range(0, 2)) = 0.6

        // Normal attenuation
        _NormalStrength ("Overall Normal Strength", Range(0,1)) = 0.45
        _NormalFadeStart("Normal Fade Start (m)", Float) = 15
        _NormalFadeEnd  ("Normal Fade End (m)",   Float) = 120
        _NormalViewAtten("View-Angle Attenuation", Range(0,1)) = 0.6
        _NormalSlopeAtten("Slope Attenuation", Range(0,1)) = 0.5

        // -------- Foam (crest/noise; no depth needed) --------
        _FoamTex        ("Foam Noise (R)", 2D) = "white" {}
        _FoamTiling     ("Foam Tiling (x,y)", Vector) = (0.12, 0.12, 0, 0)
        _FoamSpeed      ("Foam Scroll (x,y)", Vector) = (0.12, 0.08, 0, 0)
        _FoamColor      ("Foam Color", Color) = (1,1,1,1)
        _FoamAmount     ("Foam Amount", Range(0, 2)) = 1.0
        _FoamSharp      ("Foam Sharpness", Range(0.5, 8)) = 3.0
        _FoamHeightBias ("Foam Bias to Peaks", Range(0,1)) = 0.45
        _FoamCurvAmt    ("Foam Curvature Boost", Range(0,2)) = 0.4
        _FoamEnabled    ("Foam Enabled (0/1)", Range(0,1)) = 1

        // -------- Lighting --------
        _SpecularColor  ("Specular Tint", Color) = (0.9, 0.95, 1, 1)
        _SpecularStrength("Specular Strength", Range(0,2)) = 1.0
        _Smoothness     ("Smoothness", Range(0,1)) = 0.85
        _FresnelPower   ("Fresnel Power", Range(0.2, 8)) = 3.0
        _FresnelBoost   ("Fresnel Boost", Range(0, 2)) = 0.6

        // -------- Transparency (single slider) --------
        _Transparency   ("Transparency", Range(0,1)) = 0.85
    }

    SubShader
    {
        // Transparent, ZWrite On for crisp silhouettes
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // -------- Wave params supplied by Ocean.cs --------
            int _WaveCount;
            float4 _DirAmpSteep[32]; // (Dx, Dz, A, S)
            float4 _WlOmegaPad[32];  // (wavelength, omega, _, _)
            float2 _UVOffset;
            float3 _OceanOrigin;
            float  _TimeSeconds;

            // -------- Material params --------
            float4 _AbyssColor,_DeepColor,_MidColor,_JadeColor,_EmeraldColor,_ShallowColor,_RimColor;
            float _SeaLevel,_HeightRange,_RampSoftness,_RampShift;

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            float4 _NormalATiling,_NormalBTiling;
            float4 _NormalASpeed,_NormalBSpeed;
            float _NormalAStr,_NormalBStr;
            float _NormalStrength,_NormalFadeStart,_NormalFadeEnd,_NormalViewAtten,_NormalSlopeAtten;

            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            float4 _FoamTiling,_FoamSpeed,_FoamColor;
            float _FoamAmount,_FoamSharp,_FoamHeightBias,_FoamCurvAmt,_FoamEnabled;

            float4 _SpecularColor;
            float _SpecularStrength,_Smoothness,_FresnelPower,_FresnelBoost;

            float _Transparency;

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 worldPos:TEXCOORD0; float3 worldNorm:TEXCOORD1; };

            // --- helpers ---
            float3 UnpackRG(float4 t, float strength)
            {
                float3 n;
                if (t.b < 0.2 || t.b > 0.95) { float2 rg = t.rg * 2 - 1; n = float3(rg, 0); }
                else                          { float2 rg = t.xy * 2 - 1; n = float3(rg, sqrt(saturate(1 - dot(rg,rg)))); }
                n.xy *= strength;
                n.z = sqrt(saturate(1 - dot(n.xy, n.xy)));
                return normalize(n);
            }

            float3 RNM(float3 a, float3 b)
            {
                float3 r = float3(a.xy * b.z + b.xy * a.z, a.z * b.z - dot(a.xy, b.xy));
                return normalize(r);
            }

            // Smooth, soft transitions across 6 stops
            float smoothWider(float a, float b, float x, float w)
            {
                return smoothstep(a - w, b + w, x);
            }

            float3 GreenRamp(float h, float softness)
            {
                // keys for 6 stops: Abyss → Deep → Mid → Jade → Emerald → Shallow
                float w = lerp(0.02, 0.10, saturate(softness)); // widen blends with softness
                float k0=0.00, k1=0.25, k2=0.45, k3=0.65, k4=0.82, k5=0.95;

                if (h < k1) { float t = smoothWider(k0, k1, h, w); return lerp(_AbyssColor.rgb,   _DeepColor.rgb,    t); }
                if (h < k2) { float t = smoothWider(k1, k2, h, w); return lerp(_DeepColor.rgb,    _MidColor.rgb,     t); }
                if (h < k3) { float t = smoothWider(k2, k3, h, w); return lerp(_MidColor.rgb,     _JadeColor.rgb,    t); }
                if (h < k4) { float t = smoothWider(k3, k4, h, w); return lerp(_JadeColor.rgb,    _EmeraldColor.rgb, t); }
                if (h < k5) { float t = smoothWider(k4, k5, h, w); return lerp(_EmeraldColor.rgb, _ShallowColor.rgb, t); }
                return _ShallowColor.rgb;
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float2 xz = wp.xz - _OceanOrigin.xz;

                float3 disp = 0;
                float3 dPdX = float3(1,0,0);
                float3 dPdZ = float3(0,0,1);

                // Sum Gerstner waves
                [loop]
                for (int i=0;i<_WaveCount;i++)
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

                    disp.x += QA * D.x * c;
                    disp.y += A  * s;
                    disp.z += QA * D.y * c;

                    float dxp = k * D.x;
                    float dzp = k * D.y;

                    dPdX += float3(-QA * D.x * dxp * s,  A * dxp * c, -QA * D.y * dxp * s);
                    dPdZ += float3(-QA * D.x * dzp * s,  A * dzp * c, -QA * D.y * dzp * s);
                }

                wp += disp;

                // Correct orientation
                float3 n = normalize(cross(dPdZ, dPdX));

                o.worldPos  = wp;
                o.worldNorm = n;
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 V = SafeNormalize(GetWorldSpaceViewDir(IN.worldPos));
                float3 baseN = normalize(IN.worldNorm);

                // -------- Height → color ramp --------
                float halfRange = max(1e-3, _HeightRange * 0.5);
                float h01 = saturate(0.5 + (IN.worldPos.y - _SeaLevel) / (2.0 * halfRange) + _RampShift);
                float3 col = GreenRamp(h01, saturate(_RampSoftness));

                // Gentle highlight rolloff
                col = col / (1.0 + col * 0.25);

                // -------- Scrolling detail normals (world-space UVs) --------
                float t = _TimeSeconds;
                float2 uvA = IN.worldPos.xz * _NormalATiling.xy + _NormalASpeed.xy * t;
                float2 uvB = IN.worldPos.xz * _NormalBTiling.xy + _NormalBSpeed.xy * t;

                float3 nA = UnpackRG(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA), _NormalAStr);
                float3 nB = UnpackRG(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB), _NormalBStr);

                float3 up = float3(0,1,0);
                float3 T = float3(1,0,0);
                float3 B = cross(up, T);
                float3 detailN = mul(float3x3(T,B,up), RNM(nA, nB));

                float3 combined = normalize(RNM(baseN, detailN));

                // Normal tamers
                float camDist = distance(GetCameraPositionWS(), IN.worldPos);
                float distFade = saturate( (_NormalFadeEnd - camDist) / max(1e-3, _NormalFadeEnd - _NormalFadeStart) );
                float viewDot = saturate(dot(baseN, V));
                float viewAtten = lerp(1.0, viewDot*viewDot, _NormalViewAtten);
                float slopeBase = 1.0 - saturate(dot(baseN, up));
                float slopeAtten = lerp(1.0, slopeBase, _NormalSlopeAtten);
                float normalMix = saturate(_NormalStrength * distFade * viewAtten * slopeAtten);

                float3 N = normalize(lerp(baseN, combined, normalMix));

                // -------- Lighting (main light) --------
                Light Lm = GetMainLight();
                float3 L = normalize(Lm.direction);
                float3 H = SafeNormalize(L + V);
                float NdotL = saturate(dot(N, L));
                float spec = pow(saturate(dot(N, H)), lerp(8.0,128.0,_Smoothness)) * NdotL;
                col = col + _SpecularColor.rgb * spec * _SpecularStrength;

                // Fresnel rim (color only)
                float fres = pow(saturate(1 - dot(N, V)), _FresnelPower) * _FresnelBoost;
                col = lerp(col, _RimColor.rgb, saturate(fres));

                // -------- Crest foam (slope + curvature + noise + height bias) --------
                float slopeCrest = pow(1.0 - saturate(dot(N, up)), _FoamSharp);
                float curv = (length(ddx(N)) + length(ddy(N)));
                curv = saturate(curv * 0.75);
                float peak = smoothstep(1.0 - _FoamHeightBias, 1.0, h01);

                float2 fuv = IN.worldPos.xz * _FoamTiling.xy + _FoamSpeed.xy * t;
                float noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv).r;

                float foamMask = _FoamEnabled * (slopeCrest + curv * _FoamCurvAmt) * (0.6 + 0.4*noise) * peak * _FoamAmount;
                foamMask = saturate(foamMask);

                col = lerp(col, _FoamColor.rgb, foamMask);

                // -------- Final alpha from slider --------
                float aOut = saturate(_Transparency);

                return float4(col, aOut);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
