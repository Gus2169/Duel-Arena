"""Essai du rendu « low poly propre » sur une COPIE du robot provisoire (le Y Bot de Mixamo), pour le
comparer au cel-shading (demandé par l'utilisateur le 2026-10-25, bible § 13 ; l'original reste intact).

Le Y Bot est importé tel quel, ses deux maillages (la coque, les articulations) sont décimés jusqu'à des
facettes franches, ombrés à plat, et l'ensemble repart en FBX, squelette et poids compris, sous un autre
nom : Unity lui donne l'avatar du Y Bot, et il joue les mêmes animations.

  blender -b --factory-startup -P robot_lowpoly.py -- --sortie <fichier.fbx> [--rendus dossier]
                                                       [--coque 0.07] [--articulations 0.08]
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

SOURCE = r"D:\Documents\Unity\FPS Claude\Assets\_ProjectArt\Mesh\Personnages\Y Bot.fbx"


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie", required=True)
    p.add_argument("--rendus", default="")
    p.add_argument("--coque", type=float, default=0.07, help="part des faces gardées sur la coque")
    p.add_argument("--articulations", type=float, default=0.08, help="part des faces gardées sur les articulations")
    return p.parse_args(brut)


def vider():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)


def decimer(ob, ratio):
    """Décime un maillage lié au squelette (les groupes de sommets suivent), puis l'ombre à plat : de
    grandes facettes, chacune d'une seule teinte."""
    avant = len(ob.data.polygons)
    mod = ob.modifiers.new("Decimation", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    # avant l'armature dans la pile : appliqué, il ne cuit pas une pose dans le maillage
    ob.modifiers.move(ob.modifiers.find(mod.name), 0)
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for poly in ob.data.polygons:
        poly.use_smooth = False
    print(f"  {ob.name:<16} {avant:>6} -> {len(ob.data.polygons):>5} faces")


def rendre(dossier, nom):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 1200
    if scene.camera is None:
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        lum = bpy.data.objects.new("Soleil", bpy.data.lights.new("Soleil", "SUN"))
        lum.data.energy = 3.0
        lum.rotation_euler = (math.radians(50), 0, math.radians(30))
        scene.collection.objects.link(lum)
    cam = scene.camera
    cam.data.lens = 50
    cible = Vector((0.0, 0.0, 0.95))
    cam.location = cible + Vector((1.6, -3.2, 0.5))
    cam.rotation_euler = (cible - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(dossier, nom + ".png")
    bpy.ops.render.render(write_still=True)


def principal():
    a = arguments()
    vider()
    bpy.ops.import_scene.fbx(filepath=SOURCE, automatic_bone_orientation=False, ignore_leaf_bones=False)
    maillages = [o for o in bpy.data.objects if o.type == "MESH"]
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    print(f"Y Bot importé : {len(maillages)} maillages, squelette {armature.name}")
    if a.rendus:
        os.makedirs(a.rendus, exist_ok=True)
        rendre(a.rendus, "original")
    for ob in maillages:
        decimer(ob, a.coque if "Surface" in ob.name else a.articulations)
    if a.rendus:
        rendre(a.rendus, "lowpoly")
    os.makedirs(os.path.dirname(os.path.abspath(a.sortie)), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.abspath(a.sortie), use_selection=False, object_types={"ARMATURE", "MESH"},
                             add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
                             mesh_smooth_type="FACE", use_armature_deform_only=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y")
    print("EXPORT", os.path.abspath(a.sortie))


principal()
