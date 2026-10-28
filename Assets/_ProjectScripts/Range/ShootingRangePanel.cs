using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// La fenêtre du stand de tir (2026-10-16, demandée par l'utilisateur) : B l'ouvre et la ferme (Échap la
/// ferme ; depuis le 2026-10-27, Échap ouvre sinon le menu de pause du stand et ses Options, PauseMenu,
/// qui peut aussi ouvrir cette fenêtre). On y prend une arme, on change son modèle, sa finition et ses
/// accessoires, ça s'applique tout de suite (le serveur borne et publie, comme à l'apparition :
/// PlayerWeaponLooks.ServerSet, PlayerWeaponAttachments.ServerSet, qui remplit aussi les chargeurs). La
/// fiche de l'arme se lit à côté (WeaponStatSheet). On peut garder la configuration pour ses parties
/// (GameSettings), relever les mannequins, ou revenir au menu.
///
/// Elle, ou le menu de pause, ouverts, elle libère la souris et coupe les commandes de jeu
/// (PlayerInputReader.Mode.None) ; tous deux fermés, elle verrouille la souris, comme le HUD de la partie
/// (GameHud). OnGUI, outil de test.
/// </summary>
[DisallowMultipleComponent]
public class ShootingRangePanel : MonoBehaviour
{
    /// <summary>La fenêtre est-elle ouverte ? (le réticule se tait : CrosshairUI)</summary>
    public static bool IsOpen { get; private set; }

    private static ShootingRangePanel instance;

    /// <summary>Ouvre la fenêtre (le bouton du menu de pause du stand).</summary>
    public static void Open()
    {
        if (instance != null) instance.SetOpen(true);
    }

    /// <summary>Ferme la fenêtre (Échap, lu par PauseMenu).</summary>
    public static void Close()
    {
        if (instance != null) instance.SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        if (open && weapon != null) shownSlot = weapon.LocalSlot;
    }

    private static readonly string[] GripLabels = { "Aucune", "Verticale", "Inclinée" };

    private PlayerInputReader input;
    private WeaponController weapon;
    private PlayerWeaponLooks looksSource;
    private PlayerWeaponAttachments attachmentsSource;
    private int shownSlot = -1;
    // Les places de l'arsenal, famille par famille (rangées une fois : la fenêtre se dessine à chaque image).
    [System.NonSerialized] private List<int>[] familySlots;
    // Le bouton de chaque arme, son nom et son portrait (gardés : la fenêtre se dessine à chaque image).
    [System.NonSerialized] private GUIContent[] weaponContents;

    private GUIContent WeaponContent(int slot)
    {
        if (weaponContents == null || weaponContents.Length != weapon.Loadout.Count) weaponContents = new GUIContent[weapon.Loadout.Count];
        return weaponContents[slot] ??= new GUIContent();
    }

    private static List<int>[] BuildFamilySlots(IReadOnlyList<WeaponData> loadout)
    {
        var slots = new List<int>[Arsenal.FamilyCount];
        for (int family = 0; family < slots.Length; family++) slots[family] = Arsenal.SlotsOf(loadout, (WeaponFamily)family);
        return slots;
    }
    private Rect window = new Rect(0f, 0f, 980f, 830f);
    [System.NonSerialized] private GUIStyle headerStyle;
    [System.NonSerialized] private GUIStyle smallStyle;
    [System.NonSerialized] private GUIStyle statStyle;
    [System.NonSerialized] private GUIStyle windowStyle;
    [System.NonSerialized] private GUIStyle optionStyle;
    [System.NonSerialized] private Texture2D windowTexture;
    [System.NonSerialized] private Texture2D chosenTexture;

    private void OnEnable()
    {
        instance = this;
        IsOpen = false;
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
        IsOpen = false;
        if (input != null) input.SetMode(PlayerInputReader.Mode.Gameplay);
    }

    private void OnDestroy()
    {
        if (windowTexture != null) Destroy(windowTexture);
        if (chosenTexture != null) Destroy(chosenTexture);
    }

    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private bool FindPlayer()
    {
        if (weapon != null && input != null) return true;
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject player = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        if (player == null) return false;
        input = player.GetComponent<PlayerInputReader>();
        weapon = player.GetComponentInChildren<WeaponController>();
        looksSource = player.GetComponent<PlayerWeaponLooks>();
        attachmentsSource = player.GetComponent<PlayerWeaponAttachments>();
        return weapon != null && input != null;
    }

    private void Update()
    {
        if (!FindPlayer()) return;
        // B ouvre et ferme la fenêtre ; Échap est lu par le menu de pause, qui la referme d'abord.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.bKey.wasPressedThisFrame && !PauseMenu.IsOpen) SetOpen(!IsOpen);

        // Tenu à chaque image : rien d'autre dans le stand ne règle la souris ni les commandes.
        bool free = IsOpen || PauseMenu.IsOpen;
        PlayerInputReader.Mode mode = free ? PlayerInputReader.Mode.None : PlayerInputReader.Mode.Gameplay;
        if (input.CurrentMode != mode) input.SetMode(mode);
        UnityEngine.Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = free;
    }

    private void BuildStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        statStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };

        // La fenêtre du thème par défaut laisse voir le stand à travers : un fond opaque.
        windowTexture = Solid(new Color(0.09f, 0.1f, 0.12f, 0.97f));
        windowStyle = new GUIStyle(GUI.skin.window);
        windowStyle.normal.background = windowStyle.onNormal.background = windowTexture;
        windowStyle.normal.textColor = windowStyle.onNormal.textColor = Color.white;

        // Un choix actif se voit (le bouton enfoncé du thème par défaut ne se distinguait pas).
        chosenTexture = Solid(new Color(0.95f, 0.6f, 0.15f, 1f));
        optionStyle = new GUIStyle(GUI.skin.button);
        optionStyle.onNormal.background = optionStyle.onHover.background = optionStyle.onActive.background = chosenTexture;
        optionStyle.onNormal.textColor = optionStyle.onHover.textColor = optionStyle.onActive.textColor = new Color(0.08f, 0.08f, 0.08f);
    }

    private void OnGUI()
    {
        if (!IsOpen || weapon == null) return;
        BuildStyles();
        window.x = (Screen.width - window.width) * 0.5f;
        window.y = (Screen.height - window.height) * 0.5f;
        GUI.depth = -10;   // par-dessus le reste de l'interface OnGUI
        GUILayout.Window(4217, window, DrawWindow, "STAND DE TIR — ARMES ET ACCESSOIRES   (B ou Échap pour fermer)", windowStyle);
    }

    private void DrawWindow(int id)
    {
        IReadOnlyList<WeaponData> loadout = weapon.Loadout;
        if (shownSlot < 0 || shownSlot >= loadout.Count) shownSlot = weapon.LocalSlot;
        int slot = shownSlot;
        WeaponData data = weapon.DataOf(slot);
        bool server = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        WeaponLooks looks = looksSource != null ? looksSource.Looks : WeaponLooks.Default;
        WeaponAttachments attachments = weapon.Attachments;
        AttachmentCatalog catalog = data != null ? data.attachmentCatalog : null;

        // Les douze armes, une ligne par famille (2026-10-26).
        GUILayout.Space(4f);
        familySlots ??= BuildFamilySlots(loadout);
        for (int family = 0; family < familySlots.Length; family++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Arsenal.FamilyLabel((WeaponFamily)family), smallStyle, GUILayout.Width(150f));
            foreach (int i in familySlots[family])
            {
                // Le nom, et l'arme de profil à côté (WeaponPortraits : sans accessoire, finition d'origine).
                GUIContent content = WeaponContent(i);
                content.text = loadout[i].weaponName + (weapon.LocalSlot == i ? "  (en main)" : "");
                content.image = WeaponPortraits.Of(loadout[i]);
                if (GUILayout.Toggle(i == slot, content, optionStyle, GUILayout.Width(250f), GUILayout.Height(44f)) && i != slot)
                {
                    shownSlot = i;
                    weapon.RequestWeapon(i);
                }
            }
            GUILayout.EndHorizontal();
        }
        if (data == null) return;

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();

        // --- À gauche : ce qu'on change ---
        GUILayout.BeginVertical(GUILayout.Width(560f));
        if (!server) GUILayout.Label("Seul l'hôte change ses armes au stand.", smallStyle);
        GUI.enabled = server;

        if (!string.IsNullOrEmpty(data.role)) GUILayout.Label(data.role, smallStyle);

        GUILayout.Label("FINITION", headerStyle);
        GUILayout.BeginHorizontal();
        for (int f = 0; f < WeaponLooks.FinishCount; f++)
        {
            var finish = (WeaponFinish)f;
            if (GUILayout.Toggle(looks.Finish(slot) == finish, WeaponLooks.FinishLabel(finish), optionStyle) && looks.Finish(slot) != finish)
                looksSource.ServerSet(looks.WithFinish(slot, finish));
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("VISEUR", headerStyle);
        OptionGrid(data, slot, attachments, AttachmentCategory.Sight, WeaponAttachments.SightCount + 1, 4,
                   v => v == 0 ? "Mire de l'arme" : catalog != null ? catalog.SightTitle(v) : $"Viseur {v}", server);
        GUILayout.Label("POIGNÉE", headerStyle);
        OptionGrid(data, slot, attachments, AttachmentCategory.Grip, GripLabels.Length, 3, v => GripLabels[v], server);
        GUILayout.Label("LASER TACTIQUE", headerStyle);
        OptionGrid(data, slot, attachments, AttachmentCategory.Laser, 2, 2, v => v == 0 ? "Sans" : "Avec", server);
        GUILayout.Label("CHARGEUR", headerStyle);
        OptionGrid(data, slot, attachments, AttachmentCategory.Magazine, 2, 2, v => v == 0 ? "D'origine" : "Grande capacité", server);
        GUI.enabled = true;
        GUILayout.EndVertical();

        // --- À droite : la fiche, et ce que fait le stand ---
        GUILayout.Space(16f);
        GUILayout.BeginVertical();
        GUILayout.Label($"FICHE — {data.weaponName.ToUpperInvariant()}", headerStyle);
        foreach (WeaponStatSheet.Stat stat in WeaponStatSheet.Bars)
        {
            float value = WeaponStatSheet.Value(stat, data, attachments, slot);
            GUILayout.Label($"{WeaponStatSheet.Label(stat)} : {WeaponStatSheet.Format(stat, data, value)}", statStyle);
        }
        GUILayout.Label($"Portée : {WeaponStatSheet.EffectiveRange(data)}", statStyle);
        GUILayout.Label($"Rechargement : {WeaponStatSheet.Reload(data, attachments, slot)}", statStyle);
        GUILayout.Label($"Tir : {WeaponStatSheet.FireMode(data)}", statStyle);
        GUILayout.Label($"Zones : {WeaponStatSheet.Zones(data)}", statStyle);
        GUILayout.Label($"Cône à la hanche : {WeaponStatSheet.Number(data.hipSpread)}° à l'arrêt, {WeaponStatSheet.Number(data.hipSpreadMoving)}° en marchant", statStyle);
        GUILayout.Space(10f);
        GUILayout.Label("Les mannequins ont la vie d'un robot (100 PV) et se relèvent après " +
                        $"{(ShootingRange.Instance != null && ShootingRange.Instance.Dummies.Count > 0 ? ShootingRange.Instance.Dummies[0].RegenDelay : 3f):0} s sans être touchés. " +
                        "Chaque touche affiche ses dégâts et sa zone ; un mannequin neutralisé, le temps et le nombre d'impacts.", smallStyle);
        GUILayout.FlexibleSpace();

        // La Fosse (ShootingCourse) : ses cibles tombent-elles au premier impact, et la remettre à zéro.
        ShootingCourse course = ShootingCourse.Instance;
        if (course != null)
        {
            GUILayout.Label("LA FOSSE", headerStyle);
            GUI.enabled = server;
            bool oneHit = GUILayout.Toggle(course.OneHit, "  Une touche suffit (les cibles en carton de la Fosse)");
            if (oneHit != course.OneHit) course.ServerSetOneHit(oneHit);
            if (GUILayout.Button("Recommencer la Fosse", GUILayout.Height(28f))) course.ServerReset();
            GUI.enabled = true;
            GUILayout.Space(8f);
        }

        GUI.enabled = server;
        if (GUILayout.Button("Relever les mannequins", GUILayout.Height(28f)) && ShootingRange.Instance != null)
            ShootingRange.Instance.ServerRestoreDummies();
        GUI.enabled = true;
        if (GUILayout.Button("Garder ces armes pour mes parties", GUILayout.Height(28f)))
        {
            GameSettings.WeaponLooks = looks;
            GameSettings.WeaponAttachments = attachments;
        }
        if (GUILayout.Button("Fermer", GUILayout.Height(28f))) IsOpen = false;
        if (GUILayout.Button("Quitter le stand", GUILayout.Height(28f)))
        {
            IsOpen = false;
            SessionFlow.ReturnToMenu("");
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    /// <summary>Les choix d'une rubrique d'accessoires, `columns` par ligne ; ce que l'arme ne prend pas
    /// est grisé (WeaponAttachments.Accepts, la règle du serveur).</summary>
    private void OptionGrid(WeaponData data, int slot, WeaponAttachments attachments, AttachmentCategory category,
                            int count, int columns, System.Func<int, string> label, bool server)
    {
        int current = attachments.Variant(slot, category);
        for (int start = 0; start < count; start += columns)
        {
            GUILayout.BeginHorizontal();
            for (int v = start; v < Mathf.Min(count, start + columns); v++)
            {
                bool accepted = WeaponAttachments.Accepts(data, category, v);
                GUI.enabled = server && accepted;
                if (GUILayout.Toggle(current == v, label(v), optionStyle, GUILayout.Width(132f)) && current != v && accepted)
                    attachmentsSource.ServerSet(attachments.WithVariant(slot, category, v));
            }
            GUI.enabled = server;
            GUILayout.EndHorizontal();
        }
    }
}
