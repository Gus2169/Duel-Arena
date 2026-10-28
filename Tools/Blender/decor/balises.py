"""Le voyant « TIR EN COURS » de la salle de test (2026-10-27) : un caisson noir au panneau rouge allumé, un gyrophare
dessus dans sa cage. Le rouge est celui de l'énergie et du danger (bible § 12) : aucun robot ne le porte.

GY1 : fixé au mur, origine contre le mur (Y = 0), le caisson vers -Y (Unity : vers +Z), le haut en +Z.

    blender -b --factory-startup --python-exit-code 1 -P balises.py -- --rendus <dossier>
"""

import math
import os
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import commun_decor as cd  # noqa: E402

kit = cd.kit
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def boite(pc, centre, demi, mat=None, chanfrein=0.0):
    f = kit.boite_axes(pc, Vector(centre), X, Y, Z, demi[0], demi[1], demi[2], mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def voyant(scene):
    at = cd.AtelierDecor(scene, "GY1", "Voyant de tir en cours", cd.materiaux())
    caisson = at.piece("Caisson", "Noir")
    boite(caisson, (0, -0.07, 0), (0.36, 0.07, 0.12), "Noir", chanfrein=0.01)
    # la lunette du panneau
    for sz in (-1, 1):
        boite(caisson, (0, -0.142, sz * 0.095), (0.33, 0.006, 0.012), "Peinture", chanfrein=0.003)
    for sx in (-1, 1):
        boite(caisson, (sx * 0.318, -0.142, 0), (0.012, 0.006, 0.083), "Peinture", chanfrein=0.003)
    # les pattes de fixation, vissées au mur
    for sx in (-1, 1):
        boite(caisson, (sx * 0.4, -0.01, 0), (0.045, 0.01, 0.06), "Acier", chanfrein=0.003)
        kit.vis_six_pans(caisson, Vector((sx * 0.42, -0.022, 0)), -Y, r=0.008, h=0.004, mat="Acier")
    at.objet(caisson)

    panneau = at.piece("Panneau", "Noir")
    kit.quad(panneau, [Vector((-0.306, -0.1415, -0.083)), Vector((0.306, -0.1415, -0.083)),
                       Vector((0.306, -0.1415, 0.083)), Vector((-0.306, -0.1415, 0.083))], "Noir", normale=-Y)
    kit.texte(panneau, "TIR EN COURS", cd.police(cd.POLICE_POCHOIR), 0.062, Vector((0, -0.1415, 0)), X, Z,
              mat="Voyant", decalage=0.0008, scene=at.scene)
    at.objet(panneau)

    # le gyrophare : un socle, le dôme rouge, sa cage
    gyro = at.piece("Gyrophare", "Noir")
    kit.cylindre(gyro, Vector((0, -0.07, 0.12)), Vector((0, -0.07, 0.15)), 0.065, 12, mat="Noir")
    at.objet(gyro)
    dome = at.piece("Dome", "Voyant")
    kit.tranches(dome, Vector((0, -0.07, 0.15)), Vector((0, -0.07, 0.27)), [0.055, 0.055, 0.05, 0.035, 0.0], 12, mat="Voyant",
                 positions=[0.0, 0.55, 0.8, 0.94, 1.0])
    at.objet(dome)
    cage = at.piece("Cage", "Acier")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = Vector((math.cos(a) * 0.068, -0.07 + math.sin(a) * 0.068, 0))
        kit.cylindre(cage, p + Z * 0.15, p + Z * 0.265, 0.004, 5, mat="Acier")
    kit.cylindre(cage, Vector((0, -0.07, 0.265)), Vector((0, -0.07, 0.275)), 0.072, 12, mat="Acier")
    at.objet(cage)
    return at


PROPS = [("GY1", voyant)]


def construire(code):
    for c, f in PROPS:
        if c == code:
            return f(cd.nouvelle_scene(f"Decor_{c}"))
    raise KeyError(code)


if __name__ == "__main__":
    a = cd.arguments()
    if a.rendus:
        at = construire("GY1")
        cd.rendre_vues(at, a.rendus, (0, -0.07, 0.05), 0.8, ("trois_quarts", "face"))
        print("RENDU GY1", sum(kit.triangles(o) for o in at.objets if o.type == "MESH"), "triangles")
