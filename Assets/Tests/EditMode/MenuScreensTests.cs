using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le menu refait le 2026-10-17 d'après les maquettes de l'utilisateur : ce qui s'y décide sans écran. Les
/// modes de l'écran Jouer et les réglages qu'ils ouvrent (GameModeCatalog), ce qui se règle au salon et ce
/// qu'il dit des joueurs (SalonView), qui le salon met en tête d'affiche (LobbyLineup), le nom des couleurs des robots (PaletteNames), la carte de l'arène
/// (MenuArtwork.MapUV), la couleur que chaque joueur publie au salon (OnlineSession.ParseColor).
/// </summary>
public class MenuScreensTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // ------------------------------------------------------------------ Les modes de l'écran Jouer

    [Test]
    public void LesNeufModes_LesJouablesDAbord_PuisDansLOrdreDuGDD()
    {
        IReadOnlyList<GameMode> modes = GameModeCatalog.Modes;
        // La grille de 3 × 3 (maquette « A · Jouer », 2026-11-02) : ce qui se joue d'abord (le classique, le
        // Golden Gun), puis les autres modes de la sortie dans l'ordre du GDD (§ 11), puis les deux gardés pour après.
        var order = new[]
        {
            GameModeId.Classic, GameModeId.GoldenGun, GameModeId.GunGame, GameModeId.KillConfirmed, GameModeId.Zone,
            GameModeId.TagTeam, GameModeId.Tournament, GameModeId.Darkness, GameModeId.HackAndDefend,
        };
        Assert.AreEqual(order.Length, modes.Count, "Neuf cartes : la grille de 3 × 3.");
        string theme = File.ReadAllText("Assets/_ProjectUI/Shared/Theme.uss");
        for (int i = 0; i < modes.Count; i++)
        {
            Assert.AreEqual(order[i], modes[i].id, $"La carte {i + 1}.");
            if (i > 0) Assert.GreaterOrEqual((int)modes[i].availability, (int)modes[i - 1].availability,
                "Ce qui se joue, puis ce qui vient à la sortie, puis ce qui viendra après.");
            foreach (GameModeId option in new[] { GameModeId.KingOfTheHill, GameModeId.Spectators, GameModeId.Rocks, GameModeId.WeaponGifts })
                Assert.AreNotEqual(option, modes[i].id, $"{option} est une option du salon, pas une carte (GDD § 11).");
            Assert.AreNotEqual(GameModeId.Legendary, modes[i].id, "L'événement légendaire est une surprise rare, pas un mode (GDD § 10).");
            Assert.AreNotEqual(GameModeId.Ranked, modes[i].id, "Le classé vit dans l'onglet Classement.");
            foreach (string text in new[] { modes[i].name, modes[i].line, modes[i].description, modes[i].rhythm, modes[i].rhythmLong,
                                            modes[i].players, modes[i].playersLong, modes[i].goal })
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{modes[i].id} : tout est rempli.");
            Assert.AreEqual(modes[i].name.ToUpperInvariant(), modes[i].title);
            StringAssert.Contains("." + modes[i].icon + " ", theme, $"L'icône de {modes[i].id} doit exister dans Theme.uss.");
            Assert.AreEqual(modes[i].id, GameModeCatalog.Get(modes[i].id).id);
        }
    }

    [Test]
    public void LesModesJouables_OuvrentUnSalon_LesAutresDisentPourquoi()
    {
        var playables = new[]
        {
            GameModeId.Classic, GameModeId.GoldenGun, GameModeId.GunGame, GameModeId.KillConfirmed, GameModeId.Zone, GameModeId.TagTeam,
            GameModeId.Tournament,
        };
        foreach (GameModeId playable in playables)
        {
            Assert.IsTrue(GameModeCatalog.Get(playable).CanCreate, playable.ToString());
            Assert.AreEqual(playable, GameModeCatalog.ModeOf(GameModeCatalog.RulesFor(playable)), $"Le salon de {playable} joue ce mode.");
        }
        foreach (GameMode mode in GameModeCatalog.Modes)
        {
            if (System.Array.IndexOf(playables, mode.id) >= 0) continue;
            Assert.IsFalse(mode.CanCreate, $"{mode.id} n'existe pas encore : sa carte ne crée rien.");
            Assert.IsNotEmpty(GameModeCatalog.Badge(mode.availability), $"{mode.id} : elle dit pourquoi.");
        }
        Assert.IsEmpty(GameModeCatalog.Badge(GameModeAvailability.Playable), "Un mode jouable n'a pas de badge.");
        Assert.AreEqual("BIENTÔT", GameModeCatalog.Badge(GameModeAvailability.ComingSoon));
        Assert.AreEqual("APRÈS LA SORTIE", GameModeCatalog.Badge(GameModeCatalog.Get(GameModeId.HackAndDefend).availability));
    }

    [Test]
    public void LeClassique_SOuvreEnDuelSansTribune()
    {
        MatchRules classic = GameModeCatalog.RulesFor(GameModeId.Classic);
        Assert.AreEqual(2, classic.capacity, "Le classique : un contre un ; l'hôte ouvre des places au salon.");
        Assert.AreEqual(DuelOrganization.FixedDuelists, classic.organization);
        Assert.IsFalse(classic.spectatorInterventions);
        Assert.IsFalse(classic.randomDuelists, "L'hôte et le deuxième arrivé, sauf si l'hôte coche le tirage.");
        Assert.AreEqual(classic.capacity, classic.Sanitized().capacity, "Des réglages dans leurs bornes.");
    }

    [Test]
    public void LaPartieEnUneLigne_DitLeModeEtCeQuiEstRegle()
    {
        for (int capacity = MatchRules.MinCapacity; capacity <= MatchRules.MaxCapacity; capacity++)
        {
            var fixedDuel = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.FixedDuelists, roundsToWin = MatchRules.Bo5RoundsToWin };
            var rotation = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.KingOfTheHill, roundsToWin = MatchRules.Bo3RoundsToWin };
            var lively = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.KingOfTheHill, spectatorInterventions = true, randomDuelists = true };
            Assert.AreEqual("Classique · BO5 · toujours les mêmes · arme libre", GameModeCatalog.Summary(GameModeId.Classic, fixedDuel));
            Assert.AreEqual("Classique · BO3 · le gagnant reste · arme libre", GameModeCatalog.Summary(GameModeId.Classic, rotation));
            Assert.AreEqual("Classique · BO5 · le gagnant reste · duellistes tirés au sort · arme libre · cailloux et bagarre",
                            GameModeCatalog.Summary(GameModeId.Classic, lively), "Un réglage d'avant les manches vaut un BO5.");
            Assert.AreEqual(GameModeId.Classic, GameModeCatalog.ModeOf(lively), "Le roi de la colline et la tribune sont des options du classique.");
        }
        var golden = new MatchRules { capacity = 2, organization = DuelOrganization.KingOfTheHill, roundsToWin = 7, mode = GameModeId.GoldenGun };
        Assert.AreEqual("Golden Gun · premier à 7 · le gagnant reste", GameModeCatalog.Summary(GameModeId.GoldenGun, golden),
            "Le Golden Gun impose son arme : pas d'« arme libre ».");
    }

    // ------------------------------------------------------------------ Le salon : ce qui s'y règle (SalonView)

    private static SalonView.Row RowNamed(List<SalonView.Row> rows, string label)
    {
        foreach (SalonView.Row row in rows)
        {
            if (row.label == label) return row;
        }
        Assert.Fail($"Pas de rangée « {label} ».");
        return default;
    }

    [Test]
    public void LesReglagesEssentiels_SontLesManches_QuiAffronteQui_EtLArme()
    {
        var rules = new MatchRules { capacity = 4, organization = DuelOrganization.KingOfTheHill, roundsToWin = MatchRules.Bo5RoundsToWin };
        List<SalonView.Row> rows = SalonView.EssentialRows(rules);
        CollectionAssert.AreEqual(new[] { "Manches", "Qui affronte qui", "L'arme" }, rows.ConvertAll(r => r.label),
            "La maquette B : trois réglages d'emblée.");

        SalonView.Row rounds = RowNamed(rows, "Manches");
        Assert.AreEqual("BO3", rounds.choices[0].label);
        Assert.IsFalse(rounds.choices[0].selected);
        Assert.IsTrue(rounds.choices[1].selected, "Un BO5.");
        Assert.AreEqual(MatchRules.Bo3RoundsToWin, rounds.choices[0].apply(rules).roundsToWin, "Choisir BO3 fait un BO3.");

        SalonView.Row who = RowNamed(rows, "Qui affronte qui");
        Assert.IsTrue(who.choices[0].selected, "Le gagnant reste : le roi de la colline.");
        Assert.AreEqual(DuelOrganization.FixedDuelists, who.choices[1].apply(rules).organization);
        Assert.IsTrue(who.choices[2].locked, "Au hasard n'existe pas encore (GDD § 4).");
        Assert.IsFalse(who.choices[2].Enabled);

        SalonView.Row weapon = RowNamed(rows, "L'arme");
        Assert.IsTrue(weapon.choices[0].selected && weapon.choices[0].Enabled, "L'arme libre, la seule qui existe.");
        Assert.IsTrue(weapon.choices[1].Enabled && weapon.choices[2].Enabled, "Pile ou face et l'arsenal commun se choisissent au classique (2026-11-03).");
        Assert.AreEqual(WeaponChoice.CoinFlip, weapon.choices[1].apply(rules).weaponChoice);
        Assert.AreEqual(WeaponChoice.CommonArsenal, weapon.choices[2].apply(rules).weaponChoice);

        // Le Golden Gun : ses manches (le premier à 5 ou à 7), et son arme, qui ne se choisit pas.
        List<SalonView.Row> golden = SalonView.EssentialRows(GameModeCatalog.RulesFor(GameModeId.GoldenGun));
        SalonView.Row goldenRounds = RowNamed(golden, "Manches");
        CollectionAssert.AreEqual(new[] { "Premier à 5", "Premier à 7" }, System.Array.ConvertAll(goldenRounds.choices, c => c.label));
        Assert.IsTrue(goldenRounds.choices[0].selected);
        Assert.AreEqual(7, goldenRounds.choices[1].apply(GameModeCatalog.RulesFor(GameModeId.GoldenGun)).roundsToWin);
        SalonView.Row goldenWeapon = RowNamed(golden, "L'arme");
        Assert.IsTrue(goldenWeapon.isFixed, "L'arme du Golden Gun est celle du mode.");
        Assert.AreEqual(0, goldenWeapon.choices.Length);
        StringAssert.Contains("Golden Gun", goldenWeapon.hint);
    }

    [Test]
    public void LesPlaces_NeDescendentJamaisSousLesJoueursPresents()
    {
        var rules = new MatchRules { capacity = 5, organization = DuelOrganization.FixedDuelists, roundsToWin = MatchRules.Bo5RoundsToWin };
        List<SalonView.Row> rows = SalonView.AdvancedRows(rules, playerCount: 4);
        CollectionAssert.AreEqual(new[] { "Death match", "Tête seulement", "Places", "Duellistes tirés au sort", "La tribune" }, rows.ConvertAll(r => r.label),
            "Au classique, la case du Death match d'abord (GDD § 11, N8), puis Tête seulement (I3).");

        SalonView.Row places = RowNamed(rows, "Places");
        Assert.AreEqual(MatchRules.MaxCapacity - MatchRules.MinCapacity + 1, places.choices.Length, "De 2 à 6.");
        foreach (SalonView.Choice choice in places.choices)
        {
            int capacity = int.Parse(choice.label);
            Assert.AreEqual(capacity < 4, choice.unavailable, $"{capacity} places pour quatre joueurs.");
            Assert.IsFalse(choice.locked, "Hors de portée, pas verrouillé : pas de cadenas.");
            Assert.AreEqual(capacity == 5, choice.selected);
            Assert.AreEqual(capacity, choice.apply(rules).capacity);
        }

        SalonView.Row draw = RowNamed(rows, "Duellistes tirés au sort");
        Assert.IsTrue(draw.isCheck);
        Assert.IsFalse(draw.check);
        Assert.IsTrue(draw.setCheck(rules, true).randomDuelists, "Cocher la case tire les duellistes au sort.");

        SalonView.Row tribune = RowNamed(rows, "La tribune");
        Assert.IsTrue(tribune.choices[0].selected, "Rien, sans interventions.");
        Assert.IsTrue(tribune.choices[1].apply(rules).spectatorInterventions, "Les cailloux et la bagarre.");
        Assert.IsTrue(tribune.choices[2].locked, "Les armes insolites n'existent pas encore (GDD § 9).");
    }

    [Test]
    public void LeRoleDeChaqueJoueur_SeLitEnCapitales()
    {
        Assert.AreEqual("DUELLISTE · HÔTE", SalonView.Role(true, 0, false, true, false));
        Assert.AreEqual("DUELLISTE · TOI", SalonView.Role(true, 0, false, false, true));
        Assert.AreEqual("TRIBUNE · 1ER", SalonView.Role(false, 1, false, false, false));
        Assert.AreEqual("TRIBUNE · 3E", SalonView.Role(false, 3, false, false, false));
        Assert.AreEqual("TRIBUNE", SalonView.Role(false, 0, false, false, false));
        Assert.AreEqual("DANS LE TIRAGE · HÔTE", SalonView.Role(true, 0, true, true, false), "Au tirage, personne n'est duelliste d'avance.");
    }

    [Test]
    public void LaPhraseDuSalon_DitQuiCommenceEtCeQuiSuit()
    {
        Assert.AreEqual("", SalonView.Phrase(0, null, null, false, DuelOrganization.KingOfTheHill, true, "le code du salon"));
        Assert.AreEqual("En attente d'un adversaire : donne-lui le code du salon.",
                        SalonView.Phrase(1, "FAUCON", null, false, DuelOrganization.KingOfTheHill, true, "le code du salon"));
        Assert.AreEqual("En attente d'un adversaire.", SalonView.Phrase(1, "FAUCON", null, false, DuelOrganization.KingOfTheHill, true, null),
                        "Rien à donner (un client en réseau local) : rien à dire de plus.");
        Assert.AreEqual("FAUCON et RIVET commencent. Le gagnant reste ; le perdant passe au bout de la file.",
                        SalonView.Phrase(4, "FAUCON", "RIVET", false, DuelOrganization.KingOfTheHill, true, "le code du salon"));
        Assert.AreEqual("FAUCON et RIVET commencent. Les autres regardent depuis la tribune.",
                        SalonView.Phrase(3, "FAUCON", "RIVET", false, DuelOrganization.FixedDuelists, true, "le code du salon"));
        Assert.AreEqual("FAUCON et RIVET s'affrontent.",
                        SalonView.Phrase(2, "FAUCON", "RIVET", false, DuelOrganization.KingOfTheHill, false, "le code du salon"),
                        "Deux places : un duel sans tribune.");
        StringAssert.StartsWith("Les deux duellistes seront tirés au sort au lancement.",
                                SalonView.Phrase(3, null, null, true, DuelOrganization.KingOfTheHill, true, "le code du salon"));
    }

    [Test]
    public void LaFenetreDuMode_MontreLesModesDeLaSortie()
    {
        List<GameMode> modes = SalonView.WindowModes();
        Assert.AreEqual(7, modes.Count, "Les sept modes de la sortie (GDD § 11), pas ceux d'après.");
        Assert.AreEqual(GameModeId.Classic, modes[0].id);
        Assert.AreEqual(GameModeId.GoldenGun, modes[1].id);
        Assert.IsTrue(modes[1].CanCreate, "Le Golden Gun se choisit au salon.");
        Assert.AreEqual(GameModeId.GunGame, modes[2].id);
        Assert.IsTrue(modes[2].CanCreate, "Le Gun Game aussi.");
        Assert.AreEqual(GameModeId.KillConfirmed, modes[3].id);
        Assert.IsTrue(modes[3].CanCreate, "Et l'Élimination confirmée.");
        Assert.AreEqual(GameModeId.Zone, modes[4].id);
        Assert.IsTrue(modes[4].CanCreate, "Et la Zone.");
        Assert.AreEqual(GameModeId.TagTeam, modes[5].id);
        Assert.IsTrue(modes[5].CanCreate, "Et le Tag Team.");
        Assert.AreEqual(GameModeId.Tournament, modes[6].id);
        Assert.IsTrue(modes[6].CanCreate, "Et le Tournoi (2026-11-03).");
        foreach (GameMode mode in modes)
            Assert.AreNotEqual(GameModeAvailability.AfterRelease, mode.availability, mode.id.ToString());
    }

    [Test]
    public void LeTirageAuSort_VoyageDansLesReglagesDuSalon_EtSeRetient()
    {
        var drawn = new MatchRules { capacity = 4, organization = DuelOrganization.KingOfTheHill, roundsToWin = MatchRules.Bo5RoundsToWin, randomDuelists = true };
        Assert.IsTrue(OnlineSession.ParseRules(OnlineSession.FormatRules(drawn)).randomDuelists, "Le tirage voyage dans les réglages du salon.");
        Assert.IsFalse(OnlineSession.ParseRules(OnlineSession.FormatRules(MatchRules.Default)).randomDuelists);
        Assert.IsFalse(OnlineSession.ParseRules("4;0;1;3").randomDuelists, "Un salon d'avant le tirage (quatre champs) : pas de tirage.");
        StringAssert.Contains("tirés au sort", LobbyController.DescribeRules(drawn));
    }

    /// <summary>
    /// Le nombre de manches est un réglage du salon (GDD § 5, 2026-10-25) : un BO5 par défaut, ou un BO3.
    /// Il voyage dans les réglages publiés du salon, et un réglage d'avant ce choix (sans manches, ou d'une
    /// ancienne version du salon) vaut un BO5.
    /// </summary>
    [Test]
    public void LesManches_SontUnReglageDuSalon_BO5ParDefautOuBO3()
    {
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, MatchRules.Default.roundsToWin, "Un BO5 par défaut.");
        Assert.AreEqual(3, MatchRules.Bo5RoundsToWin);
        Assert.AreEqual(2, MatchRules.Bo3RoundsToWin);
        // Chaque mode s'ouvre à son nombre habituel de manches : un BO5 au classique, le premier à 5 au Golden Gun.
        foreach (GameMode mode in GameModeCatalog.Modes)
            Assert.AreEqual(MatchRules.RoundChoices(GameModeCatalog.ModeOf(GameModeCatalog.RulesFor(mode.id))).usual,
                            GameModeCatalog.RulesFor(mode.id).roundsToWin, $"{mode.id} s'ouvre à son nombre habituel de manches.");
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, GameModeCatalog.RulesFor(GameModeId.Classic).roundsToWin, "Le classique s'ouvre en BO5.");

        var bo3 = new MatchRules { capacity = 2, roundsToWin = MatchRules.Bo3RoundsToWin };
        Assert.AreEqual(MatchRules.Bo3RoundsToWin, bo3.Sanitized().roundsToWin, "Le BO3 reste un BO3.");
        Assert.AreEqual("BO3", bo3.Format);
        Assert.AreEqual("BO5", MatchRules.Default.Format);
        foreach (byte invalid in new byte[] { 0, 1, 4, 9 })
            Assert.AreEqual(MatchRules.Bo5RoundsToWin, new MatchRules { roundsToWin = invalid }.Sanitized().roundsToWin,
                $"{invalid} manches : ramené au BO5.");

        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(bo3));
        Assert.AreEqual(MatchRules.Bo3RoundsToWin, back.roundsToWin, "Le BO3 voyage dans les réglages du salon.");
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, OnlineSession.ParseRules("4;0;1").roundsToWin,
            "Un salon d'avant le réglage (trois champs) : un BO5.");
        StringAssert.Contains("BO3", LobbyController.DescribeRules(bo3));
    }

    // ------------------------------------------------------------------ Le salon : qui en tête d'affiche

    [Test]
    public void SeulLHote_EstEnTeteDAffiche_TantQuIlEstSeul()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte" }, 4);
        Assert.IsTrue(lineup.hasFirst);
        Assert.AreEqual("hôte", lineup.first);
        Assert.IsFalse(lineup.secondKnown, "Personne d'autre : le second est attendu.");
        Assert.IsFalse(lineup.drawn);
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(2, lineup.freeStands, "Quatre places : deux duellistes, deux places de tribune.");
    }

    [Test]
    public void ADeux_LesDeuxDuellistesSontConnus()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte", "ami" }, 6);
        Assert.AreEqual("hôte", lineup.first);
        Assert.IsTrue(lineup.secondKnown);
        Assert.AreEqual("ami", lineup.second);
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(4, lineup.freeStands);
    }

    [Test]
    public void ATroisEtPlus_LeDeuxiemeArrive_ResteLeSecondDuelliste()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte", "b", "c" }, 6);
        Assert.AreEqual("hôte", lineup.first, "L'hôte est le premier duelliste.");
        Assert.IsTrue(lineup.secondKnown, "Le deuxième arrivé au salon reste en tête d'affiche quand un troisième arrive (2026-10-27).");
        Assert.AreEqual("b", lineup.second);
        CollectionAssert.AreEqual(new[] { "c" }, lineup.stands, "Les suivants, sur la tribune, dans leur ordre d'arrivée.");
        Assert.AreEqual(3, lineup.freeStands, "Six places, trois joueurs : trois places libres.");

        LobbyLineup<string> full = LobbyLineup<string>.Arrange(new[] { "a", "b", "c", "d", "e", "f" }, 6);
        Assert.AreEqual("b", full.second);
        CollectionAssert.AreEqual(new[] { "c", "d", "e", "f" }, full.stands);
        Assert.AreEqual(0, full.freeStands, "Complet.");
    }

    [Test]
    public void AuTirage_PersonneNEstEnTeteDAffiche_TousSontDansLeTirage()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new[] { "hôte", "b", "c" }, 4, randomDuelists: true);
        Assert.IsTrue(lineup.drawn);
        Assert.IsFalse(lineup.hasFirst, "On ne sait pas d'avance qui commence.");
        Assert.IsFalse(lineup.secondKnown);
        CollectionAssert.AreEqual(new[] { "hôte", "b", "c" }, lineup.stands, "Tous, l'hôte compris, dans le tirage.");
        Assert.AreEqual(1, lineup.freeStands, "Quatre places, trois joueurs : une place libre.");
    }

    [Test]
    public void UnSalonVide_NeMontrePersonne()
    {
        LobbyLineup<string> lineup = LobbyLineup<string>.Arrange(new string[0], 4);
        Assert.IsFalse(lineup.hasFirst);
        Assert.IsFalse(lineup.secondKnown);
        Assert.AreEqual(0, lineup.stands.Count);
        Assert.AreEqual(2, lineup.freeStands);
        Assert.AreEqual(0, LobbyLineup<string>.Arrange(null, 2).freeStands, "Deux places : un duel sans tribune.");
    }

    // ------------------------------------------------------------------ Les couleurs des robots

    [Test]
    public void ChaqueCouleurDeLaPalette_ASonNom_LuSurSaTeinte()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        IReadOnlyList<Color32> palette = prefab.GetComponent<PlayerPaint>().Palette;
        Assert.Greater(palette.Count, 1);
        var names = new HashSet<string>();
        foreach (Color32 color in palette)
        {
            string name = PaletteNames.NameOf(color);
            Assert.IsTrue(names.Add(name), $"Deux couleurs de la palette s'appellent « {name} » : les tuiles de Personnalisation les confondraient.");
        }
        CollectionAssert.IsSubsetOf(new[] { "Orange", "Bleu", "Magenta", "Vert", "Cyan", "Violet", "Jaune" }, names,
            "La palette documentée (CLAUDE.md) : orange, bleu, magenta, vert, cyan, violet, jaune.");
    }

    [Test]
    public void LesNomsDesCouleurs_SuiventLaTeinte()
    {
        Assert.AreEqual("Rouge", PaletteNames.NameOf(new Color32(230, 30, 30, 255)));
        Assert.AreEqual("Orange", PaletteNames.NameOf(new Color32(255, 106, 19, 255)));
        Assert.AreEqual("Jaune", PaletteNames.NameOf(new Color32(255, 210, 30, 255)));
        Assert.AreEqual("Vert", PaletteNames.NameOf(new Color32(46, 224, 90, 255)));
        Assert.AreEqual("Cyan", PaletteNames.NameOf(new Color32(0, 229, 229, 255)));
        Assert.AreEqual("Bleu", PaletteNames.NameOf(new Color32(30, 144, 255, 255)));
        Assert.AreEqual("Violet", PaletteNames.NameOf(new Color32(143, 75, 255, 255)));
        Assert.AreEqual("Magenta", PaletteNames.NameOf(new Color32(255, 45, 155, 255)));
        Assert.AreEqual("Gris", PaletteNames.NameOf(new Color32(120, 122, 125, 255)), "Sans couleur : gris, blanc ou noir.");
        Assert.AreEqual("Blanc", PaletteNames.NameOf(new Color32(245, 245, 245, 255)));
        Assert.AreEqual("Noir", PaletteNames.NameOf(new Color32(10, 10, 12, 255)));
    }

    // ------------------------------------------------------------------ La carte de l'arène

    [Test]
    public void LaCarte_MetLePlusZDuMondeEnHaut_EtLePlusXADroite()
    {
        var world = new Rect(-40f, -10f, 80f, 100f);   // x de -40 à 40, z de -10 à 90
        AssertNear(new Vector2(0f, 1f), MenuArtwork.MapUV(world, new Vector2(-40f, -10f)), "Le coin x et z minimaux : en bas à gauche.");
        AssertNear(new Vector2(1f, 0f), MenuArtwork.MapUV(world, new Vector2(40f, 90f)), "Le coin x et z maximaux : en haut à droite.");
        AssertNear(new Vector2(0.5f, 0.5f), MenuArtwork.MapUV(world, new Vector2(0f, 40f)), "Le milieu.");
        AssertNear(new Vector2(0.5f, 0.88f), MenuArtwork.MapUV(world, new Vector2(0f, 2f)), "Le point d'apparition A, près du bord sud : en bas.");
        AssertNear(new Vector2(0.5f, 0.5f), MenuArtwork.MapUV(new Rect(0f, 0f, 0f, 10f), new Vector2(3f, 3f)), "Un rectangle vide ne divise pas par zéro.");
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, 1e-4f, message);
        Assert.AreEqual(expected.y, actual.y, 1e-4f, message);
    }

    // ------------------------------------------------------------------ La couleur publiée au salon

    [Test]
    public void LaCouleurPublieeAuSalon_SeRelitBornee()
    {
        Assert.IsTrue(OnlineSession.ParseColor("3", out int index));
        Assert.AreEqual(3, index);
        Assert.IsTrue(OnlineSession.ParseColor("-1", out index), "Sans choix.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor("", out index), "Un joueur d'une ancienne version n'en publie pas.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor("abc", out index));
        Assert.IsFalse(OnlineSession.ParseColor("9999", out index), "Un rang absurde ne sert à rien.");
        Assert.AreEqual(-1, index);
        Assert.IsFalse(OnlineSession.ParseColor(null, out index));
    }
}
