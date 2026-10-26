// Cel-shading du robot, pour l'essai des deux rendus (2026-10-26, bible § 13 : « low poly propre ou
// cel-shading discret, à prototyper » ; l'utilisateur veut voir les deux, bien distincts).
//
// La lumière ne se dégrade pas : elle tombe en aplats. Trois tons (lumière, demi-teinte, ombre teintée),
// l'ombre portée du soleil en aplat elle aussi, un reflet franc, un liseré sur le bord éclairé, et un
// contour sombre d'épaisseur constante à l'écran (coque inversée, passe « SRPDefaultUnlit »).
// Purement visuel, catégorie C.
Shader "DuelArena/RobotCelShading"
{
    Properties
    {
        _BaseColor ("Couleur", Color) = (1, 1, 1, 1)
        _CouleurOmbre ("Teinte de l'ombre (multipliée)", Color) = (0.36, 0.34, 0.52, 1)
        _Seuil ("Seuil lumière / ombre (N·L)", Range(-1, 1)) = 0.05
        _SeuilDemiTeinte ("Seuil de la demi-teinte (N·L)", Range(-1, 1)) = 0.5
        _ForceDemiTeinte ("Assombrissement de la demi-teinte", Range(0, 1)) = 0.18
        _Douceur ("Netteté des transitions (plus petit : plus net)", Range(0.001, 0.2)) = 0.012
        _TailleReflet ("Taille du reflet (cos de l'angle)", Range(0.8, 1)) = 0.985
        _CouleurReflet ("Couleur du reflet (alpha : force)", Color) = (1, 1, 1, 0.45)
        _Lisere ("Liseré du bord éclairé", Range(0, 1)) = 0.35
        _Ambiance ("Part de la lumière ambiante", Range(0, 1)) = 0.35
        _Contour ("Épaisseur du contour (pixels en 1080p)", Float) = 2.5
        _CouleurContour ("Couleur du contour", Color) = (0.04, 0.04, 0.06, 1)
        [ToggleUI] _ContourLisse ("Contour gonflé le long des tangentes (normales lissées, OutlineNormalsPostprocessor)", Float) = 0
        [HDR] _Emission ("Lueur ajoutée (un écran, un voyant)", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _CouleurOmbre;
            float _Seuil;
            float _SeuilDemiTeinte;
            float _ForceDemiTeinte;
            float _Douceur;
            float _TailleReflet;
            half4 _CouleurReflet;
            float _Lisere;
            float _Ambiance;
            float _Contour;
            half4 _CouleurContour;
            float _ContourLisse;
            half4 _Emission;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Une marche adoucie sur un pixel ou deux : un aplat, pas un dégradé.
            float Marche(float seuil, float x)
            {
                return smoothstep(seuil - _Douceur, seuil + _Douceur, x);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light soleil = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndl = dot(n, soleil.direction);

                // L'ombre portée tombe en aplat, comme l'ombre propre.
                float eclaire = Marche(_Seuil, ndl) * Marche(0.5, soleil.shadowAttenuation);
                float plein = Marche(_SeuilDemiTeinte, ndl) * eclaire;
                half3 base = _BaseColor.rgb;
                half3 ton = lerp(base * _CouleurOmbre.rgb, base * (1.0 - _ForceDemiTeinte), eclaire);
                ton = lerp(ton, base, plein);
                half3 couleur = ton * soleil.color;

                // Les autres lumières, en aplats aussi.
                #if defined(_ADDITIONAL_LIGHTS)
                uint nombre = GetAdditionalLightsCount();
                for (uint i = 0u; i < nombre; i++)
                {
                    Light autre = GetAdditionalLight(i, input.positionWS);
                    float part = Marche(_Seuil, dot(n, autre.direction)) * Marche(0.05, autre.distanceAttenuation * autre.shadowAttenuation);
                    couleur += base * autre.color * part * 0.5;
                }
                #endif

                // Un peu d'ambiance, pour que l'ombre ne soit pas un trou.
                couleur += base * SampleSH(n) * _Ambiance;

                // Reflet franc et liseré du bord éclairé.
                float3 h = normalize(soleil.direction + v);
                float reflet = Marche(_TailleReflet, dot(n, h)) * eclaire;
                couleur = lerp(couleur, _CouleurReflet.rgb * soleil.color, reflet * _CouleurReflet.a);
                float bord = Marche(0.72, 1.0 - saturate(dot(n, v))) * saturate(ndl * 2.0 + 0.6);
                couleur += base * bord * _Lisere * soleil.color;
                couleur += _Emission.rgb;

                couleur = MixFog(couleur, input.fog);
                return half4(couleur, 1.0);
            }
            ENDHLSL
        }

        // Le contour : la coque retournée, gonflée le long des normales d'une épaisseur constante à l'écran.
        Pass
        {
            Name "Contour"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fog : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Un modèle à facettes porte une normale lissée dans ses tangentes : sans elle, la coque
                // s'ouvrirait à chaque arête vive et le contour aurait des trous aux coins.
                float3 normalOS = _ContourLisse > 0.5 ? input.tangentOS.xyz : input.normalOS;
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                // Une épaisseur en pixels, la même de près comme de loin, proportionnelle à la définition.
                float2 direction = normalize(normalCS * _ScreenParams.xy + 1e-5);
                float pixels = _Contour * _ScreenParams.y / 1080.0;
                positionCS.xy += direction * pixels * 2.0 / _ScreenParams.xy * positionCS.w;
                output.positionCS = positionCS;
                output.fog = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(MixFog(_CouleurContour.rgb, input.fog), 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
