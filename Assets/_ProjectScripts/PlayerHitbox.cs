using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surface TOUCHABLE du joueur, volontairement distincte du CharacterController qui gère son
/// déplacement. C'est elle, et elle seule, que le raycast serveur cherche pour décider d'un dégât.
///
/// Depuis le 2026-10-05, elle est découpée en zones — tête, torse, jambes (deux capsules de jambes
/// quand la pose l'exige, allongé) — calculées par BodyLayout à partir de la posture réseau et du
/// décalage de lean. C'est ce qui rend possible le
/// lean façon Rainbow Six (seul le buste se penche, les jambes restent à couvert) et, plus tard,
/// les multiplicateurs de dégâts par zone.
///
/// Pourquoi une surface séparée du collider de mouvement :
/// 1. LE LEAN déplace la surface touchable mais ne doit JAMAIS déplacer le collider de mouvement,
///    sinon on rentre dans les murs en peekant.
/// 2. LE VAULT coupe le CharacterController pendant son arc ; ces colliders-ci ne sont jamais
///    désactivés par le mouvement, donc on reste touchable.
/// 3. LE REWIND replace cette surface dans le passé sans toucher à la simulation : il déplace la
///    racine de ce composant, les zones suivent.
///
/// Les zones sont des TRIGGERS sur le layer "Hitbox" : elles ne participent à aucune collision
/// physique et n'existent que pour les requêtes de raycast. Elles sont créées dans Awake(), avec
/// leur layer et leur statut de trigger forcés : ce sont des invariants, pas des réglages, et un
/// clic d'inspecteur suffirait à les casser en silence.
///
/// 🚨 Ne JAMAIS dériver cette surface des os animés : un Animator n'est pas déterministe entre
/// machines. L'animation AFFICHE le corps, ce composant le CALCULE depuis les mêmes scalaires
/// réseautés ; aucun des deux ne lit l'autre.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHitbox : MonoBehaviour
{
    [Header("Géométrie par posture (espace du joueur, mesurée sur le robot)")]
    [SerializeField] private StanceBody standing = BodyLayout.DefaultStanding;
    [SerializeField] private StanceBody crouching = BodyLayout.DefaultCrouching;
    [SerializeField] private StanceBody prone = BodyLayout.DefaultProne;

    private SphereCollider head;
    private CapsuleCollider torso;
    private CapsuleCollider legs;
    private CapsuleCollider secondLeg;
    private bool secondLegInUse;
    private bool queryable = true;
    private readonly List<Collider> colliders = new List<Collider>(4);

    public IReadOnlyList<Collider> Colliders { get { EnsureZones(); return colliders; } }

    /// <summary>Collider du torse : le centre de masse visuel, utilisé par les diagnostics.</summary>
    public Collider TorsoCollider { get { EnsureZones(); return torso; } }

    public StanceBody Body(PlayerLocomotion.Stance stance) => stance switch
    {
        PlayerLocomotion.Stance.Crouching => crouching,
        PlayerLocomotion.Stance.Prone => prone,
        _ => standing,
    };

    private void Awake()
    {
        EnsureZones();
    }

    private void EnsureZones()
    {
        if (head != null) return;

        int hitboxLayer = LayerMask.NameToLayer("Hitbox");
        if (hitboxLayer < 0)
        {
            Debug.LogError("PlayerHitbox : le layer \"Hitbox\" n'existe pas. Le tir serveur ne touchera jamais ce joueur.", this);
        }

        head = CreateZone<SphereCollider>(HitboxZoneType.Head, "Head", hitboxLayer);
        torso = CreateZone<CapsuleCollider>(HitboxZoneType.Torso, "Torso", hitboxLayer);
        legs = CreateZone<CapsuleCollider>(HitboxZoneType.Legs, "Legs", hitboxLayer);
        secondLeg = CreateZone<CapsuleCollider>(HitboxZoneType.Legs, "Legs2", hitboxLayer);
        secondLeg.enabled = false;

        colliders.Clear();
        colliders.Add(head);
        colliders.Add(torso);
        colliders.Add(legs);
        colliders.Add(secondLeg);

        // Le GameObject racine reste sur le layer Hitbox lui aussi : il ne porte plus de collider,
        // mais un objet de surface touchable sur un autre layer serait un piège pour la suite.
        if (hitboxLayer >= 0) gameObject.layer = hitboxLayer;
    }

    private T CreateZone<T>(HitboxZoneType zone, string zoneName, int layer) where T : Collider
    {
        var go = new GameObject(zoneName);
        go.transform.SetParent(transform, false);
        if (layer >= 0) go.layer = layer;

        T collider = go.AddComponent<T>();
        collider.isTrigger = true;
        go.AddComponent<PlayerHitboxZone>().Init(zone);
        return collider;
    }

    /// <summary>
    /// Met la surface touchable en accord avec la posture, l'œil et le lean. Appelée à chaque frame
    /// par PlayerLocomotion sur TOUTES les instances, et par le rewind avec une pose passée.
    /// </summary>
    public void Apply(PlayerLocomotion.Stance stance, Vector3 eye, float lateralLeanOffset)
    {
        EnsureZones();
        BodyLayout.Compute(Body(stance), eye, lateralLeanOffset,
                           out ZoneShape headShape, out ZoneShape torsoShape,
                           out ZoneShape legsShape, out ZoneShape secondLegShape);

        head.transform.localPosition = headShape.a;
        head.transform.localRotation = Quaternion.identity;
        head.center = Vector3.zero;
        head.radius = headShape.radius;

        PlaceCapsule(torso, torsoShape);
        PlaceCapsule(legs, legsShape);

        // La seconde jambe n'existe que dans les postures qui en déclarent une (rayon > 0).
        secondLegInUse = secondLegShape.radius > 0f;
        if (secondLegInUse) PlaceCapsule(secondLeg, secondLegShape);
        secondLeg.enabled = secondLegInUse && queryable;
    }

    /// <summary>Une capsule de Unity ne s'oriente que selon un axe local : on oriente donc son
    /// transform de A vers B, et la capsule suit son axe Y.</summary>
    private static void PlaceCapsule(CapsuleCollider capsule, ZoneShape shape)
    {
        Vector3 axis = shape.b - shape.a;
        float length = axis.magnitude;

        capsule.transform.localPosition = (shape.a + shape.b) * 0.5f;
        capsule.transform.localRotation = length > 1e-5f
            ? Quaternion.FromToRotation(Vector3.up, axis / length)
            : Quaternion.identity;

        capsule.direction = 1;
        capsule.center = Vector3.zero;
        capsule.radius = shape.radius;
        capsule.height = length + 2f * shape.radius;
    }

    /// <summary>Active ou coupe toutes les zones pour les requêtes. Sert au tireur, qui retire sa
    /// propre surface le temps de son tir : on ne se tire pas dessus.</summary>
    public void SetQueryable(bool value)
    {
        EnsureZones();
        queryable = value;
        head.enabled = value;
        torso.enabled = value;
        legs.enabled = value;
        secondLeg.enabled = value && secondLegInUse;
    }

    public bool IsQueryable
    {
        get
        {
            EnsureZones();
            return queryable;
        }
    }

    /// <summary>Décalage latéral maximal d'un côté (+1 droite, -1 gauche), en valeur absolue.</summary>
    public float MaxLateralOffset(PlayerLocomotion.Stance stance, Vector3 eye, float maxAngleDegrees, int side)
        => BodyLayout.MaxLateralOffset(Body(stance), eye, maxAngleDegrees, side);

    public Quaternion LeanRotation(PlayerLocomotion.Stance stance, Vector3 eye, float lateralLeanOffset)
        => BodyLayout.LeanRotation(Body(stance), eye, lateralLeanOffset);

    public Vector3 LeanedEye(PlayerLocomotion.Stance stance, Vector3 eye, float lateralLeanOffset)
        => BodyLayout.LeanedEye(Body(stance), eye, lateralLeanOffset);

    public bool LeanSwingsSideways(PlayerLocomotion.Stance stance) => Body(stance).leanSwingsSideways;

#if UNITY_EDITOR
    /// <summary>Les zones n'existent qu'en jeu : dessine celles de la posture debout, sans lean,
    /// pour pouvoir les comparer au modèle dans la vue Scène.</summary>
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 eye = new Vector3(0f, 1.65f, 0f);
        BodyLayout.Compute(standing, eye, 0f, out ZoneShape h, out ZoneShape t, out ZoneShape l, out ZoneShape l2);
        Gizmos.DrawWireSphere(h.a, h.radius);
        DrawCapsule(t);
        DrawCapsule(l);
        if (l2.radius > 0f) DrawCapsule(l2);
    }

    private static void DrawCapsule(ZoneShape s)
    {
        Gizmos.DrawWireSphere(s.a, s.radius);
        Gizmos.DrawWireSphere(s.b, s.radius);
        Gizmos.DrawLine(s.a + Vector3.right * s.radius, s.b + Vector3.right * s.radius);
        Gizmos.DrawLine(s.a - Vector3.right * s.radius, s.b - Vector3.right * s.radius);
    }
#endif
}
