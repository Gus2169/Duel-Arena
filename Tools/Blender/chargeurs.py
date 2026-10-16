"""Les chargeurs grande capacité de Duel Arena (Blender 5.2, low poly) : un par arme, allongé dans la
forme du chargeur de l'arme (bible § 12 ; GDD § 6 : la moitié de balles en plus).
- les chargeurs courbes ou droits et les cellules d'énergie (FA-07, FP-12, FA-30, FX-1, FX-2) : les
  deux bords du chargeur de l'arme (ses courbes DOS et FACE) allongés depuis leur haut, dans le puits,
  avec le constructeur du chargeur de l'arme (ses nervures, sa semelle, sa jauge) ;
- les chargeurs de pistolet (PA-05, FX-4) : un manchon qui prolonge la poignée, la semelle plus bas ;
- les tubes des pompes et du FP-30 : une rallonge vissée au bout du tube.
Le RV-30 n'en a pas : un barillet ne s'allonge pas (l'effet de jeu vaut quand même).

Chacun se construit dans le repère de son arme (le script de l'arme est exécuté pour lire la forme de
son chargeur), puis il est ramené à son point d'accroche « Chargeur » : l'origine de l'accessoire.

  blender -b --factory-startup -P chargeurs.py -- [--seuls CG-FA07] [--rendus dossier] [--sur-arme 1]
"""

import importlib
import math
import os
import sys

import bpy
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

ALLONGEMENT = 1.45   # la longueur d'un chargeur, pour la moitié de balles en plus (son haut reste dans le puits)
RALLONGE_POIGNEE = 0.045

CHARGEURS = [
    # (arme, son script, genre de chargeur, réglages)
    ("FA-07", "fusil_assaut.py", "propre", {}),
    ("FP-12", "fusil_precision.py", "chargeur", dict(demi=0.0140, mat_semelle="Acier")),
    ("FA-30", "serie30_fusil.py", "chargeur", dict(demi=0.0125, mat="Polymere", mat_semelle="PeintureClaire",
                                                   bande=False)),
    ("FX-1", "laser_fusil.py", "cellule", dict(demi=0.0135, segments=6, allumes=5)),
    ("FX-2", "laser_precision.py", "cellule", dict(demi=0.0150, segments=7, allumes=6)),
    ("PA-05", "pistolet.py", "poignee", {}),
    ("FX-4", "laser_pistolet.py", "cellule_poignee", {}),
    ("FAP-03", "fusil_pompe.py", "tube", dict(r=0.0118, longueur=0.070, n=8, mat="Peinture", mat_bout="Acier")),
    ("FAP-30", "serie30_pompe.py", "tube", dict(r=0.0118, longueur=0.055, n=8, mat="Acier", mat_bout="Laiton")),
    ("FP-30", "serie30_precision.py", "tube", dict(r=0.0090, longueur=0.055, n=10, mat="Acier", mat_bout="Laiton")),
    ("FX-3", "laser_dispersion.py", "tube", dict(r=0.0118, longueur=0.055, n=8, mat="Acier", mat_bout="Acier",
                                                 bagues=2)),
]


def allonger(courbe, k=ALLONGEMENT):
    """Un bord de chargeur, de haut en bas, allongé de k depuis son haut (la courbure suit)."""
    haut = Vector(courbe[0])
    return [tuple(haut + (Vector(p) - haut) * k) for p in courbe]


def renommer(ob, a, nom):
    ob.name = a.nom(nom)
    ob.data.name = a.nom(nom)


def construire_chargeur(code_arme, genre, reglages, espace, point):
    def construire(a):
        a.transformation = Matrix.Translation(-point)   # dessiné dans le repère de l'arme
        dos, face = espace.get("DOS"), espace.get("FACE")
        if genre == "propre":   # le chargeur du script de l'arme, ses nervures comprises
            espace["DOS"], espace["FACE"] = allonger(dos), allonger(face)
            try:
                espace["chargeur"](a)
            finally:
                espace["DOS"], espace["FACE"] = dos, face
            renommer(a.objets[-1], a, "Chargeur_Grand")
        elif genre == "chargeur":
            ca.chargeur(a, allonger(dos), allonger(face), reglages["demi"], nom="Chargeur_Grand",
                        mat=reglages.get("mat", "Polymere"), mat_semelle=reglages.get("mat_semelle"),
                        bande=reglages.get("bande", True))
        elif genre == "cellule":
            ca.cellule(a, allonger(dos), allonger(face), reglages["demi"], nom="Chargeur_Grand",
                       segments=reglages["segments"], allumes=reglages["allumes"])
        elif genre in ("poignee", "cellule_poignee"):
            pt, L, E = espace["pt"], espace["LONGUEUR"], RALLONGE_POIGNEE
            laser = genre == "cellule_poignee"
            pc = a.piece("Chargeur_Grand", "Peinture" if laser else "Acier")
            corps = [pt(0.006, 0.016), pt(L + E, 0.016), pt(L + E, -0.016), pt(0.006, -0.016)]
            kit.prisme(pc, corps, -0.0105, 0.0105, "Peinture" if laser else "Acier")
            manchon = [pt(L - 0.002, 0.0250), pt(L + E - 0.001, 0.0256), pt(L + E - 0.001, -0.0242), pt(L - 0.002, -0.0238)]
            f = kit.prisme(pc, manchon, -0.0160, 0.0160, "Ceramique" if laser else "Polymere")
            kit.chanfreiner(pc, 0.0015, f)
            semelle = [pt(L + E - 0.002, 0.0262 if laser else 0.0255), pt(L + E + 0.0078, 0.0270 if laser else 0.0262),
                       pt(L + E + 0.0078, -0.0256 if laser else -0.0250), pt(L + E - 0.002, -0.0252 if laser else -0.0245)]
            f = kit.prisme(pc, semelle, -0.0170, 0.0170, "Acier" if laser else "AcierBrut")
            kit.chanfreiner(pc, 0.0017, f)
            if laser:   # la jauge court le long du manchon, des deux côtés : plus de charge
                for x in (0.01605, -0.01605):
                    q = [pt(L + 0.004, 0.0110), pt(L + E - 0.006, 0.0110), pt(L + E - 0.006, -0.0060), pt(L + 0.004, -0.0060)]
                    kit.quad(pc, [P(av, h, x) for av, h in q], "Lueur", normale=GAUCHE * (1 if x > 0 else -1))
            else:       # deux nervures d'accroche sur le manchon
                for t in (L + 0.012, L + 0.026):
                    for x0, x1 in ((0.0158, 0.0166), (-0.0166, -0.0158)):
                        kit.prisme(pc, [pt(t, 0.0200), pt(t + 0.004, 0.0204), pt(t + 0.004, -0.0190), pt(t, -0.0186)],
                                   x0, x1, "Polymere")
            a.objet(pc)
        elif genre == "tube":
            av0, h = -point.y, point.z
            r, L, n = reglages["r"], reglages["longueur"], reglages["n"]
            pc = a.piece("Rallonge", reglages["mat"])
            kit.cylindre(pc, P(av0 - 0.002, h), P(av0 + L, h), r, n, mat=reglages["mat"])
            for c0, c1 in ((av0 - 0.001, av0 + 0.009), (av0 + L, av0 + L + 0.008)):   # écrou de raccord, bouchon
                f = kit.cylindre(pc, P(c0, h), P(c1, h), r + 0.0012, n, mat=reglages["mat_bout"])
                kit.chanfreiner(pc, 0.0007, f, angle_min=60)
            for i in range(reglages.get("bagues", 0)):   # sur le tube de charges : deux charges de plus, allumées
                c = av0 + L * (0.35 + 0.35 * i)
                kit.cylindre(pc, P(c - 0.003, h), P(c + 0.003, h), r + 0.0005, n, mat="Lueur")
            a.objet(pc)
            # la rallonge tient au tube par un tour de fil de fer, serré à la pince
            pc = a.piece("Fil", "Fil")
            cercle = [((r + 0.0013) * math.cos(2 * math.pi * i / 10), h + (r + 0.0013) * math.sin(2 * math.pi * i / 10))
                      for i in range(10)]
            kit.bande(pc, cercle, P(av0 + 0.0125, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, 0.0018, 0.0011, "Fil",
                      irregularite=0.0003, rng=a.rng)
            a.objet(pc)
        a.donnees["arme"] = code_arme
        a.donnees["genre"] = genre
    return construire


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = []
    for code_arme, script, genre, reglages in CHARGEURS:
        code = "CG-" + code_arme.replace("-", "")
        if seuls and code not in seuls:
            continue
        espace = acc.arme(script)
        point = acc.point_de(espace["ATELIER"], "Chargeur")
        a = acc.construire_accessoire(code, f"Chargeur grande capacité {code_arme}", "Chargeur",
                                      f"Chargeur grand — {code_arme}", construire_chargeur(code_arme, genre, reglages,
                                                                                          espace, point),
                                      args, args.usure or None)
        a.script_arme = script
        ateliers.append(a)
        if args.rendus and args.sur_arme:
            masquer = () if genre == "tube" else ("Chargeur_Standard",)
            if genre == "tube":
                cadrage = {"gauche": (Vector((0.30, -0.02, 0.04)), Vector((0.0, -0.03, 0.0)), 45)}
            elif genre in ("poignee", "cellule_poignee"):
                cadrage = {"gauche": (Vector((0.30, 0.02, -0.02)), Vector((0.0, 0.0, 0.02)), 45)}
            else:
                cadrage = {"gauche": (Vector((0.42, 0.04, -0.04)), Vector((0.0, 0.0, -0.06)), 45)}
            acc.rendre_sur_arme([a], script, args.rendus, "Chargeur", cadrage, masquer=masquer)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
