using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Écran « Options » du menu principal : Graphismes (résolution, plein écran, vsync, plafond
/// d'images/seconde) et Commandes (sensibilité + remappage des touches). Posé sur le même
/// GameObject que l'UIDocument (voir MainMenuController).
///
/// Le remappage passe par l'API de rebinding interactif de l'Input System
/// (InputActionRebindingExtensions.PerformInteractiveRebinding), appliqué à une instance LOCALE
/// de PlayerControls créée juste pour cet écran — jamais activée (Enable()), puisqu'on ne lit
/// aucune valeur dessus, on ne fait qu'inspecter/modifier ses bindings. Une fois un remappage
/// validé, il est sauvegardé en JSON (GameSettings.SaveBindingOverrides) ; c'est
/// PlayerInputReader.Awake() qui le recharge sur SA PROPRE instance au moment de jouer (chaque
/// `new PlayerControls()` déserialise une copie indépendante de l'asset généré).
/// </summary>
public class OptionsController : MonoBehaviour
{
    [Tooltip("Modèle d'une ligne de remappage (KeyBindRow.uxml).")]
    [SerializeField] private VisualTreeAsset bindRowTemplate;

    [SerializeField] private UIDocument document;

    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["Sprint"] = "Sprint",
        ["Sneak"] = "Marche furtive",
        ["Crouch"] = "S'accroupir",
        ["Prone"] = "S'allonger",
        ["LeanLeft"] = "Pencher à gauche",
        ["LeanRight"] = "Pencher à droite",
        ["Jump"] = "Sauter / Franchir",
        ["Aim"] = "Viser",
        ["Fire"] = "Tirer",
        ["Reload"] = "Recharger",
        ["Melee"] = "Taser (corps à corps)",
        ["Weapon1"] = "Arme 1",
        ["Weapon2"] = "Arme 2",
        ["Weapon3"] = "Arme 3",
        ["Weapon4"] = "Arme 4",
    };

    private static readonly Dictionary<string, string> MovePartLabels = new()
    {
        ["up"] = "Déplacement : avancer",
        ["down"] = "Déplacement : reculer",
        ["left"] = "Déplacement : aller à gauche",
        ["right"] = "Déplacement : aller à droite",
    };

    private readonly int[] frameRateCaps = { 0, 60, 120, 144, 240 };
    private readonly List<string> frameRateLabels = new() { "Illimitée", "60", "120", "144", "240" };

    private VisualElement root;
    private Button tabGraphicsButton;
    private Button tabControlsButton;
    private VisualElement pageGraphics;
    private VisualElement pageControls;

    private DropdownField resolutionDropdown;
    private Toggle fullscreenToggle;
    private Toggle vsyncToggle;
    private DropdownField frameRateDropdown;
    private Resolution[] resolutions;

    private Slider sensitivitySlider;
    private ScrollView bindScroll;
    private Button resetBindingsButton;

    private PlayerControls controls;
    private InputActionRebindingExtensions.RebindingOperation activeRebind;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        controls = new PlayerControls();
        GameSettings.ApplyBindingOverrides(controls.asset);

        WireTabs();
        WireGraphics();
        WireControls();
        BuildBindRows();
    }

    private void OnDisable()
    {
        activeRebind?.Dispose();
        activeRebind = null;
        controls?.Dispose();
        controls = null;
    }

    // ------------------------------------------------------------------
    // Onglets
    // ------------------------------------------------------------------

    private void WireTabs()
    {
        tabGraphicsButton = root.Q<Button>("tabGraphicsButton");
        tabControlsButton = root.Q<Button>("tabControlsButton");
        pageGraphics = root.Q<VisualElement>("pageGraphics");
        pageControls = root.Q<VisualElement>("pageControls");

        tabGraphicsButton.clicked += () => ShowTab(true);
        tabControlsButton.clicked += () => ShowTab(false);
    }

    private void ShowTab(bool graphics)
    {
        pageGraphics.EnableInClassList("hidden", !graphics);
        pageControls.EnableInClassList("hidden", graphics);
        tabGraphicsButton.EnableInClassList("tab-button--active", graphics);
        tabControlsButton.EnableInClassList("tab-button--active", !graphics);
    }

    // ------------------------------------------------------------------
    // Graphismes
    // ------------------------------------------------------------------

    private void WireGraphics()
    {
        resolutionDropdown = root.Q<DropdownField>("resolutionDropdown");
        fullscreenToggle = root.Q<Toggle>("fullscreenToggle");
        vsyncToggle = root.Q<Toggle>("vsyncToggle");
        frameRateDropdown = root.Q<DropdownField>("frameRateDropdown");

        resolutions = Screen.resolutions;
        var choices = new List<string>();
        int currentIndex = 0;
        for (int i = 0; i < resolutions.Length; i++)
        {
            Resolution r = resolutions[i];
            choices.Add($"{r.width} x {r.height} @ {r.refreshRateRatio.value:0} Hz");
            if (r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height) currentIndex = i;
        }
        resolutionDropdown.choices = choices;

        int savedIndex = GameSettings.ResolutionIndex;
        resolutionDropdown.index = (savedIndex >= 0 && savedIndex < choices.Count) ? savedIndex : currentIndex;
        resolutionDropdown.RegisterValueChangedCallback(_ =>
        {
            GameSettings.ResolutionIndex = resolutionDropdown.index;
            GameSettings.ApplyGraphics();
        });

        fullscreenToggle.value = GameSettings.Fullscreen;
        fullscreenToggle.RegisterValueChangedCallback(evt =>
        {
            GameSettings.Fullscreen = evt.newValue;
            GameSettings.ApplyGraphics();
        });

        vsyncToggle.value = GameSettings.VSync;
        vsyncToggle.RegisterValueChangedCallback(evt =>
        {
            GameSettings.VSync = evt.newValue;
            GameSettings.ApplyGraphics();
        });

        frameRateDropdown.choices = frameRateLabels;
        int capIndex = System.Array.IndexOf(frameRateCaps, GameSettings.FrameRateCap);
        frameRateDropdown.index = capIndex >= 0 ? capIndex : 0;
        frameRateDropdown.RegisterValueChangedCallback(_ =>
        {
            GameSettings.FrameRateCap = frameRateCaps[frameRateDropdown.index];
            GameSettings.ApplyGraphics();
        });
    }

    // ------------------------------------------------------------------
    // Commandes
    // ------------------------------------------------------------------

    private void WireControls()
    {
        sensitivitySlider = root.Q<Slider>("sensitivitySlider");
        bindScroll = root.Q<ScrollView>("bindRowScroll");
        resetBindingsButton = root.Q<Button>("resetBindingsButton");

        sensitivitySlider.value = GameSettings.SensitivityMultiplier;
        sensitivitySlider.RegisterValueChangedCallback(evt => GameSettings.SensitivityMultiplier = evt.newValue);

        resetBindingsButton.clicked += () =>
        {
            GameSettings.ResetBindingOverrides(controls.asset);
            BuildBindRows();
        };
    }

    /// <summary>Reconstruit la liste des touches depuis l'action map "Gameplay" : générique, donc
    /// n'a besoin d'aucune mise à jour si une action s'ajoute — seule une entrée dans
    /// ActionLabels/MovePartLabels est nécessaire pour qu'elle apparaisse (sinon elle est
    /// volontairement absente de la liste, comme "Look", dont le remappage n'a pas de sens).</summary>
    private void BuildBindRows()
    {
        activeRebind?.Dispose();
        activeRebind = null;
        bindScroll.Clear();

        InputActionMap map = controls.asset.FindActionMap("Gameplay", throwIfNotFound: true);
        foreach (InputAction action in map.actions)
        {
            if (action.name == "Move")
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (!action.bindings[i].isPartOfComposite) continue;
                    if (!MovePartLabels.TryGetValue(action.bindings[i].name, out string partLabel)) continue;
                    AddBindRow(partLabel, action, i);
                }
                continue;
            }

            if (!ActionLabels.TryGetValue(action.name, out string label)) continue;
            AddBindRow(label, action, 0);
        }
    }

    private void AddBindRow(string label, InputAction action, int bindingIndex)
    {
        TemplateContainer row = bindRowTemplate.Instantiate();
        row.Q<Label>("bindLabel").text = label;
        Button button = row.Q<Button>("bindButton");
        button.text = action.GetBindingDisplayString(bindingIndex);
        button.clicked += () => StartRebind(action, bindingIndex, button);
        bindScroll.Add(row);
    }

    private void StartRebind(InputAction action, int bindingIndex, Button button)
    {
        if (activeRebind != null) return; // un seul remappage à la fois

        button.text = "Appuie sur une touche...";
        button.AddToClassList("bind-button--listening");

        activeRebind = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/position")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.05f)
            .OnComplete(_ => FinishRebind(action, bindingIndex, button))
            .OnCancel(_ => FinishRebind(action, bindingIndex, button))
            .Start();
    }

    private void FinishRebind(InputAction action, int bindingIndex, Button button)
    {
        activeRebind?.Dispose();
        activeRebind = null;
        button.RemoveFromClassList("bind-button--listening");
        button.text = action.GetBindingDisplayString(bindingIndex);
        GameSettings.SaveBindingOverrides(controls.asset);
    }
}
