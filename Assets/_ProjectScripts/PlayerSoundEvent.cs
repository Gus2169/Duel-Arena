/// <summary>
/// Catalogue des sons de contexte du joueur (mouvement, postures, ramper...) — volontairement
/// distinct des sons de gameplay à fort volume (tirs, explosions), qui alimenteront plus tard
/// un canal séparé pour le mode "arène dans le noir" (Phase 6). Ajouter un nouveau type de son
/// de contexte se fait ici + une entrée dans l'asset PlayerSoundBank, sans toucher au code de
/// déclenchement dans PlayerLocomotion/WeaponController.
/// </summary>
public enum PlayerSoundEvent
{
    FootstepWalk,
    FootstepRun,
    FootstepCrouch,
    Crawl,
    LeanStart,
    LeanEnd,
    CrouchDown,
    CrouchUp,
    ProneDown,
    ProneUp,

    // Réservé : quand le système d'armes aura des munitions (Phase 3), WeaponController pourra
    // émettre ReloadStart/ReloadEnd à travers ce même enum et le même PlayerSoundBank.
}
