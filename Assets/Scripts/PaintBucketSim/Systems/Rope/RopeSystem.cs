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
        private float3 _simulatedPivotPosition;
        private float3 _simulatedPivotVelocity;
        private float3 _fixedStepPivotStart;
        private float3 _fixedStepPivotTarget;
        private int _pivotSubstepCursor;
        private float _externalEndpointTwistTorque;
        private bool _grabActive;
        private int _grabSegmentIndex = -1;
        private float _grabSegmentT;
        private float3 _requestedGrabTarget;
        private float3 _simulatedGrabTarget;
        private float3 _simulatedGrabVelocity;

        public RopeConfig Config => ropeConfig;
        public bool IsInitialized => _initialized && _data != null && _data.IsCreated;
        public int ParticleCount => IsInitialized ? _data.ParticleCount : 0;
        public bool IsGrabActive => _grabActive;
        public int GrabSegmentIndex => _grabActive ? _grabSegmentIndex : -1;
        public float GrabSegmentT => _grabActive ? _grabSegmentT : 0.0f;

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
            _simulatedPivotPosition = new float3(
                _basePivotPosition.x,
                _basePivotPosition.y,
                _basePivotPosition.z);
            _simulatedPivotVelocity = float3.zero;
            _fixedStepPivotStart = _simulatedPivotPosition;
            _fixedStepPivotTarget = _simulatedPivotPosition;
            _pivotSubstepCursor = 0;

            InitializeRopeParticles();
            _externalEndpointTwistTorque = 0.0f;
            _grabActive = false;
            _grabSegmentIndex = -1;
            _grabSegmentT = 0.0f;
            _requestedGrabTarget = float3.zero;
            _simulatedGrabTarget = float3.zero;
            _simulatedGrabVelocity = float3.zero;

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
            float torsionCompliance = ComputeTorsionCompliance(segmentRestLength);
            float endpointTwistTorque = _externalEndpointTwistTorque;
            _externalEndpointTwistTorque = 0.0f;

            float3 pivot = GetPivotPosition(context, dt);
            _data.Velocities[0] = float3.zero;
            float3 gravity = (float3)context.EnvironmentState.gravity * ropeConfig.gravityScale;
            float3 grabTarget = UpdateGrabTarget(dt, pivot);
            TryBreakFromGrabOverextension(
                pivot,
                grabTarget,
                context != null ? (float)context.Diagnostics.simulationTime : Time.time);

            int brokenSegment = _data.BreakState[0].isBroken != 0
                ? _data.BreakState[0].brokenSegmentIndex
                : -1;

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
                enforceMaximumSegmentStrain =
                    ropeConfig.enforceMaximumSegmentStrain,
                maximumSegmentStrain = ropeConfig.maximumSegmentStrain,
                enableGrabConstraint =
                    _grabActive &&
                    ropeConfig.enableInteractiveGrab,
                grabSegmentIndex = _grabSegmentIndex,
                grabSegmentT = _grabSegmentT,
                grabTarget = grabTarget,
                grabVelocity = _simulatedGrabVelocity,
                grabCompliance = ropeConfig.grabCompliance,
                maxGrabCorrectionPerIteration =
                    ropeConfig.maxGrabCorrectionPerIteration,
                brokenSegmentIndex = brokenSegment,
                positions = _data.Positions,
                previousPositions = _data.PreviousPositions,
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

            var materialFrameJob = new RopeMaterialFrameUpdateJob
            {
                dt = dt,
                solverIterations = ropeConfig.enableTwistData
                    ? ropeConfig.torsionSolverIterations
                    : 0,
                torsionCompliance = torsionCompliance,
                torsionPropagationStrength = ropeConfig.torsionPropagationStrength,
                topTwistAnchorStrength = ropeConfig.topTwistAnchorStrength,
                maxTwistGradientRadians = ropeConfig.maxTwistGradientRadians,
                maxTwistAngularSpeed = ropeConfig.maxTwistAngularSpeedRadiansPerSecond,
                twistDamping = ropeConfig.twistDamping,
                externalEndpointTorque = ropeConfig.enableTwistData
                    ? endpointTwistTorque
                    : 0.0f,
                brokenSegmentIndex = brokenSegment,
                positions = _data.Positions,
                segmentRestTwistAngles = _data.SegmentRestTwistAngles,
                segmentTwistAngles = _data.SegmentTwistAngles,
                segmentPreviousTwistAngles = _data.SegmentPreviousTwistAngles,
                segmentTwistAngularVelocities = _data.SegmentTwistAngularVelocities,
                segmentInverseTwistInertias = _data.SegmentInverseTwistInertias,
                twistLambdas = _data.TwistLambdas,
                segmentFrames = _data.SegmentFrames
            };

            handle = materialFrameJob.Schedule(handle);

            var breakJob = new RopeBreakDetectionJob
            {
                dt = dt,
                simulationTime = (float)context.Diagnostics.simulationTime,
                enableBreakByTension = ropeConfig.enableBreakByTension,
                enableBreakByStrain = ropeConfig.enableBreakByStrain && !_grabActive,
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
                segmentTwistAngles = _data.SegmentTwistAngles,
                segmentTwistAngularVelocities = _data.SegmentTwistAngularVelocities,
                segmentInverseTwistInertias = _data.SegmentInverseTwistInertias,
                segmentRestTwistAngles = _data.SegmentRestTwistAngles,
                torsionStiffness = 1.0f / Mathf.Max(torsionCompliance, 1e-8f),
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

        public float GetSegmentExtensionStrain(int segmentIndex)
        {
            if (!IsInitialized ||
                segmentIndex < 0 ||
                segmentIndex >= _data.SegmentCount)
            {
                return 0.0f;
            }

            float restLength = Mathf.Max(
                _data.StretchRestLengths[segmentIndex],
                1e-6f);
            float currentLength = math.distance(
                _data.Positions[segmentIndex],
                _data.Positions[segmentIndex + 1]);
            return Mathf.Max(0.0f, currentLength / restLength - 1.0f);
        }

        public bool TryFindClosestSegment(
            Ray ray,
            float maxDistanceMeters,
            out int segmentIndex,
            out float segmentT,
            out Vector3 grabPoint,
            out float distanceMeters)
        {
            segmentIndex = -1;
            segmentT = 0.0f;
            grabPoint = Vector3.zero;
            distanceMeters = float.PositiveInfinity;

            if (!IsInitialized || _data.SegmentCount <= 0)
                return false;

            float pickDistance = Mathf.Max(maxDistanceMeters, 0.001f);
            int brokenSegment = BrokenSegmentIndex;

            for (int c = 0; c < _data.SegmentCount; c++)
            {
                if (c == brokenSegment)
                    continue;

                Vector3 p0 = ToVector3(_data.Positions[c]);
                Vector3 p1 = ToVector3(_data.Positions[c + 1]);

                float distance = DistanceRayToSegment(
                    ray,
                    p0,
                    p1,
                    out float t,
                    out Vector3 closestOnSegment);

                if (distance >= distanceMeters || distance > pickDistance)
                    continue;

                segmentIndex = c;
                segmentT = ClampGrabSegmentT(c, t);
                grabPoint = Vector3.Lerp(p0, p1, segmentT);
                distanceMeters = distance;
            }

            return segmentIndex >= 0;
        }

        public bool BeginGrab(int segmentIndex, float segmentT, Vector3 worldPosition)
        {
            if (!IsInitialized ||
                ropeConfig == null ||
                !ropeConfig.enableInteractiveGrab ||
                segmentIndex < 0 ||
                segmentIndex >= _data.SegmentCount ||
                segmentIndex == BrokenSegmentIndex)
            {
                return false;
            }

            float t = ClampGrabSegmentT(segmentIndex, segmentT);
            float b0 = 1.0f - t;
            float b1 = t;
            float weightedInverseMass =
                b0 * b0 * _data.InverseMasses[segmentIndex] +
                b1 * b1 * _data.InverseMasses[segmentIndex + 1];

            if (weightedInverseMass <= 1e-8f)
                return false;

            _grabActive = true;
            _grabSegmentIndex = segmentIndex;
            _grabSegmentT = t;
            _requestedGrabTarget = new float3(
                worldPosition.x,
                worldPosition.y,
                worldPosition.z);
            _simulatedGrabTarget = _requestedGrabTarget;
            _simulatedGrabVelocity = float3.zero;
            return true;
        }

        public void MoveGrab(Vector3 worldPosition)
        {
            if (!_grabActive)
                return;

            _requestedGrabTarget = new float3(
                worldPosition.x,
                worldPosition.y,
                worldPosition.z);
        }

        public void EndGrab()
        {
            _grabActive = false;
            _grabSegmentIndex = -1;
            _grabSegmentT = 0.0f;
            _simulatedGrabVelocity = float3.zero;
        }

        public Vector3 GetGrabTargetPosition()
        {
            if (!_grabActive)
                return Vector3.zero;

            return ToVector3(_simulatedGrabTarget);
        }

        public Vector3 GetGrabTargetVelocity()
        {
            if (!_grabActive)
                return Vector3.zero;

            return ToVector3(_simulatedGrabVelocity);
        }

        public Vector3 GetRopeEndPosition()
        {
            if (!IsInitialized)
                return Vector3.zero;

            return GetParticlePosition(_data.ParticleCount - 1);
        }

        public float GetMechanicalEnergy(Vector3 gravity)
        {
            if (!IsInitialized)
                return 0.0f;

            float3 gravityFloat = new float3(
                gravity.x,
                gravity.y,
                gravity.z);
            float energy = 0.0f;

            for (int i = 1; i < _data.ParticleCount; i++)
            {
                float inverseMass = _data.InverseMasses[i];
                if (inverseMass <= 1e-8f)
                    continue;

                float mass = 1.0f / inverseMass;
                float3 velocity = _data.Velocities[i];
                float3 position = _data.Positions[i];
                energy += 0.5f * mass * math.lengthsq(velocity);
                energy -= mass * math.dot(gravityFloat, position);
            }

            return energy;
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
                _data.SegmentPreviousTwistAngles[c] = 0.0f;
                _data.SegmentTwistAngularVelocities[c] = 0.0f;
                _data.SegmentRestTwistAngles[c] = 0.0f;
                _data.TwistLambdas[c] = 0.0f;

                float segmentMass = Mathf.Max(
                    ropeConfig.ropeMassKg / Mathf.Max(segments, 1),
                    1e-6f);
                float radius = Mathf.Max(ropeConfig.physicalRadiusMeters, 1e-5f);
                float polarInertia =
                    0.5f *
                    segmentMass *
                    radius *
                    radius *
                    Mathf.Max(ropeConfig.twistInertiaScale, 0.01f);
                _data.SegmentInverseTwistInertias[c] =
                    1.0f / Mathf.Max(polarInertia, 1e-8f);

                quaternion frame = BuildSegmentFrame(c, 0.0f);
                _data.SegmentFrames[c] = frame;
                _data.SegmentRestFrames[c] = frame;
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
                maxBendAngleRadians = 0.0f,
                averageBendAngleRadians = 0.0f,
                endpointTwistRadians = 0.0f,
                endpointTwistAngularVelocity = 0.0f,
                maxTwistGradientRadians = 0.0f,
                twistKineticEnergy = 0.0f,
                twistElasticEnergy = 0.0f,
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

        private float ComputeTorsionCompliance(float segmentRestLength)
        {
            float rigidity =
                Mathf.Max(ropeConfig.torsionalRigidityNewtonMeterSquared, 0.0001f) *
                Mathf.Max(ropeConfig.twistStiffnessScale, 0.001f);

            return segmentRestLength / rigidity;
        }

        private float3 UpdateGrabTarget(float dt, float3 pivot)
        {
            if (!_grabActive)
                return float3.zero;

            float safeDt = Mathf.Max(dt, 1e-8f);
            float maximumDistance =
                Mathf.Max(
                    ropeConfig != null
                        ? ropeConfig.maxGrabSpeedMetersPerSecond
                        : 1.0f,
                    0.1f) *
                safeDt;
            float3 previous = _simulatedGrabTarget;
            float3 delta = _requestedGrabTarget - previous;
            float distance = math.length(delta);

            if (distance > maximumDistance && distance > 1e-8f)
                _simulatedGrabTarget = previous + delta / distance * maximumDistance;
            else
                _simulatedGrabTarget = _requestedGrabTarget;

            _simulatedGrabTarget = ClampGrabTargetToReachableSpan(
                _simulatedGrabTarget,
                pivot);

            _simulatedGrabVelocity =
                (_simulatedGrabTarget - previous) / safeDt;
            return _simulatedGrabTarget;
        }

        private float3 ClampGrabTargetToReachableSpan(
            float3 target,
            float3 pivot)
        {
            if (ropeConfig == null ||
                !ropeConfig.enforceMaximumSegmentStrain ||
                ropeConfig.enableBreakByStrain ||
                _grabSegmentIndex < 0 ||
                _grabSegmentIndex >= _data.SegmentCount ||
                (IsBroken && _grabSegmentIndex > BrokenSegmentIndex))
            {
                return target;
            }

            float upperRestLength = 0.0f;
            for (int segmentIndex = 0;
                segmentIndex < _grabSegmentIndex;
                segmentIndex++)
            {
                upperRestLength += _data.StretchRestLengths[segmentIndex];
            }

            upperRestLength +=
                _data.StretchRestLengths[_grabSegmentIndex] *
                Mathf.Clamp01(_grabSegmentT);
            float maximumLength = upperRestLength *
                (1.0f + Mathf.Clamp(
                    ropeConfig.maximumSegmentStrain,
                    0.0f,
                    0.6f));
            float3 offset = target - pivot;
            float distance = math.length(offset);

            if (distance <= maximumLength || distance <= 1e-8f)
                return target;

            return pivot + offset * (maximumLength / distance);
        }

        private bool TryBreakFromGrabOverextension(
            float3 pivot,
            float3 grabTarget,
            float simulationTime)
        {
            if (!_grabActive ||
                ropeConfig == null ||
                !ropeConfig.enableBreakByStrain ||
                !IsInitialized ||
                IsBroken ||
                _grabSegmentIndex < 0 ||
                _grabSegmentIndex >= _data.SegmentCount)
            {
                return false;
            }

            float breakStrain = Mathf.Max(ropeConfig.breakStrain, 0.001f);
            float materialPoint = Mathf.Clamp(
                _grabSegmentIndex + _grabSegmentT,
                0.0f,
                _data.SegmentCount);
            float segmentRestLength =
                ropeConfig.lengthMeters / Mathf.Max(_data.SegmentCount, 1);

            float upperRestLength = segmentRestLength * materialPoint;
            float lowerRestLength =
                segmentRestLength * (_data.SegmentCount - materialPoint);

            float minimumSideLength = segmentRestLength * 0.02f;
            float upperStrain = upperRestLength > minimumSideLength
                ? ComputePathDemandStrain(
                    ToVector3(pivot),
                    ToVector3(grabTarget),
                    upperRestLength)
                : 0.0f;
            float lowerStrain = lowerRestLength > minimumSideLength
                ? ComputePathDemandStrain(
                    ToVector3(grabTarget),
                    GetRopeEndPosition(),
                    lowerRestLength)
                : 0.0f;

            bool upperFails = upperStrain > breakStrain;
            bool lowerFails = lowerStrain > breakStrain;
            if (!upperFails && !lowerFails)
                return false;

            bool breakUpperSide = upperStrain >= lowerStrain;
            int breakSegment = breakUpperSide
                ? FindMostStretchedSegment(0, _grabSegmentIndex + 1)
                : FindMostStretchedSegment(_grabSegmentIndex, _data.SegmentCount);

            if (breakSegment < 0)
            {
                breakSegment = breakUpperSide
                    ? Mathf.Clamp(_grabSegmentIndex / 2, 0, _data.SegmentCount - 1)
                    : Mathf.Clamp(
                        (_grabSegmentIndex + _data.SegmentCount) / 2,
                        0,
                        _data.SegmentCount - 1);
            }

            _data.BreakState[0] = new RopeBreakState
            {
                isBroken = 1,
                brokenSegmentIndex = breakSegment,
                breakTime = simulationTime,
                breakTension = 0.0f,
                breakStrain = Mathf.Max(upperStrain, lowerStrain)
            };

            return true;
        }

        private int FindMostStretchedSegment(int startInclusive, int endExclusive)
        {
            int start = Mathf.Clamp(startInclusive, 0, _data.SegmentCount - 1);
            int end = Mathf.Clamp(endExclusive, start + 1, _data.SegmentCount);
            int selected = -1;
            float selectedStrain = float.NegativeInfinity;

            for (int c = start; c < end; c++)
            {
                float rest = Mathf.Max(_data.StretchRestLengths[c], 1e-6f);
                float strain = Mathf.Max(
                    0.0f,
                    (math.distance(_data.Positions[c], _data.Positions[c + 1]) - rest) / rest);

                if (strain > selectedStrain)
                {
                    selectedStrain = strain;
                    selected = c;
                }
            }

            return selected;
        }

        private static float ComputePathDemandStrain(
            Vector3 start,
            Vector3 end,
            float restLength)
        {
            if (restLength <= 1e-6f)
                return 0.0f;

            return Mathf.Max(0.0f, Vector3.Distance(start, end) / restLength - 1.0f);
        }

        private float3 GetPivotPosition(SimulationContext context, float dt)
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
            float3 target = new float3(p.x, p.y, p.z);
            int substeps = context != null && context.SimulationConfig != null
                ? Mathf.Max(context.SimulationConfig.substeps, 1)
                : 1;
            float fixedStepDuration = Mathf.Max(dt * substeps, 1e-8f);
            int localSubstep = Mathf.Clamp(
                _pivotSubstepCursor,
                0,
                substeps - 1);

            if (localSubstep == 0)
            {
                _fixedStepPivotStart = _simulatedPivotPosition;
                float3 targetDelta = target - _fixedStepPivotStart;
                float targetDistance = math.length(targetDelta);
                float maximumDistance =
                    Mathf.Max(
                        ropeConfig.maxPivotSpeedMetersPerSecond,
                        0.1f) *
                    fixedStepDuration;
                _fixedStepPivotTarget =
                    targetDistance > maximumDistance &&
                    targetDistance > 1e-8f
                        ? _fixedStepPivotStart +
                          targetDelta / targetDistance * maximumDistance
                        : target;
            }

            _simulatedPivotVelocity =
                (_fixedStepPivotTarget - _fixedStepPivotStart) /
                fixedStepDuration;
            float interpolation =
                Mathf.Clamp01((localSubstep + 1.0f) / substeps);
            _simulatedPivotPosition = math.lerp(
                _fixedStepPivotStart,
                _fixedStepPivotTarget,
                interpolation);
            _pivotSubstepCursor = (localSubstep + 1) % substeps;
            return _simulatedPivotPosition;
        }

        private float ClampGrabSegmentT(int segmentIndex, float t)
        {
            float clamped = Mathf.Clamp01(t);

            if (segmentIndex == 0)
                clamped = Mathf.Max(clamped, 0.08f);

            return clamped;
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static float DistanceRayToSegment(
            Ray ray,
            Vector3 segmentStart,
            Vector3 segmentEnd,
            out float segmentT,
            out Vector3 closestOnSegment)
        {
            Vector3 rayDirection = ray.direction;
            if (rayDirection.sqrMagnitude < 1e-10f)
            {
                segmentT = 0.0f;
                closestOnSegment = segmentStart;
                return float.PositiveInfinity;
            }

            rayDirection.Normalize();
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSq = segment.sqrMagnitude;
            if (segmentLengthSq < 1e-10f)
            {
                segmentT = 0.0f;
                closestOnSegment = segmentStart;
                return Vector3.Cross(segmentStart - ray.origin, rayDirection).magnitude;
            }

            Vector3 w0 = ray.origin - segmentStart;
            float a = 1.0f;
            float b = Vector3.Dot(rayDirection, segment);
            float c = segmentLengthSq;
            float d = Vector3.Dot(rayDirection, w0);
            float e = Vector3.Dot(segment, w0);
            float denominator = a * c - b * b;

            float rayT;
            if (denominator > 1e-8f)
            {
                rayT = (b * e - c * d) / denominator;
                segmentT = (a * e - b * d) / denominator;
            }
            else
            {
                rayT = 0.0f;
                segmentT = e / c;
            }

            if (rayT < 0.0f)
            {
                rayT = 0.0f;
                segmentT = Mathf.Clamp01(e / c);
            }
            else if (segmentT < 0.0f)
            {
                segmentT = 0.0f;
                rayT = Mathf.Max(-d / a, 0.0f);
            }
            else if (segmentT > 1.0f)
            {
                segmentT = 1.0f;
                rayT = Mathf.Max((b - d) / a, 0.0f);
            }
            else
            {
                segmentT = Mathf.Clamp01(segmentT);
            }

            Vector3 closestOnRay = ray.origin + rayDirection * rayT;
            closestOnSegment = segmentStart + segment * segmentT;
            return Vector3.Distance(closestOnRay, closestOnSegment);
        }

        public Vector3 GetSimulatedPivotPosition()
        {
            return new Vector3(
                _simulatedPivotPosition.x,
                _simulatedPivotPosition.y,
                _simulatedPivotPosition.z);
        }

        public Vector3 GetSimulatedPivotVelocity()
        {
            return new Vector3(
                _simulatedPivotVelocity.x,
                _simulatedPivotVelocity.y,
                _simulatedPivotVelocity.z);
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

        public Vector3 GetRopeEndTangent()
        {
            if (!IsInitialized || _data.ParticleCount < 2)
                return Vector3.down;

            int end = _data.ParticleCount - 1;
            int supportSpan = math.min(3, end);
            float3 delta =
                _data.Positions[end] -
                _data.Positions[end - supportSpan];

            if (math.lengthsq(delta) < 1e-10f)
            {
                delta =
                    _data.Positions[end] -
                    _data.Positions[end - 1];
            }

            if (math.lengthsq(delta) < 1e-10f)
                return Vector3.down;

            delta = math.normalize(delta);
            return new Vector3(delta.x, delta.y, delta.z);
        }

        public float GetRopeEndTwistRadians()
        {
            if (!IsInitialized ||
                !_data.SegmentTwistAngles.IsCreated ||
                _data.SegmentTwistAngles.Length == 0)
            {
                return 0.0f;
            }

            return _data.SegmentTwistAngles[_data.SegmentTwistAngles.Length - 1];
        }

        public float GetRopeEndTwistAngularVelocity()
        {
            if (!IsInitialized ||
                !_data.SegmentTwistAngularVelocities.IsCreated ||
                _data.SegmentTwistAngularVelocities.Length == 0)
            {
                return 0.0f;
            }

            return _data.SegmentTwistAngularVelocities[
                _data.SegmentTwistAngularVelocities.Length - 1];
        }

        public Vector3 GetRopeEndMaterialNormal()
        {
            if (!IsInitialized ||
                !_data.SegmentFrames.IsCreated ||
                _data.SegmentFrames.Length == 0)
            {
                return Vector3.up;
            }

            quaternion frame = _data.SegmentFrames[_data.SegmentFrames.Length - 1];
            float3 normal = math.rotate(frame, new float3(0.0f, 1.0f, 0.0f));
            return new Vector3(normal.x, normal.y, normal.z);
        }

        public void SetEndpointPayloadMass(float payloadMassKg)
        {
            if (!IsInitialized || ropeConfig == null)
                return;

            int index = _data.ParticleCount - 1;
            float ropeParticleMass =
                ropeConfig.ropeMassKg /
                Mathf.Max(1, _data.ParticleCount - 1);
            float totalTipMass =
                ropeParticleMass +
                Mathf.Max(ropeConfig.temporaryTipMassKg, 0.0f) +
                Mathf.Max(payloadMassKg, 0.0f);

            _data.InverseMasses[index] =
                1.0f / Mathf.Max(totalTipMass, 1e-6f);
        }

        public void AddEndpointTwistTorque(float torqueNewtonMeters)
        {
            if (!IsInitialized || IsBroken || ropeConfig == null || !ropeConfig.enableTwistData)
                return;

            _externalEndpointTwistTorque += torqueNewtonMeters;
        }

        public void CopyTwistAnglesTo(float[] target)
        {
            if (!IsInitialized ||
                target == null ||
                !_data.SegmentTwistAngles.IsCreated)
            {
                return;
            }

            int count = Mathf.Min(target.Length, _data.SegmentTwistAngles.Length);
            for (int i = 0; i < count; i++)
                target[i] = _data.SegmentTwistAngles[i];
        }

        public void CopySegmentFramesTo(Quaternion[] target)
        {
            if (!IsInitialized ||
                target == null ||
                !_data.SegmentFrames.IsCreated)
            {
                return;
            }

            int count = Mathf.Min(target.Length, _data.SegmentFrames.Length);
            for (int i = 0; i < count; i++)
            {
                quaternion q = _data.SegmentFrames[i];
                target[i] = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
            }
        }

        public float ApplyEndpointTwistTarget(float targetTwistRadians, float dt)
        {
            if (!IsInitialized ||
                ropeConfig == null ||
                !ropeConfig.enableTwistData ||
                !_data.SegmentTwistAngles.IsCreated ||
                _data.SegmentTwistAngles.Length == 0)
            {
                return 0.0f;
            }

            int end = _data.SegmentTwistAngles.Length - 1;
            return NormalizeAngleRadians(
                targetTwistRadians - _data.SegmentTwistAngles[end]);
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

        public void ApplyRopeEndImpulse(Vector3 impulse, float dt)
        {
            if (!IsInitialized || IsBroken)
                return;

            int index = _data.ParticleCount - 1;
            float inverseMass = _data.InverseMasses[index];
            if (inverseMass <= 0.0f)
                return;

            _data.Velocities[index] +=
                new float3(impulse.x, impulse.y, impulse.z) * inverseMass;

            if (dt > 1e-8f)
            {
                _data.PreviousPositions[index] =
                    _data.Positions[index] - _data.Velocities[index] * dt;
            }
        }

        private void ApplyTwistDamping(float dt)
        {
            if (!_data.SegmentTwistAngularVelocities.IsCreated)
                return;

            float damping = Mathf.Exp(
                -Mathf.Max(ropeConfig != null ? ropeConfig.twistDamping : 0.0f, 0.0f) *
                Mathf.Max(dt, 0.0f)
            );

            for (int i = 0; i < _data.SegmentTwistAngularVelocities.Length; i++)
                _data.SegmentTwistAngularVelocities[i] *= damping;
        }

        private void RebuildMaterialFramesFromCurrentCenterline()
        {
            if (!IsInitialized || !_data.SegmentFrames.IsCreated)
                return;

            Vector3 baseNormal = ChooseInitialNormal(GetSegmentTangentVector(0));

            for (int i = 0; i < _data.SegmentFrames.Length; i++)
            {
                Vector3 tangent = GetSegmentTangentVector(i);
                Vector3 transportedNormal = Vector3.ProjectOnPlane(baseNormal, tangent);

                if (transportedNormal.sqrMagnitude < 1e-8f)
                    transportedNormal = ChooseInitialNormal(tangent);
                else
                    transportedNormal.Normalize();

                Vector3 binormal = Vector3.Cross(tangent, transportedNormal).normalized;
                float twist = _data.SegmentTwistAngles[i];

                Vector3 materialNormal =
                    transportedNormal * Mathf.Cos(twist) +
                    binormal * Mathf.Sin(twist);

                Quaternion frame = Quaternion.LookRotation(tangent, materialNormal.normalized);
                _data.SegmentFrames[i] = new quaternion(frame.x, frame.y, frame.z, frame.w);

                baseNormal = transportedNormal;
            }
        }

        private quaternion BuildSegmentFrame(int segmentIndex, float twistRadians)
        {
            Vector3 tangent = GetSegmentTangentVector(segmentIndex);
            Vector3 normal = ChooseInitialNormal(tangent);
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

            Vector3 materialNormal =
                normal * Mathf.Cos(twistRadians) +
                binormal * Mathf.Sin(twistRadians);

            Quaternion frame = Quaternion.LookRotation(tangent, materialNormal.normalized);
            return new quaternion(frame.x, frame.y, frame.z, frame.w);
        }

        private Vector3 GetSegmentTangentVector(int segmentIndex)
        {
            if (_data == null || !_data.Positions.IsCreated || _data.ParticleCount < 2)
                return Vector3.down;

            int i0 = Mathf.Clamp(segmentIndex, 0, Mathf.Max(0, _data.ParticleCount - 2));
            int i1 = Mathf.Clamp(i0 + 1, 0, Mathf.Max(0, _data.ParticleCount - 1));

            float3 delta = _data.Positions[i1] - _data.Positions[i0];
            if (math.lengthsq(delta) < 1e-10f)
                return Vector3.down;

            delta = math.normalize(delta);
            return new Vector3(delta.x, delta.y, delta.z);
        }

        private static Vector3 ChooseInitialNormal(Vector3 tangent)
        {
            Vector3 normal = Vector3.ProjectOnPlane(Vector3.up, tangent);
            if (normal.sqrMagnitude > 1e-8f)
                return normal.normalized;

            normal = Vector3.ProjectOnPlane(Vector3.right, tangent);
            if (normal.sqrMagnitude > 1e-8f)
                return normal.normalized;

            return Vector3.forward;
        }

        private static float NormalizeAngleRadians(float angle)
        {
            const float TwoPi = Mathf.PI * 2.0f;

            angle = Mathf.Repeat(angle + Mathf.PI, TwoPi) - Mathf.PI;
            return angle;
        }
    }
}
