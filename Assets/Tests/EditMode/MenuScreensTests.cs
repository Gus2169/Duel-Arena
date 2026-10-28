using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Le menu refait le 2026-10-17 d'après les maquettes de l'utilisateur : ce qui s'y décide sans écran. Les
/// modes de l'écran Jouer et les réglages qu'ils ouvrent (GameModeCatalog), qui le salon met en tête
/// d'affiche (LobbyLineup), le nom des couleurs des robots (PaletteNames), la carte de l'arène
/// (MenuArtwork.MapUV), la couleur que chaque joueur publie au salon (OnlineSession.ParseColor).
/// </summary>
public class MenuScreensTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // ------------------------------------------------------------------ Les modes de l'écran Jouer

    [Test]
    public void LesCinqModes_SontDansLOrdre_EtToutEstRempli()
    {
        IReadOnlyList<GameMode> modes = GameModeCatalog.Modes;
        Assert.AreEqual(5, modes.Count,
            "Le classique, les dons d'armes et trois pistes : le roi de la colline, les spectateurs et les cailloux sont des options du classique (2026-10-27).");
        string theme = File.ReadAllText("Assets/_ProjectUI/Shared/Theme.uss");
        for (int i = 0; i < modes.Count; i++)
        {
            // Ce qui se joue d'abord, puis les pistes verrouillées (2026-10-27) ; chaque groupe dans l'ordre de l'énumération.
            if (i > 0 && modes[i].CanCreate == modes[i - 1].CanCreate)
                Assert.Greater((int)modes[i].id, (int)modes[i - 1].id, "Dans l'ordre de l'énumération.");
            if (i > 0) Assert.IsFalse(modes[i].CanCreate && !modes[i - 1].CanCreate, "Les modes jouables avant les modes verrouillés.");
            Assert.AreNotEqual(GameModeId.Legendary, modes[i].id,
                "L'événement légendaire est une surprise rare des modes arcade, pas un mode (GDD § 10, 2026-10-25).");
            foreach (GameModeId option in new[] { GameModeId.KingOfTheHill, GameModeId.Spectators, GameModeId.Rocks })
                Assert.AreNotEqual(option, modes[i].id, $"{option} est une option du classique, cochée au salon : pas une carte (2026-10-27).");
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].title), modes[i].id.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].subtitle), modes[i].id.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(modes[i].description), modes[i].id.ToString());
            StringAssert.Contains("." + modes[i].icon + " ", theme, $"L'icône de {modes[i].id} doit exister dans Theme.uss.");
            Assert.AreEqual(modes[i].id, GameModeCatalog.Get(modes[i].id).id);
        }
    }

    [Test]
    public void ChaqueModeJouable_OuvreUnSalonClassique()
    {
        foreach (GameModeId id in new[] { GameModeId.Classic, GameModeId.WeaponGifts })
        {
            Assert.IsTrue(GameModeCatalog.Get(id).CanCreate, id.ToString());
            Assert.AreEqual(GameModeId.Classic, GameModeCatalog.ModeOf(GameModeCatalog.RulesFor(id)), $"Le salon de {id} joue le classique.");
        }
        foreach (GameModeId id in new[] { GameModeId.Tournament, GameModeId.Darkness, GameModeId.Ranked })
        {
            Assert.IsFalse(GameModeCatalog.Get(id).CanCreate, $"{id} n'existe pas encore : sa carte ne crée rien.");
            Assert.IsNotEmpty(GameModeCatalog.Badge(GameModeCatalog.Get(id).availability), "Elle dit pourquoi.");
        }
        Assert.IsEmpty(GameModeCatalog.Badge(GameModeAvailability.Playable), "Un mode jouable n'a pas de badge.");
    }

    [Test]
    public void LesReglagesDesModes_SontCeuxQuOnAnnonce()
    {
        MatchRules classic = GameModeCatalog.RulesFor(GameModeId.Classic);
        Assert.AreEqual(2, classic.capacity, "Le classique : un contre un, personne d'autre.");
        Assert.AreEqual(DuelOrganization.FixedDuelists, classic.organization);
        Assert.IsFalse(classic.spectatorInterventions);
        Assert.IsFalse(classic.randomDuelists, "L'hôte et le deuxième arrivé, sauf si l'hôte coche le tirage.");

        MatchRules gifts = GameModeCatalog.RulesFor(GameModeId.WeaponGifts);
        Assert.AreEqual(MatchRules.MaxCapacity, gifts.capacity, "Des spectateurs pour lancer les armes.");
        Assert.IsTrue(gifts.spectatorInterventions);

        foreach (GameMode mode in GameModeCatalog.Modes)
        {
            MatchRules rules = GameModeCatalog.RulesFor(mode.id);
            Assert.AreEqual(rules.capacity, rules.Sanitized().capacity, $"{mode.id} : des réglages dans leurs bornes.");
        }
    }

    [Test]
    public void LeSalon_NommeLeClassiqueEtSesOptions_QuelsQueSoientSesReglages()
    {
        for (int capacity = MatchRules.MinCapacity; capacity <= MatchRules.MaxCapacity; capacity++)
        {
            var fixedDuel = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.FixedDuelists, spectatorInterventions = false };
            var rotation = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.KingOfTheHill, spectatorInterventions = false };
            var lively = new MatchRules { capacity = (byte)capacity, organization = DuelOrganization.KingOfTheHill, spectatorInterventions = true };
            Assert.AreEqual("Classique", GameModeCatalog.Describe(fixedDuel));
            Assert.AreEqual("Classique · roi de la colline", GameModeCatalog.Describe(rotation));
            Assert.AreEqual("Classique · roi de la colline · tribune active", GameModeCatalog.Describe(lively));
            Assert.AreEqual(GameModeId.Classic, GameModeCatalog.ModeOf(lively), "Le roi de la colline et la tribune sont des options du classique.");
        }
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
        foreach (GameMode mode in GameModeCatalog.Modes)
            Assert.AreEqual(MatchRules.Bo5RoundsToWin, GameModeCatalog.RulesFor(mode.id).roundsToWin, $"{mode.id} s'ouvre en BO5.");

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
