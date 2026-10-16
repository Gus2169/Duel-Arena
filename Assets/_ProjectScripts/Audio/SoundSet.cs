using UnityEngine;

/// <summary>
/// Un son du jeu : quelques variantes (une au hasard à chaque fois, contre l'effet « disque rayé »),
/// un volume et une plage de hauteur. Data-driven, comme le reste : changer un son, c'est changer
/// l'asset qui le porte (2026-10-13).
/// </summary>
[System.Serializable]
public class SoundSet
{
    public AudioClip[] clips = System.Array.Empty<AudioClip>();
    [Range(0f, 1f)] public float volume = 0.8f;
    public Vector2 pitchRange = new Vector2(0.97f, 1.03f);

    public bool IsEmpty => clips == null || clips.Length == 0;

    public AudioClip Pick() => IsEmpty ? null : clips[Random.Range(0, clips.Length)];

    public float PickPitch() => Random.Range(pitchRange.x, pitchRange.y);

    /// <summary>Joué sur une source existante (celle du robot, de l'arme).</summary>
    public void PlayOn(AudioSource source, float volumeScale = 1f)
    {
        AudioClip clip = Pick();
        if (source == null || clip == null) return;
        source.pitch = PickPitch();
        source.PlayOneShot(clip, volume * volumeScale);
    }

    /// <summary>Joué à plat, sans position : ce que seul ce joueur entend (interface, retours).</summary>
    public void Play2D(float volumeScale = 1f) => OneShotAudio.Play2D(Pick(), volume * volumeScale, PickPitch());

    /// <summary>Joué dans le monde, à `position` (impacts, sifflements, mort).</summary>
    public void PlayAt(Vector3 position, float minDistance, float maxDistance, float volumeScale = 1f)
        => OneShotAudio.PlayAt(Pick(), position, volume * volumeScale, PickPitch(), minDistance, maxDistance);
}
