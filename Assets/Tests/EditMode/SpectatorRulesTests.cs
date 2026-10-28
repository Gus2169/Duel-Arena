using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Phase = RoundManager.Phase;

/// <summary>
/// La tribune (2026-10-13, GDD § 4 et § 9) : qui se bat et qui attend, ce que les spectateurs ont le
/// droit de faire, et comment. Les fonctions pures que le serveur applique.
/// </summary>
public class SpectatorRulesTests
{
    // ------------------------------------------------------------------
    // Qui se bat, qui attend
    // ------------------------------------------------------------------

    private static List<ulong> Ids(params ulong[] ids) => new List<ulong>(ids);

    [Test]
    public void LesDeuxPremiersArrives_SeBattent_LesSuivantsAttendentDansLOrdre()
    {
        var duelists = new List<ulong>();
        var queue = new List<ulong>();
        DuelRoster.Sync(Ids(0, 1, 2, 3), duelists, queue, out bool left);
        CollectionAssert.AreEqual(new ulong[] { 0, 1 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 2, 3 }, queue, "La file suit l'ordre d'arrivée.");
        Assert.IsFalse(left);

        Assert.AreEqual(PlayerRole.Duelist, DuelRoster.RoleOf(1, duelists, queue, out int place));
        Assert.AreEqual(0, place, "Un duelliste n'est pas dans la file.");
        Assert.AreEqual(PlayerRole.Spectator, DuelRoster.RoleOf(3, duelists, queue, out place));
        Assert.AreEqual(2, place, "Le quatrième arrivé est deuxième dans la file.");
    }

    [Test]
    public void AuRoiDeLaColline_LeGagnantReste_LePerdantRetourneAuBoutDeLaFile()
    {
        var duelists = Ids(0, 1);
        var queue = Ids(2, 3);
        Assert.IsTrue(DuelRoster.RotateAfterMatch(DuelOrganization.KingOfTheHill, duelists, queue, 1));
        CollectionAssert.AreEquivalent(new ulong[] { 1, 2 }, duelists, "Le gagnant (1) reste, le premier de la file (2) entre.");
        CollectionAssert.AreEqual(new ulong[] { 3, 0 }, queue, "Le perdant (0) passe derrière celui qui attendait déjà.");

        // Match suivant : le nouveau venu gagne, l'ancien roi tombe.
        Assert.IsTrue(DuelRoster.RotateAfterMatch(DuelOrganization.KingOfTheHill, duelists, queue, 2));
        CollectionAssert.AreEquivalent(new ulong[] { 2, 3 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 0, 1 }, queue);
    }

    [Test]
    public void EnDuellistesFixes_SansFile_OuSansGagnant_RienNeTourne()
    {
        var duelists = Ids(0, 1);
        var queue = Ids(2);
        Assert.IsFalse(DuelRoster.RotateAfterMatch(DuelOrganization.FixedDuelists, duelists, queue, 0), "Duellistes fixes.");
        Assert.IsFalse(DuelRoster.RotateAfterMatch(DuelOrganization.KingOfTheHill, duelists, Ids(), 0), "Personne n'attend.");
        Assert.IsFalse(DuelRoster.RotateAfterMatch(DuelOrganization.KingOfTheHill, duelists, queue, RoundManager.NoWinner), "Pas de gagnant connu.");
        Assert.IsFalse(DuelRoster.RotateAfterMatch(DuelOrganization.KingOfTheHill, duelists, queue, 7), "Un « gagnant » qui n'était pas duelliste.");
        CollectionAssert.AreEqual(new ulong[] { 0, 1 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 2 }, queue);
    }

    [Test]
    public void UnDuellisteQuiPart_LaissePlaceAuPremierDeLaFile()
    {
        var duelists = Ids(0, 1);
        var queue = Ids(2, 3);
        DuelRoster.Sync(Ids(0, 2, 3), duelists, queue, out bool left);
        Assert.IsTrue(left, "Le match en cours se termine (par forfait, RoundManager).");
        CollectionAssert.AreEqual(new ulong[] { 0 }, duelists);
        Assert.IsTrue(DuelRoster.FillFromQueue(duelists, queue));
        CollectionAssert.AreEqual(new ulong[] { 0, 2 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 3 }, queue);
    }

    /// <summary>
    /// L'abandon (GDD § 4, 2026-10-25) : un duelliste qui part en plein match perd par forfait, de la
    /// préparation de la première manche à la fin de la dernière ; pas en attente, ni une fois le match
    /// gagné. Sa place attend la fin de l'écran de fin : un joueur qui arrive entre-temps passe par la
    /// file, même si personne n'attend, puis la prend pour le match suivant.
    /// </summary>
    [Test]
    public void UnDuellisteQuiPartEnPleinMatch_PerdParForfait_SaPlaceAttendLaFinDuMatch()
    {
        foreach (Phase phase in new[] { Phase.Preparation, Phase.Starting, Phase.Active, Phase.RoundOver })
            Assert.IsTrue(RoundManager.ForfeitApplies(phase), $"{phase} : le match se joue, partir c'est perdre.");
        Assert.IsFalse(RoundManager.ForfeitApplies(Phase.WaitingForPlayers), "Pas de match en attente.");
        Assert.IsFalse(RoundManager.ForfeitApplies(Phase.MatchOver), "Le match est déjà gagné.");

        var duelists = Ids(0, 1);
        var queue = Ids();
        DuelRoster.Sync(Ids(0, 9), duelists, queue, DuelRoster.DuelistSlots, out bool left, admitNewcomers: false);
        Assert.IsTrue(left);
        CollectionAssert.AreEqual(new ulong[] { 0 }, duelists, "Le gagnant par forfait reste seul jusqu'à la fin de l'écran.");
        CollectionAssert.AreEqual(new ulong[] { 9 }, queue, "Le nouveau venu attend dans la file.");
        Assert.IsTrue(DuelRoster.FillFromQueue(duelists, queue), "L'écran fini, il prend la place.");
        CollectionAssert.AreEqual(new ulong[] { 0, 9 }, duelists);

        // Sans match, un nouveau venu entre tout de suite, comme avant.
        duelists = Ids(0);
        queue = Ids();
        DuelRoster.Sync(Ids(0, 9), duelists, queue, DuelRoster.DuelistSlots, out _);
        CollectionAssert.AreEqual(new ulong[] { 0, 9 }, duelists);
    }

    /// <summary>
    /// La fin du match (2026-10-26, règles de l'utilisateur) : la revanche demande les voix des deux
    /// duellistes ; une voix pour le salon y ramène aussitôt ; sans les deux voix, le temps écoulé ramène
    /// au salon. Seul, un duelliste n'a jamais de revanche (son adversaire est parti).
    /// </summary>
    [Test]
    public void LaRevanche_DemandeLesDeuxVoix_SinonRetourAuSalon()
    {
        const PlayerMatchState.MatchVote None = PlayerMatchState.MatchVote.None;
        const PlayerMatchState.MatchVote Rematch = PlayerMatchState.MatchVote.Rematch;
        const PlayerMatchState.MatchVote Salon = PlayerMatchState.MatchVote.Salon;
        RoundManager.MatchDecision Decide(bool timeUp, params PlayerMatchState.MatchVote[] votes) => RoundManager.DecideVote(votes, timeUp);

        Assert.AreEqual(RoundManager.MatchDecision.Pending, Decide(false, None, None), "Personne n'a voté.");
        Assert.AreEqual(RoundManager.MatchDecision.Pending, Decide(false, Rematch, None), "Une seule voix pour la revanche : on attend l'autre.");
        Assert.AreEqual(RoundManager.MatchDecision.Rematch, Decide(false, Rematch, Rematch), "Les deux voix : la revanche, sans attendre.");
        Assert.AreEqual(RoundManager.MatchDecision.Salon, Decide(false, Rematch, Salon), "L'un veut le salon : pas de revanche.");
        Assert.AreEqual(RoundManager.MatchDecision.Salon, Decide(false, Salon, None), "Le salon, aussitôt choisi.");
        Assert.AreEqual(RoundManager.MatchDecision.Salon, Decide(true, Rematch, None), "Les 15 s passées sans les deux voix : le salon.");
        Assert.AreEqual(RoundManager.MatchDecision.Salon, Decide(true, None, None));
        Assert.AreEqual(RoundManager.MatchDecision.Pending, Decide(false, Rematch), "Seul (l'adversaire est parti) : jamais de revanche…");
        Assert.AreEqual(RoundManager.MatchDecision.Salon, Decide(true, Rematch), "… et le salon à la fin.");

        // Le salon gèle tout le monde, sans changement d'arme ; un départ n'y est pas un forfait.
        Assert.IsFalse(RoundManager.PlayAllowed(Phase.Salon, true));
        Assert.IsFalse(RoundManager.WeaponSwitchAllowed(Phase.Salon, false));
        Assert.IsFalse(RoundManager.ForfeitApplies(Phase.Salon));

        Assert.AreEqual("Duelliste", SalonPanel.RoleText(true, 0));
        Assert.AreEqual("Tribune · 1er à entrer", SalonPanel.RoleText(false, 1));
        Assert.AreEqual("Tribune · 3e à entrer", SalonPanel.RoleText(false, 3));
        StringAssert.Contains("retour au salon dans 7 s", MatchEndScreen.CountdownText(false, RoundManager.MatchDecision.Pending, "Revanche", 7));
        Assert.AreEqual("Revanche !", MatchEndScreen.CountdownText(false, RoundManager.MatchDecision.Rematch, "Revanche", 1));
        Assert.AreEqual("Nouvelle partie dans 4 s", MatchEndScreen.CountdownText(true, RoundManager.MatchDecision.Pending, "Revanche", 4),
                        "Après un forfait, pas de vote.");
    }

    [Test]
    public void UnNouveauVenu_NePasseJamaisDevantCeuxQuiAttendent()
    {
        // Un duelliste vient de partir, et un nouveau joueur arrive dans la même image : la place revient
        // au premier de la file, pas au nouveau.
        var duelists = Ids(0, 1);
        var queue = Ids(2);
        DuelRoster.Sync(Ids(0, 2, 9), duelists, queue, out _);
        DuelRoster.FillFromQueue(duelists, queue);
        CollectionAssert.AreEqual(new ulong[] { 0, 2 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 9 }, queue);
    }

    [Test]
    public void UnSpectateurQuiPart_QuitteLaFile_SansArreterLeMatch()
    {
        var duelists = Ids(0, 1);
        var queue = Ids(2, 3);
        DuelRoster.Sync(Ids(0, 1, 3), duelists, queue, out bool left);
        Assert.IsFalse(left);
        CollectionAssert.AreEqual(new ulong[] { 3 }, queue);
        Assert.AreEqual(PlayerRole.Spectator, DuelRoster.RoleOf(3, duelists, queue, out int place));
        Assert.AreEqual(1, place, "Le suivant passe premier de la file.");
    }

    [Test]
    public void LeBancDeTest_UneSeulePlace_FaitDuSecondUnSpectateur()
    {
        var duelists = new List<ulong>();
        var queue = new List<ulong>();
        DuelRoster.Sync(Ids(0, 1), duelists, queue, 1, out _);
        CollectionAssert.AreEqual(new ulong[] { 0 }, duelists);
        CollectionAssert.AreEqual(new ulong[] { 1 }, queue);
    }

    // ------------------------------------------------------------------
    // Ce que chaque phase permet
    // ------------------------------------------------------------------

    [Test]
    public void OnLance_PendantLaManche_JamaisPendantLaPreparationNiLeDecompte()
    {
        Assert.IsTrue(SpectatorRules.ThrowAllowed(Phase.Active));
        Assert.IsTrue(SpectatorRules.ThrowAllowed(Phase.RoundOver), "Sur le gagnant qui nargue.");
        Assert.IsTrue(SpectatorRules.ThrowAllowed(Phase.MatchOver));
        Assert.IsTrue(SpectatorRules.ThrowAllowed(Phase.WaitingForPlayers), "Pendant l'échauffement.");
        Assert.IsFalse(SpectatorRules.ThrowAllowed(Phase.Preparation), "Les duellistes sont gelés : personne ne commence la manche désavantagé.");
        Assert.IsFalse(SpectatorRules.ThrowAllowed(Phase.Starting));

        Assert.IsTrue(SpectatorRules.DropsAllowed(Phase.Active), "Le matériel tombe pendant la manche (GDD § 9).");
        foreach (Phase phase in new[] { Phase.WaitingForPlayers, Phase.Preparation, Phase.Starting, Phase.RoundOver, Phase.MatchOver })
            Assert.IsFalse(SpectatorRules.DropsAllowed(phase), phase.ToString());
    }

    // ------------------------------------------------------------------
    // L'étourdissement
    // ------------------------------------------------------------------

    [Test]
    public void UnEtourdissement_PuisUneImmunite_PersonneNeResteAuSol()
    {
        double stunned = 0.0, immune = 0.0;
        Assert.IsTrue(SpectatorRules.TryStun(ref stunned, ref immune, 10.0, 2.5f, 1.5f));
        Assert.AreEqual(12.5, stunned, 1e-9);
        Assert.AreEqual(14.0, immune, 1e-9, "Immunisé 1,5 s après la fin de l'étourdissement.");
        Assert.IsTrue(SpectatorRules.IsStunned(stunned, 12.4));
        Assert.IsFalse(SpectatorRules.IsStunned(stunned, 12.5));

        Assert.IsFalse(SpectatorRules.TryStun(ref stunned, ref immune, 11.0, 2.5f, 1.5f), "Déjà étourdi : un second coup ne prolonge rien.");
        Assert.IsFalse(SpectatorRules.TryStun(ref stunned, ref immune, 13.9, 2.5f, 1.5f), "Immunisé.");
        Assert.AreEqual(12.5, stunned, 1e-9, "Un coup refusé ne touche pas à l'état.");
        Assert.IsTrue(SpectatorRules.TryStun(ref stunned, ref immune, 14.0, 1.5f, 1.5f), "L'immunité finie, on peut de nouveau être étourdi.");
        Assert.IsFalse(SpectatorRules.TryStun(ref stunned, ref immune, 20.0, float.NaN, 1.5f), "Une durée invalide n'étourdit pas.");
    }

    // ------------------------------------------------------------------
    // Le lancer
    // ------------------------------------------------------------------

    [Test]
    public void LaForceDuLancer_SuitLElan_EtResteBornee()
    {
        Vector3 forward = new Vector3(0f, 0f, 2f); // non normalisée : elle doit l'être
        Assert.AreEqual(9f, SpectatorRules.ThrowVelocity(forward, 0f, 9f, 24f).magnitude, 1e-4f);
        Assert.AreEqual(24f, SpectatorRules.ThrowVelocity(forward, 1f, 9f, 24f).magnitude, 1e-4f);
        Assert.AreEqual(16.5f, SpectatorRules.ThrowVelocity(forward, 0.5f, 9f, 24f).magnitude, 1e-4f);
        Assert.AreEqual(24f, SpectatorRules.ThrowVelocity(forward, 50f, 9f, 24f).magnitude, 1e-4f, "Un client modifié ne lance pas plus fort qu'un joueur au bout de son élan.");
        Assert.AreEqual(9f, SpectatorRules.ThrowVelocity(forward, float.NaN, 9f, 24f).magnitude, 1e-4f);
        Assert.AreEqual(Vector3.zero, SpectatorRules.ThrowVelocity(Vector3.zero, 1f, 9f, 24f), "Sans direction, pas de lancer.");
        Assert.AreEqual(Vector3.zero, SpectatorRules.ThrowVelocity(new Vector3(float.NaN, 0f, 1f), 1f, 9f, 24f));
        Assert.AreEqual(1f, Vector3.Dot(SpectatorRules.ThrowVelocity(forward, 1f, 9f, 24f).normalized, Vector3.forward), 1e-5f);
    }

    [Test]
    public void LaSecousseDUnDuelliste_PartDuCoteOuVaLeCaillou_SansHasard()
    {
        Assert.AreEqual(1f, SpectatorRules.FlinchSide(new Vector3(5f, 0f, 1f), Vector3.right));
        Assert.AreEqual(-1f, SpectatorRules.FlinchSide(new Vector3(-5f, 0f, 1f), Vector3.right));
        Assert.AreEqual(SpectatorRules.FlinchSide(new Vector3(3f, -2f, 4f), Vector3.right),
                        SpectatorRules.FlinchSide(new Vector3(3f, -2f, 4f), Vector3.right), "Le même caillou, la même secousse.");
    }

    // ------------------------------------------------------------------
    // Les corps : coups de poing et cailloux
    // ------------------------------------------------------------------

    [Test]
    public void LaDistanceEntreDeuxSegments()
    {
        Assert.AreEqual(1f, SpectatorRules.SegmentDistance(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                                                           new Vector3(0f, -1f, 1f), new Vector3(0f, 1f, 1f), out Vector3 c1, out Vector3 c2), 1e-5f, "Deux segments croisés à 1 m.");
        Assert.AreEqual(Vector3.zero, c1);
        Assert.AreEqual(new Vector3(0f, 0f, 1f), c2);
        Assert.AreEqual(2f, SpectatorRules.SegmentDistance(Vector3.zero, Vector3.right, new Vector3(0f, 2f, 0f), new Vector3(1f, 2f, 0f), out _, out _), 1e-5f, "Parallèles.");
        Assert.AreEqual(Mathf.Sqrt(2f), SpectatorRules.SegmentDistance(Vector3.zero, Vector3.right, new Vector3(2f, 1f, 0f), new Vector3(3f, 1f, 0f), out _, out _), 1e-5f, "Bout à bout.");
        Assert.AreEqual(1f, SpectatorRules.SegmentDistance(Vector3.zero, Vector3.zero, Vector3.up, Vector3.up, out _, out _), 1e-5f, "Deux points.");
    }

    [Test]
    public void LeCoupDePoing_AtteintCeQuiEstDevantEtAPortee()
    {
        Vector3 eye = new Vector3(0f, 1.65f, 0f);
        const float range = 1.5f, radius = 0.3f, height = 1.8f, body = SpectatorRules.BodyRadius;

        Assert.IsTrue(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(0f, 0f, 1.2f), height, body, out float d, out Vector3 p),
            "Un robot à 1,2 m devant soi : le poing, à hauteur d'œil, atteint le haut de son corps.");
        Assert.Less(d, 1.3f);
        Assert.Greater(p.y, 1.45f, "Le point touché est sur la calotte du corps, au-dessus de son axe.");

        Vector3 chest = new Vector3(0f, 1.2f, 0f);
        Assert.IsTrue(SpectatorRules.PunchReaches(chest, Vector3.forward, range, radius, new Vector3(0f, 0f, 1.2f), height, body, out d, out p));
        Assert.AreEqual(1.2f - body, d, 1e-3f, "À hauteur de poitrine, on le touche sur sa face, à la surface de son corps.");
        Assert.AreEqual(1.2f - body, p.z, 1e-3f);

        Assert.IsFalse(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(0f, 0f, 2.4f), height, body, out _, out _), "Trop loin.");
        Assert.IsFalse(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(0f, 0f, -0.5f), height, body, out _, out _), "Dans le dos, même tout près.");
        Assert.IsFalse(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(1.4f, 0f, 1f), height, body, out _, out _), "Trop à côté.");
        Assert.IsTrue(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(0.5f, 0f, 0.9f), height, body, out _, out _), "Un peu à côté : le poing n'exige pas la précision d'une balle.");
        Assert.IsTrue(SpectatorRules.PunchReaches(eye, Vector3.forward, range, radius, new Vector3(0f, 0f, 0.5f), height, body, out _, out _), "Collé à soi.");
        Assert.IsFalse(SpectatorRules.PunchReaches(eye, new Vector3(float.NaN, 0f, 1f), range, radius, new Vector3(0f, 0f, 1.2f), height, body, out _, out _), "Une direction invalide ne touche rien.");
        // Accroupi, la tête est plus bas : un coup à hauteur d'œil debout passe au-dessus d'un robot accroupi loin devant.
        Assert.IsFalse(SpectatorRules.PunchReaches(eye + Vector3.up * 0.4f, Vector3.forward, range, 0.1f, new Vector3(0f, 0f, 1.3f), 1.1f, body, out _, out _), "Au-dessus d'un robot accroupi.");
    }

    [Test]
    public void UnCaillouQuiFrolelUnCorps_LeTouche_AuBonEndroitDuTrajet()
    {
        Vector3 feet = new Vector3(0f, 0f, 5f);
        Assert.IsTrue(SpectatorRules.SweepHitsBody(new Vector3(0f, 1.2f, 4f), new Vector3(0f, 1.2f, 6f), 0.09f, feet, 1.8f, SpectatorRules.BodyRadius, out float f));
        Assert.AreEqual(0.5f, f, 0.05f, "Le trajet passe par le centre du corps, à mi-chemin.");
        Assert.IsTrue(SpectatorRules.SweepHitsBody(new Vector3(0.4f, 1f, 4f), new Vector3(0.4f, 1f, 6f), 0.09f, feet, 1.8f, SpectatorRules.BodyRadius, out _), "Le bord du corps.");
        Assert.IsFalse(SpectatorRules.SweepHitsBody(new Vector3(0.6f, 1f, 4f), new Vector3(0.6f, 1f, 6f), 0.09f, feet, 1.8f, SpectatorRules.BodyRadius, out _), "À côté.");
        Assert.IsFalse(SpectatorRules.SweepHitsBody(new Vector3(0f, 2.4f, 4f), new Vector3(0f, 2.4f, 6f), 0.09f, feet, 1.8f, SpectatorRules.BodyRadius, out _), "Au-dessus de la tête.");
    }

    // ------------------------------------------------------------------
    // Ramasser
    // ------------------------------------------------------------------

    [Test]
    public void OnRamasseCeQuOnRegarde_APortee()
    {
        Vector3 eye = new Vector3(0f, 1.65f, 0f);
        var items = new List<Vector3>
        {
            new Vector3(0f, 0.1f, 4f),     // trop loin
            new Vector3(1.5f, 0.1f, 0.3f), // à portée, mais hors du regard
            new Vector3(0.4f, 0.1f, 1.2f), // à portée, dans le regard (vers le bas)
            new Vector3(0.1f, 0.1f, 1.3f), // à portée, plus près de l'axe du regard
        };
        Vector3 lookDown = (new Vector3(0f, 0.1f, 1.3f) - eye).normalized;
        Assert.AreEqual(3, SpectatorRules.BestPickup(eye, lookDown, items, 2.3f, 35f), "Le plus proche de l'axe.");
        items.RemoveAt(3);
        Assert.AreEqual(2, SpectatorRules.BestPickup(eye, lookDown, items, 2.3f, 35f));
        Assert.AreEqual(-1, SpectatorRules.BestPickup(eye, Vector3.up, items, 2.3f, 35f), "Les yeux au ciel : rien.");
        Assert.AreEqual(0, SpectatorRules.BestPickup(eye, Vector3.up, new List<Vector3> { eye + Vector3.down * 0.3f }, 2.3f, 35f), "Tout contre soi, l'angle ne compte pas.");
    }

    // ------------------------------------------------------------------
    // Le matériel
    // ------------------------------------------------------------------

    [Test]
    public void LeMaterielTombe_SelonSesPoids()
    {
        var catalog = ScriptableObject.CreateInstance<PowerUpCatalog>();
        try
        {
            var so = new UnityEditor.SerializedObject(catalog);
            var entries = so.FindProperty("entries");
            entries.arraySize = 3;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("dropWeight").floatValue = 1f;
            entries.GetArrayElementAtIndex(1).FindPropertyRelative("dropWeight").floatValue = 0f;
            entries.GetArrayElementAtIndex(2).FindPropertyRelative("dropWeight").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(0, catalog.PickWeighted(0f));
            Assert.AreEqual(0, catalog.PickWeighted(0.24f));
            Assert.AreEqual(2, catalog.PickWeighted(0.25f), "Un poids nul ne tombe jamais.");
            Assert.AreEqual(2, catalog.PickWeighted(0.999f));
            Assert.AreEqual(2, catalog.PickWeighted(1f), "Le bout du tirage reste dans le catalogue.");
            Assert.IsNull(catalog.Get(3));
            Assert.AreEqual("objet", catalog.NameOf(-1));
        }
        finally
        {
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void UneArmeLancee_ArriveEnMainChargeurPlein()
    {
        var magazine = new WeaponController.MagazineState { equipped = 0, ammo = 3, reloadEndsAt = 5.0 };
        WeaponController.ApplyPowerUp(ref magazine, 1, 10.0, 0.8f, 5);
        Assert.AreEqual(1, magazine.equipped, "L'arme lancée en main, même en pleine manche : elle remplace la sienne.");
        Assert.AreEqual(5, magazine.ammo, "Chargeur plein.");
        Assert.AreEqual(10.8, magazine.readyAt, 1e-6, "Elle met le temps de sortir.");
        Assert.AreEqual(0.0, magazine.reloadEndsAt, "Le rechargement en cours est abandonné.");

        // La même arme que celle qu'on tient : un chargeur plein (une arme laser froide), sans la ressortir.
        magazine.ammo = 0;
        magazine.heat = 0.7f;
        magazine.overheated = true;
        magazine.reloadEndsAt = 12.0;
        WeaponController.ApplyPowerUp(ref magazine, 1, 11.0, 0.8f, 5);
        Assert.AreEqual(5, magazine.ammo);
        Assert.AreEqual(0f, magazine.heat);
        Assert.IsFalse(magazine.overheated);
        Assert.AreEqual(10.8, magazine.readyAt, 1e-6, "Pas de nouvelle sortie d'arme.");
        Assert.AreEqual(0.0, magazine.reloadEndsAt);

        WeaponController.ApplyPowerUp(ref magazine, WeaponController.MaxSlots, 12.0, 0.8f, 5);
        Assert.AreEqual(1, magazine.equipped, "Une place hors de l'arsenal ne change rien.");
    }

    // ------------------------------------------------------------------
    // Le vol d'un objet
    // ------------------------------------------------------------------

    [Test]
    public void UnObjetVole_SurUneParabole_EtLeTronconEnVigueurSuitLeTemps()
    {
        var m = new ArenaItem.Motion { time = 2.0, position = new Vector3(0f, 5f, 0f), velocity = new Vector3(0f, 7f, 10f), phase = ArenaItem.Phase.Flying };
        Vector3 p = ArenaItem.Evaluate(m, 3.0, -14f);
        Assert.AreEqual(10f, p.z, 1e-4f);
        Assert.AreEqual(5f + 7f - 7f, p.y, 1e-4f, "y = y0 + vy·t + g·t²/2.");
        Assert.AreEqual(new Vector3(0f, 7f - 14f, 10f), ArenaItem.VelocityAt(m, 3.0, -14f));
        Assert.AreEqual(m.position, ArenaItem.Evaluate(m, 1.0, -14f), "Avant son départ, il est à son point de départ.");

        var resting = new ArenaItem.Motion { time = 4.0, position = new Vector3(1f, 0f, 1f), phase = ArenaItem.Phase.Resting, segment = 1 };
        Assert.AreEqual(resting.position, ArenaItem.Evaluate(resting, 9.0, -14f), "Posé, il ne bouge plus.");

        var segments = new List<ArenaItem.Motion> { m, resting };
        Assert.AreEqual(0, ArenaItem.SegmentAt(segments, 3.9));
        Assert.AreEqual(1, ArenaItem.SegmentAt(segments, 4.0));
        Assert.AreEqual(0, ArenaItem.SegmentAt(segments, 0.0), "Avant le premier tronçon : le premier.");
    }

    [Test]
    public void ApresUnChoc_LeMaterielSePoseAuSol_OuRebonditSurLeMur()
    {
        ArenaItem.Motion floor = ArenaItem.AfterImpact(new Vector3(0f, 0.2f, 0f), Vector3.up, new Vector3(3f, -8f, 0f), 5.0, 0.3f, 1, 4, 1);
        Assert.AreEqual(ArenaItem.Phase.Resting, floor.phase, "Un sol : il s'y pose.");
        Assert.AreEqual(5.0, floor.time);

        ArenaItem.Motion wall = ArenaItem.AfterImpact(Vector3.zero, Vector3.left, new Vector3(10f, 2f, 0f), 5.0, 0.3f, 1, 4, 2);
        Assert.AreEqual(ArenaItem.Phase.Flying, wall.phase, "Un mur : il rebondit.");
        Assert.Less(wall.velocity.x, 0f, "Renvoyé loin du mur.");
        Assert.AreEqual(3f, -wall.velocity.x, 1e-4f, "Il perd de sa vitesse.");
        Assert.AreEqual(2, wall.segment);

        ArenaItem.Motion tired = ArenaItem.AfterImpact(Vector3.zero, Vector3.left, new Vector3(10f, 2f, 0f), 5.0, 0.3f, 4, 4, 3);
        Assert.AreEqual(Vector3.zero, tired.velocity, "Trop de rebonds : il glisse le long du mur, sans plus rebondir (et sans monter).");
        Assert.AreEqual(ArenaItem.Phase.Flying, tired.phase);
    }

    // ------------------------------------------------------------------
    // Les commandes d'un spectateur
    // ------------------------------------------------------------------

    [Test]
    public void UnSpectateur_NaNiTirNiLeanNiFranchissement()
    {
        // Sa visée passe : c'est sa garde (2026-10-27), que le filtre ne touche plus.
        bool fire = true, firePressed = true, jump = true;
        int lean = -1;
        PlayerLocomotion.SpectatorInputFilter(ref fire, ref firePressed, ref lean, ref jump);
        Assert.IsFalse(fire);
        Assert.IsFalse(firePressed);
        Assert.AreEqual(0, lean);
        Assert.IsFalse(jump);
    }

    // ------------------------------------------------------------------
    // Les poings : l'enchaînement, la garde, la molette (2026-10-27)
    // ------------------------------------------------------------------

    [Test]
    public void LesCoupsSEnchainent_GauchePuisDroite_EtRepartentDuGaucheApresUnePause()
    {
        const float duration = 0.45f, window = 0.35f;
        Assert.IsFalse(SpectatorRules.NextPunchIsRight(false, double.PositiveInfinity, duration, window), "Le premier coup part du gauche.");
        Assert.IsFalse(SpectatorRules.NextPunchIsRight(true, double.PositiveInfinity, duration, window), "Même après un droit, s'il est loin.");
        Assert.IsTrue(SpectatorRules.NextPunchIsRight(false, 0.45, duration, window), "Relancé à la fin du gauche : le droit.");
        Assert.IsFalse(SpectatorRules.NextPunchIsRight(true, 0.45, duration, window), "Puis le gauche : gauche, droite, gauche...");
        Assert.IsTrue(SpectatorRules.NextPunchIsRight(false, 0.79, duration, window), "Jusqu'à la fin de la fenêtre, l'enchaînement tient.");
        Assert.IsFalse(SpectatorRules.NextPunchIsRight(false, 0.81, duration, window), "Après une pause, il repart du gauche.");
    }

    [Test]
    public void UnCoupDemandeEnGardeOuEnVisantUnLancer_EstPerdu_SinonIlAttendLaFinDuGeste()
    {
        Assert.IsTrue(SpectatorRules.PunchStillWanted(false, true, false, false), "Un appui demande un coup.");
        Assert.IsTrue(SpectatorRules.PunchStillWanted(true, false, false, false), "Retenu pendant un coup, il attend la fin du geste.");
        Assert.IsFalse(SpectatorRules.PunchStillWanted(false, false, false, false), "Sans appui, rien.");
        Assert.IsFalse(SpectatorRules.PunchStillWanted(true, true, true, false), "En garde, on ne frappe pas : l'appui est perdu, rien ne part quand elle retombe.");
        Assert.IsFalse(SpectatorRules.PunchStillWanted(false, true, false, true), "V en visant un lancer : perdu, aucun coup ne part juste après le lancer.");
        Assert.IsFalse(SpectatorRules.PunchStillWanted(true, false, false, true), "Un coup retenu quand la visée commence est perdu aussi.");
    }

    /// <summary>Le lancer visé (2026-10-27, demandé par l'utilisateur) : le clic droit, l'objet en main, vise
    /// et montre la trajectoire ; ni l'objet rangé, ni hors des phases où l'on lance, ni en plein coup.</summary>
    [Test]
    public void LeClicDroit_LObjetEnMain_ViseUnLancer()
    {
        Assert.IsTrue(SpectatorRules.ThrowAimHeld(true, true, true, false), "L'objet en main, le clic droit tenu : on vise.");
        Assert.IsFalse(SpectatorRules.ThrowAimHeld(true, false, true, false), "Clic droit relâché : on renonce.");
        Assert.IsFalse(SpectatorRules.ThrowAimHeld(false, true, true, false), "Les poings sortis (rien en main, ou l'objet rangé) : c'est la garde.");
        Assert.IsFalse(SpectatorRules.ThrowAimHeld(true, true, false, false), "Hors des phases où l'on lance.");
        Assert.IsFalse(SpectatorRules.ThrowAimHeld(true, true, true, true), "Pas en plein coup de poing.");
    }

    [Test]
    public void LaGarde_ArreteCeQuiVientDeFace_PasDeCoteNiDeDosNiDAuDessus()
    {
        const float half = 70f;
        Assert.IsTrue(SpectatorRules.GuardCovers(0f, new Vector3(0f, 0f, 1f), half), "De face.");
        Assert.IsTrue(SpectatorRules.GuardCovers(0f, new Vector3(1f, 0f, 1f), half), "De trois quarts.");
        Assert.IsTrue(SpectatorRules.GuardCovers(0f, new Vector3(0f, 0.4f, 1f), half), "Un peu au-dessus du regard.");
        Assert.IsFalse(SpectatorRules.GuardCovers(0f, new Vector3(1f, 0f, 0f), half), "De côté : la garde ne couvre pas le flanc.");
        Assert.IsFalse(SpectatorRules.GuardCovers(0f, new Vector3(0f, 0f, -1f), half), "Dans le dos.");
        Assert.IsFalse(SpectatorRules.GuardCovers(0f, new Vector3(0f, 1f, 0.1f), half), "Ce qui tombe d'au-dessus de la tête.");
        Assert.IsTrue(SpectatorRules.GuardCovers(90f, new Vector3(1f, 0f, 0f), half), "Le cap du robot compte : tourné vers +X, il pare ce qui vient de +X.");
        Assert.IsFalse(SpectatorRules.GuardCovers(90f, new Vector3(0f, 0f, 1f), half));
        Assert.IsFalse(SpectatorRules.GuardCovers(0f, Vector3.zero, half), "Sans direction, rien n'est paré.");
        Assert.IsFalse(SpectatorRules.GuardCovers(0f, new Vector3(float.NaN, 0f, 1f), half), "Une valeur non finie n'est jamais parée.");
    }

    [Test]
    public void LaGarde_NeParePasBaissee_NiUnObjetEnMain_NiEtourdi()
    {
        const float half = 70f;
        Vector3 ahead = Vector3.forward;
        Assert.IsTrue(SpectatorRules.GuardStops(true, true, false, 0f, ahead, half), "Levée, poings sortis, de face : elle pare.");
        Assert.IsFalse(SpectatorRules.GuardStops(false, true, false, 0f, ahead, half), "Baissée.");
        Assert.IsFalse(SpectatorRules.GuardStops(true, false, false, 0f, ahead, half), "Un objet en main (pas rangé) : rien n'est paré.");
        Assert.IsFalse(SpectatorRules.GuardStops(true, true, true, 0f, ahead, half),
            "Étourdi, même si sa garde n'est pas encore retombée chez le serveur : rien n'est paré.");
        Assert.IsFalse(SpectatorRules.GuardStops(true, true, false, 0f, Vector3.back, half), "Dans le dos.");
    }

    [Test]
    public void LesPoings_SontSortis_LesMainsVidesOuLObjetRange()
    {
        Assert.IsTrue(SpectatorRules.FistsOut(ArenaItemKind.None, false), "Rien en main : les poings.");
        Assert.IsFalse(SpectatorRules.FistsOut(ArenaItemKind.Rock, false), "Un caillou en main : pas de garde.");
        Assert.IsTrue(SpectatorRules.FistsOut(ArenaItemKind.Rock, true), "Le caillou rangé : les poings.");
        Assert.IsTrue(SpectatorRules.FistsOut(ArenaItemKind.PowerUp, true), "Une arme rangée aussi.");
    }
}
