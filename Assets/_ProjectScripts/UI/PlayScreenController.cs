using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'écran Jouer du menu (2026-10-17, d'après la maquette de l'utilisateur ; refait le 2026-11-02 d'après celle
/// qu'il a retenue, « A · Jouer ») : des onglets, une grille de cartes, et à droite la fiche de la carte choisie
/// avec son action. Posé sur MainMenuUI, à côté de MainMenuController, qui lui montre l'écran.
///
/// - MODES DE JEU : une grille de 3 × 3 cartes, celles de GameModeCatalog : l'icône du mode, son nom, une
///   ligne, son rythme et ses joueurs. Une carte jouable ouvre un salon privé avec les réglages de départ de
///   son mode (LobbyController.Create) ; l'organisation et les options se règlent ensuite au salon. Les modes à
///   venir disent où le jeu va, bien éteints sous un cadenas : ils ne se choisissent pas (2026-10-27, demandé
///   par l'utilisateur : on croyait pouvoir les choisir).
/// - SALONS : rejoindre un salon par son code, ou une partie du réseau local (MainMenuController).
/// - ENTRAÎNEMENT (demandé par l'utilisateur : le stand de tir y vit) : une carte, le stand de tir, la
///   Fosse et la salle de précision, qui sont dans la même scène, la Fosse et la salle de part et d'autre
///   du poste de tir (MainMenuController.StartShootingRange).
/// - CLASSEMENT : verrouillé, il viendra avec le classé, après la sortie (GDD § 14).
///
/// La fiche montre l'arène (MenuArtwork) ou le stand, le nom, ce qu'il faut savoir et le bouton.
/// </summary>
public class PlayScreenController : MonoBehaviour
{
    private enum Tab { Modes, Lobbies, Training }

    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;
    [SerializeField] private LobbyController lobby;

    private VisualElement root;
    private Button tabModes, tabLobbies, tabTraining, tabRanking;
    private VisualElement pageModes, pageLobbies, pageTraining;
    private VisualElement modeGrid, trainingGrid;
    private VisualElement detailImage, detailFacts;
    private Label detailCaption, detailTitle, detailText, detailNote, detailButtonLabel;
    private Button detailButton;

    private readonly List<(GameModeId id, Button card)> modeCards = new();
    private Button trainingCard;

    private Tab tab = Tab.Modes;
    private GameModeId selectedMode = GameModeId.Classic;
    private bool busy;
    private VisualElement wiredScreen;   // l'écran sur lequel les cartes sont construites (voir OnEnable)

    private MenuArtwork Artwork => mainMenu != null ? mainMenu.Artwork : null;

    private void OnEnable() => Wire();

    /// <summary>Retrouve les éléments et construit les cartes, une fois par arbre. Appelé aussi par OnShown :
    /// au retour d'une partie, MainMenuController montre l'écran Jouer avant que ce composant soit activé.</summary>
    private void Wire()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        if (lobby == null) lobby = GetComponent<LobbyController>();
        root = document.rootVisualElement;

        tabModes = root.Q<Button>("playTabModes");
        tabLobbies = root.Q<Button>("playTabLobbies");
        tabTraining = root.Q<Button>("playTabTraining");
        tabRanking = root.Q<Button>("playTabRanking");
        pageModes = root.Q<VisualElement>("playPageModes");
        pageLobbies = root.Q<VisualElement>("playPageLobbies");
        pageTraining = root.Q<VisualElement>("playPageTraining");
        modeGrid = root.Q<VisualElement>("modeGrid");
        trainingGrid = root.Q<VisualElement>("trainingGrid");
        detailImage = root.Q<VisualElement>("playDetailImage");
        detailCaption = root.Q<Label>("playDetailCaption");
        detailTitle = root.Q<Label>("playDetailTitle");
        detailText = root.Q<Label>("playDetailText");
        detailFacts = root.Q<VisualElement>("playDetailFacts");
        detailNote = root.Q<Label>("playDetailNote");
        detailButton = root.Q<Button>("playDetailButton");
        detailButtonLabel = root.Q<Label>("playDetailButtonLabel");

        // Une seule construction par arbre : l'UIDocument recrée le sien quand il se réactive (et, sans
        // rechargement de scène à l'entrée en Play Mode, ce composant garde ses champs d'avant). Un arbre
        // neuf se rebranche, le même ne se branche pas deux fois.
        VisualElement screen = root.Q<VisualElement>("screenPlay");
        if (screen == wiredScreen) return;
        wiredScreen = screen;
        tabModes.clicked += () => ShowTab(Tab.Modes);
        tabLobbies.clicked += () => ShowTab(Tab.Lobbies);
        tabTraining.clicked += () => ShowTab(Tab.Training);
        tabRanking.SetEnabled(false);
        tabRanking.tooltip = "Avec le classé, après la sortie.";
        detailButton.clicked += OnDetailButton;
        BuildModeCards();
        BuildTrainingCards();
    }

    /// <summary>Appelé par MainMenuController quand l'écran s'affiche.</summary>
    public void OnShown()
    {
        Wire();
        ShowTab(tab);
    }

    /// <summary>Une demande au service en ligne est en cours : pas de second salon.</summary>
    public void SetBusy(bool value)
    {
        busy = value;
        RefreshDetail();
    }

    private void ShowTab(Tab value)
    {
        tab = value;
        tabModes.EnableInClassList("da-tab--active", tab == Tab.Modes);
        tabLobbies.EnableInClassList("da-tab--active", tab == Tab.Lobbies);
        tabTraining.EnableInClassList("da-tab--active", tab == Tab.Training);
        pageModes.EnableInClassList("hidden", tab != Tab.Modes);
        pageLobbies.EnableInClassList("hidden", tab != Tab.Lobbies);
        pageTraining.EnableInClassList("hidden", tab != Tab.Training);
        RefreshDetail();
    }

    // ------------------------------------------------------------------
    // Les cartes
    // ------------------------------------------------------------------

    private void BuildModeCards()
    {
        modeGrid.Clear();
        modeCards.Clear();
        IReadOnlyList<GameMode> modes = GameModeCatalog.Modes;
        const int columns = 3;
        for (int start = 0; start < modes.Count; start += columns)
        {
            VisualElement row = NewRow(start + columns >= modes.Count);
            for (int i = start; i < Mathf.Min(start + columns, modes.Count); i++)
            {
                GameMode mode = modes[i];
                Button card = NewCard(mode.icon, mode.title, mode.line, mode.rhythm, mode.players,
                    GameModeCatalog.Badge(mode.availability), i == start + columns - 1);
                GameModeId id = mode.id;
                card.clicked += () => SelectMode(id);
                // Un mode verrouillé ne se choisit pas : ni survol, ni clic.
                if (!mode.CanCreate) card.SetEnabled(false);
                row.Add(card);
                modeCards.Add((id, card));
            }
            // Une rangée incomplète garde des cartes de la taille des autres.
            for (int i = modes.Count; i < start + columns; i++) row.Add(Spacer(i == start + columns - 1));
            modeGrid.Add(row);
        }
        RefreshCards();
    }

    /// <summary>La place d'une carte absente, pour que les cartes d'une rangée incomplète gardent leur taille.</summary>
    private static VisualElement Spacer(bool rowEnd)
    {
        var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
        spacer.AddToClassList("mode-tile");
        spacer.AddToClassList("mode-tile--spacer");
        if (rowEnd) spacer.AddToClassList("mode-tile--row-end");
        return spacer;
    }

    private void BuildTrainingCards()
    {
        trainingGrid.Clear();
        VisualElement row = NewRow(false);
        trainingCard = NewCard("ico-target", "STAND DE TIR ET FOSSE",
            "Les armes et leurs accessoires, la Fosse chronométrée, la salle de précision.", "SEUL", "HORS LIGNE", "", false);
        row.Add(trainingCard);
        row.Add(Spacer(false));
        row.Add(Spacer(true));
        trainingGrid.Add(row);
        // Une rangée sur trois : la carte a la taille de celles des modes.
        trainingGrid.Add(NewRow(false));
        trainingGrid.Add(NewRow(true));
        RefreshCards();
    }

    private static VisualElement NewRow(bool last)
    {
        var row = new VisualElement();
        row.AddToClassList("card-row");
        if (last) row.AddToClassList("card-row--last");
        return row;
    }

    /// <summary>Une carte : l'icône et l'étiquette en haut (« CHOISI », ou « BIENTÔT » sous un cadenas pour ce
    /// qui n'existe pas encore), le nom, une ligne, puis le rythme et les joueurs ; le liseré par-dessus.</summary>
    private static Button NewCard(string icon, string title, string line, string rhythm, string players, string badge, bool rowEnd)
    {
        var card = new Button { focusable = false };
        card.AddToClassList("mode-tile");
        card.AddToClassList("ui-select");
        if (rowEnd) card.AddToClassList("mode-tile--row-end");
        bool locked = !string.IsNullOrEmpty(badge);
        if (locked) card.AddToClassList("mode-tile--locked");

        VisualElement top = Part("mode-tile__top");
        VisualElement iconElement = Part("mode-tile__icon");
        iconElement.AddToClassList(icon);
        top.Add(iconElement);
        if (locked) top.Add(Part("mode-tile__lock"));
        top.Add(Part("mode-tile__spacer"));
        Label tag = NewLabel(badge, "mode-tile__tag");
        tag.EnableInClassList("hidden", !locked);
        top.Add(tag);
        card.Add(top);

        card.Add(NewLabel(title, "mode-tile__title"));
        card.Add(NewLabel(line, "mode-tile__line"));
        card.Add(Part("mode-tile__fill"));
        VisualElement chips = Part("mode-tile__chips");
        chips.Add(NewLabel(rhythm, "mode-tile__chip"));
        chips.Add(NewLabel(players, "mode-tile__chip"));
        card.Add(chips);
        card.Add(Part("mode-tile__edge", new DuelArena.UI.SlantBox()));
        return card;
    }

    private static VisualElement Part(string ussClass, VisualElement element = null)
    {
        element ??= new VisualElement();
        element.pickingMode = PickingMode.Ignore;
        element.AddToClassList(ussClass);
        return element;
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }

    private void SelectMode(GameModeId id)
    {
        selectedMode = id;
        RefreshCards();
        RefreshDetail();
    }

    private void RefreshCards()
    {
        foreach ((GameModeId id, Button card) in modeCards)
        {
            if (!card.enabledSelf) continue;   // un mode verrouillé garde son étiquette
            ShowChosen(card, id == selectedMode);
        }
        if (trainingCard != null) ShowChosen(trainingCard, true);
    }

    /// <summary>La carte choisie est cernée de jaune et dit « CHOISI ».</summary>
    private static void ShowChosen(Button card, bool chosen)
    {
        card.EnableInClassList("mode-tile--selected", chosen);
        Label tag = card.Q<Label>(className: "mode-tile__tag");
        if (tag == null) return;
        tag.text = chosen ? "CHOISI" : "";
        tag.EnableInClassList("hidden", !chosen);
    }

    private static void SetBackground(VisualElement element, Texture texture)
    {
        if (element == null) return;
        element.style.backgroundImage = texture switch
        {
            Texture2D t2 => new StyleBackground(t2),
            RenderTexture rt => new StyleBackground(Background.FromRenderTexture(rt)),
            _ => new StyleBackground(StyleKeyword.None),
        };
    }

    // ------------------------------------------------------------------
    // Le détail et son action
    // ------------------------------------------------------------------

    private void RefreshDetail()
    {
        if (detailTitle == null) return;
        detailFacts.Clear();
        MenuArtwork artwork = Artwork;
        string arena = artwork != null && !string.IsNullOrEmpty(artwork.arenaName) ? $"ARÈNE · {artwork.arenaName.ToUpperInvariant()}" : "";
        switch (tab)
        {
            case Tab.Modes:
            {
                GameMode mode = GameModeCatalog.Get(selectedMode);
                SetDetail(mode.title, mode.description, artwork != null ? artwork.arenaOverview : null, arena,
                    "Qui affronte qui et les règles de la partie se choisissent ensuite, dans le salon.");
                AddFact("JOUEURS", mode.playersLong);
                AddFact("RYTHME", mode.rhythmLong);
                AddFact("POUR GAGNER", mode.goal);
                if (mode.CanCreate) SetButton(true, "CRÉER UNE PARTIE", !busy);
                else SetButton(true, GameModeCatalog.Badge(mode.availability), false);
                break;
            }
            case Tab.Lobbies:
                SetDetail("SALONS PRIVÉS",
                    "Duel Arena se joue entre amis, dans des salons privés à code. Pour en ouvrir un, choisis un mode et crée la partie : le salon s'ouvre avec son code, à donner aux autres.",
                    artwork != null ? artwork.arenaOverview : null, arena, "");
                AddFact("JOUEURS", $"{MatchRules.MinCapacity} à {MatchRules.MaxCapacity}");
                AddFact("RÉGLAGES", "Par l'hôte, au salon");
                AddFact("PARTIE LANCÉE", "Le code reste valable");
                SetButton(false, "", false);
                break;
            default:
            {
                SetDetail("STAND DE TIR ET FOSSE",
                    "Essaie chaque arme et ses accessoires sur dix-sept mannequins, de 5 à 59 m. À droite du poste de tir, la Fosse, le parcours chronométré : des cibles se lèvent sur ton passage, hostiles à neutraliser, civils à épargner. À gauche, la salle de précision, une grande cible ronde, de 5 à 50 m.",
                    artwork != null ? artwork.rangeImage : null, "B-02 · STAND DE TIR",
                    "B ouvre la fenêtre des armes, Échap la pause et les Options.");
                AddFact("JOUEURS", "Seul, hors ligne");
                AddFact("LA FOSSE", "33 hostiles, 14 civils");
                float best = BestCourseTime();
                AddFact("TON RECORD", best > 0f ? $"{WeaponStatSheet.Number(best)} s" : "Aucun");
                SetButton(true, "LANCER", !busy && (mainMenu == null || !mainMenu.Busy));
                break;
            }
        }
    }

    // Le meilleur temps de la Fosse, toutes armes confondues (0 : aucun).
    private static float BestCourseTime()
    {
        float best = 0f;
        for (int key = CourseRecords.MixedLoadout; key < WeaponLooks.Slots; key++)
        {
            float time = CourseRecords.Best(key);
            if (time > 0f && (best <= 0f || time < best)) best = time;
        }
        return best;
    }

    private void SetDetail(string title, string text, Texture2D image, string caption, string note)
    {
        detailTitle.text = title;
        detailText.text = text;
        SetBackground(detailImage, image);
        detailCaption.text = caption;
        detailCaption.EnableInClassList("hidden", string.IsNullOrEmpty(caption));
        detailNote.text = note;
        detailNote.EnableInClassList("hidden", string.IsNullOrEmpty(note));
    }

    private void AddFact(string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("fact-row");
        row.Add(NewLabel(label, "fact-row__label"));
        row.Add(NewLabel(value, "fact-row__value"));
        detailFacts.Add(row);
    }

    private void SetButton(bool visible, string label, bool enabled)
    {
        detailButton.EnableInClassList("hidden", !visible);
        detailButtonLabel.text = label;
        detailButton.SetEnabled(enabled);
    }

    private void OnDetailButton()
    {
        if (busy) return;
        if (tab == Tab.Modes)
        {
            GameMode mode = GameModeCatalog.Get(selectedMode);
            if (mode.CanCreate) lobby.Create(GameModeCatalog.RulesFor(mode.id));
        }
        else if (tab == Tab.Training)
        {
            mainMenu.StartShootingRange();
        }
    }
}
