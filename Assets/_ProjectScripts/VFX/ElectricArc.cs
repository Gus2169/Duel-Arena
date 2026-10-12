using UnityEngine;

/// <summary>
/// Arc électrique entre deux électrodes : la tête du taser (GDD § 6, bible § 12).
///
/// Comment ça se lit comme de l'électricité : un vrai taser claque par impulsions (~18 par
/// seconde). À chaque impulsion l'arc change brutalement de forme et de luminosité, puis
/// s'éteint à moitié jusqu'à la suivante. C'est ce saut, pas un mouvement fluide, que l'oeil
/// reconnaît. Deux lignes superposées (un coeur fin presque blanc, un halo bleu plus large), une
/// branche occasionnelle, une lumière ponctuelle qui clignote au même rythme, et des craquelures
/// aux électrodes.
///
/// 🔊 **Le taser sorti crépite en permanence, et l'adversaire l'entend** (GDD § 8, tranché par
/// l'utilisateur le 2026-10-12) : au corps à corps les positions sont déjà connues, et sortir le
/// taser devient un choix qui se paie. Sans réglage, le composant crée donc sa propre source 3D
/// et joue la charge (Taser_Draw) à l'allumage, le crépitement en boucle (Taser_Idle_Loop)
/// tant qu'il est sorti, et une décharge (Taser_Zap) à chaque coup. Les impulsions visuelles sont
/// calées sur la position de lecture de la boucle : l'image et le claquement tombent ensemble.
///
/// 🚨 **Ce son est une information de gameplay** (catégorie B, CLAUDE.md), même si le composant,
/// lui, ne fait que l'afficher et le jouer. <see cref="Active"/> doit donc être piloté sur
/// TOUTES les machines, depuis un état que toutes connaissent (l'arme équipée publiée par le
/// serveur), jamais depuis la seule touche du propriétaire : sinon le joueur entendrait son
/// taser et son adversaire, rien.
///
/// Utilisation : poser ce composant sur l'objet du taser, renseigner les deux électrodes (sinon
/// deux points à ±1,5 cm de l'objet sur son axe X), puis piloter <see cref="Active"/> (arme
/// sortie) et <see cref="Discharge"/> (le coup). Sur un coup qui touche, appeler
/// <c>Discharge(playZap: false)</c> et <see cref="Vfx.ElectricHit"/> au point d'impact, qui joue
/// déjà le son de décharge sur la cible. Aucune incidence sur la simulation ni les dégâts.
/// </summary>
public class ElectricArc : MonoBehaviour
{
    [Header("Électrodes")]
    [SerializeField] private Transform electrodeA;
    [SerializeField] private Transform electrodeB;
    [Tooltip("Écart des électrodes par défaut (m), utilisé seulement si elles ne sont pas renseignées.")]
    [SerializeField] private float defaultGap = 0.03f;

    [Header("Forme")]
    [SerializeField, Range(4, 24)] private int segments = 10;
    [Tooltip("Amplitude des zigzags, relative à la longueur de l'arc.")]
    [SerializeField, Range(0f, 0.6f)] private float jitter = 0.28f;
    [SerializeField] private float coreWidth = 0.004f;
    [SerializeField] private float glowWidth = 0.018f;
    [Tooltip("Impulsions par seconde. Doit correspondre au clip en boucle (18 pour Taser_Idle_Loop).")]
    [SerializeField] private float pulsesPerSecond = 18f;
    [Tooltip("Luminosité entre deux impulsions (0 = l'arc s'éteint, 1 = jamais).")]
    [SerializeField, Range(0f, 1f)] private float idleFloor = 0.35f;
    [SerializeField, Range(0f, 1f)] private float branchChance = 0.35f;

    [Header("Lumière")]
    [SerializeField] private bool emitLight = true;
    [SerializeField] private float lightIntensity = 1.2f;
    [SerializeField] private float lightRange = 1.6f;

    [Header("Son")]
    [Tooltip("Le taser sorti s'entend (GDD § 8). Ne décocher que pour un taser purement décoratif (vitrine, menu).")]
    [SerializeField] private bool playSound = true;
    [Tooltip("Source du crépitement en boucle. Vide : une source 3D est créée sous cet objet (Doppler 0, comme les sons de joueur).")]
    [SerializeField] private AudioSource loopSource;
    [Tooltip("Clips propres à ce taser. Vides : ceux de VfxLibrary (Taser_Draw, Taser_Idle_Loop, Taser_Zap).")]
    [SerializeField] private AudioClip drawClip;
    [SerializeField] private AudioClip loopClip;
    [SerializeField] private AudioClip[] zapClips;
    [SerializeField, Range(0f, 1f)] private float loopVolume = 0.5f;
    [SerializeField, Range(0f, 1f)] private float drawVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float zapVolume = 0.9f;
    [Tooltip("Distance (m) à laquelle le crépitement commence à s'atténuer.")]
    [SerializeField] private float soundMinDistance = 2f;
    [Tooltip("Distance (m) au-delà de laquelle il ne s'atténue plus. Du même ordre que les pas (20 m). Réglage de ressenti ET d'équilibrage : c'est la distance à laquelle on trahit son taser.")]
    [SerializeField] private float soundMaxDistance = 25f;
    [Tooltip("Délai (s) entre la sortie et l'allumage de l'arc : le temps que le condensateur charge dans Taser_Draw, dont les premiers claquements tombent à 0,47 s.")]
    [SerializeField] private float chargeDelay = 0.45f;

    [Header("État")]
    [SerializeField] private bool activeOnStart;

    private LineRenderer core, glow, branch;
    private Light pointLight;
    private AudioSource oneShotSource;
    private bool active;
    private float litAt;
    private float lastPulseTime = -1f, nextPulseTime;
    private int lastPulseIndex = -1;
    private float dischargeUntil;
    private float pulseStrength;

    /// <summary>Taser sorti : charge, puis arc et crépitement tant que c'est vrai. Faux : plus
    /// rien n'est dessiné ni joué.</summary>
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            if (active)
            {
                litAt = Time.time + Mathf.Max(0f, chargeDelay);
                lastPulseIndex = -1;
                if (playSound)
                {
                    PlayOneShot(drawClip != null ? drawClip : Library(l => l.taserDraw), drawVolume);
                    AudioClip loop = loopClip != null ? loopClip : Library(l => l.taserIdleLoop);
                    if (loopSource != null && loop != null)
                    {
                        loopSource.clip = loop;
                        loopSource.loop = true;
                        loopSource.volume = loopVolume * 0.8f;
                        loopSource.PlayDelayed(Mathf.Max(0f, chargeDelay));
                    }
                }
            }
            else
            {
                HideAll();
                if (loopSource != null) loopSource.Stop();
            }
        }
    }

    /// <summary>Décharge pleine puissance pendant <paramref name="duration"/> secondes : arcs plus
    /// larges, plus agités, branches à chaque impulsion. Pour le coup de taser.
    /// <paramref name="playZap"/> : faux quand le coup touche, Vfx.ElectricHit jouant alors la
    /// décharge sur la cible.</summary>
    public void Discharge(float duration = 0.35f, bool playZap = true)
    {
        if (!active) Active = true;
        litAt = Mathf.Min(litAt, Time.time); // un coup pendant la charge allume l'arc tout de suite
        dischargeUntil = Time.time + duration;
        if (playSound && playZap)
        {
            AudioClip zap = zapClips != null && zapClips.Length > 0 ? VfxLibrary.Pick(zapClips) : VfxLibrary.Pick(Library(l => l.electricZap));
            PlayOneShot(zap, zapVolume);
        }
        Pulse(true);
    }

    private bool Discharging => Time.time < dischargeUntil;

    private static T Library<T>(System.Func<VfxLibrary, T> get) where T : class
    {
        var lib = VfxLibrary.Instance;
        return lib != null ? get(lib) : null;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null || oneShotSource == null) return;
        oneShotSource.PlayOneShot(clip, volume);
    }

    private void Awake()
    {
        var lib = VfxLibrary.Instance;
        Material mat = lib != null ? lib.beam : null;
        core = MakeLine("ArcCore", mat, segments + 1);
        glow = MakeLine("ArcGlow", mat, segments + 1);
        branch = MakeLine("ArcBranch", mat, Mathf.Max(4, segments / 2));

        if (emitLight)
        {
            var go = new GameObject("ArcLight");
            go.transform.SetParent(transform, false);
            pointLight = go.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.shadows = LightShadows.None;
            pointLight.range = lightRange;
            pointLight.color = lib != null ? lib.electricGlow : new Color(0.3f, 0.66f, 1f);
            pointLight.enabled = false;
        }

        if (playSound)
        {
            if (loopSource == null) loopSource = MakeSource("ArcLoopAudio");
            // Une seconde source pour la charge et les décharges : elles ont leur propre volume, et
            // suivent le taser au lieu de rester là où le coup est parti.
            oneShotSource = MakeSource("ArcOneShotAudio");
        }
        HideAll();
    }

    private AudioSource MakeSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = soundMinDistance;
        src.maxDistance = Mathf.Max(soundMinDistance + 0.1f, soundMaxDistance);
        return src;
    }

    private void Start()
    {
        if (activeOnStart) Active = true;
    }

    private void OnDisable()
    {
        HideAll();
        if (loopSource != null) loopSource.Stop();
        if (oneShotSource != null) oneShotSource.Stop();
        active = false;
    }

    private LineRenderer MakeLine(string name, Material material, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return Vfx.NewLine(go, material, points);
    }

    private void LateUpdate()
    {
        if (!active) return;
        if (Time.time < litAt) return; // le condensateur charge encore

        // Le rythme vient du son quand il y en a un : l'impulsion visuelle tombe sur le claquement.
        bool pulseNow;
        if (loopSource != null && loopSource.isPlaying && loopSource.clip != null && !Discharging)
        {
            int index = Mathf.FloorToInt(loopSource.time * pulsesPerSecond);
            pulseNow = index != lastPulseIndex;
            lastPulseIndex = index;
        }
        else
        {
            float rate = pulsesPerSecond * (Discharging ? 2f : 1f);
            pulseNow = Time.time >= nextPulseTime;
            if (pulseNow) nextPulseTime = Time.time + 1f / Mathf.Max(1f, rate) * Random.Range(0.8f, 1.2f);
        }

        if (pulseNow) Pulse(Discharging);

        // Entre deux impulsions, l'arc retombe vite vers son plancher, comme la décharge d'un
        // condensateur ; il reste un fil tremblant, jamais une ligne immobile.
        float since = Time.time - lastPulseTime;
        float k = Mathf.Lerp(idleFloor, 1f, Mathf.Exp(-since / 0.03f)) * pulseStrength;
        ApplyBrightness(k);

        if (loopSource != null) loopSource.volume = loopVolume * (Discharging ? 1f : 0.8f);
    }

    private void Pulse(bool strong)
    {
        lastPulseTime = Time.time;
        pulseStrength = strong ? 1f : Random.Range(0.75f, 1f);

        GetEndpoints(out Vector3 a, out Vector3 b);
        Vector3 hint = transform.up;
        float j = jitter * (strong ? 1.6f : 1f);
        Vfx.WriteJaggedLine(core, a, b, segments + 1, j, hint);

        // Le halo suit le coeur à quelques millimètres près : il épaissit l'arc sans le dédoubler.
        for (int i = 0; i < core.positionCount; i++)
        {
            glow.SetPosition(i, core.GetPosition(i) + Random.insideUnitSphere * 0.002f);
        }

        float w = strong ? 1.7f : 1f;
        core.widthMultiplier = coreWidth * w;
        glow.widthMultiplier = glowWidth * w;
        core.enabled = glow.enabled = true;

        if (Random.value < (strong ? 0.9f : branchChance))
        {
            int from = Random.Range(1, core.positionCount - 1);
            Vector3 start = core.GetPosition(from);
            float len = Vector3.Distance(a, b) * Random.Range(0.4f, strong ? 1.6f : 0.9f);
            Vector3 end = start + (Random.onUnitSphere + transform.forward * 0.5f).normalized * len;
            Vfx.WriteJaggedLine(branch, start, end, branch.positionCount, 0.35f, hint);
            branch.widthMultiplier = coreWidth * 0.8f * w;
            branch.enabled = true;
        }
        else
        {
            branch.enabled = false;
        }

        if (pointLight != null)
        {
            pointLight.transform.position = (a + b) * 0.5f;
            pointLight.enabled = true;
        }

        if (strong && Random.value < 0.5f) Vfx.ElectricSpark(Random.value < 0.5f ? a : b, transform.forward);
    }

    private void ApplyBrightness(float k)
    {
        var lib = VfxLibrary.Instance;
        Color c = lib != null ? lib.electricCore : Color.white;
        Color g = lib != null ? lib.electricGlow : new Color(0.3f, 0.66f, 1f);
        core.startColor = core.endColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(k));
        glow.startColor = glow.endColor = new Color(g.r, g.g, g.b, Mathf.Clamp01(k * 0.55f));
        if (branch.enabled) branch.startColor = branch.endColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(k * 0.8f));
        if (pointLight != null) pointLight.intensity = lightIntensity * k * (Discharging ? 3f : 1f);
    }

    private void GetEndpoints(out Vector3 a, out Vector3 b)
    {
        a = electrodeA != null ? electrodeA.position : transform.TransformPoint(new Vector3(-defaultGap * 0.5f, 0f, 0f));
        b = electrodeB != null ? electrodeB.position : transform.TransformPoint(new Vector3(defaultGap * 0.5f, 0f, 0f));
    }

    private void HideAll()
    {
        if (core != null) core.enabled = false;
        if (glow != null) glow.enabled = false;
        if (branch != null) branch.enabled = false;
        if (pointLight != null) pointLight.enabled = false;
    }
}
