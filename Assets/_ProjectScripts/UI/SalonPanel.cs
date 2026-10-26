using System.Collections.Generic;
using DuelArena.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Le salon de la partie (2026-10-26, demandé par l'utilisateur) : après un match sans revanche, tout le
/// monde y revient, dans l'arène (RoundManager.Phase.Salon). L'hôte y retrouve les réglages de la partie
/// (manches, places, duels, interventions) et la relance ; les autres l'y attendent, avec les mêmes
/// réglages sous les yeux et qui jouera le match suivant. Le code de la partie (ou l'adresse en réseau
/// local, chez l'hôte) reste affiché, pour inviter.
///
/// Chez l'hôte, qui est le serveur, les réglages et la relance passent directement par RoundManager
/// (ServerSetRules, ServerLaunchFromSalon). Catégorie C : chez les autres, il ne fait que lire l'état
/// publié. Posé sur /GameUI, avec l'UIDocument du HUD.
/// </summary>
[DisallowMultipleComponent]
public class SalonPanel : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    /// <summary>Le salon est-il affiché ? (le curseur est alors libre, voir GameHud)</summary>
    public static bool IsShown { get; private set; }

    // L'ordre des choix du sélecteur des duels (GameHud.uxml).
    private static readonly DuelOrganization[] Organizations = { DuelOrganization.KingOfTheHill, DuelOrganization.FixedDuelists };

    private VisualElement panel;
    private VisualElement players;
    private Label modeLabel, hintLabel, inviteLabel, statusLabel;
    private OptionCycler roundsCycler, capacityCycler, organizationCycler, interventionsCycler;
    private Button launchButton, quitButton;
    private bool shown;
    private int shownKey = int.MinValue;
    private int capacityFloor = MatchRules.MinCapacity;
    private readonly List<PlayerMatchState> ordered = new List<PlayerMatchState>();
    private readonly List<string> capacities = new List<string>();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        panel = root.Q<VisualElement>("salonPanel");
        players = root.Q<VisualElement>("salonPlayers");
        modeLabel = root.Q<Label>("salonMode");
        hintLabel = root.Q<Label>("salonRulesHint");
        inviteLabel = root.Q<Label>("salonInvite");
        statusLabel = root.Q<Label>("salonStatus");
        roundsCycler = root.Q<OptionCycler>("salonRoundsCycler");
        capacityCycler = root.Q<OptionCycler>("salonCapacityCycler");
        organizationCycler = root.Q<OptionCycler>("salonOrganizationCycler");
        interventionsCycler = root.Q<OptionCycler>("salonInterventionsCycler");
        launchButton = root.Q<Button>("salonLaunchButton");
        quitButton = root.Q<Button>("salonQuitButton");

        roundsCycler.RegisterValueChangedCallback(OnRoundsChanged);
        capacityCycler.RegisterValueChangedCallback(OnCapacityChanged);
        organizationCycler.RegisterValueChangedCallback(OnOrganizationChanged);
        interventionsCycler.RegisterValueChangedCallback(OnInterventionsChanged);
        launchButton.clicked += Launch;
        quitButton.clicked += Quit;
        shown = false;
        shownKey = int.MinValue;
    }

    private void OnDisable()
    {
        if (roundsCycler != null) roundsCycler.UnregisterValueChangedCallback(OnRoundsChanged);
        if (capacityCycler != null) capacityCycler.UnregisterValueChangedCallback(OnCapacityChanged);
        if (organizationCycler != null) organizationCycler.UnregisterValueChangedCallback(OnOrganizationChanged);
        if (interventionsCycler != null) interventionsCycler.UnregisterValueChangedCallback(OnInterventionsChanged);
        if (launchButton != null) launchButton.clicked -= Launch;
        if (quitButton != null) quitButton.clicked -= Quit;
        IsShown = false;
    }

    private void Update()
    {
        RoundManager rm = RoundManager.Instance;
        bool show = rm != null && rm.IsSpawned && GameHud.LocalPlayerObject != null && rm.CurrentPhase == RoundManager.Phase.Salon;
        if (show != shown)
        {
            shown = show;
            panel.EnableInClassList("hidden", !show);
            shownKey = int.MinValue;
        }
        IsShown = show;
        if (show) Render(rm);
    }

    /// <summary>Cette machine héberge-t-elle la partie ? (l'hôte est le serveur)</summary>
    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>Refait le salon quand ce qu'il montre change (ses textes allouaient sinon à chaque image).</summary>
    private void Render(RoundManager rm)
    {
        MatchRules rules = rm.Rules.Sanitized();
        bool host = IsHost;
        int key = (host ? 1 : 0) + 2 * (rm.CanLaunchFromSalon ? 1 : 0);
        key = key * 31 + rules.capacity;
        key = key * 31 + (int)rules.organization;
        key = key * 31 + (rules.spectatorInterventions ? 1 : 0);
        key = key * 31 + rules.roundsToWin;
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state == null) continue;
            key = key * 31 + (int)state.OwnerClientId;
            key = key * 31 + (int)state.Role * 8 + state.QueuePosition;
            key = key * 31 + state.DisplayName.GetHashCode();
            if (state.TryGetComponent(out PlayerPaint paint)) key = key * 31 + paint.Paint.GetHashCode();
        }
        if (key == shownKey) return;
        shownKey = key;

        modeLabel.text = $"{GameModeCatalog.Describe(rules)} · {rules.Format}";
        BuildPlayers();

        roundsCycler.SetValueWithoutNotify(rules.roundsToWin == MatchRules.Bo3RoundsToWin ? 1 : 0);
        // On ne réduit pas les places sous le nombre de joueurs déjà là : les choix commencent à ce nombre.
        capacityFloor = Mathf.Clamp(PlayerMatchState.Spawned.Count, MatchRules.MinCapacity, MatchRules.MaxCapacity);
        capacities.Clear();
        for (int c = capacityFloor; c <= MatchRules.MaxCapacity; c++) capacities.Add(c.ToString());
        capacityCycler.Choices = capacities;
        capacityCycler.SetValueWithoutNotify(Mathf.Max(0, rules.capacity - capacityFloor));
        organizationCycler.SetValueWithoutNotify(System.Array.IndexOf(Organizations, rules.organization));
        interventionsCycler.SetValueWithoutNotify(rules.spectatorInterventions ? 1 : 0);
        roundsCycler.SetEnabled(host);
        capacityCycler.SetEnabled(host);
        organizationCycler.SetEnabled(host);
        interventionsCycler.SetEnabled(host);
        hintLabel.text = host
            ? "Tes changements valent pour le match suivant, chez tout le monde."
            : "Seul l'hôte change les réglages.";

        inviteLabel.text = GameHud.InviteLine() ?? "";
        launchButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
        launchButton.SetEnabled(rm.CanLaunchFromSalon);
        statusLabel.text = !host ? "En attente de l'hôte…"
            : rm.CanLaunchFromSalon ? "Lance la partie quand tout le monde est prêt."
            : "Il faut deux joueurs pour lancer la partie.";
    }

    /// <summary>Les joueurs : les duellistes du match suivant, puis la tribune dans l'ordre de la file.</summary>
    private void BuildPlayers()
    {
        ordered.Clear();
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && state.IsDuelist) ordered.Add(state);
        }
        int duelists = ordered.Count;
        foreach (PlayerMatchState state in PlayerMatchState.Spawned)
        {
            if (state != null && !state.IsDuelist) ordered.Add(state);
        }
        ordered.Sort(duelists, ordered.Count - duelists, Comparer<PlayerMatchState>.Create((a, b) => a.QueuePosition.CompareTo(b.QueuePosition)));

        players.Clear();
        foreach (PlayerMatchState state in ordered) players.Add(PlayerRow(state));
    }

    private static VisualElement PlayerRow(PlayerMatchState state)
    {
        var row = new VisualElement();
        row.AddToClassList("salon-player");
        var swatch = new VisualElement();
        swatch.AddToClassList("salon-player__swatch");
        if (state.TryGetComponent(out PlayerPaint paint) && paint.Paint.a > 0) swatch.style.backgroundColor = (Color)paint.Paint;
        else swatch.style.backgroundColor = new Color(0.5f, 0.52f, 0.55f);
        row.Add(swatch);

        bool hostPlayer = state.OwnerClientId == NetworkManager.ServerClientId;
        var name = new Label(hostPlayer ? $"{state.DisplayName} (hôte)" : state.DisplayName);
        name.AddToClassList("salon-player__name");
        row.Add(name);
        var role = new Label(RoleText(state.IsDuelist, state.QueuePosition));
        role.AddToClassList("salon-player__role");
        row.Add(role);
        return row;
    }

    /// <summary>Le rôle au match suivant, en toutes lettres. Fonction pure.</summary>
    public static string RoleText(bool duelist, int queuePosition)
    {
        if (duelist) return "Duelliste";
        if (queuePosition <= 0) return "Tribune";
        return queuePosition == 1 ? "Tribune · 1er à entrer" : $"Tribune · {queuePosition}e à entrer";
    }

    private static void ChangeRules(System.Func<MatchRules, MatchRules> change)
    {
        RoundManager rm = RoundManager.Instance;
        if (!IsHost || rm == null || rm.CurrentPhase != RoundManager.Phase.Salon) return;
        rm.ServerSetRules(change(rm.Rules.Sanitized()));
    }

    private void OnRoundsChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.roundsToWin = (byte)(evt.newValue == 1 ? MatchRules.Bo3RoundsToWin : MatchRules.Bo5RoundsToWin); return r; });

    private void OnCapacityChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.capacity = (byte)(capacityFloor + evt.newValue); return r; });

    private void OnOrganizationChanged(ChangeEvent<int> evt)
    {
        if (evt.newValue < 0 || evt.newValue >= Organizations.Length) return;
        DuelOrganization chosen = Organizations[evt.newValue];
        ChangeRules(r => { r.organization = chosen; return r; });
    }

    private void OnInterventionsChanged(ChangeEvent<int> evt)
        => ChangeRules(r => { r.spectatorInterventions = evt.newValue == 1; return r; });

    private void Launch()
    {
        RoundManager rm = RoundManager.Instance;
        if (IsHost && rm != null) rm.ServerLaunchFromSalon();
    }

    private void Quit()
    {
        SessionFlow.ReturnToMenu("");
    }
}
