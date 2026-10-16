// La flamme de bouche (2026-10-16, demandée par l'utilisateur à la place du traçant des armes à
// balles) : dessinée par WeaponVisualFeedback.SpawnMuzzleFlash sur un petit maillage, une étoile vue de
// face (couleur des sommets, vert à 0) et deux langues le long du canon (vert à 1, u de la bouche vers
// la pointe, v en travers). L'alpha des sommets règle la force du coup.
//
// Un cœur presque blanc et des bords orangés. Couleur prémultipliée, comme les arcs du gant
// (ArcElectrique) : le jeu n'a pas de bloom, et une lumière seulement ajoutée disparaîtrait sur les
// murs blancs de l'arène. Catégorie C.
Shader "DuelArena/FlammeBouche"
{
    Properties
    {
        _CouleurCoeur ("Cœur", Color) = (1, 0.96, 0.82, 1)
        _CouleurBord ("Bord", Color) = (1, 0.52, 0.12, 1)
        _Branches ("Branches de l'étoile", Float) = 5
        _Intensite ("Intensité", Float) = 1.6
        _Couverture ("Couverture du fond", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FlammeBouche"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CouleurCoeur;
                half4 _CouleurBord;
                float _Branches;
                float _Intensite;
                float _Couverture;
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
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float forme;
                if (input.color.g < 0.5)
                {
                    // L'étoile, vue de face : des branches autour d'un disque.
                    float2 p = input.uv * 2.0 - 1.0;
                    float r = length(p);
                    float angle = atan2(p.y, p.x);
                    float branche = 0.45 + 0.55 * pow(abs(cos(angle * _Branches * 0.5)), 5.0);
                    forme = saturate(1.0 - r / branche);
                }
                else
                {
                    // Une langue le long du canon : large à la bouche, pointue au bout.
                    float le_long = input.uv.x;
                    float travers = abs(input.uv.y * 2.0 - 1.0);
                    float largeur = max(1e-3, 0.15 + 0.85 * (1.0 - le_long));
                    forme = saturate(1.0 - travers / largeur) * (1.0 - le_long);
                }
                float force = input.color.a;
                float couverture = forme * forme;
                half3 couleur = lerp(_CouleurBord.rgb, _CouleurCoeur.rgb, saturate(forme * 1.6)) * (couverture * _Intensite);
                return half4(couleur * force, saturate(couverture * _Couverture) * force);
            }
            ENDHLSL
        }
    }
}
