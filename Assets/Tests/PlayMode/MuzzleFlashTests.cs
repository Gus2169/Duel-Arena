using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// La flamme de bouche (2026-10-16, demandée par l'utilisateur à la place du traçant des armes à
/// balles), sur un vrai modèle d'arme : elle part de la bouche, dans l'axe du canon, suit l'arme, se
/// dessine avec elle (le layer des bras chez le tireur), éclaire un instant, puis disparaît.
/// </summary>
public class MuzzleFlashTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string RiflePath = "Assets/_ProjectArena/Weapon/Armes/FA07/FA07.prefab";

    private GameObject weapon;
    private GameObject feedbackObject;

    [TearDown]
    public void TearDown()
    {
        if (weapon != null) Object.Destroy(weapon);
        if (feedbackObject != null) Object.Destroy(feedbackObject);
    }

    private WeaponVisualFeedback BuildFeedback()
    {
        var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<WeaponVisualFeedback>();
        var field = typeof(WeaponVisualFeedback).GetField("muzzleFlashMaterial",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        feedbackObject = new GameObject("Retours");
        var feedback = feedbackObject.AddComponent<WeaponVisualFeedback>();
        field.SetValue(feedback, field.GetValue(source));
        return feedback;
    }

    [UnityTest]
    public IEnumerator LaFlamme_PartDeLaBouche_DansLAxe_AvecLArme_PuisSEteint()
    {
        WeaponVisualFeedback feedback = BuildFeedback();
        Assert.IsTrue(feedback.HasMuzzleFlash, "Garde : le matériau du préfab est repris.");

        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        Assert.IsNotNull(prefab, "Le FA-07.");
        weapon = Object.Instantiate(prefab, new Vector3(2f, 1.4f, -1f), Quaternion.Euler(-12f, 70f, 8f));
        int armsLayer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        foreach (Transform t in weapon.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = armsLayer;
        var model = weapon.GetComponent<WeaponModel>();
        Transform muzzle = model.Muzzle;
        Assert.IsNotNull(muzzle, "Garde : la bouche du FA-07.");

        GameObject flash = feedback.SpawnMuzzleFlash(muzzle, model.transform.forward);
        Assert.IsNotNull(flash, "Une flamme au coup.");
        Assert.AreSame(muzzle, flash.transform.parent, "Elle suit l'arme.");
        Assert.Less(Vector3.Distance(muzzle.position, flash.transform.position), 1e-4f, "Elle part de la bouche.");
        Assert.Less(Vector3.Angle(model.transform.forward, flash.transform.forward), 0.1f, "Dans l'axe du canon.");
        Assert.AreEqual(armsLayer, flash.layer, "Sur le layer de l'arme : chez le tireur, celui des bras, dessiné avec eux.");
        var renderer = flash.GetComponent<MeshRenderer>();
        Assert.IsNotNull(renderer);
        Assert.AreEqual("DuelArena/FlammeBouche", renderer.sharedMaterial.shader.name);
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode, "Une flamme ne fait pas d'ombre.");
        Light light = flash.GetComponentInChildren<Light>();
        Assert.IsNotNull(light, "Le coup éclaire.");
        Assert.Greater(light.range, 1f);

        // L'arme bouge : la flamme la suit.
        weapon.transform.position += Vector3.up * 0.5f;
        Assert.Less(Vector3.Distance(muzzle.position, flash.transform.position), 1e-4f, "La flamme reste à la bouche.");

        GameObject second = feedback.SpawnMuzzleFlash(muzzle, model.transform.forward);
        Assert.Greater(Quaternion.Angle(flash.transform.rotation, second.transform.rotation), 10f, "D'un coup à l'autre, elle tourne.");

        float until = Time.realtimeSinceStartup + feedback.MuzzleFlashDuration + 0.15f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(flash == null, "La flamme s'éteint aussitôt.");
        Assert.IsTrue(second == null);
    }
}
