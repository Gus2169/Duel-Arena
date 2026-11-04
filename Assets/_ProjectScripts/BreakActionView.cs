using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ce qu'une arme qui s'ouvre montre pendant son rechargement (2026-11-03 ; BreakActionGesture, le Big D du Golden
/// Gun), sur un Animator : le corps (vu des autres) ou les bras de la vue subjective. MagazineSwap en tient une par
/// Animator, à côté de la vue du chargeur, et la fait avancer au rythme du geste que montre l'Animator
/// (MagazineSwap.View.Progress). Catégorie C : rien de la simulation ne la lit.
///
/// À chaque image, après l'Animator et ce qui retouche les mains : le canon, le chien et le levier tournent comme le dit
/// le geste (BreakActionGesture.PoseAt) ; la cartouche de la chambre montre la douille vide qui glisse dehors (sans sa
/// balle), puis rien, puis la cartouche neuve qui entre ; quand la douille quitte la chambre, une copie tombe
/// (DroppedMagazine) ; de la saisie à la présentation, une copie de la cartouche neuve est dans la main gauche, à la
/// paume de la main montrée, et rejoint la chambre sans à-coup ; le pouce droit, qui abaisse le levier puis arme le
/// chien, prend les poses du geste (BreakActionGesture.ThumbAt). Hors du rechargement, une arme vide (le coup parti) a
/// son chien abattu et sa douille vide dans la chambre ; sinon, elle est au repos, chargée. L'arme en main, rechargement
/// ou non, la main droite prend la tenue du geste (BreakActionGesture.rightHold) : ses doigts épousent la crosse.
///
/// Ce qu'on cache, on le cache par Renderer.enabled et on ne rallume que ce qu'on a éteint ; les pièces tournées
/// reprennent leur rotation de repos. Un rechargement interrompu, une autre arme, rendent aussitôt l'arme intacte.
/// </summary>
public sealed class BreakActionView
{
    /// <summary>La douille vide tourne en tombant (rad/s), autour de l'axe gauche-droite de l'arme : plus petite qu'un
    /// chargeur, elle tourne plus vite.</summary>
    public const float CaseTumble = 12f;

    private readonly bool arms;
    private readonly Transform leftHand;
    private readonly Transform[] hand;    // les phalanges de la main droite que la tenue repose (BreakActionGesture.HoldOrder)
    private readonly Transform[] thumb;   // les phalanges du pouce droit, de la base au bout
    private readonly Quaternion[] thumbPose = new Quaternion[BreakActionGesture.ThumbBones];
    private WeaponModel model;
    private BreakActionGesture boundTo;
    private Transform barrel, hammer, latch, cartridge;
    private Quaternion barrelRest, hammerRest, latchRest;
    private Vector3 cartridgeRest;
    private Renderer[] caseRenderers = new Renderer[0], bulletRenderers = new Renderer[0];
    private readonly List<Renderer> hidden = new List<Renderer>();
    private GameObject inHand;
    private bool posed;
    private float last = float.NaN;   // la part montrée à l'image d'avant
    private Vector3 lastWeaponPosition, weaponVelocity;
    private bool tracked;

    /// <summary>`arms` : les bras de la vue subjective (la douille vide y tombe sans physique).</summary>
    public BreakActionView(Animator animator, bool arms)
    {
        this.arms = arms;
        leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        hand = new Transform[animator != null && animator.isHuman ? BreakActionGesture.HoldOrder.Length : 0];
        for (int b = 0; b < hand.Length; b++) hand[b] = animator.GetBoneTransform(BreakActionGesture.HoldOrder[b]);
        thumb = new Transform[hand.Length > 0 ? BreakActionGesture.ThumbBones : 0];
        for (int b = 0; b < thumb.Length; b++) thumb[b] = hand[b];
    }

    /// <summary>La copie de la cartouche neuve dans la main gauche, ou null.</summary>
    public GameObject InHand => inHand;

    /// <summary>La part du rechargement montrée à cette image (0 pour une arme vide hors du rechargement), ou NaN.</summary>
    public float Shown => last;

    /// <summary>
    /// Une image. `shown` : le modèle d'arme montré (null : aucun) ; `gesture` : le geste de l'arme en main s'il s'ouvre
    /// (null sinon) ; `progress` : la part du rechargement que l'Animator montre de ce geste (MagazineSwap.View.Progress,
    /// NaN hors de lui) ; `empty` : l'arme est vide (le coup parti, le rechargement pas encore commencé) ; `palmShift` :
    /// le point de la paume de la main montrée moins celui du geste (repère de l'os de la main).
    /// </summary>
    public void Step(WeaponModel shown, BreakActionGesture gesture, float progress, bool empty, Vector3 palmShift,
                     MagazineSwap.Drop settings, float dt)
    {
        Track(shown, dt);
        if (shown != model || gesture != boundTo)
        {
            // un autre modèle (l'arme changée, les armes reconstruites) : l'ancien est rendu intact
            Restore();
            model = shown;
            boundTo = gesture;
            Bind();
        }
        // La main droite sur l'arme, au rechargement ou non : ses phalanges comme la tenue du geste (la prise des
        // pistolets du clip refermait la main sur deux centimètres, et ses doigts entraient dans la crosse). L'Animator
        // les réécrit à chaque image : rien à rendre.
        if (model != null && gesture != null && gesture.HasHold && hand.Length == gesture.rightHold.Length)
            for (int b = 0; b < hand.Length; b++)
                if (hand[b] != null) hand[b].localRotation = gesture.rightHold[b];
        float f = !float.IsNaN(progress) ? progress : empty ? 0f : float.NaN;
        if (model == null || gesture == null || barrel == null || cartridge == null || float.IsNaN(f))
        {
            Restore();
            last = float.NaN;
            return;
        }
        BreakActionGesture.Pose pose = gesture.PoseAt(f);
        posed = true;
        barrel.localRotation = barrelRest * BreakActionGesture.Turn(-pose.barrel);
        if (hammer != null) hammer.localRotation = hammerRest * BreakActionGesture.Turn(pose.hammer);
        if (latch != null) latch.localRotation = latchRest * BreakActionGesture.Turn(-pose.latch);

        // La chambre : la douille vide qui glisse dehors, sans balle ; puis rien ; puis la cartouche neuve qui entre.
        bool caseInChamber = !float.IsNaN(pose.caseSlide), cartridgeInChamber = !float.IsNaN(pose.cartridgeSlide);
        SetShown(caseRenderers, caseInChamber || cartridgeInChamber);
        SetShown(bulletRenderers, cartridgeInChamber);
        float slide = caseInChamber ? pose.caseSlide : cartridgeInChamber ? pose.cartridgeSlide : gesture.cartridgeApproach;
        cartridge.localPosition = cartridgeRest + Vector3.back * slide;

        // La douille vide quitte la chambre entre l'image d'avant et celle-ci : une copie tombe, de là où elle sortait.
        // Vu à partir du moment où elle est déjà partie (un joueur qui arrive), rien ne tombe.
        if (!float.IsNaN(last) && last < gesture.ejectStep.y && f >= gesture.ejectStep.y && settings != null)
            DropCase(gesture, settings);

        // La cartouche neuve dans la main gauche, de la saisie à la présentation devant la chambre.
        if (f >= gesture.grab && f < gesture.seated && leftHand != null) Hold(gesture, palmShift, f);
        else DestroyHeld();

        // Le pouce droit, qui abaisse le levier puis arme le chien : ses phalanges comme dans Blender (le clip humanoïde
        // n'en garde pas la torsion), fondues dans celles de la tenue (ou de l'Animator) au début et à la fin du geste.
        if (!float.IsNaN(progress) && thumb.Length == BreakActionGesture.ThumbBones && gesture.ThumbAt(f, thumbPose))
        {
            float w = BreakActionGesture.ThumbWeight(f);
            for (int b = 0; b < thumb.Length; b++)
                if (thumb[b] != null) thumb[b].localRotation = Quaternion.Slerp(thumb[b].localRotation, thumbPose[b], w);
        }
        last = f;
    }

    /// <summary>Les phalanges du pouce droit que la vue repose (de la base au bout), pour les tests.</summary>
    public IReadOnlyList<Transform> Thumb => thumb;

    /// <summary>Les phalanges de la main droite que la tenue repose (BreakActionGesture.HoldOrder), pour les tests.</summary>
    public IReadOnlyList<Transform> Hand => hand;

    /// <summary>Rend l'arme intacte : ses pièces au repos, la cartouche dans la chambre, rien dans la main.</summary>
    public void Restore()
    {
        if (posed)
        {
            if (barrel != null) barrel.localRotation = barrelRest;
            if (hammer != null) hammer.localRotation = hammerRest;
            if (latch != null) latch.localRotation = latchRest;
            if (cartridge != null) cartridge.localPosition = cartridgeRest;
            posed = false;
        }
        foreach (Renderer r in hidden)
            if (r != null) r.enabled = true;
        hidden.Clear();
        DestroyHeld();
    }

    private void Bind()
    {
        barrel = hammer = latch = cartridge = null;
        caseRenderers = bulletRenderers = new Renderer[0];
        if (model == null || boundTo == null) return;
        Transform root = model.transform;
        barrel = root.Find(boundTo.barrelPath);
        hammer = root.Find(boundTo.hammerPath);
        latch = root.Find(boundTo.latchPath);
        cartridge = root.Find(boundTo.cartridgePath);
        Transform casePiece = root.Find(boundTo.casePath), bullet = root.Find(boundTo.bulletPath);
        if (barrel != null) barrelRest = barrel.localRotation;
        if (hammer != null) hammerRest = hammer.localRotation;
        if (latch != null) latchRest = latch.localRotation;
        if (cartridge != null) cartridgeRest = cartridge.localPosition;
        if (casePiece != null) caseRenderers = casePiece.GetComponentsInChildren<Renderer>(true);
        if (bullet != null) bulletRenderers = bullet.GetComponentsInChildren<Renderer>(true);
    }

    private void SetShown(Renderer[] renderers, bool shown)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!shown && r.enabled)
            {
                r.enabled = false;
                hidden.Add(r);
            }
            else if (shown && hidden.Remove(r))
            {
                r.enabled = true;
            }
        }
    }

    private void Track(WeaponModel shown, float dt)
    {
        if (shown == null || dt <= 0f)
        {
            tracked = false;
            return;
        }
        Vector3 p = shown.transform.position;
        weaponVelocity = tracked ? Vector3.Lerp(weaponVelocity, (p - lastWeaponPosition) / dt, 0.5f) : Vector3.zero;
        lastWeaponPosition = p;
        tracked = true;
    }

    /// <summary>La douille vide tombe : une copie là où elle quitte la chambre, avec la vitesse de l'arme et la sienne
    /// (elle glissait de plus en plus vite), qui tourne sur elle-même.</summary>
    private void DropCase(BreakActionGesture gesture, MagazineSwap.Drop settings)
    {
        Vector3 rest = cartridge.localPosition;
        cartridge.localPosition = cartridgeRest + Vector3.back * gesture.caseTravel;
        GameObject copy = CopyPieces(caseRenderers, cartridge, "Douille vide", true);
        cartridge.localPosition = rest;
        if (copy == null) return;
        Vector3 velocity = Vector3.ClampMagnitude(weaponVelocity, settings.maxInheritedSpeed)
                           + cartridge.rotation * Vector3.back * gesture.CaseExitSpeed;
        Vector3 spin = model.transform.rotation * Vector3.right * CaseTumble;
        bool shadowOnly = false;
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            shadowOnly |= r.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
        DroppedMagazine.Spawn(copy, velocity, spin, arms ? settings.armsLifetime : settings.lifetime, !arms,
                              shadowOnly ? settings.ownerRevealDelay : 0f);
    }

    /// <summary>La cartouche neuve dans la main gauche : là où la main du geste la tient (le repère de l'arme vu de la
    /// main quand la cartouche se présente, ReloadGesture.weaponInHand…), à la paume de la main montrée, puis de la main
    /// à sa place devant la chambre entre la présentation et l'instant où elle y est (MagazineSwap.SeatBlend).</summary>
    private void Hold(BreakActionGesture gesture, Vector3 palmShift, float f)
    {
        if (inHand == null)
        {
            inHand = CopyPieces(Concat(caseRenderers, bulletRenderers), cartridge, "Cartouche neuve", false);
            if (inHand == null) return;
            // la main du corps est repliée sous le gant (échelle nulle) : la copie suit l'avant-bras
            Transform holder = leftHand.lossyScale.sqrMagnitude > 1e-4f ? leftHand : leftHand.parent;
            inHand.transform.SetParent(holder, true);
        }
        // la cartouche devant la chambre (cartridge y est posée, canon tel qu'il est), dans le repère de l'arme
        Transform weapon = model.transform;
        Vector3 local = Quaternion.Inverse(weapon.rotation) * (cartridge.position - weapon.position);
        Quaternion localRotation = Quaternion.Inverse(weapon.rotation) * cartridge.rotation;
        Quaternion hand = leftHand.rotation;
        Vector3 heldPosition = leftHand.position + hand * (gesture.weaponInHandPosition + palmShift);
        Quaternion heldRotation = hand * gesture.weaponInHandRotation;
        float w = MagazineSwap.SeatBlend(f, gesture);
        Vector3 weaponPosition = Vector3.Lerp(heldPosition, weapon.position, w);
        Quaternion weaponRotation = Quaternion.Slerp(heldRotation, weapon.rotation, w);
        inHand.transform.SetPositionAndRotation(weaponPosition + weaponRotation * local, weaponRotation * localRotation);
    }

    private void DestroyHeld()
    {
        if (inHand == null) return;
        if (Application.isPlaying) Object.Destroy(inHand);
        else Object.DestroyImmediate(inHand);
        inHand = null;
    }

    private static Renderer[] Concat(Renderer[] a, Renderer[] b)
    {
        var all = new Renderer[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    /// <summary>
    /// Une copie des pièces `pieces` (maillages, matériaux, ombres, layer), sans rien d'autre : ni collider, ni script.
    /// Sa racine est `frame` (la cartouche : son origine au culot), ou, `centered`, le milieu des pièces dans ses axes
    /// (la douille vide tourne autour d'elle-même en tombant). Les pièces y gardent leur place : leurs maillages sont dans
    /// le repère de l'arme.
    /// </summary>
    private static GameObject CopyPieces(Renderer[] pieces, Transform frame, string name, bool centered)
    {
        Vector3 origin = frame.position;
        if (centered)
        {
            // le milieu des maillages (Renderer.bounds ne vaut rien sur une pièce éteinte)
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Renderer r in pieces)
            {
                MeshFilter filter = r != null ? r.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;
                sum += r.transform.TransformPoint(filter.sharedMesh.bounds.center);
                n++;
            }
            if (n > 0) origin = sum / n;
        }
        var root = new GameObject(name);
        root.layer = frame.gameObject.layer;
        root.transform.SetPositionAndRotation(origin, frame.rotation);
        var block = new MaterialPropertyBlock();
        int count = 0;
        foreach (Renderer source in pieces)
        {
            if (!(source is MeshRenderer piece)) continue;
            MeshFilter filter = piece.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || piece.forceRenderingOff) continue;
            var part = new GameObject(piece.name);
            part.layer = piece.gameObject.layer;
            part.transform.SetParent(root.transform, false);
            part.transform.SetPositionAndRotation(piece.transform.position, piece.transform.rotation);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = piece.sharedMaterials;
            r.shadowCastingMode = piece.shadowCastingMode;
            r.receiveShadows = piece.receiveShadows;
            if (piece.HasPropertyBlock())
            {
                piece.GetPropertyBlock(block);
                r.SetPropertyBlock(block);
            }
            count++;
        }
        if (count > 0) return root;
        if (Application.isPlaying) Object.Destroy(root);
        else Object.DestroyImmediate(root);
        return null;
    }
}
