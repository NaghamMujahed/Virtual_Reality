namespace PaintBucketSim.Runtime
{
    public enum FluidSolverType
    {
        CpuPbf = 0,
        GpuSparseMpmPrototype = 1
    }

    public enum FluidSolverStatus
    {
        NotInitialized = 0,
        Running = 1,
        Disabled = 2,
        Fallback = 3,
        NotImplemented = 4,
        Error = 5
    }

    public struct FluidSolverStepInput
    {
        public float dt;
        public float gravityScale;
        public bool isWarmup;
    }

    public struct FluidSolverStats
    {
        public FluidSolverType solverType;
        public FluidSolverStatus status;

        public int particleCount;
        public int solverIterations;

        public int fluidHashBuilds;
        public int boundaryHashBuilds;

        public float lastStepMilliseconds;
        public float lastDensitySolveMilliseconds;
        public float lastCorrectionMilliseconds;
        public float lastBoundaryMilliseconds;
        public float lastViscosityMilliseconds;

        public bool usedBoundaryParticles;
        public bool usedAnalyticProjection;
        public bool usedXsphViscosity;

        public int gpuGridResolutionX;
        public int gpuGridResolutionY;
        public int gpuGridResolutionZ;
        public int gpuGridNodeCount;
        public float gpuCellSizeMeters;
        public int gpuDispatchCount;

        public bool projectionGridEnabled;
        public bool projectionBuffersReady;
        public int projectionGridNodeCount;
        public int projectionDispatchCount;
        public float projectionMinFluidCellMass;

        public bool projectionDivergenceEnabled;
        public bool projectionDivergenceBufferReady;
        public float projectionDivergenceScale;
        public float maxAbsProjectionDivergence;

        public bool pressureSolveEnabled;
        public int pressureJacobiIterations;
        public float pressureRhsScale;
        public float pressureJacobiRelaxation;
        public float maxProjectionPressure;

        public bool pressureGradientSubtractionEnabled;
        public float pressureGradientScale;
        public float maxPressureVelocityCorrection;
        public bool invertPressureGradientSign;

        public bool movingBucketProjectionCouplingEnabled;
        public bool movingBucketDivergenceBoundaryEnabled;
        public bool movingBucketGridBoundaryVelocityEnabled;
        public float projectionMovingBoundaryVelocityStrength;
        public float maxProjectionBoundaryVelocityCorrection;
    }
}