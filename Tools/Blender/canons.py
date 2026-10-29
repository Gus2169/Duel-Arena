"""Ce qui se monte au canon des armes de Duel Arena (Blender 5.2, low poly) : un seul emplacement, comme dans Rainbow
Six (GDD § 6, l'utilisateur, 2026-10-29) ; sur une arme laser, des émetteurs à elle (bible § 12).

- le compensateur (CM-, sur une arme laser le stabilisateur ST-) : un manchon percé de lumières sur le dessus ;
  sur une arme laser, un anneau tenu devant l'émetteur par trois barrettes, sa lèvre allumée ;
- le frein de bouche (FB-, le dissipateur DS-) : un bloc large ouvert de deux grandes fenêtres sur les flancs ;
  sur une arme laser, des ailettes de refroidissement autour d'un cœur allumé ;
- le canon long (CL-, le focaliseur long FL-) et le canon court (CC-, le focaliseur court FC-) : les pièces de
  l'avant de l'arme, copiées (leurs matériaux compris), allongées ou raccourcies là où leur section ne change
  pas (aucun sommet entre les deux plans : un canon, une bande ventilée, une gaine) ; ce qui est devant (la
  bouche, le guidon, la tête) suit. Là où l'arme ne s'y prête pas, un ajout : un canon qui dépasse de la culasse
  (PA-05), une rallonge du fourreau (RV-30), une lèvre évasée devant l'émetteur (le focaliseur court des FX-3
  et FX-4). Le FA-30 court perd son compensateur et finit sur une couronne.

Chacun se dessine dans le repère de son arme, puis il est ramené à sa bouche (le repère « Bouche ») : l'origine
de l'accessoire. Il porte sa propre bouche (là où partent la flamme et l'éclat du tir chargé) et les points
d'accroche qu'il déplace (« Accessoires/Laser », « Accessoires/Chargeur » : la tête du FAP-30 recule avec son
canon court) ; sa fiche nomme les pièces de l'arme qu'il remplace (`masque`, les noms d'Unity).

Seules les armes dont les dégâts baissent avec la distance prennent les canons long et court (l'utilisateur,
2026-10-29) : sur les autres, l'un n'aurait que sa contrepartie, l'autre que son avantage.

  blender -b --factory-startup -P canons.py -- [--seuls CM-FA07,CL-FAP30] [--rendus dossier --sur-arme 1]
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
import exporter_armes as ex  # noqa: E402

importlib.reload(acc)
ca = acc.ca
kit = acc.kit
from kit_lowpoly import AVANT, P, Piece  # noqa: E402

COMPENSATEUR, FREIN, LONG, COURT = 1, 2, 3, 4   # BarrelKind dans le jeu

# Le bout du canon de chaque arme : où se pose ce qui se visse à la bouche (`debut`, si ce n'est pas la bouche :
# devant la culasse du PA-05, au bout des branches du FX-2 et du FX-3, ou à la place d'une pièce de bouche
# d'origine, `masque`), le rayon du canon à cet endroit, et le dessin d'un bloc qui prolonge l'arme au lieu d'un
# manchon (la culasse d'un pistolet, le fourreau du revolver). Sur une arme laser, `recul` : jusqu'où le collier
# de l'émetteur revient sur l'arme (il enserre le bout de l'émetteur du FX-1, les pointes de la fourche du FX-2) ;
# `cadre` : la tête rectangulaire du FX-3, ses quatre branches pincées par un cadre (centre en hauteur, demi-
# tailles du dehors et du dedans), son faisceau 8 mm au-dessus du centre.
ARMES = {
    "FA-07": dict(script="fusil_assaut.py", debut=0.612, r=0.0085, masque=["Cache_Flamme"]),
    "FP-12": dict(script="fusil_precision.py", debut=0.797, r=0.0110, masque=["Frein_Bouche"]),
    "FAP-03": dict(script="fusil_pompe.py", r=0.0142),
    "PA-05": dict(script="pistolet.py", debut=0.165, r=0.0068, bloc="culasse"),
    "FA-30": dict(script="serie30_fusil.py", debut=0.638, r=0.0090, masque=["Compensateur"]),
    "FX-1": dict(script="laser_fusil.py", r=0.0175, laser=True, recul=0.010),
    "FP-30": dict(script="serie30_precision.py", debut=0.836, r=0.0100),
    "FX-2": dict(script="laser_precision.py", debut=0.902, r=0.0200, laser=True, recul=0.014),
    "FAP-30": dict(script="serie30_pompe.py", r=0.0145),
    "FX-3": dict(script="laser_dispersion.py", debut=0.752, r=0.0290, laser=True,
                 cadre=(0.1066, (0.030, 0.040), (0.0175, 0.0225))),
    "RV-30": dict(script="serie30_revolver.py", r=0.0085, bloc="fourreau"),
    "FX-4": dict(script="laser_pistolet.py", r=0.0132, laser=True),
}

# Les armes dont les dégâts baissent avec la distance (WeaponData.HasFalloff) : celles qui prennent les canons.
AVEC_BAISSE = ("FAP-03", "PA-05", "FA-30", "FAP-30", "FX-3", "RV-30", "FX-4")

# Les canons allongés en étirant l'arme : (le plan où elle s'allonge, de combien). Choisis là où aucune pièce
# qui le traverse n'a de sommet (les relevés de l'avant de chaque arme, 2026-10-29).
LONGS = {
    "FA-30": (0.610, 0.080),
    "FAP-03": (0.640, 0.100),
    "FAP-30": (0.600, 0.100),
    "FX-3": (0.650, 0.080),
    "FX-4": (0.168, 0.040),
}
# Les canons raccourcis : (la tranche retirée, de av0 à av1 ; les pièces cachées sans être copiées : les
# marquages du fourreau du RV-30, que la tranche couperait au milieu des lettres).
COURTS = {
    "FAP-03": (0.621, 0.673, ()),
    "FAP-30": (0.581, 0.631, ()),
    "PA-05": (0.1265, 0.1465, ()),
    "RV-30": (0.125, 0.165, ("Marquages",)),
}


# --- la géométrie de l'arme ------------------------------------------------------------------------------

def court(at, ob):
    return ob.name.split(".")[0][len(at.prefixe) + 1:]


def nom_unity(at, ob):
    """Le nom d'une pièce de l'arme dans Unity (l'export renomme celles qui portent le nom d'un repère)."""
    return ex.RENOMMAGES_PIECES.get(court(at, ob), court(at, ob))


def reperes(at):
    return {court(at, e): e for e in at.racine.children if e.type == "EMPTY"}


def bouche_de(at):
    at.scene.view_layers[0].update()
    return reperes(at)["Bouche"].matrix_world.translation.copy()


def accroches(at):
    """Les points d'accroche de l'arme : {nom : position}."""
    at.scene.view_layers[0].update()
    groupe = reperes(at).get("Accessoires")
    return {court(at, e): e.matrix_world.translation.copy() for e in groupe.children} if groupe else {}


def av_de(v):
    return -v.y


def etendue_av(ob):
    avs = [-(ob.matrix_world @ v.co).y for v in ob.data.vertices]
    return (min(avs), max(avs)) if avs else (0.0, 0.0)


def couper_plan(bm, av):
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=P(av, 0.0), plane_no=Vector((0.0, -1.0, 0.0)))


def etirer(pc, c, longueur):
    """Allonge une pièce : ce qui est devant le plan av = c avance de `longueur` ; les faces qui le traversent
    s'étirent (entre deux sommets, la section ne change pas)."""
    couper_plan(pc.bm, c)
    for v in pc.bm.verts:
        if av_de(v.co) > c + 1e-6:
            v.co.y -= longueur


def raccourcir(pc, c0, c1):
    """Raccourcit une pièce : la tranche entre av = c0 et c1 disparaît, ce qui est devant recule ; les deux coupes se
    ressoudent (une section qui ne change pas s'y retrouve à l'identique), ou se referment. Faux s'il ne reste rien."""
    bm = pc.bm
    couper_plan(bm, c0)
    couper_plan(bm, c1)
    dedans = [f for f in bm.faces if c0 + 1e-6 < av_de(f.calc_center_median()) < c1 - 1e-6]
    bmesh.ops.delete(bm, geom=dedans, context="FACES")
    aretes = [e for e in bm.edges if c0 + 1e-6 < av_de((e.verts[0].co + e.verts[1].co) / 2) < c1 - 1e-6]
    bmesh.ops.delete(bm, geom=aretes, context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context="VERTS")
    for v in bm.verts:
        if av_de(v.co) >= c1 - 1e-6:
            v.co.y += c1 - c0
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=2e-5)
    bord = [e for e in bm.edges if e.is_boundary]
    if bord:
        bmesh.ops.holes_fill(bm, edges=bord, sides=0)
    if not bm.faces:
        bm.free()
        return False
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return True


# --- ce qui se visse à la bouche ---------------------------------------------------------------------------

def couronne(a, av, h, r, mat="Bouche", r_ame=None):
    """La face avant percée de l'âme : une couronne (noircie par la suie), un disque sombre au fond."""
    r_ame = r * 0.58 if r_ame is None else r_ame
    pc = a.piece("Couronne", mat)
    kit.anneau(pc, av - 0.0030, av, h, r, r_ame, 12, mat)
    kit.disque(pc, P(av - 0.0026, h), AVANT, r_ame * 1.02, 12, mat="Interieur")
    a.objet(pc)


def creuser(a, ob, boites):
    """Taille des boîtes (av0, av1, h0, h1, x0, x1) dans une pièce déjà faite : lumières, fenêtres."""
    cp = Piece("coupe", "Interieur")
    for av0, av1, h0, h1, x0, x1 in boites:
        kit.boite(cp, av0, av1, h0, h1, x0, x1, "Interieur")
    bmesh.ops.transform(cp.bm, matrix=a.transformation, verts=cp.bm.verts)
    a.couper(ob, cp, auto=True)


def section_bloc(espace, bloc):
    """La section (x, h) qu'un bloc prolonge : l'avant de la culasse du PA-05, celui du fourreau du RV-30."""
    if bloc == "culasse":
        return kit.mise_a_echelle(espace["SECTION_CULASSE"], 0.86, (0.0, 0.064))
    return list(espace["FOURREAU"][-1][1])


def bloc_lofte(a, nom, mat, section, av0, av1, echelle_x=1.0, chanfrein=0.0015):
    """Un bloc de la section donnée, de av0 à av1 (élargi de echelle_x), chanfreiné."""
    s = [(x * echelle_x, h) for x, h in section]
    pc = a.piece(nom, mat)
    f = kit.loft(pc, kit.sections_av([(av0, s), (av1, s)]), mat)
    kit.chanfreiner(pc, chanfrein, f)
    return a.objet(pc)


def compensateur(a, av0, h, r, section=None):
    """Un manchon, son écrou de blocage, trois lumières sur le dessus, sa couronne ; ou un bloc qui prolonge la
    culasse ou le fourreau (section), deux lumières. Rend sa bouche."""
    if section is not None:
        longueur = 0.026
        ob = bloc_lofte(a, "Compensateur", "Acier", section, av0 - 0.001, av0 + longueur)
        haut = max(hh for _, hh in section)
        demi = max(x for x, _ in section)
        creuser(a, ob, [(av0 + c - 0.0024, av0 + c + 0.0024, h + 0.004, haut + 0.01, -demi * 0.42, demi * 0.42)
                        for c in (0.009, 0.018)])
        couronne(a, av0 + longueur + 0.001, h, r + 0.0018)
        return av0 + longueur + 0.001
    rc = r + 0.0034
    longueur = min(0.062, max(0.042, 4.6 * rc))
    pc = a.piece("Compensateur", "Acier")
    f = kit.cylindre(pc, P(av0 - 0.002, h), P(av0 + longueur - 0.002, h), rc, 12, mat="Acier")
    kit.chanfreiner(pc, 0.0012, f, angle_min=60)
    f = kit.cylindre(pc, P(av0 - 0.004, h), P(av0 + 0.008, h), rc + 0.0012, 12, mat="AcierBrut")   # l'écrou
    kit.chanfreiner(pc, 0.0008, f, angle_min=60)
    ob = a.objet(pc)
    pas = longueur / 4.4
    creuser(a, ob, [(av0 + longueur - pas * (i + 1.0) - 0.0032, av0 + longueur - pas * (i + 1.0) + 0.0032,
                     h + rc * 0.30, h + rc + 0.01, -rc * 0.42, rc * 0.42) for i in range(3)])
    couronne(a, av0 + longueur, h, rc - 0.0006)
    return av0 + longueur


def frein(a, av0, h, r, section=None):
    """Un bloc large ouvert de deux grandes fenêtres d'un flanc à l'autre (on voit au travers), sa couronne."""
    if section is not None:
        longueur = 0.030
        ob = bloc_lofte(a, "Frein", "Acier", section, av0 - 0.001, av0 + longueur, echelle_x=1.12)
        bas, haut = min(hh for _, hh in section), max(hh for _, hh in section)
        creuser(a, ob, [(av0 + longueur * t0, av0 + longueur * t1, h - (h - bas) * 0.55, h + (haut - h) * 0.45, -0.06, 0.06)
                        for t0, t1 in ((0.16, 0.44), (0.56, 0.84))])
        couronne(a, av0 + longueur + 0.001, h, r + 0.0018)
        return av0 + longueur + 0.001
    longueur = min(0.072, max(0.050, 5.2 * r))
    dx, dh = r * 1.45 + 0.004, r * 1.10 + 0.003
    octo = [(-dx * 0.70, -dh), (dx * 0.70, -dh), (dx, -dh * 0.55), (dx, dh * 0.55),
            (dx * 0.70, dh), (-dx * 0.70, dh), (-dx, dh * 0.55), (-dx, -dh * 0.55)]
    ob = bloc_lofte(a, "Frein", "Acier", [(x, h + hh) for x, hh in octo], av0 - 0.001, av0 + longueur)
    creuser(a, ob, [(av0 + longueur * t0, av0 + longueur * t1, h - dh * 0.62, h + dh * 0.62, -0.08, 0.08)
                    for t0, t1 in ((0.18, 0.43), (0.55, 0.82))])
    couronne(a, av0 + longueur + 0.001, h, min(dx, dh) * 0.62)
    return av0 + longueur + 0.001


def stabilisateur(a, av0, h, r, recul=0.002):
    """Arme laser : un anneau focaliseur tenu devant l'émetteur par trois barrettes, sa lèvre allumée."""
    av1 = av0 + max(0.030, 1.6 * r)
    pc = a.piece("Stabilisateur", "Peinture")
    kit.anneau(pc, av1 - 0.008, av1, h, r * 1.15, r * 0.80, 12, "Peinture")
    kit.anneau(pc, av0 - recul, av0 + 0.005, h, r * 1.04, r * 0.72, 12, "Ceramique")   # le collier sur l'émetteur
    for i in range(3):
        ang = math.radians(90 + 120 * i)
        x, z = math.cos(ang) * r * 0.97, math.sin(ang) * r * 0.97
        kit.cylindre(pc, P(av0 + 0.004, h + z, x), P(av1 - 0.006, h + z, x), 0.0022, 6, mat="Acier")
    a.objet(pc)
    pc = a.piece("Levre", "Lueur")
    kit.anneau(pc, av1 - 0.0075, av1 + 0.0005, h, r * 0.82, r * 0.70, 12, "Lueur")
    a.objet(pc)
    return av1 + 0.0005


def dissipateur(a, av0, h, r, recul=0.002):
    """Arme laser : des ailettes de refroidissement autour d'un cœur allumé, devant l'émetteur."""
    n, pas = 5, 0.0062
    av1 = av0 + 0.006 + n * pas
    pc = a.piece("Dissipateur", "Acier")
    kit.anneau(pc, av0 - recul, av0 + 0.006, h, r * 1.10, r * 0.66, 12, "Peinture")   # le collier
    for i in range(n):
        c = av0 + 0.006 + i * pas
        kit.anneau(pc, c, c + 0.0024, h, r * 1.55, r * 0.62, 12, "Acier")
    kit.anneau(pc, av1 - 0.002, av1 + 0.004, h, r * 1.10, r * 0.66, 12, "Peinture")   # le bout
    a.objet(pc)
    pc = a.piece("Coeur", "Lueur")
    kit.cylindre(pc, P(av0, h), P(av1, h), r * 0.66, 12, mat="Lueur", bouts=False)
    a.objet(pc)
    return av1 + 0.004


def focaliseur_court(a, av0, h, r):
    """Arme laser, le focaliseur court : une lèvre évasée devant l'émetteur, qui ouvre le faisceau plus tôt."""
    longueur = 0.018
    pc = a.piece("Focaliseur", "Peinture")
    n = 12
    dehors = [(av0 - 0.002, r * 1.02), (av0 + longueur * 0.5, r * 1.10), (av0 + longueur, r * 1.34)]
    dedans = [(av0 + longueur, r * 1.20), (av0 + longueur * 0.5, r * 0.94), (av0 - 0.002, r * 0.86)]
    boucles = [kit.cercle(P(av, h), AVANT, rr, n, math.pi / n) for av, rr in dehors + dedans]
    kit.loft(pc, boucles, "Peinture", cyclique=True)
    a.objet(pc)
    pc = a.piece("Levre", "Lueur")
    kit.anneau(pc, av0 + longueur - 0.0015, av0 + longueur + 0.0005, h, r * 1.28, r * 1.22, 12, "Lueur")
    a.objet(pc)
    return av0 + longueur + 0.0005


def rectangle(av, ch, demi):
    dx, dh = demi
    return [P(av, ch - dh, -dx), P(av, ch - dh, dx), P(av, ch + dh, dx), P(av, ch + dh, -dx)]


def cadre(pc, av0, av1, ch, dehors, dedans, mat):
    """Un cadre rectangulaire creux, d'axe parallèle au canon, de av0 à av1 (demi-tailles du dehors et du dedans)."""
    return kit.loft(pc, [rectangle(av0, ch, dehors), rectangle(av1, ch, dehors), rectangle(av1, ch, dedans),
                         rectangle(av0, ch, dedans)], mat, cyclique=True)


def plus(demi, dx, dh):
    return (demi[0] + dx, demi[1] + dh)


def stabilisateur_cadre(a, av0, ch, dehors, dedans):
    """FX-3 : un cadre qui pince les quatre branches, un second cadre devant, tenu par quatre barrettes dans le
    prolongement des branches, sa lèvre allumée."""
    av1 = av0 + 0.036
    ouverture = plus(dedans, 0.004, 0.008)
    pc = a.piece("Stabilisateur", "Peinture")
    cadre(pc, av0 - 0.010, av0 + 0.004, ch, dehors, dedans, "Ceramique")
    cadre(pc, av1 - 0.008, av1, ch, dehors, ouverture, "Peinture")
    for sx in (-1, 1):
        for sh in (-1, 1):
            x, hh = sx * (dehors[0] - 0.006), ch + sh * (dehors[1] - 0.007)
            kit.cylindre(pc, P(av0 + 0.002, hh, x), P(av1 - 0.006, hh, x), 0.0024, 6, mat="Acier")
    a.objet(pc)
    pc = a.piece("Levre", "Lueur")
    cadre(pc, av1 - 0.0075, av1 + 0.0005, ch, plus(ouverture, 0.0003, 0.0003), plus(ouverture, -0.0015, -0.0015), "Lueur")
    a.objet(pc)
    return av1 + 0.0005


def dissipateur_cadre(a, av0, ch, dehors, dedans):
    """FX-3 : des ailettes rectangulaires autour d'un cœur allumé, au bout des quatre branches."""
    n, pas = 5, 0.0062
    av1 = av0 + 0.006 + n * pas
    pc = a.piece("Dissipateur", "Acier")
    cadre(pc, av0 - 0.010, av0 + 0.006, ch, dehors, dedans, "Peinture")   # le collier, qui pince les branches
    for i in range(n):
        c = av0 + 0.006 + i * pas
        cadre(pc, c, c + 0.0024, ch, plus(dehors, 0.008, 0.008), dedans, "Acier")
    cadre(pc, av1 - 0.002, av1 + 0.004, ch, dehors, dedans, "Peinture")   # le bout
    a.objet(pc)
    pc = a.piece("Coeur", "Lueur")
    coeur = plus(dedans, 0.0004, 0.0004)
    kit.loft(pc, [rectangle(av0 - 0.008, ch, coeur), rectangle(av1 + 0.002, ch, coeur)], "Lueur", bouts=False)
    a.objet(pc)
    return av1 + 0.004


def focaliseur_court_cadre(a, av0, ch, dehors, dedans):
    """FX-3, le focaliseur court : un pavillon rectangulaire qui pince les quatre branches et s'évase, son bord allumé."""
    longueur = 0.018
    avant = av0 + longueur
    pc = a.piece("Focaliseur", "Peinture")
    kit.loft(pc, [rectangle(av0 - 0.010, ch, dehors), rectangle(av0 + 0.008, ch, plus(dehors, 0.002, 0.002)),
                  rectangle(avant, ch, plus(dehors, 0.008, 0.010)), rectangle(avant, ch, plus(dehors, 0.004, 0.006)),
                  rectangle(av0 + 0.008, ch, plus(dedans, 0.004, 0.005)), rectangle(av0 - 0.010, ch, dedans)],
             "Peinture", cyclique=True)
    a.objet(pc)
    pc = a.piece("Levre", "Lueur")
    cadre(pc, avant - 0.0015, avant + 0.0005, ch, plus(dehors, 0.0083, 0.0103), plus(dehors, 0.0060, 0.0080), "Lueur")
    a.objet(pc)
    return avant + 0.0005


def rallonge_pistolet(a, av0, h, r, longueur):
    """Le canon long du PA-05 : un canon qui dépasse de la culasse, son filetage protégé par une bague."""
    pc = a.piece("Canon_Long", "Bouche")
    f = kit.cylindre(pc, P(av0 - 0.008, h), P(av0 + longueur - 0.006, h), r, 12, mat="Bouche")
    kit.chanfreiner(pc, 0.0006, f, angle_min=60)
    f = kit.cylindre(pc, P(av0 + longueur - 0.009, h), P(av0 + longueur, h), r + 0.0012, 12, mat="Acier")
    kit.chanfreiner(pc, 0.0006, f, angle_min=60)
    a.objet(pc)
    couronne(a, av0 + longueur + 0.0005, h, r + 0.0006)
    return av0 + longueur + 0.0005


def rallonge_fourreau(a, espace, av0, h, longueur):
    """Le canon long du RV-30 : une rallonge du fourreau vissée devant le sien (une gorge marque la jonction),
    sa bouche en laiton."""
    plein, avant = espace["FOURREAU"][0][1], espace["FOURREAU"][-1][1]
    pc = a.piece("Rallonge", "AcierBrut")
    f = kit.loft(pc, kit.sections_av([(av0 - 0.002, plein), (av0 + longueur - 0.010, plein), (av0 + longueur, avant)]),
                 "AcierBrut")
    kit.chanfreiner(pc, 0.0010, f)
    a.objet(pc)
    couronne(a, av0 + longueur + 0.0005, h, 0.0085, mat="Laiton", r_ame=0.0050)
    return av0 + longueur + 0.0005


def canon_court_fa30(a, h):
    """Le canon court du FA-30 : son compensateur ôté, le canon coupé court et couronné."""
    pc = a.piece("Canon_Court", "Bouche")
    kit.cylindre(pc, P(0.594, h), P(0.6315, h), 0.0090, 12, mat="Bouche")
    a.objet(pc)
    couronne(a, 0.634, h, 0.0098, r_ame=0.0055)
    return 0.634


# --- un accessoire ---------------------------------------------------------------------------------------

def construire_canon(code_arme, sorte, espace):
    reglage = ARMES[code_arme]
    at = espace["ATELIER"]

    def construire(a):
        bouche = bouche_de(at)
        b_av, h = av_de(bouche), bouche.z
        a.transformation = Matrix.Translation(-bouche)   # dessiné dans le repère de l'arme
        laser = reglage.get("laser", False)
        r = reglage["r"]
        debut = reglage.get("debut", b_av)
        section = section_bloc(espace, reglage["bloc"]) if "bloc" in reglage else None
        par_nom = {court(at, o): o for o in at.objets}
        caches = [par_nom[n] for n in reglage.get("masque", [])]   # les pièces de l'arme qu'il remplace
        deplaces = {}                                               # les points d'accroche qu'il déplace
        points = accroches(at)

        recul = reglage.get("recul", 0.002)
        if sorte == COMPENSATEUR and "cadre" in reglage:
            nouvelle = stabilisateur_cadre(a, debut, *reglage["cadre"])
        elif sorte == COMPENSATEUR:
            nouvelle = stabilisateur(a, debut, h, r, recul) if laser else compensateur(a, debut, h, r, section)
        elif sorte == FREIN and "cadre" in reglage:
            nouvelle = dissipateur_cadre(a, debut, *reglage["cadre"])
        elif sorte == FREIN:
            nouvelle = dissipateur(a, debut, h, r, recul) if laser else frein(a, debut, h, r, section)
        elif sorte == LONG and code_arme in LONGS:
            c, longueur = LONGS[code_arme]
            for ob in at.objets:
                if etendue_av(ob)[1] > c:
                    pc = acc.copier_piece(a, at, ob)
                    etirer(pc, c, longueur)
                    a.objet(pc)
                    caches.append(ob)
            nouvelle = b_av + longueur
            deplaces = {nom: av_de(p) + longueur for nom, p in points.items() if av_de(p) > c}
        elif sorte == LONG and code_arme == "PA-05":
            nouvelle = rallonge_pistolet(a, b_av, h, r, 0.032)
        elif sorte == LONG and code_arme == "RV-30":
            caches.append(par_nom["Bouche"])
            nouvelle = rallonge_fourreau(a, espace, b_av - 0.002, h, 0.045)
        elif sorte == COURT and code_arme in COURTS:
            c0, c1, laisses = COURTS[code_arme]
            for ob in at.objets:
                if etendue_av(ob)[1] <= c0:
                    continue
                caches.append(ob)
                if court(at, ob) in laisses:
                    continue
                pc = acc.copier_piece(a, at, ob)
                if raccourcir(pc, c0, c1):
                    a.objet(pc)
            nouvelle = b_av - (c1 - c0)
            for nom, p in points.items():
                if av_de(p) >= c1:
                    deplaces[nom] = av_de(p) - (c1 - c0)
                elif av_de(p) > c0:
                    deplaces[nom] = c0
        elif sorte == COURT and code_arme == "FA-30":
            caches += [par_nom["Canon"], par_nom["Compensateur"]]
            nouvelle = canon_court_fa30(a, h)
        elif sorte == COURT and "cadre" in reglage:
            nouvelle = focaliseur_court_cadre(a, debut, *reglage["cadre"])
        elif sorte == COURT and laser:
            nouvelle = focaliseur_court(a, debut, h, r)
        else:
            raise ValueError(f"{code_arme} : pas de canon {sorte}")

        # sa bouche, et les points d'accroche qu'il déplace
        a.vide(a.nom("Bouche"), a.racine, position=P(nouvelle, h))
        a.bouche = (nouvelle - b_av, 0.0)   # la suie, dans le repère de l'accessoire (l'export la lit)
        if deplaces:
            groupe = a.vide(a.nom("Accessoires"), a.racine, "CUBE", 0.004)
            for nom, av in deplaces.items():
                p = points[nom]
                a.vide(a.nom(nom), groupe, position=P(av, p.z, p.x))
        a.caches = caches
        a.deplaces = sorted(deplaces)
        a.allongement = nouvelle - b_av
        a.donnees["arme"] = code_arme
        a.donnees["variante"] = sorte
        a.donnees["masque"] = sorted({nom_unity(at, o) for o in caches})
    return construire


# --- lancement -----------------------------------------------------------------------------------------

def rendre_monte(a, script, dossier):
    """L'accessoire monté sur son arme, les pièces qu'il remplace cachées : le bout de l'arme de profil et de trois
    quarts avant."""
    at = acc.arme(script)["ATELIER"]
    scene = at.scene
    bouche = bouche_de(at)
    for o in a.caches:
        o.hide_render = True
    studio = kit.collection(f"Studio — canons sur {at.code}", scene.collection)
    try:
        e = acc.monter(a, scene, bouche)
        pistolet = av_de(bouche) < 0.3
        recul = 0.30 if pistolet else 0.42
        cible = bouche + Vector((0.0, -a.allongement / 2 + (0.03 if pistolet else 0.07), 0.0))
        for nom, dc in (("profil", Vector((recul, 0.0, 0.0))), ("avant", Vector((0.20, -0.28, 0.09)) * (recul / 0.42))):
            cam = kit.camera(f"Cam_{a.prefixe}_{nom}", studio, cible + dc, cible, 50)
            kit.rendre(scene, cam, os.path.join(dossier, f"{a.prefixe}_{nom}.png"), 1400, 1000)
        bpy.data.objects.remove(e, do_unlink=True)
    finally:
        for o in a.caches:
            o.hide_render = False


LETTRES = {COMPENSATEUR: ("CM", "ST"), FREIN: ("FB", "DS"), LONG: ("CL", "FL"), COURT: ("CC", "FC")}
TITRES = {COMPENSATEUR: ("Compensateur", "Stabilisateur"), FREIN: ("Frein de bouche", "Dissipateur"),
          LONG: ("Canon long", "Focaliseur long"), COURT: ("Canon court", "Focaliseur court")}


def principal():
    args = acc.arguments(globals().get("ARGS"))
    seuls = [c for c in args.seuls.split(",") if c]
    ateliers = []
    for code_arme, reglage in ARMES.items():
        laser = reglage.get("laser", False)
        for sorte in [COMPENSATEUR, FREIN] + ([LONG, COURT] if code_arme in AVEC_BAISSE else []):
            code = f"{LETTRES[sorte][laser]}-{code_arme.replace('-', '')}"
            if seuls and code not in seuls:
                continue
            espace = acc.arme(reglage["script"])
            titre = TITRES[sorte][laser]
            a = acc.construire_accessoire(code, f"{titre} {code_arme}", "Canon", f"{titre} — {code_arme}",
                                          construire_canon(code_arme, sorte, espace), args, args.usure or None)
            a.script_arme = reglage["script"]
            ateliers.append(a)
            print(f"    {code} : bouche {a.allongement:+.3f} m, masque {a.donnees['masque']}, accroches déplacées {a.deplaces}")
            if args.rendus and args.sur_arme:
                rendre_monte(a, reglage["script"], args.rendus)
    if args.sortie:
        acc.enregistrer(args.sortie)
    return ateliers


ATELIERS = principal()
