using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les règles pures de la prise des mains sur l'arme (WeaponGrip, 2026-10-17). Le reste (les douze
/// modèles, le rechargement, les poignées) se vérifie avec le vrai Animator : WeaponGripTests.
/// </summary>
public class WeaponGripRulesTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    /// <summary>Le poing du gant, pris pour un ellipsoïde : dans l'axe, il s'étend de sa demi-dimension ; en
    /// biais, moins loin que le coin de sa boîte, plus loin que sa plus petite demi-dimension.</summary>
    [Test]
    public void LePoing_SEtendSelonLaDirection_CommeUnEllipsoide()
    {
        var half = new Vector3(0.071f, 0.056f, 0.046f);
        Assert.AreEqual(0.071f, WeaponGrip.FistExtent(Vector3.right, half), 1e-6f);
        Assert.AreEqual(0.056f, WeaponGrip.FistExtent(Vector3.down, half), 1e-6f);
        Assert.AreEqual(0.046f, WeaponGrip.FistExtent(Vector3.forward, half), 1e-6f);

        Vector3 diagonal = new Vector3(1f, -1f, 1f).normalized;
        float extent = WeaponGrip.FistExtent(diagonal, half);
        float corner = Vector3.Dot(diagonal, new Vector3(half.x, -half.y, half.z));
        Assert.Less(extent, corner, "En biais, un ellipsoïde ne va pas jusqu'au coin de sa boîte.");
        Assert.Greater(extent, half.z, "En biais, il va plus loin que sa plus petite demi-dimension.");
    }

    /// <summary>La rotation d'une prise met la paume de la main gauche (+Z de son os) là où elle doit
    /// regarder, et ses doigts (+Y) au plus près de leur direction.</summary>
    [Test]
    public void UnePrise_TourneLaPaumeEtLesDoigts()
    {
        var settings = new WeaponGrip.Settings();
        foreach (WeaponGrip.HandHold hold in new[] { settings.handguard, settings.verticalGrip, settings.angledGrip })
        {
            Quaternion r = hold.Rotation;
            Assert.Less(Vector3.Angle(r * Vector3.forward, hold.palm), 0.01f, "La paume ne regarde pas où la prise le dit.");
            Vector3 fingers = Vector3.ProjectOnPlane(hold.fingers, hold.palm);
            Assert.Less(Vector3.Angle(r * Vector3.up, fingers), 0.01f, "Les doigts ne vont pas où la prise le dit.");
        }
        // Sous le garde-main, la paume regarde vers le haut de l'arme ; contre une poignée verticale, vers sa droite.
        Assert.Greater((settings.handguard.Rotation * Vector3.forward).y, 0.9f);
        Assert.Greater((settings.verticalGrip.Rotation * Vector3.forward).x, 0.9f);
    }

    /// <summary>Chaque châssis a sa paume dans le préfab joueur (2026-10-27) : un châssis ajouté sans la sienne
    /// prendrait celle du CT-01 et flotterait sous le garde-main, ou s'y enfoncerait (WeaponGripTests la mesure
    /// sur sa peau). Un rang inconnu prend celle du CT-01.</summary>
    [Test]
    public void ChaqueChassis_ASaPaume()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        WeaponGrip.Settings settings = prefab.GetComponent<PlayerAnimator>().HandGrip;
        Assert.AreEqual(RobotChassis.Count, settings.palmPoints.Length, "Il faut une paume par châssis, dans l'ordre de ChassisId.");
        // Les paumes mesurées : charnue (Y Bot), une plaque (CT-01), une plaque mince (OS-02).
        Assert.Greater(settings.PalmPoint(ChassisId.Prototype).z, settings.PalmPoint(ChassisId.CT01).z);
        Assert.Greater(settings.PalmPoint(ChassisId.CT01).z, settings.PalmPoint(ChassisId.OS02).z);
        Assert.AreEqual(settings.PalmPoint(ChassisId.CT01), settings.PalmPoint((ChassisId)RobotChassis.Count), "Un rang inconnu prend la paume du CT-01.");
    }
}
