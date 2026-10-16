using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// La fenêtre du stand de tir (2026-10-16, demandée par l'utilisateur) : B ou Échap l'ouvre et la
/// ferme. On y prend une arme, on change son modèle, sa finition et ses accessoires, ça s'applique tout
/// de suite (le serveur borne et publie, comme à l'apparition : PlayerWeaponLooks.ServerSet,
/// PlayerWeaponAttachments.ServerSet, qui remplit aussi les chargeurs). La fiche de l'arme se lit à côté
/// (WeaponStatSheet). On peut garder la configuration pour ses parties (GameSettings), relever les
/// mannequins, ou revenir au menu.
///
/// Ouverte, elle libère la souris et coupe les commandes de jeu (PlayerInputReader.Mode.None) ;
/// fermée, elle verrouille la souris, comme le HUD de la partie (GameHud). OnGUI, outil de test.
/// </summary>
[DisallowMultipleComponent]
public class ShootingRangePanel : MonoBehaviour
{
    /// <summary>La fenêtre est-elle ouverte ? (le réticule se tait : CrosshairUI)</summary>
    public static bool IsOpen { get; private set; }

    private static readonly string[] GripLabels = { "Aucune", "Verticale", "Inclinée" };

    private PlayerInputReader input;
    private WeaponController weapon;
    private PlayerWeaponLooks looksSource;
    private PlayerWeaponAttachments attachmentsSource;
    private int shownSlot = -1;
    private Rect window = new Rect(0f, 0f, 980f, 640f);
    [System.NonSerialized] private GUIStyle headerStyle;
    [System.NonSerialized] private GUIStyle smallStyle;
    [System.NonSerialized] private GUIStyle statStyle;
    [System.NonSerialized] private GUIStyle windowStyle;
    [System.NonSerialized] private GUIStyle optionStyle;
    [System.NonSerialized] private Texture2D windowTexture;
    [System.NonSerialized] private Texture2D chosenTexture;

    private void OnEnable() => IsOpen = false;

    private void OnDisable()
    {
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
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.bKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
        {
            IsOpen = !IsOpen;
            if (IsOpen) shownSlot = weapon.LocalSlot;
        }

        // Tenu à chaque image : rien d'autre dans le stand ne règle la souris ni les commandes.
        PlayerInputReader.Mode mode = IsOpen ? PlayerInputReader.Mode.None : PlayerInputReader.Mode.Gameplay;
        if (input.CurrentMode != mode) input.SetMode(mode);
        UnityEngine.Cursor.lockState = IsOpen ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = IsOpen;
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

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < loadout.Count; i++)
        {
            if (loadout[i] == null) continue;
            string label = $"{i + 1}  {loadout[i].weaponName}" + (weapon.LocalSlot == i ? "  (en main)" : "");
            if (GUILayout.Toggle(i == slot, label, optionStyle, GUILayout.Height(30f)) && i != slot)
            {
                shownSlot = i;
                weapon.RequestWeapon(i);
            }
        }
        GUILayout.EndHorizontal();
        if (data == null) return;

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();

        // --- À gauche : ce qu'on change ---
        GUILayout.BeginVertical(GUILayout.Width(560f));
        if (!server) GUILayout.Label("Seul l'hôte change ses armes au stand.", smallStyle);
        GUI.enabled = server;

        GUILayout.Label("MODÈLE", headerStyle);
        GUILayout.BeginHorizontal();
        for (int m = 0; m < data.ModelCount; m++)
        {
            GameObject prefab = data.ModelPrefabFor(m);
            WeaponModel model = prefab != null ? prefab.GetComponent<WeaponModel>() : null;
            string name = model != null ? model.Designation : $"Modèle {m + 1}";
            if (GUILayout.Toggle(looks.Model(slot) == m, name, optionStyle) && looks.Model(slot) != m)
                looksSource.ServerSet(looks.WithModel(slot, m));
        }
        GUILayout.EndHorizontal();

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
