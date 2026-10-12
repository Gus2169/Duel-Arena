using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Ce que le serveur accepte d'un client (relecture complète du 2026-10-12).
///
/// Avec NaN, toutes les comparaisons sont fausses : un garde-fou écrit « refuser si trop loin » ou
/// « refuser si trop tôt » laisse donc passer. Une seule durée d'input NaN empoisonnait pour toujours
/// l'horloge de sortie de sprint du serveur, et une origine de tir NaN passait le test de distance.
/// </summary>
public class InputSanityTests
{
    [Test]
    public void UnInputNormal_EstAccepte()
    {
        Assert.IsTrue(PlayerLocomotion.IsValidInput(new Vector2(0.7f, -0.7f), 12.5f, 1f / 144f));
        Assert.IsTrue(PlayerLocomotion.IsValidInput(Vector2.zero, 0f, 0f), "Un input sans durée reste recevable.");
    }

    [Test]
    public void UneValeurNaNOuInfinie_RendLInputIrrecevable()
    {
        Assert.IsFalse(PlayerLocomotion.IsValidInput(new Vector2(float.NaN, 0f), 0f, 0.016f), "Direction NaN.");
        Assert.IsFalse(PlayerLocomotion.IsValidInput(Vector2.up, float.PositiveInfinity, 0.016f), "Souris infinie.");
        Assert.IsFalse(PlayerLocomotion.IsValidInput(Vector2.up, 0f, float.NaN), "Durée NaN.");
        Assert.IsFalse(PlayerLocomotion.IsValidInput(Vector2.up, 0f, float.PositiveInfinity), "Durée infinie.");
        Assert.IsFalse(PlayerLocomotion.IsValidInput(Vector2.up, 0f, -0.016f), "Durée négative.");
    }

    [Test]
    public void UneOrigineOuUneDirectionInvalide_EstDetectee()
    {
        Assert.IsTrue(WeaponController.IsFinite(new Vector3(1f, 2f, 3f)));
        Assert.IsFalse(WeaponController.IsFinite(new Vector3(0f, float.NaN, 0f)));
        Assert.IsFalse(WeaponController.IsFinite(new Vector3(0f, 0f, float.NegativeInfinity)));
        Assert.IsFalse(WeaponController.IsFinite(double.NaN), "Instant affiché NaN.");
    }
}
