using UnityEngine;

/// <summary>
/// Les sons du jeu qui n'appartiennent ni à une arme (WeaponData) ni aux pas et postures du robot
/// (PlayerSoundBank) ni à la tribune (SpectatorSounds) : les retours de touche, la mort, le déroulé
/// de la manche, l'ambiance, la musique et l'interface (2026-10-13). Un seul asset, référencé par
/// ceux qui les jouent.
///
/// Ce qui est une information de jeu (la mort, entendue de tous) se joue dans le monde ; ce qui ne
/// regarde qu'un joueur (sa touche, ses dégâts, le décompte, l'interface) se joue à plat.
/// </summary>
[CreateAssetMenu(fileName = "GameSounds", menuName = "Duel Arena/Game Sounds")]
public class GameSounds : ScriptableObject
{
    [Header("Retours de touche (le tireur seul, GDD § 6)")]
    [Tooltip("Une touche confirmée par le serveur.")]
    public SoundSet hit = new SoundSet();
    [Tooltip("Une touche à la tête : un son différent (GDD § 6).")]
    public SoundSet headshot = new SoundSet();
    [Tooltip("Le coup qui tue.")]
    public SoundSet kill = new SoundSet();

    [Header("Dégâts reçus (le joueur touché seul, GDD § 6)")]
    [Tooltip("L'impact sur son propre châssis.")]
    public SoundSet damage = new SoundSet();
    [Tooltip("Les parasites qui l'accompagnent.")]
    public SoundSet damageStatic = new SoundSet();

    [Header("Mort (entendue de tous, à l'épave)")]
    [Tooltip("Le son signature (GDD § 6) : le robot s'éteint.")]
    public SoundSet deathShutdown = new SoundSet();
    [Tooltip("Joué avec lui : le court-circuit.")]
    public SoundSet deathCrackle = new SoundSet();
    [Tooltip("Distance (m) jusqu'à laquelle la mort s'entend à plein volume.")]
    public float deathMinDistance = 6f;
    [Tooltip("Les pièces de l'épave qui heurtent le sol.")]
    public SoundSet corpseImpact = new SoundSet();

    [Header("Déroulé de la manche (à plat)")]
    public SoundSet countdownBeep = new SoundSet();
    [Tooltip("La manche commence.")]
    public SoundSet roundStart = new SoundSet();
    [Tooltip("Quelqu'un est tombé : la manche est finie.")]
    public SoundSet roundOver = new SoundSet();
    [Tooltip("Joué en plus chez le gagnant de la manche.")]
    public SoundSet roundWon = new SoundSet();
    [Tooltip("La reconstruction des châssis, dans le noir entre deux manches (GDD § 5 et § 8). Chaque étape part `delay` secondes après la tombée du noir.")]
    public TimedSound[] reconstruction = System.Array.Empty<TimedSound>();

    [Header("Ambiance de l'arène (à plat, en boucle)")]
    public AudioClip ambienceLoop;
    [Range(0f, 1f)] public float ambienceVolume = 0.25f;

    [Header("Musique (GDD § 8 : menus, entre les manches, écran de victoire ; jamais pendant la manche)")]
    public MusicTrack menuMusic = new MusicTrack();
    public MusicTrack preparationMusic = new MusicTrack();
    [Tooltip("L'écran de victoire : un morceau au hasard (le même chez tous, tiré sur l'instant de fin du match).")]
    public MusicTrack[] victoryMusic = System.Array.Empty<MusicTrack>();

    [Header("Interface (à plat)")]
    public SoundSet uiHover = new SoundSet();
    public SoundSet uiClick = new SoundSet();
    public SoundSet uiConfirm = new SoundSet();
    public SoundSet uiBack = new SoundSet();
    [Tooltip("Une arme choisie (préparation, armurerie).")]
    public SoundSet uiSelect = new SoundSet();
}

/// <summary>Un son, `delay` secondes après le début d'une séquence.</summary>
[System.Serializable]
public struct TimedSound
{
    public SoundSet sound;
    public float delay;
}

/// <summary>Un morceau de musique : où il commence, à quel volume, s'il boucle.</summary>
[System.Serializable]
public class MusicTrack
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.6f;
    [Tooltip("Où commencer dans le morceau (s) : son attaque, quand il ne dure que quelques secondes à l'écran.")]
    public float startAt;
    public bool loop = true;
}
