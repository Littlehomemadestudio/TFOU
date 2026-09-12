Shader "Custom/AutoTerrainLit"
{
    // Lightweight vertex-colored terrain/prop shader for the generated test map.
    // Lambert + SH ambient + emission (for glowing buoys) + fog. No textures.
    Properties
    {
        _BaseColor("Base Color (tint)", Color) = (1, 1, 1, 1)
        _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        _EmissionStrength("Emission Strength", Range(0, 10)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "TerrainForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                float _EmissionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                half4  color       : COLOR;
                float  fogFactor   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight();

                float ndl = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(N);

                half3 albedo = IN.color.rgb * _BaseColor.rgb;
                half3 col = albedo * (ambient * 0.9 + mainLight.color * ndl * 1.05);
                col += _EmissionColor.rgb * _EmissionStrength;

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
