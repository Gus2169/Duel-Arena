using UnityEngine;

/// <summary>
/// Sons ponctuels joués à un POINT du monde (impacts, décharges, mort), ou en 2D pour le seul
/// joueur local (retours de touche). Les sources sont recyclées : aucune allocation par son.
///
/// Distinct de PlayerSoundEmitter, qui joue les sons de contexte d'un joueur depuis SA source
/// (catégorie B). Ici, le son appartient à un endroit, pas à quelqu'un — il reste donc audible
/// même si le joueur qui l'a causé s'est déjà éloigné.
///
/// Réglages des sources 3D : Doppler à 0 (même raison que PlayerSoundEmitter : les transforms sont
/// déplacés à la main, un Doppler calculé dessus ferait varier le pitch au hasard), atténuation
/// logarithmique entre minDistance et maxDistance.
/// </summary>
public static class Sfx
{
    private const int PoolSize = 16;

    private static AudioSource[] pool;
    private static int next;
    private static AudioSource source2D;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pool = null;
        next = 0;
        source2D = null;
    }

    /// <summary>Joue <paramref name="clip"/> en 3D à <paramref name="position"/>.</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 2f, float maxDistance = 35f, float pitchVariance = 0.05f)
    {
        if (clip == null) return;
        EnsurePool();

        // Une source encore occupée par un son long est sautée ; si toutes le sont, la plus
        // ancienne est coupée — mieux vaut perdre la queue d'un son que le début d'un autre.
        AudioSource src = null;
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource candidate = pool[(next + i) % PoolSize];
            if (candidate != null && !candidate.isPlaying)
            {
                src = candidate;
                next = (next + i + 1) % PoolSize;
                break;
            }
        }
        if (src == null)
        {
            src = pool[next];
            next = (next + 1) % PoolSize;
            if (src == null) return;
        }

        src.transform.position = position;
        src.minDistance = minDistance;
        src.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
        src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.Play();
    }

    public static void PlayAt(AudioClip[] clips, Vector3 position, float volume = 1f, float minDistance = 2f, float maxDistance = 35f, float pitchVariance = 0.05f)
    {
        PlayAt(VfxLibrary.Pick(clips), position, volume, minDistance, maxDistance, pitchVariance);
    }

    /// <summary>Son non spatialisé, pour le seul joueur de cette machine (hitmarker, interférences
    /// de dégâts). Ne jamais l'utiliser pour un son que l'adversaire devrait pouvoir localiser.</summary>
    public static void Play2D(AudioClip clip, float volume = 1f, float pitchVariance = 0f)
    {
        if (clip == null) return;
        EnsurePool();
        if (source2D == null) return;
        source2D.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        source2D.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private static void EnsurePool()
    {
        if (pool != null && pool[0] != null && source2D != null) return;

        Transform root = VfxRunner.Root;
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Sfx3D_" + i);
            go.transform.SetParent(root, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.spread = 0f;
            pool[i] = src;
        }

        var go2D = new GameObject("Sfx2D");
        go2D.transform.SetParent(root, false);
        source2D = go2D.AddComponent<AudioSource>();
        source2D.playOnAwake = false;
        source2D.spatialBlend = 0f;
        source2D.dopplerLevel = 0f;
        // Les retours de touche passent devant les sons du monde : ce sont eux qui disent au tireur
        // ce qui s'est passé.
        source2D.priority = 32;
        next = 0;
    }
}
