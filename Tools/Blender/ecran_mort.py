"""L'image de l'écran de la mort, sur la visière du robot (2026-10-26, demandé par l'utilisateur : « quand le robot
meurt, un écran glitch avec écrit FATAL ERROR sur l'écran du visage », d'après une image de référence : un
triangle d'alerte rouge et FATAL ERROR sur un fond de pixels bleus qui grésille).

Ce script n'en fait que les masques, sur fond noir : le canal rouge pour le triangle et son point
d'exclamation, le vert pour le texte. Le shader DuelArena/EcranVisage les colore, les fait grésiller (bandes
décalées, couleurs séparées, lignes de balayage) et dessine le fond de pixels.

Le cadre est celui des UV de la visière du CT-01 (robot_ct01.LARGEUR_ECRAN, BAS_ECRAN, HAUTEUR_ECRAN) : 9 cm sur
15 cm, vus de face, d'où une image de 600 x 1000. Les yeux du robot barrent la visière vers 45 à 58 % de sa
hauteur : le triangle est au-dessus, le texte en dessous, sur deux lignes (la visière est étroite).

  blender -b --factory-startup -P ecran_mort.py -- --sortie Assets/_ProjectArt/Textures/EcranMort.png
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

DOSSIER = os.path.dirname(os.path.abspath(__file__))
PROJET = os.path.normpath(os.path.join(DOSSIER, "..", ".."))
POLICE = "C:/Windows/Fonts/bahnschrift.ttf"
LARGEUR, HAUTEUR = 600, 1000          # px ; la scène fait 0,6 x 1 (une unité pour la hauteur)


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie", default=os.path.join(PROJET, "Assets", "_ProjectArt", "Textures", "EcranMort.png"))
    return p.parse_args(brut)


def emission(nom, couleur):
    mat = bpy.data.materials.new(nom)
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = couleur
    nt.links.new(e.outputs["Emission"], sortie.inputs["Surface"])
    return mat


def maillage(scene, nom, faces_points, mat):
    """Un objet plat fait de polygones (listes de points (x, y), à plat sur z = 0)."""
    me = bpy.data.meshes.new(nom)
    bm = bmesh.new()
    for pts in faces_points:
        bm.faces.new([bm.verts.new(Vector((x, y, 0.0))) for x, y in pts])
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(nom, me)
    scene.collection.objects.link(ob)
    return ob


def triangle(centre_y, hauteur, epaisseur):
    """Le triangle d'alerte, pointe en haut, en contour : trois quadrilatères entre le triangle et le triangle
    intérieur (même centre, rayon inscrit diminué de l'épaisseur)."""
    r = hauteur / 3.0                       # rayon inscrit
    k = (r - epaisseur) / r
    ext = [Vector((0.0, centre_y + 2 * r)), Vector((-r * math.sqrt(3), centre_y - r)), Vector((r * math.sqrt(3), centre_y - r))]
    c = Vector((0.0, centre_y))
    inte = [c + (p - c) * k for p in ext]
    return [[(ext[i].x, ext[i].y), (ext[(i + 1) % 3].x, ext[(i + 1) % 3].y), (inte[(i + 1) % 3].x, inte[(i + 1) % 3].y),
             (inte[i].x, inte[i].y)] for i in range(3)]


def texte(scene, contenu, y, taille, mat):
    cu = bpy.data.curves.new(f"Texte_{contenu}", "FONT")
    cu.body = contenu
    cu.font = bpy.data.fonts.load(POLICE, check_existing=True)
    cu.size = taille
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.space_character = 1.08
    cu.materials.append(mat)
    ob = bpy.data.objects.new(f"Texte_{contenu}", cu)
    scene.collection.objects.link(ob)
    ob.location = (0.0, y, 0.0)
    return ob


def principal():
    args = arguments()
    sortie = os.path.abspath(args.sortie)   # un chemin relatif n'enregistre rien (skill blender)
    scene = bpy.context.scene
    for o in list(scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    rouge = emission("Masque_Triangle", (1.0, 0.0, 0.0, 1.0))
    vert = emission("Masque_Texte", (0.0, 1.0, 0.0, 1.0))

    # Le triangle (de 61 à 93 % de la hauteur), son point d'exclamation
    base, haut = 0.61 - 0.5, 0.93 - 0.5
    h = haut - base
    centre = base + h / 3.0
    maillage(scene, "Triangle", triangle(centre, h, 0.030), rouge)
    maillage(scene, "Exclamation", [[(-0.009, 0.183), (0.009, 0.183), (0.014, 0.318), (-0.014, 0.318)],
                                     [(-0.011, 0.142), (0.011, 0.142), (0.011, 0.164), (-0.011, 0.164)]], rouge)
    # FATAL ERROR, sur deux lignes, sous les yeux (de 8 à 38 % de la hauteur)
    texte(scene, "FATAL", 0.31 - 0.5, 0.150, vert)
    texte(scene, "ERROR", 0.15 - 0.5, 0.150, vert)

    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 1.0
    cam = bpy.data.objects.new("Camera", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.0, 0.0, 5.0)
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = LARGEUR
    scene.render.resolution_y = HAUTEUR
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True     # un fond vraiment noir dans les canaux (le monde par défaut est gris)
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = sortie
    os.makedirs(os.path.dirname(sortie), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print("ECRAN", sortie)


principal()
