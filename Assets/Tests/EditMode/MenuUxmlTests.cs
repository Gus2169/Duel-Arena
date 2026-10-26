using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// Les contrôleurs du menu et de l'interface de la partie retrouvent leurs éléments par nom (CLAUDE.md) : un
/// élément renommé ou oublié dans un UXML laisse un écran mort, sans erreur à la compilation. Ces tests
/// instancient les vrais UXML et y cherchent chaque nom que les contrôleurs demandent (menu refait le
/// 2026-10-17).
/// </summary>
public class MenuUxmlTests
{
    private static VisualElement Clone(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Assert.IsNotNull(asset, path);
        return asset.Instantiate();
    }

    private static void AssertNames(VisualElement root, string owner, params string[] names)
    {
        foreach (string name in names)
            Assert.IsNotNull(root.Q(name), $"{owner} cherche « {name} », absent de l'UXML.");
    }

    [Test]
    public void LeMenu_PorteChaqueElementQueSesControleursCherchent()
    {
        VisualElement root = Clone("Assets/_ProjectUI/MainMenu/MainMenu.uxml");
        AssertNames(root, "MainMenuController",
            "screenTitle", "screenPlay", "screenLobby", "screenCustomize", "screenOptions",
            "backdrop", "backdropDim", "versionLabel", "playButton", "armoryButton", "optionsButton", "quitButton",
            "playBackButton", "customBackButton", "optionsBackButton", "quitModal", "pseudoModal", "pseudoField",
            "pseudoModalLogo", "quitCancelButton", "quitBackHint", "quitConfirmButton", "pseudoOkButton",
            "joinCodeButton", "joinCodeField", "hostPortField", "joinIpField", "joinPortField", "hostButton",
            "joinButton", "joinButtonLabel", "localIpLabel", "lanRulesLabel", "playStatusLabel");
        AssertNames(root, "PlayScreenController",
            "playTabModes", "playTabLobbies", "playTabTraining", "playTabRanking", "playPageModes", "playPageLobbies",
            "playPageTraining", "modeGrid", "trainingGrid", "playDetailIcon", "playDetailTitle", "playDetailText",
            "playDetailImage", "playDetailFacts", "playDetailButton", "playDetailButtonLabel");
        AssertNames(root, "LobbyController",
            "lobbyHeadline", "lobbySubline", "lobbySpinner", "duelistCard0", "duelistCard1", "duelistPortrait0",
            "duelistPortrait1", "duelistUnknown0", "duelistUnknown1", "duelistRole0", "duelistRole1", "duelistName0",
            "duelistName1", "duelistStatus0", "duelistStatus1", "lobbyQueueTitle", "lobbyQueueHint", "lobbyQueueList",
            "lobbyArenaName", "lobbyMap", "lobbyMapLegend", "lobbyModeValue", "lobbyRoundsCycler", "lobbyRulesHint",
            "lobbyCapacityCycler", "lobbyOrganizationCycler", "lobbyInterventionsCycler", "lobbyCodeLabel",
            "lobbyCopyLabel", "lobbyStatusLabel", "lobbyCopyButton", "lobbyStartButton", "lobbyLeaveButton");
        AssertNames(root, "ArmoryController",
            "customTabRobot", "customTabWeapons", "customPageRobot", "customPageWeapons", "customHintsRobot",
            "customHintsWeapons", "robotCategories", "robotGrid", "robotGridTitle", "robotInfoName", "robotInfoTitle",
            "robotInfoText", "characterPreviewImage");
        AssertNames(root, "WeaponCustomizationController",
            "gunsmithPreview", "weaponList", "slotRow", "slotStrip", "slotStripTitle", "slotStripCount",
            "slotStripScroll", "slotStripPrev", "slotStripNext", "weaponModelLabel", "weaponClassLabel",
            "weaponDescription", "gunsmithStatRows", "gunsmithInfoGrid", "gunsmithStatsLegend", "gunsmithAiHeader",
            "gunsmithAiText");
        AssertOptionsPanel(root);

        // Les éléments sur mesure : un espace de noms mal écrit en UXML les remplacerait par rien.
        Assert.IsInstanceOf<DuelArena.UI.ArenaMapView>(root.Q("lobbyMap"));
        Assert.IsInstanceOf<DuelArena.UI.OptionCycler>(root.Q("lobbyCapacityCycler"));
        Assert.IsInstanceOf<DuelArena.UI.Spinner>(root.Q("lobbySpinner"));
        Assert.Greater(root.Query<Button>(className: "player-card").ToList().Count, 3, "La carte du joueur, sur chaque écran.");
    }

    [Test]
    public void LeMenuDEchap_PorteLesOptionsEtSesBoutons()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "PauseMenu", "pauseRoot", "pauseInviteLabel", "pauseMain", "pauseOptions",
            "pauseResumeButton", "pauseOptionsButton", "pauseQuitButton", "pauseOptionsBackButton");
        AssertOptionsPanel(root);
    }

    /// <summary>L'écran de fin et son vote, et le salon de la partie (2026-10-26) : les éléments que
    /// cherchent MatchEndScreen et SalonPanel, et les sélecteurs du salon, de vrais OptionCycler.</summary>
    [Test]
    public void LEcranDeFin_EtLeSalon_PortentLeursElements()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "MatchEndScreen", "endScreen", "endTitle", "endSubtitle", "scoreTable", "endCountdown",
            "endQuitButton", "endVote", "endVoteStatus", "endVoteButtons", "endRematchButton", "endSalonButton");
        AssertNames(root, "SalonPanel", "salonPanel", "salonPlayers", "salonMode", "salonRulesHint", "salonInvite",
            "salonStatus", "salonRoundsCycler", "salonCapacityCycler", "salonOrganizationCycler",
            "salonInterventionsCycler", "salonLaunchButton", "salonQuitButton");
        foreach (string cycler in new[] { "salonRoundsCycler", "salonCapacityCycler", "salonOrganizationCycler", "salonInterventionsCycler" })
            Assert.IsInstanceOf<DuelArena.UI.OptionCycler>(root.Q(cycler), $"{cycler} : l'espace de noms du HUD (xmlns:da).");
    }

    private static void AssertOptionsPanel(VisualElement root)
    {
        AssertNames(root, "OptionsController",
            "tabGameplayButton", "tabAudioButton", "tabGraphicsButton", "tabControlsButton", "pageGameplay",
            "pageAudio", "pageGraphics", "pageControls", "sensitivitySlider", "sensitivityValue", "invertYCycler",
            "masterVolumeSlider", "musicVolumeSlider", "masterVolumeValue", "musicVolumeValue", "resolutionCycler",
            "fullscreenCycler", "vsyncCycler", "frameRateCycler", "bindRowScroll", "resetBindingsButton");
        Assert.IsInstanceOf<DuelArena.UI.OptionCycler>(root.Q("invertYCycler"));
    }
}
