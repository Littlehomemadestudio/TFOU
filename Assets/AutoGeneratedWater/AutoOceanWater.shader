Shader "Custom/AutoOceanWater"
{
    Properties
    {
        _DeepColor("Deep Color", Color) = (0.008, 0.09, 0.16, 1)
        _CrestColor("Crest Color", Color) = (0.02, 0.22, 0.30, 1)
        _FoamColor("Foam Color", Color) = (0.92, 0.97, 1, 1)
        _HorizonSkyColor("Horizon Sky Color", Color) = (0.45, 0.62, 0.72, 1)
        _ZenithSkyColor("Zenith Sky Color", Color) = (0.10, 0.28, 0.55, 1)
        _SubsurfaceColor("Subsurface Color", Color) = (0.05, 0.45, 0.42, 1)
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _ReflectionStrength("Reflection Strength", Range(0, 2)) = 1
        _SunSpecPower("Sun Specular Power", Range(8, 2048)) = 256
        _SunSpecIntensity("Sun Specular Intensity", Range(0, 8)) = 2
        _DetailStrength("Detail Normal Strength", Range(0, 2)) = 0.5
        _DetailScale("Detail Scale", Range(0.01, 4)) = 0.35
        _DetailSpeed("Detail Speed", Range(0, 4)) = 1
        _FoamThreshold("Crest Foam Threshold", Range(0, 1)) = 0.55
        _FoamAmount("Foam Amount", Range(0, 3)) = 1.2
        _HullFoamStrength("Hull Foam Strength", Range(0, 3)) = 1.4
        _HullFoamRadius("Hull Foam Radius", Range(1, 300)) = 40
        _WakeStrength("Wake Strength", Range(0, 3)) = 1.2
        _WakeLength("Wake Length", Range(10, 800)) = 220
        _WakeWidth("Wake Width", Range(1, 100)) = 18
        _SubsurfaceStrength("Subsurface Strength", Range(0, 3)) = 0.5
        _DetailFadeDistance("Detail Fade Distance", Range(50, 5000)) = 1200
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half4 _HorizonSkyColor;
                half4 _ZenithSkyColor;
                half4 _SubsurfaceColor;
                float _FresnelPower;
                half _ReflectionStrength;
                float _SunSpecPower;
                half _SunSpecIntensity;
                half _DetailStrength;
                half _DetailScale;
                half _DetailSpeed;
                half _FoamThreshold;
                half _FoamAmount;
                half _HullFoamStrength;
                half _HullFoamRadius;
                half _WakeStrength;
                half _WakeLength;
                half _WakeWidth;
                half _SubsurfaceStrength;
                half _DetailFadeDistance;
                float4 _ShipData;
                float4 _ShipForward;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 crest : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            float DetailH(float2 p, float t)
            {
                float h = sin(p.x * 1.0 + t * 1.7) * 0.50;
                h += sin(p.y * 1.3 - t * 1.1) * 0.40;
                h += sin((p.x + p.y) * 0.7 + t * 2.3) * 0.30;
                h += sin((p.x - p.y) * 2.1 - t * 1.9) * 0.20;
                h += sin(p.x * 3.9 + p.y * 3.1 + t * 3.3) * 0.10;
                return h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.crest = v.color.rg;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 positionWS = i.positionWS;
                float3 camPos = GetCameraPositionWS();
                float3 V = normalize(camPos - positionWS);

                float dist = distance(camPos, positionWS);
                float detailFade = saturate(1.0 - dist / max(_DetailFadeDistance, 1.0));

                float2 p = positionWS.xz * _DetailScale;
                float t = _Time.y * _DetailSpeed;

                float e = 0.4;
                float h0 = DetailH(p, t);
                float hx = DetailH(p + float2(e, 0.0), t);
                float hz = DetailH(p + float2(0.0, e), t);
                float3 detailN = normalize(float3(-(hx - h0) / e, 1.0, -(hz - h0) / e));

                float strength = _DetailStrength * (0.25 + 0.75 * detailFade);
                float3 N = normalize(float3(detailN.xz * strength, 1.0));

                float ndv = saturate(dot(N, V));
                float fresnel = pow(1.0 - ndv, _FresnelPower);

                float3 R = reflect(-V, N);
                float skyT = saturate(R.y * 1.6 + 0.08);
                half3 skyCol = lerp(_HorizonSkyColor.rgb, _ZenithSkyColor.rgb, skyT);

                Light mainLight = GetMainLight();
                half3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), _SunSpecPower) * _SunSpecIntensity;

                half3 waterCol = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(i.crest.g));

                float sss = pow(saturate(dot(V, -mainLight.direction)), 3.0) * saturate(i.crest.g) * _SubsurfaceStrength;
                waterCol += _SubsurfaceColor.rgb * sss;

                half3 col = lerp(waterCol, skyCol, saturate(fresnel * _ReflectionStrength));
                col += mainLight.color * spec;

                float foamNoise = DetailH(p * 2.7 + 13.7, t * 0.55) * 0.5 + 0.5;
                float crestFoam = smoothstep(_FoamThreshold, 1.0, i.crest.r) * _FoamAmount;
                float foam = crestFoam * (0.55 + 0.9 * foamNoise);

                float dShip = distance(positionWS.xz, _ShipData.xz);
                float ring = smoothstep(_HullFoamRadius * 2.1, _HullFoamRadius * 0.75, dShip) * _HullFoamStrength;
                foam += ring * (0.45 + 0.75 * foamNoise);

                float2 fwd = normalize(_ShipForward.xz + 1e-5);
                float2 toP = positionWS.xz - _ShipData.xz;
                float along = clamp(dot(toP, -fwd), 0.0, _WakeLength);
                float2 closest = _ShipData.xz - fwd * along;
                float dWake = distance(positionWS.xz, closest);
                float wakeFade = 1.0 - along / max(_WakeLength, 1.0);
                float wake = smoothstep(_WakeWidth, _WakeWidth * 0.2, dWake) * wakeFade * wakeFade * _WakeStrength * _ShipData.w;
                foam += wake * (0.35 + 0.85 * foamNoise);

                col = lerp(col, _FoamColor.rgb, saturate(foam));

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
