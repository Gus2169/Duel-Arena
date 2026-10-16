using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ce que fait un spectateur, sur la tribune (GDD § 9, 2026-10-13) : un contrôleur à part, puisqu'il
/// ne fait pas ce que fait un duelliste, posé sur le même robot. Son déplacement (marcher, courir,
/// s'accroupir) reste celui de PlayerLocomotion, prédit et réconcilié comme celui d'un duelliste ; le
/// rôle lui retire le reste (ni arme, ni lean, ni franchissement, ni allongé).
///
/// - SE BATTRE : un coup de poing (V, ou clic gauche les mains vides). Il étourdit un autre spectateur
///   quelques secondes, qui lâche ce qu'il tient : on peut le lui voler. Il fend aussi les blocs de la
///   tribune, d'où l'on arrache des cailloux.
/// - RAMASSER (F) : un caillou ou du matériel posé devant soi. Une seule chose en main.
/// - LANCER (clic gauche maintenu, puis relâché) : plus l'élan est long, plus ça part loin. Le geste
///   est imparfait, sans aide à la visée (GDD § 3). Clic droit annule l'élan.
/// - ÊTRE ÉTOURDI : on ne bouge plus et on ne fait plus rien pendant quelques secondes, puis on est
///   immunisé un instant, pour ne pas rester au sol sous les coups.
///
/// AUTORITÉ : comme le taser. Le joueur demande (coup, ramassage, lancer), le serveur vérifie tout et
/// décide seul ; aucune RPC n'accepte de résultat. L'étourdissement et l'objet tenu sont publiés par
/// le serveur ; l'étourdissement est lu par PlayerLocomotion.Move() (catégorie A, comme le gel de
/// manche). Les sons sont décidés par le serveur (catégorie B). Le geste, l'objet en main, les
/// étincelles et la vue subjective sont cosmétiques (catégorie C).
///
/// Le même composant reçoit, chez un DUELLISTE, ce que lui font les spectateurs : un caillou qui le
/// touche secoue sa visée (tranché par l'utilisateur le 2026-10-13 : aucun dégât).
/// </summary>
[DisallowMultipleComponent]
public class SpectatorController : NetworkBehaviour
{
    /// <summary>L'état publié par le serveur.</summary>
    public struct State : INetworkSerializeByMemcpy
    {
        /// <summary>Fin de l'étourdissement, horloge du serveur (0 : jamais étourdi).</summary>
        public double stunnedUntil;
        /// <summary>Fin de l'immunité qui suit un étourdissement.</summary>
        public double stunImmuneUntil;
        public ArenaItemKind carried;
        public byte carriedVariant;
    }

    [Header("Coup de poing (à juger en jouant)")]
    [Tooltip("Durée du geste (s) : on ne refrappe pas avant, et c'est aussi la cadence que le serveur accepte.")]
    [SerializeField] private float punchDuration = 0.45f;
    [Tooltip("De l'appui au contact (s), comme le taser : un spectateur étourdi pendant son élan ne frappe pas.")]
    [SerializeField] private float punchContactDelay = 0.15f;
    [Tooltip("Portée depuis l'œil (m) : le bras tendu.")]
    [SerializeField] private float punchRange = 1.5f;
    [SerializeField] private float punchRadius = 0.3f;
    [Tooltip("Étourdissement d'un coup de poing (s) : « quelques secondes » (demande de l'utilisateur).")]
    [SerializeField] private float punchStunDuration = 2.5f;

    [Header("Étourdissement")]
    [Tooltip("Après un étourdissement, on ne peut pas être étourdi de nouveau pendant ce temps (s) : personne ne reste au sol sous les coups de deux autres.")]
    [SerializeField] private float stunImmunity = 1.5f;
    [Tooltip("Étourdissement d'un caillou reçu par un spectateur (s).")]
    [SerializeField] private float rockStunDuration = 1.5f;

    [Header("Ramasser")]
    [Tooltip("Portée (m) depuis l'œil.")]
    [SerializeField] private float pickupRange = 2.3f;
    [Tooltip("Demi-angle du cône (degrés) dans lequel on vise l'objet à ramasser.")]
    [SerializeField] private float pickupAngle = 35f;

    [Header("Lancer (à juger en jouant)")]
    [Tooltip("Temps d'élan (s) pour lancer à pleine force.")]
    [SerializeField] private float chargeTime = 0.8f;
    [SerializeField] private float minThrowSpeed = 9f;
    [SerializeField] private float maxThrowSpeed = 24f;
    [Tooltip("Intervalle minimal entre deux lancers (s), en temps réel côté serveur.")]
    [SerializeField] private float throwCooldown = 0.5f;

    [Header("Un duelliste touché par un caillou (ressenti)")]
    [Tooltip("La visée remonte d'autant (degrés), et reste là : le duelliste doit se recaler.")]
    [SerializeField] private float flinchPitch = 3.5f;
    [Tooltip("Secousse passagère de la vue (degrés), du côté où va le caillou.")]
    [SerializeField] private float flinchShake = 2.5f;
    [SerializeField] private float flinchShakeDuration = 0.35f;

    [Header("Garde-fous serveur")]
    [Tooltip("Distance max (m) entre l'œil que le serveur connaît et l'origine annoncée d'un coup ou d'un lancer. Couvre l'avance de la prédiction en courant.")]
    [SerializeField] private float maxOriginDistance = 3f;
    [Tooltip("Rewind maximal (s) accordé au coup de poing, comme au tir et au taser.")]
    [SerializeField] private float maxRewindSeconds = 0.3f;
    [Tooltip("Marge (m) accordée au ramassage : le joueur se voit un peu en avance sur le serveur.")]
    [SerializeField] private float pickupTolerance = 0.9f;
    [SerializeField] private LayerMask worldMask = 1;

    private readonly NetworkVariable<State> state = new NetworkVariable<State>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<SpectatorController> spawned = new List<SpectatorController>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => spawned.Clear();

    public static IReadOnlyList<SpectatorController> Spawned => spawned;

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private PlayerMatchState matchState;
    private PlayerCameraLook cameraLook;
    private Camera playerCamera;
    private AudioSource audioSource;
    private Animator modelAnimator;

    // Chez le joueur.
    private float localPunchEndsAt = -1f;
    private bool charging;
    private float chargeStartedAt;
    private float throwHiddenUntil = -1f;
    private ArenaItem pickupCandidate;
    private readonly List<Vector3> candidatePositions = new List<Vector3>();
    private readonly List<ArenaItem> candidateItems = new List<ArenaItem>();

    // Au serveur.
    private double serverLastPunchAt = double.NegativeInfinity;
    private double serverLastThrowAt = double.NegativeInfinity;
    private bool serverPunchPending;
    private double serverPunchResolveAt;
    private Vector3 serverPunchOffset;
    private Vector3 serverPunchDirection;
    private double serverPunchViewTime;

    // L'objet tenu, vu sur le corps (toutes les machines).
    private GameObject handVisual;
    private ArenaItemKind handKind;
    private int handVariant = -1;

    // L'étourdissement, vu sur le corps : des arcs autour de la tête (ElectricArc).
    private ArcLines stunArcs;
    private float stunVisual;

    /// <summary>Levé à chaque coup de poing VU depuis cette machine (durée du geste) : chez le joueur à
    /// l'appui, chez les autres à la diffusion du serveur. Cosmétique (animation, vue subjective).</summary>
    public event System.Action<float> OnPunch;

    /// <summary>Levé à chaque lancer vu depuis cette machine. Cosmétique.</summary>
    public event System.Action OnThrow;

    /// <summary>Levé quand le joueur se retrouve un objet en main (le serveur a accepté le ramassage),
    /// sur toutes les machines. Cosmétique : le geste de ramasser.</summary>
    public event System.Action OnPickUp;

    // ------------------------------------------------------------------
    // Ce que lisent les autres
    // ------------------------------------------------------------------

    /// <summary>Ce joueur est-il spectateur ? (rôle publié par le serveur)</summary>
    public bool IsSpectatorRole => matchState != null && matchState.IsSpectator;

    /// <summary>Hauteur du corps (m), selon la posture publiée : la capsule que touchent les poings et
    /// les cailloux.</summary>
    public float BodyHeight => locomotion != null ? locomotion.ControllerHeight(locomotion.NetworkedStance) : 1.8f;

    public ArenaItemKind Carried => state.Value.carried;
    public int CarriedVariant => state.Value.carriedVariant;

    /// <summary>L'objet en main tel que cette machine le montre : chez le joueur qui vient de lancer,
    /// sa main est déjà vide, le temps que le serveur le confirme.</summary>
    public ArenaItemKind ShownCarried => IsOwner && Time.time < throwHiddenUntil ? ArenaItemKind.None : state.Value.carried;

    /// <summary>L'horloge de cette machine pour l'étourdissement : celle de la prédiction chez le joueur
    /// (NetworkManager.LocalTime, comme Move()), celle du serveur ailleurs.</summary>
    private double Clock
    {
        get
        {
            if (!IsSpawned || NetworkManager == null) return Time.timeAsDouble;
            return IsOwner && !IsServer ? NetworkManager.LocalTime.Time : NetworkManager.ServerTime.Time;
        }
    }

    /// <summary>Étourdi à l'instant `time` de l'horloge du serveur ? Lu par PlayerLocomotion.Move().</summary>
    public bool IsStunnedAt(double time) => SpectatorRules.IsStunned(state.Value.stunnedUntil, time);

    public bool IsStunnedNow => IsStunnedAt(Clock);

    /// <summary>Secondes d'étourdissement restantes, sur cette machine.</summary>
    public float StunRemaining => (float)System.Math.Max(0.0, state.Value.stunnedUntil - Clock);

    /// <summary>De 0 à 1, l'étourdissement montré (fondu à l'entrée et à la sortie), pour l'animation.</summary>
    public float StunVisualWeight => stunVisual;

    public bool IsPunching => Time.time < localPunchEndsAt;
    /// <summary>De l'appui au contact du coup de poing (s) : l'animation y tend le poing.</summary>
    public float PunchContactDelay => punchContactDelay;
    public bool IsCharging => charging;
    public float ChargeProgress => charging ? Mathf.Clamp01((Time.time - chargeStartedAt) / Mathf.Max(0.01f, chargeTime)) : 0f;

    /// <summary>Chez le joueur : le sprint s'efface devant le coup, l'élan et l'étourdissement
    /// (PlayerLocomotion.GateSprint).</summary>
    public bool LocalBlocksSprint => IsPunching || charging || IsStunnedNow;

    /// <summary>Chez le joueur : l'objet qu'il vise pour le ramasser, ou null.</summary>
    public ArenaItem PickupCandidate => pickupCandidate;

    /// <summary>Les spectateurs peuvent-ils agir dans cette partie ? (réglage de l'hôte)</summary>
    public static bool InterventionsAllowed
    {
        get
        {
            RoundManager rm = RoundManager.Instance;
            return rm == null || !rm.IsSpawned || rm.Rules.spectatorInterventions;
        }
    }

    /// <summary>Peut-on lancer dans la phase en cours ?</summary>
    public static bool ThrowPhaseAllowed
    {
        get
        {
            RoundManager rm = RoundManager.Instance;
            return rm == null || !rm.IsSpawned || SpectatorRules.ThrowAllowed(rm.CurrentPhase);
        }
    }

    private SpectatorSounds Sounds => Tribune.Instance != null ? Tribune.Instance.Sounds : null;

    // ------------------------------------------------------------------
    // Cycle de vie
    // ------------------------------------------------------------------

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        locomotion = GetComponent<PlayerLocomotion>();
        matchState = GetComponent<PlayerMatchState>();
        cameraLook = GetComponentInChildren<PlayerCameraLook>(true);
        audioSource = GetComponent<AudioSource>();
        Transform model = transform.Find("Model");
        modelAnimator = model != null ? model.GetComponent<Animator>() : null;
        foreach (Camera c in GetComponentsInChildren<Camera>(true))
        {
            if (c.name == "Main Camera") { playerCamera = c; break; }
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!spawned.Contains(this)) spawned.Add(this);
        state.OnValueChanged += HandleStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= HandleStateChanged;
        spawned.Remove(this);
        // Un spectateur qui part lâche ce qu'il tenait, là où il était.
        if (IsServer) ServerDropCarried(Vector3.zero);
        ClearHandVisual();
    }

    private void HandleStateChanged(State previous, State current)
    {
        bool pickedUp = previous.carried == ArenaItemKind.None && current.carried != ArenaItemKind.None;
        if (pickedUp) OnPickUp?.Invoke();
        SpectatorSounds sounds = Sounds;
        if (sounds == null) return;
        // Catégorie B : le serveur a décidé ; chacun l'entend sur ce robot.
        if (current.stunnedUntil > previous.stunnedUntil) sounds.stunned.PlayOn(audioSource);
        if (pickedUp) sounds.pickUp.PlayOn(audioSource);
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && serverPunchPending && Time.timeAsDouble >= serverPunchResolveAt)
        {
            serverPunchPending = false;
            ServerResolvePunch();
        }

        if (IsOwner) OwnerUpdate();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        UpdateHandVisual();
        UpdateStunVisual();
    }

    // ------------------------------------------------------------------
    // Chez le joueur
    // ------------------------------------------------------------------

    private void OwnerUpdate()
    {
        bool spectating = IsSpectatorRole;
        if (!spectating || input == null || !InterventionsAllowed || IsStunnedNow || input.CurrentMode != PlayerInputReader.Mode.Gameplay)
        {
            charging = false;
            pickupCandidate = null;
            return;
        }

        pickupCandidate = FindPickupCandidate();
        bool holding = ShownCarried != ArenaItemKind.None;

        bool wantsPunch = input.MeleePressedThisFrame || (!holding && input.FireWasPressedThisFrame);
        bool wantsPickUp = input.InteractPressedThisFrame;
        bool fireHeld = input.FireHeld;
        bool firePressed = input.FireWasPressedThisFrame;
        bool aimHeld = input.AimHeld;
#if UNITY_EDITOR
        AutopilotOverride(ref wantsPunch, ref wantsPickUp, ref fireHeld, ref firePressed, holding);
#endif

        if (holding)
        {
            if (!charging && firePressed && ThrowPhaseAllowed && !IsPunching)
            {
                charging = true;
                chargeStartedAt = Time.time;
            }
            if (charging && aimHeld) charging = false; // clic droit : on renonce
            if (charging && !fireHeld)
            {
                Throw();
                charging = false;
            }
        }
        else
        {
            charging = false;
        }

        if (wantsPunch && !charging && !IsPunching) Punch();
        if (wantsPickUp && !holding && pickupCandidate != null)
            PickUpServerRpc(new NetworkObjectReference(pickupCandidate.NetworkObject));
    }

    /// <summary>L'objet posé que vise le joueur : à portée de son œil, dans le cône du regard, sans
    /// mur entre les deux. Sur la tribune seulement : l'arène est hors de portée de toute façon.</summary>
    private ArenaItem FindPickupCandidate()
    {
        if (playerCamera == null) return null;
        candidatePositions.Clear();
        candidateItems.Clear();
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item == null || !item.IsResting) continue;
            candidatePositions.Add(item.RestPosition);
            candidateItems.Add(item);
        }
        Transform eye = playerCamera.transform;
        int best = SpectatorRules.BestPickup(eye.position, eye.forward, candidatePositions, pickupRange, pickupAngle);
        if (best < 0) return null;
        ArenaItem candidate = candidateItems[best];
        return CanSee(eye.position, candidate.RestPosition, worldMask) ? candidate : null;
    }

    /// <summary>
    /// L'œil voit-il l'objet posé en `item` ? Son centre, ou un peu au-dessus : un objet tombé contre un
    /// bloc fissuré ne doit pas devenir impossible à ramasser parce que le coin du bloc cache son
    /// centre (vécu au banc à deux instances, 2026-10-13). Le même test chez le joueur et au serveur.
    /// </summary>
    public static bool CanSee(Vector3 eye, Vector3 item, LayerMask worldMask)
    {
        return !Physics.Linecast(eye, item, worldMask, QueryTriggerInteraction.Ignore)
            || !Physics.Linecast(eye, item + Vector3.up * 0.25f, worldMask, QueryTriggerInteraction.Ignore);
    }

    private void Punch()
    {
        localPunchEndsAt = Time.time + punchDuration;
        OnPunch?.Invoke(punchDuration);
        Sounds?.punchSwing.PlayOn(audioSource);
        if (playerCamera == null || locomotion == null) return;
        Transform eye = playerCamera.transform;
        PunchServerRpc(eye.position, eye.forward, locomotion.ViewServerTime);
    }

    private void Throw()
    {
        float charge = ChargeProgress;
        throwHiddenUntil = Time.time + 0.6f;
        OnThrow?.Invoke();
        Sounds?.throwSwing.PlayOn(audioSource);
        if (playerCamera == null) return;
        Transform eye = playerCamera.transform;
        ThrowServerRpc(ThrowOrigin(eye), eye.forward, charge);
    }

    /// <summary>D'où part un objet lancé : un peu devant l'œil, du côté de la main droite.</summary>
    public static Vector3 ThrowOrigin(Transform eye)
        => eye.position + eye.forward * 0.4f + eye.right * 0.15f - eye.up * 0.1f;

    // ------------------------------------------------------------------
    // Au serveur : les demandes du joueur
    // ------------------------------------------------------------------

    /// <summary>Un spectateur peut-il agir maintenant ? Spectateur, interventions permises, pas étourdi.</summary>
    private bool ServerCanAct()
        => IsSpectatorRole && InterventionsAllowed && !IsStunnedAt(NetworkManager.ServerTime.Time);

    /// <summary>L'œil du joueur tel que le serveur le connaît (posture publiée, sans lean).</summary>
    private Vector3 ServerEye => transform.position + Vector3.up * (locomotion != null ? locomotion.EyeLocal(locomotion.NetworkedStance).y : 1.65f);

    [ServerRpc]
    private void PunchServerRpc(Vector3 origin, Vector3 direction, double viewServerTime)
    {
        // Une valeur non finie ne vient que d'un client modifié : avec NaN, les tests de distance
        // seraient faux, donc passés (CLAUDE.md).
        if (!WeaponController.IsFinite(origin) || !WeaponController.IsFinite(direction) || !WeaponController.IsFinite(viewServerTime)) return;
        double now = Time.timeAsDouble;
        // Un coup par geste, en temps réel (règle des garde-fous), 15 % de marge pour la gigue.
        if (now - serverLastPunchAt < punchDuration * 0.85) return;
        if (!ServerCanAct() || direction.sqrMagnitude < 1e-4f) return;
        Vector3 offset = origin - transform.position;
        if ((origin - ServerEye).sqrMagnitude > maxOriginDistance * maxOriginDistance)
        {
            Debug.LogWarning($"[Serveur] Coup de poing rejeté : origine à {Vector3.Distance(origin, ServerEye):F1} m de l'œil (max {maxOriginDistance} m).", this);
            return;
        }
        serverLastPunchAt = now;
        PunchClientRpc();

        serverPunchPending = true;
        serverPunchResolveAt = now + punchContactDelay;
        serverPunchOffset = offset;
        serverPunchDirection = direction.normalized;
        serverPunchViewTime = viewServerTime + punchContactDelay;
    }

    [ClientRpc]
    private void PunchClientRpc()
    {
        if (IsOwner) return; // déjà joué à l'appui
        OnPunch?.Invoke(punchDuration);
        Sounds?.punchSwing.PlayOn(audioSource);
    }

    /// <summary>
    /// Le coup arrive au contact. Il touche ce qui est le plus proche devant le poing : un autre
    /// spectateur, là où le frappeur le voyait (compensation de latence, comme le taser), ou un bloc
    /// fissuré de la tribune. Rien à travers un mur.
    /// </summary>
    private void ServerResolvePunch()
    {
        if (!ServerCanAct()) return; // étourdi pendant son élan : le coup ne part pas

        Vector3 origin = transform.position + serverPunchOffset;
        Vector3 direction = serverPunchDirection;
        double serverNow = NetworkManager.ServerTime.Time;
        float rewind = Mathf.Clamp((float)(serverNow - serverPunchViewTime), 0f, maxRewindSeconds);
        double targetTime = serverNow - rewind;

        SpectatorController victim = null;
        float bestDistance = float.MaxValue;
        foreach (SpectatorController other in spawned)
        {
            if (other == null || other == this || !other.IsSpectatorRole || other.locomotion == null) continue;
            Vector3 feet = other.transform.position;
            float height = other.BodyHeight;
            if (other.locomotion.ServerTrySamplePose(targetTime, out PlayerLocomotion.HitboxPose pose))
            {
                feet = pose.position;
                height = other.locomotion.ControllerHeight(pose.stance.current);
            }
            if (!SpectatorRules.PunchReaches(origin, direction, punchRange, punchRadius, feet, height,
                                             SpectatorRules.BodyRadius, out float distance, out Vector3 point)) continue;
            if (distance >= bestDistance) continue;
            if (distance > 0.05f && Physics.Linecast(origin, point, worldMask, QueryTriggerInteraction.Ignore)) continue;
            victim = other;
            bestDistance = distance;
        }

        Tribune tribune = Tribune.Instance;
        float sourceDistance = float.MaxValue;
        RockSource source = tribune != null
            ? tribune.QueryRockSource(origin, direction, punchRange, punchRadius, worldMask, out sourceDistance, out _)
            : null;
        if (source != null && sourceDistance < bestDistance)
        {
            tribune.ServerHitSource(source);
            return;
        }

        if (victim == null) return;
        victim.PunchedClientRpc();
        victim.ServerStun(punchStunDuration, direction);
        Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} frappe {PlayerMatchState.NameOf(victim.OwnerClientId)} (rewind {rewind * 1000f:F0} ms).");
    }

    [ClientRpc]
    private void PunchedClientRpc() => Sounds?.punchHit.PlayOn(audioSource);

    [ServerRpc]
    private void PickUpServerRpc(NetworkObjectReference itemReference)
    {
        if (!ServerCanAct() || state.Value.carried != ArenaItemKind.None) return;
        if (!itemReference.TryGet(out NetworkObject itemObject) || !itemObject.TryGetComponent(out ArenaItem item)) return;
        if (!item.ServerIsPickable) return;

        Vector3 eye = ServerEye;
        float reach = pickupRange + pickupTolerance;
        if ((item.RestPosition - eye).sqrMagnitude > reach * reach) return;
        if (!CanSee(eye, item.RestPosition, worldMask)) return;

        State s = state.Value;
        s.carried = item.Kind;
        s.carriedVariant = (byte)Mathf.Clamp(item.Variant, 0, 255);
        state.Value = s;
        item.ServerConsume();
    }

    [ServerRpc]
    private void ThrowServerRpc(Vector3 origin, Vector3 direction, float charge)
    {
        if (!WeaponController.IsFinite(origin) || !WeaponController.IsFinite(direction) || float.IsNaN(charge)) return;
        State s = state.Value;
        if (!ServerCanAct() || s.carried == ArenaItemKind.None || !ThrowPhaseAllowed) return;
        double now = Time.timeAsDouble;
        if (now - serverLastThrowAt < throwCooldown * 0.85) return;
        Vector3 velocity = SpectatorRules.ThrowVelocity(direction, charge, minThrowSpeed, maxThrowSpeed);
        if (velocity == Vector3.zero) return;

        Vector3 eye = ServerEye;
        if ((origin - eye).sqrMagnitude > maxOriginDistance * maxOriginDistance)
        {
            Debug.LogWarning($"[Serveur] Lancer rejeté : origine à {Vector3.Distance(origin, eye):F1} m de l'œil (max {maxOriginDistance} m).", this);
            return;
        }
        // La main dans un mur ou derrière le garde-corps : l'objet part de l'œil.
        if (Physics.Linecast(eye, origin, worldMask, QueryTriggerInteraction.Ignore)) origin = eye;

        Tribune tribune = Tribune.Instance;
        if (tribune == null || tribune.ItemPrefab == null) return;
        serverLastThrowAt = now;
        ArenaItem.ServerSpawn(tribune.ItemPrefab, s.carried, s.carriedVariant, origin, velocity, OwnerClientId, false);
        s.carried = ArenaItemKind.None;
        s.carriedVariant = 0;
        state.Value = s;
        ThrowClientRpc();
    }

    [ClientRpc]
    private void ThrowClientRpc()
    {
        if (IsOwner) return;
        OnThrow?.Invoke();
        Sounds?.throwSwing.PlayOn(audioSource);
    }

    // ------------------------------------------------------------------
    // Au serveur : ce qui arrive à ce joueur
    // ------------------------------------------------------------------

    /// <summary>Serveur : étourdit ce spectateur `duration` secondes (sauf pendant son immunité). Il
    /// lâche ce qu'il tient, poussé dans le sens du coup. Renvoie vrai s'il est étourdi.</summary>
    public bool ServerStun(float duration, Vector3 push)
    {
        if (!IsServer || !IsSpectatorRole) return false;
        State s = state.Value;
        if (!SpectatorRules.TryStun(ref s.stunnedUntil, ref s.stunImmuneUntil, NetworkManager.ServerTime.Time, duration, stunImmunity)) return false;
        state.Value = s;
        serverPunchPending = false;
        ServerDropCarried(push);
        return true;
    }

    /// <summary>Serveur : un caillou lancé touche ce spectateur.</summary>
    public void ServerRockHitSpectator(Vector3 rockVelocity)
    {
        if (ServerStun(rockStunDuration, rockVelocity.normalized))
            Debug.Log($"[Spectateurs] {PlayerMatchState.NameOf(OwnerClientId)} étourdi par un caillou.");
    }

    /// <summary>Serveur : un caillou lancé touche ce DUELLISTE. Aucun dégât : sa visée est secouée,
    /// chez lui seulement (catégorie C, déclenchée par le serveur).</summary>
    public void ServerRockHitDuelist(Vector3 rockVelocity, Vector3 point)
    {
        if (!IsServer || IsSpectatorRole) return;
        RockHitClientRpc(rockVelocity, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });
        Debug.Log($"[Spectateurs] Caillou sur le duelliste {PlayerMatchState.NameOf(OwnerClientId)}.");
    }

    [ClientRpc]
    private void RockHitClientRpc(Vector3 rockVelocity, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner || cameraLook == null) return;
        // La visée remonte et y reste ; la vue tremble un instant du côté où va le caillou. Aucun hasard :
        // le même caillou donne toujours la même secousse (GDD § 3).
        float side = SpectatorRules.FlinchSide(rockVelocity, transform.right);
        cameraLook.AddInstantRotation(flinchPitch, 0f);
        cameraLook.Shake(flinchShake * side, flinchShakeDuration);
    }

    /// <summary>Serveur : lâche ce que tient le joueur, qui tombe devant lui (poussé de `push`), et que
    /// n'importe qui peut ramasser : c'est le vol du GDD (§ 9).</summary>
    public void ServerDropCarried(Vector3 push)
    {
        if (!IsServer) return;
        State s = state.Value;
        if (s.carried == ArenaItemKind.None) return;
        Tribune tribune = Tribune.Instance;
        if (tribune != null && tribune.ItemPrefab != null)
        {
            Vector3 start = transform.position + Vector3.up * 1.2f;
            Vector3 flat = new Vector3(push.x, 0f, push.z);
            Vector3 velocity = (flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward) * 2f + Vector3.up * 2f;
            ArenaItem.ServerSpawn(tribune.ItemPrefab, s.carried, s.carriedVariant, start, velocity, OwnerClientId, true);
        }
        s.carried = ArenaItemKind.None;
        s.carriedVariant = 0;
        if (IsSpawned) state.Value = s;
    }

    /// <summary>Serveur : le joueur change de rôle. Un spectateur qui entre dans l'arène lâche ce qu'il
    /// tenait sur la tribune et n'est plus étourdi.</summary>
    public void ServerOnRoleChanged(PlayerRole role)
    {
        if (!IsServer) return;
        ServerDropCarried(Vector3.zero);
        serverPunchPending = false;
        State s = state.Value;
        if (s.stunnedUntil == 0.0 && s.stunImmuneUntil == 0.0) return;
        s.stunnedUntil = 0.0;
        s.stunImmuneUntil = 0.0;
        state.Value = s;
    }

    // ------------------------------------------------------------------
    // Ce que voient les autres (catégorie C)
    // ------------------------------------------------------------------

    /// <summary>L'objet en main droite, sur le corps du robot : les autres voient qui tient quoi.</summary>
    private void UpdateHandVisual()
    {
        ArenaItemKind kind = ShownCarried;
        int variant = kind == ArenaItemKind.None ? -1 : CarriedVariant;
        if (kind == handKind && variant == handVariant) return;
        ClearHandVisual();
        handKind = kind;
        handVariant = variant;
        if (kind == ArenaItemKind.None || modelAnimator == null || Tribune.Instance == null) return;
        Transform hand = modelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        handVisual = Tribune.Instance.ItemPrefab != null ? Tribune.Instance.ItemPrefab.CreateVisual(kind, variant, hand) : null;
        if (handVisual == null) return;
        handVisual.transform.localPosition = new Vector3(0f, 0.08f, 0.05f);
        handVisual.transform.localRotation = Quaternion.identity;
        // Chez le joueur, son corps n'est qu'une ombre (sa caméra est dans le crâne) : l'objet aussi.
        if (IsOwner)
        {
            foreach (Renderer r in handVisual.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    private void ClearHandVisual()
    {
        if (handVisual != null) Destroy(handVisual);
        handVisual = null;
        handKind = ArenaItemKind.None;
        handVariant = -1;
    }

    /// <summary>Un robot étourdi grésille : des arcs autour de la tête, et il titube sur place (la
    /// couche Etourdi de l'Animator, que PlayerAnimator règle sur StunVisualWeight).</summary>
    private void UpdateStunVisual()
    {
        bool stunned = IsSpectatorRole && IsStunnedNow;
        stunVisual = Mathf.MoveTowards(stunVisual, stunned ? 1f : 0f, Time.deltaTime / 0.2f);
        // Le joueur étourdi ne voit pas les siens : sa tête est sa caméra (il a le voile de SpectatorHud).
        DrawStunArcs(IsOwner ? 0f : stunVisual);
        if (!stunned) return;
        if (IsOwner && cameraLook != null) cameraLook.Wobble(stunVisual);
    }

    /// <summary>La même décharge que le gant du taser, vue de l'autre côté (2026-10-16) : trois petits arcs
    /// sautent autour de la tête, du matériau des arcs du gant, et changent de forme quinze fois par
    /// seconde. `strength` : la part de l'étourdissement montrée (0 les cache).</summary>
    private void DrawStunArcs(float strength)
    {
        if (strength <= 0.01f || modelAnimator == null)
        {
            if (stunArcs != null) stunArcs.HideAll();
            return;
        }
        Transform head = modelAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;
        if (stunArcs == null)
        {
            Transform glove = HeldWeapons.TaserGlove(modelAnimator);
            TaserSparkFX source = glove != null ? glove.GetComponentInChildren<TaserSparkFX>(true) : null;
            if (source == null || source.ArcMaterial == null) return;
            stunArcs = new ArcLines(transform, "Arcs d'étourdissement", source.ArcMaterial, 0, new[] { 3, 3, 3 }, null);
        }

        Vector3 center = head.position + Vector3.up * 0.1f;
        uint seed = ElectricArc.Hash((uint)NetworkObjectId * 0x9e3779b9U ^ (uint)Mathf.FloorToInt(Time.time / 0.065f));
        Camera viewer = Camera.main;
        float width = Mathf.Max(0.008f, viewer != null ? Vector3.Distance(viewer.transform.position, center) * 0.0025f : 0f);
        for (int i = 0; i < stunArcs.Count; i++)
        {
            uint s = ElectricArc.Hash(seed + (uint)(i + 1) * 0x632be5abU);
            Vector3 from = ElectricArc.OnSphere(ref s);
            Vector3 to = ElectricArc.Tilt(from, 60f + 50f * ElectricArc.Next01(ref s), ref s);
            Vector3 a = center + from * 0.13f, b = center + to * 0.13f;
            // L'arc passe au-dessus du crâne, pas à travers.
            Vector3 bow = ((from + to) * 0.5f).normalized * 0.05f;
            ElectricArc.Build(a, b, 3, 0.3f, bow, ref s, stunArcs.Points(i));
            float force = strength * (0.55f + 0.45f * ElectricArc.Next01(ref s));
            stunArcs.Show(i, width, force, force);
        }
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : étourdi jusqu'à `until` (horloge du serveur, ou temps local hors session).</summary>
    public void TestSetStunnedUntil(double until)
    {
        State s = state.Value;
        s.stunnedUntil = until;
        state.Value = s;
    }
#endif

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // BANC DE TEST — Editor uniquement : un spectateur que personne ne pilote (le clone de Multiplayer
    // Play Mode). Il marche jusqu'au bloc fissuré le plus proche, le frappe jusqu'à ce qu'il cède,
    // ramasse ce qui tombe (ou le matériel qu'il voit), et lance tout sur le duelliste, à pleine force.
    // ------------------------------------------------------------------

    public static bool AutopilotSpectator;
    private float autopilotChargeUntil;

    private void AutopilotOverride(ref bool punch, ref bool pickUp, ref bool fireHeld, ref bool firePressed, bool holding)
    {
        if (!AutopilotSpectator) return;
        punch = false;
        pickUp = false;
        fireHeld = false;
        firePressed = false;
        input.AutopilotMove(Vector2.zero);
        if (!holding && pickupCandidate == null && AutopilotGoToItem()) return;
        if (holding)
        {
            // Vise le duelliste le plus proche (tir tendu), puis prend tout son élan.
            PlayerLocomotion duelist = null;
            foreach (PlayerLocomotion p in PlayerLocomotion.SpawnedPlayers)
            {
                if (p != null && !p.IsSpectator && p.IsAlive) { duelist = p; break; }
            }
            // Un caillou au buste ; une arme à ses pieds, pour qu'il la ramasse.
            float aimHeight = ShownCarried == ArenaItemKind.Rock ? 1.2f : 0.2f;
            if (duelist != null) AutopilotLookAt(duelist.transform.position + Vector3.up * aimHeight, maxThrowSpeed);
            if (!charging && Time.time > autopilotChargeUntil + 0.5f)
            {
                firePressed = true;
                fireHeld = true;
                autopilotChargeUntil = Time.time + chargeTime + 0.1f;
            }
            else if (charging)
            {
                fireHeld = Time.time < autopilotChargeUntil;
            }
            return;
        }
        if (pickupCandidate != null)
        {
            pickUp = true;
            return;
        }

        // Le bloc fissuré le plus proche qui peut encore donner un caillou : on y va, on le frappe.
        Tribune tribune = Tribune.Instance;
        if (tribune == null || playerCamera == null) return;
        RockSource target = null;
        float best = float.MaxValue;
        foreach (RockSource source in tribune.RockSources)
        {
            if (source == null || !tribune.SourceAvailable(source.Index)) continue;
            float d = Vector3.Distance(transform.position, source.transform.position);
            if (d < best) { best = d; target = source; }
        }
        if (target == null) return;
        Vector3 eye = playerCamera.transform.position;
        Vector3 aim = target.ClosestPoint(eye + Vector3.down * 0.9f);
        AutopilotLookAtPoint(aim);
        if (Vector3.Distance(eye, aim) > punchRange * 0.8f) input.AutopilotMove(Vector2.up);
        else punch = !IsPunching;
    }

    /// <summary>Le matériel ou le caillou posé le plus proche sur la tribune : on y va, et on le
    /// regarde pour le ramasser. Faux s'il n'y en a pas.</summary>
    private bool AutopilotGoToItem()
    {
        Tribune tribune = Tribune.Instance;
        if (tribune == null || playerCamera == null) return false;
        ArenaItem nearest = null;
        float best = 12f;
        foreach (ArenaItem item in ArenaItem.Spawned)
        {
            if (item == null || !item.IsResting || !tribune.IsOnTribune(item.RestPosition)) continue;
            float d = Vector3.Distance(transform.position, item.RestPosition);
            if (d < best) { best = d; nearest = item; }
        }
        if (nearest == null) return false;
        AutopilotLookAtPoint(nearest.RestPosition);
        // Jusqu'à le voir à portée ; s'il reste caché, tout contre lui.
        if (best > 1.2f || (pickupCandidate == null && best > 0.5f)) input.AutopilotMove(Vector2.up);
        return true;
    }

    private void AutopilotLookAtPoint(Vector3 point)
    {
        Vector3 to = point - playerCamera.transform.position;
        float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        input.AutopilotLookTarget(yaw, pitch);
    }

    /// <summary>Tourne la vue du joueur de test vers `target` (avec la hausse d'un lancer à `speed`).</summary>
    private void AutopilotLookAt(Vector3 target, float speed)
    {
        if (playerCamera == null || input == null) return;
        Vector3 eye = playerCamera.transform.position;
        Vector3 to = target - eye;
        float flat = new Vector2(to.x, to.z).magnitude;
        float g = 14f;
        // Hausse du tir tendu : tan θ = (v² - √(v⁴ - g(gx² + 2yv²))) / (gx).
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * flat * flat + 2f * to.y * v2);
        float elevation = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * Mathf.Max(0.1f, flat))) * Mathf.Rad2Deg : 45f;
        float wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        input.AutopilotLookTarget(wantedYaw, -elevation);
    }
#endif
}
