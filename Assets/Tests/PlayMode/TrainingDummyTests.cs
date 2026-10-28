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

    /// <summary>Neutralisé, le mannequin tombe en arrière (sa tête finit derrière ses pieds, au sol), ne se
    /// touche plus, puis se relève et se touche de nouveau quand il retrouve sa vie (2026-10-27).</summary>
    [UnityTest]
    public IEnumerator UnMannequinNeutralise_TombeEnArriere_NeSeTouchePlus_PuisSeReleve()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
        dummy = Object.Instantiate(prefab, new Vector3(30f, 0f, 30f), Quaternion.Euler(0f, 180f, 0f));
        yield return null;
        Physics.SyncTransforms();

        var trainee = dummy.GetComponent<TrainingDummy>();
        Transform look = dummy.transform.Find("Apparence");
        Vector3 facing = dummy.transform.forward;
        Vector3 front = dummy.transform.position + facing * 5f;
        Vector3 chest = dummy.transform.position + Vector3.up * 1.25f;
        Assert.AreEqual(HitboxZoneType.Torso, ShootAt(front + Vector3.up * 1.25f, chest), "Debout : touché au buste (garde).");

        dummy.GetComponent<Health>().ApplyDamage(1000f);
        float until = Time.time + TrainingDummy.FallTime + TrainingDummy.BounceTime + 0.2f;
        while (Time.time < until) yield return null;
        Physics.SyncTransforms();

        Vector3 head = look.TransformPoint(new Vector3(0f, trainee.EyeHeight, 0f));
        Vector3 offset = head - dummy.transform.position;
        Assert.Less(offset.y, 0.5f, "Sa tête est au sol.");
        Assert.Less(Vector3.Dot(offset, facing), -1.2f, "Il est tombé en arrière, loin de celui qui tire.");
        Assert.IsNull(ShootAt(front + Vector3.up * 1.25f, chest), "À terre, il ne se touche plus là où il était debout.");
        Assert.IsNull(ShootAt(head + Vector3.up * 3f, head), "Ni couché au sol.");
        Assert.IsFalse(dummy.GetComponent<CapsuleCollider>().enabled, "Ni son corps.");

        dummy.GetComponent<Health>().ResetHealth();
        until = Time.time + TrainingDummy.RiseTime + 0.2f;
        while (Time.time < until) yield return null;
        Physics.SyncTransforms();
        Assert.Less(Vector3.Distance(look.TransformPoint(new Vector3(0f, trainee.EyeHeight, 0f)), dummy.transform.position + Vector3.up * trainee.EyeHeight), 0.02f,
            "Relevé : de nouveau debout.");
        Assert.AreEqual(HitboxZoneType.Torso, ShootAt(front + Vector3.up * 1.25f, chest), "Et de nouveau touché au buste.");
        Assert.IsTrue(dummy.GetComponent<CapsuleCollider>().enabled);
    }
}
