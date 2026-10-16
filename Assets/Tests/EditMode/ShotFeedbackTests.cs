using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Ce que le tir montre et ce qui l'entoure (2026-10-16, retours de l'utilisateur) : le laser droit dans
/// l'axe du canon, la flamme de bouche à la place du traçant des armes à balles, pas de visée pendant
/// un rechargement, Échap dans le menu.
///
/// Ce qui casse ici ne lève aucune erreur : un faisceau qui part des bras mais s'arrête ailleurs que là
/// où le joueur le voit (les bras sont dessinés à un autre champ que le monde), une arme à balles qui
/// retrouve son traçant ou perd sa flamme, une visée qui fait entrer la caméra dans l'arme pendant le
/// rechargement, un menu dont on ne sort pas.
/// </summary>
public class ShotFeedbackTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Rifle = 0, Sniper = 1, Shotgun = 2, Pistol = 3;

    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created) if (o != null) Object.DestroyImmediate(o);
        created.Clear();
    }

    private static GameObject PlayerPrefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

    private static IReadOnlyList<WeaponData> Loadout() => PlayerPrefab.GetComponentInChildren<WeaponController>(true).Loadout;

    /// <summary>
    /// Le joueur voit son faisceau partir de ses bras, dessinés par une caméra au champ plus étroit que le
    /// monde. Le rayon cherché dans le monde est celui qu'il voit : chacun de ses points tombe, ramené
    /// dans le champ des bras (ElectricArc.WorldToArms), sur le faisceau des bras.
    /// </summary>
    [Test]
    public void LeFaisceauDuJoueur_SeChercheDansLeMondeLaOuIlLeVoit()
    {
        var eyeObject = new GameObject("Oeil");
        created.Add(eyeObject);
        Transform eye = eyeObject.transform;
        eye.SetPositionAndRotation(new Vector3(3f, 1.6f, -2f), Quaternion.Euler(8f, 35f, 0f));
        const float playerFov = 74f, armsFov = 60f;

        Vector3 armsOrigin = eye.TransformPoint(new Vector3(0.14f, -0.11f, 0.38f));
        Vector3 armsDirection = eye.TransformDirection(new Vector3(-0.03f, 0.02f, 1f).normalized);
        TacticalLaser.ArmsRayToWorld(armsOrigin, armsDirection, eye, playerFov, armsFov, out Vector3 origin, out Vector3 direction);

        Assert.AreEqual(1f, direction.magnitude, 1e-4f);
        Assert.Greater(Vector3.Angle(armsDirection, direction), 0.01f, "Garde : les deux champs diffèrent, le rayon aussi.");
        Assert.Less(Vector3.Distance(armsOrigin, ElectricArc.WorldToArms(origin, eye, playerFov, armsFov)), 1e-4f, "L'émetteur se voit là où le faisceau part.");
        foreach (float distance in new[] { 0.5f, 4f, 30f, 120f })
        {
            Vector3 seen = ElectricArc.WorldToArms(origin + direction * distance, eye, playerFov, armsFov);
            Vector3 fromArms = seen - armsOrigin;
            float offLine = Vector3.Cross(fromArms, armsDirection).magnitude;
            Assert.Less(offLine, 1e-3f * Mathf.Max(1f, distance), $"À {distance} m, le point du monde se voit sur le faisceau des bras.");
            Assert.Greater(Vector3.Dot(fromArms, armsDirection), 0f, "Devant l'émetteur, pas derrière.");
        }

        TacticalLaser.ArmsRayToWorld(armsOrigin, armsDirection, eye, armsFov, armsFov, out origin, out direction);
        Assert.Less(Vector3.Distance(armsOrigin, origin), 1e-4f, "Au même champ, rien ne change.");
        Assert.AreEqual(0f, Vector3.Angle(armsDirection, direction), 1e-3f);
    }

    [Test]
    public void EnRechargeant_OnNeVisePas()
    {
        Assert.IsTrue(PlayerLocomotion.GateAim(true, false), "Hors rechargement, la visée passe.");
        Assert.IsFalse(PlayerLocomotion.GateAim(true, true), "Pendant un rechargement, non.");
        Assert.IsFalse(PlayerLocomotion.GateAim(false, false));
        Assert.IsFalse(PlayerLocomotion.GateAim(false, true));
    }

    /// <summary>Les armes à balles n'ont plus de traçant : leur tir se voit à la flamme de bouche ; le
    /// raygun garde son rayon. Le préfab du joueur a la flamme et son matériau.</summary>
    [Test]
    public void LesArmesABalles_OntUneFlammeDeBouche_LeRaygunSonRayon()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (int slot in new[] { Rifle, Sniper, Shotgun })
            Assert.IsFalse(loadout[slot].shotIsBeam, $"{loadout[slot].weaponName} : une arme à balles n'a pas de traçant.");
        Assert.IsTrue(loadout[Pistol].shotIsBeam, "Le raygun tire un rayon, qui se voit.");

        var feedback = PlayerPrefab.GetComponent<WeaponVisualFeedback>();
        Assert.IsNotNull(feedback, "Le joueur montre ses tirs.");
        Assert.IsTrue(feedback.HasMuzzleFlash, "La flamme de bouche a son matériau.");
        var material = (Material)typeof(WeaponVisualFeedback)
            .GetField("muzzleFlashMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(feedback);
        Assert.AreEqual("DuelArena/FlammeBouche", material.shader.name, "Le matériau de la flamme (couleur prémultipliée).");
        Assert.Greater(feedback.MuzzleFlashDuration, 0f);
        Assert.Less(feedback.MuzzleFlashDuration, 0.2f, "Une flamme, pas une torche.");

        foreach (WeaponData data in loadout)
        foreach (GameObject prefab in Models(data))
            Assert.IsNotNull(prefab.GetComponent<WeaponModel>().Muzzle, $"{prefab.name} : la flamme part de sa bouche.");
    }

    private static IEnumerable<GameObject> Models(WeaponData data)
    {
        for (int i = 0; i < data.ModelCount; i++)
        {
            GameObject prefab = data.ModelPrefabFor(i);
            if (prefab != null) yield return prefab;
        }
    }

    /// <summary>Échap revient d'un seul écran, de partout où c'est sans risque.</summary>
    [Test]
    public void DansLeMenu_EchapRevientDUnEcran()
    {
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenPlay", false), "De l'écran Jouer, à l'écran titre.");
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenOptions", false), "Des Options aussi.");
        Assert.AreEqual("screenTitle", MainMenuController.BackTarget("screenArmory", false), "De Personnalisation aussi.");
        Assert.AreEqual("screenArmory", MainMenuController.BackTarget("screenWeaponCustomize", false), "De l'armurerie, à Personnalisation.");
        Assert.IsNull(MainMenuController.BackTarget("screenTitle", false), "L'écran titre ne revient nulle part.");
        Assert.IsNull(MainMenuController.BackTarget("screenLobby", false), "Le salon se quitte par son bouton.");
        Assert.IsNull(MainMenuController.BackTarget("screenOptions", true), "Pendant un remappage, Échap ne fait que l'annuler.");
        Assert.IsNull(MainMenuController.BackTarget(null, false));
    }
}
