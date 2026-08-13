Shader "AR/EquirectangularDome"
{
    Properties
    {
        [MainTexture] _BaseMap("Equirectangular Map", 2D) = "gray" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.5
        _RotationY("Yaw Rotation (deg)", Float) = 0
        // 0 = Mono, 1 = Side-by-Side (left eye), 2 = Top-Bottom (top / left eye)
        _StereoMode("Stereo Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Background+10"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "EquirectangularDome"
            Tags { "LightMode" = "UniversalForward" }

            // Inside of large sphere — draw early as sky background; content draws after.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

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
                float _Opacity;
                float _RotationY;
                float _StereoMode;
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

            float2 ApplyStereoLayout(float2 uv)
            {
                // Phone AR has a single view — sample the left-eye half only.
                if (_StereoMode > 1.5)
                {
                    // Top-Bottom: top half = left eye
                    uv.y = uv.y * 0.5 + 0.5;
                }
                else if (_StereoMode > 0.5)
                {
                    // Side-by-Side: left half = left eye
                    uv.x = uv.x * 0.5;
                }

                return uv;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = ApplyStereoLayout(DirectionToEquirectUv(input.directionOS));
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half alpha = saturate(color.a * _Opacity);
                return half4(color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
