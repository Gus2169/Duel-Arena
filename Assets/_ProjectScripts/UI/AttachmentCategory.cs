/// <summary>Les 4 rubriques des accessoires (2026-10-16 : armurerie, WeaponAttachments, AttachmentMount,
/// WeaponAttachment). Les choix se codent par rubrique dans WeaponAttachments, pas par cette valeur ;
/// elle sert seulement d'index aux appels GameSettings.GetAttachment/SetAttachment.</summary>
public enum AttachmentCategory
{
    Grip = 0,
    Sight = 1,
    Laser = 2,
    Magazine = 3,
}
