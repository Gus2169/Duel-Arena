using System.Collections.Generic;
using System.Globalization;
using DuelArena.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Le panneau des Options, dans le menu principal, dans le menu d'Échap en partie et dans celui du stand de
/// tir, refait le 2026-10-17 d'après la maquette de l'utilisateur : quatre onglets verticaux, Gameplay
/// (sensibilité, axe vertical inversé), Audio (volume général et musique, 2026-10-13), Vidéo (résolution,
/// plein écran, synchronisation verticale, plafond d'images, rendu) et Contrôles (remappage des touches) ;
/// des curseurs avec leur valeur, des sélecteurs « ‹ › » (OptionCycler) et, pour ce qui se dit oui ou non,
/// des cases à cocher (2026-10-27). Posé sur le même GameObject que l'UIDocument.
///
/// Depuis le 2026-10-27 (demandé par l'utilisateur), rien ne s'applique avant le bouton Appliquer : on règle,
/// puis on applique tout d'un coup (ou on annule). La résolution ne change plus à chaque cran du sélecteur.
/// Ce qui attend d'être appliqué se lit en comparant le panneau aux réglages enregistrés (GameSettings), sans
/// copie à tenir à jour. En sortant du panneau avec des réglages en attente, une fenêtre propose de les
/// appliquer ou de les ignorer (RequestLeave). La liste des résolutions ne garde que les tailles courantes,
/// une fois chacune, au format de l'écran (CommonResolutions) : Screen.resolutions donne chaque taille une
/// fois par fréquence d'écran.
///
/// Le remappage passe par l'API de rebinding interactif de l'Input System
/// (InputActionRebindingExtensions.PerformInteractiveRebinding), appliqué à une instance LOCALE
/// de PlayerControls créée juste pour cet écran — jamais activée (Enable()), puisqu'on ne lit
/// aucune valeur dessus, on ne fait qu'inspecter/modifier ses bindings. Une fois appliqués, les remappages
/// sont sauvegardés en JSON (GameSettings.SaveBindingOverrides) ; c'est PlayerInputReader qui les recharge
/// sur SA PROPRE instance (chaque `new PlayerControls()` déserialise une copie indépendante de l'asset
/// généré), au lancement comme en pleine partie.
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
        ["Aim"] = "Viser / garde ou visée d'un lancer du spectateur",
        ["Fire"] = "Tirer / coups de poing et lancer du spectateur",
        ["Reload"] = "Recharger",
        ["Melee"] = "Taser (corps à corps) / coup de poing",
        ["Interact"] = "Ramasser / prendre une arme lancée",
        ["SwapItem"] = "Spectateur : poings / objet tenu",
    };

    private static readonly Dictionary<string, string> MovePartLabels = new()
    {
        ["up"] = "Déplacement : avancer",
        ["down"] = "Déplacement : reculer",
        ["left"] = "Déplacement : aller à gauche",
        ["right"] = "Déplacement : aller à droite",
    };

    /// <summary>Les tailles d'écran les plus courantes (16:9, 16:10, 21:9). Les autres tailles que donne
    /// l'écran ne sont pas proposées (2026-10-27, demandé par l'utilisateur).</summary>
    public static readonly Vector2Int[] CommonSizes =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
        new(1280, 800), new(1440, 900), new(1680, 1050), new(1920, 1200), new(2560, 1600),
        new(2560, 1080), new(3440, 1440),
    };

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly int[] frameRateCaps = { 0, 60, 120, 144, 240 };
    private readonly string[] frameRateLabels = { "Illimitée", "60", "120", "144", "240" };

    private VisualElement root;
    private Button[] tabs;
    private VisualElement[] pages;

    private Slider sensitivitySlider, masterVolumeSlider, musicVolumeSlider;
    private Label sensitivityValue, masterVolumeValue, musicVolumeValue;
    private Toggle invertYToggle, fullscreenToggle, vsyncToggle;
    private OptionCycler resolutionCycler, frameRateCycler, renderStyleCycler;
    private List<Vector2Int> resolutions = new();

    private ScrollView bindScroll;
    private Button resetBindingsButton;

    private Button applyButton, revertButton, leaveApplyButton, leaveDiscardButton;
    private Label pendingLabel;
    private VisualElement leaveModal;
    private System.Action pendingLeave;

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
        WireApply();
        LoadFromSettings();
    }

    private void OnDisable()
    {
        activeRebind?.Dispose();
        activeRebind = null;
        AnyRebinding = false;
        pendingLeave = null;
        controls?.Dispose();
        controls = null;
    }

    /// <summary>Le panneau s'affiche : il montre les réglages en vigueur (ce qu'on avait réglé sans
    /// l'appliquer a été appliqué ou ignoré en sortant).</summary>
    public void OnShown()
    {
        if (root == null) return;
        LoadFromSettings();
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
        invertYToggle = root.Q<Toggle>("invertYToggle");

        sensitivitySlider.RegisterValueChangedCallback(evt =>
        {
            sensitivityValue.text = Sensitivity(evt.newValue);
            RefreshPending();
        });
        invertYToggle.RegisterValueChangedCallback(_ => RefreshPending());
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

        masterVolumeSlider.RegisterValueChangedCallback(evt =>
        {
            masterVolumeValue.text = Percent(evt.newValue);
            RefreshPending();
        });
        musicVolumeSlider.RegisterValueChangedCallback(evt =>
        {
            musicVolumeValue.text = Percent(evt.newValue);
            RefreshPending();
        });
    }

    // ------------------------------------------------------------------
    // Vidéo
    // ------------------------------------------------------------------

    private void WireGraphics()
    {
        resolutionCycler = root.Q<OptionCycler>("resolutionCycler");
        fullscreenToggle = root.Q<Toggle>("fullscreenToggle");
        vsyncToggle = root.Q<Toggle>("vsyncToggle");
        frameRateCycler = root.Q<OptionCycler>("frameRateCycler");
        renderStyleCycler = root.Q<OptionCycler>("renderStyleCycler");

        var supported = new List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions) supported.Add(new Vector2Int(r.width, r.height));
        Resolution native = Screen.currentResolution;
        resolutions = CommonResolutions(supported, new Vector2Int(native.width, native.height));
        var choices = new List<string>(resolutions.Count);
        foreach (Vector2Int size in resolutions) choices.Add(ResolutionLabel(size));
        resolutionCycler.Choices = choices;
        frameRateCycler.Choices = frameRateLabels;

        resolutionCycler.RegisterValueChangedCallback(_ => RefreshPending());
        fullscreenToggle.RegisterValueChangedCallback(_ => RefreshPending());
        vsyncToggle.RegisterValueChangedCallback(_ => RefreshPending());
        frameRateCycler.RegisterValueChangedCallback(_ => RefreshPending());
        renderStyleCycler.RegisterValueChangedCallback(_ => RefreshPending());
    }

    /// <summary>« 1920 × 1080 ». Fonction pure.</summary>
    public static string ResolutionLabel(Vector2Int size) => $"{size.x} × {size.y}";

    /// <summary>
    /// Les résolutions proposées : les tailles courantes (CommonSizes) que l'écran sait afficher, au format de
    /// sa taille native (à 3 % près) et pas plus grandes qu'elle, une fois chacune, de la plus petite à la plus
    /// grande ; la taille native en fait toujours partie. Un écran à un format rare garde ses propres tailles
    /// à son format. Fonction pure, testée.
    /// </summary>
    public static List<Vector2Int> CommonResolutions(IEnumerable<Vector2Int> supported, Vector2Int native)
    {
        var distinct = new List<Vector2Int>();
        if (supported != null)
            foreach (Vector2Int size in supported)
                if (size.x > 0 && size.y > 0 && !distinct.Contains(size)) distinct.Add(size);
        if (native.x <= 0 || native.y <= 0)
        {
            native = Vector2Int.zero;
            foreach (Vector2Int size in distinct)
                if ((long)size.x * size.y > (long)native.x * native.y) native = size;
        }
        if (native.x <= 0 || native.y <= 0) return new List<Vector2Int>(System.Array.Empty<Vector2Int>());

        float aspect = (float)native.x / native.y;
        bool SameFormat(Vector2Int s) => Mathf.Abs((float)s.x / s.y - aspect) <= aspect * 0.03f;
        bool Fits(Vector2Int s) => s.x <= native.x && s.y <= native.y;

        var result = new List<Vector2Int>();
        foreach (Vector2Int size in CommonSizes)
            if (distinct.Contains(size) && SameFormat(size) && Fits(size) && !result.Contains(size)) result.Add(size);
        // Un format rare : ses propres tailles, plutôt qu'une liste vide.
        if (result.Count < 2)
            foreach (Vector2Int size in distinct)
                if (SameFormat(size) && Fits(size) && !result.Contains(size)) result.Add(size);
        if (!result.Contains(native)) result.Add(native);
        result.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return result;
    }

    /// <summary>Le rang, dans `choices`, de la résolution enregistrée ; sans elle (ou si l'écran ne la propose
    /// plus), celle de la fenêtre, puis la plus grande. Fonction pure, testée.</summary>
    public static int ResolutionIndexOf(IReadOnlyList<Vector2Int> choices, Vector2Int saved, Vector2Int window)
    {
        if (choices == null || choices.Count == 0) return 0;
        for (int i = 0; i < choices.Count; i++) if (choices[i] == saved) return i;
        for (int i = 0; i < choices.Count; i++) if (choices[i] == window) return i;
        return choices.Count - 1;
    }

    // ------------------------------------------------------------------
    // Contrôles
    // ------------------------------------------------------------------

    private void WireControls()
    {
        bindScroll = root.Q<ScrollView>("bindRowScroll");
        resetBindingsButton = root.Q<Button>("resetBindingsButton");

        // Les touches d'origine, dans le panneau : elles ne valent qu'une fois appliquées, comme le reste.
        resetBindingsButton.clicked += () =>
        {
            controls.asset.RemoveAllBindingOverrides();
            BuildBindRows();
            RefreshPending();
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
        button.text = PlayerInputReader.KeyLabel(action.GetBindingDisplayString(bindingIndex));
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
        button.text = PlayerInputReader.KeyLabel(action.GetBindingDisplayString(bindingIndex));
        RefreshPending();
    }

    private System.Collections.IEnumerator ClearRebindingNextFrame()
    {
        yield return null;
        if (activeRebind == null) AnyRebinding = false;
    }

    // ------------------------------------------------------------------
    // Appliquer, annuler, sortir (2026-10-27)
    // ------------------------------------------------------------------

    private void WireApply()
    {
        applyButton = root.Q<Button>("optionsApplyButton");
        revertButton = root.Q<Button>("optionsRevertButton");
        pendingLabel = root.Q<Label>("optionsPendingLabel");
        leaveModal = root.Q<VisualElement>("optionsLeaveModal");
        leaveApplyButton = root.Q<Button>("optionsLeaveApplyButton");
        leaveDiscardButton = root.Q<Button>("optionsLeaveDiscardButton");

        applyButton.clicked += Apply;
        revertButton.clicked += LoadFromSettings;
        leaveApplyButton.clicked += () => FinishLeave(apply: true);
        leaveDiscardButton.clicked += () => FinishLeave(apply: false);
    }

    /// <summary>Le panneau montre les réglages en vigueur : rien n'attend plus d'être appliqué.</summary>
    private void LoadFromSettings()
    {
        if (activeRebind != null) return; // un remappage écoute une touche : on ne refait pas ses lignes

        sensitivitySlider.SetValueWithoutNotify(GameSettings.SensitivityMultiplier);
        sensitivityValue.text = Sensitivity(sensitivitySlider.value);
        invertYToggle.SetValueWithoutNotify(GameSettings.InvertY);

        masterVolumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        masterVolumeValue.text = Percent(masterVolumeSlider.value);
        musicVolumeSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        musicVolumeValue.text = Percent(musicVolumeSlider.value);

        resolutionCycler.SetValueWithoutNotify(ResolutionIndexOf(resolutions, GameSettings.Resolution, new Vector2Int(Screen.width, Screen.height)));
        fullscreenToggle.SetValueWithoutNotify(GameSettings.Fullscreen);
        vsyncToggle.SetValueWithoutNotify(GameSettings.VSync);
        int capIndex = System.Array.IndexOf(frameRateCaps, GameSettings.FrameRateCap);
        frameRateCycler.SetValueWithoutNotify(capIndex >= 0 ? capIndex : 0);
        // Le rendu (low poly propre, cel-shading, les deux mêlés : l'ordre de RenderStyleMode).
        renderStyleCycler.SetValueWithoutNotify((int)GameSettings.PreferredRenderStyle);

        controls.asset.RemoveAllBindingOverrides();
        GameSettings.ApplyBindingOverrides(controls.asset);
        BuildBindRows();
        RefreshPending();
    }

    private Vector2Int ChosenResolution
        => resolutions.Count > 0 ? resolutions[Mathf.Clamp(resolutionCycler.value, 0, resolutions.Count - 1)] : Vector2Int.zero;

    private int ChosenFrameRateCap => frameRateCaps[Mathf.Clamp(frameRateCycler.value, 0, frameRateCaps.Length - 1)];

    private RenderStyleMode ChosenRenderStyle => (RenderStyleMode)Mathf.Clamp(renderStyleCycler.value, 0, 2);

    private bool ResolutionPending
        => resolutions.Count > 0
           && ChosenResolution != resolutions[ResolutionIndexOf(resolutions, GameSettings.Resolution, new Vector2Int(Screen.width, Screen.height))];

    private bool GraphicsPending
        => ResolutionPending
           || fullscreenToggle.value != GameSettings.Fullscreen
           || vsyncToggle.value != GameSettings.VSync
           || ChosenFrameRateCap != GameSettings.FrameRateCap;

    // Les remappages d'un asset qui n'en a aucun, tels que l'Input System les écrit : ce qu'on compare quand
    // rien n'est enregistré (lu une fois).
    private static string blankOverrides;

    private bool BindingsPending
    {
        get
        {
            if (controls == null) return false;
            string saved = GameSettings.SavedBindingOverridesJson;
            if (string.IsNullOrEmpty(saved))
            {
                if (blankOverrides == null)
                {
                    using var blank = new PlayerControls();
                    blankOverrides = blank.asset.SaveBindingOverridesAsJson();
                }
                saved = blankOverrides;
            }
            return controls.asset.SaveBindingOverridesAsJson() != saved;
        }
    }

    /// <summary>Des réglages ont-ils changé sans être appliqués ?</summary>
    public bool HasPendingChanges
    {
        get
        {
            if (root == null || sensitivitySlider == null) return false;
            return !Mathf.Approximately(sensitivitySlider.value, GameSettings.SensitivityMultiplier)
                   || invertYToggle.value != GameSettings.InvertY
                   || !Mathf.Approximately(masterVolumeSlider.value, GameSettings.MasterVolume)
                   || !Mathf.Approximately(musicVolumeSlider.value, GameSettings.MusicVolume)
                   || GraphicsPending
                   || ChosenRenderStyle != GameSettings.PreferredRenderStyle
                   || BindingsPending;
        }
    }

    private void RefreshPending()
    {
        if (applyButton == null) return;
        bool pending = HasPendingChanges;
        applyButton.SetEnabled(pending);
        revertButton.SetEnabled(pending);
        pendingLabel.text = pending ? "Des réglages ont changé : applique-les pour qu'ils valent." : "";
    }

    /// <summary>Applique tout ce que le panneau montre.</summary>
    public void Apply()
    {
        if (activeRebind != null) return;
        bool graphics = GraphicsPending;

        GameSettings.SensitivityMultiplier = sensitivitySlider.value;
        GameSettings.InvertY = invertYToggle.value;
        GameSettings.MasterVolume = masterVolumeSlider.value;
        GameSettings.MusicVolume = musicVolumeSlider.value;
        if (resolutions.Count > 0) GameSettings.Resolution = ChosenResolution;
        GameSettings.Fullscreen = fullscreenToggle.value;
        GameSettings.VSync = vsyncToggle.value;
        GameSettings.FrameRateCap = ChosenFrameRateCap;
        if (graphics) GameSettings.ApplyGraphics();
        if (ChosenRenderStyle != GameSettings.PreferredRenderStyle) GameSettings.PreferredRenderStyle = ChosenRenderStyle;
        if (BindingsPending) GameSettings.SaveBindingOverrides(controls.asset);
        RefreshPending();
    }

    /// <summary>
    /// Quitter le panneau (le bouton Retour, Échap) : sans réglage en attente, `leave` tout de suite ; sinon
    /// la fenêtre qui propose de les appliquer ou de les ignorer, et `leave` une fois le choix fait. Pendant
    /// un remappage, rien (Échap l'annule).
    /// </summary>
    public void RequestLeave(System.Action leave)
    {
        if (AnyRebinding) return;
        if (!HasPendingChanges)
        {
            leave?.Invoke();
            return;
        }
        pendingLeave = leave;
        leaveModal.RemoveFromClassList("hidden");
    }

    /// <summary>La fenêtre de sortie est-elle ouverte ? (Échap la referme : on reste dans les Options)</summary>
    public bool LeaveModalOpen => leaveModal != null && !leaveModal.ClassListContains("hidden");

    /// <summary>Referme la fenêtre de sortie sans quitter le panneau. Rend vrai si elle était ouverte.</summary>
    public bool CloseLeaveModal()
    {
        if (!LeaveModalOpen) return false;
        leaveModal.AddToClassList("hidden");
        pendingLeave = null;
        return true;
    }

    private void FinishLeave(bool apply)
    {
        System.Action leave = pendingLeave;
        pendingLeave = null;
        leaveModal.AddToClassList("hidden");
        if (apply) Apply();
        else LoadFromSettings();
        leave?.Invoke();
    }
}
