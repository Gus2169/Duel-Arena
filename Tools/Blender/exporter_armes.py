"""Exporte les armes de Duel Arena vers Unity (Blender 5.2) : la géométrie, l'occlusion cuite et
les paramètres des six finitions de chacune (GDD § 6).

  blender -b --factory-startup --python-exit-code 1 -P exporter_armes.py -- --sortie <dossier>
          [--armes fusil_assaut.py,pistolet.py] [--resolution 1024] [--echantillons 64]

Pour chaque arme, dans <dossier>/<CODE>/ (FA07, FX1...) :
- <CODE>_Modele.fbx : les pièces, les repères (Mire, Bouche, MainGauche, MainDroite) et les points
  d'accroche (Accessoires/...), sans le préfixe de l'arme. Dans Unity : canon vers +Z, haut +Y, en
  mètres, sans rotation ni échelle sur les objets (transformations appliquées). Chaque pièce porte
  un dépliage UV commun à toute l'arme (Smart UV Project), qui ne sert qu'à l'occlusion, et
  l'attribut « bord » de ses chanfreins en couleur de sommet (le rouge) : le shader d'Unity y met
  le métal nu, comme les matériaux d'ici.
- <CODE>_Occlusion.png : le nœud Ambient Occlusion des matériaux (1,8 cm), cuit par Cycles dans ce
  dépliage, en niveaux de gris linéaires. C'est la seule part de l'usure qui ne se recalcule pas
  dans Unity : tout le reste (bruits, éclats, rayures, rouille, huile...) y est refait par le
  shader DuelArena/ArmeUsee, à partir des mêmes paramètres.
- <CODE>_Finitions.json : ces paramètres (ceux que kit.materiau reçoit ici), pour chaque matériau
  de l'arme et chaque finition, et les pièces que la finition « dépareillée » remplace. Les
  positions (suie, zones des mains) y sont dans le repère de l'arme de Blender, comme les
  coordonnées « Object » des matériaux : le shader y revient avant de calculer l'usure.

L'export ne touche ni aux scripts des armes ni à Armes.blend. L'importeur d'Unity (menu Duel Arena >
Armes > Importer les armes de Blender) en fait les préfabs et les matériaux.
"""

import argparse
import inspect
import json
import math
import os
import random
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_armes  # noqa: E402

# Dans l'ordre de l'énumération WeaponFinish d'Unity (ajouter les nouvelles à la fin).
FINITIONS = ["", "repeinte", "rouillee", "depareillee", "polie", "encrassee"]
FAMILLES = ["stock", "serie30", "laser"]
CLASSE_PISTOLET = 3
# Sur un pistolet, le point « Poignee » est le manchon de la poignée, pas une poignée avant :
# l'armurerie d'Unity n'y accroche pas de poignée verticale.
POINTS_PISTOLET = {"Poignee": "Manchon"}
# Des pièces portent le nom d'un repère : la poignée pistolet s'appelle « Poignee » comme le point
# d'accroche de la poignée avant, la bouche du FAP-30 ou du RV-30 « Bouche » comme le bout du canon
# (Blender numérote alors le second « .001 »). Dans Unity, chacun garde un nom propre, et le repère
# le sien : WeaponModel cherche Mire et Bouche parmi les enfants de la racine.
RENOMMAGES_PIECES = {"Poignee": "PoigneePistolet", "Bouche": "BoucheCanon"}
REPERES = {"Mire", "Bouche", "MainGauche", "MainDroite", "Accessoires"}
DISTANCE_AO = 0.018   # celle du nœud Ambient Occlusion de kit.materiau

# Les bruits de kit.materiau, dans l'ordre où il les crée, avec le générateur qui tire leur décalage
# (1 : random.Random(graine) ; 2 : random.Random(graine + 7919), celui des marques d'un long service)
# et ce qui les fait exister. Les noms sont ceux des propriétés _Dec… du shader DuelArena/ArmeUsee :
# il y retrouve les motifs de Blender au même endroit. L'export vérifie cet ordre sur les matériaux
# que kit.materiau construit vraiment (verifier_decalages).
_DESSOUS = lambda p, cle: p["dessous"] is not None and p[cle] > 0  # noqa: E731
CANAUX = [
    ("Variation", 1, lambda p: bool(p["variation"])),
    ("Delavage", 2, lambda p: bool(p["delavage"])),
    ("Bords", 1, lambda p: _DESSOUS(p, "bords")),
    ("Eclats", 1, lambda p: _DESSOUS(p, "eclats")),
    ("ZoneEclats", 1, lambda p: _DESSOUS(p, "eclats")),
    ("ZoneRayures", 1, lambda p: _DESSOUS(p, "rayures")),
    ("Rayures1", 1, lambda p: _DESSOUS(p, "rayures")),
    ("Rayures2", 1, lambda p: _DESSOUS(p, "rayures")),
    ("Retouches", 2, lambda p: bool(p["retouches"])),
    ("Rouille", 2, lambda p: bool(p["rouille"])),
    ("Cloques", 2, lambda p: bool(p["rouille"])),
    ("ZoneCloques", 2, lambda p: bool(p["rouille"])),
    ("Coulures", 2, lambda p: bool(p["rouille"])),
    ("ZoneCoulures", 2, lambda p: bool(p["rouille"])),
    ("Huile", 2, lambda p: bool(p["huile"])),
    ("HuileDetail", 2, lambda p: bool(p["huile"])),
    ("Poussiere", 2, lambda p: bool(p["poussiere"])),
    ("Suie", 1, lambda p: p["suie"] is not None),
]


def arguments():
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie", required=True)
    p.add_argument("--armes", default="")
    p.add_argument("--resolution", type=int, default=1024)
    p.add_argument("--echantillons", type=int, default=64)
    p.add_argument("--marge", type=int, default=8)
    args = p.parse_args(brut)
    # Blender n'enregistre pas une image à un chemin relatif, sans rien dire (2026-10-16) : les
    # occlusions des accessoires n'étaient jamais parties.
    args.sortie = os.path.abspath(args.sortie)
    return args


def construire(script):
    """Exécute le script d'une arme (sans rendu ni enregistrement) et rend son atelier."""
    chemin = os.path.join(DOSSIER, script)
    espace = {"__name__": "__arme__", "__file__": chemin, "ARGS": []}
    with open(chemin, encoding="utf-8") as f:
        exec(compile(f.read(), chemin, "exec"), espace)
    return espace["ATELIER"]


# --- paramètres des matériaux ------------------------------------------------------------------

def couleur(c):
    c = tuple(c) if c is not None else (0.0, 0.0, 0.0, 1.0)
    return [float(x) for x in (list(c) + [1.0])[:4]]


def vecteur(v):
    return [float(v[0]), float(v[1]), float(v[2])]


def decalages(p):
    """Les décalages que kit.materiau tire pour chacun de ses bruits (voir CANAUX) ; (0, 0, 0) pour un
    bruit que ce matériau n'a pas."""
    generateurs = {1: random.Random(p["graine"]), 2: random.Random(p["graine"] + 7919)}
    resultat = {}
    for nom, n, existe in CANAUX:
        if existe(p):
            g = generateurs[n]
            resultat[nom] = (g.uniform(-50, 50), g.uniform(-50, 50), g.uniform(-50, 50))
        else:
            resultat[nom] = None
    return resultat


def verifier_decalages(kit, base, kw, attendus):
    """Les décalages de CANAUX sont-ils ceux des nœuds Mapping que kit.materiau crée, dans l'ordre ?"""
    mat = kit.materiau("DA_Verification_Decalages", base, **kw)
    lus = [tuple(n.inputs["Location"].default_value) for n in mat.node_tree.nodes if n.bl_idname == "ShaderNodeMapping"]
    voulus = [d for nom, _, _ in CANAUX if (d := attendus[nom]) is not None]
    assert len(lus) == len(voulus), (len(lus), len(voulus))
    for a, b in zip(lus, voulus):
        assert max(abs(x - y) for x, y in zip(a, b)) < 1e-4, (a, b)


def parametres(kit, nom, base, kw):
    """Les paramètres complets d'un matériau, défauts de kit.materiau compris, dans la forme que lit
    l'importeur d'Unity (WeaponImporter.Params)."""
    p = {k: v.default for k, v in inspect.signature(kit.materiau).parameters.items()
         if v.default is not inspect.Parameter.empty}
    inconnus = set(kw) - set(p)
    assert not inconnus, (nom, inconnus)
    p.update(kw)
    decs = decalages(p)
    verifier_decalages(kit, base, kw, decs)
    dessous, couche, suie, emission = p["dessous"], p["couche"], p["suie"], p["emission"]
    return {
        "nom": nom,
        "couleur": couleur(base), "rug": float(p["rug"]), "metal": float(p["metal"]),
        "aDessous": dessous is not None, "dessous": couleur(dessous),
        "rugDessous": float(p["rug_dessous"]), "metalDessous": float(p["metal_dessous"]),
        "bords": float(p["bords"]), "eclats": float(p["eclats"]), "rayures": float(p["rayures"]),
        "salete": float(p["salete"]), "variation": float(p["variation"]),
        "echelleEclats": float(p["echelle_eclats"]), "graine": int(p["graine"]), "coat": float(p["coat"]),
        "emission": couleur(emission), "forceEmission": float(p["force_emission"]) if emission is not None else 0.0,
        "aSuie": suie is not None,
        "suie": vecteur(suie[0]) + [float(suie[1])] if suie is not None else [0.0, 0.0, 0.0, 0.0],
        "aCouche": couche is not None, "couche": couleur(couche[0] if couche else None),
        "rugCouche": float(couche[1]) if couche else 0.0,
        "retouches": float(p["retouches"]), "teinteRetouches": couleur(p["teinte_retouches"]),
        "rouille": float(p["rouille"]), "teinteRouille": couleur(p["teinte_rouille"]),
        "huile": float(p["huile"]), "poussiere": float(p["poussiere"]), "delavage": float(p["delavage"]),
        "zones": [{"centre": vecteur(c), "rayon": float(r), "force": float(f)} for c, r, f in (p["zones"] or [])],
        # les décalages, à la suite, dans l'ordre de CANAUX (trois nombres par bruit)
        "decalages": [float(x) for nom, _, _ in CANAUX for x in (decs[nom] or (0.0, 0.0, 0.0))],
    }


def lire_finitions(at, objets_mesh):
    """Les matériaux de l'arme dans chaque finition, et les remplacements de la « dépareillée »."""
    ca = sys.modules["commun_armes"]
    kit = ca.kit
    mains = ca.mains_de(at)
    fiches = []
    for f in FINITIONS:
        def fabrique(nom, base, **kw):
            return parametres(kit, nom, base, kw)
        fiches.append(ca.materiaux(at.prefixe, at.bouche, f or None, mains, fabrique=fabrique))
    origine = fiches[0]
    cle_de = {fiche["nom"]: cle for cle, fiche in origine.items()}

    utilisees, remplacements = [], []
    regles = ca.REMPLACEMENTS_USURE.get("depareillee", {})
    for ob in objets_mesh:
        cles = []
        for slot in ob.material_slots:
            assert slot.material is not None and slot.material.name in cle_de, (ob.name, slot.material)
            cles.append(cle_de[slot.material.name])
        remplace = {}
        for motif, regle in regles.items():   # comme Atelier.objet : la dernière règle qui vaut l'emporte
            if motif in ob.name:
                for k in cles:
                    if k in regle:
                        remplace[k] = regle[k]
        for k in cles + list(remplace.values()):
            if k not in utilisees:
                utilisees.append(k)
        nom = nom_final(at, ob)
        remplacements += [{"piece": nom, "de": k, "vers": v} for k, v in remplace.items()]

    materiaux = []
    for cle in utilisees:
        # une clé propre à une finition (la carcasse grise de la dépareillée) reprend ses paramètres
        # dans les autres, où rien ne l'utilise
        repli = next(fiche[cle] for fiche in fiches if cle in fiche)
        materiaux.append({
            "cle": cle,
            "nom": origine[cle]["nom"] if cle in origine else repli["nom"],
            "finitions": [fiche.get(cle, repli) for fiche in fiches],
        })
    return materiaux, remplacements


# --- préparation des pièces ---------------------------------------------------------------------

def court(at, nom):
    """Le nom d'un objet sans le préfixe de son arme (et sans le « .001 » d'un doublon)."""
    base = nom.split(".")[0]
    debut = at.prefixe + "_"
    return base[len(debut):] if base.startswith(debut) else base


def nom_final(at, ob):
    """Le nom de l'objet dans Unity (voir POINTS_PISTOLET et RENOMMAGES_PIECES)."""
    nom = court(at, ob.name)
    if ob.type == "MESH":
        nom = RENOMMAGES_PIECES.get(nom, nom)
        assert nom not in REPERES, f"la pièce {ob.name} porte le nom d'un repère : à ajouter à RENOMMAGES_PIECES"
        return nom
    if at.classe == CLASSE_PISTOLET and ob.parent is not None and court(at, ob.parent.name) == "Accessoires":
        return POINTS_PISTOLET.get(nom, nom)
    return nom


def descendants(ob):
    return [ob] + list(ob.children_recursive)


def bord_en_couleur(ob):
    """Recopie l'attribut de face « bord » (1 sur les chanfreins) dans une couleur de sommet, par coin
    de face : c'est elle que lit le shader d'Unity."""
    me = ob.data
    n_faces = len(me.polygons)
    valeurs = np.zeros(n_faces, dtype=np.float32)
    attr = me.attributes.get("bord")
    if attr is not None and attr.domain == "FACE":
        attr.data.foreach_get("value", valeurs)
    rgba = np.zeros((len(me.loops), 4), dtype=np.float32)
    for poly in me.polygons:
        rgba[poly.loop_start:poly.loop_start + poly.loop_total, 0] = valeurs[poly.index]
    rgba[:, 3] = 1.0
    for ancienne in list(me.color_attributes):
        me.color_attributes.remove(ancienne)
    col = me.color_attributes.new("Bord", "FLOAT_COLOR", "CORNER")
    col.data.foreach_set("color", rgba.ravel())
    me.color_attributes.active_color = col


def contexte(scene, objets, actif):
    vl = scene.view_layers[0]
    fenetres = bpy.context.window_manager.windows
    if fenetres:
        fenetres[0].scene = scene
    for ob in vl.objects:
        ob.select_set(False, view_layer=vl)
    for ob in objets:
        ob.select_set(True, view_layer=vl)
    vl.objects.active = actif
    return dict(window=fenetres[0] if fenetres else None, scene=scene, view_layer=vl, active_object=actif,
                object=actif, selected_objects=list(objets), selected_editable_objects=list(objets))


def deplier(scene, objets_mesh):
    """Un dépliage commun à toutes les pièces, à densité égale (elles sont toutes à l'échelle 1)."""
    for ob in objets_mesh:
        me = ob.data
        while me.uv_layers:
            me.uv_layers.remove(me.uv_layers[0])
        me.uv_layers.new(name="UVMap")
    ctx = contexte(scene, objets_mesh, objets_mesh[0])
    with bpy.context.temp_override(**ctx):
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.006, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")


def cuire_occlusion(scene, objets_mesh, chemin, resolution, echantillons, marge):
    """L'occlusion du nœud Ambient Occlusion des matériaux, cuite par Cycles dans le dépliage."""
    image = bpy.data.images.new(os.path.splitext(os.path.basename(chemin))[0], resolution, resolution,
                                alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    mat = bpy.data.materials.new("DA_Cuisson_Occlusion")
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    emission = nt.nodes.new("ShaderNodeEmission")
    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 16
    ao.inputs["Distance"].default_value = DISTANCE_AO
    nt.links.new(ao.outputs["AO"], emission.inputs["Color"])
    nt.links.new(emission.outputs["Emission"], sortie.inputs["Surface"])
    cible = nt.nodes.new("ShaderNodeTexImage")
    cible.image = image
    nt.nodes.active = cible
    cible.select = True

    sauvegarde = {ob: [slot.material for slot in ob.material_slots] for ob in objets_mesh}
    for ob in objets_mesh:
        for slot in ob.material_slots:
            slot.material = mat
    moteur = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = echantillons
    scene.cycles.use_denoising = False
    scene.render.bake.margin = marge
    scene.render.bake.margin_type = "EXTEND"
    scene.render.bake.use_clear = True
    scene.render.bake.target = "IMAGE_TEXTURES"
    try:
        ctx = contexte(scene, objets_mesh, objets_mesh[0])
        with bpy.context.temp_override(**ctx):
            bpy.ops.object.bake(type="EMIT", margin=marge, use_clear=True)
    finally:
        for ob, mats in sauvegarde.items():
            for slot, m in zip(ob.material_slots, mats):
                slot.material = m
        scene.render.engine = moteur
        bpy.data.materials.remove(mat)

    # en 8 bits linéaires : l'occlusion est une donnée, pas une couleur
    sortie_8 = bpy.data.images.new(image.name + "_8", resolution, resolution, alpha=False)
    sortie_8.colorspace_settings.name = "Non-Color"
    px = np.empty(resolution * resolution * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    sortie_8.pixels.foreach_set(px)
    sortie_8.filepath_raw = chemin
    sortie_8.file_format = "PNG"
    sortie_8.save()
    assert os.path.isfile(chemin), f"l'occlusion n'a pas été enregistrée : {chemin}"
    moyenne = float(px.reshape(-1, 4)[:, 0].mean())
    bpy.data.images.remove(image)
    bpy.data.images.remove(sortie_8)
    return moyenne


def vers_axes_unity(at, objets):
    """Fait pivoter l'arme de -90° autour de X, géométrie et repères (jamais leurs axes) : avec l'export
    sans transformation d'espace, Unity ne fait plus que retourner X. Sa racine et ses objets y arrivent
    alors sans rotation, le canon vers +Z, le haut vers +Y et la gauche du tireur vers -X ; et les
    coordonnées « Object » de chaque pièce y sont celles de l'arme. (Essayé le 2026-10-15 : « Apply
    Transform » de l'export tournait deux fois les objets imbriqués, la crosse partait vers le haut ; et
    l'export par défaut laisse une rotation de -90° sur la racine, que HeldWeapons écraserait.)"""
    rotation = Matrix.Rotation(-math.pi / 2, 4, "X")
    for ob in objets:
        if ob is at.racine:
            continue
        if ob.type == "MESH":
            ob.data.transform(rotation)
        ob.location = rotation @ ob.location


def exporter_fbx(scene, at, objets, chemin):
    """Les objets sous leur nom court, exportés, puis rendus à leur nom (d'autres armes suivent)."""
    noms = {ob: ob.name for ob in objets}
    noms_maillages = {ob: ob.data.name for ob in objets if ob.type == "MESH"}
    finals = {ob: nom_final(at, ob) for ob in objets if ob is not at.racine}
    try:
        for ob, nouveau in finals.items():
            ob.name = nouveau
            if ob.type == "MESH":
                ob.data.name = nouveau
        doublons = sorted({ob.name for ob in objets if "." in ob.name})
        if doublons:
            print("  noms en double (gardent leur suffixe) :", doublons)
        ctx = contexte(scene, objets, at.racine)
        with bpy.context.temp_override(**ctx):
            bpy.ops.export_scene.fbx(
                filepath=chemin, check_existing=False, use_selection=True, object_types={"EMPTY", "MESH"},
                global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                bake_space_transform=False, axis_forward="-Z", axis_up="Y", use_space_transform=False,
                use_mesh_modifiers=True, mesh_smooth_type="OFF", use_triangles=True, use_tspace=False,
                colors_type="LINEAR", prioritize_active_color=True, use_custom_props=False,
                add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    finally:
        for ob, nom in noms.items():
            ob.name = nom
        for ob, nom in noms_maillages.items():
            ob.data.name = nom


def exporter(script, classe, famille, a):
    debut = time.time()
    at = construire(script)
    at.classe = classe
    scene = at.scene
    objets = descendants(at.racine)
    objets_mesh = [ob for ob in objets if ob.type == "MESH"]
    assert at.racine.location.length < 1e-9, "la racine de l'arme doit être à l'origine"
    for ob in objets:
        assert ob is at.racine or ob.matrix_parent_inverse.is_identity, ob.name

    dossier = os.path.join(a.sortie, at.prefixe)
    os.makedirs(dossier, exist_ok=True)
    materiaux, remplacements = lire_finitions(at, objets_mesh)
    for ob in objets_mesh:
        bord_en_couleur(ob)
    deplier(scene, objets_mesh)
    ao_moyenne = cuire_occlusion(scene, objets_mesh, os.path.join(dossier, f"{at.prefixe}_Occlusion.png"),
                                 a.resolution, a.echantillons, a.marge)
    vers_axes_unity(at, objets)
    exporter_fbx(scene, at, objets, os.path.join(dossier, f"{at.prefixe}_Modele.fbx"))

    triangles = sum(ca_kit().triangles(o) for o in objets_mesh)
    fiche = {
        "code": at.code, "racine": at.prefixe, "titre": at.titre, "classe": classe, "famille": famille,
        "nomFamille": FAMILLES[famille], "laser": bool(at.laser), "pistolet": classe == CLASSE_PISTOLET,
        "triangles": triangles, "pieces": len(objets_mesh), "resolution": a.resolution,
        "finitions": FINITIONS, "materiaux": materiaux, "remplacements": remplacements,
    }
    with open(os.path.join(dossier, f"{at.prefixe}_Finitions.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(fiche, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"EXPORT {at.code:<7} {len(objets_mesh):>3} pièces {triangles:>5} triangles, {len(materiaux)} matériaux, "
          f"{len(remplacements)} remplacements, occlusion moyenne {ao_moyenne:.3f}, {time.time() - debut:.0f} s")


def ca_kit():
    return sys.modules["commun_armes"].kit


def principal():
    a = arguments()
    voulues = [s for s in a.armes.split(",") if s]
    for classe, scripts in enumerate(commun_armes.CLASSES):
        for famille, script in enumerate(scripts):
            if script and (not voulues or script in voulues):
                exporter(script, classe, famille, a)


if __name__ == "__main__":   # lancé par Blender ; exporter_accessoires.py l'importe pour ses fonctions
    principal()
