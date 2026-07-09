using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class OrbitCameraController : MonoBehaviour
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

    [Tooltip("Higher value means faster zoom smoothing.")]
    public float zoomSmoothness = 12f;

    [Header("Pan Settings")]
    [Tooltip("Middle mouse button pan speed.")]
    public float panSpeed = 0.018f;

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
    private bool initialized;

    private void Awake()
    {
        InitializeCamera();
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

        bool pointerOverUI =
            blockInputOverUI &&
            EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();

        if (!pointerOverUI)
        {
            HandleOrbitInput();
            HandleZoomInput();
            HandlePanInput();
        }

        HandleKeyboardInput();
        HandleAutoOrbit();
        HandleCursorState();
    }

    private void LateUpdate()
    {
        ApplyCameraTransform(false);
    }

    private void HandleOrbitInput()
    {
        // Right Mouse Button = rotate around target
        if (Input.GetMouseButton(1))
        {
            lastInputTime = Time.unscaledTime;

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            yaw += mouseX * yawSpeed * Time.unscaledDeltaTime;
            pitch -= mouseY * pitchSpeed * Time.unscaledDeltaTime;

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void HandleZoomInput()
    {
        // Mouse Wheel = zoom in / zoom out
        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) > 0.001f)
        {
            lastInputTime = Time.unscaledTime;

            targetDistance -= scroll * zoomSpeed;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }

        distance = Mathf.Lerp(
            distance,
            targetDistance,
            1f - Mathf.Exp(-zoomSmoothness * Time.unscaledDeltaTime)
        );
    }

    private void HandlePanInput()
    {
        // Middle Mouse Button = pan focus point
        if (Input.GetMouseButton(2))
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

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            FocusTarget();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetView();
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

        // Ensure shortest rotation path.
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