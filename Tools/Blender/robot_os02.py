"""Le robot OS-02 de Duel Arena (Blender 5.2, low poly) : un second châssis, l'« Ossature » (2026-10-26).

Demandé par l'utilisateur : « un autre type de robot en t'inspirant du robot de Terminator, le T-800, qui fasse
les mêmes tailles que l'autre robot », sur une image de référence (l'endosquelette chromé). L'archétype
seulement, comme le CT-01 avec KAY/O : le jeu sera vendu. Un endosquelette mécanique : des os d'acier poli, des
vérins hydrauliques à la place des muscles, des côtes en barres autour d'un cœur qui luit, une colonne apparente
sous un carter blindé, et une tête de mort mécanique : un crâne d'homme en acier, compact, le front aussi large
que la mâchoire (refait le 26 octobre : la voûte haute et étroite faisait « ampoule »), des arcades zygomatiques,
des orbites sous une arcade en V, deux petits yeux qui luisent, une mâchoire à part sur ses charnières, une grille
de bouche ; ni dents, ni peinture sur le crâne.

Ce que la bible impose à tous les combattants (§ 5) : la même silhouette et les mêmes zones touchables que le
CT-01 (le même squelette, les mêmes volumes, vérifiés par commun_robots), et la peinture du joueur qui
l'identifie : les pectoraux, les omoplates, les épaulières, les brassards, les gardes des avant-bras, les plaques
des cuisses, des genoux et des tibias, les ailes du bassin, le ventre et les cous-de-pied ; ses lueurs (les yeux,
le cœur, les fentes des tibias) prennent la couleur du joueur.

Épaissi le 26 octobre (demandé par l'utilisateur) pour montrer autant de sa surface touchable que le CT-01 : à
0,7 point près par zone, de face comme de profil (couverture_robots.py) ; un robot plus fin se voyait et se visait
moins bien pour la même surface touchable. D'où la tête agrandie, le cou à vérins, la cage plus bombée et ses
flancs, le carter du dos, la taille moins fine, les brassards et les plaques des jambes.

  blender -b --factory-startup -P robot_os02.py -- [--skin vierge] [--rendus dossier] [--vues a,b] [--fbx f.fbx]
                                                    [--couleur violet]
"""

import importlib
import math
import os
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_robots as cr  # noqa: E402

importlib.reload(cr)
from commun_robots import *  # noqa: E402,F401,F403  le vocabulaire des robots : primitives, repères, matériaux

CODE = "OS-02"
PREFIXE = "OS02"

HEXAGONE = [(a, 1.0) for a in range(0, 360, 60)]          # le profil d'un os ou d'un vérin à pans


def verin(pc, a, b, r, mat_corps="Chassis", mat_tige="Metal", part=0.55):
    """Un vérin hydraulique de a à b : le corps (rayon r) sur la part `part` du trajet, la tige (r × 0,45) au-delà,
    et une bague à chaque bout."""
    m = a.lerp(b, part)
    faces = kit.cylindre(pc, a, m, r, 8, mat=mat_corps)
    kit.chanfreiner(pc, min(0.002, r * 0.15), faces, 60)
    kit.cylindre(pc, m - (b - a).normalized() * 0.004, b, r * 0.45, 8, mat=mat_tige)
    for q in (a, b):
        d = (b - a).normalized()
        kit.cylindre(pc, q - d * 0.006, q + d * 0.006, r * 0.72, 8, mat="Chrome")


def barre(pc, a, b, larg, ep, mat="Chrome", haut=HAUT):
    """Une barre à section rectangulaire de a à b (largeur selon haut, épaisseur en travers)."""
    kit.balayage(pc, [a, b], kit.contour_rect(ep, larg, min(ep, larg) * 0.25), mat, normale=haut)


# --- la tête : un crâne mécanique -------------------------------------------------------------------

# Un crâne d'homme, compact, d'un seul tenant de la nuque au sommet (2026-10-26, demandé par l'utilisateur :
# « un truc humanoïde, le front aussi large que la mâchoire, et très peu allongé » ; la voûte haute et
# étroite d'avant faisait « ampoule ») : des côtés presque droits, aussi larges que la mâchoire, une voûte
# basse qui s'arrondit vite, le front qui fuit dès l'arcade, l'occiput rond derrière. Tout d'acier : sans plaque
# peinte (l'utilisateur, le même jour).
CRANE = [  # (z, demi-largeur, devant, nuque, tempes) : sous l'arcade, le devant rentre derrière le visage
    (1.600, 0.044, -0.040, 0.058, 0.86), (1.622, 0.052, -0.062, 0.071, 0.86), (1.640, 0.060, -0.074, 0.080, 0.86),
    (1.664, 0.062, -0.084, 0.087, 0.88), (1.686, 0.063, -0.093, 0.090, 0.90), (1.708, 0.064, -0.091, 0.091, 0.90),
    (1.728, 0.064, -0.084, 0.088, 0.92), (1.744, 0.061, -0.074, 0.079, 0.92), (1.756, 0.055, -0.061, 0.066, 0.92),
    (1.765, 0.046, -0.046, 0.050, 0.92), (1.771, 0.034, -0.030, 0.034, 0.92), (1.775, 0.018, -0.013, 0.016, 0.92),
    (1.777, 0.006, -0.002, 0.006, 0.92)]


# La tête entière agrandie autour du haut du cou (2026-10-26, pour couvrir autant de surface touchable que le CT-01 :
# le crâne compact laissait la moitié de la sphère de la tête vide) : ses proportions ne changent presque pas.
TETE_PIVOT = Vector((0.0, 0.0, 1.585))
TETE_ECHELLE = Vector((1.22, 1.17, 1.14))


def section_crane(hw, f, b, tempes=0.86):
    """Douze points (x, y) : un front plat, des tempes (creusées sous 0,86), des pariétaux larges, un
    occiput rond, à pans."""
    return [(0.0, f), (0.50 * hw, f + 0.006), (tempes * hw, 0.62 * f), (hw, 0.18 * f), (0.94 * hw, 0.45 * b),
            (0.62 * hw, 0.88 * b), (0.0, b), (-0.62 * hw, 0.88 * b), (-0.94 * hw, 0.45 * b), (-hw, 0.18 * f),
            (-tempes * hw, 0.62 * f), (-0.50 * hw, f + 0.006)]


def section_u(pts, ep, centre=Vector((0.0, 0.0))):
    """Une plaque en U fermée : le contour pts (x, y), puis le même rentré de ep vers centre, à l'envers."""
    inte = []
    for x, y in reversed(pts):
        d = (Vector((x, y)) - centre).normalized()
        inte.append((x - d.x * ep, y - d.y * ep))
    return list(pts) + inte


def tete(rb):
    o = rb.os("Head")
    premiere = len(rb.pieces)
    pc = rb.piece("Crane", o.nom, "Chrome")
    loft_z(pc, CRANE, "Chrome", section_crane)
    kit.chanfreiner(pc, 0.0025)
    crane = Surface(pc)
    # les arcades zygomatiques : de la pommette vers l'oreille, au large de la tempe
    pc = rb.piece("Zygomas", o.nom, "Chrome")
    for s in (1, -1):
        kit.balayage(pc, kit.lisser_chemin([Vector((s * 0.055, -0.058, 1.637)), Vector((s * 0.065, -0.030, 1.638)),
                                            Vector((s * 0.067, 0.002, 1.640)), Vector((s * 0.062, 0.026, 1.641))], 2),
                     kit.contour_rect(0.011, 0.007, 0.002), "Chrome", normale=Z)

    # les orbites : deux puits sombres à pans sous l'arcade, un petit œil qui luit au fond de chacun
    pc = rb.piece("Orbites", o.nom, "Interieur")
    pc_yeux = rb.piece("Yeux", o.nom, "Lueur")
    for s in (1, -1):
        p, n = crane.toucher(Vector((s * 0.030, -0.4, 1.660)), Y)
        kit.cylindre(pc, p - n * 0.004, p + n * 0.006, 0.019, 6, mat="Chrome", bouts=False)
        kit.disque(pc, p + n * 0.0006, n, 0.019, 6, mat="Interieur")
        kit.disque(pc_yeux, p + n * 0.0014, n, 0.0075, 10, mat="Lueur")
    # l'arcade : une barre en V au-dessus des yeux, plus basse au milieu, un regard dur
    pc = rb.piece("Arcade", o.nom, "Chrome")
    for s in (1, -1):
        kit.balayage(pc, kit.lisser_chemin([Vector((s * 0.054, -0.066, 1.702)), Vector((s * 0.034, -0.092, 1.692)),
                                            Vector((s * 0.004, -0.101, 1.676))], 2),
                     kit.contour_rect(0.012, 0.014, 0.003), "Chrome", normale=Z)
    # les pommettes, en coin sous les yeux
    pc = rb.piece("Pommettes", o.nom, "Chrome")
    for s in (1, -1):
        joue = [(s * 0.034, -0.081), (s * 0.062, -0.061), (s * 0.067, -0.026), (s * 0.050, -0.043)]
        kit.loft(pc, [[Vector((x * k, y * k, z)) for x, y in joue] for z, k in ((1.628, 0.94), (1.646, 1.0))], "Chrome")
    kit.chanfreiner(pc, 0.0015)

    # le museau : un bloc à pans sous les yeux, fendu d'une prise d'air
    pc = rb.piece("Museau", o.nom, "Chrome")
    museau = [(0.030, -0.075), (0.047, -0.055), (0.054, -0.014), (-0.054, -0.014), (-0.047, -0.055), (-0.030, -0.075)]
    kit.loft(pc, [[Vector((x * k, y * k, z)) for x, y in museau] for z, k in ((1.602, 0.92), (1.640, 1.0))], "Chrome")
    kit.chanfreiner(pc, 0.002)
    museau_s = Surface(pc)
    for i, z in enumerate((1.616, 1.626)):
        p, n = museau_s.toucher(Vector((0.0, -0.4, z)), Y)
        x, yv = axes_decalque(n)
        c = p + n * 0.0008
        w = 0.020 - 0.004 * i
        kit.quad(pc, [c - x * w - yv * 0.0022, c + x * w - yv * 0.0022, c + x * w + yv * 0.0022, c - x * w + yv * 0.0022],
                 "Interieur", normale=n)
    # la mâchoire, à part, sur deux charnières ; des vérins de joue la tiennent
    pc = rb.piece("Machoire", o.nom, "Chrome")
    machoire = [(0.060, 0.004), (0.060, -0.036), (0.046, -0.064), (0.0, -0.072), (-0.046, -0.064), (-0.060, -0.036), (-0.060, 0.004)]
    kit.loft(pc, [[Vector((x * k, y * k, z)) for x, y in section_u(machoire, 0.010)] for z, k in ((1.562, 0.92), (1.600, 1.0))],
             "Chrome")
    kit.chanfreiner(pc, 0.002)
    machoire_s = Surface(pc)
    for i in range(5):   # la grille de la bouche : des fentes droites, pas des dents
        p, n = machoire_s.toucher(Vector((-0.020 + i * 0.010, -0.4, 1.582)), Y)
        x, yv = axes_decalque(n)
        c = p + n * 0.0008
        kit.quad(pc, [c - x * 0.0022 - yv * 0.012, c + x * 0.0022 - yv * 0.012, c + x * 0.0022 + yv * 0.012,
                      c - x * 0.0022 + yv * 0.012], "Interieur", normale=n)
    for s in (1, -1):
        tambour(pc, Vector((s * 0.062, 0.002, 1.604)), X, 0.012, 0.006, n=10, mat="Metal")
        verin(pc, Vector((s * 0.050, -0.042, 1.648)), Vector((s * 0.060, -0.008, 1.598)), 0.0055)
    for pc, _ in rb.pieces[premiere:]:
        for v in pc.bm.verts:
            v.co = TETE_PIVOT + (v.co - TETE_PIVOT) * TETE_ECHELLE
        pc.bm.normal_update()


def cou(rb):
    o = rb.os("Neck")
    pc = rb.piece("Cou", o.nom, "Chassis")
    kit.cylindre(pc, Vector((0, 0.030, 1.488)), Vector((0, 0.018, 1.610)), 0.034, 10, mat="Chassis")
    for z in (1.512, 1.540, 1.568):
        faces = kit.cylindre(pc, Vector((0, 0.028, z - 0.011)), Vector((0, 0.026, z + 0.011)), 0.046, 6, mat="Chrome")
        kit.chanfreiner(pc, 0.002, faces, 50)
    for s in (1, -1):   # les vérins du cou, devant ; ceux de la nuque, du joug au crâne (les trapèzes)
        verin(pc, Vector((s * 0.040, -0.006, 1.498)), Vector((s * 0.042, -0.024, 1.600)), 0.010)
        verin(pc, Vector((s * 0.058, 0.022, 1.500)), Vector((s * 0.052, 0.030, 1.604)), 0.014)


# --- le thorax : un plastron peint, un dos d'acier, des côtes autour d'un cœur ------------------------

PECTORAL = [(0.022, 1.490), (0.128, 1.494), (0.148, 1.452), (0.136, 1.394), (0.090, 1.360), (0.030, 1.376)]
PLASTRON = [  # (z, demi-largeur, devant)
    (1.345, 0.112, -0.120), (1.395, 0.146, -0.146), (1.452, 0.154, -0.143), (1.500, 0.128, -0.112)]
DOSSIERE = [  # (z, demi-largeur, dos)
    (1.345, 0.108, 0.096), (1.400, 0.140, 0.120), (1.455, 0.148, 0.125), (1.500, 0.126, 0.110)]
OMOPLATE = [(0.044, 1.462), (0.116, 1.470), (0.130, 1.432), (0.112, 1.378), (0.082, 1.358), (0.048, 1.370)]


def section_plastron(hw, f, ep=0.012):
    """Le plastron en (x, y), une coque ouverte derrière : du flanc droit au flanc gauche par une quille."""
    ext = [(-hw, 0.22 * f), (-0.72 * hw, 0.92 * f), (-0.24 * hw, f), (0.0, f - 0.010), (0.24 * hw, f), (0.72 * hw, 0.92 * f),
           (hw, 0.22 * f)]
    return section_u(ext, ep, Vector((0.0, 0.02)))


def section_dossiere(hw, b, ep=0.010):
    ext = [(hw, -0.024), (hw, 0.42 * b), (0.70 * hw, 0.92 * b), (0.20 * hw, b), (-0.20 * hw, b), (-0.70 * hw, 0.92 * b),
           (-hw, 0.42 * b), (-hw, -0.024)]
    return section_u(ext, ep, Vector((0.0, 0.0)))


def thorax(rb):
    o = rb.os("Spine2")
    pc = rb.piece("Plastron", o.nom, "Chrome")
    loft_z(pc, PLASTRON, "Chrome", section_plastron)
    kit.chanfreiner(pc, 0.003, angle_min=35)
    plastron = Surface(pc)
    pc = rb.piece("Pectoraux", o.nom, "Peinture")
    for s in (1, -1):
        face_avant(pc, plastron, [(s * x, z) for x, z in PECTORAL], 0.012, "Peinture")
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece("Dossiere", o.nom, "Chrome")
    loft_z(pc, DOSSIERE, "Chrome", section_dossiere)
    kit.chanfreiner(pc, 0.003, angle_min=35)
    dossiere = Surface(pc)
    pc = rb.piece("Omoplates", o.nom, "Peinture")
    for s in (1, -1):
        plaque_sur(pc, dossiere, [(s * x, z) for x, z in OMOPLATE], ORIGINE, X, Z, -Y, 0.010, "Peinture")
    kit.chanfreiner(pc, 0.0025)
    # le joug : il ferme le haut du thorax autour du cou ; les clavicules, deux barres vers les épaules
    pc = rb.piece("Joug", o.nom, "Chrome")
    loft_z(pc, [(1.494, 0.124, -0.100, 0.102), (1.534, 0.100, -0.070, 0.090)], "Chrome",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.036))
    kit.chanfreiner(pc, 0.003)
    for s in (1, -1):
        kit.balayage(pc, kit.lisser_chemin([Vector((s * 0.022, -0.100, 1.512)), Vector((s * 0.100, -0.072, 1.512)),
                                            Vector((s * 0.160, -0.008, 1.494))], 2),
                     kit.contour_rect(0.012, 0.016, 0.003), "Chrome", normale=Z)
    # le cœur, derrière les côtes : une capsule qui luit dans un cadre sombre
    o1 = rb.os("Spine1")
    pc = rb.piece("Thorax_Interieur", o1.nom, "Chassis")   # il remplit la cage : entre les côtes, du sombre
    loft_z(pc, [(1.208, 0.100, -0.094, 0.080), (1.350, 0.114, -0.102, 0.088)], "Chassis",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.034))
    pc = rb.piece("Coeur", o1.nom, "Chassis")
    kit.cylindre(pc, Vector((0, -0.090, 1.252)), Vector((0, -0.090, 1.316)), 0.020, 8, mat="Lueur")
    for z in (1.250, 1.318):
        kit.cylindre(pc, Vector((0, -0.089, z - 0.004)), Vector((0, -0.089, z + 0.004)), 0.031, 8, mat="Chrome")
    # les côtes : quatre barres épaisses de chaque côté, de la colonne au sternum, qui laissent le cœur à nu
    pc = rb.piece("Cotes", o1.nom, "Chrome")
    for i, z in enumerate((1.340, 1.308, 1.276, 1.244)):
        k = 1.0 - 0.04 * i
        for s in (1, -1):
            chemin = [Vector((s * 0.024, 0.092, z)), Vector((s * 0.094 * k, 0.080, z - 0.002)), Vector((s * 0.128 * k, 0.020, z - 0.006)),
                      Vector((s * 0.122 * k, -0.054, z - 0.012)), Vector((s * 0.072 * k, -0.108, z - 0.020)),
                      Vector((s * 0.036, -0.116, z - 0.024))]
            kit.balayage(pc, kit.lisser_chemin(chemin, 2), kit.contour_rect(0.024, 0.014, 0.004), "Chrome", normale=Z)
    # les flancs : une plaque d'acier sur le bout des côtes, de chaque côté, sous le bras
    pc = rb.piece("Flancs", o1.nom, "Chrome")
    for s in (1, -1):
        flanc = [(s * 0.120, -0.066), (s * 0.158, -0.034), (s * 0.166, 0.026), (s * 0.136, 0.066)]
        kit.loft(pc, [[Vector((x * k, y, z)) for x, y in section_u(flanc, 0.012)] for z, k in ((1.200, 0.90), (1.270, 1.0), (1.360, 1.0))],
                 "Chrome")
    kit.chanfreiner(pc, 0.0025)


CARTER = [  # (z, demi-largeur, devant, dos) : le carter du dos, du bas de la dossière à la taille
    (1.100, 0.052, 0.070, 0.132), (1.180, 0.068, 0.072, 0.166), (1.280, 0.076, 0.080, 0.176), (1.370, 0.072, 0.090, 0.166)]


def colonne(rb):
    """La colonne, apparente dans le dos entre la dossière et la ceinture : deux vertèbres et leurs épines par os ;
    par-dessus, le carter blindé de la batterie, fendu de grilles, qui donne au dos sa profondeur."""
    for os_nom, zs in (("Spine1", (1.248, 1.306)), ("Spine", (1.130, 1.188))):
        o = rb.os(os_nom)
        pc = rb.piece(f"Vertebres_{os_nom}", o.nom, "Chrome")
        for z in zs:
            kit.loft(pc, [[Vector((x, y, z + dz)) for x, y in section_rect(-0.024, 0.024, 0.060, 0.092, 0.008)] for dz in (-0.016, 0.016)],
                     "Chrome")
            kit.loft(pc, [[Vector((x, y, z + dz)) for x, y in [(-0.006, 0.090), (0.006, 0.090), (0.003, 0.112), (-0.003, 0.112)]]
                          for dz in (-0.012, 0.010)], "Chrome")
        kit.chanfreiner(pc, 0.002)
    o = rb.os("Spine1")
    pc = rb.piece("Colonne", o.nom, "Chassis")
    kit.cylindre(pc, Vector((0, 0.060, 1.060)), Vector((0, 0.064, 1.360)), 0.020, 8, mat="Chassis")
    pc = rb.piece("Carter", o.nom, "Chrome")
    loft_z(pc, CARTER, "Chrome", lambda hw, f, b: section_rect(-hw, hw, f, b, 0.022))
    kit.chanfreiner(pc, 0.003, angle_min=35)
    carter = Surface(pc)
    for z in (1.200, 1.236, 1.272, 1.308):   # les grilles d'aération, sombres
        p_, n = carter.toucher(Vector((0.0, 0.4, z)), -Y)
        x, yv = axes_decalque(n)
        c = p_ + n * 0.0008
        kit.quad(pc, [c - x * 0.040 - yv * 0.006, c + x * 0.040 - yv * 0.006, c + x * 0.040 + yv * 0.006, c - x * 0.040 + yv * 0.006],
                 "Interieur", normale=n)


def ventre(rb):
    """La taille : un noyau sombre à pans, une plaque peinte devant, des segments d'acier sur les flancs, deux gros
    vérins obliques de chaque côté."""
    o = rb.os("Spine")
    pc = rb.piece("Ventre", o.nom, "Chassis")
    loft_z(pc, [(1.090, 0.084, -0.080, 0.078), (1.160, 0.096, -0.092, 0.082), (1.230, 0.102, -0.096, 0.086)], "Chassis",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.034))
    pc = rb.piece("Ventre_Plaque", o.nom, "Peinture")
    loft_z(pc, [(1.118, 0.068, -0.100), (1.166, 0.080, -0.114), (1.212, 0.076, -0.110)], "Peinture",
           lambda hw, f: section_u([(-hw, 0.15 * f), (-0.62 * hw, f), (0.0, f - 0.006), (0.62 * hw, f), (hw, 0.15 * f)], 0.010))
    kit.chanfreiner(pc, 0.002, angle_min=35)
    pc = rb.piece("Ventre_Segments", o.nom, "Chrome")   # les obliques : trois segments d'acier de chaque côté
    for z in (1.112, 1.150, 1.188):
        for s in (1, -1):
            pts = [(s * 0.092, -0.074), (s * 0.120, -0.046), (s * 0.126, 0.034), (s * 0.104, 0.066)]
            kit.loft(pc, [[Vector((x, y, z + dz)) for x, y in section_u(pts, 0.012)] for dz in (-0.014, 0.014)], "Chrome")
    kit.chanfreiner(pc, 0.002)
    pc = rb.piece("Ventre_Verins", o.nom, "Chassis")
    for s in (1, -1):
        verin(pc, Vector((s * 0.132, -0.026, 1.232)), Vector((s * 0.118, -0.046, 1.096)), 0.017)
        verin(pc, Vector((s * 0.124, 0.060, 1.230)), Vector((s * 0.108, 0.058, 1.100)), 0.015)


def bassin(rb):
    """Le bassin : une ceinture d'acier, des ailes peintes sur les hanches, une plaque devant, le sacrum derrière."""
    o = rb.os("Hips")
    pc = rb.piece("Bassin", o.nom, "Chassis")
    loft_z(pc, [(0.930, 0.070, -0.060, 0.062), (0.990, 0.094, -0.074, 0.074), (1.066, 0.098, -0.076, 0.072)], "Chassis",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.030))
    pc = rb.piece("Ceinture", o.nom, "Chrome")
    loft_z(pc, [(1.062, 0.118, -0.094, 0.090), (1.100, 0.112, -0.090, 0.086)], "Chrome",
           lambda hw, f, b: section_rect(-hw, hw, f, b, 0.034))
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece("Bassin_Avant", o.nom, "Chassis")
    kit.loft(pc, [[Vector((x, y, 0.928)) for x, y in section_rect(-0.026, 0.026, -0.092, -0.074, 0.005)],
                  [Vector((x, y, 1.060)) for x, y in section_rect(-0.050, 0.050, -0.112, -0.090, 0.006)]], "Chassis")
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece("Sacrum", o.nom, "Chrome")
    kit.loft(pc, [[Vector((x, y, 0.952)) for x, y in section_rect(-0.022, 0.022, 0.060, 0.090, 0.005)],
                  [Vector((x, y, 1.060)) for x, y in section_rect(-0.046, 0.046, 0.066, 0.104, 0.006)]], "Chrome")
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece("Ailes", o.nom, "Peinture")   # les ailes iliaques, peintes, qui s'écartent vers le bas
    for s in (1, -1):
        haut_ = [(s * 0.112, -0.088), (s * 0.126, -0.088), (s * 0.126, 0.084), (s * 0.112, 0.084)]
        bas_ = [(s * 0.140, -0.072), (s * 0.154, -0.072), (s * 0.154, 0.068), (s * 0.140, 0.068)]
        kit.loft(pc, [[Vector((x, y, 0.948)) for x, y in bas_], [Vector((x, y, 1.062)) for x, y in haut_]], "Peinture")
    kit.chanfreiner(pc, 0.003)


# --- les bras ---------------------------------------------------------------------------------------
# Bras en croix : u le haut (le dehors, bras baissé), v l'avant ; angle 0° le haut, 90° l'avant.

EPAULIERE = [(-76, 0.94), (0, 1.24), (76, 0.94)]                         # une lame peinte à arête sur l'épaule
BRASSARD = [(-62, 0.92), (-20, 1.06), (24, 1.12), (70, 1.04), (112, 0.92)]  # la plaque peinte du bras, dehors et devant
GARDE = [(-64, 0.94), (-24, 1.06), (0, 1.12), (24, 1.06), (64, 0.94)]      # la garde du dehors de l'avant-bras


def bras(rb, cote):
    s = 1 if cote == "Left" else -1
    clav = rb.os(f"{cote}Shoulder", u=HAUT, v=AVANT)
    o = rb.os(f"{cote}Arm", u=HAUT, v=AVANT)
    ob = rb.os(f"{cote}ForeArm", u=HAUT, v=AVANT)

    pc = rb.piece(f"Epaule_{cote}", clav.nom, "Chassis")
    kit.loft(pc, [[clav.p(0.030, a, b) for a, b in section_rect(-0.024, 0.036, -0.050, 0.034, 0.010)],
                  [clav.p(0.120, a, b) for a, b in section_rect(-0.026, 0.040, -0.054, 0.038, 0.012)]], "Chassis")
    pc = rb.piece(f"Rotule_Epaule_{cote}", o.nom, "Chrome")
    tambour(pc, o.p(0.004), o.v, 0.046, 0.032, n=12, mat="Chrome")
    tambour(pc, o.p(0.004), o.v, 0.030, 0.038, n=10, mat="Metal")
    pc = rb.piece(f"Epauliere_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(-0.040, 0.060), (0.010, 0.070), (0.070, 0.066), (0.112, 0.048)], EPAULIERE, 0.011, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)

    # l'humérus, un os d'acier à pans ; un gros vérin devant (le biceps), un plus petit derrière
    pc = rb.piece(f"Humerus_{cote}", o.nom, "Chrome")
    tube_profil(pc, o, [(0.040, 0.024), (0.150, 0.021), (0.262, 0.023)], HEXAGONE, "Chrome")
    kit.chanfreiner(pc, 0.002, angle_min=40)
    pc = rb.piece(f"Bras_Verins_{cote}", o.nom, "Chassis")
    verin(pc, o.p(0.058, 0.0, 0.038), o.p(0.246, 0.0, 0.031), 0.018)
    verin(pc, o.p(0.070, 0.0, -0.034), o.p(0.236, 0.0, -0.029), 0.014)
    for st in (0.058, 0.246):
        boite_os(pc, o, st - 0.008, st + 0.008, -0.007, 0.007, -0.034, 0.038, "Chrome", ch=0.002)
    pc = rb.piece(f"Brassard_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(0.110, 0.056), (0.145, 0.066), (0.236, 0.064), (0.266, 0.052)], BRASSARD, 0.009, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)

    # le coude, l'avant-bras à deux os (radius et cubitus), un vérin entre eux, la garde peinte au dehors
    pc = rb.piece(f"Coude_{cote}", ob.nom, "Chrome")
    tambour(pc, ob.p(0.0), ob.u, 0.031, 0.026, n=12, mat="Chrome")
    tambour(pc, ob.p(0.0), ob.u, 0.021, 0.032, n=10, mat="Metal")
    pc = rb.piece(f"AvantBras_{cote}", ob.nom, "Chrome")
    for b in (-0.018, 0.018):
        boite_os(pc, ob, 0.024, 0.244, -0.012, 0.012, b - 0.0075, b + 0.0075, "Chrome", ch=0.002)
    verin(pc, ob.p(0.050, -0.016, 0.0), ob.p(0.220, -0.014, 0.0), 0.010)
    faces = kit.cylindre(pc, ob.p(0.238), ob.p(0.266), 0.027, 8, mat="Chrome")
    kit.chanfreiner(pc, 0.002, faces, 60)
    k = 0.82 if s > 0 else 1.0   # à gauche, sous la manchette du gant
    pc = rb.piece(f"Garde_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(0.050, 0.040 * k), (0.080, 0.047 * k), (0.200, 0.046 * k), (0.232, 0.036 * k)], GARDE, 0.008, "Peinture")
    kit.chanfreiner(pc, 0.002, angle_min=35)


def main_(rb, cote):
    """Une main de squelette : un dos de main d'acier, une paume sombre, des phalanges fines et leurs rotules."""
    o = rb.os(f"{cote}Hand", u=HAUT, v=AVANT)
    pc = rb.piece(f"Paume_{cote}", o.nom, "Chassis")
    kit.loft(pc, [[o.p(0.004, a, b) for a, b in section_rect(-0.014, 0.014, -0.034, 0.026, 0.006)],
                  [o.p(0.100, a, b) for a, b in section_rect(-0.014, 0.012, -0.050, 0.036, 0.006)]], "Chassis")
    pc = rb.piece(f"DosMain_{cote}", o.nom, "Chrome")
    for b in (-0.040, -0.014, 0.012, 0.034):   # les métacarpes
        boite_os(pc, o, 0.012, 0.108, 0.010, 0.020, b - 0.0045, b + 0.0045, "Chrome", ch=0.0015)
    for doigt in DOIGTS:
        for i in (1, 2, 3):
            f = rb.os(f"{cote}Hand{doigt}{i}", u=HAUT, v=AVANT)
            pc = rb.piece(f"{doigt}{i}_{cote}", f.nom, "Chrome")
            fin = f.L + (0.009 if i == 3 else -0.002)
            ep = 0.0062 - 0.0004 * i
            boite_os(pc, f, 0.004, fin, -ep, ep, -0.0055, 0.0055, "Chrome", ch=0.0015)
            kit.cylindre(pc, f.p(0.0, 0.0, -0.0062), f.p(0.0, 0.0, 0.0062), 0.0058, 8, mat="Metal")
    for i in (1, 2, 3):
        f = rb.os(f"{cote}HandThumb{i}", u=HAUT, v=AVANT)
        pc = rb.piece(f"Thumb{i}_{cote}", f.nom, "Chrome")
        fin = f.L + (0.008 if i == 3 else -0.002)
        boite_os(pc, f, 0.003 if i > 1 else -0.006, fin, -0.0068, 0.0068, -0.0068, 0.0068, "Chrome", ch=0.0015)
        if i > 1:
            kit.cylindre(pc, f.p(0.0, -0.0075, 0.0), f.p(0.0, 0.0075, 0.0), 0.0060, 8, mat="Metal")


# --- les jambes --------------------------------------------------------------------------------------

PLAQUE_CUISSE = [(-82, 0.92), (-40, 1.04), (0, 1.12), (40, 1.04), (82, 0.92)]   # la plaque peinte du dehors de la cuisse
QUADRICEPS = [(110, 0.94), (140, 1.06), (172, 0.94)]     # l'acier du dedans et de l'avant de la cuisse
MOLLET = [(212, 0.94), (270, 1.10), (328, 0.94)]         # l'acier du mollet, derrière
ROTULE = [(55, 0.96), (90, 1.28), (125, 0.96)]           # le genou, en pointe devant
TIBIA = [(0, 0.92), (45, 1.06), (90, 1.22), (135, 1.06), (180, 0.92)]   # la garde peinte du tibia, une lame en V


def jambe(rb, cote):
    s = 1 if cote == "Left" else -1
    dehors = Vector((s, 0, 0))
    o = rb.os(f"{cote}UpLeg", u=dehors, v=AVANT)
    ob = rb.os(f"{cote}Leg", u=dehors, v=AVANT)

    pc = rb.piece(f"Hanche_{cote}", o.nom, "Chrome")
    tambour(pc, o.p(0.004), o.u, 0.050, 0.034, n=14, mat="Chrome")
    tambour(pc, o.p(0.004, 0.030), o.u, 0.034, 0.014, n=12, mat="Metal")
    # le fémur, un os d'acier à pans ; le vérin du quadriceps devant, celui des ischios derrière
    pc = rb.piece(f"Femur_{cote}", o.nom, "Chrome")
    tube_profil(pc, o, [(0.040, 0.030), (0.200, 0.026), (0.385, 0.027)], HEXAGONE, "Chrome")
    kit.chanfreiner(pc, 0.002, angle_min=40)
    pc = rb.piece(f"Cuisse_Verins_{cote}", o.nom, "Chassis")
    verin(pc, o.p(0.066, 0.0, 0.052), o.p(0.360, 0.0, 0.044), 0.024)
    verin(pc, o.p(0.080, -0.010, -0.046), o.p(0.330, -0.010, -0.040), 0.017)
    verin(pc, o.p(0.090, -0.040, 0.006), o.p(0.300, -0.030, 0.004), 0.013)   # l'adducteur, en dedans
    for st in (0.066, 0.360):
        boite_os(pc, o, st - 0.009, st + 0.009, -0.008, 0.008, -0.040, 0.052, "Chrome", ch=0.002)
    pc = rb.piece(f"Cuisse_Plaque_{cote}", o.nom, "Peinture")
    coque_profil(pc, o, [(0.070, 0.077), (0.120, 0.086), (0.280, 0.083), (0.345, 0.063)], PLAQUE_CUISSE, 0.010, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    pc = rb.piece(f"Cuisse_Acier_{cote}", o.nom, "Chrome")
    coque_profil(pc, o, [(0.080, 0.070), (0.130, 0.078), (0.280, 0.075), (0.335, 0.058)], QUADRICEPS, 0.008, "Chrome")
    kit.chanfreiner(pc, 0.002, angle_min=35)

    # le genou : une charnière, une rotule peinte
    pc = rb.piece(f"Genou_{cote}", ob.nom, "Chrome")
    tambour(pc, ob.p(0.0), ob.u, 0.048, 0.040, n=12, mat="Chrome")
    tambour(pc, ob.p(0.0), ob.u, 0.030, 0.046, n=12, mat="Metal")
    pc = rb.piece(f"Rotule_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(-0.046, 0.054), (-0.014, 0.064), (0.028, 0.066), (0.058, 0.052)], ROTULE, 0.010, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)

    # le tibia à deux os, le vérin du mollet derrière, la garde peinte devant et sa fente lumineuse
    pc = rb.piece(f"Tibia_{cote}", ob.nom, "Chrome")
    for a in (-0.020, 0.020):
        boite_os(pc, ob, 0.050, 0.400, a - 0.009, a + 0.009, -0.011, 0.011, "Chrome", ch=0.002)
    verin(pc, ob.p(0.080, 0.0, -0.044), ob.p(0.370, 0.0, -0.032), 0.021)
    for st in (0.080, 0.370):
        boite_os(pc, ob, st - 0.008, st + 0.008, -0.006, 0.006, -0.040, 0.0, "Chrome", ch=0.002)
    pc = rb.piece(f"Mollet_{cote}", ob.nom, "Chrome")
    coque_profil(pc, ob, [(0.070, 0.064), (0.130, 0.074), (0.280, 0.069), (0.390, 0.051)], MOLLET, 0.008, "Chrome")
    kit.chanfreiner(pc, 0.002, angle_min=35)
    pc = rb.piece(f"Tibia_Garde_{cote}", ob.nom, "Peinture")
    coque_profil(pc, ob, [(0.085, 0.067), (0.120, 0.075), (0.300, 0.071), (0.395, 0.055)], TIBIA, 0.010, "Peinture")
    kit.chanfreiner(pc, 0.0025, angle_min=35)
    garde = Surface(pc)
    pc = rb.piece(f"Tibia_Lueur_{cote}", ob.nom, "Lueur")
    plaque_sur(pc, garde, [(0.140, -0.0035), (0.280, -0.0028), (0.280, 0.0028), (0.140, 0.0035)], ob.p(0.0), ob.d,
               ob.dir(150), -ob.dir(60), 0.0012, "Lueur", enfoncement=0.0005)


def pied(rb, cote):
    """Un pied de squelette : la cheville, un talon d'acier, trois os du pied et trois orteils, une semelle, et le
    cou-de-pied peint."""
    s = 1 if cote == "Left" else -1
    o = rb.os(f"{cote}Foot", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Cheville_{cote}", o.nom, "Chrome")
    tambour(pc, o.tete, X, 0.032, 0.036, n=12, mat="Chrome")
    pc = rb.piece(f"Talon_{cote}", o.nom, "Chrome")
    loft_y(pc, [(0.092, 0.060, 0.124, 0.050), (0.052, 0.052, 0.134, 0.074), (0.004, 0.054, 0.132, 0.064)], "Chrome",
           lambda xi, xe, zh: [(s * xi, 0.020), (s * xe, 0.020), (s * xe, zh - 0.012), (s * (xe - 0.018), zh),
                               (s * (xi + 0.018), zh), (s * xi, zh - 0.012)])
    kit.chanfreiner(pc, 0.003)
    pc = rb.piece(f"Os_Pied_{cote}", o.nom, "Chrome")
    for x in (0.068, 0.096, 0.124):
        barre(pc, Vector((s * x, 0.004, 0.060)), Vector((s * x, -0.110, 0.034)), 0.016, 0.012)
    pc = rb.piece(f"CouDePied_{cote}", o.nom, "Peinture")
    loft_y(pc, [(0.018, 0.058, 0.134, 0.074), (-0.030, 0.056, 0.140, 0.064), (-0.086, 0.058, 0.140, 0.050)], "Peinture",
           lambda xi, xe, zh: [(s * xi, zh - 0.012), (s * xe, zh - 0.012), (s * xe, zh), (s * (xe - 0.020), zh + 0.010),
                               (s * (xi + 0.020), zh + 0.010), (s * xi, zh)])
    kit.chanfreiner(pc, 0.002)
    pc = rb.piece(f"Semelle_{cote}", o.nom, "Caoutchouc")
    loft_y(pc, [(0.100, 0.052, 0.130, 0.0), (0.072, 0.044, 0.142, 0.0), (-0.112, 0.042, 0.156, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.020), (s * xi, 0.020)])
    kit.chanfreiner(pc, 0.003)
    ot = rb.os(f"{cote}ToeBase", u=Vector((s, 0, 0)), v=AVANT)
    pc = rb.piece(f"Orteils_{cote}", ot.nom, "Chrome")
    for x, bout in ((0.070, -0.188), (0.098, -0.200), (0.126, -0.190)):
        barre(pc, Vector((s * x, -0.112, 0.034)), Vector((s * x, bout, 0.026)), 0.017, 0.012)
        tambour(pc, Vector((s * x, -0.112, 0.034)), X, 0.008, 0.009, n=8, mat="Metal")
    pc = rb.piece(f"Orteils_Semelle_{cote}", ot.nom, "Caoutchouc")
    loft_y(pc, [(-0.113, 0.054, 0.142, 0.0), (-0.204, 0.060, 0.136, 0.0)], "Caoutchouc",
           lambda xi, xe, _: [(s * xi, 0.0), (s * xe, 0.0), (s * xe, 0.018), (s * xi, 0.018)])
    kit.chanfreiner(pc, 0.003)


# --- le robot ---------------------------------------------------------------------------------------------

def corps(rb, variante=None):
    tete(rb)
    cou(rb)
    thorax(rb)
    colonne(rb)
    ventre(rb)
    bassin(rb)
    for cote in ("Left", "Right"):
        bras(rb, cote)
        main_(rb, cote)
        jambe(rb, cote)
        pied(rb, cote)


SKINS = {"vierge": dict(titre="VIERGE", usure=PROPRE, genres=set())}
MODELE = cr.Modele(CODE, PREFIXE, corps, skins=SKINS, ecarts_voulus={"LeftArm": 0.022})

if __name__ == "__main__":
    RESULTAT = cr.principal(MODELE)
