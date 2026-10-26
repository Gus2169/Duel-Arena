using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Les châssis du robot (2026-10-26) : le CT-01 remplace le Y Bot, qui reste au choix (le prototype), et l'OS-02.
///
/// Ce qui casse ici ne lève aucune erreur : un robot de Blender monté sur d'autres os que ceux du Y Bot (il se déforme
/// dès la première animation), deux châssis allumés l'un dans l'autre, un choix qui se perd entre le menu et le serveur,
/// la peinture du joueur sur toutes les pièces d'un robot au lieu de ses seules plaques, ou les yeux d'une épave qui se
/// rallument.
/// </summary>
public class RobotChassisTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    // Les pièces des robots de Blender sous « Model » (RobotChassisBuilder).
    private static readonly string[] BlenderParts = { "CT01", "OS02" };
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color32 Orange = new Color32(255, 106, 19, 255);

    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created) if (o != null) Object.DestroyImmediate(o);
        created.Clear();
    }

    private static GameObject Prefab()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
        return prefab;
    }

    // Une copie du « Model » du préfab joueur, dans la scène de test.
    private Transform Model()
    {
        var copy = Object.Instantiate(Prefab().transform.Find("Model").gameObject);
        created.Add(copy);
        return copy.transform;
    }

    private static void AssertColor(Color expected, Color actual, string message)
    {
        Assert.AreEqual(expected.r, actual.r, 1e-3f, message);
        Assert.AreEqual(expected.g, actual.g, 1e-3f, message);
        Assert.AreEqual(expected.b, actual.b, 1e-3f, message);
    }

    private static SkinnedMeshRenderer Part(Transform model, string name)
    {
        Transform part = model.Find(name);
        Assert.IsNotNull(part, $"« Model » n'a pas de pièce « {name} ».");
        return part.GetComponent<SkinnedMeshRenderer>();
    }

    [Test]
    public void LeModeleDuJoueur_PorteLesTroisChassis_SurLeSqueletteDuYBot_EtMontreLeCT01()
    {
        GameObject prefab = Prefab();
        Transform model = prefab.transform.Find("Model");
        SkinnedMeshRenderer body = Part(model, "Alpha_Surface");
        Assert.IsTrue(Part(model, "CT01").enabled && !Part(model, "OS02").enabled && !body.enabled && !Part(model, "Alpha_Joints").enabled,
                      "Le CT-01 est le châssis montré par défaut.");
        Assert.IsNotNull(prefab.GetComponent<PlayerChassis>(), "Le châssis de chaque joueur est publié par le serveur.");

        // Les mêmes os que le Y Bot, avec les mêmes matrices de liaison : chaque robot suit chaque animation sans se déformer.
        var bindposes = new Dictionary<Transform, Matrix4x4>();
        for (int i = 0; i < body.bones.Length; i++) bindposes[body.bones[i]] = body.sharedMesh.bindposes[i];
        foreach (string name in BlenderParts)
        {
            SkinnedMeshRenderer part = Part(model, name);
            CollectionAssert.Contains(prefab.GetComponent<PlayerPaint>().PaintedRenderers, part, $"« {name} » prend la peinture du joueur.");
            int shared = 0;
            for (int i = 0; i < part.bones.Length; i++)
            {
                Assert.IsTrue(part.bones[i].IsChildOf(model), $"L'os « {part.bones[i].name} » de « {name} » est celui du préfab.");
                if (!bindposes.TryGetValue(part.bones[i], out Matrix4x4 expected)) continue;
                shared++;
                Matrix4x4 actual = part.sharedMesh.bindposes[i];
                for (int k = 0; k < 16; k++)
                    Assert.AreEqual(expected[k], actual[k], 1e-4f, $"La liaison de « {part.bones[i].name} » de « {name} » est celle du Y Bot.");
            }
            Assert.Greater(shared, 40, $"Garde : « {name} » partage le squelette du Y Bot.");
        }
    }

    [Test]
    public void ChoisirUnChassis_NAllumeQueSesPieces_SansToucherAuxArmes()
    {
        Transform model = Model();
        var others = new List<Renderer>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (RobotChassis.OwnerOf(r.name) < 0) others.Add(r);
        }
        Assert.IsNotEmpty(others, "Garde : le modèle porte aussi les armes et le gant.");
        var before = others.ConvertAll(r => r.enabled);

        RobotChassis.Apply(model, ChassisId.Prototype);
        Assert.IsTrue(Part(model, "Alpha_Surface").enabled && Part(model, "Alpha_Joints").enabled, "Le prototype : le Y Bot.");
        Assert.IsFalse(Part(model, "CT01").enabled || Part(model, "OS02").enabled, "Un seul châssis à la fois.");

        RobotChassis.Apply(model, ChassisId.OS02);
        Assert.IsTrue(Part(model, "OS02").enabled, "L'OS-02.");
        Assert.IsFalse(Part(model, "CT01").enabled || Part(model, "Alpha_Surface").enabled || Part(model, "Alpha_Joints").enabled);

        RobotChassis.Apply(model, ChassisId.CT01);
        Assert.IsTrue(Part(model, "CT01").enabled);
        Assert.IsFalse(Part(model, "OS02").enabled || Part(model, "Alpha_Surface").enabled || Part(model, "Alpha_Joints").enabled);
        CollectionAssert.AreEqual(before, others.ConvertAll(r => r.enabled), "Les armes et le gant ne changent pas.");
    }

    [Test]
    public void UnRangInconnu_DonneLeCT01()
    {
        Assert.AreEqual(ChassisId.Prototype, RobotChassis.Sanitized(1));
        Assert.AreEqual(ChassisId.OS02, RobotChassis.Sanitized(2));
        Assert.AreEqual(ChassisId.CT01, RobotChassis.Sanitized(0));
        Assert.AreEqual(ChassisId.CT01, RobotChassis.Sanitized(-1), "Rien de choisi.");
        Assert.AreEqual(ChassisId.CT01, RobotChassis.Sanitized(RobotChassis.Count), "Un client modifié.");
        Assert.AreEqual(ChassisId.CT01, RobotChassis.Sanitized(int.MaxValue));
    }

    [Test]
    public void LeChassisChoisi_VoyageDansLaDemandeDeConnexion()
    {
        byte[] payload = SessionFlow.BuildPayload("0.1.5", "Héloïse", WeaponLooks.Default, WeaponAttachments.None, 3, ChassisId.Prototype);
        Assert.IsTrue(SessionFlow.ParsePayload(payload, out _, out string name, out _, out _, out int paint, out int chassis));
        Assert.AreEqual((int)ChassisId.Prototype, chassis);
        Assert.AreEqual(3, paint, "Garde : la couleur est lue comme avant.");
        Assert.AreEqual("Héloïse", name);

        // Une build d'avant le choix du châssis n'envoie que cinq lignes : aucun choix, donc le CT-01.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.4\nUntel\n00000000\n00000000\n2"),
                                               out _, out _, out _, out _, out paint, out chassis));
        Assert.AreEqual(-1, chassis);
        Assert.AreEqual(ChassisId.CT01, RobotChassis.Sanitized(chassis));
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.5\nUntel\n00000000\n00000000\n2\nY"),
                                               out _, out _, out _, out _, out _, out chassis));
        Assert.AreEqual(-1, chassis, "Illisible : aucun choix.");
    }

    [Test]
    public void SurLesRobotsDeBlender_SeulesLesPlaquesPrennentLaCouleur_EtLesLueursLaRendent()
    {
        var block = new MaterialPropertyBlock();
        foreach (string part in BlenderParts)
        {
            SkinnedMeshRenderer robot = Part(Model(), part);
            PlayerPaint.PaintPart(robot, Orange, block, true);
            Material[] materials = robot.sharedMaterials;
            int painted = 0, glowing = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                robot.GetPropertyBlock(block, i);
                string name = materials[i].name;
                if (name.Contains(PlayerPaint.PaintSlot))
                {
                    painted++;
                    AssertColor(Orange, block.GetColor(BaseColorId), $"« {name} » prend la couleur du joueur.");
                }
                else if (name.Contains(PlayerPaint.GlowSlot))
                {
                    glowing++;
                    AssertColor((Color)Orange * PlayerPaint.GlowIntensity, block.GetColor(EmissionColorId), $"« {name} » luit de la couleur du joueur.");
                }
                else Assert.IsTrue(block.isEmpty, $"« {name} » garde son matériau.");
            }
            Assert.AreEqual(1, painted, $"Garde : « {part} » a un matériau de peinture.");
            Assert.AreEqual(1, glowing, $"Garde : et un de lueur.");
            robot.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, $"Rien n'est peint sur toute la pièce « {part} ».");
        }

        // Le Y Bot, d'un seul matériau sans emplacement nommé : toute la pièce.
        SkinnedMeshRenderer body = Part(Model(), "Alpha_Surface");
        PlayerPaint.PaintPart(body, Orange, block, true);
        body.GetPropertyBlock(block);
        AssertColor(Orange, block.GetColor(BaseColorId), "Le Y Bot est peint tout entier.");
    }

    [Test]
    public void LEpaveDUnRobotDeBlender_EstPeinte_MaisSesLueursRestentEteintes()
    {
        var owner = new GameObject("Joueur");
        created.Add(owner);
        var paint = owner.AddComponent<PlayerPaint>();
        var color = (NetworkVariable<Color32>)typeof(PlayerPaint)
            .GetField("networkPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(paint);
        color.Value = Orange;

        foreach (string part in BlenderParts)
        {
            Transform corpse = Model();
            SkinnedMeshRenderer robot = Part(corpse, part);
            RobotFaceScreen.ShowDeath(corpse.gameObject, 3f);
            paint.PaintOnce(robot);

            var block = new MaterialPropertyBlock();
            Material[] materials = robot.sharedMaterials;
            int glows = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                robot.GetPropertyBlock(block, i);
                if (materials[i].name.Contains(PlayerPaint.PaintSlot))
                    AssertColor(Orange, block.GetColor(BaseColorId), $"L'épave de « {part} » garde la couleur du joueur.");
                if (!materials[i].name.Contains(PlayerPaint.GlowSlot)) continue;
                glows++;
                AssertColor(Color.black, block.GetColor(EmissionColorId), $"Les lueurs de « {part} » hors service restent éteintes.");
            }
            Assert.AreEqual(1, glows, $"Garde : « {part} » a des lueurs.");
        }
    }

    /// <summary>Deux instances du jeu sur un même PC partagent leurs PlayerPrefs : chacune garde le châssis choisi
    /// dans son propre menu, comme sa couleur.</summary>
    [Test]
    public void LeChassis_ResteCeluiDeCetteInstance_QuandUneAutreEnregistreLeSien()
    {
        int saved = PlayerPrefs.GetInt(GameSettings.PreferredChassisKey, 0);
        ChassisId mine = GameSettings.PreferredChassis;
        try
        {
            GameSettings.PreferredChassis = ChassisId.Prototype;
            Assert.AreEqual((int)ChassisId.Prototype, PlayerPrefs.GetInt(GameSettings.PreferredChassisKey), "Garde : le choix est retenu.");

            PlayerPrefs.SetInt(GameSettings.PreferredChassisKey, (int)ChassisId.CT01);
            Assert.AreEqual(ChassisId.Prototype, GameSettings.PreferredChassis, "Cette instance annonce toujours le châssis de son menu.");
        }
        finally
        {
            GameSettings.PreferredChassis = mine;
            PlayerPrefs.SetInt(GameSettings.PreferredChassisKey, saved);
            PlayerPrefs.Save();
        }
    }
}
