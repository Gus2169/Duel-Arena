using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'écran Jouer du menu (2026-10-17, d'après la maquette de l'utilisateur) : des onglets, une grille de
/// cartes, et à droite le détail de la carte choisie avec son action. Posé sur MainMenuUI, à côté de
/// MainMenuController, qui lui montre l'écran.
///
/// - MODES DE JEU : les cartes de GameModeCatalog. Une carte jouable ouvre un salon privé avec les
///   réglages de son mode (LobbyController.Create) ; les autres disent où le jeu va, verrouillées : voilées
///   de sombre sous un grand cadenas, elles ne se choisissent pas (2026-10-27, demandé par l'utilisateur :
///   on les voyait trop, on croyait pouvoir les choisir).
/// - SALONS : rejoindre un salon par son code, ou une partie du réseau local (MainMenuController).
/// - ENTRAÎNEMENT (demandé par l'utilisateur : le stand de tir y vit) : une carte, le stand de tir et la
///   Fosse, qui sont dans la même scène, derrière une porte l'un de l'autre (MainMenuController.
///   StartShootingRange). Deux cartes y menaient au même endroit ; depuis le 2026-10-27, une seule.
/// - CLASSEMENT : verrouillé, il viendra avec le classé, après la sortie (GDD § 14).
///
/// Les images des cartes : l'arène (MenuArtwork) et, devant, le robot dans une pose et une couleur propres à
/// chaque mode (MenuThumbnails, réglé par `modeArt`).
/// </summary>
public class PlayScreenController : MonoBehaviour
{
    /// <summary>Le robot d'une carte de mode : sa pose, sa couleur (rang dans la palette), son angle, et un
    /// second robot en face pour un duel.</summary>
    [Serializable]
    public struct ModeArt
    {
        public GameModeId mode;
        public AnimationClip pose;
        public float time;
        [Tooltip("Rang de la couleur dans la palette des robots (-1 : silhouette sans couleur).")]
        public int paint;
        public float yaw;
        public bool duo;
        public AnimationClip duoPose;
        public float duoTime;
        public int duoPaint;
    }

    private enum Tab { Modes, Lobbies, Training }

    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;
    [SerializeField] private LobbyController lobby;
    [SerializeField] private MenuThumbnails thumbnails;
    [SerializeField] private CharacterPreviewController characterPreview;
    [SerializeField] private ModeArt[] modeArt = Array.Empty<ModeArt>();

    private VisualElement root;
    private Button tabModes, tabLobbies, tabTraining, tabRanking;
    private VisualElement pageModes, pageLobbies, pageTraining;
    private VisualElement modeGrid, trainingGrid;
    private VisualElement detailIcon, detailImage, detailFacts;
    private Label detailTitle, detailText, detailButtonLabel;
    private Button detailButton;
    private string detailIconClass;

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
        if (characterPreview == null) characterPreview = GetComponent<CharacterPreviewController>();
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
        detailIcon = root.Q<VisualElement>("playDetailIcon");
        detailTitle = root.Q<Label>("playDetailTitle");
        detailText = root.Q<Label>("playDetailText");
        detailImage = root.Q<VisualElement>("playDetailImage");
        detailFacts = root.Q<VisualElement>("playDetailFacts");
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
        RequestArt();
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
                bool locked = !mode.CanCreate;
                // Une rangée incomplète se termine par des places vides (Spacer) : la dernière carte n'en est pas le bout.
                Button card = NewCard(mode.title, mode.subtitle, mode.icon, GameModeCatalog.Badge(mode.availability),
                    mode.availability == GameModeAvailability.InClassic, locked, i == start + columns - 1);
                GameModeId id = mode.id;
                card.clicked += () => SelectMode(id);
                // Un mode verrouillé ne se choisit pas : ni survol, ni clic.
                if (locked) card.SetEnabled(false);
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
        spacer.AddToClassList("mode-card");
        spacer.AddToClassList("mode-card--spacer");
        if (rowEnd) spacer.AddToClassList("mode-card--row-end");
        return spacer;
    }

    private void BuildTrainingCards()
    {
        trainingGrid.Clear();
        VisualElement row = NewRow(true);
        trainingCard = NewCard("STAND DE TIR ET FOSSE", "SEUL · HORS LIGNE", "ico-target", "", false, false, false);
        row.Add(trainingCard);
        row.Add(Spacer(true));
        trainingGrid.Add(row);
        // Une seule rangée : elle prend la moitié de la hauteur, comme deux grandes cartes.
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

    /// <summary>Une carte : l'image en fond, le robot devant, un voile sombre en bas, le titre ; un badge
    /// et un cadenas pour ce qui n'existe pas encore ; le liseré par-dessus tout.</summary>
    private static Button NewCard(string title, string subtitle, string icon, string badge, bool liveBadge, bool locked, bool rowEnd)
    {
        var card = new Button { focusable = false };
        card.AddToClassList("mode-card");
        card.AddToClassList("ui-select");
        if (rowEnd) card.AddToClassList("mode-card--row-end");
        if (locked) card.AddToClassList("mode-card--locked");

        card.Add(Part("mode-card__art"));
        card.Add(Part("mode-card__shade", new DuelArena.UI.GradientFill()));
        if (locked)
        {
            VisualElement veil = Part("mode-card__lock");
            veil.Add(Part("mode-card__lock-icon"));
            card.Add(veil);
        }
        VisualElement iconElement = Part("mode-card__icon");
        iconElement.AddToClassList(icon);
        card.Add(iconElement);
        if (!string.IsNullOrEmpty(badge))
        {
            var badgeLabel = new Label(badge) { pickingMode = PickingMode.Ignore };
            badgeLabel.AddToClassList("mode-card__badge");
            if (liveBadge) badgeLabel.AddToClassList("mode-card__badge--live");
            card.Add(badgeLabel);
        }
        VisualElement text = Part("mode-card__text");
        text.Add(NewLabel(title, "mode-card__title"));
        text.Add(NewLabel(subtitle, "mode-card__subtitle"));
        card.Add(text);
        card.Add(Part("mode-card__edge", new DuelArena.UI.SlantBox()));
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
        foreach ((GameModeId id, Button card) in modeCards) card.EnableInClassList("mode-card--selected", id == selectedMode);
        trainingCard?.AddToClassList("mode-card--selected");
    }

    // ------------------------------------------------------------------
    // Les images : l'arène derrière, le robot devant
    // ------------------------------------------------------------------

    private void RequestArt()
    {
        MenuArtwork artwork = Artwork;
        for (int i = 0; i < modeCards.Count; i++)
        {
            Button card = modeCards[i].card;
            VisualElement backdrop = card.Q<VisualElement>(className: "mode-card__art");
            SetBackground(backdrop, artwork != null ? artwork.arenaOverview : null);
            // Chaque carte cadre un autre coin de l'arène : les images de la maquette sont toutes différentes.
            if (backdrop != null)
            {
                backdrop.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Length.Percent(i % 3 * 50f));
                backdrop.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Length.Percent(i / 3 * 50f));
            }
            if (card.Q<VisualElement>(className: "mode-card__robot") != null) continue;
            ModeArt art = ArtFor(modeCards[i].id, out bool found);
            if (!found || thumbnails == null) continue;
            if (art.duo)
            {
                AddRobot(card, art.pose, art.time, art.paint, 35f, "mode-card__robot--left");
                AddRobot(card, art.duoPose != null ? art.duoPose : art.pose, art.duoTime, art.duoPaint, -35f, "mode-card__robot--right");
            }
            else
            {
                AddRobot(card, art.pose, art.time, art.paint, art.yaw, "mode-card__robot--center");
            }
        }
        if (trainingCard != null) SetBackground(trainingCard.Q<VisualElement>(className: "mode-card__art"), artwork != null ? artwork.rangeImage : null);
    }

    private ModeArt ArtFor(GameModeId id, out bool found)
    {
        foreach (ModeArt art in modeArt)
        {
            if (art.mode != id) continue;
            found = true;
            return art;
        }
        found = false;
        return default;
    }

    /// <summary>Le robot d'une carte, posé derrière le voile (il se fond dans le bas de la carte).</summary>
    private void AddRobot(Button card, AnimationClip pose, float time, int paint, float yaw, string placement)
    {
        VisualElement robot = Part("mode-card__robot");
        robot.AddToClassList(placement);
        // Juste après l'image de fond : sous le voile, le titre et le liseré.
        card.Insert(1, robot);
        var shot = new MenuThumbnails.RobotShot
        {
            paint = PaletteColor(paint),
            silhouette = paint < 0,
            pose = pose,
            time = time,
            yaw = yaw,
            framing = MenuThumbnails.Framing.FullBody,
            width = 300,
            height = 360,
        };
        thumbnails.Robot(shot, texture => SetBackground(robot, texture));
    }

    private Color32 PaletteColor(int index)
    {
        IReadOnlyList<Color32> palette = characterPreview != null ? characterPreview.Palette : Array.Empty<Color32>();
        if (palette.Count == 0) return new Color32(244, 190, 28, 255);
        return palette[Mathf.Clamp(index, 0, palette.Count - 1)];
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
        switch (tab)
        {
            case Tab.Modes:
            {
                GameMode mode = GameModeCatalog.Get(selectedMode);
                SetDetail(mode.icon, mode.title, mode.description, artwork != null ? artwork.arenaOverview : null);
                if (mode.CanCreate)
                {
                    MatchRules rules = GameModeCatalog.RulesFor(mode.id);
                    AddFact("PLACES", rules.capacity <= MatchRules.MinCapacity ? $"{rules.capacity}, jusqu'à {MatchRules.MaxCapacity} au salon" : rules.capacity.ToString());
                    AddFact("ROI DE LA COLLINE", rules.organization == DuelOrganization.KingOfTheHill ? "Oui" : "Non, au choix du salon");
                    AddFact("SPECTATEURS", rules.capacity <= DuelRoster.DuelistSlots ? "Aucun"
                        : rules.spectatorInterventions ? "Interviennent" : "Regardent");
                    AddFact("MANCHES", $"{rules.Format} ou BO3, au salon");
                    SetButton(true, "CRÉER UNE PARTIE", !busy);
                }
                else
                {
                    AddFact("DISPONIBLE", mode.availability == GameModeAvailability.AfterRelease ? "Après la sortie" : "Bientôt");
                    SetButton(true, GameModeCatalog.Badge(mode.availability), false);
                }
                break;
            }
            case Tab.Lobbies:
                SetDetail("ico-users", "SALONS PRIVÉS",
                    "Duel Arena se joue entre amis, dans des salons privés à code. Pour en ouvrir un, choisis un mode et crée la partie : le salon s'ouvre avec son code, à donner aux autres.",
                    artwork != null ? artwork.arenaOverview : null);
                AddFact("PLACES", $"{MatchRules.MinCapacity} à {MatchRules.MaxCapacity}");
                AddFact("RÉGLAGES", "Par l'hôte, au salon");
                AddFact("PARTIE LANCÉE", "Le code reste valable");
                SetButton(false, "", false);
                break;
            default:
            {
                SetDetail("ico-target", "STAND DE TIR ET FOSSE",
                    "Essaie chaque arme et ses accessoires sur des mannequins à 5, 10, 15, 25, 35 et 50 m, puis passe la porte à droite du poste de tir : la Fosse, le parcours chronométré, où des cibles se lèvent sur ton passage, hostiles à neutraliser, civils à épargner. B ouvre la fenêtre des armes, Échap la pause et les Options.",
                    artwork != null ? artwork.rangeImage : null);
                AddFact("JOUEURS", "Seul, hors ligne");
                AddFact("ARMES", "Toutes, avec leurs accessoires");
                AddFact("MANNEQUINS", "Tombent, puis se relèvent en 3 s");
                AddFact("LA FOSSE", "20 hostiles, 8 civils");
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

    private void SetDetail(string icon, string title, string text, Texture2D image)
    {
        if (detailIconClass != null) detailIcon.RemoveFromClassList(detailIconClass);
        detailIconClass = icon;
        if (icon != null) detailIcon.AddToClassList(icon);
        detailTitle.text = title;
        detailText.text = text;
        SetBackground(detailImage, image);
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
