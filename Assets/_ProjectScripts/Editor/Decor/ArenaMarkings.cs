using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// La signalétique de la salle de test balistique (2026-10-27, bible § 13 et § 14 : « anciennes consignes, numéros de
/// test ») : ce qui dit, sans un mot de l'IA, qu'on se bat dans une salle d'essais de tir.
///
/// - au sol, peintes : les lignes de tir tous les 10 m depuis le poste A, leurs distances, les postes A et B, le
///   numéro de la salle ;
/// - sur les murs : le tunnel de tir, deux vieilles consignes, et très haut sur les murs de la salle, « B-01 » ;
/// - les voyants « TIR EN COURS » sur le tunnel, et leur lueur rouge.
///
/// Tout est plat, collé au sol ou aux murs, sans collider : rien ne change le jeu. Les textes sont les marquages de
/// Blender (MQ1, Tools/Blender/decor/marquages.py), peints par le shader du décor (peinture usée par plaques).
/// </summary>
public static class ArenaMarkings
{
    public const string RootName = "Signalétique";
    /// <summary>La ligne du poste A, d'où se comptent les distances (le point d'apparition A est à z = 2).</summary>
    private const float LineA = 2f;

    public static void Build(Transform decor, ArenaDecorBuilder.HallGeometry hall, DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject(RootName).transform;
        root.SetParent(decor, false);
        Material white = MarkingMaterial("Decor_Marquage_Blanc", DecorMaterials.BlancCasse, 0.45f);
        Material black = MarkingMaterial("Decor_Marquage_Noir", DecorMaterials.Noir, 0.3f);
        int texts = 0;

        // Les lignes de tir, d'un mur d'enceinte à l'autre, sous les blocs comme ailleurs (rien ne dépasse des murs :
        // vue de la tribune ou sur la carte du salon, une ligne ne sort pas de l'arène).
        Physics.SyncTransforms();
        int world = LayerMask.GetMask("Default");
        var lines = new DecorMeshes.Builder();
        for (int d = 0; d <= 60; d += 10)
        {
            float z = LineA + d;
            FloorStripe(lines, Enclosure(-1f, z, world).y, Enclosure(1f, z, world).y, z, d == 0 || d == 60 ? 0.16f : 0.08f);
        }
        GameObject lineObject = Flat(root, "Lignes de tir", bank.Add(lines.ToMesh("Marquage_Lignes", false)), white);

        // Au sol : les postes, les distances le long des deux grands murs, le numéro de la salle.
        texts += FloorText(root, "PosteA", new Vector3(0f, 0f, 3.6f), 0.9f, Vector3.forward, white) ? 1 : 0;
        texts += FloorText(root, "PosteB", new Vector3(0f, 0f, 56.2f), 0.9f, Vector3.back, white) ? 1 : 0;
        // Les distances, au bout de chaque ligne : 1,6 m avant le mur d'enceinte, ou avant le premier obstacle si un bloc
        // occupe cette place.
        for (int d = 10; d <= 60; d += 10)
        {
            float z = LineA + d + 0.55f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = Enclosure(side, z, world).x - side * 1.4f;
                if (Physics.CheckBox(new Vector3(x, 0.4f, z), new Vector3(0.9f, 0.3f, 0.35f), Quaternion.identity, world, QueryTriggerInteraction.Ignore)
                    && Physics.Raycast(new Vector3(side * 5f, 0.3f, z), Vector3.right * side, out RaycastHit hit, 40f, world, QueryTriggerInteraction.Ignore))
                    x = hit.point.x - side * 1.6f;
                texts += FloorText(root, $"M{d}", new Vector3(x, 0f, z), 0.45f, Vector3.forward, white) ? 1 : 0;
            }
        }
        texts += FloorText(root, "B01", new Vector3(0f, 0f, 14.6f), 2.4f, Vector3.forward, white) ? 1 : 0;
        texts += FloorText(root, "ZoneDeTir", new Vector3(-9f, 0f, 47f), 0.6f, Vector3.forward, white) ? 1 : 0;

        // Sur les murs du tunnel de tir (ses faces extérieures, au-dessus du soubassement), et deux vieilles consignes.
        texts += WallText(root, "Tunnel", new Vector3(-3.25f, 2.15f, 30f), Vector3.left, 0.36f, white) ? 1 : 0;
        texts += WallText(root, "Tunnel", new Vector3(3.25f, 2.15f, 30f), Vector3.right, 0.36f, white) ? 1 : 0;
        texts += WallText(root, "Humains", new Vector3(0f, 1.75f, 67.5f), Vector3.back, 0.2f, white) ? 1 : 0;
        texts += WallText(root, "Casque", new Vector3(0f, 1.75f, -4.5f), Vector3.forward, 0.2f, white) ? 1 : 0;
        texts += WallText(root, "Essai", new Vector3(32f, 2.2f, 31.5f), Vector3.left, 0.24f, white) ? 1 : 0;

        // Très haut sur les murs nord et sud de la salle : « B-01 », et ce qu'est la salle.
        foreach (Vector2 direction in new[] { Vector2.up, Vector2.down })
        {
            if (!HallSide(hall, direction, out Vector2 a, out Vector2 b, out Vector2 outward)) continue;
            Vector2 at = Vector2.Lerp(a, b, 0.3f);
            var inward = new Vector3(-outward.x, 0f, -outward.y);
            var basePoint = new Vector3(at.x, 0f, at.y);
            texts += WallText(root, "B01", basePoint + Vector3.up * 10.7f, inward, 2.4f, white) ? 1 : 0;
            texts += WallText(root, "Salle", basePoint + Vector3.up * 8.55f, inward, 0.46f, white) ? 1 : 0;
        }

        // Les voyants « TIR EN COURS » sur les quatre faces du tunnel, près du haut, et leur lueur.
        int beacons = 0;
        GameObject beacon = DecorImporter.Prefab("GY1");
        if (beacon != null)
        {
            foreach ((Vector3 at, Vector3 normal) in new[]
            {
                (new Vector3(-3.25f, 2.45f, 27f), Vector3.left), (new Vector3(3.25f, 2.45f, 33f), Vector3.right),
                (new Vector3(0f, 2.45f, 21.99f), Vector3.back), (new Vector3(0f, 2.45f, 38f), Vector3.forward),
            })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(beacon, root);
                go.name = "Voyant tir en cours";
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal, Vector3.up));
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true)) r.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask;
                var lamp = new GameObject("Lueur rouge").AddComponent<Light>();
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.localPosition = new Vector3(0f, 0.1f, 0.35f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.12f, 0.07f);
                lamp.range = 3.5f;
                lamp.intensity = 2.2f;
                lamp.shadows = LightShadows.None;
                lamp.lightmapBakeType = LightmapBakeType.Realtime;
                var data = lamp.gameObject.AddComponent<UniversalAdditionalLightData>();
                data.renderingLayers = ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask;
                beacons++;
            }
        }
        report.AppendLine($"Signalétique : 7 lignes de tir, {texts} marquages peints, {beacons} voyants « TIR EN COURS »" +
                          (beacon == null ? " (GY1 pas importé : Duel Arena > Arène > Importer les props de Blender)." : "."));
    }

    // ------------------------------------------------------------------
    // Les marquages
    // ------------------------------------------------------------------

    /// <summary>La peinture d'un marquage : la couleur donnée, usée par plaques (le support se voit à travers), sans
    /// ombre ni profondeur à elle (elle est collée à sa surface) ni contour.</summary>
    internal static Material MarkingMaterial(string name, Color color, float wear)
    {
        var material = new Material(Shader.Find("DuelArena/Decor"));
        material.SetFloat("_Espace", 0f);
        material.SetColor("_Base", color);
        material.SetFloat("_Rug", 0.7f);
        material.SetFloat("_Motif", 0f);
        material.SetFloat("_Nuages", 0f);
        material.SetVector("_Crasse", Vector4.zero);
        material.SetFloat("_EclatsArchi", 0f);
        material.SetFloat("_Decoupe", 0.5f);
        material.SetTexture("_MarquageTex", Texture2D.whiteTexture);
        material.SetColor("_CouleurMarquage", color);
        material.SetFloat("_UsureMarquage", wear);
        material.SetFloat("_Contour", 0f);
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetOverrideTag("RenderType", "TransparentCutout");
        Material saved = RobotChassisBuilder.SaveMaterial($"{DecorMaterials.Folder}/{name}.mat", material);
        // Sur le matériau enregistré : la recopie de SaveMaterial ne reprend pas les passes coupées.
        foreach (string pass in new[] { "ShadowCaster", "DepthOnly", "DepthNormals", DecorOutlineFeature.LightMode })
            saved.SetShaderPassEnabled(pass, false);
        return saved;
    }

    /// <summary>
    /// Le mur d'enceinte (les blocs sous la racine « MAP » du blocking) qui ferme l'arène du côté `side` (-1 : ouest, +1 :
    /// est) à la profondeur `z` : le x de sa face intérieure, puis de sa face extérieure. Le rayon part du milieu et traverse
    /// les autres blocs. Sans mur trouvé, 31,5 m.
    /// </summary>
    private static Vector2 Enclosure(float side, float z, int world)
    {
        var best = new Vector2(side * 30.5f, side * 31.5f);
        float nearest = float.MaxValue;
        var origin = new Vector3(0f, 0.3f, z);
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.right * side, 45f, world, QueryTriggerInteraction.Ignore))
        {
            Transform root = hit.collider.transform.root;
            if (root == null || root.name != "MAP" || hit.distance >= nearest) continue;
            nearest = hit.distance;
            // le rayon entre par la face intérieure du mur : la ligne court sous le mur jusqu'à sa face extérieure
            Bounds b = hit.collider.bounds;
            best = side > 0f ? new Vector2(b.min.x, b.max.x) : new Vector2(b.max.x, b.min.x);
        }
        return best;
    }

    /// <summary>Une bande peinte au sol, de x0 à x1 à la profondeur z, large de `width`.</summary>
    internal static void FloorStripe(DecorMeshes.Builder b, float x0, float x1, float z, float width)
    {
        const float y = 0.003f;
        b.Quad(new Vector3(x0, y, z - width * 0.5f), new Vector3(x1, y, z - width * 0.5f), new Vector3(x1, y, z + width * 0.5f),
               new Vector3(x0, y, z + width * 0.5f), Vector3.up);
    }

    /// <summary>Un texte peint au sol, lisible de qui regarde vers `readerUp` (son haut), de `cap` m de capitale.</summary>
    internal static bool FloorText(Transform parent, string key, Vector3 position, float cap, Vector3 readerUp, Material material)
    {
        Mesh mesh = DecorImporter.MeshOf("MQ1", key);
        if (mesh == null) return false;
        position.y = 0.004f;
        GameObject go = Flat(parent, key, mesh, material);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.up, readerUp));
        go.transform.localScale = Vector3.one * cap;
        return true;
    }

    /// <summary>Un texte peint sur un mur dont la face regarde vers `normal`, centré sur `position`.</summary>
    internal static bool WallText(Transform parent, string key, Vector3 position, Vector3 normal, float cap, Material material)
    {
        Mesh mesh = DecorImporter.MeshOf("MQ1", key);
        if (mesh == null) return false;
        GameObject go = Flat(parent, key, mesh, material);
        go.transform.SetPositionAndRotation(position + normal.normalized * 0.004f, Quaternion.LookRotation(normal, Vector3.up));
        go.transform.localScale = Vector3.one * cap;
        return true;
    }

    internal static GameObject Flat(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    /// <summary>Le pan du mur de la salle dont la normale sortante regarde le plus vers `direction` (x, z).</summary>
    private static bool HallSide(ArenaDecorBuilder.HallGeometry hall, Vector2 direction, out Vector2 a, out Vector2 b, out Vector2 outward)
    {
        Vector2[] p = hall.inner;
        float best = 0.9f;
        a = b = outward = default;
        bool found = false;
        for (int i = 0; i < p.Length; i++)
        {
            Vector2 o = DecorMeshes.OutwardOf(p, i);
            float score = Vector2.Dot(o, direction.normalized);
            if (score <= best || Vector2.Distance(p[i], p[(i + 1) % p.Length]) < 10f) continue;
            best = score;
            a = p[i];
            b = p[(i + 1) % p.Length];
            outward = o;
            found = true;
        }
        return found;
    }
}
