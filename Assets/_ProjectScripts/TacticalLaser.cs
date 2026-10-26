using UnityEngine;

/// <summary>
/// Le laser tactique (2026-10-16, GDD § 6) : un faisceau qui sort de l'émetteur, droit dans l'axe du
/// canon, et un point rouge là où il s'arrête. Il aide le tir au juger (le cône de la hanche et la gerbe
/// d'un pompe se resserrent : AttachmentCatalog.laserHipSpread) et trahit qui le porte : le faisceau se
/// voit de loin, le point sur le mur aussi. Un vrai choix, comme le voulait l'utilisateur.
///
/// Un vrai laser (demandé par l'utilisateur le 2026-10-16 ; il suivait le regard) : il suit l'arme,
/// pendant le rechargement, le sprint, la sortie de l'arme comme en visée. Chez les autres, il part de
/// l'arme de son corps ; chez le joueur, de celle de ses bras, dessinée par leur caméra au champ plus
/// étroit (FirstPersonView) : le faisceau est cherché dans le monde là où le joueur le VOIT
/// (ArmsRayToWorld), et dessiné par la caméra du joueur depuis l'endroit où il voit l'émetteur. Il est
/// arrêté par le décor et les corps comme une balle (WeaponData.worldMask, hitboxMask), jamais par les
/// zones du joueur. Pas dans la lunette du sniper : le point y suit le regard.
///
/// Réglé sur la ligne de visée de l'arme (2026-10-26) : le faisceau va de l'émetteur au point que touche
/// la ligne de visée (la mire du viseur, ou de l'arme, dans l'axe du canon), comme un laser réglé à la
/// distance de ce qu'on vise. Parallèle au canon, il passait à une dizaine de centimètres sous le
/// viseur et à droite : de près, le point n'était pas au centre du réticule en visée (signalé par
/// l'utilisateur). Il suit toujours l'arme : la ligne de visée bouge avec elle.
///
/// Catégorie C réseautée : les accessoires sont publiés par le serveur (PlayerWeaponAttachments), la
/// pose de l'arme suit l'animation de chaque machine ; rien ne relit le faisceau. Posé sur la racine du
/// préfab joueur.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(600)] // après PlayerAnimator (l'arme suit le regard) et la caméra du joueur
public class TacticalLaser : MonoBehaviour
{
    [Tooltip("Largeur apparente minimale du faisceau (rad) : il reste visible de loin, une fois et demie un pixel en 1080p sous 90°.")]
    [SerializeField] private float minimumAngularWidth = 0.0028f;

    [Tooltip("La luminosité du faisceau à son bout, rapportée à celle de l'émetteur : il s'éteint en portant.")]
    [SerializeField, Range(0f, 1f)] private float endBrightness = 0.3f;

    /// <summary>La distance de réglage la plus courte (m) : contre un mur, le faisceau ne vire pas en travers.</summary>
    public const float MinimumZeroDistance = 1f;

    private PlayerLocomotion locomotion;
    private WeaponController weapon;
    private Health health;
    private FirstPersonView firstPersonView;
    private Transform rightHand;
    private int worldLayer;
    private LineRenderer beam;
    private MeshRenderer dot;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private static Mesh quad;

    /// <summary>Le point est-il montré cette image ? (tests)</summary>
    public bool DotShown => dot != null && dot.enabled;
    /// <summary>Le faisceau est-il montré cette image ? (tests)</summary>
    public bool BeamShown => beam != null && beam.enabled;
    /// <summary>Où est le point (tests).</summary>
    public Vector3 DotPosition => dot != null ? dot.transform.position : Vector3.zero;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        weapon = GetComponentInChildren<WeaponController>(true);
        health = GetComponent<Health>();
        firstPersonView = GetComponent<FirstPersonView>();
        worldLayer = 0;   // Default : la caméra du joueur le dessine
        Transform model = transform.Find("Model");
        Animator animator = model != null ? model.GetComponent<Animator>() : null;
        rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    }

    private void OnDestroy()
    {
        if (beam != null) Destroy(beam.gameObject);
        if (dot != null) Destroy(dot.gameObject);
    }

    private void LateUpdate()
    {
        bool on = TryAim(out Vector3 origin, out Vector3 direction, out bool beamShown, out WeaponData data,
                         out bool zeroed, out Vector3 sightOrigin, out Vector3 sightDirection);
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;
        if (!on || catalog == null || catalog.dotMaterial == null)
        {
            Show(false, false);
            return;
        }

        float range = catalog.beamRange;
        if (zeroed)
        {
            float zero = Cast(sightOrigin, sightDirection, range, data, out RaycastHit seen) ? seen.distance : range;
            direction = ZeroedDirection(origin, sightOrigin, sightDirection, Mathf.Max(MinimumZeroDistance, zero));
        }
        Vector3 end = origin + direction * range;
        bool hit = Cast(origin, direction, range, data, out RaycastHit stop);
        if (hit) end = stop.point - direction * 0.01f;   // posé devant la surface, pas dedans

        Camera viewer = Camera.main;
        beamShown &= catalog.beamMaterial != null;
        Show(hit, beamShown);
        if (hit) PlaceDot(end, viewer, catalog);
        if (beamShown)
        {
            // Dessiné par la caméra du joueur (layer Default), du point où l'on voit l'émetteur.
            if (beam.gameObject.layer != worldLayer) beam.gameObject.layer = worldLayer;
            PlaceBeam(origin, end, viewer, catalog);
        }
    }

    /// <summary>Le laser est-il allumé, d'où part son faisceau, dans quel sens, et se voit-il ? Le long du
    /// canon de l'arme tenue : celle des bras chez le joueur, ramenée là où il la voit ; celle du corps
    /// chez les autres. Et la ligne de visée de cette arme, sur laquelle le régler (`zeroed`).</summary>
    private bool TryAim(out Vector3 origin, out Vector3 direction, out bool beamShown, out WeaponData data,
                        out bool zeroed, out Vector3 sightOrigin, out Vector3 sightDirection)
    {
        origin = direction = sightOrigin = sightDirection = Vector3.zero;
        beamShown = zeroed = false;
        data = null;
        if (weapon == null || locomotion == null || locomotion.IsSpectator) return false;
        if (health != null && health.IsDead) return false;
        int slot = weapon.DisplayedSlot;
        data = weapon.DataOf(slot);
        if (data == null || !weapon.Attachments.Laser(slot) || !WeaponAttachments.Accepts(data, AttachmentCategory.Laser, 1)) return false;

        if (weapon.IsOwner && weapon.AimCamera != null)
        {
            WeaponModel view = firstPersonView != null ? firstPersonView.ViewWeapon : null;
            Transform emitter = Emitter(view);
            Camera arms = firstPersonView != null ? firstPersonView.ArmsCamera : null;
            Camera eye = weapon.AimCamera;
            if (emitter != null && arms != null)
            {
                Vector3 forward = view.transform.forward;
                ArmsRayToWorld(emitter.position, forward, eye.transform, eye.fieldOfView, arms.fieldOfView,
                               out origin, out direction);
                Transform sight = AttachmentMount.SightOf(view, out _);
                if (sight != null)
                {
                    ArmsRayToWorld(sight.position, forward, eye.transform, eye.fieldOfView, arms.fieldOfView,
                                   out sightOrigin, out sightDirection);
                    zeroed = true;
                }
                beamShown = true;
            }
            else
            {
                // Dans la lunette, les bras ne se voient plus : le point suit le regard, sans faisceau.
                origin = eye.transform.position;
                direction = eye.transform.forward;
            }
        }
        else
        {
            WeaponModel held = HeldWeapons.Visible(rightHand);
            Transform emitter = Emitter(held);
            if (emitter == null) return false;
            origin = emitter.position;
            direction = held.transform.forward;
            Transform sight = AttachmentMount.SightOf(held, out _);
            if (sight != null)
            {
                sightOrigin = sight.position;
                sightDirection = direction;
                zeroed = true;
            }
            beamShown = true;
        }
        return direction.sqrMagnitude > 0.5f;
    }

    /// <summary>
    /// Le faisceau réglé sur la ligne de visée (2026-10-26) : de l'émetteur vers le point de la ligne de
    /// visée (`sightOrigin`, `sightDirection`, unitaire) à `distance`, celle de ce qu'elle touche. En
    /// visée, ce point est au centre du réticule : le point du laser y tombe, à toute distance. Fonction
    /// pure, testée.
    /// </summary>
    public static Vector3 ZeroedDirection(Vector3 emitter, Vector3 sightOrigin, Vector3 sightDirection, float distance)
    {
        Vector3 toTarget = sightOrigin + sightDirection * distance - emitter;
        return toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : sightDirection;
    }

    /// <summary>
    /// Le faisceau que le joueur voit partir de ses bras, dans le monde. Les bras sont dessinés par une
    /// caméra au même endroit que la sienne, au champ plus étroit (FirstPersonView) : un point des bras
    /// se voit là où la caméra du joueur montrerait le point du monde dont l'écart à l'axe est divisé
    /// par le rapport des tangentes de leurs demi-champs (l'inverse d'ElectricArc.WorldToArms). Une
    /// droite reste une droite : le faisceau des bras (`armsOrigin`, `armsDirection`) se voit le long de
    /// ce rayon du monde. Fonction pure, testée.
    /// </summary>
    public static void ArmsRayToWorld(Vector3 armsOrigin, Vector3 armsDirection, Transform eye, float playerFieldOfView,
                                      float armsFieldOfView, out Vector3 origin, out Vector3 direction)
    {
        float k = Mathf.Tan(armsFieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(playerFieldOfView * 0.5f * Mathf.Deg2Rad);
        if (!(k > 1e-4f)) k = 1f;
        Vector3 localOrigin = eye.InverseTransformPoint(armsOrigin);
        Vector3 localDirection = eye.InverseTransformDirection(armsDirection);
        origin = eye.TransformPoint(new Vector3(localOrigin.x / k, localOrigin.y / k, localOrigin.z));
        direction = eye.TransformDirection(new Vector3(localDirection.x / k, localDirection.y / k, localDirection.z)).normalized;
    }

    /// <summary>Le premier obstacle du regard : le décor, ou un autre corps devant lui (comme le tir,
    /// WeaponController.ResolveShot). Les zones du joueur lui-même ne l'arrêtent pas.</summary>
    private bool Cast(Vector3 origin, Vector3 direction, float range, WeaponData data, out RaycastHit stop)
    {
        bool found = Physics.Raycast(origin, direction, out stop, range, data.worldMask, QueryTriggerInteraction.Ignore);
        float limit = found ? stop.distance : range;
        int n = Physics.RaycastNonAlloc(origin, direction, hits, limit, data.hitboxMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= limit) continue;
            limit = hits[i].distance;
            stop = hits[i];
            found = true;
        }
        return found;
    }

    /// <summary>L'émetteur du laser monté sur cette arme, ou null.</summary>
    private static Transform Emitter(WeaponModel model)
    {
        WeaponAttachment laser = model != null ? AttachmentMount.Mounted(model.transform, AttachmentCategory.Laser) : null;
        return laser != null ? laser.Beam : null;
    }

    private void Show(bool dotShown, bool beamShown)
    {
        if (dotShown || beamShown) EnsureObjects();
        if (dot != null && dot.enabled != dotShown) dot.enabled = dotShown;
        if (beam != null && beam.enabled != beamShown) beam.enabled = beamShown;
    }

    private void PlaceDot(Vector3 position, Camera viewer, AttachmentCatalog catalog)
    {
        if (dot.sharedMaterial != catalog.dotMaterial) dot.sharedMaterial = catalog.dotMaterial;
        Transform t = dot.transform;
        t.position = position;
        if (viewer != null)
        {
            Vector3 toViewer = viewer.transform.position - position;
            float distance = toViewer.magnitude;
            if (distance > 1e-4f) t.rotation = Quaternion.LookRotation(-toViewer / distance, viewer.transform.up);
            t.localScale = Vector3.one * Mathf.Max(0.004f, distance * catalog.dotAngularSize);
        }
    }

    private void PlaceBeam(Vector3 start, Vector3 end, Camera viewer, AttachmentCatalog catalog)
    {
        if (beam.sharedMaterial != catalog.beamMaterial) beam.sharedMaterial = catalog.beamMaterial;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);
        float startWidth = catalog.beamWidth, endWidth = catalog.beamWidth;
        if (viewer != null)
        {
            Vector3 eye = viewer.transform.position;
            startWidth = Mathf.Max(startWidth, Vector3.Distance(eye, start) * minimumAngularWidth);
            endWidth = Mathf.Max(endWidth, Vector3.Distance(eye, end) * minimumAngularWidth);
        }
        beam.startWidth = startWidth;
        beam.endWidth = endWidth;
    }

    private void EnsureObjects()
    {
        if (beam != null && dot != null) return;
        if (beam == null)
        {
            // Enfants du joueur, pour que l'écran de fin les cache avec lui (VictoryStage) ; en
            // coordonnées du monde.
            var go = new GameObject("Faisceau laser");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.numCapVertices = 0;
            beam.textureMode = LineTextureMode.Stretch;
            beam.alignment = LineAlignment.View;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            beam.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(endBrightness, 1f) });
            beam.colorGradient = gradient;
            beam.enabled = false;
        }
        if (dot == null)
        {
            var go = new GameObject("Point laser");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            dot = go.AddComponent<MeshRenderer>();
            dot.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dot.receiveShadows = false;
            dot.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            dot.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            dot.enabled = false;
        }
    }

    /// <summary>Un carré d'un mètre, centré, tourné vers -Z (la caméra), ses UV de 0 à 1.</summary>
    private static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "Point laser" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };   // le shader les lit
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateBounds();
        return quad;
    }
}
