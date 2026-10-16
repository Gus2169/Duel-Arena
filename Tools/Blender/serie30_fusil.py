"""Fusil d'assaut FA-30 de Duel Arena (Blender 5.2, low poly) : la série 30.

La série 30 est la dernière dotation d'armes à balles du complexe : des modèles de série récents,
aux formes modernes et anguleuses (références de l'utilisateur, 2026-10-15 : pour ce fusil, le
R-301 d'Apex Legends), retrouvés dans leurs caisses et en service depuis. Deux tons : peinture
blanc cassé, aplats anthracite et noir ; aucune lueur, ce sont des armes à balles.

Le FA-30 : un corps long et fin d'un seul trait, un garde-main ajouré d'une ferme de triangles, un
bec au-dessus de la bouche, un chargeur droit incliné vers l'arrière, une crosse squelette noire.
Il a servi : la crosse réglable bloquée par un collier de serrage (son verrou a cassé), le bec
écorné, des bâtons de victoires au marqueur sur le garde-main.

Pour les animations, il garde les points de prise du FA-07 : poignée à l'origine, puits du
chargeur où la main gauche du clip de rechargement va le chercher, levier d'armement en haut à
gauche au-dessus de la poignée, dessous du garde-main sous la main gauche, crosse à l'épaule.

  blender -b --factory-startup -P serie30_fusil.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.138          # comme le FA-07
AV_BOUCHE = 0.715
H_RAIL = 0.1822
H_VISEE = 0.2065
AV_HAUSSE = -0.048
AV_GUIDON = 0.540

# Chargeur droit, incliné vers l'arrière (son pied vers la poignée).
DOS, FACE = ca.courbes_chargeur((0.178, 0.034), (0.168, -0.030), (0.152, -0.082),
                                (0.251, 0.034), (0.241, -0.026), (0.226, -0.076))

RECEPTEUR = [(-0.095, ca.section8(0.0270, 0.108, 0.170, 0.008, 0.003)),
             (0.300, ca.section8(0.0280, 0.108, 0.170, 0.008, 0.003))]
# Garde-main : le dessous remonte vers l'avant, en coin.
GARDE_MAIN = [(0.290, ca.section8(0.0300, 0.064, 0.170, 0.009, 0.012, 0.60)),
              (0.520, ca.section8(0.0300, 0.068, 0.170, 0.009, 0.012, 0.60)),
              (0.590, ca.section8(0.0270, 0.094, 0.168, 0.008, 0.010, 0.70)),
              (0.602, ca.section8(0.0240, 0.102, 0.164, 0.007, 0.008, 0.75))]
# Bandeau anthracite le long de la carcasse, qui monte vers la crosse.
BANDEAU = [(0.250, 0.1110), (0.140, 0.1110), (0.040, 0.1170), (-0.030, 0.1270), (-0.086, 0.1340),
           (-0.086, 0.1580), (-0.030, 0.1500), (0.040, 0.1400), (0.140, 0.1350), (0.250, 0.1350)]


def carcasse(a):
    pc = a.piece("Carcasse_Haute", "PeintureClaire")
    f = kit.loft(pc, kit.sections_av(RECEPTEUR), "PeintureClaire", mats_cotes=ca.mats8(dessous="Peinture"))
    kit.chanfreiner(pc, 0.0018, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    port = (0.072, 0.150, 0.131, 0.155, -0.040, -0.017)
    kit.boite(cp, *port)                                                  # fenêtre d'éjection (droite)
    kit.boite(cp, -0.030, 0.068, 0.1515, 0.1605, 0.021, 0.040)           # fente du levier d'armement
    a.couper(ob, cp)

    pc = a.piece("Bandeaux", "Peinture")   # des deux côtés ; la fenêtre d'éjection le traverse à droite
    for x0, x1 in ((0.0268, 0.0286), (-0.0286, -0.0268)):
        f = kit.prisme(pc, BANDEAU, x0, x1, "Peinture")
        kit.chanfreiner(pc, 0.0006, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, *port)
    a.couper(ob, cp)

    pc = a.piece("Carcasse_Basse", "PeintureClaire")
    f = kit.prisme(pc, [(-0.068, 0.112), (-0.068, 0.082), (-0.050, 0.050), (0.272, 0.050), (0.300, 0.094),
                        (0.300, 0.112)], -0.025, 0.025)
    kit.chanfreiner(pc, 0.002, f)
    f = kit.prisme(pc, [(0.175, 0.054), (0.168, 0.012), (0.173, 0.004), (0.247, 0.004), (0.253, 0.012),
                        (0.258, 0.054)], -0.0275, 0.0275)   # puits du chargeur, incliné comme lui
    kit.chanfreiner(pc, 0.002, f)
    ca.pontet(pc, [(0.172, 0.036), (0.166, 0.008), (0.148, -0.004), (0.046, -0.004), (0.027, 0.010)], mat="Polymere")
    ca.detente(pc, 0.063, 0.052)
    a.objet(pc)

    pc = a.piece("Prise_Puits", "Peinture")   # le flanc gauche du puits, strié : là où la main reprend le chargeur
    f = kit.prisme(pc, [(0.214, 0.010), (0.247, 0.010), (0.251, 0.048), (0.218, 0.048)], 0.0270, 0.0286)
    kit.chanfreiner(pc, 0.0005, f)
    for h in (0.016, 0.023, 0.030, 0.037, 0.044):
        av0 = 0.217 + (h - 0.010) / 0.038 * 0.004
        kit.boite(pc, av0, av0 + 0.029, h - 0.0012, h + 0.0012, 0.0284, 0.0296, "Peinture")
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.cylindre(pc, P(0.006, 0.090, 0.0245), P(0.006, 0.090, 0.0292), 0.0062, 10)        # sélecteur
    f = kit.prisme(pc, [(0.004, 0.0935), (0.029, 0.0862), (0.030, 0.0822), (0.004, 0.0868)], 0.0288, 0.0312)
    kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(0.162, 0.078, -0.0245), P(0.162, 0.078, -0.0285), 0.0046, 8)      # bouton du chargeur
    f = kit.boite(pc, 0.050, 0.064, 0.1520, 0.1600, 0.022, 0.034)                         # levier d'armement
    f += kit.boite(pc, 0.048, 0.066, 0.1490, 0.1632, 0.034, 0.044, "Polymere")
    kit.chanfreiner(pc, 0.0012, f)
    kit.boite(pc, 0.076, 0.146, 0.135, 0.151, -0.021, -0.013)                              # la culasse, dans la fenêtre
    for x0, x1 in ((0.0245, 0.0262), (-0.0245, -0.0262)):                                 # goupilles
        kit.cylindre(pc, P(-0.058, 0.100, x0), P(-0.058, 0.100, x1), 0.0042, 8)
        kit.cylindre(pc, P(0.278, 0.099, x0), P(0.278, 0.099, x1), 0.0042, 8)
    a.objet(pc)


def garde_main(a):
    pc = a.piece("Garde_Main", "PeintureClaire")
    mats = ca.mats8(chanfrein_haut="Peinture", flancs="PeintureClaire", chanfrein_bas="Polymere", dessous="Polymere")
    f = kit.loft(pc, kit.sections_av(GARDE_MAIN), "PeintureClaire", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0016, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    # la ferme de triangles, de part en part : alternés pointe en haut, pointe en bas
    w, b, t = 0.0244, 0.091, 0.120
    for k in range(9):
        x0 = 0.324 + k * w
        tris = [[(x0, b), (x0 + w, b), (x0 + w / 2, t)]]
        if k < 8:
            tris.append([(x0 + w / 2, t), (x0 + 1.5 * w, t), (x0 + w, b)])
        for tri in tris:
            cx, cy = sum(p[0] for p in tri) / 3, sum(p[1] for p in tri) / 3
            kit.prisme(cp, [(cx + (u - cx) * 0.70, cy + (v - cy) * 0.70) for u, v in tri], -0.040, 0.040)
    for av0, av1 in ((0.334, 0.372), (0.384, 0.422), (0.434, 0.472), (0.484, 0.522)):   # fentes du haut
        kit.boite(cp, av0, av1, 0.136, 0.146, 0.0235, 0.040)
        kit.boite(cp, av0, av1, 0.136, 0.146, -0.040, -0.0235)
    a.couper(ob, cp)

    pc = a.piece("Bec", "PeintureClaire")   # le bec au-dessus de la bouche, écorné à droite
    f = kit.prisme(pc, [(0.548, 0.1700), (0.598, 0.1640), (0.632, 0.1560), (0.648, 0.1556), (0.652, 0.1590),
                        (0.644, 0.1700), (0.620, 0.1790), (0.572, 0.1812), (0.552, 0.1800)], -0.0100, 0.0100)
    kit.chanfreiner(pc, 0.0012, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.prisme(cp, [(0.628, 0.1810), (0.641, 0.1655), (0.656, 0.1630), (0.662, 0.1860)], -0.016, 0.0015)
    a.couper(ob, cp)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, -0.082, 0.548, P(-0.082, 0.169), "haut", mat="Polymere")
    ca.rail(pc, 0.380, 0.490, P(0.380, 0.0655), "bas", mat="Polymere")
    ca.rail(pc, 0.553, 0.589, P(0.553, 0.122, -0.0268), "droite", mat="Polymere")
    f = kit.boite(pc, 0.496, 0.506, 0.050, 0.068, -0.0075, 0.0075, "Polymere")   # arrêt de main
    kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc)


def canon(a):
    pc = a.piece("Canon", "Bouche")
    kit.cylindre(pc, P(0.596, H_AXE), P(0.640, H_AXE), 0.0090, 12, phase=0.0)
    a.objet(pc)
    pc = a.piece("Compensateur", "Bouche")
    f = kit.tranches(pc, P(0.636, H_AXE), P(AV_BOUCHE, H_AXE), [0.0118, 0.0126, 0.0126, 0.0112], n=12,
                     positions=[0.0, 0.08, 0.92, 1.0])
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.650, 0.664, 0.678, 0.692):   # deux rangées de trous, de part en part
        for dh in (-0.0042, 0.0042):
            kit.boite(cp, av, av + 0.0055, H_AXE + dh - 0.0022, H_AXE + dh + 0.0022, -0.030, 0.030)
    kit.cylindre(cp, P(0.704, H_AXE), P(0.722, H_AXE), 0.0050, 8)
    a.couper(ob, cp, auto=True)


PROFIL_CROSSE = [(-0.090, 0.170), (-0.268, 0.168), (-0.286, 0.172), (-0.300, 0.164), (-0.300, 0.036),
                 (-0.290, 0.026), (-0.272, 0.030), (-0.262, 0.060), (-0.150, 0.098), (-0.090, 0.112)]
AJOUR_CROSSE = [(-0.126, 0.124), (-0.146, 0.156), (-0.248, 0.156), (-0.254, 0.148), (-0.254, 0.088),
                (-0.240, 0.082)]


def crosse(a):
    """La crosse squelette, réglable : un groupe à part, qu'une crosse de l'armurerie remplacera."""
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Polymere")
    f = kit.prisme(pc, PROFIL_CROSSE, -0.0185, 0.0185, "Polymere")
    kit.chanfreiner(pc, 0.0025, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Polymere")
    kit.prisme(cp, AJOUR_CROSSE, -0.03, 0.03)
    kit.prisme(cp, [(-0.284, 0.060), (-0.270, 0.060), (-0.270, 0.140), (-0.284, 0.140)], -0.03, 0.03)   # fente du talon
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.300, 0.172), (-0.309, 0.168), (-0.312, 0.156), (-0.312, 0.040), (-0.308, 0.028),
                        (-0.300, 0.024)], -0.0210, 0.0210)
    kit.chanfreiner(pc, 0.003, f)
    ca.mire_crash(pc, P(-0.108, 0.141, 0.0187), GAUCHE, r=0.0070)
    a.objet(pc).parent = groupe

    # son verrou a cassé : la crosse est bloquée par un collier de serrage autour de la barre du haut
    pc = a.piece("Collier_Crosse", "AcierBrut")
    contour = kit.contour_rect(0.0390, 0.0150, chanfrein=0.0025, cy=0.1626)
    kit.bande(pc, contour, P(-0.160, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0080, 0.0009, "AcierBrut",
              inclinaison=0.05, irregularite=0.0002, rng=a.rng)
    f = kit.boite(pc, -0.1645, -0.1555, 0.1560, 0.1690, 0.0193, 0.0238, "AcierBrut")   # boîtier de la vis
    kit.chanfreiner(pc, 0.0006, f)
    kit.vis_six_pans(pc, P(-0.160, 0.1690, 0.0215), HAUT, r=0.0024, h=0.0014, mat="Acier")
    a.objet(pc).parent = groupe


def marquages(a):
    pc = a.piece("Marquages", "MarquageNoir")
    a.texte(pc, a.code, 0.0105, P(0.200, 0.1500, 0.0282), ca.VERS_CROSSE, HAUT, mat="MarquageNoir")
    a.texte(pc, "N° 3007", 0.0046, P(0.110, 0.070, -0.0252), ca.VERS_BOUCHE, HAUT, mat="MarquageNoir",
            decalage=0.0003, resolution=1)
    ca.encoches(pc, 0.395, 0.0790, 0.0870, 0.0303, 9, mat="MarquageNoir", largeur=0.0014, pas=0.0035)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_pistolet(a, ruban=False)
    ca.chargeur(a, DOS, FACE, 0.0125, mat="Polymere", mat_semelle="PeintureClaire", bande=False)
    garde_main(a)
    canon(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE)
    ca.guidon(a, AV_GUIDON, H_RAIL, H_VISEE)
    crosse(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.330, 0.0650))
    a.accroches({
        "Viseur": P(0.100, H_RAIL),                 # rail du dessus, au-dessus de la carcasse
        "Laser": P(0.571, 0.122, -0.0388),          # petit rail à droite, devant la ferme
        "Poignee": P(0.435, 0.0535),                # rail du dessous
        "Chargeur": P(0.2145, 0.034),               # haut du chargeur, dans le puits
        "Crosse": P(-0.095, 0.140),
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_garde_main", P(0.46, 0.16, 0.20), P(0.46, 0.11, 0.0), 45),
    ("detail_crosse", P(-0.20, 0.17, 0.25), P(-0.20, 0.11, 0.0), 50),
]

ATELIER = ca.principal("FA-30", "fusil d'assaut série 30", "Fusil d'assaut série 30", construire,
                       (AV_BOUCHE, H_AXE), vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
