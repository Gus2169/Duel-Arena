using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Écran « Personnalisation » du menu principal : couleur souhaitée + arme de départ. Posé sur
/// le même GameObject que l'UIDocument (voir MainMenuController).
///
/// Volontairement minimal (demandé tel quel : « pas grand chose à faire dedans mais au moins on
/// l'aura pour la suite ») :
/// - la couleur n'est qu'une PRÉFÉRENCE stockée en local (GameSettings.PreferredColorIndex).
///   PlayerPaint reste seul maître de la couleur réellement attribuée (voir CLAUDE.md, § « La
///   couleur de chaque joueur est attribuée par le serveur ») : câbler ce choix jusqu'au serveur
///   est un chantier à part (RPC, validation, conflits entre deux clients qui veulent la même
///   couleur), explicitement reporté à l'arrivée de la vraie personnalisation.
/// - l'arme de départ, elle, est réellement appliquée : WeaponController relit
///   GameSettings.PreferredWeaponSlot à l'apparition et déclenche, une seule fois, le même
///   changement d'arme authentifié que les touches 1 à 4.
///
/// La palette est celle de PlayerPaint, lue sur le préfab joueur (via CharacterPreviewController),
/// jamais recopiée : une copie divergerait en silence le jour où la palette change.
/// </summary>
public class ArmoryController : MonoBehaviour
{
    [Tooltip("Les 4 WeaponData de la panoplie, DANS LE MÊME ORDRE que WeaponController.loadout sur le Player (touches 1 à 4).")]
    [SerializeField] private WeaponData[] loadoutPreview = new WeaponData[0];

    [Tooltip("Le robot tournant de cet écran : reçoit la couleur en temps réel à chaque clic de swatch.")]
    [SerializeField] private CharacterPreviewController characterPreview;

    [Tooltip("La fenêtre d'armurerie avancée (poignée, viseur, laser, chargeur), ouverte sur l'arme équipée.")]
    [SerializeField] private WeaponCustomizationController weaponCustomization;

    [SerializeField] private MainMenuController mainMenu;

    [SerializeField] private UIDocument document;

    private VisualElement colorRow;
    private VisualElement weaponRow;
    private readonly System.Collections.Generic.List<VisualElement> colorSwatches = new();
    private readonly System.Collections.Generic.List<Button> weaponCards = new();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        colorRow = root.Q<VisualElement>("colorSwatchRow");
        weaponRow = root.Q<VisualElement>("weaponCardRow");

        BuildColorSwatches();
        BuildWeaponCards();

        Button customizeButton = root.Q<Button>("openWeaponCustomizeButton");
        if (customizeButton != null)
        {
            customizeButton.clicked += () =>
            {
                int slot = Mathf.Max(0, GameSettings.PreferredWeaponSlot);
                mainMenu.ShowScreenByName("screenWeaponCustomize");
                weaponCustomization.Open(slot);
            };
        }
    }

    private void BuildColorSwatches()
    {
        colorRow.Clear();
        colorSwatches.Clear();

        IReadOnlyList<Color32> palette = characterPreview != null ? characterPreview.Palette : System.Array.Empty<Color32>();
        int preferred = GameSettings.PreferredColorIndex;
        int initial = preferred >= 0 && preferred < palette.Count ? preferred : 0;
        if (characterPreview != null && palette.Count > 0) characterPreview.SetPaint(palette[initial]);

        for (int i = 0; i < palette.Count; i++)
        {
            int index = i;
            var swatch = new VisualElement();
            swatch.AddToClassList("color-swatch");
            swatch.style.backgroundColor = new StyleColor(palette[i]);
            swatch.EnableInClassList("color-swatch--active", index == preferred);

            swatch.RegisterCallback<ClickEvent>(_ =>
            {
                GameSettings.PreferredColorIndex = index;
                foreach (VisualElement s in colorSwatches) s.RemoveFromClassList("color-swatch--active");
                swatch.AddToClassList("color-swatch--active");
                if (characterPreview != null) characterPreview.SetPaint(palette[index]);
            });

            colorSwatches.Add(swatch);
            colorRow.Add(swatch);
        }
    }

    private void BuildWeaponCards()
    {
        weaponRow.Clear();
        weaponCards.Clear();

        int preferred = GameSettings.PreferredWeaponSlot;
        for (int i = 0; i < loadoutPreview.Length; i++)
        {
            int slot = i;
            WeaponData data = loadoutPreview[i];
            string label = data != null ? data.weaponName : $"Arme {i + 1}";

            var card = new Button { text = label };
            card.AddToClassList("weapon-card");
            card.EnableInClassList("weapon-card--active", slot == preferred);

            card.clicked += () =>
            {
                GameSettings.PreferredWeaponSlot = slot;
                foreach (Button c in weaponCards) c.RemoveFromClassList("weapon-card--active");
                card.AddToClassList("weapon-card--active");
                // le robot de l'aperçu prend l'arme de départ en main
                if (characterPreview != null) characterPreview.RefreshWeapon();
            };

            weaponCards.Add(card);
            weaponRow.Add(card);
        }
    }
}
