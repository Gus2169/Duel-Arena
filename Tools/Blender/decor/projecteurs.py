"""Les rampes de projecteurs de la salle de test (2026-10-27, « projecteurs, pénombre ») : une poutre triangulée de
3,2 m pendue à la charpente, quatre projecteurs de chantier dessous, leur verre qui luit (la lumière du jeu vient
des Light qu'ArenaLighting pose dessous).

PR1 : origine au milieu de la membrure du haut, là où s'accrochent les suspentes ; la poutre le long de X, les
projecteurs regardent vers le bas.

    blender -b --factory-startup --python-exit-code 1 -P projecteurs.py -- --rendus <dossier>
"""

import os
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import commun_decor as cd  # noqa: E402

kit = cd.kit
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
DEMI = 1.6          # demi-longueur de la poutre
BAS = -0.30         # hauteur des membrures du bas
ECART = 0.15        # leur écart de chaque côté


def boite(pc, centre, demi, mat=None, chanfrein=0.0):
    f = kit.boite_axes(pc, Vector(centre), X, Y, Z, demi[0], demi[1], demi[2], mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def rampe(scene):
    at = cd.AtelierDecor(scene, "PR1", "Rampe de projecteurs", cd.materiaux())
    poutre = at.piece("Poutre", "Peinture")
    haut = [Vector((x, 0, 0)) for x in (-DEMI, DEMI)]
    bas_g = [Vector((x, -ECART, BAS)) for x in (-DEMI, DEMI)]
    bas_d = [Vector((x, ECART, BAS)) for x in (-DEMI, DEMI)]
    for a, b in (haut, bas_g, bas_d):
        kit.cylindre(poutre, a, b, 0.025, 8, mat="Peinture")
    # le treillis : des diagonales tous les 40 cm sur les trois faces
    n = 8
    for i in range(n):
        x0 = -DEMI + i * (2 * DEMI / n)
        x1 = x0 + 2 * DEMI / n
        for c0, c1 in ((Vector((x0, 0, 0)), Vector((x1, -ECART, BAS))), (Vector((x0, 0, 0)), Vector((x1, ECART, BAS))),
                       (Vector((x0, -ECART, BAS)), Vector((x1, ECART, BAS)))):
            if i % 2:
                c0, c1 = Vector((c0.x, c1.y, c1.z)), Vector((c1.x, c0.y, c0.z))
            kit.cylindre(poutre, c0, c1, 0.011, 6, mat="Peinture")
    # les plaques des bouts
    for sx in (-1, 1):
        x = sx * (DEMI + 0.005)
        f = kit.loft(poutre, [[Vector((x - 0.005, 0, 0.04)), Vector((x - 0.005, -ECART - 0.04, BAS - 0.03)),
                               Vector((x - 0.005, ECART + 0.04, BAS - 0.03))],
                              [Vector((x + 0.005, 0, 0.04)), Vector((x + 0.005, -ECART - 0.04, BAS - 0.03)),
                               Vector((x + 0.005, ECART + 0.04, BAS - 0.03))]], "Peinture")
        kit.chanfreiner(poutre, 0.004, f)
    at.objet(poutre)

    tetes = at.piece("Projecteurs", "Noir")
    verres = at.piece("Verres", "Lampe")
    for x in (-1.2, -0.4, 0.4, 1.2):
        # la lyre : deux plats qui descendent des membrures du bas
        for sy in (-1, 1):
            boite(tetes, (x, sy * 0.17, BAS - 0.13), (0.03, 0.008, 0.13), "Acier", chanfrein=0.002)
        boite(tetes, (x, 0, BAS - 0.015), (0.04, 0.18, 0.015), "Acier", chanfrein=0.003)
        # le corps, ses ailettes de refroidissement, la lunette autour du verre
        zc = BAS - 0.27
        boite(tetes, (x, 0, zc), (0.17, 0.15, 0.09), "Noir", chanfrein=0.012)
        for k in range(5):
            boite(tetes, (x - 0.12 + k * 0.06, 0, zc + 0.11), (0.006, 0.13, 0.02), "Noir", chanfrein=0.0)
        boite(tetes, (x, 0, zc - 0.095), (0.175, 0.155, 0.012), "Peinture", chanfrein=0.004)
        f = kit.quad(verres, [Vector((x - 0.15, -0.13, zc - 0.108)), Vector((x + 0.15, -0.13, zc - 0.108)),
                              Vector((x + 0.15, 0.13, zc - 0.108)), Vector((x - 0.15, 0.13, zc - 0.108))], "Lampe", normale=-Z)
    at.objet(tetes)
    at.objet(verres)

    # le câble : une boucle qui pend d'un bout à l'autre sous la membrure
    cable = at.piece("Cable", "Caoutchouc")
    chemin = kit.lisser_chemin([Vector((-DEMI + 0.1, ECART, BAS - 0.02)), Vector((-0.8, ECART + 0.02, BAS - 0.12)),
                                Vector((0.0, ECART + 0.02, BAS - 0.06)), Vector((0.8, ECART + 0.02, BAS - 0.12)),
                                Vector((DEMI - 0.1, ECART, BAS - 0.02))], 3)
    kit.balayage(cable, chemin, kit.section_cercle(0.012, 6), "Caoutchouc")
    at.objet(cable)
    return at


PROPS = [("PR1", rampe)]


def construire(code):
    for c, f in PROPS:
        if c == code:
            return f(cd.nouvelle_scene(f"Decor_{c}"))
    raise KeyError(code)


if __name__ == "__main__":
    a = cd.arguments()
    if a.rendus:
        at = construire("PR1")
        cd.rendre_vues(at, a.rendus, (0, 0, -0.3), 3.4, ("trois_quarts", "dessous", "profil"))
        print("RENDU PR1", sum(kit.triangles(o) for o in at.objets if o.type == "MESH"), "triangles")
