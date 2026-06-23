using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation
{
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class RopeGrab : MonoBehaviour
    {
        [Header("Pick")]
        [SerializeField, Min(5f)]    float _pickRadiusPx    = 60f;

        [Header("Drag")]
        [SerializeField, Min(0.01f)] float _dragSensitivity = 1f;
        [SerializeField, Min(0.01f)] float _scrollSpeed     = 0.5f;

        // Auto-resolved in Awake — no manual wiring needed
        RopeSimulation _sim;
        Camera         _cam;

        bool    _grabbing;
        Vector3 _grabWorldPos;
        float   _grabDepth;

        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            // Find RopeSimulation on same GameObject — no Inspector wiring needed
            _sim = GetComponent<RopeSimulation>();
            if (_sim == null)
                Debug.LogError("[RopeGrab] RopeSimulation not found on this GameObject.");

            // Find any active camera (Camera.allCameras works in all Unity versions)
            _cam = Camera.main;
            if (_cam == null && Camera.allCameras.Length > 0)
                _cam = Camera.allCameras[0];
            if (_cam == null)
                Debug.LogError("[RopeGrab] No camera found in scene.");
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null || _sim == null) return;

            // ── Press: pick nearest particle ─────────────────────────────────
            if (mouse.leftButton.wasPressedThisFrame)
                TryGrab(mouse.position.ReadValue());

            // ── Held: drag ────────────────────────────────────────────────────
            if (_grabbing && mouse.leftButton.isPressed)
            {
                Vector2 px = mouse.delta.ReadValue();
                if (px.sqrMagnitude > 0f)
                {
                    float uPerPx = _grabDepth
                        * (2f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad))
                        / Screen.height * _dragSensitivity;

                    Vector3 right   = Vector3.ProjectOnPlane(_cam.transform.right,   Vector3.up);
                    Vector3 forward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up);
                    if (right.sqrMagnitude   < 0.01f) right   = Vector3.right;
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

                    _grabWorldPos += right.normalized   * (px.x * uPerPx)
                                   + forward.normalized * (px.y * uPerPx);
                    _grabDepth = Vector3.Distance(_cam.transform.position, _grabWorldPos);
                    _sim.MoveGrab(_grabWorldPos);
                }

                float s = mouse.scroll.ReadValue().y;
                if (s != 0f)
                {
                    _grabWorldPos += Vector3.up * (s * _scrollSpeed);
                    _sim.MoveGrab(_grabWorldPos);
                }
            }

            // ── Release ───────────────────────────────────────────────────────
            if (_grabbing && mouse.leftButton.wasReleasedThisFrame)
            {
                _sim.EndGrab();
                _grabbing = false;
            }
        }

        void TryGrab(Vector2 mouseScreenPos)
        {
            Vector3[] positions = _sim.SnapshotPositions();
            if (positions.Length == 0)
            {
                Debug.LogWarning("[RopeGrab] No positions — simulation not started yet.");
                return;
            }

            float minDist = float.MaxValue;
            int   bestIdx = -1;

            for (int i = 1; i < positions.Length; i++) // skip particle 0 (fixed anchor)
            {
                Vector3 sp = _cam.WorldToScreenPoint(positions[i]);
                if (sp.z < 0f) continue;

                float d = Vector2.Distance(new Vector2(sp.x, sp.y), mouseScreenPos);
                if (d < minDist && d < _pickRadiusPx)
                {
                    minDist = d;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                Debug.Log($"[RopeGrab] No particle within {_pickRadiusPx}px of click. " +
                          $"Closest was {minDist:F0}px away. Try clicking directly on the rope.");
                return;
            }

            _grabWorldPos = positions[bestIdx];
            _grabDepth    = Vector3.Distance(_cam.transform.position, _grabWorldPos);
            _sim.BeginGrab(bestIdx, _grabWorldPos);
            _grabbing = true;
            Debug.Log($"[RopeGrab] Grabbed particle {bestIdx} at {_grabWorldPos}");
        }

        // ─── Runtime properties (read/written by RopeDebugUI) ────────────────

        public float PickRadiusPx    { get => _pickRadiusPx;    set => _pickRadiusPx    = Mathf.Max(5f, value); }
        public float DragSensitivity { get => _dragSensitivity; set => _dragSensitivity = Mathf.Max(0.01f, value); }

        // Show all particles as gizmos in Scene view for debugging
        void OnDrawGizmos()
        {
            if (_sim == null || !Application.isPlaying) return;

            Vector3[] pos = _sim.SnapshotPositions();
            for (int i = 0; i < pos.Length; i++)
            {
                Gizmos.color = (i == 0) ? Color.red : (_grabbing ? Color.yellow : Color.cyan);
                Gizmos.DrawSphere(pos[i], 0.04f);
            }
        }
    }
}
