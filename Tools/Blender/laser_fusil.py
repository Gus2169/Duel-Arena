"""Fusil laser FX-1 de Duel Arena (Blender 5.2, low poly) : la classe fusil d'assaut, en arme à faisceau.

Un prototype du programme d'essai, aux formes franchement futuristes (référence : les armes à
énergie d'Apex Legends) : carcasse en coin, capot avant massif au dessus de céramique blanc cassé,
fenêtre traversante sur un cœur d'énergie cerclé de bobines, tête d'émetteur carrée. Il a servi
lui aussi : un panneau de céramique cassé remplacé par une tôle rivetée, le capot fendu tenu au
ruban, l'émetteur noirci.

Pour les animations, il garde les points de prise du FA-07 : poignée à l'origine, cellule d'énergie
dans le puits où la main gauche du clip de rechargement va chercher le chargeur, levier de charge
en haut à gauche au-dessus de la poignée, dessous du capot sous la main gauche, crosse à l'épaule.

  blender -b --factory-startup -P laser_fusil.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.138           # axe du faisceau (la Bouche du FA-07)
AV_BOUCHE = 0.706
H_VISEE = 0.200
AV_HAUSSE = -0.040
AV_GUIDON = 0.520

# Cellule d'énergie : droite, inclinée vers l'avant, dans le puits du chargeur.
DOS, FACE = ca.courbes_chargeur((0.164, 0.026), (0.173, -0.032), (0.182, -0.090),
                                (0.256, 0.026), (0.265, -0.028), (0.274, -0.082), n=4)

# Carcasse en coin : plus haute vers l'avant, là où elle rejoint le capot.
CARCASSE = [(-0.100, ca.section8(0.022, 0.110, 0.150, 0.008, 0.004)),
            (-0.075, ca.section8(0.027, 0.106, 0.166, 0.010, 0.004)),
            (0.080, ca.section8(0.029, 0.104, 0.176, 0.011, 0.004)),
            (0.230, ca.section8(0.030, 0.104, 0.174, 0.011, 0.004)),
            (0.285, ca.section8(0.031, 0.100, 0.172, 0.011, 0.004))]

# Capot avant : carène dessous (la main gauche), dessus en céramique, nez qui remonte vers l'émetteur.
CAPOT = [(0.280, ca.section8(0.031, 0.064, 0.174, 0.012, 0.010, 0.55)),
         (0.330, ca.section8(0.032, 0.060, 0.176, 0.012, 0.010, 0.55)),
         (0.470, ca.section8(0.031, 0.064, 0.172, 0.012, 0.010, 0.55)),
         (0.560, ca.section8(0.028, 0.082, 0.164, 0.011, 0.009, 0.58)),
         (0.620, ca.section8(0.022, 0.104, 0.156, 0.009, 0.006, 0.65)),
         (0.640, ca.section8(0.019, 0.112, 0.154, 0.008, 0.006, 0.70))]


def carcasse(a):
    pc = a.piece("Carcasse_Haute", "Peinture")
    f = kit.loft(pc, kit.sections_av(CARCASSE), "Peinture")
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.boite(pc, -0.104, -0.096, 0.092, 0.138, -0.017, 0.017, "Acier")   # tenon de la crosse
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, -0.035, 0.064, 0.1495, 0.1585, 0.020, 0.040)      # fente du levier de charge (gauche)
    for av in (0.070, 0.098, 0.126):                                 # évent de chaleur (droite)
        kit.boite(cp, av, av + 0.018, 0.122, 0.150, -0.040, -0.024)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Carcasse_Basse", "Peinture")
    f = kit.prisme(pc, [(-0.078, 0.108), (-0.078, 0.082), (-0.056, 0.050), (0.146, 0.050), (0.146, 0.018),
                        (0.158, 0.006), (0.268, 0.006), (0.280, 0.020), (0.282, 0.060), (0.290, 0.100),
                        (0.290, 0.108)], -0.0255, 0.0255)
    kit.chanfreiner(pc, 0.0022, f)
    f = kit.prisme(pc, [(0.150, 0.026), (0.150, 0.010), (0.160, 0.001), (0.266, 0.001), (0.282, 0.012),
                        (0.282, 0.026)], -0.0285, 0.0285)                 # collerette du puits de la cellule
    kit.chanfreiner(pc, 0.0018, f)
    ca.pontet(pc, [(0.150, 0.032), (0.146, 0.004), (0.128, -0.006), (0.048, -0.006), (0.028, 0.008)])
    ca.detente(pc, 0.064, 0.052)
    a.objet(pc)
    # le panneau de céramique d'origine, côté droit (à gauche, il a cassé : voir rafistolages)
    pc = a.piece("Panneau_Carcasse", "Ceramique")
    f = kit.prisme(pc, [(0.154, 0.117), (0.262, 0.115), (0.268, 0.146), (0.162, 0.150), (0.152, 0.140)], -0.0306, -0.0290)
    kit.chanfreiner(pc, 0.0007, f)
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.cylindre(pc, P(0.006, 0.088, 0.0250), P(0.006, 0.088, 0.0290), 0.0060, 10)          # sélecteur
    f = kit.prisme(pc, [(0.004, 0.0915), (0.028, 0.0842), (0.029, 0.0805), (0.004, 0.0848)], 0.0286, 0.0310)
    kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(0.152, 0.080, -0.0250), P(0.152, 0.080, -0.0290), 0.0046, 8)        # bouton de la cellule
    f = kit.boite(pc, 0.046, 0.058, 0.1505, 0.1575, 0.022, 0.032)                           # levier de charge
    f += kit.boite(pc, 0.042, 0.062, 0.1470, 0.1615, 0.032, 0.043, "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    kit.quad(pc, [P(0.045, 0.1515, 0.0431), P(0.059, 0.1515, 0.0431), P(0.059, 0.1570, 0.0431),
                  P(0.045, 0.1570, 0.0431)], "Lueur", normale=GAUCHE)                       # témoin du levier
    for av in (0.070, 0.098, 0.126):                                                        # fond de l'évent
        kit.quad(pc, [P(av + 0.0005, 0.1225, -0.0243), P(av + 0.0175, 0.1225, -0.0243),
                      P(av + 0.0175, 0.1495, -0.0243), P(av + 0.0005, 0.1495, -0.0243)], "Lueur", normale=-GAUCHE)
    a.objet(pc)


def capot(a):
    pc = a.piece("Capot", "Peinture")
    mats = ca.mats8(dessus="Peinture", chanfrein_haut="Ceramique", flancs="Peinture", chanfrein_bas="Polymere",
                    dessous="Polymere")
    f = kit.loft(pc, kit.sections_av(CAPOT), "Peinture", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0015, f)
    for x0, x1 in ((0.0300, 0.0334), (-0.0334, -0.0300)):   # panneaux de céramique sur les flancs
        f = kit.prisme(pc, [(0.300, 0.082), (0.322, 0.075), (0.500, 0.077), (0.522, 0.093), (0.505, 0.112),
                            (0.310, 0.112)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0008, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.395, 0.505, H_AXE - 0.016, H_AXE + 0.016, -0.045, 0.045)   # fenêtre sur le cœur, de part en part
    fentes = [(0.538, 0.548), (0.554, 0.564), (0.570, 0.580)]
    for av0, av1 in fentes:                                                     # évents du dessus
        kit.boite(cp, av0, av1, 0.155, 0.180, -0.0125, 0.0125)
    a.couper(ob, cp, auto=True)   # capot et panneaux se chevauchent
    ca.coeur(a, 0.392, 0.508, H_AXE, r=0.0060, bobines=4, r_bobine=0.0128)
    pc = a.piece("Lueurs_Capot", "Lueur")
    ca.lueurs_dessus(pc, fentes, 0.1553, 0.0120)
    a.objet(pc, angle=None)


def emetteur(a):
    pc = a.piece("Emetteur", "Bouche")
    tete = [(0.636, ca.section8(0.018, 0.122, 0.155, 0.006, 0.006)),
            (0.690, ca.section8(0.016, 0.124, 0.152, 0.006, 0.006)),
            (0.700, ca.section8(0.013, 0.127, 0.149, 0.005, 0.005))]
    f = kit.loft(pc, kit.sections_av(tete), "Bouche")
    kit.chanfreiner(pc, 0.0008, f)
    kit.anneau(pc, 0.698, AV_BOUCHE, H_AXE, 0.0102, 0.0068, 10, "Bouche")
    kit.disque(pc, P(0.6992, H_AXE), AVANT, 0.0069, 10, mat="Laser")
    for x0, x1 in ((0.0160, 0.0192), (-0.0192, -0.0160)):   # ailerons de la tête
        f = kit.prisme(pc, [(0.655, 0.130), (0.694, 0.131), (0.702, 0.135), (0.702, 0.143), (0.694, 0.147),
                            (0.655, 0.148)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    a.objet(pc)


def montages(a):
    """Interfaces d'accessoires : viseurs dessus, laser tactique à droite, poignée dessous."""
    pc = a.piece("Montages", "Peinture")
    ca.plaque_montage(pc, 0.000, 0.200, P(0.000, 0.1745), "haut", largeur=0.022, ep=0.004, encoches=8)
    ca.plaque_montage(pc, 0.300, 0.370, P(0.300, 0.120, -0.0318), "droite", largeur=0.020, encoches=3)
    # dessous, à la place des nervures sous la main gauche : une poignée montée à 0,50 m, 12 cm devant la main, la
    # manquait d'un centimètre, bras tendu (2026-10-27)
    ca.plaque_montage(pc, 0.338, 0.462, P(0.338, 0.0640), "bas", largeur=0.018, encoches=6)
    a.objet(pc)


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Peinture")
    f = kit.prisme(pc, [(-0.096, 0.150), (-0.250, 0.157), (-0.292, 0.160), (-0.306, 0.150), (-0.306, 0.032),
                        (-0.294, 0.022), (-0.250, 0.034), (-0.150, 0.078), (-0.096, 0.090)], -0.0215, 0.0215)
    kit.chanfreiner(pc, 0.0030, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Peinture")   # un ajour plus petit que sur le FA-07 : une crosse massive
    kit.prisme(cp, [(-0.150, 0.098), (-0.168, 0.128), (-0.250, 0.132), (-0.262, 0.120), (-0.262, 0.064),
                    (-0.246, 0.058)], -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Crosse_Panneaux", "Ceramique")   # joue et talon en céramique, des deux côtés
    for x0, x1 in ((0.0210, 0.0226), (-0.0226, -0.0210)):
        f = kit.prisme(pc, [(-0.112, 0.1405), (-0.252, 0.1450), (-0.262, 0.1530), (-0.120, 0.1485)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
        f = kit.prisme(pc, [(-0.270, 0.052), (-0.296, 0.040), (-0.296, 0.146), (-0.270, 0.140)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    f = kit.prisme(pc, [(-0.306, 0.168), (-0.316, 0.164), (-0.319, 0.150), (-0.319, 0.034), (-0.315, 0.020),
                        (-0.306, 0.017)], -0.0235, 0.0235, "Caoutchouc")   # plaque de couche
    kit.chanfreiner(pc, 0.0030, f)
    ca.mire_crash(pc, P(-0.2205, 0.050, 0.0217), GAUCHE, r=0.0075)
    a.objet(pc).parent = groupe


def rafistolages(a):
    # le panneau de céramique gauche de la carcasse, cassé, remplacé par une tôle rivetée
    pc = a.piece("Plaque_Rivetee", "AcierBrut")
    f = kit.prisme(pc, [(0.092, 0.117), (0.182, 0.115), (0.186, 0.146), (0.100, 0.150), (0.090, 0.140)], 0.0290, 0.0305)
    kit.chanfreiner(pc, 0.0006, f)
    for av, h in ((0.097, 0.121), (0.178, 0.119), (0.181, 0.142), (0.104, 0.145)):
        kit.rivet(pc, P(av, h, 0.0305), GAUCHE, mat="AcierBrut")
    a.objet(pc)
    # le nez du capot fendu, tenu au ruban
    pc = a.piece("Ruban_Capot", "Ruban")
    section = ca.section8(0.0292, 0.077, 0.166, 0.011, 0.009, 0.58)
    kit.bande(pc, section, P(0.548, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.014, 0.0011, "Ruban",
              inclinaison=0.07, irregularite=0.0007, rng=a.rng, rabat=(4, 0.010))
    a.objet(pc)
    # le compteur de charge, sur le pan arrière de la carcasse (il monte de 16 mm sur 25), face au joueur
    pc = a.piece("Compteur", "Polymere")
    ca.compteur_arriere(pc, P(-0.0875, 0.158), 0.0240, 0.0220, elevation=57.4, chiffres="30")
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0105, P(0.352, 0.136, 0.0322), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "PROTOTYPE", 0.0050, P(0.222, 0.075, -0.0258), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.autocollant_laser(pc, P(0.565, 0.118, -0.0292), -GAUCHE, Vector((0, -1, 0)), 0.0095)
    ca.bande_hachuree(pc, [P(0.330, 0.0975, 0.0336), P(0.480, 0.0975, 0.0336), P(0.480, 0.1035, 0.0336),
                           P(0.330, 0.1035, 0.0336)], GAUCHE, n=16)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_futuriste(a)
    ca.cellule(a, DOS, FACE, 0.0135, segments=4, allumes=3)
    capot(a)
    emetteur(a)
    montages(a)
    ca.hausse(a, AV_HAUSSE, 0.1695, H_VISEE, cotes=6)
    ca.guidon(a, AV_GUIDON, 0.1675, H_VISEE, mat_pointe="Lueur")
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.380, 0.0600))
    a.accroches({
        "Viseur": P(0.100, 0.1785),
        "Laser": P(0.335, 0.120, -0.0343),
        "Poignee": P(0.420, 0.0615),                # plaque du dessous, 4 cm devant la main gauche
        "Chargeur": P(0.210, 0.026),
        "Crosse": P(-0.100, 0.115),
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_coeur", P(0.46, 0.20, 0.17), P(0.46, H_AXE, 0.0), 45),
    ("detail_arriere", P(-0.10, 0.27, 0.19), P(0.02, 0.15, 0.0), 45),
]

ATELIER = ca.principal("FX-1", "fusil laser", "Fusil laser", construire, (AV_BOUCHE, H_AXE), laser=True, vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
