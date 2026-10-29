"""Les crosses de l'armurerie de Duel Arena (Blender 5.2, low poly) : une légère et une lourde par arme d'épaule
(GDD § 6, accessoires choisis le 2026-10-25 ; bible § 12 : des pièces du même stock que les armes, usées
comme elles).

Chacune se règle sur la crosse d'origine de son arme (le groupe « Crosse_Standard »), qu'elle remplace dans
le jeu : sa longueur, sa hauteur à la plaque de couche et à la carcasse.
- la crosse légère (CS-, « squelette ») : un cadre d'acier (un collier à la carcasse, une barre en haut, une
  jambe de force en bas, un montant à l'arrière) et une plaque de couche fine ; on épaule plus vite, l'arme
  recule plus. Sur une crosse de chasse à poignet droit (le FP-30, le FAP-30), où la main droite tient le
  poignet, la crosse d'origine est gardée jusqu'à 20 cm de la plaque de couche, coupée net, et le même cadre
  la prolonge ;
- la crosse lourde (CP-, « pleine ») : la crosse d'origine copiée pièce à pièce (ses matériaux compris) et
  élargie, un appui-joue réglable sur deux montants, des masses d'acier boulonnées sur les flancs et une
  épaisse plaque de couche ; l'arme recule moins, on épaule moins vite.

Chacune se construit dans le repère de son arme, puis elle est ramenée à son point d'accroche « Crosse » :
l'origine de l'accessoire. Les pistolets n'en ont pas.

  blender -b --factory-startup -P crosses.py -- [--seuls CS-FA07] [--rendus dossier] [--sur-arme 1]
"""

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
import commun_accessoires as acc  # noqa: E402

importlib.reload(acc)
ca = acc.ca
kit = acc.kit
from kit_lowpoly import AVANT, GAUCHE, HAUT, P, Piece  # noqa: E402

CROSSES = [   # (arme, son script)
    ("FA-07", "fusil_assaut.py"),
    ("FP-12", "fusil_precision.py"),
    ("FAP-03", "fusil_pompe.py"),
    ("FA-30", "serie30_fusil.py"),
    ("FX-1", "laser_fusil.py"),
    ("FP-30", "serie30_precision.py"),
    ("FX-2", "laser_precision.py"),
    ("FAP-30", "serie30_pompe.py"),
    ("FX-3", "laser_dispersion.py"),
]

POIGNET = {"FP-30", "FAP-30"}    # leur crosse de chasse porte le poignet que tient la main droite

POIGNET_GARDE = 0.20             # ce qu'une crosse à poignet garde d'elle-même, depuis la plaque de couche

LEGERE, LOURDE = 1, 2            # StockKind dans le jeu
LARGEUR_LEGERE = 0.80            # la légère, amincie
LARGEUR_LOURDE = 1.22            # la lourde, élargie


# --- copier les pièces d'une arme ------------------------------------------------------------------

descendants = acc.descendants


def groupe_de(at, nom):
    return next(o for o in at.racine.children if o.name.split(".")[0] == at.nom(nom))


def copier(a, at, ob):
    return acc.copier_piece(a, at, ob)


def sommets_avhx(pieces):
    """Les sommets des pièces, en (av, h, x)."""
    return [(-v.co.y, v.co.z, v.co.x) for pc in pieces for v in pc.bm.verts]


class Gabarit:
    """La forme de la crosse d'origine, gardée avant qu'on la retouche : sa hauteur et sa largeur à une position
    av se lisent sur une vraie coupe de ses pièces (un long prisme n'a de sommets qu'à ses bouts : les sommets
    proches d'une position n'en disaient rien, vu sur le FAP-30)."""

    def __init__(self, pieces):
        self.bms = [pc.bm.copy() for pc in pieces]

    def coupe(self, av):
        hs, xs = [], []
        for source in self.bms:
            bm = source.copy()
            geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
            res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=P(av, 0.0), plane_no=Vector((0.0, 1.0, 0.0)))
            for e in res["geom_cut"]:
                if isinstance(e, bmesh.types.BMVert):
                    hs.append(e.co.z)
                    xs.append(abs(e.co.x))
            bm.free()
        return (min(hs), max(hs), max(xs)) if hs else None

    def etendue(self, av, demi=0.0):
        """(h bas, h haut, demi-largeur) des coupes à av, et à av ± demi."""
        coupes = [c for c in (self.coupe(av - demi), self.coupe(av), self.coupe(av + demi)) if c]
        if not coupes:
            return None
        return min(c[0] for c in coupes), max(c[1] for c in coupes), max(c[2] for c in coupes)

    def liberer(self):
        for bm in self.bms:
            bm.free()


def tranche(pts, av, demi=0.012):
    """L'étendue (h bas, h haut, demi-largeur) des sommets à moins de `demi` de la position av."""
    sel = [(h, abs(x)) for a_, h, x in pts if abs(a_ - av) <= demi]
    if not sel:
        return None
    return min(h for h, _ in sel), max(h for h, _ in sel), max(x for _, x in sel)


# --- les deux crosses ---------------------------------------------------------------------------------

def construire_crosse(code_arme, sorte, espace, point):
    at = espace["ATELIER"]

    def construire(a):
        a.transformation = Matrix.Translation(-point)   # dessinée dans le repère de l'arme
        groupe = groupe_de(at, "Crosse_Standard")
        sources = [o for o in descendants(groupe) if o.type == "MESH"]
        legere_cadre = sorte == LEGERE and code_arme not in POIGNET
        if sorte == LEGERE and not legere_cadre:
            # une crosse allégée n'emporte pas les cartouches de rechange de l'ancienne
            sources = [o for o in sources if "Cartouch" not in o.name]
        pieces = [copier(a, at, o) for o in sources]
        largeur = LARGEUR_LEGERE if sorte == LEGERE else LARGEUR_LOURDE
        for pc in pieces:
            bmesh.ops.scale(pc.bm, vec=(largeur, 1.0, 1.0), verts=pc.bm.verts)
        pts = sommets_avhx(pieces)
        g = Gabarit(pieces)
        av_r = min(p[0] for p in pts)                         # l'arrière de la plaque de couche
        av_f = min(max(p[0] for p in pts), point.y * -1.0)    # l'avant, au plus au point d'accroche
        corps = next((pc for pc in pieces if pc.nom.endswith("Crosse_Corps")), pieces[0])
        mat_corps = corps.materiaux[0] if corps.materiaux else "Polymere"

        if legere_cadre:
            for pc in pieces:
                pc.bm.free()
            cadre(a, g, av_r, av_f, mat_corps, socle=-point.y, h_socle=point.z)
        elif sorte == LEGERE:
            # le poignet, que tient la main droite, puis le cadre jusqu'à la plaque de couche
            coupe = av_r + POIGNET_GARDE
            for pc in pieces:
                if couper_arriere(pc, coupe):
                    a.objet(pc)
            cadre(a, g, av_r, coupe, mat_corps, collier=False)
        else:
            for pc in pieces:
                a.objet(pc)
            alourdir(a, g, av_r, av_f, mat_corps)
        g.liberer()

        a.donnees["arme"] = code_arme
        a.donnees["variante"] = sorte
        a.donnees["masque"] = ["Crosse_Standard"]
    return construire


def barre(pc, p0, p1, epaisseur, demi, mat):
    """Une barre plate de section epaisseur × 2·demi, de p0 à p1 (av, h), ses bouts coupés d'équerre."""
    d = Vector((p1[0] - p0[0], p1[1] - p0[1]))
    n = Vector((-d.y, d.x)).normalized() * (epaisseur / 2)
    profil = [(p0[0] + n.x, p0[1] + n.y), (p1[0] + n.x, p1[1] + n.y), (p1[0] - n.x, p1[1] - n.y), (p0[0] - n.x, p0[1] - n.y)]
    if (Vector(profil[1]) - Vector(profil[0])).cross(Vector(profil[2]) - Vector(profil[1])) < 0:
        profil.reverse()
    f = kit.prisme(pc, profil, -demi, demi, mat)
    kit.chanfreiner(pc, min(0.0015, epaisseur * 0.2), f)
    return f


def couper_arriere(pc, coupe):
    """Garde d'une pièce ce qui est devant av = coupe, et ferme la coupe. Faux s'il n'en reste rien."""
    bm = pc.bm
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    # le plan y = -coupe ; son côté positif (y > -coupe) est l'arrière : on l'efface
    res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=P(coupe, 0.0), plane_no=Vector((0.0, 1.0, 0.0)),
                                 clear_outer=True)
    bord = [e for e in bm.edges if e.is_boundary]
    if bord:
        nouvelles = bmesh.ops.holes_fill(bm, edges=bord, sides=0)["faces"]
        for f in nouvelles:
            f.material_index = 0
    if not bm.faces:
        bm.free()
        return False
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return True


def cadre(a, g, av_r, av_f, mat_corps, collier=True, socle=None, h_socle=None):
    """La légère d'une crosse : un cadre d'acier à la mesure de l'ancienne. Un collier à la carcasse (il
    enserre le tube de crosse du FA-07, et fait au moins 34 mm de haut autour du point d'accroche `socle`,
    `h_socle`), une barre en haut jusqu'au talon, une jambe de force du bas du collier au bec de la crosse, un
    montant à l'arrière et une plaque de couche fine. Sans collier, le cadre part de la coupe d'une crosse à
    poignet (av_f), dans laquelle ses barres s'enfoncent."""
    avant = g.etendue(av_f - 0.006) or (0.05, 0.15, 0.02)
    arriere = g.etendue(av_r + 0.012) or (0.03, 0.15, 0.02)
    f_bas, f_haut, f_demi = avant
    r_bas, r_haut, _ = arriere
    col = 0.024 if collier else 0.0              # la longueur du collier
    if collier:
        if h_socle is not None:
            f_bas, f_haut = min(f_bas, h_socle - 0.017), max(f_haut, h_socle + 0.017)
        demi_col = max(0.018, f_demi * 1.05)
        pc = a.piece("Collier", mat_corps)
        f = kit.prisme(pc, [(av_f, f_haut), (av_f - col, f_haut), (av_f - col, f_bas), (av_f, f_bas)], -demi_col, demi_col, mat_corps)
        kit.chanfreiner(pc, 0.0025, f)
        for s in (1, -1):   # la vis de blocage, au flanc gauche ; son écrou à droite
            x = s * (demi_col + 0.0002)
            kit.vis_six_pans(pc, P(av_f - col / 2, (f_bas + f_haut) / 2, x), GAUCHE * s, r=0.0034, h=0.0018,
                             mat="AcierBrut" if s > 0 else "Acier")
        a.objet(pc)

    pc = a.piece("Cadre", "Acier")
    entree = 0.004 if collier else 0.014         # sans collier, les barres entrent dans la coupe
    haut0, haut1 = (av_f - col + entree, f_haut - 0.008), (av_r + 0.010, r_haut - 0.007)
    bas0, bas1 = (av_f - col + entree, f_bas + 0.008), (av_r + 0.010, r_bas + 0.009)
    barre(pc, haut0, haut1, 0.011, 0.0085, "Acier")
    barre(pc, bas0, bas1, 0.009, 0.0075, "Acier")
    f = kit.prisme(pc, [(av_r + 0.004, r_haut - 0.002), (av_r + 0.017, r_haut - 0.002), (av_r + 0.017, r_bas + 0.002),
                        (av_r + 0.004, r_bas + 0.002)], -0.0105, 0.0105, "Acier")   # le montant arrière
    kit.chanfreiner(pc, 0.0015, f)
    # trois trous d'allègement dans le montant, et un dans la barre du haut
    a.objet(pc)
    cp = Piece("trous", "Interieur")
    hauteur = r_haut - r_bas
    for i in range(3):
        hh = r_bas + hauteur * (0.27 + 0.23 * i)
        kit.cylindre(cp, P(av_r + 0.0105, hh, 0.05), P(av_r + 0.0105, hh, -0.05), min(0.0032, hauteur * 0.06), 8, mat="Interieur")
    m = ((haut0[0] + haut1[0]) / 2, (haut0[1] + haut1[1]) / 2)
    kit.cylindre(cp, P(m[0], m[1], 0.05), P(m[0], m[1], -0.05), 0.0028, 8, mat="Interieur")
    bmesh.ops.transform(cp.bm, matrix=a.transformation, verts=cp.bm.verts)
    a.couper(a.objets[-1], cp, auto=True)

    pc = a.piece("Plaque_Fine", "Caoutchouc")
    f = kit.prisme(pc, [(av_r + 0.004, r_haut + 0.002), (av_r - 0.005, r_haut), (av_r - 0.007, r_haut - 0.006),
                        (av_r - 0.007, r_bas + 0.006), (av_r - 0.005, r_bas), (av_r + 0.004, r_bas - 0.002)],
                   -0.0150, 0.0150, "Caoutchouc")
    kit.chanfreiner(pc, 0.0020, f)
    a.objet(pc)
    a.donnees["cadre"] = True


def alourdir(a, g, av_r, av_f, mat_corps):
    """La lourde : une épaisse plaque de couche, un appui-joue réglable sur deux montants, et une masse
    d'acier boulonnée sur chaque flanc."""
    arriere = g.etendue(av_r + 0.006, 0.004)
    bas, haut, demi = arriere if arriere else (0.03, 0.15, 0.022)
    ep = 0.020
    pc = a.piece("Plaque_Lourde", "Caoutchouc")
    profil = [(av_r + 0.002, haut - 0.004), (av_r - ep + 0.004, haut - 0.006), (av_r - ep, haut - 0.014),
              (av_r - ep, bas + 0.012), (av_r - ep + 0.004, bas + 0.005), (av_r + 0.002, bas + 0.003)]
    f = kit.prisme(pc, profil, -(demi + 0.002), demi + 0.002, "Caoutchouc")
    kit.chanfreiner(pc, 0.003, f)
    for i in range(3):   # les rainures de la plaque, sur les flancs
        hh = bas + (haut - bas) * (0.3 + 0.2 * i)
        for x in (demi + 0.0021, -(demi + 0.0021)):
            kit.boite(pc, av_r - ep + 0.003, av_r - 0.002, hh - 0.0012, hh + 0.0012,
                      min(x, x + math.copysign(0.0006, x)), max(x, x + math.copysign(0.0006, x)), "Interieur")
    a.objet(pc)

    # l'appui-joue, au-dessus du busc, sur deux montants d'acier
    av0, av1 = av_r + 0.045, av_r + 0.165
    busc = max((g.etendue(av) or (0, 0, 0))[1] for av in (av0, (av0 + av1) / 2, av1))
    pc = a.piece("Appui_Joue_Lourd", mat_corps)
    profil = [(av0, busc + 0.006), (av1, busc + 0.006), (av1 - 0.006, busc + 0.022), (av0 + 0.010, busc + 0.024)]
    f = kit.prisme(pc, profil, -0.0125, 0.0125, mat_corps)
    kit.chanfreiner(pc, 0.0025, f)
    a.objet(pc)
    pc = a.piece("Montants", "Acier")
    for av in (av0 + 0.025, av1 - 0.030):
        f = kit.cylindre(pc, P(av, busc - 0.008), P(av, busc + 0.007), 0.0040, 8, mat="Acier")
        kit.chanfreiner(pc, 0.0005, f, angle_min=60)
    acc.molette(pc, P(av0 + 0.025, busc - 0.002, 0.0130), GAUCHE, 0.0060, 0.0040, dents=10, mat="Acier")
    a.objet(pc)

    # une masse d'acier sur chaque flanc, boulonnée
    milieu = g.etendue(av_r + 0.055, 0.010)
    m_bas, m_haut, m_demi = milieu if milieu else (bas, haut, demi)
    hc = (m_bas + m_haut) / 2
    hm = min(0.020, 0.30 * (m_haut - m_bas))
    pc = a.piece("Masses", "Acier")
    for s in (1, -1):
        x0 = s * (m_demi + 0.0004)
        x1 = s * (m_demi + 0.0040)
        f = kit.boite(pc, av_r + 0.024, av_r + 0.086, hc - hm, hc + hm, min(x0, x1), max(x0, x1), "Acier")
        kit.chanfreiner(pc, 0.0010, f)
        for av in (av_r + 0.034, av_r + 0.076):
            for hh in (hc - hm * 0.55, hc + hm * 0.55):
                kit.vis_six_pans(pc, P(av, hh, x1), GAUCHE * s, r=0.0028, h=0.0016, mat="AcierBrut")
    a.objet(pc)


# --- lancement -----------------------------------------------------------------------------------------

def rendre_montee(a, script, dossier):
    """La crosse montée sur son arme, la crosse d'origine cachée : de profil gauche et de trois quarts arrière."""
    at = acc.arme(script)["ATELIER"]
    scene = at.scene
    p = acc.point_de(at, "Crosse")
    cachees = [o for o in descendants(groupe_de(at, "Crosse_Standard"))]
    for o in cachees:
        o.hide_render = True
    studio = kit.collection(f"Studio — crosses sur {at.code}", scene.collection)
    try:
        e = acc.monter(a, scene, p)
        # cadré sur la crosse elle-même (celle d'un FP-30 fait 37 cm, et part du poignet)
        mn, mx = ca.bornes(a.objets)
        centre = (mn + mx) / 2 - a.racine.matrix_world.translation
        recul = max(0.55, (mx - mn).y * 1.7)
        cadrages = {"profil": (Vector((recul, 0.0, 0.0)), centre + Vector((0.0, 0.03, 0.0)), 50),
                    "trois_quarts": (Vector((0.36, 0.30, 0.16)) * (recul / 0.55), centre, 50)}
        for nom, (dc, dt, focale) in cadrages.items():
            cam = kit.camera(f"Cam_{a.prefixe}_{nom}", studio, p + dt + dc, p + dt, focale)
            kit.rendre(scene, cam, os.path.join(dossier, f"{a.prefixe}_{nom}.png"), 1400, 1000)
        bpy.data.objects.remove(e, do_unlink=True)
    finally:
        for o in cachees:
            o.hide_render = False


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = []
    for code_arme, script in CROSSES:
        for sorte, lettre, titre in ((LEGERE, "CS", "Crosse légère"), (LOURDE, "CP", "Crosse lourde")):
            code = f"{lettre}-{code_arme.replace('-', '')}"
            if seuls and code not in seuls:
                continue
            espace = acc.arme(script)
            point = acc.point_de(espace["ATELIER"], "Crosse")
            a = acc.construire_accessoire(code, f"{titre} {code_arme}", "Crosse", f"{titre} — {code_arme}",
                                          construire_crosse(code_arme, sorte, espace, point), args, args.usure or None)
            a.script_arme = script
            ateliers.append(a)
            print(f"    {code} : {a.donnees}")
            if args.rendus and args.sur_arme:
                rendre_montee(a, script, args.rendus)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
