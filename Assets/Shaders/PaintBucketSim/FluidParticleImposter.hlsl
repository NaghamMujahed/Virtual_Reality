#ifndef PAINTBUCKETSIM_FLUID_PARTICLE_IMPOSTER_INCLUDED
#define PAINTBUCKETSIM_FLUID_PARTICLE_IMPOSTER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

StructuredBuffer<float4> _ParticlePositionRadius;
StructuredBuffer<float4> _ParticleColor;
StructuredBuffer<float4> _ParticleStateAgeId;

// Camera basis + matrices in world space, supplied explicitly by the driver each
// frame. We do NOT use UNITY_MATRIX_V/VP here because those built-ins are not
// reliably set when the passes run via Graphics.ExecuteCommandBuffer outside
// URP's own camera loop (that made the surface appear camera-locked).
float3 _FluidCamRight;
float3 _FluidCamUp;
float4x4 _FluidView;
float4x4 _FluidViewProj;

float _FluidParticleScale;
float _UsePerParticleColor;
float4 _FallbackColor;
float _HideCanvasAndLostParticles;
float _UseBucketLocalParticles;
float _FluidUseParticleColor;
float4 _FluidDeepColor;
float4x4 _BucketLocalToWorld;
int _ParticleIndexStride;
int _ParticleCount;

struct Attributes
{
    float3 positionOS : POSITION;
};

struct Varyings
{
    float4 positionCS  : SV_POSITION;
    float2 uv          : TEXCOORD0;
    float  viewCenterZ : TEXCOORD1;   // view-space z of the sphere centre (negative in front)
    float  radius      : TEXCOORD2;
    float4 color       : TEXCOORD3;
    float  valid       : TEXCOORD4;   // 0 = hidden particle, discard in fragment
};

Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
{
    Varyings output = (Varyings)0;

    uint particleIndex = min(
        instanceID * (uint)max(_ParticleIndexStride, 1),
        (uint)max(_ParticleCount - 1, 0)
    );

    float4 pr = _ParticlePositionRadius[particleIndex];
    int state = (int)round(_ParticleStateAgeId[particleIndex].x);

    float3 centerWS = pr.xyz;
    bool bucketLocalState =
        state == 1 || state == 2 || state == 3 || state == 4;
    if (_UseBucketLocalParticles > 0.5 && bucketLocalState)
    {
        centerWS = mul(_BucketLocalToWorld, float4(centerWS, 1.0)).xyz;
    }

    float radius = max(pr.w * _FluidParticleScale, 0.00001);

    float2 uv = input.positionOS.xy;
    float3 offsetWS =
        _FluidCamRight * uv.x * radius +
        _FluidCamUp * uv.y * radius;
    float3 positionWS = centerWS + offsetWS;

    output.positionCS = mul(_FluidViewProj, float4(positionWS, 1.0));
    output.uv = uv;
    output.viewCenterZ = mul(_FluidView, float4(centerWS, 1.0)).z;
    output.radius = radius;

    float4 particleColor = _ParticleColor[particleIndex];
    float4 tinted = lerp(
        _FallbackColor,
        particleColor,
        saturate(_UsePerParticleColor)
    );
    output.color = _FluidUseParticleColor > 0.5 ? tinted : _FluidDeepColor;

    float valid = 1.0;
    if (_HideCanvasAndLostParticles > 0.5)
    {
        // Inactive(0), Deposited(8), Absorbed(9), Lost(10) are not liquid.
        if (state == 0 || state == 8 || state == 9 || state == 10)
            valid = 0.0;
    }
    output.valid = valid;

    return output;
}

#endif
