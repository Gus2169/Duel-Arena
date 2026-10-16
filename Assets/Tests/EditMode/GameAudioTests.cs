using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Phase = RoundManager.Phase;

/// <summary>
/// Les sons qui manquaient (2026-10-13) : retours de touche, balle qui siffle, franchir et retomber,
/// décompte, reconstruction dans le noir, musique de victoire, interface, dégâts reçus, épave.
/// Ce qu'on ne peut pas écouter ici se vérifie par les règles qui décident QUAND un son part, et par
/// les données : un son non câblé ne fait aucune erreur, il manque, simplement.
/// </summary>
public class GameAudioTests
{
    private const string GameSoundsPath = "Assets/_ProjectArt/Audio/GameSounds.asset";
    private const string PlayerSoundsPath = "Assets/_ProjectArt/Audio/PlayerSounds.asset";
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    // ------------------------------------------------------------------ Retours de touche

    [Test]
    public void UneTouche_SeClasseParGravite_LeCoupQuiTueLEmporte()
    {
        Assert.AreEqual(WeaponController.HitKind.Body, WeaponController.ClassifyHit(head: false, killed: false));
        Assert.AreEqual(WeaponController.HitKind.Head, WeaponController.ClassifyHit(head: true, killed: false));
        Assert.AreEqual(WeaponController.HitKind.Kill, WeaponController.ClassifyHit(head: true, killed: true),
            "Une tête qui tue s'entend comme le coup qui tue : c'est la nouvelle qui compte.");
        Assert.AreEqual(WeaponController.HitKind.Kill, WeaponController.ClassifyHit(head: false, killed: true));
    }

    [Test]
    public void DegatsRecus_SonnentSelonLeCoup_JamaisMuetsNiSatures()
    {
        float plomb = PlayerFeedbackAudio.DamageLevel(5f, 40f);
        float balle = PlayerFeedbackAudio.DamageLevel(17f, 40f);
        float sniper = PlayerFeedbackAudio.DamageLevel(100f, 40f);
        Assert.Greater(plomb, 0.49f, "Un plomb s'entend : jamais muet.");
        Assert.Less(plomb, balle, "Un plomb sonne moins fort qu'une balle de fusil.");
        Assert.Less(balle, sniper);
        Assert.AreEqual(1f, sniper, 1e-6f, "Au-delà d'un coup plein, le volume plafonne.");
        Assert.AreEqual(0f, PlayerFeedbackAudio.DamageLevel(0f, 40f), "Aucun dégât, aucun son (le soin de début de manche).");
        Assert.AreEqual(0f, PlayerFeedbackAudio.DamageLevel(float.NaN, 40f), "NaN : aucun son, pas un volume NaN.");
        Assert.AreEqual(1f, PlayerFeedbackAudio.DamageLevel(5f, 0f), 1e-6f, "Un réglage nul ne divise pas par zéro.");
    }

    // ------------------------------------------------------------------ La balle qui siffle

    [Test]
    public void UneBalleQuiFroleLOreille_Siffle_LaOuElleEnPasseAuPlusPres()
    {
        Assert.IsTrue(WeaponController.TryFlybyPoint(Vector3.zero, new Vector3(0f, 0f, 50f), new Vector3(1f, 0f, 20f),
            WeaponController.FlybyRadius, out Vector3 point));
        Assert.AreEqual(20f, point.z, 1e-4f, "Le sifflement part du point du trajet le plus proche de l'oreille.");
        Assert.AreEqual(0f, point.x, 1e-4f);
    }

    [Test]
    public void UneBalleQuiPasseLoin_OuSArreteAvant_OuVientDeDerriere_NeSifflePas()
    {
        Vector3 end = new Vector3(0f, 0f, 50f);
        float r = WeaponController.FlybyRadius;
        Assert.IsFalse(WeaponController.TryFlybyPoint(Vector3.zero, end, new Vector3(3f, 0f, 20f), r, out _), "Trop loin (3 m).");
        Assert.IsFalse(WeaponController.TryFlybyPoint(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(1f, 0f, 20f), r, out _),
            "La balle s'est arrêtée dans un mur avant d'arriver à sa hauteur.");
        Assert.IsFalse(WeaponController.TryFlybyPoint(Vector3.zero, end, new Vector3(1f, 0f, -5f), r, out _),
            "Derrière le tireur : la balle ne passe pas par là.");
        Assert.IsFalse(WeaponController.TryFlybyPoint(Vector3.zero, Vector3.zero, new Vector3(0.5f, 0f, 0f), r, out _),
            "Un tir de longueur nulle ne siffle pas (pas de division par zéro).");
    }

    [Test]
    public void UneBalleQuiArrivePresDeLOreille_NeSifflePas_CEstUnImpact()
    {
        // Elle finit à 60 cm de lui (sur lui, ou sur le mur derrière) : l'impact s'entend, pas un sifflement.
        Assert.IsFalse(WeaponController.TryFlybyPoint(Vector3.zero, new Vector3(0f, 0f, 20.5f), new Vector3(0.3f, 0f, 20f),
            WeaponController.FlybyRadius, out _));
    }

    [Test]
    public void UneToucheAuxJambesDePres_NeSifflePasChezLaVictime()
    {
        // Tireur à 5 m, balle dans le tibia (0,5 m de haut, sur l'avant du corps), oreille de la victime à 1,65 m.
        var origin = new Vector3(0f, 1.65f, 0f);
        var ends = new[] { new Vector3(0f, 0.5f, 4.85f) };
        var ear = new Vector3(0f, 1.65f, 5f);
        Assert.IsTrue(WeaponController.TryPickFlyby(origin, ends, 0, ear, WeaponController.FlybyRadius, out _),
            "Garde : manquée, cette balle frôlerait l'oreille (elle passe à 1,15 m, sans finir à moins d'un mètre).");
        Assert.IsFalse(WeaponController.TryPickFlyby(origin, ends, 1, ear, WeaponController.FlybyRadius, out _),
            "Arrêtée par le robot : on l'entend en impact, pas en sifflement.");
    }

    [Test]
    public void UnTirDePlombs_SiffleUneFois_LaOuIlPasseAuPlusPres()
    {
        var origin = Vector3.zero;
        var ear = new Vector3(0f, 0f, 20f);
        var ends = new[]
        {
            new Vector3(2f, 0f, 40f),  // passe à 1 m
            new Vector3(0.6f, 0f, 40f), // passe à 0,3 m : le plus proche, mais il a touché un robot
            new Vector3(1f, 0f, 40f),  // passe à 0,5 m
            new Vector3(8f, 0f, 40f),  // trop loin
        };
        Assert.IsTrue(WeaponController.TryPickFlyby(origin, ends, 0b0010, ear, WeaponController.FlybyRadius, out Vector3 point));
        Assert.AreEqual(0.5f, Vector3.Distance(point, ear), 0.01f, "Le plomb libre le plus proche de l'oreille.");
        Assert.IsFalse(WeaponController.TryPickFlyby(origin, null, 0, ear, WeaponController.FlybyRadius, out _));
    }

    // ------------------------------------------------------------------ Franchir, retomber

    /// <summary>Une image du serveur : (franchissait, franchit, était au sol, est au sol, vitesse de chute).</summary>
    private static List<PlayerSoundEvent> Sons(params (bool wasVaulting, bool isVaulting, bool wasGrounded, bool isGrounded, float fall)[] images)
    {
        bool pending = false;
        var sons = new List<PlayerSoundEvent>();
        foreach (var i in images)
        {
            PlayerSoundEvent? son = PlayerLocomotion.GetVaultSound(i.wasVaulting, i.isVaulting, i.wasGrounded, i.isGrounded, i.fall, ref pending);
            if (son.HasValue) sons.Add(son.Value);
        }
        return sons;
    }

    [Test]
    public void Franchir_SEntendAuDepart_EtLaReceptionUneSeuleFoisAuContact()
    {
        // La séquence du banc (2026-10-13) : à la fin de l'arc, le contrôleur (coupé pendant le
        // franchissement) se dit encore au sol ; le robot retombe ensuite 0,14 s, et touche le sol à 3,5 m/s.
        CollectionAssert.AreEqual(new[] { PlayerSoundEvent.Vault, PlayerSoundEvent.Land }, Sons(
            (false, false, true, true, 0f),
            (false, true, true, true, 0f),   // départ
            (true, true, true, true, 0f),    // l'arc
            (true, false, true, true, 0f),   // fin de l'arc, « au sol » pas à jour
            (false, false, true, false, 1f), // en l'air
            (false, false, false, false, 2.5f),
            (false, false, false, true, 3.5f), // contact
            (false, false, true, true, 0f)),
            "Une réception, au contact : pas une à la fin de l'arc et une autre en touchant le sol.");
    }

    [Test]
    public void Franchir_FiniPose_SEntendALImageSuivante_MemeSansVitesse()
    {
        CollectionAssert.AreEqual(new[] { PlayerSoundEvent.Vault, PlayerSoundEvent.Land }, Sons(
            (false, true, true, true, 0f),
            (true, false, true, true, 0f),
            (false, false, true, true, 0f),
            (false, false, true, true, 0f)));
    }

    [Test]
    public void Retomber_DUneChute_SEntend_UnePetiteMarcheNon()
    {
        float seuil = PlayerLocomotion.LandingSoundFallSpeed;
        CollectionAssert.AreEqual(new[] { PlayerSoundEvent.Land }, Sons((false, false, false, true, seuil + 1f)));
        CollectionAssert.IsEmpty(Sons((false, false, false, true, seuil * 0.5f)), "Descendre une marche ne fait pas le bruit d'une chute.");
        CollectionAssert.IsEmpty(Sons((false, false, true, true, seuil + 5f)), "Déjà au sol : rien.");
        CollectionAssert.IsEmpty(Sons((false, false, true, false, 0f)), "Quitter le sol ne fait pas de bruit.");
    }

    // ------------------------------------------------------------------ Décompte et reconstruction

    [Test]
    public void LeDecompte_ARondiAuChiffreAffiche()
    {
        Assert.AreEqual(3, MatchAudio.CountdownNumber(2.4f));
        Assert.AreEqual(2, MatchAudio.CountdownNumber(2f));
        Assert.AreEqual(1, MatchAudio.CountdownNumber(0.01f));
        Assert.AreEqual(1, MatchAudio.CountdownNumber(0f), "Jamais « 0 » : la manche part.");
        Assert.AreEqual(1, MatchAudio.CountdownNumber(-0.3f));
    }

    [Test]
    public void LeDecompte_BipeUneFoisParChiffre_MemeSiLHorlogeRecule()
    {
        int shown = int.MaxValue; // entrée dans la phase
        var bips = new List<int>();
        foreach (float remaining in new[] { 2.95f, 2.9f, 2.97f, 2.1f, 1.95f, 2.02f, 1.2f, 0.9f, 0.4f, 0f, -0.1f })
            if (MatchAudio.TryCountdownTick(ref shown, remaining)) bips.Add(shown);
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, bips);
    }

    [Test]
    public void ChezUnClientEnRetard_LeDecompteCommenceATrois_JamaisAQuatre()
    {
        // Décompte publié de 10 à 13 s ; l'horloge du serveur estimée par ce client retarde de 40 ms
        // (vu au banc le 2026-10-13 : deux bips au premier chiffre, et un « 4 » fugitif au HUD).
        int shown = int.MaxValue;
        var bips = new List<int>();
        for (int frame = 0; frame < 160; frame++)
        {
            double now = 9.96 + frame * 0.02;
            if (MatchAudio.TryCountdownTick(ref shown, RoundManager.RemainingIn(10.0, 13.0, now))) bips.Add(shown);
        }
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, bips);
        Assert.AreEqual(1.5f, RoundManager.RemainingIn(10.0, 13.0, 11.5), 1e-5f);
        Assert.AreEqual(0f, RoundManager.RemainingIn(10.0, 0.0, 11.5), "Sans fin publiée : 0.");
        Assert.AreEqual(0f, RoundManager.RemainingIn(10.0, 13.0, 14.0), "Finie : 0, jamais négatif.");
    }

    [Test]
    public void LaReconstruction_PartQuandLeNoirTombe_JamaisPendantLaManche()
    {
        const float lead = 0.9f;
        Assert.IsTrue(MatchAudio.ReconstructionDue(Phase.RoundOver, true, 0.5f, lead), "Fin de manche, dans le noir.");
        Assert.IsTrue(MatchAudio.ReconstructionDue(Phase.MatchOver, true, 0.5f, lead), "Fin de l'écran de victoire.");
        Assert.IsFalse(MatchAudio.ReconstructionDue(Phase.RoundOver, true, 2f, lead), "Trop tôt : le survivant nargue encore.");
        Assert.IsFalse(MatchAudio.ReconstructionDue(Phase.RoundOver, true, -0.1f, lead), "La phase est déjà finie.");
        Assert.IsFalse(MatchAudio.ReconstructionDue(Phase.RoundOver, false, 0.5f, lead), "Sans fin publiée, pas de noir.");
        foreach (Phase phase in new[] { Phase.Active, Phase.Starting, Phase.Preparation, Phase.WaitingForPlayers })
            Assert.IsFalse(MatchAudio.ReconstructionDue(phase, true, 0.5f, lead), phase + " : jamais.");
    }

    // ------------------------------------------------------------------ Musique de victoire

    [Test]
    public void LaMusiqueDeVictoire_EstLaMemeChezTous_EtChaqueMorceauPeutSortir()
    {
        var tracks = new[] { new MusicTrack(), new MusicTrack(), new MusicTrack() };
        Assert.AreSame(MatchAudio.PickVictoryTrack(tracks, 123.456), MatchAudio.PickVictoryTrack(tracks, 123.456),
            "Même instant de fin de match publié, même morceau.");
        var picked = new HashSet<MusicTrack>();
        foreach (double seed in new[] { 50.0, 50.25, 50.5 }) picked.Add(MatchAudio.PickVictoryTrack(tracks, seed)); // exacts en binaire
        Assert.AreEqual(3, picked.Count, "D'un match à l'autre, chaque morceau peut sortir.");
        Assert.IsNull(MatchAudio.PickVictoryTrack(System.Array.Empty<MusicTrack>(), 1.0));
        Assert.IsNull(MatchAudio.PickVictoryTrack(null, 1.0));
    }

    [Test]
    public void LaMusiqueDeVictoire_SurvitAUnInstantInvalide()
    {
        var tracks = new[] { new MusicTrack(), new MusicTrack(), new MusicTrack() };
        Assert.IsNotNull(MatchAudio.PickVictoryTrack(tracks, double.NaN));
        Assert.IsNotNull(MatchAudio.PickVictoryTrack(tracks, double.PositiveInfinity));
        Assert.IsNotNull(MatchAudio.PickVictoryTrack(tracks, -42.5));
        Assert.IsNotNull(MatchAudio.PickVictoryTrack(tracks, 1e300));
    }

    // ------------------------------------------------------------------ Interface

    [Test]
    public void LesSonsDeLInterface_SeLisentSurLesClassesUss()
    {
        Assert.AreEqual(UiSounds.Kind.None, UiSounds.KindOf(null));
        Assert.AreEqual(UiSounds.Kind.Click, UiSounds.KindOf(new Button()));
        Assert.AreEqual(UiSounds.Kind.Confirm, UiSounds.KindOf(WithClass(new Button(), "menu-button--primary")), "Héberger, Valider.");
        Assert.AreEqual(UiSounds.Kind.Back, UiSounds.KindOf(WithClass(new Button(), "back-button")));
        Assert.AreEqual(UiSounds.Kind.Back, UiSounds.KindOf(WithClass(new Button(), "menu-button--quit")), "Quitter revient en arrière.");
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new Button(), "loadout-card")), "Une arme choisie en préparation.");
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new Button(), "custom-weapon-row")), "Une arme de Personnalisation.");
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new VisualElement(), "color-swatch")));
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new Button(), "gunsmith-tab")), "Une arme de l'armurerie.");
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new Button(), "gunsmith-tile")), "Une tuile de l'armurerie.");
        Assert.AreEqual(UiSounds.Kind.Select, UiSounds.KindOf(WithClass(new Button(), "gunsmith-choice")), "Un choix équipé.");
        Assert.AreEqual(UiSounds.Kind.Click, UiSounds.KindOf(WithClass(new Button(), "gunsmith-arrow")), "Les flèches de la bande défilent.");
    }

    private static VisualElement WithClass(VisualElement element, string ussClass)
    {
        element.AddToClassList(ussClass);
        return element;
    }

    // ------------------------------------------------------------------ L'épave qui tombe

    [Test]
    public void LEpave_SonneSelonLeChoc_PasAuRepos()
    {
        Assert.AreEqual(0f, RobotCorpse.ImpactSoundLevel(0f));
        Assert.AreEqual(0f, RobotCorpse.ImpactSoundLevel(1f), "Une pièce qui glisse ou se pose ne cogne pas.");
        Assert.AreEqual(0f, RobotCorpse.ImpactSoundLevel(float.NaN));
        float faible = RobotCorpse.ImpactSoundLevel(1.5f);
        float fort = RobotCorpse.ImpactSoundLevel(5f);
        Assert.Greater(faible, 0f);
        Assert.Less(faible, fort, "Un choc plus violent sonne plus fort.");
        Assert.AreEqual(1f, RobotCorpse.ImpactSoundLevel(30f), 1e-6f, "Plafonné.");
    }

    // ------------------------------------------------------------------ Les données : tout est câblé

    [Test]
    public void LesSonsDuJeu_SontTousCables()
    {
        var sounds = UnityEditor.AssetDatabase.LoadAssetAtPath<GameSounds>(GameSoundsPath);
        Assert.IsNotNull(sounds, "Asset introuvable : " + GameSoundsPath);
        foreach (FieldInfo field in typeof(GameSounds).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(SoundSet)) AssertWired((SoundSet)field.GetValue(sounds), field.Name);
            else if (field.FieldType == typeof(MusicTrack)) AssertWired((MusicTrack)field.GetValue(sounds), field.Name);
            else if (field.FieldType == typeof(MusicTrack[]))
            {
                var tracks = (MusicTrack[])field.GetValue(sounds);
                Assert.IsNotEmpty(tracks, field.Name);
                foreach (MusicTrack track in tracks) AssertWired(track, field.Name);
            }
            else if (field.FieldType == typeof(TimedSound[]))
            {
                var steps = (TimedSound[])field.GetValue(sounds);
                Assert.IsNotEmpty(steps, field.Name);
                foreach (TimedSound step in steps) AssertWired(step.sound, field.Name);
            }
        }
        Assert.IsNotNull(sounds.ambienceLoop, "ambienceLoop");
    }

    [Test]
    public void LesArmes_OntLeursSonsDImpactDeSifflementDeVideEtDeSortie()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:WeaponData", new[] { "Assets/_ProjectArena/Weapon" });
        Assert.GreaterOrEqual(guids.Length, 4, "Garde : la panoplie est bien trouvée.");
        foreach (string guid in guids)
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            AssertWired(data.impactSounds, data.name + ".impactSounds");
            AssertWired(data.bodyImpactSounds, data.name + ".bodyImpactSounds");
            AssertWired(data.flybySounds, data.name + ".flybySounds");
            AssertWired(data.dryFireSounds, data.name + ".dryFireSounds");
            AssertWired(data.raiseSounds, data.name + ".raiseSounds");
        }
    }

    [Test]
    public void ChaqueSonDuRobot_ASonEntreeDansLaBanque()
    {
        var bank = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerSoundBank>(PlayerSoundsPath);
        Assert.IsNotNull(bank, "Asset introuvable : " + PlayerSoundsPath);
        foreach (PlayerSoundEvent type in System.Enum.GetValues(typeof(PlayerSoundEvent)))
        {
            Assert.IsTrue(bank.TryGetEntry(type, out PlayerSoundBank.Entry entry), type + " n'a pas d'entrée : il serait muet.");
            Assert.IsNotEmpty(entry.clips, type.ToString());
            foreach (AudioClip clip in entry.clips) Assert.IsNotNull(clip, type + " : un clip manque.");
        }
    }

    [Test]
    public void LePrefabJoueur_EntendSesTouchesEtSaMort()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
        foreach (Component component in new Component[] { prefab.GetComponent<PlayerFeedbackAudio>(), prefab.GetComponent<PlayerDeath>() })
        {
            Assert.IsNotNull(component, "Composant absent du prefab.");
            var so = new UnityEditor.SerializedObject(component);
            Assert.IsNotNull(so.FindProperty("sounds").objectReferenceValue, component.GetType().Name + ".sounds n'est pas câblé.");
        }
    }

    private static void AssertWired(SoundSet set, string name)
    {
        Assert.IsNotNull(set, name);
        Assert.IsFalse(set.IsEmpty, name + " n'a aucun son.");
        foreach (AudioClip clip in set.clips) Assert.IsNotNull(clip, name + " : un clip manque.");
        Assert.Greater(set.volume, 0f, name + " : volume nul.");
        Assert.LessOrEqual(set.pitchRange.x, set.pitchRange.y, name + " : plage de hauteur inversée.");
        Assert.Greater(set.pitchRange.x, 0f, name + " : hauteur nulle.");
    }

    private static void AssertWired(MusicTrack track, string name)
    {
        Assert.IsNotNull(track, name);
        Assert.IsNotNull(track.clip, name + " n'a pas de morceau.");
        Assert.Greater(track.volume, 0f, name + " : volume nul.");
        Assert.Less(track.startAt, track.clip.length - 5f, name + " : le départ tombe à la fin du morceau.");
    }
}
