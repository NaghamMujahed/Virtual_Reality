using Unity.Mathematics;

namespace PaintBucketSim.Runtime
{
    public enum FluidParticleState
    {
        InsideFluid = 0,
        NearBoundary = 1,
        NearHole = 2,
        Emitted = 3,
        Airborne = 4,
        Deposited = 5,
        Absorbed = 6,
        Lost = 7
    }

    public struct FluidDiagnostics
    {
        public int particleCount;
        public int capacity;

        public float particleRadius;
        public float particleSpacing;

        public float totalMass;
        public float insideMass;
        public float airborneMass;
        public float depositedMass;
        public float lostMass;

        public float fillFraction;
        public float estimatedFillVolume;

        public float3 centerOfMassWorld;
    }
}