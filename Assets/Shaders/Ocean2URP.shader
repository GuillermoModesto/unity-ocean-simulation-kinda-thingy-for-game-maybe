// Ocean/GerstnerURP_Stylized.shader
// Gerstner surface (from Ocean.cs) + stylized tropical shading (ramp, normals, foam, fresnel)
Shader "Ocean/GerstnerURP_Stylized"
{
    Properties
    {
        // -------- Height color ramp (tropical) --------
        _AbyssColor     ("Abyss", Color)              = (0.005, 0.06, 0.12, 1)
        _DeepColor      ("Deep", Color)               = (0.01,  0.11, 0.20, 1)
        _BlueColor      ("Blue", Color)               = (0.00,  0.28, 0.50, 1)
        _CobaltColor    ("Cobalt", Color)             = (0.00,  0.36, 0.62, 1)
        _MidColor       ("Mid/Teal", Color)           = (0.06,  0.35, 0.55, 1)
        _JadeColor      ("Jade", Color)               = (0.02,  0.47, 0.50, 1)
        _EmeraldColor   ("Emerald", Color)            = (0.02,  0.55, 0.46, 1)
        _AquaColor      ("Aqua", Color)               = (0.18,  0.78, 0.85, 1)
        _TurquoiseColor ("Turquoise", Color)          = (0.24,  0.88, 0.86, 1)
        _MintColor      ("Mint", Color)               = (0.64,  0.93, 0.90, 1)
        _ShallowColor   ("Shallow", Color)            = (0.72,  0.96, 0.92, 1)
        _SandColor      ("Sand/Shore", Color)         = (0.86,  0.88, 0.84, 1)
        _RimColor       ("Rim (Fresnel) Color", Color)= (0.80, 0.97, 1.00, 1)

        _SandStrength   ("Sand Strength", Range(0,1)) = 0.35
        _SandSaturation ("Sand Saturation", Range(0,1)) = 0.7
        _SandBrightness ("Sand Brightness", Range(0.5,1.5)) = 0.95

        _SeaLevel       ("Sea Level (world Y)", Float) = 0
        _HeightRange    ("Height Range (+/- m around sea)", Float) = 4.0
        _RampSoftness   ("Ramp Softness", Range(0,1)) = 0.4
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

        // -------- Transparency (depth-friendly like your old shader) --------
        _AlphaBase      ("Base Alpha", Range(0,1)) = 0.55
        _AlphaFresnel   ("Fresnel Alpha Boost", Range(0,1)) = 0.35

        // Optional wake map (black by default)
        _WakeMap            ("Wake Map (auto)", 2D) = "black" {}
        _WakeFoamStrength   ("Wake Foam Strength", Range(0,2)) = 1.0
        _WakeNormalStrength ("Wake Ripple Normal", Range(0,1)) = 0.25
        _WakeUV             ("Wake UV (x=scale, y=offX, z=offY)", Vector) = (0.02,0,0,0)
    }

    SubShader
    {
        // Transparent, but ZWrite On for crisp silhouettes (like your previous)
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
            float2 _UVOffset;        // unused here but kept for parity
            float3 _OceanOrigin;
            float  _TimeSeconds;

            // -------- Material params (Unity auto-packs) --------
            float4 _AbyssColor,_DeepColor,_BlueColor,_CobaltColor,_MidColor,_JadeColor,_EmeraldColor,_AquaColor,_TurquoiseColor,_MintColor,_ShallowColor,_SandColor,_RimColor;
            float _SandStrength,_SandSaturation,_SandBrightness;
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

            float _AlphaBase,_AlphaFresnel;

            TEXTURE2D(_WakeMap); SAMPLER(sampler_WakeMap);
            float4 _WakeUV;
            float _WakeFoamStrength,_WakeNormalStrength;

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 worldPos:TEXCOORD0; float3 worldNorm:TEXCOORD1; };

            // --- helpers ---
            float3 UnpackRG(float4 t, float strength)
            {
                // robust against RG packed or proper normal maps
                float3 n;
                if (t.b < 0.2 || t.b > 0.95) { float2 rg = t.rg * 2 - 1; n = float3(rg, 0); }
                else                          { float2 rg = t.xy * 2 - 1; n = float3(rg, sqrt(saturate(1 - dot(rg,rg)))); }
                n.xy *= strength;
                n.z = sqrt(saturate(1 - dot(n.xy, n.xy)));
                return normalize(n);
            }

            float3 RNM(float3 a, float3 b)
            {
                // Reoriented Normal Mapping
                float3 r = float3(a.xy * b.z + b.xy * a.z, a.z * b.z - dot(a.xy, b.xy));
                return normalize(r);
            }

            float3 TropicalRamp(float h, float softness)
            {
                float w = softness * 0.06;
                float k0=0.00, k1=0.08, k2=0.16, k3=0.24, k4=0.32, k5=0.42, k6=0.54, k7=0.66, k8=0.78, k9=0.90, kA=0.96;

                if (h < k1) return lerp(_AbyssColor.rgb,    _DeepColor.rgb,       smoothstep(k0-w, k1+w, h));
                if (h < k2) return lerp(_DeepColor.rgb,     _BlueColor.rgb,       smoothstep(k1-w, k2+w, h));
                if (h < k3) return lerp(_BlueColor.rgb,     _CobaltColor.rgb,     smoothstep(k2-w, k3+w, h));
                if (h < k4) return lerp(_CobaltColor.rgb,   _MidColor.rgb,        smoothstep(k3-w, k4+w, h));
                if (h < k5) return lerp(_MidColor.rgb,      _JadeColor.rgb,       smoothstep(k4-w, k5+w, h));
                if (h < k6) return lerp(_JadeColor.rgb,     _EmeraldColor.rgb,    smoothstep(k5-w, k6+w, h));
                if (h < k7) return lerp(_EmeraldColor.rgb,  _AquaColor.rgb,       smoothstep(k6-w, k7+w, h));
                if (h < k8) return lerp(_AquaColor.rgb,     _TurquoiseColor.rgb,  smoothstep(k7-w, k8+w, h));
                if (h < k9) return lerp(_TurquoiseColor.rgb,_MintColor.rgb,       smoothstep(k8-w, k9+w, h));
                if (h < kA) return lerp(_MintColor.rgb,     _ShallowColor.rgb,    smoothstep(k9-w, kA+w, h));

                // sand blend
                float tSand = smoothstep(kA-w, 1.0, h);
                float3 sand = _SandColor.rgb;
                float luma  = dot(sand, float3(0.299, 0.587, 0.114));
                sand = lerp(float3(luma,luma,luma), sand, _SandSaturation) * _SandBrightness;
                float3 sandMix = lerp(_ShallowColor.rgb, sand, tSand);
                return lerp(_ShallowColor.rgb, sandMix, _SandStrength);
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

                // Correct orientation (matches CPU): up points +Y on flat patch
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
                float h01 = 0.5 + (IN.worldPos.y - _SeaLevel) / (2.0 * halfRange);
                h01 = saturate(h01 + _RampShift);
                float3 col = TropicalRamp(h01, saturate(_RampSoftness));

                // -------- Scrolling detail normals (world-space UVs) --------
                float t = _TimeSeconds;
                float2 uvA = IN.worldPos.xz * _NormalATiling.xy + _NormalASpeed.xy * t;
                float2 uvB = IN.worldPos.xz * _NormalBTiling.xy + _NormalBSpeed.xy * t;

                float3 nA = UnpackRG(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA), _NormalAStr);
                float3 nB = UnpackRG(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB), _NormalBStr);

                // Build a world tangent frame (cheap): use XZ as tangent plane
                float3 up = float3(0,1,0);
                float3 T = float3(1,0,0);
                float3 B = cross(up, T);
                float3 detailN = mul(float3x3(T,B,up), RNM(nA, nB));

                // Basic RNM combine of analytic normal and detail
                float3 combined = normalize(RNM(baseN, detailN));

                // Normal tamers (distance, view angle, slope)
                float camDist = distance(GetCameraPositionWS(), IN.worldPos);
                float distFade = saturate( (_NormalFadeEnd - camDist) / max(1e-3, _NormalFadeEnd - _NormalFadeStart) );
                float viewDot = saturate(dot(baseN, V));
                float viewAtten = lerp(1.0, viewDot*viewDot, _NormalViewAtten);
                float slopeBase = 1.0 - saturate(dot(baseN, up));
                float slopeAtten = lerp(1.0, slopeBase, _NormalSlopeAtten);
                float normalMix = saturate(_NormalStrength * distFade * viewAtten * slopeAtten);

                float3 N = normalize(lerp(baseN, combined, normalMix));

                // Optional wake: ripple normal + foam boost
                float2 wakeUV = IN.worldPos.xz * _WakeUV.x + _WakeUV.yz;
                float wake = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV).r;
                // Approx ripple normal from wake map gradient
                float2 texel = float2(1.0/1024.0, 1.0/1024.0);
                float wL = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV - float2(texel.x, 0)).r;
                float wR = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV + float2(texel.x, 0)).r;
                float wD = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV - float2(0, texel.y)).r;
                float wU = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV + float2(0, texel.y)).r;
                float2 grad = float2(wR-wL, wU-wD);
                float3 wakeN = normalize(float3(-grad * 8.0, 1.0));
                N = normalize(lerp(N, normalize(RNM(N, wakeN)), _WakeNormalStrength));

                // -------- Lighting (main light) --------
                Light Lm = GetMainLight();
                float3 L = normalize(Lm.direction);
                float3 H = SafeNormalize(L + V);
                float NdotL = saturate(dot(N, L));
                float spec = pow(saturate(dot(N, H)), lerp(8.0,128.0,_Smoothness)) * NdotL;

                col = col + _SpecularColor.rgb * spec * _SpecularStrength;

                // Fresnel rim
                float fres = pow(saturate(1 - dot(N, V)), _FresnelPower) * _FresnelBoost;
                col = lerp(col, _RimColor.rgb, saturate(fres));

                // -------- Crest foam (slope + curvature + noise + height bias) --------
                float slopeCrest = pow(1.0 - saturate(dot(N, up)), _FoamSharp);          // steepness
                float curv = (length(ddx(N)) + length(ddy(N)));  curv = saturate(curv*0.75); // micro-crest
                float peak = smoothstep(1.0 - _FoamHeightBias, 1.0, h01);                // higher = more foam

                float2 fuv = IN.worldPos.xz * _FoamTiling.xy + _FoamSpeed.xy * t;
                float noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv).r;

                float foamMask = _FoamEnabled * (slopeCrest + curv * _FoamCurvAmt) * (0.6 + 0.4*noise) * peak * _FoamAmount;
                foamMask = saturate(foamMask + wake * _WakeFoamStrength);

                col = lerp(col, _FoamColor.rgb, foamMask);

                // -------- Alpha (depth-friendly) --------
                float aOut = saturate(_AlphaBase + fres * _AlphaFresnel);
                aOut = saturate(aOut + foamMask * 0.15);

                return float4(col, aOut);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
