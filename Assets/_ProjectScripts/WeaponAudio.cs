using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les sons d'une arme, tels que chaque machine les entend (2026-10-13). Catégorie B : le tir et le
/// chargeur sont décidés par le serveur (BroadcastShotClientRpc, MagazineState) ; ce composant ne fait
/// que les jouer, il ne décide rien et rien ne le relit.
///
/// - Le TIR suit le guide des armes automatiques du pack Gamemaster : un premier coup en début de
///   rafale, des coups secs ensuite, et une queue de réverbération lancée avec chaque coup, éteinte en
///   fondu quand le suivant part. Sans ce fondu, les queues s'empilent et la rafale devient boueuse.
/// - La MÉCANIQUE qui suit un coup (culasse, pompe) part fireMechanismDelay après lui.
/// - Le RECHARGEMENT se joue en étapes calées sur sa durée, sur une voix à part : interrompu (sprint,
///   taser, tir au pompe, changement d'arme), il se tait. Avant, un seul clip partait à l'appui et
///   continuait même interrompu : l'adversaire entendait un rechargement qui n'avait plus lieu. Au
///   pompe, chaque cartouche glissée s'entend, puis la pompe quand le chargeur est plein.
/// - L'arme qu'on SORT (changement d'arme, arme ramassée) et la gâchette pressée à vide (le tireur
///   seul) passent par la voix de la mécanique.
///
/// Les voix sont des copies de la source 3D du prefab (portée, atténuation), créées au démarrage : les
/// réglages de portée restent à un seul endroit.
/// </summary>
public class WeaponAudio : MonoBehaviour
{
    [Tooltip("La source 3D réglée dans le prefab : portée et atténuation de toutes les voix de l'arme. Vide = celle de cet objet.")]
    [SerializeField] private AudioSource template;

    [Tooltip("Fondu (s) d'une queue de réverbération quand le coup suivant part. Court : à 7 coups/s, un coup arrive toutes les 0,14 s.")]
    [SerializeField] private float tailFade = 0.06f;

    [Tooltip("Fondu (s) d'un rechargement interrompu.")]
    [SerializeField] private float reloadStopFade = 0.08f;

    /// <summary>Un coup ouvre une rafale s'il suit le précédent de plus d'un intervalle et demi de
    /// l'arme : au-delà, la réverbération du coup précédent est retombée.</summary>
    public const float BurstGapIntervals = 1.5f;

    /// <summary>Ce qu'un changement du chargeur fait entendre. Plusieurs à la fois : une dernière
    /// cartouche glissée termine le rechargement.</summary>
    [System.Flags]
    public enum ReloadSound
    {
        None = 0,
        Started = 1,
        RoundIn = 2,
        Completed = 4,
        Interrupted = 8,
    }

    private const int ShotVoiceCount = 4;

    private AudioSource[] shotVoices;
    private int nextShotVoice;
    private AudioSource[] tailVoices;
    private int activeTail = -1;
    private AudioSource mechanicsVoice; // culasse, pompe, cartouches : jamais coupée
    private AudioSource reloadVoice;    // étapes du rechargement complet : coupée s'il est interrompu
    private AudioSource chargeVoice;    // la montée en charge du tir chargé (le FX-2) : coupée au tir
    private bool chargeHeard;
    private float lastShotTime = float.NegativeInfinity;

    private struct Fade
    {
        public AudioSource source;
        public float from;
        public float startedAt;
        public float duration;
    }

    private struct Pending
    {
        public float at;
        public AudioClip clip;
        public float volume;
        public bool reload;
    }

    private readonly List<Fade> fades = new List<Fade>();
    private readonly List<Pending> pending = new List<Pending>();

    // les tailles et durées du chargeur, accessoires compris (le chargeur grande capacité)
    private WeaponController controller;

    private void Awake()
    {
        controller = GetComponent<WeaponController>();
        if (template == null) template = GetComponent<AudioSource>();
        if (template != null) template.dopplerLevel = 0f; // le roulis de la caméra ne doit pas faire varier le ton

        var holder = new GameObject("Voix de l'arme");
        holder.transform.SetParent(transform, false);
        shotVoices = new AudioSource[ShotVoiceCount];
        for (int i = 0; i < ShotVoiceCount; i++) shotVoices[i] = CreateVoice(holder);
        tailVoices = new[] { CreateVoice(holder), CreateVoice(holder) };
        mechanicsVoice = CreateVoice(holder);
        reloadVoice = CreateVoice(holder);
        chargeVoice = CreateVoice(holder);
    }

    private AudioSource CreateVoice(GameObject holder)
    {
        AudioSource voice = holder.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.dopplerLevel = 0f;
        if (template == null) return voice;
        voice.outputAudioMixerGroup = template.outputAudioMixerGroup;
        voice.spatialBlend = template.spatialBlend;
        voice.rolloffMode = template.rolloffMode;
        voice.minDistance = template.minDistance;
        voice.maxDistance = template.maxDistance;
        voice.spread = template.spread;
        voice.priority = template.priority;
        voice.reverbZoneMix = template.reverbZoneMix;
        if (template.rolloffMode == AudioRolloffMode.Custom)
            voice.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        return voice;
    }

    private void OnDisable()
    {
        pending.Clear();
        // Un fondu interrompu laisserait sa voix à mi-volume pour la suite.
        foreach (Fade f in fades)
        {
            f.source.Stop();
            f.source.volume = 1f;
        }
        fades.Clear();
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = 0; i < pending.Count; i++)
        {
            Pending p = pending[i];
            if (p.at > now) continue;
            (p.reload ? reloadVoice : mechanicsVoice).PlayOneShot(p.clip, p.volume);
            pending.RemoveAt(i--);
        }
        for (int i = 0; i < fades.Count; i++)
        {
            Fade f = fades[i];
            float t = f.duration > 0f ? (now - f.startedAt) / f.duration : 1f;
            if (t < 1f)
            {
                f.source.volume = f.from * (1f - t);
                continue;
            }
            f.source.Stop();
            f.source.volume = 1f;
            fades.RemoveAt(i--);
        }
    }

    // ------------------------------------------------------------------
    // Tir
    // ------------------------------------------------------------------

    /// <summary>Un coup de `data`, vu sur cette machine (le tireur à l'appui, les autres à la
    /// diffusion du serveur).</summary>
    public void PlayShot(WeaponData data)
    {
        if (data == null || shotVoices == null) return;
        float now = Time.time;
        bool burstStart = StartsBurst(now - lastShotTime, data.shotsPerSecond);
        lastShotTime = now;
        float pitch = Random.Range(data.firePitchRange.x, data.firePitchRange.y);

        AudioClip clip = burstStart && data.fireFirstSound != null ? data.fireFirstSound : Pick(data.fireSounds);
        if (clip != null)
        {
            AudioSource voice = shotVoices[nextShotVoice];
            nextShotVoice = (nextShotVoice + 1) % shotVoices.Length;
            Play(voice, clip, pitch, data.fireVolume);
        }

        if (data.fireTailSound != null)
        {
            if (activeTail >= 0) FadeOut(tailVoices[activeTail], tailFade);
            activeTail = (activeTail + 1) % tailVoices.Length;
            Play(tailVoices[activeTail], data.fireTailSound, pitch, data.fireVolume);
        }

        AudioClip mechanism = Pick(data.fireMechanismSounds);
        if (mechanism != null) Schedule(now + data.fireMechanismDelay, mechanism, data.fireMechanismVolume, reload: false);
    }

    /// <summary>
    /// La charge montrée de l'arme en main (0 à 1, WeaponController.DisplayedCharge), à chaque image :
    /// la montée en charge part quand la charge commence, accordée pour finir quand elle est pleine, et
    /// se tait en fondu quand elle retombe (le tir, la gâchette lâchée). Chez tous : l'adversaire
    /// l'entend venir.
    /// </summary>
    public void HearCharge(WeaponData data, float charge)
    {
        bool heard = charge > 0f && data != null && data.chargeSound != null && chargeVoice != null;
        if (heard && !chargeHeard)
        {
            AudioClip clip = data.chargeSound;
            Play(chargeVoice, clip, Mathf.Clamp(clip.length / Mathf.Max(0.05f, data.chargeTime), 0.5f, 2f), data.chargeVolume);
            // Une charge déjà commencée (un joueur qui arrive, une pose qui manquait) reprend où elle en est.
            chargeVoice.time = Mathf.Min(Mathf.Clamp01(charge) * clip.length, Mathf.Max(0f, clip.length - 0.01f));
#if UNITY_EDITOR
            OneShotAudio.DebugNote("charge " + transform.root.name, clip);
#endif
        }
        else if (!heard && chargeHeard && chargeVoice != null && chargeVoice.isPlaying)
        {
            FadeOut(chargeVoice, ChargeStopFade);
        }
        chargeHeard = heard;
    }

    /// <summary>Fondu (s) de la montée en charge quand le tir part ou qu'on lâche sans tirer.</summary>
    public const float ChargeStopFade = 0.05f;

    /// <summary>
    /// Ce coup ouvre-t-il une rafale ? Oui s'il suit le précédent de plus d'un intervalle et demi de
    /// l'arme. Fonction pure, testée.
    /// </summary>
    public static bool StartsBurst(float sinceLastShot, float shotsPerSecond)
        => shotsPerSecond <= 0f || sinceLastShot > BurstGapIntervals / shotsPerSecond;

    // ------------------------------------------------------------------
    // Rechargement
    // ------------------------------------------------------------------

    /// <summary>Fait entendre ce que change le chargeur entre deux états (prédit chez le tireur,
    /// publié par le serveur chez les autres). `data` : l'arme en main AVANT le changement,
    /// `afterData` : celle d'après (la même, sauf changement d'arme, qui la fait sortir).</summary>
    public void Hear(in WeaponController.MagazineState before, in WeaponController.MagazineState after, WeaponData data, WeaponData afterData)
    {
        if (after.equipped != before.equipped && afterData != null) PlayMechanism(afterData.raiseSounds);
        if (data == null) return;
        int capacity = controller != null ? controller.MagazineSizeOf(before.equipped) : data.magazineSize;
        float duration = controller != null ? controller.ReloadDurationOf(before.equipped) : data.reloadDuration;
        // La vidange forcée d'une arme laser surchauffée dure plus longtemps : ses étapes s'y calent.
        if (data.UsesHeat && after.overheated && controller != null) duration = controller.HeatRulesOf(before.equipped).overheatVent;
        ReloadSound sound = ClassifyReload(before, after, capacity, data.reloadPerRound, data.UsesHeat);
        if (sound == ReloadSound.None) return;
#if UNITY_EDITOR
        // Diagnostic à deux instances : ce que cette machine fait entendre du chargeur d'un joueur.
        Debug.Log($"[DIAG-SON] {transform.root.name} {data.weaponName} : {sound} ({before.ammo} → {after.ammo} balles)");
#endif
        if ((sound & ReloadSound.Interrupted) != 0) StopReload();
        if ((sound & ReloadSound.Started) != 0) StartReload(data, duration);
        if ((sound & ReloadSound.RoundIn) != 0) PlayMechanism(Pick(data.reloadRoundSounds), data.reloadVolume);
        if ((sound & ReloadSound.Completed) != 0)
        {
            AudioClip end = Pick(data.reloadEndSounds);
            if (end != null) Schedule(Time.time + data.reloadEndDelay, end, data.reloadVolume, reload: false);
        }
    }

    /// <summary>
    /// Ce qu'un changement du chargeur fait entendre. Fonction pure, testée.
    ///
    /// Un rechargement qui s'arrête sans chargeur plein a été interrompu (sprint, taser, tir au pompe,
    /// refus du serveur) : il doit se taire. Changer d'arme en rechargeant l'interrompt aussi. Au pompe
    /// (`perRound`), chaque cartouche entrée s'entend ; ailleurs, la hausse des balles à la fin n'est
    /// que le chargeur complet.
    /// </summary>
    public static ReloadSound ClassifyReload(in WeaponController.MagazineState before, in WeaponController.MagazineState after,
                                             int magazineSize, bool perRound)
        => ClassifyReload(before, after, magazineSize, perRound, false);

    /// <summary>La même chose pour une arme laser (`usesHeat`) : sa vidange va à son terme quand elle
    /// est froide, sans compter de balles.</summary>
    public static ReloadSound ClassifyReload(in WeaponController.MagazineState before, in WeaponController.MagazineState after,
                                             int magazineSize, bool perRound, bool usesHeat)
    {
        bool wasReloading = before.reloadEndsAt > 0.0;
        bool isReloading = after.reloadEndsAt > 0.0;
        if (after.equipped != before.equipped) return wasReloading ? ReloadSound.Interrupted : ReloadSound.None;

        if (!wasReloading) return isReloading ? ReloadSound.Started : ReloadSound.None;

        ReloadSound sound = ReloadSound.None;
        bool roundIn = perRound && after.ammo > before.ammo;
        if (roundIn) sound |= ReloadSound.RoundIn;
        if (isReloading)
        {
            // Toujours en cours : une cartouche de plus, ou un nouveau rechargement (annulé puis
            // relancé entre deux mises à jour du serveur).
            if (!roundIn && after.reloadEndsAt != before.reloadEndsAt) sound |= ReloadSound.Interrupted | ReloadSound.Started;
            return sound;
        }
        bool done = usesHeat ? after.heat <= 0f && !after.overheated : after.ammo >= magazineSize;
        return sound | (done ? ReloadSound.Completed : ReloadSound.Interrupted);
    }

    private void StartReload(WeaponData data, float duration)
    {
        StopReloadNow();
        if (data.reloadCues == null) return;
        float now = Time.time;
        foreach (ReloadCue cue in data.reloadCues)
        {
            AudioClip clip = Pick(cue.clips);
            if (clip != null) Schedule(now + cue.at * duration, clip, cue.volume, reload: true);
        }
    }

    private void StopReload()
    {
        pending.RemoveAll(p => p.reload);
        if (reloadVoice != null && reloadVoice.isPlaying) FadeOut(reloadVoice, reloadStopFade);
    }

    private void StopReloadNow()
    {
        pending.RemoveAll(p => p.reload);
        if (reloadVoice == null) return;
        CancelFade(reloadVoice);
        reloadVoice.Stop();
        reloadVoice.volume = 1f;
    }

    // ------------------------------------------------------------------
    // Voix
    // ------------------------------------------------------------------

    private void Play(AudioSource voice, AudioClip clip, float pitch, float volume)
    {
        CancelFade(voice);
        voice.Stop();
        voice.clip = clip;
        voice.pitch = pitch;
        voice.volume = volume;
        voice.Play();
    }

    private void PlayMechanism(AudioClip clip, float volume)
    {
        if (clip == null || mechanicsVoice == null) return;
        mechanicsVoice.PlayOneShot(clip, volume);
#if UNITY_EDITOR
        OneShotAudio.DebugNote("arme " + transform.root.name, clip);
#endif
    }

    private void PlayMechanism(SoundSet sound)
    {
        if (sound != null) PlayMechanism(sound.Pick(), sound.volume);
    }

    /// <summary>La gâchette pressée chargeur vide (le tireur seul).</summary>
    public void PlayDryFire(WeaponData data)
    {
        if (data != null) PlayMechanism(data.dryFireSounds);
    }

    private void Schedule(float at, AudioClip clip, float volume, bool reload)
        => pending.Add(new Pending { at = at, clip = clip, volume = volume, reload = reload });

    private void FadeOut(AudioSource source, float duration)
    {
        CancelFade(source);
        fades.Add(new Fade { source = source, from = source.volume, startedAt = Time.time, duration = duration });
    }

    private void CancelFade(AudioSource source)
    {
        for (int i = fades.Count - 1; i >= 0; i--)
        {
            if (fades[i].source == source) fades.RemoveAt(i);
        }
    }

    private static AudioClip Pick(AudioClip[] clips)
        => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
    /// <summary>Tests : la voix du rechargement complet joue-t-elle ?</summary>
    public bool TestReloadPlaying => reloadVoice != null && reloadVoice.isPlaying;
    /// <summary>Tests : la montée en charge joue-t-elle, et à quel volume ?</summary>
    public bool TestChargePlaying(out float volume)
    {
        volume = chargeVoice != null ? chargeVoice.volume : 0f;
        return chargeVoice != null && chargeVoice.isPlaying;
    }
    /// <summary>Tests : la voix de la mécanique (culasse, pompe, cartouches) joue-t-elle ?</summary>
    public bool TestMechanicsPlaying => mechanicsVoice != null && mechanicsVoice.isPlaying;
    /// <summary>Tests : les queues de réverbération qui jouent encore, et leur volume.</summary>
    public int TestTailsPlaying(out float loudest)
    {
        int playing = 0;
        loudest = 0f;
        foreach (AudioSource tail in tailVoices)
        {
            if (!tail.isPlaying) continue;
            playing++;
            loudest = Mathf.Max(loudest, tail.volume);
        }
        return playing;
    }
    /// <summary>Tests : le clip du dernier coup joué.</summary>
    public AudioClip TestLastShotClip => shotVoices[(nextShotVoice + shotVoices.Length - 1) % shotVoices.Length].clip;
#endif
}
