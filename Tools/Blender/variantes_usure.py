"""Planches des variantes d'usure (Blender 5.2) : des armes qui servent depuis longtemps et qu'on a
réparées plusieurs fois (proposition du 2026-10-15, voir commun_armes.VARIANTES_USURE).

Le FA-07 (stock, anthracite) et le FA-30 (série 30, blanc cassé), dans l'usure actuelle puis dans
chaque variante : une planche de profil, étiquetée, et une planche de gros plans sur la carcasse
du FA-07. Rien n'est enregistré dans le projet : les rendus vont dans le dossier donné.

  blender -b --factory-startup -P variantes_usure.py -- --rendus <dossier> [--armes fusil_assaut.py,serie30_fusil.py]
"""

import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)

VARIANTES = [None, "repeinte", "rouillee", "depareillee", "polie", "encrassee"]
COLONNES = 3
PAS_COLONNE = 1.30      # écart entre deux cases, le long des armes
PAS_ARME = 0.36         # écart entre deux armes d'une même case
PAS_LIGNE = 0.92        # écart entre deux lignes de cases


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--rendus", required=True)
    p.add_argument("--armes", default="fusil_assaut.py,serie30_fusil.py")
    return p.parse_args(brut)


def construire(script, variante):
    """Exécute le script d'une arme dans une variante d'usure (sans rendu ni enregistrement)."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__arme__", "__file__": chemin, "ARGS": ["--usure", variante] if variante else []}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    return espace["ATELIER"]


def materiau_etiquette():
    mat = bpy.data.materials.get("DA_Etiquette") or bpy.data.materials.new("DA_Etiquette")
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1.0)
    bsdf.inputs["Emission Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 0.7
    return mat


def materiau_fond():
    mat = bpy.data.materials.get("DA_Etiquette_Fond") or bpy.data.materials.new("DA_Etiquette_Fond")
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.01, 0.01, 0.012, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    return mat


def etiquette(ca, col, texte, taille, mat, parent=None, position=(0, 0, 0), rotation=(0, 0, 0), centre=True):
    cu = bpy.data.curves.new(f"Etiquette_{texte}", "FONT")
    cu.body = texte
    cu.size = taille
    cu.align_x = "CENTER" if centre else "LEFT"
    cu.font = bpy.data.fonts.load(ca.POLICE, check_existing=True)
    cu.materials.append(mat)
    ob = bpy.data.objects.new(f"Etiquette_{texte}", cu)
    col.objects.link(ob)
    ob.parent = parent
    ob.location = position
    ob.rotation_euler = rotation
    return ob


def composer(chemins, colonnes, sortie):
    """Assemble des images de même taille en une grille, de gauche à droite puis de haut en bas."""
    images = [bpy.data.images.load(c, check_existing=False) for c in chemins]
    w, h = images[0].size
    lignes = (len(images) + colonnes - 1) // colonnes
    grille = np.zeros((lignes * h, colonnes * w, 4), dtype=np.float32)
    for i, im in enumerate(images):
        px = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        ligne, colonne = divmod(i, colonnes)
        y0 = (lignes - 1 - ligne) * h   # les pixels d'une image Blender vont de bas en haut
        grille[y0:y0 + h, colonne * w:(colonne + 1) * w] = px.reshape(h, w, 4)
    sortie_im = bpy.data.images.new("Composition", colonnes * w, lignes * h, alpha=True)
    sortie_im.pixels.foreach_set(grille.ravel())
    sortie_im.filepath_raw = sortie
    sortie_im.file_format = "PNG"
    sortie_im.save()


def principal():
    a = arguments()
    os.makedirs(a.rendus, exist_ok=True)
    armes = [s for s in a.armes.split(",") if s]
    cases = [[construire(s, v) for s in armes] for v in VARIANTES]
    import commun_armes as ca
    kit = ca.kit

    scene = ca.preparer_scene("Variantes d'usure")
    col = kit.collection("Variantes", scene.collection)
    etiquettes = kit.collection("Étiquettes", scene.collection)
    mat = materiau_etiquette()
    mn_tout, mx_tout = Vector((9, 9, 9)), Vector((-9, -9, -9))
    premieres = []   # les instances de chaque case, une par arme
    for i, (variante, ateliers) in enumerate(zip(VARIANTES, cases)):
        ligne, colonne = divmod(i, COLONNES)
        base = Vector((0.0, PAS_COLONNE * colonne, -PAS_LIGNE * ligne))
        centre_y = None
        premieres.append([])
        for k, at in enumerate(ateliers):
            c = at.col
            c.instance_offset = at.racine.location
            e = bpy.data.objects.new(f"Variante_{i}_{c.name}", None)
            e.instance_type = "COLLECTION"
            e.instance_collection = c
            e.location = base + Vector((0.0, 0.0, -PAS_ARME * k))
            col.objects.link(e)
            mn, mx = ca.bornes([o for o in c.objects if o.type == "MESH"])
            decalage = e.location - c.instance_offset
            mn_tout = Vector(map(min, mn_tout, mn + decalage))
            mx_tout = Vector(map(max, mx_tout, mx + decalage))
            centre_y = (mn.y + mx.y) / 2 + decalage.y if centre_y is None else centre_y
            premieres[-1].append(e)
        nom = ca.VARIANTES_USURE.get(variante, "Actuelle")
        bas = base.z - PAS_ARME * (len(ateliers) - 1) - 0.16
        etiquette(ca, etiquettes, nom, 0.055, mat, position=(0.05, centre_y, bas),
                  rotation=(math.radians(90), 0, math.radians(90)))
        mn_tout.z = min(mn_tout.z, bas - 0.02)
    scene.view_layers[0].update()

    studio = kit.collection("Studio — variantes", scene.collection)
    centre = (mn_tout + mx_tout) / 2
    taille = mx_tout - mn_tout
    kit.lumiere("Lumiere_Cle", studio, centre + Vector((2.6, -1.0, 2.2)), centre, 360, 3.0)
    kit.lumiere("Lumiere_Remplissage", studio, centre + Vector((2.2, 1.8, 0.0)), centre, 120, 3.5)
    kit.lumiere("Lumiere_Contre", studio, centre + Vector((-2.0, 0.6, 1.6)), centre, 200, 2.0)
    largeur, hauteur = 3000, 1500
    ortho = max(taille.y * 1.04, taille.z * 1.08 * largeur / hauteur)
    cam = kit.camera("Cam_Variantes", studio, centre + Vector((3.0, 0, 0)), centre, ortho=ortho)
    kit.rendre(scene, cam, os.path.join(a.rendus, "variantes_profil.png"), largeur, hauteur, echantillons=64)

    # gros plans : la carcasse de chaque arme, de trois quarts avant, une planche par arme
    for k, script in enumerate(armes):
        chemins = []
        for i, (instances, variante) in enumerate(zip(premieres, VARIANTES)):
            cible = instances[k].location + Vector((0.0, -0.10, 0.085))
            position = cible + Vector((0.30, -0.24, 0.17))
            cam_g = kit.camera(f"Cam_Gros_Plan_{k}_{i}", studio, position, cible, 50)
            cle = kit.lumiere(f"Lumiere_Gros_Plan_{k}_{i}", studio, cible + Vector((0.35, -0.10, 0.40)), cible, 4, 0.5)
            texte = ca.VARIANTES_USURE.get(variante, "Actuelle")
            etiquette(ca, studio, texte, 0.0011, mat, parent=cam_g, position=(-0.0100, -0.0064, -0.03), centre=False)
            fond = bpy.data.meshes.new(f"Fond_{k}_{i}")
            l, hh = 0.00062 * len(texte) + 0.0008, 0.0017
            fond.from_pydata([(-0.0104, -0.0069, -0.0302), (-0.0104 + l, -0.0069, -0.0302),
                              (-0.0104 + l, -0.0069 + hh, -0.0302), (-0.0104, -0.0069 + hh, -0.0302)], [], [(0, 1, 2, 3)])
            fond.materials.append(materiau_fond())
            plaque = bpy.data.objects.new(f"Fond_{k}_{i}", fond)
            studio.objects.link(plaque)
            plaque.parent = cam_g
            chemin = os.path.join(a.rendus, f"gros_plan_{k}_{i}.png")
            kit.rendre(scene, cam_g, chemin, 1000, 667, echantillons=64)
            cle.hide_render = True
            for enfant in cam_g.children:
                enfant.hide_render = True
            chemins.append(chemin)
        nom = os.path.splitext(script)[0]
        composer(chemins, COLONNES, os.path.join(a.rendus, f"variantes_gros_plans_{nom}.png"))
    print("Rendus :", a.rendus)


principal()
