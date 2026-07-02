using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using UnityEngine;

namespace PaintBucketSim.Systems.Boundary
{
    public class BoundaryRenderer : MonoBehaviour
    {
        [SerializeField] private BoundarySystem boundarySystem;
        [SerializeField] private BoundaryConfig boundaryConfig;

        [Header("Runtime")]
        [SerializeField] private bool visible = true;

        private Mesh _cubeMesh;

        private Material _wallMaterial;
        private Material _bottomMaterial;
        private Material _holeEdgeMaterial;

        private readonly Matrix4x4[] _batchMatrices = new Matrix4x4[1023];

        private void Awake()
        {
            if (boundarySystem == null)
                boundarySystem = FindAnyObjectByType<BoundarySystem>();

            if (boundaryConfig == null && boundarySystem != null)
                boundaryConfig = boundarySystem.Config;

            _cubeMesh = CreateCubeMesh();

            _wallMaterial = CreateMaterial(boundaryConfig != null ? boundaryConfig.wallColor : Color.cyan);
            _bottomMaterial = CreateMaterial(boundaryConfig != null ? boundaryConfig.bottomColor : Color.green);
            _holeEdgeMaterial = CreateMaterial(boundaryConfig != null ? boundaryConfig.holeEdgeColor : Color.red);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                visible = !visible;
        }

        private void LateUpdate()
        {
            if (!visible)
                return;

            if (boundarySystem == null || !boundarySystem.IsInitialized)
                return;

            if (boundaryConfig != null && !boundaryConfig.renderBoundaryParticles)
                return;

            DrawType(BoundaryParticleType.Wall, _wallMaterial);
            DrawType(BoundaryParticleType.Bottom, _bottomMaterial);
            DrawType(BoundaryParticleType.HoleEdge, _holeEdgeMaterial);
        }

        private void DrawType(BoundaryParticleType type, Material material)
        {
            if (material == null || _cubeMesh == null)
                return;

            float size = boundaryConfig != null
                ? boundaryConfig.visualParticleSizeMeters
                : 0.02f;

            Vector3 scale = Vector3.one * size;

            int batchCount = 0;

            for (int i = 0; i < boundarySystem.Count; i++)
            {
                if (boundarySystem.GetParticleType(i) != type)
                    continue;

                Vector3 p = boundarySystem.GetWorldPosition(i);

                _batchMatrices[batchCount] =
                    Matrix4x4.TRS(p, Quaternion.identity, scale);

                batchCount++;

                if (batchCount == _batchMatrices.Length)
                {
                    Graphics.DrawMeshInstanced(
                        _cubeMesh,
                        0,
                        material,
                        _batchMatrices,
                        batchCount
                    );

                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    _cubeMesh,
                    0,
                    material,
                    _batchMatrices,
                    batchCount
                );
            }
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material material = new Material(shader);
            material.enableInstancing = true;
            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            return material;
        }

        private Mesh CreateCubeMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),

                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };

            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };

            Mesh mesh = new Mesh
            {
                name = "Boundary Particle Cube"
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void OnDestroy()
        {
            if (_cubeMesh != null)
                Destroy(_cubeMesh);

            if (_wallMaterial != null)
                Destroy(_wallMaterial);

            if (_bottomMaterial != null)
                Destroy(_bottomMaterial);

            if (_holeEdgeMaterial != null)
                Destroy(_holeEdgeMaterial);
        }
    }
}
