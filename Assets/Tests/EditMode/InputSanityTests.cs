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
    public void LaDureeDUnInput_EstBornee()
    {
        Assert.AreEqual(1f / 144f, PlayerLocomotion.InputDuration(1f / 144f), "Une image ordinaire reste entière.");
        Assert.AreEqual(0.1f, PlayerLocomotion.InputDuration(0.15f), "Une image trop longue se simule à la borne.");
        Assert.AreEqual(0f, PlayerLocomotion.InputDuration(-1f));
    }

    [Test]
    public void DesInputsSansDuree_NePassentPasSansBorne()
    {
        // Un client modifié qui n'annonce aucune durée : sans coût plancher, le budget ne s'usait pas, et toute sa
        // file (512 inputs) se simulait à chaque image. La borne s'exprime par seconde de temps réel.
        const int Images = 60;
        float budget = 0f;
        int simules = 0;
        for (int i = 0; i < Images; i++)
        {
            budget = PlayerLocomotion.RefillInputBudget(budget, 1f / Images);
            while (simules < 100000 && PlayerLocomotion.TrySpendInputBudget(ref budget, 0f)) simules++;
        }
        Assert.Greater(simules, 0, "Garde anti-vacuité : le budget se recharge.");
        Assert.LessOrEqual(simules, 1150, "Le budget d'une seconde ne couvre pas plus de 1 100 inputs, à 1 ms chacun.");
    }

    [Test]
    public void UnJoueurHonnete_PasseEnEntier_MemePresDeMilleImagesParSeconde()
    {
        // Le serveur tourne à 60 images/s, le joueur à 960 : ses inputs de 1 ms ne perdent rien au coût plancher.
        const int Images = 60, InputsParImage = 16;
        float budget = 0f;
        int simules = 0;
        for (int i = 0; i < Images; i++)
        {
            budget = PlayerLocomotion.RefillInputBudget(budget, 1f / Images);
            for (int j = 0; j < InputsParImage; j++)
                if (PlayerLocomotion.TrySpendInputBudget(ref budget, 0.001f)) simules++;
        }
        Assert.AreEqual(Images * InputsParImage, simules);
    }

    [Test]
    public void LeLecteurDInputs_PasseAvantCeuxQuiLeLisent()
    {
        // PlayerLocomotion le lit dans son Update : sans ordre fixé, le déplacement et le lacet pouvaient partir avec
        // une image de retard, alors que le tangage, lu en LateUpdate, était à jour.
        var order = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(PlayerInputReader), typeof(DefaultExecutionOrder));
        Assert.IsNotNull(order, "L'ordre d'exécution du lecteur d'inputs n'est pas fixé.");
        Assert.Less(order.order, 0, "Il doit passer avant les scripts sans ordre.");
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
