using System.Diagnostics;
using PaintBucketSim.Core;
using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Fluid.Solvers
{
    public sealed class GpuMpmDenseLocalSolver : IFluidSolver
    {
        private GraphicsBuffer _gridAccumIntBuffer;
        private GraphicsBuffer _gridVelocityMassBuffer;

        private GraphicsBuffer _projectionCellTypeBuffer;
        private GraphicsBuffer _projectionCellMassIntBuffer;
        private GraphicsBuffer _projectionCellDataBuffer;

        private GraphicsBuffer _projectionPressureBuffer;
        private GraphicsBuffer _projectionPressureTempBuffer;
        private GraphicsBuffer _projectionDivergenceBuffer;
        private GraphicsBuffer _projectionDivergenceAfterBuffer;
        private GraphicsBuffer _projectionFaceVelocityBuffer;
        private GraphicsBuffer _projectionFluidCellIndicesBuffer;
        private GraphicsBuffer _projectionFluidCellMetaBuffer;
        private GraphicsBuffer _projectionFluidCellDispatchArgsBuffer;
        private GraphicsBuffer _adaptiveParticlePriorityBuffer;
        private GraphicsBuffer _adaptiveActivitySummaryBuffer;
        private GraphicsBuffer _diagnosticsBuffer;
        private GraphicsBuffer _mpmTileFlagsBuffer;
        private GraphicsBuffer _mpmActiveTileIndicesBuffer;
        private GraphicsBuffer _mpmTileDispatchArgsBuffer;
        private GraphicsBuffer _mpmTileOwnerParticleCountsBuffer;
        private GraphicsBuffer _mpmTileOwnerParticleWriteCountsBuffer;
        private GraphicsBuffer _mpmTileOwnerParticleOffsetsBuffer;
        private GraphicsBuffer _mpmTileOwnerParticleIndicesBuffer;
        private GraphicsBuffer _mpmTileSupportMasksBuffer;
        private GraphicsBuffer _bucketHoleData0Buffer;
        private GraphicsBuffer _bucketHoleData1Buffer;
        private GraphicsBuffer _bucketHoleData2Buffer;
        private GraphicsBuffer _bucketHoleData3Buffer;
        private GraphicsBuffer _bucketHoleData4Buffer;

        private Vector4[] _bucketHoleData0;
        private Vector4[] _bucketHoleData1;
        private Vector4[] _bucketHoleData2;
        private Vector4[] _bucketHoleData3;
        private Vector4[] _bucketHoleData4;
        private int _uploadedBucketHoleCount;

        private int _kernelClearMpmDiagnostics = -1;
        private int _kernelClearAdaptiveActivity = -1;
        private int _kernelClassifyAdaptiveActivity = -1;
        private int _kernelClearMpmTileData = -1;
        private int _kernelMarkMpmActiveTiles = -1;
        private int _kernelExpandMpmTileSupportMasks = -1;
        private int _kernelBucketCollisionMarkMpmActiveTiles = -1;
        private int _kernelBuildMpmTileOwnerOffsets = -1;
        private int _kernelFillMpmTileOwnerParticleList = -1;
        private int _kernelClearGridActiveTiles = -1;
        private int _kernelGridUpdateActiveTiles = -1;
        private int _kernelP2GHybridTiled = -1;
        private int _kernelP2GReferenceDensityStressHybrid = -1;
        //private int _kernelG2P               = -1;
        private int _kernelG2PVelocityApic   = -1;
        private int _kernelG2PVelocityApicBucketCollision = -1;
        private int _kernelUpdateDeformation = -1;
        private int _kernelApplyMovingBucketProjectionBoundaryVelocity = -1;

        private int _kernelClearProjectionGrid = -1;
        private int _kernelMarkBucketProjectionSolids = -1;
        private int _kernelMarkProjectionFluidCells = -1;
        private int _kernelFinalizeProjectionGrid = -1;
        private int _kernelClearProjectionFluidCellList = -1;
        private int _kernelBuildProjectionFluidCellDispatchArgs = -1;
        private int _kernelBuildProjectionFaceVelocities = -1;
        private int _kernelComputeProjectionDivergence = -1;

        private int _kernelJacobiProjectionPressure = -1;
        private int _kernelJacobiProjectionPressureSparse = -1;
        private int _kernelRedBlackSorProjectionPressure = -1;
        private int _kernelRedBlackSorProjectionPressureSparse = -1;
        private int _kernelSubtractProjectionPressureGradient = -1;
        private int _kernelApplyProjectionFaceVelocitiesToGrid = -1;
        private int _kernelCollectProjectionDiagnostics = -1;

        /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// /// /// ///
        private int _kernelBucketCollision = -1;
        /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// /// /// ///
        private int _kernelStepAirborneParticles = -1;

        private FluidSolverStats _stats;

        private readonly Stopwatch _cpuDispatchWatch = new Stopwatch();
        private readonly Stopwatch _gpuStageProfileWatch = new Stopwatch();
        private Vector3 _runtimeGridOriginWorld;
        private Vector3Int _mpmTileResolution;
        private int _mpmTileCount;
        private int _mpmTileSizeCells = 8;
        private int _mpmTileSizeShift = 3;
        private bool _useMpmTileOccupancyThisStep;
        private bool _useTiledMpmGridDispatchThisStep;
        private bool _useMpmParticleTileListsThisStep;
        private bool _rebuildMpmOwnerParticleListsThisStep;
        private bool _mpmOwnerParticleListsReady;
        private int _mpmOwnerParticleListParticleCount;
        private int _effectiveOwnerTileListRebuildInterval = 1;
        private bool _useHybridTiledP2GThisStep;
        private bool _useReferenceDensityEosThisStep;
        private bool _useGridDensityEosThisStep;
        private bool _useAdaptiveTransferStencilThisStep;
        private bool _useFusedG2PPostCollisionThisStep;
        private bool _useFusedPreCollisionTileMarkThisStep;
        private bool _executionModeLogged;
        private Vector3Int _activeProjectionMin;
        private Vector3Int _activeProjectionSize;
        private int _activeProjectionNodeCount;
        private bool _useActiveProjectionBoundsThisStep;
        private bool _useSparseProjectionPressureDispatchThisStep;
        private bool _useMpmTileProjectionDispatchThisStep;
        private bool _gridContainsBucket;
        private bool _gridCoverageErrorLogged;
        private bool _tiledPathErrorLogged;
        private bool _diagnosticsReadbackPending;
        private bool _adaptiveActivityReadbackPending;
        private bool _adaptiveActivityReady;
        private int _adaptiveActivityRequestedStepIndex;
        private int _adaptiveActivityStepIndex;
        private int _adaptiveActiveParticleCount;
        private int _adaptivePriorityParticleCount;
        private int _adaptiveCalmParticleCount;
        private int _adaptiveAirParticleCount;
        private float _adaptiveAverageSpeed;
        private float _adaptiveMaximumSpeed;
        private float _adaptiveAverageJDeviation;
        private int _adaptiveProjectionCalmCounter;
        private bool _adaptiveProjectionCalmMode;
        private int _effectiveProjectionInterval = 1;
        private int _lastProjectionCompletedStep;
        private int _adaptiveProjectionCalmSubstepCount;
        private int _adaptiveProjectionCalmEntryCount;
        private int _adaptiveProjectionCalmExitCount;
        private bool _hasPressureHistory;
        private bool _mayHaveAirDomainParticles;
        private int _diagnosticsRequestedStepIndex;
        private int _lastDiagnosticsReadbackStep;
        private int _stepIndex;

        private const int DiagnosticsValueCount = 31;
        private const float DiagnosticsDivergenceScale = 100.0f;
        private const float DiagnosticsPressureScale = 1000000.0f;
        private const float DiagnosticsJScale = 1000.0f;
        private const float DiagnosticsHeightScale = 1000.0f;
        private const float DiagnosticsSpeedScale = 100.0f;
        private const int DiagnosticOutflowTransitions = 18;
        private const int DiagnosticJetParticles = 19;
        private const int DiagnosticAirborneParticles = 20;
        private const int DiagnosticLostParticles = 21;
        private const int DiagnosticLocalYSum = 22;
        private const int DiagnosticLocalYMin = 23;
        private const int DiagnosticLocalYMax = 24;
        private const int DiagnosticSpeedSum = 25;
        private const int DiagnosticSpeedMax = 26;
        private const int DiagnosticMpmActiveTiles = 27;
        private const int DiagnosticMpmTileCount = 28;
        private const int DiagnosticJetMpmCollarParticles = 30;

        public FluidSolverType SolverType => FluidSolverType.GpuSparseMpmPrototype;
        public bool IsInitialized { get; private set; }
        public FluidSolverStats Stats => _stats;

        public void Initialize(FluidSolverContext context)
        {
            Dispose();

            if (!ValidateContext(context))
            {
                _stats.status = FluidSolverStatus.Error;
                IsInitialized = false;
                return;
            }

            ResolveKernels(context);

            if (!HasValidKernels(context))
            {
                _stats.status = FluidSolverStatus.Error;
                IsInitialized = false;
                UnityEngine.Debug.LogError("GpuMpmDenseLocalSolver: Missing compute kernels.");
                return;
            }

            GpuFluidBufferSet gpuBuffers = context.GpuBufferSet;

            // Initial CPU → GPU upload, then GPU owns buffers.
            gpuBuffers.SetExternalGpuSimulationMode(false);
            gpuBuffers.EnsureBuffersPublic();
            gpuBuffers.UploadFromCpuParticlesNow();
            gpuBuffers.SetExternalGpuSimulationMode(true);

            AllocateGridBuffers(context);

            _stats = new FluidSolverStats
            {
                solverType = FluidSolverType.GpuSparseMpmPrototype,
                status = FluidSolverStatus.Running,
                particleCount = gpuBuffers.UploadedParticleCount,
                solverIterations = 1,

                gpuGridResolutionX = context.GpuMpmConfig.gridResolution.x,
                gpuGridResolutionY = context.GpuMpmConfig.gridResolution.y,
                gpuGridResolutionZ = context.GpuMpmConfig.gridResolution.z,
                gpuGridNodeCount = context.GpuMpmConfig.GridNodeCount,
                gpuCellSizeMeters = context.GpuMpmConfig.cellSizeMeters
            };

            IsInitialized = true;

            if (context.GpuMpmConfig.logLifecycle)
            {
                UnityEngine.Debug.Log(
                    "GpuMpmDenseLocalSolver: Initialized dense local GPU MPM prototype. " +
                    $"Particles={gpuBuffers.UploadedParticleCount}, GridNodes={context.GpuMpmConfig.GridNodeCount}"
                );
            }
        }

        public void Reset(FluidSolverContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            if (_gridAccumIntBuffer != null)
            {
                _gridAccumIntBuffer.Release();
                _gridAccumIntBuffer = null;
            }

            if (_gridVelocityMassBuffer != null)
            {
                _gridVelocityMassBuffer.Release();
                _gridVelocityMassBuffer = null;
            }

            if (_projectionCellTypeBuffer != null)
            {
                _projectionCellTypeBuffer.Release();
                _projectionCellTypeBuffer = null;
            }

            if (_projectionCellMassIntBuffer != null)
            {
                _projectionCellMassIntBuffer.Release();
                _projectionCellMassIntBuffer = null;
            }

            if (_projectionCellDataBuffer != null)
            {
                _projectionCellDataBuffer.Release();
                _projectionCellDataBuffer = null;
            }

            if (_projectionPressureBuffer != null)
            {
                _projectionPressureBuffer.Release();
                _projectionPressureBuffer = null;
            }

            if (_projectionPressureTempBuffer != null)
            {
                _projectionPressureTempBuffer.Release();
                _projectionPressureTempBuffer = null;
            }

            if (_projectionDivergenceBuffer != null)
            {
                _projectionDivergenceBuffer.Release();
                _projectionDivergenceBuffer = null;
            }

            if (_projectionDivergenceAfterBuffer != null)
            {
                _projectionDivergenceAfterBuffer.Release();
                _projectionDivergenceAfterBuffer = null;
            }

            if (_projectionFaceVelocityBuffer != null)
            {
                _projectionFaceVelocityBuffer.Release();
                _projectionFaceVelocityBuffer = null;
            }

            if (_projectionFluidCellIndicesBuffer != null)
            {
                _projectionFluidCellIndicesBuffer.Release();
                _projectionFluidCellIndicesBuffer = null;
            }

            if (_projectionFluidCellMetaBuffer != null)
            {
                _projectionFluidCellMetaBuffer.Release();
                _projectionFluidCellMetaBuffer = null;
            }

            if (_projectionFluidCellDispatchArgsBuffer != null)
            {
                _projectionFluidCellDispatchArgsBuffer.Release();
                _projectionFluidCellDispatchArgsBuffer = null;
            }

            if (_adaptiveParticlePriorityBuffer != null)
            {
                _adaptiveParticlePriorityBuffer.Release();
                _adaptiveParticlePriorityBuffer = null;
            }

            if (_adaptiveActivitySummaryBuffer != null)
            {
                _adaptiveActivitySummaryBuffer.Release();
                _adaptiveActivitySummaryBuffer = null;
            }

            if (_diagnosticsBuffer != null)
            {
                _diagnosticsBuffer.Release();
                _diagnosticsBuffer = null;
            }

            if (_mpmTileFlagsBuffer != null)
            {
                _mpmTileFlagsBuffer.Release();
                _mpmTileFlagsBuffer = null;
            }

            if (_mpmActiveTileIndicesBuffer != null)
            {
                _mpmActiveTileIndicesBuffer.Release();
                _mpmActiveTileIndicesBuffer = null;
            }

            if (_mpmTileDispatchArgsBuffer != null)
            {
                _mpmTileDispatchArgsBuffer.Release();
                _mpmTileDispatchArgsBuffer = null;
            }

            if (_mpmTileOwnerParticleCountsBuffer != null)
            {
                _mpmTileOwnerParticleCountsBuffer.Release();
                _mpmTileOwnerParticleCountsBuffer = null;
            }

            if (_mpmTileOwnerParticleWriteCountsBuffer != null)
            {
                _mpmTileOwnerParticleWriteCountsBuffer.Release();
                _mpmTileOwnerParticleWriteCountsBuffer = null;
            }

            if (_mpmTileOwnerParticleOffsetsBuffer != null)
            {
                _mpmTileOwnerParticleOffsetsBuffer.Release();
                _mpmTileOwnerParticleOffsetsBuffer = null;
            }

            if (_mpmTileOwnerParticleIndicesBuffer != null)
            {
                _mpmTileOwnerParticleIndicesBuffer.Release();
                _mpmTileOwnerParticleIndicesBuffer = null;
            }

            if (_mpmTileSupportMasksBuffer != null)
            {
                _mpmTileSupportMasksBuffer.Release();
                _mpmTileSupportMasksBuffer = null;
            }

            if (_bucketHoleData0Buffer != null)
            {
                _bucketHoleData0Buffer.Release();
                _bucketHoleData0Buffer = null;
            }

            if (_bucketHoleData1Buffer != null)
            {
                _bucketHoleData1Buffer.Release();
                _bucketHoleData1Buffer = null;
            }

            if (_bucketHoleData2Buffer != null)
            {
                _bucketHoleData2Buffer.Release();
                _bucketHoleData2Buffer = null;
            }

            if (_bucketHoleData3Buffer != null)
            {
                _bucketHoleData3Buffer.Release();
                _bucketHoleData3Buffer = null;
            }

            if (_bucketHoleData4Buffer != null)
            {
                _bucketHoleData4Buffer.Release();
                _bucketHoleData4Buffer = null;
            }

            _bucketHoleData0 = null;
            _bucketHoleData1 = null;
            _bucketHoleData2 = null;
            _bucketHoleData3 = null;
            _bucketHoleData4 = null;
            _uploadedBucketHoleCount = 0;

            _diagnosticsReadbackPending = false;
            _hasPressureHistory = false;
            _mayHaveAirDomainParticles = false;
            _diagnosticsRequestedStepIndex = 0;
            _lastDiagnosticsReadbackStep = 0;
            _stepIndex = 0;
            _mpmTileResolution = Vector3Int.zero;
            _mpmTileCount = 0;
            _mpmTileSizeCells = 8;
            _mpmTileSizeShift = 3;
            _useMpmTileOccupancyThisStep = false;
            _useTiledMpmGridDispatchThisStep = false;
            _useMpmParticleTileListsThisStep = false;
            _rebuildMpmOwnerParticleListsThisStep = false;
            _mpmOwnerParticleListsReady = false;
            _mpmOwnerParticleListParticleCount = 0;
            _effectiveOwnerTileListRebuildInterval = 1;
            _useHybridTiledP2GThisStep = false;
            _useReferenceDensityEosThisStep = false;
            _useGridDensityEosThisStep = false;
            _useAdaptiveTransferStencilThisStep = false;
            _executionModeLogged = false;
            _useSparseProjectionPressureDispatchThisStep = false;
            _adaptiveActivityReadbackPending = false;
            _adaptiveActivityReady = false;
            _adaptiveActivityRequestedStepIndex = 0;
            _adaptiveActivityStepIndex = 0;
            _adaptiveActiveParticleCount = 0;
            _adaptivePriorityParticleCount = 0;
            _adaptiveCalmParticleCount = 0;
            _adaptiveAirParticleCount = 0;
            _adaptiveAverageSpeed = 0.0f;
            _adaptiveMaximumSpeed = 0.0f;
            _adaptiveAverageJDeviation = 0.0f;
            _adaptiveProjectionCalmCounter = 0;
            _adaptiveProjectionCalmMode = false;
            _effectiveProjectionInterval = 1;
            _lastProjectionCompletedStep = 0;
            _adaptiveProjectionCalmSubstepCount = 0;
            _adaptiveProjectionCalmEntryCount = 0;
            _adaptiveProjectionCalmExitCount = 0;
            _activeProjectionMin = Vector3Int.zero;
            _activeProjectionSize = Vector3Int.zero;
            _activeProjectionNodeCount = 0;
            _useActiveProjectionBoundsThisStep = false;
            _useFusedG2PPostCollisionThisStep = false;
            _useFusedPreCollisionTileMarkThisStep = false;
            _tiledPathErrorLogged = false;
            IsInitialized = false;
        }

        public void Step(
            FluidSolverContext solverContext,
            SimulationContext simulationContext,
            FluidSolverStepInput stepInput)
        {
            if (!IsInitialized ||
                solverContext == null ||
                solverContext.GpuMpmConfig == null ||
                solverContext.GpuBufferSet == null ||
                !solverContext.GpuBufferSet.IsInitialized)
            {
                return;
            }

            if (!solverContext.GpuMpmConfig.enableGpuDenseMpm)
                return;

            int particleCount = solverContext.GpuBufferSet.UploadedParticleCount;
            int gridNodeCount = solverContext.GpuMpmConfig.GridNodeCount;

            if (particleCount <= 0 || gridNodeCount <= 0)
                return;

            _cpuDispatchWatch.Restart();

            ComputeShader compute = solverContext.GpuMpmConfig.denseLocalMpmCompute;

            UpdateRuntimeGridPlacement(solverContext);
            UpdateMpmTileDispatchMode(solverContext);
            if (!_useMpmTileOccupancyThisStep ||
                !_useMpmParticleTileListsThisStep ||
                !_useHybridTiledP2GThisStep ||
                !_useTiledMpmGridDispatchThisStep)
            {
                _stats.status = FluidSolverStatus.Error;
                if (!_tiledPathErrorLogged)
                {
                    _tiledPathErrorLogged = true;
                    UnityEngine.Debug.LogError(
                        "GpuMpmDenseLocalSolver: The required tiled MLS-MPM path " +
                        "is unavailable. The rejected particle-ordering and dense " +
                        "P2G fallbacks are intentionally disabled."
                    );
                }
                return;
            }
            bool projectionPathEnabled =
                solverContext.GpuMpmConfig.enableProjectionGridInfrastructure &&
                (
                    !_useReferenceDensityEosThisStep ||
                    solverContext.GpuMpmConfig
                        .referenceEnableProjectionFallback
                );
            if (projectionPathEnabled)
            {
                UpdateActiveProjectionBounds(solverContext);
                UpdateSparseProjectionPressureDispatchMode(solverContext);
                UpdateMpmTileProjectionDispatchMode(solverContext);
            }
            else
            {
                _useActiveProjectionBoundsThisStep = false;
                _useSparseProjectionPressureDispatchThisStep = false;
                _useMpmTileProjectionDispatchThisStep = false;
            }

            if (!_executionModeLogged &&
                solverContext.GpuMpmConfig.logLifecycle)
            {
                UnityEngine.Debug.Log(
                    "GpuMpmDenseLocalSolver: " +
                    $"ExecutionMode={(_useGridDensityEosThisStep ? "GridDensityEOS" : _useReferenceDensityEosThisStep ? "ParticleDensityEOS" : "ProjectedFJ")}, " +
                    $"Projection={projectionPathEnabled}, " +
                    $"Jacobi={projectionPathEnabled && solverContext.GpuMpmConfig.enableJacobiPressureSolve}, " +
                    $"Deformation={!_useReferenceDensityEosThisStep}, " +
                    $"AdaptiveSampling={solverContext.GpuMpmConfig.enableAdaptiveMultiRate && (!_useReferenceDensityEosThisStep || _useAdaptiveTransferStencilThisStep)}, " +
                    $"AdaptiveTransfer={_useAdaptiveTransferStencilThisStep}"
                );
                _executionModeLogged = true;
            }

            if (solverContext.GpuMpmConfig.rejectUndersizedGrid &&
                !_gridContainsBucket)
            {
                _stats.status = FluidSolverStatus.Error;

                if (!_gridCoverageErrorLogged)
                {
                    _gridCoverageErrorLogged = true;
                    UnityEngine.Debug.LogError(
                        "GpuMpmDenseLocalSolver: Dense grid does not contain the rotated bucket " +
                        "and quadratic transfer support. Increase gridResolution/cellSizeMeters " +
                        "or reduce gridBucketMarginMeters."
                    );
                }

                return;
            }

            SetCommonParameters(solverContext, simulationContext, stepInput);

            bool profileGpuStages =
                solverContext.GpuMpmConfig.enableGpuStageProfiling;
            if (profileGpuStages)
            {
                _gpuStageProfileWatch.Restart();
            }
            else
            {
                _stats.gpuProfileMpmCoreMilliseconds = 0.0f;
                _stats.gpuProfileMpmSetupMilliseconds = 0.0f;
                _stats.gpuProfileP2GMilliseconds = 0.0f;
                _stats.gpuProfileGridUpdateMilliseconds = 0.0f;
                _stats.gpuProfileProjectionMilliseconds = 0.0f;
                _stats.gpuProfileParticlePostMilliseconds = 0.0f;
                _stats.gpuProfileTotalMilliseconds = 0.0f;
            }

            int adaptiveDispatches = RunAdaptiveActivitySampling(
                compute: compute,
                context: solverContext,
                particleCount: particleCount
            );

            bool runBucketCollision =
                solverContext.GpuMpmConfig.enableGpuBucketCollision &&
                solverContext.GpuMpmConfig.useRealBucketCollision &&
                solverContext.BucketSystem != null &&
                solverContext.BucketSystem.IsInitialized &&
                _kernelBucketCollision >= 0;
            bool runPostBucketCollision =
                runBucketCollision &&
                solverContext.GpuMpmConfig
                    .enablePostG2PBucketCollision &&
                (
                    _useReferenceDensityEosThisStep ||
                    ShouldRunPostBucketCollision(solverContext.GpuMpmConfig)
                );
            _useFusedG2PPostCollisionThisStep =
                runPostBucketCollision &&
                _kernelG2PVelocityApicBucketCollision >= 0;
            _useFusedPreCollisionTileMarkThisStep =
                runBucketCollision &&
                _useMpmTileOccupancyThisStep &&
                _useMpmParticleTileListsThisStep &&
                _kernelBucketCollisionMarkMpmActiveTiles >= 0;

            bool runGpuDiagnostics =
                solverContext.GpuMpmConfig.enableGpuDiagnostics;

            if (runGpuDiagnostics)
            {
                BindClearMpmDiagnostics(compute);
                compute.Dispatch(_kernelClearMpmDiagnostics, 1, 1, 1);
            }

            if (runBucketCollision &&
                !_useFusedPreCollisionTileMarkThisStep)
            {
                // Pre-solve safety pass: any correction now participates in P2G
                // and therefore in the same-substep grid/projection solve.
                BindBucketCollision(compute, solverContext);
                compute.Dispatch(_kernelBucketCollision, Groups(particleCount), 1, 1);
            }

            int tileDispatches = RunMpmTileOccupancy(
                compute,
                solverContext,
                particleCount,
                _useFusedPreCollisionTileMarkThisStep
            );

            BindClearGridActiveTiles(compute);
            DispatchMpmActiveTileKernel(compute, _kernelClearGridActiveTiles);

            float gpuMpmSetupMilliseconds =
                profileGpuStages
                    ? CompleteGpuStageProfileSegment()
                    : 0.0f;

            BindP2GHybridTiled(compute, solverContext);
            DispatchMpmActiveTileKernel(
                compute,
                _kernelP2GHybridTiled
            );

            int referenceDensityDispatches = 0;
            if (_useReferenceDensityEosThisStep &&
                !_useGridDensityEosThisStep)
            {
                BindP2GReferenceDensityStressHybrid(
                    compute,
                    solverContext
                );
                DispatchMpmActiveTileKernel(
                    compute,
                    _kernelP2GReferenceDensityStressHybrid
                );
                referenceDensityDispatches = 1;
            }

            float gpuP2GMilliseconds =
                profileGpuStages
                    ? CompleteGpuStageProfileSegment()
                    : 0.0f;

            BindGridUpdateActiveTiles(compute);
            DispatchMpmActiveTileKernel(compute, _kernelGridUpdateActiveTiles);

            float gpuGridUpdateMilliseconds =
                profileGpuStages
                    ? CompleteGpuStageProfileSegment()
                    : 0.0f;
            float gpuMpmCoreMilliseconds =
                gpuMpmSetupMilliseconds +
                gpuP2GMilliseconds +
                gpuGridUpdateMilliseconds;

            int projectionInterval = projectionPathEnabled
                ? ResolveAdaptiveProjectionInterval(solverContext)
                : 0;
            bool runProjection =
                projectionPathEnabled &&
                ShouldRunProjectionThisStep(
                    solverContext.GpuMpmConfig,
                    projectionInterval
                );

            if (runProjection)
                _lastProjectionCompletedStep = _stepIndex + 1;

            int projectionDispatches = runProjection
                ? RunProjectionGridInfrastructure(
                    compute,
                    solverContext,
                    particleCount,
                    gridNodeCount
                )
                : 0;

            float gpuProjectionMilliseconds =
                profileGpuStages
                    ? CompleteGpuStageProfileSegment()
                    : 0.0f;

            if (runProjection &&
                solverContext.GpuMpmConfig.enableJacobiPressureSolve)
            {
                _hasPressureHistory = true;
            }

            //BindG2P(compute, solverContext);
            //compute.Dispatch(_kernelG2P, Groups(particleCount), 1, 1);
            int g2pKernel = _useFusedG2PPostCollisionThisStep
                ? _kernelG2PVelocityApicBucketCollision
                : _kernelG2PVelocityApic;
            BindG2PVelocityApic(compute, solverContext, g2pKernel);
            if (_useFusedG2PPostCollisionThisStep)
            {
                BindBucketCollision(
                    compute,
                    solverContext,
                    g2pKernel
                );
            }
            DispatchParticleKernel(
                compute,
                g2pKernel,
                particleCount
            );

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// /// ///
            ///
            if (!runBucketCollision)
            {
                UnityEngine.Debug.LogWarning(
                    "MLS-MPM solver is running without real bucket collision. " +
                    "This should only happen during debugging."
                );
            }
            //BindBucketCollision(compute, solverContext);
            //compute.Dispatch(_kernelBucketCollision, Groups(particleCount), 1, 1);
            /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// ///

            int deformationDispatches = 0;
            if (!_useReferenceDensityEosThisStep)
            {
                BindUpdateDeformation(
                    compute,
                    solverContext,
                    _kernelUpdateDeformation
                );
                DispatchParticleKernel(
                    compute,
                    _kernelUpdateDeformation,
                    particleCount
                );
                deformationDispatches = 1;
            }

            bool runAirborneAdvection =
                ShouldRunAirborneAdvection(solverContext.GpuMpmConfig);
            int airborneDispatches = 0;
            if (runAirborneAdvection)
            {
                BindStepAirborneParticles(compute, solverContext);
                compute.Dispatch(_kernelStepAirborneParticles, Groups(particleCount), 1, 1);
                airborneDispatches = 1;
            }

            float gpuParticlePostMilliseconds =
                profileGpuStages
                    ? CompleteGpuStageProfileSegment(restart: false)
                    : 0.0f;

            solverContext.GpuBufferSet.SetUploadedParticleCount(particleCount);

            _stepIndex++;
            RequestDiagnosticsReadbackIfDue(
                solverContext,
                runProjection
            );

            _cpuDispatchWatch.Stop();

            _stats.status = FluidSolverStatus.Running;
            _stats.particleCount = particleCount;
            _stats.lastStepMilliseconds =
                (float)_cpuDispatchWatch.Elapsed.TotalMilliseconds;

            // These timings are CPU dispatch-submit timings, not real GPU execution timings.
            int baseDispatches = runGpuDiagnostics ? 5 : 4;
            // Optional ClearDiagnostics, ClearGrid, P2G, GridUpdate, G2P

            _stats.gpuDispatchCount =
                baseDispatches +
                adaptiveDispatches +
                tileDispatches +
                referenceDensityDispatches +
                projectionDispatches +
                deformationDispatches +
                airborneDispatches;
            _stats.gpuStageProfilingEnabled = profileGpuStages;
            _stats.gpuProfileMpmSetupMilliseconds =
                gpuMpmSetupMilliseconds;
            _stats.gpuProfileP2GMilliseconds =
                gpuP2GMilliseconds;
            _stats.gpuProfileGridUpdateMilliseconds =
                gpuGridUpdateMilliseconds;
            _stats.gpuProfileMpmCoreMilliseconds =
                gpuMpmCoreMilliseconds;
            _stats.gpuProfileProjectionMilliseconds =
                gpuProjectionMilliseconds;
            _stats.gpuProfileParticlePostMilliseconds =
                gpuParticlePostMilliseconds;
            _stats.gpuProfileTotalMilliseconds =
                gpuMpmCoreMilliseconds +
                gpuProjectionMilliseconds +
                gpuParticlePostMilliseconds;
            _stats.gpuGridResolutionX = solverContext.GpuMpmConfig.gridResolution.x;
            _stats.gpuGridResolutionY = solverContext.GpuMpmConfig.gridResolution.y;
            _stats.gpuGridResolutionZ = solverContext.GpuMpmConfig.gridResolution.z;
            _stats.gpuGridNodeCount = gridNodeCount;
            _stats.gpuCellSizeMeters = solverContext.GpuMpmConfig.cellSizeMeters;
            _stats.gpuReferenceDensityEosRequested =
                solverContext.GpuMpmConfig.enableReferenceDensityEosMode;
            _stats.gpuReferenceDensityEosUsed =
                _useReferenceDensityEosThisStep;
            _stats.gpuGridDensityEosUsed =
                _useGridDensityEosThisStep;
            _stats.gpuGridDensityEosPressureScale =
                _useGridDensityEosThisStep
                    ? solverContext.GpuMpmConfig
                        .gridDensityEosPressureScale
                    : 0.0f;
            _stats.gpuReferenceGridRestDensity =
                Mathf.Max(
                    solverContext.MaterialConfig.densityKgPerM3 *
                    solverContext.ReferenceDensityScale,
                    1.0f
                );
            _stats.gpuReferenceProjectionFallbackUsed =
                _useReferenceDensityEosThisStep &&
                runProjection;
            _stats.gpuMpmTileOccupancyEnabled = true;
            _stats.gpuMpmTileOccupancyUsed =
                _useMpmTileOccupancyThisStep;
            _stats.gpuTiledMpmGridDispatchEnabled = true;
            _stats.gpuTiledMpmGridDispatchUsed =
                _useTiledMpmGridDispatchThisStep;
            _stats.gpuMpmParticleTileListsEnabled = true;
            _stats.gpuMpmParticleTileListsUsed =
                _useMpmParticleTileListsThisStep;
            _stats.gpuMpmSupportMaskTopologyUsed =
                _mpmTileSupportMasksBuffer != null &&
                _kernelExpandMpmTileSupportMasks >= 0;
            _stats.gpuOwnerTileListRebuildInterval =
                _effectiveOwnerTileListRebuildInterval;
            _stats.gpuOwnerTileListRebuilt =
                _rebuildMpmOwnerParticleListsThisStep;
            _stats.gpuOwnerTileListReused =
                _useMpmParticleTileListsThisStep &&
                !_rebuildMpmOwnerParticleListsThisStep;
            _stats.gpuHybridTiledP2GEnabled = true;
            _stats.gpuHybridTiledP2GUsed =
                _useHybridTiledP2GThisStep;
            _stats.gpuFusedG2PPostCollisionEnabled = true;
            _stats.gpuFusedG2PPostCollisionUsed =
                _useFusedG2PPostCollisionThisStep;
            _stats.gpuFusedPreCollisionTileMarkEnabled = true;
            _stats.gpuFusedPreCollisionTileMarkUsed =
                _useFusedPreCollisionTileMarkThisStep;
            _stats.gpuAdaptiveTransferStencilEnabled =
                solverContext.GpuMpmConfig.enableAdaptiveTransferStencil;
            _stats.gpuAdaptiveTransferStencilUsed =
                _useAdaptiveTransferStencilThisStep;
            _stats.gpuMpmTileSizeCells = _mpmTileSizeCells;
            _stats.gpuMpmTileResolutionX = _mpmTileResolution.x;
            _stats.gpuMpmTileResolutionY = _mpmTileResolution.y;
            _stats.gpuMpmTileResolutionZ = _mpmTileResolution.z;
            _stats.gpuMpmTileCount = _mpmTileCount;
            _stats.gpuAdaptiveMultiRateEnabled =
                solverContext.GpuMpmConfig.enableAdaptiveMultiRate &&
                (
                    !_useReferenceDensityEosThisStep ||
                    _useAdaptiveTransferStencilThisStep
                );
            _stats.gpuAdaptiveActivityReady =
                _stats.gpuAdaptiveMultiRateEnabled &&
                _adaptiveActivityReady;
            _stats.gpuAdaptiveActivitySampleInterval =
                Mathf.Clamp(
                    solverContext.GpuMpmConfig.adaptiveActivitySampleInterval,
                    1,
                    32
                );
            _stats.gpuAdaptiveActivityStepIndex =
                _adaptiveActivityStepIndex;
            _stats.gpuAdaptivePriorityParticleCount =
                _adaptivePriorityParticleCount;
            _stats.gpuAdaptiveCalmInteriorParticleCount =
                _adaptiveCalmParticleCount;
            _stats.gpuAdaptiveAirParticleCount =
                _adaptiveAirParticleCount;
            _stats.gpuAdaptivePriorityParticleFraction =
                _adaptiveActiveParticleCount > 0
                    ? (float)_adaptivePriorityParticleCount /
                      _adaptiveActiveParticleCount
                    : 0.0f;
            _stats.gpuAdaptiveAverageParticleSpeed =
                _adaptiveAverageSpeed;
            _stats.gpuAdaptiveMaximumParticleSpeed =
                _adaptiveMaximumSpeed;
            _stats.gpuAdaptiveAverageJDeviation =
                _adaptiveAverageJDeviation;
            _stats.gpuCalmInteriorDeformationInterval =
                Mathf.Clamp(
                    solverContext.GpuMpmConfig
                        .calmInteriorDeformationInterval,
                    1,
                    4
                );
            _stats.gpuCalmInteriorDeformationUpdatedThisStep =
                Mathf.Max(0, _stepIndex - 1) %
                _stats.gpuCalmInteriorDeformationInterval == 0;

            _stats.projectionGridEnabled = projectionPathEnabled;
            _stats.projectionRanThisSubstep = runProjection;
            _stats.projectionSubstepInterval = projectionInterval;
            _stats.projectionAdaptiveCadenceEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enableAdaptiveMultiRate &&
                solverContext.GpuMpmConfig.enableAdaptiveProjectionCadence;
            _stats.projectionAdaptiveCalmMode =
                _adaptiveProjectionCalmMode;
            _stats.projectionEffectiveSubstepInterval =
                _effectiveProjectionInterval;
            _stats.projectionCalmCounter =
                _adaptiveProjectionCalmCounter;
            _stats.projectionAdaptiveCalmSubstepCount =
                _adaptiveProjectionCalmSubstepCount;
            _stats.projectionAdaptiveCalmEntryCount =
                _adaptiveProjectionCalmEntryCount;
            _stats.projectionAdaptiveCalmExitCount =
                _adaptiveProjectionCalmExitCount;
            _stats.pressureWarmStartEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enablePressureWarmStart;
            _stats.pressureWarmStartFactor =
                solverContext.GpuMpmConfig.pressureWarmStartFactor;
            _stats.projectionBuffersReady =
                projectionPathEnabled &&
                _projectionCellTypeBuffer != null &&
                _projectionCellMassIntBuffer != null &&
                _projectionCellDataBuffer != null &&
                _projectionPressureBuffer != null &&
                _projectionPressureTempBuffer != null &&
                _projectionDivergenceBuffer != null &&
                _projectionDivergenceAfterBuffer != null &&
                _projectionFaceVelocityBuffer != null &&
                _projectionFluidCellIndicesBuffer != null &&
                _projectionFluidCellMetaBuffer != null &&
                _projectionFluidCellDispatchArgsBuffer != null &&
                _diagnosticsBuffer != null;

            _stats.projectionGridNodeCount =
                projectionPathEnabled ? gridNodeCount : 0;
            _stats.projectionDispatchCount = projectionDispatches;
            _stats.projectionSparsePressureDispatchEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enableSparseProjectionPressureDispatch;
            _stats.projectionSparsePressureDispatchUsed =
                runProjection &&
                _useSparseProjectionPressureDispatchThisStep;
            _stats.projectionMpmTileDispatchEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enableMpmTileProjectionDispatch;
            _stats.projectionMpmTileDispatchUsed =
                runProjection &&
                _useMpmTileProjectionDispatchThisStep;
            _stats.projectionDensityDriftCorrectionEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig
                    .enableProjectionDensityDriftCorrection;
            _stats.projectionDensityDriftStrength =
                solverContext.GpuMpmConfig
                    .projectionDensityDriftStrength;
            _stats.projectionDensityDriftMinRatio =
                solverContext.GpuMpmConfig
                    .projectionDensityDriftMinRatio;
            _stats.projectionDensityDriftMaxDivergence =
                solverContext.GpuMpmConfig
                    .projectionDensityDriftMaxDivergence;
            _stats.projectionMinFluidCellMass = solverContext.GpuMpmConfig.minFluidCellMass;
            _stats.projectionActiveBoundsEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enableActiveProjectionBounds;
            _stats.projectionActiveBoundsUsed =
                _useActiveProjectionBoundsThisStep;
            _stats.projectionActiveNodeCount =
                projectionPathEnabled
                    ? GetProjectionDispatchNodeCount(gridNodeCount)
                    : 0;
            _stats.projectionActiveNodeFraction =
                gridNodeCount > 0
                    ? (float)_stats.projectionActiveNodeCount / gridNodeCount
                    : 1.0f;
            _stats.projectionActiveMinX = _activeProjectionMin.x;
            _stats.projectionActiveMinY = _activeProjectionMin.y;
            _stats.projectionActiveMinZ = _activeProjectionMin.z;
            _stats.projectionActiveSizeX = _activeProjectionSize.x;
            _stats.projectionActiveSizeY = _activeProjectionSize.y;
            _stats.projectionActiveSizeZ = _activeProjectionSize.z;

            _stats.projectionDivergenceEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig
                    .enableProjectionDivergenceComputation;

            _stats.projectionDivergenceBufferReady = _projectionDivergenceBuffer != null;

            _stats.projectionDivergenceScale = solverContext.GpuMpmConfig.projectionDivergenceScale;

            _stats.maxAbsProjectionDivergence = solverContext.GpuMpmConfig.maxAbsProjectionDivergence;

            _stats.pressureSolveEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.enableJacobiPressureSolve;

            _stats.pressureSolveMode = (int)solverContext.GpuMpmConfig.pressureSolveMode;

            _stats.pressureJacobiIterations =
                projectionPathEnabled
                    ? solverContext.GpuMpmConfig.pressureJacobiIterations
                    : 0;

            _stats.pressureRedBlackSorIterations =
                projectionPathEnabled
                    ? solverContext.GpuMpmConfig.pressureRedBlackSorIterations
                    : 0;

            _stats.pressureRhsScale = solverContext.GpuMpmConfig.pressureRhsScale;

            _stats.pressureJacobiRelaxation = solverContext.GpuMpmConfig.pressureJacobiRelaxation;

            _stats.pressureRedBlackSorOmega =
                solverContext.GpuMpmConfig.pressureRedBlackSorOmega;

            _stats.maxProjectionPressure = solverContext.GpuMpmConfig.maxProjectionPressure;

            _stats.pressureGradientSubtractionEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig
                    .enablePressureGradientSubtraction;

            _stats.pressureGradientScale = solverContext.GpuMpmConfig.pressureGradientScale;

            _stats.maxPressureVelocityCorrection = solverContext.GpuMpmConfig.maxPressureVelocityCorrection;

            _stats.invertPressureGradientSign = solverContext.GpuMpmConfig.invertPressureGradientSign;

            _stats.movingBucketProjectionCouplingEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig
                    .enableMovingBucketProjectionCoupling;

            _stats.movingBucketDivergenceBoundaryEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.useMovingBucketVelocityInDivergence;

            _stats.movingBucketGridBoundaryVelocityEnabled =
                projectionPathEnabled &&
                solverContext.GpuMpmConfig.applyMovingBucketGridBoundaryVelocity;

            _stats.projectionMovingBoundaryVelocityStrength =
                solverContext.GpuMpmConfig.projectionMovingBoundaryVelocityStrength;

            _stats.maxProjectionBoundaryVelocityCorrection =
                solverContext.GpuMpmConfig.maxProjectionBoundaryVelocityCorrection;

            _stats.gpuGridContainsBucket = _gridContainsBucket;
            _stats.gpuGridOriginX = _runtimeGridOriginWorld.x;
            _stats.gpuGridOriginY = _runtimeGridOriginWorld.y;
            _stats.gpuGridOriginZ = _runtimeGridOriginWorld.z;
            _stats.gpuDiagnosticsEnabled = solverContext.GpuMpmConfig.enableGpuDiagnostics;
            _stats.gpuConfiguredHoleCount = _uploadedBucketHoleCount;
            _stats.gpuHoleOpeningEnabled =
                solverContext.GpuMpmConfig.enableBottomHoleOpening &&
                _uploadedBucketHoleCount > 0;
            _stats.gpuAirborneAdvectionEnabled =
                solverContext.GpuMpmConfig.enableAirborneParticleAdvection;
            _stats.gpuAirborneDispatchRan = runAirborneAdvection;
            _stats.gpuPostBucketCollisionRan = runPostBucketCollision;
        }

        private bool ValidateContext(FluidSolverContext context)
        {
            if (context == null)
            {
                UnityEngine.Debug.LogError("GpuMpmDenseLocalSolver: Context is null.");
                return false;
            }

            if (context.GpuMpmConfig == null)
            {
                UnityEngine.Debug.LogError("GpuMpmDenseLocalSolver: Missing GpuMpmSolverConfig.");
                return false;
            }

            if (context.GpuMpmConfig.denseLocalMpmCompute == null)
            {
                UnityEngine.Debug.LogError("GpuMpmDenseLocalSolver: Missing dense local MPM compute shader.");
                return false;
            }

            if (context.GpuBufferSet == null)
            {
                UnityEngine.Debug.LogError("GpuMpmDenseLocalSolver: Missing GpuFluidBufferSet.");
                return false;
            }

            return true;
        }

        private void ResolveKernels(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            _kernelClearMpmDiagnostics = compute.FindKernel("KClearMpmDiagnostics");
            _kernelClearAdaptiveActivity = compute.FindKernel("KClearAdaptiveActivity");
            _kernelClassifyAdaptiveActivity = compute.FindKernel("KClassifyAdaptiveActivity");
            _kernelClearMpmTileData = compute.FindKernel("KClearMpmTileData");
            _kernelMarkMpmActiveTiles = compute.FindKernel("KMarkMpmActiveTiles");
            _kernelExpandMpmTileSupportMasks =
                compute.FindKernel("KExpandMpmTileSupportMasks");
            _kernelBucketCollisionMarkMpmActiveTiles =
                compute.FindKernel("KBucketCollisionMarkMpmActiveTiles");
            _kernelBuildMpmTileOwnerOffsets = compute.FindKernel("KBuildMpmTileOwnerOffsets");
            _kernelFillMpmTileOwnerParticleList =
                compute.FindKernel("KFillMpmTileOwnerParticleList");
            _kernelClearGridActiveTiles = compute.FindKernel("KClearGridActiveTiles");
            _kernelGridUpdateActiveTiles = compute.FindKernel("KGridUpdateActiveTiles");
            _kernelP2GHybridTiled = compute.FindKernel("KP2GHybridTiled");
            _kernelP2GReferenceDensityStressHybrid =
                compute.FindKernel("KP2GReferenceDensityStressHybrid");
            _kernelG2PVelocityApic = compute.FindKernel("KG2PVelocityApic");
            _kernelG2PVelocityApicBucketCollision =
                compute.FindKernel("KG2PVelocityApicBucketCollision");

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// 
            _kernelBucketCollision = compute.FindKernel("KBucketCollision");
            /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// 
            _kernelStepAirborneParticles = compute.FindKernel("KStepAirborneParticles");
            
            _kernelUpdateDeformation = compute.FindKernel("KUpdateDeformation");

            _kernelClearProjectionGrid = compute.FindKernel("KClearProjectionGrid");
            _kernelMarkBucketProjectionSolids = compute.FindKernel("KMarkBucketProjectionSolids");
            _kernelMarkProjectionFluidCells = compute.FindKernel("KMarkProjectionFluidCells");
            _kernelFinalizeProjectionGrid = compute.FindKernel("KFinalizeProjectionGrid");
            _kernelClearProjectionFluidCellList = compute.FindKernel("KClearProjectionFluidCellList");
            _kernelBuildProjectionFluidCellDispatchArgs = compute.FindKernel("KBuildProjectionFluidCellDispatchArgs");
            _kernelBuildProjectionFaceVelocities = compute.FindKernel("KBuildProjectionFaceVelocities");
            _kernelComputeProjectionDivergence = compute.FindKernel("KComputeProjectionDivergence");
            _kernelJacobiProjectionPressure = compute.FindKernel("KJacobiProjectionPressure");
            _kernelJacobiProjectionPressureSparse = compute.FindKernel("KJacobiProjectionPressureSparse");
            _kernelRedBlackSorProjectionPressure = compute.FindKernel("KRedBlackSorProjectionPressure");
            _kernelRedBlackSorProjectionPressureSparse = compute.FindKernel("KRedBlackSorProjectionPressureSparse");
            _kernelSubtractProjectionPressureGradient = compute.FindKernel("KSubtractProjectionPressureGradient");
            _kernelApplyProjectionFaceVelocitiesToGrid = compute.FindKernel("KApplyProjectionFaceVelocitiesToGrid");
            _kernelCollectProjectionDiagnostics = compute.FindKernel("KCollectProjectionDiagnostics");
            _kernelApplyMovingBucketProjectionBoundaryVelocity = compute.FindKernel("KApplyMovingBucketProjectionBoundaryVelocity");
        }

        private bool HasValidKernels(FluidSolverContext context)
        {
            bool baseKernelsValid =
                _kernelClearMpmDiagnostics >= 0 &&
                _kernelClearAdaptiveActivity >= 0 &&
                _kernelClassifyAdaptiveActivity >= 0 &&
                _kernelClearMpmTileData >= 0 &&
                _kernelMarkMpmActiveTiles >= 0 &&
                _kernelExpandMpmTileSupportMasks >= 0 &&
                _kernelBucketCollisionMarkMpmActiveTiles >= 0 &&
                _kernelBuildMpmTileOwnerOffsets >= 0 &&
                _kernelFillMpmTileOwnerParticleList >= 0 &&
                _kernelClearGridActiveTiles >= 0 &&
                _kernelGridUpdateActiveTiles >= 0 &&
                _kernelP2GHybridTiled >= 0 &&
                _kernelP2GReferenceDensityStressHybrid >= 0 &&
                _kernelG2PVelocityApic >= 0 &&
                _kernelG2PVelocityApicBucketCollision >= 0 &&
                _kernelUpdateDeformation >= 0 &&
                _kernelStepAirborneParticles >= 0;

            if (!baseKernelsValid)
                return false;

            bool projectionRequested =
                context.GpuMpmConfig.enableProjectionGridInfrastructure;

            if (projectionRequested)
            {
                bool projectionKernelsValid =
                    _kernelClearProjectionGrid >= 0 &&
                    _kernelMarkBucketProjectionSolids >= 0 &&
                    _kernelMarkProjectionFluidCells >= 0 &&
                    _kernelFinalizeProjectionGrid >= 0 &&
                    _kernelClearProjectionFluidCellList >= 0 &&
                    _kernelBuildProjectionFluidCellDispatchArgs >= 0 &&
                    _kernelBuildProjectionFaceVelocities >= 0 &&
                    _kernelApplyMovingBucketProjectionBoundaryVelocity >= 0 &&
                    _kernelComputeProjectionDivergence >= 0 &&
                    _kernelJacobiProjectionPressure >= 0 &&
                    _kernelJacobiProjectionPressureSparse >= 0 &&
                    _kernelRedBlackSorProjectionPressure >= 0 &&
                    _kernelRedBlackSorProjectionPressureSparse >= 0 &&
                    _kernelSubtractProjectionPressureGradient >= 0 &&
                    _kernelApplyProjectionFaceVelocitiesToGrid >= 0 &&
                    _kernelCollectProjectionDiagnostics >= 0;

                if (!projectionKernelsValid)
                    return false;
            }

            bool bucketRequested =
                context.GpuMpmConfig.enableGpuBucketCollision &&
                context.GpuMpmConfig.useRealBucketCollision;

            if (bucketRequested &&
                _kernelBucketCollision < 0)
                return false;

            return true;
        }

        private void AllocateGridBuffers(FluidSolverContext context)
        {
            int nodeCount = Mathf.Max(1, context.GpuMpmConfig.GridNodeCount);

            _gridAccumIntBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(int) * 4
            );

            _gridVelocityMassBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float) * 4
            );

            _projectionCellTypeBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(int)
            );

            _projectionCellMassIntBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(int)
            );

            _projectionCellDataBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float) * 4
            );

            _projectionPressureBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float)
            );

            _projectionPressureTempBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float)
            );

            _projectionDivergenceBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float)
            );

            _projectionDivergenceAfterBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float)
            );

            _projectionFaceVelocityBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(float) * 4
            );

            _projectionFluidCellIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                nodeCount,
                sizeof(uint)
            );

            _projectionFluidCellMetaBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );

            _projectionFluidCellDispatchArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments,
                3,
                sizeof(uint)
            );

            _diagnosticsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                DiagnosticsValueCount,
                sizeof(uint)
            );

            UpdateMpmTileLayout(context.GpuMpmConfig);

            _mpmTileFlagsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            _mpmActiveTileIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            _mpmTileDispatchArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                3,
                sizeof(uint)
            );

            int particleCount = Mathf.Max(
                1,
                context.GpuBufferSet != null
                    ? context.GpuBufferSet.UploadedParticleCount
                    : 1
            );

            _adaptiveParticlePriorityBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCount,
                sizeof(uint)
            );

            _adaptiveActivitySummaryBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                7,
                sizeof(uint)
            );

            _mpmTileOwnerParticleCountsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            _mpmTileOwnerParticleWriteCountsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            _mpmTileOwnerParticleOffsetsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            _mpmTileOwnerParticleIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCount,
                sizeof(uint)
            );

            _mpmTileSupportMasksBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _mpmTileCount,
                sizeof(uint)
            );

            AllocateBucketHoleBuffers(context);
        }

        private void UpdateMpmTileLayout(GpuMpmSolverConfig config)
        {
            _mpmTileSizeCells = Mathf.Clamp(config.mpmTileSizeCells, 4, 8);
            if (_mpmTileSizeCells <= 4)
            {
                _mpmTileSizeCells = 4;
                _mpmTileSizeShift = 2;
            }
            else
            {
                _mpmTileSizeCells = 8;
                _mpmTileSizeShift = 3;
            }

            Vector3Int resolution = config.gridResolution;
            _mpmTileResolution = new Vector3Int(
                Mathf.Max(1, Mathf.CeilToInt((float)resolution.x / _mpmTileSizeCells)),
                Mathf.Max(1, Mathf.CeilToInt((float)resolution.y / _mpmTileSizeCells)),
                Mathf.Max(1, Mathf.CeilToInt((float)resolution.z / _mpmTileSizeCells))
            );

            _mpmTileCount = Mathf.Max(
                1,
                _mpmTileResolution.x *
                _mpmTileResolution.y *
                _mpmTileResolution.z
            );
        }

        private void AllocateBucketHoleBuffers(FluidSolverContext context)
        {
            int maxHoles = Mathf.Max(
                1,
                context.GpuMpmConfig != null
                    ? context.GpuMpmConfig.maxGpuBucketHoles
                    : 1
            );

            _bucketHoleData0 = new Vector4[maxHoles];
            _bucketHoleData1 = new Vector4[maxHoles];
            _bucketHoleData2 = new Vector4[maxHoles];
            _bucketHoleData3 = new Vector4[maxHoles];
            _bucketHoleData4 = new Vector4[maxHoles];

            _bucketHoleData0Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxHoles,
                sizeof(float) * 4
            );

            _bucketHoleData1Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxHoles,
                sizeof(float) * 4
            );

            _bucketHoleData2Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxHoles,
                sizeof(float) * 4
            );

            _bucketHoleData3Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxHoles,
                sizeof(float) * 4
            );

            _bucketHoleData4Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxHoles,
                sizeof(float) * 4
            );

            _uploadedBucketHoleCount = 0;
        }

        private void SetCommonParameters(FluidSolverContext context, SimulationContext simulationContext, FluidSolverStepInput stepInput)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            Vector3Int res = context.GpuMpmConfig.gridResolution;

            compute.SetInt("_ParticleCount", context.GpuBufferSet.UploadedParticleCount);
            compute.SetInt("_GridNodeCount", context.GpuMpmConfig.GridNodeCount);

            compute.SetInts("_GridResolution", res.x, res.y, res.z);
            compute.SetVector("_GridOrigin", _runtimeGridOriginWorld);

            compute.SetInt(
                "_EnableMpmTileOccupancy",
                _useMpmTileOccupancyThisStep ? 1 : 0
            );

            compute.SetInt(
                "_EnableTiledMpmGridDispatch",
                _useTiledMpmGridDispatchThisStep ? 1 : 0
            );

            compute.SetInt(
                "_EnableMpmParticleTileLists",
                _rebuildMpmOwnerParticleListsThisStep ? 1 : 0
            );
            compute.SetInt(
                "_EnableTiledP2G",
                _useHybridTiledP2GThisStep ? 1 : 0
            );

            compute.SetInt(
                "_EnableSparseProjectionPressureDispatch",
                _useSparseProjectionPressureDispatchThisStep ? 1 : 0
            );
            compute.SetInt(
                "_EnableReferenceDensityEos",
                _useReferenceDensityEosThisStep ? 1 : 0
            );
            compute.SetInt(
                "_EnableGridDensityEos",
                _useGridDensityEosThisStep ? 1 : 0
            );
            compute.SetFloat(
                "_ReferenceGridRestDensity",
                Mathf.Max(
                    context.MaterialConfig.densityKgPerM3 *
                    context.ReferenceDensityScale,
                    1.0f
                )
            );
            compute.SetFloat(
                "_GridDensityEosPressureScale",
                Mathf.Max(
                    context.GpuMpmConfig.gridDensityEosPressureScale,
                    0.0f
                )
            );
            compute.SetFloat(
                "_GridDensityEosMaxVelocityCorrection",
                Mathf.Max(
                    context.GpuMpmConfig
                        .gridDensityEosMaxVelocityCorrection,
                    0.0f
                )
            );
            compute.SetInt(
                "_ReferenceClampNegativePressure",
                context.GpuMpmConfig.referenceClampNegativePressure ? 1 : 0
            );
            compute.SetFloat(
                "_ReferenceEosExponent",
                Mathf.Clamp(
                    context.GpuMpmConfig.referenceEosExponent,
                    1.0f,
                    8.0f
                )
            );

            GpuMpmSolverConfig config = context.GpuMpmConfig;
            int calmDeformationInterval = Mathf.Clamp(
                config.calmInteriorDeformationInterval,
                1,
                4
            );
            bool calmDeformationUpdate =
                _stepIndex % calmDeformationInterval == 0;

            compute.SetInt(
                "_EnableAdaptiveMultiRate",
                config.enableAdaptiveMultiRate ? 1 : 0
            );
            compute.SetInt(
                "_EnableAdaptiveTransferStencil",
                _useAdaptiveTransferStencilThisStep ? 1 : 0
            );
            compute.SetInt(
                "_EnableAdaptiveG2PTransferStencil",
                _useAdaptiveTransferStencilThisStep &&
                config.enableAdaptiveG2PTransferStencil
                    ? 1
                    : 0
            );
            compute.SetInt(
                "_AdaptiveActivitySampleStep",
                _stepIndex
            );
            compute.SetInt(
                "_CalmInteriorDeformationInterval",
                calmDeformationInterval
            );
            compute.SetInt(
                "_CalmInteriorDeformationUpdateThisStep",
                calmDeformationUpdate ? 1 : 0
            );
            compute.SetFloat(
                "_AdaptivePriorityParticleSpeed",
                Mathf.Max(0.0f, config.adaptivePriorityParticleSpeed)
            );
            compute.SetFloat(
                "_AdaptivePriorityJDeviation",
                Mathf.Clamp01(config.adaptivePriorityJDeviation)
            );
            compute.SetFloat(
                "_AdaptivePriorityBoundaryBand",
                Mathf.Max(0.0f, config.adaptivePriorityBoundaryBandMeters)
            );
            compute.SetFloat(
                "_AdaptivePriorityTopFraction",
                Mathf.Clamp01(config.adaptivePriorityTopFraction)
            );

            compute.SetInts(
                "_MpmTileResolution",
                _mpmTileResolution.x,
                _mpmTileResolution.y,
                _mpmTileResolution.z
            );

            compute.SetInt("_MpmTileCount", _mpmTileCount);
            compute.SetInt("_MpmTileSizeCells", _mpmTileSizeCells);
            compute.SetInt("_MpmTileSizeShift", _mpmTileSizeShift);
            float cellSize = context.GpuMpmConfig.cellSizeMeters;

            compute.SetFloat("_CellSize", cellSize);
            compute.SetFloat("_InvCellSize", 1.0f / Mathf.Max(cellSize, 1e-6f));

            compute.SetFloat("_Dt", stepInput.dt);

            Vector3 gravity =
                simulationContext.EnvironmentState.gravity *
                stepInput.gravityScale *
                context.GpuMpmConfig.gravityScale;

            compute.SetVector("_Gravity", gravity);

            compute.SetFloat(
                "_VelocityDampingPerSecond",
                context.GpuMpmConfig.velocityDampingPerSecond
            );

            compute.SetFloat("_MaxParticleSpeed", context.GpuMpmConfig.maxParticleSpeed);
            compute.SetInt(
                "_EnableGpuDiagnostics",
                context.GpuMpmConfig.enableGpuDiagnostics ? 1 : 0
            );
            //compute.SetFloat("_PicBlend", context.GpuMpmConfig.picBlend);

            compute.SetInt("_MassFixedScale", context.GpuMpmConfig.massFixedScale);
            compute.SetInt("_MomentumFixedScale", context.GpuMpmConfig.momentumFixedScale);

            //compute.SetInt("_EnableBoxBoundary", context.GpuMpmConfig.enableBoxBoundary ? 1 : 0);
            //compute.SetFloat("_BoundaryDamping", context.GpuMpmConfig.boundaryDamping);

            ////////// G5 Changes ////////////
            //bool enableApic = context.GpuMpmConfig.enableApicTransfer;

            //compute.SetInt("_EnableApicTransfer", enableApic ? 1 : 0);
            //compute.SetFloat("_ApicP2GStrength", context.GpuMpmConfig.apicP2GStrength);
            //compute.SetFloat("_ApicG2PStrength", context.GpuMpmConfig.apicG2PStrength);
            compute.SetFloat("_AffineDamping", context.GpuMpmConfig.affineDamping);
            compute.SetFloat("_MaxAffineMagnitude", context.GpuMpmConfig.maxAffineMagnitude);

            float dInverse = context.GpuMpmConfig.useAutomaticApicDInverse
                ? 4.0f / Mathf.Max(
                    context.GpuMpmConfig.cellSizeMeters * context.GpuMpmConfig.cellSizeMeters,
                    1e-8f
                )
                : context.GpuMpmConfig.manualApicDInverse;

            compute.SetFloat("_ApicDInverse", dInverse);
            ////////// End G5 Changes ////////////
            
            ////////// G6.A Changes ////////////
            compute.SetInt("_EnableMaterialStress", context.GpuMpmConfig.enableMaterialStress ? 1 : 0);
            compute.SetFloat("_BulkModulus", context.GpuMpmConfig.bulkModulus);
            compute.SetFloat("_MpmViscosity", context.GpuMpmConfig.mpmViscosity);
            compute.SetFloat("_MaterialStressStrength", context.GpuMpmConfig.materialStressStrength);
            compute.SetFloat("_MaxStressMagnitude", context.GpuMpmConfig.maxStressMagnitude);
            compute.SetFloat("_MinJ", context.GpuMpmConfig.minJ);
            compute.SetFloat("_MaxJ", context.GpuMpmConfig.maxJ);
            compute.SetFloat("_MaxDeformationGradientValue", context.GpuMpmConfig.maxDeformationGradientValue);
            ////////// End G6.A Changes ////////////

            ////////// G7 Paint Rheology //////////
            compute.SetInt("_EnablePaintRheology", context.GpuMpmConfig.enablePaintRheology ? 1 : 0);

            compute.SetFloat("_LowShearViscosity", context.GpuMpmConfig.lowShearViscosity);
            compute.SetFloat("_HighShearViscosity", context.GpuMpmConfig.highShearViscosity);
            compute.SetFloat("_ShearThinningRelaxationTime", context.GpuMpmConfig.shearThinningRelaxationTime);
            compute.SetFloat("_ShearThinningPowerN", context.GpuMpmConfig.shearThinningPowerN);
            compute.SetFloat("_CarreauYasudaExponent", context.GpuMpmConfig.carreauYasudaExponent);

            compute.SetFloat("_YieldStress", context.GpuMpmConfig.yieldStress);
            compute.SetFloat("_YieldRegularizationRate", context.GpuMpmConfig.yieldRegularizationRate);
            compute.SetFloat("_MaxYieldViscosityContribution", context.GpuMpmConfig.maxYieldViscosityContribution);
            compute.SetFloat("_MaxEffectiveViscosity", context.GpuMpmConfig.maxEffectiveViscosity);
            compute.SetFloat("_RheologyStrength", context.GpuMpmConfig.rheologyStrength);

            compute.SetInt(
                "_EnableFreeSurfacePolish",
                context.GpuMpmConfig.enableFreeSurfacePolish ? 1 : 0
            );
            compute.SetFloat(
                "_FreeSurfaceGradientScale",
                context.GpuMpmConfig.freeSurfaceGradientScale
            );
            compute.SetFloat(
                "_FreeSurfaceNormalDampingPerSecond",
                context.GpuMpmConfig.freeSurfaceNormalDampingPerSecond
            );
            compute.SetFloat(
                "_FreeSurfaceCohesionAcceleration",
                context.GpuMpmConfig.freeSurfaceCohesionAcceleration
            );
            compute.SetFloat(
                "_MaxFreeSurfaceVelocityCorrection",
                context.GpuMpmConfig.maxFreeSurfaceVelocityCorrection
            );
            compute.SetInt(
                "_EnableJetCohesion",
                context.GpuMpmConfig.enableJetCohesion ? 1 : 0
            );
            compute.SetFloat(
                "_JetCohesionAcceleration",
                context.GpuMpmConfig.jetCohesionAcceleration
            );
            compute.SetFloat(
                "_MaxJetCohesionVelocityCorrection",
                context.GpuMpmConfig.maxJetCohesionVelocityCorrection
            );
            ////////// End G7 Paint Rheology //////////

            ////////// G8.A  bucket collision //////////
            SetBucketCollisionParameters(context);
            ////////// End G8.A bucket collision //////////

            SetOutflowAirborneParameters(context);
            if (!_useReferenceDensityEosThisStep ||
                context.GpuMpmConfig.referenceEnableProjectionFallback)
            {
                SetProjectionParameters(context);
            }
        }

        private void SetOutflowAirborneParameters(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            compute.SetInt(
                "_EnableAirborneAdvection",
                context.GpuMpmConfig.enableAirborneParticleAdvection ? 1 : 0
            );

            compute.SetFloat(
                "_AirborneDragPerSecond",
                context.GpuMpmConfig.airborneDragPerSecond
            );

            compute.SetFloat(
                "_JetStateDuration",
                context.GpuMpmConfig.jetStateDurationSeconds
            );

            compute.SetInt(
                "_EnableJetMpmCollar",
                context.GpuMpmConfig.enableJetMpmCollar ? 1 : 0
            );
            compute.SetFloat(
                "_JetMpmCollarDuration",
                context.GpuMpmConfig.jetMpmCollarDurationSeconds
            );
            compute.SetFloat(
                "_JetMpmCollarMaxDistance",
                context.GpuMpmConfig.jetMpmCollarMaxDistanceMeters
            );
            compute.SetFloat(
                "_JetMpmCollarRadialPadding",
                context.GpuMpmConfig.jetMpmCollarRadialPaddingMeters
            );

            compute.SetFloat(
                "_AirborneLifetime",
                context.GpuMpmConfig.airborneLifetimeSeconds
            );

            compute.SetFloat(
                "_AirborneKillBelowY",
                context.GpuMpmConfig.airborneKillBelowWorldY
            );

            compute.SetInt(
                "_KillAirborneBelowY",
                context.GpuMpmConfig.killAirborneBelowWorldY ? 1 : 0
            );
        }

        private void BindClearGridActiveTiles(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearGridActiveTiles,
                "_GridAccumInt",
                _gridAccumIntBuffer
            );
            compute.SetBuffer(
                _kernelClearGridActiveTiles,
                "_GridVelocityMass",
                _gridVelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelClearGridActiveTiles,
                "_MpmActiveTileIndices",
                _mpmActiveTileIndicesBuffer
            );
        }

        private void BindClearMpmDiagnostics(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearMpmDiagnostics,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private int RunMpmTileOccupancy(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount,
            bool fusePreCollision)
        {
            if (!_useMpmTileOccupancyThisStep)
                return 0;

            BindClearMpmTileData(compute);
            compute.Dispatch(
                _kernelClearMpmTileData,
                Groups64(Mathf.Max(_mpmTileCount, 3)),
                1,
                1
            );

            int dispatches = 1;

            if (fusePreCollision)
            {
                BindBucketCollisionMarkMpmActiveTiles(
                    compute,
                    context
                );
                compute.Dispatch(
                    _kernelBucketCollisionMarkMpmActiveTiles,
                    Groups(particleCount),
                    1,
                    1
                );
            }
            else
            {
                BindMarkMpmActiveTiles(compute, context);
                compute.Dispatch(
                    _kernelMarkMpmActiveTiles,
                    Groups(particleCount),
                    1,
                    1
                );
            }
            dispatches++;

            if (!_useMpmParticleTileListsThisStep)
                return dispatches;

            BindExpandMpmTileSupportMasks(compute);
            compute.Dispatch(
                _kernelExpandMpmTileSupportMasks,
                Groups64(_mpmTileCount),
                1,
                1
            );
            dispatches++;

            if (!_rebuildMpmOwnerParticleListsThisStep)
                return dispatches;

            BindBuildMpmTileOwnerOffsets(compute);
            compute.Dispatch(
                _kernelBuildMpmTileOwnerOffsets,
                1,
                1,
                1
            );
            dispatches++;

            BindFillMpmTileOwnerParticleList(compute, context);
            compute.Dispatch(
                _kernelFillMpmTileOwnerParticleList,
                Groups(particleCount),
                1,
                1
            );
            dispatches++;

            _mpmOwnerParticleListsReady = true;
            _mpmOwnerParticleListParticleCount = particleCount;

            return dispatches;
        }

        private void BindClearMpmTileData(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileFlags",
                _mpmTileFlagsBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmActiveTileIndices",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileDispatchArgs",
                _mpmTileDispatchArgsBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileOwnerParticleCounts",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileOwnerParticleWriteCounts",
                _mpmTileOwnerParticleWriteCountsBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileOwnerParticleOffsets",
                _mpmTileOwnerParticleOffsetsBuffer
            );
            compute.SetBuffer(
                _kernelClearMpmTileData,
                "_MpmTileSupportMasks",
                _mpmTileSupportMasksBuffer
            );
        }

        private void BindExpandMpmTileSupportMasks(ComputeShader compute)
        {
            int kernel = _kernelExpandMpmTileSupportMasks;
            compute.SetBuffer(
                kernel,
                "_MpmTileFlags",
                _mpmTileFlagsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmActiveTileIndices",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileDispatchArgs",
                _mpmTileDispatchArgsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleCountsRead",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileSupportMasks",
                _mpmTileSupportMasksBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private void BindMarkMpmActiveTiles(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );
            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_MpmTileOwnerParticleCounts",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                _kernelMarkMpmActiveTiles,
                "_MpmTileSupportMasks",
                _mpmTileSupportMasksBuffer
            );
        }

        private void BindBucketCollisionMarkMpmActiveTiles(
            ComputeShader compute,
            FluidSolverContext context)
        {
            int kernel = _kernelBucketCollisionMarkMpmActiveTiles;
            BindBucketCollision(compute, context, kernel);
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleCounts",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileSupportMasks",
                _mpmTileSupportMasksBuffer
            );
        }

        private void BindBuildMpmTileOwnerOffsets(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelBuildMpmTileOwnerOffsets,
                "_MpmTileOwnerParticleCounts",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                _kernelBuildMpmTileOwnerOffsets,
                "_MpmTileOwnerParticleWriteCounts",
                _mpmTileOwnerParticleWriteCountsBuffer
            );
            compute.SetBuffer(
                _kernelBuildMpmTileOwnerOffsets,
                "_MpmTileOwnerParticleOffsets",
                _mpmTileOwnerParticleOffsetsBuffer
            );
        }

        private void BindFillMpmTileOwnerParticleList(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelFillMpmTileOwnerParticleList,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );
            compute.SetBuffer(
                _kernelFillMpmTileOwnerParticleList,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
            compute.SetBuffer(
                _kernelFillMpmTileOwnerParticleList,
                "_MpmTileOwnerParticleWriteCounts",
                _mpmTileOwnerParticleWriteCountsBuffer
            );
            compute.SetBuffer(
                _kernelFillMpmTileOwnerParticleList,
                "_MpmTileOwnerParticleOffsets",
                _mpmTileOwnerParticleOffsetsBuffer
            );
            compute.SetBuffer(
                _kernelFillMpmTileOwnerParticleList,
                "_MpmTileOwnerParticleIndices",
                _mpmTileOwnerParticleIndicesBuffer
            );
        }

        private void BindP2GHybridTiled(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;
            int kernel = _kernelP2GHybridTiled;

            compute.SetBuffer(
                kernel,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC0Read",
                buffers.AffineC0Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC1Read",
                buffers.AffineC1Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC2Read",
                buffers.AffineC2Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleVolumeJRead",
                buffers.VolumeJBuffer
            );
            compute.SetBuffer(
                kernel,
                "_GridAccumInt",
                _gridAccumIntBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmActiveTileIndicesRead",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleCountsRead",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleOffsetsRead",
                _mpmTileOwnerParticleOffsetsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleIndicesRead",
                _mpmTileOwnerParticleIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_AdaptiveParticlePriorityRead",
                _adaptiveParticlePriorityBuffer
            );
        }

        private void BindP2GReferenceDensityStressHybrid(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;
            int kernel = _kernelP2GReferenceDensityStressHybrid;

            compute.SetBuffer(
                kernel,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC0Read",
                buffers.AffineC0Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC1Read",
                buffers.AffineC1Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleAffineC2Read",
                buffers.AffineC2Buffer
            );
            compute.SetBuffer(
                kernel,
                "_ParticleVolumeJ",
                buffers.VolumeJBuffer
            );
            compute.SetBuffer(
                kernel,
                "_GridAccumInt",
                _gridAccumIntBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmActiveTileIndicesRead",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleCountsRead",
                _mpmTileOwnerParticleCountsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleOffsetsRead",
                _mpmTileOwnerParticleOffsetsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileOwnerParticleIndicesRead",
                _mpmTileOwnerParticleIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private void BindGridUpdateActiveTiles(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelGridUpdateActiveTiles,
                "_GridAccumInt",
                _gridAccumIntBuffer
            );
            compute.SetBuffer(
                _kernelGridUpdateActiveTiles,
                "_GridVelocityMass",
                _gridVelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelGridUpdateActiveTiles,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            compute.SetBuffer(
                _kernelGridUpdateActiveTiles,
                "_MpmActiveTileIndices",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                _kernelGridUpdateActiveTiles,
                "_MpmTileFlagsRead",
                _mpmTileFlagsBuffer
            );
        }

        private void BindG2PVelocityApic(
            ComputeShader compute,
            FluidSolverContext context,
            int kernel)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(kernel, "_ParticlePositionRadius", buffers.PositionRadiusBuffer);
            compute.SetBuffer(kernel, "_ParticleVelocityMass", buffers.VelocityMassBuffer);

            compute.SetBuffer(kernel, "_ParticleStateAgeId", buffers.StateAgeIdBuffer);
            compute.SetBuffer(kernel, "_GridVelocityMassRead", _gridVelocityMassBuffer);

            compute.SetBuffer(kernel, "_ParticleAffineC0", buffers.AffineC0Buffer);
            compute.SetBuffer(kernel, "_ParticleAffineC1", buffers.AffineC1Buffer);
            compute.SetBuffer(kernel, "_ParticleAffineC2", buffers.AffineC2Buffer);
            compute.SetBuffer(
                kernel,
                "_ParticleVolumeJ",
                buffers.VolumeJBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_AdaptiveParticlePriorityRead",
                _adaptiveParticlePriorityBuffer
            );
        }

        private void BindUpdateDeformation(
            ComputeShader compute,
            FluidSolverContext context,
            int kernel)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(kernel, "_ParticleStateAgeIdRead", buffers.StateAgeIdBuffer);

            compute.SetBuffer(kernel, "_ParticleAffineC0Read", buffers.AffineC0Buffer);
            compute.SetBuffer(kernel, "_ParticleAffineC1Read", buffers.AffineC1Buffer);
            compute.SetBuffer(kernel, "_ParticleAffineC2Read", buffers.AffineC2Buffer);

            compute.SetBuffer(kernel, "_ParticleVolumeJ", buffers.VolumeJBuffer);

            compute.SetBuffer(kernel, "_ParticleF0", buffers.DeformationF0Buffer);
            compute.SetBuffer(kernel, "_ParticleF1", buffers.DeformationF1Buffer);
            compute.SetBuffer(kernel, "_ParticleF2", buffers.DeformationF2Buffer);
            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            compute.SetBuffer(
                kernel,
                "_AdaptiveParticlePriorityRead",
                _adaptiveParticlePriorityBuffer
            );

        }

        private void BindStepAirborneParticles(ComputeShader compute, FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelStepAirborneParticles,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelStepAirborneParticles,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelStepAirborneParticles,
                "_ParticleStateAgeId",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelStepAirborneParticles,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
            BindBucketHoleBuffers(
                compute,
                _kernelStepAirborneParticles
            );
        }

        private int Groups(int count)
        {
            return Mathf.CeilToInt(count / 256.0f);
        }

        private int Groups64(int count)
        {
            return Mathf.CeilToInt(count / 64.0f);
        }

        private int RunAdaptiveActivitySampling(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            GpuMpmSolverConfig config = context.GpuMpmConfig;
            bool adaptiveActivityNeeded =
                !_useReferenceDensityEosThisStep ||
                _useAdaptiveTransferStencilThisStep;
            if (!config.enableAdaptiveMultiRate ||
                !adaptiveActivityNeeded ||
                _adaptiveParticlePriorityBuffer == null ||
                _adaptiveActivitySummaryBuffer == null)
            {
                return 0;
            }

            int interval = Mathf.Clamp(
                config.adaptiveActivitySampleInterval,
                1,
                32
            );

            if (_stepIndex % interval != 0)
                return 0;

            compute.SetBuffer(
                _kernelClearAdaptiveActivity,
                "_AdaptiveActivitySummary",
                _adaptiveActivitySummaryBuffer
            );
            compute.Dispatch(_kernelClearAdaptiveActivity, 1, 1, 1);

            GpuFluidBufferSet buffers = context.GpuBufferSet;
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_ParticleVolumeJRead",
                buffers.VolumeJBuffer
            );
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_AdaptiveParticlePriority",
                _adaptiveParticlePriorityBuffer
            );
            compute.SetBuffer(
                _kernelClassifyAdaptiveActivity,
                "_AdaptiveActivitySummary",
                _adaptiveActivitySummaryBuffer
            );
            compute.Dispatch(
                _kernelClassifyAdaptiveActivity,
                Groups(particleCount),
                1,
                1
            );

            if (!_adaptiveActivityReadbackPending)
            {
                _adaptiveActivityReadbackPending = true;
                _adaptiveActivityRequestedStepIndex = _stepIndex + 1;
                AsyncGPUReadback.Request(
                    _adaptiveActivitySummaryBuffer,
                    OnAdaptiveActivityReadback
                );
            }

            return 2;
        }

        private void OnAdaptiveActivityReadback(
            AsyncGPUReadbackRequest request)
        {
            _adaptiveActivityReadbackPending = false;

            if (request.hasError || !IsInitialized)
            {
                _adaptiveActivityReady = false;
                return;
            }

            var values = request.GetData<uint>();
            if (values.Length < 7)
            {
                _adaptiveActivityReady = false;
                return;
            }

            _adaptiveActiveParticleCount = (int)values[0];
            _adaptivePriorityParticleCount = (int)values[1];
            _adaptiveCalmParticleCount = (int)values[2];
            _adaptiveAirParticleCount = (int)values[3];
            _adaptiveAverageSpeed =
                _adaptiveActiveParticleCount > 0
                    ? values[4] /
                      (1000.0f * _adaptiveActiveParticleCount)
                    : 0.0f;
            _adaptiveMaximumSpeed = values[5] / 1000.0f;
            _adaptiveAverageJDeviation =
                _adaptiveActiveParticleCount > 0
                    ? values[6] /
                      (1000.0f * _adaptiveActiveParticleCount)
                    : 0.0f;
            _adaptiveActivityStepIndex =
                _adaptiveActivityRequestedStepIndex;
            _adaptiveActivityReady = true;

            _stats.gpuAdaptiveActivityReady = true;
            _stats.gpuAdaptiveActivityStepIndex =
                _adaptiveActivityStepIndex;
            _stats.gpuAdaptivePriorityParticleCount =
                _adaptivePriorityParticleCount;
            _stats.gpuAdaptiveCalmInteriorParticleCount =
                _adaptiveCalmParticleCount;
            _stats.gpuAdaptiveAirParticleCount =
                _adaptiveAirParticleCount;
            _stats.gpuAdaptivePriorityParticleFraction =
                _adaptiveActiveParticleCount > 0
                    ? (float)_adaptivePriorityParticleCount /
                      _adaptiveActiveParticleCount
                    : 0.0f;
            _stats.gpuAdaptiveAverageParticleSpeed =
                _adaptiveAverageSpeed;
            _stats.gpuAdaptiveMaximumParticleSpeed =
                _adaptiveMaximumSpeed;
            _stats.gpuAdaptiveAverageJDeviation =
                _adaptiveAverageJDeviation;
        }

        private float CompleteGpuStageProfileSegment(bool restart = true)
        {
            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    _diagnosticsBuffer,
                    sizeof(uint),
                    0
                );
            request.WaitForCompletion();

            _gpuStageProfileWatch.Stop();
            float milliseconds =
                (float)_gpuStageProfileWatch.Elapsed.TotalMilliseconds;

            if (restart)
                _gpuStageProfileWatch.Restart();

            return milliseconds;
        }

        private void DispatchParticleKernel(
            ComputeShader compute,
            int kernel,
            int particleCount)
        {
            compute.Dispatch(kernel, Groups(particleCount), 1, 1);
        }

        private bool ShouldRunAirborneAdvection(GpuMpmSolverConfig config)
        {
            if (!config.enableAirborneParticleAdvection)
                return false;

            bool canCreateAirDomainParticles =
                config.enableBottomHoleOpening ||
                config.topBoundaryMode == GpuBucketTopMode.MarkSpilled;

            if (canCreateAirDomainParticles)
                _mayHaveAirDomainParticles = true;

            if (_stats.gpuJetParticleCount > 0 ||
                _stats.gpuAirborneParticleCount > 0)
            {
                _mayHaveAirDomainParticles = true;
            }

            return !config.enableSmartAirborneDispatch ||
                   canCreateAirDomainParticles ||
                   _mayHaveAirDomainParticles;
        }

        private bool ShouldRunPostBucketCollision(GpuMpmSolverConfig config)
        {
            if (!config.enablePostG2PBucketCollision)
                return false;

            if (!config.enableAdaptivePostG2PBucketCollision)
                return true;

            return config.enableBottomHoleOpening ||
                   config.topBoundaryMode == GpuBucketTopMode.MarkSpilled;
        }

        private int ResolveAdaptiveProjectionInterval(
            FluidSolverContext context)
        {
            GpuMpmSolverConfig config = context.GpuMpmConfig;
            int activeInterval = Mathf.Max(
                1,
                config.projectionSubstepInterval
            );

            _effectiveProjectionInterval = activeInterval;

            if (!config.enableAdaptiveMultiRate ||
                !config.enableAdaptiveProjectionCadence ||
                !_adaptiveActivityReady)
            {
                _adaptiveProjectionCalmCounter = 0;
                _adaptiveProjectionCalmMode = false;
                return activeInterval;
            }

            float bucketLinearSpeed = 0.0f;
            float bucketAngularSpeed = 0.0f;
            if (context.BucketSystem != null &&
                context.BucketSystem.IsInitialized)
            {
                BucketState bucketState = context.BucketSystem.State;
                bucketLinearSpeed = math.length(bucketState.velocity);
                bucketAngularSpeed =
                    math.length(bucketState.angularVelocity);
            }

            float priorityFraction =
                _adaptiveActiveParticleCount > 0
                    ? (float)_adaptivePriorityParticleCount /
                      _adaptiveActiveParticleCount
                    : 1.0f;
            bool wasCalm = _adaptiveProjectionCalmMode;

            bool urgent =
                bucketLinearSpeed >=
                    Mathf.Max(
                        0.0f,
                        config.adaptiveProjectionBucketLinearSpeed
                    ) ||
                bucketAngularSpeed >=
                    Mathf.Max(
                        0.0f,
                        config.adaptiveProjectionBucketAngularSpeed
                    ) ||
                config.enableBottomHoleOpening ||
                _adaptiveAirParticleCount > 0;

            bool calmCandidate =
                !urgent &&
                _adaptiveAverageSpeed <=
                    Mathf.Max(
                        0.0f,
                        config.adaptiveProjectionCalmAverageSpeed
                    ) &&
                priorityFraction <=
                    Mathf.Clamp01(
                        config.adaptiveProjectionCalmPriorityFraction
                    ) &&
                _adaptiveAverageJDeviation <=
                    Mathf.Clamp01(
                        config.adaptiveProjectionMaxAverageJDeviation
                    );

            if (calmCandidate)
            {
                _adaptiveProjectionCalmCounter++;
            }
            else
            {
                _adaptiveProjectionCalmCounter = 0;
                _adaptiveProjectionCalmMode = false;
            }

            if (_adaptiveProjectionCalmCounter >=
                Mathf.Max(
                    1,
                    config.adaptiveProjectionCalmDelaySubsteps
                ))
            {
                _adaptiveProjectionCalmMode = true;
            }

            if (_adaptiveProjectionCalmMode)
            {
                _effectiveProjectionInterval = Mathf.Clamp(
                    config.calmProjectionSubstepInterval,
                    activeInterval,
                    8
                );
                _adaptiveProjectionCalmSubstepCount++;
            }

            if (!wasCalm && _adaptiveProjectionCalmMode)
                _adaptiveProjectionCalmEntryCount++;
            else if (wasCalm && !_adaptiveProjectionCalmMode)
                _adaptiveProjectionCalmExitCount++;

            return _effectiveProjectionInterval;
        }

        private bool ShouldRunProjectionThisStep(
            GpuMpmSolverConfig config,
            int effectiveInterval)
        {
            if (!config.enableAdaptiveMultiRate ||
                !config.enableAdaptiveProjectionCadence)
            {
                return
                    _stepIndex == 0 ||
                    (_stepIndex + 1) %
                    Mathf.Max(1, effectiveInterval) == 0;
            }

            if (_stepIndex <= 1)
                return true;

            int completedStep = _stepIndex + 1;
            return
                completedStep - _lastProjectionCompletedStep >=
                Mathf.Max(1, effectiveInterval);
        }

        private void UpdateMpmTileDispatchMode(FluidSolverContext context)
        {
            GpuMpmSolverConfig config = context.GpuMpmConfig;

            const bool particleListsRequested = true;

            _useMpmTileOccupancyThisStep =
                particleListsRequested &&
                _mpmTileFlagsBuffer != null &&
                _mpmActiveTileIndicesBuffer != null &&
                _mpmTileDispatchArgsBuffer != null &&
                _mpmTileCount > 0;

            _useMpmParticleTileListsThisStep =
                _useMpmTileOccupancyThisStep &&
                particleListsRequested &&
                _mpmTileOwnerParticleCountsBuffer != null &&
                _mpmTileOwnerParticleWriteCountsBuffer != null &&
                _mpmTileOwnerParticleOffsetsBuffer != null &&
                _mpmTileOwnerParticleIndicesBuffer != null &&
                _mpmTileSupportMasksBuffer != null;

            _useHybridTiledP2GThisStep =
                _useMpmParticleTileListsThisStep &&
                _kernelP2GHybridTiled >= 0;

            int currentParticleCount =
                context.GpuBufferSet.UploadedParticleCount;
            int ownerListInterval = Mathf.Clamp(
                config.ownerTileListRebuildInterval,
                1,
                2
            );
            if (config.enableBottomHoleOpening)
                ownerListInterval = 1;
            if (currentParticleCount <
                Mathf.Max(0, config.ownerTileListReuseMinParticles))
            {
                ownerListInterval = 1;
            }
            _effectiveOwnerTileListRebuildInterval = ownerListInterval;
            bool hybridOwnerListReuseAvailable =
                _useHybridTiledP2GThisStep &&
                ownerListInterval > 1 &&
                _mpmOwnerParticleListsReady &&
                _mpmOwnerParticleListParticleCount == currentParticleCount;
            bool scheduledOwnerListRebuild =
                _stepIndex % ownerListInterval == 0;

            _rebuildMpmOwnerParticleListsThisStep =
                _useMpmParticleTileListsThisStep &&
                (
                    !hybridOwnerListReuseAvailable ||
                    scheduledOwnerListRebuild
                );

            if (!_useMpmParticleTileListsThisStep)
            {
                _mpmOwnerParticleListsReady = false;
                _mpmOwnerParticleListParticleCount = 0;
            }

            _useReferenceDensityEosThisStep =
                config.enableReferenceDensityEosMode &&
                _useHybridTiledP2GThisStep &&
                _kernelP2GReferenceDensityStressHybrid >= 0;

            _useGridDensityEosThisStep =
                _useReferenceDensityEosThisStep &&
                config.enableGridDensityEos;

            _useAdaptiveTransferStencilThisStep =
                config.enableAdaptiveTransferStencil &&
                config.enableAdaptiveMultiRate &&
                currentParticleCount >=
                    Mathf.Max(0, config.adaptiveTransferMinParticles) &&
                _adaptiveParticlePriorityBuffer != null;

            _useTiledMpmGridDispatchThisStep =
                _useMpmTileOccupancyThisStep &&
                _useHybridTiledP2GThisStep;

        }

        private void DispatchMpmActiveTileKernel(
            ComputeShader compute,
            int kernel)
        {
            compute.DispatchIndirect(kernel, _mpmTileDispatchArgsBuffer, 0);
        }

        private int ProjectionGroups(int count)
        {
            return Mathf.CeilToInt(count / 8.0f);
        }

        private Vector3Int GetProjectionDispatchSize(FluidSolverContext context)
        {
            if (_useActiveProjectionBoundsThisStep)
                return _activeProjectionSize;

            return context.GpuMpmConfig.gridResolution;
        }

        private void DispatchProjectionKernel(
            ComputeShader compute,
            int kernel,
            FluidSolverContext context)
        {
            BindMpmTileProjectionDispatchResources(compute, kernel);

            if (_useMpmTileProjectionDispatchThisStep)
            {
                compute.DispatchIndirect(
                    kernel,
                    _mpmTileDispatchArgsBuffer,
                    0
                );
                return;
            }

            Vector3Int dispatchSize = GetProjectionDispatchSize(context);

            compute.Dispatch(
                kernel,
                ProjectionGroups(Mathf.Max(dispatchSize.x, 1)),
                ProjectionGroups(Mathf.Max(dispatchSize.y, 1)),
                ProjectionGroups(Mathf.Max(dispatchSize.z, 1))
            );
        }

        private int GetProjectionDispatchNodeCount(int gridNodeCount)
        {
            if (_useMpmTileProjectionDispatchThisStep)
            {
                int activeTileCount = Mathf.Max(
                    _stats.gpuMpmActiveTileCount,
                    0
                );

                return activeTileCount > 0
                    ? activeTileCount * _mpmTileSizeCells *
                      _mpmTileSizeCells * _mpmTileSizeCells
                    : Mathf.Max(_activeProjectionNodeCount, 0);
            }

            return _useActiveProjectionBoundsThisStep
                ? Mathf.Max(_activeProjectionNodeCount, 0)
                : gridNodeCount;
        }

        private void BindMpmTileProjectionDispatchResources(
            ComputeShader compute,
            int kernel)
        {
            // D3D11 requires resources referenced by a kernel to be bound even
            // when the runtime branch using them is disabled.
            if (_mpmActiveTileIndicesBuffer == null ||
                _mpmTileFlagsBuffer == null)
            {
                return;
            }

            compute.SetBuffer(
                kernel,
                "_MpmActiveTileIndicesRead",
                _mpmActiveTileIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_MpmTileFlagsRead",
                _mpmTileFlagsBuffer
            );
        }

        private void UpdateRuntimeGridPlacement(FluidSolverContext context)
        {
            GpuMpmSolverConfig config = context.GpuMpmConfig;
            Vector3 gridSize = config.GridSizeWorld;

            Vector3 bucketCenter = Vector3.zero;
            float bucketBoundingRadius = 0.0f;

            if (context.BucketSystem != null &&
                context.BucketSystem.IsInitialized &&
                context.BucketSystem.Config != null)
            {
                var bucketConfig = context.BucketSystem.Config;
                var bucketState = context.BucketSystem.State;

                bucketCenter = new Vector3(
                    bucketState.position.x,
                    bucketState.position.y,
                    bucketState.position.z
                );

                float halfHeight = 0.5f * Mathf.Max(bucketConfig.heightMeters, 0.0f);
                float maxRadius = Mathf.Max(
                    bucketConfig.topRadiusMeters,
                    bucketConfig.bottomRadiusMeters
                );

                bucketBoundingRadius =
                    Mathf.Sqrt(halfHeight * halfHeight + maxRadius * maxRadius) +
                    Mathf.Max(config.gridBucketMarginMeters, 0.0f) +
                    2.0f * config.cellSizeMeters;
            }

            _runtimeGridOriginWorld = config.followBucketWithGrid
                ? bucketCenter - 0.5f * gridSize
                : config.gridOriginWorld;

            Vector3 gridMax = _runtimeGridOriginWorld + gridSize;
            Vector3 requiredMin = bucketCenter - Vector3.one * bucketBoundingRadius;
            Vector3 requiredMax = bucketCenter + Vector3.one * bucketBoundingRadius;

            _gridContainsBucket =
                requiredMin.x >= _runtimeGridOriginWorld.x &&
                requiredMin.y >= _runtimeGridOriginWorld.y &&
                requiredMin.z >= _runtimeGridOriginWorld.z &&
                requiredMax.x <= gridMax.x &&
                requiredMax.y <= gridMax.y &&
                requiredMax.z <= gridMax.z;

            _stats.gpuRequiredGridExtent = 2.0f * bucketBoundingRadius;
        }

        private void UpdateActiveProjectionBounds(FluidSolverContext context)
        {
            GpuMpmSolverConfig config = context.GpuMpmConfig;
            Vector3Int resolution = config.gridResolution;
            int gridNodeCount = config.GridNodeCount;

            _activeProjectionMin = Vector3Int.zero;
            _activeProjectionSize = resolution;
            _activeProjectionNodeCount = gridNodeCount;
            _useActiveProjectionBoundsThisStep = false;

            if (!config.enableActiveProjectionBounds ||
                !config.enableProjectionGridInfrastructure ||
                context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized ||
                context.BucketSystem.Config == null)
            {
                return;
            }

            var bucket = context.BucketSystem;
            var bucketConfig = bucket.Config;
            var bucketState = bucket.State;

            float halfHeight = 0.5f * Mathf.Max(bucketConfig.heightMeters, 0.0f);
            float maxRadius = Mathf.Max(
                bucketConfig.topRadiusMeters,
                bucketConfig.bottomRadiusMeters
            );

            float padding =
                Mathf.Max(config.activeProjectionBoundsPaddingMeters, 0.0f) +
                Mathf.Max(config.activeProjectionBoundsPaddingCells, 0) *
                Mathf.Max(config.cellSizeMeters, 1e-6f);

            Matrix4x4 localToWorld = Matrix4x4.TRS(
                bucketState.position,
                bucketState.rotation,
                Vector3.one
            );

            Vector3 localMin = new Vector3(
                -maxRadius,
                -halfHeight,
                -maxRadius
            );
            Vector3 localMax = new Vector3(
                maxRadius,
                halfHeight,
                maxRadius
            );

            Vector3 worldMin = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            );
            Vector3 worldMax = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity
            );

            for (int z = 0; z <= 1; z++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int x = 0; x <= 1; x++)
                    {
                        Vector3 localCorner = new Vector3(
                            x == 0 ? localMin.x : localMax.x,
                            y == 0 ? localMin.y : localMax.y,
                            z == 0 ? localMin.z : localMax.z
                        );

                        Vector3 worldCorner =
                            localToWorld.MultiplyPoint3x4(localCorner);

                        worldMin = Vector3.Min(worldMin, worldCorner);
                        worldMax = Vector3.Max(worldMax, worldCorner);
                    }
                }
            }

            worldMin -= Vector3.one * padding;
            worldMax += Vector3.one * padding;

            float invCellSize = 1.0f / Mathf.Max(config.cellSizeMeters, 1e-6f);
            Vector3 minGrid = (worldMin - _runtimeGridOriginWorld) * invCellSize;
            Vector3 maxGrid = (worldMax - _runtimeGridOriginWorld) * invCellSize;

            Vector3Int minCell = new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(minGrid.x), 0, resolution.x),
                Mathf.Clamp(Mathf.FloorToInt(minGrid.y), 0, resolution.y),
                Mathf.Clamp(Mathf.FloorToInt(minGrid.z), 0, resolution.z)
            );

            Vector3Int maxCellExclusive = new Vector3Int(
                Mathf.Clamp(Mathf.CeilToInt(maxGrid.x) + 1, 0, resolution.x),
                Mathf.Clamp(Mathf.CeilToInt(maxGrid.y) + 1, 0, resolution.y),
                Mathf.Clamp(Mathf.CeilToInt(maxGrid.z) + 1, 0, resolution.z)
            );

            Vector3Int size = maxCellExclusive - minCell;

            if (size.x <= 0 ||
                size.y <= 0 ||
                size.z <= 0)
            {
                return;
            }

            long activeNodeCountLong =
                (long)size.x *
                size.y *
                size.z;

            if (activeNodeCountLong <= 0 ||
                activeNodeCountLong > int.MaxValue)
            {
                return;
            }

            int activeNodeCount = (int)activeNodeCountLong;
            float activeFraction =
                gridNodeCount > 0
                    ? (float)activeNodeCount / gridNodeCount
                    : 1.0f;

            if (activeFraction >=
                Mathf.Clamp(config.activeProjectionMaxFullGridFraction, 0.1f, 1.0f))
            {
                return;
            }

            _activeProjectionMin = minCell;
            _activeProjectionSize = size;
            _activeProjectionNodeCount = activeNodeCount;
            _useActiveProjectionBoundsThisStep = true;
        }

        private void UpdateSparseProjectionPressureDispatchMode(
            FluidSolverContext context)
        {
            _useSparseProjectionPressureDispatchThisStep =
                context.GpuMpmConfig.enableSparseProjectionPressureDispatch &&
                _projectionFluidCellIndicesBuffer != null &&
                _projectionFluidCellMetaBuffer != null &&
                _projectionFluidCellDispatchArgsBuffer != null &&
                _kernelClearProjectionFluidCellList >= 0 &&
                _kernelBuildProjectionFluidCellDispatchArgs >= 0 &&
                _kernelJacobiProjectionPressureSparse >= 0 &&
                _kernelRedBlackSorProjectionPressureSparse >= 0;
        }

        private void UpdateMpmTileProjectionDispatchMode(
            FluidSolverContext context)
        {
            _useMpmTileProjectionDispatchThisStep =
                _useMpmTileOccupancyThisStep &&
                _mpmTileSizeCells == 8 &&
                _mpmTileSizeShift == 3 &&
                _mpmTileFlagsBuffer != null &&
                _mpmActiveTileIndicesBuffer != null &&
                _mpmTileDispatchArgsBuffer != null;

            // Active tiles become the projection domain; the bucket AABB is no
            // longer needed and would incorrectly clip jets near an outlet.
            if (_useMpmTileProjectionDispatchThisStep)
                _useActiveProjectionBoundsThisStep = false;
        }

        /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// /// /// 

        private void SetBucketCollisionParameters(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            bool enabled =
                context.GpuMpmConfig.enableGpuBucketCollision &&
                context.GpuMpmConfig.useRealBucketCollision &&
                context.BucketSystem != null &&
                context.BucketSystem.IsInitialized;

            compute.SetInt("_EnableGpuBucketCollision", enabled ? 1 : 0);
            compute.SetInt("_UseRealBucketCollision", context.GpuMpmConfig.useRealBucketCollision ? 1 : 0);

            if (!enabled)
                return;

            var bucket = context.BucketSystem;
            var config = bucket.Config;
            var state = bucket.State;

            Matrix4x4 localToWorld = Matrix4x4.TRS(
                state.position,
                state.rotation,
                Vector3.one
            );

            Matrix4x4 worldToLocal = localToWorld.inverse;

            compute.SetMatrix("_BucketLocalToWorld", localToWorld);
            compute.SetMatrix("_BucketWorldToLocal", worldToLocal);

            /// /// /// /// /// /// ///  G8.B Changes /// /// /// /// /// /// /// /// /// 
            compute.SetInt(
                "_EnableMovingBucketBoundaryVelocity",
                context.GpuMpmConfig.enableMovingBucketBoundaryVelocity ? 1 : 0
            );

            compute.SetFloat(
                "_BucketBoundaryVelocityStrength",
                context.GpuMpmConfig.bucketBoundaryVelocityStrength
            );

            compute.SetFloat(
                "_MaxBucketBoundaryVelocity",
                context.GpuMpmConfig.maxBucketBoundaryVelocity
            );

            float3 bucketLinearVelocity = state.velocity;
            float3 bucketAngularVelocity = state.angularVelocity;

            compute.SetVector(
                "_BucketLinearVelocityWorld",
                new Vector4(
                    bucketLinearVelocity.x,
                    bucketLinearVelocity.y,
                    bucketLinearVelocity.z,
                    0.0f
                )
            );

            compute.SetVector(
                "_BucketAngularVelocityWorld",
                new Vector4(
                    bucketAngularVelocity.x,
                    bucketAngularVelocity.y,
                    bucketAngularVelocity.z,
                    0.0f
                )
            );
            /// /// /// /// /// /// ///  End G8.B Changes /// /// /// /// /// /// /// /// /// 

            compute.SetInt("_BucketShapeType", (int)config.shapeType);

            compute.SetFloat("_BucketHeight", config.heightMeters);
            compute.SetFloat("_BucketTopRadius", config.topRadiusMeters);
            compute.SetFloat("_BucketBottomRadius", config.bottomRadiusMeters);
            compute.SetFloat("_BucketWallThickness", config.wallThicknessMeters);

            compute.SetFloat("_BucketCollisionPadding", context.GpuMpmConfig.bucketCollisionPadding);
            compute.SetFloat("_BucketRestitution", context.GpuMpmConfig.bucketRestitution);
            compute.SetFloat("_BucketFriction", context.GpuMpmConfig.bucketFriction);

            compute.SetInt("_BucketTopMode", (int)context.GpuMpmConfig.topBoundaryMode);
            compute.SetFloat("_BucketTopPadding", context.GpuMpmConfig.topBoundaryPadding);

            int activeHoleCount = UploadBucketHoleData(context);
            bool hasHole = activeHoleCount > 0;

            compute.SetInt("_ClassifyBottomHoleRegion",
                context.GpuMpmConfig.classifyBottomHoleRegion && hasHole ? 1 : 0);

            compute.SetInt("_EnableBottomHoleOpening",
                context.GpuMpmConfig.enableBottomHoleOpening && hasHole ? 1 : 0);

            compute.SetInt("_BucketHoleCount", activeHoleCount);

            compute.SetFloat(
                "_HoleOutflowExitDistance",
                context.GpuMpmConfig.holeOutflowExitDistanceMeters
            );

            if (hasHole && config.holes != null)
            {
                BucketHoleConfig hole = null;
                for (int i = 0; i < config.holes.Length; i++)
                {
                    if (config.holes[i] != null && config.holes[i].active)
                    {
                        hole = config.holes[i];
                        break;
                    }
                }

                Vector3 holeCenter = config.GetResolvedHoleLocalCenter(hole);
                Vector3 holeNormal = config.GetResolvedHoleLocalNormal(hole).normalized;

                float holeRadius =
                    hole.radiusMeters *
                    context.GpuMpmConfig.holeRadiusMultiplier;

                compute.SetVector("_HoleLocalCenter", holeCenter);
                compute.SetVector("_HoleLocalNormal", holeNormal);
                compute.SetFloat("_HoleRadius", holeRadius);
            }
            else
            {
                compute.SetVector("_HoleLocalCenter", Vector3.zero);
                compute.SetVector("_HoleLocalNormal", Vector3.down);
                compute.SetFloat("_HoleRadius", 0.0f);
            }

            compute.SetFloat("_HoleNearHeight", context.GpuMpmConfig.holeNearHeightMeters);
            compute.SetFloat("_HoleRadialPadding", context.GpuMpmConfig.holeRadialPaddingMeters);
        }

        private int UploadBucketHoleData(FluidSolverContext context)
        {
            if (_bucketHoleData0 == null ||
                _bucketHoleData1 == null ||
                _bucketHoleData2 == null ||
                _bucketHoleData3 == null ||
                _bucketHoleData4 == null ||
                _bucketHoleData0Buffer == null ||
                context.BucketSystem == null ||
                context.BucketSystem.Config == null)
            {
                _uploadedBucketHoleCount = 0;
                return 0;
            }

            System.Array.Clear(_bucketHoleData0, 0, _bucketHoleData0.Length);
            System.Array.Clear(_bucketHoleData1, 0, _bucketHoleData1.Length);
            System.Array.Clear(_bucketHoleData2, 0, _bucketHoleData2.Length);
            System.Array.Clear(_bucketHoleData3, 0, _bucketHoleData3.Length);
            System.Array.Clear(_bucketHoleData4, 0, _bucketHoleData4.Length);

            BucketConfig bucketConfig = context.BucketSystem.Config;
            BucketHoleConfig[] holes = bucketConfig.holes;

            int maxHoles = _bucketHoleData0.Length;
            int written = 0;

            if (holes != null)
            {
                for (int i = 0; i < holes.Length && written < maxHoles; i++)
                {
                    BucketHoleConfig hole = holes[i];

                    if (hole == null || !hole.active)
                        continue;

                    Vector3 center = bucketConfig.GetResolvedHoleLocalCenter(hole);
                    Vector3 normal = bucketConfig.GetResolvedHoleLocalNormal(hole).normalized;
                    Vector3 tangent = bucketConfig.GetResolvedHoleLocalTangent(hole).normalized;
                    Vector3 bitangent = bucketConfig.GetResolvedHoleLocalBitangent(hole).normalized;
                    Vector2 halfExtents = bucketConfig.GetResolvedHoleHalfExtents(hole);

                    float radiusMultiplier = context.GpuMpmConfig.holeRadiusMultiplier;
                    float halfX = Mathf.Max(halfExtents.x * radiusMultiplier, 0.001f);
                    float halfY = Mathf.Max(halfExtents.y * radiusMultiplier, 0.001f);

                    _bucketHoleData0[written] = new Vector4(
                        center.x,
                        center.y,
                        center.z,
                        halfX
                    );

                    _bucketHoleData1[written] = new Vector4(
                        normal.x,
                        normal.y,
                        normal.z,
                        halfY
                    );

                    _bucketHoleData2[written] = new Vector4(
                        tangent.x,
                        tangent.y,
                        tangent.z,
                        1.0f
                    );

                    _bucketHoleData3[written] = new Vector4(
                        bitangent.x,
                        bitangent.y,
                        bitangent.z,
                        (int)hole.shape
                    );

                    _bucketHoleData4[written] = new Vector4(
                        Mathf.Max(hole.wallThicknessMeters, bucketConfig.wallThicknessMeters),
                        Mathf.Max(hole.edgeSoftnessMeters, 0.0f),
                        Mathf.Max(hole.flowMultiplier, 0.0f),
                        Mathf.Max(hole.exitVelocityBoostMetersPerSecond, 0.0f)
                    );

                    written++;
                }
            }

            _bucketHoleData0Buffer.SetData(_bucketHoleData0);
            _bucketHoleData1Buffer.SetData(_bucketHoleData1);
            _bucketHoleData2Buffer.SetData(_bucketHoleData2);
            _bucketHoleData3Buffer.SetData(_bucketHoleData3);
            _bucketHoleData4Buffer.SetData(_bucketHoleData4);

            _uploadedBucketHoleCount = written;
            return written;
        }

        private void BindBucketCollision(ComputeShader compute, FluidSolverContext context)
        {
            BindBucketCollision(compute, context, _kernelBucketCollision);
        }

        private void BindBucketCollision(
            ComputeShader compute,
            FluidSolverContext context,
            int kernel)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                kernel,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

        //    compute.SetBuffer(
      //          _kernelBucketCollision,
    //            "_ParticleStateAgeIdRead",
  //              buffers.StateAgeIdBuffer
//            );

            compute.SetBuffer(
                kernel,
                "_ParticleStateAgeId",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                kernel,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );

            BindBucketHoleBuffers(compute, kernel);
        }

        private void BindBucketHoleBuffers(ComputeShader compute, int kernel)
        {
            if (_bucketHoleData0Buffer == null ||
                _bucketHoleData1Buffer == null ||
                _bucketHoleData2Buffer == null ||
                _bucketHoleData3Buffer == null ||
                _bucketHoleData4Buffer == null)
            {
                return;
            }

            compute.SetBuffer(kernel, "_BucketHoleData0", _bucketHoleData0Buffer);
            compute.SetBuffer(kernel, "_BucketHoleData1", _bucketHoleData1Buffer);
            compute.SetBuffer(kernel, "_BucketHoleData2", _bucketHoleData2Buffer);
            compute.SetBuffer(kernel, "_BucketHoleData3", _bucketHoleData3Buffer);
            compute.SetBuffer(kernel, "_BucketHoleData4", _bucketHoleData4Buffer);
        }
        /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// /// /// 

        private void SetProjectionParameters(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            compute.SetInt(
                "_EnableProjectionGrid",
                context.GpuMpmConfig.enableProjectionGridInfrastructure ? 1 : 0
            );

            compute.SetInt(
                "_UsePressureWarmStart",
                context.GpuMpmConfig.enablePressureWarmStart &&
                _hasPressureHistory
                    ? 1
                    : 0
            );

            compute.SetFloat(
                "_PressureWarmStartFactor",
                context.GpuMpmConfig.pressureWarmStartFactor
            );

            compute.SetInt(
                "_ProjectionMassFixedScale",
                context.GpuMpmConfig.projectionMassFixedScale
            );

            compute.SetFloat(
                "_ProjectionMinFluidCellMass",
                context.GpuMpmConfig.minFluidCellMass
            );

            compute.SetInt(
                "_EnableBucketProjectionSolidCells",
                context.GpuMpmConfig.enableBucketProjectionSolidCells ? 1 : 0
            );

            compute.SetInt(
                "_ProjectionTopOpen",
                context.GpuMpmConfig.projectionTopOpen ? 1 : 0
            );

            compute.SetInt(
                "_EnableActiveProjectionBounds",
                _useActiveProjectionBoundsThisStep ? 1 : 0
            );

            compute.SetInt(
                "_EnableMpmTileProjectionDispatch",
                _useMpmTileProjectionDispatchThisStep ? 1 : 0
            );

            compute.SetInt(
                "_UseMpmGridMassForProjection",
                context.GpuMpmConfig.useMpmGridForProjection ? 1 : 0
            );

            compute.SetInt(
                "_EnableProjectionDensityDriftCorrection",
                context.GpuMpmConfig.enableProjectionDensityDriftCorrection
                    ? 1
                    : 0
            );
            compute.SetFloat(
                "_ProjectionDensityDriftStrength",
                context.GpuMpmConfig.projectionDensityDriftStrength
            );
            compute.SetFloat(
                "_ProjectionDensityDriftMinRatio",
                context.GpuMpmConfig.projectionDensityDriftMinRatio
            );
            compute.SetFloat(
                "_ProjectionDensityDriftMaxDivergence",
                context.GpuMpmConfig.projectionDensityDriftMaxDivergence
            );

            compute.SetInts(
                "_ActiveProjectionMin",
                _activeProjectionMin.x,
                _activeProjectionMin.y,
                _activeProjectionMin.z
            );

            compute.SetInts(
                "_ActiveProjectionSize",
                _activeProjectionSize.x,
                _activeProjectionSize.y,
                _activeProjectionSize.z
            );

            compute.SetInt(
                "_ActiveProjectionNodeCount",
                GetProjectionDispatchNodeCount(context.GpuMpmConfig.GridNodeCount)
            );

            compute.SetInt(
                "_EnableProjectionDivergence",
                context.GpuMpmConfig.enableProjectionDivergenceComputation ? 1 : 0
            );

            compute.SetFloat(
                "_ProjectionDivergenceScale",
                context.GpuMpmConfig.projectionDivergenceScale
            );

            compute.SetFloat(
                "_MaxAbsProjectionDivergence",
                context.GpuMpmConfig.maxAbsProjectionDivergence
            );

            compute.SetInt(
                "_ProjectionSolidNoFlux",
                context.GpuMpmConfig.projectionSolidNoFlux ? 1 : 0
            );

            compute.SetInt(
                "_EnableJacobiPressureSolve",
                context.GpuMpmConfig.enableJacobiPressureSolve ? 1 : 0
            );

            compute.SetFloat(
                "_ProjectionPressureRhsScale",
                context.GpuMpmConfig.pressureRhsScale
            );

            compute.SetFloat(
                "_ProjectionPressureRelaxation",
                context.GpuMpmConfig.pressureJacobiRelaxation
            );

            compute.SetFloat(
                "_ProjectionPressureSorOmega",
                context.GpuMpmConfig.pressureRedBlackSorOmega
            );

            compute.SetFloat(
                "_MaxProjectionPressure",
                context.GpuMpmConfig.maxProjectionPressure
            );

            compute.SetInt(
                "_ProjectionAirPressureZero",
                context.GpuMpmConfig.projectionAirPressureZero ? 1 : 0
            );

            compute.SetInt(
                "_ProjectionSolidPressureNeumann",
                context.GpuMpmConfig.projectionSolidPressureNeumann ? 1 : 0
            );

            compute.SetInt(
                "_EnablePressureGradientSubtraction",
                context.GpuMpmConfig.enablePressureGradientSubtraction ? 1 : 0
);

            compute.SetFloat(
                "_ProjectionPressureGradientScale",
                context.GpuMpmConfig.pressureGradientScale
            );

            compute.SetFloat(
                "_MaxPressureVelocityCorrection",
                context.GpuMpmConfig.maxPressureVelocityCorrection
            );

            compute.SetInt(
                "_InvertPressureGradientSign",
                context.GpuMpmConfig.invertPressureGradientSign ? 1 : 0
            );

            compute.SetInt(
                "_PressureCorrectionFluidCellsOnly",
                context.GpuMpmConfig.pressureCorrectionFluidCellsOnly ? 1 : 0
            );

            compute.SetInt(
                "_UseStaggeredFaceProjection",
                context.GpuMpmConfig.useStaggeredFaceProjection ? 1 : 0
            );

            compute.SetInt(
                "_EnableMovingBucketProjectionCoupling",
                context.GpuMpmConfig.enableMovingBucketProjectionCoupling ? 1 : 0
            );

            compute.SetInt(
                "_UseMovingBucketVelocityInDivergence",
                context.GpuMpmConfig.useMovingBucketVelocityInDivergence ? 1 : 0
            );

            compute.SetInt(
                "_ApplyMovingBucketGridBoundaryVelocity",
                context.GpuMpmConfig.applyMovingBucketGridBoundaryVelocity ? 1 : 0
            );

            compute.SetFloat(
                "_ProjectionMovingBoundaryVelocityStrength",
                context.GpuMpmConfig.projectionMovingBoundaryVelocityStrength
            );

            compute.SetFloat(
                "_MaxProjectionBoundaryVelocityCorrection",
                context.GpuMpmConfig.maxProjectionBoundaryVelocityCorrection
            );
        }

        private void BindClearProjectionGrid(ComputeShader compute)
        {
            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionCellType", _projectionCellTypeBuffer);
            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionCellMassInt", _projectionCellMassIntBuffer);
            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionCellData", _projectionCellDataBuffer);

            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionPressure", _projectionPressureBuffer);
            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionPressureTemp", _projectionPressureTempBuffer);
            compute.SetBuffer(_kernelClearProjectionGrid, "_ProjectionDivergence", _projectionDivergenceBuffer);
            compute.SetBuffer(
                _kernelClearProjectionGrid,
                "_ProjectionDivergenceAfter",
                _projectionDivergenceAfterBuffer
            );
            compute.SetBuffer(
                _kernelClearProjectionGrid,
                "_ProjectionFaceVelocity",
                _projectionFaceVelocityBuffer
            );
        }

        private void BindMarkBucketProjectionSolids(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelMarkBucketProjectionSolids,
                "_ProjectionCellType",
                _projectionCellTypeBuffer
            );

            BindBucketHoleBuffers(compute, _kernelMarkBucketProjectionSolids);
        }

        private void BindMarkProjectionFluidCells(
            ComputeShader compute,
            FluidSolverContext context,
            int kernel)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                kernel,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionCellType",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionCellMassInt",
                _projectionCellMassIntBuffer
            );

        }

        private void BindFinalizeProjectionGrid(ComputeShader compute)
        {
            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionCellType", _projectionCellTypeBuffer);
            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionCellMassInt", _projectionCellMassIntBuffer);
            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionCellData", _projectionCellDataBuffer);

            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionPressure", _projectionPressureBuffer);
            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionPressureTemp", _projectionPressureTempBuffer);
            compute.SetBuffer(_kernelFinalizeProjectionGrid, "_ProjectionDivergence", _projectionDivergenceBuffer);
            compute.SetBuffer(
                _kernelFinalizeProjectionGrid,
                "_GridVelocityMassRead",
                _gridVelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelFinalizeProjectionGrid,
                "_ProjectionFluidCellIndices",
                _projectionFluidCellIndicesBuffer
            );
            compute.SetBuffer(
                _kernelFinalizeProjectionGrid,
                "_ProjectionFluidCellMeta",
                _projectionFluidCellMetaBuffer
            );
        }

        private void BindClearProjectionFluidCellList(
            ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearProjectionFluidCellList,
                "_ProjectionFluidCellMeta",
                _projectionFluidCellMetaBuffer
            );
            compute.SetBuffer(
                _kernelClearProjectionFluidCellList,
                "_ProjectionFluidCellDispatchArgs",
                _projectionFluidCellDispatchArgsBuffer
            );
        }

        private void BindBuildProjectionFluidCellDispatchArgs(
            ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelBuildProjectionFluidCellDispatchArgs,
                "_ProjectionFluidCellMeta",
                _projectionFluidCellMetaBuffer
            );
            compute.SetBuffer(
                _kernelBuildProjectionFluidCellDispatchArgs,
                "_ProjectionFluidCellDispatchArgs",
                _projectionFluidCellDispatchArgsBuffer
            );
        }

        private int RunProjectionGridInfrastructure(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount,
            int gridNodeCount)
        {
            if (!context.GpuMpmConfig.enableProjectionGridInfrastructure)
                return 0;

            int projectionNodeCount = GetProjectionDispatchNodeCount(gridNodeCount);

            if (projectionNodeCount <= 0)
                return 0;

            BindClearProjectionGrid(compute);
            DispatchProjectionKernel(compute, _kernelClearProjectionGrid, context);

            if (context.GpuMpmConfig.enableBucketProjectionSolidCells)
            {
                BindMarkBucketProjectionSolids(compute);
                DispatchProjectionKernel(compute, _kernelMarkBucketProjectionSolids, context);
            }

            bool useMpmGridMass =
                context.GpuMpmConfig.useMpmGridForProjection;
            if (!useMpmGridMass)
            {
                BindMarkProjectionFluidCells(
                    compute,
                    context,
                    _kernelMarkProjectionFluidCells
                );
                DispatchParticleKernel(
                    compute,
                    _kernelMarkProjectionFluidCells,
                    particleCount
                );
            }

            int sparseListDispatches = 0;
            if (_useSparseProjectionPressureDispatchThisStep)
            {
                BindClearProjectionFluidCellList(compute);
                compute.Dispatch(
                    _kernelClearProjectionFluidCellList,
                    1,
                    1,
                    1
                );
                sparseListDispatches++;
            }

            BindFinalizeProjectionGrid(compute);
            DispatchProjectionKernel(compute, _kernelFinalizeProjectionGrid, context);

            if (_useSparseProjectionPressureDispatchThisStep)
            {
                BindBuildProjectionFluidCellDispatchArgs(compute);
                compute.Dispatch(
                    _kernelBuildProjectionFluidCellDispatchArgs,
                    1,
                    1,
                    1
                );
                sparseListDispatches++;
            }

            int dispatches =
                (context.GpuMpmConfig.enableBucketProjectionSolidCells ? 3 : 2) +
                (useMpmGridMass ? 0 : 1) +
                sparseListDispatches;

            if (context.GpuMpmConfig.enableMovingBucketProjectionCoupling &&
                context.GpuMpmConfig.applyMovingBucketGridBoundaryVelocity)
            {
                BindApplyMovingBucketProjectionBoundaryVelocity(compute);

                DispatchProjectionKernel(
                    compute,
                    _kernelApplyMovingBucketProjectionBoundaryVelocity,
                    context
                );

                dispatches++;
            }

            if (context.GpuMpmConfig.enableProjectionDivergenceComputation &&
                context.GpuMpmConfig.useStaggeredFaceProjection)
            {
                BindBuildProjectionFaceVelocities(compute);
                DispatchProjectionKernel(
                    compute,
                    _kernelBuildProjectionFaceVelocities,
                    context
                );
                dispatches++;
            }

            if (context.GpuMpmConfig.enableProjectionDivergenceComputation)
            {
                BindComputeProjectionDivergence(
                    compute,
                    _projectionDivergenceBuffer,
                    false
                );
                DispatchProjectionKernel(
                    compute,
                    _kernelComputeProjectionDivergence,
                    context
                );
                dispatches++;

                if (context.GpuMpmConfig.enableGpuDiagnostics)
                {
                    BindCollectProjectionDiagnostics(
                        compute,
                        _projectionDivergenceBuffer,
                        0
                    );
                    DispatchProjectionKernel(
                        compute,
                        _kernelCollectProjectionDiagnostics,
                        context
                    );
                    dispatches++;
                }
            }

            int pressureDispatches = RunProjectionPressureSolve(compute, context, projectionNodeCount);

            dispatches += pressureDispatches;

            int pressureGradientDispatches = RunPressureGradientSubtraction(compute, context, projectionNodeCount);

            dispatches += pressureGradientDispatches;

            if (pressureGradientDispatches > 0 &&
                context.GpuMpmConfig.enableGpuDiagnostics)
            {
                BindComputeProjectionDivergence(
                    compute,
                    _projectionDivergenceAfterBuffer,
                    true
                );
                DispatchProjectionKernel(
                    compute,
                    _kernelComputeProjectionDivergence,
                    context
                );

                BindCollectProjectionDiagnostics(
                    compute,
                    _projectionDivergenceAfterBuffer,
                    1
                );
                DispatchProjectionKernel(
                    compute,
                    _kernelCollectProjectionDiagnostics,
                    context
                );

                dispatches += 2;
            }

            return dispatches;
        }

        private void BindBuildProjectionFaceVelocities(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelBuildProjectionFaceVelocities,
                "_GridVelocityMassRead",
                _gridVelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelBuildProjectionFaceVelocities,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );
            compute.SetBuffer(
                _kernelBuildProjectionFaceVelocities,
                "_ProjectionFaceVelocity",
                _projectionFaceVelocityBuffer
            );
        }

        private void BindComputeProjectionDivergence(
            ComputeShader compute,
            GraphicsBuffer targetDivergenceBuffer,
            bool afterPressureCorrection)
        {
            compute.SetInt(
                "_ProjectionComputeAfterCorrection",
                afterPressureCorrection ? 1 : 0
            );
            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_GridVelocityMassRead",
                _gridVelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_ProjectionCellDataRead",
                _projectionCellDataBuffer
            );
            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_ProjectionFaceVelocityRead",
                _projectionFaceVelocityBuffer
            );
            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_ProjectionPressureRead",
                _projectionPressureBuffer
            );

            compute.SetBuffer(
                _kernelComputeProjectionDivergence,
                "_ProjectionDivergence",
                targetDivergenceBuffer
            );
        }

        private void BindCollectProjectionDiagnostics(
            ComputeShader compute,
            GraphicsBuffer divergenceBuffer,
            int mode)
        {
            compute.SetInt("_ProjectionDiagnosticsMode", mode);
            compute.SetBuffer(
                _kernelCollectProjectionDiagnostics,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );
            compute.SetBuffer(
                _kernelCollectProjectionDiagnostics,
                "_ProjectionDivergenceRead",
                divergenceBuffer
            );
            compute.SetBuffer(
                _kernelCollectProjectionDiagnostics,
                "_ProjectionCellDataRead",
                _projectionCellDataBuffer
            );
            compute.SetBuffer(
                _kernelCollectProjectionDiagnostics,
                "_ProjectionPressureRead",
                _projectionPressureBuffer
            );
            compute.SetBuffer(
                _kernelCollectProjectionDiagnostics,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private void BindJacobiProjectionPressure(
            ComputeShader compute,
            int kernel,
            GraphicsBuffer pressureReadBuffer,
            GraphicsBuffer pressureWriteBuffer)
        {
            compute.SetBuffer(
                kernel,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionDivergenceRead",
                _projectionDivergenceBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ProjectionCellDataRead",
                _projectionCellDataBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionPressureRead",
                pressureReadBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionPressureWrite",
                pressureWriteBuffer
            );

            BindSparseProjectionFluidCellLookup(compute, kernel);
        }

        private void BindRedBlackSorProjectionPressure(
            ComputeShader compute,
            int kernel,
            int color)
        {
            compute.SetInt("_ProjectionPressureSolveColor", color);

            compute.SetBuffer(
                kernel,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionDivergenceRead",
                _projectionDivergenceBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ProjectionCellDataRead",
                _projectionCellDataBuffer
            );

            compute.SetBuffer(
                kernel,
                "_ProjectionPressure",
                _projectionPressureBuffer
            );

            BindSparseProjectionFluidCellLookup(compute, kernel);
        }

        private void BindSparseProjectionFluidCellLookup(
            ComputeShader compute,
            int kernel)
        {
            if (!_useSparseProjectionPressureDispatchThisStep)
                return;

            compute.SetBuffer(
                kernel,
                "_ProjectionFluidCellIndicesRead",
                _projectionFluidCellIndicesBuffer
            );
            compute.SetBuffer(
                kernel,
                "_ProjectionFluidCellMetaRead",
                _projectionFluidCellMetaBuffer
            );
        }

        private void DispatchProjectionPressureKernel(
            ComputeShader compute,
            int kernel,
            FluidSolverContext context)
        {
            BindMpmTileProjectionDispatchResources(compute, kernel);

            if (_useSparseProjectionPressureDispatchThisStep)
            {
                compute.DispatchIndirect(
                    kernel,
                    _projectionFluidCellDispatchArgsBuffer,
                    0
                );
                return;
            }

            DispatchProjectionKernel(compute, kernel, context);
        }

        private void SwapProjectionPressureBuffers()
        {
            (
                _projectionPressureBuffer,
                _projectionPressureTempBuffer
            ) =
            (
                _projectionPressureTempBuffer,
                _projectionPressureBuffer
            );
        }

        private int RunProjectionPressureSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int gridNodeCount)
        {
            if (context.GpuMpmConfig.pressureSolveMode ==
                ProjectionPressureSolveMode.RedBlackSor)
            {
                return RunRedBlackSorPressureSolve(compute, context, gridNodeCount);
            }

            return RunJacobiPressureSolve(compute, context, gridNodeCount);
        }

        private int RunJacobiPressureSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int gridNodeCount)
        {
            if (!context.GpuMpmConfig.enableProjectionGridInfrastructure)
                return 0;

            if (!context.GpuMpmConfig.enableProjectionDivergenceComputation)
                return 0;

            if (!context.GpuMpmConfig.enableJacobiPressureSolve)
                return 0;

            int iterations = Mathf.Max(
                1,
                context.GpuMpmConfig.pressureJacobiIterations
            );

            for (int i = 0; i < iterations; i++)
            {
                int kernel = _useSparseProjectionPressureDispatchThisStep
                    ? _kernelJacobiProjectionPressureSparse
                    : _kernelJacobiProjectionPressure;

                BindJacobiProjectionPressure(
                    compute,
                    kernel,
                    _projectionPressureBuffer,
                    _projectionPressureTempBuffer
                );

                DispatchProjectionPressureKernel(
                    compute,
                    kernel,
                    context
                );

                SwapProjectionPressureBuffers();
            }

            return iterations;
        }

        private int RunRedBlackSorPressureSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int gridNodeCount)
        {
            if (!context.GpuMpmConfig.enableProjectionGridInfrastructure)
                return 0;

            if (!context.GpuMpmConfig.enableProjectionDivergenceComputation)
                return 0;

            if (!context.GpuMpmConfig.enableJacobiPressureSolve)
                return 0;

            int iterations = Mathf.Max(
                1,
                context.GpuMpmConfig.pressureRedBlackSorIterations
            );

            int dispatches = 0;

            for (int i = 0; i < iterations; i++)
            {
                int kernel = _useSparseProjectionPressureDispatchThisStep
                    ? _kernelRedBlackSorProjectionPressureSparse
                    : _kernelRedBlackSorProjectionPressure;

                BindRedBlackSorProjectionPressure(compute, kernel, 0);
                DispatchProjectionPressureKernel(
                    compute,
                    kernel,
                    context
                );
                dispatches++;

                BindRedBlackSorProjectionPressure(compute, kernel, 1);
                DispatchProjectionPressureKernel(
                    compute,
                    kernel,
                    context
                );
                dispatches++;
            }

            return dispatches;
        }

        private void BindSubtractProjectionPressureGradient(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelSubtractProjectionPressureGradient,
                "_GridVelocityMass",
                _gridVelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelSubtractProjectionPressureGradient,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                _kernelSubtractProjectionPressureGradient,
                "_ProjectionPressureRead",
                _projectionPressureBuffer
            );
            compute.SetBuffer(
                _kernelSubtractProjectionPressureGradient,
                "_ProjectionFaceVelocity",
                _projectionFaceVelocityBuffer
            );
        }

        private void BindApplyProjectionFaceVelocitiesToGrid(
            ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                "_GridVelocityMass",
                _gridVelocityMassBuffer
            );
            compute.SetBuffer(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );
            compute.SetBuffer(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                "_ProjectionFaceVelocityRead",
                _projectionFaceVelocityBuffer
            );
            compute.SetBuffer(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                "_ProjectionPressureRead",
                _projectionPressureBuffer
            );
            compute.SetBuffer(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                "_GridVelocityMassRead",
                _gridVelocityMassBuffer
            );
        }

        private int RunPressureGradientSubtraction(
            ComputeShader compute,
            FluidSolverContext context,
            int gridNodeCount)
        {
            if (!context.GpuMpmConfig.enableProjectionGridInfrastructure)
                return 0;

            if (!context.GpuMpmConfig.enableProjectionDivergenceComputation)
                return 0;

            if (!context.GpuMpmConfig.enableJacobiPressureSolve)
                return 0;

            if (!context.GpuMpmConfig.enablePressureGradientSubtraction)
                return 0;

            BindSubtractProjectionPressureGradient(compute);

            DispatchProjectionKernel(
                compute,
                _kernelSubtractProjectionPressureGradient,
                context
            );

            if (!context.GpuMpmConfig.useStaggeredFaceProjection)
                return 1;

            BindApplyProjectionFaceVelocitiesToGrid(compute);
            DispatchProjectionKernel(
                compute,
                _kernelApplyProjectionFaceVelocitiesToGrid,
                context
            );

            return 2;
        }

        private void BindApplyMovingBucketProjectionBoundaryVelocity(
            ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelApplyMovingBucketProjectionBoundaryVelocity,
                "_GridVelocityMass",
                _gridVelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyMovingBucketProjectionBoundaryVelocity,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );
        }

        private void RequestDiagnosticsReadbackIfDue(
            FluidSolverContext context,
            bool projectionRan)
        {
            if (!context.GpuMpmConfig.enableGpuDiagnostics ||
                _diagnosticsBuffer == null ||
                _diagnosticsReadbackPending)
            {
                return;
            }

            int interval = Mathf.Max(
                1,
                context.GpuMpmConfig.diagnosticsReadbackInterval
            );

            if (_stepIndex - _lastDiagnosticsReadbackStep < interval)
                return;

            if (context.GpuMpmConfig.enableProjectionGridInfrastructure &&
                !_useReferenceDensityEosThisStep &&
                !projectionRan)
            {
                return;
            }

            _diagnosticsReadbackPending = true;
            _diagnosticsRequestedStepIndex = _stepIndex;
            _lastDiagnosticsReadbackStep = _stepIndex;
            AsyncGPUReadback.Request(_diagnosticsBuffer, OnDiagnosticsReadback);
        }

        private void OnDiagnosticsReadback(AsyncGPUReadbackRequest request)
        {
            _diagnosticsReadbackPending = false;

            if (request.hasError || !IsInitialized)
            {
                _stats.gpuDiagnosticsReady = false;
                return;
            }

            var values = request.GetData<uint>();
            if (values.Length < DiagnosticsValueCount)
            {
                _stats.gpuDiagnosticsReady = false;
                return;
            }

            int fluidCells = (int)values[5];
            float safeFluidCells = Mathf.Max(fluidCells, 1);

            _stats.gpuDiagnosticsReady = true;
            _stats.gpuDiagnosticsStepIndex = _diagnosticsRequestedStepIndex;
            _stats.gpuActiveParticleCount = (int)values[0];
            _stats.gpuNoGridSupportParticleCount = (int)values[1];
            _stats.gpuOutOfGridParticleCount = (int)values[2];
            _stats.gpuCollisionCorrectionCount = (int)values[3];
            _stats.gpuDeformationJClampCount = (int)values[4];
            _stats.gpuNanInfCount = (int)values[14];
            float safeActiveParticles = Mathf.Max(
                _stats.gpuActiveParticleCount,
                1
            );
            _stats.gpuAverageDeformationJ =
                values[15] /
                DiagnosticsJScale /
                safeActiveParticles;
            _stats.gpuMinimumDeformationJ =
                values[16] == uint.MaxValue
                    ? 0.0f
                    : values[16] / DiagnosticsJScale;
            _stats.gpuMaximumDeformationJ =
                values[17] / DiagnosticsJScale;
            _stats.gpuAverageFillHeight01 =
                values[DiagnosticLocalYSum] /
                DiagnosticsHeightScale /
                safeActiveParticles;
            _stats.gpuMinimumFillHeight01 =
                values[DiagnosticLocalYMin] == uint.MaxValue
                    ? 0.0f
                    : values[DiagnosticLocalYMin] / DiagnosticsHeightScale;
            _stats.gpuMaximumFillHeight01 =
                values[DiagnosticLocalYMax] / DiagnosticsHeightScale;
            _stats.gpuEstimatedFillSpan01 =
                Mathf.Max(
                    0.0f,
                    _stats.gpuMaximumFillHeight01 -
                    _stats.gpuMinimumFillHeight01
                );
            _stats.gpuAverageParticleSpeed =
                values[DiagnosticSpeedSum] /
                DiagnosticsSpeedScale /
                safeActiveParticles;
            _stats.gpuMaximumParticleSpeed =
                values[DiagnosticSpeedMax] / DiagnosticsSpeedScale;
            _stats.gpuMpmActiveTileCount =
                values.Length > DiagnosticMpmActiveTiles
                    ? (int)values[DiagnosticMpmActiveTiles]
                    : 0;
            if (values.Length > DiagnosticMpmTileCount &&
                values[DiagnosticMpmTileCount] > 0)
            {
                _stats.gpuMpmTileCount =
                    (int)values[DiagnosticMpmTileCount];
            }
            _stats.gpuMpmActiveTileFraction =
                _stats.gpuMpmTileCount > 0
                    ? (float)_stats.gpuMpmActiveTileCount /
                      _stats.gpuMpmTileCount
                    : 0.0f;
            if (_stats.projectionMpmTileDispatchUsed)
            {
                _stats.projectionActiveNodeCount =
                    _stats.gpuMpmActiveTileCount *
                    _mpmTileSizeCells *
                    _mpmTileSizeCells *
                    _mpmTileSizeCells;
                _stats.projectionActiveNodeFraction =
                    _stats.gpuGridNodeCount > 0
                        ? (float)_stats.projectionActiveNodeCount /
                          _stats.gpuGridNodeCount
                        : 0.0f;
            }
            _stats.projectionFluidCellCount = fluidCells;
            _stats.projectionSparsePressureCellCount = fluidCells;
            _stats.projectionSparsePressureCellFraction =
                _stats.projectionActiveNodeCount > 0
                    ? (float)fluidCells /
                      _stats.projectionActiveNodeCount
                    : 0.0f;
            _stats.projectionSolidCellCount = (int)values[6];
            _stats.projectionAirCellCount = (int)values[7];

            _stats.projectionAverageAbsDivergenceBefore =
                values[8] /
                DiagnosticsDivergenceScale /
                safeFluidCells;
            _stats.projectionMeasuredMaxAbsDivergenceBefore =
                values[9] /
                DiagnosticsDivergenceScale;
            _stats.projectionAverageAbsDivergenceAfter =
                values[10] /
                DiagnosticsDivergenceScale /
                safeFluidCells;
            _stats.projectionMeasuredMaxAbsDivergenceAfter =
                values[11] /
                DiagnosticsDivergenceScale;
            _stats.projectionAverageAbsPressure =
                values[12] /
                DiagnosticsPressureScale /
                safeFluidCells;
            _stats.projectionMeasuredMaxAbsPressure =
                values[13] /
                DiagnosticsPressureScale;
            _stats.projectionActivePressureCellCount = fluidCells;

            _stats.gpuOutflowTransitionCount =
                (int)values[DiagnosticOutflowTransitions];
            _stats.gpuJetParticleCount =
                (int)values[DiagnosticJetParticles];
            _stats.gpuJetMpmCollarParticleCount =
                (int)values[DiagnosticJetMpmCollarParticles];
            _stats.gpuAirborneParticleCount =
                (int)values[DiagnosticAirborneParticles];
            _stats.gpuLostParticleCount =
                (int)values[DiagnosticLostParticles];
        }
    }
}
