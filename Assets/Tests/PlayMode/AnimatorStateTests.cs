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
        Time.captureDeltaTime = 0f; // un test arrêté en plein geste ne laisse pas le pas de temps figé
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
        PlayerAnimator.SetLocomotion(animator, Vector2.zero);
        yield return Settle(ProneSettle);
        StringAssert.Contains("Prone Idle", ClipDominant(0), "Immobile allongé, les jambes ne doivent pas ramper.");

        // Garde anti-vacuité : la reptation existe bien et se déclenche en bougeant. Sans elle, un
        // arbre qui ne jouerait QUE l'attente passerait ce test. Depuis le 2026-10-09, bouger passe
        // aussi par Marche, qui sépare l'attente du déplacement.
        PlayerAnimator.SetLocomotion(animator, new Vector2(0f, -1f));
        animator.SetFloat("Marche", 1f);
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
        PlayerAnimator.SetLocomotion(animator, new Vector2(0f, 1f)); // course
        animator.SetFloat("Marche", 1f);
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

    /// <summary>
    /// Un pas de côté en VISÉE et en SNEAK joue la marche de côté du Y Bot, des deux côtés ; à
    /// l'allure de marche, la course de côté. La marche avance de 1,79 m/s : à pleine marche
    /// (5 m/s), elle glisserait ou serait accélérée trois fois. (Jusqu'au 2026-10-11, l'anneau lent
    /// jouait un pas chassé accroupi, faute de marche.)
    ///
    /// À gauche, c'est un clip à part, « Walk Left Miroir », mis en miroir À L'IMPORT : un enfant
    /// mis en miroir dans l'arbre faussait le déplacement de racine dès qu'il se mêlait à un autre
    /// clip (voir PlayerAnimator.DriveGait et CLAUDE.md).
    /// </summary>
    [UnityTest]
    public IEnumerator PasDeCoteLent_JoueLaMarche_EtLaMarcheGardeLaCourse()
    {
        yield return Setup();
        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("Marche", 1f);

        // En visée (allure 0,55) : la marche de côté.
        PlayerAnimator.SetLocomotion(animator, new Vector2(0.55f, 0f));
        yield return Settle(0.3f);
        Assert.AreEqual("Walk Right", ClipDominant(0), "Pas de côté en visée, à droite.");

        PlayerAnimator.SetLocomotion(animator, new Vector2(-0.55f, 0f));
        yield return Settle(0.3f);
        Assert.AreEqual("Walk Left Miroir", ClipDominant(0), "Pas de côté en visée, à gauche (le clip miroir importé).");

        // En sneak debout (allure 0,4) : la marche de côté aussi.
        PlayerAnimator.SetLocomotion(animator, new Vector2(0.4f, 0f));
        yield return Settle(0.3f);
        Assert.AreEqual("Walk Right", ClipDominant(0), "Pas de côté en sneak, à droite.");

        PlayerAnimator.SetLocomotion(animator, new Vector2(-0.4f, 0f));
        yield return Settle(0.3f);
        Assert.AreEqual("Walk Left Miroir", ClipDominant(0), "Pas de côté en sneak, à gauche.");

        PlayerAnimator.SetLocomotion(animator, new Vector2(1f, 0f));
        yield return Settle(0.3f);
        Assert.AreEqual("Run Right", ClipDominant(0), "À l'allure de marche, la course de côté reste.");
    }

    /// <summary>
    /// Recharger joue le geste sur le haut du corps, debout comme accroupi (chacun le sien depuis
    /// le 2026-10-10), puis rend l'arme (2026-10-08). Un état qui ne sortirait jamais laisserait le robot recharger indéfiniment ; un
    /// arbre aux seuils automatiques jouerait la pose allongée accroupi (voir le piège des seuils).
    /// </summary>
    [UnityTest]
    public IEnumerator Recharger_JoueLeGesteSurLeHautDuCorps_PuisRendLArme()
    {
        yield return Setup();
        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("ReloadSpeed", 1f);

        animator.SetBool("Reloading", true);
        yield return Settle(0.4f);
        AssertStateSurCouche(1, "Recharge");
        Assert.AreEqual("Reloading", ClipDominant(1));

        // Accroupi, son propre geste depuis le 2026-10-10 : buste penché en squat.
        animator.SetInteger("Stance", 1);
        animator.SetFloat("StanceF", 1f);
        yield return Settle(0.4f);
        Assert.AreEqual("Crouch Reload", ClipDominant(1), "Accroupi, le haut du corps doit recharger avec le geste accroupi.");

        animator.SetBool("Reloading", false);
        yield return Settle(0.5f);
        AssertStateSurCouche(1, "Arme");
    }

    /// <summary>
    /// Allongé, le rechargement (2026-10-10) : le haut du corps joue le geste couché de Mixamo.
    /// Joué sur le corps entier, ce clip roule le robot sur le flanc, tête 33 cm sur le côté et
    /// 12 cm plus haut, hors de sa tête touchable, qui ne bouge pas. Sur le seul haut du corps, les
    /// hanches restent à plat : la main gauche va chercher le chargeur, et la tête, coudes relevés,
    /// reste dans sa sphère touchable (13 cm de rayon allongé).
    /// </summary>
    [UnityTest]
    public IEnumerator Allonge_RechargerChangeLeChargeur_EtLaTeteResteDansSaZone()
    {
        yield return Setup();
        animator.SetFloat("SpeedMult", 1f);
        animator.SetFloat("ReloadSpeed", 2.1f / 2.2f); // le geste utile dans les 2,2 s du rechargement
        animator.SetInteger("Stance", 2);
        animator.SetFloat("StanceF", 2f);
        yield return Settle(ProneSettle);
        AssertState("Prone");

        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Vector3 idleHead = instance.transform.InverseTransformPoint(head.position);
        float idleHands = Vector3.Distance(left.position, right.position);

        animator.SetBool("Reloading", true);
        float maxHead = 0f, maxHands = 0f;
        for (float t = 0f; t < 2.2f; t += Time.deltaTime)
        {
            yield return null;
            maxHead = Mathf.Max(maxHead, Vector3.Distance(instance.transform.InverseTransformPoint(head.position), idleHead));
            maxHands = Mathf.Max(maxHands, Vector3.Distance(left.position, right.position));
        }
        Assert.AreEqual("Prone Reloading", ClipDominant(1), "Allongé, le haut du corps doit recharger avec le geste couché.");
        Debug.Log($"[RECHARGE-ALLONGE] tête à {maxHead * 100f:F1} cm au plus de sa place, mains écartées jusqu'à {maxHands * 100f:F0} cm (au repos : {idleHands * 100f:F0})");
        Assert.Greater(maxHands, idleHands + 0.25f, "La main gauche doit quitter le fusil pour changer le chargeur.");
        // Mesuré le 2026-10-10 : 11 cm au plus (coudes relevés), contre 35 cm sur le corps entier.
        Assert.Less(maxHead, 0.13f, $"La tête visible s'écarte de {maxHead * 100f:F1} cm : elle sort de la tête touchable (13 cm de rayon).");
    }

    /// <summary>
    /// Le taser frappe de la main GAUCHE (2026-10-08) : l'estocade de Mixamo est donnée du bras
    /// droit, celui qui tient le fusil. Elle est donc jouée en miroir, sur une couche qui ne pilote
    /// que le buste et le bras gauche. Sans le miroir, c'est le fusil qui partirait en avant — et
    /// avec ce masque, le bras gauche ferait le geste de garde, en arrière.
    ///
    /// Mesure au contact : la main gauche doit être loin devant, et devant la droite.
    /// </summary>
    [UnityTest]
    public IEnumerator LeTaser_FrappeDeLaMainGauche_PuisRevientAuRepos()
    {
        yield return Setup();
        animator.SetFloat("SpeedMult", 1f);
        // Les valeurs du jeu : 0,72 s de geste utile (Taser Punch) en 0,6 s (strikeDuration).
        animator.SetFloat("TaserSpeed", 0.72f / 0.6f);
        animator.SetLayerWeight(2, 1f); // en jeu, PlayerAnimator pilote ce poids pendant le geste
        yield return Settle(0.3f);

        animator.SetTrigger("Taser");
        yield return new WaitForSeconds(0.18f); // contact : TaserController.contactDelay
        AssertStateSurCouche(2, "Coup");
        Assert.AreEqual("Taser Punch", ClipDominant(2), "Le coup de taser joue le Taser Punch (2026-10-11).");

        Transform root = animator.transform;
        Vector3 left = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Vector3 right = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
        Debug.Log($"[TASER] main gauche z={left.z:F2} m, main droite z={right.z:F2} m");
        Assert.Greater(left.z, 0.5f, "Au contact, la main gauche n'est pas tendue vers l'avant.");
        Assert.Greater(left.z, right.z + 0.15f, "Au contact, c'est la main droite (celle du fusil) qui frappe.");

        yield return Settle(0.6f);
        AssertStateSurCouche(2, "Repos");
    }

    /// <summary>
    /// Le spectateur, à mains nues (clips Mixamo choisis le 2026-10-13). Debout, il joue son propre arbre
    /// (paramètre MainsNues) : l'attente, puis la course. Ses gestes passent par leur couche : le coup de
    /// poing du poing GAUCHE (le direct du taser), tendu au contact ; le lancer du bras DROIT, armé
    /// derrière l'épaule au bout de l'élan, devant au lâcher ; ramasser, la main droite vers le sol.
    /// Étourdi, il titube.
    /// </summary>
    [UnityTest]
    public IEnumerator UnSpectateur_JoueSesGestesAMainsNues()
    {
        yield return Setup();
        animator.SetFloat("MainsNues", 1f);
        animator.SetFloat("LocoSpeed", 1f);
        animator.SetLayerWeight(1, 0f); // debout, le haut du corps du duelliste se tait (PlayerAnimator)
        yield return Settle(0.3f);
        Assert.AreEqual("Idle", ClipDominant(0), "Immobile, le spectateur joue l'attente à mains nues.");

        animator.SetFloat("Marche", 1f);
        animator.SetFloat("Allure", 1f);
        animator.SetFloat("MoveY", 1f);
        yield return Settle(0.3f);
        Assert.AreEqual("Standard Run", ClipDominant(0), "À l'allure nominale, il court à mains nues.");
        animator.SetFloat("Marche", 0f);
        animator.SetFloat("Allure", 0f);
        animator.SetFloat("MoveY", 0f);

        Transform root = animator.transform;
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        int gestes = PlayerAnimator.SpectatorGesturesLayer;
        animator.SetLayerWeight(gestes, 1f); // en jeu, PlayerAnimator pilote ce poids pendant le geste

        // Le coup de poing : tendu au contact, 0,15 s après l'appui (les valeurs du jeu). Le pas de temps
        // est figé le temps du geste : la mesure tombe au même instant de l'animation quelle que soit la
        // charge de la machine. Au pas d'une image lente, le poing se mesurait encore en route (0,499 m,
        // le 2026-10-27, deux joueurs virtuels ouverts).
        Time.captureDeltaTime = 1f / 120f;
        yield return null;
        animator.SetFloat("GesteSpeed", 0.22f / 0.15f);
        animator.SetTrigger("Poing");
        yield return new WaitForSeconds(0.15f);
        Time.captureDeltaTime = 0f;
        AssertStateSurCouche(gestes, "Poing");
        Vector3 left = root.InverseTransformPoint(leftHand.position);
        Vector3 right = root.InverseTransformPoint(rightHand.position);
        Debug.Log($"[SPECTATEUR] poing : main gauche z={left.z:F2} m, main droite z={right.z:F2} m");
        Assert.Greater(left.z, 0.5f, "Au contact, le poing gauche n'est pas tendu.");
        Assert.Greater(left.z, right.z + 0.15f, "Au contact, c'est la main droite qui frappe.");
        yield return Settle(0.6f);
        AssertStateSurCouche(gestes, "Repos");

        // L'élan : au bout, la main droite est armée derrière l'épaule.
        animator.SetBool("Elan", true);
        animator.SetFloat("ElanLancer", 1f);
        yield return Settle(0.3f);
        AssertStateSurCouche(gestes, "LancerElan");
        right = root.InverseTransformPoint(rightHand.position);
        Debug.Log($"[SPECTATEUR] élan : main droite y={right.y:F2} z={right.z:F2}");
        Assert.Less(right.z, -0.1f, "Au bout de l'élan, la main droite n'est pas derrière le corps.");
        Assert.Greater(right.y, 1.3f, "Au bout de l'élan, la main droite n'est pas à hauteur d'épaule.");

        // Le lâcher : la main droite part devant.
        animator.SetBool("Elan", false);
        animator.SetTrigger("Lancer");
        yield return new WaitForSeconds(0.2f);
        AssertStateSurCouche(gestes, "Lancer");
        right = root.InverseTransformPoint(rightHand.position);
        Debug.Log($"[SPECTATEUR] lâcher : main droite z={right.z:F2}");
        Assert.Greater(right.z, 0.3f, "Au lâcher, la main droite ne part pas devant.");
        yield return Settle(0.8f);
        AssertStateSurCouche(gestes, "Repos");

        // Ramasser : tout le corps se baisse (couche CorpsEntier), la main droite descend vers le sol.
        animator.SetLayerWeight(4, 1f); // en jeu, PlayerAnimator pilote ce poids le temps du geste
        animator.SetTrigger("Ramasser");
        float lowest = float.MaxValue;
        float until = Time.time + 0.7f;
        while (Time.time < until)
        {
            yield return null;
            lowest = Mathf.Min(lowest, root.InverseTransformPoint(rightHand.position).y);
        }
        Debug.Log($"[SPECTATEUR] ramasser : main droite au plus bas à {lowest:F2} m");
        Assert.Less(lowest, 0.6f, "Ramasser : la main droite ne descend pas vers le sol.");

        // Étourdi : la couche de tout le corps, revenue à son état par défaut, titube.
        yield return Settle(0.5f);
        AssertStateSurCouche(4, "Etourdi");
        Assert.AreEqual("Dizzy Idle", ClipDominant(4), "Étourdi, le spectateur titube (Dizzy Idle).");
    }

    /// <summary>
    /// L'enchaînement et la garde du spectateur (2026-10-27). Le direct du DROIT (le geste du gauche,
    /// sans miroir) est tendu au contact ; la garde lève les deux poings, FERMÉS, devant le visage (les
    /// bras de « Standing Block Idle », les doigts du direct : le clip de Mixamo a les mains ouvertes) ;
    /// un coup paré la secoue, puis elle revient ; baissée, la couche retourne au repos.
    /// </summary>
    [UnityTest]
    public IEnumerator UnSpectateur_FrappeDuDroit_EtLeveSaGardePoingsFermes()
    {
        yield return Setup();
        // À mains nues, comme PlayerAnimator le montre : le gant du taser replie la main gauche, et le
        // poing gauche se mesurerait fermé quoi que joue la garde.
        HeldWeapons.WearTaserGlove(animator, false);
        animator.SetFloat("MainsNues", 1f);
        animator.SetLayerWeight(1, 0f);
        int gestes = PlayerAnimator.SpectatorGesturesLayer;
        animator.SetLayerWeight(gestes, 1f); // en jeu, PlayerAnimator pilote ce poids
        yield return Settle(0.3f);

        Transform root = animator.transform;
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        // Le direct du droit, tendu au contact (0,15 s après l'appui, comme le gauche), mesuré au même pas
        // de temps figé.
        Time.captureDeltaTime = 1f / 120f;
        yield return null;
        animator.SetFloat("GesteSpeed", 0.22f / 0.15f);
        animator.SetTrigger("PoingDroit");
        yield return new WaitForSeconds(0.15f);
        Time.captureDeltaTime = 0f;
        AssertStateSurCouche(gestes, "PoingDroit");
        Vector3 left = root.InverseTransformPoint(leftHand.position);
        Vector3 right = root.InverseTransformPoint(rightHand.position);
        Debug.Log($"[SPECTATEUR] poing droit : main droite x={right.x:F2} z={right.z:F2} m, main gauche z={left.z:F2} m");
        Assert.Greater(right.z, 0.5f, "Au contact, le poing droit n'est pas tendu.");
        Assert.Greater(right.z, left.z + 0.15f, "Au contact, c'est la main gauche qui frappe : le direct du droit est en miroir.");
        yield return Settle(0.6f);
        AssertStateSurCouche(gestes, "Repos");

        // La garde : les deux poings levés devant le visage, fermés.
        animator.SetBool("Garde", true);
        yield return Settle(0.5f);
        AssertStateSurCouche(gestes, "Garde");
        left = root.InverseTransformPoint(leftHand.position);
        right = root.InverseTransformPoint(rightHand.position);
        float leftFist = Vector3.Distance(leftHand.position, animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal).position);
        float rightFist = Vector3.Distance(rightHand.position, animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal).position);
        Debug.Log($"[SPECTATEUR] garde : gauche ({left.x:F2} {left.y:F2} {left.z:F2}), droite ({right.x:F2} {right.y:F2} {right.z:F2}), poings {leftFist:F3} / {rightFist:F3} m");
        foreach (Vector3 hand in new[] { left, right })
        {
            Assert.Greater(hand.z, 0.3f, "En garde, les poings ne sont pas devant le corps.");
            Assert.Greater(hand.y, 1.25f, "En garde, les poings ne sont pas levés à hauteur du visage.");
            Assert.Less(Mathf.Abs(hand.x), 0.3f, "En garde, les poings ne sont pas devant soi.");
        }
        // Main ouverte, 19 cm du poignet au bout du majeur ; poing serré, 10 (mesuré le 2026-10-27).
        Assert.Greater(Mathf.Min(leftFist, rightFist), 0.05f, "Une main est repliée sur elle-même (le gant du taser) : la mesure du poing ne dit rien.");
        Assert.Less(leftFist, 0.14f, "En garde, la main gauche est ouverte : les doigts du clip de garde n'ont pas été remplacés.");
        Assert.Less(rightFist, 0.14f, "En garde, la main droite est ouverte.");

        // Un coup paré : les bras encaissent, puis la garde revient.
        animator.SetTrigger("GardeChoc");
        yield return new WaitForSeconds(0.15f);
        AssertStateSurCouche(gestes, "GardeChoc");
        yield return Settle(1.2f);
        AssertStateSurCouche(gestes, "Garde");

        // Baissée : la couche retourne au repos.
        animator.SetBool("Garde", false);
        yield return Settle(0.4f);
        AssertStateSurCouche(gestes, "Repos");
    }
}
#endif
