using UnityEngine;
using UnityEngine.EventSystems;

namespace PaintBucketSim.Systems.Rope
{
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    public sealed class RopeGrabInteractor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private Camera targetCamera;

        [Header("Picking")]
        [SerializeField] private bool enableGameViewInput = true;
        [SerializeField, Min(0.005f)] private float pickRadiusMeters = 0.085f;

        [Header("Drag")]
        [SerializeField, Min(0.01f)] private float scrollDepthSpeed = 0.18f;

        [Header("Debug")]
        [SerializeField] private bool drawParticleGizmos = true;
        [SerializeField] private Color particleGizmoColor = new Color(0.2f, 0.85f, 1.0f, 0.85f);
        [SerializeField] private Color grabGizmoColor = new Color(1.0f, 0.72f, 0.15f, 1.0f);

        private bool _dragging;
        private Plane _dragPlane;
        private Vector3 _dragPlaneNormal;
        private float _depthOffset;
        private Vector3 _lastTarget;
        private Vector3[] _positionsBuffer;

        public bool IsDragging => _dragging;
        public float PickRadiusMeters
        {
            get => pickRadiusMeters;
            set => pickRadiusMeters = Mathf.Max(0.005f, value);
        }

        public float ScrollDepthSpeed
        {
            get => scrollDepthSpeed;
            set => scrollDepthSpeed = Mathf.Max(0.01f, value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOnSceneRopes()
        {
            EnsureSceneInteractors();
        }

        public static void EnsureSceneInteractors()
        {
            RopeSystem[] ropeSystems = FindObjectsByType<RopeSystem>();
            for (int i = 0; i < ropeSystems.Length; i++)
            {
                RopeSystem rope = ropeSystems[i];
                if (rope == null || rope.GetComponent<RopeGrabInteractor>() != null)
                    continue;

                RopeGrabInteractor interactor = rope.gameObject.AddComponent<RopeGrabInteractor>();
                interactor.ropeSystem = rope;
            }
        }

        private void Awake()
        {
            if (ropeSystem == null)
                ropeSystem = GetComponent<RopeSystem>();

            if (targetCamera == null)
                targetCamera = ResolveCamera();
        }

        private void Update()
        {
            if (!enableGameViewInput || !Application.isPlaying)
                return;

            Camera camera = targetCamera != null ? targetCamera : ResolveCamera();
            if (camera == null || ropeSystem == null)
                return;

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                TryBeginGrab(ray, camera);
            }

            if (_dragging && Input.GetMouseButton(0))
            {
                float scroll = Input.mouseScrollDelta.y;
                if (!Mathf.Approximately(scroll, 0.0f))
                    AdjustGrabDepth(scroll);

                Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                UpdateGrab(ray, camera);
            }

            if (_dragging && Input.GetMouseButtonUp(0))
                EndGrab();
        }

        public bool TryBeginGrab(Ray ray, Camera camera)
        {
            if (ropeSystem == null || !ropeSystem.IsInitialized)
                return false;

            float radius = pickRadiusMeters;
            if (ropeSystem.Config != null)
                radius = Mathf.Max(radius, ropeSystem.Config.visualRadiusMeters * 3.0f);

            bool hit = ropeSystem.TryFindClosestSegment(
                ray,
                radius,
                out int segmentIndex,
                out float segmentT,
                out Vector3 grabPoint,
                out _);

            if (!hit || !ropeSystem.BeginGrab(segmentIndex, segmentT, grabPoint))
                return false;

            SetupDragPlane(ray, camera, grabPoint);
            _dragging = true;
            _lastTarget = grabPoint;
            return true;
        }

        public void UpdateGrab(Ray ray, Camera camera)
        {
            if (!_dragging || ropeSystem == null)
                return;

            if (!_dragPlane.Raycast(ray, out float enter))
                return;

            Vector3 target = ray.GetPoint(enter) + _dragPlaneNormal * _depthOffset;
            ropeSystem.MoveGrab(target);
            _lastTarget = target;
        }

        public void AdjustGrabDepth(float scrollSteps)
        {
            _depthOffset += scrollSteps * scrollDepthSpeed;
        }

        public void EndGrab()
        {
            if (ropeSystem != null)
                ropeSystem.EndGrab();

            _dragging = false;
            _depthOffset = 0.0f;
        }

        private void SetupDragPlane(Ray ray, Camera camera, Vector3 grabPoint)
        {
            _dragPlaneNormal = camera != null
                ? camera.transform.forward
                : -ray.direction.normalized;

            if (_dragPlaneNormal.sqrMagnitude < 1e-8f)
                _dragPlaneNormal = Vector3.forward;

            _dragPlaneNormal.Normalize();
            _dragPlane = new Plane(_dragPlaneNormal, grabPoint);
            _depthOffset = 0.0f;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
        }

        private static Camera ResolveCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            Camera[] cameras = Camera.allCameras;
            return cameras != null && cameras.Length > 0 ? cameras[0] : null;
        }

        private void OnDrawGizmos()
        {
            RopeSystem rope = ropeSystem != null ? ropeSystem : GetComponent<RopeSystem>();
            if (!Application.isPlaying || rope == null || !rope.IsInitialized)
                return;

            if (drawParticleGizmos)
            {
                int count = rope.ParticleCount;
                if (_positionsBuffer == null || _positionsBuffer.Length != count)
                    _positionsBuffer = new Vector3[count];

                rope.CopyPositionsTo(_positionsBuffer);
                Gizmos.color = particleGizmoColor;
                float radius = rope.Config != null
                    ? Mathf.Max(rope.Config.visualRadiusMeters * 0.7f, 0.006f)
                    : 0.01f;

                for (int i = 1; i < _positionsBuffer.Length; i++)
                    Gizmos.DrawSphere(_positionsBuffer[i], radius);
            }

            if (_dragging)
            {
                Gizmos.color = grabGizmoColor;
                Gizmos.DrawSphere(_lastTarget, 0.055f);
            }
        }
    }
}
