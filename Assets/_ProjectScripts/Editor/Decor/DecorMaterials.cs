using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Les matériaux de l'architecture des arènes (2026-10-27, shader DuelArena/Decor), écrits par ce script : on corrige
/// la recette ici, pas le matériau. Bible § 10 : un décor sombre et terne (noir, gris acier, béton, blanc cassé,
/// presque pas de jaune), jamais les couleurs des robots ; béton, métal peint, tôle, peinture usée, impacts.
/// </summary>
public static class DecorMaterials
{
    public const string Folder = "Assets/_ProjectArena/Decor/Materiaux";
    private const string ShaderName = "DuelArena/Decor";

    // La palette du décor (sRGB) : rien qui ressemble à une couleur de robot.
    public static readonly Color BlancCasse = new Color(0.78f, 0.77f, 0.72f);
    public static readonly Color Noir = new Color(0.055f, 0.057f, 0.062f);
    public static readonly Color Anthracite = new Color(0.16f, 0.17f, 0.18f);
    public static readonly Color GrisBleu = new Color(0.13f, 0.14f, 0.155f);
    public static readonly Color Granulat = new Color(0.55f, 0.545f, 0.52f, 0f);
    public static readonly Color AcierNu = new Color(0.56f, 0.57f, 0.58f, 0.85f);

    /// <summary>Ce qu'un matériau du décor règle ; le reste garde les défauts du shader.</summary>
    public sealed class Recette
    {
        public string nom;
        public Color couleur = new Color(0.45f, 0.45f, 0.44f);
        public float rug = 0.85f, metal;
        public int motif;
        public Vector4 panneau = new Vector4(2.4f, 1.2f, 0.012f, 0.35f);
        public Vector4 trous;
        public float variationPanneau = 0.05f;
        public float nuages = 0.07f, echelleNuages = 0.7f;
        public Vector4 soubassement;
        public Color couleurSoubassement = GrisBleu, couleurLisere = BlancCasse;
        public Vector4 hachures;
        public Color couleurHachures = Noir;
        public Vector4 crasse = new Vector4(0.6f, 0.5f, 0.2f, 0.3f);
        public Vector4 impacts = new Vector4(0.4f, 0f, 0.03f, 2.6f);
        public Color couleurEclats = Granulat;
        public float eclats = 0.4f, huiles;
        public Color emission = Color.black;
        public float forceEmission;
        public float contour = 2f;
        public bool contourLisse = true;
    }

    /// <summary>Les recettes, par nom de matériau.</summary>
    public static readonly Dictionary<string, Recette> Recettes = Build();

    private static Dictionary<string, Recette> Build()
    {
        var list = new List<Recette>
        {
            // Les murs de l'arène : béton banché gris moyen, ses panneaux et ses trous de banche, un soubassement
            // gris-bleu sombre et son liseré blanc cassé, les impacts de dix ans de tirs, la crasse au pied.
            new Recette
            {
                nom = "Decor_BetonBanche", couleur = new Color(0.43f, 0.43f, 0.42f), rug = 0.88f,
                motif = 1, panneau = new Vector4(2.4f, 1.2f, 0.012f, 0.32f), trous = new Vector4(2, 2, 0.013f, 0.75f),
                soubassement = new Vector4(1.0f, 1.0f, 1.07f, 0.35f),
                crasse = new Vector4(0.7f, 0.55f, 0.25f, 0.4f), impacts = new Vector4(0.42f, 0.16f, 0.035f, 2.5f),
                eclats = 0.3f,
            },
            // Le tunnel de tir au milieu et les murs des postes de départ : un béton plus sombre, un soubassement
            // noir plus haut.
            new Recette
            {
                nom = "Decor_BetonSombre", couleur = new Color(0.33f, 0.33f, 0.33f), rug = 0.9f,
                motif = 1, panneau = new Vector4(2.4f, 1.2f, 0.012f, 0.3f), trous = new Vector4(2, 2, 0.013f, 0.7f),
                soubassement = new Vector4(1.25f, 1.25f, 1.33f, 0.3f), couleurSoubassement = Noir,
                crasse = new Vector4(0.7f, 0.55f, 0.3f, 0.4f), impacts = new Vector4(0.36f, 0.22f, 0.035f, 2.6f),
                eclats = 0.3f,
            },
            // Les barricades d'essai : un béton clair barré de hachures noires, comme un obstacle de test.
            new Recette
            {
                nom = "Decor_BetonBarricade", couleur = new Color(0.55f, 0.55f, 0.53f), rug = 0.85f,
                motif = 1, panneau = new Vector4(1.5f, 0.75f, 0.01f, 0.25f), variationPanneau = 0.03f,
                hachures = new Vector4(0.12f, 2.0f, 0.42f, 0.25f),
                crasse = new Vector4(0.45f, 0.6f, 0.15f, 0.45f), impacts = new Vector4(0.3f, 0.2f, 0.03f, 2f),
                eclats = 0.35f,
            },
            // L'acier peint : parapets des tours, plaques pare-balles, flancs des plateformes. Des tôles rivetées,
            // anthracite, qui s'écaillent jusqu'au métal sur les arêtes.
            new Recette
            {
                nom = "Decor_AcierPeint", couleur = Anthracite, rug = 0.62f,
                motif = 2, panneau = new Vector4(1.0f, 1.0f, 0.005f, 0.45f), variationPanneau = 0f,
                crasse = new Vector4(0.5f, 0.45f, 0.15f, 0.3f), impacts = new Vector4(0.35f, 0.14f, 0.022f, 2.6f),
                couleurEclats = AcierNu, eclats = 0.55f,
            },
            // La tôle striée : dessus des plateformes et des tours, marches, sol de la passerelle.
            new Recette
            {
                nom = "Decor_ToleStriee", couleur = new Color(0.34f, 0.35f, 0.36f), rug = 0.5f, metal = 0.65f,
                motif = 4, nuages = 0.12f, echelleNuages = 0.9f,
                crasse = new Vector4(0.2f, 0.3f, 0f, 0.35f), couleurEclats = AcierNu, eclats = 0.3f,
            },
            // Le sol de la salle : une dalle de béton sciée tous les 4 m, des taches d'huile.
            new Recette
            {
                nom = "Decor_Dalle", couleur = new Color(0.235f, 0.235f, 0.235f), rug = 0.82f,
                motif = 3, panneau = new Vector4(4f, 4f, 0.008f, 0.42f), variationPanneau = 0.06f,
                nuages = 0.1f, echelleNuages = 0.3f, huiles = 0.4f,
                crasse = new Vector4(0.01f, 0f, 0f, 0.12f), eclats = 0f,
            },
            // Les murs de la salle, au-dessus de la tribune : un béton plus sombre, la pénombre.
            new Recette
            {
                nom = "Decor_BetonSalle", couleur = new Color(0.25f, 0.25f, 0.255f), rug = 0.9f,
                motif = 1, panneau = new Vector4(2.4f, 1.2f, 0.012f, 0.3f), trous = new Vector4(2, 2, 0.015f, 0.7f),
                crasse = new Vector4(0.8f, 0.5f, 0.35f, 0.3f), eclats = 0.4f,
            },
            // Le plafond : un béton presque noir, en grands panneaux.
            new Recette
            {
                nom = "Decor_Plafond", couleur = new Color(0.12f, 0.12f, 0.125f), rug = 0.92f,
                motif = 1, panneau = new Vector4(3f, 3f, 0.02f, 0.4f), crasse = new Vector4(0.01f, 0f, 0f, 0f), eclats = 0f,
            },
            // La charpente : des poutres d'acier peint sombre.
            new Recette
            {
                nom = "Decor_Charpente", couleur = new Color(0.10f, 0.105f, 0.11f), rug = 0.55f, metal = 0.35f,
                crasse = new Vector4(0.01f, 0f, 0f, 0.5f), couleurEclats = AcierNu, eclats = 0.35f, contourLisse = false,
            },
            // Le garde-corps de la tribune : de l'acier peint, une bande de hachures en haut, vue de l'arène. Un gris
            // neutre, pas le blanc cassé : sous les lampes tièdes, il virait au jaune (presque pas de jaune, bible § 10).
            // Les cubes d'Unity de la tribune n'ont pas de couleur de sommet (le shader y lit « bord » partout) : pas
            // d'éclats d'arêtes sur eux.
            new Recette
            {
                nom = "Decor_GardeCorps", couleur = Anthracite, rug = 0.62f,
                motif = 2, panneau = new Vector4(1.5f, 1.0f, 0.005f, 0.4f),
                hachures = new Vector4(5.32f, 5.52f, 0.3f, 0.35f), couleurHachures = new Color(0.66f, 0.66f, 0.66f),
                crasse = new Vector4(0.01f, 0f, 0f, 0.3f), couleurEclats = AcierNu, eclats = 0f,
            },
            new Recette
            {
                nom = "Decor_AcierPeintTribune", couleur = Anthracite, rug = 0.62f,
                motif = 2, panneau = new Vector4(1.0f, 1.0f, 0.005f, 0.45f), variationPanneau = 0f,
                crasse = new Vector4(0.5f, 0.45f, 0.15f, 0.3f), couleurEclats = AcierNu, eclats = 0f,
            },
            // Le mur du fond de la tribune : béton sombre, soubassement noir à hauteur de spectateur.
            new Recette
            {
                nom = "Decor_MurTribune", couleur = new Color(0.27f, 0.27f, 0.275f), rug = 0.9f,
                motif = 1, panneau = new Vector4(2.4f, 1.2f, 0.012f, 0.3f), trous = new Vector4(2, 2, 0.013f, 0.7f),
                soubassement = new Vector4(5.6f, 5.6f, 5.67f, 0.45f), couleurSoubassement = Noir,
                crasse = new Vector4(0.01f, 0f, 0.25f, 0.3f), eclats = 0f,
            },
            // Les blocs fissurés de la tribune : un béton clair, déjà entamé.
            new Recette
            {
                nom = "Decor_BetonFissure", couleur = new Color(0.52f, 0.51f, 0.49f), rug = 0.92f,
                motif = 1, panneau = new Vector4(0.6f, 0.45f, 0.008f, 0.5f), variationPanneau = 0.1f,
                crasse = new Vector4(0.01f, 0f, 0.3f, 0.5f), impacts = new Vector4(0.25f, 0.5f, 0.04f, 9f), eclats = 0f,
            },
            // Les caisses de matériel (en attendant leur modèle) : de l'acier peint anthracite, riveté.
            new Recette
            {
                nom = "Decor_Caisse", couleur = new Color(0.19f, 0.2f, 0.205f), rug = 0.6f,
                motif = 2, panneau = new Vector4(0.5f, 0.5f, 0.006f, 0.5f),
                crasse = new Vector4(0.4f, 0.5f, 0.1f, 0.5f), impacts = new Vector4(0.3f, 0.1f, 0.02f, 2.5f),
                couleurEclats = AcierNu, eclats = 0.6f,
            },
            // Les lampes : une face blanche qui éclaire (la lumière du jeu vient de Lights à côté).
            new Recette
            {
                nom = "Decor_LampeFroide", couleur = new Color(0.9f, 0.93f, 1f), rug = 0.3f, motif = 0, nuages = 0f,
                crasse = Vector4.zero, eclats = 0f, emission = new Color(0.85f, 0.92f, 1f), forceEmission = 6f, contour = 0f,
            },
            new Recette
            {
                nom = "Decor_LampeTiede", couleur = new Color(1f, 0.95f, 0.88f), rug = 0.3f, motif = 0, nuages = 0f,
                crasse = Vector4.zero, eclats = 0f, emission = new Color(1f, 0.9f, 0.78f), forceEmission = 4.5f, contour = 0f,
            },
            // Le liseré qui luit sous le bord de la tribune : il dessine le tour de l'arène, il n'éclaire pas le jeu.
            new Recette
            {
                nom = "Decor_LedTribune", couleur = new Color(0.9f, 0.93f, 1f), rug = 0.3f, motif = 0, nuages = 0f,
                crasse = Vector4.zero, eclats = 0f, emission = new Color(0.8f, 0.9f, 1f), forceEmission = 3.5f, contour = 0f,
            },
            // Le rouge de l'énergie et du danger (bible § 12) : le voyant « tir en cours ».
            new Recette
            {
                nom = "Decor_VoyantRouge", couleur = new Color(0.9f, 0.1f, 0.08f), rug = 0.3f, motif = 0, nuages = 0f,
                crasse = Vector4.zero, eclats = 0f, emission = new Color(1f, 0.08f, 0.05f), forceEmission = 4f, contour = 0f,
            },
        };
        var result = new Dictionary<string, Recette>();
        foreach (Recette r in list) result[r.nom] = r;
        return result;
    }

    /// <summary>Crée ou met à jour tous les matériaux des recettes ; rend le dictionnaire nom → matériau.</summary>
    public static Dictionary<string, Material> CreateAll()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) throw new System.InvalidOperationException($"Shader {ShaderName} introuvable.");
        System.IO.Directory.CreateDirectory(Folder);
        var result = new Dictionary<string, Material>();
        foreach (Recette r in Recettes.Values)
        {
            var m = new Material(shader);
            Apply(m, r);
            Material saved = RobotChassisBuilder.SaveMaterial($"{Folder}/{r.nom}.mat", m);
            // Le contour (DecorOutlineFeature) : pas sur ce qui n'en a pas. Posé sur le matériau enregistré : la recopie
            // de SaveMaterial ne reprend pas les passes coupées.
            saved.SetShaderPassEnabled(DecorOutlineFeature.LightMode, r.contour > 0f);
            result[r.nom] = saved;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    public static void Apply(Material m, Recette r)
    {
        m.SetFloat("_Espace", 0f);
        m.SetColor("_Base", r.couleur);
        m.SetFloat("_Rug", r.rug);
        m.SetFloat("_Metal", r.metal);
        m.SetFloat("_Motif", r.motif);
        m.SetVector("_Panneau", r.panneau);
        m.SetVector("_Trous", r.trous);
        m.SetFloat("_VariationPanneau", r.variationPanneau);
        m.SetFloat("_Nuages", r.nuages);
        m.SetFloat("_EchelleNuages", r.echelleNuages);
        m.SetVector("_Soubassement", r.soubassement);
        m.SetColor("_CouleurSoubassement", r.couleurSoubassement);
        m.SetColor("_CouleurLisere", r.couleurLisere);
        m.SetVector("_Hachures", r.hachures);
        m.SetColor("_CouleurHachures", r.couleurHachures);
        m.SetVector("_Crasse", r.crasse);
        m.SetVector("_Impacts", r.impacts);
        m.SetColor("_CouleurEclats", r.couleurEclats);
        m.SetFloat("_EclatsArchi", r.eclats);
        m.SetFloat("_Huiles", r.huiles);
        m.SetColor("_EmissionCouleur", r.emission);
        m.SetFloat("_ForceEmission", r.forceEmission);
        m.SetFloat("_Contour", r.contour);
        m.SetFloat("_ContourLisse", r.contourLisse ? 1f : 0f);
        m.globalIlluminationFlags = r.forceEmission > 0f ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
    }
}
