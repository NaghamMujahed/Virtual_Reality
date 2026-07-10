using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class InteractiveCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The object or empty transform that the camera will orbit around.")]
    public Transform target;

    [Tooltip("Offset from the target position. Useful to look above the bucket or toward the canvas.")]
    public Vector3 targetOffset = new Vector3(0f, 1.2f, 0f);

    [Tooltip("If enabled, the camera focus follows the target every frame.")]
    public bool followTarget = true;

    [Header("Orbit Settings")]
    public float defaultDistance = 7.5f;
    public float minDistance = 1.5f;
    public float maxDistance = 20f;

    public float defaultYaw = 35f;
    public float defaultPitch = 30f;

    public float yawSpeed = 180f;
    public float pitchSpeed = 120f;

    public float minPitch = -10f;
    public float maxPitch = 80f;

    [Header("Zoom Settings")]
    [Tooltip("Mouse wheel zoom speed.")]
    public float zoomSpeed = 3.0f;

    [Tooltip("Pinch zoom speed for devices that report two-finger zoom as touch input.")]
    public float pinchZoomSpeed = 0.01f;

    [Tooltip("Zoom response for a native Windows touchpad pinch gesture.")]
    public float touchpadPinchZoomSpeed = 8.0f;

    [Tooltip("Higher value means faster zoom smoothing.")]
    public float zoomSmoothness = 12f;

    [Header("Pan Settings")]
    [Tooltip("Middle mouse or Shift + drag pan speed.")]
    public float panSpeed = 0.018f;

    [Tooltip("World-space response to a native Windows two-finger pan gesture.")]
    public float touchpadPanSpeed = 0.0015f;

    [Tooltip("Arrow key and WASD pan speed.")]
    public float keyboardPanSpeed = 0.65f;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.06f;
    public float rotationSmoothTime = 0.04f;

    [Header("Presentation Auto Orbit")]
    public bool enableAutoOrbit = false;
    public float autoOrbitSpeed = 8f;
    public float autoOrbitDelay = 3f;

    [Header("Input")]
    public bool blockInputOverUI = true;
    public bool lockCursorWhileOrbiting = false;

    private float yaw;
    private float pitch;

    private float distance;
    private float targetDistance;

    private Vector3 focusPoint;
    private Vector3 focusVelocity;
    private Vector3 panOffset;

    private Quaternion rotationVelocity;

    private float lastInputTime;
    private float previousPinchDistance;
    private bool initialized;
    private bool hasPreviousPinchDistance;
    private Camera controlledCamera;

    public bool IsViewActive =>
        isActiveAndEnabled &&
        (controlledCamera == null || controlledCamera.enabled);

    public bool AutoOrbitEnabled
    {
        get => enableAutoOrbit;
        set
        {
            enableAutoOrbit = value;
            lastInputTime = Time.unscaledTime;
        }
    }

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        WindowsTouchpadGestures.Acquire();
        InitializeCamera();
    }

    private void OnDestroy()
    {
        WindowsTouchpadGestures.Release();
    }

    private void Reset()
    {
        Camera cam = GetComponent<Camera>();

        if (cam != null)
        {
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 500f;
        }
    }

    private void InitializeCamera()
    {
        yaw = defaultYaw;
        pitch = defaultPitch;

        distance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        targetDistance = distance;

        panOffset = Vector3.zero;
        focusPoint = GetBaseFocusPoint();

        lastInputTime = Time.unscaledTime;

        initialized = true;

        ApplyCameraTransform(true);
    }

    private void Update()
    {
        if (!initialized)
            InitializeCamera();

        if (!IsViewActive)
            return;

        bool pointerOverUI =
            blockInputOverUI &&
            EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();

        if (!pointerOverUI)
        {
            HandleOrbitInput();
            HandleMouseWheelZoom();
            HandlePanInput();
        }

        HandleTouchpadGestures();
        HandleTouchscreenPinch();
        UpdateZoomSmoothing();
        HandleKeyboardInput();
        HandleAutoOrbit();
        HandleCursorState();
    }

    private void LateUpdate()
    {
        if (IsViewActive)
            ApplyCameraTransform(false);
    }

    private void HandleOrbitInput()
    {
        bool shiftHeld =
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);
        if (Input.GetMouseButton(1) && !shiftHeld)
        {
            lastInputTime = Time.unscaledTime;

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            yaw += mouseX * yawSpeed * Time.unscaledDeltaTime;
            pitch -= mouseY * pitchSpeed * Time.unscaledDeltaTime;

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void HandleMouseWheelZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) <= 0.001f)
        {
            scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        }

        if (Mathf.Abs(scroll) > 0.0001f)
            ApplyZoomDelta(scroll * zoomSpeed);
    }

    private void HandleTouchpadGestures()
    {
        if (!WindowsTouchpadGestures.TryConsume(
                out float pinchZoom,
                out Vector2 panPixels))
        {
            return;
        }

        if (Mathf.Abs(pinchZoom) > 0.00001f)
        {
            ApplyZoomDelta(
                pinchZoom * Mathf.Max(touchpadPinchZoomSpeed, 0.01f));
        }

        if (panPixels.sqrMagnitude > 0.0001f)
            ApplyTouchpadPan(panPixels);
    }

    private void HandleTouchscreenPinch()
    {
        if (Input.touchCount >= 2)
        {
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            float pinchDistance = Vector2.Distance(first.position, second.position);

            if (hasPreviousPinchDistance)
            {
                float pinchDelta = pinchDistance - previousPinchDistance;
                if (Mathf.Abs(pinchDelta) > 0.01f)
                    ApplyZoomDelta(pinchDelta * pinchZoomSpeed);
            }

            previousPinchDistance = pinchDistance;
            hasPreviousPinchDistance = true;
        }
        else
        {
            hasPreviousPinchDistance = false;
        }
    }

    private void UpdateZoomSmoothing()
    {
        distance = Mathf.Lerp(
            distance,
            targetDistance,
            1f - Mathf.Exp(-zoomSmoothness * Time.unscaledDeltaTime)
        );
    }

    private void ApplyZoomDelta(float zoomDelta)
    {
        lastInputTime = Time.unscaledTime;
        targetDistance -= zoomDelta;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
    }

    private void HandlePanInput()
    {
        bool shiftHeld =
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);
        bool dragPan =
            Input.GetMouseButton(2) ||
            (shiftHeld &&
             (Input.GetMouseButton(0) || Input.GetMouseButton(1)));

        if (dragPan)
        {
            lastInputTime = Time.unscaledTime;

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            Vector3 right = transform.right;
            Vector3 up = transform.up;

            float scale = panSpeed * distance;

            panOffset += (-right * mouseX - up * mouseY) * scale;
        }
    }

    private void ApplyTouchpadPan(Vector2 screenDelta)
    {
        lastInputTime = Time.unscaledTime;
        float scale = Mathf.Max(touchpadPanSpeed, 0.00001f) * distance;
        panOffset +=
            (-transform.right * screenDelta.x + transform.up * screenDelta.y) *
            scale;
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            FocusTarget();
        }

        if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Home))
        {
            ResetView();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            AutoOrbitEnabled = !AutoOrbitEnabled;
        }

        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null)
        {
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
            horizontal -= 1f;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
            horizontal += 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
            vertical -= 1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
            vertical += 1f;

        Vector2 movement = new Vector2(horizontal, vertical);
        if (movement.sqrMagnitude > 0.001f)
        {
            movement.Normalize();
            float scale =
                Mathf.Max(keyboardPanSpeed, 0.01f) *
                distance *
                Time.unscaledDeltaTime;
            panOffset +=
                (transform.right * movement.x + transform.up * movement.y) *
                scale;
            lastInputTime = Time.unscaledTime;
        }
    }

    private void HandleAutoOrbit()
    {
        if (!enableAutoOrbit)
            return;

        bool noRecentInput = Time.unscaledTime - lastInputTime > autoOrbitDelay;

        if (noRecentInput)
        {
            yaw += autoOrbitSpeed * Time.unscaledDeltaTime;
        }
    }

    private void HandleCursorState()
    {
        if (!lockCursorWhileOrbiting)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void FocusTarget()
    {
        lastInputTime = Time.unscaledTime;

        panOffset = Vector3.zero;
        targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
    }

    public void ResetView()
    {
        lastInputTime = Time.unscaledTime;

        yaw = defaultYaw;
        pitch = defaultPitch;

        panOffset = Vector3.zero;

        distance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        targetDistance = distance;

        ApplyCameraTransform(true);
    }

    private Vector3 GetBaseFocusPoint()
    {
        if (target != null)
            return target.position + targetOffset;

        return transform.position + transform.forward * distance;
    }

    private void ApplyCameraTransform(bool immediate)
    {
        Vector3 desiredFocus;

        if (followTarget && target != null)
        {
            desiredFocus = GetBaseFocusPoint() + panOffset;
        }
        else
        {
            desiredFocus = focusPoint + panOffset;
        }

        if (immediate)
        {
            focusPoint = desiredFocus;
        }
        else
        {
            focusPoint = Vector3.SmoothDamp(
                focusPoint,
                desiredFocus,
                ref focusVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime
            );
        }

        Quaternion desiredRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = focusPoint - desiredRotation * Vector3.forward * distance;

        if (immediate)
        {
            transform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                1f - Mathf.Exp(-20f * Time.unscaledDeltaTime)
            );

            transform.rotation = SmoothDampQuaternion(
                transform.rotation,
                desiredRotation,
                ref rotationVelocity,
                rotationSmoothTime
            );
        }
    }

    private static Quaternion SmoothDampQuaternion(
        Quaternion current,
        Quaternion target,
        ref Quaternion velocity,
        float smoothTime)
    {
        if (Time.unscaledDeltaTime < Mathf.Epsilon)
            return current;

        if (Quaternion.Dot(current, target) < 0f)
        {
            target.x = -target.x;
            target.y = -target.y;
            target.z = -target.z;
            target.w = -target.w;
        }

        Vector4 result = new Vector4(
            Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime),
            Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime),
            Mathf.SmoothDamp(current.z, target.z, ref velocity.z, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime),
            Mathf.SmoothDamp(current.w, target.w, ref velocity.w, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime)
        );

        Quaternion q = new Quaternion(result.x, result.y, result.z, result.w);
        return NormalizeQuaternion(q);
    }

    private static Quaternion NormalizeQuaternion(Quaternion q)
    {
        float magnitude = Mathf.Sqrt(
            q.x * q.x +
            q.y * q.y +
            q.z * q.z +
            q.w * q.w
        );

        if (magnitude > Mathf.Epsilon)
        {
            q.x /= magnitude;
            q.y /= magnitude;
            q.z /= magnitude;
            q.w /= magnitude;
        }

        return q;
    }
}
