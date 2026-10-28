"""Exporte les props du décor vers Unity (Blender 5.2, 2026-10-27), par les fonctions de l'export des armes : la
couleur de sommet « bord », le dépliage et l'occlusion cuite, le passage dans les axes d'Unity, le FBX.

  blender -b --factory-startup --python-exit-code 1 -P exporter_decor.py -- --sortie <dossier>
          [--props CA1,CO1] [--resolution 512] [--echantillons 48]

Pour chaque prop, dans <sortie>/<CODE>/ :
- <CODE>_Modele.fbx : les pièces sans le préfixe du prop ; Unity = (-x, z, -y) de Blender, sans rotation ni échelle ;
- <CODE>_Occlusion.png : l'occlusion des matériaux (1,8 cm), cuite par Cycles ;
- <CODE>_Fiche.json : les paramètres de chaque matériau (ceux de kit.materiau, comme la fiche d'une arme, une seule
  finition), que l'importeur d'Unity écrit dans des matériaux DuelArena/Decor (Duel Arena > Arène > Importer les
  props de Blender).
"""

import argparse
import json
import os
import sys
import time

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
PARENT = os.path.dirname(DOSSIER)
for d in (DOSSIER, PARENT):
    if d not in sys.path:
        sys.path.insert(0, d)
import commun_decor as cd  # noqa: E402
import exporter_armes as ea  # noqa: E402

kit = cd.kit
SCRIPTS = ["caisses.py", "projecteurs.py", "balises.py", "marquages.py", "mobilier.py"]


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie", required=True)
    p.add_argument("--props", default="")
    p.add_argument("--resolution", type=int, default=512)
    p.add_argument("--echantillons", type=int, default=48)
    p.add_argument("--marge", type=int, default=6)
    a = p.parse_args(brut)
    a.sortie = os.path.abspath(a.sortie)   # Blender n'enregistre pas une image à un chemin relatif (2026-10-16)
    return a


def charger(script):
    """Exécute le script d'une famille de props (sans rendu) et rend son espace : PROPS, construire."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__decor__", "__file__": chemin}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    return espace


def fiches_materiaux():
    """Les paramètres de chaque matériau des props, par clé (les mêmes que kit.materiau reçoit)."""
    def fabrique(nom, base, **kw):
        return ea.parametres(kit, nom, base, kw)
    return cd.materiaux(fabrique)


def exporter(at, a, sans_occlusion=False):
    debut = time.time()
    scene = at.scene
    objets = ea.descendants(at.racine)
    objets_mesh = [ob for ob in objets if ob.type == "MESH"]
    assert at.racine.location.length < 1e-9, "la racine du prop doit être à l'origine"
    dossier = os.path.join(a.sortie, at.prefixe)
    os.makedirs(dossier, exist_ok=True)

    params = fiches_materiaux()
    cle_de = {p["nom"]: cle for cle, p in params.items()}
    utilisees = []
    for ob in objets_mesh:
        for slot in ob.material_slots:
            assert slot.material is not None and slot.material.name in cle_de, (ob.name, slot.material)
            cle = cle_de[slot.material.name]
            if cle not in utilisees:
                utilisees.append(cle)
    materiaux = [{"cle": cle, "nom": params[cle]["nom"], "finitions": [params[cle]]} for cle in utilisees]

    for ob in objets_mesh:
        ea.bord_en_couleur(ob)
    ea.deplier(scene, objets_mesh)
    occlusion = os.path.join(dossier, f"{at.prefixe}_Occlusion.png")
    ao = 1.0 if sans_occlusion else ea.cuire_occlusion(scene, objets_mesh, occlusion, a.resolution, a.echantillons, a.marge)
    ea.vers_axes_unity(at, objets)
    ea.exporter_fbx(scene, at, objets, os.path.join(dossier, f"{at.prefixe}_Modele.fbx"))

    triangles = sum(kit.triangles(o) for o in objets_mesh)
    fiche = {"code": at.code, "racine": at.prefixe, "titre": at.titre, "triangles": triangles,
             "pieces": len(objets_mesh), "resolution": a.resolution, "occlusion": not sans_occlusion,
             "finitions": [""], "materiaux": materiaux}
    with open(os.path.join(dossier, f"{at.prefixe}_Fiche.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(fiche, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"EXPORT {at.code:<5} {len(objets_mesh):>3} pièces {triangles:>5} triangles, {len(materiaux)} matériaux, "
          f"occlusion moyenne {ao:.3f}, {time.time() - debut:.0f} s")


def principal():
    a = arguments()
    voulus = [p for p in a.props.split(",") if p]
    for script in SCRIPTS:
        espace = charger(script)
        for code, _ in espace["PROPS"]:
            if voulus and code not in voulus:
                continue
            at = espace["construire"](code)
            exporter(at, a, sans_occlusion=getattr(at, "sans_occlusion", False))


if __name__ == "__main__":
    principal()
