using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// L'épave d'un robot tué : une copie de son modèle, figée dans la pose où il est mort, qui tombe
/// en ragdoll (2026-10-12, GDD § 6 : « le châssis vole en pièces »). Depuis le 2026-11-03 (l'utilisateur, d'après Sniper
/// Elite et Mortal Kombat), le coup qui tue l'arrache en partie : des pièces volent, l'huile gicle, des étincelles et des
/// éclats partent (Burst, RobotBurst). Le coup choisit aussi comment il tombe (Fall, CorpseFall).
///
/// Une COPIE, et non le modèle lui-même : l'Animator, le calage des pieds, le buste et le lean
/// réécrivent les os à chaque image, ils se battraient avec la physique. Le modèle vivant est
/// simplement masqué (PlayerDeath), et réapparaît intact à la manche suivante. Ce n'est pas non
/// plus un ragdoll accroché au modèle en permanence (écarté le 2026-09-24 : un Rigidbody par os,
/// en continu) : les corps n'existent que le temps d'une mort.
///
/// Catégorie C pure. Chaque machine fait tomber sa propre épave : elle ne tombe pas exactement
/// pareil sur deux écrans, et rien de jouable n'en dépend. Elle vit sur le layer Corpse, qui ne
/// touche que le décor (voir CorpseLayer).
///
/// Les corps reprennent ceux de l'assistant Ragdoll d'Unity (bassin, buste, tête, bras, avant-bras,
/// cuisses, jambes). Les limites des membres se comptent depuis la pose de la mort, comme un corps
/// qui peut encore se plier et se tendre (AddArm, AddLeg).
/// </summary>
public class RobotCorpse : MonoBehaviour
{
    private readonly List<Rigidbody> bodies = new List<Rigidbody>();
    private readonly Dictionary<RagdollPart, Rigidbody> parts = new Dictionary<RagdollPart, Rigidbody>();
    private readonly List<Transform> detached = new List<Transform>();
    private bool burst;

    /// <summary>Une pièce arrachée dont les gerbes restent à monter, son moignon noté dans le repère de ce qui le porte :
    /// tout bouge d'ici là.</summary>
    private struct TornPiece
    {
        public Transform stumpBody;
        public Vector3 stump, outward;
        public Transform piece;
    }

    private readonly Queue<TornPiece> tornPieces = new Queue<TornPiece>();

    // Les gerbes de l'épave : la kill cam règle leur vitesse à chaque image (SetBurstSpeed).
    private static readonly List<ParticleSystem> FoundSystems = new List<ParticleSystem>();
    private readonly List<ParticleSystem> burstSystems = new List<ParticleSystem>();
    private readonly List<BurstFlash> burstFlashes = new List<BurstFlash>();
    private float burstSpeed = 1f;

    private Rigidbody pelvis;
    private Transform liveRoot;
    private float totalMass;

    // Les coups qui la frappent (2026-10-12, retour de l'utilisateur : au pompe et au taser, le
    // ragdoll était « violent »). Un coup pousse surtout TOUT le corps ; la pièce touchée n'en garde
    // qu'une part, plafonnée en vitesse. Avant, elle en prenait tout (un tir sur l'épave) ou 70 %
    // (le coup mortel) : un plomb lançait un avant-bras de 1,8 kg à 5 m/s, une balle de sniper une
    // tête de 5 kg à 10 m/s, et l'épave fouettait.
    private const float HitBodyShare = 0.35f;
    private const float HitBodyMaxDeltaV = 1.5f; // m/s

    /// <summary>La vitesse que l'épave hérite au plus du robot vivant (m/s, à plat). Chez l'hôte, le
    /// robot d'un client avance par à-coups (dette n° 30) : une image de rattrapage la lançait à
    /// 12 m/s. Un robot tué en courant roule encore un peu, sans glisser au loin.</summary>
    public const float MaxInheritedSpeed = 2f;

    // Les masses (kg) des pièces, celles de l'assistant Ragdoll d'Unity.
    private const float PelvisMass = 12f, TorsoMass = 14f, HeadMass = 5f, ArmMass = 2.5f, ForearmMass = 1.8f, ThighMass = 7f, CalfMass = 4f;

    /// <summary>La masse d'une épave entière (kg) : un coup puissant se règle en vitesse, convertie par
    /// elle en impulsion avant de voyager (KnockbackImpulse).</summary>
    public const float RobotMass = PelvisMass + TorsoMass + HeadMass + 2f * (ArmMass + ForearmMass) + 2f * (ThighMass + CalfMass);

    /// <summary>
    /// L'impulsion (N·s) d'un coup PUISSANT (le pompe, le taser, 2026-10-16 : « un réel effet de
    /// puissance ») : de quoi lancer l'épave entière à `speed` m/s, à plat dans le sens de `direction`,
    /// et la soulever à `lift` m/s. Un tir plongeant ne l'enfonce pas dans le sol. Fonction pure, testée.
    /// </summary>
    public static Vector3 KnockbackImpulse(Vector3 direction, float speed, float lift)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.zero;
        return (flat * Mathf.Max(0f, speed) + Vector3.up * Mathf.Max(0f, lift)) * RobotMass;
    }

    private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

    /// <summary>Centre de l'épave (le bassin), suivi par la vue du joueur mort.</summary>
    public Vector3 Center => pelvis != null ? pelvis.worldCenterOfMass : transform.position;

    public IReadOnlyList<Rigidbody> Bodies => bodies;

    /// <summary>La masse de ses pièces (kg) : RobotMass pour un robot entier.</summary>
    public float TotalMass => totalMass;

    /// <summary>
    /// Crée l'épave du modèle animé par `liveAnimator`, dans sa pose de l'image, lancée à
    /// `velocity` (la vitesse du robot quand il est tombé). Null si le modèle n'est pas humanoïde.
    /// </summary>
    public static RobotCorpse Spawn(Animator liveAnimator, Vector3 velocity)
    {
        if (liveAnimator == null || !liveAnimator.isHuman) return null;
        Transform live = liveAnimator.transform;

        // Copiée sous un parent INACTIF : aucun script de la copie ne se réveille (Awake, OnEnable)
        // avant qu'on l'ait débarrassée de tout ce qui n'est pas de l'apparence.
        var holder = new GameObject("EpaveEnPreparation");
        holder.SetActive(false);
        GameObject copy = Instantiate(live.gameObject, live.position, live.rotation, holder.transform);
        copy.name = "Epave";
        StripToAppearance(copy);

        int layer = CorpseLayer.Index;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (layer >= 0) t.gameObject.layer = layer;
        }
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            // Le corps du joueur mort lui était invisible (ombre seule) : son épave, non.
            r.shadowCastingMode = ShadowCastingMode.On;
            r.forceRenderingOff = false;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        // Hors service : la visière affiche FATAL ERROR, les lueurs s'éteignent (un robot sans visière n'a rien à afficher).
        RobotFaceScreen.ShowDeath(copy, Time.timeSinceLevelLoad);

        copy.transform.SetParent(null, true);
        Destroy(holder);

        var corpse = copy.AddComponent<RobotCorpse>();
        corpse.liveRoot = live;
        corpse.Build(liveAnimator, copy.transform, LimitInheritedVelocity(velocity));
        return corpse;
    }

    /// <summary>La vitesse héritée, bornée : `MaxInheritedSpeed` à plat, et pas d'envol (1 m/s au
    /// plus vers le haut, comme au sommet d'un franchissement).</summary>
    public static Vector3 LimitInheritedVelocity(Vector3 velocity)
    {
        if (!float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z)) return Vector3.zero;
        Vector3 flat = Vector3.ClampMagnitude(new Vector3(velocity.x, 0f, velocity.z), MaxInheritedSpeed);
        return new Vector3(flat.x, Mathf.Clamp(velocity.y, -MaxInheritedSpeed, 1f), flat.z);
    }

    /// <summary>La pièce de l'épave qui correspond à `liveRenderer` (même chemin sous le modèle),
    /// pour la peindre : un MaterialPropertyBlock ne se copie pas avec l'objet.</summary>
    public Renderer TwinOf(Renderer liveRenderer)
    {
        if (liveRenderer == null || liveRoot == null) return null;
        Transform twin = Twin(liveRoot, transform, liveRenderer.transform);
        return twin != null ? twin.GetComponent<Renderer>() : null;
    }

    /// <summary>
    /// Le coup qui a tué fait tomber l'épave (`impulse`, N·s, reçu en `point`, de la proximité `closeness` : 1 à bout portant,
    /// 0 au bout de la portée de l'arme) : une façon de tomber choisie selon lui et la graine `seed` (CorpseFall).
    /// </summary>
    public void Fall(Vector3 point, Vector3 impulse, float closeness, int seed)
    {
        if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
        if (!float.IsFinite(impulse.x) || !float.IsFinite(impulse.y) || !float.IsFinite(impulse.z)) return;
        Vector3 center = CenterOfMass();
        CorpseFall.Plan plan = PlanFor(point, impulse, closeness, seed);
        LastFall = plan;
        Push(NearestBody(point), point, plan.Push);
        // La rotation de tout le corps, d'un bloc : à chaque pièce la vitesse que lui donne sa place. Rien ne fouette.
        foreach (Rigidbody body in bodies)
        {
            if (body == null) continue;
            body.AddForce(Vector3.Cross(plan.Spin, body.worldCenterOfMass - center), ForceMode.VelocityChange);
            body.AddTorque(plan.Spin, ForceMode.VelocityChange);
        }
        foreach (RagdollPart part in UpperBody) AddVelocity(part, Vector3.down * plan.Slump);
        Vector3 forward = transform.forward;
        AddVelocity(RagdollPart.LeftThigh, forward * plan.LeftKnee);
        AddVelocity(RagdollPart.LeftCalf, forward * (plan.LeftKnee * 0.4f));
        AddVelocity(RagdollPart.RightThigh, forward * plan.RightKnee);
        AddVelocity(RagdollPart.RightCalf, forward * (plan.RightKnee * 0.4f));
        AddSpin(RagdollPart.LeftArm, plan.LeftArm);
        AddSpin(RagdollPart.LeftForearm, plan.LeftArm * 0.6f);
        AddSpin(RagdollPart.RightArm, plan.RightArm);
        AddSpin(RagdollPart.RightForearm, plan.RightArm * 0.6f);
    }

    /// <summary>La chute que le coup a choisie.</summary>
    public CorpseFall.Plan LastFall { get; private set; }

    private CorpseFall.Plan PlanFor(Vector3 point, Vector3 impulse, float closeness, int seed) => CorpseFall.For(new CorpseFall.Blow
    {
        Impulse = impulse,
        Mass = totalMass,
        Hit = NearestPart(point),
        Side = Mathf.Clamp(Vector3.Dot(point - CenterOfMass(), transform.right) / 0.2f, -1f, 1f),
        Forward = transform.forward,
        Closeness = closeness,
        Seed = seed,
    });

    private static readonly RagdollPart[] UpperBody =
    {
        RagdollPart.Pelvis, RagdollPart.Torso, RagdollPart.Head,
        RagdollPart.LeftArm, RagdollPart.LeftForearm, RagdollPart.RightArm, RagdollPart.RightForearm,
    };

    private void AddVelocity(RagdollPart part, Vector3 velocity)
    {
        if (parts.TryGetValue(part, out Rigidbody body) && body != null) body.AddForce(velocity, ForceMode.VelocityChange);
    }

    private void AddSpin(RagdollPart part, Vector3 spin)
    {
        if (parts.TryGetValue(part, out Rigidbody body) && body != null) body.AddTorque(spin, ForceMode.VelocityChange);
    }

    private Vector3 CenterOfMass()
    {
        Vector3 sum = Vector3.zero;
        float mass = 0f;
        foreach (Rigidbody body in bodies)
        {
            if (body == null) continue;
            sum += body.worldCenterOfMass * body.mass;
            mass += body.mass;
        }
        return mass > 0f ? sum / mass : transform.position;
    }

    // ------------------------------------------------------------------
    // Voler en pièces (2026-11-03)
    // ------------------------------------------------------------------

    /// <summary>La force du coup qui tue : léger (la dernière balle d'un fusil d'assaut, d'un pistolet), lourd (un tir de
    /// précision, une tête au revolver : HeavyBurstImpulse et plus), brutal (le pompe, le gant, qui projettent l'épave :
    /// BrutalBurstImpulse et plus).</summary>
    public enum BurstSeverity { Light, Heavy, Brutal }

    /// <summary>Les pièces du ragdoll.</summary>
    public enum RagdollPart { Pelvis, Torso, Head, LeftArm, LeftForearm, RightArm, RightForearm, LeftThigh, LeftCalf, RightThigh, RightCalf }

    /// <summary>L'impulsion (N·s) d'un coup lourd : un coup de 86 dégâts et plus (WeaponController.DeathImpulse).</summary>
    public const float HeavyBurstImpulse = 30f;
    /// <summary>L'impulsion d'un coup brutal : ceux qui projettent l'épave (le pompe, le gant : KnockbackImpulse).</summary>
    public const float BrutalBurstImpulse = 100f;

    /// <summary>La force d'un coup d'impulsion `impulse` (N·s). Fonction pure, testée.</summary>
    public static BurstSeverity SeverityOf(float impulse)
        => !(impulse >= HeavyBurstImpulse) ? BurstSeverity.Light : impulse >= BrutalBurstImpulse ? BurstSeverity.Brutal : BurstSeverity.Heavy;

    /// <summary>
    /// Les pièces qu'un coup arrache (leur articulation rompue : elles volent avec ce qui y tient), selon la pièce touchée et
    /// le côté du robot touché. Léger : la tête, ou le membre touché ; au buste, le bras de ce côté ; au bassin, la cuisse.
    /// Lourd : la tête ; un membre en deux morceaux ; au buste, le robot coupé en deux à la taille, et la tête ; au bassin, le
    /// buste et la cuisse de ce côté. Brutal : la tête, les deux bras, les deux jambes et le buste (et l'avant-bras ou le
    /// mollet touché). Fonction pure, testée.
    /// </summary>
    public static List<RagdollPart> PartsToDetach(RagdollPart hit, bool hitOnRight, BurstSeverity severity)
    {
        var result = new List<RagdollPart>();
        void Add(RagdollPart part)
        {
            if (!result.Contains(part)) result.Add(part);
        }
        RagdollPart arm = hitOnRight ? RagdollPart.RightArm : RagdollPart.LeftArm;
        RagdollPart thigh = hitOnRight ? RagdollPart.RightThigh : RagdollPart.LeftThigh;
        switch (severity)
        {
            case BurstSeverity.Brutal:
                Add(RagdollPart.Head);
                Add(RagdollPart.LeftArm);
                Add(RagdollPart.RightArm);
                Add(RagdollPart.LeftThigh);
                Add(RagdollPart.RightThigh);
                Add(RagdollPart.Torso);
                if (UpperOf(hit) != hit) Add(hit);
                break;
            case BurstSeverity.Heavy:
                if (hit == RagdollPart.Head) Add(RagdollPart.Head);
                else if (hit == RagdollPart.Torso)
                {
                    Add(RagdollPart.Torso);
                    Add(RagdollPart.Head);
                }
                else if (hit == RagdollPart.Pelvis)
                {
                    Add(RagdollPart.Torso);
                    Add(thigh);
                }
                else
                {
                    Add(UpperOf(hit));
                    Add(LowerOf(hit));
                }
                break;
            default:
                if (hit == RagdollPart.Torso) Add(arm);
                else if (hit == RagdollPart.Pelvis) Add(thigh);
                else Add(hit);
                break;
        }
        return result;
    }

    // Le haut d'un membre (l'avant-bras donne le bras, le mollet la cuisse), et son bas.
    private static RagdollPart UpperOf(RagdollPart part) => part switch
    {
        RagdollPart.LeftForearm => RagdollPart.LeftArm,
        RagdollPart.RightForearm => RagdollPart.RightArm,
        RagdollPart.LeftCalf => RagdollPart.LeftThigh,
        RagdollPart.RightCalf => RagdollPart.RightThigh,
        _ => part,
    };

    private static RagdollPart LowerOf(RagdollPart part) => part switch
    {
        RagdollPart.LeftArm => RagdollPart.LeftForearm,
        RagdollPart.RightArm => RagdollPart.RightForearm,
        RagdollPart.LeftThigh => RagdollPart.LeftCalf,
        RagdollPart.RightThigh => RagdollPart.RightCalf,
        _ => part,
    };

    /// <summary>Les os des pièces arrachées.</summary>
    public IReadOnlyList<Transform> Detached => detached;

    /// <summary>L'épave a-t-elle volé en pièces (Burst), et comment. Vrai une fois ses gerbes toutes nées : la kill cam
    /// les retient à cet instant pour les ralentir.</summary>
    public bool HasBurst => burst && tornPieces.Count == 0;
    public BurstSeverity Severity { get; private set; }

    /// <summary>
    /// Le coup qui a tué (`impulse`, N·s, reçu en `point`) fait voler l'épave en pièces : celles que dit PartsToDetach (la
    /// pièce touchée est la plus proche du point, hors des bras) se détachent et partent loin de l'impact, en tournant ; une gerbe d'huile,
    /// d'étincelles et d'éclats de blindage (gris et de la couleur `paint`) part de l'impact ; l'huile gicle de chaque
    /// articulation rompue et traîne derrière chaque pièce. Une fois par épave. Catégorie C : chaque machine choisit les
    /// mêmes pièces (le même point, la même impulsion), mais elles ne tombent pas pareil partout.
    /// </summary>
    public void Burst(Vector3 point, Vector3 impulse, Color paint)
    {
        if (burst || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
        if (!float.IsFinite(impulse.x) || !float.IsFinite(impulse.y) || !float.IsFinite(impulse.z)) return;
        burst = true;
        // Sous la kill cam, l'épave naît en plein ralenti : ses gerbes aussi.
        burstSpeed = KillCam.RateOf(liveRoot != null ? liveRoot.GetComponentInParent<PlayerLocomotion>() : null);
        float magnitude = impulse.magnitude;
        BurstSeverity severity = SeverityOf(magnitude);
        Vector3 direction = magnitude > 1e-4f ? impulse / magnitude : -transform.forward;
        RagdollPart hit = NearestPart(point);
        Vector3 center = pelvis != null ? pelvis.worldCenterOfMass : transform.position;
        bool hitOnRight = Vector3.Dot(point - center, transform.right) > 0f;
        int layer = gameObject.layer;
        Severity = severity;
        // Un châssis à peau continue (le prototype, le Y Bot) s'étirerait entre les pièces arrachées : il ne perd rien,
        // seule la gerbe part.
        if (RigidPieces())
        {
            foreach (RagdollPart part in PartsToDetach(hit, hitOnRight, severity)) Detach(part, point, direction, severity);
        }
        Track(RobotBurst.Impact(transform, point, direction, severity, paint, layer));
        enabled = tornPieces.Count > 0;
    }

    // Les gerbes des pièces arrachées se montent une pièce par image, puis tout se tait : un coup brutal en ferait 27 à 31
    // systèmes de particules dans l'image de l'impact, déjà la plus chargée. La pièce, elle, part tout de suite (Detach) ; au
    // ralenti de la kill cam, ces quelques images ne valent que quelques millisecondes de jeu.
    private void LateUpdate()
    {
        if (tornPieces.Count == 0)
        {
            enabled = false;
            return;
        }
        TornPiece torn = tornPieces.Dequeue();
        int layer = gameObject.layer;
        Track(RobotBurst.Stump(torn.stumpBody, torn.stumpBody.TransformPoint(torn.stump), torn.stumpBody.TransformDirection(torn.outward), Severity, layer));
        Track(RobotBurst.Trail(torn.piece, torn.piece.position, layer));
    }

    // Retient les systèmes et l'éclair d'une gerbe. Née en plein ralenti, elle prend sa vitesse.
    private void Track(GameObject effect)
    {
        effect.GetComponentsInChildren(true, FoundSystems);
        foreach (ParticleSystem system in FoundSystems)
        {
            var main = system.main;
            main.simulationSpeed = burstSpeed;
            burstSystems.Add(system);
        }
        FoundSystems.Clear();
        BurstFlash flash = effect.GetComponentInChildren<BurstFlash>(true);
        if (flash == null) return;
        flash.speed = burstSpeed;
        burstFlashes.Add(flash);
    }

    /// <summary>Le ralenti de la kill cam : chaque gerbe de l'épave avance à `speed` fois le temps (et l'éclair aussi).</summary>
    public void SetBurstSpeed(float speed)
    {
        speed = Mathf.Max(0f, speed);
        if (speed == burstSpeed) return;
        burstSpeed = speed;
        // Une gerbe se détruit d'elle-même au bout de ses dix secondes.
        for (int i = burstSystems.Count - 1; i >= 0; i--)
        {
            ParticleSystem system = burstSystems[i];
            if (system == null)
            {
                burstSystems.RemoveAt(i);
                continue;
            }
            var main = system.main;
            main.simulationSpeed = speed;
        }
        for (int i = burstFlashes.Count - 1; i >= 0; i--)
        {
            if (burstFlashes[i] != null) burstFlashes[i].speed = speed;
            else burstFlashes.RemoveAt(i);
        }
    }

    // La pièce touchée : la plus proche du point, hors des bras. La surface touchable n'a pas de bras (ils comptent pour
    // le buste) : tenant l'arme devant lui, un robot touché au buste perdait le bras qui la tenait (vu à deux instances).
    private RagdollPart NearestPart(Vector3 point)
    {
        RagdollPart nearest = RagdollPart.Torso;
        float best = float.MaxValue;
        foreach (KeyValuePair<RagdollPart, Rigidbody> pair in parts)
        {
            if (pair.Value == null || IsArm(pair.Key)) continue;
            float d = (pair.Value.worldCenterOfMass - point).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            nearest = pair.Key;
        }
        return nearest;
    }

    /// <summary>Une pièce d'un bras (bras ou avant-bras).</summary>
    public static bool IsArm(RagdollPart part)
        => part == RagdollPart.LeftArm || part == RagdollPart.LeftForearm || part == RagdollPart.RightArm || part == RagdollPart.RightForearm;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : la pièce que touche un coup reçu en `point`.</summary>
    public RagdollPart TestNearestPart(Vector3 point) => NearestPart(point);

    /// <summary>Surface de test : le plan que donnerait ce coup, sans le porter.</summary>
    public CorpseFall.Plan TestPlan(Vector3 point, Vector3 impulse, float closeness, int seed) => PlanFor(point, impulse, closeness, seed);
#endif

    // Le robot montré est-il fait de pièces rigides, chacune sur un seul os (le CT-01, l'OS-02) ?
    private bool RigidPieces()
    {
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.enabled && !RobotChassis.HasRigidPieces(RobotChassis.OwnerOf(skin.name))) return false;
        }
        return true;
    }

    private void Detach(RagdollPart part, Vector3 point, Vector3 direction, BurstSeverity severity)
    {
        if (!parts.TryGetValue(part, out Rigidbody body) || body == null) return;
        var joint = body.GetComponent<CharacterJoint>();
        if (joint == null) return;
        Rigidbody parent = joint.connectedBody;
        Vector3 stump = body.position;
        if (Application.isPlaying) Destroy(joint);
        else DestroyImmediate(joint);
        detached.Add(body.transform);

        // Loin de l'impact, dans le sens du coup, un peu soulevée, en tournant.
        Vector3 away = body.worldCenterOfMass - point;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : direction;
        float speed = severity == BurstSeverity.Light ? 3f : severity == BurstSeverity.Heavy ? 4.5f : 6f;
        body.linearVelocity += (away + direction * 0.5f).normalized * speed + Vector3.up * 1.5f;
        body.angularVelocity += Random.insideUnitSphere * 10f;

        Vector3 outward = parent != null ? stump - parent.worldCenterOfMass : away;
        Transform stumpBody = parent != null ? parent.transform : transform;
        tornPieces.Enqueue(new TornPiece
        {
            stumpBody = stumpBody,
            stump = stumpBody.InverseTransformPoint(stump),
            outward = stumpBody.InverseTransformDirection(outward),
            piece = body.transform,
        });
    }

    // Le bruit des pièces qui heurtent le sol (2026-10-13) : à partir d'une vitesse de choc, pas plus
    // d'un toutes les ImpactSoundGap secondes, et un nombre borné par épave (elle s'immobilise).
    private const float ImpactSoundMinSpeed = 1.2f; // m/s
    private const float ImpactSoundFullSpeed = 6f;  // m/s
    private const float ImpactSoundGap = 0.07f;
    private const int MaxImpactSounds = 14;
    private SoundSet impactSounds;
    private float nextImpactSoundAt;
    private int impactSoundCount;

    /// <summary>L'épave fait du bruit en tombant : chaque pièce qui heurte le décor joue un choc de
    /// métal, plus fort selon la vitesse. Catégorie C, comme l'épave.</summary>
    public void EnableImpactSounds(SoundSet sounds)
    {
        if (sounds == null || sounds.IsEmpty || impactSounds != null) return;
        impactSounds = sounds;
        foreach (Rigidbody body in bodies)
        {
            if (body != null) body.gameObject.AddComponent<CorpsePieceSound>().Corpse = this;
        }
    }

    /// <summary>Le volume (0 à 1) d'un choc de `speed` m/s, 0 sous le seuil. Fonction pure, testée.</summary>
    public static float ImpactSoundLevel(float speed)
    {
        if (!(speed >= ImpactSoundMinSpeed)) return 0f; // NaN compris
        return Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(ImpactSoundMinSpeed, ImpactSoundFullSpeed, speed));
    }

    internal void OnPieceCollision(Collision collision)
    {
        if (impactSounds == null || impactSoundCount >= MaxImpactSounds || Time.time < nextImpactSoundAt) return;
        float level = ImpactSoundLevel(collision.relativeVelocity.magnitude);
        if (level <= 0f || collision.contactCount == 0) return;
        nextImpactSoundAt = Time.time + ImpactSoundGap;
        impactSoundCount++;
        impactSounds.PlayAt(collision.GetContact(0).point, 2f, 40f, level);
    }

    /// <summary>
    /// Pousse l'épave de `impulse` (N·s) : la pièce touchée en prend une part au point d'impact
    /// (`HitBodyShare`, sans dépasser `HitBodyMaxDeltaV` de vitesse), le reste part dans tout le corps,
    /// pièce par pièce selon sa masse, sans la faire tourner. On voit d'où vient le coup, et l'épave
    /// recule d'un bloc au lieu de fouetter.
    /// </summary>
    private void Push(Rigidbody hitBody, Vector3 point, Vector3 impulse)
    {
        if (!float.IsFinite(impulse.x) || !float.IsFinite(impulse.y) || !float.IsFinite(impulse.z)) return;
        Vector3 local = Vector3.zero;
        if (hitBody != null)
        {
            local = Vector3.ClampMagnitude(impulse * HitBodyShare, hitBody.mass * HitBodyMaxDeltaV);
            hitBody.AddForceAtPosition(local, point, ForceMode.Impulse);
        }
        if (totalMass <= 0f) return;
        Vector3 deltaV = (impulse - local) / totalMass;
        foreach (Rigidbody body in bodies)
        {
            if (body != null) body.AddForce(deltaV, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// Un tir qui passe d'`origin` à `end` touche-t-il une épave ? Si oui, elle encaisse
    /// `impulse` (N·s) et l'impact est rendu (point, normale). Cosmétique : on tire sur une épave
    /// pour narguer son adversaire, le serveur n'en sait rien et les balles du jeu la traversent.
    /// </summary>
    public static bool TryHit(Vector3 origin, Vector3 end, float impulse, out Vector3 point, out Vector3 normal)
    {
        point = end;
        normal = Vector3.zero;
        int mask = CorpseLayer.Mask;
        Vector3 delta = end - origin;
        float distance = delta.magnitude;
        if (mask == 0 || distance < 0.001f) return false;

        Vector3 direction = delta / distance;
        int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, distance + 0.05f, mask, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            // Seules les pièces d'épave ont un Rigidbody : la capsule du joueur mort, sur le même
            // layer, n'en a pas.
            if (HitBuffer[i].rigidbody == null) continue;
            if (best < 0 || HitBuffer[i].distance < HitBuffer[best].distance) best = i;
        }
        if (best < 0) return false;

        RaycastHit hit = HitBuffer[best];
        RobotCorpse corpse = hit.rigidbody.GetComponentInParent<RobotCorpse>();
        if (corpse != null) corpse.Push(hit.rigidbody, hit.point, direction * impulse);
        point = hit.point;
        normal = hit.normal;
        return true;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    /// <summary>Ne garde de la copie que son apparence : plus aucun script (arme, viseur, étincelles
    /// du taser), plus d'Animator (il réécrirait les os), plus de lumière, de son, de caméra ni de collider. Sert à l'épave.</summary>
    public static void StripToAppearance(GameObject copy)
    {
        // Les scripts d'abord : ce sont eux qui dépendent des autres composants.
        MonoBehaviour[] scripts = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
        foreach (Animator a in copy.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);
        foreach (Light l in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
        foreach (AudioSource s in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(s);
        foreach (Camera c in copy.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c);
        foreach (ParticleSystem p in copy.GetComponentsInChildren<ParticleSystem>(true))
        {
            p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
    }

    private void Build(Animator live, Transform copyRoot, Vector3 velocity)
    {
        Transform Bone(HumanBodyBones b) => Twin(liveRoot, copyRoot, live.GetBoneTransform(b));

        Transform hips = Bone(HumanBodyBones.Hips);
        Transform spine = Bone(HumanBodyBones.Spine);
        Transform neck = Bone(HumanBodyBones.Neck);
        Transform head = Bone(HumanBodyBones.Head);
        if (hips == null || spine == null || head == null)
        {
            // Modèle incomplet : un seul bloc, plutôt qu'un robot figé debout.
            pelvis = AddBody(copyRoot, RobotMass, RagdollPart.Pelvis);
            AddBox(copyRoot, copyRoot.position + Vector3.up * 0.9f, new Vector3(0.45f, 1.8f, 0.3f), copyRoot);
            FinishBodies(velocity);
            return;
        }

        Vector3 right = copyRoot.right;
        Vector3 up = copyRoot.up;
        Vector3 forward = copyRoot.forward;
        Transform leftThigh = Bone(HumanBodyBones.LeftUpperLeg);
        Transform rightThigh = Bone(HumanBodyBones.RightUpperLeg);
        Transform leftArm = Bone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = Bone(HumanBodyBones.RightUpperArm);
        Vector3 neckPoint = neck != null ? neck.position : head.position;

        // Bassin et buste : des blocs, comme l'assistant Ragdoll.
        pelvis = AddBody(hips, PelvisMass, RagdollPart.Pelvis);
        float hipWidth = leftThigh != null && rightThigh != null ? Vector3.Distance(leftThigh.position, rightThigh.position) + 0.16f : 0.34f;
        float pelvisHeight = Mathf.Max(0.18f, Vector3.Distance(hips.position, spine.position) * 1.6f);
        AddBox(hips, hips.position + up * (pelvisHeight * 0.25f), new Vector3(hipWidth, pelvisHeight, 0.24f), copyRoot);

        Rigidbody torso = AddBody(spine, TorsoMass, RagdollPart.Torso);
        float shoulderWidth = leftArm != null && rightArm != null ? Vector3.Distance(leftArm.position, rightArm.position) * 0.9f : 0.36f;
        AddBox(spine, (spine.position + neckPoint) * 0.5f,
               new Vector3(shoulderWidth, Vector3.Distance(spine.position, neckPoint), 0.26f), copyRoot);
        AddJoint(torso, pelvis, right, forward, -20f, 20f, 10f);

        Rigidbody headBody = AddBody(head, HeadMass, RagdollPart.Head);
        AddSphere(head, head.position + up * 0.09f, 0.115f);
        AddJoint(headBody, torso, right, forward, -40f, 25f, 25f);

        // Bras : épaule vers le buste, coude en charnière.
        Vector3 spineDown = (hips.position - neckPoint).normalized;
        AddArm(Bone(HumanBodyBones.LeftUpperArm), Bone(HumanBodyBones.LeftLowerArm), Bone(HumanBodyBones.LeftHand), torso, spineDown, right, false);
        AddArm(Bone(HumanBodyBones.RightUpperArm), Bone(HumanBodyBones.RightLowerArm), Bone(HumanBodyBones.RightHand), torso, spineDown, right, true);

        // Jambes : hanche vers le bassin, genou en charnière, pied rattaché à la jambe.
        AddLeg(leftThigh, Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes), spineDown, right, forward, copyRoot, false);
        AddLeg(rightThigh, Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes), spineDown, right, forward, copyRoot, true);

        FinishBodies(velocity);
    }

    // Les limites d'une articulation se comptent depuis la pose de la mort, or un robot meurt en visant, accroupi, en courant :
    // celles de l'assistant Ragdoll, faites pour un robot en T, retenaient les bras en l'air là où ils visaient, et le robot
    // finissait sur le dos les bras en croix (2026-11-04). Elles se comptent donc depuis l'articulation telle qu'elle est.
    private const float ElbowMaxBend = 145f, KneeMaxBend = 140f;
    private const float ShoulderBehind = 20f, ShoulderRaise = 50f, ShoulderSwing = 40f;
    private const float HipFlexion = 115f, HipExtension = 20f;

    private void AddArm(Transform upper, Transform lower, Transform hand, Rigidbody torso, Vector3 spineDown, Vector3 right, bool rightSide)
    {
        if (upper == null || lower == null) return;
        Rigidbody arm = AddBody(upper, ArmMass, rightSide ? RagdollPart.RightArm : RagdollPart.LeftArm);
        AddCapsule(upper, lower.position, 0.055f);
        // L'épaule : le bras redescend jusqu'à longer le corps, sans passer loin derrière, ni monter beaucoup plus.
        Vector3 armDirection = (lower.position - upper.position).normalized;
        Vector3 down = Vector3.Cross(armDirection, spineDown);
        down = down.sqrMagnitude > 0.01f ? down.normalized : right;
        AddFreeJoint(arm, torso, down, armDirection, -Mathf.Min(170f, Vector3.Angle(armDirection, spineDown) + ShoulderBehind), ShoulderRaise, ShoulderSwing, ShoulderSwing);

        Rigidbody forearm = AddBody(lower, ForearmMass, rightSide ? RagdollPart.RightForearm : RagdollPart.LeftForearm);
        Vector3 end = hand != null ? hand.position + (hand.position - lower.position).normalized * 0.08f : lower.position + (lower.position - upper.position);
        AddCapsule(lower, end, 0.045f);
        AddHinge(forearm, arm, upper.position, lower.position, end, -right, ElbowMaxBend);
    }

    private void AddLeg(Transform thigh, Transform calf, Transform foot, Transform toes, Vector3 spineDown, Vector3 right, Vector3 forward, Transform characterRoot,
                        bool rightSide)
    {
        if (thigh == null || calf == null) return;
        Rigidbody thighBody = AddBody(thigh, ThighMass, rightSide ? RagdollPart.RightThigh : RagdollPart.LeftThigh);
        AddCapsule(thigh, calf.position, 0.085f);
        // La hanche : la cuisse remonte vers le ventre, un peu vers l'arrière.
        float flexion = -Vector3.SignedAngle(spineDown, (calf.position - thigh.position).normalized, right);
        AddFreeJoint(thighBody, pelvis, right, forward, -Mathf.Max(0f, HipExtension + flexion), Mathf.Max(0f, HipFlexion - flexion), 30f, 0f);

        Rigidbody calfBody = AddBody(calf, CalfMass, rightSide ? RagdollPart.RightCalf : RagdollPart.LeftCalf);
        Vector3 ankle = foot != null ? foot.position : calf.position + (calf.position - thigh.position);
        AddCapsule(calf, ankle, 0.07f);
        AddHinge(calfBody, thighBody, thigh.position, calf.position, ankle, right, KneeMaxBend);

        // Le pied suit la jambe : son bloc fait partie du même corps (aucun Rigidbody sur le pied).
        if (foot != null)
        {
            Vector3 tip = toes != null ? toes.position + (toes.position - foot.position) * 0.6f : foot.position + forward * 0.2f;
            Vector3 center = (foot.position + tip) * 0.5f;
            float length = Vector3.Distance(foot.position, tip) + 0.06f;
            AddBox(foot, center, new Vector3(0.11f, 0.09f, length), characterRoot);
        }
    }

    private Rigidbody AddBody(Transform bone, float mass, RagdollPart part)
    {
        var body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        bodies.Add(body);
        parts[part] = body;
        totalMass += mass;
        return body;
    }

    private void FinishBodies(Vector3 velocity)
    {
        foreach (Rigidbody body in bodies)
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            // Un robot tué allongé a des pièces déjà dans le sol : sans cette borne, la physique les en
            // chassait d'un coup, et l'épave sautait en l'air.
            body.maxDepenetrationVelocity = 2f;
            body.angularDamping = 0.3f;
            body.linearVelocity = velocity;
        }
    }

    /// <summary>Articulation à la manière de l'assistant Ragdoll : la torsion autour de
    /// `worldTwistAxis`, le balancement autour de `worldSwingAxis`, ramenés à l'axe local le plus
    /// proche. Limites en degrés, relatives à la pose de la mort.</summary>
    private static void AddJoint(Rigidbody body, Rigidbody parent, Vector3 worldTwistAxis, Vector3 worldSwingAxis,
                                 float lowTwist, float highTwist, float swing)
    {
        if (body == null || parent == null) return;
        var joint = body.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent;
        joint.axis = NearestAxis(body.transform.InverseTransformDirection(worldTwistAxis));
        joint.swingAxis = NearestAxis(body.transform.InverseTransformDirection(worldSwingAxis));
        joint.anchor = Vector3.zero;
        // Comme l'assistant : sans prétraitement, une épave née dans le décor ne part pas en vrille.
        joint.enablePreprocessing = false;
        joint.enableProjection = true;
        joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
        joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
        joint.swing1Limit = new SoftJointLimit { limit = swing };
        joint.swing2Limit = new SoftJointLimit { limit = 0f };
    }

    /// <summary>Une articulation d'axes exacts : la rotation autour de `worldTwistAxis`, de `lowTwist` à `highTwist` (un angle
    /// négatif tourne dans le sens direct autour de l'axe : c'est le sens de ceux de l'assistant Ragdoll).</summary>
    private static void AddFreeJoint(Rigidbody body, Rigidbody parent, Vector3 worldTwistAxis, Vector3 worldSwingAxis,
                                     float lowTwist, float highTwist, float swing1, float swing2)
    {
        if (body == null || parent == null) return;
        var joint = body.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent;
        Vector3 axis = worldTwistAxis.normalized;
        Vector3 swing = Vector3.ProjectOnPlane(worldSwingAxis, axis);
        swing = swing.sqrMagnitude > 1e-4f ? swing.normalized : Vector3.Cross(axis, Vector3.up).normalized;
        joint.axis = body.transform.InverseTransformDirection(axis);
        joint.swingAxis = body.transform.InverseTransformDirection(swing);
        joint.anchor = Vector3.zero;
        joint.enablePreprocessing = false;
        joint.enableProjection = true;
        joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
        joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
        joint.swing1Limit = new SoftJointLimit { limit = swing1 };
        joint.swing2Limit = new SoftJointLimit { limit = swing2 };
    }

    // Un coude, un genou : une charnière dans le plan du membre (de `from` à `joint` à `to`), de tendu à plié de `maxBend` ; un
    // membre tendu n'a pas de plan, il plie autour de `straightAxis`.
    private static void AddHinge(Rigidbody body, Rigidbody parent, Vector3 from, Vector3 joint, Vector3 to, Vector3 straightAxis, float maxBend)
    {
        Vector3 upper = (joint - from).normalized, lower = (to - joint).normalized;
        Vector3 axis = Vector3.Cross(upper, lower);
        float bent = Vector3.Angle(upper, lower);
        AddFreeJoint(body, parent, axis.sqrMagnitude > 0.01f ? axis : straightAxis, lower, -Mathf.Max(0f, maxBend - bent), Mathf.Min(170f, bent), 0f, 0f);
    }

    private static void AddCapsule(Transform bone, Vector3 endWorld, float radiusWorld)
    {
        Vector3 end = bone.InverseTransformPoint(endWorld);
        int axis = LargestAxis(end);
        float scale = UniformScale(bone);
        var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = axis;
        capsule.center = end * 0.5f;
        capsule.radius = radiusWorld / scale;
        capsule.height = Mathf.Abs(end[axis]) + capsule.radius;
    }

    private static void AddSphere(Transform bone, Vector3 centerWorld, float radiusWorld)
    {
        var sphere = bone.gameObject.AddComponent<SphereCollider>();
        sphere.center = bone.InverseTransformPoint(centerWorld);
        sphere.radius = radiusWorld / UniformScale(bone);
    }

    /// <summary>Un bloc de `sizeWorld` (largeur selon la droite du personnage, hauteur selon son
    /// haut, profondeur selon son avant), centré en `centerWorld`, dans le repère de l'os : chaque
    /// axe de l'os prend la dimension de l'axe du personnage dont il est le plus proche.</summary>
    private static void AddBox(Transform bone, Vector3 centerWorld, Vector3 sizeWorld, Transform characterRoot)
    {
        var box = bone.gameObject.AddComponent<BoxCollider>();
        box.center = bone.InverseTransformPoint(centerWorld);
        float scale = UniformScale(bone);
        Vector3 size = default;
        Vector3[] boneAxes = { bone.right, bone.up, bone.forward };
        Vector3[] characterAxes = { characterRoot.right, characterRoot.up, characterRoot.forward };
        for (int i = 0; i < 3; i++)
        {
            int best = 0;
            float bestDot = -1f;
            for (int j = 0; j < 3; j++)
            {
                float dot = Mathf.Abs(Vector3.Dot(boneAxes[i], characterAxes[j]));
                if (dot > bestDot) { bestDot = dot; best = j; }
            }
            size[i] = sizeWorld[best] / scale;
        }
        box.size = size;
    }

    private Rigidbody NearestBody(Vector3 point)
    {
        Rigidbody nearest = null;
        float best = float.MaxValue;
        foreach (Rigidbody body in bodies)
        {
            if (body == null) continue;
            float d = (body.worldCenterOfMass - point).sqrMagnitude;
            if (d < best) { best = d; nearest = body; }
        }
        return nearest;
    }

    private static Vector3 NearestAxis(Vector3 v)
    {
        int axis = LargestAxis(v);
        Vector3 result = Vector3.zero;
        result[axis] = Mathf.Sign(v[axis]);
        return result;
    }

    private static int LargestAxis(Vector3 v)
    {
        float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
        return x >= y && x >= z ? 0 : (y >= z ? 1 : 2);
    }

    private static float UniformScale(Transform t) => Mathf.Max(1e-4f, Mathf.Abs(t.lossyScale.x));

    /// <summary>L'équivalent de `liveTransform` (descendant de `liveRoot`) sous `copyRoot`, copie
    /// exacte de `liveRoot`.</summary>
    private static Transform Twin(Transform liveRoot, Transform copyRoot, Transform liveTransform)
    {
        if (liveTransform == null) return null;
        if (liveTransform == liveRoot) return copyRoot;
        string path = liveTransform.name;
        for (Transform p = liveTransform.parent; p != null && p != liveRoot; p = p.parent) path = p.name + "/" + path;
        return copyRoot.Find(path);
    }
}
