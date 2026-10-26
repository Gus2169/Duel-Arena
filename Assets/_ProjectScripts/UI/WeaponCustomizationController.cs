using System;
using System.Collections.Generic;
using DuelArena.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

/// <summary>
/// L'onglet ARMES de Personnalisation (l'armurerie), posé sur le GameObject `MainMenuUI` à côté des autres
/// contrôleurs d'écran. Refait le 2026-10-17 sur le plan donné par l'utilisateur (« façon Rainbow Six ») :
/// - à gauche, la panoplie : les quatre armes, chacune avec sa vignette, son modèle et sa classe (touches 1
///   à 4) ;
/// - au milieu, l'arme en grand (on la fait tourner en glissant) et, en bas, ses emplacements : viseur,
///   poignée, laser, chargeur, puis le modèle et la finition. Un emplacement ouvre au-dessus la bande de ses
///   choix ; survoler un choix le prévisualise, cliquer l'équipe ;
/// - à droite, sa fiche : le modèle, la classe, les barres (WeaponStatSheet) et le mot de l'IA de supervision
///   (Bible § 9 : froide, exacte).
/// Pas de bouton Appliquer : équiper enregistre.
///
/// Prévisualiser n'enregistre rien : l'arme, sa fiche et ses barres montrent le choix survolé, et les barres
/// disent ce qu'il gagne ou perd. Équiper (clic ou F) l'enregistre : le modèle et la finition dans
/// GameSettings.WeaponLooks (GDD § 6, 2026-10-15), les accessoires dans GameSettings.WeaponAttachments
/// (2026-10-16). Ils partent avec la demande de connexion (SessionFlow) ; le serveur les borne et les publie
/// (PlayerWeaponLooks, PlayerWeaponAttachments).
///
/// Le modèle et la finition ne changent que l'apparence ; les accessoires changent l'arme (recul,
/// épaulement, chargeur). Tous sont ouverts à tous, et une arme ne propose que ce qu'elle prend
/// (WeaponAttachments.Accepts : pas de viseur sur le sniper, que les réflex et pas de poignée sur un
/// pistolet) : les autres emplacements sont grisés, avec la raison. L'aperçu monte les accessoires comme
/// l'arme tenue en jeu (AttachmentMount).
/// </summary>
public class WeaponCustomizationController : MonoBehaviour
{
    [Tooltip("Les 4 WeaponData de la panoplie, MÊME ORDRE que WeaponController.loadout sur le Player (touches 1 à 4).")]
    [SerializeField] private WeaponData[] loadout = new WeaponData[0];

    [SerializeField] private PreviewStage stage;
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    [Tooltip("Le robot de Personnalisation : la vie d'un robot, lue sur le préfab joueur (sur le même GameObject si vide).")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("Les vignettes des armes et des accessoires.")]
    [SerializeField] private MenuThumbnails thumbnails;

    [Tooltip("Vitesse d'écriture des messages de l'IA de supervision (caractères par seconde).")]
    [SerializeField] private float aiTypingSpeed = 140f;

    /// <summary>Les emplacements : les quatre rubriques d'accessoires, puis l'apparence.</summary>
    private enum Rubric { Sight, Grip, Laser, Magazine, Model, Finish }

    private static readonly Rubric[] AttachmentRubrics = { Rubric.Sight, Rubric.Grip, Rubric.Laser, Rubric.Magazine };
    private static readonly Rubric[] AllRubrics = { Rubric.Sight, Rubric.Grip, Rubric.Laser, Rubric.Magazine, Rubric.Model, Rubric.Finish };

    private sealed class Slot
    {
        public Rubric rubric;
        public Button button;
        public Label value;
    }

    private sealed class Choice
    {
        public int variant;
        public Button button;
    }

    private sealed class StatRow
    {
        public WeaponStatSheet.Stat stat;
        public VisualElement fill, delta;
        public Label value;
    }

    private VisualElement root;
    private VisualElement page;
    private Image viewport;
    private VisualElement weaponList, slotRow, slotStrip, statRowContainer, infoGrid;
    private Label modelLabel, classLabel, description;
    private Label stripTitle, stripCount, statsLegend, aiHeader, aiText;
    private ScrollView stripScroll;
    private Button stripPrev, stripNext;
    private Label rangeValue, reloadValue, fireModeValue, zonesValue;

    private readonly List<Button> weaponRows = new();
    private readonly List<Slot> slots = new();
    private readonly List<Choice> choices = new();
    private readonly List<StatRow> statRows = new();

    private int currentSlot;
    private Rubric selectedRubric = Rubric.Sight;
    private bool stripOpen;
    private int previewVariant = -1;   // le choix survolé dans la bande, -1 : on montre ce qui est équipé
    private bool open;

    private GameObject weaponInstance;
    private int instanceModel = -1;
    private Bounds weaponBounds;

    private string aiMessage = "";
    private float aiStartTime;
    private IVisualElementScheduledItem aiTyping;

    private VisualElement wiredScreen;   // l'onglet sur lequel la panoplie est construite (voir OnEnable)

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (stage == null) stage = GetComponent<PreviewStage>();
        if (characterPreview == null) characterPreview = GetComponent<CharacterPreviewController>();
        if (mainMenu == null) mainMenu = GetComponent<MainMenuController>();
        root = document.rootVisualElement;

        page = root.Q<VisualElement>("customPageWeapons");
        viewport = root.Q<Image>("gunsmithPreview");
        weaponList = root.Q<VisualElement>("weaponList");
        slotRow = root.Q<VisualElement>("slotRow");
        slotStrip = root.Q<VisualElement>("slotStrip");
        stripTitle = root.Q<Label>("slotStripTitle");
        stripCount = root.Q<Label>("slotStripCount");
        stripScroll = root.Q<ScrollView>("slotStripScroll");
        stripPrev = root.Q<Button>("slotStripPrev");
        stripNext = root.Q<Button>("slotStripNext");
        modelLabel = root.Q<Label>("weaponModelLabel");
        classLabel = root.Q<Label>("weaponClassLabel");
        description = root.Q<Label>("weaponDescription");
        statRowContainer = root.Q<VisualElement>("gunsmithStatRows");
        infoGrid = root.Q<VisualElement>("gunsmithInfoGrid");
        statsLegend = root.Q<Label>("gunsmithStatsLegend");
        aiHeader = root.Q<Label>("gunsmithAiHeader");
        aiText = root.Q<Label>("gunsmithAiText");

        GameSettings.WeaponLooksChanged += RefreshWeaponRows;
        GameSettings.WeaponAttachmentsChanged += RefreshWeaponRows;

        // Une seule construction par arbre (voir PlayScreenController.OnEnable).
        if (page == wiredScreen) return;
        wiredScreen = page;

        BuildWeaponRows();
        BuildSlots();
        BuildStatRows();
        BuildInfoGrid();

        stripPrev.clicked += () => PageStrip(-1);
        stripNext.clicked += () => PageStrip(1);
        stripScroll.horizontalScroller.valueChanged += _ => RefreshArrows();
        stripScroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => RefreshArrows());
        stripScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RefreshArrows());
        // La molette fait défiler la bande de côté (elle n'a pas de défilement vertical).
        stripScroll.RegisterCallback<WheelEvent>(evt =>
        {
            ScrollStripTo(stripScroll.scrollOffset.x + evt.delta.y * 60f, animate: false);
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        // Quitter la bande rend l'arme équipée.
        stripScroll.RegisterCallback<PointerLeaveEvent>(_ => Preview(-1));
    }

    private void OnDisable()
    {
        GameSettings.WeaponLooksChanged -= RefreshWeaponRows;
        GameSettings.WeaponAttachmentsChanged -= RefreshWeaponRows;
    }

    /// <summary>Ouvre l'onglet sur l'arme qu'on regardait (la première au premier passage) ; appelé par
    /// ArmoryController.</summary>
    public void Open()
    {
        // Un champ resté actif sur un autre écran prendrait les touches de celui-ci.
        root.panel?.focusController?.focusedElement?.Blur();
        open = true;
        RefreshWeaponRows();
        ShowSlot(Mathf.Clamp(currentSlot, 0, Mathf.Max(0, loadout.Length - 1)));
    }

    /// <summary>Quitte l'onglet : l'arme équipée revient, la bande se replie, le plateau se coupe.</summary>
    public void Hide()
    {
        if (!open) return;
        open = false;
        previewVariant = -1;
        SetStripOpen(false);
        stage.Hide();
    }

    /// <summary>Échap replie d'abord la bande des choix (MainMenuController, qui tient seul cette touche).
    /// Rend vrai si elle était ouverte.</summary>
    public bool CloseStrip()
    {
        if (!open || !stripOpen) return false;
        if (previewVariant >= 0) Preview(-1);
        SetStripOpen(false);
        return true;
    }

    // ------------------------------------------------------------------
    // Les touches (F, flèches, 1 à 4), lues seulement quand l'onglet est affiché
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!open || page == null || page.ClassListContains("hidden")) return;
        if (mainMenu != null && (mainMenu.ModalOpen || mainMenu.CurrentScreen != MainMenuController.CustomizeScreen)) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (stripOpen && (keyboard.fKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            Equip(previewVariant);
        if (keyboard.leftArrowKey.wasPressedThisFrame) StepPreview(-1);
        if (keyboard.rightArrowKey.wasPressedThisFrame) StepPreview(1);
        if (keyboard.upArrowKey.wasPressedThisFrame) StepRubric(-1);
        if (keyboard.downArrowKey.wasPressedThisFrame) StepRubric(1);

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) ShowSlot(0);
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) ShowSlot(1);
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) ShowSlot(2);
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) ShowSlot(3);
    }

    /// <summary>Le choix suivant ou précédent de la bande, à partir de celui qu'on regarde.</summary>
    private void StepPreview(int direction)
    {
        if (!stripOpen) SetStripOpen(true);
        if (choices.Count == 0) return;
        int from = previewVariant >= 0 ? previewVariant : Equipped(selectedRubric);
        int index = choices.FindIndex(c => c.variant == from);
        index = index < 0 ? 0 : Mathf.Clamp(index + direction, 0, choices.Count - 1);
        Preview(choices[index].variant);
        stripScroll.ScrollTo(choices[index].button);
    }

    private void StepRubric(int direction)
    {
        int count = AllRubrics.Length;
        int index = Array.IndexOf(AllRubrics, selectedRubric);
        for (int step = 1; step < count; step++)
        {
            Rubric next = AllRubrics[((index + direction * step) % count + count) % count];
            if (!Available(next)) continue;
            SelectRubric(next, openStrip: true);
            return;
        }
    }

    // ------------------------------------------------------------------
    // L'arme montrée
    // ------------------------------------------------------------------

    private WeaponData CurrentData => currentSlot >= 0 && currentSlot < loadout.Length ? loadout[currentSlot] : null;

    private AttachmentCatalog Catalog => CurrentData != null ? CurrentData.attachmentCatalog : null;

    private float MaxHealth => characterPreview != null ? characterPreview.RobotMaxHealth : 100f;

    /// <summary>Ce que montre l'écran : ce qui est équipé, ou le choix survolé à sa place.</summary>
    private WeaponLooks ShownLooks
    {
        get
        {
            WeaponLooks looks = GameSettings.WeaponLooks;
            if (previewVariant < 0) return looks;
            if (selectedRubric == Rubric.Model) return looks.WithModel(currentSlot, previewVariant);
            if (selectedRubric == Rubric.Finish) return looks.WithFinish(currentSlot, (WeaponFinish)previewVariant);
            return looks;
        }
    }

    private WeaponAttachments ShownAttachments
    {
        get
        {
            WeaponAttachments attachments = GameSettings.WeaponAttachments;
            if (previewVariant < 0 || !IsAttachment(selectedRubric)) return attachments;
            return attachments.WithVariant(currentSlot, Category(selectedRubric), previewVariant);
        }
    }

    private WeaponModel ModelOf(int model)
    {
        WeaponData data = CurrentData;
        GameObject prefab = data != null ? data.ModelPrefabFor(model) : null;
        return prefab != null ? prefab.GetComponent<WeaponModel>() : null;
    }

    private WeaponModel EquippedModel => ModelOf(GameSettings.WeaponLooks.Model(currentSlot));

    private WeaponModel ShownModel => ModelOf(ShownLooks.Model(currentSlot));

    private void ShowSlot(int slot)
    {
        if (slot < 0 || slot >= loadout.Length) return;
        currentSlot = slot;
        previewVariant = -1;
        if (weaponInstance != null) Destroy(weaponInstance);
        weaponInstance = null;
        ShowWeapon(GameSettings.WeaponLooks, GameSettings.WeaponAttachments, resetRotation: true);

        // L'emplacement ouvert reste le même d'une arme à l'autre, sauf une rubrique que la nouvelle ne
        // prend pas : la première qu'elle prend.
        if (!Available(selectedRubric)) selectedRubric = FirstAvailableRubric();

        RefreshWeaponRowStates();
        RefreshHeader();
        RefreshSlots();
        BuildStrip();
        RefreshStats();
        Say(WeaponStatSheet.Brief(CurrentData, MaxHealth));
    }

    /// <summary>Met l'aperçu dans cette apparence, avec ces accessoires : un autre modèle se reconstruit
    /// (d'autres pièces, d'autres points d'accroche, un autre cadrage), le reste se pose dessus.</summary>
    private void ShowWeapon(WeaponLooks looks, WeaponAttachments attachments, bool resetRotation)
    {
        int model = looks.Model(currentSlot);
        bool rebuilt = weaponInstance == null || model != instanceModel;
        if (rebuilt) BuildWeaponInstance(model);

        if (weaponInstance != null)
        {
            WeaponFinish finish = looks.Finish(currentSlot);
            if (weaponInstance.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
            AttachmentMount.Apply(weaponInstance.transform, CurrentData, attachments, currentSlot, finish);
        }
        if (rebuilt || resetRotation) stage.ShowWeapon(weaponBounds, viewport, resetRotation);
    }

    private void BuildWeaponInstance(int model)
    {
        if (weaponInstance != null) Destroy(weaponInstance);
        weaponInstance = null;
        instanceModel = model;
        weaponBounds = new Bounds(Vector3.zero, Vector3.one * 0.3f);

        WeaponData data = CurrentData;
        GameObject prefab = data != null ? data.ModelPrefabFor(model) : null;
        if (prefab == null || stage.WeaponPivot == null) return;

        weaponInstance = Instantiate(prefab, stage.WeaponPivot);
        weaponInstance.transform.localRotation = Quaternion.identity;
        CharacterPreviewController.SetLayerRecursively(weaponInstance, PreviewLayer.Index);

        // Centrée sur le pivot, l'arme tourne autour de son milieu, pas de sa poignée pistolet
        // (l'origine du préfab).
        Transform weapon = weaponInstance.transform;
        Bounds bounds = LocalBounds(weapon.GetComponentsInChildren<Renderer>(), weapon);
        weapon.localPosition = -bounds.center;

        // Le cadrage couvre l'arme ET la place de tous les accessoires qu'elle prend : il ne bouge
        // pas quand on en change, et un chargeur grande capacité ne sort pas du cadre.
        EncapsulateAttachments(ref bounds, weapon, data);
        weaponBounds = new Bounds(bounds.center + weapon.localPosition, bounds.size);
    }

    /// <summary>Ajoute aux bornes de l'arme (dans son repère) celles de chaque accessoire qu'elle prend,
    /// posé sur son point d'accroche : lues sur les préfabs, sans les créer.</summary>
    private static void EncapsulateAttachments(ref Bounds bounds, Transform weapon, WeaponData data)
    {
        WeaponModel model = weapon.GetComponent<WeaponModel>();
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        foreach (Rubric rubric in AttachmentRubrics)
        {
            AttachmentCategory category = Category(rubric);
            Transform socket = AttachmentMount.Socket(weapon, category);
            if (socket == null) continue;
            for (int variant = 1; variant < WeaponAttachments.VariantCount(category); variant++)
            {
                if (!WeaponAttachments.Accepts(data, category, variant)) continue;
                GameObject prefab = AttachmentMount.PrefabFor(catalog, model, category, variant);
                if (prefab == null) continue;
                Matrix4x4 toWeapon = weapon.worldToLocalMatrix * socket.localToWorldMatrix;
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Matrix4x4 m = toWeapon * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Bounds local = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                        bounds.Encapsulate(m.MultiplyPoint3x4(Corner(local, corner)));
                }
            }
        }
    }

    private static Vector3 Corner(Bounds b, int corner) => b.center + Vector3.Scale(b.extents, new Vector3(
        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));

    /// <summary>Bornes de `renderers` dans le repère de `space` (l'arme), quelle que soit la
    /// rotation actuelle du plateau : les bornes du monde (Renderer.bounds) d'une arme tournée
    /// sont une boîte gonflée et décentrée.</summary>
    private static Bounds LocalBounds(IReadOnlyCollection<Renderer> renderers, Transform space)
    {
        if (renderers.Count == 0) return new Bounds(Vector3.zero, Vector3.one * 0.3f);

        bool any = false;
        Bounds result = default;
        foreach (Renderer r in renderers)
        {
            Bounds local = r.localBounds;
            Matrix4x4 toRoot = space.worldToLocalMatrix * r.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(Corner(local, corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Prévisualiser et équiper
    // ------------------------------------------------------------------

    private void Preview(int variant)
    {
        if (variant >= 0 && !Accepted(selectedRubric, variant)) variant = -1;
        if (variant == previewVariant) return;
        previewVariant = variant;

        ShowWeapon(ShownLooks, ShownAttachments, resetRotation: false);
        RefreshChoiceStates();
        RefreshHeader();
        RefreshStats();
        // En quittant la bande, l'IA garde son dernier mot : elle ne parle que de ce qu'on regarde.
        if (variant >= 0) Say(ChoiceMessage(selectedRubric, variant));
    }

    private void Equip(int variant)
    {
        if (variant < 0 || !Accepted(selectedRubric, variant)) return;
        switch (selectedRubric)
        {
            case Rubric.Model:
                GameSettings.WeaponLooks = GameSettings.WeaponLooks.WithModel(currentSlot, variant);
                break;
            case Rubric.Finish:
                GameSettings.WeaponLooks = GameSettings.WeaponLooks.WithFinish(currentSlot, (WeaponFinish)variant);
                break;
            default:
                GameSettings.WeaponAttachments = GameSettings.WeaponAttachments.WithVariant(currentSlot, Category(selectedRubric), variant);
                break;
        }

        previewVariant = -1;
        ShowWeapon(GameSettings.WeaponLooks, GameSettings.WeaponAttachments, resetRotation: false);
        RefreshHeader();
        RefreshSlots();
        // Un autre modèle n'a pas les mêmes accessoires ni les mêmes finitions : la bande se refait.
        if (selectedRubric == Rubric.Model) BuildStrip();
        else RefreshChoiceStates();
        RefreshStats();
        Say($"Configuration enregistrée. {RubricLabel(selectedRubric)} : {ChoiceTitle(selectedRubric, variant)}.");
    }

    private void SelectRubric(Rubric rubric, bool openStrip)
    {
        if (!Available(rubric)) return;
        if (previewVariant >= 0)
        {
            previewVariant = -1;
            ShowWeapon(GameSettings.WeaponLooks, GameSettings.WeaponAttachments, resetRotation: false);
            RefreshHeader();
            RefreshStats();
        }
        bool changed = rubric != selectedRubric;
        selectedRubric = rubric;
        if (changed) BuildStrip();
        SetStripOpen(openStrip);
        RefreshSlots();
        Say(RubricMessage(rubric));
    }

    /// <summary>Un clic sur un emplacement ouvre sa bande, ou la replie si c'est déjà la sienne.</summary>
    private void OnSlotClicked(Rubric rubric)
    {
        if (rubric == selectedRubric && stripOpen)
        {
            CloseStrip();
            RefreshSlots();
            return;
        }
        SelectRubric(rubric, openStrip: true);
    }

    private void SetStripOpen(bool value)
    {
        stripOpen = value;
        slotStrip?.EnableInClassList("hidden", !value);
        RefreshSlots();
        if (!value) return;
        // Une fois la bande mise en page : on y voit le choix équipé. La bande a pu être refaite entre
        // temps (une autre rubrique ouverte dans la même image) : sa carte n'y est alors plus.
        int equipped = Equipped(selectedRubric);
        Choice current = choices.Find(c => c.variant == equipped);
        stripScroll.scrollOffset = Vector2.zero;
        stripScroll.schedule.Execute(() =>
        {
            if (current != null && current.button.parent == stripScroll.contentContainer) stripScroll.ScrollTo(current.button);
            RefreshArrows();
        });
    }

    // ------------------------------------------------------------------
    // Les rubriques et leurs choix
    // ------------------------------------------------------------------

    private static bool IsAttachment(Rubric rubric) => rubric <= Rubric.Magazine;

    private static AttachmentCategory Category(Rubric rubric) => rubric switch
    {
        Rubric.Sight => AttachmentCategory.Sight,
        Rubric.Grip => AttachmentCategory.Grip,
        Rubric.Laser => AttachmentCategory.Laser,
        _ => AttachmentCategory.Magazine,
    };

    private static string RubricLabel(Rubric rubric) => rubric switch
    {
        Rubric.Sight => "Viseur",
        Rubric.Grip => "Poignée",
        Rubric.Laser => "Laser",
        Rubric.Magazine => "Chargeur",
        Rubric.Model => "Modèle",
        _ => "Finition",
    };

    private static string RubricIcon(Rubric rubric) => rubric switch
    {
        Rubric.Sight => "ico-sight",
        Rubric.Grip => "ico-grip",
        Rubric.Laser => "ico-laser",
        Rubric.Magazine => "ico-magazine",
        Rubric.Model => "ico-rifle",
        _ => "ico-brush",
    };

    private bool Available(Rubric rubric)
    {
        WeaponData data = CurrentData;
        if (data == null) return false;
        if (IsAttachment(rubric)) return WeaponAttachments.AcceptsAny(data, Category(rubric));
        if (rubric == Rubric.Model) return data.ModelCount > 1;
        WeaponModel model = EquippedModel;
        return model != null && model.GetComponent<WeaponFinishes>() != null;
    }

    private Rubric FirstAvailableRubric()
    {
        foreach (Rubric rubric in AllRubrics)
            if (Available(rubric)) return rubric;
        return Rubric.Model;
    }

    private int VariantCount(Rubric rubric)
    {
        if (IsAttachment(rubric)) return WeaponAttachments.VariantCount(Category(rubric));
        if (rubric == Rubric.Model) return CurrentData != null ? CurrentData.ModelCount : 0;
        return WeaponLooks.FinishCount;
    }

    private bool Accepted(Rubric rubric, int variant)
    {
        if (variant < 0 || variant >= VariantCount(rubric)) return false;
        return !IsAttachment(rubric) || WeaponAttachments.Accepts(CurrentData, Category(rubric), variant);
    }

    private int Equipped(Rubric rubric)
    {
        if (IsAttachment(rubric)) return GameSettings.WeaponAttachments.Variant(currentSlot, Category(rubric));
        if (rubric == Rubric.Model)
        {
            int model = GameSettings.WeaponLooks.Model(currentSlot);
            return model < VariantCount(rubric) ? model : 0;
        }
        return (int)GameSettings.WeaponLooks.Finish(currentSlot);
    }

    /// <summary>Pourquoi un emplacement est grisé.</summary>
    private string UnavailableReason(Rubric rubric)
    {
        WeaponData data = CurrentData;
        if (rubric == Rubric.Sight && data != null && data.scopeMagnification > 1f) return "Garde sa lunette";
        WeaponModel model = EquippedModel;
        if (rubric == Rubric.Grip && model != null && model.PistolHold) return "Pas sur un pistolet";
        return "Pas sur cette arme";
    }

    private bool PistolModel
    {
        get
        {
            WeaponModel model = ShownModel;
            return model != null && model.PistolHold;
        }
    }

    private static string CodeOf(GameObject prefab, string fallback)
        => prefab != null && prefab.TryGetComponent(out WeaponAttachment a) && !string.IsNullOrEmpty(a.Code) ? a.Code : fallback;

    private static string TitleOf(GameObject prefab, string fallback)
        => prefab != null && prefab.TryGetComponent(out WeaponAttachment a) && !string.IsNullOrEmpty(a.Title) ? a.Title : fallback;

    private static string ModelTitle(WeaponModel model) => model == null ? "" : model.ModelFamily switch
    {
        WeaponModel.Family.Stock => "Stock",
        WeaponModel.Family.Serie30 => "Série 30",
        _ => "Prototype laser",
    };

    /// <summary>Ce qui s'écrit sur un emplacement et sur la case d'un choix : la désignation de l'accessoire
    /// ou du modèle, les balles du chargeur, l'abrégé de la finition.</summary>
    private string ChoiceCode(Rubric rubric, int variant)
    {
        if (IsAttachment(rubric)) return AttachmentCode(CurrentData, ShownModel, Category(rubric), variant);
        if (rubric == Rubric.Model)
        {
            WeaponModel model = ModelOf(variant);
            return model != null ? model.Designation : $"M{variant + 1}";
        }
        return WeaponLooks.FinishCode((WeaponFinish)variant);
    }

    /// <summary>La désignation d'un accessoire monté sur le modèle `model` d'une arme (« HO-A », « PV-1 »,
    /// « LT-P » sous un pistolet), les balles du chargeur, « MIRE » ou « — » sans accessoire.</summary>
    public static string AttachmentCode(WeaponData data, WeaponModel model, AttachmentCategory category, int variant)
    {
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        switch (category)
        {
            case AttachmentCategory.Sight:
                return variant == 0 ? "MIRE" : CodeOf(catalog != null ? catalog.SightPrefab(variant) : null, $"V{variant}");
            case AttachmentCategory.Grip:
                return variant == 0 ? "—" : CodeOf(catalog != null ? catalog.GripPrefab((GripKind)variant) : null, "P");
            case AttachmentCategory.Laser:
                return variant == 0 ? "—" : CodeOf(catalog != null ? catalog.LaserPrefab(model != null && model.PistolHold) : null, "LT");
            default:
                return AttachmentCatalog.MagazineSize(data, variant > 0, catalog).ToString();
        }
    }

    /// <summary>
    /// Une arme résumée : le modèle choisi et sa famille (« FA-30 · Série 30 »), puis sa finition et les
    /// accessoires que l'arme porte vraiment (« Repeinte · PI-1 · LT-1 · chargeur 45 ») : ce qu'elle ne prend
    /// pas, le serveur le ferait tomber.
    /// </summary>
    public static void Summarize(WeaponData data, int slot, WeaponLooks looks, WeaponAttachments attachments,
                                 out string model, out string details)
    {
        GameObject prefab = data != null ? data.ModelPrefabFor(looks.Model(slot)) : null;
        WeaponModel weaponModel = prefab != null ? prefab.GetComponent<WeaponModel>() : null;
        model = weaponModel != null ? $"{weaponModel.Designation} · {ModelTitle(weaponModel)}" : data != null ? data.weaponName : "";

        var parts = new List<string> { WeaponLooks.FinishLabel(looks.Finish(slot)) };
        foreach (AttachmentCategory category in new[] { AttachmentCategory.Sight, AttachmentCategory.Grip, AttachmentCategory.Laser })
        {
            int variant = attachments.Variant(slot, category);
            if (variant > 0 && WeaponAttachments.Accepts(data, category, variant))
                parts.Add(AttachmentCode(data, weaponModel, category, variant));
        }
        if (attachments.ExtendedMagazine(slot) && WeaponAttachments.Accepts(data, AttachmentCategory.Magazine, 1))
            parts.Add($"chargeur {AttachmentCode(data, weaponModel, AttachmentCategory.Magazine, 1)}");
        if (parts.Count == 1) parts.Add("sans accessoire");
        details = string.Join(" · ", parts);
    }

    private string ChoiceTitle(Rubric rubric, int variant)
    {
        AttachmentCatalog catalog = Catalog;
        switch (rubric)
        {
            case Rubric.Sight:
                return variant == 0 || catalog == null ? "Mire de l'arme" : catalog.SightTitle(variant);
            case Rubric.Grip:
                if (variant == 0) return "Aucune";
                return TitleOf(catalog != null ? catalog.GripPrefab((GripKind)variant) : null,
                    variant == (int)GripKind.Vertical ? "Poignée verticale" : "Poignée inclinée");
            case Rubric.Laser:
                return variant == 0 ? "Aucun" : TitleOf(catalog != null ? catalog.LaserPrefab(PistolModel) : null, "Laser tactique");
            case Rubric.Magazine:
                return variant == 0 ? "Chargeur d'origine" : "Grande capacité";
            case Rubric.Model:
                return ModelTitle(ModelOf(variant));
            default:
                return WeaponLooks.FinishLabel((WeaponFinish)variant);
        }
    }

    /// <summary>L'effet d'un choix, en quelques mots (case de la bande), lu dans le catalogue.</summary>
    private string ChoiceEffect(Rubric rubric, int variant)
    {
        AttachmentCatalog catalog = Catalog;
        WeaponData data = CurrentData;
        switch (rubric)
        {
            case Rubric.Sight:
                return variant == 0 ? "Hausse et guidon" : "Sans grossissement";
            case Rubric.Grip:
                if (variant == (int)GripKind.Vertical) return $"Recul vertical −{Less(catalog != null ? catalog.verticalGripRecoil : 1f)}";
                if (variant == (int)GripKind.Angled) return $"Épaulement −{Less(catalog != null ? catalog.angledGripAimTime : 1f)}";
                return "Aucun effet";
            case Rubric.Laser:
                return variant == 0 ? "Aucun effet" : $"Dispersion à la hanche −{Less(catalog != null ? catalog.laserHipSpread : 1f)}";
            case Rubric.Magazine:
                if (variant == 0) return $"Rechargement {WeaponStatSheet.Reload(data, WeaponAttachments.None, currentSlot)}";
                string more = More(catalog != null ? catalog.extendedMagazineCapacity : 1f);
                if (data != null && data.reloadPerRound) return $"Cartouches +{more}";
                return $"Coups +{more}, recharge +{More(catalog != null ? catalog.extendedMagazineReload : 1f)}";
            default:
                return "Apparence seulement";
        }
    }

    /// <summary>Ce que l'IA dit d'un choix survolé. Exact (Bible § 9) : tout est lu dans les données.</summary>
    private string ChoiceMessage(Rubric rubric, int variant)
    {
        WeaponData data = CurrentData;
        AttachmentCatalog catalog = Catalog;
        string title = ChoiceTitle(rubric, variant);
        string code = ChoiceCode(rubric, variant);
        switch (rubric)
        {
            case Rubric.Sight:
                if (variant == 0) return "Mire de l'arme : sa hausse et son guidon d'origine.";
                return $"{title} ({code}). Viseur sans grossissement : il remplace la hausse et le guidon. Le tir ne change pas.";
            case Rubric.Grip:
                if (variant == (int)GripKind.Vertical)
                    return $"{title} ({code}). Recul vertical : −{Less(catalog != null ? catalog.verticalGripRecoil : 1f)}, épaulé comme à la hanche.";
                if (variant == (int)GripKind.Angled)
                {
                    float before = AttachmentCatalog.AimTime(data, GripKind.None, catalog);
                    float after = AttachmentCatalog.AimTime(data, GripKind.Angled, catalog);
                    return $"{title} ({code}). Épaulement : {Milliseconds(before)} → {Milliseconds(after)}.";
                }
                return "Aucune poignée : le recul et l'épaulement de la classe.";
            case Rubric.Laser:
                if (variant == 0) return "Aucun laser.";
                return $"{title} ({code}). Dispersion à la hanche : −{Less(catalog != null ? catalog.laserHipSpread : 1f)}. "
                       + "Son faisceau et son point se voient de loin, par l'adversaire aussi.";
            case Rubric.Magazine:
                int standard = AttachmentCatalog.MagazineSize(data, false, catalog);
                int extended = AttachmentCatalog.MagazineSize(data, true, catalog);
                string rounds = data != null && data.reloadPerRound ? "cartouches" : "coups";
                string reload = WeaponStatSheet.Reload(data, WeaponAttachments.None, currentSlot);
                if (variant == 0) return $"Chargeur d'origine : {standard} {rounds}. Rechargement : {reload}.";
                if (data != null && data.reloadPerRound)
                    return $"Chargeur grande capacité : {standard} → {extended} {rounds}. Chacune se glisse toujours en "
                           + $"{WeaponStatSheet.Number(AttachmentCatalog.ReloadDuration(data, true, catalog))} s.";
                string longer = WeaponStatSheet.Reload(data, WeaponAttachments.None.WithMagazine(currentSlot, true), currentSlot);
                return $"Chargeur grande capacité : {standard} → {extended} {rounds}. Rechargement : {reload} → {longer}.";
            case Rubric.Model:
                WeaponModel model = ModelOf(variant);
                if (model == null) return title;
                return $"{model.Designation}, {WeaponModel.FamilyLabel(model.ModelFamily)}. {WeaponModel.FamilyDescription(model.ModelFamily)} "
                       + "Apparence seulement : la classe fait les dégâts.";
            default:
                var finish = (WeaponFinish)variant;
                return $"Finition {WeaponLooks.FinishLabel(finish).ToLowerInvariant()}. {WeaponLooks.FinishDescription(finish)} "
                       + "Apparence seulement, vue par tous les joueurs.";
        }
    }

    /// <summary>Ce que l'IA dit d'un emplacement qu'on ouvre.</summary>
    private string RubricMessage(Rubric rubric)
    {
        WeaponData data = CurrentData;
        switch (rubric)
        {
            case Rubric.Sight:
                if (data != null && data.acceptedSights != SightFamilies.All)
                    return $"Viseurs sans grossissement. Cette arme ne prend que les {SightFamiliesLabel(data.acceptedSights)}.";
                return "Viseurs : dix modèles sans grossissement, points rouges, holographiques et réflex. Le tir ne change pas.";
            case Rubric.Grip:
                return "Poignées : la verticale retient le recul vertical, l'inclinée fait épauler plus vite. Une seule à la fois.";
            case Rubric.Laser:
                return "Laser tactique : moins de dispersion à la hanche. Son point se voit.";
            case Rubric.Magazine:
                return data != null && data.reloadPerRound
                    ? "Chargeur grande capacité : plus de cartouches, chacune glissée aussi vite."
                    : "Chargeur grande capacité : plus de coups, un rechargement plus long.";
            case Rubric.Model:
                return "Trois modèles par classe : le stock, la série 30, le prototype laser. Apparence seulement.";
            default:
                return "Six finitions, vues par tous les joueurs. Apparence seulement.";
        }
    }

    private static string SightFamiliesLabel(SightFamilies families)
    {
        var names = new List<string>();
        if ((families & SightFamilies.RedDot) != 0) names.Add("points rouges");
        if ((families & SightFamilies.Holographic) != 0) names.Add("holographiques");
        if ((families & SightFamilies.Reflex) != 0) names.Add("réflex");
        return names.Count == 0 ? "aucun" : string.Join(" et ", names);
    }

    // Ce qu'un facteur retire (« 20 % » pour 0,8) ou ajoute (« 15 % » pour 1,15).
    private static string Less(float factor) => $"{Mathf.RoundToInt((1f - factor) * 100f)} %";
    private static string More(float factor) => $"{Mathf.RoundToInt((factor - 1f) * 100f)} %";
    private static string Milliseconds(float seconds) => $"{Mathf.RoundToInt(seconds * 1000f)} ms";

    // ------------------------------------------------------------------
    // À gauche : la panoplie
    // ------------------------------------------------------------------

    private void BuildWeaponRows()
    {
        weaponList.Clear();
        weaponRows.Clear();
        for (int i = 0; i < loadout.Length; i++)
        {
            int slot = i;
            var row = new Button { focusable = false };
            row.AddToClassList("da-btn");
            row.AddToClassList("side-btn");
            row.AddToClassList("ui-select");
            var background = new SlantBox { pickingMode = PickingMode.Ignore };
            background.AddToClassList("slant-bg");
            row.Add(background);
            var thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            thumb.AddToClassList("side-btn__thumb");
            row.Add(thumb);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("side-btn__text");
            text.Add(NewLabel("", "side-btn__title"));
            text.Add(NewLabel(loadout[i] != null ? loadout[i].weaponName.ToUpperInvariant() : $"ARME {i + 1}", "side-btn__subtitle"));
            row.Add(text);
            row.Add(NewLabel((i + 1).ToString(), "side-btn__key"));
            row.clicked += () => ShowSlot(slot);
            weaponList.Add(row);
            weaponRows.Add(row);
        }
        RefreshWeaponRows();
    }

    /// <summary>Le modèle et la vignette de chaque arme, telle qu'elle est équipée.</summary>
    private void RefreshWeaponRows()
    {
        WeaponLooks looks = GameSettings.WeaponLooks;
        WeaponAttachments attachments = GameSettings.WeaponAttachments;
        for (int i = 0; i < weaponRows.Count && i < loadout.Length; i++)
        {
            WeaponData data = loadout[i];
            GameObject prefab = data != null ? data.ModelPrefabFor(looks.Model(i)) : null;
            WeaponModel model = prefab != null ? prefab.GetComponent<WeaponModel>() : null;
            Label title = weaponRows[i].Q<Label>(className: "side-btn__title");
            if (title != null) title.text = model != null ? model.Designation : data != null ? data.weaponName.ToUpperInvariant() : "";

            VisualElement thumb = weaponRows[i].Q<VisualElement>(className: "side-btn__thumb");
            if (thumbnails == null || thumb == null || data == null) continue;
            thumbnails.Weapon(data, looks.Model(i), looks.Finish(i), attachments, i, 224, 112, texture =>
            {
                if (texture is RenderTexture rt) thumb.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            });
        }
        RefreshWeaponRowStates();
    }

    private void RefreshWeaponRowStates()
    {
        for (int i = 0; i < weaponRows.Count; i++) weaponRows[i].EnableInClassList("side-btn--active", i == currentSlot);
    }

    // ------------------------------------------------------------------
    // Au milieu : les emplacements et la bande de leurs choix
    // ------------------------------------------------------------------

    private void BuildSlots()
    {
        slotRow.Clear();
        slots.Clear();
        foreach (Rubric rubric in AllRubrics)
        {
            var button = new Button { focusable = false };
            button.AddToClassList("slot-btn");
            button.AddToClassList("ui-select");
            if (rubric == Rubric.Model) button.AddToClassList("slot-btn--group-start");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("slot-btn__icon");
            icon.AddToClassList(RubricIcon(rubric));
            button.Add(icon);
            button.Add(NewLabel(RubricLabel(rubric), "slot-btn__label"));
            Label value = NewLabel("", "slot-btn__value");
            button.Add(value);
            var edge = new SlantBox { pickingMode = PickingMode.Ignore };
            edge.AddToClassList("slot-btn__edge");
            button.Add(edge);
            Rubric chosen = rubric;
            button.clicked += () => OnSlotClicked(chosen);
            slotRow.Add(button);
            slots.Add(new Slot { rubric = rubric, button = button, value = value });
        }
    }

    /// <summary>Chaque emplacement montre ce qui est équipé ; grisé, il dit pourquoi.</summary>
    private void RefreshSlots()
    {
        foreach (Slot slot in slots)
        {
            bool available = Available(slot.rubric);
            slot.button.SetEnabled(available);
            slot.button.EnableInClassList("slot-btn--open", available && stripOpen && slot.rubric == selectedRubric);
            slot.button.tooltip = available ? ChoiceTitle(slot.rubric, Equipped(slot.rubric)) : UnavailableReason(slot.rubric);
            slot.value.text = available ? ChoiceCode(slot.rubric, Equipped(slot.rubric)) : "—";
        }
    }

    /// <summary>La bande des choix de l'emplacement ouvert : ceux que l'arme prend, celui qui est équipé
    /// marqué. Survoler prévisualise, cliquer équipe.</summary>
    private void BuildStrip()
    {
        stripScroll.Clear();
        choices.Clear();
        for (int variant = 0; variant < VariantCount(selectedRubric); variant++)
        {
            if (!Accepted(selectedRubric, variant)) continue;
            int chosen = variant;
            var card = new Button { focusable = false };
            card.AddToClassList("choice-card");
            card.AddToClassList("ui-select");
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("choice-card__art");
            card.Add(art);
            Label code = NewLabel(ChoiceCode(selectedRubric, variant), "choice-card__code");
            card.Add(code);
            card.Add(NewLabel(ChoiceTitle(selectedRubric, variant), "choice-card__title"));
            card.Add(NewLabel(ChoiceEffect(selectedRubric, variant), "choice-card__effect"));
            card.Add(NewLabel("ÉQUIPÉ", "choice-card__tag"));
            var edge = new SlantBox { pickingMode = PickingMode.Ignore };
            edge.AddToClassList("choice-card__edge");
            card.Add(edge);
            card.RegisterCallback<PointerEnterEvent>(_ => Preview(chosen));
            card.clicked += () => Equip(chosen);
            stripScroll.Add(card);
            choices.Add(new Choice { variant = variant, button = card });
            RequestChoiceArt(variant, art, code);
        }

        stripTitle.text = RubricLabel(selectedRubric).ToUpperInvariant();
        stripCount.text = choices.Count > 1 ? $"{choices.Count} choix" : "";
        RefreshChoiceStates();
        if (stripOpen) SetStripOpen(true);
    }

    /// <summary>La vignette d'un choix : l'accessoire seul, l'arme dans un autre modèle ou une autre
    /// finition. Sans vignette (la mire, « aucun »), le code reste écrit à sa place.</summary>
    private void RequestChoiceArt(int variant, VisualElement art, Label code)
    {
        if (thumbnails == null) return;
        WeaponData data = CurrentData;
        WeaponLooks looks = GameSettings.WeaponLooks;
        void Show(Texture texture)
        {
            if (texture is not RenderTexture rt) return;
            art.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            code.AddToClassList("hidden");
        }

        switch (selectedRubric)
        {
            case Rubric.Model:
                thumbnails.Weapon(data, variant, looks.Finish(currentSlot), WeaponAttachments.None, currentSlot, 300, 132, Show);
                break;
            case Rubric.Finish:
                thumbnails.Weapon(data, looks.Model(currentSlot), (WeaponFinish)variant, WeaponAttachments.None, currentSlot, 300, 132, Show);
                break;
            default:
                if (variant == 0) return;
                GameObject prefab = AttachmentMount.PrefabFor(Catalog, EquippedModel, Category(selectedRubric), variant);
                if (prefab != null) thumbnails.Attachment(prefab, looks.Finish(currentSlot), 300, 132, Show);
                break;
        }
    }

    private void RefreshChoiceStates()
    {
        int equipped = Equipped(selectedRubric);
        foreach (Choice choice in choices)
        {
            choice.button.EnableInClassList("choice-card--equipped", choice.variant == equipped);
            choice.button.EnableInClassList("choice-card--previewed", choice.variant == previewVariant);
        }
    }

    private float MaxStripScroll => Mathf.Max(0f, stripScroll.contentContainer.layout.width - stripScroll.contentViewport.layout.width);

    private void PageStrip(int direction)
    {
        float pageWidth = Mathf.Max(100f, stripScroll.contentViewport.layout.width * 0.8f);
        ScrollStripTo(stripScroll.scrollOffset.x + direction * pageWidth, animate: true);
    }

    private void ScrollStripTo(float x, bool animate)
    {
        float max = MaxStripScroll;
        if (float.IsNaN(max)) return;
        float target = Mathf.Clamp(x, 0f, max);
        if (!animate)
        {
            stripScroll.scrollOffset = new Vector2(target, 0f);
            return;
        }
        float from = stripScroll.scrollOffset.x;
        stripScroll.experimental.animation
            .Start(from, target, 180, (_, value) => stripScroll.scrollOffset = new Vector2(value, 0f))
            .Ease(Easing.OutCubic);
    }

    private void RefreshArrows()
    {
        float max = MaxStripScroll;
        bool scrollable = !float.IsNaN(max) && max > 1f;
        float offset = stripScroll.scrollOffset.x;
        stripPrev.SetEnabled(scrollable && offset > 1f);
        stripNext.SetEnabled(scrollable && offset < max - 1f);
    }

    // ------------------------------------------------------------------
    // À droite : la fiche de l'arme et l'IA de supervision
    // ------------------------------------------------------------------

    private void RefreshHeader()
    {
        WeaponData data = CurrentData;
        WeaponModel model = ShownModel;
        modelLabel.text = model != null ? model.Designation : data != null ? data.weaponName.ToUpperInvariant() : "";
        string family = model != null ? WeaponModel.FamilyLabel(model.ModelFamily).ToUpperInvariant() : "";
        classLabel.text = data == null ? "" : string.IsNullOrEmpty(family) ? data.weaponName.ToUpperInvariant() : $"{data.weaponName.ToUpperInvariant()} · {family}";
        description.text = model != null ? WeaponModel.FamilyDescription(model.ModelFamily) : "";
    }

    private void BuildStatRows()
    {
        statRowContainer.Clear();
        statRows.Clear();
        foreach (WeaponStatSheet.Stat stat in WeaponStatSheet.Bars)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            var head = new VisualElement();
            head.AddToClassList("stat-row__head");
            head.Add(NewLabel(WeaponStatSheet.Label(stat), "stat-row__label"));
            Label value = NewLabel("", "stat-row__value");
            head.Add(value);
            row.Add(head);
            var track = new VisualElement();
            track.AddToClassList("stat-row__track");
            var fill = new VisualElement();
            fill.AddToClassList("stat-row__fill");
            var delta = new VisualElement();
            delta.AddToClassList("stat-row__delta");
            track.Add(fill);
            track.Add(delta);
            row.Add(track);
            statRowContainer.Add(row);
            statRows.Add(new StatRow { stat = stat, fill = fill, delta = delta, value = value });
        }
    }

    private void BuildInfoGrid()
    {
        infoGrid.Clear();
        rangeValue = AddInfo("PORTÉE EFFICACE");
        reloadValue = AddInfo("RECHARGEMENT");
        fireModeValue = AddInfo("MODE DE TIR");
        zonesValue = AddInfo("ZONES");
    }

    private Label AddInfo(string caption)
    {
        var info = new VisualElement();
        info.AddToClassList("info-cell");
        info.Add(NewLabel(caption, "info-cell__caption"));
        Label value = NewLabel("", "info-cell__value");
        info.Add(value);
        infoGrid.Add(info);
        return value;
    }

    /// <summary>Les barres de ce qui est équipé ; pendant un aperçu, ce que le choix survolé y gagne
    /// (vert) ou y perd (rouge).</summary>
    private void RefreshStats()
    {
        WeaponData data = CurrentData;
        WeaponAttachments equipped = GameSettings.WeaponAttachments;
        WeaponAttachments shown = ShownAttachments;
        float health = MaxHealth;
        bool anyChange = false;

        foreach (StatRow row in statRows)
        {
            float before = WeaponStatSheet.Fraction(row.stat, loadout, currentSlot, equipped, health);
            float after = WeaponStatSheet.Fraction(row.stat, loadout, currentSlot, shown, health);
            float from = WeaponStatSheet.Value(row.stat, data, equipped, currentSlot);
            float to = WeaponStatSheet.Value(row.stat, data, shown, currentSlot);
            bool changed = !Mathf.Approximately(from, to);
            bool gain = changed && (WeaponStatSheet.HigherIsBetter(row.stat) ? to > from : to < from);
            anyChange |= changed;

            float low = Mathf.Min(before, after);
            row.fill.style.width = Length.Percent(low * 100f);
            row.delta.style.left = Length.Percent(low * 100f);
            row.delta.style.width = Length.Percent(Mathf.Abs(after - before) * 100f);
            row.delta.EnableInClassList("stat-row__delta--gain", changed && gain);
            row.delta.EnableInClassList("stat-row__delta--loss", changed && !gain);
            row.value.text = changed ? WeaponStatSheet.FormatChange(row.stat, data, from, to) : WeaponStatSheet.Format(row.stat, data, to);
            row.value.EnableInClassList("stat-row__value--gain", changed && gain);
            row.value.EnableInClassList("stat-row__value--loss", changed && !gain);
        }

        rangeValue.text = WeaponStatSheet.EffectiveRange(data);
        fireModeValue.text = WeaponStatSheet.FireMode(data);
        zonesValue.text = WeaponStatSheet.Zones(data);

        string reloadBefore = WeaponStatSheet.Reload(data, equipped, currentSlot);
        string reloadAfter = WeaponStatSheet.Reload(data, shown, currentSlot);
        bool reloadChanged = reloadBefore != reloadAfter;
        reloadValue.text = reloadChanged ? $"{reloadBefore} → {reloadAfter}" : reloadAfter;
        bool slower = reloadChanged && data != null
            && AttachmentCatalog.ReloadDuration(data, shown.ExtendedMagazine(currentSlot), data.attachmentCatalog)
             > AttachmentCatalog.ReloadDuration(data, equipped.ExtendedMagazine(currentSlot), data.attachmentCatalog);
        reloadValue.EnableInClassList("stat-row__value--loss", reloadChanged && slower);
        reloadValue.EnableInClassList("stat-row__value--gain", reloadChanged && !slower);

        statsLegend.text = anyChange || reloadChanged ? "APERÇU : CE QUI CHANGE" : "";
    }

    /// <summary>L'IA de supervision parle (Bible § 9) : l'heure de la machine, puis son message, écrit
    /// lettre à lettre. Le cadre a une hauteur fixe : un message qui s'écrit ne fait pas bouger la mise
    /// en page (l'aperçu de l'arme se redimensionnerait, et sa texture avec).</summary>
    private void Say(string message)
    {
        aiHeader.text = $"IA DE SUPERVISION // {DateTime.Now:HH:mm}";
        aiMessage = message ?? "";
        aiStartTime = Time.unscaledTime;
        aiText.text = "";
        aiTyping ??= aiText.schedule.Execute(TypeStep).Every(16);
        aiTyping.Resume();
    }

    private void TypeStep()
    {
        int count = Mathf.Min(aiMessage.Length, Mathf.FloorToInt((Time.unscaledTime - aiStartTime) * aiTypingSpeed));
        bool done = count >= aiMessage.Length;
        aiText.text = done ? aiMessage : aiMessage.Substring(0, count) + "_";
        if (done) aiTyping.Pause();
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }
}
