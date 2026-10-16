// Vitre de viseur avec réticule « à l'infini » (2026-10-09 ; formes de Rainbow Six le 2026-10-16).
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
// Les formes (_Forme) sont celles de commun_accessoires.FORMES_RETICULE, dans le même ordre et aux
// mêmes proportions que le réticule des rendus de contrôle de Blender, rapportées à _Rayon (le
// rayon du cercle du réticule, ou sa demi-largeur) :
//   0 point, 1 cercle et point, 2 cercle et chevron, 3 losange et point, 4 tirets et point,
//   5 cercle et croix, 6 triangle, 7 tirets et chevron.
//
// Unités : milliradians (1 mrad = 1 cm à 10 m). Purement visuel, catégorie C.
Shader "DuelArena/ViseurReticule"
{
    Properties
    {
        _CouleurVitre ("Teinte de la vitre (alpha = opacité)", Color) = (0.45, 0.72, 0.80, 0.10)
        _CouleurReticule ("Couleur de la réticule", Color) = (1.0, 0.16, 0.10, 1.0)
        _Intensite ("Intensité de la réticule", Float) = 4
        _Forme ("Forme (0 à 7, voir l'en-tête)", Float) = 0
        _Rayon ("Rayon du réticule (mrad) : son cercle, ou sa demi-largeur", Float) = 40
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
                float _Forme;
                float _Rayon;
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

            // Distances signées (négatives à l'intérieur), dans les unités du réticule (_Rayon = 1).
            float Disque(float2 p, float r)
            {
                return length(p) - r;
            }

            float Cercle(float2 p, float r, float demiLargeur)
            {
                return abs(length(p) - r) - demiLargeur;
            }

            // Un trait à bouts carrés de a à b, comme commun_accessoires.trait.
            float Trait(float2 p, float2 a, float2 b, float demiLargeur)
            {
                float2 ab = b - a;
                float longueur = length(ab);
                float2 axe = ab / max(longueur, 1e-5);
                float2 ap = p - (a + b) * 0.5;
                float2 q = abs(float2(dot(ap, axe), dot(ap, float2(-axe.y, axe.x)))) - float2(longueur * 0.5, demiLargeur);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            // Triangle plein (Inigo Quilez, sdTriangle).
            float Triangle(float2 p, float2 p0, float2 p1, float2 p2)
            {
                float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
                float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
                float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
                float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
                float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
                float s = sign(e0.x * e2.y - e0.y * e2.x);
                float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                                   float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * sign(d.y);
            }

            // Le chevron : deux traits qui se rejoignent au-dessus du centre.
            float Chevron(float2 p, float k, float demiLargeur)
            {
                float2 sommet = float2(0.0, k * 0.15);
                return min(Trait(p, float2(-k, -k * 0.75), sommet, demiLargeur),
                           Trait(p, sommet, float2(k, -k * 0.75), demiLargeur));
            }

            float Tirets(float2 p, float demiLargeur)
            {
                return min(Trait(p, float2(0.32, 0.0), float2(0.95, 0.0), demiLargeur),
                           Trait(p, float2(-0.32, 0.0), float2(-0.95, 0.0), demiLargeur));
            }

            float Reticule(float2 p, int forme)
            {
                const float ep = 0.075;   // l'épaisseur des traits (commun_accessoires.reticule)
                float d = 1e5;
                if (forme == 0) d = Disque(p, 0.13);
                if (forme == 1 || forme == 3 || forme == 4 || forme == 5) d = Disque(p, 0.07);
                if (forme == 1 || forme == 2 || forme == 5) d = min(d, Cercle(p, 1.0, ep * 0.5));
                if (forme == 5)
                {
                    float2 q = abs(p);   // les quatre branches de la croix, par symétrie
                    d = min(d, Trait(q, float2(0.62, 0.0), float2(0.86, 0.0), ep * 0.5));
                    d = min(d, Trait(q, float2(0.0, 0.62), float2(0.0, 0.86), ep * 0.5));
                }
                if (forme == 3)
                {
                    float2 q = abs(p);   // le losange : un côté, par symétrie
                    d = min(d, Trait(q, float2(0.0, 0.75), float2(0.75, 0.0), ep * 0.5));
                }
                if (forme == 4 || forme == 7) d = min(d, Tirets(p, ep * 1.3 * 0.5));
                if (forme == 2) d = min(d, Chevron(p, 0.42, ep * 1.2 * 0.5));
                if (forme == 7) d = min(d, Chevron(p, 0.30, ep * 1.2 * 0.5));
                if (forme == 6) d = Triangle(p, float2(-0.30, -0.22), float2(0.30, -0.22), float2(0.0, 0.30));
                return d;
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
                float rayon = max(_Rayon, 1e-3);
                float d = Reticule(ecart / rayon, (int)round(_Forme)) * rayon;
                float douceur = max(_Douceur, fwidth(d));
                float reticule = (1.0 - smoothstep(-douceur, douceur, d)) * step(0.0, profondeur);

                // Un léger reflet quand on regarde la vitre de biais : elle se lit comme du verre.
                float biais = 1.0 - saturate(abs(dot(normalize(input.normalWS), -regard)));
                half vitre = saturate(_CouleurVitre.a + biais * _Reflet * 0.5);
                // Le réticule couvre ce qu'il y a derrière lui : ajouté à la lumière d'un mur blanc ou
                // du ciel, il virait au rose (vu à deux instances le 2026-10-16). Il reste rouge.
                half alpha = saturate(vitre + reticule);
                half3 couleur = _CouleurVitre.rgb * vitre * (1.0 - reticule) + _CouleurReticule.rgb * (_Intensite * reticule);
                return half4(couleur, alpha);
            }
            ENDHLSL
        }
    }
}
