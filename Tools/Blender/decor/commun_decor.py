"""Ce que partagent les props du décor des arènes de Duel Arena (Blender 5.2, 2026-10-27).

Un prop est un script qui le reconstruit en entier, comme une arme : on corrige le script, jamais le .blend.
Il se construit avec le kit des armes (kit_lowpoly : volumes, chanfreins qui portent l'usure, rafistolage,
décalques) et ses matériaux à usure procédurale (kit.materiau) : Unity refait la même usure (shader
DuelArena/Decor, espace « pièce de Blender », qui reprend celle des armes).

Repère d'un prop (celui du monde de Blender) : X à droite, Y vers l'arrière du prop, Z en haut ; en mètres ;
l'origine là où il se pose (le milieu de sa base au sol, son point d'accroche s'il pend ou se fixe au mur, la
face du mur à Y = 0, le prop vers -Y). L'export (exporter_decor.py) le passe dans les axes d'Unity comme les
armes : Unity = (-x, z, -y). Un prop posé contre un mur y regarde donc vers +Z.

Bible § 10 : décor sombre et terne (noir, gris acier, béton, blanc cassé, presque pas de jaune), jamais les
couleurs des robots ; le rouge est celui de l'énergie et du danger (les voyants).
"""

import argparse
import importlib
import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
DOSSIER = os.path.dirname(os.path.abspath(__file__))
PARENT = os.path.dirname(DOSSIER)
for d in (DOSSIER, PARENT):
    if d not in sys.path:
        sys.path.insert(0, d)
import kit_lowpoly as kit  # noqa: E402

importlib.reload(kit)
from kit_lowpoly import srgb  # noqa: E402

POLICE = r"C:\Windows\Fonts\bahnschrift.ttf"
# La police des pochoirs : Saira Stencil One (SIL Open Font License, Google Fonts), dans Tools/Blender/polices.
POLICE_POCHOIR = os.path.join(PARENT, "polices", "SairaStencilOne-Regular.ttf")

ANTHRACITE = srgb(41, 43, 46)
NOIR = srgb(16, 16, 18)
BLANC_CASSE = srgb(198, 196, 184)
ACIER = srgb(140, 142, 146)
ROUGE_VOYANT = srgb(255, 28, 18)
BLANC_LAMPE = srgb(214, 228, 255)


def materiaux(fabrique=None):
    """Les matériaux des props, par clé. `fabrique(nom, base, **kw)` remplace kit.materiau : l'export y lit les
    paramètres (exporter_armes.parametres) au lieu de construire des nœuds."""
    f = fabrique or kit.materiau
    return {
        "Peinture": f("DA_Decor_Peinture", ANTHRACITE, rug=0.6, dessous=ACIER, bords=0.55, eclats=0.35, rayures=0.3,
                      salete=0.6, variation=0.06, echelle_eclats=18.0, poussiere=0.35, graine=11),
        "PeintureClaire": f("DA_Decor_PeintureClaire", BLANC_CASSE, rug=0.62, dessous=ACIER, bords=0.6, eclats=0.25,
                            rayures=0.35, salete=0.7, variation=0.08, echelle_eclats=16.0, poussiere=0.4,
                            couche=(srgb(84, 58, 48), 0.75), graine=12),
        "Acier": f("DA_Decor_Acier", ACIER, rug=0.42, metal=0.6, salete=0.6, variation=0.05, poussiere=0.3, graine=13),
        "Noir": f("DA_Decor_Noir", NOIR, rug=0.5, dessous=ACIER, bords=0.35, eclats=0.1, salete=0.4, variation=0.04,
                  echelle_eclats=20.0, graine=14),
        "Caoutchouc": f("DA_Decor_Caoutchouc", srgb(24, 24, 25), rug=0.9, salete=0.3, variation=0.03, graine=15),
        "MarquageBlanc": f("DA_Decor_MarquageBlanc", BLANC_CASSE, rug=0.7, salete=0.2, variation=0.05, graine=16),
        "MarquageNoir": f("DA_Decor_MarquageNoir", NOIR, rug=0.7, salete=0.1, variation=0.03, graine=17),
        "Lampe": f("DA_Decor_Lampe", srgb(230, 236, 245), rug=0.25, salete=0.0, variation=0.0,
                   emission=BLANC_LAMPE, force_emission=12.0, graine=18),
        "Voyant": f("DA_Decor_Voyant", srgb(120, 14, 10), rug=0.3, salete=0.0, variation=0.0,
                    emission=ROUGE_VOYANT, force_emission=4.0, graine=19),
        "Verre": f("DA_Decor_Verre", srgb(22, 26, 30), rug=0.08, metal=0.0, salete=0.2, variation=0.02, coat=1.0, graine=20),
    }


class AtelierDecor:
    """Un prop : sa scène, sa collection, sa racine à l'origine, ses pièces. Les noms sont préfixés par le code
    (CA1_Caisse...) ; l'export retire le préfixe."""

    def __init__(self, scene, code, titre, M):
        self.scene = scene
        self.code = code
        self.prefixe = code.replace("-", "")
        self.titre = titre
        self.classe = -1          # pas une arme (exporter_armes.nom_final le lit)
        self.M = M
        self.col = kit.collection(self.prefixe, scene.collection)
        self.racine = bpy.data.objects.new(self.prefixe, None)
        self.col.objects.link(self.racine)
        self.objets = [self.racine]

    def nom(self, n):
        return f"{self.prefixe}_{n}"

    def piece(self, nom, mat=None):
        return kit.Piece(self.nom(nom), mat)

    def objet(self, pc, angle=35.0):
        ob = kit.vers_objet(pc, self.col, self.M, parent=self.racine, angle_lisse=angle)
        self.objets.append(ob)
        return ob

    def vide(self, nom, position):
        ob = bpy.data.objects.new(self.nom(nom), None)
        ob.empty_display_size = 0.05
        self.col.objects.link(ob)
        ob.parent = self.racine
        ob.location = position
        self.objets.append(ob)
        return ob


def nouvelle_scene(nom):
    """Une scène à soi pour le prop (jamais celle de l'utilisateur), vidée si elle existe."""
    scene = bpy.data.scenes.get(nom) or bpy.data.scenes.new(nom)
    kit.vider_collection(scene.collection)
    scene.render.engine = "BLENDER_EEVEE"
    scene.unit_settings.system = "METRIC"
    return scene


def police(chemin=POLICE):
    for f in bpy.data.fonts:
        if f.filepath and os.path.normcase(bpy.path.abspath(f.filepath)) == os.path.normcase(chemin):
            return f
    return bpy.data.fonts.load(chemin)


# --- les vues de contrôle -----------------------------------------------------------------------------

def studio(scene, centre, taille):
    """Trois lampes de surface et un fond sombre autour d'un prop de `taille` m."""
    col = kit.collection("Studio", scene.collection)
    c = Vector(centre)
    d = taille * 1.8
    kit.lumiere("Cle", col, c + Vector((d, -d, d * 1.1)), c, 60.0 * taille * taille, taille, (1.0, 0.97, 0.92))
    kit.lumiere("Remplissage", col, c + Vector((-d * 1.2, -d * 0.6, d * 0.5)), c, 20.0 * taille * taille, taille * 1.5, (0.85, 0.9, 1.0))
    kit.lumiere("Contre", col, c + Vector((0.0, d * 1.3, d)), c, 35.0 * taille * taille, taille)
    monde = scene.world or bpy.data.worlds.new("Monde_Decor")
    scene.world = monde
    monde.color = (0.03, 0.032, 0.036)
    scene.view_settings.view_transform = "AgX"
    return col


def rendre_vues(at, dossier, centre, taille, vues=("trois_quarts", "face", "dessus")):
    """Rend le prop sous quelques angles dans `dossier` (le scratchpad)."""
    os.makedirs(dossier, exist_ok=True)
    scene = at.scene
    col = studio(scene, centre, taille)
    c = Vector(centre)
    d = taille * 2.2
    positions = {
        "trois_quarts": c + Vector((d * 0.8, -d, d * 0.55)),
        "face": c + Vector((0.0, -d * 1.2, d * 0.15)),
        "dessus": c + Vector((d * 0.3, -d * 0.4, d * 1.3)),
        "dessous": c + Vector((d * 0.7, -d * 0.9, -d * 0.6)),
        "profil": c + Vector((d * 1.2, 0.0, d * 0.15)),
    }
    for v in vues:
        cam = kit.camera(f"Cam_{v}", col, positions[v], c, focale=50.0)
        cam.data.clip_end = taille * 20
        kit.rendre(scene, cam, os.path.join(dossier, f"{at.prefixe}_{v}.png"), 960, 720, echantillons=32)


def arguments(argv=None):
    if argv is not None:
        return argv
    brut = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--rendus", default="")
    p.add_argument("--sortie", default="")
    p.add_argument("--props", default="", help="les codes à rendre, séparés par des virgules (tous par défaut)")
    return p.parse_args(brut)
