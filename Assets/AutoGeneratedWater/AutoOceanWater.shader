Shader "Custom/AutoOceanWater"
{
    // =====================================================================
    //  TFOU AAA Ocean (URP)
    //  - 8-wave Gerstner field displaced in the VERTEX shader (CPU never
    //    touches mesh data; buoyancy samples the identical field in C#)
    //  - Analytic normals + Jacobian-based whitecaps
    //  - Kelvin-angle ship wake + hull waterline foam + contact foam
    //  - Beer-Lambert depth absorption, hull refraction via opaque texture
    //  - Procedural sky reflection w/ HDR sun disk, optional planar
    //    reflection blend, Schlick Fresnel, two-lobe sun specular + glitter
    //  - Subsurface scattering on backlit crests
    //  - Three rings (near/mid/horizon) share this shader; vertex-color
    //    alpha carries the radial detail weight so waves die smoothly at
    //    ring borders (no seams, correct LOD for cell density)
    // =====================================================================
    Properties
    {
        [Header(Water Body)]
        _DeepColor("Deep Color", Color) = (0.008, 0.09, 0.16, 1)
        _CrestColor("Shallow / Crest Color", Color) = (0.02, 0.22, 0.30, 1)
        _AbsorptionDepth("Depth Fade Distance (m)", Range(0.5, 80)) = 14
        _RefractionStrength("Refraction Distortion", Range(0, 2)) = 0.5

        [Header(Sky And Reflection)]
        _HorizonSkyColor("Horizon Sky Color", Color) = (0.45, 0.62, 0.72, 1)
        _ZenithSkyColor("Zenith Sky Color", Color) = (0.10, 0.28, 0.55, 1)
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _ReflectionStrength("Reflection Strength", Range(0, 2)) = 1

        [Header(Sun)]
        _SunSpecPower("Sun Specular Power", Range(8, 2048)) = 256
        _SunSpecIntensity("Sun Specular Intensity", Range(0, 8)) = 2
        _GlitterAmount("Sun Glitter", Range(0, 2)) = 0.7

        [Header(Subsurface)]
        _SubsurfaceColor("Subsurface Color", Color) = (0.05, 0.45, 0.42, 1)
        _SubsurfaceStrength("Subsurface Strength", Range(0, 3)) = 0.5
        _SSSPower("Subsurface Sharpness", Range(1, 16)) = 6

        [Header(Micro Detail Normals)]
        _DetailStrength("Detail Normal Strength", Range(0, 2)) = 0.5
        _DetailScale("Detail Scale", Range(0.01, 4)) = 0.35
        _DetailSpeed("Detail Speed", Range(0, 4)) = 1
        _DetailFadeDistance("Detail Fade Distance", Range(50, 5000)) = 1200

        [Header(Foam)]
        _FoamColor("Foam Color", Color) = (0.92, 0.97, 1, 1)
        _FoamThreshold("Whitecap Threshold", Range(0, 1)) = 0.55
        _FoamAmount("Whitecap Amount", Range(0, 3)) = 1.2
        _FoamScale("Foam Noise Scale", Range(0.01, 1)) = 0.09
        _FoamScroll("Foam Scroll Speed", Range(0, 4)) = 0.35
        _ContactFoamStrength("Contact Foam Strength", Range(0, 3)) = 1.3
        _ContactFoamRange("Contact Foam Range (m)", Range(0.1, 6)) = 1.4

        [Header(Ship Interaction)]
        _HullFoamStrength("Hull Foam Strength", Range(0, 3)) = 1.4
        _HullFoamRadius("Hull Foam Radius", Range(1, 300)) = 40
        _WakeStrength("Wake Strength", Range(0, 3)) = 1.2
        _WakeLength("Wake Length", Range(10, 800)) = 220
        _WakeWidth("Wake Width", Range(1, 100)) = 18
        [HideInInspector] _ShipData("Ship Data", Vector) = (0, 0, 20, 0)
        [HideInInspector] _ShipForward("Ship Forward", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }

            // Water renders after opaques (so the depth/opaque textures contain
            // ships & terrain), still writes depth, and overwrites (alpha is 1).
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex OceanVert
            #pragma fragment OceanFrag
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _REFRACTION_ON
            #pragma multi_compile_local _ _PLANAR_REFLECTION_ON
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #if defined(_REFRACTION_ON)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half4 _HorizonSkyColor;
                half4 _ZenithSkyColor;
                half4 _SubsurfaceColor;
                float4 _ShipData;     // x, z, hullRadius, speed01
                float4 _ShipForward;  // x, z, -, -
                float _AbsorptionDepth;
                float _RefractionStrength;
                float _FresnelPower;
                float _ReflectionStrength;
                float _SunSpecPower;
                float _SunSpecIntensity;
                float _GlitterAmount;
                float _SubsurfaceStrength;
                float _SSSPower;
                float _DetailStrength;
                float _DetailScale;
                float _DetailSpeed;
                float _DetailFadeDistance;
                float _FoamThreshold;
                float _FoamAmount;
                float _FoamScale;
                float _FoamScroll;
                float _ContactFoamStrength;
                float _ContactFoamRange;
                float _HullFoamStrength;
                float _HullFoamRadius;
                float _WakeStrength;
                float _WakeLength;
                float _WakeWidth;
            CBUFFER_END

            // ---- Globals pushed from C# (OceanWaves / master_ship) ----
            float4 _GerstA[8];   // dirX, dirZ, wavenumber k, amplitude
            float4 _GerstB[8];   // omega, steepness Q, detailClass, reserved
            float4 _OceanTime;   // t, windDirX, windDirZ, seaState01
            float _PlanarReflectionStrength;
            TEXTURE2D(_PlanarReflectionTexture);
            SAMPLER(sampler_PlanarReflectionTexture);

            // ===================== procedural noise =====================

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float FBM4(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                const float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
                for (int i = 0; i < 4; i++)
                {
                    v += a * VNoise(p);
                    p = mul(rot, p) * 2.03;
                    a *= 0.5;
                }
                return v;
            }

            // ===================== gerstner field =====================
            // Identical math runs in OceanWaves.cs (CPU) for buoyancy.

            void ApplyGerstner(float3 posWS, float detail, out float3 disp, out float3 nrm, out float jac)
            {
                disp = float3(0.0, 0.0, 0.0);
                nrm = float3(0.0, 1.0, 0.0);
                float jxx = 1.0, jzz = 1.0, jxz = 0.0;
                float t = _OceanTime.x;

                UNITY_UNROLL
                for (int i = 0; i < 8; i++)
                {
                    float4 A = _GerstA[i];
                    float4 B = _GerstB[i];

                    // detail: vertex-color radial weight. detailClass: 0 = long
                    // swell (always), 0.5 = mid band, 1 = short chop (near ring only).
                    // The 0.75/0.25 ramp keeps every class fading smoothly - no rings
                    // of popping chop at the fade boundary.
                    float cls = B.z;
                    float w = saturate((detail - cls * 0.75) / 0.25);
                    float amp = A.w * w;

                    float phase = A.z * dot(A.xy, posWS.xz) - B.x * t;
                    float s, c;
                    sincos(phase, s, c);

                    float qa = B.y * amp;
                    disp.x += A.x * qa * c;
                    disp.z += A.y * qa * c;
                    disp.y += amp * s;

                    float ka = A.z * amp;
                    nrm.x -= A.x * ka * c;
                    nrm.z -= A.y * ka * c;
                    nrm.y -= B.y * ka * s;

                    float qs = B.y * ka * s;
                    jxx -= qs * A.x * A.x;
                    jzz -= qs * A.y * A.y;
                    jxz -= qs * A.x * A.y;
                }

                jac = jxx * jzz - jxz * jxz;
                nrm.y = max(nrm.y, 0.15);
                nrm = normalize(nrm);
            }

            // ===================== procedural sky =====================

            half3 ProceduralSky(float3 dir, float3 sunDir, half3 sunCol)
            {
                float h = saturate(dir.y);
                half3 sky = lerp(_HorizonSkyColor.rgb, _ZenithSkyColor.rgb, pow(h, 0.55));

                float sd = max(dot(dir, sunDir), 0.0);
                sky += _HorizonSkyColor.rgb * pow(sd, 6.0) * 0.40;   // warm sun-side glow
                sky += sunCol * pow(sd, 700.0) * 30.0;               // HDR disk -> feeds bloom
                sky += sunCol * pow(sd, 90.0) * 0.70;                // corona

                sky *= lerp(0.78, 1.0, saturate(dir.y * 8.0 + 0.5)); // darken below horizon
                return sky;
            }

            // ===================== vertex =====================

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float  viewDepth   : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
                float  waveHeight  : TEXCOORD5;
                float  jacobian    : TEXCOORD6;
                half   detail      : TEXCOORD7;
            };

            Varyings OceanVert(Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float detail = IN.color.a;

                float3 disp, nrm;
                float jac;
                ApplyGerstner(posWS, detail, disp, nrm, jac);
                posWS += disp;

                VertexPositionInputs vpi = GetVertexPositionInputs(posWS);

                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS = posWS;
                OUT.normalWS = nrm;
                OUT.screenPos = vpi.positionNDC;
                OUT.viewDepth = -vpi.positionVS.z;
                OUT.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                OUT.waveHeight = disp.y;
                OUT.jacobian = jac;
                OUT.detail = detail;
                return OUT;
            }

            // ===================== fragment =====================

            half4 OceanFrag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                Light mainLight = GetMainLight();
                float3 sunDir = mainLight.direction;
                half3 sunCol = mainLight.color;

                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float3 N = IN.normalWS;

                float2 wind = normalize(_OceanTime.yz + float2(1e-4, 1e-4));

                // ---- water column depth (scene depth excludes water: transparent queue) ----
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float waterDepth = max(sceneEye - IN.viewDepth, 0.0);

                // ---- LOD fades ----
                float detailFade = saturate(IN.detail);
                float dist = distance(_WorldSpaceCameraPos, IN.positionWS);
                float distFade = 1.0 - saturate(dist / max(_DetailFadeDistance, 1.0));
                float micro = detailFade * distFade;

                // ---- wind-aligned micro detail normals (2 octaves, analytic-ish) ----
                {
                    float e = 0.4;
                    float scroll = _OceanTime.x * _DetailSpeed * 1.7;
                    float2 p1 = IN.positionWS.xz * _DetailScale * 6.0 + wind * scroll;
                    float2 p2 = IN.positionWS.xz * _DetailScale * 13.8 - wind * scroll * 1.31 + 17.7;

                    float n1a = VNoise(p1 + float2(e, 0.0)) - VNoise(p1 - float2(e, 0.0));
                    float n1b = VNoise(p1 + float2(0.0, e)) - VNoise(p1 - float2(0.0, e));
                    float n2a = VNoise(p2 + float2(e, 0.0)) - VNoise(p2 - float2(e, 0.0));
                    float n2b = VNoise(p2 + float2(0.0, e)) - VNoise(p2 - float2(0.0, e));

                    float2 grad = (float2(n1a, n1b) * 0.65 + float2(n2a, n2b) * 0.35) / (2.0 * e);
                    N = normalize(N + float3(-grad.x, 0.0, -grad.y) * _DetailStrength * micro * 1.6);
                }

                // ---- base body color: depth absorption gradient ----
                float depthT = saturate(waterDepth / max(_AbsorptionDepth, 0.01));
                half3 baseCol = lerp(_CrestColor.rgb, _DeepColor.rgb, depthT);

                float crestT = smoothstep(0.15, 1.6, IN.waveHeight) * micro;
                baseCol = lerp(baseCol, _CrestColor.rgb * 1.35, crestT * 0.45);

                // ---- refraction: what lies under the surface, absorbed by depth ----
                #if defined(_REFRACTION_ON)
                {
                    float2 refrUV = screenUV + N.xz * _RefractionStrength * 0.07 * saturate(waterDepth * 0.6);
                    half3 refr = SampleSceneColor(refrUV);
                    half3 absorp = exp(-waterDepth * half3(0.50, 0.17, 0.14));
                    refr *= absorp;
                    float refrMix = 1.0 - saturate(waterDepth / max(_AbsorptionDepth * 0.5, 0.01));
                    baseCol = lerp(baseCol, refr, refrMix * 0.85);
                }
                #endif

                // ---- sky / planar reflection ----
                float3 R = reflect(-V, N);
                half3 skyRefl = ProceduralSky(R, sunDir, sunCol);

                #if defined(_PLANAR_REFLECTION_ON)
                {
                    half3 planar = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, screenUV).rgb;
                    skyRefl = lerp(skyRefl, planar, saturate(_PlanarReflectionStrength) * smoothstep(-0.02, 0.22, R.y));
                }
                #endif

                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, max(_FresnelPower, 0.5));
                float reflAmt = saturate((0.02 + 0.98 * fres) * _ReflectionStrength);

                half3 col = lerp(baseCol, skyRefl, reflAmt);

                // ---- sun specular: tight glint + broad sheen + glitter ----
                float3 H = normalize(V + sunDir);
                float ndh = saturate(dot(N, H));
                float glitter = 0.75 + 0.5 * VNoise(IN.positionWS.xz * 1.9 + _OceanTime.x * 0.5);
                float spec = pow(ndh, max(_SunSpecPower, 8.0)) * _SunSpecIntensity * glitter;
                spec += pow(ndh, 48.0) * 0.12;
                spec *= lerp(1.0, saturate(0.5 + _GlitterAmount), micro);
                col += sunCol * spec;

                // ---- subsurface scattering on backlit crests ----
                float sssDir = pow(saturate(dot(V, normalize(-sunDir + N * 0.45))), max(_SSSPower, 1.0));
                float sssH = saturate(IN.waveHeight * 0.55 + 0.45);
                col += _SubsurfaceColor.rgb * sunCol * sssDir * sssH * _SubsurfaceStrength * micro;

                // ---- foam ----
                float2 foamUV = IN.positionWS.xz * _FoamScale;
                float fbm = FBM4(foamUV + wind * _OceanTime.x * _FoamScroll * 0.35);

                // whitecaps from the Gerstner Jacobian (surface compression)
                float crestAmt = saturate(1.0 - IN.jacobian);
                float thresh = clamp(_FoamThreshold - _OceanTime.w * 0.12, 0.05, 0.95);
                float whitecap = smoothstep(thresh, thresh + 0.35, crestAmt) * _FoamAmount * (0.30 + 1.40 * fbm) * micro;

                // contact foam where water meets any geometry (hull, rocks, shore)
                float contactEdge = 1.0 - saturate(waterDepth / max(_ContactFoamRange, 0.01));
                float contactFoam = contactEdge * contactEdge * (0.45 + 0.90 * fbm) * _ContactFoamStrength;

                // Kelvin wake + hull waterline foam
                float wake = 0.0;
                float2 toP = IN.positionWS.xz - _ShipData.xz;
                float2 fwd = normalize(_ShipForward.xz + float2(1e-4, 1e-4));
                float2 rgt = float2(-fwd.y, fwd.x);
                float along = dot(toP, -fwd);
                float lateral = abs(dot(toP, rgt));
                if (along > 0.0)
                {
                    float len = max(_WakeLength, 1.0);
                    float wid = max(_WakeWidth, 0.5);
                    float decay = exp(-along / len);

                    // two arms at the Kelvin angle (~19.47 deg => tan = 0.354)
                    float armCenter = along * 0.354;
                    float armWidth = wid * (0.35 + along * 0.012);
                    float armT = (lateral - armCenter) / armWidth;
                    float arm = exp(-armT * armT);

                    // turbulent center band
                    float coreT = lateral / (wid * 0.55);
                    float core = exp(-coreT * coreT) * exp(-along / (len * 0.30));

                    float wobble = 0.65 + 0.70 * fbm;
                    wake = (arm * 0.90 * wobble + core * 1.10) * decay * _WakeStrength * _ShipData.w;
                }

                float dHull = length(toP);
                float hullFoam = smoothstep(_HullFoamRadius, _HullFoamRadius * 0.45, dHull)
                               * _HullFoamStrength * (0.25 + 0.85 * fbm);

                float foam = saturate(whitecap + contactFoam + wake + hullFoam);
                half3 foamCol = _FoamColor.rgb * (0.55 + 0.65 * fbm);
                foamCol += sunCol * pow(ndh, 64.0) * 0.35; // wet sparkle
                foamCol *= lerp(0.85, 1.05, saturate(dot(N, sunDir) * 0.5 + 0.55));
                col = lerp(col, foamCol, foam);

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
