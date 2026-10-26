using UnityEngine;

/// <summary>
/// Le tableau des meilleurs temps de la Fosse, au mur du départ (2026-10-16), comme celui de Modern
/// Warfare 2 : un temps par arme de la panoplie, plus la panoplie mixte (CourseRecords). Il se réécrit
/// quand un record tombe, et une fois le joueur apparu (les noms des armes viennent de sa panoplie).
/// Catégorie C : rien ne le relit.
/// </summary>
[RequireComponent(typeof(TextMesh))]
public class CourseBoard : MonoBehaviour
{
    private TextMesh text;
    private bool dirty = true;
    private string lastNames;

    private void Awake() => text = GetComponent<TextMesh>();
    private void OnEnable() => CourseRecords.Changed += MarkDirty;
    private void OnDisable() => CourseRecords.Changed -= MarkDirty;
    private void MarkDirty() => dirty = true;

    private void Update()
    {
        // Les noms des armes ne sont connus qu'une fois le joueur apparu : une seule vérification par
        // seconde, et le texte n'est refait qu'au changement.
        if (!dirty && Time.frameCount % 60 != 0) return;
        string names = ShootingCourseHud.WeaponName(0) + ShootingCourseHud.WeaponName(3);
        if (!dirty && names == lastNames) return;
        lastNames = names;
        dirty = false;

        var sb = new System.Text.StringBuilder("LA FOSSE\nmeilleurs temps\n\n");
        for (int slot = 0; slot < 4; slot++) AppendLine(sb, ShootingCourseHud.WeaponName(slot), CourseRecords.Best(slot));
        AppendLine(sb, ShootingCourseHud.WeaponName(CourseRecords.MixedLoadout), CourseRecords.Best(CourseRecords.MixedLoadout));
        text.text = sb.ToString();
    }

    private static void AppendLine(System.Text.StringBuilder sb, string weapon, float best)
        => sb.Append(weapon).Append("   ").Append(float.IsNaN(best) ? "—" : ShootingCourseHud.Seconds(best)).Append('\n');
}
