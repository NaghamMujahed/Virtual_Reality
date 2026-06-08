using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Environment;
using PaintBucketSim.Systems.Rope;
using UnityEngine;
using UnityEngine.InputSystem;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Fluid;

namespace PaintBucketSim.Core
{
    public class SimulationManager : MonoBehaviour
    {
        [Header("Configs")]
        [SerializeField] private SimulationConfig simulationConfig;
        [SerializeField] private EnvironmentConfig environmentConfig;

        [Header("Systems")]
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private BucketSystem bucketSystem;
        [SerializeField] private RopeBucketCouplingSystem ropeBucketCouplingSystem;
        [SerializeField] private BoundarySystem boundarySystem;
        [SerializeField] private PaintFluidSystem paintFluidSystem;

        [Header("Runtime")]
        [SerializeField] private bool initializeOnStart = true;

        public SimulationContext Context { get; private set; }
        public TimeStepController TimeController { get; private set; }

        private EnvironmentSystem _environmentSystem;

        private float _fpsSmoothed;
        private const float FpsSmoothing = 0.08f;

        public bool IsInitialized => Context != null && Context.IsInitialized;

        private void Awake()
        {
            if (initializeOnStart)
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            if (simulationConfig == null)
            {
                Debug.LogError("SimulationManager: Missing SimulationConfig.");
                enabled = false;
                return;
            }

            if (environmentConfig == null)
            {
                Debug.LogError("SimulationManager: Missing EnvironmentConfig.");
                enabled = false;
                return;
            }

            if (simulationConfig.applyToUnityFixedDeltaTime)
            {
                Time.fixedDeltaTime = simulationConfig.fixedDeltaTime;
                Time.maximumDeltaTime = simulationConfig.maximumAllowedTimestep;
            }

            if (ropeSystem == null)
                ropeSystem = FindFirstObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            if (ropeBucketCouplingSystem == null)
                ropeBucketCouplingSystem = FindFirstObjectByType<RopeBucketCouplingSystem>();

            if (boundarySystem == null)
                boundarySystem = FindFirstObjectByType<BoundarySystem>();

            if (paintFluidSystem == null)
                paintFluidSystem = FindFirstObjectByType<PaintFluidSystem>();

            Context = new SimulationContext();
            Context.Initialize(simulationConfig, environmentConfig);

            TimeController = new TimeStepController();
            TimeController.Initialize(simulationConfig);

            _environmentSystem = new EnvironmentSystem();
            _environmentSystem.Initialize(Context);

            if (ropeSystem != null)
                ropeSystem.Initialize(Context);

            if (bucketSystem != null)
                bucketSystem.Initialize(Context);

            if (ropeBucketCouplingSystem != null)
                ropeBucketCouplingSystem.Initialize(Context);

            if(boundarySystem != null)
                boundarySystem.Initialize(Context);

            if (paintFluidSystem != null)
                paintFluidSystem.Initialize(Context);

            _fpsSmoothed = 0.0f;

            UpdateDiagnostics(0.0f);
        }

        private void Update()
        {
            HandleBasicInput();
            UpdateFps();
        }

        private void FixedUpdate()
        {
            if (!IsInitialized)
                return;

            if (!TimeController.ShouldRunFixedStep())
            {
                UpdateDiagnostics(0.0f);
                return;
            }

            TimeController.BeginFixedStep();

            int substeps = TimeController.GetSubstepCount();
            float subDt = TimeController.GetSubstepDeltaTime();

            for (int i = 0; i < substeps; i++)
            {
                StepSimulationSubstep(subDt);
                TimeController.AdvanceSubstep(subDt);
            }

            UpdateDiagnostics(subDt);
        }

        private void StepSimulationSubstep(float dt)
        {
            _environmentSystem.Step(Context, dt);

            if (ropeSystem != null)
                ropeSystem.Step(Context, dt);

            if (bucketSystem != null)
                bucketSystem.Step(Context, dt);

            if (ropeBucketCouplingSystem != null)
                ropeBucketCouplingSystem.Step(Context, dt);

            // Boundary must be updated AFTER bucket and coupling,
            // because coupling may change bucket position/rotation.
            if (boundarySystem != null)
                boundarySystem.Step(Context, dt);

            if (paintFluidSystem != null)
                paintFluidSystem.Step(Context, dt);
        }

        private void HandleBasicInput()
        {

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            if (keyboard.pKey.wasPressedThisFrame)
            {
                TimeController.TogglePause();
            }

            if (keyboard.oKey.wasPressedThisFrame)
            {
                TimeController.RequestSingleStep();
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                //TimeController.Reset();
                ResetSimulationState();
                //UpdateDiagnostics(0.0f);
            }
        }

        private void ResetSimulationState()
        {
            TimeController.Reset();

            _environmentSystem.Initialize(Context);

            if (ropeSystem != null)
                ropeSystem.ResetSystem(Context);

            if (bucketSystem != null)
                bucketSystem.ResetSystem(Context);

            if (ropeBucketCouplingSystem != null)
                ropeBucketCouplingSystem.ResetSystem(Context);

            if (boundarySystem != null)
                boundarySystem.ResetSystem(Context);

            if (paintFluidSystem != null)
                paintFluidSystem.ResetSystem(Context);

            UpdateDiagnostics(0.0f);
        }

        private void UpdateFps()
        {
            float currentFps = Time.unscaledDeltaTime > 0.0f
                ? 1.0f / Time.unscaledDeltaTime
                : 0.0f;

            if (_fpsSmoothed <= 0.01f)
                _fpsSmoothed = currentFps;
            else
                _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, currentFps, FpsSmoothing);
        }

        private void UpdateDiagnostics(float lastSubstepDt)
        {
            if (Context == null || TimeController == null)
                return;

            DiagnosticsFrame diagnostics = Context.Diagnostics;

            diagnostics.initialized = true;
            diagnostics.paused = TimeController.IsPaused;

            diagnostics.unityFrame = Time.frameCount;
            diagnostics.fixedStepIndex = TimeController.FixedStepIndex;
            diagnostics.substepIndex = TimeController.SubstepIndex;

            diagnostics.simulationTime = TimeController.SimulationTime;
            diagnostics.fixedDeltaTime = TimeController.GetFixedDeltaTime();
            diagnostics.substepDeltaTime = lastSubstepDt > 0.0f
                ? lastSubstepDt
                : TimeController.GetSubstepDeltaTime();

            diagnostics.substeps = TimeController.GetSubstepCount();
            diagnostics.fps = _fpsSmoothed;

            diagnostics.gravity = Context.EnvironmentState.gravity;
            diagnostics.windVelocity = Context.EnvironmentState.windVelocity;

            Context.Diagnostics = diagnostics;
        }
    }
}
