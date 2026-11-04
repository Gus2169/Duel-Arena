using UnityEngine;

/// <summary>
/// Comment tombe une épave (2026-11-04, retour de l'utilisateur : « souvent la même chute en arrière, les bras en croix », et son
/// idée : des impulsions au hasard dans le sens des tirs, selon la distance et le type d'arme). Une poussée d'un bloc, toujours
/// la même, renversait le robot comme une planche : à coup égal, la même chute au centimètre près.
///
/// Le coup choisit ici une façon de tomber, au hasard mais selon ce qui l'a donné : la force de l'arme (RobotCorpse.SeverityOf),
/// sa distance rapportée à la portée de l'arme, la pièce et le côté touchés. Fonctions pures, testées. Catégorie C : le serveur
/// tire la graine et la diffuse avec le coup (PlayerDeath), et chaque machine en tire le même plan ; la physique fait le reste.
/// </summary>
public static class CorpseFall
{
    /// <summary>Les façons de tomber. Le tas : les jambes lâchent, il s'affaisse. Le plongeon : les genoux cèdent et le haut du
    /// corps part devant lui. La vrille : il tourne sur lui-même et part sur le côté. Renversé : il part dans le sens du coup.</summary>
    public enum Style { Heap, Dive, Spin, Thrown }

    /// <summary>Le coup qui a tué.</summary>
    public struct Blow
    {
        /// <summary>L'impulsion du coup (N·s) : sa direction est celle du tir.</summary>
        public Vector3 Impulse;
        /// <summary>La masse de l'épave (kg).</summary>
        public float Mass;
        /// <summary>La pièce touchée.</summary>
        public RobotCorpse.RagdollPart Hit;
        /// <summary>Le côté touché, vu du robot : de -1 (sa gauche) à 1 (sa droite).</summary>
        public float Side;
        /// <summary>Où regardait le robot.</summary>
        public Vector3 Forward;
        /// <summary>La proximité du tireur : 1 à bout portant, 0 au bout de la portée de l'arme.</summary>
        public float Closeness;
        /// <summary>La graine, tirée par le serveur.</summary>
        public int Seed;
    }

    /// <summary>Ce que le coup fait à l'épave.</summary>
    public struct Plan
    {
        public Style Style;
        /// <summary>La poussée (N·s), reçue comme un coup, au point d'impact.</summary>
        public Vector3 Push;
        /// <summary>La rotation de tout le corps (rad/s), autour de son centre de masse.</summary>
        public Vector3 Spin;
        /// <summary>Ce qui tombe tout droit dans les jambes (m/s, vers le bas) : le bassin, le buste, la tête et les bras.</summary>
        public float Slump;
        /// <summary>Les genoux qui lâchent (m/s, vers l'avant du robot).</summary>
        public float LeftKnee, RightKnee;
        /// <summary>Les bras, qui partent chacun de leur côté (rad/s).</summary>
        public Vector3 LeftArm, RightArm;
    }

    /// <summary>L'écart au sens du tir d'un robot renversé (degrés, de part et d'autre) : un coup léger choisit moins où il
    /// tombe qu'un coup lourd.</summary>
    public const float LightSwerve = 28f, HeavySwerve = 14f;

    /// <summary>Le plan d'une chute. Fonction pure, testée.</summary>
    public static Plan For(in Blow blow)
    {
        var dice = new Dice(blow.Seed);
        float magnitude = blow.Impulse.magnitude;
        float mass = Mathf.Max(1f, blow.Mass);
        float closeness = Mathf.Clamp01(blow.Closeness);
        Vector3 forward = new Vector3(blow.Forward.x, 0f, blow.Forward.z);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 flat = new Vector3(blow.Impulse.x, 0f, blow.Impulse.z);
        Vector3 shot = flat.sqrMagnitude > 1e-6f ? flat.normalized : -forward;

        RobotCorpse.BurstSeverity severity = RobotCorpse.SeverityOf(magnitude);
        bool brutal = severity == RobotCorpse.BurstSeverity.Brutal;
        Style style = Pick(severity, blow.Hit, closeness, dice.Next());

        // Ce qui tient au coup : la vitesse qu'il donne au corps, plus forte de près. Une balle de fusil d'assaut ne bouge pas
        // un robot de 60 kg : il tombe parce qu'il ne tient plus, le coup ne fait que choisir où. Le pompe de loin projette
        // déjà moins (WeaponController.KillImpulse).
        float speed = magnitude / mass;
        float reach = brutal ? Mathf.Lerp(0.7f, 1f, closeness) : Mathf.Lerp(0.7f, 1.3f, closeness);

        // La vrille va dans le sens du couple du coup, au hasard s'il tombe au milieu.
        float side = Mathf.Abs(blow.Side) > 0.2f ? Mathf.Sign(blow.Side) : (dice.Next() < 0.5f ? -1f : 1f);
        float turn = Vector3.Dot(Vector3.Cross(right * side, shot), Vector3.up);
        turn = Mathf.Abs(turn) > 0.1f ? Mathf.Sign(turn) : side;

        var plan = new Plan { Style = style };
        Vector3 velocity;
        switch (style)
        {
            case Style.Heap:
                velocity = shot * dice.Between(0.1f, 0.5f) + right * dice.Between(-0.3f, 0.3f);
                plan.Spin = Vector3.up * dice.Between(-1.5f, 1.5f);
                plan.Slump = dice.Between(0.8f, 1.4f);
                plan.LeftKnee = dice.Between(1.5f, 3f);
                plan.RightKnee = dice.Between(1.5f, 3f);
                break;
            case Style.Dive:
                velocity = forward * dice.Between(0.2f, 0.6f);
                plan.Spin = right * dice.Between(1.5f, 2.5f) + Vector3.up * dice.Between(-1f, 1f);
                plan.LeftKnee = dice.Between(2f, 3.5f);
                plan.RightKnee = dice.Between(2f, 3.5f);
                break;
            case Style.Spin:
                velocity = shot * Mathf.Max(dice.Between(0.4f, 0.9f), speed * reach) + right * side * dice.Between(0.6f, 1.4f);
                plan.Spin = Vector3.up * turn * dice.Between(5f, 8f) + forward * -side * dice.Between(0.5f, 1.5f);
                if (side > 0f) plan.RightKnee = dice.Between(1.5f, 3f);
                else plan.LeftKnee = dice.Between(1.5f, 3f);
                break;
            default:
                float swerve = severity == RobotCorpse.BurstSeverity.Light ? LightSwerve : HeavySwerve;
                velocity = Quaternion.AngleAxis(dice.Between(-swerve, swerve), Vector3.up) * shot;
                velocity *= brutal ? speed * reach : Mathf.Max(dice.Between(0.9f, 1.3f), speed * 1.8f) * reach;
                plan.Spin = Vector3.up * dice.Between(-2f, 2f) + forward * dice.Between(-1f, 1f);
                plan.LeftKnee = dice.Between(0f, 1.5f);
                plan.RightKnee = dice.Between(0f, 1.5f);
                break;
        }
        // Le coup qui soulève (le pompe, le gant) soulève encore.
        plan.Push = (velocity + Vector3.up * (Mathf.Max(0f, blow.Impulse.y) / mass * reach)) * mass;
        plan.LeftArm = dice.Direction() * dice.Between(2f, 6f);
        plan.RightArm = dice.Direction() * dice.Between(2f, 6f);
        return plan;
    }

    /// <summary>
    /// La façon de tomber : un tirage (`roll`, de 0 à 1) dans les parts que donnent la force du coup, la pièce touchée et la
    /// distance. Fonction pure, testée.
    /// </summary>
    public static Style Pick(RobotCorpse.BurstSeverity severity, RobotCorpse.RagdollPart hit, float closeness, float roll)
    {
        Weights(severity, hit, closeness, out float heap, out float dive, out float spin, out float thrown);
        float pick = Mathf.Clamp01(roll) * (heap + dive + spin + thrown);
        if ((pick -= heap) < 0f) return Style.Heap;
        if ((pick -= dive) < 0f) return Style.Dive;
        return pick - spin < 0f ? Style.Spin : Style.Thrown;
    }

    /// <summary>Les parts de chaque façon de tomber. Un coup brutal renverse ; une balle à la tête fait s'effondrer, une balle
    /// dans les jambes les fait céder ; de près on est plus souvent renversé, de loin on s'effondre. Fonction pure, testée.</summary>
    public static void Weights(RobotCorpse.BurstSeverity severity, RobotCorpse.RagdollPart hit, float closeness,
                               out float heap, out float dive, out float spin, out float thrown)
    {
        bool head = hit == RobotCorpse.RagdollPart.Head;
        bool legs = hit == RobotCorpse.RagdollPart.LeftThigh || hit == RobotCorpse.RagdollPart.RightThigh
                 || hit == RobotCorpse.RagdollPart.LeftCalf || hit == RobotCorpse.RagdollPart.RightCalf;
        switch (severity)
        {
            case RobotCorpse.BurstSeverity.Brutal:
                heap = 0f; dive = 0f; spin = 25f; thrown = 75f;
                break;
            case RobotCorpse.BurstSeverity.Heavy:
                if (head) { heap = 20f; dive = 5f; spin = 25f; thrown = 50f; }
                else if (legs) { heap = 5f; dive = 40f; spin = 40f; thrown = 15f; }
                else { heap = 5f; dive = 5f; spin = 30f; thrown = 60f; }
                break;
            default:
                if (head) { heap = 45f; dive = 25f; spin = 15f; thrown = 15f; }
                else if (legs) { heap = 15f; dive = 50f; spin = 25f; thrown = 10f; }
                else { heap = 25f; dive = 15f; spin = 30f; thrown = 30f; }
                break;
        }
        float near = Mathf.Clamp01(closeness);
        heap *= 1.4f - 0.8f * near;
        thrown *= 0.6f + 0.8f * near;
    }

    // Un petit tirage à graine, le même sur toutes les machines. Haché : deux graines voisines partiraient de deux suites voisines.
    private sealed class Dice
    {
        private uint state;

        public Dice(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= h >> 15;
                h *= 0x85EBCA77u;
                h ^= h >> 13;
                h *= 0xC2B2AE3Du;
                h ^= h >> 16;
                state = h != 0u ? h : 0x6D2B79F5u;
            }
        }

        /// <summary>De 0 (compris) à 1 (non compris).</summary>
        public float Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) / 16777216f;
        }

        public float Between(float low, float high) => low + Next() * (high - low);

        public Vector3 Direction()
        {
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3(Next() * 2f - 1f, Next() * 2f - 1f, Next() * 2f - 1f);
                float length = v.magnitude;
                if (length > 0.05f && length <= 1f) return v / length;
            }
            return Vector3.up;
        }
    }
}
