"""Fusil de précision laser FX-2 de Duel Arena (Blender 5.2, low poly) : la classe précision, en arme à faisceau.

Un prototype du programme d'essai, aux formes franchement futuristes (référence : les armes à
énergie d'Apex Legends) : à la place du canon, deux poutres conductrices tiennent entre elles un
accélérateur qui luit, cerclé de bobines et visible de part en part ; elles finissent en deux dents
de part et d'autre de la lentille. Lunette anguleuse ×4 d'origine (« Lunette_Standard »,
remplaçable par un autre viseur), pas de mire de secours.

Il a servi : une poutre fêlée tenue par un collier de serrage, un câble de dérivation tiré à la main
de la carcasse à l'émetteur et attaché aux entretoises, un panneau de céramique remplacé par une
tôle à l'apprêt rouge, l'appui-joue fendu tenu au ruban, les dents de l'émetteur noircies.

Pour les animations, il garde les points de prise du FP-12 : poignée à l'origine, cellule d'énergie
dans le puits où la main gauche du clip de rechargement va chercher le chargeur, levier de charge
en haut à gauche au-dessus de la poignée, poutre basse sous la main gauche, crosse à l'épaule.

  blender -b --factory-startup -P laser_precision.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.108           # axe de l'accélérateur, entre les deux poutres
AV_BOUCHE = 0.842       # devant de la lentille
H_LUNETTE = 0.186       # axe de la lunette
AV_OCULAIRE = -0.082    # arrière de la bonnette : la mire
H_PLAQUE = 0.152        # dessus de la carcasse, sous la plaque de montage

# Cellule d'énergie : large et droite, dans le puits du chargeur du FP-12.
DOS, FACE = ca.courbes_chargeur((0.168, 0.040), (0.172, -0.012), (0.176, -0.064),
                                (0.252, 0.040), (0.255, -0.010), (0.258, -0.060), n=4)

CARCASSE = [(-0.095, ca.section8(0.022, 0.090, 0.142, 0.008, 0.004)),
            (-0.070, ca.section8(0.027, 0.084, 0.152, 0.010, 0.004)),
            (0.120, ca.section8(0.029, 0.080, 0.152, 0.011, 0.004)),
            (0.262, ca.section8(0.030, 0.080, 0.152, 0.011, 0.004)),
            (0.300, ca.section8(0.030, 0.076, 0.150, 0.011, 0.004))]

# Les deux poutres : la haute en céramique, la basse sous la main gauche ; leurs faces intérieures
# (les rails conducteurs) en cuivre.
POUTRE_HAUTE = [(0.288, ca.section8(0.0200, 0.124, 0.146, 0.006, 0.004)),
                (0.700, ca.section8(0.0190, 0.124, 0.146, 0.006, 0.004)),
                (0.796, ca.section8(0.0165, 0.1255, 0.1440, 0.006, 0.004))]
POUTRE_BASSE = [(0.288, ca.section8(0.0220, 0.064, 0.092, 0.005, 0.008, 0.70)),
                (0.720, ca.section8(0.0200, 0.064, 0.092, 0.005, 0.008, 0.70)),
                (0.796, ca.section8(0.0180, 0.072, 0.092, 0.005, 0.006, 0.75))]
# Carénages : là où les poutres sortent de la carcasse.
CARENAGE_HAUT = [(0.296, ca.section8(0.0255, 0.118, 0.150, 0.009, 0.004)),
                 (0.338, ca.section8(0.0205, 0.1225, 0.1468, 0.0065, 0.004))]
CARENAGE_BAS = [(0.288, ca.section8(0.0265, 0.062, 0.098, 0.005, 0.008, 0.72)),
                (0.334, ca.section8(0.0226, 0.0632, 0.0928, 0.005, 0.008, 0.70))]
# Les dents de l'émetteur, qui se resserrent un peu vers l'axe.
DENT_HAUTE = [(0.834, ca.section8(0.0135, 0.1230, 0.1395, 0.004, 0.003)),
              (0.878, ca.section8(0.0110, 0.1220, 0.1360, 0.004, 0.003)),
              (0.902, ca.section8(0.0045, 0.1215, 0.1275, 0.002, 0.0015))]
DENT_BASSE = [(0.834, ca.section8(0.0135, 0.0765, 0.0930, 0.003, 0.004)),
              (0.870, ca.section8(0.0110, 0.0805, 0.0945, 0.003, 0.004)),
              (0.892, ca.section8(0.0045, 0.0885, 0.0945, 0.0015, 0.002))]
BAGUES = (0.477, 0.619)   # les bagues qui tiennent les poutres, entre deux bobines de l'accélérateur
SECTION_BAGUE = ca.section8(0.0235, 0.0605, 0.1500, 0.0075, 0.0070, 0.85)
X_CABLE = 0.0257          # le câble de dérivation passe sur le flanc gauche des bagues


def carcasse(a):
    pc = a.piece("Carcasse_Haute", "Peinture")
    f = kit.loft(pc, kit.sections_av(CARCASSE), "Peinture")
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.boite(pc, -0.099, -0.091, 0.094, 0.134, -0.016, 0.016, "Acier")   # tenon de la crosse
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, -0.020, 0.064, 0.130, 0.139, 0.020, 0.040)        # fente du levier de charge (gauche)
    for av in (0.040, 0.066, 0.092):                                 # évents de chaleur (droite)
        kit.boite(cp, av, av + 0.018, 0.098, 0.134, -0.040, -0.024)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Carcasse_Basse", "Peinture")
    f = kit.prisme(pc, [(-0.072, 0.092), (-0.072, 0.074), (-0.054, 0.048), (0.150, 0.048), (0.150, 0.020),
                        (0.160, 0.010), (0.260, 0.010), (0.272, 0.022), (0.290, 0.044), (0.292, 0.092)],
                   -0.0255, 0.0255)
    kit.chanfreiner(pc, 0.0022, f)
    f = kit.prisme(pc, [(0.154, 0.030), (0.154, 0.014), (0.163, 0.004), (0.257, 0.004), (0.268, 0.014),
                        (0.268, 0.030)], -0.0285, 0.0285)                 # collerette du puits de la cellule
    kit.chanfreiner(pc, 0.0018, f)
    ca.pontet(pc, [(0.154, 0.030), (0.150, 0.004), (0.132, -0.006), (0.048, -0.006), (0.028, 0.010)])
    ca.detente(pc, 0.064, 0.050)
    a.objet(pc)

    # panneau de céramique d'origine à gauche ; à droite, il a cassé (voir rafistolages)
    pc = a.piece("Panneau_Carcasse", "Ceramique")
    f = kit.prisme(pc, PANNEAU, 0.0286, 0.0318)
    kit.chanfreiner(pc, 0.0007, f)
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.cylindre(pc, P(0.006, 0.066, 0.0250), P(0.006, 0.066, 0.0292), 0.0060, 10)          # sélecteur
    f = kit.prisme(pc, [(0.004, 0.0695), (0.028, 0.0622), (0.029, 0.0585), (0.004, 0.0628)], 0.0288, 0.0310)
    kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(0.150, 0.066, -0.0250), P(0.150, 0.066, -0.0290), 0.0046, 8)        # bouton de la cellule
    f = kit.boite(pc, 0.046, 0.058, 0.1310, 0.1380, 0.022, 0.032)                           # levier de charge
    f += kit.boite(pc, 0.042, 0.062, 0.1275, 0.1415, 0.032, 0.043, "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    kit.quad(pc, [P(0.045, 0.1320, 0.0431), P(0.059, 0.1320, 0.0431), P(0.059, 0.1370, 0.0431),
                  P(0.045, 0.1370, 0.0431)], "Lueur", normale=GAUCHE)                       # témoin du levier
    for av in (0.040, 0.066, 0.092):                                                        # fond de l'évent
        kit.quad(pc, [P(av + 0.0005, 0.0985, -0.0243), P(av + 0.0175, 0.0985, -0.0243),
                      P(av + 0.0175, 0.1335, -0.0243), P(av + 0.0005, 0.1335, -0.0243)], "Lueur", normale=-GAUCHE)
    a.objet(pc)


PANNEAU = [(0.124, 0.090), (0.256, 0.088), (0.264, 0.112), (0.256, 0.139), (0.132, 0.140), (0.122, 0.130)]


def canon(a):
    """Les deux poutres, leurs carénages, les entretoises et l'accélérateur."""
    pc = a.piece("Poutre_Haute", "Ceramique")
    mats = ca.mats8(dessus="Peinture", chanfrein_haut="Ceramique", flancs="Ceramique", chanfrein_bas="Peinture",
                    dessous="Bobine")
    f = kit.loft(pc, kit.sections_av(POUTRE_HAUTE), "Ceramique", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    fentes = [(0.736, 0.746), (0.752, 0.762), (0.768, 0.778)]
    for av0, av1 in fentes:                                                     # évents du dessus
        kit.boite(cp, av0, av1, 0.1390, 0.152, -0.0090, 0.0090)
    a.couper(ob, cp)
    pc = a.piece("Lueurs_Poutre", "Lueur")
    ca.lueurs_dessus(pc, fentes, 0.1393, 0.0088)
    a.objet(pc, angle=None)

    pc = a.piece("Poutre_Basse", "Peinture")
    mats = ca.mats8(dessus="Bobine", chanfrein_haut="Peinture", flancs="Peinture", chanfrein_bas="Polymere",
                    dessous="Polymere")
    f = kit.loft(pc, kit.sections_av(POUTRE_BASSE), "Peinture", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0012, f)
    for av in (0.344, 0.369, 0.394, 0.419, 0.444):    # nervures sous la main gauche
        f = kit.boite(pc, av, av + 0.009, 0.0600, 0.0650, -0.0130, 0.0130, "Polymere")
        kit.chanfreiner(pc, 0.0007, f)
    a.objet(pc)

    pc = a.piece("Carenages", "Peinture")
    f = kit.loft(pc, kit.sections_av(CARENAGE_HAUT), "Peinture",
                 mats_cotes=ca.mats8(chanfrein_haut="Ceramique"))
    f += kit.loft(pc, kit.sections_av(CARENAGE_BAS), "Peinture",
                  mats_cotes=ca.mats8(chanfrein_bas="Polymere", dessous="Polymere"))
    kit.chanfreiner(pc, 0.0012, f)
    kit.anneau(pc, 0.298, 0.306, H_AXE, 0.0075, 0.0050, 10, "Bobine")   # douille où l'accélérateur entre
    for av in BAGUES:
        f = kit.loft(pc, kit.sections_av([(av - 0.010, SECTION_BAGUE), (av + 0.010, SECTION_BAGUE)]), "Peinture",
                     mats_cotes=ca.mats8(chanfrein_haut="Ceramique", chanfrein_bas="Polymere", dessous="Polymere"))
        kit.chanfreiner(pc, 0.0010, f)
        kit.quad(pc, [P(av - 0.0022, 0.1501, 0.0130), P(av + 0.0022, 0.1501, 0.0130), P(av + 0.0022, 0.1501, -0.0130),
                      P(av - 0.0022, 0.1501, -0.0130)], "Lueur", normale=HAUT)            # fente de lueur, dessus
        kit.quad(pc, [P(av - 0.0022, 0.0780, -0.0236), P(av + 0.0022, 0.0780, -0.0236), P(av + 0.0022, 0.1330, -0.0236),
                      P(av - 0.0022, 0.1330, -0.0236)], "Lueur", normale=-GAUCHE)         # … et à droite
    a.objet(pc)
    ca.coeur(a, 0.300, 0.796, H_AXE, r=0.0050, bobines=6, r_bobine=0.0100)


def emetteur(a):
    pc = a.piece("Emetteur", "Bouche")
    bloc = [(0.788, ca.section8(0.0225, 0.0630, 0.1500, 0.0075, 0.0070, 0.85)),
            (0.836, ca.section8(0.0205, 0.0670, 0.1470, 0.0070, 0.0065, 0.85))]
    f = kit.loft(pc, kit.sections_av(bloc), "Bouche")
    kit.chanfreiner(pc, 0.0010, f)
    for dent in (DENT_HAUTE, DENT_BASSE):
        f = kit.loft(pc, kit.sections_av(dent), "Bouche")
        kit.chanfreiner(pc, 0.0007, f)
    kit.anneau(pc, 0.834, AV_BOUCHE, H_AXE, 0.0120, 0.0082, 10, "Bouche")
    kit.disque(pc, P(0.8372, H_AXE), AVANT, 0.0083, 10, mat="Laser")
    for x0, x1 in ((0.0198, 0.0238), (-0.0238, -0.0198)):   # plaques de céramique sur les flancs du bloc
        f = kit.prisme(pc, [(0.792, 0.075), (0.831, 0.079), (0.831, 0.134), (0.792, 0.139)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    a.objet(pc)


def montages(a):
    """Interfaces d'accessoires : viseurs dessus, laser tactique à droite, bipied ou poignée dessous."""
    pc = a.piece("Montages", "Peinture")
    ca.plaque_montage(pc, -0.060, 0.220, P(-0.060, H_PLAQUE), "haut", largeur=0.022, ep=0.0035, encoches=10)
    ca.plaque_montage(pc, 0.522, 0.594, P(0.522, 0.080, -0.0206), "droite", largeur=0.012, encoches=3)
    ca.plaque_montage(pc, 0.636, 0.716, P(0.636, 0.0645), "bas", largeur=0.016, encoches=4)
    a.objet(pc)


def lunette(a):
    """La lunette ×4 d'origine, ses embases comprises : un groupe à part, remplaçable."""
    groupe = a.vide(a.nom("Lunette_Standard"), a.racine, "CUBE", 0.004)
    hl = H_LUNETTE

    def s8(demi, dessous, dessus, ch_haut, ch_bas):
        return ca.section8(demi, hl - dessous, hl + dessus, ch_haut, ch_bas)

    corps = [(-0.072, s8(0.0190, 0.0190, 0.0200, 0.0070, 0.0060)),
             (-0.047, s8(0.0190, 0.0190, 0.0200, 0.0070, 0.0060)),
             (-0.037, s8(0.0150, 0.0145, 0.0160, 0.0055, 0.0045)),
             (0.128, s8(0.0150, 0.0145, 0.0160, 0.0055, 0.0045)),
             (0.158, s8(0.0230, 0.0210, 0.0235, 0.0085, 0.0070)),
             (0.212, s8(0.0240, 0.0220, 0.0245, 0.0090, 0.0075))]
    pc = a.piece("Lunette", "Peinture")
    f = kit.loft(pc, kit.sections_av(corps), "Peinture", mats_cotes=ca.mats8(chanfrein_haut="Ceramique"))
    kit.chanfreiner(pc, 0.0008, f)
    # tourelles : hausse dessus, dérive à droite
    f = kit.boite(pc, 0.038, 0.066, hl + 0.0150, hl + 0.0290, -0.0100, 0.0100)
    f += kit.boite(pc, 0.038, 0.066, hl - 0.0090, hl + 0.0090, -0.0270, -0.0140)
    kit.chanfreiner(pc, 0.0012, f)
    # télémètre à gauche, côté joueur
    f = kit.boite(pc, -0.004, 0.096, hl - 0.0105, hl + 0.0085, 0.0140, 0.0215)
    kit.chanfreiner(pc, 0.0010, f)
    # visière de céramique au-dessus de l'objectif
    f = kit.prisme(pc, [(0.150, hl + 0.0225), (0.222, hl + 0.0235), (0.227, hl + 0.0268), (0.160, hl + 0.0282)],
                   -0.0170, 0.0170, "Ceramique")
    kit.chanfreiner(pc, 0.0008, f)
    # embases sur la plaque de montage
    for av in (0.004, 0.110):
        f = kit.boite(pc, av - 0.010, av + 0.010, H_PLAQUE + 0.0025, hl - 0.0120, -0.0130, 0.0130)
        kit.chanfreiner(pc, 0.0010, f)
        kit.cylindre(pc, P(av, 0.162, -0.0128), P(av, 0.162, -0.0172), 0.0035, 6, mat="Acier")
    ob = a.objet(pc)
    face = corps[-1][1]
    verre = kit.mise_a_echelle(face, 0.80, (0.0, hl))
    cp = Piece("coupe", "Interieur")   # l'objectif en retrait
    kit.loft(cp, [[P(0.2088, h, x) for x, h in verre], [P(0.2160, h, x) for x, h in verre]], "Interieur")
    a.couper(ob, cp, auto=True)   # corps, tourelles, embases et visière se chevauchent
    ob.parent = groupe

    pc = a.piece("Lunette_Bonnette", "Caoutchouc")
    arriere = corps[0][1]
    ext = kit.mise_a_echelle(arriere, 1.04, (0.0, hl))
    inte = kit.mise_a_echelle(arriere, 0.74, (0.0, hl))
    kit.loft(pc, [[P(-0.071, h, x) for x, h in ext], [P(AV_OCULAIRE, h, x) for x, h in ext],
                  [P(AV_OCULAIRE, h, x) for x, h in inte], [P(-0.071, h, x) for x, h in inte]], "Caoutchouc",
             cyclique=True)
    kit.quad(pc, [P(-0.0722, h, x) for x, h in inte], "Lentille", normale=Vector((0, 1, 0)))
    kit.quad(pc, [P(0.2090, h, x) for x, h in verre], "Lentille", normale=AVANT)
    kit.quad(pc, [P(0.004, hl - 0.0045, 0.0216), P(0.050, hl - 0.0045, 0.0216), P(0.050, hl + 0.0025, 0.0216),
                  P(0.004, hl + 0.0025, 0.0216)], "Lueur", normale=GAUCHE)   # lecture du télémètre
    kit.quad(pc, [P(0.060, hl - 0.0045, 0.0216), P(0.070, hl - 0.0045, 0.0216), P(0.070, hl + 0.0025, 0.0216),
                  P(0.060, hl + 0.0025, 0.0216)], "EcranMort", normale=GAUCHE)
    a.objet(pc).parent = groupe


PROFIL_CROSSE = [(-0.090, 0.142), (-0.296, 0.137), (-0.316, 0.132), (-0.322, 0.118), (-0.322, 0.026),
                 (-0.312, 0.014), (-0.284, 0.018), (-0.200, 0.048), (-0.124, 0.074), (-0.090, 0.088)]
AJOUR_CROSSE = [(-0.140, 0.086), (-0.160, 0.114), (-0.268, 0.116), (-0.282, 0.104), (-0.282, 0.046),
                (-0.266, 0.040), (-0.210, 0.060)]


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Peinture")
    f = kit.prisme(pc, PROFIL_CROSSE, -0.0205, 0.0205)
    kit.chanfreiner(pc, 0.0030, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Peinture")
    kit.prisme(cp, AJOUR_CROSSE, -0.03, 0.03)
    a.couper(ob, cp)
    ob.parent = groupe

    pc = a.piece("Appui_Joue", "Ceramique")   # l'appui-joue relevé, à hauteur de la lunette
    f = kit.prisme(pc, [(-0.128, 0.1405), (-0.288, 0.1365), (-0.296, 0.1430), (-0.284, 0.1500), (-0.150, 0.1530),
                        (-0.130, 0.1475)], -0.0160, 0.0160)
    kit.chanfreiner(pc, 0.0020, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Panneaux", "Ceramique")   # talon en céramique, des deux côtés
    for x0, x1 in ((0.0200, 0.0216), (-0.0216, -0.0200)):
        f = kit.prisme(pc, [(-0.286, 0.046), (-0.312, 0.034), (-0.312, 0.124), (-0.286, 0.112)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    f = kit.prisme(pc, [(-0.320, 0.134), (-0.330, 0.130), (-0.334, 0.118), (-0.334, 0.026), (-0.330, 0.014),
                        (-0.320, 0.012)], -0.0225, 0.0225, "Caoutchouc")   # plaque de couche
    kit.chanfreiner(pc, 0.0030, f)
    ca.mire_crash(pc, P(-0.299, 0.079, 0.0217), GAUCHE, r=0.0070)
    a.objet(pc).parent = groupe

    pc = a.piece("Ruban_Appui", "Ruban")   # l'appui-joue fendu, tenu au ruban autour de la crosse
    for av, h_bas, h_haut, incl in ((-0.190, 0.1146, 0.1521, 0.05), (-0.240, 0.1155, 0.1513, -0.06)):
        contour = kit.contour_rect(0.0410, h_haut - h_bas, chanfrein=0.0045, cy=(h_bas + h_haut) / 2)
        kit.bande(pc, contour, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.016, 0.0011, "Ruban",
                  inclinaison=incl, irregularite=0.0006, rng=a.rng)
    a.objet(pc).parent = groupe


def rafistolages(a):
    # à droite, le panneau de céramique cassé, remplacé par une tôle à l'apprêt rouge, rivetée
    pc = a.piece("Plaque_Rivetee", "Primaire")
    f = kit.prisme(pc, PANNEAU, -0.0318, -0.0286)
    kit.chanfreiner(pc, 0.0007, f)
    for av, h in ((0.129, 0.095), (0.251, 0.093), (0.252, 0.134), (0.135, 0.135)):
        kit.rivet(pc, P(av, h, -0.0318), -GAUCHE, mat="AcierBrut")
    a.objet(pc)

    # la poutre haute fêlée, tenue par un collier de serrage
    pc = a.piece("Collier", "AcierBrut")
    section = ca.section8(0.0190, 0.124, 0.146, 0.006, 0.004)
    kit.bande(pc, section, P(0.700, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0075, 0.0009, "AcierBrut",
              inclinaison=0.04, irregularite=0.0002, rng=a.rng)
    f = kit.boite(pc, 0.6955, 0.7045, 0.1275, 0.1420, 0.0195, 0.0240, "AcierBrut")   # boîtier de la vis
    kit.chanfreiner(pc, 0.0006, f)
    kit.vis_six_pans(pc, P(0.700, 0.1420, 0.0218), HAUT, r=0.0026, h=0.0016, mat="Acier")
    fente = [(0.684, 0.1336), (0.690, 0.1302), (0.6955, 0.1318), (0.7045, 0.1292), (0.712, 0.1312), (0.718, 0.1288)]
    for (av0, h0), (av1, h1) in zip(fente, fente[1:]):   # la fêlure, de part et d'autre du collier
        kit.quad(pc, [P(av0, h0 - 0.0003, 0.0193), P(av1, h1 - 0.0003, 0.0193), P(av1, h1 + 0.0003, 0.0193),
                      P(av0, h0 + 0.0003, 0.0193)], "Interieur", normale=GAUCHE)
    a.objet(pc)

    # câble de dérivation : la carcasse alimente l'émetteur par l'extérieur, bridé sur les bagues
    pc = a.piece("Cable_Derivation", "Caoutchouc")
    chemin = [P(0.306, 0.113, 0.0226), P(0.345, 0.110, 0.0240), P(0.410, 0.1035, 0.0250), P(BAGUES[0], 0.106, X_CABLE),
              P(0.548, 0.1015, 0.0250), P(BAGUES[1], 0.106, X_CABLE), P(0.700, 0.1025, 0.0252), P(0.765, 0.108, 0.0258),
              P(0.794, 0.110, 0.0262)]
    kit.balayage(pc, kit.lisser_chemin(chemin, sous_div=3), kit.section_cercle(0.0021, 6), "Caoutchouc")
    kit.cylindre(pc, P(0.298, 0.113, 0.0226), P(0.312, 0.113, 0.0226), 0.0034, 6, mat="Acier")   # prise
    f = kit.boite(pc, 0.792, 0.812, 0.100, 0.120, 0.0236, 0.0292, "AcierBrut")              # boîte de jonction
    kit.chanfreiner(pc, 0.0008, f)
    for av, h in ((0.797, 0.115), (0.807, 0.105)):
        kit.vis_six_pans(pc, P(av, h, 0.0292), GAUCHE, r=0.0018, h=0.0010, mat="Acier")
    for av in BAGUES:   # brides vissées sur les bagues
        f = kit.boite(pc, av - 0.0045, av + 0.0045, 0.1005, 0.1115, 0.0233, 0.0292, "AcierBrut")
        kit.chanfreiner(pc, 0.0006, f)
        kit.vis_six_pans(pc, P(av, 0.106, 0.0292), GAUCHE, r=0.0018, h=0.0010, mat="Acier")
    a.objet(pc)

    # le compteur de charge, sur son boîtier à l'arrière de la carcasse, sous la lunette, face au joueur
    pc = a.piece("Compteur", "Peinture")
    f = kit.prisme(pc, [(-0.093, 0.1428), (-0.080, 0.1610), (-0.064, 0.1610), (-0.064, 0.1480)], -0.0145, 0.0145,
                   "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    ca.compteur_arriere(pc, P(-0.0865, 0.1519), 0.0230, 0.0170, elevation=35.5, chiffres="05")
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0105, P(0.191, 0.119, 0.0320), ca.VERS_CROSSE, HAUT, mat="MarquageNoir")
    a.texte(pc, "PROTOTYPE", 0.0050, P(0.212, 0.062, -0.0258), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.autocollant_laser(pc, P(0.8125, 0.107, -0.0210), -GAUCHE, Vector((0, -1, 0)), 0.0080)
    ca.bande_hachuree(pc, [P(0.345, 0.1285, 0.0201), P(0.420, 0.1285, 0.0201), P(0.420, 0.1335, 0.0201),
                           P(0.345, 0.1335, 0.0201)], GAUCHE, n=10)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_futuriste(a)
    ca.cellule(a, DOS, FACE, 0.0150, segments=5, allumes=4)
    canon(a)
    emetteur(a)
    montages(a)
    lunette(a)
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_OCULAIRE, H_LUNETTE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.380, 0.0640))
    a.accroches({
        "Viseur": P(0.075, H_PLAQUE + 0.0035),
        "Laser": P(0.558, 0.080, -0.0231),
        "Poignee": P(0.676, 0.0620),
        "Chargeur": P(0.210, 0.040),
        "Crosse": P(-0.090, 0.110),
    })


VUES = [
    ("detail_canon", P(0.60, 0.17, 0.22), P(0.58, H_AXE, 0.0), 40),
    ("detail_bouche", P(0.99, 0.15, 0.16), P(0.85, 0.11, 0.0), 40),
    ("detail_lunette", P(-0.04, 0.26, 0.17), P(0.08, 0.17, 0.0), 45),
]

ATELIER = ca.principal("FX-2", "fusil de précision laser", "Fusil de précision laser", construire, (AV_BOUCHE, H_AXE),
                       laser=True, vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
