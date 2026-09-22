using UnityEngine;

/// <summary>
/// Joue les sons de contexte du joueur (pas, lean, changements de posture, ramper...) en 3D
/// spatial — contrairement au son 2D utilisé jusqu'ici pour les footsteps, ces sons doivent
/// être localisables dans l'espace par un adversaire proche, l'info sonore étant primordiale
/// pour le joueur (selon le GDD).
///
/// Volontairement séparé de tout système de son "fort" (tirs, explosions) : ce script ne
/// concerne que les sons de contexte du joueur. Le futur mode "arène dans le noir" écoutera
/// les sons forts via un canal séparé — pas celui-ci — pour rester découplé.
///
/// Réglages de l'AudioSource attendus : Spatial Blend = 1 (3D), Doppler Level = 0 (sinon la
/// rotation caméra fait varier le pitch, indésirable). Min Distance ~1-2 / Max Distance ~20-25
/// pour une arène compacte, à ajuster à la taille réelle de la map.
/// </summary>
[RequireComponent(typeof(PlayerLocomotion))]
public class PlayerSoundEmitter : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private PlayerSoundBank soundBank;

    private PlayerLocomotion locomotion;

    private void Awake()
    {
        locomotion = GetComponent<PlayerLocomotion>();
    }

    private void OnEnable()
    {
        locomotion.OnPlayerSound += HandlePlayerSound;
    }

    private void OnDisable()
    {
        locomotion.OnPlayerSound -= HandlePlayerSound;
    }

    private void HandlePlayerSound(PlayerSoundEvent type, PlayerLocomotion.NoiseLevel noise)
    {
        // Le sneak reste silencieux pour la détection à distance (gameplay) : on respecte ça
        // pour les pas/le ramper. Les sons ponctuels (s'accroupir, pencher...) font un minimum
        // de bruit physique même en essayant d'être discret — à inverser ici si le design
        // final veut au contraire les couper aussi en sneak.
        bool isMovementLoop = type == PlayerSoundEvent.FootstepWalk || type == PlayerSoundEvent.FootstepRun
            || type == PlayerSoundEvent.FootstepCrouch || type == PlayerSoundEvent.Crawl;
        if (isMovementLoop && noise == PlayerLocomotion.NoiseLevel.Silent)
        {
            return;
        }

        if (audioSource == null || soundBank == null) return;
        if (!soundBank.TryGetEntry(type, out var entry)) return;
        if (entry.clips == null || entry.clips.Length == 0) return;

        AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
        audioSource.pitch = Random.Range(entry.pitchRange.x, entry.pitchRange.y);
        audioSource.PlayOneShot(clip, entry.volume);
    }
}
