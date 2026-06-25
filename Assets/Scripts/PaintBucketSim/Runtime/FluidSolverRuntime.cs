namespace PaintBucketSim.Runtime
{
    public enum FluidSolverType
    {
        CpuPbf = 0,

        GpuDfsphPaint = 10,

        // Legacy / removed from active architecture.
        GpuSparseMpmPrototype = 90
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

        //public int gpuGridResolutionX;
        //public int gpuGridResolutionY;
        //public int gpuGridResolutionZ;
        //public int gpuGridNodeCount;
        //public float gpuCellSizeMeters;
        public int gpuDispatchCount;

        //public bool projectionGridEnabled;
        //public bool projectionBuffersReady;
        //public int projectionGridNodeCount;
        //public int projectionDispatchCount;
        //public float projectionMinFluidCellMass;

        //public bool projectionDivergenceEnabled;
        //public bool projectionDivergenceBufferReady;
        //public float projectionDivergenceScale;
        //public float maxAbsProjectionDivergence;

        //public bool pressureSolveEnabled;
        //public int pressureJacobiIterations;
        //public float pressureRhsScale;
        //public float pressureJacobiRelaxation;
        //public float maxProjectionPressure;

        //public bool pressureGradientSubtractionEnabled;
        //public float pressureGradientScale;
        //public float maxPressureVelocityCorrection;
        //public bool invertPressureGradientSign;

        //public bool movingBucketProjectionCouplingEnabled;
        //public bool movingBucketDivergenceBoundaryEnabled;
        //public bool movingBucketGridBoundaryVelocityEnabled;
        //public float projectionMovingBoundaryVelocityStrength;
        //public float maxProjectionBoundaryVelocityCorrection;

        public bool dfsphBuffersReady;
        public int dfsphBufferCapacity;

        public float dfsphRestDensity;
        public float dfsphParticleRadius;
        public float dfsphSupportRadius;

        public int dfsphDensityIterations;
        public int dfsphDivergenceIterations;

        public bool dfsphNeighborSearchEnabled;
        public int dfsphGridResolutionX;
        public int dfsphGridResolutionY;
        public int dfsphGridResolutionZ;
        public int dfsphGridCellCount;
        public float dfsphGridCellSize;
        public int dfsphMaxParticlesPerCell;
        public bool dfsphNeighborCountDebugEnabled;

        public bool dfsphDensityComputationEnabled;
        public float dfsphMinDensityClamp;
        public float dfsphMaxDensityClamp;
        public bool dfsphDensityKernelWritesNeighborCount;

        public bool dfsphAlphaComputationEnabled;
        public float dfsphAlphaDenominatorEpsilon;
        public float dfsphMaxAlpha;
        public int dfsphMinNeighborsForFullAlpha;
        public float dfsphLowNeighborAlphaDamping;

        public bool dfsphDivergenceSolveEnabled;
        public float dfsphDivergenceErrorScale;
        public float dfsphDivergenceCorrectionStrength;
        public float dfsphMaxDivergencePressure;
        public float dfsphMaxDivergenceVelocityCorrection;
        public bool dfsphClampDivergenceToCompressionOnly;
        public bool dfsphInvertDivergenceErrorSign;
        public bool dfsphParticleAdvectionEnabled;


        public bool dfsphBucketBoundaryEnabled;
        public bool dfsphBucketBoundaryAfterAdvection;
        public int dfsphBucketBoundaryPasses;
        public float dfsphBucketBoundaryPadding;
        public float dfsphBucketBoundaryPositionStrength;
        public float dfsphBucketBoundaryVelocityStrength;
        public float dfsphBucketBoundaryRestitution;
        public float dfsphBucketBoundaryFriction;

        public bool dfsphMovingBucketWallVelocityEnabled;
        public bool dfsphMovingBucketPreSolveEnabled;
        public bool dfsphUseMovingWallVelocityInBoundary;
        public float dfsphMovingWallVelocityStrength;
        public float dfsphMaxMovingWallVelocity;
        public float dfsphMaxPreSolveWallVelocityCorrection;
        public float dfsphPreSolveWallInfluenceDistance;
        public float dfsphPreSolveWallVelocityCorrectionStrength;

        public bool dfsphPaintMaterialEnabled;
        public float dfsphXsphViscosityStrength;
        public float dfsphCohesionStrength;
        public float dfsphMaxPaintMaterialVelocityCorrection;
        public float dfsphAntiSprayDamping;
        public int dfsphAntiSprayNeighborThreshold;
        public bool dfsphWallAdhesionEnabled;
        public float dfsphWallAdhesionDistance;
        public float dfsphWallTangentialDamping;
        public float dfsphWallNormalDamping;
        public float dfsphWallAdhesionAttraction;
    }
}