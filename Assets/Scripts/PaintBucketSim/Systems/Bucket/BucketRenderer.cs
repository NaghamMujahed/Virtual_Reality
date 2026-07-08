using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using System.Collections.Generic;
using PaintBucketSim.Systems.Fluid;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Bucket
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class BucketRenderer : MonoBehaviour
    {
        [SerializeField] private BucketSystem bucketSystem;
        [SerializeField] private PaintFluidSystem paintFluidSystem;

        [Header("Visual References")]
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;

        [Header("Debug Visuals")]
        [SerializeField] private bool showAttachmentJoint = true;
        [SerializeField] private bool showHoleRing = false;

        [Header("Bucket Handle")]
        [SerializeField] private bool showBailHandle = true;
        [SerializeField, Range(0.002f, 0.015f)]
        private float bailHandleRadiusMeters = 0.006f;
        [SerializeField, Range(8, 48)]
        private int bailHandleSegments = 24;

        [Header("Color Compartments")]
        [SerializeField] private bool showColorDividers = true;
        [SerializeField] private Color colorDividerColor =
            new Color(0.78f, 0.78f, 0.82f, 1.0f);
        [SerializeField, Range(0.5f, 3.0f)]
        private float dividerVisualThicknessMultiplier = 1.15f;
        [SerializeField, Range(0.25f, 1.0f)]
        private float dividerVisualHeightFraction = 0.92f;

        [Header("Contained Paint Surface")]
        [SerializeField] private bool showFluidSurface = true;
        [SerializeField, Range(24, 96)] private int fluidSurfaceSegments = 64;
        [SerializeField, Range(0.0f, 0.02f)]
        private float fluidSurfaceWallInsetMeters = 0.006f;

        private GameObject _attachmentSphere;
        private LineRenderer _jointRingRenderer;
        private LineRenderer _bailHandleRenderer;
        private LineRenderer[] _holeRingRenderers;
        private GameObject[] _colorDividerVisuals;
        private GameObject[] _bailLugObjects;
        private GameObject _bailGripObject;
        private GameObject _fluidSurfaceObject;
        private MeshFilter _fluidSurfaceMeshFilter;
        private MeshRenderer _fluidSurfaceRenderer;

        private Mesh _generatedMesh;
        private Material _runtimeMaterial;
        private Material _holeMaterial;
        private Material _jointMaterial;
        private Material _colorDividerMaterial;
        private Material _fluidSurfaceMaterial;
        private Texture2D _brushedMetalTexture;
        private Mesh _fluidSurfaceMesh;
        private float _fluidSurfaceRadius = -1.0f;
        private int _fluidSurfaceSegmentsCached;
        private int _renderedHoleCount;

        public int RenderedHoleCount => _renderedHoleCount;
        public bool FluidSurfaceVisible =>
            _fluidSurfaceObject != null &&
            _fluidSurfaceObject.activeSelf &&
            _fluidSurfaceRenderer != null &&
            _fluidSurfaceRenderer.enabled;
        public Vector3 FluidSurfaceWorldPosition =>
            _fluidSurfaceObject != null
                ? _fluidSurfaceObject.transform.position
                : Vector3.zero;
        public float FluidSurfaceRadius => _fluidSurfaceRadius;

        private void Awake()
        {
            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();

            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();

            CreateMaterials();
        }

        private void OnDestroy()
        {
            if (_generatedMesh != null)
                Destroy(_generatedMesh);
            if (_fluidSurfaceMesh != null)
                Destroy(_fluidSurfaceMesh);
            if (_brushedMetalTexture != null)
                Destroy(_brushedMetalTexture);
            DestroyRuntimeMaterial(_runtimeMaterial);
            DestroyRuntimeMaterial(_holeMaterial);
            DestroyRuntimeMaterial(_jointMaterial);
            DestroyRuntimeMaterial(_colorDividerMaterial);
            DestroyRuntimeMaterial(_fluidSurfaceMaterial);
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material != null)
                Destroy(material);
        }

        private void Start()
        {
            RebuildMeshAndDebugVisuals();
        }

        private void LateUpdate()
        {
            if (bucketSystem == null || !bucketSystem.IsInitialized)
                return;

            SyncTransformToBucket();
            UpdateAttachmentJointVisual();
            UpdateBailHandleVisual();
            UpdateBailHardwareVisuals();
            UpdateHoleVisuals();
            UpdateColorDividerVisuals();
            UpdateFluidSurfaceVisual();
        }

        public void RebuildMeshAndDebugVisuals()
        {
            if (bucketSystem == null || bucketSystem.Config == null)
                return;

            CreateBucketMesh(bucketSystem.Config);
            CreateAttachmentVisual();
            CreateBailHandleVisual();
            CreateBailHardwareVisuals();
            CreateHoleVisuals();
            CreateColorDividerVisuals();
            CreateFluidSurfaceVisual();
        }

        private void SyncTransformToBucket()
        {
            BucketState state = bucketSystem.State;

            transform.position = ToVector3(state.position);
            transform.rotation = ToQuaternion(state.rotation);
        }

        private void CreateMaterials()
        {
            _runtimeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _holeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _jointMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _colorDividerMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Shader fluidSurfaceShader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            _fluidSurfaceMaterial = new Material(fluidSurfaceShader);
            ConfigureBucketMaterial(_runtimeMaterial);
            ConfigureMetalMaterial(_jointMaterial, 0.72f, 0.31f);
            ConfigureMetalMaterial(_holeMaterial, 0.15f, 0.22f);
            SetMaterialColor(_colorDividerMaterial, colorDividerColor);
            ConfigureMetalMaterial(_fluidSurfaceMaterial, 0.0f, 0.72f);

            _brushedMetalTexture = CreateBrushedMetalTexture();
            if (_runtimeMaterial != null &&
                _brushedMetalTexture != null &&
                _runtimeMaterial.HasProperty("_BaseMap"))
            {
                _runtimeMaterial.SetTexture("_BaseMap", _brushedMetalTexture);
                _runtimeMaterial.SetTextureScale("_BaseMap", new Vector2(4.0f, 3.0f));
            }
        }

        private void CreateBucketMesh(BucketConfig config)
        {
            if (_generatedMesh != null)
            {
                Destroy(_generatedMesh);
                _generatedMesh = null;
            }

            _generatedMesh = GenerateBucketMesh(config);
            meshFilter.sharedMesh = _generatedMesh;

            meshRenderer.enabled = true;

            if (_runtimeMaterial != null)
            {
                SetMaterialColor(_runtimeMaterial, config.bucketColor);
                meshRenderer.sharedMaterials = _holeMaterial != null
                    ? new[] { _runtimeMaterial, _holeMaterial }
                    : new[] { _runtimeMaterial, _runtimeMaterial };
            }
        }

        private Mesh GenerateBucketMesh(BucketConfig config)
        {
            int radialSegments = Mathf.Max(8, config.visualRadialSegments);

            float height = config.heightMeters;
            float topRadius = config.topRadiusMeters;
            float bottomRadius = config.shapeType == BucketShapeType.Cylinder
                ? config.topRadiusMeters
                : config.bottomRadiusMeters;
            float wall = Mathf.Clamp(
                config.wallThicknessMeters,
                0.003f,
                Mathf.Min(topRadius, bottomRadius) * 0.45f
            );

            float halfHeight = height * 0.5f;
            float bottomY = -halfHeight;
            float topY = halfHeight;
            float innerBottomY = bottomY + wall;

            float innerTopRadius = Mathf.Max(topRadius - wall, 0.01f);
            float innerBottomRadius = Mathf.Max(bottomRadius - wall, 0.01f);
            float rimDrop = Mathf.Max(wall * 1.7f, 0.012f);
            float rimBottomY = topY - rimDrop;
            float rimOuterRadius = topRadius + wall * 0.65f;
            float footHeight = Mathf.Max(wall * 1.8f, 0.012f);
            float footBottomY = bottomY + wall * 0.28f;
            float footTopY = Mathf.Min(bottomY + footHeight, rimBottomY - 0.01f);
            float footOuterRadius = bottomRadius + wall * 0.42f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var holeTunnelTriangles = new List<int>();

            int outerWallBottom = AddRing(vertices, normals, uvs, radialSegments, bottomRadius, bottomY, RingNormal.RadialOut, 0.0f);
            int outerWallTop = AddRing(vertices, normals, uvs, radialSegments, topRadius, rimBottomY, RingNormal.RadialOut, 0.78f);
            AddStrip(triangles, outerWallBottom, outerWallTop, radialSegments, false);

            int footInnerBottom = AddRing(vertices, normals, uvs, radialSegments, bottomRadius, footBottomY, RingNormal.Down, 0.0f);
            int footOuterBottom = AddRing(vertices, normals, uvs, radialSegments, footOuterRadius, footBottomY, RingNormal.Down, 0.0f);
            AddStrip(triangles, footInnerBottom, footOuterBottom, radialSegments, false);

            int footSideBottom = AddRing(vertices, normals, uvs, radialSegments, footOuterRadius, footBottomY, RingNormal.RadialOut, 0.0f);
            int footSideTop = AddRing(vertices, normals, uvs, radialSegments, footOuterRadius, footTopY, RingNormal.RadialOut, 0.08f);
            AddStrip(triangles, footSideBottom, footSideTop, radialSegments, false);

            int footTopOuter = AddRing(vertices, normals, uvs, radialSegments, footOuterRadius, footTopY, RingNormal.Up, 0.0f);
            int footTopInner = AddRing(vertices, normals, uvs, radialSegments, bottomRadius, footTopY, RingNormal.Up, 0.0f);
            AddStrip(triangles, footTopOuter, footTopInner, radialSegments, false);

            int rimLowerInner = AddRing(vertices, normals, uvs, radialSegments, topRadius, rimBottomY, RingNormal.Down, 0.0f);
            int rimLowerOuter = AddRing(vertices, normals, uvs, radialSegments, rimOuterRadius, rimBottomY, RingNormal.Down, 0.0f);
            AddStrip(triangles, rimLowerInner, rimLowerOuter, radialSegments, false);

            int rimSideBottom = AddRing(vertices, normals, uvs, radialSegments, rimOuterRadius, rimBottomY, RingNormal.RadialOut, 0.78f);
            int rimSideTop = AddRing(vertices, normals, uvs, radialSegments, rimOuterRadius, topY, RingNormal.RadialOut, 1.0f);
            AddStrip(triangles, rimSideBottom, rimSideTop, radialSegments, false);

            int rimCapOuter = AddRing(vertices, normals, uvs, radialSegments, rimOuterRadius, topY, RingNormal.Up, 1.0f);
            int rimCapInner = AddRing(vertices, normals, uvs, radialSegments, innerTopRadius, topY, RingNormal.Up, 1.0f);
            AddStrip(triangles, rimCapOuter, rimCapInner, radialSegments, false);

            int innerWallTop = AddRing(vertices, normals, uvs, radialSegments, innerTopRadius, topY - wall * 0.2f, RingNormal.RadialIn, 1.0f);
            int innerWallBottom = AddRing(vertices, normals, uvs, radialSegments, innerBottomRadius, innerBottomY, RingNormal.RadialIn, 0.0f);
            AddStrip(triangles, innerWallTop, innerWallBottom, radialSegments, false);

            int plateResolution = Mathf.Clamp(radialSegments * 3, 72, 160);
            AddPerforatedPlate(
                vertices,
                normals,
                uvs,
                triangles,
                config,
                innerBottomRadius,
                innerBottomY,
                true,
                plateResolution);
            AddPerforatedPlate(
                vertices,
                normals,
                uvs,
                triangles,
                config,
                bottomRadius,
                bottomY,
                false,
                plateResolution);
            AddHoleTunnels(
                vertices,
                normals,
                uvs,
                holeTunnelTriangles,
                triangles,
                config,
                bottomY,
                innerBottomY,
                Mathf.Max(
                    bottomRadius * 2.0f / plateResolution,
                    0.0015f));

            _renderedHoleCount = CountRenderableHoles(config);

            Mesh mesh = new Mesh
            {
                name = "Generated Thick Bucket Mesh"
            };

            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles, 0);
            mesh.SetTriangles(holeTunnelTriangles, 1);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static int CountRenderableHoles(BucketConfig config)
        {
            if (config == null || config.holes == null)
                return 0;

            int count = 0;
            for (int i = 0; i < config.holes.Length; i++)
            {
                BucketHoleConfig hole = config.holes[i];
                if (hole == null || !hole.active)
                    continue;

                Vector3 normal = config.GetResolvedHoleLocalNormal(hole);
                if (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) >= 0.8f)
                    count++;
            }

            return count;
        }

        private void CreateFluidSurfaceVisual()
        {
            if (!showFluidSurface)
                return;

            if (_fluidSurfaceObject == null)
            {
                _fluidSurfaceObject = new GameObject("Contained Paint Surface");
                _fluidSurfaceObject.transform.SetParent(transform, false);
                _fluidSurfaceMeshFilter =
                    _fluidSurfaceObject.AddComponent<MeshFilter>();
                _fluidSurfaceRenderer =
                    _fluidSurfaceObject.AddComponent<MeshRenderer>();
                _fluidSurfaceRenderer.sharedMaterial = _fluidSurfaceMaterial;
            }

            UpdateFluidSurfaceVisual();
        }

        private void UpdateFluidSurfaceVisual()
        {
            if (!showFluidSurface ||
                bucketSystem == null ||
                bucketSystem.Config == null ||
                paintFluidSystem == null ||
                !paintFluidSystem.IsInitialized)
            {
                if (_fluidSurfaceObject != null)
                    _fluidSurfaceObject.SetActive(false);
                return;
            }

            if (_fluidSurfaceObject == null)
            {
                CreateFluidSurfaceVisual();
                return;
            }

            BucketConfig bucketConfig = bucketSystem.Config;
            float fillFraction = paintFluidSystem.FluidConfig != null
                ? Mathf.Clamp01(paintFluidSystem.FluidConfig.fillFraction01)
                : 0.5f;
            FluidSolverStats stats = paintFluidSystem.SolverStats;
            if (paintFluidSystem.IsGpuSolverActive &&
                stats.gpuDiagnosticsReady &&
                stats.gpuMaximumFillHeight01 > 0.001f)
            {
                fillFraction = Mathf.Clamp01(stats.gpuMaximumFillHeight01);
            }

            float bottomRadius = bucketConfig.shapeType == BucketShapeType.Cylinder
                ? bucketConfig.topRadiusMeters
                : bucketConfig.bottomRadiusMeters;
            float radius = Mathf.Lerp(
                bottomRadius,
                bucketConfig.topRadiusMeters,
                fillFraction);
            radius = Mathf.Max(
                radius -
                bucketConfig.wallThicknessMeters -
                fluidSurfaceWallInsetMeters,
                0.01f);

            int segments = Mathf.Clamp(fluidSurfaceSegments, 24, 96);
            if (_fluidSurfaceMesh == null ||
                _fluidSurfaceSegmentsCached != segments)
            {
                if (_fluidSurfaceMesh != null)
                    Destroy(_fluidSurfaceMesh);
                _fluidSurfaceMesh = BuildFluidSurfaceMesh(radius, segments);
                _fluidSurfaceMeshFilter.sharedMesh = _fluidSurfaceMesh;
                _fluidSurfaceSegmentsCached = segments;
                _fluidSurfaceRadius = radius;
            }
            else if (Mathf.Abs(_fluidSurfaceRadius - radius) > 0.001f)
            {
                UpdateFluidSurfaceMeshRadius(_fluidSurfaceMesh, radius, segments);
                _fluidSurfaceRadius = radius;
            }

            float localY =
                -bucketConfig.heightMeters * 0.5f +
                fillFraction * bucketConfig.heightMeters;
            BucketDiagnostics diagnostics = bucketSystem.Diagnostics;
            Vector3 localCenter = new Vector3(0.0f, localY, 0.0f);
            if (diagnostics.containedFluidMass > 1e-5f)
            {
                float scale =
                    diagnostics.totalMass /
                    diagnostics.containedFluidMass;
                localCenter.x = Mathf.Clamp(
                    diagnostics.centerOfMassLocal.x * scale,
                    -radius * 0.25f,
                    radius * 0.25f);
                localCenter.z = Mathf.Clamp(
                    diagnostics.centerOfMassLocal.z * scale,
                    -radius * 0.25f,
                    radius * 0.25f);
            }

            _fluidSurfaceObject.SetActive(true);
            _fluidSurfaceRenderer.enabled = true;
            _fluidSurfaceObject.transform.position =
                transform.TransformPoint(localCenter);
            _fluidSurfaceObject.transform.rotation = Quaternion.identity;

            if (_fluidSurfaceMaterial != null)
            {
                Color color = paintFluidSystem.MaterialConfig != null
                    ? paintFluidSystem.MaterialConfig.baseColor
                    : new Color(0.1f, 0.35f, 1.0f, 1.0f);
                color.a = 1.0f;
                SetMaterialColor(_fluidSurfaceMaterial, color);
                if (_fluidSurfaceMaterial.HasProperty("_EmissionColor"))
                {
                    _fluidSurfaceMaterial.EnableKeyword("_EMISSION");
                    _fluidSurfaceMaterial.SetColor(
                        "_EmissionColor",
                        color * 0.28f);
                }
            }
        }

        private static Mesh BuildFluidSurfaceMesh(float radius, int segments)
        {
            Mesh mesh = new Mesh
            {
                name = "Contained Paint Surface Mesh"
            };
            UpdateFluidSurfaceMeshRadius(mesh, radius, segments);
            return mesh;
        }

        private static void UpdateFluidSurfaceMeshRadius(
            Mesh mesh,
            float radius,
            int segments)
        {
            Vector3[] vertices = new Vector3[segments + 1];
            Vector3[] normals = new Vector3[segments + 1];
            Vector2[] uvs = new Vector2[segments + 1];
            int[] triangles = new int[segments * 6];

            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2.0f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices[i + 1] = new Vector3(cos * radius, 0.0f, sin * radius);
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);

                int next = (i + 1) % segments;
                int tri = i * 6;
                triangles[tri] = 0;
                triangles[tri + 1] = next + 1;
                triangles[tri + 2] = i + 1;
                triangles[tri + 3] = 0;
                triangles[tri + 4] = i + 1;
                triangles[tri + 5] = next + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private static void AddPerforatedPlate(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            BucketConfig config,
            float radius,
            float y,
            bool faceUp,
            int resolution)
        {
            int[,] indices = new int[resolution + 1, resolution + 1];
            for (int x = 0; x <= resolution; x++)
            {
                for (int z = 0; z <= resolution; z++)
                    indices[x, z] = -1;
            }

            for (int x = 0; x <= resolution; x++)
            {
                float tx = x / (float)resolution;
                float px = Mathf.Lerp(-radius, radius, tx);

                for (int z = 0; z <= resolution; z++)
                {
                    float tz = z / (float)resolution;
                    float pz = Mathf.Lerp(-radius, radius, tz);
                    Vector3 point = new Vector3(px, y, pz);

                    if (px * px + pz * pz > radius * radius ||
                        IsInsideActiveBottomHole(config, point, 0.0f))
                    {
                        continue;
                    }

                    indices[x, z] = vertices.Count;
                    vertices.Add(point);
                    normals.Add(faceUp ? Vector3.up : Vector3.down);
                    uvs.Add(new Vector2(tx, tz));
                }
            }

            for (int x = 0; x < resolution; x++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    int p00 = indices[x, z];
                    int p10 = indices[x + 1, z];
                    int p01 = indices[x, z + 1];
                    int p11 = indices[x + 1, z + 1];

                    AddPlateTriangle(triangles, p00, p01, p10, faceUp);
                    AddPlateTriangle(triangles, p10, p01, p11, faceUp);
                }
            }
        }

        private static void AddPlateTriangle(
            List<int> triangles,
            int a,
            int b,
            int c,
            bool faceUp)
        {
            if (a < 0 || b < 0 || c < 0)
                return;

            if (faceUp)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
            else
            {
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
            }
        }

        private static bool IsInsideActiveBottomHole(
            BucketConfig config,
            Vector3 point,
            float expansion)
        {
            if (config == null || config.holes == null)
                return false;

            for (int i = 0; i < config.holes.Length; i++)
            {
                BucketHoleConfig hole = config.holes[i];
                if (hole == null || !hole.active)
                    continue;

                Vector3 normal = config.GetResolvedHoleLocalNormal(hole);
                if (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.8f)
                    continue;

                Vector3 center = config.GetResolvedHoleLocalCenter(hole);
                Vector3 tangent = config.GetResolvedHoleLocalTangent(hole);
                Vector3 bitangent = config.GetResolvedHoleLocalBitangent(hole);
                Vector3 delta = point - center;
                float x = Vector3.Dot(delta, tangent);
                float z = Vector3.Dot(delta, bitangent);
                Vector2 extents = config.GetResolvedHoleHalfExtents(hole);
                float a = Mathf.Max(extents.x + expansion, 0.001f);
                float b = Mathf.Max(extents.y + expansion, 0.001f);

                switch (hole.shape)
                {
                    case BucketHoleShape.Square:
                    case BucketHoleShape.Rectangle:
                        if (Mathf.Abs(x) <= a && Mathf.Abs(z) <= b)
                            return true;
                        break;

                    case BucketHoleShape.Slot:
                    {
                        bool alongX = a >= b;
                        float radius = Mathf.Min(a, b);
                        float straightHalf = Mathf.Max(Mathf.Max(a, b) - radius, 0.0f);
                        float major = alongX ? x : z;
                        float minor = alongX ? z : x;
                        float capDistance = Mathf.Max(Mathf.Abs(major) - straightHalf, 0.0f);
                        if (capDistance * capDistance + minor * minor <= radius * radius)
                            return true;
                        break;
                    }

                    case BucketHoleShape.Ellipse:
                    case BucketHoleShape.Circular:
                    default:
                        if (x * x / (a * a) + z * z / (b * b) <= 1.0f)
                            return true;
                        break;
                }
            }

            return false;
        }

        private static void AddHoleTunnels(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> tunnelTriangles,
            List<int> bodyTriangles,
            BucketConfig config,
            float outerY,
            float innerY,
            float trimWidth)
        {
            if (config == null || config.holes == null)
                return;

            for (int holeIndex = 0; holeIndex < config.holes.Length; holeIndex++)
            {
                BucketHoleConfig hole = config.holes[holeIndex];
                if (hole == null || !hole.active)
                    continue;

                Vector3 normal = config.GetResolvedHoleLocalNormal(hole);
                if (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.8f)
                    continue;

                int contourSegments =
                    hole.shape == BucketHoleShape.Square ||
                    hole.shape == BucketHoleShape.Rectangle
                        ? 4
                        : 32;
                Vector3 center = config.GetResolvedHoleLocalCenter(hole);
                Vector3 tangent = config.GetResolvedHoleLocalTangent(hole);
                Vector3 bitangent = config.GetResolvedHoleLocalBitangent(hole);

                int bottomStart = vertices.Count;
                for (int i = 0; i < contourSegments; i++)
                {
                    Vector2 contour = GetHoleContourPoint(
                        config,
                        hole,
                        i / (float)contourSegments,
                        0.0f);
                    Vector3 offset = tangent * contour.x + bitangent * contour.y;
                    Vector3 inward = -offset.normalized;
                    vertices.Add(new Vector3(
                        center.x + offset.x,
                        outerY,
                        center.z + offset.z));
                    normals.Add(inward);
                    uvs.Add(new Vector2(i / (float)contourSegments, 0.0f));
                }

                int topStart = vertices.Count;
                for (int i = 0; i < contourSegments; i++)
                {
                    Vector2 contour = GetHoleContourPoint(
                        config,
                        hole,
                        i / (float)contourSegments,
                        0.0f);
                    Vector3 offset = tangent * contour.x + bitangent * contour.y;
                    Vector3 inward = -offset.normalized;
                    vertices.Add(new Vector3(
                        center.x + offset.x,
                        innerY,
                        center.z + offset.z));
                    normals.Add(inward);
                    uvs.Add(new Vector2(i / (float)contourSegments, 1.0f));
                }

                for (int i = 0; i < contourSegments; i++)
                {
                    int next = (i + 1) % contourSegments;
                    int b0 = bottomStart + i;
                    int b1 = bottomStart + next;
                    int t0 = topStart + i;
                    int t1 = topStart + next;

                    tunnelTriangles.Add(b0);
                    tunnelTriangles.Add(b1);
                    tunnelTriangles.Add(t0);
                    tunnelTriangles.Add(b1);
                    tunnelTriangles.Add(t1);
                    tunnelTriangles.Add(t0);
                }

                AddHoleTrim(
                    vertices,
                    normals,
                    uvs,
                    bodyTriangles,
                    config,
                    hole,
                    center,
                    tangent,
                    bitangent,
                    innerY + 0.0002f,
                    trimWidth,
                    contourSegments,
                    true);
                AddHoleTrim(
                    vertices,
                    normals,
                    uvs,
                    bodyTriangles,
                    config,
                    hole,
                    center,
                    tangent,
                    bitangent,
                    outerY - 0.0002f,
                    trimWidth,
                    contourSegments,
                    false);
            }
        }

        private static void AddHoleTrim(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            BucketConfig config,
            BucketHoleConfig hole,
            Vector3 center,
            Vector3 tangent,
            Vector3 bitangent,
            float y,
            float width,
            int segments,
            bool faceUp)
        {
            int innerStart = vertices.Count;
            for (int ring = 0; ring < 2; ring++)
            {
                float expansion = ring == 0 ? 0.0f : width;
                for (int i = 0; i < segments; i++)
                {
                    Vector2 contour = GetHoleContourPoint(
                        config,
                        hole,
                        i / (float)segments,
                        expansion);
                    Vector3 offset = tangent * contour.x + bitangent * contour.y;
                    vertices.Add(new Vector3(
                        center.x + offset.x,
                        y,
                        center.z + offset.z));
                    normals.Add(faceUp ? Vector3.up : Vector3.down);
                    uvs.Add(new Vector2(
                        i / (float)segments,
                        ring));
                }
            }

            int outerStart = innerStart + segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int i0 = innerStart + i;
                int i1 = innerStart + next;
                int o0 = outerStart + i;
                int o1 = outerStart + next;

                if (faceUp)
                {
                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(o0);
                    triangles.Add(i1);
                    triangles.Add(o1);
                    triangles.Add(o0);
                }
                else
                {
                    triangles.Add(i0);
                    triangles.Add(o0);
                    triangles.Add(i1);
                    triangles.Add(i1);
                    triangles.Add(o0);
                    triangles.Add(o1);
                }
            }
        }

        private static Vector2 GetHoleContourPoint(
            BucketConfig config,
            BucketHoleConfig hole,
            float t,
            float expansion)
        {
            Vector2 extents = config.GetResolvedHoleHalfExtents(hole);
            float a = Mathf.Max(extents.x + expansion, 0.001f);
            float b = Mathf.Max(extents.y + expansion, 0.001f);

            if (hole.shape == BucketHoleShape.Square ||
                hole.shape == BucketHoleShape.Rectangle)
            {
                int edge = Mathf.FloorToInt(t * 4.0f) % 4;
                switch (edge)
                {
                    case 0: return new Vector2(a, b);
                    case 1: return new Vector2(-a, b);
                    case 2: return new Vector2(-a, -b);
                    default: return new Vector2(a, -b);
                }
            }

            float angle = t * Mathf.PI * 2.0f;
            if (hole.shape == BucketHoleShape.Slot)
            {
                bool alongX = a >= b;
                float radius = Mathf.Min(a, b);
                float straightHalf = Mathf.Max(Mathf.Max(a, b) - radius, 0.0f);
                float major = Mathf.Cos(angle) * radius +
                    Mathf.Sign(Mathf.Cos(angle)) * straightHalf;
                float minor = Mathf.Sin(angle) * radius;
                return alongX
                    ? new Vector2(major, minor)
                    : new Vector2(minor, major);
            }

            return new Vector2(
                Mathf.Cos(angle) * a,
                Mathf.Sin(angle) * b);
        }

        private enum RingNormal
        {
            RadialOut,
            RadialIn,
            Up,
            Down
        }

        private static int AddRing(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            int radialSegments,
            float radius,
            float y,
            RingNormal normalMode,
            float uvY)
        {
            int start = vertices.Count;

            for (int i = 0; i <= radialSegments; i++)
            {
                float t = i / (float)radialSegments;
                float angle = t * Mathf.PI * 2.0f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector3 radial = new Vector3(cos, 0.0f, sin);

                vertices.Add(new Vector3(cos * radius, y, sin * radius));
                normals.Add(GetRingNormal(radial, normalMode));
                uvs.Add(new Vector2(t, uvY));
            }

            return start;
        }

        private static Vector3 GetRingNormal(Vector3 radial, RingNormal mode)
        {
            switch (mode)
            {
                case RingNormal.RadialIn:
                    return -radial;

                case RingNormal.Up:
                    return Vector3.up;

                case RingNormal.Down:
                    return Vector3.down;

                case RingNormal.RadialOut:
                default:
                    return radial;
            }
        }

        private static void AddStrip(
            List<int> triangles,
            int ringA,
            int ringB,
            int radialSegments,
            bool flip)
        {
            for (int i = 0; i < radialSegments; i++)
            {
                int a0 = ringA + i;
                int a1 = ringA + i + 1;
                int b0 = ringB + i;
                int b1 = ringB + i + 1;

                if (!flip)
                {
                    triangles.Add(a0);
                    triangles.Add(b0);
                    triangles.Add(a1);
                    triangles.Add(a1);
                    triangles.Add(b0);
                    triangles.Add(b1);
                }
                else
                {
                    triangles.Add(a0);
                    triangles.Add(a1);
                    triangles.Add(b0);
                    triangles.Add(a1);
                    triangles.Add(b1);
                    triangles.Add(b0);
                }
            }
        }

        private static void AddDisk(
            List<int> triangles,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            int ringStart,
            int radialSegments,
            float y,
            bool faceUp)
        {
            int center = vertices.Count;
            vertices.Add(new Vector3(0.0f, y, 0.0f));
            normals.Add(faceUp ? Vector3.up : Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < radialSegments; i++)
            {
                int r0 = ringStart + i;
                int r1 = ringStart + i + 1;

                if (faceUp)
                {
                    triangles.Add(center);
                    triangles.Add(r1);
                    triangles.Add(r0);
                }
                else
                {
                    triangles.Add(center);
                    triangles.Add(r0);
                    triangles.Add(r1);
                }
            }
        }

        private void CreateAttachmentVisual()
        {
            if (!showAttachmentJoint || bucketSystem == null || bucketSystem.Config == null)
                return;

            BucketConfig config = bucketSystem.Config;

            if (_attachmentSphere == null)
            {
                _attachmentSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _attachmentSphere.name = "Bucket Attachment Joint";
                _attachmentSphere.transform.SetParent(transform, false);

                if (_jointMaterial != null)
                {
                    SetMaterialColor(_jointMaterial, config.jointColor);
                    _attachmentSphere.GetComponent<MeshRenderer>().sharedMaterial = _jointMaterial;
                }
            }

            Vector3 localPoint = bucketSystem.IsInitialized
                ? bucketSystem.GetAttachmentLocalPosition()
                : config.GetResolvedAttachmentLocalPoint();

            _attachmentSphere.transform.localPosition = localPoint;
            float size = config.jointVisualRadiusMeters * 2.0f;
            _attachmentSphere.transform.localScale = new Vector3(size, size, size);

            if (_jointRingRenderer == null)
            {
                GameObject ring = new GameObject("Bucket Attachment Joint Ring");
                ring.transform.SetParent(transform, false);

                _jointRingRenderer = ring.AddComponent<LineRenderer>();
                _jointRingRenderer.useWorldSpace = false;
                _jointRingRenderer.loop = true;
                _jointRingRenderer.positionCount = 48;
                _jointRingRenderer.startWidth = 0.008f;
                _jointRingRenderer.endWidth = 0.008f;
                _jointRingRenderer.material = _jointMaterial;
            }

            _jointRingRenderer.transform.localPosition = localPoint;
            SetLocalCircle(_jointRingRenderer, config.jointVisualRadiusMeters, Vector3.up);
        }

        private void CreateBailHandleVisual()
        {
            if (!showBailHandle || bucketSystem == null || bucketSystem.Config == null)
                return;

            if (_bailHandleRenderer == null)
            {
                GameObject handle = new GameObject("Bucket Bail Handle");
                handle.transform.SetParent(transform, false);

                _bailHandleRenderer = handle.AddComponent<LineRenderer>();
                _bailHandleRenderer.useWorldSpace = false;
                _bailHandleRenderer.loop = false;
                _bailHandleRenderer.material = _jointMaterial;
                _bailHandleRenderer.numCapVertices = 4;
                _bailHandleRenderer.numCornerVertices = 4;
            }

            CreateBailHardwareVisuals();
            UpdateBailHandleVisual();
        }

        private void UpdateBailHandleVisual()
        {
            if (!showBailHandle || bucketSystem == null || bucketSystem.Config == null)
            {
                if (_bailHandleRenderer != null)
                    _bailHandleRenderer.positionCount = 0;
                return;
            }

            if (_bailHandleRenderer == null)
                CreateBailHandleVisual();

            if (_bailHandleRenderer == null)
                return;

            BucketConfig config = bucketSystem.Config;
            int count = Mathf.Max(8, bailHandleSegments);

            _bailHandleRenderer.positionCount = count + 1;
            _bailHandleRenderer.startWidth = bailHandleRadiusMeters * 2.0f;
            _bailHandleRenderer.endWidth = bailHandleRadiusMeters * 2.0f;

            float halfWidth = config.topRadiusMeters + config.wallThicknessMeters * 1.8f;
            Vector3 hingeCenter = config.GetBailHingeCenterLocal();
            float handleRadius = config.GetBailRadiusMeters();
            float hingeAngle = bucketSystem.BailHingeAngleRadians;
            Quaternion hingeRotation = Quaternion.AngleAxis(
                hingeAngle * Mathf.Rad2Deg,
                Vector3.right);

            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                float angle = Mathf.Lerp(Mathf.PI, 0.0f, t);
                float x = Mathf.Cos(angle) * halfWidth;
                float arch = Mathf.Sin(angle);
                Vector3 relative = new Vector3(
                    x,
                    arch * handleRadius,
                    0.0f);

                _bailHandleRenderer.SetPosition(
                    i,
                    hingeCenter + hingeRotation * relative);
            }
        }

        private void CreateBailHardwareVisuals()
        {
            if (!showBailHandle || bucketSystem == null || bucketSystem.Config == null)
                return;

            BucketConfig config = bucketSystem.Config;
            if (_bailLugObjects == null || _bailLugObjects.Length != 2)
                _bailLugObjects = new GameObject[2];

            for (int i = 0; i < 2; i++)
            {
                if (_bailLugObjects[i] == null)
                {
                    GameObject lug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    lug.name = i == 0 ? "Bucket Bail Lug Left" : "Bucket Bail Lug Right";
                    lug.transform.SetParent(transform, false);
                    RemoveCollider(lug);
                    MeshRenderer renderer = lug.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        renderer.sharedMaterial = _jointMaterial;
                    _bailLugObjects[i] = lug;
                }
            }

            if (_bailGripObject == null)
            {
                _bailGripObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _bailGripObject.name = "Bucket Bail Grip";
                _bailGripObject.transform.SetParent(transform, false);
                RemoveCollider(_bailGripObject);
                MeshRenderer renderer = _bailGripObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = _jointMaterial;
            }

            UpdateBailHardwareVisuals();
        }

        private void UpdateBailHardwareVisuals()
        {
            if (!showBailHandle || bucketSystem == null || bucketSystem.Config == null)
                return;

            if (_bailLugObjects == null || _bailGripObject == null)
            {
                CreateBailHardwareVisuals();
                return;
            }

            BucketConfig config = bucketSystem.Config;
            Vector3 hinge = config.GetBailHingeCenterLocal();
            float lugX =
                config.topRadiusMeters +
                config.wallThicknessMeters * 0.75f;
            float lugDiameter = config.bailLugRadiusMeters * 2.0f;
            float lugHalfDepth = config.bailLugDepthMeters * 0.5f;

            for (int i = 0; i < _bailLugObjects.Length; i++)
            {
                GameObject lug = _bailLugObjects[i];
                if (lug == null)
                    continue;

                float sign = i == 0 ? -1.0f : 1.0f;
                lug.transform.localPosition =
                    hinge + new Vector3(sign * lugX, 0.0f, 0.0f);
                lug.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
                lug.transform.localScale = new Vector3(
                    lugDiameter,
                    lugHalfDepth,
                    lugDiameter);
            }

            if (_bailGripObject != null)
            {
                Vector3 apex = bucketSystem.IsInitialized
                    ? bucketSystem.GetAttachmentLocalPosition()
                    : config.GetResolvedAttachmentLocalPoint();
                float gripDiameter = bailHandleRadiusMeters * 2.7f;
                _bailGripObject.transform.localPosition = apex;
                _bailGripObject.transform.localRotation =
                    Quaternion.Euler(0.0f, 0.0f, 90.0f);
                _bailGripObject.transform.localScale = new Vector3(
                    gripDiameter,
                    config.bailGripLengthMeters * 0.5f,
                    gripDiameter);
            }
        }

        private static void RemoveCollider(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }

        private void CreateHoleVisuals()
        {
            if (!showHoleRing || bucketSystem == null || bucketSystem.Config == null)
                return;

            BucketConfig config = bucketSystem.Config;
            int count = config.holes != null ? config.holes.Length : 0;

            if (_holeRingRenderers != null)
            {
                for (int i = 0; i < _holeRingRenderers.Length; i++)
                {
                    if (_holeRingRenderers[i] != null)
                        Destroy(_holeRingRenderers[i].gameObject);
                }
            }

            _holeRingRenderers = new LineRenderer[count];

            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject($"Hole Ring {i}");
                go.transform.SetParent(transform, false);

                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = 48;
                lr.startWidth = 0.006f;
                lr.endWidth = 0.006f;
                lr.material = _holeMaterial;

                if (_holeMaterial != null)
                    SetMaterialColor(_holeMaterial, config.holeColor);

                _holeRingRenderers[i] = lr;
            }
        }

        private void CreateColorDividerVisuals()
        {
            DestroyColorDividerVisuals();

            if (!TryGetColorDividerConfig(out PaintFluidConfig fluidConfig) ||
                bucketSystem == null ||
                bucketSystem.Config == null)
            {
                return;
            }

            int dividerCount = Mathf.Clamp(fluidConfig.colorCompartmentCount - 1, 0, 15);
            if (dividerCount <= 0)
                return;

            _colorDividerVisuals = new GameObject[dividerCount];

            for (int i = 0; i < dividerCount; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Bucket Color Divider {i + 1}";
                go.transform.SetParent(transform, false);

                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null && _colorDividerMaterial != null)
                    renderer.sharedMaterial = _colorDividerMaterial;

                _colorDividerVisuals[i] = go;
            }

            UpdateColorDividerVisuals();
        }

        private void DestroyColorDividerVisuals()
        {
            if (_colorDividerVisuals == null)
                return;

            for (int i = 0; i < _colorDividerVisuals.Length; i++)
            {
                if (_colorDividerVisuals[i] != null)
                    Destroy(_colorDividerVisuals[i]);
            }

            _colorDividerVisuals = null;
        }

        private void UpdateAttachmentJointVisual()
        {
            if (!showAttachmentJoint || bucketSystem.Config == null)
                return;

            if (_attachmentSphere == null)
                CreateAttachmentVisual();

            Vector3 localPoint = bucketSystem.IsInitialized
                ? bucketSystem.GetAttachmentLocalPosition()
                : bucketSystem.Config.GetResolvedAttachmentLocalPoint();

            if (_attachmentSphere != null)
                _attachmentSphere.transform.localPosition = localPoint;

            if (_jointRingRenderer != null)
                _jointRingRenderer.transform.localPosition = localPoint;
        }

        private void UpdateHoleVisuals()
        {
            if (!showHoleRing || bucketSystem.Config == null || _holeRingRenderers == null)
                return;

            BucketConfig config = bucketSystem.Config;

            for (int i = 0; i < _holeRingRenderers.Length; i++)
            {
                if (_holeRingRenderers[i] == null || i >= config.holes.Length)
                    continue;

                BucketHoleConfig hole = config.holes[i];

                Vector3 localCenter = config.GetResolvedHoleLocalCenter(hole);
                Vector3 localNormal = config.GetResolvedHoleLocalNormal(hole);
                Vector3 localBitangent = config.GetResolvedHoleLocalBitangent(hole);

                _holeRingRenderers[i].transform.localPosition = localCenter;
                _holeRingRenderers[i].transform.localRotation =
                    Quaternion.LookRotation(localBitangent, localNormal);

                Color ringColor = hole.active
                    ? config.holeColor
                    : new Color(0.32f, 0.32f, 0.34f, 0.65f);
                _holeRingRenderers[i].startColor = ringColor;
                _holeRingRenderers[i].endColor = ringColor;
                _holeRingRenderers[i].startWidth = hole.active ? 0.006f : 0.0035f;
                _holeRingRenderers[i].endWidth = hole.active ? 0.006f : 0.0035f;

                SetLocalHoleShape(_holeRingRenderers[i], config, hole);
            }
        }

        private void UpdateColorDividerVisuals()
        {
            if (!TryGetColorDividerConfig(out PaintFluidConfig fluidConfig) ||
                bucketSystem == null ||
                bucketSystem.Config == null)
            {
                if (_colorDividerVisuals != null)
                    DestroyColorDividerVisuals();
                return;
            }

            int dividerCount = Mathf.Clamp(fluidConfig.colorCompartmentCount - 1, 0, 15);
            if (_colorDividerVisuals == null || _colorDividerVisuals.Length != dividerCount)
            {
                CreateColorDividerVisuals();
                return;
            }

            BucketConfig bucketConfig = bucketSystem.Config;
            float bottomRadius = bucketConfig.shapeType == BucketShapeType.Cylinder
                ? bucketConfig.topRadiusMeters
                : bucketConfig.bottomRadiusMeters;
            float topRadius = bucketConfig.topRadiusMeters;
            float visualRadius =
                Mathf.Max(
                    Mathf.Min(bottomRadius, topRadius) -
                    bucketConfig.wallThicknessMeters -
                    0.006f,
                    0.01f
                );
            float visualDiameter = visualRadius * 2.0f;
            float height =
                Mathf.Max(bucketConfig.heightMeters * dividerVisualHeightFraction, 0.02f);
            float thickness =
                Mathf.Max(
                    fluidConfig.colorDividerThicknessMeters,
                    bucketConfig.wallThicknessMeters * 0.65f
                ) * Mathf.Max(dividerVisualThicknessMultiplier, 0.1f);

            if (_colorDividerMaterial != null)
                SetMaterialColor(_colorDividerMaterial, colorDividerColor);

            for (int i = 0; i < dividerCount; i++)
            {
                GameObject divider = _colorDividerVisuals[i];
                if (divider == null)
                    continue;

                int boundary = i + 1;
                float t = boundary / (float)Mathf.Max(fluidConfig.colorCompartmentCount, 1);

                switch (fluidConfig.colorCompartmentAxis)
                {
                    case FluidColorCompartmentAxis.BucketLocalZ:
                    {
                        float z = Mathf.Lerp(-visualRadius, visualRadius, t);
                        divider.transform.localPosition = new Vector3(0.0f, 0.0f, z);
                        divider.transform.localRotation = Quaternion.identity;
                        divider.transform.localScale =
                            new Vector3(visualDiameter, height, thickness);
                        break;
                    }

                    case FluidColorCompartmentAxis.RadialWedges:
                    {
                        float angle =
                            -Mathf.PI +
                            Mathf.PI * 2.0f * boundary /
                            Mathf.Max(fluidConfig.colorCompartmentCount, 1);
                        float angleDegrees = -angle * Mathf.Rad2Deg;
                        divider.transform.localPosition = Vector3.zero;
                        divider.transform.localRotation =
                            Quaternion.Euler(0.0f, angleDegrees, 0.0f);
                        divider.transform.localScale =
                            new Vector3(visualDiameter, height, thickness);
                        break;
                    }

                    case FluidColorCompartmentAxis.BucketLocalX:
                    default:
                    {
                        float x = Mathf.Lerp(-visualRadius, visualRadius, t);
                        divider.transform.localPosition = new Vector3(x, 0.0f, 0.0f);
                        divider.transform.localRotation = Quaternion.identity;
                        divider.transform.localScale =
                            new Vector3(thickness, height, visualDiameter);
                        break;
                    }
                }
            }
        }

        private bool TryGetColorDividerConfig(out PaintFluidConfig fluidConfig)
        {
            fluidConfig = null;

            if (!showColorDividers)
                return false;

            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

            fluidConfig = paintFluidSystem != null
                ? paintFluidSystem.FluidConfig
                : null;

            return fluidConfig != null &&
                   fluidConfig.enableColorCompartments &&
                   fluidConfig.enablePhysicalColorDividers &&
                   fluidConfig.colorCompartmentCount > 1 &&
                   fluidConfig.colorDividerThicknessMeters > 0.0f;
        }

        private void SetLocalHoleShape(
            LineRenderer lr,
            BucketConfig config,
            BucketHoleConfig hole)
        {
            if (lr == null || config == null || hole == null)
                return;

            Vector2 halfExtents = config.GetResolvedHoleHalfExtents(hole);
            float a = Mathf.Max(halfExtents.x, 0.001f);
            float b = Mathf.Max(halfExtents.y, 0.001f);

            switch (hole.shape)
            {
                case BucketHoleShape.Square:
                case BucketHoleShape.Rectangle:
                    lr.positionCount = 4;
                    lr.SetPosition(0, new Vector3(-a, 0.0f, -b));
                    lr.SetPosition(1, new Vector3( a, 0.0f, -b));
                    lr.SetPosition(2, new Vector3( a, 0.0f,  b));
                    lr.SetPosition(3, new Vector3(-a, 0.0f,  b));
                    break;

                case BucketHoleShape.Slot:
                    SetLocalSlot(lr, a, b);
                    break;

                case BucketHoleShape.Ellipse:
                    SetLocalEllipse(lr, a, b);
                    break;

                case BucketHoleShape.Circular:
                default:
                    SetLocalEllipse(lr, a, a);
                    break;
            }
        }

        private void SetLocalEllipse(LineRenderer lr, float radiusX, float radiusZ)
        {
            if (lr == null)
                return;

            int count = Mathf.Max(lr.positionCount, 48);
            lr.positionCount = count;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float angle = t * Mathf.PI * 2.0f;

                Vector3 p = new Vector3(
                    Mathf.Cos(angle) * radiusX,
                    0.0f,
                    Mathf.Sin(angle) * radiusZ
                );

                lr.SetPosition(i, p);
            }
        }

        private void SetLocalSlot(LineRenderer lr, float halfLength, float halfWidth)
        {
            if (lr == null)
                return;

            int count = Mathf.Max(lr.positionCount, 48);
            lr.positionCount = count;

            float radius = Mathf.Max(halfWidth, 0.001f);
            float segmentHalfLength = Mathf.Max(halfLength - radius, 0.0f);
            int halfCount = count / 2;

            for (int i = 0; i < count; i++)
            {
                bool rightCap = i < halfCount;
                float capT = rightCap
                    ? (float)i / Mathf.Max(halfCount - 1, 1)
                    : (float)(i - halfCount) / Mathf.Max(count - halfCount - 1, 1);

                float angle = rightCap
                    ? Mathf.Lerp(-0.5f * Mathf.PI, 0.5f * Mathf.PI, capT)
                    : Mathf.Lerp(0.5f * Mathf.PI, 1.5f * Mathf.PI, capT);

                float centerX = rightCap ? segmentHalfLength : -segmentHalfLength;

                lr.SetPosition(
                    i,
                    new Vector3(
                        centerX + Mathf.Cos(angle) * radius,
                        0.0f,
                        Mathf.Sin(angle) * radius
                    )
                );
            }
        }

        private void SetLocalCircle(LineRenderer lr, float radius, Vector3 normalAxis)
        {
            if (lr == null)
                return;

            int count = Mathf.Max(lr.positionCount, 8);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float angle = t * Mathf.PI * 2.0f;

                Vector3 p = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.0f,
                    Mathf.Sin(angle) * radius
                );

                lr.SetPosition(i, p);
            }
        }

       private void ConfigureBucketMaterial(Material material)
{
    ConfigureMetalMaterial(material, 0.32f, 0.34f);

    if (material == null)
        return;

    material.SetFloat("_Surface", 1); // Transparent
    material.SetFloat("_Blend", 0);
    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    material.SetFloat("_ZWrite", 0);

    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
}

        private static Texture2D CreateBrushedMetalTexture()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                true)
            {
                name = "Runtime Brushed Bucket Metal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float scratch =
                    Mathf.Sin(y * 0.47f) * 0.002f +
                    Mathf.Sin(y * 1.91f) * 0.001f;

                for (int x = 0; x < size; x++)
                {
                    float noise = Mathf.PerlinNoise(
                        x * 0.12f,
                        y * 0.55f);
                    float value = Mathf.Clamp(
                        0.975f + (noise - 0.5f) * 0.015f + scratch,
                        0.95f,
                        1.0f);
                    pixels[y * size + x] =
                        new Color(value, value, value * 1.015f, 1.0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private void ConfigureMetalMaterial(
            Material material,
            float metallic,
            float smoothness)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
        }

        private void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private Vector3 ToVector3(float3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }

        private Quaternion ToQuaternion(quaternion q)
        {
            return new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        }
    }
}
