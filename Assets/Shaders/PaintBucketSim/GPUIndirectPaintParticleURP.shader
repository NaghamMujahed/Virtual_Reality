Shader "PaintBucketSim/GPU Indirect Paint Particle URP"
{
    Properties
    {
        _FallbackColor ("Fallback Color", Color) = (0.1, 0.35, 1.0, 1.0)
        _VisualRadiusScale ("Visual Radius Scale", Float) = 1.0
        _UsePerParticleColor ("Use Per Particle Color", Float) = 1.0
        _RenderMode ("Render Mode", Float) = 0.0
        _SplatNormalStrength ("Splat Normal Strength", Float) = 0.85
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLitSimple"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 color      : TEXCOORD1;
                float2 splatUv    : TEXCOORD2;
            };

            StructuredBuffer<float4> _ParticlePositionRadius;
            StructuredBuffer<float4> _ParticleColor;

            float4 _FallbackColor;
            float _VisualRadiusScale;
            float _UsePerParticleColor;
            float _Smoothness;
            float _RenderMode;
            float _SplatNormalStrength;
            float3 _CameraRightWS;
            float3 _CameraUpWS;
            float3 _CameraForwardWS;
            int _ParticleIndexStride;
            int _ParticleCount;

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                uint particleIndex = min(
                    instanceID * (uint)max(_ParticleIndexStride, 1),
                    (uint)max(_ParticleCount - 1, 0)
                );

                float4 pr = _ParticlePositionRadius[particleIndex];

                float3 centerWS = pr.xyz;
                float radius = max(pr.w * _VisualRadiusScale, 0.0001);

                float3 local;
                float3 normalWS;

                if (_RenderMode > 0.5)
                {
                    float2 uv = input.positionOS.xy;
                    local =
                        normalize(_CameraRightWS) * uv.x * radius * 2.0 +
                        normalize(_CameraUpWS) * uv.y * radius * 2.0;
                    normalWS = normalize(_CameraForwardWS);
                    output.splatUv = uv;
                }
                else
                {
                    // Our octahedron mesh has unit radius.
                    // Multiply by 2 to match the old debug renderer's visual scale behavior.
                    local = input.positionOS * radius * 2.0;
                    normalWS = normalize(input.normalOS);
                    output.splatUv = float2(0, 0);
                }

                float3 positionWS = centerWS + local;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;

                float4 particleColor = _ParticleColor[particleIndex];
                output.color = lerp(_FallbackColor, particleColor, saturate(_UsePerParticleColor));

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);

                if (_RenderMode > 0.5)
                {
                    float r2 = dot(input.splatUv, input.splatUv);
                    clip(1.0 - r2);

                    float dome = sqrt(saturate(1.0 - r2)) *
                        max(_SplatNormalStrength, 0.05);

                    n = normalize(
                        normalize(_CameraRightWS) * input.splatUv.x +
                        normalize(_CameraUpWS) * input.splatUv.y +
                        normalize(_CameraForwardWS) * dome
                    );
                }

                // Simple fake lighting to keep shader independent and easy to debug.
                float3 lightDir = normalize(float3(0.35, 0.85, 0.25));
                float ndotl = saturate(dot(n, lightDir));

                float ambient = 0.35;
                float diffuse = 0.65 * ndotl;

                float3 color = input.color.rgb * (ambient + diffuse);

                return half4(color, input.color.a);
            }

            ENDHLSL
        }
    }
}
