using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'armurerie avancée (2026-10-12) : une fenêtre dédiée, ouverte depuis Personnalisation, avec
/// une arme de profil qu'on fait tourner à la souris et des rubriques façon Call of Duty, appliquées
/// à l'arme en temps réel. Posé sur le GameObject `MainMenuUI`, à côté des autres contrôleurs d'écran.
///
/// Les deux premières rubriques (2026-10-15, GDD § 6) choisissent ce qui se voit en match, par tous
/// les joueurs : le MODÈLE de l'arme (le stock, la série 30 ou la version laser, WeaponData.ModelPrefabFor)
/// et sa FINITION (d'origine, repeinte, rouillée, dépareillée, polie par les mains, encrassée :
/// WeaponFinishes). Le choix est gardé dans GameSettings.WeaponLooks et part avec la demande de
/// connexion (SessionFlow) ; le serveur le borne et le publie (PlayerWeaponLooks). Purement
/// cosmétique : la classe de l'arme seule fait les dégâts.
///
/// Les suivantes (2026-10-16, GDD § 6) sont les accessoires, qui CHANGENT l'arme : les dix viseurs de
/// Rainbow Six, la poignée verticale (recul) ou inclinée (épaulement), le laser tactique (le tir au
/// juger, mais il se voit), le chargeur grande capacité. Tous ouverts à tous ; une arme ne propose que
/// ce qu'elle prend (WeaponAttachments.Accepts : pas de viseur sur le sniper, que les réflex sur un
/// pistolet, pas de poignée sur un pistolet), les autres rubriques sont grisées. Gardés dans
/// GameSettings.WeaponAttachments, ils partent avec la demande de connexion ; le serveur les borne et
/// les publie (PlayerWeaponAttachments). L'aperçu les monte comme l'arme tenue en jeu (AttachmentMount),
/// et chaque choix dit son effet, lu dans le catalogue (AttachmentCatalog).
/// </summary>
public class WeaponCustomizationController : MonoBehaviour
{
    [Tooltip("Les 4 WeaponData de la panoplie, MÊME ORDRE que WeaponController.loadout sur le Player (touches 1 à 4).")]
    [SerializeField] private WeaponData[] loadout = new WeaponData[0];

    [SerializeField] private PreviewStage stage;
    [SerializeField] private UIDocument document;
    [SerializeField] private MainMenuController mainMenu;

    // Les rubriques : le modèle, la finition, puis les accessoires dans l'ordre de Rainbow Six.
    private const int TabModel = 0;
    private const int TabFinish = 1;
    private const int FirstAttachmentTab = 2;
    private static readonly string[] LookLabels = { "Modèle", "Finition" };
    private static readonly AttachmentCategory[] TabCategories =
        { AttachmentCategory.Sight, AttachmentCategory.Grip, AttachmentCategory.Laser, AttachmentCategory.Magazine };

    private VisualElement root;
    private Image viewport;
    private Label weaponNameLabel;
    private VisualElement weaponSwitchRow;
    private VisualElement categoryTabRow;
    private VisualElement optionRow;

    private Button[] weaponSwitchButtons = System.Array.Empty<Button>();
    private Button[] tabButtons = System.Array.Empty<Button>();

    private int currentWeaponSlot;
    private int currentTab = TabModel;
    private GameObject currentWeaponInstance;
    private Bounds currentWeaponBounds;

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
        BuildTabs();
        root.Q<Button>("weaponCustomizeBackButton").clicked += Close;
    }

    /// <summary>Ouvre la fenêtre sur l'arme demandée (appelé par ArmoryController).</summary>
    public void Open(int weaponSlot)
    {
        currentTab = TabModel;
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

        // La rubrique ouverte reste la même d'une arme à l'autre, sauf un accessoire que la nouvelle
        // ne prend pas : on revient au modèle.
        if (!TabAvailable(currentTab)) currentTab = TabModel;
        RefreshTabs();
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

    private WeaponData CurrentData => currentWeaponSlot >= 0 && currentWeaponSlot < loadout.Length ? loadout[currentWeaponSlot] : null;

    private void BuildWeaponInstance(int slot)
    {
        if (currentWeaponInstance != null) Destroy(currentWeaponInstance);
        currentWeaponInstance = null;
        currentWeaponBounds = new Bounds(Vector3.zero, Vector3.one * 0.3f);

        WeaponData data = slot >= 0 && slot < loadout.Length ? loadout[slot] : null;
        WeaponLooks looks = GameSettings.WeaponLooks;
        GameObject prefab = data != null ? data.ModelPrefabFor(looks.Model(slot)) : null;
        weaponNameLabel.text = WeaponTitle(data, prefab, slot);
        if (prefab == null || stage.WeaponPivot == null) return;

        currentWeaponInstance = Instantiate(prefab, stage.WeaponPivot);
        currentWeaponInstance.transform.localRotation = Quaternion.identity;
        CharacterPreviewController.SetLayerRecursively(currentWeaponInstance, PreviewLayer.Index);
        if (currentWeaponInstance.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(looks.Finish(slot));

        // Centrée sur le pivot, l'arme tourne autour de son milieu, pas de sa poignée pistolet
        // (l'origine du préfab).
        Transform weapon = currentWeaponInstance.transform;
        Bounds bounds = LocalBounds(weapon.GetComponentsInChildren<Renderer>(), weapon);
        weapon.localPosition = -bounds.center;

        // Le cadrage couvre l'arme ET la place de tous les accessoires qu'elle prend : il ne bouge
        // pas quand on en change, et un chargeur grande capacité ne sort pas du cadre.
        EncapsulateAttachments(ref bounds, weapon, data);
        currentWeaponBounds = new Bounds(bounds.center + weapon.localPosition, bounds.size);

        MountAttachments();
    }

    /// <summary>Monte sur l'aperçu les accessoires choisis, dans la finition de l'arme, comme en jeu.</summary>
    private void MountAttachments()
    {
        if (currentWeaponInstance == null) return;
        AttachmentMount.Apply(currentWeaponInstance.transform, CurrentData, GameSettings.WeaponAttachments, currentWeaponSlot,
                              GameSettings.WeaponLooks.Finish(currentWeaponSlot));
    }

    /// <summary>« Fusil d'assaut — FA-07 (stock) » : la classe de l'arme, puis le modèle choisi.</summary>
    private static string WeaponTitle(WeaponData data, GameObject prefab, int slot)
    {
        string name = data != null ? data.weaponName : $"Arme {slot + 1}";
        WeaponModel model = prefab != null ? prefab.GetComponent<WeaponModel>() : null;
        if (model == null || string.IsNullOrEmpty(model.Designation)) return name;
        return $"{name} — {model.Designation} ({WeaponModel.FamilyLabel(model.ModelFamily)})";
    }

    /// <summary>Ajoute aux bornes de l'arme (dans son repère) celles de chaque accessoire qu'elle prend,
    /// posé sur son point d'accroche : lues sur les préfabs, sans les créer.</summary>
    private static void EncapsulateAttachments(ref Bounds bounds, Transform weapon, WeaponData data)
    {
        WeaponModel model = weapon.GetComponent<WeaponModel>();
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        foreach (AttachmentCategory category in TabCategories)
        {
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
    // Rubriques et choix
    // ------------------------------------------------------------------

    private static string CategoryLabel(AttachmentCategory category) => category switch
    {
        AttachmentCategory.Sight => "Viseur",
        AttachmentCategory.Grip => "Poignée",
        AttachmentCategory.Laser => "Laser",
        _ => "Chargeur",
    };

    private void BuildTabs()
    {
        categoryTabRow.Clear();
        tabButtons = new Button[FirstAttachmentTab + TabCategories.Length];
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int tab = i;
            var button = new Button { text = i < FirstAttachmentTab ? LookLabels[i] : CategoryLabel(TabCategories[i - FirstAttachmentTab]) };
            button.AddToClassList("tab-button");
            button.clicked += () =>
            {
                currentTab = tab;
                RefreshTabs();
                RefreshOptionRow();
            };
            categoryTabRow.Add(button);
            tabButtons[i] = button;
        }
    }

    private bool TabAvailable(int tab)
    {
        if (tab == TabModel) return CurrentData != null && CurrentData.ModelCount > 1;
        if (tab == TabFinish) return currentWeaponInstance != null && currentWeaponInstance.GetComponent<WeaponFinishes>() != null;
        return WeaponAttachments.AcceptsAny(CurrentData, TabCategories[tab - FirstAttachmentTab]);
    }

    private void RefreshTabs()
    {
        for (int i = 0; i < tabButtons.Length; i++)
        {
            bool available = TabAvailable(i);
            tabButtons[i].SetEnabled(available);
            tabButtons[i].tooltip = available ? string.Empty : "Pas sur cette arme";
            tabButtons[i].EnableInClassList("tab-button--active", available && i == currentTab);
        }
    }

    private void RefreshOptionRow()
    {
        optionRow.Clear();
        if (!TabAvailable(currentTab)) return;

        WeaponLooks looks = GameSettings.WeaponLooks;
        if (currentTab == TabModel)
        {
            WeaponData data = CurrentData;
            for (int i = 0; i < data.ModelCount; i++)
            {
                int model = i;
                WeaponModel weaponModel = data.ModelPrefabFor(i) != null ? data.ModelPrefabFor(i).GetComponent<WeaponModel>() : null;
                string label = weaponModel != null && !string.IsNullOrEmpty(weaponModel.Designation)
                    ? $"{weaponModel.Designation} · {WeaponModel.FamilyLabel(weaponModel.ModelFamily)}" : $"Modèle {i + 1}";
                AddCard(label, looks.Model(currentWeaponSlot) == i, () => SelectModel(model));
            }
            return;
        }
        if (currentTab == TabFinish)
        {
            for (int i = 0; i < WeaponLooks.FinishCount; i++)
            {
                var finish = (WeaponFinish)i;
                AddCard(WeaponLooks.FinishLabel(finish), looks.Finish(currentWeaponSlot) == finish, () => SelectFinish(finish));
            }
            return;
        }

        AttachmentCategory category = TabCategories[currentTab - FirstAttachmentTab];
        int selected = GameSettings.WeaponAttachments.Variant(currentWeaponSlot, category);
        for (int i = 0; i < WeaponAttachments.VariantCount(category); i++)
        {
            if (!WeaponAttachments.Accepts(CurrentData, category, i)) continue;
            int variant = i;
            AddCard(VariantLabel(CurrentData, category, variant), variant == selected, () => SelectVariant(category, variant));
        }
    }

    /// <summary>Le nom d'un choix et ce qu'il fait, lu dans le catalogue (les valeurs se règlent là).</summary>
    public static string VariantLabel(WeaponData data, AttachmentCategory category, int variant)
    {
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        switch (category)
        {
            case AttachmentCategory.Sight:
                if (variant == 0) return "Mire de l'arme";
                return catalog != null ? catalog.SightTitle(variant) : $"Viseur {variant}";
            case AttachmentCategory.Grip:
                if (variant == (int)GripKind.Vertical)
                    return $"Poignée verticale\n<size=12>recul vertical −{Less(catalog != null ? catalog.verticalGripRecoil : 1f)}</size>";
                if (variant == (int)GripKind.Angled)
                    return $"Poignée inclinée\n<size=12>épaulement −{Less(catalog != null ? catalog.angledGripAimTime : 1f)}</size>";
                return "Aucune";
            case AttachmentCategory.Laser:
                if (variant == 0) return "Aucun";
                return $"Laser tactique\n<size=12>recul à la hanche −{Less(catalog != null ? catalog.laserHipRecoil : 1f)}, mais il se voit</size>";
            default:
                int standard = data != null ? data.magazineSize : 0;
                if (variant == 0) return $"Chargeur d'origine\n<size=12>{standard} {RoundsLabel(data)}</size>";
                int extended = AttachmentCatalog.MagazineSize(data, true, catalog);
                bool longer = data != null && !data.reloadPerRound && catalog != null && catalog.extendedMagazineReload > 1f;
                string reload = longer ? $", rechargement +{More(catalog.extendedMagazineReload)}" : "";
                return $"Grande capacité\n<size=12>{extended} {RoundsLabel(data)}{reload}</size>";
        }
    }

    // Ce qu'un facteur retire (« 20 % » pour 0,8) ou ajoute (« 15 % » pour 1,15).
    private static string Less(float factor) => $"{Mathf.RoundToInt((1f - factor) * 100f)} %";
    private static string More(float factor) => $"{Mathf.RoundToInt((factor - 1f) * 100f)} %";

    private static string RoundsLabel(WeaponData data) => data != null && data.reloadPerRound ? "cartouches" : "coups";

    private void AddCard(string label, bool active, System.Action onClick)
    {
        var card = new Button { text = label, enableRichText = true };
        card.AddToClassList("weapon-card");
        card.EnableInClassList("weapon-card--active", active);
        card.clicked += onClick;
        optionRow.Add(card);
    }

    private void SelectModel(int model)
    {
        GameSettings.WeaponLooks = GameSettings.WeaponLooks.WithModel(currentWeaponSlot, model);
        // un autre modèle : d'autres pièces, d'autres points d'accroche, un autre cadrage
        SwitchWeapon(currentWeaponSlot);
    }

    private void SelectFinish(WeaponFinish finish)
    {
        GameSettings.WeaponLooks = GameSettings.WeaponLooks.WithFinish(currentWeaponSlot, finish);
        if (currentWeaponInstance != null && currentWeaponInstance.TryGetComponent(out WeaponFinishes finishes)) finishes.Apply(finish);
        MountAttachments();   // les accessoires prennent la finition de l'arme
        RefreshOptionRow();
    }

    private void SelectVariant(AttachmentCategory category, int variant)
    {
        GameSettings.WeaponAttachments = GameSettings.WeaponAttachments.WithVariant(currentWeaponSlot, category, variant);
        MountAttachments();
        RefreshOptionRow();
    }
}
