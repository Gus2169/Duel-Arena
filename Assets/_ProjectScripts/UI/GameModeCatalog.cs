using System.Collections.Generic;

/// <summary>Les modes de l'écran Jouer. Les valeurs gardent leur ordre : elles se sérialisaient
/// (l'ancien PlayScreenController.modeArt), ajouter les nouvelles à la fin. Celles qui n'ont plus de carte restent.</summary>
public enum GameModeId
{
    Classic,
    /// <summary>N'a plus de carte (2026-10-27, demandé par l'utilisateur) : le roi de la colline est une
    /// organisation du classique, choisie au salon.</summary>
    KingOfTheHill,
    Tournament,
    /// <summary>N'a plus de carte (2026-10-27) : la tribune qui intervient est une option, réglée au salon.</summary>
    Spectators,
    /// <summary>N'a plus de carte (2026-11-02, GDD § 11) : les dons d'armes sont une option, ce que fait la
    /// tribune, réglée au salon.</summary>
    WeaponGifts,
    /// <summary>N'a plus de carte (2026-10-27) : les cailloux sont des interventions de la tribune.</summary>
    Rocks,
    Darkness,
    /// <summary>N'a plus de carte (2026-10-25, GDD § 10) : c'est une surprise rare, pas un mode.</summary>
    Legendary,
    /// <summary>N'a plus de carte (2026-11-02) : le classé vit dans l'onglet Classement, après la sortie.</summary>
    Ranked,
    // Les modes de la sortie et d'après (page « Modes de jeu », 2026-11-02 ; GDD § 11).
    GunGame,
    GoldenGun,
    KillConfirmed,
    Zone,
    TagTeam,
    HackAndDefend,
}

/// <summary>Ce qu'une carte de mode permet aujourd'hui.</summary>
public enum GameModeAvailability
{
    /// <summary>Le mode existe : sa carte crée un salon avec ses réglages.</summary>
    Playable,
    /// <summary>Un mode de la sortie, pas encore là.</summary>
    ComingSoon,
    /// <summary>Gardé pour après la sortie (GDD § 11).</summary>
    AfterRelease,
}

/// <summary>Une carte de l'écran Jouer, et la fiche du mode choisi.</summary>
public readonly struct GameMode
{
    public readonly GameModeId id;
    /// <summary>Son nom, comme au salon : « Classique », « Gun Game ».</summary>
    public readonly string name;
    /// <summary>Une ligne, sur la carte.</summary>
    public readonly string line;
    /// <summary>Quelques phrases, sur la fiche.</summary>
    public readonly string description;
    /// <summary>Le rythme, en court sur la carte (« EN MANCHES ») et en clair sur la fiche.</summary>
    public readonly string rhythm, rhythmLong;
    /// <summary>Les joueurs, en court sur la carte (« 2 À 6 ») et en clair sur la fiche.</summary>
    public readonly string players, playersLong;
    /// <summary>Ce qu'il faut pour gagner, sur la fiche.</summary>
    public readonly string goal;
    public readonly GameModeAvailability availability;
    /// <summary>La classe USS de son icône (Theme.uss).</summary>
    public readonly string icon;

    public GameMode(GameModeId id, string name, string line, string description, string rhythm, string rhythmLong,
                    string players, string playersLong, string goal, GameModeAvailability availability, string icon)
    {
        this.id = id;
        this.name = name;
        this.line = line;
        this.description = description;
        this.rhythm = rhythm;
        this.rhythmLong = rhythmLong;
        this.players = players;
        this.playersLong = playersLong;
        this.goal = goal;
        this.availability = availability;
        this.icon = icon;
    }

    /// <summary>Son nom en capitales, sur la carte et la fiche.</summary>
    public string title => name.ToUpperInvariant();

    /// <summary>Sa carte crée-t-elle un salon ?</summary>
    public bool CanCreate => availability == GameModeAvailability.Playable;
}

/// <summary>
/// Les modes de l'écran Jouer (refait le 2026-11-02 d'après la maquette retenue par l'utilisateur, « A · Jouer » :
/// une grille de 3 × 3 cartes). Un mode a sa carte au menu ; l'organisation (qui affronte qui) et les options se
/// règlent au salon (GDD § 11, « Mode ou option »). Le mode est un réglage de la partie (MatchRules.mode), que
/// l'hôte change au salon. Se jouent : le classique, le Golden Gun, le Gun Game, l'Élimination confirmée, la Zone et le Tag Team (2026-11-02) ; les autres modes de la sortie
/// et les deux gardés pour après sont montrés verrouillés, pour dire où le jeu va. Les cartes jouables d'abord,
/// puis les autres dans l'ordre du GDD (§ 11). Fonctions pures, testées.
/// </summary>
public static class GameModeCatalog
{
    // Un tableau, parcouru par son indice : MatchRules.Sanitized passe par IsPlayable, et un foreach sur l'interface allouerait
    // son énumérateur à chaque fois.
    private static readonly GameMode[] All =
    {
        new GameMode(GameModeId.Classic, "Classique",
            "Un contre un, en manches : la première mort termine la manche.",
            "Le duel du jeu : un contre un, en manches, et la première mort termine la manche ; ou en Death match, avec réapparitions. Les autres attendent dans la tribune, et peuvent s'en mêler.",
            "EN MANCHES", "En manches, BO5 ou BO3, ou en Death match", "2 À 6", "2 à 6", "Gagner 3 manches (ou 2), ou 10 éliminations",
            GameModeAvailability.Playable, "ico-target"),
        new GameMode(GameModeId.GoldenGun, "Golden Gun",
            "Une arme à part, une seule balle, et elle tue partout.",
            "Tout le monde a la même arme, qui n'existe que dans ce mode : une balle, rechargée après chaque tir, qui tue partout. Rater laisse le gant.",
            "EN MANCHES", "En manches, sans préparation", "2 À 6", "2 à 6", "Le premier à 5 manches (ou 7)",
            GameModeAvailability.Playable, "ico-bullet"),
        new GameMode(GameModeId.GunGame, "Gun Game",
            "Chaque élimination fait passer à l'arme suivante. On finit au gant.",
            "Chaque élimination fait passer à l'arme suivante, des plus fortes aux plus faibles. On revient trois secondes après une mort ; le premier qui finit au gant gagne.",
            "RÉAPPARITIONS", "En continu, avec réapparitions", "2 À 6", "2 à 6", "Finir l'échelle, puis le gant",
            GameModeAvailability.Playable, "ico-ladder"),
        new GameMode(GameModeId.KillConfirmed, "Élimination confirmée",
            "Une élimination ne compte que si l'on ramasse le module du robot détruit.",
            "Un robot détruit lâche son module mémoire : l'élimination ne compte que si le vainqueur va le ramasser. La victime, revenue, peut le reprendre pour l'annuler.",
            "RÉAPPARITIONS", "En continu, avec réapparitions", "2 À 6", "2 à 6", "Le premier à 10 éliminations confirmées",
            GameModeAvailability.Playable, "ico-chip"),
        new GameMode(GameModeId.Zone, "Zone",
            "Tenir la zone qui se déplace : chaque seconde dedans rapporte.",
            "Une zone se déplace dans l'arène, annoncée un peu avant. Chaque seconde dedans rapporte des points ; à deux dedans, plus rien n'avance.",
            "RÉAPPARITIONS", "En continu, avec réapparitions", "2 À 6", "2 à 6", "Le premier à 3 000 points, ou le plus de points en 10 minutes",
            GameModeAvailability.Playable, "ico-zone"),
        new GameMode(GameModeId.TagTeam, "Tag Team",
            "En équipes : quand un duelliste tombe, son coéquipier entre.",
            "Deux équipes, un duelliste chacune à la fois : quand il tombe, un coéquipier entre, et le survivant garde ses PV. Ceux qui attendent aident depuis la tribune.",
            "EN ÉQUIPES", "En continu, un duelliste par équipe", "4 OU 6", "4 ou 6", "La première équipe à 100 points",
            GameModeAvailability.Playable, "ico-users"),
        new GameMode(GameModeId.Tournament, "Tournoi",
            "Tout le monde contre tout le monde, puis une finale.",
            "Tout le monde affronte tout le monde, plusieurs matchs en même temps, puis les deux premiers jouent la finale. Entre deux matchs, la salle d'attente montre en direct les duels en cours.",
            "POULE ET FINALE", "Matchs en BO3, finale en BO5", "3 À 6", "3 à 6", "Gagner la finale",
            GameModeAvailability.Playable, "ico-trophy"),
        new GameMode(GameModeId.Darkness, "Arène dans le noir",
            "Le classique, lumières éteintes : on voit ce qu'on entend.",
            "Le classique, lumières éteintes : on ne voit que les tirs, les blessures des robots touchés, et l'écho des bruits.",
            "EN MANCHES", "En manches, BO5 ou BO3", "2 À 6", "2 à 6", "Gagner 3 manches (ou 2)",
            GameModeAvailability.AfterRelease, "ico-moon"),
        new GameMode(GameModeId.HackAndDefend, "Hack & Defend",
            "Un attaquant, un défenseur, un terminal à pirater.",
            "À chaque manche, un attaquant et un défenseur, une seule vie : pirater un terminal, ou l'en empêcher. Les rôles s'échangent à la manche suivante.",
            "EN MANCHES", "En manches, avec un chrono", "2 À 6", "2 à 6", "Gagner 3 manches",
            GameModeAvailability.AfterRelease, "ico-terminal"),
    };

    public static readonly IReadOnlyList<GameMode> Modes = All;

    public static GameMode Get(GameModeId id)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].id == id) return All[i];
        }
        return All[0];
    }

    /// <summary>Le mode se joue-t-il ? (sa carte crée un salon, et il reste dans les réglages)</summary>
    public static bool IsPlayable(GameModeId id)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].id == id) return All[i].CanCreate;
        }
        return false;
    }

    /// <summary>Les réglages de départ du salon qu'une carte ouvre : un duel sans tribune, dans le nombre de
    /// manches habituel du mode (BO5 au classique, le premier à 5 au Golden Gun, d'une traite au Gun Game, sur la
    /// grande échelle, et à l'Élimination confirmée, le premier à 10). L'hôte ouvre la tribune et règle le reste au
    /// salon. Le Tag Team ouvre un deux contre deux, et sa tribune agit : ceux qui attendent y aident leur équipe
    /// (GDD § 11, TT5).</summary>
    public static MatchRules RulesFor(GameModeId id) => new MatchRules
    {
        capacity = id == GameModeId.TagTeam ? (byte)TagTeam.MinPlayers
                 : id == GameModeId.Tournament ? (byte)Tournament.MaxPlayers : (byte)MatchRules.MinCapacity,
        organization = DuelOrganization.FixedDuelists,
        spectatorInterventions = id == GameModeId.TagTeam,
        roundsToWin = MatchRules.RoundChoices(id).usual,
        mode = id,
    }.Sanitized();

    /// <summary>Le mode que des réglages jouent (le classique pour un mode qui ne se joue pas encore).</summary>
    public static GameModeId ModeOf(MatchRules rules) => rules.Sanitized().mode;

    /// <summary>Les mêmes réglages dans un autre mode, choisi au salon (« Changer de mode ») : les joueurs, les
    /// places, l'organisation et la tribune restent ; les manches reprennent le nombre habituel du nouveau
    /// mode. Fonction pure, testée.</summary>
    public static MatchRules WithMode(MatchRules rules, GameModeId id)
    {
        MatchRules r = rules.Sanitized();
        if (!IsPlayable(id) || r.mode == id) return r;
        r.mode = id;
        r.roundsToWin = MatchRules.RoundChoices(id).usual;
        return r.Sanitized();
    }

    /// <summary>Le mode impose-t-il l'arme ? (le Golden Gun : une arme à lui, pour tous ; le Gun Game : celle de la
    /// marche de chacun). La préparation, où l'on choisit son arme, n'a alors pas lieu, on ne ramasse pas d'arme, et
    /// la tribune n'en lance pas (GDD § 11).</summary>
    public static bool ImposesWeapon(GameModeId id) => id == GameModeId.GoldenGun || id == GameModeId.GunGame;

    /// <summary>Le nom d'un mode, comme au salon : « Classique », « Gun Game ».</summary>
    public static string Name(GameModeId id) => Get(id).name;

    /// <summary>La partie en une ligne, au pied du salon : le mode, puis ce qui est réglé
    /// (« Classique · BO5 · le gagnant reste · arme libre · cailloux et bagarre »).</summary>
    public static string Summary(GameModeId mode, MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        var parts = new List<string> { Name(mode), r.Format };
        // Au Tag Team, les équipes disent qui affronte qui ; au Tournoi, tout le monde affronte tout le monde.
        if (r.IsTagTeam) parts.Add($"{r.capacity / 2} contre {r.capacity / 2}");
        else if (r.IsTournament) parts.Add($"{r.capacity} joueurs au plus");
        else
        {
            parts.Add(r.organization == DuelOrganization.KingOfTheHill ? "le gagnant reste" : "toujours les mêmes");
            if (r.randomDuelists) parts.Add("duellistes tirés au sort");
        }
        if (!ImposesWeapon(mode)) parts.Add(WeaponChoiceLabel(r.EffectiveWeaponChoice));
        if (r.headshotsOnly) parts.Add("tête seulement");
        if (r.spectatorInterventions && !r.IsTournament) parts.Add("cailloux et bagarre");
        return string.Join(" · ", parts);
    }

    /// <summary>Le choix de l'arme en deux ou trois mots, pour le résumé de la partie.</summary>
    public static string WeaponChoiceLabel(WeaponChoice choice) => choice switch
    {
        WeaponChoice.CoinFlip => "pile ou face",
        WeaponChoice.CommonArsenal => "arsenal commun",
        _ => "arme libre",
    };

    /// <summary>Le libellé de ce qu'une carte permet, sur sa vignette (vide pour un mode jouable).</summary>
    public static string Badge(GameModeAvailability availability) => availability switch
    {
        GameModeAvailability.ComingSoon => "BIENTÔT",
        GameModeAvailability.AfterRelease => "APRÈS LA SORTIE",
        _ => "",
    };
}
