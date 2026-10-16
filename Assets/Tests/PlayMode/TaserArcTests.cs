#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Les arcs électriques du gant à décharge (2026-10-16), avec le vrai robot, son vrai gant et de vrais
/// colliders : la charge à l'appui, la décharge au contact vers ce que le coup toucherait (sinon dans
/// l'air), puis plus rien ; et les bras de la vue subjective, dont la copie du gant n'apparaît qu'une
/// fois le geste commencé.
/// </summary>
public class TaserArcTests
{
    private GameObject shooter;
    private GameObject target;
    private Animator animator;

    [TearDown]
    public void TearDown()
    {
        if (shooter != null) Object.DestroyImmediate(shooter);
        if (target != null) Object.DestroyImmediate(target);
    }

    /// <summary>Un tireur : le taser sur l'enfant « Weapon », comme le préfab joueur, et le robot à côté.
    /// Hors session, il voit les coups comme un autre joueur (le corps dessine les arcs).</summary>
    private TaserController Shooter()
    {
        shooter = new GameObject("Tireur");
        shooter.AddComponent<NetworkObject>();
        var weapon = new GameObject("Weapon");
        weapon.transform.SetParent(shooter.transform, false);
        TaserController taser = weapon.AddComponent<TaserController>();
        GameObject model = TestCharacter.InstantiateModel(out animator);
        model.transform.SetParent(shooter.transform, false);
        return taser;
    }

    private TaserSparkFX ArcsOf(Animator a) => HeldWeapons.TaserGlove(a).Find("Poing").GetComponent<TaserSparkFX>();

    private static IEnumerator WaitSinceStrike(TaserController taser, float seconds)
    {
        while (Time.time - taser.LastStrikeSeenAt < seconds) yield return null;
    }

    private static int Shown(ArcLines lines, int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++) if (lines.Line(i).enabled) n++;
        return n;
    }

    /// <summary>La plus courte distance de `p` à l'une des pointes des électrodes (monde).</summary>
    private static float ToNearestElectrode(TaserSparkFX arcs, Vector3 p)
    {
        float best = float.MaxValue;
        foreach (Vector3 tip in arcs.Electrodes) best = Mathf.Min(best, Vector3.Distance(p, arcs.transform.TransformPoint(tip)));
        return best;
    }

    [UnityTest]
    public IEnumerator ALAppuiLeGantSeCharge_AuContactLaDechargeFrappeLaCible_PuisSEteint()
    {
        TaserController taser = Shooter();
        yield return null;
        TaserSparkFX arcs = ArcsOf(animator);
        Transform fist = arcs.transform;

        // La surface touchable d'un autre robot, à un mètre dans l'axe du poing.
        target = new GameObject("Cible") { layer = 7 };
        var body = target.AddComponent<CapsuleCollider>();
        body.isTrigger = true;
        body.radius = 0.25f;
        body.height = 1.6f;
        target.transform.position = fist.position + fist.forward * 1.0f;
        Physics.SyncTransforms();

        taser.TestSeeStrike();
        yield return WaitSinceStrike(taser, 0.07f);
        ArcLines lines = arcs.TestArcs;
        Assert.IsNotNull(lines, "Rien ne se dessine à l'appui.");
        int boltLines = arcs.TestBoltLines;
        Assert.Greater(Shown(lines, boltLines, lines.Count), 0, "À l'appui, les électrodes doivent crépiter.");
        Assert.AreEqual(0, Shown(lines, 0, boltLines), "Aucun éclair ne part avant le contact.");
        Assert.IsFalse(arcs.Flash.enabled, "La lumière attend la décharge.");
        int climb = arcs.TestFirstClimbLine;
        for (int i = boltLines; i < climb; i++)
        {
            if (!lines.Line(i).enabled) continue;
            foreach (Vector3 p in lines.Points(i))
                Assert.Less(Vector3.Distance(p, fist.position), 0.06f, "Les arcs de la charge sautent d'une électrode à l'autre, sans quitter le poing.");
        }
        Assert.AreEqual(1, Shown(lines, climb, lines.Count), "Pendant la charge, un arc court des condensateurs aux électrodes.");
        for (int i = climb; i < lines.Count; i++)
        {
            if (!lines.Line(i).enabled) continue;
            Vector3[] p = lines.Points(i);
            float fromCapacitor = float.MaxValue;
            foreach (Vector3 top in arcs.Capacitors) fromCapacitor = Mathf.Min(fromCapacitor, Vector3.Distance(p[0], fist.TransformPoint(top)));
            Assert.Less(fromCapacitor, 0.003f, "L'arc ne part pas du haut d'un condensateur.");
            Assert.Less(ToNearestElectrode(arcs, p[p.Length - 1]), 0.003f, "L'arc des condensateurs ne finit pas sur une électrode.");
        }

        yield return WaitSinceStrike(taser, taser.ContactDelay + 0.05f);
        int bolts = 0;
        for (int i = 0; i < boltLines; i += 3)   // chaque éclair principal, suivi de ses deux branches
        {
            if (!lines.Line(i).enabled) continue;
            bolts++;
            Vector3[] p = lines.Points(i);
            Vector3 end = p[p.Length - 1];
            Assert.Less(ToNearestElectrode(arcs, p[0]), 0.003f, "Un éclair ne part pas d'une électrode.");
            // Les éclairs tombent autour du point touché (jusqu'à 14 cm), sur la cible.
            float miss = Vector3.Distance(end, body.ClosestPoint(end));
            Debug.Log($"[ARCS] éclair {i / 3 + 1} : {Vector3.Distance(p[0], end) * 100f:F0} cm, bout à {miss * 100f:F1} cm de la cible");
            Assert.Less(miss, 0.06f, $"La décharge ne frappe pas la cible : l'éclair finit à {miss * 100f:F0} cm d'elle.");
        }
        Assert.AreEqual(boltLines / 3, bolts, "Au contact, chaque éclair principal doit partir.");
        Assert.IsTrue(arcs.Flash.enabled, "La lumière du poing bat avec la décharge.");
        Assert.Greater(arcs.Flash.intensity, 0f);

        yield return WaitSinceStrike(taser, taser.ContactDelay + arcs.DischargeDuration + 0.05f);
        Assert.AreEqual(0, lines.ShownCount, "La décharge doit s'éteindre.");
        Assert.IsFalse(arcs.Flash.enabled, "La lumière doit s'éteindre avec elle.");
    }

    [UnityTest]
    public IEnumerator SansRienAPortee_LesEclairsSePerdentDansLAir()
    {
        TaserController taser = Shooter();
        yield return null;
        TaserSparkFX arcs = ArcsOf(animator);

        taser.TestSeeStrike();
        yield return WaitSinceStrike(taser, taser.ContactDelay + 0.05f);
        ArcLines lines = arcs.TestArcs;
        Assert.IsNotNull(lines, "Rien ne se dessine au contact.");
        int bolts = 0;
        for (int i = 0; i < arcs.TestBoltLines; i += 3)
        {
            if (!lines.Line(i).enabled) continue;
            bolts++;
            Vector3[] p = lines.Points(i);
            float reach = Vector3.Distance(p[0], p[p.Length - 1]);
            Assert.That(reach, Is.InRange(0.3f, 0.95f), $"Dans le vide, un éclair se perd à moins d'un mètre du poing ({reach * 100f:F0} cm).");
            Assert.Less(lines.Line(i).endColor.a, lines.Line(i).startColor.a, "Perdu dans l'air, un éclair s'éteint vers son bout.");
        }
        Assert.Greater(bolts, 0, "Garde anti-vacuité : aucun éclair n'est parti.");
    }

    /// <summary>La copie du gant de la vue subjective n'apparaît qu'une fois le geste commencé
    /// (FirstPersonView la montre quand la couche du taser prend du poids) : abonnée au coup, elle le
    /// ratait (l'ancienne dette n° 59).</summary>
    [UnityTest]
    public IEnumerator EnVueSubjective_LesBrasDessinentLaDecharge_MemeMontresApresLAppui()
    {
        TaserController taser = Shooter();
        var cameraGo = new GameObject("Main Camera");
        cameraGo.transform.SetParent(shooter.transform, false);
        cameraGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        cameraGo.AddComponent<Camera>().enabled = false;
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        FirstPersonView.BuildView(animator.gameObject, cameraGo.transform, layer, out Animator arms, out _);
        HeldWeapons.WearTaserGlove(arms, false);   // hors du coup, les bras ne portent pas le gant
        yield return null;

        taser.TestSeeStrike();
        yield return new WaitForSeconds(0.1f);
        HeldWeapons.WearTaserGlove(arms, true);
        yield return WaitSinceStrike(taser, taser.ContactDelay + 0.05f);

        TaserSparkFX viewArcs = ArcsOf(arms);
        Assert.IsNotNull(viewArcs.TestArcs, "Les bras ne dessinent rien : la copie du gant, montrée après l'appui, a raté le coup.");
        Assert.Greater(Shown(viewArcs.TestArcs, 0, viewArcs.TestBoltLines), 0, "Les bras ne dessinent pas la décharge.");
        Assert.AreEqual(layer, viewArcs.TestArcs.Line(0).gameObject.layer, "Les arcs des bras sont dessinés par la caméra des bras (layer Viewmodel).");
        Assert.AreNotSame(ArcsOf(animator).TestArcs, viewArcs.TestArcs, "Les bras et le corps ont chacun leurs arcs.");
    }
}
#endif
