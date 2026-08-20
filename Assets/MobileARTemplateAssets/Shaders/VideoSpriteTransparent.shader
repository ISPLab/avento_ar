Shader "AR/VideoSpriteTransparent"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 0.5)) = 0.02
        // 0 = embedded alpha in _BaseMap.a (HEVC-with-alpha / PNG)
        // 1 = side-by-side: left RGB, right grayscale alpha (H.264 — reliable on iOS)
        _AlphaLayout("Alpha Layout", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VideoSprite"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

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
                half _Opacity;
                half _AlphaCutoff;
                half _AlphaLayout;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color;
                if (_AlphaLayout > 0.5h)
                {
                    // Side-by-side: left = color, right = alpha matte.
                    float2 uvColor = float2(input.uv.x * 0.5, input.uv.y);
                    float2 uvAlpha = float2(0.5 + input.uv.x * 0.5, input.uv.y);
                    color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvColor);
                    color.a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvAlpha).r;
                }
                else
                {
                    color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                }

                color *= _BaseColor;
                color.a *= saturate(_Opacity);

                if (color.a <= _AlphaCutoff)
                    discard;

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
