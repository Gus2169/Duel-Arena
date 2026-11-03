using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Le panneau de préparation (2026-10-12, demandé par l'utilisateur) : avant chaque manche, un
/// onglet s'ouvre au centre de l'écran, chaque joueur choisit son arme parmi les douze (un clic) et
/// valide. Le décompte part dès que les deux ont validé, au plus tard au bout de 30 s
/// (RoundManager). Une fois la manche lancée, on garde son arme.
///
/// À pile ou face (GDD § 11, 2026-11-03) : la pièce (la première manche) ou la défaite désigne celui qui choisit la
/// famille des deux armes ; il la choisit ici (ou le gant seul), les autres l'attendent ; puis seules les armes de la
/// famille se choisissent.
///
/// Une VUE sur l'arme du joueur : choisir passe par le changement d'arme du joueur
/// (WeaponController.RequestWeapon : prédit, puis contrôlé par le serveur), et valider demande au
/// serveur de marquer le joueur prêt (PlayerMatchState.RequestReady). Ce panneau ne décide rien.
/// </summary>
[DisallowMultipleComponent]
public class LoadoutPanel : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    /// <summary>Le panneau est-il ouvert ? (le curseur est alors libre, voir GameHud)</summary>
    public static bool IsShown { get; private set; }

    private VisualElement panel;
    private Label titleLabel;
    private Label coinLabel;
    private VisualElement familyRow;
    private readonly List<Button> familyButtons = new List<Button>();
    private string shownCoin;
    private Label timerLabel;
    private Label roundLabel;
    private Label statusLabel;
    private Button validateButton;
    private VisualElement cardRow;
    private readonly List<Button> cards = new List<Button>();
    private readonly List<int> cardSlots = new List<int>(); // la place de la panoplie de chaque carte
    private WeaponController builtFor;

    private WeaponController weapon;
    private PlayerMatchState state;

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        panel = root.Q<VisualElement>("loadoutPanel");
        titleLabel = root.Q<Label>("loadoutTitle");
        coinLabel = root.Q<Label>("loadoutCoin");
        familyRow = root.Q<VisualElement>("loadoutFamilies");
        BuildFamilyButtons();
        shownCoin = null;
        timerLabel = root.Q<Label>("loadoutTimer");
        roundLabel = root.Q<Label>("loadoutRound");
        statusLabel = root.Q<Label>("loadoutStatus");
        validateButton = root.Q<Button>("loadoutValidate");
        cardRow = root.Q<VisualElement>("loadoutCards");
        validateButton.clicked += Validate;
        builtFor = null;
    }

    private void OnDisable()
    {
        if (validateButton != null) validateButton.clicked -= Validate;
        IsShown = false;
    }

    private void Update()
    {
        // Le match du joueur de cette machine (au Tournoi, son couloir).
        RoundManager rm = RoundManager.Local;
        NetworkObject local = GameHud.LocalPlayerObject;
        bool show = rm != null && rm.IsSpawned && local != null && rm.CurrentPhase == RoundManager.Phase.Preparation;
        if (show)
        {
            weapon = local.GetComponentInChildren<WeaponController>();
            state = local.GetComponent<PlayerMatchState>();
            // Les spectateurs ne choisissent pas d'arme : la préparation ne concerne que les duellistes, et au Tag Team
            // tous les membres des équipes, qui entreront tour à tour.
            show = weapon != null && state != null && state.IsMatchPlayer;
        }

        IsShown = show;
        panel.EnableInClassList("hidden", !show);
        if (!show) return;

        if (builtFor != weapon) BuildCards();

        // Pile ou face : la famille de la manche, choisie par la pièce ou le perdant ; seules ses armes se choisissent.
        ulong me = local.OwnerClientId;
        bool coinFlip = rm.WeaponChoiceNow == WeaponChoice.CoinFlip;
        CoinFlip.State coin = rm.CoinFlipNow;
        bool pending = rm.CoinFlipFamilyPending;
        float sinceToss = rm.CoinFlipSinceToss;
        bool spinning = sinceToss >= 0f && sinceToss < CoinFlip.TossSeconds;
        coinLabel.EnableInClassList("hidden", !coinFlip);
        if (coinFlip)
        {
            string line = CoinLine(coin, me, sinceToss, pending, PlayerMatchState.NameOf);
            if (line != shownCoin) coinLabel.text = shownCoin = line;
        }
        bool choosing = coinFlip && pending && coin.chooser == me && !spinning;
        familyRow.EnableInClassList("hidden", !choosing);
        for (int f = 0; f < familyButtons.Count; f++) familyButtons[f].SetEnabled(choosing && CoinFlip.Allowed((byte)f, coin.previous));
        titleLabel.text = !coinFlip ? "Choisis ton arme"
                        : pending ? (coin.chooser == me ? "Choisis la famille" : "Pile ou face")
                        : coin.family == CoinFlip.GloveFamily ? "Le gant seul" : "Choisis ton arme";

        bool canChoose = weapon.CanChooseWeapon;
        for (int i = 0; i < cards.Count; i++)
        {
            bool inFamily = !coinFlip || CoinFlip.SlotAllowed(weapon.DataOf(cardSlots[i]), coin.family);
            cards[i].EnableInClassList("loadout-card--active", cardSlots[i] == weapon.LocalSlot);
            cards[i].EnableInClassList("loadout-card--outside", coinFlip && !pending && !inFamily);
            cards[i].SetEnabled(canChoose && inFamily);
        }

        // Pendant que la pièce tourne, le choix de la famille attend ses 10 s.
        timerLabel.text = $"{Mathf.CeilToInt(pending ? Mathf.Min(CoinFlip.FamilyChoiceSeconds, rm.CoinFlipTimeLeft) : rm.PhaseTimeRemaining)} s";
        roundLabel.text = rm.Rules.Sanitized().IsTagTeam
            ? $"{TagTeam.Name(state.Team).ToUpperInvariant()}  ·  LE DÉCOMPTE PART QUAND TOUT LE MONDE A VALIDÉ"
            : rm.IsLane
            ? $"{(rm.Lane.final ? "FINALE" : "TOURNOI")}  ·  MANCHE {rm.RoundNumber}  ·  LE DÉCOMPTE PART QUAND LES DEUX JOUEURS ONT VALIDÉ"
            : $"MANCHE {rm.RoundNumber}  ·  LE DÉCOMPTE PART QUAND LES DEUX JOUEURS ONT VALIDÉ";

        bool ready = state.ReadyOrRequested;
        // On valide une arme de la famille : pas avant qu'elle soit choisie.
        validateButton.SetEnabled(!ready && !pending);
        validateButton.text = ready ? "Validé" : "Valider (Entrée)";
        statusLabel.text = ready ? $"Prêt.  {OpponentStatus(rm, local.OwnerClientId)}" : OpponentStatus(rm, local.OwnerClientId);
        statusLabel.EnableInClassList("loadout-status--ready", ready);

        Keyboard keyboard = Keyboard.current;
        if (!ready && !pending && !PauseMenu.IsOpen && keyboard != null
            && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            Validate();
        }
    }

    private static readonly System.Collections.Generic.List<PlayerMatchState> matchPlayers = new System.Collections.Generic.List<PlayerMatchState>();

    private static string OpponentStatus(RoundManager rm, ulong me)
    {
        // Au Tag Team, tous les membres des équipes choisissent : combien sont prêts.
        if (rm != null && rm.Rules.Sanitized().IsTagTeam)
        {
            int players = 0, ready = 0;
            foreach (PlayerMatchState other in PlayerMatchState.Spawned)
            {
                if (other == null || !other.IsMatchPlayer) continue;
                players++;
                if (other.Ready) ready++;
            }
            return $"{ready} sur {players} prêts.";
        }
        // L'adversaire du match (au Tournoi, celui de son couloir : tout le monde y est duelliste).
        if (rm != null) rm.GetMatchDuelists(matchPlayers);
        else matchPlayers.Clear();
        foreach (PlayerMatchState other in matchPlayers)
        {
            if (other == null || other.OwnerClientId == me) continue;
            return other.Ready ? $"{other.DisplayName} est prêt." : $"{other.DisplayName} choisit son arme...";
        }
        return "";
    }

    private void Validate()
    {
        if (state != null) state.RequestReady();
    }

    // Les douze armes, une colonne par famille (2026-10-26). Provisoire : le panneau aux favorites en
    // grand, « Toutes les armes » derrière, viendra avec l'interface de la refonte (GDD § 6).
    private void BuildCards()
    {
        builtFor = weapon;
        cardRow.Clear();
        cards.Clear();
        cardSlots.Clear();
        for (int family = 0; family < Arsenal.FamilyCount; family++)
        {
            List<int> slots = Arsenal.SlotsOf(weapon.Loadout, (WeaponFamily)family);
            if (slots.Count == 0) continue;
            var column = new VisualElement();
            column.AddToClassList("loadout-family");
            column.Add(NewLabel(Arsenal.FamilyLabel((WeaponFamily)family).ToUpperInvariant(), "loadout-family__title"));
            foreach (int slot in slots)
            {
                WeaponData data = weapon.DataOf(slot);
                int chosen = slot;
                var card = new Button(() => weapon.RequestWeapon(chosen));
                card.AddToClassList("loadout-card");
                // Le nom, et l'arme de profil à côté : rien d'autre (2026-10-27, demandé par l'utilisateur).
                card.Add(NewLabel(data.weaponName, "loadout-card-name"));
                card.Add(Portrait(data));
                column.Add(card);
                cards.Add(card);
                cardSlots.Add(slot);
            }
            cardRow.Add(column);
        }
    }

    /// <summary>Les cinq choix de pile ou face : les quatre familles, puis le gant seul (GDD § 11, P4).</summary>
    private void BuildFamilyButtons()
    {
        familyRow.Clear();
        familyButtons.Clear();
        for (byte f = 0; f <= CoinFlip.GloveFamily; f++)
        {
            byte family = f;
            var button = new Button(() => { if (state != null) state.RequestFamily(family); }) { text = CoinFlip.FamilyLabel(family) };
            button.AddToClassList("menu-button");
            button.AddToClassList("loadout-family-choice");
            familyRow.Add(button);
            familyButtons.Add(button);
        }
    }

    /// <summary>
    /// La ligne de pile ou face, au-dessus des armes : la pièce qui tourne (« PILE… », « FACE… », la première manche),
    /// qui choisit la famille et pourquoi (la pièce, ou la manche perdue), puis la famille des deux armes, ou le gant seul.
    /// Fonction pure, testée.
    /// </summary>
    public static string CoinLine(CoinFlip.State coin, ulong me, float sinceToss, bool pending, System.Func<ulong, string> nameOf)
    {
        bool tossed = sinceToss >= 0f;
        if (tossed && sinceToss < CoinFlip.TossSeconds) return (int)(sinceToss / 0.15f) % 2 == 0 ? "PILE…" : "FACE…";
        if (pending)
        {
            if (coin.chooser == me)
                return tossed ? "La pièce t'a désigné : choisis la famille des deux armes." : "Tu as perdu la manche : choisis la famille des deux armes.";
            string name = nameOf(coin.chooser);
            return tossed ? $"La pièce a désigné {name}, qui choisit la famille…" : $"{name} a perdu la manche et choisit la famille…";
        }
        if (coin.family == CoinFlip.GloveFamily) return "Le gant seul : une manche à mains nues.";
        return $"{CoinFlip.FamilyLabel(coin.family)} pour les deux : choisis ton arme dedans.";
    }

    private static Label NewLabel(string text, string className)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }

    /// <summary>L'arme de profil, sans accessoire, dans sa finition d'origine (WeaponPortraits, rendus par le
    /// menu) ; vide si le menu ne l'a pas rendue.</summary>
    private static VisualElement Portrait(WeaponData data)
    {
        var portrait = new VisualElement { pickingMode = PickingMode.Ignore };
        portrait.AddToClassList("loadout-card__portrait");
        Texture2D image = WeaponPortraits.Of(data);
        if (image != null) portrait.style.backgroundImage = new StyleBackground(image);
        return portrait;
    }
}
