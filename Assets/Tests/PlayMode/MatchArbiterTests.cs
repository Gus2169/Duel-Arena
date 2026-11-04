using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que l'arbitre de la partie fait sans session : le RoundManager de la scène s'inscrit lui-même, jamais un couloir du
/// Tournoi ; les statistiques restent au serveur jusqu'à la fin du match ; le site du Tournoi s'éteint au lancement, sauf
/// ses objets réseau de scène. Des [Test] synchrones : aucune image ne passe, rien d'autre que ce qu'on appelle ne tourne.
/// « NetworkVariable is written to, but doesn't know its NetworkBehaviour yet » : attendu hors session.
/// </summary>
public class MatchArbiterTests
{
    private const string LanePrefabPath = "Assets/_ProjectArena/Modes/MatchTournoi.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<PlayerMatchState> states = new List<PlayerMatchState>();

    [TearDown]
    public void TearDown()
    {
        foreach (PlayerMatchState state in states) state.OnNetworkDespawn();
        states.Clear();
        foreach (GameObject go in created)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        created.Clear();
    }

    private GameObject Track(GameObject go)
    {
        created.Add(go);
        return go;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private RoundManager NewLane()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LanePrefabPath);
        Assert.IsNotNull(prefab, "Le préfab des couloirs du Tournoi.");
        return Track(Object.Instantiate(prefab)).GetComponent<RoundManager>();
    }

    /// <summary>Une fiche de joueur présente (PlayerMatchState.Spawned), telle que le serveur la voit.</summary>
    private PlayerMatchState NewPlayer()
    {
        PlayerMatchState state = Track(new GameObject("Fiche")).AddComponent<PlayerMatchState>();
        state.OnNetworkSpawn();
        states.Add(state);
        SetTrue(state, nameof(NetworkBehaviour.IsServer));
        SetTrue(state, nameof(NetworkBehaviour.IsSpawned));
        return state;
    }

    private static void SetTrue(NetworkBehaviour behaviour, string property)
        => typeof(NetworkBehaviour).GetProperty(property).GetSetMethod(true).Invoke(behaviour, new object[] { true });

    private static void SetPhase(RoundManager round, RoundManager.Phase phase)
        => typeof(RoundManager).GetMethod("SetPhase", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(round, new object[] { phase, 1f });

    /// <summary>Instance rend le RoundManager de la scène, inscrit à son réveil, jamais un couloir du Tournoi ; sans lui (le
    /// stand), rien.</summary>
    [Test]
    public void Instance_EstLeRoundManagerDeLaScene_JamaisUnCouloir()
    {
        Assert.IsTrue(RoundManager.Instance == null, "Garde : pas de RoundManager dans la scène des tests.");
        NewLane();
        Assert.IsTrue(RoundManager.Instance == null, "Un couloir du Tournoi n'est pas celui de la scène.");
        RoundManager main = Track(new GameObject("RoundManager")).AddComponent<RoundManager>();
        Assert.AreSame(main, RoundManager.Instance);
        Object.DestroyImmediate(main.gameObject);
        Assert.IsTrue(RoundManager.Instance == null, "Détruit avec sa scène, il ne répond plus.");
    }

    /// <summary>Le compte du match reste au serveur : rien ne part à chaque tir. Une fois publié, il est entier (les tirs,
    /// les coups de gant, la manche la plus rapide) ; remis à zéro, il repart de rien.</summary>
    [Test]
    public void LesStatistiques_RestentAuServeur_JusquALaFinDuMatch()
    {
        PlayerMatchState state = NewPlayer();
        state.ServerRecordShot(true, true, 40f);
        state.ServerRecordDamage(60f);
        state.ServerRecordRoundWin(31.5f);
        Assert.AreEqual(0, state.Stats.shotsFired, "Pendant le match, rien n'est publié.");

        state.ServerPublishStats();
        PlayerMatchState.MatchStats published = state.Stats;
        Assert.AreEqual(1, published.shotsFired);
        Assert.AreEqual(1, published.shotsHit);
        Assert.AreEqual(1, published.headshots);
        Assert.AreEqual(100f, published.damageDealt, 1e-4f, "Le tir et le coup de gant.");
        Assert.AreEqual(31.5f, published.fastestRoundWin, 1e-4f);

        state.ServerRecordShot(false, false, 0f);
        Assert.AreEqual(1, state.Stats.shotsFired, "Un tir de plus n'est pas encore publié.");
        state.ServerResetStats();
        Assert.AreEqual(0, state.Stats.shotsFired, "Un nouveau match : le compte publié repart de zéro.");
        state.ServerRecordShot(false, false, 0f);
        state.ServerPublishStats();
        Assert.AreEqual(1, state.Stats.shotsFired, "Et le compte du serveur aussi.");
        Assert.AreEqual(0f, state.Stats.damageDealt, 1e-4f);
    }

    /// <summary>Toute fin de match passe par SetPhase : l'écran de fin, qui vient après, trouve le compte de chacun. Une fin
    /// de manche ne publie rien.</summary>
    [Test]
    public void LaFinDuMatch_PublieLesStatistiquesDeChacun()
    {
        RoundManager lane = NewLane();
        PlayerMatchState shooter = NewPlayer(), other = NewPlayer();
        shooter.ServerRecordShot(true, false, 25f);
        other.ServerRecordShot(false, false, 0f);

        SetPhase(lane, RoundManager.Phase.RoundOver);
        Assert.AreEqual(0, shooter.Stats.shotsFired, "Une fin de manche : rien ne part.");

        SetPhase(lane, RoundManager.Phase.MatchOver);
        Assert.AreEqual(1, shooter.Stats.shotsHit, "La fin du match les publie.");
        Assert.AreEqual(1, other.Stats.shotsFired, "Ceux de chacun.");
    }

    /// <summary>Le site du Tournoi s'éteint à son réveil, d'un bloc : ses copies, sa salle. Pas ses objets réseau de scène ni
    /// leurs parents : le serveur ne fait apparaître que ceux qui sont actifs. Il se rallume comme il était.</summary>
    [Test]
    public void LeSite_SEteintAuLancement_SaufSesObjetsReseau_PuisSeRallume()
    {
        GameObject root = Track(new GameObject("Site"));
        GameObject copy = Child(root, "Arène 2");
        GameObject copyWall = Child(copy, "Mur");
        GameObject room = Child(root, "Salle d'attente");
        GameObject floor = Child(room, "Sol");
        GameObject group = Child(room, "Mannequins");
        GameObject dummy = Child(group, "Mannequin");
        dummy.AddComponent<NetworkObject>();
        GameObject hitbox = Child(dummy, "Hitbox");
        GameObject asleep = Child(room, "Éteint dans la scène");
        asleep.SetActive(false);

        var site = root.AddComponent<TournamentSite>();
        Assert.IsTrue(root.activeSelf, "La racine reste allumée : on la cherche, et le direct vit dessus.");
        Assert.IsFalse(copy.activeSelf, "Une copie de l'arène.");
        Assert.IsTrue(copyWall.activeSelf, "Éteinte d'un bloc : ses enfants ne changent pas.");
        Assert.IsFalse(floor.activeSelf, "Le sol de la salle.");
        Assert.IsTrue(dummy.activeInHierarchy, "L'objet réseau de scène, et ses parents.");
        Assert.IsTrue(hitbox.activeInHierarchy, "Et tout ce qu'il porte.");

        site.SetLive(true);
        Assert.IsTrue(copy.activeSelf && floor.activeSelf, "Il se rallume.");
        Assert.IsFalse(asleep.activeSelf, "Ce que la scène a éteint reste éteint.");
    }
}
