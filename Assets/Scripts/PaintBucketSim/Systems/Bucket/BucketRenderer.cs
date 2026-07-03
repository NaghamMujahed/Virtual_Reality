using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
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
        [SerializeField] private bool showHoleRing = true;

        [Header("Color Compartments")]
        [SerializeField] private bool showColorDividers = true;
        [SerializeField] private Color colorDividerColor =
            new Color(0.78f, 0.78f, 0.82f, 1.0f);
        [SerializeField, Range(0.5f, 3.0f)]
        private float dividerVisualThicknessMultiplier = 1.15f;
        [SerializeField, Range(0.25f, 1.0f)]
        private float dividerVisualHeightFraction = 0.92f;

        private GameObject _attachmentSphere;
        private LineRenderer _jointRingRenderer;
        private LineRenderer[] _holeRingRenderers;
        private GameObject[] _colorDividerVisuals;

        private Mesh _generatedMesh;
        private Material _runtimeMaterial;
        private Material _holeMaterial;
        private Material _jointMaterial;
        private Material _colorDividerMaterial;

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
            UpdateHoleVisuals();
            UpdateColorDividerVisuals();
        }

        public void RebuildMeshAndDebugVisuals()
        {
            if (bucketSystem == null || bucketSystem.Config == null)
                return;

            CreateBucketMesh(bucketSystem.Config);
            CreateAttachmentVisual();
            CreateHoleVisuals();
            CreateColorDividerVisuals();
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
            SetMaterialColor(_colorDividerMaterial, colorDividerColor);
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

            if (_runtimeMaterial != null)
            {
                SetMaterialColor(_runtimeMaterial, config.bucketColor);
                meshRenderer.sharedMaterial = _runtimeMaterial;
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

            int sideVertexCount = (radialSegments + 1) * 2;
            int bottomCenterIndex = sideVertexCount;
            int bottomRingStartIndex = bottomCenterIndex + 1;

            Vector3[] vertices = new Vector3[sideVertexCount + 1 + radialSegments + 1];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];

            int[] sideTriangles = new int[radialSegments * 6];
            int[] bottomTriangles = new int[radialSegments * 3];
            int[] triangles = new int[sideTriangles.Length + bottomTriangles.Length];

            float halfHeight = height * 0.5f;

            for (int i = 0; i <= radialSegments; i++)
            {
                float t = (float)i / radialSegments;
                float angle = t * Mathf.PI * 2.0f;

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                Vector3 bottom = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
                Vector3 top = new Vector3(cos * topRadius, halfHeight, sin * topRadius);

                int bottomIndex = i * 2;
                int topIndex = bottomIndex + 1;

                vertices[bottomIndex] = bottom;
                vertices[topIndex] = top;

                Vector3 normal = new Vector3(cos, 0.0f, sin).normalized;
                normals[bottomIndex] = normal;
                normals[topIndex] = normal;

                uvs[bottomIndex] = new Vector2(t, 0.0f);
                uvs[topIndex] = new Vector2(t, 1.0f);
            }

            int tri = 0;
            for (int i = 0; i < radialSegments; i++)
            {
                int b0 = i * 2;
                int t0 = b0 + 1;
                int b1 = (i + 1) * 2;
                int t1 = b1 + 1;

                sideTriangles[tri++] = b0;
                sideTriangles[tri++] = t0;
                sideTriangles[tri++] = b1;

                sideTriangles[tri++] = b1;
                sideTriangles[tri++] = t0;
                sideTriangles[tri++] = t1;
            }

            vertices[bottomCenterIndex] = new Vector3(0.0f, -halfHeight, 0.0f);
            normals[bottomCenterIndex] = Vector3.down;
            uvs[bottomCenterIndex] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i <= radialSegments; i++)
            {
                float t = (float)i / radialSegments;
                float angle = t * Mathf.PI * 2.0f;

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                int index = bottomRingStartIndex + i;

                vertices[index] = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
                normals[index] = Vector3.down;
                uvs[index] = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);
            }

            tri = 0;
            for (int i = 0; i < radialSegments; i++)
            {
                int r0 = bottomRingStartIndex + i;
                int r1 = bottomRingStartIndex + i + 1;

                bottomTriangles[tri++] = bottomCenterIndex;
                bottomTriangles[tri++] = r1;
                bottomTriangles[tri++] = r0;
            }

            sideTriangles.CopyTo(triangles, 0);
            bottomTriangles.CopyTo(triangles, sideTriangles.Length);

            Mesh mesh = new Mesh
            {
                name = "Generated Bucket Mesh"
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            mesh.RecalculateBounds();

            return mesh;
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

            Vector3 localPoint = config.GetResolvedAttachmentLocalPoint();

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

            Vector3 localPoint = bucketSystem.Config.GetResolvedAttachmentLocalPoint();

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
