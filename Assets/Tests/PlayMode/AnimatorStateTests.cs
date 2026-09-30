#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Le graphe d'animation doit revenir à la posture RÉELLE après une chute ou un franchissement.
///
/// La première version ne renvoyait que vers « Debout » : atterrir accroupi ou finir un vault
/// allongé faisait donc clignoter la posture debout le temps d'une transition. C'est un défaut
/// purement visuel, donc silencieux — aucune erreur, aucun test existant ne l'aurait vu, et à
/// 0,15 s de transition il se remarque à peine en jouant tout en étant bien là.
///
/// Ces tests ne touchent PAS au réseau : ils vérifient le graphe seul, avec le vrai controller.
/// L'aiguillage entre valeur locale et valeur réseautée, lui, vit dans PlayerLocomotion
/// (DisplayAirborne / DisplayVaulting) et demanderait une session Netcode pour être testé.
/// </summary>
public class AnimatorStateTests
{
    private const string ModelPath = "Assets/_ProjectArt/Mesh/Personnages/SwattSolider_T_Pose.fbx";
    private const string ControllerPath = "Assets/_ProjectArt/PlayerAnimator.controller";

    private GameObject instance;
    private Animator animator;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private IEnumerator Setup()
    {
        var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Assert.IsNotNull(model, "Modèle introuvable : " + ModelPath);
        var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, "Controller introuvable : " + ControllerPath);

        instance = Object.Instantiate(model);
        animator = instance.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Pas d'Animator sur le modèle.");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        yield return null;
    }

    /// <summary>Laisse le temps aux transitions de se terminer.
    ///
    /// ⚠️ Attendre en TEMPS et non en FRAMES. Atteindre EnLAir depuis Debout enchaîne DEUX
    /// transitions — Debout vers Accroupi (0,20 s) puis Accroupi vers EnLAir (0,15 s), une
    /// transition n'étant pas interruptible par défaut. Or en PlayMode les frames défilent bien
    /// plus vite qu'à 60 Hz : une première version attendait 40 frames, ce qui ne faisait même
    /// pas 0,35 s, et les trois tests échouaient dès leur première assertion.</summary>
    private static IEnumerator Settle()
    {
        yield return new WaitForSeconds(0.6f);
    }

    private void AssertState(string attendu)
    {
        var info = animator.GetCurrentAnimatorStateInfo(0);
        Assert.IsTrue(info.IsName(attendu),
            "État attendu « " + attendu + " », mais ce n'est pas celui joué.");
    }

    /// <summary>
    /// Surveille chaque frame et échoue si le graphe passe par un état INTERDIT, même
    /// fugitivement.
    ///
    /// 🚨 C'est ce qu'il faut tester ici, et une première version le ratait. Le défaut est un
    /// passage TRANSITOIRE de 0,15 s par la posture debout, pas un état final faux : asserter
    /// l'état final après 0,6 s d'attente laissait l'enchaînement EnLAir → Debout → Accroupi se
    /// terminer, et le test passait aussi bien avec le défaut que sans. Validé par mutation, il
    /// était vert dans les deux cas — donc vide.
    ///
    /// On regarde l'état courant ET la destination de la transition en cours : pendant une
    /// transition, l'état « courant » reste la SOURCE, donc surveiller le seul état courant
    /// manquerait le passage.
    /// </summary>
    private IEnumerator AssertNePasseJamaisPar(string interdit, float duree)
    {
        float ecoule = 0f;
        while (ecoule < duree)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(interdit)
                || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(interdit)))
            {
                Assert.Fail("Le graphe est passé par « " + interdit + " » alors qu'il devait "
                            + "revenir directement à la posture réelle.");
            }
            ecoule += Time.deltaTime;
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ApresUneChute_RevientALaPostureAccroupie()
    {
        yield return Setup();

        animator.SetInteger("Stance", 1);          // accroupi
        animator.SetFloat("SpeedMult", 1f);
        animator.SetBool("Airborne", true);
        yield return Settle();
        AssertState("EnLAir");

        animator.SetBool("Airborne", false);
        yield return AssertNePasseJamaisPar("Debout", 0.5f);
        AssertState("Accroupi");
    }

    [UnityTest]
    public IEnumerator ApresUnVault_RevientALaPostureAccroupie()
    {
        yield return Setup();

        animator.SetInteger("Stance", 1);
        animator.SetFloat("SpeedMult", 1f);
        animator.SetBool("Vaulting", true);
        yield return Settle();
        AssertState("Vault");

        animator.SetBool("Vaulting", false);
        yield return AssertNePasseJamaisPar("Debout", 0.5f);
        AssertState("Accroupi");
    }

    [UnityTest]
    public IEnumerator ApresUneChute_RevientALaPostureAllongee()
    {
        yield return Setup();

        animator.SetInteger("Stance", 2);          // allongé
        animator.SetFloat("SpeedMult", 1f);
        animator.SetBool("Airborne", true);
        yield return Settle();
        AssertState("EnLAir");

        animator.SetBool("Airborne", false);
        yield return AssertNePasseJamaisPar("Debout", 0.5f);
        AssertState("Prone");
    }
}
#endif
