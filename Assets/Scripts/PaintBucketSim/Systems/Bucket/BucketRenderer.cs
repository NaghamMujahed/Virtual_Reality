using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Bucket
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class BucketRenderer : MonoBehaviour
    {
        [SerializeField] private BucketSystem bucketSystem;

        [Header("Visual References")]
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;

        [Header("Debug Visuals")]
        [SerializeField] private bool showAttachmentJoint = true;
        [SerializeField] private bool showHoleRing = true;

        private GameObject _attachmentSphere;
        private LineRenderer _jointRingRenderer;
        private LineRenderer[] _holeRingRenderers;

        private Mesh _generatedMesh;
        private Material _runtimeMaterial;
        private Material _holeMaterial;
        private Material _jointMaterial;

        private void Awake()
        {
            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

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
        }

        public void RebuildMeshAndDebugVisuals()
        {
            if (bucketSystem == null || bucketSystem.Config == null)
                return;

            CreateBucketMesh(bucketSystem.Config);
            CreateAttachmentVisual();
            CreateHoleVisuals();
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
                _runtimeMaterial.color = config.bucketColor;
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
                    _jointMaterial.color = config.jointColor;
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
                    _holeMaterial.color = config.holeColor;

                _holeRingRenderers[i] = lr;
            }
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

                _holeRingRenderers[i].transform.localPosition = localCenter;
                _holeRingRenderers[i].transform.localRotation =
                    Quaternion.FromToRotation(Vector3.up, localNormal);

                SetLocalCircle(_holeRingRenderers[i], hole.radiusMeters, Vector3.up);
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