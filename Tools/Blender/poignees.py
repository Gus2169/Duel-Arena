"""Les poignées avant de Duel Arena (Blender 5.2, low poly), des accessoires du stock (GDD § 6), une
seule sous le garde-main, comme dans Rainbow Six :
- PV-1 : la poignée verticale, qui retient le recul ; une prise ronde à crans, un bouchon au culot, une
  chambre à air enfilée sur la prise ;
- PI-1 : la poignée inclinée, qui fait viser plus vite ; un coin profilé, le pouce posé dessus.

Elles pendent sous le rail du dessous (leur embase à l'envers). Le repère « MainGauche » est là où
la paume gauche les tient : la cible de l'IK de la main gauche.

  blender -b --factory-startup -P poignees.py -- [--seuls PV-1] [--rendus dossier] [--sur-arme ...]
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
from kit_lowpoly import AVANT, GAUCHE, HAUT, P  # noqa: E402


def poignee_verticale(a):
    pc = a.piece("Prise", "Polymere")
    acc.embase(pc, -0.020, 0.020, demi=0.0122, h_haut=0.0050, mat="Polymere", sens=-1)
    rayons = [0.0116, 0.0126, 0.0136, 0.0128, 0.0139, 0.0130, 0.0141, 0.0133, 0.0146, 0.0150, 0.0147]
    positions = [0.0, 0.05, 0.18, 0.30, 0.40, 0.52, 0.62, 0.74, 0.86, 0.95, 1.0]
    f = kit.tranches(pc, P(0.0, -0.0045), P(0.0, -0.1000), rayons, n=12, mat="Polymere", positions=positions)
    kit.chanfreiner(pc, 0.0010, f, angle_min=60)
    f = kit.cylindre(pc, P(0.0, -0.1000), P(0.0, -0.1045), 0.0142, 12, mat="Caoutchouc")   # bouchon de rangement
    kit.chanfreiner(pc, 0.0012, f)
    acc.vis_de_serrage(pc, 0.0, 0.0122, h=-0.0026)
    a.objet(pc)
    # la prise a usé ses crans : une chambre à air enfilée dessus
    pc = a.piece("Chambre_Air", "Caoutchouc")
    cercle = [(0.0142 * math.cos(2 * math.pi * i / 12), 0.0142 * math.sin(2 * math.pi * i / 12)) for i in range(12)]
    kit.bande(pc, cercle, P(0.0, -0.0560), Vector((1, 0, 0)), Vector((0, -1, 0)), HAUT, 0.0240, 0.0014, "Caoutchouc",
              irregularite=0.0005, rng=a.rng)
    a.objet(pc)
    a.main_gauche(-0.002, -0.050, 0.012)
    a.donnees["effet"] = "recul"


def poignee_inclinee(a):
    pc = a.piece("Prise", "Polymere")
    acc.embase(pc, -0.046, 0.042, demi=0.0122, h_haut=0.0045, mat="Polymere", sens=-1)
    profil = [(-0.046, -0.0040), (-0.046, -0.0085), (-0.020, -0.0160), (0.004, -0.0260), (0.020, -0.0350),
              (0.028, -0.0372), (0.036, -0.0330), (0.041, -0.0200), (0.042, -0.0040)]
    f = kit.prisme(pc, profil, -0.0118, 0.0118, "Polymere")
    kit.chanfreiner(pc, 0.0028, f)
    for x, n in ((0.01185, GAUCHE), (-0.01185, -GAUCHE)):                             # stries sous le pouce
        for i in range(4):
            av = -0.034 + 0.008 * i
            h = -0.0115 - 0.0032 * i
            acc.trait(pc, P(av, h - 0.0030, x), P(av + 0.0030, h + 0.0012, x), 0.0011, "Interieur", normale=n)
    acc.vis_de_serrage(pc, -0.012, 0.0122, h=-0.0022)
    a.objet(pc)
    pc = a.piece("Reperes", "Marquage")                                              # témoins de serrage
    acc.trait(pc, P(-0.0465, -0.0022, 0.0060), P(-0.0465, -0.0070, 0.0060), 0.0011, "Marquage",
              normale=acc.VERS_TIREUR)
    a.objet(pc, angle=None)
    a.main_gauche(-0.012, -0.022, 0.012)
    a.donnees["effet"] = "epaulement"


POIGNEES = [
    ("PV-1", "Poignée verticale", poignee_verticale),
    ("PI-1", "Poignée inclinée", poignee_inclinee),
]


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = [acc.construire_accessoire(code, titre, "Poignee", f"Poignée — {titre}", f, args, args.usure or None)
                for code, titre, f in POIGNEES if not seuls or code in seuls]
    if args.rendus and args.sur_arme:
        cadrage = {"gauche": (Vector((0.30, 0.06, -0.06)), Vector((0.0, 0.0, -0.03)), 45),
                   "joueur": (Vector((0.16, 0.30, 0.10)), Vector((0.0, 0.0, -0.03)), 40)}
        for script in [s for s in args.sur_arme.split(",") if s]:
            acc.rendre_sur_arme(ateliers, script, args.rendus, "Poignee", cadrage)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
