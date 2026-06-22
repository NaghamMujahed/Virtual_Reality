using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Jobs;
using PaintBucketSim.Runtime;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System.Diagnostics;

namespace PaintBucketSim.Systems.Fluid.Solvers
{
    public sealed class CpuPbfFluidSolver : IFluidSolver
    {
        private NativeParallelMultiHashMap<int, int> _fluidHashMap;
        private NativeParallelMultiHashMap<int, int> _boundaryHashMap;

        private FluidSolverStats _stats;
        private readonly Stopwatch _stepWatch = new Stopwatch();
        private readonly Stopwatch _sectionWatch = new Stopwatch();

        public FluidSolverType SolverType => FluidSolverType.CpuPbf;
        public bool IsInitialized { get; private set; }
        public FluidSolverStats Stats => _stats;

        public void Initialize(FluidSolverContext context)
        {
            DisposeHashMaps();

            if (context == null || !context.IsValid)
            {
                _stats.status = FluidSolverStatus.Error;
                IsInitialized = false;
                UnityEngine.Debug.LogError("CpuPbfFluidSolver: Invalid solver context.");
                return;
            }

            AllocateHashMaps(context);

            _stats = new FluidSolverStats
            {
                solverType = FluidSolverType.CpuPbf,
                status = FluidSolverStatus.Running,
                particleCount = context.Particles.Count,
                solverIterations = context.PbfConfig.solverIterations
            };

            IsInitialized = true;
        }

        public void Reset(FluidSolverContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            DisposeHashMaps();
            IsInitialized = false;
            _stats.status = FluidSolverStatus.NotInitialized;
        }

        public void Step(
            FluidSolverContext solverContext,
            SimulationContext simulationContext,
            FluidSolverStepInput stepInput)
        {
            if (!IsInitialized || solverContext == null || !solverContext.IsValid)
                return;

            FluidParticleData data = solverContext.Particles;

            float h = solverContext.PbfConfig.smoothingRadiusMeters;
            float restDensity = solverContext.MaterialConfig.densityKgPerM3;
            float dt = stepInput.dt;

            _stats.fluidHashBuilds = 0;
            _stats.boundaryHashBuilds = 0;
            _stats.lastDensitySolveMilliseconds = 0.0f;
            _stats.lastCorrectionMilliseconds = 0.0f;
            _stats.lastBoundaryMilliseconds = 0.0f;
            _stats.lastViscosityMilliseconds = 0.0f;

            _stepWatch.Restart();

            var predictJob = new PbfPredictJob
            {
                dt = dt,
                gravity = (float3)simulationContext.EnvironmentState.gravity * stepInput.gravityScale,

                positions = data.Positions,
                previousPositions = data.PreviousPositions,
                velocities = data.Velocities,
                deltaPositions = data.DeltaPositions
            };

            JobHandle handle = predictJob.Schedule(data.Count, 64);
            handle.Complete();

            BuildFluidHash(solverContext, h);

            bool hasBoundary =
                solverContext.PbfConfig.useBoundaryParticleCollision &&
                solverContext.BoundarySystem != null &&
                solverContext.BoundarySystem.IsInitialized;

            if (hasBoundary)
                BuildBoundaryHash(solverContext, h);

            for (int iter = 0; iter < solverContext.PbfConfig.solverIterations; iter++)
            {
                if (solverContext.PbfConfig.rebuildFluidHashEveryIteration && iter > 0)
                    BuildFluidHash(solverContext, h);

                _sectionWatch.Restart();

                var densityJob = new PbfDensityLambdaJob
                {
                    smoothingRadius = h,
                    restDensity = restDensity,
                    lambdaEpsilon = solverContext.PbfConfig.lambdaEpsilon,
                    cellSize = h,

                    positions = data.Positions,
                    masses = data.Masses,
                    fluidHashMap = _fluidHashMap,

                    densities = data.Densities,
                    lambdas = data.Lambdas
                };

                handle = densityJob.Schedule(data.Count, 64);
                handle.Complete();

                _sectionWatch.Stop();
                _stats.lastDensitySolveMilliseconds += (float)_sectionWatch.Elapsed.TotalMilliseconds;

                _sectionWatch.Restart();

                var correctionJob = new PbfPositionCorrectionJob
                {
                    smoothingRadius = h,
                    restDensity = restDensity,
                    cellSize = h,

                    enableArtificialPressure = solverContext.PbfConfig.enableArtificialPressure,
                    artificialPressureK = solverContext.PbfConfig.artificialPressureK,
                    artificialPressureN = solverContext.PbfConfig.artificialPressureN,
                    artificialPressureDeltaQRatio = solverContext.PbfConfig.artificialPressureDeltaQRatio,

                    maxPositionCorrection = solverContext.PbfConfig.maxPositionCorrectionPerIteration,

                    positions = data.Positions,
                    masses = data.Masses,
                    lambdas = data.Lambdas,
                    fluidHashMap = _fluidHashMap,

                    deltaPositions = data.DeltaPositions
                };

                handle = correctionJob.Schedule(data.Count, 64);

                var applyJob = new PbfApplyDeltaJob
                {
                    positions = data.Positions,
                    deltaPositions = data.DeltaPositions
                };

                handle = applyJob.Schedule(data.Count, 64, handle);
                handle.Complete();

                _sectionWatch.Stop();
                _stats.lastCorrectionMilliseconds += (float)_sectionWatch.Elapsed.TotalMilliseconds;

                if (hasBoundary)
                {
                    _sectionWatch.Restart();

                    var boundaryJob = new BoundaryParticleCollisionJob
                    {
                        cellSize = h,
                        smoothingRadius = h,
                        boundaryRadiusMultiplier = solverContext.PbfConfig.boundaryParticleRadiusMultiplier,
                        strength = solverContext.PbfConfig.boundaryCollisionStrength,

                        boundaryPositions = solverContext.BoundarySystem.WorldPositionsNative,
                        boundaryNormals = solverContext.BoundarySystem.WorldNormalsNative,
                        boundaryVelocities = solverContext.BoundarySystem.WorldVelocitiesNative,
                        boundaryHashMap = _boundaryHashMap,

                        positions = data.Positions,
                        previousPositions = data.PreviousPositions,
                        velocities = data.Velocities,
                        radii = data.Radii,

                        preventCollisionEnergyInjection =
                            solverContext.PbfConfig.preventProjectionEnergyInjection
                    };

                    handle = boundaryJob.Schedule(data.Count, 64);
                    handle.Complete();

                    _sectionWatch.Stop();
                    _stats.lastBoundaryMilliseconds += (float)_sectionWatch.Elapsed.TotalMilliseconds;
                }

                if (solverContext.PbfConfig.useAnalyticBucketProjection)
                {
                    ScheduleAnalyticProjection(solverContext).Complete();
                }
            }

            var velocityJob = new PbfVelocityUpdateJob
            {
                dt = dt,
                dampingPerSecond = solverContext.PbfConfig.velocityDampingPerSecond,
                maxSpeed = solverContext.PbfConfig.maxParticleSpeed,

                positions = data.Positions,
                previousPositions = data.PreviousPositions,
                velocities = data.Velocities
            };

            handle = velocityJob.Schedule(data.Count, 64);
            handle.Complete();

            if (solverContext.PbfConfig.enableXsphViscosity &&
                solverContext.PbfConfig.xsphStrength > 0.0f)
            {
                _sectionWatch.Restart();

                if (solverContext.PbfConfig.rebuildFluidHashEveryIteration)
                    BuildFluidHash(solverContext, h);

                var xsphJob = new XsphViscosityJob
                {
                    smoothingRadius = h,
                    cellSize = h,
                    xsphStrength = solverContext.PbfConfig.xsphStrength,
                    maxVelocityChange = solverContext.PbfConfig.maxXsphVelocityChange,

                    positions = data.Positions,
                    velocities = data.Velocities,
                    fluidHashMap = _fluidHashMap,

                    outputVelocities = data.TempVelocities
                };

                handle = xsphJob.Schedule(data.Count, 64);

                var copyJob = new CopyFloat3ArrayJob
                {
                    source = data.TempVelocities,
                    destination = data.Velocities
                };

                handle = copyJob.Schedule(data.Count, 64, handle);
                handle.Complete();

                _sectionWatch.Stop();
                _stats.lastViscosityMilliseconds = (float)_sectionWatch.Elapsed.TotalMilliseconds;
            }

            _stepWatch.Stop();

            _stats.status = FluidSolverStatus.Running;
            _stats.particleCount = data.Count;
            _stats.solverIterations = solverContext.PbfConfig.solverIterations;
            _stats.lastStepMilliseconds = (float)_stepWatch.Elapsed.TotalMilliseconds;
            _stats.usedBoundaryParticles = hasBoundary;
            _stats.usedAnalyticProjection = solverContext.PbfConfig.useAnalyticBucketProjection;
            _stats.usedXsphViscosity = solverContext.PbfConfig.enableXsphViscosity;
        }

        private void AllocateHashMaps(FluidSolverContext context)
        {
            int fluidCapacity = Mathf.CeilToInt(
                context.FluidConfig.maxParticleCapacity *
                context.PbfConfig.hashCapacityMultiplier
            );

            _fluidHashMap = new NativeParallelMultiHashMap<int, int>(
                Mathf.Max(1, fluidCapacity),
                Allocator.Persistent
            );

            int boundaryCapacity = 8192;

            if (context.BoundarySystem != null && context.BoundarySystem.IsInitialized)
                boundaryCapacity = Mathf.Max(1, context.BoundarySystem.Count * 2);

            _boundaryHashMap = new NativeParallelMultiHashMap<int, int>(
                boundaryCapacity,
                Allocator.Persistent
            );
        }

        private void DisposeHashMaps()
        {
            if (_fluidHashMap.IsCreated)
                _fluidHashMap.Dispose();

            if (_boundaryHashMap.IsCreated)
                _boundaryHashMap.Dispose();
        }

        private void BuildFluidHash(FluidSolverContext context, float cellSize)
        {
            _fluidHashMap.Clear();

            var job = new BuildFluidHashJob
            {
                cellSize = cellSize,
                positions = context.Particles.Positions,
                hashMap = _fluidHashMap.AsParallelWriter()
            };

            JobHandle handle = job.Schedule(context.Particles.Count, 64);
            handle.Complete();

            _stats.fluidHashBuilds++;
        }

        private void BuildBoundaryHash(FluidSolverContext context, float cellSize)
        {
            if (context.BoundarySystem == null || !context.BoundarySystem.IsInitialized)
                return;

            if (_boundaryHashMap.Capacity < context.BoundarySystem.Count)
            {
                _boundaryHashMap.Dispose();

                _boundaryHashMap = new NativeParallelMultiHashMap<int, int>(
                    Mathf.Max(1, context.BoundarySystem.Count * 2),
                    Allocator.Persistent
                );
            }

            _boundaryHashMap.Clear();

            var job = new BuildBoundaryHashJob
            {
                cellSize = cellSize,
                boundaryPositions = context.BoundarySystem.WorldPositionsNative,
                hashMap = _boundaryHashMap.AsParallelWriter()
            };

            JobHandle handle = job.Schedule(context.BoundarySystem.Count, 64);
            handle.Complete();

            _stats.boundaryHashBuilds++;
        }

        private JobHandle ScheduleAnalyticProjection(FluidSolverContext context)
        {
            var bucket = context.BucketSystem;
            var config = bucket.Config;
            var state = bucket.State;

            float holeRadius = 0.0f;

            if (bucket.Holes != null && bucket.Holes.Length > 0)
                holeRadius = bucket.Holes[0].radius;

            var job = new AnalyticBucketProjectionJob
            {
                bucketPosition = state.position,
                bucketRotation = state.rotation,
                inverseBucketRotation = math.inverse(state.rotation),

                height = config.heightMeters,
                topRadius = config.topRadiusMeters,
                bottomRadius = config.bottomRadiusMeters,
                wallThickness = config.wallThicknessMeters,

                shapeType = (int)config.shapeType,

                projectionStrength = context.PbfConfig.analyticProjectionStrength,

                holeRadius = holeRadius,
                nearHoleHeight = context.PbfConfig.nearHoleHeightMeters,
                nearHolePadding = context.PbfConfig.nearHoleRadialPaddingMeters,

                topBoundaryMode = (int)context.PbfConfig.topBoundaryMode,
                topBoundaryPadding = context.PbfConfig.topBoundaryPaddingMeters,
                preventProjectionEnergyInjection =
                    context.PbfConfig.preventProjectionEnergyInjection,

                positions = context.Particles.Positions,
                previousPositions = context.Particles.PreviousPositions,
                radii = context.Particles.Radii,
                states = context.Particles.States
            };

            return job.Schedule(context.Particles.Count, 64);
        }
    }
}