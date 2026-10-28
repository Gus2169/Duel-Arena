using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// L'habillage du stand de tir et de la Fosse (2026-10-28, StandDecorBuilder) ne touche pas au jeu, comme celui de l'arène
/// (ArenaDecorTests) : les blocs du constructeur gardent leurs colliders, rien d'habillé ne dépasse du bloc qu'il habille
/// (une balle passerait à travers ce qui dépasse : bible § 13), et le décor n'ajoute de collider qu'aux déflecteurs du
/// stand, au-dessus du pas de tir (une balle tirée vers le haut s'y arrête). Les faisceaux des projecteurs s'arrêtent
/// au-dessus d'un robot debout sur le toit des bureaux de la Fosse.
///
/// Ce qui casse ici ne lève aucune erreur : une étagère plus grande que son bloc, une gaine habillée plus bas que la
/// vraie (on croirait devoir s'accroupir plus bas), un projecteur qui arrêterait les balles, un faisceau devant un robot.
/// </summary>
public class StandDecorTests
{
    /// <summary>Le dessus du toit des bureaux de la Fosse (m), où l'on se tient au plus haut.</summary>
    private const float RoofTop = 3f;

    [Test]
    public void LHabillageDuStand_TientDansSesBlocs_EtNAjouteQueLesDeflecteurs()
    {
        Scene scene = SceneManager.GetSceneByPath(ShootingRangeBuilder.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ShootingRangeBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject decor = roots.SingleOrDefault(r => r.name == ArenaDecorBuilder.DecorRootName);
            Assert.IsNotNull(decor, "Le stand est habillé (Duel Arena > Stand de tir > Construire le stand).");
            GameObject lighting = roots.SingleOrDefault(r => r.name == ArenaLighting.RootName);

            // Les blocs du jeu : leur collider, sur le layer du décor que les balles arrêtent.
            var blocks = new List<Bounds>();
            foreach (BoxCollider c in roots.Where(r => r.name is "Stand" or "La Fosse (décor)" or "Salle de précision (décor)").SelectMany(r => r.GetComponentsInChildren<BoxCollider>(true)))
            {
                Assert.IsTrue(c.enabled && c.gameObject.activeInHierarchy, $"Le bloc {c.name} garde son collider.");
                Assert.AreEqual(0, c.gameObject.layer, $"Le bloc {c.name} reste sur le layer Default.");
                blocks.Add(c.bounds);
            }
            Assert.Greater(blocks.Count, 150, "Les blocs du stand et de la Fosse ont été trouvés.");

            // Ce qui habille un bloc tient dans sa boîte (à 5 mm près), au-dessus du sol : ce qui s'y enfonce ne se voit pas.
            Transform dressed = decor.transform.Find("Blocs habillés");
            Assert.IsNotNull(dressed, "Les blocs habillés.");
            int checkedRenderers = 0;
            foreach (Renderer r in dressed.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                if (b.min.y < 0f) b.SetMinMax(new Vector3(b.min.x, 0f, b.min.z), b.max);
                Assert.IsTrue(blocks.Any(k => Contains(k, b, 0.005f)), $"{r.name} ({b.min} → {b.max}) dépasse de tous les blocs du stand.");
                checkedRenderers++;
            }
            Assert.Greater(checkedRenderers, 150, "Les blocs habillés ont été trouvés.");

            // Le décor n'ajoute de collider qu'aux déflecteurs du stand.
            Transform baffles = decor.transform.Find("Salle/Déflecteurs");
            Assert.IsNotNull(baffles, "Les déflecteurs du pas de tir.");
            foreach (GameObject root in new[] { decor, lighting }.Where(g => g != null))
            {
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    Assert.IsTrue(c.transform.IsChildOf(baffles), $"{c.name} ({c.transform.parent?.name}) : le décor n'ajoute pas de collider au stand.");
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Les faisceaux des projecteurs du stand et de la Fosse (bible § 17 : un effet lumineux ne masque pas un
    /// adversaire) s'éteignent au-dessus de la tête d'un robot debout sur le toit des bureaux, ne portent pas d'ombre,
    /// n'arrêtent rien et n'entrent pas dans la lumière cuite.</summary>
    [Test]
    public void LesFaisceauxDuStand_SEteignentAuDessusDUnRobotSurLeToit()
    {
        Scene scene = SceneManager.GetSceneByPath(ShootingRangeBuilder.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ShootingRangeBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject lighting = scene.GetRootGameObjects().SingleOrDefault(r => r.name == ArenaLighting.RootName);
            Assert.IsNotNull(lighting, "Le stand est éclairé (Duel Arena > Stand de tir > Construire le stand).");
            Transform beams = lighting.transform.Find("Rampes de projecteurs/Faisceaux");
            Assert.IsNotNull(beams, "Les faisceaux des projecteurs.");
            Mesh mesh = beams.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 0, "Il y a des faisceaux.");

            float head = RoofTop + 1.85f;   // un robot debout (1,805 m) sur le toit des bureaux
            float lowest = mesh.vertices.Min(v => beams.TransformPoint(v).y);
            Assert.GreaterOrEqual(lowest, head, "Un faisceau descend jusqu'à la tête d'un robot.");

            var renderer = beams.GetComponent<MeshRenderer>();
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode, "Un faisceau ne porte pas d'ombre.");
            Assert.IsFalse(GameObjectUtility.AreStaticEditorFlagsSet(beams.gameObject, StaticEditorFlags.ContributeGI),
                "Un faisceau n'entre pas dans la lumière cuite.");
            Assert.IsEmpty(beams.GetComponentsInChildren<Collider>(true), "Un faisceau n'arrête rien.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool Contains(Bounds outer, Bounds inner, float tolerance)
    {
        Vector3 a = outer.min - Vector3.one * tolerance, b = outer.max + Vector3.one * tolerance;
        return inner.min.x >= a.x && inner.min.y >= a.y && inner.min.z >= a.z
            && inner.max.x <= b.x && inner.max.y <= b.y && inner.max.z <= b.z;
    }
}
