using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le son du déroulé d'un match, dans l'arène (2026-10-13, GDD § 5 et § 8), sur /GameUI :
/// - le décompte (un bip par seconde) et le départ de la manche ;
/// - la fin d'une manche (pour tous, et un son de plus chez le gagnant) ;
/// - dans le noir entre deux manches, la reconstruction des châssis (perceuses, servomoteurs), finie
///   bien avant que la manche suivante ne démarre ;
/// - l'ambiance de la salle, en boucle ;
/// - la musique : pendant la préparation, éteinte au décompte (« pas de musique pendant la manche »,
///   et la manche commence dans un silence où l'on entend les pas), puis sur l'écran de victoire.
///
/// Il lit l'état publié par le serveur (RoundManager) et ne décide rien. À plat : chaque joueur
/// l'entend chez lui. Catégorie C.
/// </summary>
[RequireComponent(typeof(MusicPlayer))]
public class MatchAudio : MonoBehaviour
{
    [SerializeField] private GameSounds sounds;
    [Tooltip("La reconstruction part quand le noir commence à tomber : autant de secondes avant la fin de la manche (le fondu de GameHud).")]
    [SerializeField] private float reconstructionLead = 0.9f;
    [Tooltip("La musique de la préparation s'éteint au décompte en ce temps (s).")]
    [SerializeField] private float musicFadeOut = 0.8f;
    [Tooltip("Fondu d'entrée de la musique de victoire (s) : courte, le morceau part sur son attaque.")]
    [SerializeField] private float victoryFadeIn = 0.15f;

    private MusicPlayer music;
    private AudioSource ambience;

    private bool tracking;
    // Le match suivi : au Tournoi, il change (le couloir de son match, la salle d'attente).
    private RoundManager trackedMatch;
    private RoundManager.Phase lastPhase;
    private bool roundEndPending;
    private double lastPhaseStartedAt;
    private int lastCountdown;
    private bool reconstructionPlayed;
    private int lastModulesTaken;
    private int lastTagIns;
    private int lastZone;
    private bool lastZoneAnnounced;
    private readonly List<(float at, SoundSet sound)> pending = new List<(float, SoundSet)>();

    private void Awake()
    {
        music = GetComponent<MusicPlayer>();
        ambience = gameObject.AddComponent<AudioSource>();
        ambience.playOnAwake = false;
        ambience.loop = true;
        ambience.spatialBlend = 0f;
        ambience.dopplerLevel = 0f;
        ambience.priority = 0;
    }

    private void OnDisable()
    {
        pending.Clear();
        tracking = false;
    }

    private void Update()
    {
        // Le match du joueur de cette machine : au Tournoi, celui de son couloir, ou la salle d'attente.
        RoundManager rm = RoundManager.Local;
        NetworkObject local = GameHud.LocalPlayerObject;
        bool inGame = rm != null && rm.IsSpawned && local != null;
        UpdateAmbience(inGame);
        if (!inGame)
        {
            tracking = false;
            music.Stop();
            return;
        }

        // D'un match du Tournoi à la salle d'attente, ou l'inverse : on reprend l'état tel qu'il est.
        if (!ReferenceEquals(rm, trackedMatch))
        {
            trackedMatch = rm;
            tracking = false;
        }
        RoundManager.Phase phase = rm.CurrentPhase;
        if (!tracking)
        {
            // Arrivée en cours de partie : on prend l'état tel qu'il est, sans rejouer ce qui est passé.
            tracking = true;
            lastPhase = phase;
            lastPhaseStartedAt = rm.PhaseStartedAt;
            lastCountdown = CountdownNumber(rm.PhaseTimeRemaining);
            reconstructionPlayed = rm.PhaseHasEnd && rm.PhaseTimeRemaining <= reconstructionLead;
            lastModulesTaken = rm.ModulesTaken;
            lastTagIns = rm.TagIns;
            lastZone = rm.ShownZone;
            lastZoneAnnounced = rm.AnnouncedZone >= 0;
        }
        else if (phase != lastPhase || rm.PhaseStartedAt != lastPhaseStartedAt)
        {
            OnPhaseEntered(rm, phase, lastPhase, local.OwnerClientId);
            lastPhase = phase;
            lastPhaseStartedAt = rm.PhaseStartedAt;
            lastCountdown = int.MaxValue;
            reconstructionPlayed = false;
        }

        // Le son de la fin de manche attend que la kill cam ait montré le coup.
        if (roundEndPending && !KillCamRunning(rm))
        {
            roundEndPending = false;
            if (phase == RoundManager.Phase.RoundOver || phase == RoundManager.Phase.MatchOver) PlayRoundEnd(rm, local.OwnerClientId);
        }

        if (phase == RoundManager.Phase.Starting && TryCountdownTick(ref lastCountdown, rm.PhaseTimeRemaining))
            Play(sounds != null ? sounds.countdownBeep : null);

        if (!reconstructionPlayed && ReconstructionDue(phase, rm.PhaseHasEnd, rm.PhaseTimeRemaining, reconstructionLead))
        {
            reconstructionPlayed = true;
            StartReconstruction();
        }

        if (rm.ModulesTaken != lastModulesTaken)
        {
            lastModulesTaken = rm.ModulesTaken;
            OnModuleTaken(rm, local.OwnerClientId);
        }

        // Au Tag Team, un coéquipier entre dans l'arène : son châssis s'allume (la reconstruction, chez tous).
        if (rm.TagIns != lastTagIns)
        {
            lastTagIns = rm.TagIns;
            if (phase == RoundManager.Phase.Active) StartReconstruction();
        }

        // La Zone : un bip quand la suivante s'annonce, un son quand elle change de place.
        int zone = rm.ShownZone;
        bool announced = rm.AnnouncedZone >= 0;
        if (phase == RoundManager.Phase.Active && sounds != null)
        {
            if (announced && !lastZoneAnnounced) Play(sounds.countdownBeep);
            if (zone != lastZone && lastZone >= 0 && zone >= 0) Play(sounds.zoneMoved);
        }
        lastZone = zone;
        lastZoneAnnounced = announced;

        UpdateMusic(rm, phase);
        PlayPending();
    }

    /// <summary>Un module vient d'être pris (l'Élimination confirmée) : on l'entend là où il flottait (catégorie B), et
    /// celui qui l'a pris entend en plus si son élimination compte, ou s'il a annulé celle de l'autre.</summary>
    private void OnModuleTaken(RoundManager rm, ulong me)
    {
        if (sounds == null) return;
        sounds.moduleTaken?.PlayAt(rm.LastModuleAt, sounds.moduleMinDistance, sounds.moduleMaxDistance);
        if (rm.LastModuleTaker == me) Play(rm.LastModuleConfirmed ? sounds.moduleConfirmed : sounds.moduleDenied);
    }

    private void OnPhaseEntered(RoundManager rm, RoundManager.Phase phase, RoundManager.Phase previous, ulong me)
    {
        if (sounds == null) return;
        if (phase == RoundManager.Phase.Active && previous == RoundManager.Phase.Starting) Play(sounds.roundStart);

        bool roundEnded = (phase == RoundManager.Phase.RoundOver || phase == RoundManager.Phase.MatchOver)
                          && previous == RoundManager.Phase.Active;
        if (!roundEnded) return;
        // Sous la kill cam, la balle vole encore : la fin de manche s'entendra quand elle aura tout montré.
        if (KillCamRunning(rm)) roundEndPending = true;
        else PlayRoundEnd(rm, me);
    }

    private static bool KillCamRunning(RoundManager rm)
    {
        KillCam.Info info = rm.KillCamNow;
        return info.Scheduled && rm.KillCamElapsed < KillCam.DurationOf(info);
    }

    private void PlayRoundEnd(RoundManager rm, ulong me)
    {
        if (sounds == null) return;
        Play(sounds.roundOver);
        // Au Tag Team, toute l'équipe gagnante entend sa victoire, ceux qui attendaient sur la tribune compris.
        PlayerMatchState mine = PlayerMatchState.Of(me);
        bool won = rm.TagTeamMode ? mine != null && mine.Team != PlayerTeam.None && mine.Team == rm.WinningTeam
                                  : rm.LastRoundWinner == me && !GameHud.LocalIsSpectator;
        if (won) Play(sounds.roundWon);
    }

    // ------------------------------------------------------------------
    // Décompte et reconstruction (fonctions pures, testées)
    // ------------------------------------------------------------------

    /// <summary>Le chiffre que montre le décompte (3, 2, 1) pour `remaining` secondes.</summary>
    public static int CountdownNumber(float remaining) => Mathf.Max(1, Mathf.CeilToInt(remaining));

    /// <summary>Un nouveau chiffre du décompte vient-il de s'afficher ? Un bip par chiffre, jamais deux
    /// pour le même, même si l'horloge du serveur recule d'un poil.</summary>
    public static bool TryCountdownTick(ref int lastShown, float remaining)
    {
        int number = CountdownNumber(remaining);
        if (number >= lastShown) return false;
        lastShown = number;
        return true;
    }

    /// <summary>
    /// Faut-il lancer la reconstruction ? Quand le noir commence à tomber, `lead` secondes avant la
    /// fin d'une manche finie, ou de l'écran de victoire (le match suivant repart de châssis neufs).
    /// Pas au noir qui précède l'écran de victoire : la fin de match dure jusqu'à la fin de cet écran,
    /// et c'est le gagnant qu'on regarde. Jamais pendant une manche.
    /// </summary>
    public static bool ReconstructionDue(RoundManager.Phase phase, bool phaseHasEnd, float untilEnd, float lead)
    {
        if (phase != RoundManager.Phase.RoundOver && phase != RoundManager.Phase.MatchOver) return false;
        return phaseHasEnd && untilEnd <= lead && untilEnd >= 0f;
    }

    private void StartReconstruction()
    {
        if (sounds == null || sounds.reconstruction == null) return;
        float now = Time.time;
        foreach (TimedSound step in sounds.reconstruction) pending.Add((now + step.delay, step.sound));
    }

    private void PlayPending()
    {
        float now = Time.time;
        for (int i = 0; i < pending.Count; i++)
        {
            if (pending[i].at > now) continue;
            Play(pending[i].sound);
            pending.RemoveAt(i--);
        }
    }

    private static void Play(SoundSet sound) => sound?.Play2D();

    // ------------------------------------------------------------------
    // Ambiance et musique
    // ------------------------------------------------------------------

    private void UpdateAmbience(bool inGame)
    {
        bool wanted = inGame && sounds != null && sounds.ambienceLoop != null;
        if (!wanted)
        {
            if (ambience.isPlaying) ambience.Stop();
            return;
        }
        ambience.volume = sounds.ambienceVolume;
        if (ambience.isPlaying) return;
        ambience.clip = sounds.ambienceLoop;
        ambience.Play();
    }

    private void UpdateMusic(RoundManager rm, RoundManager.Phase phase)
    {
        if (sounds == null) return;
        if (phase == RoundManager.Phase.Preparation)
        {
            music.Play(sounds.preparationMusic);
        }
        else if (phase == RoundManager.Phase.Salon || phase == RoundManager.Phase.Tournament)
        {
            // Le salon est un menu : sa musique (2026-10-26) ; la salle d'attente du Tournoi aussi, entre deux
            // matchs (2026-11-03, proposition de Claude).
            music.Play(sounds.menuMusic);
        }
        else if (phase == RoundManager.Phase.MatchOver && MatchEndScreen.IsShown)
        {
            // Le noir retombe avant le match suivant : la musique s'éteint avec la lumière.
            bool ending = rm.PhaseHasEnd && rm.PhaseTimeRemaining <= reconstructionLead;
            if (ending) music.Stop(reconstructionLead);
            else music.Play(PickVictoryTrack(sounds.victoryMusic, rm.PhaseStartedAt), victoryFadeIn);
        }
        else
        {
            music.Stop(musicFadeOut);
        }
    }

    /// <summary>Le morceau de l'écran de victoire, tiré sur l'instant de fin du match publié par le
    /// serveur : le même chez tous les joueurs. Fonction pure, testée.</summary>
    public static MusicTrack PickVictoryTrack(MusicTrack[] tracks, double seed)
    {
        if (tracks == null || tracks.Length == 0) return null;
        double ms = System.Math.Floor(System.Math.Abs(seed) * 1000.0);
        if (!(ms < long.MaxValue)) ms = 0.0; // NaN, infini ou démesuré : le premier, plutôt qu'un indice négatif
        return tracks[(int)((long)ms % tracks.Length)];
    }
}
