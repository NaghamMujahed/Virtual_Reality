Shader "PaintBucketSim/MPM Canvas Hybrid Particles URP"
{
    Properties
    {
        _VisualScale ("Visual Scale", Float) = 1.0
        _MinWorldRadius ("Minimum World Radius", Float) = 0.001
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 0.8
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.4
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "ForwardHybridParticles"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct MpmCanvasHybridParticle
            {
                float4 data0;
                float4 data1;
                float4 data2;
                float4 data3;
            };

            StructuredBuffer<MpmCanvasHybridParticle> _HybridParticles;

            float3 _CanvasPosition;
            float3 _CanvasTangent;
            float3 _CanvasBitangent;
            float3 _CanvasNormal;
            float _CanvasWidth;
            float _CanvasHeight;
            int _GridWidth;
            int _GridHeight;

            float3 _CameraRightWS;
            float3 _CameraUpWS;
            float3 _CameraForwardWS;
            float _VisualScale;
            float _MinWorldRadius;
            float _GlobalAlpha;
            float _SpecularStrength;
            float _FresnelStrength;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float active : TEXCOORD3;
            };

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                MpmCanvasHybridParticle particle = _HybridParticles[instanceID];
                float active = step(0.5, particle.data3.w);

                float2 uv01 = saturate(particle.data0.xy);
                float normalOffset = particle.data0.z;
                float radiusCells = max(particle.data0.w, 0.0);
                float cellSize = min(
                    _CanvasWidth / max((float)_GridWidth, 1.0),
                    _CanvasHeight / max((float)_GridHeight, 1.0)
                );
                float radius = max(radiusCells * cellSize * max(_VisualScale, 0.0), _MinWorldRadius);

                float3 centerWS =
                    _CanvasPosition +
                    normalize(_CanvasTangent) * ((uv01.x - 0.5) * _CanvasWidth) +
                    normalize(_CanvasBitangent) * ((uv01.y - 0.5) * _CanvasHeight) +
                    normalize(_CanvasNormal) * normalOffset;

                float3 billboard =
                    normalize(_CameraRightWS) * input.positionOS.x * radius +
                    normalize(_CameraUpWS) * input.positionOS.y * radius;
                float3 positionWS = centerWS + billboard;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.positionOS.xy;
                output.color = float4(saturate(particle.data2.rgb), saturate(particle.data2.a) * _GlobalAlpha);
                output.positionWS = positionWS;
                output.active = active;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.active - 0.5);

                float r2 = dot(input.uv, input.uv);
                clip(1.0 - r2);

                float edge = smoothstep(1.0, 0.35, r2);
                float dome = sqrt(saturate(1.0 - r2));
                float3 normalWS = normalize(
                    normalize(_CameraRightWS) * input.uv.x +
                    normalize(_CameraUpWS) * input.uv.y +
                    normalize(_CameraForwardWS) * dome
                );

                float3 lightDir = normalize(float3(0.35, 0.85, 0.25));
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 halfDir = normalize(lightDir + viewDir);
                float ndotl = saturate(dot(normalWS, lightDir));
                float specular = pow(saturate(dot(normalWS, halfDir)), 96.0) * saturate(_SpecularStrength);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0) * saturate(_FresnelStrength);

                float3 color =
                    input.color.rgb * (0.42 + 0.58 * ndotl) +
                    float3(specular, specular, specular) +
                    fresnel * input.color.rgb;
                float alpha = input.color.a * edge;

                return half4(saturate(color), alpha);
            }

            ENDHLSL
        }
    }
}
