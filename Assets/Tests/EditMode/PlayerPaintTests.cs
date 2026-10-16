using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// La peinture des robots (2026-10-08) : une couleur vive par joueur, donnée par le serveur ; celle
/// que le joueur a choisie dans Personnalisation si personne ne la porte (2026-10-16).
///
/// Deux joueurs de la même couleur, un choix qui se perd en route, ou un robot peint d'une teinte
/// terne qui se fond dans le décor, ne lèvent aucune erreur : ils rendent juste le duel illisible pour
/// les spectateurs, ce que le GDD (§ 13) interdit. Ces tests gardent le choix du serveur et la palette
/// RÉELLE du prefab.
/// </summary>
public class PlayerPaintTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    /// <summary>Places d'un lobby du prototype (GDD § 16 : 2 duellistes + 2 spectateurs).</summary>
    private const int PrototypeLobbySize = 4;

    private static readonly Color32 Orange = new Color32(255, 106, 19, 255);
    private static readonly Color32 Bleu = new Color32(30, 144, 255, 255);
    private static readonly Color32 Vert = new Color32(46, 224, 90, 255);
    private static readonly Color32[] Palette = { Orange, Bleu, Vert };

    private static PlayerPaint PrefabPaint()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
        var paint = prefab.GetComponent<PlayerPaint>();
        Assert.IsNotNull(paint, "Le prefab joueur n'a pas de PlayerPaint.");
        return paint;
    }

    [Test]
    public void PremierJoueur_PrendLaPremiereCouleur()
    {
        Assert.AreEqual(Orange, PlayerPaint.PickPaint(Palette, new Color32[0]));
    }

    [Test]
    public void ChaqueNouveauJoueur_PrendUneCouleurQuePersonneNePorte()
    {
        var taken = new List<Color32>();
        for (int i = 0; i < Palette.Length; i++)
        {
            Color32 paint = PlayerPaint.PickPaint(Palette, taken);
            CollectionAssert.DoesNotContain(taken, paint, $"Le joueur {i + 1} a reçu une couleur déjà portée.");
            taken.Add(paint);
        }
    }

    [Test]
    public void UnJoueurQuiPart_LibereSaCouleur()
    {
        // Orange, bleu et vert étaient là ; le bleu est parti : le nouveau venu reprend le bleu,
        // pas une couleur portée ni la suivante de la liste.
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Orange, Vert }));
    }

    [Test]
    public void PlusDeJoueursQueDeCouleurs_LaMoinsPorteeEstDonnee()
    {
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Orange, Bleu, Vert, Orange }));
    }

    [Test]
    public void PaletteVide_PasDePeinture()
    {
        Assert.AreEqual(0, PlayerPaint.PickPaint(new Color32[0], null).a);
    }

    // ------------------------------------------------------------------ La couleur choisie (2026-10-16)

    [Test]
    public void LaCouleurChoisie_EstDonnee_SiPersonneNeLaPorte()
    {
        Assert.AreEqual(Vert, PlayerPaint.PickPaint(Palette, new Color32[0], 2), "Premier arrivé : il a la sienne, pas la première de la palette.");
        Assert.AreEqual(Vert, PlayerPaint.PickPaint(Palette, new[] { Orange }, 2), "Un autre porte l'orange : le vert reste libre.");
        Assert.AreEqual(Orange, PlayerPaint.PickPaint(Palette, new[] { Bleu }, 0));
    }

    [Test]
    public void LaCouleurChoisie_DejaPortee_LaPremiereLibreEstDonnee()
    {
        // GDD § 13 : chaque robot garde sa propre couleur. Deux joueurs veulent le vert : le second
        // arrivé prend la première couleur libre.
        Assert.AreEqual(Orange, PlayerPaint.PickPaint(Palette, new[] { Vert }, 2));
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Vert, Orange }, 2));
    }

    [Test]
    public void UnRangHorsDeLaPalette_NeVautRien()
    {
        // Aucun choix, ou un client modifié qui annonce un rang qui n'existe pas : la première libre.
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Orange }, -1));
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Orange }, Palette.Length));
        Assert.AreEqual(Bleu, PlayerPaint.PickPaint(Palette, new[] { Orange }, int.MinValue));
    }

    [Test]
    public void LaCouleurChoisie_VoyageDansLaDemandeDeConnexion()
    {
        byte[] payload = SessionFlow.BuildPayload("0.1.4", "Héloïse", WeaponLooks.Default, WeaponAttachments.None, 6);
        Assert.IsTrue(SessionFlow.ParsePayload(payload, out _, out string name, out _, out _, out int paint));
        Assert.AreEqual(6, paint);
        Assert.AreEqual("Héloïse", name, "Garde : le reste de la demande est lu comme avant.");

        // Une build d'avant le choix de la couleur n'envoie que quatre lignes : aucun choix.
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.1.4", "Untel", WeaponLooks.Default, WeaponAttachments.None),
                                               out _, out _, out _, out _, out paint));
        Assert.AreEqual(-1, paint);
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.4\nUntel\n00000000\n00000000\nbleu"),
                                               out _, out _, out _, out _, out paint));
        Assert.AreEqual(-1, paint, "Illisible : aucun choix.");
    }

    /// <summary>Deux instances du jeu sur un même PC partagent leurs PlayerPrefs : chacune garde la
    /// couleur choisie dans son propre menu, comme son pseudo.</summary>
    [Test]
    public void LaCouleur_ResteCelleDeCetteInstance_QuandUneAutreEnregistreLaSienne()
    {
        int saved = PlayerPrefs.GetInt(GameSettings.PreferredColorKey, -1);
        int mine = GameSettings.PreferredColorIndex;
        try
        {
            GameSettings.PreferredColorIndex = 4;
            Assert.AreEqual(4, PlayerPrefs.GetInt(GameSettings.PreferredColorKey), "Garde : la couleur est retenue pour le prochain lancement.");

            // L'autre instance choisit la sienne, dans la même clé du registre.
            PlayerPrefs.SetInt(GameSettings.PreferredColorKey, 1);
            Assert.AreEqual(4, GameSettings.PreferredColorIndex, "Cette instance annonce toujours la couleur de son menu.");
        }
        finally
        {
            GameSettings.PreferredColorIndex = mine;
            PlayerPrefs.SetInt(GameSettings.PreferredColorKey, saved);
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void LaPaletteDuPrefab_NeContientQueDesCouleursVives_EtDistinctes()
    {
        IReadOnlyList<Color32> palette = PrefabPaint().Palette;
        Assert.GreaterOrEqual(palette.Count, PrototypeLobbySize,
            "Moins de couleurs que de places dans un lobby : deux joueurs porteraient la même.");

        var hues = new List<float>();
        foreach (Color32 c in palette)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            string hex = ColorUtility.ToHtmlStringRGB(c);
            Assert.AreEqual(255, c.a, $"#{hex} n'est pas opaque (alpha nul = « pas de peinture »).");
            // « Couleurs vives » (GDD § 13) : saturée et lumineuse, pour trancher avec un décor sombre et terne.
            Assert.GreaterOrEqual(s, 0.6f, $"#{hex} est trop terne (saturation {s:F2}).");
            Assert.GreaterOrEqual(v, 0.7f, $"#{hex} est trop sombre (luminosité {v:F2}).");
            hues.Add(h * 360f);
        }

        for (int i = 0; i < hues.Count; i++)
        {
            for (int j = i + 1; j < hues.Count; j++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(hues[i], hues[j]));
                Assert.GreaterOrEqual(d, 20f,
                    $"#{ColorUtility.ToHtmlStringRGB(palette[i])} et #{ColorUtility.ToHtmlStringRGB(palette[j])} sont trop proches ({d:F0}° de teinte).");
            }
        }
    }

    [Test]
    public void LesDeuxPremieresCouleurs_SontOpposees()
    {
        // Ce sont celles des deux duellistes d'une partie à deux : les plus faciles à distinguer.
        IReadOnlyList<Color32> palette = PrefabPaint().Palette;
        Color.RGBToHSV(palette[0], out float h0, out _, out _);
        Color.RGBToHSV(palette[1], out float h1, out _, out _);
        float d = Mathf.Abs(Mathf.DeltaAngle(h0 * 360f, h1 * 360f));
        Assert.GreaterOrEqual(d, 120f, $"Les deux premières couleurs ne sont qu'à {d:F0}° de teinte l'une de l'autre.");
    }

    [Test]
    public void LePrefab_PeintLeCorpsDuModele_PasLesArticulations()
    {
        PlayerPaint paint = PrefabPaint();
        Assert.IsNotNull(paint.PaintedRenderers);
        Assert.Greater(paint.PaintedRenderers.Count, 0, "Aucune pièce peinte : le robot garderait sa couleur d'origine.");

        Transform model = paint.transform.Find("Model");
        Assert.IsNotNull(model, "Le prefab joueur n'a pas d'enfant « Model ».");
        foreach (Renderer r in paint.PaintedRenderers)
        {
            Assert.IsNotNull(r, "Une pièce peinte a perdu sa référence (modèle remplacé ?).");
            Assert.IsTrue(r.transform.IsChildOf(model), $"{r.name} n'appartient pas au modèle.");
        }

        // Les pièces non peintes restent visibles : au moins un renderer du modèle n'est pas peint.
        var painted = new HashSet<Renderer>(paint.PaintedRenderers);
        bool someUnpainted = false;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!painted.Contains(r)) someUnpainted = true;
        }
        Assert.IsTrue(someUnpainted, "Toutes les pièces sont peintes : les articulations ne se distingueraient plus.");
    }
}
