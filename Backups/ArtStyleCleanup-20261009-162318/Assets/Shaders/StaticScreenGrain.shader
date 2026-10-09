Shader "Hidden/PozzleRoom/Static Screen Grain"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Static Screen Grain"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _StaticGrainIntensity;
            float _StaticGrainSize;
            float _StaticGrainDensity;
            float _StaticGrainShadowWeight;
            float _StaticGrainShadowThreshold;
            float _StaticGrainShadowSoftness;
            float4 _StaticGrainTint;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float2 grainCell = floor(input.uv * _ScreenParams.xy / max(_StaticGrainSize, 0.5));
                float presence = step(1.0 - _StaticGrainDensity, Hash21(grainCell + 17.0));
                float signedNoise = Hash21(grainCell) * 2.0 - 1.0;

                float luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                float darkness = 1.0 - smoothstep(
                    _StaticGrainShadowThreshold - _StaticGrainShadowSoftness,
                    _StaticGrainShadowThreshold + _StaticGrainShadowSoftness,
                    luminance);
                float amount = _StaticGrainIntensity * presence *
                    (1.0 + darkness * _StaticGrainShadowWeight);

                source.rgb = saturate(source.rgb + signedNoise * amount * _StaticGrainTint.rgb);
                return source;
            }
            ENDHLSL
        }
    }
}
