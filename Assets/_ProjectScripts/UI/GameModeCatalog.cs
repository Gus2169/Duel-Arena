using System.Collections.Generic;

/// <summary>Les modes de l'écran Jouer. Les valeurs gardent leur ordre (l'ordre des cartes) : elles sont
/// sérialisées (PlayScreenController.modeArt), ajouter les nouvelles à la fin.</summary>
public enum GameModeId
{
    Classic,
    KingOfTheHill,
    Tournament,
    Spectators,
    WeaponGifts,
    Rocks,
    Darkness,
    /// <summary>N'a plus de carte (2026-10-25, GDD § 10) : c'est une surprise rare, pas un mode. La
    /// valeur reste, pour l'ordre sérialisé.</summary>
    Legendary,
    Ranked,
}

/// <summary>Ce qu'une carte de mode permet aujourd'hui.</summary>
public enum GameModeAvailability
{
    /// <summary>Le mode existe : sa carte crée un salon avec ses réglages.</summary>
    Playable,
    /// <summary>Ce qu'il décrit existe déjà, dans le mode Spectateurs : sa carte crée un salon Spectateurs.</summary>
    InSpectators,
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
    public bool CanCreate => availability == GameModeAvailability.Playable || availability == GameModeAvailability.InSpectators;
}

/// <summary>
/// Les modes de l'écran Jouer (2026-10-17, les neuf cartes de la maquette de l'utilisateur, tirées du GDD § 4,
/// § 9, § 10 et § 11 ; huit depuis le 2026-10-25, l'événement légendaire n'étant pas un mode). Un mode n'est
/// qu'un point de départ pour les réglages de la partie (MatchRules) : au salon, l'hôte peut encore changer
/// les places, l'organisation des duels, les interventions et le nombre de manches, et le salon nomme le
/// mode d'après ces réglages (Describe). Fonctions pures, testées.
///
/// Trois modes existent : le classique (deux joueurs, duellistes fixes, aucune intervention), le roi de la
/// colline (la rotation du GDD § 4) et les spectateurs (la tribune intervient, GDD § 9). Les dons d'armes et
/// les cailloux sont des interventions des spectateurs : leurs cartes ouvrent un salon Spectateurs. Les
/// autres sont des pistes du GDD, montrées verrouillées pour dire où le jeu va.
/// </summary>
public static class GameModeCatalog
{
    public static readonly IReadOnlyList<GameMode> Modes = new[]
    {
        new GameMode(GameModeId.Classic, "CLASSIQUE", "1V1 · BO5 OU BO3",
            "Un duel à mort, un joueur de chaque côté de l'arène. Le premier à trois manches gagnées l'emporte, ou à deux si l'hôte choisit le BO3.",
            GameModeAvailability.Playable, "ico-swords"),
        new GameMode(GameModeId.KingOfTheHill, "ROI DE LA COLLINE", "1V1 · ROTATION",
            "Le gagnant reste, le perdant retourne au bout de la file, le suivant prend sa place. Ceux qui attendent regardent depuis la tribune.",
            GameModeAvailability.Playable, "ico-crown"),
        new GameMode(GameModeId.Tournament, "TOURNOI", "ÉLIMINATION",
            "Un arbre à élimination entre les joueurs du salon, jusqu'au dernier debout.",
            GameModeAvailability.ComingSoon, "ico-trophy"),
        new GameMode(GameModeId.Spectators, "SPECTATEURS", "1V1 · TRIBUNE ACTIVE",
            "Jusqu'à six joueurs : deux duellistes, et une tribune qui s'en mêle. Cailloux, armes lancées, bagarre : tout se fait à la main, et peut rater.",
            GameModeAvailability.Playable, "ico-users"),
        new GameMode(GameModeId.WeaponGifts, "DONS D'ARMES", "TRIBUNE ACTIVE",
            "Les spectateurs ramassent les armes tombées sur la tribune et les lancent au duelliste de leur choix. Pour l'instant, dans le mode Spectateurs.",
            GameModeAvailability.InSpectators, "ico-gift"),
        new GameMode(GameModeId.Rocks, "CAILLOUX", "TRIBUNE ACTIVE",
            "Casser un bloc de la tribune, lancer le caillou : il déstabilise un duelliste, et son bruit peut le tromper. Pour l'instant, dans le mode Spectateurs.",
            GameModeAvailability.InSpectators, "ico-rock"),
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
        GameModeId.KingOfTheHill => new MatchRules
        {
            capacity = 4,
            organization = DuelOrganization.KingOfTheHill,
            spectatorInterventions = false,
            roundsToWin = MatchRules.Bo5RoundsToWin,
        },
        GameModeId.Spectators or GameModeId.WeaponGifts or GameModeId.Rocks => new MatchRules
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

    /// <summary>Le mode que des réglages jouent, quels qu'ils soient : une tribune qui intervient, c'est le
    /// mode Spectateurs ; sinon la rotation fait le roi de la colline, et les duellistes fixes le classique.</summary>
    public static GameModeId ModeOf(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        if (r.spectatorInterventions) return GameModeId.Spectators;
        return r.organization == DuelOrganization.KingOfTheHill ? GameModeId.KingOfTheHill : GameModeId.Classic;
    }

    /// <summary>Le nom du mode que des réglages jouent, pour le salon (« Roi de la colline »).</summary>
    public static string Describe(MatchRules rules) => ModeOf(rules) switch
    {
        GameModeId.Spectators => "Spectateurs",
        GameModeId.KingOfTheHill => "Roi de la colline",
        _ => "Classique",
    };

    /// <summary>Le libellé de ce qu'une carte permet, sur sa vignette (vide pour un mode jouable).</summary>
    public static string Badge(GameModeAvailability availability) => availability switch
    {
        GameModeAvailability.InSpectators => "DANS SPECTATEURS",
        GameModeAvailability.ComingSoon => "BIENTÔT",
        GameModeAvailability.AfterRelease => "APRÈS LA SORTIE",
        _ => "",
    };
}
