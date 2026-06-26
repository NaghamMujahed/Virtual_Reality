using System.Collections.Generic;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Fluid.GPU;
using PaintBucketSim.Systems.Fluid.Solvers;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using PaintBucketSim.Jobs;
using Unity.Jobs;

namespace PaintBucketSim.Systems.Fluid
{
    public class PaintFluidSystem : MonoBehaviour
    {
        [Header("Architecture")]
        [SerializeField] private FluidSolverArchitectureConfig architectureConfig;

        [Header("Configs")]
        [SerializeField] private PaintMaterialConfig paintMaterialConfig;
        [SerializeField] private PaintFluidConfig paintFluidConfig;
        [SerializeField] private PbfSolverConfig pbfSolverConfig;
        
        [Header("GPU Solver")]
        [SerializeField] private GpuMpmSolverConfig gpuMpmSolverConfig;
        [SerializeField] private GpuFluidBufferSet gpuFluidBufferSet;

        [Header("Systems")]
        [SerializeField] private BucketSystem bucketSystem;
        [SerializeField] private BoundarySystem boundarySystem;

        private FluidParticleData _data;
        private FluidSolverContext _solverContext;
        private IFluidSolver _activeSolver;

        private bool _initialized;
        private int _solverStepIndex;

        public PaintMaterialConfig MaterialConfig => paintMaterialConfig;
        public PaintFluidConfig FluidConfig => paintFluidConfig;
        public PbfSolverConfig PbfConfig => pbfSolverConfig;
        public GpuMpmSolverConfig GpuMpmConfig => gpuMpmSolverConfig;
        public FluidSolverArchitectureConfig ArchitectureConfig => architectureConfig;

        public bool IsInitialized => _initialized && _data != null && _data.IsCreated;

        public int ParticleCount => IsInitialized ? _data.Count : 0;
        public int Capacity => IsInitialized ? _data.Capacity : 0;

        public FluidSolverStats SolverStats =>
            _activeSolver != null ? _activeSolver.Stats : default;

        public bool IsGpuSolverActive =>
            _activeSolver != null &&
            _activeSolver.SolverType == FluidSolverType.GpuSparseMpmPrototype;

        private FluidParticlePoolStats _poolStats;
        public FluidParticlePoolStats PoolStats => _poolStats;

        private FluidCalibrationStats _calibrationStats;
        public FluidCalibrationStats CalibrationStats => _calibrationStats;

        public FluidDiagnostics Diagnostics
        {
            get
            {
                if (!IsInitialized || !_data.Diagnostics.IsCreated)
                    return default;

                return _data.Diagnostics[0];
            }
        }

        private void Awake()
        {
            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            if (boundarySystem == null)
                boundarySystem = FindFirstObjectByType<BoundarySystem>();

            if (gpuFluidBufferSet == null)
                gpuFluidBufferSet = FindFirstObjectByType<GpuFluidBufferSet>();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize(SimulationContext context)
        {
            if (!ValidateRequiredReferences())
            {
                _initialized = false;
                return;
            }

            DisposeSolverOnly();

            if (_data == null)
                _data = new FluidParticleData();

            _data.Allocate(paintFluidConfig.maxParticleCapacity, Allocator.Persistent);

            GenerateParticlesInsideBucket();

            _initialized = true;
            _solverStepIndex = 0;

            BuildSolverContext();
            CreateAndInitializeSolver();

            UpdateDiagnostics();
            UpdatePoolStats();

            if (pbfSolverConfig.enableWarmupOnInitialize &&
                pbfSolverConfig.enablePbf &&
                _activeSolver != null)
            {
                RunWarmup(context);
            }

            UpdateDiagnostics();
            UpdatePoolStats();
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            DisposeSolverOnly();

            if (_data != null)
            {
                _data.Dispose();
                _data = null;
            }

            _initialized = false;
        }

        private void DisposeSolverOnly()
        {
            if (_activeSolver != null)
            {
                _activeSolver.Dispose();
                _activeSolver = null;
            }
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized)
                return;

            if (_activeSolver == null || !_activeSolver.IsInitialized)
                return;

            // If PBF is disabled while CPU PBF is selected, keep the old preview behavior.
            if (_activeSolver.SolverType == FluidSolverType.CpuPbf &&
                pbfSolverConfig != null &&
                !pbfSolverConfig.enablePbf)
            {
                if (paintFluidConfig.previewMode == FluidPreviewMode.FollowBucketKinematically)
                {
                    UpdateWorldFromLocalPreview();

                    StepParticlePool(dt);
                }

                UpdateDiagnostics();
                UpdatePoolStats();

                return;
            }

            var input = new FluidSolverStepInput
            {
                dt = dt,
                gravityScale = pbfSolverConfig.fluidGravityScale,
                isWarmup = false
            };

            _activeSolver.Step(_solverContext, context, input);

            StepParticlePool(dt);

            _solverStepIndex++;

            if (_solverStepIndex % pbfSolverConfig.diagnosticsUpdateInterval == 0)
            {
                UpdateDiagnostics();

                UpdatePoolStats();
            }
        }

        private bool ValidateRequiredReferences()
        {
            if (architectureConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing FluidSolverArchitectureConfig.");
                return false;
            }

            if (paintMaterialConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PaintMaterialConfig.");
                return false;
            }

            if (paintFluidConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PaintFluidConfig.");
                return false;
            }

            if (pbfSolverConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PbfSolverConfig.");
                return false;
            }

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            if (boundarySystem == null)
                boundarySystem = FindFirstObjectByType<BoundarySystem>();

            if (bucketSystem == null || !bucketSystem.IsInitialized)
            {
                Debug.LogError("PaintFluidSystem: Missing initialized BucketSystem.");
                return false;
            }

            return true;
        }

        private void BuildSolverContext()
        {
            _solverContext = new FluidSolverContext
            {
                MaterialConfig = paintMaterialConfig,
                FluidConfig = paintFluidConfig,
                PbfConfig = pbfSolverConfig,
                ArchitectureConfig = architectureConfig,

                GpuMpmConfig = gpuMpmSolverConfig,
                GpuBufferSet = gpuFluidBufferSet,

                BucketSystem = bucketSystem,
                BoundarySystem = boundarySystem,

                Particles = _data
            };
        }

        private void CreateAndInitializeSolver()
        {
            FluidSolverType requestedSolver = architectureConfig.activeSolver;

            if (requestedSolver == FluidSolverType.CpuPbf)
            {
                _activeSolver = new CpuPbfFluidSolver();
            }
            else if (requestedSolver == FluidSolverType.GpuSparseMpmPrototype)
            {
                bool gpuReady =
                    gpuMpmSolverConfig != null &&
                    gpuMpmSolverConfig.denseLocalMpmCompute != null &&
                    gpuFluidBufferSet != null;

                if (gpuReady)
                {
                    _activeSolver = new GpuMpmDenseLocalSolver();
                }
                else if (architectureConfig.fallbackToCpuPbfIfSelectedSolverUnavailable)
                {
                    _activeSolver = new CpuPbfFluidSolver();

                    if (architectureConfig.logSolverLifecycle)
                    {
                        Debug.LogWarning(
                            "PaintFluidSystem: GPU MPM selected, but GPU config/buffers/compute are missing. " +
                            "Falling back to CpuPbfSolver."
                        );
                    }
                }
                else
                {
                    _activeSolver = new GpuMpmDenseLocalSolver();
                }
            }
            else
            {
                _activeSolver = new CpuPbfFluidSolver();
            }

            if (architectureConfig.logSolverLifecycle)
            {
                Debug.Log($"PaintFluidSystem: Created solver {_activeSolver.SolverType}");
            }

            _activeSolver.Initialize(_solverContext);
        }

        private void RunWarmup(SimulationContext context)
        {
            int steps = Mathf.Max(0, pbfSolverConfig.warmupSteps);
            float dt = 1.0f / 90.0f;

            for (int i = 0; i < steps; i++)
            {
                var input = new FluidSolverStepInput
                {
                    dt = dt,
                    gravityScale = pbfSolverConfig.warmupGravityScale,
                    isWarmup = true
                };

                _activeSolver.Step(_solverContext, context, input);
            }
        }

        public Vector3 GetParticlePosition(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return Vector3.zero;

            float3 p = _data.Positions[index];
            return new Vector3(p.x, p.y, p.z);
        }

        public float GetParticleRadius(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return 0.01f;

            return _data.Radii[index];
        }

        public Color GetParticleColor(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return Color.white;

            float4 c = _data.Colors[index];
            return new Color(c.x, c.y, c.z, c.w);
        }

        public FluidParticleState GetParticleState(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return FluidParticleState.Lost;

            return (FluidParticleState)_data.States[index];
        }

        private void GenerateParticlesInsideBucket()
        {
            BucketConfig bucketConfig = bucketSystem.Config;

            _data.ClearActive();

            float fillFraction = Mathf.Clamp01(paintFluidConfig.fillFraction01);
            float targetPaintVolume = ComputeBucketFillVolumeM3(fillFraction);

            int targetCount = Mathf.Max(1, paintFluidConfig.targetParticleCount);

            float restDensity = Mathf.Max(paintMaterialConfig.densityKgPerM3, 1.0f);
            float restVolume = targetPaintVolume / targetCount;
            float mass = restDensity * restVolume;

            float estimatedSpacing =
                paintFluidConfig.initializationMode == FluidInitializationMode.ManualSpacing
                    ? Mathf.Max(paintFluidConfig.manualParticleSpacingMeters, 0.005f)
                    : EstimateSpacingFromVolume(restVolume);
            float radius =
                estimatedSpacing *
                Mathf.Clamp(paintFluidConfig.particleRadiusToSpacing, 0.25f, 0.65f);

            //float spacing = ComputeParticleSpacing(bucketConfig);
            //float radius = spacing * paintFluidConfig.particleRadiusToSpacing;

            float halfHeight = bucketConfig.heightMeters * 0.5f;
            float yMin = -halfHeight + paintFluidConfig.wallClearanceMeters + radius;

            float yMax = Mathf.Lerp(
                -halfHeight,
                halfHeight,
                paintFluidConfig.fillFraction01
            );

            yMax -= paintFluidConfig.wallClearanceMeters + radius;

            if (yMax <= yMin)
                yMax = yMin + estimatedSpacing;

            //float particleVolume = estimatedSpacing * estimatedSpacing * estimatedSpacing;
            //float particleMass = paintMaterialConfig.densityKgPerM3 * particleVolume;

            Color color = paintMaterialConfig.baseColor;
            float4 color4 = new float4(color.r, color.g, color.b, color.a);

            for (float y = yMin; y <= yMax; y += estimatedSpacing)
            {
                float t = Mathf.InverseLerp(-halfHeight, halfHeight, y);

                float innerRadius =
                    GetInnerRadiusAtT(bucketConfig, t) -
                    paintFluidConfig.wallClearanceMeters -
                    radius;

                if (innerRadius <= 0.0f)
                    continue;

                for (float x = -innerRadius; x <= innerRadius; x += estimatedSpacing)
                {
                    for (float z = -innerRadius; z <= innerRadius; z += estimatedSpacing)
                    {
                        if (!_data.HasFreeSlot())
                            break;

                        if (x * x + z * z > innerRadius * innerRadius)
                            continue;

                        float3 local = new float3(x, y, z);

                        if (IsTooCloseToBottomHole(bucketConfig, local, radius))
                            continue;

                        float3 world = bucketSystem.LocalToWorldPoint(local);

                        _data.SpawnParticle(
                            world,
                            float3.zero,
                            mass,
                            radius,
                            restVolume,
                            restDensity,
                            color4,
                            FluidParticleState.InsideFluid
                        );
                    }

                    if (!_data.HasFreeSlot())
                        break;
                }

                if (!_data.HasFreeSlot())
                    break;
            }

            //float estimatedFillVolume = EstimateFillVolume(bucketConfig);

            //_data.Diagnostics[0] = new FluidDiagnostics
            //{
            //    particleCount = _data.Count,
            //    capacity = _data.Capacity,
            //    particleRadius = radius,
            //    particleSpacing = spacing,
            //    totalMass = particleMass * _data.Count,
            //    insideMass = particleMass * _data.Count,
            //    fillFraction = paintFluidConfig.fillFraction01,
            //    estimatedFillVolume = estimatedFillVolume,
            //    centerOfMassWorld = float3.zero
            //};
            
            targetPaintVolume = ComputeBucketFillVolumeM3(
                Mathf.Clamp01(paintFluidConfig.fillFraction01)
            );

            RecalibrateGeneratedParticles(targetPaintVolume, restDensity);
        }

        private void UpdateWorldFromLocalPreview()
        {
            for (int i = 0; i < _data.Count; i++)
            {
                float3 local = _data.LocalPositions[i];

                float3 world = bucketSystem.LocalToWorldPoint(local);
                float3 velocity = bucketSystem.GetWorldPointVelocity(world);

                _data.PreviousPositions[i] = _data.Positions[i];
                _data.Positions[i] = world;
                _data.Velocities[i] = velocity;
            }
        }

        private void UpdateDiagnostics()
        {
            if (!IsInitialized)
                return;

            float totalMass = 0.0f;
            float insideMass = 0.0f;
            float airborneMass = 0.0f;
            float depositedMass = 0.0f;
            float lostMass = 0.0f;

            float3 weightedPositionSum = float3.zero;

            for (int i = 0; i < _data.Count; i++)
            {
                float m = _data.Masses[i];
                totalMass += m;

                FluidParticleState state = (FluidParticleState)_data.States[i];

                if (FluidParticleStateUtility.IsFluidSolverState(state))
                {
                    insideMass += m;
                    weightedPositionSum += _data.Positions[i] * m;
                }
                else if (FluidParticleStateUtility.IsAirState(state))
                {
                    airborneMass += m;
                }
                else if (FluidParticleStateUtility.IsCanvasState(state))
                {
                    depositedMass += m;
                }
                else if (state == FluidParticleState.Lost)
                {
                    lostMass += m;
                }
            }

            float3 centerOfMass = insideMass > 1e-8f
                ? weightedPositionSum / insideMass
                : float3.zero;

            FluidDiagnostics d = _data.Diagnostics[0];

            d.particleCount = _data.Count;
            d.capacity = _data.Capacity;
            d.totalMass = totalMass;
            d.insideMass = insideMass;
            d.airborneMass = airborneMass;
            d.depositedMass = depositedMass;
            d.lostMass = lostMass;
            d.centerOfMassWorld = centerOfMass;

            _data.Diagnostics[0] = d;
        }

        private float ComputeParticleSpacing(BucketConfig bucketConfig)
        {
            if (paintFluidConfig.initializationMode == FluidInitializationMode.ManualSpacing)
                return paintFluidConfig.manualParticleSpacingMeters;

            float volume = Mathf.Max(EstimateFillVolume(bucketConfig), 1e-6f);
            int target = Mathf.Max(1, paintFluidConfig.targetParticleCount);

            float spacing = Mathf.Pow(volume / target, 1.0f / 3.0f);
            spacing *= 0.92f;

            return Mathf.Max(spacing, 0.005f);
        }

        private float EstimateFillVolume(BucketConfig bucketConfig)
        {
            float h = bucketConfig.heightMeters;
            float fillH = Mathf.Clamp01(paintFluidConfig.fillFraction01) * h;

            float tFill = paintFluidConfig.fillFraction01;

            float r0 = GetInnerRadiusAtT(bucketConfig, 0.0f);
            float r1 = GetInnerRadiusAtT(bucketConfig, tFill);

            float volume =
                Mathf.PI *
                fillH *
                (r0 * r0 + r0 * r1 + r1 * r1) /
                3.0f;

            return Mathf.Max(volume, 0.0f);
        }

        private float GetInnerRadiusAtT(BucketConfig bucketConfig, float t)
        {
            float outerRadius = Mathf.Lerp(
                bucketConfig.bottomRadiusMeters,
                bucketConfig.topRadiusMeters,
                t
            );

            if (bucketConfig.shapeType == BucketShapeType.Cylinder)
                outerRadius = bucketConfig.topRadiusMeters;

            float innerRadius =
                outerRadius -
                Mathf.Max(bucketConfig.wallThicknessMeters, 0.0f);

            return Mathf.Max(0.001f, innerRadius);
        }

        private bool IsTooCloseToBottomHole(
            BucketConfig bucketConfig,
            float3 localPoint,
            float particleRadius)
        {
            if (bucketConfig.holes == null)
                return false;

            for (int i = 0; i < bucketConfig.holes.Length; i++)
            {
                BucketHoleConfig hole = bucketConfig.holes[i];
                if (hole == null || !hole.active)
                    continue;

                Vector3 normal = bucketConfig.GetResolvedHoleLocalNormal(hole);
                if (Vector3.Dot(normal.normalized, Vector3.down) < 0.8f)
                    continue;

                Vector3 centerV = bucketConfig.GetResolvedHoleLocalCenter(hole);
                Vector3 tangentV = bucketConfig.GetResolvedHoleLocalTangent(hole);
                Vector3 bitangentV = bucketConfig.GetResolvedHoleLocalBitangent(hole);
                Vector2 halfExtents = bucketConfig.GetResolvedHoleHalfExtents(hole);

                Vector3 deltaV = new Vector3(
                    localPoint.x - centerV.x,
                    localPoint.y - centerV.y,
                    localPoint.z - centerV.z
                );

                float u = Vector3.Dot(deltaV, tangentV);
                float v = Vector3.Dot(deltaV, bitangentV);

                float clearance =
                    paintFluidConfig.holeClearanceMeters +
                    particleRadius;

                if (IsPointInsideHoleFootprint(
                        hole,
                        u,
                        v,
                        halfExtents,
                        clearance))
                {
                    float bottomY = -bucketConfig.heightMeters * 0.5f;
                    if (localPoint.y < bottomY + clearance * 1.5f)
                        return true;
                }
            }

            return false;
        }

        private static bool IsPointInsideHoleFootprint(
            BucketHoleConfig hole,
            float u,
            float v,
            Vector2 halfExtents,
            float padding)
        {
            float a = Mathf.Max(halfExtents.x + padding, 0.001f);
            float b = Mathf.Max(halfExtents.y + padding, 0.001f);

            switch (hole.shape)
            {
                case BucketHoleShape.Square:
                case BucketHoleShape.Rectangle:
                    return Mathf.Abs(u) <= a && Mathf.Abs(v) <= b;

                case BucketHoleShape.Slot:
                {
                    float halfLength = Mathf.Max(a, b);
                    float radius = Mathf.Min(a, b);
                    float segmentHalfLength = Mathf.Max(0.0f, halfLength - radius);
                    float du = Mathf.Abs(u) - segmentHalfLength;
                    du = Mathf.Max(du, 0.0f);
                    return du * du + v * v <= radius * radius;
                }

                case BucketHoleShape.Ellipse:
                case BucketHoleShape.Circular:
                default:
                {
                    float nx = u / a;
                    float ny = v / b;
                    return nx * nx + ny * ny <= 1.0f;
                }
            }
        }

        private void StepParticlePool(float dt)
        {
            if (!IsInitialized)
                return;

            var ageJob = new ParticleAgeUpdateJob
            {
                dt = dt,
                states = _data.States,
                ages = _data.Ages,
                stateAges = _data.StateAges
            };

            JobHandle handle = ageJob.Schedule(_data.Count, 64);
            handle.Complete();
        }

        private void UpdatePoolStats()
        {
            if (!IsInitialized)
            {
                _poolStats = default;
                return;
            }

            int inside = 0;
            int nearBoundary = 0;
            int nearHole = 0;

            int jet = 0;
            int emitted = 0;
            int airborne = 0;
            int spilled = 0;

            int deposited = 0;
            int absorbed = 0;
            int lost = 0;

            for (int i = 0; i < _data.Count; i++)
            {
                FluidParticleState state = (FluidParticleState)_data.States[i];

                switch (state)
                {
                    case FluidParticleState.InsideFluid:
                        inside++;
                        break;

                    case FluidParticleState.NearBoundary:
                        nearBoundary++;
                        break;

                    case FluidParticleState.NearHole:
                        nearHole++;
                        break;

                    case FluidParticleState.Jet:
                        jet++;
                        break;

                    case FluidParticleState.Emitted:
                        emitted++;
                        break;

                    case FluidParticleState.Airborne:
                        airborne++;
                        break;

                    case FluidParticleState.Spilled:
                        spilled++;
                        break;

                    case FluidParticleState.Deposited:
                        deposited++;
                        break;

                    case FluidParticleState.Absorbed:
                        absorbed++;
                        break;

                    case FluidParticleState.Lost:
                        lost++;
                        break;
                }
            }

            _poolStats = new FluidParticlePoolStats
            {
                activeCount = _data.Count,
                capacity = _data.Capacity,
                inactiveCount = _data.Capacity - _data.Count,

                insideFluidCount = inside,
                nearBoundaryCount = nearBoundary,
                nearHoleCount = nearHole,

                jetCount = jet,
                emittedCount = emitted,
                airborneCount = airborne,
                spilledCount = spilled,

                depositedCount = deposited,
                absorbedCount = absorbed,
                lostCount = lost,

                poolUsage01 = _data.Capacity > 0
                    ? (float)_data.Count / _data.Capacity
                    : 0.0f,

                nextParticleId = _data.NextParticleId
            };
        }

        public int CopyParticleRenderData(Vector4[] positionRadiusOutput, Vector4[] colorOutput, int maxCount, int stride)
        {
            if (!IsInitialized ||
                positionRadiusOutput == null ||
                colorOutput == null ||
                maxCount <= 0)
            {
                return 0;
            }

            stride = Mathf.Max(1, stride);

            int writableCount = Mathf.Min(
                maxCount,
                Mathf.Min(positionRadiusOutput.Length, colorOutput.Length)
            );

            int written = 0;

            for (int i = 0; i < _data.Count && written < writableCount; i += stride)
            {
                FluidParticleState state = (FluidParticleState)_data.States[i];

                if (state == FluidParticleState.Inactive ||
                    state == FluidParticleState.Lost ||
                    state == FluidParticleState.Absorbed)
                {
                    continue;
                }

                float3 p = _data.Positions[i];
                float4 c = _data.Colors[i];

                positionRadiusOutput[written] = new Vector4(
                    p.x,
                    p.y,
                    p.z,
                    _data.Radii[i]
                );

                colorOutput[written] = new Vector4(
                    c.x,
                    c.y,
                    c.z,
                    c.w
                );

                written++;
            }

            return written;
        }

        ////////////////    G6.A Changes   //////////////////

        public int CopyParticleGpuData(
            Vector4[] positionRadiusOutput,
            Vector4[] velocityMassOutput,
            Vector4[] colorOutput,
            Vector4[] stateAgeIdOutput,
            Vector4[] volumeJOutput,
            Vector4[] deformationF0Output,
            Vector4[] deformationF1Output,
            Vector4[] deformationF2Output,
            int maxCount,
            int stride)
        {
            if (!IsInitialized ||
                positionRadiusOutput == null ||
                velocityMassOutput == null ||
                colorOutput == null ||
                stateAgeIdOutput == null ||
                volumeJOutput == null ||
                deformationF0Output == null ||
                deformationF1Output == null ||
                deformationF2Output == null ||
                maxCount <= 0)
            {
                return 0;
            }

            stride = Mathf.Max(1, stride);

            int writableCount = Mathf.Min(
                maxCount,
                Mathf.Min(
                    Mathf.Min(positionRadiusOutput.Length, velocityMassOutput.Length),
                    Mathf.Min(colorOutput.Length, stateAgeIdOutput.Length)
                )
            );

            writableCount = Mathf.Min(
                writableCount,
                Mathf.Min(
                    Mathf.Min(volumeJOutput.Length, deformationF0Output.Length),
                    Mathf.Min(deformationF1Output.Length, deformationF2Output.Length)
                )
            );

            int written = 0;

            for (int i = 0; i < _data.Count && written < writableCount; i += stride)
            {
                FluidParticleState state = (FluidParticleState)_data.States[i];

                if (state == FluidParticleState.Inactive ||
                    state == FluidParticleState.Lost ||
                    state == FluidParticleState.Absorbed)
                {
                    continue;
                }

                float3 p = _data.Positions[i];
                float3 v = _data.Velocities[i];
                float4 c = _data.Colors[i];

                float mass = _data.Masses[i];
                //float restDensity = Mathf.Max(paintMaterialConfig.densityKgPerM3, 1.0f);
                //float restVolume = mass / restDensity;

                positionRadiusOutput[written] = new Vector4(
                    p.x,
                    p.y,
                    p.z,
                    _data.Radii[i]
                );

                velocityMassOutput[written] = new Vector4(
                    v.x,
                    v.y,
                    v.z,
                    mass
                );

                colorOutput[written] = new Vector4(
                    c.x,
                    c.y,
                    c.z,
                    c.w
                );

                stateAgeIdOutput[written] = new Vector4(
                    _data.States[i],
                    _data.Ages[i],
                    _data.StateAges[i],
                    _data.ParticleIds[i]
                );

                //// x = rest volume, y = current J, z = rest density, w = reserved
                //volumeJOutput[written] = new Vector4(
                //    restVolume,
                //    1.0f,
                //    restDensity,
                //    0.0f
                //);

                float restVolume = _data.RestVolumes[i];
                float restDensity = _data.RestDensities[i];

                if (restVolume <= 0.0f)
                {
                    restDensity = Mathf.Max(paintMaterialConfig.densityKgPerM3, 1.0f);
                    restVolume = _data.Masses[i] / restDensity;
                }

                volumeJOutput[written] = new Vector4(
                    restVolume,
                    1.0f,
                    restDensity,
                    0.0f
                );
                // Initial deformation gradient F = Identity.
                                deformationF0Output[written] = new Vector4(1, 0, 0, 0);
                deformationF1Output[written] = new Vector4(0, 1, 0, 0);
                deformationF2Output[written] = new Vector4(0, 0, 1, 0);

                written++;
            }

            return written;
        }

        ////////////////    End G6.A Changes   //////////////////

        private float ComputeBucketInnerVolumeM3()
        {
            if (bucketSystem == null || bucketSystem.Config == null)
                return 0.0f;

            var config = bucketSystem.Config;

            float height = Mathf.Max(config.heightMeters, 0.0f);
            float wall = Mathf.Max(config.wallThicknessMeters, 0.0f);

            float topInnerRadius = Mathf.Max(config.topRadiusMeters - wall, 0.001f);
            float bottomInnerRadius = Mathf.Max(config.bottomRadiusMeters - wall, 0.001f);

            // Cylinder
            if ((int)config.shapeType == 0)
            {
                float r = topInnerRadius;
                return Mathf.PI * r * r * height;
            }

            // Tapered cylinder / frustum
            return Mathf.PI * height / 3.0f *
                   (
                       bottomInnerRadius * bottomInnerRadius +
                       bottomInnerRadius * topInnerRadius +
                       topInnerRadius * topInnerRadius
                   );
        }

        private float ComputeBucketFillVolumeM3(float fillFraction)
        {
            if (bucketSystem == null || bucketSystem.Config == null)
                return 0.0f;

            var config = bucketSystem.Config;
            float fraction = Mathf.Clamp01(fillFraction);
            float fillHeight = Mathf.Max(config.heightMeters, 0.0f) * fraction;
            float bottomRadius = Mathf.Max(
                config.bottomRadiusMeters - config.wallThicknessMeters,
                0.001f
            );
            float topRadius = Mathf.Max(
                config.topRadiusMeters - config.wallThicknessMeters,
                0.001f
            );

            if (config.shapeType == BucketShapeType.Cylinder)
                return Mathf.PI * bottomRadius * bottomRadius * fillHeight;

            float fillRadius = Mathf.Lerp(bottomRadius, topRadius, fraction);
            return Mathf.PI * fillHeight / 3.0f *
                   (
                       bottomRadius * bottomRadius +
                       bottomRadius * fillRadius +
                       fillRadius * fillRadius
                   );
        }

        private static float EstimateSpacingFromVolume(float restVolume)
        {
            return Mathf.Pow(Mathf.Max(restVolume, 1e-12f), 1.0f / 3.0f);
        }

        private void RecalibrateGeneratedParticles(float targetPaintVolume, float restDensity)
        {
            int count = Mathf.Max(1, _data.Count);

            float restVolume = targetPaintVolume / count;
            float mass = restDensity * restVolume;

            float spacing = EstimateSpacingFromVolume(restVolume);
            float radius =
                spacing *
                Mathf.Clamp(paintFluidConfig.particleRadiusToSpacing, 0.25f, 0.65f);

            for (int i = 0; i < _data.Count; i++)
            {
                _data.RestVolumes[i] = restVolume;
                _data.RestDensities[i] = restDensity;
                _data.Masses[i] = mass;
                _data.Radii[i] = radius;
                _data.Densities[i] = restDensity;
            }

            float representedVolume = restVolume * count;

            _calibrationStats = new FluidCalibrationStats
            {
                valid = true,

                bucketInnerVolumeM3 = ComputeBucketInnerVolumeM3(),
                targetPaintVolumeM3 = targetPaintVolume,
                targetParticleCount = paintFluidConfig.targetParticleCount,
                actualParticleCount = count,

                restDensityKgPerM3 = restDensity,
                restVolumePerParticleM3 = restVolume,
                massPerParticleKg = mass,

                estimatedParticleSpacingM = spacing,
                particleRadiusM = radius,

                totalMassKg = mass * count,
                actualRepresentedVolumeM3 = representedVolume,
                fillVolumeErrorPercent =
                    targetPaintVolume > 1e-9f
                        ? Mathf.Abs(representedVolume - targetPaintVolume) / targetPaintVolume * 100.0f
                        : 0.0f,

                gridCellSizeM = gpuMpmSolverConfig != null ? gpuMpmSolverConfig.cellSizeMeters : 0.0f,
                particleSpacingToCellSizeRatio =
                    gpuMpmSolverConfig != null && gpuMpmSolverConfig.cellSizeMeters > 1e-6f
                        ? spacing / gpuMpmSolverConfig.cellSizeMeters
                        : 0.0f,

                correctFillVolumeM3 = targetPaintVolume,
                configuredFillFraction = Mathf.Clamp01(paintFluidConfig.fillFraction01),
                dryBucketMassKg = bucketSystem != null && bucketSystem.Config != null
                    ? bucketSystem.Config.massKg
                    : 0.0f,
                initialPaintMassKg = mass * count
            };
        }
    }
}
