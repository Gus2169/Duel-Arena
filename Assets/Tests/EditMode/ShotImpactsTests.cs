using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Là où arrive un tir (2026-11-04, à la place des boules rouges) : les marques du décor, avec de vrais colliders, leur taille
/// selon l'arme, leur fin, leur dessin. Ce qui casse ici se voit sans lever d'erreur : une marque qui flotte sur ce qui bouge, qui
/// pend dans le vide au bord d'une caisse, un carré gris autour d'un trou.
/// </summary>
public class ShotImpactsTests
{
    private const int World = 1;

    // Loin de ce que la scène ouverte pourrait porter.
    private static readonly Vector3 Origin = new Vector3(300f, 50f, -300f);

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.DestroyImmediate(go);
        created.Clear();
        Physics.SyncTransforms();
    }

    private GameObject Box(Vector3 min, Vector3 max)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(box);
        box.transform.position = Origin + (min + max) * 0.5f;
        box.transform.localScale = max - min;
        return box;
    }

    [Test]
    public void UneMarque_SePoseSurLeDecor_UnPeuDevant()
    {
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 3f, 0.5f));
        Physics.SyncTransforms();
        float size = 0.12f;
        Vector3 point = Origin + new Vector3(0.3f, 1.5f, 0f);
        Assert.IsTrue(ShotImpacts.TryPlace(point, Vector3.back, World, ref size, out Vector3 center, out Vector3 surface));
        Assert.AreEqual(0.12f, size, 1e-4f, "Sur un mur, elle garde sa taille.");
        Assert.AreEqual(Vector3.back, surface);
        float lift = Origin.z - center.z;
        Assert.That(lift, Is.InRange(0.001f, 0.01f), "Devant la surface, pour ne pas s'y enfoncer.");
    }

    [Test]
    public void SurCeQuiBouge_OuDansLeVide_PasDeMarque()
    {
        float size = 0.12f;
        Physics.SyncTransforms();
        Assert.IsFalse(ShotImpacts.TryPlace(Origin, Vector3.back, World, ref size, out _, out _), "Rien ici.");

        // La cible de la salle de précision, sur son chariot : la marque resterait en l'air quand elle roule.
        GameObject moving = Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 3f, 0.5f));
        moving.AddComponent<Rigidbody>().isKinematic = true;
        Physics.SyncTransforms();
        Assert.IsFalse(ShotImpacts.TryPlace(Origin + new Vector3(0f, 1.5f, 0f), Vector3.back, World, ref size, out _, out _), "Pas sur ce qui bouge.");
    }

    [Test]
    public void PresDUneArete_LaMarqueRetrecit_PourNePasPendreDansLeVide()
    {
        Box(new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 1f));
        Physics.SyncTransforms();
        float size = 0.4f;
        Vector3 nearEdge = Origin + new Vector3(0f, 0.95f, 0f);
        Assert.IsTrue(ShotImpacts.TryPlace(nearEdge, Vector3.back, World, ref size, out _, out _));
        Assert.Less(size, 0.2f, "À 5 cm du bord, une brûlure de 40 cm dépasserait.");

        float far = 0.4f;
        ShotImpacts.TryPlace(Origin + new Vector3(0f, 0.5f, 0f), Vector3.back, World, ref far, out _, out _);
        Assert.AreEqual(0.4f, far, 1e-4f, "Garde : loin du bord, elle garde sa taille.");
    }

    [Test]
    public void LesPlusAnciennes_SEffacentAvantDeCederLeurPlace()
    {
        Assert.AreEqual(1f, ShotImpacts.MarkStrength(0), "La plus récente.");
        Assert.AreEqual(1f, ShotImpacts.MarkStrength(ShotImpacts.MaxMarks - ShotImpacts.FadingMarks), "Encore loin de la fin.");
        float nearEnd = ShotImpacts.MarkStrength(ShotImpacts.MaxMarks - 4);
        Assert.That(nearEnd, Is.GreaterThan(0f).And.LessThan(0.5f), "Presque effacée.");
        Assert.Less(ShotImpacts.MarkStrength(ShotImpacts.MaxMarks - 1), ShotImpacts.MarkStrength(ShotImpacts.MaxMarks - 2), "De plus en plus, jusqu'à la dernière.");
    }

    [Test]
    public void LaTaille_SuitLaForceDeLArme_EtUneBruleureEstPlusLarge()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_ProjectArena/PlayerPrefab/Player.prefab");
        WeaponController weapon = prefab.GetComponentInChildren<WeaponController>(true);
        WeaponData assault = weapon.DataOf(0), sniper = weapon.DataOf(1), pump = weapon.DataOf(2), laser = weapon.DataOf(3);
        Assert.Less(ShotImpacts.MarkSize(pump), ShotImpacts.MarkSize(assault), "Un plomb fait un trou plus petit qu'une balle de fusil.");
        Assert.Less(ShotImpacts.MarkSize(assault), ShotImpacts.MarkSize(sniper), "Un fusil de précision en fait un plus gros.");
        Assert.IsTrue(laser.shotIsBeam, "Garde : une arme laser.");
        Assert.Greater(ShotImpacts.MarkSize(laser), ShotImpacts.MarkSize(sniper), "Une brûlure est plus large qu'un trou.");
    }

    [Test]
    public void LesMarquesDessinees_ToutDeSuite_SombresAuCentre_EtNeutresAuBord()
    {
        for (int variant = 0; variant < ShotImpacts.TileColumns; variant++)
        {
            Assert.Less(ShotImpactsBuilder.Hole(0f, 0f, variant), 0.1f, "Le cœur du trou.");
            Assert.Less(ShotImpactsBuilder.Burn(0f, 0f, variant), 0.1f, "Le centre de la brûlure.");
            // Tout le bord de la case : neutre, sinon on verrait un carré sur le décor.
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI / 32f;
                float x = Mathf.Cos(a), y = Mathf.Sin(a);
                Assert.AreEqual(0.5f, ShotImpactsBuilder.Hole(x, y, variant), 0.01f, "Le bord du trou.");
                Assert.AreEqual(0.5f, ShotImpactsBuilder.Burn(x, y, variant), 0.01f, "Le bord de la brûlure.");
                Assert.AreEqual(0f, ShotImpactsBuilder.HoleLip(x, y, variant), 0.01f, "Rien ne luit au bord de la case.");
                Assert.AreEqual(0f, ShotImpactsBuilder.BurnAsh(x, y, variant), 0.01f, "Rien ne luit au bord de la case.");
            }
        }
        Assert.AreNotEqual(ShotImpactsBuilder.Hole(0.3f, 0.2f, 0), ShotImpactsBuilder.Hole(0.3f, 0.2f, 1), "Les variantes diffèrent.");
    }

    [Test]
    public void SurUnMurNoir_LaLevreDUnTrou_EtLaCendreDUneBrulure_SeVoient_PasLeurCentre()
    {
        for (int variant = 0; variant < ShotImpacts.TileColumns; variant++)
        {
            Assert.AreEqual(0f, ShotImpactsBuilder.HoleLip(0f, 0f, variant), 0.01f, "Le cœur du trou reste noir.");
            Assert.AreEqual(0f, ShotImpactsBuilder.BurnAsh(0f, 0f, variant), 0.01f, "Le centre de la brûlure aussi.");
            float lip = 0f, ash = 0f;
            for (int i = 0; i < 64; i++)
            {
                for (float r = 0.2f; r < 0.8f; r += 0.02f)
                {
                    float x = r * Mathf.Cos(i * Mathf.PI / 32f), y = r * Mathf.Sin(i * Mathf.PI / 32f);
                    lip = Mathf.Max(lip, ShotImpactsBuilder.HoleLip(x, y, variant));
                    ash = Mathf.Max(ash, ShotImpactsBuilder.BurnAsh(x, y, variant));
                }
            }
            Assert.Greater(lip, 0.5f, "Sinon, un trou ne se voit pas sur le bas des murs.");
            Assert.Greater(ash, 0.5f, "Sinon, une brûlure non plus.");
        }
    }

    [Test]
    public void LeMateriauDesMarques_VaDansLaBuild_SaTextureEstLineaire()
    {
        var material = Resources.Load<Material>(ShotImpacts.MaterialName);
        Assert.IsNotNull(material, "Duel Arena > Armes > Préparer les impacts.");
        Assert.AreEqual("DuelArena/MarqueImpact", material.shader.name);
        Texture texture = material.GetTexture("_BaseMap");
        Assert.IsNotNull(texture);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
        Assert.IsFalse(importer.sRGBTexture, "En sRGB, 0,5 ne serait plus neutre : un carré clair autour de chaque marque.");
    }

    [Test]
    public void LaTexture_EstCelleQueDessineLeScript()
    {
        // Retoucher le dessin sans relancer le menu ne changerait rien au jeu, sans le dire.
        var material = Resources.Load<Material>(ShotImpacts.MaterialName);
        var png = new Texture2D(2, 2);
        Assert.IsTrue(png.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")))));
        int tile = png.width / ShotImpacts.TileColumns;
        Assert.AreEqual(tile * ShotImpacts.TileRows, png.height, "Huit cases carrées.");
        for (int column = 0; column < ShotImpacts.TileColumns; column++)
        {
            for (int sample = 0; sample < 12; sample++)
            {
                int i = (int)(tile * (0.2f + 0.05f * sample)), j = (int)(tile * (0.75f - 0.04f * sample));
                float x = (i + 0.5f) / tile * 2f - 1f, y = (j + 0.5f) / tile * 2f - 1f;
                Color hole = png.GetPixel(column * tile + i, tile + j), burn = png.GetPixel(column * tile + i, j);
                Assert.AreEqual(ShotImpactsBuilder.Hole(x, y, column), hole.r, 0.01f, "Le trou, en haut : Duel Arena > Armes > Préparer les impacts.");
                Assert.AreEqual(ShotImpactsBuilder.HoleLip(x, y, column), hole.g, 0.01f, "Sa lèvre.");
                Assert.AreEqual(ShotImpactsBuilder.Burn(x, y, column), burn.r, 0.01f, "La brûlure, en bas.");
                Assert.AreEqual(ShotImpactsBuilder.BurnAsh(x, y, column), burn.g, 0.01f, "Sa cendre.");
            }
        }
        Object.DestroyImmediate(png);
    }

    [Test]
    public void LesCasesDeLaTexture_SeSuivent_TrousEnHautBrulureEnBas()
    {
        var uvs = new Vector2[4];
        ShotImpacts.TileUvs(0, 0, uvs, 0);
        Assert.AreEqual(new Vector2(0f, 0.5f), uvs[0], "Le coin bas gauche de la case du premier trou.");
        Assert.AreEqual(new Vector2(0.25f, 1f), uvs[2], "Son coin haut droit.");
        ShotImpacts.TileUvs(3, 1, uvs, 0);
        Assert.AreEqual(new Vector2(0.75f, 0f), uvs[0], "La dernière brûlure.");
        Assert.AreEqual(new Vector2(1f, 0.5f), uvs[2]);
    }
}
