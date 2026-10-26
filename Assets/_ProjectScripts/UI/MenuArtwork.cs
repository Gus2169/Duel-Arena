using System;
using UnityEngine;

/// <summary>Ce qu'un repère de la carte du salon désigne.</summary>
public enum MapMarkerKind
{
    /// <summary>Un point d'apparition des duellistes (A, B).</summary>
    Spawn,
    /// <summary>Un bloc fissuré de la tribune, d'où les spectateurs tirent leurs cailloux.</summary>
    RockSource,
}

/// <summary>Un repère de la carte : sa position au sol (x, z du monde) et son libellé.</summary>
[Serializable]
public struct MapMarker
{
    public MapMarkerKind kind;
    public Vector2 position;
    public string label;
}

/// <summary>
/// Les images du menu (2026-10-17, d'après les maquettes de l'utilisateur), cuites dans les vraies scènes par
/// Duel Arena > Menu > Capturer les images du menu (MenuArtworkCapture) : le fond de l'écran titre (l'arène
/// vue de la tribune), la vue d'ensemble de l'arène, sa carte vue de dessus et ses éléments clés (points
/// d'apparition, tribune, blocs), et le stand de tir. On relance la capture quand une scène change ; on ne
/// retouche pas les images à la main.
///
/// Le menu ne charge jamais l'arène : ses objets réseau de scène et son éclairage n'ont rien à y faire.
/// </summary>
[CreateAssetMenu(menuName = "Duel Arena/Images du menu", fileName = "MenuArtwork")]
public class MenuArtwork : ScriptableObject
{
    [Tooltip("Le nom de l'arène, au salon. Provisoire tant que l'arène de test n'a pas de nom (bible § 13).")]
    public string arenaName = "SALLE DE TEST 01";

    [Tooltip("Le fond de l'écran titre et des autres écrans : l'arène vue de la tribune, avec des robots.")]
    public Texture2D titleBackdrop;

    [Tooltip("L'arène vue d'ensemble : le détail des modes, le fond des cartes.")]
    public Texture2D arenaOverview;

    [Tooltip("La carte : l'arène vue de dessus, sans la tribune au-dessus du terrain.")]
    public Texture2D arenaTopDown;

    [Tooltip("Le rectangle du sol couvert par la carte : x et y = le coin (x, z) minimal du monde, largeur et hauteur en mètres.")]
    public Rect topDownWorldRect = new Rect(-30f, -10f, 60f, 80f);

    [Tooltip("Les éléments clés de la carte : points d'apparition, blocs de la tribune.")]
    public MapMarker[] mapMarkers = Array.Empty<MapMarker>();

    [Tooltip("Le garde-corps de la tribune, vu de dessus (x, z) : le tour de l'arène où se tiennent les spectateurs.")]
    public Vector2[] walkwayRail = Array.Empty<Vector2>();

    [Tooltip("Le stand de tir : la ligne de tir et ses mannequins.")]
    public Texture2D rangeImage;

    [Tooltip("La Fosse : le parcours chronométré.")]
    public Texture2D courseImage;

    /// <summary>La position d'un point du sol (x, z) sur la carte, de 0 à 1 (0, 0 en haut à gauche). Le
    /// haut de la carte est le +z du monde : la caméra de la capture regarde vers le bas, son haut tourné
    /// vers +z. Fonction pure, testée.</summary>
    public static Vector2 MapUV(Rect worldRect, Vector2 ground)
    {
        if (worldRect.width <= 0f || worldRect.height <= 0f) return new Vector2(0.5f, 0.5f);
        return new Vector2((ground.x - worldRect.xMin) / worldRect.width,
                           1f - (ground.y - worldRect.yMin) / worldRect.height);
    }
}
