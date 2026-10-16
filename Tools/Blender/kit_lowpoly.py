"""Kit de modélisation low poly pour les armes et objets de Duel Arena (Blender 5.2).

Toute la géométrie est construite par bmesh, dans le repère de l'arme, sans
transformation d'objet : les coordonnées « Object » des matériaux sont alors celles
de l'arme, en mètres, et l'usure procédurale se raccorde d'une pièce à l'autre.

Repère (convention des armes du projet, voir CLAUDE.md « Les armes visibles ») :
origine au point de prise de la main droite, canon vers -Y (+Z dans Unity après un
export FBX aux réglages par défaut), haut +Z, droite du tireur -X. Les positions
s'écrivent (av, h, x) : av vers la bouche, h la hauteur, x le côté (+ = gauche).

Usure : chaque chanfrein reçoit l'attribut de face « bord » = 1. Les matériaux
(materiau()) y font apparaître le métal nu par endroits, avec des éclats, des
rayures et de la crasse dans les creux (occlusion ambiante). Tout marche dans
EEVEE comme dans Cycles, sans texture ni dépliage UV.
"""

import math
import random

import bmesh
import bpy
from mathutils import Vector

# --- repère et couleurs --------------------------------------------------------


def P(av, h, x=0.0):
    """Point du repère de l'arme : av vers la bouche, h en hauteur, x à gauche."""
    return Vector((x, -av, h))


AVANT = Vector((0.0, -1.0, 0.0))
HAUT = Vector((0.0, 0.0, 1.0))
GAUCHE = Vector((1.0, 0.0, 0.0))


def srgb(r, g, b, a=1.0):
    """Couleur donnée en sRGB 0-255, rendue en linéaire (ce que lit Blender)."""

    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    return (lin(r), lin(g), lin(b), a)


# --- pièces (un bmesh, un futur objet) ------------------------------------------


class Piece:
    def __init__(self, nom, materiau=None):
        self.nom = nom
        self.bm = bmesh.new()
        self.bord = self.bm.faces.layers.float.new("bord")
        self.materiaux = []
        if materiau:
            self.mat(materiau)

    def mat(self, nom):
        """Indice de matériau de la pièce pour ce nom (ajouté au besoin)."""
        if nom not in self.materiaux:
            self.materiaux.append(nom)
        return self.materiaux.index(nom)


def _appliquer_mat(pc, faces, mat):
    i = pc.mat(mat) if isinstance(mat, str) else (mat or 0)
    for f in faces:
        f.material_index = i


def orienter(faces, centre):
    """Oriente des faces d'une coque ouverte vers l'extérieur d'un centre (ou d'une fonction)."""
    for f in faces:
        c = f.calc_center_median()
        ref = centre(c) if callable(centre) else centre
        if f.normal.length > 0 and f.normal.dot(c - ref) < 0:
            f.normal_flip()


def loft(pc, sections, mat=None, bouts=True, cyclique=False, mats_cotes=None):
    """Relie des boucles fermées de même taille (listes de Vector) par des quads.
    mats_cotes : un matériau par côté de la section (le quad entre ses points i et i+1, None pour
    garder mat) ; de quoi poser des aplats de couleur le long d'une pièce."""
    bm = pc.bm
    anneaux = [[bm.verts.new(v) for v in s] for s in sections]
    faces = []
    cotes = []
    n = len(sections[0])
    paires = list(zip(anneaux, anneaux[1:]))
    if cyclique:
        paires.append((anneaux[-1], anneaux[0]))
    for a, b in paires:
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
            cotes.append(i)
    if bouts and not cyclique:
        faces.append(bm.faces.new(anneaux[0]))
        faces.append(bm.faces.new(list(reversed(anneaux[-1]))))
    _appliquer_mat(pc, faces, mat)
    if mats_cotes:
        for f, i in zip(faces, cotes):
            if mats_cotes[i]:
                f.material_index = pc.mat(mats_cotes[i])
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def prisme(pc, profil, x0, x1, mat=None):
    """Extrude un profil (av, h) entre les côtés x0 et x1."""
    return loft(pc, [[P(av, h, x0) for av, h in profil], [P(av, h, x1) for av, h in profil]], mat)


def boite(pc, av0, av1, h0, h1, x0, x1, mat=None):
    return prisme(pc, [(av0, h0), (av1, h0), (av1, h1), (av0, h1)], x0, x1, mat)


def boite_axes(pc, centre, ex, ey, ez, dx, dy, dz, mat=None):
    """Boîte orientée : un centre, trois axes unitaires et les demi-tailles le long de chacun."""
    coins = [centre + ex * (sx * dx) + ey * (sy * dy) + ez * (sz * dz)
             for sz in (-1, 1) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return loft(pc, [coins[:4], coins[4:]], mat)


def anneau(pc, av0, av1, h, r_ext, r_int, n=10, mat=None, x=0.0, phase=None):
    """Anneau creux (couronne, œilleton, bague) d'axe parallèle au canon, de av0 à av1."""
    phase = math.pi / n if phase is None else phase
    c0, c1 = P(av0, h, x), P(av1, h, x)
    boucles = [cercle(c0, AVANT, r_ext, n, phase), cercle(c1, AVANT, r_ext, n, phase),
               cercle(c1, AVANT, r_int, n, phase), cercle(c0, AVANT, r_int, n, phase)]
    return loft(pc, boucles, mat, cyclique=True)


def sections_av(sections):
    """[(av, [(x, h), ...]), ...] → boucles 3D, pour loft() le long de l'axe du canon."""
    return [[P(av, h, x) for x, h in s] for av, s in sections]


def mise_a_echelle(section, k, centre=(0.0, 0.0)):
    cx, ch = centre
    return [(cx + (x - cx) * k, ch + (h - ch) * k) for x, h in section]


def repere_axe(d):
    d = d.normalized()
    ref = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    u = d.cross(ref).normalized()
    v = d.cross(u).normalized()
    return u, v


def cercle(c, d, r, n, phase=0.0, rx=None):
    """n points autour de c, dans le plan normal à d (rx : rayon sur le premier axe)."""
    u, v = repere_axe(d)
    rx = r if rx is None else rx
    return [c + u * (math.cos(phase + 2 * math.pi * i / n) * rx) + v * (math.sin(phase + 2 * math.pi * i / n) * r)
            for i in range(n)]


def cylindre(pc, p0, p1, r0, n=8, r1=None, mat=None, phase=None, bouts=True):
    d = p1 - p0
    phase = math.pi / n if phase is None else phase
    return loft(pc, [cercle(p0, d, r0, n, phase), cercle(p1, d, r0 if r1 is None else r1, n, phase)], mat, bouts)


def tranches(pc, axe_debut, axe_fin, rayons, n=8, mat=None, phase=None, positions=None):
    """Tube à rayons variables le long d'un axe : rayons[i] à la position positions[i] (0..1)."""
    d = axe_fin - axe_debut
    phase = math.pi / n if phase is None else phase
    positions = positions or [i / (len(rayons) - 1) for i in range(len(rayons))]
    return loft(pc, [cercle(axe_debut + d * t, d, r, n, phase) for t, r in zip(positions, rayons)], mat)


def chanfreiner(pc, largeur, faces=None, angle_min=50.0):
    """Chanfreine les arêtes convexes (plus vives que angle_min) ; les chanfreins reçoivent bord = 1."""
    bm = pc.bm
    bm.normal_update()
    candidats = {e for f in faces for e in f.edges} if faces else set(bm.edges)
    seuil = math.radians(angle_min)
    aretes = []
    for e in candidats:
        if not e.is_manifold:
            continue
        try:
            if e.calc_face_angle_signed() > seuil:
                aretes.append(e)
        except ValueError:
            continue
    if not aretes:
        return []
    res = bmesh.ops.bevel(bm, geom=aretes, offset=largeur, offset_type="OFFSET", segments=1, profile=0.5,
                          affect="EDGES", clamp_overlap=True, material=-1)
    for f in res["faces"]:
        f[pc.bord] = 1.0
    return res["faces"]


def marquer_bord(pc, faces, valeur=1.0):
    for f in faces:
        f[pc.bord] = valeur


# --- balayages, rubans, colliers -------------------------------------------------


def lisser_chemin(points, sous_div=4, ferme=False):
    """Catmull-Rom : intercale des points entre les points de contrôle."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    sortie = []
    segments = n if ferme else n - 1
    for i in range(segments):
        p0 = pts[(i - 1) % n] if ferme or i > 0 else pts[0]
        p1 = pts[i]
        p2 = pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if ferme or i + 2 < n else pts[-1]
        for k in range(sous_div):
            t = k / sous_div
            t2, t3 = t * t, t * t * t
            sortie.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                                 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    if not ferme:
        sortie.append(pts[-1])
    return sortie


def balayage(pc, chemin, section, mat=None, ferme=False, echelles=None, normale=None, bouts=True):
    """Fait glisser une section 2D (liste de (a, b)) le long d'un chemin 3D (transport parallèle)."""
    n = len(chemin)
    tangentes = []
    for i in range(n):
        if ferme:
            t = chemin[(i + 1) % n] - chemin[(i - 1) % n]
        elif i == 0:
            t = chemin[1] - chemin[0]
        elif i == n - 1:
            t = chemin[-1] - chemin[-2]
        else:
            t = (chemin[i + 1] - chemin[i]).normalized() + (chemin[i] - chemin[i - 1]).normalized()
        tangentes.append(t.normalized())
    if normale is None:
        u = repere_axe(tangentes[0])[0]
    else:
        u = (normale - tangentes[0] * normale.dot(tangentes[0])).normalized()
    sections = []
    for i, p in enumerate(chemin):
        t = tangentes[i]
        u = (u - t * u.dot(t)).normalized()
        v = t.cross(u).normalized()
        e = echelles[i] if echelles else 1.0
        sections.append([p + (u * a + v * b) * e for a, b in section])
    return loft(pc, sections, mat, bouts=bouts and not ferme, cyclique=ferme)


def section_cercle(r, n, phase=0.0):
    return [(math.cos(phase + 2 * math.pi * i / n) * r, math.sin(phase + 2 * math.pi * i / n) * r) for i in range(n)]


def section_rect(l, e):
    return [(-l / 2, -e / 2), (l / 2, -e / 2), (l / 2, e / 2), (-l / 2, e / 2)]


def aire_signee(pts):
    n = len(pts)
    return 0.5 * sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))


def decaler_contour(pts, e):
    """Décale un contour 2D fermé de e vers l'extérieur (e < 0 : vers l'intérieur)."""
    signe = 1.0 if aire_signee(pts) > 0 else -1.0
    n = len(pts)
    sortie = []
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        d1, d2 = (p1 - p0).normalized(), (p2 - p1).normalized()
        n1 = Vector((d1.y, -d1.x)) * signe
        n2 = Vector((d2.y, -d2.x)) * signe
        m = (n1 + n2)
        m = m.normalized() if m.length > 1e-9 else n1
        sortie.append(p1 + m * (e / max(m.dot(n1), 0.35)))
    return sortie


def bande(pc, contour, origine, ex, ey, axe, largeur, epaisseur, mat=None, inclinaison=0.0,
          irregularite=0.0, rng=None, rabat=None):
    """Bande (ruban, collier) qui ceinture une section : contour 2D dans le repère (ex, ey),
    posée autour de origine, de largeur mesurée le long de axe. La face intérieure, cachée
    contre la pièce, n'est pas construite. rabat = (indice du point, longueur) : bout décollé."""
    rng = rng or random.Random(0)
    bm = pc.bm
    ext = decaler_contour(contour, epaisseur)
    inte = [Vector(p) for p in contour]
    pente = math.tan(inclinaison)

    def point(p2, s):
        jeu = rng.uniform(-irregularite, irregularite) if irregularite else 0.0
        return origine + ex * p2.x + ey * p2.y + axe * (s + pente * p2.y + jeu)

    s0, s1 = -largeur / 2, largeur / 2
    e0 = [bm.verts.new(point(p, s0)) for p in ext]
    e1 = [bm.verts.new(point(p, s1)) for p in ext]
    i0 = [bm.verts.new(point(p, s0)) for p in inte]
    i1 = [bm.verts.new(point(p, s1)) for p in inte]
    faces = []
    n = len(contour)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((e0[i], e0[j], e1[j], e1[i])))
        faces.append(bm.faces.new((i0[i], i0[j], e0[j], e0[i])))
        faces.append(bm.faces.new((e1[i], e1[j], i1[j], i1[i])))
    if rabat:
        k, longueur = rabat
        a, b = e0[k].co, e1[k].co
        tangente = (Vector(ext[(k + 1) % n]) - Vector(ext[k])).normalized()
        t3 = ex * tangente.x + ey * tangente.y
        normale3 = ex * (Vector(ext[k]) - Vector(inte[k])).normalized().x + ey * (Vector(ext[k]) - Vector(inte[k])).normalized().y
        c = bm.verts.new(b - t3 * longueur + normale3 * longueur * 0.35)
        d = bm.verts.new(a - t3 * longueur + normale3 * longueur * 0.35)
        faces.append(bm.faces.new((e0[k], e1[k], c, d)))
    centre_axe = origine

    def centre(c):
        # centre local : projection sur l'axe de la bande
        return centre_axe + axe * (c - centre_axe).dot(axe)

    orienter(faces, centre)
    _appliquer_mat(pc, faces, mat)
    return faces


def contour_rect(l, h, chanfrein=0.0, cx=0.0, cy=0.0):
    """Rectangle (éventuellement à coins abattus) centré sur (cx, cy), sens trigonométrique."""
    a, b, c = l / 2, h / 2, chanfrein
    if c <= 0:
        pts = [(-a, -b), (a, -b), (a, b), (-a, b)]
    else:
        pts = [(-a + c, -b), (a - c, -b), (a, -b + c), (a, b - c), (a - c, b), (-a + c, b), (-a, b - c), (-a, -b + c)]
    return [(x + cx, y + cy) for x, y in pts]


# --- petits éléments --------------------------------------------------------------


def rivet(pc, centre, normale, r=0.0022, h=0.0012, n=6, mat=None):
    normale = normale.normalized()
    return loft(pc, [cercle(centre - normale * 0.0005, normale, r, n), cercle(centre + normale * h, normale, r * 0.55, n)], mat)


def vis_six_pans(pc, centre, normale, r=0.0026, h=0.0018, mat=None, phase=0.3):
    normale = normale.normalized()
    return loft(pc, [cercle(centre - normale * 0.0005, normale, r, 6, phase), cercle(centre + normale * h, normale, r, 6, phase)], mat)


def disque(pc, centre, normale, r, n=16, mat=None, mats_quartiers=None, rotation=0.0):
    """Disque plat (décalque) ; mats_quartiers = 4 matériaux alternés en quartiers (mire de crash-test)."""
    bm = pc.bm
    normale = normale.normalized()
    c = bm.verts.new(centre)
    bord = [bm.verts.new(p) for p in cercle(centre, normale, r, n, rotation)]
    faces = []
    for i in range(n):
        f = bm.faces.new((c, bord[i], bord[(i + 1) % n]))
        faces.append(f)
        if mats_quartiers:
            f.material_index = pc.mat(mats_quartiers[(i * 4 // n) % 4])
    if not mats_quartiers:
        _appliquer_mat(pc, faces, mat)
    for f in faces:
        f.normal_update()
        if f.normal.dot(normale) < 0:
            f.normal_flip()
    return faces


def quad(pc, coins, mat=None, normale=None):
    bm = pc.bm
    f = bm.faces.new([bm.verts.new(c) for c in coins])
    f.normal_update()
    if normale is not None and f.normal.dot(normale) < 0:
        f.normal_flip()
    _appliquer_mat(pc, [f], mat)
    return f


def soudure(pc, chemin, r=0.0022, mat=None, rng=None, pas=0.0018, cotes=6):
    """Cordon de soudure « en écailles » : un tube dont le rayon pulse le long du chemin."""
    rng = rng or random.Random(1)
    # rééchantillonne le chemin à pas constant
    pts = [Vector(p) for p in chemin]
    longueur = sum((b - a).length for a, b in zip(pts, pts[1:]))
    k = max(4, int(longueur / pas))
    dense = []
    for i in range(k + 1):
        cible = longueur * i / k
        acc = 0.0
        for a, b in zip(pts, pts[1:]):
            seg = (b - a).length
            if acc + seg >= cible - 1e-9:
                dense.append(a + (b - a) * ((cible - acc) / seg if seg else 0))
                break
            acc += seg
    echelles = [(1.0 if i % 2 == 0 else 0.72) * rng.uniform(0.9, 1.1) for i in range(len(dense))]
    echelles[0] = echelles[-1] = 0.5
    sec = [(math.cos(2 * math.pi * i / cotes) * r, math.sin(2 * math.pi * i / cotes) * r * 0.65) for i in range(cotes)]
    return balayage(pc, dense, sec, mat, echelles=echelles)


def helice(centre, axe, rx, ry, longueur, tours, pas_par_tour=8, phase=0.0):
    u, v = repere_axe(axe)
    axe = axe.normalized()
    n = int(tours * pas_par_tour)
    pts = []
    for i in range(n + 1):
        t = i / n
        a = phase + 2 * math.pi * tours * t
        pts.append(centre + axe * (longueur * (t - 0.5)) + u * (math.cos(a) * rx) + v * (math.sin(a) * ry))
    return pts


def texte(pc, contenu, police, hauteur, centre, axe_x, axe_y, mat=None, decalage=0.00035, scene=None,
          espacement=1.0, resolution=2):
    """Texte plat (décalque) : la hauteur des capitales vaut hauteur ; renvoie (largeur, faces)."""
    scene = scene or bpy.context.scene
    cu = bpy.data.curves.new("tmp_texte", "FONT")
    cu.body = contenu
    cu.font = police
    cu.size = 1.0
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.resolution_u = resolution
    cu.space_character = espacement
    ob = bpy.data.objects.new("tmp_texte", cu)
    scene.collection.objects.link(ob)
    dg = scene.view_layers[0].depsgraph
    dg.update()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    echelle = hauteur / max(1e-6, (max(ys) - min(ys)))
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    normale = axe_x.cross(axe_y).normalized()
    bm = pc.bm
    verts = [bm.verts.new(centre + axe_x * ((v.co.x - cx) * echelle) + axe_y * ((v.co.y - cy) * echelle) + normale * decalage)
             for v in me.vertices]
    faces = []
    for poly in me.polygons:
        try:
            f = bm.faces.new([verts[i] for i in poly.vertices])
        except ValueError:
            continue
        f.normal_update()
        if f.normal.dot(normale) < 0:
            f.normal_flip()
        faces.append(f)
    largeur = (max(xs) - min(xs)) * echelle
    ev.to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    _appliquer_mat(pc, faces, mat)
    return largeur, faces


# --- objets, booléens, collections -------------------------------------------------


def collection(nom, parent):
    """Collection enfant de parent ; jamais celle, homonyme, d'une autre scène."""
    col = parent.children.get(nom)
    if col is None:
        col = bpy.data.collections.new(nom)
        parent.children.link(col)
    return col


def vider_collection(col):
    for ob in list(col.all_objects):
        donnees = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        if donnees is not None and donnees.users == 0:
            if isinstance(donnees, bpy.types.Mesh):
                bpy.data.meshes.remove(donnees)
            elif isinstance(donnees, bpy.types.Curve):
                bpy.data.curves.remove(donnees)
            elif isinstance(donnees, bpy.types.Light):
                bpy.data.lights.remove(donnees)
            elif isinstance(donnees, bpy.types.Camera):
                bpy.data.cameras.remove(donnees)
    for enfant in list(col.children):
        vider_collection(enfant)
        bpy.data.collections.remove(enfant)


def vers_objet(pc, collection_cible, materiaux, parent=None, angle_lisse=35.0):
    """Crée l'objet de la pièce. Ne supprime jamais rien : si le nom est déjà pris ailleurs
    dans le fichier (un objet de l'utilisateur), Blender numérote le nôtre."""
    me = bpy.data.meshes.new(pc.nom)
    pc.bm.normal_update()
    pc.bm.to_mesh(me)
    pc.bm.free()
    for nom in pc.materiaux:
        me.materials.append(materiaux[nom])
    ob = bpy.data.objects.new(pc.nom, me)
    collection_cible.objects.link(ob)
    if parent is not None:
        ob.parent = parent
    lisser(ob, angle_lisse)
    return ob


def lisser(ob, angle=35.0):
    me = ob.data
    me.shade_smooth()
    if angle is not None:
        me.set_sharp_from_angle(angle=math.radians(angle))


def couper(ob, coupeur, scene, materiaux, auto_intersection=False):
    """Différence booléenne exacte ; les faces de coupe prennent les matériaux du coupeur."""
    co = vers_objet(coupeur, scene.collection, materiaux, angle_lisse=None)
    co.display_type = "WIRE"
    co.hide_render = True
    mod = ob.modifiers.new("Coupe", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = co
    mod.solver = "EXACT"
    mod.material_mode = "TRANSFER"
    mod.use_self = auto_intersection
    dg = scene.view_layers[0].depsgraph
    dg.update()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    ob.modifiers.remove(mod)
    ancien = ob.data
    nom = ancien.name
    ob.data = me
    bpy.data.meshes.remove(ancien)
    me.name = nom
    donnees = co.data
    bpy.data.objects.remove(co, do_unlink=True)
    bpy.data.meshes.remove(donnees)
    lisser(ob)
    return ob


def triangles(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# --- matériaux procéduraux ----------------------------------------------------------


def _par_id(prises, identifiant):
    """Prise d'un nœud par son identifiant (inputs["…"] cherche par le nom, ambigu sur Mix)."""
    return next(p for p in prises if p.identifier == identifiant)


class _Graphe:
    def __init__(self, mat):
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.N = self.nt.nodes
        self.L = self.nt.links
        self.coord = self.N.new("ShaderNodeTexCoord").outputs["Object"]

    def relier(self, valeur, entree):
        if isinstance(valeur, bpy.types.NodeSocket):
            self.L.new(valeur, entree)
            return
        try:
            entree.default_value = valeur
        except (ValueError, TypeError):
            entree.default_value = tuple(valeur)[:3]  # couleur RGBA vers une entrée vecteur

    def math(self, op, a, b=0.0, borne=False):
        n = self.N.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = borne
        self.relier(a, n.inputs[0])
        self.relier(b, n.inputs[1])
        return n.outputs[0]

    def melange(self, f, a, b):
        n = self.N.new("ShaderNodeMix")
        n.data_type = "FLOAT"
        self.relier(f, _par_id(n.inputs, "Factor_Float"))
        self.relier(a, _par_id(n.inputs, "A_Float"))
        self.relier(b, _par_id(n.inputs, "B_Float"))
        return _par_id(n.outputs, "Result_Float")

    def melange_couleur(self, f, a, b):
        n = self.N.new("ShaderNodeMix")
        n.data_type = "RGBA"
        self.relier(f, _par_id(n.inputs, "Factor_Float"))
        self.relier(a, _par_id(n.inputs, "A_Color"))
        self.relier(b, _par_id(n.inputs, "B_Color"))
        return _par_id(n.outputs, "Result_Color")

    def echelle_couleur(self, couleur, s):
        n = self.N.new("ShaderNodeVectorMath")
        n.operation = "SCALE"
        self.relier(couleur, n.inputs[0])
        self.relier(s, n.inputs["Scale"])
        return n.outputs["Vector"]

    def bruit(self, echelle, detail=3.0, rugosite=0.55, decalage=(0, 0, 0), etirement=(1, 1, 1), rotation=(0, 0, 0)):
        m = self.N.new("ShaderNodeMapping")
        m.inputs["Location"].default_value = decalage
        m.inputs["Rotation"].default_value = rotation
        m.inputs["Scale"].default_value = etirement
        self.L.new(self.coord, m.inputs["Vector"])
        n = self.N.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = echelle
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rugosite
        self.L.new(m.outputs["Vector"], n.inputs["Vector"])
        return n.outputs["Fac"]

    def vecteur(self, op, a, b=None):
        n = self.N.new("ShaderNodeVectorMath")
        n.operation = op
        self.relier(a, n.inputs[0])
        if b is not None:
            self.relier(b, n.inputs[1])
        return n.outputs["Value"] if op in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else n.outputs["Vector"]

    def rayures(self, echelle, largeur, longueur, densite, decalage):
        """Rayures courtes et droites : dans chaque cellule de Voronoï, une tranche fine de
        normale tirée au hasard, bornée par une sphère ; sur une face, c'est un trait droit."""
        m = self.N.new("ShaderNodeMapping")
        m.inputs["Location"].default_value = decalage
        self.L.new(self.coord, m.inputs["Vector"])
        v = self.N.new("ShaderNodeTexVoronoi")
        v.inputs["Scale"].default_value = echelle
        self.L.new(m.outputs["Vector"], v.inputs["Vector"])
        d = self.vecteur("SUBTRACT", m.outputs["Vector"], v.outputs["Position"])  # Position : espace d'entrée
        normale = self.vecteur("NORMALIZE", self.vecteur("SUBTRACT", v.outputs["Color"], (0.5, 0.5, 0.5)))
        tranche = self.math("LESS_THAN", self.math("ABSOLUTE", self.vecteur("DOT_PRODUCT", d, normale)), largeur)
        sphere = self.math("LESS_THAN", self.vecteur("LENGTH", d), longueur)
        tirage = self.N.new("ShaderNodeSeparateXYZ")
        self.L.new(v.outputs["Color"], tirage.inputs[0])
        garde = self.math("LESS_THAN", tirage.outputs["X"], densite)
        return self.math("MULTIPLY", self.math("MULTIPLY", tranche, sphere), garde)

    def attribut(self, nom):
        n = self.N.new("ShaderNodeAttribute")
        n.attribute_type = "GEOMETRY"
        n.attribute_name = nom
        return n.outputs["Fac"]


def materiau(nom, base, rug=0.6, metal=0.0, dessous=None, rug_dessous=0.35, metal_dessous=1.0,
             bords=0.5, eclats=0.0, rayures=0.0, salete=0.5, variation=0.08, emission=None,
             force_emission=0.0, suie=None, graine=0, coat=0.0, echelle_eclats=42.0,
             couche=None, retouches=0.0, teinte_retouches=None, rouille=0.0, teinte_rouille=None,
             huile=0.0, poussiere=0.0, delavage=0.0, zones=None):
    """Matériau à usure procédurale.

    base / dessous : couleur de la peinture et de ce qu'elle cache (métal nu, plastique éraflé) ;
    bords, eclats, rayures, salete : 0..1, quantité de chaque usure ; suie = (centre, rayon).

    Les marques d'un long service, toutes absentes par défaut (voir commun_armes.VARIANTES_USURE) :
    couche = (couleur, rugosité) : une couche entre la peinture et le dessous (un apprêt, une
    ancienne peinture), qui borde les éclats ; retouches : des taches de peinture refaite au
    pinceau, de teinte_retouches, par-dessus l'usure ; rouille : dans les éclats et les creux, de
    teinte_rouille, avec des coulures ; huile : des taches sombres et luisantes ; poussiere : dans
    les creux et sur les dessus ; delavage : une peinture passée ; zones = [(centre, rayon, force)] :
    l'usure forcée là où les mains frottent, le dessous y est poli.
    """
    mat = bpy.data.materials.get(nom) or bpy.data.materials.new(nom)
    g = _Graphe(mat)
    N = g.N
    sortie = N.new("ShaderNodeOutputMaterial")
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    g.L.new(bsdf.outputs["BSDF"], sortie.inputs["Surface"])
    rng = random.Random(graine)
    dec = lambda: (rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50))  # noqa: E731
    # les marques d'un long service tirent leur propre hasard : sans elles, le matériau ne change pas
    rng2 = random.Random(graine + 7919)
    dec2 = lambda: (rng2.uniform(-50, 50), rng2.uniform(-50, 50), rng2.uniform(-50, 50))  # noqa: E731

    frottement = None   # là où les mains frottent : 0..1
    if zones:
        for centre, rayon, force in zones:
            d = g.vecteur("DISTANCE", g.coord, tuple(centre))
            f = g.math("MULTIPLY", g.math("SUBTRACT", 1.0, g.math("DIVIDE", d, rayon), borne=True), force)
            frottement = f if frottement is None else g.math("ADD", frottement, f, borne=True)

    def seuil(t, poids):
        return t if frottement is None else g.math("SUBTRACT", t, g.math("MULTIPLY", frottement, poids))

    # teinte de base légèrement nuageuse
    couleur = base
    if variation:
        v = g.bruit(4.0, 2.0, 0.5, dec())
        couleur = g.echelle_couleur(base, g.math("ADD", g.math("MULTIPLY", g.math("SUBTRACT", v, 0.5), variation * 2), 1.0))
    if delavage:   # une peinture passée, par plaques
        p = g.math("MULTIPLY", g.math("ADD", 0.4, g.math("MULTIPLY", g.bruit(2.5, 2.0, 0.5, dec2()), 0.9)), 0.4 * delavage,
                   borne=True)
        couleur = g.melange_couleur(p, couleur, (0.24, 0.24, 0.235, 1.0))

    masque = 0.0
    large = 0.0   # l'éclat bordé de sa couche, quand il y en a une
    if dessous is not None:
        morceaux, bordures = [], []
        if bords > 0:
            bruit_bords = g.bruit(38.0, 4.0, 0.6, dec())
            b = g.math("GREATER_THAN", bruit_bords, seuil(0.64 - 0.24 * bords, 0.40))
            morceaux.append(g.math("MULTIPLY", g.attribut("bord"), b))
            if couche is not None:
                b2 = g.math("GREATER_THAN", bruit_bords, seuil(0.64 - 0.24 * bords - 0.10, 0.40))
                bordures.append(g.math("MULTIPLY", g.attribut("bord"), b2))
        if eclats > 0:
            bruit_eclats = g.bruit(echelle_eclats, 4.0, 0.6, dec())
            e = g.math("GREATER_THAN", bruit_eclats, seuil(0.70 - 0.08 * eclats, 0.30))
            zone = g.math("GREATER_THAN", g.bruit(4.0, 2.0, 0.5, dec()), 0.56)
            if frottement is not None:
                zone = g.math("MAXIMUM", zone, g.math("GREATER_THAN", frottement, 0.25))
            morceaux.append(g.math("MULTIPLY", e, zone))
            if couche is not None:
                e2 = g.math("GREATER_THAN", bruit_eclats, seuil(0.70 - 0.08 * eclats - 0.075, 0.30))
                bordures.append(g.math("MULTIPLY", e2, zone))
        if rayures > 0:
            zone = g.math("GREATER_THAN", g.bruit(3.0, 2.0, 0.5, dec()), 0.60 - 0.10 * rayures)
            r1 = g.rayures(55.0, 0.00032, 0.011, 0.55 * rayures, dec())
            r2 = g.rayures(110.0, 0.00022, 0.005, 0.5 * rayures, dec())
            morceaux.append(g.math("MULTIPLY", g.math("MAXIMUM", r1, r2), zone))
        for m in morceaux:
            masque = m if masque == 0.0 else g.math("MAXIMUM", masque, m)
        for m in bordures:
            large = m if large == 0.0 else g.math("MAXIMUM", large, m)
        if large != 0.0:
            couleur = g.melange_couleur(large, couleur, couche[0])
        if masque != 0.0:
            couleur = g.melange_couleur(masque, couleur, dessous)

    rugosite = rug if large == 0.0 else g.melange(large, rug, couche[1])
    if masque != 0.0:
        rug_nu = rug_dessous if frottement is None else g.math("SUBTRACT", rug_dessous,
                                                              g.math("MULTIPLY", frottement, 0.22), borne=True)
        rugosite = g.melange(masque, rugosite, rug_nu)
    metallique = metal if masque == 0.0 else g.melange(masque, metal if large == 0.0 else g.melange(large, metal, 0.0),
                                                       metal_dessous)
    if large != 0.0 and masque == 0.0:
        metallique = g.melange(large, metal, 0.0)

    if retouches:   # peinture refaite au pinceau par-dessus l'usure, d'une teinte qui ne colle pas tout à fait
        t = g.math("GREATER_THAN", g.bruit(7.0, 2.0, 0.45, dec2()), 0.66 - 0.12 * retouches)
        couleur = g.melange_couleur(t, couleur, teinte_retouches)
        rugosite = g.melange(t, rugosite, 0.44)
        metallique = g.melange(t, metallique, 0.0)
        if masque != 0.0:
            masque = g.math("MULTIPLY", masque, g.math("SUBTRACT", 1.0, t))

    creux = None
    if salete > 0 or rouille or poussiere:
        ao = N.new("ShaderNodeAmbientOcclusion")
        ao.samples = 8
        ao.inputs["Distance"].default_value = 0.018
        creux = g.math("SUBTRACT", 1.0, ao.outputs["AO"], borne=True)

    if rouille:   # la rouille mange le métal mis à nu, cloque sous la peinture, remplit les creux et coule
        r = g.bruit(26.0, 4.0, 0.65, dec2())
        teinte = g.echelle_couleur(teinte_rouille, g.math("ADD", 0.55, g.math("MULTIPLY", r, 0.6)))
        morceaux = [g.math("MULTIPLY", g.math("MULTIPLY", creux, 2.4 * rouille), r, borne=True)]
        if masque != 0.0:
            morceaux.append(g.math("MULTIPLY", masque, g.math("GREATER_THAN", r, 0.62 - 0.30 * rouille)))
        cloques = g.math("GREATER_THAN", g.bruit(16.0, 4.0, 0.7, dec2()), 0.71 - 0.12 * rouille)
        morceaux.append(g.math("MULTIPLY", cloques, g.math("GREATER_THAN", g.bruit(2.5, 2.0, 0.5, dec2()), 0.50)))
        coulures = g.math("GREATER_THAN", g.bruit(14.0, 3.0, 0.6, dec2(), etirement=(1.0, 1.0, 0.14)), 0.61 - 0.10 * rouille)
        zone = g.math("GREATER_THAN", g.bruit(3.0, 2.0, 0.5, dec2()), 0.52)
        morceaux.append(g.math("MULTIPLY", g.math("MULTIPLY", coulures, zone), 0.6 * rouille))
        rousse = morceaux[0]
        for m in morceaux[1:]:
            rousse = g.math("MAXIMUM", rousse, m)
        couleur = g.melange_couleur(rousse, couleur, teinte)
        rugosite = g.melange(rousse, rugosite, 0.92)
        metallique = g.melange(rousse, metallique, 0.0)

    if huile:   # taches d'huile : plus sombres, luisantes
        o = g.math("MULTIPLY", g.math("GREATER_THAN", g.bruit(6.0, 3.0, 0.6, dec2()), 0.58 - 0.16 * huile),
                   g.math("ADD", 0.5, g.math("MULTIPLY", g.bruit(30.0, 2.0, 0.5, dec2()), 0.5)))
        couleur = g.echelle_couleur(couleur, g.math("SUBTRACT", 1.0, g.math("MULTIPLY", o, 0.55)))
        rugosite = g.melange(o, rugosite, 0.12)

    if salete > 0:
        creux_sale = g.math("MULTIPLY", creux, 1.6 * salete, borne=True)
        couleur = g.echelle_couleur(couleur, g.math("SUBTRACT", 1.0, g.math("MULTIPLY", creux_sale, 0.75)))
        rugosite = g.math("ADD", rugosite, g.math("MULTIPLY", creux_sale, 0.25), borne=True)

    if poussiere:   # une poussière grise dans les creux et sur les faces tournées vers le haut
        geo = N.new("ShaderNodeNewGeometry")
        nz = N.new("ShaderNodeSeparateXYZ")
        g.L.new(geo.outputs["Normal"], nz.inputs[0])
        dessus = g.math("MULTIPLY", g.math("SUBTRACT", nz.outputs["Z"], 0.55), 3.0, borne=True)
        tache = g.math("ADD", 0.35, g.math("MULTIPLY", g.bruit(14.0, 3.0, 0.6, dec2()), 0.65))
        d = g.math("MULTIPLY", g.math("MAXIMUM", g.math("MULTIPLY", creux, 2.6), g.math("MULTIPLY", dessus, 0.8)),
                   g.math("MULTIPLY", tache, poussiere), borne=True)
        couleur = g.melange_couleur(d, couleur, (0.32, 0.31, 0.29, 1.0))
        rugosite = g.melange(d, rugosite, 0.95)

    if suie:
        centre, rayon = suie
        d = N.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        g.L.new(g.coord, d.inputs[0])
        d.inputs[1].default_value = centre
        noirci = g.math("SUBTRACT", 1.0, g.math("DIVIDE", d.outputs["Value"], rayon), borne=True)
        noirci = g.math("MULTIPLY", noirci, g.math("ADD", 0.55, g.math("MULTIPLY", g.bruit(60.0, 3.0, 0.6, dec()), 0.6)), borne=True)
        couleur = g.echelle_couleur(couleur, g.math("SUBTRACT", 1.0, g.math("MULTIPLY", noirci, 0.85)))
        rugosite = g.melange(noirci, rugosite, 0.85)

    g.relier(couleur, bsdf.inputs["Base Color"])
    g.relier(rugosite, bsdf.inputs["Roughness"])
    g.relier(metallique, bsdf.inputs["Metallic"])
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = force_emission
    if coat:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.05
    # aperçu en mode Solide
    mat.diffuse_color = base
    mat.roughness = rug
    mat.metallic = metal
    # range les nœuds en colonnes lisibles
    for i, n in enumerate(N):
        n.location = (-220 * (i % 12) - 300, -180 * (i // 12))
    sortie.location = (400, 0)
    bsdf.location = (100, 0)
    return mat


# --- studio de rendu ------------------------------------------------------------------


def lumiere(nom, col, position, cible, puissance, taille, couleur=(1, 1, 1)):
    li = bpy.data.lights.new(nom, "AREA")
    li.energy = puissance
    li.size = taille
    li.color = couleur
    ob = bpy.data.objects.new(nom, li)
    col.objects.link(ob)
    ob.location = position
    ob.rotation_euler = (cible - position).to_track_quat("-Z", "Y").to_euler()
    return ob


def camera(nom, col, position, cible, focale=50.0, ortho=None):
    cam = bpy.data.cameras.new(nom)
    cam.lens = focale
    cam.clip_start = 0.01
    cam.clip_end = 50
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    ob = bpy.data.objects.new(nom, cam)
    col.objects.link(ob)
    ob.location = position
    ob.rotation_euler = (cible - position).to_track_quat("-Z", "Y").to_euler()
    return ob


def rendre(scene, cam, chemin, largeur, hauteur, echantillons=48, argile=None):
    scene.camera = cam
    scene.render.resolution_x = largeur
    scene.render.resolution_y = hauteur
    scene.render.resolution_percentage = 100
    scene.render.filepath = chemin
    scene.render.image_settings.file_format = "PNG"
    scene.eevee.taa_render_samples = echantillons
    vl = scene.view_layers[0]
    vl.material_override = argile
    bpy.ops.render.render(write_still=True, scene=scene.name)
    vl.material_override = None
