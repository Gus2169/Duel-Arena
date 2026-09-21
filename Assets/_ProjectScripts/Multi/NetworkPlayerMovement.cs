using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Déplacement réseauté AVEC prédiction client + réconciliation serveur. Suite directe de la
/// version précédente (serveur-autoritaire pur) : on ajoute la prédiction pour que le propriétaire
/// ressente un contrôle instantané malgré la latence, tout en gardant le serveur comme seule
/// source de vérité (anti-triche, cohérence).
///
/// IMPORTANT — ce script remplace NetworkTransform : on gère la position à la main via
/// networkPosition (NetworkVariable écrite uniquement par le serveur), parce qu'on a besoin d'un
/// comportement différent selon le rôle (propriétaire = prédit localement, spectateur = interpole
/// depuis le réseau) que NetworkTransform ne permet pas de distinguer nativement. Si tu avais un
/// NetworkTransform sur ce prefab depuis le test précédent, retire-le.
///
/// Les 4 cas possibles pour CE composant, sur CETTE machine, pour CE joueur :
/// 1. IsOwner && IsServer  → c'est le Host qui contrôle SON PROPRE perso : autorité directe,
///    pas besoin de prédiction (il n'a aucune latence avec lui-même).
/// 2. IsOwner && !IsServer → un vrai client distant qui contrôle SON PROPRE perso : prédiction
///    locale + envoi au serveur + réconciliation. C'est le cas qu'on vient d'ajouter.
/// 3. !IsOwner && IsServer → le serveur qui fait autorité sur le perso d'un AUTRE joueur (celui
///    d'un client distant) : applique les inputs reçus, seule source de vérité.
/// 4. !IsOwner && !IsServer → un troisième spectateur qui observe le perso d'un autre joueur :
///    pure interpolation visuelle depuis la position réseau, aucune logique de jeu.
///
/// Pour vraiment tester la prédiction (cas 2), il faut jouer depuis la fenêtre CLIENT (Virtual
/// Player), pas depuis le Host — le Host passe toujours par le cas 1, qui ne prédit rien puisqu'il
/// n'en a pas besoin. Active le Packet Delay Ms du simulateur (comme testé précédemment) et
/// compare le ressenti avec/sans ce script pour voir la différence.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputReader))]
public class NetworkPlayerMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 4.4f;
    [SerializeField] private float gravity = -18f;
    [Tooltip("Délai volontaire (secondes) auquel un spectateur affiche un joueur distant, pour toujours avoir 2 points d'historique connus entre lesquels interpoler. ~0.1s est un bon point de départ ; plus haut = plus lisse mais plus 'en retard', plus bas = plus réactif mais plus sensible aux irrégularités réseau.")]
    [SerializeField] private float interpolationDelay = 0.1f;

    private CharacterController controller;
    private PlayerInputReader input;

    private readonly NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float verticalVelocity;
    private int nextInputSequence;

    private struct PendingInput
    {
        public int sequence;
        public Vector2 moveInput;
        public float deltaTime;
    }

    // Côté propriétaire distant (cas 2) : inputs envoyés au serveur mais pas encore confirmés.
    private readonly List<PendingInput> unconfirmedInputs = new List<PendingInput>();

    // Côté serveur (cas 3) : inputs reçus des clients, en attente de traitement.
    private readonly Queue<PendingInput> serverInputQueue = new Queue<PendingInput>();

    private struct PositionSnapshot
    {
        public float time;
        public Vector3 position;
    }

    // Côté spectateur (cas 4) : petit historique récent des positions reçues, avec leur instant
    // de réception locale — sert à interpoler PROPREMENT entre deux points connus plutôt que de
    // courir après le dernier reçu (ce qui saccade dès que les paquets arrivent irrégulièrement).
    private readonly List<PositionSnapshot> remoteSnapshots = new List<PositionSnapshot>();

    // ------------------------------------------------------------------
    // Posture (test) — Debout/Accroupi uniquement pour l'instant, juste pour valider le pattern
    // avant de fusionner avec le vrai PlayerLocomotion (qui a Standing/Crouching/Prone + CanStandUp()).
    // Contrairement à Move(), pas de prédiction ici : les changements de posture sont peu
    // fréquents, donc attendre la confirmation serveur reste un compromis acceptable pour
    // l'instant. À revoir avec le même principe prédiction/réconciliation si ça se sent mou.
    // ------------------------------------------------------------------

    public enum PlayerStance { Standing, Crouching }

    [Header("Posture (test)")]
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float standingRadius = 0.35f;
    [SerializeField] private float crouchingHeight = 1.1f;
    [SerializeField] private float crouchingRadius = 0.35f;
    [SerializeField] private float stanceTransitionSpeed = 8f;

    [Tooltip("Transform de la capsule visuelle enfant (juste pour VOIR le joueur en jeu — n'affecte jamais la collision, qui reste gérée uniquement par le CharacterController du parent). Assigne l'enfant 'Capsule'. Laisse vide si tu n'as pas de mesh visuel.")]
    [SerializeField] private Transform visualCapsule;

    private readonly NetworkVariable<PlayerStance> networkStance = new NetworkVariable<PlayerStance>(
        PlayerStance.Standing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<PlayerInputReader>();
    }

    public override void OnNetworkSpawn()
    {
        // Évite un "téléport" visuel au spawn pour les spectateurs : on part de la position
        // réseau plutôt que (0,0,0) le temps du tout premier update.
        transform.position = networkPosition.Value;
        networkPosition.OnValueChanged += HandleNetworkPositionChanged;

        ApplyStanceImmediate(networkStance.Value);
    }

    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= HandleNetworkPositionChanged;
    }

    private void HandleNetworkPositionChanged(Vector3 previous, Vector3 current)
    {
        // Seul un spectateur (cas 4) a besoin de bufferiser un historique pour interpoler — le
        // propriétaire gère sa propre prédiction, et le serveur/host n'observe jamais son propre
        // perso de cette façon (il EST la position, pas un reflet en retard de celle-ci).
        if (IsOwner || IsServer) return;

        remoteSnapshots.Add(new PositionSnapshot { time = Time.time, position = current });

        float cutoff = Time.time - 1f;
        remoteSnapshots.RemoveAll(s => s.time < cutoff);
    }

    private void Update()
    {
        if (IsOwner && IsServer)
        {
            // Cas 1 : Host sur son propre perso — autorité directe, comme avant l'ajout de la
            // prédiction. Pas de ServerRpc, pas de queue : latence nulle avec soi-même.
            Move(input.MoveInput, Time.deltaTime);
            networkPosition.Value = transform.position;
        }
        else if (IsOwner)
        {
            // Cas 2 : vrai client distant sur son propre perso — prédiction + envoi au serveur.
            HandleOwnerPrediction();
        }
        else if (IsServer)
        {
            // Cas 3 : le serveur fait autorité sur le perso d'un client distant.
            ApplyBufferedServerInputs();
        }
        else
        {
            // Cas 4 : simple spectateur d'un joueur qu'il ne possède pas — interpolation visuelle
            // uniquement, jamais de logique de mouvement ici.
            UpdateRemoteInterpolation();
        }

        if (IsOwner)
        {
            // La posture se lit APRÈS le mouvement mais AVANT de vider les flags d'input du
            // frame (ConsumeFrameInputs) : sinon CrouchPressedThisFrame serait déjà effacé.
            UpdateStanceInput();
            input.ConsumeFrameInputs();
        }

        // Tourne sur TOUTES les instances (propriétaire, spectateurs, serveur) : contrairement au
        // mouvement, ce n'est pas une branche par rôle — c'est un effet purement dérivé de
        // networkStance, identique partout où on l'observe.
        UpdateStanceVisual();
    }

    // ------------------------------------------------------------------
    // Cas 4 — spectateur : interpolation par historique avec délai volontaire, plutôt que de
    // courir après le dernier point reçu. On affiche toujours le joueur distant tel qu'il était
    // il y a "interpolationDelay" secondes, en interpolant entre les deux snapshots connus qui
    // encadrent cet instant — le mouvement reste lisse même si les paquets réseau arrivent par
    // à-coups irréguliers, puisqu'on ne dépend jamais d'un seul point "le plus récent".
    // ------------------------------------------------------------------

    private void UpdateRemoteInterpolation()
    {
        if (remoteSnapshots.Count == 0)
        {
            transform.position = networkPosition.Value;
            return;
        }

        float renderTime = Time.time - interpolationDelay;

        for (int i = 0; i < remoteSnapshots.Count - 1; i++)
        {
            if (remoteSnapshots[i].time <= renderTime && renderTime <= remoteSnapshots[i + 1].time)
            {
                float span = remoteSnapshots[i + 1].time - remoteSnapshots[i].time;
                float t = span > 0.0001f ? (renderTime - remoteSnapshots[i].time) / span : 0f;
                transform.position = Vector3.Lerp(remoteSnapshots[i].position, remoteSnapshots[i + 1].position, t);
                return;
            }
        }

        // renderTime tombe avant le premier snapshot connu ou après le dernier : on prend le plus
        // proche plutôt que d'extrapoler — extrapoler serait plus réactif, mais risquerait de
        // "deviner" une trajectoire fausse si l'adversaire change soudainement de direction, ce
        // qui n'est pas acceptable pour un jeu où on vise précisément un joueur distant.
        PositionSnapshot fallback = renderTime < remoteSnapshots[0].time
            ? remoteSnapshots[0]
            : remoteSnapshots[remoteSnapshots.Count - 1];
        transform.position = fallback.position;
    }

    // ------------------------------------------------------------------
    // Cas 2 — propriétaire distant : prédiction locale + envoi au serveur
    // ------------------------------------------------------------------

    private void HandleOwnerPrediction()
    {
        Vector2 moveInput = input.MoveInput;
        float dt = Time.deltaTime;

        int sequence = nextInputSequence++;
        unconfirmedInputs.Add(new PendingInput { sequence = sequence, moveInput = moveInput, deltaTime = dt });

        // Application immédiate en local : c'est ce qui donne un ressenti instantané, sans
        // attendre l'aller-retour serveur. Utilise la MÊME fonction Move() que le serveur, pour
        // rester déterministe — condition indispensable pour que la réconciliation par rejeu
        // (plus bas) fonctionne sans à-coup.
        Move(moveInput, dt);

        SubmitInputServerRpc(sequence, moveInput, dt);

        // ConsumeFrameInputs() est appelé une seule fois, dans Update(), après UpdateStanceInput()
        // — pas ici — pour que la posture puisse encore lire les flags de ce frame.
    }

    [ServerRpc]
    private void SubmitInputServerRpc(int sequence, Vector2 moveInput, float deltaTime)
    {
        serverInputQueue.Enqueue(new PendingInput { sequence = sequence, moveInput = moveInput, deltaTime = deltaTime });
    }

    // ------------------------------------------------------------------
    // Cas 3 — serveur : seule source de vérité pour le perso d'un client distant
    // ------------------------------------------------------------------

    private void ApplyBufferedServerInputs()
    {
        int lastProcessedSequence = -1;

        while (serverInputQueue.Count > 0)
        {
            PendingInput next = serverInputQueue.Dequeue();
            Move(next.moveInput, next.deltaTime);
            lastProcessedSequence = next.sequence;
        }

        if (lastProcessedSequence >= 0)
        {
            networkPosition.Value = transform.position;
            SendCorrectionToOwner(lastProcessedSequence, transform.position, verticalVelocity);
        }
    }

    private void SendCorrectionToOwner(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity)
    {
        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveCorrectionClientRpc(confirmedSequence, confirmedPosition, confirmedVerticalVelocity, targetParams);
    }

    // ------------------------------------------------------------------
    // Réconciliation (toujours cas 2, côté propriétaire) : on se recale sur la position confirmée
    // par le serveur, puis on rejoue tous les inputs envoyés depuis cette confirmation pour
    // rattraper le présent. Comme Move() est déterministe, si la prédiction locale était déjà
    // juste, le rejeu retombe exactement au même endroit — aucun à-coup visible. Si elle était
    // fausse (collision inattendue, désync...), le rejeu corrige en douceur plutôt qu'en un
    // téléport brutal figé.
    // ------------------------------------------------------------------

    [ClientRpc]
    private void ReceiveCorrectionClientRpc(int confirmedSequence, Vector3 confirmedPosition, float confirmedVerticalVelocity, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        unconfirmedInputs.RemoveAll(p => p.sequence <= confirmedSequence);

        controller.enabled = false; // repositionnement propre, sans que CharacterController ne râle sur un déplacement instantané
        transform.position = confirmedPosition;
        controller.enabled = true;
        verticalVelocity = confirmedVerticalVelocity;

        foreach (PendingInput pending in unconfirmedInputs)
        {
            Move(pending.moveInput, pending.deltaTime);
        }
    }

    // ------------------------------------------------------------------
    // Posture : requête propriétaire → validation/confirmation serveur → effet visuel partout
    // ------------------------------------------------------------------

    private void UpdateStanceInput()
    {
        if (!input.CrouchPressedThisFrame) return;

        PlayerStance desired = networkStance.Value == PlayerStance.Standing
            ? PlayerStance.Crouching
            : PlayerStance.Standing;

        RequestStanceChangeServerRpc(desired);
    }

    [ServerRpc]
    private void RequestStanceChangeServerRpc(PlayerStance desired)
    {
        // Validation minimale pour ce test : on autorise toujours le changement. Une fois fusionné
        // avec le vrai PlayerLocomotion, c'est ici qu'il faudra brancher l'équivalent réseauté de
        // CanStandUp() (Physics.CheckCapsule) avant d'autoriser à se relever sous un plafond bas.
        networkStance.Value = desired;
    }

    private void ApplyStanceImmediate(PlayerStance stance)
    {
        var (height, radius) = GetStanceProfile(stance);
        controller.height = height;
        controller.radius = radius;
        controller.center = new Vector3(0f, height / 2f, 0f);
        ApplyVisualCapsule(height, radius);
    }

    private void UpdateStanceVisual()
    {
        var (targetHeight, targetRadius) = GetStanceProfile(networkStance.Value);

        controller.height = Mathf.MoveTowards(controller.height, targetHeight, stanceTransitionSpeed * Time.deltaTime);
        controller.radius = Mathf.MoveTowards(controller.radius, targetRadius, stanceTransitionSpeed * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height / 2f, 0f);

        // Le mesh visuel suit la taille COURANTE du collider (pas directement la cible) pour que
        // la capsule qu'on voit se rétrécisse en douceur en même temps que la vraie collision,
        // sur toutes les instances (propriétaire, spectateurs, serveur).
        ApplyVisualCapsule(controller.height, controller.radius);
    }

    private void ApplyVisualCapsule(float height, float radius)
    {
        if (visualCapsule == null) return;

        // La primitive Capsule par défaut d'Unity fait 2 unités de haut / 0.5 de rayon à l'échelle
        // 1, pivot au centre — d'où les facteurs /2 et *2 pour convertir hauteur/rayon "monde" en
        // scale local. Purement cosmétique : ne touche à aucun collider.
        visualCapsule.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        visualCapsule.localPosition = new Vector3(0f, height / 2f, 0f);
    }

    private (float height, float radius) GetStanceProfile(PlayerStance stance)
    {
        return stance == PlayerStance.Crouching
            ? (crouchingHeight, crouchingRadius)
            : (standingHeight, standingRadius);
    }

    // ------------------------------------------------------------------
    // Fonction de mouvement partagée — DOIT rester strictement identique entre la prédiction
    // locale, le traitement serveur et le rejeu de réconciliation. Toute divergence ici casse le
    // déterminisme et réintroduit des à-coups visibles.
    // ------------------------------------------------------------------

    private void Move(Vector2 moveInput, float deltaTime)
    {
        Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        Vector3 worldDir = transform.TransformDirection(inputDir);

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1f;
        }
        verticalVelocity += gravity * deltaTime;

        Vector3 motion = worldDir * moveSpeed;
        motion.y = verticalVelocity;
        controller.Move(motion * deltaTime);
    }
}
