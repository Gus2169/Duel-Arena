"""Le rechargement du Big D, l'arme du Golden Gun (2026-11-03), calculé sur le squelette du Y Bot pour Unity.

Le Big D est un derringer à un coup (Tools/Blender/golden_gun.py) : on ne change pas de chargeur, on ouvre l'arme. Le
geste suit les étapes de l'arme (golden_gun.ETAPES, en part du rechargement) : le levier s'abaisse, la main gauche
soulève le canon par-dessous et le lance en arrière (il bascule jusqu'à l'horizontale, et la douille vide tombe de la
chambre tournée vers le bas), va prendre une cartouche neuve couchée dans sa boucle, sur la hanche gauche, la monte
au-dessus de la chambre (l'arme présentée devant la poitrine, la chambre vers le haut) et l'y pousse de la paume, le
coude levé, puis prend le canon par son flanc gauche, près de la bouche, et le ramène jusqu'à ce qu'il se verrouille ;
le chien s'arme, et la main gauche revient tenir la main droite. Le pouce droit ne bouge pas : le levier et le chien
bougent seuls.

Le reste vient du rechargement à chargeur (rechargement_chargeur.py) : le haut du corps et la main droite du clip de
Mixamo (Y Bot@Reloading), recalés sur ces étapes ; l'arme tenue par la main droite et présentée à la main gauche ; la
main gauche menée par la cinématique inverse du jeu le long d'un trajet tracé dans le repère de l'arme, du canon (qui
tourne), de la cartouche ou de la hanche ; la forme du clip (3,3 s, la partie utile de 0,4 à 2,5 s) ; l'export.

blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/animations/rechargement_golden_gun.py -- \
    [--fbx <dossier>] [--rendus <dossier> --etapes 0,0.1,... --vues buste,face_droite,droite,fps]
    [--sequence <dossier> --vues-sequence buste,face_droite,fps --images 50] [--contacts --details 0.5,0.7] [--diag]

Contrôles : --contacts mesure, image par image, ce que les mains et les avant-bras enfoncent dans les pièces fermées
de l'arme et dans la cartouche tenue (pendant le geste, la main gauche au plus 6 mm : le bas du pouce sur le canon
qu'elle ramène ; au départ et au retour, la tenue des pistolets, « Pistol NoAim Idle », met le pouce gauche dans la
charnière du Big D, plus gros que le PA-05) ; --diag dit où sont l'arme, sa chambre et la cartouche par rapport à la
paume.
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
ICI = os.path.dirname(os.path.abspath(__file__))
if ICI not in sys.path:
    sys.path.insert(0, ICI)
import rechargement_chargeur as rc  # noqa: E402

cr, kit, Cle, u = rc.cr, rc.kit, rc.Cle, rc.u


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

# Les repères que la main gauche touche (point de la paume, normale de la paume, direction des doigts), dans le repère
# de l'arme d'Unity, canon fermé ; ceux du canon tournent avec lui.
# La main gauche qui tient la droite, comme au PA-05 : la prise de « Pistol NoAim Idle », relevée dans Unity
GARDE = ((-0.103, 0.038, 0.008), (0.872, -0.196, -0.448), (0.394, -0.263, 0.881))
# sous le canon, près de la bouche (passé le pontet) : la paume à plat contre son dessous, les doigts en travers
SOUS = ((0.0, (G["PIV"][1] + G["D_BAS"]) * E, 0.190), (0.0, 1.0, 0.0), (1.0, 0.0, 0.1))
# son flanc gauche, vers la bouche (devant la carcasse) : la paume contre lui, les doigts qui passent sur son dessous
# (ouvert, il regarde le haut : la main le soulève et le ramène ; fermé, ils s'arrondissent sous lui, devant le nez)
# (la paume à 2 cm du flanc : le bas du pouce du CT-01 dépasse d'elle, et entrait dans le canon)
FLANC = ((-G["DEMI"] * E - 0.022, 0.066, 0.170), (1.0, 0.0, 0.0), (0.0, -1.0, 0.2))
# La cartouche tenue par son culot, que la paume pousse : les doigts et le pouce pincent son bourrelet, les doigts
# vers le haut de l'arme ouverte (jamais vers la carcasse), le pouce vers sa gauche. De la boucle à la chambre, la main
# ne tourne pas sur le poignet : l'avant-bras se lève, la paume passe de l'avant à l'arrière, les doigts du bas au
# haut. Dans le repère de la cartouche : son culot à l'origine, z vers la balle, y le haut de l'arme quand elle est
# dans la chambre canon fermé (le bas, canon ouvert). Enfoncée, la main reste devant la chambre.
PINCE = ((0.0, 0.0, -0.015), (0.0, 0.0, 1.0), (0.0, -1.0, 0.0))


def decaler(tenue, dx=0.0, dy=0.0, dz=0.0):
    (p, n, d) = tenue
    return ((p[0] + dx, p[1] + dy, p[2] + dz), n, d)


def ouvert(p, theta, point=True):
    """Un point (ou une direction) du canon, canon ouvert de theta degrés : il tourne autour de l'axe de la charnière,
    la bouche vers le haut puis vers l'arrière."""
    a = math.radians(theta)
    c, s = math.cos(a), math.sin(a)
    if not point:
        return (p[0], p[1] * c + p[2] * s, -p[1] * s + p[2] * c)
    y, z = p[1] - AXE[1], p[2] - AXE[2]
    return (p[0], AXE[1] + y * c + z * s, AXE[2] - y * s + z * c)


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


class CleBigD(Cle):
    """Une étape du trajet de la paume. Ses repères en plus de ceux du rechargement à chargeur : « canon » (le canon à
    l'instant considéré), « cartouche » (la cartouche alignée sur la chambre, sortie de `d` mètres, ou de ce que dit
    golden_gun.pose)."""

    def __init__(self, f, ou, repere="arme", pole=None, doigts="ouvert", arret=True, d=None):
        super().__init__(f, ou, repere, pole, doigts, arret)
        self.d = d


# L'arme présentée pour charger : (roulis à droite °, tangage vers le haut °, cap vers la gauche °, vers la gauche,
# l'avant et le haut du robot m)
PRESENTATION_CHARGE = (15.0, 25.0, 30.0, 0.12, 0.06, -0.06)
# Le pôle du coude gauche autour de l'arme (monde de Blender : +X la gauche du robot, -Y l'avant, +Z le haut) : en
# bas et en dehors, pour que l'avant-bras monte vers l'arme au lieu de passer devant elle
COUDE_BIGD = Vector((0.6, 0.0, -1.0))
# quand la paume pousse la cartouche dans la chambre, par le haut : le coude levé et en dehors, le poignet droit
COUDE_HAUT = Vector((0.8, 0.4, 0.6))


class BigD:
    """Ce que le calcul du rechargement à chargeur demande à une arme (voir rechargement_chargeur.Arme)."""

    code = "BIG-D"
    clip = "Rechargement BIG-D"
    collection = None
    milieu = (0.0, 0.0, 0.0)
    tenue = PINCE
    garde_main = GARDE
    # les étapes du geste qui ne sont pas celles de l'arme
    saisie = 0.30        # la cartouche prise à la hanche
    retour = 0.94        # la main gauche revenue tenir la droite
    lancer = 0.075       # après le début de l'ouverture, le canon passe la verticale : la main le lâche, il finit seul

    def __init__(self):
        e = G["ETAPES"]
        self.etapes_arme = e
        t = rc.t_clip
        # Le haut du corps et la main droite du clip de Mixamo, recalés (voir rechargement_chargeur.Arme.recalage) :
        # dans l'ancien, la main gauche est à la hanche à 1,1 s, insère à 1,62 s, est sur le dessus de la carcasse à
        # 2,1 s et revenue à 2,5 s.
        self.recalage = [(0.0, 0.0), (t(0.0), 0.40), (t(self.saisie), 1.10), (t(e["insertion"][0] + 0.04), 1.62),
                         (t(e["fermeture"][1] - 0.04), 2.10), (t(self.retour), 2.50), (rc.IMAGES / rc.IPS, 3.30)]
        # L'arme dans la main droite, rattachée au bassin (le buste du clip de Mixamo la portait jusqu'au menton) :
        # (f, roulis à droite °, tangage vers le haut °, cap vers la gauche °, autour de sa poignée ; puis déplacée vers
        # la gauche, l'avant et le haut du robot, m). Un peu levée pendant que le canon s'ouvre, puis présentée à la
        # main gauche devant la poitrine, la chambre tournée vers le haut et vers elle, le temps de charger ; redressée
        # pour refermer.
        o0, o1 = e["ouverture"]
        i0, i1 = e["insertion"]
        f0, f1 = e["fermeture"]
        rien = (0.0,) * 6
        charge = PRESENTATION_CHARGE
        self.presentation = [(0.0,) + rien, (o0,) + rien, (o1, 5.0, 10.0, 5.0, 0.02, 0.0, 0.0),
                             (self.saisie + 0.04,) + charge, (i1,) + charge,
                             (f0 + 0.02,) + tuple(c * 0.4 for c in charge), (f1,) + rien, (1.0,) + rien]

    def presente(self, f):
        for a, b in zip(self.presentation, self.presentation[1:]):
            if f <= b[0]:
                s = rc.lisse((f - a[0]) / max(1e-6, b[0] - a[0]))
                return tuple(x + (y - x) * s for x, y in zip(a[1:], b[1:]))
        return self.presentation[-1][1:]

    def trajet(self):
        e = self.etapes_arme
        o0 = e["ouverture"][0]
        lache = o0 + self.lancer
        i0, i1 = e["insertion"]
        f0, f1 = e["fermeture"]
        poche, c, h = rc.COUDE_POCHE, COUDE_BIGD, COUDE_HAUT
        return [
            CleBigD(0.0, GARDE, doigts="garde"),
            CleBigD(o0 - 0.03, decaler(SOUS, dx=-0.03, dy=-0.05), repere="canon", pole=c, doigts="ouvert",
                    arret=False),
            CleBigD(o0, SOUS, repere="canon", pole=c, doigts="plat"),
            CleBigD(lache, SOUS, repere="canon", pole=c, doigts="plat"),
            CleBigD(lache + 0.03, decaler(SOUS, dx=-0.04, dy=-0.07), repere="canon", pole=c, doigts="ouvert",
                    arret=False),
            CleBigD(self.saisie - 0.08, "dessus_poche", repere="poche", pole=poche, doigts="ouvert", arret=False),
            CleBigD(self.saisie - 0.03, "poche", repere="poche", pole=poche, doigts="ouvert"),
            CleBigD(self.saisie, "poche", repere="poche", pole=poche, doigts="pincer"),
            CleBigD(self.saisie + 0.04, "sortie_poche", repere="poche", pole=poche, doigts="pincer", arret=False),
            # elle arrive par la gauche, un peu au-delà de la chambre, la cartouche déjà dans l'axe
            CleBigD(i0 - 0.06, decaler(PINCE, dx=-0.10, dy=0.03), repere="cartouche", d=0.13, pole=h,
                    doigts="pincer", arret=False),
            CleBigD(i0, PINCE, repere="cartouche", pole=h, doigts="pincer"),
            CleBigD(i1, PINCE, repere="cartouche", pole=h, doigts="pincer"),
            CleBigD(i1 + 0.02, decaler(PINCE, dx=-0.03, dz=-0.03), repere="cartouche", pole=h, doigts="ouvert",
                    arret=False),
            CleBigD(f0 - 0.01, FLANC, repere="canon", pole=c, doigts="pincer"),
            CleBigD(f1, FLANC, repere="canon", pole=c, doigts="pincer"),
            CleBigD(f1 + 0.05, decaler(FLANC, dx=-0.05, dy=-0.03, dz=-0.06), repere="canon", pole=c, doigts="ouvert",
                    arret=False),
            CleBigD(self.retour, GARDE, doigts="garde"),
            CleBigD(1.0, GARDE, doigts="garde"),
        ]

    def etapes(self):
        e = {nom: list(v) for nom, v in self.etapes_arme.items()}
        e.update(saisie=self.saisie, retour=self.retour, lacher=e["ouverture"][0] + self.lancer)
        return e


B = BigD()
rc.A = B


class GesteBigD(rc.Geste):
    """Le calcul du rechargement à chargeur, avec les repères du canon, de la cartouche et de la hanche du Big D."""

    def image(self, t):
        self.f = min(max((t - rc.DEBUT_UTILE) / (rc.FIN_UTILE - rc.DEBUT_UTILE), 0.0), 1.0)
        return super().image(t)

    def preparer_doigts(self):
        super().preparer_doigts()
        # pincer : les doigts fermés comme pour tenir, le pouce de la main en crochet (sur le levier d'un fusil), le
        # moins enfoncé dans le canon des poses essayées (fermé : 18 mm ; tendu : 9 ; en crochet : 6)
        pouce = {d: q for d, q in self.doigts["crochet"].items() if d.startswith("LeftHandThumb")}
        self.doigts["pincer"] = {**self.doigts["tenir"], **pouce}

    def calculer(self, arm):
        # la pose de départ de l'arme (celle du clip de Mixamo quand l'état Recharge y entre), dans le repère du bassin
        bases0 = self.bases_source(self.recaler(rc.t_clip(0.0)))
        poses0 = {}
        for nom in self.sq.ordre:
            poses0[nom] = self.sq.enfant(nom, poses0, bases0[nom])
        R_a, o_a = self.repere_arme(poses0)
        bassin = poses0[rc.os_("Hips")]
        R_h, p_h = self.sq.rotation(bassin), self.sq.position(bassin)
        self.depart_bassin = (R_h.transposed() @ R_a, R_h.transposed() @ (o_a - p_h))
        return super().calculer(arm)

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

    def resoudre(self, cle, poses):
        if cle.repere == "poche":
            return self.resoudre_poche(cle, poses)
        R_arme, o_arme = self.repere_arme(poses)
        pose = G["pose"](self.f)
        theta = pose["canon"]
        p, n, d = cle.ou
        if cle.repere == "canon":
            p, n, d = ouvert(p, theta), ouvert(n, theta, False), ouvert(d, theta, False)
        elif cle.repere == "cartouche":
            # (avant l'insertion, golden_gun.pose ne la place pas : elle attend à sa distance d'approche)
            approche = pose["cartouche"] if pose["cartouche"] is not None else G["APPROCHE_CARTOUCHE"]
            sortie = cle.d if cle.d is not None else approche * E
            rep = repere_cartouche(theta, sortie)
            p, n, d = dans(rep, p), dans(rep, n, False), dans(rep, d, False)
        point = o_arme + R_arme @ u(*p)
        R_main = rc.repere_main(R_arme @ u(*n), R_arme @ u(*d))
        pole = cle.pole if isinstance(cle.pole, Vector) else R_arme @ u(*(cle.pole or rc.COUDE_ARME))
        return point, R_main, pole

    def resoudre_poche(self, cle, poses):
        """La cartouche neuve couchée dans sa boucle, sur la hanche gauche, la balle vers l'avant : la main la prend par
        son culot, du dehors (la paume vers le corps, les doigts vers le bas), puis la monte vers la chambre sans tourner
        le poignet (les doigts passent du bas au haut avec l'avant-bras)."""
        bassin = self.sq.position(poses[rc.os_("Hips")])
        culot = bassin + rc.POCHE + {"poche": Vector((0.0, 0.05, 0.0)), "dessus_poche": Vector((0.05, 0.05, 0.08)),
                                     "sortie_poche": Vector((0.02, 0.03, 0.12))}[cle.ou]
        z, y = Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))    # la balle vers l'avant, son « haut » en haut
        x = z.cross(y)                                               # les axes de l'arme d'Unity, vus de Blender
        p, n, d = PINCE
        point = culot + x * p[0] + y * p[1] + z * p[2]
        R_main = rc.repere_main(x * n[0] + y * n[1] + z * n[2], x * d[0] + y * d[1] + z * d[2])
        return point, R_main, cle.pole


def arguments():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", default=None, help="dossier où écrire le FBX et sa fiche")
    p.add_argument("--rendus", default=None)
    p.add_argument("--etapes", default="0,0.05,0.08,0.12,0.155,0.2,0.27,0.3,0.37,0.44,0.5,0.56,0.62,0.67,0.75,0.82,"
                                       "0.88,0.94,1")
    p.add_argument("--vues", default="buste,face_droite,droite,fps")
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
    """Le poignet atteint chaque étape ; aux instants qui comptent, la paume est où elle doit être."""
    pires = sorted(geste.mesures, key=lambda m: -m["ecart"])[:3]
    print("CONTROLE BIG-D portée : écart du poignet à sa cible au pire "
          + ", ".join(f"{m['ecart'] * 1000:.1f} mm (f={m['f']:.2f})" for m in pires))
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
    sq = geste.sq
    for f in (0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.56, 0.62, 0.7, 0.8, 0.9, 1.0):
        _, poses, _, _ = geste.image(rc.t_clip(f))
        R, o = geste.repere_arme(poses)
        tete = sq.position(poses[rc.os_("Head")])
        bassin = sq.position(poses[rc.os_("Hips")])
        chambre = R @ u(*ouvert((0.0, 0.0, -1.0), G["pose"](f)["canon"], False))
        print(f"DIAG f={f:.2f} arme-tête {r(o - tete)} arme-bassin {r(o - bassin)} canon {r(R @ u(0, 0, 1), 2)} "
              f"chambre {r(chambre, 2)}")


def fiche(geste, chemin):
    """Ce que le jeu lira du geste (Duel Arena > Animations > Importer les rechargements à chargeur, BreakActionGesture) :
    ses étapes (celles de l'arme et les siennes), ce qui bouge sur l'arme (golden_gun.pose : les angles du canon, du
    chien et du levier, les glissements de la douille et de la cartouche, en mètres d'Unity), les repères que la main
    touche, et des poses relevées tout au long du rechargement, que les tests rejouent (-1 : rien à cet instant)."""
    def repere(r):
        return {"paume": list(r[0]), "normale": list(r[1]), "doigts": list(r[2])}

    def releve(f):
        p = G["pose"](f)
        return {"f": f, "canon": p["canon"], "chien": p["chien"], "levier": p["levier"],
                "douille": p["douille"] * E if p["douille"] is not None else -1.0,
                "chute": p["chute"] if p["chute"] is not None else -1.0,
                "cartouche": p["cartouche"] * E if p["cartouche"] is not None else -1.0}
    donnees = {
        "clip": B.clip, "arme": B.code, "ips": rc.IPS, "duree": rc.IMAGES / rc.IPS,
        "utile": [rc.DEBUT_UTILE, rc.FIN_UTILE], "etapes": B.etapes(), "paumeConception": list(rc.PAUME_UNITY),
        "garde": repere(GARDE), "sous": repere(SOUS), "flanc": repere(FLANC), "pince": repere(PINCE),
        "axe": list(AXE), "culot": list(CULOT), "echelle": E,
        "angleCanon": G["OUVERTURE_CANON"], "angleChien": G["ARMEMENT_CHIEN"], "angleLevier": G["ABAISSEMENT_LEVIER"],
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
    droite, ses pièces suivent golden_gun.pose, la douille vide tombe de la chambre, la cartouche neuve suit la main
    gauche de la hanche à la chambre."""

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
        # la douille vide : dans la chambre, qu'elle quitte, puis qui tombe
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
               "fps": 24}

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
    dehors). La main droite tient la poignée : elle y entre un peu, comme au PA-05."""
    from mathutils.bvhtree import BVHTree
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
                    if loc is not None and (p - loc).dot(nor) < 0.0:
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
        if args.diag:
            # la cartouche par rapport à la paume, telle que les rendus la montrent
            for f in (0.40, 0.47, 0.50, 0.56, 0.62):
                image = round(rc.t_clip(f) * rc.IPS) + 1
                scene.frame_set(image)
                rendu.poser(image)
                scene.view_layers[0].update()
                m = geste.mesures[image - 1]
                paume = m["p_main"] + m["R_main"] @ rc.PAUME
                normale = m["R_main"] @ Vector((0.0, 0.0, 1.0))
                tenue = [o for o in rendu.en_main + rendu.neuve if not o.hide_render]
                for o in tenue[:1]:
                    culot = o.matrix_world @ Vector(u(*CULOT))
                    pointe = o.matrix_world @ Vector(u(CULOT[0], CULOT[1], CULOT[2] + 0.10))
                    print(f"DIAG cartouche f={f:.2f} ({o.name}) : culot à {(culot - paume).length * 1000:.0f} mm de la "
                          f"paume, devant elle de {(culot - paume).dot(normale) * 1000:.0f} mm ; pointe devant de "
                          f"{(pointe - paume).dot(normale) * 1000:.0f} mm")
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
