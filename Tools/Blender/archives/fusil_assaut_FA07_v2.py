"""Fusil d'assaut FA-07 de Duel Arena (Blender 5.2, low poly).

Le FA-07 est une arme du stock militaire du complexe (bible § 12), restée en service longtemps
après la disparition des humains et entretenue par les robots avec ce qu'ils trouvaient (bible § 5) :
peinture usée jusqu'au métal sur les arêtes, crosse squelette récupérée à l'apprêt rouge et
soudée au tube, jambe fendue ligaturée au fil de fer, garde-main fendu tenu au ruban, compteur de
munitions mort barré d'une croix de ruban, plaque rivetée, goupille perdue remplacée par un
boulon, encoches de victoires. Mire métallique à œilleton, précise.

Pour les animations : le puits du chargeur est là où la main gauche du clip de rechargement va
le chercher, et le levier d'armement en haut à gauche, à l'aplomb de la poignée, là où elle arme.

Lancement : voir commun_armes.principal() ;
  blender -b --factory-startup -P fusil_assaut.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
"""

import importlib
import os
import sys

import bpy  # noqa: F401
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_armes as ca  # noqa: E402

importlib.reload(ca)
kit = ca.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

H_AXE = 0.138          # axe du canon (Bouche du fusil actuel : y = 0,1378 dans Unity)
AV_BOUCHE = 0.700      # face avant du frein de bouche
H_RAIL = 0.1822        # sommet des dents du rail du dessus
H_VISEE = 0.2065       # ligne de mire : centre de l'œilleton, pointe du guidon
AV_HAUSSE = -0.048
AV_GUIDON = 0.548

# Section du garde-main (x, h) : flancs droits, chanfreins hauts, quille en bas.
SECTION_GM = [(0.021, 0.170), (0.031, 0.160), (0.031, 0.108), (0.026, 0.084), (0.012, 0.063),
              (-0.012, 0.063), (-0.026, 0.084), (-0.031, 0.108), (-0.031, 0.160), (-0.021, 0.170)]

# Bords du chargeur, de haut en bas.
DOS, FACE = ca.courbes_chargeur((0.176, 0.034), (0.178, -0.028), (0.212, -0.083),
                                (0.249, 0.034), (0.253, -0.022), (0.283, -0.073))


def carcasse(a):
    pc = a.piece("Carcasse_Haute", "Peinture")
    f = kit.prisme(pc, [(-0.095, 0.108), (0.300, 0.108), (0.300, 0.161), (0.262, 0.170), (-0.083, 0.170),
                        (-0.095, 0.158)], -0.028, 0.028)
    kit.chanfreiner(pc, 0.0022, f)
    # couvre-culasse ouvert, sous la fenêtre d'éjection
    section = [(-0.0282, 0.1212), (-0.0298, 0.1222), (-0.0412, 0.1028), (-0.0396, 0.1018)]
    f = kit.loft(pc, kit.sections_av([(0.074, section), (0.148, section)]), "Peinture")
    kit.chanfreiner(pc, 0.0005, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.072, 0.150, 0.123, 0.149, -0.040, -0.017)            # fenêtre d'éjection
    kit.boite(cp, -0.030, 0.068, 0.1515, 0.1605, 0.021, 0.040)           # fente du levier d'armement
    a.couper(ob, cp, auto=True)

    pc = a.piece("Carcasse_Basse", "Peinture")
    f = kit.prisme(pc, [(-0.068, 0.112), (-0.068, 0.082), (-0.050, 0.050), (0.272, 0.050), (0.300, 0.094),
                        (0.300, 0.112)], -0.025, 0.025)
    kit.chanfreiner(pc, 0.002, f)
    f = kit.prisme(pc, [(0.165, 0.054), (0.165, 0.014), (0.173, 0.008), (0.254, 0.008), (0.263, 0.016),
                        (0.263, 0.054)], -0.0275, 0.0275)
    kit.chanfreiner(pc, 0.002, f)
    ca.pontet(pc, [(0.167, 0.036), (0.163, 0.008), (0.146, -0.004), (0.046, -0.004), (0.027, 0.010)])
    ca.detente(pc, 0.063, 0.052)
    a.objet(pc)

    # Commandes : sélecteur, arrêtoir, bouton du chargeur, levier d'armement, goupilles.
    pc = a.piece("Commandes", "Acier")
    kit.cylindre(pc, P(0.006, 0.090, 0.0245), P(0.006, 0.090, 0.0292), 0.0062, 10)
    f = kit.prisme(pc, [(0.004, 0.0935), (0.029, 0.0862), (0.030, 0.0822), (0.004, 0.0868)], 0.0288, 0.0312)
    kit.chanfreiner(pc, 0.0006, f)
    f = kit.boite(pc, 0.138, 0.150, 0.083, 0.095, 0.0245, 0.0278)
    kit.chanfreiner(pc, 0.0008, f)
    kit.cylindre(pc, P(0.158, 0.078, -0.0245), P(0.158, 0.078, -0.0285), 0.0046, 8)
    f = kit.boite(pc, 0.050, 0.064, 0.1520, 0.1600, 0.022, 0.034)       # tige dans la fente
    f += kit.boite(pc, 0.048, 0.066, 0.1490, 0.1632, 0.034, 0.044)      # bouton, poussé à l'avant
    kit.chanfreiner(pc, 0.0012, f)
    for x0, x1 in ((0.0245, 0.0262), (-0.0245, -0.0262)):
        kit.cylindre(pc, P(-0.058, 0.100, x0), P(-0.058, 0.100, x1), 0.0042, 8)
    # la goupille avant, perdue, remplacée par un boulon et son écrou
    kit.vis_six_pans(pc, P(0.288, 0.099, 0.0250), GAUCHE, r=0.0056, h=0.0032, mat="AcierBrut")
    kit.cylindre(pc, P(0.288, 0.099, -0.0245), P(0.288, 0.099, -0.0335), 0.0026, 8, mat="AcierBrut")
    kit.vis_six_pans(pc, P(0.288, 0.099, -0.0250), -GAUCHE, r=0.0050, h=0.0034, mat="AcierBrut", phase=0.0)
    a.objet(pc)

    # Rails : dessus sur toute la longueur (viseurs), dessous (poignée), flanc droit (laser tactique).
    pc = a.piece("Rails", "Peinture")
    ca.rail(pc, -0.082, 0.557, P(-0.082, 0.169), "haut")
    ca.rail(pc, 0.395, 0.525, P(0.395, 0.0635), "bas")
    ca.rail(pc, 0.437, 0.497, P(0.437, 0.122, -0.0305), "droite")
    a.objet(pc)


def garde_main(a):
    centre = (0.0, 0.12)
    enfonce = list(SECTION_GM)
    enfonce[6] = (-0.0215, 0.0885)   # coin avant droit enfoncé (un choc)
    enfonce[5] = (-0.0105, 0.0655)
    sections = [(0.300, SECTION_GM), (0.534, SECTION_GM), (0.558, kit.mise_a_echelle(enfonce, 0.975, centre)),
                (0.578, kit.mise_a_echelle(SECTION_GM, 0.86, centre))]
    pc = a.piece("Garde_Main", "Peinture")
    f = kit.loft(pc, kit.sections_av(sections), "Peinture")
    kit.chanfreiner(pc, 0.0018, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.404, 0.439, 0.474):
        kit.boite(cp, av, av + 0.024, 0.113, 0.131, 0.024, 0.040)
        kit.boite(cp, av, av + 0.024, 0.113, 0.131, -0.040, -0.024)
    kit.boite(cp, 0.395, 0.500, 0.1442, 0.1474, 0.0285, 0.040)            # rainures longitudinales
    kit.boite(cp, 0.395, 0.500, 0.1442, 0.1474, -0.040, -0.0285)
    a.couper(ob, cp)


def canon(a):
    pc = a.piece("Canon", "Bouche")
    kit.cylindre(pc, P(0.555, H_AXE), P(0.640, H_AXE), 0.0095, 12, phase=0.0)
    f = kit.tranches(pc, P(0.585, H_AXE), P(0.598, H_AXE), [0.0122, 0.0128, 0.0122], n=8, mat="Peinture")
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    a.objet(pc)
    pc = a.piece("Frein_Bouche", "Bouche")
    f = kit.tranches(pc, P(0.634, H_AXE), P(AV_BOUCHE, H_AXE), [0.0135, 0.0168, 0.0168, 0.0140], n=8,
                     positions=[0.0, 0.09, 0.86, 1.0])
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.645, 0.664):
        kit.boite(cp, av, av + 0.0125, H_AXE - 0.0072, H_AXE + 0.0072, -0.030, 0.030)
    kit.boite(cp, 0.650, 0.672, H_AXE + 0.002, H_AXE + 0.025, -0.0045, 0.0045)
    kit.cylindre(cp, P(0.688, H_AXE), P(0.712, H_AXE), 0.0048, 8)
    a.couper(ob, cp, auto=True)


def crosse(a):
    """La crosse standard : un groupe à part, qu'une crosse de l'armurerie remplacera."""
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    peinture = "Primaire"   # récupérée sur une autre arme, restée à l'apprêt
    pc = a.piece("Tube_Crosse", "Peinture")
    kit.cylindre(pc, P(-0.092, 0.130), P(-0.222, 0.130), 0.0150, 10)
    f = kit.cylindre(pc, P(-0.092, 0.130), P(-0.101, 0.130), 0.0174, 10, mat="Acier")
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    a.objet(pc)   # le tube appartient à la carcasse : une autre crosse s'y emmanche

    pc = a.piece("Crosse_Corps", peinture)
    f = kit.prisme(pc, [(-0.118, 0.150), (-0.132, 0.157), (-0.270, 0.162), (-0.287, 0.159), (-0.287, 0.030),
                        (-0.274, 0.022), (-0.246, 0.036), (-0.150, 0.097), (-0.118, 0.112)], -0.020, 0.020)
    kit.chanfreiner(pc, 0.0025, f)
    ob = a.objet(pc)
    cp = Piece("coupe", peinture)
    kit.prisme(cp, [(-0.168, 0.119), (-0.182, 0.143), (-0.257, 0.149), (-0.264, 0.141), (-0.264, 0.059),
                    (-0.252, 0.054)], -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.287, 0.166), (-0.296, 0.163), (-0.300, 0.150), (-0.300, 0.036), (-0.297, 0.022),
                        (-0.287, 0.017)], -0.0225, 0.0225)
    kit.chanfreiner(pc, 0.003, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Marquages", "Marquage")
    ca.mire_crash(pc, P(-0.2745, 0.104, 0.0204), GAUCHE)
    ca.encoches(pc, -0.200, 0.149, 0.156, 0.0203, 7)
    a.objet(pc, angle=None).parent = groupe

    # la crosse récupérée, soudée au tube ; sa jambe fendue, ligaturée
    pc = a.piece("Soudure_Crosse", "Soudure")
    anneau = kit.cercle(P(-0.1186, 0.130), Vector((0, 1, 0)), 0.0156, 22)
    kit.soudure(pc, anneau + [anneau[0]], r=0.0021, mat="Soudure", rng=a.rng, pas=0.0028, cotes=5)
    a.objet(pc).parent = groupe
    pc = a.piece("Ligature", "Fil")
    spires = kit.helice(P(-0.204, 0.0765), Vector((0, -0.844, 0.536)), 0.0216, 0.0129, 0.021, 5, pas_par_tour=8)
    kit.balayage(pc, spires, kit.section_cercle(0.0014, 3), "Fil")
    a.objet(pc).parent = groupe


def rafistolages(a):
    ex, ey = Vector((1, 0, 0)), Vector((0, 0, 1))
    pc = a.piece("Ruban_GardeMain", "Ruban")
    for av, largeur, incl, rabat in ((0.512, 0.019, 0.05, None), (0.531, 0.018, -0.07, (3, 0.012))):
        kit.bande(pc, SECTION_GM, P(av, 0.0), ex, ey, AVANT, largeur, 0.0011, "Ruban", inclinaison=incl,
                  irregularite=0.0007, rng=a.rng, rabat=rabat)
    a.objet(pc)

    pc = a.piece("Plaque_Rivetee", "AcierBrut")
    f = kit.prisme(pc, [(0.040, 0.060), (0.122, 0.057), (0.126, 0.096), (0.052, 0.101), (0.039, 0.090)], 0.0249, 0.0266)
    kit.chanfreiner(pc, 0.0007, f)
    for av, h in ((0.046, 0.064), (0.084, 0.0615), (0.119, 0.062), (0.121, 0.091), (0.086, 0.097), (0.047, 0.089)):
        kit.rivet(pc, P(av, h, 0.0266), GAUCHE, mat="AcierBrut")
    a.objet(pc)

    pc = a.piece("Compteur_Munitions", "Polymere")
    ca.compteur(pc, -0.074, -0.004, 0.114, 0.146, 0.0280, vivant=False)
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0115, P(0.17, 0.1505, 0.0280), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 0412", 0.0048, P(0.105, 0.068, -0.0250), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_pistolet(a)
    ca.chargeur(a, DOS, FACE, 0.0125)
    garde_main(a)
    canon(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE)
    ca.guidon(a, AV_GUIDON, H_RAIL, H_VISEE)
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE))
    a.accroches({
        "Viseur": P(0.100, H_RAIL),                 # sur le rail du dessus, au-dessus de la carcasse
        "Laser": P(0.467, 0.122, -0.043),          # rail du flanc droit (laser tactique)
        "Poignee": P(0.460, 0.0515),               # rail du dessous
        "Chargeur": P(0.2125, 0.034),              # haut du chargeur, dans le puits
        "Crosse": P(-0.095, 0.130),                # arrière de la carcasse, dans l'axe du tube
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_arriere", P(-0.10, 0.27, 0.19), P(0.02, 0.15, 0.0), 45),
    ("detail_crosse", P(-0.21, 0.16, 0.26), P(-0.21, 0.10, 0.0), 50),
]

ATELIER = ca.principal("FA-07", "fusil d'assaut", "Fusil d'assaut", construire, (AV_BOUCHE, H_AXE), vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
