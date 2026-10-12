#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Vu par les autres, le fusil pointe là où le joueur regarde (2026-10-09).
///
/// Le défaut, rapporté par l'utilisateur : « je vise dans la direction d'un ennemi, mais sur la vue
/// de l'ennemi, l'autre joueur ne regarde pas dans la bonne direction ». Mesuré : immobile en
/// visée, le fusil pointait 36° à gauche du regard, et de -100° à +33° selon ce que jouaient les
/// jambes. Le buste de visée est greffé sur des hanches qui ne sont pas tournées comme dans son clip.
///
/// Le test joue le vrai contrôleur, puis applique la correction du jeu (les mêmes fonctions que
/// PlayerAnimator.ApplyAimDirection, sans le lissage) et mesure la mire du fusil.
/// </summary>
public class TorsoAimTests
{
    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private static readonly (string name, int stance, Vector2 move)[] Poses =
    {
        ("debout immobile", 0, Vector2.zero),
        ("debout en avant", 0, new Vector2(0f, 0.5f)),
        ("debout à droite", 0, new Vector2(0.5f, 0f)),
        ("debout à gauche", 0, new Vector2(-0.5f, 0f)),
        ("debout en arrière", 0, new Vector2(0f, -0.5f)),
        ("accroupi immobile", 1, Vector2.zero),
        ("accroupi à droite", 1, new Vector2(0.5f, 0f)),
    };

    /// <summary>Cap et élévation d'une direction dans le repère du joueur, en degrés.</summary>
    private Vector2 YawElevation(Vector3 worldDirection)
    {
        Vector3 f = instance.transform.InverseTransformDirection(worldDirection);
        return new Vector2(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg);
    }

    private void Pose(Animator animator, int stance, Vector2 move, bool aiming)
    {
        animator.Rebind();
        animator.Update(0f);
        for (int i = 0; i < 150; i++)
        {
            animator.SetInteger("Stance", stance);
            animator.SetFloat("StanceF", stance);
            animator.SetFloat("Marche", move == Vector2.zero ? 0f : 1f);
            PlayerAnimator.SetLocomotion(animator, move);
            animator.SetFloat("LocoSpeed", 1f);
            animator.SetFloat("SpeedMult", 1f);
            animator.SetBool("Aiming", aiming);
            animator.Update(1f / 60f);
        }
    }

    private static float PrefabFloat(string field)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
        return new UnityEditor.SerializedObject(prefab.GetComponent<PlayerAnimator>()).FindProperty(field).floatValue;
    }

    [Test]
    public void EnVisee_LeFusilPointeLaOuRegardeLeJoueur_QuoiQueJouentLesJambes()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        instance.transform.SetPositionAndRotation(new Vector3(3f, 0f, -2f), Quaternion.Euler(0f, 40f, 0f));
        Transform sight = PlayerAnimator.FindSight(animator);
        Transform[] spine = PlayerAnimator.SpineChain(animator);
        Assert.IsNotNull(sight, "Le fusil du modèle n'a pas de repère « Mire ».");
        float maxYaw = PrefabFloat("maxTorsoYaw");
        float maxPitch = PrefabFloat("maxTorsoPitch");

        var failures = new List<string>();
        float worstBefore = 0f;
        foreach (float aimPitch in new[] { 0f, -30f, 25f }) // + = vers le bas, comme la caméra
        {
            foreach (var p in Poses)
            {
                Pose(animator, p.stance, p.move, true);
                Vector2 before = YawElevation(sight.forward);
                worstBefore = Mathf.Max(worstBefore, Mathf.Abs(before.x));

                PlayerAnimator.TorsoCorrection(before.x, before.y, 0f, aimPitch, 1f, maxYaw, maxPitch,
                                               out float yaw, out float elevation);
                PlayerAnimator.RotateSpine(spine, instance.transform.right, yaw, elevation);

                Vector2 after = YawElevation(sight.forward);
                string line = $"{p.name}, regard à {-aimPitch:F0}° : fusil de ({before.x:F0}° ; {before.y:F0}°) à ({after.x:F1}° ; {after.y:F1}°)";
                Debug.Log("[BUSTE] " + line);
                if (Mathf.Abs(after.x) > 1f || Mathf.Abs(after.y + aimPitch) > 1f) failures.Add(line);
            }
        }

        // Garde anti-vacuité : sans correction, le fusil part bien de travers dans ce montage.
        Assert.Greater(worstBefore, 20f, "Le fusil pointait déjà droit : le test ne prouverait rien.");
        Assert.IsEmpty(failures, "Le fusil ne pointe pas où le joueur regarde :\n" + string.Join("\n", failures));
    }

    [Test]
    public void ArmeBasse_LeTorseFaitFaceAuRegard_EtLeFusilResteTenuEnTravers()
    {
        instance = TestCharacter.InstantiateModel(out Animator animator);
        instance.transform.rotation = Quaternion.Euler(0f, -70f, 0f);
        Transform sight = PlayerAnimator.FindSight(animator);
        Transform[] spine = PlayerAnimator.SpineChain(animator);
        Transform torso = spine[spine.Length - 1];
        float maxYaw = PrefabFloat("maxTorsoYaw");

        var failures = new List<string>();
        float worstBefore = 0f;
        foreach (var p in Poses)
        {
            Pose(animator, p.stance, p.move, false);
            Vector2 rifle = YawElevation(sight.forward);
            float torsoYaw = YawElevation(torso.forward).x;
            worstBefore = Mathf.Max(worstBefore, Mathf.Abs(torsoYaw));

            // Arme basse : pas de tangage, même si le joueur regarde en l'air.
            PlayerAnimator.TorsoCorrection(rifle.x, rifle.y, torsoYaw, -30f, 0f, maxYaw, 70f,
                                           out float yaw, out float elevation);
            Assert.AreEqual(0f, elevation, 1e-4f, "Arme basse, le buste ne doit pas suivre le tangage.");
            PlayerAnimator.RotateSpine(spine, instance.transform.right, yaw, elevation);

            float after = YawElevation(torso.forward).x;
            Debug.Log($"[BUSTE] arme basse, {p.name} : torse de {torsoYaw:F0}° à {after:F1}°");
            if (Mathf.Abs(after) > 1f) failures.Add($"{p.name} : torse à {after:F1}°");
        }

        Assert.Greater(worstBefore, 20f, "Le torse faisait déjà face au regard : le test ne prouverait rien.");
        Assert.IsEmpty(failures, "Arme basse, le torse ne fait pas face au regard :\n" + string.Join("\n", failures));
    }
}
#endif
