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

        [Tooltip("Remove the initial rope/bucket attachment gap without injecting correction velocity.")]
        public bool snapBucketToRopeOnInitialize = true;

        [Tooltip("Lower value means stronger attachment. 0 is very rigid.")]
        [Min(0.0f)]
        public float attachmentCompliance = 1e-7f;

        [Range(1, 16)]
        public int solverIterations = 4;

        [Header("Correction Distribution")]
        [Tooltip("Allow correcting rope endpoint position.")]
        public bool correctRopeEnd = false;

        [Tooltip("Allow correcting bucket position and rotation.")]
        public bool correctBucket = true;

        [Tooltip("Limits the maximum linear correction per substep for stability.")]
        [Min(0.001f)]
        public float maxLinearCorrectionPerSubstep = 0.035f;

        [Tooltip("Limits the maximum angular correction per substep in radians.")]
        [Min(0.001f)]
        public float maxAngularCorrectionPerSubstep = 0.06f;

        [Header("Velocity Update")]
        [Tooltip("Compatibility mode that adds each positional correction to velocity. Keep disabled when pose reconstruction is enabled.")]
        public bool updateVelocitiesFromCorrection = false;

        [Tooltip("Rebuild rigid-body velocity once from the final constrained pose. This is the energy-stable PBD velocity update.")]
        public bool reconstructBucketVelocityFromFinalPose = true;

        [Tooltip("Resolve attachment relative velocity with a mass-aware impulse after positional projection.")]
        public bool enableVelocityStabilization = false;

        [Range(0.0f, 1.0f)]
        public float attachmentVelocityDamping = 0.9f;

        [Tooltip("Maximum attachment impulse per substep in N*s.")]
        [Min(0.01f)]
        public float maxAttachmentImpulse = 3.0f;

        [Header("Twist Coupling")]
        [Tooltip("Drive scalar rope torsion from the bucket orientation around the rope-end tangent.")]
        public bool enableTwistCoupling = false;

        [Tooltip("Torque applied back to the bucket around the rope axis per radian of twist error.")]
        [Min(0.0f)]
        public float twistTorqueStiffness = 0.45f;

        [Tooltip("Angular damping torque around the rope axis.")]
        [Min(0.0f)]
        public float twistTorqueDamping = 0.04f;

        [Tooltip("Clamp for the bucket twist reaction torque.")]
        [Min(0.0f)]
        public float maxTwistTorque = 1.2f;

        [Header("Bail Suspension")]
        [Tooltip("Prevent extreme bucket inversion while preserving natural free swing and axial twist.")]
        public bool enableBailUprightTorque = false;

        [Tooltip("No artificial upright torque is applied below this tilt angle.")]
        [Range(0.0f, 89.0f)]
        public float bailUprightActivationAngleDegrees = 55.0f;

        [Tooltip("The anti-inversion response reaches full strength at this tilt angle.")]
        [Range(1.0f, 179.0f)]
        public float bailUprightFullResponseAngleDegrees = 82.0f;

        [Tooltip("Desired angular acceleration per radian beyond the natural-motion range.")]
        [Min(0.0f)]
        public float bailUprightStiffness = 5.0f;

        [Tooltip("Angular velocity damping used by the inertia-aware anti-inversion response.")]
        [Min(0.0f)]
        public float bailUprightDamping = 0.65f;

        [Min(0.0f)]
        public float maxBailUprightTorque = 8.0f;

        [Header("Diagnostics")]
        public bool enableDiagnostics = true;

        private void OnValidate()
        {
            if (solverIterations < 1)
                solverIterations = 1;

            if (maxLinearCorrectionPerSubstep < 0.001f)
                maxLinearCorrectionPerSubstep = 0.001f;

            if (maxAngularCorrectionPerSubstep < 0.001f)
                maxAngularCorrectionPerSubstep = 0.001f;

            if (maxAttachmentImpulse < 0.01f)
                maxAttachmentImpulse = 0.01f;

            if (twistTorqueStiffness < 0.0f)
                twistTorqueStiffness = 0.0f;

            if (twistTorqueDamping < 0.0f)
                twistTorqueDamping = 0.0f;

            if (maxTwistTorque < 0.0f)
                maxTwistTorque = 0.0f;

            bailUprightStiffness = Mathf.Max(bailUprightStiffness, 0.0f);
            bailUprightDamping = Mathf.Max(bailUprightDamping, 0.0f);
            maxBailUprightTorque = Mathf.Max(maxBailUprightTorque, 0.0f);
            bailUprightFullResponseAngleDegrees = Mathf.Max(
                bailUprightFullResponseAngleDegrees,
                bailUprightActivationAngleDegrees + 1.0f);
        }
    }
}
