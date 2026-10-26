using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Une cible de la Fosse (2026-10-16), avec de vrais colliders : cachée, on ne la voit ni ne la touche ;
/// debout, elle se touche comme un robot ; tombée, elle est couchée et ne se touche plus ; sur un rail,
/// elle est là où sa pose le dit, et sa surface touchable la suit.
/// </summary>
public class CourseTargetTests
{
    private const string PrefabPath = "Assets/_ProjectArena/Stand/CibleParcours.prefab";
    private GameObject target;

    [TearDown]
    public void TearDown()
    {
        if (target != null) Object.Destroy(target);
    }

    private static HitboxZoneType? ShootAt(Vector3 origin, Vector3 point)
    {
        Physics.SyncTransforms();
        Vector3 direction = (point - origin).normalized;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, 30f, 1 << LayerMask.NameToLayer("Hitbox"), QueryTriggerInteraction.Collide))
            return null;
        PlayerHitboxZone zone = hit.collider.GetComponent<PlayerHitboxZone>();
        return zone != null ? zone.Zone : (HitboxZoneType?)null;
    }

    [UnityTest]
    public IEnumerator UneCible_CacheePuisDeboutPuisTombee_NeSeToucheQueDebout()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "La cible de la Fosse (Duel Arena > Stand de tir > Construire le stand).");
        target = Object.Instantiate(prefab, new Vector3(-40f, 0f, -40f), Quaternion.Euler(0f, 180f, 0f));
        yield return null;
        var course = target.GetComponent<CourseTarget>();
        var dummy = target.GetComponent<TrainingDummy>();
        Assert.IsFalse(dummy.Regenerates, "Une cible ne se relève qu'au départ d'une course.");
        Renderer[] renderers = target.transform.Find("Apparence").GetComponentsInChildren<Renderer>();
        Assert.Greater(renderers.Length, 0, "Garde : la cible a une apparence.");
        Collider body = target.GetComponent<CapsuleCollider>();

        Vector3 front = target.transform.position + target.transform.forward * 6f;
        Vector3 at = target.transform.position;
        float eye = dummy.EyeHeight;
        // Couchée (cachée ou tombée), la cible s'étend derrière ses pieds : un tir plongeant sur son buste.
        Vector3 lying = at - target.transform.forward * 0.9f + Vector3.up * 0.15f;
        double now = 100.0;

        course.ApplyPose(new CourseTargetMotion { state = CourseTargetState.Hidden }, now);
        Assert.IsTrue(System.Array.TrueForAll(renderers, r => r.forceRenderingOff), "Cachée : invisible.");
        Assert.IsNull(ShootAt(front + Vector3.up * 1.25f, at + Vector3.up * 1.25f), "Cachée : intouchable.");
        Assert.IsNull(ShootAt(lying + Vector3.up * 3f, lying), "Cachée : intouchable, même couchée au sol.");
        Assert.IsFalse(body.enabled, "Cachée : on ne s'y cogne pas.");

        course.ApplyPose(new CourseTargetMotion { state = CourseTargetState.Up, raisedAt = now - 1.0 }, now);
        Assert.IsTrue(System.Array.TrueForAll(renderers, r => !r.forceRenderingOff), "Debout : visible.");
        Assert.AreEqual(HitboxZoneType.Head, ShootAt(front + Vector3.up * eye, at + Vector3.up * eye), "Debout : la tête à la hauteur de l'œil.");
        Assert.AreEqual(HitboxZoneType.Torso, ShootAt(front + Vector3.up * 1.25f, at + Vector3.up * 1.25f), "Debout : le buste.");
        Assert.IsTrue(body.enabled);

        course.ApplyPose(new CourseTargetMotion { state = CourseTargetState.Down, raisedAt = now - 2.0, downAt = now - 1.0 }, now);
        Assert.Less(Vector3.Dot(target.transform.up, Vector3.up), 0.05f, "Tombée : couchée.");
        Assert.IsNull(ShootAt(front + Vector3.up * 1.25f, at + Vector3.up * 1.25f), "Tombée : on ne la touche plus.");
        Assert.IsNull(ShootAt(lying + Vector3.up * 3f, lying), "Tombée : même couchée au sol.");
        Assert.IsTrue(System.Array.TrueForAll(renderers, r => !r.forceRenderingOff), "Tombée : on la voit à terre jusqu'au départ suivant.");
    }

    [UnityTest]
    public IEnumerator UneCibleSurRail_EstLaOuSaPoseLeDit_EtSeToucheLa()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Vector3 start = new Vector3(-40f, 0f, -40f), end = new Vector3(-34f, 0f, -40f);
        target = Object.Instantiate(prefab, start, Quaternion.Euler(0f, 180f, 0f));
        var course = target.GetComponent<CourseTarget>();
        typeof(CourseTarget).GetField("moves", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(course, true);
        typeof(CourseTarget).GetField("railEnd", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(course, end);
        yield return null;

        float speed = (float)typeof(CourseTarget).GetField("railSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(course);
        double now = 50.0;
        var motion = new CourseTargetMotion { state = CourseTargetState.Up, raisedAt = now - 1.5 };
        course.ApplyPose(motion, now);
        Vector3 expected = CourseTarget.RailPosition(start, end, speed, 1.5);
        Assert.Greater(Vector3.Distance(expected, start), 2f, "Garde : elle a bien avancé sur son rail.");
        Assert.AreEqual(0f, Vector3.Distance(expected, target.transform.position), 1e-4f, "Là où sa pose le dit.");

        Vector3 front = expected + target.transform.forward * 6f;
        Assert.AreEqual(HitboxZoneType.Torso, ShootAt(front + Vector3.up * 1.25f, expected + Vector3.up * 1.25f), "Sa surface touchable l'a suivie.");
        Assert.IsNull(ShootAt(start + target.transform.forward * 6f + Vector3.up * 1.25f, start + Vector3.up * 1.25f), "Plus rien à son départ.");
    }
}
