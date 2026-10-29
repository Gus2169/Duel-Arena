using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Look caméra à la souris/stick, posé sur "CameraPivot". Gère aussi le FOV kick au sprint
/// et AddInstantRotation(), utilisée par WeaponController pour le recul.
///
/// Le YAW (rotation du corps du joueur) n'est PLUS géré ici : il est réseauté et appliqué de
/// façon déterministe dans PlayerLocomotion.Move() (voir commentaire là-bas). Ce script ne gère
/// donc plus que le PITCH (inclinaison caméra locale, purement cosmétique) et le FOV — et
/// uniquement pour le propriétaire local (voir garde IsOwner ci-dessous) : sans ça, chaque
/// instance de joueur présente sur une machine (y compris celle des AUTRES joueurs, affichée
/// localement) réagissait à la souris PHYSIQUE de cette machine, d'où la caméra qui semblait
/// "bouger toute seule" côté spectateur dès qu'on avait 2 joueurs dans la scène.
/// </summary>
public class PlayerCameraLook : MonoBehaviour
{
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    [Tooltip("Enfant 'Leanpivot' qui porte la caméra. Le lean y est appliqué APRÈS le pitch. Laissé vide, il est cherché par son nom.")]
    [SerializeField] private Transform leanPivot;

    [Header("FOV")]
    [Tooltip("La Camera réelle (sous LeanPivot). Laisse vide pour désactiver le FOV kick.")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float baseFov = 90f;
    [SerializeField] private float sprintFov = 96f;
    [SerializeField] private float fovTransitionSpeed = 8f;

    private PlayerInputReader input;
    private PlayerLocomotion locomotion;
    private FirstPersonView firstPersonView; // la lunette du sniper (zoom et sensibilité)
    private NetworkObject networkObject;
    private float pitch;

    /// <summary>Tangage de la caméra, en degrés (+ = vers le bas). Propriétaire uniquement (0
    /// ailleurs) : PlayerLocomotion le joint à chaque input pour que les autres joueurs voient le
    /// fusil pointer là où ce joueur regarde.</summary>
    public float Pitch => pitch;

    /// <summary>Décalage horizontal du RECUL, appliqué à la caméra seule (degrés). Le yaw du corps
    /// appartient à la simulation (PlayerLocomotion.Move) et n'en reçoit jamais : voir
    /// AddInstantRotation.</summary>
    private float recoilYaw;
    private bool initializedForOwner;

    private void Awake()
    {
        input = GetComponentInParent<PlayerInputReader>();
        locomotion = GetComponentInParent<PlayerLocomotion>();
        firstPersonView = GetComponentInParent<FirstPersonView>();
        networkObject = GetComponentInParent<NetworkObject>();
        if (leanPivot == null) leanPivot = transform.Find("Leanpivot");
    }

    private void LateUpdate()
    {
        // Non-owner (perso d'un autre joueur affiché localement) : on ne touche à rien ici, ni au
        // curseur ni à la rotation — sa caméra est de toute façon désactivée par PlayerLocomotion,
        // et son yaw suit la pose publiée par le serveur (voir PlayerLocomotion.UpdateRemoteInterpolation).
        if (networkObject != null && !networkObject.IsOwner) return;

        // Fait une fois, ici plutôt que dans Awake() : au moment d'Awake, IsOwner n'est pas
        // encore garanti fiable tant que l'objet n'a pas fini son spawn réseau.
        if (!initializedForOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (targetCamera != null) targetCamera.fieldOfView = baseFov;
            initializedForOwner = true;
        }

        if (input == null) return;

        // Dans la lunette du sniper, la sensibilité baisse comme le champ de vision (le yaw du
        // corps suit la même règle, dans PlayerLocomotion.ReadLocalInput). GameSettings.
        // SensitivityMultiplier est le réglage du menu Options — catégorie C pure, appliqué
        // après coup sur l'input, comme le reste de ce script.
        Vector2 look = input.LookInput * mouseSensitivity * GameSettings.SensitivityMultiplier
            * (firstPersonView != null ? firstPersonView.LookScale : 1f);

        // L'axe vertical inversé (Options) ne change que le sens du tangage, avant le snapshot qui le lit.
        if (GameSettings.InvertY) look.y = -look.y;
        pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
        ApplyLocalRotation();
        ApplyLean();

        UpdateFov();
    }

    private void ApplyLocalRotation()
    {
        transform.localRotation = Quaternion.Euler(pitch, recoilYaw, 0f);
    }

    /// <summary>Place la caméra sur l'œil penché. Le décalage est exprimé dans l'espace du JOUEUR
    /// (latéral + descente de la tête quand le buste s'incline) ; ce pivot-ci porte le pitch, donc
    /// on l'y ramène. Sans cette conversion, la descente de la tête partirait en avant ou en
    /// arrière dès qu'on regarde en haut ou en bas. Fait APRÈS le pitch, dans la même frame.</summary>
    private void ApplyLean()
    {
        if (leanPivot == null || locomotion == null) return;

        leanPivot.localPosition = Quaternion.Inverse(transform.localRotation) * locomotion.LeanCameraOffset;
        Vector3 jolt = CurrentJolt();
        leanPivot.localRotation = Quaternion.Euler(jolt.x, jolt.y, locomotion.LeanCameraTilt + jolt.z);
    }

    // ------------------------------------------------------------------
    // Secousses (2026-10-13, catégorie C) : un duelliste touché par un caillou ou par une balle (le sursaut,
    // 2026-10-29), un spectateur étourdi.
    // Elles tournent la caméra elle-même, donc la visée (le tir part de la caméra) : c'est voulu, un
    // caillou « déstabilise » (GDD § 9). Jamais le corps : son lacet appartient à la simulation.
    // ------------------------------------------------------------------

    private float shakeAmplitude;
    private float shakeDuration;
    private float shakeStartedAt = float.NegativeInfinity;
    private float wobbleWeight;
    private float wobbleSetAt = float.NegativeInfinity;

    /// <summary>Une secousse brève et amortie de `amplitude` degrés (signe : le côté où elle part),
    /// pendant `duration` secondes. Sans hasard : la même cause donne la même secousse.</summary>
    public void Shake(float amplitude, float duration)
    {
        shakeAmplitude = amplitude;
        shakeDuration = Mathf.Max(0.05f, duration);
        shakeStartedAt = Time.time;
    }

    /// <summary>Le vacillement d'un spectateur étourdi, de 0 à 1, à redonner à chaque image tant qu'il dure.</summary>
    public void Wobble(float weight)
    {
        wobbleWeight = Mathf.Clamp01(weight);
        wobbleSetAt = Time.time;
    }

    // Le sursaut d'un robot touché (2026-10-29, GDD § 6 : « un très léger sursaut de caméra », qui punit déjà le
    // joueur touché en premier) : la vue monte d'un rien en 30 ms et revient, sans hasard. Le tangage déplace
    // le point visé le temps du sursaut ; le roulis, d'un côté puis de l'autre, ne le déplace pas.
    private float flinchAmplitude;
    private float flinchStartedAt = float.NegativeInfinity;
    private float flinchSide = 1f;

    /// <summary>Le temps que met la vue à monter (s).</summary>
    public const float FlinchRise = 0.03f;
    /// <summary>Le temps du sursaut entier, retour compris (s).</summary>
    public const float FlinchDuration = 0.3f;

    /// <summary>Un sursaut de `degrees` degrés vers le haut. Un coup qui tombe pendant un autre reprend le
    /// sursaut là où il en est, sans additionner : il reste léger.</summary>
    public void Flinch(float degrees)
    {
        if (!(degrees > 0f)) return;
        float current = FlinchPitch(Time.time - flinchStartedAt, flinchAmplitude);
        flinchAmplitude = Mathf.Max(degrees, current);
        // Repris à la hauteur où il en est : la vue ne retombe pas d'un coup avant de remonter.
        flinchStartedAt = Time.time - FlinchRise * (current / flinchAmplitude);
        flinchSide = -flinchSide;
    }

    /// <summary>La montée de la vue (degrés) `t` secondes après un sursaut de `amplitude` : elle monte en
    /// FlinchRise, puis revient en douceur ; rien avant ni après. Fonction pure, testée.</summary>
    public static float FlinchPitch(float t, float amplitude)
    {
        if (!(amplitude > 0f) || !(t >= 0f) || t >= FlinchDuration) return 0f;
        if (t < FlinchRise) return amplitude * t / FlinchRise;
        float rest = 1f - (t - FlinchRise) / (FlinchDuration - FlinchRise);
        return amplitude * rest * rest;
    }

    /// <summary>Le sursaut (degrés) d'un coup de `damage` PV : `perDamage` par point, jamais plus de `max`.
    /// Fonction pure, testée.</summary>
    public static float FlinchDegrees(float damage, float perDamage, float max)
    {
        if (!(damage > 0f)) return 0f; // NaN compris
        return Mathf.Min(max, damage * perDamage);
    }

    /// <summary>La rotation ajoutée par les secousses : tangage, lacet, roulis (degrés).</summary>
    private Vector3 CurrentJolt()
    {
        Vector3 jolt = Vector3.zero;
        float t = Time.time - shakeStartedAt;
        if (t >= 0f && t < shakeDuration)
        {
            float decay = 1f - t / shakeDuration;
            float wave = Mathf.Sin(t * 38f) * decay * decay;
            jolt += new Vector3(0f, shakeAmplitude * wave, shakeAmplitude * 0.8f * wave);
        }
        float flinch = FlinchPitch(Time.time - flinchStartedAt, flinchAmplitude);
        if (flinch > 0f) jolt += new Vector3(-flinch, 0f, flinchSide * 0.6f * flinch);
        if (Time.time - wobbleSetAt < 0.1f && wobbleWeight > 0f)
        {
            float time = Time.time;
            jolt += new Vector3(Mathf.Sin(time * 3.1f) * 2.5f, Mathf.Sin(time * 2.3f) * 3f, Mathf.Sin(time * 4.2f) * 7f) * wobbleWeight;
        }
        return jolt;
    }

    private void UpdateFov()
    {
        if (targetCamera == null || locomotion == null) return;

        float zoom = firstPersonView != null ? firstPersonView.ScopeZoom : 1f;
        targetCamera.fieldOfView = NextFov(targetCamera.fieldOfView, baseFov, sprintFov, zoom,
                                           locomotion.IsSprinting, fovTransitionSpeed, Time.deltaTime);
    }

    /// <summary>
    /// Le champ de vision de l'image suivante. Sans état, testée.
    ///
    /// La lunette du sniper (2026-10-11) : la vue zoome d'un coup quand on y entre, comme dans Call
    /// of Duty, et revient d'un coup quand on en sort, MÊME EN COURANT. Corrigé le 2026-10-12 :
    /// le retour était réservé au joueur qui ne sprinte pas. Lâcher la visée la touche de sprint
    /// enfoncée (la visée coupait le sprint, qui reprend aussitôt) laissait le champ remonter de
    /// 28° à 96° à 8°/s : la vue restait zoomée près de neuf secondes, ou jusqu'à ce qu'on lâche
    /// le sprint. Seul le passage du champ normal au champ de sprint se fait en douceur.
    /// </summary>
    public static float NextFov(float current, float baseFov, float sprintFov, float zoom, bool sprinting,
                                float transitionSpeed, float deltaTime)
    {
        if (zoom > 1f) return ZoomedFov(baseFov, zoom);
        if (current < baseFov - 0.01f) current = baseFov;
        float target = sprinting ? sprintFov : baseFov;
        return Mathf.MoveTowards(current, target, transitionSpeed * deltaTime);
    }

    /// <summary>Champ de vision vertical (degrés) qui grossit `zoom` fois le champ `fov` : la
    /// tangente du demi-angle est divisée d'autant. Sans état, testée.</summary>
    public static float ZoomedFov(float fov, float zoom)
        => 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, zoom)) * Mathf.Rad2Deg;

    /// <summary>
    /// Applique une rotation instantanée à la CAMÉRA, indépendamment de l'input souris. Utilisé
    /// pour le kick de recul et sa récupération. Même convention de signe que le look souris :
    /// pitchDelta positif = caméra monte, yawDelta positif = rotation vers la droite.
    ///
    /// 🚨 Le yaw de recul ne touche JAMAIS le corps (corrigé le 2026-10-05). Il faisait
    /// `locomotion.transform.Rotate(...)`, donc modifiait le yaw — une valeur de la simulation —
    /// HORS de Move() : un client distant tournait sans que le serveur le sache. Le zigzag du MP5
    /// restait sous le seuil de réconciliation de 1°, mais une arme qui dévie d'un seul côté
    /// aurait provoqué des recalages en pleine rafale. La visée, elle, ne change pas : le tir
    /// part de la caméra, qui porte ce décalage.
    /// </summary>
    public void AddInstantRotation(float pitchDelta, float yawDelta)
    {
        pitch = Mathf.Clamp(pitch - pitchDelta, minPitch, maxPitch);
        recoilYaw += yawDelta;
        ApplyLocalRotation();
        ApplyLean();
    }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Surface de test : décalage horizontal de recul porté par la caméra.</summary>
    public float TestRecoilYaw => recoilYaw;
#endif
}
