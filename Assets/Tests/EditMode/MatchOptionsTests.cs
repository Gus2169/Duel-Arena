using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Les options de la partie (GDD § 11, 2026-11-03) : le choix de l'arme (libre, pile ou face, arsenal commun), un réglage
/// à trois valeurs qui ne vaut que dans les modes à manches où l'arme est libre, et Tête seulement, une case de tous les
/// modes. Leurs règles sont des fonctions pures : le tirage de l'arsenal commun (CommonArsenal), la pièce et les familles
/// de pile ou face (CoinFlip), la zone qui blesse (WeaponController.Hurts), et ce que l'interface en dit.
/// </summary>
public class MatchOptionsTests
{
    private const ulong Faucon = 1, Rivet = 2;

    private static IReadOnlyList<WeaponData> Loadout()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_ProjectArena/PlayerPrefab/Player.prefab");
        IReadOnlyList<WeaponData> loadout = player.GetComponentInChildren<WeaponController>(true).Loadout;
        Assert.AreEqual(WeaponController.MaxSlots, loadout.Count, "Les douze armes de l'arsenal.");
        return loadout;
    }

    // ------------------------------------------------------------------ Les réglages

    /// <summary>Le choix de l'arme et Tête seulement voyagent dans les réglages du salon (dixième et onzième champs) ; un
    /// salon d'avant les joue libres et sans la case. Vu échouer, le dixième champ oublié à la relecture.</summary>
    [Test]
    public void LesOptions_VoyagentDansLesReglagesDuSalon()
    {
        var rules = new MatchRules { capacity = 2, mode = GameModeId.Classic, roundsToWin = MatchRules.Bo3RoundsToWin,
                                     weaponChoice = WeaponChoice.CoinFlip, headshotsOnly = true };
        MatchRules back = OnlineSession.ParseRules(OnlineSession.FormatRules(rules));
        Assert.AreEqual(WeaponChoice.CoinFlip, back.weaponChoice);
        Assert.IsTrue(back.headshotsOnly);
        rules.weaponChoice = WeaponChoice.CommonArsenal;
        Assert.AreEqual(WeaponChoice.CommonArsenal, OnlineSession.ParseRules(OnlineSession.FormatRules(rules)).weaponChoice);

        MatchRules old = OnlineSession.ParseRules("2;1;0;3;0;0;0;0;10");
        Assert.AreEqual(WeaponChoice.Free, old.weaponChoice, "Un salon d'avant les options : l'arme libre.");
        Assert.IsFalse(old.headshotsOnly);
        Assert.AreEqual(WeaponChoice.Free, new MatchRules { weaponChoice = (WeaponChoice)9 }.Sanitized().weaponChoice, "Un choix inconnu (une autre version).");
    }

    /// <summary>Le salon de l'arène se refait quand un réglage change : chaque champ de MatchRules change son empreinte
    /// (Fingerprint), un champ ajouté plus tard compris. Vu échouer, les options oubliées : le salon gardait le choix de
    /// l'arme du match d'avant (vu à deux instances le 2026-11-03).</summary>
    [Test]
    public void ChaqueReglage_ChangeLEmpreinteDuSalon()
    {
        MatchRules start = GameModeCatalog.RulesFor(GameModeId.Classic);
        int fields = 0;
        foreach (System.Reflection.FieldInfo field in typeof(MatchRules).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            object boxed = start;
            object other = field.GetValue(boxed) switch
            {
                bool b => !b,
                byte b => (byte)(b + 1),
                System.Enum e => System.Enum.ToObject(field.FieldType, System.Convert.ToInt32(e) + 1),
                _ => null,
            };
            Assert.IsNotNull(other, $"{field.Name} : un type de réglage que le test ne sait pas changer.");
            field.SetValue(boxed, other);
            Assert.AreNotEqual(start.Fingerprint(), ((MatchRules)boxed).Fingerprint(), $"{field.Name} ne change pas l'empreinte.");
            fields++;
        }
        Assert.GreaterOrEqual(fields, 11, "Garde : tous les réglages passés en revue.");
    }

    /// <summary>Pile ou face et l'arsenal commun ne valent que dans les modes à manches où l'arme est libre : le classique
    /// (pas son Death match) et le Tournoi. Ailleurs, le réglage est retenu mais sans effet, et le salon les montre
    /// éteints. Vu échouer, le choix appliqué à tous les modes.</summary>
    [Test]
    public void LeChoixDeLArme_NeVautQuAuxModesAManchesOuLArmeEstLibre()
    {
        foreach (GameModeId mode in new[] { GameModeId.Classic, GameModeId.Tournament })
        {
            MatchRules r = GameModeCatalog.RulesFor(mode);
            r.weaponChoice = WeaponChoice.CoinFlip;
            Assert.AreEqual(WeaponChoice.CoinFlip, r.Sanitized().EffectiveWeaponChoice, mode.ToString());
        }
        foreach (GameModeId mode in new[] { GameModeId.GoldenGun, GameModeId.GunGame, GameModeId.KillConfirmed, GameModeId.Zone, GameModeId.TagTeam })
        {
            MatchRules r = GameModeCatalog.RulesFor(mode);
            r.weaponChoice = WeaponChoice.CommonArsenal;
            Assert.AreEqual(WeaponChoice.CommonArsenal, r.Sanitized().weaponChoice, $"{mode} : le réglage est retenu.");
            Assert.AreEqual(WeaponChoice.Free, r.Sanitized().EffectiveWeaponChoice, $"{mode} : sans effet.");
        }
        var deathMatch = new MatchRules { mode = GameModeId.Classic, deathMatch = true, weaponChoice = WeaponChoice.CoinFlip };
        Assert.AreEqual(WeaponChoice.Free, deathMatch.Sanitized().EffectiveWeaponChoice, "Le Death match se joue d'une traite : pas de manches.");

        // Le salon : les trois au classique, éteints (sans cadenas) ailleurs.
        SalonView.Row classic = RowNamed(SalonView.EssentialRows(GameModeCatalog.RulesFor(GameModeId.Classic)), "L'arme");
        Assert.IsTrue(classic.choices[1].Enabled && classic.choices[2].Enabled);
        SalonView.Row zone = RowNamed(SalonView.EssentialRows(GameModeCatalog.RulesFor(GameModeId.Zone)), "L'arme");
        Assert.IsTrue(zone.choices[0].Enabled, "Libre, toujours.");
        Assert.IsTrue(zone.choices[1].unavailable && !zone.choices[1].locked, "Pile ou face : pas dans ce mode.");
        Assert.IsTrue(zone.choices[2].unavailable && !zone.choices[2].locked);

        // L'arsenal commun n'a pas de préparation (l'arme arrive avec le décompte) ; pile ou face en a une.
        MatchRules common = GameModeCatalog.RulesFor(GameModeId.Classic);
        common.weaponChoice = WeaponChoice.CommonArsenal;
        Assert.IsFalse(RoundManager.HasPreparation(common));
        common.weaponChoice = WeaponChoice.CoinFlip;
        Assert.IsTrue(RoundManager.HasPreparation(common));
        MatchRules zoneCommon = GameModeCatalog.RulesFor(GameModeId.Zone);
        zoneCommon.weaponChoice = WeaponChoice.CommonArsenal;
        Assert.IsTrue(RoundManager.HasPreparation(zoneCommon), "Sans effet à la Zone : la préparation reste.");

        // Le résumé de la partie.
        MatchRules summary = GameModeCatalog.RulesFor(GameModeId.Classic);
        summary.weaponChoice = WeaponChoice.CoinFlip;
        summary.headshotsOnly = true;
        StringAssert.EndsWith("pile ou face · tête seulement", GameModeCatalog.Summary(GameModeId.Classic, summary));
        summary.weaponChoice = WeaponChoice.CommonArsenal;
        StringAssert.Contains("arsenal commun", GameModeCatalog.Summary(GameModeId.Classic, summary));
    }

    private static SalonView.Row RowNamed(List<SalonView.Row> rows, string label)
    {
        foreach (SalonView.Row row in rows)
        {
            if (row.label == label) return row;
        }
        Assert.Fail($"Pas de rangée « {label} ».");
        return default;
    }

    // ------------------------------------------------------------------ Tête seulement

    /// <summary>Tête seulement : seul un impact à la tête blesse ; sans la case, toutes les zones. Une case dans tous les
    /// modes, sous « Plus de réglages ». Vu échouer, la case sans effet sur le tir.</summary>
    [Test]
    public void TeteSeulement_SeuleLaTeteBlesse_UneCaseDeTousLesModes()
    {
        Assert.IsTrue(WeaponController.Hurts(HitboxZoneType.Head, true));
        Assert.IsFalse(WeaponController.Hurts(HitboxZoneType.Torso, true));
        Assert.IsFalse(WeaponController.Hurts(HitboxZoneType.Legs, true));
        Assert.IsTrue(WeaponController.Hurts(HitboxZoneType.Torso, false));
        Assert.IsTrue(WeaponController.Hurts(HitboxZoneType.Legs, false));

        foreach (GameModeId mode in new[] { GameModeId.Classic, GameModeId.GoldenGun, GameModeId.GunGame, GameModeId.KillConfirmed,
                                            GameModeId.Zone, GameModeId.TagTeam, GameModeId.Tournament })
        {
            MatchRules r = GameModeCatalog.RulesFor(mode);
            SalonView.Row row = RowNamed(SalonView.AdvancedRows(r, 2), "Tête seulement");
            Assert.IsTrue(row.isCheck && !row.check, $"{mode} : une case, décochée d'abord.");
            Assert.IsTrue(row.setCheck(r, true).headshotsOnly, $"{mode} : la cocher.");
        }
        Assert.AreEqual(" · tête seulement", GameHud.RoundExtras(WeaponChoice.Free, null, CoinFlip.NoFamily, true), "Le décompte le rappelle.");
    }

    // ------------------------------------------------------------------ L'arsenal commun

    /// <summary>L'arsenal commun : une nouvelle arme aux manches 1, 3, 5…, tirée parmi les douze, jamais deux fois de suite
    /// la même ; ses accessoires tirés parmi ce qu'elle prend, sur sa seule place ; les accessoires du joueur pour cette
    /// arme remplacés, ceux des autres gardés. Vu échouer, la même arme tirée deux fois de suite.</summary>
    [Test]
    public void LArsenalCommun_UneArmeAuSortToutesLesDeuxManches_AvecSesAccessoires()
    {
        Assert.IsTrue(CommonArsenal.DrawsAt(1) && CommonArsenal.DrawsAt(3) && CommonArsenal.DrawsAt(5));
        Assert.IsFalse(CommonArsenal.DrawsAt(2) || CommonArsenal.DrawsAt(4));

        IReadOnlyList<WeaponData> loadout = Loadout();
        var random = new System.Random(7);
        var drawn = new HashSet<int>();
        int previous = -1;
        for (int i = 0; i < 400; i++)
        {
            int slot = CommonArsenal.DrawSlot(loadout, previous, random);
            Assert.AreNotEqual(previous, slot, "Jamais deux fois de suite la même arme.");
            Assert.IsNotNull(loadout[slot]);
            drawn.Add(slot);
            previous = slot;
        }
        Assert.AreEqual(loadout.Count, drawn.Count, "Toutes les armes peuvent sortir.");

        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            var seen = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                WeaponAttachments a = CommonArsenal.DrawAttachments(data, slot, random);
                foreach (AttachmentCategory category in WeaponAttachments.Categories)
                {
                    int variant = a.Variant(slot, category);
                    Assert.IsTrue(variant == 0 || WeaponAttachments.Accepts(data, category, variant),
                                  $"{data.weaponName} : {category} {variant} n'est pas pour elle.");
                    seen.Add($"{category}:{variant}");
                    int other = (slot + 1) % loadout.Count;
                    Assert.AreEqual(0, a.Variant(other, category), "Seule la place de l'arme tirée est posée.");
                }
            }
            Assert.Greater(seen.Count, WeaponAttachments.Categories.Length, $"{data.weaponName} : des accessoires variés.");
        }

        WeaponAttachments mine = WeaponAttachments.None.WithSight(0, 3).WithSight(4, 5).WithLaser(4, true);
        WeaponAttachments common = WeaponAttachments.None.WithSight(4, 9).WithGrip(4, GripKind.Vertical);
        WeaponAttachments mixed = CommonArsenal.WithSlotFrom(mine, common, 4);
        Assert.AreEqual(9, mixed.Sight(4), "L'arme tirée a les accessoires du tirage.");
        Assert.AreEqual(GripKind.Vertical, mixed.Grip(4));
        Assert.IsFalse(mixed.Laser(4), "… tous : ce que le tirage n'a pas, elle ne l'a pas.");
        Assert.AreEqual(3, mixed.Sight(0), "Les autres armes gardent ceux du joueur.");
    }

    // ------------------------------------------------------------------ Pile ou face

    /// <summary>Pile ou face : la pièce désigne celui qui choisit à la première manche (et après une manche nulle), puis le
    /// perdant ; les 10 s du choix comptées une fois la pièce tombée ; jamais deux fois de suite la même famille ; sans
    /// choix, une des quatre familles au sort, jamais le gant ; seules les armes de la famille se prennent. Vu échouer, le
    /// vainqueur désigné à la place du perdant, et les 10 s qui partaient avec la pièce.</summary>
    [Test]
    public void PileOuFace_LaPiecePuisLePerdant_JamaisDeuxFoisLaMemeFamille()
    {
        var random = new System.Random(3);
        var chosen = new HashSet<ulong>();
        for (int i = 0; i < 50; i++)
        {
            chosen.Add(CoinFlip.Chooser(Faucon, Rivet, 1, RoundManager.NoWinner, random, out bool tossed));
            Assert.IsTrue(tossed, "La première manche : la pièce.");
        }
        CollectionAssert.AreEquivalent(new[] { Faucon, Rivet }, chosen, "La pièce désigne l'un ou l'autre.");
        Assert.AreEqual(Rivet, CoinFlip.Chooser(Faucon, Rivet, 2, Faucon, random, out bool afterLoss), "Faucon a gagné : Rivet choisit.");
        Assert.IsFalse(afterLoss);
        Assert.AreEqual(Faucon, CoinFlip.Chooser(Faucon, Rivet, 3, Rivet, random, out _));
        CoinFlip.Chooser(Faucon, Rivet, 4, RoundManager.NoWinner, random, out bool afterDraw);
        Assert.IsTrue(afterDraw, "Une manche sans vainqueur : la pièce.");
        Assert.AreEqual(100d + CoinFlip.TossSeconds + CoinFlip.FamilyChoiceSeconds, CoinFlip.FamilyDeadline(100d, true), 1e-6,
                        "Les 10 s du choix partent quand la pièce est tombée.");
        Assert.AreEqual(100d + CoinFlip.FamilyChoiceSeconds, CoinFlip.FamilyDeadline(100d, false), 1e-6, "Sans pièce, tout de suite.");

        Assert.IsFalse(CoinFlip.Allowed((byte)WeaponFamily.Precision, (byte)WeaponFamily.Precision), "Pas deux fois de suite la même famille.");
        Assert.IsTrue(CoinFlip.Allowed((byte)WeaponFamily.Shotgun, (byte)WeaponFamily.Precision));
        Assert.IsTrue(CoinFlip.Allowed(CoinFlip.GloveFamily, CoinFlip.NoFamily), "Le gant seul, le cinquième choix.");
        Assert.IsFalse(CoinFlip.Allowed(CoinFlip.GloveFamily, CoinFlip.GloveFamily));
        Assert.IsFalse(CoinFlip.Allowed((byte)(CoinFlip.GloveFamily + 1), CoinFlip.NoFamily), "Rien d'autre.");

        var random2 = new System.Random(11);
        var families = new HashSet<byte>();
        for (int i = 0; i < 100; i++)
        {
            byte family = CoinFlip.RandomFamily((byte)WeaponFamily.Pistol, random2);
            Assert.AreNotEqual((byte)WeaponFamily.Pistol, family, "Pas celle d'avant.");
            Assert.AreNotEqual(CoinFlip.GloveFamily, family, "Jamais le gant au sort.");
            families.Add(family);
        }
        Assert.AreEqual(3, families.Count, "Les trois autres.");

        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (WeaponData data in loadout)
        {
            Assert.AreEqual(true, CoinFlip.SlotAllowed(data, (byte)data.family), data.weaponName);
            Assert.IsFalse(CoinFlip.SlotAllowed(data, (byte)(((int)data.family + 1) % Arsenal.FamilyCount)), $"{data.weaponName} : pas dans une autre famille.");
            Assert.IsFalse(CoinFlip.SlotAllowed(data, CoinFlip.NoFamily), "Pas avant que la famille soit choisie.");
            Assert.IsFalse(CoinFlip.SlotAllowed(data, CoinFlip.GloveFamily), "Au gant seul, aucune arme.");
        }
    }

    /// <summary>Ce que montrent la préparation et le décompte : la pièce qui tourne, qui choisit et pourquoi, la famille des
    /// deux armes ou le gant seul ; au décompte, l'arme de l'arsenal commun ou la famille. Vu échouer, la manche perdue
    /// annoncée comme la pièce.</summary>
    [Test]
    public void LaPreparation_DitLaPieceQuiChoisitEtLaFamille()
    {
        string Name(ulong id) => id == Faucon ? "Faucon" : "Rivet";
        var coin = new CoinFlip.State { chooser = Faucon, family = CoinFlip.NoFamily, previous = CoinFlip.NoFamily };
        Assert.AreEqual("PILE…", LoadoutPanel.CoinLine(coin, Faucon, 0.05f, true, Name));
        Assert.AreEqual("FACE…", LoadoutPanel.CoinLine(coin, Faucon, 0.2f, true, Name));
        Assert.AreEqual("La pièce t'a désigné : choisis la famille des deux armes.", LoadoutPanel.CoinLine(coin, Faucon, 2f, true, Name));
        Assert.AreEqual("La pièce a désigné Faucon, qui choisit la famille…", LoadoutPanel.CoinLine(coin, Rivet, 2f, true, Name));
        Assert.AreEqual("Tu as perdu la manche : choisis la famille des deux armes.", LoadoutPanel.CoinLine(coin, Faucon, -1f, true, Name));
        Assert.AreEqual("Faucon a perdu la manche et choisit la famille…", LoadoutPanel.CoinLine(coin, Rivet, -1f, true, Name));
        coin.family = (byte)WeaponFamily.Precision;
        Assert.AreEqual("Fusil de précision pour les deux : choisis ton arme dedans.", LoadoutPanel.CoinLine(coin, Rivet, -1f, false, Name));
        coin.family = CoinFlip.GloveFamily;
        Assert.AreEqual("Le gant seul : une manche à mains nues.", LoadoutPanel.CoinLine(coin, Rivet, -1f, false, Name));

        Assert.AreEqual(" · FA-30 pour les deux", GameHud.RoundExtras(WeaponChoice.CommonArsenal, "FA-30", CoinFlip.NoFamily, false));
        Assert.AreEqual(" · fusil à pompe", GameHud.RoundExtras(WeaponChoice.CoinFlip, null, (byte)WeaponFamily.Shotgun, false));
        Assert.AreEqual(" · le gant seul · tête seulement", GameHud.RoundExtras(WeaponChoice.CoinFlip, null, CoinFlip.GloveFamily, true));
        Assert.AreEqual("", GameHud.RoundExtras(WeaponChoice.Free, null, CoinFlip.NoFamily, false));
    }
}
