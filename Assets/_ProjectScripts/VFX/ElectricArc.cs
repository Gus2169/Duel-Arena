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
/// Si une <see cref="AudioSource"/> en boucle est assignée (clip Taser_Idle_Loop, généré au même
/// rythme), les impulsions visuelles sont calées sur la position de lecture du son : l'image et
/// le claquement tombent ensemble.
///
/// Utilisation : poser ce composant sur l'objet du taser, renseigner les deux électrodes (sinon
/// deux points à ±1,5 cm de l'objet sur son axe X), puis piloter <see cref="Active"/> (arme
/// sortie) et <see cref="Discharge"/> (le coup). <see cref="Vfx.ElectricHit"/> fait l'impact sur
/// la cible. Catégorie C : purement visuel, aucune incidence sur la simulation ni les dégâts.
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
    [Tooltip("Impulsions par seconde. Doit correspondre au clip en boucle s'il y en a un (18 pour Taser_Idle_Loop).")]
    [SerializeField] private float pulsesPerSecond = 18f;
    [Tooltip("Luminosité entre deux impulsions (0 = l'arc s'éteint, 1 = jamais).")]
    [SerializeField, Range(0f, 1f)] private float idleFloor = 0.35f;
    [SerializeField, Range(0f, 1f)] private float branchChance = 0.35f;

    [Header("Lumière")]
    [SerializeField] private bool emitLight = true;
    [SerializeField] private float lightIntensity = 1.2f;
    [SerializeField] private float lightRange = 1.6f;

    [Header("Son (optionnel)")]
    [Tooltip("Source en boucle (clip Taser_Idle_Loop). Son volume suit l'état de l'arc.")]
    [SerializeField] private AudioSource loopSource;
    [SerializeField, Range(0f, 1f)] private float loopVolume = 0.5f;

    [Header("État")]
    [SerializeField] private bool activeOnStart;

    private LineRenderer core, glow, branch;
    private Light pointLight;
    private bool active;
    private float lastPulseTime = -1f, nextPulseTime;
    private int lastPulseIndex = -1;
    private float dischargeUntil;
    private float pulseStrength;

    /// <summary>Arc allumé (taser sorti). Éteint, plus rien n'est dessiné ni joué.</summary>
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            if (!active) HideAll();
            if (loopSource != null)
            {
                if (active && !loopSource.isPlaying) { loopSource.loop = true; loopSource.Play(); }
                if (!active) loopSource.Stop();
            }
        }
    }

    /// <summary>Décharge pleine puissance pendant <paramref name="duration"/> secondes : arcs plus
    /// larges, plus agités, branches à chaque impulsion. Pour le coup de taser.</summary>
    public void Discharge(float duration = 0.35f)
    {
        if (!active) Active = true;
        dischargeUntil = Time.time + duration;
        Pulse(true);
    }

    private bool Discharging => Time.time < dischargeUntil;

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
        HideAll();
    }

    private void Start()
    {
        if (activeOnStart) Active = true;
    }

    private void OnDisable()
    {
        HideAll();
        if (loopSource != null) loopSource.Stop();
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
