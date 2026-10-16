"""Fusil à dispersion laser FX-3 de Duel Arena (Blender 5.2, low poly) : la classe fusil à pompe, en arme à faisceau.

Un prototype du programme d'essai, aux formes franchement futuristes (référence : les fusils à
pompe d'Apex Legends) : carcasse courte et massive, chambre visible de part en part, tube de
charges sous une gaine épaisse qui finit en bloc de refroidissement à ailettes, tête évasée en
coupe : au fond, huit lentilles dessinent la gerbe du jeu (WeaponData.pelletPattern : un plomb
au centre, un triangle, un carré ; aucun hasard, GDD § 3), quatre cornes sur le bord. Mire à anneau hexagonal sur la plaque de la carcasse, guidon sur la tête.

Il a servi : une corne de la tête cassée net, la pompe fendue tenue au fil de fer, une tôle
soudée sur le flanc droit de la carcasse, une cellule de rechange perdue dans la cartouchière de
la crosse, la tête noircie.

Pour les animations, il garde les points de prise du FAP-03 : poignée à l'origine, fenêtre de
chargement sous la carcasse (les charges entrent une à une, là où la main gauche du clip va et
vient), pompe sous la main gauche, verrou en haut à gauche au-dessus de la poignée, crosse à l'épaule.

  blender -b --factory-startup -P laser_dispersion.py -- [--sortie f.blend] [--rendus dossier] [--vues a,b]
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

H_AXE = 0.115           # axe du faisceau (la Bouche du FAP-03)
H_TUBE = 0.081          # tube de charges, sous la gaine
AV_BOUCHE = 0.704       # devant des bagues des lentilles, au fond de la coupe
H_PLAQUE = 0.148        # dessus de la carcasse
H_RAIL = H_PLAQUE + 0.0025
H_VISEE = 0.168
AV_HAUSSE = -0.025       # un centimètre devant l'arrière : la place du compteur
AV_GUIDON = 0.668

# La gerbe du jeu (Arme_Pompe.asset, pelletPattern, en degrés ; x vers la droite du tireur, y vers
# le haut), et l'échelle qui la pose sur la face de la tête.
GERBE = [(0.0, 0.0), (0.0, 1.5), (-1.299, -0.75), (1.299, -0.75),
         (2.475, 2.475), (-2.475, 2.475), (-2.475, -2.475), (2.475, -2.475)]
ECHELLE_GERBE = 0.0075

CARCASSE = [(-0.062, ca.section8(0.0220, 0.066, 0.140, 0.008, 0.004)),
            (-0.052, ca.section8(0.0255, 0.062, 0.148, 0.010, 0.004)),
            (0.180, ca.section8(0.0265, 0.060, 0.148, 0.011, 0.005)),
            (0.212, ca.section8(0.0255, 0.064, 0.144, 0.010, 0.005))]
# La gaine finit en bloc de refroidissement, plus large, juste derrière la tête.
GAINE = [(0.205, ca.section8(0.0185, 0.0975, 0.1345, 0.0070, 0.0050)),
         (0.528, ca.section8(0.0185, 0.0975, 0.1345, 0.0070, 0.0050)),
         (0.540, ca.section8(0.0220, 0.0960, 0.1385, 0.0080, 0.0055)),
         (0.626, ca.section8(0.0220, 0.0960, 0.1385, 0.0080, 0.0055))]
# La tête s'évase vers l'avant ; son devant est creusé en coupe (COUPE), lentilles au fond.
TETE = [(0.620, ca.section8(0.0260, 0.0680, 0.1420, 0.0100, 0.0100)),
        (0.640, ca.section8(0.0330, 0.0610, 0.1490, 0.0130, 0.0130)),
        (0.690, ca.section8(0.0360, 0.0590, 0.1520, 0.0140, 0.0140)),
        (0.714, ca.section8(0.0350, 0.0600, 0.1510, 0.0135, 0.0135))]
COUPE = kit.mise_a_echelle(TETE[-1][1], 0.78, (0.0, H_AXE))
AV_FOND = 0.698         # fond de la coupe
# Pompe anguleuse, et l'arrêt de main à son avant.
SECTION_POMPE = [(0.0150, 0.0965), (0.0260, 0.0905), (0.0275, 0.0730), (0.0210, 0.0590), (0.0090, 0.0560),
                 (-0.0090, 0.0560), (-0.0210, 0.0590), (-0.0275, 0.0730), (-0.0260, 0.0905), (-0.0150, 0.0965)]
SECTION_ARRET = [(0.0150, 0.0965), (0.0270, 0.0905), (0.0290, 0.0720), (0.0225, 0.0540), (0.0095, 0.0500),
                 (-0.0095, 0.0500), (-0.0225, 0.0540), (-0.0290, 0.0720), (-0.0270, 0.0905), (-0.0150, 0.0965)]
# Crosse pleine, sculptée : plus épaisse au talon, appui-joue en céramique sur le dessus.
CROSSE = [(-0.058, ca.section8(0.0175, 0.068, 0.1400, 0.0070, 0.0060)),
          (-0.082, ca.section8(0.0180, 0.062, 0.1395, 0.0070, 0.0060)),
          (-0.126, ca.section8(0.0185, 0.056, 0.1390, 0.0072, 0.0060)),
          (-0.210, ca.section8(0.0200, 0.034, 0.1365, 0.0076, 0.0065)),
          (-0.266, ca.section8(0.0215, 0.024, 0.1345, 0.0080, 0.0070)),
          (-0.284, ca.section8(0.0210, 0.028, 0.1300, 0.0080, 0.0070))]

def carcasse(a):
    pc = a.piece("Carcasse", "Peinture")
    f = kit.loft(pc, kit.sections_av(CARCASSE), "Peinture", mats_cotes=ca.mats8(chanfrein_haut="Ceramique"))
    kit.chanfreiner(pc, 0.0018, f)
    f = kit.prisme(pc, [(-0.048, 0.066), (-0.040, 0.046), (0.090, 0.046), (0.094, 0.066)], -0.0220, 0.0220)
    kit.chanfreiner(pc, 0.0018, f)
    ca.pontet(pc, [(0.088, 0.050), (0.084, 0.020), (0.070, 0.006), (0.040, 0.004), (0.024, 0.014)])
    ca.detente(pc, 0.058, 0.050)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    kit.boite(cp, 0.088, 0.152, 0.103, 0.129, -0.040, 0.040)      # chambre, visible de part en part
    kit.boite(cp, 0.098, 0.192, 0.052, 0.074, -0.0135, 0.0135)    # fenêtre de chargement, dessous
    a.couper(ob, cp, auto=True)

    pc = a.piece("Chambre", "Lueur")   # la charge engagée, tenue par deux brides
    kit.cylindre(pc, P(0.084, H_AXE), P(0.156, H_AXE), 0.0080, 8, mat="Lueur")
    for av in (0.100, 0.138):
        f = kit.cylindre(pc, P(av, H_AXE), P(av + 0.007, H_AXE), 0.0098, 8, mat="Bobine")
        kit.chanfreiner(pc, 0.0006, f)
    for signe in (1, -1):   # cadre de bronze autour de la fenêtre
        x0, x1 = sorted((signe * 0.0254, signe * 0.0274))
        f = kit.boite(pc, 0.084, 0.156, 0.1290, 0.1330, x0, x1, "Bobine")
        f += kit.boite(pc, 0.084, 0.156, 0.0990, 0.1030, x0, x1, "Bobine")
        f += kit.boite(pc, 0.084, 0.088, 0.1030, 0.1290, x0, x1, "Bobine")
        f += kit.boite(pc, 0.152, 0.156, 0.1030, 0.1290, x0, x1, "Bobine")
    a.objet(pc)

    pc = a.piece("Commandes", "Acier")
    kit.quad(pc, [P(0.099, 0.0738, -0.0128), P(0.191, 0.0738, -0.0128), P(0.191, 0.0738, 0.0128),
                  P(0.099, 0.0738, 0.0128)], "Bobine", normale=-HAUT)                # contacts, au fond de la fenêtre
    kit.quad(pc, [P(0.104, 0.0736, -0.0030), P(0.186, 0.0736, -0.0030), P(0.186, 0.0736, 0.0030),
                  P(0.104, 0.0736, 0.0030)], "Lueur", normale=-HAUT)
    kit.cylindre(pc, P(0.078, 0.057, 0.0215), P(0.078, 0.057, 0.0250), 0.0042, 8)     # sûreté
    kit.cylindre(pc, P(0.078, 0.057, -0.0215), P(0.078, 0.057, -0.0250), 0.0042, 8)
    f = kit.prisme(pc, [(0.004, 0.124), (0.028, 0.121), (0.028, 0.129), (0.004, 0.131)], 0.0258, 0.0300)
    kit.chanfreiner(pc, 0.0006, f)                               # verrou, là où la main gauche arme
    kit.quad(pc, [P(0.008, 0.1242, 0.0301), P(0.024, 0.1222, 0.0301), P(0.024, 0.1268, 0.0301),
                  P(0.008, 0.1288, 0.0301)], "Lueur", normale=GAUCHE)
    a.objet(pc)


def canon(a):
    """Gaine du faisceau et son bloc de refroidissement, tube de charges et pompe."""
    pc = a.piece("Gaine", "Peinture")
    f = kit.loft(pc, kit.sections_av(GAINE), "Peinture",
                 mats_cotes=ca.mats8(chanfrein_haut="Ceramique", dessous="Polymere"))
    kit.chanfreiner(pc, 0.0012, f)
    for av in (0.546, 0.560, 0.574, 0.588, 0.602, 0.616):   # ailettes sur le bloc
        f = kit.boite(pc, av, av + 0.005, 0.1375, 0.1445, -0.0130, 0.0130, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    ob = a.objet(pc)
    cp = Piece("coupe", "Interieur")
    fentes = (0.548, 0.566, 0.584, 0.602)
    for av in fentes:                                    # évents du bloc, côté joueur
        kit.boite(cp, av, av + 0.010, 0.104, 0.128, 0.012, 0.035)
    a.couper(ob, cp, auto=True)   # gaine et ailettes se chevauchent
    pc = a.piece("Lueurs_Gaine", "Lueur")
    for av in fentes:
        kit.quad(pc, [P(av + 0.0004, 0.1044, 0.0122), P(av + 0.0096, 0.1044, 0.0122), P(av + 0.0096, 0.1276, 0.0122),
                      P(av + 0.0004, 0.1276, 0.0122)], "Lueur", normale=GAUCHE)
    a.objet(pc, angle=None)

    pc = a.piece("Tube_Charges", "Acier")   # six bagues allumées : six charges
    kit.cylindre(pc, P(0.207, H_TUBE), P(0.636, H_TUBE), 0.0118, 8)
    for av in (0.236, 0.266, 0.296, 0.516, 0.548, 0.580):
        kit.cylindre(pc, P(av - 0.003, H_TUBE), P(av + 0.003, H_TUBE), 0.0123, 8, mat="Lueur")
    a.objet(pc)

    pc = a.piece("Pompe", "Polymere")
    centre = (0.0, H_TUBE)
    sections = [(0.330, kit.mise_a_echelle(SECTION_POMPE, 0.93, centre)), (0.338, SECTION_POMPE),
                (0.466, SECTION_POMPE), (0.474, SECTION_ARRET), (0.488, SECTION_ARRET),
                (0.494, kit.mise_a_echelle(SECTION_ARRET, 0.94, centre))]
    f = kit.loft(pc, kit.sections_av(sections), "Polymere")
    kit.chanfreiner(pc, 0.0015, f)
    for av in (0.352, 0.376, 0.400, 0.424, 0.448):          # nervures sous la main gauche
        f = kit.boite(pc, av, av + 0.010, 0.0535, 0.0570, -0.0080, 0.0080, "Polymere")
        kit.chanfreiner(pc, 0.0006, f)
    for x0, x1 in ((0.0240, 0.0284), (-0.0284, -0.0240)):   # panneaux de céramique, entre les fils
        f = kit.prisme(pc, [(0.384, 0.0680), (0.436, 0.0680), (0.440, 0.0760), (0.436, 0.0860), (0.384, 0.0860),
                            (0.380, 0.0760)], x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0006, f)
    for x0, x1 in ((0.0112, 0.0134), (-0.0134, -0.0112)):   # barres d'armement, le long du tube
        kit.boite(pc, 0.207, 0.334, H_TUBE - 0.0025, H_TUBE + 0.0025, x0, x1, "Acier")
    a.objet(pc)

def tete(a):
    pc = a.piece("Tete", "Peinture")
    f = kit.loft(pc, kit.sections_av(TETE), "Peinture",
                 mats_cotes=ca.mats8(chanfrein_haut="Ceramique", chanfrein_bas="Polymere", dessous="Polymere"))
    kit.chanfreiner(pc, 0.0012, f)
    for signe in (1, -1):   # la ligne de tir, en lueur sur les flancs
        x0, x1 = signe * 0.0335, signe * 0.0361
        kit.quad(pc, [P(0.645, 0.1125, x0), P(0.688, 0.1125, x1), P(0.688, 0.1175, x1), P(0.645, 0.1175, x0)],
                 "Lueur", normale=GAUCHE * signe)
    ob = a.objet(pc)
    cp = Piece("coupe", "Bouche")   # la coupe, noircie par les tirs
    kit.loft(cp, [[P(AV_FOND, h, x) for x, h in COUPE], [P(0.730, h, x) for x, h in COUPE]], "Bouche")
    a.couper(ob, cp)

    pc = a.piece("Emetteur", "Bouche")   # les lentilles au fond de la coupe, et les cornes du bord
    for i, (gx, gy) in enumerate(GERBE):
        x, h = -gx * ECHELLE_GERBE, H_AXE + gy * ECHELLE_GERBE
        r = 0.0046 if i == 0 else 0.0036
        kit.anneau(pc, AV_FOND - 0.0005, AV_BOUCHE, h, r + 0.0012, r, 8, "Bouche", x=x)
        kit.disque(pc, P(AV_FOND + 0.0032, h, x), AVANT, r + 0.0001, 8, mat="Laser")
    # quatre cornes sur le bord de la coupe, qui se resserrent ; celle du bas à droite a cassé net
    for sx, sh in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        cx, ch = sx * 0.0252, (0.1411 if sh > 0 else 0.0721)
        casse = (sx, sh) == (-1, -1)

        def losange(av, s, recul):
            c = (cx - sx * recul, ch - sh * recul)
            return [P(av, c[1], c[0] + s), P(av, c[1] + s, c[0]), P(av, c[1], c[0] - s), P(av, c[1] - s, c[0])]

        if casse:
            sections = [losange(0.706, 0.0046, 0.0), losange(0.723, 0.0042, 0.0005)]
        else:
            sections = [losange(0.706, 0.0046, 0.0), losange(0.734, 0.0036, 0.0015), losange(0.752, 0.0011, 0.0034)]
        f = kit.loft(pc, sections, "Bouche")
        if casse:
            f[-1].material_index = pc.mat("AcierBrut")   # la cassure, encore claire
        kit.chanfreiner(pc, 0.0005, f)
    a.objet(pc)

def montages(a):
    """Interfaces d'accessoires : viseurs dessus, laser tactique à droite de la gaine."""
    pc = a.piece("Montages", "Peinture")
    ca.plaque_montage(pc, -0.045, 0.180, P(-0.045, H_PLAQUE), "haut", largeur=0.020, ep=0.0025, encoches=9)
    ca.plaque_montage(pc, 0.548, 0.608, P(0.548, H_AXE, -0.0218), "droite", largeur=0.014, encoches=3)
    a.objet(pc)


def crosse(a):
    groupe = a.vide(a.nom("Crosse_Standard"), a.racine, "CUBE", 0.004)
    pc = a.piece("Crosse_Corps", "Peinture")
    f = kit.loft(pc, kit.sections_av(CROSSE), "Peinture",
                 mats_cotes=ca.mats8(dessus="Ceramique", chanfrein_haut="Ceramique", chanfrein_bas="Polymere",
                                     dessous="Polymere"))
    kit.chanfreiner(pc, 0.0015, f)
    plaque = [(-0.282, ca.section8(0.0225, 0.020, 0.1360, 0.0080, 0.0080)),
              (-0.296, ca.section8(0.0220, 0.022, 0.1340, 0.0080, 0.0080))]
    f = kit.loft(pc, kit.sections_av(plaque), "Caoutchouc")   # plaque de couche
    kit.chanfreiner(pc, 0.0020, f)
    ca.mire_crash(pc, P(-0.248, 0.090, 0.0213), GAUCHE, r=0.0075)
    a.objet(pc).parent = groupe

    # cartouchière à gauche : trois cellules de rechange, la quatrième perdue
    pc = a.piece("Cartouchiere", "Polymere")
    f = kit.boite(pc, -0.180, -0.092, 0.080, 0.128, 0.0170, 0.0240, "Polymere")
    kit.chanfreiner(pc, 0.0012, f)
    for i, av in enumerate((-0.104, -0.124, -0.144, -0.164)):
        if i == 3:
            kit.quad(pc, [P(av - 0.0060, 0.084, 0.0241), P(av + 0.0060, 0.084, 0.0241), P(av + 0.0060, 0.124, 0.0241),
                          P(av - 0.0060, 0.124, 0.0241)], "Interieur", normale=GAUCHE)
            continue
        kit.cylindre(pc, P(av, 0.078, 0.0250), P(av, 0.127, 0.0250), 0.0062, 8, mat="Peinture")
        kit.cylindre(pc, P(av, 0.127, 0.0250), P(av, 0.1325, 0.0250), 0.0056, 8, mat="Lueur")
        kit.cylindre(pc, P(av, 0.0745, 0.0250), P(av, 0.078, 0.0250), 0.0050, 8, mat="Bobine")
    a.objet(pc).parent = groupe

def rafistolages(a):
    # tôle soudée sur le flanc droit de la carcasse
    pc = a.piece("Tole_Soudee", "AcierBrut")
    contour = [(0.022, 0.073), (0.070, 0.071), (0.074, 0.096), (0.026, 0.099)]
    f = kit.prisme(pc, contour, -0.0280, -0.0258)
    kit.chanfreiner(pc, 0.0006, f)
    cordon = [P(av, h, -0.0281) for av, h in contour[1:] + contour[:1]]   # soudée sur trois côtés
    kit.soudure(pc, cordon, r=0.0013, mat="Soudure", rng=a.rng, pas=0.0028, cotes=4)
    a.objet(pc)

    # la pompe fendue, tenue au fil de fer (deux fois trois tours)
    pc = a.piece("Fil_Pompe", "Fil")
    for av, incl in ((0.368, 0.03), (0.452, -0.04)):
        for k in (-0.0024, 0.0, 0.0024):
            kit.bande(pc, SECTION_POMPE, P(av + k, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0013, 0.0010,
                      "Fil", inclinaison=incl, irregularite=0.0002, rng=a.rng)
        kit.cylindre(pc, P(av, 0.0915, 0.0262), P(av - 0.004, 0.0985, 0.0288), 0.0009, 5, mat="Fil")   # torsade
    fente = [(0.343, 0.0705), (0.349, 0.0745), (0.354, 0.0727), (0.360, 0.0773), (0.366, 0.0757)]
    for (av0, h0), (av1, h1) in zip(fente, fente[1:]):   # la fêlure, sous le premier fil
        kit.quad(pc, [P(av0, h0 - 0.0003, 0.0276), P(av1, h1 - 0.0003, 0.0276), P(av1, h1 + 0.0003, 0.0276),
                      P(av0, h0 + 0.0003, 0.0276)], "Interieur", normale=GAUCHE)
    a.objet(pc)

    # le compteur de charges, sur la rampe qui monte de l'arrière de la carcasse à la hausse, face au joueur
    # (couché comme celui du FX-1 : en visée, il reste bas dans le champ, sous l'anneau)
    pc = a.piece("Compteur", "Peinture")
    f = kit.prisme(pc, [(-0.062, 0.1400), (-0.040, 0.1530), (-0.0385, 0.1530), (-0.0385, 0.1465)], -0.0140, 0.0140,
                   "Peinture")
    kit.chanfreiner(pc, 0.0010, f)
    ca.compteur_arriere(pc, P(-0.051, 0.1465), 0.0240, 0.0200, elevation=59.4, chiffres="06")
    a.objet(pc)


def marquages(a):
    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, a.code, 0.0100, P(0.120, 0.087, 0.0266), ca.VERS_CROSSE, HAUT)
    a.texte(pc, "PROTOTYPE", 0.0050, P(0.120, 0.088, -0.0266), ca.VERS_BOUCHE, HAUT, decalage=0.0003, resolution=1)
    ca.autocollant_laser(pc, P(0.668, 0.092, -0.0340), -GAUCHE, Vector((0, -1, 0)), 0.0085)
    ca.bande_hachuree(pc, [P(0.222, 0.1120, 0.0187), P(0.300, 0.1120, 0.0187), P(0.300, 0.1175, 0.0187),
                           P(0.222, 0.1175, 0.0187)], GAUCHE, n=10)
    a.objet(pc, angle=None)


def construire(a):
    carcasse(a)
    ca.poignee_futuriste(a)
    canon(a)
    tete(a)
    montages(a)
    ca.hausse(a, AV_HAUSSE, H_RAIL, H_VISEE, r_int=0.0052, r_ext=0.0072, cotes=6)
    ca.guidon(a, AV_GUIDON, 0.1505, H_VISEE, demi_base=0.0090, mat_pointe="Lueur")
    crosse(a)
    rafistolages(a)
    marquages(a)
    a.reperes(P(AV_HAUSSE, H_VISEE), P(AV_BOUCHE, H_AXE), main_gauche=P(0.410, 0.0560))
    a.accroches({
        "Viseur": P(0.070, H_RAIL),
        "Laser": P(0.578, H_AXE, -0.0243),     # la plaque à droite du bloc de refroidissement
        "Poignee": P(0.410, 0.0570),           # sous la pompe
        "Chargeur": P(0.704, H_TUBE),          # bout du tube de charges : rallonge
        "Crosse": P(-0.058, 0.100),
    })


VUES = [
    ("visee", P(AV_HAUSSE - 0.075, H_VISEE), P(1.0, H_VISEE), 30),
    ("detail_tete", P(0.88, 0.15, 0.11), P(0.70, H_AXE, 0.0), 40),
    ("face", P(1.10, H_AXE + 0.01, 0.0), P(0.70, H_AXE, 0.0), 40),
    ("detail_chambre", P(0.12, 0.17, 0.17), P(0.12, 0.105, 0.0), 45),
    ("detail_crosse", P(-0.14, 0.15, 0.21), P(-0.14, 0.10, 0.0), 50),
]

ATELIER = ca.principal("FX-3", "fusil à dispersion laser", "Fusil à dispersion laser", construire, (AV_BOUCHE, H_AXE),
                       laser=True, vues_detail=VUES, args=ca.arguments(globals().get("ARGS")))
