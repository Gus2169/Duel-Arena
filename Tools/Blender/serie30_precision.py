"""Fusil de précision FP-30 de Duel Arena (Blender 5.2, low poly) : la série 30.

La série 30 est la dernière dotation d'armes à balles du complexe (voir serie30_fusil.py). Le
FP-30 est une carabine à levier de sous-garde moderne, refaite le 2026-10-15 à la demande de
l'utilisateur d'après ses images (une carabine à levier tactique), sans chargeur : boîte de culasse
claire percée de deux lumières oblongues, chien apparent et son extension, grand levier en boucle
d'acier brut autour de la détente, tube magasin sous le canon, longuesse noire, bague de laiton,
manchon anthracite ajouré et ses rails, arrêt de main courbe, crosse de chasse pleine, longue et
fine, à poignet droit, et sa cartouchière, lunette compacte à pans (« Lunette_Standard »,
remplaçable par un autre viseur). L'ancienne version, à chargeur en boîte, est dans
archives/serie30_precision_FP30_v1.py.

Il a servi : la boucle du levier gainée de ruban toilé, la plaque de couche perdue et remplacée par
un morceau de pneu taillé, des bâtons de victoires au marqueur blanc sur la crosse.

Pour les animations :
- la main droite tient le poignet de la crosse (repère MainDroite), pas une poignée pistolet.
  L'origine reste le point de prise de la classe, d'où le fusil se place dans la main du clip (elle
  tombe dans la boucle du levier) ; à l'intégration, une IK posera la main droite sur MainDroite ;
- la main gauche sous la longuesse (repère MainGauche), la crosse à l'épaule ;
- plus de chargeur : les cartouches entrent une à une par la porte de chargement, sous la boîte de
  culasse, là où la main gauche du clip de rechargement va et vient (comme la fenêtre des pompes).

  blender -b --factory-startup -P serie30_precision.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.112           # comme le FP-12
H_TUBE = 0.084          # le tube magasin, sous le canon
AV_BOUCHE = 0.832       # la bouche, au fond de son logement à six pans
H_DESSUS = 0.136        # dessus de la boîte de culasse, de la longuesse et du manchon
H_RAIL = 0.148          # sommet des dents des rails du dessus
H_LUNETTE = 0.182
AV_OCULAIRE = 0.008     # arrière de l'oculaire : la mire
AV_CULASSE = 0.024      # arrière de la boîte de culasse, où commence le poignet de la crosse

BOITE = [(AV_CULASSE, 0.056), (AV_CULASSE, 0.124), (0.034, H_DESSUS), (0.192, H_DESSUS), (0.200, 0.128),
         (0.200, 0.062), (0.192, 0.050), (0.028, 0.050)]
LONGUESSE = ca.section8(0.0175, 0.068, H_DESSUS, 0.012, 0.012, 0.85)
MANCHON = ca.section8(0.0200, 0.066, H_DESSUS, 0.0065, 0.0080)
# Le grand levier en boucle : il pivote sous la boîte de culasse, longe son dessous, descend devant
# la détente, fait le tour des doigts et remonte se fermer sous le poignet de la crosse.
LEVIER = [P(0.152, 0.060), P(0.146, 0.046), P(0.124, 0.044), P(0.100, 0.041), P(0.088, 0.026),
          P(0.084, -0.004), P(0.078, -0.036), P(0.064, -0.060), P(0.040, -0.074), P(0.012, -0.079),
          P(-0.014, -0.073), P(-0.032, -0.056), P(-0.040, -0.030), P(-0.042, -0.002), P(-0.040, 0.024),
          P(-0.034, 0.042)]
# La crosse de chasse (d'après l'image de l'utilisateur) : dessus droit du poignet au talon, dessous
# qui monte en pente douce du talon au poignet, poignet droit juste derrière la boîte de culasse.
PROFIL_CROSSE = [(0.026, 0.120), (-0.322, 0.103), (-0.328, 0.098), (-0.334, -0.012), (-0.327, -0.020),
                 (-0.290, -0.010), (-0.220, 0.006), (-0.150, 0.024), (-0.090, 0.038), (-0.040, 0.046),
                 (0.026, 0.051)]
FENTES = [0.446 + 0.026 * i for i in range(13)]   # les fentes du manchon, de part en part


def stade(av0, av1, h, r, n=4):
    """Contour (av, h) d'une lumière oblongue : deux demi-cercles reliés."""
    pts = []
    for i in range(n + 1):   # bout avant, du bas vers le haut
        t = -math.pi / 2 + math.pi * i / n
        pts.append((av1 - r + r * math.cos(t), h + r * math.sin(t)))
    for i in range(n + 1):   # bout arrière, du haut vers le bas
        t = math.pi / 2 + math.pi * i / n
        pts.append((av0 + r + r * math.cos(t), h + r * math.sin(t)))
    return pts


def plaque_pneu(haut, bas, carcasse=0.011, bombe=0.004, crampon=0.007, blocs=6, sillon=0.05):
    """Profil (av, h) d'une plaque de couche taillée dans un pneu : le bord avant contre la crosse
    (de haut en bas), puis la bande de roulement, bombée et à crampons (de bas en haut)."""
    (a_h, h_h), (a_b, h_b) = haut, bas

    def bord(t, prof):   # t : 0 en bas, 1 en haut ; prof : vers l'arrière
        return (a_b + (a_h - a_b) * t - prof, h_b + (h_h - h_b) * t)

    pts = [haut, bas, bord(0.0, carcasse)]
    b = (1.0 - 0.02 - (blocs - 1) * sillon) / blocs
    t = 0.01
    for _ in range(blocs):
        for tt, prof in ((t, 0.0), (t, crampon), (t + b, crampon), (t + b, 0.0)):
            pts.append(bord(tt, carcasse + bombe * math.sin(math.pi * tt) + prof))
        t += b + sillon
    pts.append(bord(1.0, carcasse))
    return pts


def boite(a):
    pc = a.piece("Boite_Culasse", "PeintureClaire")
    f = kit.prisme(pc, BOITE, -0.0180, 0.0180, "PeintureClaire")
    # les queues de la boîte de culasse, sur le poignet de la crosse : dessus et dessous
    f += kit.prisme(pc, [(0.026, 0.1170), (0.026, 0.1235), (-0.016, 0.1212), (-0.026, 0.1194), (-0.026, 0.1160)],
                    -0.0075, 0.0075, "PeintureClaire")
    f += kit.prisme(pc, [(0.028, 0.0535), (0.028, 0.0475), (-0.026, 0.0432), (-0.036, 0.0444), (-0.036, 0.0490)],
                    -0.0070, 0.0070, "PeintureClaire")
    kit.chanfreiner(pc, 0.0012, f)
    ca.detente(pc, 0.064, 0.052, mat="Laiton")
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")   # les deux lumières, des deux côtés, sans traverser
    for x0, x1 in ((0.011, 0.040), (-0.040, -0.011)):
        kit.prisme(cp, stade(0.064, 0.176, 0.110, 0.008), x0, x1)
        kit.prisme(cp, stade(0.096, 0.176, 0.078, 0.007), x0, x1)
    kit.boite(cp, 0.160, 0.194, 0.044, 0.060, -0.0080, 0.0080)   # porte de chargement, dessous
    a.couper(ob, cp, auto=True)

    pc = a.piece("Commandes", "AcierBrut")
    # la culasse, au fond de la lumière haute de droite : la fenêtre d'éjection
    kit.boite(pc, 0.068, 0.172, 0.1035, 0.1165, -0.0116, -0.0104, "Acier")
    # le chien, apparent à l'arrière de la boîte de culasse, son extension vers la gauche, son axe
    f = kit.prisme(pc, [(0.044, 0.122), (0.054, 0.136), (0.050, 0.150), (0.040, 0.160), (0.028, 0.160),
                        (0.024, 0.154), (0.034, 0.150), (0.032, 0.140), (0.036, 0.128)], -0.0040, 0.0040,
                   "Laiton")
    f += kit.boite(pc, 0.028, 0.038, 0.1530, 0.1590, 0.0040, 0.0150, "Laiton")
    kit.chanfreiner(pc, 0.0005, f)
    for x, n in ((0.0180, GAUCHE), (-0.0180, -GAUCHE)):   # vis : axe du chien, axe du levier
        kit.vis_six_pans(pc, P(0.042, 0.118, x), n, r=0.0030, h=0.0012, mat="AcierBrut")
        kit.vis_six_pans(pc, P(0.152, 0.058, x), n, r=0.0034, h=0.0012, mat="AcierBrut")
    kit.vis_six_pans(pc, P(0.190, 0.122, 0.0180), GAUCHE, r=0.0026, h=0.0010, mat="AcierBrut")
    kit.vis_six_pans(pc, P(-0.004, 0.1220), HAUT, r=0.0022, h=0.0008, mat="AcierBrut")   # vis de la queue
    # le levier
    f = kit.balayage(pc, kit.lisser_chemin(LEVIER, sous_div=3), kit.section_rect(0.0110, 0.0100), "AcierBrut",
                     normale=GAUCHE)
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc)

    pc = a.piece("Porte_Chargement", "Laiton")   # au fond de l'ouverture, sous la boîte de culasse
    f = kit.boite(pc, 0.162, 0.192, 0.0585, 0.0605, -0.0066, 0.0066, "Laiton")
    kit.chanfreiner(pc, 0.0004, f)
    kit.cylindre(pc, P(0.188, 0.0590, 0.0066), P(0.188, 0.0590, -0.0066), 0.0012, 6, mat="AcierBrut")   # charnière
    a.objet(pc)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, 0.058, 0.300, P(0.058, H_DESSUS), "haut", mat="Polymere")
    ca.rail(pc, 0.420, 0.826, P(0.420, H_DESSUS), "haut", pas=0.012, mat="Polymere")
    ca.rail(pc, 0.660, 0.760, P(0.660, 0.066), "bas", largeur=0.012, epaisseur=0.0030, dent=0.0030, pas=0.010,
            longueur_dent=0.0050, mat="Polymere")
    ca.rail(pc, 0.784, 0.826, P(0.784, 0.100, -0.0200), "droite", largeur=0.012, epaisseur=0.0030, dent=0.0030,
            pas=0.008, longueur_dent=0.0040, mat="Polymere")
    a.objet(pc)


def canon(a):
    pc = a.piece("Canon", "Bouche")   # à huit pans, vu par les fentes du manchon
    kit.cylindre(pc, P(0.192, H_AXE), P(0.829, H_AXE), 0.0110, 8)
    kit.cylindre(pc, P(0.826, H_AXE), P(AV_BOUCHE, H_AXE), 0.0100, 12)
    kit.disque(pc, P(AV_BOUCHE + 0.0002, H_AXE), AVANT, 0.0048, 8, mat="Interieur")
    a.objet(pc)

    pc = a.piece("Tube", "Acier")   # le tube magasin, bouché de laiton au ras du manchon
    kit.cylindre(pc, P(0.196, H_TUBE), P(0.836, H_TUBE), 0.0090, 10)
    f = kit.cylindre(pc, P(0.836, H_TUBE), P(0.842, H_TUBE), 0.0095, 10, mat="Laiton")
    kit.chanfreiner(pc, 0.0006, f, angle_min=60)
    a.objet(pc)


def longuesse(a):
    pc = a.piece("Longuesse", "Polymere")
    f = kit.loft(pc, kit.sections_av([(0.196, LONGUESSE), (0.400, LONGUESSE)]), "Polymere")
    kit.chanfreiner(pc, 0.0012, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av0 in (0.236, 0.276, 0.316, 0.356):                      # fentes d'accroche des flancs
        for x0, x1 in ((0.0120, 0.0300), (-0.0300, -0.0120)):
            kit.prisme(cp, stade(av0, av0 + 0.030, 0.100, 0.005), x0, x1)
    a.couper(ob, cp, auto=True)

    # la bague de laiton entre la longuesse et le manchon, et son anneau de bretelle
    pc = a.piece("Bague", "Laiton")
    contour = kit.contour_rect(0.0440, 0.0750, chanfrein=0.0100, cy=0.1010)
    f = kit.loft(pc, [[P(0.400, h, x) for x, h in contour], [P(0.412, h, x) for x, h in contour]], "Laiton")
    kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(0.406, 0.0640), P(0.406, 0.0570), 0.0022, 6, mat="Acier")
    boucle = kit.cercle(P(0.406, 0.0500), GAUCHE, 0.0062, 10)
    kit.balayage(pc, boucle, kit.section_cercle(0.0012, 4), "Acier", ferme=True)
    a.objet(pc)


def manchon(a):
    """Le manchon ajouré : ses fentes montrent le canon et le tube ; logement à six pans de la bouche."""
    pc = a.piece("Manchon", "Peinture")
    f = kit.loft(pc, kit.sections_av([(0.408, MANCHON), (0.836, MANCHON)]), "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    for av0 in FENTES:
        kit.boite(cp, av0, av0 + 0.016, 0.088, 0.124, -0.030, 0.030)
    kit.cylindre(cp, P(0.828, H_AXE), P(0.850, H_AXE), 0.0145, 6, phase=0.0)
    a.couper(ob, cp, auto=True)

    pc = a.piece("Arret_Main", "Polymere")   # l'arrêt de main courbe, sous le manchon
    f = kit.prisme(pc, [(0.560, 0.0665), (0.640, 0.0665), (0.636, 0.060), (0.604, 0.054), (0.584, 0.040),
                        (0.574, 0.038), (0.570, 0.044), (0.576, 0.058)], -0.0090, 0.0090, "Polymere")
    kit.chanfreiner(pc, 0.0012, f)
    a.objet(pc)


def lunette(a):
    """La lunette compacte à pans, sur deux embases : un groupe à part, remplaçable."""
    groupe = a.vide(a.nom("Lunette_Standard"), a.racine, "CUBE", 0.004)
    hl = H_LUNETTE
    pc = a.piece("Lunette", "Peinture")
    corps = ca.section8(0.0160, hl - 0.0160, hl + 0.0160, 0.0050, 0.0050)
    f = kit.loft(pc, kit.sections_av([(0.042, corps), (0.194, corps)]), "Peinture")
    kit.chanfreiner(pc, 0.0008, f)
    f = kit.cylindre(pc, P(0.192, hl), P(0.224, hl), 0.0190, 6, phase=0.0)       # objectif à six pans
    f += kit.cylindre(pc, P(AV_OCULAIRE, hl), P(0.044, hl), 0.0158, 6, phase=0.0)   # oculaire
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    kit.disque(pc, P(0.2242, hl), AVANT, 0.0160, 6, mat="Lentille", rotation=0.0)
    kit.disque(pc, P(AV_OCULAIRE - 0.0002, hl), Vector((0, 1, 0)), 0.0128, 6, mat="Lentille", rotation=0.0)
    # tourelles : parallaxe à gauche, hausse dessus ; leurs capots d'acier brut
    for p0, p1, p2, r in ((P(0.122, hl, 0.0160), P(0.122, hl, 0.0235), P(0.122, hl, 0.0255), 0.0110),
                          (P(0.122, hl + 0.0160), P(0.122, hl + 0.0235), P(0.122, hl + 0.0255), 0.0095)):
        f = kit.cylindre(pc, p0, p1, r, 10, mat="Peinture")
        f += kit.cylindre(pc, p1, p2, r - 0.0006, 10, mat="AcierBrut")
        kit.chanfreiner(pc, 0.0005, f, angle_min=40)
    for av in (0.082, 0.162):   # embases et leurs écrous
        f = kit.boite(pc, av - 0.010, av + 0.010, H_RAIL - 0.0015, hl - 0.0155, -0.0105, 0.0105, "Acier")
        kit.chanfreiner(pc, 0.0006, f)
        kit.vis_six_pans(pc, P(av, H_RAIL + 0.0045, -0.0105), -GAUCHE, r=0.0026, h=0.0012, mat="AcierBrut")
    a.objet(pc).parent = groupe
    pc = a.piece("Lunette_Bonnette", "Caoutchouc")
    kit.anneau(pc, AV_OCULAIRE - 0.001, AV_OCULAIRE + 0.008, hl, 0.0166, 0.0132, 6, "Caoutchouc", phase=0.0)
    a.objet(pc).parent = groupe


def crosse(a):
    """La crosse de chasse, à poignet droit : un groupe à part, qu'une crosse de l'armurerie remplacera."""
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Polymere")
    f = kit.prisme(pc, PROFIL_CROSSE, -0.0165, 0.0165, "Polymere")
    kit.chanfreiner(pc, 0.0040, f)
    # grenadière sous la crosse
    kit.cylindre(pc, P(-0.250, -0.001), P(-0.250, -0.010), 0.0025, 6, mat="Acier")
    boucle = kit.cercle(P(-0.250, -0.0165), GAUCHE, 0.0062, 10)
    kit.balayage(pc, boucle, kit.section_cercle(0.0012, 4), "Acier", ferme=True)
    a.objet(pc).parent = groupe

    # la plaque de couche perdue, remplacée par un morceau de pneu taillé à la largeur de la crosse
    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, plaque_pneu((-0.325, 0.104), (-0.331, -0.018)), -0.0190, 0.0190)
    kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc).parent = groupe

    # cinq cartouches de rechange dans leur passant, sur le flanc gauche, côté joueur
    pc = a.piece("Cartouches", "Laiton")
    for av in (-0.292, -0.281, -0.270, -0.259, -0.248):
        kit.cylindre(pc, P(av, 0.020, 0.0220), P(av, 0.060, 0.0220), 0.0046, 8, mat="Laiton")
        kit.cylindre(pc, P(av, 0.060, 0.0220), P(av, 0.071, 0.0220), 0.0040, 8, r1=0.0012, mat="Bobine")
    f = kit.boite(pc, -0.299, -0.241, 0.032, 0.046, 0.0160, 0.0275, "Nylon")
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Crosse_Marquages", "Marquage")   # des bâtons de victoires au marqueur blanc
    ca.encoches(pc, -0.120, 0.0880, 0.0975, 0.0167, 9, mat="Marquage", largeur=0.0014, pas=0.0035)
    ca.mire_crash(pc, P(-0.190, 0.050, 0.0167), GAUCHE, r=0.0065)
    a.objet(pc, angle=None).parent = groupe


def rafistolages(a):
    # la boucle du levier, gainée de ruban toilé
    pc = a.piece("Ruban_Levier", "RubanNoir")
    p0, p1 = Vector(LEVIER[5]), Vector(LEVIER[6])
    d = (p1 - p0).normalized()
    ey = Vector((0.0, -d.z, d.y))
    for k, incl in ((0.25, 0.05), (0.50, -0.06), (0.75, 0.04)):
        kit.bande(pc, kit.contour_rect(0.0128, 0.0116, chanfrein=0.0014), p0.lerp(p1, k), Vector((1, 0, 0)), ey, d,
                  0.0075, 0.0010, "RubanNoir", inclinaison=incl, irregularite=0.0003, rng=a.rng)
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "MarquageNoir")
    a.texte(pc, a.code, 0.0090, P(0.062, 0.078, 0.0182), ca.VERS_CROSSE, HAUT, mat="MarquageNoir")
    a.texte(pc, "N° 3030", 0.0040, P(0.066, 0.078, -0.0182), ca.VERS_BOUCHE, HAUT, mat="MarquageNoir",
            decalage=0.0003, resolution=1)
    a.objet(pc, angle=None)


def construire(a):
    boite(a)
    crosse(a)
    canon(a)
    longuesse(a)
    manchon(a)
    lunette(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_OCULAIRE, H_LUNETTE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.300, 0.068),
              main_droite=P(-0.012, 0.083))
    a.accroches({
        "Viseur": P(0.122, H_RAIL),                 # rail de la boîte de culasse
        "Laser": P(0.805, 0.100, -0.0260),          # petit rail à droite du manchon
        "Poignee": P(0.710, 0.0600),                # rail sous le manchon
        "Chargeur": P(0.842, H_TUBE),               # bout du tube : une rallonge de tube
        "Crosse": P(AV_CULASSE, 0.090),
    })


VUES = [
    ("detail_boite", P(0.11, 0.12, 0.22), P(0.11, 0.08, 0.0), 45),
    ("detail_avant", P(0.98, 0.17, 0.17), P(0.74, 0.10, 0.0), 45),
    ("detail_lunette", P(0.12, 0.26, 0.17), P(0.12, 0.18, 0.0), 45),
    ("detail_levier", P(0.02, -0.01, 0.20), P(0.02, -0.01, 0.0), 45),
    ("detail_dessous", P(0.20, -0.10, 0.12), P(0.18, 0.06, 0.0), 45),
    ("detail_crosse", P(-0.15, 0.05, 0.36), P(-0.15, 0.05, 0.0), 45),
]

ATELIER = ca.principal("FP-30", "fusil de précision série 30", "Fusil de précision série 30", construire,
                       (AV_BOUCHE, H_AXE), vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
