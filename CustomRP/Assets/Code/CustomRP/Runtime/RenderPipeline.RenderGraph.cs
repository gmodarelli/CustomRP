using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace CustomRP
{
    public partial class RenderPipeline
    {
        void ExecuteWithRenderGraph(Camera camera, ScriptableRenderContext context, CommandBuffer cmd, CullingResults cullingResults)
        {
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
        }

        void RecordRenderGraph(CullingResults cullingResults, Camera camera, ScriptableRenderContext renderContext, CommandBuffer commandBuffer)
        {
            using (new ProfilingScope(commandBuffer, ProfilingSampler.Get(ProfileID.RecordRenderGraph)))
            {
                TextureHandle backBuffer = m_RenderGraph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);

                var prepassOutput = RenderPrepass(m_RenderGraph, cullingResults, camera);
                TextureHandle postProcessDest = RenderPostProcess(m_RenderGraph, prepassOutput, backBuffer, cullingResults, camera);
            }
        }

    }
}
