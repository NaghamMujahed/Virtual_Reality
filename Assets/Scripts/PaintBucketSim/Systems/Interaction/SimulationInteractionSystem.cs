using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Rope;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PaintBucketSim.Systems.Interaction
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Paint Bucket Sim/Simulation Interaction System")]
    public sealed class SimulationInteractionSystem : MonoBehaviour
    {
        private enum GrabKind
        {
            None = 0,
            RopeParticle = 1,
            BucketPoint = 2
        }

        private enum OverlayAnchor
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Header("References")]
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private BucketSystem bucketSystem;

        [Header("Camera Navigation")]
        [SerializeField] private bool enableCameraNavigation = true;
        [SerializeField] private bool lockCursorWhileLooking = true;
        [Min(0.01f)] [SerializeField] private float cameraMoveSpeed = 3.5f;
        [Range(1.0f, 10.0f)] [SerializeField] private float cameraSprintMultiplier = 3.0f;
        [Range(0.05f, 1.0f)] [SerializeField] private float cameraSlowMultiplier = 0.35f;
        [Range(0.01f, 1.0f)] [SerializeField] private float mouseLookSensitivity = 0.12f;
        [Min(0.1f)] [SerializeField] private float minCameraMoveSpeed = 0.5f;
        [Min(0.1f)] [SerializeField] private float maxCameraMoveSpeed = 20.0f;

        [Header("Mouse Grab")]
        [SerializeField] private bool enableMouseGrab = true;
        [Min(0.005f)] [SerializeField] private float ropePickRadiusMeters = 0.08f;
        [Min(0.0f)] [SerializeField] private float bucketPickPaddingMeters = 0.08f;
        [Range(1, 128)] [SerializeField] private int firstInteractiveRopeParticle = 1;
        [Min(0.1f)] [SerializeField] private float minGrabDepthMeters = 0.2f;
        [Min(0.1f)] [SerializeField] private float maxGrabDepthMeters = 25.0f;
        [Range(0.0001f, 0.02f)] [SerializeField] private float grabDepthScrollScale = 0.003f;
        [SerializeField] private bool showMouseHoverFeedback = true;

        [Header("Rope Grab")]
        [Range(0.0f, 1.0f)] [SerializeField] private float ropeGrabCorrectionGain = 0.85f;
        [Min(0.1f)] [SerializeField] private float ropeGrabMaxSpeedMetersPerSecond = 7.5f;

        [Header("Bucket Grab")]
        [Range(0.0f, 1.0f)] [SerializeField] private float bucketGrabLinearCorrectionGain = 0.12f;
        [Range(0.0f, 1.0f)] [SerializeField] private float bucketGrabAngularCorrectionGain = 0.18f;
        [Min(0.0f)] [SerializeField] private float bucketGrabMaxCorrectionSpeed = 0.75f;
        [Min(0.0f)] [SerializeField] private float bucketGrabMaxAngularCorrectionSpeed = 1.6f;
        [SerializeField] private bool bucketGrabAddsReleaseVelocity = false;
        [SerializeField] private bool allowBucketGrabDepthScroll = false;

        [Header("Impulse Controls")]
        [SerializeField] private bool enableKeyboardImpulses = true;
        [Min(0.0f)] [SerializeField] private float pushImpulse = 1.2f;
        [Min(0.0f)] [SerializeField] private float liftImpulse = 0.85f;
        [Min(0.0f)] [SerializeField] private float swingImpulse = 1.1f;
        [Min(0.0f)] [SerializeField] private float spinAngularImpulse = 0.08f;
        [Min(0.0f)] [SerializeField] private float heldSpinTorque = 0.35f;

        [Header("Mouse Impulses")]
        [SerializeField] private bool enableMouseImpulses = true;
        [SerializeField] private bool allowMouseImpulsesOnBucket = false;
        [Min(0.0f)] [SerializeField] private float middleMousePushImpulse = 0.9f;
        [Min(0.0f)] [SerializeField] private float mouseWheelImpulsePerNotch = 0.18f;

        [Header("Debug")]
        [SerializeField] private bool showInteractionOverlay = true;
        [SerializeField] private bool showInteractionGizmos = true;
        [SerializeField] private bool useResponsiveOverlayLayout = true;
        [SerializeField] private OverlayAnchor responsiveOverlayAnchor = OverlayAnchor.BottomRight;
        [Min(0.0f)] [SerializeField] private float responsiveOverlayMargin = 15.0f;
        [SerializeField] private Vector2 overlayPosition = new Vector2(15.0f, 250.0f);
        [SerializeField] private Vector2 overlaySize = new Vector2(440.0f, 128.0f);

        private GrabKind _grabKind = GrabKind.None;
        private int _grabRopeParticle = -1;
        private float _grabDepth;
        private Vector3 _grabTargetWorld;
        private Vector3 _grabBucketLocalPoint;
        private Vector3 _lastGrabCurrentWorld;
        private bool _cameraAnglesInitialized;
        private bool _cursorLockedByThisSystem;
        private float _cameraYaw;
        private float _cameraPitch;
        private GUIStyle _overlayStyle;
        private GUIStyle _overlayBoxStyle;
        private GUIStyle _hoverStyle;
        private int _lastOverlayFontSize;
        private GrabKind _hoverKind = GrabKind.None;
        private int _hoverRopeParticle = -1;
        private Vector3 _hoverWorldPoint;
        private bool _hasHover;

        public bool HasActiveGrab => _grabKind != GrabKind.None;
        public Vector3 GrabTargetWorld => _grabTargetWorld;
        public Vector3 GrabCurrentWorld => _lastGrabCurrentWorld;
        public string ActiveGrabLabel =>
            _grabKind == GrabKind.RopeParticle
                ? $"Rope #{_grabRopeParticle}"
                : _grabKind.ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (!Application.isPlaying)
                return;

            if (FindAnyObjectByType<SimulationInteractionSystem>() != null)
                return;

            SimulationManager manager = FindAnyObjectByType<SimulationManager>();
            GameObject host = manager != null
                ? manager.gameObject
                : new GameObject("Simulation Interaction System");

            host.AddComponent<SimulationInteractionSystem>();
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureCameraAngles();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CaptureCameraAngles();
        }

        private void OnDisable()
        {
            ReleaseCursorLock();
            ClearGrab();
        }

        private void Update()
        {
            ResolveReferences();

            UpdateMouseHover();
            UpdateCameraNavigation();
            UpdateMouseGrab();
            HandleMouseImpulses();
            HandleImpulseKeys();
        }

        private void FixedUpdate()
        {
            if (_grabKind != GrabKind.None)
                ApplyActiveGrab(Mathf.Max(Time.fixedDeltaTime, 1e-5f));

            ApplyHeldSpinTorque();
        }

        private void ResolveReferences()
        {
            if (interactionCamera == null)
                interactionCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

            if (ropeSystem == null)
                ropeSystem = FindAnyObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();
        }

        private void CaptureCameraAngles()
        {
            if (_cameraAnglesInitialized || interactionCamera == null)
                return;

            Vector3 euler = interactionCamera.transform.rotation.eulerAngles;
            _cameraYaw = euler.y;
            _cameraPitch = NormalizeAngle(euler.x);
            _cameraAnglesInitialized = true;
        }

        private void UpdateCameraNavigation()
        {
            if (!enableCameraNavigation || interactionCamera == null)
                return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null)
                return;

            Transform cameraTransform = interactionCamera.transform;

            if (mouse != null && mouse.rightButton.isPressed)
            {
                if (lockCursorWhileLooking)
                    LockCursor();

                Vector2 delta = mouse.delta.ReadValue();
                _cameraYaw += delta.x * mouseLookSensitivity;
                _cameraPitch -= delta.y * mouseLookSensitivity;
                _cameraPitch = Mathf.Clamp(_cameraPitch, -89.0f, 89.0f);
                cameraTransform.rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0.0f);
            }
            else
            {
                ReleaseCursorLock();
            }

            Vector3 localMove = Vector3.zero;
            if (keyboard.wKey.isPressed)
                localMove.z += 1.0f;
            if (keyboard.sKey.isPressed)
                localMove.z -= 1.0f;
            if (keyboard.dKey.isPressed)
                localMove.x += 1.0f;
            if (keyboard.aKey.isPressed)
                localMove.x -= 1.0f;
            if (keyboard.eKey.isPressed)
                localMove.y += 1.0f;
            if (keyboard.qKey.isPressed)
                localMove.y -= 1.0f;

            if (localMove.sqrMagnitude > 1e-6f)
            {
                float speed = cameraMoveSpeed;
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    speed *= cameraSprintMultiplier;
                if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                    speed *= cameraSlowMultiplier;

                cameraTransform.position +=
                    cameraTransform.rotation *
                    localMove.normalized *
                    speed *
                    Time.unscaledDeltaTime;
            }

            if (mouse != null &&
                _grabKind == GrabKind.None &&
                (!_hasHover || !enableMouseImpulses))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float scale = 1.0f + scroll * 0.001f;
                    cameraMoveSpeed = Mathf.Clamp(
                        cameraMoveSpeed * Mathf.Max(scale, 0.1f),
                        minCameraMoveSpeed,
                        maxCameraMoveSpeed
                    );
                }
            }
        }

        private void UpdateMouseGrab()
        {
            if (!enableMouseGrab || interactionCamera == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.leftButton.wasPressedThisFrame)
                BeginGrab(mouse.position.ReadValue());

            if (_grabKind != GrabKind.None && mouse.leftButton.isPressed)
            {
                Ray ray = interactionCamera.ScreenPointToRay(mouse.position.ReadValue());
                UpdateGrabFromRay(ray, mouse.scroll.ReadValue().y);
            }

            if (_grabKind != GrabKind.None && mouse.leftButton.wasReleasedThisFrame)
                EndGrab();
        }

        private void UpdateMouseHover()
        {
            _hasHover = false;
            _hoverKind = GrabKind.None;
            _hoverRopeParticle = -1;

            if (interactionCamera == null || Mouse.current == null || _grabKind != GrabKind.None)
                return;

            Ray ray = interactionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!TryPickTarget(
                    ray,
                    out GrabKind kind,
                    out int ropeIndex,
                    out float depth,
                    out Vector3 hitPoint))
            {
                return;
            }

            _hasHover = true;
            _hoverKind = kind;
            _hoverRopeParticle = ropeIndex;
            _hoverWorldPoint = hitPoint;
        }

        private void BeginGrab(Vector2 screenPosition)
        {
            Ray ray = interactionCamera.ScreenPointToRay(screenPosition);
            BeginGrabFromRay(ray);
        }

        public bool BeginGrabFromRay(Ray ray)
        {
            if (!TryPickTarget(
                    ray,
                    out GrabKind kind,
                    out int ropeIndex,
                    out float depth,
                    out Vector3 hitPoint))
            {
                return false;
            }

            if (kind == GrabKind.RopeParticle)
            {
                _grabKind = GrabKind.RopeParticle;
                _grabRopeParticle = ropeIndex;
                _grabDepth = Mathf.Clamp(depth, minGrabDepthMeters, maxGrabDepthMeters);
                _grabTargetWorld = ray.GetPoint(_grabDepth);
                _lastGrabCurrentWorld = ropeSystem.GetParticlePosition(ropeIndex);
                return true;
            }

            _grabKind = GrabKind.BucketPoint;
            _grabRopeParticle = -1;
            _grabDepth = Mathf.Clamp(depth, minGrabDepthMeters, maxGrabDepthMeters);
            _grabTargetWorld = ray.GetPoint(_grabDepth);
            _grabBucketLocalPoint = bucketSystem.WorldToLocalPoint(hitPoint);
            _lastGrabCurrentWorld = hitPoint;
            return true;
        }

        public void UpdateGrabFromRay(Ray ray, float scrollDelta)
        {
            if (_grabKind == GrabKind.None)
                return;

            bool allowDepthScroll =
                _grabKind != GrabKind.BucketPoint ||
                allowBucketGrabDepthScroll;

            if (allowDepthScroll && Mathf.Abs(scrollDelta) > 0.01f)
            {
                float depthScale = Mathf.Max(_grabDepth, 1.0f);
                _grabDepth += scrollDelta * grabDepthScrollScale * depthScale;
                _grabDepth = Mathf.Clamp(_grabDepth, minGrabDepthMeters, maxGrabDepthMeters);
            }

            _grabTargetWorld = ray.GetPoint(_grabDepth);
        }

        public void EndGrab()
        {
            ClearGrab();
        }

        private bool TryPickTarget(
            Ray ray,
            out GrabKind kind,
            out int ropeIndex,
            out float depth,
            out Vector3 hitPoint)
        {
            kind = GrabKind.None;
            ropeIndex = -1;
            depth = 0.0f;
            hitPoint = Vector3.zero;

            bool ropeHit = TryPickRope(ray, out int pickedRopeIndex, out float ropeDepth);
            bool bucketHit = TryPickBucket(ray, out float bucketDepth, out Vector3 bucketHitPoint);

            if (!ropeHit && !bucketHit)
                return false;

            if (ropeHit && (!bucketHit || ropeDepth <= bucketDepth))
            {
                kind = GrabKind.RopeParticle;
                ropeIndex = pickedRopeIndex;
                depth = ropeDepth;
                hitPoint = ropeSystem.GetParticlePosition(pickedRopeIndex);
                return true;
            }

            kind = GrabKind.BucketPoint;
            depth = bucketDepth;
            hitPoint = bucketHitPoint;
            return true;
        }

        private bool TryPickRope(Ray ray, out int particleIndex, out float depth)
        {
            particleIndex = -1;
            depth = 0.0f;

            if (ropeSystem == null || !ropeSystem.IsInitialized)
                return false;

            float bestDepth = float.PositiveInfinity;
            int bestIndex = -1;
            int first = Mathf.Clamp(firstInteractiveRopeParticle, 0, ropeSystem.ParticleCount - 1);

            for (int i = first; i < ropeSystem.ParticleCount; i++)
            {
                float inverseMass = ropeSystem.GetParticleInverseMass(i);
                if (inverseMass <= 0.0f)
                    continue;

                Vector3 p = ropeSystem.GetParticlePosition(i);
                float distance = DistancePointToRay(p, ray, out float candidateDepth);
                if (candidateDepth < minGrabDepthMeters || candidateDepth > maxGrabDepthMeters)
                    continue;

                if (distance <= ropePickRadiusMeters && candidateDepth < bestDepth)
                {
                    bestDepth = candidateDepth;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return false;

            particleIndex = bestIndex;
            depth = bestDepth;
            return true;
        }

        private bool TryPickBucket(Ray ray, out float depth, out Vector3 hitPoint)
        {
            depth = 0.0f;
            hitPoint = Vector3.zero;

            if (bucketSystem == null || !bucketSystem.IsInitialized)
                return false;

            Vector3 center = bucketSystem.GetCenterOfMassWorld();
            float radius = EstimateBucketPickRadius();
            if (!RaySphere(ray, center, radius, out depth))
                return false;

            if (depth < minGrabDepthMeters || depth > maxGrabDepthMeters)
                return false;

            hitPoint = ray.GetPoint(depth);
            return true;
        }

        private void ApplyActiveGrab(float dt)
        {
            if (_grabKind == GrabKind.RopeParticle)
            {
                ApplyRopeGrab(dt);
                return;
            }

            if (_grabKind == GrabKind.BucketPoint)
                ApplyBucketGrab(dt);
        }

        private void ApplyRopeGrab(float dt)
        {
            if (ropeSystem == null || !ropeSystem.IsInitialized || _grabRopeParticle < 0)
            {
                ClearGrab();
                return;
            }

            Vector3 current = ropeSystem.GetParticlePosition(_grabRopeParticle);
            Vector3 error = _grabTargetWorld - current;
            Vector3 correction = Vector3.ClampMagnitude(
                error * ropeGrabCorrectionGain,
                ropeGrabMaxSpeedMetersPerSecond * dt
            );

            ropeSystem.ApplyParticlePositionCorrection(_grabRopeParticle, correction, dt, true);
            _lastGrabCurrentWorld = current + correction;
        }

        private void ApplyBucketGrab(float dt)
        {
            if (bucketSystem == null || !bucketSystem.IsInitialized)
            {
                ClearGrab();
                return;
            }

            Vector3 current = bucketSystem.LocalToWorldPoint(_grabBucketLocalPoint);
            Vector3 error = _grabTargetWorld - current;

            Vector3 linearCorrection = Vector3.ClampMagnitude(
                error * bucketGrabLinearCorrectionGain,
                bucketGrabMaxCorrectionSpeed * dt
            );

            Vector3 center = bucketSystem.GetCenterOfMassWorld();
            Vector3 r = current - center;
            Vector3 angularCorrection = Vector3.zero;
            if (r.sqrMagnitude > 1e-6f)
            {
                angularCorrection =
                    Vector3.Cross(r, error) /
                    Mathf.Max(r.sqrMagnitude, 0.01f) *
                    bucketGrabAngularCorrectionGain;

                angularCorrection = Vector3.ClampMagnitude(
                    angularCorrection,
                    bucketGrabMaxAngularCorrectionSpeed * dt
                );
            }

            bucketSystem.ApplyCouplingCorrection(
                linearCorrection,
                angularCorrection,
                dt,
                bucketGrabAddsReleaseVelocity
            );
            _lastGrabCurrentWorld = current + linearCorrection;
        }

        private void HandleImpulseKeys()
        {
            if (!enableKeyboardImpulses ||
                bucketSystem == null ||
                !bucketSystem.IsInitialized ||
                interactionCamera == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Transform cameraTransform = interactionCamera.transform;
            Vector3 center = bucketSystem.GetCenterOfMassWorld();

            if (keyboard.fKey.wasPressedThisFrame)
                bucketSystem.ApplyImpulseAtWorldPoint(cameraTransform.forward * pushImpulse, center);

            if (keyboard.vKey.wasPressedThisFrame)
                bucketSystem.ApplyImpulseAtWorldPoint(Vector3.up * liftImpulse, center);

            if (keyboard.gKey.wasPressedThisFrame)
            {
                Vector3 point = bucketSystem.GetAttachmentWorldPosition();
                bucketSystem.ApplyImpulseAtWorldPoint(cameraTransform.right * swingImpulse, point);
            }

            if (keyboard.tKey.wasPressedThisFrame)
                bucketSystem.ApplyAngularImpulse(cameraTransform.up * spinAngularImpulse);

            if (keyboard.yKey.wasPressedThisFrame)
                bucketSystem.ApplyAngularImpulse(-cameraTransform.up * spinAngularImpulse);
        }

        private void HandleMouseImpulses()
        {
            if (!enableMouseImpulses ||
                interactionCamera == null ||
                Mouse.current == null ||
                _grabKind != GrabKind.None ||
                !_hasHover)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            Ray ray = interactionCamera.ScreenPointToRay(mouse.position.ReadValue());

            if (mouse.middleButton.wasPressedThisFrame)
                ApplyMouseImpulse(ray.direction * middleMousePushImpulse);

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Wheel up pulls toward the camera, wheel down pushes away.
                Vector3 direction = scroll > 0.0f ? -ray.direction : ray.direction;
                float notches = Mathf.Abs(scroll) / 120.0f;
                ApplyMouseImpulse(direction * (mouseWheelImpulsePerNotch * Mathf.Max(notches, 0.1f)));
            }
        }

        private void ApplyMouseImpulse(Vector3 impulse)
        {
            if (!_hasHover)
                return;

            if (_hoverKind == GrabKind.RopeParticle && ropeSystem != null)
            {
                ropeSystem.ApplyParticleImpulse(_hoverRopeParticle, impulse);
                return;
            }

            if (_hoverKind == GrabKind.BucketPoint &&
                bucketSystem != null &&
                allowMouseImpulsesOnBucket)
            {
                bucketSystem.ApplyImpulseAtWorldPoint(impulse, _hoverWorldPoint);
            }
        }

        private void ApplyHeldSpinTorque()
        {
            if (!enableKeyboardImpulses ||
                heldSpinTorque <= 0.0f ||
                bucketSystem == null ||
                !bucketSystem.IsInitialized ||
                interactionCamera == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float sign = 0.0f;
            if (keyboard.zKey.isPressed)
                sign += 1.0f;
            if (keyboard.xKey.isPressed)
                sign -= 1.0f;

            if (Mathf.Abs(sign) <= 0.0f)
                return;

            Vector3 torque = interactionCamera.transform.up * (heldSpinTorque * sign);
            bucketSystem.AddTorque(ToFloat3(torque));
        }

        private float EstimateBucketPickRadius()
        {
            BucketConfig config = bucketSystem != null ? bucketSystem.Config : null;
            if (config == null)
                return 0.5f + bucketPickPaddingMeters;

            float radius = config.GetRepresentativeRadius();
            float halfHeight = config.heightMeters * 0.5f + config.handleHeightMeters;
            return Mathf.Sqrt(radius * radius + halfHeight * halfHeight) + bucketPickPaddingMeters;
        }

        private void ClearGrab()
        {
            _grabKind = GrabKind.None;
            _grabRopeParticle = -1;
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _cursorLockedByThisSystem = true;
        }

        private void ReleaseCursorLock()
        {
            if (!_cursorLockedByThisSystem)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorLockedByThisSystem = false;
        }

        private void OnGUI()
        {
            if (!showInteractionOverlay || !Application.isPlaying)
                return;

            EnsureOverlayStyles();
            GUILayout.BeginArea(GetOverlayRect(), _overlayBoxStyle);
            GUILayout.Label("Interaction", _overlayStyle);
            GUILayout.Label("Right mouse: look | WASD move | Q/E vertical", _overlayStyle);
            GUILayout.Label("Left mouse: grab/drag | bucket drag is soft and non-impulsive", _overlayStyle);
            GUILayout.Label("Wheel/middle mouse: rope nudge only | F/V/G/T/Y/Z/X: bucket controls", _overlayStyle);
            GUILayout.Label($"Hover: {HoverLabel()} | Grab: {ActiveGrabLabel}", _overlayStyle);
            GUILayout.EndArea();

            DrawHoverFeedback();
        }

        private void EnsureOverlayStyles()
        {
            int effectiveFontSize = GetOverlayFontSize();
            if (_overlayStyle != null && _lastOverlayFontSize == effectiveFontSize)
                return;

            _lastOverlayFontSize = effectiveFontSize;
            _overlayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = effectiveFontSize,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            _overlayBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 8, 8)
            };

            _hoverStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(10, effectiveFontSize - 1),
                normal = { textColor = Color.cyan }
            };
        }

        private Rect GetOverlayRect()
        {
            if (!useResponsiveOverlayLayout)
                return new Rect(overlayPosition, overlaySize);

            float margin = Mathf.Clamp(responsiveOverlayMargin, 0.0f, 64.0f);
            float availableWidth = Mathf.Max(1.0f, Screen.width - margin * 2.0f);
            float availableHeight = Mathf.Max(1.0f, Screen.height - margin * 2.0f);

            float width = Mathf.Clamp(
                Mathf.Min(overlaySize.x, Screen.width * 0.44f),
                Mathf.Min(280.0f, availableWidth),
                availableWidth
            );

            float height = Mathf.Clamp(
                Mathf.Min(overlaySize.y, Screen.height * 0.24f),
                Mathf.Min(104.0f, availableHeight),
                availableHeight
            );

            return BuildAnchoredRect(responsiveOverlayAnchor, width, height, margin);
        }

        private Rect BuildAnchoredRect(OverlayAnchor anchor, float width, float height, float margin)
        {
            float x = anchor == OverlayAnchor.TopRight || anchor == OverlayAnchor.BottomRight
                ? Screen.width - margin - width
                : margin;

            float y = anchor == OverlayAnchor.BottomLeft || anchor == OverlayAnchor.BottomRight
                ? Screen.height - margin - height
                : margin;

            x = Mathf.Clamp(x, margin, Mathf.Max(margin, Screen.width - margin - width));
            y = Mathf.Clamp(y, margin, Mathf.Max(margin, Screen.height - margin - height));

            return new Rect(x, y, width, height);
        }

        private int GetOverlayFontSize()
        {
            if (!useResponsiveOverlayLayout)
                return 13;

            return Mathf.Clamp(Mathf.RoundToInt(Screen.height / 62.0f), 10, 13);
        }

        private void DrawHoverFeedback()
        {
            if (!showMouseHoverFeedback ||
                interactionCamera == null ||
                (!_hasHover && _grabKind == GrabKind.None))
            {
                return;
            }

            Vector3 world = _grabKind != GrabKind.None ? _lastGrabCurrentWorld : _hoverWorldPoint;
            Vector3 screen = interactionCamera.WorldToScreenPoint(world);
            if (screen.z <= 0.0f)
                return;

            float x = screen.x;
            float y = Screen.height - screen.y;
            Rect labelRect = new Rect(x - 80.0f, y - 34.0f, 160.0f, 24.0f);
            GUI.Label(labelRect, _grabKind != GrabKind.None ? ActiveGrabLabel : HoverLabel(), _hoverStyle);
            GUI.Box(new Rect(x - 5.0f, y - 5.0f, 10.0f, 10.0f), GUIContent.none);
        }

        private string HoverLabel()
        {
            if (!_hasHover)
                return "None";

            return _hoverKind == GrabKind.RopeParticle
                ? $"Rope #{_hoverRopeParticle}"
                : "Bucket";
        }

        private void OnDrawGizmos()
        {
            if (!showInteractionGizmos || _grabKind == GrabKind.None)
                return;

            Gizmos.color = _grabKind == GrabKind.RopeParticle
                ? new Color(0.1f, 0.65f, 1.0f, 0.95f)
                : new Color(1.0f, 0.75f, 0.1f, 0.95f);

            Gizmos.DrawLine(_lastGrabCurrentWorld, _grabTargetWorld);
            Gizmos.DrawSphere(_grabTargetWorld, 0.035f);
        }

        private static float DistancePointToRay(Vector3 point, Ray ray, out float depth)
        {
            Vector3 delta = point - ray.origin;
            depth = Vector3.Dot(delta, ray.direction);

            if (depth < 0.0f)
                return float.PositiveInfinity;

            Vector3 closest = ray.origin + ray.direction * depth;
            return Vector3.Distance(point, closest);
        }

        private static bool RaySphere(Ray ray, Vector3 center, float radius, out float depth)
        {
            depth = 0.0f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0.0f)
                return false;

            float sqrt = Mathf.Sqrt(discriminant);
            float t = -b - sqrt;
            if (t < 0.0f)
                t = -b + sqrt;
            if (t < 0.0f)
                return false;

            depth = t;
            return true;
        }

        private static float NormalizeAngle(float degrees)
        {
            while (degrees > 180.0f)
                degrees -= 360.0f;
            while (degrees < -180.0f)
                degrees += 360.0f;
            return degrees;
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }
    }
}
