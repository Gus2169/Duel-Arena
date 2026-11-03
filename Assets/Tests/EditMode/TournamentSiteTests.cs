using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Le site du Tournoi dans l'arène de test (2026-11-03, TournamentSiteBuilder ; GDD § 11, TO5 et TO9) : les copies de
/// l'arène, loin de tout (rien ne s'entend ni ne se voit de l'une à l'autre), qui ont tous les colliders de l'arène et rien
/// de ce qui fait le jeu ; la salle d'attente, où un robot tient à chaque point d'apparition, tourné vers les écrans, qu'il
/// voit, et d'où l'on tire sur les mannequins par-dessus le comptoir ; et ce que le jeu en attend : le préfab des
/// couloirs, inscrit dans les objets réseau, et le tableau du tournoi sur l'interface.
/// </summary>
public class TournamentSiteTests
{
    private const string ScenePath = TournamentSiteBuilder.ScenePath;
    private const int WorldMask = 1;

    private static Scene Open(out bool opened)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        return scene;
    }

    private static TournamentSite SiteOf(Scene scene, out GameObject root)
    {
        root = scene.GetRootGameObjects().SingleOrDefault(r => r.name == TournamentSiteBuilder.RootName);
        Assert.IsNotNull(root, "Le site du tournoi est posé (Duel Arena > Arène > Poser le site du tournoi).");
        var site = root.GetComponent<TournamentSite>();
        Assert.IsNotNull(site);
        Assert.IsNotNull(root.GetComponent<TournamentFeeds>(), "Le mur d'écrans a son direct.");
        return site;
    }

    /// <summary>Trois copies de l'arène (trois matchs à la fois, à six), la première celle de la scène, et la salle
    /// d'attente : à plus de 1,5 km les unes des autres (les tirs portent à 500 m, la vue à 1 km). Chaque copie a autant de
    /// colliders que l'arène, aux mêmes places décalées, et rien d'autre que du décor : ni objet réseau, ni script, ni la
    /// rampe générale (une seule lumière directionnelle éclaire tout le monde). Vu échouer, une copie posée à 800 m.</summary>
    [Test]
    public void LesCopiesDeLArene_SontLoin_EtOntTousSesColliders_SansRienDuJeu()
    {
        Scene scene = Open(out bool opened);
        try
        {
            TournamentSite site = SiteOf(scene, out GameObject root);
            Assert.AreEqual(Tournament.MaxArenas, site.ArenaCount, "Trois matchs à la fois, à six joueurs.");
            Assert.AreEqual(Vector3.zero, site.ArenaOffset(0), "La première copie est l'arène de la scène.");
            var places = new List<Vector3>();
            for (int i = 0; i < site.ArenaCount; i++) places.Add(site.ArenaOffset(i) + new Vector3(0f, 0f, 30f));
            places.Add(site.WaitingRoom.center);
            for (int i = 0; i < places.Count; i++)
            for (int j = i + 1; j < places.Count; j++)
                Assert.Greater(Vector3.Distance(places[i], places[j]), 1500f, $"Les lieux {i} et {j} : trop près, on s'y entendrait ou s'y verrait.");
            for (int i = 0; i < site.ArenaCount; i++)
            {
                Assert.AreEqual(i, site.ArenaAt(site.ArenaOffset(i) + new Vector3(10f, 0f, 40f)), $"Un point de l'arène {i + 1}.");
            }
            Assert.AreEqual(-1, site.ArenaAt(site.WaitingRoom.center), "La salle d'attente n'est dans aucune arène.");

            // Les colliders de l'arène (hors de ce qui n'est pas la salle), et ceux de chaque copie.
            var originals = new List<Collider>();
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r == root || !r.activeSelf) continue;
                if (new[] { "SpawnPoints", "RoundManager", "GameUI", "VictorySpot", ZoneLayoutBuilder.GroupName, "Main Camera", "Terrain" }.Contains(r.name)) continue;
                originals.AddRange(r.GetComponentsInChildren<Collider>(true));
            }
            Assert.Greater(originals.Count, 150, "Les colliders de l'arène ont été trouvés.");
            for (int i = 1; i < site.ArenaCount; i++)
            {
                Transform copy = root.transform.Find($"Arène {i + 1}");
                Assert.IsNotNull(copy, $"La copie {i + 1}.");
                Collider[] colliders = copy.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(originals.Count, colliders.Length, $"L'arène {i + 1} a tous les colliders de l'arène.");
                Vector3 offset = site.ArenaOffset(i);
                var expected = new HashSet<string>(originals.Select(c => Key(c, offset)));
                foreach (Collider c in colliders)
                    Assert.IsTrue(expected.Contains(Key(c, Vector3.zero)), $"{c.name} ({c.transform.position}) : à la place d'un collider de l'arène.");
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                {
                    Assert.IsFalse(c is MonoBehaviour && c.GetType().Name != "UniversalAdditionalLightData",
                                   $"{c.name} : une copie n'a pas de script ({c.GetType().Name}).");
                    if (c is Light light) Assert.AreNotEqual(LightType.Directional, light.type, "La rampe générale n'est pas copiée.");
                }
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Un collider : sa sorte et sa place (au centimètre), décalée de `offset`.</summary>
    private static string Key(Collider c, Vector3 offset)
    {
        Vector3 p = c.transform.position + offset;
        return $"{c.GetType().Name} {Mathf.RoundToInt(p.x * 100f)} {Mathf.RoundToInt(p.y * 100f)} {Mathf.RoundToInt(p.z * 100f)}";
    }

    /// <summary>La salle d'attente : un robot tient debout à chaque point d'apparition, sur le sol, dans la salle, tourné
    /// vers les écrans, qu'il voit tous ; et de là, on tire sur chaque mannequin par-dessus le comptoir. Vu échouer, le
    /// comptoir monté à 1,8 m.</summary>
    [Test]
    public void LaSalleDAttente_OnYTient_OnVoitLesEcrans_EtOnTireSurLesMannequins()
    {
        Scene scene = Open(out bool opened);
        try
        {
            TournamentSite site = SiteOf(scene, out GameObject root);
            Physics.SyncTransforms();
            Bounds room = site.WaitingRoom;
            IReadOnlyList<Transform> spawns = site.WaitingSpawns;
            Assert.GreaterOrEqual(spawns.Count, Tournament.MaxPlayers, "Une place pour chacun.");
            Assert.AreEqual(Tournament.MaxArenas, site.Screens.Count, "Un écran par copie de l'arène.");
            foreach (Transform spawn in spawns)
            {
                Vector3 p = spawn.position;
                Assert.IsTrue(room.Contains(p + Vector3.up), $"{spawn.name} ({p}) : dans la salle.");
                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.8f, WorldMask, QueryTriggerInteraction.Ignore)
                              && Mathf.Abs(ground.point.y - p.y) < 0.05f, $"{spawn.name} : sur le sol.");
                Assert.IsFalse(Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.35f, 0.5f, WorldMask, QueryTriggerInteraction.Ignore),
                               $"{spawn.name} : un robot y tient.");
                Vector3 eye = p + Vector3.up * 1.65f;
                foreach (Renderer screen in site.Screens)
                {
                    Vector3 center = screen.bounds.center;
                    Assert.Greater(Vector3.Dot(spawn.forward, (center - eye).normalized), 0.25f, $"{spawn.name} : tourné vers {screen.name}.");
                    Vector3 toward = (center - eye).normalized;
                    Assert.IsFalse(Physics.Linecast(eye, center - toward * 0.1f, WorldMask, QueryTriggerInteraction.Ignore),
                                   $"{spawn.name} voit {screen.name}.");
                    Assert.Greater(Vector3.Dot(screen.transform.forward, toward), 0.2f,
                                   $"{screen.name} se voit de face : le quadrilatère d'Unity se montre à qui regarde vers son +Z.");
                }
            }

            TrainingDummy[] dummies = root.GetComponentsInChildren<TrainingDummy>(true);
            Assert.GreaterOrEqual(dummies.Length, 4, "Des mannequins sur le pas de tir.");
            Vector3 lounge = spawns[0].position + Vector3.up * 1.65f;
            foreach (TrainingDummy dummy in dummies)
            {
                Vector3 chest = dummy.transform.position + Vector3.up * 1.2f;
                Assert.IsTrue(room.Contains(chest), $"{dummy.name} : dans la salle.");
                Assert.IsFalse(Physics.Linecast(lounge, chest, WorldMask, QueryTriggerInteraction.Ignore),
                               $"{dummy.name} : on le touche du coin d'attente, par-dessus le comptoir.");
                Assert.Greater(Vector3.Dot(dummy.transform.forward, (lounge - chest).normalized), 0.5f, $"{dummy.name} : tourné vers la salle.");
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Ce que le jeu attend du site : le RoundManager de la scène a le préfab de ses couloirs (un RoundManager coché
    /// « couloir », avec son objet réseau, inscrit dans les préfabs réseau), et l'interface le tableau du tournoi. Vu
    /// échouer, le préfab retiré du RoundManager.</summary>
    [Test]
    public void LeRoundManager_ASesCouloirs_EtLInterface_SonTableau()
    {
        Scene scene = Open(out bool opened);
        try
        {
            RoundManager main = scene.GetRootGameObjects().Select(r => r.GetComponent<RoundManager>()).Single(rm => rm != null);
            var so = new SerializedObject(main);
            Assert.IsFalse(so.FindProperty("lanePrefab").boolValue, "Celui de la scène n'est pas un couloir.");
            var lane = so.FindProperty("lanePrefabAsset").objectReferenceValue as RoundManager;
            Assert.IsNotNull(lane, "Le préfab de ses couloirs (MatchTournoi).");
            Assert.AreEqual(TournamentSiteBuilder.LanePrefabPath, AssetDatabase.GetAssetPath(lane));
            Assert.IsTrue(new SerializedObject(lane).FindProperty("lanePrefab").boolValue, "Le préfab est un couloir.");
            Assert.IsNotNull(lane.GetComponent<Unity.Netcode.NetworkObject>(), "Un couloir apparaît sur le réseau.");
            var prefabs = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            Assert.IsTrue(prefabs.PrefabList.Any(p => p.Prefab == lane.gameObject), "Inscrit dans les préfabs réseau.");

            GameObject ui = scene.GetRootGameObjects().Single(r => r.name == "GameUI");
            var board = ui.GetComponent<TournamentBoard>();
            Assert.IsNotNull(board, "Le tableau du tournoi.");
            Assert.AreEqual(ui.GetComponent<UnityEngine.UIElements.UIDocument>(), new SerializedObject(board).FindProperty("document").objectReferenceValue);
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
