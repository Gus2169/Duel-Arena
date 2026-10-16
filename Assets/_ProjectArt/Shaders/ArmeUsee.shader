// Les armes de Duel Arena (2026-10-15) : un matériau PBR d'URP dont la couleur, la rugosité et le
// métal viennent de l'usure procédurale d'ArmeUsee.hlsl, portée des matériaux de Blender. Un
// matériau par pièce et par finition (WeaponFinishes) : les paramètres sont ceux qu'exporter_armes.py
// lit dans kit_lowpoly.materiau, et l'importeur (Duel Arena > Armes) les écrit ici.
//
// Les passes d'URP 17.6 (Lit.shader) en dehors de la couleur : ombres, profondeur, normales (SSAO),
// vecteurs de mouvement. Ni carte de normales, ni transparence, ni lightmap : les armes bougent.
Shader "DuelArena/ArmeUsee"
{
    Properties
    {
        [Header(Peinture)]
        _Base ("Couleur", Color) = (0.21, 0.23, 0.25, 1)
        _Rug ("Rugosité", Range(0, 1)) = 0.6
        _Metal ("Métal", Range(0, 1)) = 0
        _Variation ("Teinte nuageuse", Range(0, 1)) = 0.08

        [Header(Ce que la peinture laisse voir)]
        [ToggleUI] _ADessous ("A un dessous", Float) = 0
        _Dessous ("Dessous (métal nu, plastique éraflé)", Color) = (0.62, 0.63, 0.64, 1)
        _RugDessous ("Rugosité du dessous", Range(0, 1)) = 0.35
        _MetalDessous ("Métal du dessous", Range(0, 1)) = 1
        _Bords ("Bords (chanfreins usés)", Range(0, 1)) = 0.5
        _Eclats ("Éclats", Range(0, 1)) = 0
        _EchelleEclats ("Échelle des éclats", Float) = 42
        _Rayures ("Rayures", Range(0, 1)) = 0
        _Salete ("Crasse des creux", Range(0, 1)) = 0.5

        [Header(Les marques d un long service)]
        [ToggleUI] _ACouche ("A une couche sous la peinture", Float) = 0
        _Couche ("Couche (apprêt)", Color) = (0.46, 0.20, 0.15, 1)
        _RugCouche ("Rugosité de la couche", Range(0, 1)) = 0.78
        _Retouches ("Retouches au pinceau", Range(0, 1)) = 0
        _TeinteRetouches ("Teinte des retouches", Color) = (0.16, 0.18, 0.19, 1)
        _Rouille ("Rouille", Range(0, 1)) = 0
        _TeinteRouille ("Teinte de la rouille", Color) = (0.24, 0.16, 0.12, 1)
        _Huile ("Huile", Range(0, 1)) = 0
        _Poussiere ("Poussière", Range(0, 1)) = 0
        _Delavage ("Peinture passée", Range(0, 1)) = 0
        _Zone0 ("Mains : zone 1 (centre, rayon)", Vector) = (0, 0, 0, 0)
        _Zone1 ("Mains : zone 2 (centre, rayon)", Vector) = (0, 0, 0, 0)
        _ForcesZones ("Mains : forces des zones", Vector) = (1, 1, 0, 0)

        [Header(Suie et lueurs)]
        _Suie ("Suie : centre, rayon", Vector) = (0, 0, 0, 0)
        _EmissionCouleur ("Émission", Color) = (0, 0, 0, 1)
        _ForceEmission ("Force de l'émission", Float) = 0
        _Coat ("Vernis", Range(0, 1)) = 0

        [Header(Cuit dans Blender)]
        [NoScaleOffset] _Occlusion ("Occlusion (1,8 cm)", 2D) = "white" {}

        [Header(Decalages des bruits tires par kit_lowpoly)]
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
            #pragma vertex ArmeVertex
            #pragma fragment ArmeFragment

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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            // le vernis des écrans et des lentilles (Coat du Principled BSDF de Blender)
            #define _CLEARCOAT 1

            #include "ArmeUsee.hlsl"
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
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float4 normalOSBord : TEXCOORD4;   // normale objet, bord (couleur de sommet rouge)
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #else
                half fogFactor : TEXCOORD5;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ArmeVertex(Attributes input)
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
                output.uv = input.texcoord;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOSBord = float4(input.normalOS, input.color.r);
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

            // InitializeInputData et InitializeBakedGIData de LitForwardPass.hlsl, sans carte de normales.
            void ArmeInputData(Varyings input, out InputData inputData)
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

            void ArmeFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                ArmeSurface s = ArmeUsure(input.positionOS, input.normalOSBord.xyz, input.uv, input.normalOSBord.w);
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = s.albedo;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = s.metallic;
                surfaceData.smoothness = s.smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = s.emission;
                surfaceData.occlusion = s.occlusion;
                surfaceData.alpha = 1;
                surfaceData.clearCoatMask = s.coat;
                surfaceData.clearCoatSmoothness = 0.95;   // Coat Roughness 0,05 dans Blender

                InputData inputData;
                ArmeInputData(input, inputData);
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
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
            #include "ArmeUsee.hlsl"
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
            #include "ArmeUsee.hlsl"
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
            #include "ArmeUsee.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #include "ArmeUsee.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
