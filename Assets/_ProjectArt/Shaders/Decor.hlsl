// Le décor des arènes (2026-10-27, bible § 10 et § 13) : un seul matériau pour les murs, le sol, la charpente
// et les pièces modelées dans Blender, sur un fond sombre et terne qui laisse les robots trancher.
//
// Deux façons de poser l'usure (_Espace) :
// - 0, l'architecture : les murs du blocking, le sol, la salle. Aucun dépliage : les motifs (béton banché et
//   ses trous de banche, bardage riveté, dalle sciée, tôle striée) se lisent sur la position du monde,
//   tournée dans les axes de l'objet (un mur en biais garde ses panneaux droits) ; deux blocs voisins de
//   même orientation se raccordent. Puis ce que la hauteur décide : le soubassement peint, le liseré, les
//   hachures, la crasse au pied des murs ; et ce qu'une salle de tir accumule : les impacts de balles, les
//   éclats sur les arêtes (couleur de sommet rouge, comme les armes).
// - 1, une pièce de Blender (caisse, projecteur, poteau de mesure...) : l'usure des armes (UsureBlender.hlsl),
//   aux paramètres que l'export écrit, pour que la pièce ait dans le jeu l'aspect de ses rendus de Blender.
//
// Le rendu se choisit pour toute la scène (_DecorStyle, global) : 0 le low poly propre (PBR d'URP), 1 le
// cel-shading (aplats, sans usure), 2 les deux mêlés (aplats, usure gardée). C'est l'essai demandé par
// l'utilisateur le 2026-10-27, avant de choisir le rendu du jeu.
#ifndef DUELARENA_DECOR_INCLUDED
#define DUELARENA_DECOR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Une seule disposition pour toutes les passes : le SRP Batcher l'exige.
CBUFFER_START(UnityPerMaterial)
    // Les paramètres d'ArmeUsee, aux mêmes noms : WeaponImporter.ApplyParams les écrit pour une pièce de Blender.
    half4 _Base;
    half4 _Dessous;
    half4 _Couche;
    half4 _TeinteRetouches;
    half4 _TeinteRouille;
    half4 _EmissionCouleur;
    float4 _Suie;
    float4 _Zone0;
    float4 _Zone1;
    float4 _ForcesZones;
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

    // Le décor
    float _Espace;               // 0 : architecture ; 1 : pièce de Blender
    float _Motif;                // 0 : aucun, 1 : béton banché, 2 : bardage riveté, 3 : dalle sciée, 4 : tôle striée
    float4 _Panneau;             // largeur, hauteur (m), largeur du joint (m), assombrissement du joint
    float4 _Trous;               // trous de banche : colonnes, rangées par panneau, rayon (m), assombrissement
    float _VariationPanneau;     // écart de teinte d'un panneau (ou d'une dalle) à l'autre
    float _Nuages;               // teinte nuageuse : force
    float _EchelleNuages;        // et taille (par m)
    float4 _Soubassement;        // hauteur du soubassement peint (m, 0 : aucun), bas du liseré, haut du liseré, usure
    half4 _CouleurSoubassement;
    half4 _CouleurLisere;
    float4 _Hachures;            // bas, haut (m), période (m, 0 : aucune), usure
    half4 _CouleurHachures;
    float4 _Crasse;              // hauteur de la crasse au pied (m), force, coulures, poussière des dessus
    float4 _Impacts;             // taille d'une cellule (m), probabilité d'un impact, rayon (m), hauteur maximale (m)
    half4 _CouleurEclats;        // ce que les éclats des arêtes découvrent (alpha : métal)
    float _EclatsArchi;
    float _Huiles;               // taches d'huile au sol
    float _ContourLisse;         // le contour gonfle le long des tangentes (normales lissées)
    float _Contour;              // épaisseur du contour (pixels en 1080p)
    half4 _CouleurContour;
    float _Decoupe;              // marquage : seuil de la peinture (0 : opaque)
    half4 _CouleurMarquage;
    float _UsureMarquage;
    float4 _MarquageTex_ST;
CBUFFER_END

TEXTURE2D(_Occlusion);
SAMPLER(sampler_Occlusion);
TEXTURE2D(_MarquageTex);
SAMPLER(sampler_MarquageTex);

// Le rendu de toute la scène (Shader.SetGlobalFloat) : 0 low poly propre, 1 cel-shading, 2 les deux mêlés.
float _DecorStyle;

#include "UsureBlender.hlsl"

// --- le repère ------------------------------------------------------------------------------------

// La position du monde dans les axes de l'objet, en mètres (sans son échelle ni son origine) : les motifs
// d'un mur tourné suivent le mur, et deux blocs de même orientation se raccordent.
void DecorRepere(float3 positionWS, float3 normalWS, out float3 aligne, out float3 normaleAlignee)
{
    float4x4 m = GetObjectToWorldMatrix();
    float3 ax = normalize(m._m00_m10_m20);
    float3 ay = normalize(m._m01_m11_m21);
    float3 az = normalize(m._m02_m12_m22);
    aligne = float3(dot(positionWS, ax), dot(positionWS, ay), dot(positionWS, az));
    normaleAlignee = float3(dot(normalWS, ax), dot(normalWS, ay), dot(normalWS, az));
}

// Les coordonnées de la face, en mètres : u le long de la face, v vers le haut ; sur un dessus, le plan (x, z).
float2 DecorFace(float3 aligne, float3 normaleAlignee, out float dessus)
{
    float3 a = abs(normaleAlignee);
    dessus = a.y >= max(a.x, a.z) ? 1.0 : 0.0;
    if (dessus > 0.5) return aligne.xz;
    return a.x >= a.z ? float2(aligne.z, aligne.y) : float2(aligne.x, aligne.y);
}

// --- les motifs -------------------------------------------------------------------------------------

// La part du pixel que couvre un trait de demi-largeur `demi` à la distance `d` : net de près, et de loin,
// quand il est plus fin qu'un pixel, sa vraie part (comme les rayures des armes).
float DecorTrait(float d, float demi)
{
    float w = max(fwidth(d), 1e-6);
    return saturate(min(demi + 0.5 * w - d, 2.0 * demi) / w);
}

// Les joints d'une grille de `taille` m : la distance au bord de la cellule.
float DecorJoints(float2 uv, float2 taille, float largeur)
{
    float2 f = frac(uv / taille) * taille;
    float2 d = min(f, taille - f);
    return DecorTrait(min(d.x, d.y), largeur * 0.5);
}

// Les trous de banche : `grille` (colonnes, rangées) par panneau, de `rayon` m. Rend (trou, anneau autour).
float2 DecorTrousDeBanche(float2 uv, float2 panneau, float2 grille, float rayon)
{
    float2 pas = panneau / max(grille, 1.0);
    float2 centre = (floor(uv / pas) + 0.5) * pas;
    float d = distance(uv, centre);
    float w = max(fwidth(d), 1e-5);
    float trou = saturate((rayon - d) / w + 0.5);
    float anneau = saturate((rayon * 1.9 - d) / w + 0.5) * (1.0 - trou);
    return float2(trou, anneau);
}

// La part qui reste d'un motif de `taille` m quand un pixel couvre `pied` m : entier de près, il s'efface vers sa
// moyenne quand il passe sous trois pixels (sans quoi, de loin, il scintille).
float DecorNettete(float pied, float taille)
{
    return saturate(1.0 - (pied * 3.0 - taille) / max(taille, 1e-4));
}

// Les rivets d'un bardage : une file de chaque côté d'un joint vertical, tous les 20 cm. Rend (rivet, sa part
// moyenne quand il s'efface).
float DecorRivets(float2 uv, float2 panneau)
{
    float u = frac(uv.x / panneau.x) * panneau.x;
    float du = min(u, panneau.x - u) - 0.04;
    float dv = (frac(uv.y / 0.2) - 0.5) * 0.2;
    float d = length(float2(du, dv));
    float w = max(fwidth(d), 1e-5);
    float rivet = saturate((0.0075 - d) / w + 0.5);
    return rivet * DecorNettete(length(fwidth(uv)), 0.015);
}

// La tôle striée : des larmes en relief tous les 3,2 cm, alternées à ±45°. Rend (larme, son côté éclairé), effacés
// vers leur moyenne au loin.
float2 DecorTole(float2 uv)
{
    const float pas = 0.032;
    float2 g = uv / pas;
    float2 cellule = floor(g);
    float2 f = frac(g) - 0.5;
    float sens = fmod(abs(cellule.x + cellule.y), 2.0) < 1.0 ? 1.0 : -1.0;
    float2 r = float2(f.x + sens * f.y, f.x - sens * f.y) * 0.7071;
    float e = length(r * float2(1.0, 3.4));
    float w = max(fwidth(e), 1e-4);
    float larme = saturate((0.30 - e) / w + 0.5);
    float lumiere = larme * saturate(0.5 + (r.x + r.y) * 1.6);
    float net = DecorNettete(length(fwidth(uv)), pas);
    return lerp(float2(0.22, 0.12), float2(larme, lumiere), net);
}

// Les impacts de balles : dans chaque cellule de `taille` m, un impact avec la probabilité `p`, de `rayon` m au
// plus. Rend (le trou, l'éclat clair qui l'entoure).
float2 DecorImpacts(float2 uv, float taille, float p, float rayon)
{
    float2 g = uv / taille;
    float2 cellule = floor(g);
    float meilleure = 8.0;
    [loop] for (int j = -1; j <= 1; j++)
    {
        [loop] for (int i = -1; i <= 1; i++)
        {
            float2 c = cellule + float2(i, j);
            float3 h = ArmeHachageCellule(int3((int2)c, 31));
            if (h.z > p) continue;
            float2 centre = (c + 0.15 + h.xy * 0.7) * taille;
            float r = rayon * (0.45 + 0.55 * h.z / max(p, 1e-3));
            meilleure = min(meilleure, distance(uv, centre) / r);
        }
    }
    float trou = 1.0 - smoothstep(0.30, 0.42, meilleure);
    float eclat = (1.0 - smoothstep(0.70, 1.0, meilleure)) * (1.0 - trou);
    // De loin, un impact plus petit que trois pixels s'efface : il ne ferait que scintiller.
    return float2(trou, eclat) * DecorNettete(length(fwidth(uv)), rayon * 1.2);
}

// --- l'architecture -----------------------------------------------------------------------------------

// La surface d'un mur, d'un sol, d'une charpente. `usure` faux : le cel-shading, en aplats (les motifs restent,
// comme des traits ; le hasard de l'usure s'en va). `proche` (0 à 1) : le détail fin (impacts, éclats, plaques de
// peinture, coulures, poussière) ne se calcule que de près, où il se voit ; de loin, il coûterait sans se voir
// (mesuré le 2026-10-27 : le décor coûtait 7 ms par image dans l'Editor, en 1080p).
ArmeSurface DecorArchitecture(float3 positionWS, float3 aligne, float3 normaleAlignee, float bord, bool usure, float proche)
{
    float dessus;
    float2 uv = DecorFace(aligne, normaleAlignee, dessus);
    float hauteur = positionWS.y;

    float3 couleur = _Base.rgb;
    float rug = _Rug;
    float metal = _Metal;
    float occlusion = 1.0;

    if (usure && _Nuages > 0.0)
    {
        float v = ArmeBruit(aligne * _EchelleNuages + 3.7, 1.0, 2, 0.55);
        couleur *= 1.0 + (v - 0.5) * 2.0 * _Nuages;
    }

    int motif = (int)round(_Motif);
    if (motif == 1 || motif == 3)
    {
        // Béton banché (murs) ou dalle sciée (sol) : des panneaux, un joint, une teinte par panneau.
        float2 panneau = _Panneau.xy;
        if (usure && _VariationPanneau > 0.0)
        {
            float3 h = ArmeHachageCellule(int3((int2)floor(uv / panneau), 17 + (int)dessus));
            couleur *= 1.0 + (h.x - 0.5) * 2.0 * _VariationPanneau;
        }
        float joint = (motif == 3 || dessus < 0.5) ? DecorJoints(uv, panneau, _Panneau.z) : 0.0;
        couleur *= 1.0 - joint * _Panneau.w;
        occlusion *= 1.0 - joint * 0.5;
        if (motif == 1 && dessus < 0.5 && _Trous.x > 0.0)
        {
            float2 t = DecorTrousDeBanche(uv, panneau, _Trous.xy, _Trous.z);
            couleur *= 1.0 - t.x * _Trous.w;
            if (usure) couleur *= 1.0 + t.y * 0.06;
            occlusion *= 1.0 - t.x * 0.6;
        }
        if (motif == 3 && usure && _Huiles > 0.0)
        {
            // Des taches d'huile au sol, rares : plus sombres, luisantes, aux bords fondus.
            float t = ArmeBruit(aligne * 0.55 + 41.0, 1.0, 2, 0.6);
            float o = smoothstep(0.70 - 0.08 * _Huiles, 0.80 - 0.08 * _Huiles, t);
            if (o > 0.0 && proche > 0.0) o *= lerp(1.0, 0.55 + 0.45 * ArmeBruit(aligne * 3.0 + 7.0, 1.0, 1, 0.5), proche);
            couleur *= 1.0 - o * 0.35;
            rug = lerp(rug, 0.3, o);
        }
    }
    else if (motif == 2)
    {
        // Bardage : des tôles peintes, leurs joints, une file de rivets de chaque côté des joints verticaux.
        float joint = dessus < 0.5 ? DecorJoints(uv, _Panneau.xy, _Panneau.z) : 0.0;
        couleur *= 1.0 - joint * _Panneau.w;
        occlusion *= 1.0 - joint * 0.4;
        if (dessus < 0.5)
        {
            float rivet = DecorRivets(uv, _Panneau.xy);
            couleur = lerp(couleur, couleur * 1.35 + 0.02, rivet);
        }
    }
    else if (motif == 4 && dessus > 0.5 && normaleAlignee.y > 0.0)
    {
        // Tôle striée, sur les dessus seulement : des larmes claires, et le métal un peu plus lisse.
        float2 t = DecorTole(uv);
        couleur *= 1.0 + t.y * 0.45 - (t.x - t.y) * 0.15;
        rug = lerp(rug, rug * 0.75, t.x);
    }

    // Le soubassement peint et son liseré, sur les faces verticales.
    if (_Soubassement.x > 0.0 && dessus < 0.5)
    {
        float wH = max(fwidth(hauteur), 1e-4);
        float bande = saturate((_Soubassement.x - hauteur) / wH + 0.5);
        float lisere = saturate((hauteur - _Soubassement.y) / wH + 0.5) * saturate((_Soubassement.z - hauteur) / wH + 0.5);
        if (usure && _Soubassement.w > 0.0 && proche > 0.0)
        {
            // La peinture s'en va par petites plaques, surtout en bas, où l'on frotte.
            float n = ArmeBruit(aligne * 7.0 + 13.0, 1.0, 3, 0.6);
            float seuil = 0.74 - 0.2 * _Soubassement.w * (1.0 - saturate(hauteur / 0.6) * 0.7);
            float partie = ArmeAuDessus(n, seuil) * proche;
            bande *= 1.0 - partie;
            lisere *= 1.0 - partie;
        }
        couleur = lerp(couleur, _CouleurSoubassement.rgb, bande);
        couleur = lerp(couleur, _CouleurLisere.rgb, lisere);
        rug = lerp(rug, 0.6, max(bande, lisere));
        metal = lerp(metal, 0.0, max(bande, lisere));
    }

    // Les hachures d'avertissement, en biais, entre deux hauteurs.
    if (_Hachures.z > 0.0 && dessus < 0.5)
    {
        float wH = max(fwidth(hauteur), 1e-4);
        float dans = saturate((hauteur - _Hachures.x) / wH + 0.5) * saturate((_Hachures.y - hauteur) / wH + 0.5);
        float s = (uv.x + hauteur) / _Hachures.z;
        float bandes = DecorTrait(abs(frac(s) - 0.25), 0.25);
        float h = dans * bandes;
        if (usure && _Hachures.w > 0.0 && proche > 0.0 && h > 0.0)
            h *= 1.0 - ArmeAuDessus(ArmeBruit(aligne * 4.1 + 29.0, 1.0, 3, 0.6), 0.70 - 0.2 * _Hachures.w) * proche;
        couleur = lerp(couleur, _CouleurHachures.rgb, h);
        rug = lerp(rug, 0.55, h);
    }

    if (usure)
    {
        // Les impacts de balles, sur les faces verticales et à hauteur de robot surtout.
        if (_Impacts.y > 0.0 && dessus < 0.5 && hauteur < _Impacts.w && proche > 0.0)
        {
            float2 i = DecorImpacts(uv, _Impacts.x, _Impacts.y, _Impacts.z) * proche;
            couleur = lerp(couleur, couleur * 0.28, i.x);
            couleur = lerp(couleur, _CouleurEclats.rgb, i.y * 0.75);
            occlusion *= 1.0 - i.x * 0.5;
            rug = lerp(rug, 0.9, i.y);
        }

        // Les arêtes écaillées (couleur de sommet rouge des chanfreins).
        if (_EclatsArchi > 0.0 && bord > 0.0 && proche > 0.0)
        {
            float b = ArmeBruit(aligne + 5.0, 14.0, 3, 0.6);
            float e = bord * ArmeAuDessus(b, 0.72 - 0.2 * _EclatsArchi) * proche;
            couleur = lerp(couleur, _CouleurEclats.rgb, e);
            metal = lerp(metal, _CouleurEclats.a, e);
            rug = lerp(rug, 0.45, e * _CouleurEclats.a);
        }

        // La crasse au pied des murs, des coulures depuis leur sommet, la poussière sur les dessus.
        if (_Crasse.y > 0.0)
        {
            float g = saturate(1.0 - hauteur / max(_Crasse.x, 1e-3));
            float n = 0.5;
            if (g > 0.0) n = ArmeBruit(aligne + 11.0, 2.3, 2, 0.55);   // (un ternaire calculerait les deux branches)
            float c = g * g * (0.5 + 0.5 * n) * _Crasse.y;
            couleur *= 1.0 - c * 0.6;
            rug = saturate(rug + c * 0.15);
        }
        if (_Crasse.z > 0.0 && dessus < 0.5 && proche > 0.0)
        {
            float n = ArmeBruit(float3(uv.x * 9.0, uv.y * 0.35, 0.0) + 23.0, 1.0, 1, 0.5);
            couleur *= 1.0 - ArmeAuDessus(n, 0.60) * _Crasse.z * 0.3 * proche;
        }
        if (_Crasse.w > 0.0 && dessus > 0.5)
        {
            float tache = 0.5;
            if (proche > 0.0) tache = lerp(0.5, ArmeBruit(aligne + 3.0, 5.0, 2, 0.6), proche);
            float p = (0.35 + 0.65 * tache) * _Crasse.w;
            couleur = lerp(couleur, float3(0.33, 0.32, 0.30), saturate(p));
            rug = lerp(rug, 0.95, saturate(p));
        }
    }

    ArmeSurface s;
    s.albedo = saturate(couleur);
    s.metallic = saturate(metal);
    s.smoothness = saturate(1.0 - rug);
    s.emission = _EmissionCouleur.rgb * _ForceEmission;
    s.occlusion = occlusion;
    s.coat = 0;
    return s;
}

// La crasse du pied s'étend aux pièces de Blender posées au sol : le même sol les salit.
void DecorCrassePiece(inout ArmeSurface s, float3 positionWS, bool usure)
{
    if (!usure || _Crasse.y <= 0.0) return;
    float g = saturate(1.0 - positionWS.y / max(_Crasse.x, 1e-3));
    if (g <= 0.0) return;
    float n = ArmeBruit(positionWS + 11.0, 2.3, 2, 0.55);
    float c = g * g * (0.5 + 0.5 * n) * _Crasse.y;
    s.albedo *= 1.0 - c * 0.6;
    s.smoothness = saturate(s.smoothness - c * 0.15);
}

// La part de détail fin selon la distance à la caméra : entière jusqu'à 18 m, nulle au-delà de 26 m.
float DecorProche(float3 positionWS)
{
    return saturate((26.0 - distance(positionWS, GetCameraPositionWS())) / 8.0);
}

// La surface, selon l'espace et le rendu. `positionOS`, `normalOS`, `uv` : ceux de la pièce (espace 1). `proche` : la
// part de détail fin (DecorProche ; 1 pour la cuisson de la lumière).
ArmeSurface DecorSurface(float3 positionWS, float3 aligne, float3 normaleAlignee, float3 positionOS, float3 normalOS,
                         float2 uv, float bord, float proche)
{
    bool usure = _DecorStyle < 0.5 || _DecorStyle > 1.5;
    ArmeSurface s;
    if (_Espace > 0.5)
    {
        s = ArmeUsure(positionOS, normalOS, uv, bord);
        if (!usure)
        {
            s.albedo = _Base.rgb;
            s.metallic = _Metal;
            s.smoothness = 1.0 - _Rug;
            s.occlusion = 1.0;
        }
        DecorCrassePiece(s, positionWS, usure);
    }
    else
    {
        s = DecorArchitecture(positionWS, aligne, normaleAlignee, bord, usure, proche);
    }
    return s;
}

// Le marquage peint (_Decoupe > 0) : la peinture de la texture, usée par plaques ; ce qui n'en a plus est
// découpé, le support se voit à travers.
void DecorMarquage(inout ArmeSurface s, float2 uv, float3 positionWS)
{
    if (_Decoupe <= 0.0) return;
    half4 t = SAMPLE_TEXTURE2D(_MarquageTex, sampler_MarquageTex, uv * _MarquageTex_ST.xy + _MarquageTex_ST.zw);
    float peinture = t.a;
    bool usure = _DecorStyle < 0.5 || _DecorStyle > 1.5;
    if (usure && _UsureMarquage > 0.0)
        peinture *= 1.0 - ArmeAuDessus(ArmeBruit(positionWS + 17.0, 3.3, 3, 0.62), 0.72 - 0.25 * _UsureMarquage);
    clip(peinture - _Decoupe);
    s.albedo = _CouleurMarquage.rgb * t.rgb;
    s.metallic = 0;
    s.smoothness = 0.35;
}

#endif
