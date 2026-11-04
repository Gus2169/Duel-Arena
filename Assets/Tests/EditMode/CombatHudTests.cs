using DuelArena.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HitKind = WeaponController.HitKind;

/// <summary>
/// Le HUD de combat (2026-10-29, CrosshairUI en UI Toolkit) : le marqueur de touche (GDD § 6 : un hitmarker,
/// plus marqué à la tête, rouge au coup qui tue), les parasites et le très léger sursaut quand on est touché, le
/// réticule qui s'écarte jusqu'au bord du cône, et le document que le robot du joueur crée lui-même.
/// </summary>
public class CombatHudTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    [Test]
    public void LeMarqueurDeTouche_PlusMarqueALaTete_EtPlusLongAuCoupQuiTue()
    {
        Assert.Greater(HitMarker.Reach(HitKind.Head), HitMarker.Reach(HitKind.Body), "À la tête, la croix est plus grande.");
        Assert.Greater(HitMarker.Thickness(HitKind.Head), HitMarker.Thickness(HitKind.Body), "À la tête, elle est plus épaisse.");
        Assert.GreaterOrEqual(HitMarker.Reach(HitKind.Kill), HitMarker.Reach(HitKind.Head), "Le coup qui tue, au moins autant.");
        Assert.Greater(HitMarker.Duration(HitKind.Kill), HitMarker.Duration(HitKind.Head), "Le coup qui tue s'attarde.");
        Assert.GreaterOrEqual(HitMarker.Duration(HitKind.Head), HitMarker.Duration(HitKind.Body));

        Color kill = new CombatReticle().killColor;
        Assert.Greater(kill.r, 0.9f, "Le coup qui tue est rouge…");
        Assert.Less(kill.g, 0.5f, "… pas blanc.");
        Assert.Less(kill.b, 0.5f);

        foreach (HitKind kind in new[] { HitKind.Body, HitKind.Head, HitKind.Kill })
        {
            float duration = HitMarker.Duration(kind);
            Assert.AreEqual(1f, HitMarker.Alpha(kind, 0f), $"{kind} : pleine au coup.");
            Assert.AreEqual(1f, HitMarker.Alpha(kind, HitMarker.Hold(kind)), $"{kind} : pleine le temps de la voir.");
            float fading = HitMarker.Alpha(kind, (HitMarker.Hold(kind) + duration) * 0.5f);
            Assert.That(fading, Is.GreaterThan(0f).And.LessThan(1f), $"{kind} : elle s'efface…");
            Assert.AreEqual(0f, HitMarker.Alpha(kind, duration), $"{kind} : … et disparaît.");
            Assert.AreEqual(0f, HitMarker.Alpha(kind, -0.01f), $"{kind} : rien avant le coup.");
            Assert.AreEqual(0f, HitMarker.Alpha(kind, float.PositiveInfinity), $"{kind} : rien sans coup.");
            Assert.LessOrEqual(duration, 0.6f, $"{kind} : un marqueur, pas un message ({duration} s).");
        }
        Assert.Greater(HitMarker.Pop(0f), 1f, "Elle apparaît un peu plus grande…");
        Assert.AreEqual(1f, HitMarker.Pop(HitMarker.PopTime), 1e-5f, "… et se pose vite.");
    }

    [Test]
    public void LeCoupQuiTue_ResteALEcran_UneTouchePlusTardiveNeLeRemplacePas()
    {
        Assert.IsTrue(HitMarker.Replaces(HitKind.Body, float.PositiveInfinity, HitKind.Body), "Rien à l'écran : la touche s'affiche.");
        Assert.IsTrue(HitMarker.Replaces(HitKind.Body, 0.1f, HitKind.Head), "Une touche à la tête remplace une touche.");
        Assert.IsTrue(HitMarker.Replaces(HitKind.Head, 0.05f, HitKind.Body), "Chaque tir a sa croix : une touche après une tête.");
        Assert.IsTrue(HitMarker.Replaces(HitKind.Body, 0.05f, HitKind.Kill), "Le coup qui tue passe toujours.");
        Assert.IsFalse(HitMarker.Replaces(HitKind.Kill, 0.1f, HitKind.Body), "Le coup qui tue reste à l'écran…");
        Assert.IsFalse(HitMarker.Replaces(HitKind.Kill, 0.1f, HitKind.Head), "… même devant une tête…");
        Assert.IsTrue(HitMarker.Replaces(HitKind.Kill, HitMarker.Duration(HitKind.Kill), HitKind.Body), "… jusqu'au bout seulement.");
    }

    /// <summary>Le réticule ne se redessine que si ce qu'il montre change : immobile ou éteint en visée, il ne coûte rien à
    /// UI Toolkit. Mais une croix qui s'ouvre, une barre qui avance, un marqueur qui s'anime ou s'efface doivent se voir.</summary>
    [Test]
    public void LeReticule_NeSeRedessineQueSiCeQuIlMontreChange()
    {
        var rest = new CombatReticle.Look
        {
            center = new Vector2(960f, 540f), crosshair = true, gap = 6f, lineLength = 8f, thickness = 2f, centerDot = true, dotSize = 2f,
            barProgress = -1f, barOffset = 28f, barColor = Color.white, markerKind = -1, color = Color.white,
        };
        Assert.IsFalse(CombatReticle.NeedsRepaint(rest, rest), "Immobile, rien à redessiner.");

        CombatReticle.Look look = rest;
        look.center.x += 0.001f;
        Assert.IsFalse(CombatReticle.NeedsRepaint(rest, look), "La projection tremble d'un rien : ça ne se voit pas.");
        look.center.x += 0.5f;
        Assert.IsTrue(CombatReticle.NeedsRepaint(rest, look), "Le point visé a bougé.");

        look = rest;
        look.gap += 1f;
        Assert.IsTrue(CombatReticle.NeedsRepaint(rest, look), "La croix s'ouvre.");

        look = rest;
        look.crosshair = false;
        Assert.IsTrue(CombatReticle.NeedsRepaint(rest, look), "La croix s'efface en visée.");

        look = rest;
        look.barProgress = 0.4f;
        Assert.IsTrue(CombatReticle.NeedsRepaint(rest, look), "La barre apparaît.");
        CombatReticle.Look further = look;
        further.barProgress = 0.5f;
        Assert.IsTrue(CombatReticle.NeedsRepaint(look, further), "Elle avance.");
        further = look;
        further.barColor = Color.red;
        Assert.IsTrue(CombatReticle.NeedsRepaint(look, further), "La charge pleine change de couleur.");

        look = rest;
        look.markerKind = (int)HitKind.Head;
        look.markerAge = 0.1f;
        Assert.IsTrue(CombatReticle.NeedsRepaint(rest, look), "Le marqueur de touche apparaît.");
        Assert.IsTrue(CombatReticle.NeedsRepaint(look, look), "Il s'anime tant qu'il se voit, même au même âge lu deux fois.");
        Assert.IsTrue(CombatReticle.NeedsRepaint(look, rest), "Il s'efface : un dernier dessin.");
    }

    [Test]
    public void LesParasites_SuiventLaForceDuCoup_EtSEteignentVite()
    {
        Assert.AreEqual(0f, DamageStaticOverlay.Strength(0f, 40f, 0.4f), "Pas de coup, pas de parasites.");
        Assert.AreEqual(0f, DamageStaticOverlay.Strength(float.NaN, 40f, 0.4f), "NaN compris.");
        float pellet = DamageStaticOverlay.Strength(8f, 40f, 0.4f);
        float bullet = DamageStaticOverlay.Strength(17f, 40f, 0.4f);
        float revolver = DamageStaticOverlay.Strength(45f, 40f, 0.4f);
        Assert.That(pellet, Is.GreaterThanOrEqualTo(0.4f), "Le plus petit coup grésille quand même.");
        Assert.Less(pellet, bullet, "Un plomb grésille moins qu'une balle…");
        Assert.Less(bullet, revolver, "… qui grésille moins qu'une balle de revolver.");
        Assert.AreEqual(1f, revolver, "Pleine force au-delà de 40 PV…");
        Assert.AreEqual(1f, DamageStaticOverlay.Strength(500f, 40f, 0.4f), "… sans jamais la dépasser.");

        Assert.AreEqual(1f, DamageStaticOverlay.Intensity(0f, 0.45f), "Pleins au coup.");
        float early = DamageStaticOverlay.Intensity(0.1f, 0.45f), late = DamageStaticOverlay.Intensity(0.3f, 0.45f);
        Assert.That(early, Is.GreaterThan(late).And.LessThan(1f), "Ils s'éteignent…");
        Assert.Greater(late, 0f);
        Assert.AreEqual(0f, DamageStaticOverlay.Intensity(0.45f, 0.45f), "… éteints au bout de leur durée.");
        Assert.AreEqual(0f, DamageStaticOverlay.Intensity(-0.01f, 0.45f), "Rien avant le coup.");
        Assert.AreEqual(0f, DamageStaticOverlay.Intensity(float.PositiveInfinity, 0.45f), "Rien sans coup.");
        Assert.AreEqual(0f, DamageStaticOverlay.Intensity(float.NaN, 0.45f), "NaN compris.");
    }

    [Test]
    public void LeSursaut_MonteDUnRien_PuisRevientEnPlace()
    {
        const float amplitude = 0.8f;
        Assert.AreEqual(0f, PlayerCameraLook.FlinchPitch(0f, amplitude), 1e-5f, "La vue part de sa place…");
        Assert.AreEqual(amplitude, PlayerCameraLook.FlinchPitch(PlayerCameraLook.FlinchRise, amplitude), 1e-5f,
            "… monte de toute l'amplitude en un instant…");
        float previous = amplitude;
        for (float t = PlayerCameraLook.FlinchRise + 0.02f; t < PlayerCameraLook.FlinchDuration; t += 0.02f)
        {
            float pitch = PlayerCameraLook.FlinchPitch(t, amplitude);
            Assert.That(pitch, Is.LessThan(previous).And.GreaterThanOrEqualTo(0f), $"… puis redescend sans à-coup ({t:F2} s)…");
            previous = pitch;
        }
        Assert.AreEqual(0f, PlayerCameraLook.FlinchPitch(PlayerCameraLook.FlinchDuration, amplitude), "… et revient en place.");
        Assert.LessOrEqual(PlayerCameraLook.FlinchDuration, 0.4f, "Un sursaut, pas une secousse.");
        Assert.AreEqual(0f, PlayerCameraLook.FlinchPitch(-0.01f, amplitude), "Rien avant le coup.");
        Assert.AreEqual(0f, PlayerCameraLook.FlinchPitch(float.NaN, amplitude), "NaN compris.");

        Assert.AreEqual(0f, PlayerCameraLook.FlinchDegrees(0f, 0.025f, 1.2f), "Pas de coup, pas de sursaut.");
        Assert.AreEqual(0f, PlayerCameraLook.FlinchDegrees(float.NaN, 0.025f, 1.2f), "NaN compris.");
        Assert.Less(PlayerCameraLook.FlinchDegrees(17f, 0.025f, 1.2f), PlayerCameraLook.FlinchDegrees(45f, 0.025f, 1.2f),
            "Une balle de fusil fait moins sursauter qu'une balle de revolver.");
        Assert.AreEqual(1.2f, PlayerCameraLook.FlinchDegrees(100f, 0.025f, 1.2f), "Jamais plus que le plafond.");
    }

    [Test]
    public void LeSursautDuPrefab_ResteTresLeger()
    {
        // GDD § 6 : « un très léger sursaut de caméra. Rien d'exagéré ». Les valeurs du préfab priment sur le code.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var hud = new SerializedObject(prefab.GetComponent<CrosshairUI>());
        float perDamage = hud.FindProperty("flinchPerDamage").floatValue;
        float max = hud.FindProperty("flinchMax").floatValue;
        Assert.That(max, Is.GreaterThan(0f).And.LessThanOrEqualTo(1.5f), $"Le sursaut le plus fort fait {max}°.");
        float rifle = PlayerCameraLook.FlinchDegrees(17f, perDamage, max);
        Assert.That(rifle, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.6f), $"Une balle de fusil d'assaut fait sursauter de {rifle}°.");
    }

    [Test]
    public void LeReticule_SEcarteJusquAuBordDuCone()
    {
        Assert.AreEqual(Mathf.Tan(4f * Mathf.Deg2Rad) * 0.5f, CrosshairUI.ConeFraction(4f, 90f), 1e-5f,
            "Un cône de 4° sous un champ de 90° : la tangente, sur la moitié de la hauteur de l'écran.");
        Assert.Less(CrosshairUI.ConeFraction(1.5f, 90f), CrosshairUI.ConeFraction(3f, 90f), "Plus le cône s'ouvre, plus les branches s'écartent.");
        Assert.Greater(CrosshairUI.ConeFraction(3f, 60f), CrosshairUI.ConeFraction(3f, 90f), "Sous un champ plus étroit, le même cône paraît plus grand.");
        Assert.AreEqual(0f, CrosshairUI.ConeFraction(0f, 90f), "Une arme sans cône : rien.");
        Assert.AreEqual(0f, CrosshairUI.ConeFraction(float.NaN, 90f), "NaN compris.");
    }

    [Test]
    public void LeHudDeCombat_EstCableSurLePrefabDuJoueur_SousLesDocumentsDesScenes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var hud = new SerializedObject(prefab.GetComponent<CrosshairUI>());
        var tree = hud.FindProperty("hudTree").objectReferenceValue as VisualTreeAsset;
        var settings = hud.FindProperty("panelSettings").objectReferenceValue as PanelSettings;
        Assert.AreEqual("Assets/_ProjectUI/Game/CombatHud.uxml", AssetDatabase.GetAssetPath(tree), "Le HUD de combat n'est pas branché.");
        Assert.AreEqual("Assets/_ProjectUI/Game/GamePanelSettings.asset", AssetDatabase.GetAssetPath(settings),
            "Le HUD doit partager le PanelSettings des scènes de jeu pour se ranger sous leurs menus.");
        Assert.Less(hud.FindProperty("sortingOrder").floatValue, 0f, "Sous les documents des scènes (0) : les menus le couvrent.");
    }
}
