using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP pass that applies a non-animated screen grain only to the camera that
/// owns StaticScreenGrainController. It runs after normal post processing.
/// </summary>
public sealed class StaticScreenGrainRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        public Material material;
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    }

    [SerializeField] public Settings settings = new Settings();

    private StaticScreenGrainPass pass;

    public override void Create()
    {
        pass = new StaticScreenGrainPass
        {
            renderPassEvent = settings.injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        StaticScreenGrainController controller = StaticScreenGrainController.Active;
        if (controller == null || !controller.IsEnabledFor(renderingData.cameraData.camera) || settings.material == null)
        {
            return;
        }

        controller.ApplyTo(settings.material);
        pass.Setup(renderer, settings.material);
        renderer.EnqueuePass(pass);
    }

    private sealed class StaticScreenGrainPass : ScriptableRenderPass
    {
        private static readonly int TemporaryColorTexture = Shader.PropertyToID("_StaticScreenGrainTemporaryColor");
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Static Screen Grain");

        private ScriptableRenderer renderer;
        private Material material;
        private RenderTextureDescriptor descriptor;

        public void Setup(ScriptableRenderer sourceRenderer, Material sourceMaterial)
        {
            renderer = sourceRenderer;
            material = sourceMaterial;
        }

        public override void OnCameraSetup(CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            commandBuffer.GetTemporaryRT(TemporaryColorTexture, descriptor, FilterMode.Point);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            CommandBuffer commandBuffer = CommandBufferPool.Get();
            using (new ProfilingScope(commandBuffer, ProfilingSampler))
            {
                commandBuffer.Blit(renderer.cameraColorTarget, TemporaryColorTexture, material);
                commandBuffer.Blit(TemporaryColorTexture, renderer.cameraColorTarget);
            }

            context.ExecuteCommandBuffer(commandBuffer);
            CommandBufferPool.Release(commandBuffer);
        }

        public override void OnCameraCleanup(CommandBuffer commandBuffer)
        {
            commandBuffer.ReleaseTemporaryRT(TemporaryColorTexture);
        }
    }
}
