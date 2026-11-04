using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le lecteur d'inputs d'un robot qui n'est pas le sien : PlayerLocomotion l'éteint à son apparition. Une carte d'actions
/// allumée voit passer chaque évènement du clavier et de la souris, et chaque `new PlayerControls()` désérialise sa propre
/// copie de l'asset : à six joueurs, cinq de trop sur chaque machine.
/// </summary>
public class PlayerInputReaderTests
{
    private GameObject go;

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    private static object Controls(PlayerInputReader reader)
        => typeof(PlayerInputReader).GetField("controls", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reader);

    [UnityTest]
    public IEnumerator UnLecteurEteint_NaPasDeCarteDActions_MaisCeluiDuJoueurEnA()
    {
        go = new GameObject("Lecteur");
        go.SetActive(false);
        var reader = go.AddComponent<PlayerInputReader>();
        reader.enabled = false; // chez un autre joueur : éteint à l'apparition, avant son premier Update
        go.SetActive(true);
        yield return null;
        yield return null;

        Assert.IsNull(Controls(reader), "Un robot d'un autre joueur n'a pas à payer une carte d'actions.");
        Assert.IsFalse(reader.ReloadPressedThisFrame, "Il ne lit rien.");
        Assert.AreEqual(Vector2.zero, reader.MoveInput, "Il ne lit rien.");

        reader.enabled = true;
        yield return null;
        Assert.IsNotNull(Controls(reader), "Garde anti-vacuité : le joueur de cette machine a la sienne dès son premier Update.");
    }

    [Test]
    public void LesTouches_SeLisentAvantLePremierUpdate()
    {
        // Le HUD peut demander une touche à l'image même où le joueur apparaît.
        go = new GameObject("Lecteur");
        var reader = go.AddComponent<PlayerInputReader>();
        Assert.IsNull(Controls(reader), "Garde anti-vacuité : la carte d'actions n'existe pas encore.");
        Assert.AreNotEqual("Reload", reader.KeyFor("Reload"), "La touche réglée, pas le nom de l'action.");
    }
}
