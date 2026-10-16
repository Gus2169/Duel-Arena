using UnityEngine;

/// <summary>
/// Les sons de la tribune (2026-10-13) : coups de poing, étourdissement, blocs qui se fendent, cailloux
/// et matériel. Data-driven : remplacer un son, c'est changer un clip dans l'asset.
///
/// Ce sont des sons de gameplay (catégorie B) : décidés par le serveur, entendus par tous, en 3D. Le
/// GDD le veut (§ 8) : « les spectateurs font du bruit, et c'est voulu », et un caillou qui tombe est un
/// leurre sonore qui peut tromper un duelliste.
/// </summary>
[CreateAssetMenu(fileName = "SpectatorSounds", menuName = "Duel Arena/Spectator Sounds")]
public class SpectatorSounds : ScriptableObject
{
    [System.Serializable]
    public class Sound
    {
        public AudioClip[] clips = System.Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 0.8f;
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

        public void PlayOn(AudioSource source)
        {
            if (source == null || clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.PlayOneShot(clip, volume);
        }
    }

    [Header("Bagarre")]
    [Tooltip("Le poing qui part (dans le vide ou non).")]
    public Sound punchSwing = new Sound();
    [Tooltip("Le poing qui touche un autre robot.")]
    public Sound punchHit = new Sound();
    [Tooltip("Un robot étourdi : grésillement électrique.")]
    public Sound stunned = new Sound();

    [Header("Blocs fissurés de la tribune")]
    [Tooltip("Un coup sur un bloc fissuré.")]
    public Sound blockHit = new Sound();
    [Tooltip("Le bloc cède : un caillou tombe.")]
    public Sound blockBreak = new Sound();

    [Header("Objets")]
    [Tooltip("Ramasser un caillou ou du matériel.")]
    public Sound pickUp = new Sound();
    [Tooltip("Le geste du lancer.")]
    public Sound throwSwing = new Sound();
    [Tooltip("Un caillou qui se brise (au sol, sur un mur, sur un robot) : le leurre sonore du GDD.")]
    public Sound rockImpact = new Sound();
    [Tooltip("Du matériel qui touche le sol ou rebondit.")]
    public Sound itemLand = new Sound();
    [Tooltip("Un duelliste prend une arme lancée par un spectateur.")]
    public Sound powerUpTaken = new Sound();
}
