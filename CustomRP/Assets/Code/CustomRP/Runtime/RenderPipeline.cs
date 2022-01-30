using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Resove the ambiguity in the RendererList name (pick the in-engine version)
using RendererList = UnityEngine.Rendering.RendererUtils.RendererList;
using RendererListDesc = UnityEngine.Rendering.RendererUtils.RendererListDesc;
using System;

namespace CustomRP
{
    public partial class RenderPipeline : UnityEngine.Rendering.RenderPipeline
    {
        enum ProfileID
        {
            DepthPrepass,
            GBuffer,

            FinalPost,

            RecordRenderGraph,
        }

        RenderPipelineAsset m_RenderPipelineAsset;
        RenderGraph m_RenderGraph = new RenderGraph("CustomRP Render Graph");

        ShaderTagId m_GBufferPassName = new ShaderTagId("GBuffer");
        ShaderTagId m_DepthOnlyPassName = new ShaderTagId("DepthOnly");
        ShaderTagId m_DeferredLightingPassName = new ShaderTagId("Deferred Lighting");

        Material m_DeferredLightingMaterial;

        GBufferOutput m_GBufferOutput;

        int m_FrameCount = 0;

        public RenderPipeline(RenderPipelineAsset asset)
        {
            m_RenderPipelineAsset = asset;

            m_GBufferOutput = new GBufferOutput();
            m_GBufferOutput.mrt = new TextureHandle[RenderGraph.kMaxMRTCount];

            InitializePostProcess();
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

                try
                {
                    ExecuteWithRenderGraph(camera, context, cmd, cullingResults);
                }
                catch (Exception e)
                {
                    Debug.LogError("Error while building Render Graph");
                    Debug.LogException(e);
                }

                // Instruct the Graphics API to perform all scheduled commands
                context.ExecuteCommandBuffer(cmd);
                context.Submit();
                cmd.Clear();

                EndCameraRendering(context, camera);
            }

            m_RenderGraph.EndFrame();
            EndFrameRendering(context, cameras);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CleanupRenderGraph();

            CleanupPostProcess();
        }

        void CleanupRenderGraph()
        {
            m_RenderGraph.Cleanup();
            m_RenderGraph = null;
        }

        // TODO: Get more input instead of hard-coding params
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

    }
}
