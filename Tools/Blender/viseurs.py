"""Les quinze viseurs de Duel Arena (Blender 5.2, low poly) : ceux de Rainbow Six Siege que l'utilisateur
a donnés en référence (2026-10-28 : points rouges OTAN, russe et M4S ; réflex OTAN, russe, MRS et
« Vintage » ; holographiques OTAN, russe, Razor et MH1 ; lunettes 2,0x, 2,5x OTAN et russe, 3,0x), mêlés
de l'allure futuriste de Destiny 2 qu'il demande : boîtiers à pans en deux tons (anthracite et céramique
blanc cassé, comme les armes laser), fenêtres au bord éclairé, projections, petits afficheurs. Validés
par l'utilisateur en quatre tours de revue (la page « Refonte des viseurs ») ; les dix d'avant sont dans
archives/viseurs_rainbow_v1.py.

Une référence donne une grammaire, pas un plan (skill blender) : chaque viseur reprend l'archétype de
celui de Rainbow Six (sa silhouette, sa place sur le rail, son genre de réticule), pas son dessin ; de
Destiny 2 ne vient qu'un langage (fenêtres en stade, bords éclairés, réticules géométriques fins,
afficheurs de données), jamais un modèle.

Couleurs (bible § 5 et § 12) : aucune couleur de robot sur un boîtier. Ce qui luit est rouge, l'énergie
des armes laser ; un hologramme est d'un blanc froid. Les réticules restent ceux de la bible : rouges
aux points rouges, aux holographiques et aux lunettes, verts aux réflex.

Les réticules (MOTIFS) sont décrits en milliradians, tels que l'œil les voit : ceux des lunettes en
milliradians de la scène, grossis à l'écran comme elle. Le jeu les lit dans une image (ecrire_reticule :
la distance au réticule autour du point visé, que le shader DuelArena/ViseurReticule dessine à l'infini),
et les lunettes grossissent la vue (la fiche porte leur grossissement et leur œilleton).

  blender -b --factory-startup -P viseurs.py -- --rendus <dossier> [--seuls PR-A,HO-B]
          [--sur-arme fusil_assaut.py] [--sortie f.blend]

Avec --rendus, le dossier reçoit en JPEG trois vues de chaque viseur, sa vue montée sur l'arme et sa
vue de l'œil en visée, la planche des viseurs, celle des réticules, et manifeste.json (la page de revue
s'en sert). L'export vers Unity : exporter_accessoires.py --scripts viseurs.py.
"""

import argparse
import importlib
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_accessoires as acc  # noqa: E402

importlib.reload(acc)
ca = acc.ca
kit = acc.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece, srgb  # noqa: E402

DROITE = acc.DROITE
VERS_TIREUR = acc.VERS_TIREUR
OEIL = 0.08                       # l'œil, derrière la mire d'une lunette (aimEyeDistance des viseurs du jeu)
FENETRE_CIBLE = 15.0              # degrés : la fenêtre utile de tous les viseurs sans grossissement (voir plus bas)
OEIL_MIN, OEIL_MAX = 0.045, 0.105  # au-delà, l'arme et les bras bougeraient trop d'un viseur à l'autre
BLANC_FROID = srgb(214, 232, 255)  # un hologramme : jamais une couleur de robot
VUES_STUDIO = ("trois_quarts", "avant_droit", "arriere")


# --- matériaux propres aux viseurs ----------------------------------------------------------------

def hologramme(nom, couleur, force, alpha):
    """De la lumière sans matière : une émission à demi transparente (un afficheur, une projection)."""
    mat = bpy.data.materials.get(nom) or bpy.data.materials.new(nom)
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (0.0, 0.0, 0.0, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.4
    bsdf.inputs["Emission Color"].default_value = couleur
    bsdf.inputs["Emission Strength"].default_value = force
    bsdf.inputs["Alpha"].default_value = alpha
    nt.links.new(bsdf.outputs["BSDF"], sortie.inputs["Surface"])
    mat.surface_render_method = "BLENDED"
    mat.use_backface_culling = False
    mat.diffuse_color = (couleur[0], couleur[1], couleur[2], alpha)
    return mat


def noir_mat(nom="DA_Noir_Mat"):
    """Le noir des intérieurs d'optique : sans reflet, même rasant (le noir du kit, à demi métallique,
    renvoyait le décor sur les parois d'un tube vues presque à plat)."""
    mat = bpy.data.materials.get(nom) or bpy.data.materials.new(nom)
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = srgb(9, 9, 10)
    bsdf.inputs["Roughness"].default_value = 1.0
    bsdf.inputs["Specular IOR Level"].default_value = 0.0
    nt.links.new(bsdf.outputs["BSDF"], sortie.inputs["Surface"])
    mat.diffuse_color = srgb(9, 9, 10)
    return mat


def completer_materiaux(a):
    # commun_armes.materiaux les déclare (l'export vers Unity y lit leurs paramètres) ; dans Blender, ils
    # sont refaits ici : sans reflet, hologrammes à demi transparents
    a.M["Holo"] = hologramme("DA_Hologramme", BLANC_FROID, 1.4, 0.55)
    a.M["HoloRouge"] = hologramme("DA_Hologramme_Rouge", ca.ROUGE_LUEUR, 2.4, 0.7)
    a.M["NoirMat"] = noir_mat()
    # les vitres à la teinte du jeu (12 %, shader du réticule) : à 22 %, elles voilaient l'image de visée
    for cle, teinte in acc.TEINTES_VITRE.items():
        a.M["Vitre_" + cle] = bpy.data.materials.get(f"DA_Vitre_{cle}_12") or acc.vitre(f"DA_Vitre_{cle}_12", teinte, 0.12)


# --- la fenêtre utile ---------------------------------------------------------------------------------
#
# L'utilisateur veut que tous les viseurs sans grossissement montrent autant de décor par leur fenêtre
# (2026-10-28 : l'égalité entre les joueurs). La fenêtre utile est ce que l'œil voit à travers toutes les
# ouvertures que son regard traverse en visée : la vitre, le bord intérieur des cadres et des liserés, à
# l'avant comme à l'arrière, l'alésage d'un tube. Toutes sont convexes : leur intersection vue de l'œil se
# calcule en découpant leurs projections l'une par l'autre. Chaque viseur prend la distance de l'œil qui
# la met à FENETRE_CIBLE (le diamètre apparent d'un disque de même surface) ; s'il n'y arrive pas entre
# OEIL_MIN et OEIL_MAX, c'est sa forme qui se retouche (sa fenêtre, sa profondeur).

_ouvertures = None   # celles du viseur en construction : les fonctions de forme y ajoutent les leurs


def ouverture(points):
    """Une ouverture que le regard traverse en visée (un contour convexe, en points du viseur)."""
    if _ouvertures is not None:
        _ouvertures.append([Vector(p) for p in points])


def _aire(poly):
    return 0.5 * sum(p[0] * q[1] - q[0] * p[1] for p, q in zip(poly, poly[1:] + poly[:1]))


def _decouper(sujet, coupe):
    """La partie du polygone sujet dans le polygone convexe coupe (Sutherland-Hodgman ; tous deux dans le
    sens trigonométrique)."""
    for a, b in zip(coupe, coupe[1:] + coupe[:1]):
        ex, ey = b[0] - a[0], b[1] - a[1]

        def dedans(p):
            return ex * (p[1] - a[1]) - ey * (p[0] - a[0]) >= 0.0

        def croisement(p, q):
            dx, dy = q[0] - p[0], q[1] - p[1]
            t = (ex * (a[1] - p[1]) - ey * (a[0] - p[0])) / (ex * dy - ey * dx)
            return p[0] + t * dx, p[1] + t * dy

        entree, sujet = sujet, []
        for p, q in zip(entree[-1:] + entree[:-1], entree):
            if dedans(q):
                if not dedans(p):
                    sujet.append(croisement(p, q))
                sujet.append(q)
            elif dedans(p):
                sujet.append(croisement(p, q))
        if not sujet:
            break
    return sujet


def fenetre_utile(ouvertures, oeil):
    """Ce que l'œil voit à travers toutes les ouvertures : le diamètre apparent (degrés) d'un disque de même
    surface sur l'écran."""
    poly = None
    for o in ouvertures:
        proj = []
        for p in o:
            r = p - oeil
            prof = r.dot(AVANT)
            proj.append((r.dot(DROITE) / prof, r.dot(HAUT) / prof))
        if _aire(proj) < 0.0:
            proj.reverse()
        poly = proj if poly is None else _decouper(poly, proj)
        if not poly:
            return 0.0
    return 2.0 * math.degrees(math.atan(math.sqrt(abs(_aire(poly)) / math.pi)))


def distance_egale(ouvertures, centre):
    """La distance de l'œil derrière la mire qui met la fenêtre utile à FENETRE_CIBLE (elle rapetisse
    quand l'œil recule)."""
    bas, haut = 0.005, 0.400
    for _ in range(50):
        milieu = 0.5 * (bas + haut)
        if fenetre_utile(ouvertures, centre + VERS_TIREUR * milieu) > FENETRE_CIBLE:
            bas = milieu
        else:
            haut = milieu
    return 0.5 * (bas + haut)


# --- formes ------------------------------------------------------------------------------------------

def apotheme8(r):
    """Distance de l'axe au milieu d'un pan, pour un octogone de rayon r (au sommet)."""
    return r * math.cos(math.pi / 8)


def assombrir(pc, faces, h0, x0=0.0, mat="NoirMat"):
    """Les faces tournées vers l'axe optique (l'intérieur d'un tube, d'une fenêtre) en noir mat, comme
    dans une vraie optique : de l'œil, elles faisaient un tunnel clair autour de l'image."""
    i_mat = pc.mat(mat)
    axe = P(0.0, h0, x0)
    for f in faces:
        if not f.is_valid:
            continue
        r = f.calc_center_median() - axe
        r -= AVANT * r.dot(AVANT)
        if r.length > 1e-6 and f.normal.dot(r.normalized()) < -0.5:
            f.material_index = i_mat


def tube8(pc, profil, h0, r_int, mat="Peinture", x=0.0):
    """Tube creux à huit pans, un pan à plat dessus : profil [(av, r)] de l'arrière vers l'avant, r au
    sommet des pans ; r_int constant ou un par point du profil. Son intérieur est noir ; son alésage compte
    dans la fenêtre utile."""
    f = acc.tube_creux(pc, profil, h0, r_int, x=x, n=8, mat=mat)
    assombrir(pc, f, h0, x)
    r_ints = list(r_int) if isinstance(r_int, (list, tuple)) else [r_int] * len(profil)
    for (av, _), ri in zip(profil, r_ints):
        ouverture(octogone(av, h0, ri, x))
    return f


def anneau_optique(pc, av0, av1, h0, r_ext, r_int, n, mat):
    """Un anneau que le regard traverse (un liseré dans un tube, la bague d'une lentille) : son bord
    intérieur compte dans la fenêtre utile."""
    f = kit.anneau(pc, av0, av1, h0, r_ext, r_int, n, mat)
    for av in (av0, av1):
        ouverture(kit.cercle(P(av, h0), AVANT, r_int, n, math.pi / n))
    return f


def octogone(av, h0, r, x=0.0):
    """Les huit sommets d'une section de tube8 (pour une vitre ou une lentille)."""
    return kit.cercle(P(av, h0, x), AVANT, r, 8, math.pi / 8)


def contour_capsule(dl, dh, n=8, cy=0.0):
    """Un stade (rectangle aux bouts en demi-cercles), en (x, h), sens trigonométrique."""
    cx = max(dl - dh, 0.0)
    pts = [(cx + dh * math.cos(math.radians(-90 + 180 * i / n)), cy + dh * math.sin(math.radians(-90 + 180 * i / n)))
           for i in range(n + 1)]
    pts += [(-cx + dh * math.cos(math.radians(90 + 180 * i / n)), cy + dh * math.sin(math.radians(90 + 180 * i / n)))
            for i in range(n + 1)]
    return pts


def decale(pts, e):
    """Le contour décalé de e vers l'extérieur (e < 0 : vers l'intérieur), même nombre de points."""
    return [(p[0], p[1]) for p in kit.decaler_contour(pts, e)]


def cadre_ferme(pc, av0, av1, exterieur, interieur, mat="Peinture", exterieur1=None, interieur1=None, pente=0.0,
                h_pente=0.0, sombre=True):
    """Un cadre fermé (une fenêtre, un capot) d'axe parallèle au canon. Contours (x, h) de même nombre
    de points et de même sens ; exterieur1, interieur1 : ceux de la face avant s'ils diffèrent (un capot
    évasé). pente : de combien le cadre avance par mètre de hauteur au-dessus de h_pente (> 0 : son haut
    part vers l'avant, < 0 : vers le tireur). sombre : ses faces intérieures en noir mat. Son bord intérieur,
    devant et derrière, compte dans la fenêtre utile."""
    ext1, int1 = exterieur1 or exterieur, interieur1 or interieur

    def boucle(av, pts):
        return [P(av + (h - h_pente) * pente, h, x) for x, h in pts]

    f = kit.loft(pc, [boucle(av0, exterieur), boucle(av1, ext1), boucle(av1, int1), boucle(av0, interieur)], mat,
                 cyclique=True)
    ouverture(boucle(av0, interieur))
    ouverture(boucle(av1, int1))
    if sombre:
        assombrir(pc, f, sum(h for _, h in interieur) / len(interieur), sum(x for x, _ in interieur) / len(interieur))
    return f


def lisere(pc, av0, av1, fenetre, largeur=0.0006, mat="Lueur", pente=0.0, h_pente=0.0):
    """Le bord éclairé d'une fenêtre (Destiny 2) : un ruban de lumière qui la borde à l'intérieur. Son
    dos entre de 0,2 mm dans le cadre, pour ne pas se battre avec sa face intérieure."""
    return cadre_ferme(pc, av0, av1, decale(fenetre, 0.0002), decale(fenetre, -largeur), mat, pente=pente,
                       h_pente=h_pente, sombre=False)


def vitre_points(a, coins, teinte):
    """La vitre (où Unity dessine le réticule) : elle compte dans la fenêtre utile."""
    ouverture(coins)
    return acc.vitre_plane(a, coins, teinte)


def vitre_contour(a, av, fenetre, teinte, pente=0.0, h_pente=0.0):
    return vitre_points(a, [P(av + (h - h_pente) * pente, h, x) for x, h in fenetre], teinte)


def coque8(pc, av0, av1, demi, bas, haut, ch_haut=0.004, ch_bas=0.0015, mats=None, mat="Peinture", quille=1.0,
           ch=0.0010, avant=()):
    """Un bloc à huit pans le long du canon, comme la coque des armes laser ; avant : des sections de
    plus, [(av, section8)], pour un nez. mats : un matériau par pan (ordre de commun_armes.section8 : 0
    chanfrein haut gauche, 1 dessus, 2 chanfrein haut droit, 3 flanc droit, 4 chanfrein bas droit,
    5 dessous, 6 chanfrein bas gauche, 7 flanc gauche)."""
    s = ca.section8(demi, bas, haut, ch_haut, ch_bas, quille)
    f = kit.loft(pc, kit.sections_av([(av0, s), (av1, s)] + list(avant)), mat, mats_cotes=mats)
    kit.chanfreiner(pc, ch, f)
    return f


def mats_pans(**pans):
    """Les matériaux des huit pans d'une coque8, nommés : flanc_gauche="Ceramique"..."""
    ordre = ("chanfrein_haut_gauche", "dessus", "chanfrein_haut_droit", "flanc_droit", "chanfrein_bas_droit",
             "dessous", "chanfrein_bas_gauche", "flanc_gauche")
    return [pans.get(nom) for nom in ordre]


def bouton(pc, centre, ex, ey, ez, dx, dy, dz=0.0012, mat="Caoutchouc"):
    f = kit.boite_axes(pc, centre + ez * dz, ex, ey, ez, dx, dy, dz, mat)
    kit.chanfreiner(pc, min(dx, dy) * 0.35, f)


def diodes(pc, coins_de, n, normale, mat="Lueur"):
    """n petites diodes (des quads) : coins_de(i) donne les coins de la i-ème."""
    for i in range(n):
        kit.quad(pc, coins_de(i), mat, normale=normale)


def mire(a, av, h):
    """L'axe optique, au dos du viseur (Unity y aligne l'œil en visée), et la distance de l'œil derrière
    lui en visée (WeaponAttachment.aimEyeDistance) : celle qui met la fenêtre utile à FENETRE_CIBLE, bornée
    entre OEIL_MIN et OEIL_MAX. Une lunette garde OEIL : elle se regarde par son œilleton."""
    a.mire(av, h)
    a.donnees["avMire"] = av
    if a.donnees.get("grossissement", 1.0) > 1.0 or not _ouvertures:
        a.donnees["distanceOeil"] = OEIL
        return
    centre = P(av, h)
    voulue = distance_egale(_ouvertures, centre)
    oeil = round(min(max(voulue, OEIL_MIN), OEIL_MAX), 4)
    a.donnees["distanceOeil"] = oeil
    a.donnees["fenetreUtile"] = round(fenetre_utile(_ouvertures, centre + VERS_TIREUR * oeil), 2)
    if abs(oeil - voulue) > 0.0005:
        print(f"  {a.code} : l'œil devrait être à {voulue * 100:.1f} cm pour une fenêtre utile de {FENETRE_CIBLE:g}° ; "
              f"borné à {oeil * 100:.1f} cm, elle fait {a.donnees['fenetreUtile']:.1f}°")


# --- réticules ---------------------------------------------------------------------------------------
#
# Primitives, en milliradians, u vers la droite du tireur et v vers le haut ; le dernier élément "noir"
# fait d'un trait un trait gravé (les lunettes : la pointe est éclairée, le reste est noir) :
#   ("disque", u, v, r) ; ("arc", u, v, r, épaisseur, a0, a1) en degrés, 0 à droite, 90 en haut ;
#   ("trait", u0, v0, u1, v1, épaisseur) ; ("poly", ((u, v), ...)) plein ;
#   ("chevron", demi-envergure, chute, épaisseur) : la pointe au point visé ;
#   ("capsule", demi-longueur, demi-hauteur, épaisseur) : un stade, en trait.

def _polaire(r, a):
    return r * math.cos(math.radians(a)), r * math.sin(math.radians(a))


def _rayons(r0, r1, angles, ep, *noir):
    return [("trait", *_polaire(r0, a), *_polaire(r1, a), ep, *noir) for a in angles]


MOTIFS = {
    # points rouges
    "PR-A": [("disque", 0, 0, 4.0)] + [("arc", 0, 0, 46.0, 2.8, a - 28, a + 28) for a in (45, 135, 225, 315)],
    "PR-B": [("arc", 0, 0, 40.0, 2.6, 0, 360), ("disque", 0, 0, 3.2)] + _rayons(40.0, 27.0, (30, 150, 270), 3.0),
    "PR-C": [("disque", 0, 0, 2.6)] + _rayons(11.0, 22.0, (0, 90, 180, 270), 2.0),
    # réflex
    "RX-A": [("poly", ((0.0, 0.0), (-7.2, -11.7), (7.2, -11.7)))] + [("trait", 21 * s, 0, 39 * s, 0, 2.0) for s in (1, -1)],
    "RX-B": [("chevron", 15.4, 12.3, 3.0)] + [("trait", s * u0, 0, s * u1, 0, 3.8) for s in (1, -1)
                                             for u0, u1 in ((24, 45), (53, 74))],
    "RX-C": [("disque", 0, 0, 3.0)] + [p for s in (1, -1) for p in (("trait", 34 * s, 20, 34 * s, -20, 2.4),
                                                                     ("trait", 34 * s, 20, 25 * s, 20, 2.4),
                                                                     ("trait", 34 * s, -20, 25 * s, -20, 2.4))],
    "RX-D": [("arc", 0, 0, 44.0, 2.6, 0, 360), ("disque", 0, 0, 3.2), ("trait", 0, -44, 0, -56, 2.6)],
    # holographiques
    "HO-A": [("capsule", 52.0, 30.0, 2.4), ("disque", 0, 0, 3.0)] + [p for s in (1, -1) for p in (
        ("trait", 62 * s, 12, 62 * s, -12, 2.4), ("trait", 69 * s, 7, 69 * s, -7, 2.4))],
    # un bouton marche renversé : le cercle ouvert en bas, sa barre qui s'arrête avant le point
    "HO-B": [("arc", 0, 0, 46.0, 3.0, -60, 240), ("trait", 0, -54, 0, -16, 3.0), ("disque", 0, 0, 3.2)],
    "HO-C": [("disque", 0, 0, 3.0)] + [p for (cu, cv), d1, d2 in (((0, 44), (-1, -1), (1, -1)), ((44, 0), (-1, 1), (-1, -1)),
                                                                   ((0, -44), (1, 1), (-1, 1)), ((-44, 0), (1, -1), (1, 1)))
                                       for p in (("trait", cu, cv, cu + 10 * d1[0], cv + 10 * d1[1], 2.6),
                                                 ("trait", cu, cv, cu + 10 * d2[0], cv + 10 * d2[1], 2.6))],
    "HO-D": [("arc", 0, 0, 52.0, 1.6, 0, 360), ("disque", 0, 0, 2.6)] + _rayons(30.0, 12.0, (90, 210, 330), 3.0),
    # lunettes (milliradians de la scène)
    "L2-A": [("disque", 0, 0, 2.2)] + _rayons(6.0, 58.0, (0, 90, 180, 270), 0.95, "noir"),
    "L25-A": [("chevron", 10.2, 8.2, 2.0), ("trait", 0, -11.2, 0, -65, 1.0, "noir")]
             + [("trait", -w, v, w, v, 1.0, "noir") for v, w in ((-23, 9.2), (-36, 7.3), (-48.5, 5.6), (-61, 3.7))],
    "L25-B": [("poly", ((0.0, 0.0), (-3.7, -11.6), (3.7, -11.6))), ("trait", 0, -11.6, 0, -65, 2.4, "noir")]
             + [("trait", 12.8 * s, 0, 61 * s, 0, 1.0, "noir") for s in (1, -1)]
             + [("trait", u * s, 4.1, u * s, -4.1, 1.0, "noir") for s in (1, -1) for u in (33, 54)],
    "L3-A": [("disque", 0, 0, 1.0), ("arc", 0, 0, 16.0, 0.75, 0, 360)] + _rayons(4.5, 34.0, (0, 90, 180, 270), 0.9, "noir"),
}


def couleur_motif(code):
    return "ReticuleVert" if code.startswith("RX") else "ReticuleRouge"


def dessiner_motif(pc, prims, centre, k, mat, mat_noir="Interieur", normale=VERS_TIREUR, droite=DROITE, haut=HAUT):
    """Un réticule à plat, face au tireur : centre, k mètres par milliradian."""

    def pt(u, v):
        return centre + droite * (u * k) + haut * (v * k)

    def arc(u, v, r, ep, a0, a1, m):
        n = max(4, int(abs(a1 - a0) / 7.5))
        for i in range(n):
            t0, t1 = (math.radians(a0 + (a1 - a0) * j / n) for j in (i, i + 1))
            q = [pt(u + math.cos(t) * rr, v + math.sin(t) * rr) for t in (t0, t1) for rr in (r - ep / 2, r + ep / 2)]
            kit.quad(pc, [q[0], q[2], q[3], q[1]], m, normale=normale)

    for prim in prims:
        noir = prim[-1] == "noir"
        q = prim[:-1] if noir else prim
        m = mat_noir if noir else mat
        genre = q[0]
        if genre == "disque":
            kit.disque(pc, pt(q[1], q[2]), normale, q[3] * k, 14, mat=m)
        elif genre == "arc":
            arc(*q[1:], m)
        elif genre == "trait":
            acc.trait(pc, pt(q[1], q[2]), pt(q[3], q[4]), q[5] * k, m, normale=normale)
        elif genre == "poly":
            kit.quad(pc, [pt(u, v) for u, v in q[1]], m, normale=normale)
        elif genre == "chevron":
            acc.chevron(pc, pt, q[1], q[2], q[3], m)
        elif genre == "capsule":
            dl, dh, ep = q[1:]
            cx = dl - dh
            arc(cx, 0, dh, ep, -90, 90, m)
            arc(-cx, 0, dh, ep, 90, 270, m)
            for v in (dh, -dh):
                acc.trait(pc, pt(-cx, v), pt(cx, v), ep * k, m, normale=normale)


def reticule_controle(a, av, h):
    """Le réticule contre la vitre, pour les rendus de contrôle (l'export ne le prend pas), à la taille où
    l'œil le voit en visée : il se confond avec celui que le jeu dessine à l'infini."""
    distance = av - a.donnees["avMire"] + a.donnees["distanceOeil"]
    mat = couleur_motif(a.code)
    pc = a.piece("Reticule", mat)
    dessiner_motif(pc, MOTIFS[a.code], P(av, h) + VERS_TIREUR * 0.0004, distance / 1000.0, mat)
    ob = a.objet(pc, angle=None)
    a.controle.append(ob)
    a.objets.remove(ob)
    return ob


# --- l'image du réticule, pour le jeu --------------------------------------------------------------------
#
# La distance signée au réticule (mrad, négative dedans) autour du point visé, en deux canaux : rouge, les
# traits qui luisent ; vert, les traits gravés (noirs) des lunettes. Codée sur 0,5 ± 0,5 pour ±PLAGE_RETICULE.
# Le shader DuelArena/ViseurReticule la lit à l'infini et en tire un bord net à toute taille (une lunette
# la grossit), sans rien savoir des formes : un nouveau réticule ne demande qu'un MOTIFS.

TAILLE_RETICULE = 512    # pixels de côté
PLAGE_RETICULE = 6.0     # mrad codés de part et d'autre du bord

# Les réflex qui tiennent sur une glissière de pistolet (Unity : SightFamilies.Reflex). Tenu à bout de bras, le
# pistolet balance dans la vue et sa petite fenêtre coupe tour à tour les traits des côtés : ils semblaient
# bouger quand on se déplace de côté (l'utilisateur, 2026-10-29). Sur un pistolet, le jeu ne montre que ce qui
# touche le point visé (le triangle, le chevron, le point) : une seconde image, `<racine>_Reticule_Centre.png`.
PETITS_REFLEX = ("RX-A", "RX-B", "RX-C")
NOMS_CENTRES = {"RX-A": "triangle", "RX-B": "chevron", "RX-C": "point"}   # leur réticule sur un pistolet

# Le réticule en quelques mots, sous l'image du viseur à l'armurerie.
NOMS_RETICULES = {
    "PR-A": "point et quatre arcs", "PR-B": "anneau à trois crans et point", "PR-C": "point et quatre traits",
    "RX-A": "triangle et traits de niveau", "RX-B": "chevron et tirets", "RX-C": "point entre deux crochets",
    "RX-D": "cercle, point et trait", "HO-A": "stade, point et crochets", "HO-B": "bouton marche et point",
    "HO-C": "coins de losange et point", "HO-D": "trident, cercle et point", "L2-A": "croix fine et point",
    "L25-A": "chevron et poteau gradué", "L25-B": "pointe et branches graduées", "L3-A": "croix, cercle et point",
}


def _etendue_motif(prims):
    """La demi-largeur (mrad) du carré qui contient le motif, autour du point visé."""
    e = 0.0
    for prim in prims:
        q = prim[:-1] if prim[-1] == "noir" else prim
        if q[0] == "disque":
            e = max(e, math.hypot(q[1], q[2]) + q[3])
        elif q[0] == "arc":
            e = max(e, math.hypot(q[1], q[2]) + q[3] + q[4] / 2)
        elif q[0] == "trait":
            e = max(e, math.hypot(q[1], q[2]) + q[5] / 2, math.hypot(q[3], q[4]) + q[5] / 2)
        elif q[0] == "poly":
            e = max(e, max(math.hypot(u, v) for u, v in q[1]))
        elif q[0] == "chevron":
            e = max(e, math.hypot(q[1], q[2]) + q[3])
        elif q[0] == "capsule":
            e = max(e, q[1] + q[3] / 2)
    return e


def _distance_segment(x, y, x0, y0, x1, y1):
    import numpy as np
    dx, dy = x1 - x0, y1 - y0
    t = np.clip(((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(x - x0 - t * dx, y - y0 - t * dy)


def _sdf_trait(x, y, u0, v0, u1, v1, demi):
    """Un trait à bouts carrés (acc.trait), de demi-épaisseur demi."""
    import numpy as np
    longueur = math.hypot(u1 - u0, v1 - v0)
    ax, ay = ((u1 - u0) / longueur, (v1 - v0) / longueur) if longueur > 1e-9 else (1.0, 0.0)
    px, py = x - (u0 + u1) / 2, y - (v0 + v1) / 2
    qx = np.abs(px * ax + py * ay) - longueur / 2
    qy = np.abs(py * ax - px * ay) - demi
    return np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0)) + np.minimum(np.maximum(qx, qy), 0.0)


def _sdf_arc(x, y, u, v, r, ep, a0, a1):
    """Un secteur d'anneau de a0 à a1 (degrés, sens trigonométrique), aux bouts coupés selon le rayon,
    comme dessiner_motif le pose."""
    import numpy as np
    demi = ep / 2
    px, py = x - u, y - v
    rho = np.hypot(px, py)
    anneau = np.abs(rho - r) - demi
    ouverture = a1 - a0
    if ouverture >= 360.0 - 1e-6:
        return anneau
    theta = (np.degrees(np.arctan2(py, px)) - a0) % 360.0
    vers_bout = np.radians(np.minimum(theta, ouverture - theta)) * rho

    def bout(a):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        return _distance_segment(px, py, (r - demi) * c, (r - demi) * s, (r + demi) * c, (r + demi) * s)

    return np.where(theta <= ouverture, np.maximum(anneau, -vers_bout), np.minimum(bout(a0), bout(a1)))


def _sdf_poly(x, y, points):
    """Un polygone convexe plein."""
    import numpy as np
    pts = [tuple(p) for p in points]
    if sum(p[0] * q[1] - q[0] * p[1] for p, q in zip(pts, pts[1:] + pts[:1])) < 0:
        pts.reverse()
    d = np.full(x.shape, -1e3)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
        n = math.hypot(x1 - x0, y1 - y0)
        d = np.maximum(d, ((x - x0) * (y1 - y0) - (y - y0) * (x1 - x0)) / n)
    return d


def _sdf_chevron(x, y, demi, chute, ep):
    """Le chevron d'acc.chevron : sa pointe au point visé, la face extérieure de ses branches par la pointe."""
    import numpy as np
    qx = np.abs(x)
    longueur = math.hypot(demi, chute)
    s = (qx * chute + y * demi) / longueur
    return np.maximum(np.maximum(s, -ep - s), (qx * demi - y * chute) / longueur - longueur)


def sdf_motif(prims, x, y, graves):
    """La distance signée (mrad) au motif : à ses traits gravés (graves), ou à ceux qui luisent."""
    import numpy as np
    d = np.full(x.shape, 1e3)
    for prim in prims:
        if (prim[-1] == "noir") != graves:
            continue
        q = prim[:-1] if graves else prim
        if q[0] == "disque":
            e = np.hypot(x - q[1], y - q[2]) - q[3]
        elif q[0] == "arc":
            e = _sdf_arc(x, y, *q[1:])
        elif q[0] == "trait":
            e = _sdf_trait(x, y, q[1], q[2], q[3], q[4], q[5] / 2)
        elif q[0] == "poly":
            e = _sdf_poly(x, y, q[1])
        elif q[0] == "chevron":
            e = _sdf_chevron(x, y, q[1], q[2], q[3])
        elif q[0] == "capsule":
            dl, dh, ep = q[1:]
            e = np.abs(_distance_segment(x, y, -(dl - dh), 0.0, dl - dh, 0.0) - dh) - ep / 2
        else:
            raise ValueError(f"primitive de réticule inconnue : {q[0]}")
        d = np.minimum(d, e)
    return d


def centre_motif(prims):
    """Les primitives du motif qui touchent le point visé (à 0,5 mrad près) : le triangle ou le chevron qui
    vise par sa pointe, le point. Les traits et les crochets des côtés tombent."""
    import numpy as np
    zero = np.zeros(1)
    return [p for p in prims if sdf_motif([p], zero, zero, p[-1] == "noir")[0] <= 0.5]


def _image_reticule(prims, nom, dossier):
    """Écrit l'image des distances au motif `prims` (`nom`, dans `dossier`) ; rend son étendue (la
    demi-largeur de l'image, en mrad)."""
    import numpy as np
    etendue = math.ceil((_etendue_motif(prims) + PLAGE_RETICULE + 2.0) / 5.0) * 5.0
    centres = (np.arange(TAILLE_RETICULE) + 0.5) / TAILLE_RETICULE * 2.0 * etendue - etendue
    x, y = np.meshgrid(centres, centres)       # la ligne 0 en bas, comme les pixels de Blender et d'Unity
    pixels = np.ones((TAILLE_RETICULE, TAILLE_RETICULE, 4), dtype=np.float32)
    for canal, graves in ((0, False), (1, True)):
        pixels[..., canal] = 0.5 + np.clip(sdf_motif(prims, x, y, graves) / (2.0 * PLAGE_RETICULE), -0.5, 0.5)
    pixels[..., 2] = 0.5
    image = bpy.data.images.new(nom, TAILLE_RETICULE, TAILLE_RETICULE, alpha=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = os.path.join(os.path.abspath(dossier), nom)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return etendue


def ecrire_reticule(a, dossier):
    """L'image du réticule du viseur (<racine>_Reticule.png) et ce que la fiche en dit : son étendue et la
    plage des distances ; pour un petit réflex, aussi celle de son centre seul (PETITS_REFLEX)."""
    prims = MOTIFS[a.code]
    nom = f"{a.prefixe}_Reticule.png"
    a.donnees.update(reticuleTexture=nom, etendueReticule=_image_reticule(prims, nom, dossier),
                     plageReticule=PLAGE_RETICULE)
    if a.code in PETITS_REFLEX:
        centre = centre_motif(prims)
        assert 0 < len(centre) < len(prims), f"{a.code} : son réticule n'a pas de centre à montrer seul"
        nom = f"{a.prefixe}_Reticule_Centre.png"
        a.donnees.update(reticuleCentreTexture=nom, etendueReticuleCentre=_image_reticule(centre, nom, dossier),
                         descriptionReticuleCentre=NOMS_CENTRES[a.code])


# --- points rouges -------------------------------------------------------------------------------------

def pr_a(a):
    """OTAN : un tube court à huit pans sur une réhausse inclinée, une visière de céramique qui dépasse
    devant, le boîtier d'électronique à droite et trois diodes à gauche ; un liseré rouge cerne la lentille."""
    H0 = 0.0360
    # la lentille à 3 cm du dos du tube, l'avant plus ouvert qu'elle : à la fenêtre utile commune
    # (FENETRE_CIBLE), l'œil reste à 7,6 cm et le tube ne cache pas plus du centre de l'écran qu'un réflex ;
    # plus loin derrière la lentille, il fallait approcher l'œil, et le tube cachait la moitié du centre
    pc = a.piece("Tube", "Peinture")
    f = tube8(pc, [(-0.012, 0.0170), (-0.007, 0.0180), (0.022, 0.0180), (0.027, 0.0196), (0.040, 0.0196)], H0,
              [0.0148, 0.0152, 0.0152, 0.0180, 0.0180])
    kit.chanfreiner(pc, 0.0007, f, angle_min=40)
    haut_tube, xg = H0 + apotheme8(0.0180), apotheme8(0.0180)
    f = kit.boite(pc, -0.006, 0.018, haut_tube - 0.0004, haut_tube + 0.0016, -0.0062, 0.0062, "Ceramique")
    kit.chanfreiner(pc, 0.0006, f)
    haut_capot = H0 + apotheme8(0.0196)
    f = kit.prisme(pc, [(0.020, haut_capot - 0.0004), (0.050, haut_capot - 0.0004), (0.050, haut_capot + 0.0010),
                        (0.030, haut_capot + 0.0046), (0.020, haut_capot + 0.0046)], -0.0078, 0.0078,
                   "Ceramique")                                                      # la visière, en coin
    kit.chanfreiner(pc, 0.0007, f)
    f = kit.boite(pc, -0.006, 0.014, H0 - 0.0060, H0 + 0.0060, xg - 0.0004, xg + 0.0014, "Ceramique")
    kit.chanfreiner(pc, 0.0006, f)
    diodes(pc, lambda i: [P(-0.003 + 0.0042 * i + du, H0 + dh, xg + 0.00145) for du, dh in
                          ((0, -0.0010), (0.0028, -0.0010), (0.0028, 0.0010), (0, 0.0010))], 3, GAUCHE)
    f = kit.boite(pc, -0.006, 0.016, H0 - 0.0085, H0 + 0.0085, -0.0300, -(xg - 0.0010))   # électronique
    kit.chanfreiner(pc, 0.0026, f)
    acc.molette(pc, P(0.005, H0, -0.0300), DROITE, 0.0062, 0.0040, dents=8)
    a.objet(pc)

    pc = a.piece("Rehausse", "Peinture")
    bas_tube = H0 - apotheme8(0.0180)
    f = kit.prisme(pc, [(-0.004, 0.0056), (0.024, 0.0056), (0.016, bas_tube + 0.0006), (-0.012, bas_tube + 0.0006)],
                   -0.0080, 0.0080)
    kit.chanfreiner(pc, 0.0012, f)
    for x, n in ((0.00805, GAUCHE), (-0.00805, DROITE)):                                  # l'évidement
        kit.quad(pc, [P(0.002, 0.0095, x), P(0.016, 0.0095, x), P(0.012, bas_tube - 0.0040, x),
                      P(-0.002, bas_tube - 0.0040, x)], "Interieur", normale=n)
    acc.embase(pc, -0.016, 0.030, demi=0.0130, h_haut=0.0060)
    acc.molette(pc, P(0.007, 0.0012, -0.0128), DROITE, 0.0085, 0.0055, dents=10)
    kit.cylindre(pc, P(0.007, 0.0012, 0.0128), P(0.007, 0.0012, 0.0142), 0.0034, 8, mat="Acier")
    a.objet(pc)

    pc = a.piece("Lisere", "Lueur")
    anneau_optique(pc, 0.0170, 0.0185, H0, 0.0153, 0.0148, 8, "Lueur")
    a.objet(pc, angle=None)
    vitre_points(a, octogone(0.0210, H0, 0.0152), "ambre")
    mire(a, -0.012, H0)
    reticule_controle(a, 0.0210, H0)


def pr_b(a):
    """Russe : une fenêtre hexagonale au bord éclairé, au bout d'un corps effilé au flanc de céramique ;
    l'émetteur se voit derrière, sa lentille rouge tournée vers la fenêtre."""
    H0 = 0.0300
    fen = [(0.0161, H0), (0.0092, H0 + 0.0115), (-0.0092, H0 + 0.0115), (-0.0161, H0), (-0.0092, H0 - 0.0115),
           (0.0092, H0 - 0.0115)]       # assez grande pour la fenêtre utile commune, l'œil à 5 cm
    pc = a.piece("Cadre", "Peinture")
    f = cadre_ferme(pc, 0.018, 0.026, decale(fen, 0.0030), fen)
    kit.chanfreiner(pc, 0.0008, f)
    f = kit.boite(pc, 0.017, 0.027, H0 + 0.0141, H0 + 0.0155, -0.0069, 0.0069, "Ceramique")      # le sourcil
    kit.chanfreiner(pc, 0.0005, f)
    a.objet(pc)
    pc = a.piece("Lisere", "Lueur")
    lisere(pc, 0.0250, 0.0260, fen)
    a.objet(pc, angle=None)

    pc = a.piece("Corps", "Peinture")
    s = ca.section8(0.0120, 0.0056, 0.0175, 0.0040, 0.0010)
    nez = ca.section8(0.0075, 0.0056, 0.0125, 0.0030, 0.0010)
    # le corps ne s'allonge pas plus de 4 cm derrière la fenêtre : en visée, l'œil s'approche de son dos, qui
    # remplissait le bas de la vue avant que la fenêtre paraisse assez grande
    f = kit.loft(pc, kit.sections_av([(-0.020, s), (0.028, s), (0.040, nez)]), "Peinture",
                 mats_cotes=mats_pans(flanc_gauche="Ceramique"))
    kit.chanfreiner(pc, 0.0010, f)
    f = kit.boite(pc, -0.014, -0.002, 0.0170, 0.0192, -0.0060, 0.0060)                         # l'émetteur
    kit.chanfreiner(pc, 0.0008, f)
    kit.disque(pc, P(-0.0019, 0.0181), AVANT, 0.0016, 10, mat="Laser")
    kit.quad(pc, [P(-0.012, 0.0105, 0.01206), P(0.016, 0.0105, 0.01206), P(0.016, 0.0117, 0.01206),
                  P(-0.012, 0.0117, 0.01206)], "Lueur", normale=GAUCHE)                         # la ligne d'état
    acc.embase(pc, -0.018, 0.032, demi=0.0130, h_haut=0.0060)
    acc.levier(pc, -0.014, 0.014, 0.0030, -0.0130, cote=-1)
    kit.cylindre(pc, P(0.000, 0.0030, 0.0128), P(0.000, 0.0030, 0.0142), 0.0032, 8, mat="Acier")
    a.objet(pc)

    vitre_contour(a, 0.0220, fen, "ambre")
    mire(a, -0.020, H0)
    reticule_controle(a, 0.0220, H0)


def pr_c(a):
    """M4S : un tube à huit pans, deux manchons de céramique, une tourelle fendue de rouge, une grosse
    molette à droite, sur un pont en A bas."""
    H0, R = 0.0320, 0.0166             # bas : perché à 46 mm, son pied de derrière montait sous le tube,
                                       # en visée, comme une colonne
    # la lentille, de 29 mm, à 3,2 cm du dos du tube, l'avant plus ouvert qu'elle : à la fenêtre utile
    # commune (FENETRE_CIBLE), l'œil reste à 7 cm et le tube ne cache pas plus du centre de l'écran qu'un
    # réflex ; long de 11 cm, il fallait coller l'œil au tube, qui cachait alors plus de la moitié du centre
    pc = a.piece("Tube", "Peinture")
    f = tube8(pc, [(-0.013, 0.0168), (-0.008, 0.0174), (-0.003, R), (0.021, R), (0.025, 0.0196), (0.040, 0.0196)], H0,
              [0.0146, 0.0150, 0.0146, 0.0146, 0.0180, 0.0180])
    kit.chanfreiner(pc, 0.0007, f, angle_min=40)
    for av0, av1, r in ((0.015, 0.020, R), (0.031, 0.037, 0.0196)):                          # manchons
        f = kit.anneau(pc, av0, av1, H0, r + 0.0009, r - 0.0004, 8, "Ceramique")
        kit.chanfreiner(pc, 0.0005, f, angle_min=40)
    haut = H0 + apotheme8(R)
    coque8(pc, -0.002, 0.010, 0.0100, haut - 0.0030, haut + 0.0068, 0.0030, 0.0005)            # tourelle
    kit.quad(pc, [P(0.000, haut + 0.00685, 0.0012), P(0.008, haut + 0.00685, 0.0012), P(0.008, haut + 0.00685, -0.0012),
                  P(0.000, haut + 0.00685, -0.0012)], "Lueur", normale=HAUT)
    capot = H0 + apotheme8(0.0196)
    kit.quad(pc, [P(0.0255, capot + 0.0001, 0.0014), P(0.0300, capot + 0.0001, 0.0014), P(0.0300, capot + 0.0001, -0.0014),
                  P(0.0255, capot + 0.0001, -0.0014)], "Lueur", normale=HAUT)
    a.objet(pc)

    pc = a.piece("Molette", "Polymere")
    xd = -apotheme8(R)
    acc.molette(pc, P(0.003, H0, xd - 0.0002), DROITE, 0.0110, 0.0055, dents=14)
    kit.disque(pc, P(0.003, H0, xd - 0.0058), DROITE, 0.0042, 12, mat="Lueur")
    a.objet(pc)

    pc = a.piece("Monture", "Peinture")
    bas = H0 - apotheme8(R)
    f = kit.boite(pc, -0.012, 0.020, bas - 0.0030, bas + 0.0004, -0.0085, 0.0085)
    kit.chanfreiner(pc, 0.0008, f)
    haut_pied = bas - 0.0026
    d = 0.30 * (haut_pied - 0.0056)                         # les deux pieds penchent l'un vers l'autre
    for profil in ([(-0.012 - d, 0.0056), (-0.002 - d, 0.0056), (-0.002, haut_pied), (-0.012, haut_pied)],
                   [(0.010 + d, 0.0056), (0.020 + d, 0.0056), (0.020, haut_pied), (0.010, haut_pied)]):
        f = kit.prisme(pc, profil, -0.0080, 0.0080)
        kit.chanfreiner(pc, 0.0010, f)
    acc.embase(pc, -0.018, 0.026, demi=0.0130, h_haut=0.0060)
    for av in (-0.011, 0.016):
        acc.vis_de_serrage(pc, av, 0.0130, h=0.0018)
    a.objet(pc)

    pc = a.piece("Lisere", "Lueur")
    anneau_optique(pc, 0.0175, 0.0190, H0, 0.0147, 0.0142, 8, "Lueur")
    a.objet(pc, angle=None)
    vitre_points(a, octogone(0.0200, H0, 0.0146), "ambre")
    mire(a, -0.013, H0)
    reticule_controle(a, 0.0200, H0)


# --- réflex --------------------------------------------------------------------------------------------

def rx_a(a):
    """OTAN : une fenêtre en écusson, presque sans cadre, sur une réhausse évidée ; l'émetteur rouge au
    dos."""
    H0 = 0.0285                        # sous le milieu de l'écusson, dont le haut se resserre
    pc = a.piece("Rehausse", "Peinture")
    acc.embase(pc, -0.024, 0.020, demi=0.0130, h_haut=0.0150)
    for x, n in ((0.01305, GAUCHE), (-0.01305, DROITE)):
        for av0, av1 in ((-0.018, -0.008), (-0.004, 0.012)):
            kit.quad(pc, [P(av0, 0.0050, x), P(av1, 0.0050, x), P(av1 - 0.002, 0.0118, x), P(av0 + 0.002, 0.0118, x)],
                     "Interieur", normale=n)
    acc.vis_de_serrage(pc, -0.006, 0.0130, h=0.0025)
    a.objet(pc)

    pc = a.piece("Corps", "Peinture")
    coque8(pc, -0.022, 0.018, 0.0124, 0.0146, 0.0205, 0.0024, 0.0008,
           mats=mats_pans(chanfrein_haut_gauche="Ceramique", chanfrein_haut_droit="Ceramique"))
    f = kit.boite(pc, -0.020, -0.010, 0.0200, 0.0212, -0.0040, 0.0040)                         # l'émetteur
    kit.chanfreiner(pc, 0.0005, f)
    kit.disque(pc, P(-0.00995, 0.0206), AVANT, 0.0008, 8, mat="Laser")
    fen = [(-0.0111, 0.0211), (0.0111, 0.0211), (0.0111, 0.0322), (0.0042, 0.0394), (-0.0042, 0.0394),
           (-0.0111, 0.0322)]           # un cadre peu profond, assez grand pour la fenêtre utile commune
    f = cadre_ferme(pc, 0.004, 0.010, decale(fen, 0.0022), fen)
    kit.chanfreiner(pc, 0.0007, f)
    for x, n in ((0.0124, GAUCHE), (-0.0124, DROITE)):
        bouton(pc, P(-0.010, 0.0178, x), AVANT, HAUT, n, 0.0030, 0.0014, dz=0.0007)
    a.objet(pc)

    vitre_contour(a, 0.0080, fen, "neutre")
    mire(a, -0.022, H0)
    reticule_controle(a, 0.0080, H0)


def rx_b(a):
    """Russe : une lentille ronde à douze pans tenue par un bras incliné sur le flanc gauche, sa bande
    de céramique et son trait rouge ; l'émetteur sur l'embase."""
    # une lentille de 28 mm : celle de 39 mm ne tombait à la fenêtre utile commune qu'avec l'œil à 13,6 cm
    R_INT, R_EXT = 0.0140, 0.0176
    H0 = 0.0170 + R_EXT                # l'anneau posé à 17 mm du rail
    x0, x1 = R_EXT - 0.0042, R_EXT + 0.0032                                        # le bras, sur le flanc gauche
    pc = a.piece("Embase", "Peinture")
    acc.embase(pc, -0.034, 0.034, demi=0.0135, h_haut=0.0080)
    for av in (-0.026, -0.013, 0.000, 0.013, 0.026):                                        # créneaux
        for x0, x1 in ((0.0135, 0.0146), (-0.0146, -0.0135)):
            f = kit.boite(pc, av - 0.0038, av + 0.0038, -0.0025, 0.0055, x0, x1)
            kit.chanfreiner(pc, 0.0004, f)
    acc.vis_de_serrage(pc, -0.004, 0.0146, h=0.0015)
    f = kit.boite(pc, -0.014, -0.002, 0.0078, 0.0112, -0.0050, 0.0050)                         # l'émetteur
    kit.chanfreiner(pc, 0.0008, f)
    kit.disque(pc, P(-0.0019, 0.0095), AVANT, 0.0012, 8, mat="Laser")
    a.objet(pc)

    pc = a.piece("Anneau", "Peinture")
    f = anneau_optique(pc, 0.014, 0.022, H0, R_EXT, R_INT, 12, "Peinture")
    kit.chanfreiner(pc, 0.0007, f, angle_min=40)
    f = kit.prisme(pc, [(0.008, 0.0075), (0.026, 0.0075), (0.025, H0 - 0.0100), (0.019, H0 + 0.0090),
                        (0.013, H0 + 0.0090), (0.012, H0 - 0.0100)], x0, x1)                    # le bras
    kit.chanfreiner(pc, 0.0010, f)
    f = kit.boite(pc, 0.008, 0.026, 0.0075, 0.0125, 0.0060, R_EXT - 0.0030)
    kit.chanfreiner(pc, 0.0008, f)
    f = kit.prisme(pc, [(0.011, 0.0140), (0.023, 0.0140), (0.022, H0 - 0.0100), (0.017, H0 + 0.0045),
                        (0.014, H0 + 0.0045), (0.013, H0 - 0.0100)], x1 - 0.0001, x1 + 0.0009, "Ceramique")
    kit.chanfreiner(pc, 0.0004, f)
    acc.trait(pc, P(0.0168, 0.0160, x1 + 0.00096), P(0.0158, H0 + 0.0030, x1 + 0.00096), 0.0012, "Lueur",
              normale=GAUCHE)
    a.objet(pc)

    vitre_points(a, kit.cercle(P(0.018, H0), AVANT, R_INT, 12, math.pi / 12), "neutre")
    mire(a, 0.014, H0)
    reticule_controle(a, 0.018, H0)


def rx_c(a):
    """MRS : une grande fenêtre au cadre très fin et au bord éclairé, posée sur un corps bas et mince."""
    H0 = 0.0272
    fen = kit.contour_rect(0.0300, 0.0200, chanfrein=0.0045, cy=H0)
    pc = a.piece("Embase", "Peinture")
    acc.embase(pc, -0.018, 0.014, demi=0.0130, h_haut=0.0060)
    acc.levier(pc, -0.014, 0.008, 0.0040, -0.0130, cote=-1)
    a.objet(pc)

    pc = a.piece("Corps", "Peinture")
    coque8(pc, -0.018, 0.012, 0.0150, 0.0058, 0.0160, 0.0026, 0.0010,
           mats=mats_pans(flanc_gauche="Ceramique", chanfrein_haut_gauche="Ceramique"))
    f = cadre_ferme(pc, 0.004, 0.010, decale(fen, 0.0018), fen)
    kit.chanfreiner(pc, 0.0005, f)
    for av in (-0.011, -0.004):
        bouton(pc, P(av, 0.0109, 0.0150), AVANT, HAUT, GAUCHE, 0.0024, 0.0016, dz=0.0007)
    a.objet(pc)
    pc = a.piece("Lisere", "Lueur")
    lisere(pc, 0.0094, 0.0100, fen, largeur=0.0005)
    a.objet(pc, angle=None)

    vitre_contour(a, 0.0070, fen, "neutre")
    mire(a, -0.020, H0)
    reticule_controle(a, 0.0070, H0)


def rx_d(a):
    """« Vintage » : un cadre de céramique anguleux, un biseau noir, un boîtier de pile sur le flanc
    gauche : le vieux collimateur refait au goût du programme d'essai."""
    H0 = 0.0328
    fen = kit.contour_rect(0.0300, 0.0240, chanfrein=0.0060, cy=H0)
    ext = decale(fen, 0.0040)
    pc = a.piece("Cadre", "Ceramique")
    f = cadre_ferme(pc, 0.000, 0.012, ext, fen, "Ceramique")
    kit.chanfreiner(pc, 0.0011, f)
    xg = max(x for x, _ in ext)
    kit.quad(pc, [P(0.002, H0 - 0.0045, xg + 0.0001), P(0.010, H0 - 0.0045, xg + 0.0001),
                  P(0.010, H0 + 0.0045, xg + 0.0001), P(0.002, H0 + 0.0045, xg + 0.0001)], "Primaire", normale=GAUCHE)
    a.objet(pc)
    pc = a.piece("Biseau", "Interieur")
    cadre_ferme(pc, 0.008, 0.012, decale(fen, 0.0002), decale(fen, -0.0012), "Interieur")
    a.objet(pc, angle=None)

    pc = a.piece("Corps", "Peinture")
    bas_cadre = H0 - 0.0120 - 0.0040
    coque8(pc, -0.008, 0.014, 0.0140, 0.0058, bas_cadre + 0.0012, 0.0026, 0.0010)
    f = kit.boite(pc, -0.008, 0.010, 0.0060, bas_cadre + 0.0026, 0.0140, 0.0200)               # la pile, à gauche
    kit.chanfreiner(pc, 0.0014, f)
    acc.molette(pc, P(0.001, 0.0128, 0.0200), GAUCHE, 0.0050, 0.0030, dents=10)
    acc.embase(pc, -0.012, 0.018, demi=0.0135, h_haut=0.0062)
    acc.vis_de_serrage(pc, 0.004, 0.0135, h=0.0018)
    a.objet(pc)

    vitre_contour(a, 0.0060, fen, "neutre")
    mire(a, 0.000, H0)
    reticule_controle(a, 0.0060, H0)


# --- holographiques --------------------------------------------------------------------------------------

def ho_a(a):
    """OTAN : une fenêtre en stade sous un capot épais, son bord éclairé de rouge, une bague de
    céramique ; au dos, deux boutons et un afficheur blanc froid."""
    H0 = 0.0440
    fen = contour_capsule(0.0225, 0.0165, cy=H0)
    pc = a.piece("Boitier", "Peinture")
    coque8(pc, -0.040, 0.026, 0.0215, 0.0055, H0 - 0.0168, 0.0040, 0.0012, mats=mats_pans(flanc_gauche="Ceramique"))
    f = cadre_ferme(pc, 0.004, 0.024, contour_capsule(0.0275, 0.0215, cy=H0), fen)
    kit.chanfreiner(pc, 0.0012, f)
    for x in (0.0110, -0.0110):
        bouton(pc, P(-0.0401, 0.0150, x), Vector((-1, 0, 0)), HAUT, VERS_TIREUR, 0.0030, 0.0026)
    acc.embase(pc, -0.036, 0.022, demi=0.0215, h_haut=0.0060)
    acc.levier(pc, -0.028, 0.014, 0.0040, -0.0215, cote=-1)
    a.objet(pc)
    pc = a.piece("Bague", "Ceramique")
    f = cadre_ferme(pc, 0.021, 0.027, contour_capsule(0.0285, 0.0225, cy=H0), contour_capsule(0.0235, 0.0175, cy=H0),
                    "Ceramique")
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc)
    pc = a.piece("Lisere", "Lueur")
    lisere(pc, 0.0230, 0.0240, fen)
    a.objet(pc, angle=None)
    pc = a.piece("Afficheur", "Holo")                                                 # la jauge de la pile
    diodes(pc, lambda i: [P(-0.0403, h, -0.0034 + 0.0024 * i + dx) for dx, h in
                          ((0.0, 0.0135), (0.0016, 0.0135), (0.0016, 0.0170), (0.0, 0.0170))], 3, VERS_TIREUR, "Holo")
    a.objet(pc, angle=None)

    vitre_contour(a, 0.0140, fen, "bleu")
    mire(a, -0.040, H0)
    reticule_controle(a, 0.0140, H0)


def ho_b(a):
    """Russe, en projection : pas de cadre, deux colonnes et une barre de lumière rouge sortent du
    boîtier ; un afficheur blanc froid sur le dessus."""
    H0 = 0.0410
    pc = a.piece("Boitier", "Peinture")
    coque8(pc, -0.050, 0.024, 0.0205, 0.0055, 0.0225, 0.0050, 0.0012, mats=mats_pans(flanc_gauche="Ceramique"))
    for x in (0.0190, -0.0190):                                                           # les projecteurs
        f = kit.boite(pc, 0.004, 0.016, 0.0215, 0.0250, x - 0.0030, x + 0.0030)
        kit.chanfreiner(pc, 0.0008, f)
        kit.disque(pc, P(0.010, 0.02505, x), HAUT, 0.0016, 8, mat="Laser")
    f = kit.prisme(pc, [(0.018, 0.0200), (0.028, 0.0200), (0.028, 0.0215), (0.020, 0.0232)], -0.0150, 0.0150,
                   "Ceramique")                                                           # la lèvre de devant
    kit.chanfreiner(pc, 0.0006, f)
    acc.embase(pc, -0.046, 0.020, demi=0.0205, h_haut=0.0060)
    acc.levier(pc, -0.036, 0.010, 0.0040, -0.0205, cote=-1)
    a.objet(pc)
    pc = a.piece("Projection", "HoloRouge")
    for x in (0.0190, -0.0190):
        kit.boite(pc, 0.0092, 0.0108, 0.0250, 0.0570, x - 0.0008, x + 0.0008, "HoloRouge")
    kit.boite(pc, 0.0092, 0.0108, 0.0570, 0.0584, -0.0198, 0.0198, "HoloRouge")
    a.objet(pc, angle=None)
    pc = a.piece("Afficheur", "Holo")                                                   # quatre segments
    diodes(pc, lambda i: [P(av, 0.02256, -0.0064 + 0.0034 * i + dx) for av, dx in
                          ((-0.030, 0.0), (-0.030, 0.0026), (-0.024, 0.0026), (-0.024, 0.0))], 4, HAUT, "Holo")
    a.objet(pc, angle=None)

    vitre_points(a, [P(0.0100, 0.0250, 0.0182), P(0.0100, 0.0250, -0.0182), P(0.0100, 0.0570, -0.0182),
                     P(0.0100, 0.0570, 0.0182)], "neutre")
    mire(a, -0.050, H0)
    reticule_controle(a, 0.0100, H0)


def ho_c(a):
    """Razor : une fenêtre en trapèze, inclinée vers le tireur, sur un boîtier à pans ; sa cellule
    rouge se voit devant."""
    H0, BAS = 0.0440, 0.0265
    pente = -math.tan(math.radians(9))
    fen = [(-0.0215, BAS), (0.0215, BAS), (0.0175, 0.0600), (0.0160, 0.0615), (-0.0160, 0.0615), (-0.0175, 0.0600)]
    pc = a.piece("Boitier", "Peinture")
    coque8(pc, -0.040, 0.040, 0.0255, 0.0055, 0.0258, 0.0060, 0.0015, quille=0.85,
           mats=mats_pans(chanfrein_haut_gauche="Ceramique", chanfrein_haut_droit="Ceramique"))
    f = cadre_ferme(pc, 0.022, 0.040, decale(fen, 0.0040), fen, pente=pente, h_pente=BAS)
    kit.chanfreiner(pc, 0.0012, f)
    for x in (0.0080, -0.0080):
        bouton(pc, P(-0.0402, 0.0135, x), Vector((-1, 0, 0)), HAUT, VERS_TIREUR, 0.0040, 0.0034)
    acc.embase(pc, -0.036, 0.036, demi=0.0230, h_haut=0.0060)
    acc.levier(pc, -0.028, 0.026, 0.0045, -0.0230, cote=-1)
    a.objet(pc)
    pc = a.piece("Cellule", "Peinture")
    f = kit.cylindre(pc, P(0.039, 0.0150), P(0.0445, 0.0150), 0.0065, 8, mat="Peinture")
    kit.chanfreiner(pc, 0.0006, f)
    kit.disque(pc, P(0.04455, 0.0150), AVANT, 0.0042, 8, mat="Laser")
    a.objet(pc)
    pc = a.piece("Lisere", "Lueur")
    lisere(pc, 0.0390, 0.0400, fen, pente=pente, h_pente=BAS)
    a.objet(pc, angle=None)

    av_vitre = 0.0310
    vitre_contour(a, av_vitre, fen, "bleu", pente=pente, h_pente=BAS)
    mire(a, -0.040, H0)
    reticule_controle(a, av_vitre + (H0 - BAS) * pente, H0)


def ho_d(a):
    """MH1 : un coin de céramique blanche, sa fenêtre penchée vers l'avant comme une proue, un afficheur
    blanc froid sur le chanfrein gauche."""
    H0 = 0.0400
    fen = kit.contour_rect(0.0360, 0.0280, chanfrein=0.0070, cy=H0)
    BAS = H0 - 0.0140
    pente = math.tan(math.radians(18))
    pc = a.piece("Boitier", "Ceramique")
    s = ca.section8(0.0200, 0.0055, 0.0235, 0.0070, 0.0012)
    nez = ca.section8(0.0200, 0.0055, 0.0150, 0.0050, 0.0012)
    f = kit.loft(pc, kit.sections_av([(-0.044, s), (0.008, s), (0.024, nez)]), "Ceramique",
                 mats_cotes=mats_pans(flanc_droit="Peinture", chanfrein_bas_droit="Peinture", dessous="Peinture",
                                      chanfrein_bas_gauche="Peinture"))
    kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc)
    pc = a.piece("Cadre", "Peinture")
    f = cadre_ferme(pc, -0.004, 0.010, decale(fen, 0.0032), fen, pente=pente, h_pente=BAS)
    kit.chanfreiner(pc, 0.0010, f)
    acc.embase(pc, -0.040, 0.020, demi=0.0200, h_haut=0.0060)
    acc.levier(pc, -0.032, 0.012, 0.0040, -0.0200, cote=-1)
    a.objet(pc)
    pc = a.piece("Afficheur", "Holo")
    n = Vector((0.0, 0.0, 0.0)) + (GAUCHE + HAUT).normalized() * 0.0002
    kit.quad(pc, [P(-0.032, 0.0183, 0.01825) + n, P(-0.012, 0.0183, 0.01825) + n, P(-0.012, 0.0218, 0.01475) + n,
                  P(-0.032, 0.0218, 0.01475) + n], "Holo", normale=(GAUCHE + HAUT).normalized())
    a.objet(pc, angle=None)

    av_vitre = 0.0030
    vitre_contour(a, av_vitre, fen, "bleu", pente=pente, h_pente=BAS)
    mire(a, -0.044, H0)
    reticule_controle(a, av_vitre + (H0 - BAS) * pente, H0)


# --- lunettes --------------------------------------------------------------------------------------------

def monture_deux_bagues(a, H0, bagues, r_ext, r_int, embase):
    pc = a.piece("Monture", "Peinture")
    for av in bagues:
        f = kit.anneau(pc, av - 0.0055, av + 0.0055, H0, r_ext, r_int, 8, "Peinture")
        kit.chanfreiner(pc, 0.0006, f, angle_min=40)
        f = kit.boite(pc, av - 0.0055, av + 0.0055, 0.0058, H0 - apotheme8(r_int) + 0.0004, -0.0080, 0.0080)
        kit.chanfreiner(pc, 0.0008, f)
        acc.vis_de_serrage(pc, av, 0.0130, h=0.0018)
    acc.embase(pc, embase[0], embase[1], demi=0.0130, h_haut=0.0060)
    a.objet(pc)


def l2_a(a):
    """2,0x : un tube fin à huit pans, un œilleton nu (son cercle rouge cernait toute la vue grossie,
    retiré le 2026-10-29 à la demande de l'utilisateur), une tourelle en haut à gauche (l'angle mort de
    la lunette 2,0x), deux manchons de céramique."""
    H0 = 0.0440
    a.donnees["grossissement"] = 2.0
    pc = a.piece("Oculaire", "Peinture")
    f = tube8(pc, [(-0.092, 0.0186), (-0.072, 0.0186), (-0.062, 0.0156)], H0, [0.0168, 0.0168, 0.0132])
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    a.oculaire.append(a.objet(pc))
    pc = a.piece("Tube", "Peinture")
    f = tube8(pc, [(-0.0625, 0.0152), (0.052, 0.0152), (0.064, 0.0194), (0.090, 0.0194)], H0,
              [0.0130, 0.0130, 0.0172, 0.0172])
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    for av0, av1 in ((-0.034, -0.012), (0.024, 0.044)):
        f = kit.anneau(pc, av0, av1, H0, 0.0160, 0.0148, 8, "Ceramique")
        kit.chanfreiner(pc, 0.0004, f, angle_min=40)
    ez = (HAUT + GAUCHE).normalized()
    ey = ez.cross(AVANT).normalized()
    centre = P(0.006, H0) + ez * (apotheme8(0.0152) + 0.0050)
    f = kit.boite_axes(pc, centre, AVANT, ey, ez, 0.0060, 0.0060, 0.0055, "Peinture")          # la tourelle
    kit.chanfreiner(pc, 0.0010, f)
    kit.disque(pc, centre + ez * 0.00556, ez, 0.0030, 8, mat="Lueur")
    kit.disque(pc, P(0.084, H0), AVANT, 0.0171, 8, mat="Lentille", rotation=math.pi / 8)
    a.objet(pc)
    monture_deux_bagues(a, H0, (-0.044, 0.030), 0.0178, 0.0151, (-0.054, 0.040))

    acc.vitre_plane(a, octogone(-0.0620, H0, 0.0132), "ambre")
    mire(a, -0.092, H0)


def l25_a(a):
    """2,5x OTAN : un corps de prisme à pans, un pare-soleil évasé, la fibre rouge sur le dessus."""
    H0 = 0.0420
    a.donnees["grossissement"] = 2.5
    pc = a.piece("Oculaire", "Peinture")
    f = tube8(pc, [(-0.062, 0.0168), (-0.036, 0.0168)], H0, [0.0146, 0.0140])
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    f = kit.anneau(pc, -0.064, -0.056, H0, 0.0176, 0.0150, 8, "Caoutchouc")              # la bonnette
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    a.oculaire.append(a.objet(pc))
    pc = a.piece("Corps", "Peinture")
    coque8(pc, -0.037, 0.030, 0.0165, H0 - 0.0180, H0 + 0.0168, 0.0060, 0.0040,
           mats=mats_pans(flanc_gauche="Ceramique", chanfrein_haut_gauche="Ceramique"))
    f = tube8(pc, [(0.028, 0.0186), (0.044, 0.0198), (0.062, 0.0210)], H0, [0.0166, 0.0178, 0.0192])   # pare-soleil
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    f = kit.boite(pc, -0.026, 0.022, H0 + 0.0166, H0 + 0.0200, -0.0045, 0.0045)                  # la fibre
    kit.chanfreiner(pc, 0.0008, f)
    kit.quad(pc, [P(-0.024, H0 + 0.02005, 0.0020), P(0.020, H0 + 0.02005, 0.0020), P(0.020, H0 + 0.02005, -0.0020),
                  P(-0.024, H0 + 0.02005, -0.0020)], "Lueur", normale=HAUT)
    kit.disque(pc, P(0.050, H0), AVANT, 0.0180, 8, mat="Lentille", rotation=math.pi / 8)
    a.objet(pc)
    pc = a.piece("Monture", "Peinture")
    f = kit.boite(pc, -0.026, 0.024, 0.0058, H0 - 0.0175, -0.0110, 0.0110)
    kit.chanfreiner(pc, 0.0012, f)
    acc.embase(pc, -0.030, 0.028, demi=0.0130, h_haut=0.0060)
    for av in (-0.016, 0.014):
        acc.molette(pc, P(av, 0.0040, 0.0130), GAUCHE, 0.0058, 0.0040, dents=10)
    a.objet(pc)

    acc.vitre_plane(a, octogone(-0.0360, H0, 0.0136), "ambre")
    mire(a, -0.064, H0)


def l25_b(a):
    """2,5x russe : un corps carré, un grand pare-soleil rectangulaire évasé, une flasque latérale à
    levier, un afficheur blanc froid sur le flanc gauche."""
    H0 = 0.0460
    a.donnees["grossissement"] = 2.5
    pc = a.piece("Oculaire", "Caoutchouc")
    f = tube8(pc, [(-0.070, 0.0172), (-0.060, 0.0166), (-0.044, 0.0156)], H0, [0.0150, 0.0146, 0.0136],
              mat="Caoutchouc")
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    a.oculaire.append(a.objet(pc))
    pc = a.piece("Corps", "Peinture")
    coque8(pc, -0.045, 0.022, 0.0175, H0 - 0.0200, H0 + 0.0175, 0.0040, 0.0030, mats=mats_pans(flanc_gauche="Ceramique"))
    int0 = kit.contour_rect(0.0300, 0.0260, chanfrein=0.0040, cy=H0)
    int1 = kit.contour_rect(0.0340, 0.0300, chanfrein=0.0045, cy=H0)
    f = cadre_ferme(pc, 0.020, 0.040, decale(int0, 0.0030), int0)
    kit.chanfreiner(pc, 0.0007, f)
    f = cadre_ferme(pc, 0.040, 0.060, decale(int0, 0.0030), int0, exterieur1=decale(int1, 0.0030), interieur1=int1)
    kit.chanfreiner(pc, 0.0007, f)
    kit.quad(pc, [P(0.045, h, x) for x, h in int0], "Lentille", normale=AVANT)
    a.objet(pc)
    pc = a.piece("Afficheur", "Holo")
    xg = 0.01755
    kit.quad(pc, [P(-0.036, H0 - 0.0070, xg), P(-0.008, H0 - 0.0070, xg), P(-0.008, H0 + 0.0070, xg),
                  P(-0.036, H0 + 0.0070, xg)], "Holo", normale=GAUCHE)
    a.texte(pc, "2,5×", 0.0060, P(-0.022, H0, xg + 0.0001), ca.VERS_CROSSE, HAUT, mat="Interieur")
    a.objet(pc, angle=None)
    pc = a.piece("Monture", "Peinture")
    f = kit.boite(pc, -0.032, 0.012, 0.0058, H0 - 0.0195, -0.0120, 0.0120)
    kit.chanfreiner(pc, 0.0010, f)
    f = kit.prisme(pc, [(-0.034, 0.0050), (0.014, 0.0050), (0.010, H0 - 0.0060), (-0.030, H0 - 0.0060)], 0.0120, 0.0200)
    kit.chanfreiner(pc, 0.0010, f)                                                       # la flasque, à gauche
    acc.levier(pc, -0.028, 0.006, 0.0140, 0.0200, cote=1, epaisseur=0.0026, hauteur=0.0075)
    acc.embase(pc, -0.034, 0.016, demi=0.0130, h_haut=0.0060)
    a.objet(pc)

    acc.vitre_plane(a, octogone(-0.0440, H0, 0.0136), "ambre")
    mire(a, -0.070, H0)


def l3_a(a):
    """3,0x : un long tube, une arête de céramique fendue de rouge sur le dessus, une tourelle de
    parallaxe à gauche, un liseré rouge à l'objectif, une molette d'acier près de l'œilleton (à la place
    de l'afficheur « 3× », retiré le 2026-10-29 à la demande de l'utilisateur)."""
    H0 = 0.0460
    a.donnees["grossissement"] = 3.0
    pc = a.piece("Oculaire", "Peinture")
    f = tube8(pc, [(-0.102, 0.0196), (-0.084, 0.0196), (-0.074, 0.0158)], H0, [0.0176, 0.0176, 0.0136])
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    acc.molette(pc, P(-0.091, H0 + 0.0010, apotheme8(0.0196) - 0.0010), GAUCHE, 0.0062, 0.0075, dents=12,
                mat="Acier")                                                     # la molette, sur la partie droite
    a.oculaire.append(a.objet(pc))

    pc = a.piece("Tube", "Peinture")
    f = tube8(pc, [(-0.0745, 0.0156), (0.048, 0.0156), (0.064, 0.0228), (0.098, 0.0228)], H0,
              [0.0134, 0.0134, 0.0206, 0.0206])
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    haut, xg = H0 + apotheme8(0.0156), apotheme8(0.0156)
    f = kit.prisme(pc, [(-0.060, haut - 0.0004), (0.042, haut - 0.0004), (0.032, haut + 0.0074), (-0.050, haut + 0.0074)],
                   -0.0042, 0.0042, "Ceramique")                                       # l'arête du dessus
    kit.chanfreiner(pc, 0.0008, f)
    kit.quad(pc, [P(-0.046, haut + 0.00745, 0.0012), P(0.028, haut + 0.00745, 0.0012), P(0.028, haut + 0.00745, -0.0012),
                  P(-0.046, haut + 0.00745, -0.0012)], "Lueur", normale=HAUT)
    f = kit.cylindre(pc, P(-0.004, H0, xg - 0.0010), P(-0.004, H0, xg + 0.0085), 0.0082, 8, mat="Peinture")
    kit.chanfreiner(pc, 0.0007, f, angle_min=40)                                         # parallaxe, à gauche
    kit.disque(pc, P(-0.004, H0, xg + 0.00855), GAUCHE, 0.0045, 8, mat="Lueur")
    kit.anneau(pc, 0.0975, 0.0985, H0, 0.0229, 0.0212, 8, "Lueur")
    kit.disque(pc, P(0.086, H0), AVANT, 0.0205, 8, mat="Lentille", rotation=math.pi / 8)
    a.objet(pc)
    monture_deux_bagues(a, H0, (-0.050, 0.020), 0.0181, 0.0155, (-0.060, 0.030))

    acc.vitre_plane(a, octogone(-0.0740, H0, 0.0136), "ambre")
    mire(a, -0.102, H0)


VISEURS = [
    ("PR-A", "Point rouge A", pr_a),
    ("PR-B", "Point rouge B", pr_b),
    ("PR-C", "Point rouge C", pr_c),
    ("RX-A", "Réflex A", rx_a),
    ("RX-B", "Réflex B", rx_b),
    ("RX-C", "Réflex C", rx_c),
    ("RX-D", "Réflex D", rx_d),
    ("HO-A", "Holographique A", ho_a),
    ("HO-B", "Holographique B", ho_b),
    ("HO-C", "Holographique C", ho_c),
    ("HO-D", "Holographique D", ho_d),
    ("L2-A", "Lunette 2,0x", l2_a),
    ("L25-A", "Lunette 2,5x A", l25_a),
    ("L25-B", "Lunette 2,5x B", l25_b),
    ("L3-A", "Lunette 3,0x", l3_a),
]
LIGNES_PLANCHE = [["PR-A", "PR-B", "PR-C"], ["RX-A", "RX-B", "RX-C", "RX-D"], ["HO-A", "HO-B", "HO-C", "HO-D"],
                  ["L2-A", "L25-A", "L25-B", "L3-A"]]

# Pour la page de revue : la famille, le viseur de Rainbow Six qui sert de référence, l'idée, le réticule.
FICHES = {
    "PR-A": ("Points rouges", "Point rouge OTAN (A)", pr_a, "Un point, cerné de quatre arcs."),
    "PR-B": ("Points rouges", "Point rouge russe (B)", pr_b, "Un anneau à trois crans et un point."),
    "PR-C": ("Points rouges", "Point rouge M4S (C)", pr_c, "Un petit point et quatre traits."),
    "RX-A": ("Réflex", "Réflex OTAN (A)", rx_a, "Le triangle des réflex et deux traits de niveau."),
    "RX-B": ("Réflex", "Réflex russe (B)", rx_b, "La flèche et ses tirets, coupés en deux."),
    "RX-C": ("Réflex", "Réflex MRS (C)", rx_c, "Un point entre deux crochets."),
    "RX-D": ("Réflex", "Réflex « Vintage » (D)", rx_d, "Un cercle, un point, un trait dessous."),
    "HO-A": ("Holographiques", "Holographique OTAN (A)", ho_a, "Un stade, un point et des crochets de côté."),
    "HO-B": ("Holographiques", "Holographique russe (B)", ho_b,
             "Un bouton marche renversé : un cercle ouvert en bas, sa barre, et un point."),
    "HO-C": ("Holographiques", "Holographique Razor (C)", ho_c, "Les coins d'un losange et un point."),
    "HO-D": ("Holographiques", "Holographique MH1 (D)", ho_d, "Un trident, un cercle fin et un point."),
    "L2-A": ("Lunettes", "Lunette 2,0x", l2_a, "Une croix fine et un point rouge."),
    "L25-A": ("Lunettes", "Lunette 2,5x OTAN", l25_a, "Le chevron rouge et ses graduations."),
    "L25-B": ("Lunettes", "Lunette 2,5x russe", l25_b, "Une pointe rouge sur un poteau, deux branches graduées."),
    "L3-A": ("Lunettes", "Lunette 3,0x", l3_a, "Une croix fine, un cercle et un point rouge."),
}


# --- rendus ----------------------------------------------------------------------------------------------

def rendre_jpeg(scene, cam, chemin, largeur, hauteur, echantillons=48):
    scene.camera = cam
    r = scene.render
    r.resolution_x, r.resolution_y, r.resolution_percentage = largeur, hauteur, 100
    r.filepath = chemin
    r.image_settings.file_format = "JPEG"
    r.image_settings.color_mode = "RGB"
    r.image_settings.quality = 88
    scene.eevee.taa_render_samples = echantillons
    bpy.ops.render.render(write_still=True, scene=scene.name)


def camera_oeil(nom, col, oeil, champ_vertical):
    """La caméra de l'œil en visée : le champ vertical des bras (60°), divisé par le grossissement."""
    cam = kit.camera(nom, col, oeil, oeil + Vector((0.0, -1.0, 0.0)), 50)
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(champ_vertical)
    cam.data.clip_start = 0.005
    cam.data.clip_end = 80
    return cam


def planche(ateliers, dossier):
    """Les viseurs ensemble, une famille par ligne : de dos (comme les icônes de Rainbow Six) et de trois
    quarts."""
    par_code = {a.code: a for a in ateliers}
    scene = ca.preparer_scene("Planche — viseurs")
    col = kit.collection("Viseurs", scene.collection)
    etiquettes = Piece("Planche_Etiquettes", "Marquage")
    police = bpy.data.fonts.load(ca.POLICE, check_existing=True)
    pas_x, pas_h = 0.120, 0.120
    for i, ligne in enumerate(LIGNES_PLANCHE):
        for j, code in enumerate(ligne):
            if code not in par_code:
                continue
            a = par_code[code]
            position = Vector((-(j - 1.5) * pas_x, 0.0, -i * pas_h))
            acc.monter(a, scene, position, col=col)
            kit.texte(etiquettes, a.titre, police, 0.0058, position + Vector((0, 0.05, -0.016)), Vector((-1, 0, 0)),
                      Vector((0, 0, 1)), "Marquage", scene=scene)
    kit.vers_objet(etiquettes, col, ateliers[0].M)
    scene.view_layers[0].update()
    studio = kit.collection(f"Studio — {scene.name}", scene.collection)
    centre = Vector((0.0, 0.0, -1.5 * pas_h + 0.03))
    kit.lumiere("Lumiere_Cle", studio, centre + Vector((0.5, 0.9, 0.9)), centre, 40, 1.0)
    kit.lumiere("Lumiere_Remplissage", studio, centre + Vector((-0.6, 0.9, 0.2)), centre, 16, 1.2)
    kit.lumiere("Lumiere_Contre", studio, centre + Vector((0.0, -0.8, 0.8)), centre, 32, 0.8)
    cam = kit.camera("Cam_Planche_Dos", studio, centre + Vector((0, 0.8, 0)), centre, ortho=0.56)
    rendre_jpeg(scene, cam, os.path.join(dossier, "planche_dos.jpg"), 1600, 1600, 64)
    cam = kit.camera("Cam_Planche_TroisQuarts", studio, centre + Vector((0.52, 0.80, 0.42)), centre, 40)
    rendre_jpeg(scene, cam, os.path.join(dossier, "planche_trois_quarts.jpg"), 1600, 1600, 64)


def planche_reticules(ateliers, dossier):
    """Les réticules à la même échelle, sur un fond sombre ; un cercle gris montre dans chacun la tête
    d'un robot à 10 m (24 cm). Ceux des lunettes sont en milliradians de la scène, comme ce cercle : la
    lunette grossit l'un et l'autre. Leurs traits gravés, noirs dans la lunette, y sont gris clair."""
    par_code = {a.code: a for a in ateliers}
    scene = ca.preparer_scene("Planche — réticules")
    col = kit.collection("Réticules", scene.collection)
    M = dict(ateliers[0].M)
    M["Fond"] = kit.materiau("DA_Planche_Fond", srgb(30, 32, 36), rug=0.9, salete=0.0, variation=0.0)
    M["Tete"] = kit.materiau("DA_Repere_Tete", srgb(92, 95, 100), rug=0.9, salete=0.0, variation=0.0)
    M["Grave"] = kit.materiau("DA_Trait_Grave", srgb(190, 192, 196), rug=0.9, salete=0.0, variation=0.0)
    # une lueur moins forte qu'en visée : à pleine force, le rouge et le vert pâlissent sous AgX
    M["ReticuleRouge"] = kit.materiau("DA_Réticule_Rouge_Planche", srgb(40, 6, 4), rug=0.5, salete=0.0, variation=0.0,
                                      emission=acc.ROUGE_RETICULE, force_emission=1.8)
    M["ReticuleVert"] = kit.materiau("DA_Réticule_Vert_Planche", srgb(6, 40, 10), rug=0.5, salete=0.0, variation=0.0,
                                     emission=acc.VERT_RETICULE, force_emission=1.4)
    police = bpy.data.fonts.load(ca.POLICE, check_existing=True)
    pas, k1 = 0.170, 0.00085
    fond = Piece("Planche_Fond", "Fond")
    kit.quad(fond, [Vector((2.2 * pas, -0.003, 0.5 * pas)), Vector((-2.2 * pas, -0.003, 0.5 * pas)),
                    Vector((-2.2 * pas, -0.003, -3.6 * pas)), Vector((2.2 * pas, -0.003, -3.6 * pas))], "Fond",
             normale=VERS_TIREUR)
    kit.vers_objet(fond, col, M)
    for i, ligne in enumerate(LIGNES_PLANCHE):
        for j, code in enumerate(ligne):
            if code not in par_code:
                continue
            a = par_code[code]
            centre = Vector((-(j - 1.5) * pas, 0.0, -i * pas))
            pc = Piece(f"Motif_{a.prefixe}", couleur_motif(code))
            dessiner_motif(pc, [("arc", 0, 0, 12.0, 0.6, 0, 360)], centre - VERS_TIREUR * 0.0005, k1, "Tete")
            dessiner_motif(pc, MOTIFS[code], centre, k1, couleur_motif(code), mat_noir="Grave")
            kit.texte(pc, f"{code} · {a.titre}", police, 0.0075, centre + Vector((0, 0.0005, -0.072)), Vector((-1, 0, 0)),
                      Vector((0, 0, 1)), "Marquage", scene=scene)
            kit.vers_objet(pc, col, M, angle_lisse=None)
    centre = Vector((0.0, 0.0, -1.5 * pas))
    studio = kit.collection(f"Studio — {scene.name}", scene.collection)
    kit.lumiere("Lumiere_Face", studio, centre + Vector((0.0, 1.0, 0.3)), centre, 60, 1.6)
    cam = kit.camera("Cam_Planche_Reticules", studio, centre + Vector((0, 0.9, 0)), centre, ortho=4.3 * pas)
    rendre_jpeg(scene, cam, os.path.join(dossier, "planche_reticules.jpg"), 1600, 1600, 32)


def stand_de_visee(scene, point):
    """Ce que l'œil voit au loin en visant : un sol, un mur, deux silhouettes de robot à 10 et 22 m, la
    tête de la première sur la ligne de visée."""
    col = kit.collection("Stand de visée", scene.collection)
    M = {"Sol": kit.materiau("DA_Stand_Sol", srgb(66, 68, 70), rug=0.85, salete=0.3),
         "Mur": kit.materiau("DA_Stand_Mur", srgb(104, 106, 108), rug=0.9, salete=0.4),
         "Cible": kit.materiau("DA_Stand_Cible", srgb(172, 170, 162), rug=0.7, salete=0.2)}
    axe = point.z + 0.04
    sol = axe - 1.55
    pc = Piece("Stand_Decor", "Sol")
    kit.quad(pc, [Vector((-60, 3, sol)), Vector((60, 3, sol)), Vector((60, -45, sol)), Vector((-60, -45, sol))], "Sol",
             normale=HAUT)
    kit.quad(pc, [Vector((-60, -45, sol)), Vector((60, -45, sol)), Vector((60, -45, sol + 40)), Vector((-60, -45, sol + 40))],
             "Mur", normale=VERS_TIREUR)
    for av, x in ((10.0, 0.0), (22.0, -1.3)):
        kit.boite(pc, av - 0.12, av + 0.12, axe - 0.80, axe - 0.15, x - 0.24, x + 0.24, "Cible")
        kit.boite(pc, av - 0.10, av + 0.10, axe - 0.12, axe + 0.12, x - 0.11, x + 0.11, "Cible")
        kit.boite(pc, av - 0.06, av + 0.06, sol, axe - 0.80, x - 0.05, x + 0.05, "Cible")
    kit.vers_objet(pc, col, M, angle_lisse=None)
    soleil = bpy.data.lights.new("Stand_Soleil", "SUN")
    soleil.energy = 2.5
    ob = bpy.data.objects.new("Stand_Soleil", soleil)
    col.objects.link(ob)
    ob.rotation_euler = (math.radians(55), 0.0, math.radians(160))
    return col


def vues_sur_arme(ateliers, script, dossier):
    """Chaque viseur monté sur une arme, à la place de ses organes de visée : de trois quarts, et depuis
    l'œil du joueur en visée (à la distance de l'œil du viseur, derrière sa mire). Une lunette se regarde par son seul
    œilleton, le champ divisé par son grossissement : comme si son optique remplissait l'œilleton."""
    at = acc.arme(script)["ATELIER"]
    scene = at.scene
    scene.view_layers[0].update()
    point = acc.point_de(at, "Viseur")
    masques = [o for o in (bpy.data.objects.get(at.nom(n)) for n in ("Hausse", "Guidon", "Guidon_Base", "Points_Hausse"))
               if o is not None]
    for o in masques:
        o.hide_render = True
    studio = kit.collection(f"Studio — viseurs sur {at.code}", scene.collection)
    stand_de_visee(scene, point)
    try:
        for a in ateliers:
            e = acc.monter(a, scene, point)
            cible = Vector((0.0, point.y, point.z + 0.02))
            cam = kit.camera(f"Cam_{a.prefixe}_SurArme", studio, cible + Vector((0.20, 0.26, 0.11)), cible, 45)
            rendre_jpeg(scene, cam, os.path.join(dossier, f"{a.prefixe}_sur_arme.jpg"), 1100, 790)
            oeil = point + P(a.donnees["avMire"], a.donnees["hauteurAxe"]) + Vector((0.0, a.donnees["distanceOeil"], 0.0))
            mag = a.donnees.get("grossissement", 1.0)
            caches, temporaires = [], []
            if mag > 1.0:
                for o in a.objets + a.vitres:
                    if o not in a.oculaire:
                        o.hide_render = True
                        caches.append(o)
                pc = Piece(f"{a.prefixe}_Reticule_Oeil", "ReticuleRouge")
                dessiner_motif(pc, MOTIFS[a.code], oeil + Vector((0.0, -0.5, 0.0)), 0.5 / 1000.0, "ReticuleRouge")
                temporaires.append(kit.vers_objet(pc, studio, a.M, angle_lisse=None))
            cam = camera_oeil(f"Cam_{a.prefixe}_Oeil", studio, oeil, 60.0 / mag)
            rendre_jpeg(scene, cam, os.path.join(dossier, f"{a.prefixe}_visee.jpg"), 1200, 800)
            for o in caches:
                o.hide_render = False
            for o in temporaires:
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.objects.remove(e, do_unlink=True)
    finally:
        for o in masques:
            o.hide_render = False


def mesurer_encombrement(ateliers, dossier, l=480, h=320):
    """Ce que chaque viseur sans grossissement cache de l'écran en visée, mesuré sur un rendu du viseur seul
    (sans l'arme, sa vitre et le réticule de contrôle cachés, fond transparent) : la part opaque du disque de
    40° autour du réticule, et de tout l'écran (champ vertical de 60°, format 3:2). En pourcents, par code."""
    import numpy as np
    parts = {}
    for a in ateliers:
        if "fenetreUtile" not in a.donnees:
            continue
        scene = a.scene
        scene.render.film_transparent = True
        scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = l, h, 100
        scene.eevee.taa_render_samples = 1
        caches = [o for o in a.vitres + a.controle if not o.hide_render]
        for o in caches:
            o.hide_render = True
        oeil = P(a.donnees["avMire"], a.donnees["hauteurAxe"]) + VERS_TIREUR * a.donnees["distanceOeil"]
        cam = camera_oeil(f"Cam_{a.prefixe}_Masque", a.col, oeil, 60.0)
        chemin = os.path.join(dossier, f"{a.prefixe}_masque.png")
        scene.camera = cam
        scene.render.filepath = chemin
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"
        bpy.ops.render.render(write_still=True, scene=scene.name)
        im = bpy.data.images.load(chemin)
        opaque = np.array(im.pixels[:], dtype=np.float32).reshape(h, l, 4)[..., 3] > 0.5
        bpy.data.images.remove(im)
        os.remove(chemin)
        bpy.data.objects.remove(cam, do_unlink=True)
        for o in caches:
            o.hide_render = False
        scene.render.film_transparent = False
        yy, xx = np.mgrid[0:h, 0:l]
        f = (h / 2) / math.tan(math.radians(30.0))                       # pixels par unité de tangente
        disque = np.hypot((xx + 0.5 - l / 2) / f, (yy + 0.5 - h / 2) / f) <= math.tan(math.radians(20.0))
        parts[a.code] = {"centre": round(100 * float(opaque[disque].mean()), 1), "ecran": round(100 * float(opaque.mean()), 1)}
        print(f"  {a.code:<6} cache {parts[a.code]['centre']:4.1f} % du centre de l'écran, {parts[a.code]['ecran']:4.1f} % de l'écran")
    return parts


def ecrire_manifeste(ateliers, dossier, sur_arme, encombrement=None):
    fiches = []
    for a in ateliers:
        famille, r6, construire, motif = FICHES[a.code]
        images = {v: f"{a.prefixe}_{v}.jpg" for v in VUES_STUDIO}
        if sur_arme:
            images.update(sur_arme=f"{a.prefixe}_sur_arme.jpg", visee=f"{a.prefixe}_visee.jpg")
        fiches.append({"code": a.code, "titre": a.titre, "famille": famille, "r6": r6,
                       "idee": " ".join(construire.__doc__.split()), "motif": motif,
                       "grossissement": a.donnees.get("grossissement", 1.0), "distanceOeil": a.donnees["distanceOeil"],
                       "fenetreUtile": a.donnees.get("fenetreUtile"), "cache": (encombrement or {}).get(a.code),
                       "triangles": sum(kit.triangles(o) for o in a.objets), "images": images})
    with open(os.path.join(dossier, "manifeste.json"), "w", encoding="utf-8") as f:
        json.dump({"viseurs": fiches, "planches": ["planche_dos.jpg", "planche_trois_quarts.jpg", "planche_reticules.jpg"]},
                  f, ensure_ascii=False, indent=1)


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    sans_rendus = argparse.Namespace(**{**vars(args), "rendus": None})

    def preparer(construire):
        def construire_complet(a):
            global _ouvertures
            completer_materiaux(a)
            a.oculaire = []
            _ouvertures = []
            try:
                construire(a)
            finally:
                _ouvertures = None
            # ce que la fiche dit du réticule (l'importeur d'Unity le lit), et son image, que
            # exporter_accessoires fait écrire dans le dossier de l'accessoire
            a.donnees["couleurReticule"] = "vert" if couleur_motif(a.code) == "ReticuleVert" else "rouge"
            a.donnees["descriptionReticule"] = NOMS_RETICULES[a.code]
            a.avant_fiche = lambda dossier: ecrire_reticule(a, dossier)
        return construire_complet

    ateliers = []
    for code, titre, construire in VISEURS:
        if seuls and code not in seuls:
            continue
        ateliers.append(acc.construire_accessoire(code, titre, "Viseur", f"Viseur — {titre}",
                                                  preparer(construire), sans_rendus))
    print(f"Fenêtre utile visée : {FENETRE_CIBLE:g}°")
    for a in ateliers:
        if "fenetreUtile" in a.donnees:
            print(f"  {a.code:<6} œil à {a.donnees['distanceOeil'] * 100:4.1f} cm, fenêtre utile {a.donnees['fenetreUtile']:4.1f}°")
    if args.rendus:
        os.makedirs(args.rendus, exist_ok=True)
        for a in ateliers:
            for nom in VUES_STUDIO:
                cam = a.cams[nom][0]
                rendre_jpeg(a.scene, cam, os.path.join(args.rendus, f"{a.prefixe}_{nom}.jpg"), 1100, 790)
        planche(ateliers, args.rendus)
        planche_reticules(ateliers, args.rendus)
        if args.sur_arme:
            vues_sur_arme(ateliers, args.sur_arme, args.rendus)
        print("Ce que chaque viseur cache en visée :")
        encombrement = mesurer_encombrement(ateliers, args.rendus)
        ecrire_manifeste(ateliers, args.rendus, bool(args.sur_arme), encombrement)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
