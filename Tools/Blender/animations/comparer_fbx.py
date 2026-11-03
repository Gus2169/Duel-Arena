"""Compare les animations de deux FBX (le même squelette) : l'écart le plus grand de chaque courbe, clé par clé.
blender -b --factory-startup -P comparer_fbx.py -- <a.fbx> <b.fbx>"""
import sys

import bpy

a_fbx, b_fbx = sys.argv[sys.argv.index("--") + 1:][:2]


def courbes(chemin):
    avant = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=chemin, automatic_bone_orientation=False)
    action = next(a for a in bpy.data.actions if a not in avant)
    valeurs = {}
    for c in action.fcurves if hasattr(action, "fcurves") else []:
        valeurs[(c.data_path, c.array_index)] = [k.co[1] for k in c.keyframe_points]
    if not valeurs:   # Blender 5 : les courbes sont dans les couches de l'action
        for couche in action.layers:
            for bande in couche.strips:
                for sac in bande.channelbags:
                    for c in sac.fcurves:
                        valeurs[(c.data_path, c.array_index)] = [k.co[1] for k in c.keyframe_points]
    return valeurs


A, B = courbes(a_fbx), courbes(b_fbx)
print("COURBES", len(A), len(B))
pire, ou = 0.0, None
manque = [k for k in A if k not in B] + [k for k in B if k not in A]
for k in A:
    if k not in B:
        continue
    if len(A[k]) != len(B[k]):
        print("CLES DIFFERENTES", k, len(A[k]), len(B[k]))
        continue
    for i, (x, y) in enumerate(zip(A[k], B[k])):
        if abs(x - y) > pire:
            pire, ou = abs(x - y), (k, i)
print("MANQUE", manque[:5])
print("ECART MAX", pire, ou)
