// Les faisceaux des projecteurs de l'arène (2026-10-27) : la lumière des rampes vue dans la brume de la salle.
//
// Un cône ouvert sous chaque projecteur (ArenaLighting.AddBeams), en lumière ajoutée. Il est plus dense au cœur
// qu'aux bords (la normale du maillage est radiale : le rapport entre elle et le regard dessine un volume), il
// s'éteint en descendant (uv.y : 0 contre le verre, 1 au bout), bien au-dessus des robots (la lisibilité d'abord,
// bible § 17), il s'efface de près (on ne le traverse pas en plein) et dans la brume du lointain. Rien ne le lit :
// purement visuel, catégorie C.
Shader "DuelArena/FaisceauProjecteur"
{
    Properties
    {
        _Couleur ("Couleur", Color) = (0.80, 0.88, 1.0, 1.0)
        _Intensite ("Intensité", Float) = 0.05
        _Proche ("Distance sur laquelle il s'efface de près (m)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FaisceauProjecteur"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Couleur;
                float _Intensite;
                float _Proche;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 vue = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float face = abs(dot(normalize(input.normalWS), vue));   // 1 au cœur, 0 sur la silhouette
                float t = input.uv.y;
                float descente = (1.0 - t) * (1.0 - t);
                float verre = smoothstep(0.0, 0.05, t);                  // pas d'arête nette contre le verre
                float proche = saturate((distance(input.positionWS, GetCameraPositionWS()) - 0.5) / _Proche);
                half3 couleur = _Couleur.rgb * (_Intensite * face * face * descente * verre * proche);
                couleur = MixFogColor(couleur, half3(0.0, 0.0, 0.0), input.fog);   // lumière ajoutée : la brume l'éteint
                return half4(couleur, 0.0);
            }
            ENDHLSL
        }
    }
}
