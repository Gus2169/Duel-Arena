"""Ce que partagent les accessoires des armes de Duel Arena (Blender 5.2).

Les accessoires (liste de l'utilisateur du 2026-10-16, GDD § 6, bible § 12) se montent sur les
points d'accroche des armes (« <code>_Accessoires » : Viseur, Laser, Poignee, Chargeur) :
- viseurs.py : les dix viseurs sans grossissement de Rainbow Six Siege (trois points rouges,
  quatre holographiques, trois réflex), en low poly, dans la DA du stock.

Un accessoire est construit dans le repère de l'arme qui le porte (P(av, h, x) : av vers la bouche,
h en hauteur, x à gauche du tireur), son origine au point d'accroche : pour un viseur, le dessus
des dents du rail du dessus (ou de la plaque à encoches d'une arme laser), au milieu de l'embase.
L'embase y pose à plat, une courte jupe de chaque côté le long des dents (le canal fait la largeur
d'un rail à dents, 21,2 mm) : sur une plaque, la jupe se perd dans la carcasse.

Repères, comme sur les armes : « <code>_Mire », sur l'axe optique, au dos du viseur (Unity y aligne
l'œil en visée). La vitre (« <code>_Vitre ») est un objet et un matériau à part : Unity remplace son
matériau par le shader du réticule, dessiné à l'infini. Le réticule dessiné ici (« <code>_Reticule »)
ne sert qu'aux rendus de contrôle ; l'export ne le prend pas.
"""

import argparse
import importlib
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
if DOSSIER not in sys.path:
    sys.path.insert(0, DOSSIER)
import commun_armes as ca  # noqa: E402

importlib.reload(ca)
kit = ca.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece, srgb  # noqa: E402

RAIL = 0.0212            # largeur d'un rail à dents (type Picatinny)
JUPE = 0.003             # de combien une embase descend le long des dents
VERS_TIREUR = -AVANT     # la normale d'une face qui regarde le tireur
DROITE = -GAUCHE         # la droite du tireur
ROUGE_RETICULE = srgb(255, 34, 24)
VERT_RETICULE = srgb(70, 255, 96)
TEINTES_VITRE = {
    "ambre": srgb(222, 176, 104),    # traitement des points rouges, vu de dos
    "bleu": srgb(132, 172, 214),     # holographiques
    "neutre": srgb(184, 196, 200),   # réflex
}


# --- matériaux propres aux accessoires --------------------------------------------------------------

def vitre(nom, teinte, opacite=0.22):
    """Verre teinté, transparent dans EEVEE comme dans Cycles. Unity le remplace par le shader du
    réticule : seuls sa teinte et son opacité s'y retrouvent."""
    mat = bpy.data.materials.get(nom) or bpy.data.materials.new(nom)
    nt = mat.node_tree
    nt.nodes.clear()
    sortie = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = teinte
    bsdf.inputs["Roughness"].default_value = 0.03
    bsdf.inputs["Alpha"].default_value = opacite
    bsdf.inputs["Coat Weight"].default_value = 1.0
    bsdf.inputs["Coat Roughness"].default_value = 0.02
    nt.links.new(bsdf.outputs["BSDF"], sortie.inputs["Surface"])
    mat.surface_render_method = "BLENDED"
    mat.use_backface_culling = False
    mat.diffuse_color = (teinte[0], teinte[1], teinte[2], opacite)
    return mat


def materiaux(code, variante=None, mains=None, fabrique=None):
    """Les matériaux des armes (commun_armes.materiaux), plus ceux des seuls accessoires : les vitres
    et le réticule des rendus de contrôle. La suie est loin : un accessoire n'a pas de bouche."""
    M = ca.materiaux(code, (9.0, 9.0), variante, mains, fabrique)
    if fabrique is None:
        for cle, teinte in TEINTES_VITRE.items():
            M["Vitre_" + cle] = vitre(f"DA_Vitre_{cle}", teinte)
        M["ReticuleRouge"] = kit.materiau("DA_Réticule_Rouge", srgb(40, 6, 4), rug=0.5, salete=0.0, variation=0.0,
                                          emission=ROUGE_RETICULE, force_emission=7.0)
        M["ReticuleVert"] = kit.materiau("DA_Réticule_Vert", srgb(6, 40, 10), rug=0.5, salete=0.0, variation=0.0,
                                         emission=VERT_RETICULE, force_emission=5.0)
    return M


# --- atelier : un accessoire ------------------------------------------------------------------------

class AtelierAccessoire(ca.Atelier):
    """Un accessoire : sa collection, sa racine au point d'accroche, ses pièces, sa vitre, ses repères.

    transformation : une matrice appliquée à toute la géométrie et aux repères, pour construire une
    pièce dans le repère qui l'arrange (un laser dessiné posé à plat puis couché sur le flanc droit,
    un chargeur dessiné dans le repère de son arme puis ramené à son point d'accroche)."""

    transformation = None

    def objet(self, pc, angle=35.0):
        if self.transformation is not None:
            bmesh.ops.transform(pc.bm, matrix=self.transformation, verts=pc.bm.verts)
        return super().objet(pc, angle)

    def vide(self, nom, parent, forme="PLAIN_AXES", taille=0.015, position=None):
        if position is not None and self.transformation is not None:
            position = self.transformation @ Vector(position)
        return super().vide(nom, parent, forme, taille, position)

    def faisceau(self, av, h, x=0.0):
        """D'où part le faisceau d'un laser (Unity le tire vers l'avant de l'arme)."""
        self.vide(self.nom("Faisceau"), self.racine, position=P(av, h, x))

    def main_gauche(self, av, h, x=0.0):
        """Là où la paume gauche tient une poignée : la cible de l'IK de la main gauche."""
        self.vide(self.nom("MainGauche"), self.racine, position=P(av, h, x))

    def __init__(self, scene, code, titre, categorie, M):
        super().__init__(scene, code, titre, False, M)
        self.categorie = categorie   # le point d'accroche : Viseur, Laser, Poignee, Chargeur
        self.vitres = []             # objets dont Unity remplace le matériau (le réticule y est dessiné)
        self.controle = []           # objets des seuls rendus de contrôle, que l'export ne prend pas
        self.donnees = {}            # ce qu'Unity lit de l'accessoire (réticule...), dans la fiche exportée

    def mire(self, av, h):
        """L'axe optique, au dos du viseur : Unity y aligne l'œil en visée."""
        self.vide(self.nom("Mire"), self.racine, position=P(av, h))
        self.donnees["hauteurAxe"] = h


# --- embases --------------------------------------------------------------------------------------

def profil_embase(demi, h_haut, ch=0.0015, jupe=JUPE, canal=RAIL / 2, sens=1):
    """Section (x, h) d'une embase posée sur un rail : dessous à h = 0 sur les dents, une jupe de chaque
    côté qui descend de `jupe` le long d'elles. sens -1 : une embase pendue sous un rail (poignée)."""
    pts = [(demi, -jupe), (demi, h_haut - ch), (demi - ch, h_haut), (-(demi - ch), h_haut), (-demi, h_haut - ch),
           (-demi, -jupe), (-canal, -jupe), (-canal, 0.0), (canal, 0.0), (canal, -jupe)]
    return [(x, h * sens) for x, h in pts]


def embase(pc, av0, av1, demi=0.0130, h_haut=0.006, mat="Peinture", ch=0.0015, sens=1):
    profil = profil_embase(demi, h_haut, ch, sens=sens)
    f = kit.loft(pc, kit.sections_av([(av0, profil), (av1, profil)]), mat)
    kit.chanfreiner(pc, 0.0010, f)
    return f


def vis_de_serrage(pc, av, demi, h=0.0012, mat="Acier", ecrou=True):
    """Le boulon qui serre l'embase sur le rail : l'écrou à droite (côté des autres), la tête à gauche."""
    if ecrou:
        kit.vis_six_pans(pc, P(av, h, -demi), DROITE, r=0.0040, h=0.0026, mat=mat)
    kit.cylindre(pc, P(av, h, demi - 0.0002), P(av, h, demi + 0.0014), 0.0032, 8, mat=mat)


def molette(pc, centre, axe, r, epaisseur, dents=12, creux=0.88, mat="Polymere"):
    """Molette crantée (bouton de réglage, serrage au pouce) : une étoile extrudée le long de son axe."""
    axe = axe.normalized()
    u, v = kit.repere_axe(axe)

    def etoile(c):
        return [c + u * (math.cos(math.pi * i / dents) * (r if i % 2 == 0 else r * creux))
                + v * (math.sin(math.pi * i / dents) * (r if i % 2 == 0 else r * creux)) for i in range(2 * dents)]

    f = kit.loft(pc, [etoile(centre), etoile(centre + axe * epaisseur)], mat)
    kit.chanfreiner(pc, 0.0005, f, angle_min=70)
    return f


def levier(pc, av0, av1, h, x, cote=-1, epaisseur=0.0022, hauteur=0.0062, mat="Acier"):
    """Levier de serrage rapide couché le long de l'embase, sur un flanc (cote -1 : à droite), son bout
    côté tireur relevé en prise de pouce."""
    xa, xb = x, x + cote * epaisseur
    f = kit.boite(pc, av0, av1, h - hauteur / 2, h + hauteur / 2, min(xa, xb), max(xa, xb), mat)
    kit.chanfreiner(pc, 0.0008, f)
    xc = x + cote * (epaisseur + 0.0012)
    f = kit.boite(pc, av0 - 0.0035, av0 + 0.0040, h - hauteur / 2 - 0.0010, h + hauteur / 2 + 0.0010,
                  min(x, xc), max(x, xc), mat)
    kit.chanfreiner(pc, 0.0010, f)


def tube_creux(pc, profil, h, r_int, x=0.0, n=20, mat=None, phase=None):
    """Tube creux d'axe parallèle au canon : profil = [(av, r_ext), ...] de l'arrière vers l'avant ;
    r_int, un rayon intérieur constant ou un par point du profil."""
    phase = math.pi / n if phase is None else phase
    r_ints = list(r_int) if isinstance(r_int, (list, tuple)) else [r_int] * len(profil)
    ext = [kit.cercle(P(av, h, x), AVANT, r, n, phase) for av, r in profil]
    inte = [kit.cercle(P(av, h, x), AVANT, ri, n, phase) for (av, _), ri in reversed(list(zip(profil, r_ints)))]
    return kit.loft(pc, ext + inte, mat, cyclique=True)


def _longueurs(pts):
    """Paramètre de chaque point d'une ligne brisée (x, h), de 0 à 1 selon la longueur parcourue."""
    acc_, l = [0.0], 0.0
    for a, b in zip(pts, pts[1:]):
        l += math.hypot(b[0] - a[0], b[1] - a[1])
        acc_.append(l)
    return [v / l for v in acc_]


def _au_parametre(pts, params, t):
    for i in range(len(pts) - 1):
        if params[i] - 1e-12 <= t <= params[i + 1] + 1e-12:
            u = (t - params[i]) / max(1e-12, params[i + 1] - params[i])
            return (pts[i][0] + (pts[i + 1][0] - pts[i][0]) * u, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * u)
    return pts[-1]


def profil_epais(pc, av0, av1, exterieur, interieur, mat=None):
    """Une pièce dont la section (x, h) est la bande entre deux lignes ouvertes, exterieur et interieur,
    parcourues dans le même sens, extrudée de av0 à av1 : un cadre de fenêtre, une visière, une selle sur
    un tube. Ses bouts sont faits de quads entre les deux lignes, jamais d'une face concave à beaucoup
    de côtés, que Blender ou Unity peuvent mal trianguler (vu sur la visière du HO-D)."""
    pe, pi = _longueurs(exterieur), _longueurs(interieur)
    T = sorted({round(t, 9) for t in pe + pi})
    ext = [_au_parametre(exterieur, pe, t) for t in T]
    inte = [_au_parametre(interieur, pi, t) for t in T]
    bm = pc.bm
    E = [[bm.verts.new(P(av, h, x)) for x, h in ext] for av in (av0, av1)]
    I = [[bm.verts.new(P(av, h, x)) for x, h in inte] for av in (av0, av1)]
    faces = []
    for i in range(len(T) - 1):
        faces.append(bm.faces.new((E[0][i], E[0][i + 1], E[1][i + 1], E[1][i])))
        faces.append(bm.faces.new((I[0][i + 1], I[0][i], I[1][i], I[1][i + 1])))
        for k in (0, 1):
            if (Vector(ext[i]) - Vector(inte[i])).length + (Vector(ext[i + 1]) - Vector(inte[i + 1])).length > 1e-7:
                q = (E[k][i + 1], E[k][i], I[k][i], I[k][i + 1])
                faces.append(bm.faces.new(q if k == 0 else tuple(reversed(q))))
    for j in (0, len(T) - 1):   # les deux pieds, s'ils ont une épaisseur
        if (Vector(ext[j]) - Vector(inte[j])).length > 1e-7:
            faces.append(bm.faces.new((E[0][j], I[0][j], I[1][j], E[1][j])))
    bmesh.ops.remove_doubles(bm, verts=[v for anneau in E + I for v in anneau], dist=1e-7)
    faces = [f for f in faces if f.is_valid]
    i_mat = pc.mat(mat) if isinstance(mat, str) else (mat or 0)
    for f in faces:
        f.material_index = i_mat
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def cadre(pc, av0, av1, exterieur, interieur, mat=None):
    """Cadre d'une fenêtre (vu de dos, un « ∩ »), extrudé le long du canon : exterieur et interieur
    sont des contours ouverts (x, h), du bas à gauche au bas à droite."""
    return profil_epais(pc, av0, av1, exterieur, interieur, mat)


def ruban_autour(a, pc, l, h, centre_h, av, largeur=0.006, mat="RubanNoir", incl=0.04, rabat=None, x=0.0):
    """Un tour de ruban (ou un collier) autour d'une section rectangulaire l × h, à av."""
    contour = kit.contour_rect(l, h, chanfrein=min(l, h) * 0.18, cx=x, cy=centre_h)
    kit.bande(pc, contour, P(av, 0.0), Vector((1, 0, 0)), Vector((0, 0, 1)), AVANT, largeur, 0.0010, mat,
              inclinaison=incl, irregularite=0.0004, rng=a.rng, rabat=rabat)


# --- vitre et réticule ------------------------------------------------------------------------------

def vitre_plane(a, coins, teinte="bleu", nom="Vitre"):
    """La vitre, un objet à part (contour plan, dans l'ordre), face au tireur."""
    pc = a.piece(nom, "Vitre_" + teinte)
    kit.quad(pc, coins, "Vitre_" + teinte, normale=VERS_TIREUR)
    ob = a.objet(pc, angle=None)
    a.vitres.append(ob)
    a.donnees["teinte"] = teinte
    return ob


def vitre_ronde(a, av, h, r, teinte="ambre", n=20, nom="Vitre", x=0.0):
    pc = a.piece(nom, "Vitre_" + teinte)
    kit.disque(pc, P(av, h, x), VERS_TIREUR, r, n, mat="Vitre_" + teinte)
    ob = a.objet(pc, angle=None)
    a.vitres.append(ob)
    a.donnees["teinte"] = teinte
    return ob


def trait(pc, p0, p1, ep, mat, normale=VERS_TIREUR):
    d = (p1 - p0).normalized()
    t = d.cross(normale).normalized() * (ep / 2)
    kit.quad(pc, [p0 - t, p1 - t, p1 + t, p0 + t], mat, normale=normale)


def arc(pc, centre, r, ep, mat, a0=0.0, a1=2 * math.pi, n=28, normale=VERS_TIREUR):
    pas = (a1 - a0) / n
    for i in range(n):
        u0, u1 = a0 + pas * i, a0 + pas * (i + 1)
        p = [centre + (DROITE * math.cos(u) + HAUT * math.sin(u)) * rr for u in (u0, u1) for rr in (r - ep / 2, r + ep / 2)]
        kit.quad(pc, [p[0], p[2], p[3], p[1]], mat, normale=normale)


FORMES_RETICULE = ("point", "cercle_point", "cercle_chevron", "losange_point", "tirets_point", "cercle_croix",
                   "triangle", "tirets_chevron")


def reticule(a, forme, couleur, av, h, rayon):
    """Le réticule de Rainbow Six tel qu'on le voit en visant, posé contre la vitre, pour les seuls
    rendus de contrôle : dans Unity, le shader du viseur le dessine à l'infini (fiche : forme, couleur).
    rayon : celui du cercle du réticule, ou sa demi-largeur."""
    assert forme in FORMES_RETICULE, forme
    mat = "ReticuleRouge" if couleur == "rouge" else "ReticuleVert"
    pc = a.piece("Reticule", mat)
    c = P(av, h) + VERS_TIREUR * 0.0004
    ep = rayon * 0.075

    def pt(u, v):
        return c + DROITE * (u * rayon) + HAUT * (v * rayon)

    if forme in ("point", "cercle_point", "losange_point", "tirets_point", "cercle_croix"):
        kit.disque(pc, c, VERS_TIREUR, rayon * (0.13 if forme == "point" else 0.07), 10, mat=mat)
    if forme in ("cercle_point", "cercle_chevron", "cercle_croix"):
        arc(pc, c, rayon, ep, mat)
    if forme == "cercle_croix":
        for u, v in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            trait(pc, pt(u * 0.62, v * 0.62), pt(u * 0.86, v * 0.86), ep, mat)
    if forme == "losange_point":
        coins = [pt(0, 0.75), pt(0.75, 0), pt(0, -0.75), pt(-0.75, 0)]
        for i in range(4):
            trait(pc, coins[i], coins[(i + 1) % 4], ep, mat)
    if forme in ("tirets_point", "tirets_chevron"):
        for s in (1, -1):
            trait(pc, pt(s * 0.32, 0), pt(s * 0.95, 0), ep * 1.3, mat)
    if forme in ("cercle_chevron", "tirets_chevron"):
        k = 0.42 if forme == "cercle_chevron" else 0.30
        trait(pc, pt(-k, -k * 0.75), pt(0, k * 0.15), ep * 1.2, mat)
        trait(pc, pt(0, k * 0.15), pt(k, -k * 0.75), ep * 1.2, mat)
    if forme == "triangle":
        kit.quad(pc, [pt(-0.30, -0.22), pt(0.30, -0.22), pt(0.0, 0.30)], mat, normale=VERS_TIREUR)
    ob = a.objet(pc, angle=None)
    a.controle.append(ob)
    a.objets.remove(ob)
    a.donnees["reticule"] = forme
    a.donnees["couleurReticule"] = couleur
    a.donnees["rayonReticule"] = rayon   # Unity en tire sa taille à l'écran (WeaponImporter)
    return ob


# --- studio et lancement ----------------------------------------------------------------------------

def studio(scene, a, vues_detail=()):
    """Lumières et caméras de contrôle, cadrées sur l'accessoire (petit : on le cadre de près)."""
    col = kit.collection(f"Studio — {scene.name}", scene.collection)
    scene.view_layers[0].update()
    mn, mx = ca.bornes(a.objets)
    c = (mn + mx) / 2
    taille = mx - mn
    L = max(taille.x, taille.y, taille.z)
    k = 0.45
    kit.lumiere("Lumiere_Cle", col, c + Vector((0.9, -0.5, 1.0)) * k, c, 45 * k * k, 1.0 * k)
    kit.lumiere("Lumiere_Remplissage", col, c + Vector((0.7, 0.9, 0.1)) * k, c, 16 * k * k, 1.5 * k)
    kit.lumiere("Lumiere_Contre", col, c + Vector((-0.9, 0.5, 0.7)) * k, c, 40 * k * k, 0.6 * k)
    kit.lumiere("Lumiere_Droite", col, c + Vector((-1.0, -0.4, 0.4)) * k, c, 18 * k * k, 1.2 * k)
    ortho = L * 1.45
    cams = {
        "arriere": (kit.camera("Cam_Arriere", col, c + Vector((0, 0.6, 0)), c, ortho=max(taille.x, taille.z) * 1.6), 1000, 1000),
        "trois_quarts": (kit.camera("Cam_Trois_Quarts", col, c + Vector((0.95, 0.55, 0.60)).normalized() * L * 3.6, c, 50),
                         1400, 1000),
        "avant_droit": (kit.camera("Cam_Avant_Droit", col, c + Vector((-0.95, -0.70, 0.45)).normalized() * L * 3.6, c, 50),
                        1400, 1000),
        "profil_gauche": (kit.camera("Cam_Profil_Gauche", col, c + Vector((0.6, 0, 0)), c, ortho=ortho), 1400, 1000),
        "dessus": (kit.camera("Cam_Dessus", col, c + Vector((0, 0, 0.6)), c, ortho=ortho), 1400, 1000),
    }
    cams["dessus"][0].rotation_euler = (0.0, 0.0, math.radians(90))
    for nom, position, cible, focale in vues_detail:
        cams[nom] = (kit.camera(f"Cam_{nom}", col, position, cible, focale), 1200, 1000)
    scene.camera = cams["trois_quarts"][0]
    return cams


def arguments(brut=None):
    if brut is None:
        brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--sortie")
    p.add_argument("--rendus")
    p.add_argument("--vues", default="")
    p.add_argument("--seuls", default="", help="codes des accessoires à construire (PR-A,HO-B...)")
    p.add_argument("--sur-arme", default="", help="script(s) d'arme où monter chaque accessoire pour un rendu")
    p.add_argument("--usure", default="", choices=[""] + list(ca.VARIANTES_USURE))
    return p.parse_args(list(brut))


def construire_accessoire(code, titre, categorie, nom_scene, construire, args, variante=None):
    """Construit un accessoire dans sa scène (recréée) ; rend ses vues si --rendus."""
    if variante:
        nom_scene = f"{nom_scene} — {ca.VARIANTES_USURE[variante]}"
    scene = ca.preparer_scene(nom_scene)
    if bpy.app.background:
        ca.retirer_scene_usine(scene)
    M = materiaux(code.replace("-", ""), variante)
    a = AtelierAccessoire(scene, code, titre, categorie, M)
    a.variante = variante
    construire(a)
    total = sum(kit.triangles(o) for o in a.objets)
    print(f"  {a.code:<6} {len(a.objets):>3} objets {total:>6} triangles   ({titre})")
    a.cams = studio(scene, a, getattr(construire, "vues", ()))
    if args.rendus:
        dossier = os.path.join(args.rendus, a.prefixe)
        os.makedirs(dossier, exist_ok=True)
        vues = [v for v in args.vues.split(",") if v] or list(a.cams)
        for nom in vues:
            if nom in a.cams:
                cam, l, h = a.cams[nom]
                kit.rendre(scene, cam, os.path.join(dossier, nom + ".png"), l, h)
    return a


def monter(a, scene_arme, point, nom=None, col=None):
    """Instancie l'accessoire dans la scène d'une arme (ou dans col), à un point (coordonnées du monde)."""
    a.col.instance_offset = a.racine.location
    e = bpy.data.objects.new(nom or f"Monte_{a.prefixe}", None)
    e.instance_type = "COLLECTION"
    e.instance_collection = a.col
    e.location = point
    (col or scene_arme.collection).objects.link(e)
    return e


_ARMES = {}


def arme(script):
    """Construit une arme (sans rendu ni enregistrement), une fois par script, et rend l'espace de son
    script : son atelier (ATELIER) et ses constantes (les courbes de son chargeur...)."""
    if script not in _ARMES:
        chemin = os.path.join(DOSSIER, script)
        espace = {"__name__": "__arme__", "__file__": chemin, "ARGS": []}
        with open(chemin, encoding="utf-8") as f:
            exec(compile(f.read(), chemin, "exec"), espace)
        _ARMES[script] = espace
    return _ARMES[script]


def point_de(at, nom):
    """La position d'un point d'accroche d'une arme (sa racine est à l'origine), cherché sous son groupe
    « Accessoires » : une pièce peut porter le même nom (la poignée pistolet s'appelle « Poignee »
    comme le point de la poignée avant ; Blender numérote alors le second)."""
    at.scene.view_layers[0].update()
    groupe = next(o for o in at.racine.children if o.name.split('.')[0] == at.nom('Accessoires'))
    point = next(o for o in groupe.children if o.name.split('.')[0] == at.nom(nom))
    return point.matrix_world.translation.copy()


def rendre_sur_arme(ateliers, script, dossier, point, cadrages, masquer=()):
    """Monte chaque accessoire sur une arme, au point d'accroche `point`, en masquant ce qu'il remplace,
    et rend les vues `cadrages` = {nom: (position de la caméra, cible, focale)}, relatives au point."""
    at = arme(script)["ATELIER"]
    scene = at.scene
    p = point_de(at, point)
    masques = [o for o in (bpy.data.objects.get(at.nom(n)) for n in masquer) if o is not None]
    for o in masques:
        o.hide_render = True
    studio = kit.collection(f"Studio — accessoires sur {at.code}", scene.collection)
    try:
        for a in ateliers:
            e = monter(a, scene, p)
            for nom, (dc, dt, focale) in cadrages.items():
                cam = kit.camera(f"Cam_{a.prefixe}_{nom}", studio, p + dt + dc, p + dt, focale)
                kit.rendre(scene, cam, os.path.join(dossier, f"{at.prefixe}_{a.prefixe}_{nom}.png"), 1400, 1000)
            bpy.data.objects.remove(e, do_unlink=True)
    finally:
        for o in masques:
            o.hide_render = False
    return at


def enregistrer(sortie):
    if bpy.app.background:
        bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=sortie, check_existing=False)
    print("Enregistré :", sortie)
