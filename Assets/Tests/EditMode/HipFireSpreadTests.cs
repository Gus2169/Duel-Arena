using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le tir à la hanche façon Rainbow Six (2026-10-16, HipFireSpread) et le coup mortel puissant du pompe
/// et du taser.
///
/// Ce qui casse ici ne lève aucune erreur : des balles qui se tassent au centre du cône (le tir au jugé
/// redevient facile), un cône qui ne s'ouvre pas en bougeant ou ne se referme pas en épaulant, une rafale
/// qui ne se referme jamais, un laser sans effet, un hasard qui diffère entre le tireur et le serveur (le
/// traçant montrerait une balle que le serveur n'a pas tirée), un client qui avance son hasard sans tirer,
/// une épave que le pompe pousse comme une balle de fusil.
/// </summary>
public class HipFireSpreadTests
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

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    /// <summary>Une arme de test aux valeurs connues.</summary>
    private WeaponData TestWeapon(HipFireSpread.AimCurve curve = HipFireSpread.AimCurve.Slow)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        created.Add(data);
        data.hipSpread = 1.5f;
        data.hipSpreadMoving = 3f;
        data.hipSpreadPerShot = 0.3f;
        data.hipSpreadMaxBloom = 1.5f;
        data.hipSpreadRecovery = 4f;
        data.aimCurve = curve;
        return data;
    }

    [Test]
    public void ALaHanche_LaBallePartAuHasardDansToutLeCone()
    {
        const int count = 4000;
        double radiusSum = 0;
        int edge = 0;
        var quadrants = new int[4];
        for (int shot = 0; shot < count; shot++)
        {
            Vector2 p = HipFireSpread.UnitDisk(HipFireSpread.Seed(7ul, shot));
            float r = p.magnitude;
            Assert.LessOrEqual(r, 1f + 1e-5f, "Dans le cône, jamais au-delà.");
            radiusSum += r;
            if (r > 0.9f) edge++;
            quadrants[(p.x >= 0f ? 0 : 1) + (p.y >= 0f ? 0 : 2)]++;
        }
        // Uniforme sur la surface du disque : rayon moyen 2/3, 19 % des balles au-delà de 0,9.
        Assert.AreEqual(2f / 3f, radiusSum / count, 0.02, "Les balles couvrent tout le cône, elles ne se tassent pas au centre.");
        Assert.AreEqual(0.19, edge / (double)count, 0.03, "Le bord du cône est atteint aussi souvent qu'il le doit.");
        foreach (int q in quadrants) Assert.AreEqual(0.25, q / (double)count, 0.03, "Aucun côté n'est favorisé.");

        Assert.AreEqual(Vector2.zero, HipFireSpread.Offset(0f, 123u), "Sans cône, la balle part dans l'axe.");
        Assert.LessOrEqual(HipFireSpread.Offset(2f, 123u).magnitude, 2f + 1e-5f, "L'écart tient dans le cône.");
    }

    [Test]
    public void LeHasard_EstLeMemeChezLeTireurEtLeServeur_EtChangeAChaqueBalle()
    {
        uint seed = HipFireSpread.Seed(3ul, 41);
        Assert.AreEqual(HipFireSpread.UnitDisk(seed), HipFireSpread.UnitDisk(HipFireSpread.Seed(3ul, 41)),
            "La même balle du même joueur part au même endroit, partout : c'est tout ce qui les accorde.");
        Assert.AreNotEqual(HipFireSpread.UnitDisk(seed), HipFireSpread.UnitDisk(HipFireSpread.Seed(3ul, 42)), "La balle suivante part ailleurs.");
        Assert.AreNotEqual(HipFireSpread.UnitDisk(seed), HipFireSpread.UnitDisk(HipFireSpread.Seed(4ul, 41)), "Un autre joueur a son propre hasard.");

        // Le numéro de la balle n'avance qu'en tirant vraiment : un client ne choisit pas son hasard.
        var magazine = new WeaponController.MagazineState { ammo = 2 };
        Assert.IsTrue(WeaponController.TryConsumeRound(ref magazine, 0.0, 30));
        Assert.AreEqual(1, magazine.shotsFired, "Une balle tirée compte.");
        Assert.IsTrue(WeaponController.TryConsumeRound(ref magazine, 0.0, 30));
        Assert.IsFalse(WeaponController.TryConsumeRound(ref magazine, 0.0, 30), "Garde : le chargeur est vide.");
        Assert.AreEqual(2, magazine.shotsFired, "Un tir refusé ne compte pas.");
    }

    [Test]
    public void LeCone_SOuvreEnBougeant_EtSeResserreAccroupiEtAllonge()
    {
        WeaponData data = TestWeapon();
        float Cone(float speed, PlayerLocomotion.Stance stance = PlayerLocomotion.Stance.Standing) =>
            HipFireSpread.Cone(data, speed, stance, 0f, 0f, 1f);

        Assert.AreEqual(1.5f, Cone(0f), 1e-5f, "À l'arrêt, le cône de l'arme.");
        Assert.AreEqual(3f, Cone(HipFireSpread.ReferenceSpeed), 1e-5f, "À la marche, le cône en mouvement.");
        Assert.Less(Cone(2f), Cone(HipFireSpread.ReferenceSpeed), "En sneak, entre les deux.");
        Assert.Greater(Cone(2f), Cone(0f));
        Assert.AreEqual(4.5f, Cone(10f), 1e-5f, "L'élan d'un sprint l'ouvre encore.");
        Assert.AreEqual(Cone(10f), Cone(40f), 1e-5f, "Jusqu'à une borne.");
        Assert.AreEqual(1.5f, Cone(float.NaN), 1e-5f, "Une vitesse invalide ne casse rien.");

        float crouched = Cone(0f, PlayerLocomotion.Stance.Crouching);
        float prone = Cone(0f, PlayerLocomotion.Stance.Prone);
        Assert.Less(crouched, Cone(0f), "Accroupi, plus serré.");
        Assert.Less(prone, crouched, "Allongé, plus encore.");
    }

    [Test]
    public void EnEpaulant_LeConeSeReferme_SelonLaClasseDArme()
    {
        foreach (HipFireSpread.AimCurve curve in new[] { HipFireSpread.AimCurve.Slow, HipFireSpread.AimCurve.Linear, HipFireSpread.AimCurve.Fast })
        {
            WeaponData data = TestWeapon(curve);
            Assert.AreEqual(data.hipSpread, HipFireSpread.Cone(data, 0f, PlayerLocomotion.Stance.Standing, 0f, 0f, 1f), 1e-5f, $"{curve} : à la hanche, tout le cône.");
            Assert.AreEqual(0f, HipFireSpread.Cone(data, 8f, PlayerLocomotion.Stance.Standing, 1f, 1.5f, 1f), 1e-6f,
                $"{curve} : épaulée, la balle part dans l'axe, même en bougeant et en rafale.");
        }

        float Half(HipFireSpread.AimCurve curve) => HipFireSpread.Cone(TestWeapon(curve), 0f, PlayerLocomotion.Stance.Standing, 0.5f, 0f, 1f);
        Assert.Less(Half(HipFireSpread.AimCurve.Fast), Half(HipFireSpread.AimCurve.Linear), "Un pistolet est précis tôt...");
        Assert.Less(Half(HipFireSpread.AimCurve.Linear), Half(HipFireSpread.AimCurve.Slow), "... un fusil d'assaut à la fin (Rainbow Six, Y9S1).");
        Assert.AreEqual(0.75f * 1.5f, Half(HipFireSpread.AimCurve.Slow), 1e-5f);

        Assert.AreEqual(0f, HipFireSpread.AimProgress(0f, 0.2f), "Pas encore épaulé.");
        Assert.AreEqual(0.5f, HipFireSpread.AimProgress(0.1f, 0.2f), 1e-6f);
        Assert.AreEqual(1f, HipFireSpread.AimProgress(5f, 0.2f), "Épaulé.");
        Assert.AreEqual(0f, HipFireSpread.AimProgress(float.NaN, 0.2f));
        Assert.AreEqual(1f, HipFireSpread.AimProgress(0.01f, 0f), "Une arme qui épaule d'un coup.");

        // Le temps d'épaulement se compte sur les inputs, depuis le dernier qui n'épaulait pas.
        double clock = 0.0, lastUnaimed = 0.0, lastSprint = double.NegativeInfinity;
        void Feed(float dt, bool aiming)
        {
            PlayerLocomotion.AdvanceInputClock(ref clock, ref lastSprint, dt, false);
            PlayerLocomotion.TrackAim(ref lastUnaimed, clock, aiming);
        }
        Feed(0.01f, false);
        Feed(0.01f, true);
        Feed(0.01f, true);
        Assert.AreEqual(0.02, clock - lastUnaimed, 1e-9, "Deux inputs épaulés.");
        Feed(0.01f, false);
        Assert.AreEqual(0.0, clock - lastUnaimed, 1e-9, "Lâcher la visée la remet à zéro.");
    }

    [Test]
    public void LaRafale_OuvreLeCone_PuisIlSeReferme()
    {
        WeaponData data = TestWeapon();
        double clock = 10.0, last = double.NegativeInfinity;
        float bloom = 0f;
        var used = new List<float>();
        for (int shot = 0; shot < 12; shot++)
        {
            float at = HipFireSpread.BloomAt(bloom, last, clock, data.hipSpreadRecovery);
            used.Add(at);
            bloom = HipFireSpread.BloomAfterShot(data, at);
            last = clock;
            clock += 1.0 / 14.0; // rafale à 14 coups par seconde
        }
        Assert.AreEqual(0f, used[0], "La première balle part d'un cône au repos.");
        Assert.Greater(used[3], used[1], "La rafale ouvre le cône...");
        Assert.LessOrEqual(used[11], data.hipSpreadMaxBloom + 1e-5f, "... jusqu'à une borne.");
        Assert.Greater(used[11], data.hipSpreadMaxBloom * 0.5f, "Garde : la rafale l'a bien ouvert.");

        Assert.AreEqual(bloom, HipFireSpread.BloomAt(bloom, last, last + HipFireSpread.BloomRecoveryDelay, data.hipSpreadRecovery),
            "Entre deux balles d'une rafale, il ne se referme pas...");
        Assert.Less(HipFireSpread.BloomAt(bloom, last, last + 0.4, data.hipSpreadRecovery), bloom, "... sans tirer, il se referme...");
        Assert.AreEqual(0f, HipFireSpread.BloomAt(bloom, last, last + 1.0, data.hipSpreadRecovery), "... tout à fait en moins d'une seconde.");
        Assert.AreEqual(bloom, HipFireSpread.BloomAt(bloom, last, last - 1.0, data.hipSpreadRecovery), "Une horloge qui recule ne referme rien.");
        Assert.AreEqual(0f, HipFireSpread.BloomAt(float.NaN, last, clock, 4f), "Une valeur invalide ne casse rien.");

        float opened = HipFireSpread.Cone(data, 0f, PlayerLocomotion.Stance.Standing, 0f, bloom, 1f);
        Assert.AreEqual(data.hipSpread + bloom, opened, 1e-5f, "Le cône compte la rafale.");

        // Les armes de la panoplie, à leur propre cadence : une rafale de fusil d'assaut ou de raygun
        // ouvre le cône ; le sniper, entre deux balles, a le temps de le refermer.
        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (int slot in new[] { Rifle, Pistol, Sniper })
        {
            WeaponData weapon = loadout[slot];
            float b = 0f, at = 0f;
            double t = 0.0, previous = double.NegativeInfinity;
            for (int shot = 0; shot < 10; shot++)
            {
                at = HipFireSpread.BloomAt(b, previous, t, weapon.hipSpreadRecovery);
                b = HipFireSpread.BloomAfterShot(weapon, at);
                previous = t;
                t += 1.0 / weapon.shotsPerSecond;
            }
            if (slot == Sniper)
                Assert.AreEqual(0f, at, 1e-5f, $"{weapon.weaponName} : coup par coup, le cône se referme entre deux balles.");
            else
                Assert.Greater(at, weapon.hipSpreadMaxBloom * 0.8f, $"{weapon.weaponName} : la rafale ouvre le cône à sa cadence ({at:F2}°).");
        }
    }

    [Test]
    public void LeLaser_ResserreLeCone_EtLaGerbeDuPompe()
    {
        WeaponData data = TestWeapon();
        float without = HipFireSpread.Cone(data, 5f, PlayerLocomotion.Stance.Standing, 0f, 0.6f, 1f);
        Assert.AreEqual(without * 0.75f, HipFireSpread.Cone(data, 5f, PlayerLocomotion.Stance.Standing, 0f, 0.6f, 0.75f), 1e-5f);

        WeaponData shotgun = Loadout()[Shotgun];
        Assert.Greater(shotgun.PelletCount, 1, "Garde : le pompe tire des plombs.");
        for (int i = 0; i < shotgun.PelletCount; i++)
        {
            Assert.AreEqual(shotgun.PelletOffset(i, false) * 0.75f, shotgun.PelletOffset(i, false, 0.75f), $"Plomb {i} : la gerbe se resserre à la hanche.");
            Assert.AreEqual(shotgun.PelletOffset(i, true), shotgun.PelletOffset(i, true, 0.75f), $"Plomb {i} : épaulée, celle de la visée.");
        }
    }

    /// <summary>Les choix de l'utilisateur (2026-10-16) : tout le monde a un cône à la hanche, celui du
    /// sniper est le plus large ; le pompe n'en a pas, sa gerbe en tient lieu. Les courbes d'épaulement
    /// sont celles de Rainbow Six.</summary>
    [Test]
    public void LaPanoplie_TireAuJugeFaconRainbowSix()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        Assert.AreEqual(WeaponController.MaxSlots, loadout.Count);
        // Toute arme qui tire une seule balle (ou un seul rayon) a son cône à la hanche (2026-10-26 : le
        // slug du FAP-30 compris) ; les armes à gerbe n'en ont pas.
        foreach (WeaponData data in loadout)
        {
            if (data.PelletCount > 1) Assert.AreEqual(0f, data.hipSpread, $"{data.weaponName} : sa gerbe est sa dispersion.");
            else Assert.Greater(data.hipSpread, 0f, $"{data.weaponName} : à la hanche, la balle se disperse.");
        }
        foreach (int slot in new[] { Rifle, Sniper, Pistol })
        {
            WeaponData data = loadout[slot];
            Assert.AreEqual(1, data.PelletCount, $"Garde : {data.weaponName} tire une balle.");
            Assert.Greater(data.hipSpread, 0f, $"{data.weaponName} : à la hanche, la balle se disperse.");
            Assert.Greater(data.hipSpreadMoving, data.hipSpread, $"{data.weaponName} : davantage en bougeant.");
            Assert.Greater(data.hipSpreadRecovery, 0f, $"{data.weaponName} : la rafale se referme.");
        }
        Assert.Greater(loadout[Rifle].hipSpreadPerShot, 0f, "Une rafale de fusil d'assaut ouvre le cône.");
        Assert.Greater(loadout[Sniper].hipSpread, loadout[Rifle].hipSpread, "Le sniper a le plus large cône.");
        Assert.Greater(loadout[Sniper].hipSpread, loadout[Pistol].hipSpread);
        Assert.AreEqual(0f, loadout[Shotgun].hipSpread, "Le pompe : sa gerbe est sa dispersion.");
        Assert.AreEqual(0f, loadout[Shotgun].hipSpreadMoving);

        Assert.AreEqual(HipFireSpread.AimCurve.Slow, loadout[Rifle].aimCurve, "Fusil d'assaut : précis à la fin de l'épaulement.");
        Assert.AreEqual(HipFireSpread.AimCurve.Linear, loadout[Sniper].aimCurve, "Fusil de précision : en ligne droite.");
        Assert.AreEqual(HipFireSpread.AimCurve.Fast, loadout[Pistol].aimCurve, "Pistolet : précis tôt.");
        Assert.AreEqual(HipFireSpread.AimCurve.Fast, loadout[Shotgun].aimCurve, "Pompe : précis tôt.");
    }

    [Test]
    public void LeCoupMortel_DuPompeProjette_LesAutresPoussent()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponData shotgun = loadout[Shotgun];
        Assert.Greater(shotgun.killKnockback, 0f, "Le pompe projette l'épave.");
        float pointBlank = shotgun.damagePerHit * shotgun.PelletCount;

        Vector3 blow = WeaponController.KillImpulse(shotgun, Vector3.forward, pointBlank);
        Assert.AreEqual(shotgun.killKnockback * RobotCorpse.RobotMass, blow.z, 0.01f, "À bout portant, l'épave entière part à la vitesse du coup...");
        Assert.AreEqual(shotgun.killKnockbackLift * RobotCorpse.RobotMass, blow.y, 0.01f, "... un peu soulevée.");
        Assert.AreEqual(0f, blow.x, 1e-4f, "Dans le sens du tir.");

        Vector3 plunging = WeaponController.KillImpulse(shotgun, new Vector3(0f, -0.6f, 0.8f), pointBlank);
        Assert.AreEqual(blow.magnitude, plunging.magnitude, 0.01f, "Un tir plongeant projette autant...");
        Assert.Greater(plunging.y, 0f, "... sans enfoncer l'épave dans le sol.");

        float far = WeaponController.KillImpulse(shotgun, Vector3.forward, 2f * shotgun.damagePerHit * 0.3f).magnitude;
        Assert.Less(far, blow.magnitude, "De loin, quelques plombs : moins fort.");
        Assert.GreaterOrEqual(far, blow.magnitude * WeaponController.MinKnockbackShare - 0.01f, "Mais jamais rien.");

        foreach (int slot in new[] { Rifle, Sniper, Pistol })
        {
            WeaponData data = loadout[slot];
            Assert.AreEqual(0f, data.killKnockback, $"{data.weaponName} : une arme ordinaire.");
            Assert.AreEqual(WeaponController.DeathImpulse(100f), WeaponController.KillImpulse(data, Vector3.forward, 100f).magnitude, 1e-4f,
                $"{data.weaponName} : la simple poussée selon les dégâts.");
            Assert.Less(WeaponController.KillImpulse(data, Vector3.forward, 1000f).magnitude, blow.magnitude * 0.5f,
                $"{data.weaponName} : bien moins que le pompe.");
        }

        Vector3 strike = TaserController.DeathImpulse(Vector3.forward);
        Assert.AreEqual(TaserController.StrikeKnockback * RobotCorpse.RobotMass, strike.z, 0.01f, "Le taser projette aussi.");
        Assert.Greater(strike.y, 0f);
    }
}
