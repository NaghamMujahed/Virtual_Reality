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
        public float gpuGridOriginX;
        public float gpuGridOriginY;
        public float gpuGridOriginZ;
        public bool gpuGridContainsBucket;
        public float gpuRequiredGridExtent;
        public int gpuActiveParticleCount;
        public int gpuOutOfGridParticleCount;
        public int gpuNoGridSupportParticleCount;
        public int gpuCollisionCorrectionCount;
        public int gpuNanInfCount;
        public int gpuDeformationJClampCount;
        public float gpuAverageDeformationJ;
        public float gpuMinimumDeformationJ;
        public float gpuMaximumDeformationJ;
        public float gpuAverageFillHeight01;
        public float gpuMinimumFillHeight01;
        public float gpuMaximumFillHeight01;
        public float gpuEstimatedFillSpan01;
        public float gpuAverageParticleSpeed;
        public float gpuMaximumParticleSpeed;

        public int gpuConfiguredHoleCount;
        public bool gpuHoleOpeningEnabled;
        public bool gpuAirborneAdvectionEnabled;
        public int gpuOutflowTransitionCount;
        public int gpuJetParticleCount;
        public int gpuAirborneParticleCount;
        public int gpuLostParticleCount;

        public bool projectionGridEnabled;
        public bool projectionBuffersReady;
        public bool projectionRanThisSubstep;
        public int projectionSubstepInterval;
        public bool pressureWarmStartEnabled;
        public float pressureWarmStartFactor;
        public int projectionGridNodeCount;
        public int projectionDispatchCount;
        public float projectionMinFluidCellMass;

        public bool projectionDivergenceEnabled;
        public bool projectionDivergenceBufferReady;
        public float projectionDivergenceScale;
        public float maxAbsProjectionDivergence;
        public int projectionFluidCellCount;
        public int projectionSolidCellCount;
        public int projectionAirCellCount;
        public float projectionAverageAbsDivergenceBefore;
        public float projectionMeasuredMaxAbsDivergenceBefore;
        public float projectionAverageAbsDivergenceAfter;
        public float projectionMeasuredMaxAbsDivergenceAfter;
        public float projectionAverageAbsPressure;
        public float projectionMeasuredMaxAbsPressure;
        public int projectionActivePressureCellCount;

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

        public bool gpuDiagnosticsEnabled;
        public bool gpuDiagnosticsReady;
        public int gpuDiagnosticsStepIndex;
    }
}
