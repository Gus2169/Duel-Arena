using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// L'éditeur des armes (2026-10-26, demandé par l'utilisateur) : tous les chiffres clés des douze armes
/// au même endroit, sans passer par l'inspecteur ; le motif de recul et la gerbe se dessinent à la
/// souris, et chaque réglage montre aussitôt ce qu'il change (coups pour neutraliser un robot, temps
/// pour le faire, rafale sur une cible). Duel Arena > Armes > Éditeur des armes.
///
/// Il écrit dans les WeaponData de l'arsenal (celles du préfab joueur, WeaponController), dans le
/// catalogue des accessoires et dans les réglages du recul selon la posture (WeaponController du
/// préfab), par des SerializedObject : l'Undo marche, et rien n'est enregistré sur le disque avant
/// « Enregistrer ». Les nombres qu'il montre sont calculés par les fonctions du jeu (WeaponData.DamageAt,
/// RecoilPattern, HipFireSpread, BodyLayout) : ce qu'il annonce est ce que le serveur comptera.
/// </summary>
public partial class WeaponEditorWindow : EditorWindow
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const float ListWidth = 168f;

    private enum Tab { Stats, Recoil, Pellets, Table, Shared }

    private static readonly string[] TabLabels = { "Caractéristiques", "Recul", "Gerbe", "Tableau", "Réglages communs" };

    [SerializeField] private int selectedSlot;
    [SerializeField] private Tab tab;
    [SerializeField] private Vector2 listScroll;

    // Le jeu, lu sur le préfab joueur.
    private GameObject playerPrefab;
    private WeaponController controller;
    private PlayerLocomotion locomotion;
    private PlayerHitbox hitbox;
    private Health health;
    private AttachmentCatalog catalog;
    private readonly List<WeaponData> loadout = new List<WeaponData>();
    private readonly Dictionary<Object, SerializedObject> serializedObjects = new Dictionary<Object, SerializedObject>();

    [MenuItem("Duel Arena/Armes/Éditeur des armes", priority = 0)]
    public static void Open()
    {
        var window = GetWindow<WeaponEditorWindow>();
        window.titleContent = new GUIContent("Éditeur des armes");
        window.minSize = new Vector2(860f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        Undo.undoRedoPerformed += Repaint;
        Load();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
    }

    private void OnFocus() => Load();

    private void Load()
    {
        playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        controller = playerPrefab != null ? playerPrefab.GetComponentInChildren<WeaponController>(true) : null;
        locomotion = playerPrefab != null ? playerPrefab.GetComponent<PlayerLocomotion>() : null;
        hitbox = playerPrefab != null ? playerPrefab.GetComponentInChildren<PlayerHitbox>(true) : null;
        health = playerPrefab != null ? playerPrefab.GetComponent<Health>() : null;
        loadout.Clear();
        catalog = null;
        if (controller == null) return;
        for (int slot = 0; slot < controller.SlotCount; slot++)
        {
            WeaponData data = controller.DataOf(slot);
            loadout.Add(data);
            if (catalog == null && data != null) catalog = data.attachmentCatalog;
        }
        selectedSlot = Mathf.Clamp(selectedSlot, 0, Mathf.Max(0, loadout.Count - 1));
    }

    /// <summary>La vie d'un robot (le préfab joueur) : ce qu'il faut retirer pour le neutraliser.</summary>
    private float MaxHealth => health != null ? health.Max : 100f;

    private WeaponData Selected => selectedSlot >= 0 && selectedSlot < loadout.Count ? loadout[selectedSlot] : null;

    private SerializedObject Serialized(Object target)
    {
        if (!serializedObjects.TryGetValue(target, out SerializedObject so) || so == null || so.targetObject == null)
        {
            so = new SerializedObject(target);
            serializedObjects[target] = so;
        }
        return so;
    }

    private void OnGUI()
    {
        if (controller == null || loadout.Count == 0) Load();
        if (controller == null || loadout.Count == 0)
        {
            EditorGUILayout.HelpBox($"L'arsenal est introuvable : le préfab joueur ({PlayerPrefabPath}) et son WeaponController portent les douze armes.", MessageType.Error);
            return;
        }

        DrawToolbar();
        if (EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Partie en cours : chaque changement vaut tout de suite dans cette instance. Pour un essai à deux, réglez hors du Play Mode : le joueur virtuel garderait les anciennes valeurs, et le tireur et le serveur ne compteraient plus pareil.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawWeaponList();
        EditorGUILayout.BeginVertical();
        WeaponData data = Selected;
        if (data == null)
        {
            EditorGUILayout.HelpBox("Cette place de l'arsenal est vide.", MessageType.Warning);
        }
        else
        {
            switch (tab)
            {
                case Tab.Stats: DrawStatsTab(data); break;
                case Tab.Recoil: DrawRecoilTab(data); break;
                case Tab.Pellets: DrawPelletTab(data); break;
                case Tab.Table: DrawTableTab(); break;
                default: DrawSharedTab(); break;
            }
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    // ------------------------------------------------------------------
    // La barre du haut, la liste des armes, l'enregistrement
    // ------------------------------------------------------------------

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        tab = (Tab)GUILayout.Toolbar((int)tab, TabLabels, EditorStyles.toolbarButton, GUI.ToolbarButtonSize.FitToContents);
        GUILayout.FlexibleSpace();
        bool dirty = AnyDirty();
        if (dirty) GUILayout.Label("Modifications non enregistrées", Styles.Warning);
        using (new EditorGUI.DisabledScope(!dirty))
        {
            if (GUILayout.Button(new GUIContent("Enregistrer", "Écrit sur le disque les armes, le catalogue des accessoires et le préfab joueur modifiés."), EditorStyles.toolbarButton, GUILayout.Width(80f)))
                SaveAll();
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool AnyDirty()
    {
        foreach (WeaponData data in loadout)
            if (data != null && EditorUtility.IsDirty(data)) return true;
        return (catalog != null && EditorUtility.IsDirty(catalog)) || (controller != null && EditorUtility.IsDirty(controller));
    }

    private void SaveAll()
    {
        foreach (WeaponData data in loadout)
            if (data != null) AssetDatabase.SaveAssetIfDirty(data);
        if (catalog != null) AssetDatabase.SaveAssetIfDirty(catalog);
        if (controller != null && EditorUtility.IsDirty(controller)) AssetDatabase.SaveAssetIfDirty(controller);
    }

    private void DrawWeaponList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        for (int family = 0; family < Arsenal.FamilyCount; family++)
        {
            List<int> slots = Arsenal.SlotsOf(loadout, (WeaponFamily)family);
            if (slots.Count == 0) continue;
            GUILayout.Label(Arsenal.FamilyLabel((WeaponFamily)family), Styles.FamilyHeader);
            foreach (int slot in slots) DrawWeaponRow(slot);
            GUILayout.Space(6f);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawWeaponRow(int slot)
    {
        WeaponData data = loadout[slot];
        Rect rect = GUILayoutUtility.GetRect(ListWidth - 20f, 34f, GUILayout.ExpandWidth(true));
        bool selected = slot == selectedSlot;
        if (Event.current.type == EventType.Repaint)
        {
            if (selected) EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.55f, 0.7f, 0.9f));
            else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.12f));
            string mark = EditorUtility.IsDirty(data) ? "  ●" : "";
            Styles.WeaponName.Draw(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 8f, 16f), data.weaponName + mark, false, false, false, false);
            Styles.WeaponRole.Draw(new Rect(rect.x + 6f, rect.y + 17f, rect.width - 8f, 14f), data.role, false, false, false, false);
        }
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            selectedSlot = slot;
            selectedShot = selectedPellet = -1;
            GUI.FocusControl(null);
        }
    }

    // ------------------------------------------------------------------
    // Les briques communes des onglets
    // ------------------------------------------------------------------

    private static readonly Dictionary<(System.Type, string), (RangeAttribute range, MinAttribute min)> limits =
        new Dictionary<(System.Type, string), (RangeAttribute, MinAttribute)>();

    /// <summary>Les bornes que l'asset donne à un champ ([Range], [Min]) : l'éditeur les respecte.</summary>
    private static (RangeAttribute range, MinAttribute min) Limits(System.Type type, string property)
    {
        if (limits.TryGetValue((type, property), out var found)) return found;
        System.Reflection.FieldInfo field = null;
        for (System.Type t = type; t != null && field == null; t = t.BaseType)
            field = t.GetField(property, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        found = field != null
            ? ((RangeAttribute)System.Attribute.GetCustomAttribute(field, typeof(RangeAttribute)), (MinAttribute)System.Attribute.GetCustomAttribute(field, typeof(MinAttribute)))
            : (null, null);
        limits[(type, property)] = found;
        return found;
    }

    /// <summary>
    /// Un champ de l'asset, avec son libellé en français et l'infobulle de son attribut, dans ses bornes.
    /// Dessiné selon son type plutôt que par PropertyField, qui répéterait les en-têtes ([Header]) de
    /// l'inspecteur au-dessus des groupes de l'éditeur.
    /// </summary>
    private static void Field(SerializedObject so, string property, string label, params GUILayoutOption[] options)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null)
        {
            EditorGUILayout.LabelField(label, $"(champ « {property} » introuvable)");
            return;
        }
        var content = new GUIContent(label, p.tooltip);
        (RangeAttribute range, MinAttribute min) = Limits(so.targetObject.GetType(), property);
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float:
            {
                EditorGUI.BeginChangeCheck();
                float value = range != null ? EditorGUILayout.Slider(content, p.floatValue, range.min, range.max, options) : EditorGUILayout.FloatField(content, p.floatValue, options);
                if (EditorGUI.EndChangeCheck()) p.floatValue = min != null ? Mathf.Max(min.min, value) : value;
                return;
            }
            case SerializedPropertyType.Integer:
            {
                EditorGUI.BeginChangeCheck();
                int value = range != null ? EditorGUILayout.IntSlider(content, p.intValue, (int)range.min, (int)range.max, options) : EditorGUILayout.IntField(content, p.intValue, options);
                if (EditorGUI.EndChangeCheck()) p.intValue = min != null ? Mathf.Max((int)min.min, value) : value;
                return;
            }
            case SerializedPropertyType.Boolean:
            {
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUILayout.Toggle(content, p.boolValue, options);
                if (EditorGUI.EndChangeCheck()) p.boolValue = value;
                return;
            }
            case SerializedPropertyType.String:
            {
                EditorGUI.BeginChangeCheck();
                string value = EditorGUILayout.TextField(content, p.stringValue, options);
                if (EditorGUI.EndChangeCheck()) p.stringValue = value;
                return;
            }
            case SerializedPropertyType.Color:
            {
                EditorGUI.BeginChangeCheck();
                Color value = EditorGUILayout.ColorField(content, p.colorValue, options);
                if (EditorGUI.EndChangeCheck()) p.colorValue = value;
                return;
            }
            default:
                EditorGUILayout.PropertyField(p, content, true, options);
                return;
        }
    }

    /// <summary>Une énumération, ses valeurs nommées en français (dans l'ordre de leurs valeurs, 0, 1, 2…).</summary>
    private static void Choice(SerializedObject so, string property, string label, string[] choices)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.Popup(new GUIContent(label, p.tooltip), p.intValue, choices);
        if (EditorGUI.EndChangeCheck()) p.intValue = value;
    }

    /// <summary>Des drapeaux (1, 2, 4…), nommés en français dans l'ordre de leurs bits.</summary>
    private static void Mask(SerializedObject so, string property, string label, string[] names)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) return;
        int all = (1 << names.Length) - 1;
        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.MaskField(new GUIContent(label, p.tooltip), p.intValue & all, names);
        if (EditorGUI.EndChangeCheck()) p.intValue = value & all;
    }

    private static void Section(string title)
    {
        GUILayout.Space(10f);
        GUILayout.Label(title, Styles.SectionHeader);
        Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        GUILayout.Space(3f);
    }

    /// <summary>Ce qu'un réglage donne, en clair, sous ses champs.</summary>
    private static void Note(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        EditorGUILayout.LabelField(text, Styles.Note);
    }

    private static string N(float value) => WeaponStatSheet.Number(value);

    /// <summary>« 1 coup », « 6 coups ».</summary>
    private static string Count(int n, string singular, string plural) => $"{n} {(n > 1 ? plural : singular)}";

    /// <summary>Les coups (tous les plombs) pour neutraliser un robot à pleine vie dans une zone, à une
    /// distance, chargés à fond : comme le serveur compte (WeaponData.DamageAt).</summary>
    private int ShotsToKill(WeaponData data, HitboxZoneType zone, float distance)
    {
        float perShot = data.DamageAt(zone, distance) * data.PelletCount;
        return perShot > 0f ? Mathf.CeilToInt(MaxHealth / perShot - 1e-4f) : 0;
    }

    /// <summary>Le temps du premier coup au coup qui neutralise, tirés au plus vite (le tir chargé :
    /// chargés à fond).</summary>
    private static float TimeToKill(WeaponData data, int shots)
    {
        if (shots <= 0) return float.PositiveInfinity;
        return (data.UsesCharge ? data.chargeTime : 0f) + (shots - 1) * RecoilPattern.ShotInterval(data);
    }

    /// <summary>Écrit un nouveau tableau dans un champ de l'asset, sous l'Undo.</summary>
    private static void WriteArray(WeaponData data, string undoName, System.Action<WeaponData> write)
    {
        Undo.RecordObject(data, undoName);
        write(data);
        EditorUtility.SetDirty(data);
    }

    private static class Styles
    {
        private static GUIStyle familyHeader, weaponName, weaponRole, sectionHeader, note, warning, small, smallBold, tooltip, computed, tableHeader;

        public static GUIStyle Computed => computed ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.8f, 1f) : new Color(0.1f, 0.3f, 0.6f) },
        };

        public static GUIStyle TableHeader => tableHeader ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            wordWrap = true,
            alignment = TextAnchor.LowerCenter,
        };

        public static GUIStyle FamilyHeader => familyHeader ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            padding = new RectOffset(4, 0, 6, 2),
        };

        public static GUIStyle WeaponName => weaponName ??= new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
        public static GUIStyle WeaponRole => weaponRole ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
        public static GUIStyle SectionHeader => sectionHeader ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        public static GUIStyle Note => note ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            fontSize = 11,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.8f, 1f) : new Color(0.1f, 0.3f, 0.6f) },
            padding = new RectOffset(2, 2, 1, 3),
        };
        public static GUIStyle Warning => warning ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(1f, 0.6f, 0.2f) },
            alignment = TextAnchor.MiddleRight,
        };
        public static GUIStyle Small => small ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        public static GUIStyle SmallBold => smallBold ??= new GUIStyle(EditorStyles.miniBoldLabel);
        public static GUIStyle Tooltip => tooltip ??= new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = false };
    }
}
