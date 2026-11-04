using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            "joinButton", "joinButtonLabel", "localIpLabel", "lanRulesLabel", "playStatusLabel",
            "sessionEndModal", "sessionEndText", "sessionEndOkButton", "loadingScreen");
        VisualElement loading = root.Q("loadingScreen");
        Assert.AreEqual(loading.parent.childCount - 1, loading.parent.IndexOf(loading), "Le chargement passe par-dessus tout le menu.");
        Assert.AreEqual(GameHud.LoadingTitle(false, false), root.Q<Label>("loadingScreenTitle").text, "Le même mot que dans le noir de l'arène.");
        AssertNames(root, "PlayScreenController",
            "playTabModes", "playTabLobbies", "playTabTraining", "playTabRanking", "playPageModes", "playPageLobbies",
            "playPageTraining", "modeGrid", "trainingGrid", "playDetailImage", "playDetailCaption", "playDetailTitle",
            "playDetailText", "playDetailFacts", "playDetailNote", "playDetailButton", "playDetailButtonLabel");
        AssertSalonView(root.Q("screenLobby"), "LobbyController");
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

        // La carte du joueur, sur chaque écran, sauf le salon : le code y a sa place (maquette « B · Salon »).
        Assert.AreEqual(3, root.Query<Button>(className: "player-card").ToList().Count, "Jouer, Personnalisation, Options.");
        Assert.IsNull(root.Q("screenLobby").Q(className: "player-card"));
    }

    [Test]
    public void LeMenuDEchap_PorteLesOptionsEtSesBoutons()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "PauseMenu", "pauseRoot", "pauseInviteLabel", "pauseMain", "pauseOptions",
            "pauseResumeButton", "pauseOptionsButton", "pauseQuitButton", "pauseOptionsBackButton");
        AssertOptionsPanel(root);
    }

    /// <summary>La Zone (2026-11-04) : les barres des points, l'objectif et les losanges des repères que cherche GameHud.</summary>
    [Test]
    public void LaZone_PorteSesBarresEtSesReperes()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "GameHud", "scoreGoal", "leftGauge", "leftGaugeFill", "rightGauge", "rightGaugeFill", "zoneStatus",
            "zoneMarker", "zoneMarkerIcon", "zoneMarkerHalo", "zoneMarkerLabel",
            "nextZoneMarker", "nextZoneMarkerIcon", "nextZoneMarkerHalo", "nextZoneMarkerLabel");
        Assert.IsTrue(root.Q("rightGauge").ClassListContains("score-gauge--mirror"), "À droite, la barre monte aussi vers le chrono.");
        Assert.IsTrue(root.Q("leftGauge").ClassListContains("hidden") && root.Q("scoreGoal").ClassListContains("hidden"), "Hors de la Zone, rien.");
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
    /// cherchent MatchEndScreen et SalonPanel.</summary>
    [Test]
    public void LEcranDeFin_EtLeSalon_PortentLeursElements()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "MatchEndScreen", "endScreen", "endTitle", "endSubtitle", "scoreTable", "endCountdown",
            "endQuitButton", "endVote", "endVoteStatus", "endVoteButtons", "endRematchButton", "endSalonButton");
        AssertNames(root, "SalonPanel", "salonPanel");
        AssertSalonView(root.Q("salonPanel"), "SalonPanel");
    }

    /// <summary>
    /// Le salon de l'arène montre la vue d'ensemble et le nom de l'arène, ceux du salon du menu (MenuArtwork), posés sur
    /// SalonPanel dans la scène : MenuArtwork et ses grandes images ne se chargent pas avec l'arène. Vus échouer : la scène
    /// qui citait encore MenuArtwork, puis sans le nom, puis sans l'image.
    /// </summary>
    [Test]
    public void LeSalonDeLArene_MontreLImageEtLeNomDeLArene_SansChargerLesImagesDuMenu()
    {
        const string scenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
        const string artworkPath = "Assets/_ProjectUI/Images/MenuArtwork.asset";
        var artwork = AssetDatabase.LoadAssetAtPath<MenuArtwork>(artworkPath);
        Assert.IsNotNull(artwork.arenaOverview, "Garde : l'image du salon du menu.");
        Assert.IsNotEmpty(artwork.arenaName, "Garde : le nom de l'arène.");
        CollectionAssert.DoesNotContain(AssetDatabase.GetDependencies(scenePath, true), artworkPath,
            "L'arène ne charge pas les images du menu.");

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            SalonPanel panel = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (panel == null) panel = root.GetComponentInChildren<SalonPanel>(true);
            Assert.IsNotNull(panel, "Garde : le salon est sur /GameUI.");
            var serialized = new SerializedObject(panel);
            var image = serialized.FindProperty("arenaImage").objectReferenceValue as Texture2D;
            string name = serialized.FindProperty("arenaName").stringValue;
            Assert.AreSame(artwork.arenaOverview, image, "L'image du salon du menu.");
            Assert.AreEqual(artwork.arenaName, name, "Le nom du salon du menu.");

            VisualElement hud = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
            var view = new SalonView(hud.Q("salonPanel"));
            view.SetArena(image, name);
            Assert.AreEqual(name, hud.Q<Label>("salonArenaName").text, "Le salon montre le nom.");
            Assert.AreSame(image, hud.Q("salonArenaImage").style.backgroundImage.value.texture, "Le salon montre l'image.");
            view.Dispose();
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Le salon (2026-11-02, maquette « B · Salon ») : le même gabarit au menu et dans l'arène. Chaque nom que
    /// SalonView cherche y est, et l'écran se remplit d'un salon à quatre places et trois joueurs : les trois
    /// joueurs puis une place libre, trois réglages d'emblée et trois sous « Plus de réglages », repliés.
    /// Chez un invité, ni le bouton du mode, ni le lancement, et les réglages ne se changent pas.
    /// </summary>
    private static void AssertSalonView(VisualElement host, string owner)
    {
        Assert.IsNotNull(host, owner);
        AssertNames(host, owner + " (SalonView)",
            "salonView", "salonEyebrow", "salonModeName", "salonChangeMode", "salonCodeBlock", "salonCodeCaption",
            "salonCode", "salonCopy", "salonCopyLabel", "salonPlayersTitle", "salonPlayerList", "salonPhrase",
            "salonEssentials", "salonMore", "salonMoreLabel", "salonAdvanced", "salonHint", "salonArenaImage",
            "salonArenaName", "salonLeave", "salonLeaveLabel", "salonStatus", "salonSummary", "salonLaunch",
            "salonModeWindow", "salonModeList", "salonModeClose", "salonEvening", "salonEveningTitle", "salonEveningList",
            "salonEveningLast");

        var rules = new MatchRules { capacity = 4, organization = DuelOrganization.KingOfTheHill, roundsToWin = MatchRules.Bo5RoundsToWin };
        var players = new System.Collections.Generic.List<SalonView.Player>
        {
            new SalonView.Player { name = "Faucon", role = SalonView.Role(true, 0, false, true, false) },
            new SalonView.Player { name = "Rivet", role = SalonView.Role(true, 0, false, false, true), local = true },
            new SalonView.Player { name = "Kilo", role = SalonView.Role(false, 1, false, false, false) },
        };
        var view = new SalonView(host);
        view.Show(new SalonView.Model
        {
            eyebrow = "SALON DE FAUCON · EN LIGNE", mode = GameModeId.Classic, rules = rules, host = false, editable = false,
            inviteCaption = "CODE", inviteValue = "K7M2QX", players = players, canLaunch = false,
        });
        Assert.AreEqual("CLASSIQUE", host.Q<Label>("salonModeName").text);
        Assert.AreEqual("LES JOUEURS · 3 SUR 4", host.Q<Label>("salonPlayersTitle").text);
        Assert.AreEqual(4, host.Q("salonPlayerList").childCount, "Trois joueurs et une place libre.");
        Assert.IsTrue(host.Q("salonPlayerList")[1].ClassListContains("salon-player--self"), "Sa propre rangée se voit.");
        Assert.IsTrue(host.Q("salonPlayerList")[3].ClassListContains("salon-player--empty"));
        Assert.AreEqual(3, host.Q("salonEssentials").childCount);
        Assert.AreEqual(5, host.Q("salonAdvanced").childCount, "Le Death match du classique, Tête seulement, les places, le tirage, la tribune.");
        Assert.IsTrue(host.Q("salonAdvanced").ClassListContains("hidden"), "Plus de réglages : replié d'abord.");
        Assert.AreEqual("PLUS DE RÉGLAGES (5)", host.Q<Label>("salonMoreLabel").text);
        Assert.IsTrue(host.Q("salonLaunch").ClassListContains("hidden"), "Seul l'hôte lance.");
        Assert.IsTrue(host.Q("salonChangeMode").ClassListContains("hidden"), "Seul l'hôte change de mode.");
        foreach (Button choice in host.Q("salonEssentials").Query<Button>(className: "salon-seg").ToList())
            Assert.IsFalse(choice.enabledSelf, "Chez un invité, rien ne se change.");
        Assert.AreEqual("K7M2QX", host.Q<Label>("salonCode").text);
        StringAssert.StartsWith("Classique · BO5", host.Q<Label>("salonSummary").text);
        Assert.IsTrue(host.Q("salonEvening").ClassListContains("hidden"), "Sans match fini, pas de tableau de la soirée.");

        // Le tableau de la soirée (GDD § 14), après trois matchs : une ligne par joueur, la sienne marquée, et le dernier.
        view.Show(new SalonView.Model
        {
            eyebrow = "SALON DE FAUCON · EN LIGNE", mode = GameModeId.Classic, rules = rules, host = false, editable = false,
            inviteCaption = "CODE", inviteValue = "K7M2QX", players = players, canLaunch = false,
            evening = new System.Collections.Generic.List<SalonView.EveningLine>
            {
                new SalonView.EveningLine { name = "Faucon", wins = Evening.WinsLabel(2), detail = "Classique 2 · sur 3 matchs" },
                new SalonView.EveningLine { name = "Rivet", wins = Evening.WinsLabel(1), detail = "Gun Game 1 · sur 3 matchs", local = true },
            },
            eveningMatches = 3,
            eveningLast = "Classique : Faucon bat Rivet, 2 – 1",
        });
        Assert.IsFalse(host.Q("salonEvening").ClassListContains("hidden"), "Le tableau de la soirée se montre.");
        Assert.AreEqual("LA SOIRÉE · 3 MATCHS", host.Q<Label>("salonEveningTitle").text);
        Assert.AreEqual(2, host.Q("salonEveningList").childCount);
        Assert.IsTrue(host.Q("salonEveningList")[1].ClassListContains("salon-evening--self"), "Sa propre ligne se voit.");
        Assert.AreEqual("Dernier match · Classique : Faucon bat Rivet, 2 – 1", host.Q<Label>("salonEveningLast").text);
        view.Dispose();
    }

    /// <summary>Le tableau du Tournoi (2026-11-03) : les éléments que cherche TournamentBoard, caché au départ, et qui ne
    /// prend jamais la souris (on joue dans la salle d'attente).</summary>
    [Test]
    public void LeHud_PorteLeTableauDuTournoi()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "TournamentBoard", "tournamentBoard", "tournamentStage", "tournamentMine", "tournamentStandings",
            "tournamentMatchesHeading", "tournamentMatches");
        VisualElement board = root.Q("tournamentBoard");
        Assert.IsTrue(board.ClassListContains("hidden"), "Caché hors de la salle d'attente.");
        Assert.AreEqual(PickingMode.Ignore, board.pickingMode, "Il ne prend jamais la souris.");
    }

    /// <summary>La légende de la kill cam (2026-11-04) : les éléments que cherche GameHud, cachés au départ, qui ne prennent
    /// jamais la souris, et sous le noir de la fin de manche.</summary>
    [Test]
    public void LeHud_PorteLaLegendeDeLaKillCam()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "GameHud", "killCamCaption", "killCamTitle", "killCamLine");
        VisualElement frame = root.Q("killCamCaption");
        Assert.IsTrue(frame.ClassListContains("hidden"), "Cachée hors de la kill cam.");
        Assert.AreEqual(PickingMode.Ignore, frame.pickingMode, "Elle ne prend jamais la souris.");
        Assert.AreEqual("LE COUP MORTEL", root.Q<Label>("killCamTitle").text);
        VisualElement fade = root.Q("fade");
        Assert.AreEqual(frame.parent, fade.parent);
        Assert.Less(frame.parent.IndexOf(frame), fade.parent.IndexOf(fade), "Le noir passe par-dessus le bandeau.");
    }

    /// <summary>Le chargement dans l'arène (2026-11-03) : ce que cherche GameHud, porté par le noir, qui l'emporte quand
    /// il se lève, et qui ne prend jamais la souris.</summary>
    [Test]
    public void LeHud_PorteLEcranDeChargement_DansLeNoir()
    {
        VisualElement root = Clone("Assets/_ProjectUI/Game/GameHud.uxml");
        AssertNames(root, "GameHud", "fade", "loadingBlock", "loadingTitle");
        VisualElement block = root.Q("loadingBlock");
        Assert.AreEqual(root.Q("fade"), block.parent, "Dans le noir : il se lève avec lui.");
        Assert.AreEqual(PickingMode.Ignore, block.pickingMode, "Il ne prend jamais la souris.");
        Assert.AreEqual(GameHud.LoadingTitle(false, false), root.Q<Label>("loadingTitle").text, "Le même mot que le menu.");
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
