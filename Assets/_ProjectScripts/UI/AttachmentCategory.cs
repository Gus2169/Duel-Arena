/// <summary>Les rubriques des accessoires (2026-10-16 : armurerie, WeaponAttachments, AttachmentMount,
/// WeaponAttachment ; la crosse et le canon depuis le 2026-10-29). Les choix se codent par rubrique dans
/// WeaponAttachments, pas par cette valeur ; elle sert d'index aux appels GameSettings.GetAttachment/SetAttachment.
/// 🚨 Ajouter les nouvelles à la fin.</summary>
public enum AttachmentCategory
{
    Grip = 0,
    Sight = 1,
    Laser = 2,
    Magazine = 3,
    /// <summary>La crosse (2026-10-29) : légère ou lourde, à la place de celle de l'arme.</summary>
    Stock = 4,
    /// <summary>Le canon (2026-10-29, un seul emplacement comme Rainbow Six) : compensateur, frein de bouche,
    /// canon long ou court ; sur une arme laser, leurs émetteurs à elles.</summary>
    Barrel = 5,
}
