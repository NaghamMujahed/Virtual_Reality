using PaintBucketSim.Core;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Bucket
{
    public sealed class BucketDriveSystem : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private BucketSystem bucketSystem;

        [Header("Initial Impulse")]
        [SerializeField] private bool applyInitialImpulseOnInitialize = false;

        [SerializeField]
        private Vector3 initialImpulseDirectionWorld =
            Vector3.right;

        [SerializeField] private float initialImpulseNewtonSeconds = 0.4f;

        [SerializeField] private bool applyInitialImpulseAtAttachment = true;

        [SerializeField]
        private Vector3 initialImpulseLocalPoint =
            Vector3.zero;

        [Header("Initial Angular Impulse")]
        [SerializeField] private bool applyInitialAngularImpulseOnInitialize = false;

        [SerializeField]
        private Vector3 initialAngularImpulseWorld =
            new Vector3(0.0f, 0.0f, 0.05f);

        [Header("Spring Drag / VR Hand / Mouse Target")]
        [SerializeField] private bool enableSpringDrive = false;

        [Tooltip("This can be a VR hand target, mouse target, or any moving interaction point.")]
        [SerializeField] private Transform driveTargetTransform;

        [Tooltip("Local point on the bucket that is pulled by the target.")]
        [SerializeField]
        private Vector3 drivenLocalPoint =
            Vector3.zero;

        [SerializeField] private float springStiffness = 80.0f;

        [SerializeField] private float springDamping = 12.0f;

        [SerializeField] private float maxDriveForce = 150.0f;

        [Header("Debug Constant Force")]
        [SerializeField] private bool enableConstantForce = false;

        [SerializeField]
        private Vector3 constantForceWorld =
            Vector3.zero;

        [SerializeField] private bool applyConstantForceAtAttachment = true;

        private bool _initialImpulseApplied;
        private bool _hasLastTargetPosition;
        private Vector3 _lastTargetPosition;

        public bool IsEnabled =>
            bucketSystem != null &&
            bucketSystem.IsInitialized;

        private void Awake()
        {
            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();
        }

        public void Initialize(SimulationContext context)
        {
            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            _initialImpulseApplied = false;
            _hasLastTargetPosition = false;

            ApplyInitialMotionIfNeeded();
        }

        public void ResetSystem(SimulationContext context)
        {
            _initialImpulseApplied = false;
            _hasLastTargetPosition = false;

            ApplyInitialMotionIfNeeded();
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsEnabled || dt <= 1e-8f)
                return;

            ApplySpringDrive(dt);
            ApplyConstantForce();
        }

        private void ApplyInitialMotionIfNeeded()
        {
            if (!IsEnabled || _initialImpulseApplied)
                return;

            if (applyInitialImpulseOnInitialize)
            {
                Vector3 dir = initialImpulseDirectionWorld;

                if (dir.sqrMagnitude < 1e-8f)
                    dir = Vector3.right;

                dir.Normalize();

                Vector3 impulse =
                    dir * Mathf.Max(initialImpulseNewtonSeconds, 0.0f);

                if (applyInitialImpulseAtAttachment)
                {
                    Vector3 attachment =
                        bucketSystem.GetAttachmentWorldPosition();

                    bucketSystem.AddImpulseAtWorldPoint(
                        ToFloat3(impulse),
                        ToFloat3(attachment)
                    );
                }
                else
                {
                    float3 localPoint =
                        ToFloat3(initialImpulseLocalPoint);

                    float3 worldPoint =
                        bucketSystem.LocalToWorldPoint(localPoint);

                    bucketSystem.AddImpulseAtWorldPoint(
                        ToFloat3(impulse),
                        worldPoint
                    );
                }
            }

            if (applyInitialAngularImpulseOnInitialize)
            {
                bucketSystem.AddAngularImpulse(
                    ToFloat3(initialAngularImpulseWorld)
                );
            }

            _initialImpulseApplied = true;
        }

        private void ApplySpringDrive(float dt)
        {
            if (!enableSpringDrive || driveTargetTransform == null)
                return;

            Vector3 targetPosition =
                driveTargetTransform.position;

            Vector3 targetVelocity =
                Vector3.zero;

            if (_hasLastTargetPosition)
            {
                targetVelocity =
                    (targetPosition - _lastTargetPosition) /
                    Mathf.Max(dt, 1e-8f);
            }

            _lastTargetPosition = targetPosition;
            _hasLastTargetPosition = true;

            float3 localPoint =
                ToFloat3(drivenLocalPoint);

            float3 worldPointF =
                bucketSystem.LocalToWorldPoint(localPoint);

            Vector3 worldPoint =
                ToVector3(worldPointF);

            Vector3 pointVelocity =
                ToVector3(
                    bucketSystem.GetWorldPointVelocity(worldPointF)
                );

            Vector3 positionError =
                targetPosition - worldPoint;

            Vector3 velocityError =
                targetVelocity - pointVelocity;

            Vector3 force =
                springStiffness * positionError +
                springDamping * velocityError;

            force =
                Vector3.ClampMagnitude(
                    force,
                    Mathf.Max(maxDriveForce, 0.0f)
                );

            bucketSystem.AddForceAtWorldPoint(
                ToFloat3(force),
                worldPointF
            );
        }

        private void ApplyConstantForce()
        {
            if (!enableConstantForce)
                return;

            Vector3 force =
                constantForceWorld;

            if (force.sqrMagnitude < 1e-10f)
                return;

            if (applyConstantForceAtAttachment)
            {
                Vector3 attachment =
                    bucketSystem.GetAttachmentWorldPosition();

                bucketSystem.AddForceAtWorldPoint(
                    ToFloat3(force),
                    ToFloat3(attachment)
                );
            }
            else
            {
                bucketSystem.AddForce(
                    ToFloat3(force)
                );
            }
        }

        private static float3 ToFloat3(Vector3 v)
        {
            return new float3(v.x, v.y, v.z);
        }

        private static Vector3 ToVector3(float3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }
    }
}