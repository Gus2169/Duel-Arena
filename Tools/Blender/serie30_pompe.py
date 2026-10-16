"""Fusil à pompe FAP-30 de Duel Arena (Blender 5.2, low poly) : la série 30.

La série 30 est la dernière dotation d'armes à balles du complexe (voir serie30_fusil.py). Le
FAP-30 (référence de l'utilisateur, 2026-10-15 : un fusil à pompe à gaine et dissipateurs) : une
longue gaine à section carrée en trois tronçons, dont les fenêtres montrent des ailettes de
dissipation en laiton ; une chambre apparente cerclée de laiton sur la carcasse ; une pompe
anguleuse et son arrêt de main ajouré ; une crosse de chasse pleine, longue et fine, à poignet
droit (la même que celle du FP-30, demandée par l'utilisateur le 2026-10-15).

Il a servi : une ailette perdue, une autre remplacée par une lame d'acier brut, le poignet de la
crosse fêlé et serré de ruban toilé, des bâtons de victoires au marqueur sur la tête.

Pour les animations, il garde les points de prise du FAP-03, sauf la poignée :
- la main droite tient le poignet de la crosse (repère MainDroite), où une IK la posera à
  l'intégration ; l'origine reste le point de prise de la classe, d'où le fusil se place dans la
  main du clip ;
- fenêtre de chargement sous la carcasse (les cartouches entrent une à une, là où la main gauche du
  clip va et vient), pompe sous la main gauche (MainGauche), verrou en haut à gauche, crosse à
  l'épaule.

  blender -b --factory-startup -P serie30_pompe.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.115           # comme le FAP-03
H_TUBE = 0.081
AV_BOUCHE = 0.698
H_PLAQUE = 0.148        # dessus de la carcasse
H_RAIL = 0.1535         # sommet du rail bas de la carcasse
H_VISEE = 0.170
AV_HAUSSE = 0.020
AV_GUIDON = 0.640
H_BANDE = 0.1530        # dessus de la bande ventilée

AV_CARCASSE = 0.008     # arrière de la carcasse, où commence le poignet de la crosse
CARCASSE = [(AV_CARCASSE, ca.section8(0.0220, 0.066, 0.140, 0.008, 0.004)),
            (0.018, ca.section8(0.0250, 0.062, 0.148, 0.010, 0.004)),
            (0.180, ca.section8(0.0260, 0.060, 0.148, 0.010, 0.005)),
            (0.212, ca.section8(0.0250, 0.064, 0.146, 0.010, 0.005))]
S_GAINE = ca.section8(0.0230, 0.0955, 0.1465, 0.0065, 0.0060)
S_GORGE = kit.mise_a_echelle(S_GAINE, 0.93, (0.0, 0.121))
S_BAGUE = kit.mise_a_echelle(S_GAINE, 0.965, (0.0, 0.121))
TRONCONS = [(0.217, 0.333), (0.367, 0.483), (0.517, 0.636)]   # les fenêtres des trois tronçons
SECTION_POMPE = [(0.0150, 0.0945), (0.0260, 0.0890), (0.0275, 0.0730), (0.0210, 0.0590), (0.0090, 0.0560),
                 (-0.0090, 0.0560), (-0.0210, 0.0590), (-0.0275, 0.0730), (-0.0260, 0.0890), (-0.0150, 0.0945)]
# La crosse de chasse, la même que celle du FP-30 : dessus droit du poignet au talon, dessous qui
# monte en pente douce du talon au poignet, poignet droit juste derrière la carcasse ; plus haute
# que celle du FP-30 de la différence des carcasses et des lignes de mire.
PROFIL_CROSSE = [(0.010, 0.134), (-0.338, 0.117), (-0.344, 0.112), (-0.350, 0.002), (-0.343, -0.006),
                 (-0.306, 0.004), (-0.236, 0.020), (-0.166, 0.038), (-0.106, 0.050), (-0.056, 0.058),
                 (0.010, 0.062)]


def fissure(pc, points, x, largeur, mat="Interieur"):
    """Fissure peinte à plat sur un flanc gauche : une bande fine qui suit des points (av, h)."""
    for (a0, h0), (a1, h1) in zip(points, points[1:]):
        d = Vector((a1 - a0, h1 - h0)).normalized()
        n = Vector((-d.y, d.x)) * (largeur / 2)
        kit.quad(pc, [P(a0 - n.x, h0 - n.y, x), P(a1 - n.x, h1 - n.y, x), P(a1 + n.x, h1 + n.y, x),
                      P(a0 + n.x, h0 + n.y, x)], mat, normale=GAUCHE)


def carcasse(a):
    pc = a.piece("Carcasse", "Peinture")
    f = kit.loft(pc, kit.sections_av(CARCASSE), "Peinture", mats_cotes=ca.mats8(chanfrein_haut="PeintureClaire"))
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.prisme(pc, [(0.014, 0.066), (0.020, 0.054), (0.030, 0.046), (0.090, 0.046), (0.094, 0.066)],
                   -0.0220, 0.0220)
    kit.chanfreiner(pc, 0.0018, f)
    # les queues de la carcasse, sur le poignet de la crosse : dessus et dessous
    f = kit.prisme(pc, [(0.010, 0.1310), (0.010, 0.1375), (-0.030, 0.1352), (-0.040, 0.1336), (-0.040, 0.1300)],
                   -0.0075, 0.0075)
    f += kit.prisme(pc, [(0.020, 0.0650), (0.020, 0.0600), (-0.032, 0.0570), (-0.042, 0.0580), (-0.042, 0.0615)],
                    -0.0070, 0.0070)
    kit.chanfreiner(pc, 0.0010, f)
    # le pontet fait le tour de la détente et remonte se fermer derrière elle
    ca.pontet(pc, [(0.088, 0.050), (0.084, 0.020), (0.070, 0.006), (0.036, 0.004), (0.022, 0.016), (0.019, 0.034),
                   (0.020, 0.050)], mat="Polymere")
    ca.detente(pc, 0.058, 0.050)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.070, 0.150, 0.102, 0.170, -0.040, 0.040)      # berceau de la chambre, ouvert en haut
    kit.boite(cp, 0.098, 0.192, 0.052, 0.074, -0.0135, 0.0135)    # fenêtre de chargement, dessous
    a.couper(ob, cp, auto=True)

    pc = a.piece("Chambre", "AcierBrut")   # la chambre apparente, cerclée de laiton
    f = kit.tranches(pc, P(0.066, 0.118), P(0.154, 0.118), [0.0150, 0.0165, 0.0165, 0.0150], n=10,
                     positions=[0.0, 0.06, 0.94, 1.0], mat="AcierBrut")
    kit.chanfreiner(pc, 0.0008, f, angle_min=40)
    for av in (0.082, 0.107, 0.132):
        f = kit.cylindre(pc, P(av, 0.118), P(av + 0.006, 0.118), 0.0174, 10, mat="Laiton")
        kit.chanfreiner(pc, 0.0005, f)
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.quad(pc, [P(0.099, 0.0738, -0.0128), P(0.191, 0.0738, -0.0128), P(0.191, 0.0738, 0.0128),
                  P(0.099, 0.0738, 0.0128)], "Acier", normale=-HAUT)        # l'élévateur, au fond de la fenêtre
    kit.cylindre(pc, P(0.078, 0.057, 0.0215), P(0.078, 0.057, 0.0250), 0.0042, 8)     # sûreté
    kit.cylindre(pc, P(0.078, 0.057, -0.0215), P(0.078, 0.057, -0.0250), 0.0042, 8)
    f = kit.prisme(pc, [(0.020, 0.124), (0.044, 0.121), (0.044, 0.129), (0.020, 0.131)], 0.0258, 0.0300)
    kit.chanfreiner(pc, 0.0006, f)                               # verrou, là où la main gauche arme
    kit.vis_six_pans(pc, P(-0.016, 0.1361), HAUT, r=0.0022, h=0.0008, mat="AcierBrut")   # vis de la queue
    a.objet(pc)

    pc = a.piece("Rails", "Polymere")
    ca.rail(pc, 0.012, 0.068, P(0.012, 0.1475), "haut", epaisseur=0.003, dent=0.003, mat="Polymere")
    a.objet(pc)


def gaine(a):
    """La gaine du canon en trois tronçons, ses fenêtres et leurs ailettes, la bande ventilée."""
    pc = a.piece("Gaine", "PeintureClaire")
    sections = [(0.205, S_GAINE)]
    for g in (0.345, 0.495):   # deux gorges entre les tronçons
        sections += [(g - 0.001, S_GAINE), (g + 0.001, S_GORGE), (g + 0.009, S_GORGE), (g + 0.011, S_GAINE)]
    sections.append((0.652, S_GAINE))
    mats = ca.mats8(dessus="Peinture", chanfrein_haut="Peinture", flancs="PeintureClaire", chanfrein_bas="Peinture",
                    dessous="Peinture")
    f = kit.loft(pc, kit.sections_av(sections), "PeintureClaire", mats_cotes=mats)
    kit.chanfreiner(pc, 0.0012, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    fenetres = []
    for av0, av1 in TRONCONS:
        for h0, h1 in ((0.1035, 0.1180), (0.1240, 0.1385)):
            fenetres.append((av0, av1, h0, h1))
            kit.boite(cp, av0, av1, h0, h1, 0.0150, 0.040)
            kit.boite(cp, av0, av1, h0, h1, -0.040, -0.0150)
    a.couper(ob, cp)

    pc = a.piece("Bagues_Gaine", "Peinture")   # les gorges, en anthracite
    for g in (0.345, 0.495):
        f = kit.loft(pc, kit.sections_av([(g + 0.0015, S_BAGUE), (g + 0.0085, S_BAGUE)]), "Peinture")
        kit.chanfreiner(pc, 0.0005, f)
    a.objet(pc)

    # trois ailettes de laiton par fenêtre ; au tronçon avant, l'ailette du milieu de la fenêtre
    # haute gauche est perdue ; au tronçon du milieu, celle de la fenêtre basse gauche est en acier brut
    pc = a.piece("Ailettes", "Laiton")
    for i, (av0, av1, h0, h1) in enumerate(fenetres):
        for k in range(3):
            hc = h0 + (h1 - h0) * (k + 1) / 4
            for signe in (1, -1):
                perdue = signe > 0 and i == 5 and k == 1
                if perdue:
                    continue
                brute = signe > 0 and i == 2 and k == 1
                x0, x1 = sorted((signe * 0.0148, signe * 0.0212))
                kit.boite(pc, av0 + 0.002, av1 - 0.002, hc - 0.0011, hc + 0.0011, x0, x1,
                          "AcierBrut" if brute else "Laiton")
    a.objet(pc)

    pc = a.piece("Bande_Ventilee", "Polymere")
    for av in (0.226, 0.296, 0.366, 0.436, 0.506, 0.576, 0.636):
        kit.boite(pc, av - 0.004, av + 0.004, 0.1455, 0.1490, -0.0030, 0.0030, "Polymere")
    f = kit.boite(pc, 0.215, 0.648, 0.1485, H_BANDE, -0.0045, 0.0045, "Polymere")
    kit.chanfreiner(pc, 0.0008, f)
    a.objet(pc)


def tete(a):
    pc = a.piece("Tete", "PeintureClaire")
    tete = [(0.648, ca.section8(0.0250, 0.064, 0.1490, 0.0080, 0.0080)),
            (0.690, ca.section8(0.0240, 0.066, 0.1475, 0.0075, 0.0075))]
    f = kit.loft(pc, kit.sections_av(tete), "PeintureClaire",
                 mats_cotes=ca.mats8(chanfrein_haut="Peinture", chanfrein_bas="Peinture", dessous="Peinture"))
    kit.chanfreiner(pc, 0.0012, f)
    ca.rail(pc, 0.653, 0.686, P(0.653, 0.105, -0.0243), "droite", largeur=0.014, epaisseur=0.003, dent=0.003,
            pas=0.008, longueur_dent=0.004, mat="Polymere")   # petit rail à droite (laser tactique)
    a.objet(pc)
    pc = a.piece("Bouche", "Bouche")
    kit.anneau(pc, 0.688, AV_BOUCHE, H_AXE, 0.0145, 0.0095, 12, "Bouche")
    kit.disque(pc, P(0.6885, H_AXE), AVANT, 0.0096, 12, mat="Interieur")
    kit.anneau(pc, 0.688, 0.694, H_TUBE, 0.0105, 0.0060, 10, "Acier")     # bouchon du magasin
    kit.disque(pc, P(0.6885, H_TUBE), AVANT, 0.0061, 10, mat="Acier")
    a.objet(pc)


def tube_et_pompe(a):
    pc = a.piece("Tube_Magasin", "Acier")
    kit.cylindre(pc, P(0.207, H_TUBE), P(0.652, H_TUBE), 0.0118, 8)
    a.objet(pc)

    pc = a.piece("Pompe", "Polymere")
    centre = (0.0, H_TUBE)
    sections = [(0.330, kit.mise_a_echelle(SECTION_POMPE, 0.93, centre)), (0.338, SECTION_POMPE),
                (0.492, SECTION_POMPE), (0.500, kit.mise_a_echelle(SECTION_POMPE, 0.93, centre))]
    f = kit.loft(pc, kit.sections_av(sections), "Polymere")
    kit.chanfreiner(pc, 0.0015, f)
    for av in (0.352, 0.376, 0.400, 0.424, 0.448):          # nervures sous la main gauche
        f = kit.boite(pc, av, av + 0.010, 0.0535, 0.0570, -0.0080, 0.0080, "Polymere")
        kit.chanfreiner(pc, 0.0006, f)
    for x0, x1 in ((0.0240, 0.0284), (-0.0284, -0.0240)):   # panneaux blanc cassé sur les flancs
        f = kit.prisme(pc, [(0.350, 0.0680), (0.462, 0.0680), (0.468, 0.0760), (0.462, 0.0860), (0.350, 0.0860),
                            (0.344, 0.0760)], x0, x1, "PeintureClaire")
        kit.chanfreiner(pc, 0.0006, f)
    f = kit.prisme(pc, [(0.462, 0.0600), (0.498, 0.0600), (0.488, 0.0120), (0.478, 0.0110)], -0.0110, 0.0110,
                   "Polymere")                               # arrêt de main, à l'avant de la pompe
    kit.chanfreiner(pc, 0.0012, f)
    for x0, x1 in ((0.0112, 0.0134), (-0.0134, -0.0112)):   # barres d'armement, le long du tube
        kit.boite(pc, 0.207, 0.334, H_TUBE - 0.0025, H_TUBE + 0.0025, x0, x1, "Acier")
    ob = a.objet(pc)
    cp = Piece("coupe", "Polymere")
    kit.prisme(cp, [(0.471, 0.0520), (0.490, 0.0520), (0.483, 0.0270)], -0.030, 0.030)   # l'ajour de l'arrêt
    a.couper(ob, cp, auto=True)


def crosse(a):
    """La crosse de chasse, à poignet droit : un groupe à part, qu'une crosse de l'armurerie remplacera."""
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Polymere")
    f = kit.prisme(pc, PROFIL_CROSSE, -0.0165, 0.0165, "Polymere")
    kit.chanfreiner(pc, 0.0040, f)
    a.objet(pc).parent = groupe

    pc = a.piece("Plaque_Couche", "Caoutchouc")
    f = kit.prisme(pc, [(-0.340, 0.118), (-0.351, 0.115), (-0.358, 0.000), (-0.347, -0.010)], -0.0190, 0.0190)
    kit.chanfreiner(pc, 0.0025, f)
    ca.mire_crash(pc, P(-0.302, 0.072, 0.0167), GAUCHE, r=0.0070)
    a.objet(pc).parent = groupe

    # le poignet de la crosse, fêlé, serré de ruban toilé
    pc = a.piece("Ruban_Crosse", "RubanNoir")
    for av, cy, h, largeur, incl in ((-0.050, 0.0949, 0.0752, 0.016, 0.05), (-0.072, 0.0930, 0.0773, 0.015, -0.06)):
        contour = kit.contour_rect(0.0350, h, chanfrein=0.0052, cy=cy)
        kit.bande(pc, contour, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, largeur, 0.0012, "RubanNoir",
                  inclinaison=incl, irregularite=0.0005, rng=a.rng)
    a.objet(pc).parent = groupe
    pc = a.piece("Fissure_Crosse", "Interieur")
    fissure(pc, [(-0.020, 0.112), (-0.034, 0.104), (-0.048, 0.099), (-0.064, 0.090), (-0.082, 0.086), (-0.096, 0.079)],
            0.0167, 0.0009)
    a.objet(pc, angle=None).parent = groupe


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0095, P(0.112, 0.088, 0.0262), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "N° 3020", 0.0045, P(0.112, 0.088, -0.0262), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.encoches(pc, 0.684, 0.0760, 0.0850, 0.0249, 7, mat="MarquageNoir", largeur=0.0014, pas=0.0035)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    gaine(a)
    tete(a)
    tube_et_pompe(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE, r_int=0.0052, r_ext=0.0072)
    ca.guidon(a, AV_GUIDON, H_BANDE, H_VISEE, demi_base=0.0058)
    crosse(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.410, 0.0560),
              main_droite=P(-0.026, 0.096))
    a.accroches({
        "Viseur": P(0.044, H_RAIL),                 # rail de la carcasse, devant la hausse
        "Laser": P(0.669, 0.105, -0.0303),          # petit rail du flanc droit de la tête
        "Poignee": P(0.410, 0.0570),                # sous la pompe
        "Chargeur": P(0.694, H_TUBE),               # bouchon du magasin : rallonge
        "Crosse": P(AV_CARCASSE, 0.100),
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_gaine", P(0.44, 0.16, 0.17), P(0.44, 0.12, 0.0), 45),
    ("detail_crosse", P(-0.16, 0.07, 0.38), P(-0.16, 0.07, 0.0), 45),
    ("detail_poignet", P(-0.03, 0.10, 0.20), P(-0.03, 0.09, 0.0), 45),
]

ATELIER = ca.principal("FAP-30", "fusil à pompe série 30", "Fusil à pompe série 30", construire,
                       (AV_BOUCHE, H_AXE), vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
