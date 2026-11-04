"""Le rechargement du Big D, l'arme du Golden Gun, calculé sur le squelette du Y Bot pour Unity (2026-11-03 ; refait le
même jour d'après le Wingman d'Apex Legends, à la demande de l'utilisateur : « fluide, aisé et nonchalant »).

Le Big D est un derringer à un coup (Tools/Blender/golden_gun.py) : on ne change pas de chargeur, on ouvre l'arme. La
main droite fait tout, au poignet : le pouce abaisse le levier pendant que l'arme prend son élan ; un coup de poignet
vers le haut, arrêté net, l'arme roulée vers l'extérieur, et le canon bascule seul jusqu'à l'horizontale, rebondit sur
sa butée, et l'éjecteur chasse la douille vide, qui s'envole sur le côté puis tombe ; l'arme flotte jusque devant la
poitrine, la chambre tournée vers le haut. La
main gauche ne fait que charger : elle prend une cartouche neuve, debout dans sa boucle sur la hanche gauche, la balle
en bas, par son bourrelet, entre le pouce et l'index, et la glisse dans la chambre. Un coup de poignet vers le bas, et
le canon retombe et se verrouille ; le pouce droit arme le chien, et la main gauche revient tenir la droite. Le geste
suit les étapes de l'arme (golden_gun.ETAPES, en part du rechargement).

Le reste vient du rechargement à chargeur (rechargement_chargeur.py) : le haut du corps et les doigts droits du clip de
Mixamo (Y Bot@Reloading), recalés sur ces étapes ; l'arme tenue par la main droite, ici rattachée au bassin ; la main
gauche menée par la cinématique inverse du jeu le long d'un trajet tracé dans le repère de l'arme, de la cartouche ou
du corps ; la forme du clip (3,3 s, la partie utile de 0,4 à 2,5 s) ; l'export. Ici, la main gauche suit une spline
d'un arrêt au suivant (GesteBigD.image : elle ne repart ni ne ralentit d'un coup aux étapes qu'elle traverse), et une
étape sur l'arme qu'elle a quittée ne la suit plus (GesteBigD.instant_de : les coups de poignet ne l'entraînent pas).
Un clip humanoïde ne garde des doigts que leurs plis (sans torsion) : la pince de la main gauche ne tourne que d'eux
(plis_des_doigts, relevés dans Unity), et le jeu repose lui-même le pouce droit, d'après la fiche (pouce_droit_unity).
Les doigts qui travaillent (la pince de
la main gauche, le pouce droit sur le levier puis sur le chien) sont posés par le calcul : une petite cinématique
inverse sur leurs trois phalanges (resoudre_phalanges).

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/animations/rechargement_golden_gun.py -- \
    [--fbx <dossier>] [--rendus <dossier> --etapes 0,0.1,... --vues buste,face_droite,gauche,fps]
    [--sequence <dossier> --vues-sequence buste,face_droite,fps --images 50] [--contacts --details 0.5,0.7] [--diag]

Contrôles : --contacts mesure, image par image, ce que les mains et les avant-bras enfoncent dans les pièces fermées
de l'arme et dans la cartouche tenue, et où passe la douille vide (loin de la tête) ; --diag dit où sont l'arme, sa
chambre et la cartouche par rapport à la paume, et ce que la cinématique inverse des doigts laisse d'écart.
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
ICI = os.path.dirname(os.path.abspath(__file__))
if ICI not in sys.path:
    sys.path.insert(0, ICI)
import rechargement_chargeur as rc  # noqa: E402

cr, kit, Cle, u = rc.cr, rc.kit, rc.Cle, rc.u
lisse = rc.lisse


def charger_big_d():
    """Le modèle, ses mesures et ses étapes : golden_gun.py, exécuté tel quel (il construit l'arme dans sa scène)."""
    chemin = os.path.join(rc.DOSSIER, "golden_gun.py")
    ns = {"ARGS": [], "__file__": chemin, "__name__": "golden_gun"}
    with open(chemin, encoding="utf-8") as fichier:
        exec(compile(fichier.read(), chemin, "exec"), ns)
    return ns


G = charger_big_d()
E = G["ECHELLE"]


def unity(av, h, x=0.0):
    """Un point du Big D (relevé autour de PIV, avant ECHELLE) dans le repère de l'arme d'Unity (x à droite, y en haut,
    z vers la bouche), en mètres."""
    return (-x * E, (G["PIV"][1] + h) * E, (G["PIV"][0] + av) * E)


AXE = unity(*G["AXE"])                                   # l'axe de la charnière
CULOT = unity(G["AV_CULASSE"] - 0.0001, G["D_AXE"])       # le culot de la cartouche dans la chambre
R_BOURRELET = 0.0077 * E                                 # le rayon du bourrelet de la cartouche (golden_gun.douille)

# Les repères que la main gauche touche (point de la paume, normale de la paume, direction des doigts), dans le repère
# de l'arme d'Unity, canon fermé.
# La main gauche qui tient la droite, comme au PA-05 : la prise de « Pistol NoAim Idle », relevée dans Unity
GARDE = ((-0.103, 0.038, 0.008), (0.872, -0.196, -0.448), (0.394, -0.263, 0.881))

# La pince de la main gauche, dans le repère de l'os de la main (Blender : +Y les doigts, +Z la paume, le pouce du côté
# -X) : le culot de la cartouche entre le bout du pouce et celui de l'index, la balle qui part devant la paume. Le
# pouce et l'index s'y referment (resoudre_phalanges), les trois autres doigts restent repliés (« tenir »).
PINCE_CULOT = Vector((-0.050, 0.150, 0.075))
PINCE_TRAVERS = Vector((1.0, 0.15, -0.25))    # du pouce vers l'index
PINCE_AXE = Vector((-0.10, 0.35, 0.93))       # du culot vers la balle
PINCE_OUVERTE = 0.014                          # de combien la pince s'ouvre pour prendre ou lâcher
# Autour de la cartouche, les doigts vont vers la droite de l'arme et un peu vers son bas (canon ouvert : vers l'avant
# du robot) : la main vient de la gauche, l'avant-bras loin de l'arme.
PINCE_TOUR = math.atan2(0.6, 1.0)

# Le pouce droit (repère de l'arme d'Unity) : la surface qu'il touche, sa normale, et la direction de sa dernière
# phalange. Son bout appuie de son épaisseur (EPAISSEUR_POUCE) au-dessus de la surface. Ses poses tordent ses phalanges,
# ce que l'humanoïde d'Unity ne rejoue pas (plis_des_doigts : ramené à ses plis, il ne pouvait à la fois toucher le
# levier et le chien et rester hors de la carcasse) : le jeu les repose lui-même pendant le rechargement, d'après la
# fiche (pouce_droit_unity, BreakActionView).
EPAISSEUR_POUCE = 0.0065
LEVIER_DESSUS = unity(G["BOUT_LEVIER"][0] + 0.0005, G["BOUT_LEVIER"][1] + G["R_MOLETAGE"],
                      G["XL_LEVIER"])   # le bout moleté du levier, par-dessus
LEVIER_NORMALE = (-0.29, 0.96, 0.0)
LEVIER_AXE = unity(*G["AXE_LEVIER"])
LEVIER_POUCE = (0.15, -0.35, 0.92)            # le pouce couché le long du flanc
CHIEN_DESSUS = unity(*G["CRETE_CHIEN"])                                 # la crête striée du chien abattu
CHIEN_NORMALE = (0.0, 0.975, -0.221)
CHIEN_AXE = unity(*G["AXE_CHIEN"])
CHIEN_POUCE = (0.50, -0.45, 0.74)             # passé par-dessus la carcasse, depuis la gauche
# Au repos, il est où le met la tenue de la main droite (TENUE), couché le long du flanc gauche.
# Entre le repos et le chien, il passe par le dehors (sinon il frôle l'arrière de la carcasse) : son bout levé, à
# gauche de l'arme, à mi-chemin, à sa place par rapport à la crête du chien
DEGAGE_BOUT = tuple(c + d for c, d in zip(CHIEN_DESSUS, (-0.037, -0.0184, 0.0178)))
DEGAGE_POUCE = (0.30, -0.30, 0.90)


def decaler(tenue, dx=0.0, dy=0.0, dz=0.0):
    (p, n, d) = tenue
    return ((p[0] + dx, p[1] + dy, p[2] + dz), n, d)


def tourner_yz(p, centre, degres, point=True):
    """Un point (ou une direction) tourné autour d'un axe parallèle à x : comme le canon s'ouvre (la bouche vers le
    haut puis vers l'arrière), comme le levier s'abaisse et comme le chien s'arme, pour un angle positif."""
    a = math.radians(degres)
    c, s = math.cos(a), math.sin(a)
    if not point:
        return (p[0], p[1] * c + p[2] * s, -p[1] * s + p[2] * c)
    y, z = p[1] - centre[1], p[2] - centre[2]
    return (p[0], centre[1] + y * c + z * s, centre[2] - y * s + z * c)


def ouvert(p, theta, point=True):
    """Un point (ou une direction) du canon, canon ouvert de theta degrés : il tourne autour de l'axe de la charnière,
    la bouche vers le haut puis vers l'arrière."""
    return tourner_yz(p, AXE, theta, point)


def repere_cartouche(theta, decalage):
    """Le repère de la cartouche (origine, x, y, z) dans celui de l'arme, canon ouvert de theta degrés, sortie de la
    chambre de `decalage` mètres."""
    z = ouvert((0.0, 0.0, 1.0), theta, False)
    y = ouvert((0.0, 1.0, 0.0), theta, False)
    o = ouvert(CULOT, theta)
    return tuple(oc - zc * decalage for oc, zc in zip(o, z)), (1.0, 0.0, 0.0), y, z


def dans(repere, p, point=True):
    o, x, y, z = repere
    return tuple((o[i] if point else 0.0) + p[0] * x[i] + p[1] * y[i] + p[2] * z[i] for i in range(3))


def autour(z, doigts):
    """Le repère d'une cartouche (x, y, z), z vers la balle, tourné pour que la pince y mette les doigts vers `doigts`
    (repère indirect, comme celui de l'arme d'Unity vu de Blender : x = z ^ y)."""
    z = z.normalized()
    e1 = (doigts - z * doigts.dot(z)).normalized()
    e2 = z.cross(e1)
    c, s = math.cos(PINCE_TOUR), math.sin(PINCE_TOUR)
    return e1 * c + e2 * s, e1 * s - e2 * c, z


def entre(qa, qb, w):
    """Deux rotations mêlées par le plus court chemin."""
    return qa.slerp(qb if qa.dot(qb) >= 0.0 else -qb, w)


PLIS_DOIGTS = os.path.join(ICI, "plis_doigts_ybot.json")
# La tenue de la main droite sur le Big D (tenue_golden_gun.py) : les rotations locales d'Unity de ses quinze phalanges,
# calculées sur les pièces des robots du jeu. Le geste en part : les doigts y restent (l'arme ne bouge pas dans la main
# droite), et le pouce s'y repose entre le levier et le chien.
TENUE = os.path.join(ICI, "tenue_golden_gun.json")
MONDE_UNITY = Matrix(((-1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0)))   # le monde d'Unity dans celui de Blender


def vecteur_rotation(R):
    """Le petit vecteur d'une rotation (son axe fois son angle, en radians)."""
    q = R.to_quaternion()
    if q.w < 0.0:
        q.negate()
    axe, angle = q.to_axis_angle()
    return axe * angle


def plis_des_doigts(sq):
    """Les plis que l'humanoïde d'Unity rejoue, pour chaque phalange du pouce et de l'index des deux mains (relevés dans
    Unity sur le robot du jeu : Duel Arena > Animations > Relever les plis des doigts du Y Bot, plis_doigts_ybot.json).
    Pour chacune : sa base (le repos, ramené à ce que l'humanoïde en garde : il perd la torsion du pouce), les axes de
    ses plis et leurs bornes (radians depuis cette base, le muscle de -1 à +1), dans le repère de repos de l'os sous
    son parent, celui de ses rotations locales. Un clip humanoïde ne garde que ces plis : un doigt qui tourne autrement
    arrive ailleurs dans le jeu (le pouce droit tombait à 3 à 7 cm du levier et du chien)."""
    with open(PLIS_DOIGTS, encoding="utf-8") as fichier:
        releve = {o["nom"]: o for o in json.load(fichier)["os"]}
    U = MONDE_UNITY
    repos_b = {nom: sq.rotation(sq.repos[nom]) for nom in releve}
    gardee = {}
    for nom, o in releve.items():
        # d'un os d'Unity à celui de Blender (une matrice impropre : Unity est indirect), relevé au repos
        K = repos_b[nom].transposed() @ U @ Matrix(o["repos"])
        gardee[nom] = U @ Matrix(o["base"]) @ K.inverted()
    plis = {}
    for nom, o in releve.items():
        parent = sq.parent[nom]
        if parent not in gardee:
            continue   # la main : la cinématique inverse du bras la place
        F = gardee[parent] @ repos_b[parent].transposed() @ repos_b[nom]   # le repos de l'os sous son parent
        axes, bornes = [], []
        for m in o["muscles"]:
            a = vecteur_rotation(U @ Matrix(m["petit"]) @ U.transposed()).normalized()
            bas = vecteur_rotation(U @ Matrix(m["moins"]) @ U.transposed()).dot(a)
            haut = vecteur_rotation(U @ Matrix(m["plus"]) @ U.transposed()).dot(a)
            axes.append(F.transposed() @ a)
            bornes.append((min(0.0, bas), max(0.0, haut)))
        plis[nom] = ((F.transposed() @ gardee[nom]).to_quaternion(), axes, bornes)
    return plis


def tenue_droite(sq):
    """La tenue de la main droite (tenue_golden_gun.json), en rotations des os de Blender : la rotation locale d'Unity
    L d'une phalange, sous son parent, est Kᵀ_parent · (repos du parent)ᵀ · repos · base · K (pouce_droit_unity) ; sa
    base est donc reposᵀ · repos du parent · K_parent · L · Kᵀ (K, la matrice d'un os d'Unity à celui de Blender,
    relevée au repos, plis_doigts_ybot.json, est orthogonale)."""
    with open(TENUE, encoding="utf-8") as fichier:
        tenue = json.load(fichier)
    r = tenue["rotations"]
    return depuis_unity(sq, tenue["os"], r), r


def depuis_unity(sq, noms, r):
    """Les rotations locales d'Unity `r` (x, y, z, w à la suite) des os `noms` (sans « mixamorig: »), en rotations des
    os de Blender (voir tenue_droite)."""
    with open(PLIS_DOIGTS, encoding="utf-8") as fichier:
        releve = {o["nom"]: o for o in json.load(fichier)["os"]}
    bases = {}
    for i, court in enumerate(noms):
        nom = rc.os_(court)
        parent = sq.parent[nom]
        K = {n: sq.rotation(sq.repos[n]).transposed() @ MONDE_UNITY @ Matrix(releve[n]["repos"]) for n in (nom, parent)}
        repos, repos_parent = sq.repos[nom].to_3x3().normalized(), sq.repos[parent].to_3x3().normalized()
        L = Quaternion((r[4 * i + 3], r[4 * i], r[4 * i + 1], r[4 * i + 2])).to_matrix()
        bases[nom] = (repos.transposed() @ repos_parent @ K[parent] @ L @ K[nom].transposed()).to_quaternion()
    return bases


def pouce_de_la_tenue(sq):
    """Le pouce droit qui travaille (tenue_golden_gun.json, « pouce ») : ses poses sur le levier, levé et abaissé, et
    sur la crête du chien, abattu et armé, cherchées contre les pièces des robots du jeu, l'arme dans l'état de cette
    étape. La cinématique inverse de son seul bout le mettait dans la carcasse (de 7,5 à 12,9 mm selon le châssis : le
    pouce du CT-01 a 23 mm d'épaisseur, le levier ne sort que de 6 mm du flanc)."""
    with open(TENUE, encoding="utf-8") as fichier:
        poses = json.load(fichier).get("pouce", {})
    chaine = [f"RightHandThumb{k}" for k in (1, 2, 3)]
    return {nom: depuis_unity(sq, chaine, p["rotations"]) for nom, p in poses.items()}


def resoudre_lineaire(A, b):
    """A x = b, par élimination de Gauss avec pivot partiel (A carrée, petite)."""
    n = len(b)
    M = [list(A[i]) + [b[i]] for i in range(n)]
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(M[r][c]))
        M[c], M[p] = M[p], M[c]
        for r in range(c + 1, n):
            k = M[r][c] / M[c][c]
            for j in range(c, n + 1):
                M[r][j] -= k * M[c][j]
    x = [0.0] * n
    for i in range(n - 1, -1, -1):
        x[i] = (M[i][n] - sum(M[i][j] * x[j] for j in range(i + 1, n))) / M[i][i]
    return x


def spline_serree(temps, valeurs, t):
    """La spline cubique qui passe par les valeurs (des Vector) aux temps donnés, de vitesse nulle aux deux bouts : ni la
    vitesse ni l'accélération ne sautent aux passages (une course d'un arrêt au suivant). Entre deux valeurs seules, la
    courbe en S de `lisse`."""
    n = len(temps) - 1
    if n == 0:
        return valeurs[0].copy()
    t = min(temps[-1], max(temps[0], t))
    h = [temps[i + 1] - temps[i] for i in range(n)]
    pentes = [(valeurs[i + 1] - valeurs[i]) / h[i] for i in range(n)]
    A = [[0.0] * (n + 1) for _ in range(n + 1)]
    A[0][0], A[0][1] = 2.0 * h[0], h[0]
    A[n][n - 1], A[n][n] = h[n - 1], 2.0 * h[n - 1]
    for i in range(1, n):
        A[i][i - 1], A[i][i], A[i][i + 1] = h[i - 1], 2.0 * (h[i - 1] + h[i]), h[i]
    droite = [pentes[0] * 6.0] + [(pentes[i] - pentes[i - 1]) * 6.0 for i in range(1, n)] + [pentes[n - 1] * -6.0]
    M = [Vector(c) for c in zip(*[resoudre_lineaire(A, [d[k] for d in droite]) for k in range(len(valeurs[0]))])]
    i = min(n - 1, max(k for k in range(n) if temps[k] <= t))
    a = (temps[i + 1] - t) / h[i]
    b = 1.0 - a
    return valeurs[i] * a + valeurs[i + 1] * b + (M[i] * (a ** 3 - a) + M[i + 1] * (b ** 3 - b)) * (h[i] * h[i] / 6.0)


def spline_rotations(temps, rotations, t):
    """La même spline pour des rotations (des Quaternion) : sur leurs quatre composantes, chacune du côté de la
    précédente (le plus court chemin de l'une à l'autre), puis ramenée à l'unité."""
    alignees = [rotations[0].copy()]
    for q in rotations[1:]:
        q = q.copy()
        if q.dot(alignees[-1]) < 0.0:
            q.negate()
        alignees.append(q)
    return Quaternion(spline_serree(temps, [Vector(q) for q in alignees], t)).normalized()


def resoudre_phalanges(sq, poses, bases, chaine, depart, cibles, directions=(), raideur=0.02, tours=60, plis=None):
    """La petite cinématique inverse des doigts : les rotations (locales, celles des bases) des os de `chaine` (de la
    main vers le bout) qui amènent des points de ces os sur leurs cibles. cibles : (rang dans la chaîne, distance le
    long de l'os (m), point visé (monde), poids) ; directions : (rang, direction visée de l'os (monde), poids). Sans
    `plis`, chaque os tourne de sa rotation de départ (`depart`) d'un petit vecteur de rotation ; avec (plis_des_doigts),
    il ne tourne que de ses plis, ceux que l'humanoïde d'Unity rejoue, depuis leur base, entre leurs bornes : le départ
    est ramené sur eux. La raideur retient les os près de leur départ. Moindres carrés amortis (Levenberg-Marquardt),
    dérivées par différences finies. Rend les rotations et l'écart qui reste à chaque cible."""
    tailles = [len(plis[nom][1]) if plis else 3 for nom in chaine]
    debuts = [sum(tailles[:k]) for k in range(len(chaine))]
    n = sum(tailles)

    def rotations(omega):
        q = {}
        for k, nom in enumerate(chaine):
            if plis:
                q_base, axes, _ = plis[nom]
                w = sum((a * t for a, t in zip(axes, omega[debuts[k]:debuts[k] + tailles[k]])), Vector())
                q[nom] = Quaternion(w.normalized(), w.length) @ q_base if w.length > 1e-12 else q_base.copy()
            else:
                w = Vector(omega[3 * k:3 * k + 3])
                q[nom] = depart[nom] @ Quaternion(w.normalized(), w.length) if w.length > 1e-12 else depart[nom].copy()
        return q

    def poses_de(omega):
        p = dict(poses)
        for nom, q in rotations(omega).items():
            m = q.to_matrix().to_4x4()
            m.translation = bases[nom].translation
            p[nom] = sq.enfant(nom, p, m)
        return p

    def points(p):
        return [sq.position(p[chaine[k]]) + sq.rotation(p[chaine[k]]) @ Vector((0.0, long, 0.0))
                for k, long, _, _ in cibles]

    # le départ, ramené sur les plis (moindres carrés sur leurs axes)
    omega0 = [0.0] * n
    if plis:
        for k, nom in enumerate(chaine):
            q_base, axes, _ = plis[nom]
            d = depart[nom] @ q_base.inverted()
            if d.w < 0.0:
                d.negate()
            axe, angle = d.to_axis_angle()
            v = axe * angle
            A = [[a.dot(b) for b in axes] for a in axes]
            omega0[debuts[k]:debuts[k] + tailles[k]] = resoudre_lineaire(A, [a.dot(v) for a in axes])

    def residus(omega):
        p = poses_de(omega)
        r = []
        for (_, _, cible, poids), point in zip(cibles, points(p)):
            r += list((point - cible) * poids)
        for k, voulu, poids in directions:
            r += list((sq.rotation(p[chaine[k]]) @ Vector((0.0, 1.0, 0.0)) - voulu.normalized()) * poids)
        r += [(c - c0) * raideur for c, c0 in zip(omega, omega0)]
        if plis:   # les bornes de l'humanoïde : au-delà, Unity ramène le muscle
            for k, nom in enumerate(chaine):
                for t, (bas, haut) in zip(omega[debuts[k]:debuts[k] + tailles[k]], plis[nom][2]):
                    r.append(0.5 * max(0.0, bas - t, t - haut))
        return r

    omega = list(omega0)
    r = residus(omega)
    cout = sum(x * x for x in r)
    mu = 1e-3
    for _ in range(tours):
        colonnes = []
        for j in range(n):
            o = list(omega)
            o[j] += 1e-5
            colonnes.append([(a - b) / 1e-5 for a, b in zip(residus(o), r)])
        A = [[sum(ci * cj for ci, cj in zip(colonnes[i], colonnes[j])) for j in range(n)] for i in range(n)]
        g = [sum(ci * ri for ci, ri in zip(colonnes[i], r)) for i in range(n)]
        for i in range(n):
            A[i][i] += mu * (1.0 + A[i][i])
        essai = [a + b for a, b in zip(omega, resoudre_lineaire(A, [-x for x in g]))]
        r2 = residus(essai)
        c2 = sum(x * x for x in r2)
        if c2 < cout:
            omega, r, cout, mu = essai, r2, c2, mu * 0.3
        else:
            mu *= 10.0
    p = poses_de(omega)
    return rotations(omega), [(point - cible).length for (_, _, cible, _), point in zip(cibles, points(p))]


class CleBigD(Cle):
    """Une étape du trajet de la paume. Ses repères en plus de ceux du rechargement à chargeur : « cartouche » (la
    cartouche alignée sur la chambre, sortie de `d` mètres, ou de ce que dit golden_gun.pose ; `ou` : « pince », la
    paume telle que la pince la met, décalée de `decal` dans le repère de la cartouche) ; « poche » (la cartouche debout
    dans sa boucle, sur la hanche gauche, la balle en bas : `ou` est le nom d'une place autour d'elle) ; « corps » (par
    rapport à la paume de la garde au début du geste, dans les axes du monde : `ou` = (décalage, normale de la paume,
    direction des doigts))."""

    def __init__(self, f, ou, repere="arme", pole=None, doigts="ouvert", arret=True, d=None, decal=(0.0, 0.0, 0.0)):
        super().__init__(f, ou, repere, pole, doigts, arret)
        self.d, self.decal = d, decal


# L'arme dans la main droite, rattachée au bassin, à chaque étape : (roulis à droite °, tangage vers le haut °, cap
# vers la gauche °, vers la gauche, l'avant et le haut du robot m), autour de sa poignée, depuis sa pose de départ (le
# canon déjà levé de 22°). Présentée à la main gauche : levée, tournée vers la gauche (la chambre ouverte regarde en
# haut et vers elle : l'avant-bras gauche arrive par le côté, sans passer devant les yeux), ramenée devant la poitrine.
CHARGE = (0.0, 20.0, 50.0, 0.09, 0.07, -0.07)
# Le pôle du coude gauche (monde de Blender : +X la gauche du robot, -Y l'avant, +Z le haut) : à la hanche, dehors et
# en arrière ; au-dessus de la chambre, dehors et en bas ; ailleurs, en bas et en dehors
COUDE_POCHE = rc.COUDE_POCHE
COUDE_CHARGE = Vector((0.7, 0.3, -0.6))
COUDE_BIGD = Vector((0.6, 0.0, -1.0))


def coup(s):
    """Un coup de poignet : il part doucement, file, et s'arrête net (la fin d'une courbe en S, resserrée)."""
    return lisse(min(1.0, max(0.0, s)) ** 2.5)


class BigD:
    """Ce que le calcul du rechargement à chargeur demande à une arme (voir rechargement_chargeur.Arme)."""

    code = "BIG-D"
    clip = "Rechargement BIG-D"
    collection = None
    milieu = (0.0, 0.0, 0.0)
    garde_main = GARDE
    tenue = GARDE         # (le calcul à chargeur en tire la prise d'un chargeur ; la pince se calcule sur la main)
    # les étapes du geste qui ne sont pas celles de l'arme
    saisie = 0.25         # la cartouche pincée dans sa boucle
    retour = 0.94         # la main gauche revenue tenir la droite

    def __init__(self):
        e = G["ETAPES"]
        self.etapes_arme = e
        t = rc.t_clip
        o0 = e["ouverture"][0]
        i0, i1 = e["insertion"]
        f0, f1 = e["fermeture"]
        # Le haut du corps et la main droite du clip de Mixamo, recalés (voir rechargement_chargeur.Arme.recalage) :
        # dans l'ancien, la main gauche est à la hanche à 1,1 s, insère à 1,62 s, est sur le dessus de la carcasse à
        # 2,1 s et revenue à 2,5 s.
        self.recalage = [(0.0, 0.0), (t(0.0), 0.40), (t(self.saisie), 1.10), (t(i0 + 0.04), 1.62),
                         (t(f1 - 0.04), 2.10), (t(self.retour), 2.50), (rc.IMAGES / rc.IPS, 3.30)]
        # (f, pose, courbe qui y mène) : l'élan (l'arme s'abaisse un peu) ; le coup de poignet vers le haut, arrêté net
        # quand le canon part, l'arme roulée vers l'extérieur (la douille chassée part sur le côté, loin de la visière :
        # droite, elle lui passait à 11 cm du visage) ; un rien de recul ; puis l'arme flotte jusqu'à la main gauche et
        # s'y tient, à peine mouvante. Pour la refermer (demandé par l'utilisateur : « un mouvement brusque du poignet et
        # rapide vers le bas ») : le poignet se casse vers le haut en ramenant l'arme devant, puis le coup, sec, vers le
        # bas, arrêté net au moment où le canon part se rabattre (golden_gun.ETAPES["fermeture"]) ; un rien de rebond,
        # et le retour sans hâte à la tenue.
        rien = (0.0,) * 6
        charge2 = tuple(c + d for c, d in zip(CHARGE, (0.0, 2.0, -2.0, 0.0, 0.0, 0.005)))
        coup_fin = f0 + 0.005             # le poignet arrêté : le canon est déjà parti
        coup_debut = coup_fin - 0.032     # 80 ms de coup
        self.presentation = [
            (0.0, rien, "lisse"),
            (0.06, (0.0, -8.0, 0.0, 0.0, 0.005, -0.02), "lisse"),
            (o0, (35.0, 28.0, -8.0, 0.0, -0.01, 0.04), "coup"),
            (o0 + 0.03, (30.0, 23.0, -8.0, 0.0, -0.01, 0.035), "lisse"),
            (0.44, CHARGE, "lisse"),
            (0.595, charge2, "lisse"),
            (coup_debut, (-2.0, 40.0, 22.0, 0.05, 0.05, -0.01), "lisse"),
            (coup_fin, (-2.0, -20.0, 8.0, 0.03, 0.03, -0.06), "coup"),
            (coup_fin + 0.03, (-1.0, -9.0, 6.0, 0.025, 0.02, -0.04), "lisse"),
            (0.92, rien, "lisse"),
            (1.0, rien, "lisse"),
        ]

    def presente(self, f):
        for (fa, a, _), (fb, b, courbe) in zip(self.presentation, self.presentation[1:]):
            if f <= fb:
                s = (f - fa) / max(1e-6, fb - fa)
                s = coup(s) if courbe == "coup" else lisse(s)
                return tuple(x + (y - x) * s for x, y in zip(a, b))
        return self.presentation[-1][1]

    def trajet(self):
        e = self.etapes_arme
        i0, i1 = e["insertion"]
        p, c, b = COUDE_POCHE, COUDE_CHARGE, COUDE_BIGD
        # la main lâchée, à plat, la paume vers le corps ; puis la main au repos, sous l'arme
        lache = ((0.03, 0.02, -0.07), (-0.7, 0.3, -0.6), (0.2, -0.9, -0.3))
        repos = ((0.05, 0.03, -0.13), (-0.8, 0.2, -0.5), (0.1, -0.6, -0.8))
        return [
            CleBigD(0.0, GARDE, pole=b, doigts="garde"),
            # elle lâche la main droite pendant l'élan, et descend vers la hanche
            CleBigD(0.05, lache, repere="corps", pole=b, doigts="plat", arret=False),
            CleBigD(self.saisie - 0.09, "dessus_poche", repere="poche", pole=p, doigts="pince_ouverte", arret=False),
            CleBigD(self.saisie - 0.03, "poche", repere="poche", pole=p, doigts="pince_ouverte"),
            CleBigD(self.saisie, "poche", repere="poche", pole=p, doigts="pince"),
            CleBigD(self.saisie + 0.02, "poche", repere="poche", pole=p, doigts="pince"),
            CleBigD(self.saisie + 0.06, "sortie_poche", repere="poche", pole=p, doigts="pince", arret=False),
            # elle monte la cartouche vers la chambre, en arc, la balle déjà tournée vers elle
            CleBigD(0.41, "pince", repere="cartouche", d=0.16, decal=(-0.08, 0.0, 0.0), pole=c, doigts="pince",
                    arret=False),
            CleBigD(i0, "pince", repere="cartouche", pole=c, doigts="pince"),
            # elle la pousse presque au fond, la lâche, et la cartouche finit de glisser seule dans la chambre tournée
            # vers le haut (au fond, le bout des doigts entrerait dans la culasse)
            CleBigD(self.lacher, "pince", repere="cartouche", pole=c, doigts="pince"),
            CleBigD(i1, "pince", repere="cartouche", d=0.025, pole=c, doigts="pince_ouverte", arret=False),
            # elle s'écarte (le coup de poignet part sans elle), puis elle va, sans hâte, sous l'arme
            CleBigD(i1 + 0.03, "pince", repere="cartouche", d=0.08, decal=(-0.05, 0.04, 0.0), pole=c, doigts="ouvert",
                    arret=False),
            CleBigD(0.79, repos, repere="corps", pole=b, doigts="plat", arret=False),
            CleBigD(self.retour, GARDE, pole=b, doigts="garde"),
            CleBigD(1.0, GARDE, pole=b, doigts="garde"),
        ]

    @property
    def lacher(self):
        """La cartouche lâchée, presque au fond : elle finit de glisser seule."""
        i0, i1 = self.etapes_arme["insertion"]
        return i0 + 0.8 * (i1 - i0)

    def etapes(self):
        e = {nom: list(v) for nom, v in self.etapes_arme.items()}
        e.update(saisie=self.saisie, lacher=self.lacher, retour=self.retour)
        return e


B = BigD()
rc.A = B


class GesteBigD(rc.Geste):
    """Le calcul du rechargement à chargeur, avec les repères de la cartouche, de la hanche et du corps du Big D, la
    pince de la main gauche et le pouce droit."""

    def image(self, t):
        """Comme rechargement_chargeur.Geste.image, mais la paume suit une spline d'un arrêt au suivant (position,
        rotation et coude) : elle ne repart ni ne ralentit d'un coup aux étapes qu'elle ne fait que traverser ; puis le
        pouce droit."""
        sq = self.sq
        f = (t - rc.DEBUT_UTILE) / (rc.FIN_UTILE - rc.DEBUT_UTILE)
        fc = min(max(f, 0.0), 1.0)
        self.f = fc
        bases = self.bases_source(self.recaler(t))
        poses = {}
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        R_arme, o_arme = self.arme_presentee(poses, fc)
        R_droite = R_arme @ rc.PRISE_ROTATION.to_matrix().transposed()
        ecart_droit = self.atteindre("Right", o_arme - R_droite @ rc.PRISE_POSITION, R_droite, None, poses, bases)
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        # la course où tombe f, d'un arrêt au suivant : ses étapes, chacune dans son repère à cet instant
        cles = self.cles
        i = max(k for k in range(len(cles)) if cles[k].f <= fc + 1e-9) if fc > 0 else 0
        j = min(i + 1, len(cles) - 1)
        debut = max(k for k in range(i + 1) if k == 0 or cles[k].arret)
        fin = min(k for k in range(j, len(cles)) if k == len(cles) - 1 or cles[k].arret)
        resolues = [self.resoudre(cles[k], poses, self.instant_de(k, fc)) for k in range(debut, fin + 1)]
        temps = [cles[k].f for k in range(debut, fin + 1)]
        paume = spline_serree(temps, [r[0] for r in resolues], fc)
        R_main = spline_rotations(temps, [r[1].to_quaternion() for r in resolues], fc).to_matrix()
        pole = spline_serree(temps, [r[2] for r in resolues], fc)
        ecart = max(ecart_droit, self.atteindre("Left", paume - R_main @ rc.PAUME, R_main, pole, poses, bases))
        # les doigts : la pose de l'étape, fondue vers la suivante
        A, B = cles[i], cles[j]
        s = 0.0 if j == i else min(1.0, max(0.0, (fc - A.f) / (B.f - A.f)))
        da, db = self.doigts[A.doigts], self.doigts[B.doigts]
        wd = lisse(min(1.0, s / 0.6)) if A.doigts != B.doigts else 0.0
        for d in rc.DOIGTS:
            if rc.os_(d) in bases and d in da:
                m = da[d].slerp(db[d], wd).to_matrix().to_4x4()
                m.translation = bases[rc.os_(d)].translation
                bases[rc.os_(d)] = m
        for nom in sq.ordre:
            poses[nom] = sq.enfant(nom, poses, bases[nom])
        self.poser_pouce_droit(bases, poses, fc)
        return bases, poses, ecart, f

    # -- les doigts qui travaillent ----------------------------------------------------------------------------
    def longueur(self, nom):
        return self.sq.arm.data.bones[nom].length * self.sq.M.to_scale().x

    def pose_de_reference(self):
        """Les bases et les poses du clip à l'instant où l'état Recharge y entre (les doigts s'y calculent dans le
        repère de leur main, qui ne dépend pas de l'instant)."""
        bases = self.bases_source(self.recaler(rc.t_clip(0.0)))
        poses = {}
        for nom in self.sq.ordre:
            poses[nom] = self.sq.enfant(nom, poses, bases[nom])
        return bases, poses

    def preparer_doigts(self):
        super().preparer_doigts()
        self.plis = plis_des_doigts(self.sq)
        bases, poses = self.pose_de_reference()
        self.preparer_pince(bases, poses)
        self.preparer_pouce_droit(bases, poses)

    def preparer_pince(self, bases, poses):
        """La pince : le pouce et l'index qui serrent le bourrelet (leurs bouts de part et d'autre, à son épaisseur
        près), puis la même, ouverte ; les trois autres doigts repliés. Et la paume vue de la cartouche ainsi tenue
        (self.pince : point, normale, doigts, dans le repère de la cartouche)."""
        sq, os_ = self.sq, rc.os_
        main = poses[os_("LeftHand")]
        R_H, P_H = sq.rotation(main), sq.position(main)
        a = PINCE_AXE.normalized()
        w = (PINCE_TRAVERS - a * PINCE_TRAVERS.dot(a)).normalized()
        index = [os_(f"LeftHandIndex{k}") for k in (1, 2, 3)]
        pouce = [os_(f"LeftHandThumb{k}") for k in (1, 2, 3)]
        depart = {**{os_(d): q for d, q in self.doigts["tenir"].items() if "Index" in d},
                  **{os_(d): q for d, q in self.doigts["ouvert"].items() if "Thumb" in d}}
        self.ecarts_doigts = {}
        for nom, ouverture in (("pince", 0.0), ("pince_ouverte", PINCE_OUVERTE)):
            milieu = PINCE_CULOT + a * 0.0013    # au milieu de l'épaisseur du bourrelet
            vise_index = P_H + R_H @ (milieu + w * (R_BOURRELET + 0.0055 + ouverture / 2))
            vise_pouce = P_H + R_H @ (milieu - w * (R_BOURRELET + 0.0062 + ouverture / 2))
            q_i, e_i = resoudre_phalanges(sq, poses, bases, index, depart,
                                          [(2, self.longueur(index[2]) + 0.004, vise_index, 1.0)], raideur=0.01,
                                          plis=self.plis)
            q_p, e_p = resoudre_phalanges(sq, poses, bases, pouce, depart,
                                          [(2, self.longueur(pouce[2]) + 0.003, vise_pouce, 1.0)], raideur=0.01,
                                          plis=self.plis)
            self.doigts[nom] = {**self.doigts["tenir"], **{n[len("mixamorig:"):]: q for n, q in {**q_i, **q_p}.items()}}
            self.ecarts_doigts[nom] = e_i + e_p
        # la cartouche dans la main (repère de l'os de la main), et la paume vue d'elle
        X, Y, Z = autour(a, Vector((0.0, 1.0, 0.0)))

        def vue(v, point=True):
            v = v - PINCE_CULOT if point else v
            return (v.dot(X), v.dot(Y), v.dot(Z))
        self.pince = (vue(rc.PAUME), vue(Vector((0.0, 0.0, 1.0)), False), vue(Vector((0.0, 1.0, 0.0)), False))
        self.cartouche_dans_main = (PINCE_CULOT.copy(), X, Y, Z)

    def preparer_pouce_droit(self, bases, poses):
        """Le pouce droit au repos, sur le bout du levier, levier levé puis abaissé, et sur la crête du chien, abattu
        puis armé : l'arme ne bouge pas dans la main droite, ces poses valent tout le geste. Le repos et les autres
        doigts droits sont ceux de la tenue de la main droite (tenue_golden_gun.py, que le jeu repose l'arme en main) :
        dans le clip de Mixamo, ils se refermaient sur deux centimètres et entraient dans la crosse."""
        sq, os_ = self.sq, rc.os_
        R_arme, o_arme = self.repere_arme(poses)
        chaine = [os_(f"RightHandThumb{k}") for k in (1, 2, 3)]
        self.pouce_chaine = chaine
        tenue, self.tenue_unity = tenue_droite(sq)
        self.prise_droite = {nom: q for nom, q in tenue.items() if "Thumb" not in nom}
        bout = self.longueur(chaine[2]) + 0.002

        def cible(surface, normale):
            p = tuple(s + n * EPAISSEUR_POUCE for s, n in zip(surface, Vector(normale).normalized()))
            return o_arme + R_arme @ u(*p)

        self.pouce = {"repos": {n: tenue[n] for n in chaine}}
        depart = self.pouce["repos"]

        abaisse = G["ABAISSEMENT_LEVIER"]
        arme = G["ARMEMENT_CHIEN"]
        places = {
            "levier_haut": (LEVIER_DESSUS, LEVIER_NORMALE, LEVIER_POUCE),
            "levier_bas": (tourner_yz(LEVIER_DESSUS, LEVIER_AXE, abaisse), tourner_yz(LEVIER_NORMALE, None, abaisse, False),
                           LEVIER_POUCE),
            "chien_abattu": (CHIEN_DESSUS, CHIEN_NORMALE, CHIEN_POUCE),
            "chien_arme": (tourner_yz(CHIEN_DESSUS, CHIEN_AXE, arme), tourner_yz(CHIEN_NORMALE, None, arme, False),
                           CHIEN_POUCE),
        }
        travail = pouce_de_la_tenue(sq)
        for nom, (surface, normale, direction) in places.items():
            if nom in travail:   # cherchée contre les pièces des robots (tenue_golden_gun.py)
                self.pouce[nom] = travail[nom]
                continue
            q, ecart = resoudre_phalanges(sq, poses, bases, chaine, depart, [(2, bout, cible(surface, normale), 1.0)],
                                          [(2, R_arme @ u(*direction), 0.005)], raideur=0.01)
            self.pouce[nom] = q
            self.ecarts_doigts[f"pouce {nom}"] = ecart
        q, ecart = resoudre_phalanges(sq, poses, bases, chaine, depart, [(2, bout, o_arme + R_arme @ u(*DEGAGE_BOUT), 1.0)],
                                      [(2, R_arme @ u(*DEGAGE_POUCE), 0.005)], raideur=0.01)
        self.pouce["degage"] = q
        self.ecarts_doigts["pouce degage"] = ecart

    def instant_de(self, k, f):
        """L'instant où lire une étape rattachée à l'arme : la main suit l'arme du début à la fin d'une suite d'étapes
        dans le même repère ; avant, elle vise où l'arme sera quand elle y arrivera ; après, l'endroit où elle l'a
        quittée (l'arme qui part d'un coup de poignet ne l'entraîne pas)."""
        if not hasattr(self, "suites"):
            self.suites = {}
            cles = self.cles
            k0 = 0
            while k0 < len(cles):
                k1 = k0
                while k1 + 1 < len(cles) and cles[k1 + 1].repere == cles[k0].repere:
                    k1 += 1
                for n in range(k0, k1 + 1):
                    self.suites[n] = (cles[k0].f, cles[k1].f)
                k0 = k1 + 1
        if self.cles[k].repere not in ("arme", "cartouche"):
            return f
        debut, fin = self.suites[k]
        return min(fin, max(debut, f))

    def pouce_droit(self, f):
        """Les rotations des phalanges du pouce droit à la part f. Posé sur le levier, il l'abaisse, le tient et le
        relâche, comme le levier (golden_gun.appui) ; plus tard, le canon verrouillé (il reste couché sur la carcasse
        pendant le coup de poignet qui le referme : bougeant à côté du canon, il paraissait le rabattre), il monte sur le
        chien par le dehors et l'arme, comme le chien (golden_gun.doux), puis revient se coucher sur la carcasse, par le
        dehors. Entre deux contacts, une spline de pose en pose, arrêtée aux contacts."""
        e = G["ETAPES"]
        l0, l1 = e["levier"]
        c0, c1 = e["armement"]
        P, chaine = self.pouce, self.pouce_chaine
        if l0 <= f <= l1:
            w = G["appui"](G["part"](f, "levier"))
            return {n: entre(P["levier_haut"][n], P["levier_bas"][n], w) for n in chaine}
        if c0 <= f <= c1:
            w = G["doux"](G["part"](f, "armement"))
            return {n: entre(P["chien_abattu"][n], P["chien_arme"][n], w) for n in chaine}
        courses = [
            [(0.0, "repos"), (l0, "levier_haut")],
            [(l1, "levier_haut"), (l1 + 0.06, "repos")],
            [(c0 - 0.09, "repos"), (c0 - 0.045, "degage"), (c0, "chien_abattu")],
            [(c1, "chien_arme"), (c1 + 0.04, "degage"), (c1 + 0.10, "repos")],
        ]
        for course in courses:
            if course[0][0] <= f <= course[-1][0]:
                temps = [c[0] for c in course]
                return {n: spline_rotations(temps, [P[c[1]][n] for c in course], f) for n in chaine}
        return {n: P["repos"][n] for n in chaine}

    def poser_pouce_droit(self, bases, poses, f):
        tournes = self.pouce_droit(f)
        tournes.update(self.prise_droite)
        for nom, q in tournes.items():
            m = q.to_matrix().to_4x4()
            m.translation = bases[nom].translation
            bases[nom] = m
        for nom in self.sq.ordre:
            if nom.startswith(rc.os_("RightHand")) and nom != rc.os_("RightHand"):
                poses[nom] = self.sq.enfant(nom, poses, bases[nom])

    # -- l'arme et les repères -------------------------------------------------------------------------------
    def calculer(self, arm):
        # la pose de départ de l'arme (celle du clip de Mixamo quand l'état Recharge y entre), dans le repère du bassin,
        # et la paume de la garde par rapport au bassin
        bases0, poses0 = self.pose_de_reference()
        R_a, o_a = self.repere_arme(poses0)
        bassin = poses0[rc.os_("Hips")]
        R_h, p_h = self.sq.rotation(bassin), self.sq.position(bassin)
        self.depart_bassin = (R_h.transposed() @ R_a, R_h.transposed() @ (o_a - p_h))
        self.f = 0.0
        self.garde_bassin = None
        poses0 = self.poser_arme_depart(bases0, poses0)
        point, _, _ = self.resoudre(CleBigD(0.0, GARDE), poses0)
        self.garde_bassin = point - self.sq.position(poses0[rc.os_("Hips")])
        return super().calculer(arm)

    def poser_arme_depart(self, bases, poses):
        """Les poses à la part 0, l'arme posée dans la main droite (pour lire où est la garde)."""
        R_arme, o_arme = self.arme_presentee(poses, 0.0)
        R_droite = R_arme @ rc.PRISE_ROTATION.to_matrix().transposed()
        self.atteindre("Right", o_arme - R_droite @ rc.PRISE_POSITION, R_droite, None, poses, bases)
        for nom in self.sq.ordre:
            poses[nom] = self.sq.enfant(nom, poses, bases[nom])
        return poses

    def arme_presentee(self, poses, f):
        """L'arme rattachée au bassin plutôt qu'au buste, présentée (BigD.presentation) ; le déplacement est dans les
        axes du robot (monde de Blender : +X sa gauche, -Y l'avant, +Z le haut)."""
        bassin = poses[rc.os_("Hips")]
        R_h, p_h = self.sq.rotation(bassin), self.sq.position(bassin)
        R0, o0 = R_h @ self.depart_bassin[0], p_h + R_h @ self.depart_bassin[1]
        roulis, tangage, cap, gauche, avant, haut = B.presente(f)
        tourne = (Matrix.Rotation(math.radians(cap), 3, "Z") @ Matrix.Rotation(-math.radians(tangage), 3, "X")
                  @ Matrix.Rotation(-math.radians(roulis), 3, "Y"))
        return R0 @ tourne, o0 + Vector((gauche, -avant, haut))

    def resoudre(self, cle, poses, tau=None):
        """Le point, la rotation et le coude visés par une étape. Une étape rattachée à l'arme (« arme »,
        « cartouche ») se lit sur l'arme telle qu'elle est présentée à l'instant `tau` (par défaut, maintenant), par
        rapport au bassin d'aujourd'hui."""
        if cle.repere == "poche":
            return self.resoudre_poche(cle, poses)
        if cle.repere == "corps":
            (decalage, normale, doigts) = cle.ou
            bassin = self.sq.position(poses[rc.os_("Hips")])
            return bassin + self.garde_bassin + Vector(decalage), rc.repere_main(Vector(normale), Vector(doigts)), cle.pole
        f = self.f if tau is None else tau
        R_arme, o_arme = self.arme_presentee(poses, f)
        p, n, d = self.pince if cle.ou == "pince" else cle.ou
        if cle.repere == "cartouche":
            pose = G["pose"](f)
            # (avant l'insertion, golden_gun.pose ne la place pas : elle attend à sa distance d'approche)
            approche = pose["cartouche"] if pose["cartouche"] is not None else G["APPROCHE_CARTOUCHE"]
            sortie = cle.d if cle.d is not None else approche * E
            rep = repere_cartouche(pose["canon"], sortie)
            p = tuple(a + b for a, b in zip(p, cle.decal))
            p, n, d = dans(rep, p), dans(rep, n, False), dans(rep, d, False)
        point = o_arme + R_arme @ u(*p)
        R_main = rc.repere_main(R_arme @ u(*n), R_arme @ u(*d))
        pole = cle.pole if isinstance(cle.pole, Vector) else R_arme @ u(*(cle.pole or rc.COUDE_ARME))
        return point, R_main, pole

    def repere_poche(self, poses, place):
        """La cartouche neuve debout dans sa boucle, sur la hanche gauche, la balle en bas (ou soulevée, `place`) :
        son culot et son repère dans le monde, tourné pour que la main la prenne les doigts vers l'avant."""
        bassin = self.sq.position(poses[rc.os_("Hips")])
        culot = bassin + rc.POCHE + {"poche": Vector((0.0, 0.0, 0.04)), "dessus_poche": Vector((0.0, 0.0, 0.10)),
                                     "sortie_poche": Vector((0.0, -0.02, 0.15))}[place]
        return culot, autour(Vector((0.0, 0.0, -1.0)), Vector((-0.35, -1.0, 0.0)))

    def resoudre_poche(self, cle, poses):
        """La main la prend par son bourrelet, par-dessus, la paume vers le bas, puis la tire vers le haut."""
        culot, (x, y, z) = self.repere_poche(poses, cle.ou)
        p, n, d = self.pince
        point = culot + x * p[0] + y * p[1] + z * p[2]
        R_main = rc.repere_main(x * n[0] + y * n[1] + z * n[2], x * d[0] + y * d[1] + z * d[2])
        return point, R_main, cle.pole


def arguments():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", default=None, help="dossier où écrire le FBX et sa fiche")
    p.add_argument("--rendus", default=None)
    p.add_argument("--etapes", default="0,0.06,0.1,0.12,0.15,0.18,0.22,0.27,0.3,0.36,0.44,0.49,0.53,0.57,0.62,0.66,"
                                       "0.69,0.72,0.78,0.82,0.86,0.94,1")
    p.add_argument("--vues", default="buste,face_droite,gauche,fps")
    p.add_argument("--diag", action="store_true", help="où sont l'arme et sa chambre, étape par étape")
    p.add_argument("--contacts", action="store_true", help="ce que les mains enfoncent dans l'arme")
    p.add_argument("--details", default="", help="les instants où détailler les contacts, os par os")
    p.add_argument("--sequence", default=None, help="dossier d'une séquence animée (APNG) de tout le geste")
    p.add_argument("--vues-sequence", default="buste,face_droite,fps")
    p.add_argument("--images", type=int, default=50, help="les images de la séquence")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return p.parse_args(argv)


def paume(geste, poses):
    main = poses[rc.os_("LeftHand")]
    return geste.sq.position(main) + geste.sq.rotation(main) @ rc.PAUME


def controler(geste):
    """Le poignet atteint chaque étape ; aux instants qui comptent, la paume est où elle doit être ; les doigts qui
    travaillent sont sur leurs cibles."""
    pires = sorted(geste.mesures, key=lambda m: -m["ecart"])[:3]
    print("CONTROLE BIG-D portée : écart du poignet à sa cible au pire "
          + ", ".join(f"{m['ecart'] * 1000:.1f} mm (f={m['f']:.2f})" for m in pires))
    for nom, ecarts in geste.ecarts_doigts.items():
        print(f"CONTROLE BIG-D doigts {nom} : bout à " + ", ".join(f"{e * 1000:.1f}" for e in ecarts) + " mm de sa cible")
    for cle in geste.cles:
        if not cle.arret:
            continue
        _, poses, _, _ = geste.image(rc.t_clip(cle.f))
        voulu, _, _ = geste.resoudre(cle, poses)
        print(f"CONTROLE BIG-D f={cle.f:.3f} {cle.repere:<9} : paume à {(paume(geste, poses) - voulu).length * 1000:.1f}"
              " mm de sa place")


def diagnostiquer(geste):
    """L'arme par rapport à la tête et au bassin, son canon, sa chambre (monde de Blender : +X la gauche du robot, -Y
    l'avant, +Z le haut)."""
    def r(v, n=3):
        return "(" + ", ".join(f"{c:+.{n}f}" for c in v) + ")"
    for a in ("repos", "levier_haut", "levier_bas", "chien_abattu", "chien_arme"):
        angles = [math.degrees(geste.pouce["repos"][n].rotation_difference(geste.pouce[a][n]).angle) for n in geste.pouce_chaine]
        print(f"DIAG pouce {a:<13} : " + ", ".join(f"{x:.0f}°" for x in angles) + " depuis le repos")
    sq = geste.sq
    for f in (0.0, 0.06, 0.12, 0.15, 0.2, 0.3, 0.44, 0.5, 0.57, 0.64, 0.69, 0.73, 0.8, 0.9, 1.0):
        _, poses, _, _ = geste.image(rc.t_clip(f))
        R, o = geste.repere_arme(poses)
        tete = sq.position(poses[rc.os_("Head")])
        bassin = sq.position(poses[rc.os_("Hips")])
        chambre = R @ u(*ouvert((0.0, 0.0, -1.0), G["pose"](f)["canon"], False))
        print(f"DIAG f={f:.2f} arme-tête {r(o - tete)} arme-bassin {r(o - bassin)} canon {r(R @ u(0, 0, 1), 2)} "
              f"chambre {r(chambre, 2)}")


def pouce_droit_unity(geste, n):
    """Les rotations locales des trois phalanges du pouce droit, telles qu'Unity les pose (x, y, z, w, dans le repère
    de chaque os d'Unity, par rapport à son parent), à n + 1 parts régulières du geste, à la suite (le JSON d'Unity ne
    lit pas de tableaux imbriqués : 12 nombres par part) : le jeu les repose lui-même
    pendant le rechargement (BreakActionView), l'humanoïde n'en rejouant pas la torsion. D'un os de Blender à celui
    d'Unity, la matrice relevée au repos (plis_doigts_ybot.json) : la rotation locale d'Unity est
    Kᵀ_parent · (repos du parent)ᵀ · repos · base · K."""
    with open(PLIS_DOIGTS, encoding="utf-8") as fichier:
        releve = {o["nom"]: o for o in json.load(fichier)["os"]}
    sq = geste.sq
    K = {nom: sq.rotation(sq.repos[nom]).transposed() @ MONDE_UNITY @ Matrix(releve[nom]["repos"])
         for nom in [sq.parent[geste.pouce_chaine[0]]] + geste.pouce_chaine}
    repos = {nom: sq.repos[nom].to_3x3().normalized() for nom in K}
    poses = []
    for i in range(n + 1):
        tournes = geste.pouce_droit(i / n)
        pose = []
        for nom in geste.pouce_chaine:
            parent = sq.parent[nom]
            local = K[parent].transposed() @ repos[parent].transposed() @ repos[nom] @ tournes[nom].to_matrix() @ K[nom]
            q = local.to_quaternion()
            pose += [q.x, q.y, q.z, q.w]
        poses += pose
    return poses


def fiche(geste, chemin):
    """Ce que le jeu lira du geste (Duel Arena > Animations > Importer les rechargements à chargeur, BreakActionGesture) :
    ses étapes (celles de l'arme et les siennes), ce qui bouge sur l'arme (golden_gun.pose : les angles du canon, du
    chien et du levier, les glissements de la douille et de la cartouche, en mètres d'Unity), les repères que les mains
    touchent, et des poses relevées tout au long du rechargement, que les tests rejouent (-1 : rien à cet instant)."""
    def repere(r):
        return {"paume": list(r[0]), "normale": list(r[1]), "doigts": list(r[2])}

    def releve(f):
        p = G["pose"](f)
        return {"f": f, "canon": p["canon"], "chien": p["chien"], "levier": p["levier"],
                "douille": p["douille"] * E if p["douille"] is not None else -1.0,
                "chute": p["chute"] if p["chute"] is not None else -1.0,
                "cartouche": p["cartouche"] * E if p["cartouche"] is not None else -1.0}

    def surface(point, normale):
        return {"point": list(point), "normale": list(Vector(normale).normalized())}
    abaisse, arme = G["ABAISSEMENT_LEVIER"], G["ARMEMENT_CHIEN"]
    donnees = {
        "clip": B.clip, "arme": B.code, "ips": rc.IPS, "duree": rc.IMAGES / rc.IPS,
        "utile": [rc.DEBUT_UTILE, rc.FIN_UTILE], "etapes": B.etapes(), "paumeConception": list(rc.PAUME_UNITY),
        "garde": repere(GARDE), "pince": repere(geste.pince), "poche": list(rc.POCHE), "axe": list(AXE),
        "culot": list(CULOT), "echelle": E, "rayonBourrelet": R_BOURRELET,
        "levier": surface(LEVIER_DESSUS, LEVIER_NORMALE),
        "levierAbaisse": surface(tourner_yz(LEVIER_DESSUS, LEVIER_AXE, abaisse),
                                 tourner_yz(LEVIER_NORMALE, None, abaisse, False)),
        "chien": surface(CHIEN_DESSUS, CHIEN_NORMALE),
        "chienArme": surface(tourner_yz(CHIEN_DESSUS, CHIEN_AXE, arme), tourner_yz(CHIEN_NORMALE, None, arme, False)),
        "angleCanon": G["OUVERTURE_CANON"], "angleChien": arme, "angleLevier": abaisse,
        "departCanon": G["DEPART_CANON"], "departFermeture": G["DEPART_FERMETURE"], "rebondCanon": G["REBOND_CANON"],
        "enfonceLevier": G["ENFONCE_LEVIER"], "relacheLevier": G["RELACHE_LEVIER"], "epaisseurPouce": EPAISSEUR_POUCE,
        "pouceDroit": pouce_droit_unity(geste, 200),
        "tenueDroite": list(geste.tenue_unity),
        "sortieDouille": G["SORTIE_DOUILLE"] * E, "approcheCartouche": G["APPROCHE_CARTOUCHE"] * E,
        "dureeRechargement": G["DUREE_RECHARGEMENT"],
        "releves": [releve(i / 200) for i in range(201)],
    }
    with open(chemin, "w", encoding="utf-8") as f:
        json.dump(donnees, f, ensure_ascii=False, indent=2)
    print("FICHE", chemin)


# --- les rendus de contrôle --------------------------------------------------------------------------------

class Rendu:
    """L'arme en main, ses pièces et ses cartouches, image par image (le jeu le fera de même) : elle suit la main
    droite, ses pièces suivent golden_gun.pose, la douille vide est chassée de la chambre et tombe, la cartouche neuve
    suit la main gauche de la hanche à la chambre."""

    def __init__(self, scene, geste):
        self.geste = geste
        at = G["ATELIER"]
        scene.collection.children.link(at.col)
        self.racine = at.racine
        objet = lambda nom: bpy.data.objects[at.nom(nom)]   # noqa: E731
        self.canon, self.chien, self.levier, self.cartouche = objet("Canon"), objet("Chien"), objet("Levier"), \
            objet("Cartouche")
        self.base_cartouche = self.cartouche.location.copy()
        self.neuve = list(self.cartouche.children)
        col = kit.collection("Cartouches", scene.collection)
        self.vide = bpy.data.objects.new("Douille_Vide", objet("Douille").data)
        col.objects.link(self.vide)
        self.en_main = []
        for o in self.neuve:
            c = bpy.data.objects.new(o.name + "_EnMain", o.data)
            col.objects.link(c)
            self.en_main.append(c)
        pivot = Vector(u(*AXE))
        self.pivot = pivot
        e = G["ETAPES"]
        # la cartouche neuve vue de la main gauche, au moment où elle se présente devant la chambre
        m = geste.mesure(e["insertion"][0])
        self.T_main_cartouche = rc.matrice(m["R_main"], m["p_main"]).inverted() @ self.cartouche_monde(
            m, e["insertion"][0], G["APPROCHE_CARTOUCHE"] * E)
        # la douille vide : sa matrice au moment où elle quitte la chambre, et sa vitesse
        self.t_lache = e["ejection"][1]
        k = round(rc.t_clip(self.t_lache) * rc.IPS)
        m1, m0 = geste.mesures[k], geste.mesures[k - 1]
        self.M_lache = self.douille_monde(m1, m1["f"])
        self.c_lache = self.M_lache @ self.centre_douille()
        # (par seconde de jeu : la partie utile du clip, 2,1 s, est lue en DUREE_RECHARGEMENT)
        self.vitesse = (self.c_lache - self.douille_monde(m0, m0["f"]) @ self.centre_douille()) * rc.IPS \
            * (rc.FIN_UTILE - rc.DEBUT_UTILE) / G["DUREE_RECHARGEMENT"]

    def centre_douille(self):
        return Vector(u(*CULOT)) + Vector(u(0.0, 0.0, 0.0268 * E))

    def canon_monde(self, m, f):
        W = rc.matrice(m["R_arme"], m["o_arme"])
        theta = G["pose"](f)["canon"]
        return W @ Matrix.Translation(self.pivot) @ Matrix.Rotation(-math.radians(theta), 4, "X") \
            @ Matrix.Translation(-self.pivot)

    def douille_monde(self, m, f):
        sortie = G["pose"](f)["douille"]
        sortie = G["SORTIE_DOUILLE"] if sortie is None else sortie
        return self.canon_monde(m, f) @ Matrix.Translation(Vector((0.0, sortie * E, 0.0)))

    def cartouche_monde(self, m, f, sortie):
        return self.canon_monde(m, f) @ Matrix.Translation(Vector((0.0, sortie, 0.0)))

    def poser(self, image):
        m = self.geste.mesures[image - 1]
        f = m["f"]
        pose = G["pose"](f)
        self.racine.matrix_world = rc.matrice(m["R_arme"], m["o_arme"])
        self.canon.rotation_euler.x = -math.radians(pose["canon"])
        self.chien.rotation_euler.x = math.radians(pose["chien"])
        self.levier.rotation_euler.x = -math.radians(pose["levier"])
        # la cartouche neuve : dans la chambre dès qu'elle s'y présente, dans la main de la hanche jusque-là
        dans_chambre = pose["cartouche"] is not None
        for o in self.neuve:
            o.hide_render = not dans_chambre
        self.cartouche.location = self.base_cartouche + Vector((0.0, (pose["cartouche"] or 0.0) * E, 0.0))
        en_main = B.saisie <= f and not dans_chambre
        H = rc.matrice(m["R_main"], m["p_main"])
        for c in self.en_main:
            c.hide_render = not en_main
            c.matrix_world = H @ self.T_main_cartouche
        # la douille vide : dans la chambre, qu'elle quitte, puis qui s'envole et tombe
        if pose["chute"] is None:
            self.vide.matrix_world = self.douille_monde(m, f)
        else:
            tau = pose["chute"]
            deplacement = self.vitesse * tau + Vector((0.0, 0.0, -4.905 * tau * tau))
            tourne = Matrix.Translation(self.c_lache) @ Matrix.Rotation(12.0 * tau, 4, self.M_lache.to_3x3() @
                                                                         Vector((1, 0, 0))) \
                @ Matrix.Translation(-self.c_lache)
            self.vide.matrix_world = Matrix.Translation(deplacement) @ tourne @ self.M_lache
        self.vide.hide_render = (self.vide.matrix_world @ self.centre_douille()).z < 0.01   # au sol


def preparer_studio(scene, rb, col):
    cr.lumieres(col, Vector((0.0, -0.3, 1.2)))
    monde = bpy.data.worlds.new("Monde")
    monde.node_tree.nodes["Background"].inputs["Color"].default_value = kit.srgb(28, 29, 32)
    scene.world = monde
    studio = kit.collection("Studio", scene.collection)
    sol = bpy.data.meshes.new("Sol")
    sol.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
    studio.objects.link(bpy.data.objects.new("Sol", sol))
    return studio


def viser(cam, position, cible):
    cam.location = position
    cam.rotation_euler = (cible - position).to_track_quat("-Z", "Y").to_euler()


class Cameras:
    """Les vues de contrôle : le corps entier (fixe) ; l'arme de près, de devant à gauche et de la gauche (elles la
    suivent sans tourner avec elle) ; de l'œil vers l'arme (la tête repliée comme en vue subjective : ce que le joueur
    peut voir de son geste, pas le cadrage du jeu, qui se règle dans Unity)."""

    FOCALES = {"corps": 45, "buste": 50, "proche": 50, "face_droite": 45, "gauche": 50, "droite": 50, "detail": 60,
               "pouce": 60, "fps": 24}

    def __init__(self, studio, rb):
        self.rb = rb
        self.cams = {nom: kit.camera(f"Cam_{nom}", studio, Vector((0, 0, 1)), Vector((0, -1, 1)), focale)
                     for nom, focale in self.FOCALES.items()}

    def placer(self, vue, m):
        cam = self.cams[vue]
        arme = m["o_arme"] + m["R_arme"] @ u(0.0, 0.07, 0.07)   # le milieu de l'arme fermée
        if vue == "corps":
            centre = Vector((0.0, -0.2, 1.1))
            viser(cam, centre + Vector((1.9, -1.6, 0.35)), centre + Vector((0.0, 0.0, 0.1)))
        elif vue == "buste":   # fixe : le haut du corps, de devant à droite et d'un peu au-dessus
            viser(cam, Vector((-0.95, -1.55, 1.62)), Vector((0.02, -0.12, 1.25)))
        elif vue == "face_droite":
            viser(cam, arme + Vector((-0.50, -0.62, 0.24)), arme + Vector((0.0, 0.0, 0.04)))
        elif vue == "proche":
            viser(cam, arme + Vector((0.50, -0.55, 0.28)), arme)
        elif vue == "gauche":
            viser(cam, arme + Vector((0.80, 0.04, 0.10)), arme)
        elif vue == "droite":
            viser(cam, arme + Vector((-0.80, 0.04, 0.10)), arme)
        elif vue == "detail":
            viser(cam, arme + Vector((0.30, -0.36, 0.22)), arme + Vector((0.0, 0.0, 0.03)))
        elif vue == "pouce":   # le pouce droit sur le levier et le chien : de la gauche de l'arme, de près
            vise = m["o_arme"] + m["R_arme"] @ u(-0.02, 0.07, 0.03)
            viser(cam, vise + m["R_arme"] @ u(-0.30, 0.10, -0.05), vise)
        else:
            tete = self.rb.arm.matrix_world @ self.rb.arm.pose.bones[rc.os_("Head")].head
            viser(cam, tete + Vector((0.0, 0.04, 0.06)), arme)
        return cam


def masque_bras(ob):
    """En vue subjective, le jeu ne montre que les bras du robot (FirstPersonView replie la tête, le cou, le buste et
    les clavicules) : un masque qui ne garde que leurs pièces, allumé pour cette vue."""
    os_bras = ("LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand")
    bras = {g.index for g in ob.vertex_groups if any(g.name.endswith(":" + n) or (":" + n) in g.name for n in os_bras)}
    groupe = ob.vertex_groups.new(name="Bras_VueSubjective")
    groupe.add([v.index for v in ob.data.vertices if any(e.group in bras and e.weight > 0.0 for e in v.groups)], 1.0,
               "REPLACE")
    mod = ob.modifiers.new("Bras", "MASK")
    mod.vertex_group = groupe.name
    mod.show_render = mod.show_viewport = False   # (le contrôle des contacts lit le maillage déformé entier)
    return mod


def controler_contacts(scene, rendu, ob, pas=2, details=()):
    """Ce que les mains et les avant-bras enfoncent dans l'arme et dans la cartouche tenue, image par image : la
    profondeur du sommet le plus enfoncé dans une pièce (le point le plus proche de la pièce, fermée, dit dedans ou
    dehors ; un sommet dit dedans l'est s'il traverse la pièce un nombre impair de fois dans au moins deux directions
    sur trois : près d'une arête, la face la plus proche peut tourner le dos au sommet, et une petite pièce paraissait
    traversée de 3 cm). La main droite tient la poignée : elle y entre un peu, comme au PA-05."""
    from mathutils.bvhtree import BVHTree
    directions = [Vector(d).normalized() for d in ((1.0, 0.31, 0.17), (-0.23, 1.0, 0.41), (0.13, -0.37, 1.0))]

    def dedans(arbre, p):
        impairs = 0
        for d in directions:
            n, o = 0, p.copy()
            while True:
                loc, _, _, _ = arbre.ray_cast(o, d)
                if loc is None:
                    break
                n += 1
                o = loc + d * 1e-5
            impairs += n % 2
        return impairs >= 2
    parties = {"main gauche": ("LeftHand",), "avant-bras gauche": ("LeftForeArm",), "main droite": ("RightHand",),
               "avant-bras droit": ("RightForeArm",)}
    sommets = {}
    for nom, os_noms in parties.items():
        groupes = {g.index for g in ob.vertex_groups if any((":" + n) in g.name for n in os_noms)}
        sommets[nom] = [v.index for v in ob.data.vertices if any(e.group in groupes for e in v.groups)]

    def fermee(o):   # une pièce dont chaque arête borde deux faces : seule une pièce fermée a un dedans
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(o.data)
        ok = all(len(e.link_faces) == 2 for e in bm.edges)
        bm.free()
        return ok
    pieces = [o for o in rendu.racine.children_recursive if o.type == "MESH"] + rendu.en_main
    ouvertes = [o.name for o in pieces if not fermee(o)]
    pieces = [o for o in pieces if o.name not in ouvertes]
    print("CONTACT pièces ouvertes, non mesurées : " + ", ".join(ouvertes))
    pires, enfonce, depart = {}, {}, {}
    a_detailler = set()
    for f in (0.0,) + tuple(details):
        image = round(rc.t_clip(f) * rc.IPS) + 1
        a_detailler.add(image - (image - 1) % pas)
    vl = scene.view_layers[0]
    for image in range(1, rc.IMAGES + 2, pas):
        scene.frame_set(image)
        rendu.poser(image)
        vl.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        M = ob.matrix_world
        f = rendu.geste.mesures[image - 1]["f"]
        if not 0.0 <= f <= 1.0:
            ev.to_mesh_clear()
            continue
        for o in pieces:
            if o.hide_render:
                continue
            arbre = BVHTree.FromObject(o, dg)
            Mi = o.matrix_world.inverted()
            for nom, indices in sommets.items():
                for i in indices:
                    p = Mi @ (M @ me.vertices[i].co)
                    loc, nor, _, dist = arbre.find_nearest(p, 0.03)
                    if loc is not None and (p - loc).dot(nor) < 0.0 and dedans(arbre, p):
                        cle = (nom, o.name)
                        if dist > pires.get(cle, (0.0, 0.0))[0]:
                            pires[cle] = (dist, f)
                        if nom != "main droite" and dist > 0.004:
                            enfonce.setdefault((nom, o.name), []).append(round(f, 3))
                        if image in a_detailler and dist > 0.002:   # quel os, de combien
                            os_ = max(me.vertices[i].groups, key=lambda e: e.weight).group
                            g = (image, ob.vertex_groups[os_].name, o.name)
                            depart[g] = max(depart.get(g, 0.0), dist)
        ev.to_mesh_clear()
    for (nom, piece), (prof, f) in sorted(pires.items(), key=lambda x: -x[1][0]):
        print(f"CONTACT {nom:<17} dans {piece:<28} : {prof * 1000:5.1f} mm au plus (f={f:.3f})")
    if not pires:
        print("CONTACT rien n'entre dans l'arme")
    for (image, os_, piece), prof in sorted(depart.items(), key=lambda x: (x[0][0], -x[1])):
        f = rendu.geste.mesures[image - 1]["f"]
        print(f"CONTACT détail f={f:.3f} : {os_} dans {piece}, {prof * 1000:.1f} mm")
    for (nom, piece), fs in sorted(enfonce.items()):
        fs = sorted(set(fs))
        print(f"CONTACT {nom} dans {piece} de plus de 4 mm, f = " + ", ".join(f"{x:.3f}" for x in fs))


def controler_douille(scene, rendu, ob):
    """La douille vide chassée de la chambre : où elle passe par rapport au robot, de sa sortie à sa chute au sol (au
    plus près de la peau du robot, et de sa tête), pour qu'elle ne lui vole pas au visage."""
    from mathutils.bvhtree import BVHTree
    vl = scene.view_layers[0]
    tete = {g.index for g in ob.vertex_groups if g.name.replace("mixamorig:", "") in ("Head", "HeadTop_End", "Neck")}
    sommets_tete = [v.index for v in ob.data.vertices if v.groups and max(v.groups, key=lambda e: e.weight).group in tete]
    pire, pire_tete = (1.0, 0.0, ""), (1.0, 0.0)
    for image in range(1, rc.IMAGES + 2):
        m = rendu.geste.mesures[image - 1]
        if G["pose"](m["f"])["chute"] is None or not 0.0 <= m["f"] <= 1.0:
            continue
        scene.frame_set(image)
        rendu.poser(image)
        if rendu.vide.hide_render:
            break
        vl.update()
        dg = bpy.context.evaluated_depsgraph_get()
        arbre = BVHTree.FromObject(ob, dg)
        c = rendu.vide.matrix_world @ rendu.centre_douille()
        loc, _, i, _ = arbre.find_nearest(ob.matrix_world.inverted() @ c)
        dist = (ob.matrix_world @ loc - c).length if loc is not None else 1.0   # (le maillage peut être à l'échelle)
        if dist < pire[0]:
            v = ob.data.vertices[ob.data.polygons[i].vertices[0]]
            groupe = ob.vertex_groups[max(v.groups, key=lambda e: e.weight).group].name if v.groups else ""
            pire = (dist, m["f"], groupe.replace("mixamorig:", ""))
        me = ob.evaluated_get(dg).to_mesh()
        d_tete = min((ob.matrix_world @ me.vertices[k].co - c).length for k in sommets_tete)
        ob.evaluated_get(dg).to_mesh_clear()
        if d_tete < pire_tete[0]:
            pire_tete = (d_tete, m["f"])
    print(f"CONTACT douille vide : à {pire[0] * 1000:.0f} mm du robot au plus près (f={pire[1]:.3f}, {pire[2]}), "
          f"à {pire_tete[0] * 1000:.0f} mm de sa tête (f={pire_tete[1]:.3f})")


def rendre_images(scene, rendu, masque, cams, dossier, etapes, vues, largeur=640, hauteur=480, echantillons=24):
    os.makedirs(dossier, exist_ok=True)
    chemins = {}
    for vue in vues:
        masque.show_render = vue == "fps"
        for f in etapes:
            image = round(rc.t_clip(f) * rc.IPS) + 1
            scene.frame_set(image)
            rendu.poser(image)
            cam = cams.placer(vue, rendu.geste.mesures[image - 1])
            chemin = os.path.join(dossier, f"{vue}_{int(round(f * 1000)):04d}.png")
            cr.rendre(scene, cam, chemin, largeur, hauteur, echantillons)
            chemins[(vue, f)] = chemin
    masque.show_render = False
    return chemins


def principal():
    args = arguments()
    scene = bpy.data.scenes.new("Rechargement du Big D")
    if bpy.context.window_manager.windows:
        bpy.context.window_manager.windows[0].scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    # le calcul lit bpy.context.scene (les imports, l'image du clip de Mixamo) : en arrière-plan, sans fenêtre, ce
    # serait la scène du Big D, que golden_gun.py vient de construire
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        calculer_et_rendre(args, scene)
    print("FIN")


def calculer_et_rendre(args, scene):
    rb, ob, col = cr.construire(rc.robot_ct01.MODELE, scene, "orange")
    source = cr.importer_squelette(scene, "Source")
    action_source = cr.charger_attente(rc.SOURCE)
    cr.poser(source, action_source, 1)
    scene.render.fps = rc.IPS   # après les imports : l'import d'un FBX met la scène à la cadence du fichier
    sq = rc.Squelette(rb.arm)
    geste = GesteBigD(sq, source, action_source)
    action = geste.calculer(rb.arm)
    controler(geste)
    if args.diag:
        diagnostiquer(geste)
    if args.fbx:
        dossier = args.fbx if os.path.isabs(args.fbx) else os.path.join(cr.PROJET, args.fbx)
        rc.exporter(rb.arm, action, os.path.join(dossier, f"Y Bot@{B.clip}.fbx"))
        fiche(geste, os.path.join(dossier, f"Y Bot@{B.clip}.json"))
    if args.rendus or args.sequence or args.contacts:
        cr.poser(rb.arm, action, 1)
        studio = preparer_studio(scene, rb, col)
        scene.frame_set(round(rc.t_clip(0.0) * rc.IPS) + 1)
        cams = Cameras(studio, rb)
        rendu = Rendu(scene, geste)
        masque = masque_bras(ob)
        if args.contacts:
            controler_contacts(scene, rendu, ob, details=[float(x) for x in args.details.split(",") if x])
            controler_douille(scene, rendu, ob)
        if args.diag:
            # la cartouche par rapport aux bouts du pouce et de l'index gauches, telle que les rendus la montrent
            for f in (0.30, 0.40, 0.49, 0.53, 0.57):
                image = round(rc.t_clip(f) * rc.IPS) + 1
                scene.frame_set(image)
                rendu.poser(image)
                scene.view_layers[0].update()
                m = geste.mesures[image - 1]
                tenue = [o for o in rendu.en_main + rendu.neuve if not o.hide_render]
                for o in tenue[:1]:
                    culot = o.matrix_world @ Vector(u(*CULOT))
                    print(f"DIAG cartouche f={f:.2f} ({o.name}) : culot à {(culot - m['p_main']).length * 1000:.0f} mm "
                          "de l'os de la main gauche")
        if args.rendus:
            rendre_images(scene, rendu, masque, cams, os.path.abspath(args.rendus),
                          [float(x) for x in args.etapes.split(",")], args.vues.split(","))
        if args.sequence:
            n = args.images
            etapes = [i / (n - 1) for i in range(n)]
            rendre_images(scene, rendu, masque, cams, os.path.abspath(args.sequence), etapes,
                          args.vues_sequence.split(","), 720, 540, 16)


if __name__ == "__main__":
    principal()
