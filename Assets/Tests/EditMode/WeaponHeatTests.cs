using NUnit.Framework;
using UnityEngine;
using HeatRules = WeaponController.HeatRules;
using MagazineState = WeaponController.MagazineState;

/// <summary>
/// La chaleur des armes laser (2026-10-26, GDD § 6) : l'arme chauffe en tirant et refroidit seule ;
/// trop chaude, elle surchauffe et se vide d'elle-même par le geste du rechargement, plus longuement
/// qu'une vidange voulue. Fonctions pures, partagées par le tireur (qui prédit) et le serveur (qui fait
/// foi).
///
/// Ce qui casse ici ne lève aucune erreur : une arme qui ne surchauffe jamais (22 × 1/22 tombe à
/// 0,99999 en virgule flottante), qui ne refroidit pas, ou trop tôt ; une vidange interrompue qui efface
/// la surchauffe (un sprint suffirait à la contourner) ; le serveur qui voit une autre chaleur que le
/// tireur et refuse ses tirs.
/// </summary>
public class WeaponHeatTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static readonly HeatRules Rules = new HeatRules { perShot = 0.25f, coolDelay = 0.3f, coolTime = 2f, vent = 1.5f, overheatVent = 2.5f };

    private static WeaponController PrefabWeapon() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true);

    private static WeaponData Weapon(string designation)
    {
        WeaponController weapon = PrefabWeapon();
        for (int slot = 0; slot < weapon.SlotCount; slot++)
            if (weapon.DataOf(slot).Designation == designation) return weapon.DataOf(slot);
        Assert.Fail($"{designation} n'est pas dans l'arsenal.");
        return null;
    }

    [Test]
    public void LeTirQuiLaRemplit_Part_PuisElleSurchauffeEtSeVideDElleMeme()
    {
        var m = new MagazineState { ammo = 1 };
        for (int shot = 1; shot <= 3; shot++)
        {
            Assert.IsTrue(WeaponController.TryFireHot(ref m, shot * 0.1, Rules, 0.0), $"Tir {shot}.");
            Assert.AreEqual(0.25f * shot, m.heat, 1e-5f, $"Tir {shot} : un quart de plus.");
            Assert.IsFalse(m.overheated);
        }
        Assert.AreEqual(1, m.ammo, "Une arme laser ne dépense pas de balles.");

        Assert.IsTrue(WeaponController.TryFireHot(ref m, 0.4, Rules, 0.0), "Le tir qui la remplit part.");
        Assert.IsTrue(m.overheated, "Puis elle surchauffe.");
        Assert.AreEqual(0.4 + Rules.overheatVent, m.reloadEndsAt, 1e-9, "Et se vide d'elle-même, par la vidange forcée.");
        Assert.AreEqual(4, m.shotsFired, "Chaque rayon avance le numéro du tir (le hasard de la hanche).");

        Assert.IsFalse(WeaponController.TryFireHot(ref m, 1.0, Rules, 0.0), "Pendant la vidange, elle ne tire pas.");
        Assert.IsTrue(WeaponController.TryFireHot(ref m, 0.4 + Rules.overheatVent, Rules, 0.0), "Vidée, elle repart froide.");
        Assert.AreEqual(Rules.perShot, m.heat, 1e-5f);
        Assert.IsFalse(m.overheated);
    }

    [Test]
    public void ElleRefroiditSeule_ApresUnCourtDelaiSansTirer()
    {
        var m = new MagazineState();
        WeaponController.TryFireHot(ref m, 10.0, Rules, 0.0);
        WeaponController.TryFireHot(ref m, 10.1, Rules, 0.0);
        Assert.AreEqual(0.5f, WeaponController.HeatAt(m, 10.1 + 0.29, Rules), 1e-5f, "Pas avant le délai.");
        Assert.AreEqual(0.25f, WeaponController.HeatAt(m, 10.1 + 0.3 + 0.5, Rules), 1e-5f, "Puis à son rythme : de pleine à froide en 2 s.");
        Assert.AreEqual(0f, WeaponController.HeatAt(m, 20.0, Rules), "Jamais en dessous de froide.");

        // Le tir suivant part de la chaleur refroidie.
        Assert.IsTrue(WeaponController.TryFireHot(ref m, 10.9, Rules, 0.0));
        Assert.AreEqual(0.5f, m.heat, 1e-5f, "0,25 restant + 0,25.");
    }

    [Test]
    public void LaVidangeVoulue_EstPlusCourte_EtSeulementSIlYAQuelqueChoseAVider()
    {
        var cold = new MagazineState();
        Assert.IsFalse(WeaponController.TryStartVent(ref cold, 1.0, Rules), "Froide : rien à vider.");

        var m = new MagazineState();
        WeaponController.TryFireHot(ref m, 1.0, Rules, 0.0);
        Assert.IsTrue(WeaponController.TryStartVent(ref m, 1.1, Rules));
        Assert.AreEqual(1.1 + Rules.vent, m.reloadEndsAt, 1e-9, "La vidange voulue.");
        Assert.IsFalse(WeaponController.TryStartVent(ref m, 1.2, Rules), "Une à la fois.");
        Assert.IsFalse(WeaponController.CompleteVentIfDue(ref m, 1.1 + Rules.vent - 0.01));
        Assert.IsTrue(WeaponController.CompleteVentIfDue(ref m, 1.1 + Rules.vent));
        Assert.AreEqual(0f, m.heat, "Vidée : froide.");
        Assert.AreEqual(0.0, m.reloadEndsAt);
    }

    /// <summary>Une vidange s'interrompt comme un rechargement (sprint, taser) ; une arme surchauffée le
    /// reste alors, jusqu'à être vidée ou tout à fait refroidie : sprinter ne contourne pas la surchauffe.</summary>
    [Test]
    public void UneSurchauffeInterrompue_ResteSurchauffee_JusquAEtreVideeOuFroide()
    {
        var m = new MagazineState();
        for (int shot = 1; shot <= 4; shot++) WeaponController.TryFireHot(ref m, shot * 0.1, Rules, 0.0);
        Assert.IsTrue(m.overheated, "Garde : surchauffée.");
        m.reloadEndsAt = 0.0;   // un sprint interrompt la vidange forcée

        Assert.IsFalse(WeaponController.TryFireHot(ref m, 1.0, Rules, 0.0), "Toujours surchauffée.");
        Assert.IsTrue(WeaponController.IsOverheated(m, 2.5, Rules), "Elle refroidit sans tirer : 0,3 s de délai puis 2 s.");
        Assert.IsTrue(WeaponController.TryStartVent(ref m, 1.0, Rules));
        Assert.AreEqual(1.0 + Rules.overheatVent, m.reloadEndsAt, 1e-9, "La revider coûte la vidange forcée, pas la courte.");

        var cooled = m;
        cooled.reloadEndsAt = 0.0;
        Assert.IsFalse(WeaponController.IsOverheated(cooled, 0.4 + 0.3 + 2.0, Rules), "Tout à fait refroidie : plus surchauffée.");
        Assert.IsTrue(WeaponController.TryFireHot(ref cooled, 0.4 + 0.3 + 2.0, Rules, 0.0));
        Assert.IsFalse(cooled.overheated);
    }

    /// <summary>Le serveur compte avec une marge (ReloadCompletionTolerance) qui profite au tireur ; la
    /// chaleur, elle, se compte à l'instant exact : les deux tombent sur la même, tir après tir.</summary>
    [Test]
    public void LeTireurEtLeServeur_TombentSurLaMemeChaleur()
    {
        WeaponData fx1 = Weapon("FX-1");
        HeatRules rules = WeaponController.HeatRulesFor(fx1, false);
        var shooter = new MagazineState();
        var server = new MagazineState();
        double t = 5.0;
        int sent = 0, overheats = 0;
        for (int shot = 0; shot < 120; shot++)
        {
            t += shot % 8 == 7 ? 0.55 : 1.0 / fx1.shotsPerSecond;   // des rafales de huit
            // Le tireur n'envoie que les tirs qu'il voit partir ; le serveur les juge avec sa marge.
            if (!WeaponController.TryFireHot(ref shooter, t, rules, 0.0)) continue;
            sent++;
            Assert.IsTrue(WeaponController.TryFireHot(ref server, t, rules, WeaponController.ReloadCompletionTolerance),
                $"Tir {shot} : parti chez le tireur, refusé par le serveur.");
            Assert.AreEqual(shooter.heat, server.heat, $"Tir {shot} : deux chaleurs.");
            Assert.AreEqual(shooter.overheated, server.overheated);
            if (shooter.overheated) overheats++;
        }
        Assert.Greater(overheats, 1, "Garde : la rafale a surchauffé l'arme, plusieurs fois.");
        Assert.Less(sent, 120, "Garde : des tirs ont été refusés pendant les vidanges.");
    }

    /// <summary>Le FX-1 (précision de l'utilisateur, 2026-10-26) : le FA-07 vide ses 30 balles en 4 s, le
    /// FX-1 surchauffe après 3 s de tir continu ; mais il refroidit entre les rafales.</summary>
    [Test]
    public void LeFX1_SurchauffeApresTroisSecondesDeTirContinu_MaisRefroiditEntreLesRafales()
    {
        WeaponData fx1 = Weapon("FX-1");
        WeaponData fa07 = Weapon("FA-07");
        HeatRules rules = WeaponController.HeatRulesFor(fx1, false);
        int inARow = WeaponController.ShotsBeforeOverheat(rules, fx1.shotsPerSecond);
        float seconds = (inARow - 1) / fx1.shotsPerSecond;
        Assert.AreEqual(3f, seconds, 0.2f, $"{inARow} tirs d'affilée : {seconds:F2} s de tir continu.");
        Assert.AreEqual(4f, (fa07.magazineSize - 1) / fa07.shotsPerSecond, 0.2f, "Garde : le FA-07 vide son chargeur en 4 s.");

        // Des rafales de cinq, une demi-seconde entre elles : bien plus de tirs avant la surchauffe.
        var m = new MagazineState();
        double t = 0.0;
        int fired = 0;
        while (!m.overheated && fired < 200)
        {
            for (int i = 0; i < 5 && !m.overheated; i++, fired++)
            {
                t += 1.0 / fx1.shotsPerSecond;
                Assert.IsTrue(WeaponController.TryFireHot(ref m, t, rules, 0.0));
            }
            t += 0.5;
        }
        Assert.Greater(fired, inARow + 10, "Des rafales courtes tirent plus longtemps qu'un tir continu.");
    }

    /// <summary>Chaque arme laser surchauffe (aucune ne tire sans fin), et sa cellule grande capacité lui
    /// donne la moitié de chaleur en plus, comme un grand chargeur (GDD § 6).</summary>
    [Test]
    public void ChaqueArmeLaser_Surchauffe_EtLaGrandeCelluleTireUneMoitieDePlus()
    {
        WeaponController weapon = PrefabWeapon();
        int lasers = 0;
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            if (!data.UsesHeat) continue;
            lasers++;
            HeatRules standard = WeaponController.HeatRulesFor(data, false);
            HeatRules big = WeaponController.HeatRulesFor(data, true);
            Assert.AreEqual(standard.perShot / data.attachmentCatalog.extendedMagazineCapacity, big.perShot, 1e-6f, $"{data.weaponName} : la moitié de chaleur en plus.");
            Assert.Greater(big.vent, standard.vent, $"{data.weaponName} : la grande cellule se vide plus lentement.");
            Assert.Greater(standard.overheatVent, standard.vent, $"{data.weaponName} : la surchauffe se paie (vidange forcée plus longue).");
            int shots = WeaponController.ShotsBeforeOverheat(standard, data.shotsPerSecond);
            Assert.That(shots, Is.InRange(2, 100), $"{data.weaponName} : {shots} tirs d'affilée.");
        }
        Assert.AreEqual(4, lasers, "Les quatre prototypes laser chauffent.");
    }
}
