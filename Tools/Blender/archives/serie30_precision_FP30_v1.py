"""Fusil de précision FP-30 de Duel Arena (Blender 5.2, low poly) : la série 30.

La série 30 est la dernière dotation d'armes à balles du complexe (voir serie30_fusil.py). Le
FP-30 (demande de l'utilisateur, 2026-10-15 : « un sniper type Winchester », d'après le 30-30
Repeater d'Apex Legends) : une carabine à levier de sous-garde, canon à huit pans, tube sous le
canon, longuesse blanc cassé, boîte de culasse anthracite à plaques de laiton et anneau de selle,
chien apparent, grand levier en boucle, crosse ajourée et ses cartouches de rechange, lunette ancienne sur bagues de
laiton (« Lunette_Standard », remplaçable par un autre viseur).

Il a servi : la boucle du levier gainée de ruban toilé, la bague avant du tube perdue et remplacée
par du fil de fer, des bâtons de victoires au marqueur sur la crosse.

Pour les animations, il garde les points de prise du FP-12 : poignée à l'origine, chargeur de cinq
coups dans le puits où la main gauche du clip de rechargement va le chercher (une boîte, comme sur
les carabines à levier modernes), verrou en haut à gauche au-dessus de la poignée, longuesse sous
la main gauche, crosse à l'épaule.

  blender -b --factory-startup -P serie30_precision.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
"""

import importlib
import os
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_armes as ca  # noqa: E402

importlib.reload(ca)
kit = ca.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

H_AXE = 0.112           # comme le FP-12
H_TUBE = 0.0865         # le tube sous le canon
AV_BOUCHE = 0.842
H_RAIL = 0.152          # sommet des dents du rail de la boîte de culasse
H_LUNETTE = 0.178
AV_OCULAIRE = -0.096    # arrière de la bonnette : la mire

DOS, FACE = ca.courbes_chargeur((0.172, 0.052), (0.174, 0.0), (0.178, -0.055),
                                (0.236, 0.052), (0.238, 0.0), (0.242, -0.050), n=4)
LONGUESSE = [(0.252, ca.section8(0.0195, 0.068, 0.1265, 0.006, 0.009, 0.75)),
             (0.466, ca.section8(0.0195, 0.068, 0.1265, 0.006, 0.009, 0.75)),
             (0.476, ca.section8(0.0180, 0.072, 0.1250, 0.005, 0.008, 0.75))]
# La boucle du levier : pontet devant la détente, puis la boucle des doigts devant la poignée.
LEVIER = [P(0.152, 0.046), P(0.147, 0.006), P(0.128, -0.006), P(0.062, -0.008), P(0.042, -0.016),
          P(0.035, -0.048), P(0.024, -0.078), P(0.006, -0.090), P(-0.008, -0.086)]
# La crosse : barre du haut, talon, jambe qui rejoint le pied de la poignée, montant devant ;
# ajourée en son milieu (AJOUR_CROSSE).
PROFIL_CROSSE = [(-0.058, 0.140), (-0.280, 0.132), (-0.296, 0.126), (-0.302, 0.114), (-0.302, 0.020),
                 (-0.294, 0.008), (-0.270, 0.006), (-0.062, -0.074), (-0.050, -0.083), (-0.040, -0.070),
                 (-0.036, -0.040), (-0.022, 0.012), (-0.012, 0.053), (-0.058, 0.060)]
AJOUR_CROSSE = [(-0.084, -0.028), (-0.082, 0.040), (-0.100, 0.098), (-0.120, 0.110), (-0.236, 0.110),
                (-0.252, 0.096), (-0.256, 0.040), (-0.244, 0.020), (-0.200, 0.000), (-0.100, -0.040)]
PLAQUE = [(0.004, 0.064), (0.140, 0.064), (0.150, 0.074), (0.150, 0.124), (0.014, 0.124), (0.004, 0.114)]


def boite(a):
    pc = a.piece("Boite_Culasse", "Peinture")
    f = kit.prisme(pc, [(-0.060, 0.058), (-0.060, 0.128), (-0.050, 0.140), (0.256, 0.140), (0.262, 0.134),
                        (0.262, 0.074), (0.255, 0.052), (0.150, 0.050), (0.000, 0.050), (-0.040, 0.054)],
                   -0.0190, 0.0190, "Peinture")
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.prisme(pc, [(0.160, 0.052), (0.160, 0.032), (0.168, 0.026), (0.246, 0.026), (0.254, 0.034),
                        (0.254, 0.052)], -0.0215, 0.0215, "Peinture")   # puits du chargeur
    kit.chanfreiner(pc, 0.0018, f)
    ca.detente(pc, 0.064, 0.052)
    a.objet(pc)

    pc = a.piece("Plaques_Laiton", "Laiton")   # les plaques des flancs ; à droite, la porte de chargement
    for x0, x1 in ((0.0185, 0.0202), (-0.0202, -0.0185)):
        f = kit.prisme(pc, PLAQUE, x0, x1, "Laiton")
        kit.chanfreiner(pc, 0.0006, f)
    f = kit.prisme(pc, [(0.096, 0.078), (0.136, 0.078), (0.140, 0.086), (0.136, 0.094), (0.096, 0.094)],
                   -0.0212, -0.0200, "Laiton")
    kit.chanfreiner(pc, 0.0004, f)
    kit.vis_six_pans(pc, P(0.100, 0.086, -0.0212), -GAUCHE, r=0.0018, h=0.0008, mat="AcierBrut")
    a.objet(pc)

    pc = a.piece("Commandes", "Laiton")
    # le chien, apparent à l'arrière de la boîte de culasse, et son axe
    f = kit.prisme(pc, [(-0.040, 0.128), (-0.030, 0.140), (-0.036, 0.152), (-0.044, 0.156), (-0.050, 0.152),
                        (-0.046, 0.146), (-0.052, 0.142), (-0.050, 0.134)], -0.0035, 0.0035, "Laiton")
    kit.chanfreiner(pc, 0.0005, f)
    kit.cylindre(pc, P(-0.042, 0.118, 0.0196), P(-0.042, 0.118, -0.0196), 0.0022, 8, mat="AcierBrut")
    # le verrou, en haut à gauche : là où la main gauche du clip arme
    f = kit.boite(pc, 0.010, 0.028, 0.1240, 0.1340, 0.0180, 0.0262, "Acier")
    kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(0.019, 0.129, 0.0262), P(0.019, 0.129, 0.0312), 0.0050, 8, mat="Acier")
    # le levier : le pontet devant la détente, puis la boucle des doigts
    f = kit.balayage(pc, kit.lisser_chemin(LEVIER, sous_div=3), kit.section_rect(0.0120, 0.0085), "Laiton",
                     normale=GAUCHE)
    kit.chanfreiner(pc, 0.0008, f)
    kit.cylindre(pc, P(-0.006, -0.084, 0.0080), P(-0.006, -0.084, -0.0080), 0.0026, 8, mat="AcierBrut")   # axe
    # l'anneau de selle, à gauche de la boîte de culasse, et son piton
    kit.cylindre(pc, P(-0.030, 0.0790, 0.0188), P(-0.030, 0.0790, 0.0222), 0.0026, 6, mat="Acier")
    boucle = kit.cercle(P(-0.030, 0.0690, 0.0218), GAUCHE, 0.0100, 12)
    kit.balayage(pc, boucle, kit.section_cercle(0.0014, 5), "Laiton", ferme=True)
    a.objet(pc)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, -0.030, 0.250, P(-0.030, 0.140), "haut", mat="Polymere")
    a.objet(pc)


def canon(a):
    pc = a.piece("Canon", "Bouche")   # à huit pans
    kit.tranches(pc, P(0.250, H_AXE), P(AV_BOUCHE, H_AXE), [0.0118, 0.0118, 0.0108], n=8,
                 positions=[0.0, 0.97, 1.0], mat="Bouche")
    kit.disque(pc, P(AV_BOUCHE + 0.0002, H_AXE), AVANT, 0.0052, 8, mat="Interieur")
    # guidon sur rampe, à perle de laiton
    f = kit.prisme(pc, [(0.808, 0.1230), (0.828, 0.1230), (0.828, 0.1290), (0.818, 0.1320), (0.810, 0.1300)],
                   -0.0020, 0.0020, "Acier")
    kit.chanfreiner(pc, 0.0004, f)
    kit.disque(pc, P(0.8099, 0.1305), Vector((0, 1, 0)), 0.0016, 6, mat="Laiton")
    a.objet(pc)

    pc = a.piece("Tube", "Acier")
    kit.cylindre(pc, P(0.250, H_TUBE), P(0.700, H_TUBE), 0.0090, 10)
    f = kit.cylindre(pc, P(0.700, H_TUBE), P(0.708, H_TUBE), 0.0094, 10, mat="Laiton")   # bouchon
    kit.chanfreiner(pc, 0.0006, f, angle_min=60)
    contour = kit.contour_rect(0.0280, 0.0500, chanfrein=0.0085, cy=0.1005)            # bague, devant la longuesse
    f = kit.loft(pc, [[P(0.476, h, x) for x, h in contour], [P(0.488, h, x) for x, h in contour]], "Laiton")
    kit.chanfreiner(pc, 0.0006, f)
    a.objet(pc)


def longuesse(a):
    pc = a.piece("Longuesse", "PeintureClaire")
    mats = ca.mats8(chanfrein_haut="Peinture", flancs="PeintureClaire", chanfrein_bas="Peinture", dessous="Polymere")
    f = kit.loft(pc, kit.sections_av(LONGUESSE), "PeintureClaire", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.prisme(pc, [(0.436, 0.0690), (0.470, 0.0690), (0.466, 0.0560), (0.446, 0.0440)], -0.0090, 0.0090,
                   "Polymere")                                                     # arrêt de main
    kit.chanfreiner(pc, 0.0010, f)
    ca.rail(pc, 0.395, 0.445, P(0.395, 0.100, -0.0190), "droite", largeur=0.012, epaisseur=0.003, dent=0.003,
            pas=0.008, longueur_dent=0.004, mat="Polymere")                        # petit rail (laser tactique)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.292, 0.440, 0.1015, 0.1070, 0.0130, 0.030)                    # rainure, côté joueur
    a.couper(ob, cp, auto=True)


def lunette(a):
    """La lunette ancienne, sur bagues de laiton : un groupe à part, remplaçable."""
    groupe = a.vide(a.nom("Lunette_Standard"), a.racine, "CUBE", 0.004)
    hl = H_LUNETTE
    pc = a.piece("Lunette", "Peinture")
    profil = [(-0.088, 0.0170), (-0.066, 0.0170), (-0.056, 0.0125), (0.148, 0.0125), (0.172, 0.0190), (0.224, 0.0190)]
    t0, t1 = profil[0][0], profil[-1][0]
    f = kit.tranches(pc, P(t0, hl), P(t1, hl), [r for _, r in profil], n=12, phase=0.0,
                     positions=[(av - t0) / (t1 - t0) for av, _ in profil], mat="Peinture")
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    for cote in (1, -1):   # tourelles : hausse dessus, dérive à droite
        if cote > 0:
            p0, p1, p2 = P(0.050, hl + 0.0115), P(0.050, hl + 0.0220), P(0.050, hl + 0.0245)
        else:
            p0, p1, p2 = P(0.050, hl, -0.0115), P(0.050, hl, -0.0220), P(0.050, hl, -0.0245)
        f = kit.cylindre(pc, p0, p1, 0.0078, 10, mat="Peinture")
        f += kit.cylindre(pc, p1, p2, 0.0080, 10, mat="Laiton")
        kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    for av in (0.000, 0.112):   # bagues de laiton et leurs embases
        f = kit.anneau(pc, av - 0.006, av + 0.006, hl, 0.0150, 0.0124, 12, "Laiton", phase=0.0)
        f += kit.boite(pc, av - 0.008, av + 0.008, H_RAIL - 0.0015, hl - 0.0120, -0.0080, 0.0080, "Laiton")
        kit.chanfreiner(pc, 0.0006, f)
        kit.vis_six_pans(pc, P(av, H_RAIL + 0.006, -0.0080), -GAUCHE, r=0.0020, h=0.0010, mat="AcierBrut")
    kit.disque(pc, P(t1 + 0.0002, hl), AVANT, 0.0175, 12, mat="Lentille", rotation=0.0)
    a.objet(pc).parent = groupe
    pc = a.piece("Lunette_Bonnette", "Caoutchouc")
    kit.anneau(pc, AV_OCULAIRE, -0.087, hl, 0.0178, 0.0145, 12, "Caoutchouc", phase=0.0)
    kit.disque(pc, P(-0.0882, hl), Vector((0, 1, 0)), 0.0150, 12, mat="Lentille", rotation=0.0)
    a.objet(pc).parent = groupe


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "PeintureClaire")
    f = kit.prisme(pc, PROFIL_CROSSE, -0.0190, 0.0190, "PeintureClaire")
    kit.chanfreiner(pc, 0.0025, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Peinture")   # l'ajour, ses parois en anthracite
    kit.prisme(cp, AJOUR_CROSSE, -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.302, 0.134), (-0.311, 0.130), (-0.314, 0.118), (-0.314, 0.016), (-0.310, 0.006),
                        (-0.302, 0.004)], -0.0215, 0.0215)
    kit.chanfreiner(pc, 0.003, f)
    a.objet(pc).parent = groupe

    # quatre cartouches de rechange dans leur passant, sur le talon, côté joueur
    pc = a.piece("Cartouches", "Laiton")
    for av in (-0.266, -0.276, -0.286, -0.296):
        kit.cylindre(pc, P(av, 0.040, 0.0235), P(av, 0.080, 0.0235), 0.0046, 8, mat="Laiton")
        kit.cylindre(pc, P(av, 0.080, 0.0235), P(av, 0.091, 0.0235), 0.0040, 8, r1=0.0012, mat="Bobine")
    f = kit.boite(pc, -0.301, -0.260, 0.050, 0.062, 0.0185, 0.0298, "Polymere")
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc).parent = groupe


def rafistolages(a):
    # la boucle du levier, gainée de ruban toilé
    pc = a.piece("Ruban_Levier", "RubanNoir")
    p0, p1 = Vector(LEVIER[5]), Vector(LEVIER[6])
    d = (p1 - p0).normalized()
    ey = Vector((0.0, -d.z, d.y))
    for k, incl in ((0.25, 0.05), (0.50, -0.06), (0.75, 0.04)):
        kit.bande(pc, kit.contour_rect(0.0138, 0.0101, chanfrein=0.0014), p0.lerp(p1, k), Vector((1, 0, 0)), ey, d,
                  0.0075, 0.0010, "RubanNoir", inclinaison=incl, irregularite=0.0003, rng=a.rng)
    a.objet(pc)
    # la bague avant du tube perdue, remplacée par trois tours de fil de fer
    pc = a.piece("Fil_Tube", "Fil")
    contour = kit.contour_rect(0.0262, 0.0470, chanfrein=0.0080, cy=0.1005)
    for k in (-0.0030, 0.0, 0.0030):
        kit.bande(pc, contour, P(0.690 + k, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0013, 0.0010,
                  "Fil", inclinaison=0.04, irregularite=0.0002, rng=a.rng)
    kit.cylindre(pc, P(0.690, 0.1240, 0.0040), P(0.686, 0.1300, 0.0055), 0.0009, 5, mat="Fil")   # torsade
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "MarquageNoir")
    a.texte(pc, a.code, 0.0100, P(0.077, 0.094, 0.0203), ca.VERS_CROSSE, HAUT, mat="MarquageNoir")
    a.texte(pc, "N° 3030", 0.0045, P(0.050, 0.114, -0.0203), ca.VERS_BOUCHE, HAUT, mat="MarquageNoir",
            decalage=0.0003, resolution=1)
    ca.mire_crash(pc, P(-0.060, 0.010, 0.0192), GAUCHE, r=0.0065)
    ca.encoches(pc, -0.130, 0.1180, 0.1265, 0.0192, 8, mat="MarquageNoir", largeur=0.0014, pas=0.0035)
    a.objet(pc, angle=None)


def construire(a):
    boite(a)
    ca.poignee_pistolet(a, ruban=False)
    ca.chargeur(a, DOS, FACE, 0.0135, mat="Polymere", mat_semelle="Laiton", bande=False)
    canon(a)
    longuesse(a)
    lunette(a)
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_OCULAIRE, H_LUNETTE), P(AV_BOUCHE, H_AXE))
    a.accroches({
        "Viseur": P(0.056, H_RAIL),                 # rail de la boîte de culasse
        "Laser": P(0.420, 0.100, -0.0250),          # petit rail à droite de la longuesse
        "Poignee": P(0.360, 0.066),                 # sous la longuesse
        "Chargeur": P(0.204, 0.052),                # haut du chargeur, dans le puits
        "Crosse": P(-0.060, 0.100),
    })


VUES = [
    ("detail_levier", P(0.05, -0.02, 0.19), P(0.05, -0.02, 0.0), 45),
    ("detail_crosse", P(-0.22, 0.06, 0.26), P(-0.22, 0.05, 0.0), 45),
    ("detail_lunette", P(0.06, 0.24, 0.17), P(0.06, 0.17, 0.0), 45),
]

ATELIER = ca.principal("FP-30", "fusil de précision série 30", "Fusil de précision série 30", construire,
                       (AV_BOUCHE, H_AXE), vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
