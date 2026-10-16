using UnityEngine;

/// <summary>
/// Le tir à la hanche façon Rainbow Six (2026-10-16, demandé par l'utilisateur : il était trop facile
/// de toucher au jugé ; GDD § 3 amendé pour la hanche). Épaulée, la balle part exactement dans l'axe.
/// À la hanche, elle part au HASARD dans un cône :
///   - qui s'ouvre en se déplaçant (de `hipSpread` à l'arrêt à `hipSpreadMoving` à l'allure de marche,
///     et jusqu'au double à l'allure d'un sprint, quand l'élan n'est pas retombé) ;
///   - qui s'ouvre au fil d'une rafale (`hipSpreadPerShot` par balle, plafonné, et qui se referme à
///     `hipSpreadRecovery` degrés par seconde passé `BloomRecoveryDelay` sans tirer) ;
///   - plus serré accroupi et allongé ;
///   - qui se referme pendant qu'on épaule, selon la courbe de la classe d'arme (Rainbow Six, Y9S1 : la
///     précision arrive tôt aux pistolets, à mi-course aux fusils de précision, à la fin aux fusils
///     d'assaut) ;
///   - et que le laser tactique resserre (Rainbow Six jusqu'en Y9S1).
/// Une arme à plombs n'a pas de cône : sa gerbe à motif fixe (GDD § 3) est sa dispersion à la hanche.
///
/// Le hasard est le MÊME chez le tireur et sur le serveur : un hachage du numéro de la balle
/// (MagazineState.shotsFired, compté par le serveur et prédit par le tireur) et du joueur. Le traçant
/// du tireur montre donc où le serveur fait arriver la balle. Le client n'envoie que l'axe de son
/// regard : le serveur calcule le cône sur son propre état de l'input de référence (vitesse, posture,
/// temps d'épaulement, rafale), jamais sur ce que le client prétend.
///
/// Fonctions pures, testées (HipFireSpreadTests).
/// </summary>
public static class HipFireSpread
{
    /// <summary>Comment la précision arrive pendant qu'on épaule (Rainbow Six, Y9S1). Enregistré par sa
    /// valeur : ajouter les nouvelles à la fin.</summary>
    public enum AimCurve : byte
    {
        /// <summary>La précision arrive à la fin (fusils d'assaut, mitraillettes, LMG).</summary>
        Slow,
        /// <summary>En ligne droite (fusils de précision).</summary>
        Linear,
        /// <summary>La précision arrive tôt (pistolets, fusils à pompe).</summary>
        Fast,
    }

    /// <summary>L'allure (m/s) à laquelle le cône atteint `hipSpreadMoving` : la marche.</summary>
    public const float ReferenceSpeed = 5f;

    /// <summary>Au-delà, le cône continue de s'ouvrir, jusqu'à ce multiple de l'allure de marche (l'élan
    /// d'un sprint, 10 m/s).</summary>
    public const float MaxSpeedFactor = 2f;

    /// <summary>Le cône accroupi et allongé, relatif au cône debout.</summary>
    public const float CrouchFactor = 0.8f;
    public const float ProneFactor = 0.65f;

    /// <summary>
    /// Le demi-angle (degrés) du cône : à `speed` m/s, dans la posture `stance`, épaulé à
    /// `aimProgress` (0 à la hanche, 1 épaulé), ouvert de `bloom` degrés par la rafale, et resserré par
    /// `laserFactor` (1 sans laser).
    /// </summary>
    public static float Cone(WeaponData data, float speed, PlayerLocomotion.Stance stance, float aimProgress, float bloom, float laserFactor)
    {
        if (data == null) return 0f;
        if (!float.IsFinite(speed)) speed = 0f;
        if (!float.IsFinite(bloom)) bloom = 0f;
        float movement = Mathf.Clamp(speed / ReferenceSpeed, 0f, MaxSpeedFactor);
        float cone = data.hipSpread + (data.hipSpreadMoving - data.hipSpread) * movement + Mathf.Max(0f, bloom);
        cone = Mathf.Max(0f, cone) * StanceFactor(stance);
        cone *= 1f - Precision(data.aimCurve, aimProgress);
        return cone * (float.IsFinite(laserFactor) ? Mathf.Clamp01(laserFactor) : 1f);
    }

    public static float StanceFactor(PlayerLocomotion.Stance stance) => stance switch
    {
        PlayerLocomotion.Stance.Crouching => CrouchFactor,
        PlayerLocomotion.Stance.Prone => ProneFactor,
        _ => 1f,
    };

    /// <summary>Où en est l'épaulement (0 à 1), après `aimHeldTime` secondes de visée, pour une arme qui
    /// épaule en `aimTime` secondes.</summary>
    public static float AimProgress(float aimHeldTime, float aimTime)
    {
        if (!(aimHeldTime > 0f)) return 0f; // NaN compris
        return aimTime > 0f ? Mathf.Clamp01(aimHeldTime / aimTime) : 1f;
    }

    /// <summary>La part de la précision gagnée à `progress` de l'épaulement (0 à la hanche, 1 épaulé).</summary>
    public static float Precision(AimCurve curve, float progress)
    {
        float p = float.IsFinite(progress) ? Mathf.Clamp01(progress) : 0f;
        return curve switch
        {
            AimCurve.Fast => 1f - (1f - p) * (1f - p),
            AimCurve.Linear => p,
            _ => p * p,
        };
    }

    /// <summary>Le cône de la rafale ne se referme qu'après ce temps sans tirer (s) : entre deux balles
    /// d'une rafale, il ne se referme pas (sans ce délai, à 7 coups par seconde, il se refermait plus
    /// vite que la rafale ne l'ouvrait).</summary>
    public const float BloomRecoveryDelay = 0.2f;

    /// <summary>L'ouverture due à la rafale au moment d'un tir à `clock` (l'horloge des inputs), le
    /// précédent ayant laissé `bloom` degrés à `lastShotClock`.</summary>
    public static float BloomAt(float bloom, double lastShotClock, double clock, float recovery)
    {
        if (!float.IsFinite(bloom) || bloom <= 0f) return 0f;
        double elapsed = clock - lastShotClock - BloomRecoveryDelay;
        if (!(elapsed > 0.0)) return bloom;                // dans la rafale, ou une horloge qui recule
        if (recovery <= 0f) return bloom;
        if (double.IsInfinity(elapsed)) return 0f;
        return Mathf.Max(0f, bloom - recovery * (float)elapsed);
    }

    /// <summary>L'ouverture que laisse un tir parti avec `bloomAt` : une balle de plus, plafonnée.</summary>
    public static float BloomAfterShot(WeaponData data, float bloomAt)
    {
        if (data == null) return 0f;
        return Mathf.Clamp(bloomAt + data.hipSpreadPerShot, 0f, Mathf.Max(0f, data.hipSpreadMaxBloom));
    }

    /// <summary>La graine de la balle numéro `shot` du joueur `shooter` (son identifiant Netcode).</summary>
    public static uint Seed(ulong shooter, int shot)
        => Hash(unchecked((uint)shooter * 0x9E3779B1u ^ (uint)(shooter >> 32)) ^ Hash(unchecked((uint)shot)));

    /// <summary>Un point tiré au hasard dans le disque unité, uniformément, d'après `seed`.</summary>
    public static Vector2 UnitDisk(uint seed)
    {
        uint a = Hash(seed);
        uint b = Hash(a ^ 0x68E31DA4u);
        float u = (a >> 8) * (1f / 16777216f); // [0 ; 1[
        float v = (b >> 8) * (1f / 16777216f);
        float radius = Mathf.Sqrt(u);           // uniforme sur la surface, pas concentré au centre
        float angle = v * 2f * Mathf.PI;
        return new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
    }

    /// <summary>L'écart (degrés, x à droite, y en haut) de la balle de graine `seed` dans un cône de
    /// `cone` degrés.</summary>
    public static Vector2 Offset(float cone, uint seed) => cone > 0f ? UnitDisk(seed) * cone : Vector2.zero;

    /// <summary>Hachage d'un entier (lowbias32, Chris Wellons) : le même résultat sur toutes les
    /// machines, sans état partagé (UnityEngine.Random en a un).</summary>
    public static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
