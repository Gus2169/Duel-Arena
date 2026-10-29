// Vitre de viseur avec réticule « à l'infini » (2026-10-09 ; dessiné d'après une image depuis la refonte
// des viseurs, 2026-10-28).
//
// La réticule n'est pas peinte sur la vitre : elle est calculée à partir de la DIRECTION du regard.
// Chaque pixel de la vitre regarde dans une direction ; la réticule s'allume quand cette direction
// tombe à quelques milliradians de l'axe de visée. Elle est donc comme projetée à l'infini, à la
// manière d'un vrai point rouge : elle reste sur la cible quelle que soit la position de l'œil, et
// n'apparaît qu'à travers la vitre, et seulement de dos (vue de face, la vitre n'est qu'un verre teinté).
//
// L'axe de visée a deux modes :
// - celui du viseur (+Z de l'objet, le canon) : pour l'arme vue par les autres ;
// - celui de la caméra (_AxeCamera = 1) : pour la vue subjective. Le tir part de la caméra, donc
//   le réticule doit marquer le centre de l'écran, même quand l'arme balance au rythme des pas.
//   FirstPersonView pose ce mode sur la copie des bras.
//
// Le réticule vient d'une image (_Reticule, faite par Tools/Blender/viseurs.py d'après le même motif que
// ses rendus) : la distance signée au réticule, en milliradians autour du point visé, au centre de
// l'image ; rouge, les traits qui luisent ; vert, les traits gravés, noirs (ceux des lunettes). Codée sur
// 0,5 ± 0,5 pour ±_Plage, l'image couvrant ±_Etendue. Le bord se tire de la distance, net à toute taille :
// une lunette grossit la vue, et le réticule avec elle (ses milliradians sont ceux de la scène).
//
// Unités : milliradians (1 mrad = 1 cm à 10 m). Purement visuel, catégorie C.
Shader "DuelArena/ViseurReticule"
{
    Properties
    {
        _CouleurVitre ("Teinte de la vitre (alpha = opacité)", Color) = (0.45, 0.72, 0.80, 0.10)
        _CouleurReticule ("Couleur de la réticule", Color) = (1.0, 0.16, 0.10, 1.0)
        _Intensite ("Intensité de la réticule", Float) = 4
        [NoScaleOffset] _Reticule ("Image du réticule (distances : rouge lumineux, vert gravé)", 2D) = "white" {}
        _Etendue ("Demi-largeur de l'image (mrad)", Float) = 60
        _Plage ("Distance codée de part et d'autre du bord (mrad)", Float) = 6
        _Douceur ("Douceur minimale du bord (mrad)", Float) = 0.2
        _Reflet ("Reflet sur les bords de la vitre", Range(0, 1)) = 0.35
        [Toggle] _AxeCamera ("Réticule sur l'axe de la caméra (vue subjective)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ViseurReticule"
            Tags { "LightMode" = "UniversalForward" }

            // Couleur prémultipliée : la teinte voile ce qu'il y a derrière, la réticule s'ajoute.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off   // la vitre est un plan : vue de face, elle reste un verre teinté

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CouleurVitre;
                half4 _CouleurReticule;
                float _Intensite;
                float _Etendue;
                float _Plage;
                float _Douceur;
                float _Reflet;
                float _AxeCamera;
            CBUFFER_END
            TEXTURE2D(_Reticule);
            SAMPLER(sampler_Reticule);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Les distances signées (mrad, négatives dans le réticule) aux traits qui luisent (x) et aux
            // traits gravés (y), à l'écart `ecart` (mrad) du point visé. Hors de l'image, loin de tout.
            float2 Distances(float2 ecart)
            {
                float etendue = max(_Etendue, 1e-3);
                float2 uv = 0.5 + ecart / (2.0 * etendue);
                float2 d = (SAMPLE_TEXTURE2D(_Reticule, sampler_Reticule, uv).rg - 0.5) * (2.0 * _Plage);
                float dehors = step(0.5, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
                return lerp(d, float2(_Plage, _Plage), dehors);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 regard = normalize(input.positionWS - GetCameraPositionWS());

                // Le repère de visée : l'axe (avant) et deux directions qui lui sont perpendiculaires.
                float3 avant, droite, haut;
                if (_AxeCamera > 0.5)
                {
                    // Lignes de la matrice de vue : droite, haut, et l'arrière de la caméra.
                    droite = UNITY_MATRIX_V[0].xyz;
                    haut = UNITY_MATRIX_V[1].xyz;
                    avant = -UNITY_MATRIX_V[2].xyz;
                }
                else
                {
                    droite = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                    haut = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                    avant = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                }

                // Écart angulaire à l'axe, en milliradians (la tangente vaut l'angle à ces échelles).
                // Calculé sans branche : la douceur du bord lit ses dérivées d'un pixel à l'autre.
                float profondeur = dot(regard, avant);
                float2 ecart = float2(dot(regard, droite), dot(regard, haut)) / max(profondeur, 1e-3) * 1000.0;
                float2 d = Distances(ecart);
                float2 douceur = max(float2(_Douceur, _Douceur), fwidth(d));
                float devant = step(0.0, profondeur);
                float reticule = (1.0 - smoothstep(-douceur.x, douceur.x, d.x)) * devant;
                float gravure = (1.0 - smoothstep(-douceur.y, douceur.y, d.y)) * devant * (1.0 - reticule);

                // Un léger reflet quand on regarde la vitre de biais : elle se lit comme du verre.
                float biais = 1.0 - saturate(abs(dot(normalize(input.normalWS), -regard)));
                half vitre = saturate(_CouleurVitre.a + biais * _Reflet * 0.5);
                // Le réticule couvre ce qu'il y a derrière lui : ajouté à la lumière d'un mur blanc ou
                // du ciel, il virait au rose (vu à deux instances le 2026-10-16). Il reste rouge. Les
                // traits gravés sont noirs : ils couvrent, sans lumière.
                half couvert = saturate(reticule + gravure);
                half alpha = saturate(vitre + couvert);
                half3 couleur = _CouleurVitre.rgb * vitre * (1.0 - couvert) + _CouleurReticule.rgb * (_Intensite * reticule);
                return half4(couleur, alpha);
            }
            ENDHLSL
        }
    }
}
