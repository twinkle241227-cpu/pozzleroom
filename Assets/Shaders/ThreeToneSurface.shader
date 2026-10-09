Shader "PozzleRoom/Three Tone Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _ToneLight("Light Tone", Color) = (1,0.97,0.9,1)
        _ToneMid("Middle Tone", Color) = (0.65,0.65,0.65,1)
        _ToneDark("Dark Tone", Color) = (0.25,0.28,0.34,1)
        _ToneLow("Dark / Middle Threshold", Range(0,1)) = 0.2
        _ToneHigh("Middle / Light Threshold", Range(0,1)) = 0.65
        _ToneSoftness("Band Edge Softness", Range(0.001,0.2)) = 0.035
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _SrcBlendAlpha("Source Alpha", Float) = 1
        [HideInInspector] _DstBlendAlpha("Destination Alpha", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _AlphaToMask("Alpha To Coverage", Float) = 0
        [HideInInspector] _ReceiveShadows("Receive Shadows", Float) = 1
        [HideInInspector] _Smoothness("Smoothness", Float) = 0.2
        [HideInInspector] _Metallic("Metallic", Float) = 0
        [HideInInspector] _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        [HideInInspector] _SpecGlossMap("Specular Map", 2D) = "white" {}
        [HideInInspector] _SpecColor("Specular Color", Color) = (0.2,0.2,0.2,1)
        [HideInInspector] _OcclusionMap("Occlusion", 2D) = "white" {}
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Float) = 1
        [HideInInspector] _EmissionMap("Emission", 2D) = "white" {}
        [HideInInspector] _EmissionColor("Emission Color", Color) = (0,0,0,1)
        [HideInInspector] _ClearCoatMask("Clear Coat", Float) = 0
        [HideInInspector] _ClearCoatSmoothness("Clear Coat Smoothness", Float) = 0
        [HideInInspector] _DetailMask("Detail Mask", 2D) = "white" {}
        [HideInInspector] _DetailAlbedoMap("Detail Albedo", 2D) = "linearGrey" {}
        [HideInInspector] _DetailNormalMap("Detail Normal", 2D) = "bump" {}
        [HideInInspector] _DetailAlbedoMapScale("Detail Albedo Scale", Float) = 1
        [HideInInspector] _DetailNormalMapScale("Detail Normal Scale", Float) = 1
        [HideInInspector] _ParallaxMap("Height", 2D) = "black" {}
        [HideInInspector] _Parallax("Height Scale", Float) = 0.005
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "ThreeToneForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Separate material uniforms: correctness across reused Lit auxiliary passes
            // takes priority over SRP batching. GPU instancing remains supported.
            half4 _ToneLight, _ToneMid, _ToneDark;
            float _ToneLow, _ToneHigh, _ToneSoftness;

            void AccumulateToneLight(Light light, half3 normalWS, uint layers,
                                     inout half3 direct, inout half3 visible)
            {
                #ifdef _LIGHT_LAYERS
                if (!IsMatchingLightLayer(light.layerMask, layers)) return;
                #endif
                half3 energy = light.color * light.distanceAttenuation
                             * saturate(dot(normalWS, light.direction));
                direct += energy;
                visible += energy * light.shadowAttenuation;
            }

            half4 ThreeToneFragment(InputData inputData, SurfaceData surfaceData)
            {
                half4 mask = CalculateShadowMask(inputData);
                AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surfaceData);
                Light mainLight = GetMainLight(inputData, mask, ao);
                MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
                half3 direct = 0, visible = 0;
                uint layers = GetMeshRenderingLayer();
                AccumulateToneLight(mainLight, inputData.normalWS, layers, direct, visible);
                #if defined(_ADDITIONAL_LIGHTS)
                #if USE_FORWARD_PLUS
                for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData, mask, ao);
                    AccumulateToneLight(light, inputData.normalWS, layers, direct, visible);
                }
                #endif
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light light = GetAdditionalLight(lightIndex, inputData, mask, ao);
                    AccumulateToneLight(light, inputData.normalWS, layers, direct, visible);
                LIGHT_LOOP_END
                #endif
                half3 ambient = max(0, inputData.bakedGI) * ao.indirectAmbientOcclusion;
                half luminance = dot(direct + ambient, half3(0.2126,0.7152,0.0722));
                half level = saturate(luminance);
                float low = min(_ToneLow, _ToneHigh);
                float high = max(low + 0.001, max(_ToneLow, _ToneHigh));
                float width = max(_ToneSoftness, fwidth(level));
                // Tone controls affect brightness only; preserve the authored albedo hue.
                half3 weights = half3(0.2126,0.7152,0.0722);
                half darkTone = dot(_ToneDark.rgb, weights);
                half band = lerp(darkTone, dot(_ToneMid.rgb, weights), smoothstep(low-width,low+width,level));
                band = lerp(band, dot(_ToneLight.rgb, weights), smoothstep(high-width,high+width,level));
                // Shadow visibility never enters the band thresholds. Its continuous
                // interpolation retains the penumbra from URP's shadow maps.
                half visibility = saturate(dot(visible + ambient, half3(0.2126,0.7152,0.0722))
                                          / max(luminance, 0.0001));
                half3 color = surfaceData.albedo * lerp(darkTone, band, visibility);
                return half4(color + surfaceData.emission, surfaceData.alpha);
            }
            // Keep URP's normal maps, lightmap coordinates, fog and vertex handling.
            #define UniversalFragmentPBR ThreeToneFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
