using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Les voix de l'arme (WeaponAudio, 2026-10-13), avec de vraies sources audio : ce qu'on ne peut pas
/// écouter ici, on le vérifie à l'état des voix. Un rechargement interrompu doit se TAIRE (avant, son
/// clip continuait et l'adversaire entendait un rechargement qui n'avait plus lieu), une rafale ne
/// doit garder qu'une queue de réverbération vivante, et la culasse part après le coup.
/// </summary>
public class WeaponAudioPlayModeTests
{
    private GameObject holder;
    private GameObject listener;
    private WeaponAudio audio;
    private WeaponData data;

    [SetUp]
    public void SetUp()
    {
        listener = new GameObject("Oreille de test", typeof(AudioListener));
        holder = new GameObject("Arme de test");
        AudioSource template = holder.AddComponent<AudioSource>(); // la source du prefab, avant WeaponAudio
        template.spatialBlend = 1f;
        audio = holder.AddComponent<WeaponAudio>();

        data = ScriptableObject.CreateInstance<WeaponData>();
        data.shotsPerSecond = 7f;
        data.fireSounds = new[] { Tone("coup", 0.09f) };
        data.fireFirstSound = Tone("premier coup", 0.09f);
        data.fireVolume = 1f;
        data.fireTailSound = Tone("queue", 2f);
        data.magazineSize = 30;
        data.reloadDuration = 2f;
        data.reloadCues = new[]
        {
            new ReloadCue { clips = new[] { Tone("chargeur retiré", 0.5f) }, at = 0f, volume = 1f },
            new ReloadCue { clips = new[] { Tone("chargeur inséré", 0.3f) }, at = 0.5f, volume = 1f }, // à 1 s
        };
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(holder);
        Object.Destroy(listener);
        Object.Destroy(data);
    }

    private static AudioClip Tone(string name, float seconds)
    {
        const int rate = 48000;
        int samples = Mathf.CeilToInt(seconds * rate);
        var clip = AudioClip.Create(name, samples, 1, rate, false);
        var buffer = new float[samples];
        for (int i = 0; i < samples; i++) buffer[i] = 0.2f * Mathf.Sin(2f * Mathf.PI * 440f * i / rate);
        clip.SetData(buffer, 0);
        return clip;
    }

    private static WeaponController.MagazineState Chargeur(byte balles, double finRechargement = 0.0)
    {
        var m = new WeaponController.MagazineState { reloadEndsAt = finRechargement };
        m.ammo = balles;
        return m;
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }

    [UnityTest]
    public IEnumerator UnRechargementInterrompu_SeTait_EtSesEtapesSuivantesNePartentPas()
    {
        audio.Hear(Chargeur(5), Chargeur(5, 100.0), data, data);
        yield return Wait(0.1f);
        Assert.IsTrue(audio.TestReloadPlaying, "Garde : la première étape (au début du rechargement) joue.");

        audio.Hear(Chargeur(5, 100.0), Chargeur(5), data, data); // sprint, taser, refus du serveur
        yield return Wait(0.2f); // la première étape (0,5 s) jouerait encore
        Assert.IsFalse(audio.TestReloadPlaying, "Le rechargement interrompu se tait (fondu de 0,08 s).");

        yield return Wait(0.85f); // la seconde étape aurait commencé à 1 s
        Assert.IsFalse(audio.TestReloadPlaying, "Les étapes suivantes d'un rechargement interrompu ne partent pas.");
    }

    [UnityTest]
    public IEnumerator UnRechargementMeneASonTerme_JoueToutesSesEtapes()
    {
        audio.Hear(Chargeur(5), Chargeur(5, 100.0), data, data);
        yield return Wait(0.7f);
        Assert.IsFalse(audio.TestReloadPlaying, "Garde : la première étape (0,5 s) est finie, la seconde pas encore partie.");
        yield return Wait(0.45f); // la seconde étape part à 1 s, à la moitié du rechargement
        Assert.IsTrue(audio.TestReloadPlaying, "La seconde étape part à sa part de la durée du rechargement.");
    }

    [UnityTest]
    public IEnumerator UneRafale_NeGardeQuUneQueueDeReverberation()
    {
        audio.PlayShot(data);
        Assert.AreEqual(data.fireFirstSound, audio.TestLastShotClip, "Le premier coup d'une rafale a son propre son.");
        yield return Wait(1f / 7f);
        audio.PlayShot(data);
        Assert.AreEqual(data.fireSounds[0], audio.TestLastShotClip, "Les coups suivants sont des coups secs.");
        Assert.AreEqual(2, audio.TestTailsPlaying(out _), "Garde : au coup suivant, l'ancienne queue s'éteint en fondu.");

        yield return Wait(0.12f); // au-delà du fondu (0,06 s)
        Assert.AreEqual(1, audio.TestTailsPlaying(out float loudest), "Une seule queue reste : sinon elles s'empilent et la rafale devient boueuse.");
        Assert.AreEqual(1f, loudest, 1e-3f, "La queue du dernier coup garde tout son volume.");
    }

    [UnityTest]
    public IEnumerator LaCulasse_PartApresLeCoup_PasAvant()
    {
        data.fireTailSound = null;
        data.fireMechanismSounds = new[] { Tone("culasse", 0.4f) };
        data.fireMechanismDelay = 0.3f;
        audio.PlayShot(data);
        yield return Wait(0.15f);
        Assert.IsFalse(audio.TestMechanicsPlaying, "La culasse attend son délai.");
        yield return Wait(0.3f);
        Assert.IsTrue(audio.TestMechanicsPlaying, "La culasse part fireMechanismDelay après le coup.");
    }
}
