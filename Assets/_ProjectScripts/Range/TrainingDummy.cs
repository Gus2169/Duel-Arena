using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Un mannequin du stand de tir (2026-10-16, demandé par l'utilisateur pour essayer les armes et leurs
/// accessoires) : un robot debout, immobile, qui encaisse les tirs comme un joueur et se relève.
///
/// Il est touché exactement comme un robot : la même surface touchable (PlayerHitbox, posture debout,
/// sans lean ; ses valeurs viennent du préfab joueur, recopiées par le constructeur du stand), la même
/// vie (Health, 100 PV), les mêmes dégâts par zone et selon la distance (WeaponController.ServerFire,
/// qui ne fait pas la différence). Le serveur le soigne en entier quand il n'a pas été touché depuis
/// `regenDelay` secondes, neutralisé ou non (RegenDue).
///
/// L'apparence est une copie du modèle du robot (RobotAppearance), dans l'attente à mains nues, sur le
/// layer Default, teintée pour se distinguer d'un joueur. Catégorie C : rien ne la relit.
///
/// Neutralisé, un mannequin du stand tombe en arrière, raide, comme une cible de stand, pour qu'on le voie
/// tout de suite (2026-10-27, demandé par l'utilisateur), et se relève quand il retrouve sa vie (FallAngle).
/// Chaque machine l'anime en voyant sa vie tomber à zéro, puis revenir (catégorie C). À terre, ni sa
/// surface touchable ni son corps ne répondent : on ne touche que ce qui est debout. Les cibles de la Fosse
/// (CourseTarget) ont leur propre chute.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class TrainingDummy : NetworkBehaviour
{
    /// <summary>Durée (s) de la chute, qui accélère jusqu'au sol, et du petit rebond qui suit.</summary>
    public const float FallTime = 0.42f;
    public const float BounceTime = 0.22f;
    /// <summary>Hauteur (degrés) du rebond sur le sol.</summary>
    public const float BounceAngle = 7f;
    /// <summary>Durée (s) du relevé.</summary>
    public const float RiseTime = 0.35f;

    [Tooltip("Le temps (s) sans être touché après lequel le mannequin retrouve toute sa vie.")]
    [SerializeField] private float regenDelay = 3f;

    [Tooltip("Distance (m) derrière les pieds autour de laquelle le mannequin bascule : son dos se pose sur le sol.")]
    [SerializeField] private float fallPivotBack = 0.14f;

    [Tooltip("Se relève-t-il seul ? Faux pour les cibles de la Fosse, relevées par le parcours (CourseTarget).")]
    [SerializeField] private bool regenerates = true;

    [Tooltip("Hauteur de l'œil (m) : la tête touchable est centrée dessus, comme celle d'un robot debout (PlayerLocomotion).")]
    [SerializeField] private float eyeHeight = 1.65f;

    [Tooltip("Le modèle du robot (l'enfant « Model » du préfab joueur), copié pour l'apparence.")]
    [SerializeField] private GameObject robotModel;

    [Tooltip("L'attente à mains nues (Y Bot@Idle), jouée en boucle.")]
    [SerializeField] private AnimationClip idleClip;

    [SerializeField] private Color tint = new Color(0.86f, 0.74f, 0.42f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");   // URP Lit
    private static readonly int CelEmissionId = Shader.PropertyToID("_Emission");          // DuelArena/RobotCelShading

    private Health health;
    private PlayerHitbox hitbox;
    private PlayableGraph idleGraph;
    private AnimationClipPlayable idlePlayable;
    private float lastSeenHealth = -1f;
    private double lastHitAt = double.NegativeInfinity;

    // La chute vue sur cette machine (catégorie C).
    private Transform appearance;
    private Collider[] bodyColliders;
    private bool shownDown;
    private float shownSince;
    private bool hittable = true;

    public Health Health => health;
    public PlayerHitbox Hitbox => hitbox;
    public bool Regenerates => regenerates;
    public float RegenDelay => regenDelay;
    public float EyeHeight => eyeHeight;

    /// <summary>Le point au-dessus de la tête où le stand affiche sa vie (monde) : au-dessus de sa tête posée
    /// au sol quand il est tombé.</summary>
    public Vector3 LabelPoint => DisplayedPoint(new Vector3(0f, eyeHeight, 0f)) + Vector3.up * 0.45f;

    /// <summary>Le centre de la tête touchable (monde) : les chiffres d'une touche à la tête y partent.</summary>
    public Vector3 HeadPoint => transform.position + Vector3.up * eyeHeight;

    private void Awake()
    {
        health = GetComponent<Health>();
        hitbox = GetComponentInChildren<PlayerHitbox>(true);
        bodyColliders = GetComponents<Collider>();
        if (hitbox != null) hitbox.Apply(PlayerLocomotion.Stance.Standing, new Vector3(0f, eyeHeight, 0f), 0f);
        BuildAppearance();
    }

    public override void OnDestroy()
    {
        if (idleGraph.IsValid()) idleGraph.Destroy();
        base.OnDestroy();
    }

    private void Update()
    {
        // L'attente boucle, quel que soit le réglage de boucle de son import.
        if (idlePlayable.IsValid() && idleClip != null && idleClip.length > 0f && idlePlayable.GetTime() >= idleClip.length)
            idlePlayable.SetTime(idlePlayable.GetTime() % idleClip.length);

        // Les cibles de la Fosse tombent et se lèvent par le parcours (CourseTarget).
        if (regenerates) UpdateFall();

        if (!regenerates || !IsServer || !IsSpawned || health == null) return;
        double now = NetworkManager.ServerTime.Time;
        float current = health.Current;
        if (lastSeenHealth >= 0f && current < lastSeenHealth) lastHitAt = now;
        lastSeenHealth = current;
        if (RegenDue(current, health.Max, lastHitAt, now, regenDelay))
        {
            health.ResetHealth();
            lastSeenHealth = health.Max;
        }
    }

    /// <summary>Serveur : toute sa vie, tout de suite (le bouton « Relever les mannequins »).</summary>
    public void ServerRestore()
    {
        if (!IsServer || health == null) return;
        health.ResetHealth();
        lastSeenHealth = health.Max;
        lastHitAt = double.NegativeInfinity;
    }

    /// <summary>
    /// La chute vue sur cette machine : il tombe dès que sa vie publiée est à zéro, se relève quand elle
    /// revient. Sa surface touchable et son corps s'éteignent à la chute et ne reviennent qu'une fois debout.
    /// </summary>
    private void UpdateFall()
    {
        if (health == null || appearance == null) return;
        bool down = health.IsDead;
        if (down != shownDown)
        {
            shownDown = down;
            shownSince = Time.time;
            if (down) SetHittable(false);
            if (idlePlayable.IsValid()) idlePlayable.SetSpeed(down ? 0.0 : 1.0);   // à terre, il ne respire plus
        }

        float angle = FallAngle(shownDown, Time.time - shownSince);
        // Basculé autour d'un point derrière les talons : son dos se pose sur le sol.
        Quaternion tilt = Quaternion.AngleAxis(-angle, Vector3.right);
        Vector3 pivot = new Vector3(0f, 0f, -fallPivotBack);
        appearance.localRotation = tilt;
        appearance.localPosition = pivot + tilt * -pivot;

        if (!shownDown && !hittable && angle <= 0f) SetHittable(true);
    }

    private void SetHittable(bool value)
    {
        hittable = value;
        if (hitbox != null) hitbox.SetQueryable(value);
        foreach (Collider c in bodyColliders) if (c != null) c.enabled = value;
        // Physics.autoSyncTransforms vaut false : la surface touchable rallumée doit être là où elle se voit.
        Physics.SyncTransforms();
    }

    /// <summary>Un point du mannequin debout (repère du mannequin), là où cette machine le montre.</summary>
    private Vector3 DisplayedPoint(Vector3 local) => (appearance != null ? appearance : transform).TransformPoint(local);

    /// <summary>
    /// L'inclinaison (degrés, vers l'arrière) d'un mannequin du stand, `since` secondes après qu'il est tombé
    /// (`down`) ou s'est relevé : la chute accélère jusqu'à 90 (couché), rebondit un peu sur le sol et s'y
    /// repose ; le relevé, adouci, le ramène à 0. Fonction pure, testée.
    /// </summary>
    public static float FallAngle(bool down, float since)
    {
        if (float.IsNaN(since) || since < 0f) since = 0f;
        if (!down)
        {
            float r = Mathf.Clamp01(since / RiseTime);
            return 90f * (1f - r * r * (3f - 2f * r));
        }
        if (since < FallTime)
        {
            float t = since / FallTime;
            return 90f * t * t;
        }
        float b = Mathf.Clamp01((since - FallTime) / BounceTime);
        return 90f - BounceAngle * Mathf.Sin(b * Mathf.PI);
    }

    /// <summary>
    /// Le mannequin doit-il retrouver toute sa vie ? Oui s'il en a perdu et n'a pas été touché depuis
    /// `delay` secondes (neutralisé compris). Un instant invalide ne soigne rien. Fonction pure, testée.
    /// </summary>
    public static bool RegenDue(float current, float max, double lastHitAt, double now, float delay)
    {
        if (!(current < max)) return false;
        double since = now - lastHitAt;
        if (double.IsNaN(since)) return false;
        return since >= delay;
    }

    private void BuildAppearance()
    {
        if (robotModel == null) return;
        GameObject copy = RobotAppearance.Copy(robotModel, transform, "Apparence");
        appearance = copy.transform;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;   // Default : la caméra du joueur le dessine
        Tint(copy, tint);

        Animator animator = copy.GetComponent<Animator>();
        if (animator == null || idleClip == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;
        idleGraph = PlayableGraph.Create("AttenteMannequin");
        idleGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(idleGraph, "Attente", animator);
        idlePlayable = AnimationClipPlayable.Create(idleGraph, idleClip);
        idlePlayable.SetApplyFootIK(false);
        // Chaque mannequin a son propre instant dans l'attente : ils ne respirent pas en chœur.
        idlePlayable.SetTime(Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.11f, idleClip.length));
        output.SetSourcePlayable(idlePlayable);
        idleGraph.Play();
    }

    /// <summary>
    /// Teint la copie du robot d'une seule couleur (le sable du stand, le rouge ou le blanc des cibles de la Fosse) et
    /// éteint ses lueurs (les yeux et les fentes du CT-01), dans les deux rendus du réglage du rendu (RenderStyle) :
    /// `_EmissionColor` pour URP Lit, `_Emission` pour le cel-shading, dont le matériau garde sinon sa propre lueur. Le
    /// bloc de propriétés reste quand le réglage change les matériaux. Testé (RenderStyleTests).
    /// </summary>
    public static void Tint(GameObject appearance, Color color)
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in appearance.GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
            // Un mannequin n'a pas de lueurs : teintes, éteintes.
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].name.Contains(PlayerPaint.GlowSlot)) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, color);
                block.SetColor(EmissionColorId, Color.black);
                block.SetColor(CelEmissionId, Color.black);
                r.SetPropertyBlock(block, i);
            }
        }
    }
}
