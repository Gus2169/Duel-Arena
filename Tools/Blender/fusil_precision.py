"""Fusil de précision FP-12 de Duel Arena (Blender 5.2, low poly).

Un fusil à verrou sur châssis, au canon lourd. Le joueur vise en lunette plein écran (GDD § 6) :
la lunette ×4 est montée d'origine, mais c'est un objet à part (« Lunette_Standard »), qu'un autre
viseur de l'armurerie remplacera. Pas de mire de secours.

Pour les animations : le chargeur de cinq coups est dans le puits où la main gauche du clip de
rechargement va le chercher, et la manette de culasse est à gauche, en haut de la carcasse, là où
elle arme (une culasse à tirage rectiligne, d'où la manette relevée).

Le FP-12 a beaucoup tiré : encoches de victoires par paquets, bouton de culasse perdu remplacé par
un écrou soudé, appui-joue récupéré à l'apprêt rouge et rembourré au ruban, bonnette de la lunette
fendue tenue au ruban.
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
import commun_armes as ca  # noqa: E402

importlib.reload(ca)
kit = ca.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

H_AXE = 0.112
AV_BOUCHE = 0.860
H_RAIL = 0.1455         # sommet des dents du rail du dessus, d'un seul tenant de la carcasse au garde-main
H_LUNETTE = 0.178       # axe de la lunette
AV_OCULAIRE = -0.085    # arrière de la bonnette : la mire

# Section du garde-main (x, h), octogonale autour de l'axe du canon.
R_GM = 0.025
SECTION_GM = [(math.cos(math.pi / 8 + i * math.pi / 4) * R_GM, H_AXE + math.sin(math.pi / 8 + i * math.pi / 4) * R_GM)
              for i in range(8)]
# Section arrière du garde-main, là où il rejoint la carcasse : la largeur du châssis, le haut du rail.
# Mêmes huit points, dans le même ordre que l'octogone, pour que le loft passe de l'une à l'autre.
SECTION_JONCTION = [(0.0215, H_AXE + 0.0150), (0.0150, H_AXE + 0.0225), (-0.0150, H_AXE + 0.0225),
                    (-0.0215, H_AXE + 0.0150), (-0.0215, H_AXE - 0.0110), (-0.0150, H_AXE - 0.0150),
                    (0.0150, H_AXE - 0.0150), (0.0215, H_AXE - 0.0110)]
AV_CARCASSE = 0.205   # avant de la carcasse : le garde-main part de là

DOS, FACE = ca.courbes_chargeur((0.172, 0.052), (0.174, 0.0), (0.178, -0.055),
                                (0.236, 0.052), (0.238, 0.0), (0.242, -0.050), n=4)


def chassis(a):
    # Châssis : le bloc d'alu qui porte la carcasse, avec son puits de chargeur.
    pc = a.piece("Chassis", "Peinture")
    f = kit.prisme(pc, [(-0.088, 0.060), (-0.088, 0.098), (-0.078, 0.104), (0.318, 0.104),
                        (0.318, 0.076), (0.262, 0.048), (-0.050, 0.048)], -0.0215, 0.0215)
    kit.chanfreiner(pc, 0.0022, f)
    f = kit.prisme(pc, [(0.160, 0.050), (0.160, 0.030), (0.168, 0.024), (0.246, 0.024), (0.254, 0.032),
                        (0.254, 0.050)], -0.0235, 0.0235)    # puits de chargeur
    kit.chanfreiner(pc, 0.002, f)
    ca.pontet(pc, [(0.162, 0.038), (0.157, 0.012), (0.141, 0.001), (0.046, 0.001), (0.027, 0.013)])
    ca.detente(pc, 0.064, 0.050)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.020, 0.075, 0.130, 0.270):   # allègements sur les flancs
        kit.prisme(cp, [(av, 0.070), (av + 0.040, 0.070), (av + 0.034, 0.091), (av + 0.006, 0.091)], 0.0175, 0.030)
        kit.prisme(cp, [(av, 0.070), (av + 0.040, 0.070), (av + 0.034, 0.091), (av + 0.006, 0.091)], -0.030, -0.0175)
    a.couper(ob, cp, auto=True)   # châssis, puits, pontet et détente se chevauchent

    # Carcasse (le boîtier de culasse), cylindrique, au-dessus du châssis ; fenêtre d'éjection à droite.
    pc = a.piece("Carcasse", "Acier")
    f = kit.tranches(pc, P(-0.078, H_AXE), P(0.212, H_AXE), [0.0160, 0.0175, 0.0175, 0.0168], n=10,
                     positions=[0.0, 0.03, 0.97, 1.0], mat="Acier")
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.030, 0.098, H_AXE - 0.004, H_AXE + 0.0125, -0.030, -0.008)
    a.couper(ob, cp)

    # Culasse : manette relevée à gauche ; son bouton perdu, remplacé par un écrou soudé.
    pc = a.piece("Culasse", "Acier")
    racine = P(0.008, H_AXE + 0.006, 0.016)
    coude = P(0.004, H_AXE + 0.018, 0.034)
    bout = P(-0.002, H_AXE + 0.024, 0.047)
    f = kit.balayage(pc, [racine, coude, bout], kit.section_cercle(0.0034, 8), "Acier")
    kit.vis_six_pans(pc, bout - Vector((0.002, 0, 0)), GAUCHE, r=0.0068, h=0.0070, mat="AcierBrut", phase=0.0)
    anneau = kit.cercle(bout + Vector((0.0005, 0, 0)), GAUCHE, 0.0042, 10)
    kit.soudure(pc, anneau + [anneau[0]], r=0.0013, mat="Soudure", rng=a.rng, pas=0.0016, cotes=5)
    # sûreté et bouton de chargeur
    f = kit.boite(pc, -0.004, 0.010, 0.097, 0.104, -0.0225, -0.0190, "Acier")      # sûreté (droite)
    kit.cylindre(pc, P(0.252, 0.040, 0.0235), P(0.252, 0.040, 0.0270), 0.0042, 8)
    a.objet(pc)

    # Rails : dessus d'un seul tenant, de la carcasse au bout du garde-main (lunette, viseurs) ;
    # dessous (bipied, poignée) et à droite (laser tactique). Sur la carcasse, une embase le porte à la hauteur du garde-main.
    pc = a.piece("Rails", "Peinture")
    f = kit.boite(pc, -0.072, AV_CARCASSE + 0.008, H_AXE + 0.0150, H_AXE + 0.0220, -0.0105, 0.0105)
    kit.chanfreiner(pc, 0.0008, f)
    ca.rail(pc, -0.072, 0.640, P(-0.072, H_AXE + 0.0215), "haut")
    ca.rail(pc, 0.460, 0.620, P(0.460, H_AXE - R_GM * 0.92 + 0.0010), "bas")
    ca.rail(pc, 0.500, 0.580, P(0.500, H_AXE, -R_GM * 0.92 + 0.0010), "droite")
    a.objet(pc)


def garde_main(a):
    pc = a.piece("Garde_Main", "Peinture")
    # il part de l'avant de la carcasse, à la largeur du châssis, puis s'affine en octogone
    sections = [(AV_CARCASSE, SECTION_JONCTION), (0.298, SECTION_JONCTION), (0.338, SECTION_GM), (0.630, SECTION_GM),
                (0.645, kit.mise_a_echelle(SECTION_GM, 0.86, (0, H_AXE)))]
    f = kit.loft(pc, kit.sections_av(sections), "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.222, 0.258, 0.345, 0.385, 0.425, 0.465):   # fentes des deux côtés (moins à droite : le rail)
        kit.boite(cp, av, av + 0.026, H_AXE - 0.0055, H_AXE + 0.0055, 0.016, 0.032)
        if av < 0.48:
            kit.boite(cp, av, av + 0.026, H_AXE - 0.0055, H_AXE + 0.0055, -0.032, -0.016)
    a.couper(ob, cp)


def canon(a):
    pc = a.piece("Canon", "Bouche")   # il sort du garde-main ; le reste est caché dedans
    kit.tranches(pc, P(0.630, H_AXE), P(0.800, H_AXE), [0.0112, 0.0112, 0.0098], n=12, positions=[0, 0.25, 1])
    a.objet(pc)
    pc = a.piece("Frein_Bouche", "Bouche")
    f = kit.tranches(pc, P(0.795, H_AXE), P(AV_BOUCHE, H_AXE), [0.0120, 0.0158, 0.0158, 0.0130], n=8,
                     positions=[0.0, 0.08, 0.88, 1.0])
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.808, 0.826, 0.844):
        kit.boite(cp, av, av + 0.010, H_AXE - 0.0085, H_AXE + 0.0085, -0.030, 0.030)
    kit.cylindre(cp, P(0.850, H_AXE), P(0.875, H_AXE), 0.0050, 8)
    a.couper(ob, cp, auto=True)


def lunette(a):
    """La lunette ×4 d'origine, ses colliers compris : un groupe à part, remplaçable."""
    groupe = a.vide(a.nom("Lunette_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Lunette", "Peinture")
    profil = [(-0.075, 0.0205), (-0.042, 0.0205), (-0.030, 0.0160), (0.050, 0.0160), (0.054, 0.0186), (0.106, 0.0186),
              (0.110, 0.0160), (0.178, 0.0160), (0.205, 0.0255), (0.245, 0.0255)]
    t0, t1 = profil[0][0], profil[-1][0]
    f = kit.tranches(pc, P(t0, H_LUNETTE), P(t1, H_LUNETTE), [r for _, r in profil], n=12, phase=0.0,
                     positions=[(av - t0) / (t1 - t0) for av, _ in profil])
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    # tourelles : hausse dessus, dérive à droite, parallaxe à gauche
    f = kit.cylindre(pc, P(0.080, H_LUNETTE + 0.0170), P(0.080, H_LUNETTE + 0.0335), 0.0102, 12)
    f += kit.cylindre(pc, P(0.080, H_LUNETTE, -0.0170), P(0.080, H_LUNETTE, -0.0310), 0.0092, 12)
    f += kit.cylindre(pc, P(0.080, H_LUNETTE, 0.0170), P(0.080, H_LUNETTE, 0.0280), 0.0082, 12)
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    kit.disque(pc, P(t0 - 0.0002, H_LUNETTE), Vector((0, 1, 0)), 0.0176, 12, mat="Lentille", rotation=0.0)
    kit.disque(pc, P(t1 + 0.0002, H_LUNETTE), AVANT, 0.0228, 12, mat="Lentille", rotation=0.0)
    # colliers et embases sur le rail
    for av in (0.010, 0.140):
        f = kit.boite(pc, av - 0.009, av + 0.009, H_RAIL - 0.0015, H_LUNETTE - 0.012, -0.0125, 0.0125)
        kit.chanfreiner(pc, 0.0008, f)
        kit.anneau(pc, av - 0.009, av + 0.009, H_LUNETTE, 0.0196, 0.01595, 12, "Peinture")
        kit.cylindre(pc, P(av, H_RAIL + 0.004, -0.0125), P(av, H_RAIL + 0.004, -0.0185), 0.0040, 6, mat="Acier")
    a.objet(pc).parent = groupe

    pc = a.piece("Lunette_Bonnette", "Caoutchouc")
    kit.anneau(pc, AV_OCULAIRE, -0.0745, H_LUNETTE, 0.0212, 0.0178, 12, "Caoutchouc", phase=0.0)
    # bonnette fendue, tenue au ruban
    contour = [(math.cos(2 * math.pi * i / 12) * 0.0212, math.sin(2 * math.pi * i / 12) * 0.0212) for i in range(12)]
    kit.bande(pc, contour, P(-0.080, 0.0) + Vector((0, 0, H_LUNETTE)), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT,
              0.0065, 0.0010, "Ruban", inclinaison=0.12, irregularite=0.0004, rng=a.rng)
    a.objet(pc).parent = groupe

    pc = a.piece("Lunette_Marquage", "Marquage")
    a.texte(pc, "300 M", 0.0042, P(0.140, H_LUNETTE + 0.0005, 0.0163), ca.VERS_CROSSE, HAUT, resolution=1)
    a.objet(pc, angle=None).parent = groupe


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Charniere_Crosse", "Acier")   # la crosse se replie : sa charnière reste sur le châssis
    f = kit.boite(pc, -0.096, -0.086, 0.064, 0.118, -0.0175, 0.0175)
    kit.chanfreiner(pc, 0.0012, f)
    kit.cylindre(pc, P(-0.091, 0.112, 0.0195), P(-0.091, 0.112, -0.0195), 0.0045, 8)
    a.objet(pc)

    pc = a.piece("Crosse_Corps", "Peinture")
    f = kit.prisme(pc, [(-0.094, 0.120), (-0.110, 0.124), (-0.318, 0.124), (-0.325, 0.118), (-0.325, 0.016),
                        (-0.312, 0.008), (-0.270, 0.020), (-0.140, 0.056), (-0.094, 0.064)], -0.018, 0.018)
    kit.chanfreiner(pc, 0.0025, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Peinture")
    kit.prisme(cp, [(-0.150, 0.069), (-0.165, 0.108), (-0.300, 0.110), (-0.306, 0.100), (-0.306, 0.036),
                    (-0.287, 0.031)], -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    appui = "Primaire"   # récupéré sur une autre arme
    pc = a.piece("Appui_Joue", appui)
    f = kit.prisme(pc, [(-0.292, 0.1255), (-0.140, 0.1255), (-0.150, 0.1425), (-0.282, 0.1425)], -0.0155, 0.0155)
    kit.chanfreiner(pc, 0.0022, f)
    for av in (-0.170, -0.262):
        kit.cylindre(pc, P(av, 0.118), P(av, 0.1265), 0.0045, 8, mat="Acier")
    # rembourré de ruban : les robots imitent les gestes des humains
    contour = kit.contour_rect(0.031, 0.017, chanfrein=0.0025, cy=0.134)
    for av, incl in ((-0.255, 0.06), (-0.232, -0.04), (-0.209, 0.05)):
        kit.bande(pc, contour, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.017, 0.0012, "Ruban",
                  inclinaison=incl, irregularite=0.0006, rng=a.rng)
    a.objet(pc).parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.325, 0.128), (-0.334, 0.124), (-0.337, 0.110), (-0.337, 0.018), (-0.333, 0.006),
                        (-0.325, 0.004)], -0.0205, 0.0205)
    kit.chanfreiner(pc, 0.003, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Marquages", "Marquage")
    ca.mire_crash(pc, P(-0.3155, 0.070, 0.0184), GAUCHE, r=0.0085)
    ca.encoches(pc, -0.172, 0.1135, 0.1205, 0.0183, 13)
    a.objet(pc, angle=None).parent = groupe


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0095, P(0.140, 0.059, 0.0215), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 1204", 0.0045, P(0.205, 0.060, -0.0215), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    a.objet(pc, angle=None)


def construire(a):
    chassis(a)
    ca.poignee_pistolet(a, ruban=False)
    ca.chargeur(a, DOS, FACE, 0.0140, mat_semelle="Acier")
    garde_main(a)
    canon(a)
    lunette(a)
    crosse(a)
    marquages(a)
    a.reperes(P(AV_OCULAIRE, H_LUNETTE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.380, 0.0890))
    a.accroches({
        "Viseur": P(0.075, H_RAIL),
        "Laser": P(0.540, H_AXE, -0.0350),
        "Poignee": P(0.540, H_AXE - 0.0350),
        "Chargeur": P(0.204, 0.052),
        "Crosse": P(-0.091, 0.092),
    })


VUES = [
    ("detail_culasse", P(-0.06, 0.24, 0.20), P(0.02, 0.13, 0.02), 45),
    ("detail_crosse", P(-0.22, 0.18, 0.25), P(-0.22, 0.09, 0.0), 50),
]

ATELIER = ca.principal("FP-12", "fusil de précision", "Fusil de précision", construire, (AV_BOUCHE, H_AXE), vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
