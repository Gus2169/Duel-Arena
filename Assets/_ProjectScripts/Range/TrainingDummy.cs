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
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class TrainingDummy : NetworkBehaviour
{
    [Tooltip("Le temps (s) sans être touché après lequel le mannequin retrouve toute sa vie.")]
    [SerializeField] private float regenDelay = 3f;

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
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Health health;
    private PlayerHitbox hitbox;
    private PlayableGraph idleGraph;
    private AnimationClipPlayable idlePlayable;
    private float lastSeenHealth = -1f;
    private double lastHitAt = double.NegativeInfinity;

    public Health Health => health;
    public PlayerHitbox Hitbox => hitbox;
    public bool Regenerates => regenerates;
    public float RegenDelay => regenDelay;
    public float EyeHeight => eyeHeight;

    /// <summary>Le point au-dessus de la tête où le stand affiche sa vie (monde).</summary>
    public Vector3 LabelPoint => transform.position + Vector3.up * (eyeHeight + 0.45f);

    /// <summary>Le centre de la tête touchable (monde) : les chiffres d'une touche à la tête y partent.</summary>
    public Vector3 HeadPoint => transform.position + Vector3.up * eyeHeight;

    private void Awake()
    {
        health = GetComponent<Health>();
        hitbox = GetComponentInChildren<PlayerHitbox>(true);
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
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;   // Default : la caméra du joueur le dessine

        var block = new MaterialPropertyBlock();
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            r.SetPropertyBlock(block);
            // Un mannequin n'a pas de lueurs (les yeux et les fentes du CT-01) : sable, éteintes.
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].name.Contains(PlayerPaint.GlowSlot)) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, tint);
                block.SetColor(EmissionColorId, Color.black);
                r.SetPropertyBlock(block, i);
            }
        }

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
}
