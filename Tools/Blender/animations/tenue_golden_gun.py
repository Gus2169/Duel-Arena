"""La tenue de la main droite des robots sur le Big D (2026-11-03, l'utilisateur : « il y a des doigts qui passent à
travers la crosse »).

La prise des pistolets du clip de Mixamo (« Pistol Aim Idle ») referme la main sur deux centimètres, et les robots du
jeu ont des doigts épais (le CT-01 et l'OS-02, des blocs) : leurs bouts entraient de 14 à 20 mm dans la crosse du Big D,
et de 14 à 15 mm dans celle des autres armes de poing. Le Big D s'avance donc de 3 cm dans la main (golden_gun :
AVANCE_DANS_LA_MAIN), sa crosse s'amincit (DEMI_CROSSE), et ce script calcule les rotations des quinze phalanges de la
main droite qui l'enveloppent : le majeur, l'annulaire et l'auriculaire passent devant la bande avant et se couchent
sur le flanc gauche de la crosse, l'index sur la détente, le pouce le long du flanc gauche, son bout vers le levier. Le
jeu les repose lui-même, l'arme en main (BreakActionGesture.rightHold, BreakActionView).

Les données : la main droite des robots dans la prise du clip, relevée dans Unity (Duel Arena > Animations > Relever
les mains des robots, mains_robots.json) : ses os dans le repère de l'arme, les axes des muscles de chaque phalange,
et les pièces de chaque châssis dans le repère de leur os. L'arme : golden_gun.py, voxelisée (--res, en mm).

Chaque doigt se plie autour des axes de ses muscles (l'écart et le pli à sa base, le pli des deux phalanges
suivantes), en degrés depuis la pose du clip, positif quand il s'ouvre : une recherche sur une grille garde la pose
qui n'enfonce rien chez aucun châssis (à --jeu près), touche l'arme où il faut (chez le CT-01, le plus épais) et
s'écarte le moins du clip. Écrit tenue_golden_gun.json (que lit l'export du rechargement, rechargement_golden_gun.py,
pour la fiche du geste), et des vues de contrôle avec --rendus.

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/animations/tenue_golden_gun.py -- [--res 0.5]
        [--rendus DOSSIER]
"""

import argparse
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

ICI = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.dirname(ICI)
MAINS = os.path.join(ICI, "mains_robots.json")
SORTIE = os.path.join(ICI, "tenue_golden_gun.json")
CHAINES = {"Thumb": ["RightHandThumb1", "RightHandThumb2", "RightHandThumb3"],
           "Index": ["RightHandIndex1", "RightHandIndex2", "RightHandIndex3"],
           "Middle": ["RightHandMiddle1", "RightHandMiddle2", "RightHandMiddle3"],
           "Ring": ["RightHandRing1", "RightHandRing2", "RightHandRing3"],
           "Pinky": ["RightHandPinky1", "RightHandPinky2", "RightHandPinky3"]}
ORDRE = ["Thumb", "Index", "Middle", "Ring", "Pinky"]   # BreakActionGesture.HoldOrder
REFERENCE = "CT01"   # le châssis aux doigts les plus épais : c'est lui qui touche l'arme

p = argparse.ArgumentParser()
p.add_argument("--res", type=float, default=0.5, help="la taille d'un voxel de l'arme (mm)")
p.add_argument("--jeu", type=float, default=0.6, help="ce qu'un doigt peut enfoncer (mm)")
p.add_argument("--rendus", default="")
a = p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

# --- l'arme, voxelisée : (av, h, x vers la gauche) en mm, dans le repère de l'arme ------------------------------
golden = os.path.join(BLENDER, "golden_gun.py")
sys.path.insert(0, BLENDER)
G = {"__name__": "golden_gun", "__file__": golden, "ARGS": ["--teinte", "or"]}
exec(compile(open(golden, encoding="utf-8").read(), golden, "exec"), G)
atelier = G["ATELIER"]
vue = atelier.scene.view_layers[0]

R = a.res
AV0, AV1, H0, H1, X0, X1 = -90.0, 200.0, -80.0, 130.0, -50.0, 50.0
NA, NH, NX = int((AV1 - AV0) / R) + 1, int((H1 - H0) / R) + 1, int((X1 - X0) / R) + 1
MOBILES = ("Levier_Corps", "Chien_Corps")   # ce que le pouce fait bouger : le levier qu'il abaisse, le chien qu'il arme
pieces = []


def nom_de(ob):
    return ob.name.split("_", 1)[1]


VOXELS = [ob for ob in atelier.objets if ob.type == "MESH" and ob.visible_get()
          and nom_de(ob) not in ("Gravure", "Marquages", "Douille", "Balle")]


def voxeliser(objets, plein, etiquette):
    """Les pièces fermées de `objets`, à leur place du moment, dans plein et etiquette."""
    vue.update()
    dg = vue.depsgraph
    xs = X0 + np.arange(NX) * R
    for ob in objets:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        vv = [Vector((c.x * 1000, -c.y * 1000, c.z * 1000)) for c in (ob.matrix_world @ x.co for x in me.vertices)]
        ff = [tuple(pl.vertices) for pl in me.polygons]
        ev.to_mesh_clear()
        if not ff:
            continue
        if nom_de(ob) not in pieces:
            pieces.append(nom_de(ob))
        n = pieces.index(nom_de(ob)) + 1
        bvh = BVHTree.FromPolygons(vv, ff)
        lo = [min(c[k] for c in vv) for k in range(3)]
        hi = [max(c[k] for c in vv) for k in range(3)]
        for ia in range(max(0, int((lo[1] - AV0) / R) - 1), min(NA, int((hi[1] - AV0) / R) + 2)):
            for ih in range(max(0, int((lo[2] - H0) / R) - 1), min(NH, int((hi[2] - H0) / R) + 2)):
                # un rayon le long de x : ses entrées et sorties dans la pièce (fermée)
                o, d = Vector((X0 - 5.0, AV0 + ia * R, H0 + ih * R)), Vector((1.0, 0.0, 0.0))
                coups = []
                for _ in range(32):
                    hit = bvh.ray_cast(o, d)
                    if hit[0] is None:
                        break
                    coups.append(hit[0].x)
                    o = hit[0] + d * 1e-4
                if len(coups) % 2:
                    continue
                for x0, x1 in zip(coups[0::2], coups[1::2]):
                    m = (xs >= x0) & (xs <= x1)
                    plein[ia, ih, m] = True
                    etiquette[ia, ih, m] = n


# ce qui ne bouge pas, une fois pour toutes ; puis l'arme au repos (le modèle : le chien armé, le levier levé)
plein_fixe = np.zeros((NA, NH, NX), dtype=bool)
etiquette_fixe = np.zeros((NA, NH, NX), dtype=np.uint8)
voxeliser([ob for ob in VOXELS if nom_de(ob) not in MOBILES], plein_fixe, etiquette_fixe)
plein, etiquette = plein_fixe.copy(), etiquette_fixe.copy()
voxeliser([ob for ob in VOXELS if nom_de(ob) in MOBILES], plein, etiquette)


def distance(masque, n, bord_dehors=True):
    """La distance (en voxels, de face en face) de chaque voxel du masque à sa sortie, jusqu'à n. bord_dehors : ce qui
    est hors de la grille est hors du masque (la profondeur dans l'arme) ; sinon il en fait partie (la distance à une
    pièce, que le bord de la grille ne doit pas remplacer : le bout de l'auriculaire, qui passe à 5 cm sur la gauche de
    l'arme, s'y croyait collé à la crosse)."""
    d = np.zeros(masque.shape, dtype=np.int16)
    m = masque.copy()
    for _ in range(n):
        if not m.any():
            break
        d[m] += 1
        e = m.copy()
        e[1:] &= m[:-1]
        e[:-1] &= m[1:]
        e[:, 1:] &= m[:, :-1]
        e[:, :-1] &= m[:, 1:]
        e[:, :, 1:] &= m[:, :, :-1]
        e[:, :, :-1] &= m[:, :, 1:]
        if bord_dehors:
            e[0] = e[-1] = False
            e[:, 0] = e[:, -1] = False
            e[:, :, 0] = e[:, :, -1] = False
        m = e
    return d


def vers(noms, etiq=None):
    """La distance (voxels) à quelques pièces de l'arme, hors d'elles."""
    etiq = etiquette if etiq is None else etiq
    return distance(~np.isin(etiq, [i + 1 for i, n in enumerate(pieces) if n in noms]), int(12 / R), False)


prof = distance(plein, int(30 / R))
vers_arme = distance(~plein, int(12 / R), False)
vers_crosse = vers({"Crosse"})
vers_detente = vers({"Commandes"})
vers_cadre = vers({"Carcasse", "Levier_Corps"})
print("ARME", pieces, int(plein.sum()), "voxels")

# --- la main ----------------------------------------------------------------------------------------------------
M = json.load(open(MAINS, encoding="utf-8"))
OS = {o["nom"]: o for o in M["os"]}


def uq(q):
    return Quaternion((q[3], q[0], q[1], q[2]))


base, axes = {}, {}   # axes[(os, dof)] : l'axe du muscle dans le repère de l'os (positif : le doigt s'ouvre)
for m in M["muscles"]:
    base[m["os"]] = uq(m["base"])
    axe, angle = (base[m["os"]].inverted() @ uq(m["petit"])).to_axis_angle()
    if angle > math.pi:
        angle -= 2 * math.pi
    axes[(m["os"], m["dof"])] = axe * (1.0 if angle >= 0 else -1.0)
CHASSIS = list(M["chassis"])
MORCEAUX = {(ch, b): np.array(d["v"], dtype=float) for ch, parts in M["chassis"].items() for b, d in parts.items()}
MAIN = OS["RightHand"]
Wq, Wp = uq(MAIN["q"]), Vector(MAIN["p"])


def lire(grille, pts):
    """Ce que vaut la grille sous des points du repère de l'arme d'Unity (m) : -1 hors de la grille."""
    av, h, x = pts[:, 2] * 1000, pts[:, 1] * 1000, -pts[:, 0] * 1000
    ia, ih, ix = (np.round((v - v0) / R).astype(int) for v, v0 in ((av, AV0), (h, H0), (x, X0)))
    ok = (ia >= 0) & (ia < NA) & (ih >= 0) & (ih < NH) & (ix >= 0) & (ix < NX)
    out = np.full(len(pts), -1, dtype=np.int16)
    out[ok] = grille[ia[ok], ih[ok], ix[ok]]
    return out


def enfonce(pts, profondeur=None):
    d = lire(prof if profondeur is None else profondeur, pts)
    return float(d.max()) * R if (d > 0).any() else 0.0


def ecart(pts, grille, profondeur=None):
    """La distance (mm) des points à des pièces de l'arme : 0 dès qu'un point est dans l'arme."""
    if (lire(prof if profondeur is None else profondeur, pts) > 0).any():
        return 0.0
    e = lire(grille, pts)
    e = e[e >= 0]
    return float(e.min()) * R if len(e) else 99.0


def morceau(ch, b, q, pp):
    v = MORCEAUX.get((ch, b))
    return None if v is None else v @ np.array(q.to_matrix()).T + np.array(pp)


def phalange(b, pq, pp, dofs):
    """La phalange b sous son parent (pq, pp), tournée de dofs = [(dof, degrés)] depuis la pose du clip."""
    lq = base[b].copy()
    for dof, phi in dofs:
        lq = lq @ Quaternion(axes[(b, dof)], math.radians(phi))
    return pq @ lq, pp + pq @ Vector(OS[b]["lp"]), lq


# le bout du pouce vise le dessus du bout du levier, sur le flanc gauche (repère de l'arme d'Unity)
levier = Vector(G["Q"](*G["BOUT_LEVIER"], G["XL_LEVIER"] + 0.0038)) * G["ECHELLE"]
CIBLE_POUCE = np.array([-levier.x, levier.z + 0.004, -levier.y])
TOL = a.jeu + 1.5   # au-delà, une phalange n'est plus essayée


def chercher(doigt):
    b1, b2, b3 = CHAINES[doigt]
    ecarts = np.arange(-30.0, 30.1, 7.5) if doigt == "Thumb" else [-10.0, 0.0, 10.0]
    g1, g2, g3 = np.arange(-60.0, 30.1, 5.0), np.arange(-40.0, 80.1, 5.0), np.arange(-50.0, 40.1, 5.0)
    if doigt == "Thumb":
        g1 = np.arange(-25.0, 60.1, 5.0)
    meilleur = None
    for e in ecarts:
        for f1 in g1:
            d1 = [(1, e), (2, f1)]
            q1, p1, l1 = phalange(b1, Wq, Wp, d1)
            m1 = [morceau(ch, b1, q1, p1) for ch in CHASSIS]
            e1 = max(enfonce(m) for m in m1 if m is not None)
            if e1 > TOL:
                continue
            for f2 in g2:
                q2, p2, l2 = phalange(b2, q1, p1, [(2, f2)])
                m2 = [morceau(ch, b2, q2, p2) for ch in CHASSIS]
                e2 = max(enfonce(m) for m in m2 if m is not None)
                if e2 > TOL:
                    continue
                for f3 in g3:
                    q3, p3, l3 = phalange(b3, q2, p2, [(2, f3)])
                    m3 = [morceau(ch, b3, q3, p3) for ch in CHASSIS]
                    e3 = max(enfonce(m) for m in m3 if m is not None)
                    if e3 > TOL:
                        continue
                    pen = max(e1, e2, e3)
                    ref2, ref3 = morceau(REFERENCE, b2, q2, p2), morceau(REFERENCE, b3, q3, p3)
                    score = 20.0 * max(0.0, pen - a.jeu) + 0.01 * (abs(e) + abs(f1) + abs(f2) + abs(f3))
                    if doigt == "Index":
                        # le bout sur la détente
                        milieu = min(ecart(m, vers_arme) for m in m2 if m is not None)
                        bout = min(ecart(m, vers_detente) for m in m3 if m is not None)
                        score += 2.0 * max(0.0, bout - 1.0) + 0.3 * max(0.0, milieu - 4.0)
                    elif doigt == "Thumb":
                        # couché le long du flanc gauche, le bout vers le levier
                        milieu = min(ecart(m, vers_cadre) for m in m2 if m is not None)
                        bout = min(ecart(m, vers_cadre) for m in m3 if m is not None)
                        score += (1.0 * max(0.0, bout - 1.0) + 0.5 * max(0.0, milieu - 1.5)
                                  + 0.05 * float(np.linalg.norm(ref3.mean(axis=0) - CIBLE_POUCE)) * 1000)
                    else:
                        # la base contre la bande avant, le milieu et le bout sur le caoutchouc de la crosse
                        base_ = min(ecart(m, vers_arme) for m in m1 if m is not None)
                        milieu, bout = ecart(ref2, vers_crosse), ecart(ref3, vers_crosse)
                        score += (0.4 * max(0.0, base_ - 1.5) + 1.5 * max(0.0, milieu - 1.0)
                                  + 1.5 * max(0.0, bout - 1.0) + 0.01 * abs(f3 - 0.7 * f2))
                    if meilleur is None or score < meilleur[0]:
                        meilleur = (score, [d1, [(2, f2)], [(2, f3)]], pen, milieu, bout, [l1, l2, l3])
    return meilleur


tenue, resultats = {}, {}
for doigt in ORDRE:
    m = chercher(doigt)
    if m is None:
        raise SystemExit(f"TENUE {doigt} : aucune pose n'évite l'arme")
    score, angles, pen, milieu, bout, locales = m
    tenue[doigt] = locales
    resultats[doigt] = {"angles": [[[int(d), float(f)] for d, f in x] for x in angles], "enfonce": pen,
                        "milieu": milieu, "bout": bout}
    print(f"TENUE {doigt:<7} enfoncé {pen:4.1f} mm, milieu à {milieu:4.1f}, bout à {bout:4.1f} | angles "
          f"{[[round(f) for _, f in x] for x in angles]}")

paume = {ch: enfonce(morceau(ch, "RightHand", Wq, Wp)) for ch in CHASSIS}
gauche = {ch: enfonce(np.array(d["v"], dtype=float)) for ch, d in M["gauche"].items()}
print("TENUE paume enfoncée :", {k: round(v, 1) for k, v in paume.items()}, "; main gauche :",
      {k: round(v, 1) for k, v in gauche.items()})


# --- le pouce qui travaille (2026-11-03) : sur le levier, levé puis abaissé, et sur la crête du chien, abattu puis
# armé. Le geste le posait par une cinématique inverse de son seul bout (rechargement_golden_gun.resoudre_phalanges),
# 6,5 mm au-dessus de la pièce : le pouce du CT-01 a 23 mm d'épaisseur, le levier ne sort que de 6 mm du flanc, et il
# entrait de 7,5 à 12,9 mm dans la carcasse selon le châssis. Chaque pose est cherchée ici comme la tenue, l'arme dans
# l'état de cette étape du rechargement (golden_gun.pose : le levier abaissé, le chien abattu ou armé) : rien
# d'enfoncé chez aucun châssis, la dernière phalange du CT-01 contre la pièce, au plus près du point où le pouce
# appuie, et le moins loin possible de la tenue.
def echantillons(ch, b, pas=2.0):
    """Des points sur les faces de la pièce de l'os b du châssis ch (repère de l'os), tous les `pas` mm au plus : les
    sommets seuls laisseraient une face de bloc traverser le levier (5 mm d'épaisseur) sans qu'aucun n'y entre."""
    d = M["chassis"][ch].get(b)
    if d is None:
        return None
    v = np.array(d["v"], dtype=float)
    points = [v]
    for f in d["f"]:
        p0, p1, p2 = v[f[0]], v[f[1]], v[f[2]]
        n = int(max(np.linalg.norm(p1 - p0), np.linalg.norm(p2 - p0), np.linalg.norm(p2 - p1)) * 1000 / pas) + 1
        if n < 2:
            continue
        uu, vv = np.meshgrid(np.arange(n + 1), np.arange(n + 1))
        garde = uu + vv <= n
        uu, vv = uu[garde] / n, vv[garde] / n
        points.append(p0 + np.outer(uu, p1 - p0) + np.outer(vv, p2 - p0))
    return np.concatenate(points)


POUCE_FIN = {(ch, b): echantillons(ch, b) for ch in CHASSIS for b in CHAINES["Thumb"]}


def morceau_fin(ch, b, q, pp):
    v = POUCE_FIN.get((ch, b))
    return None if v is None else v @ np.array(q.to_matrix()).T + np.array(pp)


GROUPE_LEVIER = bpy.data.objects[atelier.nom("Levier")]
GROUPE_CHIEN = bpy.data.objects[atelier.nom("Chien")]
REPOS_LEVIER, REPOS_CHIEN = GROUPE_LEVIER.rotation_euler.x, GROUPE_CHIEN.rotation_euler.x
E = G["ECHELLE"]


def poser_arme(levier, chien):
    """Le levier abaissé de `levier` degrés, le chien abattu de `chien` degrés (golden_gun.pose)."""
    GROUPE_LEVIER.rotation_euler.x = REPOS_LEVIER - math.radians(levier)
    GROUPE_CHIEN.rotation_euler.x = REPOS_CHIEN + math.radians(chien)
    vue.update()


def unity_de(p):
    """Un point du monde de Blender (m) dans le repère de l'arme d'Unity."""
    return np.array([-p.x, p.z, -p.y])


def suivre(groupe, point, avant, apres):
    """Le point d'une pièce (monde de Blender) quand son groupe passe de la pose `avant` à la pose `apres`."""
    poser_arme(*avant)
    m0 = groupe.matrix_world.copy()
    poser_arme(*apres)
    return groupe.matrix_world @ m0.inverted() @ point


# où le pouce appuie : le dessus du bout moleté du levier (près de son bout), la crête striée du chien (abattu)
DESSUS_LEVIER = Vector(G["Q"](G["BOUT_LEVIER"][0] + 0.0005, G["BOUT_LEVIER"][1] + G["R_MOLETAGE"], G["XL_LEVIER"])) * E
CRETE = Vector(G["Q"](*G["CRETE_CHIEN"])) * E
ABAISSE, ARME = G["ABAISSEMENT_LEVIER"], G["ARMEMENT_CHIEN"]
# (nom, levier abaissé, chien abattu, la pièce, le point où il appuie)
POSES_POUCE = [("levier_haut", 0.0, ARME, "Levier_Corps", DESSUS_LEVIER),
               ("levier_bas", ABAISSE, ARME, "Levier_Corps", suivre(GROUPE_LEVIER, DESSUS_LEVIER, (0, ARME), (ABAISSE, ARME))),
               ("chien_abattu", 0.0, ARME, "Chien_Corps", CRETE),
               ("chien_arme", 0.0, 0.0, "Chien_Corps", suivre(GROUPE_CHIEN, CRETE, (0, ARME), (0, 0)))]
DEPART_POUCE = [x[1] for x in resultats["Thumb"]["angles"][0]] + [resultats["Thumb"]["angles"][1][0][1],
                                                                  resultats["Thumb"]["angles"][2][0][1]]


def chercher_pouce(profondeur, vers_piece, cible, grille=None):
    """Élague sur les sommets des pièces (rapide), puis juge sur leurs faces (POUCE_FIN) les poses dont la dernière
    phalange peut toucher le point. grille : les angles essayés (écarts, plis des trois phalanges) ; par défaut, la
    grille large, puis des passes au degré et demi près autour de la meilleure pose, tant qu'elles l'améliorent (au pas
    de 5°, le pouce ne suivait pas le levier qui descend)."""
    if grille is None:
        meilleur = chercher_pouce(profondeur, vers_piece, cible,
                                  (np.arange(-30.0, 30.1, 7.5), np.arange(-25.0, 60.1, 5.0), np.arange(-40.0, 80.1, 5.0),
                                   np.arange(-50.0, 40.1, 5.0)))
        for _ in range(4):
            if meilleur is None:
                return None
            (_, e0), (_, f10) = meilleur[1][0]
            f20, f30 = meilleur[1][1][0][1], meilleur[1][2][0][1]
            fine = chercher_pouce(profondeur, vers_piece, cible,
                                  tuple(np.arange(v - 6.0, v + 6.1, 1.5) for v in (e0, f10, f20, f30)))
            if fine is None or fine[0] >= meilleur[0] - 1e-6:
                break
            meilleur = fine
        return meilleur
    b1, b2, b3 = CHAINES["Thumb"]
    meilleur = None

    def fin(b, q, pp):
        return max(enfonce(m, profondeur) for m in (morceau_fin(ch, b, q, pp) for ch in CHASSIS) if m is not None)

    ecarts, plis1, plis2, plis3 = grille
    for e in ecarts:
        for f1 in plis1:
            d1 = [(1, e), (2, f1)]
            q1, p1, l1 = phalange(b1, Wq, Wp, d1)
            if max(enfonce(m, profondeur) for m in (morceau(ch, b1, q1, p1) for ch in CHASSIS) if m is not None) > TOL:
                continue
            e1 = None
            for f2 in plis2:
                q2, p2, l2 = phalange(b2, q1, p1, [(2, f2)])
                if max(enfonce(m, profondeur) for m in (morceau(ch, b2, q2, p2) for ch in CHASSIS) if m is not None) > TOL:
                    continue
                e2 = None
                for f3 in plis3:
                    q3, p3, l3 = phalange(b3, q2, p2, [(2, f3)])
                    if max(enfonce(m, profondeur) for m in (morceau(ch, b3, q3, p3) for ch in CHASSIS)
                           if m is not None) > TOL:
                        continue
                    if float(np.min(np.linalg.norm(morceau(REFERENCE, b3, q3, p3) - cible, axis=1))) * 1000 > 40.0:
                        continue   # sa dernière phalange ne passe pas près du point
                    e1 = fin(b1, q1, p1) if e1 is None else e1
                    e2 = fin(b2, q2, p2) if e2 is None else e2
                    if max(e1, e2) > TOL:
                        break
                    e3 = fin(b3, q3, p3)
                    if e3 > TOL:
                        continue
                    pen = max(e1, e2, e3)
                    ref3 = morceau_fin(REFERENCE, b3, q3, p3)
                    # la distance à la seule pièce visée (ecart rendrait 0 dès qu'un point effleure une autre pièce)
                    vp = lire(vers_piece, ref3)
                    vp = vp[vp >= 0]
                    contact = float(vp.min()) * R if len(vp) else 99.0
                    pres = float(np.min(np.linalg.norm(ref3 - cible, axis=1))) * 1000
                    change = sum(abs(x - x0) for x, x0 in zip((e, f1, f2, f3), DEPART_POUCE))
                    score = (20.0 * max(0.0, pen - a.jeu) + 2.0 * max(0.0, contact - 1.0) + 0.3 * max(0.0, pres - 2.0)
                             + 0.01 * change)
                    if meilleur is None or score < meilleur[0]:
                        meilleur = (score, [d1, [(2, f2)], [(2, f3)]], pen, contact, pres, [l1, l2, l3])
    return meilleur


pouce_qui_travaille, poses_pouce = {}, {}
for nom, levier_bas, chien_abattu, piece, point in POSES_POUCE:
    poser_arme(levier_bas, chien_abattu)
    p_etat, e_etat = plein_fixe.copy(), etiquette_fixe.copy()
    voxeliser([ob for ob in VOXELS if nom_de(ob) in MOBILES], p_etat, e_etat)
    prof_etat = distance(p_etat, int(12 / R))
    vers_etat = vers({piece}, e_etat)
    m = chercher_pouce(prof_etat, vers_etat, unity_de(point))
    if m is None:
        raise SystemExit(f"POUCE {nom} : aucune pose n'évite l'arme")
    score, angles, pen, contact, pres, locales = m
    poses_pouce[nom] = locales
    # où la dernière phalange du CT-01 passe au plus près du point d'appui (repère de l'arme d'Unity, mm)
    q, pp = Wq, Wp
    for b, lq in zip(CHAINES["Thumb"], locales):
        pp, q = pp + q @ Vector(OS[b]["lp"]), q @ lq
    ref3 = morceau_fin(REFERENCE, CHAINES["Thumb"][2], q, pp)
    proche = ref3[int(np.argmin(np.linalg.norm(ref3 - unity_de(point), axis=1)))] * 1000
    print(f"POUCE {nom:<12} point d'appui {np.round(unity_de(point) * 1000, 1)}, le pouce au plus près en "
          f"{np.round(proche, 1)}")
    pouce_qui_travaille[nom] = {"angles": [[[int(d), float(f)] for d, f in x] for x in angles], "enfonce": pen,
                                "contact": contact, "pres": pres,
                                "rotations": [c for q in locales for c in (q.x, q.y, q.z, q.w)]}
    print(f"POUCE {nom:<12} enfoncé {pen:4.1f} mm, à {contact:4.1f} mm de la pièce, à {pres:4.1f} mm du point | angles "
          f"{[[round(f) for _, f in x] for x in angles]}")
poser_arme(0.0, 0.0)

rotations = [c for doigt in ORDRE for q in tenue[doigt] for c in (q.x, q.y, q.z, q.w)]
with open(SORTIE, "w", encoding="utf-8") as f:
    json.dump({"os": [b for doigt in ORDRE for b in CHAINES[doigt]], "rotations": rotations, "doigts": resultats,
               "paume": paume, "mainGauche": gauche, "pouce": pouce_qui_travaille, "resolution": R,
               "clip": M["clip"]}, f, ensure_ascii=False, indent=1)
print("TENUE écrite :", SORTIE)

# --- les vues de contrôle : l'arme et les deux mains, chaque châssis ---------------------------------------------
if a.rendus:
    import kit_lowpoly as kit
    dossier = os.path.abspath(a.rendus)
    os.makedirs(dossier, exist_ok=True)
    scene = atelier.scene
    col = bpy.data.collections.new("Tenue")
    scene.collection.children.link(col)
    mat = bpy.data.materials.new("Main_Tenue")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.62, 0.72, 1.0)

    def blender(w):
        return Vector((-w.x, -w.z, w.y))

    poses = {}
    for doigt in ORDRE:
        pq, pp = Wq, Wp
        for b, lq in zip(CHAINES[doigt], tenue[doigt]):
            pp, pq = pp + pq @ Vector(OS[b]["lp"]), pq @ lq
            poses[b] = (pq, pp)
    poses["RightHand"] = (Wq, Wp)
    objets = {}
    for ch in CHASSIS:
        verts, faces = [], []
        for b, d in M["chassis"][ch].items():
            q, pp = poses[b]
            i0 = len(verts)
            verts += [blender(pp + q @ Vector(v)) for v in d["v"]]
            faces += [tuple(i0 + i for i in f) for f in d["f"]]
        i0 = len(verts)
        verts += [blender(Vector(v)) for v in M["gauche"][ch]["v"]]
        faces += [tuple(i0 + i for i in f) for f in M["gauche"][ch]["f"]]
        me = bpy.data.meshes.new(f"Tenue_{ch}")
        me.from_pydata(verts, [], faces)
        me.materials.append(mat)
        objets[ch] = bpy.data.objects.new(me.name, me)
        col.objects.link(objets[ch])
    prise = Vector((0.0, -0.010, -0.010))
    oeil = G["MIRE"] + Vector((0, 0.40, 0))
    vues = {"oeil": (oeil, oeil + Vector((0, -1, 0)), 60, 1280, 720),
            "oeil_proche": (oeil, prise + Vector((0, 0, 0.02)), 18, 1280, 720),
            "gauche": (prise + Vector((0.34, -0.01, 0.03)), prise + Vector((0, -0.02, 0.01)), 34, 1000, 1000),
            "droite": (prise + Vector((-0.34, -0.01, 0.03)), prise + Vector((0, -0.02, 0.01)), 34, 1000, 1000),
            "dessous": (prise + Vector((0.08, -0.06, -0.30)), prise + Vector((0, -0.03, 0.0)), 36, 1000, 1000)}
    for nom, (pos, cible, angle, l, h) in vues.items():
        cam = bpy.data.cameras.new(nom)
        cam.sensor_fit = "VERTICAL"
        cam.angle = math.radians(angle)
        cam.clip_start = 0.005
        o = bpy.data.objects.new(nom, cam)
        col.objects.link(o)
        o.location = pos
        o.rotation_euler = (cible - pos).to_track_quat("-Z", "Y").to_euler()
        for ch, ob in objets.items():
            for autre in objets.values():
                autre.hide_render = autre is not ob
            kit.rendre(scene, o, os.path.join(dossier, f"{ch}_{nom}.png"), l, h, echantillons=24)
print("fini")
