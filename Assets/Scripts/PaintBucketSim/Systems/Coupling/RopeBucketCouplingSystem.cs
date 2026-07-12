using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Rope;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Coupling
{
    public class RopeBucketCouplingSystem : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private RopeBucketCouplingConfig couplingConfig;

        [Header("Systems")]
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private BucketSystem bucketSystem;

        private RopeBucketCouplingDiagnostics _diagnostics;
        private float _twistRestOffsetRadians;
        private bool _twistRestInitialized;

        public RopeBucketCouplingDiagnostics Diagnostics => _diagnostics;
        public RopeBucketCouplingConfig Config => couplingConfig;

        public bool IsReady =>
            couplingConfig != null &&
            ropeSystem != null &&
            bucketSystem != null &&
            ropeSystem.IsInitialized &&
            bucketSystem.IsInitialized;

        private void Awake()
        {
            if (ropeSystem == null)
                ropeSystem = FindAnyObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();
        }

        public void Initialize(SimulationContext context)
        {
            if (couplingConfig == null)
            {
                Debug.LogWarning("RopeBucketCouplingSystem: Missing Coupling Config. Coupling disabled.");
            }

            if (ropeSystem == null)
                ropeSystem = FindAnyObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            _diagnostics = default;
            _twistRestOffsetRadians = 0.0f;
            _twistRestInitialized = false;

            if (couplingConfig != null &&
                couplingConfig.enableCoupling &&
                couplingConfig.snapBucketToRopeOnInitialize &&
                IsReady)
            {
                Vector3 correction =
                    ropeSystem.GetRopeEndPosition() -
                    bucketSystem.GetAttachmentWorldPosition();

                bucketSystem.ApplyCouplingCorrection(
                    correction,
                    Vector3.zero,
                    1.0f,
                    false
                );
            }

            UpdateEndpointPayload();
            InitializeTwistRestOffset();

        }

        public void ResetSystem(SimulationContext context)
        {
            _diagnostics = default;
            _twistRestOffsetRadians = 0.0f;
            _twistRestInitialized = false;
            UpdateEndpointPayload();
            InitializeTwistRestOffset();
        }

        public void Step(SimulationContext context, float dt)
        {
            if (couplingConfig == null || !couplingConfig.enableCoupling)
            {
                _diagnostics.enabled = 0;
                return;
            }

            _diagnostics.enabled = 1;

            if (!IsReady || ropeSystem.IsBroken)
            {
                _diagnostics.active = 0;
                return;
            }

            _diagnostics.active = 1;
            _diagnostics.iterations = couplingConfig.solverIterations;
            UpdateEndpointPayload();
            ConstrainGrabbedBucketToRopeReach(dt);
            bucketSystem.DriveBailTowardWorldPoint(
                ropeSystem.GetRopeEndPosition(),
                dt);

            Vector3 totalRopeCorrection = Vector3.zero;
            Vector3 totalBucketLinearCorrection = Vector3.zero;
            Vector3 totalBucketAngularCorrection = Vector3.zero;

            float maxError = 0.0f;

            float alpha = couplingConfig.attachmentCompliance / Mathf.Max(dt * dt, 1e-8f);

            // XPBD lambdas for 3 scalar axes of the vector constraint.
            Vector3 lambda = Vector3.zero;

            for (int iteration = 0; iteration < couplingConfig.solverIterations; iteration++)
            {
                SolveAxis(
                    Vector3.right,
                    alpha,
                    dt,
                    ref lambda.x,
                    ref totalRopeCorrection,
                    ref totalBucketLinearCorrection,
                    ref totalBucketAngularCorrection,
                    ref maxError
                );

                SolveAxis(
                    Vector3.up,
                    alpha,
                    dt,
                    ref lambda.y,
                    ref totalRopeCorrection,
                    ref totalBucketLinearCorrection,
                    ref totalBucketAngularCorrection,
                    ref maxError
                );

                SolveAxis(
                    Vector3.forward,
                    alpha,
                    dt,
                    ref lambda.z,
                    ref totalRopeCorrection,
                    ref totalBucketLinearCorrection,
                    ref totalBucketAngularCorrection,
                    ref maxError
                );
            }

            CloseGrabAttachmentResidual(
                dt,
                ref totalBucketLinearCorrection,
                ref maxError);

            Vector3 ropeEnd = ropeSystem.GetRopeEndPosition();
            Vector3 attach = bucketSystem.GetAttachmentWorldPosition();
            float finalError = (ropeEnd - attach).magnitude;

            if (couplingConfig.correctBucket &&
                couplingConfig.reconstructBucketVelocityFromFinalPose)
            {
                bucketSystem.ReconstructVelocityFromConstrainedPose(dt);
            }

            SynchronizeGrabHandoffVelocity(dt);

            Vector3 gravity = context != null
                ? context.EnvironmentState.gravity
                : Physics.gravity;
            float attachmentImpulse =
                ApplyAttachmentVelocityStabilization(dt, gravity);

            _diagnostics.attachmentError = finalError;
            _diagnostics.maxAttachmentError = maxError;
            _diagnostics.estimatedConstraintForce =
                totalBucketLinearCorrection.magnitude / Mathf.Max(dt * dt, 1e-8f);
            _diagnostics.attachmentImpulse = attachmentImpulse;

            _diagnostics.ropeEndPosition = ToFloat3(ropeEnd);
            _diagnostics.bucketAttachmentPosition = ToFloat3(attach);

            _diagnostics.totalRopeCorrection = ToFloat3(totalRopeCorrection);
            _diagnostics.totalBucketLinearCorrection = ToFloat3(totalBucketLinearCorrection);
            _diagnostics.totalBucketAngularCorrection = ToFloat3(totalBucketAngularCorrection);

            ApplyTwistCoupling(dt);
            ApplyBailUprightTorque();
        }

        private void ConstrainGrabbedBucketToRopeReach(float dt)
        {
            if (!bucketSystem.IsGrabActive || !couplingConfig.correctBucket)
                return;

            float maximumLength = ropeSystem.GetMaximumReachableLength();
            if (float.IsInfinity(maximumLength))
                return;

            Vector3 pivot = ropeSystem.GetSimulatedPivotPosition();
            Vector3 offset =
                bucketSystem.GetAttachmentWorldPosition() - pivot;
            float distance = offset.magnitude;
            if (distance <= maximumLength || distance <= 1e-6f)
                return;

            Vector3 correction =
                -offset * ((distance - maximumLength) / distance);
            bucketSystem.ApplyCouplingCorrection(
                correction,
                Vector3.zero,
                dt,
                false);
        }

        private void CloseGrabAttachmentResidual(
            float dt,
            ref Vector3 totalBucketLinearCorrection,
            ref float maxError)
        {
            if (!bucketSystem.IsGrabActive || !couplingConfig.correctBucket)
                return;

            Vector3 residual =
                ropeSystem.GetRopeEndPosition() -
                bucketSystem.GetAttachmentWorldPosition();
            float error = residual.magnitude;
            if (error <= 1e-6f)
                return;

            maxError = Mathf.Max(maxError, error);
            bucketSystem.ApplyCouplingCorrection(
                residual,
                Vector3.zero,
                dt,
                false);
            totalBucketLinearCorrection += residual;
        }

        private void UpdateEndpointPayload()
        {
            if (ropeSystem == null || bucketSystem == null ||
                !ropeSystem.IsInitialized || !bucketSystem.IsInitialized)
            {
                return;
            }

            ropeSystem.SetEndpointPayloadMass(bucketSystem.State.mass);
        }

        private void InitializeTwistRestOffset()
        {
            if (TryGetBucketToRopeTwist(out float relativeTwist, out _))
            {
                _twistRestOffsetRadians = relativeTwist;
                _twistRestInitialized = true;
            }
        }

        private void ApplyTwistCoupling(float dt)
        {
            _diagnostics.twistEnabled = 0;
            _diagnostics.targetTwistRadians = 0.0f;
            _diagnostics.endpointTwistRadians = 0.0f;
            _diagnostics.twistErrorRadians = 0.0f;
            _diagnostics.appliedTwistTorque = 0.0f;

            if (!couplingConfig.enableTwistCoupling ||
                ropeSystem == null ||
                bucketSystem == null ||
                ropeSystem.IsBroken)
            {
                return;
            }

            if (!TryGetBucketToRopeTwist(
                out float relativeTwist,
                out Vector3 tangent))
            {
                return;
            }

            if (!_twistRestInitialized)
            {
                _twistRestOffsetRadians = relativeTwist;
                _twistRestInitialized = true;
                return;
            }

            float endpointTwist = ropeSystem.GetRopeEndTwistRadians();
            float error = NormalizeAngleRadians(
                relativeTwist - _twistRestOffsetRadians);

            BucketState state = bucketSystem.State;
            Vector3 bucketAngularVelocity = ToVector3(state.angularVelocity);
            // The light endpoint frame carries solver-scale torsional velocity;
            // its damping belongs to the rod solve, not the bucket joint.
            float bucketAxialAngularVelocity =
                Vector3.Dot(bucketAngularVelocity, tangent);

            float torqueMagnitude =
                error * couplingConfig.twistTorqueStiffness +
                bucketAxialAngularVelocity *
                couplingConfig.twistTorqueDamping;

            torqueMagnitude = Mathf.Clamp(
                torqueMagnitude,
                -couplingConfig.maxTwistTorque,
                couplingConfig.maxTwistTorque
            );

            if (Mathf.Abs(torqueMagnitude) > 1e-6f)
            {
                ropeSystem.AddEndpointTwistTorque(torqueMagnitude);
                bucketSystem.AddTorque(ToFloat3(-tangent * torqueMagnitude));
            }

            _diagnostics.twistEnabled = 1;
            _diagnostics.targetTwistRadians = relativeTwist;
            _diagnostics.endpointTwistRadians = endpointTwist;
            _diagnostics.twistErrorRadians = error;
            _diagnostics.appliedTwistTorque = torqueMagnitude;
        }

        private bool TryGetBucketToRopeTwist(
            out float relativeTwist,
            out Vector3 tangent)
        {
            relativeTwist = 0.0f;
            tangent = Vector3.down;

            if (ropeSystem == null || bucketSystem == null ||
                !ropeSystem.IsInitialized || !bucketSystem.IsInitialized)
            {
                return false;
            }

            tangent = ropeSystem.GetRopeEndTangent();
            if (tangent.sqrMagnitude < 1e-8f)
                return false;
            tangent.Normalize();

            Vector3 ropeNormal = Vector3.ProjectOnPlane(
                ropeSystem.GetRopeEndMaterialNormal(),
                tangent);
            BucketState state = bucketSystem.State;
            Quaternion bucketRotation = ToQuaternion(state.rotation);
            Vector3 bucketForward = Vector3.ProjectOnPlane(
                bucketRotation * Vector3.forward,
                tangent);

            if (ropeNormal.sqrMagnitude < 1e-8f ||
                bucketForward.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            ropeNormal.Normalize();
            bucketForward.Normalize();
            relativeTwist =
                Vector3.SignedAngle(ropeNormal, bucketForward, tangent) *
                Mathf.Deg2Rad;
            return true;
        }

        private void ApplyBailUprightTorque()
        {
            if (!couplingConfig.enableBailUprightTorque ||
                couplingConfig.maxBailUprightTorque <= 0.0f)
            {
                return;
            }

            Vector3 tangent = ropeSystem.GetRopeEndTangent();
            if (tangent.sqrMagnitude < 1e-8f)
                return;

            tangent.Normalize();
            Vector3 desiredUp = -tangent;

            BucketState state = bucketSystem.State;
            Quaternion rotation = ToQuaternion(state.rotation);
            Vector3 bucketUp = rotation * Vector3.up;
            Vector3 axis = Vector3.Cross(bucketUp, desiredUp);
            float axisLength = axis.magnitude;
            float dot = Mathf.Clamp(Vector3.Dot(bucketUp, desiredUp), -1.0f, 1.0f);
            float angle = Mathf.Atan2(axisLength, dot);
            float activationAngle =
                couplingConfig.bailUprightActivationAngleDegrees *
                Mathf.Deg2Rad;
            if (angle <= activationAngle)
                return;

            if (axisLength < 1e-6f)
            {
                if (dot > 0.0f)
                    return;

                axis = Vector3.ProjectOnPlane(rotation * Vector3.right, tangent);
                if (axis.sqrMagnitude < 1e-8f)
                    axis = Vector3.Cross(tangent, Vector3.forward);
            }

            axis.Normalize();
            Vector3 angularVelocity = ToVector3(state.angularVelocity);
            Vector3 transverseAngularVelocity =
                angularVelocity -
                tangent * Vector3.Dot(angularVelocity, tangent);
            float fullResponseAngle = Mathf.Max(
                couplingConfig.bailUprightFullResponseAngleDegrees *
                Mathf.Deg2Rad,
                activationAngle + Mathf.Deg2Rad);
            float response = Mathf.SmoothStep(
                0.0f,
                1.0f,
                Mathf.InverseLerp(activationAngle, fullResponseAngle, angle));
            Vector3 desiredAngularAcceleration =
                axis * (angle * couplingConfig.bailUprightStiffness) -
                transverseAngularVelocity * couplingConfig.bailUprightDamping;
            Vector3 torque =
                bucketSystem.MultiplyInertiaWorld(
                    desiredAngularAcceleration * response);
            torque = Vector3.ClampMagnitude(
                torque,
                couplingConfig.maxBailUprightTorque);

            bucketSystem.AddTorque(ToFloat3(torque));
        }

        private float ApplyAttachmentVelocityStabilization(
            float dt,
            Vector3 gravity)
        {
            if (!couplingConfig.enableVelocityStabilization)
                return 0.0f;

            float totalImpulse = 0.0f;

            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = i == 0
                    ? Vector3.right
                    : (i == 1 ? Vector3.up : Vector3.forward);
                Vector3 attachment = bucketSystem.GetAttachmentWorldPosition();
                Vector3 center = bucketSystem.GetCenterOfMassWorld();
                Vector3 r = attachment - center;

                float wRope = couplingConfig.correctRopeEnd
                    ? ropeSystem.GetRopeEndInverseMass()
                    : 0.0f;
                float bucketDynamicWeight = GetBucketDynamicWeight();
                bool correctBucketVelocity =
                    couplingConfig.correctBucket &&
                    bucketDynamicWeight > 1e-5f;
                float wBucketLinear = correctBucketVelocity
                    ? bucketSystem.GetInverseMass() * bucketDynamicWeight
                    : 0.0f;
                // Partial rotational ownership creates an artificial release kick.
                bool allowBucketRotation =
                    correctBucketVelocity &&
                    bucketSystem.GrabConstraintInfluence <= 1e-5f;

                Vector3 rxn = Vector3.Cross(r, axis);
                Vector3 inverseInertiaRxn = allowBucketRotation
                    ? bucketSystem.MultiplyInverseInertiaWorld(rxn) *
                      bucketDynamicWeight
                    : Vector3.zero;
                float wBucketAngular = allowBucketRotation
                    ? Vector3.Dot(rxn, inverseInertiaRxn)
                    : 0.0f;

                float denominator = wRope + wBucketLinear + wBucketAngular;
                if (denominator <= 1e-8f)
                    continue;

                float relativeVelocity = Vector3.Dot(
                    ropeSystem.GetRopeEndVelocity() -
                    bucketSystem.GetAttachmentWorldVelocity(),
                    axis);
                float impulseScalar =
                    -relativeVelocity *
                    couplingConfig.attachmentVelocityDamping /
                    denominator;
                float supportImpulse =
                    bucketSystem.State.mass *
                    Mathf.Abs(Vector3.Dot(gravity, axis)) *
                    dt *
                    1.15f;
                float impulseLimit = Mathf.Max(
                    couplingConfig.maxAttachmentImpulse,
                    supportImpulse);
                impulseScalar = Mathf.Clamp(
                    impulseScalar,
                    -impulseLimit,
                    impulseLimit);

                Vector3 impulse = axis * impulseScalar;
                if (couplingConfig.correctRopeEnd)
                    ropeSystem.ApplyRopeEndImpulse(impulse, dt);
                if (correctBucketVelocity)
                {
                    Vector3 bucketImpulsePoint = allowBucketRotation
                        ? attachment
                        : center;
                    bucketSystem.ApplyImpulseAtWorldPoint(
                        -impulse * bucketDynamicWeight,
                        bucketImpulsePoint);
                }

                totalImpulse += Mathf.Abs(impulseScalar);
            }

            return totalImpulse;
        }

        private void SolveAxis(
            Vector3 axis,
            float alpha,
            float dt,
            ref float lambdaAxis,
            ref Vector3 totalRopeCorrection,
            ref Vector3 totalBucketLinearCorrection,
            ref Vector3 totalBucketAngularCorrection,
            ref float maxError)
        {
            Vector3 ropeEnd = ropeSystem.GetRopeEndPosition();
            Vector3 attachment = bucketSystem.GetAttachmentWorldPosition();
            Vector3 bucketCenter = bucketSystem.GetCenterOfMassWorld();

            Vector3 cVector = ropeEnd - attachment;
            float c = Vector3.Dot(cVector, axis);

            maxError = Mathf.Max(maxError, Mathf.Abs(cVector.magnitude));

            float wRope = couplingConfig.correctRopeEnd
                ? ropeSystem.GetRopeEndInverseMass()
                : 0.0f;

            float bucketDynamicWeight = GetBucketDynamicWeight();
            float grabInfluence = Mathf.Clamp01(
                bucketSystem.GrabConstraintInfluence);
            bool correctBucketPose =
                couplingConfig.correctBucket &&
                bucketDynamicWeight > 1e-5f;
            float wBucketLinear = correctBucketPose
                ? bucketSystem.GetInverseMass() * bucketDynamicWeight
                : 0.0f;
            bool allowBucketRotation =
                correctBucketPose && grabInfluence <= 1e-5f;

            Vector3 r = attachment - bucketCenter;

            // Effective rotational inverse mass:
            // (r x n)^T I^-1 (r x n)
            Vector3 rxn = Vector3.Cross(r, axis);
            Vector3 iInvRxn = allowBucketRotation
                ? bucketSystem.MultiplyInverseInertiaWorld(rxn) *
                  bucketDynamicWeight
                : Vector3.zero;

            float wBucketAngular = allowBucketRotation
                ? Vector3.Dot(rxn, iInvRxn)
                : 0.0f;

            float denominator = wRope + wBucketLinear + wBucketAngular + alpha;
            if (denominator <= 1e-8f)
                return;

            float deltaLambda = (-c - alpha * lambdaAxis) / denominator;
            Vector3 ropeCorrection = wRope * deltaLambda * axis;
            Vector3 bucketLinearCorrection = -wBucketLinear * deltaLambda * axis;

            // Rotation gradient sign for bucket is -(r x axis).
            Vector3 bucketAngularCorrection = -deltaLambda * iInvRxn;

            float correctionScale = 1.0f;
            if (couplingConfig.correctRopeEnd)
            {
                correctionScale = Mathf.Min(
                    correctionScale,
                    GetCorrectionScale(
                        totalRopeCorrection,
                        ropeCorrection,
                        couplingConfig.maxLinearCorrectionPerSubstep));
            }

            if (correctBucketPose)
            {
                correctionScale = Mathf.Min(
                    correctionScale,
                    GetCorrectionScale(
                        totalBucketLinearCorrection,
                        bucketLinearCorrection,
                        couplingConfig.maxLinearCorrectionPerSubstep));
                if (allowBucketRotation)
                {
                    correctionScale = Mathf.Min(
                        correctionScale,
                        GetCorrectionScale(
                            totalBucketAngularCorrection,
                            bucketAngularCorrection,
                            couplingConfig.maxAngularCorrectionPerSubstep));
                }
            }

            deltaLambda *= correctionScale;
            lambdaAxis += deltaLambda;
            ropeCorrection *= correctionScale;
            bucketLinearCorrection *= correctionScale;
            bucketAngularCorrection *= correctionScale;

            if (couplingConfig.correctRopeEnd)
            {
                ropeSystem.ApplyRopeEndPositionCorrection(
                    ropeCorrection,
                    dt,
                    couplingConfig.updateVelocitiesFromCorrection &&
                    !couplingConfig.reconstructBucketVelocityFromFinalPose &&
                    !couplingConfig.enableVelocityStabilization
                );

                totalRopeCorrection += ropeCorrection;
            }

            if (correctBucketPose)
            {
                bucketSystem.ApplyCouplingCorrection(
                    bucketLinearCorrection,
                    bucketAngularCorrection,
                    dt,
                    couplingConfig.updateVelocitiesFromCorrection &&
                    !couplingConfig.reconstructBucketVelocityFromFinalPose &&
                    !couplingConfig.enableVelocityStabilization
                );

                totalBucketLinearCorrection += bucketLinearCorrection;
                totalBucketAngularCorrection += bucketAngularCorrection;
            }
        }

        private float GetCorrectionScale(
            Vector3 accumulated,
            Vector3 increment,
            float maxMagnitude)
        {
            if (increment.sqrMagnitude < 1e-12f)
                return 1.0f;

            float limit = Mathf.Max(maxMagnitude, 0.0f);
            if ((accumulated + increment).sqrMagnitude <= limit * limit)
                return 1.0f;

            float a = Vector3.Dot(increment, increment);
            float b = 2.0f * Vector3.Dot(accumulated, increment);
            float c = Vector3.Dot(accumulated, accumulated) - limit * limit;
            float discriminant = Mathf.Max(b * b - 4.0f * a * c, 0.0f);
            float exitScale = (-b + Mathf.Sqrt(discriminant)) / (2.0f * a);
            return Mathf.Clamp01(exitScale);
        }

        private float GetBucketDynamicWeight()
        {
            float weight = 1.0f - Mathf.Clamp01(
                bucketSystem.GrabConstraintInfluence);
            return weight * weight * (3.0f - 2.0f * weight);
        }

        private void SynchronizeGrabHandoffVelocity(float dt)
        {
            if (bucketSystem.GrabConstraintInfluence <= 1e-5f ||
                !couplingConfig.correctRopeEnd)
                return;

            float inverseMass = ropeSystem.GetRopeEndInverseMass();
            if (inverseMass <= 1e-8f)
                return;

            Vector3 velocityDelta =
                bucketSystem.GetAttachmentWorldVelocity() -
                ropeSystem.GetRopeEndVelocity();
            ropeSystem.ApplyRopeEndImpulse(
                velocityDelta / inverseMass,
                dt);
        }

        private float3 ToFloat3(Vector3 v)
        {
            return new float3(v.x, v.y, v.z);
        }

        private Vector3 ToVector3(float3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }

        private Quaternion ToQuaternion(quaternion q)
        {
            return new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        }

        private static float NormalizeAngleRadians(float angle)
        {
            float twoPi = Mathf.PI * 2.0f;
            angle += Mathf.PI;
            angle -= twoPi * Mathf.Floor(angle / twoPi);
            return angle - Mathf.PI;
        }
    }
}
