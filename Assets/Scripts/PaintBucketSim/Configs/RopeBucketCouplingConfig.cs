using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "RopeBucketCouplingConfig",
        menuName = "Paint Bucket Sim/Rope Bucket Coupling Config")]
    public class RopeBucketCouplingConfig : ScriptableObject
    {
        [Header("Attachment Constraint")]
        [Tooltip("If true, the rope end is physically constrained to the bucket attachment point.")]
        public bool enableCoupling = true;

        [Tooltip("Lower value means stronger attachment. 0 is very rigid.")]
        [Min(0.0f)]
        public float attachmentCompliance = 1e-7f;

        [Range(1, 16)]
        public int solverIterations = 4;

        [Header("Correction Distribution")]
        [Tooltip("Allow correcting rope endpoint position.")]
        public bool correctRopeEnd = true;

        [Tooltip("Allow correcting bucket position and rotation.")]
        public bool correctBucket = true;

        [Tooltip("Limits the maximum linear correction per substep for stability.")]
        [Min(0.001f)]
        public float maxLinearCorrectionPerSubstep = 0.25f;

        [Tooltip("Limits the maximum angular correction per substep in radians.")]
        [Min(0.001f)]
        public float maxAngularCorrectionPerSubstep = 0.4f;

        [Header("Velocity Update")]
        [Tooltip("Apply velocity changes from positional corrections.")]
        public bool updateVelocitiesFromCorrection = true;

        [Header("Diagnostics")]
        public bool enableDiagnostics = true;

        [Header("Initialization")]
        public bool alignBucketAttachmentToRopeEndOnInitialize = true;

        public bool resetBucketVelocityAfterInitialAlignment = true;

        [Min(0.0f)]
        public float initialAlignmentWarningDistance = 0.02f;

        private void OnValidate()
        {
            if (solverIterations < 1)
                solverIterations = 1;

            if (maxLinearCorrectionPerSubstep < 0.001f)
                maxLinearCorrectionPerSubstep = 0.001f;

            if (maxAngularCorrectionPerSubstep < 0.001f)
                maxAngularCorrectionPerSubstep = 0.001f;
        }
    }
}