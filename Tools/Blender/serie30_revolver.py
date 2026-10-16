"""Revolver RV-30 de Duel Arena (Blender 5.2, low poly) : la série 30, classe pistolet.

La série 30 est la dernière dotation d'armes à balles du complexe (voir serie30_fusil.py). Le
RV-30 (référence de l'utilisateur, 2026-10-15 : le Wingman d'Apex Legends) : un gros revolver
anguleux, carter du barillet à pans en acier, fenêtre sur le barillet cannelé, fourreau du canon
en acier, bouche en retrait, rail sur toute la longueur, chien, queue de détente et couronne de bouche en laiton.
Tenu à deux mains (WeaponModel.pistolHold).

Il a servi : les plaquettes de crosse refaites dans une tôle à l'apprêt rouge, vissées ;
l'anneau de dragonne perdu, remplacé par une boucle de fil de fer.

Pour les animations, il garde la prise du PA-05 : poignée à l'origine (même axe, même
inclinaison). Le jeu prête au pistolet les gestes du fusil (dette n° 45) ; le point « Chargeur »
est au barillet (un chargeur rapide).

  blender -b --factory-startup -P serie30_revolver.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.072           # axe du canon
H_BARILLET = H_AXE - 0.0125   # axe du barillet : la chambre du haut est dans l'axe du canon
AV_BOUCHE = 0.198
H_MIRE = 0.1085
T = (0.010, 0.040)      # haut de l'axe de la poignée (celle du PA-05)
INCLINAISON = 18.0
LONGUEUR = 0.106
_R = math.radians(INCLINAISON)
U = (-math.sin(_R), -math.cos(_R))   # le long de la poignée, vers le bas
N = (math.cos(_R), -math.sin(_R))    # vers l'avant de la poignée

CARTER = [(0.020, ca.section8(0.0175, 0.036, 0.098, 0.007, 0.007)),
          (0.028, ca.section8(0.0200, 0.034, 0.100, 0.008, 0.008)),
          (0.090, ca.section8(0.0200, 0.034, 0.100, 0.008, 0.008)),
          (0.098, ca.section8(0.0175, 0.036, 0.098, 0.007, 0.007))]
FOURREAU = [(0.096, ca.section8(0.0150, 0.052, 0.098, 0.007, 0.006)),
            (0.188, ca.section8(0.0150, 0.052, 0.098, 0.007, 0.006)),
            (0.198, ca.section8(0.0130, 0.055, 0.095, 0.006, 0.005))]
AVANT_POIGNEE = [(0.0, 0.0235), (0.026, 0.0215), (0.050, 0.0235), (0.078, 0.0220), (LONGUEUR, 0.0260)]
ARRIERE_POIGNEE = [(LONGUEUR, -0.0270), (0.072, -0.0262), (0.030, -0.0250), (0.004, -0.0232)]


def pt(t, d):
    return (T[0] + U[0] * t + N[0] * d, T[1] + U[1] * t + N[1] * d)


def carcasse(a):
    pc = a.piece("Carcasse", "Peinture")
    f = kit.prisme(pc, [(-0.040, 0.036), (-0.036, 0.028), (-0.022, 0.024), (0.032, 0.022), (0.186, 0.022),
                        (0.196, 0.030), (0.198, 0.040), (0.198, 0.054), (0.100, 0.054), (0.096, 0.040),
                        (0.022, 0.040), (0.018, 0.064), (0.010, 0.072), (-0.034, 0.074), (-0.042, 0.064)],
                   -0.0130, 0.0130)
    kit.chanfreiner(pc, 0.0016, f)
    f = kit.boite(pc, -0.034, 0.024, 0.0880, 0.0985, -0.0100, 0.0100)   # pont au-dessus du chien
    kit.chanfreiner(pc, 0.0010, f)
    ca.pontet(pc, [(0.084, 0.024), (0.080, 0.002), (0.066, -0.008), (0.036, -0.008), (0.022, 0.004)])
    a.objet(pc)

    pc = a.piece("Rails", "Peinture")
    ca.rail(pc, -0.010, 0.190, P(-0.010, 0.098), "haut", largeur=0.014, epaisseur=0.004, dent=0.0025, pas=0.0085,
            longueur_dent=0.0050)
    ca.rail(pc, 0.118, 0.178, P(0.118, 0.0225), "bas", largeur=0.016, epaisseur=0.004, dent=0.003, pas=0.008,
            longueur_dent=0.0045)
    a.objet(pc)

    pc = a.piece("Commandes", "Laiton")
    ca.detente(pc, 0.052, 0.024, mat="Laiton", echelle=0.70)
    f = kit.cylindre(pc, P(0.044, 0.031, 0.0128), P(0.044, 0.031, 0.0156), 0.0045, 10, mat="Laiton")   # ouverture du barillet
    kit.chanfreiner(pc, 0.0005, f)
    # le chien, crénelé, qui dépasse derrière le pont
    f = kit.prisme(pc, [(-0.020, 0.066), (-0.026, 0.080), (-0.034, 0.088), (-0.040, 0.087), (-0.043, 0.091),
                        (-0.049, 0.088), (-0.047, 0.083), (-0.052, 0.080), (-0.047, 0.075), (-0.031, 0.064)],
                   -0.0035, 0.0035, "Laiton")
    kit.chanfreiner(pc, 0.0005, f)
    kit.cylindre(pc, P(-0.028, 0.070, 0.0136), P(-0.028, 0.070, -0.0136), 0.0022, 8, mat="AcierBrut")   # axe du chien
    a.objet(pc)


def barillet(a):
    pc = a.piece("Carter_Barillet", "AcierBrut")
    f = kit.loft(pc, kit.sections_av(CARTER), "AcierBrut")
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.033, 0.085, 0.045, 0.075, 0.011, 0.040)   # fenêtre sur le barillet, côté joueur
    a.couper(ob, cp)

    pc = a.piece("Barillet", "AcierBrut")   # six pans, cannelés ; un pan face à la fenêtre
    r = 0.0190
    kit.cylindre(pc, P(0.030, H_BARILLET), P(0.088, H_BARILLET), r, 6, mat="AcierBrut")
    apotheme = r * math.cos(math.pi / 6)
    for k in range(6):
        ang = math.pi / 6 + math.pi / 3 * k + math.pi / 6   # milieu du pan
        nx, nh = -math.cos(ang), -math.sin(ang)             # le repère de kit.cercle autour de l'avant
        tx, th = -nh, nx                                    # le long du pan, en travers
        c = (nx * (apotheme + 0.0002), H_BARILLET + nh * (apotheme + 0.0002))
        l = 0.0038
        kit.quad(pc, [P(0.036, c[1] - th * l, c[0] - tx * l), P(0.082, c[1] - th * l, c[0] - tx * l),
                      P(0.082, c[1] + th * l, c[0] + tx * l), P(0.036, c[1] + th * l, c[0] + tx * l)], "Interieur",
                 normale=Vector((nx, 0.0, nh)))
    a.objet(pc)

    # la plaque hexagonale du flanc droit, et le numéro de série de la série
    pc = a.piece("Plaque_Carter", "Polymere")
    hexa = [(0.060 + math.cos(math.pi / 3 * k) * 0.0150, 0.067 + math.sin(math.pi / 3 * k) * 0.0130) for k in range(6)]
    f = kit.prisme(pc, hexa, -0.0214, -0.0195, "Polymere")
    kit.chanfreiner(pc, 0.0005, f)
    a.texte(pc, "30", 0.0090, P(0.060, 0.067, -0.0216), ca.VERS_BOUCHE, HAUT, decalage=0.0002)
    a.objet(pc, angle=None)


def canon(a):
    pc = a.piece("Fourreau", "AcierBrut")
    f = kit.loft(pc, kit.sections_av(FOURREAU), "AcierBrut")
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    poche = kit.mise_a_echelle(FOURREAU[-1][1], 0.70, (0.0, 0.0735))   # la bouche en retrait
    kit.loft(cp, [[P(0.192, h, x) for x, h in poche], [P(0.206, h, x) for x, h in poche]], "Interieur")
    for x0, x1 in ((0.0120, 0.030), (-0.030, -0.0120)):                # cannelure le long du fourreau
        kit.boite(cp, 0.110, 0.178, 0.0680, 0.0740, x0, x1)
    a.couper(ob, cp)
    pc = a.piece("Bouche", "Bouche")
    kit.anneau(pc, 0.1915, AV_BOUCHE, H_AXE, 0.0085, 0.0050, 10, "Laiton")   # couronne de laiton, noircie au fond
    kit.disque(pc, P(0.1925, H_AXE), AVANT, 0.0051, 10, mat="Interieur")
    a.objet(pc)


def poignee(a):
    pc = a.piece("Poignee", "Polymere")
    f = kit.prisme(pc, [pt(t, d) for t, d in AVANT_POIGNEE] + [pt(t, d) for t, d in ARRIERE_POIGNEE], -0.0150, 0.0150)
    kit.chanfreiner(pc, 0.0035, f)
    semelle = [pt(LONGUEUR - 0.002, 0.0265), pt(LONGUEUR + 0.0055, 0.0270), pt(LONGUEUR + 0.0055, -0.0275),
               pt(LONGUEUR - 0.002, -0.0272)]
    f = kit.prisme(pc, semelle, -0.0158, 0.0158, "AcierBrut")
    kit.chanfreiner(pc, 0.0015, f)
    a.objet(pc)


def mire(a):
    """Hausse à cran et guidon, trois points blancs : deux de part et d'autre du cran, un sur le guidon."""
    pc = a.piece("Hausse", "Peinture")
    f = kit.boite(pc, -0.032, -0.016, 0.0975, 0.1125, -0.0085, 0.0085)
    kit.chanfreiner(pc, 0.0008, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, -0.040, -0.008, 0.1065, 0.1160, -0.0017, 0.0017)
    a.couper(ob, cp)
    pc = a.piece("Guidon", "Peinture")
    f = kit.boite(pc, 0.180, 0.188, 0.1035, 0.1110, -0.0016, 0.0016)
    kit.chanfreiner(pc, 0.0004, f)
    kit.disque(pc, P(0.1799, 0.1085), Vector((0, 1, 0)), 0.0010, 6, mat="Marquage")
    for x in (0.0042, -0.0042):
        kit.disque(pc, P(-0.0322, 0.1085, x), Vector((0, 1, 0)), 0.0010, 6, mat="Marquage")
    a.objet(pc)


def rafistolages(a):
    # les plaquettes de crosse, refaites dans une tôle à l'apprêt et vissées
    pc = a.piece("Plaquettes", "Primaire")
    contour = [pt(0.014, 0.0160), pt(0.094, 0.0165), pt(0.098, 0.0060), pt(0.090, -0.0185), pt(0.016, -0.0180)]
    for x0, x1, signe in ((0.0145, 0.0168, 1), (-0.0168, -0.0145, -1)):
        f = kit.prisme(pc, contour, x0, x1, "Primaire")
        kit.chanfreiner(pc, 0.0006, f)
        for t in (0.030, 0.080):
            kit.vis_six_pans(pc, P(*pt(t, -0.001), x1 if signe > 0 else x0), GAUCHE * signe, r=0.0021, h=0.0010,
                             mat="AcierBrut")
    a.objet(pc)
    # l'anneau de dragonne perdu, remplacé par une boucle de fil de fer
    pc = a.piece("Dragonne", "Fil")
    c = pt(LONGUEUR + 0.0090, -0.0215)
    boucle = kit.cercle(P(*c), GAUCHE, 0.0058, 10)
    kit.balayage(pc, boucle, kit.section_cercle(0.0011, 4), "Fil", ferme=True)
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "MarquageNoir")
    a.texte(pc, a.code, 0.0068, P(0.145, 0.0828, 0.0152), ca.VERS_CROSSE, HAUT, mat="MarquageNoir")
    a.texte(pc, "N° 3050", 0.0035, P(0.120, 0.0380, -0.0131), ca.VERS_BOUCHE, HAUT, mat="Marquage",
            decalage=0.0003, resolution=1)
    ca.mire_crash(pc, P(0.170, 0.0380, 0.0131), GAUCHE, r=0.0045)
    ca.encoches(pc, 0.135, 0.0275, 0.0335, 0.0131, 6, mat="Marquage", largeur=0.0010, pas=0.0030)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    barillet(a)
    canon(a)
    poignee(a)
    mire(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(-0.024, H_MIRE), P(AV_BOUCHE, H_AXE))
    a.accroches({
        "Viseur": P(0.060, 0.1045),                 # rail du dessus, au-dessus du barillet
        "Laser": P(0.148, 0.0155),                  # rail sous le canon (laser tactique)
        "Poignee": P(*pt(0.055, 0.0)),              # manchon de poignée
        "Chargeur": P(0.059, H_BARILLET),           # le barillet : un chargeur rapide
    })


VUES = [
    ("visee", P(-0.40, H_MIRE + 0.0015), P(1.0, H_MIRE), 40),
    ("detail_barillet", P(0.07, 0.11, 0.17), P(0.07, 0.065, 0.0), 45),
    ("detail_poignee", P(0.02, 0.03, 0.17), P(-0.005, 0.0, 0.0), 45),
]

ATELIER = ca.principal("RV-30", "revolver série 30", "Revolver série 30", construire, (AV_BOUCHE, H_AXE),
                       vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
