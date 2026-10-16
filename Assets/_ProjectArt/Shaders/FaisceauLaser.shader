// Le faisceau et le point du laser tactique (2026-10-16, GDD § 6).
//
// Le faisceau est un LineRenderer (TacticalLaser) : u le long du faisceau, v en travers ; il est
// plus dense au cœur qu'aux bords, et s'éteint vers son bout par la couleur des sommets. Le point est
// un carré tourné vers la caméra : un cœur net et un halo. Lumière ajoutée, mais qui couvre en partie
// ce qu'il y a derrière (_Couverture) : seulement ajouté, le rouge disparaissait sur les murs blancs
// de l'arène (vu à deux instances le 2026-10-16). Purement visuel, catégorie C.
Shader "DuelArena/FaisceauLaser"
{
    Properties
    {
        _Couleur ("Couleur", Color) = (1.0, 0.13, 0.09, 1.0)
        _Intensite ("Intensité", Float) = 2
        [Toggle] _Point ("Le point (dégradé radial) au lieu du faisceau", Float) = 0
        _Couverture ("Couverture du fond (0 : lumière seulement ajoutée)", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FaisceauLaser"
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
                half4 _Couleur;
                float _Intensite;
                float _Point;
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
                float densite, couvre;
                if (_Point > 0.5)
                {
                    float r = length(input.uv * 2.0 - 1.0);
                    float coeur = 1.0 - smoothstep(0.12, 0.22, r);
                    float halo = saturate(1.0 - r);
                    densite = coeur + halo * halo * 0.6;
                    couvre = saturate(coeur + halo * halo * 0.25);   // le cœur couvre, le halo s'ajoute
                }
                else
                {
                    float travers = abs(input.uv.y * 2.0 - 1.0);
                    densite = (1.0 - travers) * (1.0 - travers);
                    couvre = densite;
                }
                half3 couleur = _Couleur.rgb * input.color.rgb * (_Intensite * densite * input.color.a);
                return half4(couleur, saturate(couvre * _Couverture * input.color.a));
            }
            ENDHLSL
        }
    }
}
