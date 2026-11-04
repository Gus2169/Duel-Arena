using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Part = RobotCorpse.RagdollPart;
using Severity = RobotCorpse.BurstSeverity;

/// <summary>
/// Le robot qui vole en pièces quand il est tué (RobotCorpse.Burst, 2026-11-03, avec la kill cam) : la force du coup,
/// sur les vrais coups du jeu (l'arsenal du préfab joueur, le gant), ce qu'elle arrache selon la pièce touchée, et quels
/// châssis peuvent perdre des pièces. Fonctions pures, et les maillages des robots.
/// </summary>
public class RobotBurstTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

    private static IReadOnlyList<WeaponData> Loadout() => Prefab.GetComponentInChildren<WeaponController>(true).Loadout;

    private static Severity Of(Vector3 impulse) => RobotCorpse.SeverityOf(impulse.magnitude);

    /// <summary>La force du coup qui tue : légère à la dernière balle d'un fusil d'assaut, tête comprise ; lourde au fusil
    /// de précision ; brutale au pompe, même de loin, et au gant. Vu échouer, le fusil de précision compté léger.</summary>
    [Test]
    public void LaForceDuCoup_LegereAuFusilDAssaut_LourdeAuSniper_BrutaleAuPompeEtAuGant()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        WeaponData rifle = loadout[0];
        WeaponData sniper = loadout[1];
        WeaponData shotgun = loadout[2];
        Assert.AreEqual("FA-07", rifle.weaponName);
        Assert.AreEqual("FP-12", sniper.weaponName);
        Assert.AreEqual("FAP-03", shotgun.weaponName);

        Assert.AreEqual(Severity.Light, Of(WeaponController.KillImpulse(rifle, Vector3.forward, rifle.damagePerHit)));
        Assert.AreEqual(Severity.Light, Of(WeaponController.KillImpulse(rifle, Vector3.forward, rifle.damagePerHit * rifle.headMultiplier)));
        Assert.AreEqual(Severity.Heavy, Of(WeaponController.KillImpulse(sniper, Vector3.forward, sniper.damagePerHit)));
        Assert.AreEqual(Severity.Heavy, Of(WeaponController.KillImpulse(sniper, Vector3.forward, sniper.damagePerHit * sniper.headMultiplier)));
        Assert.AreEqual(Severity.Brutal, Of(WeaponController.KillImpulse(shotgun, Vector3.forward, shotgun.damagePerHit * shotgun.PelletCount)),
                        "Le pompe à bout portant.");
        Assert.AreEqual(Severity.Brutal, Of(WeaponController.KillImpulse(shotgun, Vector3.forward, shotgun.damagePerHit)),
                        "Le pompe de loin, un plomb.");
        Assert.AreEqual(Severity.Brutal, Of(TaserController.DeathImpulse(Vector3.forward)), "Le gant.");
        Assert.AreEqual(Severity.Light, RobotCorpse.SeverityOf(float.NaN), "Un coup sans force connue : léger.");
    }

    /// <summary>Au Tournoi, un joueur en plein match ne paie pas les effets des autres arènes ; la salle d'attente, elle, voit
    /// tout (le direct filme chaque arène), et hors du Tournoi on est toujours dans la même. Vu échouer, la salle d'attente
    /// privée des épaves du direct.</summary>
    [Test]
    public void AuTournoi_ChacunNeVoitQueSonMatch_SaufLaSalleDAttente()
    {
        Assert.IsTrue(PlayerDeath.ShowsEffects(inLane: true, sameLane: true), "Dans son couloir, tout se voit.");
        Assert.IsFalse(PlayerDeath.ShowsEffects(inLane: true, sameLane: false), "Un autre couloir, à 2 km : rien.");
        Assert.IsTrue(PlayerDeath.ShowsEffects(inLane: false, sameLane: false), "La salle d'attente regarde les écrans.");
        Assert.IsTrue(PlayerDeath.ShowsEffects(inLane: false, sameLane: true), "Hors du Tournoi, tous dans la même arène.");
    }

    /// <summary>
    /// Ce qui s'arrache : léger, la tête, le membre touché, le bras du côté touché au buste, la cuisse au bassin ; lourd, la
    /// tête, un membre en deux morceaux, le robot coupé à la taille ; brutal, la tête, les bras, les jambes et le buste.
    /// Jamais le bassin (le tronc qui reste), jamais rien, jamais deux fois la même pièce. Vu échouer, le côté touché
    /// ignoré.
    /// </summary>
    [Test]
    public void CeQuiSArrache_SuitLaPieceTouchee_LeCoteEtLaForce()
    {
        CollectionAssert.AreEquivalent(new[] { Part.Head }, RobotCorpse.PartsToDetach(Part.Head, false, Severity.Light));
        CollectionAssert.AreEquivalent(new[] { Part.RightArm }, RobotCorpse.PartsToDetach(Part.Torso, true, Severity.Light));
        CollectionAssert.AreEquivalent(new[] { Part.LeftArm }, RobotCorpse.PartsToDetach(Part.Torso, false, Severity.Light));
        CollectionAssert.AreEquivalent(new[] { Part.LeftThigh }, RobotCorpse.PartsToDetach(Part.Pelvis, false, Severity.Light));
        CollectionAssert.AreEquivalent(new[] { Part.RightForearm }, RobotCorpse.PartsToDetach(Part.RightForearm, true, Severity.Light));

        CollectionAssert.AreEquivalent(new[] { Part.Head }, RobotCorpse.PartsToDetach(Part.Head, true, Severity.Heavy));
        CollectionAssert.AreEquivalent(new[] { Part.Torso, Part.Head }, RobotCorpse.PartsToDetach(Part.Torso, true, Severity.Heavy));
        CollectionAssert.AreEquivalent(new[] { Part.LeftThigh, Part.LeftCalf }, RobotCorpse.PartsToDetach(Part.LeftCalf, false, Severity.Heavy));
        CollectionAssert.AreEquivalent(new[] { Part.Torso, Part.RightThigh }, RobotCorpse.PartsToDetach(Part.Pelvis, true, Severity.Heavy));

        CollectionAssert.AreEquivalent(new[] { Part.Head, Part.LeftArm, Part.RightArm, Part.LeftThigh, Part.RightThigh, Part.Torso },
                                       RobotCorpse.PartsToDetach(Part.Torso, true, Severity.Brutal));
        CollectionAssert.Contains(RobotCorpse.PartsToDetach(Part.LeftForearm, false, Severity.Brutal), Part.LeftForearm);

        foreach (Part hit in System.Enum.GetValues(typeof(Part)))
        {
            foreach (Severity severity in System.Enum.GetValues(typeof(Severity)))
            {
                foreach (bool right in new[] { false, true })
                {
                    List<Part> parts = RobotCorpse.PartsToDetach(hit, right, severity);
                    Assert.IsNotEmpty(parts, $"{hit}, {severity} : le coup qui tue arrache toujours quelque chose.");
                    CollectionAssert.DoesNotContain(parts, Part.Pelvis, "Le bassin est le tronc qui reste.");
                    CollectionAssert.AllItemsAreUnique(parts);
                }
            }
        }
    }

    /// <summary>
    /// Seuls les robots faits de pièces rigides perdent des pièces : le CT-01 et l'OS-02, dont chaque sommet tient à un
    /// seul os (une pièce arrachée part entière) ; pas le prototype, dont la peau continue s'étirerait. Vu échouer, le
    /// prototype compté rigide.
    /// </summary>
    [Test]
    public void SeulsLesRobotsAPiecesRigides_PerdentDesPieces()
    {
        Assert.IsTrue(RobotChassis.HasRigidPieces((int)ChassisId.CT01));
        Assert.IsTrue(RobotChassis.HasRigidPieces((int)ChassisId.OS02));
        Assert.IsFalse(RobotChassis.HasRigidPieces((int)ChassisId.Prototype), "La peau continue du Y Bot s'étirerait.");
        Assert.IsTrue(RobotChassis.HasRigidPieces(-1), "Une arme, le gant.");

        int checkedRobots = 0;
        foreach (SkinnedMeshRenderer skin in Prefab.transform.Find("Model").GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int owner = RobotChassis.OwnerOf(skin.name);
            if (owner < 0 || !RobotChassis.HasRigidPieces(owner)) continue;
            Mesh mesh = skin.sharedMesh;
            Assert.IsTrue(mesh.isReadable, $"{skin.name} : le maillage monté se lit.");
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            int at = 0;
            for (int v = 0; v < perVertex.Length; v++)
            {
                float strongest = 0f;
                for (int i = 0; i < perVertex[v]; i++) strongest = Mathf.Max(strongest, weights[at + i].weight);
                at += perVertex[v];
                Assert.Greater(strongest, 0.999f, $"{skin.name}, sommet {v} : il tient à un seul os.");
            }
            checkedRobots++;
        }
        Assert.AreEqual(2, checkedRobots, "Le CT-01 et l'OS-02.");
    }
}
