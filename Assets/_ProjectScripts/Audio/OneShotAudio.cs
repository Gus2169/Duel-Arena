using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les sons ponctuels qui n'appartiennent à aucun robot : un impact de balle sur un mur, une balle qui
/// siffle, l'épave qui tombe, un clic d'interface (2026-10-13). Des voix en réserve, sous un objet qui
/// survit aux scènes ; la plus ancienne est reprise quand toutes jouent.
///
/// Catégorie C : chaque machine joue ce qu'elle voit, rien ne relit ces sons.
/// </summary>
public static class OneShotAudio
{
    private const int WorldVoiceCount = 24;
    private const int FlatVoiceCount = 8;

    private static GameObject root;
    private static readonly List<AudioSource> worldVoices = new List<AudioSource>();
    private static readonly List<AudioSource> flatVoices = new List<AudioSource>();
    private static int nextWorld;
    private static int nextFlat;

    // Le rechargement de domaine est coupé à l'entrée en Play Mode : l'objet d'une session précédente
    // est détruit, ses voix aussi.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        root = null;
        worldVoices.Clear();
        flatVoices.Clear();
        nextWorld = 0;
        nextFlat = 0;
        listener = null;
#if UNITY_EDITOR
        DebugJournal.Clear();
#endif
    }

#if UNITY_EDITOR
    /// <summary>Diagnostic (Editor seulement) : les derniers sons ponctuels joués sur cette machine
    /// (instant, genre, clip), lus par `eval` sur chaque instance du banc : le son ne s'écoute pas
    /// ici. Les plus anciens sortent au-delà de 4096 (une partie entière).</summary>
    public static readonly List<string> DebugJournal = new List<string>();

    public static void DebugNote(string kind, AudioClip clip)
    {
        if (clip == null) return;
        if (DebugJournal.Count >= 4096) DebugJournal.RemoveAt(0);
        DebugJournal.Add($"{Time.time:F2} {kind} {clip.name}");
    }
#endif

    private static AudioListener listener;

    /// <summary>
    /// L'oreille de ce joueur : l'écouteur actif, null s'il n'y en a pas. L'arène en compte plusieurs (sa
    /// caméra de secours, la caméra de chaque robot), tous éteints sauf un ; FindAnyObjectByType rendait
    /// la caméra de secours, éteinte, et aucune balle ne sifflait (vu au banc le 2026-10-13).
    /// </summary>
    public static AudioListener ActiveListener
    {
        get
        {
            if (listener != null && listener.isActiveAndEnabled) return listener;
            listener = null;
            foreach (AudioListener candidate in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            {
                if (!candidate.isActiveAndEnabled) continue;
                listener = candidate;
                break;
            }
            return listener;
        }
    }

    /// <summary>Joue `clip` dans le monde. Atténuation logarithmique : plein volume jusqu'à
    /// `minDistance`, puis moitié à chaque doublement de la distance, jusqu'à `maxDistance`.</summary>
    public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume, float pitch, float minDistance, float maxDistance)
    {
        if (clip == null || volume <= 0f || !EnsureRoot()) return null;
        AudioSource voice = Take(worldVoices, ref nextWorld);
        voice.transform.position = position;
        voice.spatialBlend = 1f;
        voice.minDistance = Mathf.Max(0.1f, minDistance);
        voice.maxDistance = Mathf.Max(voice.minDistance + 0.1f, maxDistance);
        Start(voice, clip, volume, pitch);
        return voice;
    }

    /// <summary>Joue `clip` à plat, sans position : ce que seul ce joueur entend.</summary>
    public static void Play2D(AudioClip clip, float volume, float pitch = 1f)
    {
        if (clip == null || volume <= 0f || !EnsureRoot()) return;
        AudioSource voice = Take(flatVoices, ref nextFlat);
        voice.spatialBlend = 0f;
        Start(voice, clip, volume, pitch);
    }

    private static void Start(AudioSource voice, AudioClip clip, float volume, float pitch)
    {
        voice.Stop();
        voice.clip = clip;
        voice.volume = volume;
        voice.pitch = pitch;
        voice.Play();
#if UNITY_EDITOR
        DebugNote(voice.spatialBlend > 0f ? "monde" : "plat", clip);
#endif
    }

    /// <summary>Une voix libre, ou la plus ancienne si toutes jouent.</summary>
    private static AudioSource Take(List<AudioSource> voices, ref int next)
    {
        for (int i = 0; i < voices.Count; i++)
        {
            int index = (next + i) % voices.Count;
            if (voices[index] != null && !voices[index].isPlaying)
            {
                next = (index + 1) % voices.Count;
                return voices[index];
            }
        }
        AudioSource oldest = voices[next];
        next = (next + 1) % voices.Count;
        return oldest;
    }

    private static bool EnsureRoot()
    {
        if (root != null) return true;
        if (!Application.isPlaying) return false;
        root = new GameObject("Sons ponctuels") { hideFlags = HideFlags.DontSave };
        Object.DontDestroyOnLoad(root);
        worldVoices.Clear();
        flatVoices.Clear();
        for (int i = 0; i < WorldVoiceCount; i++) worldVoices.Add(CreateVoice($"Monde {i + 1}"));
        for (int i = 0; i < FlatVoiceCount; i++) flatVoices.Add(CreateVoice($"A plat {i + 1}"));
        return true;
    }

    private static AudioSource CreateVoice(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        AudioSource voice = go.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Logarithmic;
        return voice;
    }
}
