using UnityEngine;

/// <summary>
/// Feedback visuel de tir : flash de bouche, balle traçante, impact (étincelles, poussière,
/// trace sur le décor ; gerbe d'étincelles sur un robot) et son d'impact au point touché. Appelé
/// par WeaponController.Fire() (feedback local du tireur) et par BroadcastShotClientRpc (ce que
/// voient les autres joueurs).
///
/// L'API publique (SpawnTracer / SpawnImpact) n'a pas changé depuis le placeholder procédural :
/// WeaponController n'a rien eu à modifier. Les effets eux-mêmes vivent dans <see cref="Vfx"/>.
/// Sans l'asset Resources/VfxLibrary, on retombe sur l'ancien rendu (ligne + sphère), pour qu'un
/// asset manquant ne rende jamais les tirs invisibles.
///
/// Tireur ou spectateur ? Les deux appels reçoivent l'origine du rayon, qui est l'oeil du tireur.
/// Si elle coïncide avec la caméra active de cette machine, c'est notre propre tir, vu à la
/// première personne : le flash se réduit à sa lumière, et le traceur part d'une bouche virtuelle
/// sous la vue au lieu de sortir de l'oeil.
/// </summary>
public class WeaponVisualFeedback : MonoBehaviour
{
    [Header("Bouche du canon (en attendant une arme visible)")]
    [Tooltip("Vue à la première personne : décalage (droite, haut, avant) de la bouche virtuelle par rapport à la caméra, en mètres.")]
    [SerializeField] private Vector3 firstPersonMuzzleOffset = new Vector3(0.13f, -0.12f, 0.45f);
    [Tooltip("Vue d'un adversaire : décalage (droite, haut, avant) depuis son oeil — l'arme épaulée du robot.")]
    [SerializeField] private Vector3 thirdPersonMuzzleOffset = new Vector3(0.12f, -0.1f, 0.6f);

    [Header("Secours sans VfxLibrary — trace de tir")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.3f);
    [SerializeField] private float tracerWidth = 0.015f;
    [SerializeField] private float tracerDuration = 0.05f;

    [Header("Secours sans VfxLibrary — marqueur d'impact")]
    [SerializeField] private Color impactColor = new Color(1f, 0.3f, 0.1f);
    [SerializeField] private float impactSize = 0.12f;
    [SerializeField] private float impactLifetime = 1.2f;

    private static Shader cachedUnlitShader;
    private static readonly Collider[] overlap = new Collider[8];

    // SpawnTracer précède toujours SpawnImpact pour un même tir (dans Fire() comme dans la RPC) :
    // le premier dit au second si le tir est le nôtre.
    private bool pendingLocalShot;

    private static Shader GetUnlitShader()
    {
        if (cachedUnlitShader != null) return cachedUnlitShader;

        // On essaie plusieurs shaders unlit connus dans l'ordre, pour que ça marche que le projet
        // soit en URP (cas probable) ou en Built-in Render Pipeline, sans configuration manuelle.
        cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Sprites/Default");
        if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Unlit/Color");
        return cachedUnlitShader;
    }

    /// <summary>Flash de bouche + balle traçante entre le point de départ et le point d'arrivée du tir.</summary>
    public void SpawnTracer(Vector3 start, Vector3 end)
    {
        bool firstPerson = Vfx.IsLocalViewOrigin(start);
        pendingLocalShot = firstPerson;

        if (VfxLibrary.Instance == null)
        {
            SpawnFallbackTracer(start, end);
            return;
        }

        Vector3 dir = end - start;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 muzzle;
        if (firstPerson && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            muzzle = start + cam.right * firstPersonMuzzleOffset.x + cam.up * firstPersonMuzzleOffset.y + cam.forward * firstPersonMuzzleOffset.z;
        }
        else
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : transform.right;
            muzzle = start + right * thirdPersonMuzzleOffset.x + Vector3.up * thirdPersonMuzzleOffset.y + dir * thirdPersonMuzzleOffset.z;
        }

        Vfx.MuzzleFlash(muzzle, dir, firstPerson);
        Vfx.Tracer(muzzle, end);
    }

    /// <summary>Impact à l'endroit précis où le tir a touché quelque chose.</summary>
    public void SpawnImpact(Vector3 point, Vector3 normal)
    {
        bool localShot = pendingLocalShot;
        pendingLocalShot = false;

        if (VfxLibrary.Instance == null)
        {
            SpawnFallbackImpact(point, normal);
            return;
        }

        bool onRobot = TryFindHitZone(point, ZoneRadius, out PlayerHitboxZone zone);
        bool onWorld = !onRobot && Physics.CheckSphere(point, 0.05f, WorldMask, QueryTriggerInteraction.Ignore);
        if (!onRobot && !onWorld)
        {
            // Ni robot ni décor sous le point : c'est un impact décidé par le serveur sur la pose
            // REMBOBINÉE d'un adversaire, que cette machine affiche ailleurs (son propre retard
            // d'interpolation). Un robot tout proche ? Alors c'était lui. Sans ça, on poserait une
            // trace de balle flottant dans le vide.
            onRobot = TryFindHitZone(point, NearRobotRadius, out zone);
        }
        Vfx.BulletImpact(point, normal, onRobot ? ImpactSurface.Chassis : ImpactSurface.World, decal: onWorld);

        // La zone touchée ne voyage pas sur le réseau : le hitmarker (confirmé par le serveur)
        // vient la chercher ici, dans le tir local.
        if (onRobot && localShot)
        {
            Health target = zone.GetComponentInParent<Health>();
            if (target != null) CombatFeedback.ReportLocalHit(target, zone.Zone);
        }
    }

    private const float ZoneRadius = 0.06f;
    private const float NearRobotRadius = 0.6f;

    private static int HitboxMask
    {
        get
        {
            int mask = LayerMask.GetMask("Hitbox");
            return mask != 0 ? mask : 1 << 7;
        }
    }

    // La géométrie qui arrête les balles (WeaponData.worldMask vaut Default) : c'est sur elle, et
    // seulement sur elle, qu'une trace d'impact a un sens.
    private static int WorldMask => LayerMask.GetMask("Default");

    /// <summary>Le point touché est-il sur un hitbox de joueur ? Rien d'autre ne vit sur ce
    /// layer. La zone retenue est la plus proche du point, au cas où deux zones se touchent.</summary>
    private static bool TryFindHitZone(Vector3 point, float radius, out PlayerHitboxZone zone)
    {
        zone = null;
        int count = Physics.OverlapSphereNonAlloc(point, radius, overlap, HitboxMask, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var z = overlap[i].GetComponent<PlayerHitboxZone>();
            if (z == null) continue;
            float d = (overlap[i].ClosestPoint(point) - point).sqrMagnitude;
            if (d < best)
            {
                best = d;
                zone = z;
            }
        }
        return zone != null;
    }

    // ------------------------------------------------------------------ secours

    private void SpawnFallbackTracer(Vector3 start, Vector3 end)
    {
        var go = new GameObject("ShotTracer");
        var lr = go.AddComponent<LineRenderer>();

        var mat = new Material(GetUnlitShader());
        mat.color = tracerColor;
        lr.material = mat;
        lr.startColor = tracerColor;
        lr.endColor = tracerColor;
        lr.startWidth = tracerWidth;
        lr.endWidth = tracerWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Destroy(go, tracerDuration);
    }

    private void SpawnFallbackImpact(Vector3 point, Vector3 normal)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ImpactMarker";

        // CreatePrimitive livre TOUJOURS un collider. Il faut le neutraliser, et surtout le faire
        // IMMÉDIATEMENT : Destroy() est différé à la fin de la frame, donc le collider reste bien
        // vivant pour la physique entre-temps. C'est enabled = false qui prend effet tout de
        // suite ; Destroy ne fait que le ménage ensuite.
        //
        // Ce marqueur apparaît pile à l'endroit touché, donc à la surface du corps de la cible.
        // Un collider actif ne serait pas qu'un détail cosmétique :
        //   - le CharacterController de la cible se pousserait hors de la sphère à son prochain
        //     Move() — d'où un adversaire qui RECULE visiblement à chaque balle encaissée ;
        //   - la sphère naît sur le layer Default, celui de worldMask, donc elle bloquerait les
        //     tirs suivants comme un mur ;
        //   - elle compterait aussi comme obstacle pour CanStandUp() et l'anti-clipping du lean.
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
            Destroy(col);
        }

        go.transform.position = point + normal * 0.01f; // léger décalage pour éviter le z-fighting
        go.transform.localScale = Vector3.one * impactSize;

        var renderer = go.GetComponent<Renderer>();
        var mat = new Material(GetUnlitShader());
        mat.color = impactColor;
        renderer.material = mat;

        Destroy(go, impactLifetime);
    }
}
