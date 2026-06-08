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

        public RopeBucketCouplingDiagnostics Diagnostics => _diagnostics;

        public bool IsReady =>
            couplingConfig != null &&
            ropeSystem != null &&
            bucketSystem != null &&
            ropeSystem.IsInitialized &&
            bucketSystem.IsInitialized;

        private void Awake()
        {
            if (ropeSystem == null)
                ropeSystem = FindFirstObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();
        }

        public void Initialize(SimulationContext context)
        {
            if (couplingConfig == null)
            {
                Debug.LogWarning("RopeBucketCouplingSystem: Missing Coupling Config. Coupling disabled.");
            }

            if (ropeSystem == null)
                ropeSystem = FindFirstObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            _diagnostics = default;
        }

        public void ResetSystem(SimulationContext context)
        {
            _diagnostics = default;
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

            Vector3 ropeEnd = ropeSystem.GetRopeEndPosition();
            Vector3 attach = bucketSystem.GetAttachmentWorldPosition();
            float finalError = (ropeEnd - attach).magnitude;

            _diagnostics.attachmentError = finalError;
            _diagnostics.maxAttachmentError = maxError;
            _diagnostics.estimatedConstraintForce =
                totalBucketLinearCorrection.magnitude / Mathf.Max(dt * dt, 1e-8f);

            _diagnostics.ropeEndPosition = ToFloat3(ropeEnd);
            _diagnostics.bucketAttachmentPosition = ToFloat3(attach);

            _diagnostics.totalRopeCorrection = ToFloat3(totalRopeCorrection);
            _diagnostics.totalBucketLinearCorrection = ToFloat3(totalBucketLinearCorrection);
            _diagnostics.totalBucketAngularCorrection = ToFloat3(totalBucketAngularCorrection);
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

            float wBucketLinear = couplingConfig.correctBucket
                ? bucketSystem.GetInverseMass()
                : 0.0f;

            Vector3 r = attachment - bucketCenter;

            // Effective rotational inverse mass:
            // (r x n)^T I^-1 (r x n)
            Vector3 rxn = Vector3.Cross(r, axis);
            Vector3 iInvRxn = couplingConfig.correctBucket
                ? bucketSystem.MultiplyInverseInertiaWorld(rxn)
                : Vector3.zero;

            float wBucketAngular = couplingConfig.correctBucket
                ? Vector3.Dot(rxn, iInvRxn)
                : 0.0f;

            float denominator = wRope + wBucketLinear + wBucketAngular + alpha;
            if (denominator <= 1e-8f)
                return;

            float deltaLambda = (-c - alpha * lambdaAxis) / denominator;
            lambdaAxis += deltaLambda;

            Vector3 ropeCorrection = wRope * deltaLambda * axis;

            Vector3 bucketLinearCorrection = -wBucketLinear * deltaLambda * axis;

            // Rotation gradient sign for bucket is -(r x axis).
            Vector3 bucketAngularCorrection = -deltaLambda * iInvRxn;

            ropeCorrection = ClampVectorMagnitude(
                ropeCorrection,
                couplingConfig.maxLinearCorrectionPerSubstep
            );

            bucketLinearCorrection = ClampVectorMagnitude(
                bucketLinearCorrection,
                couplingConfig.maxLinearCorrectionPerSubstep
            );

            bucketAngularCorrection = ClampVectorMagnitude(
                bucketAngularCorrection,
                couplingConfig.maxAngularCorrectionPerSubstep
            );

            if (couplingConfig.correctRopeEnd)
            {
                ropeSystem.ApplyRopeEndPositionCorrection(
                    ropeCorrection,
                    dt,
                    couplingConfig.updateVelocitiesFromCorrection
                );

                totalRopeCorrection += ropeCorrection;
            }

            if (couplingConfig.correctBucket)
            {
                bucketSystem.ApplyCouplingCorrection(
                    bucketLinearCorrection,
                    bucketAngularCorrection,
                    dt,
                    couplingConfig.updateVelocitiesFromCorrection
                );

                totalBucketLinearCorrection += bucketLinearCorrection;
                totalBucketAngularCorrection += bucketAngularCorrection;
            }
        }

        private Vector3 ClampVectorMagnitude(Vector3 v, float maxMagnitude)
        {
            float mag = v.magnitude;
            if (mag <= maxMagnitude || mag < 1e-8f)
                return v;

            return v / mag * maxMagnitude;
        }

        private float3 ToFloat3(Vector3 v)
        {
            return new float3(v.x, v.y, v.z);
        }
    }
}