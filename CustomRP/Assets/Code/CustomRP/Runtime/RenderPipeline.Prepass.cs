using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace CustomRP
{
    public partial class RenderPipeline
    {
        struct GBufferOutput
        {
            public TextureHandle[] mrt;
            public int gBufferCount;
        }

        struct PrepassOutput
        {
            public TextureHandle depthBuffer;
            public TextureHandle normalBuffer;

            public GBufferOutput gbuffer;

            public TextureHandle resolvedDepthBuffer;

            public TextureHandle stencilBuffer;
        }

        PrepassOutput RenderPrepass(RenderGraph renderGraph, CullingResults cullingResults, Camera camera)
        {
            var result = new PrepassOutput();

            bool msaa = false || camera.allowMSAA; // TODO

            Vector2 currentResolution = new Vector2(camera.pixelWidth, camera.pixelHeight);

            result.depthBuffer = CreateDepthBuffer(renderGraph, currentResolution, true, MSAASamples.None);
            result.stencilBuffer = result.depthBuffer;

            RenderDepthPrepass(renderGraph, cullingResults, camera, ref result);

            ResolvePrepassBuffer(renderGraph, camera, ref result);

            RenderGBuffer(renderGraph, cullingResults, camera, ref result);

            return result;
        }

        class DepthPrePassData
        {
            public RendererListHandle rendererList;
            public Rect viewport;
        }

        void RenderDepthPrepass(RenderGraph renderGraph, CullingResults cullingResults, Camera camera, ref PrepassOutput prepassOutput)
        {
            using (var builder = renderGraph.AddRenderPass<DepthPrePassData>("Depth Prepass", out var passData, ProfilingSampler.Get(ProfileID.DepthPrepass)))
            {
                builder.AllowRendererListCulling(false);

                passData.rendererList = builder.UseRendererList(renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(cullingResults, camera, m_DepthOnlyPassName)));
                passData.viewport = new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);
                prepassOutput.depthBuffer = builder.UseDepthBuffer(prepassOutput.depthBuffer, DepthAccess.ReadWrite);
                prepassOutput.gbuffer = m_GBufferOutput;

                builder.SetRenderFunc(
                    (DepthPrePassData data, RenderGraphContext context) =>
                    {
                        context.cmd.SetViewport(data.viewport);
                        CoreUtils.DrawRendererList(context.renderContext, context.cmd, data.rendererList);
                    });
            }
        }

        void ResolvePrepassBuffer(RenderGraph renderGraph, Camera camera, ref PrepassOutput output)
        {
            if (false || !camera.allowMSAA) // TODO
            {
                output.resolvedDepthBuffer = output.depthBuffer;

                return;
            }

            // TODO: Do MSAA resolve
        }

        class GBufferPassData
        {
            public RendererListHandle rendererList;
            public Rect viewport;
        }

        void RenderGBuffer(RenderGraph renderGraph, CullingResults cull, Camera camera, ref PrepassOutput prepassOutput)
        {
            using (var builder = renderGraph.AddRenderPass<GBufferPassData>("GBuffer", out var passData, ProfilingSampler.Get(ProfileID.GBuffer)))
            {
                builder.AllowRendererListCulling(false);

                SetupGBufferTargets(camera, builder, ref prepassOutput);
                passData.rendererList = builder.UseRendererList(renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(cull, camera, m_GBufferPassName)));
                passData.viewport = new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);

                builder.SetRenderFunc(
                    (GBufferPassData data, RenderGraphContext context) =>
                    {
                        context.cmd.SetViewport(data.viewport);
                        CoreUtils.DrawRendererList(context.renderContext, context.cmd, data.rendererList);
                    });
            }
        }

        void SetupGBufferTargets(Camera camera, RenderGraphBuilder builder, ref PrepassOutput prepassOutput)
        {
            Vector2 resolution = new Vector2(camera.pixelWidth, camera.pixelHeight);

            int currentIndex = 0;

            prepassOutput.depthBuffer = builder.UseDepthBuffer(prepassOutput.depthBuffer, DepthAccess.ReadWrite);
            prepassOutput.normalBuffer = CreateNormalBuffer(m_RenderGraph, resolution, true, MSAASamples.None);

            FastMemoryDesc gbufferFastMemoryDesc;
            gbufferFastMemoryDesc.inFastMemory = true;
            gbufferFastMemoryDesc.residencyFraction = 1.0f;
            gbufferFastMemoryDesc.flags = FastMemoryFlags.SpillTop;

            m_GBufferOutput.mrt[currentIndex] = builder.UseColorBuffer(m_RenderGraph.CreateTexture(
                new TextureDesc(resolution, false, false)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                    enableRandomWrite = true,
                    bindTextureMS = false,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.black,
                    name = $"GBuffer{currentIndex}",
                    fastMemoryDesc = gbufferFastMemoryDesc
                }), currentIndex++);

            m_GBufferOutput.mrt[currentIndex] = builder.UseColorBuffer(prepassOutput.normalBuffer, currentIndex++);
            m_GBufferOutput.gBufferCount = currentIndex;
        }

        TextureHandle CreateDepthBuffer(RenderGraph renderGraph, Vector2 resolution, bool clear, MSAASamples msaaSamples)
        {
            bool msaa = msaaSamples != MSAASamples.None;

            FastMemoryDesc fastMemDesc;
            fastMemDesc.inFastMemory = true;
            fastMemDesc.residencyFraction = 1.0f;
            fastMemDesc.flags = FastMemoryFlags.SpillTop;

            TextureDesc depthDesc = new TextureDesc(resolution, false, false)
            {
                depthBufferBits = DepthBits.Depth32,
                bindTextureMS = msaa,
                msaaSamples = msaaSamples,
                clearBuffer = clear,
                name = msaa ? "CameraDepthStencilMSAA" : "CameraDepthStencil",
                fastMemoryDesc = fastMemDesc
            };

            return renderGraph.CreateTexture(depthDesc);
        }

        TextureHandle CreateNormalBuffer(RenderGraph renderGraph, Vector2 resolution, bool clear, MSAASamples msaaSamples)
        {
            bool msaa = msaaSamples != MSAASamples.None;

            FastMemoryDesc fastMemDesc;
            fastMemDesc.inFastMemory = true;
            fastMemDesc.residencyFraction = 1.0f;
            fastMemDesc.flags = FastMemoryFlags.SpillTop;

            TextureDesc normalDesc = new TextureDesc(resolution, false, false)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                clearBuffer = clear,
                clearColor = Color.black,
                bindTextureMS = msaa,
                msaaSamples = msaaSamples,
                enableRandomWrite = !msaa,
                name = msaa ? "NormalBufferMSAA" : "NormalBuffer",
                fastMemoryDesc = fastMemDesc,
                fallBackToBlackTexture = true
            };
            return renderGraph.CreateTexture(normalDesc);
        }
    }
}
