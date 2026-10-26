"""Ce que partagent les armes de Duel Arena (Blender 5.2).

Chaque arme a son script, qui la construit par principal() :
- les armes à balles : fusil_assaut.py (FA-07), fusil_precision.py (FP-12), fusil_pompe.py
  (FAP-03), pistolet.py (PA-05) ;
- les prototypes laser, des armes à part aux formes futuristes (référence : Apex Legends), une
  par classe : laser_fusil.py (FX-1), laser_precision.py (FX-2), laser_dispersion.py (FX-3),
  laser_pistolet.py (FX-4). Une cellule d'énergie y tient la place du chargeur.
construire_armes.py les réunit toutes dans Armes.blend.

Les deux familles gardent les mêmes points de prise, pour que les animations servent à toutes :
poignée à l'origine, et ce que le seul clip de rechargement du jeu (Y Bot@Reloading) va chercher
de la main gauche, chargeur sous la carcasse puis levier d'armement en haut à gauche au-dessus de
la poignée ; dessous du garde-main sous la main gauche, crosse à l'épaule.

Les armes sont « nues » : les accessoires (viseurs, lasers tactiques, poignées, chargeurs
améliorés, crosses) viendront plus tard. Elles portent ce qu'il faut pour les recevoir : rails
(plaques à encoches sur les lasers), et des points d'accroche (objets vides sous
« <code>_Accessoires »). La crosse, le chargeur et les organes de visée sont des objets à part,
qu'un accessoire remplace ou masque.

Noms : chaque objet est préfixé du code de son arme (« FA07_ », « FX1_ »), pour que toutes tiennent
dans un même fichier ; l'export vers Unity retirera le préfixe.
"""

import argparse
import importlib
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import kit_lowpoly as kit  # noqa: E402

importlib.reload(kit)
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece, srgb  # noqa: E402

POLICE = "C:/Windows/Fonts/bahnschrift.ttf"

# Les scripts des armes, par classe du jeu (dans l'ordre de la panoplie, touches 1 à 4 : fusil
# d'assaut, précision, pompe, pistolet) et par famille (le stock, la série 30, le prototype laser).
# La planche (construire_armes.py) et l'export vers Unity (exporter_armes.py) les lisent ici.
CLASSES = [("fusil_assaut.py", "serie30_fusil.py", "laser_fusil.py"),
           ("fusil_precision.py", "serie30_precision.py", "laser_precision.py"),
           ("fusil_pompe.py", "serie30_pompe.py", "laser_dispersion.py"),
           ("pistolet.py", "serie30_revolver.py", "laser_pistolet.py")]
ROUGE_LASER = srgb(255, 38, 30)   # ni le rouge ni le blanc ne sont des couleurs de robot
ROUGE_LUEUR = srgb(255, 26, 14)   # plus saturé : une lueur forte vire au rose sous AgX


# --- matériaux -------------------------------------------------------------------------

# Les variantes d'usure (proposées le 2026-10-15) : des armes qui servent depuis longtemps et qu'on
# a réparées plusieurs fois. Chacune ajoute ses marques aux matériaux, par famille de matériau ;
# « depareillee » échange aussi la finition de certaines pièces (REMPLACEMENTS_USURE). La clé sert
# à --usure ; sans variante, les matériaux ne changent pas.
VARIANTES_USURE = {
    "repeinte": "Repeinte",
    "rouillee": "Rouillée",
    "depareillee": "Dépareillée",
    "polie": "Polie par les mains",
    "encrassee": "Encrassée",
}
_APPRET = srgb(118, 50, 38)
MARQUES_USURE = {
    # repeinte au pinceau, couche après couche, sans décaper : l'apprêt rouge borde les éclats, des
    # retouches d'une autre teinte les recouvrent (de l'anthracite sur le blanc, faute de blanc)
    "repeinte": {
        "peinture": dict(couche=(_APPRET, 0.78), retouches=0.55, teinte_retouches=srgb(42, 45, 49), eclats=0.85,
                         bords=0.9),
        "claire": dict(couche=(_APPRET, 0.78), retouches=0.5, teinte_retouches=srgb(60, 63, 67), eclats=0.7),
        "ceramique": dict(retouches=0.4, teinte_retouches=srgb(212, 208, 198)),
        "polymere": dict(retouches=0.3, teinte_retouches=srgb(46, 48, 50)),
    },
    # sortie rouillée de ses caisses et jamais décapée : la rouille mange les éclats, remplit les
    # creux et coule ; la peinture est passée. Une rouille brune et sombre : l'orange est une
    # couleur de robot
    "rouillee": {
        "peinture": dict(rouille=0.85, teinte_rouille=srgb(62, 42, 31), delavage=0.55, eclats=0.85, bords=0.9,
                         salete=0.8),
        "claire": dict(rouille=0.75, teinte_rouille=srgb(66, 44, 32), delavage=0.45, eclats=0.75, bords=0.85),
        "primaire": dict(rouille=0.7, teinte_rouille=srgb(58, 40, 30)),
        "acier": dict(rouille=0.7, teinte_rouille=srgb(58, 40, 30)),
        "ceramique": dict(rouille=0.5, teinte_rouille=srgb(62, 43, 32), delavage=0.3),
    },
    # chaque réparation a pris la pièce d'une autre arme (voir REMPLACEMENTS_USURE)
    "depareillee": {
        "peinture": dict(eclats=0.6, bords=0.8),
    },
    # des milliers de duels : sous les mains, la peinture est partie et le métal est poli
    "polie": {
        "peinture": dict(mains=True, delavage=0.35, bords=0.95, eclats=0.65),
        "claire": dict(mains=True, delavage=0.3, bords=0.9, eclats=0.6),
        "ceramique": dict(mains=True, delavage=0.25),
        "polymere": dict(mains=True, bords=0.75, eclats=0.4, rayures=0.6),
        "acier": dict(mains=True),
    },
    # jamais nettoyée : huile, poussière, crasse et suie
    "encrassee": {
        "peinture": dict(huile=0.95, poussiere=1.0, salete=1.0, variation=0.22),
        "claire": dict(huile=0.9, poussiere=0.9, salete=1.0, variation=0.18),
        "ceramique": dict(huile=0.75, poussiere=0.9, salete=0.95, variation=0.15),
        "polymere": dict(huile=0.85, poussiere=1.0, salete=0.9, variation=0.2),
        "acier": dict(huile=0.9, poussiere=0.7, salete=0.9),
        "laiton": dict(huile=0.4, salete=0.9),
        "souple": dict(poussiere=0.6, salete=0.8),
    },
}
# (la pièce dont le nom contient…) : {son matériau d'origine : celui de la pièce rapportée}
REMPLACEMENTS_USURE = {
    "depareillee": {
        "Garde_Main": {"Peinture": "PeintureClaire", "PeintureClaire": "Peinture"},
        "Capot": {"Peinture": "PeintureClaire", "Ceramique": "Peinture"},
        "Crosse_Corps": {"Peinture": "Primaire", "Polymere": "Primaire", "PeintureClaire": "Primaire"},
        "Chargeur_Standard": {"Peinture": "AcierBrut", "Polymere": "AcierBrut", "PeintureClaire": "AcierBrut"},
        "Carcasse_Basse": {"Peinture": "PeintureGrise", "PeintureClaire": "PeintureGrise"},
    },
}


def materiaux(code, bouche, variante=None, mains=None, fabrique=None):
    """bouche = (av, h) du bout du canon, où se concentre la suie. Le matériau qui la porte
    est propre à l'arme : plusieurs armes peuvent cohabiter dans un même Blender. variante : une
    des VARIANTES_USURE (ses matériaux portent son nom, pour cohabiter avec ceux des autres) ;
    mains : [(point, rayon)] là où les mains frottent, pour la variante « polie » ; fabrique : ce
    qui reçoit (nom, base, paramètres) de chaque matériau, kit.materiau par défaut (l'export vers
    Unity y lit les paramètres, sans créer de matériau)."""
    fabrique = fabrique or kit.materiau
    acier_nu = srgb(158, 160, 163)
    peinture = srgb(55, 59, 64)
    primaire = srgb(118, 50, 38)
    av, h = bouche
    marques = MARQUES_USURE.get(variante, {})
    suffixe = f"_{variante}" if variante else ""

    def mat(nom, base, famille=None, **kw):
        if famille in marques:
            ajout = dict(marques[famille])
            if ajout.pop("mains", False) and mains:
                ajout["zones"] = [(point, rayon, 1.0) for point, rayon in mains]
            kw.update(ajout)
        return fabrique(nom + suffixe, base, **kw)

    M = {}
    M["Peinture"] = mat("DA_Peinture", peinture, "peinture", rug=0.58, dessous=acier_nu, rug_dessous=0.4,
                                 metal_dessous=0.6, bords=0.75, eclats=0.5, rayures=0.6, salete=0.6, graine=1)
    M["Polymere"] = mat("DA_Polymère", srgb(31, 32, 34), "polymere", rug=0.72, dessous=srgb(80, 81, 83),
                                 rug_dessous=0.85, metal_dessous=0.0, bords=0.55, rayures=0.35, salete=0.5, graine=2)
    M["Primaire"] = mat("DA_Apprêt_Rouge", primaire, "primaire", rug=0.78, dessous=acier_nu, rug_dessous=0.4,
                                 metal_dessous=0.6, bords=0.9, eclats=0.8, rayures=0.45, salete=0.7, graine=3)
    M["Acier"] = mat("DA_Acier", srgb(62, 64, 68), "acier", rug=0.42, metal=1.0, dessous=srgb(150, 152, 156),
                              rug_dessous=0.25, bords=0.7, rayures=0.3, salete=0.6, graine=4)
    M["Bouche"] = mat(f"DA_Acier_Bouche_{code}", srgb(62, 64, 68), "acier", rug=0.42, metal=1.0,
                               dessous=srgb(150, 152, 156), rug_dessous=0.25, bords=0.7, rayures=0.3,
                               salete=0.6, graine=4, suie=(P(av, h), 0.11 * (1.7 if variante == "encrassee" else 1.0)))
    M["AcierBrut"] = mat("DA_Acier_Brut", srgb(128, 128, 126), "acier", rug=0.5, metal=0.75,
                                  dessous=srgb(175, 176, 178), rug_dessous=0.3, bords=0.6, rayures=0.8,
                                  salete=0.8, graine=5)
    M["Soudure"] = mat("DA_Soudure", srgb(92, 88, 84), rug=0.62, metal=1.0, salete=0.9,
                                variation=0.2, graine=14)
    M["Caoutchouc"] = mat("DA_Caoutchouc", srgb(24, 24, 24), "souple", rug=0.9, dessous=srgb(50, 50, 50),
                                   rug_dessous=0.95, metal_dessous=0.0, bords=0.5, salete=0.4, graine=6)
    M["Ruban"] = mat("DA_Ruban_Adhésif", srgb(176, 172, 160), "souple", rug=0.55, salete=0.9, variation=0.12, graine=7)
    M["RubanNoir"] = mat("DA_Ruban_Toilé", srgb(30, 30, 30), "souple", rug=0.95, salete=0.4, variation=0.1, graine=8)
    M["Marquage"] = mat("DA_Marquage_Blanc", srgb(206, 201, 188), rug=0.65, dessous=peinture,
                                 rug_dessous=0.58, metal_dessous=0.0, bords=0.0, eclats=0.6, rayures=0.5,
                                 salete=0.3, graine=9, echelle_eclats=150.0)
    M["MarquageNoir"] = mat("DA_Marquage_Noir", srgb(22, 22, 22), rug=0.7, dessous=primaire,
                                     rug_dessous=0.78, metal_dessous=0.0, bords=0.0, eclats=0.6, salete=0.3,
                                     graine=10, echelle_eclats=150.0)
    M["Rayure"] = mat("DA_Gravure", srgb(172, 174, 178), rug=0.3, metal=1.0, salete=0.2, variation=0.0)
    M["Interieur"] = mat("DA_Intérieur", srgb(14, 14, 15), rug=0.8, metal=0.3, salete=0.0, variation=0.0)
    M["EcranMort"] = mat("DA_Écran_Mort", srgb(12, 12, 12), rug=0.12, salete=0.3, variation=0.0, coat=1.0)
    M["Lentille"] = mat("DA_Lentille", srgb(6, 9, 16), rug=0.02, salete=0.0, variation=0.0, coat=1.0)
    M["Nylon"] = mat("DA_Nylon", srgb(22, 22, 22), "souple", rug=0.45, salete=0.2, variation=0.0)
    M["Fil"] = mat("DA_Fil_De_Fer", srgb(90, 90, 92), "acier", rug=0.5, metal=1.0, salete=0.5, graine=13)
    # pièces des prototypes laser
    M["Laser"] = mat("DA_Lentille_Laser", srgb(90, 12, 10), rug=0.05, salete=0.0, variation=0.0,
                              emission=ROUGE_LUEUR, force_emission=3.0, coat=1.0)
    M["Lueur"] = mat("DA_Lueur", srgb(30, 6, 4), rug=0.4, salete=0.0, variation=0.0,
                              emission=ROUGE_LUEUR, force_emission=1.3)
    M["Ceramique"] = mat("DA_Céramique", srgb(196, 192, 182), "ceramique", rug=0.72, dessous=srgb(64, 64, 66),
                                  rug_dessous=0.6, metal_dessous=0.0, bords=0.55, eclats=0.35, rayures=0.4,
                                  salete=0.6, graine=15)
    M["Bobine"] = mat("DA_Bobine", srgb(84, 78, 70), "laiton", rug=0.35, metal=1.0, dessous=srgb(150, 140, 128),
                               rug_dessous=0.25, bords=0.6, salete=0.6, graine=16)
    # série 30 : une peinture blanc cassé sur métal, qui s'écaille jusqu'à l'acier
    M["PeintureClaire"] = mat("DA_Peinture_Claire", srgb(201, 198, 189), "claire", rug=0.6, dessous=acier_nu,
                                       rug_dessous=0.4, metal_dessous=0.6, bords=0.7, eclats=0.45, rayures=0.55,
                                       salete=0.55, graine=17)
    # … et son accent, un laiton terni (ailettes, bagues, chien, détente)
    M["Laiton"] = mat("DA_Laiton", srgb(122, 106, 80), "laiton", rug=0.4, metal=0.7, dessous=srgb(170, 152, 116),
                               rug_dessous=0.3, bords=0.6, salete=0.6, graine=18)
    if variante == "depareillee":   # une carcasse d'un autre lot, d'un gris plus clair
        M["PeintureGrise"] = mat("DA_Peinture_Grise", srgb(96, 99, 102), "peinture", rug=0.58, dessous=acier_nu,
                                 rug_dessous=0.4, metal_dessous=0.6, bords=0.75, eclats=0.5, rayures=0.6, salete=0.6,
                                 graine=19)
    return M


# --- atelier : une version d'une arme -------------------------------------------------------

class Atelier:
    """Construit une version d'une arme : sa collection, sa racine, ses pièces, ses repères."""

    def __init__(self, scene, code, titre, laser, M, remplacements=None):
        self.scene = scene
        self.remplacements = remplacements or {}   # variante « depareillee » : voir REMPLACEMENTS_USURE
        self.code = code
        self.titre = titre
        self.prefixe = code.replace("-", "")
        self.laser = laser
        self.M = M
        self.rng = random.Random(sum(map(ord, code)))
        self.col = kit.collection(f"{code} — {titre}", scene.collection)
        self.racine = self.vide(self.prefixe, None, "ARROWS", 0.06)
        self.objets = []
        self.police = bpy.data.fonts.load(POLICE, check_existing=True)

    def nom(self, x):
        return f"{self.prefixe}_{x}"

    def piece(self, x, mat=None):
        return Piece(self.nom(x), mat)

    def objet(self, pc, angle=35.0):
        M = self.M
        for motif, regle in self.remplacements.items():
            if motif in pc.nom:
                M = {**M, **{k: self.M[v] for k, v in regle.items()}}
        ob = kit.vers_objet(pc, self.col, M, self.racine, angle_lisse=angle)
        self.objets.append(ob)
        return ob

    def couper(self, ob, coupeur, auto=False):
        return kit.couper(ob, coupeur, self.scene, self.M, auto_intersection=auto)

    def vide(self, nom, parent, forme="PLAIN_AXES", taille=0.015, position=None):
        e = bpy.data.objects.new(nom, None)
        e.empty_display_type = forme
        e.empty_display_size = taille
        if position is not None:
            e.location = position
        e.parent = parent
        self.col.objects.link(e)
        return e

    def reperes(self, mire, bouche, main_gauche=None, main_droite=None):
        """Mire : là où se place l'œil en visée (WeaponModel.sight) ; Bouche : bout du canon ;
        MainGauche : sous le garde-main, là où la paume gauche tient l'arme, cible de l'IK de la main
        gauche à l'intégration (les animations la placent pour un garde-main profond ; absent d'un
        pistolet, tenu à deux mains sur la poignée) ; MainDroite : seulement quand la main droite ne
        tient pas une poignée pistolet à l'origine (une crosse à poignet droit), cible de l'IK de la
        main droite, l'arme restant placée par l'origine."""
        self.vide(self.nom("Mire"), self.racine, position=mire)
        self.vide(self.nom("Bouche"), self.racine, position=bouche)
        if main_gauche is not None:
            self.vide(self.nom("MainGauche"), self.racine, position=main_gauche)
        if main_droite is not None:
            self.vide(self.nom("MainDroite"), self.racine, position=main_droite)

    def accroches(self, points):
        """Points d'accroche des accessoires (axes de l'arme : +Y Unity vers le haut, +Z vers l'avant)."""
        groupe = self.vide(self.nom("Accessoires"), self.racine, "CUBE", 0.004)
        for nom, position in points.items():
            self.vide(self.nom(nom), groupe, "PLAIN_AXES", 0.02, position)

    def texte(self, pc, contenu, hauteur, centre, axe_x, axe_y, mat="Marquage", **kw):
        return kit.texte(pc, contenu, self.police, hauteur, centre, axe_x, axe_y, mat, scene=self.scene, **kw)

    def placer(self, decalage):
        self.racine.location = decalage


VERS_CROSSE = Vector((0, 1, 0))   # sens de lecture d'un texte vu du flanc gauche
VERS_BOUCHE = Vector((0, -1, 0))  # … vu du flanc droit


# --- éléments communs ------------------------------------------------------------------------

REPERES_FACE = {   # (le long du rail, en travers, normale)
    "haut": (AVANT, GAUCHE, HAUT),
    "bas": (AVANT, GAUCHE, -HAUT),
    "gauche": (AVANT, HAUT, GAUCHE),
    "droite": (AVANT, HAUT, -GAUCHE),
}


def rail(pc, av0, av1, origine, face="haut", largeur=0.021, epaisseur=0.0065, dent=0.0055, pas=0.010,
         longueur_dent=0.0052, mat="Peinture", zones=None):
    """Rail à dents (type Picatinny) posé sur une surface : origine = milieu de la base, à av0.
    zones = [(av_a, av_b), ...] où mettre des dents (par défaut : toute la longueur)."""
    le_long, travers, n = REPERES_FACE[face]
    longueur = av1 - av0
    f = kit.boite_axes(pc, origine + le_long * (longueur / 2) + n * (epaisseur / 2), le_long, travers, n,
                       longueur / 2, largeur / 2, epaisseur / 2, mat)
    kit.chanfreiner(pc, 0.0010, f)
    for a0, a1 in zones or [(av0 + 0.004, av1 - 0.004)]:
        a = a0
        while a + longueur_dent <= a1 + 1e-6:
            centre = origine + le_long * (a - av0 + longueur_dent / 2) + n * (epaisseur + dent / 2)
            kit.boite_axes(pc, centre, le_long, travers, n, longueur_dent / 2, largeur * 0.47, dent / 2, mat)
            a += pas


def hausse(a, av, h_rail, h_visee, r_int=0.0042, r_ext=0.0063, nom="Hausse", mat="Peinture", cotes=16):
    """Hausse à anneau (œilleton ouvert) sur le rail du dessus. Un jeu n'a pas de profondeur de
    champ : une tour percée d'un trou cacherait l'écran en visée. Un anneau fin sur une tige laisse
    la vue dégagée, et le guidon centré dans l'anneau reste précis. Objet à part : masqué quand un
    viseur prend sa place."""
    pc = a.piece(nom, mat)
    hb = h_rail
    f = kit.boite(pc, av - 0.012, av + 0.012, hb - 0.0015, hb + 0.0055, -0.0125, 0.0125, mat)
    kit.chanfreiner(pc, 0.001, f)
    bas_anneau = h_visee - r_ext + 0.0012
    f = kit.prisme(pc, [(av - 0.0045, hb + 0.005), (av + 0.0045, hb + 0.005), (av + 0.0022, bas_anneau),
                        (av - 0.0022, bas_anneau)], -0.0028, 0.0028, mat)
    kit.chanfreiner(pc, 0.0006, f)
    kit.anneau(pc, av - 0.0016, av + 0.0016, h_visee, r_ext, r_int, cotes, mat,
               phase=math.pi / 2 if cotes == 6 else None)   # un hexagone posé sur une pointe
    for x0, x1 in ((0.0088, 0.0108), (-0.0108, -0.0088)):   # oreilles, à mi-hauteur de l'anneau
        f = kit.prisme(pc, [(av - 0.008, hb + 0.005), (av + 0.008, hb + 0.005), (av + 0.0035, h_visee + 0.001),
                            (av - 0.0035, h_visee + 0.001)], x0, x1, mat)
        kit.chanfreiner(pc, 0.0006, f)
    kit.cylindre(pc, P(av, hb + 0.0025, -0.0125), P(av, hb + 0.0025, -0.0165), 0.0034, 8, mat="Acier")  # molette
    return a.objet(pc)


def guidon(a, av, h_rail, h_visee, nom="Guidon", mat="Peinture", demi_base=0.0125, mat_pointe="Marquage"):
    """Guidon à lame fine entre deux oreilles, pointe claire côté tireur (on la voit dans l'anneau).
    demi_base : demi-largeur de l'embase (0,0125 sur un rail ; moins sur une bande de pompe)."""
    pc = a.piece(nom, mat)
    hb = h_rail
    f = kit.boite(pc, av - 0.010, av + 0.010, hb - 0.0015, hb + 0.0055, -demi_base, demi_base, mat)
    kit.chanfreiner(pc, 0.001, f)
    o0, o1 = demi_base * 0.44, demi_base * 0.72
    for x0, x1 in ((o0, o1), (-o1, -o0)):
        f = kit.prisme(pc, [(av - 0.008, hb + 0.004), (av + 0.008, hb + 0.004), (av + 0.003, h_visee + 0.0045),
                            (av - 0.003, h_visee + 0.0045)], x0, x1, mat)
        kit.chanfreiner(pc, 0.0008, f)
    f = kit.prisme(pc, [(av - 0.006, hb + 0.004), (av + 0.006, hb + 0.004), (av + 0.0025, hb + 0.010),
                        (av - 0.0025, hb + 0.010)], -0.0026, 0.0026, mat)
    kit.chanfreiner(pc, 0.0006, f)
    kit.prisme(pc, [(av - 0.0016, hb + 0.009), (av + 0.0016, hb + 0.009), (av + 0.0010, h_visee),
                    (av - 0.0010, h_visee)], -0.0009, 0.0009, mat)
    kit.quad(pc, [P(av - 0.00112, h_visee - 0.0024, -0.0008), P(av - 0.00112, h_visee - 0.0024, 0.0008),
                  P(av - 0.00112, h_visee - 0.0003, 0.0008), P(av - 0.00112, h_visee - 0.0003, -0.0008)],
             mat_pointe, normale=Vector((0, 1, 0)))
    return a.objet(pc)


def bezier(p0, c, p2, s):
    return tuple((1 - s) ** 2 * u + 2 * (1 - s) * s * v + s * s * w for u, v, w in zip(p0, c, p2))


def courbes_chargeur(haut_dos, c_dos, bas_dos, haut_face, c_face, bas_face, n=6):
    pas = [i / (n - 1) for i in range(n)]
    return ([bezier(haut_dos, c_dos, bas_dos, s) for s in pas], [bezier(haut_face, c_face, bas_face, s) for s in pas])


def semelle(pc, dos, face, demi, mat, ep_bas=0.0075, debord=0.003, ep_haut=0.004):
    bd, bf = Vector(dos[-1]), Vector(face[-1])
    d = (bf - bd).normalized()
    haut = Vector((-d.y, d.x))
    coins = [bd - d * debord + haut * ep_haut, bf + d * debord + haut * ep_haut,
             bf + d * debord - haut * ep_bas, bd - d * debord - haut * ep_bas]
    f = kit.prisme(pc, [tuple(c) for c in coins], -demi, demi, mat)
    kit.chanfreiner(pc, 0.0022, f)


def chargeur(a, dos, face, demi, nom="Chargeur_Standard", mat="Polymere", mat_semelle=None, bande=True):
    """Chargeur droit ou courbe, défini par ses deux bords (dos, face) de haut en bas."""
    pc = a.piece(nom, mat)
    f = kit.prisme(pc, face + list(reversed(dos)), -demi, demi, mat)
    kit.chanfreiner(pc, 0.0022, f)
    semelle(pc, dos, face, demi + 0.0023, mat_semelle or mat)
    if bande:   # bande d'essai, des deux côtés
        i = max(1, len(dos) - 3)
        pa = [Vector(dos[i]) + (Vector(dos[i + 1]) - Vector(dos[i])) * k for k in (0.55, 0.85)]
        pb = [Vector(face[i]) + (Vector(face[i + 1]) - Vector(face[i])) * k for k in (0.55, 0.85)]
        for x, n in ((demi + 0.0001, GAUCHE), (-demi - 0.0001, -GAUCHE)):
            kit.quad(pc, [P(pa[0].x, pa[0].y, x), P(pb[0].x, pb[0].y, x), P(pb[1].x, pb[1].y, x),
                          P(pa[1].x, pa[1].y, x)], "Marquage", normale=n)
    return a.objet(pc)


def cellule(a, dos, face, demi, nom="Chargeur_Standard", segments=4, allumes=3):
    """Cellule d'énergie des prototypes : la silhouette du chargeur (même puits, même geste),
    un carter métallique, une fenêtre de charge lumineuse et des contacts dans le culot."""
    pc = a.piece(nom, "Peinture")
    f = kit.prisme(pc, face + list(reversed(dos)), -demi, demi, "Peinture")
    kit.chanfreiner(pc, 0.0022, f)
    semelle(pc, dos, face, demi + 0.0023, "Acier", ep_bas=0.009)
    # fenêtre de charge (gauche, côté joueur) : des barres allumées, une éteinte
    n = len(dos)
    for k in range(segments):
        s0 = 0.25 + 0.6 * k / segments
        s1 = s0 + 0.6 / segments * 0.78
        def le_long(courbe, s):
            t = s * (n - 1)
            i = min(int(t), n - 2)
            u = t - i
            return Vector(courbe[i]) + (Vector(courbe[i + 1]) - Vector(courbe[i])) * u
        coins = []
        for s, w in ((s0, 0.36), (s0, 0.64), (s1, 0.64), (s1, 0.36)):
            p = le_long(dos, s).lerp(le_long(face, s), w)
            coins.append(P(p.x, p.y, demi + 0.0002))
        kit.quad(pc, coins, "Lueur" if k >= segments - allumes else "EcranMort", normale=GAUCHE)
    # nervures de part et d'autre de la fenêtre
    for w in (0.26, 0.74):
        pts = [Vector(d).lerp(Vector(fc), w) for d, fc in zip(dos, face)]
        for x0, x1 in ((demi - 0.0004, demi + 0.0012), (-demi - 0.0012, -demi + 0.0004)):
            for p0, p1 in zip(pts[1:-2], pts[2:-1]):
                kit.prisme(pc, [(p0.x - 0.0016, p0.y), (p0.x + 0.0016, p0.y), (p1.x + 0.0016, p1.y), (p1.x - 0.0016, p1.y)],
                           x0, x1, "Peinture")
    return a.objet(pc)


def pontet(pc, points, mat="Peinture"):
    f = kit.balayage(pc, [P(av, h) for av, h in points], kit.section_rect(0.0105, 0.0055), mat, normale=GAUCHE)
    kit.chanfreiner(pc, 0.0008, f)


def detente(pc, av, h_haut, mat="Acier", echelle=1.0):
    """Queue de détente courbe, son haut caché dans la carcasse (av = son bord avant en haut)."""
    profil = [(0.000, 0.000), (0.004, -0.012), (0.002, -0.024), (-0.004, -0.034), (-0.010, -0.038),
              (-0.008, -0.033), (-0.004, -0.024), (-0.003, -0.012), (-0.006, 0.000)]
    kit.prisme(pc, [(av + u * echelle, h_haut + v * echelle) for u, v in profil], -0.003, 0.003, mat)


def poignee_pistolet(a, T=(0.016, 0.046), inclinaison=20.0, longueur=0.14, ruban=True, nom="Poignee"):
    """Poignée des armes d'épaule de la famille (fusil, précision, pompe) : même main, même prise.
    T = haut de l'axe de la poignée ; l'origine de l'arme (la prise de la main) est sur cet axe."""
    rake = math.radians(inclinaison)
    u = (-math.sin(rake), -math.cos(rake))
    n = (math.cos(rake), -math.sin(rake))

    def pt(t, d):
        return (T[0] + u[0] * t + n[0] * d, T[1] + u[1] * t + n[1] * d)

    k = longueur / 0.14
    avant = [(0.0, 0.021), (0.03 * k, 0.0215), (0.058 * k, 0.0185), (0.09 * k, 0.0215), (0.122 * k, 0.0235),
             (longueur, 0.0245)]
    arriere = [(longueur, -0.022), (0.115 * k, -0.0245), (0.07 * k, -0.0265), (0.03 * k, -0.0245), (0.0, -0.024)]
    pc = a.piece(nom, "Polymere")
    f = kit.prisme(pc, [pt(t, d) for t, d in avant] + [pt(t, d) for t, d in arriere], -0.0165, 0.0165)
    kit.chanfreiner(pc, 0.0032, f)
    objets = [a.objet(pc)]
    if ruban:
        def profondeur(liste, t):
            liste = sorted(liste)
            for (t0, d0), (t1, d1) in zip(liste, liste[1:]):
                if t0 <= t <= t1:
                    return d0 + (d1 - d0) * (t - t0) / (t1 - t0)
            return liste[-1][1]

        pc = a.piece("Ruban_Poignee", "RubanNoir")
        ex, ey = Vector((1, 0, 0)), Vector((0, -n[0], n[1]))
        axe = Vector((0, -u[0], u[1]))
        for t, incl in ((0.046 * k, 0.06), (0.077 * k, -0.05), (0.108 * k, 0.08)):
            df, db = profondeur(avant, t), -profondeur(arriere, t)
            contour = kit.contour_rect(0.033, df + db, chanfrein=0.004, cy=(df - db) / 2)
            kit.bande(pc, contour, P(*pt(t, 0.0)), ex, ey, axe, 0.016, 0.0013, "RubanNoir", inclinaison=incl,
                      irregularite=0.0006, rng=a.rng)
        objets.append(a.objet(pc))
    return objets


# --- armes laser : formes futuristes -------------------------------------------------------------

def section8(demi, bas, haut, ch_haut=0.008, ch_bas=0.006, quille=1.0):
    """Section à huit points (x, h) d'une carcasse ou d'un capot, toujours dans le même ordre, pour
    que deux sections se relient par un loft. Côtés (indices pour loft(mats_cotes=...)) :
    0 chanfrein haut gauche, 1 dessus, 2 chanfrein haut droit, 3 flanc droit, 4 chanfrein bas droit,
    5 dessous, 6 chanfrein bas gauche, 7 flanc gauche. quille < 1 : le dessous plus étroit (une carène)."""
    db = demi * quille
    return [(demi, haut - ch_haut), (demi - ch_haut, haut), (-(demi - ch_haut), haut), (-demi, haut - ch_haut),
            (-demi, bas + ch_bas), (-db, bas), (db, bas), (demi, bas + ch_bas)]


def mats8(dessus=None, chanfrein_haut=None, flancs=None, chanfrein_bas=None, dessous=None):
    """Matériaux des huit côtés d'une section8, par zones."""
    return [chanfrein_haut, dessus, chanfrein_haut, flancs, chanfrein_bas, dessous, chanfrein_bas, flancs]


def poignee_futuriste(a, T=(0.016, 0.046), inclinaison=19.0, longueur=0.135, nom="Poignee"):
    """Poignée des armes laser : même main, même prise que celle des armes à balles (même axe, même
    origine), mais sculptée : appui pour le doigt, renflement de paume, talon évasé, flancs en céramique."""
    rake = math.radians(inclinaison)
    u = (-math.sin(rake), -math.cos(rake))
    n = (math.cos(rake), -math.sin(rake))

    def pt(t, d):
        return (T[0] + u[0] * t + n[0] * d, T[1] + u[1] * t + n[1] * d)

    k = longueur / 0.135
    avant = [(0.0, 0.020), (0.016 * k, 0.0235), (0.030 * k, 0.0195), (0.068 * k, 0.0215), (0.104 * k, 0.0230),
             (longueur, 0.0275)]
    arriere = [(longueur, -0.0265), (0.118 * k, -0.0245), (0.064 * k, -0.0290), (0.022 * k, -0.0262), (0.0, -0.024)]
    pc = a.piece(nom, "Polymere")
    f = kit.prisme(pc, [pt(t, d) for t, d in avant] + [pt(t, d) for t, d in arriere], -0.0165, 0.0165)
    kit.chanfreiner(pc, 0.0035, f)
    flanc = [pt(0.034 * k, 0.0135), pt(0.070 * k, 0.0150), pt(0.104 * k, 0.0160), pt(0.118 * k, 0.0100),
             pt(0.118 * k, -0.0185), pt(0.064 * k, -0.0215), pt(0.034 * k, -0.0185)]
    for x0, x1 in ((0.0158, 0.0174), (-0.0174, -0.0158)):
        f = kit.prisme(pc, flanc, x0, x1, "Ceramique")
        kit.chanfreiner(pc, 0.0007, f)
    return [a.objet(pc)]


def coeur(a, av0, av1, h, r=0.0058, bobines=4, r_bobine=0.0125, nom="Coeur", x=0.0, n=10):
    """Le cœur d'énergie qu'on voit par la fenêtre d'un capot : une tige qui luit, cerclée de bobines."""
    pc = a.piece(nom, "Lueur")
    kit.cylindre(pc, P(av0, h, x), P(av1, h, x), r, 8, mat="Lueur")
    pas = (av1 - av0) / (bobines + 1)
    for i in range(bobines):
        c = av0 + pas * (i + 1)
        f = kit.cylindre(pc, P(c - 0.004, h, x), P(c + 0.004, h, x), r_bobine, n, mat="Bobine")
        kit.chanfreiner(pc, 0.0006, f)
    return a.objet(pc)


def plaque_montage(pc, av0, av1, origine, face="haut", largeur=0.020, ep=0.0025, encoches=6, mat="Peinture"):
    """Interface d'accessoire des armes laser, à la place d'un rail à dents : une plaque à encoches."""
    le_long, travers, n = REPERES_FACE[face]
    longueur = av1 - av0
    f = kit.boite_axes(pc, origine + le_long * (longueur / 2) + n * (ep / 2), le_long, travers, n,
                       longueur / 2, largeur / 2, ep / 2, mat)
    kit.chanfreiner(pc, 0.0006, f)
    pas = longueur / encoches
    for i in range(encoches):
        c = origine + le_long * (pas * (i + 0.5)) + n * (ep + 0.0002)
        dl, dt = pas * 0.28, largeur * 0.32
        kit.quad(pc, [c - le_long * dl - travers * dt, c + le_long * dl - travers * dt,
                      c + le_long * dl + travers * dt, c - le_long * dl + travers * dt], "Interieur", normale=n)


def lueurs_dessus(pc, fentes, h_fond, demi):
    """Le fond rougeoyant de fentes découpées dans un dessus : fentes = [(av0, av1), ...]."""
    for av0, av1 in fentes:
        kit.quad(pc, [P(av0 + 0.0004, h_fond, -demi), P(av1 - 0.0004, h_fond, -demi),
                      P(av1 - 0.0004, h_fond, demi), P(av0 + 0.0004, h_fond, demi)], "Lueur", normale=HAUT)


# --- pièces laser ------------------------------------------------------------------------------

def emetteur(pc, av0, av1, h, r, n=10, ailettes=4, mat_corps="Bouche", mat_ailette="Peinture", lentille=True,
             r_ailette=1.45):
    """Émetteur d'un prototype, à la place du canon : corps, ailettes de refroidissement,
    couronne et lentille rouge (là où sort le rayon, au même point que la bouche du classique)."""
    f = kit.tranches(pc, P(av0, h), P(av1, h), [r * 0.82, r, r, r * 0.9], n=n, mat=mat_corps,
                     positions=[0.0, 0.05, 0.93, 1.0])
    kit.chanfreiner(pc, 0.0008, f)
    if ailettes:
        pas = (av1 - av0) * 0.55 / max(1, ailettes - 1)
        for i in range(ailettes):
            a = av0 + (av1 - av0) * 0.18 + pas * i
            f = kit.cylindre(pc, P(a, h), P(a + 0.0032, h), r * r_ailette, n, mat=mat_ailette)
            kit.chanfreiner(pc, 0.0006, f)
    if lentille:
        kit.anneau(pc, av1 - 0.002, av1 + 0.007, h, r * 1.02, r * 0.66, n, mat_corps)
        kit.disque(pc, P(av1 + 0.001, h), AVANT, r * 0.66, n, mat="Laser")


def autocollant_laser(pc, centre, normale, droite, taille):
    """Pictogramme « danger laser » (triangle, soleil), en noir et blanc cassé : presque pas de jaune."""
    normale = normale.normalized()
    haut = normale.cross(droite).normalized()
    droite = haut.cross(normale).normalized()

    def tri(r, ecart):
        pts = [centre + (haut * math.cos(a) + droite * math.sin(a)) * r + normale * ecart
               for a in (0.0, 2 * math.pi / 3, 4 * math.pi / 3)]
        return pts

    kit.quad(pc, tri(taille, 0.0003), "MarquageNoir", normale=normale)
    kit.quad(pc, tri(taille * 0.72, 0.0005), "Marquage", normale=normale)
    c = centre - haut * (taille * 0.08) + normale * 0.0007
    kit.disque(pc, c, normale, taille * 0.11, 8, mat="MarquageNoir")
    for i in range(8):
        ang = 2 * math.pi * i / 8
        d = haut * math.cos(ang) + droite * math.sin(ang)
        t = d.cross(normale).normalized() * (taille * 0.025)
        p0, p1 = c + d * (taille * 0.15), c + d * (taille * 0.30)
        kit.quad(pc, [p0 - t, p0 + t, p1 + t, p1 - t], "MarquageNoir", normale=normale)


def bande_hachuree(pc, coins, normale, n=6):
    """Bande d'essai à hachures noires et blanc cassé, sur un quadrilatère plat (coins dans l'ordre)."""
    a, b, c, d = coins
    for i in range(n):
        u0, u1 = i / n, (i + 1) / n
        s = 0.5 / n
        q = [a.lerp(b, u0), a.lerp(b, u1), d.lerp(c, min(1.0, u1 + s)), d.lerp(c, min(1.0, u0 + s))]
        kit.quad(pc, q, "MarquageNoir" if i % 2 else "Marquage", normale=normale)


def compteur(pc, av0, av1, h0, h1, x, vivant, chiffres="30"):
    """Compteur de munitions sur le flanc gauche : mort et barré de ruban (celui de l'ancien FA-07), ou
    allumé (afficheur à sept segments). Les armes laser ont le leur à l'arrière : compteur_arriere."""
    f = kit.boite(pc, av0, av1, h0, h1, x - 0.0004, x + 0.0036, "Polymere")
    kit.chanfreiner(pc, 0.0015, f)
    e = 0.004
    xs = x + 0.0037
    kit.quad(pc, [P(av0 + e, h0 + e, xs), P(av1 - e, h0 + e, xs), P(av1 - e, h1 - e, xs), P(av0 + e, h1 - e, xs)],
             "EcranMort", normale=GAUCHE)
    if not vivant:
        centre = ((av0 + av1) / 2, (h0 + h1) / 2)
        demi_l, demi_h = (av1 - av0) / 2, (h1 - h0) / 2
        angle = math.atan2(demi_h, demi_l)
        longueur = math.hypot(demi_l, demi_h) * 1.9
        for k, ang in enumerate((angle, -angle)):
            c, s = math.cos(ang), math.sin(ang)
            coins = [(centre[0] + c * u - s * v, centre[1] + s * u + c * v) for u, v in
                     ((-longueur / 2, -0.0055), (longueur / 2, -0.0055), (longueur / 2, 0.0055), (-longueur / 2, 0.0055))]
            kit.prisme(pc, coins, xs + 0.0002 + 0.0005 * k, xs + 0.0006 + 0.0005 * k, "Ruban")
        return
    # sept segments, lus depuis la gauche : le premier chiffre est côté bouche
    SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd",
           "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcdfg"}
    hauteur = (h1 - h0) - 2 * e - 0.006
    largeur = hauteur * 0.55
    ep = hauteur * 0.12
    cy = (h0 + h1) / 2
    for i, ch in enumerate(chiffres):
        # vu du flanc gauche, l'avant de l'arme est à gauche de l'image : premier chiffre côté bouche
        cx = (av0 + av1) / 2 + (0.5 - i) * (largeur + 0.004)
        gauche_img, droite_img = cx + largeur / 2, cx - largeur / 2   # av décroît vers la droite de l'image
        haut_, milieu, bas = cy + hauteur / 2, cy, cy - hauteur / 2
        segs = {
            "a": ((gauche_img, haut_), (droite_img, haut_)), "g": ((gauche_img, milieu), (droite_img, milieu)),
            "d": ((gauche_img, bas), (droite_img, bas)), "f": ((gauche_img, haut_), (gauche_img, milieu)),
            "b": ((droite_img, haut_), (droite_img, milieu)), "e": ((gauche_img, milieu), (gauche_img, bas)),
            "c": ((droite_img, milieu), (droite_img, bas)),
        }
        for s in SEG[ch]:
            (a0, b0), (a1, b1) = segs[s]
            if abs(b0 - b1) < 1e-9:   # horizontal
                q = [P(a0, b0 - ep / 2, xs + 0.0002), P(a1, b0 - ep / 2, xs + 0.0002), P(a1, b0 + ep / 2, xs + 0.0002),
                     P(a0, b0 + ep / 2, xs + 0.0002)]
            else:
                q = [P(a0 - ep / 2, b0, xs + 0.0002), P(a0 + ep / 2, b0, xs + 0.0002), P(a0 + ep / 2, b1, xs + 0.0002),
                     P(a0 - ep / 2, b1, xs + 0.0002)]
            kit.quad(pc, q, "Lueur", normale=GAUCHE)


SEPT_SEGMENTS = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd",
                 "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcdfg"}


def repere_compteur(elevation=0.0, lacet=0.0):
    """Le repère de l'écran d'un compteur : sa normale (vers le joueur : l'arrière, +Y, relevée de
    `elevation` degrés et tournée de `lacet` degrés vers la gauche), le haut et la droite de l'écran."""
    e, l = math.radians(elevation), math.radians(lacet)
    n = Vector((math.sin(l) * math.cos(e), math.cos(l) * math.cos(e), math.sin(e)))
    haut = Vector((-math.sin(l) * math.sin(e), -math.cos(l) * math.sin(e), math.cos(e)))
    droite = Vector((-math.cos(l), math.sin(l), 0.0))   # la droite du joueur
    return n, haut, droite


def compteur_arriere(pc, centre, largeur, hauteur, elevation=0.0, chiffres="30", saillie=0.0025, epaisseur=0.004,
                     lacet=0.0):
    """Compteur de charge des armes laser, à l'arrière de l'arme, face au joueur, pour qu'il se lise en
    vue subjective (demandé par l'utilisateur le 2026-10-15) : un écran à sept segments allumé, dans
    son cadre. centre : milieu de l'écran, sur la face qui le porte ; elevation : l'écran relevé, en
    degrés (0 : vertical, face à l'arrière ; posé sur un pan qui monte vers l'avant, il regarde l'œil,
    derrière et au-dessus de l'arme) ; lacet : l'écran tourné vers la gauche, en degrés (celui du FX-2,
    sur le flanc gauche, regarde l'œil à la hanche) ; saillie : de combien le cadre dépasse de la face.
    Les chiffres se lisent de gauche à droite pour le joueur."""
    n, haut, droite = repere_compteur(elevation, lacet)
    surface = centre + n * saillie
    f = kit.boite_axes(pc, surface - n * (epaisseur / 2), droite, haut, n, largeur / 2, hauteur / 2, epaisseur / 2,
                       "Polymere")
    kit.chanfreiner(pc, 0.0010, f)

    def sur_ecran(u, v, decalage):
        return surface + droite * u + haut * v + n * decalage

    marge = 0.002
    lu, lv = largeur / 2 - marge, hauteur / 2 - marge
    kit.quad(pc, [sur_ecran(-lu, -lv, 0.0002), sur_ecran(lu, -lv, 0.0002), sur_ecran(lu, lv, 0.0002),
                  sur_ecran(-lu, lv, 0.0002)], "EcranMort", normale=n)
    nb = len(chiffres)
    h_ch = min(hauteur - 2 * marge - 0.003, (largeur - 2 * marge - 0.002) / (0.55 * nb + 0.2 * (nb - 1)))
    l_ch, ep = h_ch * 0.55, h_ch * 0.12
    for i, ch in enumerate(chiffres):
        cu = (i - (nb - 1) / 2) * (l_ch + h_ch * 0.2)
        g, d, hh, bb = cu - l_ch / 2, cu + l_ch / 2, h_ch / 2, -h_ch / 2
        segs = {"a": ((g, hh), (d, hh)), "g": ((g, 0.0), (d, 0.0)), "d": ((g, bb), (d, bb)),
                "f": ((g, hh), (g, 0.0)), "b": ((d, hh), (d, 0.0)), "e": ((g, 0.0), (g, bb)), "c": ((d, 0.0), (d, bb))}
        for s in SEPT_SEGMENTS[ch]:
            (u0, v0), (u1, v1) = segs[s]
            if abs(v0 - v1) < 1e-9:   # horizontal
                coins = [(u0, v0 - ep / 2), (u1, v0 - ep / 2), (u1, v0 + ep / 2), (u0, v0 + ep / 2)]
            else:
                coins = [(u0 - ep / 2, v0), (u0 + ep / 2, v0), (u0 + ep / 2, v1), (u0 - ep / 2, v1)]
            kit.quad(pc, [sur_ecran(u, v, 0.0004) for u, v in coins], "Lueur", normale=n)


def encoches(pc, av_depart, h_bas, h_haut, x, nombre, normale=GAUCHE, pas=0.004, mat="Rayure", largeur=0.0011):
    """Encoches de victoires, par paquets de cinq (quatre barres et une barre en travers) : gravées
    (mat="Rayure"), ou tracées au marqueur (mat="MarquageNoir", plus larges)."""
    e = largeur / 2
    av = av_depart
    restant = nombre
    while restant > 0:
        paquet = min(5, restant)
        barres = min(4, paquet)
        debut = av
        for _ in range(barres):
            kit.quad(pc, [P(av - e, h_bas, x), P(av + e, h_bas, x), P(av + e, h_haut, x),
                          P(av - e, h_haut, x)], mat, normale=normale)
            av -= pas
        if paquet == 5:
            a = Vector((debut + 0.0025, h_bas + 0.0005))
            b = Vector((av + pas - 0.0025, h_haut - 0.0004))
            d = (b - a).normalized()
            nn = Vector((-d.y, d.x)) * (e * 0.0006 / 0.00055)   # 0,6 mm pour la gravure d'origine
            xx = x + (0.0001 if normale.x > 0 else -0.0001)
            kit.quad(pc, [P(*(a - nn), xx), P(*(b - nn), xx), P(*(b + nn), xx), P(*(a + nn), xx)], mat,
                     normale=normale)
        av -= pas * 1.5
        restant -= paquet


def mire_crash(pc, centre, normale, r=0.0098):
    kit.disque(pc, centre, normale, r, 16, mats_quartiers=["MarquageNoir", "Marquage", "MarquageNoir", "Marquage"])


# --- scène, studio, lancement ------------------------------------------------------------------

def preparer_scene(nom_scene):
    scene = bpy.data.scenes.get(nom_scene) or bpy.data.scenes.new(nom_scene)
    for ancienne in list(scene.collection.children):
        kit.vider_collection(ancienne)
        bpy.data.collections.remove(ancienne)
    scene.render.engine = "BLENDER_EEVEE"
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    monde = bpy.data.worlds.get("DA_Studio") or bpy.data.worlds.new("DA_Studio")
    fond = monde.node_tree.nodes.get("Background")
    if fond is not None:
        fond.inputs["Color"].default_value = srgb(30, 31, 34)
        fond.inputs["Strength"].default_value = 1.0
    scene.world = monde
    return scene


def retirer_scene_usine(garder):
    """En arrière-plan (--factory-startup) : retire la scène d'usine, son cube, sa lampe, sa caméra.
    Les scènes des autres armes, construites dans la même session, restent."""
    usine = bpy.data.scenes.get("Scene")
    if usine is None or usine == garder:
        return
    objets = list(usine.objects)
    collections = list(usine.collection.children_recursive)
    bpy.data.scenes.remove(usine)
    for ob in objets:
        if not ob.users_scene:
            bpy.data.objects.remove(ob, do_unlink=True)
    toutes = set()
    for s in bpy.data.scenes:
        toutes.update(s.collection.children_recursive)
    for col in collections:
        if col not in toutes:
            bpy.data.collections.remove(col)
    bpy.data.orphans_purge(do_recursive=True)


def bornes(objets):
    pts = [ob.matrix_world @ Vector(c) for ob in objets if ob.type == "MESH" for c in ob.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def studio(scene, atelier, vues_detail=()):
    """Lumières et caméras de contrôle, cadrées sur l'arme. vues_detail : [(nom, position, cible,
    focale)] en coordonnées de l'arme."""
    col = kit.collection(f"Studio — {scene.name}", scene.collection)
    scene.view_layers[0].update()
    mn, mx = bornes(atelier.objets)
    c = (mn + mx) / 2
    taille = mx - mn
    L = max(taille.y, 0.2)
    k = max(L, 0.45)
    kit.lumiere("Lumiere_Cle", col, c + Vector((0.9, -0.5, 1.0)) * k, c, 45 * k * k, 1.0 * k)
    kit.lumiere("Lumiere_Remplissage", col, c + Vector((0.7, 0.9, 0.1)) * k, c, 14 * k * k, 1.5 * k)
    kit.lumiere("Lumiere_Contre", col, c + Vector((-0.9, 0.5, 0.7)) * k, c, 40 * k * k, 0.6 * k)
    kit.lumiere("Lumiere_Droite", col, c + Vector((-1.0, -0.4, 0.4)) * k, c, 18 * k * k, 1.2 * k)
    ortho = max(L * 1.08, taille.z * 1.3 * 1.8)
    recul = L * max(1.0, 0.42 / L)   # une petite arme se cadre de plus loin
    cams = {
        "profil_gauche": (kit.camera("Cam_Profil_Gauche", col, c + Vector((1.5 * k, 0, 0)), c, ortho=ortho), 1800, 1000),
        "profil_droit": (kit.camera("Cam_Profil_Droit", col, c + Vector((-1.5 * k, 0, 0)), c, ortho=ortho), 1800, 1000),
        "trois_quarts": (kit.camera("Cam_Trois_Quarts", col, c + Vector((0.95, -0.85, 0.45)) * recul, c, 50), 1600, 1000),
        "arriere": (kit.camera("Cam_Arriere", col, c + Vector((0.20, 0.62, 0.24)) * recul,
                               c + Vector((0, -0.10 * L, 0.0)), 35), 1600, 1000),
        "dessus": (kit.camera("Cam_Dessus", col, c + Vector((0, 0, 1.5 * k)), c, ortho=L * 1.08), 1800, 700),
    }
    cams["dessus"][0].rotation_euler = (0.0, 0.0, math.radians(90))   # le canon à gauche de l'image
    for nom, position, cible, focale in vues_detail:
        cams[nom] = (kit.camera(f"Cam_{nom}", col, position, cible, focale), 1400, 1000)
    scene.camera = cams["profil_gauche"][0]
    return cams


def arguments(brut=None):
    """brut : les ARGS du pont ou de construire_armes (une liste, même vide : rien à rendre ni à
    enregistrer) ; None : la ligne de commande de Blender après « -- »."""
    if brut is None:
        brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie")
    p.add_argument("--rendus")
    p.add_argument("--vues", default="")
    p.add_argument("--usure", default="", choices=[""] + list(VARIANTES_USURE))
    return p.parse_args(list(brut))


def mains_de(at):
    """Là où les mains frottent, pour la finition « polie », lu sur les repères de l'arme : la main
    droite 5 cm au-dessus de sa prise (l'origine, ou MainDroite sur un poignet de crosse droit), la
    main gauche sous le garde-main (MainGauche, absent d'un pistolet)."""
    reperes = {e.name.split(".")[0]: e.location.copy() for e in at.racine.children}
    mains = [(reperes.get(at.nom("MainDroite"), Vector((0.0, 0.0, 0.0))) + Vector((0.0, 0.0, 0.05)), 0.10)]
    if at.nom("MainGauche") in reperes:
        mains.append((reperes[at.nom("MainGauche")], 0.11))
    return mains


def principal(code, titre, nom_scene, construire, bouche, laser=False, vues_detail=(), args=None):
    """Construit une arme dans sa scène (recréée), puis rend les vues demandées et enregistre."""
    a_ = args or arguments()
    variante = a_.usure or None
    if variante:   # une scène par variante, pour qu'elles cohabitent
        nom_scene = f"{nom_scene} — {VARIANTES_USURE[variante]}"
    scene = preparer_scene(nom_scene)
    if bpy.app.background:
        retirer_scene_usine(scene)
    M = materiaux(code.replace("-", ""), bouche, variante)
    at = Atelier(scene, code, titre, laser, M, REMPLACEMENTS_USURE.get(variante))
    at.bouche = bouche          # gardés pour l'export vers Unity (exporter_armes.py)
    at.variante = variante
    construire(at)
    if any(famille.get("mains") for famille in MARQUES_USURE.get(variante, {}).values()):
        # les mains frottent sur les repères de l'arme : on refait les matériaux de la variante avec eux
        materiaux(code.replace("-", ""), bouche, variante, mains_de(at))
    total = sum(kit.triangles(o) for o in at.objets)
    print(f"  {at.code:<8} {len(at.objets):>3} objets {total:>6} triangles")
    for o in at.objets:
        print(f"      {o.name:<30} {kit.triangles(o):>5}")
    cams = studio(scene, at, vues_detail)
    if not bpy.app.background:
        for fenetre in bpy.context.window_manager.windows:
            fenetre.scene = scene
    if a_.rendus:
        os.makedirs(a_.rendus, exist_ok=True)
        vues = [v for v in a_.vues.split(",") if v] or list(cams)
        for nom in vues:
            if nom in cams:
                cam, l, h = cams[nom]
                kit.rendre(scene, cam, os.path.join(a_.rendus, nom + ".png"), l, h)
        if not a_.vues:
            argile = bpy.data.materials.get("DA_Argile_Contrôle") or bpy.data.materials.new("DA_Argile_Contrôle")
            bsdf = argile.node_tree.nodes.get("Principled BSDF")
            if bsdf is not None:
                bsdf.inputs["Base Color"].default_value = srgb(150, 150, 150)
                bsdf.inputs["Roughness"].default_value = 0.6
            cam, l, h = cams["profil_gauche"]
            kit.rendre(scene, cam, os.path.join(a_.rendus, "profil_argile.png"), l, h, argile=argile)
    if a_.sortie:
        if bpy.app.background:
            bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=a_.sortie, check_existing=False)
        print("Enregistré :", a_.sortie)
    return at
