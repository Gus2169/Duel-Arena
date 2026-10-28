using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Le menu d'Échap en partie : reprendre, options, quitter la partie (2026-10-12). Refait en UI
/// Toolkit le même jour, à la demande de l'utilisateur : les Options y sont (le même panneau que
/// celui du menu principal, OptionsPanel.uxml, piloté par OptionsController), et « Quitter le jeu »
/// n'y est plus.
///
/// Sur /GameUI de MultiTestScene, à côté de GameHud, et depuis le 2026-10-27 sur /Interface du stand de
/// tir (StandPause.uxml, demandé par l'utilisateur pour changer ses réglages au stand), où il ouvre aussi
/// la fenêtre des armes (ShootingRangePanel). Le jeu ne se met pas en pause (c'est une partie en
/// réseau) : menu ouvert, GameHud (ou la fenêtre du stand) libère le curseur et coupe les commandes du
/// joueur.
///
/// Échap ouvre et ferme le menu ; depuis les Options, il ramène au menu (en proposant d'appliquer ce qui
/// a changé, OptionsController.RequestLeave) ; au stand, il referme d'abord la fenêtre des armes. Pendant
/// un remappage de touche, Échap annule le remappage et ne fait rien d'autre.
/// </summary>
[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    /// <summary>Le menu d'Échap est-il ouvert sur cette machine ?</summary>
    public static bool IsOpen { get; private set; }

    private VisualElement pauseRoot;
    private VisualElement pauseMain;
    private VisualElement pauseOptions;
    private Button resumeButton;
    private Button optionsButton;
    private Button weaponsButton;
    private Button quitButton;
    private Button optionsBackButton;
    private Label inviteLabel;
    private OptionsController options;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        options = GetComponent<OptionsController>();
        VisualElement root = document.rootVisualElement;
        pauseRoot = root.Q<VisualElement>("pauseRoot");
        inviteLabel = root.Q<Label>("pauseInviteLabel");
        pauseMain = root.Q<VisualElement>("pauseMain");
        pauseOptions = root.Q<VisualElement>("pauseOptions");
        resumeButton = root.Q<Button>("pauseResumeButton");
        optionsButton = root.Q<Button>("pauseOptionsButton");
        weaponsButton = root.Q<Button>("pauseWeaponsButton");   // au stand seulement
        quitButton = root.Q<Button>("pauseQuitButton");
        optionsBackButton = root.Q<Button>("pauseOptionsBackButton");

        resumeButton.clicked += Close;
        optionsButton.clicked += ShowOptions;
        optionsBackButton.clicked += LeaveOptions;
        quitButton.clicked += QuitMatch;
        if (weaponsButton != null) weaponsButton.clicked += OpenWeapons;
        Close();
    }

    private void OnDisable()
    {
        resumeButton.clicked -= Close;
        optionsButton.clicked -= ShowOptions;
        optionsBackButton.clicked -= LeaveOptions;
        quitButton.clicked -= QuitMatch;
        if (weaponsButton != null) weaponsButton.clicked -= OpenWeapons;
        IsOpen = false;
    }

    private void Update()
    {
        // Pas de joueur (session arrêtée, robot pas encore apparu) : pas de menu.
        if (GameHud.LocalPlayerObject == null)
        {
            if (IsOpen) Close();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || OptionsController.AnyRebinding) return;

        // Au stand, la fenêtre des armes se referme d'abord (Échap est lu ici seul).
        if (!IsOpen && ShootingRangePanel.IsOpen)
        {
            ShootingRangePanel.Close();
            return;
        }
        if (!IsOpen) Open();
        else if (!pauseOptions.ClassListContains("hidden"))
        {
            // La fenêtre « Appliquer les réglages ? » se referme d'abord : on reste dans les Options.
            if (options == null || !options.CloseLeaveModal()) LeaveOptions();
        }
        else Close();
    }

    private void Open()
    {
        IsOpen = true;
        pauseRoot.RemoveFromClassList("hidden");

        // Le code de la partie en ligne (ou l'adresse de l'hôte en réseau local) : de quoi inviter
        // quelqu'un sans quitter la partie (2026-10-13). Rien au stand.
        string invite = GameHud.InviteLine();
        if (inviteLabel != null)
        {
            inviteLabel.text = invite ?? "";
            inviteLabel.EnableInClassList("hidden", string.IsNullOrEmpty(invite));
        }
        ShowMain();
    }

    private void Close()
    {
        IsOpen = false;
        pauseRoot.AddToClassList("hidden");
    }

    private void ShowMain()
    {
        pauseMain.RemoveFromClassList("hidden");
        pauseOptions.AddToClassList("hidden");
    }

    private void ShowOptions()
    {
        pauseMain.AddToClassList("hidden");
        pauseOptions.RemoveFromClassList("hidden");
        if (options != null) options.OnShown();
    }

    /// <summary>Revenir des Options au menu, après avoir proposé d'appliquer ce qui a changé.</summary>
    private void LeaveOptions()
    {
        if (options != null) options.RequestLeave(ShowMain);
        else ShowMain();
    }

    /// <summary>Au stand : la fenêtre des armes et des accessoires, à la place de ce menu.</summary>
    private void OpenWeapons()
    {
        Close();
        ShootingRangePanel.Open();
    }

    private void QuitMatch()
    {
        Close();
        SessionFlow.ReturnToMenu("");
    }
}
