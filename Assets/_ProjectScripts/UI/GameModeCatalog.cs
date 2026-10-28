using System.Collections.Generic;

/// <summary>Les modes de l'écran Jouer. Les valeurs gardent leur ordre : elles sont sérialisées
/// (PlayScreenController.modeArt), ajouter les nouvelles à la fin. Celles qui n'ont plus de carte restent.</summary>
public enum GameModeId
{
    Classic,
    /// <summary>N'a plus de carte (2026-10-27, demandé par l'utilisateur) : le roi de la colline est une option
    /// du classique, cochée au salon.</summary>
    KingOfTheHill,
    Tournament,
    /// <summary>N'a plus de carte (2026-10-27) : la tribune qui intervient est une option du classique.</summary>
    Spectators,
    WeaponGifts,
    /// <summary>N'a plus de carte (2026-10-27) : les cailloux sont des interventions de la tribune.</summary>
    Rocks,
    Darkness,
    /// <summary>N'a plus de carte (2026-10-25, GDD § 10) : c'est une surprise rare, pas un mode.</summary>
    Legendary,
    Ranked,
}

/// <summary>Ce qu'une carte de mode permet aujourd'hui.</summary>
public enum GameModeAvailability
{
    /// <summary>Le mode existe : sa carte crée un salon avec ses réglages.</summary>
    Playable,
    /// <summary>Ce qu'il décrit existe déjà dans le classique, avec la tribune qui intervient : sa carte crée
    /// un salon classique réglé ainsi.</summary>
    InClassic,
    /// <summary>Une piste du GDD, pas encore là.</summary>
    ComingSoon,
    /// <summary>Le classé, après la sortie (GDD § 3).</summary>
    AfterRelease,
}

/// <summary>Une carte de l'écran Jouer.</summary>
public readonly struct GameMode
{
    public readonly GameModeId id;
    public readonly string title;
    public readonly string subtitle;
    public readonly string description;
    public readonly GameModeAvailability availability;
    /// <summary>La classe USS de son icône (Theme.uss).</summary>
    public readonly string icon;

    public GameMode(GameModeId id, string title, string subtitle, string description, GameModeAvailability availability, string icon)
    {
        this.id = id;
        this.title = title;
        this.subtitle = subtitle;
        this.description = description;
        this.availability = availability;
        this.icon = icon;
    }

    /// <summary>Sa carte crée-t-elle un salon ?</summary>
    public bool CanCreate => availability == GameModeAvailability.Playable || availability == GameModeAvailability.InClassic;
}

/// <summary>
/// Les modes de l'écran Jouer (2026-10-17, les cartes de la maquette de l'utilisateur, tirées du GDD § 4, § 9,
/// § 10 et § 11). Un mode n'est qu'un point de départ pour les réglages de la partie (MatchRules) : au salon,
/// l'hôte peut encore changer les places, le roi de la colline, les interventions, les duellistes tirés au
/// sort et le nombre de manches. Fonctions pures, testées.
///
/// Depuis le 2026-10-27 (demandé par l'utilisateur), un seul mode se joue, le classique : le roi de la
/// colline et la tribune qui intervient (les cailloux, les armes lancées, la bagarre) en sont des options,
/// cochées au salon, et leurs cartes (Roi de la colline, Spectateurs, Cailloux) ont quitté l'écran. Les dons
/// d'armes ouvrent un classique où la tribune intervient. Les autres sont des pistes du GDD, montrées
/// verrouillées pour dire où le jeu va.
/// </summary>
public static class GameModeCatalog
{
    public static readonly IReadOnlyList<GameMode> Modes = new[]
    {
        new GameMode(GameModeId.Classic, "CLASSIQUE", "1V1 · BO5 OU BO3",
            "Un duel à mort, un joueur de chaque côté de l'arène. Le premier à trois manches gagnées l'emporte, ou à deux si l'hôte choisit le BO3. Au salon, l'hôte ouvre la tribune à d'autres joueurs, et choisit le roi de la colline, les interventions des spectateurs et les duellistes tirés au sort.",
            GameModeAvailability.Playable, "ico-swords"),
        new GameMode(GameModeId.WeaponGifts, "DONS D'ARMES", "1V1 · TRIBUNE ACTIVE",
            "Les spectateurs ramassent les armes tombées sur la tribune et les lancent au duelliste de leur choix. Pour l'instant, une partie classique où la tribune intervient.",
            GameModeAvailability.InClassic, "ico-gift"),
        new GameMode(GameModeId.Tournament, "TOURNOI", "ÉLIMINATION",
            "Un arbre à élimination entre les joueurs du salon, jusqu'au dernier debout.",
            GameModeAvailability.ComingSoon, "ico-trophy"),
        new GameMode(GameModeId.Darkness, "ARÈNE DANS LE NOIR", "1V1 · AU SON",
            "Les lumières s'éteignent : le son devient l'information principale.",
            GameModeAvailability.ComingSoon, "ico-moon"),
        new GameMode(GameModeId.Ranked, "CLASSÉ", "1V1 · DUEL PUR",
            "Le duel pur, sans spectateurs ni interventions, sur serveur dédié. Après la sortie.",
            GameModeAvailability.AfterRelease, "ico-medal"),
    };

    public static GameMode Get(GameModeId id)
    {
        foreach (GameMode mode in Modes)
        {
            if (mode.id == id) return mode;
        }
        return Modes[0];
    }

    /// <summary>Les réglages de départ du salon qu'une carte ouvre (ceux du classique pour une carte qui
    /// n'en ouvre pas). Tous en BO5 : l'hôte passe en BO3 au salon.</summary>
    public static MatchRules RulesFor(GameModeId id) => id switch
    {
        GameModeId.WeaponGifts => new MatchRules
        {
            capacity = MatchRules.MaxCapacity,
            organization = DuelOrganization.KingOfTheHill,
            spectatorInterventions = true,
            roundsToWin = MatchRules.Bo5RoundsToWin,
        },
        _ => new MatchRules
        {
            capacity = MatchRules.MinCapacity,
            organization = DuelOrganization.FixedDuelists,
            spectatorInterventions = false,
            roundsToWin = MatchRules.Bo5RoundsToWin,
        },
    };

    /// <summary>Le mode que des réglages jouent, quels qu'ils soient : le classique, avec ses options.</summary>
    public static GameModeId ModeOf(MatchRules rules) => GameModeId.Classic;

    /// <summary>Le nom du mode que des réglages jouent, pour le salon : « Classique », et ses options cochées
    /// (« Classique · roi de la colline · tribune active »).</summary>
    public static string Describe(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        string name = "Classique";
        if (r.organization == DuelOrganization.KingOfTheHill) name += " · roi de la colline";
        if (r.spectatorInterventions) name += " · tribune active";
        return name;
    }

    /// <summary>Le libellé de ce qu'une carte permet, sur sa vignette (vide pour un mode jouable).</summary>
    public static string Badge(GameModeAvailability availability) => availability switch
    {
        GameModeAvailability.InClassic => "TRIBUNE ACTIVE",
        GameModeAvailability.ComingSoon => "BIENTÔT",
        GameModeAvailability.AfterRelease => "APRÈS LA SORTIE",
        _ => "",
    };
}
