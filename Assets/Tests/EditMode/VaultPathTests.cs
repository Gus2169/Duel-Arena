using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Le franchissement ne traverse rien (2026-11-04, l'utilisateur : « il passe à travers les murs », sur les caisses collées
/// au bloc central de l'arène). Le contrôleur est coupé pendant l'arc : rien d'autre n'arrête le robot que ce que
/// PlayerLocomotion.FindVault refuse. Quand le passage est fermé, le robot se hisse sur l'obstacle, s'il y tient (demandé par
/// l'utilisateur le même jour). Avec de vrais colliders et les réglages du préfab, puis sur la vraie arène.
///
/// Ce qui casse ici ne lève aucune erreur : un robot qui se retrouve dans une pièce fermée, ou dans un mur, d'où le
/// contrôleur le sort du mauvais côté.
/// </summary>
public class VaultPathTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const string ScenePath = "Assets/_ProjectScenes/MultiTestScene.unity";

    // Loin de tout ce que la scène ouverte pourrait porter.
    private static readonly Vector3 Origin = new Vector3(-300f, 50f, 300f);

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.DestroyImmediate(go);
        created.Clear();
        Physics.SyncTransforms();
    }

    private static PlayerLocomotion.VaultGeometry PrefabGeometry()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        return prefab.GetComponent<PlayerLocomotion>().VaultGeometryOf(prefab.GetComponent<CharacterController>());
    }

    /// <summary>Une boîte, de `min` à `max` autour de l'origine des tests.</summary>
    private void Box(Vector3 min, Vector3 max)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(box);
        box.transform.position = Origin + (min + max) * 0.5f;
        box.transform.localScale = max - min;
    }

    private void Floor() => Box(new Vector3(-20f, -1f, -20f), new Vector3(20f, 0f, 20f));

    /// <summary>Une caisse d'un mètre, de face, sa face avant en z = 0 : le robot est 40 cm devant et la regarde.</summary>
    private void Crate() => Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 1f));

    private bool Vault(out Vector3 landing)
    {
        Physics.SyncTransforms();
        bool found = PlayerLocomotion.FindVault(Origin + new Vector3(0f, 0f, -0.4f), Vector3.forward, PrefabGeometry(), out landing, out _);
        landing -= Origin;
        return found;
    }

    /// <summary>Le robot est sur la caisse d'un mètre (Crate), sa capsule entière devant le mur qui est en z = 1.</summary>
    private static void AssertOnCrate(Vector3 landing)
    {
        Assert.AreEqual(1f, landing.y, 1e-3f, "Sur la caisse.");
        Assert.LessOrEqual(landing.z + 0.35f, 1f, "La capsule devant ce qui ferme le passage.");
        Assert.GreaterOrEqual(landing.z, 0.15f, "Pas au bord de la caisse.");
    }

    [Test]
    public void UneCaisseSeule_SeFranchit_EtOnRetombeDerriere()
    {
        Floor();
        Crate();
        Assert.IsTrue(Vault(out Vector3 landing), "Rien derrière la caisse : le franchissement passe.");
        Assert.AreEqual(0f, landing.y, 1e-3f, "Au sol.");
        Assert.Greater(landing.z, 1.3f, "La capsule entière derrière la caisse.");
    }

    /// <summary>Le bloc central de l'arène : un mur de 50 cm contre la caisse, une pièce libre derrière. La sonde de la face
    /// arrière, partie de la pièce, y trouve une arrivée où le robot tient : il faut voir le mur sur le passage.</summary>
    [Test]
    public void UneCaisseContreUnMurFin_LeRobotMonteDessus_MemeSiLaPlaceEstLibreDerriere()
    {
        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 1f), new Vector3(4f, 3f, 1.5f));
        Assert.IsTrue(Vault(out Vector3 landing), "Le passage est fermé : le robot se hisse sur la caisse.");
        AssertOnCrate(landing);
    }

    [Test]
    public void UneCaisseContreUnMurEpais_LeRobotMonteDessus()
    {
        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 1f), new Vector3(4f, 3f, 5f));
        Assert.IsTrue(Vault(out Vector3 landing), "Le passage est fermé : le robot se hisse sur la caisse.");
        AssertOnCrate(landing);
    }

    [Test]
    public void DerriereLaCaisse_IlFautLaPlaceDeLaCapsule()
    {
        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 1.5f), new Vector3(4f, 3f, 2f));
        Assert.IsTrue(Vault(out Vector3 onCrate), "50 cm entre la caisse et le mur : il ne tient pas derrière, il monte dessus.");
        Assert.AreEqual(1f, onCrate.y, 1e-3f, "Sur la caisse.");
        Assert.Less(onCrate.z, 1f, "Elle le porte.");
        TearDown();

        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 2.2f), new Vector3(4f, 3f, 2.7f));
        Assert.IsTrue(Vault(out Vector3 landing), "1,2 m entre la caisse et le mur : il y tient.");
        Assert.AreEqual(0f, landing.y, 1e-3f, "Au sol.");
        Assert.Less(landing.z, 2.2f - 0.3f, "Devant le mur.");
    }

    /// <summary>Une barricade, un creux trop petit derrière : le robot n'y tient pas, et 50 cm de large ne le portent pas.</summary>
    [Test]
    public void SurUnDessusTropEtroit_RienNeSePasse()
    {
        Floor();
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 0.5f));
        Box(new Vector3(-4f, 0f, 1.15f), new Vector3(4f, 3f, 1.65f));
        Assert.IsFalse(Vault(out Vector3 landing), $"Le robot arriverait sur 50 cm, en {landing}.");
    }

    /// <summary>Le bloc central de l'arène vu de la caisse de 2 m : un mur d'un mètre devant ses pieds, 3 m de vide derrière. Un
    /// mur de 50 cm : le robot ne se tient pas dessus non plus.</summary>
    [Test]
    public void UnMurAuBordDuVide_NeSeFranchitPas()
    {
        Floor();
        Box(new Vector3(-5f, 0f, -5f), new Vector3(5f, 2f, 0f));
        Box(new Vector3(-5f, 0f, 0f), new Vector3(5f, 3f, 0.5f));
        Physics.SyncTransforms();
        Vector3 feet = Origin + new Vector3(0f, 2f, -0.4f);
        Assert.IsFalse(PlayerLocomotion.FindVault(feet, Vector3.forward, PrefabGeometry(), out Vector3 landing, out _), $"Le robot sauterait dans le vide, en {landing - Origin}.");
        TearDown();

        // Garde : le même mur, du sol à 1,5 m sous son sommet, se franchit.
        Floor();
        Box(new Vector3(-5f, 0f, -5f), new Vector3(5f, 2f, 0f));
        Box(new Vector3(-5f, 0f, 0f), new Vector3(5f, 3f, 0.5f));
        Box(new Vector3(-5f, 0f, 0.5f), new Vector3(5f, 1.6f, 5f));
        Physics.SyncTransforms();
        Assert.IsTrue(PlayerLocomotion.FindVault(feet, Vector3.forward, PrefabGeometry(), out Vector3 down, out _), "Du sol à portée derrière : il passe.");
        Assert.AreEqual(1.6f, (down - Origin).y, 1e-3f, "Sur ce sol.");
    }

    [Test]
    public void SurUnObstacleTropMince_ContreUnMur_RienNeSePasse()
    {
        Floor();
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 0.4f));
        Box(new Vector3(-4f, 0f, 0.4f), new Vector3(4f, 3f, 0.9f));
        Assert.IsFalse(Vault(out Vector3 landing), $"Le robot ne tient pas sur 40 cm contre un mur : il arriverait en {landing}.");
    }

    [Test]
    public void SousUnPlafond_OnNeMonteSurLaCaisseQueSiOnYTientDebout()
    {
        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 1f), new Vector3(4f, 3f, 1.5f));
        Box(new Vector3(-5f, 2.6f, -5f), new Vector3(5f, 2.9f, 1f));
        Assert.IsFalse(Vault(out _), "1,6 m au-dessus de la caisse : le robot n'y tient pas debout.");
        TearDown();

        Floor();
        Crate();
        Box(new Vector3(-4f, 0f, 1f), new Vector3(4f, 3f, 1.5f));
        Box(new Vector3(-5f, 3.1f, -5f), new Vector3(5f, 3.4f, 1f));
        Assert.IsTrue(Vault(out Vector3 landing), "2,1 m au-dessus : il y tient.");
        AssertOnCrate(landing);
    }

    /// <summary>De biais, la capsule s'arrête devant le mur comme de face, le long de son regard : une caisse de 80 cm, où une
    /// arrivée ordinaire serait déjà dans le mur.</summary>
    [Test]
    public void DeBiais_LeRobotMonteAussiSurLaCaisse_EtSArreteDevantLeMur()
    {
        Floor();
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 0.8f));
        Box(new Vector3(-4f, 0f, 0.8f), new Vector3(4f, 3f, 1.3f));
        Physics.SyncTransforms();
        Vector3 forward = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
        Assert.IsTrue(PlayerLocomotion.FindVault(Origin + new Vector3(0f, 0f, -0.4f), forward, PrefabGeometry(), out Vector3 landing, out _), "À 45°, il monte aussi.");
        landing -= Origin;
        Assert.AreEqual(1f, landing.y, 1e-3f, "Sur la caisse.");
        Assert.LessOrEqual(landing.z + 0.35f, 0.8f, "La capsule devant le mur.");
    }

    [Test]
    public void SousUnPlafondBas_LaBarricadeHauteNePassePas_LaBasseSi()
    {
        Floor();
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1.25f, 0.5f));
        Box(new Vector3(-5f, 2.2f, -5f), new Vector3(5f, 2.5f, 5f));
        Assert.IsFalse(Vault(out _), "Sous 2,2 m de plafond, le robot ramassé ne passe pas au-dessus de 1,25 m.");
        TearDown();

        Floor();
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0.5f, 0.5f));
        Box(new Vector3(-5f, 2.2f, -5f), new Vector3(5f, 2.5f, 5f));
        Assert.IsTrue(Vault(out _), "Au-dessus de 50 cm, il passe.");
    }

    [Test]
    public void UneMarcheALArrivee_NeRefusePasLeFranchissement()
    {
        Floor();
        Crate();
        // 20 cm, sous la capsule à l'arrivée mais pas sous son centre : le contrôleur l'enjambe.
        Box(new Vector3(-4f, 0f, 1.6f), new Vector3(4f, 0.2f, 3f));
        Assert.IsTrue(Vault(out Vector3 landing), "Ce que le contrôleur enjambe n'arrête pas le robot.");
        Assert.AreEqual(0f, landing.y, 1e-3f);
    }

    /// <summary>De biais, l'arrivée va plus loin le long du regard : à 45°, un rayon de capsule y laissait la capsule mordre
    /// l'arête de la barricade, et le refus d'une arrivée où le robot ne tient pas retirait une barricade sur trois.</summary>
    [Test]
    public void DeBiais_UneBarricadeSeFranchit_EtLaCapsuleArriveDerriere()
    {
        Floor();
        Box(new Vector3(-5f, 0f, 0f), new Vector3(5f, 1.25f, 0.5f));
        Physics.SyncTransforms();
        PlayerLocomotion.VaultGeometry g = PrefabGeometry();
        Vector3 forward = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
        Assert.IsTrue(PlayerLocomotion.FindVault(Origin + new Vector3(0f, 0f, -0.4f), forward, g, out Vector3 landing, out _), "À 45°, la barricade se franchit.");
        Assert.GreaterOrEqual(landing.z - Origin.z - 0.5f, g.radius + g.skinWidth - 1e-3f, "La capsule entière derrière sa face arrière.");
    }

    [Test]
    public void DansLArene_RienNeSeFranchitAuTraversDUnMur()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Physics.SyncTransforms();
            PlayerLocomotion.VaultGeometry g = PrefabGeometry();

            // Le défaut, tel que vu en jeu : les caisses collées au bloc central, de chaque côté. Le robot monte dessus, et le
            // mur de 3 m l'arrête.
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(-4.75f, 0f, 27f), Vector3.right, g, out Vector3 west, out _), "La caisse ouest du bloc central.");
            Assert.AreEqual(1f, west.y, 0.01f, "Sur la caisse ouest.");
            Assert.Less(west.x, -3.25f - 0.35f, "Devant le mur du bloc central.");
            Assert.IsFalse(PlayerLocomotion.FindVault(west, Vector3.right, g, out _, out _), "Du dessus de la caisse, le mur ne se franchit pas.");
            // De la caisse d'un mètre, on monte sur celle de 2 m empilée à côté ; de là, le mur de 3 m ferme une pièce sans issue.
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(-3.75f, 1f, 27.2f), Vector3.back, g, out Vector3 stacked, out _), "La caisse empilée.");
            Assert.AreEqual(2f, stacked.y, 0.01f, "Sur elle.");
            Assert.IsFalse(PlayerLocomotion.FindVault(stacked, Vector3.right, g, out Vector3 pit, out _), $"Le robot tomberait dans le bloc central, en {pit}.");
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(4.75f, 0f, 27f), Vector3.left, g, out Vector3 east, out _), "La caisse est du bloc central.");
            Assert.AreEqual(1f, east.y, 0.01f, "Sur la caisse est.");
            Assert.Greater(east.x, 3.25f + 0.35f, "Devant le mur du bloc central.");
            // Celles collées au mur d'enceinte : le robot sortirait de l'arène.
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(-24.5f, 0f, 49f), Vector3.left, g, out Vector3 side, out _), "La caisse contre le mur ouest.");
            Assert.AreEqual(1f, side.y, 0.01f, "Sur la caisse.");
            Assert.Greater(side.x, -26f + 0.35f, "Devant le mur d'enceinte.");
            Assert.IsFalse(PlayerLocomotion.FindVault(side, Vector3.left, g, out _, out _), "Du dessus de la caisse, le mur d'enceinte ne se franchit pas.");
            // Ce qui se franchit se franchit encore.
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(20f, 0f, 25.3f), Vector3.forward, g, out Vector3 low, out _), "La barricade de 0,5 m.");
            Assert.Greater(low.z, 26.25f, "Derrière elle.");
            Assert.IsTrue(PlayerLocomotion.FindVault(new Vector3(26f, 0f, 25.3f), Vector3.forward, g, out _, out _), "La barricade de 1,25 m.");
            Assert.IsFalse(PlayerLocomotion.FindVault(new Vector3(14f, 0f, 25.3f), Vector3.forward, g, out _, out _), "La barricade de 1,6 m, refusée exprès.");

            // Tout le sol, dans huit directions : ce qui se franchit ne passe à travers rien de plus haut que l'arc.
            int vaults = 0;
            var failures = new List<string>();
            var raised = new Queue<Vector3>();
            var seen = new HashSet<Vector3Int>();
            for (float x = -34f; x <= 34f; x += 0.5f)
            {
                for (float z = -6f; z <= 70f; z += 0.5f)
                {
                    var feet = new Vector3(x, 0f, z);
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.4f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    for (int d = 0; d < 8; d++)
                    {
                        Vector3 forward = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                        if (!PlayerLocomotion.FindVault(feet, forward, g, out Vector3 landing, out float top)) continue;
                        vaults++;
                        float high = top + 0.5f;
                        var from = new Vector3(feet.x, high, feet.z);
                        var to = new Vector3(landing.x, high, landing.z);
                        if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && failures.Count < 8)
                            failures.Add($"de {feet} vers {forward} : à travers {hit.collider.name}, jusqu'en {landing}");
                        if (landing.y > 0.25f && seen.Add(Vector3Int.RoundToInt(landing * 4f))) raised.Enqueue(landing);
                    }
                }
            }
            Assert.Greater(vaults, 50, "Garde : l'arène a de quoi franchir, le balayage a vu des franchissements.");
            Assert.IsEmpty(failures, string.Join("\n", failures));

            // Et d'où le robot arrive, il repart : rien ne mène plus haut que la caisse de 2 m, ni dans le bloc central.
            float highest = 0f;
            while (raised.Count > 0)
            {
                Vector3 feet = raised.Dequeue();
                highest = Mathf.Max(highest, feet.y);
                for (int d = 0; d < 8; d++)
                {
                    Vector3 forward = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                    if (!PlayerLocomotion.FindVault(feet, forward, g, out Vector3 landing, out _)) continue;
                    Assert.IsFalse(Mathf.Abs(landing.x) < 3.25f && landing.z > 22f && landing.z < 38f, $"De {feet} vers {forward}, le robot arrive dans le bloc central, en {landing}.");
                    if (landing.y > 0.25f && seen.Add(Vector3Int.RoundToInt(landing * 4f))) raised.Enqueue(landing);
                }
            }
            Assert.AreEqual(2f, highest, 0.01f, "Le plus haut où monte le robot, la caisse empilée.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
