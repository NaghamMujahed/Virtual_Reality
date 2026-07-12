using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Runtime;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Bucket
{
    public class BucketSystem : MonoBehaviour
    {
        private const float GrabAngularFrequency = 12.0f;
        private const float GrabMaxAcceleration = 25.0f;
        private const float GrabReleaseBlendSeconds = 0.2f;

        [Header("Config")]
        [SerializeField] private BucketConfig bucketConfig;

        [Header("Pose Reference")]
        [Tooltip("Used for initial pose and for KinematicFollowTransform mode.")]
        [SerializeField] private Transform poseReferenceTransform;

        private BucketData _data;
        private bool _initialized;

        private float3 _externalForce;
        private float3 _externalTorque;
        private float3 _containedFluidInertiaAtCenter;
        private float3 _lastFluidReactionLinearImpulse;
        private float3 _lastFluidReactionAngularImpulse;
        private int _lastFluidCouplingSampleStep;
        private float _bailHingeAngleRadians;
        private float _bailHingeAngularVelocity;
        private float3 _stepStartCenterOfMassWorld;
        private quaternion _stepStartRotation;
        private bool _hasStepStartPose;
        private bool _grabActive;
        private float3 _grabRequestedTarget;
        private float3 _grabTarget;
        private float3 _grabTargetVelocity;
        private float3 _grabCenterOffsetWorld;
        private float _grabConstraintInfluence;

        public BucketConfig Config => bucketConfig;
        public bool IsInitialized => _initialized && _data != null;
        public BucketState State => _data != null ? _data.State : default;
        public BucketAttachmentWorldState Attachment => _data != null ? _data.Attachment : default;
        public BucketHoleWorldState[] Holes => _data != null ? _data.Holes : null;
        public BucketDiagnostics Diagnostics => _data != null ? _data.Diagnostics : default;
        public float BailHingeAngleRadians => _bailHingeAngleRadians;
        public bool IsGrabActive => _grabActive;
        public float GrabConstraintInfluence => _grabConstraintInfluence;

        private void OnDestroy()
        {
            _data = null;
            _initialized = false;
        }

        public void Initialize(SimulationContext context)
        {
            if (bucketConfig == null)
            {
                Debug.LogError("BucketSystem: Missing BucketConfig.");
                _initialized = false;
                return;
            }

            if (poseReferenceTransform == null)
                poseReferenceTransform = transform;

            _data = new BucketData();
            _data.AllocateHoles(bucketConfig.holes != null ? bucketConfig.holes.Length : 0);

            _containedFluidInertiaAtCenter = float3.zero;
            _lastFluidReactionLinearImpulse = float3.zero;
            _lastFluidReactionAngularImpulse = float3.zero;
            _lastFluidCouplingSampleStep = -1;

            InitializeStateFromReference();
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();

            _externalForce = float3.zero;
            _externalTorque = float3.zero;
            _bailHingeAngleRadians = 0.0f;
            _bailHingeAngularVelocity = 0.0f;
            _stepStartCenterOfMassWorld = GetCenterOfMassWorldFloat3();
            _stepStartRotation = _data.State.rotation;
            _hasStepStartPose = false;
            _grabActive = false;
            _grabRequestedTarget = float3.zero;
            _grabTarget = float3.zero;
            _grabTargetVelocity = float3.zero;
            _grabCenterOffsetWorld = float3.zero;
            _grabConstraintInfluence = 0.0f;

            _initialized = true;
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void RefreshHoleConfiguration()
        {
            if (!IsInitialized || bucketConfig == null)
                return;

            int holeCount = bucketConfig.holes != null
                ? bucketConfig.holes.Length
                : 0;
            if (_data.Holes == null || _data.Holes.Length != holeCount)
                _data.AllocateHoles(holeCount);

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized)
                return;

            CaptureStepStartPose();
            UpdateGrabConstraintInfluence(dt);

            if (bucketConfig.motionMode == BucketMotionMode.KinematicFollowTransform)
            {
                EndGrab();
                _grabConstraintInfluence = 0.0f;
                ReadStateFromReferenceTransform(dt);
                UpdateAttachmentAndHoles();
                UpdateDiagnostics();
                return;
            }

            if (bucketConfig.motionMode == BucketMotionMode.LockedInitialPose)
            {
                EndGrab();
                _grabConstraintInfluence = 0.0f;
                UpdateAttachmentAndHoles();
                UpdateDiagnostics();
                return;
            }

            if (_grabActive)
                ApplyGrabForce(dt);

            IntegrateDynamic(context, dt);

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();

            ClearExternalForces();
        }

        private void CaptureStepStartPose()
        {
            _stepStartCenterOfMassWorld = GetCenterOfMassWorldFloat3();
            _stepStartRotation = _data.State.rotation;
            _hasStepStartPose = true;
        }

        public void AddForce(float3 force)
        {
            _externalForce += force;
        }

        public void AddTorque(float3 torque)
        {
            _externalTorque += torque;
        }

        public void AddForceAtWorldPoint(float3 force, float3 worldPoint)
        {
            _externalForce += force;

            float3 r = worldPoint - GetCenterOfMassWorldFloat3();
            _externalTorque += math.cross(r, force);
        }

        public void ApplyImpulseAtWorldPoint(Vector3 impulse, Vector3 worldPoint)
        {
            if (!IsInitialized)
                return;

            BucketState state = _data.State;
            float3 impulseFloat = new float3(impulse.x, impulse.y, impulse.z);
            float3 pointFloat = new float3(worldPoint.x, worldPoint.y, worldPoint.z);
            float3 r = pointFloat - GetCenterOfMassWorldFloat3();

            state.velocity += impulseFloat * state.inverseMass;
            state.angularVelocity += MultiplyInverseInertiaWorld(
                state,
                math.cross(r, impulseFloat));

            _data.State = state;
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public void ApplyMomentumImpulse(
            float3 linearImpulseWorld,
            float3 angularImpulseWorld,
            int fluidSampleStep)
        {
            if (!IsInitialized)
                return;

            BucketState state = _data.State;
            state.velocity += linearImpulseWorld * state.inverseMass;
            state.angularVelocity += MultiplyInverseInertiaWorld(
                state,
                angularImpulseWorld);

            _data.State = state;
            _lastFluidReactionLinearImpulse = linearImpulseWorld;
            _lastFluidReactionAngularImpulse = angularImpulseWorld;
            _lastFluidCouplingSampleStep = fluidSampleStep;
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public void ClearExternalForces()
        {
            _externalForce = float3.zero;
            _externalTorque = float3.zero;
        }

        public bool BeginGrab(Vector3 worldPoint)
        {
            if (!IsInitialized ||
                bucketConfig == null ||
                bucketConfig.motionMode != BucketMotionMode.DynamicFree)
            {
                return false;
            }

            float3 point = new float3(worldPoint.x, worldPoint.y, worldPoint.z);
            _grabActive = true;
            _grabRequestedTarget = point;
            _grabTarget = point;
            _grabTargetVelocity = float3.zero;
            _grabCenterOffsetWorld = GetCenterOfMassWorldFloat3() - point;
            _grabConstraintInfluence = 1.0f;
            return true;
        }

        public void MoveGrab(Vector3 worldTarget)
        {
            if (!_grabActive)
                return;

            _grabRequestedTarget = new float3(
                worldTarget.x,
                worldTarget.y,
                worldTarget.z);
        }

        public void EndGrab()
        {
            _grabActive = false;
            _grabTargetVelocity = float3.zero;
        }

        public float3 LocalToWorldPoint(float3 localPoint)
        {
            return _data.State.position + math.rotate(_data.State.rotation, localPoint);
        }

        public float3 LocalToWorldVector(float3 localVector)
        {
            return math.rotate(_data.State.rotation, localVector);
        }

        public float3 WorldToLocalPoint(float3 worldPoint)
        {
            return math.rotate(
                math.inverse(_data.State.rotation),
                worldPoint - _data.State.position);
        }

        public float3 GetWorldPointVelocity(float3 worldPoint)
        {
            float3 r = worldPoint - GetCenterOfMassWorldFloat3();
            return _data.State.velocity + math.cross(_data.State.angularVelocity, r);
        }

        private void InitializeStateFromReference()
        {
            Vector3 p = poseReferenceTransform != null ? poseReferenceTransform.position : transform.position;
            Quaternion r = poseReferenceTransform != null ? poseReferenceTransform.rotation : transform.rotation;

            BucketState state = new BucketState
            {
                position = new float3(p.x, p.y, p.z),
                rotation = new quaternion(r.x, r.y, r.z, r.w),

                velocity = float3.zero,
                angularVelocity = float3.zero,

                mass = math.max(bucketConfig.massKg, 0.01f),
                inverseMass = 1.0f / math.max(bucketConfig.massKg, 0.01f),
                dryMass = math.max(bucketConfig.massKg, 0.01f),
                containedFluidMass = 0.0f,
                centerOfMassLocal = float3.zero
            };

            float3 inertia = ComputeBodyInertiaTensor();
            state.inertiaTensorBody = inertia;
            state.inverseInertiaTensorBody = new float3(
                inertia.x > 1e-8f ? 1.0f / inertia.x : 0.0f,
                inertia.y > 1e-8f ? 1.0f / inertia.y : 0.0f,
                inertia.z > 1e-8f ? 1.0f / inertia.z : 0.0f
            );

            _data.State = state;
        }

        private void ReadStateFromReferenceTransform(float dt)
        {
            Vector3 p = poseReferenceTransform != null ? poseReferenceTransform.position : transform.position;
            Quaternion r = poseReferenceTransform != null ? poseReferenceTransform.rotation : transform.rotation;

            BucketState state = _data.State;

            float3 newPosition = new float3(p.x, p.y, p.z);
            quaternion newRotation = new quaternion(r.x, r.y, r.z, r.w);

            float safeDt = math.max(dt, 1e-6f);
            float3 previousPosition = state.position;
            quaternion previousRotation = state.rotation;
            float3 previousCenterOfMass =
                previousPosition +
                math.rotate(previousRotation, state.centerOfMassLocal);

            state.position = newPosition;
            state.rotation = math.normalize(newRotation);
            float3 newCenterOfMass =
                newPosition +
                math.rotate(state.rotation, state.centerOfMassLocal);
            state.velocity = (newCenterOfMass - previousCenterOfMass) / safeDt;

            quaternion delta = math.normalize(math.mul(state.rotation, math.inverse(previousRotation)));
            if (delta.value.w < 0.0f)
                delta.value = -delta.value;

            float sinHalf = math.length(delta.value.xyz);
            if (sinHalf > 1e-7f)
            {
                float angle = 2.0f * math.atan2(sinHalf, math.clamp(delta.value.w, -1.0f, 1.0f));
                state.angularVelocity = delta.value.xyz / sinHalf * (angle / safeDt);
            }
            else
            {
                state.angularVelocity = float3.zero;
            }

            _data.State = state;
        }

        private void ApplyGrabForce(float dt)
        {
            BucketState state = _data.State;
            float safeDt = math.max(dt, 1e-6f);
            UpdateGrabTarget(safeDt);

            float3 desiredCenter =
                _grabTarget + _grabCenterOffsetWorld;
            float3 center = GetCenterOfMassWorldFloat3();
            float3 centerError = desiredCenter - center;
            float3 relativeVelocity =
                _grabTargetVelocity - state.velocity;
            float3 force = state.mass * (
                GrabAngularFrequency * GrabAngularFrequency * centerError +
                2.0f * GrabAngularFrequency * relativeVelocity);
            float maximumForce = state.mass * GrabMaxAcceleration;
            float forceLength = math.length(force);
            if (forceLength > maximumForce && forceLength > 1e-6f)
                force *= maximumForce / forceLength;

            AddForce(force);
        }

        private void UpdateGrabTarget(float dt)
        {
            float3 toRequested = _grabRequestedTarget - _grabTarget;
            float distance = math.length(toRequested);
            float maximumSpeed =
                GrabMaxAcceleration / GrabAngularFrequency;
            float stoppingSpeed = math.sqrt(
                2.0f * GrabMaxAcceleration * distance);
            float3 desiredVelocity = distance > 1e-6f
                ? toRequested / distance *
                  math.min(
                      math.min(maximumSpeed, stoppingSpeed),
                      distance / dt)
                : float3.zero;

            float3 velocityDelta = desiredVelocity - _grabTargetVelocity;
            float maximumVelocityChange = GrabMaxAcceleration * dt;
            float velocityDeltaLength = math.length(velocityDelta);
            if (velocityDeltaLength > maximumVelocityChange &&
                velocityDeltaLength > 1e-6f)
            {
                velocityDelta *=
                    maximumVelocityChange / velocityDeltaLength;
            }

            _grabTargetVelocity += velocityDelta;
            float3 movement = _grabTargetVelocity * dt;
            if (math.lengthsq(movement) >= distance * distance &&
                math.dot(movement, toRequested) > 0.0f)
            {
                _grabTarget = _grabRequestedTarget;
                _grabTargetVelocity = float3.zero;
                return;
            }

            _grabTarget += movement;
        }

        private void UpdateGrabConstraintInfluence(float dt)
        {
            if (_grabActive)
            {
                _grabConstraintInfluence = 1.0f;
                return;
            }

            _grabConstraintInfluence = math.max(
                0.0f,
                _grabConstraintInfluence -
                math.max(dt, 0.0f) / GrabReleaseBlendSeconds);
        }

        private void IntegrateDynamic(SimulationContext context, float dt)
        {
            BucketState state = _data.State;

            float3 force = _externalForce;
            float3 torque = _externalTorque;

            if (context != null &&
                bucketConfig.gravityMode == BucketGravityMode.FullBody)
            {
                force += state.mass * (float3)context.EnvironmentState.gravity;
            }
            else if (context != null &&
                     bucketConfig.gravityMode ==
                         BucketGravityMode.RopeSuspendedPayload)
            {
                float3 suspendedCenterOfMassWorld =
                    GetCenterOfMassWorldFloat3();
                float3 attachmentOffset =
                    _data.Attachment.worldPosition -
                    suspendedCenterOfMassWorld;
                float3 supportForce =
                    -state.mass * (float3)context.EnvironmentState.gravity;
                float3 suspensionTorque = math.cross(
                    attachmentOffset,
                    supportForce);
                float suspensionTorqueLength = math.length(
                    suspensionTorque);
                if (suspensionTorqueLength > 1e-7f)
                {
                    float3 axisWorld =
                        suspensionTorque / suspensionTorqueLength;
                    float3 axisBody = math.rotate(
                        math.inverse(state.rotation),
                        axisWorld);
                    float centerInertia = math.max(
                        math.dot(
                            state.inertiaTensorBody * axisBody,
                            axisBody),
                        1e-6f);
                    float perpendicularDistanceSquared = math.max(
                        math.lengthsq(attachmentOffset) -
                        math.pow(
                            math.dot(attachmentOffset, axisWorld),
                            2.0f),
                        0.0f);
                    float attachmentInertia =
                        centerInertia +
                        state.mass * perpendicularDistanceSquared;
                    suspensionTorque *=
                        centerInertia /
                        math.max(attachmentInertia, centerInertia);
                    torque += suspensionTorque;
                }
            }

            float3 acceleration = force * state.inverseMass;
            state.velocity += acceleration * dt;

            float linearDamping = math.exp(-math.max(bucketConfig.linearDampingPerSecond, 0.0f) * dt);
            state.velocity *= linearDamping;

            float3 centerOfMassWorld =
                state.position +
                math.rotate(state.rotation, state.centerOfMassLocal);
            centerOfMassWorld += state.velocity * dt;

            float3 angularAcceleration = ComputeWorldAngularAcceleration(
                state,
                torque);
            state.angularVelocity += angularAcceleration * dt;

            float angularDamping = math.exp(-math.max(bucketConfig.angularDampingPerSecond, 0.0f) * dt);
            state.angularVelocity *= angularDamping;

            float angularSpeed = math.length(state.angularVelocity);
            if (angularSpeed > 1e-7f)
            {
                float3 axis = state.angularVelocity / angularSpeed;
                quaternion deltaRotation = quaternion.AxisAngle(axis, angularSpeed * dt);
                state.rotation = math.normalize(math.mul(deltaRotation, state.rotation));
            }

            state.position =
                centerOfMassWorld -
                math.rotate(state.rotation, state.centerOfMassLocal);

            _data.State = state;
        }

        private float3 MultiplyInverseInertiaWorld(
            BucketState state,
            float3 worldVector)
        {
            quaternion invRot = math.inverse(state.rotation);
            float3 localVector = math.rotate(invRot, worldVector);
            return math.rotate(
                state.rotation,
                localVector * state.inverseInertiaTensorBody);
        }

        private float3 ComputeWorldAngularAcceleration(
            BucketState state,
            float3 worldTorque)
        {
            quaternion inverseRotation = math.inverse(state.rotation);
            float3 localTorque = math.rotate(inverseRotation, worldTorque);
            float3 localAngularVelocity = math.rotate(
                inverseRotation,
                state.angularVelocity);
            float3 localAngularMomentum =
                state.inertiaTensorBody * localAngularVelocity;
            float3 localAngularAcceleration =
                (localTorque -
                 math.cross(localAngularVelocity, localAngularMomentum)) *
                state.inverseInertiaTensorBody;

            return math.rotate(state.rotation, localAngularAcceleration);
        }

        private float3 ComputeBodyInertiaTensor()
        {
            if (!bucketConfig.automaticInertiaTensor)
            {
                Vector3 manual = bucketConfig.manualInertiaTensor;

                return new float3(
                    math.max(manual.x, 1e-6f),
                    math.max(manual.y, 1e-6f),
                    math.max(manual.z, 1e-6f)
                );
            }

            float m = math.max(bucketConfig.massKg, 0.01f);
            float h = math.max(bucketConfig.heightMeters, 0.01f);
            float r = math.max(bucketConfig.GetRepresentativeRadius(), 0.01f);

            // Thin cylindrical shell approximation for the empty bucket body.
            float iXz = m * (0.5f * r * r + h * h / 12.0f);
            float iY = m * r * r;

            return new float3(iXz, iY, iXz);
        }

        private void UpdateAttachmentAndHoles()
        {
            Vector3 attachmentLocalVector = GetPhysicalAttachmentLocalPoint();
            float3 localAttachment = new float3(
                attachmentLocalVector.x,
                attachmentLocalVector.y,
                attachmentLocalVector.z
            );

            float3 worldAttachment = LocalToWorldPoint(localAttachment);
            float3 worldAttachmentVelocity = GetWorldPointVelocity(worldAttachment);

            _data.Attachment = new BucketAttachmentWorldState
            {
                localPoint = localAttachment,
                worldPosition = worldAttachment,
                worldVelocity = worldAttachmentVelocity
            };

            if (_data.Holes == null)
                return;

            for (int i = 0; i < _data.Holes.Length; i++)
            {
                BucketHoleConfig holeConfig = bucketConfig.holes[i];

                Vector3 localCenterVector = bucketConfig.GetResolvedHoleLocalCenter(holeConfig);
                Vector3 localNormalVector = bucketConfig.GetResolvedHoleLocalNormal(holeConfig);
                Vector3 localTangentVector = bucketConfig.GetResolvedHoleLocalTangent(holeConfig);
                Vector3 localBitangentVector = bucketConfig.GetResolvedHoleLocalBitangent(holeConfig);
                Vector2 halfExtentsVector = bucketConfig.GetResolvedHoleHalfExtents(holeConfig);

                float3 localCenter = new float3(
                    localCenterVector.x,
                    localCenterVector.y,
                    localCenterVector.z
                );

                float3 localNormal = math.normalize(new float3(
                    localNormalVector.x,
                    localNormalVector.y,
                    localNormalVector.z
                ));

                float3 localTangent = math.normalize(new float3(
                    localTangentVector.x,
                    localTangentVector.y,
                    localTangentVector.z
                ));

                float3 localBitangent = math.normalize(new float3(
                    localBitangentVector.x,
                    localBitangentVector.y,
                    localBitangentVector.z
                ));

                float3 worldCenter = LocalToWorldPoint(localCenter);
                float3 worldNormal = math.normalize(LocalToWorldVector(localNormal));
                float3 worldTangent = math.normalize(LocalToWorldVector(localTangent));
                float3 worldBitangent = math.normalize(LocalToWorldVector(localBitangent));
                float3 worldVelocity = GetWorldPointVelocity(worldCenter);

                float radius = math.max(holeConfig.radiusMeters, 0.001f);
                float area = math.max(bucketConfig.GetResolvedHoleArea(holeConfig), 0.0f);
                float2 halfExtents = new float2(
                    math.max(halfExtentsVector.x, 0.001f),
                    math.max(halfExtentsVector.y, 0.001f)
                );

                _data.Holes[i] = new BucketHoleWorldState
                {
                    active = holeConfig.active ? 1 : 0,
                    shape = (int)holeConfig.shape,

                    localCenter = localCenter,
                    localNormal = localNormal,
                    localTangent = localTangent,
                    localBitangent = localBitangent,

                    worldCenter = worldCenter,
                    worldNormal = worldNormal,
                    worldTangent = worldTangent,
                    worldBitangent = worldBitangent,
                    worldVelocity = worldVelocity,

                    radius = radius,
                    halfExtents = halfExtents,
                    area = area,
                    wallThickness = math.max(holeConfig.wallThicknessMeters, bucketConfig.wallThicknessMeters),
                    edgeSoftness = math.max(holeConfig.edgeSoftnessMeters, 0.0f),
                    flowMultiplier = math.max(holeConfig.flowMultiplier, 0.0f),
                    exitVelocityBoost = math.max(holeConfig.exitVelocityBoostMetersPerSecond, 0.0f)
                };
            }
        }

        private void UpdateDiagnostics()
        {
            BucketState state = _data.State;

            float speed = math.length(state.velocity);
            float angularSpeed = math.length(state.angularVelocity);

            float linearEnergy = 0.5f * state.mass * speed * speed;

            float3 localOmega = math.rotate(math.inverse(state.rotation), state.angularVelocity);
            float angularEnergy =
                0.5f *
                math.dot(
                    state.inertiaTensorBody * localOmega,
                    localOmega
                );

            _data.Diagnostics = new BucketDiagnostics
            {
                speed = speed,
                angularSpeed = angularSpeed,
                kineticEnergyLinear = linearEnergy,
                kineticEnergyAngular = angularEnergy,
                dryMass = state.dryMass,
                containedFluidMass = state.containedFluidMass,
                totalMass = state.mass,
                centerOfMassLocal = state.centerOfMassLocal,
                centerOfMassWorld = GetCenterOfMassWorldFloat3(),
                fluidCouplingSampleStep = _lastFluidCouplingSampleStep,
                fluidReactionLinearImpulse =
                    _lastFluidReactionLinearImpulse,
                fluidReactionAngularImpulse =
                    _lastFluidReactionAngularImpulse,
                bailHingeAngleRadians = _bailHingeAngleRadians,
                bailHingeAngularVelocity = _bailHingeAngularVelocity,
                attachmentWorldPosition = _data.Attachment.worldPosition,
                attachmentWorldVelocity = _data.Attachment.worldVelocity,
                holeCount = _data.Holes != null ? _data.Holes.Length : 0
            };
        }

        public float GetInverseMass()
        {
            if (!IsInitialized)
                return 0.0f;

            return _data.State.inverseMass;
        }

        public Vector3 GetCenterOfMassWorld()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 p = GetCenterOfMassWorldFloat3();
            return new Vector3(p.x, p.y, p.z);
        }

        public float GetMechanicalEnergy(Vector3 gravity)
        {
            if (!IsInitialized)
                return 0.0f;

            BucketState state = _data.State;
            float3 centerOfMass = GetCenterOfMassWorldFloat3();
            float potential =
                -state.mass *
                math.dot(
                    new float3(gravity.x, gravity.y, gravity.z),
                    centerOfMass);
            return
                _data.Diagnostics.kineticEnergyLinear +
                _data.Diagnostics.kineticEnergyAngular +
                potential;
        }

        public Vector3 GetAttachmentWorldPosition()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 p = _data.Attachment.worldPosition;
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetAttachmentLocalPosition()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 p = _data.Attachment.localPoint;
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetAttachmentWorldVelocity()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 v = _data.Attachment.worldVelocity;
            return new Vector3(v.x, v.y, v.z);
        }

        public void DriveBailTowardWorldPoint(Vector3 worldTarget, float dt)
        {
            if (!IsInitialized || bucketConfig == null)
                return;

            if (!bucketConfig.enableBailHinge)
            {
                _bailHingeAngleRadians = 0.0f;
                _bailHingeAngularVelocity = 0.0f;
                return;
            }

            float3 targetWorld = new float3(
                worldTarget.x,
                worldTarget.y,
                worldTarget.z);
            float3 targetLocal = WorldToLocalPoint(targetWorld);
            Vector3 hingeCenterVector = bucketConfig.GetBailHingeCenterLocal();
            float3 relative = targetLocal - new float3(
                hingeCenterVector.x,
                hingeCenterVector.y,
                hingeCenterVector.z);

            if (math.lengthsq(relative.yz) < 1e-8f)
                return;

            float maxAngle = math.radians(
                math.clamp(bucketConfig.bailHingeMaxAngleDegrees, 5.0f, 88.0f));
            float targetAngle = math.clamp(
                math.atan2(relative.z, relative.y),
                -maxAngle,
                maxAngle);
            float error = targetAngle - _bailHingeAngleRadians;
            float acceleration =
                error * math.max(bucketConfig.bailHingeResponse, 0.1f) -
                _bailHingeAngularVelocity *
                math.max(bucketConfig.bailHingeDamping, 0.0f);

            float safeDt = math.max(dt, 0.0f);
            _bailHingeAngularVelocity += acceleration * safeDt;
            _bailHingeAngleRadians = math.clamp(
                _bailHingeAngleRadians +
                _bailHingeAngularVelocity * safeDt,
                -maxAngle,
                maxAngle);

            if (math.abs(_bailHingeAngleRadians) >= maxAngle - 1e-5f)
                _bailHingeAngularVelocity *= 0.25f;

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public Vector3 MultiplyInverseInertiaWorld(Vector3 worldVector)
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 v = new float3(worldVector.x, worldVector.y, worldVector.z);

            BucketState state = _data.State;

            quaternion invRot = math.inverse(state.rotation);

            float3 local = math.rotate(invRot, v);
            float3 localResult = local * state.inverseInertiaTensorBody;
            float3 worldResult = math.rotate(state.rotation, localResult);

            return new Vector3(worldResult.x, worldResult.y, worldResult.z);
        }

        public Vector3 MultiplyInertiaWorld(Vector3 worldVector)
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 v = new float3(worldVector.x, worldVector.y, worldVector.z);
            BucketState state = _data.State;
            quaternion invRot = math.inverse(state.rotation);
            float3 local = math.rotate(invRot, v);
            float3 inverseInertia = state.inverseInertiaTensorBody;
            float3 localResult = new float3(
                inverseInertia.x > 1e-8f ? local.x / inverseInertia.x : 0.0f,
                inverseInertia.y > 1e-8f ? local.y / inverseInertia.y : 0.0f,
                inverseInertia.z > 1e-8f ? local.z / inverseInertia.z : 0.0f);
            float3 worldResult = math.rotate(state.rotation, localResult);

            return new Vector3(worldResult.x, worldResult.y, worldResult.z);
        }

        public void SetContainedFluidLoad(
            float fluidMassKg,
            float3 fluidCenterOfMassLocal,
            float estimatedFillHeightMeters,
            float dt)
        {
            SetContainedFluidLoad(
                fluidMassKg,
                fluidCenterOfMassLocal,
                estimatedFillHeightMeters,
                dt,
                float3.zero,
                false);
        }

        public void SetContainedFluidLoad(
            float fluidMassKg,
            float3 fluidCenterOfMassLocal,
            float estimatedFillHeightMeters,
            float dt,
            float3 fluidSecondMomentLocal)
        {
            SetContainedFluidLoad(
                fluidMassKg,
                fluidCenterOfMassLocal,
                estimatedFillHeightMeters,
                dt,
                fluidSecondMomentLocal,
                true);
        }

        private void SetContainedFluidLoad(
            float fluidMassKg,
            float3 fluidCenterOfMassLocal,
            float estimatedFillHeightMeters,
            float dt,
            float3 fluidSecondMomentLocal,
            bool hasMeasuredSecondMoment)
        {
            if (!IsInitialized || bucketConfig == null)
                return;

            if (!bucketConfig.enableContainedFluidLoad)
            {
                fluidMassKg = 0.0f;
                fluidCenterOfMassLocal = float3.zero;
                fluidSecondMomentLocal = float3.zero;
                hasMeasuredSecondMoment = false;
            }

            float rawFluidMass = math.max(fluidMassKg, 0.0f);
            float3 measuredFluidCenter = fluidCenterOfMassLocal;

            float scaledMass = math.clamp(
                fluidMassKg * math.max(bucketConfig.containedFluidMassScale, 0.0f),
                0.0f,
                math.max(bucketConfig.maxContainedFluidMassKg, 0.1f));

            float maxHorizontalOffset = math.max(
                bucketConfig.maxFluidCenterOfMassOffsetMeters,
                0.0f);
            float2 horizontal = fluidCenterOfMassLocal.xz;
            float horizontalLength = math.length(horizontal);
            if (horizontalLength > maxHorizontalOffset && horizontalLength > 1e-7f)
                horizontal *= maxHorizontalOffset / horizontalLength;

            float halfHeight = math.max(bucketConfig.heightMeters * 0.5f, 0.025f);
            float3 targetFluidCenter = new float3(
                horizontal.x,
                math.clamp(fluidCenterOfMassLocal.y, -halfHeight, halfHeight),
                horizontal.y);

            BucketState state = _data.State;
            float3 oldCenterOfMassLocal = state.centerOfMassLocal;
            float oldFluidMass = state.containedFluidMass;
            float oldTotalMass = state.mass;
            float3 oldLocalAngularVelocity = math.rotate(
                math.inverse(state.rotation),
                state.angularVelocity);
            float3 localAngularMomentum =
                state.inertiaTensorBody * oldLocalAngularVelocity;
            float3 currentFluidCenter = oldFluidMass > 1e-6f
                ? state.centerOfMassLocal * oldTotalMass / oldFluidMass
                : targetFluidCenter;
            float response = 1.0f - math.exp(
                -math.max(bucketConfig.fluidLoadResponsePerSecond, 0.1f) *
                math.max(dt, 0.0f));

            float3 targetFluidInertiaAtCenter =
                ComputeTargetFluidInertiaAtCenter(
                    rawFluidMass,
                    scaledMass,
                    measuredFluidCenter,
                    fluidSecondMomentLocal,
                    estimatedFillHeightMeters,
                    hasMeasuredSecondMoment);
            _containedFluidInertiaAtCenter = math.lerp(
                _containedFluidInertiaAtCenter,
                targetFluidInertiaAtCenter,
                response);

            state.containedFluidMass = math.lerp(
                state.containedFluidMass,
                scaledMass,
                response);

            currentFluidCenter = math.lerp(
                currentFluidCenter,
                targetFluidCenter,
                response);

            state.mass = state.dryMass + state.containedFluidMass;
            state.inverseMass = 1.0f / math.max(state.mass, 0.01f);
            state.centerOfMassLocal =
                currentFluidCenter *
                (state.containedFluidMass / math.max(state.mass, 0.01f));

            float3 originVelocity =
                state.velocity -
                math.cross(
                    state.angularVelocity,
                    math.rotate(state.rotation, oldCenterOfMassLocal));
            state.velocity =
                originVelocity +
                math.cross(
                    state.angularVelocity,
                    math.rotate(state.rotation, state.centerOfMassLocal));

            state.inertiaTensorBody = ComputeCombinedInertiaTensor(
                state,
                currentFluidCenter);
            state.inverseInertiaTensorBody = new float3(
                1.0f / math.max(state.inertiaTensorBody.x, 1e-6f),
                1.0f / math.max(state.inertiaTensorBody.y, 1e-6f),
                1.0f / math.max(state.inertiaTensorBody.z, 1e-6f));
            float3 newLocalAngularVelocity =
                localAngularMomentum * state.inverseInertiaTensorBody;
            state.angularVelocity = math.rotate(
                state.rotation,
                newLocalAngularVelocity);

            _data.State = state;
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public void ApplyCouplingCorrection(
            Vector3 linearCorrection,
            Vector3 angularCorrection,
            float dt,
            bool updateVelocity)
        {
            if (!IsInitialized)
                return;

            BucketState state = _data.State;
            float3 centerOfMassWorld = GetCenterOfMassWorldFloat3();

            float3 dp = new float3(
                linearCorrection.x,
                linearCorrection.y,
                linearCorrection.z
            );

            float3 dTheta = new float3(
                angularCorrection.x,
                angularCorrection.y,
                angularCorrection.z
            );

            centerOfMassWorld += dp;

            float angle = math.length(dTheta);
            if (angle > 1e-8f)
            {
                float3 axis = dTheta / angle;
                quaternion dq = quaternion.AxisAngle(axis, angle);
                state.rotation = math.normalize(math.mul(dq, state.rotation));
            }

            state.position =
                centerOfMassWorld -
                math.rotate(state.rotation, state.centerOfMassLocal);

            if (updateVelocity && dt > 1e-8f)
            {
                state.velocity += dp / dt;
                state.angularVelocity += dTheta / dt;
            }

            _data.State = state;

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        public void ReconstructVelocityFromConstrainedPose(float dt)
        {
            if (!IsInitialized || !_hasStepStartPose || dt <= 1e-8f)
                return;

            BucketState state = _data.State;
            float inverseDt = 1.0f / dt;
            float3 finalCenterOfMass = GetCenterOfMassWorldFloat3();
            state.velocity =
                (finalCenterOfMass - _stepStartCenterOfMassWorld) * inverseDt;

            quaternion delta = math.normalize(
                math.mul(state.rotation, math.inverse(_stepStartRotation)));
            if (delta.value.w < 0.0f)
                delta.value = -delta.value;

            float sinHalfAngle = math.length(delta.value.xyz);
            if (sinHalfAngle > 1e-7f)
            {
                float angle = 2.0f * math.atan2(
                    sinHalfAngle,
                    math.clamp(delta.value.w, -1.0f, 1.0f));
                state.angularVelocity =
                    delta.value.xyz / sinHalfAngle * (angle * inverseDt);
            }
            else
            {
                state.angularVelocity = float3.zero;
            }

            _data.State = state;
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }

        private float3 GetCenterOfMassWorldFloat3()
        {
            BucketState state = _data.State;
            return state.position + math.rotate(state.rotation, state.centerOfMassLocal);
        }

        private Vector3 GetPhysicalAttachmentLocalPoint()
        {
            return bucketConfig != null
                ? bucketConfig.GetResolvedAttachmentLocalPoint()
                : Vector3.zero;
        }

        private float3 ComputeCombinedInertiaTensor(
            BucketState state,
            float3 fluidCenterLocal)
        {
            float3 dryInertia = ComputeBodyInertiaTensor();
            float3 combinedCenter = state.centerOfMassLocal;
            float3 dryOffset = -combinedCenter;
            dryInertia += ParallelAxisDiagonal(state.dryMass, dryOffset);

            float fluidMass = math.max(state.containedFluidMass, 0.0f);
            if (fluidMass <= 1e-6f)
                return dryInertia;

            float3 fluidInertia = math.max(
                _containedFluidInertiaAtCenter,
                new float3(1e-6f));
            fluidInertia += ParallelAxisDiagonal(
                fluidMass,
                fluidCenterLocal - combinedCenter);

            return dryInertia + fluidInertia;
        }

        private float3 ComputeTargetFluidInertiaAtCenter(
            float rawFluidMass,
            float scaledFluidMass,
            float3 measuredFluidCenter,
            float3 measuredSecondMoment,
            float estimatedFillHeightMeters,
            bool hasMeasuredSecondMoment)
        {
            if (scaledFluidMass <= 1e-6f)
                return float3.zero;

            float inertiaScale = math.max(
                bucketConfig.fluidInertiaScale,
                0.1f);
            if (hasMeasuredSecondMoment && rawFluidMass > 1e-6f)
            {
                float massScale = scaledFluidMass / rawFluidMass;
                float3 secondMoment =
                    math.max(measuredSecondMoment, float3.zero) * massScale;
                float3 inertiaAboutOrigin = new float3(
                    secondMoment.y + secondMoment.z,
                    secondMoment.x + secondMoment.z,
                    secondMoment.x + secondMoment.y);
                float3 inertiaAtFluidCenter =
                    inertiaAboutOrigin -
                    ParallelAxisDiagonal(
                        scaledFluidMass,
                        measuredFluidCenter);
                return math.max(
                    inertiaAtFluidCenter * inertiaScale,
                    new float3(1e-6f));
            }

            float radius = math.max(
                bucketConfig.GetRepresentativeRadius() -
                bucketConfig.wallThicknessMeters,
                0.01f);
            float height = math.clamp(
                estimatedFillHeightMeters,
                0.01f,
                math.max(bucketConfig.heightMeters, 0.01f));
            float radialInertia =
                (1.0f / 12.0f) *
                scaledFluidMass *
                (3.0f * radius * radius + height * height) *
                inertiaScale;
            float axialInertia =
                0.5f * scaledFluidMass * radius * radius * inertiaScale;
            return new float3(
                radialInertia,
                axialInertia,
                radialInertia);
        }

        private static float3 ParallelAxisDiagonal(float mass, float3 offset)
        {
            float distanceSquared = math.lengthsq(offset);
            return mass * new float3(
                distanceSquared - offset.x * offset.x,
                distanceSquared - offset.y * offset.y,
                distanceSquared - offset.z * offset.z);
        }
    }
}
