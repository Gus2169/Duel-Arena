using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les rôles des douze armes, choisis par l'utilisateur le 2026-10-26 (page « Refonte des armes », GDD
/// § 6). Les chiffres de départ se régleront dans l'éditeur des armes : ces tests ne fixent pas les
/// valeurs, mais ce qui fait le rôle de chaque arme. Dans chaque famille, le stock est la référence, la
/// série 30 prend un risque (plus rapide ou plus lourde), le prototype laser a une mécanique à lui.
///
/// Ce qui casse ici ne lève aucune erreur : un FA-30 qui garde ses dégâts au loin (il écraserait le
/// FA-07), une carabine à levier qui tue d'une balle au buste (c'est le sniper), un slug à plusieurs
/// plombs, un éventail rond, un revolver qui ne tue plus à la tête. Lus dans les VRAIS assets.
/// </summary>
public class WeaponArsenalTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const float Health = 100f;

    private static WeaponData Weapon(string designation)
    {
        WeaponController weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true);
        for (int slot = 0; slot < weapon.SlotCount; slot++)
            if (weapon.DataOf(slot).Designation == designation) return weapon.DataOf(slot);
        Assert.Fail($"{designation} n'est pas dans l'arsenal.");
        return null;
    }

    private static int ShotsToKill(WeaponData data, HitboxZoneType zone, float distance)
        => Mathf.CeilToInt(Health / data.DamageAt(zone, distance) - 1e-4f);

    /// <summary>Ce que monte le regard sur une rafale de `shots` coups, sans son retour : le motif de
    /// recul coup par coup et sa force (RecoilPattern, 2026-10-26).</summary>
    private static float Climb(WeaponData data, int shots)
    {
        var points = new System.Collections.Generic.List<Vector2>();
        RecoilPattern.Burst(data, shots + 1, Vector2.one, false, points);
        return points[shots].y;
    }

    [Test]
    public void LeFA30_TirePlusVite_EpauleVite_MaisPerdSesDegatsAuLoin()
    {
        WeaponData fa07 = Weapon("FA-07"), fa30 = Weapon("FA-30");
        Assert.IsTrue(fa30.isAutomatic);
        Assert.Greater(fa30.shotsPerSecond, fa07.shotsPerSecond, "Cadence élevée.");
        Assert.Less(fa30.magazineSize, fa07.magazineSize, "Moins de balles.");
        Assert.Less(fa30.aimTime, fa07.aimTime, "Rapide à épauler.");
        float ttkNear07 = (ShotsToKill(fa07, HitboxZoneType.Torso, 10f) - 1) / fa07.shotsPerSecond;
        float ttkNear30 = (ShotsToKill(fa30, HitboxZoneType.Torso, 10f) - 1) / fa30.shotsPerSecond;
        Assert.Less(ttkNear30, ttkNear07, "De près, il tue plus vite que la référence.");
        Assert.Greater(ShotsToKill(fa30, HitboxZoneType.Torso, 50f), ShotsToKill(fa07, HitboxZoneType.Torso, 50f),
            "Au loin, il lui faut plus de balles que la référence.");
    }

    [Test]
    public void LeFX1_EstLeFA07EnRayons_AvecMoinsDeRecul()
    {
        WeaponData fa07 = Weapon("FA-07"), fx1 = Weapon("FX-1");
        Assert.IsTrue(fx1.isAutomatic);
        Assert.IsTrue(fx1.shotIsBeam, "Des rayons.");
        Assert.AreEqual(fa07.shotsPerSecond, fx1.shotsPerSecond, "La cadence du FA-07.");
        Assert.AreEqual(ShotsToKill(fa07, HitboxZoneType.Torso, 20f), ShotsToKill(fx1, HitboxZoneType.Torso, 20f), "Ses dégâts.");
        Assert.Less(Climb(fx1, 20), Climb(fa07, 20), "Moins de recul que le FA-07 : sa rafale monte moins.");
        Assert.IsTrue(fx1.UsesHeat, "Il chauffe au lieu de compter des balles (voir WeaponHeatTests).");
    }

    [Test]
    public void LeFP30_TueEnDeuxBallesAuBuste_UneALaTete_PlusVifQueLeFP12()
    {
        WeaponData fp12 = Weapon("FP-12"), fp30 = Weapon("FP-30");
        Assert.AreEqual(2, ShotsToKill(fp30, HitboxZoneType.Torso, 40f), "Deux balles au buste.");
        Assert.AreEqual(1, ShotsToKill(fp30, HitboxZoneType.Head, 40f), "Une à la tête.");
        Assert.Greater(fp30.shotsPerSecond, fp12.shotsPerSecond, "Une cadence plus élevée.");
        Assert.Less(fp30.raiseTime, fp12.raiseTime, "Plus mobile : il se sort plus vite...");
        Assert.Greater(fp30.moveSpeedMultiplier, fp12.moveSpeedMultiplier, "... et pèse moins.");
        Assert.IsTrue(fp30.reloadPerRound, "Chargé cartouche par cartouche.");
        Assert.AreEqual(4, fp30.magazineSize, "Quatre cartouches dans son magasin (précision de l'utilisateur).");
        Assert.Greater(fp30.scopeMagnification, 1f, "Sa lunette compacte.");
        Assert.Less(fp30.scopeMagnification, fp12.scopeMagnification, "Moins forte que celle du FP-12.");
    }

    [Test]
    public void LeFX2_TueAuBuste_ChargeAFond()
    {
        WeaponData fx2 = Weapon("FX-2");
        Assert.AreEqual(1, ShotsToKill(fx2, HitboxZoneType.Torso, 60f), "Chargé à fond, il tue au buste.");
        Assert.IsTrue(fx2.shotIsBeam);
        Assert.IsTrue(fx2.UsesHeat);
        Assert.Greater(fx2.scopeMagnification, 1f, "Sa lunette.");

        // Le tir chargé (GDD § 6) : on tient la gâchette pour charger, on la lâche pour tirer.
        Assert.IsTrue(fx2.UsesCharge, "Il se charge.");
        Assert.Less(fx2.DamageAt(HitboxZoneType.Torso, 60f, 0.99f), Health, "Pas tout à fait chargé, il ne tue plus au buste.");
        Assert.Less(fx2.DamageAt(HitboxZoneType.Torso, 60f, 0f), Health / 2f, "Relâché tôt, il blesse moins.");
        Assert.Greater(fx2.DamageAt(HitboxZoneType.Torso, 60f, 0f), 0f, "Mais il blesse.");
        Assert.GreaterOrEqual(fx2.DamageAt(HitboxZoneType.Head, 60f, 0.75f), Health, "À la tête, une charge partielle suffit.");
        Assert.IsTrue(Vector2.zero == fx2.PelletOffset(0, false), "Une seule balle : aucun hasard dans la charge.");

        // Un tir chauffe autant, chargé ou non : trois tirs d'affilée, pleins ou pas, puis la surchauffe.
        var heat = WeaponController.HeatRulesFor(fx2, false);
        Assert.AreEqual(3, WeaponController.ShotsBeforeOverheat(heat, fx2.shotsPerSecond), "Trois tirs pleins d'affilée.");
    }

    [Test]
    public void LeSlugDuFAP30_EstUneSeuleBalleLourde_PourLaMoyenneDistance()
    {
        WeaponData fap03 = Weapon("FAP-03"), fap30 = Weapon("FAP-30");
        Assert.AreEqual(1, fap30.PelletCount, "Une seule balle.");
        Assert.Greater(fap30.hipSpread, 0f, "Sans gerbe, il a un cône à la hanche : il faut épauler.");
        Assert.AreEqual(1, ShotsToKill(fap30, HitboxZoneType.Torso, 12f), "Une balle au buste à moyenne distance.");
        Assert.AreEqual(2, ShotsToKill(fap30, HitboxZoneType.Torso, 25f), "Deux au-delà.");
        Assert.AreEqual(1, ShotsToKill(fap30, HitboxZoneType.Head, 25f), "Une à la tête.");
        float pump = fap03.PelletCount * fap03.DamageAt(HitboxZoneType.Torso, 15f);
        Assert.Less(pump, Health, "Garde : à 15 m, même toute la gerbe du FAP-03 ne tue pas.");
        Assert.IsTrue(fap30.reloadPerRound, "Un fusil à pompe : cartouche par cartouche.");
    }

    [Test]
    public void LeFX3_TireUnEventail_TresLargeDeCote_FinEnHauteur()
    {
        WeaponData fx3 = Weapon("FX-3");
        Assert.Greater(fx3.PelletCount, 1, "Une gerbe de rayons.");
        float width = 0f, height = 0f;
        for (int i = 0; i < fx3.PelletCount; i++)
        {
            Vector2 offset = fx3.PelletOffset(i, false);
            width = Mathf.Max(width, Mathf.Abs(offset.x));
            height = Mathf.Max(height, Mathf.Abs(offset.y));
        }
        Assert.Greater(width, 5f * height, "Très large de côté, fine en hauteur (l'éventail).");
        Assert.Less(fx3.aimPatternScale, 1f, "Épaulée, elle se resserre.");
        Assert.AreEqual(fx3.PelletOffset(3, false), fx3.PelletOffset(3, false), "Toujours la même (GDD § 3).");
        Assert.IsTrue(fx3.shotIsBeam);
        Assert.IsTrue(fx3.UsesHeat);
    }

    [Test]
    public void LeRV30_TueDUneBalleALaTete_EtRechargeLentement()
    {
        WeaponData pa05 = Weapon("PA-05"), rv30 = Weapon("RV-30");
        Assert.AreEqual(1, ShotsToKill(rv30, HitboxZoneType.Head, 15f), "Une balle à la tête.");
        Assert.Greater(ShotsToKill(rv30, HitboxZoneType.Torso, 15f), 1, "Mais pas au buste.");
        Assert.AreEqual(6, rv30.magazineSize, "Six coups.");
        Assert.Greater(rv30.reloadDuration, pa05.reloadDuration, "Il recharge plus lentement que le pistolet.");
        Assert.Greater(RecoilPattern.Kick(rv30, 0).magnitude, RecoilPattern.Kick(pa05, 0).magnitude, "Un recul fort : son coup pousse plus que celui du pistolet.");
        Assert.IsFalse(rv30.shotIsBeam, "Une arme à balles : la flamme de bouche.");
    }

    [Test]
    public void LeFX4_ChauffeAuLieuDeRecharger_EtTireAuDoigt()
    {
        WeaponData fx4 = Weapon("FX-4");
        Assert.IsFalse(fx4.isAutomatic, "Au doigt : un tir par appui.");
        Assert.IsTrue(fx4.UsesHeat, "Il chauffe au lieu de se recharger.");
        Assert.IsTrue(fx4.shotIsBeam, "Son rayon rouge.");
        Assert.Greater(fx4.shotsPerSecond, Weapon("PA-05").shotsPerSecond, "Sa cadence n'est bornée que par la limite du serveur.");
    }
}
