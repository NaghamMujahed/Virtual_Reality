using System.Diagnostics;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;

namespace PaintBucketSim.Systems.Fluid.Solvers
{
    public sealed class GpuDfsphPaintSolver : IFluidSolver
    {
        private FluidSolverStats _stats;
        private readonly Stopwatch _cpuDispatchWatch = new Stopwatch();
        private readonly GpuDfsphBufferSet _dfsphBuffers = new GpuDfsphBufferSet();

        private int _kernelApplyExternalForces = -1;
        private int _kernelAdvectParticles = -1;

        private int _kernelClearDfsphScalarData = -1;
        private int _kernelClearDfsphTempData = -1;

        private int _kernelClearSpatialGrid = -1;
        private int _kernelBuildSpatialGrid = -1;
        private int _kernelComputeNeighborCountDebug = -1;

        private int _kernelComputeDensity = -1;

        private int _kernelComputeAlpha = -1;

        private int _kernelComputeDivergencePressure = -1;
        private int _kernelApplyDivergenceVelocityCorrection = -1;
        private int _kernelCopyVelocityTempToMain = -1;

        private int _kernelComputeDensityPressure = -1;
        private int _kernelApplyDensityVelocityCorrection = -1;

        private int _kernelApplyBucketSdfBoundary = -1;

        private int _kernelApplyBucketVelocityPreSolve = -1;

        private int _kernelApplyPaintViscosityCohesion = -1;
        private int _kernelApplyWallAdhesionDamping = -1;

        private int _kernelComputeBoundaryFactor = -1;
        private int _kernelApplyBoundaryPressurePush = -1;

        private readonly int[] _spatialGridStatsCpu = new int[4];
        private int _gridStatsFrameCounter;


        private int _kernelApplyBucketFrameTransport = -1;

        private int _kernelResolveSdfBoundaryPreSolve = -1;

        private int _kernelResolveBoundaryParticleCollisionPreSolve = -1;

        private bool _hasPreviousBucketTransform;
        private Matrix4x4 _previousBucketLocalToWorld = Matrix4x4.identity;
        private Matrix4x4 _previousBucketWorldToLocal = Matrix4x4.identity;

        private Matrix4x4 _currentBucketLocalToWorld = Matrix4x4.identity;
        private Matrix4x4 _currentBucketWorldToLocal = Matrix4x4.identity;


        private readonly GpuBoundaryParticleBufferSet _boundaryBuffers = new GpuBoundaryParticleBufferSet();

        private Vector4[] _boundaryPositionRadiusCpu;
        private Vector4[] _boundaryNormalTypeCpu;
        private Vector4[] _boundaryVelocityPsiCpu;

        private readonly int[] _boundaryGridStatsCpu = new int[4];
        private int _boundaryGridStatsFrameCounter;

        private int _boundaryParticleCount;

        private int _kernelClearBoundarySpatialGrid = -1;
        private int _kernelBuildBoundarySpatialGrid = -1;


        public FluidSolverType SolverType => FluidSolverType.GpuDfsphPaint;
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

            if (!HasValidKernels())
            {
                _stats.status = FluidSolverStatus.Error;
                IsInitialized = false;
                UnityEngine.Debug.LogError("GpuDfsphPaintSolver: Missing compute kernels.");
                return;
            }

            GpuFluidBufferSet gpuBuffers = context.GpuBufferSet;

            gpuBuffers.SetExternalGpuSimulationMode(false);
            gpuBuffers.EnsureBuffersPublic();
            gpuBuffers.UploadFromCpuParticlesNow();
            UnityEngine.Debug.Log(
                $"GPU Upload Check: uploaded={gpuBuffers.UploadedParticleCount}, " +
                $"gpuMaxParticles={context.GpuDfsphConfig.maxParticles}, " +
                $"gpuBufferCapacity={gpuBuffers.Capacity}"
            );
            gpuBuffers.SetExternalGpuSimulationMode(true);

            int requiredParticleCapacity = Mathf.Max(   
                context.GpuDfsphConfig.maxParticles,
                gpuBuffers.UploadedParticleCount
            );

            int gridCellCount = context.GpuDfsphConfig.GridCellCount;

            _dfsphBuffers.EnsureCapacity(requiredParticleCapacity, gridCellCount, context.GpuDfsphConfig.maxParticlesPerCell);

            if (context.GpuDfsphConfig.enableGpuBoundaryParticles)
            {
                _boundaryBuffers.EnsureCapacity(
                    context.GpuDfsphConfig.maxBoundaryParticles,
                    context.GpuDfsphConfig.GridCellCount,
                    context.GpuDfsphConfig.maxBoundaryParticlesPerCell
                );

                EnsureBoundaryCpuArrays(
                    context.GpuDfsphConfig.maxBoundaryParticles
                );
            }

            ClearDfsphParticleData(context);

            _stats = new FluidSolverStats
            {
                solverType = FluidSolverType.GpuDfsphPaint,
                status = FluidSolverStatus.Running,
                particleCount = gpuBuffers.UploadedParticleCount,
                solverIterations = 1
            };

            IsInitialized = true;

            if (context.GpuDfsphConfig.logLifecycle)
            {
                UnityEngine.Debug.Log(
                    $"GpuDfsphPaintSolver initialized. Particles={gpuBuffers.UploadedParticleCount}"
                );
            }

            _hasPreviousBucketTransform = false;
            _previousBucketLocalToWorld = Matrix4x4.identity;
            _previousBucketWorldToLocal = Matrix4x4.identity;
            _currentBucketLocalToWorld = Matrix4x4.identity;
            _currentBucketWorldToLocal = Matrix4x4.identity;
        }

        public void Reset(FluidSolverContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            _dfsphBuffers.Dispose();

            _boundaryBuffers.Dispose();
            _boundaryPositionRadiusCpu = null;
            _boundaryNormalTypeCpu = null;
            _boundaryVelocityPsiCpu = null;
            _boundaryParticleCount = 0;

            IsInitialized = false;
        }

        public void Step(
            FluidSolverContext solverContext,
            SimulationContext simulationContext,
            FluidSolverStepInput stepInput)
        {
            if (!IsInitialized ||
                solverContext == null ||
                solverContext.GpuDfsphConfig == null ||
                solverContext.GpuBufferSet == null ||
                !solverContext.GpuBufferSet.IsInitialized)
            {
                return;
            }

            if (!solverContext.GpuDfsphConfig.enableGpuDfsph)
                return;

            int uploadedParticleCount = solverContext.GpuBufferSet.UploadedParticleCount;
            int maxParticles = solverContext.GpuDfsphConfig.maxParticles;

            if (uploadedParticleCount > maxParticles)
            {
                UnityEngine.Debug.LogError(
                    $"GPU DFSPH particle count mismatch. Uploaded={uploadedParticleCount}, " +
                    $"MaxParticles={maxParticles}. Do not silently clamp because this keeps only bottom layers. " +
                    $"Increase maxParticles or reduce PaintFluidConfig targetParticleCount/spacing."
                );

                return;
            }

            int particleCount = uploadedParticleCount;

            if (particleCount <= 0)
                return;

            _cpuDispatchWatch.Restart();

            ComputeShader compute = solverContext.GpuDfsphConfig.dfsphCompute;

            int uploadedBoundaryCount = UploadBoundaryParticlesToGpu(solverContext);

            SetCommonParameters(
                compute,
                solverContext,
                simulationContext,
                stepInput,
                particleCount
            );

            compute.SetInt(
                "_BoundaryParticleCount",
                uploadedBoundaryCount
            );

            int boundaryGridDispatches =
                RunBoundaryNeighborGrid(
                    compute,
                    solverContext
                );

            BindApplyExternalForces(compute, solverContext);
            compute.Dispatch(
                _kernelApplyExternalForces,
                Groups(particleCount),
                1,
                1
            );

            int bucketVelocityPreSolveDispatches =
                RunBucketVelocityPreSolve(
                    compute,
                    solverContext,
                    particleCount
                );

            // مهم:
            // إذا BucketFrameTransport مفعّل، يجب أن يأتي هنا قبل fluid grid.
            // لكن الأفضل حاليًا تعطيله.
            int bucketFrameTransportDispatches =
                RunBucketFrameTransport(
                    compute,
                    solverContext,
                    particleCount
                );

            int sdfPreSolveDispatches =
                RunSdfBoundaryPreSolve(
                    compute,
                    solverContext,
                    particleCount
                );

            int boundaryParticleCollisionDispatches =
                RunBoundaryParticleCollisionPreSolve(
                    compute,
                    solverContext,
                    particleCount
                );

            int neighborDispatches =
                RunNeighborSearch(
                    compute,
                    solverContext,
                    particleCount
                );

            int boundaryFactorDispatches = RunBoundaryFactorComputation(compute, solverContext, particleCount);

            _gridStatsFrameCounter++;

            if (_gridStatsFrameCounter % 60 == 0 &&
                _dfsphBuffers.SpatialGridStatsBuffer != null)
            {
                _dfsphBuffers.SpatialGridStatsBuffer.GetData(_spatialGridStatsCpu);

                UnityEngine.Debug.Log(
                    $"DFSPH Grid Stats: " +
                    $"inserted={_spatialGridStatsCpu[0]}, " +
                    $"overflow={_spatialGridStatsCpu[1]}, " +
                    $"outOfGrid={_spatialGridStatsCpu[2]}, " +
                    $"maxCellCount={_spatialGridStatsCpu[3]}, " +
                    $"particleCount={particleCount}"
                );
            }

            int densityDispatches = RunDensityComputation(compute, solverContext, particleCount);
            int alphaDispatches = RunAlphaComputation(compute, solverContext, particleCount);
            int divergenceDispatches = RunDivergenceSolve(compute, solverContext, particleCount);
            int densitySolveDispatches = RunDensitySolve(compute, solverContext, particleCount);
            int boundaryPressurePushDispatches = RunBoundaryPressurePush(compute, solverContext, particleCount);
            int paintMaterialDispatches = RunPaintMaterialModel(compute, solverContext, particleCount);

            int advectionDispatches = 0;

            if (solverContext.GpuDfsphConfig.enableParticleAdvection)
            {
                BindAdvectParticles(compute, solverContext);

                compute.Dispatch(_kernelAdvectParticles, Groups(particleCount), 1, 1);

                advectionDispatches = 1;
            }

            int bucketBoundaryDispatches = RunBucketSdfBoundary(compute, solverContext, particleCount);

            //solverContext.GpuBufferSet.SetUploadedParticleCount(particleCount);

            _cpuDispatchWatch.Stop();

            _stats.status = FluidSolverStatus.Running;
            _stats.particleCount = particleCount;
            _stats.lastStepMilliseconds = (float)_cpuDispatchWatch.Elapsed.TotalMilliseconds;

            _stats.gpuDispatchCount =
                1 +
                bucketFrameTransportDispatches +
                bucketVelocityPreSolveDispatches +
                neighborDispatches +
                boundaryFactorDispatches +
                densityDispatches +
                alphaDispatches +
                divergenceDispatches +
                densitySolveDispatches +
                boundaryPressurePushDispatches +
                paintMaterialDispatches +
                advectionDispatches +
                sdfPreSolveDispatches +
                boundaryGridDispatches +
                bucketBoundaryDispatches;

            _stats.dfsphBuffersReady = _dfsphBuffers.IsInitialized;
            _stats.dfsphBufferCapacity = _dfsphBuffers.Capacity;

            _stats.dfsphRestDensity = solverContext.GpuDfsphConfig.restDensity;
            _stats.dfsphParticleRadius = solverContext.GpuDfsphConfig.particleRadius;
            _stats.dfsphSupportRadius = solverContext.GpuDfsphConfig.supportRadius;

            _stats.dfsphDensityIterations = solverContext.GpuDfsphConfig.densitySolverIterations;

            _stats.dfsphDivergenceIterations = solverContext.GpuDfsphConfig.divergenceSolverIterations;

            _stats.dfsphNeighborSearchEnabled = solverContext.GpuDfsphConfig.enableNeighborSearch;

            _stats.dfsphGridResolutionX = solverContext.GpuDfsphConfig.gridResolution.x;

            _stats.dfsphGridResolutionY = solverContext.GpuDfsphConfig.gridResolution.y;

            _stats.dfsphGridResolutionZ = solverContext.GpuDfsphConfig.gridResolution.z;

            _stats.dfsphGridCellCount = solverContext.GpuDfsphConfig.GridCellCount;

            _stats.dfsphGridCellSize = solverContext.GpuDfsphConfig.gridCellSize;

            _stats.dfsphMaxParticlesPerCell = solverContext.GpuDfsphConfig.maxParticlesPerCell;

            _stats.dfsphNeighborCountDebugEnabled = solverContext.GpuDfsphConfig.enableNeighborCountDebug;

            _stats.dfsphDensityComputationEnabled = solverContext.GpuDfsphConfig.enableDensityComputation;

            _stats.dfsphMinDensityClamp = solverContext.GpuDfsphConfig.minDensityClamp;

            _stats.dfsphMaxDensityClamp = solverContext.GpuDfsphConfig.maxDensityClamp;

            _stats.dfsphDensityKernelWritesNeighborCount = solverContext.GpuDfsphConfig.densityKernelWritesNeighborCount;

            _stats.dfsphAlphaComputationEnabled = solverContext.GpuDfsphConfig.enableAlphaComputation;

            _stats.dfsphAlphaDenominatorEpsilon = solverContext.GpuDfsphConfig.alphaDenominatorEpsilon;

            _stats.dfsphMaxAlpha = solverContext.GpuDfsphConfig.maxAlpha;

            _stats.dfsphMinNeighborsForFullAlpha = solverContext.GpuDfsphConfig.minNeighborsForFullAlpha;

            _stats.dfsphLowNeighborAlphaDamping = solverContext.GpuDfsphConfig.lowNeighborAlphaDamping;

            _stats.dfsphDivergenceSolveEnabled = solverContext.GpuDfsphConfig.enableDivergenceSolve;

            _stats.dfsphDivergenceErrorScale = solverContext.GpuDfsphConfig.divergenceErrorScale;

            _stats.dfsphDivergenceCorrectionStrength = solverContext.GpuDfsphConfig.divergenceCorrectionStrength;

            _stats.dfsphMaxDivergencePressure = solverContext.GpuDfsphConfig.maxDivergencePressure;

            _stats.dfsphMaxDivergenceVelocityCorrection = solverContext.GpuDfsphConfig.maxDivergenceVelocityCorrection;

            _stats.dfsphClampDivergenceToCompressionOnly = solverContext.GpuDfsphConfig.clampDivergenceToCompressionOnly;

            _stats.dfsphInvertDivergenceErrorSign = solverContext.GpuDfsphConfig.invertDivergenceErrorSign;

            _stats.dfsphParticleAdvectionEnabled = solverContext.GpuDfsphConfig.enableParticleAdvection;


            _stats.dfsphBucketBoundaryEnabled = solverContext.GpuDfsphConfig.enableBucketSdfBoundary;

            _stats.dfsphBucketBoundaryAfterAdvection = solverContext.GpuDfsphConfig.applyBucketBoundaryAfterAdvection;

            _stats.dfsphBucketBoundaryPasses = solverContext.GpuDfsphConfig.bucketBoundaryPasses;

            _stats.dfsphBucketBoundaryPadding = solverContext.GpuDfsphConfig.bucketBoundaryPadding;

            _stats.dfsphBucketBoundaryPositionStrength = solverContext.GpuDfsphConfig.bucketBoundaryPositionCorrectionStrength;

            _stats.dfsphBucketBoundaryVelocityStrength = solverContext.GpuDfsphConfig.bucketBoundaryVelocityCorrectionStrength;

            _stats.dfsphBucketBoundaryRestitution = solverContext.GpuDfsphConfig.bucketBoundaryRestitution;

            _stats.dfsphBucketBoundaryFriction = solverContext.GpuDfsphConfig.bucketBoundaryFriction;


            _stats.dfsphMovingBucketWallVelocityEnabled = solverContext.GpuDfsphConfig.enableMovingBucketWallVelocity;

            _stats.dfsphMovingBucketPreSolveEnabled = solverContext.GpuDfsphConfig.applyMovingBucketVelocityBeforeSolve;

            _stats.dfsphUseMovingWallVelocityInBoundary = solverContext.GpuDfsphConfig.useMovingWallVelocityInBoundary;

            _stats.dfsphMovingWallVelocityStrength = solverContext.GpuDfsphConfig.movingWallVelocityStrength;

            _stats.dfsphMaxMovingWallVelocity = solverContext.GpuDfsphConfig.maxMovingWallVelocity;

            _stats.dfsphMaxPreSolveWallVelocityCorrection = solverContext.GpuDfsphConfig.maxPreSolveWallVelocityCorrection;

            _stats.dfsphPreSolveWallInfluenceDistance = solverContext.GpuDfsphConfig.preSolveWallInfluenceDistance;

            _stats.dfsphPreSolveWallVelocityCorrectionStrength = solverContext.GpuDfsphConfig.preSolveWallVelocityCorrectionStrength;


            _stats.dfsphPaintMaterialEnabled = solverContext.GpuDfsphConfig.enablePaintMaterialModel;

            _stats.dfsphXsphViscosityStrength = solverContext.GpuDfsphConfig.xsphViscosityStrength;

            _stats.dfsphCohesionStrength = solverContext.GpuDfsphConfig.cohesionStrength;

            _stats.dfsphMaxPaintMaterialVelocityCorrection = solverContext.GpuDfsphConfig.maxPaintMaterialVelocityCorrection;

            _stats.dfsphAntiSprayDamping = solverContext.GpuDfsphConfig.antiSprayDamping;

            _stats.dfsphAntiSprayNeighborThreshold = solverContext.GpuDfsphConfig.antiSprayNeighborThreshold;

            _stats.dfsphWallAdhesionEnabled = solverContext.GpuDfsphConfig.enableWallAdhesionDamping;

            _stats.dfsphWallAdhesionDistance = solverContext.GpuDfsphConfig.wallAdhesionDistance;

            _stats.dfsphWallTangentialDamping = solverContext.GpuDfsphConfig.wallTangentialDamping;

            _stats.dfsphWallNormalDamping = solverContext.GpuDfsphConfig.wallNormalDamping;

            _stats.dfsphWallAdhesionAttraction = solverContext.GpuDfsphConfig.wallAdhesionAttraction;

            if (solverContext.BucketSystem != null && solverContext.BucketSystem.IsInitialized)
            {
                _previousBucketLocalToWorld = _currentBucketLocalToWorld;
                _previousBucketWorldToLocal = _currentBucketWorldToLocal;
                _hasPreviousBucketTransform = true;
            }
        }

        private bool ValidateContext(FluidSolverContext context)
        {
            if (context == null)
            {
                UnityEngine.Debug.LogError("GpuDfsphPaintSolver: Context is null.");
                return false;
            }

            if (context.GpuDfsphConfig == null)
            {
                UnityEngine.Debug.LogError("GpuDfsphPaintSolver: Missing GpuDfsphSolverConfig.");
                return false;
            }

            if (context.GpuDfsphConfig.dfsphCompute == null)
            {
                UnityEngine.Debug.LogError("GpuDfsphPaintSolver: Missing DFSPH compute shader.");
                return false;
            }

            if (context.GpuBufferSet == null)
            {
                UnityEngine.Debug.LogError("GpuDfsphPaintSolver: Missing GpuFluidBufferSet.");
                return false;
            }

            return true;
        }

        private void ResolveKernels(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuDfsphConfig.dfsphCompute;

            _kernelClearDfsphScalarData = compute.FindKernel("KClearDfsphScalarData");

            _kernelClearDfsphTempData = compute.FindKernel("KClearDfsphTempData");

            _kernelApplyExternalForces = compute.FindKernel("KApplyExternalForces");

            _kernelAdvectParticles = compute.FindKernel("KAdvectParticles");

            _kernelClearSpatialGrid = compute.FindKernel("KClearSpatialGrid");

            _kernelBuildSpatialGrid = compute.FindKernel("KBuildSpatialGrid");

            _kernelComputeNeighborCountDebug = compute.FindKernel("KComputeNeighborCountDebug");

            _kernelComputeDensity = compute.FindKernel("KComputeDensity");

            _kernelComputeAlpha = compute.FindKernel("KComputeAlpha");

            _kernelComputeDivergencePressure = compute.FindKernel("KComputeDivergencePressure");

            _kernelApplyDivergenceVelocityCorrection = compute.FindKernel("KApplyDivergenceVelocityCorrection");

            _kernelCopyVelocityTempToMain = compute.FindKernel("KCopyVelocityTempToMain");

            _kernelComputeDensityPressure = compute.FindKernel("KComputeDensityPressure");

            _kernelApplyDensityVelocityCorrection = compute.FindKernel("KApplyDensityVelocityCorrection");

            _kernelApplyBucketSdfBoundary = compute.FindKernel("KApplyBucketSdfBoundary");

            _kernelApplyBucketVelocityPreSolve = compute.FindKernel("KApplyBucketVelocityPreSolve");

            _kernelApplyPaintViscosityCohesion = compute.FindKernel("KApplyPaintViscosityCohesion");

            _kernelApplyWallAdhesionDamping = compute.FindKernel("KApplyWallAdhesionDamping");

            _kernelApplyBucketFrameTransport = compute.FindKernel("KApplyBucketFrameTransport");

            _kernelComputeBoundaryFactor = compute.FindKernel("KComputeBoundaryFactor");
            _kernelApplyBoundaryPressurePush = compute.FindKernel("KApplyBoundaryPressurePush");

            _kernelResolveSdfBoundaryPreSolve = compute.FindKernel("KResolveSdfBoundaryPreSolve");

            _kernelClearBoundarySpatialGrid =compute.FindKernel("KClearBoundarySpatialGrid");
            _kernelBuildBoundarySpatialGrid = compute.FindKernel("KBuildBoundarySpatialGrid");

            _kernelResolveBoundaryParticleCollisionPreSolve = compute.FindKernel("KResolveBoundaryParticleCollisionPreSolve");
        }

        private bool HasValidKernels()
        {
            return
                _kernelClearDfsphScalarData >= 0 &&
                _kernelClearDfsphTempData >= 0 &&
                _kernelApplyExternalForces >= 0 &&
                _kernelAdvectParticles >= 0 &&
                _kernelClearSpatialGrid >= 0 &&
                _kernelBuildSpatialGrid >= 0 &&
                _kernelComputeNeighborCountDebug >= 0 &&
                _kernelComputeDensity >= 0 &&
                _kernelComputeAlpha >= 0 &&
                _kernelComputeDivergencePressure >= 0 &&
                _kernelApplyDivergenceVelocityCorrection >= 0 &&
                _kernelComputeDensityPressure >= 0 &&
                _kernelApplyDensityVelocityCorrection >= 0 &&
                _kernelApplyBucketSdfBoundary >= 0 &&
                _kernelApplyBucketVelocityPreSolve >= 0 &&
                _kernelApplyPaintViscosityCohesion >= 0 &&
                _kernelApplyWallAdhesionDamping >= 0 &&
                _kernelApplyBucketFrameTransport >= 0 &&
                _kernelComputeBoundaryFactor >= 0 &&
                _kernelApplyBoundaryPressurePush >= 0 &&
                _kernelResolveSdfBoundaryPreSolve >= 0 &&
                _kernelClearBoundarySpatialGrid >= 0 &&
                _kernelBuildBoundarySpatialGrid >= 0 &&
                _kernelResolveBoundaryParticleCollisionPreSolve >= 0 &&
                _kernelCopyVelocityTempToMain >= 0;
        }

        private void SetCommonParameters(
            ComputeShader compute,
            FluidSolverContext context,
            SimulationContext simulationContext,
            FluidSolverStepInput stepInput,
            int particleCount)
        {
            compute.SetInt("_ParticleCount", particleCount);

            float dt = Mathf.Min(
                stepInput.dt,
                context.GpuDfsphConfig.maxDt
            );

            compute.SetFloat("_Dt", dt);

            Vector3 gravity =
                simulationContext.EnvironmentState.gravity *
                stepInput.gravityScale;

            compute.SetVector("_Gravity", gravity);

            compute.SetFloat(
                "_VelocityDampingPerSecond",
                context.GpuDfsphConfig.velocityDampingPerSecond
            );

            compute.SetFloat(
                "_MaxParticleSpeed",
                context.GpuDfsphConfig.maxParticleSpeed
            );

            Vector3Int gridResolution = context.GpuDfsphConfig.gridResolution;

            compute.SetInts(
                "_GridResolution",
                gridResolution.x,
                gridResolution.y,
                gridResolution.z
            );

            compute.SetInt(
                "_GridCellCount",
                context.GpuDfsphConfig.GridCellCount
            );

            Vector3 gridOrigin = context.GpuDfsphConfig.gridOriginWorld;

            if (context.GpuDfsphConfig.centerNeighborGridOnBucket &&
                context.BucketSystem != null &&
                context.BucketSystem.IsInitialized)
            {
                Vector3Int res = context.GpuDfsphConfig.gridResolution;
                float cell = context.GpuDfsphConfig.gridCellSize;

                Vector3 gridWorldSize = new Vector3(
                    res.x * cell,
                    res.y * cell,
                    res.z * cell
                );

                Vector3 bucketCenter = context.BucketSystem.GetCenterOfMassWorld();

                Vector3 gridCenter =
                    bucketCenter +
                    context.GpuDfsphConfig.neighborGridCenterOffset;

                gridOrigin =
                    gridCenter -
                    0.5f * gridWorldSize;
            }

            compute.SetVector("_GridOrigin", gridOrigin);

            compute.SetFloat(
                "_GridCellSize",
                context.GpuDfsphConfig.gridCellSize
            );

            compute.SetFloat(
                "_InvGridCellSize",
                1.0f / Mathf.Max(context.GpuDfsphConfig.gridCellSize, 1e-6f)
            );

            compute.SetFloat(
                "_SupportRadius",
                context.GpuDfsphConfig.supportRadius
            );

            compute.SetFloat(
                "_SupportRadius2",
                context.GpuDfsphConfig.supportRadius *
                context.GpuDfsphConfig.supportRadius
            );

            compute.SetInt(
                "_MaxParticlesPerCell",
                context.GpuDfsphConfig.maxParticlesPerCell
            );

            compute.SetInt(
                "_DiscardParticlesOutsideGrid",
                context.GpuDfsphConfig.discardParticlesOutsideGrid ? 1 : 0
            );

            compute.SetFloat(
                "_RestDensity",
                context.GpuDfsphConfig.restDensity
            );

            compute.SetFloat(
                "_MinDensityClamp",
                context.GpuDfsphConfig.minDensityClamp
            );

            compute.SetFloat(
                "_MaxDensityClamp",
                context.GpuDfsphConfig.maxDensityClamp
            );

            compute.SetInt(
                "_DensityKernelWritesNeighborCount",
                context.GpuDfsphConfig.densityKernelWritesNeighborCount ? 1 : 0
            );

            float h = Mathf.Max(context.GpuDfsphConfig.supportRadius, 1e-6f);

            float poly6Coefficient =
                315.0f /
                (64.0f * Mathf.PI * Mathf.Pow(h, 9.0f));

            compute.SetFloat(
                "_Poly6KernelCoefficient",
                poly6Coefficient
            );

            compute.SetFloat(
                "_AlphaDenominatorEpsilon",
                context.GpuDfsphConfig.alphaDenominatorEpsilon
            );

            compute.SetFloat(
                "_MaxAlpha",
                context.GpuDfsphConfig.maxAlpha
            );

            compute.SetInt(
                "_MinNeighborsForFullAlpha",
                context.GpuDfsphConfig.minNeighborsForFullAlpha
            );

            compute.SetFloat(
                "_LowNeighborAlphaDamping",
                context.GpuDfsphConfig.lowNeighborAlphaDamping
            );

            float h0 = Mathf.Max(context.GpuDfsphConfig.supportRadius, 1e-6f);

            float spikyGradientCoefficient =
                -45.0f /
                (Mathf.PI * Mathf.Pow(h0, 6.0f));

            compute.SetFloat(
                "_SpikyGradientCoefficient",
                spikyGradientCoefficient
            );

            compute.SetInt(
                "_EnableDivergenceSolve",
                context.GpuDfsphConfig.enableDivergenceSolve ? 1 : 0
            );

            compute.SetFloat(
                "_DivergenceErrorScale",
                context.GpuDfsphConfig.divergenceErrorScale
            );

            compute.SetFloat(
                "_DivergenceCorrectionStrength",
                context.GpuDfsphConfig.divergenceCorrectionStrength
            );

            compute.SetFloat(
                "_MaxDivergencePressure",
                context.GpuDfsphConfig.maxDivergencePressure
            );

            compute.SetFloat(
                "_MaxDivergenceVelocityCorrection",
                context.GpuDfsphConfig.maxDivergenceVelocityCorrection
            );

            compute.SetInt(
                "_ClampDivergenceToCompressionOnly",
                context.GpuDfsphConfig.clampDivergenceToCompressionOnly ? 1 : 0
            );

            compute.SetInt(
                "_InvertDivergenceErrorSign",
                context.GpuDfsphConfig.invertDivergenceErrorSign ? 1 : 0
            );

            compute.SetInt(
                "_EnableParticleAdvection",
                context.GpuDfsphConfig.enableParticleAdvection ? 1 : 0
            );

            compute.SetInt(
                "_EnableDensitySolve",
                context.GpuDfsphConfig.enableDensitySolve ? 1 : 0
            );

            compute.SetFloat(
                "_DensityErrorScale",
                context.GpuDfsphConfig.densityErrorScale
            );

            compute.SetFloat(
                "_DensityCorrectionStrength",
                context.GpuDfsphConfig.densityCorrectionStrength
            );

            compute.SetFloat(
                "_MaxDensityPressure",
                context.GpuDfsphConfig.maxDensityPressure
            );

            compute.SetFloat(
                "_MaxDensityVelocityCorrection",
                context.GpuDfsphConfig.maxDensityVelocityCorrection
            );

            compute.SetInt(
                "_ClampDensityToCompressionOnly",
                context.GpuDfsphConfig.clampDensityToCompressionOnly ? 1 : 0
            );

            compute.SetInt(
                "_InvertDensityErrorSign",
                context.GpuDfsphConfig.invertDensityErrorSign ? 1 : 0
            );

            SetBucketSdfBoundaryParameters(context);


            compute.SetInt(
                "_EnablePaintMaterialModel",
                context.GpuDfsphConfig.enablePaintMaterialModel ? 1 : 0
            );

            compute.SetFloat(
                "_XsphViscosityStrength",
                context.GpuDfsphConfig.xsphViscosityStrength
            );

            compute.SetFloat(
                "_MaxPaintMaterialVelocityCorrection",
                context.GpuDfsphConfig.maxPaintMaterialVelocityCorrection
            );

            compute.SetFloat(
                "_CohesionStrength",
                context.GpuDfsphConfig.cohesionStrength
            );

            compute.SetFloat(
                "_CohesionDistanceFactor",
                context.GpuDfsphConfig.cohesionDistanceFactor
            );

            compute.SetFloat(
                "_AntiSprayDamping",
                context.GpuDfsphConfig.antiSprayDamping
            );

            compute.SetInt(
                "_AntiSprayNeighborThreshold",
                context.GpuDfsphConfig.antiSprayNeighborThreshold
            );

            compute.SetInt(
                "_EnableWallAdhesionDamping",
                context.GpuDfsphConfig.enableWallAdhesionDamping ? 1 : 0
            );

            compute.SetFloat(
                "_WallAdhesionDistance",
                context.GpuDfsphConfig.wallAdhesionDistance
            );

            compute.SetFloat(
                "_WallTangentialDamping",
                context.GpuDfsphConfig.wallTangentialDamping
            );

            compute.SetFloat(
                "_WallNormalDamping",
                context.GpuDfsphConfig.wallNormalDamping
            );

            compute.SetFloat(
                "_WallAdhesionAttraction",
                context.GpuDfsphConfig.wallAdhesionAttraction
            );

            compute.SetInt(
                "_EnableBoundaryAwareDfsph",
                context.GpuDfsphConfig.enableBoundaryAwareDfsph ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryInfluenceDistance",
                context.GpuDfsphConfig.boundaryInfluenceDistance
            );

            compute.SetFloat(
                "_BoundaryDensityContribution",
                context.GpuDfsphConfig.boundaryDensityContribution
            );

            compute.SetFloat(
                "_BoundaryAlphaScaleNearWall",
                context.GpuDfsphConfig.boundaryAlphaScaleNearWall
            );

            compute.SetInt(
                "_EnableBoundaryPressurePush",
                context.GpuDfsphConfig.enableBoundaryPressurePush ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryPressurePushStrength",
                context.GpuDfsphConfig.boundaryPressurePushStrength
            );

            compute.SetFloat(
                "_MaxBoundaryPressureVelocityCorrection",
                context.GpuDfsphConfig.maxBoundaryPressureVelocityCorrection
            );

            compute.SetInt(
                "_UseMovingWallVelocityForBoundaryPush",
                context.GpuDfsphConfig.useMovingWallVelocityForBoundaryPush ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryDensityErrorPushScale",
                context.GpuDfsphConfig.boundaryDensityErrorPushScale
            );

            compute.SetInt(
                "_EnableConservativeSdfBoundary",
                context.GpuDfsphConfig.enableConservativeSdfBoundary ? 1 : 0
            );

            compute.SetInt(
                "_EnableSdfPreSolveCollision",
                context.GpuDfsphConfig.enableSdfPreSolveCollision ? 1 : 0
            );

            compute.SetFloat(
                "_SdfSkinDistance",
                context.GpuDfsphConfig.sdfSkinDistance
            );

            compute.SetFloat(
                "_SdfNormalVelocityRemoval",
                context.GpuDfsphConfig.sdfNormalVelocityRemoval
            );

            compute.SetFloat(
                "_SdfTangentialFriction",
                context.GpuDfsphConfig.sdfTangentialFriction
            );

            compute.SetFloat(
                "_MaxSdfPreSolveVelocityCorrection",
                context.GpuDfsphConfig.maxSdfPreSolveVelocityCorrection
            );

            compute.SetFloat(
                "_MaxSdfPositionCorrectionPerStep",
                context.GpuDfsphConfig.maxSdfPositionCorrectionPerStep
            );

            compute.SetFloat(
                "_SdfProjectionStrength",
                context.GpuDfsphConfig.sdfProjectionStrength
            );

            compute.SetInt(
                "_SdfUseMovingWallVelocity",
                context.GpuDfsphConfig.sdfUseMovingWallVelocity ? 1 : 0
            );

            compute.SetInt(
                "_EnableGpuBoundaryParticles",
                context.GpuDfsphConfig.enableGpuBoundaryParticles ? 1 : 0
            );

            compute.SetInt(
                "_BoundaryParticleCount",
                _boundaryParticleCount
            );

            compute.SetInt(
                "_MaxBoundaryParticlesPerCell",
                context.GpuDfsphConfig.maxBoundaryParticlesPerCell
            );

            compute.SetInt(
                "_DiscardBoundaryParticlesOutsideGrid",
                context.GpuDfsphConfig.discardBoundaryParticlesOutsideGrid ? 1 : 0
            );

            compute.SetInt(
                "_EnableBoundaryParticleDensity",
                context.GpuDfsphConfig.enableBoundaryParticleDensity ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryDensityStrength",
                context.GpuDfsphConfig.boundaryDensityStrength
            );

            compute.SetFloat(
                "_MaxBoundaryDensityContribution",
                context.GpuDfsphConfig.maxBoundaryDensityContribution
            );

            compute.SetInt(
                "_EnableBoundaryParticleAlpha",
                context.GpuDfsphConfig.enableBoundaryParticleAlpha ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryAlphaStrength",
                context.GpuDfsphConfig.boundaryAlphaStrength
            );

            compute.SetInt(
                "_MinBoundaryNeighborsForFullSupport",
                context.GpuDfsphConfig.minBoundaryNeighborsForFullSupport
            );

            compute.SetFloat(
                "_LowBoundaryNeighborDamping",
                context.GpuDfsphConfig.lowBoundaryNeighborDamping
            );

            compute.SetInt(
                "_WriteBoundaryDensityDebug",
                context.GpuDfsphConfig.writeBoundaryDensityDebug ? 1 : 0
            );

            compute.SetInt(
                "_EnableBoundaryDivergencePressure",
                context.GpuDfsphConfig.enableBoundaryDivergencePressure ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryDivergencePressureStrength",
                context.GpuDfsphConfig.boundaryDivergencePressureStrength
            );

            compute.SetInt(
                "_EnableBoundaryDensityPressure",
                context.GpuDfsphConfig.enableBoundaryDensityPressure ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryDensityPressureStrength",
                context.GpuDfsphConfig.boundaryDensityPressureStrength
            );

            compute.SetFloat(
                "_MaxBoundaryDivergenceVelocityCorrection",
                context.GpuDfsphConfig.maxBoundaryDivergenceVelocityCorrection
            );

            compute.SetFloat(
                "_MaxBoundaryDensityVelocityCorrection",
                context.GpuDfsphConfig.maxBoundaryDensityVelocityCorrection
            );

            compute.SetInt(
                "_UseBoundaryVelocityInPressure",
                context.GpuDfsphConfig.useBoundaryVelocityInPressure ? 1 : 0
            );

            compute.SetInt(
                "_EnableBoundaryNormalFiltering",
                context.GpuDfsphConfig.enableBoundaryNormalFiltering ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryNormalMinDot",
                context.GpuDfsphConfig.boundaryNormalMinDot
            );

            compute.SetFloat(
                "_BoundaryPressureDebugScale",
                context.GpuDfsphConfig.boundaryPressureDebugScale
            );

            compute.SetInt(
                "_EnableBoundaryParticleCollisionPreSolve",
                context.GpuDfsphConfig.enableBoundaryParticleCollisionPreSolve ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryCollisionDistanceFactor",
                context.GpuDfsphConfig.boundaryCollisionDistanceFactor
            );

            compute.SetFloat(
                "_BoundaryCollisionNormalVelocityRemoval",
                context.GpuDfsphConfig.boundaryCollisionNormalVelocityRemoval
            );

            compute.SetFloat(
                "_BoundaryCollisionTangentialFriction",
                context.GpuDfsphConfig.boundaryCollisionTangentialFriction
            );

            compute.SetFloat(
                "_MaxBoundaryCollisionVelocityCorrection",
                context.GpuDfsphConfig.maxBoundaryCollisionVelocityCorrection
            );

            compute.SetFloat(
                "_MaxBoundaryCollisionPositionCorrection",
                context.GpuDfsphConfig.maxBoundaryCollisionPositionCorrection
            );

            compute.SetInt(
                "_UseBoundaryVelocityInCollision",
                context.GpuDfsphConfig.useBoundaryVelocityInCollision ? 1 : 0
            );

            compute.SetInt(
                "_EnableBoundaryCollisionNormalFilter",
                context.GpuDfsphConfig.enableBoundaryCollisionNormalFilter ? 1 : 0
            );

            compute.SetFloat(
                "_BoundaryCollisionNormalMinDot",
                context.GpuDfsphConfig.boundaryCollisionNormalMinDot
            );
        }

        private void SetBucketSdfBoundaryParameters(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuDfsphConfig.dfsphCompute;

            bool enabled =
                context.GpuDfsphConfig.enableBucketSdfBoundary &&
                context.BucketSystem != null &&
                context.BucketSystem.IsInitialized &&
                context.BucketSystem.Config != null;

            compute.SetInt("_EnableBucketSdfBoundary", enabled ? 1 : 0);

            compute.SetInt(
                "_BucketBoundaryTopOpen",
                context.GpuDfsphConfig.bucketBoundaryTopOpen ? 1 : 0
            );

            compute.SetInt(
                "_BucketBoundaryEnableSideWalls",
                context.GpuDfsphConfig.bucketBoundaryEnableSideWalls ? 1 : 0
            );

            compute.SetInt(
                "_BucketBoundaryEnableBottom",
                context.GpuDfsphConfig.bucketBoundaryEnableBottom ? 1 : 0
            );

            compute.SetFloat(
                "_BucketBoundaryPadding",
                context.GpuDfsphConfig.bucketBoundaryPadding
            );

            compute.SetFloat(
                "_BucketBoundaryPositionStrength",
                context.GpuDfsphConfig.bucketBoundaryPositionCorrectionStrength
            );

            compute.SetFloat(
                "_BucketBoundaryVelocityStrength",
                context.GpuDfsphConfig.bucketBoundaryVelocityCorrectionStrength
            );

            compute.SetFloat(
                "_BucketBoundaryRestitution",
                context.GpuDfsphConfig.bucketBoundaryRestitution
            );

            compute.SetFloat(
                "_BucketBoundaryFriction",
                context.GpuDfsphConfig.bucketBoundaryFriction
            );


            compute.SetInt(
                "_EnableMovingBucketWallVelocity",
                context.GpuDfsphConfig.enableMovingBucketWallVelocity ? 1 : 0
            );

            compute.SetInt(
                "_UseMovingWallVelocityInBoundary",
                context.GpuDfsphConfig.useMovingWallVelocityInBoundary ? 1 : 0
            );

            compute.SetFloat(
                "_MovingWallVelocityStrength",
                context.GpuDfsphConfig.movingWallVelocityStrength
            );

            compute.SetFloat(
                "_MaxMovingWallVelocity",
                context.GpuDfsphConfig.maxMovingWallVelocity
            );

            compute.SetFloat(
                "_MaxPreSolveWallVelocityCorrection",
                context.GpuDfsphConfig.maxPreSolveWallVelocityCorrection
            );

            compute.SetFloat(
                "_PreSolveWallInfluenceDistance",
                context.GpuDfsphConfig.preSolveWallInfluenceDistance
            );

            compute.SetFloat(
                "_PreSolveWallVelocityCorrectionStrength",
                context.GpuDfsphConfig.preSolveWallVelocityCorrectionStrength
            );

            if (!enabled)
            {
                compute.SetInt("_EnableBucketFrameTransport", 0);
                compute.SetMatrix("_PreviousBucketWorldToLocal", Matrix4x4.identity);
                compute.SetMatrix("_CurrentBucketLocalToWorld", Matrix4x4.identity);

                compute.SetVector("_BucketPositionWorld", Vector4.zero);
                compute.SetVector("_BucketLinearVelocityWorld", Vector4.zero);
                compute.SetVector("_BucketAngularVelocityWorld", Vector4.zero);
                return;
            }

            var bucket = context.BucketSystem;
            var config = bucket.Config;
            var state = bucket.State;

            Vector3 bucketPosition = state.position;
            Vector3 bucketLinearVelocity = state.velocity;
            Vector3 bucketAngularVelocity = state.angularVelocity;

            compute.SetVector(
                "_BucketPositionWorld",
                new Vector4(
                    bucketPosition.x,
                    bucketPosition.y,
                    bucketPosition.z,
                    0.0f
                )
            );

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

            Matrix4x4 localToWorld = Matrix4x4.TRS(
                state.position,
                state.rotation,
                Vector3.one
            );

            Matrix4x4 worldToLocal = localToWorld.inverse;

            _currentBucketLocalToWorld = localToWorld;
            _currentBucketWorldToLocal = worldToLocal;

            if (!_hasPreviousBucketTransform)
            {
                _previousBucketLocalToWorld = localToWorld;
                _previousBucketWorldToLocal = worldToLocal;
                _hasPreviousBucketTransform = true;
            }

            compute.SetMatrix(
                "_PreviousBucketWorldToLocal",
                _previousBucketWorldToLocal
            );

            compute.SetMatrix(
                "_CurrentBucketLocalToWorld",
                _currentBucketLocalToWorld
            );

            compute.SetInt(
                "_EnableBucketFrameTransport",
                context.GpuDfsphConfig.enableBucketFrameTransport ? 1 : 0
            );

            compute.SetFloat(
                "_BucketFramePositionTransportStrength",
                context.GpuDfsphConfig.bucketFramePositionTransportStrength
            );

            compute.SetFloat(
                "_BucketFrameVelocityTransportStrength",
                context.GpuDfsphConfig.bucketFrameVelocityTransportStrength
            );

            compute.SetFloat(
                "_MaxBucketFrameTransportVelocity",
                context.GpuDfsphConfig.maxBucketFrameTransportVelocity
            );

            compute.SetFloat(
                "_BucketFrameTransportMinDelta",
                context.GpuDfsphConfig.bucketFrameTransportMinDelta
            );

            compute.SetMatrix("_BucketLocalToWorld", localToWorld);
            compute.SetMatrix("_BucketWorldToLocal", worldToLocal);

            compute.SetInt("_BucketShapeType", (int)config.shapeType);

            compute.SetFloat("_BucketHeight", config.heightMeters);
            compute.SetFloat("_BucketTopRadius", config.topRadiusMeters);
            compute.SetFloat("_BucketBottomRadius", config.bottomRadiusMeters);
            compute.SetFloat("_BucketWallThickness", config.wallThicknessMeters);
        }

        private void BindApplyExternalForces(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyExternalForces,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyExternalForces,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private void ClearDfsphParticleData(FluidSolverContext context)
        {
            ComputeShader compute = context.GpuDfsphConfig.dfsphCompute;

            int capacity = _dfsphBuffers.Capacity;

            compute.SetInt("_DfsphBufferCapacity", capacity);
            compute.SetFloat("_RestDensity", context.GpuDfsphConfig.restDensity);

            BindClearDfsphScalarData(compute);

            compute.Dispatch(
                _kernelClearDfsphScalarData,
                Groups(capacity),
                1,
                1
            );

            BindClearDfsphTempData(compute);

            compute.Dispatch(
                _kernelClearDfsphTempData,
                Groups(capacity),
                1,
                1
            );
        }

        private void BindClearDfsphScalarData(ComputeShader compute)
        {
            compute.SetBuffer(_kernelClearDfsphScalarData, "_Density", _dfsphBuffers.DensityBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_PredictedDensity", _dfsphBuffers.PredictedDensityBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_Alpha", _dfsphBuffers.AlphaBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_DensityError", _dfsphBuffers.DensityErrorBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_DivergenceError", _dfsphBuffers.DivergenceErrorBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_DivergencePressure", _dfsphBuffers.DivergencePressureBuffer);
            compute.SetBuffer(_kernelClearDfsphScalarData, "_DensityPressure", _dfsphBuffers.DensityPressureBuffer);
        }

        private void BindClearDfsphTempData(ComputeShader compute)
        {
            compute.SetBuffer(_kernelClearDfsphTempData, "_NeighborCount", _dfsphBuffers.NeighborCountBuffer);
            compute.SetBuffer(_kernelClearDfsphTempData, "_DfsphTempVector", _dfsphBuffers.TempVectorBuffer);
            compute.SetBuffer(_kernelClearDfsphTempData, "_DfsphTempScalar", _dfsphBuffers.TempScalarBuffer);

            compute.SetBuffer(_kernelClearDfsphTempData, "_BoundaryFactor", _dfsphBuffers.BoundaryFactorBuffer);

            compute.SetBuffer(_kernelClearDfsphTempData, "_BoundaryNeighborCount", _dfsphBuffers.BoundaryNeighborCountBuffer);
            compute.SetBuffer(_kernelClearDfsphTempData, "_BoundaryDensityContributionDebug", _dfsphBuffers.BoundaryDensityContributionBuffer);
        }

        private void BindClearSpatialGrid(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearSpatialGrid,
                "_ParticleCellIndex",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelClearSpatialGrid,
                "_NeighborCount",
                _dfsphBuffers.NeighborCountBuffer
            );

            compute.SetBuffer(
                _kernelClearSpatialGrid,
                "_CellParticleCount",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelClearSpatialGrid,
                "_SpatialGridStats",
                _dfsphBuffers.SpatialGridStatsBuffer
            );
        }

        private void BindBuildSpatialGrid(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_ParticleCellIndex",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_CellParticleCount",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_CellParticleIndices",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelBuildSpatialGrid,
                "_SpatialGridStats",
                _dfsphBuffers.SpatialGridStatsBuffer
            );
        }

        private void BindComputeNeighborCountDebug(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeNeighborCountDebug,
                "_NeighborCount",
                _dfsphBuffers.NeighborCountBuffer
            );
        }

        private int RunNeighborSearch(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableNeighborSearch)
                return 0;

            int gridCellCount = context.GpuDfsphConfig.GridCellCount;

            int clearCount = Mathf.Max(
                particleCount,
                gridCellCount
            );

            compute.SetInt("_SpatialClearElementCount", clearCount);

            BindClearSpatialGrid(compute);

            compute.Dispatch(
                _kernelClearSpatialGrid,
                Groups(clearCount),
                1,
                1
            );

            BindBuildSpatialGrid(compute, context);

            compute.Dispatch(
                _kernelBuildSpatialGrid,
                Groups(particleCount),
                1,
                1
            );

            int dispatches = 2;

            if (context.GpuDfsphConfig.enableNeighborCountDebug)
            {
                BindComputeNeighborCountDebug(compute, context);

                compute.Dispatch(
                    _kernelComputeNeighborCountDebug,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;
            }

            return dispatches;
        }

        private void BindComputeDensity(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeDensity,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_Density",
                _dfsphBuffers.DensityBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_PredictedDensity",
                _dfsphBuffers.PredictedDensityBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_DensityError",
                _dfsphBuffers.DensityErrorBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_NeighborCount",
                _dfsphBuffers.NeighborCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryFactorRead",
                _dfsphBuffers.BoundaryFactorBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryPositionRadiusRead",
                _boundaryBuffers.BoundaryPositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryVelocityPsiRead",
                _boundaryBuffers.BoundaryVelocityPsiBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryCellCountRead",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryCellIndicesRead",
                _boundaryBuffers.BoundaryCellIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryNeighborCount",
                _dfsphBuffers.BoundaryNeighborCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensity,
                "_BoundaryDensityContributionDebug",
                _dfsphBuffers.BoundaryDensityContributionBuffer
            );
        }

        private int RunDensityComputation(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableDensityComputation)
                return 0;

            if (!context.GpuDfsphConfig.enableNeighborSearch)
                return 0;

            BindComputeDensity(compute, context);

            compute.Dispatch(
                _kernelComputeDensity,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private void BindComputeAlpha(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_NeighborCount",
                _dfsphBuffers.NeighborCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_Alpha",
                _dfsphBuffers.AlphaBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_DfsphTempScalar",
                _dfsphBuffers.TempScalarBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_BoundaryFactorRead",
                _dfsphBuffers.BoundaryFactorBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_BoundaryPositionRadiusRead",
                _boundaryBuffers.BoundaryPositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_BoundaryVelocityPsiRead",
                _boundaryBuffers.BoundaryVelocityPsiBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_BoundaryCellCountRead",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeAlpha,
                "_BoundaryCellIndicesRead",
                _boundaryBuffers.BoundaryCellIndicesBuffer
            );
        }

        private int RunAlphaComputation(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableAlphaComputation)
                return 0;

            if (!context.GpuDfsphConfig.enableNeighborSearch)
                return 0;

            if (!context.GpuDfsphConfig.enableDensityComputation)
                return 0;

            BindComputeAlpha(compute, context);

            compute.Dispatch(_kernelComputeAlpha, Groups(particleCount), 1, 1);

            return 1;
        }

        private void BindAdvectParticles(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelAdvectParticles,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelAdvectParticles,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelAdvectParticles,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private void BindComputeDivergencePressure(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_AlphaRead",
                _dfsphBuffers.AlphaBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_DivergenceError",
                _dfsphBuffers.DivergenceErrorBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_DivergencePressure",
                _dfsphBuffers.DivergencePressureBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure, // غيّر kernel حسب الدالة
                "_BoundaryPositionRadiusRead",
                _boundaryBuffers.BoundaryPositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_BoundaryNormalTypeRead",
                _boundaryBuffers.BoundaryNormalTypeBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_BoundaryVelocityPsiRead",
                _boundaryBuffers.BoundaryVelocityPsiBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_BoundaryCellCountRead",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDivergencePressure,
                "_BoundaryCellIndicesRead",
                _boundaryBuffers.BoundaryCellIndicesBuffer
            );
        }

        private void BindApplyDivergenceVelocityCorrection(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_DivergencePressureRead",
                _dfsphBuffers.DivergencePressureBuffer
            );

            compute.SetBuffer(
                _kernelApplyDivergenceVelocityCorrection,
                "_DfsphTempVector",
                _dfsphBuffers.TempVectorBuffer
            );

            BindBoundaryBuffersForKernel(
                compute,
                _kernelApplyDivergenceVelocityCorrection
            );
            
        }

        private void BindCopyVelocityTempToMain(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelCopyVelocityTempToMain,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelCopyVelocityTempToMain,
                "_DfsphTempVectorRead",
                _dfsphBuffers.TempVectorBuffer
            );

            compute.SetBuffer(
                _kernelCopyVelocityTempToMain,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunDivergenceSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableDivergenceSolve)
                return 0;

            if (!context.GpuDfsphConfig.enableNeighborSearch ||
                !context.GpuDfsphConfig.enableDensityComputation ||
                !context.GpuDfsphConfig.enableAlphaComputation)
            {
                return 0;
            }

            int iterations = Mathf.Max(
                0,
                context.GpuDfsphConfig.divergenceSolverIterations
            );

            int dispatches = 0;

            for (int i = 0; i < iterations; i++)
            {
                BindComputeDivergencePressure(compute, context);

                compute.Dispatch(
                    _kernelComputeDivergencePressure,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;

                BindApplyDivergenceVelocityCorrection(compute, context);

                compute.Dispatch(
                    _kernelApplyDivergenceVelocityCorrection,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;

                BindCopyVelocityTempToMain(compute, context);

                compute.Dispatch(
                    _kernelCopyVelocityTempToMain,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;
            }

            return dispatches;
        }

        private static int Groups(int count)
        {
            return Mathf.CeilToInt(count / 256.0f);
        }

        private void BindComputeDensityPressure(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_DensityRead",
                _dfsphBuffers.DensityBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_AlphaRead",
                _dfsphBuffers.AlphaBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_PredictedDensity",
                _dfsphBuffers.PredictedDensityBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_DensityError",
                _dfsphBuffers.DensityErrorBuffer
            );

            compute.SetBuffer(
                _kernelComputeDensityPressure,
                "_DensityPressure",
                _dfsphBuffers.DensityPressureBuffer
            );
        }

        private void BindApplyDensityVelocityCorrection(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_DensityPressureRead",
                _dfsphBuffers.DensityPressureBuffer
            );

            compute.SetBuffer(
                _kernelApplyDensityVelocityCorrection,
                "_DfsphTempVector",
                _dfsphBuffers.TempVectorBuffer
            );

            BindBoundaryBuffersForKernel(
                compute,
                _kernelApplyDensityVelocityCorrection
            );
        }

        private int RunDensitySolve(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableDensitySolve)
                return 0;

            if (!context.GpuDfsphConfig.enableNeighborSearch ||
                !context.GpuDfsphConfig.enableDensityComputation ||
                !context.GpuDfsphConfig.enableAlphaComputation)
            {
                return 0;
            }

            int iterations = Mathf.Max(
                0,
                context.GpuDfsphConfig.densitySolverIterations
            );

           
           
            iterations = Mathf.Min(
                iterations,
                context.GpuDfsphConfig.safetyMaxDensityIterations
            );
           

            int dispatches = 0;

            for (int i = 0; i < iterations; i++)
            {
                BindComputeDensityPressure(compute, context);

                compute.Dispatch(
                    _kernelComputeDensityPressure,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;

                BindApplyDensityVelocityCorrection(compute, context);

                compute.Dispatch(
                    _kernelApplyDensityVelocityCorrection,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;

                BindCopyVelocityTempToMain(compute, context);

                compute.Dispatch(
                    _kernelCopyVelocityTempToMain,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;
            }

            return dispatches;
        }

        private void BindApplyBucketSdfBoundary(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyBucketSdfBoundary,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketSdfBoundary,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketSdfBoundary,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunBucketSdfBoundary(
            ComputeShader compute, FluidSolverContext context, int particleCount)
        {
            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (!context.GpuDfsphConfig.applyBucketBoundaryAfterAdvection)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            int passes = Mathf.Clamp(
                context.GpuDfsphConfig.bucketBoundaryPasses,
                1,
                4
            );

            BindApplyBucketSdfBoundary(compute, context);

            for (int i = 0; i < passes; i++)
            {
                compute.Dispatch(
                    _kernelApplyBucketSdfBoundary,
                    Groups(particleCount),
                    1,
                    1
                );
            }

            return passes;
        }

        private void BindApplyBucketVelocityPreSolve(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyBucketVelocityPreSolve,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketVelocityPreSolve,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketVelocityPreSolve,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunBucketVelocityPreSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (!context.GpuDfsphConfig.enableMovingBucketWallVelocity)
                return 0;

            if (!context.GpuDfsphConfig.applyMovingBucketVelocityBeforeSolve)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            BindApplyBucketVelocityPreSolve(compute, context);

            compute.Dispatch(
                _kernelApplyBucketVelocityPreSolve,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private void BindApplyPaintViscosityCohesion(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_ParticleVelocityMassRead",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_ParticleCellIndexRead",
                _dfsphBuffers.ParticleCellIndexBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_CellParticleCountRead",
                _dfsphBuffers.CellParticleCountBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_CellParticleIndicesRead",
                _dfsphBuffers.CellParticleIndicesBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_NeighborCount",
                _dfsphBuffers.NeighborCountBuffer
            );

            compute.SetBuffer(
                _kernelApplyPaintViscosityCohesion,
                "_DfsphTempVector",
                _dfsphBuffers.TempVectorBuffer
            );
        }

        private void BindApplyWallAdhesionDamping(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyWallAdhesionDamping,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyWallAdhesionDamping,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyWallAdhesionDamping,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunPaintMaterialModel(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            int dispatches = 0;

            if (context.GpuDfsphConfig.enablePaintMaterialModel &&
                context.GpuDfsphConfig.enableNeighborSearch)
            {
                BindApplyPaintViscosityCohesion(compute, context);

                compute.Dispatch(
                    _kernelApplyPaintViscosityCohesion,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;

                BindCopyVelocityTempToMain(compute, context);

                compute.Dispatch(
                    _kernelCopyVelocityTempToMain,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;
            }

            if (context.GpuDfsphConfig.enableWallAdhesionDamping &&
                context.GpuDfsphConfig.enableBucketSdfBoundary)
            {
                BindApplyWallAdhesionDamping(compute, context);

                compute.Dispatch(
                    _kernelApplyWallAdhesionDamping,
                    Groups(particleCount),
                    1,
                    1
                );

                dispatches++;
            }

            return dispatches;
        }

        private void BindApplyBucketFrameTransport(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyBucketFrameTransport,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketFrameTransport,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyBucketFrameTransport,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunBucketFrameTransport(
        ComputeShader compute,
        FluidSolverContext context,
        int particleCount)
        {
            if (!context.GpuDfsphConfig.enableBucketFrameTransport)
                return 0;

            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            BindApplyBucketFrameTransport(compute, context);

            compute.Dispatch(
                _kernelApplyBucketFrameTransport,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private void BindComputeBoundaryFactor(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelComputeBoundaryFactor,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelComputeBoundaryFactor,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelComputeBoundaryFactor,
                "_BoundaryFactor",
                _dfsphBuffers.BoundaryFactorBuffer
            );
        }

        private void BindApplyBoundaryPressurePush(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelApplyBoundaryPressurePush,
                "_ParticlePositionRadiusRead",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelApplyBoundaryPressurePush,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelApplyBoundaryPressurePush,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            compute.SetBuffer(
                _kernelApplyBoundaryPressurePush,
                "_BoundaryFactorRead",
                _dfsphBuffers.BoundaryFactorBuffer
            );

            compute.SetBuffer(
                _kernelApplyBoundaryPressurePush,
                "_DensityErrorRead",
                _dfsphBuffers.DensityErrorBuffer
            );
        }

        private int RunBoundaryFactorComputation(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableBoundaryAwareDfsph)
                return 0;

            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            BindComputeBoundaryFactor(compute, context);

            compute.Dispatch(
                _kernelComputeBoundaryFactor,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private int RunBoundaryPressurePush(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableBoundaryAwareDfsph)
                return 0;

            if (!context.GpuDfsphConfig.enableBoundaryPressurePush)
                return 0;

            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            BindApplyBoundaryPressurePush(compute, context);

            compute.Dispatch(
                _kernelApplyBoundaryPressurePush,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private void BindResolveSdfBoundaryPreSolve(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelResolveSdfBoundaryPreSolve,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelResolveSdfBoundaryPreSolve,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelResolveSdfBoundaryPreSolve,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );
        }

        private int RunSdfBoundaryPreSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableConservativeSdfBoundary)
                return 0;

            if (!context.GpuDfsphConfig.enableSdfPreSolveCollision)
                return 0;

            if (!context.GpuDfsphConfig.enableBucketSdfBoundary)
                return 0;

            if (context.BucketSystem == null ||
                !context.BucketSystem.IsInitialized)
                return 0;

            BindResolveSdfBoundaryPreSolve(compute, context);

            compute.Dispatch(
                _kernelResolveSdfBoundaryPreSolve,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }

        private void EnsureBoundaryCpuArrays(int capacity)
        {
            capacity = Mathf.Max(1, capacity);

            if (_boundaryPositionRadiusCpu == null ||
                _boundaryPositionRadiusCpu.Length < capacity)
            {
                _boundaryPositionRadiusCpu =
                    new Vector4[capacity];

                _boundaryNormalTypeCpu =
                    new Vector4[capacity];

                _boundaryVelocityPsiCpu =
                    new Vector4[capacity];
            }
        }

        private int UploadBoundaryParticlesToGpu(FluidSolverContext context)
        {
            _boundaryParticleCount = 0;

            if (!context.GpuDfsphConfig.enableGpuBoundaryParticles)
                return 0;

            if (context.BoundarySystem == null ||
                !context.BoundarySystem.IsInitialized)
                return 0;

            if (!_boundaryBuffers.IsInitialized)
                return 0;

            int capacity =
                context.GpuDfsphConfig.maxBoundaryParticles;

            EnsureBoundaryCpuArrays(capacity);

            float fallbackRadius =
                context.GpuDfsphConfig.fallbackBoundaryParticleRadius;

            // مؤقتًا:
            // إذا لم تكن psi محسوبة من BoundarySystem، نستعمل mass تقديرية.
            float spacing =
                Mathf.Max(
                    fallbackRadius * 2.0f,
                    0.0001f
                );

            float fallbackPsi =
                context.GpuDfsphConfig.restDensity *
                spacing *
                spacing *
                spacing;

            int exported =
                context.BoundarySystem.ExportGpuBoundaryParticles(
                    _boundaryPositionRadiusCpu,
                    _boundaryNormalTypeCpu,
                    _boundaryVelocityPsiCpu,
                    fallbackRadius,
                    fallbackPsi,
                    context.GpuDfsphConfig.boundaryPsiScale
                );

            exported = Mathf.Min(exported, capacity);

            if (exported <= 0)
                return 0;

            _boundaryParticleCount = exported;

            _boundaryBuffers.BoundaryPositionRadiusBuffer.SetData(_boundaryPositionRadiusCpu, 0, 0, exported);

            _boundaryBuffers.BoundaryNormalTypeBuffer.SetData(_boundaryNormalTypeCpu, 0, 0, exported);

            _boundaryBuffers.BoundaryVelocityPsiBuffer.SetData(_boundaryVelocityPsiCpu, 0, 0, exported);

            return exported;
        }

        private void BindClearBoundarySpatialGrid(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelClearBoundarySpatialGrid,
                "_BoundaryCellCount",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                _kernelClearBoundarySpatialGrid,
                "_BoundaryGridStats",
                _boundaryBuffers.BoundaryGridStatsBuffer
            );
        }

        private void BindBuildBoundarySpatialGrid(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelBuildBoundarySpatialGrid,
                "_BoundaryPositionRadiusRead",
                _boundaryBuffers.BoundaryPositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelBuildBoundarySpatialGrid,
                "_BoundaryCellCount",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                _kernelBuildBoundarySpatialGrid,
                "_BoundaryCellIndices",
                _boundaryBuffers.BoundaryCellIndicesBuffer
            );

            compute.SetBuffer(
                _kernelBuildBoundarySpatialGrid,
                "_BoundaryGridStats",
                _boundaryBuffers.BoundaryGridStatsBuffer
            );
        }

        private int RunBoundaryNeighborGrid(ComputeShader compute, FluidSolverContext context)
        {
            if (!context.GpuDfsphConfig.enableGpuBoundaryParticles)
                return 0;

            if (!_boundaryBuffers.IsInitialized)
                return 0;

            if (_boundaryParticleCount <= 0)
                return 0;

            int dispatches = 0;

            BindClearBoundarySpatialGrid(compute);

            compute.Dispatch(
                _kernelClearBoundarySpatialGrid,
                Groups(context.GpuDfsphConfig.GridCellCount),
                1,
                1
            );

            dispatches++;

            BindBuildBoundarySpatialGrid(compute);

            compute.Dispatch(
                _kernelBuildBoundarySpatialGrid,
                Groups(_boundaryParticleCount),
                1,
                1
            );

            dispatches++;

            return dispatches;
        }

        private void LogBoundaryGridStatsIfNeeded(FluidSolverContext context)
        {
            int every =
                context.GpuDfsphConfig.boundaryGridStatsLogEveryNFrames;

            if (every <= 0)
                return;

            if (!_boundaryBuffers.IsInitialized ||
                _boundaryBuffers.BoundaryGridStatsBuffer == null)
                return;

            _boundaryGridStatsFrameCounter++;

            if (_boundaryGridStatsFrameCounter % every != 0)
                return;

            _boundaryBuffers.BoundaryGridStatsBuffer.GetData(
                _boundaryGridStatsCpu
            );

            UnityEngine.Debug.Log(
                $"Boundary Grid Stats: " +
                $"inserted={_boundaryGridStatsCpu[0]}, " +
                $"overflow={_boundaryGridStatsCpu[1]}, " +
                $"outOfGrid={_boundaryGridStatsCpu[2]}, " +
                $"maxCellCount={_boundaryGridStatsCpu[3]}, " +
                $"boundaryCount={_boundaryParticleCount}"
            );
        }

        private void BindBoundaryBuffersForKernel(
            ComputeShader compute,
            int kernel)
        {
            if (!_boundaryBuffers.IsInitialized)
                return;

            compute.SetBuffer(
                kernel,
                "_BoundaryPositionRadiusRead",
                _boundaryBuffers.BoundaryPositionRadiusBuffer
            );

            compute.SetBuffer(
                kernel,
                "_BoundaryNormalTypeRead",
                _boundaryBuffers.BoundaryNormalTypeBuffer
            );

            compute.SetBuffer(
                kernel,
                "_BoundaryVelocityPsiRead",
                _boundaryBuffers.BoundaryVelocityPsiBuffer
            );

            compute.SetBuffer(
                kernel,
                "_BoundaryCellCountRead",
                _boundaryBuffers.BoundaryCellCountBuffer
            );

            compute.SetBuffer(
                kernel,
                "_BoundaryCellIndicesRead",
                _boundaryBuffers.BoundaryCellIndicesBuffer
            );
        }

        private void BindResolveBoundaryParticleCollisionPreSolve(
            ComputeShader compute,
            FluidSolverContext context)
        {
            GpuFluidBufferSet buffers = context.GpuBufferSet;

            compute.SetBuffer(
                _kernelResolveBoundaryParticleCollisionPreSolve,
                "_ParticlePositionRadius",
                buffers.PositionRadiusBuffer
            );

            compute.SetBuffer(
                _kernelResolveBoundaryParticleCollisionPreSolve,
                "_ParticleVelocityMass",
                buffers.VelocityMassBuffer
            );

            compute.SetBuffer(
                _kernelResolveBoundaryParticleCollisionPreSolve,
                "_ParticleStateAgeIdRead",
                buffers.StateAgeIdBuffer
            );

            BindBoundaryBuffersForKernel(
                compute,
                _kernelResolveBoundaryParticleCollisionPreSolve
            );
        }

        private int RunBoundaryParticleCollisionPreSolve(
            ComputeShader compute,
            FluidSolverContext context,
            int particleCount)
        {
            if (!context.GpuDfsphConfig.enableGpuBoundaryParticles)
                return 0;

            if (!context.GpuDfsphConfig.enableBoundaryParticleCollisionPreSolve)
                return 0;

            if (_boundaryParticleCount <= 0)
                return 0;

            if (!_boundaryBuffers.IsInitialized)
                return 0;

            BindResolveBoundaryParticleCollisionPreSolve(
                compute,
                context
            );

            compute.Dispatch(
                _kernelResolveBoundaryParticleCollisionPreSolve,
                Groups(particleCount),
                1,
                1
            );

            return 1;
        }
    }
}