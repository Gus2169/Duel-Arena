using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Le contour du décor en cel-shading et en mêlés (2026-10-27, le réglage du rendu) : la passe « DecorContour » du
/// shader DuelArena/Decor (une coque retournée), dessinée après les opaques, seulement quand le rendu en a un
/// (RenderStyle.Outlines). Sous l'étiquette ordinaire (SRPDefaultUnlit), URP la dessinait pour chaque objet même vide :
/// elle coûtait en low poly propre, et il fallait la couper matériau par matériau. Ajoutée au renderer d'URP du jeu
/// par Duel Arena > Rendu > Préparer le réglage du rendu.
/// </summary>
public sealed class DecorOutlineFeature : ScriptableRendererFeature
{
    public const string LightMode = "DecorContour";

    private OutlinePass pass;

    // L'étiquette se crée ici, pas dans un champ statique : URP construit la fonction en désérialisant le renderer,
    // où Unity refuse ShaderTagId (« TagToID is not allowed to be called from a ScriptableObject constructor ») ; la
    // création du pipeline échouait à mi-chemin, puis chaque nouvelle tentative (« Blitter is already initialized »),
    // et plus aucune caméra ne dessinait (vu le 2026-10-27).
    public override void Create()
    {
        pass = new OutlinePass(new ShaderTagId(LightMode)) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (RenderStyle.Outlines) renderer.EnqueuePass(pass);
    }

    private sealed class OutlinePass : ScriptableRenderPass
    {
        private readonly ShaderTagId tag;

        public OutlinePass(ShaderTagId tag) => this.tag = tag;

        private sealed class PassData
        {
            public RendererListHandle list;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var lights = frameData.Get<UniversalLightData>();

            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(tag, rendering, camera, lights, camera.defaultOpaqueSortFlags);
            var filtering = new FilteringSettings(RenderQueueRange.opaque);
            var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);

            using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Contour du décor", out PassData data);
            data.list = renderGraph.CreateRendererList(parameters);
            builder.UseRendererList(data.list);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            builder.SetRenderFunc((PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
        }
    }
}
