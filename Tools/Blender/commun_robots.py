"""Ce que partagent les robots de Duel Arena dans Blender (2026-10-26, sorti de robot_ct01.py quand un second
robot est arrivé) : le squelette du Y Bot et un repère par os, l'atelier (les pièces, une par os, et ce qu'un skin
en garde), les primitives à pans, les matériaux, l'assemblage en un seul maillage lié au squelette, les contrôles
(la peau du Y Bot, la surface touchable du jeu), les rendus et l'export vers Unity.

Un robot est un script qui décrit son corps et le confie à principal(Modele(...)) : robot_ct01.py (le CT-01),
robot_os02.py (l'OS-02). Les deux ont la même silhouette et les mêmes zones touchables (bible § 5).
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
ATTENTE_FUSIL = os.path.join(PROJET, "Assets", "_ProjectArt", "Animations", "Ch35_nonPBR@Rifle Idle.fbx")
ARMES_BLEND = os.path.join(DOSSIER, "Armes.blend")
POLICE = "C:/Windows/Fonts/bahnschrift.ttf"
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
TOLERANCE_VOLUME = 0.016   # un tambour de hanche, une plaque : un peu plus large que la peau du Y Bot
# La surface touchable du jeu, debout (Player.prefab : PlayerHitbox, l'œil de PlayerLocomotion), dans le repère
# du robot dans Blender (le z de Unity est le -y de Blender) : la tête, une sphère sur l'œil ; le torse et les
# jambes, des capsules. Les bras n'en font pas partie.
TETE_TOUCHABLE = (Vector((0, 0, 1.65)), Vector((0, 0, 1.65)), 0.14)
TORSE_TOUCHABLE = (Vector((0, 0.02, 1.17)), Vector((0, 0.03, 1.30)), 0.23)
JAMBES_TOUCHABLES = (Vector((0, 0, 0.20)), Vector((0, 0, 0.80)), 0.20)

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
    fin), la police des marquages ; et ce que son skin en garde (genres : les genres de pièces gardés,
    None pour tous) et refait dans un autre matériau (remplacements : {pièce : {matériau : autre}})."""

    def __init__(self, scene, arm, M, prefixe, genres=None, remplacements=None):
        self.scene = scene
        self.prefixe = prefixe
        self.arm = arm
        self.M = M
        self.pieces = []            # (Piece, nom de l'os)
        self.rng = random.Random(7)
        self.police = bpy.data.fonts.load(POLICE, check_existing=True)
        self.genres = genres
        self.remplacements = remplacements or {}

    def os(self, nom, u=None, v=None):
        return Os(self.arm, nom, u, v)

    def piece(self, nom, os_nom, mat=None, genre=None):
        """genre : « marquage » (les décalques : numéros, mires, désignations, bâtons de victoires) ou
        « rafistolage » (soudure, pièce rapportée), que le skin garde ou non."""
        pc = Piece(f"{self.prefixe}_{nom}", mat)
        pc.genre = genre
        self.pieces.append((pc, os_nom))
        return pc

    def surface(self, nom):
        """La surface d'une pièce déjà construite, et son os : de quoi y poser une livrée."""
        pc, os_nom = next((pc, o) for pc, o in self.pieces if pc.nom == f"{self.prefixe}_{nom}")
        return Surface(pc), os_nom

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


# --- primitives à pans (v2) -----------------------------------------------------------------------

def coque_profil(pc, o, stations, profil, epaisseur, mat, centre=(0.0, 0.0)):
    """Une plaque à pans autour d'un os : profil = [(angle, facteur)], son contour extérieur d'un bord à
    l'autre dans le plan (u, v) de l'os (une arête là où le facteur culmine) ; stations = [(s, rayon)] ;
    épaisse de epaisseur vers l'axe. Fermée par ses bouts."""
    ca, cb = centre
    sections = []
    for s, r in stations:
        c = o.p(s, ca, cb)
        ext = [c + o.dir(a) * (r * f) for a, f in profil]
        inte = [c + o.dir(a) * (r * f - epaisseur) for a, f in reversed(profil)]
        sections.append(ext + inte)
    return kit.loft(pc, sections, mat)


def tube_profil(pc, o, stations, profil, mat, centre=(0.0, 0.0)):
    """Un tube fermé à pans le long d'un os : profil = [(angle, facteur)] sur le tour complet."""
    ca, cb = centre
    return kit.loft(pc, [[o.p(s, ca, cb) + o.dir(a) * (r * f) for a, f in profil] for s, r in stations], mat)


def plaque_sur(pc, surface, contour, origine, ex, ey, direction, saillie, mat, enfoncement=0.004, anneaux=(0.62, 0.28)):
    """Une plaque posée sur une pièce déjà construite, qui en épouse la courbure : le contour [(a, b)] du plan
    (origine, ex, ey), des anneaux qui se resserrent vers son centre, et le centre, sont projetés le long de
    direction sur la surface ; la plaque monte de saillie au-dessus (et s'enfonce d'enfoncement dessous) le
    long de la normale de chaque point touché. Tendue entre ses seuls bords, une plaque posée sur une surface
    bombée se faisait traverser en son milieu (les pectoraux sortaient sombres, cerclés de peinture)."""
    ca = sum(a for a, _ in contour) / len(contour)
    cb = sum(b for _, b in contour) / len(contour)
    cercles = [list(contour)] + [[(ca + (a - ca) * k, cb + (b - cb) * k) for a, b in contour] for k in anneaux]

    def projeter(a, b):
        return surface.toucher(origine + ex * a + ey * b - direction * 0.35, direction)

    return plaque_projetee(pc, ([projeter(a, b) for a, b in cercle] for cercle in cercles), lambda: projeter(ca, cb),
                           saillie, mat, enfoncement)


def plaque_radiale(pc, surface, contour, centre, saillie, mat, enfoncement=0.004, anneaux=(0.8, 0.6, 0.4, 0.2),
                   pas=0.012):
    """plaque_sur sur une pièce bombée de tous côtés (une voûte de crâne) : le contour [Vector], à peu près
    sur la surface, redécoupé tous les `pas` au plus, ses anneaux et son milieu sont projetés depuis centre, au
    cœur de la pièce, sur la surface, et la plaque monte le long de ces rayons (les normales d'une voûte à
    pans la cassaient à chaque arête) ; une plaque peut ainsi en faire le tour, du dessus jusqu'aux flancs.
    Des mailles serrées : sur une voûte de 8 cm de rayon, une face de plus de 4 cm passe sous la surface."""
    fin = []
    for a, b in zip(contour, contour[1:] + contour[:1]):
        n = max(1, math.ceil((b - a).length / pas))
        fin.extend(a.lerp(b, i / n) for i in range(n))
    milieu = sum(fin, Vector()) / len(fin)
    cercles = [fin] + [[milieu + (p - milieu) * k for p in fin] for k in anneaux]

    def projeter(p):
        d = (p - centre).normalized()
        return surface.toucher(centre + d * 0.35, -d)[0], d

    return plaque_projetee(pc, ([projeter(p) for p in cercle] for cercle in cercles), lambda: projeter(milieu),
                           saillie, mat, enfoncement)


def plaque_projetee(pc, cercles, milieu, saillie, mat, enfoncement):
    """La plaque de plaque_sur et plaque_radiale, une fois ses points projetés : cercles = des anneaux de
    (point, normale), du bord vers le milieu ; milieu() = le (point, normale) du centre."""
    bm = pc.bm
    indice = pc.mat(mat) if isinstance(mat, str) else (mat or 0)
    dehors, dedans = [], []
    for points in cercles:
        dehors.append([bm.verts.new(q + nq * saillie) for q, nq in points])
        dedans.append([bm.verts.new(q - nq * enfoncement) for q, nq in points])
    q, nq = milieu()
    centres = (bm.verts.new(q + nq * saillie), bm.verts.new(q - nq * enfoncement))
    faces = []
    m = len(dehors[0])
    for couche, centre in zip((dehors, dedans), centres):
        for r in range(len(couche) - 1):
            for k in range(m):
                faces.append(bm.faces.new((couche[r][k], couche[r][(k + 1) % m], couche[r + 1][(k + 1) % m], couche[r + 1][k])))
        for k in range(m):
            faces.append(bm.faces.new((couche[-1][k], couche[-1][(k + 1) % m], centre)))
    for k in range(m):
        faces.append(bm.faces.new((dehors[0][k], dedans[0][k], dedans[0][(k + 1) % m], dehors[0][(k + 1) % m])))
    for f in faces:
        f.material_index = indice
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
ORIGINE = Vector((0, 0, 0))


def face_avant(pc, surface, contour, saillie, mat, enfoncement=0.004):
    """plaque_sur vue de face : le contour en (x, z), projeté vers l'arrière (+Y)."""
    return plaque_sur(pc, surface, contour, ORIGINE, X, Z, Y, saillie, mat, enfoncement)


# --- primitives du robot (suite) ---------------------------------------------------------------------

def trait(pc, centre, normale, direction, longueur, largeur, mat="Marquage"):
    """Un trait peint (un décalque) : un quad couché sur la face."""
    d = (direction - normale * direction.dot(normale)).normalized() * (longueur / 2)
    e = normale.cross(d).normalized() * (largeur / 2)
    c = centre + normale * 0.0008
    kit.quad(pc, [c - d - e, c + d - e, c + d + e, c - d + e], mat, normale=normale)


def frange(a, b, pas):
    return [a + i * pas for i in range(int(round((b - a) / pas)) + 1)]


def ruban(pc, surface, chemin, direction, largeur, mat, decalage=0.0012):
    """Une bande peinte qui épouse une surface (un décalque) : les points de chemin (du monde) sont projetés le
    long de direction ; une face par tronçon, decalage au-dessus de la surface, large de largeur."""
    pts = [surface.toucher(q - direction * 0.35, direction) for q in chemin]
    bm = pc.bm
    i = pc.mat(mat)
    bords = []
    for k, (p, n) in enumerate(pts):
        t = (pts[min(k + 1, len(pts) - 1)][0] - pts[max(k - 1, 0)][0]).normalized()
        lat = n.cross(t).normalized() * (largeur / 2)
        c = p + n * decalage
        bords.append((bm.verts.new(c + lat), bm.verts.new(c - lat)))
    for k in range(len(pts) - 1):
        f = bm.faces.new((bords[k][0], bords[k][1], bords[k + 1][1], bords[k + 1][0]))
        f.material_index = i
        f.normal_update()
        if f.normal.dot(pts[k][1]) < 0:
            f.normal_flip()


def section_lame(aw, b0, h, t, ridge=0.005):
    """La section d'une lame posée devant un os, en (a, b) : deux faces planes qui se rejoignent en une arête
    (h au-dessus du dos de la lame, un peu vers le dehors), épaisse de t ; b0 : le dos de la lame devant l'axe."""
    return [(aw, b0 + 0.004), (ridge, b0 + h), (-0.92 * aw, b0 + 0.002), (-0.92 * aw + 0.004, b0 - t),
            (ridge, b0 + h - t), (aw - 0.004, b0 - t + 0.002)]


def section_lame_dehors(bw, a0, h, t, ridge=-0.006):
    """La même lame, posée sur le flanc dehors d'un os : l'arête vers le dehors (+a)."""
    return [(a0 + 0.004, bw), (a0 + h, ridge), (a0 + 0.002, -0.92 * bw), (a0 - t, -0.92 * bw + 0.004),
            (a0 + h - t, ridge), (a0 - t + 0.002, bw - 0.004)]


def assombrir(c, k):
    return tuple(x * k for x in c[:3]) + (1.0,)


APPRET = srgb(118, 50, 38)
FAMILLES = ("peinture", "blindage", "chassis", "metal", "chrome", "caoutchouc", "marquage", "appret", "soudure")
PROPRE = {f: dict(bords=0.0, eclats=0.0, rayures=0.0, salete=0.0, variation=0.02) for f in FAMILLES}

# --- matériaux ----------------------------------------------------------------------------------------

def materiaux(prefixe, couleur="violet", usure=None, suffixe=""):
    """Les matériaux d'un robot, tous préfixés de son code ; usure : les réglages d'un skin, famille par famille,
    par-dessus ceux d'origine (une valeur peut être une fonction de la couleur du joueur) ; suffixe : au nom de
    chaque matériau (plusieurs skins dans une scène : sinon le dernier construit écrase les autres)."""
    acier_nu = srgb(150, 152, 156)
    usure = usure or {}
    M = {}

    def fabrique(nom, base, famille=None, **kw):
        kw.update({k: (v(PALETTE[couleur]) if callable(v) else v) for k, v in usure.get(famille, {}).items()})
        m = kit.materiau(nom + suffixe, base, **kw)
        m["base_aplat"] = list(base)
        return m

    M["Peinture"] = fabrique(f"{prefixe}_Peinture_{couleur}", PALETTE[couleur], "peinture", rug=0.5, dessous=acier_nu, rug_dessous=0.4,
                             metal_dessous=0.6, bords=0.62, eclats=0.38, rayures=0.45, salete=0.5, graine=21)
    M["Blindage"] = fabrique(f"{prefixe}_Blindage", srgb(62, 66, 72), "blindage", rug=0.46, metal=0.35, dessous=srgb(160, 162, 166),
                             rug_dessous=0.35, metal_dessous=0.8, bords=0.6, eclats=0.28, rayures=0.3, salete=0.55, graine=29)
    M["Chassis"] = fabrique(f"{prefixe}_Chassis", srgb(34, 36, 40), "chassis", rug=0.55, dessous=acier_nu, rug_dessous=0.4,
                            metal_dessous=0.6, bords=0.5, eclats=0.2, rayures=0.18, salete=0.6, graine=22)
    M["Metal"] = fabrique(f"{prefixe}_Metal", srgb(112, 114, 118), "metal", rug=0.38, metal=1.0, dessous=srgb(170, 172, 176),
                          rug_dessous=0.25, bords=0.55, rayures=0.5, salete=0.65, graine=23)
    # l'acier poli d'une ossature (OS-02) : clair, très métallique, ses arêtes polies par l'usage
    # (pas tout à fait métallique : un acier poli reflète le décor sombre de l'arène et paraissait noir)
    M["Chrome"] = fabrique(f"{prefixe}_Chrome", srgb(178, 182, 188), "chrome", rug=0.30, metal=0.65, dessous=srgb(222, 224, 228),
                           rug_dessous=0.14, bords=0.5, rayures=0.3, salete=0.4, graine=31)
    M["Interieur"] = fabrique(f"{prefixe}_Interieur", srgb(16, 16, 17), rug=0.8, metal=0.3, salete=0.0, variation=0.0)
    M["Caoutchouc"] = fabrique(f"{prefixe}_Caoutchouc", srgb(24, 24, 24), "caoutchouc", rug=0.9, dessous=srgb(50, 50, 50),
                               rug_dessous=0.95, metal_dessous=0.0, bords=0.5, salete=0.5, graine=24)
    # la visière : mate (vernie, elle renvoyait la lumière de remplissage et le visage devenait gris)
    M["Ecran"] = fabrique(f"{prefixe}_Ecran", srgb(9, 11, 13), rug=0.7, salete=0.0, variation=0.0)
    M["Lueur"] = lueur(prefixe, couleur, fabrique)
    M["Lentille"] = fabrique(f"{prefixe}_Lentille", srgb(6, 9, 16), rug=0.02, salete=0.0, variation=0.0, coat=1.0)
    M["Marquage"] = fabrique(f"{prefixe}_Marquage_Blanc", srgb(214, 210, 198), "marquage", rug=0.62, dessous=srgb(44, 47, 52),
                             rug_dessous=0.55, metal_dessous=0.0, bords=0.0, eclats=0.5, rayures=0.45, salete=0.3,
                             graine=25, echelle_eclats=150.0)
    M["MarquageNoir"] = fabrique(f"{prefixe}_Marquage_Noir", srgb(20, 20, 21), "marquage", rug=0.68, dessous=srgb(160, 160, 160),
                                 rug_dessous=0.6, metal_dessous=0.0, bords=0.0, eclats=0.45, salete=0.3, graine=26,
                                 echelle_eclats=150.0)
    M["Appret"] = fabrique(f"{prefixe}_Appret", srgb(118, 50, 38), "appret", rug=0.78, dessous=acier_nu, rug_dessous=0.4,
                           metal_dessous=0.6, bords=0.85, eclats=0.7, rayures=0.45, salete=0.7, graine=27)
    M["Soudure"] = fabrique(f"{prefixe}_Soudure", srgb(92, 88, 84), "soudure", rug=0.62, metal=1.0, salete=0.9, variation=0.2, graine=28)
    return M


def sature(c):
    """La même teinte, saturée au plus : une lueur forte de la couleur exacte blanchit sous AgX."""
    bas, haut = min(c[:3]), max(c[:3])
    if haut - bas < 1e-6:
        return c
    return tuple(haut * (x - bas) / (haut - bas) for x in c[:3]) + (1.0,)


def lueur(prefixe, couleur, fabrique=None):
    """Les lueurs (yeux, cœur, fentes) : la couleur du joueur, saturée, en émission."""
    fabrique = fabrique or kit.materiau
    m = fabrique(f"{prefixe}_Lueur_{couleur}", srgb(26, 20, 34), rug=0.4, salete=0.0, variation=0.0,
                 emission=sature(PALETTE[couleur]), force_emission=4.5)
    m["base_aplat"] = list(srgb(26, 20, 34))
    return m


# --- assemblage sur le squelette ------------------------------------------------------------------------

def importer_squelette(scene, prefixe):
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
    arm.name = f"{prefixe}_Squelette"
    arm.data.pose_position = "REST"
    return arm


def assembler(rb, col):
    """Chaque pièce devient un objet enfant du squelette, ramené dans son repère, lié à son os seul ;
    puis tout est réuni en un maillage (un seul SkinnedMeshRenderer dans Unity)."""
    inverse = rb.arm.matrix_world.inverted()
    objets = []
    for pc, os_nom in rb.pieces:
        if pc.genre is not None and rb.genres is not None and pc.genre not in rb.genres:
            pc.bm.free()
            continue
        remplacement = rb.remplacements.get(pc.nom[len(rb.prefixe) + 1:], {})
        pc.materiaux = [remplacement.get(m, m) for m in pc.materiaux]
        bmesh.ops.transform(pc.bm, matrix=inverse, verts=pc.bm.verts)
        ob = kit.vers_objet(pc, col, rb.M, parent=rb.arm, angle_lisse=getattr(pc, "angle_lisse", 35.0))
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
    ob.name = rb.prefixe
    ob.data.name = rb.prefixe
    mod = ob.modifiers.new("Squelette", "ARMATURE")
    mod.object = rb.arm
    mod.use_vertex_groups = True
    return ob


def uv_ecran(rb, ob, cadre):
    """Les UV de la visière : sa projection de face dans le cadre de l'image de l'écran (cadre = largeur, bas,
    hauteur, en m ; u de gauche à droite pour qui la regarde, v de bas en haut), pour l'écran de la mort
    (« FATAL ERROR », DuelArena/EcranVisage). Le reste du maillage n'affiche pas d'image : ses UV restent à zéro."""
    largeur, bas, hauteur = cadre
    me = ob.data
    uv = me.uv_layers.new(name="UVMap", do_init=False)
    ecran = [i for i, m in enumerate(me.materials) if m == rb.M["Ecran"]]
    M = ob.matrix_world
    for poly in me.polygons:
        if poly.material_index not in ecran:
            continue
        for li in poly.loop_indices:
            q = M @ me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (0.5 + q.x / largeur, (q.z - bas) / hauteur)


def verifier_volumes(arm, ob, ecarts_voulus=None):
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
        if r > ref + TOLERANCE_VOLUME + (ecarts_voulus or {}).get(court.replace("Right", "Left"), 0.0):
            marque = "   <-- dépasse"
            depasse = True
        print(f"    {court:<16} {r:.3f} / {ref:.3f}{marque}")
    return not depasse


def hors_capsule(p, a, b, r):
    """De combien p sort de la capsule (a, b, r) ; négatif dedans."""
    ab = b - a
    t = 0.0 if ab.length_squared == 0 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length - r


def verifier_surface_touchable(arm, ob):
    """Ce qui sort de la surface touchable debout, partie par partie (les bras exclus) : la part des sommets à
    plus d'un centimètre dehors, et le plus grand écart. Les pieds débordent comme ceux du Y Bot : le bas de
    la capsule des jambes est rond."""
    parties = {"tête et cou": ("Head", "Neck"), "buste": ("Spine2", "Spine1", "Spine", "Hips"),
               "jambes": ("UpLeg", "Leg"), "pieds": ("Foot", "ToeBase")}
    groupes = {g.index: g.name.replace("mixamorig:", "") for g in ob.vertex_groups}
    Mo = ob.matrix_world
    stats = {nom: [0, 0, 0.0, None] for nom in parties}
    for v in ob.data.vertices:
        os_nom = groupes[v.groups[0].group]
        partie = next((nom for nom, motifs in parties.items()
                       if any(os_nom == m or os_nom.endswith(m) and ("Left" in os_nom or "Right" in os_nom) for m in motifs)), None)
        if partie is None:
            continue
        p = Mo @ v.co
        d = min(hors_capsule(p, *TETE_TOUCHABLE), hors_capsule(p, *TORSE_TOUCHABLE), hors_capsule(p, *JAMBES_TOUCHABLES))
        st = stats[partie]
        st[0] += 1
        st[1] += d > 0.01
        if d > st[2]:
            st[2], st[3] = d, (os_nom, tuple(round(c, 3) for c in p))
    print("  Surface touchable debout (sommets à plus de 1 cm dehors, plus grand écart, et où) :")
    for nom, (n, dehors, pire, ou) in stats.items():
        print(f"    {nom:<12} {100.0 * dehors / max(n, 1):5.1f} %   {100.0 * max(pire, 0.0):4.1f} cm   {ou}")


# --- les trois rendus --------------------------------------------------------------------------------------

def repere_usure(col):
    """Les matériaux du kit lisent la position de l'objet (en m) ; les maillages du robot sont dans le
    repère du squelette (en cm). Un repère neutre à l'origine du monde leur rend des mètres."""
    e = bpy.data.objects.get("Robots_Repere_Usure") or bpy.data.objects.new("Robots_Repere_Usure", None)
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


def contour(nom="Robots_Contour"):
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


def charger_attente(chemin=None):
    """L'action d'un clip du même squelette (l'attente à mains nues par défaut), pour poser le robot dans les
    rendus."""
    chemin = chemin or ATTENTE
    avant_obj = set(bpy.data.objects)
    avant_act = set(bpy.data.actions)
    scene = bpy.context.scene
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=chemin, automatic_bone_orientation=False, ignore_leaf_bones=False)
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


def armer(col, arm, collection="FA-07 — fusil d'assaut", av_main_gauche=0.30, h_main_gauche=0.108):
    """L'arme du héros : la collection de l'arme, prise dans Armes.blend, posée dans les mains du robot posé.
    Son origine (la prise de la main droite) au creux de la main droite ; son repère MainGauche (av, h) au
    creux de la main gauche ; le haut au plus près de la verticale."""
    with bpy.data.libraries.load(ARMES_BLEND, link=False) as (source, cible):
        cible.collections = [collection]
    arme = cible.collections[0]
    pb = arm.pose.bones
    M = arm.matrix_world

    def creux(main):
        return (M @ pb[f"mixamorig:{main}"].head).lerp(M @ pb[f"mixamorig:{main}Middle1"].head, 0.5)

    droite, gauche = creux("RightHand"), creux("LeftHand")
    v = gauche - droite
    haut = (Z - v.normalized() * Z.dot(v.normalized())).normalized()
    canon = (v - haut * h_main_gauche).normalized()
    haut = (haut - canon * haut.dot(canon)).normalized()
    gauche_tireur = (-canon).cross(haut)
    repere = Matrix((gauche_tireur, -canon, haut)).transposed().to_4x4()
    repere.translation = droite
    inst = bpy.data.objects.new("CT01_Arme", None)
    inst.instance_type = "COLLECTION"
    inst.instance_collection = arme
    col.objects.link(inst)
    inst.matrix_world = repere
    return inst


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
                            ("Mur", [(-largeur, 2.2, 0), (largeur, 2.2, 0), (largeur, 2.2, 9), (-largeur, 2.2, 9)], mur_mat)):
        me = bpy.data.meshes.new(f"CT01_Decor_{nom}")
        me.from_pydata([Vector(c) for c in coins], [], [(0, 1, 2, 3)])
        me.materials.append(mat)
        ob = bpy.data.objects.new(f"CT01_Decor_{nom}", me)
        col.objects.link(ob)


def lumieres(col, centre, recul=1.0):
    """L'éclairage de l'arène : une clé chaude venue d'en haut à gauche (le soleil de l'arène), un
    remplissage froid et faible, un contre-jour qui détache la silhouette du mur. recul : les lampes plus loin,
    plus grandes et plus fortes d'autant (le même éclairage au centre), pour éclairer pareil une rangée de robots."""
    soleil = bpy.data.lights.new("CT01_Soleil", "SUN")
    soleil.energy = 3.2
    soleil.color = (1.0, 0.957, 0.839)
    soleil.angle = math.radians(2.0)
    ob = bpy.data.objects.new("CT01_Soleil", soleil)
    col.objects.link(ob)
    ob.rotation_euler = (math.radians(42), 0, math.radians(-32))
    kit.lumiere("CT01_Remplissage", col, centre + Vector((-3.5, -4.0, 1.2)) * recul, centre, 900 * recul ** 2, 4.0 * recul,
                couleur=(0.78, 0.86, 1.0))
    kit.lumiere("CT01_Contre", col, centre + Vector((2.5, 3.0, 2.8)) * recul, centre, 1300 * recul ** 2, 2.0 * recul,
                couleur=(1.0, 0.95, 0.9))


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

class Modele:
    """Un modèle de robot : son code (CT-01), son préfixe (CT01), son corps (corps(rb, variante) : toutes ses
    pièces), ses variantes et ses skins (des dictionnaires, voir robot_ct01), le cadre de l'écran de sa visière
    (largeur, bas, hauteur : None s'il n'en a pas) et ce qu'il dépasse exprès de la peau du Y Bot."""

    def __init__(self, code, prefixe, corps, variantes=None, skins=None, cadre_ecran=None, ecarts_voulus=None):
        self.code = code
        self.prefixe = prefixe
        self.corps = corps
        self.variantes = variantes or {}
        self.skins = skins or {}
        self.cadre_ecran = cadre_ecran
        self.ecarts_voulus = ecarts_voulus or {}


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(globals().get("ARGS") or [])
    p = argparse.ArgumentParser()
    p.add_argument("--rendus", default="")
    p.add_argument("--vues", default="")
    p.add_argument("--fbx", default="")
    p.add_argument("--sortie", default="")
    p.add_argument("--couleur", default="violet", choices=list(PALETTE))
    p.add_argument("--variantes", default="", help="lettres des variantes du modèle : les rendre côte à côte")
    p.add_argument("--skins", default="", help="clés des skins du modèle : les rendre côte à côte")
    p.add_argument("--skin", default="", help="une clé des skins du modèle : le robot construit, rendu et exporté ainsi")
    return p.parse_args(brut)


def construire(modele, scene, couleur, variante=None, skin=None, nommer=False):
    col = kit.collection(f"{modele.code} — Robot" + "".join(f" {x}" for x in (variante, skin) if x), scene.collection)
    arm = importer_squelette(scene, modele.prefixe)
    for c in arm.users_collection:
        c.objects.unlink(arm)
    col.objects.link(arm)
    reglages = modele.skins[skin] if skin else {}
    M = materiaux(modele.prefixe, couleur, reglages.get("usure"), f"_{skin}" if skin and nommer else "")
    rb = Robot(scene, arm, M, modele.prefixe, genres=reglages.get("genres"), remplacements=reglages.get("remplacements"))
    modele.corps(rb, variante)
    if reglages.get("livree"):
        reglages["livree"](rb)
    n_pieces = sum(1 for pc, _ in rb.pieces if pc.genre is None or rb.genres is None or pc.genre in rb.genres)
    ob = assembler(rb, col)
    if modele.cadre_ecran:
        uv_ecran(rb, ob, modele.cadre_ecran)
    nom = modele.code + (f" {variante} ({modele.variantes[variante][0].lower()})" if variante else "") + \
        (f" {modele.skins[skin]['titre'].lower()}" if skin else "")
    print(f"  {nom} : {n_pieces} pièces, {kit.triangles(ob)} triangles, {len(ob.data.materials)} matériaux")
    verifier_volumes(arm, ob, modele.ecarts_voulus)
    verifier_surface_touchable(arm, ob)
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


def principal(modele):
    args = arguments()
    scene = bpy.data.scenes.get(f"Robot {modele.code}") or bpy.data.scenes.new(f"Robot {modele.code}")
    if bpy.app.background:
        usine = bpy.data.scenes.get("Scene")
        if usine is not None and usine != scene:
            for o in list(usine.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.scenes.remove(usine)
    if bpy.context.window_manager.windows:
        bpy.context.window_manager.windows[0].scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    if args.variantes:
        robots = [(v,) + construire(modele, scene, args.couleur, variante=v) for v in args.variantes.split(",")]
        if args.rendus:
            rendus_cote_a_cote(scene, robots, {v: f"{v} — {t}" for v, (t, *_) in modele.variantes.items()}, args, "variantes", 1.0,
                               [("dos", 180), ("dos_34", 150), ("face_34", -25), ("profil", -90)],
                               [("zoom_dos", 150, Vector((0.42, -1.75, 1.22)), 1.10, 70, 1200, 1400),
                                ("zoom_face", -25, Vector((-0.42, -1.75, 1.22)), 1.10, 70, 1200, 1400)])
        return robots
    if args.skins:
        robots = [(k,) + construire(modele, scene, args.couleur, skin=k, nommer=True) for k in args.skins.split(",")]
        if args.rendus:
            rendus_cote_a_cote(scene, robots, {k: s["titre"] for k, s in modele.skins.items()}, args, "skins", 0.85,
                               [("face_34", -25), ("dos_34", 150)],
                               [("portrait", -25, Vector((-0.75, -3.0, 1.10)), 0.93, 50, 1200, 1700),
                                ("portrait_dos", 150, Vector((0.75, -3.0, 1.10)), 0.93, 50, 1200, 1700)])
        return robots
    rb, ob, col = construire(modele, scene, args.couleur, skin=args.skin or None)
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
    monde = bpy.data.worlds.get("Robots_Monde") or bpy.data.worlds.new("Robots_Monde")
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
    vues = [v for v in args.vues.split(",") if v] or ["comparaison", "planche", "bustes", "couleurs", "heros", "dos"]

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
            m = kit.materiau(f"{rb.prefixe}_Peinture_{nom}", PALETTE[nom], rug=0.5, dessous=srgb(150, 152, 156), rug_dessous=0.4,
                             metal_dessous=0.6, bords=0.62, eclats=0.38, rayures=0.45, salete=0.5, graine=21)
            ancrer(m, repere)
            M_couleur = dict(lowpoly)
            M_couleur[rb.M["Peinture"].name] = m
            m_lueur = lueur(rb.prefixe, nom)
            ancrer(m_lueur, repere)
            M_couleur[rb.M["Lueur"].name] = m_lueur
            x = (i - 3) * 0.78
            arm2, ob2 = copie(rb, ob, col, Vector((x, 0.35 * abs(i - 3), 0)), -18 + (i - 3) * -4, f"couleur{i}", M_couleur, action, None)
            copies[f"couleur{i}"] = (arm2, ob2, None)
        scene.frame_set(1)
        cam = kit.camera("CT01_Cam_Couleurs", col, Vector((0.4, -8.2, 1.5)), Vector((0, 0.4, 0.9)), 45)
        rendre(scene, cam, os.path.join(args.rendus, "couleurs.png"), 2400, 1100)
        vider_copies()
    if "dos" in vues:
        # le robot de dos, de trois quarts : ce que voit celui qui le suit
        arm2, ob2 = copie(rb, ob, col, Vector((0, 0, 0)), 152, "dos", lowpoly, action, None)
        scene.frame_set(1)
        cam = kit.camera("CT01_Cam_Dos", col, Vector((0.55, -2.65, 1.20)), Vector((0.0, 0, 0.98)), 40)
        rendre(scene, cam, os.path.join(args.rendus, "dos.png"), 1600, 2000)
        copies["dos"] = (arm2, ob2, None)
        vider_copies()
    if "heros" in vues:
        # Le robot armé, dans l'attente du fusil des duellistes, vu d'en bas : la menace
        action_fusil = charger_attente(ATTENTE_FUSIL)
        arm2, ob2 = copie(rb, ob, col, Vector((0, 0, 0)), -12, "heros", lowpoly, action_fusil, None)
        scene.frame_set(1)
        scene.view_layers[0].update()
        arme = armer(col, arm2)
        contre = kit.lumiere("CT01_Contre_Heros", col, Vector((-2.2, 0.9, 2.0)), Vector((0, 0.1, 1.2)), 800, 1.0,
                             couleur=(0.82, 0.86, 1.0))
        cam = kit.camera("CT01_Cam_Heros", col, Vector((0.62, -2.45, 0.85)), Vector((0.0, 0, 1.05)), 35)
        rendre(scene, cam, os.path.join(args.rendus, "heros.png"), 1600, 2000)
        bpy.data.objects.remove(arme, do_unlink=True)
        bpy.data.objects.remove(contre, do_unlink=True)
        copies["heros"] = (arm2, ob2, None)
        vider_copies()


def rendus_cote_a_cote(scene, robots, titres, args, nom, ecart, vues, plans):
    """Des robots côte à côte (les variantes, les skins), dans l'attente à mains nues : une planche par vue
    (vue orthographique, comme la planche ; vues = [(nom, rotation)]), chacun nommé sous ses pieds ; puis un
    gros plan de chacun, seul (plans = [(nom, rotation, caméra par rapport au robot, hauteur visée, focale,
    largeur, hauteur)])."""
    os.makedirs(args.rendus, exist_ok=True)
    monde = bpy.data.worlds.get("Robots_Monde") or bpy.data.worlds.new("Robots_Monde")
    fond = monde.node_tree.nodes.get("Background")
    if fond is not None:
        fond.inputs["Color"].default_value = srgb(20, 21, 23)
        fond.inputs["Strength"].default_value = 0.6
    scene.world = monde
    scene.view_settings.view_transform = "AgX"
    col = kit.collection("Rendus", scene.collection)
    repere = repere_usure(col)
    for _, rb, _, _ in robots:
        for m in set(rb.M.values()):
            ancrer(m, repere)
    action = charger_attente()
    decor(col)
    lumieres(col, Vector((0, 0, 1.0)), recul=3.0)   # de loin : la même lumière d'un bout à l'autre de la rangée
    n = len(robots)
    xs = [(i - (n - 1) / 2) * ecart for i in range(n)]
    for (cle, rb, _, _), x in zip(robots, xs):
        rb.arm.location.x += x
        poser(rb.arm, action, 1)
        etiquette(col, titres[cle], Vector((x, -0.6, -0.17)), 0.08)
    base = [rb.arm.rotation_euler.z for _, rb, _, _ in robots]
    largeur = 0.6 + ecart * n
    cam = kit.camera(f"CT01_Cam_{nom}", col, Vector((0, -9.0, 0.85)), Vector((0, 0, 0.85)), 50, ortho=largeur)
    hauteur_px = int(round(3000 * 2.7 / largeur / 10)) * 10
    for vue, angle in vues:
        for (_, rb, _, _), z in zip(robots, base):
            rb.arm.rotation_euler.z = z + math.radians(angle)
        scene.frame_set(1)
        rendre(scene, cam, os.path.join(args.rendus, f"{nom}_{vue}.png"), 3000, hauteur_px)
    for (cle, rb, ob, _), x, z in zip(robots, xs, base):
        for _, _, autre, _ in robots:
            autre.hide_render = autre is not ob
        rb.arm.location.x -= x   # chacun au centre, sous la même lumière
        for plan, angle, position, cible_z, focale, l_px, h_px in plans:
            rb.arm.rotation_euler.z = z + math.radians(angle)
            scene.frame_set(1)
            cam_plan = kit.camera(f"CT01_Cam_{plan}_{cle}", col, position, Vector((0, 0, cible_z)), focale)
            rendre(scene, cam_plan, os.path.join(args.rendus, f"{plan}_{cle}.png"), l_px, h_px)
        rb.arm.location.x += x
    for _, _, ob, _ in robots:
        ob.hide_render = False


