using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum FluidTopBoundaryMode
    {
        Open = 0,
        TemporaryLid = 1,
        MarkLost = 2
    }

    [CreateAssetMenu(
        fileName = "PbfSolverConfig",
        menuName = "Paint Bucket Sim/PBF Solver Config")]
    public class PbfSolverConfig : ScriptableObject
    {
        [Header("Solver")]
        public bool enablePbf = true;

        [Range(1, 12)]
        public int solverIterations = 4;

        [Tooltip("Smoothing radius h for density and neighbor search.")]
        [Min(0.005f)]
        public float smoothingRadiusMeters = 0.075f;

        [Tooltip("Small epsilon used in PBF lambda denominator.")]
        [Min(0.000001f)]
        public float lambdaEpsilon = 120.0f;

        [Header("Artificial Pressure / sCorr")]
        public bool enableArtificialPressure = true;

        [Min(0.0f)]
        public float artificialPressureK = 0.001f;

        [Range(1, 8)]
        public int artificialPressureN = 4;

        [Range(0.05f, 0.9f)]
        public float artificialPressureDeltaQRatio = 0.3f;

        [Header("Velocity")]
        [Range(0.0f, 10.0f)]
        public float velocityDampingPerSecond = 0.02f;

        [Min(0.1f)]
        public float maxParticleSpeed = 15.0f;

        [Header("Boundary Interaction")]
        public bool useBoundaryParticleCollision = true;

        [Range(0.0f, 2.0f)]
        public float boundaryCollisionStrength = 0.8f;

        [Tooltip("Boundary collision distance = particleRadius + boundaryParticleRadiusMultiplier * smoothingRadius.")]
        [Range(0.05f, 0.8f)]
        public float boundaryParticleRadiusMultiplier = 0.22f;

        public bool useAnalyticBucketProjection = true;

        [Range(0.0f, 1.0f)]
        public float analyticProjectionStrength = 1.0f;

        [Header("Hole / Near-Hole Classification")]
        [Tooltip("Particles near the bottom hole are marked NearHole, but not emitted yet. Emission belongs to O1.")]
        public bool classifyNearHoleParticles = true;

        [Min(0.001f)]
        public float nearHoleHeightMeters = 0.08f;

        [Min(0.0f)]
        public float nearHoleRadialPaddingMeters = 0.03f;

        [Header("Spatial Hash")]
        [Tooltip("Capacity multiplier for hash maps. Higher helps avoid capacity issues when particle count grows.")]
        [Range(1.0f, 4.0f)]
        public float hashCapacityMultiplier = 2.0f;

        [Header("Stability Guards")]
        [Tooltip("Maximum PBF position correction per particle per iteration.")]
        [Min(0.001f)]
        public float maxPositionCorrectionPerIteration = 0.025f;

        [Tooltip("Temporary gravity scale for early PBF stabilization.")]
        [Range(0.0f, 1.0f)]
        public float fluidGravityScale = 0.5f;

        [Header("Performance")]
        [Tooltip("If false, fluid hash is built once per substep and reused during solver iterations. Faster and usually acceptable.")]
        public bool rebuildFluidHashEveryIteration = false;

        [Tooltip("Update expensive fluid diagnostics every N simulation steps.")]
        [Min(1)]
        public int diagnosticsUpdateInterval = 8;

        [Header("Warmup / Rest Relaxation")]
        public bool enableWarmupOnInitialize = true;

        [Range(0, 80)]
        public int warmupSteps = 20;

        [Tooltip("During warmup, gravity is usually reduced to let particles settle without explosion.")]
        [Range(0.0f, 1.0f)]
        public float warmupGravityScale = 0.1f;

        [Header("XSPH Viscosity")]
        public bool enableXsphViscosity = true;

        [Tooltip("Velocity smoothing strength. Higher = more viscous/cohesive-looking motion.")]
        [Range(0.0f, 0.5f)]
        public float xsphStrength = 0.08f;

        [Tooltip("Clamp how much XSPH can change velocity per step.")]
        [Min(0.0f)]
        public float maxXsphVelocityChange = 1.5f;

        [Header("Containment / Stability")]
        public FluidTopBoundaryMode topBoundaryMode = FluidTopBoundaryMode.TemporaryLid;

        [Min(0.0f)]
        public float topBoundaryPaddingMeters = 0.02f;

        [Tooltip("If true, projection corrections also move previous positions to avoid converting correction into velocity.")]
        public bool preventProjectionEnergyInjection = true;

        private void OnValidate()
        {
            if (solverIterations < 1)
                solverIterations = 1;

            if (smoothingRadiusMeters < 0.005f)
                smoothingRadiusMeters = 0.005f;

            if (lambdaEpsilon < 0.000001f)
                lambdaEpsilon = 0.000001f;

            if (maxParticleSpeed < 0.1f)
                maxParticleSpeed = 0.1f;

            if (diagnosticsUpdateInterval < 1)
                diagnosticsUpdateInterval = 1;

            if (maxPositionCorrectionPerIteration < 0.001f)
                maxPositionCorrectionPerIteration = 0.001f;
        }
    }
}