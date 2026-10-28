"""Le mobilier du stand de tir et de la Fosse (2026-10-28) : il remplace l'apparence d'un bloc du constructeur du stand
(ShootingRangeBuilder), dont le collider reste la vérité du jeu. Chaque modèle tient donc EXACTEMENT dans la boîte de son
bloc (rien ne dépasse : une balle passerait à travers), son dos (+Y) contre le mur.

- ET1 : l'établi (2 × 0,8 × 1,05 m), plateau d'acier, tiroirs, étau, une arme démontée sur un tapis, un rouleau de
  ruban (le rafistolage, bible § 5) ; le coin d'attente du stand et l'atelier de la Fosse.
- RA1 : l'étagère à matériel (3 × 0,6 × 2 m), montants perforés, croix de contreventement, quatre niveaux : caisses de
  munitions, bacs, câble, pièces de rechange du CT-01.

    blender -b --factory-startup --python-exit-code 1 -P mobilier.py -- --rendus <dossier> [--props ET1]
"""

import math
import os
import sys

from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import commun_decor as cd  # noqa: E402

kit = cd.kit
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
DECALQUE = 0.0004


def boite(pc, centre, demi, mat=None, chanfrein=0.0):
    f = kit.boite_axes(pc, Vector(centre), X, Y, Z, demi[0], demi[1], demi[2], mat)
    if chanfrein:
        kit.chanfreiner(pc, chanfrein, f)
    return f


def de(pc, x0, x1, y0, y1, z0, z1, mat=None, chanfrein=0.0):
    """Une boîte donnée par ses bornes."""
    return boite(pc, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), ((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2), mat, chanfrein)


def pochoir(at, pc, contenu, hauteur, centre, axe_x, axe_y, mat="MarquageBlanc"):
    return kit.texte(pc, contenu, cd.police(cd.POLICE_POCHOIR), hauteur, Vector(centre), axe_x, axe_y,
                     mat=mat, decalage=DECALQUE, scene=at.scene)


def etabli(scene, code, titre):
    """L'établi : 2 m de long (X), 0,8 m de profondeur (Y, le dos en +Y), son plateau à 0,95 m ; ce qui est posé dessus
    ne dépasse pas 1,05 m (la boîte du bloc)."""
    at = cd.AtelierDecor(scene, code, titre, cd.materiaux())
    L, P = 1.0, 0.4   # demi-longueur, demi-profondeur

    bati = at.piece("Bati", "Peinture")
    for sx in (-1, 1):
        for sy in (-1, 1):
            de(bati, sx * (L - 0.07) - 0.025, sx * (L - 0.07) + 0.025, sy * (P - 0.06) - 0.025, sy * (P - 0.06) + 0.025,
               0.0, 0.89, chanfrein=0.004)
    for sy in (-1, 1):
        de(bati, -L + 0.05, L - 0.05, sy * (P - 0.06) - 0.02, sy * (P - 0.06) + 0.02, 0.82, 0.89, chanfrein=0.004)
        de(bati, -L + 0.05, L - 0.05, sy * (P - 0.06) - 0.015, sy * (P - 0.06) + 0.015, 0.12, 0.16, chanfrein=0.003)
    for sx in (-1, 1):
        de(bati, sx * (L - 0.07) - 0.02, sx * (L - 0.07) + 0.02, -P + 0.06, P - 0.06, 0.82, 0.89, chanfrein=0.004)
    # la tablette du bas et le bloc des tiroirs, à droite
    de(bati, -L + 0.1, L - 0.1, -P + 0.08, P - 0.08, 0.16, 0.18)
    de(bati, 0.34, L - 0.1, -P + 0.05, P - 0.08, 0.18, 0.82, chanfrein=0.005)
    at.objet(bati)

    plateau = at.piece("Plateau", "Acier")
    de(plateau, -L, L, -P, P, 0.89, 0.95, chanfrein=0.006)
    # les façades des tiroirs et leurs poignées
    for i, (z0, z1) in enumerate(((0.62, 0.8), (0.42, 0.6), (0.2, 0.4))):
        de(plateau, 0.37, L - 0.13, -P + 0.04, -P + 0.05, z0, z1, "Peinture", chanfrein=0.003)
        de(plateau, 0.55, 0.75, -P + 0.025, -P + 0.04, (z0 + z1) / 2 - 0.012, (z0 + z1) / 2 + 0.012, "Acier", chanfrein=0.002)
    at.objet(plateau)

    # le tapis de caoutchouc et l'arme démontée dessus (une carcasse, un canon, un chargeur) ; un rouleau de ruban
    dessus = at.piece("Dessus", "Caoutchouc")
    de(dessus, 0.15, 0.92, -0.32, 0.26, 0.95, 0.957)
    de(dessus, 0.22, 0.6, -0.08, 0.06, 0.957, 1.0, "Peinture", chanfrein=0.004)
    kit.cylindre(dessus, Vector((0.6, -0.01, 0.975)), Vector((0.9, -0.01, 0.975)), 0.011, n=8, mat="Acier")
    de(dessus, 0.3, 0.42, -0.24, -0.17, 0.957, 1.03, "Noir", chanfrein=0.003)
    kit.cylindre(dessus, Vector((-0.3, 0.26, 0.95)), Vector((-0.3, 0.26, 0.995)), 0.055, n=12, mat="Noir")
    kit.cylindre(dessus, Vector((-0.3, 0.26, 0.951)), Vector((-0.3, 0.26, 0.996)), 0.03, n=10, mat="PeintureClaire")
    at.objet(dessus)

    # l'étau, au coin gauche de devant
    etau = at.piece("Etau", "Peinture")
    de(etau, -0.86, -0.6, -0.38, -0.18, 0.95, 0.99, chanfrein=0.004)
    de(etau, -0.84, -0.62, -0.37, -0.32, 0.99, 1.05, chanfrein=0.004)
    de(etau, -0.84, -0.62, -0.26, -0.21, 0.99, 1.05, chanfrein=0.004)
    kit.cylindre(etau, Vector((-0.73, -0.4, 1.01)), Vector((-0.73, -0.2, 1.01)), 0.008, n=6, mat="Acier")
    kit.cylindre(etau, Vector((-0.82, -0.395, 1.01)), Vector((-0.64, -0.395, 1.01)), 0.005, n=6, mat="Acier")
    at.objet(etau)

    # la caisse à outils sur la tablette, et la plaque de l'établi
    caisse = at.piece("CaisseOutils", "Peinture")
    de(caisse, -0.72, -0.22, -0.2, 0.12, 0.18, 0.38, chanfrein=0.006)
    de(caisse, -0.6, -0.34, -0.03, -0.01, 0.38, 0.41, "Acier", chanfrein=0.003)
    pochoir(at, caisse, "ATELIER 02", 0.03, (-0.47, -0.2 - DECALQUE, 0.3), X, Z)
    at.objet(caisse)
    return at


def etagere(scene, code, titre):
    """L'étagère à matériel : 3 m de long (X), 0,6 m de profondeur (Y, le dos en +Y), 2 m de haut ; ce qu'elle porte ne
    dépasse pas de ses montants."""
    at = cd.AtelierDecor(scene, code, titre, cd.materiaux())
    L, P, H = 1.5, 0.3, 2.0
    niveaux = (0.1, 0.62, 1.14, 1.66)

    montants = at.piece("Montants", "Peinture")
    for x in (-L + 0.025, 0.0, L - 0.025):
        for sy in (-1, 1):
            de(montants, x - 0.025, x + 0.025, sy * (P - 0.025) - 0.025, sy * (P - 0.025) + 0.025, 0.0, H, chanfrein=0.003)
            # les perforations du montant de devant : des fentes peintes en noir
            if sy < 0:
                for z in [0.2 + 0.08 * i for i in range(22)]:
                    kit.quad(montants, [Vector((x - 0.006, -P - DECALQUE, z)), Vector((x + 0.006, -P - DECALQUE, z)),
                                        Vector((x + 0.006, -P - DECALQUE, z + 0.03)), Vector((x - 0.006, -P - DECALQUE, z + 0.03))],
                             "Noir", normale=-Y)
    for z in niveaux:
        de(montants, -L + 0.05, L - 0.05, -P + 0.005, P - 0.005, z, z + 0.03, chanfrein=0.003)
        de(montants, -L + 0.05, L - 0.05, -P, -P + 0.02, z - 0.02, z + 0.05, "Acier", chanfrein=0.002)
    at.objet(montants)

    # la croix de contreventement, au dos, dans chaque travée
    croix = at.piece("Croix", "Acier")
    for x0, x1 in ((-L + 0.05, -0.025), (0.025, L - 0.05)):
        for a, b in (((x0, 0.15), (x1, 1.9)), ((x0, 1.9), (x1, 0.15))):
            p0, p1 = Vector((a[0], P - 0.012, a[1])), Vector((b[0], P - 0.012, b[1]))
            d = (p1 - p0).normalized()
            n = Y
            kit.boite_axes(croix, (p0 + p1) / 2, d, n, d.cross(n), (p1 - p0).length / 2, 0.004, 0.02, "Acier")
    at.objet(croix)

    charge = at.piece("Charge", "Peinture")
    # en bas : deux caisses de munitions et une bobine de câble
    for x0 in (-1.35, -0.85):
        de(charge, x0, x0 + 0.42, -0.22, 0.18, 0.13, 0.43, "Peinture", chanfrein=0.008)
        de(charge, x0 + 0.06, x0 + 0.36, -0.225, -0.22, 0.25, 0.31, "PeintureClaire")
    kit.cylindre(charge, Vector((0.55, -0.18, 0.37)), Vector((0.55, 0.2, 0.37)), 0.22, n=14, mat="Caoutchouc")
    kit.cylindre(charge, Vector((0.55, -0.185, 0.37)), Vector((0.55, 0.205, 0.37)), 0.07, n=10, mat="Acier")
    # au premier niveau : des bacs noirs, étiquetés
    bacs = at.piece("Bacs", "Noir")
    for i, x0 in enumerate((-1.4, -0.95, -0.5, 0.1, 0.55, 1.0)):
        de(bacs, x0, x0 + 0.4, -0.27, 0.25, 0.65, 0.87, chanfrein=0.006)
        de(bacs, x0 + 0.08, x0 + 0.32, -0.272, -0.27, 0.77, 0.83, "PeintureClaire")
    pochoir(at, bacs, "PIÈCES CT-01", 0.035, (-0.75, -0.2725 - DECALQUE, 0.73), X, Z, "MarquageBlanc")
    at.objet(bacs)
    # au deuxième niveau : des pièces de rechange (avant-bras, plaques, un casque de CT-01 sans visière)
    for x in (-1.2, -0.8):
        kit.cylindre(charge, Vector((x, -0.15, 1.24)), Vector((x, 0.15, 1.24)), 0.07, n=8, mat="Acier")
    de(charge, -0.4, 0.2, -0.2, 0.2, 1.17, 1.21, "Peinture", chanfrein=0.004)
    de(charge, -0.35, 0.15, -0.18, 0.18, 1.21, 1.25, "Peinture", chanfrein=0.004)
    de(charge, 0.5, 0.86, -0.2, 0.16, 1.17, 1.45, "Peinture", chanfrein=0.02)
    de(charge, 0.54, 0.82, -0.205, -0.2, 1.27, 1.36, "Noir")
    # en haut : des cartons repeints, une caisse plate
    for x0, h in ((-1.35, 0.26), (-0.9, 0.2), (0.2, 0.28)):
        de(charge, x0, x0 + 0.42, -0.25, 0.22, 1.69, 1.69 + h, "PeintureClaire", chanfrein=0.006)
    de(charge, 0.75, 1.4, -0.24, 0.24, 1.69, 1.83, "Peinture", chanfrein=0.006)
    at.objet(charge)
    return at


PROPS = [
    ("ET1", lambda s: etabli(s, "ET1", "Établi")),
    ("RA1", lambda s: etagere(s, "RA1", "Étagère à matériel")),
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
            bas, haut = enveloppe(at)
            print("ENVELOPPE", code, [round(v, 4) for v in bas], [round(v, 4) for v in haut])
            taille = 3.0 if code == "RA1" else 2.0
            cd.rendre_vues(at, a.rendus, (0, 0, 0.6 if code == "ET1" else 1.0), taille, vues=("trois_quarts", "face", "profil"))
            print("RENDU", code, sum(kit.triangles(o) for o in at.objets if o.type == "MESH"), "triangles")
