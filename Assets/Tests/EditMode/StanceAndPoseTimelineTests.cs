using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La ligne de temps de l'affichage (2026-10-06) : transitions de posture et poses datées par le
/// serveur.
///
/// Ces deux fonctions décident de ce qu'un joueur VOIT de son adversaire et de l'endroit où le
/// serveur le replace pour juger un tir. Une erreur n'y lève rien : l'adversaire saccade, se
/// couche trop tôt, ou se fait toucher là où on ne le voyait pas.
/// </summary>
public class StanceAndPoseTimelineTests
{
    private const float Eps = 1e-4f;

    // ------------------------------------------------------------------
    // Transitions de posture
    // ------------------------------------------------------------------

    [Test]
    public void Transition_VaDeZeroAUn_EnDouceur()
    {
        Assert.AreEqual(0f, PlayerLocomotion.TransitionProgress(10.0, 10.0, 0.6f), Eps, "Au changement, rien n'a encore bougé.");
        Assert.AreEqual(0.5f, PlayerLocomotion.TransitionProgress(10.0, 10.3, 0.6f), Eps, "À mi-durée, à mi-chemin.");
        Assert.AreEqual(1f, PlayerLocomotion.TransitionProgress(10.0, 10.6, 0.6f), Eps, "À la fin, arrivé.");
        Assert.AreEqual(1f, PlayerLocomotion.TransitionProgress(10.0, 99.0, 0.6f), Eps, "Longtemps après, toujours arrivé.");
        Assert.AreEqual(0f, PlayerLocomotion.TransitionProgress(10.0, 9.0, 0.6f), Eps, "Avant le changement, rien.");

        // Départ et arrivée adoucis : le premier dixième fait bien moins d'un dixième du chemin.
        // Une progression linéaire donnerait un corps qui démarre et s'arrête d'un coup.
        Assert.Less(PlayerLocomotion.TransitionProgress(10.0, 10.06, 0.6f), 0.05f);
    }

    [Test]
    public void Transition_SansDuree_EstImmediate()
    {
        // Apparition et début de manche : changedAt vaut -∞, et une durée nulle ne doit pas diviser par zéro.
        Assert.AreEqual(1f, PlayerLocomotion.TransitionProgress(double.NegativeInfinity, 0.0, 0.6f), Eps);
        Assert.AreEqual(1f, PlayerLocomotion.TransitionProgress(10.0, 10.0, 0f), Eps);
    }

    [Test]
    public void PostureAffichee_AvantLeChangement_ResteLAncienneEtablie()
    {
        // Un spectateur affiche le passé : s'il regarde un instant antérieur au changement, il doit
        // voir l'ancienne posture, complète — pas le début d'une transition qui n'a pas encore eu lieu.
        var state = new PlayerLocomotion.StanceState
        {
            current = PlayerLocomotion.Stance.Prone,
            previous = PlayerLocomotion.Stance.Standing,
            changedAt = 20.0,
        };

        PlayerLocomotion.ResolveStance(state, 19.9, 0.9f, out var from, out var to, out float progress);
        Assert.AreEqual(PlayerLocomotion.Stance.Standing, from);
        Assert.AreEqual(PlayerLocomotion.Stance.Standing, to);
        Assert.AreEqual(1f, progress, Eps);

        PlayerLocomotion.ResolveStance(state, 20.45, 0.9f, out from, out to, out progress);
        Assert.AreEqual(PlayerLocomotion.Stance.Standing, from);
        Assert.AreEqual(PlayerLocomotion.Stance.Prone, to);
        Assert.AreEqual(0.5f, progress, Eps, "À mi-durée, à mi-chemin de la posture d'arrivée.");
    }

    // ------------------------------------------------------------------
    // Poses datées par le serveur
    // ------------------------------------------------------------------

    private static PlayerLocomotion.DisplayPose Pose(double time, float x, float yaw = 0f, float lean = 0f, byte flags = 0)
    {
        return new PlayerLocomotion.DisplayPose
        {
            time = time,
            position = new Vector3(x, 0f, 0f),
            yaw = yaw,
            leanOffset = lean,
            flags = flags,
        };
    }

    [Test]
    public void Pose_InterpoleeSelonLaDateDuServeur_EtNonSelonLArrivee()
    {
        // Deux poses à 33 ms d'écart côté serveur. Peu importe quand elles sont ARRIVÉES : à mi-temps
        // entre leurs dates, on est à mi-chemin. C'était tout le défaut de l'ancienne datation à
        // l'arrivée, où la gigue du réseau devenait des variations de vitesse.
        var poses = new List<PlayerLocomotion.DisplayPose> { Pose(5.000, 0f, lean: 0f), Pose(5.033, 0.33f, lean: 0.2f) };

        PlayerLocomotion.DisplayPose p = PlayerLocomotion.SampleDisplayPose(poses, 5.0165);
        Assert.AreEqual(0.165f, p.position.x, Eps);
        Assert.AreEqual(0.1f, p.leanOffset, Eps, "Le lean doit suivre la même ligne de temps que la position.");
    }

    [Test]
    public void Pose_LaRotationPrendLePlusCourtChemin()
    {
        var poses = new List<PlayerLocomotion.DisplayPose> { Pose(1.0, 0f, yaw: 350f), Pose(2.0, 0f, yaw: 10f) };
        float yaw = Mathf.Repeat(PlayerLocomotion.SampleDisplayPose(poses, 1.5).yaw, 360f);
        Assert.IsTrue(yaw < 1f || yaw > 359f, $"Attendu ~0°, obtenu {yaw}° : l'adversaire tournerait à l'envers.");
    }

    [Test]
    public void Pose_HorsDeLHistorique_PoseLaPlusProche_SansExtrapoler()
    {
        var poses = new List<PlayerLocomotion.DisplayPose> { Pose(1.0, 1f), Pose(2.0, 2f) };
        Assert.AreEqual(1f, PlayerLocomotion.SampleDisplayPose(poses, 0.5).position.x, Eps);
        Assert.AreEqual(2f, PlayerLocomotion.SampleDisplayPose(poses, 9.0).position.x, Eps,
            "Au-delà de la dernière pose, on montre la dernière connue, sans deviner la suite.");
    }

    [Test]
    public void Pose_LesDrapeauxDAnimationSuiventLaPoseDeDebutDIntervalle()
    {
        // Drapeaux discrets (au sol, franchissement, visée) : pas d'interpolation, celle du début.
        var poses = new List<PlayerLocomotion.DisplayPose> { Pose(1.0, 0f, flags: 1), Pose(2.0, 0f, flags: 0), Pose(3.0, 0f, flags: 0) };
        Assert.AreEqual(1, PlayerLocomotion.SampleDisplayPose(poses, 1.9).flags);
        Assert.AreEqual(0, PlayerLocomotion.SampleDisplayPose(poses, 2.1).flags);
    }
}
