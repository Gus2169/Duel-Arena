using System.Collections.Generic;
using DuelArena.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le HUD de la partie ne refait ce qu'il montre que quand cela change : ses textes s'écrivaient à chaque image, ceux d'un
/// spectateur avec ses touches, sur chaque machine qui regarde. Ces tests disent que les textes sont restés mot pour mot,
/// que ce qui doit les refaire les refait (un remappage, un pseudo ou une peinture qui arrivent après coup), et que
/// l'écran de fin et la caméra du joueur ne perdent rien en route.
/// </summary>
public class HudRefreshTests
{
    // ------------------------------------------------------------------
    // Le spectateur
    // ------------------------------------------------------------------

    [Test]
    public void LaPlaceDuSpectateur_SeDitMotPourMot()
    {
        Assert.AreEqual("SPECTATEUR · prochain duel : toi", SpectatorHud.RoleLine(PlayerTeam.None, 1, true));
        Assert.AreEqual("SPECTATEUR · 3e dans la file", SpectatorHud.RoleLine(PlayerTeam.None, 3, true));
        Assert.AreEqual("SPECTATEUR · en attente d'une place · interventions désactivées", SpectatorHud.RoleLine(PlayerTeam.None, 0, false));
        Assert.AreEqual("ÉQUIPE A · tu entres au prochain relais", SpectatorHud.RoleLine(PlayerTeam.A, 1, true));
        Assert.AreEqual("ÉQUIPE B · tu entres au 2e relais", SpectatorHud.RoleLine(PlayerTeam.B, 2, true));
    }

    [Test]
    public void CeQuIlAEnMain_EtSesCommandes_SeDisentMotPourMot()
    {
        Assert.AreEqual("Poings", SpectatorHud.CarryLine(ArenaItemKind.None, true, "rien"));
        Assert.AreEqual("Poings  ·  rangé : un caillou", SpectatorHud.CarryLine(ArenaItemKind.Rock, true, "un caillou"));
        Assert.AreEqual("En main : un caillou", SpectatorHud.CarryLine(ArenaItemKind.Rock, false, "un caillou"));

        const string fight = "F : coups de poing (gauche, droite)  ·  A : garde";
        Assert.AreEqual(fight + "  ·  I : ramasser  ·  frappe un bloc fissuré pour en arracher un caillou",
                        SpectatorHud.HintsLine(ArenaItemKind.None, true, true, false, "F", "A", "S", "I"));
        Assert.AreEqual(fight + "  ·  S : sortir le caillou", SpectatorHud.HintsLine(ArenaItemKind.Rock, true, true, false, "F", "A", "S", "I"));
        Assert.AreEqual("On lance pendant la manche  ·  S : les poings", SpectatorHud.HintsLine(ArenaItemKind.Rock, false, false, false, "F", "A", "S", "I"));
        Assert.AreEqual("F : lancer  ·  relâcher A : renoncer", SpectatorHud.HintsLine(ArenaItemKind.Rock, false, true, true, "F", "A", "S", "I"));
        Assert.AreEqual("Maintenir A : viser (la trajectoire s'affiche), puis F : lancer  ·  S : les poings",
                        SpectatorHud.HintsLine(ArenaItemKind.Rock, false, true, false, "F", "A", "S", "I"));

        Assert.AreEqual("I : ramasser le caillou", SpectatorHud.PromptLine("I", "le caillou", false));
        Assert.AreEqual("I : prendre le FP-12 (chargeur plein)", SpectatorHud.PromptLine("I", "le FP-12", true));
    }

    [Test]
    public void LesTextesDuSpectateur_SeRefontAuChangement_RemappageCompris()
    {
        var shown = new SpectatorHud.TextsKey
        {
            place = 2, team = PlayerTeam.None, interventions = true, controls = true, carried = ArenaItemKind.Rock,
            variant = 1, fistsOut = false, throwAllowed = true, aiming = false, keys = 3,
        };
        SpectatorHud.TextsKey same = shown;
        Assert.IsTrue(same.Equals(shown), "Rien n'a changé : rien à refaire.");

        void AssertChanges(System.Action<SpectatorHud.TextsKey[]> change, string what)
        {
            var key = new[] { shown };
            change(key);
            Assert.IsFalse(key[0].Equals(shown), what);
        }
        AssertChanges(k => k[0].keys = 4, "Une touche remappée en pleine partie se voit aussitôt.");
        AssertChanges(k => k[0].place = 1, "La place dans la file.");
        AssertChanges(k => k[0].team = PlayerTeam.A, "L'équipe.");
        AssertChanges(k => k[0].interventions = false, "Les interventions.");
        AssertChanges(k => k[0].controls = false, "Il n'a plus de quoi agir.");
        AssertChanges(k => k[0].carried = ArenaItemKind.None, "L'objet tenu.");
        AssertChanges(k => k[0].variant = 2, "L'arme tenue.");
        AssertChanges(k => k[0].fistsOut = true, "L'objet rangé.");
        AssertChanges(k => k[0].throwAllowed = false, "Le lancer permis.");
        AssertChanges(k => k[0].aiming = true, "Le lancer visé.");
    }

    // ------------------------------------------------------------------
    // La préparation
    // ------------------------------------------------------------------

    [Test]
    public void LaLigneDEtatDeLaPreparation_SeDitMotPourMot()
    {
        Assert.AreEqual("Rivet choisit son arme...", LoadoutPanel.StatusLine(false, false, 0, 0, "Rivet", false));
        Assert.AreEqual("Prêt.  Rivet est prêt.", LoadoutPanel.StatusLine(true, false, 0, 0, "Rivet", true));
        Assert.AreEqual("", LoadoutPanel.StatusLine(false, false, 0, 0, null, false), "Sans adversaire, rien à dire.");
        Assert.AreEqual("Prêt.  ", LoadoutPanel.StatusLine(true, false, 0, 0, null, false));
        Assert.AreEqual("1 sur 3 prêts.", LoadoutPanel.StatusLine(false, true, 3, 1, null, false));
    }

    [Test]
    public void LaLigneDeLaManche_SeDitMotPourMot()
    {
        const string both = "LE DÉCOMPTE PART QUAND LES DEUX JOUEURS ONT VALIDÉ";
        Assert.AreEqual($"MANCHE 2  ·  {both}", LoadoutPanel.RoundLine(false, PlayerTeam.None, false, false, 2));
        Assert.AreEqual($"TOURNOI  ·  MANCHE 1  ·  {both}", LoadoutPanel.RoundLine(false, PlayerTeam.None, true, false, 1));
        Assert.AreEqual($"FINALE  ·  MANCHE 3  ·  {both}", LoadoutPanel.RoundLine(false, PlayerTeam.None, true, true, 3));
        Assert.AreEqual("ÉQUIPE B  ·  LE DÉCOMPTE PART QUAND TOUT LE MONDE A VALIDÉ", LoadoutPanel.RoundLine(true, PlayerTeam.B, false, false, 1));
    }

    // ------------------------------------------------------------------
    // La barre de score et la ligne sous elle
    // ------------------------------------------------------------------

    [Test]
    public void LaBarreDeScore_SeRefaitAuChangement_PseudoEtPeintureCompris()
    {
        var side = new GameHud.ScoreSide { shown = true, id = 1, score = 2, name = "Faucon", paint = Color.blue };
        var shown = new GameHud.ScoreBarKey { target = 3, ladder = GunGameLadder.Full, left = side, right = side };
        GameHud.ScoreBarKey same = shown;
        Assert.IsTrue(same.Equals(shown), "Rien n'a changé : rien à refaire.");

        GameHud.ScoreBarKey Left(GameHud.ScoreSide changed)
        {
            GameHud.ScoreBarKey key = shown;
            key.left = changed;
            return key;
        }
        GameHud.ScoreSide other = side;
        other.name = "Joueur 2";
        Assert.IsFalse(Left(other).Equals(shown), "Le pseudo arrive après l'apparition du robot.");
        other = side;
        other.paint = Color.white;
        Assert.IsFalse(Left(other).Equals(shown), "La peinture aussi.");
        other = side;
        other.score = 3;
        Assert.IsFalse(Left(other).Equals(shown), "Une manche de plus.");
        other = side;
        other.id = 2;
        Assert.IsFalse(Left(other).Equals(shown), "Au Tag Team, un relais change le combattant.");
        other = side;
        other.shown = false;
        Assert.IsFalse(Left(other).Equals(shown), "Un côté qui disparaît.");

        GameHud.ScoreBarKey right = shown;
        right.right.score = 3;
        Assert.IsFalse(right.Equals(shown), "L'autre côté compte autant.");
        GameHud.ScoreBarKey spectating = shown;
        spectating.spectating = true;
        Assert.IsFalse(spectating.Equals(shown), "Vu de la tribune, les cercles sont neutres.");
        GameHud.ScoreBarKey target = shown;
        target.target = 5;
        Assert.IsFalse(target.Equals(shown), "Le nombre de marques.");
        GameHud.ScoreBarKey ladder = shown;
        ladder.ladder = GunGameLadder.Laser;
        Assert.IsFalse(ladder.Equals(shown), "L'échelle.");
        GameHud.ScoreBarKey tagTeam = shown;
        tagTeam.tagTeam = true;
        Assert.IsFalse(tagTeam.Equals(shown), "La barre du Tag Team n'est pas celle d'un duel.");
    }

    [Test]
    public void LaLigneSousLaBarre_NeSeConfondPasDUnModeALAutre_EtVoitUnPseudoTardif()
    {
        var zone = new GameHud.StatusKey { source = 1, a = 3, b = 7, name = "Rivet" };
        GameHud.StatusKey same = zone;
        Assert.IsTrue(same.Equals(zone));
        GameHud.StatusKey team = zone;
        team.source = 2;
        Assert.IsFalse(team.Equals(zone), "La Zone, le Tag Team et le Tournoi se partagent le même texte : passer de l'un à l'autre le refait.");
        GameHud.StatusKey seconds = zone;
        seconds.b = 6;
        Assert.IsFalse(seconds.Equals(zone), "La seconde change.");
        GameHud.StatusKey name = zone;
        name.name = "Joueur 2";
        Assert.IsFalse(name.Equals(zone), "Un pseudo qui arrive après coup.");
        GameHud.StatusKey next = zone;
        next.next = "Ombre";
        Assert.IsFalse(next.Equals(zone), "Le coéquipier qui entre ensuite.");
    }

    [Test]
    public void LEtatDeLaZone_SeDitMotPourMot()
    {
        Assert.AreEqual("ZONE DISPUTÉE · ELLE BOUGE DANS 7 S", GameHud.ZoneStatusLine(true, false, false, null, 7));
        Assert.AreEqual("ZONE LIBRE · ELLE BOUGE DANS 12 S", GameHud.ZoneStatusLine(false, true, false, null, 12));
        Assert.AreEqual("TU TIENS LA ZONE · ELLE BOUGE DANS 1 S", GameHud.ZoneStatusLine(false, false, true, null, 1));
        Assert.AreEqual("GUSTAVO TIENT LA ZONE · ELLE BOUGE DANS 30 S", GameHud.ZoneStatusLine(false, false, false, "Gustavo", 30));
    }

    [Test]
    public void UnRepereDeLaZone_GlisseParSonAncre_ALaMemePlaceDansLeHud()
    {
        VisualElement root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_ProjectUI/Game/GameHud.uxml").Instantiate();
        VisualElement marker = root.Q("zoneMarker");
        VisualElement parent = marker.parent;
        int place = parent.IndexOf(marker);

        VisualElement anchor = GameHud.MarkerAnchor(marker);

        Assert.AreSame(anchor, marker.parent, "Le repère garde son centrage : c'est son parent qui glisse.");
        Assert.AreEqual(place, parent.IndexOf(anchor), "L'ordre compte : chaque bloc se dessine par-dessus les précédents.");
        Assert.AreEqual(UsageHints.DynamicTransform, anchor.usageHints, "Un transform seul, pas une mise en page par image.");
        Assert.AreEqual(PickingMode.Ignore, anchor.pickingMode, "Il couvre l'écran : il ne doit rien attraper.");
        Assert.IsNotNull(root.Q<Label>("zoneMarkerLabel"), "Le HUD retrouve toujours ses éléments par nom.");
        Assert.AreSame(anchor, GameHud.MarkerAnchor(marker), "Réactivé sur le même arbre : pas une ancre de plus.");
        Assert.IsNull(GameHud.MarkerAnchor(null));
    }

    // ------------------------------------------------------------------
    // L'écran de fin
    // ------------------------------------------------------------------

    /// <summary>Le tirage tel qu'il se faisait à chaque image, rejoué depuis la graine : l'enchaînement retenu doit rester
    /// le même sur toutes les machines, y compris face à une version d'avant.</summary>
    private static VictoryStage.CelebrationFrame ReplayedFromSeed(int seed, float t, IReadOnlyList<float> lengths, float fade)
    {
        int count = lengths.Count;
        var random = new System.Random(seed);
        int current = random.Next(count);
        float start = 0f;
        for (int guard = 0; guard < 10000; guard++)
        {
            float length = Mathf.Max(lengths[current], fade * 2f + 0.1f);
            float end = start + length;
            float nextStart = end - fade;
            int next = (current + 1 + random.Next(count - 1)) % count;
            if (t < nextStart) return new VictoryStage.CelebrationFrame { clip = current, time = t - start, nextClip = next };
            if (t < end)
            {
                return new VictoryStage.CelebrationFrame
                {
                    clip = current,
                    time = t - start,
                    nextClip = next,
                    nextTime = t - nextStart,
                    nextWeight = Mathf.Clamp01((t - nextStart) / Mathf.Max(fade, 1e-3f)),
                };
            }
            current = next;
            start = nextStart;
        }
        return new VictoryStage.CelebrationFrame { clip = current };
    }

    [Test]
    public void LEnchainementRetenu_DonneLesImagesDuTirageRejoue_DansNImporteQuelOrdre()
    {
        float[] lengths = { 3.2f, 2.4f, 4.6f };
        const float fade = VictoryStage.CrossFade;
        foreach (int seed in new[] { 0, 7, -123456 })
        {
            var plan = new VictoryStage.CelebrationPlan(seed, lengths, fade);
            var clips = new HashSet<int>();
            for (float t = 0f; t < 40f; t += 0.0173f)
            {
                VictoryStage.CelebrationFrame expected = ReplayedFromSeed(seed, t, lengths, fade);
                VictoryStage.CelebrationFrame frame = plan.At(t);
                Assert.AreEqual(expected.clip, frame.clip, $"Graine {seed}, à {t} s.");
                Assert.AreEqual(expected.nextClip, frame.nextClip, $"Graine {seed}, à {t} s.");
                Assert.AreEqual(expected.time, frame.time, 0f, $"Graine {seed}, à {t} s : au bit près, sur toutes les machines.");
                Assert.AreEqual(expected.nextTime, frame.nextTime, 0f, $"Graine {seed}, à {t} s.");
                Assert.AreEqual(expected.nextWeight, frame.nextWeight, 0f, $"Graine {seed}, à {t} s.");
                clips.Add(frame.clip);
            }
            Assert.Greater(clips.Count, 1, "Garde : les célébrations se sont enchaînées.");

            // Un instant relu après d'autres, plus loin : retenir le tirage ne l'a pas fait avancer.
            for (float t = 39f; t >= 0f; t -= 1.3f)
                Assert.AreEqual(ReplayedFromSeed(seed, t, lengths, fade).clip, plan.At(t).clip, $"Graine {seed}, relu à {t} s.");
        }
    }

    [Test]
    public void LEnchainementRetenu_GardeLesCasSimples()
    {
        Assert.AreEqual(default(VictoryStage.CelebrationFrame), new VictoryStage.CelebrationPlan(1, System.Array.Empty<float>(), 0.35f).At(5f));
        VictoryStage.CelebrationFrame only = new VictoryStage.CelebrationPlan(1, new[] { 2f }, 0.35f).At(5f);
        Assert.AreEqual(0, only.clip);
        Assert.AreEqual(1f, only.time, 1e-5f, "Une seule célébration boucle.");
    }

    // ------------------------------------------------------------------
    // La lunette
    // ------------------------------------------------------------------

    [Test]
    public void LaLunette_NeSeRedessineQueSiElleChange()
    {
        var center = new Vector2(960f, 540f);
        Color color = Color.black;
        Assert.IsFalse(ScopeOverlay.NeedsRepaint(center, 0.92f, color, center, 0.92f, color), "Une image fixe : six anneaux à ne pas refaire.");
        Assert.IsFalse(ScopeOverlay.NeedsRepaint(center, 0.92f, color, center + new Vector2(0.004f, -0.004f), 0.92f, color),
                       "Le point visé tremble d'un rien d'une image à l'autre.");
        Assert.IsTrue(ScopeOverlay.NeedsRepaint(center, 0.92f, color, center + Vector2.right, 0.92f, color), "Le point visé bouge.");
        Assert.IsTrue(ScopeOverlay.NeedsRepaint(center, 0.92f, color, center, 0.8f, color), "L'ouverture change.");
        Assert.IsTrue(ScopeOverlay.NeedsRepaint(center, 0.92f, color, center, 0.92f, Color.red), "La croix change de couleur.");
        Assert.IsTrue(ScopeOverlay.NeedsRepaint(default, -1f, default, center, 0.92f, color), "Rien n'est encore dessiné.");
    }

    // ------------------------------------------------------------------
    // La caméra du joueur sous une autre
    // ------------------------------------------------------------------

    [Test]
    public void LaCameraDuJoueur_SEteintSousUneAutre_PuisRevient()
    {
        var go = new GameObject("Caméra du joueur");
        try
        {
            var player = go.AddComponent<Camera>();
            var cover = new KillCam.CoveredCamera();
            cover.Hold();
            cover.Release();
            Assert.IsTrue(player.enabled, "Sans caméra retenue, rien ne se passe.");

            cover.Follow(player);
            Assert.AreSame(player, cover.Player, "La kill cam part de sa pose et y revient.");
            Assert.IsTrue(player.enabled, "Elle ne s'éteint qu'une fois la caméra qui couvre allumée (Hold).");
            cover.Hold();
            Assert.IsFalse(player.enabled, "Sous la kill cam, l'arène ne se dessine qu'une fois.");
            Assert.IsTrue(go.activeSelf, "Son composant seul : son AudioListener reste l'oreille du joueur.");

            cover.Release();
            Assert.IsTrue(player.enabled, "Elle revient avec la fin de la kill cam.");
            Assert.IsNull(cover.Player);
            cover.Release();
            Assert.IsTrue(player.enabled, "La rendre deux fois ne change rien.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LaKillCamEtLePlateau_SePassentLaCameraDuJoueur_SansLaRallumerSousEux()
    {
        var go = new GameObject("Caméra du joueur");
        try
        {
            var player = go.AddComponent<Camera>();
            var killCam = new KillCam.CoveredCamera();
            var stage = new KillCam.CoveredCamera();
            killCam.Follow(player);
            stage.Follow(player);
            killCam.Hold();
            stage.Hold();
            Assert.IsFalse(player.enabled);

            // La kill cam se finit (Update) ; le plateau tient bon (LateUpdate), avant le rendu de cette image.
            killCam.Release();
            stage.Hold();
            Assert.IsFalse(player.enabled, "Le plateau couvre encore l'écran.");
            stage.Release();
            Assert.IsTrue(player.enabled);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LeRobotDisparu_NeGenePasLaFinDeLaKillCam()
    {
        var go = new GameObject("Caméra du joueur");
        var player = go.AddComponent<Camera>();
        var cover = new KillCam.CoveredCamera();
        cover.Follow(player);
        cover.Hold();
        Object.DestroyImmediate(go);

        Assert.DoesNotThrow(cover.Hold, "Le joueur a quitté la partie pendant la kill cam.");
        Assert.DoesNotThrow(cover.Release);
        Assert.IsTrue(cover.Player == null);
    }
}
