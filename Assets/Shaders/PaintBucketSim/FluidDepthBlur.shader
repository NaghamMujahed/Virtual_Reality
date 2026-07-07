// Screen-space fluid rendering — separable bilateral depth smoothing (G32).
//
// Edge-preserving blur of the nearest-surface eye-depth target. Spatial Gaussian
// weight is modulated by a depth-difference term so the blur does not bleed
// across silhouette edges (the bumpy sphere look becomes a smooth surface while
// object boundaries stay crisp). Background texels (no fluid) are ignored.
//
// Rendered with DrawProcedural(..., Triangles, 3): a single fullscreen triangle
// generated from SV_VertexID, so it does not depend on any blit mesh.
Shader "PaintBucketSim/Fluid Depth Blur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FluidDepthBilateral"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            Texture2D _BlurSource;
            SamplerState sampler_BlurSource;

            float2 _BlurTexelDir;        // per-tap uv step (direction * texel size)
            float  _BlurDepthFalloff;    // bilateral depth sensitivity
            int    _BlurRadiusTaps;      // taps on each side
            float  _FluidBackgroundDepth;// depth value meaning "no fluid"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.uv = uv;
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                return o;
            }

            float Frag(Varyings input) : SV_Target
            {
                float centerDepth =
                    _BlurSource.SampleLevel(sampler_BlurSource, input.uv, 0).r;

                // Leave background (no-fluid) texels untouched.
                if (centerDepth >= _FluidBackgroundDepth * 0.5)
                    return centerDepth;

                int radius = clamp(_BlurRadiusTaps, 0, 32);
                float sigma = max((float)radius * 0.5, 0.5);
                float spatialK = 1.0 / (2.0 * sigma * sigma);

                float sum = centerDepth;
                float wsum = 1.0;

                [loop]
                for (int i = -radius; i <= radius; i++)
                {
                    if (i == 0)
                        continue;

                    float2 uv = input.uv + _BlurTexelDir * (float)i;
                    float d = _BlurSource.SampleLevel(
                        sampler_BlurSource, uv, 0).r;

                    if (d >= _FluidBackgroundDepth * 0.5)
                        continue;

                    float wSpatial = exp(-(float)(i * i) * spatialK);
                    float dz = d - centerDepth;
                    float wDepth = exp(-dz * dz * _BlurDepthFalloff);
                    float w = wSpatial * wDepth;

                    sum += d * w;
                    wsum += w;
                }

                return sum / max(wsum, 1e-5);
            }
            ENDHLSL
        }
    }
}
