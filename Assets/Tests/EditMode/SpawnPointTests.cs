using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests de la sélection de point d'apparition. Le comportement qui compte : deux joueurs ne
/// doivent jamais apparaître l'un sur l'autre, y compris quand la boucle de manches les replace
/// après être morts au même endroit.
/// </summary>
public class SpawnPointTests
{
    private GameObject root;
    private PlayerSpawnPoints spawnPoints;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("SpawnPointsTest");
        spawnPoints = root.AddComponent<PlayerSpawnPoints>();
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    private void Configure(params Vector3[] positions)
    {
        var transforms = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var point = new GameObject($"Spawn{i}");
            point.transform.SetParent(root.transform);
            point.transform.position = positions[i];
            transforms[i] = point.transform;
        }

        // Le champ est sérialisé et privé : la réflexion évite de l'ouvrir en public juste pour
        // les tests. Le code de production reste inchangé.
        typeof(PlayerSpawnPoints)
            .GetField("spawnPoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(spawnPoints, transforms);
    }

    [Test]
    public void AucunPointConfigure_RenvoieFalse()
    {
        // Le joueur garde alors la position du prefab — comportement voulu, pas une erreur.
        Assert.IsFalse(spawnPoints.TryGetSpawnPointFarthestFrom(new List<Vector3>(), out _, out _));
    }

    [Test]
    public void AucunOccupant_ChoisitLePremierPoint_DoncDeterministe()
    {
        // Déterminisme volontaire : un tirage aléatoire rendrait un bug de spawn difficile à
        // reproduire.
        Configure(new Vector3(0, 0, 2), new Vector3(0, 0, 58));

        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(new List<Vector3>(), out var a, out _));
        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(new List<Vector3>(), out var b, out _));

        Assert.AreEqual(a, b);
        Assert.AreEqual(new Vector3(0, 0, 2), a);
    }

    [Test]
    public void UnOccupant_ChoisitLePointLePlusEloigne()
    {
        Configure(new Vector3(0, 0, 2), new Vector3(0, 0, 58));

        var occupied = new List<Vector3> { new Vector3(0, 0, 2) };

        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(occupied, out var chosen, out _));
        Assert.AreEqual(new Vector3(0, 0, 58), chosen,
            "Le nouveau venu doit apparaître à l'opposé de l'adversaire déjà présent.");
    }

    [Test]
    public void DeuxJoueursPlacesSuccessivement_FinissentAuxDeuxExtremites()
    {
        // Le cas réel de la boucle BO5 : les deux sont morts au même endroit, on les replace l'un
        // après l'autre. Placer le premier doit influencer le choix du second.
        Configure(new Vector3(0, 0, 2), new Vector3(0, 0, 58));

        var mortsAuMemeEndroit = new List<Vector3> { new Vector3(0, 0, 30), new Vector3(0, 0, 30) };

        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(mortsAuMemeEndroit, out var premier, out _));

        var occupied = new List<Vector3> { premier };
        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(occupied, out var second, out _));

        Assert.AreNotEqual(premier, second, "Les deux joueurs ne doivent jamais partager un point de spawn.");
    }

    [Test]
    public void RotationDuPoint_DonneLaDirectionDuRegard()
    {
        Configure(new Vector3(0, 0, 2));
        root.transform.GetChild(0).rotation = Quaternion.Euler(0f, 180f, 0f);

        Assert.IsTrue(spawnPoints.TryGetSpawnPointFarthestFrom(new List<Vector3>(), out _, out float yaw));
        Assert.AreEqual(180f, yaw, 0.01f);
    }

    [Test]
    public void ListeOccupantsNull_NeLevePas()
    {
        Configure(new Vector3(0, 0, 2));
        Assert.DoesNotThrow(() => spawnPoints.TryGetSpawnPointFarthestFrom(null, out _, out _));
    }

    /// <summary>En manches, les postes A et B, et l'on change de côté à chaque manche (GDD § 5, T3) : le premier
    /// duelliste commence au poste A, le second au B, puis ils échangent ; en BO5, chacun joue les deux côtés. Les postes
    /// sont les points d'apparition, dans leur ordre. Vu échouer, chacun gardant son poste tout le match.</summary>
    [Test]
    public void EnManches_LesPostesAEtB_EtOnChangeDeCoteAChaqueManche()
    {
        Assert.AreEqual(0, RoundManager.SidePost(0, 1), "Manche 1 : le premier au poste A.");
        Assert.AreEqual(1, RoundManager.SidePost(1, 1), "Manche 1 : le second au poste B.");
        Assert.AreEqual(1, RoundManager.SidePost(0, 2), "Manche 2 : on change de côté.");
        Assert.AreEqual(0, RoundManager.SidePost(1, 2));
        var sides = new HashSet<int>();
        for (int round = 1; round <= 5; round++)
        {
            Assert.AreNotEqual(RoundManager.SidePost(0, round), RoundManager.SidePost(1, round), $"Manche {round} : jamais au même poste.");
            sides.Add(RoundManager.SidePost(0, round));
        }
        Assert.AreEqual(2, sides.Count, "En BO5, chacun joue les deux côtés.");

        Configure(new Vector3(0, 0, 2), new Vector3(0, 0, 58));
        root.transform.GetChild(1).rotation = Quaternion.Euler(0f, 180f, 0f);
        Assert.IsTrue(spawnPoints.TryGetPost(0, out Vector3 a, out float yawA));
        Assert.IsTrue(spawnPoints.TryGetPost(1, out Vector3 b, out float yawB));
        Assert.AreEqual(new Vector3(0, 0, 2), a, "Le poste A : le premier point.");
        Assert.AreEqual(new Vector3(0, 0, 58), b, "Le poste B : le second.");
        Assert.AreEqual(0f, yawA, 0.01f);
        Assert.AreEqual(180f, yawB, 0.01f);
        Assert.IsFalse(spawnPoints.TryGetPost(2, out _, out _), "Pas de troisième poste.");
        Assert.IsFalse(spawnPoints.TryGetPost(-1, out _, out _), "-1 : pas de poste, le point le plus éloigné.");
    }
}
