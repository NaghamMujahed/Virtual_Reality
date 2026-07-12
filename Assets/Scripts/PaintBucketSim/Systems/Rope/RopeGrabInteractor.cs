using PaintBucketSim.Systems.Bucket;
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
        [SerializeField] private BucketSystem bucketSystem;
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
        private bool _draggingBucket;
        private Plane _dragPlane;
        private Vector3 _dragPlaneNormal;
        private float _depthOffset;
        private Vector3 _lastTarget;
        private Vector3 _bucketGrabToAttachmentOffset;
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

            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            if (targetCamera == null)
                targetCamera = ResolveCamera();
        }

        private void OnDisable()
        {
            EndGrab();
        }

        private void Update()
        {
            if (!enableGameViewInput || !Application.isPlaying)
                return;

            Camera camera = targetCamera != null && targetCamera.isActiveAndEnabled
                ? targetCamera
                : ResolveCamera();
            if (camera != null && camera != targetCamera)
                targetCamera = camera;
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
            Vector3 grabPoint;
            if (ropeSystem != null && ropeSystem.IsInitialized)
            {
                float radius = pickRadiusMeters;
                if (ropeSystem.Config != null)
                {
                    radius = Mathf.Max(
                        radius,
                        ropeSystem.Config.visualRadiusMeters * 3.0f);
                }

                bool hitRope = ropeSystem.TryFindClosestSegment(
                    ray,
                    radius,
                    out int segmentIndex,
                    out float segmentT,
                    out grabPoint,
                    out _);
                if (hitRope &&
                    ropeSystem.BeginGrab(segmentIndex, segmentT, grabPoint))
                {
                    BeginDrag(ray, camera, grabPoint, false);
                    return true;
                }
            }

            if (bucketSystem == null ||
                !TryPickBucket(ray, out grabPoint) ||
                !bucketSystem.BeginGrab(grabPoint))
            {
                return false;
            }

            BeginDrag(ray, camera, grabPoint, true);
            return true;
        }

        private void BeginDrag(
            Ray ray,
            Camera camera,
            Vector3 grabPoint,
            bool bucket)
        {
            SetupDragPlane(ray, camera, grabPoint);
            _dragging = true;
            _draggingBucket = bucket;
            _lastTarget = grabPoint;
            _bucketGrabToAttachmentOffset = bucket && bucketSystem != null
                ? bucketSystem.GetAttachmentWorldPosition() - grabPoint
                : Vector3.zero;
        }

        private bool TryPickBucket(Ray ray, out Vector3 grabPoint)
        {
            grabPoint = Vector3.zero;
            Renderer bucketRenderer =
                bucketSystem != null
                    ? bucketSystem.GetComponent<Renderer>()
                    : null;
            if (bucketRenderer == null ||
                !bucketRenderer.bounds.IntersectRay(ray, out float distance))
                return false;

            grabPoint = ray.GetPoint(distance);
            return true;
        }

        public void UpdateGrab(Ray ray, Camera camera)
        {
            if (!_dragging ||
                (ropeSystem == null && bucketSystem == null))
                return;

            if (!_dragPlane.Raycast(ray, out float enter))
                return;

            Vector3 target = ray.GetPoint(enter) + _dragPlaneNormal * _depthOffset;
            Vector3 pointerTarget = target;
            if (_draggingBucket)
            {
                target = ConstrainBucketTargetToRopeReach(target);
                bucketSystem?.MoveGrab(target);
            }
            else
                ropeSystem?.MoveGrab(target);
            _lastTarget = pointerTarget;
        }

        public void AdjustGrabDepth(float scrollSteps)
        {
            _depthOffset += scrollSteps * scrollDepthSpeed;
        }

        public void EndGrab()
        {
            ropeSystem?.EndGrab();
            bucketSystem?.EndGrab();

            _dragging = false;
            _draggingBucket = false;
            _depthOffset = 0.0f;
            _bucketGrabToAttachmentOffset = Vector3.zero;
        }

        private Vector3 ConstrainBucketTargetToRopeReach(Vector3 target)
        {
            if (ropeSystem == null || ropeSystem.Config == null ||
                ropeSystem.IsBroken)
            {
                return target;
            }

            Vector3 pivot = ropeSystem.GetSimulatedPivotPosition();
            Vector3 desiredAttachment =
                target + _bucketGrabToAttachmentOffset;
            Vector3 pivotToAttachment = desiredAttachment - pivot;
            if (pivotToAttachment.sqrMagnitude < 1e-8f)
                return target;

            float maximumLength = ropeSystem.GetMaximumReachableLength();
            float distance = pivotToAttachment.magnitude;
            if (float.IsInfinity(maximumLength) ||
                distance <= maximumLength)
            {
                return target;
            }

            desiredAttachment =
                pivot +
                pivotToAttachment * (maximumLength / distance);
            return desiredAttachment - _bucketGrabToAttachmentOffset;
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
