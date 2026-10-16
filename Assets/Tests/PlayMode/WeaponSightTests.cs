#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le viseur à écran numérique (2026-10-09) : un écran qui affiche, en visée, ce que filme une caméra
/// placée à l'œil, avec la réticule au centre. Il était intégré à l'ancien fusil d'assaut
/// (FusilAssaut.prefab), que le FA-07 de Blender a remplacé en main le 2026-10-15 ; il reviendra en
/// accessoire. Testé sur ce préfab, que la vue subjective sait toujours allumer (FirstPersonView).
///
/// Ce qui casse ici ne lève aucune erreur : un repère « Mire » déplacé, et l'œil ne regarde plus au
/// centre de l'écran ; un matériau posé sur l'asset au lieu de la copie, et tous les joueurs verraient
/// l'écran du propriétaire ; une caméra du viseur qui tourne hors visée, et l'on paie un rendu du décor
/// de plus à chaque image pour rien.
/// </summary>
public class WeaponSightTests
{
    private const string RiflePath = "Assets/_ProjectArena/Weapon/FusilAssaut.prefab";
    private GameObject instance;
    private GameObject cameraGo;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
    }

    private static UnityEditor.SerializedObject SightSettings(WeaponSight sight) => new UnityEditor.SerializedObject(sight);

    private WeaponSight InstantiateRifle()
    {
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        Assert.IsNotNull(asset, "Le fusil au viseur numérique a disparu : " + RiflePath);
        instance = Object.Instantiate(asset);
        var sight = instance.GetComponent<WeaponSight>();
        Assert.IsNotNull(sight, "Le fusil n'a pas de WeaponSight.");
        return sight;
    }

    [Test]
    public void EnVisee_LOeilRegardeAuCentreDeLEcranDuViseur()
    {
        WeaponSight sight = InstantiateRifle();
        Transform mire = instance.GetComponent<WeaponModel>().Sight;
        Assert.IsNotNull(mire, "Le fusil n'a pas de repère « Mire ».");

        // L'écran : la plus grande face du sous-maillage de l'écran tournée vers l'œil (vers -Z).
        var settings = SightSettings(sight);
        var renderer = (Renderer)settings.FindProperty("screenRenderer").objectReferenceValue;
        int index = settings.FindProperty("screenMaterialIndex").intValue;
        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] v = mesh.vertices;
        int[] tris = mesh.GetTriangles(index);
        Bounds pane = default;
        bool any = false;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
            if (Vector3.Cross(b - a, c - a).normalized.z > -0.9f) continue;
            if (!any) { pane = new Bounds(a, Vector3.zero); any = true; }
            pane.Encapsulate(a); pane.Encapsulate(b); pane.Encapsulate(c);
        }
        Assert.IsTrue(any, "L'écran du viseur n'a aucune face tournée vers l'œil.");

        // La Mire est sur l'axe optique, au centre de l'écran : à 2 mm près, et dans l'axe du canon.
        Vector3 local = mire.localPosition;
        Assert.Less(Mathf.Abs(local.x - pane.center.x), 0.002f, "La Mire n'est pas au centre de l'écran (horizontalement).");
        Assert.Less(Mathf.Abs(local.y - pane.center.y), 0.002f, "La Mire n'est pas au centre de l'écran (verticalement).");
        Assert.Less(Quaternion.Angle(Quaternion.identity, mire.localRotation), 0.01f, "La Mire doit regarder dans l'axe du canon (+Z).");

        // Et la vue subjective place la Mire sur l'axe du regard, devant l'œil.
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
        Vector3 aim = new UnityEditor.SerializedObject(prefab.GetComponent<FirstPersonView>()).FindProperty("aimSightPosition").vector3Value;
        Assert.AreEqual(0f, aim.x, 1e-4f, "En visée, l'écran doit être centré sur le regard.");
        Assert.AreEqual(0f, aim.y, 1e-4f, "En visée, l'écran doit être centré sur le regard.");
        Assert.Greater(aim.z, 0.05f, "En visée, l'écran doit être devant l'œil.");
    }

    [Test]
    public void LEcranNumerique_NeVitQueSurLaCopie_EtSaCameraNeTourneQuEnVisee()
    {
        WeaponSight sight = InstantiateRifle();
        var settings = SightSettings(sight);
        var renderer = (Renderer)settings.FindProperty("screenRenderer").objectReferenceValue;
        int index = settings.FindProperty("screenMaterialIndex").intValue;
        Material assetMaterial = renderer.sharedMaterials[index];

        cameraGo = new GameObject("CameraTest");
        var playerCamera = cameraGo.AddComponent<Camera>();
        playerCamera.fieldOfView = 90f;
        sight.EnableDigitalScreen(playerCamera);

        var sightCamera = playerCamera.transform.Find("CameraViseur")?.GetComponent<Camera>();
        Assert.IsNotNull(sightCamera, "La caméra du viseur n'a pas été créée sous la caméra du joueur.");
        Assert.IsNotNull(sightCamera.targetTexture, "La caméra du viseur doit filmer dans une texture.");

        Material screen = renderer.sharedMaterials[index];
        Assert.AreNotSame(assetMaterial, screen, "L'écran de la copie doit recevoir son propre matériau.");
        Assert.AreEqual("DuelArena/ViseurEcran", screen.shader.name);
        Assert.AreSame(sightCamera.targetTexture, screen.GetTexture("_Ecran"), "L'écran doit afficher ce que filme le viseur.");

        // L'asset du fusil n'est pas touché : les autres joueurs gardent l'écran d'origine.
        var rifleAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        Assert.AreNotEqual("DuelArena/ViseurEcran", rifleAsset.GetComponent<MeshRenderer>().sharedMaterials[index].shader.name,
            "Le matériau de l'écran numérique a été posé sur l'asset : tous les joueurs le verraient.");

        // Hors visée, la caméra du viseur ne tourne pas ; en visée, elle filme au champ du grossissement.
        sight.UpdateScreen(playerCamera, null, 0f);
        Assert.IsFalse(sightCamera.enabled, "À la hanche, la caméra du viseur ne doit pas tourner.");
        Assert.AreEqual(0f, screen.GetVector("_Projection").w, 1e-4f, "À la hanche, l'écran reste en veille.");

        sight.UpdateScreen(playerCamera, null, 1f);
        Assert.IsTrue(sightCamera.enabled, "En visée, la caméra du viseur doit filmer.");
        Assert.AreEqual(1f, screen.GetVector("_Projection").w, 1e-4f, "En visée, l'écran est allumé.");
        float expected = 2f * Mathf.Atan(WeaponSight.FilmedHalfTangent / sight.Magnification) * Mathf.Rad2Deg;
        Assert.AreEqual(expected, sightCamera.fieldOfView, 0.01f, "Le champ du viseur ne correspond pas à son grossissement.");
    }
}
#endif
