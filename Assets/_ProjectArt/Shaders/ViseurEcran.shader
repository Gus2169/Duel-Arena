// Écran d'un viseur numérique, en vue subjective (2026-10-09). Voir WeaponSight.
//
// En chaque point de l'écran, on retrouve la direction du MONDE qu'il cache, et on y lit l'image de
// la caméra du viseur. Les bras sont dessinés par une autre caméra, à un autre champ que le monde
// (60° contre 90°) : la direction réelle du point (repère de vue) est donc multipliée par le
// rapport des deux champs (_Projection.x) pour tomber sur le pixel du monde qu'il recouvre. Au
// grossissement 1, l'écran paraît transparent ; au-delà, l'image grossit autour du centre.
//
// La réticule est calculée à partir de cette même direction : elle marque le centre de l'écran du
// joueur, là où partent les balles, et ne dépend pas du grossissement. Unités : milliradians du
// champ du monde : à 90° de champ, un pixel vaut 2 à 3 mrad, d'où un point de 6 mrad de rayon.
Shader "DuelArena/ViseurEcran"
{
    Properties
    {
        _Ecran ("Image du viseur (fournie par WeaponSight)", 2D) = "black" {}
        _CouleurVeille ("Écran en veille", Color) = (0.32, 0.52, 0.58, 1)
        _Teinte ("Teinte de l'image", Color) = (0.90, 1.0, 0.97, 1)
        _Lignes ("Lignes de balayage", Range(0, 0.3)) = 0.06
        _CouleurReticule ("Couleur de la réticule", Color) = (1.0, 0.22, 0.08, 1)
        _Intensite ("Intensité de la réticule", Float) = 2.5
        _RayonPoint ("Rayon du point (mrad)", Float) = 6
        _RayonCercle ("Rayon du cercle (mrad, 0 = pas de cercle)", Float) = 30
        _EpaisseurCercle ("Épaisseur du cercle (mrad)", Float) = 3
        _Douceur ("Douceur du bord (mrad)", Float) = 0.9
        _Projection ("Champ monde/bras, grossissement, étendue filmée, allumage", Vector) = (1.732, 1, 0.55, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "ViseurEcran"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Ecran);
            SAMPLER(sampler_Ecran);

            CBUFFER_START(UnityPerMaterial)
                float4 _Ecran_ST;
                half4 _CouleurVeille;
                half4 _Teinte;
                half _Lignes;
                half4 _CouleurReticule;
                float _Intensite;
                float _RayonPoint;
                float _RayonCercle;
                float _EpaisseurCercle;
                float _Douceur;
                float4 _Projection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionVS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionVS = TransformWorldToView(positionWS);
                return output;
            }

            float Trait(float d, float w, float s)
            {
                return 1.0 - smoothstep(w - s, w + s, d);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // La direction de ce point vu de l'œil, puis celle du monde qu'il recouvre à l'écran.
                float2 directionBras = input.positionVS.xy / max(-input.positionVS.z, 1e-4);
                float2 directionMonde = directionBras * _Projection.x;

                // La caméra du viseur filme ±(étendue / grossissement) : l'image grossie se lit donc
                // à directionMonde / étendue, quel que soit le grossissement.
                float2 uv = 0.5 + 0.5 * directionMonde / _Projection.z;
                // L'image s'allume pendant que l'arme monte à l'œil (_Projection.w : 0 à la hanche,
                // 1 en visée), et se fond dans la veille près des bords de ce que filme le viseur :
                // pendant la montée, un rectangle d'image à bord net apparaissait sur l'écran.
                float2 bord = min(uv, 1.0 - uv);
                float image = saturate(_Projection.w) * smoothstep(0.0, 0.06, min(bord.x, bord.y));
                half3 vue = SAMPLE_TEXTURE2D(_Ecran, sampler_Ecran, saturate(uv)).rgb * _Teinte.rgb;
                half3 couleur = lerp(_CouleurVeille.rgb, vue, image);
                // Une ligne de pixels sur deux, à peine plus sombre : on lit un écran, pas une vitre.
                couleur *= 1.0 - _Lignes * step(1.0, fmod(input.positionCS.y, 2.0));

                if (_Projection.w > 0.0)
                {
                    float r = length(directionMonde) * 1000.0;
                    float reticule = Trait(r, _RayonPoint, _Douceur);
                    if (_RayonCercle > 0.0)
                    {
                        reticule = max(reticule, Trait(abs(r - _RayonCercle), _EpaisseurCercle * 0.5, _Douceur));
                    }
                    couleur = lerp(couleur, _CouleurReticule.rgb * _Intensite, saturate(reticule) * image);
                }
                return half4(couleur, 1.0);
            }
            ENDHLSL
        }
    }
}
