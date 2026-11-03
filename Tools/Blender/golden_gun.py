"""Le Big D, l'arme du Golden Gun (Blender 5.2, low poly).

D'après les photos de l'utilisateur (2026-11-02) : un derringer à un coup en acier inoxydable
satiné, à crosse de caoutchouc noir quadrillé de pointes de diamant, qui tire une seule cartouche
(une .45-70 : douille droite à bourrelet, balle chemisée à pointe de plomb), mise directement dans
la chambre. Les cotes viennent de la photo de l'arme fermée, redressée dans le repère de l'arme et
relevée autour de l'axe de sa charnière (PIV ; « av » vers la bouche, « h » en hauteur) ; l'arme garde
la prise des pistolets (la poignée à l'origine). Le canon est un octogone aux grands chanfreins,
d'après la photo de la bouche ; puis toute l'arme est agrandie de moitié autour de la prise (ECHELLE),
pour qu'on la voie bien dans la main des robots (demandé par l'utilisateur).

Le canon pivote sur l'axe de la charnière (AXE), plus bas que sur la photo pour que la charnière
dépasse moins du canon (demandé par l'utilisateur), et bascule en arrière jusqu'à l'horizontale,
au-dessus de la carcasse : c'est un groupe à part (« Canon »), dont l'origine est l'axe, avec sa
cartouche dans la chambre ; le faire tourner autour de son X (le travers de l'arme) ouvre l'arme. Tout
ce qui tourne est tracé pour passer : le nez de la carcasse, un disque autour de l'axe fendu en deux
oreilles, est plus près de l'axe que le dessus du canon, que le canon longe en basculant ; les pattes
du canon tournent dans la fente entre les oreilles ; les joues de la carcasse, qui couvrent le bas du
canon, sont derrière lui dans le sens où il tourne ; et retourné, le canon passe au-dessus du nez, du
dos de la carcasse et du chien : rien ne monte derrière l'axe plus haut que le dessus du canon n'est
sous lui.

Le guidon, une rampe droite à la bouche, porte le point rouge sur la ligne de mire, qui passe dans la
rainure du nez, entre les pattes du canon : le cran de mire.

Ce qui bouge au rechargement est un groupe à part, dont l'origine est son axe ou son point, et que l'on
fait tourner autour de son X ou glisser : le canon (« Canon », sur l'axe de la charnière) ; dans le
canon, la cartouche (« Cartouche », son culot sur l'axe de l'âme : la douille et la balle, deux pièces,
la douille vide tombant seule) ; le chien (« Chien », dessiné armé, prêt à tirer : tourné vers l'avant
de ARMEMENT_CHIEN, il s'abat) ; le levier qui libère le canon (« Levier »). pose(t) donne leurs
positions à chaque instant du rechargement (ouvrir, éjecter, charger, refermer, armer). Autour du X de
Blender, le canon s'ouvre d'un angle négatif, le chien s'abat d'un angle positif, le levier s'abaisse
d'un angle négatif.

--teinte or (le Golden Gun, choisi par l'utilisateur le 2026-11-02) ou inox (les photos).
"""

import importlib
import math
import os
import sys

from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_armes as ca  # noqa: E402

importlib.reload(ca)
kit = ca.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

ECHELLE = 1.50              # l'arme agrandie autour de la prise, pour qu'on la voie dans la main des robots
PIV = (0.042, 0.070)        # l'axe de la charnière de la photo, dans le repère de l'arme ; le reste se relève autour
AXE = (0.0, -0.0045)        # l'axe de la charnière du Big D : 6,5 mm au-dessus du canon, contre 11 sur la photo
D_HAUT = -0.011             # le dessus du canon
D_BAS = -0.042              # son dessous
D_AXE = -0.0210             # l'âme, dans le haut du canon
DEMI = 0.0120               # demi-largeur du canon et de la carcasse
DEMI_DESSUS = 0.0060        # le dessus du canon, entre ses grands chanfreins
CHANFREIN_HAUT = 0.0060
DEMI_DESSOUS = 0.0070
CHANFREIN_BAS = 0.0050
DEMI_AILERON = 0.0056       # l'aileron du guidon, presque aussi large que le dessus
DEMI_TENON = 0.0085         # le tenon du canon, entre les joues de la carcasse
DEMI_FENTE = 0.0075         # la fente du nez, entre ses deux oreilles, où tournent les pattes du canon
DEMI_MIRE = 0.0030          # la rainure de visée, entre les pattes
AV_BOUCHE = 0.109
AV_CULASSE = -0.0050        # l'arrière du canon, contre le bouclier de la carcasse (à -0,006)
H_VISEE = -0.0015           # la ligne de mire, 9,5 mm au-dessus du canon
R_AME = 0.0058
R_NEZ = 0.0058              # le nez, un disque autour de l'axe, que le canon longe en basculant

# le bord avant des joues de la carcasse, qui couvrent le bas du canon (de l'arrière vers l'avant) ; derrière
# l'axe, un arc autour de lui, que le canon longe en s'ouvrant au lieu d'y plonger
JOUE = ([(AXE[0] + 0.0210 * math.cos(math.radians(d)), AXE[1] + 0.0210 * math.sin(math.radians(d)))
         for d in (-106, -100, -95, -90)]
        + [(0.0133, -0.0262), (0.0267, -0.0275), (0.0400, -0.0308), (0.0453, -0.0342), (0.0473, -0.0395),
           (0.0478, -0.0425)])
# le guidon, une rampe droite comme sur la photo, jusqu'à la face où se pose le point rouge : tout ce qu'on voit du
# guidon dans le cran de mire, la rampe restant cachée sous le fond du cran
AILERON = [(0.0800, D_HAUT), (0.1030, H_VISEE - 0.0033), (0.1030, H_VISEE + 0.0016), (0.1076, H_VISEE + 0.0016),
           (0.1086, H_VISEE + 0.0012), (0.1090, H_VISEE + 0.0004), (0.1090, D_HAUT)]
# le chien abattu, plus bas que sur la photo : sa crête reste sous la ligne de mire ; il tourne autour de AXE_CHIEN,
# dans la carcasse, et le modèle le montre armé, tourné vers l'arrière de ARMEMENT_CHIEN
CHIEN = [(-0.0244, -0.0174), (-0.0246, -0.0104), (-0.0250, -0.0069), (-0.0262, -0.0050), (-0.0285, -0.0042),
         (-0.0315, -0.0046), (-0.0338, -0.0054), (-0.0350, -0.0064), (-0.0345, -0.0074), (-0.0325, -0.0080),
         (-0.0300, -0.0092), (-0.0285, -0.0114), (-0.0282, -0.0174)]
AXE_CHIEN = (-0.0263, -0.0150)
ARMEMENT_CHIEN = 40.0       # degrés
AXE_LEVIER = (0.0087, -0.0435)
ABAISSEMENT_LEVIER = 14.0   # degrés : le bout du levier, sous le pouce, descend et libère le canon
OUVERTURE_CANON = 180.0     # degrés : le canon à l'horizontale, au-dessus de la carcasse, la chambre vers l'avant
SORTIE_DOUILLE = 0.060      # la douille vide hors de la chambre, avant de tomber
APPROCHE_CARTOUCHE = 0.080  # la cartouche neuve, présentée devant la chambre avant d'y entrer
# Le rechargement, de 0 (le coup parti, chien abattu, douille vide dans la chambre) à 1 (refermé, armé) : quand
# chaque pièce bouge, en part de sa durée. Les mains du robot s'y calent, et Unity le rejoue (voir pose).
DUREE_RECHARGEMENT = 2.5    # s, celle du Golden Gun (Arme_GoldenGun.asset)
ETAPES = {
    "levier": (0.02, 0.10),      # le pouce abaisse le levier, puis le relâche
    "ouverture": (0.08, 0.24),   # la main gauche lance le canon, qui bascule jusqu'à l'horizontale
    "ejection": (0.14, 0.18),    # la chambre tournée vers le bas, la douille vide glisse dehors, puis tombe
    "insertion": (0.50, 0.62),   # la cartouche neuve, présentée devant la chambre, y entre
    "fermeture": (0.68, 0.82),   # la main gauche ramène le canon, qui se verrouille
    "armement": (0.86, 0.93),    # le pouce arme le chien
}
# la détente de la photo : une lame qui part vers l'arrière, face avant creuse, et dont le bout revient vers l'avant
DETENTE = [(0.0222, -0.0430), (0.0212, -0.0470), (0.0192, -0.0510), (0.0175, -0.0550), (0.0166, -0.0588),
           (0.0164, -0.0622), (0.0169, -0.0648), (0.0168, -0.0662), (0.0160, -0.0668), (0.0150, -0.0664),
           (0.0140, -0.0645), (0.0130, -0.0610), (0.0126, -0.0575), (0.0133, -0.0545), (0.0150, -0.0510),
           (0.0172, -0.0475), (0.0190, -0.0430)]
PONTET = [(0.0497, -0.0442), (0.0490, -0.0480), (0.0475, -0.0560), (0.0458, -0.0640), (0.0446, -0.0682),
          (0.0425, -0.0705), (0.0350, -0.0712), (0.0200, -0.0718), (0.0120, -0.0715), (0.0090, -0.0700),
          (0.0078, -0.0660), (0.0075, -0.0560), (0.0078, -0.0475)]
# la crosse, son caoutchouc vu de gauche ; la carcasse se voit devant (sa bande avant) et au-dessus
CROSSE = [(-0.0213, -0.0315), (-0.0293, -0.0275), (-0.0400, -0.0262), (-0.0480, -0.0268), (-0.0573, -0.0308),
          (-0.0627, -0.0382), (-0.0653, -0.0475), (-0.0664, -0.0608), (-0.0660, -0.0742), (-0.0647, -0.0862),
          (-0.0613, -0.0955), (-0.0560, -0.1002), (-0.0467, -0.1022), (-0.0333, -0.1015), (-0.0253, -0.0988),
          (-0.0216, -0.0942), (-0.0207, -0.0848), (-0.0213, -0.0742), (-0.0224, -0.0608), (-0.0220, -0.0475),
          (-0.0213, -0.0368)]
VIS_CROSSE = (-0.0390, -0.0645)
DEMI_CROSSE = 0.0145
ARRONDI_CROSSE = 0.0030     # le bord du caoutchouc, un quart de rond
# La crosse de la photo est presque droite et, agrandie avec l'arme, trop épaisse pour la main des robots, qui tient
# celle des pistolets, inclinée de 18° : elle est inclinée vers l'arrière et réduite autour de son haut, sans changer
# de forme ; la carcasse au-dessus ne bouge pas
PRISE_ANCRE = (-0.044, -0.030)
PRISE_ANGLE = math.radians(-12.0)
PRISE_ECHELLE = 0.82
PRISE_DECALAGE = 0.0035     # vers l'avant : la crosse centrée sur celle des pistolets

TEINTES = {   # où va l'acier ou l'or ; le chien, la détente et le levier restent d'acier
    "inox": dict(cadre="Inox", canon="Inox", pieces="Inox", gravure="GravureClaire"),
    "or": dict(cadre="Or", canon="Or", pieces="Inox", gravure="GravureSombre"),
}


def lire_arguments():
    """--teinte, puis les options communes des armes."""
    brut = globals().get("ARGS")
    if brut is None:
        brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    brut = list(brut)
    teinte = "or"
    if "--teinte" in brut:
        i = brut.index("--teinte")
        teinte = brut[i + 1]
        del brut[i:i + 2]
    return teinte, ca.arguments(brut)


TEINTE, ARGUMENTS = lire_arguments()


def q(av, h):
    """Un point du profil, relevé autour de l'axe de la charnière."""
    return (PIV[0] + av, PIV[1] + h)


def Q(av, h, x=0.0):
    return P(PIV[0] + av, PIV[1] + h, x)


def arc(r, degres):
    """Des points d'un arc de rayon r autour de l'axe de la charnière."""
    return [(AXE[0] + r * math.cos(math.radians(d)), AXE[1] + r * math.sin(math.radians(d))) for d in degres]


def tourner(p, centre, degres):
    """p tourné autour de centre dans le plan du profil : vers l'arrière, en haut, pour un angle positif."""
    a = math.radians(degres)
    dav, dh = p[0] - centre[0], p[1] - centre[1]
    return (centre[0] + dav * math.cos(a) - dh * math.sin(a), centre[1] + dav * math.sin(a) + dh * math.cos(a))


def arme(p):
    """Un point du chien, dessiné abattu, à sa place une fois armé."""
    return tourner(p, AXE_CHIEN, ARMEMENT_CHIEN)


def prise(p):
    """Un point de la crosse, ou de la carcasse qui la porte, ramené à la prise de la main (voir PRISE_ANGLE) ; on
    passe en douceur de la crosse au reste de la carcasse, qui ne bouge pas."""
    av, h = p
    w = min(1.0, max(0.0, (-0.030 - h) / 0.020)) * min(1.0, max(0.0, (-0.004 - av) / 0.010))
    if w == 0.0:
        return (av, h)
    dav, dh = (av - PRISE_ANCRE[0]) * PRISE_ECHELLE, (h - PRISE_ANCRE[1]) * PRISE_ECHELLE
    c, s = math.cos(PRISE_ANGLE), math.sin(PRISE_ANGLE)
    cible = (PRISE_ANCRE[0] + PRISE_DECALAGE + dav * c - dh * s, PRISE_ANCRE[1] + dav * s + dh * c)
    return (av + w * (cible[0] - av), h + w * (cible[1] - h))


def prisme(pc, profil, x0, x1, mat, chanfrein=0.0):
    f = kit.prisme(pc, [q(av, h) for av, h in profil], x0, x1, mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def boite(pc, av0, av1, h0, h1, x0, x1, mat=None):
    return kit.boite(pc, PIV[0] + av0, PIV[0] + av1, PIV[1] + h0, PIV[1] + h1, x0, x1, mat)


def trait(pc, a0, a1, largeur, x, mat):
    """Un trait plat sur un flanc (x : sa face), de a0 à a1."""
    d = Vector((a1[0] - a0[0], a1[1] - a0[1]))
    n = Vector((-d.y, d.x)).normalized() * (largeur / 2)
    coins = [Q(a0[0] - n.x, a0[1] - n.y, x), Q(a1[0] - n.x, a1[1] - n.y, x), Q(a1[0] + n.x, a1[1] + n.y, x),
             Q(a0[0] + n.x, a0[1] + n.y, x)]
    kit.quad(pc, coins, mat, normale=GAUCHE if x > 0 else -GAUCHE)


def troncon(pc, culot, axe, profil, mat):
    """Un tronçon de cartouche, tourné autour de axe : profil = (distance au culot, rayon)."""
    s0, s1 = profil[0][0], profil[-1][0]
    kit.tranches(pc, culot + axe * s0, culot + axe * s1, [r for _, r in profil], 12, mat=mat,
                 positions=[(s - s0) / (s1 - s0) for s, _ in profil])


def douille(pc, culot, axe):
    """La douille de la .45-70, de son culot le long de axe : le bourrelet, le corps droit, l'amorce."""
    axe = axe.normalized()
    troncon(pc, culot, axe, [(0.0, 0.0077), (0.0018, 0.0077), (0.0018, 0.0060), (0.0030, 0.0064), (0.0525, 0.0062),
                             (0.0535, 0.0060)], "LaitonCartouche")
    kit.disque(pc, culot - axe * 0.0001, -axe, 0.0027, 12, mat="AcierBrut")   # l'amorce


def balle(pc, culot, axe):
    """Sa balle chemisée à pointe de plomb, sertie au bout de la douille (65 mm en tout)."""
    axe = axe.normalized()
    troncon(pc, culot, axe, [(0.0535, 0.00585), (0.0565, 0.00585), (0.0598, 0.0051), (0.0620, 0.0039)], "Cuivre")
    troncon(pc, culot, axe, [(0.0620, 0.0039), (0.0640, 0.0027), (0.0650, 0.0019)], "Plomb")


def cartouche(pc, culot, axe):
    douille(pc, culot, axe)
    balle(pc, culot, axe)


def mobile(a, nom, centre, dans=None):
    """Le groupe d'une pièce qui bouge, dont l'origine est son axe ou son point (centre, dans le repère de l'arme),
    sous la racine ou sous un autre groupe (dans : ce que mobile a rendu pour lui). Rend le groupe et de quoi y
    accrocher une pièce, dont le maillage reste dans le repère de l'arme."""
    parent, base = dans if dans else (a.racine, Vector())
    groupe = a.vide(a.nom(nom), parent, "CUBE", 0.004, position=centre - base)

    def accrocher(ob):
        ob.parent = groupe
        ob.location = -centre
        return ob

    return (groupe, centre), accrocher


def canon(a, T):
    """Le canon et ce qui bascule avec lui, sous un groupe dont l'origine est l'axe de la charnière."""
    groupe, accrocher = mobile(a, "Canon", Q(*AXE))
    mat = T["canon"]

    # le bloc, un octogone aux grands chanfreins ; le bas, que couvrent les joues de la carcasse, réduit à un tenon
    # entre elles
    section = [(DEMI_DESSUS, D_HAUT), (DEMI, D_HAUT - CHANFREIN_HAUT), (DEMI, D_BAS + CHANFREIN_BAS),
               (DEMI_DESSOUS, D_BAS), (-DEMI_DESSOUS, D_BAS), (-DEMI, D_BAS + CHANFREIN_BAS),
               (-DEMI, D_HAUT - CHANFREIN_HAUT), (-DEMI_DESSUS, D_HAUT)]
    pc = a.piece("Canon_Bloc", mat)
    f = kit.loft(pc, kit.sections_av([(PIV[0] + av, [(x, PIV[1] + h) for x, h in section])
                                      for av in (AV_CULASSE, AV_BOUCHE)]), mat)
    kit.chanfreiner(pc, 0.0008, f)
    ob = a.objet(pc)
    cp = Piece("coupe", mat)
    joues = ([(av, h + 0.00015) for av, h in JOUE]   # un jeu de 0,15 mm au-dessus des joues
             + [(0.0520, -0.0425), (0.0520, -0.0460), (AV_CULASSE - 0.001, -0.0460), (AV_CULASSE - 0.001, -0.0255)])
    for x0, x1 in ((DEMI_TENON, DEMI + 0.002), (-DEMI - 0.002, -DEMI_TENON)):
        kit.prisme(cp, [q(av, h) for av, h in joues], x0, x1)
    a.couper(ob, cp, auto=True)
    kit.lisser(ob, 20.0)   # les chanfreins du bas, à 28° des flancs, restent des arêtes
    accrocher(ob)

    # sous le nez : le canon le remplit jusqu'à l'arc, chanfreins compris, et ses deux pattes montent tenir l'axe ;
    # l'arc s'arrête assez bas pour que, retourné, il passe au-dessus du dos de la carcasse
    pc = a.piece("Canon_Charniere", mat)
    dessus = arc(R_NEZ + 0.0003, range(-118, -37, 8))
    bas = D_HAUT - CHANFREIN_HAUT
    prisme(pc, [(-0.0040, bas)] + dessus + [(0.0020, bas)], -DEMI, DEMI, mat)   # devant, il rentre sous le nez
    # rondes autour de l'axe, sous le dessus du nez : vu d'en haut, le nez reste un bloc, fendu de la rainure
    patte = [(-0.0035, D_HAUT)] + arc(0.0050, (200, 160, 120, 90, 60, 20, -20)) + [(0.0050, D_HAUT)]
    for x0, x1 in ((DEMI_MIRE, DEMI_FENTE - 0.00005), (-DEMI_FENTE + 0.00005, -DEMI_MIRE)):   # contre les oreilles
        prisme(pc, patte, x0, x1, mat, 0.0004)
    accrocher(a.objet(pc))

    pc = a.piece("Guidon", mat)
    prisme(pc, AILERON, -DEMI_AILERON, DEMI_AILERON, mat, 0.0006)
    # le point, sur la face du haut de la rampe, face à l'œil
    kit.cylindre(pc, Q(0.1029, H_VISEE), Q(0.1051, H_VISEE), 0.0009, 8, mat="Lueur")
    kit.disque(pc, Q(AV_BOUCHE + 0.0001, D_AXE), AVANT, R_AME + 0.0014, 16, mat="AcierBrut")   # la couronne
    kit.disque(pc, Q(AV_BOUCHE + 0.0002, D_AXE), AVANT, R_AME, 16, mat="Interieur")
    accrocher(a.objet(pc))

    # sur le flanc gauche, que voit le joueur : une balle qui file vers la bouche, et le nom
    x = DEMI + 0.0003
    pc = a.piece("Gravure", T["gravure"])
    trace = [(0.0760, -0.0300), (0.0890, -0.0300), (0.0940, -0.0292), (0.0975, -0.0275), (0.0996, -0.0258),
             (0.1002, -0.0250), (0.0996, -0.0242), (0.0975, -0.0225), (0.0940, -0.0208), (0.0890, -0.0200),
             (0.0760, -0.0200)]
    for p0, p1 in zip(trace, trace[1:] + trace[:1]):
        trait(pc, p0, p1, 0.0006, x, T["gravure"])
    trait(pc, (0.0800, -0.0300), (0.0800, -0.0200), 0.0005, x, T["gravure"])
    for h, debut in ((-0.0290, 0.0500), (-0.0270, 0.0360), (-0.0250, 0.0300), (-0.0230, 0.0300), (-0.0210, 0.0360)):
        trait(pc, (debut, h), (0.0735, h), 0.0005, x, T["gravure"])
    a.texte(pc, "BIG D", 0.0040, Q(0.0800, -0.0330, x), ca.VERS_CROSSE, HAUT, mat="MarquageNoir", decalage=0.0)
    accrocher(a.objet(pc, angle=None))

    # dans la chambre, le culot contre le bouclier : la douille et la balle, sous leur groupe, dont l'origine est le
    # culot, sur l'axe de l'âme
    culot = Q(AV_CULASSE - 0.0001, D_AXE)
    _, dans_la_chambre = mobile(a, "Cartouche", culot, groupe)
    pc = a.piece("Douille", "LaitonCartouche")
    douille(pc, culot, AVANT)
    dans_la_chambre(a.objet(pc))
    pc = a.piece("Balle", "Cuivre")
    balle(pc, culot, AVANT)
    dans_la_chambre(a.objet(pc))


def carcasse(a, T):
    mat = T["cadre"]
    profil = (arc(R_NEZ, range(-50, 141, 10))                                           # le nez
              + [(-0.0080, -0.0034), (-0.0120, -0.0064), (-0.0160, -0.0088), (-0.0200, -0.0101),  # le dessus
                 (-0.0240, -0.0106),
                 (-0.0275, -0.0110), (-0.0333, -0.0155), (-0.0400, -0.0202), (-0.0467, -0.0262),  # le dos
                 (-0.0560, -0.0350), (-0.0600, -0.0600), (-0.0570, -0.0900), (-0.0300, -0.0960),  # sous le caoutchouc
                 (-0.0215, -0.0925), (-0.0200, -0.0862), (-0.0173, -0.0742), (-0.0133, -0.0608),  # la bande avant
                 (-0.0080, -0.0528), (-0.0030, -0.0485), (0.0020, -0.0470), (0.0080, -0.0472),    # l'arche
                 (0.0110, -0.0460), (0.0200, -0.0452), (0.0470, -0.0448), (0.0495, -0.0440)]      # sur le pontet
              + JOUE[::-1]
              + [(-0.0060, -0.0245), (-0.0060, -0.0100)]                                  # le bouclier
              + arc(R_NEZ, (-124, -110, -90, -70)))                                       # sous le nez
    pc = a.piece("Carcasse", mat)
    prisme(pc, [prise(p) for p in profil], -DEMI, DEMI, mat, 0.0010)
    ob = a.objet(pc)
    cp = Piece("coupe", mat)
    # la place du tenon, assez profonde pour que l'arrière du canon y passe en s'ouvrant (il descend d'abord un peu)
    boite(cp, -0.0061, 0.0520, D_BAS - 0.0006, D_HAUT, -DEMI_TENON, DEMI_TENON)
    boite(cp, AXE[0] - 0.0065, AXE[0] + 0.0090, D_HAUT + 0.0002, 0.0100, -DEMI_FENTE, DEMI_FENTE)  # la fente du nez
    boite(cp, -0.0095, -0.0060, H_VISEE - 0.0025, 0.0100, -DEMI_MIRE, DEMI_MIRE)  # la rainure de visée
    a.couper(ob, cp, auto=True)

    pc = a.piece("Pontet", mat)
    f = kit.balayage(pc, [Q(*p) for p in kit.lisser_chemin(PONTET, 2)], kit.section_rect(0.0065, 0.0032), mat,
                     normale=GAUCHE)
    kit.chanfreiner(pc, 0.0006, f)
    prisme(pc, [(0.0488, -0.0425), (0.0530, -0.0440), (0.0490, -0.0478)], -0.0030, 0.0030, mat, 0.0005)   # l'éperon
    a.objet(pc)

    _, au_chien = mobile(a, "Chien", Q(*AXE_CHIEN))   # armé : prêt à tirer
    pc = a.piece("Chien_Corps", T["pieces"])
    prisme(pc, [arme(p) for p in CHIEN], -0.0032, 0.0032, T["pieces"], 0.0006)
    for av in (-0.0300, -0.0315, -0.0330):   # les stries de la crête, sous le pouce
        strie = [(av - 0.0003, -0.0054), (av + 0.0003, -0.0054), (av + 0.0003, -0.0040), (av - 0.0003, -0.0040)]
        prisme(pc, [arme(p) for p in strie], -0.0026, 0.0026, "Interieur")
    au_chien(a.objet(pc))

    pc = a.piece("Commandes", T["pieces"])
    # la détente, aux bords arrondis
    contour = [Vector(q(*p)) for p in kit.lisser_chemin(DETENTE, 3, ferme=True)]
    retrait = kit.decaler_contour(contour, -0.0006)
    kit.loft(pc, [[P(p.x, p.y, x) for p in pts] for x, pts in ((-0.0025, retrait), (-0.0017, contour),
                                                                 (0.0017, contour), (0.0025, retrait))], T["pieces"])
    for cote in (1, -1):   # la lumière de la détente
        trait(pc, (0.0166, -0.0525), (0.0149, -0.0610), 0.0010, cote * 0.00252, "Interieur")
    bouton = (-0.0180, -0.0205)   # la sûreté transversale
    kit.cylindre(pc, Q(*bouton, DEMI - 0.0004), Q(*bouton, DEMI + 0.0012), 0.0032, 12, mat=T["pieces"])
    kit.cylindre(pc, Q(*bouton, -DEMI + 0.0004), Q(*bouton, -DEMI - 0.0008), 0.0028, 12, mat=T["pieces"])
    for cote in (1, -1):   # l'axe de la charnière, celui du chien, une vis et une goupille
        kit.disque(pc, Q(*AXE, cote * (DEMI + 0.0002)), GAUCHE * cote, 0.0020, 10, mat="Interieur")
        kit.disque(pc, Q(*AXE_CHIEN, cote * (DEMI + 0.0002)), GAUCHE * cote, 0.0013, 8, mat="Interieur")
        kit.disque(pc, Q(-0.0127, -0.0118, cote * (DEMI + 0.0002)), GAUCHE * cote, 0.0014, 6, mat="Interieur")
        kit.disque(pc, Q(*prise((-0.0170, -0.0640)), cote * (DEMI + 0.0002)), GAUCHE * cote, 0.0012, 8,
                   mat="Interieur")
    a.objet(pc)

    # le levier qui libère le canon, sur le flanc gauche : son bout moleté, sous le pouce, s'abaisse
    _, au_levier = mobile(a, "Levier", Q(*AXE_LEVIER))
    pc = a.piece("Levier_Corps", T["pieces"])
    bout, moletage = (-0.0095, -0.0412), (-0.0010, -0.0418)
    xl = DEMI + 0.0022
    kit.cylindre(pc, Q(*AXE_LEVIER, DEMI - 0.0004), Q(*AXE_LEVIER, DEMI + 0.0016), 0.0038, 12, mat=T["pieces"])
    kit.cylindre(pc, Q(*AXE_LEVIER, xl), Q(*moletage, xl), 0.0011, 6, mat=T["pieces"])
    kit.cylindre(pc, Q(*moletage, xl), Q(*bout, xl), 0.0017, 10, mat="AcierBrut")
    au_levier(a.objet(pc))


def dedans(contour, p):
    """Le point p est-il dans le contour (liste de points 2D) ?"""
    n, oui = len(contour), False
    for i in range(n):
        a, b = contour[i], contour[(i + 1) % n]
        if (a[1] > p[1]) != (b[1] > p[1]) and p[0] < a[0] + (p[1] - a[1]) * (b[0] - a[0]) / (b[1] - a[1]):
            oui = not oui
    return oui


def crosse(a, T):
    """Le caoutchouc de la crosse, quadrillé de pointes de diamant, et sa vis de laiton."""
    pc = a.piece("Crosse", "Caoutchouc")
    plein = [Vector(q(*prise(p))) for p in kit.lisser_chemin(CROSSE, 3, ferme=True)]
    bord = []   # (côté, retrait) du bord, du flanc au tour de la crosse : un quart de rond
    for d in (90, 60, 30, 0):
        bord.append((DEMI_CROSSE - ARRONDI_CROSSE * (1 - math.sin(math.radians(d))),
                     ARRONDI_CROSSE * (1 - math.cos(math.radians(d)))))
    sections = [(-x, r) for x, r in bord] + [(x, r) for x, r in reversed(bord)]
    kit.loft(pc, [[P(p.x, p.y, x) for p in (kit.decaler_contour(plein, -r) if r else plein)] for x, r in sections],
             "Caoutchouc")
    zone = kit.decaler_contour(plein, -0.0058)
    vis = Vector(q(*prise(VIS_CROSSE)))
    pas_av, pas_h = 0.0046, 0.0060
    demi_av, demi_h = pas_av / 2 - 0.0004, pas_h / 2 - 0.0005   # les demi-diagonales d'une pointe, sillon compris
    av0, av1 = min(p.x for p in zone), max(p.x for p in zone)
    h0, h1 = min(p.y for p in zone), max(p.y for p in zone)
    for cote in (1, -1):
        x0, x1 = cote * (DEMI_CROSSE + 0.00002), cote * (DEMI_CROSSE + 0.0008)
        for i in range(int((av1 - av0) / pas_av) + 2):
            for j in range(int((h1 - h0) / pas_h) + 2):
                for decalage in (0.0, 0.5):
                    c = Vector((av0 + (i + decalage) * pas_av, h0 + (j + decalage) * pas_h))
                    coins = [c + Vector((-demi_av, 0)), c + Vector((0, -demi_h)), c + Vector((demi_av, 0)),
                             c + Vector((0, demi_h))]
                    if not all(dedans(zone, p) for p in coins) or (c - vis).length < 0.0055:
                        continue
                    bm = pc.bm
                    base = [bm.verts.new(P(p.x, p.y, x0)) for p in coins]
                    pointe = bm.verts.new(P(c.x, c.y, x1))
                    faces = [bm.faces.new((base[k], base[(k + 1) % 4], pointe)) for k in range(4)]
                    for f in faces:
                        f.material_index = pc.mat("Caoutchouc")
                        f.normal_update()
                    kit.orienter(faces, P(c.x, c.y, x0))
        kit.cylindre(pc, P(vis.x, vis.y, cote * (DEMI_CROSSE - 0.0002)), P(vis.x, vis.y, cote * (DEMI_CROSSE + 0.0007)),
                     0.0027, 12, mat="LaitonCartouche")
        kit.disque(pc, P(vis.x, vis.y, cote * (DEMI_CROSSE + 0.00072)), GAUCHE * cote, 0.0013, 6, mat="Interieur")
    a.objet(pc)


def marquages(a, T):
    pc = a.piece("Marquages", "MarquageNoir")
    a.texte(pc, "N° 0001", 0.0026, Q(-0.0110, -0.0330, -DEMI), ca.VERS_BOUCHE, HAUT, mat="MarquageNoir",
            decalage=0.0003, resolution=1)
    a.objet(pc, angle=None)


def descendants(ob):
    return [d for e in ob.children for d in [e] + descendants(e)]


def agrandir(a, k):
    """Agrandit toute l'arme autour de la prise de la main (l'origine) : pièces, repères, groupe du canon."""
    echelle = Matrix.Scale(k, 4)
    for ob in descendants(a.racine):
        if ob.type == "MESH":
            ob.data.transform(echelle)
        ob.location = ob.location * k


def construire(a):
    T = TEINTES[TEINTE]
    canon(a, T)
    carcasse(a, T)
    crosse(a, T)
    marquages(a, T)
    a.reperes(Q(-0.035, H_VISEE), Q(AV_BOUCHE, D_AXE))
    agrandir(a, ECHELLE)
    # la cartouche sort de la chambre pendant le rechargement, et va dans la main : son occlusion se cuit hors du canon,
    # où elle serait presque noire (exporter_armes.cuire_occlusion)
    a.cuire_a_part = [a.nom("Cartouche")]


def doux(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3.0 - 2.0 * x)


def part(t, etape):
    """Où en est une étape à l'instant t du rechargement : 0 avant, 1 après."""
    debut, fin = ETAPES[etape]
    return min(1.0, max(0.0, (t - debut) / (fin - debut)))


def pose(t):
    """Les pièces mobiles à l'instant t du rechargement (0 à 1), en degrés et en mètres de l'arme (avant ECHELLE) :
    canon (ouvert de tant), chien (abattu de tant depuis l'armé), levier (abaissé de tant) ; douille : ce que la
    douille vide est déjà sortie de la chambre, None une fois partie (chute : les secondes depuis) ; cartouche : ce
    que la cartouche neuve est encore devant la chambre, None tant qu'elle n'y est pas présentée."""
    ejection = part(t, "ejection")
    return dict(
        canon=OUVERTURE_CANON * (doux(part(t, "ouverture")) - doux(part(t, "fermeture"))),
        chien=ARMEMENT_CHIEN * (1.0 - doux(part(t, "armement"))),
        levier=ABAISSEMENT_LEVIER * math.sin(math.pi * part(t, "levier")),
        douille=SORTIE_DOUILLE * ejection * ejection if ejection < 1.0 else None,   # elle glisse de plus en plus vite
        chute=(t - ETAPES["ejection"][1]) * DUREE_RECHARGEMENT if ejection >= 1.0 else None,
        cartouche=APPROCHE_CARTOUCHE * (1.0 - doux(part(t, "insertion"))) if t >= ETAPES["insertion"][0] else None,
    )


def presentation(at, dossier):
    """La cartouche posée au-dessus de l'arme, et une vue de trois quarts comme les photos (rendus de contrôle)."""
    scene = at.scene
    col = kit.collection(f"Présentation — {scene.name}", scene.collection)
    pc = Piece(at.nom("Cartouche_Presentation"))
    cartouche(pc, Q(0.025, 0.026, 0.0), AVANT)
    ob = kit.vers_objet(pc, col, at.M)   # hors de la racine : l'export ne la voit pas
    ob.data.transform(Matrix.Scale(ECHELLE, 4))
    cam = kit.camera("Cam_Presentation", col, Q(0.07, 0.11, 0.36) * ECHELLE, Q(0.022, -0.030, 0.0) * ECHELLE, 50)
    kit.rendre(scene, cam, os.path.join(dossier, "presentation.png"), 1600, 1000)


MIRE = Q(-0.035, H_VISEE) * ECHELLE   # l'œil se place à 40 cm derrière, comme pour les autres pistolets
VUES = [
    ("visee", MIRE + Vector((0, 0.40, 0.0002)), MIRE + Vector((0, -1.0, 0)), 40),
    ("visee_proche", MIRE + Vector((0, 0.40, 0)), MIRE + Vector((0, -1.0, 0)), 400),
    ("detail_charniere", Q(0.040, 0.045, 0.16) * ECHELLE, Q(0.005, -0.020, 0.0) * ECHELLE, 45),
    ("bouche", Q(0.215, 0.020, 0.075) * ECHELLE, Q(0.098, -0.024, 0.0) * ECHELLE, 50),
    ("detail_crosse", Q(-0.010, -0.020, 0.17) * ECHELLE, Q(-0.045, -0.065, 0.0) * ECHELLE, 45),
]

ATELIER = ca.principal("BIG-D", "derringer du Golden Gun", "Big D" if TEINTE == "or" else f"Big D — {TEINTE}",
                       construire, tuple(c * ECHELLE for c in q(AV_BOUCHE, D_AXE)), vues_detail=VUES,
                       args=ARGUMENTS)
if ARGUMENTS.rendus:
    presentation(ATELIER, ARGUMENTS.rendus)
