using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Le module mémoire d'un robot détruit, à l'Élimination confirmée (GDD § 11, 2026-11-02). Catégorie C : chaque machine
/// montre ceux que publie le serveur (RoundManager, KillConfirmed), à la couleur du robot détruit, qui flottent et
/// tournent sur place, sans collider (rien ne s'y arrête). Le préfab (Resources/ModuleMemoire) se construit par
/// Duel Arena > Modes > Construire le module mémoire (MemoryModuleBuilder) : on corrige le script, pas le préfab.
/// </summary>
public class MemoryModule : MonoBehaviour
{
    /// <summary>Le préfab, dans un dossier Resources (il voyage dans la build sans être posé dans une scène).</summary>
    public const string ResourceName = "ModuleMemoire";

    [Tooltip("Les pièces qui luisent à la couleur du robot détruit.")]
    [SerializeField] private Renderer[] glowRenderers = System.Array.Empty<Renderer>();
    [Tooltip("La lumière qu'il jette autour de lui, à la même couleur : on le repère dans la pénombre.")]
    [SerializeField] private Light glowLight;
    [Tooltip("Facteur de la lueur (émission) sur la couleur du robot. Au-delà de 1,2, la teinte sature : le module d'un robot orange luisait jaune, la couleur de l'autre (vu le 2026-11-02).")]
    [SerializeField] private float glowIntensity = 1.2f;
    [Tooltip("Il tourne sur lui-même de tant de degrés par seconde.")]
    [SerializeField] private float spinSpeed = 90f;
    [Tooltip("Il monte et descend de tant (m) autour de son point.")]
    [SerializeField] private float bobAmplitude = 0.05f;
    [Tooltip("Allers-retours par seconde.")]
    [SerializeField] private float bobFrequency = 0.8f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Vector3 anchor;
    private float phase;
    [System.NonSerialized] private MaterialPropertyBlock block;

    /// <summary>Le pose en `position` (son centre au repos), à la couleur `paint` ; `seed` décale son mouvement de
    /// celui des autres modules.</summary>
    public void Show(Vector3 position, Color paint, int seed)
    {
        anchor = position;
        phase = Mathf.Repeat(seed * 0.618034f, 1f);
        block ??= new MaterialPropertyBlock();
        foreach (Renderer glow in glowRenderers)
        {
            if (glow == null) continue;
            glow.GetPropertyBlock(block);
            block.SetColor(BaseColorId, paint);
            block.SetColor(EmissionColorId, paint * glowIntensity);
            glow.SetPropertyBlock(block);
        }
        if (glowLight != null) glowLight.color = paint;
        Animate();
    }

    private void Update() => Animate();

    private void Animate()
    {
        float t = Time.time + phase / Mathf.Max(0.01f, bobFrequency);
        transform.SetPositionAndRotation(anchor + Vector3.up * Bob(t, bobAmplitude, bobFrequency),
                                         Quaternion.Euler(0f, Mathf.Repeat(t * spinSpeed + phase * 360f, 360f), 0f));
    }

    /// <summary>De combien il est monté au-dessus de son point à l'instant `time`. Fonction pure.</summary>
    public static float Bob(float time, float amplitude, float frequency) => amplitude * Mathf.Sin(time * frequency * 2f * Mathf.PI);
}

/// <summary>
/// Les modules que montre cette machine, tenus à jour sur la liste du serveur, sondée à chaque image (RoundManager) :
/// un module publié apparaît, un module pris disparaît. Rien ne voyage de plus.
/// </summary>
public class MemoryModuleViews
{
    private readonly Dictionary<ushort, MemoryModule> shown = new Dictionary<ushort, MemoryModule>();
    private readonly List<ushort> gone = new List<ushort>();
    private GameObject prefab;
    private bool prefabLoaded;

    /// <summary>Les modules montrés.</summary>
    public int Count => shown.Count;

    public void Sync(NetworkList<KillConfirmed.Module> modules, Transform parent)
    {
        gone.Clear();
        foreach (KeyValuePair<ushort, MemoryModule> entry in shown)
        {
            if (!Contains(modules, entry.Key)) gone.Add(entry.Key);
        }
        foreach (ushort id in gone)
        {
            if (shown[id] != null) Object.Destroy(shown[id].gameObject);
            shown.Remove(id);
        }

        for (int i = 0; i < modules.Count; i++)
        {
            KillConfirmed.Module module = modules[i];
            if (shown.ContainsKey(module.id)) continue;
            MemoryModule view = Create(parent);
            if (view == null) return;
            view.Show(module.position, PaintOf(module.victimId), module.id);
            shown[module.id] = view;
        }
    }

    /// <summary>Retire tout ce qui est montré (la partie s'arrête).</summary>
    public void Clear()
    {
        foreach (KeyValuePair<ushort, MemoryModule> entry in shown)
        {
            if (entry.Value != null) Object.Destroy(entry.Value.gameObject);
        }
        shown.Clear();
    }

    private MemoryModule Create(Transform parent)
    {
        if (!prefabLoaded)
        {
            prefabLoaded = true;
            prefab = Resources.Load<GameObject>(MemoryModule.ResourceName);
            if (prefab == null) Debug.LogError($"[Modes] Le module mémoire (Resources/{MemoryModule.ResourceName}) est introuvable : Duel Arena > Modes > Construire le module mémoire.");
        }
        if (prefab == null) return null;
        GameObject instance = Object.Instantiate(prefab, parent);
        instance.name = MemoryModule.ResourceName;
        return instance.GetComponent<MemoryModule>();
    }

    private static bool Contains(NetworkList<KillConfirmed.Module> modules, ushort id)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i].id == id) return true;
        }
        return false;
    }

    /// <summary>La couleur du robot détruit (blanche s'il n'est plus là).</summary>
    private static Color PaintOf(ulong clientId)
    {
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || player.OwnerClientId != clientId) continue;
            if (player.TryGetComponent(out PlayerPaint paint) && paint.Paint.a > 0) return paint.Paint;
        }
        return new Color(0.94f, 0.95f, 0.96f);
    }
}
