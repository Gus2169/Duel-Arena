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

// --- bruit ---------------------------------------------------------------------------------------

uint ArmeRot(uint x, uint k)
{
    return (x << k) | (x >> (32u - k));
}

// lookup3 de Bob Jenkins (trois mots, sa fonction « final ») : le hachage des sommets du Perlin.
uint ArmeHachage(uint3 k)
{
    uint a = 0xDEADBEEFu + (3u << 2) + 13u;
    uint b = a;
    uint c = a;
    a += k.x;
    b += k.y;
    c += k.z;
    c ^= b; c -= ArmeRot(b, 14u);
    a ^= c; a -= ArmeRot(c, 11u);
    b ^= a; b -= ArmeRot(a, 25u);
    c ^= b; c -= ArmeRot(b, 16u);
    a ^= c; a -= ArmeRot(c, 4u);
    b ^= a; b -= ArmeRot(a, 14u);
    c ^= b; c -= ArmeRot(b, 24u);
    return c;
}

// PCG 3D en entiers signés (le décalage à droite garde le signe), ramené dans [0, 1] : le hachage
// des cellules du Voronoï, le germe comme la couleur.
float3 ArmeHachageCellule(int3 v)
{
    v = v * 1664525 + 1013904223;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return float3(v & 0x7FFFFFFF) * (1.0 / 2147483648.0);
}

// Les douze gradients du Perlin amélioré.
float ArmeGradient(uint h, float3 d)
{
    h &= 15u;
    float u = h < 8u ? d.x : d.y;
    float v = h < 4u ? d.y : ((h == 12u || h == 14u) ? d.x : d.z);
    return ((h & 1u) ? -u : u) + ((h & 2u) ? -v : v);
}

float ArmePerlin(float3 p)
{
    float3 cellule = floor(p);
    float3 f = p - cellule;
    uint3 i = asuint((int3)cellule);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float c000 = ArmeGradient(ArmeHachage(i), f);
    float c100 = ArmeGradient(ArmeHachage(i + uint3(1, 0, 0)), f - float3(1, 0, 0));
    float c010 = ArmeGradient(ArmeHachage(i + uint3(0, 1, 0)), f - float3(0, 1, 0));
    float c110 = ArmeGradient(ArmeHachage(i + uint3(1, 1, 0)), f - float3(1, 1, 0));
    float c001 = ArmeGradient(ArmeHachage(i + uint3(0, 0, 1)), f - float3(0, 0, 1));
    float c101 = ArmeGradient(ArmeHachage(i + uint3(1, 0, 1)), f - float3(1, 0, 1));
    float c011 = ArmeGradient(ArmeHachage(i + uint3(0, 1, 1)), f - float3(0, 1, 1));
    float c111 = ArmeGradient(ArmeHachage(i + uint3(1, 1, 1)), f - float3(1, 1, 1));
    float x00 = lerp(c000, c100, u.x);
    float x10 = lerp(c010, c110, u.x);
    float x01 = lerp(c001, c101, u.x);
    float x11 = lerp(c011, c111, u.x);
    float y0 = lerp(x00, x10, u.y);
    float y1 = lerp(x01, x11, u.y);
    return 0.982 * lerp(y0, y1, u.z);
}

// Le nœud Noise Texture (fBm, normalisé, lacunarité 2) à l'échelle `echelle` : `detail` + 1 octaves.
float ArmeBruit(float3 p, float echelle, int detail, float rugosite)
{
    p *= echelle;
    float somme = 0.0, amplitude = 1.0, total = 0.0, frequence = 1.0;
    [loop] for (int o = 0; o <= detail; o++)
    {
        somme += ArmePerlin(p * frequence) * amplitude;
        total += amplitude;
        amplitude *= rugosite;
        frequence *= 2.0;
    }
    return 0.5 * somme / total + 0.5;
}

// Le nœud Voronoi (F1, euclidien, hasard 1) : la position du point le plus proche (dans l'espace
// d'entrée, comme sa sortie Position) et la couleur de sa cellule.
void ArmeVoronoi(float3 p, float echelle, out float3 position, out float3 couleur)
{
    float3 c = p * echelle;
    float3 cellule = floor(c);
    float3 local = c - cellule;
    int3 ci = (int3)cellule;
    float meilleure = 8.0;
    float3 cible = 0.0;
    int3 voisine = 0;
    [loop] for (int k = -1; k <= 1; k++)
    {
        [loop] for (int j = -1; j <= 1; j++)
        {
            [loop] for (int i = -1; i <= 1; i++)
            {
                int3 o = int3(i, j, k);
                float3 germe = float3(o) + ArmeHachageCellule(ci + o);
                float d = distance(germe, local);
                if (d < meilleure)
                {
                    meilleure = d;
                    cible = germe;
                    voisine = o;
                }
            }
        }
    }
    position = (cible + cellule) / echelle;
    couleur = ArmeHachageCellule(ci + voisine);
}

// x > seuil (le Math GREATER_THAN des matériaux), adouci sur la largeur d'un pixel : les bords des
// éclats ne crénèlent pas. La largeur est bornée : de loin, un pixel couvre beaucoup de motifs, et un
// passage trop doux mettrait du métal nu partout à moitié.
float ArmeAuDessus(float x, float seuil)
{
    float w = clamp(fwidth(x), 1e-5, 0.1);
    return saturate((x - seuil) / w + 0.5);
}

// Rayures courtes et droites (kit._Graphe.rayures) : dans chaque cellule, une tranche fine de normale
// tirée au hasard, bornée par une sphère ; sur une face, c'est un trait droit.
float ArmeRayures(float3 p, float echelle, float largeur, float longueur, float densite, float3 decalage)
{
    float3 m = p + decalage;
    float3 position, couleur;
    ArmeVoronoi(m, echelle, position, couleur);
    float3 d = m - position;
    float3 n = normalize(couleur - 0.5 + 1e-6);
    float plan = abs(dot(d, n));
    // la part du pixel que couvre le trait (largeur 2 × largeur) : nette de près, et de loin, quand il
    // est plus fin qu'un pixel, sa vraie part (il ne s'élargit pas)
    float w = max(fwidth(plan), 1e-6);
    float tranche = saturate(min(largeur + 0.5 * w - plan, 2.0 * largeur) / w);
    float sphere = step(length(d), longueur);
    float garde = step(couleur.x, densite);
    return tranche * sphere * garde;
}

// --- l'usure ---------------------------------------------------------------------------------------

struct ArmeSurface
{
    half3 albedo;
    half metallic;
    half smoothness;
    half3 emission;
    half occlusion;
    half coat;
};

// positionOS, normalOS : repère objet d'Unity ; uv : le dépliage de l'occlusion ; bord : la couleur de
// sommet rouge. Toutes les branches portent sur des paramètres du matériau : elles sont uniformes, et
// les dérivées (fwidth) y restent valables.
ArmeSurface ArmeUsure(float3 positionOS, float3 normalOS, float2 uv, float bord)
{
    // Le repère de l'arme de Blender : exporter_armes.vers_axes_unity l'a fait pivoter de -90° autour
    // de X, et Unity a retourné X.
    float3 p = float3(-positionOS.x, -positionOS.z, positionOS.y);
    float haut = normalize(normalOS).y;

    // là où les mains frottent (finition « polie par les mains ») : 0..1
    float frottement = 0.0;
    if (_Zone0.w > 0.0) frottement = saturate(1.0 - distance(p, _Zone0.xyz) / _Zone0.w) * _ForcesZones.x;
    if (_Zone1.w > 0.0) frottement = saturate(frottement + saturate(1.0 - distance(p, _Zone1.xyz) / _Zone1.w) * _ForcesZones.y);

    // teinte de base légèrement nuageuse, puis passée par plaques
    float3 couleur = _Base.rgb;
    if (_Variation != 0.0)
    {
        float v = ArmeBruit(p + _DecVariation.xyz, 4.0, 2, 0.5);
        couleur = _Base.rgb * ((v - 0.5) * _Variation * 2.0 + 1.0);
    }
    if (_Delavage != 0.0)
    {
        float plaque = saturate((0.4 + ArmeBruit(p + _DecDelavage.xyz, 2.5, 2, 0.5) * 0.9) * 0.4 * _Delavage);
        couleur = lerp(couleur, float3(0.24, 0.24, 0.235), plaque);
    }

    // ce que la peinture laisse voir : bords, éclats, rayures (et la couche qui borde les éclats)
    float masque = 0.0;
    float large = 0.0;
    if (_ADessous > 0.5)
    {
        if (_Bords > 0.0)
        {
            float b = ArmeBruit(p + _DecBords.xyz, 38.0, 4, 0.6);
            masque = max(masque, bord * ArmeAuDessus(b, 0.64 - 0.24 * _Bords - frottement * 0.40));
            if (_ACouche > 0.5) large = max(large, bord * ArmeAuDessus(b, 0.64 - 0.24 * _Bords - 0.10 - frottement * 0.40));
        }
        if (_Eclats > 0.0)
        {
            float e = ArmeBruit(p + _DecEclats.xyz, _EchelleEclats, 4, 0.6);
            float zone = ArmeAuDessus(ArmeBruit(p + _DecZoneEclats.xyz, 4.0, 2, 0.5), 0.56);
            if (_Zone0.w > 0.0) zone = max(zone, ArmeAuDessus(frottement, 0.25));
            masque = max(masque, ArmeAuDessus(e, 0.70 - 0.08 * _Eclats - frottement * 0.30) * zone);
            if (_ACouche > 0.5) large = max(large, ArmeAuDessus(e, 0.70 - 0.08 * _Eclats - 0.075 - frottement * 0.30) * zone);
        }
        if (_Rayures > 0.0)
        {
            float zone = ArmeAuDessus(ArmeBruit(p + _DecZoneRayures.xyz, 3.0, 2, 0.5), 0.60 - 0.10 * _Rayures);
            float r1 = ArmeRayures(p, 55.0, 0.00032, 0.011, 0.55 * _Rayures, _DecRayures1.xyz);
            float r2 = ArmeRayures(p, 110.0, 0.00022, 0.005, 0.5 * _Rayures, _DecRayures2.xyz);
            masque = max(masque, max(r1, r2) * zone);
        }
        couleur = lerp(couleur, _Couche.rgb, large);
        couleur = lerp(couleur, _Dessous.rgb, masque);
    }

    float rugosite = lerp(_Rug, _RugCouche, large);
    rugosite = lerp(rugosite, saturate(_RugDessous - frottement * 0.22), masque);
    float metallique = lerp(lerp(_Metal, 0.0, large), _MetalDessous, masque);

    // peinture refaite au pinceau par-dessus l'usure, d'une teinte qui ne colle pas tout à fait
    if (_Retouches != 0.0)
    {
        float t = ArmeAuDessus(ArmeBruit(p + _DecRetouches.xyz, 7.0, 2, 0.45), 0.66 - 0.12 * _Retouches);
        couleur = lerp(couleur, _TeinteRetouches.rgb, t);
        rugosite = lerp(rugosite, 0.44, t);
        metallique = lerp(metallique, 0.0, t);
        masque *= 1.0 - t;
    }

    float occlusion = SAMPLE_TEXTURE2D(_Occlusion, sampler_Occlusion, uv).r;
    float creux = saturate(1.0 - occlusion);

    // la rouille mange le métal mis à nu, cloque sous la peinture, remplit les creux et coule
    if (_Rouille != 0.0)
    {
        float r = ArmeBruit(p + _DecRouille.xyz, 26.0, 4, 0.65);
        float3 teinte = _TeinteRouille.rgb * (0.55 + r * 0.6);
        float rousse = saturate(creux * 2.4 * _Rouille * r);
        rousse = max(rousse, masque * ArmeAuDessus(r, 0.62 - 0.30 * _Rouille));
        float cloques = ArmeAuDessus(ArmeBruit(p + _DecCloques.xyz, 16.0, 4, 0.7), 0.71 - 0.12 * _Rouille);
        rousse = max(rousse, cloques * ArmeAuDessus(ArmeBruit(p + _DecZoneCloques.xyz, 2.5, 2, 0.5), 0.50));
        float coulures = ArmeAuDessus(ArmeBruit(p * float3(1.0, 1.0, 0.14) + _DecCoulures.xyz, 14.0, 3, 0.6), 0.61 - 0.10 * _Rouille);
        float zone = ArmeAuDessus(ArmeBruit(p + _DecZoneCoulures.xyz, 3.0, 2, 0.5), 0.52);
        rousse = max(rousse, coulures * zone * 0.6 * _Rouille);
        couleur = lerp(couleur, teinte, rousse);
        rugosite = lerp(rugosite, 0.92, rousse);
        metallique = lerp(metallique, 0.0, rousse);
    }

    // taches d'huile : plus sombres, luisantes
    if (_Huile != 0.0)
    {
        float o = ArmeAuDessus(ArmeBruit(p + _DecHuile.xyz, 6.0, 3, 0.6), 0.58 - 0.16 * _Huile)
                  * (0.5 + ArmeBruit(p + _DecHuileDetail.xyz, 30.0, 2, 0.5) * 0.5);
        couleur *= 1.0 - o * 0.55;
        rugosite = lerp(rugosite, 0.12, o);
    }

    // la crasse des creux
    if (_Salete > 0.0)
    {
        float sale = saturate(creux * 1.6 * _Salete);
        couleur *= 1.0 - sale * 0.75;
        rugosite = saturate(rugosite + sale * 0.25);
    }

    // une poussière grise dans les creux et sur les faces tournées vers le haut
    if (_Poussiere != 0.0)
    {
        float dessus = saturate((haut - 0.55) * 3.0);
        float tache = 0.35 + ArmeBruit(p + _DecPoussiere.xyz, 14.0, 3, 0.6) * 0.65;
        float d = saturate(max(creux * 2.6, dessus * 0.8) * tache * _Poussiere);
        couleur = lerp(couleur, float3(0.32, 0.31, 0.29), d);
        rugosite = lerp(rugosite, 0.95, d);
    }

    // la suie de la bouche
    if (_Suie.w > 0.0)
    {
        float noirci = saturate(1.0 - distance(p, _Suie.xyz) / _Suie.w);
        noirci = saturate(noirci * (0.55 + ArmeBruit(p + _DecSuie.xyz, 60.0, 3, 0.6) * 0.6));
        couleur *= 1.0 - noirci * 0.85;
        rugosite = lerp(rugosite, 0.85, noirci);
    }

    ArmeSurface s;
    s.albedo = saturate(couleur);
    s.metallic = saturate(metallique);
    s.smoothness = saturate(1.0 - rugosite);
    s.emission = _EmissionCouleur.rgb * _ForceEmission;
    s.occlusion = occlusion;
    s.coat = _Coat;
    return s;
}

#endif
