using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// La kill cam et le plateau de l'écran de fin couvrent tout l'écran : la caméra du joueur s'éteint dessous, et Camera.main
/// devient la leur, celle que veulent le laser, la zone et les arcs. Ici, de vraies caméras, sur une scène vide.
/// </summary>
public class CoveredCameraTests
{
    private GameObject playerObject;
    private Camera player;
    private GameObject prefab;
    private GameObject spot;
    private VictoryStage stage;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("Caméra du joueur") { tag = "MainCamera" };
        player = playerObject.AddComponent<Camera>();
        playerObject.AddComponent<AudioListener>();

        // Le moins qu'il faut au plateau : un préfab joueur avec son enfant « Model ».
        prefab = new GameObject("Joueur");
        new GameObject("Model").transform.SetParent(prefab.transform, false);
        spot = new GameObject("Point de la victoire");
    }

    [TearDown]
    public void TearDown()
    {
        if (stage != null) Object.DestroyImmediate(stage.gameObject);
        if (KillCam.Instance != null) Object.DestroyImmediate(KillCam.Instance.gameObject);
        Object.DestroyImmediate(spot);
        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(playerObject);
    }

    [UnityTest]
    public IEnumerator LaKillCam_PrepareSaCameraEtSaBalleDesSonApparition_SansRienAllumer()
    {
        KillCam killCam = KillCam.Ensure(SceneManager.GetActiveScene());
        yield return null;

        Camera camera = killCam.TestCamera;
        Assert.IsNotNull(camera, "Sous le noir du chargement, pas à l'instant du coup.");
        Assert.IsFalse(camera.enabled, "Elle attend le coup.");
        Assert.IsTrue(camera.CompareTag("MainCamera"), "Allumée, elle est Camera.main : la caméra du joueur est éteinte dessous.");
        Assert.AreSame(player, Camera.main, "Éteinte, elle ne prend la place de personne.");

        LineRenderer[] lines = killCam.GetComponentsInChildren<LineRenderer>(true);
        Assert.Greater(lines.Length, 8, "De quoi suivre tous les plombs d'une décharge, et le sillage.");
        foreach (LineRenderer line in lines) Assert.IsFalse(line.enabled, line.name);
        Assert.IsFalse(killCam.TestPlaying);
    }

    [UnityTest]
    public IEnumerator LePlateau_EteintLaCameraDuJoueur_PuisLaRend()
    {
        stage = VictoryStage.Create();
        Assert.AreSame(player, Camera.main, "Garde : la caméra du joueur rend, le plateau n'est pas montré.");

        stage.Show(spot.transform, prefab, ChassisId.CT01, default, null, 1, player);
        Assert.IsFalse(player.enabled, "Dès la première image de l'écran de fin : l'arène ne se dessine qu'une fois.");
        yield return null;

        Assert.IsTrue(stage.TestCamera.enabled, "Le plateau a l'écran.");
        Assert.IsFalse(player.enabled);
        Assert.AreSame(stage.TestCamera, Camera.main, "Le laser, la zone et les arcs se règlent sur la caméra qui rend.");
        Assert.IsTrue(playerObject.GetComponent<AudioListener>().isActiveAndEnabled, "L'oreille du joueur reste à sa place.");

        stage.Hide();
        Assert.IsTrue(player.enabled, "L'écran de fin refermé, le joueur retrouve sa vue, dans la même image.");
        Assert.IsFalse(stage.TestCamera.enabled);
        Assert.AreSame(player, Camera.main);
    }

    [UnityTest]
    public IEnumerator LePlateau_TientLaCameraDuJoueur_QuandLaKillCamLaRend()
    {
        stage = VictoryStage.Create();
        stage.Show(spot.transform, prefab, ChassisId.CT01, default, null, 1, player);
        yield return null;
        Assert.IsFalse(player.enabled, "Garde : elle est éteinte.");

        // La kill cam qui se finit la rallume dans son Update ; le plateau la reprend avant le rendu.
        player.enabled = true;
        yield return null;
        Assert.IsFalse(player.enabled, "Le plateau couvre encore l'écran : pas deux rendus de l'arène jusqu'à sa fin.");
    }

    [UnityTest]
    public IEnumerator LePlateauDetruit_RendLaCameraDuJoueur()
    {
        stage = VictoryStage.Create();
        stage.Show(spot.transform, prefab, ChassisId.CT01, default, null, 1, player);
        yield return null;
        Assert.IsFalse(player.enabled, "Garde : elle est éteinte.");

        Object.DestroyImmediate(stage.gameObject);
        Assert.IsTrue(player.enabled, "L'arène quittée en plein écran de fin ne laisse pas le joueur sans vue.");
    }
}
