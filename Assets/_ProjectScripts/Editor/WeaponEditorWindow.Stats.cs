using UnityEditor;
using UnityEngine;

/// <summary>L'onglet « Caractéristiques » : tous les chiffres d'une arme, rangés comme on les règle, et
/// sous chaque groupe ce qu'ils donnent.</summary>
public partial class WeaponEditorWindow
{
    [SerializeField] private Vector2 statsScroll;

    private static readonly string[] FamilyChoices =
    {
        Arsenal.FamilyLabel(WeaponFamily.AssaultRifle),
        Arsenal.FamilyLabel(WeaponFamily.Precision),
        Arsenal.FamilyLabel(WeaponFamily.Shotgun),
        Arsenal.FamilyLabel(WeaponFamily.Pistol),
    };

    private void DrawStatsTab(WeaponData data)
    {
        SerializedObject so = Serialized(data);
        so.Update();
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 250f;
        statsScroll = EditorGUILayout.BeginScrollView(statsScroll);
        GUILayout.Space(4f);

        Section("Identité");
        Field(so, "weaponName", "Nom");
        Choice(so, "family", "Famille", FamilyChoices);
        Field(so, "role", "Rôle (montré au choix de l'arme)");

        DrawDamageSection(so, data);
        DrawFireRateSection(so, data);
        DrawRangeSection(so, data);
        DrawAmmoSection(so, data);
        DrawChargeSection(so, data);
        DrawHipFireSection(so, data);
        DrawHandlingSection(so, data);
        DrawRecoilSummarySection(so, data);
        DrawPelletSummarySection(so, data);

        Section("Coup mortel");
        Field(so, "killKnockback", "Projection de l'épave (m/s, 0 = poussée simple)");
        if (data.killKnockback > 0f) Field(so, "killKnockbackLift", "Soulèvement de l'épave (m/s)");

        Section("Accessoires acceptés");
        Mask(so, "acceptedAttachments", "Emplacements", new[] { "Viseur", "Poignée", "Laser", "Grand chargeur" });
        Mask(so, "acceptedSights", "Familles de viseurs", new[] { "Points rouges", "Holographiques", "Réflex" });

        Section("Le tir se voit");
        Field(so, "shotIsBeam", "Rayon (arme laser), sinon flamme de bouche");
        if (data.shotIsBeam) Field(so, "tracerColor", "Couleur du rayon");

        GUILayout.Space(12f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Le modèle, les sons et le reste se règlent dans l'inspecteur.", Styles.Small);
        if (GUILayout.Button("Ouvrir dans l'inspecteur", GUILayout.Width(170f)))
        {
            Selection.activeObject = data;
            EditorGUIUtility.PingObject(data);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(12f);

        EditorGUILayout.EndScrollView();
        EditorGUIUtility.labelWidth = previousLabelWidth;
        so.ApplyModifiedProperties();
    }

    private void DrawDamageSection(SerializedObject so, WeaponData data)
    {
        Section("Dégâts");
        bool pellets = data.PelletCount > 1;
        Field(so, "damagePerHit", pellets ? "Dégâts d'un plomb, au buste" : "Dégâts d'une balle, au buste");
        Field(so, "headMultiplier", "Multiplicateur à la tête");
        Field(so, "legsMultiplier", "Multiplicateur aux jambes");

        string unit = pellets ? $"Une décharge complète ({data.PelletCount} plombs)" : data.UsesCharge ? "Un tir chargé à fond" : "Un tir";
        float near = Mathf.Max(0f, data.falloffStart);
        Note($"{unit}, de près : buste {N(data.DamageAt(HitboxZoneType.Torso, near) * data.PelletCount)}, "
             + $"tête {N(data.DamageAt(HitboxZoneType.Head, near) * data.PelletCount)}, "
             + $"jambes {N(data.DamageAt(HitboxZoneType.Legs, near) * data.PelletCount)}.");
        int body = ShotsToKill(data, HitboxZoneType.Torso, near), head = ShotsToKill(data, HitboxZoneType.Head, near), legs = ShotsToKill(data, HitboxZoneType.Legs, near);
        Note($"Pour neutraliser un robot ({N(MaxHealth)} PV) : {Count(body, "coup", "coups")} au buste, {head} à la tête, {legs} aux jambes.");
    }

    private void DrawFireRateSection(SerializedObject so, WeaponData data)
    {
        Section("Cadence");
        Field(so, "shotsPerSecond", "Cadence (coups par seconde)");
        Field(so, "isAutomatic", "Automatique (gâchette tenue)");
        float interval = data.shotsPerSecond > 0f ? 1f / data.shotsPerSecond : 0f;
        Note($"{Mathf.RoundToInt(data.shotsPerSecond * 60f)} coups par minute : un coup toutes les {N(interval)} s.");
        float near = Mathf.Max(0f, data.falloffStart);
        int body = ShotsToKill(data, HitboxZoneType.Torso, near), head = ShotsToKill(data, HitboxZoneType.Head, near);
        string charged = data.UsesCharge ? ", chargés à fond" : "";
        Note($"Temps pour neutraliser, de près, tous les coups au but{charged} : {N(TimeToKill(data, body))} s au buste, {N(TimeToKill(data, head))} s à la tête.");
    }

    private void DrawRangeSection(SerializedObject so, WeaponData data)
    {
        Section("Portée");
        Field(so, "maxRange", "Portée maximale (m)");
        Field(so, "falloffStart", "Dégâts pleins jusqu'à (m)");
        Field(so, "falloffEnd", "Baisse finie à (m)");
        Field(so, "minDamageMultiplier", "Ce qu'il reste des dégâts au-delà");
        Note(data.falloffEnd > data.falloffStart
            ? $"Pleins jusqu'à {N(data.falloffStart)} m, puis en baisse jusqu'à ×{N(data.minDamageMultiplier)} à {N(data.falloffEnd)} m."
            : "Aucune baisse : les dégâts sont les mêmes à toute distance (« Baisse finie à » doit dépasser « Dégâts pleins jusqu'à »).");
        DrawShotsToKillChart(data);
    }

    /// <summary>Les coups pour neutraliser un robot selon la distance, au buste et à la tête, jusqu'à la
    /// portée de l'arme. Des traits droits seulement (EditorGUI.DrawRect) : il est dans une zone qui défile.</summary>
    private void DrawShotsToKillChart(WeaponData data)
    {
        Rect area = GUILayoutUtility.GetRect(200f, 150f, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return;
        var torsoColor = new Color(1f, 0.8f, 0.25f);
        var headColor = new Color(1f, 0.35f, 0.35f);
        float legend = LegendItem(area.x + 34f, area.y + 2f, "Coups pour neutraliser un robot, selon la distance :", null);
        legend = LegendItem(legend + 6f, area.y + 2f, "au buste", torsoColor);
        LegendItem(legend + 6f, area.y + 2f, "à la tête", headColor);
        var rect = new Rect(area.x + 34f, area.y + 22f, area.width - 44f, area.height - 42f);
        bool dark = EditorGUIUtility.isProSkin;
        EditorGUI.DrawRect(rect, dark ? new Color(0.13f, 0.13f, 0.14f) : new Color(0.93f, 0.93f, 0.93f));

        float range = Mathf.Max(1f, data.maxRange);
        int most = 1;
        for (int i = 0; i <= 40; i++)
            most = Mathf.Max(most, ShotsToKill(data, HitboxZoneType.Torso, range * i / 40f));
        int top = most + 1;
        float Y(int shots) => rect.yMax - rect.height * shots / top;
        Color grid = dark ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.08f);
        GUIStyle tick = EditorStyles.miniLabel;
        int every = top > 12 ? 5 : top > 6 ? 2 : 1;
        for (int shots = 1; shots < top; shots++)
        {
            if (shots % every != 0 && shots != 1) continue;
            EditorGUI.DrawRect(new Rect(rect.x, Y(shots), rect.width, 1f), grid);
            tick.Draw(new Rect(area.x, Y(shots) - 8f, 30f, 16f), shots.ToString(), false, false, false, false);
        }
        foreach (float meters in new[] { data.falloffStart, data.falloffEnd, range })
        {
            if (meters <= 0f || meters > range) continue;
            float x = rect.x + rect.width * meters / range;
            EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), grid);
            tick.Draw(new Rect(x - 20f, rect.yMax + 2f, 50f, 16f), N(meters) + " m", false, false, false, false);
        }

        DrawSteps(data, HitboxZoneType.Head, rect, range, top, headColor);
        DrawSteps(data, HitboxZoneType.Torso, rect, range, top, torsoColor);
    }

    /// <summary>Un élément de légende (un trait de couleur, puis son texte) ; rend où commence le suivant.</summary>
    private static float LegendItem(float x, float y, string text, Color? color)
    {
        if (color.HasValue)
        {
            EditorGUI.DrawRect(new Rect(x, y + 7f, 14f, 3f), color.Value);
            x += 18f;
        }
        Vector2 size = Styles.SmallBold.CalcSize(new GUIContent(text));
        Styles.SmallBold.Draw(new Rect(x, y, size.x, 16f), text, false, false, false, false);
        return x + size.x;
    }

    private void DrawSteps(WeaponData data, HitboxZoneType zone, Rect rect, float range, int top, Color color)
    {
        int columns = Mathf.Max(2, Mathf.RoundToInt(rect.width));
        float previousY = float.NaN;
        for (int column = 0; column < columns; column++)
        {
            float meters = range * column / (columns - 1);
            int shots = Mathf.Min(ShotsToKill(data, zone, meters), top);
            float y = rect.yMax - rect.height * shots / top;
            float x = rect.x + column * rect.width / (columns - 1);
            EditorGUI.DrawRect(new Rect(x, y - 1f, 1.5f, 2f), color);
            if (!float.IsNaN(previousY) && !Mathf.Approximately(previousY, y))
                EditorGUI.DrawRect(new Rect(x, Mathf.Min(previousY, y) - 1f, 1.5f, Mathf.Abs(previousY - y) + 2f), color);
            previousY = y;
        }
    }

    private void DrawAmmoSection(SerializedObject so, WeaponData data)
    {
        if (!data.UsesHeat)
        {
            Section("Chargeur");
            Field(so, "magazineSize", "Balles par chargeur");
            Field(so, "reloadPerRound", "Rechargement cartouche par cartouche");
            Field(so, "reloadDuration", data.reloadPerRound ? "Une cartouche (s)" : "Rechargement (s)");
            int extended = AttachmentCatalog.MagazineSize(data, true, catalog);
            float reload = AttachmentCatalog.ReloadDuration(data, true, catalog);
            float full = data.reloadPerRound ? data.reloadDuration * data.magazineSize : data.reloadDuration;
            Note((data.reloadPerRound ? $"Recharger le chargeur vide : {N(full)} s. " : "")
                 + $"Avec le grand chargeur : {extended} balles, {(data.reloadPerRound ? $"{N(reload)} s par cartouche" : $"rechargement en {N(reload)} s")}.");
            Note($"Vider le chargeur au plus vite : {N((data.magazineSize - 1) * RecoilPattern.ShotInterval(data))} s.");
            if (GUILayout.Button(new GUIContent("En faire une arme laser (chaleur au lieu de balles)", "Une arme laser chauffe en tirant et refroidit seule ; surchauffée, elle se vide d'elle-même (GDD § 6)."), GUILayout.Width(330f)))
            {
                WriteArray(data, "Arme laser", d => d.heatPerShot = 1f / Mathf.Max(1, d.magazineSize));
                GUIUtility.ExitGUI();
            }
            return;
        }

        Section("Chaleur (arme laser)");
        Field(so, "heatPerShot", "Chaleur d'un tir (1 = la surchauffe)");
        Field(so, "heatCoolDelay", "Refroidit après (s sans tirer)");
        Field(so, "heatCoolTime", "Refroidissement complet (s)");
        Field(so, "reloadDuration", "Vidange voulue (s, la touche de rechargement)");
        Field(so, "overheatVentDuration", "Vidange forcée par la surchauffe (s)");
        int shots = WeaponController.ShotsBeforeOverheat(WeaponController.HeatRulesFor(data, false), data.shotsPerSecond);
        int bigCell = WeaponController.ShotsBeforeOverheat(WeaponController.HeatRulesFor(data, true), data.shotsPerSecond);
        Note($"{Count(shots, "tir", "tirs")} d'affilée au plus vite avant la surchauffe ({bigCell} avec la cellule grande capacité).");
        if (GUILayout.Button(new GUIContent("Revenir à un chargeur", "La chaleur à zéro : l'arme compte des balles."), GUILayout.Width(330f)))
        {
            WriteArray(data, "Arme à chargeur", d => d.heatPerShot = 0f);
            GUIUtility.ExitGUI();
        }
    }

    private void DrawChargeSection(SerializedObject so, WeaponData data)
    {
        Section("Tir chargé");
        Field(so, "chargeTime", "Charge à fond en (s, 0 = tir à l'appui)");
        if (!data.UsesCharge)
        {
            Note("L'arme tire à l'appui. Une charge (le FX-2) fait tirer quand on lâche la gâchette.");
            return;
        }
        Field(so, "unchargedDamage", "Tir lâché sans charge (part des dégâts)");
        Note($"Au buste : {N(data.DamageAt(HitboxZoneType.Torso, 0f, 0f))} dégâts sans charge, {N(data.DamageAt(HitboxZoneType.Torso, 0f, 0.5f))} à mi-charge, {N(data.DamageAt(HitboxZoneType.Torso, 0f, 1f))} chargé à fond.");
    }

    private void DrawHipFireSection(SerializedObject so, WeaponData data)
    {
        Section("Précision à la hanche");
        Field(so, "hipSpread", "Cône à l'arrêt (°)");
        Field(so, "hipSpreadMoving", "Cône en marchant (°)");
        Field(so, "hipSpreadPerShot", "Ouverture par coup de rafale (°)");
        Field(so, "hipSpreadMaxBloom", "Ouverture de rafale au plus (°)");
        Field(so, "hipSpreadRecovery", "Refermeture de la rafale (°/s)");
        Choice(so, "aimCurve", "Précision en épaulant", new[] { "À la fin (fusils d'assaut)", "En ligne droite (précision)", "Tôt (pistolets, pompes)" });
        Field(so, "hipCrosshair", "Réticule à la hanche");
        if (data.PelletCount > 1 && data.hipSpread <= 0f)
        {
            Note("Arme à gerbe : sa gerbe tient lieu de cône à la hanche (onglet « Gerbe »).");
            return;
        }
        float still = HipFireSpread.Cone(data, 0f, PlayerLocomotion.Stance.Standing, 0f, 0f, 1f);
        float walking = HipFireSpread.Cone(data, HipFireSpread.ReferenceSpeed, PlayerLocomotion.Stance.Standing, 0f, 0f, 1f);
        float sprint = HipFireSpread.Cone(data, HipFireSpread.ReferenceSpeed * HipFireSpread.MaxSpeedFactor, PlayerLocomotion.Stance.Standing, 0f, 0f, 1f);
        Note($"Debout : {N(still)}° à l'arrêt, {N(walking)}° en marchant, {N(sprint)}° dans l'élan d'un sprint ; accroupi ×{N(HipFireSpread.CrouchFactor)}, allongé ×{N(HipFireSpread.ProneFactor)}, laser ×{N(catalog != null ? catalog.laserHipSpread : 1f)}. Épaulée, la balle part dans l'axe.");
        if (data.hipSpreadPerShot > 0f)
            Note($"Une rafale ouvre le cône de {N(data.hipSpreadPerShot)}° par coup, jusqu'à {N(data.hipSpreadMaxBloom)}° de plus, refermé à {N(data.hipSpreadRecovery)}°/s après {N(HipFireSpread.BloomRecoveryDelay)} s sans tirer.");
    }

    private void DrawHandlingSection(SerializedObject so, WeaponData data)
    {
        Section("Maniement");
        Field(so, "aimTime", "Épaulement (s)");
        Field(so, "scopeMagnification", "Grossissement de la lunette (1 = sans)");
        Field(so, "raiseTime", "Sortie de l'arme (s)");
        Field(so, "sprintToFireTime", "Sortie de sprint (s avant de tirer)");
        Field(so, "moveSpeedMultiplier", "Poids : vitesse de déplacement (×)");
        if (locomotion != null)
            Note($"En marchant : {N(locomotion.NominalSpeed(PlayerLocomotion.Stance.Standing) * data.moveSpeedMultiplier)} m/s. Avec la poignée inclinée, épaulée en {N(AttachmentCatalog.AimTime(data, GripKind.Angled, catalog))} s.");
    }

    private void DrawRecoilSummarySection(SerializedObject so, WeaponData data)
    {
        Section("Recul");
        Field(so, "recoilPerShotDegrees", "Force (° par coup du motif)");
        Field(so, "recoilRecoverySpeed", "Retour du regard (°/s)");
        Field(so, "recoilResetDelay", "Remise à zéro du motif (s sans tirer)");
        Note(RecoilSummary(data, PlayerLocomotion.Stance.Standing, PreviewMovement.Still, true, false, false));
        if (GUILayout.Button("Dessiner le motif de recul →", GUILayout.Width(330f))) tab = Tab.Recoil;
    }

    private void DrawPelletSummarySection(SerializedObject so, WeaponData data)
    {
        Section("Gerbe");
        if (data.PelletCount > 1)
        {
            Field(so, "aimPatternScale", "Resserrement épaulé (1 = la même)");
            Note($"{data.PelletCount} plombs, écartés de {N(MeanPelletOffset(data))}° en moyenne à la hanche.");
        }
        else
        {
            Note("Une seule balle, dans l'axe.");
        }
        if (GUILayout.Button("Dessiner la gerbe →", GUILayout.Width(330f))) tab = Tab.Pellets;
    }

    private static float MeanPelletOffset(WeaponData data)
    {
        if (data.PelletCount <= 1) return 0f;
        float sum = 0f;
        for (int i = 0; i < data.PelletCount; i++) sum += data.PelletOffset(i, false).magnitude;
        return sum / data.PelletCount;
    }
}
