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
        [Header("Config")]
        [SerializeField] private BucketConfig bucketConfig;

        [Header("Pose Reference")]
        [Tooltip("Used for initial pose and for KinematicFollowTransform mode.")]
        [SerializeField] private Transform poseReferenceTransform;

        private BucketData _data;
        private bool _initialized;

        private float3 _externalForce;
        private float3 _externalTorque;

        public BucketConfig Config => bucketConfig;
        public bool IsInitialized => _initialized && _data != null;
        public BucketState State => _data != null ? _data.State : default;
        public BucketAttachmentWorldState Attachment => _data != null ? _data.Attachment : default;
        public BucketHoleWorldState[] Holes => _data != null ? _data.Holes : null;
        public BucketDiagnostics Diagnostics => _data != null ? _data.Diagnostics : default;

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

            InitializeStateFromReference();
            UpdateAttachmentAndHoles();
            UpdateDiagnostics();

            _externalForce = float3.zero;
            _externalTorque = float3.zero;

            _initialized = true;
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized)
                return;

            if (bucketConfig.motionMode == BucketMotionMode.KinematicFollowTransform)
            {
                ReadStateFromReferenceTransform();
                UpdateAttachmentAndHoles();
                UpdateDiagnostics();
                return;
            }

            if (bucketConfig.motionMode == BucketMotionMode.LockedInitialPose)
            {
                UpdateAttachmentAndHoles();
                UpdateDiagnostics();
                return;
            }

            IntegrateDynamic(context, dt);

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();

            ClearExternalForces();
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

            float3 r = worldPoint - _data.State.position;
            _externalTorque += math.cross(r, force);
        }

        public void ClearExternalForces()
        {
            _externalForce = float3.zero;
            _externalTorque = float3.zero;
        }

        public float3 LocalToWorldPoint(float3 localPoint)
        {
            return _data.State.position + math.rotate(_data.State.rotation, localPoint);
        }

        public float3 LocalToWorldVector(float3 localVector)
        {
            return math.rotate(_data.State.rotation, localVector);
        }

        public float3 GetWorldPointVelocity(float3 worldPoint)
        {
            float3 r = worldPoint - _data.State.position;
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
                inverseMass = 1.0f / math.max(bucketConfig.massKg, 0.01f)
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

        private void ReadStateFromReferenceTransform()
        {
            Vector3 p = poseReferenceTransform != null ? poseReferenceTransform.position : transform.position;
            Quaternion r = poseReferenceTransform != null ? poseReferenceTransform.rotation : transform.rotation;

            BucketState state = _data.State;

            float3 newPosition = new float3(p.x, p.y, p.z);
            quaternion newRotation = new quaternion(r.x, r.y, r.z, r.w);

            // Kinematic mode is mainly for visual/transform-driven testing.
            // Velocity is estimated weakly later when needed by coupling.
            state.position = newPosition;
            state.rotation = math.normalize(newRotation);
            state.velocity = float3.zero;
            state.angularVelocity = float3.zero;

            _data.State = state;
        }

        private void IntegrateDynamic(SimulationContext context, float dt)
        {
            BucketState state = _data.State;

            float3 force = _externalForce;
            float3 torque = _externalTorque;

            if (bucketConfig.applyGravityInDynamicMode && context != null)
                force += state.mass * (float3)context.EnvironmentState.gravity;

            float3 acceleration = force * state.inverseMass;
            state.velocity += acceleration * dt;

            float linearDamping = math.exp(-math.max(bucketConfig.linearDampingPerSecond, 0.0f) * dt);
            state.velocity *= linearDamping;

            state.position += state.velocity * dt;

            float3 angularAcceleration = WorldAngularAccelerationFromTorque(state, torque);
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

            _data.State = state;
        }

        private float3 WorldAngularAccelerationFromTorque(BucketState state, float3 worldTorque)
        {
            quaternion invRot = math.inverse(state.rotation);

            float3 localTorque = math.rotate(invRot, worldTorque);
            float3 localAlpha = localTorque * state.inverseInertiaTensorBody;

            return math.rotate(state.rotation, localAlpha);
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

            // Solid cylinder approximation.
            // For a real thin-walled bucket this is not perfect, but good enough for B1.
            float iXz = (1.0f / 12.0f) * m * (3.0f * r * r + h * h);
            float iY = 0.5f * m * r * r;

            return new float3(iXz, iY, iXz);
        }

        private void UpdateAttachmentAndHoles()
        {
            Vector3 attachmentLocalVector = bucketConfig.GetResolvedAttachmentLocalPoint();
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

                float3 worldCenter = LocalToWorldPoint(localCenter);
                float3 worldNormal = math.normalize(LocalToWorldVector(localNormal));
                float3 worldVelocity = GetWorldPointVelocity(worldCenter);

                float radius = math.max(holeConfig.radiusMeters, 0.001f);
                float area = math.PI * radius * radius;

                _data.Holes[i] = new BucketHoleWorldState
                {
                    active = holeConfig.active ? 1 : 0,
                    shape = (int)holeConfig.shape,

                    localCenter = localCenter,
                    localNormal = localNormal,

                    worldCenter = worldCenter,
                    worldNormal = worldNormal,
                    worldVelocity = worldVelocity,

                    radius = radius,
                    area = area,
                    wallThickness = math.max(holeConfig.wallThicknessMeters, bucketConfig.wallThicknessMeters)
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

            float3 p = _data.State.position;
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetAttachmentWorldPosition()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 p = _data.Attachment.worldPosition;
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetAttachmentWorldVelocity()
        {
            if (!IsInitialized)
                return Vector3.zero;

            float3 v = _data.Attachment.worldVelocity;
            return new Vector3(v.x, v.y, v.z);
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

        public void ApplyCouplingCorrection(
            Vector3 linearCorrection,
            Vector3 angularCorrection,
            float dt,
            bool updateVelocity)
        {
            if (!IsInitialized)
                return;

            BucketState state = _data.State;

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

            state.position += dp;

            float angle = math.length(dTheta);
            if (angle > 1e-8f)
            {
                float3 axis = dTheta / angle;
                quaternion dq = quaternion.AxisAngle(axis, angle);
                state.rotation = math.normalize(math.mul(dq, state.rotation));
            }

            if (updateVelocity && dt > 1e-8f)
            {
                state.velocity += dp / dt;
                state.angularVelocity += dTheta / dt;
            }

            _data.State = state;

            UpdateAttachmentAndHoles();
            UpdateDiagnostics();
        }
    }
}