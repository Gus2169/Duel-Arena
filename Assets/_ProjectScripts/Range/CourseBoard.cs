using UnityEngine;

/// <summary>
/// Le tableau des meilleurs temps de la Fosse, au mur du départ (2026-10-16), comme celui de Modern
/// Warfare 2 : un temps par arme de la panoplie, plus la panoplie mixte (CourseRecords). Il se réécrit
/// quand un record tombe, et une fois le joueur apparu (les noms des armes viennent de sa panoplie).
/// Catégorie C : rien ne le relit.
///
/// Depuis le 2026-10-28 : le titre, puis deux colonnes (les armes 1 à 6 ; 7 à 12 et la panoplie mixte) : il n'en
/// montrait que quatre depuis la refonte de l'arsenal. Ses textes se dessinent par DuelArena/TexteMonde, qui teste la
/// profondeur (le matériau de police d'Unity passait à travers les murs) ; la texture de la police, qui change en
/// cours de jeu, y est posée par un bloc de propriétés.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TextMesh))]
public class CourseBoard : MonoBehaviour
{
    /// <summary>Les places de la panoplie listées au tableau (l'arsenal en a douze).</summary>
    public const int Slots = 12;

    [Tooltip("Les deux colonnes des temps : les armes 1 à 6, puis 7 à 12 et la panoplie mixte. Sans elles, tout va dans le titre.")]
    [SerializeField] private TextMesh left;
    [SerializeField] private TextMesh right;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private TextMesh title;
    private MaterialPropertyBlock block;
    private bool dirty = true;
    private string lastNames;

    private void Awake() => title = GetComponent<TextMesh>();

    // Hors jeu, Awake ne passe qu'à l'ouverture de la scène : le titre se retrouve après une recompilation.
    private void OnValidate() => title = GetComponent<TextMesh>();

    private void OnEnable()
    {
        CourseRecords.Changed += MarkDirty;
        Font.textureRebuilt += OnFontRebuilt;
    }

    private void OnDisable()
    {
        CourseRecords.Changed -= MarkDirty;
        Font.textureRebuilt -= OnFontRebuilt;
    }

    private void MarkDirty() => dirty = true;

    private void OnFontRebuilt(Font font)
    {
        if (title != null && font == title.font) ApplyFontTexture();
    }

    private void Update()
    {
        // Hors jeu (l'éditeur, les captures du menu), le tableau garde son texte et montre seulement ses lettres : la
        // texture de la police va dans un bloc de propriétés, qui ne touche pas à la scène.
        if (!Application.isPlaying)
        {
            ApplyFontTexture();
            return;
        }
        // Les noms des armes ne sont connus qu'une fois le joueur apparu : une seule vérification par
        // seconde, et le texte n'est refait qu'au changement.
        if (!dirty && Time.frameCount % 60 != 0) return;
        string names = ShootingCourseHud.WeaponName(0) + ShootingCourseHud.WeaponName(Slots - 1);
        if (!dirty && names == lastNames) return;
        lastNames = names;
        dirty = false;

        if (left == null || right == null)
        {
            var all = new System.Text.StringBuilder("LA FOSSE\nmeilleurs temps\n\n");
            for (int slot = 0; slot < Slots; slot++) AppendLine(all, slot);
            AppendLine(all, CourseRecords.MixedLoadout);
            title.text = all.ToString();
        }
        else
        {
            title.text = "LA FOSSE · MEILLEURS TEMPS";
            var a = new System.Text.StringBuilder();
            var b = new System.Text.StringBuilder();
            for (int slot = 0; slot < Slots; slot++) AppendLine(slot < Slots / 2 ? a : b, slot);
            AppendLine(b, CourseRecords.MixedLoadout);
            left.text = a.ToString();
            right.text = b.ToString();
        }
        // Écrire un texte peut agrandir la texture de la police : on la reprend après.
        ApplyFontTexture();
    }

    private void ApplyFontTexture()
    {
        Texture texture = title != null && title.font != null && title.font.material != null ? title.font.material.mainTexture : null;
        if (texture == null) return;
        block ??= new MaterialPropertyBlock();
        foreach (TextMesh mesh in new[] { title, left, right })
        {
            if (mesh == null) continue;
            var r = mesh.GetComponent<MeshRenderer>();
            r.GetPropertyBlock(block);
            block.SetTexture(MainTexId, texture);
            r.SetPropertyBlock(block);
        }
    }

    private static void AppendLine(System.Text.StringBuilder sb, int weaponKey)
    {
        float best = CourseRecords.Best(weaponKey);
        sb.Append(ShootingCourseHud.WeaponName(weaponKey).ToUpperInvariant()).Append("   ")
          .Append(float.IsNaN(best) ? "—" : ShootingCourseHud.Seconds(best)).Append('\n');
    }
}
