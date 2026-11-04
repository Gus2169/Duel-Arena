using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le salon (2026-11-02, d'après la maquette retenue par l'utilisateur, « B · Salon ») : les joueurs en liste à
/// gauche (leur couleur, leur nom, leur rôle) ; à droite les réglages de la partie, les essentiels visibles et le
/// reste sous « Plus de réglages », puis l'image de l'arène ; au pied, la partie en une ligne et « Lancer la
/// partie ». L'hôte change de mode dans une fenêtre.
///
/// Le même écran sert aux deux salons : celui du menu, avant la partie en ligne (LobbyController), et celui de
/// l'arène, après un match (SalonPanel). Il ne décide rien : il montre un Model et dit ce que l'hôte a choisi
/// (RulesChanged, LaunchClicked, LeaveClicked). Il retrouve ses éléments par nom dans le gabarit
/// `Shared/SalonView.uxml`, et crée lui-même les rangées des réglages et les joueurs.
///
/// Les choix des réglages sont des données (EssentialRows, AdvancedRows, fonctions pures testées) : ce qui
/// n'existe pas encore dans le jeu est montré éteint, sous un petit cadenas (Au hasard, Armes insolites : GDD § 4 et
/// § 11) ; les cases qui ne changeraient rien aujourd'hui (les blessures restent) attendent leur règle pour apparaître.
///
/// Au Tag Team (2026-11-02, GDD § 11, TT1), les joueurs sont rangés par équipe ; l'hôte en change un d'équipe d'un
/// clic (PlayerClicked : A, B, hors équipe) et tire les équipes au sort (ShuffleClicked).
///
/// Sous les joueurs, le tableau de la soirée (2026-11-03, GDD § 14) : qui a gagné quoi depuis l'ouverture de la
/// partie, et le dernier match (Evening).
/// </summary>
public sealed class SalonView
{
    /// <summary>Un joueur du salon, tel que la liste le montre.</summary>
    public struct Player
    {
        public string name;
        public Color32 paint;
        /// <summary>Son rôle en capitales, « DUELLISTE · HÔTE » (Role).</summary>
        public string role;
        /// <summary>Le joueur de cette machine : sa rangée est cernée de jaune.</summary>
        public bool local;
        /// <summary>Son équipe au Tag Team (aucune ailleurs).</summary>
        public PlayerTeam team;
    }

    /// <summary>Ce que le salon montre.</summary>
    public struct Model
    {
        /// <summary>La ligne au-dessus du mode, « SALON DE FAUCON · EN LIGNE ».</summary>
        public string eyebrow;
        public GameModeId mode;
        public MatchRules rules;
        /// <summary>Cette machine héberge : elle seule change le mode et les réglages, et lance.</summary>
        public bool host;
        /// <summary>Les réglages se changent (l'hôte, tant que la partie n'est pas lancée).</summary>
        public bool editable;
        /// <summary>Ce qu'on donne aux autres pour venir : « CODE » et le code, ou « ADRESSE » et l'adresse
        /// (vides : rien à donner).</summary>
        public string inviteCaption, inviteValue;
        /// <summary>Les joueurs, dans l'ordre : les duellistes, puis la tribune.</summary>
        public List<Player> players;
        /// <summary>Qui commence et ce qui suit, en une phrase (Phrase).</summary>
        public string phrase;
        public string status;
        public bool statusError;
        public bool canLaunch;
        /// <summary>« QUITTER LE SALON » au menu, « QUITTER LA PARTIE » dans l'arène.</summary>
        public string leaveLabel;
        /// <summary>Le Tag Team : les joueurs rangés par équipe.</summary>
        public bool teams;
        /// <summary>L'hôte change les équipes (un clic sur un joueur, le tirage au sort).</summary>
        public bool teamsEditable;
        /// <summary>Le tableau de la soirée (GDD § 14) : ses lignes, déjà rangées (Evening.Tally), le nombre de matchs finis
        /// et le dernier, en une ligne (Evening.MatchLine). Sans ligne, la section se cache (au salon du menu, toujours).</summary>
        public List<EveningLine> evening;
        public int eveningMatches;
        public string eveningLast;
    }

    /// <summary>Une ligne du tableau de la soirée, telle que le salon la montre.</summary>
    public struct EveningLine
    {
        public string name;
        /// <summary>Sa couleur, s'il est encore là (sinon, gris).</summary>
        public Color32 paint;
        /// <summary>Le joueur de cette machine : sa ligne est marquée de jaune.</summary>
        public bool local;
        /// <summary>« 3 VICTOIRES » (Evening.WinsLabel).</summary>
        public string wins;
        /// <summary>« Classique 2 · Gun Game 1 · sur 4 matchs » (Evening.Breakdown).</summary>
        public string detail;
    }

    /// <summary>Un choix d'une rangée des réglages.</summary>
    public readonly struct Choice
    {
        public readonly string label;
        public readonly bool selected;
        /// <summary>Pas encore dans le jeu : éteint, sous un petit cadenas.</summary>
        public readonly bool locked;
        /// <summary>Hors de portée aujourd'hui (moins de places que de joueurs présents) : éteint, sans cadenas.</summary>
        public readonly bool unavailable;
        /// <summary>Les réglages une fois ce choix fait (null pour un choix verrouillé).</summary>
        public readonly Func<MatchRules, MatchRules> apply;

        public Choice(string label, bool selected, Func<MatchRules, MatchRules> apply, bool locked = false, bool unavailable = false)
        {
            this.label = label;
            this.selected = selected;
            this.apply = apply;
            this.locked = locked;
            this.unavailable = unavailable;
        }

        public bool Enabled => !locked && !unavailable && apply != null;
    }

    /// <summary>Une rangée des réglages : un choix parmi plusieurs, une case à cocher, ou ce que le mode fixe
    /// (une phrase, qui ne se change pas).</summary>
    public readonly struct Row
    {
        public readonly string label;
        /// <summary>Les choix (vide pour une case à cocher ou ce que le mode fixe).</summary>
        public readonly Choice[] choices;
        /// <summary>Une case à cocher : cochée ou non, et ce qu'elle fait des réglages.</summary>
        public readonly bool isCheck, check;
        public readonly Func<MatchRules, bool, MatchRules> setCheck;
        /// <summary>Ce que le mode fixe : rien à choisir, `hint` le dit.</summary>
        public readonly bool isFixed;
        /// <summary>Une phrase à côté d'une case à cocher, ou ce que le mode fixe.</summary>
        public readonly string hint;

        public Row(string label, params Choice[] choices)
        {
            this.label = label;
            this.choices = choices;
            isCheck = false;
            check = false;
            setCheck = null;
            isFixed = false;
            hint = "";
        }

        public Row(string label, bool check, Func<MatchRules, bool, MatchRules> setCheck, string hint)
        {
            this.label = label;
            choices = Array.Empty<Choice>();
            isCheck = true;
            this.check = check;
            this.setCheck = setCheck;
            isFixed = false;
            this.hint = hint;
        }

        /// <summary>Ce que le mode fixe, dit en une phrase.</summary>
        public Row(string label, string fixedText)
        {
            this.label = label;
            choices = Array.Empty<Choice>();
            isCheck = false;
            check = false;
            setCheck = null;
            isFixed = true;
            hint = fixedText;
        }
    }

    // ------------------------------------------------------------------
    // Ce que le salon propose (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>Les réglages visibles d'emblée : les manches, qui affronte qui, l'arme (la maquette B). Les
    /// manches sont celles du mode (BO3 ou BO5 au classique, le premier à 5 ou à 7 au Golden Gun), et l'arme,
    /// celle qu'il impose s'il en impose une. Le Gun Game n'a pas de manches : l'échelle et ses armes à la place ;
    /// le Death match du classique et l'Élimination confirmée, les éliminations à faire ; la Zone, son but, aux points.</summary>
    public static List<Row> EssentialRows(MatchRules rules)
    {
        MatchRules r = rules.Sanitized();
        bool king = r.organization == DuelOrganization.KingOfTheHill;
        var rows = new List<Row>();
        // Le Tag Team : ses points, et ses équipes à la place de qui affronte qui.
        if (r.IsTagTeam)
        {
            rows.Add(new Row("Le but", $"La première équipe à {TagTeam.TargetPoints} points : {TagTeam.KillPoints} par élimination, {TagTeam.HeadshotPoints} à la tête, {TagTeam.GlovePoints} au gant"));
            rows.Add(new Row("Les équipes", "Le premier de chaque équipe se bat ; quand il tombe, un coéquipier entre, et le survivant garde ses PV"));
            rows.Add(WeaponRow(r));
            return rows;
        }
        // Le Tournoi : son but et ses matchs, fixés (GDD § 11, TO1, TO4, TO9).
        if (r.IsTournament)
        {
            rows.Add(new Row("Le but", "Gagner la finale : la poule, chacun contre chacun, puis la finale entre les deux premiers"));
            rows.Add(new Row("Les matchs", "BO3 en poule, BO5 en finale ; ceux d'un tour en même temps, chacun dans son arène"));
            rows.Add(WeaponRow(r));
            return rows;
        }
        if (r.mode == GameModeId.GunGame)
        {
            rows.Add(LadderRow(r));
            rows.Add(new Row("Ses armes", GunGame.Describe(r.ladder)));
        }
        // La Zone se gagne aux points, à un compte fixe.
        else if (r.CountsPoints)
            rows.Add(new Row("Le but", $"Le premier à {ZoneMode.Points(ZoneMode.TargetPoints)} points, ou le plus de points en {Mathf.RoundToInt(ZoneMode.MatchDuration / 60f)} minutes : {ZoneMode.PointsPerTick} par seconde seul dans la zone, {ZoneMode.PointsPerKill} par élimination"));
        // Au Death match du classique (sa case est sous « Plus de réglages ») et à l'Élimination confirmée, les
        // éliminations à faire à la place des manches.
        else if (r.CountsKills) rows.Add(KillsRow(r));
        else rows.Add(RoundsRow(r));
        // Le hasard du GDD (§ 4 : le gagnant reste, son adversaire est tiré dans la file) viendra ici.
        rows.Add(new Row("Qui affronte qui",
            new Choice("Le gagnant reste", king, x => { x.organization = DuelOrganization.KingOfTheHill; return x; }),
            new Choice("Toujours les mêmes", !king, x => { x.organization = DuelOrganization.FixedDuelists; return x; }),
            new Choice("Au hasard", false, null, locked: true)));
        rows.Add(WeaponRow(r));
        return rows;
    }

    /// <summary>Les éliminations à faire au Death match du classique, ou à confirmer à l'Élimination confirmée.</summary>
    private static Row KillsRow(MatchRules r)
    {
        var choices = new Choice[MatchRules.KillChoices.Length];
        for (int i = 0; i < choices.Length; i++)
        {
            byte kills = MatchRules.KillChoices[i];
            choices[i] = new Choice("Premier à " + kills, r.killsToWin == kills, x => { x.killsToWin = kills; return x; });
        }
        return new Row("Éliminations", choices);
    }

    /// <summary>Les échelles du Gun Game (GDD § 11, GG2) : la grande, puis une par rang d'arme.</summary>
    private static Row LadderRow(MatchRules r)
    {
        var choices = new Choice[GunGame.Ladders.Count];
        for (int i = 0; i < choices.Length; i++)
        {
            GunGameLadder ladder = GunGame.Ladders[i];
            choices[i] = new Choice(GunGame.Name(ladder), r.ladder == ladder, x => { x.ladder = ladder; return x; });
        }
        return new Row("L'échelle", choices);
    }

    /// <summary>Les manches que le mode offre, la plus courte d'abord.</summary>
    private static Row RoundsRow(MatchRules r)
    {
        (byte usual, byte other) = MatchRules.RoundChoices(r.mode);
        byte shorter = (byte)Mathf.Min(usual, other), longer = (byte)Mathf.Max(usual, other);
        return new Row("Manches",
            new Choice(RoundsLabel(r.mode, shorter), r.roundsToWin == shorter, x => { x.roundsToWin = shorter; return x; }),
            new Choice(RoundsLabel(r.mode, longer), r.roundsToWin == longer, x => { x.roundsToWin = longer; return x; }));
    }

    /// <summary>« BO3 » au classique (deux manches à gagner), « Premier à 5 » au Golden Gun.</summary>
    public static string RoundsLabel(GameModeId mode, int roundsToWin)
        => mode == GameModeId.GoldenGun ? "Premier à " + roundsToWin : "BO" + (roundsToWin * 2 - 1);

    /// <summary>Le choix de l'arme, un seul réglage (GDD § 11) : libre, pile ou face, ou arsenal commun ; ou ce
    /// que le mode impose (le Golden Gun).</summary>
    private static Row WeaponRow(MatchRules r)
    {
        if (r.mode == GameModeId.GoldenGun) return new Row("L'arme", "Le Golden Gun, le même pour tous");
        if (r.mode == GameModeId.GunGame) return new Row("L'arme", "Celle de sa marche, la suivante à chaque élimination");
        // Pile ou face et l'arsenal commun ne valent que dans les modes à manches où l'arme est libre (GDD § 11).
        bool applies = MatchRules.WeaponChoiceApplies(r.mode, r.IsDeathMatch);
        WeaponChoice choice = r.EffectiveWeaponChoice;
        return new Row("L'arme",
            new Choice("Libre", choice == WeaponChoice.Free, x => { x.weaponChoice = WeaponChoice.Free; return x; }),
            new Choice("Pile ou face", choice == WeaponChoice.CoinFlip, x => { x.weaponChoice = WeaponChoice.CoinFlip; return x; }, unavailable: !applies),
            new Choice("Arsenal commun", choice == WeaponChoice.CommonArsenal, x => { x.weaponChoice = WeaponChoice.CommonArsenal; return x; }, unavailable: !applies));
    }

    /// <summary>Les réglages sous « Plus de réglages » : au classique, la case du Death match ; les places (jamais
    /// moins que les joueurs présents), le tirage des duellistes, ce que fait la tribune.</summary>
    public static List<Row> AdvancedRows(MatchRules rules, int playerCount)
    {
        MatchRules r = rules.Sanitized();
        int floor = Mathf.Clamp(playerCount, MatchRules.MinCapacity, MatchRules.MaxCapacity);
        // Le Tag Team se joue à quatre au moins : deux contre deux.
        if (r.IsTagTeam) floor = Mathf.Max(floor, TagTeam.MinPlayers);
        // Le Tournoi, à trois au moins : à deux, la poule serait déjà la finale.
        if (r.IsTournament) floor = Mathf.Max(floor, Tournament.MinPlayers);
        var places = new Choice[MatchRules.MaxCapacity - MatchRules.MinCapacity + 1];
        for (int c = MatchRules.MinCapacity; c <= MatchRules.MaxCapacity; c++)
        {
            byte capacity = (byte)c;
            places[c - MatchRules.MinCapacity] = new Choice(c.ToString(), r.capacity == c,
                x => { x.capacity = capacity; return x; }, unavailable: c < floor);
        }
        var rows = new List<Row>();
        // Le Death match, une case du classique (GDD § 11, N8) : des éliminations, avec réapparitions, au lieu des manches.
        if (r.mode == GameModeId.Classic)
            rows.Add(new Row("Death match", r.deathMatch, (x, on) => { x.deathMatch = on; return x; },
                             $"Le premier à {r.killsToWin} éliminations, avec réapparitions, au lieu des manches"));
        // Tête seulement (GDD § 11, I3), dans tous les modes.
        rows.Add(new Row("Tête seulement", r.headshotsOnly, (x, on) => { x.headshotsOnly = on; return x; },
                         "Seuls les tirs à la tête blessent ; le gant tue toujours"));
        rows.Add(new Row("Places", places));
        // Au Tag Team, les équipes se tirent au sort à côté des joueurs (le bouton « Équipes au hasard ») ; au Tournoi,
        // tout le monde se bat, dans l'ordre du tirage.
        if (!r.IsTagTeam && !r.IsTournament)
            rows.Add(new Row("Duellistes tirés au sort", r.randomDuelists,
                (x, on) => { x.randomDuelists = on; return x; }, "Qui commence, au hasard, au lancement"));
        // Les armes insolites tomberont de la tribune (Dons d'armes, GDD § 9) ; d'ici là, rien ne tombe. Le Tournoi n'a pas
        // de tribune : on attend son match dans la salle d'attente, sans intervenir (TO5).
        if (r.IsTournament) return rows;
        rows.Add(new Row("La tribune",
            new Choice("Rien", !r.spectatorInterventions, x => { x.spectatorInterventions = false; return x; }),
            new Choice("Cailloux et bagarre", r.spectatorInterventions, x => { x.spectatorInterventions = true; return x; }),
            new Choice("Armes insolites", false, null, locked: true)));
        return rows;
    }

    /// <summary>Le rôle d'un joueur au match suivant, en capitales : « DUELLISTE », « TRIBUNE · 1ER »,
    /// « DANS LE TIRAGE », suivi de « HÔTE » et de « TOI ».</summary>
    public static string Role(bool duelist, int queuePosition, bool drawn, bool host, bool local)
    {
        string role = drawn ? "DANS LE TIRAGE"
            : duelist ? "DUELLISTE"
            : queuePosition <= 0 ? "TRIBUNE"
            : queuePosition == 1 ? "TRIBUNE · 1ER"
            : $"TRIBUNE · {queuePosition}E";
        if (host) role += " · HÔTE";
        if (local) role += " · TOI";
        return role;
    }

    /// <summary>Le rôle d'un joueur du Tag Team, en capitales : « SE BAT EN PREMIER », « ENTRE EN 2E », ou « TRIBUNE »
    /// hors équipe, suivi de « HÔTE » et de « TOI ». `place` : sa place dans le relais de son équipe (0 : le premier à se
    /// battre).</summary>
    public static string TeamRole(PlayerTeam team, int place, bool host, bool local)
    {
        string role = team == PlayerTeam.None ? "TRIBUNE"
            : place <= 0 ? "SE BAT EN PREMIER"
            : $"ENTRE EN {place + 1}E";
        if (host) role += " · HÔTE";
        if (local) role += " · TOI";
        return role;
    }

    /// <summary>Le rôle d'un joueur au Tournoi, en capitales : tous y jouent (« DANS LE TOURNOI »), suivi de « HÔTE » et
    /// de « TOI ».</summary>
    public static string TournamentRole(bool host, bool local)
    {
        string role = "DANS LE TOURNOI";
        if (host) role += " · HÔTE";
        if (local) role += " · TOI";
        return role;
    }

    /// <summary>Ce que le Tournoi sera, en une phrase, selon les joueurs présents : ses matchs de poule, ses tours, ceux
    /// qui se jouent à la fois et le repos à nombre impair, puis la finale ; ou ce qu'il manque (trois joueurs au moins).
    /// Fonction pure, testée.</summary>
    public static string TournamentPhrase(int playerCount)
    {
        if (playerCount < Tournament.MinPlayers) return "Il faut trois joueurs au moins : chacun affronte tous les autres.";
        int matches = playerCount * (playerCount - 1) / 2;
        int perTour = Tournament.MatchesPerTour(playerCount);
        string phrase = $"{playerCount} joueurs : {matches} matchs de poule en {Tournament.TourCount(playerCount)} tours";
        if (perTour > 1) phrase += $", {perTour} à la fois";
        if (playerCount % 2 == 1) phrase += ", un joueur au repos à chaque tour";
        return phrase + ", puis la finale entre les deux premiers.";
    }

    /// <summary>Qui commence au Tag Team, en une phrase : les premiers de chaque équipe (`firstA`, `firstB`), ou ce qu'il
    /// manque (deux équipes égales de deux ou trois joueurs, GDD § 11, TT2, TT9).</summary>
    public static string TeamPhrase(int teamA, int teamB, string firstA, string firstB)
    {
        if (TagTeam.CanPlay(teamA, teamB))
            return $"{teamA} contre {teamB} : {firstA} commence pour l'équipe A, {firstB} pour l'équipe B. Ceux qui attendent aident depuis la tribune.";
        if (teamA + teamB < TagTeam.MinPlayers)
            return "Il faut quatre joueurs : deux par équipe.";
        return "Les équipes doivent compter autant de joueurs : deux ou trois de chaque côté.";
    }

    /// <summary>Qui commence, et ce qui suit, en une phrase. `first` et `second` : les deux duellistes
    /// (null tant qu'on ne les connaît pas) ; `drawn` : ils seront tirés au sort au lancement ; `tribune` :
    /// la partie a des places de tribune ; `invite` : ce qu'on donne pour venir (« le code du salon »).</summary>
    public static string Phrase(int playerCount, string first, string second, bool drawn, DuelOrganization organization,
                                bool tribune, string invite)
    {
        if (playerCount <= 0) return "";
        if (playerCount < DuelRoster.DuelistSlots)
            return string.IsNullOrEmpty(invite) ? "En attente d'un adversaire." : $"En attente d'un adversaire : donne-lui {invite}.";
        string next = !tribune ? ""
            : organization == DuelOrganization.KingOfTheHill ? " Le gagnant reste ; le perdant passe au bout de la file."
            : " Les autres regardent depuis la tribune.";
        if (drawn) return "Les deux duellistes seront tirés au sort au lancement." + next;
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) return next.TrimStart();
        return tribune ? $"{first} et {second} commencent.{next}" : $"{first} et {second} s'affrontent.";
    }

    /// <summary>Les modes de la fenêtre « Changer de mode » : ceux de la sortie (les jouables, puis ceux à
    /// venir, verrouillés) ; les modes d'après la sortie n'y sont pas.</summary>
    public static List<GameMode> WindowModes()
    {
        var modes = new List<GameMode>();
        foreach (GameMode mode in GameModeCatalog.Modes)
        {
            if (mode.availability != GameModeAvailability.AfterRelease) modes.Add(mode);
        }
        return modes;
    }

    // ------------------------------------------------------------------
    // L'écran
    // ------------------------------------------------------------------

    /// <summary>L'hôte a changé les réglages (déjà ramenés dans leurs bornes).</summary>
    public event Action<MatchRules> RulesChanged;
    public event Action LaunchClicked;
    public event Action LeaveClicked;
    /// <summary>Au Tag Team, l'hôte a cliqué un joueur (son rang dans Model.players) : il change d'équipe.</summary>
    public event Action<int> PlayerClicked;
    /// <summary>Au Tag Team, l'hôte tire les équipes au sort.</summary>
    public event Action ShuffleClicked;

    private readonly VisualElement view;
    private readonly Label eyebrow, modeName, codeCaption, code, copyLabel, playersTitle, phrase, hint;
    private readonly Label summary, status, leaveLabel, moreLabel, arenaName, eveningTitle, eveningLast;
    private readonly VisualElement codeBlock, playerList, essentials, advanced, arenaImage, modeWindow, modeList;
    private readonly VisualElement evening, eveningList;
    private readonly Button changeMode, copy, more, leave, launch, modeClose, shuffle;

    private Model model;
    private bool advancedOpen;
    private float copiedUntil;

    /// <summary>`root` : un ancêtre du gabarit (le document, ou l'écran qui l'instancie).</summary>
    public SalonView(VisualElement root)
    {
        view = root.Q<VisualElement>("salonView");
        // Le conteneur du gabarit (TemplateContainer) prend toute la place que lui laisse l'écran.
        if (view.parent != null && view.parent != root) view.parent.style.flexGrow = 1f;

        eyebrow = view.Q<Label>("salonEyebrow");
        modeName = view.Q<Label>("salonModeName");
        changeMode = view.Q<Button>("salonChangeMode");
        codeBlock = view.Q<VisualElement>("salonCodeBlock");
        codeCaption = view.Q<Label>("salonCodeCaption");
        code = view.Q<Label>("salonCode");
        copy = view.Q<Button>("salonCopy");
        copyLabel = view.Q<Label>("salonCopyLabel");
        playersTitle = view.Q<Label>("salonPlayersTitle");
        playerList = view.Q<VisualElement>("salonPlayerList");
        phrase = view.Q<Label>("salonPhrase");
        evening = view.Q<VisualElement>("salonEvening");
        eveningTitle = view.Q<Label>("salonEveningTitle");
        eveningList = view.Q<VisualElement>("salonEveningList");
        eveningLast = view.Q<Label>("salonEveningLast");
        essentials = view.Q<VisualElement>("salonEssentials");
        more = view.Q<Button>("salonMore");
        moreLabel = view.Q<Label>("salonMoreLabel");
        advanced = view.Q<VisualElement>("salonAdvanced");
        hint = view.Q<Label>("salonHint");
        arenaImage = view.Q<VisualElement>("salonArenaImage");
        arenaName = view.Q<Label>("salonArenaName");
        leave = view.Q<Button>("salonLeave");
        leaveLabel = view.Q<Label>("salonLeaveLabel");
        status = view.Q<Label>("salonStatus");
        summary = view.Q<Label>("salonSummary");
        launch = view.Q<Button>("salonLaunch");
        modeWindow = view.Q<VisualElement>("salonModeWindow");
        modeList = view.Q<VisualElement>("salonModeList");
        modeClose = view.Q<Button>("salonModeClose");
        shuffle = view.Q<Button>("salonShuffle");

        // La fenêtre du mode voile tout l'écran qui accueille le salon, ses marges comprises : elle passe au bout
        // de l'hôte, avec les feuilles de style du gabarit (qui ne valent que pour ses descendants).
        VisualElement template = view.parent;
        if (template != null && template != root)
        {
            for (int i = 0; i < template.styleSheets.count; i++)
            {
                if (!root.styleSheets.Contains(template.styleSheets[i])) root.styleSheets.Add(template.styleSheets[i]);
            }
            root.Add(modeWindow);
        }

        changeMode.clicked += OpenWindow;
        copy.clicked += CopyInvite;
        more.clicked += ToggleAdvanced;
        leave.clicked += OnLeave;
        launch.clicked += OnLaunch;
        modeClose.clicked += CloseWindowClicked;
        shuffle.clicked += OnShuffle;
    }

    /// <summary>Débranche les boutons (le contrôleur se désactive ; l'arbre peut rester).</summary>
    public void Dispose()
    {
        changeMode.clicked -= OpenWindow;
        copy.clicked -= CopyInvite;
        more.clicked -= ToggleAdvanced;
        leave.clicked -= OnLeave;
        launch.clicked -= OnLaunch;
        modeClose.clicked -= CloseWindowClicked;
        shuffle.clicked -= OnShuffle;
        RulesChanged = null;
        LaunchClicked = null;
        LeaveClicked = null;
        PlayerClicked = null;
        ShuffleClicked = null;
    }

    /// <summary>L'arène de la partie : son image et son nom.</summary>
    public void SetArena(Texture2D image, string name)
    {
        arenaImage.style.backgroundImage = image != null ? new StyleBackground(image) : new StyleBackground(StyleKeyword.None);
        arenaName.text = string.IsNullOrEmpty(name) ? "" : name;
    }

    public void Show(Model value)
    {
        model = value;
        MatchRules rules = value.rules.Sanitized();
        List<Player> players = value.players ?? new List<Player>();

        eyebrow.text = value.eyebrow ?? "";
        modeName.text = GameModeCatalog.Get(value.mode).title;
        changeMode.EnableInClassList("hidden", !value.host);
        changeMode.SetEnabled(value.editable);

        bool invite = !string.IsNullOrEmpty(value.inviteValue);
        codeBlock.EnableInClassList("hidden", !invite);
        codeCaption.text = value.inviteCaption ?? "";
        code.text = invite ? value.inviteValue : "";

        playersTitle.text = $"LES JOUEURS · {players.Count} SUR {rules.capacity}";
        playerList.Clear();
        if (value.teams)
        {
            // Au Tag Team : l'équipe A, l'équipe B, puis ceux qui n'en ont pas (sur la tribune).
            foreach (PlayerTeam team in TeamOrder)
            {
                bool any = false;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].team != team) continue;
                    if (!any) playerList.Add(TeamHeader(team));
                    any = true;
                    playerList.Add(PlayerRow(players[i], value.teamsEditable ? i : -1));
                }
            }
        }
        else
        {
            for (int i = 0; i < players.Count; i++) playerList.Add(PlayerRow(players[i], -1));
        }
        for (int i = players.Count; i < rules.capacity; i++) playerList.Add(FreeRow());
        phrase.text = value.phrase ?? "";
        ShowEvening(value, players.Count);
        shuffle.EnableInClassList("hidden", !value.teams || !value.host);
        shuffle.SetEnabled(value.teamsEditable);

        BuildRows(essentials, EssentialRows(rules), value.editable);
        List<Row> advancedRows = AdvancedRows(rules, players.Count);
        BuildRows(advanced, advancedRows, value.editable);
        advanced.EnableInClassList("hidden", !advancedOpen);
        more.EnableInClassList("salon-view__more--open", advancedOpen);
        moreLabel.text = advancedOpen ? "MOINS DE RÉGLAGES" : $"PLUS DE RÉGLAGES ({advancedRows.Count})";
        hint.text = value.host
            ? "Chaque changement s'affiche aussitôt chez les autres joueurs."
            : "Seul l'hôte change les réglages.";

        leaveLabel.text = string.IsNullOrEmpty(value.leaveLabel) ? "QUITTER LE SALON" : value.leaveLabel;
        status.text = value.status ?? "";
        status.EnableInClassList("da-status--error", value.statusError);
        summary.text = GameModeCatalog.Summary(value.mode, rules);
        launch.EnableInClassList("hidden", !value.host);
        launch.SetEnabled(value.host && value.canLaunch);

        if (!value.editable) CloseWindow();
        else if (IsWindowOpen) BuildModeList();
    }

    /// <summary>Remet « COPIER » un moment après « COPIÉ ! » (appelé à chaque image par le contrôleur).</summary>
    public void Tick(float now)
    {
        if (copiedUntil <= 0f || now < copiedUntil) return;
        copiedUntil = 0f;
        copyLabel.text = "COPIER";
    }

    public bool IsWindowOpen => modeWindow != null && !modeWindow.ClassListContains("hidden");

    /// <summary>Ferme la fenêtre « Changer de mode » si elle est ouverte (Échap) ; vrai si elle l'était.</summary>
    public bool CloseWindow()
    {
        if (!IsWindowOpen) return false;
        modeWindow.AddToClassList("hidden");
        return true;
    }

    // ------------------------------------------------------------------

    /// <summary>Les lignes de la soirée qui tiennent sous la liste des joueurs : toutes (six au plus) jusqu'à quatre
    /// joueurs, les quatre premières au-delà. Fonction pure, testée.</summary>
    public static int EveningRowsShown(int players, int rows) => Math.Max(0, Math.Min(rows, players > 4 ? 4 : 6));

    /// <summary>Le tableau de la soirée, sous les joueurs : caché tant qu'aucun match n'est fini.</summary>
    private void ShowEvening(Model value, int players)
    {
        List<EveningLine> lines = value.evening;
        bool any = lines != null && lines.Count > 0;
        evening.EnableInClassList("hidden", !any);
        eveningList.Clear();
        if (!any) return;
        eveningTitle.text = Evening.Title(value.eveningMatches);
        int shown = EveningRowsShown(players, lines.Count);
        for (int i = 0; i < shown; i++) eveningList.Add(EveningRow(lines[i]));
        eveningLast.text = string.IsNullOrEmpty(value.eveningLast) ? "" : "Dernier match · " + value.eveningLast;
    }

    private static VisualElement EveningRow(EveningLine line)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("salon-evening");
        if (line.local) row.AddToClassList("salon-evening--self");
        var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
        swatch.AddToClassList("salon-player__swatch");
        swatch.style.backgroundColor = line.paint.a > 0 ? (Color)line.paint : new Color(0.5f, 0.52f, 0.55f);
        row.Add(swatch);
        var text = new VisualElement { pickingMode = PickingMode.Ignore };
        text.AddToClassList("salon-evening__text");
        text.Add(NewLabel((line.name ?? "").ToUpperInvariant(), "salon-evening__name"));
        text.Add(NewLabel(line.detail ?? "", "salon-evening__detail"));
        row.Add(text);
        row.Add(NewLabel(line.wins ?? "", "salon-evening__wins"));
        return row;
    }

    private static readonly PlayerTeam[] TeamOrder = { PlayerTeam.A, PlayerTeam.B, PlayerTeam.None };

    /// <summary>Le nom d'une équipe au-dessus de ses joueurs (« HORS ÉQUIPE · TRIBUNE » pour les autres).</summary>
    private static Label TeamHeader(PlayerTeam team)
    {
        Label header = NewLabel(team == PlayerTeam.None ? "HORS ÉQUIPE · TRIBUNE" : TagTeam.Name(team).ToUpperInvariant(), "salon-team");
        if (team == PlayerTeam.None) header.AddToClassList("salon-team--none");
        return header;
    }

    /// <summary>La rangée d'un joueur ; `clickIndex` ≥ 0 : l'hôte la clique pour le changer d'équipe (le Tag Team).</summary>
    private VisualElement PlayerRow(Player player, int clickIndex)
    {
        var row = new VisualElement();
        row.AddToClassList("salon-player");
        if (player.local) row.AddToClassList("salon-player--self");
        if (clickIndex >= 0)
        {
            row.AddToClassList("salon-player--editable");
            row.AddToClassList("ui-select");   // le son d'un choix (UiSounds)
            row.tooltip = "Clique pour le changer d'équipe.";
            row.AddManipulator(new Clickable(() => PlayerClicked?.Invoke(clickIndex)));
        }
        var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
        swatch.AddToClassList("salon-player__swatch");
        swatch.style.backgroundColor = player.paint.a > 0 ? (Color)player.paint : new Color(0.5f, 0.52f, 0.55f);
        row.Add(swatch);
        row.Add(NewLabel((player.name ?? "").ToUpperInvariant(), "salon-player__name"));
        row.Add(NewLabel(player.role ?? "", "salon-player__role"));
        return row;
    }

    private static VisualElement FreeRow()
    {
        var row = new VisualElement();
        row.AddToClassList("salon-player");
        row.AddToClassList("salon-player--empty");
        var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
        swatch.AddToClassList("salon-player__swatch");
        row.Add(swatch);
        row.Add(NewLabel("Place libre", "salon-player__name"));
        return row;
    }

    private void BuildRows(VisualElement container, List<Row> rows, bool editable)
    {
        container.Clear();
        container.EnableInClassList("salon-view__rows--readonly", !editable);
        foreach (Row row in rows)
        {
            var line = new VisualElement();
            line.AddToClassList("salon-row");
            line.Add(NewLabel(row.label, "salon-row__label"));
            if (row.isFixed)
            {
                line.Add(NewLabel(row.hint, "salon-row__fixed"));
            }
            else if (row.isCheck)
            {
                var toggle = new Toggle { value = row.check, focusable = false };
                toggle.AddToClassList("da-check");
                toggle.AddToClassList("salon-row__check");
                toggle.SetEnabled(editable);
                Func<MatchRules, bool, MatchRules> set = row.setCheck;
                toggle.RegisterValueChangedCallback(evt => Change(r => set(r, evt.newValue)));
                line.Add(toggle);
                line.Add(NewLabel(row.hint, "salon-row__hint"));
            }
            else
            {
                var choices = new VisualElement();
                choices.AddToClassList("salon-row__choices");
                foreach (Choice choice in row.choices) choices.Add(ChoiceButton(choice, editable));
                line.Add(choices);
            }
            container.Add(line);
        }
    }

    private Button ChoiceButton(Choice choice, bool editable)
    {
        // Le texte dans un Label : un Button qui a des enfants ne mesure plus son propre texte.
        var button = new Button { focusable = false };
        button.AddToClassList("salon-seg");
        button.AddToClassList("ui-select");   // le son d'un choix (UiSounds)
        if (choice.selected) button.AddToClassList("salon-seg--on");
        button.Add(NewLabel(choice.label, "salon-seg__label"));
        if (choice.locked)
        {
            var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            lockIcon.AddToClassList("salon-seg__lock");
            button.Add(lockIcon);
            button.tooltip = "Pas encore dans le jeu.";
        }
        // Chez les autres, rien ne se change ; le choix déjà fait reste allumé, sans rien refaire.
        button.SetEnabled(editable && choice.Enabled);
        Func<MatchRules, MatchRules> apply = choice.apply;
        if (apply != null && !choice.selected) button.clicked += () => Change(apply);
        return button;
    }

    private void Change(Func<MatchRules, MatchRules> change)
    {
        if (!model.editable) return;
        MatchRules next = change(model.rules.Sanitized()).Sanitized();
        RulesChanged?.Invoke(next);
    }

    private void ToggleAdvanced()
    {
        advancedOpen = !advancedOpen;
        Show(model);
    }

    private void OpenWindow()
    {
        if (!model.editable) return;
        BuildModeList();
        modeWindow.RemoveFromClassList("hidden");
    }

    private void CloseWindowClicked() => CloseWindow();

    /// <summary>Les modes, celui du salon cerné de jaune ; ceux à venir éteints. Choisir un autre mode jouable
    /// change les réglages (GameModeCatalog.WithMode : les joueurs et le reste des réglages restent) et
    /// referme la fenêtre.</summary>
    private void BuildModeList()
    {
        modeList.Clear();
        foreach (GameMode mode in WindowModes())
        {
            var item = new Button { focusable = false };
            item.AddToClassList("salon-mode");
            item.AddToClassList("ui-select");
            bool current = mode.id == model.mode;
            if (current) item.AddToClassList("salon-mode--current");
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("salon-mode__top");
            top.Add(NewLabel(mode.title, "salon-mode__name"));
            string badge = current ? "EN COURS" : GameModeCatalog.Badge(mode.availability);
            if (!string.IsNullOrEmpty(badge)) top.Add(NewLabel(badge, "salon-mode__badge"));
            item.Add(top);
            item.Add(NewLabel(mode.line, "salon-mode__line"));
            item.SetEnabled(mode.CanCreate);
            GameModeId id = mode.id;
            item.clicked += () => ChooseMode(id);
            modeList.Add(item);
        }
    }

    private void ChooseMode(GameModeId id)
    {
        CloseWindow();
        if (id != model.mode) Change(r => GameModeCatalog.WithMode(r, id));
    }

    private void CopyInvite()
    {
        if (string.IsNullOrEmpty(model.inviteValue)) return;
        GUIUtility.systemCopyBuffer = model.inviteValue;
        copyLabel.text = "COPIÉ !";
        copiedUntil = Time.unscaledTime + 1.5f;
    }

    private void OnLeave() => LeaveClicked?.Invoke();

    private void OnShuffle()
    {
        if (model.host && model.teamsEditable) ShuffleClicked?.Invoke();
    }

    private void OnLaunch()
    {
        if (model.host && model.canLaunch) LaunchClicked?.Invoke();
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }
}
