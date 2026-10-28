// Le décor des arènes (2026-10-27) : voir Decor.hlsl. Les passes d'URP 17.6 comme ArmeUsee (lumières du
// Forward+, ombres, SSAO, lightmaps et sondes), plus le contour du cel-shading (coque retournée, comme
// RobotCelShading), qui ne se dessine que dans les rendus 1 et 2, et la passe Meta des lightmaps : le décor
// est statique, sa lumière peut se cuire.
Shader "DuelArena/Decor"
{
    Properties
    {
        [Header(Espace et motif)]
        [Enum(Architecture, 0, Piece de Blender, 1)] _Espace ("Espace de l'usure", Float) = 0
        [Enum(Aucun, 0, Beton banche, 1, Bardage rivete, 2, Dalle sciee, 3, Tole striee, 4)] _Motif ("Motif", Float) = 0
        _Panneau ("Panneau : largeur, hauteur, joint (m), assombrissement", Vector) = (2.4, 1.2, 0.012, 0.35)
        _Trous ("Trous de banche : colonnes, rangées, rayon (m), assombrissement", Vector) = (2, 2, 0.014, 0.7)
        _VariationPanneau ("Écart de teinte d'un panneau à l'autre", Range(0, 0.3)) = 0.05

        [Header(Peinture)]
        _Base ("Couleur", Color) = (0.42, 0.42, 0.41, 1)
        _Rug ("Rugosité", Range(0, 1)) = 0.85
        _Metal ("Métal", Range(0, 1)) = 0
        _Nuages ("Teinte nuageuse", Range(0, 0.5)) = 0.08
        _EchelleNuages ("Taille des nuages (par m)", Float) = 0.7

        [Header(Soubassement et hachures)]
        _Soubassement ("Soubassement : hauteur, bas et haut du liseré (m), usure", Vector) = (0, 0, 0, 0)
        _CouleurSoubassement ("Couleur du soubassement", Color) = (0.11, 0.115, 0.125, 1)
        _CouleurLisere ("Couleur du liseré", Color) = (0.72, 0.71, 0.67, 1)
        _Hachures ("Hachures : bas, haut, période (m), usure", Vector) = (0, 0, 0, 0)
        _CouleurHachures ("Couleur des hachures", Color) = (0.06, 0.06, 0.07, 1)

        [Header(Usure de l architecture)]
        _Crasse ("Crasse : hauteur (m), force, coulures, poussière des dessus", Vector) = (0.6, 0.5, 0.2, 0.3)
        _Impacts ("Impacts : cellule (m), probabilité, rayon (m), hauteur max (m)", Vector) = (0.4, 0, 0.03, 2.6)
        _CouleurEclats ("Éclats des arêtes (alpha : métal)", Color) = (0.62, 0.61, 0.58, 0)
        _EclatsArchi ("Éclats des arêtes", Range(0, 1)) = 0.4
        _Huiles ("Taches d'huile (sol)", Range(0, 1)) = 0

        [Header(Contour du cel shading)]
        [ToggleUI] _ContourLisse ("Contour le long des tangentes (normales lissées)", Float) = 1
        _Contour ("Épaisseur du contour (pixels en 1080p)", Float) = 2
        _CouleurContour ("Couleur du contour", Color) = (0.03, 0.03, 0.045, 1)

        [Header(Marquage peint)]
        _Decoupe ("Seuil de la peinture (0 : opaque)", Range(0, 1)) = 0
        _MarquageTex ("Marquage (alpha : peinture)", 2D) = "white" {}
        _CouleurMarquage ("Couleur du marquage", Color) = (0.78, 0.77, 0.72, 1)
        _UsureMarquage ("Usure du marquage", Range(0, 1)) = 0.4

        [Header(Piece de Blender (parametres d ArmeUsee))]
        [ToggleUI] _ADessous ("A un dessous", Float) = 0
        _Dessous ("Dessous", Color) = (0.62, 0.63, 0.64, 1)
        _RugDessous ("Rugosité du dessous", Range(0, 1)) = 0.35
        _MetalDessous ("Métal du dessous", Range(0, 1)) = 1
        _Bords ("Bords", Range(0, 1)) = 0.5
        _Eclats ("Éclats", Range(0, 1)) = 0
        _EchelleEclats ("Échelle des éclats", Float) = 42
        _Rayures ("Rayures", Range(0, 1)) = 0
        _Salete ("Crasse des creux", Range(0, 1)) = 0.5
        _Variation ("Teinte nuageuse (pièce)", Range(0, 1)) = 0.08
        [ToggleUI] _ACouche ("A une couche", Float) = 0
        _Couche ("Couche", Color) = (0.46, 0.20, 0.15, 1)
        _RugCouche ("Rugosité de la couche", Range(0, 1)) = 0.78
        _Retouches ("Retouches", Range(0, 1)) = 0
        _TeinteRetouches ("Teinte des retouches", Color) = (0.16, 0.18, 0.19, 1)
        _Rouille ("Rouille", Range(0, 1)) = 0
        _TeinteRouille ("Teinte de la rouille", Color) = (0.24, 0.16, 0.12, 1)
        _Huile ("Huile", Range(0, 1)) = 0
        _Poussiere ("Poussière", Range(0, 1)) = 0
        _Delavage ("Peinture passée", Range(0, 1)) = 0
        _Zone0 ("Zone 1", Vector) = (0, 0, 0, 0)
        _Zone1 ("Zone 2", Vector) = (0, 0, 0, 0)
        _ForcesZones ("Forces des zones", Vector) = (1, 1, 0, 0)
        _Suie ("Suie : centre, rayon", Vector) = (0, 0, 0, 0)
        _EmissionCouleur ("Émission", Color) = (0, 0, 0, 1)
        _ForceEmission ("Force de l'émission", Float) = 0
        _Coat ("Vernis", Range(0, 1)) = 0
        [NoScaleOffset] _Occlusion ("Occlusion cuite", 2D) = "white" {}
        _DecVariation ("Décalage : Variation", Vector) = (0, 0, 0, 0)
        _DecDelavage ("Décalage : Delavage", Vector) = (0, 0, 0, 0)
        _DecBords ("Décalage : Bords", Vector) = (0, 0, 0, 0)
        _DecEclats ("Décalage : Eclats", Vector) = (0, 0, 0, 0)
        _DecZoneEclats ("Décalage : ZoneEclats", Vector) = (0, 0, 0, 0)
        _DecZoneRayures ("Décalage : ZoneRayures", Vector) = (0, 0, 0, 0)
        _DecRayures1 ("Décalage : Rayures1", Vector) = (0, 0, 0, 0)
        _DecRayures2 ("Décalage : Rayures2", Vector) = (0, 0, 0, 0)
        _DecRetouches ("Décalage : Retouches", Vector) = (0, 0, 0, 0)
        _DecRouille ("Décalage : Rouille", Vector) = (0, 0, 0, 0)
        _DecCloques ("Décalage : Cloques", Vector) = (0, 0, 0, 0)
        _DecZoneCloques ("Décalage : ZoneCloques", Vector) = (0, 0, 0, 0)
        _DecCoulures ("Décalage : Coulures", Vector) = (0, 0, 0, 0)
        _DecZoneCoulures ("Décalage : ZoneCoulures", Vector) = (0, 0, 0, 0)
        _DecHuile ("Décalage : Huile", Vector) = (0, 0, 0, 0)
        _DecHuileDetail ("Décalage : HuileDetail", Vector) = (0, 0, 0, 0)
        _DecPoussiere ("Décalage : Poussiere", Vector) = (0, 0, 0, 0)
        _DecSuie ("Décalage : Suie", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DecorVertex
            #pragma fragment DecorFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Decor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 uvBord : TEXCOORD0;          // uv, bord (couleur de sommet rouge)
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                float3 aligne : TEXCOORD5;
                float3 normaleAlignee : TEXCOORD6;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD7;
            #else
                half fogFactor : TEXCOORD7;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD8;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 9);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DecorVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.uvBord = float3(input.texcoord, input.color.r);
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                DecorRepere(vertexInput.positionWS, normalInput.normalWS, output.aligne, output.normaleAlignee);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(vertexInput.positionWS, normalInput.normalWS));
            #else
                output.fogFactor = fogFactor;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            // InitializeInputData et InitializeBakedGIData de LitForwardPass.hlsl, sans carte de normales (comme ArmeUsee).
            void DecorInputData(Varyings input, out InputData inputData)
            {
                inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

            #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
                                              inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif
            }

            // --- le cel-shading ---------------------------------------------------------------------

            // La lumière diffuse d'une source. Son ombre portée est tranchée à mi-chemin (un aplat net) : douce, ses
            // petites variations (l'acné et le crénelage de la carte d'ombres, au loin) faisaient basculer le palier et
            // dessinaient des dents de scie (2026-10-27).
            half3 DecorDiffuse(Light light, float3 n)
            {
                half ombre = smoothstep(0.3, 0.7, light.shadowAttenuation);
                return light.color * (light.distanceAttenuation * ombre * saturate(dot(n, light.direction)));
            }

            // La lumière principale posée en paliers : sa luminance est ramenée au palier le plus proche, une marche
            // tous les 1,25 diaphragme, la teinte gardée. Chaque face prend un aplat, l'ombre portée un autre.
            half3 DecorPaliers(half3 lumiere)
            {
                const float pas = 1.25;
                float l = max(Luminance(lumiere), 1e-4);
                float e = log2(l) / pas;
                float base = floor(e);
                float t = e - base;
                float w = clamp(fwidth(e), 1e-3, 0.5);
                float marche = saturate((t - 0.5) / w + 0.5);
                return lumiere * (exp2((base + marche) * pas) / l);
            }

            half3 DecorCel(InputData inputData, half3 albedo, half occlusion, half3 emission)
            {
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = 1;
                half4 shadowMask = CalculateShadowMask(inputData);
                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
                uint meshRenderingLayers = GetMeshRenderingLayer();
                float3 n = inputData.normalWS;

                // Seule la lumière principale (la rampe générale et son ombre) tombe en aplats. Les lumières
                // d'appoint (projecteurs, lampes), la lumière cuite et l'occlusion d'écran s'ajoutent en douceur :
                // posée en paliers, la somme des flaques des projecteurs, qui se recouvrent, dessinait les courbes de
                // niveau d'un relief (des vagues sur le sol, « pas naturel » pour l'utilisateur), et le bruit de la
                // lumière cuite des bords dentelés (2026-10-27). Les lumières sont lues sans l'occlusion, qui
                // s'applique ensuite.
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);
                half3 principale = 0;
            #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
            #endif
                    principale = DecorDiffuse(mainLight, n);

                half3 appoint = 0;

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
            #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            #endif
                        appoint += DecorDiffuse(light, n);
                }
            #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
            #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            #endif
                        appoint += DecorDiffuse(light, n);
                LIGHT_LOOP_END
            #endif
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                appoint += inputData.vertexLighting;
            #endif
                half3 indirecte = inputData.bakedGI * aoFactor.indirectAmbientOcclusion;
                return albedo * ((DecorPaliers(principale) + appoint) * aoFactor.directAmbientOcclusion + indirecte) + emission;
            }

            void DecorFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                ArmeSurface s = DecorSurface(input.positionWS, input.aligne, input.normaleAlignee, input.positionOS,
                                             input.normalOS, input.uvBord.xy, input.uvBord.z, DecorProche(input.positionWS));
                DecorMarquage(s, input.uvBord.xy, input.positionWS);

                InputData inputData;
                DecorInputData(input, inputData);
                half3 color;
                if (_DecorStyle > 0.5)
                {
                    color = DecorCel(inputData, s.albedo, s.occlusion, s.emission);
                }
                else
                {
                    SurfaceData surfaceData = (SurfaceData)0;
                    surfaceData.albedo = s.albedo;
                    surfaceData.specular = half3(0, 0, 0);
                    surfaceData.metallic = s.metallic;
                    surfaceData.smoothness = s.smoothness;
                    surfaceData.normalTS = half3(0, 0, 1);
                    surfaceData.emission = s.emission;
                    surfaceData.occlusion = s.occlusion;
                    surfaceData.alpha = 1;
                    color = UniversalFragmentPBR(inputData, surfaceData).rgb;
                }
                color = MixFog(color, inputData.fogCoord);
                outColor = half4(color, 1);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        // Le contour du cel-shading : la coque retournée, gonflée d'une épaisseur constante à l'écran. Son étiquette à
        // lui (« DecorContour ») : URP ne la dessine pas de lui-même, DecorOutlineFeature la dessine quand le rendu en a
        // un (RenderStyle.Outlines) ; en low poly propre, elle ne coûte rien. Coupée sur les matériaux sans contour
        // (_Contour à 0 : lampes, marquages). Dans le rendu 0, ses triangles sortent de l'écran.
        Pass
        {
            Name "Contour"
            Tags { "LightMode" = "DecorContour" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ContourVertex
            #pragma fragment ContourFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Decor.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fog : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ContourVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                if (_DecorStyle < 0.5 || _Contour <= 0.0)
                {
                    output.positionCS = float4(2.0, 2.0, 2.0, 1.0);   // hors de l'écran
                    return output;
                }
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalOS = _ContourLisse > 0.5 ? input.tangentOS.xyz : input.normalOS;
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float2 direction = normalize(normalCS * _ScreenParams.xy + 1e-5);
                float pixels = _Contour * _ScreenParams.y / 1080.0;
                positionCS.xy += direction * pixels * 2.0 / _ScreenParams.xy * positionCS.w;
                output.positionCS = positionCS;
                output.fog = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 ContourFragment(Varyings input) : SV_Target
            {
                return half4(MixFog(_CouleurContour.rgb, input.fog), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Decor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Decor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // pour la texture des normales de la caméra (SSAO)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include "Decor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        // La couleur et la lueur du décor pour la cuisson des lightmaps.
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MetaVertex
            #pragma fragment MetaFragmentDecor
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Decor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uvBord : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                float3 aligne : TEXCOORD4;
                float3 normaleAlignee : TEXCOORD5;
            };

            Varyings MetaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.uvBord = float3(input.uv0, input.color.r);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                DecorRepere(output.positionWS, TransformObjectToWorldNormal(input.normalOS), output.aligne, output.normaleAlignee);
                return output;
            }

            half4 MetaFragmentDecor(Varyings input) : SV_Target
            {
                ArmeSurface s = DecorSurface(input.positionWS, input.aligne, input.normaleAlignee, input.positionOS,
                                             input.normalOS, input.uvBord.xy, input.uvBord.z, 1.0);
                MetaInput meta = (MetaInput)0;
                meta.Albedo = s.albedo;
                meta.Emission = s.emission;
                return MetaFragment(meta);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
