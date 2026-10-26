using System.Collections.Generic;
using System.Globalization;
using DuelArena.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Le panneau des Options, dans le menu principal et dans le menu d'Échap en partie, refait le 2026-10-17
/// d'après la maquette de l'utilisateur : quatre onglets verticaux, Gameplay (sensibilité, axe vertical
/// inversé), Audio (volume général et musique, 2026-10-13), Vidéo (résolution, plein écran, synchronisation
/// verticale, plafond d'images) et Contrôles (remappage des touches) ; des curseurs avec leur valeur et des
/// sélecteurs « ‹ Non › » (OptionCycler). Chaque réglage s'applique aussitôt : pas de bouton Appliquer.
/// Posé sur le même GameObject que l'UIDocument.
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
        ["Melee"] = "Taser (corps à corps) / coup de poing",
        ["Interact"] = "Ramasser / prendre une arme lancée",
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

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly int[] frameRateCaps = { 0, 60, 120, 144, 240 };
    private readonly string[] frameRateLabels = { "Illimitée", "60", "120", "144", "240" };

    private VisualElement root;
    private Button[] tabs;
    private VisualElement[] pages;

    private Slider sensitivitySlider, masterVolumeSlider, musicVolumeSlider;
    private Label sensitivityValue, masterVolumeValue, musicVolumeValue;
    private OptionCycler invertYCycler, resolutionCycler, fullscreenCycler, vsyncCycler, frameRateCycler;
    private Resolution[] resolutions;

    private ScrollView bindScroll;
    private Button resetBindingsButton;

    private PlayerControls controls;
    private InputActionRebindingExtensions.RebindingOperation activeRebind;

    /// <summary>Un remappage attend une touche : Échap l'annule, il ne doit pas aussi fermer le
    /// menu d'Échap (PauseMenu) qui affiche ce panneau en partie.</summary>
    public static bool AnyRebinding { get; private set; }

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        controls = new PlayerControls();
        GameSettings.ApplyBindingOverrides(controls.asset);

        WireTabs();
        WireGameplay();
        WireAudio();
        WireGraphics();
        WireControls();
        BuildBindRows();
    }

    private void OnDisable()
    {
        activeRebind?.Dispose();
        activeRebind = null;
        AnyRebinding = false;
        controls?.Dispose();
        controls = null;
    }

    // ------------------------------------------------------------------
    // Onglets
    // ------------------------------------------------------------------

    private void WireTabs()
    {
        tabs = new[]
        {
            root.Q<Button>("tabGameplayButton"),
            root.Q<Button>("tabAudioButton"),
            root.Q<Button>("tabGraphicsButton"),
            root.Q<Button>("tabControlsButton"),
        };
        pages = new[]
        {
            root.Q<VisualElement>("pageGameplay"),
            root.Q<VisualElement>("pageAudio"),
            root.Q<VisualElement>("pageGraphics"),
            root.Q<VisualElement>("pageControls"),
        };
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i].clicked += () => ShowTab(index);
        }
    }

    private void ShowTab(int index)
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            pages[i].EnableInClassList("hidden", i != index);
            tabs[i].EnableInClassList("opt-tab--active", i == index);
        }
    }

    // ------------------------------------------------------------------
    // Gameplay : la souris
    // ------------------------------------------------------------------

    private void WireGameplay()
    {
        sensitivitySlider = root.Q<Slider>("sensitivitySlider");
        sensitivityValue = root.Q<Label>("sensitivityValue");
        invertYCycler = root.Q<OptionCycler>("invertYCycler");

        sensitivitySlider.SetValueWithoutNotify(GameSettings.SensitivityMultiplier);
        sensitivityValue.text = Sensitivity(sensitivitySlider.value);
        sensitivitySlider.RegisterValueChangedCallback(evt =>
        {
            GameSettings.SensitivityMultiplier = evt.newValue;
            sensitivityValue.text = Sensitivity(evt.newValue);
        });

        invertYCycler.SetValueWithoutNotify(GameSettings.InvertY ? 1 : 0);
        invertYCycler.RegisterValueChangedCallback(evt => GameSettings.InvertY = evt.newValue == 1);
    }

    /// <summary>La sensibilité telle qu'on la lit (« 1,25 »). Fonction pure.</summary>
    public static string Sensitivity(float multiplier) => multiplier.ToString("0.00", French);

    /// <summary>Un volume en pourcentage (« 80 »). Fonction pure.</summary>
    public static string Percent(float volume) => Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f).ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Audio (2026-10-13)
    // ------------------------------------------------------------------

    private void WireAudio()
    {
        masterVolumeSlider = root.Q<Slider>("masterVolumeSlider");
        musicVolumeSlider = root.Q<Slider>("musicVolumeSlider");
        masterVolumeValue = root.Q<Label>("masterVolumeValue");
        musicVolumeValue = root.Q<Label>("musicVolumeValue");

        masterVolumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        masterVolumeValue.text = Percent(masterVolumeSlider.value);
        masterVolumeSlider.RegisterValueChangedCallback(evt =>
        {
            GameSettings.MasterVolume = evt.newValue;
            masterVolumeValue.text = Percent(evt.newValue);
        });
        musicVolumeSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        musicVolumeValue.text = Percent(musicVolumeSlider.value);
        musicVolumeSlider.RegisterValueChangedCallback(evt =>
        {
            GameSettings.MusicVolume = evt.newValue;
            musicVolumeValue.text = Percent(evt.newValue);
        });
    }

    // ------------------------------------------------------------------
    // Vidéo
    // ------------------------------------------------------------------

    private void WireGraphics()
    {
        resolutionCycler = root.Q<OptionCycler>("resolutionCycler");
        fullscreenCycler = root.Q<OptionCycler>("fullscreenCycler");
        vsyncCycler = root.Q<OptionCycler>("vsyncCycler");
        frameRateCycler = root.Q<OptionCycler>("frameRateCycler");

        resolutions = Screen.resolutions;
        var choices = new List<string>();
        int currentIndex = 0;
        for (int i = 0; i < resolutions.Length; i++)
        {
            Resolution r = resolutions[i];
            choices.Add($"{r.width} × {r.height} · {r.refreshRateRatio.value:0} Hz");
            if (r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height) currentIndex = i;
        }
        resolutionCycler.Choices = choices;
        int savedIndex = GameSettings.ResolutionIndex;
        resolutionCycler.SetValueWithoutNotify(savedIndex >= 0 && savedIndex < choices.Count ? savedIndex : currentIndex);
        resolutionCycler.RegisterValueChangedCallback(evt =>
        {
            GameSettings.ResolutionIndex = evt.newValue;
            GameSettings.ApplyGraphics();
        });

        fullscreenCycler.SetValueWithoutNotify(GameSettings.Fullscreen ? 1 : 0);
        fullscreenCycler.RegisterValueChangedCallback(evt =>
        {
            GameSettings.Fullscreen = evt.newValue == 1;
            GameSettings.ApplyGraphics();
        });

        vsyncCycler.SetValueWithoutNotify(GameSettings.VSync ? 1 : 0);
        vsyncCycler.RegisterValueChangedCallback(evt =>
        {
            GameSettings.VSync = evt.newValue == 1;
            GameSettings.ApplyGraphics();
        });

        frameRateCycler.Choices = frameRateLabels;
        int capIndex = System.Array.IndexOf(frameRateCaps, GameSettings.FrameRateCap);
        frameRateCycler.SetValueWithoutNotify(capIndex >= 0 ? capIndex : 0);
        frameRateCycler.RegisterValueChangedCallback(evt =>
        {
            GameSettings.FrameRateCap = frameRateCaps[Mathf.Clamp(evt.newValue, 0, frameRateCaps.Length - 1)];
            GameSettings.ApplyGraphics();
        });
    }

    // ------------------------------------------------------------------
    // Contrôles
    // ------------------------------------------------------------------

    private void WireControls()
    {
        bindScroll = root.Q<ScrollView>("bindRowScroll");
        resetBindingsButton = root.Q<Button>("resetBindingsButton");

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
        AnyRebinding = true;

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
        // Libéré à l'image suivante : la touche Échap qui vient d'annuler est encore « pressée
        // pendant cette image » quand le menu d'Échap la lit.
        StartCoroutine(ClearRebindingNextFrame());
        button.RemoveFromClassList("bind-button--listening");
        button.text = action.GetBindingDisplayString(bindingIndex);
        GameSettings.SaveBindingOverrides(controls.asset);
    }

    private System.Collections.IEnumerator ClearRebindingNextFrame()
    {
        yield return null;
        if (activeRebind == null) AnyRebinding = false;
    }
}
