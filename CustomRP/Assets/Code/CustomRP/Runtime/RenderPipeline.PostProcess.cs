using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace CustomRP
{
    public partial class RenderPipeline
    {
        Material m_FinalPassMaterial;

        void InitializePostProcess()
        {
            m_FinalPassMaterial = CoreUtils.CreateEngineMaterial("CustomRP/PostProcessPass");
        }

        void CleanupPostProcess()
        {
            CoreUtils.Destroy(m_FinalPassMaterial);
            m_FinalPassMaterial = null;
        }

        class FinalPassData
        {
            public Material finalPassMaterial;
            public Camera camera;

            public TextureHandle source;
            public TextureHandle destination;
        }

        TextureHandle RenderPostProcess(RenderGraph renderGraph, in PrepassOutput prepassOutput, TextureHandle backBuffer, CullingResults cullResults, Camera camera)
        {
            using (var builder = renderGraph.AddRenderPass<FinalPassData>("Final Pass", out var passData, ProfilingSampler.Get(ProfileID.FinalPost)))
            {
                builder.AllowRendererListCulling(false);

                // TODO: This is temporary, we're using the GBuffer Albedo render target
                passData.source = builder.ReadTexture(prepassOutput.gbuffer.mrt[0]);
                passData.destination = builder.WriteTexture(backBuffer);
                passData.finalPassMaterial = m_FinalPassMaterial;
                passData.camera = camera;

                builder.SetRenderFunc(
                    (FinalPassData data, RenderGraphContext ctx) =>
                    {
                        Material finalPassMaterial = data.finalPassMaterial;
                        finalPassMaterial.SetTexture("_InputTexture", data.source);

                        CoreUtils.SetRenderTarget(ctx.cmd, data.destination);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, finalPassMaterial, 0, MeshTopology.Triangles, 3, 1);
                    });
            }

            return backBuffer;
        }
    }
}
