Shader "Custom/AutoSoftParticle"
{
    // Soft radial billboard shader for the procedurally generated ship FX
    // (bow spray, wake foam, funnel smoke). Pure math - no textures needed.
    Properties
    {
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Falloff("Edge Softness", Range(0.3, 6)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SoftParticle"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Falloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float2 uv          : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;
                float r = saturate(1.0 - length(d) * 2.0);
                float alpha = pow(r, _Falloff);

                // slight interior mottling so blobs read as foam/smoke, not discs
                float mot = 0.82 + 0.18 * sin(d.x * 37.0 + d.y * 21.0);

                half4 col;
                col.rgb = IN.color.rgb * _Color.rgb * mot;
                col.a = IN.color.a * _Color.a * alpha;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
