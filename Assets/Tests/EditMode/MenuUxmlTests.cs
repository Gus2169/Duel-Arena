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
            "lobbyCapacityCycler", "lobbyKingToggle", "lobbyInterventionsToggle", "lobbyRandomToggle", "lobbyCodeLabel",
            "lobbyCopyLabel", "lobbyStatusLabel", "lobbyCopyButton", "lobbyStartButton", "lobbyLeaveButton");
        // Ce qui se dit oui ou non : de vraies cases à cocher (2026-10-27).
        foreach (string check in new[] { "lobbyKingToggle", "lobbyInterventionsToggle", "lobbyRandomToggle" })
            Assert.IsInstanceOf<Toggle>(root.Q(check), check);
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

    /// <summary>Le menu de pause du stand de tir (2026-10-27) : le même que celui de la partie, les Options
    /// comprises, et le bouton de la fenêtre des armes.</summary>
    [Test]
    public void LaPauseDuStand_PorteLesOptionsEtLaFenetreDesArmes()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/StandPause.uxml");
        AssertNames(root, "PauseMenu", "pauseRoot", "pauseMain", "pauseOptions", "pauseResumeButton",
            "pauseWeaponsButton", "pauseOptionsButton", "pauseQuitButton", "pauseOptionsBackButton");
        Assert.IsTrue(root.Q("pauseRoot").ClassListContains("hidden"), "Fermé au départ.");
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
            "salonStatus", "salonRoundsCycler", "salonCapacityCycler", "salonKingToggle", "salonInterventionsToggle",
            "salonRandomToggle", "salonLaunchButton", "salonQuitButton");
        foreach (string cycler in new[] { "salonRoundsCycler", "salonCapacityCycler" })
            Assert.IsInstanceOf<DuelArena.UI.OptionCycler>(root.Q(cycler), $"{cycler} : l'espace de noms du HUD (xmlns:da).");
        foreach (string check in new[] { "salonKingToggle", "salonInterventionsToggle", "salonRandomToggle" })
            Assert.IsInstanceOf<Toggle>(root.Q(check), check);
    }

    /// <summary>La vue d'un robot touché par un caillou (2026-10-27) : l'élément que cherche HitIndicatorHud, un
    /// vrai HitIndicatorOverlay, et caché tant que rien ne l'a touché.</summary>
    [Test]
    public void LeHud_PorteLIndicateurDesCaillouxRecus()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        VisualElement indicator = root.Q("hitIndicator");
        Assert.IsInstanceOf<DuelArena.UI.HitIndicatorOverlay>(indicator, "HitIndicatorHud cherche « hitIndicator » : l'espace de noms du HUD (xmlns:da).");
        Assert.IsTrue(indicator.ClassListContains("hidden"), "Caché au départ.");
        Assert.AreEqual(PickingMode.Ignore, indicator.pickingMode, "Il ne prend jamais la souris.");
    }

    [Test]
    public void LeHudDeCombat_PorteChaqueElementQueCrosshairUICherche()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/CombatHud.uxml");
        AssertNames(root, "CrosshairUI",
            "combatHud", "damageStatic", "scope", "reticle", "emptyHint", "ammoPanel", "weaponName", "ammoRow",
            "ammoValue", "ammoSize", "heatRow", "heatLabel", "heatFill");
        Assert.IsInstanceOf<DuelArena.UI.CombatReticle>(root.Q("reticle"));
        Assert.IsInstanceOf<DuelArena.UI.ScopeOverlay>(root.Q("scope"));
        Assert.IsInstanceOf<DuelArena.UI.DamageStaticOverlay>(root.Q("damageStatic"));
    }

    private static void AssertOptionsPanel(VisualElement root)
    {
        AssertNames(root, "OptionsController",
            "tabGameplayButton", "tabAudioButton", "tabGraphicsButton", "tabControlsButton", "pageGameplay",
            "pageAudio", "pageGraphics", "pageControls", "sensitivitySlider", "sensitivityValue", "invertYToggle",
            "masterVolumeSlider", "musicVolumeSlider", "masterVolumeValue", "musicVolumeValue", "resolutionCycler",
            "fullscreenToggle", "vsyncToggle", "frameRateCycler", "renderStyleCycler", "bindRowScroll", "resetBindingsButton",
            "optionsApplyButton", "optionsRevertButton", "optionsPendingLabel", "optionsLeaveModal",
            "optionsLeaveApplyButton", "optionsLeaveDiscardButton");
        Assert.IsInstanceOf<DuelArena.UI.OptionCycler>(root.Q("resolutionCycler"));
        foreach (string check in new[] { "invertYToggle", "fullscreenToggle", "vsyncToggle" })
            Assert.IsInstanceOf<Toggle>(root.Q(check), $"{check} : une case à cocher (2026-10-27).");
        Assert.IsTrue(root.Q("optionsLeaveModal").ClassListContains("hidden"), "La fenêtre de sortie est fermée au départ.");
    }
}
