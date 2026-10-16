"""Fusil d'assaut FA-07 de Duel Arena (Blender 5.2, low poly) : l'arme à balles du stock.

Le FA-07 est l'arme de dotation du complexe (bible § 12), restée en service longtemps après la
disparition des humains et entretenue par les robots avec ce qu'ils trouvaient (bible § 5). Sa
silhouette est celle d'une carabine de type M4 (refaite le 2026-10-15 à la demande de l'utilisateur,
d'après une photo, avec un avant plus fin) : carcasses haute et basse, garde-main à quatre rails,
guidon en « A » sur le canon, cache-flamme, hausse rabattable à anneau, crosse télescopique sur son
tube et sa tour, chargeur courbe.

Il a servi : les caches des rails perdus, remplacés par du ruban adhésif ; l'anneau de grenadière
perdu, remplacé par une boucle de fil de fer ; la goupille avant perdue, remplacée par un boulon ;
des encoches de victoires sur la crosse.

Pour les animations : le puits du chargeur est là où la main gauche du clip de rechargement va le
chercher. Le garde-main, plus fin que l'ancien, passe au-dessus de la main gauche des animations :
à l'intégration, une IK la pose sur le point MainGauche.

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
AV_BOUCHE = 0.672      # bout du cache-flamme
H_DESSUS = 0.160       # dessus de la carcasse haute et du garde-main
H_RAIL = 0.172         # sommet des dents du rail du dessus
H_VISEE = 0.2065       # ligne de mire : centre de l'anneau, pointe du guidon
AV_HAUSSE = -0.048
AV_GUIDON = 0.520
H_TUBE_CROSSE = 0.135

# Chargeur courbe, droit en haut et qui part vers l'avant en bas, comme celui d'un M4. Son puits est
# plus près de la poignée que sur l'ancien FA-07 (proportions d'un M4), mais sa courbe ramène le bas
# là où la main gauche du clip de rechargement va le chercher, vers av 0,19 à 0,22.
DOS, FACE = ca.courbes_chargeur((0.130, 0.034), (0.128, -0.050), (0.160, -0.100),
                                (0.202, 0.034), (0.202, -0.046), (0.232, -0.092))
# Carcasse haute : flancs droits, épaulement chanfreiné sous le rail, comme celle d'un M4.
SECTION_HAUTE = ca.section8(0.0190, 0.108, H_DESSUS, 0.0050, 0.0020)
# Garde-main à quatre rails : un corps à huit pans, fin, centré sur le canon.
SECTION_GM = ca.section8(0.0185, H_AXE - 0.022, H_DESSUS, 0.0055, 0.0055)


def carcasse(a):
    pc = a.piece("Carcasse_Haute", "Peinture")
    f = kit.loft(pc, kit.sections_av([(-0.072, SECTION_HAUTE), (0.248, SECTION_HAUTE)]), "Peinture")
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.prisme(pc, [(0.058, 0.128), (0.074, 0.128), (0.074, 0.154), (0.064, 0.154)], -0.0240, -0.0185)
    kit.chanfreiner(pc, 0.0008, f)                                           # déflecteur, derrière la fenêtre
    f = kit.prisme(pc, [(0.024, 0.128), (0.056, 0.128), (0.056, 0.150), (0.034, 0.152)], -0.0255, -0.0185)
    kit.chanfreiner(pc, 0.0008, f)                                           # logement de l'assistance
    section = [(-0.0192, 0.1222), (-0.0208, 0.1232), (-0.0322, 0.1028), (-0.0306, 0.1018)]
    f = kit.loft(pc, kit.sections_av([(0.078, section), (0.146, section)]), "Peinture")   # couvre-culasse ouvert
    kit.chanfreiner(pc, 0.0005, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.076, 0.148, 0.124, 0.148, -0.040, -0.012)            # fenêtre d'éjection
    a.couper(ob, cp, auto=True)

    pc = a.piece("Carcasse_Basse", "Peinture")
    f = kit.prisme(pc, [(-0.086, 0.124), (-0.086, 0.100), (-0.074, 0.088), (-0.062, 0.074), (-0.036, 0.052),
                        (0.116, 0.052), (0.116, 0.110), (-0.072, 0.110), (-0.072, 0.124)], -0.0170, 0.0170)
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.cylindre(pc, P(-0.086, H_TUBE_CROSSE), P(-0.071, H_TUBE_CROSSE), 0.0185, 12)   # tour du tube de crosse
    kit.chanfreiner(pc, 0.0010, f, angle_min=60)
    f = kit.prisme(pc, [(0.116, 0.110), (0.116, 0.016), (0.124, 0.006), (0.212, 0.006), (0.220, 0.016),
                        (0.226, 0.074), (0.248, 0.094), (0.248, 0.110)], -0.0175, 0.0175)   # puits du chargeur
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.prisme(pc, [(0.114, 0.016), (0.114, 0.004), (0.122, -0.002), (0.214, -0.002), (0.222, 0.004),
                        (0.222, 0.016)], -0.0190, 0.0190)                    # lèvre du puits
    kit.chanfreiner(pc, 0.0012, f)
    ca.pontet(pc, [(0.118, 0.040), (0.114, 0.008), (0.102, -0.004), (0.046, -0.004), (0.027, 0.010)])
    ca.detente(pc, 0.063, 0.054)
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.cylindre(pc, P(0.004, 0.090, 0.0168), P(0.004, 0.090, 0.0212), 0.0058, 10)             # sélecteur
    f = kit.prisme(pc, [(0.002, 0.0935), (0.026, 0.0862), (0.027, 0.0822), (0.002, 0.0868)], 0.0205, 0.0228)
    kit.chanfreiner(pc, 0.0006, f)
    f = kit.prisme(pc, [(0.102, 0.084), (0.116, 0.084), (0.118, 0.102), (0.106, 0.102)], 0.0168, 0.0205)
    kit.chanfreiner(pc, 0.0006, f)                                                             # arrêtoir de culasse
    kit.cylindre(pc, P(0.108, 0.078, -0.0168), P(0.108, 0.078, -0.0205), 0.0042, 8)           # bouton du chargeur
    for av, h in ((0.040, 0.084), (0.066, 0.068)):                                             # axes du chien, de la détente
        for x0, x1 in ((0.0168, 0.0182), (-0.0168, -0.0182)):
            kit.cylindre(pc, P(av, h, x0), P(av, h, x1), 0.0030, 8)
    kit.cylindre(pc, P(0.008, 0.140, -0.0255), P(0.030, 0.140, -0.0245), 0.0056, 10)          # assistance
    kit.boite(pc, 0.080, 0.144, 0.128, 0.144, -0.0140, -0.0080)                                # culasse, dans la fenêtre
    f = kit.boite(pc, -0.084, -0.072, 0.150, 0.1590, -0.0185, 0.0185)                          # poignée d'armement en T
    f += kit.boite(pc, -0.082, -0.070, 0.1515, 0.1570, 0.0185, 0.0275)                         # et son loquet
    kit.chanfreiner(pc, 0.0008, f)
    for x0, x1 in ((0.0170, 0.0186), (-0.0170, -0.0186)):                                      # goupille arrière
        kit.cylindre(pc, P(-0.058, 0.100, x0), P(-0.058, 0.100, x1), 0.0040, 8)
    # la goupille avant, perdue, remplacée par un boulon et son écrou
    kit.vis_six_pans(pc, P(0.236, 0.099, 0.0172), GAUCHE, r=0.0054, h=0.0030, mat="AcierBrut")
    kit.cylindre(pc, P(0.236, 0.099, -0.0170), P(0.236, 0.099, -0.0260), 0.0025, 8, mat="AcierBrut")
    kit.vis_six_pans(pc, P(0.236, 0.099, -0.0172), -GAUCHE, r=0.0048, h=0.0032, mat="AcierBrut", phase=0.0)
    a.objet(pc)


def chargeur(a):
    """Le chargeur courbe, en alliage peint, ses deux nervures de chaque côté et sa semelle."""
    pc = a.piece("Chargeur_Standard", "Peinture")
    f = kit.prisme(pc, FACE + list(reversed(DOS)), -0.0115, 0.0115, "Peinture")
    kit.chanfreiner(pc, 0.0018, f)
    ca.semelle(pc, DOS, FACE, 0.0128, "Polymere", ep_bas=0.0065)
    for w in (0.30, 0.70):
        pts = [Vector(d).lerp(Vector(fc), w) for d, fc in zip(DOS, FACE)]
        for x0, x1 in ((0.0111, 0.0125), (-0.0125, -0.0111)):
            for p0, p1 in zip(pts[1:-1], pts[2:]):
                kit.prisme(pc, [(p0.x - 0.0020, p0.y), (p0.x + 0.0020, p0.y), (p1.x + 0.0020, p1.y),
                                (p1.x - 0.0020, p1.y)], x0, x1, "Peinture")
    a.objet(pc)


def garde_main(a):
    pc = a.piece("Garde_Main", "Peinture")
    f = kit.loft(pc, kit.sections_av([(0.248, SECTION_GM), (0.490, SECTION_GM)]), "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    f = kit.tranches(pc, P(0.238, H_AXE), P(0.254, H_AXE), [0.0215, 0.0236, 0.0236, 0.0215], n=16,
                     positions=[0.0, 0.2, 0.8, 1.0], mat="Peinture")       # écrou de canon
    kit.chanfreiner(pc, 0.0006, f, angle_min=40)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av in (0.264, 0.294, 0.324, 0.354, 0.384, 0.414):                  # évents ronds, de part en part
        kit.cylindre(cp, P(av, 0.1262, 0.040), P(av, 0.1262, -0.040), 0.0040, 8)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, -0.070, 0.488, P(-0.070, H_DESSUS), "haut", mat="Polymere")
    for face, origine in (("bas", P(0.252, H_AXE - 0.022)), ("gauche", P(0.252, H_AXE, 0.0185)),
                          ("droite", P(0.252, H_AXE, -0.0185))):
        ca.rail(pc, 0.252, 0.486, origine, face, largeur=0.012, epaisseur=0.0040, dent=0.0040, pas=0.012,
                longueur_dent=0.0050, mat="Polymere")
    a.objet(pc)


def canon(a):
    pc = a.piece("Canon", "Bouche")
    kit.cylindre(pc, P(0.248, H_AXE), P(0.616, H_AXE), 0.0080, 12, phase=0.0)
    a.objet(pc)

    # le guidon en « A » sur le bloc d'emprunt de gaz, et le tenon de baïonnette
    pc = a.piece("Guidon_Base", "Peinture")
    f = kit.boite(pc, 0.506, 0.534, 0.128, 0.150, -0.0110, 0.0110)
    kit.chanfreiner(pc, 0.0012, f)
    f = kit.prisme(pc, [(0.502, 0.149), (0.538, 0.149), (0.528, 0.197), (0.512, 0.197)], -0.0055, 0.0055)
    kit.chanfreiner(pc, 0.0010, f)
    f = kit.boite(pc, 0.510, 0.526, 0.120, 0.129, -0.0040, 0.0040)
    kit.chanfreiner(pc, 0.0006, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.prisme(cp, [(0.512, 0.156), (0.528, 0.156), (0.520, 0.184)], -0.020, 0.020)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Cache_Flamme", "Bouche")
    f = kit.tranches(pc, P(0.610, H_AXE), P(AV_BOUCHE, H_AXE), [0.0095, 0.0108, 0.0108, 0.0102], n=10,
                     positions=[0.0, 0.10, 0.90, 1.0])
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.626, 0.664, H_AXE + 0.0040, H_AXE + 0.0150, -0.0015, 0.0015)   # fentes : dessus et côtés
    kit.boite(cp, 0.626, 0.664, H_AXE - 0.0015, H_AXE + 0.0015, 0.0040, 0.0150)
    kit.boite(cp, 0.626, 0.664, H_AXE - 0.0015, H_AXE + 0.0015, -0.0150, -0.0040)
    kit.cylindre(cp, P(0.660, H_AXE), P(0.682, H_AXE), 0.0050, 8)
    a.couper(ob, cp, auto=True)


def crosse(a):
    """La crosse télescopique : un groupe à part, qu'une crosse de l'armurerie remplacera ;
    le tube reste à la carcasse (une autre crosse s'y emmanche)."""
    pc = a.piece("Tube_Crosse", "Peinture")
    kit.cylindre(pc, P(-0.086, H_TUBE_CROSSE), P(-0.236, H_TUBE_CROSSE), 0.0150, 12)
    f = kit.cylindre(pc, P(-0.086, H_TUBE_CROSSE), P(-0.089, H_TUBE_CROSSE), 0.0200, 12, mat="Acier")   # plaque
    f += kit.cylindre(pc, P(-0.089, H_TUBE_CROSSE), P(-0.099, H_TUBE_CROSSE), 0.0172, 12, mat="Acier")  # écrou
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    a.objet(pc)

    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Polymere")
    f = kit.prisme(pc, [(-0.150, 0.156), (-0.290, 0.156), (-0.300, 0.150), (-0.302, 0.044), (-0.294, 0.034),
                        (-0.276, 0.036), (-0.250, 0.108), (-0.170, 0.116), (-0.150, 0.120)], -0.0170, 0.0170)
    kit.chanfreiner(pc, 0.0025, f)
    for x in (0.0175, -0.0175):   # les tubes des flancs
        f = kit.cylindre(pc, P(-0.162, 0.138, x), P(-0.286, 0.138, x), 0.0080, 10, mat="Polymere")
        kit.chanfreiner(pc, 0.0008, f, angle_min=60)
        kit.disque(pc, P(-0.2862, 0.138, x), Vector((0, 1, 0)), 0.0064, 10, mat="Acier")
    f = kit.prisme(pc, [(-0.214, 0.106), (-0.176, 0.110), (-0.176, 0.117), (-0.214, 0.113)], -0.0050, 0.0050)
    kit.chanfreiner(pc, 0.0006, f)                                           # levier de réglage
    kit.disque(pc, P(-0.278, 0.120, 0.0172), GAUCHE, 0.0045, 10, mat="Acier")  # embase de grenadière
    a.objet(pc).parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.302, 0.158), (-0.311, 0.154), (-0.314, 0.142), (-0.314, 0.040), (-0.310, 0.030),
                        (-0.302, 0.028)], -0.0205, 0.0205)
    kit.chanfreiner(pc, 0.003, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Marquages", "Rayure")
    ca.encoches(pc, -0.200, 0.1475, 0.1545, 0.0171, 7)
    a.objet(pc, angle=None).parent = groupe


def rafistolages(a):
    # les caches des rails perdus : du ruban adhésif autour du garde-main, rails compris
    pc = a.piece("Ruban_GardeMain", "Ruban")
    contour = kit.contour_rect(0.0540, 0.0650, chanfrein=0.0100, cy=0.1400)
    for av, largeur, incl, rabat in ((0.438, 0.018, 0.05, None), (0.461, 0.017, -0.07, (3, 0.010))):
        kit.bande(pc, contour, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, largeur, 0.0011, "Ruban",
                  inclinaison=incl, irregularite=0.0007, rng=a.rng, rabat=rabat)
    a.objet(pc)
    # l'anneau de grenadière perdu, remplacé par une boucle de fil de fer sous le guidon
    pc = a.piece("Fil_Grenadiere", "Fil")
    boucle = kit.cercle(P(0.518, 0.1125), GAUCHE, 0.0062, 10)
    kit.balayage(pc, boucle, kit.section_cercle(0.0011, 4), "Fil", ferme=True)
    kit.cylindre(pc, P(0.518, 0.1190, 0.0010), P(0.522, 0.1230, 0.0040), 0.0009, 5, mat="Fil")   # torsade
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0090, P(0.170, 0.062, 0.0176), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 0412", 0.0040, P(0.170, 0.062, -0.0176), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    for texte, av, h in (("S", -0.010, 0.0990), ("1", 0.016, 0.1010), ("R", 0.027, 0.0820)):   # sélecteur
        a.texte(pc, texte, 0.0035, P(av, h, 0.0171), ca.VERS_CROSSE, HAUT, decalage=0.0003, resolution=1)
    for i, av in enumerate((0.268, 0.304, 0.340, 0.376, 0.412)):                              # repères du rail
        a.texte(pc, f"G{4 * (i + 1)}", 0.0028, P(av, 0.1500, 0.0186), ca.VERS_CROSSE, HAUT, decalage=0.0002,
                resolution=1)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_pistolet(a, ruban=False)
    chargeur(a)
    garde_main(a)
    canon(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE)
    ca.guidon(a, AV_GUIDON, 0.196, H_VISEE, demi_base=0.0075)
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.300, H_AXE - 0.030))
    a.accroches({
        "Viseur": P(0.100, H_RAIL),                 # rail du dessus, au-dessus de la carcasse
        "Laser": P(0.395, H_AXE, -0.0265),          # rail du flanc droit, derrière le ruban
        "Poignee": P(0.390, H_AXE - 0.030),         # rail du dessous
        "Chargeur": P(0.166, 0.034),                # haut du chargeur, dans le puits
        "Crosse": P(-0.086, H_TUBE_CROSSE),         # le tube de crosse
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_garde_main", P(0.39, 0.17, 0.17), P(0.39, H_AXE, 0.0), 45),
    ("detail_crosse", P(-0.22, 0.17, 0.24), P(-0.22, 0.12, 0.0), 50),
]

ATELIER = ca.principal("FA-07", "fusil d'assaut", "Fusil d'assaut", construire, (AV_BOUCHE, H_AXE), vues_detail=VUES,
                       args=ca.arguments(globals().get("ARGS")))
