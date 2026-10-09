Shader "PozzleRoom/Global Chunky Stylized"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [NoScaleOffset] _PatternAtlas("Hand-painted Pattern Atlas", 2D) = "black" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _WarmLitColor("Warm Lit Color", Color) = (1.00,0.38,0.16,1)
        _CoolShadowColor("Cool Shadow Color", Color) = (0.16,0.30,0.62,1)
        _HighlightColor("Highlight Dot Color", Color) = (1,0.96,0.86,1)
        _PatternScale("Pattern Scale", Range(0.1, 20)) = 3.2
        _PatternStrength("Pattern Strength", Range(0, 1)) = 0.28
        _DotStrength("Dot Strength", Range(0, 1)) = 0.38
        _HighlightStrength("Highlight Strength", Range(0, 1)) = 0.72
        _HighlightBoost("Highlight Brightness", Range(0, 3)) = 1.45
        _SpecularSize("Highlight Size", Range(0.05, 1)) = 0.42
        [HideInInspector] _MappingMode("Mapping Mode", Float) = 0
        _RandomOffset("Random Offset", Vector) = (0,0,0,0)
        _ObjectBoundsCenter("Object Bounds Center", Vector) = (0,0,0,0)
        _ObjectBoundsSize("Object Bounds Size", Vector) = (1,1,1,0)
        [HideInInspector] _ObjectWorldScale("Object World Scale", Vector) = (1,1,1,0)
        _Smoothness("Smoothness", Range(0,1)) = 0.18
        _Metallic("Metallic", Range(0,1)) = 0
        [HideInInspector] _Cutoff("Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 250

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PatternAtlas);
            SAMPLER(sampler_PatternAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _WarmLitColor;
                half4 _CoolShadowColor;
                half4 _HighlightColor;
                float _PatternScale;
                float _PatternStrength;
                float _DotStrength;
                float _HighlightStrength;
                float _HighlightBoost;
                float _SpecularSize;
                float _MappingMode;
                float4 _RandomOffset;
                float4 _ObjectBoundsCenter;
                float4 _ObjectBoundsSize;
                float4 _ObjectWorldScale;
                float _Smoothness;
                float _Metallic;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                half3 normalOS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float n = sin(dot(p, float2(41.0, 289.0)));
                return frac(float2(262144.0, 32768.0) * n);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 smoothF = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, smoothF.x), lerp(c, d, smoothF.x), smoothF.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.55;
                value += ValueNoise(p) * amplitude;
                p = mul(float2x2(0.80, -0.60, 0.60, 0.80), p * 2.03 + 11.7);
                amplitude *= 0.5;
                value += ValueNoise(p) * amplitude;
                p = mul(float2x2(0.60, -0.80, 0.80, 0.60), p * 2.11 + 7.3);
                amplitude *= 0.5;
                value += ValueNoise(p) * amplitude;
                return value;
            }

            float4 Pattern2D(float2 uv)
            {
                // Layered rotated noise removes the visible square-cell layout while
                // keeping chunky, torn-paint silhouettes.
                float broadNoise = Fbm(uv * 0.72 + 3.4);
                float detailNoise = Fbm(uv * 1.85 + 19.1);
                float field = broadNoise * 0.72 + detailNoise * 0.28;
                float threshold = 0.51;
                float chunk = smoothstep(threshold - 0.09, threshold + 0.09, field);

                // Jitter every halftone centre so highlights feel hand-stamped rather
                // than like a computer-perfect dot grid.
                float2 dotGrid = uv * 5.5;
                float2 dotCell = floor(dotGrid);
                float2 jitter = (Hash22(dotCell) - 0.5) * 0.46;
                float2 dotPosition = frac(dotGrid) - 0.5 - jitter;
                float radius = lerp(0.10, 0.19, Hash21(dotCell + 31.7));
                float dots = 1.0 - smoothstep(radius, radius + 0.055, length(dotPosition));
                dots *= step(0.40, Hash21(dotCell * 0.61 + 8.3));

                // Broken elongated stamps provide a second, directional brush layer.
                float2 strokeUv = mul(float2x2(0.906, -0.423, 0.423, 0.906), uv);
                float2 strokeGrid = strokeUv * float2(2.4, 8.5);
                float2 strokeCell = floor(strokeGrid);
                float2 strokeF = frac(strokeGrid) - 0.5;
                strokeF += (Hash22(strokeCell + 57.0) - 0.5) * 0.22;
                float stroke = (1.0 - smoothstep(0.16, 0.25, abs(strokeF.x))) *
                               (1.0 - smoothstep(0.28, 0.46, abs(strokeF.y)));
                stroke *= step(0.63, Hash21(strokeCell * 0.47 + 12.8));

                // Fine irregular grain is intentionally non-binary and sparse.
                float grainNoise = Fbm(uv * 4.7 + 41.3);
                float grain = smoothstep(0.66, 0.86, grainNoise);
                return float4(chunk, dots, stroke, grain);
            }

            float SampleAtlasTile(float2 uv, float tileIndex)
            {
                float2 tile = float2(fmod(tileIndex, 4.0), floor(tileIndex * 0.25));
                // A small inset prevents neighbouring atlas cells bleeding together.
                float2 tileUv = lerp(0.018, 0.982, frac(uv));
                float2 atlasUv = (tile + tileUv) * 0.25;
                return SAMPLE_TEXTURE2D(_PatternAtlas, sampler_PatternAtlas, atlasUv).r;
            }

            float4 AtlasPattern2D(float2 uv, float seed)
            {
                float rotationSelector = floor(frac(seed * 0.071) * 4.0);
                float2 atlasUv = uv;
                if (rotationSelector > 0.5) atlasUv = float2(-atlasUv.y, atlasUv.x);
                if (rotationSelector > 1.5) atlasUv = -atlasUv;
                if (rotationSelector > 2.5) atlasUv = float2(atlasUv.y, -atlasUv.x);

                // Unity's atlas UV starts at the bottom, therefore tile 15 is the
                // visually top-right black/white halftone tile. Every decorative
                // layer now uses this clean motif; only scale, offset and threshold
                // vary, avoiding the dirty scratch/blotch collage appearance.
                const float halftoneTile = 15.0;
                float broadDots = SampleAtlasTile(atlasUv * 0.48 + seed, halftoneTile);
                float highlightDots = SampleAtlasTile(atlasUv * 1.30 + seed * 1.71, halftoneTile);
                float mediumDots = SampleAtlasTile(atlasUv * 0.82 + seed * 0.63, halftoneTile);
                float fineDots = SampleAtlasTile(atlasUv * 2.15 + seed * 2.13, halftoneTile);
                return saturate(float4(
                    smoothstep(0.42, 0.72, broadDots),
                    smoothstep(0.56, 0.80, highlightDots),
                    smoothstep(0.48, 0.76, mediumDots),
                    smoothstep(0.64, 0.88, fineDots)));
            }

            float4 TriplanarPattern(float3 positionOS, float3 normalOS)
            {
                float useLargeObjectMapping = step(0.5, _MappingMode);
                float3 weights = pow(abs(normalize(normalOS)), 2.0);
                weights /= max(weights.x + weights.y + weights.z, 0.0001);
                float uniformSize = max(max(abs(_ObjectBoundsSize.x), abs(_ObjectBoundsSize.y)), abs(_ObjectBoundsSize.z));
                uniformSize = max(uniformSize, 0.0001);
                float3 localNormalized = (positionOS - _ObjectBoundsCenter.xyz) / uniformSize;
                // Props use object-normalised coordinates. Architecture uses scaled
                // object coordinates: world-like density that still follows movement
                // and rotation instead of behaving like a camera/world-space filter.
                float3 propP = localNormalized * _PatternScale;
                float3 architectureP = (positionOS - _ObjectBoundsCenter.xyz) *
                    max(abs(_ObjectWorldScale.xyz), 0.0001) * 0.72;
                float3 p = lerp(propP, architectureP, useLargeObjectMapping) + _RandomOffset.xyz;
                float4 procedural = Pattern2D(p.zy) * weights.x +
                                    Pattern2D(p.xz) * weights.y +
                                    Pattern2D(p.xy) * weights.z;
                float seed = dot(_RandomOffset.xyz, float3(0.73, 1.13, 1.91));
                float4 painted = AtlasPattern2D(p.zy, seed + 1.0) * weights.x +
                                 AtlasPattern2D(p.xz, seed + 5.0) * weights.y +
                                 AtlasPattern2D(p.xy, seed + 9.0) * weights.z;
                return saturate(lerp(procedural, max(procedural * 0.42, painted), 0.72));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float4 pattern = TriplanarPattern(input.positionOS, input.normalOS);

                float uniformSize = max(max(abs(_ObjectBoundsSize.x), abs(_ObjectBoundsSize.y)), abs(_ObjectBoundsSize.z));
                uniformSize = max(uniformSize, 0.0001);
                float3 localNormalized = (input.positionOS - _ObjectBoundsCenter.xyz) / uniformSize;
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half directLight = saturate(ndotl * mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                half3 directLighting = mainLight.color * directLight;
                half lightLevel = saturate(directLight * dot(mainLight.color, half3(0.2126h, 0.7152h, 0.0722h)));
                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 mainHalfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half mainNdotH = saturate(dot(normalWS, mainHalfDirection));
                half specularPower = lerp(72.0h, 8.0h, (half)_SpecularSize);
                half specularLevel = pow(mainNdotH, specularPower) * directLight;
                half broadSpecular = pow(mainNdotH, max(3.0h, specularPower * 0.32h)) * directLight;

                // URP treats Spot Lights and Point Lights as additional lights.
                // Accumulate them explicitly so their cone/range/colour and shadows
                // drive both the surface lighting and the warm/cool pattern masks.
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightCount; ++lightIndex)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
                    half additionalNdotL = saturate(dot(normalWS, additionalLight.direction));
                    half additionalAttenuation = additionalLight.distanceAttenuation * additionalLight.shadowAttenuation;
                    half additionalLevel = saturate(additionalNdotL * additionalAttenuation);
                    directLighting += additionalLight.color * additionalLevel;
                    half perceivedBrightness = dot(additionalLight.color, half3(0.2126h, 0.7152h, 0.0722h));
                    lightLevel = saturate(lightLevel + additionalLevel * perceivedBrightness);
                    half3 additionalHalfDirection = SafeNormalize(additionalLight.direction + viewDirection);
                    half additionalNdotH = saturate(dot(normalWS, additionalHalfDirection));
                    specularLevel = saturate(specularLevel +
                        pow(additionalNdotH, specularPower) * additionalLevel);
                    broadSpecular = saturate(broadSpecular +
                        pow(additionalNdotH, max(3.0h, specularPower * 0.32h)) * additionalLevel);
                }

                // Surface style remains continuous. It never quantizes or reshapes
                // shadow attenuation, so URP soft-shadow penumbrae stay physical.
                half warmAmount = smoothstep(0.18h, 0.82h, lightLevel);
                half3 surfaceTint = lerp(_CoolShadowColor.rgb, _WarmLitColor.rgb, warmAmount);
                half surfacePattern = pattern.x * (half)_PatternStrength;
                half3 stylized = lerp(baseSample.rgb, baseSample.rgb * surfaceTint * 1.18h,
                                      saturate(surfacePattern));

                half highlightAmount = smoothstep(0.58h, 1.0h, lightLevel) *
                                       smoothstep(0.025h, 0.42h, broadSpecular);
                half highlightDots = pattern.y * highlightAmount * _DotStrength * _HighlightStrength;
                stylized = lerp(stylized, _HighlightColor.rgb * _HighlightBoost, saturate(highlightDots));

                // A broad painted sheen plus a smaller white core makes rounded
                // bottles and glasses read as curved, glossy forms.
                half paintedSheen = saturate(broadSpecular * warmAmount * _HighlightBoost);
                stylized += _HighlightColor.rgb * paintedSheen * 0.34h;
                stylized += _HighlightColor.rgb * specularLevel * _HighlightBoost * 0.62h;

                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + directLighting;
                half3 finalColor = stylized * max(lighting, 0.03h);
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, baseSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}
