using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les arcs électriques du gant à décharge (2026-10-16, à la place des étincelles).
///
/// Ce qui casse ici ne lève aucune erreur : un éclair qui ne part pas de l'électrode ou n'arrive pas sur
/// la cible, une décharge qui part à l'appui au lieu du contact (l'instant où le serveur juge le coup),
/// des arcs dessinés deux fois chez le joueur (par ses bras et par son corps, qu'il ne voit pas), mal
/// placés dans la vue subjective (les deux caméras n'ont pas le même champ), ou une lueur qui prend la
/// couleur d'un robot.
/// </summary>
public class ElectricArcTests
{
    private const string GlovePrefabPath = "Assets/_ProjectArena/Weapon/GantChoc.prefab";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    [Test]
    public void UnEclair_VaDeSonDepartASonArrivee_EnZigzagSansSEgarer()
    {
        var start = new Vector3(0.1f, 1.4f, 0.2f);
        Vector3 end = start + new Vector3(0.3f, -0.2f, 0.9f);
        float length = Vector3.Distance(start, end);
        Vector3 axis = (end - start) / length;
        var points = new Vector3[ElectricArc.PointCount(4)];
        var again = new Vector3[points.Length];
        Assert.AreEqual(17, points.Length);

        float widest = 0f;
        for (uint graine = 1; graine <= 200; graine++)
        {
            uint s1 = graine, s2 = graine;
            ElectricArc.Build(start, end, 4, 0.28f, Vector3.zero, ref s1, points);
            ElectricArc.Build(start, end, 4, 0.28f, Vector3.zero, ref s2, again);
            Assert.AreEqual(start, points[0], "L'éclair ne part pas de l'électrode.");
            Assert.AreEqual(end, points[points.Length - 1], "L'éclair n'arrive pas là où il frappe.");
            CollectionAssert.AreEqual(points, again, "La même graine doit donner le même éclair : il suit le poing sans changer de forme.");
            foreach (Vector3 p in points)
            {
                Vector3 d = p - start;
                float off = (d - axis * Vector3.Dot(d, axis)).magnitude / length;
                widest = Mathf.Max(widest, off);
                // La somme des déplacements de quatre générations ne dépasse pas 0,59 fois la longueur.
                Assert.Less(off, 0.6f, $"Graine {graine} : l'éclair s'égare à {off * length * 100f:F0} cm de sa ligne.");
            }
        }
        Assert.Greater(widest, 0.08f, "Garde anti-vacuité : un éclair qui ne se brise pas est un trait.");
    }

    [Test]
    public void LesBras_MontrentLePointDuMonde_LaOuLeMontreLaCameraDuJoueur()
    {
        var eyeGo = new GameObject("Oeil");
        try
        {
            eyeGo.transform.SetPositionAndRotation(new Vector3(2f, 1.6f, -3f), Quaternion.Euler(8f, 35f, 4f));
            Camera player = eyeGo.AddComponent<Camera>();
            var armsGo = new GameObject("CameraBras");
            armsGo.transform.SetParent(eyeGo.transform, false);
            Camera arms = armsGo.AddComponent<Camera>();
            player.fieldOfView = 80f;
            arms.fieldOfView = 60f;   // FirstPersonView.armsFieldOfView
            player.aspect = arms.aspect = 16f / 9f;

            Vector3[] world =
            {
                eyeGo.transform.TransformPoint(new Vector3(0.4f, -0.3f, 1.5f)),
                eyeGo.transform.TransformPoint(new Vector3(-0.25f, 0.2f, 0.8f)),
            };
            foreach (Vector3 p in world)
            {
                Vector3 drawn = ElectricArc.WorldToArms(p, eyeGo.transform, player.fieldOfView, arms.fieldOfView);
                Vector3 seenByPlayer = player.WorldToViewportPoint(p);
                Vector3 seenByArms = arms.WorldToViewportPoint(drawn);
                Assert.Less(Vector2.Distance(seenByPlayer, seenByArms), 1e-4f, "Dans la vue subjective, l'éclair tomberait à côté de sa cible.");
                Assert.AreEqual(seenByPlayer.z, seenByArms.z, 1e-4f, "La profondeur est gardée.");
                Assert.Greater(Vector3.Distance(drawn, p), 0.02f, "Garde anti-vacuité : hors de l'axe, les deux champs diffèrent, le point doit bouger.");
            }
            Vector3 onAxis = eyeGo.transform.TransformPoint(new Vector3(0f, 0f, 1.2f));
            Assert.Less(Vector3.Distance(ElectricArc.WorldToArms(onAxis, eyeGo.transform, 80f, 60f), onAxis), 1e-5f, "Sur l'axe, le point ne bouge pas.");
        }
        finally
        {
            Object.DestroyImmediate(eyeGo);
        }
    }

    [Test]
    public void LaDecharge_PartAuContact_PasALAppui_PuisSEteint()
    {
        const float contact = 0.18f, duration = 0.24f;   // TaserController.contactDelay, TaserSparkFX.dischargeDuration
        Assert.AreEqual(TaserSparkFX.Phase.None, TaserSparkFX.PhaseAt(-0.01f, contact, duration));
        Assert.AreEqual(TaserSparkFX.Phase.None, TaserSparkFX.PhaseAt(float.NaN, contact, duration));
        Assert.AreEqual(TaserSparkFX.Phase.Charge, TaserSparkFX.PhaseAt(0f, contact, duration), "À l'appui, le gant se charge.");
        Assert.AreEqual(TaserSparkFX.Phase.Charge, TaserSparkFX.PhaseAt(contact - 0.001f, contact, duration),
                        "Rien ne part avant le contact : c'est à cet instant que le serveur juge le coup.");
        Assert.AreEqual(TaserSparkFX.Phase.Discharge, TaserSparkFX.PhaseAt(contact, contact, duration), "La décharge part au contact, avec son son.");
        Assert.AreEqual(TaserSparkFX.Phase.Discharge, TaserSparkFX.PhaseAt(contact + duration - 0.001f, contact, duration));
        Assert.AreEqual(TaserSparkFX.Phase.None, TaserSparkFX.PhaseAt(contact + duration, contact, duration));

        Assert.AreEqual(1f, TaserSparkFX.DischargeStrength(0f, duration), 1e-5f, "La décharge est pleine au contact.");
        float previous = 1f;
        for (float t = 0.02f; t < duration; t += 0.02f)
        {
            float strength = TaserSparkFX.DischargeStrength(t, duration);
            Assert.Less(strength, previous, "La décharge s'éteint sans se rallumer.");
            previous = strength;
        }
        Assert.AreEqual(0f, TaserSparkFX.DischargeStrength(duration, duration), 1e-5f, "Éteinte à la fin.");
    }

    [Test]
    public void ChezLeJoueur_SeulsSesBrasDessinentLesArcs()
    {
        Assert.IsTrue(TaserSparkFX.ShouldDraw(onViewCopy: true, playerHasView: true), "Le joueur voit les arcs de ses bras.");
        Assert.IsFalse(TaserSparkFX.ShouldDraw(onViewCopy: false, playerHasView: true),
                       "Son corps, qu'il ne voit pas, en dessinerait une seconde fois ailleurs à l'écran.");
        Assert.IsTrue(TaserSparkFX.ShouldDraw(onViewCopy: false, playerHasView: false), "Chez les autres, le corps dessine les arcs.");
    }

    [Test]
    public void LeGant_LanceSesArcsDeSesElectrodes_AuxCouleursDeLEnergie()
    {
        var glove = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GlovePrefabPath);
        Assert.IsNotNull(glove, "Préfab du gant introuvable : " + GlovePrefabPath);
        Assert.IsEmpty(glove.GetComponentsInChildren<ParticleSystem>(true),
                       "Plus de particules sur le gant : l'utilisateur veut de vrais arcs électriques (2026-10-16).");

        Transform fist = glove.transform.Find(GloveImporter.FistName);
        Assert.IsNotNull(fist, "Le gant n'a pas de repère « Poing ».");
        TaserSparkFX arcs = fist.GetComponent<TaserSparkFX>();
        Assert.IsNotNull(arcs, "Le poing ne porte pas les arcs (TaserSparkFX).");
        Assert.IsNotNull(arcs.ArcMaterial, "Les arcs n'ont pas de matériau : Unity les dessinerait en magenta.");
        Assert.AreEqual("DuelArena/ArcElectrique", arcs.ArcMaterial.shader.name);
        Assert.IsTrue(arcs.ArcMaterial.shader.isSupported, "Le shader des arcs ne tourne pas sous URP.");

        // Les arcs partent des vraies pointes des électrodes du modèle, voisines l'une de l'autre dans la
        // liste (un arc de la charge saute de l'une à la suivante).
        Vector3[] tips = GloveImporter.ElectrodeTips(glove.transform, fist);
        Assert.AreEqual(4, tips.Length, "Le GD-1 a quatre électrodes.");
        Assert.AreEqual(tips.Length, arcs.Electrodes.Count, "Les arcs ne connaissent pas toutes les électrodes : réimporter le gant (GloveImporter).");
        for (int i = 0; i < tips.Length; i++)
        {
            Assert.Less(Vector3.Distance(tips[i], arcs.Electrodes[i]), 0.001f,
                        $"L'électrode {i + 1} des arcs n'est plus au bout de celle du modèle : réimporter le gant (GloveImporter).");
            if (i > 0) Assert.Less(Vector3.Distance(arcs.Electrodes[i - 1], arcs.Electrodes[i]), 0.04f, "Deux électrodes qui se suivent sont voisines.");
        }

        // Et des arcs courent du haut des condensateurs, sur la manchette, là où le joueur voit son gant.
        Vector3[] tops = GloveImporter.CapacitorTops(glove.transform, fist);
        Assert.AreEqual(2, tops.Length, "Les condensateurs du GD-1 sont de part et d'autre de l'axe de la manchette.");
        Assert.AreEqual(tops.Length, arcs.Capacitors.Count, "Les arcs ne connaissent pas les condensateurs : réimporter le gant (GloveImporter).");
        for (int i = 0; i < tops.Length; i++)
        {
            Assert.Less(Vector3.Distance(tops[i], arcs.Capacitors[i]), 0.001f, $"Le condensateur {i + 1} des arcs n'est plus sur le modèle : réimporter le gant (GloveImporter).");
            Assert.Less(arcs.Capacitors[i].z, -0.2f, "Les condensateurs sont sur la manchette, derrière la main.");
            Assert.Greater(arcs.Capacitors[i].y, arcs.Electrodes[0].y + 0.05f, "Les condensateurs sont sur le dessus du gant.");
        }

        // Les couleurs de l'énergie (bible § 12) : un cœur blanc, un halo et une lumière rouges, plus
        // proches du rouge que de toute couleur de robot (la peinture identifie le joueur).
        var player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        IReadOnlyList<Color32> palette = player.GetComponent<PlayerPaint>().Palette;
        Assert.Greater(palette.Count, 0, "Garde anti-vacuité : la palette des robots est vide.");
        Color core = arcs.ArcMaterial.GetColor("_CouleurCoeur");
        Assert.Greater(Mathf.Min(core.r, Mathf.Min(core.g, core.b)), 0.9f, "Le cœur d'un arc est blanc.");
        AssertReadsAsRed(arcs.ArcMaterial.GetColor("_CouleurHalo"), palette, "Le halo des arcs");
        Assert.IsNotNull(arcs.Flash, "Le poing n'a plus sa lumière.");
        AssertReadsAsRed(arcs.Flash.color, palette, "La lumière du poing");
    }

    private static void AssertReadsAsRed(Color color, IReadOnlyList<Color32> palette, string what)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out _);
        Assert.Greater(saturation, 0.5f, $"{what} n'est plus rouge mais pâle (saturation {saturation:F2}).");
        float toRed = HueDistance(hue, 0f);
        foreach (Color32 robot in palette)
        {
            Color.RGBToHSV(robot, out float robotHue, out _, out _);
            Assert.Less(toRed, HueDistance(hue, robotHue),
                        $"{what} (#{ColorUtility.ToHtmlStringRGB(color)}) se lit comme la couleur d'un robot (#{ColorUtility.ToHtmlStringRGB(robot)}).");
        }
    }

    /// <summary>L'écart de deux teintes de [0 ; 1], en degrés sur le cercle.</summary>
    private static float HueDistance(float a, float b)
    {
        float d = Mathf.Abs(a - b) % 1f;
        return Mathf.Min(d, 1f - d) * 360f;
    }
}
