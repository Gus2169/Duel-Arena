using UnityEngine;

/// <summary>
/// Surface TOUCHABLE du joueur, volontairement distincte du CharacterController qui gère son
/// déplacement. C'est elle, et elle seule, que le raycast serveur cherche pour décider d'un dégât.
///
/// Pourquoi séparer les deux :
///
/// 1. LE LEAN. Se pencher déplace la caméra latéralement mais ne doit JAMAIS déplacer le collider
///    de mouvement — sinon on rentre dans les murs en peekant. Tant que le hitbox ÉTAIT le
///    CharacterController, un joueur penché exposait visuellement son flanc sans que rien de
///    touchable ne bouge avec lui : il était partiellement intouchable. C'est le trou que ce
///    composant ferme.
/// 2. LE VAULT. Le vault coupe le CharacterController (controller.enabled = false) pendant son
///    arc. Tant que le hitbox était ce collider, vaulter revenait à disparaître du monde physique
///    pendant 0,45 s. Ce collider-ci n'est jamais désactivé par le mouvement, donc le vault
///    redevient une mécanique ordinaire.
/// 3. LE REWIND. La compensation de latence a besoin de replacer une surface touchable dans le
///    passé sans toucher à la simulation en cours. Un collider dédié se manipule librement ;
///    bouger le CharacterController casserait le mouvement.
///
/// Le collider est un TRIGGER, sur le layer "Hitbox" : il ne participe donc à aucune résolution
/// de collision physique (ni contre les murs, ni contre l'autre joueur) et n'existe que pour les
/// requêtes de raycast. C'est ce qui garantit qu'ajouter cette surface ne change strictement rien
/// au déplacement.
///
/// Dimensions et position sont pilotées par PlayerLocomotion (voir ApplyHitbox) : fonction pure de
/// la posture réseau + du décalage de lean, donc identique sur toutes les machines pour une même
/// posture — pas d'interpolation locale qui ferait diverger la surface touchable d'un écran à
/// l'autre.
///
/// 🚨 Ne JAMAIS dériver cette surface des os animés : un Animator n'est pas déterministe entre
/// machines. L'animation AFFICHE le corps, ce composant le CALCULE depuis les mêmes scalaires
/// réseautés ; aucun des deux ne lit l'autre.
/// </summary>
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerHitbox : MonoBehaviour
{
    private CapsuleCollider capsule;

    /// <summary>Le collider touchable, exposé pour que le tir serveur puisse l'ignorer
    /// temporairement (on ne se tire pas dessus).</summary>
    public Collider Collider
    {
        get
        {
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            return capsule;
        }
    }

    private void Awake()
    {
        capsule = GetComponent<CapsuleCollider>();

        // Forcés dans le code plutôt que laissés à l'inspecteur : ce sont des invariants du
        // système, pas des réglages. Un hitbox non-trigger repousserait physiquement les joueurs,
        // un hitbox sur le mauvais layer serait invisible au tir serveur ou bloquerait les balles
        // comme un mur. Les deux se règlent silencieusement mal en un clic.
        capsule.isTrigger = true;
        capsule.direction = 1; // axe Y

        int hitboxLayer = LayerMask.NameToLayer("Hitbox");
        if (hitboxLayer < 0)
        {
            Debug.LogError("PlayerHitbox : le layer \"Hitbox\" n'existe pas. Le tir serveur ne touchera jamais ce joueur.", this);
            return;
        }
        gameObject.layer = hitboxLayer;
    }

    /// <summary>
    /// Applique la géométrie de la surface touchable. Appelée à chaque frame par PlayerLocomotion
    /// sur TOUTES les instances : la posture vient d'une NetworkVariable et le lean du serveur
    /// (ou de la prédiction locale pour le propriétaire), donc la surface reste cohérente partout.
    /// </summary>
    /// <param name="height">Hauteur de la capsule, depuis le profil de posture.</param>
    /// <param name="radius">Rayon de la capsule, depuis le profil de posture.</param>
    /// <param name="lateralOffset">Décalage latéral du lean, en unités locales du joueur.</param>
    public void Apply(float height, float radius, float lateralOffset)
    {
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();

        capsule.height = height;
        capsule.radius = radius;

        // Le centre du collider suit le lean. On décale le COLLIDER et non le transform pour que
        // la position du GameObject reste celle du joueur — c'est ce qui permet au rewind de ne
        // sauvegarder et restaurer qu'une position et une rotation.
        capsule.center = new Vector3(lateralOffset, height / 2f, 0f);
    }
}
