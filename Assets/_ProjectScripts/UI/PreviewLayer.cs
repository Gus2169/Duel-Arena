using UnityEngine;

/// <summary>Le layer `Preview` (9) : les modèles tournants du menu (personnage, arme), isolés de
/// tout le reste par le masque de rendu des caméras (voir PreviewStage, MainMenuController).</summary>
public static class PreviewLayer
{
    public const string Name = "Preview";

    private static int cached = -1;
    public static int Index => cached >= 0 ? cached : (cached = LayerMask.NameToLayer(Name));
}
