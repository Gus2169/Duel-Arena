using UnityEngine;

/// <summary>Posé sur chaque pièce d'une épave (RobotCorpse.EnableImpactSounds) : relaie ses chocs à
/// l'épave, qui décide du bruit (2026-10-13). Catégorie C.</summary>
[DisallowMultipleComponent]
public class CorpsePieceSound : MonoBehaviour
{
    public RobotCorpse Corpse { get; set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (Corpse != null) Corpse.OnPieceCollision(collision);
    }
}
