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
        RoundManager rm = RoundManager.Instance;
        NetworkObject local = GameHud.LocalPlayerObject;
        bool show = rm != null && rm.IsSpawned && local != null && rm.CurrentPhase == RoundManager.Phase.Preparation;
        if (show)
        {
            weapon = local.GetComponentInChildren<WeaponController>();
            state = local.GetComponent<PlayerMatchState>();
            // Les spectateurs ne choisissent pas d'arme : la préparation ne concerne que les duellistes.
            show = weapon != null && state != null && state.IsDuelist;
        }

        IsShown = show;
        panel.EnableInClassList("hidden", !show);
        if (!show) return;

        if (builtFor != weapon) BuildCards();

        bool canChoose = weapon.CanChooseWeapon;
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].EnableInClassList("loadout-card--active", cardSlots[i] == weapon.LocalSlot);
            cards[i].SetEnabled(canChoose);
        }

        timerLabel.text = $"{Mathf.CeilToInt(rm.PhaseTimeRemaining)} s";
        roundLabel.text = $"MANCHE {rm.RoundNumber}  ·  LE DÉCOMPTE PART QUAND LES DEUX JOUEURS ONT VALIDÉ";

        bool ready = state.ReadyOrRequested;
        validateButton.SetEnabled(!ready);
        validateButton.text = ready ? "Validé" : "Valider (Entrée)";
        statusLabel.text = ready ? $"Prêt.  {OpponentStatus(local.OwnerClientId)}" : OpponentStatus(local.OwnerClientId);
        statusLabel.EnableInClassList("loadout-status--ready", ready);

        Keyboard keyboard = Keyboard.current;
        if (!ready && !PauseMenu.IsOpen && keyboard != null
            && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            Validate();
        }
    }

    private static string OpponentStatus(ulong me)
    {
        foreach (PlayerMatchState other in PlayerMatchState.Spawned)
        {
            if (other == null || other.OwnerClientId == me || !other.IsDuelist) continue;
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
