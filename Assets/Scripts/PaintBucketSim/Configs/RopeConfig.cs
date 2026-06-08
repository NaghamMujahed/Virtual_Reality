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

        public RopeBendingModel bendingModel = RopeBendingModel.AngleArccos;
        public bool enableBending = true;

        [Tooltip("Rest angle in degrees. 0 means initially straight between neighboring segments.")]
        [Range(0.0f, 120.0f)]
        public float restBendAngleDegrees = 0.0f;

        [Header("Damping")]
        public RopeDampingMode dampingMode = RopeDampingMode.Combined;

        [Tooltip("Numerical velocity damping. Useful for stability but not a physical material model.")]
        [Range(0.0f, 10.0f)]
        public float exponentialDampingPerSecond = 0.08f;

        [Tooltip("Physical-like damping ratio for relative motion between rope particles.")]
        [Range(0.0f, 2.0f)]
        public float segmentDampingRatio = 0.08f;

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

        [Header("Air Drag")]
        public RopeAirDragMode airDragMode = RopeAirDragMode.Linear;

        [Tooltip("Linear drag coefficient in kg/s per rope particle. Approximate, practical value.")]
        [Min(0.0f)]
        public float linearAirDragKgPerSecond = 0.015f;

        [Tooltip("Quadratic drag coefficient. Used with air density and rope radius.")]
        [Min(0.0f)]
        public float quadraticDragCoefficient = 1.1f;

        [Header("Environment")]
        [Range(0.0f, 2.0f)]
        public float gravityScale = 1.0f;

        [Header("Twist Scaffold")]
        [Tooltip("Creates twist-related data for later bucket coupling. True twist torque becomes meaningful when the bucket provides endpoint rotation.")]
        public bool enableTwistData = true;

        [Tooltip("Approximate twist stiffness scale for later bucket coupling.")]
        [Min(0.0f)]
        public float twistStiffnessScale = 1.0f;

        [Tooltip("Approximate twist damping for later bucket coupling.")]
        [Min(0.0f)]
        public float twistDamping = 0.1f;

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

            if (youngModulusPa < 1000.0f)
                youngModulusPa = 1000.0f;
        }
    }
}