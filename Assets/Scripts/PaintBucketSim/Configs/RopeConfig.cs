using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum RopeComplianceMode
    {
        Manual = 0,
        MaterialBased = 1
    }

    public enum RopeIntegrationMode
    {
        SemiImplicitEuler = 0,
        PositionVerlet = 1
    }

    public enum RopeBendingModel
    {
        DistanceProxy = 0,
        AngleArccos = 1
    }

    public enum RopePivotMotionMode
    {
        StaticTransform = 0,
        Sinusoidal = 1,
        Noise = 2
    }

    public enum RopeAirDragMode
    {
        None = 0,
        Linear = 1,
        Quadratic = 2
    }

    public enum RopeDampingMode
    {
        None = 0,
        ExponentialVelocity = 1,
        RelativeSegment = 2,
        Combined = 3
    }

    [CreateAssetMenu(
        fileName = "RopeConfig",
        menuName = "Paint Bucket Sim/Rope Config")]
    public class RopeConfig : ScriptableObject
    {
        [Header("Geometry")]
        [Min(0.05f)]
        public float lengthMeters = 2.5f;

        [Range(2, 128)]
        public int segmentCount = 24;

        [Min(0.001f)]
        public float physicalRadiusMeters = 0.012f;

        [Min(0.001f)]
        public float visualRadiusMeters = 0.012f;

        public Vector3 initialDirection = new Vector3(0.25f, -1.0f, 0.0f);

        [Header("Mass")]
        [Min(0.0001f)]
        public float ropeMassKg = 0.12f;

        [Min(0.0f)]
        public float temporaryTipMassKg = 0.6f;

        [Header("Integration")]
        public RopeIntegrationMode integrationMode = RopeIntegrationMode.SemiImplicitEuler;

        [Header("Compliance")]
        public RopeComplianceMode complianceMode = RopeComplianceMode.MaterialBased;

        [Tooltip("Used only when Compliance Mode = Manual.")]
        [Min(0.0f)]
        public float manualStretchCompliance = 1e-6f;

        [Tooltip("Used only when Compliance Mode = Manual.")]
        [Min(0.0f)]
        public float manualBendCompliance = 5e-4f;

        [Tooltip("Young's modulus in Pascal. Nylon/rope values vary widely. This must be calibrated.")]
        [Min(1000.0f)]
        public float youngModulusPa = 1.0e8f;

        [Tooltip("Multiplier used to tune material-based stretch compliance.")]
        [Min(0.0001f)]
        public float stretchComplianceScale = 1.0f;

        [Tooltip("Multiplier used to tune material-based bend compliance. Bending is approximate in this rope model.")]
        [Min(0.0001f)]
        public float bendComplianceScale = 1.0f;

        [Header("XPBD Solver")]
        [Range(1, 64)]
        public int solverIterations = 12;

        [Tooltip("Apply a final unilateral strain limit. This keeps heavy endpoint payloads from defeating iterative XPBD convergence.")]
        public bool enforceMaximumSegmentStrain = true;

        [Tooltip("Maximum per-segment extension after the XPBD solve. Keep above Break Strain when strain breaking is enabled.")]
        [Range(0.0f, 0.6f)]
        public float maximumSegmentStrain = 0.02f;

        public RopeBendingModel bendingModel = RopeBendingModel.AngleArccos;
        public bool enableBending = true;

        [Tooltip("Rest angle in degrees. 0 means initially straight between neighboring segments.")]
        [Range(0.0f, 120.0f)]
        public float restBendAngleDegrees = 0.0f;

        [Header("Damping")]
        public RopeDampingMode dampingMode = RopeDampingMode.ExponentialVelocity;

        [Tooltip("Numerical velocity damping. Useful for stability but not a physical material model.")]
        [Range(0.0f, 10.0f)]
        public float exponentialDampingPerSecond = 0.05f;

        [Tooltip("Physical-like damping ratio for relative motion between rope particles.")]
        [Range(0.0f, 2.0f)]
        public float segmentDampingRatio = 0.0f;

        [Header("Break Condition")]
        public bool enableBreakByTension = true;

        [Tooltip("Approximate break tension in Newton. Needs calibration.")]
        [Min(0.01f)]
        public float breakTensionNewton = 120.0f;

        public bool enableBreakByStrain = true;

        [Tooltip("Relative segment strain. Example 0.35 = 35% stretch.")]
        [Range(0.01f, 3.0f)]
        public float breakStrain = 0.35f;

        [Header("Pivot Motion")]
        public RopePivotMotionMode pivotMotionMode = RopePivotMotionMode.StaticTransform;

        public Vector3 pivotMotionAmplitude = new Vector3(0.05f, 0.0f, 0.0f);

        [Min(0.0f)]
        public float pivotMotionFrequencyHz = 0.5f;

        [Min(0.0f)]
        public float pivotNoiseStrength = 0.02f;

        [Min(0.0f)]
        public float pivotNoiseSpeed = 0.5f;

        [Tooltip("Maximum physical anchor speed. Prevents low frame-rate Transform sampling from becoming a teleport impulse.")]
        [Min(0.1f)]
        public float maxPivotSpeedMetersPerSecond = 0.6f;

        [Header("Interactive Grab")]
        public bool enableInteractiveGrab = true;

        [Tooltip("Softness of the rope grab constraint. Lower values feel more direct; higher values feel springier.")]
        [Min(0.0f)]
        public float grabCompliance = 1e-6f;

        [Tooltip("Maximum world-space correction the grab constraint can request per solver iteration.")]
        [Min(0.001f)]
        public float maxGrabCorrectionPerIteration = 0.045f;

        [Tooltip("Maximum physical speed for the interactive grab target.")]
        [Min(0.1f)]
        public float maxGrabSpeedMetersPerSecond = 1.2f;

        [Header("Air Drag")]
        public RopeAirDragMode airDragMode = RopeAirDragMode.Quadratic;

        [Tooltip("Linear drag coefficient in kg/s per rope particle. Approximate, practical value.")]
        [Min(0.0f)]
        public float linearAirDragKgPerSecond = 0.0f;

        [Tooltip("Quadratic drag coefficient. Used with air density and rope radius.")]
        [Min(0.0f)]
        public float quadraticDragCoefficient = 1.1f;

        [Header("Environment")]
        [Range(0.0f, 2.0f)]
        public float gravityScale = 1.0f;

        [Header("Rod Torsion")]
        [Tooltip("Enables the inertial discrete-rod torsion solve and material frames.")]
        public bool enableTwistData = true;

        [Tooltip("Multiplier applied to the physical torsional rigidity.")]
        [Min(0.0f)]
        public float twistStiffnessScale = 1.0f;

        [Tooltip("Angular velocity damping per second for segment material frames.")]
        [Min(0.0f)]
        public float twistDamping = 1.1f;

        [Tooltip("Effective GJ torsional rigidity in N*m^2. Braided rope is much softer in torsion than a solid cylinder.")]
        [Min(0.0001f)]
        public float torsionalRigidityNewtonMeterSquared = 0.06f;

        [Tooltip("Scales polar segment inertia to account for rotating strands and unresolved fibers.")]
        [Min(0.01f)]
        public float twistInertiaScale = 6.0f;

        [Tooltip("Safety clamp for segment angular velocity around the rope axis.")]
        [Min(1.0f)]
        public float maxTwistAngularSpeedRadiansPerSecond = 18.0f;

        [Tooltip("Number of scalar torsion smoothing iterations used by the material-frame rope stage.")]
        [Range(0, 24)]
        public int torsionSolverIterations = 12;

        [Tooltip("How strongly neighboring material frames resist twist discontinuities.")]
        [Range(0.0f, 1.0f)]
        public float torsionPropagationStrength = 0.45f;

        [Tooltip("How strongly the ceiling end resists axial twist. 1 means the top material frame is fixed.")]
        [Range(0.0f, 1.0f)]
        public float topTwistAnchorStrength = 0.85f;

        [Tooltip("Maximum allowed twist change between neighboring segments after smoothing.")]
        [Min(0.001f)]
        public float maxTwistGradientRadians = 0.42f;

        [Tooltip("Endpoint-follow contribution retained for compatibility. Torque coupling is used by the current rod solver.")]
        [Range(0.0f, 1.0f)]
        public float endpointTwistFollow = 0.35f;

        [Tooltip("Maximum endpoint twist correction per substep in radians.")]
        [Min(0.001f)]
        public float maxEndpointTwistCorrectionRadians = 0.12f;

        [Header("Diagnostics")]
        public bool enableTensionDiagnostics = true;

        private void OnValidate()
        {
            if (segmentCount < 2)
                segmentCount = 2;

            if (lengthMeters <= 0.0f)
                lengthMeters = 0.05f;

            if (physicalRadiusMeters <= 0.0f)
                physicalRadiusMeters = 0.001f;

            if (visualRadiusMeters <= 0.0f)
                visualRadiusMeters = physicalRadiusMeters;

            if (solverIterations < 1)
                solverIterations = 1;

            if (initialDirection.sqrMagnitude < 1e-6f)
                initialDirection = Vector3.down;

            if (maxPivotSpeedMetersPerSecond < 0.1f)
                maxPivotSpeedMetersPerSecond = 0.1f;

            if (grabCompliance < 0.0f)
                grabCompliance = 0.0f;

            if (maxGrabCorrectionPerIteration < 0.001f)
                maxGrabCorrectionPerIteration = 0.001f;

            if (maxGrabSpeedMetersPerSecond < 0.1f)
                maxGrabSpeedMetersPerSecond = 0.1f;

            if (youngModulusPa < 1000.0f)
                youngModulusPa = 1000.0f;

            if (maxEndpointTwistCorrectionRadians < 0.001f)
                maxEndpointTwistCorrectionRadians = 0.001f;

            if (torsionalRigidityNewtonMeterSquared < 0.0001f)
                torsionalRigidityNewtonMeterSquared = 0.0001f;

            if (twistInertiaScale < 0.01f)
                twistInertiaScale = 0.01f;

            if (maxTwistAngularSpeedRadiansPerSecond < 1.0f)
                maxTwistAngularSpeedRadiansPerSecond = 1.0f;

            if (torsionSolverIterations < 0)
                torsionSolverIterations = 0;

            if (maxTwistGradientRadians < 0.001f)
                maxTwistGradientRadians = 0.001f;
        }
    }
}
