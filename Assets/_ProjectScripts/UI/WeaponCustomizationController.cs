using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'armurerie avancée (2026-10-12) : une fenêtre dédiée, ouverte depuis Personnalisation, avec
/// une arme de profil qu'on fait tourner à la souris et quatre rubriques d'accessoires façon Call
/// of Duty Cold War (poignée, viseur, laser, chargeur étendu), appliqués à l'arme en temps réel.
/// Posé sur le GameObject `MainMenuUI`, à côté des autres contrôleurs d'écran.
///
/// 🚨 PUREMENT COSMÉTIQUE ET LIMITÉ AU MENU : les accessoires choisis ici ne changent RIEN à
/// l'arme réellement tenue en jeu (ni pour le propriétaire, ni pour les autres joueurs) — les
/// brancher demanderait de réseauter le choix et de toucher WeaponController/HeldWeapons/
/// FirstPersonView, un chantier à part, délibérément hors scope (voir GameSettings.SetAttachment).
///
/// La géométrie des accessoires est générée (PreviewAttachmentFactory, des primitives, pas des
/// modèles 3D). Elle s'accroche à des POINTS D'ACCROCHE posés dans le préfab de chaque arme,
/// enfants de `Accessoires` : `Poignee`, `Viseur`, `Laser`, `Chargeur` (voir SocketNames). Leur
/// position place l'accessoire, leur rotation l'oriente (un chargeur pend le long de -Y, un laser
/// tire le long de +Z), leur échelle le dimensionne (le chargeur d'un pistolet est plus court).
/// Une arme sans point pour une rubrique n'a pas cette rubrique : elle s'affiche grisée (le raygun
/// n'a pas de poignée avant). Les points se règlent à la main dans le préfab, sans code.
/// </summary>
public class WeaponCustomizationController : MonoBehaviour
{
    [Tooltip("Les 4 WeaponData de la panoplie, MÊME ORDRE que WeaponController.loadout sur le Player (touches 1 à 4).")]
    [SerializeField] private WeaponData[] loadout = new WeaponData[0];

    [SerializeField] private PreviewStage stage;
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    /// <summary>Le groupe des points d'accroche dans le préfab d'une arme.</summary>
    public const string SocketGroupName = "Accessoires";

    /// <summary>Nom du point d'accroche de chaque rubrique, indexé par AttachmentCategory.</summary>
    public static readonly string[] SocketNames = { "Poignee", "Viseur", "Laser", "Chargeur" };

    private static readonly string[] CategoryLabels = { "Poignée", "Viseur", "Laser", "Chargeur étendu" };

    // Index 0 = « aucun accessoire » dans chaque rubrique : jamais de géométrie construite pour
    // lui, juste un nom affiché dans la liste des choix.
    private static readonly string[][] VariantLabels =
    {
        new[] { "Aucune", "Poignée courte", "Poignée verticale" },
        new[] { "Aucun", "Point rouge", "Holographique" },
        new[] { "Aucun", "Laser tactique" },
        new[] { "Standard", "Chargeur étendu" },
    };

    private VisualElement root;
    private Image viewport;
    private Label weaponNameLabel;
    private VisualElement weaponSwitchRow;
    private VisualElement categoryTabRow;
    private VisualElement optionRow;

    private Button[] weaponSwitchButtons = System.Array.Empty<Button>();
    private Button[] categoryTabButtons = System.Array.Empty<Button>();

    private int currentWeaponSlot;
    private AttachmentCategory currentCategory = AttachmentCategory.Grip;
    private GameObject currentWeaponInstance;
    private Bounds currentWeaponBounds;

    // Une entrée par rubrique DISPONIBLE sur l'arme affichée : le tableau a la même taille que
    // VariantLabels[catégorie], l'indice 0 reste toujours null (pas de géométrie pour « aucun »).
    private readonly Dictionary<AttachmentCategory, GameObject[]> attachmentInstances = new();

    private bool wired;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (stage == null) stage = GetComponent<PreviewStage>();
        root = document.rootVisualElement;

        viewport = root.Q<Image>("weaponPreviewImage");
        weaponNameLabel = root.Q<Label>("weaponCustomizeName");
        weaponSwitchRow = root.Q<VisualElement>("weaponSwitchRow");
        categoryTabRow = root.Q<VisualElement>("categoryTabRow");
        optionRow = root.Q<VisualElement>("weaponOptionRow");

        if (wired) return;
        wired = true;

        BuildWeaponSwitcher();
        BuildCategoryTabs();
        root.Q<Button>("weaponCustomizeBackButton").clicked += Close;
    }

    /// <summary>Ouvre la fenêtre sur l'arme demandée (appelé par ArmoryController).</summary>
    public void Open(int weaponSlot)
    {
        currentCategory = AttachmentCategory.Grip;
        SwitchWeapon(Mathf.Clamp(weaponSlot, 0, loadout.Length - 1));
    }

    private void Close()
    {
        stage.Hide();
        mainMenu.ShowScreenByName("screenArmory");
    }

    // ------------------------------------------------------------------
    // Sélecteur d'arme
    // ------------------------------------------------------------------

    private void BuildWeaponSwitcher()
    {
        weaponSwitchRow.Clear();
        weaponSwitchButtons = new Button[loadout.Length];
        for (int i = 0; i < loadout.Length; i++)
        {
            int slot = i;
            WeaponData data = loadout[i];
            var button = new Button { text = data != null ? data.weaponName : $"Arme {i + 1}" };
            button.AddToClassList("weapon-switch-button");
            button.clicked += () => SwitchWeapon(slot);
            weaponSwitchRow.Add(button);
            weaponSwitchButtons[i] = button;
        }
    }

    private void SwitchWeapon(int slot)
    {
        currentWeaponSlot = slot;
        BuildWeaponInstance(slot);
        RefreshWeaponSwitchHighlight();

        // La rubrique ouverte reste la même d'une arme à l'autre, sauf si la nouvelle ne l'a pas.
        if (!attachmentInstances.ContainsKey(currentCategory))
        {
            foreach (AttachmentCategory category in System.Enum.GetValues(typeof(AttachmentCategory)))
            {
                if (attachmentInstances.ContainsKey(category)) { currentCategory = category; break; }
            }
        }
        RefreshCategoryTabs();
        RefreshOptionRow();
        stage.ShowWeapon(currentWeaponBounds, viewport);
    }

    private void RefreshWeaponSwitchHighlight()
    {
        for (int i = 0; i < weaponSwitchButtons.Length; i++)
        {
            weaponSwitchButtons[i].EnableInClassList("weapon-switch-button--active", i == currentWeaponSlot);
        }
    }

    // ------------------------------------------------------------------
    // L'arme et ses accessoires
    // ------------------------------------------------------------------

    private void BuildWeaponInstance(int slot)
    {
        if (currentWeaponInstance != null) Destroy(currentWeaponInstance);
        currentWeaponInstance = null;
        attachmentInstances.Clear();
        currentWeaponBounds = new Bounds(Vector3.zero, Vector3.one * 0.3f);

        WeaponData data = slot >= 0 && slot < loadout.Length ? loadout[slot] : null;
        weaponNameLabel.text = data != null ? data.weaponName : $"Arme {slot + 1}";
        if (data == null || data.modelPrefab == null || stage.WeaponPivot == null) return;

        currentWeaponInstance = Instantiate(data.modelPrefab, stage.WeaponPivot);
        currentWeaponInstance.transform.localRotation = Quaternion.identity;
        CharacterPreviewController.SetLayerRecursively(currentWeaponInstance, PreviewLayer.Index);

        // Centrée sur le pivot, l'arme tourne autour de son milieu, pas de sa poignée pistolet
        // (l'origine du préfab).
        Transform weapon = currentWeaponInstance.transform;
        Bounds bounds = LocalBounds(weapon.GetComponentsInChildren<Renderer>(), weapon);
        weapon.localPosition = -bounds.center;

        // Le cadrage couvre l'arme ET la place de tous ses accessoires possibles : il ne bouge pas
        // quand on en change, et un chargeur étendu ne sort pas du cadre.
        var accessoryRenderers = new List<Renderer>();
        BuildAttachments(accessoryRenderers);
        bounds.Encapsulate(LocalBounds(accessoryRenderers, weapon));
        currentWeaponBounds = new Bounds(bounds.center + weapon.localPosition, bounds.size);

        ApplySavedSelections(slot);
    }

    private void BuildAttachments(List<Renderer> accessoryRenderers)
    {
        Transform group = currentWeaponInstance.transform.Find(SocketGroupName);
        if (group == null) return;

        foreach (AttachmentCategory category in System.Enum.GetValues(typeof(AttachmentCategory)))
        {
            Transform socket = group.Find(SocketNames[(int)category]);
            if (socket == null) continue;

            string[] labels = VariantLabels[(int)category];
            var options = new GameObject[labels.Length];
            for (int i = 1; i < labels.Length; i++)
            {
                GameObject go = PreviewAttachmentFactory.Build(category, i, socket);
                CharacterPreviewController.SetLayerRecursively(go, PreviewLayer.Index);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r.name != PreviewAttachmentFactory.BeamName) accessoryRenderers.Add(r);
                }
                go.SetActive(false);
                options[i] = go;
            }
            attachmentInstances[category] = options;
        }
    }

    private void ApplySavedSelections(int slot)
    {
        foreach (var pair in attachmentInstances)
        {
            ActivateVariant(pair.Value, GameSettings.GetAttachment(slot, (int)pair.Key));
        }
    }

    private static void ActivateVariant(GameObject[] options, int variant)
    {
        if (options == null) return;
        for (int i = 1; i < options.Length; i++)
        {
            if (options[i] != null) options[i].SetActive(i == variant);
        }
    }

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
                Vector3 c = local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 p = toRoot.MultiplyPoint3x4(c);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Rubriques et choix
    // ------------------------------------------------------------------

    private void BuildCategoryTabs()
    {
        categoryTabRow.Clear();
        categoryTabButtons = new Button[CategoryLabels.Length];
        for (int i = 0; i < CategoryLabels.Length; i++)
        {
            int cat = i;
            var button = new Button { text = CategoryLabels[i] };
            button.AddToClassList("tab-button");
            button.clicked += () =>
            {
                currentCategory = (AttachmentCategory)cat;
                RefreshCategoryTabs();
                RefreshOptionRow();
            };
            categoryTabRow.Add(button);
            categoryTabButtons[i] = button;
        }
    }

    private void RefreshCategoryTabs()
    {
        for (int i = 0; i < categoryTabButtons.Length; i++)
        {
            bool available = attachmentInstances.ContainsKey((AttachmentCategory)i);
            categoryTabButtons[i].SetEnabled(available);
            categoryTabButtons[i].tooltip = available ? string.Empty : "Pas disponible sur cette arme";
            categoryTabButtons[i].EnableInClassList("tab-button--active", available && i == (int)currentCategory);
        }
    }

    private void RefreshOptionRow()
    {
        optionRow.Clear();
        if (!attachmentInstances.ContainsKey(currentCategory)) return;

        string[] labels = VariantLabels[(int)currentCategory];
        int selected = GameSettings.GetAttachment(currentWeaponSlot, (int)currentCategory);

        for (int i = 0; i < labels.Length; i++)
        {
            int variant = i;
            var card = new Button { text = labels[i] };
            card.AddToClassList("weapon-card");
            card.EnableInClassList("weapon-card--active", variant == selected);
            card.clicked += () => SelectVariant(variant);
            optionRow.Add(card);
        }
    }

    private void SelectVariant(int variant)
    {
        GameSettings.SetAttachment(currentWeaponSlot, (int)currentCategory, variant);
        if (attachmentInstances.TryGetValue(currentCategory, out GameObject[] options))
        {
            ActivateVariant(options, variant);
        }
        RefreshOptionRow();
    }
}
