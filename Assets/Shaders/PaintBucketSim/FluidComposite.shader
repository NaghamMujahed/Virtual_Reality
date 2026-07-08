// Screen-space fluid rendering — surface composite (G32).
//
// Drawn as a transparent fullscreen quad during URP's transparent pass, so it
// composites over the opaque scene without a ScriptableRendererFeature. It reads
// the smoothed fluid eye-depth and thickness targets (set as globals by the
// driver) plus URP's scene colour/depth, reconstructs the surface normal from
// depth, and shades the liquid with Fresnel reflection, screen-space refraction,
// Beer-Lambert absorption, and a specular highlight.
//
// Requires URP "Depth Texture" and "Opaque Texture" to be enabled on the active
// URP asset (for _CameraDepthTexture / _CameraOpaqueTexture).
Shader "PaintBucketSim/Fluid Composite"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "FluidComposite"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_FluidDepthTex);
            SAMPLER(sampler_FluidDepthTex);
            TEXTURE2D(_FluidThicknessTex);
            SAMPLER(sampler_FluidThicknessTex);

            float4 _FluidTexelSize;        // (1/w, 1/h, w, h) of the fluid targets
            float  _FluidBackgroundDepth;
            float  _FluidProj00;
            float  _FluidProj11;
            float  _FluidNormalYSign;
            float4x4 _FluidCameraToWorld;

            float  _FluidRefractionStrength;
            float  _FluidAbsorption;
            float  _FluidOpacity;
            float  _FluidFlipY;
            float  _FluidFresnelF0;
            float  _FluidReflectionStrength;
            float  _FluidSpecularStrength;
            float  _FluidSpecularPower;
            float  _FluidUseParticleColor;
            float4 _FluidDeepColor;
            float4 _FluidLightDir;         // xyz world-space light direction

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            // Fullscreen quad: the mesh already stores clip-space [-1,1] corners.
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                return o;
            }

            float SampleFluidDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(
                    _FluidDepthTex, sampler_FluidDepthTex, uv, 0).r;
            }

            // Reconstruct view-space position from screen uv + linear eye depth.
            float3 ViewPosition(float2 uv, float eyeDepth)
            {
                float2 ndc = uv * 2.0 - 1.0;
                ndc.y *= _FluidNormalYSign;
                return float3(
                    ndc.x / _FluidProj00,
                    ndc.y / _FluidProj11,
                    -1.0) * eyeDepth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // screenUV addresses the scene textures; fluidUV addresses our
                // own off-screen targets, which may be stored vertically flipped
                // relative to the scene depending on the graphics API.
                float2 screenUV = input.positionCS.xy / _ScreenParams.xy;
                float2 fluidUV = _FluidFlipY > 0.5
                    ? float2(screenUV.x, 1.0 - screenUV.y)
                    : screenUV;

                float eyeC = SampleFluidDepth(fluidUV);

                // No fluid here -> let the scene show through.
                if (eyeC >= _FluidBackgroundDepth * 0.5)
                {
                    clip(-1.0);
                    return half4(0, 0, 0, 0);
                }

                // Occlusion against opaque scene geometry in front of the fluid.
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                if (sceneEye < eyeC - 0.002)
                {
                    clip(-1.0);
                    return half4(0, 0, 0, 0);
                }

                float2 texel = _FluidTexelSize.xy;
                float eyeL = SampleFluidDepth(fluidUV - float2(texel.x, 0));
                float eyeR = SampleFluidDepth(fluidUV + float2(texel.x, 0));
                float eyeD = SampleFluidDepth(fluidUV - float2(0, texel.y));
                float eyeU = SampleFluidDepth(fluidUV + float2(0, texel.y));

                float3 posC = ViewPosition(fluidUV, eyeC);

                // Pick the closer neighbour on each axis to avoid smearing the
                // normal across silhouette edges.
                float3 ddxPos =
                    (abs(eyeR - eyeC) < abs(eyeC - eyeL))
                        ? (ViewPosition(fluidUV + float2(texel.x, 0), eyeR) - posC)
                        : (posC - ViewPosition(fluidUV - float2(texel.x, 0), eyeL));
                float3 ddyPos =
                    (abs(eyeU - eyeC) < abs(eyeC - eyeD))
                        ? (ViewPosition(fluidUV + float2(0, texel.y), eyeU) - posC)
                        : (posC - ViewPosition(fluidUV - float2(0, texel.y), eyeD));

                float3 viewN = normalize(cross(ddxPos, ddyPos));
                if (viewN.z < 0.0)
                    viewN = -viewN; // face the camera

                float3 worldN = normalize(mul((float3x3)_FluidCameraToWorld, viewN));
                float3 worldPos = mul(_FluidCameraToWorld, float4(posC, 1.0)).xyz;
                float3 viewDirW = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // Thickness + pigment tint.
                float4 tk = SAMPLE_TEXTURE2D_LOD(
                    _FluidThicknessTex, sampler_FluidThicknessTex, fluidUV, 0);
                float thickness = max(tk.a, 0.0);
                float3 tint = _FluidUseParticleColor > 0.5
                    ? tk.rgb / max(tk.a, 1e-4)
                    : _FluidDeepColor.rgb;

                // Screen-space refraction: bend the scene behind by the normal.
                float2 refractUV = screenUV + worldN.xy * _FluidRefractionStrength;
                float3 refracted = SampleSceneColor(refractUV);

                // Opacity: thickness-driven coverage lifted toward fully opaque
                // paint by the user opacity control. 1 -> solid selected colour.
                float coverage = 1.0 - exp(-thickness * _FluidAbsorption);
                coverage = saturate(lerp(coverage, 1.0, _FluidOpacity));
                float3 body = lerp(refracted, tint, coverage);

                // Fresnel.
                float fresnel = _FluidFresnelF0 +
                    (1.0 - _FluidFresnelF0) *
                    pow(1.0 - saturate(dot(worldN, viewDirW)), 5.0);

                // Environment reflection from the scene's ambient sky gradient
                // (always available, no reflection-probe dependency).
                float3 reflDir = reflect(-viewDirW, worldN);
                float skyT = saturate(reflDir.y * 0.5 + 0.5);
                float3 sky = lerp(
                    unity_AmbientGround.rgb,
                    unity_AmbientSky.rgb,
                    skyT);
                float3 reflection = sky * _FluidReflectionStrength;

                float3 color = lerp(body, reflection, saturate(fresnel));

                // Specular highlight.
                float3 lightDir = normalize(_FluidLightDir.xyz);
                float3 halfDir = normalize(lightDir + viewDirW);
                float spec = pow(
                    saturate(dot(worldN, halfDir)),
                    max(_FluidSpecularPower, 1.0)) * _FluidSpecularStrength;
                color += spec;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
