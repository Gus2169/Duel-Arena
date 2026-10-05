using UnityEngine;

/// <summary>Étiquette posée sur chaque collider touchable : dit au serveur QUELLE zone un tir a
/// atteinte. Créée par PlayerHitbox, jamais à la main.</summary>
[DisallowMultipleComponent]
public class PlayerHitboxZone : MonoBehaviour
{
    public HitboxZoneType Zone { get; private set; }

    public void Init(HitboxZoneType zone) => Zone = zone;
}
