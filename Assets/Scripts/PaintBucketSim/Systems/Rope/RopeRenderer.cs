using UnityEngine;

namespace PaintBucketSim.Systems.Rope
{
    [RequireComponent(typeof(LineRenderer))]
    public class RopeRenderer : MonoBehaviour
    {
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private LineRenderer lineRenderer;

        [Header("Visual")]
        [SerializeField] private Color ropeColor = new Color(0.85f, 0.75f, 0.55f);
        [SerializeField] private Color brokenPartColor = new Color(1.0f, 0.45f, 0.25f);
        [SerializeField] private float fallbackWidth = 0.02f;

        private Vector3[] _positionsBuffer;
        private Vector3[] _topBuffer;
        private Vector3[] _bottomBuffer;

        private LineRenderer _detachedLineRenderer;

        private void Awake()
        {
            if (ropeSystem == null)
                ropeSystem = FindFirstObjectByType<RopeSystem>();

            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();

            ConfigureLineRenderer(lineRenderer, ropeColor);
            EnsureDetachedRenderer();
        }

        private void LateUpdate()
        {
            if (ropeSystem == null || !ropeSystem.IsInitialized)
                return;

            int count = ropeSystem.ParticleCount;
            if (count <= 0)
                return;

            if (_positionsBuffer == null || _positionsBuffer.Length != count)
                _positionsBuffer = new Vector3[count];

            ropeSystem.CopyPositionsTo(_positionsBuffer);

            float width = ropeSystem.Config != null
                ? ropeSystem.Config.visualRadiusMeters * 2.0f
                : fallbackWidth;

            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;

            if (_detachedLineRenderer != null)
            {
                _detachedLineRenderer.startWidth = width;
                _detachedLineRenderer.endWidth = width;
            }

            if (!ropeSystem.IsBroken)
            {
                lineRenderer.positionCount = count;
                lineRenderer.SetPositions(_positionsBuffer);

                if (_detachedLineRenderer != null)
                    _detachedLineRenderer.positionCount = 0;

                return;
            }

            int brokenSegment = ropeSystem.BrokenSegmentIndex;

            if (brokenSegment < 0 || brokenSegment >= count - 1)
            {
                lineRenderer.positionCount = count;
                lineRenderer.SetPositions(_positionsBuffer);

                if (_detachedLineRenderer != null)
                    _detachedLineRenderer.positionCount = 0;

                return;
            }

            int topCount = brokenSegment + 1;
            int bottomStart = brokenSegment + 1;
            int bottomCount = count - bottomStart;

            if (_topBuffer == null || _topBuffer.Length != topCount)
                _topBuffer = new Vector3[topCount];

            if (_bottomBuffer == null || _bottomBuffer.Length != bottomCount)
                _bottomBuffer = new Vector3[bottomCount];

            for (int i = 0; i < topCount; i++)
                _topBuffer[i] = _positionsBuffer[i];

            for (int i = 0; i < bottomCount; i++)
                _bottomBuffer[i] = _positionsBuffer[bottomStart + i];

            lineRenderer.positionCount = topCount;
            lineRenderer.SetPositions(_topBuffer);

            EnsureDetachedRenderer();

            _detachedLineRenderer.positionCount = bottomCount;
            _detachedLineRenderer.SetPositions(_bottomBuffer);
        }

        private void EnsureDetachedRenderer()
        {
            if (_detachedLineRenderer != null)
                return;

            GameObject go = new GameObject("DetachedRopePart");
            go.transform.SetParent(transform, false);

            _detachedLineRenderer = go.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_detachedLineRenderer, brokenPartColor);
        }

        private void ConfigureLineRenderer(LineRenderer renderer, Color color)
        {
            if (renderer == null)
                return;

            renderer.useWorldSpace = true;
            renderer.positionCount = 0;

            renderer.startColor = color;
            renderer.endColor = color;

            renderer.startWidth = fallbackWidth;
            renderer.endWidth = fallbackWidth;
        }
    }
}