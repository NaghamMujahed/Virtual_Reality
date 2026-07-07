using PaintBucketSim.Configs;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Fluid
{
    /// <summary>
    /// Screen-space fluid rendering (G32) — the WebGPU-Ocean style MLS-MPM liquid
    /// surface. Renders the GPU particles as sphere imposters into an off-screen
    /// eye-depth target, smooths it with a bilateral filter, accumulates a
    /// thickness target, then composites a shaded liquid surface (normal from
    /// depth, Fresnel, screen-space refraction, Beer-Lambert absorption, specular)
    /// over the scene as a transparent fullscreen quad.
    ///
    /// Off-screen passes run in the camera's beginCameraRendering callback via
    /// Graphics.ExecuteCommandBuffer; the composite rides URP's transparent pass
    /// so it can read _CameraOpaqueTexture / _CameraDepthTexture without a
    /// ScriptableRendererFeature. Activated by
    /// GpuParticleRenderConfig.fluidRenderMode == ScreenSpaceFluid.
    /// </summary>
    [DefaultExecutionOrder(3100)]
    public class ScreenSpaceFluidRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GpuFluidBufferSet gpuBufferSet;
        [SerializeField] private GpuParticleRenderConfig renderConfig;
        [SerializeField] private BucketSystem bucketSystem;

        [Header("Shaders (auto-resolved by name if empty)")]
        [SerializeField] private Shader imposterShader;
        [SerializeField] private Shader blurShader;
        [SerializeField] private Shader compositeShader;

        private const string ImposterShaderName = "PaintBucketSim/Fluid Particle Imposter";
        private const string BlurShaderName = "PaintBucketSim/Fluid Depth Blur";
        private const string CompositeShaderName = "PaintBucketSim/Fluid Composite";
        private const float BackgroundDepth = 1.0e9f;

        private Material _imposterMat;
        private Material _blurMat;
        private Material _compositeMat;
        private Mesh _quadMesh;

        private GraphicsBuffer _argsBuffer;
        private readonly uint[] _args = new uint[5];

        private RenderTexture _depthRT;
        private RenderTexture _blurRT;
        private RenderTexture _thicknessRT;
        private int _rtWidth;
        private int _rtHeight;

        private CommandBuffer _cmd;
        private GameObject _compositeObject;
        private MeshRenderer _compositeRenderer;
        private bool _callbackRegistered;
        private int _effectiveStride = 1;

        // Global / material property ids.
        private static readonly int ID_ParticlePositionRadius = Shader.PropertyToID("_ParticlePositionRadius");
        private static readonly int ID_ParticleColor = Shader.PropertyToID("_ParticleColor");
        private static readonly int ID_ParticleStateAgeId = Shader.PropertyToID("_ParticleStateAgeId");
        private static readonly int ID_FluidCamRight = Shader.PropertyToID("_FluidCamRight");
        private static readonly int ID_FluidCamUp = Shader.PropertyToID("_FluidCamUp");
        private static readonly int ID_FluidView = Shader.PropertyToID("_FluidView");
        private static readonly int ID_FluidViewProj = Shader.PropertyToID("_FluidViewProj");
        private static readonly int ID_FluidParticleScale = Shader.PropertyToID("_FluidParticleScale");
        private static readonly int ID_UsePerParticleColor = Shader.PropertyToID("_UsePerParticleColor");
        private static readonly int ID_FallbackColor = Shader.PropertyToID("_FallbackColor");
        private static readonly int ID_HideCanvasAndLostParticles = Shader.PropertyToID("_HideCanvasAndLostParticles");
        private static readonly int ID_UseBucketLocalParticles = Shader.PropertyToID("_UseBucketLocalParticles");
        private static readonly int ID_BucketLocalToWorld = Shader.PropertyToID("_BucketLocalToWorld");
        private static readonly int ID_ParticleIndexStride = Shader.PropertyToID("_ParticleIndexStride");
        private static readonly int ID_ParticleCount = Shader.PropertyToID("_ParticleCount");
        private static readonly int ID_FluidThicknessPerParticle = Shader.PropertyToID("_FluidThicknessPerParticle");
        private static readonly int ID_FluidUseParticleColor = Shader.PropertyToID("_FluidUseParticleColor");
        private static readonly int ID_FluidDeepColor = Shader.PropertyToID("_FluidDeepColor");

        private static readonly int ID_BlurSource = Shader.PropertyToID("_BlurSource");
        private static readonly int ID_BlurTexelDir = Shader.PropertyToID("_BlurTexelDir");
        private static readonly int ID_BlurDepthFalloff = Shader.PropertyToID("_BlurDepthFalloff");
        private static readonly int ID_BlurRadiusTaps = Shader.PropertyToID("_BlurRadiusTaps");
        private static readonly int ID_FluidBackgroundDepth = Shader.PropertyToID("_FluidBackgroundDepth");

        private static readonly int ID_FluidDepthTex = Shader.PropertyToID("_FluidDepthTex");
        private static readonly int ID_FluidThicknessTex = Shader.PropertyToID("_FluidThicknessTex");
        private static readonly int ID_FluidTexelSize = Shader.PropertyToID("_FluidTexelSize");
        private static readonly int ID_FluidProj00 = Shader.PropertyToID("_FluidProj00");
        private static readonly int ID_FluidProj11 = Shader.PropertyToID("_FluidProj11");
        private static readonly int ID_FluidNormalYSign = Shader.PropertyToID("_FluidNormalYSign");
        private static readonly int ID_FluidCameraToWorld = Shader.PropertyToID("_FluidCameraToWorld");
        private static readonly int ID_FluidRefractionStrength = Shader.PropertyToID("_FluidRefractionStrength");
        private static readonly int ID_FluidAbsorption = Shader.PropertyToID("_FluidAbsorption");
        private static readonly int ID_FluidOpacity = Shader.PropertyToID("_FluidOpacity");
        private static readonly int ID_FluidFlipY = Shader.PropertyToID("_FluidFlipY");
        private static readonly int ID_FluidFresnelF0 = Shader.PropertyToID("_FluidFresnelF0");
        private static readonly int ID_FluidReflectionStrength = Shader.PropertyToID("_FluidReflectionStrength");
        private static readonly int ID_FluidSpecularStrength = Shader.PropertyToID("_FluidSpecularStrength");
        private static readonly int ID_FluidSpecularPower = Shader.PropertyToID("_FluidSpecularPower");
        private static readonly int ID_FluidLightDir = Shader.PropertyToID("_FluidLightDir");

        public bool IsActiveMode =>
            renderConfig != null &&
            renderConfig.enableGpuIndirectRendering &&
            renderConfig.fluidRenderMode == FluidRenderMode.ScreenSpaceFluid;

        private void Awake()
        {
            if (gpuBufferSet == null)
                gpuBufferSet = FindAnyObjectByType<GpuFluidBufferSet>();
            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            ResolveShaders();
            EnsureResources();
        }

        /// <summary>
        /// Injects shared references. Used when the component is auto-attached by
        /// <see cref="GpuParticleIndirectRenderer"/> so the config ScriptableObject
        /// (which is not discoverable via FindAnyObjectByType) is wired up.
        /// </summary>
        public void Configure(
            GpuFluidBufferSet buffers,
            GpuParticleRenderConfig config,
            BucketSystem bucket)
        {
            if (buffers != null)
                gpuBufferSet = buffers;
            if (config != null)
                renderConfig = config;
            if (bucket != null)
                bucketSystem = bucket;
        }

        private void OnEnable()
        {
            if (!_callbackRegistered)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                _callbackRegistered = true;
            }
        }

        private void OnDisable()
        {
            if (_callbackRegistered)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                _callbackRegistered = false;
            }

            SetCompositeVisible(false);
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void ResolveShaders()
        {
            if (imposterShader == null)
                imposterShader = Shader.Find(ImposterShaderName);
            if (blurShader == null)
                blurShader = Shader.Find(BlurShaderName);
            if (compositeShader == null)
                compositeShader = Shader.Find(CompositeShaderName);
        }

        private void EnsureResources()
        {
            if (_imposterMat == null && imposterShader != null)
                _imposterMat = new Material(imposterShader) { name = "FluidImposter (runtime)" };
            if (_blurMat == null && blurShader != null)
                _blurMat = new Material(blurShader) { name = "FluidDepthBlur (runtime)" };
            if (_compositeMat == null && compositeShader != null)
                _compositeMat = new Material(compositeShader) { name = "FluidComposite (runtime)" };

            if (_quadMesh == null)
                _quadMesh = CreateUnitQuad();

            if (_argsBuffer == null)
            {
                _argsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments,
                    1,
                    sizeof(uint) * 5);
            }

            if (_cmd == null)
                _cmd = new CommandBuffer { name = "ScreenSpaceFluid" };

            EnsureCompositeObject();
        }

        private void EnsureCompositeObject()
        {
            if (_compositeObject != null || _compositeMat == null || _quadMesh == null)
                return;

            _compositeObject = new GameObject("__ScreenSpaceFluidComposite")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _compositeObject.transform.SetParent(transform, false);

            MeshFilter filter = _compositeObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _quadMesh;

            _compositeRenderer = _compositeObject.AddComponent<MeshRenderer>();
            _compositeRenderer.sharedMaterial = _compositeMat;
            _compositeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _compositeRenderer.receiveShadows = false;
            _compositeRenderer.lightProbeUsage = LightProbeUsage.Off;
            _compositeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _compositeRenderer.allowOcclusionWhenDynamic = false;
            _compositeRenderer.enabled = false;
        }

        private void SetCompositeVisible(bool visible)
        {
            if (_compositeRenderer != null)
                _compositeRenderer.enabled = visible;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Game, VR and Scene-view cameras all get the fluid surface. Each
            // camera's callback sets its own matrices/targets/globals right before
            // that camera renders, so both game and scene views are correct.
            // Reflection/preview cameras are skipped.
            bool eligibleCamera =
                camera.cameraType == CameraType.Game ||
                camera.cameraType == CameraType.VR ||
                camera.cameraType == CameraType.SceneView;

            if (!eligibleCamera || !IsActiveMode || !IsReady())
            {
                SetCompositeVisible(false);
                return;
            }

            EnsureResources();

            if (_imposterMat == null || _blurMat == null || _compositeMat == null)
            {
                SetCompositeVisible(false);
                return;
            }

            if (!EnsureTargets(camera))
            {
                SetCompositeVisible(false);
                return;
            }

            int visibleCount = UpdateArgs();
            if (visibleCount <= 0)
            {
                SetCompositeVisible(false);
                return;
            }

            RecordOffscreenPasses(camera, visibleCount);
            Graphics.ExecuteCommandBuffer(_cmd);

            SetCompositeGlobals(camera);
            SetCompositeVisible(true);
        }

        private bool IsReady()
        {
            return gpuBufferSet != null &&
                   gpuBufferSet.IsInitialized &&
                   gpuBufferSet.UploadedParticleCount > 0 &&
                   gpuBufferSet.PositionRadiusBuffer != null &&
                   gpuBufferSet.ColorBuffer != null &&
                   gpuBufferSet.StateAgeIdBuffer != null;
        }

        private bool EnsureTargets(Camera camera)
        {
            int divisor = renderConfig != null
                ? Mathf.Clamp(renderConfig.fluidResolutionDivisor, 1, 4)
                : 1;

            int width = Mathf.Max(1, camera.pixelWidth / divisor);
            int height = Mathf.Max(1, camera.pixelHeight / divisor);

            if (_depthRT != null && (_rtWidth != width || _rtHeight != height))
                ReleaseTargets();

            if (_depthRT == null)
            {
                _depthRT = CreateTarget(width, height, RenderTextureFormat.RFloat, FilterMode.Point);
                _blurRT = CreateTarget(width, height, RenderTextureFormat.RFloat, FilterMode.Point);
                _thicknessRT = CreateTarget(width, height, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
                _rtWidth = width;
                _rtHeight = height;
            }

            return _depthRT != null && _blurRT != null && _thicknessRT != null;
        }

        private static RenderTexture CreateTarget(
            int width,
            int height,
            RenderTextureFormat format,
            FilterMode filter)
        {
            var rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = "FluidTarget",
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
            return rt;
        }

        private int UpdateArgs()
        {
            int uploaded = Mathf.Max(0, gpuBufferSet.UploadedParticleCount);
            int stride = renderConfig != null ? Mathf.Max(1, renderConfig.renderStride) : 1;
            int visible = Mathf.CeilToInt(uploaded / (float)stride);

            if (renderConfig != null && visible > renderConfig.maxRenderedParticles)
            {
                stride = Mathf.Max(
                    stride,
                    Mathf.CeilToInt(uploaded / (float)renderConfig.maxRenderedParticles));
                visible = Mathf.CeilToInt(uploaded / (float)stride);
            }

            _effectiveStride = stride;

            _args[0] = _quadMesh.GetIndexCount(0);
            _args[1] = (uint)Mathf.Max(0, visible);
            _args[2] = _quadMesh.GetIndexStart(0);
            _args[3] = _quadMesh.GetBaseVertex(0);
            _args[4] = 0;
            _argsBuffer.SetData(_args);

            return visible;
        }

        private void RecordOffscreenPasses(Camera camera, int visibleCount)
        {
            _cmd.Clear();

            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            _cmd.SetViewProjectionMatrices(view, gpuProj);

            // Pass the matrices explicitly too: UNITY_MATRIX_V/VP are unreliable
            // under Graphics.ExecuteCommandBuffer, so the imposter shader uses
            // these instead (otherwise the surface renders camera-locked).
            _cmd.SetGlobalMatrix(ID_FluidView, view);
            _cmd.SetGlobalMatrix(ID_FluidViewProj, gpuProj * view);

            // Camera basis in world space for the imposter billboards.
            Transform camT = camera.transform;
            _cmd.SetGlobalVector(ID_FluidCamRight, camT.right);
            _cmd.SetGlobalVector(ID_FluidCamUp, camT.up);

            // Particle buffers + imposter parameters.
            _cmd.SetGlobalBuffer(ID_ParticlePositionRadius, gpuBufferSet.PositionRadiusBuffer);
            _cmd.SetGlobalBuffer(ID_ParticleColor, gpuBufferSet.ColorBuffer);
            _cmd.SetGlobalBuffer(ID_ParticleStateAgeId, gpuBufferSet.StateAgeIdBuffer);
            _cmd.SetGlobalFloat(ID_FluidParticleScale, renderConfig.fluidParticleScale);
            _cmd.SetGlobalFloat(ID_UsePerParticleColor, renderConfig.usePerParticleColor ? 1f : 0f);
            _cmd.SetGlobalVector(ID_FallbackColor, renderConfig.fallbackColor);
            _cmd.SetGlobalFloat(ID_HideCanvasAndLostParticles, renderConfig.hideCanvasAndLostParticles ? 1f : 0f);
            _cmd.SetGlobalFloat(ID_FluidThicknessPerParticle, renderConfig.fluidThicknessPerParticle);
            _cmd.SetGlobalFloat(ID_FluidUseParticleColor, renderConfig.fluidUseParticleColor ? 1f : 0f);
            _cmd.SetGlobalColor(ID_FluidDeepColor, renderConfig.fluidDeepColor);
            _cmd.SetGlobalInt(ID_ParticleIndexStride, _effectiveStride);
            _cmd.SetGlobalInt(ID_ParticleCount, gpuBufferSet.UploadedParticleCount);

            bool useBucketLocal =
                gpuBufferSet.MpmParticlesUseBucketLocalSpace &&
                bucketSystem != null &&
                bucketSystem.IsInitialized;
            _cmd.SetGlobalFloat(ID_UseBucketLocalParticles, useBucketLocal ? 1f : 0f);
            if (useBucketLocal)
            {
                var state = bucketSystem.State;
                _cmd.SetGlobalMatrix(
                    ID_BucketLocalToWorld,
                    Matrix4x4.TRS(state.position, state.rotation, Vector3.one));
            }
            else
            {
                _cmd.SetGlobalMatrix(ID_BucketLocalToWorld, Matrix4x4.identity);
            }

            // Pass 0 — nearest-surface eye depth (Min blend).
            _cmd.SetRenderTarget(_depthRT);
            _cmd.ClearRenderTarget(false, true, new Color(BackgroundDepth, BackgroundDepth, BackgroundDepth, BackgroundDepth));
            _cmd.DrawMeshInstancedIndirect(_quadMesh, 0, _imposterMat, 0, _argsBuffer);

            // Pass 1 — additive thickness + weighted colour.
            _cmd.SetRenderTarget(_thicknessRT);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            _cmd.DrawMeshInstancedIndirect(_quadMesh, 0, _imposterMat, 1, _argsBuffer);

            // Bilateral depth smoothing (separable, ping-pong depth<->blur).
            int iterations = renderConfig != null
                ? Mathf.Clamp(renderConfig.fluidSmoothingIterations, 0, 8)
                : 0;
            int taps = renderConfig != null
                ? Mathf.Clamp(Mathf.RoundToInt(renderConfig.fluidBlurRadiusPixels), 1, 16)
                : 4;
            float falloff = renderConfig != null ? renderConfig.fluidBlurDepthFalloff : 12f;

            _cmd.SetGlobalFloat(ID_BlurDepthFalloff, falloff);
            _cmd.SetGlobalInt(ID_BlurRadiusTaps, taps);
            _cmd.SetGlobalFloat(ID_FluidBackgroundDepth, BackgroundDepth);

            float texelX = 1f / _rtWidth;
            float texelY = 1f / _rtHeight;

            for (int i = 0; i < iterations; i++)
            {
                BlurPass(_depthRT, _blurRT, new Vector4(texelX, 0f, 0f, 0f));
                BlurPass(_blurRT, _depthRT, new Vector4(0f, texelY, 0f, 0f));
            }
        }

        private void BlurPass(RenderTexture source, RenderTexture dest, Vector4 texelDir)
        {
            _cmd.SetGlobalTexture(ID_BlurSource, source);
            _cmd.SetGlobalVector(ID_BlurTexelDir, texelDir);
            _cmd.SetRenderTarget(dest);
            _cmd.DrawProcedural(Matrix4x4.identity, _blurMat, 0, MeshTopology.Triangles, 3, 1);
        }

        private void SetCompositeGlobals(Camera camera)
        {
            // Smoothed depth ends in _depthRT (even number of ping-pong swaps).
            Shader.SetGlobalTexture(ID_FluidDepthTex, _depthRT);
            Shader.SetGlobalTexture(ID_FluidThicknessTex, _thicknessRT);
            Shader.SetGlobalVector(
                ID_FluidTexelSize,
                new Vector4(1f / _rtWidth, 1f / _rtHeight, _rtWidth, _rtHeight));
            Shader.SetGlobalFloat(ID_FluidBackgroundDepth, BackgroundDepth);

            Matrix4x4 proj = camera.projectionMatrix;
            Shader.SetGlobalFloat(ID_FluidProj00, proj.m00);
            Shader.SetGlobalFloat(ID_FluidProj11, proj.m11);
            // Screen uv (positionCS/_ScreenParams) has its origin at the top on
            // D3D/Metal but the bottom on OpenGL; flip ndc.y so the reconstructed
            // view-space normal points the right way regardless of platform.
            Shader.SetGlobalFloat(
                ID_FluidNormalYSign,
                SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            Shader.SetGlobalMatrix(ID_FluidCameraToWorld, camera.cameraToWorldMatrix);

            Shader.SetGlobalFloat(ID_FluidRefractionStrength, renderConfig.fluidRefractionStrength);
            Shader.SetGlobalFloat(ID_FluidAbsorption, renderConfig.fluidAbsorption);
            Shader.SetGlobalFloat(ID_FluidOpacity, renderConfig.fluidOpacity);
            Shader.SetGlobalFloat(ID_FluidFlipY, renderConfig.fluidFlipVertical ? 1f : 0f);
            Shader.SetGlobalFloat(ID_FluidFresnelF0, renderConfig.fluidFresnelF0);
            Shader.SetGlobalFloat(ID_FluidReflectionStrength, renderConfig.fluidReflectionStrength);
            Shader.SetGlobalFloat(ID_FluidSpecularStrength, renderConfig.fluidSpecularStrength);
            Shader.SetGlobalFloat(ID_FluidSpecularPower, renderConfig.fluidSpecularPower);
            Shader.SetGlobalFloat(ID_FluidUseParticleColor, renderConfig.fluidUseParticleColor ? 1f : 0f);
            Shader.SetGlobalColor(ID_FluidDeepColor, renderConfig.fluidDeepColor);

            Vector3 lightDir = ResolveLightDirection();
            Shader.SetGlobalVector(ID_FluidLightDir, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
        }

        private Vector3 ResolveLightDirection()
        {
            if (RenderSettings.sun != null)
                return -RenderSettings.sun.transform.forward;

            Vector3 configured = renderConfig != null
                ? renderConfig.fluidLightDirection
                : new Vector3(0.35f, 0.85f, 0.25f);

            return configured.sqrMagnitude > 1e-4f
                ? configured.normalized
                : new Vector3(0.35f, 0.85f, 0.25f).normalized;
        }

        private static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "FluidUnitQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(-1f,  1f, 0f),
                new Vector3( 1f,  1f, 0f),
                new Vector3( 1f, -1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            // Huge bounds so the composite MeshRenderer is never frustum-culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.0e9f);
            return mesh;
        }

        private void ReleaseTargets()
        {
            ReleaseTarget(ref _depthRT);
            ReleaseTarget(ref _blurRT);
            ReleaseTarget(ref _thicknessRT);
            _rtWidth = 0;
            _rtHeight = 0;
        }

        private static void ReleaseTarget(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            if (Application.isPlaying)
                Destroy(rt);
            else
                DestroyImmediate(rt);
            rt = null;
        }

        private void ReleaseResources()
        {
            ReleaseTargets();

            if (_argsBuffer != null)
            {
                _argsBuffer.Release();
                _argsBuffer = null;
            }

            _cmd?.Release();
            _cmd = null;

            DestroyImmediateSafe(_imposterMat); _imposterMat = null;
            DestroyImmediateSafe(_blurMat); _blurMat = null;
            DestroyImmediateSafe(_compositeMat); _compositeMat = null;
            DestroyImmediateSafe(_quadMesh); _quadMesh = null;

            if (_compositeObject != null)
            {
                if (Application.isPlaying)
                    Destroy(_compositeObject);
                else
                    DestroyImmediate(_compositeObject);
                _compositeObject = null;
                _compositeRenderer = null;
            }
        }

        private static void DestroyImmediateSafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
