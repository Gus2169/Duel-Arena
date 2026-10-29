using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le coup de taser (2026-10-08), interrogé par de VRAIS colliders, comme le fait le serveur
/// (TaserController.QueryStrike).
///
/// Un coup qui passe à travers un mur, qui rate un adversaire collé à soi, ou qui touche quelqu'un
/// dans son dos ne lève aucune erreur : il tue ou épargne la mauvaise personne, et le coup tue
/// d'un seul geste.
/// </summary>
public class TaserStrikeTests
{
    private const float Range = 1.0f;
    private const float Radius = 0.3f;
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private static readonly Vector3 Eye = new Vector3(0f, 1.65f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly Collider[] buffer = new Collider[16];
    private LayerMask hitboxMask;

    [SetUp]
    public void SetUp()
    {
        hitboxMask = 1 << LayerMask.NameToLayer("Hitbox");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        spawned.Clear();
    }

    /// <summary>Un adversaire debout, face à l'attaquant, à `z` mètres devant lui.</summary>
    private PlayerHitbox Opponent(float z, string name = "Adversaire")
    {
        var go = new GameObject(name);
        spawned.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(0f, 0f, z), Quaternion.Euler(0f, 180f, 0f));
        var hitbox = go.AddComponent<PlayerHitbox>();
        hitbox.Apply(PlayerLocomotion.Stance.Standing, new Vector3(0f, 1.65f, 0f), 0f);
        return hitbox;
    }

    private Collider Strike()
    {
        Physics.SyncTransforms();
        return TaserController.QueryStrike(Eye, Vector3.forward, Range, Radius, hitboxMask, 1, buffer);
    }

    private static bool Belongs(Collider c, PlayerHitbox hitbox) => c != null && c.transform.IsChildOf(hitbox.transform);

    [UnityTest]
    public IEnumerator APortee_LAdversaireEstTouche()
    {
        PlayerHitbox target = Opponent(1.2f);
        yield return null;
        Assert.IsTrue(Belongs(Strike(), target), "Un adversaire à 1,2 m doit être touché.");
    }

    [UnityTest]
    public IEnumerator HorsDePortee_RienNEstTouche()
    {
        Opponent(1.8f);
        yield return null;
        Assert.IsNull(Strike(), "Un adversaire à 1,8 m est hors de portée du taser.");
    }

#if UNITY_EDITOR
    /// <summary>Le gant tel qu'il est réglé dans le préfab joueur (2026-10-29) : il touchait jusqu'à 2,44 m entre
    /// les deux robots, trop loin pour l'utilisateur (« 1,5 ou 1 m »). Un adversaire à 1,3 m est touché, à 1,6 m
    /// non.</summary>
    [UnityTest]
    public IEnumerator LeGantDuJeu_NeToucheQuAMoinsDUnMetreEtDemi()
    {
        var player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var taser = new UnityEditor.SerializedObject(player.GetComponentInChildren<TaserController>(true));
        float range = taser.FindProperty("range").floatValue, radius = taser.FindProperty("radius").floatValue;
        PlayerHitbox target = Opponent(1.3f);
        yield return null;
        Physics.SyncTransforms();
        Assert.IsTrue(Belongs(TaserController.QueryStrike(Eye, Vector3.forward, range, radius, hitboxMask, 1, buffer), target),
                      "À 1,3 m, le coup du gant doit toucher.");
        target.transform.position = new Vector3(0f, 0f, 1.6f);
        Physics.SyncTransforms();
        Assert.IsNull(TaserController.QueryStrike(Eye, Vector3.forward, range, radius, hitboxMask, 1, buffer),
                      $"À 1,6 m, le coup du gant ne doit plus toucher (portée réglée : {range} m).");
    }
#endif

    [UnityTest]
    public IEnumerator DerriereUnMur_RienNEstTouche()
    {
        PlayerHitbox target = Opponent(1.2f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spawned.Add(wall);
        wall.transform.position = new Vector3(0f, 1.5f, 0.7f);
        wall.transform.localScale = new Vector3(3f, 3f, 0.1f);
        yield return null;

        // Garde anti-vacuité : sans le mur, ce même adversaire est bien touché.
        wall.SetActive(false);
        Assert.IsTrue(Belongs(Strike(), target), "Garde : sans le mur, l'adversaire doit être touché.");
        wall.SetActive(true);
        Assert.IsNull(Strike(), "Le coup a traversé le mur.");
    }

    [UnityTest]
    public IEnumerator AdversaireColleASoi_EstTouche()
    {
        // L'œil de l'attaquant est DANS la surface touchable de l'adversaire : un balayage
        // (SphereCast) l'ignorerait, puisqu'il commence à l'intérieur.
        PlayerHitbox target = Opponent(0.05f);
        yield return null;
        Assert.IsTrue(Belongs(Strike(), target), "Un adversaire collé à soi doit être touché.");
    }

    [UnityTest]
    public IEnumerator AdversaireDansLeDos_NEstPasTouche()
    {
        // Sa tête effleure le volume du coup, mais derrière l'attaquant.
        Opponent(-0.35f);
        yield return null;
        Assert.IsNull(Strike(), "Le taser a touché quelqu'un dans le dos de l'attaquant.");
    }

    [UnityTest]
    public IEnumerator DeuxAdversaires_LePlusProcheEstTouche()
    {
        PlayerHitbox near = Opponent(1.0f, "Proche");
        Opponent(1.7f, "Loin");
        yield return null;
        Assert.IsTrue(Belongs(Strike(), near), "Le coup doit toucher l'adversaire le plus proche.");
    }
}
