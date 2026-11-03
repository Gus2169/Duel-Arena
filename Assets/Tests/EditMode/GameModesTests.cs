using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les modes de jeu (2026-11-02, GDD § 11) : le mode est un réglage de la partie (MatchRules.mode), que l'hôte
/// change au salon, et la boucle de manches le suit. Le Golden Gun : une arme à lui, hors de l'arsenal, mise en
/// main par le serveur à chaque manche ; une balle qui tue partout, rechargée après chaque tir ; plus de
/// manches (le premier à 5 ou à 7) ; pas de préparation. Le Gun Game : d'une traite, avec réapparitions ; une
/// élimination fait monter d'une marche sur l'échelle choisie au salon, le gant la finit ; un coup de gant avant
/// fait redescendre le tué ; le mort revient au point le plus éloigné que l'adversaire ne voit pas. L'Élimination
/// confirmée : d'une traite, avec réapparitions ; le robot détruit lâche son module, que son vainqueur doit ramasser
/// pour que l'élimination compte, et que sa victime revenue peut reprendre pour l'annuler. La Zone : aux points, d'une
/// traite ; seul dans la zone, on en gagne, à deux plus rien n'avance ; elle change de place toutes les minutes,
/// annoncée avant, parmi les emplacements de l'arène. Le Tag Team : deux équipes égales, un combattant de chacune ; le
/// relais fait entrer celui qui attend depuis le plus longtemps ; la première équipe à 100 points gagne.
/// </summary>
public class GameModesTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static WeaponController PrefabWeapon() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true);

    // ------------------------------------------------------------------ Le mode dans les réglages

    [Test]
    public void LeMode_EstUnReglageDeLaPartie_QuiFaitLAllerRetourDuSalon()
    {
        MatchRules golden = GameModeCatalog.RulesFor(GameModeId.GoldenGun);
        Assert.AreEqual(GameModeId.GoldenGun, golden.mode);
        Assert.AreEqual(GameModeId.GoldenGun, GameModeCatalog.ModeOf(golden));
        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(golden));
        Assert.AreEqual(GameModeId.GoldenGun, back.mode, "Le mode voyage dans les réglages du salon.");
        Assert.AreEqual(golden.roundsToWin, back.roundsToWin);
        Assert.AreEqual(GameModeId.Classic, OnlineSession.ParseRules("4;0;1;3;0").mode, "Un salon d'avant le mode (cinq champs) : le classique.");
        Assert.AreEqual(GameModeId.Classic, MatchRules.Default.mode);
    }

    [Test]
    public void UnModeQuiNeSeJouePasEncore_RedevientLeClassique()
    {
        var rules = new MatchRules { capacity = 2, mode = GameModeId.Darkness, roundsToWin = MatchRules.Bo5RoundsToWin };
        Assert.AreEqual(GameModeId.Classic, rules.Sanitized().mode);
        Assert.AreEqual(GameModeId.Classic, new MatchRules { mode = (GameModeId)250 }.Sanitized().mode, "Un mode inconnu (une autre version).");
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.Classic));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.GoldenGun));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.GunGame));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.KillConfirmed));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.Zone));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.TagTeam));
        Assert.IsTrue(GameModeCatalog.IsPlayable(GameModeId.Tournament));
        Assert.IsFalse(GameModeCatalog.IsPlayable(GameModeId.Darkness), "L'Arène dans le noir viendra après la sortie.");
    }

    [Test]
    public void LesManches_SontCellesDuMode()
    {
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, GameModeCatalog.RulesFor(GameModeId.Classic).roundsToWin);
        Assert.AreEqual(5, GameModeCatalog.RulesFor(GameModeId.GoldenGun).roundsToWin, "Le Golden Gun : le premier à 5 (GDD § 11, G4).");

        var golden = new MatchRules { mode = GameModeId.GoldenGun, roundsToWin = 7 };
        Assert.AreEqual(7, golden.Sanitized().roundsToWin, "Ou le premier à 7.");
        Assert.AreEqual(5, new MatchRules { mode = GameModeId.GoldenGun, roundsToWin = 3 }.Sanitized().roundsToWin,
            "Un BO5 du classique n'existe pas au Golden Gun : le premier à 5.");
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, new MatchRules { mode = GameModeId.Classic, roundsToWin = 7 }.Sanitized().roundsToWin,
            "Le premier à 7 n'existe pas au classique : un BO5.");
        Assert.AreEqual("premier à 7", golden.Format);
        Assert.AreEqual("BO3", new MatchRules { mode = GameModeId.Classic, roundsToWin = MatchRules.Bo3RoundsToWin }.Format);
    }

    [Test]
    public void ChangerDeMode_GardeLesJoueursEtLesReglages_SaufLesManches()
    {
        var classic = new MatchRules
        {
            capacity = 5, organization = DuelOrganization.KingOfTheHill, spectatorInterventions = true,
            randomDuelists = true, roundsToWin = MatchRules.Bo3RoundsToWin, mode = GameModeId.Classic,
        };
        MatchRules golden = GameModeCatalog.WithMode(classic, GameModeId.GoldenGun);
        Assert.AreEqual(GameModeId.GoldenGun, golden.mode);
        Assert.AreEqual(5, golden.roundsToWin, "Les manches du nouveau mode, à leur nombre habituel.");
        Assert.AreEqual(classic.capacity, golden.capacity);
        Assert.AreEqual(classic.organization, golden.organization);
        Assert.AreEqual(classic.spectatorInterventions, golden.spectatorInterventions);
        Assert.AreEqual(classic.randomDuelists, golden.randomDuelists);

        MatchRules again = GameModeCatalog.WithMode(golden, GameModeId.Classic);
        Assert.AreEqual(GameModeId.Classic, again.mode);
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, again.roundsToWin);
        Assert.AreEqual(golden, GameModeCatalog.WithMode(golden, GameModeId.Darkness), "Un mode qui ne se joue pas encore ne se choisit pas.");
    }

    // ------------------------------------------------------------------ La boucle de manches selon le mode

    [Test]
    public void LeGoldenGun_SansPreparation_ImposeSonArme()
    {
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.Classic), "Le classique : on choisit son arme avant chaque manche.");
        Assert.IsFalse(RoundManager.HasPreparation(GameModeId.GoldenGun), "Le Golden Gun : le décompte tout de suite (GDD § 11, G4).");
        Assert.AreEqual(-1, RoundManager.ImposedWeaponSlot(GameModeCatalog.RulesFor(GameModeId.Classic), 0));
        Assert.AreEqual(WeaponController.GoldenGunSlot, RoundManager.ImposedWeaponSlot(GameModeCatalog.RulesFor(GameModeId.GoldenGun), 0));
        Assert.AreEqual(WeaponController.GoldenGunSlot, RoundManager.ImposedWeaponSlot(GameModeCatalog.RulesFor(GameModeId.GoldenGun), 5),
            "La même pour tous, quelle que soit la marche.");
        Assert.IsFalse(RoundManager.HasRespawns(GameModeCatalog.RulesFor(GameModeId.GoldenGun)), "Le Golden Gun se joue en manches.");
        Assert.IsTrue(GameModeCatalog.ImposesWeapon(GameModeId.GoldenGun));
        StringAssert.DoesNotContain("arme libre", GameModeCatalog.Summary(GameModeId.GoldenGun, GameModeCatalog.RulesFor(GameModeId.GoldenGun)));
    }

    // ------------------------------------------------------------------ L'arme du Golden Gun

    [Test]
    public void LArmeDuGoldenGun_EstHorsDeLArsenal()
    {
        WeaponController weapon = PrefabWeapon();
        WeaponData golden = weapon.DataOf(WeaponController.GoldenGunSlot);
        Assert.IsNotNull(golden, "L'arme du Golden Gun, à la place qui suit l'arsenal.");
        Assert.AreEqual(WeaponController.MaxSlots, weapon.SlotCount, "L'arsenal garde ses douze armes.");
        for (int slot = 0; slot < weapon.SlotCount; slot++)
            Assert.AreNotSame(golden, weapon.DataOf(slot), "Elle n'est pas dans l'arsenal (armurerie, préparation).");
        CollectionAssert.DoesNotContain(Arsenal.DisplayOrder(weapon.Loadout), WeaponController.GoldenGunSlot);
        Assert.AreSame(golden, weapon.AllWeapons[WeaponController.GoldenGunSlot], "La main du robot sait la montrer.");
        Assert.IsNotNull(golden.modelPrefab, "Un modèle, même provisoire.");

        var magazine = new WeaponController.MagazineState { equipped = 0, ammo = 30 };
        Assert.IsFalse(WeaponController.TrySwitchWeapon(ref magazine, WeaponController.GoldenGunSlot, 1.0, 0.35f, 1),
            "Aucun joueur ne la demande : seul le serveur la met en main.");
    }

    [Test]
    public void LeServeurMetLArmeDuModeEnMain_PleineEtASortir()
    {
        var magazine = new WeaponController.MagazineState { equipped = 3, ammo = 0, reloadEndsAt = 9.0, readyAt = 0.0 };
        WeaponController.ForceWeapon(ref magazine, WeaponController.GoldenGunSlot, 10.0, 0.35f, 1);
        Assert.AreEqual(WeaponController.GoldenGunSlot, magazine.equipped);
        Assert.AreEqual(1, magazine.ammo, "Chargée.");
        Assert.AreEqual(0.0, magazine.reloadEndsAt, "Le rechargement en cours est abandonné.");
        Assert.AreEqual(10.35, magazine.readyAt, 1e-6, "Elle se sort.");

        WeaponController.ForceWeapon(ref magazine, WeaponController.GoldenGunSlot, 20.0, 0.35f, 1);
        Assert.AreEqual(10.35, magazine.readyAt, 1e-6, "Déjà en main : elle ne se ressort pas.");
    }

    [Test]
    public void UneBalleDuGoldenGun_TuePartout_EtSeRechargeSeule()
    {
        WeaponData golden = PrefabWeapon().DataOf(WeaponController.GoldenGunSlot);
        Assert.AreEqual(1, golden.magazineSize, "Une seule balle (G3).");
        Assert.IsTrue(golden.reloadsAfterEachShot, "Rechargée après chaque tir (G3).");
        Assert.IsFalse(golden.UsesHeat);
        Assert.AreEqual(1, golden.PelletCount);
        foreach (HitboxZoneType zone in System.Enum.GetValues(typeof(HitboxZoneType)))
        {
            foreach (float distance in new[] { 1f, 30f, golden.maxRange })
                Assert.GreaterOrEqual(golden.DamageAt(zone, distance), 100f, $"{zone} à {distance} m : une balle tue, jambes comprises (G2).");
        }

        Assert.IsTrue(WeaponController.AutoReloadWanted(false, false, false, false, 0, false, afterEachShot: true),
            "Vide, elle recharge sans rien presser.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(false, false, false, false, 1, false, afterEachShot: true), "Chargée : rien.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(false, false, false, false, 0, true, afterEachShot: true), "Déjà en train de recharger.");
        Assert.IsFalse(WeaponController.AutoReloadWanted(false, false, false, false, 0, false),
            "Une arme ordinaire vide attend la gâchette ou la touche.");
    }

    // ------------------------------------------------------------------ Le Gun Game

    [Test]
    public void LeGunGame_SeJoueDUneTraite_SurLEchelleChoisieAuSalon()
    {
        MatchRules gunGame = GameModeCatalog.RulesFor(GameModeId.GunGame);
        Assert.AreEqual(GameModeId.GunGame, gunGame.mode);
        Assert.AreEqual(GunGameLadder.Full, gunGame.ladder, "La grande échelle par défaut.");
        Assert.AreEqual(1, gunGame.roundsToWin, "Pas de manches : le match se joue d'une traite (GG1).");
        Assert.AreEqual(1, new MatchRules { mode = GameModeId.GunGame, roundsToWin = 3 }.Sanitized().roundsToWin);
        Assert.AreEqual("grande échelle", gunGame.Format);
        Assert.IsFalse(RoundManager.HasPreparation(GameModeId.GunGame), "L'arme est celle de la marche : pas de préparation.");
        Assert.IsTrue(RoundManager.HasRespawns(GameModeCatalog.RulesFor(GameModeId.GunGame)), "Avec réapparitions (GG1).");
        Assert.IsFalse(RoundManager.HasRespawns(GameModeCatalog.RulesFor(GameModeId.Classic)));
        Assert.IsTrue(GameModeCatalog.ImposesWeapon(GameModeId.GunGame), "Ni préparation, ni arme ramassée, ni arme lancée (T7).");
        Assert.AreEqual("Gun Game · échelle laser · toujours les mêmes",
            GameModeCatalog.Summary(GameModeId.GunGame, new MatchRules { capacity = 2, organization = DuelOrganization.FixedDuelists, mode = GameModeId.GunGame, ladder = GunGameLadder.Laser }));

        var laser = new MatchRules { capacity = 3, organization = DuelOrganization.KingOfTheHill, mode = GameModeId.GunGame, ladder = GunGameLadder.Laser };
        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(laser));
        Assert.AreEqual(GameModeId.GunGame, back.mode);
        Assert.AreEqual(GunGameLadder.Laser, back.ladder, "L'échelle voyage dans les réglages du salon.");
        Assert.AreEqual(GunGameLadder.Full, OnlineSession.ParseRules("4;0;1;3;0;9").ladder, "Un salon d'avant l'échelle (six champs) : la grande.");
        Assert.AreEqual(GunGameLadder.Full, new MatchRules { mode = GameModeId.GunGame, ladder = (GunGameLadder)200 }.Sanitized().ladder,
            "Une échelle inconnue (une autre version) : la grande.");

        MatchRules fromClassic = GameModeCatalog.WithMode(new MatchRules { capacity = 4, mode = GameModeId.Classic, roundsToWin = 2, ladder = GunGameLadder.Stock },
                                                          GameModeId.GunGame);
        Assert.AreEqual(GunGameLadder.Stock, fromClassic.ladder, "L'échelle retenue revient quand l'hôte repasse au Gun Game.");
        Assert.AreEqual(1, fromClassic.roundsToWin);
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, GameModeCatalog.WithMode(fromClassic, GameModeId.Classic).roundsToWin);
    }

    [Test]
    public void LesEchelles_SontCellesDuGDD_DesPlusFortesAuxPlusFaibles_PuisLeGant()
    {
        WeaponController weapon = PrefabWeapon();
        for (int slot = 0; slot < WeaponController.MaxSlots; slot++)
            Assert.AreEqual(weapon.DataOf(slot).weaponName, GunGame.Designations[slot], $"La désignation de la place {slot} est celle du préfab.");

        // La grande échelle, dans l'ordre de l'utilisateur (GG8) : les douze, chacune une fois.
        Assert.AreEqual("FP-30, FP-12, FX-2, FAP-30, FX-3, FAP-03, FA-07, FX-1, FA-30, RV-30, FX-4, PA-05, puis le gant",
                        GunGame.Describe(GunGameLadder.Full));
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, new List<int>(GunGame.Weapons(GunGameLadder.Full)));
        // Les échelles courtes (proposition de Claude) : les armes d'un rang, dans l'ordre de la grande.
        Assert.AreEqual("FP-12, FAP-03, FA-07, PA-05, puis le gant", GunGame.Describe(GunGameLadder.Stock));
        Assert.AreEqual("FP-30, FAP-30, FA-30, RV-30, puis le gant", GunGame.Describe(GunGameLadder.Series30));
        Assert.AreEqual("FX-2, FX-3, FX-1, FX-4, puis le gant", GunGame.Describe(GunGameLadder.Laser));
        foreach (GunGameLadder ladder in GunGame.Ladders)
        {
            int glove = GunGame.GloveStep(ladder);
            Assert.AreEqual(glove + 1, GunGame.StepCount(ladder));
            Assert.AreEqual(WeaponController.GloveSlot, GunGame.SlotAt(ladder, glove), $"{ladder} : la dernière marche est le gant (GG4).");
            Assert.AreEqual("Gant", GunGame.WeaponNameAt(ladder, glove));
            Assert.AreEqual(WeaponController.GloveSlot, GunGame.SlotAt(ladder, glove + 3), "Au-delà, le gant encore.");
            Assert.AreEqual(GunGame.Weapons(ladder)[0], GunGame.SlotAt(ladder, -1), "En deçà, la première arme.");
        }
    }

    [Test]
    public void UneEliminationALArme_FaitMonter_LeGantFinitOuFaitRedescendre()
    {
        GunGameLadder ladder = GunGameLadder.Stock;   // quatre armes, le gant à la marche 4
        GunGame.KillOutcome shot = GunGame.AfterKill(ladder, 0, 2, byGlove: false);
        Assert.AreEqual(1, shot.killerStep, "À l'arme : la marche suivante.");
        Assert.AreEqual(2, shot.victimStep, "Le tué ne perd rien.");
        Assert.IsFalse(shot.killerWins);

        GunGame.KillOutcome toGlove = GunGame.AfterKill(ladder, 3, 0, byGlove: false);
        Assert.AreEqual(4, toGlove.killerStep, "La dernière arme mène au gant.");
        Assert.IsFalse(toGlove.killerWins, "Il faut encore le finir au gant.");

        GunGame.KillOutcome glove = GunGame.AfterKill(ladder, 4, 3, byGlove: true);
        Assert.IsTrue(glove.killerWins, "Au gant, sur la dernière marche : gagné (GG4).");

        GunGame.KillOutcome humiliation = GunGame.AfterKill(ladder, 1, 3, byGlove: true);
        Assert.AreEqual(1, humiliation.killerStep, "Un coup de gant en cours de partie ne fait pas monter (GG5).");
        Assert.AreEqual(2, humiliation.victimStep, "Le tué redescend d'un cran (GG5).");
        Assert.IsFalse(humiliation.killerWins);
        Assert.AreEqual(0, GunGame.AfterKill(ladder, 2, 0, byGlove: true).victimStep, "Pas plus bas que la première arme.");
        Assert.AreEqual(3, GunGame.AfterKill(ladder, 0, 4, byGlove: true).victimStep, "Au gant, il revient à la dernière arme.");
    }

    [Test]
    public void LArmeSuivante_ArriveEnPleineRafale_SansAttendreLesTirsEnRoute()
    {
        // Le tueur tire encore : deux tirs sont partis que le serveur n'a pas comptés quand il lui donne l'arme suivante.
        var local = new WeaponController.MagazineState { equipped = 6, ammo = 2, actionsProcessed = 10 };
        var server = new WeaponController.MagazineState { equipped = 6, ammo = 4, actionsProcessed = 10 };
        WeaponController.ForceWeapon(ref server, 1, 20.0, 0.8f, 5);
        unchecked { server.forced++; }
        const ushort sent = 12;
        WeaponController.ReconcileMagazine(server, sent, ref local);
        Assert.AreEqual(1, local.equipped, "L'arme suivante, tout de suite, tirs en route ou non.");
        Assert.AreEqual(5, local.ammo, "Pleine.");

        // Ensuite, comme avant : rien ne se recale tant que des tirs sont en route.
        var later = local;
        later.ammo = 3;
        WeaponController.ReconcileMagazine(later, sent, ref local);
        Assert.AreEqual(5, local.ammo, "Deux tirs encore en route : la prédiction tient.");
        later.actionsProcessed = sent;
        WeaponController.ReconcileMagazine(later, sent, ref local);
        Assert.AreEqual(3, local.ammo, "Tout est arrivé : le serveur fait foi.");
    }

    [Test]
    public void LArmeImposee_EstCelleDeLaMarche()
    {
        var laser = new MatchRules { capacity = 2, mode = GameModeId.GunGame, ladder = GunGameLadder.Laser };
        Assert.AreEqual(7, RoundManager.ImposedWeaponSlot(laser, 0), "La première marche de l'échelle laser : le FX-2.");
        Assert.AreEqual(3, RoundManager.ImposedWeaponSlot(laser, 3), "La quatrième : le FX-4.");
        Assert.AreEqual(WeaponController.GloveSlot, RoundManager.ImposedWeaponSlot(laser, 4), "Puis le gant.");
        Assert.AreEqual(6, RoundManager.ImposedWeaponSlot(GameModeCatalog.RulesFor(GameModeId.GunGame), 0), "La grande échelle part du FP-30.");
    }

    [Test]
    public void LeGant_NeTireNiNeRecharge_EtNEstPasDansLArsenal()
    {
        WeaponController weapon = PrefabWeapon();
        WeaponData glove = weapon.DataOf(WeaponController.GloveSlot);
        Assert.IsNotNull(glove, "Le gant seul, à la place qui suit le Golden Gun.");
        Assert.IsTrue(glove.unarmed);
        Assert.IsNull(glove.modelPrefab, "Rien en main : le gant est déjà au poing.");
        Assert.AreEqual(0, glove.magazineSize, "Pas de balle.");
        Assert.AreSame(glove, weapon.AllWeapons[WeaponController.GloveSlot]);
        for (int slot = 0; slot < weapon.SlotCount; slot++) Assert.AreNotSame(glove, weapon.DataOf(slot));
        for (int slot = 0; slot < weapon.SlotCount; slot++)
            Assert.GreaterOrEqual(glove.moveSpeedMultiplier, weapon.DataOf(slot).moveSpeedMultiplier, "À mains nues, on va au moins aussi vite qu'armé.");

        var magazine = new WeaponController.MagazineState { equipped = WeaponController.GloveSlot, ammo = 0 };
        Assert.IsFalse(WeaponController.TryConsumeRound(ref magazine, 5.0, glove.magazineSize), "Rien ne part.");
        Assert.IsFalse(WeaponController.TryStartReload(ref magazine, 5.0, glove.magazineSize, 1f), "Rien à recharger.");
        Assert.IsFalse(PlayerLocomotion.GateAim(true, false, unarmed: true), "Rien à épauler.");
        Assert.IsTrue(PlayerLocomotion.GateAim(true, false), "Armé, on épaule.");
    }

    [Test]
    public void LaReapparition_VaAuPointLePlusLoin_QueLAdversaireNeVoitPas()
    {
        Assert.AreEqual(1, PlayerSpawnPoints.PickRespawn(new[] { 40f, 25f, 10f }, new[] { false, true, true }),
            "Un point caché l'emporte sur un point en vue, même plus loin.");
        Assert.AreEqual(2, PlayerSpawnPoints.PickRespawn(new[] { 40f, 25f, 30f }, new[] { false, true, true }),
            "Parmi les points cachés, le plus éloigné.");
        Assert.AreEqual(0, PlayerSpawnPoints.PickRespawn(new[] { 40f, 25f, 30f }, new[] { false, false, false }),
            "Tous en vue : le plus éloigné.");
        Assert.AreEqual(0, PlayerSpawnPoints.PickRespawn(new[] { 20f, 20f }, new[] { true, true }), "À égalité, le premier.");
        Assert.AreEqual(-1, PlayerSpawnPoints.PickRespawn(new float[0], new bool[0]));
    }

    [Test]
    public void LaReapparition_NeSeFaitPasSousLesYeuxDeLAdversaire_AvecDeVraisColliders()
    {
        var objects = new List<GameObject>();
        try
        {
            GameObject root = new GameObject("Points");
            objects.Add(root);
            var points = root.AddComponent<PlayerSpawnPoints>();
            Transform near = NewPoint(root.transform, "Caché, près", new Vector3(0f, 0f, 12f), objects);
            Transform far = NewPoint(root.transform, "En vue, loin", new Vector3(6f, 0f, 40f), objects);
            Transform hidden = NewPoint(root.transform, "Caché, loin", new Vector3(20f, 0f, 30f), objects);
            var so = new UnityEditor.SerializedObject(points);
            SetArray(so.FindProperty("spawnPoints"), new[] { far });
            SetArray(so.FindProperty("respawnPoints"), new[] { near, hidden });
            so.ApplyModifiedPropertiesWithoutUndo();

            // Deux murs : l'un cache le point près de l'adversaire, l'autre celui de côté.
            objects.Add(NewWall(new Vector3(0f, 1.5f, 10.5f), new Vector3(1f, 3f, 0.5f)));
            objects.Add(NewWall(new Vector3(10f, 1.5f, 15f), new Vector3(0.5f, 3f, 30f)));
            Physics.SyncTransforms();

            var eyes = new List<Vector3> { new Vector3(0f, 1.65f, 0f) };
            Assert.IsTrue(points.TryGetRespawnPoint(eyes, out Vector3 position, out _));
            Assert.AreEqual(hidden.position, position, "Le plus éloigné des points cachés, pas celui qu'il voit au bout de l'allée.");

            hidden.position = new Vector3(-20f, 0f, 30f);   // de l'autre côté : en vue
            Physics.SyncTransforms();
            Assert.IsTrue(points.TryGetRespawnPoint(eyes, out position, out _));
            Assert.AreEqual(near.position, position, "Le seul caché qui reste, même près.");
        }
        finally
        {
            foreach (GameObject o in objects) Object.DestroyImmediate(o);
        }
    }

    private static Transform NewPoint(Transform parent, string name, Vector3 position, List<GameObject> objects)
    {
        var point = new GameObject(name).transform;
        point.SetParent(parent, false);
        point.position = position;
        objects.Add(point.gameObject);
        return point;
    }

    private static GameObject NewWall(Vector3 center, Vector3 size)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = center;
        wall.transform.localScale = size;
        wall.layer = 0;
        return wall;
    }

    private static void SetArray(UnityEditor.SerializedProperty array, Transform[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>Les points de réapparition de l'arène de test (RespawnPointsBuilder) : un robot y tient debout sans
    /// toucher une caisse, on s'y rend à pied depuis le poste A (rien derrière un mur d'enceinte), ils sont répartis,
    /// et de chaque poste, l'adversaire en a toujours plusieurs hors de sa vue.</summary>
    [Test]
    public void LArene_ADesPointsDeReapparition_OuTientUnRobot_HorsDeVueDesPostes()
    {
        const string scenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            PlayerSpawnPoints points = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                points = root.GetComponentInChildren<PlayerSpawnPoints>(true);
                if (points != null) break;
            }
            Assert.IsNotNull(points);
            var so = new UnityEditor.SerializedObject(points);
            UnityEditor.SerializedProperty respawns = so.FindProperty("respawnPoints");
            Assert.GreaterOrEqual(respawns.arraySize, 12, "Des points partout dans l'arène (Duel Arena > Arène > Poser les points de réapparition).");
            Vector3 postA = ((Transform)so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).objectReferenceValue).position;
            Vector3 postB = ((Transform)so.FindProperty("spawnPoints").GetArrayElementAtIndex(1).objectReferenceValue).position;

            Physics.SyncTransforms();
            var reachable = new HashSet<Vector2Int>();
            foreach (Vector3 cell in RespawnPointsBuilder.ReachableFloor(postA))
                reachable.Add(new Vector2Int(Mathf.RoundToInt(cell.x * 2f), Mathf.RoundToInt(cell.z * 2f)));

            var positions = new List<Vector3>();
            for (int i = 0; i < respawns.arraySize; i++)
            {
                Vector3 p = ((Transform)respawns.GetArrayElementAtIndex(i).objectReferenceValue).position;
                positions.Add(p);
                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.8f, 1, QueryTriggerInteraction.Ignore)
                              && Mathf.Abs(ground.point.y - p.y) < 0.05f, $"Point {i + 1} ({p}) : sur le sol.");
                Assert.IsFalse(Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.35f, 0.5f, 1, QueryTriggerInteraction.Ignore),
                    $"Point {i + 1} ({p}) : un robot y tient sans toucher le décor.");
                Assert.IsTrue(reachable.Contains(new Vector2Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.z * 2f))),
                    $"Point {i + 1} ({p}) : on s'y rend à pied depuis le poste A (dans l'enceinte).");
            }
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                    Assert.Greater(Vector3.Distance(positions[i], positions[j]), 6f, $"Les points {i + 1} et {j + 1} sont trop près.");
            }

            foreach (Vector3 post in new[] { postA, postB })
            {
                Vector3 eye = post + Vector3.up * 1.65f;
                int hidden = 0;
                foreach (Vector3 p in positions)
                {
                    if (Physics.Linecast(eye, p + Vector3.up * 1.6f, 1, QueryTriggerInteraction.Ignore)
                        && Physics.Linecast(eye, p + Vector3.up * 1.1f, 1, QueryTriggerInteraction.Ignore)) hidden++;
                }
                Assert.GreaterOrEqual(hidden, 3, $"Du poste {post}, l'adversaire a des points hors de sa vue où revenir.");
            }
        }
        finally
        {
            if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ------------------------------------------------------------------ Le Death match du classique

    [Test]
    public void LeDeathMatch_EstUneCaseDuClassique_QuiVoyageDansLesReglages()
    {
        MatchRules classic = GameModeCatalog.RulesFor(GameModeId.Classic);
        Assert.IsFalse(classic.IsDeathMatch, "Le classique se joue en manches par défaut.");
        Assert.AreEqual(MatchRules.DefaultKillsToWin, classic.killsToWin, "10 éliminations par défaut (proposition de Claude).");
        Assert.AreEqual(MatchRules.Bo5RoundsToWin, classic.WinTarget);
        Assert.IsFalse(RoundManager.HasRespawns(classic));

        MatchRules dm = classic;
        dm.deathMatch = true;
        dm.killsToWin = 15;
        dm = dm.Sanitized();
        Assert.IsTrue(dm.IsDeathMatch);
        Assert.AreEqual(15, dm.WinTarget, "Le match se gagne aux éliminations, pas aux manches (N8).");
        Assert.IsTrue(RoundManager.HasRespawns(dm), "Avec réapparitions (N8).");
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.Classic), "Chacun choisit son arme avant le match.");
        Assert.AreEqual("Death match à 15", dm.Format);
        Assert.AreEqual("Classique · Death match à 15 · toujours les mêmes · arme libre", GameModeCatalog.Summary(GameModeId.Classic, dm));

        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(dm));
        Assert.IsTrue(back.deathMatch, "La case voyage dans les réglages du salon.");
        Assert.AreEqual(15, back.killsToWin);
        MatchRules old = OnlineSession.ParseRules("4;0;1;3;0;0;0");
        Assert.IsFalse(old.deathMatch, "Un salon d'avant la case (sept champs) : en manches.");
        Assert.AreEqual(MatchRules.DefaultKillsToWin, old.killsToWin);
        Assert.AreEqual(MatchRules.DefaultKillsToWin, new MatchRules { deathMatch = true, killsToWin = 12 }.Sanitized().killsToWin,
            "12 n'est pas proposé : 10.");

        // Une case du classique : retenue dans un autre mode, sans effet.
        MatchRules golden = GameModeCatalog.WithMode(dm, GameModeId.GoldenGun);
        Assert.IsTrue(golden.deathMatch, "Retenue...");
        Assert.IsFalse(golden.IsDeathMatch, "...mais le Golden Gun se joue en manches.");
        Assert.IsFalse(RoundManager.HasRespawns(golden));
        Assert.AreEqual(golden.roundsToWin, golden.WinTarget);
        Assert.IsTrue(GameModeCatalog.WithMode(golden, GameModeId.Classic).IsDeathMatch, "Revenu au classique, la case l'est encore.");
    }

    [Test]
    public void UneElimination_CompteSeulementPourUnAuteurQuiNEstPasLeTue()
    {
        Assert.IsTrue(RoundManager.KillScores(0, 1));
        Assert.IsFalse(RoundManager.KillScores(1, 1), "On ne marque pas en se tuant.");
        Assert.IsFalse(RoundManager.KillScores(Health.NoKiller, 1), "Sans auteur connu, personne ne marque.");
    }

    [Test]
    public void LeSalonDuClassique_ProposeLeDeathMatch_EtSesEliminations()
    {
        MatchRules classic = GameModeCatalog.RulesFor(GameModeId.Classic);
        Row(SalonView.EssentialRows(classic), "Manches");
        SalonView.Row check = Row(SalonView.AdvancedRows(classic, 2), "Death match");
        Assert.IsTrue(check.isCheck, "Une case (sous « Plus de réglages »).");
        Assert.IsFalse(check.check);
        StringAssert.Contains("10 éliminations", check.hint);
        MatchRules dm = check.setCheck(classic, true);
        Assert.IsTrue(dm.IsDeathMatch);

        List<SalonView.Row> dmRows = SalonView.EssentialRows(dm);
        Assert.AreEqual(3, dmRows.Count, "Toujours trois réglages d'emblée (la maquette B).");
        foreach (SalonView.Row row in dmRows) Assert.AreNotEqual("Manches", row.label, "Les éliminations à la place des manches.");
        SalonView.Row kills = Row(dmRows, "Éliminations");
        CollectionAssert.AreEqual(new[] { "Premier à 5", "Premier à 10", "Premier à 15" }, System.Array.ConvertAll(kills.choices, c => c.label));
        Assert.IsTrue(kills.choices[1].selected, "10 par défaut.");
        Assert.AreEqual(5, kills.choices[0].apply(dm).killsToWin);
        Assert.IsTrue(Row(SalonView.AdvancedRows(dm, 2), "Death match").check);

        foreach (GameModeId other in new[] { GameModeId.GoldenGun, GameModeId.GunGame })
        {
            foreach (SalonView.Row row in SalonView.AdvancedRows(GameModeCatalog.RulesFor(other), 2))
                Assert.AreNotEqual("Death match", row.label, $"Le Death match est une case du classique, pas de {other}.");
        }
    }

    // ------------------------------------------------------------------ L'Élimination confirmée

    [Test]
    public void LEliminationConfirmee_SeJoueDUneTraite_AuxEliminationsConfirmees()
    {
        MatchRules kc = GameModeCatalog.RulesFor(GameModeId.KillConfirmed);
        Assert.AreEqual(GameModeId.KillConfirmed, kc.mode, "Un mode jouable : sa carte ouvre un salon.");
        Assert.IsTrue(kc.CountsKills, "Le match se gagne aux éliminations, pas aux manches.");
        Assert.AreEqual(MatchRules.DefaultKillsToWin, kc.WinTarget, "Le premier à 10 (GDD § 11).");
        Assert.IsTrue(RoundManager.HasRespawns(kc), "Avec réapparitions.");
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.KillConfirmed), "L'arme est libre : chacun la choisit avant le match.");
        Assert.IsFalse(GameModeCatalog.ImposesWeapon(GameModeId.KillConfirmed));
        Assert.AreEqual(-1, RoundManager.ImposedWeaponSlot(kc, 0), "Chacun garde la sienne.");
        Assert.AreEqual("premier à 10", kc.Format);
        Assert.AreEqual("Élimination confirmée · premier à 10 · toujours les mêmes · arme libre",
                        GameModeCatalog.Summary(GameModeId.KillConfirmed, kc));

        MatchRules five = kc;
        five.killsToWin = 5;
        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(five));
        Assert.AreEqual(GameModeId.KillConfirmed, back.mode, "Le mode voyage dans les réglages du salon.");
        Assert.AreEqual(5, back.WinTarget);

        // Le même réglage que les éliminations du Death match, gardé d'un mode à l'autre.
        MatchRules dm = GameModeCatalog.RulesFor(GameModeId.Classic);
        dm.deathMatch = true;
        dm.killsToWin = 15;
        MatchRules fromDm = GameModeCatalog.WithMode(dm, GameModeId.KillConfirmed);
        Assert.AreEqual(15, fromDm.WinTarget);
        Assert.IsFalse(fromDm.IsDeathMatch, "La case du Death match ne vaut qu'au classique.");
    }

    [Test]
    public void UnModule_SePrendEnPassantDessus_ParSonVainqueurOuSaVictime()
    {
        var module = new Vector3(10f, 0.8f, 10f);
        var near = new Vector3(10.9f, 0f, 10.6f);
        var far = new Vector3(11.3f, 0f, 10f);
        Assert.IsTrue(KillConfirmed.Reaches(module, near), "À 1,08 m, on le ramasse en passant.");
        Assert.IsFalse(KillConfirmed.Reaches(module, far), "À 1,3 m, on passe à côté.");
        Assert.IsTrue(KillConfirmed.Reaches(module, new Vector3(10f, 1f, 10f)), "Debout sur une caisse basse, à côté.");
        Assert.IsFalse(KillConfirmed.Reaches(module, new Vector3(10f, 4.5f, 10f)), "Sur la passerelle, au-dessus : trop haut.");
        Assert.IsFalse(KillConfirmed.Reaches(module, new Vector3(10f, -2f, 10f)), "Bien en dessous : trop bas.");

        Assert.AreEqual(KillConfirmed.Taker.Killer, KillConfirmed.Resolve(module, true, near, true, far), "Son vainqueur le ramasse : l'élimination compte.");
        Assert.AreEqual(KillConfirmed.Taker.Victim, KillConfirmed.Resolve(module, true, far, true, near), "Sa victime revenue le reprend : elle est annulée.");
        Assert.AreEqual(KillConfirmed.Taker.None, KillConfirmed.Resolve(module, false, near, false, near), "Un mort ne ramasse rien.");
        Assert.AreEqual(KillConfirmed.Taker.None, KillConfirmed.Resolve(module, true, far, true, far), "Personne dessus.");
        var closer = new Vector3(10.2f, 0f, 10f);
        Assert.AreEqual(KillConfirmed.Taker.Victim, KillConfirmed.Resolve(module, true, near, true, closer), "Les deux dessus : le plus proche.");
        Assert.AreEqual(KillConfirmed.Taker.Killer, KillConfirmed.Resolve(module, true, closer, true, near));
        Assert.AreEqual(KillConfirmed.Taker.Killer, KillConfirmed.Resolve(module, true, closer, true, closer), "À égalité, son vainqueur.");
    }

    [Test]
    public void LeMort_NeReapparaitPasSurSonModule()
    {
        // Le point le plus loin de l'adversaire, caché, est celui où le mort est tombé (2) : il y reprenait son
        // module à l'instant de son retour (vu à deux instances le 2026-11-02).
        var distances = new List<float> { 20f, 35f, 58f, 40f };
        var hidden = new List<bool> { true, true, true, false };
        Assert.AreEqual(2, PlayerSpawnPoints.PickRespawn(distances, hidden), "Sans module, le plus loin parmi les points cachés.");
        Assert.AreEqual(1, PlayerSpawnPoints.PickRespawn(distances, hidden, new List<bool> { true, true, false, true }),
            "Son module y flotte : le plus loin des autres points cachés.");
        Assert.AreEqual(3, PlayerSpawnPoints.PickRespawn(distances, hidden, new List<bool> { false, false, false, true }),
            "Seul point permis, même en vue.");
        Assert.AreEqual(2, PlayerSpawnPoints.PickRespawn(distances, hidden, new List<bool> { false, false, false, false }),
            "Tous trop près : la règle d'avant.");
        Assert.Greater(KillConfirmed.RespawnClearance, KillConfirmed.PickupRadius * 4f, "Le reprendre demande d'y retourner.");
    }

    [Test]
    public void LeModule_FlotteAuDessusDuSolOuEstTombeLeRobot_AvecDeVraisColliders()
    {
        // Loin de tout ce que la scène ouverte pourrait porter.
        var origin = new Vector3(300f, 50f, -300f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            floor.transform.position = origin + new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            crate.transform.position = origin + new Vector3(5f, 0.5f, 0f);
            Physics.SyncTransforms();

            Vector3 standing = KillConfirmed.FindDropPoint(origin + new Vector3(2f, 0f, 3f), 1);
            Assert.AreEqual(origin.x + 2f, standing.x, 1e-4f, "À sa place.");
            Assert.AreEqual(origin.z + 3f, standing.z, 1e-4f);
            Assert.AreEqual(origin.y + KillConfirmed.FloatHeight, standing.y, 1e-3f, "Au-dessus du sol où il est tombé.");
            Assert.AreEqual(origin.y + KillConfirmed.FloatHeight, KillConfirmed.FindDropPoint(origin + new Vector3(-3f, 1.1f, 0f), 1).y, 1e-3f,
                "Tué en l'air (en plein franchissement) : son module tombe au sol, sous lui.");
            Assert.AreEqual(origin.y + 1f + KillConfirmed.FloatHeight, KillConfirmed.FindDropPoint(origin + new Vector3(5f, 1f, 0f), 1).y, 1e-3f,
                "Tué debout sur une caisse : sur la caisse.");
            Assert.AreEqual(origin.y + 30f + KillConfirmed.FloatHeight, KillConfirmed.FindDropPoint(origin + new Vector3(0f, 30f, 0f), 1).y, 1e-3f,
                "Sans sol à portée sous lui : à ses pieds.");
        }
        finally
        {
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(crate);
            Physics.SyncTransforms();
        }
    }

    [Test]
    public void LeModuleMemoire_EstConstruit_LuitEtNArreteRien()
    {
        var prefab = Resources.Load<GameObject>(MemoryModule.ResourceName);
        Assert.IsNotNull(prefab, "Duel Arena > Modes > Construire le module mémoire.");
        MemoryModule module = prefab.GetComponent<MemoryModule>();
        Assert.IsNotNull(module);
        Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "Rien ne s'arrête sur un module : ni balle, ni robot.");
        var serialized = new UnityEditor.SerializedObject(module);
        SerializedPropertyHasObjects(serialized.FindProperty("glowRenderers"), "Il luit à la couleur du robot détruit.");
        Assert.IsNotNull(serialized.FindProperty("glowLight").objectReferenceValue, "Sa lumière, pour le repérer dans la pénombre.");
    }

    private static void SerializedPropertyHasObjects(UnityEditor.SerializedProperty array, string message)
    {
        Assert.Greater(array.arraySize, 0, message);
        for (int i = 0; i < array.arraySize; i++) Assert.IsNotNull(array.GetArrayElementAtIndex(i).objectReferenceValue, message);
    }

    [Test]
    public void LeSalonDeLEliminationConfirmee_ProposeLesEliminations_EtLArmeLibre()
    {
        MatchRules kc = GameModeCatalog.RulesFor(GameModeId.KillConfirmed);
        List<SalonView.Row> rows = SalonView.EssentialRows(kc);
        Assert.AreEqual(3, rows.Count, "Trois réglages d'emblée (la maquette B).");
        SalonView.Row kills = Row(rows, "Éliminations");
        CollectionAssert.AreEqual(new[] { "Premier à 5", "Premier à 10", "Premier à 15" }, System.Array.ConvertAll(kills.choices, c => c.label));
        Assert.IsTrue(kills.choices[1].selected, "10 par défaut (GDD § 11).");
        Assert.AreEqual(15, kills.choices[2].apply(kc).WinTarget);
        SalonView.Row weapon = Row(rows, "L'arme");
        Assert.AreEqual("Libre", weapon.choices[0].label);
        Assert.IsTrue(weapon.choices[0].selected, "L'arme est libre.");
        foreach (SalonView.Row row in SalonView.AdvancedRows(kc, 2))
            Assert.AreNotEqual("Death match", row.label, "La case du Death match est au classique.");
    }

    // ------------------------------------------------------------------ La Zone

    [Test]
    public void LaZone_SeJoueAuxPoints_DUneTraite()
    {
        MatchRules zone = GameModeCatalog.RulesFor(GameModeId.Zone);
        Assert.AreEqual(GameModeId.Zone, zone.mode, "Un mode jouable : sa carte ouvre un salon.");
        Assert.IsTrue(zone.CountsPoints);
        Assert.IsFalse(zone.CountsKills);
        Assert.AreEqual(3000, zone.WinTarget, "Le premier à 3 000 points (GDD § 11, proposition de Claude).");
        Assert.IsTrue(RoundManager.HasRespawns(zone), "Avec réapparitions (Z8).");
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.Zone), "L'arme est libre : chacun la choisit avant le match.");
        Assert.AreEqual(-1, RoundManager.ImposedWeaponSlot(zone, 0));
        Assert.AreEqual("premier à 3\u00A0000 points", zone.Format);
        Assert.AreEqual("Zone · premier à 3\u00A0000 points · toujours les mêmes · arme libre", GameModeCatalog.Summary(GameModeId.Zone, zone));
        Assert.AreEqual(GameModeId.Zone, OnlineSession.ParseRules(OnlineSession.FormatRules(zone)).mode, "Le mode voyage dans les réglages du salon.");
        Assert.Less(ZoneMode.PointsPerKill, ZoneMode.PointsPerSecond * 10, "Une élimination rapporte moins qu'un moment dans la zone (Z1, Z4).");
        Assert.AreEqual("1\u00A0500", ZoneMode.Points(1500));
        Assert.AreEqual("20", ZoneMode.Points(20));
    }

    [Test]
    public void LaZone_ChangeDePlaceToutesLesMinutes_AnnonceeDixSecondesAvant()
    {
        Assert.AreEqual(0, ZoneMode.ZoneAt(0f, 4), "Elle s'ouvre dès le départ, au premier emplacement (Z3).");
        Assert.AreEqual(0, ZoneMode.ZoneAt(59.9f, 4));
        Assert.AreEqual(1, ZoneMode.ZoneAt(60f, 4), "Puis la suivante, dans l'ordre de l'arène.");
        Assert.AreEqual(3, ZoneMode.ZoneAt(239f, 4));
        Assert.AreEqual(0, ZoneMode.ZoneAt(240f, 4), "Et l'on recommence le tour.");
        Assert.AreEqual(-1, ZoneMode.ZoneAt(10f, 0), "Une arène sans zone.");
        Assert.AreEqual(60f, ZoneMode.TimeUntilMove(0f), 1e-4f);
        Assert.AreEqual(10f, ZoneMode.TimeUntilMove(50f), 1e-4f);
        Assert.IsFalse(ZoneMode.Announcing(49.9f));
        Assert.IsTrue(ZoneMode.Announcing(50f), "Annoncée dix secondes avant (Z9).");
        Assert.IsTrue(ZoneMode.Announcing(59.99f));
        Assert.IsFalse(ZoneMode.Announcing(60f), "La nouvelle zone n'annonce pas tout de suite la suivante.");
    }

    [Test]
    public void SeulDansLaZone_OnGagneDesPoints_ADeuxPlusRienNAvance()
    {
        Assert.AreEqual(ZoneMode.Hold.Free, ZoneMode.HoldOf(0));
        Assert.AreEqual(ZoneMode.Hold.Held, ZoneMode.HoldOf(1), "Seul dedans : il la tient.");
        Assert.AreEqual(ZoneMode.Hold.Contested, ZoneMode.HoldOf(2), "À deux dedans, plus rien n'avance (Z5).");

        // 20 points par seconde, comptés image par image, sans rien perdre des fractions.
        float carry = 0f;
        int total = 0;
        for (int i = 0; i < 300; i++) total += ZoneMode.Earn(1f / 300f, ref carry);
        Assert.AreEqual(ZoneMode.PointsPerSecond, total, "Une seconde en 300 images.");
        for (int i = 0; i < 90; i++) total += ZoneMode.Earn(1f / 60f, ref carry);
        Assert.AreEqual(ZoneMode.PointsPerSecond * 2.5f, total, 1f, "Puis une seconde et demie en 90 images.");

        var center = new Vector3(18f, 0f, 30f);
        Assert.IsTrue(ZoneMode.Contains(center, 4f, new Vector3(21f, 0f, 32f)), "À 3,6 m du centre : dedans.");
        Assert.IsFalse(ZoneMode.Contains(center, 4f, new Vector3(22.5f, 0f, 30f)), "À 4,5 m : dehors.");
        Assert.IsTrue(ZoneMode.Contains(center, 4f, new Vector3(18f, 1f, 30f)), "Debout sur une caisse basse dedans.");
        Assert.IsFalse(ZoneMode.Contains(center, 4f, new Vector3(18f, 4.5f, 30f)), "Sur la passerelle, au-dessus : pas dedans.");
    }

    [Test]
    public void UnRepereHorsChamp_SePoseAuBordDeLEcran()
    {
        var panel = new Vector2(1920f, 1080f);
        Vector2 front = GameHud.MarkerPosition(new Vector3(0.5f, 0.5f, 10f), panel, 48f);
        Assert.AreEqual(960f, front.x, 1e-3f, "Devant : à sa place.");
        Assert.AreEqual(540f, front.y, 1e-3f);
        Vector2 right = GameHud.MarkerPosition(new Vector3(1.6f, 0.5f, 10f), panel, 48f);
        Assert.AreEqual(1920f - 48f, right.x, 1e-3f, "Hors champ à droite : au bord droit.");
        Vector2 behind = GameHud.MarkerPosition(new Vector3(0.3f, 0.6f, -5f), panel, 48f);
        Assert.AreEqual(0.7f * 1920f, behind.x, 1e-2f, "Derrière, à gauche : retourné, à droite de l'écran...");
        Assert.AreEqual(1080f - 48f, behind.y, 1e-3f, "...posé au bas de l'écran.");
    }

    [Test]
    public void LaZone_NeCachePasLAdversaire_NiLeReticule()
    {
        // Le repère flotte au-dessus de la paroi (2,2 m) et de la tête d'un robot debout (1,8 m).
        Assert.Greater(GameHud.MarkerHeight, 2.2f + 0.5f, "Le repère se pose au-dessus de la paroi et du robot qui tient la zone.");

        // Sur le réticule, il s'estompe ; à l'écart, il est plein.
        var panel = new Vector2(1920f, 1080f);
        Assert.AreEqual(0.25f, GameHud.MarkerOpacity(panel * 0.5f, panel), 1e-3f, "Sur le réticule : estompé.");
        Assert.Less(GameHud.MarkerOpacity(panel * 0.5f + new Vector2(80f, 0f), panel), 0.7f, "À 80 points du réticule : encore estompé.");
        Assert.AreEqual(1f, GameHud.MarkerOpacity(new Vector2(200f, 540f), panel), 1e-3f, "Au bord de l'écran : plein.");
        Assert.AreEqual(1f, GameHud.MarkerOpacity(panel * 0.5f + new Vector2(0f, 170f), panel), 1e-3f, "À 15 % de la hauteur : plein.");

        // Vue de dedans, la paroi n'est plus qu'un voile ; elle revient en s'éloignant du bord.
        const float radius = 4f;
        Assert.AreEqual(0.25f, ZoneLayout.WallFade(0f, radius, 0.25f, 6f), 1e-4f, "Au centre : un quart.");
        Assert.AreEqual(0.25f, ZoneLayout.WallFade(3.9f, radius, 0.25f, 6f), 1e-4f, "Encore dedans : un quart.");
        float near = ZoneLayout.WallFade(radius + 3f, radius, 0.25f, 6f);
        Assert.That(near, Is.GreaterThan(0.25f).And.LessThan(1f), "À 3 m du bord : entre les deux.");
        Assert.AreEqual(1f, ZoneLayout.WallFade(radius + 6f, radius, 0.25f, 6f), 1e-4f, "À 6 m du bord : tout son éclat.");
        Assert.AreEqual(1f, ZoneLayout.WallFade(40f, radius, 0.25f, 6f), 1e-4f, "De loin : tout son éclat.");
    }

    [Test]
    public void LArene_ADesZones_DegageesEtEquitables()
    {
        const string scenePath = "Assets/_ProjectScenes/MultiTestScene.unity";
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            ZoneLayout layout = null;
            PlayerSpawnPoints posts = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (layout == null) layout = root.GetComponentInChildren<ZoneLayout>(true);
                if (posts == null) posts = root.GetComponentInChildren<PlayerSpawnPoints>(true);
            }
            Assert.IsNotNull(layout, "Duel Arena > Arène > Poser les zones.");
            Assert.AreEqual(4, layout.Count, "Quatre emplacements (proposition de Claude).");
            Assert.IsEmpty(layout.GetComponentsInChildren<Collider>(true), "Une zone n'arrête rien.");
            var so = new UnityEditor.SerializedObject(layout);
            Assert.IsNotNull(so.FindProperty("zoneMaterial").objectReferenceValue, "Sa lumière.");
            Assert.IsNotNull(so.FindProperty("arcMaterial").objectReferenceValue, "Ses arcs quand on la tient.");
            Assert.IsNotNull(so.FindProperty("sounds").objectReferenceValue, "Son grésillement.");
            var spawns = new UnityEditor.SerializedObject(posts).FindProperty("spawnPoints");
            Vector3 postA = ((Transform)spawns.GetArrayElementAtIndex(0).objectReferenceValue).position;
            Vector3 postB = ((Transform)spawns.GetArrayElementAtIndex(1).objectReferenceValue).position;

            Physics.SyncTransforms();
            var reachable = new HashSet<Vector2Int>();
            foreach (Vector3 cell in RespawnPointsBuilder.ReachableFloor(postA))
                reachable.Add(new Vector2Int(Mathf.RoundToInt(cell.x * 2f), Mathf.RoundToInt(cell.z * 2f)));
            for (int i = 0; i < layout.Count; i++)
            {
                Vector3 c = layout.Center(i);
                int inside = 0, walkable = 0;
                for (float x = -layout.Radius; x <= layout.Radius + 0.01f; x += 0.5f)
                {
                    for (float z = -layout.Radius; z <= layout.Radius + 0.01f; z += 0.5f)
                    {
                        if (x * x + z * z > layout.Radius * layout.Radius) continue;
                        inside++;
                        if (reachable.Contains(new Vector2Int(Mathf.RoundToInt((c.x + x) * 2f), Mathf.RoundToInt((c.z + z) * 2f)))) walkable++;
                    }
                }
                Assert.Greater(walkable, inside * 0.9f, $"{layout.NameOf(i)} : un sol dégagé, qu'on rejoint à pied ({walkable} cases sur {inside}).");
                Assert.IsTrue(Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.8f, 1, QueryTriggerInteraction.Ignore)
                              && Mathf.Abs(ground.point.y - c.y) < 0.05f, $"{layout.NameOf(i)} : posée sur le sol.");
                Assert.Greater(Vector3.Distance(c, postA), 12f, $"{layout.NameOf(i)} : loin du poste A.");
                Assert.Greater(Vector3.Distance(c, postB), 12f, $"{layout.NameOf(i)} : loin du poste B.");
                for (int j = i + 1; j < layout.Count; j++)
                    Assert.Greater(Vector3.Distance(c, layout.Center(j)), 2f * layout.Radius + 4f, $"{layout.NameOf(i)} et {layout.NameOf(j)} : bien séparées.");
            }
            Vector3 first = layout.Center(0);
            Assert.AreEqual(Vector3.Distance(first, postA), Vector3.Distance(first, postB), 1.5f, "La première, ouverte dès le départ, à égale distance des deux postes.");
        }
        finally
        {
            if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void LeSalonDeLaZone_DitSonBut_EtLArmeLibre()
    {
        MatchRules zone = GameModeCatalog.RulesFor(GameModeId.Zone);
        List<SalonView.Row> rows = SalonView.EssentialRows(zone);
        Assert.AreEqual(3, rows.Count, "Trois réglages d'emblée (la maquette B).");
        foreach (SalonView.Row row in rows)
        {
            Assert.AreNotEqual("Manches", row.label, "La Zone se gagne aux points.");
            Assert.AreNotEqual("Éliminations", row.label);
        }
        StringAssert.Contains("3\u00A0000 points", Row(rows, "Le but").hint);
        Assert.AreEqual("Libre", Row(rows, "L'arme").choices[0].label);
    }

    [Test]
    public void LeSalonDuGunGame_ProposeLEchelle_EtDitSesArmes()
    {
        MatchRules rules = GameModeCatalog.RulesFor(GameModeId.GunGame);
        List<SalonView.Row> rows = SalonView.EssentialRows(rules);
        foreach (SalonView.Row row in rows) Assert.AreNotEqual("Manches", row.label, "Pas de manches au Gun Game.");

        SalonView.Row ladder = Row(rows, "L'échelle");
        CollectionAssert.AreEqual(new[] { "Grande", "Stock", "Série 30", "Laser" }, System.Array.ConvertAll(ladder.choices, c => c.label));
        Assert.IsTrue(ladder.choices[0].selected);
        Assert.AreEqual(GunGameLadder.Laser, ladder.choices[3].apply(rules).ladder);
        SalonView.Row weapons = Row(rows, "Ses armes");
        Assert.IsTrue(weapons.isFixed);
        Assert.AreEqual(GunGame.Describe(GunGameLadder.Full), weapons.hint);
        Assert.IsTrue(Row(rows, "L'arme").isFixed, "L'arme est celle de la marche.");

        rules.ladder = GunGameLadder.Laser;
        Assert.AreEqual(GunGame.Describe(GunGameLadder.Laser), Row(SalonView.EssentialRows(rules), "Ses armes").hint);
    }

    [Test]
    public void LeTagTeam_SeJoueEnEquipes_LaPremiereACentPoints()
    {
        MatchRules tag = GameModeCatalog.RulesFor(GameModeId.TagTeam);
        Assert.AreEqual(GameModeId.TagTeam, tag.mode, "Un mode jouable : sa carte ouvre un salon.");
        Assert.IsTrue(tag.IsTagTeam);
        Assert.AreEqual(4, tag.capacity, "Sa carte ouvre un deux contre deux (TT2).");
        Assert.IsTrue(tag.spectatorInterventions, "Ceux qui attendent aident depuis la tribune (TT5).");
        Assert.AreEqual(100, tag.WinTarget, "La première équipe à 100 points (TT6).");
        Assert.IsTrue(RoundManager.HasRespawns(tag), "D'une traite : un coéquipier entre à la place du mort (TT4).");
        Assert.IsTrue(RoundManager.HasPreparation(GameModeId.TagTeam), "L'arme est libre : chacun la choisit avant le match.");
        Assert.AreEqual(-1, RoundManager.ImposedWeaponSlot(tag, 0));
        Assert.AreEqual("première équipe à 100 points", tag.Format);
        Assert.AreEqual("Tag Team · première équipe à 100 points · 2 contre 2 · arme libre · cailloux et bagarre",
                        GameModeCatalog.Summary(GameModeId.TagTeam, tag));
        Assert.AreEqual(GameModeId.TagTeam, OnlineSession.ParseRules(OnlineSession.FormatRules(tag)).mode, "Le mode voyage dans les réglages du salon.");

        MatchRules small = tag;
        small.capacity = 2;
        Assert.AreEqual(4, small.Sanitized().capacity, "Jamais moins de quatre places au Tag Team.");
        Assert.AreEqual(4, GameModeCatalog.WithMode(GameModeCatalog.RulesFor(GameModeId.Classic), GameModeId.TagTeam).capacity,
                        "Passer au Tag Team depuis un duel ouvre les places qu'il faut.");
    }

    [Test]
    public void LesEquipes_SontEgales_DeDeuxOuTroisJoueurs()
    {
        Assert.IsTrue(TagTeam.CanPlay(2, 2), "Deux contre deux (TT2).");
        Assert.IsTrue(TagTeam.CanPlay(3, 3), "Trois contre trois.");
        Assert.IsFalse(TagTeam.CanPlay(2, 3), "Jamais à nombre impair (TT9).");
        Assert.IsFalse(TagTeam.CanPlay(1, 1), "Il faut un coéquipier.");
        Assert.IsFalse(TagTeam.CanPlay(4, 4), "Six places au plus.");

        var A = PlayerTeam.A;
        var B = PlayerTeam.B;
        var none = PlayerTeam.None;
        CollectionAssert.AreEqual(new[] { A, B, A, B }, TagTeam.AutoTeams(4), "Par défaut, A et B dans l'ordre d'arrivée.");
        CollectionAssert.AreEqual(new[] { A, B, A, B, none }, TagTeam.AutoTeams(5), "Le dernier d'un nombre impair attend hors équipe.");
        CollectionAssert.AreEqual(new[] { A, B, A }, TagTeam.AutoTeams(3, keepOdd: true), "Le banc de test : deux contre un.");

        for (int seed = 0; seed < 20; seed++)
        {
            PlayerTeam[] five = TagTeam.Shuffled(5, new System.Random(seed));
            Assert.AreEqual(2, System.Array.FindAll(five, t => t == A).Length, "Au hasard, toujours égales.");
            Assert.AreEqual(2, System.Array.FindAll(five, t => t == B).Length);
            Assert.AreEqual(1, System.Array.FindAll(five, t => t == none).Length);
        }
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++) seen.Add(string.Join(",", TagTeam.Shuffled(4, new System.Random(seed))));
        Assert.Greater(seen.Count, 2, "Au hasard pour de bon (TT1).");

        Assert.AreEqual(B, TagTeam.NextTeam(A, 2, 2), "Un clic de l'hôte : A, puis B, puis hors équipe.");
        Assert.AreEqual(none, TagTeam.NextTeam(B, 2, 2));
        Assert.AreEqual(A, TagTeam.NextTeam(none, 2, 2));
        Assert.AreEqual(B, TagTeam.NextTeam(none, 3, 2), "Une équipe pleine se saute.");
        Assert.AreEqual(none, TagTeam.NextTeam(A, 3, 3));
    }

    [Test]
    public void LeRelais_FaitEntrerCeluiQuiAttendDepuisLePlusLongtemps()
    {
        var team = new List<ulong> { 1, 2, 3 };
        Assert.AreEqual(2UL, TagTeam.Relay(team), "Le combattant tombe : le premier qui attend entre (TT4).");
        CollectionAssert.AreEqual(new ulong[] { 2, 3, 1 }, team, "Le tombé attend à son tour, au bout du relais.");
        Assert.AreEqual(3UL, TagTeam.Relay(team), "Puis celui qui attend depuis le plus longtemps.");
        Assert.AreEqual(1UL, TagTeam.Relay(team));
        var alone = new List<ulong> { 7 };
        Assert.AreEqual(7UL, TagTeam.Relay(alone), "Seul dans son équipe : il revient lui-même.");
    }

    [Test]
    public void UneElimination_RapporteDixQuinzeOuVingtPoints()
    {
        Assert.AreEqual(10, TagTeam.PointsFor(Health.DamageCause.Shot), "Une élimination (proposition de Claude du GDD).");
        Assert.AreEqual(15, TagTeam.PointsFor(Health.DamageCause.HeadShot), "À la tête.");
        Assert.AreEqual(20, TagTeam.PointsFor(Health.DamageCause.Glove), "Au gant.");
        Assert.AreEqual("+15 pour l'équipe A · à la tête", GameHud.KillLine(15, PlayerTeam.A, Health.DamageCause.HeadShot));
        Assert.AreEqual("+20 pour l'équipe B · au gant", GameHud.KillLine(20, PlayerTeam.B, Health.DamageCause.Glove));
        Assert.AreEqual("+10 pour l'équipe A", GameHud.KillLine(10, PlayerTeam.A, Health.DamageCause.Shot));
    }

    [Test]
    public void LesEquipesDuSalon_VoyagentDansSaPropriete()
    {
        var composed = new List<(string id, PlayerTeam team)> { ("hote", PlayerTeam.A), ("deux", PlayerTeam.B), ("trois", PlayerTeam.None), ("mal;forme", PlayerTeam.A) };
        string text = TagTeam.Format(composed);
        Assert.AreEqual("hote:A;deux:B;trois:-", text, "Un identifiant illisible n'y entre pas.");
        Dictionary<string, PlayerTeam> parsed = TagTeam.Parse(text + ";cassé;x:Z");
        Assert.AreEqual(3, parsed.Count, "Ce qui ne se lit pas est ignoré.");
        Assert.AreEqual(PlayerTeam.A, parsed["hote"]);
        Assert.AreEqual(PlayerTeam.B, parsed["deux"]);
        Assert.AreEqual(PlayerTeam.None, parsed["trois"]);

        var players = new List<LobbyController.Entry>
        {
            new LobbyController.Entry("hote", "Hôte", default, true, true),
            new LobbyController.Entry("deux", "Deux", default, false, false),
            new LobbyController.Entry("trois", "Trois", default, false, false),
            new LobbyController.Entry("quatre", "Quatre", default, false, false),
        };
        CollectionAssert.AreEqual(TagTeam.AutoTeams(4), LobbyController.TeamsOf(players, null), "Sans composition de l'hôte, les équipes par défaut.");
        CollectionAssert.AreEqual(new[] { PlayerTeam.A, PlayerTeam.B, PlayerTeam.None, PlayerTeam.None }, LobbyController.TeamsOf(players, parsed),
                                  "Celles de l'hôte ; un nouveau venu attend hors équipe.");
    }

    [Test]
    public void LeSalonDuTagTeam_DitSonBut_EtSesEquipes()
    {
        MatchRules tag = GameModeCatalog.RulesFor(GameModeId.TagTeam);
        List<SalonView.Row> rows = SalonView.EssentialRows(tag);
        foreach (SalonView.Row row in rows) Assert.AreNotEqual("Qui affronte qui", row.label, "Les équipes disent qui affronte qui.");
        StringAssert.Contains("100 points", Row(rows, "Le but").hint);
        StringAssert.Contains("coéquipier", Row(rows, "Les équipes").hint);
        Assert.AreEqual("Libre", Row(rows, "L'arme").choices[0].label);

        List<SalonView.Row> advanced = SalonView.AdvancedRows(tag, 2);
        foreach (SalonView.Row row in advanced) Assert.AreNotEqual("Duellistes tirés au sort", row.label, "Les équipes se tirent au sort à côté des joueurs.");
        SalonView.Row places = Row(advanced, "Places");
        Assert.IsTrue(places.choices[0].unavailable && places.choices[1].unavailable, "Ni deux ni trois places au Tag Team.");
        Assert.IsTrue(places.choices[2].Enabled, "Quatre places, deux contre deux.");

        Assert.AreEqual("SE BAT EN PREMIER · HÔTE", SalonView.TeamRole(PlayerTeam.A, 0, true, false));
        Assert.AreEqual("ENTRE EN 2E · TOI", SalonView.TeamRole(PlayerTeam.B, 1, false, true));
        Assert.AreEqual("TRIBUNE", SalonView.TeamRole(PlayerTeam.None, 0, false, false));
        StringAssert.Contains("ALPHA commence pour l'équipe A, BRAVO pour l'équipe B", SalonView.TeamPhrase(2, 2, "ALPHA", "BRAVO"));
        Assert.AreEqual("Il faut quatre joueurs : deux par équipe.", SalonView.TeamPhrase(2, 1, "ALPHA", "BRAVO"));
        StringAssert.Contains("autant de joueurs", SalonView.TeamPhrase(3, 2, "ALPHA", "BRAVO"));
    }

    [Test]
    public void LeHUDDuTagTeam_DitSonEquipeEtSonTour()
    {
        Assert.AreEqual("ÉQUIPE A · TU TE BATS · ENSUITE BRAVO", GameHud.TeamStatus(PlayerTeam.A, true, 0, "ALPHA", "Bravo"));
        Assert.AreEqual("ÉQUIPE A · TU TE BATS", GameHud.TeamStatus(PlayerTeam.A, true, 0, "ALPHA", null), "Seul dans son équipe.");
        Assert.AreEqual("ÉQUIPE B · TU ENTRES QUAND ALPHA TOMBE", GameHud.TeamStatus(PlayerTeam.B, false, 1, "Alpha", null));
        Assert.AreEqual("ÉQUIPE B · TU ENTRES AU 2E RELAIS", GameHud.TeamStatus(PlayerTeam.B, false, 2, "Alpha", null));
        Assert.AreEqual("ÉQUIPE A CONTRE ÉQUIPE B", GameHud.TeamStatus(PlayerTeam.None, false, 0, null, null), "Vu d'ailleurs.");
        StringAssert.Contains("si tout le monde la choisit", MatchEndScreen.CountdownText(false, RoundManager.MatchDecision.Pending, "Revanche", 9, teams: true),
                              "Au Tag Team, la revanche demande la voix de tous.");
    }

    private static SalonView.Row Row(List<SalonView.Row> rows, string label)
    {
        foreach (SalonView.Row row in rows)
        {
            if (row.label == label) return row;
        }
        Assert.Fail($"Pas de rangée « {label} ».");
        return default;
    }
}
