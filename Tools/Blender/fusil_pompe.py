"""Fusil à pompe FAP-03 de Duel Arena (Blender 5.2, low poly).

Un fusil à pompe à magasin tubulaire. Les cartouches entrent une à une par la fenêtre de
chargement, sous la carcasse, là où la main gauche du clip de rechargement va et vient ; la
pompe est sous la main gauche au repos. Mire à anneau sur le rail de la carcasse, guidon au bout
de la bande ventilée.

Le FAP-03 : collier de serrage qui tient le tube au canon (le collier d'origine a cassé), pompe
lisse à force d'usage, regarnie de ruban toilé, crosse fendue rafistolée à l'agrafeuse, bouche
noircie.
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

H_AXE = 0.115
H_TUBE = 0.081
AV_BOUCHE = 0.720
H_RAIL = 0.152
H_VISEE = 0.168
AV_HAUSSE = -0.035
AV_GUIDON = 0.688

SECTION_POMPE = [(0.012, 0.1025), (0.0235, 0.098), (0.0255, 0.078), (0.021, 0.062), (0.010, 0.0575),
                 (-0.010, 0.0575), (-0.021, 0.062), (-0.0255, 0.078), (-0.0235, 0.098), (-0.012, 0.1025)]


def carcasse(a):
    pc = a.piece("Carcasse", "Peinture")
    f = kit.prisme(pc, [(-0.060, 0.064), (-0.060, 0.132), (-0.050, 0.140), (0.195, 0.140), (0.207, 0.130),
                        (0.207, 0.064)], -0.0245, 0.0245)
    kit.chanfreiner(pc, 0.0022, f)
    f = kit.prisme(pc, [(-0.048, 0.066), (-0.040, 0.046), (0.090, 0.046), (0.094, 0.066)], -0.0220, 0.0220)
    kit.chanfreiner(pc, 0.0018, f)
    ca.pontet(pc, [(0.088, 0.050), (0.084, 0.020), (0.070, 0.006), (0.040, 0.004), (0.024, 0.014)])
    ca.detente(pc, 0.058, 0.050)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.075, 0.150, 0.103, 0.132, -0.040, -0.016)     # fenêtre d'éjection
    kit.boite(cp, 0.098, 0.192, 0.055, 0.079, -0.0135, 0.0135)    # fenêtre de chargement, dessous
    a.couper(ob, cp, auto=True)

    pc = a.piece("Commandes", "Acier")
    kit.quad(pc, [P(0.099, 0.0788, -0.0128), P(0.191, 0.0788, -0.0128), P(0.191, 0.0788, 0.0128), P(0.099, 0.0788, 0.0128)],
             "Acier", normale=-HAUT)                             # l'élévateur, au fond de la fenêtre
    kit.cylindre(pc, P(0.078, 0.057, 0.0215), P(0.078, 0.057, 0.0250), 0.0042, 8)     # sûreté
    kit.cylindre(pc, P(0.078, 0.057, -0.0215), P(0.078, 0.057, -0.0250), 0.0042, 8)
    f = kit.prisme(pc, [(0.004, 0.124), (0.028, 0.121), (0.028, 0.127), (0.004, 0.130)], 0.0244, 0.0285)
    kit.chanfreiner(pc, 0.0006, f)                               # verrou de culasse, là où la main gauche arme
    a.objet(pc)

    pc = a.piece("Rails", "Peinture")
    ca.rail(pc, -0.052, 0.195, P(-0.052, 0.1395), "haut")
    ca.rail(pc, 0.584, 0.601, P(0.584, 0.098, -0.0158), "droite", pas=0.006, longueur_dent=0.003)
    a.objet(pc)


def canon_et_tube(a):
    r_canon = 0.0125
    pc = a.piece("Canon", "Bouche")
    kit.cylindre(pc, P(0.200, H_AXE), P(0.708, H_AXE), r_canon, 10, phase=0.0)
    kit.anneau(pc, 0.704, AV_BOUCHE, H_AXE, 0.0142, 0.0090, 10, "Bouche", phase=0.0)
    kit.disque(pc, P(0.7083, H_AXE), AVANT, 0.0089, 10, mat="Interieur")
    a.objet(pc)

    # bande ventilée : elle porte le guidon
    pc = a.piece("Bande_Ventilee", "Peinture")
    haut_canon = H_AXE + r_canon
    for av in (0.240, 0.350, 0.460, 0.570, 0.680):
        kit.boite(pc, av - 0.004, av + 0.004, haut_canon - 0.001, 0.1345, -0.0025, 0.0025)
    f = kit.boite(pc, 0.212, 0.694, 0.1335, 0.1405, -0.0042, 0.0042)
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc)

    # magasin tubulaire, bouchon, collier avant qui tient le tube au canon
    pc = a.piece("Tube_Magasin", "Peinture")
    kit.cylindre(pc, P(0.207, H_TUBE), P(0.600, H_TUBE), 0.0118, 8)
    f = kit.cylindre(pc, P(0.600, H_TUBE), P(0.616, H_TUBE), 0.0128, 8, mat="Acier")
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    f = kit.boite(pc, 0.584, 0.601, 0.067, H_AXE + r_canon + 0.0005, -0.0158, 0.0158)
    kit.chanfreiner(pc, 0.0015, f)
    a.objet(pc)


def pompe(a):
    pc = a.piece("Pompe", "Polymere")
    centre = (0.0, H_TUBE)
    sections = [(0.330, kit.mise_a_echelle(SECTION_POMPE, 0.93, centre)), (0.338, SECTION_POMPE),
                (0.482, SECTION_POMPE), (0.490, kit.mise_a_echelle(SECTION_POMPE, 0.93, centre))]
    f = kit.loft(pc, kit.sections_av(sections), "Polymere")
    kit.chanfreiner(pc, 0.0015, f)
    for x0, x1 in ((0.0112, 0.0134), (-0.0134, -0.0112)):      # barres d'armement, le long du tube
        kit.boite(pc, 0.207, 0.334, H_TUBE - 0.0025, H_TUBE + 0.0025, x0, x1, "Acier")
    # lissée par l'usage, regarnie de ruban toilé
    for av, incl in ((0.372, 0.05), (0.410, -0.04), (0.448, 0.06)):
        kit.bande(pc, SECTION_POMPE, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.024, 0.0013,
                  "RubanNoir", inclinaison=incl, irregularite=0.0006, rng=a.rng)
    a.objet(pc)


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Polymere")
    f = kit.prisme(pc, [(-0.058, 0.136), (-0.075, 0.134), (-0.270, 0.132), (-0.284, 0.127), (-0.284, 0.024),
                        (-0.270, 0.016), (-0.195, 0.040), (-0.120, 0.058), (-0.085, 0.060), (-0.058, 0.066)],
                   -0.0185, 0.0185)
    kit.chanfreiner(pc, 0.003, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Polymere")   # trou de pouce arrondi, juste derrière la poignée
    trou = [(-0.150 + math.cos(t) * 0.036, 0.093 + math.sin(t) * 0.016) for t in [2 * math.pi * i / 12 for i in range(12)]]
    kit.prisme(cp, trou, -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.284, 0.137), (-0.293, 0.134), (-0.296, 0.120), (-0.296, 0.030), (-0.292, 0.018),
                        (-0.284, 0.014)], -0.0205, 0.0205)
    kit.chanfreiner(pc, 0.003, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Marquages", "Marquage")
    x = 0.0186
    ca.mire_crash(pc, P(-0.258, 0.102, x), GAUCHE, r=0.0090)
    ca.encoches(pc, -0.205, 0.120, 0.127, x, 4)
    # la fente, et les agrafes qui la tiennent
    fente = [(-0.214, 0.036), (-0.220, 0.047), (-0.217, 0.056), (-0.225, 0.068), (-0.222, 0.078), (-0.230, 0.090)]
    for (a0, b0), (a1, b1) in zip(fente, fente[1:]):
        d = Vector((a1 - a0, b1 - b0)).normalized()
        nn = Vector((-d.y, d.x)) * 0.0006
        kit.quad(pc, [P(a0 - nn.x, b0 - nn.y, x), P(a1 - nn.x, b1 - nn.y, x), P(a1 + nn.x, b1 + nn.y, x),
                      P(a0 + nn.x, b0 + nn.y, x)], "Interieur", normale=GAUCHE)
    for (a0, b0) in ((-0.2175, 0.042), (-0.219, 0.0555), (-0.2235, 0.072), (-0.226, 0.084)):
        kit.boite(pc, a0 - 0.0055, a0 + 0.0055, b0 - 0.0008, b0 + 0.0008, x - 0.0004, x + 0.0009, "AcierBrut")
    a.objet(pc, angle=None).parent = groupe


def rafistolages(a):
    # collier de serrage autour du canon et du tube : le collier d'origine a cassé
    pc = a.piece("Collier_Tube", "AcierBrut")
    rb, rt = 0.0125, 0.0118
    contour = [(math.cos(t) * rb, H_AXE + math.sin(t) * rb) for t in [math.pi * i / 6 for i in range(7)]]
    contour += [(math.cos(t) * rt, H_TUBE + math.sin(t) * rt) for t in [math.pi + math.pi * i / 6 for i in range(7)]]
    kit.bande(pc, contour, P(0.540, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.008, 0.0009, "AcierBrut")
    f = kit.boite(pc, 0.534, 0.546, 0.091, 0.105, 0.0120, 0.0168)
    kit.chanfreiner(pc, 0.0007, f)
    kit.cylindre(pc, P(0.540, 0.088, 0.0148), P(0.540, 0.108, 0.0148), 0.0026, 6)
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0105, P(0.105, 0.120, 0.0245), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 0077", 0.0045, P(0.045, 0.118, -0.0245), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_pistolet(a, ruban=False)
    canon_et_tube(a)
    pompe(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE, r_int=0.0052, r_ext=0.0072)
    ca.guidon(a, AV_GUIDON, 0.1405, H_VISEE, demi_base=0.0058)
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.410, 0.0575))
    a.accroches({
        "Viseur": P(0.070, H_RAIL),
        "Laser": P(0.5925, 0.098, -0.0280),       # le petit rail du collier avant, à droite (laser tactique)
        "Poignee": P(0.410, 0.0575),              # sous la pompe (poignée de pompe)
        "Chargeur": P(0.616, H_TUBE),             # bout du tube : rallonge de magasin
        "Crosse": P(-0.058, 0.100),
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_dessous", P(0.10, -0.10, 0.18), P(0.14, 0.06, 0.0), 45),
    ("detail_crosse", P(-0.16, 0.13, 0.22), P(-0.16, 0.09, 0.0), 50),
]

ATELIER = ca.principal("FAP-03", "fusil à pompe", "Fusil à pompe", construire, (AV_BOUCHE, H_AXE), vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
