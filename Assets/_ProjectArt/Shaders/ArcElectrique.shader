// Les arcs électriques (2026-10-16) : la décharge du gant (TaserSparkFX) et le grésillement d'un
// spectateur étourdi (SpectatorController), dessinés par des LineRenderer (ArcLines) : u le long de
// l'arc, v en travers.
//
// Un cœur blanc, net, et un halo rouge qui s'éteint vers les bords : l'énergie de la bible (§ 12),
// celle de la jauge du gant et des lueurs des armes laser ; ni le rouge ni le blanc ne sont des
// couleurs de robot. La force de chaque arc (l'alpha des sommets) bat à chaque forme. Le jeu n'a ni
// bloom ni post-traitement : la lueur est dessinée ici. Couleur prémultipliée : le cœur couvre ce
// qu'il y a derrière et le halo s'y ajoute en le couvrant en partie ; seulement ajouté, un arc
// disparaîtrait sur les murs blancs de l'arène (le piège du laser, 2026-10-16). Catégorie C.
Shader "DuelArena/ArcElectrique"
{
    Properties
    {
        _CouleurCoeur ("Cœur", Color) = (1, 1, 1, 1)
        _CouleurHalo ("Halo", Color) = (1, 0.102, 0.055, 1)
        _Coeur ("Largeur du cœur (part de la demi-largeur)", Range(0.02, 0.6)) = 0.2
        _Douceur ("Chute du halo (exposant)", Range(0.5, 6)) = 1.6
        _Intensite ("Intensité du halo", Float) = 2
        _Couverture ("Couverture du fond par le halo", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ArcElectrique"
            Tags { "LightMode" = "UniversalForward" }

            // Couleur prémultipliée : la lumière s'ajoute, et couvre le fond de sa couverture.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CouleurCoeur;
                half4 _CouleurHalo;
                float _Coeur;
                float _Douceur;
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
                float travers = abs(input.uv.y * 2.0 - 1.0);   // 0 au milieu de l'arc, 1 à son bord
                float lissage = max(fwidth(travers), 1e-4);
                float coeur = 1.0 - smoothstep(_Coeur - lissage, _Coeur + lissage, travers);
                float halo = pow(saturate(1.0 - travers), _Douceur);
                float force = input.color.a;
                half3 couleur = (_CouleurCoeur.rgb * coeur + _CouleurHalo.rgb * (halo * _Intensite * (1.0 - coeur))) * force;
                return half4(couleur, saturate(coeur + halo * _Couverture) * force);
            }
            ENDHLSL
        }
    }
}
