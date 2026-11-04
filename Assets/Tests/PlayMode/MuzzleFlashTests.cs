using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// La flamme de bouche (2026-10-16, demandée par l'utilisateur à la place du traçant des armes à
/// balles), sur un vrai modèle d'arme : elle part de la bouche, dans l'axe du canon, suit l'arme, se
/// dessine avec elle (le layer des bras chez le tireur), éclaire un instant, puis disparaît. Flammes et
/// rayons sortent d'une réserve : un tir ne crée rien, et rien ne s'éteint avant son heure.
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

    private Transform BuildMuzzle()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        Assert.IsNotNull(prefab, "Le FA-07.");
        weapon = Object.Instantiate(prefab, new Vector3(2f, 1.4f, -1f), Quaternion.Euler(-12f, 70f, 8f));
        return weapon.GetComponent<WeaponModel>().Muzzle;
    }

    private static IEnumerator WaitFor(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private int RaysShown()
    {
        int shown = 0;
        foreach (LineRenderer ray in feedbackObject.GetComponentsInChildren<LineRenderer>(true))
        {
            if (ray.gameObject.activeSelf) shown++;
        }
        return shown;
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

        yield return WaitFor(feedback.MuzzleFlashDuration + 0.15f);
        Assert.IsFalse(flash.activeSelf, "La flamme s'éteint aussitôt.");
        Assert.IsFalse(second.activeSelf);
        Assert.AreNotSame(muzzle, flash.transform.parent, "Éteinte, elle quitte l'arme : l'épave et les bras ne copient pas la réserve.");

        GameObject third = feedback.SpawnMuzzleFlash(muzzle, model.transform.forward);
        Assert.IsTrue(third == flash || third == second, "Le coup suivant reprend une flamme de la réserve.");
        Assert.AreSame(muzzle, third.transform.parent, "Elle retourne à la bouche.");
        Assert.AreEqual(armsLayer, third.layer);
        Assert.Less(Vector3.Distance(muzzle.position, third.transform.position), 1e-4f);
    }

    /// <summary>La kill cam tient sa flamme 0,45 s pendant que le jeu continue : tout est pris, la réserve grandit, et
    /// rien ne s'éteint avant son heure. Vu échouer, la plus ancienne reprise pour le coup suivant.</summary>
    [UnityTest]
    public IEnumerator UneFlammeTenue_ResteJusquaSonEcheance_MemeSiLesCoupsSeSuivent()
    {
        WeaponVisualFeedback feedback = BuildFeedback();
        Transform muzzle = BuildMuzzle();
        GameObject held = feedback.SpawnMuzzleFlash(muzzle, muzzle.forward, 1.2f, 1f);
        GameObject second = feedback.SpawnMuzzleFlash(muzzle, muzzle.forward);
        GameObject third = feedback.SpawnMuzzleFlash(muzzle, muzzle.forward);
        Assert.AreNotSame(held, second);
        Assert.AreNotSame(held, third);
        Assert.AreNotSame(second, third, "Trois flammes à la fois.");

        yield return WaitFor(feedback.MuzzleFlashDuration + 0.1f);
        Assert.IsFalse(second.activeSelf, "Le coup ordinaire s'éteint à son heure.");
        Assert.IsTrue(held.activeSelf, "La flamme tenue reste.");
        yield return WaitFor(1f);
        Assert.IsFalse(held.activeSelf, "Puis elle s'éteint à la sienne.");
    }

    /// <summary>Une arme refaite (HeldWeapons) emporte la flamme qui brûlait à sa bouche : une autre renaît au coup
    /// suivant, sans erreur. Vu échouer, l'objet détruit gardé dans la réserve.</summary>
    [UnityTest]
    public IEnumerator UneFlammeEmporteeParUneArmeRefaite_Renait()
    {
        WeaponVisualFeedback feedback = BuildFeedback();
        Transform muzzle = BuildMuzzle();
        GameObject carried = feedback.SpawnMuzzleFlash(muzzle, muzzle.forward);
        GameObject otherCarried = feedback.SpawnMuzzleFlash(muzzle, muzzle.forward);
        Object.Destroy(weapon);
        yield return null;
        Assert.IsTrue(carried == null && otherCarried == null, "Garde : l'arme a emporté ses flammes.");

        Transform next = BuildMuzzle();
        GameObject flash = feedback.SpawnMuzzleFlash(next, next.forward);
        Assert.IsNotNull(flash);
        Assert.AreSame(next, flash.transform.parent);
        yield return WaitFor(feedback.MuzzleFlashDuration + 0.1f);
        Assert.IsFalse(flash.activeSelf, "Elle s'éteint comme les autres.");
    }

    /// <summary>
    /// Les rayons d'une arme laser sortent de la réserve, prête dès l'apparition : un tir de FX-3 (huit rayons) n'y crée
    /// rien ; chacun va d'un point à l'autre, à sa couleur, et s'éteint ; plus épais et plus longtemps, un tir chargé ;
    /// tout pris, elle grandit plutôt que d'éteindre un rayon avant son heure. Vu échouer, une ligne créée à chaque tir.
    /// </summary>
    [UnityTest]
    public IEnumerator LesRayons_SortentDeLaReserve_SansRienCreer_PuisSEteignent()
    {
        WeaponVisualFeedback feedback = BuildFeedback();
        int reserve = feedbackObject.GetComponentsInChildren<LineRenderer>(true).Length;
        Assert.GreaterOrEqual(reserve, 8, "La réserve tient les huit rayons d'un tir de FX-3.");
        Assert.AreEqual(0, RaysShown(), "Elle dort jusqu'au premier tir.");

        var red = new Color(1f, 0.1f, 0.05f);
        for (int i = 0; i < 8; i++) feedback.SpawnTracer(Vector3.up, Vector3.up + Vector3.forward * (i + 1), red);
        Assert.AreEqual(reserve, feedbackObject.GetComponentsInChildren<LineRenderer>(true).Length, "Un tir ne crée rien.");
        Assert.AreEqual(8, RaysShown(), "Chaque plomb a son rayon.");
        float[] ends = new float[8];
        foreach (LineRenderer ray in feedbackObject.GetComponentsInChildren<LineRenderer>(false))
        {
            Assert.AreEqual(Vector3.up, ray.GetPosition(0));
            Assert.Less(Mathf.Abs(ray.startColor.g - red.g) + Mathf.Abs(ray.endColor.g - red.g), 0.02f, "À la couleur de l'arme.");
            ends[Mathf.RoundToInt(ray.GetPosition(1).z) - 1] = 1f;
        }
        CollectionAssert.DoesNotContain(ends, 0f, "Chacun va à son point.");

        feedback.SpawnTracer(Vector3.zero, Vector3.right, red);
        Assert.AreEqual(9, RaysShown(), "Tout pris, la réserve grandit : aucun rayon ne s'éteint avant son heure.");

        yield return WaitFor(0.25f);
        Assert.AreEqual(0, RaysShown(), "Les rayons s'éteignent aussitôt.");

        int grown = feedbackObject.GetComponentsInChildren<LineRenderer>(true).Length;
        feedback.SpawnTracer(Vector3.zero, Vector3.right, red);
        LineRenderer plain = feedbackObject.GetComponentInChildren<LineRenderer>(false);
        float plainWidth = plain.startWidth;
        yield return WaitFor(0.25f);
        feedback.SpawnTracer(Vector3.zero, Vector3.right, red, 10f);
        LineRenderer charged = feedbackObject.GetComponentInChildren<LineRenderer>(false);
        Assert.AreEqual(plainWidth * 10f, charged.startWidth, 1e-5f, "Un tir chargé est plus épais.");
        yield return WaitFor(0.1f);
        Assert.AreEqual(1, RaysShown(), "Et il dure plus longtemps.");
        yield return WaitFor(0.55f);
        Assert.AreEqual(0, RaysShown());
        Assert.AreEqual(grown, feedbackObject.GetComponentsInChildren<LineRenderer>(true).Length, "La réserve resservie ne grandit plus.");
    }

    /// <summary>Le maillage des marques et leurs étincelles se créent à l'arrivée, dans la scène du jeu : le premier impact
    /// n'a plus rien à faire, et le maillage, vide, se dessine déjà (ses shaders se préparent sous le noir). Vu échouer, la
    /// boîte du maillage laissée vide : il ne se dessine pas tant qu'il n'a pas de marque.</summary>
    [UnityTest]
    public IEnumerator LesImpacts_SePreparent_AvantLePremierTir()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShotImpacts.Prepare(scene);
        ShotImpacts impacts = Object.FindAnyObjectByType<ShotImpacts>();
        Assert.IsNotNull(impacts, "Rien ne prépare les impacts.");
        Assert.AreEqual(scene, impacts.gameObject.scene, "Ils meurent avec la scène.");
        Assert.AreEqual(2, impacts.GetComponentsInChildren<ParticleSystem>(true).Length, "Les étincelles et l'éclat.");
        Mesh mesh = impacts.GetComponent<MeshFilter>().sharedMesh;
        Assert.IsNotNull(mesh, "Le matériau des marques est dans Resources.");
        Assert.Greater(mesh.bounds.size.x, 1000f, "Vide, le maillage se dessine déjà : sa boîte tient toute l'arène.");

        ShotImpacts.Prepare(scene);
        Assert.AreEqual(1, Object.FindObjectsByType<ShotImpacts>(FindObjectsInactive.Exclude).Length, "Une fois.");
        Object.Destroy(impacts.gameObject);
        yield return null;
    }
}
