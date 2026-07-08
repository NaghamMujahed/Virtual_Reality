// Screen-space fluid rendering — particle imposter passes (G32).
//
// Both passes render each fluid particle as a camera-facing sphere imposter,
// reading the same GPU particle buffers the indirect particle renderer uses.
//
//   Pass 0 "FluidDepth"     : outputs nearest-surface linear eye depth using a
//                             Min blend (no hardware depth buffer / SV_Depth, so
//                             it is immune to reversed-Z target differences).
//   Pass 1 "FluidThickness" : additively accumulates view-ray thickness and a
//                             thickness-weighted particle colour.
//
// Camera basis and matrices are supplied by the driver via globals /
// CommandBuffer.SetViewProjectionMatrices so the passes work when executed
// outside URP's own camera loop.
Shader "PaintBucketSim/Fluid Particle Imposter"
{
    Properties
    {
        _FluidParticleScale ("Fluid Particle Scale", Float) = 1.6
        _FluidThicknessPerParticle ("Thickness Per Particle", Float) = 0.55
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        // -------------------------------------------------------------------
        // Pass 0 — nearest-surface eye depth (Min blend).
        // -------------------------------------------------------------------
        Pass
        {
            Name "FluidDepth"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One
            BlendOp Min

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDepth

            #include "FluidParticleImposter.hlsl"

            float FragDepth(Varyings input) : SV_Target
            {
                float mag2 = dot(input.uv, input.uv);
                clip(1.0 - mag2);
                clip(input.valid - 0.5);

                // Push the billboard point onto the sphere surface toward the
                // camera (+Z in view space) and report positive eye depth.
                float zCam = sqrt(saturate(1.0 - mag2)) * input.radius;
                float eyeDepth = -(input.viewCenterZ + zCam);
                return eyeDepth;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // Pass 1 — additive thickness + thickness-weighted colour.
        // -------------------------------------------------------------------
        Pass
        {
            Name "FluidThickness"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragThickness

            #include "FluidParticleImposter.hlsl"

            float _FluidThicknessPerParticle;

            half4 FragThickness(Varyings input) : SV_Target
            {
                float mag2 = dot(input.uv, input.uv);
                clip(1.0 - mag2);
                clip(input.valid - 0.5);

                // Chord length through the sphere along the view ray.
                float chord =
                    2.0 * sqrt(saturate(1.0 - mag2)) *
                    input.radius * max(_FluidThicknessPerParticle, 0.0);

                return half4(input.color.rgb * chord, chord);
            }
            ENDHLSL
        }
    }
}
