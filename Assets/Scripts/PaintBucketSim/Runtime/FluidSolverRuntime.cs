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
        public bool gpuActiveMpmGridBoundsEnabled;
        public bool gpuActiveMpmGridBoundsUsed;
        public int gpuActiveMpmGridNodeCount;
        public float gpuActiveMpmGridNodeFraction;
        public int gpuActiveMpmGridMinX;
        public int gpuActiveMpmGridMinY;
        public int gpuActiveMpmGridMinZ;
        public int gpuActiveMpmGridSizeX;
        public int gpuActiveMpmGridSizeY;
        public int gpuActiveMpmGridSizeZ;
        public bool gpuMpmTileOccupancyEnabled;
        public bool gpuMpmTileOccupancyUsed;
        public bool gpuTiledMpmGridDispatchEnabled;
        public bool gpuTiledMpmGridDispatchUsed;
        public bool gpuMpmParticleTileListsEnabled;
        public bool gpuMpmParticleTileListsUsed;
        public int gpuOwnerTileListRebuildInterval;
        public bool gpuOwnerTileListRebuilt;
        public bool gpuOwnerTileListReused;
        public bool gpuTiledP2GEnabled;
        public bool gpuTiledP2GUsed;
        public bool gpuTileOrderedParticlePipelineEnabled;
        public bool gpuTileOrderedParticlePipelineUsed;
        public bool gpuHybridTiledP2GEnabled;
        public bool gpuHybridTiledP2GUsed;
        public bool gpuCenteredHybridP2GOwnerEnabled;
        public bool gpuFusedG2PPostCollisionEnabled;
        public bool gpuFusedG2PPostCollisionUsed;
        public bool gpuFusedPreCollisionTileMarkEnabled;
        public bool gpuFusedPreCollisionTileMarkUsed;
        public bool gpuAdaptiveTransferStencilEnabled;
        public bool gpuAdaptiveTransferStencilUsed;
        public int gpuMpmTileSizeCells;
        public int gpuMpmTileResolutionX;
        public int gpuMpmTileResolutionY;
        public int gpuMpmTileResolutionZ;
        public int gpuMpmTileCount;
        public int gpuMpmActiveTileCount;
        public float gpuMpmActiveTileFraction;
        public int gpuMpmTileParticleListCapacity;
        public int gpuMpmTileParticleReferenceCount;
        public int gpuMpmTileMaxParticleReferences;
        public int gpuMpmTileParticleListOverflowCount;
        public int gpuDispatchCount;
        public bool gpuStageProfilingEnabled;
        public float gpuProfileMpmSetupMilliseconds;
        public float gpuProfileP2GMilliseconds;
        public float gpuProfileGridUpdateMilliseconds;
        public float gpuProfileMpmCoreMilliseconds;
        public float gpuProfileProjectionMilliseconds;
        public float gpuProfileParticlePostMilliseconds;
        public float gpuProfileTotalMilliseconds;
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
        public float gpuAverageLocalX01;
        public float gpuAverageLocalZ01;
        public float gpuAverageParticleSpeed;
        public float gpuMaximumParticleSpeed;
        public bool gpuReferenceDensityEosRequested;
        public bool gpuReferenceDensityEosUsed;
        public bool gpuGridDensityEosUsed;
        public float gpuGridDensityEosPressureScale;
        public float gpuReferenceGridRestDensity;
        public bool gpuReferenceProjectionFallbackUsed;
        public bool gpuAdaptiveMultiRateEnabled;
        public bool gpuAdaptiveActivityReady;
        public int gpuAdaptiveActivitySampleInterval;
        public int gpuAdaptiveActivityStepIndex;
        public int gpuAdaptivePriorityParticleCount;
        public int gpuAdaptiveCalmInteriorParticleCount;
        public int gpuAdaptiveAirParticleCount;
        public float gpuAdaptivePriorityParticleFraction;
        public float gpuAdaptiveAverageParticleSpeed;
        public float gpuAdaptiveMaximumParticleSpeed;
        public float gpuAdaptiveAverageJDeviation;
        public int gpuCalmInteriorDeformationInterval;
        public bool gpuCalmInteriorDeformationUpdatedThisStep;

        public int gpuConfiguredHoleCount;
        public bool gpuHoleOpeningEnabled;
        public bool gpuAirborneAdvectionEnabled;
        public bool gpuAirborneDispatchRan;
        public bool gpuPostBucketCollisionRan;
        public int gpuOutflowTransitionCount;
        public int gpuJetParticleCount;
        public int gpuJetMpmCollarParticleCount;
        public int gpuAirborneParticleCount;
        public int gpuLostParticleCount;

        public bool projectionGridEnabled;
        public bool projectionBuffersReady;
        public bool projectionRanThisSubstep;
        public int projectionSubstepInterval;
        public bool projectionAdaptiveCadenceEnabled;
        public bool projectionAdaptiveCalmMode;
        public int projectionEffectiveSubstepInterval;
        public int projectionCalmCounter;
        public int projectionAdaptiveCalmSubstepCount;
        public int projectionAdaptiveCalmEntryCount;
        public int projectionAdaptiveCalmExitCount;
        public bool pressureWarmStartEnabled;
        public float pressureWarmStartFactor;
        public int projectionGridNodeCount;
        public int projectionDispatchCount;
        public bool projectionSparsePressureDispatchEnabled;
        public bool projectionSparsePressureDispatchUsed;
        public bool projectionMpmTileDispatchEnabled;
        public bool projectionMpmTileDispatchUsed;
        public bool projectionDensityDriftCorrectionEnabled;
        public float projectionDensityDriftStrength;
        public float projectionDensityDriftMinRatio;
        public float projectionDensityDriftMaxDivergence;
        public int projectionSparsePressureCellCount;
        public float projectionSparsePressureCellFraction;
        public float projectionMinFluidCellMass;
        public bool projectionActiveBoundsEnabled;
        public bool projectionActiveBoundsUsed;
        public int projectionActiveNodeCount;
        public float projectionActiveNodeFraction;
        public int projectionActiveMinX;
        public int projectionActiveMinY;
        public int projectionActiveMinZ;
        public int projectionActiveSizeX;
        public int projectionActiveSizeY;
        public int projectionActiveSizeZ;

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
        public int pressureSolveMode;
        public int pressureJacobiIterations;
        public int pressureRedBlackSorIterations;
        public float pressureRhsScale;
        public float pressureJacobiRelaxation;
        public float pressureRedBlackSorOmega;
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
