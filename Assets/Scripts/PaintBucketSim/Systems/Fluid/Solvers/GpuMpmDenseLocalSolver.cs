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
        private GraphicsBuffer _diagnosticsBuffer;
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

        private int _kernelClearGrid         = -1;
        private int _kernelClearMpmDiagnostics = -1;
        private int _kernelP2G               = -1;
        private int _kernelGridUpdate        = -1;
        //private int _kernelG2P               = -1;
        private int _kernelG2PVelocityApic   = -1;
        private int _kernelUpdateDeformation = -1;
        private int _kernelApplyMovingBucketProjectionBoundaryVelocity = -1;

        private int _kernelClearProjectionGrid = -1;
        private int _kernelMarkBucketProjectionSolids = -1;
        private int _kernelMarkProjectionFluidCells = -1;
        private int _kernelFinalizeProjectionGrid = -1;
        private int _kernelBuildProjectionFaceVelocities = -1;
        private int _kernelComputeProjectionDivergence = -1;

        private int _kernelJacobiProjectionPressure = -1;
        private int _kernelSubtractProjectionPressureGradient = -1;
        private int _kernelApplyProjectionFaceVelocitiesToGrid = -1;
        private int _kernelCollectProjectionDiagnostics = -1;

        /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// /// /// ///
        private int _kernelBucketCollision = -1;
        /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// /// /// ///
        private int _kernelStepAirborneParticles = -1;

        private FluidSolverStats _stats;

        private readonly Stopwatch _cpuDispatchWatch = new Stopwatch();
        private Vector3 _runtimeGridOriginWorld;
        private bool _gridContainsBucket;
        private bool _gridCoverageErrorLogged;
        private bool _diagnosticsReadbackPending;
        private bool _hasPressureHistory;
        private int _diagnosticsRequestedStepIndex;
        private int _lastDiagnosticsReadbackStep;
        private int _stepIndex;

        private const int DiagnosticsValueCount = 27;
        private const float DiagnosticsDivergenceScale = 100.0f;
        private const float DiagnosticsPressureScale = 1000000.0f;
        private const float DiagnosticsJScale = 10000.0f;
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

            if (_diagnosticsBuffer != null)
            {
                _diagnosticsBuffer.Release();
                _diagnosticsBuffer = null;
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
            _diagnosticsRequestedStepIndex = 0;
            _lastDiagnosticsReadbackStep = 0;
            _stepIndex = 0;
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

            bool runBucketCollision =
                solverContext.GpuMpmConfig.enableGpuBucketCollision &&
                solverContext.GpuMpmConfig.useRealBucketCollision &&
                solverContext.BucketSystem != null &&
                solverContext.BucketSystem.IsInitialized &&
                _kernelBucketCollision >= 0;

            bool runGpuDiagnostics =
                solverContext.GpuMpmConfig.enableGpuDiagnostics;

            if (runGpuDiagnostics)
            {
                BindClearMpmDiagnostics(compute);
                compute.Dispatch(_kernelClearMpmDiagnostics, 1, 1, 1);
            }

            if (runBucketCollision)
            {
                // Pre-solve safety pass: any correction now participates in P2G
                // and therefore in the same-substep grid/projection solve.
                BindBucketCollision(compute, solverContext);
                compute.Dispatch(_kernelBucketCollision, Groups(particleCount), 1, 1);
            }

            BindClearGrid(compute);
            compute.Dispatch(_kernelClearGrid, Groups(gridNodeCount), 1, 1);

            BindP2G(compute, solverContext);
            compute.Dispatch(_kernelP2G, Groups(particleCount), 1, 1);

            BindGridUpdate(compute);
            compute.Dispatch(_kernelGridUpdate, Groups(gridNodeCount), 1, 1);

            int projectionInterval = Mathf.Max(
                1,
                solverContext.GpuMpmConfig.projectionSubstepInterval
            );
            bool runProjection =
                solverContext.GpuMpmConfig.enableProjectionGridInfrastructure &&
                (
                    _stepIndex == 0 ||
                    (_stepIndex + 1) % projectionInterval == 0
                );

            int projectionDispatches = runProjection
                ? RunProjectionGridInfrastructure(
                    compute,
                    solverContext,
                    particleCount,
                    gridNodeCount
                )
                : 0;

            if (runProjection &&
                solverContext.GpuMpmConfig.enableJacobiPressureSolve)
            {
                _hasPressureHistory = true;
            }

            //BindG2P(compute, solverContext);
            //compute.Dispatch(_kernelG2P, Groups(particleCount), 1, 1);
            BindG2PVelocityApic(compute, solverContext);
            compute.Dispatch(_kernelG2PVelocityApic, Groups(particleCount), 1, 1);

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// /// ///
            /// 
            if (runBucketCollision)
            {
                BindBucketCollision(compute, solverContext);
                compute.Dispatch(_kernelBucketCollision, Groups(particleCount), 1, 1);
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    "MLS-MPM solver is running without real bucket collision. " +
                    "This should only happen during debugging."
                );
            }
            //BindBucketCollision(compute, solverContext);
            //compute.Dispatch(_kernelBucketCollision, Groups(particleCount), 1, 1);
            /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// ///

            BindUpdateDeformation(compute, solverContext);
            compute.Dispatch(_kernelUpdateDeformation, Groups(particleCount), 1, 1);

            int airborneDispatches = 0;
            if (solverContext.GpuMpmConfig.enableAirborneParticleAdvection)
            {
                BindStepAirborneParticles(compute, solverContext);
                compute.Dispatch(_kernelStepAirborneParticles, Groups(particleCount), 1, 1);
                airborneDispatches = 1;
            }

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

            int bucketDispatches = runBucketCollision ? 2 : 0;
            int deformationDispatches = 1;

            _stats.gpuDispatchCount =
                baseDispatches +
                projectionDispatches +
                bucketDispatches +
                deformationDispatches +
                airborneDispatches;
            _stats.gpuGridResolutionX = solverContext.GpuMpmConfig.gridResolution.x;
            _stats.gpuGridResolutionY = solverContext.GpuMpmConfig.gridResolution.y;
            _stats.gpuGridResolutionZ = solverContext.GpuMpmConfig.gridResolution.z;
            _stats.gpuGridNodeCount = gridNodeCount;
            _stats.gpuCellSizeMeters = solverContext.GpuMpmConfig.cellSizeMeters;

            _stats.projectionGridEnabled = solverContext.GpuMpmConfig.enableProjectionGridInfrastructure;
            _stats.projectionRanThisSubstep = runProjection;
            _stats.projectionSubstepInterval = projectionInterval;
            _stats.pressureWarmStartEnabled =
                solverContext.GpuMpmConfig.enablePressureWarmStart;
            _stats.pressureWarmStartFactor =
                solverContext.GpuMpmConfig.pressureWarmStartFactor;
            _stats.projectionBuffersReady =
                _projectionCellTypeBuffer != null &&
                _projectionCellMassIntBuffer != null &&
                _projectionCellDataBuffer != null &&
                _projectionPressureBuffer != null &&
                _projectionPressureTempBuffer != null &&
                _projectionDivergenceBuffer != null &&
                _projectionDivergenceAfterBuffer != null &&
                _projectionFaceVelocityBuffer != null &&
                _diagnosticsBuffer != null;

            _stats.projectionGridNodeCount = gridNodeCount;
            _stats.projectionDispatchCount = projectionDispatches;
            _stats.projectionMinFluidCellMass = solverContext.GpuMpmConfig.minFluidCellMass;

            _stats.projectionDivergenceEnabled = solverContext.GpuMpmConfig.enableProjectionDivergenceComputation;

            _stats.projectionDivergenceBufferReady = _projectionDivergenceBuffer != null;

            _stats.projectionDivergenceScale = solverContext.GpuMpmConfig.projectionDivergenceScale;

            _stats.maxAbsProjectionDivergence = solverContext.GpuMpmConfig.maxAbsProjectionDivergence;

            _stats.pressureSolveEnabled = solverContext.GpuMpmConfig.enableJacobiPressureSolve;

            _stats.pressureJacobiIterations = solverContext.GpuMpmConfig.pressureJacobiIterations;

            _stats.pressureRhsScale = solverContext.GpuMpmConfig.pressureRhsScale;

            _stats.pressureJacobiRelaxation = solverContext.GpuMpmConfig.pressureJacobiRelaxation;

            _stats.maxProjectionPressure = solverContext.GpuMpmConfig.maxProjectionPressure;

            _stats.pressureGradientSubtractionEnabled = solverContext.GpuMpmConfig.enablePressureGradientSubtraction;

            _stats.pressureGradientScale = solverContext.GpuMpmConfig.pressureGradientScale;

            _stats.maxPressureVelocityCorrection = solverContext.GpuMpmConfig.maxPressureVelocityCorrection;

            _stats.invertPressureGradientSign = solverContext.GpuMpmConfig.invertPressureGradientSign;

            _stats.movingBucketProjectionCouplingEnabled =
    solverContext.GpuMpmConfig.enableMovingBucketProjectionCoupling;

            _stats.movingBucketDivergenceBoundaryEnabled =
                solverContext.GpuMpmConfig.useMovingBucketVelocityInDivergence;

            _stats.movingBucketGridBoundaryVelocityEnabled =
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

            _kernelClearGrid = compute.FindKernel("KClearGrid");
            _kernelClearMpmDiagnostics = compute.FindKernel("KClearMpmDiagnostics");
            _kernelP2G = compute.FindKernel("KP2G");
            _kernelGridUpdate = compute.FindKernel("KGridUpdate");
            _kernelG2PVelocityApic = compute.FindKernel("KG2PVelocityApic");

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// 
            _kernelBucketCollision = compute.FindKernel("KBucketCollision");
            /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// 
            _kernelStepAirborneParticles = compute.FindKernel("KStepAirborneParticles");
            
            _kernelUpdateDeformation = compute.FindKernel("KUpdateDeformation");

            _kernelClearProjectionGrid = compute.FindKernel("KClearProjectionGrid");
            _kernelMarkBucketProjectionSolids = compute.FindKernel("KMarkBucketProjectionSolids");
            _kernelMarkProjectionFluidCells = compute.FindKernel("KMarkProjectionFluidCells");
            _kernelFinalizeProjectionGrid = compute.FindKernel("KFinalizeProjectionGrid");
            _kernelBuildProjectionFaceVelocities = compute.FindKernel("KBuildProjectionFaceVelocities");
            _kernelComputeProjectionDivergence = compute.FindKernel("KComputeProjectionDivergence");
            _kernelJacobiProjectionPressure = compute.FindKernel("KJacobiProjectionPressure");
            _kernelSubtractProjectionPressureGradient = compute.FindKernel("KSubtractProjectionPressureGradient");
            _kernelApplyProjectionFaceVelocitiesToGrid = compute.FindKernel("KApplyProjectionFaceVelocitiesToGrid");
            _kernelCollectProjectionDiagnostics = compute.FindKernel("KCollectProjectionDiagnostics");
            _kernelApplyMovingBucketProjectionBoundaryVelocity = compute.FindKernel("KApplyMovingBucketProjectionBoundaryVelocity");
        }

        private bool HasValidKernels(FluidSolverContext context)
        {
            bool baseKernelsValid =
                _kernelClearGrid >= 0 &&
                _kernelClearMpmDiagnostics >= 0 &&
                _kernelP2G >= 0 &&
                _kernelGridUpdate >= 0 &&
                _kernelG2PVelocityApic >= 0 &&
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
                    _kernelBuildProjectionFaceVelocities >= 0 &&
                    _kernelApplyMovingBucketProjectionBoundaryVelocity >= 0 &&
                    _kernelComputeProjectionDivergence >= 0 &&
                    _kernelJacobiProjectionPressure >= 0 &&
                    _kernelSubtractProjectionPressureGradient >= 0 &&
                    _kernelApplyProjectionFaceVelocitiesToGrid >= 0 &&
                    _kernelCollectProjectionDiagnostics >= 0;

                if (!projectionKernelsValid)
                    return false;
            }

            bool bucketRequested =
                context.GpuMpmConfig.enableGpuBucketCollision &&
                context.GpuMpmConfig.useRealBucketCollision;

            if (bucketRequested && _kernelBucketCollision < 0)
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

            _diagnosticsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                DiagnosticsValueCount,
                sizeof(uint)
            );

            AllocateBucketHoleBuffers(context);
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

            compute.SetFloat("_YieldStress", context.GpuMpmConfig.yieldStress);
            compute.SetFloat("_MaxYieldViscosityContribution", context.GpuMpmConfig.maxYieldViscosityContribution);
            compute.SetFloat("_MaxEffectiveViscosity", context.GpuMpmConfig.maxEffectiveViscosity);
            compute.SetFloat("_RheologyStrength", context.GpuMpmConfig.rheologyStrength);
            ////////// End G7 Paint Rheology //////////

            ////////// G8.A  bucket collision //////////
            SetBucketCollisionParameters(context);
            ////////// End G8.A bucket collision //////////

            SetOutflowAirborneParameters(context);
            SetProjectionParameters(context);
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

        private void BindClearGrid(ComputeShader compute)
        {
            compute.SetBuffer(_kernelClearGrid, "_GridAccumInt", _gridAccumIntBuffer);
            compute.SetBuffer(_kernelClearGrid, "_GridVelocityMass", _gridVelocityMassBuffer);
        }

        private void BindClearMpmDiagnostics(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearMpmDiagnostics,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private void BindP2G(ComputeShader compute, FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(_kernelP2G, "_ParticlePositionRadius", buffers.PositionRadiusBuffer);
            compute.SetBuffer(_kernelP2G, "_ParticleVelocityMass", buffers.VelocityMassBuffer);
            compute.SetBuffer(_kernelP2G, "_ParticleStateAgeIdRead", buffers.StateAgeIdBuffer);

            ////////// G5 Changes ////////////
            compute.SetBuffer(_kernelP2G, "_ParticleAffineC0", buffers.AffineC0Buffer);
            compute.SetBuffer(_kernelP2G, "_ParticleAffineC1", buffers.AffineC1Buffer);
            compute.SetBuffer(_kernelP2G, "_ParticleAffineC2", buffers.AffineC2Buffer);
            ////////// End G5 Changes ////////////

            ////////// G6.A Changes ////////////
            compute.SetBuffer(_kernelP2G, "_ParticleVolumeJRead", buffers.VolumeJBuffer);
            //compute.SetBuffer(_kernelP2G, "_ParticleF0", buffers.DeformationF0Buffer);
            //compute.SetBuffer(_kernelP2G, "_ParticleF1", buffers.DeformationF1Buffer);
            //compute.SetBuffer(_kernelP2G, "_ParticleF2", buffers.DeformationF2Buffer);
            ////////// End G6.A Changes ////////////

            compute.SetBuffer(_kernelP2G, "_GridAccumInt", _gridAccumIntBuffer);
            compute.SetBuffer(_kernelP2G, "_MpmDiagnostics", _diagnosticsBuffer);
        }

        private void BindGridUpdate(ComputeShader compute)
        {
            compute.SetBuffer(_kernelGridUpdate, "_GridAccumInt", _gridAccumIntBuffer);
            compute.SetBuffer(_kernelGridUpdate, "_GridVelocityMass", _gridVelocityMassBuffer);
            compute.SetBuffer(_kernelGridUpdate, "_MpmDiagnostics", _diagnosticsBuffer);
        }

        private void BindG2PVelocityApic(ComputeShader compute, FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticlePositionRadius", buffers.PositionRadiusBuffer);
            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticleVelocityMass", buffers.VelocityMassBuffer);

            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticleStateAgeIdRead", buffers.StateAgeIdBuffer);
            compute.SetBuffer(_kernelG2PVelocityApic, "_GridVelocityMassRead", _gridVelocityMassBuffer);

            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticleAffineC0", buffers.AffineC0Buffer);
            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticleAffineC1", buffers.AffineC1Buffer);
            compute.SetBuffer(_kernelG2PVelocityApic, "_ParticleAffineC2", buffers.AffineC2Buffer);
            compute.SetBuffer(
                _kernelG2PVelocityApic,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );
        }

        private void BindUpdateDeformation(ComputeShader compute, FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleStateAgeIdRead", buffers.StateAgeIdBuffer);

            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleAffineC0Read", buffers.AffineC0Buffer);
            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleAffineC1Read", buffers.AffineC1Buffer);
            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleAffineC2Read", buffers.AffineC2Buffer);

            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleVolumeJ", buffers.VolumeJBuffer);

            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleF0", buffers.DeformationF0Buffer);
            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleF1", buffers.DeformationF1Buffer);
            compute.SetBuffer(_kernelUpdateDeformation, "_ParticleF2", buffers.DeformationF2Buffer);
            compute.SetBuffer(
                _kernelUpdateDeformation,
                "_MpmDiagnostics",
                _diagnosticsBuffer
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
        }

        private int Groups(int count)
        {
            return Mathf.CeilToInt(count / 256.0f);
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
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelBucketCollision,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelBucketCollision,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

        //    compute.SetBuffer(
      //          _kernelBucketCollision,
    //            "_ParticleStateAgeIdRead",
  //              buffers.StateAgeIdBuffer
//            );

            compute.SetBuffer(
                _kernelBucketCollision,
                "_ParticleStateAgeId",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelBucketCollision,
                "_MpmDiagnostics",
                _diagnosticsBuffer
            );

            BindBucketHoleBuffers(compute, _kernelBucketCollision);
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

        private void BindMarkProjectionFluidCells(ComputeShader compute, FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelMarkProjectionFluidCells,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelMarkProjectionFluidCells,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelMarkProjectionFluidCells,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelMarkProjectionFluidCells,
                "_ProjectionCellType",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                _kernelMarkProjectionFluidCells,
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
        }

        private int RunProjectionGridInfrastructure(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount,
            int gridNodeCount)
        {
            if (!context.GpuMpmConfig.enableProjectionGridInfrastructure)
                return 0;

            BindClearProjectionGrid(compute);
            compute.Dispatch(_kernelClearProjectionGrid, Groups(gridNodeCount), 1, 1);

            if (context.GpuMpmConfig.enableBucketProjectionSolidCells)
            {
                BindMarkBucketProjectionSolids(compute);
                compute.Dispatch(_kernelMarkBucketProjectionSolids, Groups(gridNodeCount), 1, 1);
            }

            BindMarkProjectionFluidCells(compute, context);
            compute.Dispatch(_kernelMarkProjectionFluidCells, Groups(particleCount), 1, 1);

            BindFinalizeProjectionGrid(compute);
            compute.Dispatch(_kernelFinalizeProjectionGrid, Groups(gridNodeCount), 1, 1);

            int dispatches = context.GpuMpmConfig.enableBucketProjectionSolidCells ? 4 : 3;

            if (context.GpuMpmConfig.enableMovingBucketProjectionCoupling &&
                context.GpuMpmConfig.applyMovingBucketGridBoundaryVelocity)
            {
                BindApplyMovingBucketProjectionBoundaryVelocity(compute);

                compute.Dispatch(
                    _kernelApplyMovingBucketProjectionBoundaryVelocity,
                    Groups(gridNodeCount),
                    1,
                    1
                );

                dispatches++;
            }

            if (context.GpuMpmConfig.enableProjectionDivergenceComputation &&
                context.GpuMpmConfig.useStaggeredFaceProjection)
            {
                BindBuildProjectionFaceVelocities(compute);
                compute.Dispatch(
                    _kernelBuildProjectionFaceVelocities,
                    Groups(gridNodeCount),
                    1,
                    1
                );
                dispatches++;
            }

            if (context.GpuMpmConfig.enableProjectionDivergenceComputation)
            {
                BindComputeProjectionDivergence(
                    compute,
                    _projectionDivergenceBuffer
                );
                compute.Dispatch(_kernelComputeProjectionDivergence, Groups(gridNodeCount), 1, 1);
                dispatches++;

                if (context.GpuMpmConfig.enableGpuDiagnostics)
                {
                    BindCollectProjectionDiagnostics(
                        compute,
                        _projectionDivergenceBuffer,
                        0
                    );
                    compute.Dispatch(
                        _kernelCollectProjectionDiagnostics,
                        Groups(gridNodeCount),
                        1,
                        1
                    );
                    dispatches++;
                }
            }

            int pressureDispatches = RunJacobiPressureSolve(compute, context, gridNodeCount);

            dispatches += pressureDispatches;

            int pressureGradientDispatches = RunPressureGradientSubtraction(compute, context, gridNodeCount);

            dispatches += pressureGradientDispatches;

            if (pressureGradientDispatches > 0 &&
                context.GpuMpmConfig.enableGpuDiagnostics)
            {
                BindComputeProjectionDivergence(
                    compute,
                    _projectionDivergenceAfterBuffer
                );
                compute.Dispatch(
                    _kernelComputeProjectionDivergence,
                    Groups(gridNodeCount),
                    1,
                    1
                );

                BindCollectProjectionDiagnostics(
                    compute,
                    _projectionDivergenceAfterBuffer,
                    1
                );
                compute.Dispatch(
                    _kernelCollectProjectionDiagnostics,
                    Groups(gridNodeCount),
                    1,
                    1
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
            GraphicsBuffer targetDivergenceBuffer)
        {
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
            GraphicsBuffer pressureReadBuffer,
            GraphicsBuffer pressureWriteBuffer)
        {
            compute.SetBuffer(
                _kernelJacobiProjectionPressure,
                "_ProjectionCellTypeRead",
                _projectionCellTypeBuffer
            );

            compute.SetBuffer(
                _kernelJacobiProjectionPressure,
                "_ProjectionDivergenceRead",
                _projectionDivergenceBuffer
            );

            compute.SetBuffer(
                _kernelJacobiProjectionPressure,
                "_ProjectionPressureRead",
                pressureReadBuffer
            );

            compute.SetBuffer(
                _kernelJacobiProjectionPressure,
                "_ProjectionPressureWrite",
                pressureWriteBuffer
            );
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
                BindJacobiProjectionPressure(
                    compute,
                    _projectionPressureBuffer,
                    _projectionPressureTempBuffer
                );

                compute.Dispatch(
                    _kernelJacobiProjectionPressure,
                    Groups(gridNodeCount),
                    1,
                    1
                );

                SwapProjectionPressureBuffers();
            }

            return iterations;
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

            compute.Dispatch(
                _kernelSubtractProjectionPressureGradient,
                Groups(gridNodeCount),
                1,
                1
            );

            if (!context.GpuMpmConfig.useStaggeredFaceProjection)
                return 1;

            BindApplyProjectionFaceVelocitiesToGrid(compute);
            compute.Dispatch(
                _kernelApplyProjectionFaceVelocitiesToGrid,
                Groups(gridNodeCount),
                1,
                1
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

            _stats.projectionFluidCellCount = fluidCells;
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
            _stats.gpuAirborneParticleCount =
                (int)values[DiagnosticAirborneParticles];
            _stats.gpuLostParticleCount =
                (int)values[DiagnosticLostParticles];
        }
    }
}
