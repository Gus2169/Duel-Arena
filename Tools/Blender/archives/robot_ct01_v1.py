"""Le robot CT-01 de Duel Arena (Blender 5.2, low poly) : un candidat au robot définitif (2026-10-26).

Inspiré de l'archétype de KAY/O (Valorant), à la demande de l'utilisateur, sans en reprendre le dessin
(le jeu sera vendu) : un robot de combat au visage d'écran, des plaques peintes sur une ossature sombre,
des articulations mécaniques visibles, des doigts articulés et une unité d'énergie dans le dos. Et ce que
la bible impose aux combattants de Duel Arena (§ 5, § 11) : un prototype de soldat en évaluation, aux
marquages de crash-test (les mires noir et blanc des tempes et des épaulières, son numéro), peint de la
couleur de son joueur, usé et rafistolé (une soudure en travers du plastron, une plaque d'apprêt rivetée
sur la cuisse gauche).

Pièces rigides sur le squelette du Y Bot (Mixamo) : chaque pièce suit un seul os, comme un robot ; des
tambours aux articulations cachent le jeu entre deux pièces. Le squelette, ses noms et sa pose de repos
(en T) sont ceux du Y Bot : Unity lui donne un avatar humanoïde et il joue toutes les animations du jeu.
Il tient dans la silhouette du Y Bot, mesurée os par os (VOLUMES_Y_BOT), donc dans les zones touchables
du jeu (bible § 5 : même silhouette pour tous) ; le script imprime ce qui en dépasse.

Les rendus de comparaison (bible § 10 et § 18, l'utilisateur tranche) :
- low poly propre : les matériaux usés du kit des armes (kit_lowpoly.materiau) ;
- cel-shading discret : des aplats en trois tons, un reflet franc, un contour ;
- les deux mêlés : l'usure du low poly sous l'éclairage en aplats, avec le contour.

  blender -b --factory-startup -P robot_ct01.py -- [--rendus dossier] [--vues a,b] [--fbx f.fbx]
                                                   [--sortie f.blend] [--couleur violet]

Repère : celui du Y Bot importé dans Blender (mètres, haut +Z, le robot regarde vers -Y, sa gauche en
+X). Les pièces sont construites dans ce repère, puis ramenées dans celui du squelette (l'objet Armature
du FBX de Mixamo, tourné de 90° et à l'échelle 0,01) : elles en sont les enfants, sans transformation,
comme les maillages du Y Bot, et l'export FBX vers Unity est celui de l'essai du Y Bot à facettes.
"""

import argparse
import importlib
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import kit_lowpoly as kit  # noqa: E402

importlib.reload(kit)
from kit_lowpoly import Piece, srgb  # noqa: E402

PROJET = os.path.normpath(os.path.join(DOSSIER, "..", ".."))
SOURCE = os.path.join(PROJET, "Assets", "_ProjectArt", "Mesh", "Personnages", "Y Bot.fbx")
ATTENTE = os.path.join(PROJET, "Assets", "_ProjectArt", "Animations", "MainsNues", "Y Bot@Idle.fbx")
POLICE = "C:/Windows/Fonts/bahnschrift.ttf"
CODE = "CT-01"
PREFIXE = "CT01"
NUMERO = "07"

# La palette des robots (PlayerPaint, dans l'ordre du préfab) ; le violet est celui de l'utilisateur.
PALETTE = {
    "orange": srgb(255, 106, 19), "bleu": srgb(30, 144, 255), "magenta": srgb(255, 45, 155),
    "vert": srgb(46, 224, 90), "cyan": srgb(0, 229, 229), "violet": srgb(143, 75, 255), "jaune": srgb(255, 210, 30),
}

# Le Y Bot, os par os (relevé le 2026-10-26 sur ses sommets, en m, repère ci-dessus) : la peau la plus
# éloignée de l'axe de chaque os. Le robot ne doit en sortir que de quelques millimètres. Le plastron,
# tenu par Spine2, couvre aussi la poitrine que le Y Bot donne à Spine1 : il est comparé à elle.
VOLUMES_Y_BOT = {
    "Head": 0.134, "Neck": 0.068, "Spine2": 0.215, "Spine1": 0.215, "Spine": 0.165, "Hips": 0.172,
    "LeftShoulder": 0.131, "LeftArm": 0.083, "LeftForeArm": 0.069, "LeftHand": 0.052,
    "LeftUpLeg": 0.124, "LeftLeg": 0.098, "LeftFoot": 0.112, "LeftToeBase": 0.069,
}
TOLERANCE_VOLUME = 0.016   # une épaulière, un tambour de hanche : un peu plus large que la peau du Y Bot

DOIGTS = ("Index", "Middle", "Ring", "Pinky")


# --- le squelette -------------------------------------------------------------------------------

class Os:
    """Un os du squelette au repos, en m dans le repère du monde, et un repère qui le suit :
    d le long de l'os, u et v deux directions données (projetées) ; p(s, a, b) = tête + d s + u a + v b."""

    def __init__(self, arm, nom, u=None, v=None):
        b = arm.data.bones[f"mixamorig:{nom}"]
        M = arm.matrix_world
        self.nom = b.name
        self.court = nom
        self.tete = M @ b.head_local
        self.queue = M @ b.tail_local
        self.L = (self.queue - self.tete).length
        self.d = (self.queue - self.tete).normalized()
        u = Vector((0, 0, 1)) if u is None else Vector(u)
        self.u = (u - self.d * u.dot(self.d)).normalized()
        v = self.d.cross(self.u) if v is None else Vector(v)
        v = v - self.d * v.dot(self.d) - self.u * v.dot(self.u)
        self.v = v.normalized()

    def p(self, s, a=0.0, b=0.0):
        return self.tete + self.d * s + self.u * a + self.v * b

    def dir(self, angle):
        """Direction dans le plan (u, v) : 0 = u, 90° = v."""
        r = math.radians(angle)
        return self.u * math.cos(r) + self.v * math.sin(r)


AVANT = Vector((0, -1, 0))
HAUT = Vector((0, 0, 1))


class Robot:
    """L'atelier du robot : le squelette, les matériaux, les pièces (une par os et par matériau à la
    fin), la police des marquages."""

    def __init__(self, scene, arm, M):
        self.scene = scene
        self.arm = arm
        self.M = M
        self.pieces = []            # (Piece, nom de l'os)
        self.rng = random.Random(7)
        self.police = bpy.data.fonts.load(POLICE, check_existing=True)

    def os(self, nom, u=None, v=None):
        return Os(self.arm, nom, u, v)

    def piece(self, nom, os_nom, mat=None):
        pc = Piece(f"{PREFIXE}_{nom}", mat)
        self.pieces.append((pc, os_nom))
        return pc

    def texte(self, pc, contenu, hauteur, centre, axe_x, axe_y, mat="Marquage", **kw):
        return kit.texte(pc, contenu, self.police, hauteur, centre, axe_x, axe_y, mat, scene=self.scene, **kw)


# --- primitives propres au robot -----------------------------------------------------------------

def coque(pc, o, stations, a0, a1, epaisseur, mat, k=9, centre=(0.0, 0.0)):
    """Une plaque cintrée autour d'un os : stations = [(s, rayon extérieur)], de l'angle a0 à a1 dans le
    plan (u, v) de l'os (0 = u, 90° = v), épaisse de epaisseur vers l'axe. Une coque fermée par ses bouts."""
    ca, cb = centre
    sections = []
    for s, r in stations:
        ext = [o.p(s, ca, cb) + o.dir(a0 + (a1 - a0) * i / (k - 1)) * r for i in range(k)]
        inte = [o.p(s, ca, cb) + o.dir(a1 - (a1 - a0) * i / (k - 1)) * (r - epaisseur) for i in range(k)]
        sections.append(ext + inte)
    return kit.loft(pc, sections, mat)


def tube(pc, o, stations, n=8, mat=None, phase=None, centre=(0.0, 0.0)):
    """Un tube à sections régulières le long d'un os : stations = [(s, rayon)]."""
    ca, cb = centre
    phase = math.pi / n if phase is None else phase
    sections = [[o.p(s, ca, cb) + o.dir(math.degrees(phase + 2 * math.pi * i / n)) * r for i in range(n)]
                for s, r in stations]
    return kit.loft(pc, sections, mat)


def tambour(pc, centre, axe, r, demi_largeur, n=12, mat=None):
    """Une articulation : un cylindre court d'axe donné, chanfreiné."""
    axe = axe.normalized()
    faces = kit.cylindre(pc, centre - axe * demi_largeur, centre + axe * demi_largeur, r, n, mat=mat)
    kit.chanfreiner(pc, min(0.004, r * 0.15), faces, angle_min=60.0)
    return faces


def section_torse(hw, f, b, quille=0.012):
    """La section du buste, en (x, y) : douze points, une arête au milieu du devant, deux pans de
    poitrine, des flancs, un dos plat ; toujours dans le même ordre pour les lofts."""
    return [(0.22 * hw, f), (0.64 * hw, f + quille), (hw, 0.32 * f), (hw, 0.40 * b), (0.66 * hw, b), (0.20 * hw, b),
            (-0.20 * hw, b), (-0.66 * hw, b), (-hw, 0.40 * b), (-hw, 0.32 * f), (-0.64 * hw, f + quille), (-0.22 * hw, f)]


def section_rect(x0, x1, y0, y1, ch):
    """Rectangle (x, y) à coins abattus, huit points dans le sens trigonométrique."""
    return [(x0 + ch, y0), (x1 - ch, y0), (x1, y0 + ch), (x1, y1 - ch), (x1 - ch, y1), (x0 + ch, y1), (x0, y1 - ch),
            (x0, y0 + ch)]


def loft_z(pc, stations, mat, section):
    """Loft vertical : stations = [(z, paramètres de la section)] ; section(*paramètres) -> [(x, y)]."""
    return kit.loft(pc, [[Vector((x, y, z)) for x, y in section(*p)] for z, *p in stations], mat)


def loft_y(pc, stations, mat, section):
    """Loft le long de Y (vers l'arrière) : stations = [(y, paramètres)] ; section(*p) -> [(x, z)]."""
    return kit.loft(pc, [[Vector((x, y, z)) for x, z in section(*p)] for y, *p in stations], mat)


def boite_os(pc, o, s0, s1, a0, a1, b0, b1, mat, ch=0.0):
    """Boîte alignée sur le repère d'un os : de s0 à s1 le long de l'os, a dans u, b dans v."""
    sec = section_rect(a0, a1, b0, b1, ch) if ch else [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    return kit.loft(pc, [[o.p(s0, a, b) for a, b in sec], [o.p(s1, a, b) for a, b in sec]], mat)


class Surface:
    """Pour poser un marquage sur une pièce déjà construite : un rayon vers elle donne le point et la
    normale de sa face."""

    def __init__(self, pc):
        pc.bm.normal_update()
        self.bvh = BVHTree.FromBMesh(pc.bm)

    def toucher(self, origine, direction):
        hit = self.bvh.ray_cast(origine, direction.normalized(), 1.0)
        if hit[0] is None:
            raise ValueError(f"rien touché depuis {tuple(round(c, 3) for c in origine)}")
        point, normale = hit[0], hit[1]
        if normale.dot(direction) > 0:
            normale = -normale
        return point, normale


def axes_decalque(normale, haut=HAUT):
    """Les axes d'un décalque lisible de l'extérieur : axe_y vers le haut donné, axe_x vers la droite
    de qui le regarde."""
    y = (haut - normale * haut.dot(normale)).normalized()
    x = (-normale).cross(y).normalized()
    return x, y


def mire_crash(pc, centre, normale, r):
    """La mire de crash-test : un disque en quartiers noir et blanc, cerclé de noir (bible § 5)."""
    kit.disque(pc, centre + normale * 0.0012, normale, r, 16, mats_quartiers=["MarquageNoir", "Marquage", "MarquageNoir", "Marquage"])
    kit.disque(pc, centre + normale * 0.0006, normale, r * 1.12, 16, mat="MarquageNoir")


# --- la tête ---------------------------------------------------------------------------------------

def tete(rb):
    o = rb.os("Head")
    # Le casque : un bloc à pans, plus étroit au menton, peint ; le visage est un écran.
    pc = rb.piece("Casque", o.nom, "Peinture")
    stations = [(1.578, 0.058, -0.078, 0.070, 0.022), (1.600, 0.073, -0.097, 0.088, 0.026),
                (1.630, 0.082, -0.105, 0.098, 0.028), (1.700, 0.085, -0.107, 0.104, 0.030),
                (1.746, 0.081, -0.103, 0.100, 0.030), (1.779, 0.067, -0.088, 0.088, 0.027),
                (1.797, 0.044, -0.062, 0.064, 0.019)]
    loft_z(pc, stations, "Peinture", lambda hw, f, b, ch: section_rect(-hw, hw, f, b, ch))
    kit.chanfreiner(pc, 0.003)
    casque = Surface(pc)

    # L'écran du visage dans son cadre, en saillie sur le devant du casque
    pc = rb.piece("Visage", o.nom, "Chassis")
    zc = 1.668
    ext = kit.contour_rect(0.142, 0.094, 0.016, 0.0, zc)
    inte = kit.contour_rect(0.122, 0.076, 0.010, 0.0, zc)
    y0, y1 = -0.084, -0.117
    boucles = [[Vector((x, y0, z)) for x, z in ext], [Vector((x, y1, z)) for x, z in ext],
               [Vector((x, y1 + 0.004, z)) for x, z in inte], [Vector((x, y0, z)) for x, z in inte]]
    kit.loft(pc, boucles, "Chassis", cyclique=True)
    kit.chanfreiner(pc, 0.0025)
    ecran = kit.contour_rect(0.124, 0.078, 0.010, 0.0, zc)
    kit.loft(pc, [[Vector((x, -0.100, z)) for x, z in ecran], [Vector((x, -0.1105, z)) for x, z in ecran]], "Ecran")
    # l'affichage : deux yeux en barres, un peu froncés, et les coins d'un viseur
    ya = -0.1108
    for s in (1, -1):
        c = Vector((s * 0.027, ya, zc + 0.006))
        a = math.radians(7 * s)   # le bout intérieur plus bas : un regard un peu froncé
        dx = Vector((math.cos(a), 0, math.sin(a)))
        dz = Vector((-math.sin(a), 0, math.cos(a)))
        coins = [c + dx * (sx * 0.0155) + dz * (sz * 0.0058) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        kit.quad(pc, coins, "Affichage", normale=AVANT)
    for sx in (1, -1):
        for sz in (1, -1):
            cx, cz = sx * 0.053, zc + sz * 0.030
            kit.quad(pc, [Vector((cx, ya, cz)), Vector((cx - sx * 0.010, ya, cz)), Vector((cx - sx * 0.010, ya, cz - sz * 0.0016)),
                          Vector((cx, ya, cz - sz * 0.0016))], "Affichage", normale=AVANT)
            kit.quad(pc, [Vector((cx, ya, cz)), Vector((cx, ya, cz - sz * 0.008)), Vector((cx - sx * 0.0016, ya, cz - sz * 0.008)),
                          Vector((cx - sx * 0.0016, ya, cz))], "Affichage", normale=AVANT)

    # La mâchoire : une grille sous l'écran
    pc = rb.piece("Machoire", o.nom, "Interieur")
    fond = section_rect(-0.040, 0.040, 1.586, 1.618, 0.006)
    kit.loft(pc, [[Vector((x, -0.078, z)) for x, z in fond], [Vector((x, -0.101, z)) for x, z in fond]], "Interieur")
    for i in range(3):
        z0 = 1.589 + i * 0.0095
        barre = section_rect(-0.038 + i * 0.002, 0.038 - i * 0.002, z0, z0 + 0.0055, 0.0015)
        kit.loft(pc, [[Vector((x, -0.099, z)) for x, z in barre], [Vector((x, -0.106 - i * 0.001, z)) for x, z in barre]], "Chassis")

    # Les tempes : un capteur rond de chaque côté, la mire de crash-test dessus
    pc = rb.piece("Tempes", o.nom, "Chassis")
    for s in (1, -1):
        c = Vector((s * 0.088, 0.004, 1.668))
        tambour(pc, c, Vector((1, 0, 0)), 0.037, 0.012, n=12, mat="Chassis")
        tambour(pc, c + Vector((s * 0.008, 0, 0)), Vector((1, 0, 0)), 0.029, 0.007, n=12, mat="Metal")
        mire_crash(pc, Vector((s * 0.1031, 0.004, 1.668)), Vector((s, 0, 0)), 0.022)
    # le capteur de la tempe droite, vers l'avant
    axe = Vector((-1.0, -0.45, 0.0)).normalized()
    p0 = Vector((-0.078, -0.040, 1.718))
    kit.cylindre(pc, p0, p0 + axe * 0.016, 0.0085, 10, mat="Chassis")
    kit.disque(pc, p0 + axe * 0.0163, axe, 0.0062, 10, mat="Lentille")

    # La crête : une arête sombre du front à la nuque
    pc = rb.piece("Crete", o.nom, "Chassis")
    chemin = [Vector((0, -0.097, 1.758)), Vector((0, -0.082, 1.787)), Vector((0, -0.058, 1.801)), Vector((0, 0.0, 1.802)),
              Vector((0, 0.060, 1.801)), Vector((0, 0.083, 1.788)), Vector((0, 0.098, 1.760))]
    chemin = kit.lisser_chemin(chemin, 3)
    kit.balayage(pc, chemin, [(-0.011, 0.004), (0.011, 0.004), (0.008, -0.007), (-0.008, -0.007)], "Chassis", normale=Vector((1, 0, 0)))

    # La nuque : une grille de refroidissement
    pc = rb.piece("Nuque", o.nom, "Chassis")
    kit.loft(pc, [[Vector((x, 0.088, z)) for x, z in section_rect(-0.042, 0.042, 1.632, 1.716, 0.008)],
                  [Vector((x, 0.110, z)) for x, z in section_rect(-0.040, 0.040, 1.634, 1.714, 0.008)]], "Chassis")
    for i in range(4):
        z = 1.642 + i * 0.020
        kit.loft(pc, [[Vector((x, 0.108, zz)) for x, zz in section_rect(-0.036, 0.036, z, z + 0.006, 0.001)],
                      [Vector((x, 0.118, zz)) for x, zz in section_rect(-0.036, 0.036, z, z + 0.006, 0.001)]], "Metal")

    # Marquages du casque : le numéro au-dessus de l'oreille gauche
    pc = rb.piece("Casque_Marquages", o.nom, "Marquage")
    point, normale = casque.toucher(Vector((0.3, 0.050, 1.735)), Vector((-1, 0, 0)))
    x, y = axes_decalque(normale)
    rb.texte(pc, NUMERO, 0.024, point, x, y, "MarquageNoir", decalage=0.0008)
    return casque


def cou(rb):
    o = rb.os("Neck")
    pc = rb.piece("Cou", o.nom, "Chassis")
    kit.cylindre(pc, Vector((0, 0.034, 1.486)), Vector((0, 0.010, 1.592)), 0.034, 10, mat="Interieur")
    for i, z in enumerate((1.512, 1.537, 1.562)):
        y = 0.034 - (z - 1.486) * 0.226
        faces = kit.cylindre(pc, Vector((0, y, z - 0.006)), Vector((0, y, z + 0.006)), 0.044, 12, mat="Metal")
        kit.chanfreiner(pc, 0.002, faces, 60)


# --- le buste ---------------------------------------------------------------------------------------

PLASTRON = [(1.300, 0.128, -0.118, 0.100), (1.340, 0.145, -0.135, 0.112), (1.400, 0.155, -0.145, 0.120),
            (1.450, 0.152, -0.139, 0.120), (1.490, 0.130, -0.114, 0.112), (1.517, 0.094, -0.078, 0.100)]


def buste(rb):
    o = rb.os("Spine2")
    pc = rb.piece("Plastron", o.nom, "Peinture")
    loft_z(pc, PLASTRON, "Peinture", section_torse)
    kit.chanfreiner(pc, 0.004)
    plastron = Surface(pc)

    # le col, une bague sombre autour du cou
    pc = rb.piece("Col", o.nom, "Chassis")
    c0, c1 = Vector((0, 0.030, 1.500)), Vector((0, 0.028, 1.532))
    boucles = [kit.cercle(c0, HAUT, 0.074, 12), kit.cercle(c1, HAUT, 0.064, 12), kit.cercle(c1, HAUT, 0.048, 12),
               kit.cercle(c0, HAUT, 0.050, 12)]
    kit.loft(pc, boucles, "Chassis", cyclique=True)
    kit.chanfreiner(pc, 0.003)

    # le sternum : une bande sombre en saillie au milieu du plastron
    pc = rb.piece("Sternum", o.nom, "Chassis")
    sections = []
    for z, hw, f, b in PLASTRON[:-1]:
        if z < 1.31:
            continue
        sections.append([Vector((x, y, z)) for x, y in [(-0.020, f + 0.006), (0.020, f + 0.006), (0.016, f - 0.006), (-0.016, f - 0.006)]])
    kit.loft(pc, sections, "Chassis")
    kit.chanfreiner(pc, 0.002)
    for z in (1.345, 1.395, 1.445):
        p, n = plastron.toucher(Vector((0, -0.3, z)), Vector((0, 1, 0)))
        kit.vis_six_pans(pc, p + n * 0.005, n, r=0.0045, h=0.003, mat="Metal")

    # deux évents au-dessus de la poitrine
    pc = rb.piece("Events", o.nom, "Interieur")
    for s in (1, -1):
        p, n = plastron.toucher(Vector((s * 0.085, -0.4, 1.462)), Vector((0, 1, 0)))
        x, y = axes_decalque(n)
        for i in range(3):
            c = p + y * (0.012 - i * 0.010) + n * 0.0008
            kit.quad(pc, [c + x * (-0.024) + y * (-0.0028), c + x * 0.024 + y * (-0.0028), c + x * 0.024 + y * 0.0028,
                          c + x * (-0.024) + y * 0.0028], "Interieur", normale=n)

    # Marquages : le numéro sur le pectoral droit, la mire de crash-test sur le gauche, la désignation
    pc = rb.piece("Plastron_Marquages", o.nom, "MarquageNoir")
    p, n = plastron.toucher(Vector((-0.075, -0.4, 1.392)), Vector((0, 1, 0)))
    x, y = axes_decalque(n)
    rb.texte(pc, NUMERO, 0.052, p, x, y, "MarquageNoir", decalage=0.0009)
    p, n = plastron.toucher(Vector((0.072, -0.4, 1.398)), Vector((0, 1, 0)))
    mire_crash(pc, p, n, 0.021)
    p, n = plastron.toucher(Vector((0.072, -0.4, 1.355)), Vector((0, 1, 0)))
    x, y = axes_decalque(n)
    rb.texte(pc, CODE, 0.011, p, x, y, "MarquageNoir", decalage=0.0009)

    # La soudure d'une vieille réparation, en travers du pectoral gauche, et ses deux rivets
    pc = rb.piece("Plastron_Soudure", o.nom, "Soudure")
    chemin = []
    for i in range(7):
        t = i / 6
        xx, zz = 0.030 + 0.085 * t, 1.330 + 0.045 * t - 0.012 * math.sin(math.pi * t)
        p, n = plastron.toucher(Vector((xx, -0.4, zz)), Vector((0, 1, 0)))
        chemin.append(p + n * 0.0006)
    kit.soudure(pc, chemin, r=0.0026, mat="Soudure", rng=random.Random(3), pas=0.0024)
    return plastron


def dos(rb):
    o = rb.os("Spine2")
    pc = rb.piece("Unite_Energie", o.nom, "Chassis")
    stations = [(1.300, -0.098, 0.098, 0.104, 0.170, 0.018), (1.470, -0.104, 0.104, 0.104, 0.182, 0.020),
                (1.492, -0.088, 0.088, 0.104, 0.170, 0.016)]
    loft_z(pc, stations, "Chassis", lambda x0, x1, y0, y1, ch: section_rect(x0, x1, y0, y1, ch))
    kit.chanfreiner(pc, 0.004)
    unite = Surface(pc)
    # les ailettes, en haut
    for i in range(3):
        z = 1.418 + i * 0.022
        faces = kit.loft(pc, [[Vector((x, y, z)) for x, y in section_rect(-0.090, 0.090, 0.170, 0.196, 0.003)],
                              [Vector((x, y, z + 0.008)) for x, y in section_rect(-0.090, 0.090, 0.170, 0.196, 0.003)]], "Metal")
    # deux cheminées sur le dessus
    for s in (1, -1):
        c0, c1 = Vector((s * 0.068, 0.150, 1.480)), Vector((s * 0.068, 0.155, 1.545))
        faces = kit.cylindre(pc, c0, c1, 0.016, 10, mat="Metal")
        kit.chanfreiner(pc, 0.002, faces, 60)
        kit.disque(pc, c1 + Vector((0, 0, 0.0004)), HAUT, 0.011, 10, mat="Interieur")
        kit.cylindre(pc, c0 + Vector((0, 0, 0.022)), c0 + Vector((0, 0, 0.030)), 0.019, 10, mat="Chassis")
    # la trappe de la batterie, peinte, rivetée
    pc2 = rb.piece("Trappe", o.nom, "Peinture")
    faces = kit.loft(pc2, [[Vector((x, 0.169, z)) for x, z in section_rect(-0.080, 0.080, 1.318, 1.405, 0.010)],
                          [Vector((x, 0.177, z)) for x, z in section_rect(-0.078, 0.078, 1.320, 1.403, 0.010)]], "Peinture")
    kit.chanfreiner(pc2, 0.002)
    for sx in (1, -1):
        for zz in (1.326, 1.397):
            kit.rivet(pc2, Vector((sx * 0.072, 0.1771, zz)), Vector((0, 1, 0)), r=0.0028, h=0.0016, mat="Metal")
    # le marquage de la trappe
    pc3 = rb.piece("Dos_Marquages", o.nom, "MarquageNoir")
    x, y = axes_decalque(Vector((0, 1, 0)))
    rb.texte(pc3, CODE, 0.024, Vector((0, 0.1772, 1.372)), x, y, "MarquageNoir", decalage=0.0009)
    rb.texte(pc3, "UNITÉ D'ESSAI · NE PAS OUVRIR", 0.0065, Vector((0, 0.1772, 1.343)), x, y, "MarquageNoir", decalage=0.0009)


VENTRE = [  # (os, z bas, z haut, demi-largeur, devant, dos)
    ("Spine1", 1.268, 1.316, 0.126, -0.114, 0.096),
    ("Spine1", 1.220, 1.268, 0.122, -0.110, 0.094),
    ("Spine", 1.172, 1.220, 0.119, -0.106, 0.092),
    ("Spine", 1.124, 1.172, 0.121, -0.104, 0.092),
]


def ventre(rb):
    o = rb.os("Spine1")
    pc = rb.piece("Colonne", o.nom, "Interieur")
    kit.cylindre(pc, Vector((0, 0.020, 1.100)), Vector((0, 0.028, 1.345)), 0.064, 12, mat="Interieur")
    for i, (os_nom, z0, z1, hw, f, b) in enumerate(VENTRE):
        pc = rb.piece(f"Ventre_{i + 1}", f"mixamorig:{os_nom}", "Chassis")
        loft_z(pc, [(z0, hw - 0.004, f + 0.004, b - 0.004), (z0 + 0.008, hw, f, b), (z1, hw + 0.002, f - 0.002, b)],
               "Chassis", lambda hw_, f_, b_: section_torse(hw_, f_, b_, 0.008))
        kit.chanfreiner(pc, 0.003)


def bassin(rb):
    o = rb.os("Hips")
    pc = rb.piece("Bassin", o.nom, "Chassis")
    loft_z(pc, [(0.902, 0.052, -0.062, 0.062), (0.930, 0.092, -0.086, 0.094), (0.990, 0.128, -0.100, 0.108),
                (1.050, 0.134, -0.104, 0.100)], "Chassis", lambda hw, f, b: section_torse(hw, f, b, 0.006))
    kit.chanfreiner(pc, 0.004)
    # la ceinture et sa boucle
    pc = rb.piece("Ceinture", o.nom, "Chassis")
    loft_z(pc, [(1.046, 0.140, -0.112, 0.106), (1.104, 0.137, -0.110, 0.104)], "Chassis",
           lambda hw, f, b: section_torse(hw, f, b, 0.008))
    kit.chanfreiner(pc, 0.003)
    faces = kit.loft(pc, [[Vector((x, -0.111, z)) for x, z in section_rect(-0.032, 0.032, 1.056, 1.096, 0.006)],
                          [Vector((x, -0.121, z)) for x, z in section_rect(-0.029, 0.029, 1.059, 1.093, 0.006)]], "Metal")
    # la coquille, sombre (peinte, elle se lisait comme un slip)
    pc = rb.piece("Coquille", o.nom, "Chassis")
    haut_ = section_rect(-0.054, 0.054, -0.121, -0.103, 0.006)
    bas_ = section_rect(-0.030, 0.030, -0.098, -0.082, 0.005)
    kit.loft(pc, [[Vector((x, y, 0.915)) for x, y in bas_], [Vector((x, y, 1.044)) for x, y in haut_]], "Chassis")
    kit.chanfreiner(pc, 0.003)
    # les tassettes, sur les hanches
    pc = rb.piece("Tassettes", o.nom, "Peinture")
    for s in (1, -1):
        haut_ = [(s * 0.138, -0.072), (s * 0.150, -0.072), (s * 0.150, 0.068), (s * 0.138, 0.068)]
        bas_ = [(s * 0.152, -0.064), (s * 0.165, -0.064), (s * 0.165, 0.060), (s * 0.152, 0.060)]
        kit.loft(pc, [[Vector((x, y, 0.938)) for x, y in bas_], [Vector((x, y, 1.044)) for x, y in haut_]], "Peinture")
    kit.chanfreiner(pc, 0.003)
    # le dos du bassin
    pc = rb.piece("Bassin_Dos", o.nom, "Chassis")
    kit.loft(pc, [[Vector((x, y, 0.935)) for x, y in section_rect(-0.090, 0.090, 0.098, 0.118, 0.006)],
                  [Vector((x, y, 1.040)) for x, y in section_rect(-0.100, 0.100, 0.100, 0.122, 0.006)]], "Chassis")
    kit.chanfreiner(pc, 0.003)


# --- les bras ---------------------------------------------------------------------------------------

def bras(rb, cote):
    s = 1 if cote == "Left" else -1
    clav = rb.os(f"{cote}Shoulder", u=HAUT, v=AVANT)
    o = rb.os(f"{cote}Arm", u=HAUT, v=AVANT)
    ob = rb.os(f"{cote}ForeArm", u=HAUT, v=AVANT)

    # le logement de l'épaule, entre le plastron et l'épaulière
    pc = rb.piece(f"Epaule_{cote}", clav.nom, "Chassis")
    sec = section_rect(-0.030, 0.046, -0.068, 0.056, 0.014)
    kit.loft(pc, [[clav.p(0.030, a, b) for a, b in sec], [clav.p(0.128, a, b) for a, b in section_rect(-0.034, 0.050, -0.072, 0.060, 0.016)]],
             "Chassis")
    kit.chanfreiner(pc, 0.003)

    # l'articulation de l'épaule
    pc = rb.piece(f"Bras_{cote}", o.nom, "Chassis")
    tambour(pc, o.p(0.010), o.d, 0.050, 0.024, n=12, mat="Metal")
    # le bras : une ossature sombre et un vérin devant
    tube(pc, o, [(0.060, 0.050), (0.170, 0.048), (0.262, 0.044)], 8, "Chassis")
    kit.chanfreiner(pc, 0.003, angle_min=60)
    kit.cylindre(pc, o.p(0.105, 0.0, 0.050), o.p(0.250, 0.0, 0.044), 0.010, 8, mat="Metal")
    kit.cylindre(pc, o.p(0.100, 0.0, 0.050), o.p(0.165, 0.0, 0.049), 0.015, 8, mat="Chassis")

    # l'épaulière, en deux lames : la grande peinte, la petite sombre
    pc = rb.piece(f"Epauliere_{cote}", o.nom, "Peinture")
    coque(pc, o, [(-0.022, 0.078), (0.025, 0.089), (0.085, 0.090), (0.125, 0.084), (0.140, 0.077)], -112, 104, 0.013,
          "Peinture", k=11, centre=(0.004, 0.0))
    kit.chanfreiner(pc, 0.003)
    epauliere = Surface(pc)
    pc2 = rb.piece(f"Epauliere_Lame_{cote}", o.nom, "Chassis")
    coque(pc2, o, [(0.118, 0.080), (0.150, 0.077), (0.175, 0.071)], -98, 92, 0.010, "Chassis", k=9, centre=(-0.002, 0.0))
    kit.chanfreiner(pc2, 0.0025)
    # la mire sur le dessus (tournée vers l'extérieur, bras baissé)
    pc3 = rb.piece(f"Epauliere_Marquages_{cote}", o.nom, "Marquage")
    p, n = epauliere.toucher(o.p(0.060, 0.30, 0.0), -o.u)
    mire_crash(pc3, p, n, 0.024)

    # le coude : un tambour d'axe vertical (la charnière, bras en croix) et une butée derrière
    pc = rb.piece(f"Coude_{cote}", ob.nom, "Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.040, 0.034, n=12, mat="Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.026, 0.040, n=10, mat="Metal")
    kit.loft(pc, [[ob.p(-0.020, a, b) for a, b in section_rect(-0.024, 0.024, -0.058, -0.030, 0.006)],
                  [ob.p(0.045, a, b) for a, b in section_rect(-0.022, 0.022, -0.050, -0.026, 0.006)]], "Chassis")

    # l'avant-bras : ossature, garde peint (sous la manchette du gant à gauche) et bague du poignet
    pc = rb.piece(f"AvantBras_{cote}", ob.nom, "Chassis")
    tube(pc, ob, [(0.030, 0.040), (0.150, 0.037), (0.262, 0.032)], 8, "Chassis")
    faces = kit.cylindre(pc, ob.p(0.248), ob.p(0.278), 0.036, 10, mat="Metal")
    kit.chanfreiner(pc, 0.002, faces, 60)
    pc = rb.piece(f"Garde_{cote}", ob.nom, "Peinture")
    tube(pc, ob, [(0.040, 0.053), (0.052, 0.058), (0.130, 0.056), (0.222, 0.047), (0.236, 0.042)], 8, "Peinture", phase=math.pi / 8)
    kit.chanfreiner(pc, 0.003)
    garde = Surface(pc)
    if s < 0:   # à droite, sur le dessus du garde (le côté du dos de la main) : des bâtons de victoires
        pc4 = rb.piece(f"Garde_Marquages_{cote}", ob.nom, "Marquage")
        for i in range(4):
            p, n = garde.toucher(ob.p(0.100 + i * 0.011, 0.25, 0.0), -ob.u)
            trait(pc4, p, n, ob.v, 0.022, 0.0026)
        p, n = garde.toucher(ob.p(0.1165, 0.25, 0.0), -ob.u)
        trait(pc4, p, n, (ob.d * 0.045 + ob.v * 0.024).normalized(), 0.052, 0.0026)


def trait(pc, centre, normale, direction, longueur, largeur, mat="Marquage"):
    """Un trait peint (un décalque) : un quad couché sur la face."""
    d = (direction - normale * direction.dot(normale)).normalized() * (longueur / 2)
    e = normale.cross(d).normalized() * (largeur / 2)
    c = centre + normale * 0.0008
    kit.quad(pc, [c - d - e, c + d - e, c + d + e, c - d + e], mat, normale=normale)


def main_(rb, cote):
    o = rb.os(f"{cote}Hand", u=HAUT, v=AVANT)
    pc = rb.piece(f"Paume_{cote}", o.nom, "Chassis")
    kit.loft(pc, [[o.p(0.004, a, b) for a, b in section_rect(-0.019, 0.019, -0.040, 0.030, 0.008)],
                  [o.p(0.050, a, b) for a, b in section_rect(-0.021, 0.021, -0.056, 0.040, 0.008)],
                  [o.p(0.114, a, b) for a, b in section_rect(-0.017, 0.017, -0.056, 0.040, 0.008)]], "Chassis")
    kit.chanfreiner(pc, 0.003)
    # le dos de la main, peint
    pc = rb.piece(f"DosMain_{cote}", o.nom, "Peinture")
    kit.loft(pc, [[o.p(0.016, a, b) for a, b in section_rect(0.016, 0.027, -0.044, 0.030, 0.004)],
                  [o.p(0.100, a, b) for a, b in section_rect(0.014, 0.025, -0.050, 0.036, 0.004)]], "Peinture")
    kit.chanfreiner(pc, 0.002)
    # les doigts : trois phalanges et leurs jointures
    for doigt in DOIGTS:
        largeur = 0.0145 if doigt == "Pinky" else 0.0165
        for k in (1, 2, 3):
            f = rb.os(f"{cote}Hand{doigt}{k}", u=HAUT, v=AVANT)
            pc = rb.piece(f"{doigt}{k}_{cote}", f.nom, "Chassis")
            fin = f.L + (0.010 if k == 3 else -0.002)
            ep = 0.0085 - 0.0005 * k
            kit.loft(pc, [[f.p(0.003, a, b) for a, b in section_rect(-ep, ep, -largeur / 2, largeur / 2, 0.003)],
                          [f.p(fin, a, b) for a, b in section_rect(-ep + (0.002 if k == 3 else 0), ep - (0.001 if k == 3 else 0),
                                                                    -largeur / 2 + 0.001, largeur / 2 - 0.001, 0.003)]], "Chassis")
            kit.cylindre(pc, f.p(0.0, 0.0, -largeur / 2 + 0.001), f.p(0.0, 0.0, largeur / 2 - 0.001), 0.0062, 8, mat="Metal")
    # le pouce
    for k in (1, 2, 3):
        f = rb.os(f"{cote}HandThumb{k}", u=HAUT, v=AVANT)
        pc = rb.piece(f"Thumb{k}_{cote}", f.nom, "Chassis")
        fin = f.L + (0.008 if k == 3 else -0.002)
        kit.loft(pc, [[f.p(0.003 if k > 1 else -0.006, a, b) for a, b in section_rect(-0.0095, 0.0095, -0.0095, 0.0095, 0.003)],
                      [f.p(fin, a, b) for a, b in section_rect(-0.0085, 0.0085, -0.0085, 0.0085, 0.003)]], "Chassis")
        if k > 1:
            kit.cylindre(pc, f.p(0.0, -0.0095, 0.0), f.p(0.0, 0.0095, 0.0), 0.0065, 8, mat="Metal")


# --- les jambes --------------------------------------------------------------------------------------

def jambe(rb, cote):
    s = 1 if cote == "Left" else -1
    dehors = Vector((s, 0, 0))
    o = rb.os(f"{cote}UpLeg", u=dehors, v=AVANT)
    ob = rb.os(f"{cote}Leg", u=dehors, v=AVANT)

    # la hanche : un tambour latéral
    pc = rb.piece(f"Hanche_{cote}", o.nom, "Chassis")
    tambour(pc, o.p(0.004), o.u, 0.062, 0.042, n=14, mat="Chassis")
    tambour(pc, o.p(0.004, 0.030), o.u, 0.044, 0.016, n=12, mat="Metal")
    # la cuisse : ossature et cuissard peint
    pc = rb.piece(f"Cuisse_{cote}", o.nom, "Chassis")
    tube(pc, o, [(0.050, 0.070), (0.220, 0.064), (0.385, 0.054)], 8, "Chassis")
    pc = rb.piece(f"Cuissard_{cote}", o.nom, "Peinture")
    coque(pc, o, [(0.070, 0.088), (0.100, 0.100), (0.220, 0.097), (0.320, 0.086), (0.348, 0.078)], 268, 465, 0.015, "Peinture", k=11)
    kit.chanfreiner(pc, 0.003)
    cuissard = Surface(pc)
    if s > 0:   # la pièce rapportée : une plaque d'apprêt rivetée sur le flanc de la cuisse gauche
        pc2 = rb.piece(f"Cuissard_Piece_{cote}", o.nom, "Appret")
        p, n = cuissard.toucher(o.p(0.205, 0.40, -0.010), -o.u)
        x, y = axes_decalque(n, -o.d)
        sec = kit.contour_rect(0.058, 0.074, 0.008)
        kit.loft(pc2, [[p + x * a + y * b + n * 0.0005 for a, b in sec], [p + x * a + y * b + n * 0.0040 for a, b in sec]], "Appret")
        kit.chanfreiner(pc2, 0.0012)
        for a, b in ((-0.022, -0.030), (0.022, -0.030), (0.022, 0.030), (-0.022, 0.030)):
            kit.rivet(pc2, p + x * a + y * b + n * 0.0040, n, r=0.0030, h=0.0016, mat="Metal")

    # le genou : tambour et genouillère peinte (elle suit le tibia)
    pc = rb.piece(f"Genou_{cote}", ob.nom, "Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.050, 0.044, n=14, mat="Chassis")
    tambour(pc, ob.p(0.0), ob.u, 0.034, 0.050, n=12, mat="Metal")
    pc = rb.piece(f"Genouillere_{cote}", ob.nom, "Peinture")
    coque(pc, ob, [(-0.050, 0.060), (-0.030, 0.074), (0.010, 0.082), (0.050, 0.074), (0.066, 0.062)], 35, 145, 0.014, "Peinture", k=9)
    kit.chanfreiner(pc, 0.003)
    # le tibia : ossature, jambière peinte, vérin du mollet
    pc = rb.piece(f"Tibia_{cote}", ob.nom, "Chassis")
    tube(pc, ob, [(0.060, 0.056), (0.250, 0.050), (0.400, 0.044)], 8, "Chassis")
    pc = rb.piece(f"Jambiere_{cote}", ob.nom, "Peinture")
    coque(pc, ob, [(0.075, 0.074), (0.095, 0.084), (0.210, 0.082), (0.330, 0.070), (0.360, 0.062)], 20, 160, 0.014, "Peinture", k=11)
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece(f"Verin_{cote}", ob.nom, "Metal")
    kit.cylindre(pc, ob.p(0.080, 0.0, -0.062), ob.p(0.370, 0.0, -0.058), 0.011, 8, mat="Metal")
    faces = kit.cylindre(pc, ob.p(0.075, 0.0, -0.062), ob.p(0.215, 0.0, -0.060), 0.021, 10, mat="Chassis")
    kit.chanfreiner(pc, 0.002, faces, 60)


def section_pied(xi, xe, zh, s):
    """La section d'une botte en (x, z) : six points, le dessus en dos d'âne."""
    m = (xi + xe) / 2
    pts = [(xi, 0.016), (xe, 0.016), (xe, zh - 0.022), (m + (xe - m) * 0.45, zh), (m - (m - xi) * 0.45, zh), (xi, zh - 0.022)]
    return [(s * x, z) for x, z in pts]


def pied(rb, cote):
    s = 1 if cote == "Left" else -1
    o = rb.os(f"{cote}Foot", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Cheville_{cote}", o.nom, "Chassis")
    tambour(pc, o.tete, Vector((1, 0, 0)), 0.036, 0.040, n=12, mat="Metal")
    pc = rb.piece(f"Botte_{cote}", o.nom, "Chassis")
    loft_y(pc, [(0.094, 0.054, 0.128, 0.070), (0.062, 0.044, 0.142, 0.110), (0.000, 0.042, 0.150, 0.118),
                (-0.060, 0.042, 0.155, 0.092), (-0.111, 0.046, 0.158, 0.066)], "Chassis",
           lambda xi, xe, zh: section_pied(xi, xe, zh, s))
    kit.chanfreiner(pc, 0.004)
    pc = rb.piece(f"Semelle_{cote}", o.nom, "Caoutchouc")
    loft_y(pc, [(0.098, 0.046, 0.134, 0.0), (0.070, 0.040, 0.146, 0.0), (-0.112, 0.039, 0.162, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.018), (s * xi, 0.018)])
    kit.chanfreiner(pc, 0.003)
    # le cou-de-pied, peint
    pc = rb.piece(f"CouDePied_{cote}", o.nom, "Peinture")
    loft_y(pc, [(0.028, 0.056, 0.138, 0.121), (-0.020, 0.054, 0.144, 0.112), (-0.072, 0.056, 0.146, 0.087)], "Peinture",
           lambda xi, xe, zh: [(s * xi, zh - 0.030), (s * xe, zh - 0.030), (s * xe, zh - 0.004), (s * (xe - 0.020), zh + 0.007),
                               (s * (xi + 0.020), zh + 0.007), (s * xi, zh - 0.004)])
    kit.chanfreiner(pc, 0.002)
    # la pointe, sur l'os des orteils
    ot = rb.os(f"{cote}ToeBase", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Pointe_{cote}", ot.nom, "Chassis")
    loft_y(pc, [(-0.113, 0.047, 0.157, 0.064), (-0.168, 0.050, 0.155, 0.056), (-0.207, 0.062, 0.142, 0.040)], "Chassis",
           lambda xi, xe, zh: section_pied(xi, xe, zh, s))
    kit.chanfreiner(pc, 0.004)
    pc = rb.piece(f"Pointe_Semelle_{cote}", ot.nom, "Caoutchouc")
    loft_y(pc, [(-0.114, 0.040, 0.161, 0.0), (-0.210, 0.058, 0.146, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.018), (s * xi, 0.018)])
    kit.chanfreiner(pc, 0.003)


# --- matériaux ----------------------------------------------------------------------------------------

def materiaux(couleur="violet"):
    acier_nu = srgb(150, 152, 156)
    M = {}

    def fabrique(nom, base, **kw):
        m = kit.materiau(nom, base, **kw)
        m["base_aplat"] = list(base)
        return m

    M["Peinture"] = fabrique(f"{PREFIXE}_Peinture_{couleur}", PALETTE[couleur], rug=0.5, dessous=acier_nu, rug_dessous=0.4,
                                 metal_dessous=0.6, bords=0.62, eclats=0.38, rayures=0.45, salete=0.5, graine=21)
    M["Chassis"] = fabrique(f"{PREFIXE}_Chassis", srgb(44, 47, 52), rug=0.55, dessous=acier_nu, rug_dessous=0.4,
                                metal_dessous=0.6, bords=0.5, eclats=0.2, rayures=0.18, salete=0.6, graine=22)
    M["Metal"] = fabrique(f"{PREFIXE}_Metal", srgb(112, 114, 118), rug=0.38, metal=1.0, dessous=srgb(170, 172, 176),
                              rug_dessous=0.25, bords=0.55, rayures=0.5, salete=0.65, graine=23)
    M["Interieur"] = fabrique(f"{PREFIXE}_Interieur", srgb(16, 16, 17), rug=0.8, metal=0.3, salete=0.0, variation=0.0)
    M["Caoutchouc"] = fabrique(f"{PREFIXE}_Caoutchouc", srgb(24, 24, 24), rug=0.9, dessous=srgb(50, 50, 50),
                                   rug_dessous=0.95, metal_dessous=0.0, bords=0.5, salete=0.5, graine=24)
    # un écran mat : vernis, il renvoyait la lumière de remplissage et le visage devenait gris
    M["Ecran"] = fabrique(f"{PREFIXE}_Ecran", srgb(9, 11, 13), rug=0.75, salete=0.0, variation=0.0)
    M["Affichage"] = fabrique(f"{PREFIXE}_Affichage", srgb(40, 38, 34), rug=0.4, salete=0.0, variation=0.0,
                                  emission=srgb(255, 246, 232), force_emission=9.0)
    M["Lentille"] = fabrique(f"{PREFIXE}_Lentille", srgb(6, 9, 16), rug=0.02, salete=0.0, variation=0.0, coat=1.0)
    M["Marquage"] = fabrique(f"{PREFIXE}_Marquage_Blanc", srgb(214, 210, 198), rug=0.62, dessous=srgb(44, 47, 52),
                                 rug_dessous=0.55, metal_dessous=0.0, bords=0.0, eclats=0.5, rayures=0.45, salete=0.3,
                                 graine=25, echelle_eclats=150.0)
    M["MarquageNoir"] = fabrique(f"{PREFIXE}_Marquage_Noir", srgb(20, 20, 21), rug=0.68, dessous=srgb(160, 160, 160),
                                     rug_dessous=0.6, metal_dessous=0.0, bords=0.0, eclats=0.45, salete=0.3, graine=26,
                                     echelle_eclats=150.0)
    M["Appret"] = fabrique(f"{PREFIXE}_Appret", srgb(118, 50, 38), rug=0.78, dessous=acier_nu, rug_dessous=0.4,
                               metal_dessous=0.6, bords=0.85, eclats=0.7, rayures=0.45, salete=0.7, graine=27)
    M["Soudure"] = fabrique(f"{PREFIXE}_Soudure", srgb(92, 88, 84), rug=0.62, metal=1.0, salete=0.9, variation=0.2, graine=28)
    return M


# --- assemblage sur le squelette ------------------------------------------------------------------------

def importer_squelette(scene):
    """Le squelette du Y Bot (son FBX, tel quel), sans ses maillages."""
    avant = set(bpy.data.objects)
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=SOURCE, automatic_bone_orientation=False, ignore_leaf_bones=False)
    nouveaux = [o for o in bpy.data.objects if o not in avant]
    arm = next(o for o in nouveaux if o.type == "ARMATURE")
    for o in nouveaux:
        if o != arm:
            donnees = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if isinstance(donnees, bpy.types.Mesh) and donnees.users == 0:
                bpy.data.meshes.remove(donnees)
    arm.name = f"{PREFIXE}_Squelette"
    arm.data.pose_position = "REST"
    return arm


def assembler(rb, col):
    """Chaque pièce devient un objet enfant du squelette, ramené dans son repère, lié à son os seul ;
    puis tout est réuni en un maillage (un seul SkinnedMeshRenderer dans Unity)."""
    inverse = rb.arm.matrix_world.inverted()
    objets = []
    for pc, os_nom in rb.pieces:
        bmesh.ops.transform(pc.bm, matrix=inverse, verts=pc.bm.verts)
        ob = kit.vers_objet(pc, col, rb.M, parent=rb.arm, angle_lisse=35.0)
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = Matrix.Identity(4)
        groupe = ob.vertex_groups.new(name=os_nom)
        groupe.add(range(len(ob.data.vertices)), 1.0, "REPLACE")
        objets.append(ob)
    vl = rb.scene.view_layers[0]
    actif = objets[0]
    with bpy.context.temp_override(active_object=actif, selected_editable_objects=objets, selected_objects=objets,
                                   object=actif, scene=rb.scene, view_layer=vl):
        bpy.ops.object.join()
    ob = actif
    ob.name = PREFIXE
    ob.data.name = PREFIXE
    mod = ob.modifiers.new("Squelette", "ARMATURE")
    mod.object = rb.arm
    mod.use_vertex_groups = True
    return ob


def verifier_volumes(arm, ob):
    """Ce qui sort de la silhouette du Y Bot : pour chaque os, la distance maximale à son axe."""
    M = arm.matrix_world
    groupes = {g.index: g.name for g in ob.vertex_groups}
    pires = {}
    Mo = ob.matrix_world
    axes = {b.name: (M @ b.head_local, M @ b.tail_local) for b in arm.data.bones}
    for v in ob.data.vertices:
        nom = groupes[v.groups[0].group]
        h, t = axes[nom]
        d = (t - h).normalized()
        p = Mo @ v.co
        s = (p - h).dot(d)
        r = ((p - h) - d * s).length
        if r > pires.get(nom, 0.0):
            pires[nom] = r
    print("  Volume par os (rayon maximal du robot / du Y Bot) :")
    depasse = False
    for nom, r in sorted(pires.items()):
        court = nom.replace("mixamorig:", "")
        ref = VOLUMES_Y_BOT.get(court.replace("Right", "Left"))
        if ref is None:
            continue
        marque = ""
        if r > ref + TOLERANCE_VOLUME:
            marque = "   <-- dépasse"
            depasse = True
        print(f"    {court:<16} {r:.3f} / {ref:.3f}{marque}")
    return not depasse


# --- les trois rendus --------------------------------------------------------------------------------------

def repere_usure(col):
    """Les matériaux du kit lisent la position de l'objet (en m) ; les maillages du robot sont dans le
    repère du squelette (en cm). Un repère neutre à l'origine du monde leur rend des mètres."""
    e = bpy.data.objects.get(f"{PREFIXE}_Repere_Usure") or bpy.data.objects.new(f"{PREFIXE}_Repere_Usure", None)
    if e.name not in col.objects:
        col.objects.link(e)
    return e


def ancrer(mat, repere):
    for n in mat.node_tree.nodes:
        if n.type == "TEX_COORD":
            n.object = repere


def _couleur_de(mat):
    """La couleur de base d'un matériau du kit : la prise reliée à Base Color (la peinture usée), ou sa
    valeur."""
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    entree = bsdf.inputs["Base Color"]
    return bsdf, entree.links[0].from_socket if entree.links else tuple(entree.default_value)


def aplats(mat_source, nom, usure):
    """Le cel-shading : la lumière reçue (Shader to RGB) en trois tons, un reflet franc et un liseré,
    rendus par une émission. usure : garde la couleur usée du matériau (les deux rendus mêlés) ; sinon
    la couleur de base, en aplat."""
    mat = mat_source.copy()
    mat.name = nom
    nt = mat.node_tree
    N, L = nt.nodes, nt.links
    bsdf, couleur = _couleur_de(mat)
    if not usure and "base_aplat" in mat_source:
        couleur = tuple(mat_source["base_aplat"])
    metal = bsdf.inputs["Metallic"].default_value
    emission_src = bsdf.inputs["Emission Color"]
    force = bsdf.inputs["Emission Strength"].default_value
    sortie = next(n for n in N if n.type == "OUTPUT_MATERIAL")

    diffus = N.new("ShaderNodeBsdfDiffuse")
    diffus.inputs["Color"].default_value = (1, 1, 1, 1)
    vers_rgb = N.new("ShaderNodeShaderToRGB")
    L.new(diffus.outputs["BSDF"], vers_rgb.inputs["Shader"])
    gris = N.new("ShaderNodeRGBToBW")
    L.new(vers_rgb.outputs["Color"], gris.inputs["Color"])
    rampe = N.new("ShaderNodeValToRGB")
    rampe.color_ramp.interpolation = "CONSTANT"
    el = rampe.color_ramp.elements
    el[0].position, el[0].color = 0.0, (0.34, 0.34, 0.36, 1)
    el[1].position, el[1].color = 0.10, (0.62, 0.62, 0.64, 1)
    e3 = el.new(0.36)
    e3.color = (1.0, 1.0, 1.0, 1)
    L.new(gris.outputs["Val"], rampe.inputs["Fac"])
    teinte = N.new("ShaderNodeMix")
    teinte.data_type = "RGBA"
    teinte.blend_type = "MULTIPLY"
    kit._par_id(teinte.inputs, "Factor_Float").default_value = 1.0
    if isinstance(couleur, bpy.types.NodeSocket):
        L.new(couleur, kit._par_id(teinte.inputs, "A_Color"))
    else:
        kit._par_id(teinte.inputs, "A_Color").default_value = couleur
    L.new(rampe.outputs["Color"], kit._par_id(teinte.inputs, "B_Color"))

    # le reflet : franc, plus fort sur le métal
    brillant = N.new("ShaderNodeBsdfGlossy")
    brillant.inputs["Roughness"].default_value = 0.28
    vers_rgb2 = N.new("ShaderNodeShaderToRGB")
    L.new(brillant.outputs["BSDF"], vers_rgb2.inputs["Shader"])
    gris2 = N.new("ShaderNodeRGBToBW")
    L.new(vers_rgb2.outputs["Color"], gris2.inputs["Color"])
    seuil = N.new("ShaderNodeMath")
    seuil.operation = "GREATER_THAN"
    L.new(gris2.outputs["Val"], seuil.inputs[0])
    seuil.inputs[1].default_value = 0.55
    reflet = N.new("ShaderNodeMath")
    reflet.operation = "MULTIPLY"
    L.new(seuil.outputs[0], reflet.inputs[0])
    reflet.inputs[1].default_value = 0.10 + 0.20 * metal
    # le liseré : les bords tournés vers la lumière d'en face
    poids = N.new("ShaderNodeLayerWeight")
    poids.inputs["Blend"].default_value = 0.35
    seuil3 = N.new("ShaderNodeMath")
    seuil3.operation = "GREATER_THAN"
    L.new(poids.outputs["Facing"], seuil3.inputs[0])
    seuil3.inputs[1].default_value = 0.78
    eclaire = N.new("ShaderNodeMath")
    eclaire.operation = "GREATER_THAN"
    L.new(gris.outputs["Val"], eclaire.inputs[0])
    eclaire.inputs[1].default_value = 0.10
    lisere = N.new("ShaderNodeMath")
    lisere.operation = "MULTIPLY"
    L.new(seuil3.outputs[0], lisere.inputs[0])
    L.new(eclaire.outputs[0], lisere.inputs[1])
    lisere2 = N.new("ShaderNodeMath")
    lisere2.operation = "MULTIPLY"
    L.new(lisere.outputs[0], lisere2.inputs[0])
    lisere2.inputs[1].default_value = 0.10
    somme = N.new("ShaderNodeMath")
    somme.operation = "ADD"
    L.new(reflet.outputs[0], somme.inputs[0])
    L.new(lisere2.outputs[0], somme.inputs[1])
    ajout = N.new("ShaderNodeMix")
    ajout.data_type = "RGBA"
    ajout.blend_type = "ADD"
    L.new(somme.outputs[0], kit._par_id(ajout.inputs, "Factor_Float"))
    L.new(kit._par_id(teinte.outputs, "Result_Color"), kit._par_id(ajout.inputs, "A_Color"))
    kit._par_id(ajout.inputs, "B_Color").default_value = (1, 1, 1, 1)

    emission = N.new("ShaderNodeEmission")
    L.new(kit._par_id(ajout.outputs, "Result_Color"), emission.inputs["Color"])
    emission.inputs["Strength"].default_value = 1.0
    final = emission.outputs["Emission"]
    if force > 0:   # l'affichage du visage reste lumineux
        lueur = N.new("ShaderNodeEmission")
        if emission_src.links:
            L.new(emission_src.links[0].from_socket, lueur.inputs["Color"])
        else:
            lueur.inputs["Color"].default_value = emission_src.default_value
        lueur.inputs["Strength"].default_value = force
        mel = N.new("ShaderNodeAddShader")
        L.new(final, mel.inputs[0])
        L.new(lueur.outputs["Emission"], mel.inputs[1])
        final = mel.outputs["Shader"]
    for lien in list(sortie.inputs["Surface"].links):
        L.remove(lien)
    L.new(final, sortie.inputs["Surface"])
    return mat


def contour(nom="CT01_Contour"):
    mat = bpy.data.materials.get(nom) or bpy.data.materials.new(nom)
    mat.use_backface_culling = True
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = srgb(12, 12, 14)
    nt.links.new(e.outputs["Emission"], sortie.inputs["Surface"])
    return mat


def ajouter_contour(ob, mat_contour, epaisseur_monde, echelle):
    """Le contour du cel-shading : la coque inversée (Solidify, normales retournées, faces arrière
    seules), épaisse de epaisseur_monde (m)."""
    ob.data.materials.append(mat_contour)
    mod = ob.modifiers.new("Contour", "SOLIDIFY")
    mod.thickness = epaisseur_monde / echelle
    mod.offset = 1.0
    mod.use_flip_normals = True
    mod.use_rim = False
    mod.material_offset = len(ob.data.materials) - 1
    mod.material_offset_rim = len(ob.data.materials) - 1
    return mod


def charger_attente():
    """L'action de l'attente à mains nues (le même squelette), pour poser le robot dans les rendus."""
    avant_obj = set(bpy.data.objects)
    avant_act = set(bpy.data.actions)
    scene = bpy.context.scene
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=ATTENTE, automatic_bone_orientation=False, ignore_leaf_bones=False)
    action = next(a for a in bpy.data.actions if a not in avant_act)
    action.use_fake_user = True
    for o in [o for o in bpy.data.objects if o not in avant_obj]:
        donnees = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if isinstance(donnees, bpy.types.Mesh) and donnees.users == 0:
            bpy.data.meshes.remove(donnees)
    return action


def poser(arm, action, image):
    arm.data.pose_position = "POSE"
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    if hasattr(arm.animation_data, "action_slot") and action.slots:
        arm.animation_data.action_slot = action.slots[0]


def copie(rb, ob, col, decalage, rotation_z, style, M_style, action, mat_contour):
    """Une copie du robot (squelette et maillage), posée, tournée, dans un style de rendu."""
    arm2 = rb.arm.copy()
    arm2.data = rb.arm.data
    col.objects.link(arm2)
    arm2.location = rb.arm.location + decalage
    arm2.rotation_euler = (rb.arm.rotation_euler.x, rb.arm.rotation_euler.y, rb.arm.rotation_euler.z + math.radians(rotation_z))
    poser(arm2, action, 1)
    me = ob.data.copy()
    ob2 = ob.copy()
    ob2.data = me
    ob2.name = f"{ob.name}_{style}"
    col.objects.link(ob2)
    ob2.parent = arm2
    ob2.matrix_parent_inverse = Matrix.Identity(4)
    ob2.modifiers["Squelette"].object = arm2
    ob2.hide_render = False
    ob2.hide_viewport = False
    arm2.hide_render = False
    for i, m in enumerate(me.materials):
        if m is not None and m.name in M_style:
            me.materials[i] = M_style[m.name]
    if mat_contour is not None:
        ajouter_contour(ob2, mat_contour, 0.0032, rb.arm.matrix_world.to_scale().x)
    return arm2, ob2


def etiquette(col, texte, position, taille=0.075):
    cu = bpy.data.curves.new(f"Etiquette_{texte}", "FONT")
    cu.body = texte
    cu.font = bpy.data.fonts.load(POLICE, check_existing=True)
    cu.size = taille
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new(f"Etiquette_{texte}", cu)
    col.objects.link(ob)
    ob.location = position
    ob.rotation_euler = (math.radians(90), 0, 0)
    mat = bpy.data.materials.get("CT01_Etiquette") or bpy.data.materials.new("CT01_Etiquette")
    nt = mat.node_tree
    nt.nodes.clear()
    s = nt.nodes.new("ShaderNodeOutputMaterial")
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = srgb(225, 226, 230)
    nt.links.new(e.outputs["Emission"], s.inputs["Surface"])
    cu.materials.append(mat)
    return ob


def decor(col, largeur=9.0):
    """Le décor de l'arène, sombre et terne (bible § 13) : un sol de béton, un mur."""
    sol_mat = kit.materiau("CT01_Decor_Sol", srgb(40, 42, 45), rug=0.85, variation=0.12, salete=0.0, graine=31)
    mur_mat = kit.materiau("CT01_Decor_Mur", srgb(58, 60, 64), rug=0.8, variation=0.10, salete=0.0, graine=32)
    for nom, coins, mat in (("Sol", [(-largeur, -14, 0), (largeur, -14, 0), (largeur, 3, 0), (-largeur, 3, 0)], sol_mat),
                            ("Mur", [(-largeur, 2.2, 0), (largeur, 2.2, 0), (largeur, 2.2, 4), (-largeur, 2.2, 4)], mur_mat)):
        me = bpy.data.meshes.new(f"CT01_Decor_{nom}")
        me.from_pydata([Vector(c) for c in coins], [], [(0, 1, 2, 3)])
        me.materials.append(mat)
        ob = bpy.data.objects.new(f"CT01_Decor_{nom}", me)
        col.objects.link(ob)


def lumieres(col, centre):
    """L'éclairage de l'arène : une clé chaude venue d'en haut à gauche (le soleil de l'arène), un
    remplissage froid et faible, un contre-jour qui détache la silhouette du mur."""
    soleil = bpy.data.lights.new("CT01_Soleil", "SUN")
    soleil.energy = 3.2
    soleil.color = (1.0, 0.957, 0.839)
    soleil.angle = math.radians(2.0)
    ob = bpy.data.objects.new("CT01_Soleil", soleil)
    col.objects.link(ob)
    ob.rotation_euler = (math.radians(42), 0, math.radians(-32))
    kit.lumiere("CT01_Remplissage", col, centre + Vector((-3.5, -4.0, 1.2)), centre, 900, 4.0, couleur=(0.78, 0.86, 1.0))
    kit.lumiere("CT01_Contre", col, centre + Vector((2.5, 3.0, 2.8)), centre, 1300, 2.0, couleur=(1.0, 0.95, 0.9))


def rendre(scene, cam, chemin, largeur, hauteur, echantillons=64):
    scene.camera = cam
    scene.render.resolution_x = largeur
    scene.render.resolution_y = hauteur
    scene.render.resolution_percentage = 100
    scene.render.filepath = chemin
    scene.render.image_settings.file_format = "PNG"
    scene.eevee.taa_render_samples = echantillons
    bpy.ops.render.render(write_still=True, scene=scene.name)
    print("RENDU", chemin)


# --- lancement ---------------------------------------------------------------------------------------------

def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(globals().get("ARGS") or [])
    p = argparse.ArgumentParser()
    p.add_argument("--rendus", default="")
    p.add_argument("--vues", default="")
    p.add_argument("--fbx", default="")
    p.add_argument("--sortie", default="")
    p.add_argument("--couleur", default="violet", choices=list(PALETTE))
    return p.parse_args(brut)


def construire(scene, couleur):
    col = kit.collection(f"{CODE} — Robot", scene.collection)
    arm = importer_squelette(scene)
    for c in arm.users_collection:
        c.objects.unlink(arm)
    col.objects.link(arm)
    M = materiaux(couleur)
    rb = Robot(scene, arm, M)
    tete(rb)
    cou(rb)
    buste(rb)
    dos(rb)
    ventre(rb)
    bassin(rb)
    for cote in ("Left", "Right"):
        bras(rb, cote)
        main_(rb, cote)
        jambe(rb, cote)
        pied(rb, cote)
    n_pieces = len(rb.pieces)
    ob = assembler(rb, col)
    print(f"  {CODE} : {n_pieces} pièces, {kit.triangles(ob)} triangles, {len(ob.data.materials)} matériaux")
    verifier_volumes(arm, ob)
    return rb, ob, col


def exporter_fbx(rb, ob, chemin):
    chemin = os.path.abspath(chemin)
    os.makedirs(os.path.dirname(chemin), exist_ok=True)
    vl = rb.scene.view_layers[0]
    for o in vl.objects:
        o.select_set(False)
    rb.arm.select_set(True)
    ob.select_set(True)
    with bpy.context.temp_override(scene=rb.scene, view_layer=vl, active_object=ob, selected_objects=[rb.arm, ob]):
        bpy.ops.export_scene.fbx(filepath=chemin, use_selection=True, object_types={"ARMATURE", "MESH"},
                                 add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True, use_triangles=True,
                                 mesh_smooth_type="OFF", use_armature_deform_only=False,
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                                 path_mode="STRIP", embed_textures=False)
    print("EXPORT", chemin)


def principal():
    args = arguments()
    scene = bpy.data.scenes.get(f"Robot {CODE}") or bpy.data.scenes.new(f"Robot {CODE}")
    if bpy.app.background:
        usine = bpy.data.scenes.get("Scene")
        if usine is not None and usine != scene:
            for o in list(usine.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.scenes.remove(usine)
    if bpy.context.window_manager.windows:
        bpy.context.window_manager.windows[0].scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    rb, ob, col = construire(scene, args.couleur)
    if args.fbx:
        exporter_fbx(rb, ob, args.fbx)
    if args.rendus:
        rendus(rb, ob, col, args)
    if args.sortie:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.sortie), copy=True)
    return rb, ob


def rendus(rb, ob, col_robot, args):
    scene = rb.scene
    os.makedirs(args.rendus, exist_ok=True)
    monde = bpy.data.worlds.get("CT01_Monde") or bpy.data.worlds.new("CT01_Monde")
    fond = monde.node_tree.nodes.get("Background")
    if fond is not None:
        fond.inputs["Color"].default_value = srgb(20, 21, 23)
        fond.inputs["Strength"].default_value = 0.6
    scene.world = monde
    scene.view_settings.view_transform = "AgX"
    col = kit.collection("Rendus", scene.collection)
    repere = repere_usure(col)
    lowpoly = {}
    for m in set(rb.M.values()):
        ancrer(m, repere)
        lowpoly[m.name] = m
    cel = {m.name: aplats(m, m.name + "_Cel", usure=False) for m in rb.M.values()}
    meles = {m.name: aplats(m, m.name + "_Meles", usure=True) for m in rb.M.values()}
    for m in list(cel.values()) + list(meles.values()):
        ancrer(m, repere)
    mat_contour = contour()
    action = charger_attente()
    ob.hide_render = True
    ob.hide_viewport = True
    rb.arm.hide_render = True
    decor(col)
    lumieres(col, Vector((0, 0, 1.0)))
    scene.frame_set(1)
    vues = [v for v in args.vues.split(",") if v] or ["comparaison", "planche", "bustes", "couleurs"]

    styles = [("lowpoly", "LOW POLY PROPRE", lowpoly, None), ("cel", "CEL-SHADING", cel, mat_contour),
              ("meles", "LES DEUX MÊLÉS", meles, mat_contour)]
    copies = {}

    def vider_copies():
        for arm2, ob2, etq in copies.values():
            for o in (ob2, arm2, etq):
                if o is not None:
                    bpy.data.objects.remove(o, do_unlink=True)
        copies.clear()

    if "comparaison" in vues or "bustes" in vues:
        for i, (cle, titre, M_style, mc) in enumerate(styles):
            x = (i - 1) * 1.1
            arm2, ob2 = copie(rb, ob, col, Vector((x, 0, 0)), -20, cle, M_style, action, mc)
            etq = etiquette(col, titre, Vector((x, -0.42, 0.004)), 0.07)
            etq.rotation_euler = (math.radians(62), 0, 0)
            copies[cle] = (arm2, ob2, etq)
        scene.frame_set(1)
        if "comparaison" in vues:
            cam = kit.camera("CT01_Cam_Comparaison", col, Vector((0.0, -6.3, 1.25)), Vector((0.0, 0, 0.88)), 50)
            rendre(scene, cam, os.path.join(args.rendus, "comparaison.png"), 2400, 1350)
        if "bustes" in vues:
            for i, (cle, _, _, _) in enumerate(styles):   # plus serrés : on cadre les bustes
                arm2, ob2, etq = copies[cle]
                arm2.location.x = (i - 1) * 0.82
                etq.hide_render = True
            scene.frame_set(1)
            cam2 = kit.camera("CT01_Cam_Bustes", col, Vector((0.0, -3.6, 1.50)), Vector((0.0, 0, 1.42)), 50)
            rendre(scene, cam2, os.path.join(args.rendus, "bustes.png"), 2400, 1100)
        vider_copies()
    if "planche" in vues:
        for i, angle in enumerate((0, -45, -90, 180)):
            x = (i - 1.5) * 0.95
            arm2, ob2 = copie(rb, ob, col, Vector((x, 0, 0)), angle, f"planche{i}", lowpoly, action, None)
            copies[f"planche{i}"] = (arm2, ob2, None)
        scene.frame_set(1)
        cam = kit.camera("CT01_Cam_Planche", col, Vector((0, -9.0, 0.95)), Vector((0, 0, 0.95)), 50, ortho=4.4)
        rendre(scene, cam, os.path.join(args.rendus, "planche.png"), 2400, 1400)
        vider_copies()
    if "couleurs" in vues:
        for i, nom in enumerate(PALETTE):
            m = kit.materiau(f"{PREFIXE}_Peinture_{nom}", PALETTE[nom], rug=0.5, dessous=srgb(150, 152, 156), rug_dessous=0.4,
                             metal_dessous=0.6, bords=0.62, eclats=0.38, rayures=0.45, salete=0.5, graine=21)
            ancrer(m, repere)
            M_couleur = dict(lowpoly)
            M_couleur[rb.M["Peinture"].name] = m
            x = (i - 3) * 0.78
            arm2, ob2 = copie(rb, ob, col, Vector((x, 0.35 * abs(i - 3), 0)), -18 + (i - 3) * -4, f"couleur{i}", M_couleur, action, None)
            copies[f"couleur{i}"] = (arm2, ob2, None)
        scene.frame_set(1)
        cam = kit.camera("CT01_Cam_Couleurs", col, Vector((0.4, -8.2, 1.5)), Vector((0, 0.4, 0.9)), 45)
        rendre(scene, cam, os.path.join(args.rendus, "couleurs.png"), 2400, 1100)
        vider_copies()


RESULTAT = principal()
