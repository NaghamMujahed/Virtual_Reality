using System.Collections.Generic;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Jobs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Bucket;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Fluid
{
    public class PaintFluidSystem : MonoBehaviour
    {
        [Header("Configs")]
        [SerializeField] private PaintMaterialConfig paintMaterialConfig;
        [SerializeField] private PaintFluidConfig paintFluidConfig;
        [SerializeField] private PbfSolverConfig pbfSolverConfig;

        [Header("Systems")]
        [SerializeField] private BucketSystem bucketSystem;
        [SerializeField] private BoundarySystem boundarySystem;

        private FluidParticleData _data;
        private bool _initialized;

        private NativeParallelMultiHashMap<int, int> _fluidHashMap;
        private NativeParallelMultiHashMap<int, int> _boundaryHashMap;

        public PaintMaterialConfig MaterialConfig => paintMaterialConfig;
        public PaintFluidConfig FluidConfig => paintFluidConfig;
        public PbfSolverConfig PbfConfig => pbfSolverConfig;

        public bool IsInitialized => _initialized && _data != null && _data.IsCreated;
        public int ParticleCount => IsInitialized ? _data.Count : 0;
        public int Capacity => IsInitialized ? _data.Capacity : 0;

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
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize(SimulationContext context)
        {
            if (paintMaterialConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PaintMaterialConfig.");
                _initialized = false;
                return;
            }

            if (paintFluidConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PaintFluidConfig.");
                _initialized = false;
                return;
            }

            if (pbfSolverConfig == null)
            {
                Debug.LogError("PaintFluidSystem: Missing PbfSolverConfig.");
                _initialized = false;
                return;
            }

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            if (boundarySystem == null)
                boundarySystem = FindFirstObjectByType<BoundarySystem>();

            if (bucketSystem == null || !bucketSystem.IsInitialized)
            {
                Debug.LogError("PaintFluidSystem: Missing initialized BucketSystem.");
                _initialized = false;
                return;
            }

            if (_data == null)
                _data = new FluidParticleData();

            _data.Allocate(paintFluidConfig.maxParticleCapacity, Allocator.Persistent);

            AllocateHashMaps();

            GenerateParticlesInsideBucket();
            UpdateWorldFromLocalPreview();
            UpdateDiagnostics();

            _initialized = true;
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            if (_fluidHashMap.IsCreated)
                _fluidHashMap.Dispose();

            if (_boundaryHashMap.IsCreated)
                _boundaryHashMap.Dispose();

            if (_data != null)
            {
                _data.Dispose();
                _data = null;
            }

            _initialized = false;
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized)
                return;

            if (!pbfSolverConfig.enablePbf)
            {
                if (paintFluidConfig.previewMode == FluidPreviewMode.FollowBucketKinematically)
                    UpdateWorldFromLocalPreview();

                UpdateDiagnostics();
                return;
            }

            StepPbf(context, dt);
            UpdateDiagnostics();
        }

        private void StepPbf(SimulationContext context, float dt)
        {
            float h = pbfSolverConfig.smoothingRadiusMeters;
            float restDensity = paintMaterialConfig.densityKgPerM3;

            var predictJob = new PbfPredictJob
            {
                dt = dt,
                gravity = (float3)context.EnvironmentState.gravity * pbfSolverConfig.fluidGravityScale,

                positions = _data.Positions,
                previousPositions = _data.PreviousPositions,
                velocities = _data.Velocities,
                deltaPositions = _data.DeltaPositions
            };

            JobHandle handle = predictJob.Schedule(_data.Count, 64);

            handle.Complete();

            bool hasBoundary = (
                pbfSolverConfig.useBoundaryParticleCollision &&
                boundarySystem != null &&
                boundarySystem.IsInitialized
            );

            if (hasBoundary)
            {
                BuildBoundaryHash(h);
            }

            for (int iter = 0; iter < pbfSolverConfig.solverIterations; iter++)
            {
                BuildFluidHash(h);

                var densityJob = new PbfDensityLambdaJob
                {
                    smoothingRadius = h,
                    restDensity = restDensity,
                    lambdaEpsilon = pbfSolverConfig.lambdaEpsilon,
                    cellSize = h,

                    positions = _data.Positions,
                    masses = _data.Masses,
                    fluidHashMap = _fluidHashMap,

                    densities = _data.Densities,
                    lambdas = _data.Lambdas
                };

                handle = densityJob.Schedule(_data.Count, 64);

                var correctionJob = new PbfPositionCorrectionJob
                {
                    smoothingRadius = h,
                    restDensity = restDensity,
                    cellSize = h,

                    enableArtificialPressure = pbfSolverConfig.enableArtificialPressure,
                    artificialPressureK = pbfSolverConfig.artificialPressureK,
                    artificialPressureN = pbfSolverConfig.artificialPressureN,
                    artificialPressureDeltaQRatio = pbfSolverConfig.artificialPressureDeltaQRatio,

                    maxPositionCorrection = pbfSolverConfig.maxPositionCorrectionPerIteration,

                    positions = _data.Positions,
                    masses = _data.Masses,
                    lambdas = _data.Lambdas,
                    fluidHashMap = _fluidHashMap,

                    deltaPositions = _data.DeltaPositions
                };

                handle = correctionJob.Schedule(_data.Count, 64, handle);

                var applyJob = new PbfApplyDeltaJob
                {
                    positions = _data.Positions,
                    deltaPositions = _data.DeltaPositions
                };

                handle = applyJob.Schedule(_data.Count, 64, handle);
                handle.Complete();

                if (hasBoundary)
                {
                    //BuildBoundaryHash(h);

                    var boundaryJob = new BoundaryParticleCollisionJob
                    {
                        cellSize = h,
                        smoothingRadius = h,
                        boundaryRadiusMultiplier = pbfSolverConfig.boundaryParticleRadiusMultiplier,
                        strength = pbfSolverConfig.boundaryCollisionStrength,

                        boundaryPositions = boundarySystem.WorldPositionsNative,
                        boundaryNormals = boundarySystem.WorldNormalsNative,
                        boundaryVelocities = boundarySystem.WorldVelocitiesNative,
                        boundaryHashMap = _boundaryHashMap,

                        positions = _data.Positions,
                        velocities = _data.Velocities,
                        radii = _data.Radii
                    };

                    handle = boundaryJob.Schedule(_data.Count, 64);
                    handle.Complete();
                }

                if (pbfSolverConfig.useAnalyticBucketProjection)
                {
                    ScheduleAnalyticProjection(h).Complete();
                }
            }

            var velocityJob = new PbfVelocityUpdateJob
            {
                dt = dt,
                dampingPerSecond = pbfSolverConfig.velocityDampingPerSecond,
                maxSpeed = pbfSolverConfig.maxParticleSpeed,

                positions = _data.Positions,
                previousPositions = _data.PreviousPositions,
                velocities = _data.Velocities
            };

            handle = velocityJob.Schedule(_data.Count, 64);
            handle.Complete();
        }

        private void AllocateHashMaps()
        {
            if (_fluidHashMap.IsCreated)
                _fluidHashMap.Dispose();

            if (_boundaryHashMap.IsCreated)
                _boundaryHashMap.Dispose();

            int fluidCapacity = Mathf.CeilToInt(
                paintFluidConfig.maxParticleCapacity *
                pbfSolverConfig.hashCapacityMultiplier
            );

            _fluidHashMap = new NativeParallelMultiHashMap<int, int>(
                Mathf.Max(1, fluidCapacity),
                Allocator.Persistent
            );

            int boundaryCapacity = 8192;

            if (boundarySystem != null && boundarySystem.IsInitialized)
                boundaryCapacity = Mathf.Max(1, boundarySystem.Count * 2);

            _boundaryHashMap = new NativeParallelMultiHashMap<int, int>(
                boundaryCapacity,
                Allocator.Persistent
            );
        }

        private void BuildFluidHash(float cellSize)
        {
            _fluidHashMap.Clear();

            var job = new BuildFluidHashJob
            {
                cellSize = cellSize,
                positions = _data.Positions,
                hashMap = _fluidHashMap.AsParallelWriter()
            };

            JobHandle handle = job.Schedule(_data.Count, 64);
            handle.Complete();
        }

        private void BuildBoundaryHash(float cellSize)
        {
            if (!boundarySystem.IsInitialized)
                return;

            if (_boundaryHashMap.Capacity < boundarySystem.Count)
            {
                _boundaryHashMap.Dispose();
                _boundaryHashMap = new NativeParallelMultiHashMap<int, int>(
                    Mathf.Max(1, boundarySystem.Count * 2),
                    Allocator.Persistent
                );
            }

            _boundaryHashMap.Clear();

            var job = new BuildBoundaryHashJob
            {
                cellSize = cellSize,
                boundaryPositions = boundarySystem.WorldPositionsNative,
                hashMap = _boundaryHashMap.AsParallelWriter()
            };

            JobHandle handle = job.Schedule(boundarySystem.Count, 64);
            handle.Complete();
        }

        private JobHandle ScheduleAnalyticProjection(float smoothingRadius)
        {
            BucketConfig config = bucketSystem.Config;
            BucketState state = bucketSystem.State;

            float holeRadius = 0.0f;

            if (bucketSystem.Holes != null && bucketSystem.Holes.Length > 0)
                holeRadius = bucketSystem.Holes[0].radius;

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

                projectionStrength = pbfSolverConfig.analyticProjectionStrength,

                holeRadius = holeRadius,
                nearHoleHeight = pbfSolverConfig.nearHoleHeightMeters,
                nearHolePadding = pbfSolverConfig.nearHoleRadialPaddingMeters,

                positions = _data.Positions,
                radii = _data.Radii,
                states = _data.States
            };

            return job.Schedule(_data.Count, 64);
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

            float spacing = ComputeParticleSpacing(bucketConfig);
            float radius = spacing * paintFluidConfig.particleRadiusToSpacing;

            float halfHeight = bucketConfig.heightMeters * 0.5f;
            float yMin = -halfHeight + paintFluidConfig.wallClearanceMeters + radius;
            float yMax = Mathf.Lerp(
                -halfHeight,
                halfHeight,
                paintFluidConfig.fillFraction01
            );

            yMax -= paintFluidConfig.wallClearanceMeters + radius;

            if (yMax <= yMin)
                yMax = yMin + spacing;

            var localPositions = new List<float3>(
                Mathf.Min(paintFluidConfig.targetParticleCount * 2, paintFluidConfig.maxParticleCapacity)
            );

            for (float y = yMin; y <= yMax; y += spacing)
            {
                float t = Mathf.InverseLerp(-halfHeight, halfHeight, y);
                float innerRadius = GetInnerRadiusAtT(bucketConfig, t) - paintFluidConfig.wallClearanceMeters - radius;

                if (innerRadius <= 0.0f)
                    continue;

                for (float x = -innerRadius; x <= innerRadius; x += spacing)
                {
                    for (float z = -innerRadius; z <= innerRadius; z += spacing)
                    {
                        if (x * x + z * z > innerRadius * innerRadius)
                            continue;

                        float3 candidate = new float3(x, y, z);

                        if (IsTooCloseToBottomHole(bucketConfig, candidate, radius))
                            continue;

                        localPositions.Add(candidate);

                        if (localPositions.Count >= paintFluidConfig.maxParticleCapacity)
                            break;
                    }

                    if (localPositions.Count >= paintFluidConfig.maxParticleCapacity)
                        break;
                }

                if (localPositions.Count >= paintFluidConfig.maxParticleCapacity)
                    break;
            }

            int count = localPositions.Count;
            _data.SetCount(count);

            //float particleVolume = (4.0f / 3.0f) * Mathf.PI * radius * radius * radius;
            float particleVolume = spacing * spacing * spacing;
            float particleMass = paintMaterialConfig.densityKgPerM3 * particleVolume;

            Color color = paintMaterialConfig.baseColor;
            float4 color4 = new float4(color.r, color.g, color.b, color.a);

            for (int i = 0; i < count; i++)
            {
                float3 local = localPositions[i];

                _data.LocalPositions[i] = local;

                float3 world = bucketSystem.LocalToWorldPoint(local);

                _data.Positions[i] = world;
                _data.PreviousPositions[i] = world;
                _data.Velocities[i] = float3.zero;
                _data.DeltaPositions[i] = float3.zero;

                _data.Masses[i] = particleMass;
                _data.Radii[i] = radius;

                _data.Densities[i] = paintMaterialConfig.densityKgPerM3;
                _data.Lambdas[i] = 0.0f;

                _data.Colors[i] = color4;
                _data.States[i] = (int)FluidParticleState.InsideFluid;
            }

            float estimatedFillVolume = EstimateFillVolume(bucketConfig);

            _data.Diagnostics[0] = new FluidDiagnostics
            {
                particleCount = count,
                capacity = _data.Capacity,
                particleRadius = radius,
                particleSpacing = spacing,
                totalMass = particleMass * count,
                insideMass = particleMass * count,
                fillFraction = paintFluidConfig.fillFraction01,
                estimatedFillVolume = estimatedFillVolume,
                centerOfMassWorld = float3.zero
            };
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

                if (state == FluidParticleState.InsideFluid ||
                    state == FluidParticleState.NearBoundary ||
                    state == FluidParticleState.NearHole)
                {
                    insideMass += m;
                    weightedPositionSum += _data.Positions[i] * m;
                }
                else if (state == FluidParticleState.Airborne ||
                         state == FluidParticleState.Emitted)
                {
                    airborneMass += m;
                }
                else if (state == FluidParticleState.Deposited ||
                         state == FluidParticleState.Absorbed)
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

        private bool IsTooCloseToBottomHole(BucketConfig bucketConfig, float3 localPoint, float particleRadius)
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

                float2 p = new float2(localPoint.x, localPoint.z);
                float2 c = new float2(centerV.x, centerV.z);

                float clearance =
                    hole.radiusMeters +
                    paintFluidConfig.holeClearanceMeters +
                    particleRadius;

                if (math.lengthsq(p - c) <= clearance * clearance)
                {
                    float bottomY = -bucketConfig.heightMeters * 0.5f;
                    if (localPoint.y < bottomY + clearance * 1.5f)
                        return true;
                }
            }

            return false;
        }
    }
}