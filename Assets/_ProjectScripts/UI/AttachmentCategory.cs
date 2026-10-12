/// <summary>Les 4 rubriques de l'armurerie avancée (WeaponCustomizationController). L'ordre sert
/// d'index dans GameSettings.GetAttachment/SetAttachment — ne pas réordonner sans migrer les
/// préférences déjà enregistrées (elles redeviendraient 0 = aucun accessoire, sans dégât).</summary>
public enum AttachmentCategory
{
    Grip = 0,
    Sight = 1,
    Laser = 2,
    Magazine = 3,
}
