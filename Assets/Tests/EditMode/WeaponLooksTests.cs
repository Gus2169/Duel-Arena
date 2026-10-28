using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L'apparence des armes (2026-10-15, GDD § 6) : pour chacune des douze armes de l'arsenal (refonte du
/// 2026-10-26 : chaque modèle est une arme), la finition choisie à l'armurerie, annoncée en se
/// connectant, bornée par le serveur et vue par tous.
///
/// Ce qui casse ici ne lève aucune erreur : une arme qui prend la finition de sa voisine, la neuvième
/// arme qui déborde des 32 bits d'avant, un client modifié qui demande une finition qui n'existe pas
/// (et un serveur qui la publie telle quelle), une ancienne build qu'on lit de travers, deux instances
/// du jeu sur un PC qui échangent leurs armes, le réglage d'avant la refonte perdu, une finition
/// « dépareillée » qui ne change aucune pièce.
/// </summary>
public class WeaponLooksTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    [Test]
    public void LApparence_GardeUneFinitionParArme_SansToucherAuxAutres()
    {
        var looks = WeaponLooks.Default;
        Assert.AreEqual(WeaponController.MaxSlots, WeaponLooks.Slots, "Une finition pour chaque arme de l'arsenal.");
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            Assert.AreEqual(WeaponFinish.Origine, looks.Finish(slot), "Sans choix : d'origine.");

        // Chaque arme la sienne, jusqu'à la dernière (au-delà des 32 bits d'avant la refonte).
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            looks = looks.WithFinish(slot, (WeaponFinish)(slot % WeaponLooks.FinishCount));
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            Assert.AreEqual((WeaponFinish)(slot % WeaponLooks.FinishCount), looks.Finish(slot), $"Arme {slot + 1}.");

        // Changer une arme ne touche pas aux autres.
        WeaponLooks changed = looks.WithFinish(9, WeaponFinish.Encrassee);
        Assert.AreEqual(WeaponFinish.Encrassee, changed.Finish(9));
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            if (slot != 9) Assert.AreEqual(looks.Finish(slot), changed.Finish(slot), $"Arme {slot + 1} : intacte.");

        // Hors de l'arsenal : rien ne bouge.
        Assert.AreEqual(looks, looks.WithFinish(WeaponLooks.Slots, WeaponFinish.Polie));
        Assert.AreEqual(WeaponFinish.Origine, looks.Finish(-1));
    }

    [Test]
    public void LeServeur_RameneALOrigine_CeQuiNExistePas()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        // Un client modifié : tous les bits levés (des finitions qui n'existent pas, des bits sans arme).
        WeaponLooks sanitized = new WeaponLooks(ulong.MaxValue).Sanitized(loadout);
        Assert.AreEqual(WeaponLooks.Default, sanitized, "Une finition inexistante redevient l'origine, et les bits sans arme s'effacent.");

        // Un choix honnête passe tel quel, jusqu'à la dernière finition.
        var honest = WeaponLooks.Default;
        for (int slot = 0; slot < WeaponLooks.Slots; slot++)
            honest = honest.WithFinish(slot, (WeaponFinish)((WeaponLooks.FinishCount - 1 - slot % WeaponLooks.FinishCount)));
        Assert.AreEqual(honest, honest.Sanitized(loadout), "Le serveur ne doit pas toucher à un choix valable.");

        // Juste au-delà de la dernière finition.
        WeaponLooks beyond = WeaponLooks.Default.WithFinish(11, (WeaponFinish)WeaponLooks.FinishCount).Sanitized(loadout);
        Assert.AreEqual(WeaponFinish.Origine, beyond.Finish(11));
    }

    [Test]
    public void LaDemandeDeConnexion_PorteLApparenceDesDouzeArmes()
    {
        WeaponLooks looks = WeaponLooks.Default.WithFinish(0, WeaponFinish.Polie).WithFinish(3, WeaponFinish.Depareillee)
                                              .WithFinish(11, WeaponFinish.Rouillee);
        Assert.IsTrue(SessionFlow.ParsePayload(SessionFlow.BuildPayload("0.3.0", "Héloïse", looks), out string version, out string name, out WeaponLooks read));
        Assert.AreEqual("0.3.0", version);
        Assert.AreEqual("Héloïse", name, "Le pseudo se lit toujours.");
        Assert.AreEqual(looks, read, "L'apparence arrive telle qu'elle est partie, la douzième arme comprise.");

        // Une ancienne build : sa version seule, ou sa version et son pseudo. Les armes d'origine.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.2\nUntel"), out _, out name, out read));
        Assert.AreEqual("Untel", name);
        Assert.AreEqual(WeaponLooks.Default, read);
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.1.0"), out _, out _, out read));
        Assert.AreEqual(WeaponLooks.Default, read);

        // Un client modifié qui envoie n'importe quoi à la place de l'apparence : les armes d'origine.
        Assert.IsTrue(SessionFlow.ParsePayload(System.Text.Encoding.UTF8.GetBytes("0.3.0\nUntel\nZZ-pas-hexa"), out _, out _, out read));
        Assert.AreEqual(WeaponLooks.Default, read);
    }

    /// <summary>Comme le pseudo : deux instances du jeu sur un PC (l'Editor et son clone, deux builds)
    /// partagent la clé du registre ; chacune garde ses armes.</summary>
    [Test]
    public void LApparence_ResteCelleDeCetteInstance_QuandUneAutreEnregistreLaSienne()
    {
        WeaponLooks before = GameSettings.WeaponLooks;
        string stored = PlayerPrefs.GetString(GameSettings.WeaponLooksKey, "");
        try
        {
            WeaponLooks mine = WeaponLooks.Default.WithFinish(2, WeaponFinish.Rouillee);
            GameSettings.WeaponLooks = mine;
            PlayerPrefs.SetString(GameSettings.WeaponLooksKey, WeaponLooks.Default.WithFinish(0, WeaponFinish.Polie).Packed.ToString("X12"));
            Assert.AreEqual(mine, GameSettings.WeaponLooks, "L'autre instance a écrit la sienne : celle-ci garde ses armes.");
        }
        finally
        {
            GameSettings.WeaponLooks = before;
            PlayerPrefs.SetString(GameSettings.WeaponLooksKey, stored);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Le réglage d'avant la refonte (8 bits par classe : le modèle, puis la finition) : la
    /// finition passe à l'arme que montrait le modèle choisi ; les autres armes restent d'origine.</summary>
    [Test]
    public void LApparenceDAvantLaRefonte_PasseALArmeDuModeleChoisi()
    {
        // Fusil d'assaut : modèle 2 (FX-1), polie ; pompe : modèle 1 (FAP-30), rouillée ; pistolet :
        // modèle 0 (FX-4), dépareillée ; sniper : rien.
        uint old = (2u | (uint)WeaponFinish.Polie << 4)
                 | (1u | (uint)WeaponFinish.Rouillee << 4) << 16
                 | (0u | (uint)WeaponFinish.Depareillee << 4) << 24;
        WeaponLooks looks = WeaponLooks.FromBeforeOverhaul(old, GameSettings.ArmsBeforeOverhaul);
        Assert.AreEqual(WeaponFinish.Polie, looks.Finish(5), "Le FX-1, cinquième arme de l'arsenal après les quatre d'avant.");
        Assert.AreEqual(WeaponFinish.Rouillee, looks.Finish(8), "Le FAP-30.");
        Assert.AreEqual(WeaponFinish.Depareillee, looks.Finish(3), "Le FX-4, modèle par défaut du pistolet d'avant.");
        Assert.AreEqual(WeaponFinish.Origine, looks.Finish(0), "Le FA-07 n'était pas le modèle choisi.");
        Assert.AreEqual(WeaponFinish.Origine, looks.Finish(1), "Le sniper n'avait rien.");

        // Les places nommées par la table sont bien ces armes-là dans l'arsenal du préfab.
        IReadOnlyList<WeaponData> loadout = Loadout();
        string[] expected = { "FX-1", "FAP-30", "FX-4" };
        int[] slots = { 5, 8, 3 };
        for (int i = 0; i < slots.Length; i++) Assert.AreEqual(expected[i], loadout[slots[i]].Designation);
    }

    /// <summary>« Dépareillée » : les pièces qu'une réparation a prises sur une autre arme portent sa
    /// finition (exporter_armes, REMPLACEMENTS_USURE) ; les autres gardent la leur.</summary>
    [Test]
    public void LaFinitionDepareillee_DonneAuxPiecesRapporteesLaFinitionDUneAutreArme()
    {
        var rifle = Loadout()[0].modelPrefab;
        var finishes = rifle.GetComponent<WeaponFinishes>();
        int handguard = -1, receiver = -1;
        for (int i = 0; i < finishes.PieceCount; i++)
        {
            if (finishes.RendererOf(i).name == "Garde_Main") handguard = i;
            if (finishes.RendererOf(i).name == "Carcasse_Haute") receiver = i;
        }
        Assert.GreaterOrEqual(handguard, 0, $"{rifle.name} n'a plus de garde-main.");
        Assert.GreaterOrEqual(receiver, 0, $"{rifle.name} n'a plus de carcasse haute.");

        Material originHandguard = finishes.MaterialsOf(handguard, WeaponFinish.Origine)[0];
        Material mismatched = finishes.MaterialsOf(handguard, WeaponFinish.Depareillee)[0];
        Material originReceiver = finishes.MaterialsOf(receiver, WeaponFinish.Origine)[0];
        Material receiverMismatched = finishes.MaterialsOf(receiver, WeaponFinish.Depareillee)[0];
        Assert.AreSame(originHandguard, originReceiver, "Le garde-main et la carcasse ont la même peinture d'origine.");
        Assert.AreNotEqual(originHandguard.GetColor("_Base"), mismatched.GetColor("_Base"),
            "Dépareillée : le garde-main vient d'une autre arme, d'une autre couleur.");
        Assert.AreEqual(originReceiver.GetColor("_Base"), receiverMismatched.GetColor("_Base"),
            "Dépareillée : la carcasse haute reste de sa couleur (seules ses marques changent).");
    }
}
