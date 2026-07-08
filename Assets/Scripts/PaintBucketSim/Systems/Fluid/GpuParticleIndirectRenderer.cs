using System.Diagnostics;
using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid.GPU;
using PaintBucketSim.Systems.Bucket;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Fluid
{
    [DefaultExecutionOrder(3000)]
    public class GpuParticleIndirectRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GpuFluidBufferSet gpuBufferSet;
        [SerializeField] private GpuParticleRenderConfig renderConfig;
        [SerializeField] private BucketSystem bucketSystem;

        [Header("Rendering")]
        [SerializeField] private Mesh particleMesh;
        [SerializeField] private Material particleMaterial;

        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows = false;

        private GraphicsBuffer _commandBuffer;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandData;
        private GraphicsBuffer _renderableParticleIndices;
        private int _renderableParticleCapacity;
        private ComputeShader _visibilityCompute;
        private int _kernelBuildRenderableParticleList = -1;
        private bool _usingGpuVisibilityCompaction;

        private MaterialPropertyBlock _mpb;
        private Mesh _generatedParticleMesh;
        private GpuParticleVisualMode _generatedVisualMode;

        private readonly Stopwatch _renderSubmitWatch = new Stopwatch();

        private GpuParticleRenderStats _stats;

        public GpuParticleRenderStats Stats => _stats;

        private void Awake()
        {
            if (gpuBufferSet == null)
                gpuBufferSet = FindAnyObjectByType<GpuFluidBufferSet>();
            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            EnsureParticleMesh();

            if (particleMaterial == null)
                particleMaterial = CreateDefaultMaterial();

            _mpb = new MaterialPropertyBlock();

            EnsureScreenSpaceFluidRenderer();
        }

        // The screen-space fluid surface is controlled entirely from the render
        // config (fluidRenderMode). Auto-attach its renderer here and inject the
        // shared references so the user only has to flip the config toggle — the
        // config ScriptableObject is not discoverable via FindAnyObjectByType.
        private void EnsureScreenSpaceFluidRenderer()
        {
            var fluidRenderer = GetComponent<ScreenSpaceFluidRenderer>();
            if (fluidRenderer == null)
                fluidRenderer = gameObject.AddComponent<ScreenSpaceFluidRenderer>();

            fluidRenderer.Configure(gpuBufferSet, renderConfig, bucketSystem);
        }

        private void OnEnable()
        {
            EnsureCommandBuffer();
        }

        private void OnDisable()
        {
            ReleaseCommandBuffer();
            ReleaseVisibilityResources();
        }

        private void OnDestroy()
        {
            ReleaseCommandBuffer();
            ReleaseVisibilityResources();
            ReleaseGeneratedMesh();
        }

        private void LateUpdate()
        {
            if (renderConfig == null ||
                !renderConfig.enableGpuIndirectRendering ||
                // Screen-space fluid mode renders the liquid surface instead of
                // discrete particles; skip the particle draw to avoid doubling.
                renderConfig.fluidRenderMode == FluidRenderMode.ScreenSpaceFluid ||
                gpuBufferSet == null ||
                !gpuBufferSet.IsInitialized ||
                gpuBufferSet.UploadedParticleCount <= 0 ||
                particleMaterial == null ||
                particleMesh == null)
            {
                _stats.enabled = false;
                return;
            }

            EnsureParticleMesh();
            EnsureCommandBuffer();
            EnsureVisibilityResources();
            UpdateCommandBuffer();
            _usingGpuVisibilityCompaction = BuildRenderableParticleList();
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

        private void EnsureVisibilityResources()
        {
            int requestedCapacity = Mathf.Max(
                1,
                Mathf.Min(
                    gpuBufferSet != null ? gpuBufferSet.Capacity : 1,
                    renderConfig != null ? renderConfig.maxRenderedParticles : 1
                )
            );

            if (_renderableParticleIndices == null ||
                _renderableParticleCapacity != requestedCapacity)
            {
                ReleaseVisibilityResources();
                _renderableParticleIndices = new GraphicsBuffer(
                    GraphicsBuffer.Target.Append,
                    requestedCapacity,
                    sizeof(uint)
                );
                _renderableParticleCapacity = requestedCapacity;
            }

            ComputeShader requestedCompute =
                gpuBufferSet != null ? gpuBufferSet.UtilityCompute : null;

            if (_visibilityCompute == requestedCompute &&
                _kernelBuildRenderableParticleList >= 0)
            {
                return;
            }

            _visibilityCompute = requestedCompute;
            _kernelBuildRenderableParticleList = -1;

            if (_visibilityCompute == null)
                return;

            try
            {
                _kernelBuildRenderableParticleList =
                    _visibilityCompute.FindKernel("KBuildRenderableParticleList");
            }
            catch
            {
                _kernelBuildRenderableParticleList = -1;
            }
        }

        private void UpdateCommandBuffer()
        {
            int uploadedCount = Mathf.Max(0, gpuBufferSet.UploadedParticleCount);
            int stride = renderConfig != null
                ? Mathf.Max(1, renderConfig.renderStride)
                : 1;

            int visibleCount = Mathf.CeilToInt(uploadedCount / (float)stride);

            if (renderConfig != null && visibleCount > renderConfig.maxRenderedParticles)
            {
                stride = Mathf.Max(
                    stride,
                    Mathf.CeilToInt(uploadedCount / (float)renderConfig.maxRenderedParticles)
                );
                visibleCount = Mathf.CeilToInt(uploadedCount / (float)stride);
            }

            _effectiveRenderStride = stride;

            _commandData[0].indexCountPerInstance = particleMesh.GetIndexCount(0);
            _commandData[0].instanceCount = (uint)Mathf.Max(0, visibleCount);
            _commandData[0].startIndex = particleMesh.GetIndexStart(0);
            _commandData[0].baseVertexIndex = particleMesh.GetBaseVertex(0);
            _commandData[0].startInstance = 0;

            _commandBuffer.SetData(_commandData);
        }

        private bool BuildRenderableParticleList()
        {
            if (_visibilityCompute == null ||
                _kernelBuildRenderableParticleList < 0 ||
                _renderableParticleIndices == null ||
                _commandBuffer == null)
            {
                return false;
            }

            int uploadedCount = Mathf.Max(0, gpuBufferSet.UploadedParticleCount);
            if (uploadedCount <= 0)
                return false;

            _renderableParticleIndices.SetCounterValue(0);

            _visibilityCompute.SetInt("_ParticleCount", uploadedCount);
            _visibilityCompute.SetInt(
                "_ParticleIndexStride",
                Mathf.Max(_effectiveRenderStride, 1)
            );
            _visibilityCompute.SetInt(
                "_HideCanvasAndLostParticles",
                renderConfig.hideCanvasAndLostParticles ? 1 : 0
            );
            _visibilityCompute.SetBuffer(
                _kernelBuildRenderableParticleList,
                "_ParticleStateAgeId",
                gpuBufferSet.StateAgeIdBuffer
            );
            _visibilityCompute.SetBuffer(
                _kernelBuildRenderableParticleList,
                "_RenderableParticleIndices",
                _renderableParticleIndices
            );

            int candidateCount = Mathf.CeilToInt(
                uploadedCount / (float)Mathf.Max(_effectiveRenderStride, 1)
            );
            int groups = Mathf.CeilToInt(candidateCount / 256.0f);
            _visibilityCompute.Dispatch(
                _kernelBuildRenderableParticleList,
                Mathf.Max(groups, 1),
                1,
                1
            );

            // instanceCount is the second uint in IndirectDrawIndexedArgs.
            GraphicsBuffer.CopyCount(
                _renderableParticleIndices,
                _commandBuffer,
                sizeof(uint)
            );
            return true;
        }

        private void RenderParticles()
        {
            if (_commandBuffer == null)
                return;

            _renderSubmitWatch.Restart();

            _mpb.SetBuffer("_ParticlePositionRadius", gpuBufferSet.PositionRadiusBuffer);
            _mpb.SetBuffer("_ParticleColor", gpuBufferSet.ColorBuffer);
            _mpb.SetBuffer("_ParticleStateAgeId", gpuBufferSet.StateAgeIdBuffer);
            _mpb.SetBuffer("_RenderableParticleIndices", _renderableParticleIndices);
            _mpb.SetFloat(
                "_UseRenderableParticleList",
                _usingGpuVisibilityCompaction ? 1.0f : 0.0f
            );

            bool useSplat =
                renderConfig.visualMode == GpuParticleVisualMode.CameraFacingSplat;

            _mpb.SetFloat("_VisualRadiusScale", renderConfig.visualRadiusScale);
            _mpb.SetFloat("_UsePerParticleColor", renderConfig.usePerParticleColor ? 1.0f : 0.0f);
            _mpb.SetFloat(
                "_HideCanvasAndLostParticles",
                renderConfig.hideCanvasAndLostParticles ? 1.0f : 0.0f
            );
            _mpb.SetColor("_FallbackColor", renderConfig.fallbackColor);
            _mpb.SetInt("_ParticleIndexStride", _effectiveRenderStride);
            _mpb.SetInt("_ParticleCount", gpuBufferSet.UploadedParticleCount);
            bool useBucketLocal =
                gpuBufferSet.MpmParticlesUseBucketLocalSpace &&
                bucketSystem != null &&
                bucketSystem.IsInitialized;
            _mpb.SetFloat(
                "_UseBucketLocalParticles",
                useBucketLocal ? 1.0f : 0.0f
            );
            if (useBucketLocal)
            {
                var state = bucketSystem.State;
                _mpb.SetMatrix(
                    "_BucketLocalToWorld",
                    Matrix4x4.TRS(
                        state.position,
                        state.rotation,
                        Vector3.one
                    )
                );
            }
            _mpb.SetFloat("_RenderMode", useSplat ? 1.0f : 0.0f);
            _mpb.SetFloat("_SplatNormalStrength", renderConfig.splatNormalStrength);
            _mpb.SetFloat("_SplatEdgeSoftness", renderConfig.splatEdgeSoftness);
            _mpb.SetFloat("_PaintSpecularStrength", renderConfig.paintSpecularStrength);
            _mpb.SetFloat("_PaintFresnelStrength", renderConfig.paintFresnelStrength);

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

            _stats.renderStride = _effectiveRenderStride;
            _stats.visualMode = renderConfig != null
                ? (int)renderConfig.visualMode
                : 0;
            _stats.visualRadiusScale = renderConfig != null
                ? renderConfig.visualRadiusScale
                : 1.0f;
            _stats.meshVertexCount = particleMesh != null
                ? particleMesh.vertexCount
                : 0;
            _stats.meshIndexCount = particleMesh != null
                ? (int)particleMesh.GetIndexCount(0)
                : 0;

            _stats.uploadFrame = gpuBufferSet != null
                ? gpuBufferSet.Stats.uploadFrame
                : -1;

            _stats.cpuUploadMilliseconds = gpuBufferSet != null
                ? gpuBufferSet.Stats.cpuUploadMilliseconds
                : 0.0f;

            _stats.cpuRenderSubmitMilliseconds =
                (float)_renderSubmitWatch.Elapsed.TotalMilliseconds;

            _stats.usingPerParticleColor = renderConfig != null && renderConfig.usePerParticleColor;
            _stats.usingCameraFacingSplat =
                renderConfig != null &&
                renderConfig.visualMode == GpuParticleVisualMode.CameraFacingSplat;
            _stats.usingGpuVisibilityCompaction = _usingGpuVisibilityCompaction;
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
            _usingGpuVisibilityCompaction = false;
        }

        private void ReleaseVisibilityResources()
        {
            if (_renderableParticleIndices != null)
            {
                _renderableParticleIndices.Release();
                _renderableParticleIndices = null;
            }

            _renderableParticleCapacity = 0;
        }

        private int _effectiveRenderStride = 1;

        private void EnsureParticleMesh()
        {
            GpuParticleVisualMode mode = renderConfig != null
                ? renderConfig.visualMode
                : GpuParticleVisualMode.OctahedronMesh;

            if (particleMesh != null &&
                particleMesh != _generatedParticleMesh)
            {
                return;
            }

            if (_generatedParticleMesh != null &&
                _generatedVisualMode == mode)
            {
                particleMesh = _generatedParticleMesh;
                return;
            }

            ReleaseGeneratedMesh();

            _generatedVisualMode = mode;
            _generatedParticleMesh =
                mode == GpuParticleVisualMode.CameraFacingSplat
                    ? CreateCameraFacingQuadMesh()
                    : CreateOctahedronMesh();
            particleMesh = _generatedParticleMesh;
        }

        private void ReleaseGeneratedMesh()
        {
            if (_generatedParticleMesh == null)
                return;

            if (Application.isPlaying)
                Destroy(_generatedParticleMesh);
            else
                DestroyImmediate(_generatedParticleMesh);

            _generatedParticleMesh = null;
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

        private Mesh CreateCameraFacingQuadMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(-1, -1, 0),
                new Vector3(-1,  1, 0),
                new Vector3( 1,  1, 0),
                new Vector3( 1, -1, 0)
            };

            Vector3[] normals =
            {
                Vector3.back,
                Vector3.back,
                Vector3.back,
                Vector3.back
            };

            int[] triangles =
            {
                0, 1, 2,
                0, 2, 3
            };

            Mesh mesh = new Mesh
            {
                name = "GPU Paint Particle Camera Facing Splat"
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
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

        private void OnValidate()
        {
            if (!Application.isPlaying)
                return;

            if (particleMesh == _generatedParticleMesh)
                particleMesh = null;
        }
    }
}
