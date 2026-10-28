using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Des vues de contrôle du décor, rendues par une caméra temporaire de la scène ouverte, post-traitement compris
/// (2026-10-27) : on ne juge le décor que sur l'image. Elles vont dans le dossier qu'on donne (le scratchpad, pas le
/// projet).
/// </summary>
public static class DecorPreview
{
    /// <summary>Un point de vue : un nom, l'œil, ce qu'il regarde, le champ vertical.</summary>
    public readonly struct View
    {
        public readonly string name;
        public readonly Vector3 eye, target;
        public readonly float fov;

        public View(string name, Vector3 eye, Vector3 target, float fov = 60f)
        {
            this.name = name;
            this.eye = eye;
            this.target = target;
            this.fov = fov;
        }
    }

    /// <summary>Les vues de l'arène de test : à hauteur d'œil d'un robot debout (1,65 m), du haut d'une tour, de la
    /// tribune, et l'ensemble.</summary>
    public static readonly View[] ArenaViews =
    {
        new View("1_depart_sud", new Vector3(0.5f, 1.65f, 3.2f), new Vector3(-2f, 1.3f, 30f)),
        new View("2_centre", new Vector3(-9f, 1.65f, 23f), new Vector3(6f, 1.4f, 50f)),
        new View("3_tour_ouest", new Vector3(-18.5f, 3.65f, 44.2f), new Vector3(8f, 0.5f, 24f)),
        new View("4_tribune", new Vector3(-30.2f, 6.15f, 13.4f), new Vector3(6f, 0f, 36f)),
        new View("5_ensemble", new Vector3(14f, 13f, -6f), new Vector3(-3f, 0f, 34f), 62f),
        new View("6_barricades", new Vector3(18f, 1.65f, 20.5f), new Vector3(22f, 0.8f, 27f)),
    };

    /// <summary>Rend chaque vue dans `folder` (`<préfixe><vue>.png`). Rend la liste des fichiers.</summary>
    public static List<string> Capture(string folder, string prefix, IEnumerable<View> views, int width = 1600, int height = 900)
    {
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        var go = new GameObject("CaptureDecor (temporaire)") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 600f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = ~(1 << PreviewLayer.Index);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            foreach (View view in views)
            {
                cam.fieldOfView = view.fov;
                cam.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation((view.target - view.eye).normalized, Vector3.up));
                string path = Path.Combine(folder, prefix + view.name + ".png");
                Render(cam, width, height, path);
                files.Add(path);
            }
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
        return files;
    }

    /// <summary>Rend la caméra dans un PNG de `width` × `height`.</summary>
    public static void Render(Camera cam, int width, int height, string path)
    {
        cam.aspect = (float)width / height;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        resolved.Create();
        try
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else
            {
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = null;
            }
            Graphics.Blit(target, resolved);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            target.Release();
            resolved.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(resolved);
        }
    }
}
