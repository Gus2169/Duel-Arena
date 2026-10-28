"""Les marquages peints de la salle de test (2026-10-27) : des textes au pochoir (Saira Stencil One, SIL OFL) et des
flèches, un objet par marquage, que l'habillage d'Unity pose au sol ou sur les murs (ArenaMarkings) et peint de sa
couleur (blanc cassé, noir, rouge). L'usure de la peinture est celle du shader DuelArena/Decor (peinture découpée
par plaques), pas de la géométrie.

MQ1 : chaque marquage a sa hauteur de capitale à 1 m, centré sur l'origine, dans le plan XZ, lisible de -Y (dans
Unity : dans le plan XY, lisible de +Z) ; Unity le met à l'échelle. Les phrases sont des propositions de Claude,
dans l'humour sec de la bible (§ 14 : anciennes consignes, numéros de test).

    blender -b --factory-startup --python-exit-code 1 -P marquages.py -- --rendus <dossier>
"""

import os
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import commun_decor as cd  # noqa: E402

kit = cd.kit
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

# Le nom de l'objet dans Unity, et son texte.
TEXTES = [
    ("B01", "B-01"),
    ("Salle", "SALLE DE TEST BALISTIQUE"),
    ("PosteA", "POSTE A"),
    ("PosteB", "POSTE B"),
    ("TirEnCours", "TIR EN COURS"),
    ("ZoneDeTir", "ZONE DE TIR"),
    ("Tunnel", "TUNNEL 02"),
    ("Humains", "ACCÈS RÉSERVÉ AU PERSONNEL HUMAIN"),
    ("Essai", "ESSAI N° 4471"),
    ("Casque", "CASQUE ANTIBRUIT OBLIGATOIRE"),
    ("Cible", "CIBLE"),
    # Le stand de tir et la Fosse (2026-10-28) : la salle B-02, ses postes et ses vieilles consignes ; la Fosse, salle
    # B-03, ses consignes de parcours et le nom de ses pièces.
    ("B02", "B-02"),
    ("StandDeTir", "STAND DE TIR"),
    ("LigneDeTir", "LIGNE DE TIR"),
    ("PareBalles", "PARE-BALLES"),
    ("Riposte", "LES MANNEQUINS NE RIPOSTENT PAS"),
    ("Douilles", "RAMASSEZ VOS DOUILLES"),
    ("Condamne", "ACCÈS CONDAMNÉ"),
    ("LaFosse", "LA FOSSE"),
    ("B03", "B-03"),
    ("Entrainement", "ZONE D'ENTRAÎNEMENT RAPPROCHÉ"),
    ("Depart", "DÉPART"),
    ("Arrivee", "ARRIVÉE"),
    ("Franchir", "FRANCHIR"),
    ("Accroupi", "ACCROUPI"),
    ("Tete", "ATTENTION À LA TÊTE"),
    ("Civils", "CIVILS : NE PAS TIRER"),
    ("Quai", "QUAI 3"),
    ("Atelier", "ATELIER"),
    ("Reserve", "RÉSERVE"),
    ("Bureaux", "BUREAUX"),
    # La salle de précision (2026-10-28) : la salle B-04, sa consigne, le bouton qui efface la cible.
    ("B04", "B-04"),
    ("SallePrecision", "SALLE DE PRÉCISION"),
    ("Precision", "PRÉCISION"),
    ("Consigne", "TOUT IMPACT EST CONSIGNÉ"),
    ("Effacer", "EFFACER"),
    # Les distances, chaque mètre de 5 à 60 : les lignes du stand, les mannequins, les boutons du pupitre.
] + [(f"M{d}", f"{d} M") for d in range(5, 61)] + [(f"N{n}", f"{n}") for n in range(1, 10)]


def hauteur_boite(contenu, police, scene):
    """La hauteur (m) de la boîte d'un texte de taille 1 : kit.texte met cette boîte à la hauteur demandée."""
    cu = bpy.data.curves.new("tmp_mesure", "FONT")
    cu.body = contenu
    cu.font = police
    cu.size = 1.0
    ob = bpy.data.objects.new("tmp_mesure", cu)
    scene.collection.objects.link(ob)
    dg = scene.view_layers[0].depsgraph
    dg.update()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    ys = [v.co.y for v in me.vertices]
    h = max(ys) - min(ys)
    ev.to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    return h


def marquages(scene):
    at = cd.AtelierDecor(scene, "MQ1", "Marquages peints", cd.materiaux())
    at.sans_occlusion = True
    pochoir = cd.police(cd.POLICE_POCHOIR)
    # kit.texte met la boîte du texte à la hauteur demandée : un accent (DÉPART) ou une jambe (le Q de QUAI) la
    # grandissent, et le texte rapetissait. Ici, chaque texte a la hauteur de capitale de son « H » à 1 m (2026-10-28).
    capitale = hauteur_boite("H", pochoir, at.scene)
    for nom, texte in TEXTES:
        pc = at.piece(nom, "MarquageBlanc")
        hauteur = hauteur_boite(texte, pochoir, at.scene) / capitale
        kit.texte(pc, texte, pochoir, hauteur, Vector((0, 0, 0)), X, Z, mat="MarquageBlanc", decalage=0.0, scene=at.scene)
        at.objet(pc, angle=None)
    # une flèche, la pointe vers +X (la droite du lecteur), 1 m de haut
    pc = at.piece("Fleche", "MarquageBlanc")
    contour = [(-1.0, 0.18), (0.2, 0.18), (0.2, 0.5), (1.0, 0.0), (0.2, -0.5), (0.2, -0.18), (-1.0, -0.18)]
    bm = pc.bm
    f = bm.faces.new([bm.verts.new(Vector((x, 0.0, z))) for x, z in contour])
    f.normal_update()
    if f.normal.dot(-Y) < 0:
        f.normal_flip()
    f.material_index = pc.mat("MarquageBlanc")
    at.objet(pc, angle=None)
    return at


PROPS = [("MQ1", marquages)]


def construire(code):
    for c, f in PROPS:
        if c == code:
            return f(cd.nouvelle_scene(f"Decor_{c}"))
    raise KeyError(code)


if __name__ == "__main__":
    a = cd.arguments()
    if a.rendus:
        at = construire("MQ1")
        for i, ob in enumerate(o for o in at.objets if o.type == "MESH"):
            ob.location = Vector((0, 0, -i * 1.4))
        cd.rendre_vues(at, a.rendus, (0, 0, -9), 18.0, ("face",))
        print("RENDU MQ1", sum(kit.triangles(o) for o in at.objets if o.type == "MESH"), "triangles")
