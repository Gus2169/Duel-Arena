// Un texte posé dans le monde (2026-10-28) : le tableau des records de la Fosse (CourseBoard), un TextMesh. Le matériau
// de police d'Unity (GUI/Text Shader) se dessine par-dessus tout : le tableau se voyait à travers les murs de la Fosse.
// Celui-ci teste la profondeur. La couleur vient des sommets (TextMesh.color), la forme des lettres de l'alpha de la
// texture de la police (posée par CourseBoard : une police dynamique change de texture en cours de jeu). Catégorie C.
Shader "DuelArena/TexteMonde"
{
    Properties
    {
        _MainTex ("Texture de la police", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TexteMonde"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half lettre = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return half4(input.color.rgb, input.color.a * lettre);
            }
            ENDHLSL
        }
    }
}
