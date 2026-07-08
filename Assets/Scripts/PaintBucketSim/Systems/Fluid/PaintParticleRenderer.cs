using PaintBucketSim.Configs;
using UnityEngine;

namespace PaintBucketSim.Systems.Fluid
{
    public class PaintParticleRenderer : MonoBehaviour
    {
        [SerializeField] private PaintFluidSystem paintFluidSystem;
        [SerializeField] private PaintFluidConfig paintFluidConfig;
        [SerializeField] private PaintMaterialConfig paintMaterialConfig;

        [Header("Runtime")]
        [SerializeField] private bool visible = true;

        private Mesh _particleMesh;
        private Material _particleMaterial;

        private readonly Matrix4x4[] _batchMatrices = new Matrix4x4[1023];

        private void Awake()
        {
            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

            if (paintFluidConfig == null && paintFluidSystem != null)
                paintFluidConfig = paintFluidSystem.FluidConfig;

            if (paintMaterialConfig == null && paintFluidSystem != null)
                paintMaterialConfig = paintFluidSystem.MaterialConfig;

            _particleMesh = CreateOctahedronMesh();
            _particleMaterial = CreateMaterial(
                paintMaterialConfig != null ? paintMaterialConfig.baseColor : Color.blue
            );
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
                visible = !visible;
        }

        private void LateUpdate()
        {
            if (!visible)
                return;

            if (paintFluidSystem == null || !paintFluidSystem.IsInitialized)
                return;

            // GPU simulation owns a different position buffer after bootstrap.
            // Rendering the stale CPU copy at the same time is misleading.
            if (paintFluidSystem.IsGpuSolverActive)
                return;

            if (paintFluidConfig != null && !paintFluidConfig.renderParticles)
                return;

            DrawParticles();
        }

        private void DrawParticles()
        {
            int particleCount = paintFluidSystem.ParticleCount;

            int stride = 1;
            int maxRendered = particleCount;

            if (paintFluidConfig != null && paintFluidConfig.enableRenderLod)
            {
                stride = Mathf.Max(1, paintFluidConfig.renderStride);
                maxRendered = Mathf.Min(
                    particleCount,
                    Mathf.Max(1, paintFluidConfig.maxRenderedParticles)
                );

                if (particleCount > maxRendered)
                {
                    stride = Mathf.Max(
                        stride,
                        Mathf.CeilToInt((float)particleCount / maxRendered)
                    );
                }
            }

            int rendered = 0;
            int batchCount = 0;

            for (int i = 0; i < particleCount; i += stride)
            {
                if (rendered >= maxRendered)
                    break;

                Vector3 p = paintFluidSystem.GetParticlePosition(i);
                float r = paintFluidSystem.GetParticleRadius(i);

                float scaleValue = r * 2.0f;

                if (paintFluidConfig != null)
                    scaleValue *= paintFluidConfig.visualParticleSizeScale;

                Vector3 scale = Vector3.one * scaleValue;

                _batchMatrices[batchCount] = Matrix4x4.TRS(
                    p,
                    Quaternion.identity,
                    scale
                );

                batchCount++;
                rendered++;

                if (batchCount == _batchMatrices.Length)
                {
                    Graphics.DrawMeshInstanced(
                        _particleMesh,
                        0,
                        _particleMaterial,
                        _batchMatrices,
                        batchCount
                    );

                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    _particleMesh,
                    0,
                    _particleMaterial,
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

        private Mesh CreateOctahedronMesh()
        {
            Vector3[] vertices =
            {
                new Vector3( 0,  1,  0),
                new Vector3( 1,  0,  0),
                new Vector3( 0,  0,  1),
                new Vector3(-1,  0,  0),
                new Vector3( 0,  0, -1),
                new Vector3( 0, -1,  0)
            };

            int[] triangles =
            {
                0, 2, 1,
                0, 3, 2,
                0, 4, 3,
                0, 1, 4,

                5, 1, 2,
                5, 2, 3,
                5, 3, 4,
                5, 4, 1
            };

            Mesh mesh = new Mesh
            {
                name = "Paint Particle Octahedron"
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void OnDestroy()
        {
            if (_particleMesh != null)
                Destroy(_particleMesh);

            if (_particleMaterial != null)
                Destroy(_particleMaterial);
        }
    }
}
