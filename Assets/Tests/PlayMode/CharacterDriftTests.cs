#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le personnage affiché ne doit JAMAIS s'éloigner de sa propre racine.
///
/// POURQUOI CE TEST EXISTE : ce défaut a été livré deux fois de suite, et les deux fois une
/// mesure hors runtime l'a déclaré corrigé alors qu'il ne l'était pas.
///
/// - `AnimationClip.averageSpeed` mesure le DELTA DE ROOT MOTION. Mettre "Bake Into Pose" le fait
///   tomber à zéro tout en LAISSANT le corps voyager dans la pose : la mesure disait « plus de
///   déplacement » pendant que le personnage dérivait puis claquait en arrière à chaque boucle.
/// - `AnimationClip.SampleAnimation` applique les courbes BRUTES, sans la gestion de root motion
///   de l'Animator. Une animation réellement "In Place" y affiche plusieurs mètres de dérive.
///
/// Les deux mesurent quelque chose de vrai, mais pas CE QUI SE VOIT À L'ÉCRAN. Seul le runtime
/// le mesure, parce que la séparation racine/pose est un travail que fait l'Animator, pas le clip.
///
/// Le test fait donc tourner le vrai Animator avec le vrai controller et relève la position des
/// hanches par rapport à la racine, frame par frame.
/// </summary>
public class CharacterDriftTests
{
    /// <summary>Une foulée fait osciller les hanches de quelques centimètres — c'est normal et
    /// souhaitable. Au-delà, le corps QUITTE sa racine, ce qui est le défaut recherché.</summary>
    private const float DriftLimit = 0.5f;

    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private static IEnumerator MeasureDrift(GameObject go, Animator animator, int stance, float moveY, int frames, System.Action<float> report)
    {
        animator.SetInteger("Stance", stance);
        animator.SetFloat("MoveX", 0f);
        animator.SetFloat("MoveY", moveY);
        animator.SetFloat("SpeedMult", 1f);

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Assert.IsNotNull(hips, "Os Hips introuvable : le rig n'est pas humanoïde.");

        // Quelques frames pour que la transition d'état se termine avant de mesurer.
        for (int i = 0; i < 20; i++) yield return null;

        float max = 0f;
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            Vector3 local = go.transform.InverseTransformPoint(hips.position);
            float drift = new Vector2(local.x, local.z).magnitude;
            if (drift > max) max = drift;
        }
        // On JOURNALISE la valeur, pas seulement le verdict. Un test vert dit « sous le seuil »
        // sans dire de combien : connaître la marge réelle (quelques centimètres d'oscillation de
        // foulée contre un seuil à 50 cm, et plusieurs MÈTRES quand le défaut est présent) est ce
        // qui permet de juger si le seuil est bien placé.
        Debug.Log($"[DRIFT] stance={stance} moveY={moveY} derive_max={max:F3}m seuil={DriftLimit:F2}m");
        report(max);
    }

    private IEnumerator Setup()
    {
        // Le personnage du prefab, pas un chemin en dur : voir TestCharacter.
        instance = TestCharacter.InstantiateModel(out _);
        yield return null;
    }

    [UnityTest]
    public IEnumerator CourseDebout_LeCorpsResteSurSaRacine()
    {
        yield return Setup();
        float drift = -1f;
        yield return MeasureDrift(instance, instance.GetComponent<Animator>(), 0, 1f, 120, d => drift = d);
        Assert.Less(drift, DriftLimit, "Le corps s'éloigne de sa racine de " + drift.ToString("F2") + " m en courant.");
    }

    [UnityTest]
    public IEnumerator SprintDebout_LeCorpsResteSurSaRacine()
    {
        yield return Setup();
        float drift = -1f;
        yield return MeasureDrift(instance, instance.GetComponent<Animator>(), 0, 2f, 120, d => drift = d);
        Assert.Less(drift, DriftLimit, "Le corps s'éloigne de sa racine de " + drift.ToString("F2") + " m en sprintant.");
    }

    [UnityTest]
    public IEnumerator MarcheAccroupie_LeCorpsResteSurSaRacine()
    {
        yield return Setup();
        float drift = -1f;
        yield return MeasureDrift(instance, instance.GetComponent<Animator>(), 1, 1f, 120, d => drift = d);
        Assert.Less(drift, DriftLimit, "Le corps s'éloigne de sa racine de " + drift.ToString("F2") + " m accroupi.");
    }
}
#endif
