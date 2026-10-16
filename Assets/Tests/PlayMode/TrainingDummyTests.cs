using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Un mannequin du stand de tir (2026-10-16), avec de vrais colliders : il se touche comme un robot
/// debout (la tête à la hauteur de l'œil, le buste, les jambes), son corps n'arrête pas les balles, il
/// encaisse les dégâts, et il se montre (la copie du robot, dessinée par la caméra du joueur).
/// </summary>
public class TrainingDummyTests
{
    private const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    private GameObject dummy;

    [TearDown]
    public void TearDown()
    {
        if (dummy != null) Object.Destroy(dummy);
    }

    private static HitboxZoneType? ShootAt(Vector3 origin, Vector3 target)
    {
        Vector3 direction = (target - origin).normalized;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, 20f, 1 << LayerMask.NameToLayer("Hitbox"), QueryTriggerInteraction.Collide))
            return null;
        PlayerHitboxZone zone = hit.collider.GetComponent<PlayerHitboxZone>();
        return zone != null ? zone.Zone : (HitboxZoneType?)null;
    }

    [UnityTest]
    public IEnumerator UnMannequin_SeToucheCommeUnRobotDebout()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
        Assert.IsNotNull(prefab, "Le préfab du mannequin.");
        dummy = Object.Instantiate(prefab, new Vector3(30f, 0f, 30f), Quaternion.Euler(0f, 180f, 0f));
        yield return null;
        Physics.SyncTransforms();

        var trainee = dummy.GetComponent<TrainingDummy>();
        float eye = trainee.EyeHeight;
        Vector3 front = dummy.transform.position + dummy.transform.forward * 5f;
        Vector3 at = dummy.transform.position;
        Assert.AreEqual(HitboxZoneType.Head, ShootAt(front + Vector3.up * eye, at + Vector3.up * eye), "La tête, à la hauteur de l'œil.");
        Assert.AreEqual(HitboxZoneType.Torso, ShootAt(front + Vector3.up * 1.25f, at + Vector3.up * 1.25f), "Le buste.");
        Assert.AreEqual(HitboxZoneType.Legs, ShootAt(front + Vector3.up * 0.5f, at + Vector3.up * 0.5f), "Les jambes.");
        Assert.IsNull(ShootAt(front + Vector3.up * 2.3f, at + Vector3.up * 2.3f), "Rien au-dessus de la tête.");

        Assert.IsFalse(Physics.Raycast(front + Vector3.up * 1.25f, -dummy.transform.forward, 20f, 1, QueryTriggerInteraction.Ignore),
            "Son corps n'arrête pas les balles (le décor seul, Default, les arrête).");

        Health health = dummy.GetComponent<Health>();
        health.ApplyDamage(30f);
        Assert.AreEqual(health.Max - 30f, health.Current, 1e-4f, "Il encaisse les dégâts.");

        Transform look = dummy.transform.Find("Apparence");
        Assert.IsNotNull(look, "La copie du robot.");
        Renderer[] renderers = look.GetComponentsInChildren<Renderer>();
        Assert.Greater(renderers.Length, 0);
        foreach (Renderer r in renderers) Assert.AreEqual(0, r.gameObject.layer, $"{r.name} : sur Default, la caméra du joueur le dessine.");
        Animator animator = look.GetComponent<Animator>();
        Assert.IsNull(animator.runtimeAnimatorController, "Il joue son attente, pas le contrôleur du jeu.");
    }
}
