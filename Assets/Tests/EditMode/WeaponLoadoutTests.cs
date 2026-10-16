using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MagazineState = WeaponController.MagazineState;

/// <summary>
/// La panoplie (2026-10-11) : fusil d'assaut, sniper, pompe et raygun, touches 1 à 4, réglés sur
/// Call of Duty et Rainbow Six.
///
/// Ce qui casse ici ne lève aucune erreur : un sniper qui ne tue plus d'une balle au buste, une gerbe
/// qui change d'un tir à l'autre (le GDD § 3 interdit le hasard), un pompe qui recharge tout d'un
/// coup, une arme qu'on tire avant de l'avoir sortie. Les seuils de dégâts sont lus dans les VRAIS
/// assets : ce sont eux qui tournent en jeu, pas des valeurs de test.
/// </summary>
public class WeaponLoadoutTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const float Health = 100f;
    private const int RaygunSlot = 3;   // touche 4

    private static WeaponController PrefabWeapon()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, "Prefab joueur introuvable : " + PlayerPrefabPath);
        var weapon = prefab.GetComponentInChildren<WeaponController>(true);
        Assert.IsNotNull(weapon, "Le prefab joueur n'a pas de WeaponController.");
        return weapon;
    }

    private static WeaponData Slot(int slot)
    {
        WeaponData data = PrefabWeapon().DataOf(slot);
        Assert.IsNotNull(data, $"La place {slot + 1} de la panoplie est vide.");
        return data;
    }

    private static int ShotsToKill(float damagePerShot) => Mathf.CeilToInt(Health / damagePerShot - 1e-4f);

    // ------------------------------------------------------------------
    // La panoplie du prefab
    // ------------------------------------------------------------------

    /// <summary>Les modèles au choix d'une arme : celui par défaut, puis les deux autres.</summary>
    private static IEnumerable<GameObject> ModelsOf(WeaponData data)
    {
        for (int i = 0; i < data.ModelCount; i++) yield return data.ModelPrefabFor(i);
    }

    [Test]
    public void LaPanoplie_ADesModelesTenusEnMain_AvecMireEtBouche()
    {
        WeaponController weapon = PrefabWeapon();
        Assert.AreEqual(WeaponController.MaxSlots, weapon.SlotCount, "Quatre armes, une par touche (1 à 4).");

        var designations = new HashSet<string>();
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            WeaponData data = weapon.DataOf(slot);
            Assert.IsNotNull(data.modelPrefab, $"{data.weaponName} n'a pas de modèle.");
            Assert.AreEqual(3, data.ModelCount, $"{data.weaponName} : le stock, la série 30 et la version laser (GDD § 6).");
            var families = new HashSet<WeaponModel.Family>();
            foreach (GameObject prefab in ModelsOf(data))
            {
                Assert.IsNotNull(prefab, $"{data.weaponName} : un modèle manque.");
                var model = prefab.GetComponent<WeaponModel>();
                Assert.IsNotNull(model, $"{prefab.name} n'a pas de WeaponModel.");
                Assert.IsFalse(string.IsNullOrEmpty(model.Designation), $"{prefab.name} n'a pas de désignation.");
                Assert.IsTrue(designations.Add(model.Designation), $"Deux modèles s'appellent {model.Designation} : HeldWeapons les confondrait.");
                Assert.IsTrue(families.Add(model.ModelFamily), $"{data.weaponName} a deux modèles de la famille {model.ModelFamily}.");
                Assert.IsNotNull(model.Sight, $"{prefab.name} n'a pas de mire.");
                Assert.IsNotNull(model.Muzzle, $"{prefab.name} n'a pas de bouche.");
                Assert.Less(Quaternion.Angle(Quaternion.identity, model.Sight.localRotation), 0.01f, $"La mire de {prefab.name} doit regarder dans l'axe du canon (+Z).");
                Assert.Greater(model.Muzzle.localPosition.z, model.Sight.localPosition.z, $"La bouche de {prefab.name} doit être devant la mire.");
                Assert.Greater(model.Muzzle.localPosition.z, 0.1f, $"La bouche de {prefab.name} doit être devant la poignée (origine).");
                Assert.AreEqual(Vector3.one, prefab.transform.localScale, $"{prefab.name} : à l'échelle 1, la poignée au point de prise.");
            }
            // Le modèle par défaut, celui de qui n'a rien choisi et des armes lancées par la tribune : le
            // stock, sauf au raygun, qui se montre en pistolet laser (choix de l'utilisateur, 2026-10-15).
            // Les deux autres suivent l'ordre des familles.
            WeaponModel defaultModel = data.modelPrefab.GetComponent<WeaponModel>();
            if (slot == RaygunSlot)
                Assert.AreEqual("FX-4", defaultModel.Designation, "Le raygun se montre d'abord en pistolet laser (GDD § 6).");
            else
                Assert.AreEqual(WeaponModel.Family.Stock, defaultModel.ModelFamily, $"{data.weaponName} : le modèle par défaut est celui du stock.");
            Assert.Less((int)data.ModelPrefabFor(1).GetComponent<WeaponModel>().ModelFamily, (int)data.ModelPrefabFor(2).GetComponent<WeaponModel>().ModelFamily,
                $"{data.weaponName} : après le modèle par défaut, les deux autres dans l'ordre des familles.");
        }

        // Les armes se créent en main au lancement, au point de prise du préfab (HeldWeapons) : aucune
        // n'y est posée d'avance, la vue subjective en aurait une de trop.
        var player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform hand = null;
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorig:RightHand") hand = t;
        Assert.IsNotNull(hand.Find(HeldWeapons.GripAnchorName), "Le point de prise des armes manque sous la main droite.");
        Assert.AreEqual(0, hand.GetComponentsInChildren<WeaponModel>(true).Length, "Une arme est posée dans le préfab joueur.");
        Assert.IsNotNull(player.GetComponent<PlayerWeaponLooks>(), "Sans PlayerWeaponLooks, personne ne verrait les armes choisies.");
    }

    /// <summary>Réimporter les armes de Blender garde le modèle par défaut de chaque classe : celui que
    /// son WeaponData désigne déjà, s'il est des siens (le FX-4 du raygun), le stock sinon ; les deux
    /// autres suivent l'ordre des familles. Sans ça, la prochaine importation rendrait au raygun son
    /// pistolet à balles, sans un mot.</summary>
    [Test]
    public void LaReimportation_GardeLeModeleParDefautDeChaqueClasse()
    {
        string[] paths = { "Armes/PA05/PA05.prefab", "Armes/RV30/RV30.prefab", "Armes/FX4/FX4.prefab" };
        Assert.AreEqual((int)WeaponModel.Family.Laser, WeaponImporter.DefaultFamily("Armes/FX4/FX4.prefab", paths), "Un choix fait dans Unity survit.");
        Assert.AreEqual((int)WeaponModel.Family.Serie30, WeaponImporter.DefaultFamily("Armes/RV30/RV30.prefab", paths));
        Assert.AreEqual((int)WeaponModel.Family.Stock, WeaponImporter.DefaultFamily("Raygun.prefab", paths), "Un modèle qui n'est pas de la classe (l'ancien raygun) : le stock.");
        Assert.AreEqual((int)WeaponModel.Family.Stock, WeaponImporter.DefaultFamily(null, paths), "Premier import : le stock.");

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, WeaponImporter.ModelOrder(0, 3));
        CollectionAssert.AreEqual(new[] { 1, 0, 2 }, WeaponImporter.ModelOrder(1, 3));
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, WeaponImporter.ModelOrder(2, 3));
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, WeaponImporter.ModelOrder(7, 3), "Une famille hors bornes : le stock.");
    }

    /// <summary>Les six finitions de chaque modèle (GDD § 6) : chaque pièce a ses matériaux dans
    /// chacune, tous du shader de l'usure, et une finition change vraiment quelque chose.</summary>
    [Test]
    public void ChaqueModele_ASesSixFinitions_DuShaderDeLUsure()
    {
        WeaponController weapon = PrefabWeapon();
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            foreach (GameObject prefab in ModelsOf(weapon.DataOf(slot)))
            {
                var finishes = prefab.GetComponent<WeaponFinishes>();
                Assert.IsNotNull(finishes, $"{prefab.name} n'a pas de finitions.");
                Assert.AreEqual(prefab.GetComponentsInChildren<MeshRenderer>(true).Length, finishes.PieceCount,
                    $"{prefab.name} : une pièce n'a pas ses finitions.");
                var distinct = new HashSet<Material>();
                for (int i = 0; i < finishes.PieceCount; i++)
                {
                    Renderer r = finishes.RendererOf(i);
                    Assert.IsNotNull(r, $"{prefab.name} : une pièce a perdu son rendu.");
                    CollectionAssert.AreEqual(r.sharedMaterials, finishes.MaterialsOf(i, WeaponFinish.Origine),
                        $"{prefab.name}/{r.name} : le préfab doit porter la finition d'origine.");
                    for (int f = 0; f < WeaponLooks.FinishCount; f++)
                    {
                        Material[] mats = finishes.MaterialsOf(i, (WeaponFinish)f);
                        Assert.AreEqual(r.sharedMaterials.Length, mats.Length, $"{prefab.name}/{r.name} : un matériau par sous-maillage.");
                        foreach (Material m in mats)
                        {
                            Assert.IsNotNull(m, $"{prefab.name}/{r.name} : un matériau manque à la finition {(WeaponFinish)f}.");
                            Assert.AreEqual("DuelArena/ArmeUsee", m.shader.name, $"{m.name} n'est pas du shader de l'usure.");
                            Assert.IsNotNull(m.GetTexture("_Occlusion"), $"{m.name} a perdu l'occlusion cuite dans Blender.");
                            distinct.Add(m);
                        }
                    }
                }
                Assert.Greater(distinct.Count, prefab.GetComponentsInChildren<MeshRenderer>(true)[0].sharedMaterials.Length,
                    $"{prefab.name} : les finitions ne changent aucun matériau.");
            }
        }
    }

    /// <summary>Les accessoires (2026-10-16) se montent aux points posés sous `Accessoires` dans le
    /// préfab de chaque arme (AttachmentMount) ; une arme sans point pour une rubrique ne l'a pas. Un
    /// modèle remplacé sans ses points perdrait ses accessoires en silence.</summary>
    [Test]
    public void ChaqueArme_ASesPointsDAccroche_SaufLaPoigneeDuPistolet()
    {
        WeaponController weapon = PrefabWeapon();
        for (int slot = 0; slot < weapon.SlotCount; slot++)
        {
            foreach (GameObject prefab in ModelsOf(weapon.DataOf(slot)))
            {
                Transform group = prefab.transform.Find(AttachmentMount.SocketGroupName);
                Assert.IsNotNull(group, $"{prefab.name} n'a pas de groupe « {AttachmentMount.SocketGroupName} ».");

                bool pistol = prefab.GetComponent<WeaponModel>().PistolHold;
                foreach (AttachmentCategory category in System.Enum.GetValues(typeof(AttachmentCategory)))
                {
                    Transform socket = group.Find(AttachmentMount.SocketName(category));
                    if (category == AttachmentCategory.Grip && pistol)
                    {
                        Assert.IsNull(socket, $"{prefab.name} est un pistolet : pas de poignée avant.");
                        continue;
                    }
                    Assert.IsNotNull(socket, $"{prefab.name} n'a pas de point d'accroche pour {category}.");
                    Assert.AreEqual(0, socket.childCount, $"Le point {socket.name} de {prefab.name} doit rester vide : l'accessoire y est monté au lancement.");
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Dégâts : zones et distance (façon Call of Duty)
    // ------------------------------------------------------------------

    /// <summary>Le gant à décharge du taser : le GD-1 (2026-10-16), construit dans Blender dans la DA du projet
    /// (Tools/Blender/gant_choc.py), à la place du modèle généré par IA de 28 404 triangles. Porté sur
    /// l'avant-bras gauche, la main qui frappe.</summary>
    [Test]
    public void LeGant_EstSurLAvantBrasGauche_LegerEtDansLaDA()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform glove = null, hand = null;
        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == HeldWeapons.TaserGloveName) glove = t;
            if (t.name == "mixamorig:LeftHand") hand = t;
        }
        Assert.IsNotNull(glove, "Le préfab joueur ne porte pas le gant du taser.");
        Assert.AreEqual("mixamorig:LeftForeArm", glove.parent.name, "Le gant se porte sur l'avant-bras gauche : c'est la main gauche qui frappe.");
        Assert.AreEqual(hand.localPosition, glove.localPosition, "L'origine du gant est au poignet.");
        Assert.AreEqual(Vector3.zero, hand.localScale, "La main du robot traverserait le poing du gant.");

        Transform fist = glove.Find("Poing");
        Assert.IsNotNull(fist, "Le gant n'a pas de repère « Poing ».");
        Assert.Greater(fist.localPosition.z, 0.12f, "Le poing est devant les jointures (12,8 cm du poignet).");
        Assert.Less(fist.localPosition.z, 0.25f, "Le poing est au bout de la main, pas au-delà.");
        Assert.IsNotNull(fist.GetComponentInChildren<TaserSparkFX>(true), "Les arcs du coup partent du poing (ElectricArcTests).");

        int triangles = 0;
        MeshRenderer[] pieces = glove.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer piece in pieces) triangles += piece.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
        Assert.Greater(triangles, 500, "Le gant n'a plus de modèle.");
        Assert.Less(triangles, 5000, $"Le gant pèse {triangles} triangles : le budget d'une arme du projet (l'ancien en avait 28 404).");
        foreach (MeshRenderer piece in pieces)
            foreach (Material m in piece.sharedMaterials)
                Assert.AreEqual(WeaponImporter.ShaderName, m.shader.name, $"{piece.name} : le gant s'use comme les armes (ArmeUsee).");
        var finishes = glove.GetComponent<WeaponFinishes>();
        Assert.IsNotNull(finishes, "Le gant n'a pas ses finitions.");
        Assert.AreEqual(pieces.Length, finishes.PieceCount, "Chaque pièce du gant a ses six finitions.");
    }

    [Test]
    public void LeFusilDAssaut_GardeSesSixBallesAuBuste_EtLaTeteAccelere()
    {
        WeaponData ar = Slot(0);
        Assert.AreEqual(6, ShotsToKill(ar.DamageAt(HitboxZoneType.Torso, 10f)), "TTK du GDD : 6 balles au buste.");
        Assert.Less(ShotsToKill(ar.DamageAt(HitboxZoneType.Head, 10f)), 6, "Viser la tête doit payer.");
        Assert.AreEqual(ar.DamageAt(HitboxZoneType.Torso, 1f), ar.DamageAt(HitboxZoneType.Torso, 90f), 1e-4f, "Pas de baisse de dégâts avec la distance.");
    }

    [Test]
    public void LeSniper_TueDUneBalleAuBusteEtALaTete_PasAuxJambes()
    {
        WeaponData sniper = Slot(1);
        Assert.AreEqual(1, ShotsToKill(sniper.DamageAt(HitboxZoneType.Torso, 60f)), "Au buste, à toute distance.");
        Assert.AreEqual(1, ShotsToKill(sniper.DamageAt(HitboxZoneType.Head, 60f)));
        Assert.AreEqual(2, ShotsToKill(sniper.DamageAt(HitboxZoneType.Legs, 60f)), "Aux jambes, il en faut deux.");
        Assert.IsFalse(sniper.isAutomatic, "Un sniper à verrou tire coup par coup.");
        Assert.Greater(sniper.scopeMagnification, 1f, "Le sniper a sa lunette plein écran.");
        Assert.IsFalse(sniper.hipCrosshair, "Pas de réticule à la hanche, comme dans CoD.");
    }

    [Test]
    public void LePompe_TueDePres_PasDeLoin_SansBonusALaTete()
    {
        WeaponData pompe = Slot(2);
        Assert.Greater(pompe.PelletCount, 1, "Le pompe tire des plombs.");
        float pointBlank = pompe.PelletCount * pompe.DamageAt(HitboxZoneType.Torso, 3f);
        float far = pompe.PelletCount * pompe.DamageAt(HitboxZoneType.Torso, 25f);
        Assert.GreaterOrEqual(pointBlank, Health, "Tous les plombs au buste, à bout portant, tuent.");
        Assert.Less(far, Health, "À 25 m, même tous les plombs ne tuent pas.");
        Assert.AreEqual(pompe.DamageAt(HitboxZoneType.Torso, 3f), pompe.DamageAt(HitboxZoneType.Head, 3f), 1e-4f,
            "Les plombs ne comptent pas la tête (CoD).");
        Assert.IsTrue(pompe.reloadPerRound, "Le pompe recharge cartouche par cartouche.");
    }

    [Test]
    public void LeRaygun_EstUnPistolet_QuatreTirsAuBuste()
    {
        WeaponData raygun = Slot(3);
        Assert.IsFalse(raygun.isAutomatic, "Coup par coup.");
        Assert.AreEqual(1, raygun.PelletCount, "Un rayon, pas une gerbe.");
        Assert.AreEqual(4, ShotsToKill(raygun.DamageAt(HitboxZoneType.Torso, 10f)));
        foreach (GameObject prefab in ModelsOf(raygun))
            Assert.IsTrue(prefab.GetComponent<WeaponModel>().PistolHold, $"{prefab.name} : tenu à bout de bras en vue subjective.");
        // Le plus léger : on le sort le plus vite et on court avec.
        for (int slot = 0; slot < 3; slot++)
        {
            Assert.Less(raygun.raiseTime, Slot(slot).raiseTime, $"Le raygun se sort plus vite que {Slot(slot).weaponName}.");
            Assert.GreaterOrEqual(raygun.moveSpeedMultiplier, Slot(slot).moveSpeedMultiplier);
        }
    }

    [Test]
    public void LaBaisseDeDegatsAvecLaDistance_EstContinue()
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        data.damagePerHit = 20f;
        data.falloffStart = 10f;
        data.falloffEnd = 20f;
        data.minDamageMultiplier = 0.5f;
        Assert.AreEqual(20f, data.DamageAt(HitboxZoneType.Torso, 5f), 1e-4f);
        Assert.AreEqual(20f, data.DamageAt(HitboxZoneType.Torso, 10f), 1e-4f);
        Assert.AreEqual(15f, data.DamageAt(HitboxZoneType.Torso, 15f), 1e-4f);
        Assert.AreEqual(10f, data.DamageAt(HitboxZoneType.Torso, 20f), 1e-4f);
        Assert.AreEqual(10f, data.DamageAt(HitboxZoneType.Torso, 80f), 1e-4f);
        Object.DestroyImmediate(data);
    }

    // ------------------------------------------------------------------
    // La gerbe : un motif fixe (GDD § 3)
    // ------------------------------------------------------------------

    [Test]
    public void LaGerbe_EstUnMotifFixe_DansLeRepereDuRegard()
    {
        WeaponData pompe = Slot(2);
        Vector3 forward = new Vector3(0.3f, -0.2f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right);
        for (int i = 0; i < pompe.PelletCount; i++)
        {
            Vector2 offset = pompe.PelletOffset(i, false);
            Vector3 a = WeaponController.PelletDirection(forward, offset);
            Vector3 b = WeaponController.PelletDirection(forward, offset);
            Assert.AreEqual(a, b, $"Plomb {i} : deux tirs identiques doivent partir exactement pareil.");
            Assert.AreEqual(offset.magnitude, Vector3.Angle(forward, a), 0.05f, $"Plomb {i} : écart angulaire.");
            if (Mathf.Abs(offset.x) > 0.1f) Assert.AreEqual(Mathf.Sign(offset.x), Mathf.Sign(Vector3.Dot(a, right)), $"Plomb {i} : côté.");
            if (Mathf.Abs(offset.y) > 0.1f) Assert.AreEqual(Mathf.Sign(offset.y), Mathf.Sign(Vector3.Dot(a, up)), $"Plomb {i} : hauteur.");
        }
        // En visée, la gerbe se resserre.
        float hip = 0f, aim = 0f;
        for (int i = 0; i < pompe.PelletCount; i++)
        {
            hip = Mathf.Max(hip, pompe.PelletOffset(i, false).magnitude);
            aim = Mathf.Max(aim, pompe.PelletOffset(i, true).magnitude);
        }
        Assert.Less(aim, hip, "La gerbe ne se resserre pas en visée.");

        // Regard à la verticale : pas de NaN.
        Vector3 vertical = WeaponController.PelletDirection(Vector3.up, new Vector2(2f, 1f));
        Assert.IsFalse(float.IsNaN(vertical.x) || float.IsNaN(vertical.y) || float.IsNaN(vertical.z));
        Assert.AreEqual(Vector3.forward, WeaponController.PelletDirection(Vector3.forward, Vector2.zero), "Une balle seule part dans l'axe.");
    }

    // ------------------------------------------------------------------
    // Chargeur : cartouche par cartouche, changement d'arme
    // ------------------------------------------------------------------

    [Test]
    public void LePompe_GlisseSesCartouchesUneAUne_EtTirerInterromptLeRechargement()
    {
        const int size = 6;
        const float shell = 0.5f;
        var m = new MagazineState { ammo = 1 };
        Assert.IsTrue(WeaponController.TryStartReload(ref m, 10.0, size, shell));

        Assert.IsFalse(WeaponController.CompleteReloadIfDue(ref m, 10.4, size, true, shell));
        Assert.AreEqual(1, m.ammo);
        Assert.IsTrue(WeaponController.CompleteReloadIfDue(ref m, 10.5, size, true, shell));
        Assert.AreEqual(2, m.ammo, "Une cartouche à 0,5 s.");
        Assert.Greater(m.reloadEndsAt, 0.0, "Le rechargement continue.");
        WeaponController.CompleteReloadIfDue(ref m, 11.6, size, true, shell);
        Assert.AreEqual(4, m.ammo, "Deux de plus à 1,5 s (et pas trois à 1,6 s).");

        // Tirer pendant le rechargement : il s'arrête, le coup part avec ce qui est chargé.
        Assert.IsTrue(WeaponController.TryConsumeRound(ref m, 11.7, size, true, shell, 0.0));
        Assert.AreEqual(3, m.ammo);
        Assert.AreEqual(0.0, m.reloadEndsAt, "Le tir n'a pas interrompu le rechargement.");

        // Jusqu'au plein, puis plus rien.
        var full = new MagazineState { ammo = 4 };
        WeaponController.TryStartReload(ref full, 0.0, size, shell);
        WeaponController.CompleteReloadIfDue(ref full, 10.0, size, true, shell);
        Assert.AreEqual(size, full.ammo);
        Assert.AreEqual(0.0, full.reloadEndsAt);

        // Un chargeur complet, lui, ne se tire pas en rechargeant.
        var rifle = new MagazineState { ammo = 3 };
        WeaponController.TryStartReload(ref rifle, 0.0, 30, 2f);
        Assert.IsFalse(WeaponController.TryConsumeRound(ref rifle, 1.0, 30, false, 0f, 0.0));
        // Et un pompe vide en rechargement attend sa première cartouche.
        var empty = new MagazineState { ammo = 0 };
        WeaponController.TryStartReload(ref empty, 0.0, size, shell);
        Assert.IsFalse(WeaponController.TryConsumeRound(ref empty, 0.2, size, true, shell, 0.0));
        Assert.IsTrue(WeaponController.TryConsumeRound(ref empty, 0.47, size, true, shell, WeaponController.ReloadCompletionTolerance),
            "La première cartouche à 50 ms près compte (gigue du réseau), comme pour un rechargement complet.");
    }

    [Test]
    public void ChangerDArme_GardeLesBallesDeChacune_EtLaNouvelleSeSortAvantDeTirer()
    {
        var m = new MagazineState { equipped = 0 };
        m.SetAmmo(0, 12);
        m.SetAmmo(1, 5);
        WeaponController.TryStartReload(ref m, 0.0, 30, 2.2f);

        Assert.IsTrue(WeaponController.TrySwitchWeapon(ref m, 1, 1.0, 0.8f));
        Assert.AreEqual(1, m.equipped);
        Assert.AreEqual(5, m.ammo, "Le chargeur de la nouvelle arme.");
        Assert.AreEqual(12, m.AmmoOf(0), "Celui de l'ancienne n'a pas bougé.");
        Assert.AreEqual(0.0, m.reloadEndsAt, "Changer d'arme annule le rechargement (CoD).");

        Assert.IsFalse(WeaponController.TryConsumeRound(ref m, 1.5, 5, false, 0f, 0.0), "L'arme n'est pas encore sortie.");
        Assert.IsTrue(WeaponController.TryConsumeRound(ref m, 1.81, 5, false, 0f, 0.0));
        Assert.AreEqual(4, m.ammo);

        Assert.IsFalse(WeaponController.TrySwitchWeapon(ref m, 1, 2.0, 0.8f), "Déjà en main.");
        Assert.IsFalse(WeaponController.TrySwitchWeapon(ref m, WeaponController.MaxSlots, 2.0, 0.8f), "Hors de la panoplie.");
    }

    [Test]
    public void LeRecalage_ReprendArmeEtChargeurs_UneFoisToutAccuse()
    {
        var local = new MagazineState { equipped = 2, actionsProcessed = 0 };
        local.SetAmmo(0, 30);
        local.SetAmmo(2, 6);
        var server = local;
        server.actionsProcessed = 3;
        server.equipped = 0; // le serveur a refusé le changement
        server.SetAmmo(2, 4);

        MagazineState before = local;
        WeaponController.ReconcileMagazine(server, 4, ref local);
        Assert.AreEqual(before.equipped, local.equipped, "Une demande est encore en route : pas de recalage.");

        WeaponController.ReconcileMagazine(server, 3, ref local);
        Assert.AreEqual(0, local.equipped, "Tout est accusé : le serveur fait foi, arme en main comprise.");
        Assert.AreEqual(4, local.AmmoOf(2), "Et pour le chargeur de chaque arme.");
    }

    [Test]
    public void LaLunette_GrossitLeChamp_Exactement()
    {
        float fov = PlayerCameraLook.ZoomedFov(90f, 4f);
        Assert.AreEqual(0.25f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(45f * Mathf.Deg2Rad), 1e-4f);
        Assert.AreEqual(90f, PlayerCameraLook.ZoomedFov(90f, 1f), 1e-4f);
    }

    /// <summary>Rapporté par l'utilisateur le 2026-10-12 : viser au sniper touche de sprint tenue,
    /// puis lâcher la visée, laissait la vue zoomée jusqu'à ce qu'on lâche le sprint. Le champ de la
    /// lunette ne revenait d'un coup qu'à un joueur qui ne sprinte pas.</summary>
    [Test]
    public void SortirDeLaLunette_DezoomeAussitot_MemeEnCourant()
    {
        const float Base = 90f, Sprint = 96f, Speed = 8f, Dt = 1f / 60f;
        float scoped = PlayerCameraLook.NextFov(Base, Base, Sprint, 4f, false, Speed, Dt);
        Assert.AreEqual(PlayerCameraLook.ZoomedFov(Base, 4f), scoped, 1e-4f, "Dans la lunette : le champ du grossissement.");

        foreach (bool sprinting in new[] { false, true })
        {
            float next = PlayerCameraLook.NextFov(scoped, Base, Sprint, 1f, sprinting, Speed, Dt);
            Assert.GreaterOrEqual(next, Base - 0.01f,
                $"En sortant de la lunette ({(sprinting ? "en courant" : "à l'arrêt")}), la vue doit revenir au champ normal dès l'image suivante.");
        }

        // Le passage au champ de sprint, lui, reste progressif.
        float sprintStart = PlayerCameraLook.NextFov(Base, Base, Sprint, 1f, true, Speed, Dt);
        Assert.Less(sprintStart, Sprint, "Le champ de sprint s'installe en douceur.");
        Assert.Greater(sprintStart, Base);
    }
}
