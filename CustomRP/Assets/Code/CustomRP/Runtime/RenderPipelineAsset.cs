using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    [CreateAssetMenu(menuName = "Rendering/CustomRP/RenderPipelineAsset")]
    public class RenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new RenderPipelineInstance(this);
        }
    }
}
