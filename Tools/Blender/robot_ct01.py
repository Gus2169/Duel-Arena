"""Le robot CT-01 de Duel Arena (Blender 5.2, low poly) : le robot du jeu (2026-10-26).

Inspiré de l'archétype de KAY/O (Valorant), à la demande de l'utilisateur, sans en reprendre le dessin
(le jeu sera vendu). La v2 (même jour, « plus menaçant », sur une image de référence de l'utilisateur :
un robot massif aux épaules hautes) : une silhouette en V, la poitrine qui s'évase vers des épaulières à
arête et des trapèzes hauts où la tête s'enfonce, une taille fine ; des plaques à pans et à arêtes vives,
blindées (gris acier) ou peintes de la couleur du joueur ; un casque étroit, au menton en avant et au front
fuyant, une visière sombre en pointe et deux fentes lumineuses pour les yeux ; un cœur hexagonal lumineux,
tramé de triangles, entre les pectoraux ; des genoux et des coudes en pointe, des bottes en coin. Les
lueurs (yeux, cœur, fentes des gantelets et des jambières) prennent la couleur du joueur. Et ce que la
bible impose aux combattants de Duel Arena (§ 5, § 11) : un prototype de soldat en évaluation, aux
marquages de crash-test (les mires noir et blanc des flancs du casque, du pectoral et des épaulières, son
numéro), usé et rafistolé (une soudure sur le pectoral droit, une plaque d'apprêt rivetée sur la cuisse
gauche). La v1 (plus ronde, un visage d'écran à deux yeux) est dans archives/robot_ct01_v1.py.

Pièces rigides sur le squelette du Y Bot (Mixamo) : chaque pièce suit un seul os, comme un robot ; des
tambours aux articulations cachent le jeu entre deux pièces. Le squelette, ses noms et sa pose de repos
(en T) sont ceux du Y Bot : Unity lui donne un avatar humanoïde et il joue toutes les animations du jeu.
Il tient dans la surface touchable du jeu (bible § 5 : même silhouette pour tous) : le script imprime, partie
par partie, ce qui en sort (verifier_surface_touchable), et ce qui dépasse la silhouette du Y Bot, os par os
(VOLUMES_Y_BOT : les épaulières, voulues plus larges, hors de la surface touchable comme les bras du Y Bot).

Les rendus de comparaison (bible § 10 et § 18, l'utilisateur tranche) :
- low poly propre : les matériaux usés du kit des armes (kit_lowpoly.materiau) ;
- cel-shading discret : des aplats en trois tons, un reflet franc, un contour ;
- les deux mêlés : l'usure du low poly sous l'éclairage en aplats, avec le contour.

  blender -b --factory-startup -P robot_ct01.py -- [--rendus dossier] [--vues a,b] [--fbx f.fbx]
                                                   [--sortie f.blend] [--couleur violet] [--variantes A,B,C,D]

Vues : comparaison, bustes, planche, couleurs (la palette), heros (le robot armé du FA-07, vu d'en bas), dos.
--variantes construit une variante du bas du dos par lettre (VARIANTES) et les rend côte à côte.

Repère : celui du Y Bot importé dans Blender (mètres, haut +Z, le robot regarde vers -Y, sa gauche en
+X). Les pièces sont construites dans ce repère, puis ramenées dans celui du squelette (l'objet Armature
du FBX de Mixamo, tourné de 90° et à l'échelle 0,01) : elles en sont les enfants, sans transformation,
comme les maillages du Y Bot, et l'export FBX vers Unity est celui de l'essai du Y Bot à facettes.
"""

import importlib
import math
import os
import random
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_robots as cr  # noqa: E402

importlib.reload(cr)
from commun_robots import *  # noqa: E402,F401,F403  le vocabulaire des robots : primitives, repères, matériaux

CODE = "CT-01"
PREFIXE = "CT01"
NUMERO = "07"

# Plus large que le Y Bot, exprès : les épaulières (2,2 cm de plus que son deltoïde). Elles restent hors de la
# surface touchable, comme les bras du Y Bot (le torse touchable fait 23 cm de rayon).
ECARTS_VOULUS = {"LeftArm": 0.022}


# Le cadre de l'image de la visière (Assets/_ProjectArt/Textures/EcranMort.png, faite par ecran_mort.py) : vue de
# face, 9 cm de large centrés sur le nez, de 1,600 m à 1,750 m de haut (toute la visière, les yeux au milieu).
LARGEUR_ECRAN, BAS_ECRAN, HAUTEUR_ECRAN = 0.090, 1.600, 0.150


# --- la tête ---------------------------------------------------------------------------------------

CASQUE = [  # (z, demi-largeur, devant, nuque) : le menton en avant, le front fuyant
    (1.572, 0.050, -0.090, 0.060), (1.600, 0.066, -0.102, 0.078), (1.640, 0.074, -0.105, 0.088),
    (1.700, 0.078, -0.098, 0.092), (1.750, 0.075, -0.084, 0.090), (1.782, 0.062, -0.062, 0.078),
    (1.797, 0.040, -0.034, 0.054)]


def section_casque(hw, f, b):
    """Neuf points (x, y) : une arête au milieu du visage, deux pans de visière, des joues, des flancs, une
    nuque large. Côtés (du point i au suivant) : 0 et 8 la visière, 1 et 7 les joues, 2 et 6 les flancs,
    3 à 5 la nuque."""
    return [(0.0, f), (0.55 * hw, f + 0.020), (hw, 0.30 * f), (hw, 0.55 * b), (0.62 * hw, b),
            (-0.62 * hw, b), (-hw, 0.55 * b), (-hw, 0.30 * f), (-0.55 * hw, f + 0.020)]


def tete(rb):
    o = rb.os("Head")
    # Le casque : étroit et haut, à pans, peint ; la visière sombre en pointe, les joues blindées.
    pc = rb.piece("Casque", o.nom, "Peinture")
    faces = loft_z(pc, CASQUE, "Peinture", section_casque)
    n = 9
    for bande in range(len(CASQUE) - 1):
        for cote in range(n):
            f = faces[bande * n + cote]
            if 1 <= bande <= 3 and cote in (0, 8):
                f.material_index = pc.mat("Ecran")
            elif (1 <= bande <= 3 and cote in (1, 7)) or (bande == 0 and cote in (8, 0, 1, 7)):
                f.material_index = pc.mat("Blindage")
    kit.chanfreiner(pc, 0.0025)
    casque = Surface(pc)

    # Les yeux : deux fentes lumineuses qui remontent vers les tempes, un regard dur
    pc = rb.piece("Yeux", o.nom, "Lueur")
    for s in (1, -1):
        face_avant(pc, casque, [(s * 0.011, 1.664), (s * 0.042, 1.679), (s * 0.042, 1.6865), (s * 0.011, 1.6715)],
                   0.0010, "Lueur", enfoncement=0.0004)

    # La crête : une lame sombre du front à la nuque
    pc = rb.piece("Crete", o.nom, "Blindage")
    chemin = [Vector((0, -0.090, 1.748)), Vector((0, -0.068, 1.779)), Vector((0, -0.034, 1.794)), Vector((0, 0.010, 1.795)),
              Vector((0, 0.046, 1.792)), Vector((0, 0.068, 1.776)), Vector((0, 0.082, 1.750))]
    kit.balayage(pc, kit.lisser_chemin(chemin, 3), [(-0.008, 0.004), (0.008, 0.004), (0.003, -0.005), (-0.003, -0.005)],
                 "Blindage", normale=X)

    # Les ailerons : deux plaques en biais sur l'arrière des flancs
    pc = rb.piece("Ailerons", o.nom, "Blindage")
    for s in (1, -1):
        plaque_sur(pc, casque, [(0.028, 1.672), (0.070, 1.682), (0.076, 1.732), (0.040, 1.728)], ORIGINE, Y, Z,
                   Vector((-s, 0, 0)), 0.008, "Blindage", enfoncement=0.003)
    kit.chanfreiner(pc, 0.0015)

    # Le couvre-nuque : une plaque blindée sur le bas de l'arrière du casque, fendue de trois évents
    pc = rb.piece("Couvre_Nuque", o.nom, "Blindage")
    plaque_sur(pc, casque, [(-0.046, 1.580), (0.046, 1.580), (0.052, 1.630), (0.0, 1.640), (-0.052, 1.630)], ORIGINE, X, Z,
               -Y, 0.008, "Blindage", enfoncement=0.003)
    kit.chanfreiner(pc, 0.0015)
    nuque = Surface(pc)
    for i in range(3):
        p, nrm = nuque.toucher(Vector((0.0, 0.35, 1.596 + i * 0.012)), -Y)
        x, y = axes_decalque(nrm)
        c = p + nrm * 0.0007
        kit.quad(pc, [c + x * (-0.028) + y * (-0.0022), c + x * 0.028 + y * (-0.0022), c + x * 0.028 + y * 0.0022,
                      c + x * (-0.028) + y * 0.0022], "Interieur", normale=nrm)

    # Les mires de crash-test sur les flancs, et le capteur de la tempe droite
    pc = rb.piece("Casque_Marquages", o.nom, "Marquage", genre="marquage")
    for s in (1, -1):
        p, nrm = casque.toucher(Vector((s * 0.35, 0.010, 1.706)), Vector((-s, 0, 0)))
        mire_crash(pc, p, nrm, 0.016)
    pc = rb.piece("Capteur", o.nom, "Chassis")
    axe = Vector((-1.0, -0.6, 0.0)).normalized()
    p0 = Vector((-0.066, -0.044, 1.655))
    kit.cylindre(pc, p0, p0 + axe * 0.014, 0.0075, 10, mat="Chassis")
    kit.disque(pc, p0 + axe * 0.0143, axe, 0.0052, 10, mat="Lentille")


def cou(rb):
    o = rb.os("Neck")
    pc = rb.piece("Cou", o.nom, "Chassis")
    kit.cylindre(pc, Vector((0, 0.034, 1.486)), Vector((0, 0.010, 1.592)), 0.034, 10, mat="Interieur")
    for z in (1.512, 1.537, 1.562):
        y = 0.034 - (z - 1.486) * 0.226
        faces = kit.cylindre(pc, Vector((0, y, z - 0.006)), Vector((0, y, z + 0.006)), 0.043, 12, mat="Metal")
        kit.chanfreiner(pc, 0.002, faces, 60)


# --- le buste ---------------------------------------------------------------------------------------

CUIRASSE = [  # (z, demi-largeur, devant, dos) : la poitrine s'évase vers les épaules, la taille est fine
    (1.300, 0.124, -0.114, 0.098), (1.345, 0.150, -0.136, 0.112), (1.400, 0.164, -0.146, 0.121),
    (1.455, 0.168, -0.136, 0.124), (1.500, 0.148, -0.108, 0.118), (1.530, 0.104, -0.070, 0.106)]


def section_cuirasse(hw, f, b):
    """Seize points (x, y) : devant, le sillon du cœur entre deux pans de poitrine ; des flancs ; derrière, le
    sillon de la colonne entre deux pans d'omoplates (le dos répond à la face)."""
    return [(0.08 * hw, f + 0.010), (0.30 * hw, f), (0.80 * hw, f + 0.010), (hw, 0.35 * f), (hw, 0.45 * b),
            (0.72 * hw, b - 0.004), (0.36 * hw, b + 0.010), (0.10 * hw, b + 0.002), (-0.10 * hw, b + 0.002),
            (-0.36 * hw, b + 0.010), (-0.72 * hw, b - 0.004), (-hw, 0.45 * b), (-hw, 0.35 * f),
            (-0.80 * hw, f + 0.010), (-0.30 * hw, f), (-0.08 * hw, f + 0.010)]


PECTORAL = [(0.046, 1.476), (0.140, 1.488), (0.152, 1.448), (0.142, 1.372), (0.098, 1.332), (0.050, 1.342),
            (0.044, 1.400)]


def buste(rb):
    o = rb.os("Spine2")
    pc = rb.piece("Cuirasse", o.nom, "Blindage")
    loft_z(pc, CUIRASSE, "Blindage", section_cuirasse)
    kit.chanfreiner(pc, 0.004)
    cuirasse = Surface(pc)
    rb.cuirasse = cuirasse   # le dos y pose ses omoplates

    # Le gorgerin : un col haut derrière et sur les côtés, échancré devant ; la tête s'y enfonce
    pc = rb.piece("Gorgerin", o.nom, "Blindage")
    centre = Vector((0, 0.028, 0))
    boucles = [[], [], [], []]
    for i in range(16):
        a = 2 * math.pi * i / 16
        d = Vector((math.sin(a), -math.cos(a), 0))
        devant = max(0.0, math.cos(a))
        haut = 1.566 - 0.040 * devant
        r_int, r_ext = 0.058, 0.088 + 0.008 * (1 - devant)
        boucles[0].append(centre + d * r_int + Vector((0, 0, 1.484)))
        boucles[1].append(centre + d * r_ext + Vector((0, 0, 1.484)))
        boucles[2].append(centre + d * (r_ext - 0.010) + Vector((0, 0, haut)))
        boucles[3].append(centre + d * r_int + Vector((0, 0, haut + 0.003)))
    kit.loft(pc, boucles, "Blindage", cyclique=True)
    kit.chanfreiner(pc, 0.0025)

    # Les trapèzes : deux dalles qui montent du plastron vers le cou, à la hauteur du torse touchable : plus
    # haut, une balle y passerait sans toucher (entre le torse et la tête), ou compterait comme un tir à la tête
    pc = rb.piece("Trapezes", o.nom, "Blindage")
    for s in (1, -1):
        dedans = [(-0.050, 1.478), (0.100, 1.478), (0.104, 1.518), (0.040, 1.528), (-0.032, 1.508)]
        dehors = [(-0.064, 1.420), (0.110, 1.420), (0.112, 1.456), (0.040, 1.470), (-0.050, 1.448)]
        kit.loft(pc, [[Vector((s * 0.072, y, z)) for y, z in dedans], [Vector((s * 0.166, y, z)) for y, z in dehors]], "Blindage")
    kit.chanfreiner(pc, 0.003)

    # Les pectoraux : deux grandes plaques peintes
    pc = rb.piece("Pectoraux", o.nom, "Peinture")
    for s in (1, -1):
        face_avant(pc, cuirasse, [(s * x, z) for x, z in PECTORAL], 0.013, "Peinture")
    kit.chanfreiner(pc, 0.003)
    pectoraux = Surface(pc)

    # Le cœur : un hexagone lumineux entre les pectoraux, tramé de triangles, dans un logement sombre
    pc = rb.piece("Coeur", o.nom, "Chassis")
    zc, yf = 1.414, -0.150
    hexa = lambda r, y: [Vector((r * math.cos(math.radians(90 + 60 * k)), y, zc + r * math.sin(math.radians(90 + 60 * k))))  # noqa: E731
                         for k in range(6)]
    kit.loft(pc, [hexa(0.040, -0.122), hexa(0.040, yf), hexa(0.031, yf), hexa(0.031, yf + 0.006)], "Chassis", cyclique=True)
    kit.chanfreiner(pc, 0.002)
    c = Vector((0, yf + 0.005, zc))
    bord = hexa(0.031, yf + 0.005)
    bm = pc.bm
    centre_v = bm.verts.new(c)
    bord_v = [bm.verts.new(p) for p in bord]
    for k in range(6):
        f = bm.faces.new((centre_v, bord_v[k], bord_v[(k + 1) % 6]))
        f.material_index = pc.mat("Lueur")
        f.normal_update()
        if f.normal.dot(-Y) < 0:
            f.normal_flip()
    trame = hexa(0.016, yf + 0.0042)
    for k in range(6):   # les rayons et l'hexagone intérieur : un cristal
        trait(pc, (c + bord[k]) * 0.5 + Y * -0.0008, -Y, bord[k] - c, 0.031, 0.0016, mat="Chassis")
        trait(pc, (trame[k] + trame[(k + 1) % 6]) * 0.5 + Y * -0.0008, -Y, trame[(k + 1) % 6] - trame[k], 0.016, 0.0016,
              mat="Chassis")

    # Des évents au-dessus des pectoraux
    pc = rb.piece("Events", o.nom, "Interieur")
    for s in (1, -1):
        p, n = cuirasse.toucher(Vector((s * 0.092, -0.4, 1.503)), Y)
        x, y = axes_decalque(n)
        for i in range(3):
            c2 = p + y * (0.004 - i * 0.008) + n * 0.0008
            kit.quad(pc, [c2 + x * (-0.022) + y * (-0.0024), c2 + x * 0.022 + y * (-0.0024), c2 + x * 0.022 + y * 0.0024,
                          c2 + x * (-0.022) + y * 0.0024], "Interieur", normale=n)

    # Marquages : le numéro sur le pectoral droit, la mire sur le gauche, la désignation dessous
    pc = rb.piece("Pectoraux_Marquages", o.nom, "MarquageNoir", genre="marquage")
    p, n = pectoraux.toucher(Vector((-0.098, -0.4, 1.420)), Y)
    x, y = axes_decalque(n)
    rb.texte(pc, NUMERO, 0.046, p, x, y, "MarquageNoir", decalage=0.0009)
    p, n = pectoraux.toucher(Vector((0.098, -0.4, 1.428)), Y)
    mire_crash(pc, p, n, 0.021)
    p, n = pectoraux.toucher(Vector((0.098, -0.4, 1.382)), Y)
    x, y = axes_decalque(n)
    rb.texte(pc, CODE, 0.011, p, x, y, "MarquageNoir", decalage=0.0009)

    # Une vieille réparation : une soudure en travers du pectoral droit
    pc = rb.piece("Pectoraux_Soudure", o.nom, "Soudure", genre="rafistolage")
    chemin = []
    for i in range(7):
        t = i / 6
        xx, zz = -0.056 - 0.082 * t, 1.352 + 0.036 * t - 0.010 * math.sin(math.pi * t)
        p, n = pectoraux.toucher(Vector((xx, -0.4, zz)), Y)
        chemin.append(p + n * 0.0006)
    kit.soudure(pc, chemin, r=0.0026, mat="Soudure", rng=random.Random(3), pas=0.0024)


OMOPLATE = [(0.050, 1.448), (0.128, 1.454), (0.148, 1.418), (0.138, 1.350), (0.098, 1.318), (0.056, 1.330),
            (0.050, 1.390)]
REACTEUR = [(0.0, 1.508), (0.046, 1.478), (0.046, 1.332), (0.0, 1.300), (-0.046, 1.332), (-0.046, 1.478)]
Z_REACTEUR = 1.404


def dos(rb):
    """Le dos, l'écho de la face (retour de l'utilisateur : « revois la forme complète à l'arrière ») : deux
    omoplates peintes répondent aux pectoraux, sur les pans d'omoplates de la cuirasse ; un réacteur hexagonal
    blindé court le long de la colonne, entre elles, et luit à travers un hublot tramé de triangles, comme le cœur
    de la poitrine ; des ailettes sur ses flancs, deux cheminées courtes au-dessus. La désignation sur une
    omoplate, la mire de crash-test sur l'autre, comme le numéro et la mire des pectoraux."""
    o = rb.os("Spine2")
    pc = rb.piece("Omoplates", o.nom, "Peinture")
    for s in (1, -1):
        plaque_sur(pc, rb.cuirasse, [(s * x, z) for x, z in OMOPLATE], ORIGINE, X, Z, -Y, 0.012, "Peinture")
    kit.chanfreiner(pc, 0.003)
    omoplates = Surface(pc)

    # le réacteur : un hexagone allongé, blindé, biseauté vers l'arrière
    pc = rb.piece("Reacteur", o.nom, "Blindage")

    def hexa(k, y):
        return [Vector((x * k, y, Z_REACTEUR + (z - Z_REACTEUR) * k)) for x, z in REACTEUR]

    kit.loft(pc, [hexa(1.0, 0.104), hexa(1.0, 0.166), hexa(0.86, 0.180)], "Blindage")
    kit.chanfreiner(pc, 0.003)
    # le hublot : l'hexagone lumineux, ses rayons et son cadre, comme le cœur de la poitrine
    bm = pc.bm
    c = Vector((0.0, 0.1806, Z_REACTEUR))
    bord = hexa(0.56, 0.1806)
    centre_v = bm.verts.new(c)
    bord_v = [bm.verts.new(q) for q in bord]
    for k in range(6):
        f = bm.faces.new((centre_v, bord_v[k], bord_v[(k + 1) % 6]))
        f.material_index = pc.mat("Lueur")
        f.normal_update()
        if f.normal.dot(Y) < 0:
            f.normal_flip()
    for k in range(6):
        trait(pc, (c + bord[k]) * 0.5, Y, bord[k] - c, (bord[k] - c).length, 0.0016, mat="Chassis")
        trait(pc, (bord[k] + bord[(k + 1) % 6]) * 0.5, Y, bord[(k + 1) % 6] - bord[k], (bord[(k + 1) % 6] - bord[k]).length + 0.002,
              0.0026, mat="Chassis")
    # des ailettes sur ses flancs
    for s in (1, -1):
        for i in range(4):
            z = 1.344 + i * 0.030
            kit.loft(pc, [[Vector((s * 0.044, y, z + dz)) for y, dz in ((0.118, 0.0), (0.162, 0.0), (0.162, 0.008), (0.118, 0.008))],
                          [Vector((s * 0.060, y, z + dz)) for y, dz in ((0.120, 0.0), (0.158, 0.0), (0.158, 0.008), (0.120, 0.008))]],
                     "Metal")
    # deux cheminées courtes sur les pans du haut (plus hautes, elles sortiraient du torse touchable)
    for s in (1, -1):
        c0, c1 = Vector((s * 0.024, 0.140, 1.474)), Vector((s * 0.030, 0.146, 1.497))
        faces = kit.cylindre(pc, c0, c1, 0.011, 8, mat="Chassis")
        kit.chanfreiner(pc, 0.0015, faces, 60)
        kit.disque(pc, c1 + (c1 - c0).normalized() * 0.0004, (c1 - c0).normalized(), 0.007, 8, mat="Interieur")

    # Marquages : la désignation sur l'omoplate gauche, la mire de crash-test sur la droite ; sur le réacteur, sous
    # le hublot, la consigne
    pc = rb.piece("Dos_Marquages", o.nom, "MarquageNoir", genre="marquage")
    p, n = omoplates.toucher(Vector((0.098, 0.4, 1.396)), -Y)
    x, y = axes_decalque(n)
    rb.texte(pc, CODE, 0.022, p, x, y, "MarquageNoir", decalage=0.0009)
    p, n = omoplates.toucher(Vector((-0.098, 0.4, 1.404)), -Y)
    mire_crash(pc, p, n, 0.020)
    x, y = axes_decalque(Y)
    rb.texte(pc, "NE PAS OUVRIR", 0.0048, Vector((0.0, 0.1806, 1.336)), x, y, "Marquage", decalage=0.0009)


VENTRE = [  # (os, z bas, z haut, demi-largeur, devant, dos) : plus étroit que le buste, la taille en V
    ("Spine1", 1.268, 1.316, 0.114, -0.106, 0.094),
    ("Spine1", 1.220, 1.268, 0.110, -0.102, 0.092),
    ("Spine", 1.172, 1.220, 0.106, -0.099, 0.090),
    ("Spine", 1.124, 1.172, 0.110, -0.098, 0.090),
]


def ventre(rb):
    o = rb.os("Spine1")
    pc = rb.piece("Colonne", o.nom, "Interieur")
    kit.cylindre(pc, Vector((0, 0.020, 1.100)), Vector((0, 0.028, 1.345)), 0.064, 12, mat="Interieur")
    for i, (os_nom, z0, z1, hw, f, b) in enumerate(VENTRE):
        pc = rb.piece(f"Ventre_{i + 1}", f"mixamorig:{os_nom}", "Chassis")
        loft_z(pc, [(z0, hw - 0.006, f + 0.006, b - 0.004), (z0 + 0.010, hw, f, b), (z1, hw + 0.002, f - 0.002, b)],
               "Chassis", lambda hw_, f_, b_: section_rect(-hw_, hw_, f_, b_, 0.034))
        kit.chanfreiner(pc, 0.003)


def section_dos_enveloppe(hw, cote, b0, h, t, avant=-0.020):
    """La plaque du bas du dos, en (x, y) : du devant du flanc gauche au devant du flanc droit en passant par le
    dos, une arête au milieu ; épaisse de t vers le corps."""
    ext = [(cote, avant), (cote, 0.45 * b0), (0.72 * hw, b0 + 0.004), (0.0, b0 + h), (-0.72 * hw, b0 + 0.004),
           (-cote, 0.45 * b0), (-cote, avant)]
    centre = Vector((0.0, 0.02))
    inte = []
    for x, y in reversed(ext):
        d = (Vector((x, y)) - centre).normalized()
        inte.append((x - d.x * t, y - d.y * t))
    return ext + inte


def dos_bas(rb, mat="Peinture"):
    """Le bas du dos d'une seule plaque à arête, sur Spine1, qui enveloppe les flancs jusqu'à leur milieu (retours de
    l'utilisateur : les écailles faisaient des côtes, puis deux plaques l'une sur l'autre des bandes ; les bandes de
    l'abdomen ne se voient plus que de face). Son bas reste plus large que la quatrième bande de l'abdomen, qui en
    perçait le coin arrière (un petit rectangle sombre). Dessous, la taille : un anneau sombre en retrait, sur Spine,
    entre la plaque et la ceinture (la colonne plie là : le lean tourne Spine)."""
    pc = rb.piece("Dos_Bas", "mixamorig:Spine1", mat)
    kit.loft(pc, [[Vector((x, y, z)) for x, y in section_dos_enveloppe(hw, c, b0, h, 0.010)]
                  for z, hw, c, b0, h in ((1.128, 0.104, 0.126, 0.090, 0.020), (1.318, 0.118, 0.132, 0.094, 0.022))], mat)
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    surface = Surface(pc)
    pc = rb.piece("Taille", "mixamorig:Spine", "Chassis")
    loft_z(pc, [(1.088, 0.094, -0.086, 0.082), (1.150, 0.098, -0.090, 0.086)], "Chassis",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.030))
    kit.chanfreiner(pc, 0.003)
    return surface


def bassin(rb):
    """Le bassin de la v4, repris tel quel à la demande de l'utilisateur (2026-10-26 : « je n'aime pas l'effet slip
    finalement, je préfère cette version » ; un bloc peint, large aux hanches, avait l'air d'un slip, deux fois) :
    un bloc sombre, une ceinture gris acier et sa boucle hexagonale, la coquille sombre et anguleuse devant, des
    tassettes peintes sur les hanches, une plaque sombre derrière."""
    o = rb.os("Hips")
    pc = rb.piece("Bassin", o.nom, "Chassis")
    loft_z(pc, [(0.902, 0.050, -0.062, 0.062), (0.930, 0.090, -0.086, 0.094), (0.990, 0.124, -0.098, 0.106),
                (1.050, 0.128, -0.102, 0.100)], "Chassis", lambda hw, f, b: section_rect(-hw, hw, f, b, min(0.034, 0.36 * hw)))
    kit.chanfreiner(pc, 0.004)
    # la ceinture, plus étroite que le buste, et sa boucle hexagonale
    pc = rb.piece("Ceinture", o.nom, "Blindage")
    loft_z(pc, [(1.046, 0.132, -0.108, 0.104), (1.104, 0.129, -0.106, 0.102)], "Blindage",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.036))
    kit.chanfreiner(pc, 0.003)
    hexa = [Vector((0.024 * math.cos(math.radians(60 * k)), 0, 1.075 + 0.024 * math.sin(math.radians(60 * k)))) for k in range(6)]
    kit.loft(pc, [[p + Vector((0, -0.104, 0)) for p in hexa], [p * 0.8 + Vector((0, -0.118, 1.075 * 0.2)) for p in hexa]], "Metal")
    # la coquille, sombre et anguleuse
    pc = rb.piece("Coquille", o.nom, "Chassis")
    kit.loft(pc, [[Vector((x, y, 0.918)) for x, y in section_rect(-0.026, 0.026, -0.096, -0.080, 0.005)],
                  [Vector((x, y, 1.044)) for x, y in section_rect(-0.050, 0.050, -0.120, -0.100, 0.006)]], "Chassis")
    kit.chanfreiner(pc, 0.003)
    # les tassettes, peintes, qui s'écartent vers le bas
    pc = rb.piece("Tassettes", o.nom, "Peinture")
    for s in (1, -1):
        haut_ = [(s * 0.128, -0.082), (s * 0.141, -0.082), (s * 0.141, 0.072), (s * 0.128, 0.072)]
        bas_ = [(s * 0.150, -0.072), (s * 0.163, -0.072), (s * 0.163, 0.060), (s * 0.150, 0.060)]
        kit.loft(pc, [[Vector((x, y, 0.926)) for x, y in bas_], [Vector((x, y, 1.044)) for x, y in haut_]], "Peinture")
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece("Bassin_Dos", o.nom, "Chassis")
    kit.loft(pc, [[Vector((x, y, 0.935)) for x, y in section_rect(-0.090, 0.090, 0.098, 0.118, 0.006)],
                  [Vector((x, y, 1.040)) for x, y in section_rect(-0.100, 0.100, 0.100, 0.122, 0.006)]], "Chassis")
    kit.chanfreiner(pc, 0.003)


# --- les variantes du bas du dos ---------------------------------------------------------------------
#
# Montrées à l'utilisateur le 2026-10-26 (« fais-moi plusieurs variantes ») avec un bassin d'un bloc, qu'il a refusé ;
# il a gardé le bas du dos d'une seule plaque (A). Les autres restent à portée : --variantes A,B,C,D les rend côte
# à côte, sur le bassin de la v4.

DORSAL = [(0.050, 1.296), (0.118, 1.302), (0.126, 1.262), (0.100, 1.198), (0.060, 1.150), (0.036, 1.154), (0.036, 1.232)]


def dos_dorsaux(rb):
    """B. Le bas du dos gris acier, et deux dorsaux peints en V sous les omoplates, qui se rejoignent presque à la
    taille : l'écho des pectoraux sur la cuirasse, une carrure en V."""
    surface = dos_bas(rb, "Blindage")
    pc = rb.piece("Dorsaux", "mixamorig:Spine1", "Peinture")
    for s in (1, -1):
        plaque_sur(pc, surface, [(s * x, z) for x, z in DORSAL], ORIGINE, X, Z, -Y, 0.010, "Peinture")
    kit.chanfreiner(pc, 0.0025)


COLONNE_DOS = [(0.0, 1.314), (0.029, 1.296), (0.023, 1.160), (0.0, 1.134), (-0.023, 1.160), (-0.029, 1.296)]


def dos_colonne(rb):
    """C. Le bas du dos peint, et une colonne blindée qui sort de sous la pointe du réacteur et descend jusqu'à la
    taille, une fente lumineuse sur sa longueur : un axe fort, du cou au bassin."""
    surface = dos_bas(rb)
    pc = rb.piece("Colonne_Dos", "mixamorig:Spine1", "Blindage")
    plaque_sur(pc, surface, COLONNE_DOS, ORIGINE, X, Z, -Y, 0.014, "Blindage")
    kit.chanfreiner(pc, 0.002)
    colonne = Surface(pc)
    pc = rb.piece("Colonne_Dos_Lueur", "mixamorig:Spine1", "Lueur")
    plaque_sur(pc, colonne, [(-0.005, 1.276), (0.005, 1.276), (0.005, 1.170), (-0.005, 1.170)], ORIGINE, X, Z, -Y,
               0.0012, "Lueur", enfoncement=0.0005)


def dos_mire(rb):
    """D. Le bas du dos peint, une grande mire de crash-test sur un pan (le mannequin d'essai, bible § 5)."""
    surface = dos_bas(rb)
    pc = rb.piece("Dos_Bas_Marquages", "mixamorig:Spine1", "MarquageNoir", genre="marquage")
    p, n = surface.toucher(Vector((-0.050, 0.4, 1.226)), -Y)
    mire_crash(pc, p, n, 0.024)


VARIANTES = {  # lettre : (nom, le bas du dos)
    "A": ("UNE PLAQUE", dos_bas),
    "B": ("DORSAUX EN V", dos_dorsaux),
    "C": ("COLONNE", dos_colonne),
    "D": ("MIRE", dos_mire),
}


# --- les skins ---------------------------------------------------------------------------------------
#
# Demandés par l'utilisateur le 2026-10-26 : « un skin vierge, de base, sans éraflure, sans usure ni sticker, puis
# plusieurs variantes ». Un skin, c'est une usure (les réglages de kit.materiau, famille de matériau par famille,
# comme les finitions des armes : commun_armes.MARQUES_USURE), les genres de pièces qu'il garde (les marquages
# du programme d'essai, le rafistolage), des pièces refaites dans un autre matériau, et une livrée (des
# décalques à lui). La peinture reste toujours la couleur du joueur, sur les mêmes plaques : c'est elle qui
# l'identifie (bible § 5) ; les réparations sont ternes et sur de petites pièces seulement.

def livree_mannequin(rb):
    """Le mannequin d'essai (bible § 5) : une mire de crash-test sur chaque articulation (genoux, coudes,
    hanches), comme sur les mannequins des essais de choc, et une règle graduée sur chaque cuisse."""
    for cote, s in (("Left", 1), ("Right", -1)):
        dehors = Vector((s, 0, 0))
        # les genoux : sur le pan extérieur de la genouillère
        ob = rb.os(f"{cote}Leg", u=dehors, v=AVANT)
        surface, os_nom = rb.surface(f"Genouillere_{cote}")
        pc = rb.piece(f"Livree_Genou_{cote}", os_nom, "Marquage")
        n0 = ob.dir(47.6)
        p, n = surface.toucher(ob.p(0.010, 0.0212, 0.0665) + n0 * 0.3, -n0)
        mire_crash(pc, p, n, 0.014)
        # les coudes : sur le pan de la coudière tourné vers le dehors, bras baissé
        oa = rb.os(f"{cote}ForeArm", u=HAUT, v=AVANT)
        surface, os_nom = rb.surface(f"Coudiere_{cote}")
        pc = rb.piece(f"Livree_Coude_{cote}", os_nom, "Marquage")
        n0 = oa.dir(-47.8)
        p, n = surface.toucher(oa.p(0.004, 0.0159, -0.041) + n0 * 0.3, -n0)
        mire_crash(pc, p, n, 0.012)
        # les hanches : au milieu de la tassette
        surface, os_nom = rb.surface("Tassettes")
        pc = rb.piece(f"Livree_Hanche_{cote}", os_nom, "Marquage")
        p, n = surface.toucher(Vector((s * 0.5, -0.004, 0.988)), -dehors)
        mire_crash(pc, p, n, 0.020)
        # la règle : un trait noir le long du pan extérieur de la lame de la cuisse, gradué tous les 2,5 cm
        o = rb.os(f"{cote}UpLeg", u=dehors, v=AVANT)
        surface, os_nom = rb.surface(f"Cuissard_{cote}")
        pc = rb.piece(f"Livree_Regle_{cote}", os_nom, "MarquageNoir")
        ruban(pc, surface, [o.p(st, 0.036) for st in frange(0.095, 0.295, 0.0125)], -o.v, 0.0030, "MarquageNoir")
        for k, st in enumerate(frange(0.100, 0.290, 0.025)):
            longueur = 0.016 if k % 2 == 0 else 0.009
            ruban(pc, surface, [o.p(st, 0.036 - a) for a in frange(0.0, longueur, longueur / 3)], -o.v, 0.0022,
                  "MarquageNoir")


def livree_bandes(rb):
    """Deux bandes blanches, comme sur une voiture de course : du front à la nuque sur le casque, le long des
    lames peintes des cuisses et des bras, et sur le bas du dos."""
    surface, os_nom = rb.surface("Casque")
    pc = rb.piece("Livree_Casque", os_nom, "Marquage")
    for s in (1, -1):
        ruban(pc, surface, [Vector((s * 0.018, y, 1.9)) for y in frange(-0.066, 0.078, 0.008)], -Z, 0.010, "Marquage")
    surface, os_nom = rb.surface("Dos_Bas")
    pc = rb.piece("Livree_Dos", os_nom, "Marquage")
    for s in (1, -1):
        ruban(pc, surface, [Vector((s * 0.024, 0.0, z)) for z in frange(1.140, 1.296, 0.012)], -Y, 0.010, "Marquage")
    for cote, s in (("Left", 1), ("Right", -1)):
        o = rb.os(f"{cote}UpLeg", u=Vector((s, 0, 0)), v=AVANT)
        surface, os_nom = rb.surface(f"Cuissard_{cote}")
        pc = rb.piece(f"Livree_Cuisse_{cote}", os_nom, "Marquage")
        for a in (0.026, -0.016):   # à 2,1 cm de part et d'autre de l'arête (un peu vers le dehors)
            ruban(pc, surface, [o.p(st, a) for st in frange(0.085, 0.315, 0.0115)], -o.v, 0.010, "Marquage")
        oa = rb.os(f"{cote}Arm", u=HAUT, v=AVANT)
        surface, os_nom = rb.surface(f"Biceps_{cote}")
        pc = rb.piece(f"Livree_Bras_{cote}", os_nom, "Marquage")
        for a in (0.016, -0.016):
            ruban(pc, surface, [oa.p(st, a) for st in frange(0.115, 0.235, 0.012)], -oa.v, 0.008, "Marquage")


SKINS = {  # clé : titre, usure par famille, genres gardés (absent : tous), pièces refaites, livrée
    # la base de tous : sortie d'atelier, ni usure ni marquage ni réparation
    "vierge": dict(titre="VIERGE", usure=PROPRE, genres=set()),
    # le robot tel qu'il est : l'usure de tous les jours, les marquages du programme d'essai, une soudure et une
    # plaque d'apprêt rivetée
    "prototype": dict(titre="PROTOTYPE", usure={}),
    # repeint au pinceau sans décaper : l'apprêt affleure au bord des éclats, des retouches d'une teinte plus
    # sombre, faute de la bonne
    "repeint": dict(titre="REPEINT", usure={
        "peinture": dict(couche=(APPRET, 0.78), retouches=0.55, teinte_retouches=lambda c: assombrir(c, 0.55),
                         eclats=0.85, bords=0.9),
        "blindage": dict(couche=(APPRET, 0.78), retouches=0.4, teinte_retouches=srgb(44, 47, 52), eclats=0.6,
                         bords=0.8)}),
    # jamais nettoyé : huile, poussière, crasse et suie
    "encrasse": dict(titre="ENCRASSÉ", usure={
        **{f: dict(huile=0.95, poussiere=1.0, salete=1.0, variation=0.22) for f in FAMILLES},
        "marquage": dict(huile=0.5, poussiere=0.8, salete=0.9)}),
    # des plaques prises sur d'autres châssis, ternes et petites : la lame de l'avant-bras droit à l'apprêt, le
    # mollet gauche en acier nu, l'épaulière droite gris acier
    "depareille": dict(titre="DÉPAREILLÉ", usure={}, remplacements={
        "Gantelet_Right": {"Peinture": "Appret"}, "Mollet_Left": {"Peinture": "Metal"},
        "Epauliere_Right": {"Peinture": "Blindage"}}),
    # les livrées, sur une peinture propre
    "mannequin": dict(titre="MANNEQUIN", usure=PROPRE, genres={"marquage"}, livree=livree_mannequin),
    "bandes": dict(titre="BANDES", usure=PROPRE, genres={"marquage"}, livree=livree_bandes),
}


# --- les bras ---------------------------------------------------------------------------------------

# Les bras (2026-10-26, retour de l'utilisateur : « affine les bras et les épaules, surtout à l'avant ; que toute
# la morphologie soit cohérente ») : le langage des jambes. Une poutre à plat et un vérin à nu, une lame peinte
# à arête devant, une plaque blindée sur le flanc ; l'épaulière n'est plus qu'une lame peinte sur le dessus de
# l'épaule (dehors, bras baissé), qui ne déborde plus vers l'avant. Bras en croix : 0° le haut (le dehors, bras
# baissé), 90° l'avant.
EPAULIERE = [(-58, 0.94), (0, 1.22), (58, 0.94)]        # une lame sur le dessus de l'épaule
EPAULE_DOS = [(-122, 0.94), (-90, 1.14), (-58, 0.94)]   # une plaque peinte derrière l'épaule
LAME_AVANT = [(30, 0.92), (90, 1.26), (150, 0.92)]      # la lame peinte, devant le bras et l'avant-bras
FLANC_BRAS = [(-26, 0.96), (0, 1.12), (26, 0.96)]       # la plaque blindée du dehors de l'avant-bras
COUDIERE = [(-140, 0.94), (-90, 1.26), (-40, 0.94)]     # le coude en pointe vers l'arrière
ARRIERE = [(-150, 0.94), (-90, 1.20), (-30, 0.94)]      # une lame peinte à arête, derrière le bras ou l'avant-bras


def bras(rb, cote):
    s = 1 if cote == "Left" else -1
    clav = rb.os(f"{cote}Shoulder", u=HAUT, v=AVANT)
    o = rb.os(f"{cote}Arm", u=HAUT, v=AVANT)
    ob = rb.os(f"{cote}ForeArm", u=HAUT, v=AVANT)

    # le logement de l'épaule, sous les trapèzes, moins profond devant
    pc = rb.piece(f"Epaule_{cote}", clav.nom, "Chassis")
    kit.loft(pc, [[clav.p(0.030, a, b) for a, b in section_rect(-0.028, 0.040, -0.060, 0.042, 0.012)],
                  [clav.p(0.124, a, b) for a, b in section_rect(-0.030, 0.044, -0.064, 0.046, 0.014)]], "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=40)

    # le bras : l'articulation, une poutre à plat, un vérin à nu derrière
    pc = rb.piece(f"Bras_{cote}", o.nom, "Chassis")
    tambour(pc, o.p(0.010), o.d, 0.046, 0.022, n=12, mat="Metal")
    kit.loft(pc, [[o.p(0.055, a, b) for a, b in section_rect(-0.036, 0.036, -0.038, 0.034, 0.011)],
                  [o.p(0.170, a, b) for a, b in section_rect(-0.033, 0.033, -0.035, 0.031, 0.010)],
                  [o.p(0.262, a, b) for a, b in section_rect(-0.031, 0.031, -0.033, 0.029, 0.009)]], "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=40)
    kit.cylindre(pc, o.p(0.100, -0.046, 0.0), o.p(0.245, -0.042, 0.0), 0.008, 8, mat="Metal")
    faces = kit.cylindre(pc, o.p(0.095, -0.046, 0.0), o.p(0.165, -0.045, 0.0), 0.013, 8, mat="Chassis")
    kit.chanfreiner(pc, 0.002, faces, 60)
    # la lame peinte derrière le bras
    pc = rb.piece(f"Triceps_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(0.100, 0.040), (0.140, 0.047), (0.228, 0.045), (0.254, 0.032)], ARRIERE, 0.009, "Peinture")
    kit.chanfreiner(pc, 0.002, angle_min=35)
    # la lame peinte devant le bras
    pc = rb.piece(f"Biceps_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(0.105, 0.040), (0.140, 0.046), (0.225, 0.044), (0.252, 0.032)], LAME_AVANT, 0.009, "Peinture")
    kit.chanfreiner(pc, 0.002, angle_min=35)

    # l'épaulière : une lame peinte sur le dessus de l'épaule, une plaque blindée derrière
    pc = rb.piece(f"Epauliere_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(-0.028, 0.060), (0.010, 0.072), (0.075, 0.074), (0.118, 0.064), (0.146, 0.038)], EPAULIERE,
                 0.012, "Peinture", centre=(0.006, 0.0))
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    epauliere = Surface(pc)
    pc2 = rb.piece(f"Epauliere_Lame_{cote}", o.nom, "Peinture")
    coque_profil(pc2, o, [(-0.004, 0.056), (0.060, 0.060), (0.104, 0.048)], EPAULE_DOS, 0.009, "Peinture")
    kit.chanfreiner(pc2, 0.002, angle_min=35)
    # la mire de crash-test, sur le pan avant de la lame (tournée vers le dehors et l'avant, bras baissé)
    pc3 = rb.piece(f"Epauliere_Marquages_{cote}", o.nom, "Marquage", genre="marquage")
    p, n = epauliere.toucher(o.p(0.062) + o.dir(29) * 0.30, -o.dir(29))
    mire_crash(pc3, p, n, 0.017)

    # le coude : un tambour (la charnière) et une coudière en pointe vers l'arrière
    pc = rb.piece(f"Coude_{cote}", ob.nom, "Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.036, 0.030, n=12, mat="Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.024, 0.036, n=10, mat="Metal")
    pc2 = rb.piece(f"Coudiere_{cote}", ob.nom, "Blindage")
    coque_profil(pc2, ob, [(-0.032, 0.036), (0.000, 0.044), (0.040, 0.040)], COUDIERE, 0.009, "Blindage")
    kit.chanfreiner(pc2, 0.002, angle_min=35)

    # l'avant-bras : une poutre à plat, la lame peinte devant et sa fente lumineuse, une plaque blindée sur le
    # flanc, la bague du poignet. À gauche, tout tient sous la manchette du gant (plus mince, sans flanc).
    pc = rb.piece(f"AvantBras_{cote}", ob.nom, "Chassis")
    kit.loft(pc, [[ob.p(0.030, a, b) for a, b in section_rect(-0.032, 0.032, -0.033, 0.033, 0.010)],
                  [ob.p(0.150, a, b) for a, b in section_rect(-0.030, 0.030, -0.031, 0.031, 0.009)],
                  [ob.p(0.262, a, b) for a, b in section_rect(-0.027, 0.027, -0.028, 0.028, 0.008)]], "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=40)
    faces = kit.cylindre(pc, ob.p(0.248), ob.p(0.278), 0.034, 10, mat="Metal")
    kit.chanfreiner(pc, 0.002, faces, 60)
    k = 0.82 if s > 0 else 1.0
    pc = rb.piece(f"Gantelet_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(0.045, 0.040 * k), (0.070, 0.046 * k), (0.185, 0.046 * k), (0.238, 0.036 * k)], LAME_AVANT,
                 0.010, "Peinture")
    kit.chanfreiner(pc, 0.002, angle_min=35)
    gantelet = Surface(pc)
    pc4 = rb.piece(f"Gantelet_Lueur_{cote}", ob.nom, "Lueur")
    plaque_sur(pc4, gantelet, [(0.085, -0.0035), (0.195, -0.0025), (0.195, 0.0025), (0.085, 0.0035)], ob.p(0.0), ob.d,
               ob.dir(150), -ob.dir(60), 0.0011, "Lueur", enfoncement=0.0005)
    if s < 0:   # à droite, le bras que l'on voit nu : une plaque derrière l'avant-bras
        pc = rb.piece(f"AvantBras_Arriere_{cote}", ob.nom, "Peinture")
        coque_profil(pc, ob, [(0.062, 0.038), (0.170, 0.040), (0.222, 0.030)], ARRIERE, 0.008, "Peinture")
        kit.chanfreiner(pc, 0.002, angle_min=35)
    if s < 0:   # à droite, le bras que l'on voit nu : la plaque du flanc, et des bâtons de victoires dessus
        pc = rb.piece(f"AvantBras_Flanc_{cote}", ob.nom, "Blindage")
        coque_profil(pc, ob, [(0.060, 0.044), (0.170, 0.044), (0.215, 0.034)], FLANC_BRAS, 0.008, "Blindage")
        kit.chanfreiner(pc, 0.002, angle_min=35)
        flanc = Surface(pc)
        pc5 = rb.piece(f"Gantelet_Marquages_{cote}", ob.nom, "Marquage", genre="marquage")
        for i in range(4):
            p, n = flanc.toucher(ob.p(0.105 + i * 0.011, 0.25, 0.010), -ob.u)
            trait(pc5, p, n, ob.v, 0.014, 0.0022)
        p, n = flanc.toucher(ob.p(0.1215, 0.25, 0.010), -ob.u)
        trait(pc5, p, n, (ob.d * 0.045 + ob.v * 0.016).normalized(), 0.048, 0.0022)


def main_(rb, cote):
    o = rb.os(f"{cote}Hand", u=HAUT, v=AVANT)
    pc = rb.piece(f"Paume_{cote}", o.nom, "Chassis")
    kit.loft(pc, [[o.p(0.004, a, b) for a, b in section_rect(-0.019, 0.019, -0.040, 0.030, 0.008)],
                  [o.p(0.050, a, b) for a, b in section_rect(-0.021, 0.021, -0.057, 0.041, 0.008)],
                  [o.p(0.114, a, b) for a, b in section_rect(-0.018, 0.018, -0.057, 0.041, 0.008)]], "Chassis")
    kit.chanfreiner(pc, 0.003)
    # le dos de la main : deux plaques blindées, la seconde en garde de jointures
    pc = rb.piece(f"DosMain_{cote}", o.nom, "Blindage")
    kit.loft(pc, [[o.p(0.014, a, b) for a, b in section_rect(0.016, 0.028, -0.046, 0.030, 0.005)],
                  [o.p(0.074, a, b) for a, b in section_rect(0.017, 0.031, -0.052, 0.036, 0.005)]], "Blindage")
    kit.loft(pc, [[o.p(0.080, a, b) for a, b in section_rect(0.014, 0.029, -0.058, 0.042, 0.004)],
                  [o.p(0.118, a, b) for a, b in section_rect(0.012, 0.024, -0.056, 0.040, 0.004)]], "Blindage")
    kit.chanfreiner(pc, 0.002)
    # les doigts : trois phalanges épaisses, la dernière en pointe, et leurs jointures
    for doigt in DOIGTS:
        largeur = 0.0155 if doigt == "Pinky" else 0.0175
        for k in (1, 2, 3):
            f = rb.os(f"{cote}Hand{doigt}{k}", u=HAUT, v=AVANT)
            pc = rb.piece(f"{doigt}{k}_{cote}", f.nom, "Chassis")
            fin = f.L + (0.011 if k == 3 else -0.002)
            ep = 0.0092 - 0.0005 * k
            bout = 0.55 if k == 3 else 0.92
            kit.loft(pc, [[f.p(0.003, a, b) for a, b in section_rect(-ep, ep, -largeur / 2, largeur / 2, 0.003)],
                          [f.p(fin, a * bout, b * bout) for a, b in section_rect(-ep, ep, -largeur / 2, largeur / 2, 0.002)]],
                     "Chassis")
            kit.cylindre(pc, f.p(0.0, 0.0, -largeur / 2 + 0.001), f.p(0.0, 0.0, largeur / 2 - 0.001), 0.0064, 8, mat="Metal")
    for k in (1, 2, 3):
        f = rb.os(f"{cote}HandThumb{k}", u=HAUT, v=AVANT)
        pc = rb.piece(f"Thumb{k}_{cote}", f.nom, "Chassis")
        fin = f.L + (0.009 if k == 3 else -0.002)
        bout = 0.6 if k == 3 else 0.92
        kit.loft(pc, [[f.p(0.003 if k > 1 else -0.006, a, b) for a, b in section_rect(-0.010, 0.010, -0.010, 0.010, 0.003)],
                      [f.p(fin, a * bout, b * bout) for a, b in section_rect(-0.009, 0.009, -0.009, 0.009, 0.002)]], "Chassis")
        if k > 1:
            kit.cylindre(pc, f.p(0.0, -0.0098, 0.0), f.p(0.0, 0.0098, 0.0), 0.0066, 8, mat="Metal")


# --- les jambes --------------------------------------------------------------------------------------

# Les jambes (2026-10-26, retour de l'utilisateur sur la v2 : « plus de caractère, plus de faces, pas rondes, ou
# peut-être plus fines ») : une ossature en poutre à plat, des vérins à nu, et des plaques en lames à arête, faites
# de faces planes, qui ne font plus le tour de la jambe : une lame peinte devant la cuisse et le tibia, des plaques
# blindées sur les flancs, le genou et le haut du mollet.
GENOUILLERE = [(48, 0.96), (90, 1.30), (132, 0.96)]     # deux pans qui se rejoignent en pointe devant
JAMBIERE = [(26, 0.92), (90, 1.30), (154, 0.92)]        # une lame en V, l'arête devant
FLANC_TIBIA = [(-22, 1.00), (0, 1.07), (22, 1.00)]      # une plaque à arête sur le dehors du tibia
MOLLET = [(222, 0.95), (270, 1.22), (318, 0.95)]        # la lame peinte du mollet, en V vers l'arrière


def jambe(rb, cote):
    s = 1 if cote == "Left" else -1
    dehors = Vector((s, 0, 0))
    o = rb.os(f"{cote}UpLeg", u=dehors, v=AVANT)
    ob = rb.os(f"{cote}Leg", u=dehors, v=AVANT)

    pc = rb.piece(f"Hanche_{cote}", o.nom, "Chassis")
    tambour(pc, o.p(0.004), o.u, 0.060, 0.040, n=14, mat="Chassis")
    tambour(pc, o.p(0.004, 0.030), o.u, 0.042, 0.016, n=12, mat="Metal")
    # la cuisse : une poutre à plat, et un vérin à nu derrière, côté intérieur
    pc = rb.piece(f"Cuisse_{cote}", o.nom, "Chassis")
    kit.loft(pc, [[o.p(0.040, a, b) for a, b in section_rect(-0.050, 0.050, -0.058, 0.034, 0.015)],
                  [o.p(0.200, a, b) for a, b in section_rect(-0.044, 0.046, -0.052, 0.032, 0.013)],
                  [o.p(0.385, a, b) for a, b in section_rect(-0.036, 0.038, -0.042, 0.028, 0.010)]], "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=40)
    kit.cylindre(pc, o.p(0.070, -0.060, -0.018), o.p(0.330, -0.050, -0.016), 0.009, 8, mat="Metal")
    faces = kit.cylindre(pc, o.p(0.065, -0.060, -0.018), o.p(0.190, -0.056, -0.017), 0.015, 8, mat="Chassis")
    kit.chanfreiner(pc, 0.002, faces, 60)
    # la lame de la cuisse, peinte, devant : deux pans, une arête, une pointe vers le genou
    pc = rb.piece(f"Cuissard_{cote}", o.nom, "Peinture")
    stations = [(0.070, 0.064, 0.046, 0.018, 0.012), (0.115, 0.072, 0.052, 0.022, 0.012), (0.250, 0.060, 0.050, 0.019, 0.012),
                (0.330, 0.040, 0.046, 0.012, 0.011), (0.366, 0.014, 0.042, 0.006, 0.008)]
    kit.loft(pc, [[o.p(st, a, b) for a, b in section_lame(aw, b0, h, t)] for st, aw, b0, h, t in stations], "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    cuissard = Surface(pc)
    # la lame peinte de l'arrière de la cuisse, l'arête vers l'arrière
    pc = rb.piece(f"Cuisse_Arriere_{cote}", o.nom, "Peinture")
    stations = [(0.080, 0.050, 0.064, 0.014, 0.010), (0.140, 0.056, 0.067, 0.016, 0.010), (0.260, 0.048, 0.061, 0.013, 0.010),
                (0.322, 0.024, 0.052, 0.006, 0.008)]
    kit.loft(pc, [[o.p(st, a, -b) for a, b in section_lame(aw, b0, h, t)] for st, aw, b0, h, t in stations], "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    # la plaque blindée du flanc, l'arête vers le dehors, en pointe vers le bas
    pc = rb.piece(f"Cuisse_Flanc_{cote}", o.nom, "Blindage")
    stations = [(0.050, 0.044, 0.054, 0.017, 0.010), (0.090, 0.048, 0.058, 0.019, 0.010), (0.220, 0.044, 0.055, 0.017, 0.010),
                (0.290, 0.022, 0.050, 0.008, 0.008)]
    kit.loft(pc, [[o.p(st, a, b) for a, b in section_lame_dehors(bw, a0, h, t)] for st, bw, a0, h, t in stations], "Blindage")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    if s > 0:   # la pièce rapportée : une plaque d'apprêt rivetée sur le pan dehors de la lame de la cuisse gauche
        pc2 = rb.piece(f"Cuissard_Piece_{cote}", o.nom, "Appret", genre="rafistolage")
        p, n = cuissard.toucher(o.p(0.200, 0.036, 0.062) + o.dir(60) * 0.35, -o.dir(60))
        x, y = axes_decalque(n, -o.d)
        sec = kit.contour_rect(0.036, 0.058, 0.006)
        kit.loft(pc2, [[p + x * a + y * b + n * 0.0005 for a, b in sec], [p + x * a + y * b + n * 0.0038 for a, b in sec]], "Appret")
        kit.chanfreiner(pc2, 0.0012)
        for a, b in ((-0.012, -0.022), (0.012, -0.022), (0.012, 0.022), (-0.012, 0.022)):
            kit.rivet(pc2, p + x * a + y * b + n * 0.0038, n, r=0.0028, h=0.0015, mat="Metal")

    # le genou : tambour et genouillère à deux pans, en pointe (elle suit le tibia)
    pc = rb.piece(f"Genou_{cote}", ob.nom, "Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.044, 0.040, n=12, mat="Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.030, 0.046, n=12, mat="Metal")
    pc = rb.piece(f"Genouillere_{cote}", ob.nom, "Blindage")
    coque_profil(pc, ob, [(-0.050, 0.050), (-0.022, 0.062), (0.016, 0.066), (0.054, 0.056), (0.070, 0.044)], GENOUILLERE,
                 0.011, "Blindage")
    kit.chanfreiner(pc, 0.0025, angle_min=35)

    # le tibia : une poutre à plat, la lame peinte devant et sa fente lumineuse, une plaque sur le flanc, le haut
    # du mollet blindé et le vérin à nu dessous
    pc = rb.piece(f"Tibia_{cote}", ob.nom, "Chassis")
    kit.loft(pc, [[ob.p(0.055, a, b) for a, b in section_rect(-0.034, 0.034, -0.040, 0.030, 0.010)],
                  [ob.p(0.250, a, b) for a, b in section_rect(-0.030, 0.030, -0.036, 0.028, 0.009)],
                  [ob.p(0.400, a, b) for a, b in section_rect(-0.026, 0.026, -0.030, 0.026, 0.008)]], "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=40)
    kit.cylindre(pc, ob.p(0.100, -0.044, -0.016), ob.p(0.362, -0.036, -0.014), 0.009, 8, mat="Metal")
    faces = kit.cylindre(pc, ob.p(0.120, -0.044, -0.016), ob.p(0.220, -0.042, -0.015), 0.015, 8, mat="Chassis")
    kit.chanfreiner(pc, 0.002, faces, 60)
    pc = rb.piece(f"Jambiere_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(0.080, 0.052), (0.100, 0.060), (0.210, 0.058), (0.320, 0.048), (0.360, 0.040)], JAMBIERE,
                 0.012, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    jambiere = Surface(pc)
    pc2 = rb.piece(f"Jambiere_Lueur_{cote}", ob.nom, "Lueur")
    plaque_sur(pc2, jambiere, [(0.130, -0.004), (0.290, -0.003), (0.290, 0.003), (0.130, 0.004)], ob.p(0.0), ob.d,
               ob.dir(148), -ob.dir(58), 0.0012, "Lueur", enfoncement=0.0005)
    pc = rb.piece(f"Tibia_Flanc_{cote}", ob.nom, "Blindage")
    coque_profil(pc, ob, [(0.065, 0.050), (0.150, 0.050), (0.200, 0.040)], FLANC_TIBIA, 0.008, "Blindage")
    kit.chanfreiner(pc, 0.002, angle_min=35)
    pc = rb.piece(f"Mollet_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(0.060, 0.054), (0.110, 0.062), (0.200, 0.058), (0.270, 0.044), (0.300, 0.030)], MOLLET, 0.011,
                 "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)


def section_botte(xi, xe, zh, s):
    """La section d'une botte en (x, z) : sept points, une arête au milieu du dessus."""
    m = (xi + xe) / 2
    pts = [(xi, 0.020), (xe, 0.020), (xe, zh - 0.026), (m + (xe - m) * 0.5, zh - 0.004), (m, zh + 0.006),
           (m - (m - xi) * 0.5, zh - 0.004), (xi, zh - 0.026)]
    return [(s * x, z) for x, z in pts]


def pied(rb, cote):
    s = 1 if cote == "Left" else -1
    o = rb.os(f"{cote}Foot", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Cheville_{cote}", o.nom, "Metal")
    tambour(pc, o.tete, X, 0.036, 0.040, n=12, mat="Metal")
    pc = rb.piece(f"Botte_{cote}", o.nom, "Blindage")
    loft_y(pc, [(0.094, 0.058, 0.126, 0.064), (0.068, 0.044, 0.142, 0.110), (0.008, 0.042, 0.150, 0.120),
                (-0.056, 0.042, 0.156, 0.094), (-0.111, 0.046, 0.158, 0.066)], "Blindage",
           lambda xi, xe, zh: section_botte(xi, xe, zh, s))
    kit.chanfreiner(pc, 0.004)
    # les gardes de cheville, à pans, de chaque côté
    for cote_x, x0, x1 in ((1, 0.150, 0.161), (-1, 0.040, 0.029)):
        poly = [(0.050, 0.064), (0.002, 0.060), (-0.022, 0.100), (0.016, 0.142), (0.062, 0.122)]
        kit.loft(pc, [[Vector((s * x0, y, z)) for y, z in poly], [Vector((s * x1, y, z)) for y, z in poly]], "Blindage")
    kit.chanfreiner(pc, 0.002)
    botte = Surface(pc)
    # le talon, une plaque peinte, l'écho du cou-de-pied
    pc = rb.piece(f"Talon_{cote}", o.nom, "Peinture")
    plaque_sur(pc, botte, [(s * 0.066, 0.030), (s * 0.120, 0.030), (s * 0.122, 0.064), (s * 0.093, 0.078), (s * 0.064, 0.064)],
               ORIGINE, X, Z, -Y, 0.008, "Peinture", enfoncement=0.003)
    kit.chanfreiner(pc, 0.0015)
    pc = rb.piece(f"Semelle_{cote}", o.nom, "Caoutchouc")
    loft_y(pc, [(0.100, 0.052, 0.130, 0.0), (0.072, 0.040, 0.146, 0.0), (-0.112, 0.039, 0.162, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.022), (s * xi, 0.022)])
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece(f"CouDePied_{cote}", o.nom, "Peinture")
    loft_y(pc, [(0.030, 0.056, 0.138, 0.123), (-0.020, 0.054, 0.144, 0.114), (-0.074, 0.056, 0.146, 0.089)], "Peinture",
           lambda xi, xe, zh: [(s * xi, zh - 0.030), (s * xe, zh - 0.030), (s * xe, zh - 0.004), (s * (xe - 0.022), zh + 0.008),
                               (s * (xi + 0.022), zh + 0.008), (s * xi, zh - 0.004)])
    kit.chanfreiner(pc, 0.002)
    # la pointe, en coin, sur l'os des orteils
    ot = rb.os(f"{cote}ToeBase", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Pointe_{cote}", ot.nom, "Blindage")
    loft_y(pc, [(-0.113, 0.047, 0.157, 0.066), (-0.162, 0.054, 0.152, 0.058), (-0.206, 0.074, 0.130, 0.036)], "Blindage",
           lambda xi, xe, zh: section_botte(xi, xe, zh, s))
    kit.chanfreiner(pc, 0.004)
    pc = rb.piece(f"Pointe_Semelle_{cote}", ot.nom, "Caoutchouc")
    loft_y(pc, [(-0.114, 0.040, 0.161, 0.0), (-0.208, 0.064, 0.140, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.022), (s * xi, 0.022)])
    kit.chanfreiner(pc, 0.003)


# --- le robot ---------------------------------------------------------------------------------------------

def corps(rb, variante=None):
    """Toutes les pièces du CT-01 ; variante : le bas du dos (VARIANTES, A par défaut)."""
    tete(rb)
    cou(rb)
    buste(rb)
    dos(rb)
    ventre(rb)
    VARIANTES[variante or "A"][1](rb)
    bassin(rb)
    for cote in ("Left", "Right"):
        bras(rb, cote)
        main_(rb, cote)
        jambe(rb, cote)
        pied(rb, cote)


MODELE = cr.Modele(CODE, PREFIXE, corps, VARIANTES, SKINS, (LARGEUR_ECRAN, BAS_ECRAN, HAUTEUR_ECRAN), ECARTS_VOULUS)

if __name__ == "__main__":
    RESULTAT = cr.principal(MODELE)
