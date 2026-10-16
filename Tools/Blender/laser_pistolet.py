"""Pistolet laser FX-4 de Duel Arena (Blender 5.2, low poly) : la classe pistolet, en arme à faisceau.

Un prototype du programme d'essai, aux formes franchement futuristes (référence : les armes à
énergie d'Apex Legends) : boîtier anguleux percé d'une fenêtre sur un petit cœur cerclé de bobines,
tête d'émetteur à plaques de céramique, et devant la lentille un anneau focaliseur hexagonal tenu
par deux entretoises. Mire à cran et guidon, trois points lumineux ; une plaque sur le boîtier
(viseur point rouge) et une autre sous la carcasse (laser tactique) attendent les accessoires.
Tenu à deux mains (WeaponModel.pistolHold), comme le raygun du jeu, qu'il remplacera.

Il a servi : une plaque de céramique de la tête perdue, remplacée par une tôle boulonnée, le
boîtier fêlé tenu au ruban, l'anneau et la tête noircis.

Pour les animations, il garde les points de prise du PA-05 : poignée à l'origine, cellule d'énergie
dans la poignée (le jeu prête au pistolet les gestes du fusil, dette n° 45), crans de prise à
l'arrière du boîtier.

  blender -b --factory-startup -P laser_pistolet.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.064
AV_BOUCHE = 0.200       # devant de l'anneau focaliseur
H_MIRE = 0.0910
T = (0.010, 0.040)      # haut de l'axe de la poignée (celle du PA-05)
INCLINAISON = 18.0
LONGUEUR = 0.110
_R = math.radians(INCLINAISON)
U = (-math.sin(_R), -math.cos(_R))   # le long de la poignée, vers le bas
N = (math.cos(_R), -math.sin(_R))    # vers l'avant de la poignée

BOITIER = [(-0.032, ca.section8(0.0135, 0.045, 0.080, 0.006, 0.003)),
           (-0.020, ca.section8(0.0150, 0.045, 0.084, 0.007, 0.003)),
           (0.118, ca.section8(0.0155, 0.045, 0.084, 0.007, 0.003)),
           (0.132, ca.section8(0.0165, 0.043, 0.0835, 0.007, 0.004))]
TETE = [(0.128, ca.section8(0.0170, 0.040, 0.0840, 0.007, 0.005)),
        (0.176, ca.section8(0.0170, 0.038, 0.0820, 0.007, 0.005)),
        (0.188, ca.section8(0.0145, 0.042, 0.0780, 0.006, 0.004))]


def pt(t, d):
    return (T[0] + U[0] * t + N[0] * d, T[1] + U[1] * t + N[1] * d)


def boitier(a):
    pc = a.piece("Boitier", "Peinture")
    f = kit.loft(pc, kit.sections_av(BOITIER), "Peinture", mats_cotes=ca.mats8(chanfrein_haut="Ceramique"))
    kit.chanfreiner(pc, 0.0012, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.058, 0.108, 0.056, 0.072, -0.030, 0.030)              # fenêtre sur le cœur, de part en part
    for i in range(3):                                                    # crans de prise
        av = -0.028 + i * 0.0052
        kit.boite(cp, av, av + 0.0019, 0.050, 0.0745, 0.0115, 0.020)
        kit.boite(cp, av, av + 0.0019, 0.050, 0.0745, -0.020, -0.0115)
    a.couper(ob, cp)
    ca.coeur(a, 0.054, 0.112, H_AXE, r=0.0030, bobines=3, r_bobine=0.0068)

    pc = a.piece("Montages", "Peinture")
    ca.plaque_montage(pc, 0.002, 0.044, P(0.002, 0.0838), "haut", largeur=0.016, ep=0.0018, encoches=4)
    ca.plaque_montage(pc, 0.092, 0.146, P(0.092, 0.0272), "bas", largeur=0.018, ep=0.0025, encoches=4)
    a.objet(pc)


def tete(a):
    pc = a.piece("Tete", "Bouche")
    f = kit.loft(pc, kit.sections_av(TETE), "Bouche", mats_cotes=ca.mats8(chanfrein_haut="Ceramique"))
    kit.chanfreiner(pc, 0.0010, f)
    kit.anneau(pc, 0.1855, 0.1900, H_AXE, 0.0085, 0.0058, 10, "Bouche")
    kit.disque(pc, P(0.1872, H_AXE), AVANT, 0.0059, 10, mat="Laser")
    for h0, h1 in ((H_AXE + 0.0085, H_AXE + 0.0128), (H_AXE - 0.0128, H_AXE - 0.0085)):   # entretoises
        f = kit.boite(pc, 0.186, 0.1955, h0, h1, -0.0025, 0.0025, "Bouche")
        kit.chanfreiner(pc, 0.0005, f)
    f = kit.anneau(pc, 0.1945, AV_BOUCHE, H_AXE, 0.0132, 0.0092, 6, "Bouche", phase=0.0)   # anneau focaliseur
    kit.chanfreiner(pc, 0.0005, f)
    # plaque de céramique d'origine à droite (à gauche, voir rafistolages), et la ligne de tir
    f = kit.prisme(pc, PLAQUE_TETE, -0.0200, -0.0150, "Ceramique")
    kit.chanfreiner(pc, 0.0005, f)
    for signe in (1, -1):
        x = signe * 0.0172
        kit.quad(pc, [P(0.132, 0.0478, x), P(0.172, 0.0470, x), P(0.172, 0.0500, x), P(0.132, 0.0508, x)],
                 "Lueur", normale=GAUCHE * signe)
    a.objet(pc)


PLAQUE_TETE = [(0.136, 0.0545), (0.178, 0.0535), (0.184, 0.0600), (0.178, 0.0770), (0.140, 0.0780), (0.134, 0.0700)]


def carcasse(a):
    pc = a.piece("Carcasse", "Peinture")
    f = kit.prisme(pc, [(-0.034, 0.047), (-0.036, 0.038), (-0.022, 0.030), (0.032, 0.027), (0.140, 0.027),
                        (0.156, 0.034), (0.158, 0.047)], -0.0135, 0.0135)
    kit.chanfreiner(pc, 0.0018, f)
    ca.pontet(pc, [(0.086, 0.030), (0.082, 0.004), (0.066, -0.006), (0.036, -0.006), (0.024, 0.006)])
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    ca.detente(pc, 0.051, 0.031, echelle=0.72)
    f = kit.boite(pc, 0.028, 0.062, 0.0420, 0.0465, 0.0130, 0.0152)       # arrêtoir
    kit.chanfreiner(pc, 0.0005, f)
    kit.quad(pc, [P(0.052, 0.0428, 0.0153), P(0.060, 0.0428, 0.0153), P(0.060, 0.0457, 0.0153),
                  P(0.052, 0.0457, 0.0153)], "Lueur", normale=GAUCHE)        # témoin de charge
    kit.cylindre(pc, P(0.027, 0.022, 0.0135), P(0.027, 0.022, 0.0160), 0.0034, 8)   # bouton de la cellule
    a.objet(pc)


def cellule(a):
    """La cellule d'énergie, dans la poignée : son corps (caché) et son culot qui luit, qu'une cellule
    rallongée remplacera."""
    pc = a.piece("Chargeur_Standard", "Peinture")
    corps = [pt(0.006, 0.016), pt(LONGUEUR, 0.016), pt(LONGUEUR, -0.016), pt(0.006, -0.016)]
    kit.prisme(pc, corps, -0.0105, 0.0105, "Peinture")
    culot = [pt(LONGUEUR - 0.002, 0.0270), pt(LONGUEUR + 0.0080, 0.0278), pt(LONGUEUR + 0.0080, -0.0262),
             pt(LONGUEUR - 0.002, -0.0258)]
    f = kit.prisme(pc, culot, -0.0172, 0.0172, "Acier")
    kit.chanfreiner(pc, 0.0016, f)
    for x in (0.0173, -0.0173):   # la jauge du culot, des deux côtés
        q = [pt(LONGUEUR + 0.0015, 0.0190), pt(LONGUEUR + 0.0060, 0.0194), pt(LONGUEUR + 0.0060, -0.0176),
             pt(LONGUEUR + 0.0015, -0.0172)]
        kit.quad(pc, [P(av, h, x) for av, h in q], "Lueur", normale=GAUCHE * (1 if x > 0 else -1))
    a.objet(pc)


def mire(a):
    """Hausse à cran et guidon, trois points lumineux : deux de part et d'autre du cran, un sur le guidon."""
    pc = a.piece("Hausse", "Peinture")
    f = kit.boite(pc, -0.024, -0.012, 0.0820, 0.0935, -0.0085, 0.0085)
    kit.chanfreiner(pc, 0.0008, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, -0.030, -0.006, 0.0885, 0.0960, -0.0017, 0.0017)
    a.couper(ob, cp)
    pc = a.piece("Points_Hausse", "Lueur")
    for x in (0.0042, -0.0042):
        kit.disque(pc, P(-0.0241, 0.0905, x), Vector((0, 1, 0)), 0.0010, 6, mat="Lueur")   # face au tireur
    a.objet(pc, angle=None)
    pc = a.piece("Guidon", "Peinture")
    f = kit.boite(pc, 0.146, 0.154, 0.0825, 0.0932, -0.0016, 0.0016)
    kit.chanfreiner(pc, 0.0004, f)
    kit.disque(pc, P(0.1459, 0.0912), Vector((0, 1, 0)), 0.0010, 6, mat="Lueur")
    a.objet(pc)


def rafistolages(a):
    # à gauche, la plaque de céramique de la tête perdue, remplacée par une tôle boulonnée
    pc = a.piece("Tole_Tete", "AcierBrut")
    f = kit.prisme(pc, PLAQUE_TETE, 0.0150, 0.0192)
    kit.chanfreiner(pc, 0.0005, f)
    for av, h in ((0.141, 0.0595), (0.174, 0.0715)):
        kit.vis_six_pans(pc, P(av, h, 0.0192), GAUCHE, r=0.0019, h=0.0012, mat="Acier")
    a.objet(pc)
    # le boîtier fêlé, juste derrière la tête, tenu au ruban
    pc = a.piece("Ruban_Boitier", "Ruban")
    section = ca.section8(0.0155, 0.045, 0.084, 0.007, 0.003)
    kit.bande(pc, section, P(0.116, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.010, 0.0010, "Ruban",
              inclinaison=0.08, irregularite=0.0005, rng=a.rng, rabat=(3, 0.006))
    a.objet(pc)
    pc = a.piece("Compteur", "Polymere")   # le compteur de charge, à l'arrière du boîtier, face au joueur
    ca.compteur_arriere(pc, P(-0.032, 0.0615), 0.0220, 0.0230, elevation=0.0, chiffres="12")
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0065, P(0.112, 0.0370, 0.0136), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "PROTOTYPE", 0.0030, P(0.112, 0.0370, -0.0136), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.mire_crash(pc, P(0.140, 0.0372, 0.0137), GAUCHE, r=0.0040)
    ca.autocollant_laser(pc, P(0.159, 0.0660, -0.0201), -GAUCHE, Vector((0, -1, 0)), 0.0058)
    a.objet(pc, angle=None)


def construire(a):
    boitier(a)
    tete(a)
    carcasse(a)
    ca.poignee_futuriste(a, T=T, inclinaison=INCLINAISON, longueur=LONGUEUR)
    cellule(a)
    mire(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(-0.018, H_MIRE), P(AV_BOUCHE, H_AXE))
    a.accroches({
        "Viseur": P(0.023, 0.0856),                 # plaque sur le boîtier (viseur point rouge)
        "Laser": P(0.119, 0.0247),                  # plaque sous la carcasse (laser tactique)
        "Poignee": P(*pt(0.055, 0.0)),              # manchon de poignée
        "Chargeur": P(*pt(LONGUEUR, 0.0)),          # culot de la cellule
    })


VUES = [
    ("visee", P(-0.40, H_MIRE + 0.0015), P(1.0, H_MIRE), 40),
    ("detail_tete", P(0.27, 0.10, 0.10), P(0.17, H_AXE, 0.0), 40),
    ("detail_poignee", P(0.02, 0.03, 0.17), P(-0.005, 0.0, 0.0), 45),
]

ATELIER = ca.principal("FX-4", "pistolet laser", "Pistolet laser", construire, (AV_BOUCHE, H_AXE), laser=True,
                       vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
