using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Ce que le HUD montre de la tribune (2026-10-13), à côté de GameHud sur /GameUI :
/// - chez un SPECTATEUR : sa place dans la file (« prochain duel : toi »), ce qu'il tient, ses
///   commandes, l'élan de son lancer, l'invite pour ramasser ce qu'il vise, et l'étourdissement ;
/// - chez un DUELLISTE : l'invite pour prendre une arme lancée par un spectateur, à ses pieds.
///
/// Catégorie C pure : il lit l'état publié par le serveur (rôle, objet tenu, étourdissement) et la
/// prédiction du joueur (élan, objet visé), et ne décide rien. Les touches affichées sont celles du
/// joueur, remappage compris.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorHud : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    private Label interactPrompt;
    private VisualElement chargeBar;
    private VisualElement chargeFill;
    private VisualElement spectatorPanel;
    private Label roleLabel;
    private Label carryLabel;
    private Label hintsLabel;
    private VisualElement stunOverlay;
    private Label stunLabel;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        interactPrompt = root.Q<Label>("interactPrompt");
        chargeBar = root.Q<VisualElement>("chargeBar");
        chargeFill = root.Q<VisualElement>("chargeFill");
        spectatorPanel = root.Q<VisualElement>("spectatorPanel");
        roleLabel = root.Q<Label>("spectatorRole");
        carryLabel = root.Q<Label>("spectatorCarry");
        hintsLabel = root.Q<Label>("spectatorHints");
        stunOverlay = root.Q<VisualElement>("stunOverlay");
        stunLabel = root.Q<Label>("stunLabel");
    }

    private void LateUpdate()
    {
        NetworkObject local = GameHud.LocalPlayerObject;
        RoundManager rm = RoundManager.Instance;
        bool inGame = local != null && rm != null && rm.IsSpawned && !MatchEndScreen.IsShown && !PauseMenu.IsOpen;
        PlayerMatchState state = inGame ? local.GetComponent<PlayerMatchState>() : null;
        PlayerInputReader input = inGame ? local.GetComponent<PlayerInputReader>() : null;
        bool spectating = state != null && state.IsSpectator;

        if (!inGame || state == null || input == null)
        {
            HideAll();
            return;
        }

        if (spectating) ShowSpectator(rm, state, input, local.GetComponent<SpectatorController>());
        else ShowDuelist(input, local.GetComponentInChildren<WeaponController>());
    }

    private void HideAll()
    {
        SetVisible(interactPrompt, false);
        SetVisible(chargeBar, false);
        SetVisible(spectatorPanel, false);
        SetVisible(stunOverlay, false);
    }

    // ------------------------------------------------------------------
    // Le duelliste : une arme lancée à ses pieds
    // ------------------------------------------------------------------

    private void ShowDuelist(PlayerInputReader input, WeaponController weapon)
    {
        SetVisible(chargeBar, false);
        SetVisible(spectatorPanel, false);
        SetVisible(stunOverlay, false);
        ArenaItem candidate = weapon != null ? weapon.PowerUpCandidate : null;
        bool show = candidate != null && !GameHud.HidesCrosshair;
        SetVisible(interactPrompt, show);
        if (show) interactPrompt.text = $"{input.KeyFor("Interact")} : prendre {ItemName(candidate)} (chargeur plein)";
    }

    // ------------------------------------------------------------------
    // Le spectateur
    // ------------------------------------------------------------------

    private void ShowSpectator(RoundManager rm, PlayerMatchState state, PlayerInputReader input, SpectatorController spectator)
    {
        bool hidden = GameHud.HidesCrosshair; // un écran a la main, ou le noir est tombé
        SetVisible(spectatorPanel, !hidden);
        bool interventions = rm.Rules.spectatorInterventions;

        int place = state.QueuePosition;
        string queue = place == 1 ? "prochain duel : toi"
                     : place > 1 ? $"{place}e dans la file"
                     : "en attente d'une place";
        roleLabel.text = interventions ? $"SPECTATEUR · {queue}" : $"SPECTATEUR · {queue} · interventions désactivées";

        if (spectator == null || !interventions)
        {
            carryLabel.text = "";
            hintsLabel.text = "Regarde le duel : ton tour viendra.";
            SetVisible(interactPrompt, false);
            SetVisible(chargeBar, false);
            SetVisible(stunOverlay, false);
            return;
        }

        string punch = $"{input.KeyFor("Melee")} : coup de poing";
        string fire = input.KeyFor("Fire");
        ArenaItemKind carried = spectator.ShownCarried;
        if (carried == ArenaItemKind.None)
        {
            carryLabel.text = "Mains nues";
            hintsLabel.text = $"{fire} ou {punch}  ·  {input.KeyFor("Interact")} : ramasser  ·  frappe un bloc fissuré pour en arracher un caillou";
        }
        else
        {
            carryLabel.text = $"En main : {CarriedName(carried, spectator.CarriedVariant)}";
            hintsLabel.text = SpectatorController.ThrowPhaseAllowed
                ? $"Maintenir {fire} : élan, relâcher : lancer  ·  {input.KeyFor("Aim")} : annuler  ·  {punch}"
                : $"On lance pendant la manche  ·  {punch}";
        }

        // L'objet visé, à ramasser.
        ArenaItem candidate = spectator.PickupCandidate;
        bool prompt = !hidden && candidate != null && carried == ArenaItemKind.None;
        SetVisible(interactPrompt, prompt);
        if (prompt) interactPrompt.text = $"{input.KeyFor("Interact")} : ramasser {ItemName(candidate)}";

        // L'élan du lancer.
        bool charging = !hidden && spectator.IsCharging;
        SetVisible(chargeBar, charging);
        if (charging) chargeFill.style.width = Length.Percent(100f * spectator.ChargeProgress);

        // Étourdi : l'écran se voile, le mot clignote, les secondes restantes.
        bool stunned = spectator.IsStunnedNow;
        SetVisible(stunOverlay, stunned);
        if (stunned)
        {
            stunOverlay.style.opacity = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
            stunLabel.text = $"ÉTOURDI  {Mathf.CeilToInt(spectator.StunRemaining)}";
        }
    }

    private static string ItemName(ArenaItem item)
    {
        if (item == null) return "l'objet";
        if (item.Kind == ArenaItemKind.Rock) return "le caillou";
        return item.Catalog != null ? $"le {item.Catalog.NameOf(item.Variant)}" : "l'arme";
    }

    private static string CarriedName(ArenaItemKind kind, int variant)
    {
        if (kind == ArenaItemKind.Rock) return "un caillou";
        Tribune tribune = Tribune.Instance;
        string name = tribune != null && tribune.Catalog != null ? tribune.Catalog.NameOf(variant) : "une arme";
        return $"{name} (à lancer à un duelliste)";
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element != null) element.EnableInClassList("hidden", !visible);
    }
}
