/// <summary>
/// Catalogue des sons de contexte du joueur (mouvement, postures, ramper...) — volontairement
/// distinct des sons de gameplay à fort volume (tirs, explosions), qui alimenteront plus tard
/// un canal séparé pour le mode "arène dans le noir". Ajouter un nouveau type de son
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

    // Le son du rechargement ne passe PAS par ici (2026-10-08) : il dépend de l'arme, comme le son
    // de tir, donc il vit dans WeaponData.reloadSound.
}
