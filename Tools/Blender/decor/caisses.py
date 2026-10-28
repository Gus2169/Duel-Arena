"""Les caisses de la salle de test (2026-10-27) : elles remplacent l'apparence des caisses du blocking, dont le
collider reste la vérité du jeu. Chaque modèle tient donc EXACTEMENT dans la boîte de son bloc : rien ne dépasse
(une balle passerait à travers ce qui dépasse), et les creux restent de quelques centimètres.

- CA1 : le caisson-conteneur d'un mètre, un conteneur en réduction : cadre anthracite, pièces de coin d'acier,
  tôle ondulée blanc cassé, deux portes et leurs barres de fermeture.
- CA2 : le caisson renforcé d'un mètre, panneaux anthracite dans un cadre d'acier, une croix d'acier sur chaque
  flanc, des anneaux de levage : les pièces de rechange du CT-01.
- CO1 : le conteneur de matériel d'essai de deux mètres (les deux du poste de départ A), tôle ondulée blanc cassé,
  portes et barres de fermeture.

Les caisses d'un mètre de la première version (une ceinture à mi-hauteur, des poignées encastrées, un couvercle
débordant) se lisaient comme des tiroirs (l'utilisateur, le 2026-10-27) : plus rien d'horizontal ne coupe une
caisse en deux, ce sont les montants, les ondulations et les croix qui la dessinent.

    blender -b --factory-startup --python-exit-code 1 -P caisses.py -- --rendus <dossier>
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import commun_decor as cd  # noqa: E402

kit = cd.kit
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
MIRE = ["MarquageNoir", "MarquageBlanc", "MarquageNoir", "MarquageBlanc"]   # la mire de crash-test, en quartiers
DECALQUE = 0.0004


def boite(pc, centre, demi, mat=None, chanfrein=0.0):
    f = kit.boite_axes(pc, Vector(centre), X, Y, Z, demi[0], demi[1], demi[2], mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def barre(pc, centre, axe, normale, demi_longueur, demi_largeur, demi_epaisseur, mat=None, chanfrein=0.0):
    """Un fer plat : sa longueur le long de `axe`, son épaisseur selon `normale`."""
    axe, normale = axe.normalized(), normale.normalized()
    f = kit.boite_axes(pc, Vector(centre), axe, normale, axe.cross(normale), demi_longueur, demi_epaisseur,
                       demi_largeur, mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def ovale(pc, centre, u, v, demi_u, demi_v, mat, normale, n=6):
    """Un trou oblong peint (la lumière d'une pièce de coin) : un ovale plat, décalqué sur la face."""
    c, l, r = Vector(centre), demi_u - demi_v, demi_v
    pts = [c + u * (l + r * math.cos(a)) + v * (r * math.sin(a))
           for a in (-math.pi / 2 + math.pi * i / n for i in range(n + 1))]
    pts += [c + u * (-l + r * math.cos(a)) + v * (r * math.sin(a))
            for a in (math.pi / 2 + math.pi * i / n for i in range(n + 1))]
    return kit.quad(pc, pts, mat, normale=normale)


def pochoir(at, pc, contenu, hauteur, centre, axe_x, axe_y, mat="MarquageBlanc", police=None):
    """Un texte peint au pochoir sur une face (décalque à 0,4 mm)."""
    return kit.texte(pc, contenu, police or cd.police(cd.POLICE_POCHOIR), hauteur, Vector(centre), axe_x, axe_y,
                     mat=mat, decalage=DECALQUE, scene=at.scene)


def ondulations(pc, origine, u, normale, longueur, z0, z1, n, creux, mat):
    """Une paroi de tôle ondulée verticale : n trapèzes sur `longueur`, crêtes dans le plan de `origine`, creux
    vers l'intérieur ; une demi-crête à chaque bout (symétrique)."""
    pas = longueur / n
    profil = [(-longueur / 2, 0.0)]
    for i in range(n):
        a = -longueur / 2 + i * pas
        profil += [(a + pas * 0.1, 0.0), (a + pas * 0.3, -creux), (a + pas * 0.7, -creux), (a + pas * 0.9, 0.0)]
    profil += [(longueur / 2, 0.0), (longueur / 2, -creux - 0.008), (-longueur / 2, -creux - 0.008)]
    o = Vector(origine)
    sections = [[o + u * a + normale * b + Z * z for a, b in profil] for z in (z0, z1)]
    return kit.loft(pc, sections, mat)


def caisson_conteneur(scene, code, titre):
    """Le caisson-conteneur d'un mètre : un conteneur en réduction. Huit pièces de coin d'acier (seules à toucher
    les faces de la boîte), un cadre anthracite rentré d'un centimètre, trois parois de tôle ondulée, deux portes
    et quatre barres de fermeture sur -Y, des pochoirs et des plaques."""
    at = cd.AtelierDecor(scene, code, titre, cd.materiaux())
    E = 0.5           # la demi-boîte du bloc
    c, cz = 0.05, 0.045   # demi-taille des pièces de coin
    m = 0.04          # demi-section des montants et des longerons
    p = E - 0.01 - m  # leur axe : 1 cm derrière les pièces de coin

    coins = at.piece("Coins", "Acier")
    trous = at.piece("Lumieres", "Noir")
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (cz, 1 - cz):
                boite(coins, (sx * (E - c), sy * (E - c), z), (c, c, cz), chanfrein=0.006)
                ovale(trous, (sx * (E + DECALQUE), sy * (E - c), z), Y, Z, 0.024, 0.012, "Noir", X * sx)
                ovale(trous, (sx * (E - c), sy * (E + DECALQUE), z), X, Z, 0.024, 0.012, "Noir", Y * sy)
                if z > 0.5:
                    ovale(trous, (sx * (E - c), sy * (E - c), 1 + DECALQUE), X, Y, 0.024, 0.012, "Noir", Z)
    at.objet(coins)

    cadre = at.piece("Cadre", "Peinture")
    for sx in (-1, 1):
        for sy in (-1, 1):
            boite(cadre, (sx * p, sy * p, 0.5), (m, m, 0.5 - 2 * cz), chanfrein=0.005)
    for z in (cz, 1 - cz):
        for s in (-1, 1):
            boite(cadre, (0, s * p, z), (E - 2 * c, m, m), chanfrein=0.005)
            boite(cadre, (s * p, 0, z), (m, E - 2 * c, m), chanfrein=0.005)
    at.objet(cadre)

    # Les fourches, dans les longerons du bas des flancs ±X.
    for sx in (-1, 1):
        for y in (-0.2, 0.2):
            boite(trous, (sx * (p + m - 0.004), y, cz), (0.0045, 0.06, 0.024), "Noir")
    at.objet(trous)

    # Les parois : la tôle ondulée entre les montants (sept ondes, crêtes à 2 cm derrière les montants), et le
    # toit, une tôle à peine nervurée rentrée d'un centimètre sous les longerons.
    tole = at.piece("Tole", "PeintureClaire")
    crete, longueur, z0, z1 = p + m - 0.01, 2 * (p - m) + 0.02, cz + m - 0.01, 1 - cz - m + 0.01
    ondulations(tole, (0, crete, 0), -X, Y, longueur, z0, z1, 7, 0.02, "PeintureClaire")
    ondulations(tole, (crete, 0, 0), Y, X, longueur, z0, z1, 7, 0.02, "PeintureClaire")
    ondulations(tole, (-crete, 0, 0), -Y, -X, longueur, z0, z1, 7, 0.02, "PeintureClaire")
    boite(tole, (0, 0, 1 - cz - 0.005), (p - m + 0.01, p - m + 0.01, 0.035), "PeintureClaire")
    for x in (-0.27, -0.09, 0.09, 0.27):
        boite(tole, (x, 0, 1 - cz + 0.034), (0.035, p - m, 0.004), "PeintureClaire", chanfrein=0.002)
    at.objet(tole)

    # Les portes sur -Y : deux battants plats, rentrés de 2,4 cm, quatre barres de fermeture verticales et leurs
    # guides, les gâches en haut et en bas, les leviers, les charnières sur les montants.
    yp = -(p + m - 0.014)           # la face des battants
    portes = at.piece("Portes", "PeintureClaire")
    for sx in (-1, 1):
        boite(portes, (sx * (p - m) / 2, yp + 0.012, 0.5), ((p - m) / 2 - 0.003, 0.012, 0.5 - cz - m + 0.005),
              "PeintureClaire", chanfrein=0.005)
    at.objet(portes)
    ferrures = at.piece("Ferrures", "Acier")
    yb = yp - 0.013                  # l'axe des barres
    for x in (-0.305, -0.105, 0.105, 0.305):
        kit.cylindre(ferrures, Vector((x, yb, 0.1)), Vector((x, yb, 0.9)), 0.008, 8, mat="Acier")
        for z in (0.22, 0.5, 0.78):
            boite(ferrures, (x, yb + 0.006, z), (0.016, 0.007, 0.012), "Acier", chanfrein=0.002)
        for z in (0.1, 0.9):
            boite(ferrures, (x, yb, z), (0.022, 0.009, 0.014), "Acier", chanfrein=0.003)
        sens = 1 if x < 0 else -1      # les leviers rabattus vers le milieu des battants
        boite(ferrures, (x + sens * 0.034, yb - 0.004, 0.42), (0.034, 0.005, 0.009), "Noir", chanfrein=0.002)
    for sx in (-1, 1):
        for z in (0.2, 0.5, 0.8):
            boite(ferrures, (sx * (p - m - 0.004), yp - 0.006, z), (0.012, 0.008, 0.026), "Acier", chanfrein=0.003)
    at.objet(ferrures)

    deco = at.piece("Pochoirs", "MarquageNoir")
    yd = yp - DECALQUE
    # (entre les barres : 18 cm de large)
    pochoir(at, deco, "B-01", 0.05, (0.205, yp, 0.80), X, Z, "MarquageNoir")
    kit.disque(deco, Vector((0.205, yd, 0.30)), -Y, 0.045, n=16, mats_quartiers=MIRE)
    pochoir(at, deco, "BRUT  600 KG", 0.019, (-0.205, yp, 0.83), X, Z, "MarquageNoir")
    pochoir(at, deco, "TARE  140 KG", 0.019, (-0.205, yp, 0.80), X, Z, "MarquageNoir")
    at.objet(deco)

    # Les plaques des flancs et du dos : une tôle noire vissée sur les crêtes, peinte en blanc.
    plaques = at.piece("Plaques", "Noir")
    textes = at.piece("Plaques_Textes", "MarquageBlanc")
    for n, u in ((X, Y), (-X, -Y), (Y, -X)):
        barre(plaques, n * (crete + 0.003) + Z * 0.64, u, n, 0.12, 0.07, 0.003, "Noir", chanfrein=0.0015)
        face = n * (crete + 0.006)
        pochoir(at, textes, "B-01", 0.046, face + Z * 0.66, u, Z, "MarquageBlanc")
        pochoir(at, textes, "CAISSON 07", 0.02, face + Z * 0.605, u, Z, "MarquageBlanc")
    at.objet(plaques)
    at.objet(textes)
    return at


def caisson_renforce(scene, code, titre):
    """Le caisson renforcé d'un mètre : un cadre de tubes d'acier aux arêtes de la boîte, des panneaux anthracite
    rentrés de 3 cm, une croix d'acier et son gousset (la mire de crash-test) sur chaque flanc, quatre anneaux de
    levage sur le dessus, les pochoirs des pièces de rechange du CT-01."""
    at = cd.AtelierDecor(scene, code, titre, cd.materiaux())
    E, e = 0.5, 0.035   # la demi-boîte du bloc, la demi-section des tubes

    cadre = at.piece("Cadre", "Acier")
    for sx in (-1, 1):
        for sy in (-1, 1):
            boite(cadre, (sx * (E - e), sy * (E - e), 0.5), (e, e, 0.5), chanfrein=0.008)
    for z in (e, 1 - e):
        for s in (-1, 1):
            boite(cadre, (0, s * (E - e), z), (E - 2 * e, e, e), chanfrein=0.008)
            boite(cadre, (s * (E - e), 0, z), (e, E - 2 * e, e), chanfrein=0.008)
    at.objet(cadre)

    panneaux = at.piece("Panneaux", "Peinture")
    boite(panneaux, (0, 0, 0.5), (E - 0.03, E - 0.03, 0.47), chanfrein=0.01)
    at.objet(panneaux)

    # Les croix : deux fers plats posés sur le panneau, d'un coin intérieur du cadre à l'autre (leurs bouts
    # rentrent dans les tubes), un gousset vissé sur leur croisement (il cache aussi leur recouvrement).
    croix = at.piece("Croix", "Acier")
    deco = at.piece("Pochoirs", "MarquageBlanc")
    plan = E - 0.03                                    # la face des panneaux
    flancs = ((-Y, X), (Y, -X), (X, Y), (-X, -Y))      # (normale, l'axe horizontal tel qu'on le lit de face)
    for n, u in flancs:
        milieu = n * (plan + 0.006) + Z * 0.5
        for d in (u + Z, u - Z):
            barre(croix, milieu, d, n, 0.6, 0.025, 0.006, "Acier", chanfrein=0.003)
        barre(croix, n * (plan + 0.0155) + Z * 0.5, u, n, 0.065, 0.065, 0.0035, "Acier", chanfrein=0.002)
        gousset = n * (plan + 0.019)
        for du in (-0.045, 0.045):
            for dz in (-0.045, 0.045):
                kit.vis_six_pans(croix, gousset + u * du + Z * (0.5 + dz), n, r=0.008, h=0.005, mat="Acier")
        kit.disque(deco, gousset + n * DECALQUE + Z * 0.5, n, 0.038, n=16, mats_quartiers=MIRE)
        face = n * plan
        if abs(n.y) > 0.5:
            pochoir(at, deco, "CT-01", 0.065, face + Z * 0.80, u, Z)
            pochoir(at, deco, "LOT B-01  07", 0.032, face + Z * 0.21, u, Z)
        else:
            pochoir(at, deco, "PIÈCES DE RECHANGE", 0.028, face + Z * 0.81, u, Z)
            pochoir(at, deco, "CHARGE MAX.  250 KG", 0.026, face + Z * 0.20, u, Z)
    at.objet(croix)

    # Le dessus : la désignation, quatre anneaux de levage dans les coins (rien ne dépasse le cadre).
    pochoir(at, deco, "CT-01", 0.11, (0, 0, 0.97), X, Y)
    at.objet(deco)
    anneaux = at.piece("Anneaux", "Acier")
    trous = at.piece("Anneaux_Trous", "Noir")
    for sx in (-1, 1):
        for sy in (-1, 1):
            d = Vector((sx, sy, 0)).normalized()
            n = Vector((-sy, sx, 0)).normalized()
            centre = Vector((sx * 0.36, sy * 0.36, 0.985))
            barre(anneaux, centre, d, n, 0.032, 0.015, 0.006, "Acier", chanfrein=0.002)
            for s in (-1, 1):
                kit.disque(trous, centre + n * (s * (0.006 + DECALQUE)) + Z * 0.002, n * s, 0.007, n=8, mat="Noir")
    at.objet(anneaux)
    at.objet(trous)
    return at


def conteneur(scene, code, titre):
    """Le conteneur de deux mètres : un cadre d'acier, des parois de tôle ondulée rentrées, deux portes et leurs
    barres de fermeture sur la face -Y, des pochoirs."""
    at = cd.AtelierDecor(scene, code, titre, cd.materiaux())
    cadre = at.piece("Cadre", "Peinture")
    m = 0.06   # demi-section des montants
    for sx in (-1, 1):
        for sy in (-1, 1):
            boite(cadre, (sx * (1 - m), sy * (1 - m), 1.0), (m, m, 1.0), chanfrein=0.008)
    for sz, h in ((0.06, 0.06), (1.94, 0.06)):
        for sy in (-1, 1):
            boite(cadre, (0, sy * (1 - m), sz), (1 - 2 * m, m, h), chanfrein=0.008)
        for sx in (-1, 1):
            boite(cadre, (sx * (1 - m), 0, sz), (m, 1 - 2 * m, h), chanfrein=0.008)
    at.objet(cadre)

    # Les parois : une tôle ondulée (trapèzes de 20 cm, 3,5 cm de profondeur), entre les montants.
    tole = at.piece("Tole", "PeintureClaire")

    def paroi(origine, u, normale, longueur, z0, z1, pas=0.2, creux=0.035):
        n = int(round(longueur / pas))
        pts = []
        for i in range(n):
            a = -longueur / 2 + i * pas
            pts += [(a, 0.0), (a + pas * 0.2, -creux), (a + pas * 0.6, -creux), (a + pas * 0.8, 0.0)]
        pts.append((longueur / 2, 0.0))
        dos = [(longueur / 2, -creux - 0.01), (-longueur / 2, -creux - 0.01)]
        contour = pts + dos
        sections = []
        for z in (z0, z1):
            sections.append([Vector(origine) + u * a + normale * b + Z * z for a, b in contour])
        return kit.loft(tole, sections, "PeintureClaire")

    l = 2 - 4 * m
    paroi((0, 1 - 0.005, 0), -X, Y, l, 0.12, 1.88)          # le dos
    paroi((1 - 0.005, 0, 0), Y, X, l, 0.12, 1.88)           # les flancs
    paroi((-1 + 0.005, 0, 0), -Y, -X, l, 0.12, 1.88)
    boite(tole, (0, 0, 1.94), (1 - 2 * m, 1 - 2 * m, 0.04), "PeintureClaire")   # le toit
    at.objet(tole)

    # Les portes, sur -Y : deux battants, quatre barres de fermeture, leurs poignées.
    portes = at.piece("Portes", "PeintureClaire")
    for sx in (-1, 1):
        boite(portes, (sx * 0.44, -0.96, 1.0), (0.43, 0.012, 0.86), "PeintureClaire", chanfrein=0.006)
    at.objet(portes)
    barres = at.piece("Barres", "Acier")
    for x in (-0.66, -0.22, 0.22, 0.66):
        kit.cylindre(barres, Vector((x, -0.982, 0.16)), Vector((x, -0.982, 1.84)), 0.014, 8, mat="Acier")
        boite(barres, (x + 0.05, -0.993, 1.0), (0.06, 0.006, 0.018), "Noir", chanfrein=0.002)
        for z in (0.2, 1.8):
            boite(barres, (x, -0.992, z), (0.03, 0.008, 0.025), "Acier", chanfrein=0.003)
    at.objet(barres)

    deco = at.piece("Pochoirs", "MarquageNoir")
    pochoir(at, deco, "B-01", 0.2, (0.44, -0.9725, 1.40), X, Z, "MarquageNoir")
    pochoir(at, deco, "MATÉRIEL D'ESSAI", 0.05, (0.44, -0.9725, 1.20), X, Z, "MarquageNoir")
    pochoir(at, deco, "NE PAS OUVRIR", 0.045, (-0.44, -0.9725, 1.40), X, Z, "MarquageNoir")
    pochoir(at, deco, "EN COURS DE TIR", 0.045, (-0.44, -0.9725, 1.32), X, Z, "MarquageNoir")
    kit.disque(deco, Vector((-0.44, -0.9725, 0.75)), -Y, 0.11, n=20, mats_quartiers=MIRE)
    at.objet(deco)
    return at


PROPS = [
    ("CA1", lambda s: caisson_conteneur(s, "CA1", "Caisson-conteneur")),
    ("CA2", lambda s: caisson_renforce(s, "CA2", "Caisson renforcé")),
    ("CO1", lambda s: conteneur(s, "CO1", "Conteneur de matériel d'essai")),
]


def construire(code):
    for c, f in PROPS:
        if c == code:
            return f(cd.nouvelle_scene(f"Decor_{c}"))
    raise KeyError(code)


def enveloppe(at):
    """La boîte de tous les sommets du prop (dans le repère de sa racine) : elle doit tenir dans celle du bloc."""
    pts = [o.matrix_world @ v.co for o in at.objets if o.type == "MESH" for v in o.data.vertices]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


if __name__ == "__main__":
    a = cd.arguments()
    if a.rendus:
        for code, _ in PROPS:
            if a.props and code not in a.props.split(","):
                continue
            at = construire(code)
            taille = 2.0 if code.startswith("CO") else 1.0
            bas, haut = enveloppe(at)
            print("ENVELOPPE", code, [round(v, 4) for v in bas], [round(v, 4) for v in haut])
            cd.rendre_vues(at, a.rendus, (0, 0, taille / 2), taille, vues=("trois_quarts", "face", "dessus", "profil"))
            print("RENDU", code, sum(kit.triangles(o) for o in at.objets if o.type == "MESH"), "triangles")
