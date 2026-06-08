using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Jobs;
using PaintBucketSim.Runtime;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Rope
{
    public class RopeSystem : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private RopeConfig ropeConfig;

        [Header("Scene")]
        [SerializeField] private Transform pivotTransform;

        private RopeData _data;
        private bool _initialized;

        private Vector3 _basePivotPosition;

        public RopeConfig Config => ropeConfig;
        public bool IsInitialized => _initialized && _data != null && _data.IsCreated;
        public int ParticleCount => IsInitialized ? _data.ParticleCount : 0;

        public bool IsBroken => IsInitialized && _data.BreakState[0].isBroken != 0;
        public int BrokenSegmentIndex => IsInitialized ? _data.BreakState[0].brokenSegmentIndex : -1;

        public RopeDiagnostics Diagnostics
        {
            get
            {
                if (!IsInitialized || !_data.Diagnostics.IsCreated)
                    return default;

                return _data.Diagnostics[0];
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize(SimulationContext context)
        {
            if (ropeConfig == null)
            {
                Debug.LogError("RopeSystem: Missing RopeConfig.");
                _initialized = false;
                return;
            }

            if (_data == null)
                _data = new RopeData();

            _data.Allocate(ropeConfig.segmentCount, Allocator.Persistent);

            _basePivotPosition = pivotTransform != null ? pivotTransform.position : transform.position;

            InitializeRopeParticles();

            _initialized = true;
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            if (_data != null)
            {
                _data.Dispose();
                _data = null;
            }

            _initialized = false;
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized || context == null)
                return;

            float segmentRestLength = ropeConfig.lengthMeters / math.max(ropeConfig.segmentCount, 1);
            float stretchCompliance = ComputeStretchCompliance(segmentRestLength);
            float bendCompliance = ComputeBendCompliance(segmentRestLength);

            int brokenSegment = _data.BreakState[0].isBroken != 0
                ? _data.BreakState[0].brokenSegmentIndex
                : -1;

            float3 pivot = GetPivotPosition(context);
            float3 gravity = (float3)context.EnvironmentState.gravity * ropeConfig.gravityScale;

            bool useRelativeDamping =
                ropeConfig.dampingMode == RopeDampingMode.RelativeSegment ||
                ropeConfig.dampingMode == RopeDampingMode.Combined;

            JobHandle handle = default;

            if (useRelativeDamping)
            {
                var dampingJob = new RopeApplyRelativeDampingJob
                {
                    dt = dt,
                    stretchCompliance = stretchCompliance,
                    dampingRatio = ropeConfig.segmentDampingRatio,
                    brokenSegmentIndex = brokenSegment,
                    positions = _data.Positions,
                    velocities = _data.Velocities,
                    inverseMasses = _data.InverseMasses
                };

                handle = dampingJob.Schedule();
            }

            var predictJob = new RopePredictJob
            {
                dt = dt,
                integrationMode = (int)ropeConfig.integrationMode,
                gravity = gravity,
                pivotPosition = pivot,

                windVelocity = context.EnvironmentState.windVelocity,
                airDragMode = (int)ropeConfig.airDragMode,
                airDensity = context.EnvironmentState.airDensity,
                airViscosity = context.EnvironmentState.airViscosity,
                ropeRadius = ropeConfig.physicalRadiusMeters,
                linearAirDragKgPerSecond = ropeConfig.linearAirDragKgPerSecond,
                quadraticDragCoefficient = ropeConfig.quadraticDragCoefficient,

                positions = _data.Positions,
                previousPositions = _data.PreviousPositions,
                velocities = _data.Velocities,
                inverseMasses = _data.InverseMasses
            };

            handle = predictJob.Schedule(_data.ParticleCount, 32, handle);

            var resetJob = new RopeResetLambdasJob
            {
                stretchLambdas = _data.StretchLambdas,
                bendLambdas = _data.BendLambdas
            };

            handle = resetJob.Schedule(handle);

            var solveJob = new RopeSolveXpbdJob
            {
                dt = dt,
                solverIterations = ropeConfig.solverIterations,
                stretchCompliance = stretchCompliance,
                bendCompliance = bendCompliance,
                enableBending = ropeConfig.enableBending,
                bendingModel = (int)ropeConfig.bendingModel,
                brokenSegmentIndex = brokenSegment,
                positions = _data.Positions,
                inverseMasses = _data.InverseMasses,
                stretchRestLengths = _data.StretchRestLengths,
                stretchLambdas = _data.StretchLambdas,
                bendRestAngles = _data.BendRestAngles,
                bendRestLengths = _data.BendRestLengths,
                bendLambdas = _data.BendLambdas
            };

            handle = solveJob.Schedule(handle);

            float exponentialDamping = 0.0f;
            if (ropeConfig.dampingMode == RopeDampingMode.ExponentialVelocity ||
                ropeConfig.dampingMode == RopeDampingMode.Combined)
            {
                exponentialDamping = ropeConfig.exponentialDampingPerSecond;
            }

            var velocityJob = new RopeVelocityUpdateJob
            {
                dt = dt,
                exponentialDampingPerSecond = exponentialDamping,
                positions = _data.Positions,
                previousPositions = _data.PreviousPositions,
                velocities = _data.Velocities,
                inverseMasses = _data.InverseMasses
            };

            handle = velocityJob.Schedule(_data.ParticleCount, 32, handle);

            var breakJob = new RopeBreakDetectionJob
            {
                dt = dt,
                simulationTime = (float)context.Diagnostics.simulationTime,
                enableBreakByTension = ropeConfig.enableBreakByTension,
                enableBreakByStrain = ropeConfig.enableBreakByStrain,
                breakTension = ropeConfig.breakTensionNewton,
                breakStrain = ropeConfig.breakStrain,
                positions = _data.Positions,
                stretchRestLengths = _data.StretchRestLengths,
                stretchLambdas = _data.StretchLambdas,
                breakState = _data.BreakState
            };

            handle = breakJob.Schedule(handle);

            var diagnosticsJob = new RopeDiagnosticsJob
            {
                dt = dt,
                positions = _data.Positions,
                stretchRestLengths = _data.StretchRestLengths,
                stretchLambdas = _data.StretchLambdas,
                breakState = _data.BreakState,
                diagnostics = _data.Diagnostics
            };

            handle = diagnosticsJob.Schedule(handle);
            handle.Complete();
        }

        public void CopyPositionsTo(Vector3[] target)
        {
            if (!IsInitialized || target == null)
                return;

            int count = Mathf.Min(target.Length, _data.ParticleCount);

            for (int i = 0; i < count; i++)
            {
                float3 p = _data.Positions[i];
                target[i] = new Vector3(p.x, p.y, p.z);
            }
        }

        public Vector3 GetParticlePosition(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.ParticleCount)
                return Vector3.zero;

            float3 p = _data.Positions[index];
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetRopeEndPosition()
        {
            if (!IsInitialized)
                return Vector3.zero;

            return GetParticlePosition(_data.ParticleCount - 1);
        }

        private void InitializeRopeParticles()
        {
            float length = Mathf.Max(ropeConfig.lengthMeters, 0.05f);
            int segments = Mathf.Max(2, ropeConfig.segmentCount);
            int particles = segments + 1;

            float restLength = length / segments;

            Vector3 directionVector = ropeConfig.initialDirection;
            if (directionVector.sqrMagnitude < 1e-6f)
                directionVector = Vector3.down;

            directionVector.Normalize();

            float3 pivot = new float3(_basePivotPosition.x, _basePivotPosition.y, _basePivotPosition.z);
            float3 direction = new float3(directionVector.x, directionVector.y, directionVector.z);

            float movingParticleMass = ropeConfig.ropeMassKg / Mathf.Max(1, particles - 1);

            for (int i = 0; i < particles; i++)
            {
                float3 p = pivot + direction * (restLength * i);

                _data.Positions[i] = p;
                _data.PreviousPositions[i] = p;
                _data.Velocities[i] = float3.zero;

                if (i == 0)
                {
                    _data.InverseMasses[i] = 0.0f;
                }
                else
                {
                    float mass = movingParticleMass;

                    if (i == particles - 1)
                        mass += ropeConfig.temporaryTipMassKg;

                    mass = Mathf.Max(mass, 1e-6f);
                    _data.InverseMasses[i] = 1.0f / mass;
                }
            }

            float restAngleRad = math.radians(ropeConfig.restBendAngleDegrees);

            for (int c = 0; c < segments; c++)
            {
                _data.StretchRestLengths[c] = restLength;
                _data.StretchLambdas[c] = 0.0f;

                _data.SegmentTwistAngles[c] = 0.0f;
                _data.SegmentTwistAngularVelocities[c] = 0.0f;
                _data.SegmentRestTwistAngles[c] = 0.0f;
            }

            for (int c = 0; c < _data.BendConstraintCount; c++)
            {
                _data.BendRestAngles[c] = restAngleRad;
                _data.BendRestLengths[c] = restLength * 2.0f;
                _data.BendLambdas[c] = 0.0f;
            }

            _data.BreakState[0] = new RopeBreakState
            {
                isBroken = 0,
                brokenSegmentIndex = -1,
                breakTime = 0.0f,
                breakTension = 0.0f,
                breakStrain = 0.0f
            };

            _data.Diagnostics[0] = new RopeDiagnostics
            {
                particleCount = particles,
                segmentCount = segments,
                currentLength = length,
                restLength = length,
                maxStretchError = 0.0f,
                averageStretchError = 0.0f,
                maxTensionEstimate = 0.0f,
                maxStrain = 0.0f,
                isBroken = 0,
                brokenSegmentIndex = -1
            };
        }

        private float ComputeStretchCompliance(float segmentRestLength)
        {
            if (ropeConfig.complianceMode == RopeComplianceMode.Manual)
                return ropeConfig.manualStretchCompliance;

            float radius = Mathf.Max(ropeConfig.physicalRadiusMeters, 1e-5f);
            float area = Mathf.PI * radius * radius;
            float young = Mathf.Max(ropeConfig.youngModulusPa, 1000.0f);

            float compliance = segmentRestLength / (young * area);
            return compliance * ropeConfig.stretchComplianceScale;
        }

        private float ComputeBendCompliance(float segmentRestLength)
        {
            if (ropeConfig.complianceMode == RopeComplianceMode.Manual)
                return ropeConfig.manualBendCompliance;

            float radius = Mathf.Max(ropeConfig.physicalRadiusMeters, 1e-5f);
            float young = Mathf.Max(ropeConfig.youngModulusPa, 1000.0f);

            float secondMomentArea = Mathf.PI * Mathf.Pow(radius, 4.0f) / 4.0f;

            // Approximate bending compliance for this simplified centerline rope.
            // It is intentionally scaled because true rod bending requires a full rod frame model.
            float compliance = segmentRestLength / Mathf.Max(young * secondMomentArea, 1e-8f);

            return compliance * ropeConfig.bendComplianceScale;
        }

        private float3 GetPivotPosition(SimulationContext context)
        {
            Vector3 basePosition = pivotTransform != null
                ? pivotTransform.position
                : _basePivotPosition;

            Vector3 offset = Vector3.zero;

            float t = context != null ? (float)context.Diagnostics.simulationTime : Time.time;

            if (ropeConfig.pivotMotionMode == RopePivotMotionMode.Sinusoidal)
            {
                float phase = 2.0f * Mathf.PI * ropeConfig.pivotMotionFrequencyHz * t;
                offset = ropeConfig.pivotMotionAmplitude * Mathf.Sin(phase);
            }
            else if (ropeConfig.pivotMotionMode == RopePivotMotionMode.Noise)
            {
                float speed = ropeConfig.pivotNoiseSpeed;
                float strength = ropeConfig.pivotNoiseStrength;

                float nx = Mathf.PerlinNoise(t * speed, 0.13f) - 0.5f;
                float ny = Mathf.PerlinNoise(0.37f, t * speed) - 0.5f;
                float nz = Mathf.PerlinNoise(t * speed, t * speed + 0.71f) - 0.5f;

                offset = new Vector3(nx, ny, nz) * strength;
            }

            Vector3 p = basePosition + offset;
            return new float3(p.x, p.y, p.z);
        }

        public float GetRopeEndInverseMass()
        {
            if (!IsInitialized)
                return 0.0f;

            int index = _data.ParticleCount - 1;
            return _data.InverseMasses[index];
        }

        public Vector3 GetRopeEndVelocity()
        {
            if (!IsInitialized)
                return Vector3.zero;

            int index = _data.ParticleCount - 1;
            float3 v = _data.Velocities[index];
            return new Vector3(v.x, v.y, v.z);
        }

        public void ApplyRopeEndPositionCorrection(Vector3 correction, float dt, bool updateVelocity)
        {
            if (!IsInitialized || IsBroken)
                return;

            int index = _data.ParticleCount - 1;

            float3 c = new float3(correction.x, correction.y, correction.z);

            _data.Positions[index] += c;

            if (updateVelocity && dt > 1e-8f)
            {
                _data.Velocities[index] += c / dt;

                // Keep previous position consistent with corrected velocity.
                _data.PreviousPositions[index] =
                    _data.Positions[index] - _data.Velocities[index] * dt;
            }
        }
    }
}