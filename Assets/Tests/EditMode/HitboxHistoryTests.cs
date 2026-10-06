using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests de l'échantillonnage de l'historique de pose — le cœur du rewind.
///
/// C'est le code où une régression coûterait le plus cher ET se verrait le moins : un rewind qui
/// échantillonne mal ne lève aucune erreur, il fait juste rater des tirs qui auraient dû toucher.
/// D'où des tests sur cette fonction en priorité.
/// </summary>
public class HitboxHistoryTests
{
    private static PlayerLocomotion.HitboxPose Pose(float time, float x, float yaw = 0f, float lean = 0f,
        PlayerLocomotion.StanceState? stance = null)
    {
        return new PlayerLocomotion.HitboxPose
        {
            time = time,
            position = new Vector3(x, 0f, 0f),
            yaw = yaw,
            leanOffset = lean,
            stance = stance ?? Etablie(PlayerLocomotion.Stance.Standing),
        };
    }

    private static PlayerLocomotion.StanceState Etablie(PlayerLocomotion.Stance stance)
        => new PlayerLocomotion.StanceState { current = stance, previous = stance, changedAt = double.NegativeInfinity };

    [Test]
    public void HistoriqueVide_RenvoieFalse()
    {
        var history = new List<PlayerLocomotion.HitboxPose>();

        bool found = PlayerLocomotion.SampleHitboxHistory(history, 1f, out _);

        Assert.IsFalse(found, "Un historique vide ne doit rien fournir : l'appelant doit pouvoir " +
                              "renoncer au rewind plutôt que d'utiliser une pose inventée.");
    }

    [Test]
    public void HistoriqueNull_RenvoieFalseSansLever()
    {
        Assert.DoesNotThrow(() => PlayerLocomotion.SampleHitboxHistory(null, 1f, out _));
        Assert.IsFalse(PlayerLocomotion.SampleHitboxHistory(null, 1f, out _));
    }

    [Test]
    public void InstantEntreDeuxPoses_InterpoleLaPosition()
    {
        var history = new List<PlayerLocomotion.HitboxPose> { Pose(10f, 0f), Pose(11f, 10f) };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10.5f, out var result));
        Assert.AreEqual(5f, result.position.x, 0.0001f, "Mi-chemin dans le temps = mi-chemin dans l'espace.");
    }

    [Test]
    public void InstantEntreDeuxPoses_InterpoleLeLean()
    {
        // Le lean déplace la surface touchable : ne pas l'interpoler ferait rater les tirs sur un
        // adversaire en train de peeker, exactement le cas que le rewind doit couvrir.
        var history = new List<PlayerLocomotion.HitboxPose>
        {
            Pose(10f, 0f, lean: 0f),
            Pose(11f, 0f, lean: 0.5f),
        };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10.5f, out var result));
        Assert.AreEqual(0.25f, result.leanOffset, 0.0001f);
    }

    [Test]
    public void YawInterpole_ParLePlusCourtChemin()
    {
        // LerpAngle et non Lerp : entre 350° et 10°, le chemin court passe par 0°, pas par 180°.
        var history = new List<PlayerLocomotion.HitboxPose>
        {
            Pose(10f, 0f, yaw: 350f),
            Pose(11f, 0f, yaw: 10f),
        };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10.5f, out var result));

        float normalized = Mathf.Repeat(result.yaw, 360f);
        bool nearZero = normalized < 1f || normalized > 359f;
        Assert.IsTrue(nearZero, $"Attendu ~0°/360°, obtenu {normalized}° — signe d'un Lerp linéaire " +
                                "au lieu d'un LerpAngle, qui ferait tourner le hitbox à l'envers.");
    }

    [Test]
    public void LaPosture_EstCelleEnVigueurALInstantVise()
    {
        // La posture ne s'interpole pas : elle porte l'instant exact de son changement (10,4 ici),
        // et la transition se recalcule ensuite à l'instant visé. Avant le changement, on doit
        // retrouver l'ancienne posture ; après, la nouvelle, avec sa transition.
        var accroupi = new PlayerLocomotion.StanceState
        {
            current = PlayerLocomotion.Stance.Crouching,
            previous = PlayerLocomotion.Stance.Standing,
            changedAt = 10.4,
        };
        var history = new List<PlayerLocomotion.HitboxPose>
        {
            Pose(10f, 0f, stance: Etablie(PlayerLocomotion.Stance.Standing)),
            Pose(11f, 0f, stance: accroupi),
        };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10.2f, out var avant));
        Assert.AreEqual(PlayerLocomotion.Stance.Standing, avant.stance.current, "Avant le changement, la posture d'avant.");

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10.6f, out var apres));
        Assert.AreEqual(PlayerLocomotion.Stance.Crouching, apres.stance.current, "Après le changement, la nouvelle posture.");
        Assert.AreEqual(10.4, apres.stance.changedAt, 1e-9, "L'instant du changement doit survivre à l'échantillonnage.");
    }

    [Test]
    public void InstantPlusAncienQueLHistorique_SeRabatSurLaPlusVieillePose()
    {
        // Arrive quand un client annonce un rewind plus grand que la fenêtre conservée. Mieux vaut
        // la pose la plus ancienne connue qu'un échec silencieux.
        var history = new List<PlayerLocomotion.HitboxPose> { Pose(10f, 1f), Pose(11f, 2f) };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 5f, out var result));
        Assert.AreEqual(1f, result.position.x, 0.0001f);
    }

    [Test]
    public void InstantDansLeFutur_SeRabatSurLaPoseLaPlusRecente()
    {
        var history = new List<PlayerLocomotion.HitboxPose> { Pose(10f, 1f), Pose(11f, 2f) };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 99f, out var result));
        Assert.AreEqual(2f, result.position.x, 0.0001f);
    }

    [Test]
    public void PosesAuMemeInstant_NeDivisePasParZero()
    {
        // Deux frames enregistrées au même Time.time (possible si deux écritures tombent dans la
        // même frame) : la division par l'écart de temps doit être protégée.
        var history = new List<PlayerLocomotion.HitboxPose> { Pose(10f, 1f), Pose(10f, 2f) };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 10f, out var result));
        Assert.IsFalse(float.IsNaN(result.position.x), "NaN = division par zéro non protégée.");
    }

    [Test]
    public void UneSeulePose_LaRenvoieQuelQueSoitLInstant()
    {
        var history = new List<PlayerLocomotion.HitboxPose> { Pose(10f, 7f) };

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 3f, out var avant));
        Assert.AreEqual(7f, avant.position.x, 0.0001f);

        Assert.IsTrue(PlayerLocomotion.SampleHitboxHistory(history, 30f, out var apres));
        Assert.AreEqual(7f, apres.position.x, 0.0001f);
    }
}
