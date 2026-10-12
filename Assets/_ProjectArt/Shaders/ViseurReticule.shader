// Vitre de viseur avec réticule « à l'infini » (2026-10-09).
//
// La réticule n'est pas peinte sur la vitre : elle est calculée à partir de la DIRECTION du regard.
// Chaque pixel de la vitre regarde dans une direction ; la réticule s'allume quand cette direction
// tombe à quelques milliradians de l'axe de visée. Elle est donc comme projetée à l'infini, à la
// manière d'un vrai point rouge : elle reste sur la cible quelle que soit la position de l'œil, et
// n'apparaît qu'à travers la vitre.
//
// L'axe de visée a deux modes :
// - celui du fusil (+Z de l'objet, le canon) : pour le fusil vu par les autres ;
// - celui de la caméra (_AxeCamera = 1) : pour la vue subjective. Le tir part de la caméra, donc
//   le point doit marquer le centre de l'écran, même quand le fusil balance au rythme des pas.
//   FirstPersonView pose ce mode sur la copie des bras.
//
// Unités : milliradians (1 mrad = 1 cm à 10 m). Purement visuel, catégorie C.
Shader "DuelArena/ViseurReticule"
{
    Properties
    {
        _CouleurVitre ("Teinte de la vitre (alpha = opacité)", Color) = (0.45, 0.72, 0.80, 0.10)
        _CouleurReticule ("Couleur de la réticule", Color) = (1.0, 0.16, 0.10, 1.0)
        _Intensite ("Intensité de la réticule", Float) = 4
        _RayonPoint ("Rayon du point (mrad)", Float) = 1.6
        _RayonCercle ("Rayon du cercle (mrad, 0 = pas de cercle)", Float) = 0
        _EpaisseurCercle ("Épaisseur du cercle (mrad)", Float) = 0.6
        _Douceur ("Douceur du bord (mrad)", Float) = 0.35
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CouleurVitre;
                half4 _CouleurReticule;
                float _Intensite;
                float _RayonPoint;
                float _RayonCercle;
                float _EpaisseurCercle;
                float _Douceur;
                float _Reflet;
                float _AxeCamera;
            CBUFFER_END

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

            // Bande lumineuse de largeur w (mrad) autour de la distance d, adoucie de s.
            float Trait(float d, float w, float s)
            {
                return 1.0 - smoothstep(w - s, w + s, d);
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

                float profondeur = dot(regard, avant);
                float reticule = 0.0;
                if (profondeur > 0.0)
                {
                    // Écart angulaire à l'axe, en milliradians (la tangente vaut l'angle à ces échelles).
                    float2 ecart = float2(dot(regard, droite), dot(regard, haut)) / profondeur * 1000.0;
                    float r = length(ecart);
                    reticule = Trait(r, _RayonPoint, _Douceur);
                    if (_RayonCercle > 0.0)
                    {
                        reticule = max(reticule, Trait(abs(r - _RayonCercle), _EpaisseurCercle * 0.5, _Douceur));
                    }
                }

                // Un léger reflet quand on regarde la vitre de biais : elle se lit comme du verre.
                float biais = 1.0 - saturate(abs(dot(normalize(input.normalWS), -regard)));
                half alpha = saturate(_CouleurVitre.a + biais * _Reflet * 0.5);
                half3 couleur = _CouleurVitre.rgb * alpha + _CouleurReticule.rgb * (_Intensite * reticule);
                return half4(couleur, alpha);
            }
            ENDHLSL
        }
    }
}
