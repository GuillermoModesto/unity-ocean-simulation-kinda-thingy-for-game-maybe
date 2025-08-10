Shader "Custom/Water_SimpleZWrite"
{
    Properties
    {
        // --- Tropical color ramp (7 stops + rim) ---
        _DeepColor      ("Deep (ocean)", Color)      = (0.01, 0.11, 0.20, 1)
        _BlueColor      ("Blue (offshore)", Color)   = (0.00, 0.28, 0.50, 1)
        _MidColor       ("Mid / Teal", Color)        = (0.06, 0.35, 0.55, 1)
        _EmeraldColor   ("Emerald", Color)           = (0.02, 0.55, 0.46, 1)
        _AquaColor      ("Aqua", Color)              = (0.18, 0.78, 0.85, 1)
        _ShallowColor   ("Shallow", Color)           = (0.32, 0.90, 0.86, 1)
        _SandColor      ("Sand / Shore", Color)      = (0.86, 0.88, 0.84, 1)
        _RimColor       ("Rim (Fresnel) Color", Color)= (0.80, 0.97, 1.00, 1)

        _SandStrength   ("Sand Strength", Range(0,1)) = 0.35
        _SandSaturation ("Sand Saturation", Range(0,1)) = 0.7
        _SandBrightness ("Sand Brightness", Range(0.5,1.5)) = 0.95

        _SeaLevel       ("Sea Level (world Y)", Float) = 0
        _HeightRange    ("Height Range (+/- m around sea)", Float) = 4.0
        _RampSoftness   ("Ramp Softness", Range(0,1)) = 0.4
        _RampShift      ("Ramp Shift (bias)", Range(-0.3,0.3)) = 0.0

        // --- Normals (two layers) ---
        _NormalA        ("Normal A", 2D) = "bump" {}
        _NormalB        ("Normal B", 2D) = "bump" {}
        _NormalATiling  ("Normal A Tiling (x,y)", Vector) = (0.08, 0.08, 0, 0)
        _NormalBTiling  ("Normal B Tiling (x,y)", Vector) = (0.24, 0.24, 0, 0)
        _NormalASpeed   ("Normal A Scroll (x,y)", Vector) = (0.03, 0.01, 0, 0)
        _NormalBSpeed   ("Normal B Scroll (x,y)", Vector) = (-0.02, 0.00, 0, 0)
        _NormalAStr     ("Normal A Strength", Range(0, 2)) = 0.9
        _NormalBStr     ("Normal B Strength", Range(0, 2)) = 0.6

        // --- Normal tamers ---
        _NormalStrength ("Overall Normal Strength", Range(0,1)) = 0.45
        _NormalFadeStart("Normal Fade Start (m)", Float) = 15
        _NormalFadeEnd  ("Normal Fade End (m)",   Float) = 120
        _NormalViewAtten("View-Angle Attenuation", Range(0,1)) = 0.6
        _NormalSlopeAtten("Slope Attenuation", Range(0,1)) = 0.5

        // --- Foam (crest only; no depth needed) ---
        _FoamTex        ("Foam Noise (R)", 2D) = "white" {}
        _FoamTiling     ("Foam Tiling (x,y)", Vector) = (0.12, 0.12, 0, 0)
        _FoamSpeed      ("Foam Scroll (x,y)", Vector) = (0.12, 0.08, 0, 0)
        _FoamColor      ("Foam Color", Color) = (1,1,1,1)
        _FoamAmount     ("Foam Amount", Range(0, 2)) = 1.0
        _FoamSharp      ("Foam Sharpness", Range(0.5, 8)) = 3.0
        _FoamHeightBias ("Foam Bias to Peaks", Range(0,1)) = 0.45
        _FoamCurvAmt    ("Foam Curvature Boost", Range(0,2)) = 0.4

        // --- Lighting-ish ---
        _SpecularColor  ("Specular Tint", Color) = (0.9, 0.95, 1, 1)
        _SpecularStrength("Specular Strength", Range(0,2)) = 1.0
        _Smoothness     ("Smoothness", Range(0,1)) = 0.85
        _FresnelPower   ("Fresnel Power", Range(0.2, 8)) = 3.0
        _FresnelBoost   ("Fresnel Boost", Range(0, 2)) = 0.6

        // --- Transparency ---
        _AlphaBase      ("Base Alpha", Range(0,1)) = 0.55
        _AlphaFresnel   ("Fresnel Alpha Boost", Range(0,1)) = 0.35

        // --- Time / flow (kept for compatibility; not used for normals/foam) ---
        _WaveTime       ("Wave Time (seconds)", Float) = 0
        _UseExternalTime("Use External Time (1=yes)", Float) = 0
        _WindDir        ("_WindDir (x,y,w=strength)", Vector) = (1,0,0,1)
        _CurrentDir     ("_CurrentDir (x,y,w=strength)", Vector) = (1,0,0,1)

        // --- Wake / ripples (optional; fed by WakePainter) ---
        _WakeMap            ("Wake Map (auto)", 2D) = "black" {}
        _WakeFoamStrength   ("Wake Foam Strength", Range(0,2)) = 1.0
        _WakeNormalStrength ("Wake Ripple Normal", Range(0,1)) = 0.25
        _WakeUV             ("Wake UV (x=scale, y=offX, z=offY)", Vector) = (0.02,0,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                // ramp
                float4 _DeepColor, _BlueColor, _MidColor, _EmeraldColor, _AquaColor, _ShallowColor, _SandColor, _RimColor;
                float _SandStrength, _SandSaturation, _SandBrightness;
                float _SeaLevel, _HeightRange, _RampSoftness, _RampShift;

                // normals
                float4 _NormalATiling, _NormalBTiling;
                float4 _NormalASpeed,  _NormalBSpeed;
                float _NormalAStr, _NormalBStr;

                // tamers
                float _NormalStrength, _NormalFadeStart, _NormalFadeEnd, _NormalViewAtten, _NormalSlopeAtten;

                // foam
                float4 _FoamTiling, _FoamSpeed, _FoamColor;
                float _FoamAmount, _FoamSharp, _FoamHeightBias, _FoamCurvAmt;

                // spec/fresnel
                float4 _SpecularColor;
                float _SpecularStrength, _Smoothness, _FresnelPower, _FresnelBoost;

                // alpha
                float _AlphaBase, _AlphaFresnel;

                // time/flow (compat)
                float _WaveTime, _UseExternalTime;
                float4 _WindDir, _CurrentDir;

                // wake
                float4 _WakeUV;
                float _WakeFoamStrength, _WakeNormalStrength;
            CBUFFER_END

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_WakeMap); SAMPLER(sampler_WakeMap);

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 worldPos:TEXCOORD0; float3 worldNorm:TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.worldPos  = TransformObjectToWorld(IN.positionOS.xyz);
                o.worldNorm = normalize(TransformObjectToWorldNormal(IN.normalOS));
                o.positionCS = TransformWorldToHClip(o.worldPos);
                return o;
            }

            float3 UnpackRG(float4 t, float s)
            {
                float3 n;
                if (t.b < 0.2 || t.b > 0.95) { float2 rg=t.rg*2-1; n=float3(rg,0); }
                else                          { n = UnpackNormal(t); }
                n.xy *= s;
                n.z = sqrt(saturate(1 - dot(n.xy,n.xy)));
                return normalize(n);
            }

            float3 RNM(float3 a,float3 b)
            {
                float3 r=float3(a.xy*b.z + b.xy*a.z, a.z*b.z - dot(a.xy,b.xy));
                return normalize(r);
            }

            // Piecewise smooth tropical ramp with tunable sand
            float3 TropicalRamp(float h, float s)
            {
                float k0=0.00, k1=0.12, k2=0.28, k3=0.45, k4=0.62, k5=0.78, k6=0.92;
                float w = s * 0.06;
                float3 c;

                if (h < k1)      c = lerp(_DeepColor.rgb,     _BlueColor.rgb,     smoothstep(k0-w, k1+w, h));
                else if (h < k2) c = lerp(_BlueColor.rgb,     _MidColor.rgb,      smoothstep(k1-w, k2+w, h));
                else if (h < k3) c = lerp(_MidColor.rgb,      _EmeraldColor.rgb,  smoothstep(k2-w, k3+w, h));
                else if (h < k4) c = lerp(_EmeraldColor.rgb,  _AquaColor.rgb,     smoothstep(k3-w, k4+w, h));
                else if (h < k5) c = lerp(_AquaColor.rgb,     _ShallowColor.rgb,  smoothstep(k4-w, k5+w, h));
                else
                {
                    float tSand = smoothstep(k5-w, k6+w, h);
                    float3 sand = _SandColor.rgb;
                    float luma = dot(sand, float3(0.299, 0.587, 0.114));
                    sand = lerp(float3(luma,luma,luma), sand, _SandSaturation) * _SandBrightness;
                    float3 sandMix = lerp(_ShallowColor.rgb, sand, tSand);
                    c = lerp(_ShallowColor.rgb, sandMix, _SandStrength);
                }
                return c;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // --- Height → 0..1 ---
                float halfRange = max(1e-3, _HeightRange * 0.5);
                float h01 = 0.5 + (IN.worldPos.y - _SeaLevel) / (2.0 * halfRange);
                h01 = saturate(h01 + _RampShift);

                // --- Base tropical color ---
                float3 col = TropicalRamp(h01, saturate(_RampSoftness));

                // --- Time (decoupled from wind/current) ---
                float t = (_UseExternalTime > 0.5) ? _WaveTime : _Time.y;

                // --- Two scrolling normal layers (NO wind/current influence) ---
                float2 uvA = IN.worldPos.xz * _NormalATiling.xy + _NormalASpeed.xy * t;
                float2 uvB = IN.worldPos.xz * _NormalBTiling.xy + _NormalBSpeed.xy * t;

                float3 nA = UnpackRG(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA), _NormalAStr);
                float3 nB = UnpackRG(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB), _NormalBStr);

                float3 up = float3(0,1,0), T=float3(1,0,0), B=cross(up,T);
                float3 baseN   = normalize(IN.worldNorm);
                float3 detailN = mul(float3x3(T,B,up), RNM(nA,nB));
                float3 combined = normalize(RNM(baseN, detailN));

                // --- Normal tamers ---
                float3 camPos = GetCameraPositionWS();
                float camDist = distance(camPos, IN.worldPos);
                float distFade = saturate( (_NormalFadeEnd - camDist) / max(1e-3, _NormalFadeEnd - _NormalFadeStart) );

                float3 V = SafeNormalize(GetWorldSpaceViewDir(IN.worldPos));
                float viewDot = saturate(dot(baseN, V));
                float viewAtten = lerp(1.0, viewDot*viewDot, _NormalViewAtten);

                float slopeBase = 1.0 - saturate(dot(baseN, up));
                float slopeAtten = lerp(1.0, slopeBase, _NormalSlopeAtten);

                float normalMix = saturate(_NormalStrength * distFade * viewAtten * slopeAtten);
                float3 N = normalize(lerp(baseN, combined, normalMix));

                // --- Wake sampling (foam + ripple normal) ---
                float2 wakeUV = IN.worldPos.xz * _WakeUV.x + _WakeUV.yz;
                float wake = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV).r;

                // gradient (finite difference)
                float2 texel = float2(1.0/1024.0, 1.0/1024.0); // safe default
                float wL = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV - float2(texel.x, 0)).r;
                float wR = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV + float2(texel.x, 0)).r;
                float wD = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV - float2(0, texel.y)).r;
                float wU = SAMPLE_TEXTURE2D(_WakeMap, sampler_WakeMap, wakeUV + float2(0, texel.y)).r;

                float2 grad = float2(wR - wL, wU - wD);
                float3 wakeN = normalize(float3(-grad * 8.0, 1.0));
                N = normalize(lerp(N, normalize(RNM(N, wakeN)), _WakeNormalStrength));

                // --- Fresnel + simple spec ---
                float fres = pow(saturate(1 - dot(N, V)), _FresnelPower) * _FresnelBoost;
                Light Lm = GetMainLight();
                float3 L = normalize(Lm.direction);
                float3 H = normalize(L + V);
                float NdotL = saturate(dot(N,L));
                float NdotH = saturate(dot(N,H));
                float spec = pow(NdotH, lerp(8.0,128.0,_Smoothness)) * NdotL;

                col = col + _SpecularColor.rgb * spec * _SpecularStrength;
                col = lerp(col, _RimColor.rgb, saturate(fres));

                // --- Crest foam (decoupled from wind/current; uses own speed/tiling) ---
                float curv = (length(ddx(N)) + length(ddy(N)));  curv = saturate(curv * 0.75);
                float peak = smoothstep(1.0 - _FoamHeightBias, 1.0, h01);

                float2 fuv = IN.worldPos.xz * _FoamTiling.xy + _FoamSpeed.xy * t; // <— no flow
                float noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv).r;

                float crestSlope = pow(1.0 - saturate(dot(N, up)), _FoamSharp);
                float foamMask = (crestSlope + curv * _FoamCurvAmt) * (0.6 + 0.4*noise) * peak * _FoamAmount;

                // add wake foam
                foamMask = saturate(foamMask + wake * _WakeFoamStrength);

                // apply foam
                col = lerp(col, _FoamColor.rgb, foamMask);

                // --- Alpha ---
                float aOut = saturate(_AlphaBase + fres * _AlphaFresnel);
                aOut = saturate(aOut + foamMask * 0.15);

                return float4(col, aOut);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
