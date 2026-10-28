using DuelArena.UI;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La vue d'un robot touché par un caillou (2026-10-27) : la zone rouge autour du réticule pointe vers l'endroit
/// d'où le caillou est parti, les arcs grésillent au bord de l'écran du même côté, et le tout s'efface vite
/// (HitIndicatorHud, HitIndicatorOverlay ; les éclairs plats d'ElectricArc).
/// </summary>
public class HitIndicatorTests
{
    private static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);

    [Test]
    public void LaZone_PointeVersLeLanceur_SelonLeCapDuRegard()
    {
        Assert.AreEqual(0f, HitIndicatorHud.ScreenAngle(Vector3.forward, Eye, new Vector3(0f, 6f, 20f)), 0.01f,
            "Devant, même lancé de la passerelle au-dessus : en haut du cercle.");
        Assert.AreEqual(90f, HitIndicatorHud.ScreenAngle(Vector3.forward, Eye, new Vector3(10f, 1.6f, 0f)), 0.01f, "À droite.");
        Assert.AreEqual(-90f, HitIndicatorHud.ScreenAngle(Vector3.forward, Eye, new Vector3(-10f, 1.6f, 0f)), 0.01f, "À gauche.");
        Assert.AreEqual(180f, Mathf.Abs(HitIndicatorHud.ScreenAngle(Vector3.forward, Eye, new Vector3(0f, 1.6f, -10f))), 0.01f,
            "Derrière : en bas du cercle.");
        Assert.AreEqual(-90f, HitIndicatorHud.ScreenAngle(Vector3.right, Eye, new Vector3(0f, 1.6f, 20f)), 0.01f,
            "Le regard tourné à droite : ce qui était devant passe à gauche.");
        Vector3 lookingDown = new Vector3(0f, -0.9f, 0.44f).normalized;
        Assert.AreEqual(0f, HitIndicatorHud.ScreenAngle(lookingDown, Eye, new Vector3(0f, 6f, 20f)), 0.01f,
            "Le regard baissé ne change rien : seul le cap compte.");
    }

    [Test]
    public void LesArcs_PartentDuBordDeLEcran_DuCoteDuLanceur()
    {
        const float w = 1920f, h = 1080f;
        AssertNear(new Vector2(960f, 0f), HitIndicatorOverlay.BorderPoint(0f, w, h), "Devant : le bord du haut.");
        AssertNear(new Vector2(1920f, 540f), HitIndicatorOverlay.BorderPoint(90f, w, h), "À droite : le bord droit.");
        AssertNear(new Vector2(960f, 1080f), HitIndicatorOverlay.BorderPoint(180f, w, h), "Derrière : le bord du bas.");
        AssertNear(new Vector2(0f, 540f), HitIndicatorOverlay.BorderPoint(-90f, w, h), "À gauche : le bord gauche.");
        AssertNear(new Vector2(1500f, 0f), HitIndicatorOverlay.BorderPoint(45f, w, h),
            "De biais sur un écran large : le bord du haut, dans la direction du lanceur.");
        Vector2 back = HitIndicatorOverlay.BorderPoint(-150f, w, h);
        Assert.AreEqual(1080f, back.y, 0.01f, "Derrière à gauche : le bord du bas…");
        Assert.Less(back.x, 960f, "… à gauche du milieu.");
    }

    [Test]
    public void LaZone_ResteUnInstant_PuisSEfface_EtLesArcsSontUnEclat()
    {
        Assert.AreEqual(1f, HitIndicatorOverlay.RingAlpha(0f, 0.6f, 1.2f), "Pleine au coup.");
        Assert.AreEqual(1f, HitIndicatorOverlay.RingAlpha(0.6f, 0.6f, 1.2f), "Pleine le temps de la lire.");
        float fading = HitIndicatorOverlay.RingAlpha(1.2f, 0.6f, 1.2f);
        Assert.Greater(fading, 0f, "Puis elle s'efface…");
        Assert.Less(fading, 1f);
        Assert.AreEqual(0f, HitIndicatorOverlay.RingAlpha(1.8f, 0.6f, 1.2f), 1e-5f, "… et il n'en reste rien (à l'arrondi près)…");
        Assert.AreEqual(0f, HitIndicatorOverlay.RingAlpha(30f, 0.6f, 1.2f));

        Assert.AreEqual(1f, HitIndicatorOverlay.FlashAlpha(0f, 0.45f), "Les arcs éclatent au coup…");
        Assert.Less(HitIndicatorOverlay.FlashAlpha(0.2f, 0.45f), HitIndicatorOverlay.FlashAlpha(0.1f, 0.45f), "… retombent vite…");
        Assert.AreEqual(0f, HitIndicatorOverlay.FlashAlpha(0.45f, 0.45f), "… et s'éteignent…");
        Assert.AreEqual(0f, HitIndicatorOverlay.FlashAlpha(1f, 0.45f), "… pour de bon.");
    }

    [Test]
    public void UnEclairPlat_VaDuDepartALArrivee_SansJamaisEtreDroit()
    {
        var points = new Vector2[ElectricArc.PointCount(4)];
        var again = new Vector2[points.Length];
        for (uint s = 1; s <= 50; s++)
        {
            uint seed = s, seedAgain = s;
            ElectricArc.Build2D(Vector2.zero, new Vector2(100f, 0f), 4, 0.3f, Vector2.zero, ref seed, points);
            ElectricArc.Build2D(Vector2.zero, new Vector2(100f, 0f), 4, 0.3f, Vector2.zero, ref seedAgain, again);
            Assert.AreEqual(Vector2.zero, points[0], "Il part du départ.");
            Assert.AreEqual(new Vector2(100f, 0f), points[points.Length - 1], "Il arrive à l'arrivée.");
            // Le milieu s'écarte de la droite d'au moins 40 % de ce que permet sa brisure : jamais presque droit.
            Assert.GreaterOrEqual(Mathf.Abs(points[points.Length / 2].y), 100f * 0.3f * 0.4f - 1e-3f, $"graine {s}");
            CollectionAssert.AreEqual(points, again, "La même graine, le même éclair : il garde sa forme le temps d'une forme.");
        }
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, 0.01f, message);
        Assert.AreEqual(expected.y, actual.y, 0.01f, message);
    }
}
