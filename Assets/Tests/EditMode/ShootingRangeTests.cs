using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Le stand de tir (2026-10-16) : des mannequins qui se relèvent quand on cesse de tirer, qui ont la
/// vie et la surface touchable d'un robot, et une scène qu'on atteint depuis le menu.
///
/// Ce qui casse ici ne lève aucune erreur : un mannequin qui ne se relève jamais (ou pendant qu'on le
/// mitraille), un mannequin plus gros ou plus petit qu'un robot (les essais d'armes y mentiraient), un
/// stand absent de la Build (le bouton du menu ne mène nulle part).
/// </summary>
public class ShootingRangeTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string DummyPrefabPath = "Assets/_ProjectArena/Stand/Mannequin.prefab";
    private const string ScenePath = "Assets/_ProjectScenes/StandDeTir.unity";

    [Test]
    public void UnMannequin_SeReleve_QuandOnCesseDeLeToucher()
    {
        Assert.IsFalse(TrainingDummy.RegenDue(100f, 100f, 0.0, 50.0, 3f), "Plein, rien à soigner.");
        Assert.IsFalse(TrainingDummy.RegenDue(40f, 100f, 10.0, 12.0, 3f), "Touché il y a 2 s : il attend.");
        Assert.IsTrue(TrainingDummy.RegenDue(40f, 100f, 10.0, 13.0, 3f), "3 s sans être touché : il se relève.");
        Assert.IsTrue(TrainingDummy.RegenDue(0f, 100f, 10.0, 13.5, 3f), "Neutralisé aussi.");
        Assert.IsFalse(TrainingDummy.RegenDue(40f, 100f, double.NaN, 13.0, 3f), "Un instant invalide ne soigne rien.");
    }

    /// <summary>Neutralisé, un mannequin tombe en arrière, raide, et se relève quand il retrouve sa vie
    /// (2026-10-27, demandé par l'utilisateur).</summary>
    [Test]
    public void UnMannequinNeutralise_TombeEnArriere_PuisSeReleve()
    {
        Assert.AreEqual(0f, TrainingDummy.FallAngle(false, 10f), "Debout, il ne penche pas.");
        Assert.AreEqual(0f, TrainingDummy.FallAngle(true, 0f), "La chute part de debout.");
        Assert.Less(TrainingDummy.FallAngle(true, TrainingDummy.FallTime * 0.5f), 45f, "Elle accélère : à mi-temps, moins de la moitié du chemin.");
        Assert.AreEqual(90f, TrainingDummy.FallAngle(true, TrainingDummy.FallTime), 1e-3f, "Couché au sol au bout de la chute.");
        float bounce = TrainingDummy.FallAngle(true, TrainingDummy.FallTime + TrainingDummy.BounceTime * 0.5f);
        Assert.Less(bounce, 90f, "Il rebondit un peu sur le sol...");
        Assert.Greater(bounce, 90f - TrainingDummy.BounceAngle - 0.01f, "... à peine.");
        Assert.AreEqual(90f, TrainingDummy.FallAngle(true, 3f), 1e-3f, "Puis il reste à terre.");

        Assert.AreEqual(90f, TrainingDummy.FallAngle(false, 0f), 1e-3f, "Le relevé part du sol.");
        Assert.AreEqual(0f, TrainingDummy.FallAngle(false, TrainingDummy.RiseTime), 1e-3f, "Debout au bout du relevé.");
        Assert.AreEqual(0f, TrainingDummy.FallAngle(true, float.NaN), "Un instant invalide : comme au départ de la chute.");
    }

    [Test]
    public void LeBilan_DitLeTempsEtLesImpacts_ALaFrancaise()
    {
        Assert.AreEqual("Neutralisé en 0,71 s · 6 impacts", ShootingRangeHud.SeriesReport(0.7149f, 6));
        Assert.AreEqual("Neutralisé d'un coup · 1 impact", ShootingRangeHud.SeriesReport(0f, 1));
        Assert.AreEqual("tête", ShootingRangeHud.ZoneLabel(HitboxZoneType.Head));
        Assert.AreEqual("jambes", ShootingRangeHud.ZoneLabel(HitboxZoneType.Legs));
        Assert.AreEqual("buste", ShootingRangeHud.ZoneLabel(HitboxZoneType.Torso));
    }

    /// <summary>Le mannequin est touché comme un robot : sa vie, la géométrie de sa surface touchable et
    /// la hauteur de sa tête sont celles du préfab joueur ; son corps n'arrête pas les balles.</summary>
    [Test]
    public void LeMannequin_ALaVieEtLaSurfaceTouchableDUnRobot()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject dummy = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
        Assert.IsNotNull(dummy, "Le préfab du mannequin (Duel Arena > Stand de tir > Construire le stand).");

        Assert.AreEqual(player.GetComponent<Health>().Max, dummy.GetComponent<Health>().Max, "La vie d'un robot.");
        string playerBody = EditorJsonUtility.ToJson(player.GetComponentInChildren<PlayerHitbox>(true));
        string dummyBody = EditorJsonUtility.ToJson(dummy.GetComponentInChildren<PlayerHitbox>(true));
        Assert.AreEqual(playerBody, dummyBody, "La surface touchable d'un robot, posture par posture.");
        float eye = new SerializedObject(player.GetComponent<PlayerLocomotion>()).FindProperty("standingProfile.cameraHeight").floatValue;
        Assert.AreEqual(eye, dummy.GetComponent<TrainingDummy>().EyeHeight, 1e-5f, "La tête à la hauteur de celle d'un robot debout.");

        WeaponData rifle = player.GetComponentInChildren<WeaponController>(true).Loadout[0];
        foreach (Collider c in dummy.GetComponents<Collider>())
            Assert.AreEqual(0, rifle.worldMask.value & (1 << c.gameObject.layer), "Son corps n'arrête pas les balles : seule sa surface touchable compte.");
        Assert.IsNotNull(dummy.GetComponent<Unity.Netcode.NetworkObject>(), "Sa vie est tenue par le serveur.");
    }

    [Test]
    public void LeStand_EstDansLaBuild_AvecSesMannequinsEtSonPosteDeTir()
    {
        Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled), "Le menu y mène : la scène est dans la Build.");
        Assert.AreEqual(ShootingRange.SceneName, System.IO.Path.GetFileNameWithoutExtension(ScenePath));

        string yaml = System.IO.File.ReadAllText(ScenePath);
        string dummyGuid = AssetDatabase.AssetPathToGUID(DummyPrefabPath);
        Assert.GreaterOrEqual(System.Text.RegularExpressions.Regex.Matches(yaml, "m_SourcePrefab: {fileID: 100100000, guid: " + dummyGuid).Count, 6,
            "Des mannequins à plusieurs distances.");
        foreach (string type in new[] { "PlayerSpawnPoints", "ShootingRange", "ShootingRangePanel", "ShootingRangeHud", "ShootingRangeBootstrap" })
            Assert.IsTrue(yaml.Contains("guid: " + ScriptGuid(type)), $"Le stand a son {type}.");

        // Chaque objet réseau de la scène a son propre identifiant : enregistrés avec celui du préfab,
        // les mannequins faisaient échouer le chargement du stand (2026-10-16).
        var ids = System.Text.RegularExpressions.Regex.Matches(yaml, @"propertyPath: GlobalObjectIdHash\s+value: (\d+)")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
        ids.AddRange(System.Text.RegularExpressions.Regex.Matches(yaml, @"\n  GlobalObjectIdHash: (\d+)")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value));
        Assert.GreaterOrEqual(ids.Count, 8, "Garde : les identifiants des mannequins et du stand sont lus.");
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), $"Deux objets réseau du stand partagent un identifiant : {string.Join(", ", ids)}.");
    }

    private static string ScriptGuid(string type) => AssetDatabase.FindAssets($"{type} t:MonoScript")
        .First(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == type);
}
