using Unity.Mathematics;

namespace PaintBucketSim.Runtime
{
    public enum FluidParticleState
    {
        Inactive = 0,

        // Solver-domain states
        InsideFluid = 1,
        NearBoundary = 2,
        NearHole = 3,

        // Transition / outflow states
        Jet = 4,
        Emitted = 5,

        // Air-domain states
        Airborne = 6,
        Spilled = 7,

        // Canvas-domain states
        Deposited = 8,
        Absorbed = 9,

        // Terminal state
        Lost = 10
    }

    public static class FluidParticleStateUtility
    {
        public static bool IsActive(FluidParticleState state)
        {
            return state != FluidParticleState.Inactive;
        }

        public static bool IsFluidSolverState(FluidParticleState state)
        {
            return state == FluidParticleState.InsideFluid ||
                   state == FluidParticleState.NearBoundary ||
                   state == FluidParticleState.NearHole;
        }

        public static bool IsAirState(FluidParticleState state)
        {
            return state == FluidParticleState.Jet ||
                   state == FluidParticleState.Emitted ||
                   state == FluidParticleState.Airborne ||
                   state == FluidParticleState.Spilled;
        }

        public static bool IsCanvasState(FluidParticleState state)
        {
            return state == FluidParticleState.Deposited ||
                   state == FluidParticleState.Absorbed;
        }

        public static bool IsTerminalState(FluidParticleState state)
        {
            return state == FluidParticleState.Lost ||
                   state == FluidParticleState.Inactive;
        }
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

    public struct FluidParticlePoolStats
    {
        public int activeCount;
        public int capacity;
        public int inactiveCount;

        public int insideFluidCount;
        public int nearBoundaryCount;
        public int nearHoleCount;

        public int jetCount;
        public int emittedCount;
        public int airborneCount;
        public int spilledCount;

        public int depositedCount;
        public int absorbedCount;
        public int lostCount;

        public float poolUsage01;
        public int nextParticleId;
    }
}