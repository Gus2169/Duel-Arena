"""Construit toutes les armes de Duel Arena dans un seul fichier (Blender 5.2).

Une scène par arme : les quatre armes à balles du stock (fusil d'assaut, précision, pompe,
pistolet), les quatre de la série 30 (fusil d'assaut, précision à levier, pompe, revolver : la
dernière dotation, aux formes modernes) et les quatre prototypes laser (FX-1 à FX-4). Une scène « Planche » les montre
ensemble, vues du flanc gauche : chaque classe sur une ligne ; le stock à gauche, la série 30 au
milieu, le prototype laser à droite.

  blender -b --factory-startup -P construire_armes.py -- --sortie Armes.blend [--rendus dossier] [--empreinte]
  python -I blender_exec.py -f construire_armes.py --delai 600 -- [--rendus dossier]

--empreinte imprime une empreinte de la géométrie et des matériaux de chaque arme : deux
constructions doivent donner les mêmes (les booléens exacts ne le garantissent qu'avec l'auto-
intersection, voir commun_armes.Atelier.couper).
"""

import argparse
import hashlib
import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)

import commun_armes  # noqa: E402

# (stock, série 30, prototype laser) de chaque classe ; None : pas d'arme de cette famille
CLASSES = commun_armes.CLASSES
NOM_PLANCHE = "Planche — armes de Duel Arena"


def arguments():
    brut = list(globals().get("ARGS") or [])
    if not brut and "--" in sys.argv:
        brut = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--sortie")
    p.add_argument("--rendus")
    p.add_argument("--empreinte", action="store_true")
    return p.parse_args(brut)


def construire(script):
    """Exécute le script d'une arme (sans rendu ni enregistrement) et rend son atelier."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__arme__", "__file__": chemin, "ARGS": []}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    return espace["ATELIER"]


def empreinte(atelier):
    """La forme seule : chaque face par ses sommets et son matériau, sans l'ordre des sommets, que
    le booléen exact ne rend pas toujours le même d'une construction à l'autre (la forme, si)."""
    h = hashlib.sha256()
    for ob in sorted(atelier.objets, key=lambda o: o.name):
        me = ob.data
        h.update(ob.name.encode())
        faces = sorted("%s|%s" % (",".join(sorted("%.4f %.4f %.4f" % tuple(me.vertices[i].co) for i in p.vertices)),
                                  me.materials[p.material_index].name if me.materials else "")
                       for p in me.polygons)
        h.update("\n".join(faces).encode())
    return h.hexdigest()[:16]


def planche(lignes):
    """lignes : [(atelier du stock, de la série 30, laser), ...], de haut en bas (None : case vide)."""
    import commun_armes as ca
    kit = ca.kit
    scene = ca.preparer_scene(NOM_PLANCHE)
    col = kit.collection("Armes", scene.collection)
    mn_tout, mx_tout = Vector((9, 9, 9)), Vector((-9, -9, -9))
    h = 0.0
    for paire in lignes:
        hauteur = 0.0
        for j, at in enumerate(paire):   # le stock à gauche de l'image, puis la série 30, puis le laser
            if at is None:
                continue
            c = at.col
            c.instance_offset = at.racine.location
            e = bpy.data.objects.new(f"Planche_{c.name}", None)
            e.instance_type = "COLLECTION"
            e.instance_collection = c
            e.location = Vector((0, 1.45 * j, -h))   # vu du flanc gauche, +Y est à droite de l'image
            col.objects.link(e)
            mn, mx = ca.bornes([o for o in c.objects if o.type == "MESH"])
            decalage = e.location - c.instance_offset
            mn_tout = Vector(map(min, mn_tout, mn + decalage))
            mx_tout = Vector(map(max, mx_tout, mx + decalage))
            hauteur = max(hauteur, (mx - mn).z)
        h += hauteur + 0.07
    scene.view_layers[0].update()
    studio = kit.collection(f"Studio — {scene.name}", scene.collection)
    centre = (mn_tout + mx_tout) / 2
    taille = mx_tout - mn_tout
    kit.lumiere("Lumiere_Cle", studio, centre + Vector((2.2, -0.8, 2.0)), centre, 260, 2.5)
    kit.lumiere("Lumiere_Remplissage", studio, centre + Vector((1.8, 1.6, 0.0)), centre, 90, 3.0)
    kit.lumiere("Lumiere_Contre", studio, centre + Vector((-1.8, 0.6, 1.4)), centre, 160, 1.5)
    cam = kit.camera("Cam_Planche", studio, centre + Vector((3.0, 0, 0)), centre,
                     ortho=max(taille.y * 1.05, taille.z * 2.0 * 1.08))   # image 2:1
    scene.camera = cam
    return scene, cam


def principal():
    a = arguments()
    lignes = [tuple(construire(s) if s else None for s in paire) for paire in CLASSES]
    if a.empreinte:
        for paire in lignes:
            for at in paire:
                if at is not None:
                    print(f"EMPREINTE {at.code:<8} {empreinte(at)}")
    scene, cam = planche(lignes)
    if not bpy.app.background:
        for fenetre in bpy.context.window_manager.windows:
            fenetre.scene = scene
    if a.rendus:
        os.makedirs(a.rendus, exist_ok=True)
        import commun_armes as ca
        ca.kit.rendre(scene, cam, os.path.join(a.rendus, "planche.png"), 3000, 1500, echantillons=64)
    if a.sortie:
        if bpy.app.background:
            bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=a.sortie, check_existing=False)
        print("Enregistré :", a.sortie)


principal()
