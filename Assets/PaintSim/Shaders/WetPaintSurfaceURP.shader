Shader "PaintSim/Wet Paint Surface URP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Paint Albedo", 2D) = "white" {}
        _PaintSurfaceData ("Coverage / Wetness / Thickness", 2D) = "black" {}
        [MainColor] _BaseColor ("Canvas Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Legacy Color", Color) = (1, 1, 1, 1)
        _CanvasSmoothness ("Canvas Smoothness", Range(0, 1)) = 0.22
        _DryPaintSmoothness ("Dry Paint Smoothness", Range(0, 1)) = 0.48
        _WetPaintSmoothness ("Wet Paint Smoothness", Range(0, 1)) = 0.94
        _PaintNormalStrength ("Paint Thickness Normal", Range(0, 8)) = 2.5
        _ParallaxStrength ("Wet Film Parallax", Range(0, 0.04)) = 0.008
        _EdgeRidgeStrength ("Contact Edge Ridge", Range(0, 12)) = 4.0
        _EdgeDarkening ("Contact Edge Darkening", Range(0, 0.35)) = 0.08
        _MicroNormalStrength ("Paint Micro Normal", Range(0, 0.25)) = 0.035
        _CanvasGrainStrength ("Canvas Grain Strength", Range(0, 0.25)) = 0.045
        _CanvasGrainScale ("Canvas Grain Scale", Range(8, 512)) = 180
        _PigmentSaturation ("Pigment Saturation", Range(0.6, 1.6)) = 1.10
        _WetDarkening ("Wet Pigment Darkening", Range(0, 0.25)) = 0.055
        _EdgeHighlightStrength ("Wet Edge Highlight", Range(0, 1.2)) = 0.28
        _WetSpecularStrength ("Wet Specular Strength", Range(0, 2)) = 0.85
        _ClearCoatStrength ("Wet Clear Coat", Range(0, 1.5)) = 0.75
        _EnvironmentReflection ("Environment Reflection", Range(0, 2)) = 0.55
        _FresnelStrength ("Wet Fresnel Strength", Range(0, 1)) = 0.18
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PaintSurfaceData);
            SAMPLER(sampler_PaintSurfaceData);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half _CanvasSmoothness;
                half _DryPaintSmoothness;
                half _WetPaintSmoothness;
                half _PaintNormalStrength;
                half _ParallaxStrength;
                half _EdgeRidgeStrength;
                half _EdgeDarkening;
                half _MicroNormalStrength;
                half _CanvasGrainStrength;
                half _CanvasGrainScale;
                half _PigmentSaturation;
                half _WetDarkening;
                half _EdgeHighlightStrength;
                half _WetSpecularStrength;
                half _ClearCoatStrength;
                half _EnvironmentReflection;
                half _FresnelStrength;
                half _Cull;
            CBUFFER_END

            float4 _PaintSurfaceData_TexelSize;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(
                    normalInputs.tangentWS,
                    input.tangentOS.w * GetOddNegativeScale()
                );
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float2 ResolveParallaxUv(
                Varyings input,
                half3 viewDirectionWS)
            {
                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS =
                    input.tangentWS.w * cross(normalWS, tangentWS);
                half3 viewDirectionTS = half3(
                    dot(viewDirectionWS, tangentWS),
                    dot(viewDirectionWS, bitangentWS),
                    dot(viewDirectionWS, normalWS)
                );

                half4 data = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv
                );
                float2 parallaxDirection =
                    viewDirectionTS.xy /
                    max(abs(viewDirectionTS.z), 0.25h);
                float offsetAmount =
                    data.b * data.r * _ParallaxStrength;

                return saturate(
                    input.uv - parallaxDirection * offsetAmount
                );
            }

            half3 ResolvePaintNormal(
                Varyings input,
                float2 uv,
                half coverage,
                out half edgeRidge)
            {
                float2 texel = max(
                    _PaintSurfaceData_TexelSize.xy,
                    float2(1e-6, 1e-6)
                );

                half4 leftData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    uv - float2(texel.x, 0)
                );
                half4 rightData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    uv + float2(texel.x, 0)
                );
                half4 downData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    uv - float2(0, texel.y)
                );
                half4 upData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    uv + float2(0, texel.y)
                );

                half2 gradient = half2(
                    rightData.b - leftData.b,
                    upData.b - downData.b
                );
                half2 coverageGradient = half2(
                    rightData.r - leftData.r,
                    upData.r - downData.r
                );
                edgeRidge = saturate(
                    length(coverageGradient) * _EdgeRidgeStrength
                );

                float2 microCell = floor(
                    uv / max(texel, float2(1e-6, 1e-6))
                );
                half2 microNormal = half2(
                    Hash21(microCell) - 0.5,
                    Hash21(microCell + 19.17) - 0.5
                ) * _MicroNormalStrength * coverage;

                half3 normalTS = normalize(
                    half3(
                        -gradient.x * _PaintNormalStrength * coverage +
                            microNormal.x,
                        -gradient.y * _PaintNormalStrength * coverage +
                            microNormal.y,
                        1.0h
                    )
                );

                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS =
                    input.tangentWS.w * cross(normalWS, tangentWS);
                return normalize(
                    tangentWS * normalTS.x +
                    bitangentWS * normalTS.y +
                    normalWS * normalTS.z
                );
            }

            half3 EvaluateLight(
                Light light,
                half3 normalWS,
                half3 viewDirectionWS,
                half3 albedo,
                half smoothness,
                half specularStrength,
                half clearCoatStrength)
            {
                half attenuation =
                    light.distanceAttenuation * light.shadowAttenuation;
                half ndotl = saturate(dot(normalWS, light.direction));
                half3 halfDirection =
                    SafeNormalize(light.direction + viewDirectionWS);
                half specularPower = exp2(4.0h + smoothness * 8.0h);
                half specular =
                    pow(saturate(dot(normalWS, halfDirection)), specularPower) *
                    specularStrength;
                half coatSpecular = pow(
                    saturate(dot(normalWS, halfDirection)),
                    exp2(10.0h + smoothness * 4.0h)
                ) * clearCoatStrength;

                return light.color * attenuation *
                    (
                        albedo * ndotl +
                        (specular + coatSpecular).xxx * ndotl
                    );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 viewDirectionWS =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float2 paintUv = ResolveParallaxUv(
                    input,
                    viewDirectionWS
                );
                half3 albedo = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    paintUv
                ).rgb;
                half4 surfaceData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    paintUv
                );

                half coverage = saturate(surfaceData.r);
                half wetness = saturate(surfaceData.g);
                float grainCoordinateScale = max((float)_CanvasGrainScale, 1.0);
                half coarseGrain =
                    Hash21(floor(paintUv * grainCoordinateScale)) - 0.5h;
                half fineGrain =
                    Hash21(floor(paintUv * grainCoordinateScale * 2.73 + 9.31)) -
                    0.5h;
                half grain =
                    (coarseGrain * 0.7h + fineGrain * 0.3h) *
                    _CanvasGrainStrength;
                albedo *= 1.0h + grain * (1.0h - coverage * wetness * 0.55h);

                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(
                    luminance.xxx,
                    albedo,
                    lerp(1.0h, _PigmentSaturation, coverage)
                );
                albedo *= 1.0h - coverage * wetness * _WetDarkening;

                half edgeRidge;
                half3 normalWS = ResolvePaintNormal(
                    input,
                    paintUv,
                    coverage,
                    edgeRidge
                );
                albedo *= 1.0h - edgeRidge * coverage * _EdgeDarkening;

                half smoothness = lerp(
                    _CanvasSmoothness,
                    _DryPaintSmoothness,
                    coverage
                );
                smoothness = lerp(
                    smoothness,
                    _WetPaintSmoothness,
                    coverage * wetness
                );
                smoothness = saturate(
                    smoothness + edgeRidge * wetness * 0.08h
                );

                half specularStrength = lerp(
                    0.04h,
                    _WetSpecularStrength,
                    coverage * wetness
                );
                half clearCoatStrength =
                    coverage * wetness * _ClearCoatStrength *
                    lerp(0.75h, 1.25h, edgeRidge);

                half3 ambientLighting = max(
                    SampleSH(normalWS),
                    half3(0.22h, 0.22h, 0.22h)
                );
                half3 color = ambientLighting * albedo;
                Light mainLight = GetMainLight();
                color += EvaluateLight(
                    mainLight,
                    normalWS,
                    viewDirectionWS,
                    albedo,
                    smoothness,
                    specularStrength,
                    clearCoatStrength
                );
                color +=
                    mainLight.color *
                    mainLight.distanceAttenuation *
                    edgeRidge *
                    coverage *
                    wetness *
                    _EdgeHighlightStrength;

                half3 reflectionDirection =
                    reflect(-viewDirectionWS, normalWS);
                half3 environmentReflection =
                    GlossyEnvironmentReflection(
                        reflectionDirection,
                        input.positionWS,
                        1.0h - smoothness,
                        1.0h
                    );
                color += environmentReflection *
                    coverage *
                    lerp(0.05h, _EnvironmentReflection, wetness);

                half fresnel =
                    pow(1.0h - saturate(dot(normalWS, viewDirectionWS)), 4.0h) *
                    coverage *
                    lerp(0.2h, 1.0h, wetness) *
                    _FresnelStrength;
                color += fresnel.xxx;
                color = MixFog(color, input.fogFactor);

                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
