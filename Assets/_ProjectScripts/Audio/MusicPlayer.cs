using UnityEngine;

/// <summary>
/// La musique d'une scène (2026-10-13, GDD § 8) : un morceau à la fois, en fondu enchaîné, au volume
/// des Options. Au menu, il lance la musique du menu tout seul ; dans l'arène, MatchAudio lui dit
/// quoi jouer (préparation, écran de victoire) et quand se taire (la manche).
///
/// Catégorie C, à plat : chaque joueur entend sa musique, rien ne la relit.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    [SerializeField] private GameSounds sounds;
    [Tooltip("Lance la musique du menu au démarrage (scène du menu principal).")]
    [SerializeField] private bool playMenuMusicOnStart;
    [Tooltip("Durée (s) d'un fondu, à l'entrée comme à la sortie d'un morceau.")]
    [SerializeField] private float fadeTime = 1.2f;

    private readonly AudioSource[] decks = new AudioSource[2];
    private readonly MusicTrack[] deckTracks = new MusicTrack[2];
    private readonly float[] deckLevel = new float[2];
    private readonly float[] deckTarget = new float[2];
    private readonly float[] deckFadeTime = new float[2];
    private int active = -1;
    private float musicVolume = 1f;

    /// <summary>Le morceau qui joue (ou qui entre), null si rien.</summary>
    public MusicTrack Current => active >= 0 && deckTarget[active] > 0f ? deckTracks[active] : null;

    private void Awake()
    {
        for (int i = 0; i < decks.Length; i++)
        {
            AudioSource deck = gameObject.AddComponent<AudioSource>();
            deck.playOnAwake = false;
            deck.spatialBlend = 0f;
            deck.dopplerLevel = 0f;
            deck.priority = 0; // jamais coupée par manque de voix
            decks[i] = deck;
        }
    }

    private void OnEnable()
    {
        musicVolume = GameSettings.MusicVolume; // lu une fois : PlayerPrefs relit le registre à chaque appel
        GameSettings.AudioChanged += OnAudioChanged;
    }

    private void OnDisable() => GameSettings.AudioChanged -= OnAudioChanged;

    private void OnAudioChanged() => musicVolume = GameSettings.MusicVolume;

    private void Start()
    {
        if (playMenuMusicOnStart && sounds != null) Play(sounds.menuMusic);
    }

    /// <summary>Fait entrer `track` en fondu, et sortir celui d'avant. Sans effet s'il joue déjà.</summary>
    public void Play(MusicTrack track, float fade = -1f)
    {
        if (track == null || track.clip == null)
        {
            Stop(fade);
            return;
        }
        if (Current == track) return;
        float time = fade >= 0f ? fade : fadeTime;
        if (active >= 0) FadeTo(active, 0f, time);

        int next = active < 0 ? 0 : 1 - active;
        AudioSource deck = decks[next];
        deck.Stop();
        deck.clip = track.clip;
        deck.loop = track.loop;
        deckTracks[next] = track;
        deckLevel[next] = 0f;
        FadeTo(next, 1f, time);
        Apply(next);
        deck.Play();
        // Après Play : une musique est lue en streaming, et l'avance ne prend qu'une fois la lecture lancée.
        deck.time = Mathf.Clamp(track.startAt, 0f, Mathf.Max(0f, track.clip.length - 0.1f));
        active = next;
    }

    /// <summary>Éteint la musique en fondu.</summary>
    public void Stop(float fade = -1f)
    {
        float time = fade >= 0f ? fade : fadeTime;
        for (int i = 0; i < decks.Length; i++) FadeTo(i, 0f, time);
    }

    private void FadeTo(int deck, float target, float time)
    {
        deckTarget[deck] = target;
        deckFadeTime[deck] = Mathf.Max(0.01f, time);
    }

    private void Update()
    {
        for (int i = 0; i < decks.Length; i++)
        {
            if (deckTracks[i] == null) continue;
            deckLevel[i] = Mathf.MoveTowards(deckLevel[i], deckTarget[i], Time.unscaledDeltaTime / deckFadeTime[i]);
            Apply(i);
            if (deckLevel[i] <= 0f && deckTarget[i] <= 0f && decks[i].isPlaying) decks[i].Stop();
        }
    }

    private void Apply(int deck)
    {
        MusicTrack track = deckTracks[deck];
        decks[deck].volume = track == null ? 0f : deckLevel[deck] * track.volume * musicVolume;
    }
}
