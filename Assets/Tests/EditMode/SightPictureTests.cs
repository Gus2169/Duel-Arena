using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Ce qu'on voit en visée, dans l'armurerie (2026-10-27, demandé par l'utilisateur, comme dans Rainbow Six) :
/// chaque viseur monté sur l'arme, vu de l'œil qui vise, son réticule allumé, et la mire de l'arme sans viseur
/// (MenuThumbnails.SightPicture).
///
/// Ce qui casse ici ne lève aucune erreur, l'image est seulement fausse : une caméra posée ailleurs qu'à l'œil
/// de la vue subjective, ou qui regarde de travers, montre un viseur sans réticule (il n'est dessiné qu'à
/// travers la vitre, dans l'axe du viseur) ou un réticule décentré ; un viseur dont le corps bouche la fenêtre
/// vue de l'œil ; une mire dont le guidon n'est plus dans l'axe ; une image vide (l'arme hors du layer de la
/// caméra) ; une forme de réticule ajoutée dans Blender sans nom dans l'armurerie.
/// </summary>
public class SightPictureTests
{
    private const string PlayerPrefabPath = "Assets/_ProjectArena/PlayerPrefab/Player.prefab";
    private const int Rifle = 0, Pistol = 10;   // le FA-07, le PA-05 (tenu à bout de bras)
    private const int Width = 640, Height = 320;
    private static readonly int ReticleShapeId = Shader.PropertyToID("_Forme");
    private static readonly int ReticleColorId = Shader.PropertyToID("_CouleurReticule");

    private GameObject host;
    private MenuThumbnails thumbnails;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("VignettesDeTest");
        thumbnails = host.AddComponent<MenuThumbnails>();
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) Object.DestroyImmediate(host);
    }

    private static IReadOnlyList<WeaponData> Loadout() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<WeaponController>(true).Loadout;

    private static WeaponAttachment Sight(WeaponData data, int sight)
    {
        GameObject prefab = data.attachmentCatalog != null ? data.attachmentCatalog.SightPrefab(sight) : null;
        Assert.IsNotNull(prefab, $"Le viseur {sight} n'est plus dans le catalogue.");
        var attachment = prefab.GetComponent<WeaponAttachment>();
        Assert.IsNotNull(attachment.Lens, $"{attachment.Code} n'a plus de vitre.");
        return attachment;
    }

    /// <summary>Ce qu'on voit en visée avec le viseur `sight` (0 : la mire de l'arme), pixel par pixel, de bas
    /// en haut.</summary>
    private Color[] Picture(WeaponData data, int slot, int sight)
    {
        RenderTexture rendered = thumbnails.RenderSightPictureNow(data, WeaponFinish.Origine,
            WeaponAttachments.None.WithSight(slot, sight), slot, Width, Height);
        Assert.IsNotNull(rendered, $"{data.Designation}, viseur {sight} : pas d'image.");
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rendered;
            texture.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
            texture.Apply();
            return texture.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(texture);
            rendered.Release();
            Object.DestroyImmediate(rendered);
        }
    }

    private static bool NearCenter(int x, int y)
        => new Vector2(x + 0.5f - Width * 0.5f, y + 0.5f - Height * 0.5f).magnitude <= Height * 0.04f;

    /// <summary>Un pixel du réticule : couvert, vif, de la teinte de sa couleur (le corps du viseur est gris, sa
    /// vitre presque transparente).</summary>
    private static bool IsReticle(Color pixel, float hue)
    {
        if (pixel.a < 0.5f) return false;
        Color.RGBToHSV(pixel, out float h, out float saturation, out float value);
        float gap = Mathf.Abs(h - hue);
        return Mathf.Min(gap, 1f - gap) < 0.06f && saturation > 0.45f && value > 0.6f;
    }

    [Test]
    public void ChaqueViseur_VuDeLOeil_MontreSonReticuleAuCentre()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        var cases = new List<(int slot, int sight)>();
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            cases.Add((Rifle, sight));
            // Le pistolet, à bout de bras : l'œil est à 40 cm de la mire.
            if (WeaponAttachments.Accepts(loadout[Pistol], AttachmentCategory.Sight, sight)) cases.Add((Pistol, sight));
        }
        Assert.Greater(cases.Count, WeaponAttachments.SightCount, "Le pistolet ne prend plus aucun viseur : le bout de bras n'est plus vérifié.");

        foreach ((int slot, int sight) in cases)
        {
            WeaponData data = loadout[slot];
            Assert.IsTrue(WeaponAttachments.Accepts(data, AttachmentCategory.Sight, sight), $"{data.Designation} ne prend plus le viseur {sight}.");
            WeaponAttachment attachment = Sight(data, sight);
            Color.RGBToHSV(attachment.Lens.sharedMaterial.GetColor(ReticleColorId), out float hue, out _, out _);
            string what = $"{data.Designation} avec le {attachment.Code}";

            Color[] pixels = Picture(data, slot, sight);
            int reticle = 0, centered = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (!IsReticle(pixels[y * Width + x], hue)) continue;
                reticle++;
                if (NearCenter(x, y)) centered++;
            }
            Assert.Greater(reticle, 0, $"{what} : aucun réticule vu de l'œil.");
            Assert.Greater(centered, 0, $"{what} : le réticule n'est pas au centre de l'image ({reticle} pixels ailleurs).");
            Assert.Less(reticle, Width * Height * 0.15f, $"{what} : le réticule couvre l'image.");
        }
    }

    [Test]
    public void SansViseur_LaMireDeLArme_MetSonGuidonDansLAxe()
    {
        IReadOnlyList<WeaponData> loadout = Loadout();
        int weapons = 0;
        for (int slot = 0; slot < loadout.Count; slot++)
        {
            WeaponData data = loadout[slot];
            // Une arme à lunette n'a pas de bande des viseurs.
            if (!WeaponAttachments.AcceptsAny(data, AttachmentCategory.Sight)) continue;
            weapons++;

            Color[] pixels = Picture(data, slot, 0);
            bool sightOnAxis = false;
            for (int y = 0; y < Height && !sightOnAxis; y++)
            for (int x = 0; x < Width && !sightOnAxis; x++)
                sightOnAxis = pixels[y * Width + x].a > 0.5f && NearCenter(x, y);
            Assert.IsTrue(sightOnAxis, $"{data.Designation} : rien de l'arme au centre de sa mire, vue de l'œil.");
            // Au-dessus des organes de visée, le vide : l'œil n'est pas dans l'arme.
            Assert.Less(pixels[(Height - 1) * Width].a, 0.1f, $"{data.Designation} : le coin haut gauche de la mire est couvert.");
            Assert.Less(pixels[Height * Width - 1].a, 0.1f, $"{data.Designation} : le coin haut droit de la mire est couvert.");
        }
        Assert.GreaterOrEqual(weapons, 6, "Moins de six armes prennent un viseur : le test ne voit plus grand-chose.");
    }

    [Test]
    public void ChaqueReticule_ASonNomDansLArmurerie()
    {
        WeaponData rifle = Loadout()[Rifle];
        for (int sight = 1; sight <= WeaponAttachments.SightCount; sight++)
        {
            WeaponAttachment attachment = Sight(rifle, sight);
            int shape = Mathf.RoundToInt(attachment.Lens.sharedMaterial.GetFloat(ReticleShapeId));
            Assert.IsNotEmpty(WeaponCustomizationController.ReticleShapeLabel(shape),
                $"{attachment.Code} : son réticule (forme {shape}) n'a pas de nom dans l'armurerie.");
        }
    }
}
