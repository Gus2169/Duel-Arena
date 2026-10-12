using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le pivot sur place (2026-10-10) : à l'arrêt, le bas du corps garde son cap pendant que le joueur
/// tourne, et pivote vers le regard au-delà de PlayerAnimator.TurnThreshold. Sans ça, le robot
/// pivotait d'un bloc et ses pieds tournaient en glissant sur le sol.
/// </summary>
public class LegsTurnTests
{
    private const float Dt = 1f / 60f;

    private static PlayerAnimator.LegsTurnState Start(float yaw)
    {
        var s = new PlayerAnimator.LegsTurnState();
        PlayerAnimator.StepLegsTurn(ref s, yaw, true, true, Dt);
        return s;
    }

    [Test]
    public void ALArret_UnPetitRegardDeCote_NeFaitPasTournerLesJambes()
    {
        var s = Start(10f);
        for (int i = 0; i < 120; i++) PlayerAnimator.StepLegsTurn(ref s, 10f + 40f, true, true, Dt);
        Assert.AreEqual(10f, s.legsYaw, 0.01f, "Les jambes ont suivi un regard de 40° : le buste suffit.");
    }

    [Test]
    public void AuDelaDuSeuil_LesJambesPivotentJusquAuRegard_SansAllerDUnBloc()
    {
        var s = Start(0f);
        PlayerAnimator.StepLegsTurn(ref s, 100f, true, true, Dt);
        float afterOneFrame = Mathf.DeltaAngle(0f, s.legsYaw);
        Assert.Greater(afterOneFrame, 0f, "Au-delà du seuil, les jambes doivent se mettre à pivoter.");
        Assert.LessOrEqual(afterOneFrame, PlayerAnimator.TurnSpeed * Dt + 0.01f, "Les jambes ont sauté d'un bloc vers le regard.");

        for (int i = 0; i < 120; i++) PlayerAnimator.StepLegsTurn(ref s, 100f, true, true, Dt);
        Assert.AreEqual(100f, s.legsYaw, 0.5f, "Le pivot ne finit pas sur le regard.");

        var t = Start(0f);
        PlayerAnimator.StepLegsTurn(ref t, -100f, true, true, Dt);
        Assert.Less(Mathf.DeltaAngle(0f, t.legsYaw), 0f, "Regard à gauche : les jambes pivotent à gauche.");
    }

    [Test]
    public void EnMarchant_LesJambesSeRealignentSurLeRegard()
    {
        var s = Start(0f);
        PlayerAnimator.StepLegsTurn(ref s, 60f, true, true, Dt);
        Assert.AreEqual(0f, s.legsYaw, 0.01f);
        for (int i = 0; i < 12; i++) PlayerAnimator.StepLegsTurn(ref s, 60f, false, true, Dt);
        Assert.AreEqual(60f, s.legsYaw, 0.01f, "En marche, les jambes doivent suivre le regard (l'arbre suppose un modèle aligné).");
    }

    [Test]
    public void EnTournantTresVite_LesJambesNeRestentJamaisTropEnArriere()
    {
        var s = Start(0f);
        PlayerAnimator.StepLegsTurn(ref s, 170f, true, true, Dt);
        Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(s.legsYaw, 170f)), PlayerAnimator.MaxLegsLag + 0.01f,
            "Le buste ne pourrait plus faire face au regard.");
    }
}
