Shader "AR/EquirectangularDome"
{
    Properties
    {
        [MainTexture] _BaseMap("Equirectangular Map", 2D) = "gray" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _RotationY("Yaw Rotation (deg)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-10"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "EquirectangularDome"
            Tags { "LightMode" = "UniversalForward" }

            // Inside of sphere; draw early as a sky replacement.
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _RotationY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            float2 DirectionToEquirectUv(float3 dir)
            {
                dir = normalize(dir);
                float yaw = radians(_RotationY);
                float cosY = cos(yaw);
                float sinY = sin(yaw);
                float3 rotated = float3(
                    dir.x * cosY - dir.z * sinY,
                    dir.y,
                    dir.x * sinY + dir.z * cosY);

                // Unity Core.hlsl already defines PI / TWO_PI.
                float2 uv;
                uv.x = atan2(rotated.x, rotated.z) / TWO_PI + 0.5;
                uv.y = asin(clamp(rotated.y, -1.0, 1.0)) / PI + 0.5;
                return uv;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = DirectionToEquirectUv(input.directionOS);
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
