using UnityEngine;

/// <summary>
/// Pilote l'Animator du personnage à partir de l'état affiché du joueur.
///
/// CATÉGORIE C — purement cosmétique. Ce script LIT, il n'écrit jamais rien que la simulation
/// relise. C'est la règle centrale du projet côté animation : l'animation AFFICHE, elle ne décide
/// pas. Un Animator n'est pas déterministe entre machines (blending, vitesse de lecture,
/// LateUpdate), donc tout ce qui en dépendrait ferait diverger les instances.
///
/// Contrairement aux autres scripts cosmétiques, il tourne sur TOUTES les instances et non
/// seulement chez le propriétaire : il faut bien voir son adversaire bouger.
///
/// C'est précisément ce qui dicte d'où vient la vitesse. `currentVelocity` n'existe que là où la
/// simulation tourne (propriétaire et serveur) ; chez un spectateur elle vaudrait zéro en
/// permanence et l'adversaire glisserait sans bouger les jambes. Le DÉPLACEMENT DU TRANSFORM,
/// lui, est juste dans les quatre cas réseau — y compris l'interpolation du spectateur, qui est
/// justement ce qu'on veut montrer.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerLocomotion))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("Animator du personnage (enfant Model). Laissé vide, il est cherché dans les enfants.")]
    [SerializeField] private Animator animator;

    // Les vitesses de référence (celles où l'arbre de mélange vaut 1) ne sont plus recopiées ici :
    // elles sont LUES dans PlayerLocomotion.NominalSpeed. La copie s'était désaccordée en silence
    // (4,4 m/s ici contre 5 m/s réglés dans le prefab), et l'animation tournait 14 % trop vite.

    [Tooltip("Lissage des paramètres de mélange. Purement visuel : évite qu'un à-coup d'une frame ne fasse claquer l'animation.")]
    [SerializeField] private float blendSmoothing = 12f;

    [Tooltip("Durée de la partie UTILE du clip de vault, en secondes — pas sa durée totale. Le contrôleur entre dans l'état à 20 % du clip pour sauter la préparation, et la réception est coupée par la sortie d'état. Sert à lire le geste à la vitesse qui le fait tenir dans la durée du vault.")]
    [SerializeField] private float vaultClipLength = 0.70f;

    [Header("Lean et posture allongée (affichage)")]
    [Tooltip("Recul du modèle en position allongée, en mètres (Z local). Sa tête tombe ainsi à l'intérieur du CharacterController, là où la caméra et la tête touchable la placent (voir BodyLayout.DefaultProne). Doit rester accordé à ces deux valeurs.")]
    [SerializeField] private float proneModelOffset = -0.30f;
    [Tooltip("Vitesse (m/s) à laquelle le modèle glisse vers ce décalage. Purement visuel : la surface touchable, elle, change de posture d'un coup.")]
    [SerializeField] private float modelOffsetSpeed = 1.5f;

    [Header("Cadence de lecture")]
    [Tooltip("Plancher du multiplicateur de vitesse de lecture. Ne descend pas à 0 : il s'applique aussi à l'animation d'attente, qui doit continuer de respirer à l'arrêt.")]
    [SerializeField] private float minPlaybackSpeed = 0.75f;

    [Tooltip("Cadence à l'allure nominale, par posture. Les clips Mixamo ne sont pas authorés à la même échelle que les vitesses du jeu : c'est le réglage à toucher si les pieds patinent ou si la foulée paraît frénétique.")]
    [SerializeField] private float standingPlaybackScale = 0.85f;
    [SerializeField] private float crouchingPlaybackScale = 1f;
    [SerializeField] private float pronePlaybackScale = 1.4f;

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly int StanceId = Animator.StringToHash("Stance");
    private static readonly int SpeedMultId = Animator.StringToHash("SpeedMult");
    private static readonly int AirborneId = Animator.StringToHash("Airborne");
    private static readonly int VaultingId = Animator.StringToHash("Vaulting");
    private static readonly int VaultSpeedId = Animator.StringToHash("VaultSpeed");
    private static readonly int StanceFId = Animator.StringToHash("StanceF");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int FireId = Animator.StringToHash("Fire");

    private PlayerLocomotion locomotion;
    private WeaponController weapon;
    private Vector3 previousPosition;
    private Vector2 smoothedMove;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        weapon = GetComponentInChildren<WeaponController>();
        previousPosition = transform.position;
    }

    private void OnEnable()
    {
        if (weapon != null) weapon.OnShotFired += DeclencherAnimationDeTir;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.OnShotFired -= DeclencherAnimationDeTir;
    }

    /// <summary>Un coup part : on relance le geste de tir sur le haut du corps.
    ///
    /// Piloté par ÉVÉNEMENT et non par sondage d'un état, parce que tirer est un instant et
    /// non une condition qui dure — sonder raterait les coups tombant entre deux frames.
    /// L'événement est levé des deux côtés par WeaponController : chez le tireur depuis Fire(),
    /// chez tous les autres depuis la diffusion serveur qui existait déjà pour le son et le
    /// tracer. Aucun réseau supplémentaire n'a été nécessaire.</summary>
    private void DeclencherAnimationDeTir()
    {
        if (animator != null) animator.SetTrigger(FireId);
    }

    /// <summary>
    /// LateUpdate et pas Update : Move() est appelée depuis Update(), donc la position n'est
    /// définitive qu'après. Lire trop tôt donnerait la vitesse de la frame précédente.
    /// </summary>
    private void LateUpdate()
    {
        if (animator == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        delta.y = 0f;

        Vector3 local = transform.InverseTransformDirection(delta / dt);

        // NetworkedStance et non CurrentStance : cette dernière n'est mise à jour que dans Move(),
        // qu'un spectateur n'appelle jamais. Elle y resterait bloquée sur Standing, et l'adversaire
        // n'aurait jamais d'animation accroupie ni allongée. Symptôme vécu le 2026-09-29.
        PlayerLocomotion.Stance stance = locomotion.NetworkedStance;
        float reference = locomotion.NominalSpeed(stance);
        Vector2 target = new Vector2(local.x, local.z) / Mathf.Max(0.01f, reference);

        // Borne volontaire. Une resynchronisation de réconciliation TÉLÉPORTE le propriétaire de
        // quelques centimètres en une frame : dérivée telle quelle, cette téléportation vaut
        // plusieurs mètres par seconde et ferait sursauter l'animation à chaque recalage. On
        // plafonne donc à un peu plus que le sprint, ce qui laisse passer tout mouvement réel.
        target = Vector2.ClampMagnitude(target, 2.5f);

        smoothedMove = Vector2.Lerp(smoothedMove, target, 1f - Mathf.Exp(-blendSmoothing * dt));

        animator.SetFloat(MoveXId, smoothedMove.x);
        animator.SetFloat(MoveYId, smoothedMove.y);

        animator.SetInteger(StanceId, (int)stance);

        // Miroir FLOTTANT de la posture. Un arbre de melange n'accepte qu'un parametre float,
        // alors que les transitions de la couche de deplacement ont besoin d'un entier pour
        // comparer exactement. Duplication imposee par Unity, pas un choix.
        animator.SetFloat(StanceFId, (int)stance);

        // Un arbre de mélange NE modifie PAS la cadence de ses clips. À mi-vitesse il mélange
        // l'attente et la course, mais la course joue à 100 % de sa cadence pendant que le corps
        // n'avance qu'à moitié : les jambes s'agitent sans que le personnage suive. On accorde
        // donc la vitesse de LECTURE au déplacement réel.
        //
        // 🚨 PLAFONNÉ À 1, et c'est le point qui n'était pas évident. Au-delà de l'allure
        // nominale, ce n'est pas la cadence qui doit monter mais le CLIP qui change : à MoveY=2
        // l'arbre joue Sprint Forward, déjà authorée pour sprinter. Une première version plafonnait
        // à 1,5 et accélérait donc de 50 % une animation qui n'en avait aucun besoin — le sprint
        // paraissait frénétique.
        //
        // Le plancher, lui, ne descend pas à zéro : ce multiplicateur pilote l'état entier,
        // animation d'attente comprise, qui doit continuer de respirer à l'arrêt.
        float cadence = Mathf.Clamp(smoothedMove.magnitude, minPlaybackSpeed, 1f)
                        * PlaybackScale(stance);
        animator.SetFloat(SpeedMultId, cadence);

        // Chute et franchissement passent par des accesseurs qui choisissent eux-mêmes entre la
        // valeur locale et la valeur réseautée selon le cas réseau. Ce script n'a donc pas à
        // connaître la notion de propriétaire — et il ne peut pas se tromper de source, ce qui
        // est exactement l'erreur commise sur la posture.
        animator.SetBool(AirborneId, locomotion.DisplayAirborne);
        animator.SetBool(VaultingId, locomotion.DisplayVaulting);

        // Le clip de vault est lu à la vitesse qui fait tenir sa partie UTILE dans la durée
        // réelle du franchissement.
        //
        // Deux pièges déjà payés ici. Le premier clip avait son geste à mi-course : sur 0,45 s on
        // n'en voyait que l'élan, d'où un personnage qui restait debout en passant l'obstacle. Le
        // second, joué en entier, était comprimé 2,6x et devenait illisible — alors que 40 % de
        // sa durée sont une préparation et une réception quasi immobiles. On entre donc dans
        // l'état à 20 % du clip et on n'étale que l'arc, ce qui tombe à 1,56x sans toucher au
        // gameplay. Au-delà, c'est vaultDuration qu'il faut allonger, et ça change le jeu.
        float duree = Mathf.Max(0.05f, locomotion.VaultDuration);
        animator.SetFloat(VaultSpeedId, vaultClipLength / duree);

        // La visée passe par le même aiguillage que le reste : IsAiming est affectée dans
        // Move(), qu'un spectateur n'appelle jamais, donc l'adversaire n'épaulerait jamais.
        animator.SetBool(AimingId, locomotion.DisplayAiming);

        ApplyProneModelOffset(stance, dt);
        ApplyVisualLean(stance);
    }

    /// <summary>Allongé, le modèle recule pour que sa tête soit là où sont la caméra et la tête
    /// touchable, à l'intérieur du CharacterController. Glissement en douceur : c'est de
    /// l'affichage, la surface touchable change de posture d'un coup.</summary>
    private void ApplyProneModelOffset(PlayerLocomotion.Stance stance, float dt)
    {
        Transform model = animator.transform;
        float target = stance == PlayerLocomotion.Stance.Prone ? proneModelOffset : 0f;
        Vector3 p = model.localPosition;
        p.z = Mathf.MoveTowards(p.z, target, modelOffsetSpeed * dt);
        model.localPosition = p;
    }

    /// <summary>
    /// Lean façon Rainbow Six, VU : le buste pivote autour de la base de la colonne, les jambes ne
    /// bougent pas.
    ///
    /// La rotation vient de BodyLayout, avec le même décalage réseauté que la surface touchable :
    /// l'animation et le hitbox lisent la même source, aucun ne lit l'autre. On touche ce qu'on
    /// voit, sans que le hitbox dépende jamais d'un os animé.
    ///
    /// Appliquée ici, en LateUpdate, APRÈS l'évaluation de l'Animator : la rotation s'ajoute à la
    /// pose de la frame, que l'Animator réécrit à chaque évaluation. D'où l'obligation de garder
    /// l'Animator en AlwaysAnimate : s'il cessait d'évaluer (modèle hors champ), la rotation
    /// s'accumulerait frame après frame et le buste tournerait sur lui-même.
    /// </summary>
    private void ApplyVisualLean(PlayerLocomotion.Stance stance)
    {
        PlayerHitbox hitbox = locomotion.Hitbox;
        if (hitbox == null) return;

        float lean = locomotion.DisplayLeanOffset;
        if (Mathf.Abs(lean) < 0.001f) return;

        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (spine == null) return;

        // Rotation exprimée dans l'espace du JOUEUR : on la convertit dans le repère monde, puis
        // on l'applique à l'os autour de sa propre position (la base de la colonne).
        Quaternion local = hitbox.LeanRotation(stance, locomotion.EyeLocal(stance), lean);
        Quaternion world = transform.rotation * local * Quaternion.Inverse(transform.rotation);
        spine.rotation = world * spine.rotation;
    }

    /// <summary>Accorde la cadence d'une posture à ses clips. Mixamo n'authore pas ses animations
    /// à l'échelle des vitesses de ce jeu : le ramper est plus lent que la vitesse prone réelle,
    /// la course debout plus rapide que la marche. C'est un réglage de RESSENTI, à ajuster en
    /// jouant et non par calcul.</summary>
    private float PlaybackScale(PlayerLocomotion.Stance stance)
    {
        if (stance == PlayerLocomotion.Stance.Crouching) return crouchingPlaybackScale;
        if (stance == PlayerLocomotion.Stance.Prone) return pronePlaybackScale;
        return standingPlaybackScale;
    }
}
