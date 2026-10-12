using UnityEngine;

/// <summary>
/// Cale les pieds au sol pendant l'appui (2026-10-10). Demandé par l'utilisateur : « je veux que
/// les animations soient parfaites, on n'accepte pas les glissades ».
///
/// La cadence des jambes (PlayerAnimator.DriveGait) fait avancer l'animation à la vitesse du corps,
/// en moyenne sur un pas. Il reste des glissades qu'aucune cadence ne corrige : les clips Mixamo
/// glissent d'eux-mêmes pendant leurs appuis (de 2 % en marche à 25 % en course, mesuré à leur
/// propre vitesse), un mélange de deux clips pose ses pieds entre les deux, et une vitesse qui
/// change arrive au milieu d'un pas.
///
/// Pendant qu'un pied porte, on retient où il s'est posé dans le MONDE et on l'y maintient, par une
/// IK à deux os sur la jambe (cuisse et mollet), appliquée après l'Animator. Le pied garde son
/// orientation animée. Quand il se lève, l'écart retenu se résorbe pendant qu'il est en l'air : rien
/// ne bouge au sol. Le talon tient le pied quand il attaque le sol, la pointe dès qu'elle touche,
/// jusqu'à ce qu'il décolle.
///
/// Un pied « porte » quand il est au plus bas qu'il ait été récemment (à 2 cm près), et qu'il s'y
/// est posé sans balayer le sol : une pointe qui frôle le sol pendant le balancier, en avançant
/// deux fois plus vite que le corps le long de la marche, n'est pas un appui.
///
/// Catégorie C stricte : visuel, local à chaque machine, rien ne le relit. Ni la caméra, ni le tir,
/// ni la surface touchable ne dépendent des os. Désactivé allongé, en l'air, en franchissement et
/// pendant les changements de posture vers ou depuis le sol.
/// </summary>
public class FootPlanter
{
    /// <summary>Marge au-dessus du plus bas récent pour que le talon ou la pointe se pose (m).</summary>
    public const float ContactHeight = 0.02f;

    /// <summary>Marge au-delà de laquelle il se lève (m). L'écart avec ContactHeight évite qu'un
    /// pied à la limite ne se pose et se lève à chaque image.</summary>
    public const float ReleaseHeight = 0.045f;

    /// <summary>Écart maximal (m) entre le pied animé et l'endroit où on le retient. Au-delà, la
    /// jambe se tendrait trop : le pied est relâché.</summary>
    public const float MaxCorrection = 0.25f;

    /// <summary>Rotation maximale (degrés) qu'on impose au pied pour qu'il ne pivote pas au sol.</summary>
    public const float MaxYaw = 45f;

    /// <summary>À l'arrêt, écart (m) ou rotation (degrés) au-delà desquels un pied posé fait un pas
    /// pour rejoindre sa place : le corps a tourné sur place au-dessus de lui.</summary>
    public const float StepDistance = 0.10f;
    public const float StepYaw = 22f;

    /// <summary>Durée (s) et hauteur (m) de ce pas.</summary>
    public const float StepTime = 0.2f;
    public const float StepHeight = 0.06f;

    /// <summary>Sous cette vitesse du corps (m/s), on est à l'arrêt : les pas viennent du calage, pas
    /// de l'animation de marche.</summary>
    public const float StepBodySpeed = 0.3f;

    /// <summary>Temps (s) en lequel l'écart se résorbe une fois le pied levé.</summary>
    public const float ReleaseTime = 0.07f;

    /// <summary>Vitesse (m/s) à laquelle le plus bas récent remonte s'il n'est pas rafraîchi : il
    /// suit ainsi un sol qui change de hauteur sous le personnage.</summary>
    private const float MinimumCreep = 0.05f;

    private struct Foot
    {
        public Transform upper, lower, foot, toes;
        public float heelMin, toeMin;
        public bool heelDown, toeDown;
        public Vector3 heelLock, toeLock;
        public Vector3 correction; // décalage horizontal appliqué à la cheville (monde)
        public float yaw;          // rotation du pied autour de la verticale (degrés)
        public Vector3 lastHeel, lastToe;
        public bool stepping;      // pas sur place en cours (pivot à l'arrêt)
        public float stepT;
        public Vector3 stepFrom, stepTo;
        public float stepYawFrom;
    }

    private readonly Transform root;
    private Foot left, right;
    private bool valid, hasLast, wasChangingPose;
    private Vector3 lastRoot;


    public FootPlanter(Animator animator)
    {
        root = animator.transform;
        left = Make(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
        right = Make(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        valid = left.upper != null && left.lower != null && left.foot != null && left.toes != null
             && right.upper != null && right.lower != null && right.foot != null && right.toes != null;
    }

    private static Foot Make(Animator a, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, HumanBodyBones toes)
    {
        return new Foot
        {
            upper = a.GetBoneTransform(upper), lower = a.GetBoneTransform(lower),
            foot = a.GetBoneTransform(foot), toes = a.GetBoneTransform(toes),
            heelMin = float.MaxValue, toeMin = float.MaxValue,
        };
    }

    /// <summary>Le pied gauche ou droit est-il retenu au sol en ce moment ? (diagnostic, tests)</summary>
    public bool IsPlanted(bool leftFoot) => leftFoot ? (left.heelDown || left.toeDown) : (right.heelDown || right.toeDown);

    /// <summary>Nombre de fois où une jambe n'a pas pu atteindre l'endroit où son pied est retenu
    /// (diagnostic, tests) : jambe déjà tendue, le pied glisse alors quand même.</summary>
    public static int OutOfReach;

    /// <summary>Oublie tout : après une téléportation, ou quand le calage reprend.</summary>
    public void Reset()
    {
        hasLast = false;
        left.stepping = right.stepping = false;
        left.heelDown = left.toeDown = right.heelDown = right.toeDown = false;
        left.correction = right.correction = Vector3.zero;
        left.yaw = right.yaw = 0f;
    }

    /// <summary>À appeler après l'évaluation de l'Animator, avant toute autre retouche des os.</summary>
    /// <param name="active">Faux allongé, en l'air, en franchissement, pendant un passage au sol.</param>
    /// <param name="changingPose">Vrai pendant le fondu debout ↔ accroupi. Les pieds animés y sont
    /// un mélange de deux poses, qui passe la pointe jusqu'à 4 cm sous le sol : on n'en apprend pas
    /// la hauteur du sol, et on la réapprend dans la pose d'arrivée.</param>
    public void Apply(bool active, float dt, bool changingPose = false)
    {
        if (!valid || dt <= 0f) return;

        if (wasChangingPose && !changingPose)
        {
            left.heelMin = left.toeMin = right.heelMin = right.toeMin = float.MaxValue;
        }
        wasChangingPose = changingPose;

        Vector3 rootPos = root.position;
        // Une téléportation (manche, recalage) n'est pas un pas : on repart de zéro.
        if (hasLast && (rootPos - lastRoot).sqrMagnitude > 1f) Reset();
        Vector3 bodyVelocity = hasLast ? Horizontal(rootPos - lastRoot) / dt : Vector3.zero;

        // Un pied peut partir quand l'autre a fait la moitié de son pas : pieds écartés (accroupi),
        // attendre qu'il ait fini laisserait le corps tirer le second trop loin.
        // Le sol, vu des deux pieds : la plus basse des pointes et le plus bas des talons. Un pied
        // peut être sur la pointe, talon levé (le droit, accroupi) : c'est l'autre qui dit où est le sol.
        float groundToe = Mathf.Min(left.toeMin, right.toeMin);
        float groundHeel = Mathf.Min(left.heelMin, right.heelMin);
        Step(ref left, active, changingPose, groundToe, groundHeel, right.stepping && right.stepT < 0.5f, rootPos.y, bodyVelocity, dt);
        Step(ref right, active, changingPose, groundToe, groundHeel, left.stepping && left.stepT < 0.5f, rootPos.y, bodyVelocity, dt);

        lastRoot = rootPos;
        hasLast = true;
    }


    private void Step(ref Foot f, bool active, bool changingPose, float groundToe, float groundHeel, bool otherStepping,
                      float ground, Vector3 bodyVelocity, float dt)
    {
        Vector3 heel = f.foot.position;
        Vector3 toe = f.toes.position;
        float heelHeight = heel.y - ground;
        float toeHeight = toe.y - ground;
        if (!changingPose)
        {
            f.heelMin = Mathf.Min(f.heelMin + MinimumCreep * dt, heelHeight);
            f.toeMin = Mathf.Min(f.toeMin + MinimumCreep * dt, toeHeight);
        }

        // Pendant le fondu debout ↔ accroupi, le mélange des deux poses de jambe fait passer la pointe
        // jusqu'à 5 cm sous le sol : le pied est relevé d'autant, jamais plus bas que le sol.
        float lift = 0f;
        if (active && changingPose && groundToe < 1f && groundHeel < 1f)
            lift = Mathf.Max(0f, Mathf.Max(groundToe - toeHeight, groundHeel - heelHeight));

        // Où on VOIT le talon et la pointe, correction en cours comprise : c'est là qu'ils se posent.
        Quaternion shownYaw = Quaternion.AngleAxis(f.yaw, Vector3.up);
        Vector3 shownHeel = heel + f.correction;
        Vector3 shownToe = shownHeel + shownYaw * (toe - heel);

        if (!active)
        {
            // Coupé (changement de posture, saut, franchissement) : les plus bas retenus ne valent
            // plus rien — accroupi, les pieds ne sont pas à la hauteur de la pose debout. Ils se
            // remesurent à la reprise.
            f.heelDown = f.toeDown = false;
            f.stepping = false;
            f.heelMin = f.toeMin = float.MaxValue;
        }
        else if (!f.stepping)
        {
            // Le pied ANIMÉ, dans le monde, le long de la marche : immobile pour un appui parfait, un
            // peu en avant ou en arrière pour un clip qui glisse, et deux fois plus vite que le corps
            // pour une pointe qui frôle le sol en balancier — ce n'est pas un appui.
            float bodySpeed = bodyVelocity.magnitude;
            Vector3 along = bodySpeed > 0.05f ? bodyVelocity / bodySpeed : Vector3.zero;
            float heelAhead = hasLast ? Vector3.Dot(Horizontal(heel - f.lastHeel) / dt, along) : 0f;
            float toeAhead = hasLast ? Vector3.Dot(Horizontal(toe - f.lastToe) / dt, along) : 0f;
            float sweep = 1.2f * bodySpeed + 0.25f;

            UpdateContact(ref f.heelDown, ref f.heelLock, heelHeight, f.heelMin, heelAhead, sweep, shownHeel);
            UpdateContact(ref f.toeDown, ref f.toeLock, toeHeight, f.toeMin, toeAhead, sweep, shownToe);
        }
        f.lastHeel = heel;
        f.lastToe = toe;

        // Pied à plat : la pointe ET l'axe du pied restent où ils se sont posés — sinon le pied
        // pivote autour de la pointe retenue, et le talon balaie le sol (course de côté). Pointe
        // seule : elle tient, le pied tourne librement autour d'elle. Talon seul : il tient à son
        // tour. Levé : l'écart se résorbe en l'air.
        float decay = Mathf.Exp(-dt / ReleaseTime);
        float yaw = f.yaw * decay;
        Vector3 ankle;
        if (f.heelDown && f.toeDown)
        {
            Vector3 animAxis = Horizontal(toe - heel);
            Vector3 lockAxis = Horizontal(f.toeLock - f.heelLock);
            if (animAxis.sqrMagnitude > 1e-6f && lockAxis.sqrMagnitude > 1e-6f)
                yaw = Vector3.SignedAngle(animAxis, lockAxis, Vector3.up);
            ankle = f.toeLock + Quaternion.AngleAxis(yaw, Vector3.up) * (heel - toe);
        }
        else if (f.toeDown) ankle = f.toeLock + Quaternion.AngleAxis(yaw, Vector3.up) * (heel - toe);
        else if (f.heelDown) ankle = f.heelLock;
        else ankle = heel + f.correction * decay;
        Vector3 desired = Horizontal(ankle - heel);

        if (desired.sqrMagnitude > MaxCorrection * MaxCorrection || Mathf.Abs(yaw) > MaxYaw)
        {
            // La jambe se tendrait ou se tordrait trop : le pied est relâché, l'écart se résorbe en l'air.
            f.heelDown = f.toeDown = false;
            desired = f.correction * decay;
            yaw = f.yaw * decay;
        }
        // À l'arrêt, un pied posé que le corps a trop tiré (le corps a tourné sur place au-dessus de
        // lui, ou il change de posture) fait un petit pas : il se lève et va se reposer là où
        // l'attente le veut, un pied à la fois. Il ne bouge donc qu'en l'air.
        bool idle = bodyVelocity.magnitude < StepBodySpeed;
        if (!f.stepping && active && idle && !otherStepping && (f.heelDown || f.toeDown)
            && (desired.sqrMagnitude > StepDistance * StepDistance || Mathf.Abs(yaw) > StepYaw))
        {
            f.stepping = true;
            f.stepT = 0f;
            // Là où il est retenu À CETTE IMAGE : la correction de l'image d'avant le ferait sauter
            // de ce que l'animation a bougé entre-temps.
            f.stepFrom = heel + desired;
            f.stepTo = heel;
            f.stepYawFrom = yaw;
            f.heelDown = f.toeDown = false;
        }
        if (f.stepping)
        {
            f.stepT += dt / StepTime;
            float t = Mathf.Clamp01(f.stepT);
            // Le pied se lève, avance, puis se repose : il ne quitte sa place qu'une fois décollé, et
            // sa destination est figée dès qu'il redescend (sinon la pointe frotte le sol au départ
            // et à l'arrivée du pas, en suivant un fondu de posture qui bouge encore).
            float e = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, t));
            if (t < 0.85f) f.stepTo = heel;
            // Levé au-dessus de la pose ANIMÉE, pas au-dessus de son point de départ : en changeant
            // de posture, le talon animé monte (accroupi, le pied droit est sur la pointe) et la
            // pointe, levée d'autant que lui, restait au sol.
            Vector3 target = Vector3.Lerp(f.stepFrom, f.stepTo, e);
            target.y = heel.y + lift + StepHeight * Mathf.Sin(Mathf.PI * t);
            Quaternion stepRotation = Quaternion.AngleAxis(Mathf.Lerp(f.stepYawFrom, 0f, e), Vector3.up) * f.foot.rotation;
            SolveTwoBone(f.upper, f.lower, f.foot, target);
            f.foot.rotation = stepRotation;
            f.correction = Horizontal(target - heel);
            f.yaw = Mathf.Lerp(f.stepYawFrom, 0f, e);
            if (f.stepT >= 1f)
            {
                // Reposé : il tient tout de suite là où il vient d'arriver. Attendre l'image
                // suivante pour le détecter au sol le laisserait suivre l'animation une image de
                // plus, et en plein changement de posture, elle bouge encore.
                f.stepping = false;
                f.correction = Vector3.zero;
                f.yaw = 0f;
                f.heelDown = f.toeDown = true;
                f.heelLock = f.foot.position;
                f.toeLock = f.toes.position;
            }
            return;
        }

        f.correction = desired;
        f.yaw = yaw;

        if (f.correction.sqrMagnitude > 1e-8f || Mathf.Abs(f.yaw) > 0.01f || lift > 0f)
        {
            Quaternion footRotation = Quaternion.AngleAxis(f.yaw, Vector3.up) * f.foot.rotation;
            SolveTwoBone(f.upper, f.lower, f.foot, heel + f.correction + Vector3.up * lift);
            f.foot.rotation = footRotation;
        }
    }

    private static void UpdateContact(ref bool down, ref Vector3 locked, float height, float minHeight, float speed, float sweep, Vector3 displayed)
    {
        if (down)
        {
            if (height > minHeight + ReleaseHeight) down = false;
        }
        else if (height < minHeight + ContactHeight && speed < sweep)
        {
            down = true;
            locked = displayed; // là où on le VOIT, correction en cours comprise : aucun saut
        }
    }

    private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>
    /// IK analytique à deux os : amène `end` sur `target` en pliant `upper` et `lower`, dans le plan
    /// où la jambe est déjà pliée (le genou garde sa direction). La rotation de `end` dans le monde
    /// est conservée : le pied reste à plat. Hors de portée, la jambe se tend vers la cible.
    /// Fonction pure sur des transforms, testée.
    /// </summary>
    public static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target)
    {
        Quaternion endRotation = end.rotation;
        Vector3 a = upper.position, b = lower.position, c = end.position;
        float lab = Vector3.Distance(a, b);
        float lcb = Vector3.Distance(b, c);
        float wanted = Vector3.Distance(a, target);
        float lat = Mathf.Clamp(wanted, Mathf.Abs(lab - lcb) + 1e-3f, lab + lcb - 1e-3f);
        if (wanted - lat > 0.005f) OutOfReach++;

        float acab0 = AngleBetween(c - a, b - a);
        float babc0 = AngleBetween(a - b, c - b);
        float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis = Vector3.Cross(c - a, b - a);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(c - a, upper.forward);
        axis.Normalize();

        upper.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * upper.rotation;
        lower.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis) * lower.rotation;

        a = upper.position;
        c = end.position;
        upper.rotation = Quaternion.FromToRotation(c - a, target - a) * upper.rotation;
        end.rotation = endRotation;
    }

    private static float AngleBetween(Vector3 u, Vector3 v)
    {
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
    }
}
