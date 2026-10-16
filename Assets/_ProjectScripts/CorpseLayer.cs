using UnityEngine;

/// <summary>
/// Le layer `Corpse` (10) : l'épave d'un robot tué et sa capsule de mouvement (2026-10-12).
///
/// Il ne touche QUE le décor (Default) : ni les joueurs (le survivant traverse l'épave et la place
/// où son adversaire est tombé), ni lui-même (les pièces d'une épave ne se bousculent pas entre
/// elles, et elles ignorent la capsule restée invisible du joueur mort). Il n'est dans aucun masque
/// de la simulation : ni worldMask (les balles le traversent), ni obstacleMask (PlayerLocomotion l'en
/// retire). Une épave n'a pas la même trajectoire sur deux machines : rien de jouable ne doit en
/// dépendre. La matrice des collisions est vérifiée par un test (CorpseTests).
/// </summary>
public static class CorpseLayer
{
    public const string Name = "Corpse";

    private static int cached = -1;
    public static int Index => cached >= 0 ? cached : (cached = LayerMask.NameToLayer(Name));

    /// <summary>Masque de ce seul layer (0 s'il n'existe pas).</summary>
    public static int Mask => Index >= 0 ? 1 << Index : 0;
}
