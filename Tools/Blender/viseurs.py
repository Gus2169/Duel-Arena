"""Les dix viseurs sans grossissement de Duel Arena (Blender 5.2, low poly) : ceux de Rainbow Six
Siege (référence de l'utilisateur, 2026-10-16), trois points rouges, quatre holographiques et trois
réflex, du même stock que les armes (bible § 12) : boîtiers anthracite usés, embases sur rail,
vitres teintées, une réparation chacun.

Une référence donne une grammaire, pas un plan (skill blender) : chaque viseur reprend l'archétype
de l'icône de Rainbow Six (et de l'optique réelle qu'elle suit), pas son dessin.
- PR-A : un tube épais à boîtier de pile sur le flanc droit, sur un collier et une grosse molette ;
- PR-B : un petit tube haut perché, son corps descendu en pierre tombale sur deux jambes ;
- PR-C : un tube long sur deux colliers, une grosse molette crantée en bas à droite ;
- HO-A : une boîte à fenêtre rectangulaire, quatre vis au dos ;
- HO-B : une fenêtre sur une longue embase, la pile en travers devant, deux boutons en V au dos ;
- HO-C : une grande fenêtre en trapèze sur une embase à pans ;
- HO-D : une tête ronde sous une visière, sur un corps étroit et haut ;
- RX-A : une petite fenêtre ouverte sur une réhausse épaisse ;
- RX-B : une grande lentille ronde tenue par un corps sur le flanc gauche ;
- RX-C : une petite fenêtre en arche sous une visière, levier de serrage.

  blender -b --factory-startup -P viseurs.py -- [--seuls PR-A,HO-B] [--rendus dossier] [--vues a,b]
          [--sur-arme fusil_assaut.py] [--sortie f.blend] [--usure rouillee]
"""

import importlib
import math
import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_accessoires as acc  # noqa: E402

importlib.reload(acc)
ca = acc.ca
kit = acc.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

DROITE = acc.DROITE
VERS_TIREUR = acc.VERS_TIREUR


# --- pièces communes à plusieurs viseurs ---------------------------------------------------------------

def selle(pc, av0, av1, h0, r_appui, demi, distance, angle=90.0, mat="Peinture", n=6):
    """Un bloc posé sur un tube d'axe (h0), du côté `angle` (90 : dessus, 180 : à droite, 0 : à
    gauche), de demi-largeur `demi`, jusqu'à `distance` de l'axe ; son pied épouse le tube au rayon
    r_appui (sous la surface du tube, au-dessus de son alésage)."""
    a = math.radians(angle)
    d = Vector((math.cos(a), math.sin(a)))
    t = Vector((-d.y, d.x))
    demi_angle = math.asin(min(0.99, demi / r_appui))
    arc = [Vector((math.cos(a - demi_angle + 2 * demi_angle * i / n), math.sin(a - demi_angle + 2 * demi_angle * i / n)))
           * r_appui for i in range(n + 1)]
    exterieur = [arc[0], d * distance - t * demi, d * distance + t * demi, arc[-1]]
    f = acc.profil_epais(pc, av0, av1, [(p.x, h0 + p.y) for p in exterieur], [(p.x, h0 + p.y) for p in arc], mat)
    kit.chanfreiner(pc, 0.0010, f)
    return f


def vis_cruciforme(pc, centre, normale, r=0.0023, mat="Acier"):
    """Tête de vis cruciforme sur une face (une rondelle et sa croix)."""
    normale = normale.normalized()
    kit.cylindre(pc, centre - normale * 0.0003, centre + normale * 0.0006, r, 8, mat=mat)
    c = centre + normale * 0.0008
    u, v = kit.repere_axe(normale)
    for a_, b_ in ((u, v), (v, u)):
        kit.quad(pc, [c - a_ * (r * 0.75) - b_ * 0.00035, c + a_ * (r * 0.75) - b_ * 0.00035,
                      c + a_ * (r * 0.75) + b_ * 0.00035, c - a_ * (r * 0.75) + b_ * 0.00035], "Interieur", normale=normale)


def bouton(pc, centre, ex, ey, ez, dx, dy, dz=0.0012, mat="Caoutchouc"):
    f = kit.boite_axes(pc, centre + ez * dz, ex, ey, ez, dx, dy, dz, mat)
    kit.chanfreiner(pc, min(dx, dy) * 0.35, f)


def arc_profil(r, centre_h, a0, a1, n, x=0.0):
    return [(x + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), centre_h + r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]


def temoins(pc, points, normale, mat="Marquage"):
    """Témoins de serrage au marqueur : deux traits courts qui chevauchent deux pièces."""
    for p0, p1 in points:
        acc.trait(pc, p0, p1, 0.0011, mat, normale=normale)


# --- PR-A -----------------------------------------------------------------------------------------------

def point_rouge_a(a):
    H0 = 0.0365
    R_EXT, R_INT = 0.0176, 0.0150
    pc = a.piece("Tube", "Peinture")
    f = acc.tube_creux(pc, [(-0.052, 0.0168), (-0.047, R_EXT), (0.036, R_EXT), (0.040, 0.0189), (0.048, 0.0189)], H0,
                       [0.0146, R_INT, R_INT, 0.0160, 0.0160], n=20)
    kit.chanfreiner(pc, 0.0007, f, angle_min=60)
    # boîtier de pile et d'électronique, sur le flanc droit, et son commutateur rotatif
    x_in = -(math.sqrt(R_EXT ** 2 - 0.008 ** 2) - 0.0002)
    f = kit.boite(pc, -0.025, 0.017, H0 - 0.008, H0 + 0.008, -0.0300, x_in)
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.cylindre(pc, P(0.017, H0, -0.0222), P(0.021, H0, -0.0222), 0.0068, 12, mat="Peinture")   # bouchon de pile
    kit.chanfreiner(pc, 0.0006, f)
    acc.molette(pc, P(-0.006, H0, -0.0300), DROITE, 0.0074, 0.0048, dents=10)
    # tourelle de réglage, dessus
    f = kit.cylindre(pc, P(0.004, H0 + 0.0168), P(0.004, H0 + 0.0240), 0.0070, 12, mat="Peinture")
    kit.chanfreiner(pc, 0.0008, f)
    kit.quad(pc, [P(0.0005, H0 + 0.02405, 0.0006), P(0.0075, H0 + 0.02405, 0.0006), P(0.0075, H0 + 0.02405, -0.0006),
                  P(0.0005, H0 + 0.02405, -0.0006)], "Interieur", normale=HAUT)
    a.objet(pc)

    pc = a.piece("Monture", "Peinture")
    f = kit.anneau(pc, -0.013, 0.013, H0, 0.0203, R_EXT - 0.0003, 20, "Peinture")      # collier
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    f = kit.boite(pc, -0.013, 0.013, 0.0050, H0 - 0.0185, -0.0090, 0.0090)            # entretoise
    kit.chanfreiner(pc, 0.0010, f)
    acc.embase(pc, -0.022, 0.024, demi=0.0130, h_haut=0.0060)
    acc.molette(pc, P(0.001, 0.0012, -0.0128), DROITE, 0.0090, 0.0060, dents=10)        # serrage au pouce
    kit.cylindre(pc, P(0.001, 0.0012, 0.0128), P(0.001, 0.0012, 0.0142), 0.0034, 8, mat="Acier")
    a.objet(pc)

    # le capot de pile a perdu sa vis : un collier de serrage enserre le boîtier et le tube
    pc = a.piece("Collier", "Nylon")
    contour = [(R_EXT, H0), (0.0124, H0 + 0.0124), (0.0, H0 + R_EXT), (-0.0124, H0 + 0.0124), (-0.0152, H0 + 0.0080),
               (-0.0300, H0 + 0.0080), (-0.0300, H0 - 0.0080), (-0.0152, H0 - 0.0080), (-0.0124, H0 - 0.0124),
               (0.0, H0 - R_EXT), (0.0124, H0 - 0.0124)]
    kit.bande(pc, contour, P(0.0152, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0040, 0.0010, "Nylon",
              irregularite=0.0002, rng=a.rng, rabat=(5, 0.006))
    a.objet(pc)

    acc.vitre_ronde(a, 0.030, H0, R_INT, "ambre")
    acc.reticule(a, "point", "rouge", 0.030, H0, 0.0058)
    a.mire(-0.052, H0)


# --- PR-B -----------------------------------------------------------------------------------------------

def point_rouge_b(a):
    H0 = 0.0490
    R_EXT, R_INT = 0.0134, 0.0112
    pc = a.piece("Corps", "Peinture")
    f = acc.tube_creux(pc, [(-0.026, 0.0128), (-0.022, R_EXT), (0.020, R_EXT), (0.024, 0.0141), (0.031, 0.0141)], H0,
                       [0.0108, R_INT, R_INT, 0.0122, 0.0122], n=20)
    kit.chanfreiner(pc, 0.0006, f, angle_min=60)
    # le corps descend en « pierre tombale » sous le tube : des flancs droits jusqu'à l'axe
    f = acc.profil_epais(pc, -0.020, 0.018, [(R_EXT, H0), (R_EXT, 0.0285), (-R_EXT, 0.0285), (-R_EXT, H0)],
                         arc_profil(R_INT + 0.0006, H0, 0, -180, 10), "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    for c0, c1, r in ((P(-0.003, H0 + 0.0128), P(-0.003, H0 + 0.0186), 0.0057),       # tourelle du dessus
                      (P(-0.003, H0, -0.0128), P(-0.003, H0, -0.0186), 0.0057)):      # tourelle de droite
        f = kit.cylindre(pc, c0, c1, r, 12, mat="Peinture")
        kit.chanfreiner(pc, 0.0007, f)
    acc.molette(pc, P(0.006, H0 - 0.002, 0.0128), GAUCHE, 0.0072, 0.0050, dents=10)   # bouchon de pile, à gauche
    a.objet(pc)

    pc = a.piece("Rehausse", "Peinture")
    f = kit.boite(pc, -0.018, 0.014, 0.0260, 0.0300, -0.0128, 0.0128)               # plateau
    kit.chanfreiner(pc, 0.0010, f)
    for x0, x1 in ((0.0019, 0.0118), (-0.0118, -0.0019)):                             # deux jambes
        f = kit.boite(pc, -0.016, 0.012, 0.0055, 0.0262, x0, x1)
        kit.chanfreiner(pc, 0.0009, f)
    acc.embase(pc, -0.020, 0.016, demi=0.0130, h_haut=0.0065)
    acc.levier(pc, -0.018, 0.012, 0.0030, -0.0130, cote=-1)
    kit.cylindre(pc, P(-0.003, 0.0030, 0.0128), P(-0.003, 0.0030, 0.0142), 0.0032, 8, mat="Acier")
    # des témoins de serrage au marqueur, entre le plateau et le corps
    temoins(pc, [(P(-0.0185, 0.0270, 0.0042), P(-0.0215, 0.0335, 0.0042)),
                 (P(-0.0185, 0.0270, -0.0042), P(-0.0215, 0.0335, -0.0042))], VERS_TIREUR)
    a.objet(pc)

    # la tourelle du dessus réglée au marqueur : un trait du capuchon au corps
    pc = a.piece("Reperes", "Marquage")
    acc.trait(pc, P(-0.0030, H0 + 0.01865, 0.0), P(0.0018, H0 + 0.01865, 0.0), 0.0010, "Marquage", normale=HAUT)
    a.objet(pc, angle=None)

    acc.vitre_ronde(a, 0.016, H0, R_INT, "ambre")
    acc.reticule(a, "cercle_chevron", "rouge", 0.016, H0, 0.0070)
    a.mire(-0.026, H0)


# --- PR-C -----------------------------------------------------------------------------------------------

def point_rouge_c(a):
    H0 = 0.0400
    R_EXT, R_INT = 0.0155, 0.0133
    pc = a.piece("Tube", "Peinture")
    f = acc.tube_creux(pc, [(-0.060, 0.0160), (-0.051, 0.0163), (-0.047, R_EXT), (0.020, R_EXT), (0.028, 0.0171),
                            (0.052, 0.0171)], H0, [0.0138, 0.0140, R_INT, R_INT, 0.0150, 0.0150], n=20)
    kit.chanfreiner(pc, 0.0007, f, angle_min=60)
    selle(pc, -0.011, 0.011, H0, R_EXT - 0.0007, 0.0105, 0.0185, angle=90)            # logement des réglages
    f = kit.cylindre(pc, P(0.0, H0 + 0.0185), P(0.0, H0 + 0.0230), 0.0072, 12, mat="Peinture")
    kit.chanfreiner(pc, 0.0008, f)
    selle(pc, -0.010, 0.010, H0, R_EXT - 0.0007, 0.0090, 0.0225, angle=195)           # logement de la molette
    a.objet(pc)
    pc = a.piece("Molette", "Polymere")
    u = math.radians(195)
    axe = Vector((math.cos(u), 0.0, math.sin(u)))
    centre = P(0.0, H0 + 0.0225 * math.sin(u), 0.0225 * math.cos(u))
    acc.molette(pc, centre, axe, 0.0118, 0.0058, dents=12)                           # luminosité, la grosse molette
    kit.cylindre(pc, centre + axe * 0.0058, centre + axe * 0.0068, 0.0050, 10, mat="Acier")
    a.objet(pc)

    pc = a.piece("Monture", "Peinture")
    for av in (-0.040, 0.0145):                                                        # deux colliers
        f = kit.anneau(pc, av - 0.0050, av + 0.0050, H0, 0.0180, R_EXT - 0.0003, 20, "Peinture")
        kit.chanfreiner(pc, 0.0007, f, angle_min=60)
        f = kit.boite(pc, av - 0.0050, av + 0.0050, 0.0055, H0 - 0.0168, -0.0080, 0.0080)
        kit.chanfreiner(pc, 0.0008, f)
        for x in (0.0056, -0.0056):                                                    # vis des chapeaux
            kit.vis_six_pans(pc, P(av, H0 + 0.0176, x), HAUT, r=0.0019, h=0.0012, mat="Acier")
        acc.vis_de_serrage(pc, av, 0.0130, h=0.0015)
    acc.embase(pc, -0.048, 0.022, demi=0.0130, h_haut=0.0060)
    a.objet(pc)

    # le pare-soleil cabossé, tenu par du ruban toilé
    pc = a.piece("Ruban", "RubanNoir")
    cercle = [(0.0171 * math.cos(2 * math.pi * i / 16), H0 + 0.0171 * math.sin(2 * math.pi * i / 16)) for i in range(16)]
    kit.bande(pc, cercle, P(0.043, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.010, 0.0011, "RubanNoir",
              inclinaison=0.05, irregularite=0.0004, rng=a.rng, rabat=(12, 0.007))
    a.objet(pc)

    acc.vitre_ronde(a, 0.040, H0, 0.0150, "ambre")
    acc.reticule(a, "point", "rouge", 0.040, H0, 0.0050)
    a.mire(-0.060, H0)


# --- HO-A -----------------------------------------------------------------------------------------------

def holo_a(a):
    H0 = 0.0415
    pc = a.piece("Boitier", "Peinture")
    acc.embase(pc, -0.044, 0.042, demi=0.0230, h_haut=0.0240, ch=0.0030)
    f = acc.cadre(pc, -0.040, 0.040,
                  [(0.0240, 0.0230), (0.0240, 0.0598), (0.0198, 0.0640), (-0.0198, 0.0640), (-0.0240, 0.0598),
                   (-0.0240, 0.0230)],
                  [(0.0195, 0.0230), (0.0195, 0.0590), (-0.0195, 0.0590), (-0.0195, 0.0230)], "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    f = kit.cylindre(pc, P(0.042, 0.0125, -0.0120), P(0.0455, 0.0125, -0.0120), 0.0078, 12, mat="Peinture")   # pile
    kit.chanfreiner(pc, 0.0007, f)
    for i, av in enumerate((-0.027, -0.013)):                                          # boutons, à gauche
        bouton(pc, P(av, 0.0128, 0.0230), AVANT, HAUT, GAUCHE, 0.0042, 0.0034)
    acc.levier(pc, -0.030, 0.024, 0.0045, -0.0230, cote=-1)
    kit.cylindre(pc, P(-0.003, 0.0040, 0.0230), P(-0.003, 0.0040, 0.0244), 0.0034, 8, mat="Acier")
    for x, h in ((0.0165, 0.0185), (-0.0165, 0.0185), (0.0165, 0.0065)):               # vis du dos
        vis_cruciforme(pc, P(-0.0442, h, x), VERS_TIREUR)
    # la quatrième vis perdue, remplacée par un boulon et son écrou
    kit.vis_six_pans(pc, P(-0.0441, 0.0065, -0.0165), VERS_TIREUR, r=0.0030, h=0.0024, mat="AcierBrut")
    a.objet(pc)
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, "N° 2217", 0.0032, P(0.010, 0.0110, -0.02305), ca.VERS_BOUCHE, HAUT, decalage=0.0002, resolution=1)
    a.objet(pc, angle=None)

    acc.vitre_plane(a, [P(-0.006, 0.0240, 0.0195), P(-0.006, 0.0240, -0.0195), P(-0.006, 0.0590, -0.0195),
                        P(-0.006, 0.0590, 0.0195)], "bleu")
    acc.reticule(a, "cercle_point", "rouge", -0.006, H0, 0.0105)
    a.mire(-0.044, H0)


# --- HO-B -----------------------------------------------------------------------------------------------

def holo_b(a):
    H0 = 0.0390
    pc = a.piece("Boitier", "Peinture")
    acc.embase(pc, -0.052, 0.046, demi=0.0205, h_haut=0.0205, ch=0.0025)
    f = acc.cadre(pc, -0.036, 0.012,
                  [(0.0225, 0.0195), (0.0225, 0.0572), (0.0182, 0.0618), (-0.0182, 0.0618), (-0.0225, 0.0572),
                   (-0.0225, 0.0195)],
                  [(0.0182, 0.0195), (0.0182, 0.0575), (-0.0182, 0.0575), (-0.0182, 0.0195)], "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    f = kit.cylindre(pc, P(0.054, 0.0130, -0.0212), P(0.054, 0.0130, 0.0212), 0.0115, 14, mat="Peinture")   # piles
    kit.chanfreiner(pc, 0.0010, f, angle_min=60)
    acc.molette(pc, P(0.054, 0.0130, -0.0212), DROITE, 0.0100, 0.0030, dents=12, mat="Peinture")   # trappe
    # deux boutons en V au dos, sous la fenêtre
    for s in (1, -1):
        t = math.radians(28) * s
        ex = (GAUCHE * math.cos(t) + HAUT * math.sin(t)).normalized()
        ey = ex.cross(VERS_TIREUR).normalized() * -1
        bouton(pc, P(-0.0521, 0.0105, 0.0080 * s), ex, ey, VERS_TIREUR, 0.0050, 0.0026)
    acc.molette(pc, P(0.006, 0.0040, -0.0205), DROITE, 0.0072, 0.0050, dents=10)     # serrage au pouce
    kit.cylindre(pc, P(0.006, 0.0040, 0.0205), P(0.006, 0.0040, 0.0219), 0.0034, 8, mat="Acier")
    a.objet(pc)
    pc = a.piece("Marquages", "Marquage")
    for x, plus in ((0.0105, False), (-0.0105, True)):
        c = P(-0.0524, 0.0050, x)
        acc.trait(pc, c + DROITE * 0.0014, c - DROITE * 0.0014, 0.0005, "Marquage")
        if plus:
            acc.trait(pc, c + HAUT * 0.0014, c - HAUT * 0.0014, 0.0005, "Marquage")
    a.objet(pc, angle=None)

    # la trappe des piles tient avec un collier de serrage
    pc = a.piece("Collier", "Nylon")
    cercle = [(0.054 + 0.0115 * math.cos(2 * math.pi * i / 14), 0.0130 + 0.0115 * math.sin(2 * math.pi * i / 14))
              for i in range(14)]
    kit.bande(pc, cercle, P(0.0, 0.0, -0.0150), AVANT, HAUT, GAUCHE, 0.0040, 0.0010, "Nylon",
              irregularite=0.0003, rng=a.rng, rabat=(4, 0.006))
    a.objet(pc)

    acc.vitre_plane(a, [P(-0.020, 0.0205, 0.0182), P(-0.020, 0.0205, -0.0182), P(-0.020, 0.0575, -0.0182),
                        P(-0.020, 0.0575, 0.0182)], "bleu")
    acc.reticule(a, "losange_point", "rouge", -0.020, H0, 0.0095)
    a.mire(-0.052, H0)


# --- HO-C -----------------------------------------------------------------------------------------------

def holo_c(a):
    H0 = 0.0438
    pc = a.piece("Boitier", "Peinture")
    profil = [(0.0228, -0.003), (0.0262, 0.0040), (0.0262, 0.0182), (0.0190, 0.0258), (-0.0190, 0.0258),
              (-0.0262, 0.0182), (-0.0262, 0.0040), (-0.0228, -0.003), (-acc.RAIL / 2, -0.003), (-acc.RAIL / 2, 0.0),
              (acc.RAIL / 2, 0.0), (acc.RAIL / 2, -0.003)]
    f = kit.loft(pc, kit.sections_av([(-0.040, profil), (0.044, profil)]), "Peinture")
    kit.chanfreiner(pc, 0.0012, f)
    f = acc.cadre(pc, -0.034, 0.030,
                  [(0.0270, 0.0250), (0.0252, 0.0638), (0.0212, 0.0676), (-0.0212, 0.0676), (-0.0252, 0.0638),
                   (-0.0270, 0.0250)],
                  [(0.0222, 0.0256), (0.0208, 0.0618), (-0.0208, 0.0618), (-0.0222, 0.0256)], "Peinture")
    kit.chanfreiner(pc, 0.0013, f)
    for x in (0.0080, -0.0080):                                                        # boutons du dos
        bouton(pc, P(-0.0402, 0.0118, x), Vector((-1, 0, 0)), HAUT, VERS_TIREUR, 0.0042, 0.0038)
    acc.levier(pc, -0.028, 0.030, 0.0080, -0.0262, cote=-1, epaisseur=0.0030, hauteur=0.0085)
    kit.cylindre(pc, P(0.000, 0.0060, 0.0262), P(0.000, 0.0060, 0.0276), 0.0036, 8, mat="Acier")
    # trappe de pile, sur le flanc gauche, que voit le joueur
    f = kit.boite(pc, -0.012, 0.022, 0.0070, 0.0175, 0.0262, 0.0274)
    kit.chanfreiner(pc, 0.0006, f)
    vis_cruciforme(pc, P(-0.008, 0.0122, 0.0275), GAUCHE, r=0.0019)
    kit.vis_six_pans(pc, P(0.018, 0.0122, 0.0275), GAUCHE, r=0.0024, h=0.0020, mat="AcierBrut")   # écrou de fortune
    a.objet(pc)

    # un ruban toilé sur le dessus du cadre, là où il s'est fendu
    pc = a.piece("Ruban", "RubanNoir")
    contour = kit.contour_rect(0.0480, 0.0120, chanfrein=0.004, cy=0.0620)
    kit.bande(pc, contour, P(0.016, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0090, 0.0010, "RubanNoir",
              inclinaison=-0.06, irregularite=0.0004, rng=a.rng, rabat=(6, 0.006))
    a.objet(pc)

    acc.vitre_plane(a, [P(-0.012, 0.0256, 0.0222), P(-0.012, 0.0256, -0.0222), P(-0.012, 0.0618, -0.0208),
                        P(-0.012, 0.0618, 0.0208)], "bleu")
    acc.reticule(a, "tirets_point", "rouge", -0.012, H0, 0.0120)
    a.mire(-0.040, H0)


# --- HO-D -----------------------------------------------------------------------------------------------

def holo_d(a):
    H0 = 0.0480
    R_EXT, R_INT = 0.0168, 0.0145
    pc = a.piece("Boitier", "Peinture")
    acc.embase(pc, -0.026, 0.026, demi=0.0140, h_haut=0.0085)
    f = kit.boite(pc, -0.022, 0.020, 0.0080, H0 - R_INT - 0.0003, -0.0150, 0.0150)    # corps étroit et haut
    kit.chanfreiner(pc, 0.0018, f)
    # la tête ronde, et ses épaules jusqu'au corps
    f = acc.tube_creux(pc, [(-0.020, R_EXT), (0.018, R_EXT)], H0, R_INT, n=20)
    kit.chanfreiner(pc, 0.0007, f, angle_min=60)
    exterieur = [(0.0, H0 - R_INT - 0.0010), (0.0150, H0 - R_INT - 0.0010), (R_EXT, H0)]
    interieur = arc_profil(R_INT + 0.0005, H0, -90, 0, 6)
    for s in (1, -1):
        f = acc.profil_epais(pc, -0.020, 0.018, [(x * s, h) for x, h in exterieur], [(x * s, h) for x, h in interieur],
                             "Peinture")
        kit.chanfreiner(pc, 0.0008, f)
    # la visière en trapèze sur la tête
    exterieur = [(R_EXT + 0.0002, H0), (0.0122, H0 + R_EXT + 0.0045), (-0.0122, H0 + R_EXT + 0.0045),
                 (-(R_EXT + 0.0002), H0)]
    f = acc.profil_epais(pc, -0.014, 0.026, exterieur, arc_profil(R_EXT - 0.0004, H0, 0, 180, 10), "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    acc.molette(pc, P(0.000, 0.0230, 0.0150), GAUCHE, 0.0074, 0.0042, dents=10)       # bouchon de pile, à gauche
    f = kit.cylindre(pc, P(-0.004, H0, -0.0168), P(-0.004, H0, -0.0210), 0.0045, 10, mat="Peinture")  # réglage
    kit.chanfreiner(pc, 0.0006, f)
    acc.levier(pc, -0.022, 0.018, 0.0042, -0.0140, cote=-1)
    kit.cylindre(pc, P(-0.002, 0.0042, 0.0140), P(-0.002, 0.0042, 0.0154), 0.0034, 8, mat="Acier")
    a.objet(pc)

    # le corps fendu, tenu par un tour de ruban adhésif
    pc = a.piece("Ruban", "Ruban")
    acc.ruban_autour(a, pc, 0.0300, H0 - R_INT - 0.0080, (0.0080 + H0 - R_INT) / 2, 0.006, largeur=0.0085, mat="Ruban",
                     incl=0.05, rabat=(5, 0.006))
    a.objet(pc)

    acc.vitre_ronde(a, 0.006, H0, R_INT, "bleu")
    acc.reticule(a, "cercle_croix", "rouge", 0.006, H0, 0.0090)
    a.mire(-0.020, H0)


# --- RX-A -----------------------------------------------------------------------------------------------

def reflex_a(a):
    H0 = 0.0290
    pc = a.piece("Rehausse", "Peinture")
    acc.embase(pc, -0.024, 0.020, demi=0.0130, h_haut=0.0165)
    for x, n in ((0.01305, GAUCHE), (-0.01305, -GAUCHE)):                             # allègements des flancs
        for av0, av1 in ((-0.017, -0.006), (-0.002, 0.012)):
            kit.quad(pc, [P(av0, 0.0050, x), P(av1, 0.0050, x), P(av1, 0.0115, x), P(av0, 0.0115, x)], "Interieur",
                     normale=n)
    acc.vis_de_serrage(pc, -0.004, 0.0130, h=0.0025)
    temoins(pc, [(P(0.0035, 0.0150, 0.01308), P(0.0035, 0.0185, 0.01308))], GAUCHE)    # témoin de serrage
    a.objet(pc)

    pc = a.piece("Corps", "Peinture")
    f = kit.boite(pc, -0.022, 0.019, 0.0163, 0.0225, -0.0124, 0.0124)                 # semelle du réflex
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.boite(pc, -0.022, 0.004, 0.0220, 0.0262, -0.0124, 0.0124)                 # dos, la diode
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.boite(pc, -0.020, -0.012, 0.0262, 0.0284, -0.0035, 0.0035)
    kit.chanfreiner(pc, 0.0008, f)
    f = acc.cadre(pc, 0.004, 0.015,
                  [(0.0124, 0.0220), (0.0124, 0.0300), (0.0098, 0.0380), (-0.0098, 0.0380), (-0.0124, 0.0300),
                   (-0.0124, 0.0220)],
                  [(0.0098, 0.0225), (0.0098, 0.0296), (0.0078, 0.0352), (-0.0078, 0.0352), (-0.0098, 0.0296),
                   (-0.0098, 0.0225)], "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    for x, n in ((0.0124, GAUCHE), (-0.0124, -GAUCHE)):                               # boutons + et −
        bouton(pc, P(-0.010, 0.0240, x), AVANT, HAUT, n, 0.0030, 0.0016, dz=0.0008)
    a.objet(pc)

    interieur = [(0.0098, 0.0225), (0.0098, 0.0296), (0.0078, 0.0352), (-0.0078, 0.0352), (-0.0098, 0.0296),
                 (-0.0098, 0.0225)]
    acc.vitre_plane(a, [P(0.0095, h, x) for x, h in interieur], "neutre")
    acc.reticule(a, "triangle", "vert", 0.0095, H0, 0.0060)
    a.mire(-0.022, H0)


# --- RX-B -----------------------------------------------------------------------------------------------

def reflex_b(a):
    H0 = 0.0400
    R_EXT, R_INT = 0.0225, 0.0190
    pc = a.piece("Embase", "Peinture")
    acc.embase(pc, -0.034, 0.034, demi=0.0135, h_haut=0.0080)
    for av in (-0.026, -0.013, 0.000, 0.013, 0.026):                                  # créneaux de la queue d'aronde
        for x0, x1 in ((0.0135, 0.0146), (-0.0146, -0.0135)):
            f = kit.boite(pc, av - 0.0038, av + 0.0038, -0.0025, 0.0055, x0, x1)
            kit.chanfreiner(pc, 0.0004, f)
    acc.vis_de_serrage(pc, -0.004, 0.0146, h=0.0015)
    a.objet(pc)

    pc = a.piece("Corps", "Peinture")
    f = kit.boite(pc, -0.032, 0.026, 0.0075, 0.0560, 0.0168, 0.0300)                 # le corps, sur le flanc gauche
    kit.chanfreiner(pc, 0.0024, f)
    f = kit.anneau(pc, 0.014, 0.024, H0, R_EXT, R_INT, 24, "Peinture")               # la grande lentille ronde
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    f = kit.boite(pc, 0.014, 0.024, 0.0075, H0 - R_EXT + 0.0010, 0.0060, 0.0170)      # le pied de la lentille
    kit.chanfreiner(pc, 0.0008, f)
    acc.molette(pc, P(-0.032, 0.0420, 0.0234), VERS_TIREUR, 0.0058, 0.0035, dents=10)  # commutateur, au dos
    acc.molette(pc, P(-0.004, 0.0250, 0.0300), GAUCHE, 0.0080, 0.0034, dents=12)      # bouchon de pile, à gauche
    a.objet(pc)

    # le corps a pris un coup : une boucle de fil de fer le tient serré à l'embase
    pc = a.piece("Fil", "Fil")
    contour = kit.contour_rect(0.0150, 0.0505, chanfrein=0.003, cx=0.0234, cy=0.0300)
    kit.bande(pc, contour, P(-0.020, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0016, 0.0012, "Fil",
              irregularite=0.0003, rng=a.rng)
    a.objet(pc)

    acc.vitre_ronde(a, 0.019, H0, R_INT, "neutre", n=24)
    acc.reticule(a, "tirets_chevron", "vert", 0.019, H0, 0.0120)
    a.mire(0.014, H0)


# --- RX-C -----------------------------------------------------------------------------------------------

def reflex_c(a):
    H0 = 0.0295
    pc = a.piece("Rehausse", "Peinture")
    acc.embase(pc, -0.022, 0.020, demi=0.0130, h_haut=0.0150)
    acc.levier(pc, -0.020, 0.014, 0.0060, -0.0130, cote=-1)
    kit.cylindre(pc, P(-0.003, 0.0060, 0.0128), P(-0.003, 0.0060, 0.0142), 0.0034, 8, mat="Acier")
    a.objet(pc)

    pc = a.piece("Corps", "Peinture")
    f = kit.boite(pc, -0.022, 0.018, 0.0148, 0.0205, -0.0124, 0.0124)
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.boite(pc, -0.022, 0.004, 0.0200, 0.0250, -0.0124, 0.0124)
    kit.chanfreiner(pc, 0.0014, f)
    f = kit.boite(pc, -0.020, -0.011, 0.0250, 0.0270, -0.0034, 0.0034)
    kit.chanfreiner(pc, 0.0008, f)
    exterieur = [(0.0124, 0.0200)] + arc_profil(0.0124, 0.0290, 0, 180, 10) + [(-0.0124, 0.0200)]
    interieur = [(0.0094, 0.0205)] + arc_profil(0.0094, 0.0290, 0, 180, 10) + [(-0.0094, 0.0205)]
    f = acc.cadre(pc, 0.004, 0.016, exterieur, interieur, "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc)

    # le levier de serrage, enrubanné de toile pour ne plus glisser sous le pouce
    pc = a.piece("Ruban", "RubanNoir")
    contour = kit.contour_rect(0.0050, 0.0090, chanfrein=0.0012, cx=-0.0141, cy=0.0060)
    kit.bande(pc, contour, P(-0.010, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0110, 0.0009, "RubanNoir",
              inclinaison=0.08, irregularite=0.0003, rng=a.rng, rabat=(1, 0.004))
    a.objet(pc)

    acc.vitre_plane(a, [P(0.010, h, x) for x, h in interieur], "neutre")
    acc.reticule(a, "triangle", "vert", 0.010, H0, 0.0060)
    a.mire(-0.022, H0)


VISEURS = [
    ("PR-A", "Point rouge A", point_rouge_a),
    ("PR-B", "Point rouge B", point_rouge_b),
    ("PR-C", "Point rouge C", point_rouge_c),
    ("HO-A", "Holographique A", holo_a),
    ("HO-B", "Holographique B", holo_b),
    ("HO-C", "Holographique C", holo_c),
    ("HO-D", "Holographique D", holo_d),
    ("RX-A", "Réflex A", reflex_a),
    ("RX-B", "Réflex B", reflex_b),
    ("RX-C", "Réflex C", reflex_c),
]
LIGNES_PLANCHE = [["PR-A", "PR-B", "PR-C"], ["HO-A", "HO-B", "HO-C", "HO-D"], ["RX-A", "RX-B", "RX-C"]]


# --- planche et montage sur une arme ---------------------------------------------------------------

def planche(ateliers, dossier):
    """Les viseurs ensemble, une famille par ligne : de dos (comme les icônes de Rainbow Six) et de
    trois quarts."""
    par_code = {a.code: a for a in ateliers}
    scene = ca.preparer_scene("Planche — viseurs")
    col = kit.collection("Viseurs", scene.collection)
    etiquettes = Piece("Planche_Etiquettes", "Marquage")
    police = bpy.data.fonts.load(ca.POLICE, check_existing=True)
    M = acc.materiaux("PLANCHE")
    pas_x, pas_h = 0.085, 0.105
    for i, ligne in enumerate(LIGNES_PLANCHE):
        for j, code in enumerate(ligne):
            if code not in par_code:
                continue
            a = par_code[code]
            # vue de dos : +Y vers la caméra, la gauche du tireur à gauche de l'image
            position = Vector((-(j - 1.5) * pas_x, 0.0, -i * pas_h))
            acc.monter(a, scene, position, col=col)
            kit.texte(etiquettes, a.titre, police, 0.0065, position + Vector((0, 0.03, -0.018)), Vector((-1, 0, 0)),
                      Vector((0, 0, 1)), "Marquage", scene=scene)
    kit.vers_objet(etiquettes, col, M)
    scene.view_layers[0].update()
    studio = kit.collection(f"Studio — {scene.name}", scene.collection)
    centre = Vector((0.0, 0.0, -pas_h + 0.03))
    kit.lumiere("Lumiere_Cle", studio, centre + Vector((0.5, 0.9, 0.9)), centre, 36, 1.0)
    kit.lumiere("Lumiere_Remplissage", studio, centre + Vector((-0.6, 0.9, 0.2)), centre, 14, 1.2)
    kit.lumiere("Lumiere_Contre", studio, centre + Vector((0.0, -0.8, 0.8)), centre, 30, 0.8)
    cam = kit.camera("Cam_Planche_Dos", studio, centre + Vector((0, 0.8, 0)), centre, ortho=0.40)
    kit.rendre(scene, cam, os.path.join(dossier, "planche_dos.png"), 1800, 1500, echantillons=64)
    cam = kit.camera("Cam_Planche_TroisQuarts", studio, centre + Vector((0.42, 0.62, 0.36)), centre, 42)
    kit.rendre(scene, cam, os.path.join(dossier, "planche_trois_quarts.png"), 1800, 1500, echantillons=64)
    return scene


def sur_arme(ateliers, script, dossier):
    """Monte chaque viseur sur une arme (à la place de ses organes de visée) et rend deux vues : de
    trois quarts, et dans l'axe du viseur, depuis l'œil du joueur."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__arme__", "__file__": chemin, "ARGS": []}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    at = espace["ATELIER"]
    scene = at.scene
    scene.view_layers[0].update()
    point = acc.point_de(at, "Viseur")
    mire = bpy.data.objects[at.nom("Mire")].matrix_world.translation.copy()
    masques = [bpy.data.objects.get(at.nom(n)) for n in ("Hausse", "Guidon")]
    for ob in masques:
        if ob is not None:
            ob.hide_render = True
    studio = kit.collection(f"Studio — viseurs sur {at.code}", scene.collection)
    for a in ateliers:
        e = acc.monter(a, scene, point)
        h_axe = point.z + a.donnees["hauteurAxe"]
        cible = Vector((0.0, point.y, point.z + 0.02))
        cam = kit.camera(f"Cam_{a.prefixe}_TroisQuarts", studio, cible + Vector((0.20, 0.26, 0.11)), cible, 45)
        kit.rendre(scene, cam, os.path.join(dossier, f"{at.prefixe}_{a.prefixe}_trois_quarts.png"), 1400, 1000)
        oeil = Vector((0.0, mire.y + 0.10, h_axe))     # l'œil du joueur, 10 cm derrière la mire de l'arme
        cam = kit.camera(f"Cam_{a.prefixe}_Visee", studio, oeil, oeil + Vector((0, -1.0, 0)), 30)
        kit.rendre(scene, cam, os.path.join(dossier, f"{at.prefixe}_{a.prefixe}_visee.png"), 1400, 1000)
        bpy.data.objects.remove(e, do_unlink=True)
    for ob in masques:
        if ob is not None:
            ob.hide_render = False
    return at


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = []
    for code, titre, construire in VISEURS:
        if seuls and code not in seuls:
            continue
        ateliers.append(acc.construire_accessoire(code, titre, "Viseur", f"Viseur — {titre}", construire, args,
                                                  args.usure or None))
    if args.rendus:
        planche(ateliers, args.rendus)
        if args.sur_arme:
            sur_arme(ateliers, args.sur_arme, args.rendus)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
