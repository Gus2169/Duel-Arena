using UnityEngine;

/// <summary>
/// Le nom d'une couleur de robot, lu sur sa teinte (2026-10-17, pour les tuiles de Personnalisation) : la
/// palette vit dans le préfab joueur (PlayerPaint) et peut changer ; une liste de noms tenue à côté
/// divergerait en silence. Fonction pure, testée contre la palette du préfab.
/// </summary>
public static class PaletteNames
{
    public static string NameOf(Color32 color)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        if (value < 0.2f) return "Noir";
        if (saturation < 0.18f) return value > 0.8f ? "Blanc" : "Gris";
        float degrees = hue * 360f;
        if (degrees < 15f || degrees >= 345f) return "Rouge";
        if (degrees < 45f) return "Orange";
        if (degrees < 70f) return "Jaune";
        if (degrees < 160f) return "Vert";
        if (degrees < 200f) return "Cyan";
        if (degrees < 250f) return "Bleu";
        if (degrees < 290f) return "Violet";
        return "Magenta";
    }
}
