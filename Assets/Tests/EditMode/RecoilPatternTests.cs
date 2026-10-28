using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le recul coup par coup (2026-10-26) et l'éditeur des armes qui le dessine. Ce qui casse ici ne lève
/// aucune erreur : un motif qui boucle au lieu de répéter son dernier coup (la rafale repart en bas au
/// milieu du chargeur), un regard qui ne revient plus d'un recul vers le bas, une arme lente qui
/// enchaînerait son motif, un point qu'on déplace et qui emporte la fin de la rafale, un aperçu qui
/// annonce un robot neutralisé un coup trop tôt.
/// </summary>
public class RecoilPatternTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private readonly List<WeaponData> created = new List<WeaponData>();

    [TearDown]
    public void TearDown()
    {
        foreach (WeaponData data in created) Object.DestroyImmediate(data);
        created.Clear();
    }

    private WeaponData Weapon(float shotsPerSecond, params Vector2[] pattern)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        created.Add(data);
        data.recoilPattern = pattern;
        data.recoilPerShotDegrees = 1f;
        data.recoilRecoverySpeed = 0f;
        data.recoilResetDelay = 0.35f;
        data.shotsPerSecond = shotsPerSecond;
        data.chargeTime = 0f;
        data.heatPerShot = 0f;
        data.hipSpread = data.hipSpreadMoving = data.hipSpreadPerShot = data.hipSpreadMaxBloom = 0f;
        data.pelletPattern = new Vector2[0];
        data.falloffStart = data.falloffEnd = 0f;
        return data;
    }

    private static List<Vector2> Burst(WeaponData data, int shots, bool recovery)
    {
        var points = new List<Vector2>();
        RecoilPattern.Burst(data, shots, Vector2.one, recovery, points);
        return points;
    }

    private static void AreClose(Vector2 expected, Vector2 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, 1e-5f, message + " (x)");
        Assert.AreEqual(expected.y, actual.y, 1e-5f, message + " (y)");
    }

    [Test]
    public void UnCoup_PousseSelonSonRangDansLeMotif_PuisLeDernierSeRepete()
    {
        WeaponData data = Weapon(10f, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(-0.5f, 2f));
        data.recoilPerShotDegrees = 2f;
        AreClose(new Vector2(0f, 2f), RecoilPattern.Kick(data, 0), "Le premier coup, à la force de l'arme.");
        AreClose(new Vector2(1f, 2f), RecoilPattern.Kick(data, 1), "Le deuxième.");
        AreClose(new Vector2(-1f, 4f), RecoilPattern.Kick(data, 2), "Le dernier du motif.");
        AreClose(new Vector2(-1f, 4f), RecoilPattern.Kick(data, 7), "Au-delà du motif, le dernier coup se répète (il ne reprend pas au début).");
        data.recoilPattern = new Vector2[0];
        AreClose(Vector2.zero, RecoilPattern.Kick(data, 3), "Sans motif, aucun recul.");
    }

    [Test]
    public void LeRetourDuRegard_RameneChaqueAxeVersLAxe_SansLeDepasser_YComprisVersLeBas()
    {
        Vector2 back = RecoilPattern.Recovery(new Vector2(3f, -2f), 4f, 0.25f);
        AreClose(new Vector2(1f, -1f), back, "Un degré par axe en un quart de seconde à 4°/s, un recul vers le bas compris.");
        back = RecoilPattern.Recovery(new Vector2(0.5f, -0.2f), 4f, 1f);
        AreClose(new Vector2(0.5f, -0.2f), back, "Le regard revient jusqu'à l'axe, pas au-delà.");
        AreClose(Vector2.zero, RecoilPattern.Recovery(new Vector2(1f, 1f), 0f, 1f), "Sans vitesse de retour, rien ne revient.");
    }

    [Test]
    public void UneRafale_PartLePremierCoupDansLAxe_EtLeRegardRevientEntreDeuxCoups()
    {
        WeaponData data = Weapon(10f, new Vector2(0f, 1f));
        data.recoilRecoverySpeed = 2f; // 0,2° entre deux coups, à 10 coups par seconde
        List<Vector2> motif = Burst(data, 3, false);
        AreClose(Vector2.zero, motif[0], "Le premier coup part dans l'axe : sa poussée déplace le suivant.");
        AreClose(new Vector2(0f, 2f), motif[2], "Sans retour, le troisième part deux poussées plus haut.");

        List<Vector2> enJeu = Burst(data, 3, true);
        AreClose(new Vector2(0f, 0.8f), enJeu[1], "Le regard est revenu de 0,2° avant le deuxième coup.");
        AreClose(new Vector2(0f, 1.6f), enJeu[2], "Et encore de 0,2° avant le troisième.");

        List<Vector2> accroupi = new List<Vector2>();
        RecoilPattern.Burst(data, 3, new Vector2(1f, 0.5f), false, accroupi);
        AreClose(new Vector2(0f, 1f), accroupi[2], "La posture et la visée réduisent chaque poussée.");
    }

    [Test]
    public void UneArmeQuiTirePlusLentementQueLaRemiseAZero_ReprendSonMotifAuPremierCoup()
    {
        WeaponData slow = Weapon(1f, new Vector2(0f, 1f), new Vector2(5f, 5f));
        Assert.IsFalse(RecoilPattern.Chains(slow), "Un coup par seconde, plus lent que la remise à zéro (0,35 s).");
        AreClose(new Vector2(0f, 2f), Burst(slow, 3, false)[2], "Chaque coup pousse comme le premier.");

        WeaponData fast = Weapon(10f, new Vector2(0f, 1f), new Vector2(5f, 5f));
        Assert.IsTrue(RecoilPattern.Chains(fast), "Garde : à dix coups par seconde, le motif s'enchaîne.");
        AreClose(new Vector2(5f, 6f), Burst(fast, 3, false)[2], "Le deuxième coup pousse selon le motif.");

        WeaponData charged = Weapon(2.5f, new Vector2(0f, 1f));
        charged.chargeTime = 1f;
        Assert.AreEqual(1f, RecoilPattern.ShotInterval(charged), 1e-6f, "Un tir chargé à fond attend sa charge, pas seulement la cadence.");
    }

    [Test]
    public void DeplacerUnCoup_NeBougeQueLui_OuToutLaSuiteAvecMaj()
    {
        Vector2[] pattern = { new Vector2(0f, 1f) }; // le dernier se répète : quatre coups à (0,0) (0,1) (0,2) (0,3)
        Vector2[] alone = RecoilPatternEditing.MoveShot(pattern, 2, new Vector2(1f, 0f), false, 4);
        Vector2[] positions = RecoilPatternEditing.Positions(alone, 4, Vector2.one);
        AreClose(new Vector2(1f, 2f), positions[2], "Le troisième coup a bougé d'un degré à droite.");
        AreClose(new Vector2(0f, 1f), positions[1], "Le deuxième n'a pas bougé.");
        AreClose(new Vector2(0f, 3f), positions[3], "Le quatrième non plus.");

        Vector2[] withFollowing = RecoilPatternEditing.MoveShot(pattern, 2, new Vector2(1f, 0f), true, 4);
        positions = RecoilPatternEditing.Positions(withFollowing, 4, Vector2.one);
        AreClose(new Vector2(1f, 2f), positions[2], "Avec Maj, le troisième coup bouge...");
        AreClose(new Vector2(1f, 3f), positions[3], "... et la suite le suit, d'autant et pas plus.");
        Assert.AreEqual(1, pattern.Length, "Le motif d'origine n'est jamais modifié : l'éditeur écrit le nouveau sous l'Undo.");
    }

    [Test]
    public void LAperçu_CompteLesBallesParZone_EtLeCoupQuiNeutralise()
    {
        WeaponData data = Weapon(10f, Vector2.zero);
        data.damagePerHit = 25f;
        data.headMultiplier = 2f;
        data.legsMultiplier = 1f;
        data.maxRange = 100f;
        var setup = new BurstSetup
        {
            shots = 6,
            recoilScale = Vector2.one,
            aiming = true,
            stance = PlayerLocomotion.Stance.Standing,
            laserFactor = 1f,
            target = TargetShape.Facing(BodyLayout.DefaultStanding, new Vector3(0f, 1.65f, 0f)),
            distance = 10f,
            maxHealth = 100f,
        };
        var preview = new BurstPreview();
        preview.Compute(data, setup);
        Assert.AreEqual(6, preview.TorsoHits, "Sans recul, épaulé, tout part au milieu du buste.");
        Assert.AreEqual(3, preview.KillShot, "25 dégâts par balle : le quatrième coup retire les 100 PV.");
        Assert.AreEqual(0.3f, preview.KillTime, 1e-5f, "Trois intervalles de 0,1 s après le premier coup.");

        setup.aimAtHead = true;
        preview.Compute(data, setup);
        Assert.AreEqual(6, preview.HeadHits, "Visé à la tête.");
        Assert.AreEqual(1, preview.KillShot, "50 à la tête : deux coups.");

        data.recoilPattern = new[] { new Vector2(0f, 10f) };
        setup.aimAtHead = false;
        preview.Compute(data, setup);
        Assert.AreEqual(1, preview.TorsoHits, "Dix degrés par coup : à 10 m, seul le premier touche...");
        Assert.AreEqual(5, preview.Misses, "... les autres passent au-dessus du robot.");
        Assert.AreEqual(-1, preview.KillShot, "La rafale ne le neutralise pas.");
    }

    [Test]
    public void ChaqueArmeDeLArsenal_AUnMotifDeRecul_QuiPousseDAbordVersLeHaut()
    {
        WeaponController weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true);
        Assert.AreEqual(WeaponController.MaxSlots, weapon.SlotCount, "Garde : les douze armes.");
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            Assert.IsNotNull(data.recoilPattern, $"{data.weaponName} : pas de motif de recul.");
            Assert.Greater(data.recoilPattern.Length, 0, $"{data.weaponName} : un motif vide, l'arme ne recule plus.");
            foreach (Vector2 kick in data.recoilPattern)
                Assert.IsTrue(float.IsFinite(kick.x) && float.IsFinite(kick.y), $"{data.weaponName} : une poussée non finie.");
            Assert.Greater(RecoilPattern.Kick(data, 0).y, 0f, $"{data.weaponName} : le premier coup doit pousser le regard vers le haut.");
        }
    }
}
