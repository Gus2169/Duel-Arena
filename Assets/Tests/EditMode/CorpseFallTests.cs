using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Comment tombe une épave (CorpseFall, 2026-11-04) : au hasard, mais selon le coup. Ce qui casse ici se voit sans lever d'erreur :
/// le même robot qui tombe de la même façon à chaque mort, un coup de pompe qui fait s'affaisser, une vrille à contresens du coup.
/// </summary>
public class CorpseFallTests
{
    private const RobotCorpse.RagdollPart Torso = RobotCorpse.RagdollPart.Torso;
    private const RobotCorpse.RagdollPart Head = RobotCorpse.RagdollPart.Head;
    private const RobotCorpse.RagdollPart Thigh = RobotCorpse.RagdollPart.LeftThigh;
    private const RobotCorpse.BurstSeverity Light = RobotCorpse.BurstSeverity.Light;
    private const RobotCorpse.BurstSeverity Heavy = RobotCorpse.BurstSeverity.Heavy;
    private const RobotCorpse.BurstSeverity Brutal = RobotCorpse.BurstSeverity.Brutal;

    // Un tir de face (vers -Z, sur un robot qui regarde +Z).
    private static CorpseFall.Blow Blow(float impulse, RobotCorpse.RagdollPart hit, float closeness, int seed, float side = 0.6f, float lift = 0f)
        => new CorpseFall.Blow
        {
            Impulse = Vector3.back * impulse + Vector3.up * lift,
            Mass = RobotCorpse.RobotMass,
            Hit = hit,
            Side = side,
            Forward = Vector3.forward,
            Closeness = closeness,
            Seed = seed,
        };

    private static int SeedFor(CorpseFall.Style style, float impulse, RobotCorpse.RagdollPart hit, float closeness, float side = 0.6f)
    {
        for (int seed = 1; seed < 10000; seed++)
        {
            if (CorpseFall.For(Blow(impulse, hit, closeness, seed, side)).Style == style) return seed;
        }
        Assert.Fail($"Aucune graine ne donne {style}.");
        return 0;
    }

    [Test]
    public void LaMemeGraine_LaMemeChute_UneAutreGraine_UneAutreChute()
    {
        CorpseFall.Plan first = CorpseFall.For(Blow(14f, Torso, 0.8f, 42));
        CorpseFall.Plan again = CorpseFall.For(Blow(14f, Torso, 0.8f, 42));
        Assert.AreEqual(first.Style, again.Style);
        Assert.AreEqual(first.Push, again.Push, "Chaque machine tire le même plan de la même graine.");
        Assert.AreEqual(first.Spin, again.Spin);
        Assert.AreEqual(first.LeftArm, again.LeftArm);
        int different = 0;
        for (int seed = 43; seed < 63; seed++)
        {
            if (CorpseFall.For(Blow(14f, Torso, 0.8f, seed)).Push != first.Push) different++;
        }
        Assert.AreEqual(20, different, "Deux graines voisines ne font pas la même chute.");
    }

    [Test]
    public void LesFaconsDeTomber_SeTirentSelonLeursParts_MemeAvecDesGrainesVoisines()
    {
        CorpseFall.Weights(Light, Torso, 0.8f, out float heap, out float dive, out float spin, out float thrown);
        float total = heap + dive + spin + thrown;
        var counts = new int[4];
        const int draws = 4000;
        for (int seed = 0; seed < draws; seed++) counts[(int)CorpseFall.For(Blow(14f, Torso, 0.8f, seed)).Style]++;
        float[] expected = { heap / total, dive / total, spin / total, thrown / total };
        for (int i = 0; i < 4; i++)
        {
            Assert.Greater(expected[i], 0.1f, "Garde : chaque façon a sa part.");
            Assert.AreEqual(expected[i], counts[i] / (float)draws, 0.03f, $"{(CorpseFall.Style)i} : sa part.");
        }
    }

    [Test]
    public void UnCoupBrutal_RenverseOuFaitTourner_JamaisSAffaisser()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            CorpseFall.Style style = CorpseFall.For(Blow(300f, Torso, 0.9f, seed)).Style;
            Assert.That(style, Is.EqualTo(CorpseFall.Style.Thrown).Or.EqualTo(CorpseFall.Style.Spin), "Le pompe et le gant projettent.");
        }
    }

    [Test]
    public void LaPieceTouchee_ChoisitLaChute()
    {
        CorpseFall.Weights(Light, Thigh, 0.8f, out _, out float diveLegs, out _, out _);
        CorpseFall.Weights(Light, Torso, 0.8f, out _, out float diveChest, out _, out _);
        CorpseFall.Weights(Light, Head, 0.8f, out float heapHead, out _, out _, out _);
        CorpseFall.Weights(Light, Torso, 0.8f, out float heapChest, out _, out _, out _);
        Assert.Greater(diveLegs, diveChest * 2f, "Une balle dans les jambes les fait céder.");
        Assert.Greater(heapHead, heapChest, "Une balle dans la tête fait s'affaisser sur place.");
    }

    [Test]
    public void DeLoin_OnSAffaisse_DePres_OnEstRenverse()
    {
        CorpseFall.Weights(Light, Torso, 0f, out float heapFar, out _, out _, out float thrownFar);
        CorpseFall.Weights(Light, Torso, 1f, out float heapNear, out _, out _, out float thrownNear);
        Assert.Greater(heapFar, heapNear);
        Assert.Greater(thrownNear, thrownFar);
        Assert.AreEqual(CorpseFall.Style.Heap, CorpseFall.Pick(Light, Torso, 0f, 0f), "Le début du tirage.");
        Assert.AreEqual(CorpseFall.Style.Thrown, CorpseFall.Pick(Light, Torso, 0f, 1f), "Sa fin.");
    }

    [Test]
    public void RenverseDansLeSensDuTir_PlusFortDePres_EtSelonLArme()
    {
        const float rifle = 14f, sniper = 45f;
        int seed = SeedFor(CorpseFall.Style.Thrown, sniper, Torso, 1f);
        Vector3 near = CorpseFall.For(Blow(sniper, Torso, 1f, seed)).Push;
        Assert.Greater(Vector3.Dot(near.normalized, Vector3.back), 0.9f, "Il part dans le sens du tir, à l'écart près.");
        float far = 0f, close = 0f, light = 0f, heavy = 0f;
        for (int i = 1; i < 400; i++)
        {
            CorpseFall.Plan farPlan = CorpseFall.For(Blow(sniper, Torso, 0f, i));
            CorpseFall.Plan nearPlan = CorpseFall.For(Blow(sniper, Torso, 1f, i));
            CorpseFall.Plan rifleSame = CorpseFall.For(Blow(rifle, Torso, 1f, i));
            if (farPlan.Style == CorpseFall.Style.Thrown) far = Mathf.Max(far, farPlan.Push.magnitude);
            if (nearPlan.Style == CorpseFall.Style.Thrown) { close = Mathf.Max(close, nearPlan.Push.magnitude); heavy = close; }
            if (rifleSame.Style == CorpseFall.Style.Thrown) light = Mathf.Max(light, rifleSame.Push.magnitude);
        }
        Assert.Greater(close, far * 1.5f, "De près, il part bien plus fort que de loin.");
        Assert.Greater(heavy, light, "Un fusil de précision renverse plus fort qu'un fusil d'assaut.");
    }

    [Test]
    public void UnRobotRenverse_SEcarteDuTir_DAutantPlusQueLeCoupEstLeger()
    {
        float lightest = 0f, heaviest = 0f;
        for (int seed = 0; seed < 600; seed++)
        {
            CorpseFall.Plan light = CorpseFall.For(Blow(14f, Torso, 0.8f, seed));
            CorpseFall.Plan heavy = CorpseFall.For(Blow(45f, Torso, 0.8f, seed));
            if (light.Style == CorpseFall.Style.Thrown) lightest = Mathf.Max(lightest, Vector3.Angle(light.Push, Vector3.back));
            if (heavy.Style == CorpseFall.Style.Thrown) heaviest = Mathf.Max(heaviest, Vector3.Angle(heavy.Push, Vector3.back));
        }
        Assert.That(lightest, Is.InRange(CorpseFall.HeavySwerve + 5f, CorpseFall.LightSwerve + 0.01f), "Un coup léger choisit à peine où il tombe.");
        Assert.That(heaviest, Is.InRange(8f, CorpseFall.HeavySwerve + 0.01f), "Un coup lourd le dit mieux.");
    }

    [Test]
    public void LaVrille_TourneDansLeSensDuCoup()
    {
        // Touché de face à sa droite, son épaule droite recule. Vu d'en haut (Y vers soi), il tourne dans le sens de +Y.
        int right = SeedFor(CorpseFall.Style.Spin, 14f, Torso, 0.8f, 0.8f);
        int left = SeedFor(CorpseFall.Style.Spin, 14f, Torso, 0.8f, -0.8f);
        CorpseFall.Plan onRight = CorpseFall.For(Blow(14f, Torso, 0.8f, right, 0.8f));
        CorpseFall.Plan onLeft = CorpseFall.For(Blow(14f, Torso, 0.8f, left, -0.8f));
        Assert.Greater(onRight.Spin.y, 4f, $"Touché à droite ({onRight.Spin}).");
        Assert.Less(onLeft.Spin.y, -4f, $"Touché à gauche ({onLeft.Spin}).");
        Assert.Greater(onRight.RightKnee, 1f, "La jambe de ce côté lâche.");
        Assert.AreEqual(0f, onRight.LeftKnee, "L'autre tient : il pivote dessus.");
        Assert.Greater(onLeft.LeftKnee, 1f);
    }

    [Test]
    public void LePlongeon_VaVersLAvantDuRobot_EtLeTas_TombeDansSesJambes()
    {
        int dive = SeedFor(CorpseFall.Style.Dive, 14f, Thigh, 0.8f);
        CorpseFall.Plan plan = CorpseFall.For(Blow(14f, Thigh, 0.8f, dive));
        Assert.Greater(plan.Push.z, 0f, "Il tombe devant lui, vers le tireur.");
        Assert.Greater(plan.Spin.x, 1f, "Le haut du corps part vers l'avant.");
        Assert.Greater(Mathf.Min(plan.LeftKnee, plan.RightKnee), 1.5f, "Les genoux cèdent.");

        int heap = SeedFor(CorpseFall.Style.Heap, 14f, Head, 0.3f);
        plan = CorpseFall.For(Blow(14f, Head, 0.3f, heap));
        Assert.Greater(plan.Slump, 0.5f, "Le haut du corps tombe dans ses jambes.");
        Assert.Less(plan.Push.magnitude / RobotCorpse.RobotMass, 0.7f, "Il reste à peu près où il était.");
    }

    [Test]
    public void LesBras_PartentChacunDeLeurCote()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            CorpseFall.Plan plan = CorpseFall.For(Blow(14f, Torso, 0.8f, seed));
            Assert.That(plan.LeftArm.magnitude, Is.InRange(1.9f, 6.1f));
            Assert.That(plan.RightArm.magnitude, Is.InRange(1.9f, 6.1f));
            Assert.AreNotEqual(plan.LeftArm, plan.RightArm, "Deux bras symétriques, c'est la croix.");
        }
    }

    [Test]
    public void LeCoupQuiSouleve_SouleveEncore_UnCoupPlongeant_NEnfoncePas()
    {
        int seed = SeedFor(CorpseFall.Style.Thrown, 340f, Torso, 1f);
        Assert.AreEqual(86f, CorpseFall.For(Blow(340f, Torso, 1f, seed, 0.6f, 86f)).Push.y, 0.01f, "Le pompe soulève l'épave.");
        Assert.AreEqual(0f, CorpseFall.For(Blow(340f, Torso, 1f, seed, 0.6f, -60f)).Push.y, 0.01f, "Un tir plongeant ne l'enfonce pas dans le sol.");
    }

    [Test]
    public void UnCoupSansDirection_NeCassePasLaChute()
    {
        var blow = new CorpseFall.Blow { Mass = 0f, Forward = Vector3.zero, Hit = Torso, Seed = 3 };
        CorpseFall.Plan plan = CorpseFall.For(blow);
        foreach (Vector3 v in new[] { plan.Push, plan.Spin, plan.LeftArm, plan.RightArm })
            Assert.IsTrue(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z));
    }

    [Test]
    public void LaProximite_VaDeUnABoutPortant_AZeroAuBoutDeLaPortee()
    {
        Assert.AreEqual(1f, WeaponController.Closeness(0f, 100f));
        Assert.AreEqual(0.75f, WeaponController.Closeness(25f, 100f), 1e-5f);
        Assert.AreEqual(0f, WeaponController.Closeness(150f, 100f));
        Assert.AreEqual(1f, WeaponController.Closeness(10f, 0f), "Une arme sans portée.");
        Assert.AreEqual(1f, WeaponController.Closeness(float.NaN, 100f));
    }
}
