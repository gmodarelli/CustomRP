Shader "CustomRP/PostProcessPass"
{
    HLSLINCLUDE
#pragma target 4.5
#pragma editor_async_compilation
#pragma only_renderers d3d11

#include "Library.hlsl"

        Texture2D _InputTexture;
		SamplerState sampler_LinearClamp;

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        float4 Frag(Varyings input) : SV_Target0
        {
            float4 outColor = _InputTexture.Sample(sampler_LinearClamp, input.texcoord);
            return float4(outColor.rgb, 1);
        }

    ENDHLSL

    SubShader
    {
        Pass
        {
            Tags { "LightMode" = "Post Process Pass" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
