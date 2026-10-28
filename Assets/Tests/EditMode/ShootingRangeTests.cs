using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Le stand de tir (2026-10-16) : des mannequins qui se relèvent quand on cesse de tirer, qui ont la
/// vie et la surface touchable d'un robot, et une scène qu'on atteint depuis le menu.
///
/// Ce qui casse ici ne lève aucune erreur : un mannequin qui ne se relève jamais (ou pendant qu'on le
/// mitraille), un mannequin plus gros ou plus petit qu'un robot (les essais d'armes y mentiraient), un
/// stand absent de la Build (le bouton du menu ne mène nulle part), un mannequin caché derrière un autre.
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

    /// <summary>
    /// Les mannequins du stand (2026-10-28 : dispersés d'un mur à l'autre, demandé par l'utilisateur : « ils sont tous au
    /// centre, un peu en ligne ») se voient tous du poste du milieu, à la ligne de tir : le buste de chaque mannequin à
    /// découvert, sans mur, cloison, pilier ni barricade devant, et sans un autre mannequin à moins d'un mètre de la ligne
    /// de visée ; ceux des barricades, la tête seule. Ils occupent toute la largeur du stand, chacun à sa distance, et
    /// tiennent debout hors des murs.
    /// </summary>
    [Test]
    public void LesMannequinsDuStand_SeVoientTousDuPosteDuMilieu_EtOccupentSaLargeur()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Physics.SyncTransforms();
            List<TrainingDummy> dummies = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TrainingDummy>(true))
                .Where(d => d.Regenerates).ToList();
            Assert.AreEqual(ShootingRangeBuilder.OpenDummies.Length + ShootingRangeBuilder.CoveredDummies.Length, dummies.Count, "Les mannequins du stand.");
            int world = LayerMask.GetMask("Default");
            Vector3 eye = new Vector3(ShootingRangeBuilder.FiringPost.x, 1.65f, -0.4f);
            int covered = 0;
            foreach (TrainingDummy dummy in dummies)
            {
                Vector3 feet = dummy.transform.position;
                bool behindCover = ShootingRangeBuilder.CoveredDummies.Any(c => Vector2.Distance(c, new Vector2(feet.x, feet.z)) < 0.01f);
                Vector3 torso = feet + Vector3.up * 1.2f, head = feet + Vector3.up * 1.62f;
                bool torsoSeen = !Physics.Linecast(eye, torso, world, QueryTriggerInteraction.Ignore);
                if (behindCover)
                {
                    covered++;
                    Assert.IsFalse(torsoSeen, $"{dummy.name} : la barricade cache son buste.");
                    Assert.IsFalse(Physics.Linecast(eye, head, world, QueryTriggerInteraction.Ignore), $"{dummy.name} : sa tête dépasse de la barricade.");
                }
                else Assert.IsTrue(torsoSeen, $"{dummy.name} : son buste se voit du poste du milieu.");
                Vector3 aim = behindCover ? head : torso;
                foreach (TrainingDummy other in dummies)
                {
                    if (other != dummy)
                        Assert.Greater(LineClearance(eye, aim, other.transform.position), 1f, $"{other.name} est devant {dummy.name}.");
                }
                // Tourné vers le poste de tir, debout hors des murs.
                Vector3 toPost = ShootingRangeBuilder.FiringPost - feet;
                Assert.Less(Vector3.Angle(dummy.transform.forward, new Vector3(toPost.x, 0f, toPost.z)), 1f, $"{dummy.name} regarde le poste de tir.");
                Assert.IsFalse(Physics.CheckCapsule(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.45f, 0.3f, world, QueryTriggerInteraction.Ignore),
                               $"{dummy.name} tient debout hors des murs.");
            }
            Assert.AreEqual(ShootingRangeBuilder.CoveredDummies.Length, covered, "Garde : les mannequins couverts sont trouvés.");

            // Dispersés : ils vont d'un mur à l'autre, chacun à sa distance.
            Rect stand = StandDecorBuilder.StandRoom;
            Assert.GreaterOrEqual(dummies.Count(d => d.transform.position.x < stand.xMin + 7f), 4, "Des mannequins à gauche.");
            Assert.GreaterOrEqual(dummies.Count(d => d.transform.position.x > stand.xMax - 7f), 4, "Des mannequins à droite.");
            Assert.AreEqual(dummies.Count, dummies.Select(d => Mathf.RoundToInt(d.transform.position.z)).Distinct().Count(), "Chaque mannequin à sa distance.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>De combien la ligne de visée de `eye` à `target` passe loin (à plat) de l'axe d'un mannequin debout en
    /// `feet`, là où elle le croise plus bas que le haut de sa tête (1,85 m) ; l'infini si elle ne le croise pas entre l'œil
    /// et la cible.</summary>
    private static float LineClearance(Vector3 eye, Vector3 target, Vector3 feet)
    {
        Vector2 a = new(eye.x, eye.z), ab = new Vector2(target.x, target.z) - a, c = new(feet.x, feet.z);
        float t = Vector2.Dot(c - a, ab) / ab.sqrMagnitude;
        if (t <= 0f || t >= 0.98f || Mathf.Lerp(eye.y, target.y, t) >= feet.y + 1.85f) return float.PositiveInfinity;
        return Vector2.Distance(a + ab * t, c);
    }

    private static string ScriptGuid(string type) => AssetDatabase.FindAssets($"{type} t:MonoScript")
        .First(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == type);
}
