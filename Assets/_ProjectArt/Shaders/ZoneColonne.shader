// La colonne de lumière de la Zone (2026-11-04, l'utilisateur : « marqueur plus visible, surtout à travers les murs et les
// obstacles »).
//
// Un cylindre ouvert au-dessus de la zone (ZoneLayout), en lumière ajoutée, dessiné par-dessus tout (ZTest Always) : on la voit
// à travers les murs, depuis toute l'arène. Elle est plus dense au cœur qu'aux bords (la normale du maillage est radiale : le
// rapport entre elle et le regard dessine un volume), nulle à son pied, au-dessus de la tête d'un robot qu'elle ne voile jamais,
// et elle s'efface vers le haut (uv.y : 0 au pied, 1 au sommet). Sa couleur vient de ZoneLayout (un bloc de propriétés), comme
// celle de l'anneau. Rien ne la lit : purement visuel, catégorie C.
Shader "DuelArena/ZoneColonne"
{
    Properties
    {
        _BaseColor ("Couleur", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneColonne"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            // La kill cam assombrit le décor (KillCam, global) : la colonne s'éteint d'autant.
            float _ReplayDim;

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
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 vue = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float face = abs(dot(normalize(input.normalWS), vue));   // 1 au cœur, 0 sur la silhouette
                float t = input.uv.y;
                float pied = smoothstep(0.0, 0.12, t);
                float sommet = pow(1.0 - t, 1.4);
                half3 couleur = _BaseColor.rgb * (face * face * pied * sommet);
                return half4(couleur * (1.0 - _ReplayDim), 0.0);
            }
            ENDHLSL
        }
    }
}
