using UnityEditor;
using UnityEngine;

/// <summary>
/// L'onglet « Tableau » : les chiffres clés des douze armes côte à côte, modifiables case par case,
/// avec ce qu'ils donnent (coups et temps pour neutraliser un robot à la distance choisie) : de quoi
/// équilibrer l'arsenal d'un coup d'œil. Et l'onglet « Réglages communs » : ce qui vaut pour toutes
/// les armes (les effets des accessoires, le recul selon la posture).
/// </summary>
public partial class WeaponEditorWindow
{
    [SerializeField] private Vector2 tableScroll;
    [SerializeField] private float tableDistance = 10f;
    [SerializeField] private Vector2 sharedScroll;

    private delegate void CellDrawer(Rect rect, WeaponData data, SerializedObject so);

    private struct Column
    {
        public GUIContent title;
        public float width;
        public CellDrawer draw;
    }

    private Column[] columns;

    private Column[] Columns => columns ??= new[]
    {
        Col("Arme", "Choisir l'arme : ses caractéristiques.", 72f, (r, d, so) =>
        {
            if (GUI.Button(r, d.weaponName, EditorStyles.miniButton)) { selectedSlot = loadout.IndexOf(d); tab = Tab.Stats; }
        }),
        Col("Dégâts", "Dégâts d'une balle (d'un plomb) au buste, de près.", 54f, (r, d, so) => FloatCell(r, so, "damagePerHit", 0f)),
        Col("× Tête", "Multiplicateur des dégâts à la tête.", 46f, (r, d, so) => FloatCell(r, so, "headMultiplier", 0f)),
        Col("× Jambes", "Multiplicateur des dégâts aux jambes.", 52f, (r, d, so) => FloatCell(r, so, "legsMultiplier", 0f)),
        Col("Plombs", "Plombs d'un tir (onglet « Gerbe »).", 44f, (r, d, so) => GUI.Label(r, d.PelletCount.ToString(), Styles.Computed)),
        Col("Coups/s", "La cadence : coups par seconde.", 54f, (r, d, so) => FloatCell(r, so, "shotsPerSecond", 0.05f)),
        Col("Auto", "Automatique : la gâchette tenue tire en rafale.", 34f, (r, d, so) => ToggleCell(r, so, "isAutomatic")),
        Col("Charge (s)", "Le tir chargé : temps pour charger à fond (0 : tir à l'appui).", 56f, (r, d, so) => FloatCell(r, so, "chargeTime", 0f)),
        Col("Chargeur", "Balles par chargeur (une arme laser chauffe à la place).", 56f, (r, d, so) =>
        {
            if (d.UsesHeat) GUI.Label(r, "chaleur", Styles.Computed);
            else IntCell(r, so, "magazineSize", 1, 255);
        }),
        Col("Chaleur", "Arme laser : chaleur d'un tir (1 = la surchauffe).", 54f, (r, d, so) =>
        {
            if (d.UsesHeat) FloatCell(r, so, "heatPerShot", 0.001f, 1f);
            else GUI.Label(r, "—", Styles.Computed);
        }),
        Col("Recharg. (s)", "Rechargement complet, d'une cartouche (cartouche par cartouche), ou vidange voulue (arme laser).", 62f, (r, d, so) => FloatCell(r, so, "reloadDuration", 0.01f)),
        Col("Pleins (m)", "Dégâts pleins jusqu'à cette distance.", 58f, (r, d, so) => FloatCell(r, so, "falloffStart", 0f)),
        Col("Baisse (m)", "Baisse finie à cette distance (plus loin que les dégâts pleins, sinon aucune baisse).", 58f, (r, d, so) => FloatCell(r, so, "falloffEnd", 0f)),
        Col("Reste ×", "Ce qu'il reste des dégâts au-delà de la baisse.", 50f, (r, d, so) => FloatCell(r, so, "minDamageMultiplier", 0f, 1f)),
        Col("Portée (m)", "Portée maximale.", 58f, (r, d, so) => FloatCell(r, so, "maxRange", 1f)),
        Col("Épaul. (s)", "Temps pour épauler.", 54f, (r, d, so) => FloatCell(r, so, "aimTime", 0f)),
        Col("Sortie (s)", "Temps pour sortir l'arme.", 54f, (r, d, so) => FloatCell(r, so, "raiseTime", 0f)),
        Col("Sprint (s)", "Sortie de sprint : temps avant de pouvoir tirer.", 54f, (r, d, so) => FloatCell(r, so, "sprintToFireTime", 0f)),
        Col("Poids ×", "Vitesse de déplacement avec l'arme en main.", 50f, (r, d, so) => FloatCell(r, so, "moveSpeedMultiplier", 0.5f, 1.1f)),
        Col("Recul (°)", "La force du recul : degrés par coup du motif (onglet « Recul »).", 56f, (r, d, so) => FloatCell(r, so, "recoilPerShotDegrees", 0f)),
        Col("Hanche (°)", "Le cône à la hanche, debout à l'arrêt.", 58f, (r, d, so) => FloatCell(r, so, "hipSpread", 0f)),
        Col("Buste / tête", "Coups pour neutraliser un robot à la distance des calculs, tous au but.", 76f, (r, d, so) =>
            GUI.Label(r, $"{ShotsToKill(d, HitboxZoneType.Torso, tableDistance)} / {ShotsToKill(d, HitboxZoneType.Head, tableDistance)}", Styles.Computed)),
        Col("Temps (s)", "Temps pour neutraliser au buste à la distance des calculs, tous les coups au but, tirés au plus vite.", 62f, (r, d, so) =>
            GUI.Label(r, N(TimeToKill(d, ShotsToKill(d, HitboxZoneType.Torso, tableDistance))), Styles.Computed)),
    };

    private static Column Col(string title, string tooltip, float width, CellDrawer draw)
        => new Column { title = new GUIContent(title, tooltip), width = width, draw = draw };

    private void DrawTableTab()
    {
        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Chaque case se modifie (Entrée pour valider) ; les cases en couleur se calculent.", Styles.Small);
        GUILayout.FlexibleSpace();
        float previous = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 150f;
        tableDistance = EditorGUILayout.Slider("Distance des calculs (m)", tableDistance, 0f, 100f, GUILayout.Width(340f));
        EditorGUIUtility.labelWidth = previous;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);

        Column[] all = Columns;
        float width = 8f;
        foreach (Column column in all) width += column.width;

        tableScroll = EditorGUILayout.BeginScrollView(tableScroll);
        Rect header = GUILayoutUtility.GetRect(width, 32f, GUILayout.Width(width));
        float x = header.x + 4f;
        foreach (Column column in all)
        {
            GUI.Label(new Rect(x, header.y, column.width, header.height), column.title, Styles.TableHeader);
            x += column.width;
        }

        int row = 0;
        for (int family = 0; family < Arsenal.FamilyCount; family++)
        {
            var slots = Arsenal.SlotsOf(loadout, (WeaponFamily)family);
            if (slots.Count == 0) continue;
            Rect familyRow = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width));
            GUI.Label(new Rect(familyRow.x + 4f, familyRow.y, 300f, familyRow.height), Arsenal.FamilyLabel((WeaponFamily)family), EditorStyles.miniBoldLabel);
            foreach (int slot in slots)
            {
                WeaponData data = loadout[slot];
                Rect rect = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width));
                if (Event.current.type == EventType.Repaint)
                {
                    if (slot == selectedSlot) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.18f));
                    else if (row % 2 == 1) EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.07f));
                }
                SerializedObject so = Serialized(data);
                so.Update();
                x = rect.x + 4f;
                foreach (Column column in all)
                {
                    column.draw(new Rect(x + 1f, rect.y + 1f, column.width - 4f, 18f), data, so);
                    x += column.width;
                }
                so.ApplyModifiedProperties();
                row++;
            }
        }
        GUILayout.Space(8f);
        EditorGUILayout.EndScrollView();
    }

    private static void FloatCell(Rect rect, SerializedObject so, string property, float min, float max = float.PositiveInfinity)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        float value = EditorGUI.DelayedFloatField(rect, p.floatValue);
        if (EditorGUI.EndChangeCheck()) p.floatValue = Mathf.Clamp(value, min, max);
    }

    private static void IntCell(Rect rect, SerializedObject so, string property, int min, int max)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        int value = EditorGUI.DelayedIntField(rect, p.intValue);
        if (EditorGUI.EndChangeCheck()) p.intValue = Mathf.Clamp(value, min, max);
    }

    private static void ToggleCell(Rect rect, SerializedObject so, string property)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        bool value = EditorGUI.Toggle(new Rect(rect.x + (rect.width - 14f) * 0.5f, rect.y, 16f, rect.height), p.boolValue);
        if (EditorGUI.EndChangeCheck()) p.boolValue = value;
    }

    // ------------------------------------------------------------------
    // Réglages communs
    // ------------------------------------------------------------------

    private static readonly string[] StanceRows = { "Debout", "Accroupi", "Allongé" };
    private static readonly string[] MovementColumns = { "À l'arrêt", "Sneak", "En marche" };
    private static readonly string[,] RecoilPercentProperties =
    {
        { "recoilPercentStandingStatic", "recoilPercentStandingSneaking", "recoilPercentStandingMoving" },
        { "recoilPercentCrouchingStatic", "recoilPercentCrouchingSneaking", "recoilPercentCrouchingMoving" },
        { "recoilPercentProneStatic", "recoilPercentProneSneaking", "recoilPercentProneMoving" },
    };

    /// <summary>Les accessoires qui peuvent porter des effets, dans l'ordre de l'armurerie. Les viseurs n'en ont
    /// pas : ils ne changent que l'image de visée (GDD § 6).</summary>
    private static readonly (AttachmentCategory category, int variant, string name)[] EffectAccessories =
    {
        (AttachmentCategory.Barrel, (int)BarrelKind.Compensator, "Compensateur (ou stabilisateur d'une arme laser)"),
        (AttachmentCategory.Barrel, (int)BarrelKind.MuzzleBrake, "Frein de bouche (ou dissipateur)"),
        (AttachmentCategory.Barrel, (int)BarrelKind.Long, "Canon long (ou focaliseur long)"),
        (AttachmentCategory.Barrel, (int)BarrelKind.Short, "Canon court (ou focaliseur court)"),
        (AttachmentCategory.Grip, (int)GripKind.Vertical, "Poignée verticale"),
        (AttachmentCategory.Grip, (int)GripKind.Angled, "Poignée inclinée"),
        (AttachmentCategory.Laser, 1, "Laser tactique"),
        (AttachmentCategory.Magazine, (int)MagazineKind.Extended, "Chargeur grande capacité (ou cellule grande capacité)"),
        (AttachmentCategory.Magazine, (int)MagazineKind.Fast, "Chargeur rapide (ou cellule rapide)"),
        (AttachmentCategory.Stock, (int)StockKind.Light, "Crosse légère"),
        (AttachmentCategory.Stock, (int)StockKind.Heavy, "Crosse lourde"),
    };

    private static GUIContent[] statLabels;

    /// <summary>
    /// Les effets de chaque accessoire, en lignes « caractéristique × facteur » qu'on modifie, retire ou ajoute
    /// (2026-10-29, demandé par l'utilisateur : « supprimer, ajouter, modifier, de façon super simple »). Écrits
    /// dans le catalogue par SerializedObject (Undo compris) ; « Enregistrer » les met sur le disque.
    /// </summary>
    private void DrawEffects()
    {
        statLabels ??= System.Array.ConvertAll(WeaponStats.All, s => new GUIContent(WeaponStats.Label(s), WeaponStats.Help(s)));
        SerializedObject so = Serialized(catalog);
        so.Update();
        SerializedProperty effects = so.FindProperty("effects");
        foreach (var (category, variant, name) in EffectAccessories)
        {
            GUILayout.Space(6f);
            GUILayout.Label(name, EditorStyles.boldLabel);
            int index = FindEffect(effects, category, variant);
            SerializedProperty modifiers = index >= 0 ? effects.GetArrayElementAtIndex(index).FindPropertyRelative("modifiers") : null;
            int count = modifiers != null ? modifiers.arraySize : 0;
            if (count == 0) Note("Aucun effet.");
            for (int i = 0; i < count; i++)
            {
                SerializedProperty modifier = modifiers.GetArrayElementAtIndex(i);
                SerializedProperty stat = modifier.FindPropertyRelative("stat");
                SerializedProperty factor = modifier.FindPropertyRelative("factor");
                EditorGUILayout.BeginHorizontal();
                int shown = System.Array.IndexOf(WeaponStats.All, (WeaponStat)stat.intValue);
                EditorGUI.BeginChangeCheck();
                int chosen = EditorGUILayout.Popup(Mathf.Max(0, shown), statLabels, GUILayout.Width(190f));
                if (EditorGUI.EndChangeCheck()) stat.intValue = (int)WeaponStats.All[chosen];
                GUILayout.Label("×", GUILayout.Width(12f));
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.DelayedFloatField(factor.floatValue, GUILayout.Width(56f));
                if (EditorGUI.EndChangeCheck()) factor.floatValue = Mathf.Clamp(value, 0.05f, 5f);
                GUILayout.Label(WeaponStats.Percent(factor.floatValue), GUILayout.Width(64f));
                bool remove = GUILayout.Button(new GUIContent("Retirer", "Retire cet effet de l'accessoire."), GUILayout.Width(64f));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    modifiers.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            if (GUILayout.Button(new GUIContent("+ Ajouter un effet", "Une caractéristique de plus, à ×1 (sans effet) : choisissez-la puis réglez son facteur."), GUILayout.Width(150f)))
            {
                if (index < 0)
                {
                    index = effects.arraySize;
                    effects.InsertArrayElementAtIndex(index);
                    SerializedProperty added = effects.GetArrayElementAtIndex(index);
                    added.FindPropertyRelative("category").intValue = (int)category;
                    added.FindPropertyRelative("variant").intValue = variant;
                    added.FindPropertyRelative("modifiers").arraySize = 0;
                }
                modifiers = effects.GetArrayElementAtIndex(index).FindPropertyRelative("modifiers");
                int n = modifiers.arraySize;
                modifiers.InsertArrayElementAtIndex(n);
                SerializedProperty fresh = modifiers.GetArrayElementAtIndex(n);
                fresh.FindPropertyRelative("stat").intValue = (int)UnusedStat(modifiers, n);
                fresh.FindPropertyRelative("factor").floatValue = 1f;
            }
        }
        GUILayout.Space(8f);
        Field(so, "firstShots", "« Recul des premiers coups » : combien de coups");
        so.ApplyModifiedProperties();
    }

    /// <summary>L'effet de cet accessoire dans le catalogue, ou -1.</summary>
    private static int FindEffect(SerializedProperty effects, AttachmentCategory category, int variant)
    {
        for (int i = 0; i < effects.arraySize; i++)
        {
            SerializedProperty effect = effects.GetArrayElementAtIndex(i);
            if (effect.FindPropertyRelative("category").intValue == (int)category && effect.FindPropertyRelative("variant").intValue == variant)
                return i;
        }
        return -1;
    }

    /// <summary>La première caractéristique que l'accessoire ne change pas encore (les `count` premiers effets).</summary>
    private static WeaponStat UnusedStat(SerializedProperty modifiers, int count)
    {
        foreach (WeaponStat stat in WeaponStats.All)
        {
            bool used = false;
            for (int i = 0; i < count && !used; i++)
                used = modifiers.GetArrayElementAtIndex(i).FindPropertyRelative("stat").intValue == (int)stat;
            if (!used) return stat;
        }
        return WeaponStats.All[0];
    }

    private void DrawSharedTab()
    {
        sharedScroll = EditorGUILayout.BeginScrollView(sharedScroll);
        float previous = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 280f;

        Section("Les effets des accessoires (toutes les armes)");
        if (catalog != null)
        {
            Note("Chaque effet multiplie une caractéristique de l'arme qui porte l'accessoire : ×0,8 en retire 20 %, ×1,5 en ajoute la moitié. "
                 + "Deux effets sur la même caractéristique se multiplient. Une arme laser prend ses cellules à la place des chargeurs : "
                 + "le chargeur y est la chaleur qu'elle tient avant la surchauffe, le rechargement ses vidanges ; et ses émetteurs à la place "
                 + "de ce qui va au canon, avec les mêmes effets (la flamme de bouche y est l'épaisseur du rayon). Ce que chaque arme prend se "
                 + "règle dans son onglet Caractéristiques.");
            DrawEffects();
        }
        else
        {
            EditorGUILayout.HelpBox("Le catalogue des accessoires n'est branché sur aucune arme.", MessageType.Warning);
        }

        Section("Le recul selon la posture (toutes les armes, en % de la force)");
        if (controller != null)
        {
            SerializedObject so = Serialized(controller);
            so.Update();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(90f));
            foreach (string column in MovementColumns) GUILayout.Label(column, EditorStyles.miniBoldLabel, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
            for (int stance = 0; stance < StanceRows.Length; stance++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(StanceRows[stance], GUILayout.Width(90f));
                for (int movement = 0; movement < MovementColumns.Length; movement++)
                {
                    SerializedProperty p = so.FindProperty(RecoilPercentProperties[stance, movement]);
                    EditorGUI.BeginChangeCheck();
                    float value = EditorGUILayout.DelayedFloatField(p.floatValue, GUILayout.Width(80f));
                    if (EditorGUI.EndChangeCheck()) p.floatValue = Mathf.Clamp(value, 0f, 100f);
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            EditorGUIUtility.labelWidth = 200f;
            SerializedProperty aiming = so.FindProperty("recoilPercentWhileAiming");
            EditorGUI.BeginChangeCheck();
            float aimed = EditorGUILayout.DelayedFloatField(new GUIContent("Épaulé, en plus (%)", aiming.tooltip), aiming.floatValue, GUILayout.Width(290f));
            if (EditorGUI.EndChangeCheck()) aiming.floatValue = Mathf.Clamp(aimed, 0f, 100f);
            so.ApplyModifiedProperties();
            Note($"Épaulé, debout à l'arrêt, un coup pousse de {N(controller.RecoilMultiplier(PlayerLocomotion.Stance.Standing, false, false, true) * 100f)} % de la force de l'arme ; accroupi, {N(controller.RecoilMultiplier(PlayerLocomotion.Stance.Crouching, false, false, true) * 100f)} %. Réglé sur le préfab joueur (WeaponController).");
        }

        Section("Un robot");
        Note($"Vie : {N(MaxHealth)} PV (Health du préfab joueur). Tous les calculs de l'éditeur s'y rapportent.");
        if (locomotion != null)
            Note($"Marche : {N(locomotion.NominalSpeed(PlayerLocomotion.Stance.Standing))} m/s debout, {N(locomotion.NominalSpeed(PlayerLocomotion.Stance.Crouching))} m/s accroupi, avant le poids de l'arme (PlayerLocomotion).");

        EditorGUIUtility.labelWidth = previous;
        GUILayout.Space(10f);
        EditorGUILayout.EndScrollView();
    }
}
