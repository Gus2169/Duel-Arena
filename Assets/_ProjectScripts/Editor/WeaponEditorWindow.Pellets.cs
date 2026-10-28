using UnityEditor;
using UnityEngine;

/// <summary>
/// L'onglet « Gerbe » : les plombs d'une arme à gerbe se placent à la souris (GDD § 3 : un motif fixe,
/// jamais de hasard), et l'aperçu montre une décharge sur un robot à la distance choisie : les plombs
/// au but, les dégâts, les décharges pour le neutraliser.
/// </summary>
public partial class WeaponEditorWindow
{
    [SerializeField] private int selectedPellet = -1;
    [SerializeField] private Vector2 pelletSideScroll;

    private readonly AngleGraph pelletGraph = new AngleGraph();
    private readonly BurstPreview pelletShot = new BurstPreview();
    private int draggedPellet = -1;
    private int pelletUndoGroup;

    /// <summary>Ce que la gerbe devient dans le contexte de l'aperçu : resserrée épaulée, ou par le laser
    /// à la hanche (WeaponData.PelletOffset).</summary>
    private float PelletScale(WeaponData data)
        => previewAiming ? data.aimPatternScale : previewLaser && catalog != null && AcceptsLaser(data) ? catalog.laserHipSpread : 1f;

    private void DrawPelletTab(WeaponData data)
    {
        SerializedObject so = Serialized(data);
        so.Update();
        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        InlineField(so, "damagePerHit", data.PelletCount > 1 ? "Dégâts d'un plomb" : "Dégâts", 118f, 60f);
        GUILayout.Space(10f);
        InlineField(so, "aimPatternScale", "Resserrement épaulé", 128f, 150f);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        so.ApplyModifiedProperties();

        BurstSetup setup = Setup(data, 1);
        setup.recoilScale = Vector2.zero;
        pelletShot.Compute(data, setup);
        Vector2[] pattern = data.pelletPattern ?? new Vector2[0];
        float scale = PelletScale(data);
        var shown = new Vector2[Mathf.Max(1, pattern.Length)];
        for (int i = 0; i < pattern.Length; i++) shown[i] = pattern[i] * scale;
        selectedPellet = selectedPellet >= 0 && selectedPellet < pattern.Length ? selectedPellet : -1;

        EditorGUILayout.BeginHorizontal();
        Rect graphRect = GUILayoutUtility.GetRect(240f, 240f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawPelletGraph(graphRect, data, setup, shown, pattern.Length > 0 && scale > 1e-4f, scale);
        EditorGUILayout.BeginVertical(GUILayout.Width(SideWidth));
        pelletSideScroll = EditorGUILayout.BeginScrollView(pelletSideScroll);
        DrawPelletSidePanel(data, setup);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPelletGraph(Rect rect, WeaponData data, in BurstSetup setup, Vector2[] shown, bool editable, float scale)
    {
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        if (e.type == EventType.Layout) return;
        rect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);

        fitPoints.Clear();
        fitPoints.AddRange(shown);
        foreach (BurstPreview.Bullet bullet in pelletShot.bullets) fitPoints.Add(bullet.degrees);
        if (showTarget) AddTargetBounds(setup, fitPoints);
        pelletGraph.Fit(rect, fitPoints, 2f);
        pelletGraph.DrawBackground();
        if (showTarget) DrawTarget(pelletGraph, setup);

        // Où partent vraiment les plombs (le cône à la hanche compris), puis la gerbe à placer.
        foreach (BurstPreview.Bullet bullet in pelletShot.bullets)
        {
            Color color = bullet.zone switch
            {
                HitboxZoneType.Head => HeadColor,
                HitboxZoneType.Torso => TorsoColor,
                HitboxZoneType.Legs => LegsColor,
                _ => MissColor,
            };
            pelletGraph.Dot(bullet.degrees, 2.6f, color);
        }
        for (int i = 0; i < shown.Length; i++)
        {
            bool selected = i == selectedPellet;
            pelletGraph.Ring(shown[i], selected ? 7f : 5f, PatternColor, selected ? 2.5f : 1.8f);
            if (selected) pelletGraph.Ring(shown[i], 10f, Color.white, 1.2f);
            if (editable) pelletGraph.Label(shown[i], (i + 1).ToString(), new Vector2(7f, -16f), EditorStyles.miniBoldLabel);
        }
        if (e.type == EventType.Repaint)
        {
            string legend = editable
                ? "La gerbe : glisser un plomb (Ctrl : par 0,1°). Les points : où partent les plombs, colorés par zone touchée."
                : "Une seule balle, dans l'axe. Ajouter des plombs en fait une arme à gerbe.";
            EditorStyles.miniLabel.Draw(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 15f), legend, false, false, false, false);
        }

        int hovered = editable && rect.Contains(e.mousePosition) ? pelletGraph.Pick(shown, e.mousePosition, 9f) : -1;
        if (draggedPellet >= 0) hovered = draggedPellet;
        if (hovered >= 0 && e.type == EventType.Repaint)
        {
            Vector2 offset = data.pelletPattern[hovered];
            string text = $"Plomb {hovered + 1} : {N(offset.x)}° de côté, {N(offset.y)}° en hauteur (à la hanche)";
            Vector2 size = Styles.Tooltip.CalcSize(new GUIContent(text));
            Styles.Tooltip.Draw(new Rect(e.mousePosition.x + 14f, e.mousePosition.y + 10f, size.x, size.y), text, false, false, false, false);
        }

        switch (e.GetTypeForControl(control))
        {
            case EventType.MouseDown:
                if (e.button != 0 || !rect.Contains(e.mousePosition) || !editable) break;
                int pick = pelletGraph.Pick(shown, e.mousePosition, 9f);
                selectedPellet = pick;
                if (pick >= 0)
                {
                    draggedPellet = pick;
                    GUIUtility.hotControl = control;
                    pelletGraph.Frozen = true;
                    pelletUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Déplacer un plomb de la gerbe");
                }
                e.Use();
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != control || draggedPellet < 0) break;
                Vector2 wanted = pelletGraph.ToDegrees(e.mousePosition) / scale;
                if (e.control || e.command) wanted = new Vector2(Mathf.Round(wanted.x * 10f) / 10f, Mathf.Round(wanted.y * 10f) / 10f);
                int index = draggedPellet;
                WriteArray(data, "Déplacer un plomb de la gerbe", d =>
                {
                    var copy = (Vector2[])d.pelletPattern.Clone();
                    copy[index] = wanted;
                    d.pelletPattern = copy;
                });
                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != control) break;
                GUIUtility.hotControl = 0;
                draggedPellet = -1;
                pelletGraph.Frozen = false;
                Undo.CollapseUndoOperations(pelletUndoGroup);
                e.Use();
                break;
            case EventType.MouseMove:
                if (rect.Contains(e.mousePosition)) Repaint();
                break;
        }
    }

    private void DrawPelletSidePanel(WeaponData data, in BurstSetup setup)
    {
        DrawPreviewContext(data, false);
        GUILayout.Space(4f);
        showTarget = EditorGUILayout.ToggleLeft("Cible", showTarget);

        GUILayout.Space(6f);
        int pellets = pelletShot.bullets.Count;
        int hits = pelletShot.HeadHits + pelletShot.TorsoHits + pelletShot.LegHits;
        string where = setup.aimAtHead ? "visé à la tête" : "visé au buste";
        if (pellets > 1)
        {
            Note($"À {N(setup.distance)} m, {where} : {hits} plombs sur {pellets} au but ({pelletShot.HeadHits} à la tête, {pelletShot.TorsoHits} au buste, {pelletShot.LegHits} aux jambes), {N(pelletShot.Damage)} dégâts.");
            Note(pelletShot.Damage > 0f
                ? $"Il faut {Count(Mathf.CeilToInt(MaxHealth / pelletShot.Damage - 1e-4f), "décharge", "décharges")} comme celle-ci pour neutraliser un robot."
                : "Aucun plomb ne touche : la décharge ne blesse pas.");
            Note($"Écart moyen des plombs : {N(MeanPelletOffset(data))}° à la hanche, {N(MeanPelletOffset(data) * data.aimPatternScale)}° épaulé.");
        }
        else
        {
            Note($"À {N(setup.distance)} m, {where} : la balle {(hits > 0 ? "touche" : "manque")} ({N(pelletShot.Damage)} dégâts).");
        }
        if (data.shotIsBeam && data.PelletCount > 1)
            Note("Le modèle de Blender d'une arme laser à gerbe place ses lentilles d'après sa gerbe (Tools/Blender/laser_dispersion.py) : à refaire si elle change.");

        GUILayout.Space(8f);
        GUILayout.Label("Outils", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("+ Un plomb", "Un plomb de plus : le miroir du plomb choisi, ou dans l'axe.")))
        {
            Vector2 offset = selectedPellet >= 0 ? new Vector2(-data.pelletPattern[selectedPellet].x, data.pelletPattern[selectedPellet].y) : Vector2.zero;
            WriteArray(data, "Ajouter un plomb", d => d.pelletPattern = RecoilPatternEditing.With(d.pelletPattern, offset));
            selectedPellet = data.pelletPattern.Length - 1;
        }
        using (new EditorGUI.DisabledScope(selectedPellet < 0))
        {
            if (GUILayout.Button("− Ce plomb"))
            {
                int index = selectedPellet;
                WriteArray(data, "Retirer un plomb", d => d.pelletPattern = RecoilPatternEditing.Without(d.pelletPattern, index));
                selectedPellet = -1;
            }
        }
        if (GUILayout.Button(new GUIContent("Miroir ↔", "La gauche pour la droite.")))
            WriteArray(data, "Gerbe en miroir", d => d.pelletPattern = RecoilPatternEditing.Mirrored(d.pelletPattern));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Resserrer ×0,9", "Chaque plomb 10 % plus près de l'axe.")))
            WriteArray(data, "Resserrer la gerbe", d => d.pelletPattern = RecoilPatternEditing.Scaled(d.pelletPattern, Vector2.one * 0.9f));
        if (GUILayout.Button(new GUIContent("Écarter ×1,1", "Chaque plomb 10 % plus loin de l'axe.")))
            WriteArray(data, "Écarter la gerbe", d => d.pelletPattern = RecoilPatternEditing.Scaled(d.pelletPattern, Vector2.one * 1.1f));
        using (new EditorGUI.DisabledScope(data.pelletPattern == null || data.pelletPattern.Length == 0))
        {
            if (GUILayout.Button(new GUIContent("Une seule balle", "Retire tous les plombs : l'arme tire une balle dans l'axe."))
                && EditorUtility.DisplayDialog("Une seule balle", $"Retirer les {data.PelletCount} plombs de {data.weaponName} ? Il tirera une seule balle (Ctrl+Z pour revenir).", "Retirer", "Annuler"))
            {
                WriteArray(data, "Une seule balle", d => d.pelletPattern = new Vector2[0]);
                selectedPellet = -1;
            }
        }
        EditorGUILayout.EndHorizontal();

        Vector2[] pattern = data.pelletPattern ?? new Vector2[0];
        if (pattern.Length == 0) return;
        GUILayout.Space(8f);
        GUILayout.Label(new GUIContent("Plomb par plomb", "L'écart de chaque plomb à l'axe, en degrés, à la hanche sans laser : la valeur enregistrée."), EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(70f));
        GUILayout.Label(new GUIContent("→ (°)", "Vers la droite ; négatif vers la gauche."), EditorStyles.miniBoldLabel, GUILayout.Width(80f));
        GUILayout.Label(new GUIContent("↑ (°)", "Vers le haut ; négatif vers le bas."), EditorStyles.miniBoldLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < pattern.Length; i++)
        {
            Rect row = EditorGUILayout.BeginHorizontal();
            if (i == selectedPellet && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, new Color(PatternColor.r, PatternColor.g, PatternColor.b, 0.18f));
            if (GUILayout.Button($"Plomb {i + 1}", EditorStyles.label, GUILayout.Width(70f))) selectedPellet = i;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.DelayedFloatField(Round(pattern[i].x), GUILayout.Width(80f));
            float y = EditorGUILayout.DelayedFloatField(Round(pattern[i].y), GUILayout.Width(80f));
            if (EditorGUI.EndChangeCheck())
            {
                int index = i;
                var offset = new Vector2(x == Round(pattern[i].x) ? pattern[i].x : x, y == Round(pattern[i].y) ? pattern[i].y : y);
                WriteArray(data, "Modifier un plomb de la gerbe", d =>
                {
                    var copy = (Vector2[])d.pelletPattern.Clone();
                    copy[index] = offset;
                    d.pelletPattern = copy;
                });
            }
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.Space(10f);
    }
}
