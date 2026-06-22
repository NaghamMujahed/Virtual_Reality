using System.Diagnostics;
using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Fluid
{
    public class GpuParticleIndirectRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GpuFluidBufferSet gpuBufferSet;
        [SerializeField] private GpuParticleRenderConfig renderConfig;

        [Header("Rendering")]
        [SerializeField] private Mesh particleMesh;
        [SerializeField] private Material particleMaterial;

        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows = false;

        private GraphicsBuffer _commandBuffer;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandData;

        private MaterialPropertyBlock _mpb;

        private readonly Stopwatch _renderSubmitWatch = new Stopwatch();

        private GpuParticleRenderStats _stats;

        public GpuParticleRenderStats Stats => _stats;

        private void Awake()
        {
            if (gpuBufferSet == null)
                gpuBufferSet = FindFirstObjectByType<GpuFluidBufferSet>();

            if (particleMesh == null)
                particleMesh = CreateOctahedronMesh();

            if (particleMaterial == null)
                particleMaterial = CreateDefaultMaterial();

            _mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            EnsureCommandBuffer();
        }

        private void OnDisable()
        {
            ReleaseCommandBuffer();
        }

        private void OnDestroy()
        {
            ReleaseCommandBuffer();
        }

        private void LateUpdate()
        {
            if (renderConfig == null ||
                !renderConfig.enableGpuIndirectRendering ||
                gpuBufferSet == null ||
                !gpuBufferSet.IsInitialized ||
                gpuBufferSet.UploadedParticleCount <= 0 ||
                particleMaterial == null ||
                particleMesh == null)
            {
                _stats.enabled = false;
                return;
            }

            EnsureCommandBuffer();
            UpdateCommandBuffer();
            RenderParticles();
            UpdateStats();
        }

        private void EnsureCommandBuffer()
        {
            if (_commandBuffer != null)
                return;

            _commandBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size
            );

            _commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];

            _commandData[0].indexCountPerInstance = particleMesh != null
                ? particleMesh.GetIndexCount(0)
                : 0;

            _commandData[0].instanceCount = 0;

            _commandData[0].startIndex = particleMesh != null
                ? particleMesh.GetIndexStart(0)
                : 0;

            _commandData[0].baseVertexIndex = particleMesh != null
                ? particleMesh.GetBaseVertex(0)
                : 0;

            _commandData[0].startInstance = 0;

            _commandBuffer.SetData(_commandData);
        }

        private void UpdateCommandBuffer()
        {
            uint visibleCount = (uint)Mathf.Max(0, gpuBufferSet.UploadedParticleCount);

            // Optional extra safety cap from render config.
            if (renderConfig != null)
                visibleCount = (uint)Mathf.Min((int)visibleCount, renderConfig.maxRenderedParticles);

            _commandData[0].indexCountPerInstance = particleMesh.GetIndexCount(0);
            _commandData[0].instanceCount = visibleCount;
            _commandData[0].startIndex = particleMesh.GetIndexStart(0);
            _commandData[0].baseVertexIndex = particleMesh.GetBaseVertex(0);
            _commandData[0].startInstance = 0;

            _commandBuffer.SetData(_commandData);
        }

        private void RenderParticles()
        {
            if (_commandBuffer == null)
                return;

            _renderSubmitWatch.Restart();

            _mpb.SetBuffer("_ParticlePositionRadius", gpuBufferSet.PositionRadiusBuffer);
            _mpb.SetBuffer("_ParticleColor", gpuBufferSet.ColorBuffer);

            _mpb.SetFloat("_VisualRadiusScale", renderConfig.visualRadiusScale);
            _mpb.SetFloat("_UsePerParticleColor", renderConfig.usePerParticleColor ? 1.0f : 0.0f);
            _mpb.SetColor("_FallbackColor", renderConfig.fallbackColor);

            RenderParams renderParams = new RenderParams(particleMaterial)
            {
                worldBounds = new Bounds(
                    renderConfig.worldBoundsCenter,
                    renderConfig.worldBoundsSize
                ),
                matProps = _mpb,
                shadowCastingMode = shadowCastingMode,
                receiveShadows = receiveShadows,
                layer = gameObject.layer
            };

            Graphics.RenderMeshIndirect(
                renderParams,
                particleMesh,
                _commandBuffer,
                1
            );

            _renderSubmitWatch.Stop();
        }

        private void UpdateStats()
        {
            _stats.initialized = _commandBuffer != null;
            _stats.enabled = renderConfig != null && renderConfig.enableGpuIndirectRendering;

            _stats.uploadedParticles = gpuBufferSet != null
                ? gpuBufferSet.UploadedParticleCount
                : 0;

            _stats.maxRenderedParticles = renderConfig != null
                ? renderConfig.maxRenderedParticles
                : 0;

            _stats.renderStride = gpuBufferSet != null && gpuBufferSet.Config != null
                ? gpuBufferSet.Config.uploadStride
                : 1;

            _stats.uploadFrame = gpuBufferSet != null
                ? gpuBufferSet.Stats.uploadFrame
                : -1;

            _stats.cpuUploadMilliseconds = gpuBufferSet != null
                ? gpuBufferSet.Stats.cpuUploadMilliseconds
                : 0.0f;

            _stats.cpuRenderSubmitMilliseconds =
                (float)_renderSubmitWatch.Elapsed.TotalMilliseconds;

            _stats.usingPerParticleColor = renderConfig != null && renderConfig.usePerParticleColor;
        }

        private void ReleaseCommandBuffer()
        {
            if (_commandBuffer != null)
            {
                _commandBuffer.Release();
                _commandBuffer = null;
            }

            _commandData = null;
            _stats = default;
        }

        private Material CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("PaintBucketSim/GPU Indirect Paint Particle URP");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader);
            material.enableInstancing = true;

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
                name = "GPU Paint Particle Octahedron"
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}