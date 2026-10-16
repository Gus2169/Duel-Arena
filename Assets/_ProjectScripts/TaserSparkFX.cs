using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les arcs électriques du gant à décharge (2026-10-16, à la place des étincelles : l'utilisateur
/// voulait « de vrais arcs électriques, comme des éclairs, qui partent du gant »). Catégorie C pure :
/// rien ici n'est lu par la simulation, la surface touchable ou le tir.
///
/// Le geste se voit comme il s'entend (TaserController) : à l'appui, la charge, de petits arcs qui
/// sautent d'une électrode à l'autre ; au CONTACT, ContactDelay plus tard (l'instant où le serveur
/// résout le coup), la décharge : des éclairs partent des électrodes vers ce que le coup toucherait
/// (TaserController.TryPreviewContact : le corps à portée, sinon le mur, sinon ils se perdent dans
/// l'air), se ramifient, changent de forme toutes les `reshapeInterval` et s'éteignent ; la lumière du
/// poing bat avec eux. Que le coup touche ou non : seul le serveur le juge.
///
/// Les couleurs sont celles de l'énergie dans la bible (§ 12) : un cœur blanc et un halo rouge, comme
/// la jauge du gant et les lueurs des armes laser ; ni le rouge ni le blanc ne sont des couleurs de
/// robot. Le matériau (DuelArena/ArcElectrique) dessine lui-même la lueur : le jeu n'a pas de bloom.
///
/// Le composant vit sur le repère « Poing » de GantChoc.prefab, donc sur chaque copie du gant : celle
/// du corps, vue par les autres, et celle de la vue subjective (FirstPersonView, layer Viewmodel),
/// montrée seulement pendant le coup. Il SONDE l'instant du dernier coup vu ici
/// (TaserController.LastStrikeSeenAt) au lieu de s'abonner à OnStrike : la copie des bras ne s'active
/// qu'une fois le geste commencé, et un abonnement ratait le coup (l'ancienne dette n° 59). Chez le
/// joueur, seuls ses bras dessinent ; son corps, qu'il ne voit pas, se tait.
/// </summary>
[DefaultExecutionOrder(600)] // après l'animation et le placement de la vue subjective (FirstPersonView)
public class TaserSparkFX : MonoBehaviour
{
    public enum Phase { None, Charge, Discharge }

    [Tooltip("Les pointes des électrodes, dans le repère du poing (m) : les arcs en partent. Relevées sur le modèle par l'importeur du gant (GloveImporter.ElectrodeTips).")]
    [SerializeField] private Vector3[] electrodes = new Vector3[0];

    [Tooltip("Le haut des condensateurs, sur la manchette, dans le repère du poing (m) : des arcs en courent jusqu'aux électrodes, par-dessus le dos de la main, là où le joueur voit son gant pendant le coup. Relevés par l'importeur (GloveImporter.CapacitorTops).")]
    [SerializeField] private Vector3[] capacitors = new Vector3[0];

    [Tooltip("Le matériau des arcs (DuelArena/ArcElectrique). L'étourdissement des spectateurs le reprend.")]
    [SerializeField] private Material arcMaterial;

    [Tooltip("La lumière du poing, qui bat avec la décharge.")]
    [SerializeField] private Light flash;

    [Header("La décharge (ressenti, 2026-10-16)")]
    [Tooltip("Durée de la décharge après le contact (s). Le geste entier dure 0,6 s (TaserController.StrikeDuration).")]
    [SerializeField] private float dischargeDuration = 0.24f;

    [Tooltip("Un arc change de forme toutes les ... (s) : c'est ce qui le fait crépiter.")]
    [SerializeField] private float reshapeInterval = 0.035f;

    [Tooltip("Éclairs principaux de la décharge, chacun parti d'une électrode, avec jusqu'à deux branches.")]
    [SerializeField, Range(1, 4)] private int bolts = 4;

    [Tooltip("Largeur d'un éclair principal à son départ, halo compris (m). Les branches en ont un peu plus de la moitié, les arcs entre électrodes 40 %.")]
    [SerializeField] private float boltWidth = 0.032f;

    [Tooltip("Largeur apparente minimale (rad) : un éclair reste lisible de loin.")]
    [SerializeField] private float minimumAngularWidth = 0.005f;

    [Tooltip("Sans rien à portée, les éclairs se perdent dans l'air à cette distance du poing (m, de ... à ...).")]
    [SerializeField] private Vector2 openReach = new Vector2(0.4f, 0.85f);

    [Tooltip("Intensité de la lumière du poing au contact.")]
    [SerializeField] private float flashIntensity = 6f;

    /// <summary>La force des arcs de la charge, entre l'appui et le contact.</summary>
    public const float ChargeStrength = 0.55f;

    // Les lignes : chaque éclair principal suivi de ses deux branches, un arc par paire d'électrodes
    // voisines, puis les arcs qui courent des condensateurs aux électrodes.
    private const int BoltGenerations = 4, BranchGenerations = 3, CrackleGenerations = 2, ClimbGenerations = 3;
    private const int BranchesPerBolt = 2, ClimbLines = 2;
    private const float BoltJaggedness = 0.28f, BranchJaggedness = 0.3f, CrackleJaggedness = 0.35f, ClimbJaggedness = 0.25f;

    private Transform boundRoot;   // le joueur dont on a cherché le taser (refait s'il change)
    private TaserController taser;
    private PlayerLocomotion locomotion;
    private FirstPersonView firstPersonView;
    private Health health;
    private Camera viewCamera;   // la caméra du joueur, quand ce gant est celui de ses bras
    private int viewLayer;
    private bool onView;
    private ArcLines arcs;
    private int shownShape = int.MinValue;
    private float shownStrike = float.NaN;
    private bool contact;
    private Vector3 contactPoint;   // dans le monde
    private static AnimationCurve taper;

    /// <summary>Le matériau des arcs (repris par l'étourdissement des spectateurs).</summary>
    public Material ArcMaterial => arcMaterial;

    /// <summary>Les pointes des électrodes, dans le repère du poing.</summary>
    public IReadOnlyList<Vector3> Electrodes => electrodes;

    /// <summary>Le haut des condensateurs, dans le repère du poing.</summary>
    public IReadOnlyList<Vector3> Capacitors => capacitors;

    /// <summary>La lumière du poing.</summary>
    public Light Flash => flash;

    /// <summary>Durée de la décharge après le contact (s).</summary>
    public float DischargeDuration => dischargeDuration;

    /// <summary>
    /// Où en est le geste `sinceStrike` secondes après l'appui : la charge jusqu'au contact, puis la
    /// décharge pendant `dischargeDuration`, puis plus rien. Sans état, testée.
    /// </summary>
    public static Phase PhaseAt(float sinceStrike, float contactDelay, float dischargeDuration)
    {
        if (!(sinceStrike >= 0f)) return Phase.None;   // NaN compris
        if (sinceStrike < contactDelay) return Phase.Charge;
        return sinceStrike < contactDelay + dischargeDuration ? Phase.Discharge : Phase.None;
    }

    /// <summary>La force de la décharge `sinceContact` secondes après le contact : pleine au contact,
    /// éteinte au bout de `duration`. Sans état, testée.</summary>
    public static float DischargeStrength(float sinceContact, float duration)
    {
        if (duration <= 0f) return 0f;
        return Mathf.Sqrt(1f - Mathf.Clamp01(sinceContact / duration));
    }

    /// <summary>Qui dessine les arcs : les bras du joueur, la seule copie du gant qu'il voie, et le corps
    /// chez les autres. Le corps d'un joueur qui a sa vue subjective se tait. Sans état, testée.</summary>
    public static bool ShouldDraw(bool onViewCopy, bool playerHasView) => onViewCopy || !playerHasView;

    private void Awake()
    {
        viewLayer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        if (flash != null) flash.enabled = false;
        Bind();
    }

    /// <summary>Le taser et le joueur du gant, cherchés depuis sa racine : le gant n'est pas sous le
    /// taser (l'enfant « Weapon »), et une copie du modèle peut changer de parent après sa création.</summary>
    private void Bind()
    {
        boundRoot = transform.root;
        taser = GetComponentInParent<TaserController>();
        if (taser == null) taser = boundRoot.GetComponentInChildren<TaserController>(true);
        locomotion = boundRoot.GetComponent<PlayerLocomotion>();
        firstPersonView = boundRoot.GetComponent<FirstPersonView>();
        health = boundRoot.GetComponent<Health>();
        viewCamera = null;
        if (arcs != null) arcs.Destroy();   // elles vivent sous l'ancienne racine
        arcs = null;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        if (arcs != null) arcs.Destroy();
        arcs = null;
    }

    private void LateUpdate()
    {
        if (transform.root != boundRoot) Bind();
        float since = taser != null ? Time.time - taser.LastStrikeSeenAt : float.PositiveInfinity;
        Phase phase = taser != null ? PhaseAt(since, taser.ContactDelay, dischargeDuration) : Phase.None;
        // La copie des bras prend le layer Viewmodel après sa création (FirstPersonView.BuildView).
        onView = viewLayer >= 0 && gameObject.layer == viewLayer;
        bool playerHasView = firstPersonView != null && firstPersonView.ViewAnimator != null;
        bool dead = health != null && health.IsDead;   // un attaquant tué pendant le geste ne frappe pas
        if (phase == Phase.None || dead || !ShouldDraw(onView, playerHasView) || arcMaterial == null || electrodes.Length < 2)
        {
            Hide();
            return;
        }
        if (onView && viewCamera == null) viewCamera = GetComponentInParent<Camera>();
        EnsureArcs();

        int shape = Mathf.FloorToInt(since / Mathf.Max(0.005f, reshapeInterval));
        if (taser.LastStrikeSeenAt != shownStrike) contact = false;   // un nouveau coup
        if (shape != shownShape || taser.LastStrikeSeenAt != shownStrike)
        {
            shownShape = shape;
            shownStrike = taser.LastStrikeSeenAt;
            // Là où va la décharge : revu à chaque forme, la cible bouge.
            if (phase == Phase.Discharge) FindContact();
        }
        uint seed = ElectricArc.Hash(ElectricArc.Hash((uint)Mathf.RoundToInt(shownStrike * 1000f)) ^ (uint)shape);
        float strength = phase == Phase.Discharge
            ? DischargeStrength(since - taser.ContactDelay, dischargeDuration)
            : ChargeStrength;
        Draw(phase == Phase.Discharge, seed, strength);
    }

    /// <summary>Les arcs de cette image. Leur forme ne dépend que de la graine (celle du coup et de la
    /// forme en cours) : d'une image à l'autre, ils suivent le poing sans changer de forme.</summary>
    private void Draw(bool discharge, uint seed, float strength)
    {
        Camera viewer = onView ? viewCamera : Camera.main;
        Vector3 eye = viewer != null ? viewer.transform.position : transform.position;
        Vector3 forward = transform.forward;
        float beat = 0.6f + 0.4f * ElectricArc.Next01(ref seed);
        int count = electrodes.Length;
        int line = 0;

        if (discharge)
        {
            // Le point du monde, vu par la caméra des bras pour la copie de la vue subjective.
            Vector3 target = onView ? ToArms(contactPoint) : contactPoint;
            int first = (int)(ElectricArc.Next01(ref seed) * count);
            for (int b = 0; b < bolts; b++, line += 1 + BranchesPerBolt)
            {
                uint s = ElectricArc.Hash(seed + (uint)(b + 1) * 0x632be5abU);
                Vector3 start = transform.TransformPoint(electrodes[(first + b) % count]);
                Vector3 end;
                if (contact)
                {
                    // Les éclairs tombent autour du point touché, pas tous au même endroit : en vue
                    // subjective, le poing vise la cible et des éclairs tous sur le même point
                    // partiraient dans l'axe du regard, cachés derrière le gant.
                    Vector3 toward = target - start;
                    Vector3 side = toward.sqrMagnitude > 1e-8f ? ElectricArc.RandomPerpendicular(toward.normalized, ref s) : Vector3.zero;
                    end = target + side * (0.04f + 0.10f * ElectricArc.Next01(ref s));
                }
                else
                {
                    Vector3 direction = ElectricArc.Tilt(forward, 22f * ElectricArc.Next01(ref s), ref s);
                    end = start + direction * Mathf.Lerp(openReach.x, openReach.y, ElectricArc.Next01(ref s));
                }
                float length = Vector3.Distance(start, end);
                // Chaque éclair se courbe de son côté : ils s'écartent les uns des autres.
                Vector3 bow = length > 1e-4f
                    ? ElectricArc.RandomPerpendicular((end - start) / length, ref s) * (length * (0.1f + 0.12f * ElectricArc.Next01(ref s)))
                    : Vector3.zero;
                Vector3[] p = arcs.Points(line);
                ElectricArc.Build(start, end, BoltGenerations, BoltJaggedness, bow, ref s, p);
                float width = Width(boltWidth, start, eye);
                float force = strength * (0.7f + 0.3f * ElectricArc.Next01(ref s));
                // Un éclair qui frappe reste vif jusqu'au bout ; perdu dans l'air, il s'y éteint.
                arcs.Show(line, width, force, contact ? force : force * 0.15f);

                for (int k = 1; k <= BranchesPerBolt; k++)
                {
                    if (ElectricArc.Next01(ref s) < 0.35f)
                    {
                        arcs.Hide(line + k);
                        continue;
                    }
                    int last = p.Length - 1;
                    int at = 2 + (int)(ElectricArc.Next01(ref s) * (last - 4));   // ni au départ ni au bout
                    Vector3 along = (p[at + 1] - p[at - 1]).normalized;
                    Vector3 direction = ElectricArc.Tilt(along, 25f + 20f * ElectricArc.Next01(ref s), ref s);
                    float reach = Vector3.Distance(p[at], end) * (0.3f + 0.3f * ElectricArc.Next01(ref s));
                    ElectricArc.Build(p[at], p[at] + direction * reach, BranchGenerations, BranchJaggedness, Vector3.zero, ref s, arcs.Points(line + k));
                    arcs.Show(line + k, width * 0.55f, force * 0.8f, 0f);
                }
            }
        }
        else
        {
            for (int i = 0; i < bolts * (1 + BranchesPerBolt); i++) arcs.Hide(i);
            line = bolts * (1 + BranchesPerBolt);
        }

        // Les arcs entre électrodes voisines : tous pendant la décharge ; pendant la charge, un sûr et
        // un autre une fois sur deux.
        int pairs = count - 1;
        int sure = (int)(ElectricArc.Next01(ref seed) * pairs);
        for (int c = 0; c < pairs; c++)
        {
            int index = line + c;
            uint s = ElectricArc.Hash(seed ^ (uint)(c + 7) * 0x85ebca77U);
            if (!discharge && c != sure && ElectricArc.Next01(ref s) < 0.5f)
            {
                arcs.Hide(index);
                continue;
            }
            Vector3 a = transform.TransformPoint(electrodes[c]);
            Vector3 b = transform.TransformPoint(electrodes[c + 1]);
            float gap = Vector3.Distance(a, b);
            // En avant et vers le dos de la main : vu de derrière, en vue subjective, l'arc dépasse du poing.
            Vector3 bow = forward * (gap * (0.25f + 0.3f * ElectricArc.Next01(ref s)))
                        + transform.up * (gap * (0.2f + 0.3f * ElectricArc.Next01(ref s)));
            ElectricArc.Build(a, b, CrackleGenerations, CrackleJaggedness, bow, ref s, arcs.Points(index));
            float force = strength * (0.6f + 0.4f * ElectricArc.Next01(ref s));
            arcs.Show(index, Width(boltWidth * 0.4f, a, eye), force, force);
        }
        line += pairs;

        // Les arcs qui courent des condensateurs aux électrodes, par-dessus le dos de la main : un
        // pendant la charge, les deux pendant la décharge.
        int climbs = capacitors.Length > 0 ? ClimbLines : 0;
        for (int k = 0; k < climbs; k++)
        {
            int index = line + k;
            uint s = ElectricArc.Hash(seed ^ (uint)(k + 31) * 0xc2b2ae35U);
            if (!discharge && k > 0)
            {
                arcs.Hide(index);
                continue;
            }
            Vector3 a = transform.TransformPoint(capacitors[(k + (int)(ElectricArc.Next01(ref s) * capacitors.Length)) % capacitors.Length]);
            Vector3 b = transform.TransformPoint(electrodes[(int)(ElectricArc.Next01(ref s) * count) % count]);
            float length = Vector3.Distance(a, b);
            Vector3 bow = transform.up * (length * (0.12f + 0.08f * ElectricArc.Next01(ref s)));
            ElectricArc.Build(a, b, ClimbGenerations, ClimbJaggedness, bow, ref s, arcs.Points(index));
            float force = strength * (discharge ? 0.85f : 1f) * (0.6f + 0.4f * ElectricArc.Next01(ref s));
            arcs.Show(index, Width(boltWidth * 0.45f, a, eye), force, force);
        }

        if (flash != null)
        {
            if (flash.enabled != discharge) flash.enabled = discharge;
            if (discharge) flash.intensity = flashIntensity * strength * beat;
        }
    }

    /// <summary>Là où le coup toucherait, vu d'ici : depuis la caméra du joueur pour ses bras, depuis l'œil
    /// et le regard affichés chez les autres (PlayerLocomotion), sinon depuis le poing (aperçu, tests).</summary>
    private void FindContact()
    {
        Vector3 origin, direction;
        if (onView && viewCamera != null)
        {
            origin = viewCamera.transform.position;
            direction = viewCamera.transform.forward;
        }
        else if (!onView && locomotion != null)
        {
            origin = locomotion.DisplayEyePosition;
            direction = locomotion.DisplayLookDirection;
        }
        else
        {
            origin = transform.position;
            direction = transform.forward;
        }
        // Une cible tuée par le coup sort des requêtes (sa surface touchable s'éteint) : la décharge
        // reste sur le point touché jusqu'au bout, au lieu de la lâcher.
        if (taser.TryPreviewContact(origin, direction, out Vector3 point, out _))
        {
            contact = true;
            contactPoint = point;
        }
    }

    private Vector3 ToArms(Vector3 world)
    {
        Camera arms = firstPersonView != null ? firstPersonView.ArmsCamera : null;
        if (viewCamera == null || arms == null) return world;
        return ElectricArc.WorldToArms(world, viewCamera.transform, viewCamera.fieldOfView, arms.fieldOfView);
    }

    private float Width(float width, Vector3 at, Vector3 eye)
        => Mathf.Max(width, Vector3.Distance(eye, at) * minimumAngularWidth);

    private void EnsureArcs()
    {
        if (arcs != null) return;
        if (taper == null) taper = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.45f));
        int boltLines = bolts * (1 + BranchesPerBolt);
        int crackleLines = electrodes.Length - 1;
        var generations = new int[boltLines + crackleLines + (capacitors.Length > 0 ? ClimbLines : 0)];
        for (int i = 0; i < generations.Length; i++)
        {
            generations[i] = i >= boltLines + crackleLines ? ClimbGenerations
                : i >= boltLines ? CrackleGenerations
                : i % (1 + BranchesPerBolt) == 0 ? BoltGenerations : BranchGenerations;
        }
        // Sous la racine du joueur, en coordonnées du monde : hors du modèle, que copient l'épave et la
        // vue subjective, et cachées avec le joueur par l'écran de fin (comme le laser, TacticalLaser).
        arcs = new ArcLines(transform.root, onView ? "Arcs du gant (vue)" : "Arcs du gant", arcMaterial,
                            onView ? viewLayer : 0, generations, taper);
    }

    private void Hide()
    {
        if (arcs != null) arcs.HideAll();
        if (flash != null && flash.enabled) flash.enabled = false;
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : les lignes de ce gant (null tant qu'il n'a rien dessiné).</summary>
    public ArcLines TestArcs => arcs;

    /// <summary>Surface de test : le nombre de lignes des éclairs principaux et de leurs branches (les
    /// arcs entre électrodes viennent après, puis ceux des condensateurs).</summary>
    public int TestBoltLines => bolts * (1 + BranchesPerBolt);

    /// <summary>Surface de test : la première ligne des arcs des condensateurs.</summary>
    public int TestFirstClimbLine => TestBoltLines + electrodes.Length - 1;
#endif
}
