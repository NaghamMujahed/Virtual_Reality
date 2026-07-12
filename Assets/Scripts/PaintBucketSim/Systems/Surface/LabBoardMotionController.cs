using PaintBucketSim.Core;
using PaintSim.Scripts.Stages.Surface;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PaintBucketSim.Systems.Surface
{
    public enum LabBoardMotionMode
    {
        Static = 0,
        GentleTilt = 1,
        CrossAxisTilt = 2,
        TiltedOrbit = 3,
        FigureEight = 4,
        RotarySweep = 5
    }

    [DefaultExecutionOrder(-5)]
    [DisallowMultipleComponent]
    public sealed class LabBoardMotionController : MonoBehaviour
    {
        private const int MotionModeCount = 6;
        private const float TwoPi = Mathf.PI * 2.0f;

        [Header("References")]
        [SerializeField] private Transform controlledTransform;
        [SerializeField] private PaintSurface paintSurface;
        [SerializeField] private SimulationManager simulationManager;

        [Header("Motion")]
        [SerializeField] private bool enableMotion;
        [SerializeField] private LabBoardMotionMode motionMode = LabBoardMotionMode.Static;
        [SerializeField, Range(0.05f, 4.0f)] private float speedMultiplier = 1.0f;
        [SerializeField, Range(0.0f, 35.0f)] private float tiltAmplitudeDegrees = 14.0f;
        [SerializeField, Range(0.0f, 120.0f)] private float spinDegreesPerSecond = 18.0f;
        [SerializeField, Range(0.0f, 0.4f)] private float travelAmplitudeMeters = 0.08f;

        [Header("Keyboard Trim")]
        [SerializeField] private bool enableKeyboardControl = true;
        [SerializeField, Range(5.0f, 90.0f)] private float manualTiltSpeedDegreesPerSecond = 32.0f;
        [SerializeField, Range(0.0f, 60.0f)] private float maxManualTiltDegrees = 35.0f;
        [SerializeField, Range(0.0f, 180.0f)] private float maxManualYawDegrees = 90.0f;

        private Vector3 _initialLocalPosition;
        private Quaternion _initialLocalRotation;
        private Vector3 _initialLocalScale;
        private Vector3 _manualEulerOffset;
        private float _motionTime;
        private bool _initialPoseCaptured;
        private bool _poseDirty;

        public bool MotionEnabled
        {
            get => enableMotion;
            set => enableMotion = value;
        }

        public LabBoardMotionMode MotionMode
        {
            get => motionMode;
            set => motionMode = ClampMotionMode(value);
        }

        public int MotionModeIndex => (int)motionMode;
        public int ModeCount => MotionModeCount;
        public string MotionModeLabel => GetMotionModeLabel(motionMode);
        public float SpeedMultiplier => speedMultiplier;
        public float TiltAmplitudeDegrees => tiltAmplitudeDegrees;
        public float SpinDegreesPerSecond => spinDegreesPerSecond;
        public float TravelAmplitudeMeters => travelAmplitudeMeters;
        public Vector3 ManualEulerOffset => _manualEulerOffset;
        public bool IsMotionActive => enableMotion && motionMode != LabBoardMotionMode.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOnPaintSurfaces()
        {
            PaintSurface[] surfaces = Object.FindObjectsByType<PaintSurface>(FindObjectsSortMode.None);

            for (int i = 0; i < surfaces.Length; i++)
            {
                PaintSurface surface = surfaces[i];
                if (surface == null || surface.GetComponent<LabBoardMotionController>() != null)
                    continue;

                LabBoardMotionController controller =
                    surface.gameObject.AddComponent<LabBoardMotionController>();
                controller.paintSurface = surface;
                controller.controlledTransform = surface.transform;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureInitialPose();
        }

        private void Update()
        {
            ResolveReferences();
            HandleKeyboardInput();

            if (!Application.isPlaying || controlledTransform == null)
                return;

            if (IsMotionActive && !IsSimulationPaused())
                _motionTime += Time.deltaTime * speedMultiplier;

            ApplyPose();
        }

        private void LateUpdate()
        {
            if (!_poseDirty || paintSurface == null)
                return;

            paintSurface.RefreshSurfaceFrameFromTransform();
            _poseDirty = false;
        }

        public void SetMotionEnabled(bool enabled)
        {
            enableMotion = enabled;
        }

        public void ToggleMotion()
        {
            enableMotion = !enableMotion;
        }

        public void SetMotionMode(int modeIndex)
        {
            motionMode = ClampMotionMode((LabBoardMotionMode)modeIndex);
        }

        public void CycleMotionMode(int direction)
        {
            int current = (int)motionMode;
            int next = (current + direction) % MotionModeCount;
            if (next < 0)
                next += MotionModeCount;

            motionMode = (LabBoardMotionMode)next;
        }

        public void SetSpeedMultiplier(float value)
        {
            speedMultiplier = Mathf.Clamp(value, 0.05f, 4.0f);
        }

        public void SetTiltAmplitude(float value)
        {
            tiltAmplitudeDegrees = Mathf.Clamp(value, 0.0f, 35.0f);
        }

        public void SetSpinDegreesPerSecond(float value)
        {
            spinDegreesPerSecond = Mathf.Clamp(value, 0.0f, 120.0f);
        }

        public void SetTravelAmplitude(float value)
        {
            travelAmplitudeMeters = Mathf.Clamp(value, 0.0f, 0.4f);
        }

        public void ClearPaintFilm()
        {
            paintSurface?.ClearPaintFilm();
        }

        public void ResetBoardPose(bool clearPaintFilm)
        {
            ResolveReferences();
            CaptureInitialPoseIfNeeded();

            _motionTime = 0.0f;
            _manualEulerOffset = Vector3.zero;

            if (controlledTransform != null)
            {
                controlledTransform.localPosition = _initialLocalPosition;
                controlledTransform.localRotation = _initialLocalRotation;
                controlledTransform.localScale = _initialLocalScale;
                _poseDirty = true;
            }

            if (paintSurface != null)
                paintSurface.ResetRuntimeSurface(clearPaintFilm);
        }

        public static string GetMotionModeLabel(LabBoardMotionMode mode)
        {
            switch (mode)
            {
                case LabBoardMotionMode.GentleTilt:
                    return "Gentle Tilt";
                case LabBoardMotionMode.CrossAxisTilt:
                    return "Cross-Axis Tilt";
                case LabBoardMotionMode.TiltedOrbit:
                    return "Tilted Orbit";
                case LabBoardMotionMode.FigureEight:
                    return "Figure Eight";
                case LabBoardMotionMode.RotarySweep:
                    return "Rotary Sweep";
                case LabBoardMotionMode.Static:
                default:
                    return "Static";
            }
        }

        private void ResolveReferences()
        {
            if (paintSurface == null)
                paintSurface = GetComponent<PaintSurface>();

            if (controlledTransform == null)
                controlledTransform = paintSurface != null ? paintSurface.transform : transform;

            if (simulationManager == null)
                simulationManager = Object.FindFirstObjectByType<SimulationManager>();
        }

        private void CaptureInitialPose()
        {
            if (controlledTransform == null)
                return;

            _initialLocalPosition = controlledTransform.localPosition;
            _initialLocalRotation = controlledTransform.localRotation;
            _initialLocalScale = controlledTransform.localScale;
            _initialPoseCaptured = true;
        }

        private void CaptureInitialPoseIfNeeded()
        {
            if (!_initialPoseCaptured)
                CaptureInitialPose();
        }

        private void HandleKeyboardInput()
        {
            if (!enableKeyboardControl || Keyboard.current == null)
                return;

            Keyboard keyboard = Keyboard.current;

            if (keyboard.mKey.wasPressedThisFrame)
                ToggleMotion();

            if (keyboard.bKey.wasPressedThisFrame)
                CycleMotionMode(1);

            if (keyboard.nKey.wasPressedThisFrame)
                CycleMotionMode(-1);

            if (keyboard.cKey.wasPressedThisFrame)
                ClearPaintFilm();

            if (keyboard.homeKey.wasPressedThisFrame)
                ResetBoardPose(false);

            float pitchInput = ReadAxis(
                keyboard.pageUpKey.isPressed,
                keyboard.pageDownKey.isPressed);
            float rollInput = ReadAxis(keyboard.jKey.isPressed, keyboard.lKey.isPressed);
            float yawInput = ReadAxis(keyboard.eKey.isPressed, keyboard.qKey.isPressed);

            if (Mathf.Abs(pitchInput) > 0.0f ||
                Mathf.Abs(rollInput) > 0.0f ||
                Mathf.Abs(yawInput) > 0.0f)
            {
                float delta = manualTiltSpeedDegreesPerSecond * Time.unscaledDeltaTime;
                _manualEulerOffset.x += pitchInput * delta;
                _manualEulerOffset.z += rollInput * delta;
                _manualEulerOffset.y += yawInput * delta;

                _manualEulerOffset.x = Mathf.Clamp(
                    _manualEulerOffset.x,
                    -maxManualTiltDegrees,
                    maxManualTiltDegrees);
                _manualEulerOffset.z = Mathf.Clamp(
                    _manualEulerOffset.z,
                    -maxManualTiltDegrees,
                    maxManualTiltDegrees);
                _manualEulerOffset.y = Mathf.Clamp(
                    _manualEulerOffset.y,
                    -maxManualYawDegrees,
                    maxManualYawDegrees);
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
                _manualEulerOffset = Vector3.zero;
        }

        private void ApplyPose()
        {
            CaptureInitialPoseIfNeeded();

            if (controlledTransform == null)
                return;

            Vector3 localPosition = _initialLocalPosition;
            Vector3 proceduralEuler = Vector3.zero;

            if (IsMotionActive)
                EvaluateMotionPose(ref localPosition, ref proceduralEuler);

            Quaternion targetRotation =
                _initialLocalRotation *
                Quaternion.Euler(proceduralEuler + _manualEulerOffset);

            controlledTransform.localPosition = localPosition;
            controlledTransform.localRotation = targetRotation;
            controlledTransform.localScale = _initialLocalScale;
            _poseDirty = true;
        }

        private void EvaluateMotionPose(
            ref Vector3 localPosition,
            ref Vector3 proceduralEuler)
        {
            float phase = _motionTime * TwoPi;

            switch (motionMode)
            {
                case LabBoardMotionMode.GentleTilt:
                    proceduralEuler.x = Mathf.Sin(phase) * tiltAmplitudeDegrees;
                    proceduralEuler.z = Mathf.Sin(phase * 0.73f + 1.1f) * tiltAmplitudeDegrees * 0.45f;
                    break;

                case LabBoardMotionMode.CrossAxisTilt:
                    proceduralEuler.x = Mathf.Sin(phase) * tiltAmplitudeDegrees;
                    proceduralEuler.z = Mathf.Cos(phase * 0.86f) * tiltAmplitudeDegrees;
                    proceduralEuler.y = Mathf.Sin(phase * 0.31f) * tiltAmplitudeDegrees * 0.25f;
                    break;

                case LabBoardMotionMode.TiltedOrbit:
                    proceduralEuler.x = Mathf.Sin(phase) * tiltAmplitudeDegrees * 0.55f;
                    proceduralEuler.z = Mathf.Cos(phase) * tiltAmplitudeDegrees;
                    proceduralEuler.y = _motionTime * spinDegreesPerSecond;
                    break;

                case LabBoardMotionMode.FigureEight:
                    localPosition += new Vector3(
                        Mathf.Sin(phase),
                        0.0f,
                        Mathf.Sin(phase * 2.0f) * 0.5f
                    ) * travelAmplitudeMeters;
                    proceduralEuler.x = Mathf.Sin(phase * 2.0f) * tiltAmplitudeDegrees * 0.65f;
                    proceduralEuler.z = Mathf.Sin(phase) * tiltAmplitudeDegrees;
                    break;

                case LabBoardMotionMode.RotarySweep:
                    proceduralEuler.x =
                        tiltAmplitudeDegrees * 0.55f +
                        Mathf.Sin(phase) * tiltAmplitudeDegrees * 0.2f;
                    proceduralEuler.y = _motionTime * spinDegreesPerSecond;
                    proceduralEuler.z = Mathf.Cos(phase * 0.5f) * tiltAmplitudeDegrees * 0.35f;
                    break;
            }
        }

        private bool IsSimulationPaused()
        {
            return simulationManager != null &&
                   simulationManager.TimeController != null &&
                   simulationManager.TimeController.IsPaused;
        }

        private static float ReadAxis(bool positive, bool negative)
        {
            return (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
        }

        private static LabBoardMotionMode ClampMotionMode(LabBoardMotionMode mode)
        {
            int index = Mathf.Clamp((int)mode, 0, MotionModeCount - 1);
            return (LabBoardMotionMode)index;
        }
    }
}
