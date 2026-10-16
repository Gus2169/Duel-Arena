using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// L'écran « Personnalisation » du menu principal (`screenArmory`), refait le 2026-10-16 sur demande de
/// l'utilisateur en deux parties bien distinctes, sur la même disposition plein écran que l'armurerie :
///
/// - ROBOT, à gauche : le robot qui tourne (CharacterPreviewController : sans arme, en attente à mains
///   nues) et sa couleur. Le choix part dans la demande de connexion (GameSettings.PreferredColorIndex,
///   SessionFlow) ; en partie, le serveur la donne si aucun autre joueur ne la porte, sinon la première
///   libre (PlayerPaint). La palette est celle de PlayerPaint, lue sur le préfab joueur, jamais
///   recopiée : une copie divergerait en silence le jour où la palette change.
/// - ARMES, à droite : les quatre armes de la panoplie, chacune avec son modèle, sa finition et ses
///   accessoires ; un clic ouvre l'armurerie sur elle (WeaponCustomizationController).
///
/// Il n'y a plus d'arme de départ (retirée le 2026-10-16, demandé par l'utilisateur) : on arrive en
/// partie avec la première arme, et chacun choisit la sienne avant chaque manche, pendant la
/// préparation.
///
/// Posé sur le même GameObject que l'UIDocument (voir MainMenuController).
/// </summary>
public class ArmoryController : MonoBehaviour
{
    [Tooltip("Les 4 WeaponData de la panoplie, DANS LE MÊME ORDRE que WeaponController.loadout sur le Player (touches 1 à 4).")]
    [SerializeField] private WeaponData[] loadoutPreview = new WeaponData[0];

    [Tooltip("Le robot tournant de cet écran : reçoit la couleur en temps réel à chaque clic de swatch.")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("L'armurerie (modèle, finition et accessoires de chaque arme), ouverte sur l'arme cliquée.")]
    [SerializeField] private WeaponCustomizationController weaponCustomization;

    [SerializeField] private MainMenuController mainMenu;

    [SerializeField] private UIDocument document;

    private VisualElement colorRow;
    private Label colorRuleLabel;
    private VisualElement weaponList;
    private Label playerName;
    private VisualElement playerColor;
    private readonly List<VisualElement> colorSwatches = new();
    private bool wired;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        colorRow = root.Q<VisualElement>("colorSwatchRow");
        colorRuleLabel = root.Q<Label>("colorRuleLabel");
        weaponList = root.Q<VisualElement>("weaponSummaryList");
        playerName = root.Q<Label>("customPlayerName");
        playerColor = root.Q<VisualElement>("customPlayerColor");

        GameSettings.WeaponLooksChanged += BuildWeaponList;
        GameSettings.WeaponAttachmentsChanged += BuildWeaponList;

        BuildColorSwatches();
        BuildWeaponList();
        RefreshPlayerCard();

        if (wired) return;
        wired = true;
        root.Q<Button>("openWeaponCustomizeButton").clicked += () => OpenArmory(0);
    }

    private void OnDisable()
    {
        GameSettings.WeaponLooksChanged -= BuildWeaponList;
        GameSettings.WeaponAttachmentsChanged -= BuildWeaponList;
    }

    /// <summary>Appelé par MainMenuController quand l'écran s'affiche : le pseudo a pu changer depuis
    /// l'écran Jouer, et les armes depuis l'armurerie.</summary>
    public void Refresh()
    {
        RefreshPlayerCard();
        BuildWeaponList();
    }

    private void OpenArmory(int slot)
    {
        mainMenu.ShowScreenByName("screenWeaponCustomize");
        weaponCustomization.Open(slot);
    }

    // ------------------------------------------------------------------
    // ROBOT : la couleur
    // ------------------------------------------------------------------

    private IReadOnlyList<Color32> Palette => characterPreview != null ? characterPreview.Palette : System.Array.Empty<Color32>();

    private void BuildColorSwatches()
    {
        colorRow.Clear();
        colorSwatches.Clear();

        IReadOnlyList<Color32> palette = Palette;
        int preferred = GameSettings.PreferredColorIndex;
        for (int i = 0; i < palette.Count; i++)
        {
            int index = i;
            var swatch = new VisualElement();
            swatch.AddToClassList("color-swatch");
            swatch.style.backgroundColor = new StyleColor(palette[i]);
            swatch.RegisterCallback<ClickEvent>(_ => SelectColor(index));
            colorSwatches.Add(swatch);
            colorRow.Add(swatch);
        }
        ShowColor(preferred);
    }

    private void SelectColor(int index)
    {
        GameSettings.PreferredColorIndex = index;
        ShowColor(index);
    }

    /// <summary>Le robot, la pastille choisie et la carte du joueur dans la couleur `index` (-1 : aucun
    /// choix, le robot montre la première couleur, celle du premier joueur arrivé).</summary>
    private void ShowColor(int index)
    {
        IReadOnlyList<Color32> palette = Palette;
        bool chosen = index >= 0 && index < palette.Count;
        for (int i = 0; i < colorSwatches.Count; i++) colorSwatches[i].EnableInClassList("color-swatch--active", i == index);

        if (palette.Count > 0)
        {
            Color32 shown = palette[chosen ? index : 0];
            if (characterPreview != null) characterPreview.SetPaint(shown);
            playerColor.style.backgroundColor = new StyleColor(shown);
        }
        colorRuleLabel.text = chosen
            ? "En partie, ton robot porte cette couleur si personne ne l'a déjà ; sinon la première couleur libre de la palette."
            : "Aucune couleur choisie : en partie, ton robot prend la première couleur libre de la palette.";
    }

    private void RefreshPlayerCard()
    {
        string name = GameSettings.PlayerName;
        playerName.text = string.IsNullOrEmpty(name) ? "Sans pseudo" : name;
        ShowColor(GameSettings.PreferredColorIndex);
    }

    // ------------------------------------------------------------------
    // ARMES : le résumé de chacune, qui ouvre l'armurerie
    // ------------------------------------------------------------------

    private void BuildWeaponList()
    {
        if (weaponList == null) return;
        weaponList.Clear();
        WeaponLooks looks = GameSettings.WeaponLooks;
        WeaponAttachments attachments = GameSettings.WeaponAttachments;

        for (int i = 0; i < loadoutPreview.Length; i++)
        {
            int slot = i;
            WeaponData data = loadoutPreview[i];
            WeaponCustomizationController.Summarize(data, slot, looks, attachments, out string model, out string details);

            var row = new Button { focusable = false };
            row.AddToClassList("custom-weapon-row");
            row.Add(NewLabel((slot + 1).ToString(), "custom-weapon-key"));
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("custom-weapon-text");
            text.Add(NewLabel(data != null ? data.weaponName.ToUpperInvariant() : $"ARME {slot + 1}", "custom-weapon-class"));
            text.Add(NewLabel(model, "custom-weapon-model"));
            text.Add(NewLabel(details, "custom-weapon-details"));
            row.Add(text);
            row.Add(NewLabel("›", "custom-weapon-arrow"));
            row.clicked += () => OpenArmory(slot);
            weaponList.Add(row);
        }
    }

    private static Label NewLabel(string text, string ussClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(ussClass);
        return label;
    }
}
