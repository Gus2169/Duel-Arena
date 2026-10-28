// L'usure procédurale des armes (2026-10-15) : le graphe de kit_lowpoly.materiau (Tools/Blender),
// porté tel quel. Mêmes paramètres (exportés par exporter_armes.py, un matériau par finition), mêmes
// seuils, même ordre des mélanges, et les mêmes motifs, au même endroit : le bruit et le Voronoï sont
// ceux de Blender 5.2, à la précision des flottants près (vérifié le 2026-10-15 contre des cuissons
// de Cycles, pixel par pixel : 5·10⁻⁵ d'écart sur le bruit, 0,06 % de pixels du Voronoï qui changent
// de cellule à une frontière), et chaque bruit reçoit le décalage que kit_lowpoly lui a tiré. Les
// armes ont donc ici l'usure validée sur les rendus de Blender.
//
// Les deux bruits sont des algorithmes publics : le Perlin « amélioré » de Ken Perlin (×0,982 et
// sommé en fBm normalisé, comme le nœud Noise Texture) haché par lookup3 de Bob Jenkins (domaine
// public), et le Voronoï F1 haché par PCG 3D (Jarzynski et Olano, JCGT 2020) en entiers signés.
//
// Ce qui ne se recalcule pas ici vient du maillage : la couleur de sommet rouge porte l'attribut
// « bord » des chanfreins (1 : le métal peut y apparaître), la texture _Occlusion le nœud Ambient
// Occlusion de Blender (1,8 cm), cuit par Cycles dans le dépliage UV.
//
// Les coordonnées de l'usure sont celles de l'objet, ramenées au repère de l'arme de Blender (les
// paramètres, la suie et les zones des mains y sont exprimés). Les pièces d'une arme n'ont ni
// rotation ni échelle sous sa racine : leur repère objet est celui de l'arme, et l'usure se
// raccorde d'une pièce à l'autre, où que l'arme soit tenue.
#ifndef DUELARENA_ARME_USEE_INCLUDED
#define DUELARENA_ARME_USEE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Une seule disposition pour toutes les passes : le SRP Batcher l'exige.
CBUFFER_START(UnityPerMaterial)
    half4 _Base;
    half4 _Dessous;
    half4 _Couche;
    half4 _TeinteRetouches;
    half4 _TeinteRouille;
    half4 _EmissionCouleur;
    float4 _Suie;          // centre (repère de l'arme de Blender), rayon ; rayon nul : pas de suie
    float4 _Zone0;         // là où les mains frottent : centre, rayon (nul : pas de zone)
    float4 _Zone1;
    float4 _ForcesZones;   // x : force de la zone 0, y : de la zone 1
    // le décalage (le « Location » du nœud Mapping) que kit_lowpoly a tiré pour chaque bruit
    float4 _DecVariation;
    float4 _DecDelavage;
    float4 _DecBords;
    float4 _DecEclats;
    float4 _DecZoneEclats;
    float4 _DecZoneRayures;
    float4 _DecRayures1;
    float4 _DecRayures2;
    float4 _DecRetouches;
    float4 _DecRouille;
    float4 _DecCloques;
    float4 _DecZoneCloques;
    float4 _DecCoulures;
    float4 _DecZoneCoulures;
    float4 _DecHuile;
    float4 _DecHuileDetail;
    float4 _DecPoussiere;
    float4 _DecSuie;
    float _Rug;
    float _Metal;
    float _ADessous;
    float _RugDessous;
    float _MetalDessous;
    float _Bords;
    float _Eclats;
    float _EchelleEclats;
    float _Rayures;
    float _Salete;
    float _Variation;
    float _ForceEmission;
    float _Coat;
    float _ACouche;
    float _RugCouche;
    float _Retouches;
    float _Rouille;
    float _Huile;
    float _Poussiere;
    float _Delavage;
CBUFFER_END

TEXTURE2D(_Occlusion);
SAMPLER(sampler_Occlusion);

// Le bruit, le Voronoï et l'usure elle-même : partagés avec le décor (DuelArena/Decor, 2026-10-27).
#include "UsureBlender.hlsl"

#endif
