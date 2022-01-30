Shader "CustomRP/SimpleUnlitColor"
{
	Properties
	{
		_BaseColor("Base Color", Color) = (1, 1, 1, 1)
	}

    SubShader
    {
        Pass
        {
            Tags { "LightMode" = "GBuffer" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4x4 unity_MatrixVP;
			float4x4 unity_ObjectToWorld;

            float4 _BaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : NORMAL;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 positionWS = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionCS = mul(unity_MatrixVP, positionWS);
                OUT.normalWS = mul((float3x3)unity_ObjectToWorld, IN.normal);
                return OUT;
            }

            void frag(Varyings IN,
                out float4 outGBuffer0 : SV_Target0,
                out float4 outGBuffer1 : SV_Target1
			)
            {
                outGBuffer0 = _BaseColor;

                outGBuffer1 = float4(IN.normalWS * 0.5 + 0.5, 0.0);
            }
			ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "DepthOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4x4 unity_MatrixVP;
			float4x4 unity_ObjectToWorld;

            float4 _BaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 positionWS = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionCS = mul(unity_MatrixVP, positionWS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_TARGET
            {
                return 0;
            }
			ENDHLSL
        }
    }
}
