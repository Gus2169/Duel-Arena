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
    private GameObject instance;
    private Animator animator;

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private IEnumerator Setup()
    {
        // Le personnage du prefab, pas un chemin en dur : voir TestCharacter.
        instance = TestCharacter.InstantiateModel(out animator);
        yield return null;
    }

    /// <summary>Laisse le temps aux transitions de se terminer.
    ///
    /// ⚠️ Attendre en TEMPS et non en FRAMES. Atteindre EnLAir depuis Debout enchaîne DEUX
    /// transitions — Debout vers Accroupi (0,20 s) puis Accroupi vers EnLAir (0,15 s), une
    /// transition n'étant pas interruptible par défaut. Or en PlayMode les frames défilent bien
    /// plus vite qu'à 60 Hz : une première version attendait 40 frames, ce qui ne faisait même
    /// pas 0,35 s, et les trois tests échouaient dès leur première assertion.</summary>
    private static IEnumerator Settle(float seconds = 0.6f)
    {
        yield return new WaitForSeconds(seconds);
    }

    /// <summary>Se coucher ou se relever passe par un clip de transition d'environ une seconde
    /// (2026-10-06) : il faut l'attendre avant de regarder la pose d'arrivée.</summary>
    private const float ProneSettle = 1.4f;

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

    private void AssertStateSurCouche(int couche, string attendu)
    {
        var info = animator.GetCurrentAnimatorStateInfo(couche);
        Assert.IsTrue(info.IsName(attendu),
            "Couche " + couche + " : état attendu « " + attendu + " », mais ce n'est pas celui joué.");
    }

    /// <summary>Renvoie le nom du clip DOMINANT sur une couche, celui dont le poids de mélange
    /// est le plus fort.</summary>
    private string ClipDominant(int couche)
    {
        var infos = animator.GetCurrentAnimatorClipInfo(couche);
        Assert.Greater(infos.Length, 0, "Aucun clip joué sur la couche " + couche + ".");

        string meilleur = null;
        float poidsMax = -1f;
        foreach (var info in infos)
        {
            if (info.weight > poidsMax) { poidsMax = info.weight; meilleur = info.clip.name; }
        }
        return meilleur;
    }

    /// <summary>
    /// Le haut du corps doit jouer le clip de LA POSTURE COURANTE.
    ///
    /// 🚨 CE TEST EXISTE À CAUSE D'UN PIÈGE UNITY COÛTEUX : BlendTree.useAutomaticThresholds vaut
    /// TRUE par défaut, et RÉÉCRIT les seuils passés à AddChild en les répartissant uniformément.
    /// Les seuils 0/1/2 devenaient 0/0,5/1, si bien que StanceF=1 — accroupi — désignait le
    /// TROISIÈME enfant, celui du prone. Un buste allongé greffé sur des hanches accroupies donnait
    /// un personnage bras au ciel, et le tir accroupi jouait l'animation de tir couché.
    ///
    /// Rien ne le signale : ni le compilateur, ni la console, ni les tests d'états, qui ne
    /// regardent que le nom de l'état et pas le clip réellement joué.
    /// </summary>
    [UnityTest]
    public IEnumerator LeHautDuCorps_JoueLeClipDeLaPostureCourante()
    {
        yield return Setup();

        animator.SetFloat("SpeedMult", 1f);

        animator.SetInteger("Stance", 0);
        animator.SetFloat("StanceF", 0f);
        yield return Settle();
        StringAssert.Contains("Rifle Idle", ClipDominant(1));

        animator.SetInteger("Stance", 1);
        animator.SetFloat("StanceF", 1f);
        yield return Settle();
        StringAssert.Contains("Crouching", ClipDominant(1));

        animator.SetInteger("Stance", 2);
        animator.SetFloat("StanceF", 2f);
        yield return Settle(ProneSettle);
        StringAssert.Contains("Prone", ClipDominant(1));
    }

    /// <summary>
    /// Se coucher passe par le clip genou → allongé, puis finit dans la pose allongée ; se relever
    /// passe par le clip inverse, puis finit debout (2026-10-06).
    ///
    /// Avant, un simple fondu de 0,2 s faisait basculer le corps d'une posture à l'autre : « trop
    /// instantané ». Ce test garde le passage par le clip — un fondu direct remis par erreur
    /// sauterait l'état de transition et le ferait échouer — ET l'arrivée, pour qu'un état de
    /// transition qui ne sortirait jamais ne passe pas pour un succès.
    /// </summary>
    [UnityTest]
    public IEnumerator SeCoucherEtSeRelever_PassentParLeurClipDeTransition()
    {
        yield return Setup();
        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("PostureSpeed", 2.4f);
        yield return Settle();

        animator.SetInteger("Stance", 2);
        yield return AssertPassePar("GenouVersAllonge", ProneSettle);
        AssertState("Prone");

        animator.SetInteger("Stance", 0);
        yield return AssertPassePar("AllongeVersGenou", ProneSettle);
        AssertState("Debout");
    }

    /// <summary>Attend `duree` secondes en exigeant que le graphe traverse l'état demandé, comme
    /// état courant ou comme destination d'une transition.</summary>
    private IEnumerator AssertPassePar(string etat, float duree)
    {
        bool vu = false;
        float ecoule = 0f;
        while (ecoule < duree)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(etat)
                || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(etat)))
            {
                vu = true;
            }
            ecoule += Time.deltaTime;
            yield return null;
        }
        Assert.IsTrue(vu, "Le graphe n'est jamais passé par « " + etat + " » : la transition de posture a été sautée.");
    }

    /// <summary>
    /// Allongé et immobile, les jambes jouent la pose d'ATTENTE, pas la reptation.
    ///
    /// Même piège que ci-dessus, découvert le 2026-10-05 sur la couche de déplacement : l'arbre
    /// allongé gardait ses seuils automatiques (0 / 0,5 / 1 au lieu de -1 / 0 / 1). À MoveY = 0, il
    /// jouait donc la reptation arrière à plein poids : un joueur immobile rampait sur place, et
    /// son corps tournait en diagonale au fil du clip — à 35° de sa surface touchable.
    /// </summary>
    [UnityTest]
    public IEnumerator Allonge_Immobile_JoueLaPoseDAttente_EtRampeQuandIlBouge()
    {
        yield return Setup();

        animator.SetFloat("SpeedMult", 1f);
        animator.SetInteger("Stance", 2);
        animator.SetFloat("StanceF", 2f);
        animator.SetFloat("MoveX", 0f);
        animator.SetFloat("MoveY", 0f);
        yield return Settle(ProneSettle);
        StringAssert.Contains("Prone Idle", ClipDominant(0), "Immobile allongé, les jambes ne doivent pas ramper.");

        // Garde anti-vacuité : la reptation existe bien et se déclenche en bougeant. Sans elle, un
        // arbre qui ne jouerait QUE l'attente passerait ce test.
        animator.SetFloat("MoveY", -1f);
        yield return Settle();
        StringAssert.Contains("Prone Position", ClipDominant(0), "En reculant allongé, les jambes doivent ramper.");
    }

    [UnityTest]
    public IEnumerator Viser_BasculeLeHautDuCorpsEnPositionDeVisee()
    {
        yield return Setup();

        animator.SetInteger("Stance", 0);
        animator.SetFloat("SpeedMult", 1f);
        yield return Settle();
        AssertStateSurCouche(1, "Arme");

        animator.SetBool("Aiming", true);
        yield return Settle();
        AssertStateSurCouche(1, "Vise");

        animator.SetBool("Aiming", false);
        yield return Settle();
        AssertStateSurCouche(1, "Arme");
    }

    [UnityTest]
    public IEnumerator UnTir_DeclencheLeGesteSurLeHautDuCorps()
    {
        yield return Setup();

        animator.SetInteger("Stance", 0);
        animator.SetFloat("SpeedMult", 1f);
        yield return Settle();

        animator.SetTrigger("Fire");

        // Court : le geste de tir doit partir TOUT DE SUITE. Attendre Settle() le laisserait
        // se terminer et revenir à Arme, ce qui ne prouverait rien — c'est le piège du test vide
        // déjà payé trois fois sur ce projet.
        yield return new WaitForSeconds(0.1f);
        AssertStateSurCouche(1, "Tir");
    }

    /// <summary>
    /// Le masque doit laisser les JAMBES à la couche de déplacement.
    ///
    /// C'est toute la raison d'être de cette couche : pouvoir tirer en strafant. Si le masque
    /// incluait les jambes par erreur, la visée les figerait et le personnage glisserait, jambes
    /// immobiles — un défaut qui se voit mal en jeu et que rien d'autre n'attraperait.
    ///
    /// On vérifie donc que la cuisse continue de BOUGER pendant une course visée.
    /// </summary>
    [UnityTest]
    public IEnumerator LeMasque_LaisseLesJambesALaCoucheDeDeplacement()
    {
        yield return Setup();

        animator.SetInteger("Stance", 0);
        animator.SetFloat("MoveY", 1f);        // course
        animator.SetFloat("SpeedMult", 1f);
        animator.SetBool("Aiming", true);      // et visée en même temps
        yield return Settle();

        Transform cuisse = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Assert.IsNotNull(cuisse, "Os LeftUpperLeg introuvable.");

        Quaternion reference = cuisse.localRotation;
        float amplitudeMax = 0f;

        float ecoule = 0f;
        while (ecoule < 0.6f)
        {
            ecoule += Time.deltaTime;
            yield return null;
            float ecart = Quaternion.Angle(reference, cuisse.localRotation);
            if (ecart > amplitudeMax) amplitudeMax = ecart;
        }

        Assert.Greater(amplitudeMax, 10f,
            "La cuisse n'a bougé que de " + amplitudeMax.ToString("F1") + "° pendant une course "
            + "visée : le haut du corps a figé les jambes, donc le masque les inclut à tort.");
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
