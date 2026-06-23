using System.Diagnostics;
using PaintBucketSim.Core;
using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;
using Unity.Mathematics;

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

        private int _kernelClearGrid         = -1;
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
        private int _kernelComputeProjectionDivergence = -1;

        private int _kernelJacobiProjectionPressure = -1;
        private int _kernelSubtractProjectionPressureGradient = -1;

        /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// /// /// ///
        private int _kernelBucketCollision = -1;
        /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// /// /// ///

        private FluidSolverStats _stats;

        private readonly Stopwatch _cpuDispatchWatch = new Stopwatch();

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

            SetCommonParameters(solverContext, simulationContext, stepInput);

            BindClearGrid(compute);
            compute.Dispatch(_kernelClearGrid, Groups(gridNodeCount), 1, 1);

            BindP2G(compute, solverContext);
            compute.Dispatch(_kernelP2G, Groups(particleCount), 1, 1);

            BindGridUpdate(compute);
            compute.Dispatch(_kernelGridUpdate, Groups(gridNodeCount), 1, 1);

            int projectionDispatches = RunProjectionGridInfrastructure(
                compute,
                solverContext,
                particleCount,
                gridNodeCount
            );

            //BindG2P(compute, solverContext);
            //compute.Dispatch(_kernelG2P, Groups(particleCount), 1, 1);
            BindG2PVelocityApic(compute, solverContext);
            compute.Dispatch(_kernelG2PVelocityApic, Groups(particleCount), 1, 1);

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// /// ///
            /// 
            bool runBucketCollision =
                solverContext.GpuMpmConfig.enableGpuBucketCollision &&
                solverContext.GpuMpmConfig.useRealBucketCollision &&
                solverContext.BucketSystem != null &&
                solverContext.BucketSystem.IsInitialized &&
                _kernelBucketCollision >= 0;

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

            solverContext.GpuBufferSet.SetUploadedParticleCount(particleCount);

            _cpuDispatchWatch.Stop();

            _stats.status = FluidSolverStatus.Running;
            _stats.particleCount = particleCount;
            _stats.lastStepMilliseconds =
                (float)_cpuDispatchWatch.Elapsed.TotalMilliseconds;

            // These timings are CPU dispatch-submit timings, not real GPU execution timings.
            int baseDispatches = 4;
            // ClearGrid, P2G, GridUpdate, G2P

            int bucketDispatches = runBucketCollision ? 1 : 0;
            int deformationDispatches = 1;

            _stats.gpuDispatchCount =
                baseDispatches +
                projectionDispatches +
                bucketDispatches +
                deformationDispatches;
            _stats.gpuGridResolutionX = solverContext.GpuMpmConfig.gridResolution.x;
            _stats.gpuGridResolutionY = solverContext.GpuMpmConfig.gridResolution.y;
            _stats.gpuGridResolutionZ = solverContext.GpuMpmConfig.gridResolution.z;
            _stats.gpuGridNodeCount = gridNodeCount;
            _stats.gpuCellSizeMeters = solverContext.GpuMpmConfig.cellSizeMeters;

            _stats.projectionGridEnabled = solverContext.GpuMpmConfig.enableProjectionGridInfrastructure;
            _stats.projectionBuffersReady =
                _projectionCellTypeBuffer != null &&
                _projectionCellMassIntBuffer != null &&
                _projectionCellDataBuffer != null &&
                _projectionPressureBuffer != null &&
                _projectionPressureTempBuffer != null &&
                _projectionDivergenceBuffer != null;

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
            _kernelP2G = compute.FindKernel("KP2G");
            _kernelGridUpdate = compute.FindKernel("KGridUpdate");
            _kernelG2PVelocityApic = compute.FindKernel("KG2PVelocityApic");

            /// /// /// /// /// /// /// G8.A Changes /// /// /// /// /// /// /// 
            _kernelBucketCollision = compute.FindKernel("KBucketCollision");
            /// /// /// /// /// /// /// End G8.A Changes /// /// /// /// /// /// /// 
            
            _kernelUpdateDeformation = compute.FindKernel("KUpdateDeformation");

            _kernelClearProjectionGrid = compute.FindKernel("KClearProjectionGrid");
            _kernelMarkBucketProjectionSolids = compute.FindKernel("KMarkBucketProjectionSolids");
            _kernelMarkProjectionFluidCells = compute.FindKernel("KMarkProjectionFluidCells");
            _kernelFinalizeProjectionGrid = compute.FindKernel("KFinalizeProjectionGrid");
            _kernelComputeProjectionDivergence = compute.FindKernel("KComputeProjectionDivergence");
            _kernelJacobiProjectionPressure = compute.FindKernel("KJacobiProjectionPressure");
            _kernelSubtractProjectionPressureGradient = compute.FindKernel("KSubtractProjectionPressureGradient");
            _kernelApplyMovingBucketProjectionBoundaryVelocity = compute.FindKernel("KApplyMovingBucketProjectionBoundaryVelocity");
        }

        private bool HasValidKernels(FluidSolverContext context)
        {
            bool baseKernelsValid =
                _kernelClearGrid >= 0 &&
                _kernelP2G >= 0 &&
                _kernelGridUpdate >= 0 &&
                _kernelG2PVelocityApic >= 0 &&
                _kernelUpdateDeformation >= 0;

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
                    _kernelApplyMovingBucketProjectionBoundaryVelocity >= 0 &&
                    _kernelComputeProjectionDivergence >= 0 &&
                    _kernelJacobiProjectionPressure >= 0 &&
                    _kernelSubtractProjectionPressureGradient >= 0;

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
        }

        private void SetCommonParameters(FluidSolverContext context, SimulationContext simulationContext, FluidSolverStepInput stepInput)
        {
            ComputeShader compute = context.GpuMpmConfig.denseLocalMpmCompute;

            Vector3Int res = context.GpuMpmConfig.gridResolution;

            compute.SetInt("_ParticleCount", context.GpuBufferSet.UploadedParticleCount);
            compute.SetInt("_GridNodeCount", context.GpuMpmConfig.GridNodeCount);

            compute.SetInts("_GridResolution", res.x, res.y, res.z);
            compute.SetVector("_GridOrigin", context.GpuMpmConfig.gridOriginWorld);

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

            SetProjectionParameters(context);
        }

        private void BindClearGrid(ComputeShader compute)
        {
            compute.SetBuffer(_kernelClearGrid, "_GridAccumInt", _gridAccumIntBuffer);
            compute.SetBuffer(_kernelClearGrid, "_GridVelocityMass", _gridVelocityMassBuffer);
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
        }

        private void BindGridUpdate(ComputeShader compute)
        {
            compute.SetBuffer(_kernelGridUpdate, "_GridAccumInt", _gridAccumIntBuffer);
            compute.SetBuffer(_kernelGridUpdate, "_GridVelocityMass", _gridVelocityMassBuffer);
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
        }

        private int Groups(int count)
        {
            return Mathf.CeilToInt(count / 256.0f);
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

            bool hasHole =
                config.holes != null &&
                config.holes.Length > 0 &&
                config.holes[0] != null &&
                config.holes[0].active;

            compute.SetInt("_ClassifyBottomHoleRegion",
                context.GpuMpmConfig.classifyBottomHoleRegion && hasHole ? 1 : 0);

            compute.SetInt("_EnableBottomHoleOpening",
                context.GpuMpmConfig.enableBottomHoleOpening && hasHole ? 1 : 0);

            if (hasHole)
            {
                BucketHoleConfig hole = config.holes[0];

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
        }

        private void BindMarkBucketProjectionSolids(ComputeShader compute)
        {
            compute.SetBuffer(
                _kernelMarkBucketProjectionSolids,
                "_ProjectionCellType",
                _projectionCellTypeBuffer
            );
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

            if (context.GpuMpmConfig.enableProjectionDivergenceComputation)
            {
                BindComputeProjectionDivergence(compute);
                compute.Dispatch(_kernelComputeProjectionDivergence, Groups(gridNodeCount), 1, 1);
                dispatches++;
            }

            int pressureDispatches = RunJacobiPressureSolve(compute, context, gridNodeCount);

            dispatches += pressureDispatches;

            int pressureGradientDispatches = RunPressureGradientSubtraction(compute, context, gridNodeCount);

            dispatches += pressureGradientDispatches;

            return dispatches;
        }

        private void BindComputeProjectionDivergence(ComputeShader compute)
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
                "_ProjectionDivergence",
                _projectionDivergenceBuffer
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

            return 1;
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
    }
}