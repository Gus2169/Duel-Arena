using UnityEngine;

/// <summary>
/// Les mains sur l'arme (2026-10-17, dette n° 71) : la main gauche sur le garde-main de l'arme en main
/// (le repère « MainGauche » de son modèle, ou celui de la poignée montée), et la droite sur le poignet
/// d'une crosse droite (« MainDroite » : FP-30, FAP-30), par une IK à deux os sur chaque bras
/// (TwoBoneIK). Sur le corps (PlayerAnimator) comme sur les bras de la vue subjective (FirstPersonView).
///
/// Les clips de Mixamo posent la main gauche pour un garde-main profond : 11 à 17 cm sous le repère, et
/// en arrière (mesuré le 2026-10-15). On l'y ramène APRÈS tout ce qui tourne la colonne (la direction du
/// buste, puis le lean) : une IK de l'Animator (OnAnimatorIK) passerait avant, et la main manquerait
/// l'arme de toute leur rotation.
///
/// Ce qui touche l'arme : sur le corps, vu des autres, le poing du gant du taser (la main y est repliée),
/// posé contre le repère du côté où regarde la paume de la prise ; sur les bras, gant ôté, la paume,
/// orientée selon la prise (sous le garde-main, autour d'une poignée verticale, contre une poignée
/// inclinée). Le coude plie vers le bas et l'extérieur ; quand le bras presque tendu n'atteint pas (le
/// garde-main d'un pompe, loin devant), la clavicule avance l'épaule.
///
/// La main gauche lâche l'arme (poids lissé) pendant le rechargement (elle va au chargeur), le coup de
/// taser (elle frappe), le franchissement et la mort ; elle n'a rien à tenir sur un pistolet, qui n'a pas
/// de repère (la pose de pistolet le tient à deux mains).
///
/// La main droite d'une crosse droite : l'arme est son enfant. On retient sa pose dans le monde, on
/// déplace la main, puis on remet l'arme à cette pose : elle reste placée par son origine, là où
/// l'animation l'a mise. Son décalage sous la main a changé : RestoreWeapon le remet au point de prise au
/// début de l'image suivante, avant que rien ne lise sa mire (la direction du buste, le placement des
/// bras de la vue subjective). Les bras de la vue subjective lâchent ce poignet en visée
/// (FirstPersonView.StraightStockHold).
///
/// Catégorie C stricte : ni la simulation, ni le tir, ni la surface touchable ne lisent ces os.
/// </summary>
public class WeaponGrip
{
    /// <summary>Comment la main gauche tient l'arme, dans les axes de l'arme (x à droite, y en haut, z vers
    /// la bouche) : où regarde sa paume, et où vont ses doigts (du poignet vers les jointures).</summary>
    [System.Serializable]
    public struct HandHold
    {
        public Vector3 palm;
        public Vector3 fingers;

        public HandHold(Vector3 palm, Vector3 fingers)
        {
            this.palm = palm;
            this.fingers = fingers;
        }

        /// <summary>La rotation de la main gauche dans les axes de l'arme : la paume est le +Z de son os,
        /// les doigts son +Y (le Y Bot de Mixamo).</summary>
        public Quaternion Rotation => Quaternion.LookRotation(palm, fingers);
    }

    /// <summary>Les réglages de la prise (ressenti), portés par PlayerAnimator dans le préfab joueur ; la vue
    /// subjective prend les mêmes.</summary>
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Temps (s) que met la main gauche à lâcher l'arme ou à la reprendre : au rechargement, au franchissement.")]
        public float blendTime = 0.15f;

        [Tooltip("Sous le garde-main : la paume vers le haut, les doigts vers le flanc droit et l'avant (la prise des clips de visée, mesurée sur le Y Bot).")]
        public HandHold handguard = new HandHold(new Vector3(0.1f, 1f, 0.15f), new Vector3(0.65f, -0.15f, 0.75f));

        [Tooltip("Autour d'une poignée verticale (PV-1) : la paume contre son flanc gauche, les doigts vers l'avant, le pouce en haut.")]
        public HandHold verticalGrip = new HandHold(new Vector3(1f, -0.1f, -0.1f), new Vector3(0.1f, 0.3f, 0.95f));

        [Tooltip("Contre une poignée inclinée (PI-1) : comme sous le garde-main, la paume appuyée sur la rampe.")]
        public HandHold angledGrip = new HandHold(new Vector3(0.3f, 1f, 0.3f), new Vector3(0.65f, -0.15f, 0.75f));

        [Tooltip("Main nue (bras de la vue subjective) : le point de la paume qui se pose sur le repère, dans le repère de l'os de la main (m ; +Y vers les doigts, +Z côté paume). Mesuré sur la peau du Y Bot : le plus saillant du milieu de la paume.")]
        public Vector3 palmPoint = new Vector3(0.010f, 0.050f, 0.037f);

        [Tooltip("Gant du taser (vu des autres) : le milieu de son poing, dans le repère du gant (m ; +Z vers le poing, +Y vers le dos de la main, +X vers le pouce). Mesuré sur le GD-1 : la boîte des sommets au-delà de 10 cm du poignet.")]
        public Vector3 gloveFistCenter = new Vector3(0.011f, -0.01f, 0.146f);

        [Tooltip("Et ses demi-dimensions (m), le poing pris pour un ellipsoïde : il se pose du côté du repère où regarde la paume de la prise, son bord juste contre lui.")]
        public Vector3 gloveFistHalfSize = new Vector3(0.071f, 0.056f, 0.046f);

        [Tooltip("Où plie le coude gauche, dans les axes de l'arme : vers le bas et l'extérieur (à gauche).")]
        public Vector3 elbowPole = new Vector3(-0.5f, -1f, 0f);

        [Tooltip("Part du pôle dans le pli du coude : 0 garde celui du clip.")]
        [Range(0f, 1f)] public float elbowPoleWeight = 1f;

        [Tooltip("Crosse droite (FP-30, FAP-30) : la main droite tourne de tant (degrés, autour de l'axe gauche-droite de l'arme, l'auriculaire vers la crosse) par rapport à sa tenue d'une poignée pistolet, pour envelopper le poignet de la crosse.")]
        public float straightStockAngle = 60f;

        [Tooltip("Rotation maximale (degrés) de la clavicule gauche, qui avance l'épaule quand le bras, presque tendu, n'atteint pas le garde-main (celui d'un pompe, loin devant).")]
        public float maxShoulderTurn = 15f;
    }

    /// <summary>Part de sa longueur au-delà de laquelle le bras ne se tend plus : la clavicule avance
    /// l'épaule pour le reste. Un coude verrouillé fait raide.</summary>
    public const float MaxArmExtension = 0.97f;

    private readonly Transform leftShoulder, leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand, anchor, glove;
    private readonly bool valid;
    private float leftWeight;
    private Transform movedWeapon;          // l'arme dont la main droite a changé le décalage
    private AttachmentCatalog gripCodesOf;  // le catalogue dont on a relu le code de la poignée inclinée
    private string angledGripCode;

    public WeaponGrip(Animator animator)
    {
        if (animator == null) return;
        leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        anchor = rightHand != null ? rightHand.Find(HeldWeapons.GripAnchorName) : null;
        glove = HeldWeapons.TaserGlove(animator);
        valid = leftUpper != null && leftLower != null && leftHand != null
             && rightUpper != null && rightLower != null && rightHand != null;
    }

    /// <summary>La main gauche tient-elle l'arme, de 0 à 1 (lissé ; tests).</summary>
    public float LeftWeight => leftWeight;

    /// <summary>
    /// Remet l'arme que la main droite a déplacée à son point de prise (« PriseArme »). À appeler au début
    /// de l'image, avant toute lecture de sa mire : l'Animator a remis la main dans sa pose animée, et
    /// l'arme, enfant de la main, n'y est plus.
    /// </summary>
    public void RestoreWeapon()
    {
        if (movedWeapon == null) return;
        movedWeapon.localPosition = anchor != null ? anchor.localPosition : Vector3.zero;
        movedWeapon.localRotation = anchor != null ? anchor.localRotation : Quaternion.identity;
        movedWeapon = null;
    }

    /// <summary>
    /// Pose les mains sur l'arme `model` (null : aucune). À appeler après tout ce qui tourne la colonne.
    /// </summary>
    /// <param name="catalog">Le catalogue des accessoires de l'arme : dit quelle poignée est montée.</param>
    /// <param name="leftWanted">La main gauche doit-elle tenir l'arme ? (ni rechargement, ni franchissement,
    /// ni mort). Le poids y va en `Settings.blendTime`.</param>
    /// <param name="leftScale">Multiplie ce poids sans lissage : le haut du corps qui s'efface (passage au
    /// sol), le geste du taser qui monte.</param>
    /// <param name="rightScale">Le poids de la main droite sur le poignet d'une crosse droite, sans lissage :
    /// 1 sur le corps, moins sur les bras de la vue subjective qui épaulent (FirstPersonView.StraightStockHold).</param>
    public void Apply(WeaponModel model, AttachmentCatalog catalog, bool leftWanted, float leftScale, Settings settings, float dt,
                      float rightScale = 1f)
    {
        if (!valid || settings == null) return;
        leftWeight = Mathf.MoveTowards(leftWeight, leftWanted && model != null ? 1f : 0f,
                                       Mathf.Max(0f, dt) / Mathf.Max(0.01f, settings.blendTime));
        if (model == null) return;
        RestoreWeapon();

        Transform weapon = model.transform;
        if (model.RightHand != null)
        {
            // Au point de prise, quoi qu'il soit arrivé avant (une copie du corps prise pendant que la main
            // le tenait déplacé) : c'est de là que l'animation place l'arme.
            weapon.localPosition = anchor != null ? anchor.localPosition : Vector3.zero;
            weapon.localRotation = anchor != null ? anchor.localRotation : Quaternion.identity;
        }
        Vector3 weaponPosition = weapon.position;
        Quaternion weaponRotation = weapon.rotation;
        if (model.RightHand != null && rightScale > 1e-4f)
            HoldStraightStock(model.RightHand.position, weapon, weaponPosition, weaponRotation, settings, Mathf.Clamp01(rightScale));

        float w = Mathf.SmoothStep(0f, 1f, leftWeight) * Mathf.Clamp01(leftScale);
        Transform marker = LeftHold(model, catalog, settings, out HandHold hold);
        if (marker == null || w <= 1e-4f) return;

        Quaternion holdRotation = weaponRotation * hold.Rotation;
        Vector3 pole = weaponRotation * settings.elbowPole;
        float poleWeight = settings.elbowPoleWeight * w;
        if (glove != null && glove.gameObject.activeInHierarchy)
        {
            // Le gant (vu des autres) : le poing contre le repère, du côté où regarde la paume. Son bord dans
            // cette direction dépend de la façon dont il est tourné, que la prise change : deux passes.
            Vector3 palm = holdRotation * Vector3.forward;
            Vector3 fist = leftLower.InverseTransformPoint(glove.TransformPoint(settings.gloveFistCenter));
            Vector3 start = leftLower.TransformPoint(fist);
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 target = Vector3.Lerp(start, marker.position - palm * FistExtent(glove.InverseTransformDirection(palm), settings.gloveFistHalfSize), w);
                if (pass == 0) ReachWithShoulder(fist, target, settings.maxShoulderTurn * w);
                TwoBoneIK.Solve(leftUpper, leftLower, fist, target, pole, poleWeight);
            }
        }
        else
        {
            // La main nue (vue subjective) : la paume sur le repère, orientée selon la prise.
            Quaternion handRotation = Quaternion.Slerp(leftHand.rotation, holdRotation, w);
            Vector3 wrist = leftLower.InverseTransformPoint(leftHand.position);
            Vector3 target = Vector3.Lerp(leftHand.position, marker.position - holdRotation * settings.palmPoint, w);
            ReachWithShoulder(wrist, target, settings.maxShoulderTurn * w);
            TwoBoneIK.Solve(leftUpper, leftLower, wrist, target, pole, poleWeight);
            leftHand.rotation = handRotation;
        }
    }

    /// <summary>
    /// Quand le bras gauche, tendu à MaxArmExtension de sa longueur, n'atteindrait pas `target` avec le
    /// point `effector` de l'avant-bras, la clavicule tourne (`maxTurn` degrés au plus) pour avancer
    /// l'épaule d'autant vers la cible, comme on avance l'épaule pour tenir le garde-main d'un pompe.
    /// </summary>
    private void ReachWithShoulder(Vector3 effector, Vector3 target, float maxTurn)
    {
        if (leftShoulder == null || maxTurn <= 0f) return;
        Vector3 shoulder = leftUpper.position;
        float length = Vector3.Distance(shoulder, leftLower.position) + Vector3.Distance(leftLower.position, leftLower.TransformPoint(effector));
        float missing = Vector3.Distance(shoulder, target) - length * MaxArmExtension;
        if (missing <= 0f) return;
        Vector3 clavicle = leftShoulder.position;
        Vector3 moved = shoulder + (target - shoulder).normalized * missing;
        Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(shoulder - clavicle, moved - clavicle), maxTurn);
        leftShoulder.rotation = turn * leftShoulder.rotation;
    }

    /// <summary>Jusqu'où le poing s'étend de son milieu dans la direction `direction` (unitaire, repère du
    /// gant), pris pour un ellipsoïde de demi-dimensions `halfSize`. Pure, testée.</summary>
    public static float FistExtent(Vector3 direction, Vector3 halfSize)
    {
        return new Vector3(direction.x * halfSize.x, direction.y * halfSize.y, direction.z * halfSize.z).magnitude;
    }

    /// <summary>La main droite sur le poignet d'une crosse droite (`grip`, le repère « MainDroite ») : la
    /// prise d'une poignée pistolet (le décalage de « PriseArme »), tournée pour envelopper le poignet, et
    /// l'arme remise là où l'animation l'avait mise. À un poids `weight` inférieur à 1, la main s'arrête
    /// entre sa pose animée et la prise.</summary>
    private void HoldStraightStock(Vector3 grip, Transform weapon, Vector3 weaponPosition, Quaternion weaponRotation, Settings settings,
                                   float weight)
    {
        Vector3 anchorPosition = anchor != null ? anchor.localPosition : Vector3.zero;
        Quaternion anchorRotation = anchor != null ? anchor.localRotation : Quaternion.identity;
        Quaternion handRotation = weaponRotation * Quaternion.AngleAxis(settings.straightStockAngle, Vector3.right)
                                  * Quaternion.Inverse(anchorRotation);
        Vector3 wrist = grip - handRotation * Vector3.Scale(anchorPosition, rightHand.lossyScale);
        if (weight < 1f)
        {
            wrist = Vector3.Lerp(rightHand.position, wrist, weight);
            handRotation = Quaternion.Slerp(rightHand.rotation, handRotation, weight);
        }
        TwoBoneIK.Solve(rightUpper, rightLower, rightLower.InverseTransformPoint(rightHand.position), wrist, Vector3.zero, 0f);
        rightHand.rotation = handRotation;
        weapon.SetPositionAndRotation(weaponPosition, weaponRotation);
        movedWeapon = weapon;
    }

    /// <summary>Où la main gauche tient l'arme : la poignée montée (son repère « MainGauche »), sinon le
    /// garde-main du modèle (null pour un pistolet) ; et comment.</summary>
    private Transform LeftHold(WeaponModel model, AttachmentCatalog catalog, Settings settings, out HandHold hold)
    {
        hold = settings.handguard;
        WeaponAttachment grip = AttachmentMount.Mounted(model.transform, AttachmentCategory.Grip);
        if (grip == null || grip.LeftHand == null) return model.LeftHand;
        hold = IsAngledGrip(grip, catalog) ? settings.angledGrip : settings.verticalGrip;
        return grip.LeftHand;
    }

    private bool IsAngledGrip(WeaponAttachment grip, AttachmentCatalog catalog)
    {
        if (catalog == null) return false;
        if (catalog != gripCodesOf)
        {
            gripCodesOf = catalog;
            angledGripCode = catalog.angledGrip != null && catalog.angledGrip.TryGetComponent(out WeaponAttachment angled)
                ? angled.Code : null;
        }
        return angledGripCode != null && grip.Code == angledGripCode;
    }
}
