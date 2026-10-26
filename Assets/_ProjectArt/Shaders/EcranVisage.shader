// L'écran du visage (2026-10-26, demandé par l'utilisateur) : la visière du robot, un écran sombre de son
// vivant ; à sa mort, un écran qui grésille et affiche FATAL ERROR sous un triangle d'alerte rouge, d'après
// l'image de référence de l'utilisateur (un fond de pixels bleus, un texte aux couleurs séparées).
//
// L'image (Textures/EcranMort.png, faite par Tools/Blender/ecran_mort.py) n'en porte que les masques : le
// rouge pour le triangle, le vert pour le texte, dans le cadre des UV de la visière (robot_ct01.uv_ecran).
// Le reste se calcule ici : le fond de pixels, les couleurs, et le glitch (des bandes décalées, les couleurs
// séparées, des lignes de balayage, un scintillement), violent à la mort puis par à-coups. _MortDepuis est
// l'instant de la mort sur l'horloge du shader (_Time.y = Time.timeSinceLevelLoad), posé par RobotFaceScreen
// dans un MaterialPropertyBlock. Catégorie C : rien de jouable n'en dépend.
//
// Le rouge n'est pas une couleur de robot (bible § 5) ; le jeu n'a pas de bloom : l'écran est dessiné à plein,
// sans lumière, comme un écran.
Shader "DuelArena/EcranVisage"
{
    Properties
    {
        _CouleurEteinte ("Écran éteint", Color) = (0.035, 0.043, 0.051, 1)
        _Reflet ("Reflet de la vitre, sur les bords", Range(0, 1)) = 0.12
        [NoScaleOffset] _Image ("Masques (rouge : triangle, vert : texte)", 2D) = "black" {}
        _Mort ("Mort (0 ou 1)", Range(0, 1)) = 0
        _MortDepuis ("Instant de la mort (Time.timeSinceLevelLoad)", Float) = 0
        _CouleurAlerte ("Triangle", Color) = (1, 0.09, 0.08, 1)
        _CouleurTexte ("Texte", Color) = (0.92, 0.96, 1, 1)
        _CouleurFond ("Fond de pixels", Color) = (0.05, 0.2, 0.62, 1)
        _Pixels ("Pixels du fond, en largeur", Float) = 18
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "EcranVisage"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Image);
            SAMPLER(sampler_Image);

            CBUFFER_START(UnityPerMaterial)
                half4 _CouleurEteinte;
                float _Reflet;
                float _Mort;
                float _MortDepuis;
                half4 _CouleurAlerte;
                half4 _CouleurTexte;
                half4 _CouleurFond;
                float _Pixels;
            CBUFFER_END

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
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            float Hasard(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 eteint = _CouleurEteinte.rgb + pow(1.0 - saturate(dot(n, v)), 4.0) * _Reflet;
                if (_Mort < 0.5) return half4(eteint, 1.0);

                float t = max(0.0, _Time.y - _MortDepuis);
                float pas = floor(t * 14.0);                    // le glitch change de forme 14 fois par seconde
                float2 uv = input.uv;

                // Violent à la mort, puis par à-coups (un pas sur sept environ)
                float choc = saturate(1.0 - t / 0.8);
                float acoup = step(0.86, Hasard(float2(pas, 3.1)));
                float force = max(choc, 0.7 * acoup);

                // Des bandes horizontales décalées, d'autant plus nombreuses que le glitch est fort
                float bande = floor(uv.y * 36.0);
                float decale = Hasard(float2(bande, pas)) > 0.96 - 0.35 * force
                    ? (Hasard(float2(bande + 17.0, pas)) - 0.5) * (0.04 + 0.22 * force) : 0.0;
                float2 uvg = float2(uv.x + decale, uv.y);

                // Les couleurs séparées : une frange rouge à droite, une bleue à gauche
                float ecart = 0.006 + 0.03 * force;
                half4 milieu = SAMPLE_TEXTURE2D(_Image, sampler_Image, uvg);
                half4 droite = SAMPLE_TEXTURE2D(_Image, sampler_Image, uvg + float2(ecart, 0.0));
                half4 gauche = SAMPLE_TEXTURE2D(_Image, sampler_Image, uvg - float2(ecart, 0.0));

                // Le fond : des pixels bleus qui scintillent, aux bords sombres (le cadre fait 9 x 15 cm : des pixels carrés)
                float2 grille = float2(_Pixels, _Pixels * 1.667);
                float2 cellule = floor(uv * grille);
                float bruit = Hasard(cellule + pas * 0.37);
                float2 dans = frac(uv * grille);
                float joint = smoothstep(0.0, 0.14, dans.x) * smoothstep(0.0, 0.14, dans.y);
                half3 c = _CouleurFond.rgb * (0.2 + 0.8 * bruit * bruit) * (0.35 + 0.65 * joint);

                // Le triangle, puis le texte, avec leurs franges
                c = lerp(c, _CouleurAlerte.rgb, milieu.r);
                c.r = max(c.r, droite.r);
                c = lerp(c, _CouleurTexte.rgb, milieu.g);
                c.r = max(c.r, droite.g * 0.95);
                c.b = max(c.b, gauche.g);

                // Les lignes de balayage, le scintillement, l'éclair blanc de la mort
                c *= 0.84 + 0.16 * sin(uv.y * 6.2832 * 60.0);
                c *= 0.82 + 0.18 * Hasard(float2(pas, 7.7));
                c = lerp(c, half3(1.0, 1.0, 1.0), saturate(1.0 - t / 0.12));
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
