using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// L'habillage de l'arène (2026-10-27, ArenaDecorBuilder) ne touche pas au jeu : le blocking garde ses colliders, rien
/// d'habillé ne dépasse du bloc qu'il habille (une balle passerait à travers ce qui dépasse : bible § 13, « aucune surface
/// ne laisse passer les balles »), le décor n'ajoute aucun collider dans l'arène (seuls le sol, les murs et le plafond
/// de la salle en ont), et les props de Blender n'en ont aucun.
///
/// Ce qui casse ici ne lève aucune erreur : une caisse plus grosse que son bloc, un mur habillé plus haut que le vrai,
/// une poutre qui arrêterait les balles ou les cailloux. Vu échouer à son premier passage, sur un vrai défaut : la
/// barricade de 1,25 m flotte de 2,5 cm, et son habillage descendait jusqu'au sol.
/// </summary>
public class ArenaDecorTests
{
    private const string ScenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
    private static readonly string[] AllowedColliders = { "Sol", "Murs", "Plafond" };

    [Test]
    public void LHabillage_TientDansLeBlocking_EtNAjouteAucunCollider()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject decor = roots.SingleOrDefault(r => r.name == ArenaDecorBuilder.DecorRootName);
            Assert.IsNotNull(decor, "L'arène est habillée (Duel Arena > Arène > Habiller l'arène).");
            GameObject lighting = roots.SingleOrDefault(r => r.name == ArenaLighting.RootName);
            Transform tribune = roots.SelectMany(r => r.GetComponentsInChildren<Tribune>(true)).Single().transform;

            // Le blocking : chaque bloc garde son collider, sur le layer du décor que les balles arrêtent.
            var blocks = new List<Bounds>();
            foreach (MeshCollider c in roots.SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)))
            {
                if (c.transform.IsChildOf(tribune) || c.transform.IsChildOf(decor.transform)) continue;
                if (!c.GetComponents<Component>().Any(k => k != null && k.GetType().Name == "ProBuilderMesh")) continue;
                Assert.IsTrue(c.enabled && c.gameObject.activeInHierarchy, $"Le bloc {c.name} garde son collider.");
                Assert.AreEqual(0, c.gameObject.layer, $"Le bloc {c.name} reste sur le layer Default.");
                blocks.Add(c.bounds);
            }
            Assert.Greater(blocks.Count, 90, "Les blocs du blocking ont été trouvés.");

            // Ce qui habille un bloc tient dans sa boîte (à 5 mm près), au-dessus du sol : ce qui s'y enfonce ne se voit pas.
            Transform dressed = decor.transform.Find("Blocking habillé");
            Assert.IsNotNull(dressed, "Le blocking habillé.");
            int checkedRenderers = 0;
            foreach (Renderer r in dressed.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                if (b.min.y < 0f) b.SetMinMax(new Vector3(b.min.x, 0f, b.min.z), b.max);
                Assert.IsTrue(blocks.Any(k => Contains(k, b, 0.005f)), $"{r.name} ({b.min} → {b.max}) dépasse de tous les blocs du blocking.");
                checkedRenderers++;
            }
            Assert.Greater(checkedRenderers, 90, "Les blocs habillés ont été trouvés.");

            // Le décor n'ajoute de collider qu'au sol, aux murs et au plafond de la salle.
            foreach (GameObject root in new[] { decor, lighting }.Where(g => g != null))
            {
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    Assert.Contains(c.name, AllowedColliders, $"{c.name} : le décor n'ajoute pas de collider dans l'arène.");
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Les faisceaux des projecteurs (2026-10-27) : un effet lumineux ne masque pas un adversaire (bible § 17).
    /// Ils s'éteignent au-dessus de la tête d'un robot debout sur la passerelle, ne portent pas d'ombre, n'arrêtent rien et
    /// n'entrent pas dans la lumière cuite (la cuisson les prendrait pour des écrans). Vu échouer, faisceaux descendus de
    /// 3 m, puis passer.</summary>
    [Test]
    public void LesFaisceaux_SEteignentAuDessusDesRobots_EtNeCachentRien()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject lighting = roots.SingleOrDefault(r => r.name == ArenaLighting.RootName);
            Assert.IsNotNull(lighting, "L'arène est éclairée (Duel Arena > Arène > Habiller l'arène).");
            Transform beams = lighting.transform.Find("Rampes de projecteurs/Faisceaux");
            Assert.IsNotNull(beams, "Les faisceaux des projecteurs.");
            Mesh mesh = beams.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 0, "Il y a des faisceaux.");

            Tribune tribune = roots.SelectMany(r => r.GetComponentsInChildren<Tribune>(true)).Single();
            float spectatorHead = tribune.FloorTop + 1.85f;   // un robot debout (1,805 m) sur la passerelle
            float lowest = mesh.vertices.Min(v => beams.TransformPoint(v).y);
            Assert.GreaterOrEqual(lowest, spectatorHead, "Un faisceau descend jusqu'à la tête d'un robot.");

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

    [Test]
    public void LesPropsDuDecor_NOntAucunCollider()
    {
        string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { DecorImporter.Folder }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        Assert.Greater(prefabs.Length, 0, "Les props du décor sont importés (Duel Arena > Arène > Importer les props de Blender).");
        foreach (string path in prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), $"{path} n'a pas de collider : un prop ne change jamais le jeu.");
        }
    }

    private static bool Contains(Bounds outer, Bounds inner, float tolerance)
    {
        Vector3 a = outer.min - Vector3.one * tolerance, b = outer.max + Vector3.one * tolerance;
        return inner.min.x >= a.x && inner.min.y >= a.y && inner.min.z >= a.z
            && inner.max.x <= b.x && inner.max.y <= b.y && inner.max.z <= b.z;
    }
}
