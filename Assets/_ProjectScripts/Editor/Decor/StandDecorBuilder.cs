using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// L'habillage du stand de tir et de la Fosse (2026-10-28, demandé par l'utilisateur : « la même ambiance que l'arène »),
/// appelé par Duel Arena > Stand de tir > Construire le stand (ShootingRangeBuilder). Les salles deviennent des salles du
/// même complexe souterrain que la salle de test B-01 : le stand, la salle B-02 ; la Fosse, la salle B-03, un centre
/// d'entraînement rapproché (bible § 13) ; la salle de précision, la salle B-04, et sa cible sur son chariot.
///
/// Comme pour l'arène, **le jeu est fait de colliders** (les blocs du constructeur, qui font foi) et rien de ce qu'on voit
/// ne change le jeu : chaque bloc prend l'apparence qu'il demande (Look), tenue dans sa boîte ; ce qui s'y ajoute est plat
/// contre un mur ou le sol (marquages, panneaux acoustiques), ou hors de portée sous le plafond (poutres, rampes de
/// projecteurs, déflecteurs du stand, qui arrêtent les balles tirées vers le haut). Les matières sont celles de l'arène
/// (DecorMaterials), les props de Blender aussi (caisses, conteneurs, rampes, voyants, marquages au pochoir) ; la lumière
/// est dans StandLighting.
/// </summary>
public static class StandDecorBuilder
{
    /// <summary>La hauteur au-dessus de laquelle un mur de salle passe dans la pénombre (le calque de la salle).</summary>
    public const float HallWallSplit = 6f;

    /// <summary>Le stand (x, z), la Fosse et la salle de précision : leurs murs intérieurs.</summary>
    public static readonly Rect StandRoom = Rect.MinMaxRect(-1.5f, -9.5f, 21.5f, 65.5f);
    public static readonly Rect CourseRoom = Rect.MinMaxRect(22.5f, -9.5f, 61.5f, 49.5f);
    public static readonly Rect AimRoom = Rect.MinMaxRect(-7.5f, -9.5f, -2.5f, 55.5f);

    /// <summary>Ce qu'un bloc devient.</summary>
    public enum Kind
    {
        Box,
        Wall,
        Ceiling,
        Crate,
        Prop,
        Backstop,
        Divider,
        Canopy,
    }

    /// <summary>L'apparence d'un bloc : la matière de ses flancs et de son dessus (des recettes de DecorMaterials), son
    /// chanfrein, sa façon d'être habillé, et le prop de Blender qui le remplace s'il y en a un.</summary>
    public sealed class Look
    {
        public readonly string material, top, prop;
        public readonly float chamfer;
        public readonly Kind kind;

        private Look(string material, string top = null, float chamfer = 0.04f, Kind kind = Kind.Box, string prop = null)
        {
            this.material = material;
            this.top = top;
            this.chamfer = chamfer;
            this.kind = kind;
            this.prop = prop;
        }

        /// <summary>Le sol des deux salles : la dalle de béton sciée de l'arène.</summary>
        public static readonly Look Dalle = new("Decor_Dalle", null, 0f);
        /// <summary>Les murs de la salle : du béton banché jusqu'à 6 m, puis le béton sombre de la pénombre.</summary>
        public static readonly Look Mur = new("Decor_BetonBanche", kind: Kind.Wall);
        public static readonly Look Plafond = new("Decor_Plafond", null, 0f, Kind.Ceiling);
        public static readonly Look PareBalles = new("Decor_PareBalles", null, 0f, Kind.Backstop);
        public static readonly Look Cloison = new("Decor_Acoustique", null, 0f, Kind.Divider);
        public static readonly Look Auvent = new("Decor_Charpente", null, 0.02f, Kind.Canopy);
        public static readonly Look Barricade = new("Decor_BetonBarricade");
        /// <summary>Une caisse d'un mètre ou un conteneur de deux : son modèle de Blender (CA1, CA2, CO1).</summary>
        public static readonly Look Caisse = new("Decor_Caisse", null, 0.03f, Kind.Crate);
        public static readonly Look Etabli = new("Decor_Caisse", null, 0.02f, Kind.Prop, "ET1");
        public static readonly Look Etagere = new("Decor_Caisse", null, 0.02f, Kind.Prop, "RA1");
        public static readonly Look Beton = new("Decor_BetonBanche");
        public static readonly Look BetonSombre = new("Decor_BetonSombre");
        public static readonly Look Module = new("Decor_Module", null, 0.015f);
        public static readonly Look Gaine = new("Decor_Gaine", null, 0.02f);
        public static readonly Look Acier = new("Decor_AcierPeint", null, 0.015f);
        public static readonly Look Toit = new("Decor_BetonSombre", "Decor_Dalle", 0.03f);
        public static readonly Look Marche = new("Decor_AcierPeint", "Decor_ToleStriee", 0.01f);
        public static readonly Look Plateforme = new("Decor_AcierPeint", "Decor_ToleStriee", 0.015f);
        public static readonly Look GardeCorps = new("Decor_GardeCorps", null, 0.005f);
        public static readonly Look Poteau = new("Decor_AcierPeint", null, 0.01f);
        public static readonly Look Tableau = new("Decor_Charpente", null, 0.01f);
        /// <summary>Le comptoir de la salle de précision : des panneaux peints, un dessus d'acier.</summary>
        public static readonly Look Comptoir = new("Decor_Module", "Decor_AcierPeint", 0.015f);
    }

    /// <summary>Un bloc du jeu (son BoxCollider fait foi) et son apparence.</summary>
    public readonly struct Block
    {
        public readonly GameObject go;
        public readonly Look look;

        public Block(GameObject go, Look look)
        {
            this.go = go;
            this.look = look;
        }
    }

    /// <summary>Habille la scène du stand (déjà enregistrée : ses maillages, sa lumière et ses sondes vont à côté d'elle).
    /// Rend un compte rendu.</summary>
    public static string Dress(Scene scene, IReadOnlyList<Block> blocks, string bankPath)
    {
        var report = new StringBuilder();
        Dictionary<string, Material> materials = DecorMaterials.CreateAll();
        var decor = new GameObject(ArenaDecorBuilder.DecorRootName);
        SceneManager.MoveGameObjectToScene(decor, scene);
        var bank = new DecorMeshes.Bank(bankPath);

        DressBlocks(decor.transform, blocks, materials, bank, report);
        BuildHallStructure(decor.transform, materials, bank, report);
        BuildWallPanels(decor.transform, materials, bank, report);
        BuildMarkings(decor.transform, bank, report);
        DressAimRange(decor.transform, scene, materials, bank, report);
        StandLighting.Build(scene, materials, bank, report);

        bank.Save();
        EditorSceneManager.MarkSceneDirty(scene);
        report.Insert(0, $"Stand habillé : {bank.Count} maillages dans {bankPath}.\n");
        return report.ToString();
    }

    // ------------------------------------------------------------------
    // Les blocs
    // ------------------------------------------------------------------

    private static void DressBlocks(Transform decor, IReadOnlyList<Block> blocks, Dictionary<string, Material> materials,
                                    DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject("Blocs habillés").transform;
        root.SetParent(decor, false);
        int dressed = 0, props = 0;
        foreach (Block block in blocks)
        {
            var box = block.go.GetComponent<BoxCollider>();
            Look look = block.look;
            Transform t = block.go.transform;
            if (t.rotation != Quaternion.identity)
            {
                // Un bloc penché (le garde-corps d'un escalier) : une boîte orientée, sans chanfrein.
                var b = new DecorMeshes.Builder();
                Vector3 half = box.size * 0.5f;
                DecorMeshes.OrientedBox(b, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, half);
                GameObject go = Part(root, block.go.name, bank.Add(b.ToMesh($"Bloc_{dressed:000}_{block.go.name}")), materials[look.material], ArenaDecorBuilder.ArenaLayerMask);
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                dressed++;
                continue;
            }

            Bounds bounds = box.bounds;
            switch (look.kind)
            {
                case Kind.Crate:
                    if (ArenaDecorBuilder.PlaceCrate(root, t, bounds))
                    {
                        props++;
                        continue;
                    }
                    break;
                case Kind.Prop:
                    if (PlaceProp(root, block.go.name, look.prop, bounds))
                    {
                        props++;
                        continue;
                    }
                    break;
            }

            var mesh = new DecorMeshes.Builder();
            // Un bloc posé au sol descend juste sous le sol (on n'en dessine que ce qui se voit) ; un bloc qui flotte garde son
            // vide. Le pied se règle dans le monde, puis la boîte se centre sur l'objet.
            Vector3 worldMin = bounds.min, worldMax = bounds.max;
            if (look.kind is Kind.Box or Kind.Crate or Kind.Prop && worldMin.y > -0.005f && worldMin.y < 0.005f) worldMin.y = -look.chamfer - 0.01f;
            Vector3 center = (worldMin + worldMax) * 0.5f, min = worldMin - center, max = worldMax - center;
            string name = $"Bloc_{dressed:000}_{block.go.name}";
            switch (look.kind)
            {
                case Kind.Wall:
                    DressWall(root, block.go.name, bounds, look, materials, bank, ref dressed);
                    continue;
                case Kind.Ceiling:
                    mesh.Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                              new Vector3(min.x, min.y, max.z), Vector3.down);
                    GameObject ceiling = Part(root, block.go.name, bank.Add(mesh.ToMesh(name)), materials[look.material], ArenaDecorBuilder.HallLayerMask);
                    ceiling.transform.position = center;
                    ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    dressed++;
                    continue;
                case Kind.Backstop:
                    Backstop(mesh, min, max);
                    PartMulti(root, block.go.name, bank.Add(mesh.ToMesh(name)), center, materials["Decor_PareBalles"], materials["Decor_AcierPeint"]);
                    dressed++;
                    continue;
                case Kind.Divider:
                    Divider(mesh, min, max);
                    PartMulti(root, block.go.name, bank.Add(mesh.ToMesh(name)), center, materials["Decor_AcierPeint"], materials["Decor_Acoustique"]);
                    dressed++;
                    continue;
                case Kind.Canopy:
                    DecorMeshes.ChamferedBox(mesh, min, max, look.chamfer);
                    int columns = Mathf.FloorToInt((bounds.size.x - 3f) / 3f) + 1;
                    for (int column = 0; column < columns; column++)
                    {
                        float x = bounds.center.x + (column - (columns - 1) * 0.5f) * 3f;
                        foreach (float z in new[] { -2.2f, -5f, -7.8f })
                        {
                            var at = new Vector3(x, min.y - 0.004f, z) - new Vector3(0f, 0f, center.z);
                            at.x -= center.x;
                            mesh.Quad(at + new Vector3(-0.6f, 0f, -0.15f), at + new Vector3(0.6f, 0f, -0.15f), at + new Vector3(0.6f, 0f, 0.15f),
                                      at + new Vector3(-0.6f, 0f, 0.15f), Vector3.down, 0f, 1);
                        }
                    }
                    PartMulti(root, block.go.name, bank.Add(mesh.ToMesh(name)), center, materials[look.material], materials["Decor_LampeFroide"]);
                    dressed++;
                    continue;
            }

            DecorMeshes.ChamferedBox(mesh, min, max, look.chamfer, look.top != null ? 1 : 0);
            GameObject part = Part(root, block.go.name, bank.Add(mesh.ToMesh(name)), materials[look.material], ArenaDecorBuilder.ArenaLayerMask);
            part.transform.position = center;
            if (look.top != null) part.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials[look.material], materials[look.top] };
            dressed++;
        }
        report.AppendLine($"Blocs : {dressed} habillés, {props} caisses, conteneurs et props de Blender (leurs colliders inchangés).");
    }

    /// <summary>Un mur de salle : du béton banché à hauteur des robots (le calque de l'arène, que la rampe générale
    /// éclaire), le béton de la pénombre au-dessus de 6 m (le calque de la salle).</summary>
    private static void DressWall(Transform root, string name, Bounds bounds, Look look, Dictionary<string, Material> materials,
                                  DecorMeshes.Bank bank, ref int dressed)
    {
        float split = Mathf.Clamp(HallWallSplit, bounds.min.y, bounds.max.y);
        if (split > bounds.min.y + 0.01f)
        {
            // Posé au sol : il descend juste sous le sol.
            float bottom = bounds.min.y < 0.005f ? -look.chamfer - 0.01f : bounds.min.y;
            var low = new Bounds();
            low.SetMinMax(new Vector3(bounds.min.x, bottom, bounds.min.z), new Vector3(bounds.max.x, split, bounds.max.z));
            var b = new DecorMeshes.Builder();
            DecorMeshes.ChamferedBox(b, low.min - low.center, low.max - low.center, look.chamfer);
            GameObject go = Part(root, name, bank.Add(b.ToMesh($"Bloc_{dressed:000}_{name}")), materials[look.material], ArenaDecorBuilder.ArenaLayerMask);
            go.transform.position = low.center;
            dressed++;
        }
        if (bounds.max.y > split + 0.01f)
        {
            var high = new Bounds();
            high.SetMinMax(new Vector3(bounds.min.x, split, bounds.min.z), bounds.max);
            var b = new DecorMeshes.Builder();
            DecorMeshes.ChamferedBox(b, high.min - high.center, high.max - high.center, 0f);
            GameObject go = Part(root, name + " (haut)", bank.Add(b.ToMesh($"Bloc_{dressed:000}_{name}_haut")), materials["Decor_BetonSalle"], ArenaDecorBuilder.HallLayerMask);
            go.transform.position = high.center;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            dressed++;
        }
    }

    /// <summary>
    /// Le pare-balles du fond du stand, dans sa boîte : des lames d'acier noirci inclinées à 45° (le bord avant au ras de
    /// la boîte, là où les balles s'arrêtent), tenues par des montants, une tôle de fond, un chapeau. Sous-maillage 0 :
    /// les lames ; 1 : l'acier peint.
    /// </summary>
    private static void Backstop(DecorMeshes.Builder b, Vector3 min, Vector3 max)
    {
        var slope = new Vector3(0f, 1f, 1f).normalized;
        var normal = new Vector3(0f, 1f, -1f).normalized;
        float halfX = (max.x - min.x) * 0.5f, cx = (max.x + min.x) * 0.5f;
        const float plate = 0.8f;
        for (float y = min.y + 0.1f; y + plate * slope.y <= max.y - 0.12f; y += 0.4f)
        {
            Vector3 front = new Vector3(cx, y, min.z);
            DecorMeshes.OrientedBox(b, front + slope * (plate * 0.5f) - normal * 0.015f, Vector3.right, slope, normal, new Vector3(halfX, plate * 0.5f, 0.015f));
        }
        // La tôle de fond.
        DecorMeshes.OrientedBox(b, new Vector3(cx, (min.y + max.y) * 0.5f, max.z - 0.05f), Vector3.right, Vector3.up, Vector3.forward,
                                new Vector3(halfX, (max.y - min.y) * 0.5f, 0.05f));
        // Les montants, tous les 5,4 m, et le chapeau.
        int posts = Mathf.Max(2, Mathf.RoundToInt((max.x - min.x) / 5.4f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float x = Mathf.Lerp(min.x + 0.08f, max.x - 0.08f, i / (float)(posts - 1));
            DecorMeshes.OrientedBox(b, new Vector3(x, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                    new Vector3(0.08f, (max.y - min.y) * 0.5f, (max.z - min.z) * 0.5f), 1);
        }
        DecorMeshes.OrientedBox(b, new Vector3(cx, max.y - 0.06f, (min.z + max.z) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                new Vector3(halfX, 0.06f, (max.z - min.z) * 0.5f), 1);
    }

    /// <summary>Une cloison de poste de tir, dans sa boîte : un cadre d'acier peint (sous-maillage 0), un panneau de feutre
    /// acoustique (1), un pied plein.</summary>
    private static void Divider(DecorMeshes.Builder b, Vector3 min, Vector3 max)
    {
        Vector3 size = max - min;
        float cx = (min.x + max.x) * 0.5f;
        const float post = 0.06f, kick = 0.14f;
        foreach (float z in new[] { min.z + post * 0.5f, max.z - post * 0.5f })
            DecorMeshes.OrientedBox(b, new Vector3(cx, (min.y + max.y) * 0.5f, z), Vector3.right, Vector3.up, Vector3.forward,
                                    new Vector3(size.x * 0.5f, size.y * 0.5f, post * 0.5f));
        DecorMeshes.OrientedBox(b, new Vector3(cx, max.y - post * 0.5f, (min.z + max.z) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                new Vector3(size.x * 0.5f, post * 0.5f, size.z * 0.5f - post));
        DecorMeshes.OrientedBox(b, new Vector3(cx, min.y + kick * 0.5f, (min.z + max.z) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                new Vector3(size.x * 0.5f, kick * 0.5f, size.z * 0.5f - post));
        DecorMeshes.OrientedBox(b, new Vector3(cx, (min.y + kick + max.y - post) * 0.5f, (min.z + max.z) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                new Vector3(size.x * 0.4f, (size.y - kick - post) * 0.5f, size.z * 0.5f - post), 1);
    }

    /// <summary>
    /// Un prop de Blender à la place d'un bloc qui a ses dimensions (l'établi ET1, l'étagère RA1) : posé au milieu du bas
    /// du bloc, sa longueur (le X d'un prop) le long du grand côté du bloc, son dos (le -Z) contre le mur qui touche le
    /// bloc, s'il y en a un. Faux s'il n'est pas importé : le bloc est alors habillé comme les autres.
    /// </summary>
    private static bool PlaceProp(Transform parent, string name, string code, Bounds bounds)
    {
        GameObject prefab = code != null ? DecorImporter.Prefab(code) : null;
        if (prefab == null) return false;
        int world = LayerMask.GetMask("Default");
        // Le dos du prop regarde l'un des deux petits côtés du bloc : celui qu'un mur touche (une fine boîte juste
        // dehors le rencontre), sinon le premier.
        bool alongX = bounds.size.x >= bounds.size.z;
        Vector3[] sides = alongX ? new[] { Vector3.back, Vector3.forward } : new[] { Vector3.left, Vector3.right };
        Vector3 back = sides[0];
        foreach (Vector3 d in sides)
        {
            float extent = alongX ? bounds.extents.z : bounds.extents.x;
            Vector3 face = bounds.center + d * (extent + 0.03f);
            Vector3 half = alongX ? new Vector3(bounds.extents.x * 0.8f, bounds.extents.y * 0.8f, 0.02f)
                                  : new Vector3(0.02f, bounds.extents.y * 0.8f, bounds.extents.z * 0.8f);
            if (Physics.CheckBox(face, half, Quaternion.identity, world, QueryTriggerInteraction.Ignore))
            {
                back = d;
                break;
            }
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.LookRotation(-back, Vector3.up));
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true)) r.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask;
        return true;
    }

    // ------------------------------------------------------------------
    // Sous le plafond : les poutres, les déflecteurs du stand
    // ------------------------------------------------------------------

    private static void BuildHallStructure(Transform decor, Dictionary<string, Material> materials, DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject("Salle").transform;
        root.SetParent(decor, false);
        float top = ShootingRangeBuilder.CeilingHeight;

        // Des poutres en I tous les 6 m, d'un mur à l'autre, dans les deux salles. Hors de portée : sans collider, comme la
        // charpente de l'arène.
        var beams = new DecorMeshes.Builder();
        int count = 0;
        for (float z = StandRoom.yMin + 6.5f; z < StandRoom.yMax; z += 6f, count++)
            DecorMeshes.IBeam(beams, new Vector3(StandRoom.xMin, top - 0.45f, z), new Vector3(StandRoom.xMax, top - 0.45f, z), 0.8f, 0.35f);
        for (float z = CourseRoom.yMin + 3.5f; z < CourseRoom.yMax; z += 6f, count++)
            DecorMeshes.IBeam(beams, new Vector3(CourseRoom.xMin, top - 0.45f, z), new Vector3(CourseRoom.xMax, top - 0.45f, z), 0.8f, 0.35f);
        GameObject beamObject = Part(root, "Poutres", bank.Add(beams.ToMesh("Salle_Poutres")), materials["Decor_Charpente"], ArenaDecorBuilder.HallLayerMask);
        beamObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Les déflecteurs du stand, au-dessus du pas de tir : des tôles inclinées qui renvoient les balles perdues vers le
        // plafond. Ils sont dans la ligne de mire d'un tir levé : un collider chacun (une balle s'y arrête).
        var baffles = new GameObject("Déflecteurs").transform;
        baffles.SetParent(root, false);
        var slope = new Vector3(0f, 0.766f, 0.643f);   // 40° de la verticale, le bas vers les tireurs
        var normal = new Vector3(0f, 0.643f, -0.766f);
        var mesh = new DecorMeshes.Builder();
        var half = new Vector3((StandRoom.width - 0.2f) * 0.5f, 0.7f, 0.03f);
        DecorMeshes.OrientedBox(mesh, Vector3.zero, Vector3.right, slope, normal, half);
        Mesh baffleMesh = bank.Add(mesh.ToMesh("Stand_Deflecteur"));
        int baffleCount = 0;
        for (float z = 6f; z <= 60.1f; z += 6f)
        {
            GameObject go = Part(baffles, "Déflecteur", baffleMesh, materials["Decor_AcierPeint"], ArenaDecorBuilder.HallLayerMask);
            go.transform.SetPositionAndRotation(new Vector3(StandRoom.center.x, top - 1.55f, z), Quaternion.identity);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // Le collider suit la tôle : une boîte tournée comme elle.
            var holder = new GameObject("Collider");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localRotation = Quaternion.LookRotation(normal, slope);
            holder.AddComponent<BoxCollider>().size = new Vector3(half.x * 2f, half.y * 2f, half.z * 2f);
            baffleCount++;
        }
        report.AppendLine($"Salle : {count} poutres, {baffleCount} déflecteurs au-dessus du pas de tir.");
    }

    /// <summary>Les panneaux acoustiques des grands murs du stand et de la salle de précision, entre les pilastres, à hauteur
    /// de tir : 1,5 cm sur le mur (dans la marge d'un collider : pas le leur).</summary>
    private static void BuildWallPanels(Transform decor, Dictionary<string, Material> materials, DecorMeshes.Bank bank, StringBuilder report)
    {
        var b = new DecorMeshes.Builder();
        int panels = 0;
        // Le stand : ses pilastres (1 m), ses deux portes près du poste de tir (la salle de précision, la Fosse), le pare-balles.
        var stand = new List<(float, float)> { (-8.1f, -3.9f) };
        for (float pz = 6f; pz <= 62f; pz += 8f) stand.Add((pz - 0.65f, pz + 0.65f));
        stand.Add((63.6f, 99f));
        panels += PanelRun(b, StandRoom.xMin, 1f, StandRoom.yMin + 0.6f, stand);
        panels += PanelRun(b, StandRoom.xMax, -1f, StandRoom.yMin + 0.6f, stand);
        // La salle de précision : ses pilastres (80 cm), la place de ses grands textes, sa porte, le pare-balles.
        var left = new List<(float, float)>();
        for (float pz = 6f; pz <= 46f; pz += 8f) left.Add((pz - 0.55f, pz + 0.55f));
        left.Add((53.6f, 99f));
        var right = new List<(float, float)>(left) { (-8.1f, -3.9f), (AimWallTextRight - 2.2f, AimWallTextRight + 2.2f) };
        left.Add((AimWallTextLeft - 2.2f, AimWallTextLeft + 2.2f));
        left.Sort();
        right.Sort();
        panels += PanelRun(b, AimRoom.xMin, 1f, AimRoom.yMin + 0.6f, left);
        panels += PanelRun(b, AimRoom.xMax, -1f, AimRoom.yMin + 0.6f, right);
        GameObject go = Part(decor, "Panneaux acoustiques", bank.Add(b.ToMesh("Stand_Panneaux")), materials["Decor_Acoustique"], ArenaDecorBuilder.ArenaLayerMask);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        report.AppendLine($"Murs : {panels} panneaux acoustiques.");
    }

    /// <summary>Une rangée de panneaux sur le mur x = `x` (sa face regarde vers `inward`), de `z0` jusqu'au bout, coupée par
    /// les intervalles `stops` (triés) ; un panneau fait au moins 1,5 m.</summary>
    private static int PanelRun(DecorMeshes.Builder b, float x, float inward, float z0, List<(float, float)> stops)
    {
        int panels = 0;
        foreach ((float a, float c) in stops)
        {
            if (a - z0 > 1.5f)
            {
                DecorMeshes.OrientedBox(b, new Vector3(x + inward * 0.0075f, 2.45f, (z0 + a) * 0.5f), Vector3.forward, Vector3.up, Vector3.right,
                                        new Vector3((a - z0) * 0.5f, 1.15f, 0.0075f));
                panels++;
            }
            z0 = Mathf.Max(z0, c);
        }
        return panels;
    }

    /// <summary>Le milieu (z) des grands textes « B-04 » de la salle de précision, sur son mur de gauche et de droite.</summary>
    private const float AimWallTextLeft = 10f, AimWallTextRight = 26f;

    // ------------------------------------------------------------------
    // La signalétique
    // ------------------------------------------------------------------

    /// <summary>
    /// Ce qui dit, sans un mot de l'IA, qu'on est au stand de tir et dans la Fosse (bible § 13 et § 14 : « anciennes
    /// consignes, numéros de test ») : au stand, la ligne de tir et ses postes numérotés, les distances tous les 5 m, la
    /// distance de chaque mannequin, « B-02 · STAND DE TIR », les voyants « TIR EN COURS » au-dessus du pare-balles et
    /// deux vieilles consignes ; dans la Fosse, le départ et l'arrivée, des flèches au sol le long du parcours, « FRANCHIR »
    /// et « ACCROUPI » devant ce qui les demande, le nom des pièces, « B-03 · ZONE D'ENTRAÎNEMENT RAPPROCHÉ ». Les textes
    /// sont des propositions de Claude, à relire par l'utilisateur.
    /// </summary>
    private static void BuildMarkings(Transform decor, DecorMeshes.Bank bank, StringBuilder report)
    {
        var root = new GameObject("Signalétique").transform;
        root.SetParent(decor, false);
        Material white = ArenaMarkings.MarkingMaterial("Decor_Marquage_Blanc", DecorMaterials.BlancCasse, 0.45f);
        int texts = 0;

        // ---- Le stand : la ligne de tir, les distances.
        var lines = new DecorMeshes.Builder();
        ArenaMarkings.FloorStripe(lines, StandRoom.xMin, StandRoom.xMax, 0f, 0.16f);
        for (int d = 5; d <= 60; d += 5)
            ArenaMarkings.FloorStripe(lines, StandRoom.xMin + 0.6f, StandRoom.xMax - 0.6f, d, d % 10 == 0 ? 0.08f : 0.04f);
        // Le départ et l'arrivée de la Fosse.
        Stripe(lines, new Vector3(30f, 0f, CourseRoom.yMin), new Vector3(30f, 0f, -3f), 0.14f);
        Stripe(lines, new Vector3(CourseRoom.xMin, 0f, -3f), new Vector3(29.8f, 0f, -3f), 0.14f);
        ArenaMarkings.Flat(root, "Lignes", bank.Add(lines.ToMesh("Marquage_Lignes", false)), white);

        // La salle de précision : sa ligne de tir derrière le comptoir, et les distances du pupitre.
        ArenaMarkings.FloorStripe(lines, AimRoom.xMin, AimRoom.xMax, -0.1f, 0.12f);
        foreach (int d in AimRange.Distances)
            ArenaMarkings.FloorStripe(lines, AimRoom.xMin + 0.5f, AimRoom.xMax - 0.5f, d, 0.05f);

        float middle = StandRoom.center.x;
        foreach (float x in new[] { StandRoom.xMin + 1.3f, StandRoom.xMax - 1.3f })
            for (int d = 5; d <= 60; d += 5)
                texts += Floor(root, $"M{d}", new Vector3(x, 0f, d + 0.6f), 0.42f, Vector3.forward, white);
        for (int i = 0; i < ShootingRangeBuilder.Posts.Length; i++)
            texts += Floor(root, $"N{i + 1}", new Vector3(ShootingRangeBuilder.Posts[i], 0f, -0.75f), 0.34f, Vector3.forward, white);
        texts += Floor(root, "LigneDeTir", new Vector3(middle, 0f, 1.2f), 0.26f, Vector3.forward, white);
        foreach (Vector2 p in ShootingRangeBuilder.OpenDummies)
            texts += Floor(root, $"M{p.y:0}", new Vector3(p.x, 0f, p.y - 1.15f), 0.3f, Vector3.forward, white);
        foreach (Vector2 p in ShootingRangeBuilder.CoveredDummies)
            texts += Floor(root, $"M{p.y:0}", new Vector3(p.x, 0f, p.y - 1.75f), 0.3f, Vector3.forward, white);

        // Le nom de la salle, haut sur les grands murs ; le pare-balles ; les consignes du coin d'attente.
        texts += Wall(root, "B02", new Vector3(StandRoom.xMin, 8.1f, 18f), Vector3.right, 1.6f, white);
        texts += Wall(root, "StandDeTir", new Vector3(StandRoom.xMin, 6.7f, 18f), Vector3.right, 0.42f, white);
        texts += Wall(root, "B02", new Vector3(StandRoom.xMax, 8.1f, 34f), Vector3.left, 1.6f, white);
        texts += Wall(root, "StandDeTir", new Vector3(StandRoom.xMax, 6.7f, 34f), Vector3.left, 0.42f, white);
        texts += Wall(root, "PareBalles", new Vector3(middle, 7.1f, StandRoom.yMax), Vector3.back, 0.5f, white);
        texts += Wall(root, "Douilles", new Vector3(2f, 2.6f, StandRoom.yMin), Vector3.forward, 0.2f, white);
        texts += Wall(root, "Casque", new Vector3(8f, 2.4f, StandRoom.yMin), Vector3.forward, 0.2f, white);
        texts += Wall(root, "Riposte", new Vector3(14f, 2.4f, StandRoom.yMin), Vector3.forward, 0.2f, white);
        // La porte de la Fosse, à droite du poste ; celle de la salle de précision, à gauche.
        texts += Wall(root, "LaFosse", new Vector3(StandRoom.xMax, 3.45f, -6f), Vector3.left, 0.3f, white);
        texts += Arrow(root, new Vector3(StandRoom.xMax - 2.1f, 0f, -6f), Vector3.right, 0.7f, white);
        texts += Floor(root, "LaFosse", new Vector3(StandRoom.xMax - 4.2f, 0f, -6f), 0.36f, Vector3.right, white);
        texts += Wall(root, "SallePrecision", new Vector3(StandRoom.xMin, 3.45f, -6f), Vector3.right, 0.24f, white);
        texts += Arrow(root, new Vector3(StandRoom.xMin + 2.1f, 0f, -6f), Vector3.left, 0.7f, white);
        texts += Floor(root, "Precision", new Vector3(StandRoom.xMin + 4.4f, 0f, -6f), 0.32f, Vector3.left, white);

        // ---- La salle de précision : les distances, le nom de la salle, la sortie, une consigne.
        foreach (int d in AimRange.Distances)
            texts += Floor(root, $"M{d}", new Vector3(AimRoom.xMin + 1.1f, 0f, d + 0.6f), 0.36f, Vector3.forward, white);
        texts += Floor(root, "LigneDeTir", new Vector3(ShootingRangeBuilder.AimLaneX, 0f, 1.5f), 0.22f, Vector3.forward, white);
        texts += Wall(root, "B04", new Vector3(AimRoom.xMin, 3.2f, AimWallTextLeft), Vector3.right, 0.9f, white);
        texts += Wall(root, "SallePrecision", new Vector3(AimRoom.xMin, 2.2f, AimWallTextLeft), Vector3.right, 0.22f, white);
        texts += Wall(root, "B04", new Vector3(AimRoom.xMax, 3.2f, AimWallTextRight), Vector3.left, 0.9f, white);
        texts += Wall(root, "SallePrecision", new Vector3(AimRoom.xMax, 2.2f, AimWallTextRight), Vector3.left, 0.22f, white);
        texts += Wall(root, "StandDeTir", new Vector3(AimRoom.xMax, 3.45f, -6f), Vector3.left, 0.24f, white);
        texts += Wall(root, "Consigne", new Vector3(ShootingRangeBuilder.AimLaneX, 2.5f, AimRoom.yMin), Vector3.forward, 0.2f, white);

        // ---- La Fosse : le départ, l'arrivée, le tableau, les consignes.
        texts += Floor(root, "Depart", new Vector3(28.4f, 0f, -6.25f), 0.32f, Vector3.right, white);
        texts += Floor(root, "Arrivee", new Vector3(26.15f, 0f, -2.42f), 0.26f, Vector3.back, white);
        texts += Wall(root, "LaFosse", new Vector3(26.25f, 4.15f, CourseRoom.yMin), Vector3.forward, 0.55f, white);
        texts += Wall(root, "Civils", new Vector3(33.2f, 2.2f, CourseRoom.yMin), Vector3.forward, 0.2f, white);
        texts += Floor(root, "Franchir", new Vector3(40.75f, 0f, 0f), 0.3f, Vector3.right, white);
        texts += Floor(root, "Franchir", new Vector3(26.15f, 0f, -0.42f), 0.24f, Vector3.back, white);
        texts += Floor(root, "Accroupi", new Vector3(57.85f, 0f, 11.9f), 0.32f, Vector3.forward, white);
        texts += Wall(root, "Tete", new Vector3(57.85f, 1.75f, 13f), Vector3.back, 0.14f, white);
        // Le nom des pièces et de la salle.
        texts += Wall(root, "Quai", new Vector3(CourseRoom.xMax, 2.6f, 0f), Vector3.left, 0.32f, white);
        texts += Wall(root, "Atelier", new Vector3(36f, 2.35f, 7.8f), Vector3.back, 0.3f, white);
        texts += Wall(root, "Reserve", new Vector3(38.2f, 2.62f, 10f), Vector3.right, 0.17f, white);
        texts += Wall(root, "Bureaux", new Vector3(35.8f, 2.43f, 21.8f), Vector3.back, 0.15f, white);
        texts += Wall(root, "B03", new Vector3(CourseRoom.xMax, 7.9f, 21f), Vector3.left, 1.6f, white);
        texts += Wall(root, "Entrainement", new Vector3(CourseRoom.xMax, 6.6f, 21f), Vector3.left, 0.3f, white);
        texts += Wall(root, "B03", new Vector3(41f, 7.9f, CourseRoom.yMax), Vector3.back, 1.6f, white);
        texts += Wall(root, "Entrainement", new Vector3(41f, 6.6f, CourseRoom.yMax), Vector3.back, 0.3f, white);
        // Les flèches du parcours (la pointe vers où l'on va).
        foreach ((Vector3 at, Vector3 towards) in new[]
        {
            (new Vector3(32.6f, 0f, -6f), Vector3.right), (new Vector3(58.9f, 0f, 3.1f), Vector3.forward),
            (new Vector3(56.6f, 0f, 19.2f), Vector3.left), (new Vector3(44f, 0f, 14f), Vector3.forward),
            (new Vector3(40f, 0f, 16f), Vector3.back), (new Vector3(46f, 0f, 37f), Vector3.right),
            (new Vector3(57.4f, 0f, 35.6f), Vector3.forward), (new Vector3(58.6f, 0f, 42.4f), Vector3.left),
            (new Vector3(40f, 3f, 32.4f), Vector3.left), (new Vector3(29.4f, 3f, 30.8f), Vector3.left),
            (new Vector3(26.2f, 0f, 26f), Vector3.back), (new Vector3(28.2f, 0f, 18f), Vector3.back),
            (new Vector3(24.2f, 0f, 11f), Vector3.back), (new Vector3(28.2f, 0f, 4.2f), Vector3.back),
        })
            texts += Arrow(root, at, towards, 0.6f, white);

        // Les voyants « TIR EN COURS » : au-dessus des pare-balles, et au départ de la Fosse.
        int beacons = 0;
        GameObject beacon = DecorImporter.Prefab("GY1");
        if (beacon != null)
        {
            foreach ((Vector3 at, Vector3 n) in new[]
            {
                (new Vector3(StandRoom.center.x - 7f, 6.9f, StandRoom.yMax), Vector3.back), (new Vector3(StandRoom.center.x + 7f, 6.9f, StandRoom.yMax), Vector3.back),
                (new Vector3(ShootingRangeBuilder.AimLaneX + 1.4f, 4.45f, AimRoom.yMax), Vector3.back),
                (new Vector3(29.2f, 3.2f, CourseRoom.yMin), Vector3.forward), (new Vector3(30.2f, 2.6f, -1.6f), Vector3.right),
            })
            {
                PlaceBeacon(root, beacon, at, n);
                beacons++;
            }
        }
        report.AppendLine($"Signalétique : {texts} marquages peints, {beacons} voyants « TIR EN COURS »" +
                          (beacon == null ? " (GY1 pas importé : Duel Arena > Arène > Importer les props de Blender)." : "."));
    }

    /// <summary>Un texte peint au sol, à la hauteur de `position` (le sol, le toit, la passerelle), lisible de qui
    /// regarde vers `readerUp`. Rend 1 s'il est posé.</summary>
    private static int Floor(Transform parent, string key, Vector3 position, float cap, Vector3 readerUp, Material material)
    {
        Mesh mesh = DecorImporter.MeshOf("MQ1", key);
        if (mesh == null) return 0;
        GameObject go = ArenaMarkings.Flat(parent, key, mesh, material);
        go.transform.SetPositionAndRotation(new Vector3(position.x, position.y + 0.004f, position.z), Quaternion.LookRotation(Vector3.up, readerUp));
        go.transform.localScale = Vector3.one * cap;
        return 1;
    }

    private static int Wall(Transform parent, string key, Vector3 position, Vector3 normal, float cap, Material material)
        => ArenaMarkings.WallText(parent, key, position, normal, cap, material) ? 1 : 0;

    /// <summary>Une flèche peinte au sol, la pointe vers `towards` (la flèche de MQ1 pointe vers la droite du lecteur).</summary>
    private static int Arrow(Transform parent, Vector3 position, Vector3 towards, float size, Material material)
        => Floor(parent, "Fleche", position, size, new Vector3(-towards.z, 0f, towards.x), material);

    /// <summary>Une bande peinte au sol de `a` à `c`, large de `width`.</summary>
    private static void Stripe(DecorMeshes.Builder b, Vector3 a, Vector3 c, float width)
    {
        Vector3 along = (c - a).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, along) * (width * 0.5f);
        const float y = 0.003f;
        a.y = c.y = y;
        b.Quad(a - side, c - side, c + side, a + side, Vector3.up);
    }

    /// <summary>Un voyant « TIR EN COURS » (GY1) contre un mur, et sa lueur rouge (comme ArenaMarkings).</summary>
    private static void PlaceBeacon(Transform parent, GameObject prefab, Vector3 at, Vector3 normal)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = "Voyant tir en cours";
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal, Vector3.up));
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true)) r.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask;
        Light lamp = ArenaLighting.NewLight(go.transform, "Lueur rouge", LightType.Point);
        lamp.transform.localPosition = new Vector3(0f, 0.1f, 0.35f);
        lamp.color = new Color(1f, 0.12f, 0.07f);
        lamp.range = 3.5f;
        lamp.intensity = 2.2f;
        lamp.shadows = LightShadows.None;
        ArenaLighting.SetLayers(lamp, ArenaDecorBuilder.ArenaLayerMask | ArenaDecorBuilder.HallLayerMask);
    }

    // ------------------------------------------------------------------
    // La salle de précision : le chariot de la cible, son rail, le pupitre
    // ------------------------------------------------------------------

    private const string TargetMaterialPath = "Assets/_ProjectArena/Stand/CiblePrecision.mat";
    private const string ScreenMaterialPath = "Assets/_ProjectArena/Stand/CiblePrecisionEcran.mat";
    private const string BeamMaterialPath = "Assets/_ProjectArena/Stand/CiblePrecisionFaisceau.mat";
    private const string ButtonMaterialPath = "Assets/_ProjectArena/Stand/PupitreBouton.mat";

    /// <summary>Le pied de l'écran holographique, sur le comptoir derrière le pupitre (x, z) : à 32° à gauche de la ligne de
    /// visée du tireur, à 1,4 m de son œil. Son écran (0,52 m, la cible de près) reste dans le champ sans cacher la cible,
    /// même à 5 m, où elle prend 19° de part et d'autre.</summary>
    public static readonly Vector2 HologramFoot = new(-5.7f, 0.64f);
    public const float HologramSize = 0.52f;

    /// <summary>
    /// Habille la salle de précision (AimRange, posé par ShootingRangeBuilder) : le chariot (son socle, son mât, le disque de
    /// la cible : sa face imprimée, qu'AimRange peint, son dos et son chant d'acier), qui roule (ni statique ni lumière cuite :
    /// les sondes de lumière l'éclairent) ; le rail au sol ; le pupitre, une plaque d'acier posée sur le comptoir, et ses
    /// boutons (leur fond, dont AimRange règle la couleur, et leur texte au pochoir) ; derrière lui, l'écran holographique
    /// (demandé par l'utilisateur : « un peu comme une télé sur la table ») : un projecteur, sa réglette, la lumière qui
    /// monte, et l'écran de lumière (DuelArena/Hologramme) tourné vers l'œil du tireur, qui montre la cible de près.
    /// </summary>
    private static void DressAimRange(Transform decor, Scene scene, Dictionary<string, Material> materials, DecorMeshes.Bank bank, StringBuilder report)
    {
        AimRange aim = ArenaDecorBuilder.FindInScene<AimRange>(scene);
        if (aim == null) return;
        var so = new SerializedObject(aim);
        var carrier = so.FindProperty("carrier").objectReferenceValue as Transform;
        var board = so.FindProperty("board").objectReferenceValue as Transform;
        var console = so.FindProperty("console").objectReferenceValue as Transform;
        Material steel = materials["Decor_AcierPeint"], dark = materials["Decor_Charpente"];

        // Le chariot : le socle et le mât prennent la forme de leur collider.
        int parts = 0;
        foreach (Transform part in carrier)
        {
            var box = part.GetComponent<BoxCollider>();
            if (box == null) continue;
            var b = new DecorMeshes.Builder();
            DecorMeshes.ChamferedBox(b, -box.size * 0.5f, box.size * 0.5f, 0.01f);
            MovingPart(part, "Habillage", bank.Add(b.ToMesh($"Precision_{part.name}")), steel);
            parts++;
        }
        // Le disque, aux sommets de son collider (le prisme du constructeur) : sa face imprimée 0,5 mm dedans (le premier
        // matériau ; ses uv en mètres, la texture du carré du disque par le pavage du matériau), son dos et son chant d'acier.
        int segments = ShootingRangeBuilder.TargetSegments;
        float radius = AimRange.BoardRadius, depth = AimRange.BoardThickness * 0.5f;
        Vector3 Edge(int i, float z)
        {
            float a = i * Mathf.PI * 2f / segments;
            return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z);
        }
        var disc = new DecorMeshes.Builder();
        for (int i = 0; i < segments; i++)
        {
            int j = i + 1;
            disc.Triangle(new Vector3(0f, 0f, -depth + 0.0005f), Edge(i, -depth + 0.0005f), Edge(j, -depth + 0.0005f), Vector3.back);
            disc.Triangle(new Vector3(0f, 0f, depth), Edge(j, depth), Edge(i, depth), Vector3.forward, 0f, 1);
            disc.Quad(Edge(i, -depth), Edge(j, -depth), Edge(j, depth), Edge(i, depth), Edge(i, 0f) + Edge(j, 0f), 1f, 1);
        }
        Material targetMaterial = TargetMaterial(TargetMaterialPath, "Universal Render Pipeline/Lit", Color.white,
                                                 Vector2.one / AimRange.BoardDiameter, new Vector2(0.5f, 0.5f));
        GameObject discObject = MovingPart(board, "Disque", bank.Add(disc.ToMesh("Precision_Disque", false)), targetMaterial);
        Renderer face = discObject.GetComponent<MeshRenderer>();
        face.sharedMaterials = new[] { targetMaterial, dark };
        parts++;

        // Le rail : deux fers plats d'un centimètre, sous le socle, de la ligne de tir au pare-balles.
        var root = new GameObject("Salle de précision").transform;
        root.SetParent(decor, false);
        var rail = new DecorMeshes.Builder();
        float z0 = 1.2f, z1 = AimRoom.yMax - 1.6f;
        foreach (float dx in new[] { -0.16f, 0.16f })
            DecorMeshes.OrientedBox(rail, new Vector3(ShootingRangeBuilder.AimLaneX + dx, 0.005f, (z0 + z1) * 0.5f), Vector3.right, Vector3.up, Vector3.forward,
                                    new Vector3(0.015f, 0.005f, (z1 - z0) * 0.5f));
        GameObject railObject = Part(root, "Rail", bank.Add(rail.ToMesh("Precision_Rail")), steel, ArenaDecorBuilder.ArenaLayerMask);
        railObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Le pupitre : sa plaque (1 cm sur le comptoir), ses boutons et leurs textes.
        var plate = new DecorMeshes.Builder();
        Vector2 size = AimRange.ConsoleSize;
        DecorMeshes.ChamferedBox(plate, new Vector3(-size.x * 0.5f, -0.005f, -size.y * 0.5f), new Vector3(size.x * 0.5f, 0.005f, size.y * 0.5f), 0.004f);
        GameObject plateObject = Part(root, "Pupitre", bank.Add(plate.ToMesh("Precision_Pupitre")), dark, ArenaDecorBuilder.ArenaLayerMask);
        plateObject.transform.position = new Vector3(console.position.x, ShootingRangeBuilder.CounterHeight + 0.005f, console.position.z);

        Material buttonMaterial = TargetMaterial(ButtonMaterialPath, "Universal Render Pipeline/Unlit", Color.white);
        Material label = ArenaMarkings.MarkingMaterial("Decor_Marquage_Pupitre", DecorMaterials.BlancCasse, 0.05f);
        var buttons = new Renderer[AimRange.ButtonCount];
        int labels = 0;
        for (int i = 0; i < AimRange.ButtonCount; i++)
        {
            Rect r = AimRange.ButtonRect(i);
            buttons[i] = FlatQuad(console, i == AimRange.ClearButton ? "Bouton effacer" : $"Bouton {AimRange.Distances[i]} m",
                                  new Vector3(r.center.x, r.center.y, -0.002f), Quaternion.identity, r.size, buttonMaterial);
            string key = i == AimRange.ClearButton ? "Effacer" : $"M{AimRange.Distances[i]}";
            Vector3 at = console.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
            labels += Floor(console, key, at, i == AimRange.ClearButton ? 0.042f : 0.05f, Vector3.forward, label);
        }

        Renderer screen = BuildHologram(root, materials, bank);

        so.FindProperty("boardFace").objectReferenceValue = face;
        so.FindProperty("screen").objectReferenceValue = screen;
        SerializedProperty list = so.FindProperty("buttons");
        list.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"Salle de précision : le chariot ({parts} pièces, la cible de {AimRange.BoardDiameter:0.0} m), le rail, le pupitre ({buttons.Length} boutons, {labels} textes), l'écran holographique.");
    }

    /// <summary>
    /// L'écran holographique, sur le comptoir derrière le pupitre (HologramFoot) : un projecteur (un socle d'acier sombre et sa
    /// réglette lumineuse), la lumière qui monte de la réglette au bas de l'écran, l'écran (un carré de lumière de 0,52 m,
    /// tourné vers l'œil du tireur) et une faible lueur froide sur le comptoir. Rien n'y a de collider : c'est de la lumière,
    /// les balles la traversent ; ni l'écran ni sa lumière n'entrent dans la cuisson. Rend l'écran (AimRange y pose la cible).
    /// </summary>
    private static Renderer BuildHologram(Transform root, Dictionary<string, Material> materials, DecorMeshes.Bank bank)
    {
        const float footHeight = 0.04f, rise = 0.1f;
        Vector3 eye = ShootingRangeBuilder.AimShootingSpot + Vector3.up * 1.65f;
        var foot = new Vector3(HologramFoot.x, ShootingRangeBuilder.CounterHeight, HologramFoot.y);
        Vector3 center = foot + Vector3.up * (footHeight + rise + HologramSize * 0.5f);
        Vector3 toScreen = center - eye;
        Quaternion facing = Quaternion.LookRotation(toScreen.normalized, Vector3.up);
        Quaternion yaw = Quaternion.LookRotation(new Vector3(toScreen.x, 0f, toScreen.z).normalized, Vector3.up);
        var holo = new GameObject("Écran holographique").transform;
        holo.SetParent(root, false);

        var body = new DecorMeshes.Builder();
        DecorMeshes.ChamferedBox(body, new Vector3(-0.18f, 0f, -0.05f), new Vector3(0.18f, footHeight, 0.05f), 0.006f);
        GameObject projector = Part(holo, "Projecteur", bank.Add(body.ToMesh("Precision_Projecteur")), materials["Decor_Charpente"], ArenaDecorBuilder.ArenaLayerMask);
        projector.transform.SetPositionAndRotation(foot, yaw);
        var strip = new DecorMeshes.Builder();
        float y = footHeight + 0.001f;
        strip.Quad(new Vector3(-0.15f, y, -0.012f), new Vector3(0.15f, y, -0.012f), new Vector3(0.15f, y, 0.012f), new Vector3(-0.15f, y, 0.012f), Vector3.up);
        GameObject stripObject = Part(holo, "Réglette", bank.Add(strip.ToMesh("Precision_Reglette")), materials["Decor_LampeFroide"], ArenaDecorBuilder.ArenaLayerMask);
        stripObject.transform.SetPositionAndRotation(foot, yaw);
        stripObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        Renderer screen = FlatQuad(holo, "Écran", center, facing, new Vector2(HologramSize, HologramSize), HologramMaterial(ScreenMaterialPath, false));

        // La lumière qui monte : de la réglette (uv.y = 0) au bas de l'écran (uv.y = 1).
        float half = HologramSize * 0.5f;
        var beam = new Mesh { name = "Precision_Faisceau" };
        beam.SetVertices(new List<Vector3>
        {
            foot + yaw * new Vector3(-0.15f, y + 0.001f, 0f), foot + yaw * new Vector3(0.15f, y + 0.001f, 0f),
            center + facing * new Vector3(half, -half, 0f), center + facing * new Vector3(-half, -half, 0f),
        });
        beam.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
        beam.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        beam.RecalculateNormals();
        beam.RecalculateBounds();
        var beamObject = new GameObject("Lumière du projecteur");
        beamObject.transform.SetParent(holo, false);
        beamObject.AddComponent<MeshFilter>().sharedMesh = bank.Add(beam);
        var beamRenderer = beamObject.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = HologramMaterial(BeamMaterialPath, true);
        beamRenderer.shadowCastingMode = ShadowCastingMode.Off;
        beamRenderer.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask;

        Light glow = ArenaLighting.NewLight(holo, "Lueur de l'hologramme", LightType.Point);
        glow.transform.position = center - toScreen.normalized * 0.15f;
        glow.color = new Color(0.72f, 0.86f, 1f);
        glow.intensity = 0.6f;
        glow.range = 1.6f;
        glow.shadows = LightShadows.None;
        glow.lightmapBakeType = LightmapBakeType.Realtime;
        ArenaLighting.SetLayers(glow, ArenaDecorBuilder.ArenaLayerMask);
        return screen;
    }

    /// <summary>Un matériau DuelArena/Hologramme : l'écran (la cible) ou la lumière du projecteur (`beam`).</summary>
    private static Material HologramMaterial(string path, bool beam)
    {
        var material = new Material(Shader.Find("DuelArena/Hologramme"));
        material.SetFloat("_Faisceau", beam ? 1f : 0f);
        return RobotChassisBuilder.SaveMaterial(path, material);
    }

    /// <summary>Une pièce qui roule avec le chariot : ni statique (ni lumière cuite, ni lot statique), éclairée par les sondes.</summary>
    private static GameObject MovingPart(Transform parent, string name, Mesh mesh, Material material)
    {
        GameObject go = Part(parent, name, mesh, material, ArenaDecorBuilder.ArenaLayerMask);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        return go;
    }

    /// <summary>Un quadrilatère texturé (le Quad d'Unity : ses UV de 0 à 1, sa face vers son -Z), de taille `size`, ni
    /// statique ni ombre : la face de la cible, l'écran et les boutons du pupitre, dont la couleur ou la texture change.</summary>
    private static Renderer FlatQuad(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, Vector2 size, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.renderingLayerMask = ArenaDecorBuilder.ArenaLayerMask;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    /// <summary>Un matériau d'URP pour la salle de précision (la face de la cible, les boutons), refait à chaque construction ;
    /// `scale` et `offset` : le pavage de sa texture (la face du disque a ses uv en mètres).</summary>
    private static Material TargetMaterial(string path, string shader, Color color, Vector2? scale = null, Vector2? offset = null)
    {
        var material = new Material(Shader.Find(shader));
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        if (scale.HasValue) material.SetTextureScale("_BaseMap", scale.Value);
        if (offset.HasValue) material.SetTextureOffset("_BaseMap", offset.Value);
        return RobotChassisBuilder.SaveMaterial(path, material);
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    private static GameObject Part(Transform parent, string name, Mesh mesh, Material material, uint layers)
        => ArenaDecorBuilder.Part(parent, name, mesh, material, layers);

    /// <summary>Un objet à deux matériaux (les sous-maillages 0 et 1), sur le calque de l'arène.</summary>
    private static void PartMulti(Transform parent, string name, Mesh mesh, Vector3 position, Material first, Material second)
    {
        GameObject go = Part(parent, name, mesh, first, ArenaDecorBuilder.ArenaLayerMask);
        go.transform.position = position;
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { first, second };
    }
}
