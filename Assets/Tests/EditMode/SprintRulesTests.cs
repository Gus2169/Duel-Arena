using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le sprint (2026-10-10), demandé par l'utilisateur « comme beaucoup de FPS » : vers l'avant
/// seulement, diagonales avant comprises ; on ne recharge pas et on ne frappe pas en courant ; et
/// se mettre à courir pendant un rechargement l'annule, comme dans Call of Duty ou Rainbow Six.
///
/// Ce qui casse ici ne lève aucune erreur : un joueur qui recharge en sprintant vers un abri, ou
/// qui ne peut plus recharger du tout parce qu'il garde la touche de sprint enfoncée.
/// </summary>
public class SprintRulesTests
{
    [Test]
    public void OnSprinteVersLAvant_EtEnDiagonaleAvant_JamaisDeCoteNiEnArriere()
    {
        Assert.IsTrue(PlayerLocomotion.IsSprintDirection(Vector2.up));
        Assert.IsTrue(PlayerLocomotion.IsSprintDirection(new Vector2(1f, 1f).normalized), "diagonale avant droite");
        Assert.IsTrue(PlayerLocomotion.IsSprintDirection(new Vector2(-1f, 1f).normalized), "diagonale avant gauche");

        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(Vector2.right), "de côté");
        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(Vector2.left), "de côté");
        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(Vector2.down), "en arrière");
        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(new Vector2(1f, -1f).normalized), "diagonale arrière");
        // Un stick de manette presque de côté, avec une pointe d'avant : refusé (72°).
        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(new Vector2(0.95f, 0.31f)), "presque de côté");
        Assert.IsFalse(PlayerLocomotion.IsSprintDirection(Vector2.zero), "immobile");
    }

    /// <summary>
    /// La sortie de sprint se compte sur les inputs (2026-10-11) : la somme de leurs durées depuis
    /// le dernier qui voulait sprinter, chacune bornée comme sur le serveur. Le joueur et le serveur
    /// avancent la même horloge sur les mêmes inputs : ce que le joueur a attendu, le serveur le
    /// compte au centième près, sans gigue du réseau.
    /// </summary>
    [Test]
    public void LaSortieDeSprint_SeCompteSurLesInputs_DepuisLeDernierQuiSprintait()
    {
        double clock = 0.0, lastSprint = double.NegativeInfinity;
        Assert.IsTrue(double.IsPositiveInfinity(clock - lastSprint), "Sans sprint, l'arme est prête depuis toujours.");

        for (int i = 0; i < 30; i++) PlayerLocomotion.AdvanceInputClock(ref clock, ref lastSprint, 1f / 60f, true);
        Assert.AreEqual(0.0, clock - lastSprint, 1e-9, "Pendant le sprint, aucun temps ne s'écoule depuis le sprint.");

        for (int i = 0; i < 18; i++) PlayerLocomotion.AdvanceInputClock(ref clock, ref lastSprint, 1f / 60f, false);
        Assert.AreEqual(0.3, clock - lastSprint, 1e-6, "18 inputs à 60 images/s après le sprint : 0,3 s.");

        // Un input de 2 s (le jeu a gelé) ne compte que pour la borne du serveur : sinon un client
        // pourrait annoncer de longs inputs pour raccourcir l'attente d'un coup.
        PlayerLocomotion.AdvanceInputClock(ref clock, ref lastSprint, 0f, true);
        PlayerLocomotion.AdvanceInputClock(ref clock, ref lastSprint, 2f, false);
        Assert.Less(clock - lastSprint, 0.11, "La durée d'un input est bornée comme sur le serveur.");
    }

    /// <summary>On recule et on va de côté moins vite qu'en avançant (2026-10-10) ; les diagonales
    /// avant gardent toute la vitesse, et rien ne saute d'une direction à sa voisine.</summary>
    [Test]
    public void ReculerEtAllerDeCote_ReduisentLaVitesse_LesDiagonalesAvantNon()
    {
        float F(Vector2 move) => PlayerLocomotion.DirectionSpeedMultiplier(move, 0.85f, 0.7f);
        Assert.AreEqual(1f, F(Vector2.up), 1e-4f, "vers l'avant");
        Assert.AreEqual(1f, F(new Vector2(1f, 1f).normalized), 1e-4f, "diagonale avant");
        Assert.AreEqual(0.85f, F(Vector2.right), 1e-4f, "de côté");
        Assert.AreEqual(0.85f, F(Vector2.left), 1e-4f, "de côté");
        Assert.AreEqual(0.7f, F(Vector2.down), 1e-4f, "en arrière");
        Assert.AreEqual(0.775f, F(new Vector2(-1f, -1f).normalized), 1e-4f, "diagonale arrière");
        Assert.AreEqual(1f, F(Vector2.zero), 1e-4f, "immobile");

        // Continue : d'un degré à l'autre, jamais plus que la pente de la plus forte section.
        float previous = F(Vector2.up);
        for (int a = 1; a <= 180; a++)
        {
            float current = F(new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)));
            Assert.LessOrEqual(current, previous + 1e-4f, $"à {a}°, la vitesse remonte");
            Assert.Less(previous - current, 0.005f, $"à {a}°, la vitesse saute");
            previous = current;
        }
    }

    [Test]
    public void LaVisee_EtLeTir_EmpechentDeVouloirSprinter()
    {
        Assert.IsTrue(PlayerLocomotion.WantsToSprint(true, Vector2.up, false, false));
        Assert.IsFalse(PlayerLocomotion.WantsToSprint(false, Vector2.up, false, false), "touche relâchée");
        Assert.IsFalse(PlayerLocomotion.WantsToSprint(true, Vector2.up, true, false), "en visée");
        Assert.IsFalse(PlayerLocomotion.WantsToSprint(true, Vector2.up, false, true), "en tirant");
        Assert.IsFalse(PlayerLocomotion.WantsToSprint(true, Vector2.right, false, false), "de côté");
    }

    [Test]
    public void RechargerEnSprintant_ArreteLeSprint_SansAnnulerLeRechargement()
    {
        bool previous = false;
        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, false, false, out bool cancel), "Le joueur sprinte.");
        Assert.IsFalse(cancel);

        // Il appuie sur recharger, la touche de sprint toujours enfoncée.
        for (int frame = 0; frame < 60; frame++)
        {
            Assert.IsFalse(PlayerLocomotion.GateSprint(true, ref previous, true, false, out cancel),
                $"Image {frame} : il sprinte encore en rechargeant.");
            Assert.IsFalse(cancel, $"Image {frame} : la touche tenue depuis avant a annulé le rechargement.");
        }

        // Le rechargement fini, la touche toujours tenue : le sprint reprend.
        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, false, false, out cancel));
        Assert.IsFalse(cancel);
    }

    [Test]
    public void SeMettreACourirPendantUnRechargement_LAnnule()
    {
        bool previous = false;
        // Il recharge à l'arrêt.
        Assert.IsFalse(PlayerLocomotion.GateSprint(false, ref previous, true, false, out bool cancel));
        Assert.IsFalse(cancel);

        // Il se met à courir : le rechargement est annulé, et il sprinte tout de suite.
        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, true, false, out cancel), "Il ne sprinte pas.");
        Assert.IsTrue(cancel, "Se mettre à courir n'a pas annulé le rechargement.");

        // L'image suivante, le rechargement n'existe plus : rien à annuler.
        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, false, false, out cancel));
        Assert.IsFalse(cancel);
    }

    [Test]
    public void ReprendreLaTouchePendantUnRechargementCommenceEnSprint_LAnnule()
    {
        bool previous = false;
        PlayerLocomotion.GateSprint(true, ref previous, false, false, out _);   // sprint
        PlayerLocomotion.GateSprint(true, ref previous, true, false, out _);    // recharge, touche tenue
        PlayerLocomotion.GateSprint(false, ref previous, true, false, out _);   // touche relâchée

        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, true, false, out bool cancel));
        Assert.IsTrue(cancel, "Relâcher puis reprendre la touche, c'est se remettre à courir.");
    }

    [Test]
    public void PendantLeCoupDeTaser_PasDeSprint_EtLeCoupVaASonTerme()
    {
        bool previous = false;
        PlayerLocomotion.GateSprint(true, ref previous, false, false, out _);   // sprint

        // Coup donné en sprintant : le sprint s'arrête pendant tout le geste, même si le joueur
        // reprend la touche, et le coup n'annule rien d'autre.
        Assert.IsFalse(PlayerLocomotion.GateSprint(true, ref previous, false, true, out bool cancel));
        PlayerLocomotion.GateSprint(false, ref previous, false, true, out cancel);
        Assert.IsFalse(PlayerLocomotion.GateSprint(true, ref previous, false, true, out cancel), "Il sprinte pendant le geste.");
        Assert.IsFalse(cancel);

        // Même un rechargement en cours n'est pas annulé par une reprise de touche pendant le geste
        // (c'est le coup lui-même qui l'annule, voir TaserController).
        Assert.IsFalse(PlayerLocomotion.GateSprint(true, ref previous, true, true, out cancel));
        Assert.IsFalse(cancel);

        // Le geste fini, touche tenue : le sprint reprend.
        Assert.IsTrue(PlayerLocomotion.GateSprint(true, ref previous, false, false, out cancel));
    }
}
