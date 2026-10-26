using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// L'épave d'un robot tué : une copie de son modèle, figée dans la pose où il est mort, qui tombe
/// en ragdoll (2026-10-12, GDD § 6 : « le châssis vole en pièces, ragdoll, pas de gore »).
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
/// Les corps et les limites des articulations reprennent ceux de l'assistant Ragdoll d'Unity (bassin,
/// buste, tête, bras, avant-bras, cuisses, jambes), relatives à la pose de la mort.
/// </summary>
public class RobotCorpse : MonoBehaviour
{
    private readonly List<Rigidbody> bodies = new List<Rigidbody>();
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

    /// <summary>Le coup qui a tué : une impulsion (N·s) reçue en `point`, sur la pièce la plus proche.</summary>
    public void ApplyImpulse(Vector3 point, Vector3 impulse) => Push(NearestBody(point), point, impulse);

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
    /// du taser), plus d'Animator (il réécrirait les os), plus de lumière, de son ni de collider.</summary>
    private static void StripToAppearance(GameObject copy)
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
            pelvis = AddBody(copyRoot, RobotMass);
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
        pelvis = AddBody(hips, PelvisMass);
        float hipWidth = leftThigh != null && rightThigh != null ? Vector3.Distance(leftThigh.position, rightThigh.position) + 0.16f : 0.34f;
        float pelvisHeight = Mathf.Max(0.18f, Vector3.Distance(hips.position, spine.position) * 1.6f);
        AddBox(hips, hips.position + up * (pelvisHeight * 0.25f), new Vector3(hipWidth, pelvisHeight, 0.24f), copyRoot);

        Rigidbody torso = AddBody(spine, TorsoMass);
        float shoulderWidth = leftArm != null && rightArm != null ? Vector3.Distance(leftArm.position, rightArm.position) * 0.9f : 0.36f;
        AddBox(spine, (spine.position + neckPoint) * 0.5f,
               new Vector3(shoulderWidth, Vector3.Distance(spine.position, neckPoint), 0.26f), copyRoot);
        AddJoint(torso, pelvis, right, forward, -20f, 20f, 10f);

        Rigidbody headBody = AddBody(head, HeadMass);
        AddSphere(head, head.position + up * 0.09f, 0.115f);
        AddJoint(headBody, torso, right, forward, -40f, 25f, 25f);

        // Bras : épaule vers le buste, coude en charnière. Côté droit en miroir.
        AddArm(Bone(HumanBodyBones.LeftUpperArm), Bone(HumanBodyBones.LeftLowerArm), Bone(HumanBodyBones.LeftHand), torso, up, forward);
        AddArm(Bone(HumanBodyBones.RightUpperArm), Bone(HumanBodyBones.RightLowerArm), Bone(HumanBodyBones.RightHand), torso, -up, forward);

        // Jambes : hanche vers le bassin, genou en charnière, pied rattaché à la jambe.
        AddLeg(leftThigh, Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes), right, forward, copyRoot);
        AddLeg(rightThigh, Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes), right, forward, copyRoot);

        FinishBodies(velocity);
    }

    private void AddArm(Transform upper, Transform lower, Transform hand, Rigidbody torso, Vector3 twistAxis, Vector3 forward)
    {
        if (upper == null || lower == null) return;
        Rigidbody arm = AddBody(upper, ArmMass);
        AddCapsule(upper, lower.position, 0.055f);
        AddJoint(arm, torso, twistAxis, forward, -70f, 10f, 50f);

        Rigidbody forearm = AddBody(lower, ForearmMass);
        Vector3 end = hand != null ? hand.position + (hand.position - lower.position).normalized * 0.08f : lower.position + (lower.position - upper.position);
        AddCapsule(lower, end, 0.045f);
        AddJoint(forearm, arm, forward, twistAxis, -90f, 0f, 0f);
    }

    private void AddLeg(Transform thigh, Transform calf, Transform foot, Transform toes, Vector3 right, Vector3 forward, Transform characterRoot)
    {
        if (thigh == null || calf == null) return;
        Rigidbody thighBody = AddBody(thigh, ThighMass);
        AddCapsule(thigh, calf.position, 0.085f);
        AddJoint(thighBody, pelvis, right, forward, -20f, 70f, 30f);

        Rigidbody calfBody = AddBody(calf, CalfMass);
        AddCapsule(calf, foot != null ? foot.position : calf.position + (calf.position - thigh.position), 0.07f);
        AddJoint(calfBody, thighBody, right, forward, -80f, 0f, 0f);

        // Le pied suit la jambe : son bloc fait partie du même corps (aucun Rigidbody sur le pied).
        if (foot != null)
        {
            Vector3 tip = toes != null ? toes.position + (toes.position - foot.position) * 0.6f : foot.position + forward * 0.2f;
            Vector3 center = (foot.position + tip) * 0.5f;
            float length = Vector3.Distance(foot.position, tip) + 0.06f;
            AddBox(foot, center, new Vector3(0.11f, 0.09f, length), characterRoot);
        }
    }

    private Rigidbody AddBody(Transform bone, float mass)
    {
        var body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        bodies.Add(body);
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
