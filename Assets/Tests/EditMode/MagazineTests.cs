using System.Collections.Generic;
using NUnit.Framework;
using MagazineState = WeaponController.MagazineState;

/// <summary>
/// Le chargeur (2026-10-08) : tenu par le serveur, prédit par le tireur.
///
/// Les défauts qu'on veut empêcher ne lèvent aucune erreur : un tir à vide accepté, un compteur
/// qui remonte d'une balle en pleine rafale, un premier tir refusé juste après un rechargement
/// alors que le joueur l'a vu partir. Les règles sont des fonctions pures de WeaponController ;
/// le scénario sous latence les fait jouer ensemble, avec un tireur qui prédit et un serveur
/// qui reçoit tout en retard.
/// </summary>
public class MagazineTests
{
    private const int Mag = 30;
    private const float Reload = 2.2f;

    private static MagazineState Full() => new MagazineState { ammo = Mag };

    // ------------------------------------------------------------------
    // Règles du serveur
    // ------------------------------------------------------------------

    [Test]
    public void Tirer_ConsommeUneBalle()
    {
        MagazineState m = Full();
        Assert.IsTrue(WeaponController.TryConsumeRound(ref m, 1.0, Mag));
        Assert.AreEqual(Mag - 1, m.ammo);
    }

    [Test]
    public void AVide_LeTirEstRefuse()
    {
        var m = new MagazineState { ammo = 0 };
        Assert.IsFalse(WeaponController.TryConsumeRound(ref m, 1.0, Mag));
        Assert.AreEqual(0, m.ammo);
    }

    [Test]
    public void PendantUnRechargement_LeTirEstRefuse()
    {
        var m = new MagazineState { ammo = 12 };
        Assert.IsTrue(WeaponController.TryStartReload(ref m, 10.0, Mag, Reload));
        Assert.IsFalse(WeaponController.TryConsumeRound(ref m, 11.0, Mag), "On ne tire pas en rechargeant, même avec des balles restantes.");
        Assert.AreEqual(12, m.ammo);
    }

    /// <summary>Le rechargement automatique (2026-10-27, demandé par l'utilisateur) : la gâchette pressée
    /// chargeur vide recharge ; une arme automatique, dès la dernière balle, tant qu'on tient la gâchette.</summary>
    [Test]
    public void AVide_LaGachetteRecharge_CommeDansLesFPSClassiques()
    {
        Assert.IsTrue(WeaponController.AutoReloadWanted(true, true, false, false, 0, false), "Une arme au coup par coup : l'appui à vide recharge.");
        Assert.IsTrue(WeaponController.AutoReloadWanted(false, true, true, false, 0, false), "Une arme automatique : la gâchette tenue sur la dernière balle.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(false, true, false, false, 0, false),
            "Au coup par coup, la gâchette restée enfoncée depuis le dernier tir ne recharge pas : il faut un appui.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(true, true, true, false, 1, false), "Une balle reste : on tire.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(true, true, true, false, 0, true), "Déjà en train de recharger.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(true, true, true, true, 0, false), "Une arme laser se vide d'elle-même.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(false, false, true, false, 0, false), "Sans la gâchette, rien.");
    }

    [Test]
    public void ChargeurPlein_LeRechargementEstRefuse()
    {
        MagazineState m = Full();
        Assert.IsFalse(WeaponController.TryStartReload(ref m, 1.0, Mag, Reload));
        Assert.AreEqual(0.0, m.reloadEndsAt);
    }

    [Test]
    public void RechargementDejaEnCours_UneNouvelleDemandeNeRepoussePasLaFin()
    {
        var m = new MagazineState { ammo = 3 };
        WeaponController.TryStartReload(ref m, 10.0, Mag, Reload);
        double end = m.reloadEndsAt;
        Assert.IsFalse(WeaponController.TryStartReload(ref m, 11.0, Mag, Reload));
        Assert.AreEqual(end, m.reloadEndsAt);
    }

    [Test]
    public void LeRechargement_RemplitLeChargeurAuBoutDeSaDuree_PasAvant()
    {
        var m = new MagazineState { ammo = 0 };
        WeaponController.TryStartReload(ref m, 10.0, Mag, Reload);

        Assert.IsFalse(WeaponController.CompleteReloadIfDue(ref m, 10.0 + Reload - 0.01, Mag));
        Assert.AreEqual(0, m.ammo);

        Assert.IsTrue(WeaponController.CompleteReloadIfDue(ref m, 10.0 + Reload, Mag));
        Assert.AreEqual(Mag, m.ammo);
        Assert.AreEqual(0.0, m.reloadEndsAt);
    }

    [Test]
    public void UnTirJusteAvantLaFin_TermineLeRechargement_UnTirTropTot_EstRefuse()
    {
        var m = new MagazineState { ammo = 0 };
        WeaponController.TryStartReload(ref m, 10.0, Mag, Reload);
        double end = m.reloadEndsAt;

        MagazineState tropTot = m;
        Assert.IsFalse(WeaponController.TryConsumeRound(ref tropTot, end - WeaponController.ReloadCompletionTolerance - 0.01, Mag));

        // Une valeur fixe, pas une fraction de la marge : un test réglé sur la marge resterait vert
        // avec une marge nulle (vu par mutation le 2026-10-08).
        MagazineState justeAvant = m;
        Assert.IsTrue(WeaponController.TryConsumeRound(ref justeAvant, end - 0.02, Mag),
            "La gigue du réseau peut faire arriver le premier tir 20 ms avant la fin.");
        Assert.AreEqual(Mag - 1, justeAvant.ammo);
    }

    // ------------------------------------------------------------------
    // Recalage du chargeur prédit
    // ------------------------------------------------------------------

    [Test]
    public void DemandesEnRoute_LePreditNeBougePas()
    {
        // Le serveur n'a vu que 3 des 5 tirs : son compte est en retard, pas faux.
        var server = new MagazineState { ammo = 27, actionsProcessed = 3 };
        int local = 25;
        bool reloading = false;
        WeaponController.ReconcileMagazine(server, 5, ref local, ref reloading);
        Assert.AreEqual(25, local);
    }

    [Test]
    public void ToutEstAccuse_LeServeurFaitFoi()
    {
        // Un tir refusé par le serveur (cadence, hors manche) : le tireur l'avait décompté.
        var server = new MagazineState { ammo = 26, actionsProcessed = 5 };
        int local = 25;
        bool reloading = false;
        WeaponController.ReconcileMagazine(server, 5, ref local, ref reloading);
        Assert.AreEqual(26, local);
    }

    [Test]
    public void LeServeurRechargeEncore_LeTireurQuiAFiniGardeSonChargeur()
    {
        // Le tireur a fini de recharger quelques millisecondes avant le serveur : recopier le
        // chargeur vide du serveur ferait clignoter son compteur à 0.
        var server = new MagazineState { ammo = 0, actionsProcessed = 7, reloadEndsAt = 20.0 };
        int local = Mag;
        bool reloading = false;
        WeaponController.ReconcileMagazine(server, 7, ref local, ref reloading);
        Assert.AreEqual(Mag, local);
        Assert.IsFalse(reloading);
    }

    [Test]
    public void RechargementRefuseOuAnnule_LePreditSArrete()
    {
        // Début de manche pendant un rechargement : le serveur remplit le chargeur et l'annule.
        var server = new MagazineState { ammo = Mag, actionsProcessed = 7 };
        int local = 4;
        bool reloading = true;
        WeaponController.ReconcileMagazine(server, 7, ref local, ref reloading);
        Assert.IsFalse(reloading);
        Assert.AreEqual(Mag, local);
    }

    [Test]
    public void CompteurDeDemandes_QuiReboucle_ResteEnRoute()
    {
        var server = new MagazineState { ammo = 10, actionsProcessed = ushort.MaxValue };
        int local = 9;
        bool reloading = false;
        WeaponController.ReconcileMagazine(server, 0, ref local, ref reloading); // 65 535 + 1 = 0
        Assert.AreEqual(9, local);
    }

    // ------------------------------------------------------------------
    // Scénario complet sous latence
    // ------------------------------------------------------------------

    private struct Outcome
    {
        public int rejected;
        public int localIncreases;
        public int finalLocal;
        public int finalServer;
        public int shotsFired;
    }

    /// <summary>
    /// Un tireur vide son chargeur en rafale, recharge, puis tire encore cinq balles. Les messages
    /// mettent `Latency` à aller et autant à revenir, dans l'ordre (RPC fiables). L'horloge
    /// prédite du tireur est en avance du trajet et d'une marge (comme NetworkManager.LocalTime).
    /// `reloadExtraLatency` retarde la seule demande de rechargement : c'est la gigue qui fait
    /// arriver le premier tir suivant un peu avant la fin du rechargement côté serveur.
    /// </summary>
    private static Outcome Simulate(double reloadExtraLatency)
    {
        const double Dt = 1.0 / 60.0;
        const double Latency = 0.075;
        const double ClockMargin = 0.05;
        const double FireInterval = 1.0 / 7.0;
        const int ExtraShots = 5;

        MagazineState server = Full();
        MagazineState seenByClient = server;
        var toServer = new Queue<(double arrival, bool reload)>();
        var toClient = new Queue<(double arrival, MagazineState state)>();

        int localAmmo = Mag;
        bool localReloading = false;
        double localReloadEnd = 0.0;
        ushort sent = 0;
        double cooldown = 0.0;
        bool reloaded = false;
        double lastArrival = 0.0;

        var o = new Outcome();
        for (double t = 0.0; t < 12.0; t += Dt)
        {
            // Serveur : traite ce qui est arrivé, termine un rechargement échu, publie.
            while (toServer.Count > 0 && toServer.Peek().arrival <= t)
            {
                bool reload = toServer.Dequeue().reload;
                server.actionsProcessed++;
                if (reload) WeaponController.TryStartReload(ref server, t, Mag, Reload);
                else if (!WeaponController.TryConsumeRound(ref server, t, Mag)) o.rejected++;
            }
            WeaponController.CompleteReloadIfDue(ref server, t, Mag);
            toClient.Enqueue((t + Latency, server));

            while (toClient.Count > 0 && toClient.Peek().arrival <= t) seenByClient = toClient.Dequeue().state;

            // Tireur : même enchaînement que WeaponController.Update.
            double clock = t + Latency + ClockMargin;
            int before = localAmmo;
            if (localReloading && clock >= localReloadEnd)
            {
                localReloading = false;
                localAmmo = Mag;
            }
            WeaponController.ReconcileMagazine(seenByClient, sent, ref localAmmo, ref localReloading);
            if (localAmmo > before) o.localIncreases++;

            if (!reloaded && localAmmo == 0 && !localReloading)
            {
                reloaded = true;
                localReloading = true;
                localReloadEnd = clock + Reload;
                sent++;
                lastArrival = System.Math.Max(lastArrival, t + Latency + reloadExtraLatency);
                toServer.Enqueue((lastArrival, true));
            }

            cooldown -= Dt;
            if (o.shotsFired < Mag + ExtraShots && cooldown <= 0.0 && localAmmo > 0 && !localReloading)
            {
                cooldown = FireInterval;
                localAmmo--;
                sent++;
                o.shotsFired++;
                lastArrival = System.Math.Max(lastArrival, t + Latency); // RPC fiables : dans l'ordre
                toServer.Enqueue((lastArrival, false));
            }
        }

        o.finalLocal = localAmmo;
        o.finalServer = server.ammo;
        return o;
    }

    [Test]
    public void RafalePuisRechargement_SousLatence_AucunTirRefuse_EtLeCompteurNeRemonteQuAuRechargement()
    {
        Outcome o = Simulate(0.0);
        Assert.AreEqual(Mag + 5, o.shotsFired, "Garde anti-vacuité : la rafale, le rechargement et la reprise ont bien eu lieu.");
        Assert.AreEqual(0, o.rejected, "Le serveur a refusé des tirs que le tireur a vus partir.");
        Assert.AreEqual(1, o.localIncreases, "Le compteur du tireur est remonté ailleurs qu'à la fin du rechargement (clignotement).");
        Assert.AreEqual(Mag - 5, o.finalLocal);
        Assert.AreEqual(Mag - 5, o.finalServer);
    }

    [Test]
    public void GigueSurLaDemandeDeRechargement_DansLaMarge_LePremierTirSuivantPasse()
    {
        // La demande de rechargement a mis 30 ms de plus que les tirs : le premier tir après le
        // rechargement arrive 30 ms avant la fin calculée par le serveur.
        Outcome o = Simulate(0.03);
        Assert.AreEqual(0, o.rejected, "Le premier tir après le rechargement a été refusé pour quelques millisecondes.");
        Assert.AreEqual(Mag - 5, o.finalServer);
    }

    [Test]
    public void GigueAuDelaDeLaMarge_LeTirEstRefuse_MaisLesComptesFinissentDAccord()
    {
        Outcome o = Simulate(0.1);
        Assert.Greater(o.rejected, 0, "Garde anti-vacuité : ce scénario doit faire refuser un tir.");
        Assert.AreEqual(o.finalServer, o.finalLocal, "Après un refus, le tireur doit se recaler sur le serveur.");
    }
}
