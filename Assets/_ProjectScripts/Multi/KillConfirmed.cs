using Unity.Netcode;
using UnityEngine;

/// <summary>
/// L'Élimination confirmée (GDD § 11, 2026-11-02, d'après le mode du même nom de Call of Duty) : un mode à part, avec
/// réapparitions. Un robot détruit lâche son module mémoire là où il est tombé ; l'élimination ne compte que quand
/// son vainqueur va le ramasser, et la victime, revenue, peut le reprendre pour l'annuler. Le premier à
/// `MatchRules.killsToWin` éliminations confirmées gagne (10 par défaut). Le module est un objet du mode, sans plus :
/// il n'entre pas dans le lore (I11).
///
/// Le serveur tient les modules (RoundManager : une liste publiée), juge qui les prend et compte ; chaque machine les
/// montre (MemoryModule, catégorie C). Fonctions pures, testées.
/// </summary>
public static class KillConfirmed
{
    /// <summary>Le module flotte à cette hauteur (m) au-dessus du sol où le robot est tombé : à mi-cuisse, qu'on le
    /// voie par-dessus les barricades basses (proposition de Claude, à régler en jouant).</summary>
    public const float FloatHeight = 0.8f;

    /// <summary>On prend un module en passant à moins de ce rayon (m), mesuré à plat depuis ses pieds.</summary>
    public const float PickupRadius = 1.2f;

    /// <summary>Le module doit être à hauteur du robot : de ce qu'il descend sous ses pieds (un robot debout sur une
    /// caisse, à côté) à ce qu'il monte au-dessus (un robot au sol, sous un module tombé du toit d'une caisse).</summary>
    public const float PickupMinRise = -0.5f, PickupMaxRise = 2f;

    /// <summary>Le sol se cherche jusqu'à cette profondeur (m) sous le robot tombé : un robot tué en plein
    /// franchissement lâche son module au sol, sous lui.</summary>
    public const float DropSearchDepth = 6f;

    /// <summary>Au plus tant de modules à la fois : au-delà, le plus ancien disparaît (une élimination qui ne
    /// comptera plus, ni pour l'un ni pour l'autre).</summary>
    public const int MaxModules = 16;

    /// <summary>Un mort ne réapparaît pas à moins de tant de mètres (à plat) de ses propres modules : le point le plus
    /// éloigné de l'adversaire est souvent celui où il est tombé, et il reprenait son module à l'instant de son retour
    /// (vu à deux instances le 2026-11-02). Le reprendre reste possible, en y retournant.</summary>
    public const float RespawnClearance = 10f;

    /// <summary>Le module d'un robot détruit, publié par le serveur (RoundManager).</summary>
    public struct Module : INetworkSerializable, System.IEquatable<Module>
    {
        /// <summary>Son numéro, unique dans la partie (de quoi le suivre d'une image à l'autre).</summary>
        public ushort id;
        /// <summary>Où il flotte (son centre, au repos).</summary>
        public Vector3 position;
        /// <summary>Le robot détruit, qui peut le reprendre.</summary>
        public ulong victimId;
        /// <summary>Son vainqueur, qui doit le ramasser pour que l'élimination compte.</summary>
        public ulong killerId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref victimId);
            serializer.SerializeValue(ref killerId);
        }

        public bool Equals(Module other) => id == other.id && position == other.position
                                            && victimId == other.victimId && killerId == other.killerId;
    }

    /// <summary>Qui prend un module.</summary>
    public enum Taker : byte
    {
        None,
        /// <summary>Son vainqueur : l'élimination compte.</summary>
        Killer,
        /// <summary>Sa victime, revenue : l'élimination est annulée.</summary>
        Victim,
    }

    /// <summary>Un robot dont les pieds sont en `feet` atteint-il le module en `module` ?</summary>
    public static bool Reaches(Vector3 module, Vector3 feet)
    {
        float rise = module.y - feet.y;
        if (rise < PickupMinRise || rise > PickupMaxRise) return false;
        return FlatDistanceSq(module, feet) <= PickupRadius * PickupRadius;
    }

    /// <summary>Qui prend le module à cette image : son vainqueur, sa victime, ou personne. Seul un robot en vie le
    /// prend ; si les deux l'atteignent à la même image, le plus proche (le vainqueur, à égalité).</summary>
    public static Taker Resolve(Vector3 module, bool killerAlive, Vector3 killerFeet, bool victimAlive, Vector3 victimFeet)
    {
        bool killer = killerAlive && Reaches(module, killerFeet);
        bool victim = victimAlive && Reaches(module, victimFeet);
        if (killer && victim) return FlatDistanceSq(module, victimFeet) < FlatDistanceSq(module, killerFeet) ? Taker.Victim : Taker.Killer;
        if (killer) return Taker.Killer;
        return victim ? Taker.Victim : Taker.None;
    }

    /// <summary>Où flotte le module d'un robot tombé les pieds en `feet` : au-dessus du sol trouvé sous lui
    /// (`floorY`), ou de ses pieds faute de sol.</summary>
    public static Vector3 DropPoint(Vector3 feet, float? floorY) => new Vector3(feet.x, (floorY ?? feet.y) + FloatHeight, feet.z);

    /// <summary>Où flotte le module d'un robot tombé les pieds en `feet`, le sol cherché sous lui dans le décor
    /// (`worldMask`) : un robot tué en l'air lâche son module au sol.</summary>
    public static Vector3 FindDropPoint(Vector3 feet, int worldMask)
    {
        Vector3 from = feet + Vector3.up * 0.5f;
        float? floor = Physics.Raycast(from, Vector3.down, out RaycastHit hit, DropSearchDepth + 0.5f, worldMask, QueryTriggerInteraction.Ignore)
            ? hit.point.y : (float?)null;
        return DropPoint(feet, floor);
    }

    private static float FlatDistanceSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
