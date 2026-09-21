using UnityEngine;

/// <summary>
/// Table de correspondance PlayerSoundEvent -> clips/volume/pitch. Data-driven, comme
/// WeaponData : ajouter un son = ajouter une entrée dans cet asset, jamais toucher au code.
/// </summary>
[CreateAssetMenu(fileName = "NewPlayerSoundBank", menuName = "Duel Arena/Player Sound Bank")]
public class PlayerSoundBank : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public PlayerSoundEvent type;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.7f;
        [Tooltip("Variation aléatoire de pitch par occurrence, pour éviter l'effet 'disque rayé'.")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
    }

    public Entry[] entries;

    public bool TryGetEntry(PlayerSoundEvent type, out Entry entry)
    {
        if (entries != null)
        {
            foreach (var e in entries)
            {
                if (e.type == type)
                {
                    entry = e;
                    return true;
                }
            }
        }

        entry = null;
        return false;
    }
}
