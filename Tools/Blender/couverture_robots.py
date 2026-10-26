"""Quelle part de la surface touchable du jeu chaque robot couvre-t-il à l'écran ? (2026-10-26)

Demandé par l'utilisateur (« le collider est-il le même entre les deux robots ? l'OS-02 me paraît plus fin, donc
plus dur à toucher ») : la surface touchable est la même pour tous (PlayerHitbox, calculée depuis la posture et le
lean, jamais depuis le modèle), mais un robot plus fin la montre moins. Des rayons parallèles (une vue orthographique,
un tous les 4 mm), de face et de profil, sur chaque robot posé dans l'attente au fusil (le clip du soldat, comme la
vue héroïque), sans l'arme : pour chaque zone touchable (la tête, une sphère ; le torse et les jambes, des capsules),
la part de sa silhouette où un rayon rencontre le robot. Puis deux rendus : la silhouette des zones en rouge derrière
les robots, leur contour devant.

Relevé le 2026-10-26 (« toute » = les trois zones) : de face, Y Bot 69,6 %, CT-01 67,6 % ; de profil, 65,8 et 62,6 %.
L'OS-02 n'en couvrait que 52,8 et 51,4 % ; épaissi, 67,6 et 62,4 %, à 0,7 point près sur chaque zone.

  blender -b --factory-startup -P couverture_robots.py -- <dossier de sortie> [couleur] [--seuls CT-01,OS-02] [--sans-rendu]

--seuls : les robots mesurés (« Y BOT », « CT-01 », « OS-02 ») ; chacun est comparé au CT-01, la référence, zone par
zone, s'il est mesuré. --sans-rendu : les chiffres seuls, pour itérer.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

BLENDER = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BLENDER)
sys.dont_write_bytecode = True
import commun_robots as cr  # noqa: E402
import kit_lowpoly as kit  # noqa: E402
import robot_ct01  # noqa: E402
import robot_os02  # noqa: E402

brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SEULS = None
SANS_RENDU = "--sans-rendu" in brut
if "--seuls" in brut:
    SEULS = set(brut[brut.index("--seuls") + 1].split(","))
    del brut[brut.index("--seuls"):brut.index("--seuls") + 2]
args = [a for a in brut if not a.startswith("--")]
SORTIE = os.path.abspath(args[0]) if args else os.path.abspath("couverture")
COULEUR = args[1] if len(args) > 1 else "cyan"
os.makedirs(SORTIE, exist_ok=True)
PAS = 0.004

scene = bpy.data.scenes.new("Couverture")
usine = bpy.data.scenes.get("Scene")
if usine is not None and usine != scene:
    for o in list(usine.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(usine)
scene.render.engine = "BLENDER_EEVEE"

# --- les trois robots, à l'origine ---------------------------------------------------------------
robots = []
for module in (robot_ct01, robot_os02):
    if SEULS is not None and module.MODELE.code not in SEULS:
        continue
    rb, ob, col = cr.construire(module.MODELE, scene, COULEUR, skin="vierge")
    robots.append({"nom": module.MODELE.code, "arm": rb.arm, "objets": [ob], "M": rb.M})
col_y = kit.collection("Y Bot", scene.collection)
avant = set(bpy.data.objects)
with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
    bpy.ops.import_scene.fbx(filepath=cr.SOURCE, automatic_bone_orientation=False, ignore_leaf_bones=False)
nouveaux = [o for o in bpy.data.objects if o not in avant]
for o in nouveaux:
    for c in list(o.users_collection):
        c.objects.unlink(o)
    col_y.objects.link(o)
if SEULS is None or "Y BOT" in SEULS:
    robots.insert(0, {"nom": "Y BOT", "arm": next(o for o in nouveaux if o.type == "ARMATURE"),
                      "objets": [o for o in nouveaux if o.type == "MESH"], "M": None})
else:
    for o in nouveaux:
        bpy.data.objects.remove(o, do_unlink=True)

action = cr.charger_attente(cr.ATTENTE_FUSIL)
for r in robots:
    cr.poser(r["arm"], action, 1)
scene.frame_set(1)
dg = scene.view_layers[0].depsgraph
dg.update()


def maillage_monde(objets):
    verts, polys = [], []
    for ob in objets:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        M = ev.matrix_world.copy()
        base = len(verts)
        verts.extend(M @ v.co for v in me.vertices)
        polys.extend([base + i for i in p.vertices] for p in me.polygons)
        ev.to_mesh_clear()
    return verts, polys


# Les pieds sur le sol : le clip du soldat porte la hauteur de son propre bassin (dans le jeu, le
# reciblage humanoïde l'adapte au robot).
for r in robots:
    verts, _ = maillage_monde(r["objets"])
    bas = min(v.z for v in verts)
    r["arm"].location.z -= bas
    r["recale_cm"] = round(-100 * bas, 1)
scene.frame_set(1)
dg.update()


# --- la mesure -----------------------------------------------------------------------------------
def zones_2d(vue):
    """(nom, a, b, r) dans le plan de la vue : de face (x, z), de profil (y, z)."""
    sortie = []
    for nom, (a, b, r) in (("tête", cr.TETE_TOUCHABLE), ("torse", cr.TORSE_TOUCHABLE), ("jambes", cr.JAMBES_TOUCHABLES)):
        if vue == "face":
            sortie.append((nom, (a.x, a.z), (b.x, b.z), r))
        else:
            sortie.append((nom, (a.y, a.z), (b.y, b.z), r))
    return sortie


def dans_stade(h, z, a, b, r):
    abh, abz = b[0] - a[0], b[1] - a[1]
    l2 = abh * abh + abz * abz
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((h - a[0]) * abh + (z - a[1]) * abz) / l2))
    dh, dz = h - (a[0] + abh * t), z - (a[1] + abz * t)
    return dh * dh + dz * dz <= r * r


def mesurer(bvh, vue):
    zs = zones_2d(vue)
    par_zone = {nom: [0, 0] for nom, *_ in zs}
    union, dehors = [0, 0], 0
    n_h, n_z = int(1.2 / PAS), int(1.9 / PAS)
    direction = Vector((0, 1, 0)) if vue == "face" else Vector((1, 0, 0))
    for i in range(n_h):
        h = -0.6 + (i + 0.5) * PAS
        for j in range(n_z):
            z = (j + 0.5) * PAS
            origine = Vector((h, -3.0, z)) if vue == "face" else Vector((-3.0, h, z))
            touche = bvh.ray_cast(origine, direction, 6.0)[0] is not None
            dedans = False
            for nom, a, b, r in zs:
                if dans_stade(h, z, a, b, r):
                    par_zone[nom][0] += 1
                    par_zone[nom][1] += touche
                    dedans = True
            if dedans:
                union[0] += 1
                union[1] += touche
            elif touche:
                dehors += 1
    pc = {nom: round(100.0 * t / max(n, 1), 1) for nom, (n, t) in par_zone.items()}
    pc["toute"] = round(100.0 * union[1] / max(union[0], 1), 1)
    pc["hors_zones_cm2"] = round(dehors * PAS * PAS * 1e4)
    return pc


resultats = {}
for r in robots:
    verts, polys = maillage_monde(r["objets"])
    bvh = BVHTree.FromPolygons(verts, polys)
    resultats[r["nom"]] = {"face": mesurer(bvh, "face"), "profil": mesurer(bvh, "profil"), "recale_cm": r["recale_cm"]}
    print("COUVERTURE", r["nom"], resultats[r["nom"]])
with open(os.path.join(SORTIE, "couverture.json"), "w", encoding="utf-8") as f:
    json.dump(resultats, f, ensure_ascii=False, indent=1)
if "CT-01" in resultats:
    reference = resultats["CT-01"]
    for nom, res in resultats.items():
        if nom == "CT-01":
            continue
        for vue in ("face", "profil"):
            ecarts = "  ".join(f"{z} {res[vue][z] - reference[vue][z]:+.1f}" for z in ("tête", "torse", "jambes", "toute"))
            print(f"ECART {nom} / CT-01, {vue} : {ecarts}")
if SANS_RENDU:
    raise SystemExit(0)

# --- le rendu ------------------------------------------------------------------------------------
col_r = kit.collection("Rendus", scene.collection)
repere = cr.repere_usure(col_r)
for r in robots:
    if r["M"]:
        for m in set(r["M"].values()):
            cr.ancrer(m, repere)
monde = bpy.data.worlds.new("Couverture_Monde")
fond = monde.node_tree.nodes.get("Background")
fond.inputs["Color"].default_value = (0.012, 0.013, 0.016, 1)
fond.inputs["Strength"].default_value = 1.0
scene.world = monde
scene.view_settings.view_transform = "Standard"


def emission(nom, couleur):
    mat = bpy.data.materials.new(nom)
    nt = mat.node_tree
    nt.nodes.clear()
    s = nt.nodes.new("ShaderNodeOutputMaterial")
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = couleur
    nt.links.new(e.outputs["Emission"], s.inputs["Surface"])
    return mat


ROUGE = emission("Couverture_Zone", (0.80, 0.04, 0.03, 1))
TRAIT = emission("Couverture_Contour", (1.0, 0.22, 0.16, 1))


def stade_points(a, b, r, n=40):
    dh, dz = b[0] - a[0], b[1] - a[1]
    longueur = math.hypot(dh, dz)
    uh, uz = (dh / longueur, dz / longueur) if longueur > 1e-9 else (0.0, 1.0)
    nh, nz = -uz, uh
    pts = []
    for centre, signe in ((b, 1), (a, -1)):
        for k in range(n + 1):
            t = math.pi * k / n
            c, s = math.cos(t), math.sin(t)
            pts.append((centre[0] + signe * r * (nh * c + uh * s), centre[1] + signe * r * (nz * c + uz * s)))
    return pts


def zones_3d(vue, decalage, profondeur):
    """La silhouette des zones (pleine, derrière) ou leur contour (devant), dans le plan de la vue."""
    for nom, a, b, r in zones_2d(vue):
        pts = stade_points(a, b, r)
        if vue == "face":
            monde_pts = [Vector((decalage + h, profondeur, z)) for h, z in pts]
        else:
            monde_pts = [Vector((profondeur, decalage + h, z)) for h, z in pts]
        if profondeur > 0:
            me = bpy.data.meshes.new(f"Zone_{nom}")
            me.from_pydata(monde_pts, [], [list(range(len(monde_pts)))])
            me.materials.append(ROUGE)
            col_r.objects.link(bpy.data.objects.new(f"Zone_{nom}", me))
        else:
            cu = bpy.data.curves.new(f"Contour_{nom}", "CURVE")
            cu.dimensions = "3D"
            cu.bevel_depth = 0.0028
            sp = cu.splines.new("POLY")
            sp.points.add(len(monde_pts) - 1)
            for p, q in zip(sp.points, monde_pts):
                p.co = (q.x, q.y, q.z, 1)
            sp.use_cyclic_u = True
            cu.materials.append(TRAIT)
            col_r.objects.link(bpy.data.objects.new(f"Contour_{nom}", cu))


def vider_rendus():
    for o in list(col_r.objects):
        if o.name.startswith(("Zone_", "Contour_", "Etiquette_", "Couverture_")):
            bpy.data.objects.remove(o, do_unlink=True)


def soleil(nom, direction, energie):
    li = bpy.data.lights.new(nom, "SUN")
    li.energy = energie
    li.angle = math.radians(3)
    ob = bpy.data.objects.new(nom, li)
    col_r.objects.link(ob)
    ob.rotation_euler = Vector(direction).normalized().to_track_quat("-Z", "Y").to_euler()


def camera(nom, position, direction, echelle):
    cd = bpy.data.cameras.new(nom)
    cd.type = "ORTHO"
    cd.ortho_scale = echelle
    cam = bpy.data.objects.new(nom, cd)
    col_r.objects.link(cam)
    cam.location = position
    cam.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    return cam


ECART = 1.3
base = {id(r["arm"]): r["arm"].location.copy() for r in robots}
for vue in ("face", "profil"):
    vider_rendus()
    for k, r in enumerate(robots):
        d = (k - 1) * ECART if vue == "face" else (1 - k) * ECART
        r["arm"].location = base[id(r["arm"])] + (Vector((d, 0, 0)) if vue == "face" else Vector((0, d, 0)))
        zones_3d(vue, d, 1.2)
        zones_3d(vue, d, -1.2)
        m = resultats[r["nom"]][vue]
        for texte, z, taille in ((r["nom"], -0.11, 0.075), (f"{m['toute']:.0f} % couvert", -0.21, 0.06)):
            et = cr.etiquette(col_r, texte, Vector((d, -1.3, z)) if vue == "face" else Vector((-1.3, d, z)), taille)
            et.name = "Etiquette_" + texte
            if vue == "profil":
                et.rotation_euler = (math.radians(90), 0, math.radians(-90))
    if vue == "face":
        soleil("Couverture_Cle", (0.35, 1.0, -0.55), 3.0)
        soleil("Couverture_Remplissage", (-0.6, 1.0, -0.2), 1.1)
        cam = camera("Couverture_Camera", Vector((0, -6, 0.92)), (0, 1, 0), 3.9)
    else:
        soleil("Couverture_Cle", (1.0, -0.35, -0.55), 3.0)
        soleil("Couverture_Remplissage", (1.0, 0.6, -0.2), 1.1)
        cam = camera("Couverture_Camera", Vector((-6, 0, 0.92)), (1, 0, 0), 3.9)
    scene.frame_set(1)
    cr.rendre(scene, cam, os.path.join(SORTIE, f"couverture_{vue}.png"), 1950, 1150, 32)
print("FINI")
