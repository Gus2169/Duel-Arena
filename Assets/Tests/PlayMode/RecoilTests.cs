using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le recul ne doit JAMAIS toucher au yaw du corps.
///
/// Le yaw appartient à la simulation : il n'évolue que dans PlayerLocomotion.Move(), qui est
/// prédite, envoyée au serveur et rejouée. Jusqu'au 2026-10-05, le recul horizontal faisait
/// `locomotion.transform.Rotate(...)` depuis PlayerCameraLook, donc HORS de Move() : un client
/// distant tournait sans que le serveur le sache. Le zigzag du MP5 restait sous le seuil de
/// réconciliation de 1°, donc rien ne se voyait — c'est exactement la classe de défaut silencieux
/// que ce projet a déjà payée cinq fois avec les valeurs cumulatives.
/// </summary>
public class RecoilTests
{
    private GameObject playerObject;
    private GameObject pivotObject;
    private PlayerCameraLook look;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("TestPlayer");
        playerObject.SetActive(false);
        playerObject.layer = LayerMask.NameToLayer("Player");
        playerObject.AddComponent<CharacterController>();
        playerObject.AddComponent<PlayerInputReader>();
        playerObject.AddComponent<PlayerLocomotion>();

        // Même hiérarchie que le prefab : le CameraPivot est un enfant direct du joueur, et
        // PlayerCameraLook retrouve PlayerLocomotion dans ses parents — c'est ce chemin qui
        // permettait au recul de tourner le corps.
        pivotObject = new GameObject("CameraPivot");
        pivotObject.transform.SetParent(playerObject.transform, false);
        pivotObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        look = pivotObject.AddComponent<PlayerCameraLook>();

        playerObject.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.Euler(0f, 30f, 0f));
        playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (playerObject != null) Object.DestroyImmediate(playerObject);
    }

    [UnityTest]
    public IEnumerator LeReculHorizontal_NeTournePasLeCorps()
    {
        yield return null; // laisse passer Awake et un premier LateUpdate

        float bodyYawBefore = playerObject.transform.eulerAngles.y;
        float cameraYawBefore = pivotObject.transform.eulerAngles.y;

        look.AddInstantRotation(0f, 5f);

        // Garde anti-vacuité : le recul doit bien avoir été appliqué QUELQUE PART. Sans elle, une
        // version qui ignorerait purement le recul horizontal passerait ce test.
        Assert.AreEqual(5f, Mathf.DeltaAngle(cameraYawBefore, pivotObject.transform.eulerAngles.y), 0.01f,
            "La caméra ne porte pas le recul horizontal : la visée ne bouge plus du tout.");

        Assert.AreEqual(bodyYawBefore, playerObject.transform.eulerAngles.y,
            "Le recul a tourné le CORPS : le yaw a changé hors de Move(), le serveur ne le saura jamais.");
    }

    [UnityTest]
    public IEnumerator LaRecuperationDuRecul_RameneLaCameraDansLAxeDuCorps()
    {
        yield return null;

        look.AddInstantRotation(0f, 3f);
        look.AddInstantRotation(0f, -1.5f);
        look.AddInstantRotation(0f, -1.5f);

        Assert.AreEqual(0f, look.TestRecoilYaw, 1e-4f,
            "Après une récupération complète, la caméra doit regarder exactement là où le corps regarde.");
        Assert.AreEqual(0f, Mathf.DeltaAngle(playerObject.transform.eulerAngles.y, pivotObject.transform.eulerAngles.y), 0.01f);
    }
}
