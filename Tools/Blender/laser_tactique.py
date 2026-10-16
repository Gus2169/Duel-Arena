"""Le laser tactique de Duel Arena (Blender 5.2, low poly), un accessoire du stock (GDD § 6) : toujours
allumé, son faisceau et son point se voient de partout ; il aide le tir au juger mais trahit la
position de qui le porte. Son faisceau est rouge, la couleur qu'aucun robot ne porte (bible § 12).

- LT-1 : le boîtier des armes d'épaule, sur le rail (ou la plaque) du flanc droit. Dessiné posé à plat
  puis couché sur le flanc (la face gauche du dessin devient son dessus, que voit le joueur ; son
  dessus devient le flanc droit, que voient les autres) : l'émetteur du laser et l'illuminateur à
  l'avant, le sélecteur et l'étiquette de danger côté adversaires ;
- LT-P : le petit boîtier des pistolets, pendu sous le canon, ses palettes sous l'index.

Le repère « Faisceau » est l'émetteur : Unity y fait partir le faisceau, vers l'avant de l'arme.

  blender -b --factory-startup -P laser_tactique.py -- [--seuls LT-1] [--rendus dossier] [--sur-arme ...]
"""

import importlib
import math
import os
import sys

import bpy  # noqa: F401
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_accessoires as acc  # noqa: E402

importlib.reload(acc)
ca = acc.ca
kit = acc.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P  # noqa: E402

VERS_TIREUR = acc.VERS_TIREUR


def laser_fusil(a):
    # dessiné posé sur un rail, puis couché sur le flanc droit : HAUT devient la droite, GAUCHE le haut
    a.transformation = Matrix.Rotation(math.radians(-90), 4, "Y")
    pc = a.piece("Boitier", "Peinture")
    acc.embase(pc, -0.024, 0.024, demi=0.0112, h_haut=0.0040)
    f = kit.boite(pc, -0.032, 0.036, 0.0035, 0.0300, -0.0170, 0.0170)
    kit.chanfreiner(pc, 0.0024, f)
    for x, r, mat in ((0.0080, 0.0050, "Laser"), (-0.0060, 0.0074, "Lentille")):   # laser, illuminateur
        f = kit.anneau(pc, 0.0345, 0.0390, 0.0170, r + 0.0020, r, 12, "Peinture", x=x)
        kit.chanfreiner(pc, 0.0005, f, angle_min=60)
        kit.disque(pc, P(0.0372, 0.0170, x), AVANT, r, 12, mat=mat)
    acc.molette(pc, P(-0.012, 0.0300, 0.0035), HAUT, 0.0062, 0.0032, dents=10)      # sélecteur, côté adversaires
    acc.molette(pc, P(-0.032, 0.0170, 0.0), VERS_TIREUR, 0.0082, 0.0050, dents=12, mat="Peinture")   # pile
    for av in (-0.024, 0.026):                                                        # vis du capot (dessus, côté joueur)
        kit.vis_six_pans(pc, P(av, 0.0260, 0.0170), GAUCHE, r=0.0020, h=0.0010, mat="Acier")
    kit.vis_six_pans(pc, P(av, 0.0080, 0.0170), GAUCHE, r=0.0028, h=0.0022, mat="AcierBrut")   # un écrou, faute de vis
    kit.vis_six_pans(pc, P(-0.024, 0.0080, 0.0170), GAUCHE, r=0.0020, h=0.0010, mat="Acier")
    acc.vis_de_serrage(pc, 0.0, 0.0112, h=0.0016)
    a.objet(pc)

    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, "LT-1", 0.0052, P(0.004, 0.0170, 0.01705), HAUT, AVANT, decalage=0.0002)   # lisible d'au-dessus
    ca.autocollant_laser(pc, P(0.014, 0.03015, -0.006), HAUT, AVANT, 0.0058)
    a.objet(pc, angle=None)

    # le bouchon de pile, desserré par le recul, tenu par un tour de ruban adhésif
    pc = a.piece("Ruban", "Ruban")
    acc.ruban_autour(a, pc, 0.0345, 0.0270, 0.01675, -0.0265, largeur=0.0090, mat="Ruban", incl=0.06,
                     rabat=(5, 0.006))
    a.objet(pc)
    a.faisceau(0.0390, 0.0170, 0.0080)
    a.donnees["couleurFaisceau"] = "rouge"


def laser_pistolet(a):
    pc = a.piece("Boitier", "Polymere")
    acc.embase(pc, -0.020, 0.020, demi=0.0105, h_haut=0.0040, mat="Polymere", sens=-1)
    f = kit.boite(pc, -0.024, 0.026, -0.0225, -0.0035, -0.0140, 0.0140)
    kit.chanfreiner(pc, 0.0022, f)
    for x, r, mat in ((-0.0062, 0.0040, "Laser"), (0.0058, 0.0058, "Lentille")):
        f = kit.anneau(pc, 0.0245, 0.0285, -0.0130, r + 0.0016, r, 12, "Polymere", x=x)
        kit.chanfreiner(pc, 0.0004, f, angle_min=60)
        kit.disque(pc, P(0.0268, -0.0130, x), AVANT, r, 12, mat=mat)
    # palettes ambidextres, sous l'index ; la droite, cassée, refaite dans une tôle pliée
    for x0, x1, mat in ((0.0140, 0.0168, "Caoutchouc"), (-0.0168, -0.0140, "AcierBrut")):
        f = kit.boite(pc, -0.026, -0.015, -0.0190, -0.0080, x0, x1, mat)
        kit.chanfreiner(pc, 0.0008, f)
    acc.vis_de_serrage(pc, 0.002, 0.0105, h=-0.0020)
    a.objet(pc)

    pc = a.piece("Marquages", "Marquage")
    a.texte(pc, "LT-P", 0.0038, P(0.002, -0.0128, 0.01405), ca.VERS_CROSSE, HAUT, decalage=0.0002)
    ca.autocollant_laser(pc, P(0.002, -0.0128, -0.01405), -GAUCHE, AVANT, 0.0042)
    a.objet(pc, angle=None)
    a.faisceau(0.0285, -0.0130, -0.0062)
    a.donnees["couleurFaisceau"] = "rouge"


LASERS = [
    ("LT-1", "Laser tactique", laser_fusil),
    ("LT-P", "Laser tactique de poing", laser_pistolet),
]


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = [acc.construire_accessoire(code, titre, "Laser", f"Laser — {titre}", f, args, args.usure or None)
                for code, titre, f in LASERS if not seuls or code in seuls]
    if args.rendus and args.sur_arme:
        par_code = {a.code: a for a in ateliers}
        # les armes d'épaule, vues de la droite (les autres) et d'au-dessus (le joueur) ; les pistolets d'en dessous
        cadrage_droite = {"droite": (Vector((-0.26, 0.10, 0.10)), Vector((0.0, -0.02, 0.0)), 45),
                          "joueur": (Vector((0.10, 0.30, 0.20)), Vector((0.0, -0.02, 0.0)), 40)}
        cadrage_poing = {"dessous": (Vector((0.12, 0.14, -0.10)), Vector((0.0, 0.0, -0.01)), 45),
                         "droite": (Vector((-0.20, 0.06, -0.03)), Vector((0.0, 0.0, -0.01)), 45)}
        for script in [s for s in args.sur_arme.split(",") if s]:
            pistolet = script in ("pistolet.py", "laser_pistolet.py", "serie30_revolver.py")
            a = par_code.get("LT-P" if pistolet else "LT-1")
            if a is not None:
                acc.rendre_sur_arme([a], script, args.rendus, "Laser", cadrage_poing if pistolet else cadrage_droite)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
