// Un écran holographique (2026-10-28) : la cible de la salle de précision vue de près, debout sur le comptoir (AimRange,
// demandé par l'utilisateur : « un écran holographique futuriste, un peu comme une télé sur la table »). De la lumière, pas
// une surface : additif, le noir y est transparent (le noir de la cible, le dehors du disque), le reste luit dans la teinte
// froide de la salle, jamais une couleur de robot ; les impacts restent rouges. Des lignes de balayage qui défilent, une
// bande plus claire qui descend, un scintillement à peine visible, un liseré au bord et aux coins.
// _Faisceau = 1 : la lumière qui monte du projecteur à l'écran (pleine au pied, effacée en montant). Catégorie C.
Shader "DuelArena/Hologramme"
{
    Properties
    {
        _BaseMap ("Image", 2D) = "black" {}
        _Couleur ("Teinte", Color) = (0.72, 0.86, 1, 1)
        _Intensite ("Intensite", Float) = 1.25
        _Lignes ("Lignes de balayage (sur la hauteur)", Float) = 90
        _Cadre ("Lisere du bord (part de l'image)", Float) = 0.008
        _Faisceau ("Faisceau du projecteur (0 ou 1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Hologramme"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Couleur;
                half _Intensite;
                half _Lignes;
                half _Cadre;
                half _Faisceau;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 uv = input.uv;
                if (_Faisceau > 0.5)
                {
                    // La lumière du projecteur : pleine au pied (uv.y = 0), effacée en montant, plus dense au milieu.
                    half monte = 1.0 - saturate(uv.y);
                    half milieu = smoothstep(0.0, 0.55, 1.0 - abs(uv.x * 2.0 - 1.0));
                    half voile = monte * monte * milieu * (0.9 + 0.1 * sin(t * 31.0));
                    return half4(_Couleur.rgb * (voile * 0.32 * _Intensite), 0);
                }

                half3 image = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb;
                // Les lignes de balayage, qui défilent vers le haut ; une bande plus claire qui descend toutes les 3 s.
                half lignes = 0.8 + 0.2 * sin((uv.y * _Lignes - t * 4.0) * 6.2831853);
                half bande = 1.0 + 0.35 * smoothstep(0.05, 0.0, abs(frac(1.0 - t * 0.33) - uv.y));
                // Un scintillement à peine visible.
                half scintille = 0.95 + 0.05 * sin(t * 37.0) * sin(t * 23.3);
                // Le liseré du bord, plus vif aux coins (des équerres).
                float2 bord = min(uv, 1.0 - uv);
                half lisere = step(min(bord.x, bord.y), _Cadre);
                half coins = lisere * step(max(bord.x, bord.y), 0.09);
                half3 lumiere = image * _Couleur.rgb * (lignes * bande * scintille) + _Couleur.rgb * (lisere * 0.12 + coins * 0.55);
                return half4(lumiere * _Intensite, 0);
            }
            ENDHLSL
        }
    }
}
