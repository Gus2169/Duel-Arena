using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les Options (2026-10-27, demandé par l'utilisateur) : la résolution ne propose que les tailles courantes,
/// une fois chacune, au format de l'écran (Screen.resolutions donne chaque taille une fois par fréquence, et des
/// tailles d'un autre format qui déformeraient l'image), et le réglage enregistré se retrouve dans la liste.
/// </summary>
public class OptionsTests
{
    private static Vector2Int R(int w, int h) => new Vector2Int(w, h);

    [Test]
    public void LesResolutions_SontLesTaillesCourantes_UneFoisChacune_AuFormatDeLEcran()
    {
        // Ce que donne un écran 1080p : chaque taille à 60, 120 et 144 Hz, et des tailles 4:3, 5:4 et 16:10.
        var supported = new List<Vector2Int>();
        foreach (Vector2Int size in new[] { R(640, 480), R(800, 600), R(1024, 768), R(1152, 864), R(1280, 720), R(1280, 800),
                                            R(1280, 1024), R(1366, 768), R(1440, 900), R(1600, 900), R(1680, 1050), R(1920, 1080) })
            for (int hz = 0; hz < 3; hz++) supported.Add(size);

        List<Vector2Int> choices = OptionsController.CommonResolutions(supported, R(1920, 1080));
        CollectionAssert.AreEqual(new[] { R(1280, 720), R(1366, 768), R(1600, 900), R(1920, 1080) }, choices,
            "Les tailles 16:9 courantes, une fois chacune, de la plus petite à la plus grande.");
    }

    [Test]
    public void LaTailleNative_EstToujoursProposee_EtRienDePlusGrand()
    {
        var supported = new[] { R(1280, 720), R(1920, 1080), R(2560, 1440), R(3840, 2160), R(2752, 1548) };
        List<Vector2Int> choices = OptionsController.CommonResolutions(supported, R(2752, 1548));
        CollectionAssert.Contains(choices, R(2752, 1548), "La taille native d'un écran rare, en dernier.");
        Assert.AreEqual(R(2752, 1548), choices[choices.Count - 1]);
        CollectionAssert.DoesNotContain(choices, R(3840, 2160), "Rien de plus grand que l'écran.");

        List<Vector2Int> wide = OptionsController.CommonResolutions(new[] { R(1920, 1080), R(2560, 1080), R(3440, 1440) }, R(3440, 1440));
        CollectionAssert.AreEqual(new[] { R(2560, 1080), R(3440, 1440) }, wide, "Un écran 21:9 garde ses tailles 21:9.");
    }

    [Test]
    public void LaResolutionEnregistree_SeRetrouve_SinonCelleDeLaFenetre()
    {
        var choices = new[] { R(1280, 720), R(1600, 900), R(1920, 1080) };
        Assert.AreEqual(1, OptionsController.ResolutionIndexOf(choices, R(1600, 900), R(1920, 1080)), "Celle qu'on a choisie.");
        Assert.AreEqual(0, OptionsController.ResolutionIndexOf(choices, Vector2Int.zero, R(1280, 720)), "Rien d'enregistré : celle de la fenêtre.");
        Assert.AreEqual(2, OptionsController.ResolutionIndexOf(choices, R(800, 600), R(1000, 700)), "Ni l'une ni l'autre : la plus grande.");
        Assert.AreEqual("1920 × 1080", OptionsController.ResolutionLabel(R(1920, 1080)));
    }
}
