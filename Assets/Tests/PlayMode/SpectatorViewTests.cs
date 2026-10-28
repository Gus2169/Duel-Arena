#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// La vue subjective d'un spectateur (SpectatorView, 2026-10-27) : la copie du robot à mains nues, sous la
/// caméra, placée comme le jeu la place (SpectatorView.Place, réglages du préfab). Au repos, elle ne se voit
/// pas ; en garde, ses deux poings tiennent le bas de l'écran sans en boucher le centre ; pour chacun des
/// trois châssis. Tête dans l'œil, la caméra était dans le buste du CT-01 : un spectateur ne voyait plus
/// rien de la tribune (vu à deux instances le 2026-10-27, le défaut datait du CT-01).
///
/// On mesure ce qui se voit : les bras seuls, rendus par une caméra au champ des leurs, sur un fond
/// transparent (comme WeaponGripTests).
/// </summary>
public class SpectatorViewTests
{
    private static readonly ChassisId[] AllChassis = { ChassisId.CT01, ChassisId.Prototype, ChassisId.OS02 };

    private GameObject instance;
    private GameObject cameraGo;

    /// <summary>Le rendu mêlé, celui du joueur par défaut et le pire pour ce que couvrent les bras (le contour
    /// élargit leur silhouette).</summary>
    [SetUp]
    public void FixRenderStyle() => RenderStyle.Set(RenderStyleMode.Meles);

    [TearDown]
    public void TearDown()
    {
        if (instance != null) Object.DestroyImmediate(instance);
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
        RenderStyle.Set(RenderStyleMode.LowPolyPropre);
    }

    [UnityTest]
    public IEnumerator EnVueSubjective_LeSpectateurVoitDevantLui_EtSaGardeTientLeBasDeLEcran()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestCharacter.PlayerPrefabPath);
        // Les valeurs du préfab priment sur les défauts du code.
        var spectatorView = new UnityEditor.SerializedObject(prefab.GetComponent<SpectatorView>());
        Vector3 restOffset = spectatorView.FindProperty("viewOffset").vector3Value;
        Vector3 guardOffset = spectatorView.FindProperty("guardViewOffset").vector3Value;
        float fieldOfView = new UnityEditor.SerializedObject(prefab.GetComponent<FirstPersonView>()).FindProperty("armsFieldOfView").floatValue;

        // La copie comme SpectatorView la construit : à mains nues, ni arme ni gant.
        instance = TestCharacter.InstantiateModel(out _);
        cameraGo = new GameObject("CameraTest");
        int layer = LayerMask.NameToLayer(FirstPersonView.LayerName);
        GameObject copy = FirstPersonView.BuildView(instance, cameraGo.transform, layer, out Animator arms, out _);
        foreach (WeaponModel weapon in copy.GetComponentsInChildren<WeaponModel>(true)) weapon.gameObject.SetActive(false);
        HeldWeapons.WearTaserGlove(arms, false);
        instance.SetActive(false);
        Transform head = arms.GetBoneTransform(HumanBodyBones.Head);

        const int width = 480, height = 270;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << layer;
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 5f;
        camera.aspect = (float)width / height;
        camera.allowHDR = false;   // sans HDR, le tampon d'URP garde la couche alpha
        camera.allowMSAA = false;

        // La part de l'écran couverte, celle du centre (le tiers du milieu, dans les deux sens) et celle de
        // la moitié du haut.
        void Measure(out float screen, out float center, out float top)
        {
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;
            Color32[] colors = pixels.GetPixels32();
            int covered = 0, middle = 0, upper = 0;
            for (int y = 0; y < height; y++)   // la première ligne est celle du bas
                for (int x = 0; x < width; x++)
                {
                    if (colors[y * width + x].a < 128) continue;
                    covered++;
                    if (x >= width / 3 && x < width * 2 / 3 && y >= height / 3 && y < height * 2 / 3) middle++;
                    if (y >= height / 2) upper++;
                }
            screen = (float)covered / (width * height);
            center = (float)middle / (width / 3 * (height / 3));
            top = (float)upper / (width * (height - height / 2));
        }

        int gestures = PlayerAnimator.SpectatorGesturesLayer;
        arms.SetFloat("MainsNues", 1f);
        arms.SetLayerWeight(1, 0f); // debout, le haut du corps du duelliste se tait (PlayerAnimator)
        var report = new System.Text.StringBuilder();
        try
        {
            foreach (ChassisId chassis in AllChassis)
            {
                RobotChassis.Apply(copy.transform, chassis);
                string name = RobotChassis.NameOf(chassis);

                // Au repos : les bras pendent sous l'écran, le buste reste derrière la caméra.
                arms.SetBool("Garde", false);
                arms.SetLayerWeight(gestures, 0f);
                yield return new WaitForSeconds(0.3f);
                SpectatorView.Place(copy.transform, head, restOffset);
                Measure(out float restScreen, out _, out _);

                // En garde : les deux poings levés.
                arms.SetBool("Garde", true);
                arms.SetLayerWeight(gestures, 1f);
                yield return new WaitForSeconds(0.5f);
                SpectatorView.Place(copy.transform, head, guardOffset);
                Measure(out float guardScreen, out float center, out float top);
                report.AppendLine($"{name} : repos {restScreen:P1}, garde {guardScreen:P1} (centre {center:P1}, haut {top:P1})");

                Assert.Less(restScreen, 0.02f, $"{name} : au repos, le spectateur voit son propre robot ({restScreen:P0} de l'écran) : la caméra est dans son buste.");
                Assert.Greater(guardScreen, 0.02f, $"{name} : en garde, ses poings ne se voient pas.");
                Assert.Less(guardScreen, 0.2f, $"{name} : en garde, ses bras couvrent {guardScreen:P0} de l'écran.");
                Assert.Less(center, 0.05f, $"{name} : en garde, ses poings bouchent le centre de l'écran ({center:P0}).");
                Assert.Less(top, 0.01f, $"{name} : en garde, ses bras montent dans le haut de l'écran ({top:P0}).");
            }
        }
        finally
        {
            Debug.Log("[SPECTATEUR] vue subjective :\n" + report);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }
    }
}
#endif
