using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les matériaux de cel-shading des robots, en face de leurs matériaux du jeu (2026-10-27, le réglage du rendu,
/// RenderStyle) : pour chaque châssis (CT-01, OS-02, le prototype), ceux de l'essai des rendus (RenderTrialBuilder,
/// DuelArena/RobotCelShading). La visière garde le sien (l'écran de la mort, DuelArena/EcranVisage). Construit par
/// Duel Arena > Rendu > Préparer le réglage du rendu ; rangé dans un dossier Resources, pour être chargé partout (le
/// menu, l'arène, le stand) et emporté dans la build avec les matériaux qu'il désigne.
/// </summary>
public sealed class RobotStyleMaterials : ScriptableObject
{
    public const string ResourceName = "RobotStyleMaterials";

    [Serializable]
    public struct Pair
    {
        public Material game;
        public Material cel;
    }

    [SerializeField] private Pair[] pairs = Array.Empty<Pair>();

    [NonSerialized] private Dictionary<Material, Material> toCel;
    [NonSerialized] private Dictionary<Material, Material> toGame;

    public IReadOnlyList<Pair> Pairs => pairs;

    public void SetPairs(Pair[] value)
    {
        pairs = value ?? Array.Empty<Pair>();
        toCel = null;
        toGame = null;
    }

    /// <summary>Le matériau à montrer à la place de `m` dans ce rendu : son double, ou lui-même s'il n'en a pas.</summary>
    public Material For(Material m, bool cel)
    {
        if (m == null) return null;
        if (toCel == null) Build();
        return (cel ? toCel : toGame).TryGetValue(m, out Material other) ? other : m;
    }

    /// <summary>Habille toutes les pièces sous `root` pour ce rendu. La peinture du joueur (un MaterialPropertyBlock
    /// par pièce, PlayerPaint.PaintPart) reste : elle ne dépend pas du matériau.</summary>
    public void Apply(GameObject root, bool cel)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Material swap = For(slots[i], cel);
                if (swap == slots[i]) continue;
                slots[i] = swap;
                changed = true;
            }
            if (changed) r.sharedMaterials = slots;
        }
    }

    private void Build()
    {
        toCel = new Dictionary<Material, Material>();
        toGame = new Dictionary<Material, Material>();
        foreach (Pair p in pairs)
        {
            if (p.game == null || p.cel == null) continue;
            toCel[p.game] = p.cel;
            toGame[p.cel] = p.game;
        }
    }
}
