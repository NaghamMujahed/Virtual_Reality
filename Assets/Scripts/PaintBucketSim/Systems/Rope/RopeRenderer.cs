using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Rope
{
    [RequireComponent(typeof(LineRenderer))]
    public class RopeRenderer : MonoBehaviour
    {
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;

        [Header("Visual")]
        [SerializeField] private Color ropeColor = new Color(0.85f, 0.75f, 0.55f);
        [SerializeField] private Color brokenPartColor = new Color(1.0f, 0.45f, 0.25f);
        [SerializeField] private float defaultWidth = 0.02f;

        [Header("Professional Rope Mesh")]
        [SerializeField] private bool useProceduralTube = true;
        [SerializeField] private bool useBraidedStrands = true;
        [SerializeField, Range(6, 24)] private int radialSegments = 12;
        [SerializeField, Range(1, 6)] private int strandCount = 3;
        [SerializeField, Range(0.0f, 8.0f)] private float helicalTurnsPerMeter = 2.35f;
        [SerializeField, Range(0.25f, 0.65f)] private float strandRadiusRatio = 0.46f;
        [SerializeField, Range(0.2f, 0.7f)] private float strandOrbitRadiusRatio = 0.48f;
        [SerializeField, Range(5, 12)] private int strandRadialSegments = 8;

        private Vector3[] _positionsBuffer;
        private Vector3[] _topBuffer;
        private Vector3[] _bottomBuffer;
        private float[] _twistBuffer;
        private Quaternion[] _frameBuffer;

        private LineRenderer _detachedLineRenderer;
        private Mesh _ropeMesh;
        private Material _ropeMaterial;
        private Material[] _strandMaterials;
        private Texture2D _fiberTexture;
        private Vector3[] _meshVertices;
        private Vector3[] _meshNormals;
        private Vector2[] _meshUvs;
        private int[][] _strandTriangleBuffers;

        private void Awake()
        {
            if (ropeSystem == null)
                ropeSystem = FindAnyObjectByType<RopeSystem>();

            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();

            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();

            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();

            ConfigureLineRenderer(lineRenderer, ropeColor);
            EnsureDetachedRenderer();
            EnsureMeshRenderer();
        }

        private void OnDestroy()
        {
            if (_ropeMesh != null)
                Destroy(_ropeMesh);
            if (_ropeMaterial != null)
                Destroy(_ropeMaterial);
            if (_strandMaterials != null)
            {
                for (int i = 0; i < _strandMaterials.Length; i++)
                {
                    if (_strandMaterials[i] != null)
                        Destroy(_strandMaterials[i]);
                }
            }
            if (_fiberTexture != null)
                Destroy(_fiberTexture);
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

            if (_twistBuffer == null || _twistBuffer.Length != Mathf.Max(0, count - 1))
                _twistBuffer = new float[Mathf.Max(0, count - 1)];

            ropeSystem.CopyTwistAnglesTo(_twistBuffer);

            if (_frameBuffer == null || _frameBuffer.Length != Mathf.Max(0, count - 1))
                _frameBuffer = new Quaternion[Mathf.Max(0, count - 1)];

            ropeSystem.CopySegmentFramesTo(_frameBuffer);

            float width = ropeSystem.Config != null
                ? ropeSystem.Config.visualRadiusMeters * 2.0f
                : defaultWidth;

            if (useProceduralTube && !ropeSystem.IsBroken)
            {
                lineRenderer.positionCount = 0;
                if (_detachedLineRenderer != null)
                    _detachedLineRenderer.positionCount = 0;

                BuildProceduralTube(_positionsBuffer, width * 0.5f);
                return;
            }

            if (meshRenderer != null)
                meshRenderer.enabled = false;

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

            renderer.startWidth = defaultWidth;
            renderer.endWidth = defaultWidth;
        }

        private void EnsureMeshRenderer()
        {
            if (meshFilter == null || meshRenderer == null)
                return;

            if (_ropeMesh == null)
            {
                _ropeMesh = new Mesh
                {
                    name = "Procedural Twisted Rope Mesh"
                };
                _ropeMesh.MarkDynamic();
                meshFilter.sharedMesh = _ropeMesh;
            }

            if (useBraidedStrands)
            {
                EnsureStrandMaterials();
                meshRenderer.sharedMaterials = _strandMaterials;
            }
            else
            {
                if (_ropeMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                    _ropeMaterial = new Material(
                        shader != null ? shader : Shader.Find("Standard"))
                    {
                        name = "Runtime Twisted Rope Material"
                    };
                    SetMaterialColor(_ropeMaterial, ropeColor);
                    _ropeMaterial.SetFloat("_Smoothness", 0.18f);
                }

                meshRenderer.sharedMaterial = _ropeMaterial;
            }

            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }

        private void EnsureStrandMaterials()
        {
            int count = Mathf.Clamp(strandCount, 2, 6);
            if (_strandMaterials != null && _strandMaterials.Length == count)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            _strandMaterials = new Material[count];
            if (_fiberTexture == null)
                _fiberTexture = CreateFiberTexture();

            for (int i = 0; i < count; i++)
            {
                float phase = i / (float)Mathf.Max(count - 1, 1);
                float brightness = Mathf.Lerp(0.82f, 1.12f, phase);
                Color strandColor = new Color(
                    Mathf.Clamp01(ropeColor.r * brightness),
                    Mathf.Clamp01(ropeColor.g * brightness),
                    Mathf.Clamp01(ropeColor.b * brightness),
                    ropeColor.a);

                Material material = new Material(shader)
                {
                    name = $"Runtime Rope Strand {i + 1}",
                    enableInstancing = true
                };
                SetMaterialColor(material, strandColor);

                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", 0.0f);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 0.13f);
                if (_fiberTexture != null && material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", _fiberTexture);
                    material.SetTextureScale("_BaseMap", new Vector2(2.0f, 1.0f));
                }

                _strandMaterials[i] = material;
            }
        }

        private static Texture2D CreateFiberTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                true)
            {
                name = "Runtime Rope Fiber Texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fiber =
                        0.5f +
                        0.5f * Mathf.Sin((x * 0.8f + y * 0.18f) * Mathf.PI);
                    float noise = Mathf.PerlinNoise(x * 0.19f, y * 0.07f);
                    float value = Mathf.Clamp(
                        0.76f + fiber * 0.16f + (noise - 0.5f) * 0.08f,
                        0.62f,
                        1.0f);
                    pixels[y * size + x] =
                        new Color(value, value * 0.97f, value * 0.9f, 1.0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private void BuildProceduralTube(Vector3[] positions, float radius)
        {
            if (meshFilter == null || meshRenderer == null || positions == null || positions.Length < 2)
                return;

            EnsureMeshRenderer();

            int rings = positions.Length;
            int strands = useBraidedStrands
                ? Mathf.Clamp(strandCount, 2, 6)
                : 1;
            int sides = useBraidedStrands
                ? Mathf.Clamp(strandRadialSegments, 5, 12)
                : Mathf.Max(6, radialSegments);
            int vertexCount = rings * sides * strands;
            int segmentCount = rings - 1;
            int triangleIndexCountPerStrand = segmentCount * sides * 6;

            if (_ropeMesh.indexFormat != IndexFormat.UInt32 && vertexCount > 65000)
                _ropeMesh.indexFormat = IndexFormat.UInt32;

            EnsureMeshBuffers(
                vertexCount,
                strands,
                triangleIndexCountPerStrand);

            Vector3 normal = ChooseInitialNormal(ComputeTangent(positions, 0));
            float distance = 0.0f;

            for (int i = 0; i < rings; i++)
            {
                if (i > 0)
                    distance += Vector3.Distance(positions[i - 1], positions[i]);

                Vector3 tangent = ComputeTangent(positions, i);
                Vector3 ringNormal;
                Vector3 binormal;

                if (_frameBuffer != null && _frameBuffer.Length > 0)
                {
                    int frameIndex = Mathf.Clamp(i, 0, _frameBuffer.Length - 1);
                    Quaternion frame = _frameBuffer[frameIndex];

                    tangent = frame * Vector3.forward;
                    ringNormal = frame * Vector3.up;
                    binormal = frame * Vector3.right;

                    if (tangent.sqrMagnitude < 1e-8f)
                        tangent = ComputeTangent(positions, i);

                    if (ringNormal.sqrMagnitude < 1e-8f)
                        ringNormal = ChooseInitialNormal(tangent);

                    tangent.Normalize();
                    ringNormal = Vector3.ProjectOnPlane(ringNormal, tangent).normalized;
                    binormal = Vector3.Cross(tangent, ringNormal).normalized;
                }
                else
                {
                    normal = Vector3.ProjectOnPlane(normal, tangent);
                    if (normal.sqrMagnitude < 1e-6f)
                        normal = ChooseInitialNormal(tangent);

                    normal.Normalize();
                    ringNormal = normal;
                    binormal = Vector3.Cross(tangent, normal).normalized;
                }

                for (int strand = 0; strand < strands; strand++)
                {
                    float strandPhase =
                        strand / (float)strands * Mathf.PI * 2.0f;
                    float layAngle =
                        strandPhase +
                        distance * helicalTurnsPerMeter * Mathf.PI * 2.0f;
                    Vector3 orbitRadial =
                        Mathf.Cos(layAngle) * ringNormal +
                        Mathf.Sin(layAngle) * binormal;
                    Vector3 orbitBinormal =
                        Vector3.Cross(tangent, orbitRadial).normalized;

                    float centerOffset = useBraidedStrands
                        ? radius * strandOrbitRadiusRatio
                        : 0.0f;
                    float fiberRadius = useBraidedStrands
                        ? radius * strandRadiusRatio
                        : radius;
                    Vector3 strandCenter =
                        positions[i] + orbitRadial * centerOffset;

                    for (int side = 0; side < sides; side++)
                    {
                        float t = side / (float)sides;
                        float theta = t * Mathf.PI * 2.0f;
                        Vector3 tubeRadial =
                            Mathf.Cos(theta) * orbitRadial +
                            Mathf.Sin(theta) * orbitBinormal;
                        int vertexIndex =
                            (strand * rings + i) * sides + side;

                        _meshVertices[vertexIndex] =
                            strandCenter + tubeRadial * fiberRadius;
                        _meshNormals[vertexIndex] = tubeRadial;
                        _meshUvs[vertexIndex] =
                            new Vector2(t, distance * 8.0f);
                    }
                }
            }

            for (int strand = 0; strand < strands; strand++)
            {
                int tri = 0;
                int[] triangles = _strandTriangleBuffers[strand];

                for (int i = 0; i < segmentCount; i++)
                {
                    int ring = (strand * rings + i) * sides;
                    int nextRing = ring + sides;

                    for (int side = 0; side < sides; side++)
                    {
                        int nextSide = (side + 1) % sides;

                        int a = ring + side;
                        int b = ring + nextSide;
                        int c = nextRing + side;
                        int d = nextRing + nextSide;

                        triangles[tri++] = a;
                        triangles[tri++] = c;
                        triangles[tri++] = b;

                        triangles[tri++] = b;
                        triangles[tri++] = c;
                        triangles[tri++] = d;
                    }
                }
            }

            _ropeMesh.Clear();
            _ropeMesh.vertices = _meshVertices;
            _ropeMesh.normals = _meshNormals;
            _ropeMesh.uv = _meshUvs;
            _ropeMesh.subMeshCount = strands;
            for (int strand = 0; strand < strands; strand++)
                _ropeMesh.SetTriangles(_strandTriangleBuffers[strand], strand);
            _ropeMesh.RecalculateBounds();

            if (useBraidedStrands)
            {
                EnsureStrandMaterials();
                meshRenderer.sharedMaterials = _strandMaterials;
            }

            meshRenderer.enabled = true;
        }

        private void EnsureMeshBuffers(
            int vertexCount,
            int strands,
            int triangleCountPerStrand)
        {
            if (_meshVertices == null || _meshVertices.Length != vertexCount)
            {
                _meshVertices = new Vector3[vertexCount];
                _meshNormals = new Vector3[vertexCount];
                _meshUvs = new Vector2[vertexCount];
            }

            if (_strandTriangleBuffers == null ||
                _strandTriangleBuffers.Length != strands)
            {
                _strandTriangleBuffers = new int[strands][];
            }

            for (int i = 0; i < strands; i++)
            {
                if (_strandTriangleBuffers[i] == null ||
                    _strandTriangleBuffers[i].Length != triangleCountPerStrand)
                {
                    _strandTriangleBuffers[i] =
                        new int[triangleCountPerStrand];
                }
            }
        }

        private static Vector3 ComputeTangent(Vector3[] positions, int index)
        {
            if (positions == null || positions.Length < 2)
                return Vector3.down;

            Vector3 tangent;
            if (index <= 0)
                tangent = positions[1] - positions[0];
            else if (index >= positions.Length - 1)
                tangent = positions[index] - positions[index - 1];
            else
                tangent = positions[index + 1] - positions[index - 1];

            if (tangent.sqrMagnitude < 1e-8f)
                return Vector3.down;

            return tangent.normalized;
        }

        private static Vector3 ChooseInitialNormal(Vector3 tangent)
        {
            Vector3 upProjected = Vector3.ProjectOnPlane(Vector3.up, tangent);
            if (upProjected.sqrMagnitude > 1e-6f)
                return upProjected.normalized;

            Vector3 rightProjected = Vector3.ProjectOnPlane(Vector3.right, tangent);
            if (rightProjected.sqrMagnitude > 1e-6f)
                return rightProjected.normalized;

            return Vector3.forward;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }
    }
}
