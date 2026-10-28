using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// L'onglet « Recul » : le motif coup par coup se dessine à la souris (chaque point est un coup de la
/// rafale, là où il part sans compenser), et l'aperçu montre la rafale telle qu'elle partira en jeu dans
/// la posture choisie, avec le retour du regard, le cône à la hanche et ce qu'elle fait à un robot.
/// </summary>
public partial class WeaponEditorWindow
{
    [SerializeField] private PlayerLocomotion.Stance previewStance = PlayerLocomotion.Stance.Standing;
    [SerializeField] private PreviewMovement previewMovement = PreviewMovement.Still;
    [SerializeField] private bool previewAiming = true;
    [SerializeField] private bool previewGrip;
    [SerializeField] private bool previewLaser;
    [SerializeField] private bool previewExtended;
    [SerializeField] private bool previewAtHead;
    [SerializeField] private float previewDistance = 20f;
    [SerializeField] private bool showPattern = true;
    [SerializeField] private bool showBurst = true;
    [SerializeField] private bool showTarget = true;
    [SerializeField] private int selectedShot = -1;
    [SerializeField] private Vector2 recoilSideScroll;

    private readonly AngleGraph recoilGraph = new AngleGraph();
    private readonly BurstPreview burst = new BurstPreview();
    private readonly List<Vector2> fitPoints = new List<Vector2>();
    private int draggedShot = -1;
    private int dragUndoGroup;

    private const float SideWidth = 350f;

    private static readonly Color PatternColor = new Color(1f, 0.62f, 0.15f);
    private static readonly Color BurstColor = new Color(0.3f, 0.78f, 1f);
    private static readonly Color HeadColor = new Color(1f, 0.33f, 0.33f);
    private static readonly Color TorsoColor = new Color(1f, 0.82f, 0.25f);
    private static readonly Color LegsColor = new Color(0.45f, 0.9f, 0.45f);
    private static readonly Color MissColor = new Color(0.6f, 0.6f, 0.6f, 0.85f);

    // ------------------------------------------------------------------
    // Le contexte de l'aperçu, lu sur le préfab joueur
    // ------------------------------------------------------------------

    private static bool AcceptsGrip(WeaponData data) => WeaponAttachments.Accepts(data, AttachmentCategory.Grip, (int)GripKind.Vertical);
    private static bool AcceptsLaser(WeaponData data) => WeaponAttachments.Accepts(data, AttachmentCategory.Laser, 1);
    private static bool AcceptsMagazine(WeaponData data) => WeaponAttachments.Accepts(data, AttachmentCategory.Magazine, 1);

    /// <summary>Ce que la posture, le déplacement, la visée et la poignée verticale laissent de la
    /// poussée du motif (x horizontal, y vertical) : les réglages du préfab (WeaponController).</summary>
    private Vector2 RecoilScale(WeaponData data, PlayerLocomotion.Stance stance, PreviewMovement movement, bool aiming, bool grip)
    {
        float multiplier = controller != null
            ? controller.RecoilMultiplier(stance, movement != PreviewMovement.Still, movement == PreviewMovement.Sneak, aiming)
            : 1f;
        float vertical = grip && catalog != null && AcceptsGrip(data) ? catalog.verticalGripRecoil : 1f;
        return new Vector2(multiplier, multiplier * vertical);
    }

    /// <summary>L'allure du tireur (m/s) : la marche ou le sneak de la posture, alourdis par l'arme.</summary>
    private float PreviewSpeed(WeaponData data, PlayerLocomotion.Stance stance, PreviewMovement movement)
    {
        if (movement == PreviewMovement.Still || locomotion == null) return 0f;
        float speed = locomotion.NominalSpeed(stance);
        if (movement == PreviewMovement.Sneak)
        {
            SerializedObject so = Serialized(locomotion);
            so.Update();
            float walk = so.FindProperty("walkSpeed").floatValue, sneak = so.FindProperty("sneakSpeed").floatValue;
            if (walk > 0f) speed *= sneak / walk;
        }
        return speed * data.moveSpeedMultiplier;
    }

    /// <summary>La cible : un robot debout, tel que le serveur le touche (le préfab joueur).</summary>
    private TargetShape Target()
    {
        StanceBody body = hitbox != null ? hitbox.Body(PlayerLocomotion.Stance.Standing) : BodyLayout.DefaultStanding;
        Vector3 eye = locomotion != null ? locomotion.EyeLocal(PlayerLocomotion.Stance.Standing) : new Vector3(0f, 1.65f, 0f);
        return TargetShape.Facing(body, eye);
    }

    private BurstSetup Setup(WeaponData data, int shots) => new BurstSetup
    {
        shots = shots,
        recoilScale = RecoilScale(data, previewStance, previewMovement, previewAiming, previewGrip),
        aiming = previewAiming,
        stance = previewStance,
        speed = PreviewSpeed(data, previewStance, previewMovement),
        laserFactor = previewLaser && catalog != null && AcceptsLaser(data) ? catalog.laserHipSpread : 1f,
        target = Target(),
        aimAtHead = previewAtHead,
        distance = previewDistance,
        maxHealth = MaxHealth,
    };

    private static string ContextLabel(PlayerLocomotion.Stance stance, PreviewMovement movement, bool aiming)
    {
        string posture = stance switch
        {
            PlayerLocomotion.Stance.Crouching => "accroupi",
            PlayerLocomotion.Stance.Prone => "allongé",
            _ => "debout",
        };
        string moving = movement switch
        {
            PreviewMovement.Sneak => "en sneak",
            PreviewMovement.Moving => "en marche",
            _ => "à l'arrêt",
        };
        return $"{(aiming ? "épaulé" : "à la hanche")}, {posture} {moving}";
    }

    private static string Side(float x) => x > 0.005f ? " à droite" : x < -0.005f ? " à gauche" : "";

    private static string Capitalized(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>Ce que fait le recul d'une arme dans un contexte, en une phrase.</summary>
    private string RecoilSummary(WeaponData data, PlayerLocomotion.Stance stance, PreviewMovement movement, bool aiming, bool grip, bool extended)
    {
        Vector2 scale = RecoilScale(data, stance, movement, aiming, grip);
        string context = Capitalized(ContextLabel(stance, movement, aiming));
        float interval = RecoilPattern.ShotInterval(data);
        if (!RecoilPattern.Chains(data))
        {
            Vector2 kick = Vector2.Scale(RecoilPattern.Kick(data, 0), scale);
            float back = data.recoilRecoverySpeed > 0f ? Mathf.Max(Mathf.Abs(kick.x), Mathf.Abs(kick.y)) / data.recoilRecoverySpeed : float.PositiveInfinity;
            return $"{context} : un coup pousse le regard de {N(kick.y)}° vers le haut{(Mathf.Abs(kick.x) > 0.005f ? $" et de {N(Mathf.Abs(kick.x))}°{Side(kick.x)}" : "")}, qui revient en {N(back)} s. "
                   + $"Un coup toutes les {N(interval)} s, plus lent que la remise à zéro du motif ({N(data.recoilResetDelay)} s) : chaque coup reprend le motif au premier.";
        }
        int shots = RecoilPattern.BurstLength(data, extended);
        var points = new List<Vector2>();
        RecoilPattern.Burst(data, shots, scale, true, points);
        Vector2 last = points[points.Count - 1];
        return $"{context} : sur une rafale de {shots} coups ({N((shots - 1) * interval)} s), sans compenser, le dernier part {N(last.y)}° plus haut"
               + (Mathf.Abs(last.x) > 0.005f ? $" et {N(Mathf.Abs(last.x))}°{Side(last.x)}" : "") + " que le premier.";
    }

    // ------------------------------------------------------------------
    // L'onglet
    // ------------------------------------------------------------------

    private void DrawRecoilTab(WeaponData data)
    {
        SerializedObject so = Serialized(data);
        so.Update();
        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        InlineField(so, "recoilPerShotDegrees", "Force (°)", 62f, 60f);
        GUILayout.Space(10f);
        InlineField(so, "recoilRecoverySpeed", "Retour du regard (°/s)", 132f, 50f);
        GUILayout.Space(10f);
        InlineField(so, "recoilResetDelay", "Remise à zéro (s)", 110f, 50f);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        so.ApplyModifiedProperties();

        bool extended = previewExtended && AcceptsMagazine(data);
        int burstLength = RecoilPattern.BurstLength(data, extended);
        bool chains = RecoilPattern.Chains(data);
        int editShots = chains ? Mathf.Max(2, burstLength) : 2;
        BurstSetup setup = Setup(data, burstLength);
        burst.Compute(data, setup);
        Vector2 scaleDegrees = setup.recoilScale * data.recoilPerShotDegrees;
        Vector2[] positions = RecoilPatternEditing.Positions(data.recoilPattern, editShots, scaleDegrees);
        bool editable = Mathf.Abs(scaleDegrees.x) > 1e-4f && Mathf.Abs(scaleDegrees.y) > 1e-4f;
        selectedShot = selectedShot >= 1 && selectedShot < editShots ? selectedShot : -1;

        EditorGUILayout.BeginHorizontal();
        Rect graphRect = GUILayoutUtility.GetRect(240f, 240f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawRecoilGraph(graphRect, data, setup, positions, editShots, scaleDegrees, editable);
        EditorGUILayout.BeginVertical(GUILayout.Width(SideWidth));
        recoilSideScroll = EditorGUILayout.BeginScrollView(recoilSideScroll);
        DrawRecoilSidePanel(data, setup, chains, burstLength, editShots, scaleDegrees, editable);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private static void InlineField(SerializedObject so, string property, string label, float labelWidth, float fieldWidth)
    {
        float previous = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = labelWidth;
        Field(so, property, label, GUILayout.Width(labelWidth + fieldWidth));
        EditorGUIUtility.labelWidth = previous;
    }

    // ------------------------------------------------------------------
    // La toile
    // ------------------------------------------------------------------

    private void DrawRecoilGraph(Rect rect, WeaponData data, in BurstSetup setup, Vector2[] positions, int editShots, Vector2 scaleDegrees, bool editable)
    {
        // L'identifiant d'abord, à chaque évènement (Layout compris) : ceux des champs du panneau de droite
        // n'en dépendent pas.
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        if (e.type == EventType.Layout) return;
        rect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);

        fitPoints.Clear();
        if (showPattern) fitPoints.AddRange(positions);
        if (showBurst)
        {
            fitPoints.AddRange(burst.aim);
            foreach (BurstPreview.Bullet bullet in burst.bullets) fitPoints.Add(bullet.degrees);
        }
        if (showTarget) AddTargetBounds(setup, fitPoints);
        recoilGraph.Fit(rect, fitPoints, 2f);
        recoilGraph.DrawBackground();

        if (showTarget) DrawTarget(recoilGraph, setup);
        if (showBurst) DrawBullets(recoilGraph, setup);
        if (showPattern) DrawPattern(recoilGraph, positions, editShots);
        // Le regard de la rafale par-dessus le motif, plus fin : ils se suivent souvent de près.
        if (showBurst) DrawAimPath(recoilGraph);
        DrawLegend(rect, editable);

        int hovered = showPattern && rect.Contains(e.mousePosition) ? recoilGraph.Pick(positions, e.mousePosition, 9f) : -1;
        if (draggedShot >= 1) hovered = draggedShot;
        if (hovered >= 0 && e.type == EventType.Repaint)
        {
            Vector2 p = positions[hovered];
            string text = hovered == 0
                ? "Coup 1 : dans l'axe"
                : $"Coup {hovered + 1} : {N(Mathf.Abs(p.x))}°{(p.x >= 0f ? " →" : " ←")}, {N(Mathf.Abs(p.y))}°{(p.y >= 0f ? " ↑" : " ↓")}";
            Vector2 size = Styles.Tooltip.CalcSize(new GUIContent(text));
            Styles.Tooltip.Draw(new Rect(e.mousePosition.x + 14f, e.mousePosition.y + 10f, size.x, size.y), text, false, false, false, false);
        }

        switch (e.GetTypeForControl(control))
        {
            case EventType.MouseDown:
                if (e.button != 0 || !rect.Contains(e.mousePosition) || !showPattern) break;
                int pick = recoilGraph.Pick(positions, e.mousePosition, 9f, 1);
                selectedShot = pick;
                if (pick >= 1 && editable)
                {
                    draggedShot = pick;
                    GUIUtility.hotControl = control;
                    recoilGraph.Frozen = true;
                    dragUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Déplacer un coup du motif de recul");
                }
                e.Use();
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != control || draggedShot < 1) break;
                Vector2 wanted = recoilGraph.ToDegrees(e.mousePosition);
                if (e.control || e.command) wanted = new Vector2(Mathf.Round(wanted.x * 10f) / 10f, Mathf.Round(wanted.y * 10f) / 10f);
                Vector2 delta = wanted - positions[draggedShot];
                var units = new Vector2(delta.x / scaleDegrees.x, delta.y / scaleDegrees.y);
                Vector2[] moved = RecoilPatternEditing.MoveShot(data.recoilPattern, draggedShot, units, e.shift, editShots);
                WriteArray(data, "Déplacer un coup du motif de recul", d => d.recoilPattern = moved);
                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != control) break;
                GUIUtility.hotControl = 0;
                draggedShot = -1;
                recoilGraph.Frozen = false;
                Undo.CollapseUndoOperations(dragUndoGroup);
                e.Use();
                break;
            case EventType.MouseMove:
                if (rect.Contains(e.mousePosition)) Repaint();
                break;
        }
    }

    /// <summary>Un angle de la cible (mètres dans son plan) en degrés, depuis le point visé.</summary>
    private static Vector2 TargetDegrees(in BurstSetup setup, Vector3 meters)
    {
        Vector2 aim = setup.target.AimPoint(setup.aimAtHead);
        return new Vector2(BurstPreview.Degrees(meters.x - aim.x, setup.distance), BurstPreview.Degrees(meters.y - aim.y, setup.distance));
    }

    private static void AddTargetBounds(in BurstSetup setup, List<Vector2> points)
    {
        foreach (ZoneShape zone in new[] { setup.target.head, setup.target.torso, setup.target.legs })
        {
            float r = BurstPreview.Degrees(zone.radius, setup.distance);
            foreach (Vector3 end in new[] { zone.a, zone.b })
            {
                Vector2 d = TargetDegrees(setup, end);
                points.Add(d + new Vector2(r, r));
                points.Add(d - new Vector2(r, r));
            }
        }
    }

    private static void DrawTarget(AngleGraph graph, in BurstSetup setup)
    {
        DrawZone(graph, setup, setup.target.legs, LegsColor);
        DrawZone(graph, setup, setup.target.torso, TorsoColor);
        DrawZone(graph, setup, setup.target.head, HeadColor);
    }

    private static void DrawZone(AngleGraph graph, in BurstSetup setup, ZoneShape zone, Color color)
    {
        graph.Capsule(TargetDegrees(setup, zone.a), TargetDegrees(setup, zone.b), BurstPreview.Degrees(zone.radius, setup.distance),
                      new Color(color.r, color.g, color.b, 0.13f), new Color(color.r, color.g, color.b, 0.55f));
    }

    private void DrawAimPath(AngleGraph graph)
    {
        graph.Polyline(burst.aim, BurstColor, 1.6f);
        foreach (Vector2 aim in burst.aim) graph.Dot(aim, 1.8f, BurstColor);
    }

    private void DrawBullets(AngleGraph graph, in BurstSetup setup)
    {
        foreach (BurstPreview.Bullet bullet in burst.bullets)
        {
            Color color = bullet.zone switch
            {
                HitboxZoneType.Head => HeadColor,
                HitboxZoneType.Torso => TorsoColor,
                HitboxZoneType.Legs => LegsColor,
                _ => MissColor,
            };
            graph.Dot(bullet.degrees, bullet.zone.HasValue ? 3f : 2.2f, color);
        }
        // Le cône à la hanche du coup choisi (ou du premier) : où sa balle pouvait partir.
        if (!setup.aiming && burst.cones.Count > 0)
        {
            int shot = selectedShot >= 0 && selectedShot < burst.cones.Count ? selectedShot : 0;
            if (burst.cones[shot] > 0f)
                graph.Ring(burst.aim[shot], burst.cones[shot] * graph.PixelsPerDegree, new Color(BurstColor.r, BurstColor.g, BurstColor.b, 0.5f), 1.2f);
        }
    }

    private void DrawPattern(AngleGraph graph, Vector2[] positions, int editShots)
    {
        graph.Polyline(positions, PatternColor, 2.5f);
        float spacing = positions.Length > 1 ? graph.PixelsPerDegree * MeanStep(positions) : 100f;
        int labelEvery = spacing >= 18f ? 1 : spacing >= 7f ? 5 : 10;
        for (int shot = 0; shot < positions.Length; shot++)
        {
            bool selected = shot == selectedShot;
            if (shot == 0) graph.Ring(positions[0], 6f, PatternColor, 2f);
            else graph.Dot(positions[shot], selected ? 6f : 4f, PatternColor);
            if (selected) graph.Ring(positions[shot], 9f, Color.white, 1.5f);
            if (shot == 0 || (shot + 1) % labelEvery == 0 || shot == editShots - 1 || selected)
                graph.Label(positions[shot], (shot + 1).ToString(), new Vector2(7f, -16f), EditorStyles.miniBoldLabel);
        }
    }

    private static float MeanStep(Vector2[] positions)
    {
        float sum = 0f;
        for (int i = 1; i < positions.Length; i++) sum += (positions[i] - positions[i - 1]).magnitude;
        return sum / (positions.Length - 1);
    }

    private void DrawLegend(Rect rect, bool editable)
    {
        if (Event.current.type != EventType.Repaint) return;
        float y = rect.y + 6f;
        void Line(Color color, string text)
        {
            EditorGUI.DrawRect(new Rect(rect.x + 8f, y + 6f, 10f, 3f), color);
            EditorStyles.miniLabel.Draw(new Rect(rect.x + 22f, y, rect.width - 30f, 15f), text, false, false, false, false);
            y += 15f;
        }
        if (showPattern) Line(PatternColor, editable ? "Le motif : glisser un coup (Maj : avec la suite, Ctrl : par 0,1°)" : "Le motif (recul nul dans ce contexte)");
        if (showBurst) Line(BurstColor, "La rafale en jeu : le regard, avec son retour ; les balles");
        if (showTarget) Line(TorsoColor, $"Un robot debout à {N(previewDistance)} m : tête, buste, jambes");
    }

    // ------------------------------------------------------------------
    // Le panneau de droite
    // ------------------------------------------------------------------

    private void DrawRecoilSidePanel(WeaponData data, in BurstSetup setup, bool chains, int burstLength, int editShots, Vector2 scaleDegrees, bool editable)
    {
        DrawPreviewContext(data, true);

        GUILayout.Space(6f);
        EditorGUILayout.BeginHorizontal();
        showPattern = EditorGUILayout.ToggleLeft("Motif", showPattern, GUILayout.Width(70f));
        showBurst = EditorGUILayout.ToggleLeft("Rafale en jeu", showBurst, GUILayout.Width(110f));
        showTarget = EditorGUILayout.ToggleLeft("Cible", showTarget, GUILayout.Width(70f));
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);
        Note(RecoilSummary(data, previewStance, previewMovement, previewAiming, previewGrip, previewExtended && AcceptsMagazine(data)));
        Note(RecoveryWarning(data, chains, editShots, scaleDegrees));
        Note(HitsSummary(data, setup));

        GUILayout.Space(8f);
        GUILayout.Label("Outils", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Miroir ↔", "La gauche pour la droite.")))
            WriteArray(data, "Motif de recul en miroir", d => d.recoilPattern = RecoilPatternEditing.Mirrored(d.recoilPattern));
        if (GUILayout.Button(new GUIContent("Par défaut", "Le motif d'une nouvelle arme : il monte, puis part en « S ».")))
            WriteArray(data, "Motif de recul par défaut", d => d.recoilPattern = WeaponData.DefaultRecoilPattern());
        if (GUILayout.Button(new GUIContent("Copier de…", "Reprendre le motif d'une autre arme (la force reste celle-ci).")))
            ShowCopyPatternMenu(data);
        EditorGUILayout.EndHorizontal();

        int reachable = chains ? editShots - 1 : 1;
        GUILayout.Space(8f);
        GUILayout.Label(new GUIContent("Coup par coup", "La poussée de chaque coup, en degrés dans le contexte de l'aperçu : elle déplace le coup suivant."), EditorStyles.boldLabel);
        if (!chains)
            Note("Seul le premier coup du motif compte : l'arme tire moins vite que la remise à zéro du motif.");
        DrawKickTable(data, reachable, scaleDegrees, editable);

        Vector2[] pattern = data.recoilPattern ?? new Vector2[0];
        if (pattern.Length < reachable)
        {
            Note($"Coups {pattern.Length + 1} à {reachable} : la poussée du coup {pattern.Length} se répète.");
            if (GUILayout.Button($"Les détailler un par un ({reachable} coups)"))
                WriteArray(data, "Détailler le motif de recul", d => d.recoilPattern = RecoilPatternEditing.Extended(d.recoilPattern, reachable));
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Un coup"))
            WriteArray(data, "Ajouter un coup au motif", d => d.recoilPattern = RecoilPatternEditing.Resized(d.recoilPattern, (d.recoilPattern?.Length ?? 0) + 1));
        using (new EditorGUI.DisabledScope(pattern.Length <= 1))
        {
            if (GUILayout.Button("− Le dernier"))
                WriteArray(data, "Retirer un coup du motif", d => d.recoilPattern = RecoilPatternEditing.Resized(d.recoilPattern, d.recoilPattern.Length - 1));
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10f);
    }

    /// <summary>Le contexte de l'aperçu, commun au recul et à la gerbe.</summary>
    private void DrawPreviewContext(WeaponData data, bool recoil)
    {
        GUILayout.Label("Aperçu", EditorStyles.boldLabel);
        previewStance = (PlayerLocomotion.Stance)GUILayout.Toolbar((int)previewStance, new[] { "Debout", "Accroupi", "Allongé" });
        previewMovement = (PreviewMovement)GUILayout.Toolbar((int)previewMovement, new[] { "À l'arrêt", "Sneak", "En marche" });
        previewAiming = GUILayout.Toolbar(previewAiming ? 0 : 1, new[] { "Épaulé", "À la hanche" }) == 0;
        GUILayout.Space(2f);
        if (recoil)
        {
            using (new EditorGUI.DisabledScope(!AcceptsGrip(data) || catalog == null))
                previewGrip = EditorGUILayout.ToggleLeft($"Poignée verticale (recul vertical ×{N(catalog != null ? catalog.verticalGripRecoil : 1f)})", previewGrip);
        }
        using (new EditorGUI.DisabledScope(!AcceptsLaser(data) || catalog == null))
            previewLaser = EditorGUILayout.ToggleLeft($"Laser tactique (dispersion à la hanche ×{N(catalog != null ? catalog.laserHipSpread : 1f)})", previewLaser);
        if (recoil)
        {
            using (new EditorGUI.DisabledScope(!AcceptsMagazine(data)))
                previewExtended = EditorGUILayout.ToggleLeft(data.UsesHeat ? "Cellule grande capacité" : "Grand chargeur", previewExtended);
        }
        float previous = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 90f;
        previewDistance = EditorGUILayout.Slider("Cible à (m)", previewDistance, 2f, 100f);
        EditorGUIUtility.labelWidth = previous;
        previewAtHead = GUILayout.Toolbar(previewAtHead ? 1 : 0, new[] { "Visé au buste", "Visé à la tête" }) == 1;
        if (recoil && controller != null)
        {
            Vector2 scale = RecoilScale(data, previewStance, previewMovement, previewAiming, previewGrip);
            Note($"Dans ce contexte, un coup pousse de {Mathf.RoundToInt(scale.y * 100f)} % de la force"
                 + (Mathf.Approximately(scale.x, scale.y) ? "." : $" vers le haut, {Mathf.RoundToInt(scale.x * 100f)} % de côté.")
                 + " La part de chaque posture se règle dans « Réglages communs ».");
        }
    }

    /// <summary>
    /// Quand le retour du regard rattrape presque chaque poussée avant le coup suivant, la rafale ne
    /// monte presque pas en jeu : le motif ne se sent plus. L'éditeur le dit, plutôt que de laisser
    /// dessiner un motif qu'aucun joueur ne verra.
    /// </summary>
    private string RecoveryWarning(WeaponData data, bool chains, int editShots, Vector2 scaleDegrees)
    {
        if (!chains || burst.aim.Count < 2) return "";
        Vector2[] raw = RecoilPatternEditing.Positions(data.recoilPattern, Mathf.Min(editShots, burst.aim.Count), scaleDegrees);
        float rawTop = 0f, top = 0f;
        foreach (Vector2 p in raw) rawTop = Mathf.Max(rawTop, p.magnitude);
        foreach (Vector2 p in burst.aim) top = Mathf.Max(top, p.magnitude);
        if (rawTop < 0.5f || top >= 0.5f * rawTop) return "";
        return $"⚠ Dans ce contexte, le retour du regard ({N(data.recoilRecoverySpeed)}°/s) rattrape presque chaque poussée avant le coup suivant : "
               + $"la rafale s'écarte de {N(top)}° au plus, quand le motif seul irait à {N(rawTop)}°. Pour que le motif se sente, baissez le retour du regard ou montez la force.";
    }

    /// <summary>Ce que la rafale de l'aperçu fait au robot.</summary>
    private string HitsSummary(WeaponData data, in BurstSetup setup)
    {
        int total = burst.bullets.Count;
        if (total == 0) return "";
        string unit = data.PelletCount > 1 ? "plombs" : "balles";
        string where = setup.aimAtHead ? "visé à la tête" : "visé au buste";
        string text = $"À {N(setup.distance)} m, {where}, sans compenser, sur {total} {unit} : {burst.HeadHits} à la tête, {burst.TorsoHits} au buste, {burst.LegHits} aux jambes, {burst.Misses} à côté. ";
        text += burst.KillShot >= 0
            ? $"Le robot tombe au coup {burst.KillShot + 1}, en {N(burst.KillTime)} s."
            : $"La rafale de {burst.Shots} coups ne suffit pas à le neutraliser.";
        return text;
    }

    private void DrawKickTable(WeaponData data, int reachable, Vector2 scaleDegrees, bool editable)
    {
        Vector2[] pattern = data.recoilPattern ?? new Vector2[0];
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(70f));
        GUILayout.Label(new GUIContent("→ (°)", "Vers la droite ; négatif vers la gauche."), EditorStyles.miniBoldLabel, GUILayout.Width(80f));
        GUILayout.Label(new GUIContent("↑ (°)", "Vers le haut."), EditorStyles.miniBoldLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < pattern.Length; i++)
        {
            bool used = i < reachable;
            bool selectedRow = selectedShot == i + 1;
            Rect row = EditorGUILayout.BeginHorizontal();
            if (selectedRow && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, new Color(PatternColor.r, PatternColor.g, PatternColor.b, 0.18f));
            if (GUILayout.Button(new GUIContent($"Coup {i + 1}", used ? "Choisir ce coup : il déplace le coup suivant." : "Au-delà de la rafale : ne sert pas."), used ? EditorStyles.label : EditorStyles.centeredGreyMiniLabel, GUILayout.Width(70f)))
                selectedShot = i + 1;
            using (new EditorGUI.DisabledScope(!editable || !used))
            {
                Vector2 degrees = Vector2.Scale(pattern[i], scaleDegrees);
                EditorGUI.BeginChangeCheck();
                float x = EditorGUILayout.DelayedFloatField(Round(degrees.x), GUILayout.Width(80f));
                float y = EditorGUILayout.DelayedFloatField(Round(degrees.y), GUILayout.Width(80f));
                if (EditorGUI.EndChangeCheck() && editable)
                {
                    // Seul l'axe modifié change : l'autre garderait sinon l'arrondi de l'affichage.
                    int index = i;
                    var kick = new Vector2(x == Round(degrees.x) ? pattern[i].x : x / scaleDegrees.x,
                                           y == Round(degrees.y) ? pattern[i].y : y / scaleDegrees.y);
                    WriteArray(data, "Modifier un coup du motif de recul", d =>
                    {
                        var copy = (Vector2[])d.recoilPattern.Clone();
                        copy[index] = kick;
                        d.recoilPattern = copy;
                    });
                }
            }
            if (!used) GUILayout.Label("inutilisé", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

    private void ShowCopyPatternMenu(WeaponData data)
    {
        var menu = new GenericMenu();
        foreach (int slot in Arsenal.DisplayOrder(loadout))
        {
            WeaponData other = loadout[slot];
            if (other == null || other == data) continue;
            menu.AddItem(new GUIContent($"{other.weaponName} ({(other.recoilPattern?.Length ?? 0)} coups)"), false, () =>
                WriteArray(data, "Copier un motif de recul", d => d.recoilPattern = (Vector2[])other.recoilPattern.Clone()));
        }
        menu.ShowAsContext();
    }
}
