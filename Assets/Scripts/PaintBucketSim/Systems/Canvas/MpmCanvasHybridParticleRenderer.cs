using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Canvas
{
    [DefaultExecutionOrder(275)]
    [DisallowMultipleComponent]
    public class MpmCanvasHybridParticleRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MpmCanvasPaintSurface surface;
        [SerializeField] private MpmCanvasDepositor depositor;
        [SerializeField] private Material particleMaterial;

        [Header("Rendering")]
        [SerializeField] private bool renderSurfaceParticles = true;
        [SerializeField] private bool renderDroplets = true;
        [Range(0.1f, 12.0f)] [SerializeField] private float surfaceVisualScale = 0.8f;
        [Range(0.1f, 16.0f)] [SerializeField] private float dropletVisualScale = 1.8f;
        [Min(0.00001f)] [SerializeField] private float minimumWorldRadiusMeters = 0.0012f;
        [Range(0.0f, 1.0f)] [SerializeField] private float surfaceAlpha = 0.35f;
        [Range(0.0f, 1.0f)] [SerializeField] private float dropletAlpha = 0.65f;
        [Range(0.0f, 1.0f)] [SerializeField] private float specularStrength = 0.45f;
        [Range(0.0f, 1.0f)] [SerializeField] private float fresnelStrength = 0.18f;
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows;

        private Mesh _quadMesh;
        private Material _generatedMaterial;
        private MaterialPropertyBlock _mpb;

        private static readonly int ID_HybridParticles = Shader.PropertyToID("_HybridParticles");
        private static readonly int ID_CanvasPosition = Shader.PropertyToID("_CanvasPosition");
        private static readonly int ID_CanvasTangent = Shader.PropertyToID("_CanvasTangent");
        private static readonly int ID_CanvasBitangent = Shader.PropertyToID("_CanvasBitangent");
        private static readonly int ID_CanvasNormal = Shader.PropertyToID("_CanvasNormal");
        private static readonly int ID_CanvasWidth = Shader.PropertyToID("_CanvasWidth");
        private static readonly int ID_CanvasHeight = Shader.PropertyToID("_CanvasHeight");
        private static readonly int ID_GridWidth = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_FlipU = Shader.PropertyToID("_FlipU");
        private static readonly int ID_FlipV = Shader.PropertyToID("_FlipV");
        private static readonly int ID_SwapUV = Shader.PropertyToID("_SwapUV");
        private static readonly int ID_Rotate90 = Shader.PropertyToID("_Rotate90");
        private static readonly int ID_InvertTextureY = Shader.PropertyToID("_InvertTextureY");
        private static readonly int ID_CameraRight = Shader.PropertyToID("_CameraRightWS");
        private static readonly int ID_CameraUp = Shader.PropertyToID("_CameraUpWS");
        private static readonly int ID_CameraForward = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int ID_VisualScale = Shader.PropertyToID("_VisualScale");
        private static readonly int ID_MinWorldRadius = Shader.PropertyToID("_MinWorldRadius");
        private static readonly int ID_GlobalAlpha = Shader.PropertyToID("_GlobalAlpha");
        private static readonly int ID_SpecularStrength = Shader.PropertyToID("_SpecularStrength");
        private static readonly int ID_FresnelStrength = Shader.PropertyToID("_FresnelStrength");

        private void Awake()
        {
            ResolveReferences();
            EnsureResources();
        }

        private void OnEnable()
        {
            EnsureResources();
        }

        private void OnDisable()
        {
            ReleaseGeneratedResources();
        }

        private void OnDestroy()
        {
            ReleaseGeneratedResources();
        }

        private void LateUpdate()
        {
            ResolveReferences();

            if (surface == null ||
                depositor == null ||
                !depositor.HasHybridParticleBuffers)
            {
                return;
            }

            EnsureResources();
            surface.RefreshFrameState(Time.deltaTime);

            if (renderSurfaceParticles)
                DrawBuffer(depositor.SurfaceParticleBuffer, depositor.SurfaceParticleCapacity, surfaceAlpha, surfaceVisualScale);

            if (renderDroplets)
                DrawBuffer(depositor.DropletParticleBuffer, depositor.DropletParticleCapacity, dropletAlpha, dropletVisualScale);
        }

        private void DrawBuffer(GraphicsBuffer buffer, int count, float alpha, float visualScale)
        {
            if (buffer == null || count <= 0 || _quadMesh == null || particleMaterial == null)
                return;

            MpmCanvasSurfaceFrame frame = surface.Frame;
            MpmPaintFilmGrid grid = surface.FilmGrid;

            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetBuffer(ID_HybridParticles, buffer);
            _mpb.SetVector(ID_CanvasPosition, frame.position);
            _mpb.SetVector(ID_CanvasTangent, frame.tangent);
            _mpb.SetVector(ID_CanvasBitangent, frame.bitangent);
            _mpb.SetVector(ID_CanvasNormal, frame.normal);
            _mpb.SetFloat(ID_CanvasWidth, frame.widthMeters);
            _mpb.SetFloat(ID_CanvasHeight, frame.heightMeters);
            _mpb.SetInt(ID_GridWidth, grid.Width);
            _mpb.SetInt(ID_GridHeight, grid.Height);
            _mpb.SetInt(ID_FlipU, surface.FlipU ? 1 : 0);
            _mpb.SetInt(ID_FlipV, surface.FlipV ? 1 : 0);
            _mpb.SetInt(ID_SwapUV, surface.SwapUV ? 1 : 0);
            _mpb.SetInt(ID_Rotate90, surface.Rotate90 ? 1 : 0);
            _mpb.SetInt(ID_InvertTextureY, surface.InvertTextureY ? 1 : 0);
            _mpb.SetFloat(ID_VisualScale, visualScale);
            _mpb.SetFloat(ID_MinWorldRadius, minimumWorldRadiusMeters);
            _mpb.SetFloat(ID_GlobalAlpha, alpha);
            _mpb.SetFloat(ID_SpecularStrength, specularStrength);
            _mpb.SetFloat(ID_FresnelStrength, fresnelStrength);

            Camera camera = Camera.main != null ? Camera.main : Camera.current;
            if (camera != null)
            {
                Transform t = camera.transform;
                _mpb.SetVector(ID_CameraRight, t.right);
                _mpb.SetVector(ID_CameraUp, t.up);
                _mpb.SetVector(ID_CameraForward, -t.forward);
            }
            else
            {
                _mpb.SetVector(ID_CameraRight, Vector3.right);
                _mpb.SetVector(ID_CameraUp, Vector3.up);
                _mpb.SetVector(ID_CameraForward, Vector3.back);
            }

            RenderParams renderParams = new RenderParams(particleMaterial)
            {
                worldBounds = new Bounds(frame.position, new Vector3(frame.widthMeters + 2.0f, frame.heightMeters + 2.0f, 2.0f)),
                matProps = _mpb,
                shadowCastingMode = shadowCastingMode,
                receiveShadows = receiveShadows,
                layer = gameObject.layer
            };

            Graphics.RenderMeshPrimitives(renderParams, _quadMesh, 0, count);
        }

        private void ResolveReferences()
        {
            if (surface == null)
                surface = GetComponent<MpmCanvasPaintSurface>();

            if (surface == null)
                surface = FindAnyObjectByType<MpmCanvasPaintSurface>();

            if (depositor == null)
                depositor = GetComponent<MpmCanvasDepositor>();

            if (depositor == null)
                depositor = FindAnyObjectByType<MpmCanvasDepositor>();
        }

        private void EnsureResources()
        {
            if (_quadMesh == null)
                _quadMesh = CreateQuadMesh();

            if (particleMaterial == null)
                particleMaterial = CreateDefaultMaterial();

            _mpb ??= new MaterialPropertyBlock();
        }

        private Material CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("PaintBucketSim/MPM Canvas Hybrid Particles URP");
            if (shader == null)
                return null;

            _generatedMaterial = new Material(shader)
            {
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            return _generatedMaterial;
        }

        private Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "MPM Canvas Hybrid Particle Quad",
                hideFlags = HideFlags.DontSave
            };

            mesh.vertices = new[]
            {
                new Vector3(-1.0f, -1.0f, 0.0f),
                new Vector3(-1.0f,  1.0f, 0.0f),
                new Vector3( 1.0f,  1.0f, 0.0f),
                new Vector3( 1.0f, -1.0f, 0.0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ReleaseGeneratedResources()
        {
            if (_quadMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_quadMesh);
                else
                    DestroyImmediate(_quadMesh);

                _quadMesh = null;
            }

            if (_generatedMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_generatedMaterial);
                else
                    DestroyImmediate(_generatedMaterial);

                _generatedMaterial = null;
            }

            _mpb = null;
        }

        private void OnValidate()
        {
            surfaceVisualScale = Mathf.Clamp(surfaceVisualScale, 0.1f, 12.0f);
            dropletVisualScale = Mathf.Clamp(dropletVisualScale, 0.1f, 16.0f);
            minimumWorldRadiusMeters = Mathf.Max(0.00001f, minimumWorldRadiusMeters);
            surfaceAlpha = Mathf.Clamp01(surfaceAlpha);
            dropletAlpha = Mathf.Clamp01(dropletAlpha);
            specularStrength = Mathf.Clamp01(specularStrength);
            fresnelStrength = Mathf.Clamp01(fresnelStrength);
        }
    }
}
