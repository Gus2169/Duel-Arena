using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Les sons de l'interface (2026-10-13), posés une fois sur un document UI Toolkit (le menu principal,
/// l'interface de la partie) : survol et clic de tout ce qui se clique, y compris ce que les contrôleurs
/// créent à la volée (cartes d'arme, pastilles de couleur, lignes de touches). Le genre du son se lit
/// sur les classes USS déjà en place : un bouton principal valide, un bouton de retour revient, une
/// carte d'arme se choisit.
///
/// À plat, chez ce joueur seul. Catégorie C.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class UiSounds : MonoBehaviour
{
    [SerializeField] private GameSounds sounds;

    private VisualElement root;
    private VisualElement hovered;

    public enum Kind
    {
        None,
        Click,
        Confirm,
        Back,
        Select,
    }

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        if (root == null) return;
        root.RegisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
        root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
    }

    private void OnDisable()
    {
        if (root == null) return;
        root.UnregisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
        root.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        root = null;
        hovered = null;
    }

    private void OnPointerOver(PointerOverEvent evt)
    {
        VisualElement element = Clickable(evt.target as VisualElement);
        if (element == hovered) return;
        hovered = element;
        if (element != null && element.enabledInHierarchy && sounds != null) sounds.uiHover?.Play2D();
    }

    private void OnClick(ClickEvent evt)
    {
        VisualElement element = Clickable(evt.target as VisualElement);
        if (element == null || !element.enabledInHierarchy || sounds == null) return;
        SoundSet sound = KindOf(element) switch
        {
            Kind.Confirm => sounds.uiConfirm,
            Kind.Back => sounds.uiBack,
            Kind.Select => sounds.uiSelect,
            _ => sounds.uiClick,
        };
        sound?.Play2D();
    }

    /// <summary>L'élément cliquable qui contient `element` (lui-même ou un parent), ou null.</summary>
    private static VisualElement Clickable(VisualElement element)
    {
        for (VisualElement v = element; v != null; v = v.parent)
        {
            if (v is Button || v is Toggle || v is DropdownField || v.ClassListContains("color-swatch")) return v;
        }
        return null;
    }

    /// <summary>Le genre de son d'un élément cliquable, d'après ses classes USS. Fonction pure, testée.</summary>
    public static Kind KindOf(VisualElement element)
    {
        if (element == null) return Kind.None;
        if (element.ClassListContains("back-button") || element.ClassListContains("menu-button--quit")) return Kind.Back;
        if (element.ClassListContains("menu-button--primary")) return Kind.Confirm;
        if (element.ClassListContains("custom-weapon-row") || element.ClassListContains("loadout-card")
            || element.ClassListContains("color-swatch") || element.ClassListContains("gunsmith-tab")
            || element.ClassListContains("gunsmith-tile") || element.ClassListContains("gunsmith-choice")) return Kind.Select;
        return Kind.Click;
    }
}
