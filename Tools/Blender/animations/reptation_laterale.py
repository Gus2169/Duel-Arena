"""La reptation de côté du robot allongé (2026-10-29), calculée sur le squelette du Y Bot pour Unity.

Allongé, la couche du haut du corps de l'Animator pose l'attente couchée (« Prone Idle ») sur le buste et les
bras : de ce clip, le jeu ne montre que le bassin (sa hauteur, son roulis, son cap, qui entraînent le buste) et
les jambes. Le robot avance à vitesse constante dans le jeu, et le déplacement de la racine est jeté : le bassin
avance donc ici à vitesse constante, et les pieds restent plantés au sol pendant leur appui. Ils passent l'un
après l'autre : la jambe du côté où l'on va s'écarte, genou plié vers l'extérieur, puis tire ; l'autre pousse,
puis revient, talon levé. Deux pas par cycle, sur la durée de la reptation arrière de Mixamo (1,9 s), pour que
l'arbre les mêle en diagonale sans les étirer. Le bassin et le buste au repos sont ceux de l'attente couchée,
relevés dans Unity sur le Y Bot : passer de l'attente à la reptation ne fait pas bouger le buste.

Deux clips, un par côté, calculés chacun (la jambe qui mène change de côté, le bassin de l'attente n'est pas
symétrique), exportés en FBX (le squelette seul) puis importés par Duel Arena > Animations > Importer la
reptation de côté.

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/animations/reptation_laterale.py -- \
    --fbx "Assets/_ProjectArt/Animations/Blender" [--rendus <dossier>] [--images 1,5,9,13,17,21,25,29]
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

DOSSIER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, DOSSIER)
import commun_robots as cr  # noqa: E402
import kit_lowpoly as kit  # noqa: E402
import robot_ct01  # noqa: E402

ATTENTE_COUCHEE = os.path.join(cr.PROJET, "Assets", "_ProjectArt", "Animations", "Ch35_nonPBR@Prone Idle.fbx")

# --- le rythme ---------------------------------------------------------------------------------------------
IPS = 30
IMAGES = 57                 # un cycle : 1,9 s, comme la reptation arrière (images 1 à 58, la dernière = la première)
PAS = 2                     # pas par cycle
VITESSE = 0.366             # m/s : la reptation (0,43) de côté (×0,85), pour une cadence de 1 dans le jeu
APPUI = 0.55                # part du pas où un pied reste planté ; l'autre part, il passe

# --- le bassin : celui de l'attente couchée sur le Y Bot (Unity, 2026-10-29), dans le repère de Blender -----
BASSIN = Vector((0.001, -0.003, 0.146))
BASSIN_X = Vector((0.997, 0.053, -0.061))
BASSIN_Y = Vector((0.060, -0.991, 0.121))
ROULIS = 3.0                # degrés : la hanche de la jambe qui passe se soulève
LACET = 2.0                 # degrés : le bassin tourne ses jambes vers le côté où l'on va pendant que la menante passe
SOULEVE = 0.010             # m : le bassin se soulève un peu à chaque passage

# --- les pieds, dans le repère du bassin (x vers la gauche du robot, y vers ses pieds) --------------------
CENTRE = {"Left": 0.22, "Right": -0.28}     # où se tient chaque cheville, de côté
ALLONGE = 0.815             # m, de la hanche à la cheville d'une jambe posée : presque tendue, le genou au ras du sol
HAUTEUR_CHEVILLE = 0.15                     # pied dressé, orteils au sol
PIED_INCLINAISON = 25.0                     # degrés sous l'horizontale, de la cheville vers la plante
GENOU_POSE = 0.25           # un genou posé tourne un peu vers l'extérieur
# la jambe qui mène s'écarte en grenouille, genou dehors ; l'autre revient talon levé, genou vers le bas et le dehors
PASSAGE = {"mene": {"avance": 0.18, "leve": 0.05, "genou_dehors": 1.0, "genou_bas": 0.25},
           "suit": {"avance": 0.06, "leve": 0.12, "genou_dehors": 0.7, "genou_bas": 0.7}}

HAUT_DU_CORPS = ["Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
                 "RightShoulder", "RightArm", "RightForeArm", "RightHand"]


def os_(n):
    return f"mixamorig:{n}"


def lisse(u):
    u = min(1.0, max(0.0, u))
    return u * u * (3.0 - 2.0 * u)


def arguments():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", default=None, help="dossier où écrire les deux FBX")
    p.add_argument("--rendus", default=None)
    p.add_argument("--images", default="1,5,9,13,17,21,25,29")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return p.parse_args(argv)


class Squelette:
    """Le squelette et ses poses calculées os par os, dans le repère de l'armature (en cm, comme le FBX du
    Y Bot) : M passe au monde de Blender (rotation de 90° autour de X, échelle 0,01)."""

    def __init__(self, arm):
        self.arm = arm
        self.M = arm.matrix_world.copy()
        self.Mi = self.M.inverted()
        self.repos = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
        self.longueur = {b.name: ((self.M @ b.tail_local) - (self.M @ b.head_local)).length for b in arm.data.bones}

    def pose_depuis_monde(self, rotation_monde, position_monde):
        """La matrice de pose (repère de l'armature) d'un os de rotation et de tête donnés dans le monde."""
        R = (self.Mi.to_3x3().normalized() @ rotation_monde).to_4x4()
        R.translation = self.Mi @ position_monde
        return R

    def base(self, nom, pose, poses):
        """La matrice « basis » (relative à la pose du parent et au repos) qui donne cette pose."""
        p = self.parent[nom]
        if p is None:
            return self.repos[nom].inverted() @ pose
        return (poses[p] @ self.repos[p].inverted() @ self.repos[nom]).inverted() @ pose

    def enfant(self, nom, poses, base=None):
        """La pose d'un os depuis celle de son parent et sa base (le repos si None)."""
        p = self.parent[nom]
        m = poses[p] @ self.repos[p].inverted() @ self.repos[nom]
        return m @ base if base is not None else m

    def monde(self, pose):
        return self.M @ pose


def repere(x, y):
    """La rotation de colonnes (x, y, x × y), x rendu perpendiculaire à y."""
    y = y.normalized()
    x = (x - y * x.dot(y)).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed()


def ik_jambe(hanche, cheville, pole, l1, l2):
    """Genou et repères de la cuisse et du tibia (axe Y le long de l'os, X la charnière du genou, comme les os
    de Mixamo) pour une cheville visée et un genou tourné vers `pole`."""
    d = cheville - hanche
    dist = min(max(d.length, abs(l1 - l2) + 1e-4), l1 + l2 - 1e-4)
    a = d.normalized()
    p = (pole - a * pole.dot(a)).normalized()
    cos_al = (l1 * l1 + dist * dist - l2 * l2) / (2.0 * l1 * dist)
    al = math.acos(min(1.0, max(-1.0, cos_al)))
    genou = hanche + (a * math.cos(al) + p * math.sin(al)) * l1
    cheville = hanche + a * dist
    charniere = a.cross(p).normalized()
    return genou, cheville, repere(charniere, genou - hanche), repere(charniere, cheville - genou)


def poser_haut_du_corps(arm, action, image):
    """Les rotations (bases) du haut du corps d'un clip à une image : l'attente couchée de Mixamo sur le soldat,
    copiée os par os (même squelette de Mixamo, mêmes repères au repos)."""
    cr.poser(arm, action, image)
    bpy.context.scene.frame_set(image)
    q = {n: arm.pose.bones[os_(n)].rotation_quaternion.copy() for n in HAUT_DU_CORPS if os_(n) in arm.pose.bones}
    arm.animation_data.action = None
    return q


def calculer(sq, sens, haut, pieds, nom):
    """L'action de la reptation vers la gauche (sens = 1) ou la droite (sens = -1). `haut` : les rotations du
    haut du corps ; `pieds` : la rotation de chaque pied dans le repère de son tibia (pied_sur_tibia)."""
    arm = sq.arm
    action = bpy.data.actions.new(nom)
    action.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = action
    duree_pas = IMAGES / IPS / PAS
    pas_monde = VITESSE * duree_pas
    derive = VITESSE * APPUI * duree_pas            # ce qu'un pied planté recule sous le bassin pendant son appui
    rot0 = repere(BASSIN_X, BASSIN_Y)
    menante = "Left" if sens > 0 else "Right"
    l1, l2 = sq.longueur[os_("LeftUpLeg")], sq.longueur[os_("LeftLeg")]
    precedent = {}
    for image in range(1, IMAGES + 2):
        t = (image - 1) / IPS
        phase = (t / duree_pas) % 1.0
        x_bassin = sens * VITESSE * t
        # le bassin : roulis vers la jambe qui passe, un peu de cap, un peu soulevé
        onde = math.sin(2.0 * math.pi * phase)
        roulis = math.radians(ROULIS) * onde
        lacet = -math.radians(LACET) * onde * sens
        R = Matrix.Rotation(lacet, 3, "Z") @ Matrix.Rotation(-roulis * sens, 3, "Y") @ rot0
        position = BASSIN + Vector((x_bassin, 0.0, SOULEVE * (0.5 - 0.5 * math.cos(4.0 * math.pi * phase))))
        poses = {}
        bases = {}
        poses[os_("Hips")] = sq.pose_depuis_monde(R, position)
        bases[os_("Hips")] = sq.base(os_("Hips"), poses[os_("Hips")], poses)
        # le haut du corps : l'attente couchée (le jeu le remplace de toute façon par sa couche du haut du corps)
        for n in HAUT_DU_CORPS:
            q = haut.get(n, Quaternion())
            b = q.to_matrix().to_4x4()
            poses[os_(n)] = sq.enfant(os_(n), poses, b)
            bases[os_(n)] = b
        for cote in ("Left", "Right"):
            signe = 1.0 if cote == "Left" else -1.0
            role = "mene" if cote == menante else "suit"
            debut = 0.0 if role == "mene" else 0.5
            u = (phase - debut) % 1.0                 # 0 à 1 sur le pas, l'envol d'abord
            envol = 1.0 - APPUI
            centre = CENTRE[cote]
            # dans le repère du bassin, un pied planté recule de `derive` (à l'opposé du sens) ; en l'air, il revient
            pose_x_atterri = centre + sens * derive / 2.0
            pose_x_decolle = centre - sens * derive / 2.0
            if u < envol:
                v = u / envol
                x = pose_x_decolle + (pose_x_atterri - pose_x_decolle) * lisse(v)
                leve = math.sin(math.pi * v)
            else:
                v = (u - envol) / APPUI
                x = pose_x_atterri + (pose_x_decolle - pose_x_atterri) * v
                leve = 0.0
            reglage = PASSAGE[role]
            hanche = sq.monde(sq.enfant(os_(f"{cote}UpLeg"), poses)).translation
            # posé, le pied reste où il est de côté ; vers l'arrière, il se tient à ALLONGE de la hanche (il pivote
            # sur ses orteils de quelques centimètres quand la hanche passe au-dessus)
            cx = x_bassin + x
            dx, dz = cx - hanche.x, HAUTEUR_CHEVILLE - hanche.z
            recul = math.sqrt(max(0.0, ALLONGE * ALLONGE - dx * dx - dz * dz))
            cheville = Vector((cx, hanche.y + recul - reglage["avance"] * leve, HAUTEUR_CHEVILLE + reglage["leve"] * leve))
            # le genou tourne vers l'extérieur dès que le pied quitte le sol, avant que la jambe plie : il ne
            # descend jamais sous sa hauteur de jambe posée
            w = min(1.0, 2.0 * leve)
            pole = Vector((signe * (GENOU_POSE + (reglage["genou_dehors"] - GENOU_POSE) * w), 0.0,
                           -(1.0 + (reglage["genou_bas"] - 1.0) * w)))
            genou, cheville, r_cuisse, r_tibia = ik_jambe(hanche, cheville, pole, l1, l2)
            nom_cuisse, nom_tibia = os_(f"{cote}UpLeg"), os_(f"{cote}Leg")
            poses[nom_cuisse] = sq.pose_depuis_monde(r_cuisse, hanche)
            bases[nom_cuisse] = sq.base(nom_cuisse, poses[nom_cuisse], poses)
            poses[nom_tibia] = sq.pose_depuis_monde(r_tibia, genou)
            bases[nom_tibia] = sq.base(nom_tibia, poses[nom_tibia], poses)
            # le pied, raide sur le tibia : dressé, orteils au sol, quand la jambe est au repos
            poses[os_(f"{cote}Foot")] = sq.pose_depuis_monde(r_tibia @ pieds[cote], cheville)
            bases[os_(f"{cote}Foot")] = sq.base(os_(f"{cote}Foot"), poses[os_(f"{cote}Foot")], poses)
        for cote in ("Left", "Right"):
            bases[os_(f"{cote}ToeBase")] = Matrix.Identity(4)
        for nom_os, b in bases.items():
            pb = arm.pose.bones[nom_os]
            q = b.to_quaternion().normalized()
            if nom_os in precedent and q.dot(precedent[nom_os]) < 0.0:
                q = -q
            precedent[nom_os] = q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=image)
            if sq.parent[nom_os] is None:
                pb.location = b.translation
                pb.keyframe_insert("location", frame=image)
    print(f"ACTION {nom} : {IMAGES + 1} images, pas de {pas_monde:.3f} m, appui {derive:.3f} m sous le bassin")
    controler(sq, action, nom)
    return action


def controler(sq, action, nom):
    """Les pieds plantés ne glissent pas (la cheville reste en place pendant chaque appui), les orteils
    touchent le sol sans s'y enfoncer, les genoux restent au-dessus, le cycle boucle."""
    arm = sq.arm
    cr.poser(arm, action, 1)
    scene = bpy.context.scene
    pb = arm.pose.bones
    M = arm.matrix_world
    mesures = []
    for image in range(1, IMAGES + 2):
        scene.frame_set(image)
        ligne = {"bassin": M @ pb[os_("Hips")].head}
        for cote in ("Left", "Right"):
            ligne[cote] = (M @ pb[os_(f"{cote}Foot")].head, M @ pb[os_(f"{cote}Leg")].head,
                           M @ pb[os_(f"{cote}Toe_End")].head, M @ pb[os_(f"{cote}Toe_End")].tail)
        mesures.append(ligne)
    for cote in ("Left", "Right"):
        chev = [m[cote][0] for m in mesures]
        poses_pied = [i for i in range(len(chev) - 1)
                      if abs(chev[i + 1].z - HAUTEUR_CHEVILLE) < 1e-3 and abs(chev[i].z - HAUTEUR_CHEVILLE) < 1e-3]
        glisse = max(abs(chev[i + 1].x - chev[i].x) for i in poses_pied)
        glisse_y = max(abs(chev[i + 1].y - chev[i].y) for i in poses_pied)
        orteils = [m[cote][2].z for m in mesures]
        genoux = [m[cote][1].z for m in mesures]
        print(f"CONTROLE {nom} {cote} : pied planté, de côté {glisse * 1000:.1f} mm/image, vers l'arrière {glisse_y * 1000:.1f} mm/image, "
              f"orteils de {min(orteils) * 100:+.1f} à {max(orteils) * 100:+.1f} cm, genou au plus bas {min(genoux) * 100:.1f} cm")
    d = mesures[-1]["bassin"] - mesures[0]["bassin"]
    print(f"CONTROLE {nom} : le bassin avance de ({d.x:+.3f}, {d.y:+.3f}, {d.z:+.3f}) m par cycle, "
          f"hauteur {min(m['bassin'].z for m in mesures):.3f} à {max(m['bassin'].z for m in mesures):.3f} m")


def pied_sur_tibia(sq, cote):
    """La rotation du pied dans le repère du tibia, prise sur la jambe au repos : pied dressé, la plante en
    arrière et vers le bas, aligné sur la jambe."""
    poses = {os_("Hips"): sq.pose_depuis_monde(repere(BASSIN_X, BASSIN_Y), BASSIN)}
    hanche = sq.monde(sq.enfant(os_(f"{cote}UpLeg"), poses)).translation
    dx, dz = CENTRE[cote] - hanche.x, HAUTEUR_CHEVILLE - hanche.z
    cheville = Vector((CENTRE[cote], hanche.y + math.sqrt(ALLONGE * ALLONGE - dx * dx - dz * dz), HAUTEUR_CHEVILLE))
    signe = 1.0 if cote == "Left" else -1.0
    _, _, _, r_tibia = ik_jambe(hanche, cheville, Vector((signe * GENOU_POSE, 0.0, -1.0)),
                                sq.longueur[os_("LeftUpLeg")], sq.longueur[os_("LeftLeg")])
    sol = (cheville - hanche)
    sol.z = 0.0
    sol.normalize()
    inc = math.radians(PIED_INCLINAISON)
    y = sol * math.cos(inc) + Vector((0.0, 0.0, -math.sin(inc)))
    r_pied = repere(Vector((-1.0, 0.0, 0.0)), y)
    return r_tibia.transposed() @ r_pied


def exporter(arm, action, chemin):
    """Le squelette seul et son action. En centimètres, comme les FBX de Mixamo (FBX_SCALE_NONE : l'échelle de
    l'armature, 0,01, passe dans les données) : la racine arrive à l'échelle 1 dans Unity, qui peut copier
    l'avatar du Y Bot sans écart (avec FBX_SCALE_ALL, celle des robots, la racine restait à 0,01)."""
    chemin = os.path.abspath(chemin)
    os.makedirs(os.path.dirname(chemin), exist_ok=True)
    scene = bpy.context.scene
    cr.poser(arm, action, 1)
    scene.frame_start, scene.frame_end = 1, IMAGES + 1
    vl = scene.view_layers[0]
    for o in vl.objects:
        o.select_set(False)
    arm.select_set(True)
    with bpy.context.temp_override(scene=scene, view_layer=vl, active_object=arm, selected_objects=[arm]):
        bpy.ops.export_scene.fbx(filepath=chemin, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                                 use_armature_deform_only=False, apply_scale_options="FBX_SCALE_NONE",
                                 axis_forward="-Z", axis_up="Y", bake_anim=True, bake_anim_use_all_bones=True,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                                 bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0, path_mode="STRIP")
    print("EXPORT", chemin)


def rendus(scene, sq, rb, col, actions, dossier, images):
    os.makedirs(dossier, exist_ok=True)
    cr.lumieres(col, Vector((0.0, 0.4, 0.2)))
    monde = bpy.data.worlds.new("Monde")
    monde.node_tree.nodes["Background"].inputs["Color"].default_value = kit.srgb(20, 21, 23)
    scene.world = monde
    studio = kit.collection("Studio", scene.collection)
    # un sol à carreaux de 10 cm : un pied planté ne doit pas glisser dessus
    sol = bpy.data.meshes.new("Sol")
    sol.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
    sob = bpy.data.objects.new("Sol", sol)
    studio.objects.link(sob)
    mat = bpy.data.materials.new("Sol_Carreaux")
    mat.use_nodes = True
    nt = mat.node_tree
    damier = nt.nodes.new("ShaderNodeTexChecker")
    damier.inputs["Scale"].default_value = 60.0
    damier.inputs["Color1"].default_value = kit.srgb(150, 150, 152)
    damier.inputs["Color2"].default_value = kit.srgb(110, 110, 114)
    coord = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(coord.outputs["Generated"], damier.inputs["Vector"])
    nt.links.new(damier.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    sol.materials.append(mat)
    for nom, action in actions.items():
        sens = 1.0 if nom == "gauche" else -1.0
        cr.poser(sq.arm, action, 1)
        centre = BASSIN + Vector((sens * VITESSE * IMAGES / IPS / PAS / 2.0, 0.42, 0.0))
        vues = {"dessus": (Vector((0.0, 0.0, 3.0)), 30), "arriere": (Vector((0.0, 2.6, 1.3)), 35),
                "face": (Vector((0.0, -3.4, 1.4)), 35), "profil": (Vector((-sens * 3.0, 0.4, 0.6)), 35)}
        for vue, (decalage, focale) in vues.items():
            cam = kit.camera(f"Cam_{nom}_{vue}", studio, centre + decalage, centre + Vector((0, 0, 0.1)), focale)
            for image in images:
                scene.frame_set(image)
                kit.rendre(scene, cam, os.path.join(dossier, f"{nom}_{vue}_{image:03d}.png"), 800, 600, 32)


def principal():
    args = arguments()
    scene = bpy.data.scenes.new("Reptation de côté")
    usine = bpy.data.scenes.get("Scene")
    if usine is not None and usine != scene:
        for o in list(usine.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.scenes.remove(usine)
    if bpy.context.window_manager.windows:
        bpy.context.window_manager.windows[0].scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.fps = IPS
    rb, ob, col = cr.construire(robot_ct01.MODELE, scene, "violet")
    sq = Squelette(rb.arm)
    pieds = {c: pied_sur_tibia(sq, c) for c in ("Left", "Right")}
    attente = cr.charger_attente(ATTENTE_COUCHEE)
    haut = poser_haut_du_corps(rb.arm, attente, 1)
    print("HAUT DU CORPS", len(haut), "os repris de l'attente couchée")
    actions = {"gauche": calculer(sq, 1.0, haut, pieds, "Reptation Gauche"),
               "droite": calculer(sq, -1.0, haut, pieds, "Reptation Droite")}
    if args.fbx:
        for nom, action in actions.items():
            exporter(rb.arm, action, os.path.join(cr.PROJET, args.fbx, f"Y Bot@Reptation {nom.capitalize()}.fbx")
                     if not os.path.isabs(args.fbx) else os.path.join(args.fbx, f"Y Bot@Reptation {nom.capitalize()}.fbx"))
    if args.rendus:
        rendus(scene, sq, rb, col, actions, os.path.abspath(args.rendus), [int(i) for i in args.images.split(",")])
    print("FIN")


principal()
