using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Le chargeur qui quitte l'arme pendant un rechargement (2026-10-29, le FA-07 d'abord ; ReloadGesture). Catégorie C :
/// rien de la simulation ne le lit, et il ne change rien au réseau.
///
/// Pour une arme qui prend un geste de rechargement à chargeur, ce composant fait jouer ce geste à l'état Recharge de
/// l'Animator (le paramètre GesteRecharge, sur le corps et sur les bras de la vue subjective), puis suit ce que
/// l'Animator en montre, image par image : à l'étape où le chargeur vide tombe, celui de l'arme se cache et une copie
/// tombe (DroppedMagazine) ; à celle où la main gauche prend le neuf, une copie apparaît dans sa main, à la paume du
/// châssis montré (WeaponGrip.Settings.palmPoints) ; elle rejoint sa place sous le puits jusqu'à l'étape où il est en
/// place, et le chargeur de l'arme réapparaît. Puis les pièces d'armement du geste (la poignée d'armement, le levier,
/// la culasse, la glissière et ce qu'elle porte) reculent avec la main gauche et reviennent (ReloadGesture.ArmingOffset).
///
/// Tout se déduit du temps de l'état que joue l'Animator, qui suit lui-même le rechargement (WeaponController.
/// IsReloading, ReloadSpeed) : le chargeur va avec la main, chez le tireur comme chez les autres. Un rechargement
/// interrompu (sprint, taser, changement d'arme, mort) rend aussitôt l'arme intacte. Allongé, le corps joue le geste
/// commun, qui garde le chargeur en place.
///
/// Ce qu'on cache, on le cache par Renderer.enabled, que rien d'autre ne touche sur une arme (AttachmentMount montre
/// et cache ses pièces par SetActive, la mort et l'écran de fin par forceRenderingOff), et on ne rallume que ce qu'on
/// a éteint.
///
/// Une arme qui s'ouvre (2026-11-03, le Big D du Golden Gun : BreakActionGesture) n'a pas de chargeur : son geste se
/// joue de la même façon (sa valeur de GesteRecharge, le temps de l'état Recharge, le cadrage de la vue subjective),
/// et ce qu'il change sur l'arme (le canon qui bascule, la douille vide qui tombe, la cartouche neuve de la main à la
/// chambre, le chien, le levier), une BreakActionView par Animator le montre.
/// </summary>
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public class MagazineSwap : MonoBehaviour
{
    /// <summary>Le paramètre de l'Animator qui choisit le geste de l'état Recharge (0 : le geste commun).</summary>
    public const string GestureParameter = "GesteRecharge";

    /// <summary>L'état du haut du corps qui joue le rechargement.</summary>
    public const string ReloadStateName = "Recharge";

    /// <summary>La couche du haut du corps (HautDuCorps, PlayerAnimator).</summary>
    public const int UpperBodyLayer = 1;

    private static readonly int GestureId = Animator.StringToHash(GestureParameter);
    private static readonly int StanceFId = Animator.StringToHash("StanceF");
    private static readonly int ReloadStateId = Animator.StringToHash(ReloadStateName);

    [Tooltip("Les gestes de rechargement où le chargeur quitte l'arme, chacun avec ses armes (Duel Arena > Animations > Importer les rechargements à chargeur). Une arme qui n'en a pas garde le geste commun (Y Bot@Reloading), son chargeur en place.")]
    [SerializeField] private ReloadGesture[] gestures = new ReloadGesture[0];

    [SerializeField] private Drop drop = new Drop();

    /// <summary>Le chargeur vide qui tombe (ressenti).</summary>
    [System.Serializable]
    public class Drop
    {
        [Tooltip("Vitesse (m/s) à laquelle il sort du puits, vers le bas de l'arme, en plus de celle de l'arme.")]
        public float ejectSpeed = 0.8f;
        [Tooltip("Vitesse de l'arme qu'il emporte au plus (m/s) : l'arme d'un joueur distant avance par à-coups (dette n° 30).")]
        public float maxInheritedSpeed = 2f;
        [Tooltip("Il tourne en tombant (rad/s), autour de l'axe gauche-droite de l'arme.")]
        public float tumble = 4f;
        [Tooltip("Temps (s) qu'il reste au sol avant de disparaître.")]
        public float lifetime = 8f;
        [Tooltip("En vue subjective, temps (s) de sa chute avant qu'il s'efface : il sort de l'écran par le bas.")]
        public float armsLifetime = 0.45f;
        [Tooltip("Chez le joueur, temps (s) avant que celui de son corps se voie : celui de ses bras tombe d'abord hors de l'écran.")]
        public float ownerRevealDelay = 0.35f;
    }

    /// <summary>Ce que montre une copie du chargeur, à une part du rechargement.</summary>
    public enum Phase
    {
        /// <summary>Le chargeur est dans l'arme (avant qu'il tombe, une fois le neuf en place, hors rechargement).</summary>
        InWeapon,
        /// <summary>Le puits est vide : l'ancien est tombé, la main va chercher le neuf.</summary>
        Empty,
        /// <summary>Le neuf est dans la main gauche.</summary>
        InHand,
    }

    /// <summary>Ce que montre le chargeur à la part `f` du rechargement. Fonction pure, testée.</summary>
    public static Phase PhaseAt(float f, ReloadGesture gesture)
    {
        if (gesture == null || float.IsNaN(f) || f < gesture.drop || f >= gesture.seated) return Phase.InWeapon;
        return f < gesture.grab ? Phase.Empty : Phase.InHand;
    }

    /// <summary>De la main à sa place : 0 jusqu'à l'approche, 1 une fois en place. Fonction pure, testée.</summary>
    public static float SeatBlend(float f, ReloadGesture gesture)
    {
        if (gesture == null) return 1f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(gesture.approach, gesture.seated, f));
    }

    /// <summary>
    /// La part du rechargement que montre `animator` du geste `gesture` (le temps de son état Recharge, ou de celui
    /// vers lequel il va), ou NaN s'il ne le montre pas : un autre geste, l'état d'après, allongé (le geste commun),
    /// ou le haut du corps effacé.
    /// </summary>
    public static float ShownProgress(Animator animator, ReloadGesture gesture)
        => animator != null && animator.runtimeAnimatorController != null && HasParameter(animator, GestureParameter)
            ? ProgressOf(animator, gesture) : float.NaN;

    // La même chose sans chercher le paramètre (Animator.parameters alloue un tableau à chaque appel) : la vue le
    // sait une fois pour toutes.
    private static float ProgressOf(Animator animator, ReloadGesture gesture)
    {
        if (animator == null || gesture == null || !animator.isActiveAndEnabled || animator.layerCount <= UpperBodyLayer)
            return float.NaN;
        if (Mathf.Abs(animator.GetFloat(GestureId) - gesture.animatorValue) > 0.5f) return float.NaN;
        if (animator.GetFloat(StanceFId) > 1.5f || animator.GetLayerWeight(UpperBodyLayer) < 0.5f) return float.NaN;
        AnimatorStateInfo state = animator.IsInTransition(UpperBodyLayer)
            ? animator.GetNextAnimatorStateInfo(UpperBodyLayer)
            : animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);
        if (state.shortNameHash != ReloadStateId) return float.NaN;
        return gesture.ProgressAt(state.normalizedTime * gesture.clipLength);
    }

    private static bool HasParameter(Animator animator, string name)
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.name == name) return true;
        return false;
    }

    /// <summary>Le chargeur que porte un modèle d'arme : celui d'un accessoire monté (grand, rapide), sinon le sien.</summary>
    public static Transform MagazineOf(WeaponModel model)
    {
        if (model == null) return null;
        WeaponAttachment mounted = AttachmentMount.Mounted(model.transform, AttachmentCategory.Magazine);
        return mounted != null ? mounted.transform : model.transform.Find(StandardMagazineName);
    }

    /// <summary>La pièce du chargeur d'origine d'un modèle d'arme (exporter_armes).</summary>
    public const string StandardMagazineName = "Chargeur_Standard";

    /// <summary>
    /// Le chargeur que montre un Animator (le corps, ou les bras de la vue subjective), d'une image à l'autre. Sans
    /// composant : MagazineSwap en tient un par Animator, les tests aussi.
    /// </summary>
    public sealed class View
    {
        private readonly Animator animator;
        private readonly bool arms;
        private readonly bool hasParameter;   // le contrôleur connaît-il GesteRecharge ?
        private readonly Transform leftHand;
        private readonly List<Renderer> hidden = new List<Renderer>();
        private WeaponModel model;
        private Transform magazine;
        private GameObject inHand;
        private bool dropped;
        private Vector3 lastWeaponPosition, weaponVelocity;
        private bool tracked;
        // les pièces d'armement qui reculent, leur place au repos (dans le repère du modèle, et la leur)
        private readonly List<Transform> arming = new List<Transform>();
        private readonly List<Vector3> armingRest = new List<Vector3>(), armingLocal = new List<Vector3>();
        private ReloadGesture armingOf;

        /// <summary>`arms` : les bras de la vue subjective (le chargeur vide y tombe sans physique).</summary>
        public View(Animator animator, bool arms)
        {
            this.animator = animator;
            this.arms = arms;
            hasParameter = animator != null && animator.runtimeAnimatorController != null && HasParameter(animator, GestureParameter);
            leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        }

        public Animator Animator => animator;

        /// <summary>Le contrôleur de cet Animator sait-il choisir le geste (GesteRecharge) ?</summary>
        public bool HasGestureParameter => hasParameter;

        /// <summary>Ce que montre le chargeur à cette image.</summary>
        public Phase Shown { get; private set; }

        /// <summary>La part du rechargement que l'Animator montre de ce geste à cette image, ou NaN (pas ce geste,
        /// pas de rechargement) : la vue subjective cadre le puits pendant qu'il se joue (FirstPersonView).</summary>
        public float Progress { get; private set; } = float.NaN;

        /// <summary>La copie du chargeur neuf dans la main gauche, ou null.</summary>
        public GameObject InHand => inHand;

        /// <summary>Le chargeur de l'arme est-il caché ?</summary>
        public bool MagazineHidden => hidden.Count > 0;

        /// <summary>Les pièces d'armement que le geste fait reculer en ce moment (vide au repos).</summary>
        public IReadOnlyList<Transform> ArmingMoved => arming;

        /// <summary>
        /// Une image, après l'Animator et ce qui retouche les mains (la prise, la vue subjective). `shown` : le modèle
        /// d'arme montré (null : aucun) ; `gesture` : le geste de l'arme en main (null : le geste commun) ;
        /// `reloading` : l'arme recharge-t-elle (WeaponController.IsReloading, et le joueur vivant) ; `palmShift` :
        /// le point de la paume de la main montrée moins celui du geste (repère de l'os de la main).
        /// </summary>
        public void Step(WeaponModel shown, ReloadGesture gesture, bool reloading, Vector3 palmShift, Drop settings, float dt)
        {
            Track(shown, dt);
            if (shown != model)
            {
                // Un autre modèle (l'arme changée, les armes reconstruites) : l'ancien est rendu intact.
                Restore();
                dropped = false;
                model = shown;
            }
            float f = reloading && hasParameter && shown != null ? ProgressOf(animator, gesture) : float.NaN;
            Progress = f;
            if (gesture is BreakActionGesture)
            {
                // une arme qui s'ouvre n'a pas de chargeur : BreakActionView montre son geste
                Restore();
                dropped = false;
                Shown = Phase.InWeapon;
                return;
            }
            Phase phase = PhaseAt(f, gesture);
            if (model == null || float.IsNaN(f) || leftHand == null)
            {
                Restore();
                dropped = false;
                Shown = Phase.InWeapon;
                return;
            }
            MoveArming(gesture, f);
            Shown = phase;
            if (phase == Phase.InWeapon)
            {
                // avant qu'il tombe, ou le neuf en place : l'arme a son chargeur ; il ne retombe pas d'ici la fin
                RestoreMagazine();
                if (f >= gesture.seated) dropped = true;
                return;
            }
            if (!dropped)
            {
                dropped = true;
                magazine = MagazineOf(model);
                if (magazine == null) return;
                // vu à partir du moment où la main a déjà pris le neuf (un joueur qui arrive) : rien ne tombe
                if (phase == Phase.Empty && settings != null) Fall(settings);
                Hide();
            }
            if (magazine == null) return;
            if (phase == Phase.InHand) Hold(gesture, palmShift, f);
            else DestroyHeld();
        }

        /// <summary>Rend l'arme intacte : son chargeur montré, rien dans la main, ses pièces d'armement en place.</summary>
        public void Restore()
        {
            RestoreMagazine();
            RestoreArming();
        }

        private void RestoreMagazine()
        {
            foreach (Renderer r in hidden)
                if (r != null) r.enabled = true;
            hidden.Clear();
            DestroyHeld();
        }

        /// <summary>Les pièces d'armement du geste reculent de ce que dit sa course à cette part du rechargement
        /// (ReloadGesture.ArmingOffset), dans l'axe du canon : comme la main qui les tire.</summary>
        private void MoveArming(ReloadGesture gesture, float f)
        {
            float offset = gesture != null ? gesture.ArmingOffset(f) : 0f;
            if (offset <= 0f)
            {
                RestoreArming();
                return;
            }
            Transform root = model.transform;
            if (armingOf != gesture)
            {
                RestoreArming();
                armingOf = gesture;
                foreach (string path in gesture.armingParts)
                {
                    AddArming(root, root.Find(path));
                    // la même pièce dans un accessoire monté, qui en porte sa copie (le canon court du PA-05)
                    string name = path.Substring(path.LastIndexOf('/') + 1);
                    foreach (WeaponAttachment a in root.GetComponentsInChildren<WeaponAttachment>(true))
                        AddArming(root, a.transform.Find(name));
                }
            }
            for (int i = 0; i < arming.Count; i++)
                if (arming[i] != null) arming[i].position = root.TransformPoint(armingRest[i] + Vector3.back * offset);
        }

        private void AddArming(Transform root, Transform part)
        {
            if (part == null || arming.Contains(part)) return;
            arming.Add(part);
            armingRest.Add(root.InverseTransformPoint(part.position));
            armingLocal.Add(part.localPosition);
        }

        private void RestoreArming()
        {
            for (int i = 0; i < arming.Count; i++)
                if (arming[i] != null) arming[i].localPosition = armingLocal[i];
            arming.Clear();
            armingRest.Clear();
            armingLocal.Clear();
            armingOf = null;
        }

        private void Track(WeaponModel shown, float dt)
        {
            if (shown == null || dt <= 0f)
            {
                tracked = false;
                return;
            }
            Vector3 p = shown.transform.position;
            if (tracked) weaponVelocity = Vector3.Lerp(weaponVelocity, (p - lastWeaponPosition) / dt, 0.5f);
            else weaponVelocity = Vector3.zero;
            lastWeaponPosition = p;
            tracked = true;
        }

        private void Hide()
        {
            foreach (Renderer r in magazine.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }

        /// <summary>Le chargeur vide tombe : une copie à sa place, avec la vitesse de l'arme et un peu vers le bas.</summary>
        private void Fall(Drop settings)
        {
            GameObject copy = Copy(magazine, "Chargeur tombé");
            Transform weapon = model.transform;
            Vector3 velocity = Vector3.ClampMagnitude(weaponVelocity, settings.maxInheritedSpeed) + weapon.rotation * Vector3.down * settings.ejectSpeed;
            Vector3 spin = weapon.rotation * Vector3.right * settings.tumble;
            bool shadowOnly = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                shadowOnly |= r.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
            DroppedMagazine.Spawn(copy, velocity, spin, arms ? settings.armsLifetime : settings.lifetime, !arms,
                                  shadowOnly ? settings.ownerRevealDelay : 0f);
        }

        /// <summary>Le neuf dans la main gauche : là où la main du geste le tient, à la paume de la main montrée, puis
        /// de la main à sa place sous le puits entre l'approche et l'insertion.</summary>
        private void Hold(ReloadGesture gesture, Vector3 palmShift, float f)
        {
            if (inHand == null)
            {
                inHand = Copy(magazine, "Chargeur neuf");
                // la main du corps est repliée sous le gant (échelle nulle) : la copie suit l'avant-bras
                Transform holder = leftHand.lossyScale.sqrMagnitude > 1e-4f ? leftHand : leftHand.parent;
                inHand.transform.SetParent(holder, true);
            }
            Transform weapon = model.transform;
            Quaternion hand = leftHand.rotation;
            Vector3 heldPosition = leftHand.position + hand * (gesture.weaponInHandPosition + palmShift);
            Quaternion heldRotation = hand * gesture.weaponInHandRotation;
            float w = SeatBlend(f, gesture);
            Vector3 weaponPosition = Vector3.Lerp(heldPosition, weapon.position, w);
            Quaternion weaponRotation = Quaternion.Slerp(heldRotation, weapon.rotation, w);
            // le chargeur là où il serait dans une arme ainsi placée
            Vector3 local = weapon.InverseTransformPoint(magazine.position);
            Quaternion localRotation = Quaternion.Inverse(weapon.rotation) * magazine.rotation;
            inHand.transform.SetPositionAndRotation(weaponPosition + weaponRotation * local, weaponRotation * localRotation);
        }

        private void DestroyHeld()
        {
            if (inHand == null) return;
            if (Application.isPlaying) Object.Destroy(inHand);
            else Object.DestroyImmediate(inHand);
            inHand = null;
        }

        /// <summary>Une copie des pièces visibles du chargeur (maillages, matériaux, ombres, layer), à sa place, sans
        /// rien d'autre : ni collider, ni script.</summary>
        private static GameObject Copy(Transform source, string name)
        {
            var root = new GameObject(name);
            root.layer = source.gameObject.layer;
            root.transform.SetPositionAndRotation(source.position, source.rotation);
            var block = new MaterialPropertyBlock();
            foreach (MeshRenderer piece in source.GetComponentsInChildren<MeshRenderer>(false))
            {
                MeshFilter filter = piece.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || piece.forceRenderingOff) continue;
                var part = new GameObject(piece.name);
                part.layer = piece.gameObject.layer;
                part.transform.SetParent(root.transform, false);
                part.transform.SetPositionAndRotation(piece.transform.position, piece.transform.rotation);
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterials = piece.sharedMaterials;
                r.shadowCastingMode = piece.shadowCastingMode;
                r.receiveShadows = piece.receiveShadows;
                if (piece.HasPropertyBlock())
                {
                    piece.GetPropertyBlock(block);
                    r.SetPropertyBlock(block);
                }
            }
            return root;
        }
    }

    private WeaponController weapon;
    private PlayerLocomotion locomotion;
    private PlayerAnimator playerAnimator;
    private FirstPersonView firstPerson;
    private PlayerChassis chassis;
    private Animator bodyAnimator;
    private Transform bodyRightHand;
    private View body, armsView;
    private BreakActionView bodyOpening, armsOpening;   // une arme qui s'ouvre (BreakActionGesture)

    /// <summary>Les gestes de ce joueur (tests).</summary>
    public IReadOnlyList<ReloadGesture> Gestures => gestures;

    /// <summary>Le geste où le chargeur quitte l'arme `data`, ou null (le geste commun).</summary>
    public ReloadGesture GestureFor(WeaponData data)
    {
        if (data == null || gestures == null) return null;
        foreach (ReloadGesture g in gestures)
            if (g != null && g.AppliesTo(data)) return g;
        return null;
    }

    /// <summary>Le chargeur du corps, puis celui des bras de la vue subjective (null hors du propriétaire) ; tests.</summary>
    public View Body => body;
    public View Arms => armsView;

    /// <summary>Ce qu'une arme qui s'ouvre montre sur le corps, puis sur les bras (tests).</summary>
    public BreakActionView BodyOpening => bodyOpening;
    public BreakActionView ArmsOpening => armsOpening;

    private void Awake()
    {
        weapon = GetComponentInChildren<WeaponController>();
        locomotion = GetComponent<PlayerLocomotion>();
        playerAnimator = GetComponent<PlayerAnimator>();
        firstPerson = GetComponent<FirstPersonView>();
        chassis = GetComponent<PlayerChassis>();
        Transform model = transform.Find("Model");
        bodyAnimator = model != null ? model.GetComponent<Animator>() : null;
        bodyRightHand = bodyAnimator != null ? bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        body = new View(bodyAnimator, false);
        bodyOpening = new BreakActionView(bodyAnimator, false);
    }

    private ReloadGesture CurrentGesture => weapon != null && (locomotion == null || !locomotion.IsSpectator) ? GestureFor(weapon.Data) : null;

    // Avant l'Animator : le geste de l'arme en main, sur le corps et sur les bras.
    private void Update()
    {
        ReloadGesture gesture = CurrentGesture;
        float value = gesture != null ? gesture.animatorValue : 0f;
        if (body.HasGestureParameter) bodyAnimator.SetFloat(GestureId, value);
        Animator view = firstPerson != null ? firstPerson.ViewAnimator : null;
        if (view != (armsView != null ? armsView.Animator : null))
        {
            armsView?.Restore();
            armsOpening?.Restore();
            armsView = view != null ? new View(view, true) : null;
            armsOpening = view != null ? new BreakActionView(view, true) : null;
        }
        if (armsView != null && armsView.HasGestureParameter) view.SetFloat(GestureId, value);
    }

    // Après l'Animator, la prise des mains et le placement des bras de la vue subjective.
    private void LateUpdate()
    {
        if (weapon == null) return;
        ReloadGesture gesture = CurrentGesture;
        bool reloading = weapon.ShowsReloadGesture && (locomotion == null || locomotion.IsAlive);
        float dt = Time.deltaTime;
        var opening = gesture as BreakActionGesture;
        bool empty = weapon.DisplayedAmmo <= 0;   // le coup parti, le rechargement pas encore commencé
        WeaponModel bodyShown = HeldWeapons.Visible(bodyRightHand);
        body.Step(bodyShown, gesture, reloading, Vector3.zero, drop, dt);
        bodyOpening.Step(bodyShown, opening, body.Progress, empty, Vector3.zero, drop, dt);
        if (armsView == null) return;
        Vector3 shift = gesture != null && playerAnimator != null
            ? playerAnimator.HandGrip.PalmPoint(chassis != null ? chassis.Value : ChassisId.CT01) - gesture.designPalm
            : Vector3.zero;
        WeaponModel armsShown = firstPerson != null ? firstPerson.ViewWeapon : null;
        armsView.Step(armsShown, gesture, reloading, shift, drop, dt);
        armsOpening?.Step(armsShown, opening, armsView.Progress, empty, shift, drop, dt);
    }

    private void OnDisable()
    {
        body?.Restore();
        armsView?.Restore();
        bodyOpening?.Restore();
        armsOpening?.Restore();
    }

#if UNITY_EDITOR
    /// <summary>Réglage par l'outil qui importe un geste (Editor seulement).</summary>
    public void EditorSetGestures(ReloadGesture[] value) => gestures = value;
#endif
}
