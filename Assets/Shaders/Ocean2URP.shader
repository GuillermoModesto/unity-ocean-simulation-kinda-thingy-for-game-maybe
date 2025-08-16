/*
 Summary: URP-compatible ocean surface shader that consumes wave buffers from Ocean.cs to render normals, displacement, and shading.

 Usage:
   - Use with a material referenced by Ocean.cs. Shader expects wave arrays (_DirAmpSteep, _WlOmegaPad).
   - Transparency drives render queue; Ocean.cs can force opaque when near-opaque for sorting stability.
   - Tweak normals/foam parameters in the material for look-dev.

   Added:
   - Polygon-driven contact/ripple foam. Provide _Poly (Vector4[]: x=worldX, y=worldZ) + _PolyCount.
   - If _PolyCount >= 3, foam/ripples originate from the polygon boundary (signed distance SDF).
   - Falls back to point emitters if no polygon is provided.
*/

Shader "Ocean/GerstnerURP_Simplified_Green"
{
    Properties
    {
        _AbyssColor     ("Abyss", Color)               = (0.005, 0.06, 0.12, 1)
        _DeepColor      ("Deep", Color)                = (0.01,  0.11, 0.20, 1)
        _MidColor       ("Mid / Teal", Color)          = (0.06,  0.35, 0.55, 1)
        _JadeColor      ("Jade", Color)                = (0.02,  0.47, 0.50, 1)
        _EmeraldColor   ("Emerald", Color)             = (0.02,  0.55, 0.46, 1)
        _ShallowColor   ("Shallow", Color)             = (0.50,  0.86, 0.88, 1)
        _RimColor       ("Rim (Fresnel) Color", Color) = (0.80, 0.97, 1.00, 1)

        _SeaLevel       ("Sea Level (world Y)", Float) = 0
        _HeightRange    ("Height Range (+/- m around sea)", Float) = 4.0
        _RampSoftness   ("Ramp Softness", Range(0,1))  = 0.6
        _RampShift      ("Ramp Shift (bias)", Range(-0.3,0.3)) = 0.0

        _NormalA        ("Normal A", 2D) = "bump" {}
        _NormalB        ("Normal B", 2D) = "bump" {}
        _NormalATiling  ("Normal A Tiling (x,y)", Vector) = (0.08, 0.08, 0, 0)
        _NormalBTiling  ("Normal B Tiling (x,y)", Vector) = (0.24, 0.24, 0, 0)
        _NormalASpeed   ("Normal A Scroll (x,y)", Vector) = (0.03, 0.01, 0, 0)
        _NormalBSpeed   ("Normal B Scroll (x,y)", Vector) = (-0.02, 0.00, 0, 0)
        _NormalAStr     ("Normal A Strength", Range(0, 2)) = 0.9
        _NormalBStr     ("Normal B Strength", Range(0, 2)) = 1.2
        _NormalStrength   ("Normal Blend Strength", Range(0,2)) = 1
        _NormalFadeStart  ("Normal Fade Start (m)", Float)      = 5
        _NormalFadeEnd    ("Normal Fade End (m)", Float)        = 150
        _NormalViewAtten  ("Normal View Attenuation", Range(0,1)) = 0.5
        _NormalSlopeAtten ("Normal Slope Attenuation", Range(0,1)) = 0.5

        _SpecularColor    ("Specular Color", Color)             = (1,1,1,1)
        _SpecularStrength ("Specular Strength", Range(0,4))     = 1
        _FresnelPower     ("Fresnel Power", Range(0.5,8))       = 3
        _FresnelBoost     ("Fresnel Boost", Range(0,2))         = 1

        _Gloss          ("Smoothness", Range(0,1))     = 0.85
        _Metallic       ("Metallic", Range(0,1))       = 0.05
        _FoamColor      ("Foam Color", Color)          = (1,1,1,1)
        _FoamTex        ("Foam Texture", 2D) = "white" {}
        _FoamTiling     ("Foam Tiling", Vector)        = (0.1,0.1,0,0)
        _FoamCutoff     ("Foam Cutoff", Range(0,1))    = 0.45
        _FoamStrength   ("Foam Strength", Range(0,3))  = 1.0
        _FoamEnabled   ("Foam Enabled", Float)            = 1
        _FoamAmount    ("Foam Amount", Range(0,3))        = 1
        _FoamCurvAmt   ("Foam Curvature Amount", Range(0,3)) = 1
        _FoamSharp     ("Foam Sharpness", Range(0.5,4))   = 2
        _FoamHeightBias("Foam Height Bias", Range(0,1))   = 0.3
        _FoamSpeed     ("Foam Scroll (x,y)", Vector)      = (0.05, 0.02, 0, 0)

        _Transparency   ("Transparency", Range(0,1))   = 0.85

        // --- Polygon ripple controls (world XZ) ---
        _PolyRippleSpeed     ("Poly Ripple Speed (units/s)", Float) = 1.0
        _PolyRippleWidth     ("Poly Ripple Band Width", Range(0.0, 1.0)) = 0.15
        _PolyRippleRepeat    ("Poly Ripple Wavelength", Float) = 2.0
        _PolyRippleIntensity ("Poly Ripple Intensity", Float) = 1.0
        _PolyRippleDecay    ("Poly Ripple Decay (1/unit)", Float) = 0.6
        _PolyRippleMaxDist  ("Poly Ripple Max Distance (0=off)", Float) = 8.0

        [HideInInspector]_PolyCount ("Polygon Count", Int) = 0
        [HideInInspector]_PolyTime  ("Poly Time", Float)   = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 300

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            int _WaveCount;
            float4 _DirAmpSteep[32]; // (Dx, Dz, A, S)
            float4 _WlOmegaPad[32];  // (wavelength, omega, _, _)
            float2 _UVOffset;
            float3 _OceanOrigin;
            float  _TimeSeconds;

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

            // --- Existing point contact inputs ---
            #define MAX_CONTACTS 64
            int    _ContactCount;
            float4 _ContactPoints[MAX_CONTACTS]; // (x=worldX, y=worldZ, z=radius, w=strength)
            float  _ContactFoamAmount;           // overall intensity
            float4 _ContactRipple;               // (frequency, speed, decay, strength)

            // --- NEW: Polygon inputs (world XZ) ---
            // Keep modest upper bound for mobile friendliness
            #define MAX_POLY 256
            int   _PolyCount;
            float _PolyTime;
            float4 _Poly[MAX_POLY]; // use .xy as (worldX, worldZ)

            // NEW: Polygon ripple controls
            float _PolyRippleSpeed;
            float _PolyRippleWidth;
            float _PolyRippleRepeat;
            float _PolyRippleIntensity;
            float _PolyRippleDecay;
            float _PolyRippleMaxDist;


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

            float smoothWider(float a, float b, float x, float w)
            {
                return smoothstep(a - w, b + w, x);
            }

            float3 GreenRamp(float h, float softness)
            {
                float w = lerp(0.02, 0.10, saturate(softness)); // widen blends with softness
                float k0=0.00, k1=0.25, k2=0.45, k3=0.65, k4=0.82, k5=0.95;

                if (h < k1) { float t = smoothWider(k0, k1, h, w); return lerp(_AbyssColor.rgb,   _DeepColor.rgb,    t); }
                if (h < k2) { float t = smoothWider(k1, k2, h, w); return lerp(_DeepColor.rgb,    _MidColor.rgb,     t); }
                if (h < k3) { float t = smoothWider(k2, k3, h, w); return lerp(_MidColor.rgb,     _JadeColor.rgb,    t); }
                if (h < k4) { float t = smoothWider(k3, k4, h, w); return lerp(_JadeColor.rgb,    _EmeraldColor.rgb, t); }
                if (h < k5) { float t = smoothWider(k4, k5, h, w); return lerp(_EmeraldColor.rgb, _ShallowColor.rgb, t); }
                return _ShallowColor.rgb;
            }

            // ---------- NEW: Polygon SDF helpers (world XZ) ----------
            float2 ClosestPointOnSegment(float2 a, float2 b, float2 p)
            {
                float2 ab = b - a;
                float t = dot(p - a, ab) / max(dot(ab, ab), 1e-6);
                t = saturate(t);
                return a + t * ab;
            }

            float DistToPolyline(float2 p, int count)
            {
                if (count <= 0) return 1e6;
                float d = 1e6;
                [loop]
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    float2 a = _Poly[i].xy;
                    float2 b = _Poly[j].xy;
                    float2 c = ClosestPointOnSegment(a, b, p);
                    d = min(d, length(p - c));
                }
                return d;
            }

            bool PointInPolygon(float2 p, int count)
            {
                bool inside = false;
                [loop]
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    float2 pi = _Poly[i].xy;
                    float2 pj = _Poly[j].xy;
                    bool cond = ((pi.y > p.y) != (pj.y > p.y)) &&
                                (p.x < (pj.x - pi.x) * (p.y - pi.y) / max(pj.y - pi.y, 1e-6) + pi.x);
                    inside = cond ? !inside : inside;
                }
                return inside;
            }

            // Signed distance: negative inside, positive outside
            float SdPolygon(float2 p, int count)
            {
                float d = DistToPolyline(p, count);
                return PointInPolygon(p, count) ? -d : d;
            }

            // Band-limited ripple along SDF
            float PolygonRipple(float sd, float t, float speed, float repeat, float width)
            {
                // ripple travels outward from boundary; abs(sd) for symmetric inside/outside bands
                float phase = (abs(sd) - speed * t) / max(repeat, 1e-6);
                float tri = 1.0 - abs(frac(phase) * 2.0 - 1.0);    // [0,1]
                float band = smoothstep(0.5, 0.5 - width, tri);    // thin band
                return band;
            }

            // Distance-based attenuation: exponential + gentle fade to 0 near MaxDist
            float PolyRippleAtten(float sd, float decay, float maxDist)
            {
                float d = abs(sd);
                // Exponential rolloff
                float a = exp(-d * max(decay, 1e-6));
                // Optional outer fade-to-zero (0 disables)
                if (maxDist > 0.0)
                {
                    // start fading at 80% of maxDist to avoid a hard edge
                    a *= smoothstep(maxDist, maxDist * 0.8, d);
                }
                return a;
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float2 xz = wp.xz - _OceanOrigin.xz;

                float3 disp = 0;
                float3 dPdX = float3(1,0,0);
                float3 dPdZ = float3(0,0,1);

                [loop]
                for (int i=0;i<_WaveCount;i++)
                {
                    float2 D = normalize(_DirAmpSteep[i].xy);
                    float  A = _DirAmpSteep[i].z;
                    float  S = _DirAmpSteep[i].w;
                    float  wl = _WlOmegaPad[i].x;
                    float  w  = _WlOmegaPad[i].y;
                    float  phi = _WlOmegaPad[i].z;

                    float k = 2.0 * PI / max(0.001, wl);
                    float phase = k * dot(D, xz) - w * _TimeSeconds + phi;
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

                float halfRange = max(1e-3, _HeightRange * 0.5);
                float h01 = saturate(0.5 + (IN.worldPos.y - _SeaLevel) / (2.0 * halfRange) + _RampShift);
                float3 col = GreenRamp(h01, saturate(_RampSoftness));

                col = col / (1.0 + col * 0.25);

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

                float camDist = distance(GetCameraPositionWS(), IN.worldPos);
                float distFade = saturate( (_NormalFadeEnd - camDist) / max(1e-3, _NormalFadeEnd - _NormalFadeStart) );
                float viewDot = saturate(dot(baseN, V));
                float viewAtten = lerp(1.0, viewDot*viewDot, _NormalViewAtten);
                float slopeBase = 1.0 - saturate(dot(baseN, up));
                float slopeAtten = lerp(1.0, slopeBase, _NormalSlopeAtten);
                float normalMix = saturate(_NormalStrength * distFade * viewAtten * slopeAtten);

                float3 N = normalize(lerp(baseN, combined, normalMix));

                Light Lm = GetMainLight();
                float3 L = normalize(Lm.direction);
                float3 H = SafeNormalize(L + V);
                float NdotL = saturate(dot(N, L));
                float spec = pow(saturate(dot(N, H)), lerp(8.0,128.0,_Smoothness)) * NdotL;
                col = col + _SpecularColor.rgb * spec * _SpecularStrength;

                float fres = pow(saturate(1 - dot(N, V)), _FresnelPower) * _FresnelBoost;
                col = lerp(col, _RimColor.rgb, saturate(fres));

                float slopeCrest = pow(1.0 - saturate(dot(N, up)), _FoamSharp);
                float curv = (length(ddx(N)) + length(ddy(N)));
                curv = saturate(curv * 0.75);
                float peak = smoothstep(1.0 - _FoamHeightBias, 1.0, h01);

                float2 fuv = IN.worldPos.xz * _FoamTiling.xy + _FoamSpeed.xy * t;
                float noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv).r;

                float foamMask = _FoamEnabled * (slopeCrest + curv * _FoamCurvAmt) * (0.6 + 0.4*noise) * peak * _FoamAmount;
                foamMask = saturate(foamMask);

                // ----------------- CONTACT FOAM -----------------
                // A) NEW polygon-driven ripple/foam (preferred if polygon provided)
                float polyFoam = 0.0;
                if (_PolyCount >= 3)
                {
                    float2 p = IN.worldPos.xz;
                    float sd = SdPolygon(p, _PolyCount); // signed distance, <0 inside
                    float band = PolygonRipple(sd, _PolyTime, _PolyRippleSpeed, _PolyRippleRepeat, _PolyRippleWidth);
                    float atten = PolyRippleAtten(sd, _PolyRippleDecay, _PolyRippleMaxDist);
                    polyFoam = saturate(band * atten * _PolyRippleIntensity);

                }

                // B) Legacy point emitters (fallback if no polygon)
                float contactFoam = 0.0;
                if (_PolyCount < 3 && _ContactCount > 0)
                {
                    [loop]
                    for (int i = 0; i < _ContactCount; i++)
                    {
                        float2 center = _ContactPoints[i].xy;
                        float  radius = max(1e-4, _ContactPoints[i].z);
                        float  strength = _ContactPoints[i].w;

                        float2 d = (IN.worldPos.xz - center) / radius;
                        float  dist = length(d);

                        float edge = 1.0 - saturate(dist);
                        edge = edge * edge;
                        float localFoam = edge * strength;

                        float freq   = _ContactRipple.x;
                        float speed  = _ContactRipple.y;
                        float decay  = _ContactRipple.z;
                        float amp    = _ContactRipple.w;
                        float ripple = sin(dist * freq - _TimeSeconds * speed) * exp(-dist * decay) * amp;

                        contactFoam = max(contactFoam, localFoam + ripple);
                    }
                }

                // Combine: polygon preferred, else points; also blend in your base foam if desired
                float contactMask = max(polyFoam, contactFoam);
                // Tint toward white based on contact foam amount + base foam (optional weighting)
                float combinedFoam = saturate(contactMask * _ContactFoamAmount);
                col = lerp(col, float3(1,1,1), combinedFoam);

                // If you want to also include your normal foam field:
                // col = lerp(col, _FoamColor.rgb, foamMask * _FoamStrength);

                float aOut = saturate(_Transparency);

                return float4(col, aOut);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
