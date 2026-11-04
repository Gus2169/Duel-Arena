using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Ce que le HUD montre de la tribune (2026-10-13), à côté de GameHud sur /GameUI :
/// - chez un SPECTATEUR : sa place dans la file (« prochain duel : toi »), ce qu'il a en main (ses
///   poings, ou l'objet tenu, rangé ou non), ses commandes, l'invite pour ramasser ce qu'il vise, et
///   l'étourdissement (la trajectoire d'un lancer qu'il vise se dessine dans le monde : ThrowArcPreview) ;
/// - chez un DUELLISTE : l'invite pour prendre une arme lancée par un spectateur, à ses pieds.
///
/// Catégorie C pure : il lit l'état publié par le serveur (rôle, objet tenu, étourdissement) et la
/// prédiction du joueur (objet visé), et ne décide rien. Les touches affichées sont celles du
/// joueur, remappage compris. Ses textes ne sont refaits que quand ce qu'ils disent change (TextsKey) : la
/// plupart des machines d'une partie à six sont des spectateurs, et l'hôte qui perd au roi de la colline en est un.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorHud : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    private Label interactPrompt;
    private VisualElement spectatorPanel;
    private Label roleLabel;
    private Label carryLabel;
    private Label hintsLabel;
    private VisualElement stunOverlay;
    private Label stunLabel;

    // Le robot de cette machine et ses composants, cherchés quand il change, comme HitIndicatorHud : l'arme vient après
    // la caméra dans le préfab, une recherche traverserait toute la vue subjective à chaque image.
    private NetworkObject followed;
    private PlayerMatchState state;
    private PlayerInputReader input;
    private SpectatorController spectator;
    private WeaponController weapon;

    // Les touches du joueur : les lire analyse leurs liaisons et alloue. Relues à chaque remappage ; `keysVersion` en
    // porte le changement dans les clés des textes.
    private bool keysStale;
    private int keysVersion;
    private string fireKey, aimKey, swapKey, interactKey;

    private TextsKey shownTexts;
    private bool textsWritten;
    private PromptKey shownPrompt;
    private int shownStunSeconds;

    /// <summary>Ce dont dépendent la place, ce qu'il a en main et ses commandes. `controls` : il a de quoi agir, le reste
    /// ne compte qu'alors. `keys` : la version de ses touches.</summary>
    public struct TextsKey : System.IEquatable<TextsKey>
    {
        public int place;
        public PlayerTeam team;
        public bool interventions;
        public bool controls;
        public ArenaItemKind carried;
        public int variant;
        public bool fistsOut;
        public bool throwAllowed;
        public bool aiming;
        public int keys;

        public bool Equals(TextsKey o) => place == o.place && team == o.team && interventions == o.interventions && controls == o.controls
                                          && carried == o.carried && variant == o.variant && fistsOut == o.fistsOut
                                          && throwAllowed == o.throwAllowed && aiming == o.aiming && keys == o.keys;
    }

    /// <summary>Ce dont dépend l'invite : l'objet, et pour qui.</summary>
    private struct PromptKey : System.IEquatable<PromptKey>
    {
        public ArenaItem item;
        public ArenaItemKind kind;
        public int variant;
        public bool duelist;
        public int keys;

        public bool Equals(PromptKey o) => item == o.item && kind == o.kind && variant == o.variant && duelist == o.duelist && keys == o.keys;
    }

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        interactPrompt = root.Q<Label>("interactPrompt");
        spectatorPanel = root.Q<VisualElement>("spectatorPanel");
        roleLabel = root.Q<Label>("spectatorRole");
        carryLabel = root.Q<Label>("spectatorCarry");
        hintsLabel = root.Q<Label>("spectatorHints");
        stunOverlay = root.Q<VisualElement>("stunOverlay");
        stunLabel = root.Q<Label>("stunLabel");
        // Les éléments viennent d'être retrouvés : leurs textes sont à refaire.
        textsWritten = false;
        shownPrompt = default;
        shownStunSeconds = int.MinValue;
        Follow(null);
        GameSettings.BindingsChanged += ForgetKeys;
    }

    private void OnDisable()
    {
        GameSettings.BindingsChanged -= ForgetKeys;
    }

    // Un remappage en pleine partie (le menu d'Échap) : les touches se relisent à l'image suivante, une fois le lecteur
    // du joueur à jour (il écoute le même évènement).
    private void ForgetKeys() => keysStale = true;

    private void Follow(NetworkObject local)
    {
        followed = local;
        state = local != null ? local.GetComponent<PlayerMatchState>() : null;
        input = local != null ? local.GetComponent<PlayerInputReader>() : null;
        spectator = local != null ? local.GetComponent<SpectatorController>() : null;
        weapon = local != null ? local.GetComponentInChildren<WeaponController>(true) : null;
        keysStale = true;
    }

    private void ReadKeys()
    {
        keysStale = false;
        keysVersion++;
        fireKey = input.KeyFor("Fire");
        aimKey = input.KeyFor("Aim");
        swapKey = input.KeyFor("SwapItem");
        interactKey = input.KeyFor("Interact");
    }

    private void LateUpdate()
    {
        NetworkObject local = GameHud.LocalPlayerObject;
        if (local != followed) Follow(local);
        RoundManager rm = RoundManager.Instance;
        bool inGame = local != null && rm != null && rm.IsSpawned && !MatchEndScreen.IsShown && !PauseMenu.IsOpen;

        if (!inGame || state == null || input == null)
        {
            HideAll();
            return;
        }

        if (keysStale) ReadKeys();
        if (state.IsSpectator) ShowSpectator(rm);
        else ShowDuelist();
    }

    private void HideAll()
    {
        SetVisible(interactPrompt, false);
        SetVisible(spectatorPanel, false);
        SetVisible(stunOverlay, false);
    }

    // ------------------------------------------------------------------
    // Le duelliste : une arme lancée à ses pieds
    // ------------------------------------------------------------------

    private void ShowDuelist()
    {
        SetVisible(spectatorPanel, false);
        SetVisible(stunOverlay, false);
        ArenaItem candidate = weapon != null ? weapon.PowerUpCandidate : null;
        bool show = candidate != null && !GameHud.HidesCrosshair;
        SetVisible(interactPrompt, show);
        if (show) ShowPrompt(candidate, true);
    }

    private void ShowPrompt(ArenaItem candidate, bool duelist)
    {
        var key = new PromptKey { item = candidate, kind = candidate.Kind, variant = candidate.Variant, duelist = duelist, keys = keysVersion };
        if (key.Equals(shownPrompt)) return;
        shownPrompt = key;
        interactPrompt.text = PromptLine(interactKey, ItemName(candidate), duelist);
    }

    // ------------------------------------------------------------------
    // Le spectateur
    // ------------------------------------------------------------------

    private void ShowSpectator(RoundManager rm)
    {
        bool hidden = GameHud.HidesCrosshair; // un écran a la main, ou le noir est tombé
        SetVisible(spectatorPanel, !hidden);
        bool interventions = rm.Rules.spectatorInterventions;
        bool controls = spectator != null && interventions;

        var texts = new TextsKey
        {
            place = state.QueuePosition,
            team = state.Team,
            interventions = interventions,
            controls = controls,
            keys = keysVersion,
        };
        if (controls)
        {
            texts.carried = spectator.ShownCarried;
            texts.variant = spectator.CarriedVariant;
            texts.fistsOut = spectator.FistsOut;
            texts.throwAllowed = SpectatorController.ThrowPhaseAllowed;
            texts.aiming = spectator.IsAimingThrow;
        }
        if (!textsWritten || !texts.Equals(shownTexts)) WriteTexts(texts);

        if (!controls)
        {
            SetVisible(interactPrompt, false);
            SetVisible(stunOverlay, false);
            return;
        }

        // L'objet visé, à ramasser.
        ArenaItem candidate = spectator.PickupCandidate;
        bool prompt = !hidden && candidate != null && texts.carried == ArenaItemKind.None;
        SetVisible(interactPrompt, prompt);
        if (prompt) ShowPrompt(candidate, false);

        // Étourdi : l'écran se voile, le mot clignote, les secondes restantes.
        bool stunned = spectator.IsStunnedNow;
        SetVisible(stunOverlay, stunned);
        if (!stunned) return;
        stunOverlay.style.opacity = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
        int seconds = Mathf.CeilToInt(spectator.StunRemaining);
        if (seconds == shownStunSeconds) return;
        shownStunSeconds = seconds;
        stunLabel.text = $"ÉTOURDI  {seconds}";
    }

    private void WriteTexts(in TextsKey texts)
    {
        textsWritten = true;
        shownTexts = texts;
        roleLabel.text = RoleLine(texts.team, texts.place, texts.interventions);
        carryLabel.text = texts.controls ? CarryLine(texts.carried, texts.fistsOut, CarriedName(texts.carried, texts.variant)) : "";
        hintsLabel.text = texts.controls
            ? HintsLine(texts.carried, texts.fistsOut, texts.throwAllowed, texts.aiming, fireKey, aimKey, swapKey, interactKey)
            : "Regarde le duel : ton tour viendra.";
    }

    /// <summary>Sa place : « SPECTATEUR · prochain duel : toi », « SPECTATEUR · 3e dans la file », et au Tag Team, pour celui
    /// qui attend son tour avec son équipe et aide d'ici (GDD § 11, TT5), « ÉQUIPE A · tu entres au 2e relais ». Fonction
    /// pure, testée.</summary>
    public static string RoleLine(PlayerTeam team, int place, bool interventions)
    {
        string queue = team != PlayerTeam.None ? (place <= 1 ? "tu entres au prochain relais" : $"tu entres au {place}e relais")
                     : place == 1 ? "prochain duel : toi"
                     : place > 1 ? $"{place}e dans la file"
                     : "en attente d'une place";
        string who = team != PlayerTeam.None ? TagTeam.Name(team).ToUpperInvariant() : "SPECTATEUR";
        return interventions ? $"{who} · {queue}" : $"{who} · {queue} · interventions désactivées";
    }

    /// <summary>Ce qu'il a en main : ses poings, l'objet rangé (la molette, 2026-10-27), ou l'objet. Fonction pure, testée.</summary>
    public static string CarryLine(ArenaItemKind carried, bool fistsOut, string carriedName)
    {
        if (carried == ArenaItemKind.None) return "Poings";
        return fistsOut ? $"Poings  ·  rangé : {carriedName}" : $"En main : {carriedName}";
    }

    /// <summary>Ses commandes, à ses touches (`fire`, `aim`, `swap`, `interact`) : se battre, ramasser, sortir l'objet rangé,
    /// le lancer. Fonction pure, testée.</summary>
    public static string HintsLine(ArenaItemKind carried, bool fistsOut, bool throwAllowed, bool aiming,
                                   string fire, string aim, string swap, string interact)
    {
        string fight = $"{fire} : coups de poing (gauche, droite)  ·  {aim} : garde";
        if (carried == ArenaItemKind.None) return $"{fight}  ·  {interact} : ramasser  ·  frappe un bloc fissuré pour en arracher un caillou";
        if (fistsOut) return $"{fight}  ·  {swap} : sortir {ShortName(carried)}";
        if (!throwAllowed) return $"On lance pendant la manche  ·  {swap} : les poings";
        return aiming ? $"{fire} : lancer  ·  relâcher {aim} : renoncer"
                      : $"Maintenir {aim} : viser (la trajectoire s'affiche), puis {fire} : lancer  ·  {swap} : les poings";
    }

    /// <summary>L'invite à ramasser `item`, à sa touche `key`. Fonction pure, testée.</summary>
    public static string PromptLine(string key, string item, bool duelist)
        => duelist ? $"{key} : prendre {item} (chargeur plein)" : $"{key} : ramasser {item}";

    private static string ItemName(ArenaItem item)
    {
        if (item == null) return "l'objet";
        if (item.Kind == ArenaItemKind.Rock) return "le caillou";
        return item.Catalog != null ? $"le {item.Catalog.NameOf(item.Variant)}" : "l'arme";
    }

    private static string ShortName(ArenaItemKind kind) => kind == ArenaItemKind.Rock ? "le caillou" : "l'arme";

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
