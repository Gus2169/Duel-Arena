using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Les accessoires des armes (2026-10-16, GDD § 6) : les quinze viseurs (ceux de Rainbow Six refaits, et
/// quatre lunettes pour les armes de précision, 2026-10-28), la poignée verticale ou inclinée, le laser
/// tactique et le chargeur grande capacité, choisis à l'armurerie, annoncés en se connectant, bornés par le
/// serveur, et qui CHANGENT l'arme.
///
/// Ce qui casse ici ne lève aucune erreur : un serveur qui laisse un client modifié poser un point rouge
/// sur le sniper, une lunette sur un fusil d'assaut ou une poignée sur un pistolet, un chargeur que le
/// joueur et le serveur ne comptent pas pareil, une ancienne build lue de travers, une poignée qui retient
/// le mauvais recul, un viseur rangé à la place d'un autre, une hausse qui reste dans la fenêtre du viseur,
/// la lunette d'origine qui reste sous une lunette d'accessoire, un chargeur d'origine qui dépasse sous le
/// grand, un viseur qui n'a pas la distance de l'œil mesurée dans Blender.
/// </summary>
public class WeaponAttachmentsTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Rifle = 0, Sniper = 1, Shotgun = 2, Pistol = 3;
    private const int Carbine = 6, ChargedRifle = 7;   // le FP-30 et le FX-2 : avec le FP-12, les armes de précision
    private const int RedDotA = 1, HoloA = 4, ReflexA = 8, ReflexC = 10, ReflexD = 11, ScopeL2A = 12, ScopeL3A = 15;

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    /// <summary>Les deux ont-ils les mêmes accessoires pour l'arme de la place `slot` ?</summary>
    private static bool SameSlot(WeaponAttachments a, WeaponAttachments b, int slot)
        => a.Sight(slot) == b.Sight(slot) && a.Grip(slot) == b.Grip(slot) && a.Laser(slot) == b.Laser(slot)
           && a.ExtendedMagazine(slot) == b.ExtendedMagazine(slot);

    private static AttachmentCatalog Catalog() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentImporter.CatalogPath);

    private static GameObject ModelPrefab(string code) =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponImporter.Root}/{code}/{code}.prefab");

    private Transform Instantiate(string code)
    {
        GameObject prefab = ModelPrefab(code);
        Assert.IsNotNull(prefab, $"Le modèle {code} n'est plus là.");
        var go = Object.Instantiate(prefab);
        created.Add(go);
        return go.transform;
    }

    private static bool Shown(Transform weapon, string part)
    {
        Transform t = weapon.Find(part);
        Assert.IsNotNull(t, $"{weapon.name} n'a plus de pièce « {part} ».");
        return t.gameObject.activeSelf;
    }

    // ------------------------------------------------------------------
    // Le choix, le serveur, la connexion
    // ------------------------------------------------------------------

    [Test]
    public void LesAccessoires_GardentUnChoixParRubriqueEtParArme_SansToucherAuxAutres()
    {
        WeaponAttachments a = WeaponAttachments.None;
        for (int slot = 0; slot < WeaponAttachments.Slots; slot++)
        {
            Assert.AreEqual(0, a.Sight(slot), "Sans choix : la mire de l'arme.");
            Assert.AreEqual(GripKind.None, a.Grip(slot));
            Assert.IsFalse(a.Laser(slot));
            Assert.IsFalse(a.ExtendedMagazine(slot));
        }

        a = a.With(Rifle, HoloA, GripKind.Vertical, true, true).With(Pistol, ReflexC, GripKind.None, false, true)
             .WithGrip(Sniper, GripKind.Angled);
        Assert.AreEqual(HoloA, a.Sight(Rifle));
        Assert.AreEqual(GripKind.Vertical, a.Grip(Rifle));
        Assert.IsTrue(a.Laser(Rifle));
        Assert.IsTrue(a.ExtendedMagazine(Rifle));
        Assert.AreEqual(ReflexC, a.Sight(Pistol), "Le dixième viseur tient dans ses quatre bits.");
        Assert.IsFalse(a.Laser(Pistol));
        Assert.IsTrue(a.ExtendedMagazine(Pistol));
        Assert.AreEqual(GripKind.Angled, a.Grip(Sniper));
        Assert.AreEqual(0, a.Sight(Sniper), "Une rubrique change sans toucher aux autres.");
        WeaponAttachments last = a.WithSight(Sniper, ScopeL3A);
        Assert.AreEqual(ScopeL3A, last.Sight(Sniper), "Le quinzième viseur remplit ses quatre bits...");
        Assert.AreEqual(GripKind.Angled, last.Grip(Sniper), "... sans déborder sur la poignée.");
        Assert.AreEqual(0, last.Sight(Shotgun), "... ni sur l'arme suivante.");
        Assert.AreEqual(0, a.Sight(Shotgun), "La place 3 n'a rien reçu.");
        Assert.AreEqual(GripKind.None, a.Grip(Shotgun));
        Assert.IsFalse(a.Laser(Shotgun) || a.ExtendedMagazine(Shotgun));

        // Les douze armes ont chacune les leurs (96 bits depuis la refonte du 2026-10-26) : la dernière
        // ne déborde pas sur les autres.
        WeaponAttachments far = a.With(11, ReflexC, GripKind.Vertical, true, true).With(8, HoloA, GripKind.Angled, false, true);
        Assert.AreEqual(ReflexC, far.Sight(11));
        Assert.AreEqual(GripKind.Vertical, far.Grip(11));
        Assert.IsTrue(far.Laser(11) && far.ExtendedMagazine(11));
        Assert.AreEqual(HoloA, far.Sight(8), "La neuvième arme, la première de la seconde moitié.");
        for (int slot = 0; slot < 8; slot++) Assert.IsTrue(SameSlot(a, far, slot), $"Place {slot + 1} : intacte.");
        Assert.IsTrue(WeaponAttachments.TryParseHex(far.ToHex(), out WeaponAttachments reread));
        Assert.AreEqual(far, reread, "Écrits puis relus en hexadécimal, tels quels.");

        // Une rubrique à la fois, par les choix de l'armurerie : les autres restent.
        WeaponAttachments b = a.WithVariant(Rifle, AttachmentCategory.Sight, RedDotA).WithVariant(Rifle, AttachmentCategory.Laser, 0);
        Assert.AreEqual(RedDotA, b.Variant(Rifle, AttachmentCategory.Sight));
        Assert.AreEqual(0, b.Variant(Rifle, AttachmentCategory.Laser));
        Assert.AreEqual((int)GripKind.Vertical, b.Variant(Rifle, AttachmentCategory.Grip));
        Assert.AreEqual(1, b.Variant(Rifle, AttachmentCategory.Magazine));
        for (int slot = 1; slot < WeaponAttachments.Slots; slot++) Assert.IsTrue(SameSlot(a, b, slot), $"Place {slot + 1} : les autres armes n'ont pas bougé.");

        // Hors de l'arsenal : rien ne bouge.
        Assert.AreEqual(a, a.With(WeaponAttachments.Slots, HoloA, GripKind.Angled, true, true));
        Assert.AreEqual(0, a.Sight(-1));
        Assert.AreEqual(WeaponAttachments.SightCount + 1, WeaponAttachments.VariantCount(AttachmentCategory.Sight),
            "Quinze viseurs, plus la mire de l'arme.");
    }

    /// <summary>Le serveur ne publie que ce que l'arme prend : un client modifié qui demande un point rouge
    /// sur le sniper, une lunette sur le fusil d'assaut, un point rouge ou le réflex à grand cadre sur un
    /// pistolet, une poignée sur un pistolet ou un accessoire qui n'existe pas n'a que ce qu'il pouvait avoir.</summary>
    [Test]
    public void LeServeur_FaitTomberCeQueLArmeNePrendPas()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        var asked = WeaponAttachments.None
            .With(Rifle, HoloA, GripKind.Angled, true, true)
            .With(Sniper, RedDotA, GripKind.Vertical, true, true)
            .With(Shotgun, ReflexA, GripKind.Vertical, true, true)
            .With(Pistol, RedDotA, GripKind.Vertical, true, true);
        WeaponAttachments kept = asked.Sanitized(loadout);

        Assert.IsTrue(SameSlot(asked, kept, Rifle), "Le fusil d'assaut prend tout.");
        Assert.AreEqual(0, kept.Sight(Sniper), "Le sniper ne prend qu'une lunette : pas de point rouge, il garde la sienne.");
        Assert.AreEqual(ScopeL2A, asked.WithSight(Sniper, ScopeL2A).Sanitized(loadout).Sight(Sniper), "Une lunette, si.");
        Assert.AreEqual(0, asked.WithSight(Rifle, ScopeL2A).Sanitized(loadout).Sight(Rifle), "Une lunette ne va pas sur le fusil d'assaut.");
        Assert.AreEqual(ReflexD, asked.WithSight(Rifle, ReflexD).Sanitized(loadout).Sight(Rifle), "Le réflex à grand cadre, si.");
        Assert.AreEqual(0, asked.WithSight(Pistol, ReflexD).Sanitized(loadout).Sight(Pistol), "Le grand cadre ne tient pas sur une glissière de pistolet.");
        Assert.AreEqual(GripKind.Vertical, kept.Grip(Sniper), "Mais il prend une poignée...");
        Assert.IsTrue(kept.Laser(Sniper), "... un laser...");
        Assert.IsTrue(kept.ExtendedMagazine(Sniper), "... et le chargeur.");
        Assert.IsTrue(SameSlot(asked, kept, Shotgun), "Le fusil à pompe prend tout.");
        Assert.AreEqual(0, kept.Sight(Pistol), "Un point rouge ne tient pas sur une glissière de pistolet.");
        Assert.AreEqual(GripKind.None, kept.Grip(Pistol), "Un pistolet n'a pas de poignée avant.");
        Assert.IsTrue(kept.Laser(Pistol));
        Assert.IsTrue(kept.ExtendedMagazine(Pistol));

        var reflex = WeaponAttachments.None.WithSight(Pistol, ReflexA);
        Assert.AreEqual(ReflexA, reflex.Sanitized(loadout).Sight(Pistol), "Un réflex, si.");
        Assert.AreEqual(ReflexC, reflex.WithSight(Pistol, ReflexC).Sanitized(loadout).Sight(Pistol));

        // Tous les bits levés : le viseur 15 (la lunette ×3), qui ne reste que sur les armes de précision, et
        // une poignée qui n'existe pas.
        WeaponAttachments all = new WeaponAttachments(ulong.MaxValue, uint.MaxValue).Sanitized(loadout);
        for (int slot = 0; slot < WeaponAttachments.Slots; slot++)
        {
            bool precision = slot == Sniper || slot == Carbine || slot == ChargedRifle;
            Assert.AreEqual(precision ? ScopeL3A : 0, all.Sight(slot),
                $"Place {slot + 1} ({loadout[slot].Designation}) : la lunette ×3 ne va que sur une arme de précision.");
            Assert.AreEqual(GripKind.None, all.Grip(slot), $"Place {slot + 1} : la poignée 3 n'existe pas.");
        }
        Assert.AreEqual(kept, kept.Sanitized(loadout), "Le serveur ne touche pas à un choix valable.");
        Assert.AreEqual(WeaponAttachments.None, asked.Sanitized(null), "Sans panoplie, rien.");
    }

    /// <summary>La partie ARMES de Personnalisation résume chaque arme (2026-10-16) : elle ne montre que
    /// ce que le serveur garderait, avec la désignation de chaque accessoire (le laser de poing sous un
    /// pistolet). Un viseur demandé sur le sniper n'y figure pas : en partie, il n'y serait pas.</summary>
    [Test]
    public void LeResumeDePersonnalisation_NeMontreQueCeQueLArmePorte()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        AttachmentCatalog catalog = Catalog();
        string Code(GameObject prefab) => prefab.GetComponent<WeaponAttachment>().Code;
        var asked = WeaponAttachments.None
            .With(Rifle, HoloA, GripKind.Vertical, true, true)
            .With(Sniper, RedDotA, GripKind.None, false, false)
            .With(Pistol, ReflexA, GripKind.Vertical, true, false);
        WeaponLooks looks = WeaponLooks.Default.WithFinish(Rifle, WeaponFinish.Rouillee);

        WeaponCustomizationController.Summarize(loadout[Rifle], Rifle, looks, asked, out string model, out string details);
        StringAssert.StartsWith(loadout[Rifle].Designation, model, "La désignation de l'arme d'abord.");
        Assert.AreEqual($"Rouillée · {Code(catalog.SightPrefab(HoloA))} · {Code(catalog.verticalGrip)} · {Code(catalog.laser)} · "
                        + $"chargeur {AttachmentCatalog.MagazineSize(loadout[Rifle], true, catalog)}", details);

        WeaponCustomizationController.Summarize(loadout[Sniper], Sniper, looks, asked, out _, out details);
        Assert.AreEqual("D'origine · sans accessoire", details, "Le sniper garde sa lunette : le point rouge demandé n'y est pas.");

        WeaponCustomizationController.Summarize(loadout[Pistol], Pistol, looks, asked, out _, out details);
        Assert.AreEqual($"D'origine · {Code(catalog.SightPrefab(ReflexA))} · {Code(catalog.pistolLaser)}", details,
                        "Un pistolet : le réflex et le laser de poing, pas de poignée.");
    }

    [Test]
    public void LaDemandeDeConnexion_PorteLesAccessoires()
    {
        WeaponLooks looks = WeaponLooks.Default.WithFinish(0, WeaponFinish.Polie);
        WeaponAttachments attachments = WeaponAttachments.None.With(Rifle, HoloA, GripKind.Vertical, true, false)
                                                              .With(Pistol, ReflexC, GripKind.None, true, true)
                                                              .With(11, ReflexA, GripKind.None, true, false);
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.1.4", "Héloïse", looks, attachments),
                                               out string version, out string name, out WeaponLooks readLooks, out WeaponAttachments read));
        Assert.AreEqual("0.1.4", version);
        Assert.AreEqual("Héloïse", name);
        Assert.AreEqual(looks, readLooks, "L'apparence se lit toujours.");
        Assert.AreEqual(attachments, read, "Les accessoires arrivent tels qu'ils sont partis.");

        // Une build d'avant les accessoires (0.1.3) : la version, le pseudo et l'apparence. Aucun accessoire.
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.1.3", "Untel", looks), out _, out _, out readLooks, out read));
        Assert.AreEqual(looks, readLooks);
        Assert.AreEqual(WeaponAttachments.None, read);
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.4\nUntel\n00000000\nZZ-pas-hexa"),
                                               out _, out _, out _, out read));
        Assert.AreEqual(WeaponAttachments.None, read, "N'importe quoi à la place des accessoires : aucun.");
    }

    /// <summary>Comme le pseudo et l'apparence : deux instances du jeu sur un PC partagent la clé du
    /// registre ; chacune garde ses accessoires.</summary>
    [Test]
    public void LesAccessoires_RestentCeuxDeCetteInstance_QuandUneAutreEnregistreLesSiens()
    {
        WeaponAttachments before = GameSettings.WeaponAttachments;
        string stored = PlayerPrefs.GetString(GameSettings.WeaponAttachmentsKey, "");
        try
        {
            WeaponAttachments mine = WeaponAttachments.None.With(Rifle, HoloA, GripKind.Vertical, true, true);
            GameSettings.WeaponAttachments = mine;
            PlayerPrefs.SetString(GameSettings.WeaponAttachmentsKey, WeaponAttachments.None.WithSight(Rifle, RedDotA).ToHex());
            Assert.AreEqual(mine, GameSettings.WeaponAttachments, "L'autre instance a écrit les siens : celle-ci garde les siens.");
        }
        finally
        {
            GameSettings.WeaponAttachments = before;
            PlayerPrefs.SetString(GameSettings.WeaponAttachmentsKey, stored);
            PlayerPrefs.Save();
        }
    }

    // ------------------------------------------------------------------
    // Les effets
    // ------------------------------------------------------------------

    [Test]
    public void LeChargeurGrandeCapacite_AjouteLaMoitieDeBalles_EtAllongeUnPeuLeRechargement()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        AttachmentCatalog catalog = Catalog();
        int[] expected = { 45, 8, 9 };   // 30, 5 et 6 balles d'origine (FA-07, FP-12, FAP-03)
        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            // Une arme laser n'a pas de balles : sa cellule grande capacité tient plus de chaleur (WeaponHeatTests).
            if (data.UsesHeat) continue;
            int big = slot < expected.Length ? expected[slot] : Mathf.RoundToInt(data.magazineSize * catalog.extendedMagazineCapacity);
            Assert.AreEqual(data.magazineSize, AttachmentCatalog.MagazineSize(data, false, catalog), $"{data.weaponName} : le chargeur d'origine.");
            Assert.AreEqual(big, AttachmentCatalog.MagazineSize(data, true, catalog), $"{data.weaponName} : le grand chargeur.");
            Assert.AreEqual(data.magazineSize, AttachmentCatalog.MagazineSize(data, true, null), "Sans catalogue, aucun effet.");
            Assert.AreEqual(data.reloadDuration, AttachmentCatalog.ReloadDuration(data, false, catalog), 1e-6f);
        }

        WeaponData rifle = loadout[Rifle];
        Assert.AreEqual(rifle.reloadDuration * catalog.extendedMagazineReload, AttachmentCatalog.ReloadDuration(rifle, true, catalog), 1e-5f,
            "Un chargeur plus long se change moins vite.");
        Assert.Greater(catalog.extendedMagazineReload, 1f);
        WeaponData shotgun = loadout[Shotgun];
        Assert.IsTrue(shotgun.reloadPerRound, "Le fusil à pompe recharge cartouche par cartouche.");
        Assert.AreEqual(shotgun.reloadDuration, AttachmentCatalog.ReloadDuration(shotgun, true, catalog), 1e-6f,
            "Une cartouche se glisse aussi vite : le tube en prend seulement plus.");
    }

    /// <summary>Depuis le tir à la hanche façon Rainbow Six (2026-10-16), le laser ne touche plus au
    /// recul : il resserre la dispersion à la hanche (HipFireSpreadTests).</summary>
    [Test]
    public void LaPoigneeVerticale_RetientLeReculVertical_EtLeLaserResserreLaHanche()
    {
        AttachmentCatalog catalog = Catalog();
        var grip = WeaponAttachments.None.WithGrip(Rifle, GripKind.Vertical);
        AttachmentCatalog.RecoilFactors(catalog, grip, Rifle, out float vertical, out float horizontal);
        Assert.AreEqual(catalog.verticalGripRecoil, vertical, 1e-6f, "La poignée verticale retient le recul vertical...");
        Assert.AreEqual(1f, horizontal, 1e-6f, "... et rien d'autre.");
        Assert.Less(catalog.verticalGripRecoil, 1f);
        Assert.AreEqual(1f, AttachmentCatalog.HipSpreadFactor(catalog, grip, Rifle), 1e-6f, "Elle ne resserre pas le cône.");

        var laser = WeaponAttachments.None.WithLaser(Rifle, true);
        AttachmentCatalog.RecoilFactors(catalog, laser, Rifle, out vertical, out horizontal);
        Assert.AreEqual(1f, vertical, 1e-6f, "Le laser ne retient pas le recul...");
        Assert.AreEqual(1f, horizontal, 1e-6f);
        Assert.AreEqual(catalog.laserHipSpread, AttachmentCatalog.HipSpreadFactor(catalog, laser, Rifle), 1e-6f, "... il resserre la hanche.");
        Assert.Less(catalog.laserHipSpread, 1f);
        Assert.AreEqual(1f, AttachmentCatalog.HipSpreadFactor(catalog, laser, Sniper), 1e-6f, "Le laser d'une arme ne resserre pas une autre.");

        AttachmentCatalog.RecoilFactors(catalog, grip, Sniper, out vertical, out _);
        Assert.AreEqual(1f, vertical, 1e-6f, "La poignée d'une arme ne retient pas le recul d'une autre.");
        AttachmentCatalog.RecoilFactors(null, grip, Rifle, out vertical, out _);
        Assert.AreEqual(1f, vertical, 1e-6f, "Sans catalogue, aucun effet.");
        Assert.AreEqual(1f, AttachmentCatalog.HipSpreadFactor(null, laser, Rifle), 1e-6f);
    }

    [Test]
    public void LaPoigneeInclinee_FaitEpaulerPlusVite()
    {
        AttachmentCatalog catalog = Catalog();
        WeaponData rifle = Loadout()[Rifle];
        Assert.AreEqual(rifle.aimTime, AttachmentCatalog.AimTime(rifle, GripKind.None, catalog), 1e-6f);
        Assert.AreEqual(rifle.aimTime, AttachmentCatalog.AimTime(rifle, GripKind.Vertical, catalog), 1e-6f, "La poignée verticale n'y change rien.");
        Assert.AreEqual(rifle.aimTime * catalog.angledGripAimTime, AttachmentCatalog.AimTime(rifle, GripKind.Angled, catalog), 1e-6f);
        Assert.Less(catalog.angledGripAimTime, 1f);
    }

    // ------------------------------------------------------------------
    // Le catalogue et la panoplie
    // ------------------------------------------------------------------

    /// <summary>Le catalogue range les viseurs dans l'ordre de WeaponAttachments, celui de Rainbow Six,
    /// et chaque viseur dessine le réticule de sa fiche ; chaque arme de la panoplie le porte et ne
    /// prend que ce que dit le GDD (§ 6).</summary>
    [Test]
    public void LeCatalogue_RangeLesViseursDansLOrdre_EtChaqueArmeLePorte()
    {
        AttachmentCatalog catalog = Catalog();
        Assert.IsNotNull(catalog, $"Pas de catalogue en {AttachmentImporter.CatalogPath}.");
        Assert.AreEqual(WeaponAttachments.SightCount, catalog.sights.Length);
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            WeaponAttachment a = catalog.SightPrefab(sight).GetComponent<WeaponAttachment>();
            Assert.AreEqual(AttachmentImporter.SightOrder[sight - 1], a.Code, $"Le viseur {sight} n'est pas à sa place.");
            Assert.AreEqual(AttachmentCategory.Sight, a.Category);
            Assert.IsNotNull(a.Sight, $"{a.Code} n'a pas de mire.");
            Assert.IsNotNull(a.Lens, $"{a.Code} n'a pas de vitre.");
            Material lens = a.Lens.sharedMaterial;
            Assert.AreEqual(AttachmentImporter.LensShaderName, lens.shader.name, $"La vitre de {a.Code} ne dessine pas de réticule.");
            Assert.IsNotNull(lens.GetTexture("_Reticule"), $"La vitre de {a.Code} n'a pas l'image de son réticule.");
            Assert.Greater(lens.GetFloat("_Etendue"), 0f);
            CollectionAssert.IsSubsetOf(new[] { "Hausse", "Guidon", "Lunette_Standard" }, new List<string>(a.Hides),
                $"{a.Code} doit cacher la hausse et le guidon, ou la lunette d'origine d'une arme de précision.");

            // Une lunette grossit, et ne montre que son œilleton dans sa vue grossie ; un viseur, non.
            bool scope = WeaponAttachments.FamilyOf(sight) == SightFamilies.Scope;
            Assert.AreEqual(scope, a.Magnification > 1f, $"{a.Code} : {(scope ? "une lunette sans grossissement" : "un viseur qui grossit")}.");
            Assert.AreEqual(scope, a.Eyepiece.Count > 0, $"{a.Code} : {(scope ? "une lunette sans œilleton" : "un œilleton sur un viseur")}.");
            if (!scope) continue;
            int tube = 0;
            foreach (Renderer r in a.GetComponentsInChildren<Renderer>(true))
                if (!a.ShownWhenScoped(r)) tube++;
            Assert.Greater(tube, 0, $"{a.Code} : rien de son tube ne se cache dans la vue grossie.");
            Assert.IsTrue(a.ShownWhenScoped(a.Lens), $"{a.Code} : sa vitre reste, avec le réticule.");
            Assert.Less(lens.GetColor("_CouleurVitre").a, 0.06f, $"{a.Code} : sa vitre teinte toute la vue grossie, elle voilerait la cible.");
        }
        Assert.AreEqual("PR-A", catalog.SightPrefab(RedDotA).GetComponent<WeaponAttachment>().Code);
        Assert.AreEqual("RX-A", catalog.SightPrefab(ReflexA).GetComponent<WeaponAttachment>().Code);
        Assert.AreEqual(SightFamilies.RedDot, WeaponAttachments.FamilyOf(RedDotA));
        Assert.AreEqual(SightFamilies.Holographic, WeaponAttachments.FamilyOf(HoloA));
        Assert.AreEqual(SightFamilies.Reflex, WeaponAttachments.FamilyOf(ReflexA));
        Assert.AreEqual(SightFamilies.WideReflex, WeaponAttachments.FamilyOf(ReflexD));
        Assert.AreEqual(SightFamilies.Scope, WeaponAttachments.FamilyOf(ScopeL2A));
        Assert.AreEqual(SightFamilies.Scope, WeaponAttachments.FamilyOf(ScopeL3A));
        Assert.AreEqual(2f, catalog.SightPrefab(ScopeL2A).GetComponent<WeaponAttachment>().Magnification, 1e-6f, "La L2-A grossit deux fois.");
        Assert.AreEqual(3f, catalog.SightPrefab(ScopeL3A).GetComponent<WeaponAttachment>().Magnification, 1e-6f, "La L3-A, trois fois.");
        Assert.AreEqual("Mire de l'arme", catalog.SightTitle(0));
        Assert.AreEqual("Mire de l'arme", AttachmentCatalog.BaseSightTitle(Loadout()[Rifle]));
        Assert.AreEqual("Lunette d'origine ×4", AttachmentCatalog.BaseSightTitle(Loadout()[Sniper]),
            "Sans viseur, le sniper a sa lunette : l'armurerie la nomme.");

        Assert.AreEqual(AttachmentCategory.Laser, catalog.laser.GetComponent<WeaponAttachment>().Category);
        Assert.IsNotNull(catalog.laser.GetComponent<WeaponAttachment>().Beam, "Le laser n'a pas d'émetteur.");
        Assert.IsNotNull(catalog.pistolLaser.GetComponent<WeaponAttachment>().Beam, "Le laser de poing n'a pas d'émetteur.");
        Assert.IsNotNull(catalog.verticalGrip.GetComponent<WeaponAttachment>().LeftHand, "La poignée verticale n'a pas de prise.");
        Assert.IsNotNull(catalog.angledGrip.GetComponent<WeaponAttachment>().LeftHand, "La poignée inclinée n'a pas de prise.");
        Assert.IsNotNull(catalog.beamMaterial);
        Assert.IsNotNull(catalog.dotMaterial);

        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (WeaponData data in loadout)
        {
            Assert.AreSame(catalog, data.attachmentCatalog, $"{data.weaponName} ne porte pas le catalogue : ses accessoires n'auraient aucun effet.");
            // Chaque modèle qui a un chargeur a son grand chargeur, sauf le barillet du RV-30.
            WeaponModel m = data.modelPrefab.GetComponent<WeaponModel>();
            GameObject magazine = catalog.MagazinePrefab(m.Designation);
            if (m.Designation == "RV-30") Assert.IsNull(magazine, "Un barillet ne s'allonge pas.");
            else Assert.IsNotNull(magazine, $"{m.Designation} n'a pas de chargeur grande capacité.");
        }
        Assert.AreEqual(AttachmentSlots.All, loadout[Rifle].acceptedAttachments);
        Assert.AreEqual(AttachmentSlots.All, loadout[Sniper].acceptedAttachments, "Le sniper prend tout, sa lunette remplacée par une lunette.");
        Assert.AreEqual(AttachmentSlots.All, loadout[Shotgun].acceptedAttachments);
        Assert.AreEqual(AttachmentSlots.Sight | AttachmentSlots.Laser | AttachmentSlots.Magazine, loadout[Pistol].acceptedAttachments);
        Assert.AreEqual(SightFamilies.Reflex, loadout[Pistol].acceptedSights);
    }

    /// <summary>Qui prend quel viseur (2026-10-28, l'utilisateur) : les lunettes ne vont que sur les trois armes
    /// de précision (FP-12, FX-2 et la carabine FP-30), qui gardent sinon leur lunette d'origine et ne prennent
    /// aucun viseur sans grossissement ; une glissière de pistolet n'a la place que des trois petits réflex ;
    /// les autres prennent les onze viseurs sans grossissement, le réflex à grand cadre compris.</summary>
    [Test]
    public void LesLunettes_NeVontQueSurLesArmesDePrecision()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        int precision = 0, pistols = 0, others = 0;
        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            bool isPrecision = data.Designation is "FP-12" or "FX-2" or "FP-30";
            bool isPistol = data.modelPrefab.GetComponent<WeaponModel>().PistolHold;
            if (isPrecision) precision++; else if (isPistol) pistols++; else others++;
            if (isPrecision) Assert.Greater(data.scopeMagnification, 1f, $"{data.Designation} : une arme de précision a sa lunette d'origine.");
            for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
            {
                SightFamilies family = WeaponAttachments.FamilyOf(sight);
                bool expected = isPrecision ? family == SightFamilies.Scope
                              : isPistol ? family == SightFamilies.Reflex
                              : family != SightFamilies.Scope;
                Assert.AreEqual(expected, WeaponAttachments.Accepts(data, AttachmentCategory.Sight, sight),
                    $"{data.Designation} et le viseur {sight} ({family}).");
            }
        }
        Assert.AreEqual(3, precision, "Trois armes de précision.");
        Assert.AreEqual(3, pistols, "Trois pistolets.");
        Assert.AreEqual(6, others);
    }

    /// <summary>Ce que Blender a mesuré de chaque viseur arrive dans son préfab (AttachmentImporter) : la
    /// distance de l'œil qui donne à tous les viseurs sans grossissement la même fenêtre utile, le nom de son
    /// réticule, et le grossissement d'une lunette. Une distance unique pour tous (0,08 m) fausserait l'équité
    /// des fenêtres voulue par l'utilisateur.</summary>
    [Test]
    public void ChaqueViseur_PrendDeSaFicheLaDistanceDeLOeil_EtSonGrossissement()
    {
        AttachmentCatalog catalog = Catalog();
        var distances = new HashSet<float>();
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            WeaponAttachment a = catalog.SightPrefab(sight).GetComponent<WeaponAttachment>();
            string folder = a.Code.Replace("-", "");
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>($"{AttachmentImporter.Root}/{folder}/{folder}_Finitions.json");
            Assert.IsNotNull(sheet, $"{a.Code} n'a pas de fiche.");
            float eye = Number(sheet.text, "distanceOeil");
            float zoom = Number(sheet.text, "grossissement", 1f);   // absent : sans grossissement
            Assert.AreEqual(eye, a.AimEyeDistance, 1e-5f, $"{a.Code} : la distance de l'œil de sa fiche.");
            Assert.AreEqual(zoom, a.Magnification, 1e-5f, $"{a.Code} : le grossissement de sa fiche.");
            StringAssert.Contains($"\"descriptionReticule\": \"{a.ReticleLabel}\"", sheet.text, $"{a.Code} : le nom du réticule de sa fiche.");
            if (zoom <= 1f) distances.Add(Mathf.Round(eye * 1000f));
        }
        Assert.Greater(distances.Count, 5, "Garde : les viseurs sans grossissement n'ont pas tous la même distance de l'œil.");
    }

    /// <summary>Un nombre de la fiche d'un accessoire (JSON de Blender), par son nom ; `absent` s'il n'y est
    /// pas (NaN : il doit y être).</summary>
    private static float Number(string json, string key, float absent = float.NaN)
    {
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\":\\s*(-?[0-9.]+(?:[eE]-?[0-9]+)?)");
        if (!m.Success && !float.IsNaN(absent)) return absent;
        Assert.IsTrue(m.Success, $"La fiche n'a pas de « {key} ».");
        return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>La mire de chaque viseur passe par sa vitre, devant elle, loin de ses bords : en visée, on
    /// regarde au travers, et le réticule (à l'infini, sur l'axe) tombe dans la fenêtre. Pas forcément au
    /// milieu : la fenêtre du RX-A monte plus haut que l'axe (la refonte du 2026-10-28).</summary>
    [Test]
    public void LaMireDeChaqueViseur_PasseParSaVitre()
    {
        AttachmentCatalog catalog = Catalog();
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            GameObject prefab = catalog.SightPrefab(sight);
            WeaponAttachment a = prefab.GetComponent<WeaponAttachment>();
            Mesh mesh = a.Lens.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * a.Lens.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            var pane = new Bounds(toRoot.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 v in vertices) pane.Encapsulate(toRoot.MultiplyPoint3x4(v));
            Vector3 mire = prefab.transform.InverseTransformPoint(a.Sight.position);

            Assert.Less(Mathf.Abs(mire.x - pane.center.x), 0.001f, $"{a.Code} : la mire n'est pas au milieu de la vitre (horizontalement).");
            Assert.Greater(Mathf.Min(mire.y - pane.min.y, pane.max.y - mire.y), 0.004f,
                $"{a.Code} : la mire passe à moins de 4 mm du bord de la vitre (de {pane.min.y * 1000f:0.0} à {pane.max.y * 1000f:0.0} mm, mire à {mire.y * 1000f:0.0}).");
            Assert.Greater(pane.size.x, 0.012f, $"{a.Code} : une vitre de moins de 12 mm.");
            Assert.Less(mire.z, pane.center.z, $"{a.Code} : la vitre doit être devant la mire.");
            Assert.Less(Quaternion.Angle(Quaternion.identity, a.Sight.localRotation), 0.01f, $"{a.Code} : la mire doit regarder dans l'axe du canon.");
        }
    }

    // ------------------------------------------------------------------
    // Le montage sur l'arme (AttachmentMount)
    // ------------------------------------------------------------------

    /// <summary>La vitre d'un viseur ne fait pas d'ombre, et sur l'arme du corps du joueur (vue de ses
    /// propres yeux, une ombre seule) elle ne se dessine pas : elle flottait devant lui, une tache grise
    /// à l'écran (vu par l'utilisateur le 2026-10-16).</summary>
    [Test]
    public void LaVitreDUnViseur_NeFaitPasDOmbre_EtSEteintSurLeCorpsDuJoueur()
    {
        WeaponData rifle = Loadout()[Rifle];
        Transform own = Instantiate("FA07");
        foreach (Renderer r in own.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        AttachmentMount.Apply(own, rifle, WeaponAttachments.None.WithSight(Rifle, HoloA), Rifle, WeaponFinish.Origine);
        WeaponAttachment ownSight = AttachmentMount.Mounted(own, AttachmentCategory.Sight);
        Assert.IsNotNull(ownSight, "Garde : le holographique est monté.");
        Assert.IsNotNull(ownSight.Lens, "Garde : il a une vitre.");
        int frame = 0;
        foreach (Renderer r in ownSight.GetComponentsInChildren<Renderer>(true))
        {
            if (r == ownSight.Lens) continue;
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, $"Garde : {r.name}, comme l'arme, n'est qu'une ombre.");
            frame++;
        }
        Assert.Greater(frame, 0, "Garde : le viseur a un cadre.");
        Assert.IsTrue(ownSight.Lens.forceRenderingOff, "Sur le corps du joueur, la vitre ne se dessine pas.");
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, ownSight.Lens.shadowCastingMode, "Elle ne fait pas d'ombre.");

        Transform seen = Instantiate("FA07");
        AttachmentMount.Apply(seen, rifle, WeaponAttachments.None.WithSight(Rifle, HoloA), Rifle, WeaponFinish.Origine);
        WeaponAttachment seenSight = AttachmentMount.Mounted(seen, AttachmentCategory.Sight);
        Assert.IsFalse(seenSight.Lens.forceRenderingOff, "Sur une arme qu'on voit, la vitre se voit.");
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, seenSight.Lens.shadowCastingMode, "Toujours sans ombre.");
    }

    [Test]
    public void UnViseurMonte_CacheLesOrganesDeVisee_EtLesRendQuandIlPart()
    {
        WeaponData rifle = Loadout()[Rifle];
        Transform weapon = Instantiate("FA07");
        WeaponModel model = weapon.GetComponent<WeaponModel>();

        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None.WithSight(Rifle, HoloA), Rifle, WeaponFinish.Origine);
        WeaponAttachment sight = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
        Assert.IsNotNull(sight, "Le holographique A n'est pas monté.");
        Assert.AreEqual("HO-A", sight.Code);
        Assert.AreSame(AttachmentMount.Socket(weapon, AttachmentCategory.Sight), sight.transform.parent, "Monté ailleurs que sur le rail.");
        Assert.Less(sight.transform.localPosition.magnitude, 1e-6f, "Le préfab a son origine au point d'accroche.");
        Assert.IsFalse(Shown(weapon, "Hausse"), "La hausse resterait dans la fenêtre du viseur.");
        Assert.IsFalse(Shown(weapon, "Guidon"));
        Assert.IsFalse(Shown(weapon, "Guidon_Base"), "La tour du guidon se voyait sous le réticule.");
        Assert.IsTrue(Shown(weapon, "Chargeur_Standard"), "Un viseur ne touche pas au chargeur.");
        Assert.AreSame(sight.Sight, AttachmentMount.SightOf(model, out float eye), "En visée, l'œil va derrière le viseur...");
        Assert.AreEqual(sight.AimEyeDistance, eye, 1e-6f, "... à sa distance.");

        // Un autre viseur prend sa place, sans doublon.
        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None.WithSight(Rifle, RedDotA), Rifle, WeaponFinish.Origine);
        Assert.AreEqual(1, weapon.GetComponentsInChildren<WeaponAttachment>().Length, "Deux viseurs sur le même rail.");
        Assert.AreEqual("PR-A", AttachmentMount.Mounted(weapon, AttachmentCategory.Sight).Code);

        // Plus de viseur : la mire de l'arme revient.
        AttachmentMount.Apply(weapon, rifle, WeaponAttachments.None, Rifle, WeaponFinish.Origine);
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight));
        Assert.AreEqual(0, weapon.GetComponentsInChildren<WeaponAttachment>(true).Length);
        Assert.IsTrue(Shown(weapon, "Hausse"), "La hausse doit revenir quand le viseur part.");
        Assert.IsTrue(Shown(weapon, "Guidon"));
        Assert.IsTrue(Shown(weapon, "Guidon_Base"));
        Assert.AreSame(model.Sight, AttachmentMount.SightOf(model, out eye));
        Assert.AreEqual(model.AimEyeDistance, eye, 1e-6f);
    }

    [Test]
    public void LeGrandChargeur_RemplaceCeluiDOrigine_SaufLaRallongeDUnTube()
    {
        Transform rifle = Instantiate("FA07");
        AttachmentMount.Apply(rifle, Loadout()[Rifle], WeaponAttachments.None.WithMagazine(Rifle, true), Rifle, WeaponFinish.Origine);
        Assert.AreEqual("CG-FA07", AttachmentMount.Mounted(rifle, AttachmentCategory.Magazine).Code, "Le FA-07 a son grand chargeur.");
        Assert.IsFalse(Shown(rifle, "Chargeur_Standard"), "Le chargeur d'origine dépasserait sous le grand.");
        Assert.IsTrue(Shown(rifle, "Hausse"));

        Transform shotgun = Instantiate("FAP03");
        AttachmentMount.Apply(shotgun, Loadout()[Shotgun], WeaponAttachments.None.WithMagazine(Shotgun, true), Shotgun, WeaponFinish.Origine);
        Assert.AreEqual("CG-FAP03", AttachmentMount.Mounted(shotgun, AttachmentCategory.Magazine).Code);
        Assert.IsTrue(Shown(shotgun, "Tube_Magasin"), "La rallonge s'ajoute au tube, elle ne le remplace pas.");

        // Le chargeur d'un autre modèle ne se monte pas : le FA-30 a le sien.
        Transform other = Instantiate("FA30");
        AttachmentMount.Apply(other, Loadout()[Rifle], WeaponAttachments.None.WithMagazine(Rifle, true), Rifle, WeaponFinish.Origine);
        Assert.AreEqual("CG-FA30", AttachmentMount.Mounted(other, AttachmentCategory.Magazine).Code);
    }

    [Test]
    public void UnPistolet_PrendLeLaserDePoingEtUnReflex_JamaisDePoignee()
    {
        WeaponData pistol = Loadout()[Pistol];
        Transform weapon = Instantiate("FX4");
        // Ce que le serveur n'aurait pas publié (une poignée, un point rouge) ne se monte pas non plus.
        var asked = WeaponAttachments.None.With(Pistol, RedDotA, GripKind.Vertical, true, false);
        AttachmentMount.Apply(weapon, pistol, asked, Pistol, WeaponFinish.Origine);
        Assert.AreEqual("LT-P", AttachmentMount.Mounted(weapon, AttachmentCategory.Laser).Code, "Sous le canon d'un pistolet, le laser de poing.");
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight), "Un point rouge ne tient pas sur la glissière.");
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Grip));
        Assert.IsTrue(Shown(weapon, "Hausse"));

        AttachmentMount.Apply(weapon, pistol, asked.WithSight(Pistol, ReflexA), Pistol, WeaponFinish.Origine);
        WeaponAttachment reflex = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
        Assert.AreEqual("RX-A", reflex.Code);
        Assert.IsFalse(Shown(weapon, "Hausse"));
        Assert.AreSame(reflex.Sight, AttachmentMount.SightOf(weapon.GetComponent<WeaponModel>(), out float eye));
        Assert.AreEqual(weapon.GetComponent<WeaponModel>().AimEyeDistance, eye, 1e-6f, "Un pistolet se vise toujours à bout de bras.");

        // Le barillet du RV-30 n'a pas de modèle de grand chargeur : rien n'est monté, l'effet vaut.
        const int Revolver = 11;
        WeaponData rv30 = Loadout()[Revolver];
        Assert.AreEqual("RV-30", rv30.Designation, "Garde : la douzième arme est le revolver.");
        Transform revolver = Instantiate("RV30");
        AttachmentMount.Apply(revolver, rv30, WeaponAttachments.None.WithMagazine(Revolver, true), Revolver, WeaponFinish.Origine);
        Assert.IsNull(AttachmentMount.Mounted(revolver, AttachmentCategory.Magazine));
        Assert.AreEqual(9, AttachmentCatalog.MagazineSize(rv30, true, rv30.attachmentCatalog), "Six coups, neuf au grand chargeur.");
    }

    [Test]
    public void LeSniper_GardeSaLunette_EtLesAccessoiresPrennentLaFinitionDeLArme()
    {
        WeaponData sniper = Loadout()[Sniper];
        Transform weapon = Instantiate("FP12");
        var asked = WeaponAttachments.None.With(Sniper, HoloA, GripKind.Angled, true, true);
        AttachmentMount.Apply(weapon, sniper, asked, Sniper, WeaponFinish.Rouillee);
        Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight), "Le sniper garde sa lunette.");
        Assert.IsTrue(Shown(weapon, "Lunette_Standard"));
        Assert.AreSame(weapon.GetComponent<WeaponModel>().Sight, AttachmentMount.SightOf(weapon.GetComponent<WeaponModel>(), out _));

        foreach (AttachmentCategory category in new[] { AttachmentCategory.Grip, AttachmentCategory.Laser, AttachmentCategory.Magazine })
        {
            WeaponAttachment a = AttachmentMount.Mounted(weapon, category);
            Assert.IsNotNull(a, $"Le sniper prend {category}.");
            Assert.AreEqual(WeaponFinish.Rouillee, a.GetComponent<WeaponFinishes>().Shown, $"{a.Code} : la finition de l'arme.");
        }
        AttachmentMount.Apply(weapon, sniper, asked, Sniper, WeaponFinish.Polie);
        Assert.AreEqual(WeaponFinish.Polie, AttachmentMount.Mounted(weapon, AttachmentCategory.Laser).GetComponent<WeaponFinishes>().Shown,
            "Une autre finition passe aux accessoires déjà montés.");
        Assert.AreEqual(3, weapon.GetComponentsInChildren<WeaponAttachment>().Length, "Rien n'est monté en double.");
    }

    /// <summary>Une lunette d'accessoire prend la place de la lunette d'origine de chaque arme de précision
    /// (2026-10-28) : celle-ci se cache (elle serait dans le tube de la nouvelle), l'œil va derrière l'œilleton
    /// de la nouvelle, et la lunette d'origine revient quand l'autre part.</summary>
    [Test]
    public void UneLunette_RemplaceCelleDOrigine_EtLaRendQuandEllePart()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        foreach (int slot in new[] { Sniper, Carbine, ChargedRifle })
        {
            WeaponData data = loadout[slot];
            Transform weapon = Instantiate(data.Designation.Replace("-", ""));
            WeaponModel model = weapon.GetComponent<WeaponModel>();
            Assert.IsTrue(Shown(weapon, "Lunette_Standard"), $"Garde : sans viseur, le {data.Designation} a sa lunette d'origine.");

            AttachmentMount.Apply(weapon, data, WeaponAttachments.None.WithSight(slot, ScopeL2A), slot, WeaponFinish.Origine);
            WeaponAttachment scope = AttachmentMount.Mounted(weapon, AttachmentCategory.Sight);
            Assert.IsNotNull(scope, $"La L2-A n'est pas montée sur le {data.Designation}.");
            Assert.AreEqual("L2-A", scope.Code);
            Assert.IsFalse(Shown(weapon, "Lunette_Standard"), $"{data.Designation} : la lunette d'origine resterait dans le tube de la nouvelle.");
            Assert.AreSame(scope.Sight, AttachmentMount.SightOf(model, out float eye), $"{data.Designation} : en visée, l'œil va derrière son œilleton...");
            Assert.AreEqual(scope.AimEyeDistance, eye, 1e-6f, "... à sa distance.");

            AttachmentMount.Apply(weapon, data, WeaponAttachments.None, slot, WeaponFinish.Origine);
            Assert.IsNull(AttachmentMount.Mounted(weapon, AttachmentCategory.Sight));
            Assert.IsTrue(Shown(weapon, "Lunette_Standard"), $"{data.Designation} : la lunette d'origine revient.");
            Assert.AreSame(model.Sight, AttachmentMount.SightOf(model, out _));
        }
    }

    /// <summary>Sur un pistolet, un petit réflex ne montre que le centre de son réticule (2026-10-29, l'utilisateur : tenu à
    /// bout de bras, le pistolet balance, et sa petite fenêtre coupait tour à tour les traits des côtés, qui semblaient
    /// bouger quand on se déplace de côté) : le triangle, le chevron, le point. Sur une arme d'épaule, le réticule entier.</summary>
    [Test]
    public void UnPetitReflex_SurUnPistolet_NeMontreQueLeCentreDeSonReticule()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        AttachmentCatalog catalog = Catalog();
        const int PistolPA05 = 10;
        Assert.AreEqual("PA-05", loadout[PistolPA05].Designation, "Garde : la onzième arme est le PA-05.");
        for (int sight = ReflexA; sight <= ReflexC; sight++)
        {
            WeaponAttachment prefab = catalog.SightPrefab(sight).GetComponent<WeaponAttachment>();
            string root = prefab.Code.Replace("-", "");
            Transform pistol = Instantiate("PA05");
            AttachmentMount.Apply(pistol, loadout[PistolPA05], WeaponAttachments.None.WithSight(PistolPA05, sight), PistolPA05, WeaponFinish.Origine);
            WeaponAttachment onPistol = AttachmentMount.Mounted(pistol, AttachmentCategory.Sight);
            Assert.IsNotNull(onPistol, $"{prefab.Code} n'est pas monté sur le PA-05.");
            Assert.AreEqual($"{root}_Reticule_Centre", onPistol.Lens.sharedMaterial.GetTexture("_Reticule").name,
                $"{prefab.Code} sur le PA-05 : le réticule entier, ses traits des côtés compris.");

            Transform rifle = Instantiate("FA07");
            AttachmentMount.Apply(rifle, loadout[Rifle], WeaponAttachments.None.WithSight(Rifle, sight), Rifle, WeaponFinish.Origine);
            Assert.AreEqual($"{root}_Reticule", AttachmentMount.Mounted(rifle, AttachmentCategory.Sight).Lens.sharedMaterial.GetTexture("_Reticule").name,
                $"{prefab.Code} sur le FA-07 : le réticule entier.");
            Assert.AreNotEqual(prefab.ReticleLabel, prefab.ReticleLabelOn(true), $"{prefab.Code} : l'armurerie nomme ce qu'il en reste sur un pistolet.");
        }
        Assert.IsNull(catalog.SightPrefab(ReflexD).GetComponent<WeaponAttachment>().PistolLens, "Le RX-D ne va pas sur un pistolet.");
    }

    /// <summary>Dans la vue grossie d'une lunette, rien ne luit ni n'affiche autour de la vue (2026-10-29, l'utilisateur) :
    /// le cercle rouge de l'œilleton de la 2,0x cernait toute la vue, et l'afficheur « 3× » de la 3,0x y restait. Seule la
    /// vitre, avec son réticule, peut luire.</summary>
    [Test]
    public void DansUneLunette_RienNeLuitAutourDeLaVue()
    {
        AttachmentCatalog catalog = Catalog();
        int scopes = 0;
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            WeaponAttachment a = catalog.SightPrefab(sight).GetComponent<WeaponAttachment>();
            if (a.Magnification <= 1f) continue;
            scopes++;
            foreach (Renderer r in a.Eyepiece)
                foreach (Material m in r.sharedMaterials)
                    Assert.IsFalse(m != null && (m.name.Contains("Lueur") || m.name.Contains("Holo")),
                        $"{a.Code} : {r.name} montre {m?.name} autour de la vue grossie.");
        }
        Assert.AreEqual(4, scopes, "Garde : les quatre lunettes ont été vues.");
    }

    /// <summary>Par où l'on regarde, épaulé (FirstPersonView.ScopeViewOf) : la lunette d'origine du sniper, dessinée
    /// par le HUD, tant qu'aucune lunette d'accessoire ne la remplace ; avec une lunette d'accessoire, son œilleton,
    /// à SON grossissement (×2, pas le ×4 de la lunette d'origine) ; avec un viseur sans grossissement, ni l'un ni
    /// l'autre.</summary>
    [Test]
    public void UneLunetteDAccessoire_GrossitDeSonGrossissement_PasDeCeluiDeLArme()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        AttachmentCatalog catalog = Catalog();
        WeaponAttachment l2a = catalog.SightPrefab(ScopeL2A).GetComponent<WeaponAttachment>();
        WeaponAttachment l3a = catalog.SightPrefab(ScopeL3A).GetComponent<WeaponAttachment>();
        WeaponAttachment holo = catalog.SightPrefab(HoloA).GetComponent<WeaponAttachment>();
        WeaponData sniper = loadout[Sniper], rifle = loadout[Rifle];
        Assert.AreEqual(4f, sniper.scopeMagnification, 1e-6f, "Garde : la lunette d'origine du FP-12 grossit quatre fois.");

        Assert.AreEqual(FirstPersonView.ScopeView.BuiltIn, FirstPersonView.ScopeViewOf(sniper, null));
        Assert.AreEqual(4f, FirstPersonView.ScopeMagnification(sniper, null), 1e-6f);
        Assert.AreEqual(FirstPersonView.ScopeView.Eyepiece, FirstPersonView.ScopeViewOf(sniper, l2a), "La lunette d'accessoire remplace celle du HUD.");
        Assert.AreEqual(2f, FirstPersonView.ScopeMagnification(sniper, l2a), 1e-6f, "×2, le grossissement de la L2-A.");
        Assert.AreEqual(3f, FirstPersonView.ScopeMagnification(sniper, l3a), 1e-6f);
        Assert.AreEqual(FirstPersonView.ScopeView.None, FirstPersonView.ScopeViewOf(rifle, null), "La mire du fusil d'assaut ne grossit pas.");
        Assert.AreEqual(FirstPersonView.ScopeView.None, FirstPersonView.ScopeViewOf(rifle, holo));
        Assert.AreEqual(1f, FirstPersonView.ScopeMagnification(rifle, holo), 1e-6f);
    }
}
