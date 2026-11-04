// Les marques des tirs sur le décor (ShotImpacts, 2026-11-04) : trous de balle et brûlures, en un seul maillage.
//
// La texture ne se pose pas sur le décor, elle le module (son rouge) : 0,5 ne change rien, au-dessous elle assombrit, au-dessus
// elle éclaircit (Blend DstColor SrcColor : le double du produit). Sans lumière à calculer, une marque prend celle de la surface
// qu'elle touche, dans la pénombre comme sous les projecteurs, et la brume et le fond sombre de la kill cam avec. La couleur du
// sommet dose la marque (son alpha : 0 l'efface) : les plus anciennes s'effacent avant de céder leur place.
//
// Une multiplication ne se voit pas sur un mur noir, or la moitié basse des murs l'est : une seconde passe ajoute un rien de
// lumière (le vert de la texture, ×_Eclat) à la lèvre d'un trou et à la cendre d'une brûlure.
//
// Une marque se pose sur le collider, et ce qu'on voit le dépasse un peu (1,5 cm pour les panneaux acoustiques du stand) : elle
// avance vers l'œil de _Avance mètres, le long du regard, ce qui la garde devant sans la décaler à l'écran.
Shader "DuelArena/MarqueImpact"
{
    Properties
    {
        _BaseMap ("Marques (linéaire ; rouge : 0,5 neutre ; vert : ce qui luit)", 2D) = "gray" {}
        _Avance ("Avance vers l'œil (m)", Float) = 0.03
        _Eclat ("Éclat ajouté aux bords", Float) = 0.025
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _Avance;
            float _Eclat;
        CBUFFER_END

        // Le fond sombre de la kill cam : ce qu'on ajoute s'y éteint avec le décor.
        float _ReplayDim;

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
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS + GetWorldSpaceNormalizeViewDir(positionWS) * _Avance);
            output.uv = input.uv;
            output.color = input.color;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "MarqueImpact"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor SrcColor
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                half value = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).r;
                return half4(lerp(0.5h, value, input.color.a).xxx, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MarqueImpactBords"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                half value = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).g;
                return half4((value * input.color.a * (half)_Eclat * (1.0h - (half)_ReplayDim)).xxx, 0.0h);
            }
            ENDHLSL
        }
    }
}
