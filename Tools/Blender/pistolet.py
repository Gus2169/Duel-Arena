"""Pistolet PA-05 de Duel Arena (Blender 5.2, low poly).

Un pistolet de service lourd, tenu à deux mains (WeaponModel.pistolHold). Le chargeur est dans la
poignée, comme celui du raygun du jeu. Hausse à cran et guidon à point clair ; une plaque
d'adaptation sur la culasse (viseur point rouge) et un rail sous le canon (laser tactique)
attendent les accessoires.

Le PA-05 : poignée cerclée de chambre à air, semelle de chargeur dépareillée en alu brut, arêtes
de culasse polies par les mains, trois encoches.
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

H_AXE = 0.066
AV_BOUCHE = 0.168
H_MIRE = 0.0910
T = (0.010, 0.040)          # haut de l'axe de la poignée
INCLINAISON = math.radians(18)
U = (-math.sin(INCLINAISON), -math.cos(INCLINAISON))   # le long de la poignée, vers le bas
N = (math.cos(INCLINAISON), -math.sin(INCLINAISON))    # vers l'avant de la poignée
LONGUEUR = 0.110

SECTION_CULASSE = [(0.0145, 0.045), (0.0145, 0.076), (0.0102, 0.084), (-0.0102, 0.084), (-0.0145, 0.076),
                   (-0.0145, 0.045)]


def pt(t, d):
    return (T[0] + U[0] * t + N[0] * d, T[1] + U[1] * t + N[1] * d)


AVANT_POIGNEE = [(0.0, 0.024), (0.028, 0.0222), (0.052, 0.0240), (0.080, 0.0222), (LONGUEUR, 0.0240)]
ARRIERE_POIGNEE = [(LONGUEUR, -0.0240), (0.070, -0.0262), (0.030, -0.0250), (0.004, -0.0230)]


def culasse(a):
    centre = (0.0, 0.064)
    sections = [(-0.030, SECTION_CULASSE), (0.150, SECTION_CULASSE),
                (0.165, kit.mise_a_echelle(SECTION_CULASSE, 0.86, centre))]
    pc = a.piece("Culasse", "Peinture")
    f = kit.loft(pc, kit.sections_av(sections), "Peinture")
    kit.chanfreiner(pc, 0.0015, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.040, 0.082, 0.070, 0.090, -0.020, -0.004)            # fenêtre d'éjection, à droite
    for i in range(6):                                                    # crans de prise
        av = -0.022 + i * 0.0052
        kit.boite(cp, av, av + 0.0019, 0.052, 0.0745, 0.0130, 0.020)
        kit.boite(cp, av, av + 0.0019, 0.052, 0.0745, -0.020, -0.0130)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Culasse_Details", "Acier")
    kit.boite(pc, 0.041, 0.081, 0.0665, 0.0815, -0.0075, 0.0060, "Acier")   # le canon dans la fenêtre
    kit.cylindre(pc, P(0.150, H_AXE), P(AV_BOUCHE, H_AXE), 0.0068, 10, mat="Bouche")
    kit.disque(pc, P(AV_BOUCHE + 0.0003, H_AXE), AVANT, 0.0042, 10, mat="Interieur")
    # plaque d'adaptation (viseur point rouge) et ses vis
    f = kit.boite(pc, 0.000, 0.040, 0.0838, 0.0856, -0.0092, 0.0092, "Peinture")
    kit.chanfreiner(pc, 0.0005, f)
    for av in (0.006, 0.034):
        kit.cylindre(pc, P(av, 0.0855), P(av, 0.0870), 0.0018, 6, mat="AcierBrut")
    a.objet(pc)

    # hausse à cran et guidon : le cran est découpé dans la hausse
    pc = a.piece("Hausse", "Peinture")
    f = kit.boite(pc, -0.024, -0.012, 0.0835, 0.0935, -0.0085, 0.0085)
    kit.chanfreiner(pc, 0.0008, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, -0.030, -0.006, 0.0885, 0.0960, -0.0017, 0.0017)
    a.couper(ob, cp)
    pc = a.piece("Guidon", "Peinture")
    f = kit.boite(pc, 0.148, 0.156, 0.0835, 0.0932, -0.0016, 0.0016)
    kit.chanfreiner(pc, 0.0004, f)
    kit.disque(pc, P(0.1479, 0.0912), Vector((0, 1, 0)), 0.0010, 6, mat="Marquage")
    a.objet(pc)


def carcasse(a):
    pc = a.piece("Carcasse", "Polymere")
    f = kit.prisme(pc, [(-0.034, 0.047), (-0.036, 0.040), (-0.024, 0.031), (0.030, 0.028), (0.150, 0.028),
                        (0.152, 0.040), (0.150, 0.047)], -0.0135, 0.0135)
    kit.chanfreiner(pc, 0.0018, f)
    poignee = [pt(t, d) for t, d in AVANT_POIGNEE] + [pt(t, d) for t, d in ARRIERE_POIGNEE]
    f = kit.prisme(pc, poignee, -0.0155, 0.0155)
    kit.chanfreiner(pc, 0.0035, f)
    ca.pontet(pc, [(0.086, 0.030), (0.082, 0.004), (0.066, -0.006), (0.036, -0.006), (0.024, 0.006)])
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    ca.detente(pc, 0.051, 0.031, echelle=0.72)
    f = kit.boite(pc, 0.028, 0.062, 0.0455, 0.0500, 0.0130, 0.0152)       # arrêtoir de culasse
    kit.chanfreiner(pc, 0.0005, f)
    kit.cylindre(pc, P(0.027, 0.022, 0.0135), P(0.027, 0.022, 0.0160), 0.0034, 8)   # bouton du chargeur
    kit.cylindre(pc, P(0.098, 0.038, 0.0130), P(0.098, 0.038, 0.0150), 0.0030, 8)   # levier de démontage
    a.objet(pc)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, 0.090, 0.148, P(0.090, 0.0285), "bas", largeur=0.019, mat="Polymere")
    a.objet(pc)


def chargeur(a):
    """Le chargeur, dans la poignée : son corps (caché) et sa semelle, qu'un chargeur rallongé remplacera."""
    pc = a.piece("Chargeur_Standard", "Acier")
    corps = [pt(0.006, 0.016), pt(LONGUEUR, 0.016), pt(LONGUEUR, -0.016), pt(0.006, -0.016)]
    kit.prisme(pc, corps, -0.0105, 0.0105, "Acier")
    semelle = [pt(LONGUEUR - 0.002, 0.0255), pt(LONGUEUR + 0.0075, 0.0262), pt(LONGUEUR + 0.0075, -0.0250),
               pt(LONGUEUR - 0.002, -0.0245)]
    f = kit.prisme(pc, semelle, -0.0168, 0.0168, "AcierBrut")   # la semelle vient d'un autre chargeur
    kit.chanfreiner(pc, 0.0018, f)
    a.objet(pc)


def rafistolages(a):
    pc = a.piece("Chambre_A_Air", "Caoutchouc")   # la poignée cerclée de chambre à air
    ex, ey = Vector((1, 0, 0)), Vector((0, -N[0], N[1]))
    axe = Vector((0, -U[0], U[1]))

    def profondeur(liste, t):
        liste = sorted(liste)
        for (t0, d0), (t1, d1) in zip(liste, liste[1:]):
            if t0 <= t <= t1:
                return d0 + (d1 - d0) * (t - t0) / (t1 - t0)
        return liste[-1][1]

    for t, incl in ((0.040, 0.05), (0.072, -0.06)):
        df, db = profondeur(AVANT_POIGNEE, t), -profondeur(ARRIERE_POIGNEE, t)
        contour = kit.contour_rect(0.031, df + db, chanfrein=0.0045, cy=(df - db) / 2)
        kit.bande(pc, contour, P(*pt(t, 0.0)), ex, ey, axe, 0.014, 0.0016, "Caoutchouc", inclinaison=incl,
                  irregularite=0.0004, rng=a.rng)
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0072, P(0.098, 0.060, 0.0146), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 3301", 0.0035, P(0.110, 0.0375, -0.0136), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.mire_crash(pc, P(0.122, 0.0375, 0.0137), GAUCHE, r=0.0042)
    ca.encoches(pc, 0.084, 0.0335, 0.0405, 0.0137, 3, pas=0.003)
    a.objet(pc, angle=None)


def construire(a):
    culasse(a)
    carcasse(a)
    chargeur(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(-0.018, H_MIRE), P(AV_BOUCHE, H_AXE))
    a.accroches({
        "Viseur": P(0.020, 0.0856),                 # plaque d'adaptation sur la culasse
        "Laser": P(0.119, 0.0165),                  # rail sous le canon (laser tactique)
        "Poignee": P(*pt(0.055, 0.0)),              # manchon de poignée
        "Chargeur": P(*pt(LONGUEUR, 0.0)),          # semelle du chargeur
    })


VUES = [
    ("visee", P(-0.40, H_MIRE + 0.0015), P(1.0, H_MIRE), 40),
    ("detail_poignee", P(0.02, 0.03, 0.17), P(-0.005, 0.0, 0.0), 45),
]

ATELIER = ca.principal("PA-05", "pistolet", "Pistolet", construire, (AV_BOUCHE, H_AXE), vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
