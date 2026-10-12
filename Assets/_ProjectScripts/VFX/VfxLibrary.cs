using UnityEngine;

/// <summary>
/// Matériaux, couleurs et sons des effets visuels (étincelles, poussière, flashs, arcs
/// électriques). Data-driven comme WeaponData et PlayerSoundBank : régler un effet se fait dans
/// l'asset, jamais dans le code.
///
/// Un seul asset, chargé depuis Resources (<c>Assets/_ProjectArt/VFX/Resources/VfxLibrary.asset</c>)
/// plutôt que référencé par un prefab : les effets sont demandés depuis du code statique
/// (<see cref="Vfx"/>), et passer par Resources garantit que matériaux et shaders entrent dans la
/// build. Sans l'asset, <see cref="Vfx"/> retombe sur les marqueurs procéduraux d'avant au lieu de
/// planter.
///
/// DA (bible § 10 et § 17) : décor sombre, robots de couleur vive, effets « étincelles, flashes,
/// poussière, fumée légère ; pas de gore », et jamais d'effet qui masque l'adversaire. D'où des
/// effets courts et petits. L'électricité est d'un bleu-blanc froid : elle doit rester distincte
/// de la peinture des robots quand la couleur par joueur arrivera.
/// </summary>
[CreateAssetMenu(fileName = "VfxLibrary", menuName = "Duel Arena/VFX Library")]
public class VfxLibrary : ScriptableObject
{
    public const string ResourcePath = "VfxLibrary";

    [Header("Matériaux (URP Particles/Unlit)")]
    [Tooltip("Point lumineux doux, additif. Étiré par la vitesse, il fait aussi les étincelles.")]
    public Material glow;
    [Tooltip("Éclat en étoile, additif : coeur des flashs.")]
    public Material star;
    [Tooltip("Craquelures électriques, additif, atlas 2x2.")]
    public Material crackle;
    [Tooltip("Flash de bouche, additif, atlas 2x2.")]
    public Material muzzle;
    [Tooltip("Profil de faisceau pour les LineRenderer (arcs, traceurs), additif.")]
    public Material beam;
    [Tooltip("Fumée et poussière, alpha, atlas 2x2.")]
    public Material smoke;
    [Tooltip("Trace d'impact sur le décor, alpha.")]
    public Material scorch;

    [Header("Couleurs")]
    public Color electricCore = new Color(0.86f, 0.95f, 1f);
    public Color electricGlow = new Color(0.30f, 0.66f, 1f);
    public Color sparkHot = new Color(1f, 0.82f, 0.55f);
    public Color sparkCool = new Color(1f, 0.45f, 0.15f);
    public Color muzzleColor = new Color(1f, 0.78f, 0.45f);
    public Color tracerColor = new Color(1f, 0.86f, 0.62f);
    public Color dustColor = new Color(0.52f, 0.50f, 0.47f, 0.55f);
    public Color smokeColor = new Color(0.20f, 0.21f, 0.23f, 0.45f);
    public Color scorchColor = new Color(0.04f, 0.04f, 0.04f, 0.85f);

    [Header("Sons d'impact (joués en 3D au point touché)")]
    public AudioClip[] impactWorld;
    public AudioClip[] impactChassis;
    [Range(0f, 1f)] public float impactVolume = 0.8f;
    [Tooltip("Distance (m) à laquelle un impact commence à s'atténuer.")]
    public float impactMinDistance = 2f;
    [Tooltip("Distance (m) au-delà de laquelle un impact ne s'entend plus. L'arène fait ~66 x 74 m.")]
    public float impactMaxDistance = 35f;

    [Header("Sons électriques (taser)")]
    [Tooltip("Charge du condensateur à la sortie du taser. Défaut d'ElectricArc.")]
    public AudioClip taserDraw;
    [Tooltip("Crépitement tant que le taser est sorti : 2,000 s, 18 claquements par seconde, calés avec l'arc. Défaut d'ElectricArc.")]
    public AudioClip taserIdleLoop;
    public AudioClip[] electricHit;
    public AudioClip[] electricZap;
    [Range(0f, 1f)] public float electricVolume = 0.95f;

    [Header("Sons de mort")]
    public AudioClip[] deathShutdown;
    [Range(0f, 1f)] public float deathVolume = 1f;

    [Header("Budget")]
    [Tooltip("Traces d'impact visibles en même temps. Les plus anciennes disparaissent d'abord.")]
    public int maxScorchMarks = 32;
    [Tooltip("Durée de vie (s) d'une trace d'impact, fondu compris. Le fondu au noir entre deux manches est le bon moment pour tout nettoyer (Vfx.ClearPersistent).")]
    public float scorchLifetime = 12f;
    [Tooltip("Lumières ponctuelles simultanées pour les flashs. URP limite les lumières additionnelles par objet : rester bas.")]
    public int maxFlashLights = 4;

    private static VfxLibrary cached;
    private static bool searched;

    /// <summary>L'asset de Resources, ou null s'il manque (les effets se dégradent alors).</summary>
    public static VfxLibrary Instance
    {
        get
        {
            if (!searched)
            {
                searched = true;
                cached = Resources.Load<VfxLibrary>(ResourcePath);
                if (cached == null) Debug.LogWarning($"[VFX] Resources/{ResourcePath} introuvable : effets réduits aux marqueurs de secours.");
            }
            return cached;
        }
    }

    // Le rechargement de domaine est désactivé à l'entrée en Play Mode (voir CLAUDE.md) : sans ça,
    // la recherche ne serait faite qu'une fois par session d'Editor.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cached = null;
        searched = false;
    }

    public static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }
}
