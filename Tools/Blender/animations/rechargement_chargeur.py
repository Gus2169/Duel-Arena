"""Le rechargement où le chargeur quitte l'arme (2026-10-29), calculé sur le squelette du Y Bot pour Unity : un clip
par arme à chargeur (le FA-07 d'abord, jugé au stand, puis les autres sur le même geste ; voir ARMES).

Le clip de rechargement du jeu (« Y Bot@Reloading », de Mixamo, le seul pour toutes les armes) remet le chargeur là
où il l'a pris : il ne quitte jamais l'arme. Ce clip-ci en garde le haut du corps et la main droite (l'arme qui se
lève et se penche vers la main gauche), recalés dans le temps sur les étapes sonores de l'arme (WeaponData.reloadCues :
au FA-07, le chargeur qui sort à 4 % du rechargement, le neuf qui entre à 45 %, l'armement à 72 %). La main gauche, elle, est
calculée par une cinématique inverse sur un trajet tracé dans le repère de l'arme : elle quitte le garde-main pendant
que le chargeur vide tombe, va prendre le neuf au porte-chargeur de la hanche gauche, le monte sous le puits, l'insère,
frappe sa semelle, tire la poignée d'armement et revient au garde-main.

Le clip a la forme de l'ancien (3,3 s ; la partie utile de 0,4 à 2,5 s) : l'état Recharge de l'Animator y entre au
même instant et le lit à la même vitesse (ReloadSpeed). Le chargeur est l'affaire du jeu (catégorie C) : la fiche
écrite à côté du FBX (même nom, en .json) dit à quels instants il tombe, apparaît dans la main et rentre.

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/animations/rechargement_chargeur.py -- \
    --fbx "Assets/_ProjectArt/Animations/Blender" [--armes FA-07,FA-30] [--rendus <dossier>] [--etapes 0,0.45,1]
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, DOSSIER)
import commun_robots as cr  # noqa: E402
import kit_lowpoly as kit  # noqa: E402
import robot_ct01  # noqa: E402

SOURCE = os.path.join(cr.PROJET, "Assets", "_ProjectArt", "Animations", "Y Bot@Reloading.fbx")

# --- le temps ----------------------------------------------------------------------------------------------
# 60 images par seconde, pas 30 comme Mixamo : la frappe sur la semelle est un geste d'une vingtaine de
# millisecondes, et entre deux clés à 30 images/s Unity n'en montrait que l'élan (la paume à 9 mm de la semelle).
IPS = 60
IMAGES = 198                      # 3,3 s, comme Y Bot@Reloading : images 1 à 199
IPS_SOURCE = 30                   # la cadence de Y Bot@Reloading (Mixamo)
DEBUT_UTILE, FIN_UTILE = 0.4, 2.5  # la partie utile, en secondes du clip : celle que l'état Recharge lit

def t_clip(f):
    """L'instant du clip (s) d'une part du rechargement."""
    return DEBUT_UTILE + f * (FIN_UTILE - DEBUT_UTILE)


# --- l'arme dans la main droite ----------------------------------------------------------------------------
# Le repère « PriseArme » du préfab joueur, relevé dans Unity (2026-10-29) sur cinq poses de Y Bot@Reloading et
# ramené à l'os de la main droite de Blender (à 3 mm et 0,3° près d'une pose à l'autre) : le repère de l'arme de
# Blender (gauche +X, canon -Y, haut +Z) dans celui de l'os, en mètres.
PRISE_POSITION = Vector((-0.00251, 0.06514, 0.02986))
PRISE_ROTATION = Quaternion((0.01031, -0.664654, -0.026646, -0.746605))

# --- la main gauche ----------------------------------------------------------------------------------------
# Dans Unity, l'os de la main gauche a les axes de celui de Blender, X retourné (relevé le même jour). La paume est
# son +Z, les doigts son +Y (WeaponGrip.HandHold). Le point de la paume du CT-01 (WeaponGrip.Settings.palmPoints,
# repère d'Unity) : c'est lui qu'on pose sur l'arme.
PAUME_UNITY = (0.010, 0.050, 0.021)
PAUME = Vector((-PAUME_UNITY[0], PAUME_UNITY[1], PAUME_UNITY[2]))


def u(x, y, z):
    """Un vecteur du repère de l'arme d'Unity (x à droite, y en haut, z vers la bouche), dans celui de Blender."""
    return Vector((-x, -z, y))


# Le porte-chargeur, sur la hanche gauche, devant : le chargeur neuf y tient debout, son milieu à cet endroit par
# rapport au bassin (repère du monde de Blender : +X la gauche du robot, -Y l'avant, +Z le haut). Un choix de
# contenu, à faire valider : la cuisse ou la poitrine le placeraient ailleurs.
POCHE = Vector((0.17, -0.11, 0.0))

# Le pôle du coude gauche (où il plie), dans le repère de l'arme d'Unity : en bas et dehors (WeaponGrip.elbowPole)
COUDE_ARME = (-0.5, -1.0, 0.0)
COUDE_POCHE = Vector((0.6, 0.6, -0.6))     # à la hanche : dehors et en arrière (monde de Blender)

def decaler(tenue, dx=0.0, dy=0.0, dz=0.0):
    (p, n, d) = tenue
    return ((p[0] + dx, p[1] + dy, p[2] + dz), n, d)


def milieu_entre(a, b, dx, dy, dz):
    """Un point de passage : le milieu de deux repères (leurs points), décalé."""
    return tuple((x + y) / 2 + d for x, y, d in zip(a[0], b[0], (dx, dy, dz)))


class Arme:
    """Ce qui change d'une arme à l'autre : son modèle dans Armes.blend, ses étapes (en part du rechargement : 0 il
    commence, 1 le chargeur est plein ; les sonores sont celles de son WeaponData.reloadCues, les autres se placent
    entre elles), et ses repères dans le repère de l'arme d'Unity (x à droite, y en haut, z vers la bouche), chacun
    (point de la paume, normale de la paume, direction des doigts) :

    - garde_main : la prise sous le garde-main, celle de WeaponGrip (Settings.handguard : paume, doigts) sur le repère
      MainGauche : le clip commence et finit où l'IK du jeu prend la main ;
    - tenue : le chargeur tenu, la paume contre son flanc gauche, sous le puits, l'index en haut, les doigts autour de
      son avant. C'est ainsi qu'il sort du porte-chargeur et qu'il entre ;
    - semelle : le dessous de sa semelle, frappé paume ouverte ;
    - levier : la poignée d'armement, deux doigts devant elle, la paume vers l'arrière ; on la tire de `course` et on
      la lâche (elle revient seule), ou, une culasse (`culasse`), on la tire jusqu'à `recul` puis on la ramène en avant
      jusqu'à `avance` avant de la lâcher ;
    - pieces : les pièces du modèle (leurs chemins dans le préfab d'Unity) qui reculent avec la main : le jeu les
      déplace selon la fiche (MagazineSwap) ;
    - milieu : le milieu du chargeur (le porte-chargeur le tient là ; les rendus le font tomber de là) ;
    - axe_puits : la direction où le chargeur entre (vers le haut du puits) : la main l'approche 4 cm en dessous.

    Les points de passage de la main se déduisent de ces repères et de ces étapes, avec les écarts du FA-07 (le premier
    geste réglé). `sons` : les étapes que marquent, dans l'ordre, les trois étapes sonores de l'arme.
    """

    def __init__(self, code, collection, garde_main, tenue, semelle, levier, course, milieu, axe_puits=(0.0, 1.0, 0.0),
                 culasse=False, sortie=0.04, saisie=0.26, approche=0.40, insertion=0.45, frappe=0.52,
                 prise_levier=0.66, recul=None, avance=None, armement=0.72, retour=0.92,
                 sons=("sortie", "insertion", "armement"), presentation=None, pieces=()):
        self.code, self.collection = code, collection
        self.clip = f"Rechargement {code}"
        self.garde_main, self.tenue, self.semelle, self.levier = garde_main, tenue, semelle, levier
        self.course, self.milieu, self.axe_puits, self.culasse = course, milieu, axe_puits, culasse
        self.sortie, self.saisie, self.approche, self.insertion = sortie, saisie, approche, insertion
        self.frappe, self.prise_levier, self.armement, self.retour = frappe, prise_levier, armement, retour
        self.recul, self.avance, self.sons, self.pieces = recul, avance, sons, pieces
        # Le haut du corps et la main droite du clip de Mixamo, recalés : (instant du nouveau clip, instant de
        # l'ancien). Dans l'ancien, la main gauche est au chargeur à 0,4 s, à la hanche à 1,1 s, insère à 1,62 s, est
        # sur le dessus de la carcasse à 2,1 s et revenue à 2,5 s (relevé dans Unity sur le Y Bot, le 2026-10-29).
        self.recalage = [(0.0, 0.0), (t_clip(0.0), 0.40), (t_clip(saisie), 1.10), (t_clip(insertion), 1.62),
                         (t_clip(prise_levier), 2.10), (t_clip(retour), 2.50), (IMAGES / IPS, 3.30)]
        # L'arme dans la main droite. Le clip de Mixamo la lève canon haut, jusqu'à 54° quand le chargeur entre : vu
        # de l'œil du jeu, son puits tournait le dos à la caméra, sous le bord de l'écran, et la vue subjective ne
        # montrait ni le chargeur ni la main. Ici elle garde sa pose de départ (celle du clip quand l'état Recharge y
        # entre), dans le repère du buste, et se présente à la main gauche : roulée vers la droite, son flanc gauche
        # et son puits vers l'œil, un peu tournée vers le corps et levée, puis à demi redressée pour l'armement.
        # (f, roulis à droite °, tangage vers le haut °, cap vers la gauche °, montée m), autour de sa poignée. Une
        # arme peut avoir la sienne (`presentation`).
        self.presentation = presentation or [
            (0.0, 0.0, 0.0, 0.0, 0.0), (sortie + 0.06, 28.0, 0.0, 6.0, 0.03), (insertion + 0.02, 28.0, 0.0, 6.0, 0.03),
            (frappe + 0.05, 10.0, -4.0, 3.0, 0.01), (armement + 0.02, 10.0, -4.0, 3.0, 0.01),
            (retour, 0.0, 0.0, 0.0, 0.0), (1.0, 0.0, 0.0, 0.0, 0.0)]

    def levier_tire(self):
        return decaler(self.levier, dz=-self.course)

    def presente(self, f):
        """La présentation de l'arme à la part `f`, adoucie d'une étape à l'autre."""
        for a, b in zip(self.presentation, self.presentation[1:]):
            if f <= b[0]:
                s = lisse((f - a[0]) / max(1e-6, b[0] - a[0]))
                return tuple(x + (y - x) * s for x, y in zip(a[1:], b[1:]))
        return self.presentation[-1][1:]

    def lache(self):
        """Là où la main lâche la poignée : tirée (elle revient seule), ou ramenée en avant (une culasse)."""
        return self.levier if self.culasse else self.levier_tire()

    def trajet(self):
        """Le trajet de la paume gauche (le point de la paume du CT-01), étape par étape. Les passages se placent par
        rapport aux étapes de l'arme, aux écarts du FA-07."""
        G, T, S, L = self.garde_main, self.tenue, self.semelle, self.levier
        Lt, Ll = self.levier_tire(), self.lache()
        cles = [
            Cle(0.0, G, doigts="garde"),
            Cle(self.sortie + 0.03, (decaler(G, -0.06, -0.088, -0.06)[0], (0.3, 0.9, 0.0), (0.6, -0.3, 0.7)),
                arret=False),
            Cle(self.saisie - 0.07, "dessus_poche", repere="poche", pole=COUDE_POCHE, arret=False),
            Cle(self.saisie - 0.02, "poche", repere="poche", pole=COUDE_POCHE, doigts="ouvert"),
            Cle(self.saisie, "poche", repere="poche", pole=COUDE_POCHE, doigts="tenir"),
            Cle(self.saisie + 0.05, "sortie_poche", repere="poche", pole=COUDE_POCHE, doigts="tenir", arret=False),
            Cle(self.approche - 0.04, decaler(T, dx=-0.05, dy=-0.15, dz=-0.02), doigts="tenir", arret=False),
            Cle(self.approche, decaler(T, *(-0.04 * c for c in self.axe_puits)), doigts="tenir"),
            Cle(self.insertion, T, doigts="tenir"),
            # la main pousse encore : le chargeur est en place, puis elle le lâche
            Cle(self.insertion + 0.02, T, doigts="tenir"),
            Cle(self.insertion + 0.035, (decaler(T, -0.0305, -0.042, 0.005)[0], (0.6, 0.8, 0.0), (0.3, 0.0, 0.95)),
                doigts="plat", arret=False),
            Cle(self.frappe - 0.02, decaler(S, dy=-0.035), doigts="plat"),
            Cle(self.frappe, S, doigts="plat"),
            Cle(self.frappe + 0.01, S, doigts="plat"),
            Cle(self.frappe + 0.03, decaler(S, dx=-0.015, dy=-0.025), doigts="plat", arret=False),
            Cle(self.prise_levier - 0.06, (milieu_entre(S, L, -0.09, -0.0095, 0.034), (0.6, 0.2, -0.7), (0.6, 0.6, 0.4)),
                doigts="ouvert", arret=False),
            Cle(self.prise_levier - 0.03, decaler(L, dx=-0.02, dy=0.03, dz=0.01), doigts="ouvert"),
            Cle(self.prise_levier, L, doigts="crochet"),
        ]
        if self.culasse:   # elle recule, puis revient en avant avec la main
            cles += [Cle(self.recul, Lt, doigts="crochet"), Cle(self.avance, L, doigts="crochet")]
        else:              # tirée, puis lâchée : elle revient seule
            cles += [Cle(self.armement - 0.01, Lt, doigts="crochet")]
        return cles + [
            Cle(self.armement, Ll, doigts="ouvert"),
            Cle(self.armement + 0.04, (decaler(Ll, -0.06, -0.025, 0.014)[0], (0.4, -0.4, -0.8), (0.9, 0.2, 0.2)),
                doigts="ouvert", arret=False),
            Cle((self.armement + self.retour) / 2, (milieu_entre(Ll, G, -0.08, -0.0865, 0.037), (0.4, 0.9, 0.0),
                                                     (0.6, 0.0, 0.8)), doigts="ouvert", arret=False),
            Cle(self.retour, G, doigts="garde"),
            Cle(1.0, G, doigts="garde"),
        ]

    def armement_fiche(self):
        """Les pièces d'armement et les instants où elles bougent : la main les prend, les a tirées à fond, les lâche
        (une poignée revient seule, en 15 millièmes du rechargement) ou les ramène (une culasse), et elles sont
        revenues."""
        if self.culasse:
            tiree, lachee, revenue = self.recul, self.recul, self.avance
        else:
            tiree, lachee, revenue = self.armement - 0.01, self.armement, self.armement + 0.015
        return {"pieces": list(self.pieces), "course": self.course, "prise": self.prise_levier, "tiree": tiree,
                "lachee": lachee, "revenue": revenue}

    def etapes(self):
        """Les étapes, sous les noms de la fiche (et de ReloadGesture)."""
        e = {"sortie": self.sortie, "saisie": self.saisie, "approche": self.approche, "insertion": self.insertion,
             "frappe": self.frappe, "priseLevier": self.prise_levier, "armement": self.armement, "retour": self.retour}
        if self.culasse:
            e["recul"], e["avance"] = self.recul, self.avance
        return e


# Les armes à chargeur, dans le repère de l'arme d'Unity (relevé sur leurs modèles exportés : Chargeur_Standard,
# MainGauche ; et sur les scripts des armes). Un code s'ajoute ici et à MagazineReloadImporter.Gestures dans Unity.
ARMES = {}
ARMES["FA-07"] = Arme(
    "FA-07", "FA-07 — fusil d'assaut",
    garde_main=((0.0, 0.108, 0.300), (0.1, 1.0, 0.15), (0.65, -0.15, 0.75)),
    tenue=((-0.0145, -0.058, 0.165), (1.0, 0.0, 0.0), (0.0, 0.15, 1.0)),       # le flanc à x = -0,0125, nervures comprises
    semelle=((0.0, -0.106, 0.196), (0.0, 0.994, -0.11), (0.35, 0.0, 0.94)),    # penchée comme le bas du chargeur courbe
    # la poignée en T à l'arrière du dessus (comme sur un M4) : deux doigts devant son loquet gauche (x -0,0185 à
    # -0,0275, z -0,082 à -0,070)
    levier=((-0.040, 0.165, -0.064), (0.0, -0.2, -1.0), (1.0, 0.0, 0.1)), course=0.07, pieces=("Levier_Armement",),
    milieu=(0.0, -0.036, 0.183))

ARMES["FA-30"] = Arme(
    "FA-30", "FA-30 — fusil d'assaut série 30",
    garde_main=((0.0, 0.065, 0.330), (0.1, 1.0, 0.15), (0.65, -0.15, 0.75)),
    # le chargeur droit, incliné de 13° vers l'arrière (serie30_fusil.py) : son flanc à x = -0,0125, à mi-hauteur de
    # ce qui sort du puits, la paume 1,3 cm en arrière de son milieu comme au FA-07, les doigts dans son équerre
    tenue=((-0.0145, -0.039, 0.187), (1.0, 0.0, 0.0), (0.0, -0.12, 0.99)),
    semelle=((0.0, -0.089, 0.1896), (0.0, 0.997, -0.081), (0.35, 0.08, 0.93)),
    # le bouton du levier, sur le flanc gauche au-dessus de la poignée (x -0,022 à -0,044, z 0,048 à 0,066) : deux
    # doigts devant lui ; sa fente le laisse reculer de 7,8 cm
    levier=((-0.059, 0.158, 0.072), (0.0, -0.2, -1.0), (1.0, 0.0, 0.1)), course=0.075, pieces=("Levier_Armement",),
    milieu=(0.0, -0.028, 0.200), axe_puits=(0.0, 0.988, 0.154))

ARMES["FP-12"] = Arme(
    "FP-12", "FP-12 — fusil de précision",
    garde_main=((0.0, 0.089, 0.380), (0.1, 1.0, 0.15), (0.65, -0.15, 0.75)),
    # le chargeur court sous le châssis (fusil_precision.py : demi-épaisseur 0,014), presque droit : son flanc à
    # mi-hauteur de ce qui sort du puits (de 0,024 à -0,055), la paume 1,3 cm en arrière de son milieu
    tenue=((-0.016, -0.0155, 0.1945), (1.0, 0.0, 0.0), (0.0, 0.03, 1.0)),
    semelle=((0.0, -0.0625, 0.2106), (0.0, 0.997, -0.078), (0.35, 0.08, 0.93)),
    # la culasse à tirage rectiligne : le bouton de sa manette (un écrou soudé), en haut à gauche, sous la lunette
    # (x -0,047 à -0,054, z -0,011 à 0,003) ; deux doigts devant lui ; elle recule de 8 cm et revient. Son son
    # (Rech_Culasse, 0,82 s) : elle recule de 0,09 à 0,27 s après son départ, revient de 0,49 à 0,60 s.
    levier=((-0.068, 0.138, 0.009), (0.0, -0.2, -1.0), (1.0, 0.0, 0.1)), course=0.08, culasse=True, pieces=("Culasse",),
    milieu=(0.0, -0.0055, 0.2085), axe_puits=(0.0, 0.999, -0.038),
    sortie=0.05, saisie=0.25, approche=0.37, insertion=0.42, frappe=0.49, prise_levier=0.68, recul=0.76, avance=0.85,
    armement=0.87, retour=0.97, sons=("sortie", "insertion", "priseLevier"))

ARMES["PA-05"] = Arme(
    "PA-05", "PA-05 — pistolet",
    # un pistolet n'a pas de repère MainGauche (WeaponGrip ne prend pas sa main gauche) : le geste part de la prise de
    # « Pistol NoAim Idle », la tenue du pistolet hors visée (ArmePistolet), relevée dans Unity sur le Y Bot
    garde_main=((-0.103, 0.038, 0.008), (0.872, -0.196, -0.448), (0.394, -0.263, 0.881)),
    # le chargeur est dans la poignée, inclinée de 18° (pistolet.py) : seule sa semelle en sort. La paume la tient
    # par-dessous, 2 mm sous elle, les doigts vers l'avant de la poignée, et la pousse dans l'axe de la poignée
    tenue=((0.0, -0.0737, -0.0269), (0.0, 0.951, 0.309), (0.0, -0.309, 0.951)),
    semelle=((0.0, -0.0737, -0.0269), (0.0, 0.951, 0.309), (0.25, -0.30, 0.92)),
    # la glissière, prise par-dessus à l'arrière (ses crans de prise de -0,022 à 0,009, la hausse à 0,094) : la paume
    # sur elle, les doigts qui passent sur son flanc droit ; on la tire de 3,5 cm et on la lâche
    levier=((0.0, 0.096, -0.012), (0.0, -1.0, 0.0), (1.0, -0.3, 0.0)), course=0.035,
    # la glissière et ce qu'elle porte : sa plaque d'adaptation, ses organes de visée, le viseur monté
    pieces=("Culasse", "Culasse_Details", "Hausse", "Guidon", "Accessoires/Viseur"),
    milieu=(0.0, -0.020, -0.013), axe_puits=(0.0, 0.951, 0.309),
    # présenté à la main gauche : roulé pour montrer son flanc gauche et le bas de sa poignée, canon un peu levé, monté
    # vers la poitrine ; d'aplomb, encore levé, pour la glissière
    presentation=[(0.0, 0.0, 0.0, 0.0, 0.0), (0.10, 25.0, 15.0, 10.0, 0.06), (0.47, 25.0, 15.0, 10.0, 0.06),
                  (0.57, 5.0, 10.0, 0.0, 0.06), (0.74, 5.0, 10.0, 0.0, 0.06), (0.92, 0.0, 0.0, 0.0, 0.0),
                  (1.0, 0.0, 0.0, 0.0, 0.0)])

A = ARMES["FA-07"]   # l'arme du geste en cours de calcul (principal la change pour chacune)


EXTENSION_MAX = 0.97                 # au-delà, la clavicule avance l'épaule (WeaponGrip.MaxArmExtension)
CLAVICULE_MAX = math.radians(15.0)   # WeaponGrip.Settings.maxShoulderTurn

OS_BRAS = ("LeftArm", "LeftForeArm", "LeftHand")
DOIGTS = [f"LeftHand{d}{i}" for d in ("Thumb", "Index", "Middle", "Ring", "Pinky") for i in (1, 2, 3)]
# Les poses des doigts gauches, prises au clip de Mixamo (instant, s) : le garde-main au début, la main ouverte qui va
# chercher, fermée sur le chargeur neuf qu'elle monte, presque plate quand elle lâche l'ancien, en crochet sur le
# dessus de la carcasse (la fermeture des doigts mesurée de 0 à 3,3 s : 22 à 158°).
DOIGTS_SOURCE = {"garde": 0.0, "ouvert": 1.8, "tenir": 1.4, "plat": 1.1, "crochet": 2.2}


def os_(n):
    return f"mixamorig:{n}"


def lisse(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3.0 - 2.0 * x)


def arguments():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", default=None, help="dossier où écrire le FBX et sa fiche")
    p.add_argument("--armes", default=",".join(ARMES), help="les codes des armes, séparés par des virgules")
    p.add_argument("--rendus", default=None)
    p.add_argument("--etapes", default="0,0.04,0.12,0.2,0.26,0.33,0.4,0.45,0.5,0.52,0.6,0.66,0.71,0.8,0.92,1")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return p.parse_args(argv)


class Squelette:
    """Le squelette et ses poses calculées os par os, dans le repère de l'armature (en cm, comme le FBX du Y Bot) :
    M passe au monde de Blender (rotation de 90° autour de X, échelle 0,01). Le même que celui de la reptation."""

    def __init__(self, arm):
        self.arm = arm
        self.M = arm.matrix_world.copy()
        self.Mi = self.M.inverted()
        self.Mr = self.M.to_3x3().normalized()
        self.repos = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
        profondeur = {b.name: len(b.parent_recursive) for b in arm.data.bones}
        self.ordre = sorted(profondeur, key=lambda n: profondeur[n])   # les parents avant les enfants

    def pose_depuis_monde(self, rotation_monde, position_monde):
        R = (self.Mi.to_3x3().normalized() @ rotation_monde).to_4x4()
        R.translation = self.Mi @ position_monde
        return R

    def base(self, nom, pose, poses):
        p = self.parent[nom]
        if p is None:
            return self.repos[nom].inverted() @ pose
        return (poses[p] @ self.repos[p].inverted() @ self.repos[nom]).inverted() @ pose

    def enfant(self, nom, poses, base):
        p = self.parent[nom]
        if p is None:
            return self.repos[nom] @ base
        return poses[p] @ self.repos[p].inverted() @ self.repos[nom] @ base

    def position(self, pose):
        return self.M @ pose.translation

    def rotation(self, pose):
        return (self.Mr @ pose.to_3x3().normalized()).normalized()


def repere_main(paume, doigts):
    """La rotation (monde) de l'os d'une main : les doigts son +Y, la paume son +Z."""
    z = paume.normalized()
    y = (doigts - z * doigts.dot(z)).normalized()
    return Matrix((y.cross(z), y, z)).transposed()


def tourner(axe, angle):
    return Matrix.Rotation(angle, 3, axe.normalized()) if axe.length > 1e-9 else Matrix.Identity(3)


def de_vers(a, b):
    """La plus petite rotation qui amène la direction a sur b."""
    a, b = a.normalized(), b.normalized()
    axe = a.cross(b)
    c = max(-1.0, min(1.0, a.dot(b)))
    if axe.length < 1e-9:
        return Matrix.Identity(3)
    return tourner(axe, math.acos(c))


def angle(a, b):
    return math.acos(max(-1.0, min(1.0, a.normalized().dot(b.normalized()))))


def ik_bras(a, R_haut, R_bas, l_ab, l_bc, cible, pole):
    """La cinématique inverse à deux os du jeu (TwoBoneIK.Solve), dans le monde : a l'épaule, R_haut et R_bas les
    rotations du bras et de l'avant-bras, l_ab et l_bc le coude et le poignet dans leur repère. Rend les rotations qui
    posent le poignet sur la cible, le coude tourné vers le pôle, et l'écart restant (hors de portée)."""
    def chaine(Rh, Rb):
        b = a + Rh @ l_ab
        return b, b + Rb @ l_bc
    b, c = chaine(R_haut, R_bas)
    lab, lcb = l_ab.length, l_bc.length
    voulu = (cible - a).length
    lat = min(max(voulu, abs(lab - lcb) + 1e-3), lab + lcb - 1e-3)
    acab0, babc0 = angle(c - a, b - a), angle(a - b, c - b)
    acab1 = math.acos(max(-1.0, min(1.0, (lcb * lcb - lab * lab - lat * lat) / (-2.0 * lab * lat))))
    babc1 = math.acos(max(-1.0, min(1.0, (lat * lat - lab * lab - lcb * lcb) / (-2.0 * lab * lcb))))
    axe = (c - a).cross(b - a)
    if axe.length < 1e-9:
        axe = (c - a).cross(R_haut @ Vector((0, 0, 1)))
    Q1 = tourner(axe, acab1 - acab0)
    R_haut, R_bas = Q1 @ R_haut, Q1 @ R_bas
    R_bas = tourner(axe, babc1 - babc0) @ R_bas
    b, c = chaine(R_haut, R_bas)
    Q3 = de_vers(c - a, cible - a)
    R_haut, R_bas = Q3 @ R_haut, Q3 @ R_bas
    b, c = chaine(R_haut, R_bas)
    membre = c - a
    coude = (b - a) - membre * ((b - a).dot(membre) / membre.length_squared)
    vers = pole - membre * (pole.dot(membre) / membre.length_squared) if pole is not None else Vector()
    if coude.length > 1e-6 and vers.length > 1e-6:
        signe = 1.0 if membre.dot(coude.cross(vers)) >= 0.0 else -1.0
        Q4 = tourner(membre, signe * angle(coude, vers))
        R_haut, R_bas = Q4 @ R_haut, Q4 @ R_bas
    b, c = chaine(R_haut, R_bas)
    return R_haut, R_bas, b, c, (c - cible).length


class Cle:
    """Une étape du trajet de la paume : sa part du rechargement, où (et comment) elle tient, dans quel repère, le
    pôle du coude, les doigts, et si la main s'y arrête (sinon, elle y passe)."""

    def __init__(self, f, ou, repere="arme", pole=None, doigts="ouvert", arret=True):
        self.f, self.ou, self.repere, self.pole, self.doigts, self.arret = f, ou, repere, pole, doigts, arret


class Geste:
    """Le calcul : le clip de Mixamo recalé pour tout sauf le bras gauche, le bras gauche par l'IK."""

    def __init__(self, sq, source_arm, source_action):
        self.sq = sq
        self.src = source_arm
        self.src_action = source_action
        self.cles = A.trajet()
        self.doigts = {}
        self.T_main_chargeur = None   # le repère de l'arme vu de la main gauche, chargeur en place

    # -- le clip de Mixamo, recalé ------------------------------------------------------------------------
    @staticmethod
    def recaler(t):
        for (t0, s0), (t1, s1) in zip(A.recalage, A.recalage[1:]):
            if t <= t1:
                return s0 + (s1 - s0) * (t - t0) / (t1 - t0)
        return A.recalage[-1][1]

    def bases_source(self, t_source):
        scene = bpy.context.scene
        image = self.src_action.frame_range[0] + t_source * IPS_SOURCE
        scene.frame_set(int(math.floor(image)), subframe=image - math.floor(image))
        return {pb.name: pb.matrix_basis.copy() for pb in self.src.pose.bones}

    def preparer_doigts(self):
        """Les poses des doigts gauches, prises au clip de Mixamo : sur le garde-main (au début), la main qui va au
        chargeur (ouverte), tenant le chargeur (à la hanche), sur le dessus de la carcasse (en crochet) ; la main
        plate est celle du repos."""
        for nom, t in DOIGTS_SOURCE.items():
            b = self.bases_source(t)
            self.doigts[nom] = {d: b[os_(d)].to_quaternion() for d in DOIGTS if os_(d) in b}

    # -- les repères ----------------------------------------------------------------------------------------
    def repere_arme(self, poses):
        """La rotation et l'origine (monde) du repère de l'arme de Blender, d'après la main droite."""
        pose = poses[os_("RightHand")]
        R = self.sq.rotation(pose)
        o = self.sq.position(pose) + R @ PRISE_POSITION
        return R @ PRISE_ROTATION.to_matrix(), o

    def resoudre(self, cle, poses):
        """Une étape dans le monde : (point de la paume, rotation de la main, pôle du coude)."""
        R_arme, o_arme = self.repere_arme(poses)
        if cle.repere == "arme":
            (p, n, d) = cle.ou
            point = o_arme + R_arme @ u(*p)
            R_main = repere_main(R_arme @ u(*n), R_arme @ u(*d))
            pole = R_arme @ u(*(cle.pole or COUDE_ARME)) if not isinstance(cle.pole, Vector) else cle.pole
            return point, R_main, pole
        # le porte-chargeur : le chargeur neuf debout sur la hanche, tenu comme il le sera en entrant
        bassin = self.sq.position(poses[os_("Hips")])
        milieu = bassin + POCHE + {"poche": Vector(), "dessus_poche": Vector((0.0, 0.0, 0.09)),
                                   "sortie_poche": Vector((0.0, -0.02, 0.12))}[cle.ou]
        # le repère de l'arme d'un chargeur debout, l'avant vers l'avant du robot, son milieu au porte-chargeur
        R_debout = Matrix(((1, 0, 0), (0, 1, 0), (0, 0, 1)))   # le repère de l'arme de Blender, d'aplomb
        o = milieu - R_debout @ u(*A.milieu)                    # le milieu du chargeur dans le repère de l'arme
        (p, n, d) = A.tenue
        point = o + R_debout @ u(*p)
        R_main = repere_main(R_debout @ u(*n), R_debout @ u(*d))
        return point, R_main, cle.pole

    # -- une image ----------------------------------------------------------------------------------------------
    def arme_presentee(self, poses, f):
        """Le repère de l'arme (monde) à la part `f` : sa pose de départ dans le repère du buste (Spine2), présentée
        à la main gauche (Arme.presentation : roulée, tournée, levée autour de sa poignée)."""
        buste = poses[os_("Spine2")]
        R_b, p_b = self.sq.rotation(buste), self.sq.position(buste)
        R0, o0 = R_b @ self.depart_R, p_b + R_b @ self.depart_p
        roulis, tangage, cap, montee = A.presente(f)
        tourne = (Matrix.Rotation(math.radians(cap), 3, "Z") @ Matrix.Rotation(-math.radians(tangage), 3, "X")
                  @ Matrix.Rotation(-math.radians(roulis), 3, "Y"))
        return R0 @ tourne, o0 + R0 @ Vector((0.0, 0.0, montee))

    def atteindre(self, cote, cible, R_main, pole, poses, bases):
        """Pose le poignet d'un bras (`cote` : Left, Right) sur `cible`, la main tournée de `R_main` (monde), le coude
        vers `pole` (None : le pli du clip de Mixamo). Quand le bras tendu n'atteint pas, la clavicule avance l'épaule
        (WeaponGrip.ReachWithShoulder). Rend l'écart qui reste (hors de portée)."""
        sq = self.sq
        bras, avant, main, clavicule_os = (os_(f"{cote}{n}") for n in ("Arm", "ForeArm", "Hand", "Shoulder"))
        epaule = sq.position(poses[bras])
        longueur = (sq.position(poses[avant]) - epaule).length + (sq.position(poses[main]) - sq.position(poses[avant])).length
        manque = (cible - epaule).length - longueur * EXTENSION_MAX
        if manque > 0.0:
            clavicule = sq.position(poses[clavicule_os])
            q = (epaule - clavicule).rotation_difference(epaule + (cible - epaule).normalized() * manque - clavicule)
            if q.angle > CLAVICULE_MAX:
                q = Quaternion().slerp(q, CLAVICULE_MAX / q.angle)
            poses[clavicule_os] = sq.pose_depuis_monde(q.to_matrix() @ sq.rotation(poses[clavicule_os]), clavicule)
            bases[clavicule_os] = sq.base(clavicule_os, poses[clavicule_os], poses)
            for nom in (bras, avant, main):
                poses[nom] = sq.enfant(nom, poses, bases[nom])
            epaule = sq.position(poses[bras])
        R_haut, R_bas = sq.rotation(poses[bras]), sq.rotation(poses[avant])
        coude0, poignet0 = sq.position(poses[avant]), sq.position(poses[main])
        l_ab = R_haut.transposed() @ (coude0 - epaule)
        l_bc = R_bas.transposed() @ (poignet0 - coude0)
        R_haut, R_bas, coude, poignet, ecart = ik_bras(epaule, R_haut, R_bas, l_ab, l_bc, cible, pole)
        poses[bras] = sq.pose_depuis_monde(R_haut, epaule)
        poses[avant] = sq.pose_depuis_monde(R_bas, coude)
        poses[main] = sq.pose_depuis_monde(R_main, poignet)
        for nom in (bras, avant, main):
            bases[nom] = sq.base(nom, poses[nom], poses)
        return ecart

    def image(self, t):
        sq = self.sq
        f = (t - DEBUT_UTILE) / (FIN_UTILE - DEBUT_UTILE)
        fc = min(max(f, 0.0), 1.0)
        bases = self.bases_source(self.recaler(t))
        poses = {}
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        # l'arme, présentée : la main droite la tient là (son coude plie comme dans le clip de Mixamo)
        R_arme, o_arme = self.arme_presentee(poses, fc)
        R_droite = R_arme @ PRISE_ROTATION.to_matrix().transposed()
        ecart_droit = self.atteindre("Right", o_arme - R_droite @ PRISE_POSITION, R_droite, None, poses, bases)
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        # la paume : les étapes qui encadrent f, chacune dans son repère à cet instant
        cles = self.cles
        i = max(k for k in range(len(cles)) if cles[k].f <= fc + 1e-9) if fc > 0 else 0
        j = min(i + 1, len(cles) - 1)
        A, B = cles[i], cles[j]
        # (borné : une étape calculée, 0,42 + 0,02, peut tomber un rien après l'instant qu'elle doit marquer)
        s = 0.0 if j == i else min(1.0, max(0.0, (fc - A.f) / (B.f - A.f)))
        pa, Ra, pola = self.resoudre(A, poses)
        pb, Rb, polb = self.resoudre(B, poses)
        # Hermite : vitesse nulle aux arrêts, celle de Catmull-Rom aux passages
        def vitesse(k):
            c = cles[k]
            if c.arret or k == 0 or k == len(cles) - 1:
                return Vector()
            p0, _, _ = self.resoudre(cles[k - 1], poses)
            p1, _, _ = self.resoudre(cles[k + 1], poses)
            return (p1 - p0) / (cles[k + 1].f - cles[k - 1].f)
        va, vb = vitesse(i), vitesse(j)
        duree = max(1e-6, B.f - A.f)
        s2, s3 = s * s, s * s * s
        h00, h10, h01, h11 = 2 * s3 - 3 * s2 + 1, s3 - 2 * s2 + s, -2 * s3 + 3 * s2, s3 - s2
        paume = pa * h00 + va * (h10 * duree) + pb * h01 + vb * (h11 * duree)
        w = lisse(s) if (A.arret and B.arret) else s
        R_main = Ra.to_quaternion().slerp(Rb.to_quaternion(), w).to_matrix()
        pole = pola.lerp(polb, s)
        # le bras gauche
        ecart = max(ecart_droit, self.atteindre("Left", paume - R_main @ PAUME, R_main, pole, poses, bases))
        # les doigts : la pose de l'étape, fondue vers la suivante
        da, db = self.doigts[A.doigts], self.doigts[B.doigts]
        wd = lisse(min(1.0, s / 0.6)) if A.doigts != B.doigts else 0.0
        for d in DOIGTS:
            if os_(d) in bases and d in da:
                q = da[d].slerp(db[d], wd)
                b = q.to_matrix().to_4x4()
                b.translation = bases[os_(d)].translation
                bases[os_(d)] = b
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        return bases, poses, ecart, f

    def calculer(self, arm):
        """L'action, et ce que le contrôle et les rendus en lisent à chaque image."""
        action = bpy.data.actions.new(A.clip)
        action.use_fake_user = True
        arm.animation_data_create()
        arm.animation_data.action = action
        self.preparer_doigts()
        # la pose de départ de l'arme : celle du clip de Mixamo quand l'état Recharge y entre, dans le repère du buste
        bases0 = self.bases_source(self.recaler(t_clip(0.0)))
        poses0 = {}
        for nom in self.sq.ordre:
            poses0[nom] = self.sq.enfant(nom, poses0, bases0[nom])
        R_a, o_a = self.repere_arme(poses0)
        R_b, p_b = self.sq.rotation(poses0[os_("Spine2")]), self.sq.position(poses0[os_("Spine2")])
        self.depart_R, self.depart_p = R_b.transposed() @ R_a, R_b.transposed() @ (o_a - p_b)
        self.mesures = []
        precedent = {}
        for image in range(1, IMAGES + 2):
            t = (image - 1) / IPS
            bases, poses, ecart, f = self.image(t)
            R_arme, o_arme = self.repere_arme(poses)
            main = poses[os_("LeftHand")]
            self.mesures.append({"image": image, "t": t, "f": f, "ecart": ecart, "R_arme": R_arme, "o_arme": o_arme,
                                 "R_main": self.sq.rotation(main), "p_main": self.sq.position(main)})
            for nom, b in bases.items():
                pb = arm.pose.bones[nom]
                pb.rotation_mode = "QUATERNION"
                q = b.to_quaternion().normalized()
                if nom in precedent and q.dot(precedent[nom]) < 0.0:
                    q = -q
                precedent[nom] = q
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=image)
                if self.sq.parent[nom] is None:
                    pb.location = b.translation
                    pb.keyframe_insert("location", frame=image)
        # le chargeur dans la main : le repère de l'arme vu de la main quand le chargeur est en place (la tenue telle
        # qu'elle est écrite : l'image la plus proche tombe entre deux)
        (p, n, d) = A.tenue
        R_main = repere_main(u(*n), u(*d))
        H = R_main.to_4x4()
        H.translation = u(*p) - R_main @ PAUME
        self.T_main_chargeur = H.inverted()
        return action

    def mesure(self, f):
        image = round(t_clip(f) * IPS) + 1
        return self.mesures[image - 1]


def controler(geste):
    """Le poignet atteint chaque étape ; aux instants qui comptent, la paume est où elle doit être."""
    pires = sorted(geste.mesures, key=lambda m: -m["ecart"])[:3]
    print(f"CONTROLE {A.code} portée : écart du poignet à sa cible au pire "
          + ", ".join(f"{m['ecart'] * 1000:.1f} mm (f={m['f']:.2f})" for m in pires))
    etapes = [("garde-main au début", 0.0, A.garde_main), ("chargeur en place", A.insertion, A.tenue),
              ("semelle frappée", A.frappe, A.semelle), ("poignée prise", A.prise_levier, A.levier)]
    if A.culasse:
        etapes += [("culasse tirée", A.recul, A.levier_tire()), ("culasse revenue", A.avance, A.levier)]
    for nom, f, cle in etapes + [("garde-main au retour", A.retour, A.garde_main)]:
        # à l'instant exact de l'étape, qui tombe d'ordinaire entre deux images
        _, poses, _, _ = geste.image(t_clip(f))
        R_arme, o_arme = geste.repere_arme(poses)
        main = poses[os_("LeftHand")]
        paume = geste.sq.position(main) + geste.sq.rotation(main) @ PAUME
        voulu = o_arme + R_arme @ u(*cle[0])
        print(f"CONTROLE {A.code} {nom} (f={f}) : paume à {(paume - voulu).length * 1000:.1f} mm de sa place")


def fiche(geste, chemin):
    """Ce que le jeu lit du geste (Duel Arena > Animations > Importer les rechargements à chargeur) : ses étapes, et
    les repères que la main touche (les tests les rejouent sur le robot du jeu et les cherchent sur le modèle)."""
    def repere(r):
        return {"paume": list(r[0]), "normale": list(r[1]), "doigts": list(r[2])}
    donnees = {
        "clip": A.clip, "arme": A.code, "ips": IPS, "duree": IMAGES / IPS, "utile": [DEBUT_UTILE, FIN_UTILE],
        "etapes": A.etapes(), "sons": list(A.sons), "culasse": A.culasse,
        "paumeConception": list(PAUME_UNITY),
        "tenue": repere(A.tenue), "semelle": repere(A.semelle), "levier": repere(A.levier),
        "gardeMain": repere(A.garde_main), "course": A.course, "axePuits": list(A.axe_puits),
        "pieceArmement": A.armement_fiche(),
    }
    with open(chemin, "w", encoding="utf-8") as fichier:
        json.dump(donnees, fichier, ensure_ascii=False, indent=2)
    print("FICHE", chemin)


def exporter(arm, action, chemin):
    """Le squelette seul et son action, en centimètres comme les FBX de Mixamo (voir reptation_laterale.py)."""
    chemin = os.path.abspath(chemin)
    os.makedirs(os.path.dirname(chemin), exist_ok=True)
    scene = bpy.context.scene
    cr.poser(arm, action, 1)
    scene.render.fps, scene.render.fps_base = IPS, 1.0
    scene.frame_start, scene.frame_end = 1, IMAGES + 1
    vl = scene.view_layers[0]
    for o in vl.objects:
        o.select_set(False)
    arm.select_set(True)
    with bpy.context.temp_override(scene=scene, view_layer=vl, active_object=arm, selected_objects=[arm]):
        bpy.ops.export_scene.fbx(filepath=chemin, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                                 use_armature_deform_only=False, apply_scale_options="FBX_SCALE_NONE",
                                 axis_forward="-Z", axis_up="Y", bake_anim=True, bake_anim_use_all_bones=True,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                                 bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0, path_mode="STRIP")
    print("EXPORT", chemin)


# --- les rendus de contrôle --------------------------------------------------------------------------------

def charger_arme(col):
    with bpy.data.libraries.load(cr.ARMES_BLEND, link=False) as (source, cible):
        cible.collections = [A.collection]
    arme = cible.collections[0]
    col.children.link(arme)
    racine = next(o for o in arme.objects if o.parent is None)
    chargeur = next(o for o in arme.objects if o.name.endswith("Chargeur_Standard"))
    return arme, racine, chargeur


def matrice(R, o):
    m = R.to_4x4()
    m.translation = o
    return m


def animer_arme(geste, arme, racine, chargeur):
    """L'arme suit la main droite ; le chargeur vide tombe à la sortie, le neuf suit la main gauche jusqu'à son
    insertion (le jeu le fera de même, MagazineSwap)."""
    vide = chargeur.copy()
    vide.data = chargeur.data
    vide.name = "Chargeur_Vide"
    neuf = chargeur.copy()
    neuf.data = chargeur.data
    neuf.name = "Chargeur_Neuf"
    for o in (vide, neuf):
        arme.objects.link(o)
        o.parent = None
    tombe = None
    vitesse = Vector()
    for k, m in enumerate(geste.mesures):
        image, f = m["image"], m["f"]
        W = matrice(m["R_arme"], m["o_arme"])
        racine.matrix_world = W
        racine.keyframe_insert("location", frame=image)
        racine.keyframe_insert("rotation_euler", frame=image)
        dedans = f < A.sortie or f >= A.insertion
        chargeur.hide_render = not dedans
        chargeur.keyframe_insert("hide_render", frame=image)
        # le vide : en place jusqu'à la sortie, puis il tombe avec la vitesse de l'arme, un peu poussé vers le bas
        if f >= A.sortie:
            if tombe is None:
                precedent = geste.mesures[k - 1]
                vitesse = (m["o_arme"] - precedent["o_arme"]) * IPS + m["R_arme"] @ Vector((0, 0, -1.2))
                tombe = (W.copy(), image, W @ u(*A.milieu))
            dt = (image - tombe[1]) / IPS
            c = tombe[2] + vitesse * dt + Vector((0, 0, -4.905 * dt * dt))
            axe = tombe[0].to_3x3() @ Vector((1, 0, 0))
            vide.matrix_world = Matrix.Translation(c) @ Matrix.Rotation(-2.5 * dt, 4, axe) \
                @ Matrix.Translation(-tombe[2]) @ tombe[0]
            vide.hide_render = c.z < 0.0
        else:
            vide.matrix_world = W
            vide.hide_render = True
        vide.keyframe_insert("location", frame=image)
        vide.keyframe_insert("rotation_euler", frame=image)
        vide.keyframe_insert("hide_render", frame=image)
        # le neuf : dans la main de la saisie à l'insertion
        H = matrice(m["R_main"], m["p_main"])
        neuf.matrix_world = H @ geste.T_main_chargeur
        neuf.hide_render = not (A.saisie <= f < A.insertion)
        neuf.keyframe_insert("location", frame=image)
        neuf.keyframe_insert("rotation_euler", frame=image)
        neuf.keyframe_insert("hide_render", frame=image)


def rendus(scene, geste, rb, col, dossier, etapes):
    os.makedirs(dossier, exist_ok=True)
    cr.lumieres(col, Vector((0.0, -0.3, 1.2)))
    monde = bpy.data.worlds.new("Monde")
    monde.node_tree.nodes["Background"].inputs["Color"].default_value = kit.srgb(28, 29, 32)
    scene.world = monde
    studio = kit.collection("Studio", scene.collection)
    sol = bpy.data.meshes.new("Sol")
    sol.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
    sob = bpy.data.objects.new("Sol", sol)
    studio.objects.link(sob)
    arme, racine, chargeur = charger_arme(col)
    animer_arme(geste, arme, racine, chargeur)
    centre = Vector((0.0, -0.2, 1.1))
    # la vue subjective, à peu près : la caméra du joueur au-dessus et en arrière de l'arme tenue à la hanche (les
    # bras du jeu sont placés 12 cm à droite, 18 cm plus bas et 27 cm plus loin que la visée), le regard droit devant
    scene.frame_set(round(t_clip(0.0) * IPS) + 1)
    tete = rb.arm.matrix_world @ rb.arm.pose.bones[os_("Head")].head
    oeil = tete + Vector((0.0, 0.04, 0.06))
    # les vues fixées à l'arme (roulis compris : elle ne bouge pas dans l'image) : de sa gauche, de sa droite, de
    # dessous ; les colonnes de la caméra (son +X, son +Y, son +Z, qui regarde vers -Z) dans le repère de l'arme
    vues_arme = {"arme_gauche": (Vector((0.62, -0.14, 0.0)), ((0, 1, 0), (0, 0, 1), (1, 0, 0))),
                 "arme_droite": (Vector((-0.62, -0.14, 0.0)), ((0, -1, 0), (0, 0, 1), (-1, 0, 0))),
                 "arme_dessous": (Vector((0.10, -0.16, -0.62)), ((1, 0, 0), (0, -1, 0), (0, 0, -1)))}
    for vue in list(vues_arme) + ["gauche", "fps"]:
        # en vue subjective, le jeu replie la tête et le cou (FirstPersonView.BuildView)
        for nom in ("Head", "Neck"):
            rb.arm.pose.bones[os_(nom)].scale = (0.0, 0.0, 0.0) if vue == "fps" else (1.0, 1.0, 1.0)
        for f in etapes:
            image = round(t_clip(f) * IPS) + 1
            scene.frame_set(image)
            m = geste.mesures[image - 1]
            if vue in vues_arme:
                position, colonnes = vues_arme[vue]
                local = Matrix(colonnes).transposed().to_4x4()
                local.translation = position
                cam = kit.camera(f"Cam_{vue}_{image}", studio, Vector(), Vector((0, 0, -1)), 50)
                cam.matrix_world = matrice(m["R_arme"], m["o_arme"]) @ local
            elif vue == "gauche":
                cam = kit.camera(f"Cam_{vue}_{image}", studio, centre + Vector((2.2, -0.9, 0.2)), centre, 45)
            else:
                cam = kit.camera(f"Cam_{vue}_{image}", studio, oeil, oeil + Vector((-0.08, -1.0, -0.45)), 24)
            cr.rendre(scene, cam, os.path.join(dossier, f"{vue}_{int(round(f * 1000)):04d}.png"), 560, 560, 32)


def principal():
    global A
    args = arguments()
    scene = bpy.data.scenes.new("Rechargements à chargeur")
    usine = bpy.data.scenes.get("Scene")
    if usine is not None and usine != scene:
        for o in list(usine.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.scenes.remove(usine)
    if bpy.context.window_manager.windows:
        bpy.context.window_manager.windows[0].scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    rb, ob, col = cr.construire(robot_ct01.MODELE, scene, "orange")
    source = cr.importer_squelette(scene, "Source")
    action_source = cr.charger_attente(SOURCE)
    cr.poser(source, action_source, 1)
    # après les imports : l'import d'un FBX met la scène à la cadence du fichier (30 images/s chez Mixamo)
    scene.render.fps = IPS
    sq = Squelette(rb.arm)
    codes = [c.strip() for c in args.armes.split(",") if c.strip()]
    if args.rendus and len(codes) > 1:
        raise SystemExit("les rendus se font une arme à la fois (--armes CODE)")
    for code in codes:
        A = ARMES[code]
        geste = Geste(sq, source, action_source)
        action = geste.calculer(rb.arm)
        controler(geste)
        if args.fbx:
            dossier = args.fbx if os.path.isabs(args.fbx) else os.path.join(cr.PROJET, args.fbx)
            exporter(rb.arm, action, os.path.join(dossier, f"Y Bot@{A.clip}.fbx"))
            fiche(geste, os.path.join(dossier, f"Y Bot@{A.clip}.json"))
        if args.rendus:
            cr.poser(rb.arm, action, 1)
            rendus(scene, geste, rb, col, os.path.abspath(args.rendus), [float(x) for x in args.etapes.split(",")])
    print("FIN")


if __name__ == "__main__":   # lancé par Blender ; rechargement_golden_gun.py l'importe pour sa mécanique
    principal()
