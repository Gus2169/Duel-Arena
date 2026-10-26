using System.Collections.Generic;
using DuelArena.UI;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'écran « Personnalisation » du menu principal (`screenCustomize`), refait le 2026-10-17 d'après les
/// maquettes de l'utilisateur : deux onglets, ROBOT et ARMES. Ce composant tient les onglets et le ROBOT ;
/// l'onglet ARMES est WeaponCustomizationController.
///
/// ROBOT : à gauche les rubriques, au milieu le robot qui tourne (CharacterPreviewController : sans arme,
/// en attente à mains nues), à droite les choix de la rubrique ouverte et leur fiche. « Rempli avec ce qu'on
/// a » (l'utilisateur) : le châssis (le CT-01, le prototype des premiers essais, le Y Bot, ou l'OS-02, 2026-10-26 ;
/// le gabarit et la surface touchable sont communs à tous, bible § 5) et la couleur. Marquages et accessoires sont
/// montrés verrouillés ; les effets ne viendront pas (GDD § 14,
/// 2026-10-25 : leur rubrique est retirée). Pas de bouton Appliquer : un choix s'enregistre au clic.
///
/// La couleur et le châssis partent dans la demande de connexion (GameSettings.PreferredColorIndex et
/// PreferredChassis, SessionFlow) ; en partie, le serveur donne la couleur si aucun autre joueur ne la porte,
/// sinon la première libre (PlayerPaint), et le châssis tel quel (PlayerChassis). La palette
/// est celle de PlayerPaint, lue sur le préfab joueur, jamais recopiée.
///
/// Posé sur le même GameObject que l'UIDocument (voir MainMenuController).
/// </summary>
public class ArmoryController : MonoBehaviour
{
    [Tooltip("Le robot tournant de cet écran : reçoit la couleur en temps réel à chaque choix.")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("L'onglet ARMES (modèle, finition et accessoires de chaque arme).")]
    [SerializeField] private WeaponCustomizationController weaponCustomization;

    [SerializeField] private MainMenuController mainMenu;

    [Tooltip("Les vignettes du robot dans chaque couleur.")]
    [SerializeField] private MenuThumbnails thumbnails;

    [SerializeField] private UIDocument document;

    private enum Category { Chassis, Colors, Markings, Accessories }

    private enum Tab { Robot, Weapons }

    private static readonly (Category category, string title, string icon, bool available)[] Categories =
    {
        (Category.Chassis, "CHÂSSIS", "ico-robot", true),
        (Category.Colors, "COULEURS", "ico-palette", true),
        (Category.Markings, "MARQUAGES", "ico-tag", false),
        (Category.Accessories, "ACCESSOIRES", "ico-gear", false),
    };

    private const int GridColumns = 3;

    private Button tabRobot, tabWeapons;
    private VisualElement pageRobot, pageWeapons, hintsRobot, hintsWeapons;
    private VisualElement categoryList, grid;
    private Label gridTitle, infoName, infoTitle, infoText;
    private readonly List<(Category category, Button button)> categoryButtons = new();
    private readonly List<(int index, Button tile)> tiles = new();

    private Tab tab = Tab.Robot;
    private Category category = Category.Colors;
    private bool shown;
    private VisualElement wiredScreen;   // l'écran sur lequel les rubriques sont construites (voir OnEnable)

    private void OnEnable() => Wire();

    /// <summary>Retrouve les éléments et construit les rubriques, une fois par arbre. Appelé aussi par OnShown,
    /// qui peut venir avant l'activation de ce composant.</summary>
    private void Wire()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        VisualElement root = document.rootVisualElement;

        tabRobot = root.Q<Button>("customTabRobot");
        tabWeapons = root.Q<Button>("customTabWeapons");
        pageRobot = root.Q<VisualElement>("customPageRobot");
        pageWeapons = root.Q<VisualElement>("customPageWeapons");
        hintsRobot = root.Q<VisualElement>("customHintsRobot");
        hintsWeapons = root.Q<VisualElement>("customHintsWeapons");
        categoryList = root.Q<VisualElement>("robotCategories");
        grid = root.Q<VisualElement>("robotGrid");
        gridTitle = root.Q<Label>("robotGridTitle");
        infoName = root.Q<Label>("robotInfoName");
        infoTitle = root.Q<Label>("robotInfoTitle");
        infoText = root.Q<Label>("robotInfoText");

        // Le châssis et la couleur du robot montrés dès le départ (le robot se construit à son premier affichage).
        characterPreview?.SetChassis(GameSettings.PreferredChassis);
        if (characterPreview != null && characterPreview.Palette.Count > 0) characterPreview.SetPaint(ShownColor);

        // Une seule construction par arbre (voir PlayScreenController.OnEnable).
        VisualElement screen = root.Q<VisualElement>("screenCustomize");
        if (screen == wiredScreen) return;
        wiredScreen = screen;
        tabRobot.clicked += () => ShowTab(Tab.Robot);
        tabWeapons.clicked += () => ShowTab(Tab.Weapons);
        BuildCategories();
    }

    /// <summary>Appelé par MainMenuController quand l'écran s'affiche : l'onglet ROBOT d'abord.</summary>
    public void OnShown()
    {
        Wire();
        shown = true;
        ShowTab(Tab.Robot);
    }

    /// <summary>Appelé quand on quitte l'écran : le plateau d'aperçu se coupe.</summary>
    public void OnHidden()
    {
        if (!shown) return;
        shown = false;
        weaponCustomization?.Hide();
        characterPreview?.Hide();
    }

    /// <summary>Le pseudo a changé (la carte du joueur) : la fiche du robot le reprend.</summary>
    public void RefreshPlayerName()
    {
        if (infoName == null) return;
        string name = PlayerMatchState.SanitizeName(GameSettings.PlayerName);
        infoName.text = string.IsNullOrEmpty(name) ? "SANS PSEUDO" : name.ToUpperInvariant();
    }

    private void ShowTab(Tab value)
    {
        tab = value;
        tabRobot.EnableInClassList("da-tab--active", tab == Tab.Robot);
        tabWeapons.EnableInClassList("da-tab--active", tab == Tab.Weapons);
        pageRobot.EnableInClassList("hidden", tab != Tab.Robot);
        pageWeapons.EnableInClassList("hidden", tab != Tab.Weapons);
        hintsRobot.EnableInClassList("hidden", tab != Tab.Robot);
        hintsWeapons.EnableInClassList("hidden", tab != Tab.Weapons);

        if (tab == Tab.Robot)
        {
            weaponCustomization?.Hide();
            characterPreview?.Show();
            SelectCategory(category);
        }
        else
        {
            weaponCustomization?.Open();
        }
    }

    // ------------------------------------------------------------------
    // Les rubriques
    // ------------------------------------------------------------------

    private void BuildCategories()
    {
        categoryList.Clear();
        categoryButtons.Clear();
        foreach ((Category value, string title, string icon, bool available) in Categories)
        {
            var button = new Button { focusable = false };
            button.AddToClassList("da-btn");
            button.AddToClassList("side-btn");
            button.AddToClassList("ui-select");
            button.Add(new SlantBox { pickingMode = PickingMode.Ignore });
            button[0].AddToClassList("slant-bg");
            var iconElement = new VisualElement { pickingMode = PickingMode.Ignore };
            iconElement.AddToClassList("side-btn__icon");
            iconElement.AddToClassList(icon);
            button.Add(iconElement);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("side-btn__text");
            text.Add(NewLabel(title, "side-btn__title"));
            if (!available) text.Add(NewLabel("BIENTÔT", "side-btn__subtitle"));
            button.Add(text);
            if (!available)
            {
                var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore };
                lockIcon.AddToClassList("side-btn__lock");
                button.Add(lockIcon);
                button.SetEnabled(false);
            }
            Category chosen = value;
            button.clicked += () => SelectCategory(chosen);
            categoryList.Add(button);
            categoryButtons.Add((value, button));
        }
    }

    private void SelectCategory(Category value)
    {
        category = value;
        foreach ((Category c, Button button) in categoryButtons) button.EnableInClassList("side-btn--active", c == category);
        BuildGrid();
        RefreshInfo();
    }

    // ------------------------------------------------------------------
    // Les choix : le châssis, les couleurs
    // ------------------------------------------------------------------

    private IReadOnlyList<Color32> Palette => characterPreview != null ? characterPreview.Palette : System.Array.Empty<Color32>();

    /// <summary>La couleur montrée : celle choisie, sinon la première de la palette (celle du premier
    /// joueur arrivé).</summary>
    private Color32 ShownColor
    {
        get
        {
            IReadOnlyList<Color32> palette = Palette;
            if (palette.Count == 0) return new Color32(244, 190, 28, 255);
            int index = GameSettings.PreferredColorIndex;
            return palette[index >= 0 && index < palette.Count ? index : 0];
        }
    }

    private void BuildGrid()
    {
        grid.Clear();
        tiles.Clear();
        var entries = new List<(int index, string name, Color32 paint)>();
        if (category == Category.Chassis)
        {
            gridTitle.text = "CHÂSSIS";
            for (int i = 0; i < RobotChassis.Count; i++) entries.Add((i, RobotChassis.NameOf((ChassisId)i), ShownColor));
        }
        else
        {
            gridTitle.text = "COULEURS";
            IReadOnlyList<Color32> palette = Palette;
            for (int i = 0; i < palette.Count; i++) entries.Add((i, PaletteNames.NameOf(palette[i]).ToUpperInvariant(), palette[i]));
        }

        for (int start = 0; start < entries.Count; start += GridColumns)
        {
            var row = new VisualElement();
            row.AddToClassList("grid-row");
            for (int column = 0; column < GridColumns; column++)
            {
                int i = start + column;
                bool rowEnd = column == GridColumns - 1;
                if (i >= entries.Count)
                {
                    // Une place vide garde la largeur des tuiles de la rangée.
                    var placeholder = new VisualElement();
                    placeholder.AddToClassList("robot-tile");
                    placeholder.AddToClassList("robot-tile--placeholder");
                    if (rowEnd) placeholder.AddToClassList("robot-tile--row-end");
                    row.Add(placeholder);
                    continue;
                }
                (int index, string name, Color32 paint) = entries[i];
                ChassisId body = category == Category.Chassis ? (ChassisId)index : GameSettings.PreferredChassis;
                Button tile = NewTile(name, paint, body, rowEnd);
                int chosen = index;
                tile.clicked += () => SelectTile(chosen);
                row.Add(tile);
                tiles.Add((index, tile));
            }
            grid.Add(row);
        }
        RefreshTiles();
    }

    private Button NewTile(string name, Color32 paint, ChassisId chassis, bool rowEnd)
    {
        var tile = new Button { focusable = false };
        tile.AddToClassList("robot-tile");
        tile.AddToClassList("ui-select");
        if (rowEnd) tile.AddToClassList("robot-tile--row-end");
        var art = new VisualElement { pickingMode = PickingMode.Ignore };
        art.AddToClassList("robot-tile__art");
        tile.Add(art);
        tile.Add(NewLabel(name, "robot-tile__name"));
        var check = new VisualElement { pickingMode = PickingMode.Ignore };
        check.AddToClassList("robot-tile__check");
        var checkIcon = new VisualElement { pickingMode = PickingMode.Ignore };
        checkIcon.AddToClassList("robot-tile__check-icon");
        check.Add(checkIcon);
        tile.Add(check);
        var edge = new SlantBox { pickingMode = PickingMode.Ignore };
        edge.AddToClassList("robot-tile__edge");
        tile.Add(edge);

        if (thumbnails != null)
        {
            var shot = new MenuThumbnails.RobotShot
            {
                paint = paint,
                chassis = chassis,
                time = 0.4f,
                yaw = -22f,
                framing = MenuThumbnails.Framing.FullBody,
                width = 220,
                height = 300,
            };
            thumbnails.Robot(shot, texture =>
            {
                if (texture is RenderTexture rt) art.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            });
        }
        return tile;
    }

    private void SelectTile(int index)
    {
        if (category == Category.Chassis)
        {
            GameSettings.PreferredChassis = (ChassisId)index;
            characterPreview?.SetChassis(GameSettings.PreferredChassis);
        }
        else if (category == Category.Colors)
        {
            GameSettings.PreferredColorIndex = index;
            characterPreview?.SetPaint(ShownColor);
            mainMenu?.RefreshPlayerCards();
            // Le châssis montre le robot dans la couleur choisie : sa vignette se refera à l'ouverture.
        }
        RefreshTiles();
        RefreshInfo();
    }

    private void RefreshTiles()
    {
        int selected = category == Category.Chassis ? (int)GameSettings.PreferredChassis : GameSettings.PreferredColorIndex;
        foreach ((int index, Button tile) in tiles)
        {
            bool on = index == selected;
            tile.EnableInClassList("robot-tile--selected", on);
            VisualElement check = tile.Q<VisualElement>(className: "robot-tile__check");
            check?.EnableInClassList("hidden", !on);
        }
    }

    private void RefreshInfo()
    {
        RefreshPlayerName();
        if (category == Category.Chassis)
        {
            ChassisId chassis = GameSettings.PreferredChassis;
            infoTitle.text = RobotChassis.NameOf(chassis);
            infoText.text = chassis switch
            {
                ChassisId.Prototype => "Le robot des premiers essais, gardé au hangar. Même gabarit et même surface touchable que les autres : seule l'allure change.",
                ChassisId.OS02 => "L'Ossature : un endosquelette d'acier, vérins à nu, crâne de métal, plaques à ta couleur. Même gabarit et même surface touchable que les autres, et autant de silhouette à viser.",
                _ => "Le mannequin d'essai blindé du programme : casque à visière, plaques à ta couleur. Tous les châssis ont le même gabarit et la même surface touchable : c'est la peinture qui distingue chaque combattant.",
            };
            return;
        }

        IReadOnlyList<Color32> palette = Palette;
        int index = GameSettings.PreferredColorIndex;
        bool chosen = index >= 0 && index < palette.Count;
        infoTitle.text = chosen ? PaletteNames.NameOf(palette[index]).ToUpperInvariant() : "AUCUNE COULEUR";
        infoText.text = chosen
            ? "En partie, ton robot porte cette couleur si personne ne l'a déjà ; sinon la première couleur libre de la palette. Deux robots n'ont jamais la même."
            : "Aucune couleur choisie : en partie, ton robot prend la première couleur libre de la palette.";
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }
}
