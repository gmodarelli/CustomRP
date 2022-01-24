using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Resove the ambiguity in the RendererList name (pick the in-engine version)
using RendererList = UnityEngine.Rendering.RendererUtils.RendererList;
using RendererListDesc = UnityEngine.Rendering.RendererUtils.RendererListDesc;

namespace CustomRP
{
    public class RenderPipelineInstance : UnityEngine.Rendering.RenderPipeline
    {
        enum ProfileID
        {
            DepthPrepass,
            GBuffer,

            RecordRenderGraph,
        }

        struct GBufferOutput
        {
            public TextureHandle[] mrt;
            public int gBufferCount;
        }

        struct DBufferOutput
        {
            public TextureHandle[] mrt;
            public int dBufferCount;
        }

        struct PrepassOutput
        {
            public TextureHandle depthBuffer;
            public TextureHandle normalBuffer;

            public GBufferOutput gbuffer;

            public TextureHandle resolvedDepthBuffer;

            public TextureHandle stencilBuffer;
        }

        RenderPipelineAsset m_RenderPipelineAsset;
        RenderGraph m_RenderGraph = new RenderGraph("CustomRP Render Graph");

        ShaderTagId m_GBufferPassName = new ShaderTagId("GBuffer");
        ShaderTagId m_DepthOnlyPassName = new ShaderTagId("DepthOnly");

        GBufferOutput m_GBufferOutput;

        int m_FrameCount = 0;

        public RenderPipelineInstance(RenderPipelineAsset asset)
        {
            m_RenderPipelineAsset = asset;

            m_GBufferOutput = new GBufferOutput();
            m_GBufferOutput.mrt = new TextureHandle[RenderGraph.kMaxMRTCount];
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            var cmd = CommandBufferPool.Get("");

#if UNITY_EDITOR
            int newCount = m_FrameCount;
            foreach (var c in cameras)
            {
                if (c.cameraType != CameraType.Preview)
                {
                    newCount++;
                    break;
                }
            }
#else
            int newCount = Time.frameCount;
#endif

            if (newCount != m_FrameCount)
            {
                m_FrameCount = newCount;
            }

            // Iterate over all cameras
            foreach (Camera camera in cameras)
            {
                // Get the culling parameters from the current Camera
                camera.TryGetCullingParameters(out var cullingParameters);

                // Use the culling parameters to perform a cull operation, and store the results
                var cullingResults = context.Cull(ref cullingParameters);

                // Update the value of built-in shader variables, based on the current Camera
                context.SetupCameraProperties(camera);

                TextureHandle backBuffer = m_RenderGraph.ImportBackbuffer(camera.targetTexture);

                using (m_RenderGraph.RecordAndExecute(new RenderGraphParameters
                {
                    executionName = camera.name,
                    currentFrameIndex = m_FrameCount,
                    rendererListCulling = false,
                    scriptableRenderContext = context,
                    commandBuffer = cmd
                }))
                {
                    RecordRenderGraph(cullingResults, camera, context, cmd);
                }

                // Schedule a command to draw the Skybox if required
                if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
                {
                    context.DrawSkybox(camera);
                }

                // Instruct the Graphics API to perform all scheduled commands
                context.ExecuteCommandBuffer(cmd);
                context.Submit();
                cmd.Clear();
            }

            m_RenderGraph.EndFrame();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CleanupRenderGraph();
        }

        void CleanupRenderGraph()
        {
            m_RenderGraph.Cleanup();
            m_RenderGraph = null;
        }

        void RecordRenderGraph(CullingResults cullingResults, Camera camera, ScriptableRenderContext renderContext, CommandBuffer commandBuffer)
        {
            using (new ProfilingScope(commandBuffer, ProfilingSampler.Get(ProfileID.RecordRenderGraph)))
            {
                var prepassOutput = RenderPrepass(m_RenderGraph, cullingResults, camera);
            }
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

        class GBufferPassData
        {
            public RendererListHandle rendererList;
            public DBufferOutput dBuffer;               // TODO: Use when doing depth-prepass
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

        RendererListDesc CreateOpaqueRendererListDesc(CullingResults cull, Camera camera, ShaderTagId passName)
        {
            var result = new RendererListDesc(passName, cull, camera)
            {
                rendererConfiguration = PerObjectData.None,
                renderQueueRange = new RenderQueueRange { lowerBound = (int)RenderQueue.Background, upperBound = (int)RenderQueue.GeometryLast },
                sortingCriteria = SortingCriteria.CommonOpaque,
                stateBlock = null,
                overrideMaterial = null,
                excludeObjectMotionVectors = true
            };
            return result;
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
    }
}
