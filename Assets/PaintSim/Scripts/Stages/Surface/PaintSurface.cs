using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;

namespace PaintSim.Scripts.Stages.Surface
{
    public enum PaintSurfaceSizingMode
    {
        FitRendererBounds = 0,
        ExplicitGridSize = 1
    }

    public enum PaintSurfacePlaneMode
    {
        AutoFromMeshBounds = 0,
        LocalXY = 1,
        LocalXZ = 2,
        LocalYZ = 3
    }

    [RequireComponent(typeof(MeshRenderer))]
    public sealed class PaintSurface : MonoBehaviour
    {
        [Header("Surface Type")]
        [SerializeField] private SurfaceType _surfaceType = SurfaceType.Wood;

        [Header("Grid Settings")]
        [SerializeField] private PaintSurfaceSizingMode _sizingMode =
            PaintSurfaceSizingMode.FitRendererBounds;
        [SerializeField] private PaintSurfacePlaneMode _planeMode =
            PaintSurfacePlaneMode.AutoFromMeshBounds;
        [SerializeField] private int _gridWidth = 256;
        [SerializeField] private int _gridHeight = 256;
        [SerializeField] private float _cellSize = 0.01f;
        [SerializeField] private bool _resizeTransformToExplicitGrid = false;

        [Header("Impact Capture")]
        [SerializeField, Min(0.0001f)] private float _impactCaptureDistance = 0.03f;
        [SerializeField] private bool _doubleSidedImpact = true;

        [Header("Rendering")]
        [SerializeField] private ComputeShader _paintFilmBakerShader;
        [SerializeField] private float _maxThickness = 0.0001f;
        [SerializeField] private float _wetnessShine = 0.8f;
        [SerializeField, Min(1)] private int _renderEveryNFrames = 1;

        [Header("Evolution / Drying")]
        [SerializeField] private bool _enableEvolution = true;
        [SerializeField] private ComputeShader _evaporationShader;
        [SerializeField] private float _evaporationRate = 0.05f;
        [SerializeField] private float _diffusionRate = 30.0f;
        [SerializeField, Min(0.0f)] private float _runoffRate = 0.18f;
        [SerializeField, Min(0.000001f)] private float _minimumWetThickness = 0.000015f;
        [SerializeField, Min(1)] private int _evolveEveryNFrames = 2;

        [Header("Debug / Validation")]
        [SerializeField] private Color _debugStampColor = new Color(1.0f, 0.05f, 0.02f, 1.0f);
        [SerializeField, Min(0.000001f)] private float _debugStampThickness = 0.00008f;

        public PaintFilmGrid PaintFilmGrid { get; private set; }
        public SurfaceProperties SurfaceProperties { get; private set; }
        public SurfaceType SurfaceType => _surfaceType;

        private PaintSurfaceRenderer _renderer;
        private PaintEvolver _evolver;
        private MeshRenderer _meshRenderer;
        private MeshFilter _meshFilter;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshFilter = GetComponent<MeshFilter>();
            SurfaceProperties = SurfaceProperties.FromType(_surfaceType);
            ResolveShaders();

            InitGrid();
            InitEvolver();
            InitRenderer();
        }

        private void ResolveShaders()
        {
            if (_paintFilmBakerShader == null)
            {
                _paintFilmBakerShader =
                    Resources.Load<ComputeShader>("ComputeShaders/Surface/PaintFilmBaker");
            }

            if (_evaporationShader == null)
            {
                _evaporationShader =
                    Resources.Load<ComputeShader>("ComputeShaders/Surface/PaintEvaporation");
            }
        }

        private void InitGrid()
        {
            _gridWidth = Mathf.Max(1, _gridWidth);
            _gridHeight = Mathf.Max(1, _gridHeight);
            _cellSize = Mathf.Max(_cellSize, 0.0001f);

            ResolveSurfaceFrame(
                out Vector3 originWS,
                out Vector3 axisU,
                out Vector3 axisV,
                out Vector3 normalWS,
                out float worldWidth,
                out float worldHeight
            );

            float cellSizeU = Mathf.Max(worldWidth / _gridWidth, 0.0001f);
            float cellSizeV = Mathf.Max(worldHeight / _gridHeight, 0.0001f);
            _cellSize = Mathf.Sqrt(cellSizeU * cellSizeV);

            PaintFilmGrid = new PaintFilmGrid(
                gridWidth: _gridWidth,
                gridHeight: _gridHeight,
                cellSizeU: cellSizeU,
                cellSizeV: cellSizeV,
                surfaceY: originWS.y,
                gridOrigin: new Vector2(originWS.x, originWS.z),
                surfaceOriginWS: originWS,
                surfaceAxisU: axisU,
                surfaceAxisV: axisV,
                surfaceNormalWS: normalWS,
                impactCaptureDistance: Mathf.Max(
                    _impactCaptureDistance,
                    Mathf.Max(cellSizeU, cellSizeV) * 1.5f
                ),
                doubleSidedImpact: _doubleSidedImpact
            );
        }

        private void ResolveSurfaceFrame(
            out Vector3 originWS,
            out Vector3 axisU,
            out Vector3 axisV,
            out Vector3 normalWS,
            out float worldWidth,
            out float worldHeight)
        {
            Bounds localBounds = GetLocalSurfaceBounds();
            PaintSurfacePlaneMode resolvedPlane = ResolvePlaneMode(localBounds);
            ResolvePlaneAxes(resolvedPlane, out axisU, out axisV, out normalWS);

            if (_sizingMode == PaintSurfaceSizingMode.ExplicitGridSize)
            {
                worldWidth = Mathf.Max(_gridWidth * _cellSize, 0.0001f);
                worldHeight = Mathf.Max(_gridHeight * _cellSize, 0.0001f);

                if (_resizeTransformToExplicitGrid)
                    ResizeTransformForExplicitGrid(resolvedPlane, worldWidth, worldHeight);

                Vector3 center = transform.position;
                originWS = center - axisU * (worldWidth * 0.5f) - axisV * (worldHeight * 0.5f);
                return;
            }

            Vector3[] corners = GetWorldCorners(localBounds);
            ProjectCornersOntoFrame(
                corners,
                axisU,
                axisV,
                normalWS,
                out float minU,
                out float maxU,
                out float minV,
                out float maxV,
                out float minN,
                out float maxN
            );

            worldWidth = Mathf.Max(maxU - minU, _cellSize);
            worldHeight = Mathf.Max(maxV - minV, _cellSize);
            // Use the visible/contact face instead of the mesh center.
            // This matters for thick boards/cubes: the paint surface should be
            // on the top/front face, not buried halfway inside the object.
            float normalOffset = maxN;

            originWS = axisU * minU + axisV * minV + normalWS * normalOffset;
        }

        private Bounds GetLocalSurfaceBounds()
        {
            if (_meshFilter != null && _meshFilter.sharedMesh != null)
                return _meshFilter.sharedMesh.bounds;

            if (_meshRenderer != null)
            {
                Bounds worldBounds = _meshRenderer.bounds;
                Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);
                Vector3 localSize = transform.InverseTransformVector(worldBounds.size);
                localSize = new Vector3(
                    Mathf.Abs(localSize.x),
                    Mathf.Abs(localSize.y),
                    Mathf.Abs(localSize.z)
                );

                return new Bounds(localCenter, localSize);
            }

            return new Bounds(Vector3.zero, new Vector3(1.0f, 0.0f, 1.0f));
        }

        private PaintSurfacePlaneMode ResolvePlaneMode(Bounds localBounds)
        {
            if (_planeMode != PaintSurfacePlaneMode.AutoFromMeshBounds)
                return _planeMode;

            Vector3 size = localBounds.size;

            if (size.y <= size.x && size.y <= size.z)
                return PaintSurfacePlaneMode.LocalXZ;

            if (size.z <= size.x && size.z <= size.y)
                return PaintSurfacePlaneMode.LocalXY;

            return PaintSurfacePlaneMode.LocalYZ;
        }

        private void ResolvePlaneAxes(
            PaintSurfacePlaneMode planeMode,
            out Vector3 axisU,
            out Vector3 axisV,
            out Vector3 normalWS)
        {
            switch (planeMode)
            {
                case PaintSurfacePlaneMode.LocalXY:
                    axisU = transform.right.normalized;
                    axisV = transform.up.normalized;
                    normalWS = transform.forward.normalized;
                    break;
                case PaintSurfacePlaneMode.LocalYZ:
                    axisU = transform.up.normalized;
                    axisV = transform.forward.normalized;
                    normalWS = transform.right.normalized;
                    break;
                case PaintSurfacePlaneMode.LocalXZ:
                default:
                    axisU = transform.right.normalized;
                    axisV = transform.forward.normalized;
                    normalWS = transform.up.normalized;
                    break;
            }

            if (Vector3.Dot(Vector3.Cross(axisU, axisV), normalWS) < 0.0f)
                normalWS = -normalWS;
        }

        private void ResizeTransformForExplicitGrid(
            PaintSurfacePlaneMode planeMode,
            float worldWidth,
            float worldHeight)
        {
            Vector3 lossyScale = transform.lossyScale;
            Vector3 safeLossyScale = new Vector3(
                Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f),
                Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f),
                Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f)
            );

            Bounds localBounds = GetLocalSurfaceBounds();
            Vector3 localSize = localBounds.size;
            Vector3 scale = transform.localScale;

            switch (planeMode)
            {
                case PaintSurfacePlaneMode.LocalXY:
                    scale.x *= worldWidth / Mathf.Max(localSize.x * safeLossyScale.x, 0.0001f);
                    scale.y *= worldHeight / Mathf.Max(localSize.y * safeLossyScale.y, 0.0001f);
                    break;
                case PaintSurfacePlaneMode.LocalYZ:
                    scale.y *= worldWidth / Mathf.Max(localSize.y * safeLossyScale.y, 0.0001f);
                    scale.z *= worldHeight / Mathf.Max(localSize.z * safeLossyScale.z, 0.0001f);
                    break;
                case PaintSurfacePlaneMode.LocalXZ:
                default:
                    scale.x *= worldWidth / Mathf.Max(localSize.x * safeLossyScale.x, 0.0001f);
                    scale.z *= worldHeight / Mathf.Max(localSize.z * safeLossyScale.z, 0.0001f);
                    break;
            }

            transform.localScale = scale;
        }

        private Vector3[] GetWorldCorners(Bounds localBounds)
        {
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;

            return new[]
            {
                transform.TransformPoint(new Vector3(min.x, min.y, min.z)),
                transform.TransformPoint(new Vector3(max.x, min.y, min.z)),
                transform.TransformPoint(new Vector3(min.x, max.y, min.z)),
                transform.TransformPoint(new Vector3(max.x, max.y, min.z)),
                transform.TransformPoint(new Vector3(min.x, min.y, max.z)),
                transform.TransformPoint(new Vector3(max.x, min.y, max.z)),
                transform.TransformPoint(new Vector3(min.x, max.y, max.z)),
                transform.TransformPoint(new Vector3(max.x, max.y, max.z)),
            };
        }

        private static void ProjectCornersOntoFrame(
            Vector3[] corners,
            Vector3 axisU,
            Vector3 axisV,
            Vector3 normalWS,
            out float minU,
            out float maxU,
            out float minV,
            out float maxV,
            out float minN,
            out float maxN)
        {
            minU = minV = minN = float.PositiveInfinity;
            maxU = maxV = maxN = float.NegativeInfinity;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 corner = corners[i];
                float u = Vector3.Dot(corner, axisU);
                float v = Vector3.Dot(corner, axisV);
                float n = Vector3.Dot(corner, normalWS);

                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v);
                maxV = Mathf.Max(maxV, v);
                minN = Mathf.Min(minN, n);
                maxN = Mathf.Max(maxN, n);
            }
        }

        private void InitEvolver()
        {
            if (!_enableEvolution)
                return;

            if (_evaporationShader == null)
            {
                Debug.LogWarning("[PaintSurface] Evaporation shader is missing.");
                return;
            }

            _evolver = new PaintEvolver(_evaporationShader, PaintFilmGrid);
            _evolver.EvaporationRate = _evaporationRate;
            _evolver.DiffusionRate = EffectiveDiffusionRate();
            _evolver.RunoffRate = _runoffRate;
            _evolver.MinimumWetThickness = _minimumWetThickness;
        }

        private void InitRenderer()
        {
            if (_paintFilmBakerShader == null)
            {
                Debug.LogError("[PaintSurface] PaintFilmBaker shader is missing.");
                return;
            }

            _renderer = new PaintSurfaceRenderer(
                _paintFilmBakerShader,
                PaintFilmGrid,
                _meshRenderer
            );

            _renderer.MaxThickness = _maxThickness;
            _renderer.WetnessShine = _wetnessShine;
        }

        public void Render()
        {
            if (_enableEvolution &&
                _evolver != null &&
                Time.frameCount % Mathf.Max(1, _evolveEveryNFrames) == 0)
            {
                _evolver.EvaporationRate = _evaporationRate;
                _evolver.DiffusionRate = EffectiveDiffusionRate();
                _evolver.RunoffRate = _runoffRate;
                _evolver.MinimumWetThickness = _minimumWetThickness;
                _evolver.Evolve(Time.deltaTime * Mathf.Max(1, _evolveEveryNFrames));
            }

            if (Time.frameCount % Mathf.Max(1, _renderEveryNFrames) == 0)
                _renderer?.Render();
        }

        [ContextMenu("PaintSim/Debug Stamp Center")]
        private void DebugStampCenter()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[PaintSurface] Debug Stamp Center is intended for Play Mode " +
                    "after the paint grid has been initialized."
                );
                return;
            }

            if (PaintFilmGrid == null || PaintFilmGrid.PaintCellBuffer == null)
            {
                Debug.LogWarning("[PaintSurface] PaintFilmGrid is not initialized.");
                return;
            }

            int centerX = PaintFilmGrid.GridWidth / 2;
            int centerY = PaintFilmGrid.GridHeight / 2;
            int radius = Mathf.Max(2, Mathf.RoundToInt(0.02f / PaintFilmGrid.CellSize));
            int thickness = PaintCellData.ToThicknessInt(_debugStampThickness);

            var oneCell = new PaintCellData[1];

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    float normalizedDistance =
                        Mathf.Sqrt(x * x + y * y) / Mathf.Max(radius, 1);

                    if (normalizedDistance > 1.0f)
                        continue;

                    float weight = Mathf.Exp(-normalizedDistance * normalizedDistance * 4.0f);
                    int ix = centerX + x;
                    int iy = centerY + y;

                    if (ix < 0 || ix >= PaintFilmGrid.GridWidth ||
                        iy < 0 || iy >= PaintFilmGrid.GridHeight)
                    {
                        continue;
                    }

                    oneCell[0] = new PaintCellData
                    {
                        ThicknessInt = Mathf.Max(1, Mathf.RoundToInt(thickness * weight)),
                        Wetness = 1.0f,
                        Age = 0.0f,
                        IsActive = 1u,
                        Color = _debugStampColor,
                        FlowVelocity = Vector2.zero,
                        PaintDensity = 1200.0f
                    };

                    PaintFilmGrid.PaintCellBuffer.SetData(
                        oneCell,
                        0,
                        iy * PaintFilmGrid.GridWidth + ix,
                        1
                    );
                }
            }

            _renderer?.Render();
            Debug.Log("[PaintSurface] Debug stamp written to the center of the paint surface.");
        }

        public void Dispose()
        {
            PaintFilmGrid?.Dispose();
            _renderer?.Dispose();
            PaintFilmGrid = null;
            _renderer = null;
            _evolver = null;
        }

        private void OnValidate()
        {
            _gridWidth = Mathf.Max(1, _gridWidth);
            _gridHeight = Mathf.Max(1, _gridHeight);
            _cellSize = Mathf.Max(_cellSize, 0.0001f);
            _impactCaptureDistance = Mathf.Max(_impactCaptureDistance, 0.0001f);
            _evaporationRate = Mathf.Max(_evaporationRate, 0.0f);
            _diffusionRate = Mathf.Max(_diffusionRate, 0.0f);
            _runoffRate = Mathf.Max(_runoffRate, 0.0f);
            _minimumWetThickness = Mathf.Max(_minimumWetThickness, 0.000001f);
            _renderEveryNFrames = Mathf.Max(1, _renderEveryNFrames);
            _evolveEveryNFrames = Mathf.Max(1, _evolveEveryNFrames);
            SurfaceProperties = SurfaceProperties.FromType(_surfaceType);
        }

        private float EffectiveDiffusionRate()
        {
            // Existing scenes used values around 30 for the old non-conservative
            // diffusion shader. The new ping-pong spread model expects a smaller
            // normalized rate, so we preserve inspector compatibility here.
            return Mathf.Clamp(_diffusionRate * 0.02f, 0.0f, 2.0f);
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
