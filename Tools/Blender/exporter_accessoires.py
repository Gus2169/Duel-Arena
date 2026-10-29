"""Exporte les accessoires des armes de Duel Arena vers Unity (Blender 5.2) : la géométrie, l'occlusion
cuite et les paramètres des six finitions de chacun, comme exporter_armes.py pour les armes (il en
reprend les fonctions).

  blender -b --factory-startup --python-exit-code 1 -P exporter_accessoires.py -- --sortie <dossier>
          [--scripts viseurs.py,poignees.py] [--resolution 512] [--echantillons 64]

Pour chaque accessoire, dans <dossier>/<CODE>/ (PRA, LT1, PV1, CGFA07...) :
- <CODE>_Modele.fbx : ses pièces, sa vitre (un objet à part, sans dépliage : Unity y met le shader du
  réticule) et ses repères (Mire, Faisceau, MainGauche), son origine au point d'accroche de l'arme. Le
  réticule dessiné des rendus de contrôle n'y est pas.
- <CODE>_Occlusion.png : l'occlusion de ses pièces, cuite sans la vitre (elle n'arrête pas la lumière).
- <CODE>_Finitions.json : la fiche des armes (matériaux de chaque finition), plus ce qu'Unity lit de
  l'accessoire : sa rubrique (Viseur, Laser, Poignee, Chargeur), sa vitre, l'œilleton d'une lunette, et
  ses données (réticule et sa couleur, teinte de la vitre, hauteur de l'axe, distance de l'œil,
  grossissement ; arme d'un chargeur...).
- <CODE>_Reticule.png, pour un viseur : l'image de son réticule (viseurs.ecrire_reticule).

L'importeur d'Unity (Duel Arena > Armes > Importer les armes de Blender) en fait les préfabs et le
catalogue des accessoires.
"""

import argparse
import json
import os
import sys
import time

import bpy

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import exporter_armes as ex  # noqa: E402

SCRIPTS = ["viseurs.py", "laser_tactique.py", "poignees.py", "chargeurs.py"]


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie", required=True)
    p.add_argument("--scripts", default="")
    p.add_argument("--seuls", default="", help="codes des accessoires (PR-A,CG-FA07...)")
    p.add_argument("--resolution", type=int, default=512)
    p.add_argument("--echantillons", type=int, default=64)
    p.add_argument("--marge", type=int, default=6)
    args = p.parse_args(brut)
    # Blender n'enregistre pas une image à un chemin relatif, sans rien dire (2026-10-16) : les
    # occlusions des accessoires n'étaient jamais parties.
    args.sortie = os.path.abspath(args.sortie)
    return args


def ateliers_de(script):
    """Exécute le script d'une famille d'accessoires (sans rendu ni enregistrement), rend ses ateliers."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__accessoire__", "__file__": chemin, "ARGS": []}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    return espace["ATELIERS"]


def exporter(a, args):
    debut = time.time()
    a.classe = None                  # ce que lisent les fonctions de l'export des armes
    if not hasattr(a, "bouche"):     # la suie : loin, sauf sur ce qui a une « bouche » (les électrodes du gant)
        a.bouche = (9.0, 9.0)
    scene = a.scene
    objets = [o for o in ex.descendants(a.racine) if o not in a.controle]
    vitres = [o for o in objets if o in a.vitres]
    objets_mesh = [o for o in objets if o.type == "MESH" and o not in vitres]
    assert a.racine.location.length < 1e-9, "la racine de l'accessoire doit être à l'origine"
    for ob in objets:
        assert ob is a.racine or ob.matrix_parent_inverse.is_identity, ob.name
    for ob in a.controle:            # le réticule des rendus de contrôle ne part pas
        ob.hide_render = True

    dossier = os.path.join(args.sortie, a.prefixe)
    os.makedirs(dossier, exist_ok=True)
    materiaux, remplacements = ex.lire_finitions(a, objets_mesh)
    for ob in objets_mesh:
        ex.bord_en_couleur(ob)
    ex.deplier(scene, objets_mesh)
    for ob in vitres:                # la vitre n'arrête pas la lumière de l'occlusion
        ob.hide_render = True
    try:
        ao = ex.cuire_occlusion(scene, objets_mesh, os.path.join(dossier, f"{a.prefixe}_Occlusion.png"),
                                args.resolution, args.echantillons, args.marge)
    finally:
        for ob in vitres:
            ob.hide_render = False
    ex.vers_axes_unity(a, objets)
    ex.exporter_fbx(scene, a, objets, os.path.join(dossier, f"{a.prefixe}_Modele.fbx"))

    triangles = sum(ex.ca_kit().triangles(o) for o in objets_mesh)
    if hasattr(a, "avant_fiche"):    # ce que l'accessoire écrit de plus (l'image du réticule d'un viseur)
        a.avant_fiche(dossier)
    fiche = {
        "code": a.code, "racine": a.prefixe, "titre": a.titre, "categorie": a.categorie,
        "vitres": [ex.nom_final(a, o) for o in vitres],
        # l'œilleton d'une lunette : seul visible en visée, la vue grossie (FirstPersonView)
        "oculaire": [ex.nom_final(a, o) for o in getattr(a, "oculaire", [])],
        "donnees": a.donnees,
        "triangles": triangles, "pieces": len(objets_mesh), "resolution": args.resolution,
        "finitions": ex.FINITIONS, "materiaux": materiaux, "remplacements": remplacements,
    }
    with open(os.path.join(dossier, f"{a.prefixe}_Finitions.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(fiche, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"EXPORT {a.code:<8} {len(objets_mesh):>2} pièces {triangles:>5} triangles, {len(materiaux)} matériaux, "
          f"occlusion moyenne {ao:.3f}, {time.time() - debut:.0f} s")


def principal():
    args = arguments()
    scripts = [s for s in args.scripts.split(",") if s] or SCRIPTS
    seuls = [c for c in args.seuls.split(",") if c]
    for script in scripts:
        for a in ateliers_de(script):
            if not seuls or a.code in seuls:
                exporter(a, args)


principal()
