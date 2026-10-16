"""Le gant à décharge GD-1 de Duel Arena (Blender 5.2, low poly) : l'arme de corps à corps, le taser
(GDD § 6, bible § 12), porté sur l'avant-bras gauche, dans l'esprit des gantelets de Fallout.

Un gantelet d'atelier détourné : une manchette d'acier peinte en deux coques, fermée par deux
sangles ; sur le dessus, le bloc de décharge, deux condensateurs à ailettes de céramique (des
isolateurs de ligne à haute tension) et une jauge de charge tournée vers le coude, qui luit rouge
comme l'énergie des armes laser ; deux câbles gainés courent jusqu'au poing. Le poing : le dos de
la main en deux lames, une barre de jointures d'acier qui porte quatre électrodes dans leurs
isolateurs, noircies par les arcs, les doigts repliés dessous et le pouce par-dessus.

Il a servi : la coque fêlée au flanc, sous une tôle rivetée ; un câble regainé de ruban toilé ; un
collier de serrage pour tenir les deux câbles. Et sur le flanc du pouce, celui que voit le joueur
quand il frappe, le pictogramme « haute tension » et la désignation.

Repère : le gant est construit dans le repère de l'avant-bras du robot (le préfab GantChoc, posé
au poignet sur l'os de l'avant-bras gauche) : av vers les doigts (+Z dans Unity), h vers le dos de
la main (+Y), x vers l'auriculaire (le pouce à -x, +X dans Unity). L'origine est au poignet ; le
repère « Poing » marque le devant du poing, au bout des électrodes, là où partent les étincelles
(TaserSparkFX). La main du robot est repliée (échelle 0) quand il le porte : le poing du gant est la
main. Mesures du Y Bot, au repos : l'avant-bras fait 8 cm de large au poignet et 11,6 cm près du
coude ; les jointures sont à 12,8 cm du poignet.

  blender -b --factory-startup -P gant_choc.py -- [--rendus dossier] [--vues a,b] [--usure rouillee]

Export vers Unity : exporter_accessoires.py --scripts gant_choc.py --sortie Assets/_ProjectArena/Weapon/Gant,
puis Duel Arena > Armes > Importer le gant de Blender.
"""

import importlib
import math
import os
import sys

import bpy  # noqa: F401
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

CODE = "GD-1"
TITRE = "Gant à décharge"
POUCE = -GAUCHE                 # le flanc du pouce (-x), celui que voit le joueur quand il frappe
AURICULAIRE = GAUCHE            # l'autre flanc (+x)
VERS_COUDE = -AVANT

AV_POING = 0.192                # bout des électrodes : le devant du poing
H_ELECTRODES = 0.016
X_ELECTRODES = (-0.0435, -0.0145, 0.0145, 0.0435)

# La manchette, du coude au poignet : (av, demi-largeur, bas, haut). Elle laisse 8 à 14 mm autour de
# l'avant-bras du robot, qui s'évase vers le coude.
STATIONS = [(-0.190, 0.069, -0.061, 0.064),
            (-0.150, 0.064, -0.055, 0.060),
            (-0.090, 0.056, -0.046, 0.054),
            (-0.030, 0.055, -0.047, 0.053),
            (-0.012, 0.053, -0.045, 0.051)]
EPAISSEUR = 0.005


def station(av):
    """La section de la manchette à av (interpolée entre les stations)."""
    for (a0, d0, b0, h0), (a1, d1, b1, h1) in zip(STATIONS, STATIONS[1:]):
        if a0 <= av <= a1:
            t = (av - a0) / (a1 - a0)
            return d0 + (d1 - d0) * t, b0 + (b1 - b0) * t, h0 + (h1 - h0) * t
    a, d, b, h = STATIONS[0] if av < STATIONS[0][0] else STATIONS[-1]
    return d, b, h


def section_manchette(av, retrait=0.0):
    d, b, h = station(av)
    return ca.section8(d - retrait, b + retrait, h - retrait, ch_haut=0.022 - retrait, ch_bas=0.020 - retrait)


# --- la manchette -----------------------------------------------------------------------------------

def manchette(a):
    pc = a.piece("Manchette", "Peinture")
    av0 = STATIONS[0][0]
    sections = [(av0 + 0.010, section_manchette(av0 + 0.010, EPAISSEUR)),   # le fond du creux, où entre le bras
                (av0, section_manchette(av0, EPAISSEUR)),
                (av0, section_manchette(av0))]
    sections += [(av, section_manchette(av)) for av, _, _, _ in STATIONS[1:]]
    f = kit.loft(pc, kit.sections_av(sections), "Peinture")
    creux = list(f[:8]) + [f[-2]]                           # les parois du creux et son fond
    for face in creux:
        face.material_index = pc.mat("Interieur")
    kit.chanfreiner(pc, 0.0022, [x for x in f if x not in creux])
    # la ligne où se rejoignent les deux coques, sur chaque flanc, d'une station à l'autre
    bornes = [STATIONS[0][0] + 0.005] + [av for av, _, _, _ in STATIONS[1:-1]] + [-0.020]
    for signe in (1, -1):
        for av_a, av_b in zip(bornes, bornes[1:]):
            da, ba, ha = station(av_a)
            db, bb, hb = station(av_b)
            ya, yb = (ba + ha) / 2 - 0.004, (bb + hb) / 2 - 0.004
            kit.quad(pc, [P(av_a, ya - 0.0011, signe * (da + 0.0003)), P(av_b, yb - 0.0011, signe * (db + 0.0003)),
                          P(av_b, yb + 0.0011, signe * (db + 0.0003)), P(av_a, ya + 0.0011, signe * (da + 0.0003))],
                     "Interieur", normale=GAUCHE * signe)
    a.objet(pc)

    # les charnières, côté auriculaire ; les fermoirs, côté pouce
    pc = a.piece("Charnieres", "Acier")
    for av in (-0.170, -0.105, -0.040):
        d, b, h = station(av)
        y = (b + h) / 2 - 0.004
        kit.cylindre(pc, P(av - 0.012, y, d + 0.003), P(av + 0.012, y, d + 0.003), 0.0042, 6, mat="Acier")
    for av in (-0.140, -0.060):
        d, b, h = station(av)
        y = (b + h) / 2 - 0.004
        f = kit.boite(pc, av - 0.011, av + 0.011, y - 0.007, y + 0.007, -(d + 0.006), -(d - 0.001), "Acier")
        kit.chanfreiner(pc, 0.0012, f)
        f = kit.boite(pc, av - 0.016, av + 0.004, y + 0.0015, y + 0.0045, -(d + 0.0085), -(d + 0.006), "AcierBrut")
        kit.chanfreiner(pc, 0.0006, f)                      # le levier du fermoir
    a.objet(pc)

    # deux sangles de nylon, leur boucle côté pouce
    pc = a.piece("Sangles", "Nylon")
    for av in (-0.165, -0.082):
        section = section_manchette(av)
        kit.bande(pc, section, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.020, 0.0022, "Nylon",
                  irregularite=0.0003, rng=a.rng)
        d, b, h = station(av)
        y = (b + h) / 2 + 0.010
        f = kit.boite(pc, av - 0.013, av + 0.013, y - 0.010, y + 0.010, -(d + 0.0058), -(d + 0.0022), "Acier")
        kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc)


# --- le bloc de décharge ----------------------------------------------------------------------------

BLOC = [(-0.176, 0.034, 0.054, 0.080), (-0.160, 0.035, 0.053, 0.082), (-0.060, 0.032, 0.048, 0.077),
        (-0.044, 0.028, 0.046, 0.071)]
H_CONDENSATEURS = 0.0865
X_CONDENSATEURS = (-0.0175, 0.0175)


def bloc(a):
    pc = a.piece("Bloc", "Peinture")
    sections = [(av, ca.section8(d, b, h, ch_haut=0.008, ch_bas=0.003)) for av, d, b, h in BLOC]
    f = kit.loft(pc, kit.sections_av(sections), "Peinture")
    kit.chanfreiner(pc, 0.0015, f, angle_min=35)
    # la jauge de charge, sur la face arrière, tournée vers le coude : trois cases allumées sur quatre
    av = BLOC[0][0] - 0.0004
    h0, h1 = 0.063, 0.078
    kit.quad(pc, [P(av, h0, -0.024), P(av, h0, 0.024), P(av, h1, 0.024), P(av, h1, -0.024)], "EcranMort",
             normale=VERS_COUDE)
    for i in range(4):
        x0 = -0.0215 + i * 0.0110
        kit.quad(pc, [P(av - 0.0003, h0 + 0.0022, x0), P(av - 0.0003, h0 + 0.0022, x0 + 0.0088),
                      P(av - 0.0003, h1 - 0.0022, x0 + 0.0088), P(av - 0.0003, h1 - 0.0022, x0)],
                 "Lueur" if i < 3 else "Interieur", normale=VERS_COUDE)
    a.objet(pc)

    # les deux condensateurs : des isolateurs à ailettes, tenus par des colliers d'acier
    pc = a.piece("Condensateurs", "Ceramique")
    n_ailettes = 5
    rayons = [0.0090] + [0.0128 if i % 2 else 0.0098 for i in range(1, 2 * n_ailettes)] + [0.0090]
    positions = [i / (2 * n_ailettes) for i in range(2 * n_ailettes + 1)]
    for x in X_CONDENSATEURS:
        kit.tranches(pc, P(-0.162, H_CONDENSATEURS, x), P(-0.074, H_CONDENSATEURS, x), rayons, n=8,
                     mat="Ceramique", positions=positions)
        for av0, av1 in ((-0.169, -0.160), (-0.076, -0.067)):        # les bornes d'acier aux deux bouts
            kit.cylindre(pc, P(av0, H_CONDENSATEURS, x), P(av1, H_CONDENSATEURS, x), 0.0074, 8, mat="Acier")
    for av in (-0.140, -0.096):                                      # les colliers qui les tiennent au bloc
        contour = [(0.0310, 0.0), (0.0310, 0.0102), (0.0225, 0.0135), (0.0125, 0.0135), (0.0040, 0.0102),
                   (-0.0040, 0.0102), (-0.0125, 0.0135), (-0.0225, 0.0135), (-0.0310, 0.0102), (-0.0310, 0.0)]
        h_bloc = station_bloc(av)
        kit.bande(pc, contour, P(av, 0.0) + HAUT * (H_CONDENSATEURS - 0.0005) + Vector((0, 0, 0)),
                  Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.008, 0.0016, "Acier")
        kit.boite(pc, av - 0.004, av + 0.004, h_bloc - 0.002, H_CONDENSATEURS, -0.0325, -0.0295, "Acier")
        kit.boite(pc, av - 0.004, av + 0.004, h_bloc - 0.002, H_CONDENSATEURS, 0.0295, 0.0325, "Acier")
    a.objet(pc)


def station_bloc(av):
    for (a0, _, _, h0), (a1, _, _, h1) in zip(BLOC, BLOC[1:]):
        if a0 <= av <= a1:
            return h0 + (h1 - h0) * (av - a0) / (a1 - a0)
    return BLOC[-1][3]


# --- le poignet et la main --------------------------------------------------------------------------

def poignet(a):
    """Un soufflet de caoutchouc entre la manchette et la main."""
    pc = a.piece("Soufflet", "Caoutchouc")
    anneaux = []
    for i, av in enumerate((-0.018, -0.011, -0.004, 0.003, 0.010, 0.016)):
        k = 1.0 if i % 2 == 0 else 0.90
        anneaux.append(kit.cercle(P(av, 0.004), AVANT, 0.044 * k, 12, rx=0.052 * k))
    f = kit.loft(pc, anneaux, "Caoutchouc")
    a.objet(pc)


MAIN = [(0.004, 0.044, -0.038, 0.020), (0.066, 0.050, -0.044, 0.024), (0.114, 0.055, -0.046, 0.024)]


def main(a):
    pc = a.piece("Main", "Caoutchouc")
    sections = [(av, ca.section8(d, b, h, ch_haut=0.010, ch_bas=0.012)) for av, d, b, h in MAIN]
    f = kit.loft(pc, kit.sections_av(sections), "Caoutchouc")
    kit.chanfreiner(pc, 0.0025, f)
    a.objet(pc)

    # le dos de la main, en deux lames qui se chevauchent, rivetées aux coins
    pc = a.piece("Dos_Main", "Peinture")
    for av0, av1, h_bord, h_dos, demi in ((0.006, 0.066, 0.016, 0.035, 0.0525), (0.058, 0.116, 0.019, 0.039, 0.0580)):
        arc = []
        for i in range(7):
            t = -1.0 + 2.0 * i / 6
            arc.append((t * demi, h_bord + (h_dos - h_bord) * (1.0 - t * t)))
        dessous = [(x, h - 0.0045) for x, h in arc]
        acc.profil_epais(pc, av0, av1, arc, dessous, "Peinture")   # sans chanfrein : ses bords fins le déréglaient
        for av, x in ((av0 + 0.008, -demi + 0.009), (av0 + 0.008, demi - 0.009)):
            h = h_bord + (h_dos - h_bord) * (1.0 - (x / demi) ** 2)
            kit.rivet(pc, P(av, h + 0.0002, x), HAUT, r=0.0024, h=0.0013, mat="Acier")
    a.objet(pc)


def poing(a):
    # la barre des jointures, et sa plaque avant noircie par les arcs
    pc = a.piece("Jointures", "Acier")
    profil = [(0.106, -0.002), (0.106, 0.032), (0.120, 0.044), (0.156, 0.042), (0.168, 0.032), (0.168, -0.006),
              (0.158, -0.012), (0.116, -0.012)]
    f = kit.prisme(pc, profil, -0.060, 0.060, "Acier")
    kit.chanfreiner(pc, 0.0030, f)
    f = kit.boite(pc, 0.1678, 0.1720, -0.003, 0.035, -0.056, 0.056, "Bouche")   # la plaque des électrodes
    kit.chanfreiner(pc, 0.0010, f)
    for x in (-0.052, 0.052):
        kit.vis_six_pans(pc, P(0.1721, 0.029, x), AVANT, r=0.0028, h=0.0015, mat="Acier")
    a.objet(pc)

    # les quatre électrodes : un isolateur de céramique à ailettes, une pointe d'acier
    pc = a.piece("Electrodes", "Ceramique")
    for x in X_ELECTRODES:
        kit.tranches(pc, P(0.171, H_ELECTRODES, x), P(0.182, H_ELECTRODES, x),
                     [0.0080, 0.0080, 0.0058, 0.0084, 0.0048], n=8, mat="Ceramique",
                     positions=[0.0, 0.25, 0.45, 0.70, 1.0])
        kit.cylindre(pc, P(0.181, H_ELECTRODES, x), P(AV_POING, H_ELECTRODES, x), 0.0032, 6, r1=0.0016, mat="Bouche")
    a.objet(pc)

    # les doigts repliés sous la barre, et le pouce par-dessus
    pc = a.piece("Doigts", "Polymere")
    doigt = [(0.112, -0.008), (0.158, -0.010), (0.170, -0.020), (0.168, -0.042), (0.152, -0.059), (0.128, -0.066),
             (0.106, -0.061), (0.102, -0.036)]
    for x in X_ELECTRODES:
        f = kit.prisme(pc, doigt, x - 0.0132, x + 0.0132, "Polymere")
        kit.chanfreiner(pc, 0.0034, f)
        f = kit.boite(pc, 0.1600, 0.1712, -0.040, -0.022, x - 0.0105, x + 0.0105, "Peinture")   # coque de phalange
        kit.chanfreiner(pc, 0.0014, f)
    a.objet(pc)

    # le pouce, deux phalanges blindées : il longe la paume côté pouce, puis se replie devant l'index
    pc = a.piece("Pouce", "Peinture")
    articulations = [P(0.048, -0.018, -0.062), P(0.118, -0.032, -0.066), P(0.172, -0.046, -0.022)]
    for (p0, p1), (largeur, hauteur) in zip(zip(articulations, articulations[1:]), ((0.0160, 0.0160), (0.0140, 0.0130))):
        axe = (p1 - p0).normalized()
        cote = axe.cross(HAUT).normalized()
        dessus = cote.cross(axe).normalized()
        f = kit.boite_axes(pc, (p0 + p1) / 2, cote, dessus, axe, largeur, hauteur, (p1 - p0).length / 2 + 0.006,
                           "Peinture")
        kit.chanfreiner(pc, 0.0030, f)
    kit.cylindre(pc, articulations[1] + Vector((0, 0, -0.016)), articulations[1] + Vector((0, 0, 0.016)), 0.0045, 6,
                 mat="Acier")                                                                 # l'axe de l'articulation
    a.objet(pc)


# --- les câbles, les rafistolages, les marquages ----------------------------------------------------

def cables(a):
    pc = a.piece("Cables", "Caoutchouc")
    for x in (-0.012, 0.012):
        points = [P(-0.050, 0.060, x), P(-0.020, 0.056, x * 1.1), P(0.010, 0.052, x * 1.3), P(0.050, 0.041, x * 1.5),
                  P(0.090, 0.044, x * 1.6), P(0.112, 0.040, x * 1.7)]
        kit.balayage(pc, kit.lisser_chemin(points, sous_div=2), kit.section_cercle(0.0046, 6), "Caoutchouc")
    a.objet(pc)
    # un collier de serrage tient les deux câbles sur le soufflet
    pc = a.piece("Collier", "Nylon")
    contour = [(-0.022, -0.0045), (0.022, -0.0045), (0.025, 0.0), (0.022, 0.0045), (-0.022, 0.0045), (-0.025, 0.0)]
    kit.bande(pc, contour, P(0.012, 0.052), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0045, 0.0012, "Nylon")
    f = kit.boite(pc, 0.0095, 0.0145, 0.0555, 0.0605, -0.031, -0.024, "Nylon")                 # la tête du collier
    kit.chanfreiner(pc, 0.0005, f)
    a.objet(pc)


def rafistolages(a):
    # la coque fêlée, côté pouce, sous une tôle rivetée
    pc = a.piece("Tole", "AcierBrut")
    av0, av1 = -0.140, -0.100
    d0, d1 = station(av0)[0], station(av1)[0]
    bas0, haut0, bas1, haut1 = 0.010, 0.044, 0.008, 0.040

    def coin(av, h, d, dehors):
        return P(av, h, -(d + (0.0030 if dehors else -0.0005)))

    sections = [[coin(av0, bas0, d0, False), coin(av0, bas0, d0, True), coin(av0, haut0, d0, True), coin(av0, haut0, d0, False)],
                [coin(av1, bas1, d1, False), coin(av1, bas1, d1, True), coin(av1, haut1, d1, True), coin(av1, haut1, d1, False)]]
    f = kit.loft(pc, sections, "AcierBrut")
    kit.chanfreiner(pc, 0.0008, f)
    for av, h in ((av0 + 0.005, 0.015), (av0 + 0.005, 0.038), (av1 - 0.005, 0.013), (av1 - 0.005, 0.035)):
        kit.rivet(pc, P(av, h, -(station(av)[0] + 0.0030)), POUCE, r=0.0020, h=0.0011, mat="Acier")
    a.objet(pc)
    # un câble regainé de ruban toilé, au-dessus du poignet
    pc = a.piece("Ruban", "RubanNoir")
    kit.bande(pc, kit.section_cercle(0.0049, 8), P(-0.012, 0.0575, -0.0135), Vector((1, 0, 0)), Vector((0, 0, 1)),
              Vector((0.0, -0.985, -0.17)).normalized(), 0.022, 0.0011, "RubanNoir", irregularite=0.0004, rng=a.rng,
              rabat=(2, 0.006))
    a.objet(pc)


def autocollant_tension(pc, centre, normale, droite, taille):
    """Le pictogramme « danger électrique » : un triangle noir bordé de blanc cassé, un éclair noir."""
    normale = normale.normalized()
    haut = normale.cross(droite).normalized()
    droite = haut.cross(normale).normalized()

    def tri(r, ecart):
        return [centre + (haut * math.cos(t) + droite * math.sin(t)) * r + normale * ecart
                for t in (0.0, 2 * math.pi / 3, 4 * math.pi / 3)]

    kit.quad(pc, tri(taille, 0.0003), "MarquageNoir", normale=normale)
    kit.quad(pc, tri(taille * 0.72, 0.0005), "Marquage", normale=normale)

    def p(u, v):
        return centre + droite * (u * taille) + haut * (v * taille) + normale * 0.0007

    # l'éclair, en deux quadrilatères convexes
    kit.quad(pc, [p(0.06, 0.34), p(-0.13, -0.03), p(-0.01, -0.03), p(0.15, 0.34)], "MarquageNoir", normale=normale)
    kit.quad(pc, [p(-0.02, 0.05), p(0.14, 0.05), p(-0.06, -0.30), p(-0.05, -0.04)], "MarquageNoir", normale=normale)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    # côté pouce, vu du joueur : le pictogramme et « HAUTE TENSION »
    d, b, h = station(-0.060)
    autocollant_tension(pc, P(-0.064, 0.024, -(d + 0.0002)), POUCE, AVANT, 0.0150)
    av_a, av_b = -0.186, -0.114
    long_flanc = (P(av_b, 0.0, -station(av_b)[0]) - P(av_a, 0.0, -station(av_a)[0])).normalized()
    av_t = -0.123
    a.texte(pc, "HAUTE TENSION", 0.0058, P(av_t, -0.024, -(station(av_t)[0] + 0.0003)), long_flanc, HAUT,
            mat="Marquage", resolution=1)
    # côté auriculaire : la désignation, et les bâtons des victoires au marqueur
    av_c = -0.124                                      # entre les deux sangles, le long du flanc qui s'évase
    vers_coude = (P(-0.150, 0.0, station(-0.150)[0]) - P(-0.096, 0.0, station(-0.096)[0])).normalized()
    a.texte(pc, CODE, 0.0090, P(av_c, 0.020, station(av_c)[0] + 0.0003), vers_coude, HAUT, mat="Marquage",
            resolution=1)
    for i in range(5):
        av = -0.160 + 0.0060 * i
        kit.quad(pc, [P(av, -0.012, station(av)[0] + 0.0003), P(av + 0.0016, -0.012, station(av)[0] + 0.0003),
                      P(av + 0.0016, 0.006, station(av)[0] + 0.0003), P(av, 0.006, station(av)[0] + 0.0003)],
                 "Marquage", normale=AURICULAIRE)
    av_a, av_b = -0.163, -0.133
    kit.quad(pc, [P(av_a, -0.011, station(av_a)[0] + 0.0005), P(av_b, 0.004, station(av_b)[0] + 0.0005),
                  P(av_b, 0.006, station(av_b)[0] + 0.0005), P(av_a, -0.009, station(av_a)[0] + 0.0005)],
             "Marquage", normale=AURICULAIRE)
    # la bande d'essai, sur le devant du bloc
    av = BLOC[-1][0] + 0.0004
    ca.bande_hachuree(pc, [P(av, 0.0525, 0.024), P(av, 0.0525, -0.024), P(av, 0.0585, -0.024), P(av, 0.0585, 0.024)],
                      AVANT, n=6)
    a.objet(pc, angle=None)


def construire(a):
    manchette(a)
    bloc(a)
    poignet(a)
    main(a)
    poing(a)
    cables(a)
    rafistolages(a)
    marquages(a)
    a.vide(a.nom("Poing"), a.racine, position=P(AV_POING, H_ELECTRODES - 0.002))
    a.donnees["poing"] = [AV_POING, H_ELECTRODES - 0.002, 0.0]


construire.vues = [
    ("joueur", P(-0.42, 0.20, -0.16), P(0.06, 0.02, 0.0), 40),      # par-dessus l'épaule, comme le joueur qui frappe
    ("poing", P(0.40, 0.10, -0.22), P(0.13, 0.00, 0.0), 40),       # le devant du poing, côté pouce
    ("dessous", P(0.10, -0.45, -0.22), P(0.06, -0.02, 0.0), 40),   # la paume et le pouce replié
    ("detail_designation", P(-0.124, 0.03, 0.30), P(-0.124, 0.012, 0.05), 30),
]


def principal():
    args = acc.arguments(globals().get("ARGS"))
    variante = args.usure or None
    nom_scene = "Gant à décharge"
    if variante:
        nom_scene = f"{nom_scene} — {ca.VARIANTES_USURE[variante]}"
    scene = ca.preparer_scene(nom_scene)
    if bpy.app.background:
        ca.retirer_scene_usine(scene)
    bouche = (AV_POING, H_ELECTRODES)            # la suie des arcs, au bout des électrodes
    M = ca.materiaux(CODE.replace("-", ""), bouche, variante)
    a = acc.AtelierAccessoire(scene, CODE, TITRE, "Gant", M)
    a.bouche = bouche
    a.variante = variante
    construire(a)
    total = sum(kit.triangles(o) for o in a.objets)
    print(f"  {a.code:<6} {len(a.objets):>3} objets {total:>6} triangles   ({TITRE})")
    for o in a.objets:
        print(f"      {o.name:<26} {kit.triangles(o):>5}")
    a.cams = ca.studio(scene, a, construire.vues)
    if args.rendus:
        dossier = os.path.join(args.rendus, a.prefixe)
        os.makedirs(dossier, exist_ok=True)
        vues = [v for v in args.vues.split(",") if v] or list(a.cams)
        for nom in vues:
            if nom in a.cams:
                cam, l, h = a.cams[nom]
                kit.rendre(scene, cam, os.path.join(dossier, nom + ".png"), l, h)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return [a]


ATELIERS = principal()
