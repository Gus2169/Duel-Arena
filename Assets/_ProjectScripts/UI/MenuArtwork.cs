using UnityEngine;

/// <summary>
/// Les images du menu (2026-10-17, d'après les maquettes de l'utilisateur), cuites dans les vraies scènes par
/// Duel Arena > Menu > Capturer les images du menu (MenuArtworkCapture) : le fond de l'écran titre (l'arène
/// vue de la tribune), la vue d'ensemble de l'arène et le stand de tir. On relance la capture quand une scène
/// change ; on ne retouche pas les images à la main.
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

    [Tooltip("Le stand de tir : la ligne de tir et ses mannequins.")]
    public Texture2D rangeImage;
}
