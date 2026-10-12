using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hôte invisible des effets : recycle les systèmes de particules, fait vivre les traceurs, les
/// arcs éphémères, les flashs lumineux et les traces d'impact. Créé à la première demande, gardé
/// entre les scènes (DontDestroyOnLoad), jamais posé à la main.
///
/// Catégorie C stricte (voir CLAUDE.md) : rien ici n'est relu par la simulation, rien n'a de
/// collider, et chaque machine fait vivre ses propres effets. Tout tourne en temps non mis à
/// l'échelle (unscaledTime) pour qu'un futur ralenti de killcam ne fige pas les retours.
/// </summary>
[DefaultExecutionOrder(1000)]
public class VfxRunner : MonoBehaviour
{
    private static VfxRunner instance;

    public static VfxRunner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("[VFX]");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<VfxRunner>();
            }
            return instance;
        }
    }

    public static Transform Root => Instance.transform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    // ------------------------------------------------------------------ effets de particules

    private struct Timed
    {
        public Vfx.PooledEffect effect;
        public float releaseAt;
    }

    private readonly List<Timed> timedEffects = new List<Timed>();

    public void Release(Vfx.PooledEffect effect, float after)
    {
        timedEffects.Add(new Timed { effect = effect, releaseAt = Time.unscaledTime + after });
    }

    // ------------------------------------------------------------------ flashs lumineux

    private class Flash
    {
        public Light light;
        public float start, duration, intensity, flicker;
    }

    private readonly List<Flash> flashes = new List<Flash>();

    public void LightFlash(Vector3 position, Color color, float intensity, float range, float duration, float flicker, int maxLights)
    {
        Flash flash = null;
        foreach (var f in flashes)
        {
            if (f.light != null && !f.light.enabled) { flash = f; break; }
        }
        if (flash == null && flashes.Count < Mathf.Max(1, maxLights))
        {
            var go = new GameObject("VfxLight");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.enabled = false;
            flash = new Flash { light = light };
            flashes.Add(flash);
        }
        if (flash == null)
        {
            // Toutes occupées : on recycle celle qui finit le plus tôt.
            flash = flashes[0];
            foreach (var f in flashes)
            {
                if (f.start + f.duration < flash.start + flash.duration) flash = f;
            }
        }
        if (flash.light == null) return;

        flash.light.transform.position = position;
        flash.light.color = color;
        flash.light.range = range;
        flash.light.intensity = intensity;
        flash.light.enabled = true;
        flash.start = Time.unscaledTime;
        flash.duration = Mathf.Max(0.01f, duration);
        flash.intensity = intensity;
        flash.flicker = flicker;
    }

    // ------------------------------------------------------------------ traceurs

    private class TracerState
    {
        public LineRenderer line;
        public Vector3 start, end;
        public float startTime, distance;
        public bool active;
    }

    private readonly List<TracerState> tracers = new List<TracerState>();

    // Une balle de 9 mm va à ~400 m/s : on la ralentit à peine pour qu'on la voie partir, sans
    // qu'elle paraisse arriver après l'impact (traversée de l'arène en ~0,2 s).
    private const float TracerSpeed = 320f;
    private const float TracerLength = 2.2f;

    public void Tracer(Vector3 start, Vector3 end, Material material, Color color)
    {
        TracerState tracer = null;
        foreach (var t in tracers)
        {
            if (!t.active) { tracer = t; break; }
        }
        if (tracer == null)
        {
            var go = new GameObject("VfxTracer");
            go.transform.SetParent(transform, false);
            var lr = Vfx.NewLine(go, material, 2);
            tracer = new TracerState { line = lr };
            tracers.Add(tracer);
        }

        tracer.start = start;
        tracer.end = end;
        tracer.distance = Vector3.Distance(start, end);
        tracer.startTime = Time.unscaledTime;
        tracer.active = true;
        tracer.line.startColor = new Color(color.r, color.g, color.b, 0.15f); // queue
        tracer.line.endColor = new Color(color.r, color.g, color.b, 0.95f);  // tête
        tracer.line.startWidth = 0.006f;
        tracer.line.endWidth = 0.02f;
        tracer.line.SetPosition(0, start);
        tracer.line.SetPosition(1, start);
        tracer.line.enabled = true;
    }

    // ------------------------------------------------------------------ arcs éphémères

    private class ArcBurst
    {
        public LineRenderer line;
        public Vector3 center, normal;
        public float radius, start, duration, nextRefresh, width;
        public Color core;
        public bool active;
    }

    private readonly List<ArcBurst> arcs = new List<ArcBurst>();
    private const int ArcPoints = 9;

    /// <summary>Arcs électriques qui rampent autour d'un point pendant <paramref name="duration"/> :
    /// décharge d'un taser sur un châssis, court-circuit d'un robot détruit.</summary>
    public void ArcBursts(Vector3 center, Vector3 normal, float radius, float duration, int count, Material material, Color core, float width)
    {
        for (int n = 0; n < count; n++)
        {
            ArcBurst arc = null;
            foreach (var a in arcs)
            {
                if (!a.active) { arc = a; break; }
            }
            if (arc == null)
            {
                if (arcs.Count >= 12) return;
                var go = new GameObject("VfxArc");
                go.transform.SetParent(transform, false);
                arc = new ArcBurst { line = Vfx.NewLine(go, material, ArcPoints) };
                arcs.Add(arc);
            }

            arc.center = center;
            arc.normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            arc.radius = radius;
            arc.start = Time.unscaledTime;
            arc.duration = duration * Random.Range(0.6f, 1f);
            arc.nextRefresh = 0f;
            arc.core = core;
            arc.width = width;
            arc.active = true;
            arc.line.enabled = true;
        }
    }

    // ------------------------------------------------------------------ traces d'impact

    private class Mark
    {
        public GameObject go;
        public MeshRenderer renderer;
        public float start, lifetime;
        public Color color;
        public bool active;
    }

    private readonly List<Mark> marks = new List<Mark>();
    private int nextMark;
    private MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public void ScorchMark(Vector3 point, Vector3 normal, float size, Material material, Color color, float lifetime, int maxMarks)
    {
        if (material == null || normal.sqrMagnitude < 0.0001f) return;
        block ??= new MaterialPropertyBlock();

        Mark mark;
        if (marks.Count < Mathf.Max(1, maxMarks))
        {
            var go = new GameObject("VfxScorch");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mark = new Mark { go = go, renderer = mr };
            marks.Add(mark);
        }
        else
        {
            mark = marks[nextMark % marks.Count];
            nextMark = (nextMark + 1) % marks.Count;
        }
        if (mark.go == null) return;

        Vector3 n = normal.normalized;
        // Décollée de 4 mm du mur, sinon elle scintille avec lui (z-fighting).
        mark.go.transform.SetPositionAndRotation(point + n * 0.004f, Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        mark.go.transform.localScale = Vector3.one * size;
        mark.start = Time.unscaledTime;
        mark.lifetime = lifetime;
        mark.color = color;
        mark.active = true;
        mark.go.SetActive(true);
        block.SetColor(BaseColorId, color);
        mark.renderer.SetPropertyBlock(block);
    }

    public void ClearMarks()
    {
        foreach (var m in marks)
        {
            m.active = false;
            if (m.go != null) m.go.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ boucle

    private void LateUpdate()
    {
        float now = Time.unscaledTime;

        for (int i = timedEffects.Count - 1; i >= 0; i--)
        {
            if (now < timedEffects[i].releaseAt) continue;
            Vfx.Return(timedEffects[i].effect);
            timedEffects.RemoveAt(i);
        }

        foreach (var f in flashes)
        {
            if (f.light == null || !f.light.enabled) continue;
            float t = (now - f.start) / f.duration;
            if (t >= 1f) { f.light.enabled = false; continue; }
            float k = (1f - t) * (1f - t);
            if (f.flicker > 0f) k *= 1f - f.flicker * Random.value;
            f.light.intensity = f.intensity * k;
        }

        foreach (var t in tracers)
        {
            if (!t.active) continue;
            float traveled = (now - t.startTime) * TracerSpeed;
            float head = Mathf.Min(traveled, t.distance);
            float tail = Mathf.Clamp(traveled - TracerLength, 0f, t.distance);
            if (tail >= t.distance - 0.001f)
            {
                t.active = false;
                t.line.enabled = false;
                continue;
            }
            Vector3 dir = t.distance > 0.0001f ? (t.end - t.start) / t.distance : Vector3.zero;
            t.line.SetPosition(0, t.start + dir * tail);
            t.line.SetPosition(1, t.start + dir * head);
        }

        foreach (var a in arcs)
        {
            if (!a.active) continue;
            float t = (now - a.start) / a.duration;
            if (t >= 1f) { a.active = false; a.line.enabled = false; continue; }
            if (now >= a.nextRefresh)
            {
                // Réécrit l'arc ~30 fois par seconde : c'est le saut de forme, pas un mouvement
                // fluide, qui fait lire « électricité ».
                a.nextRefresh = now + Random.Range(0.025f, 0.045f);
                RebuildArc(a);
            }
            float alpha = (1f - t) * Random.Range(0.55f, 1f);
            a.line.startColor = new Color(a.core.r, a.core.g, a.core.b, alpha);
            a.line.endColor = new Color(a.core.r, a.core.g, a.core.b, alpha * 0.6f);
        }

        if (block != null)
        {
            foreach (var m in marks)
            {
                if (!m.active || m.go == null) continue;
                float t = (now - m.start) / Mathf.Max(0.01f, m.lifetime);
                if (t >= 1f) { m.active = false; m.go.SetActive(false); continue; }
                // Pleine opacité aux deux premiers tiers, puis fondu.
                float fade = t < 0.66f ? 1f : 1f - (t - 0.66f) / 0.34f;
                block.SetColor(BaseColorId, new Color(m.color.r, m.color.g, m.color.b, m.color.a * fade));
                m.renderer.SetPropertyBlock(block);
            }
        }
    }

    private static void RebuildArc(ArcBurst a)
    {
        // Deux points pris dans l'hémisphère tourné vers l'extérieur de la surface, reliés par une
        // ligne brisée : l'arc « court » sur le châssis au lieu de traverser le corps.
        Vector3 p0 = a.center + HemispherePoint(a.normal) * a.radius;
        Vector3 p1 = a.center + HemispherePoint(a.normal) * a.radius;
        Vfx.WriteJaggedLine(a.line, p0, p1, ArcPoints, 0.22f, a.normal);
        a.line.widthMultiplier = a.width * Random.Range(0.7f, 1.3f);
    }

    private static Vector3 HemispherePoint(Vector3 normal)
    {
        Vector3 d = Random.onUnitSphere;
        if (Vector3.Dot(d, normal) < 0f) d = Vector3.Reflect(d, normal);
        return d;
    }
}
