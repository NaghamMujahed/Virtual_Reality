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
        _WetSpecularStrength ("Wet Specular Strength", Range(0, 2)) = 0.85
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
                half _WetSpecularStrength;
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

            half3 ResolvePaintNormal(Varyings input, half coverage)
            {
                float2 texel = max(
                    _PaintSurfaceData_TexelSize.xy,
                    float2(1e-6, 1e-6)
                );

                half leftHeight = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv - float2(texel.x, 0)
                ).b;
                half rightHeight = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv + float2(texel.x, 0)
                ).b;
                half downHeight = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv - float2(0, texel.y)
                ).b;
                half upHeight = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv + float2(0, texel.y)
                ).b;

                half2 gradient = half2(
                    rightHeight - leftHeight,
                    upHeight - downHeight
                );
                half3 normalTS = normalize(
                    half3(
                        -gradient.x * _PaintNormalStrength * coverage,
                        -gradient.y * _PaintNormalStrength * coverage,
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
                half specularStrength)
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

                return light.color * attenuation *
                    (albedo * ndotl + specular.xxx * ndotl);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv
                ).rgb;
                half4 surfaceData = SAMPLE_TEXTURE2D(
                    _PaintSurfaceData,
                    sampler_PaintSurfaceData,
                    input.uv
                );

                half coverage = saturate(surfaceData.r);
                half wetness = saturate(surfaceData.g);
                half3 normalWS = ResolvePaintNormal(input, coverage);
                half3 viewDirectionWS =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

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

                half specularStrength = lerp(
                    0.04h,
                    _WetSpecularStrength,
                    coverage * wetness
                );

                half3 color = SampleSH(normalWS) * albedo;
                Light mainLight = GetMainLight();
                color += EvaluateLight(
                    mainLight,
                    normalWS,
                    viewDirectionWS,
                    albedo,
                    smoothness,
                    specularStrength
                );

                half fresnel =
                    pow(1.0h - saturate(dot(normalWS, viewDirectionWS)), 4.0h) *
                    coverage * wetness * _FresnelStrength;
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
