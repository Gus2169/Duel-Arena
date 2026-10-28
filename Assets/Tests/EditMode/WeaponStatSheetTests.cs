using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La fiche des armes à l'armurerie (2026-10-16) : les barres, ce que les accessoires y changent, et
/// ce qu'annonce l'IA de supervision.
///
/// Ce qui casse ici ne lève aucune erreur : une barre qui ne bouge pas quand on monte l'accessoire qui
/// l'aide (le joueur ne voit pas ce qu'il gagne), une échelle où rien n'est jamais plein, un accessoire
/// qui semble toucher aux dégâts (GDD § 6 : aucun ne le fait), une IA qui annonce six coups quand le
/// serveur en compte cinq (Bible § 9 : elle ne se trompe jamais).
///
/// Les valeurs des armes sont provisoires (à juger en jouant) : les tests lisent les vraies données et
/// en vérifient les relations, jamais un nombre recopié. Les écritures se vérifient sur une arme de
/// test aux valeurs connues.
/// </summary>
public class WeaponStatSheetTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static GameObject PlayerPrefab => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

    private static IReadOnlyList<WeaponData> Loadout() => PlayerPrefab.GetComponentInChildren<WeaponController>(true).Loadout;

    private static float RobotHealth() => PlayerPrefab.GetComponent<Health>().Max;

    /// <summary>Toutes les combinaisons d'accessoires qu'une place peut porter, telles que le serveur les
    /// garde (Sanitized) : viseurs, poignées, laser, chargeur.</summary>
    private static IEnumerable<WeaponAttachments> Configurations(IReadOnlyList<WeaponData> loadout, int slot)
    {
        var seen = new HashSet<string>();
        for (int sight = 0; sight <= WeaponAttachments.SightCount; sight++)
        for (int grip = 0; grip < 3; grip++)
        for (int laser = 0; laser < 2; laser++)
        for (int magazine = 0; magazine < 2; magazine++)
        {
            WeaponAttachments a = WeaponAttachments.None.With(slot, sight, (GripKind)grip, laser == 1, magazine == 1).Sanitized(loadout);
            if (seen.Add(a.ToHex())) yield return a;
        }
    }

    [Test]
    public void ChaqueBarre_TientEntreZeroEtUn_EtLeMieuxDeLaPanoplieEstPlein()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        float health = RobotHealth();
        Assert.AreEqual(WeaponController.MaxSlots, loadout.Count, "Les barres comparent les douze armes (GDD § 15).");

        foreach (WeaponStatSheet.Stat stat in WeaponStatSheet.Bars)
        {
            float best = 0f;
            int measured = 0;
            for (int slot = 0; slot < loadout.Count; slot++)
            {
                foreach (WeaponAttachments a in Configurations(loadout, slot))
                {
                    float fraction = WeaponStatSheet.Fraction(stat, loadout, slot, a, health);
                    Assert.That(fraction, Is.InRange(0f, 1f), $"{stat}, {loadout[slot].weaponName}, accessoires {a}.");
                    best = Mathf.Max(best, fraction);
                    measured++;
                }
            }
            Assert.Greater(measured, 4, "Garde : plusieurs configurations par arme.");
            Assert.AreEqual(1f, best, 1e-5f, $"{stat} : le mieux qu'on puisse avoir remplit la barre.");
        }
    }

    [Test]
    public void UnAccessoire_RemplitLaBarreDeCeQuIlAide_EtNeTouchePasAuReste()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        float health = RobotHealth();
        int checks = 0;

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            WeaponAttachments none = WeaponAttachments.None;
            float Bar(WeaponStatSheet.Stat stat, WeaponAttachments a) => WeaponStatSheet.Fraction(stat, loadout, slot, a, health);

            if (WeaponAttachments.Accepts(data, AttachmentCategory.Grip, (int)GripKind.Vertical))
            {
                WeaponAttachments grip = none.WithGrip(slot, GripKind.Vertical);
                Assert.Greater(Bar(WeaponStatSheet.Stat.Control, grip), Bar(WeaponStatSheet.Stat.Control, none), $"{data.weaponName} : la poignée verticale retient le recul.");
                Assert.AreEqual(Bar(WeaponStatSheet.Stat.HipPrecision, none), Bar(WeaponStatSheet.Stat.HipPrecision, grip), "Elle ne resserre pas la hanche.");
                Assert.AreEqual(Bar(WeaponStatSheet.Stat.Handling, none), Bar(WeaponStatSheet.Stat.Handling, grip), "Elle ne fait pas épauler plus vite.");
                checks++;
            }
            if (WeaponAttachments.Accepts(data, AttachmentCategory.Grip, (int)GripKind.Angled))
            {
                WeaponAttachments grip = none.WithGrip(slot, GripKind.Angled);
                Assert.Greater(Bar(WeaponStatSheet.Stat.Handling, grip), Bar(WeaponStatSheet.Stat.Handling, none), $"{data.weaponName} : la poignée inclinée fait épauler plus vite.");
                Assert.AreEqual(Bar(WeaponStatSheet.Stat.Control, none), Bar(WeaponStatSheet.Stat.Control, grip), "Elle ne retient pas le recul.");
                checks++;
            }
            if (WeaponAttachments.Accepts(data, AttachmentCategory.Laser, 1))
            {
                WeaponAttachments laser = none.WithLaser(slot, true);
                Assert.Greater(Bar(WeaponStatSheet.Stat.HipPrecision, laser), Bar(WeaponStatSheet.Stat.HipPrecision, none), $"{data.weaponName} : le laser resserre la hanche.");
                Assert.AreEqual(Bar(WeaponStatSheet.Stat.Control, none), Bar(WeaponStatSheet.Stat.Control, laser), "Épaulé, le laser ne change rien.");
                checks++;
            }
            if (WeaponAttachments.Accepts(data, AttachmentCategory.Magazine, 1))
            {
                WeaponAttachments magazine = none.WithMagazine(slot, true);
                Assert.Greater(Bar(WeaponStatSheet.Stat.Capacity, magazine), Bar(WeaponStatSheet.Stat.Capacity, none), $"{data.weaponName} : le grand chargeur se voit.");
                checks++;
            }

            // GDD § 6 : aucun accessoire ne touche aux dégâts, à la cadence ni au poids de l'arme.
            foreach (WeaponAttachments a in Configurations(loadout, slot))
            {
                foreach (WeaponStatSheet.Stat stat in new[] { WeaponStatSheet.Stat.Damage, WeaponStatSheet.Stat.FireRate, WeaponStatSheet.Stat.Mobility })
                    Assert.AreEqual(Bar(stat, none), Bar(stat, a), $"{data.weaponName}, {stat}, accessoires {a}.");
            }
        }
        Assert.GreaterOrEqual(checks, 8, "Garde : chaque accessoire a été vérifié sur plusieurs armes.");
    }

    [Test]
    public void LaBarreDesDegats_DitCombienDeTirsAuBustePourNeutraliserUnRobot()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        float health = RobotHealth();
        bool oneShot = false, several = false;

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            float bar = WeaponStatSheet.Fraction(WeaponStatSheet.Stat.Damage, loadout, slot, WeaponAttachments.None, health);
            int shots = WeaponStatSheet.ShotsToKill(loadout[slot], 1f, health);
            Assert.GreaterOrEqual(bar * shots, 1f - 1e-5f, $"{loadout[slot].weaponName} : {shots} tirs remplissent la barre.");
            Assert.Less(bar * (shots - 1), 1f, $"{loadout[slot].weaponName} : un tir de moins ne la remplit pas.");
            oneShot |= shots == 1;
            several |= shots > 1;
        }
        Assert.IsTrue(oneShot && several, "Garde : une arme qui tue d'un tir au buste et une qui en demande plusieurs.");
    }

    /// <summary>Bible § 9 : l'IA ne se trompe jamais. Ses nombres sont rejoués ici comme le serveur
    /// compte : les dégâts de WeaponData.DamageAt, tous les plombs d'un tir additionnés puis ôtés d'un
    /// coup, la mort à zéro (Health).</summary>
    [Test]
    public void LIA_AnnonceLesCoupsQueCompteLeServeur()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        float health = RobotHealth();
        int zonesChecked = 0;

        foreach (WeaponData data in loadout)
        {
            foreach ((HitboxZoneType zone, float multiplier) in new[]
                     { (HitboxZoneType.Torso, 1f), (HitboxZoneType.Head, data.headMultiplier), (HitboxZoneType.Legs, data.legsMultiplier) })
            {
                int announced = WeaponStatSheet.ShotsToKill(data, multiplier, health);
                float perShot = data.DamageAt(zone, 0f) * data.PelletCount;
                Assert.AreEqual(announced - 1, ShotsSurvived(perShot, health), $"{data.weaponName}, {zone} : {announced} tirs annoncés.");
                zonesChecked++;
            }

            string brief = WeaponStatSheet.Brief(data, health);
            // « Il faut 6 impacts » en début de phrase, « Jusqu'à 8 m, il faut 1 décharge » après la portée.
            StringAssert.Contains($"faut {WeaponStatSheet.ShotsToKill(data, 1f, health)} ", brief, "Le message dit le nombre de tirs au buste.");
            if (data.falloffEnd > data.falloffStart)
                StringAssert.Contains($"Jusqu'à {WeaponStatSheet.Number(data.falloffStart)} m", brief, "Il le dit à la portée des pleins dégâts.");
        }
        Assert.AreEqual(3 * WeaponController.MaxSlots, zonesChecked);
    }

    // Les tirs qu'un robot à pleine vie encaisse sans tomber, comptés comme Health.ApplyDamage.
    private static int ShotsSurvived(float perShot, float health)
    {
        int survived = 0;
        for (float current = health - perShot; current > 0f; current = Mathf.Max(0f, current - perShot)) survived++;
        return survived;
    }

    [Test]
    public void LaFiche_SEcritALaFrancaise()
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        try
        {
            data.weaponName = "Arme d'essai";
            data.damagePerHit = 18f;
            data.pelletPattern = new Vector2[8];
            data.shotsPerSecond = 1.1f;
            data.isAutomatic = false;
            data.maxRange = 35f;
            data.falloffStart = 8f;
            data.falloffEnd = 20f;
            data.minDamageMultiplier = 0.3f;
            data.headMultiplier = 1f;
            data.legsMultiplier = 1f;
            data.reloadPerRound = true;
            data.reloadDuration = 0.55f;
            data.attachmentCatalog = null;

            Assert.AreEqual("18 × 8", WeaponStatSheet.Format(WeaponStatSheet.Stat.Damage, data, 144f), "Les plombs d'une décharge.");
            Assert.AreEqual("66 coups/min", WeaponStatSheet.Format(WeaponStatSheet.Stat.FireRate, data, 1.1f));
            Assert.AreEqual("1,2° par coup", WeaponStatSheet.Format(WeaponStatSheet.Stat.Control, data, 1.2f), "La virgule française.");
            Assert.AreEqual("125 ms", WeaponStatSheet.Format(WeaponStatSheet.Stat.Handling, data, 0.125f));
            Assert.AreEqual("95 %", WeaponStatSheet.Format(WeaponStatSheet.Stat.Mobility, data, 0.95f));
            Assert.AreEqual("1,2° → 0,96°", WeaponStatSheet.FormatChange(WeaponStatSheet.Stat.Control, data, 1.2f, 0.96f));
            Assert.AreEqual("30 → 45", WeaponStatSheet.FormatChange(WeaponStatSheet.Stat.Capacity, data, 30f, 45f));
            Assert.AreEqual("8 m, puis ×0,3 à 20 m", WeaponStatSheet.EffectiveRange(data));
            Assert.AreEqual("0,55 s par cartouche", WeaponStatSheet.Reload(data, WeaponAttachments.None, 0));
            Assert.AreEqual("Coup par coup, 8 plombs", WeaponStatSheet.FireMode(data));
            Assert.AreEqual("Aucun bonus", WeaponStatSheet.Zones(data));
            Assert.AreEqual("Arme d'essai. Coup par coup, 8 plombs, 66 coups par minute. Jusqu'à 8 m, il faut 1 décharge complète "
                            + "au buste pour neutraliser un robot.", WeaponStatSheet.Brief(data, 100f));

            data.pelletPattern = new Vector2[0];
            data.damagePerHit = 17f;
            data.isAutomatic = true;
            data.falloffEnd = 0f;
            data.maxRange = 100f;
            data.headMultiplier = 1.4f;
            data.legsMultiplier = 0.6f;
            data.reloadPerRound = false;
            data.reloadDuration = 2.2f;
            Assert.AreEqual("17", WeaponStatSheet.Format(WeaponStatSheet.Stat.Damage, data, 17f));
            Assert.AreEqual("100 m", WeaponStatSheet.EffectiveRange(data), "Sans baisse : la portée de l'arme.");
            Assert.AreEqual("2,2 s", WeaponStatSheet.Reload(data, WeaponAttachments.None, 0));
            Assert.AreEqual("Tête ×1,4, jambes ×0,6", WeaponStatSheet.Zones(data));
            Assert.AreEqual("Arme d'essai. Automatique, 66 coups par minute. Il faut 6 impacts au buste pour neutraliser un robot, "
                            + "5 à la tête, 10 aux jambes.", WeaponStatSheet.Brief(data, 100f));

            // Une arme qui se charge (le FX-2) : l'IA compte des tirs chargés à fond.
            data.chargeTime = 1f;
            data.damagePerHit = 100f;
            data.shotsPerSecond = 2.5f;
            data.isAutomatic = false;
            data.headMultiplier = 1.5f;
            Assert.AreEqual("Tir chargé en 1 s", WeaponStatSheet.FireMode(data));
            Assert.AreEqual("Arme d'essai. Tir chargé en 1 s, 150 coups par minute. Il faut 1 impact chargé à fond au buste ou "
                            + "à la tête pour neutraliser un robot, 2 aux jambes.", WeaponStatSheet.Brief(data, 100f));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }
}
