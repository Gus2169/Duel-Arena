"""Le canon du Big D traverse-t-il la carcasse en basculant ? (2026-11-03)

Construit le Big D (golden_gun.py), puis fait tourner le canon de 0 à 180° par pas de `pas` degrés, le chien armé
puis abattu (pendant le rechargement, il est abattu quand le canon s'ouvre), et compte à chaque angle les paires de
triangles du canon (et de ce qu'il porte : pattes, guidon, cartouche) et des pièces fixes qui se coupent (arbres BVH).
Une face posée à plat contre une autre, ou deux triangles de part et d'autre d'un plan x = constante, ne comptent pas :
ils se touchent sans se traverser. À relancer après toute retouche du nez, des pattes, du canon ou du chien.

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/golden_gun_collisions.py -- [pas_en_degres]
"""
import math
import os
import sys

import bpy
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
pas = float(args[0]) if args else 2.5
script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "golden_gun.py")
ns = {"ARGS": [], "__file__": script, "__name__": "golden_gun"}
exec(compile(open(script, encoding="utf-8").read(), script, "exec"), ns)
at = ns["ATELIER"]
scene = at.scene
canon = bpy.data.objects[at.nom("Canon")]
chien = bpy.data.objects[at.nom("Chien")]


def descendants(ob):
    return [d for e in ob.children for d in [e] + descendants(e)]


mobiles = [o for o in descendants(canon) if o.type == "MESH"]
fixes = [o for o in descendants(at.racine) if o.type == "MESH" and o not in mobiles]


def arbre(ob, dg):
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    verts = [ob.matrix_world @ v.co for v in me.vertices]
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    ev.to_mesh_clear()
    return BVHTree.FromPolygons(verts, tris), verts, tris


def plan_x(verts, tri):
    """Le triangle est-il dans un plan x = constante (un flanc, une joue, un tenon) ? Rend ce x, sinon None."""
    xs = [verts[i].x for i in tri]
    return xs[0] if max(xs) - min(xs) < 1e-6 else None


def touche(va, ta, vb, tb):
    """Le triangle a est dans un plan x = c, et le triangle b tout entier d'un côté de ce plan."""
    c = plan_x(va, ta)
    if c is None:
        return False
    d = [vb[k].x - c for k in tb]
    return min(d) > -1e-6 or max(d) < 1e-6


def de_part_et_d_autre(va, ta, vb, tb):
    xa, xb = [va[k].x for k in ta], [vb[k].x for k in tb]
    return max(xa) <= min(xb) + 1e-6 or max(xb) <= min(xa) + 1e-6


pire = 0
for abattu in (0.0, ns["ARMEMENT_CHIEN"]):
    chien.rotation_euler.x = math.radians(abattu)
    canon.rotation_euler.x = 0.0
    scene.view_layers[0].update()
    fixes_arbres = {o.name: arbre(o, scene.view_layers[0].depsgraph) for o in fixes}
    angle, touches = 0.0, []
    while angle <= 180.0 + 1e-6:
        canon.rotation_euler.x = -math.radians(angle)
        scene.view_layers[0].update()
        dg = scene.view_layers[0].depsgraph
        for m in mobiles:
            am, vm, tm = arbre(m, dg)
            for nom, (af, vf, tf) in fixes_arbres.items():
                n = sum(1 for i, j in am.overlap(af)
                        if not touche(vm, tm[i], vf, tf[j]) and not touche(vf, tf[j], vm, tm[i])
                        and not de_part_et_d_autre(vm, tm[i], vf, tf[j]))
                if n:
                    touches.append(f"{angle:.1f}° {m.name.split('_', 1)[1]}×{nom.split('_', 1)[1]}:{n}")
                    pire = max(pire, n)
        angle += pas
    print(f"COLLISIONS chien {'abattu' if abattu else 'armé'} : " + (", ".join(touches) if touches else "aucune"))
print("COLLISIONS pire :", pire)
sys.exit(1 if pire else 0)
