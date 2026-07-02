using System;
using UnityEngine;

namespace PaintBucketSim.Systems.Canvas
{
    public enum MpmCanvasSurfaceAxisPreset
    {
        QuadXYForward = 0,
        PlaneXZUp = 1,
        Custom = 2
    }

    public enum MpmCanvasSurfaceMaterialPreset
    {
        Custom = 0,
        Paper = 1,
        Fabric = 2,
        Wood = 3,
        Glass = 4,
        Metal = 5,
        Plastic = 6
    }

    [Serializable]
    public struct MpmCanvasSurfaceMaterialSettings
    {
        [Range(0.0f, 1.0f)] public float absorptionRate;
        [Range(0.0f, 3.0f)] public float spreadFactor;
        [Range(0.0f, 3.0f)] public float dripFactor;
        [Range(0.0f, 2.0f)] public float dryingRate;
        [Range(0.05f, 2.0f)] public float wetnessRetention;
        [Range(0.0f, 1.0f)] public float roughness;
        [Range(0.25f, 8.0f)] public float splatSharpness;
        [Range(0.0f, 1.0f)] public float glossResponse;

        public static MpmCanvasSurfaceMaterialSettings FromPreset(
            MpmCanvasSurfaceMaterialPreset preset)
        {
            switch (preset)
            {
                case MpmCanvasSurfaceMaterialPreset.Paper:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.86f,
                        spreadFactor = 0.62f,
                        dripFactor = 0.06f,
                        dryingRate = 0.58f,
                        wetnessRetention = 0.34f,
                        roughness = 0.84f,
                        splatSharpness = 3.9f,
                        glossResponse = 0.05f
                    };

                case MpmCanvasSurfaceMaterialPreset.Fabric:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.82f,
                        spreadFactor = 0.86f,
                        dripFactor = 0.10f,
                        dryingRate = 0.36f,
                        wetnessRetention = 0.44f,
                        roughness = 0.94f,
                        splatSharpness = 4.8f,
                        glossResponse = 0.04f
                    };

                case MpmCanvasSurfaceMaterialPreset.Wood:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.32f,
                        spreadFactor = 0.82f,
                        dripFactor = 0.36f,
                        dryingRate = 0.18f,
                        wetnessRetention = 0.88f,
                        roughness = 0.58f,
                        splatSharpness = 3.5f,
                        glossResponse = 0.20f
                    };

                case MpmCanvasSurfaceMaterialPreset.Glass:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.01f,
                        spreadFactor = 1.90f,
                        dripFactor = 1.65f,
                        dryingRate = 0.05f,
                        wetnessRetention = 1.65f,
                        roughness = 0.03f,
                        splatSharpness = 6.4f,
                        glossResponse = 0.95f
                    };

                case MpmCanvasSurfaceMaterialPreset.Metal:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.02f,
                        spreadFactor = 1.55f,
                        dripFactor = 1.28f,
                        dryingRate = 0.08f,
                        wetnessRetention = 1.35f,
                        roughness = 0.12f,
                        splatSharpness = 5.5f,
                        glossResponse = 0.82f
                    };

                case MpmCanvasSurfaceMaterialPreset.Plastic:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.06f,
                        spreadFactor = 1.35f,
                        dripFactor = 0.95f,
                        dryingRate = 0.10f,
                        wetnessRetention = 1.18f,
                        roughness = 0.18f,
                        splatSharpness = 4.8f,
                        glossResponse = 0.62f
                    };

                default:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.40f,
                        spreadFactor = 1.0f,
                        dripFactor = 0.35f,
                        dryingRate = 0.20f,
                        wetnessRetention = 0.85f,
                        roughness = 0.45f,
                        splatSharpness = 3.5f,
                        glossResponse = 0.25f
                    };
            }
        }

        public void Sanitize()
        {
            absorptionRate = Mathf.Clamp01(absorptionRate);
            spreadFactor = Mathf.Clamp(spreadFactor, 0.0f, 3.0f);
            dripFactor = Mathf.Clamp(dripFactor, 0.0f, 3.0f);
            dryingRate = Mathf.Clamp(dryingRate, 0.0f, 2.0f);
            wetnessRetention = Mathf.Clamp(wetnessRetention, 0.05f, 2.0f);
            roughness = Mathf.Clamp01(roughness);
            splatSharpness = Mathf.Clamp(splatSharpness, 0.25f, 8.0f);
            glossResponse = Mathf.Clamp01(glossResponse);
        }
    }

    public struct MpmCanvasSurfaceFrame
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 tangent;
        public Vector3 bitangent;

        public Vector3 previousPosition;
        public Vector3 previousNormal;
        public Vector3 previousTangent;
        public Vector3 previousBitangent;

        public Vector3 linearVelocity;
        public Vector3 angularVelocity;

        public float widthMeters;
        public float heightMeters;
    }

    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    public class MpmCanvasPaintSurface : MonoBehaviour
    {
        [Header("Surface Transform")]
        [SerializeField] private Transform surfaceTransform;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private MpmCanvasSurfaceAxisPreset axisPreset =
            MpmCanvasSurfaceAxisPreset.QuadXYForward;
        [SerializeField] private Vector3 customLocalTangent = Vector3.right;
        [SerializeField] private Vector3 customLocalBitangent = Vector3.up;

        [Header("Physical Size")]
        [Min(0.01f)] [SerializeField] private float widthMeters = 1.2f;
        [Min(0.01f)] [SerializeField] private float heightMeters = 0.8f;
        [SerializeField] private Vector2Int resolution = new Vector2Int(512, 512);

        [Header("Texture UV Mapping")]
        [SerializeField] private bool flipU;
        [SerializeField] private bool flipV;
        [SerializeField] private bool swapUV;
        [SerializeField] private bool rotate90;
        [SerializeField] private bool invertTextureY;

        [Header("Surface Material")]
        [SerializeField] private MpmCanvasSurfaceMaterialPreset materialPreset =
            MpmCanvasSurfaceMaterialPreset.Paper;
        [SerializeField] private bool autoApplyPreset = true;
        [SerializeField] private MpmCanvasSurfaceMaterialSettings materialSettings =
            MpmCanvasSurfaceMaterialSettings.FromPreset(
                MpmCanvasSurfaceMaterialPreset.Paper);

        [Header("Render Target")]
        [SerializeField] private Color backgroundColor = new Color(0.92f, 0.90f, 0.84f, 1.0f);
        [SerializeField] private bool applyTextureToTargetRenderer = true;
        [SerializeField] private string baseMapProperty = "_BaseMap";
        [SerializeField] private string mainTextureProperty = "_MainTex";
        [SerializeField] private string normalMapProperty = "_BumpMap";
        [SerializeField] private string maskMapProperty = "_MetallicGlossMap";
        [SerializeField] private string heightMapProperty = "_ParallaxMap";
        [SerializeField] private string customMaterialMapProperty = "_MpmPaintMaterialMap";

        [Header("Debug")]
        [SerializeField] private bool drawBoundsGizmo = true;
        [SerializeField] private bool drawAxesGizmo = true;
        [SerializeField] private Color boundsGizmoColor = new Color(0.1f, 0.85f, 1.0f, 1.0f);

        private readonly MpmPaintFilmGrid _filmGrid = new MpmPaintFilmGrid();
        private RenderTexture _paintTexture;
        private RenderTexture _normalTexture;
        private RenderTexture _materialTexture;
        private RenderTexture _heightTexture;
        private MaterialPropertyBlock _propertyBlock;

        private bool _hasCommittedTransform;
        private Vector3 _committedPosition;
        private Quaternion _committedRotation;

        private int _frameStateFrame = -1;
        private int _committedFrame = -1;
        private MpmCanvasSurfaceFrame _frame;

        public MpmPaintFilmGrid FilmGrid => _filmGrid;
        public RenderTexture PaintTexture => _paintTexture;
        public RenderTexture NormalTexture => _normalTexture;
        public RenderTexture MaterialTexture => _materialTexture;
        public RenderTexture HeightTexture => _heightTexture;
        public Color BackgroundColor => backgroundColor;
        public Vector2Int Resolution => resolution;
        public float WidthMeters => widthMeters;
        public float HeightMeters => heightMeters;
        public bool FlipU => flipU;
        public bool FlipV => flipV;
        public bool SwapUV => swapUV;
        public bool Rotate90 => rotate90;
        public bool InvertTextureY => invertTextureY;
        public MpmCanvasSurfaceMaterialSettings MaterialSettings => materialSettings;
        public MpmCanvasSurfaceFrame Frame => _frame;
        public event Action SurfaceReset;

        private Transform ResolvedTransform =>
            surfaceTransform != null ? surfaceTransform : transform;

        private void Awake()
        {
            ResolveReferences();
            EnsureResources();
            InitializeCommittedTransform();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureResources();
            InitializeCommittedTransform();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void LateUpdate()
        {
            RefreshFrameState(Time.deltaTime);
        }

        public void EnsureResources()
        {
            SanitizeSerializedValues();

            _filmGrid.Ensure(resolution.x, resolution.y);
            EnsureRenderTexture();
            ApplyOutputTexture();
        }

        [ContextMenu("Reset MPM Canvas Surface")]
        public void ResetSurface()
        {
            EnsureResources();
            _filmGrid.Clear();
            ClearRenderTextures();
            SurfaceReset?.Invoke();
        }

        public void RefreshFrameState(float dt)
        {
            EnsureResources();

            if (_frameStateFrame == Time.frameCount)
                return;

            Transform t = ResolvedTransform;
            Quaternion currentRotation = t.rotation;
            Vector3 currentPosition = t.position;

            if (!_hasCommittedTransform)
            {
                _committedPosition = currentPosition;
                _committedRotation = currentRotation;
                _hasCommittedTransform = true;
            }

            BuildAxes(currentRotation, out Vector3 tangent, out Vector3 bitangent, out Vector3 normal);
            BuildAxes(
                _committedRotation,
                out Vector3 previousTangent,
                out Vector3 previousBitangent,
                out Vector3 previousNormal
            );

            float safeDt = Mathf.Max(dt, 1e-5f);
            _frame = new MpmCanvasSurfaceFrame
            {
                position = currentPosition,
                tangent = tangent,
                bitangent = bitangent,
                normal = normal,

                previousPosition = _committedPosition,
                previousTangent = previousTangent,
                previousBitangent = previousBitangent,
                previousNormal = previousNormal,

                linearVelocity = (currentPosition - _committedPosition) / safeDt,
                angularVelocity = ComputeAngularVelocity(
                    _committedRotation,
                    currentRotation,
                    safeDt
                ),

                widthMeters = widthMeters,
                heightMeters = heightMeters
            };

            _frameStateFrame = Time.frameCount;
        }

        public void CommitFrameState()
        {
            if (_committedFrame == Time.frameCount)
                return;

            Transform t = ResolvedTransform;
            _committedPosition = t.position;
            _committedRotation = t.rotation;
            _hasCommittedTransform = true;
            _committedFrame = Time.frameCount;
        }

        public void ApplyOutputTexture()
        {
            if (!applyTextureToTargetRenderer ||
                targetRenderer == null ||
                _paintTexture == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_propertyBlock);

            if (!string.IsNullOrEmpty(baseMapProperty))
                _propertyBlock.SetTexture(baseMapProperty, _paintTexture);

            if (!string.IsNullOrEmpty(mainTextureProperty))
                _propertyBlock.SetTexture(mainTextureProperty, _paintTexture);

            if (_normalTexture != null && !string.IsNullOrEmpty(normalMapProperty))
                _propertyBlock.SetTexture(normalMapProperty, _normalTexture);

            if (_materialTexture != null && !string.IsNullOrEmpty(maskMapProperty))
                _propertyBlock.SetTexture(maskMapProperty, _materialTexture);

            if (_heightTexture != null && !string.IsNullOrEmpty(heightMapProperty))
                _propertyBlock.SetTexture(heightMapProperty, _heightTexture);

            if (_materialTexture != null && !string.IsNullOrEmpty(customMaterialMapProperty))
                _propertyBlock.SetTexture(customMaterialMapProperty, _materialTexture);

            _propertyBlock.SetColor("_BaseColor", Color.white);
            _propertyBlock.SetFloat("_Smoothness", Mathf.Lerp(0.18f, 0.92f, materialSettings.glossResponse));
            _propertyBlock.SetFloat("_BumpScale", 1.0f);
            _propertyBlock.SetFloat("_Parallax", 0.018f);
            targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void ResolveReferences()
        {
            if (surfaceTransform == null)
                surfaceTransform = transform;

            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
        }

        private void EnsureRenderTexture()
        {
            if (_paintTexture != null &&
                _normalTexture != null &&
                _materialTexture != null &&
                _heightTexture != null &&
                _paintTexture.width == resolution.x &&
                _paintTexture.height == resolution.y &&
                _normalTexture.width == resolution.x &&
                _normalTexture.height == resolution.y &&
                _materialTexture.width == resolution.x &&
                _materialTexture.height == resolution.y &&
                _heightTexture.width == resolution.x &&
                _heightTexture.height == resolution.y)
            {
                return;
            }

            ReleaseRenderTexture();

            _paintTexture = CreateRenderTexture(
                $"{name}_MpmCanvasPaintTexture",
                RenderTextureFormat.ARGB32,
                FilterMode.Bilinear
            );

            _normalTexture = CreateRenderTexture(
                $"{name}_MpmCanvasNormalTexture",
                RenderTextureFormat.ARGB32,
                FilterMode.Bilinear
            );

            _materialTexture = CreateRenderTexture(
                $"{name}_MpmCanvasMaterialTexture",
                RenderTextureFormat.ARGB32,
                FilterMode.Bilinear
            );

            _heightTexture = CreateRenderTexture(
                $"{name}_MpmCanvasHeightTexture",
                RenderTextureFormat.ARGB32,
                FilterMode.Bilinear
            );

            ClearRenderTextures();
        }

        private RenderTexture CreateRenderTexture(
            string textureName,
            RenderTextureFormat format,
            FilterMode filterMode)
        {
            RenderTexture texture = new RenderTexture(
                resolution.x,
                resolution.y,
                0,
                format
            )
            {
                name = textureName,
                enableRandomWrite = true,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            texture.Create();
            return texture;
        }

        private void ClearRenderTextures()
        {
            ClearRenderTexture(_paintTexture, backgroundColor);
            ClearRenderTexture(_normalTexture, new Color(0.5f, 0.5f, 1.0f, 1.0f));
            ClearRenderTexture(_materialTexture, new Color(0.0f, 0.0f, 1.0f, 0.0f));
            ClearRenderTexture(_heightTexture, Color.black);
        }

        private static void ClearRenderTexture(RenderTexture texture, Color color)
        {
            if (texture == null)
                return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, color);
            RenderTexture.active = previous;
        }

        private void ReleaseResources()
        {
            _filmGrid.Release();
            ReleaseRenderTexture();
        }

        private void ReleaseRenderTexture()
        {
            ReleaseRenderTexture(ref _paintTexture);
            ReleaseRenderTexture(ref _normalTexture);
            ReleaseRenderTexture(ref _materialTexture);
            ReleaseRenderTexture(ref _heightTexture);
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();

            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);

            texture = null;
        }

        private void InitializeCommittedTransform()
        {
            Transform t = ResolvedTransform;
            _committedPosition = t.position;
            _committedRotation = t.rotation;
            _hasCommittedTransform = true;
            _frameStateFrame = -1;
            _committedFrame = -1;
            RefreshFrameState(Time.deltaTime);
        }

        private void BuildAxes(
            Quaternion rotation,
            out Vector3 tangent,
            out Vector3 bitangent,
            out Vector3 normal)
        {
            switch (axisPreset)
            {
                case MpmCanvasSurfaceAxisPreset.PlaneXZUp:
                    tangent = rotation * Vector3.right;
                    bitangent = rotation * Vector3.back;
                    break;

                case MpmCanvasSurfaceAxisPreset.Custom:
                    tangent = rotation * SafeNormalized(customLocalTangent, Vector3.right);
                    bitangent = rotation * SafeNormalized(customLocalBitangent, Vector3.up);
                    break;

                default:
                    tangent = rotation * Vector3.right;
                    bitangent = rotation * Vector3.up;
                    break;
            }

            tangent = SafeNormalized(tangent, Vector3.right);
            bitangent -= tangent * Vector3.Dot(bitangent, tangent);
            bitangent = SafeNormalized(bitangent, Vector3.up);
            normal = SafeNormalized(Vector3.Cross(tangent, bitangent), Vector3.forward);
        }

        private static Vector3 SafeNormalized(Vector3 value, Vector3 fallback)
        {
            float lengthSq = value.sqrMagnitude;
            return lengthSq > 1e-10f ? value / Mathf.Sqrt(lengthSq) : fallback.normalized;
        }

        private static Vector3 ComputeAngularVelocity(
            Quaternion previous,
            Quaternion current,
            float dt)
        {
            Quaternion delta = current * Quaternion.Inverse(previous);
            if (delta.w < 0.0f)
            {
                delta.x = -delta.x;
                delta.y = -delta.y;
                delta.z = -delta.z;
                delta.w = -delta.w;
            }

            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-10f)
                return Vector3.zero;

            if (angleDeg > 180.0f)
                angleDeg -= 360.0f;

            return axis.normalized * (angleDeg * Mathf.Deg2Rad / Mathf.Max(dt, 1e-5f));
        }

        private void OnValidate()
        {
            SanitizeSerializedValues();
        }

        private void SanitizeSerializedValues()
        {
            widthMeters = Mathf.Max(widthMeters, 0.01f);
            heightMeters = Mathf.Max(heightMeters, 0.01f);
            resolution.x = Mathf.Clamp(resolution.x, 16, 4096);
            resolution.y = Mathf.Clamp(resolution.y, 16, 4096);

            if (autoApplyPreset && materialPreset != MpmCanvasSurfaceMaterialPreset.Custom)
                materialSettings = MpmCanvasSurfaceMaterialSettings.FromPreset(materialPreset);

            materialSettings.Sanitize();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawBoundsGizmo && !drawAxesGizmo)
                return;

            SanitizeSerializedValues();
            ResolveReferences();

            Transform t = ResolvedTransform;
            BuildAxes(t.rotation, out Vector3 tangent, out Vector3 bitangent, out Vector3 normal);

            Vector3 center = t.position;
            Vector3 halfU = tangent * (widthMeters * 0.5f);
            Vector3 halfV = bitangent * (heightMeters * 0.5f);

            if (drawBoundsGizmo)
            {
                Gizmos.color = boundsGizmoColor;
                Vector3 a = center - halfU - halfV;
                Vector3 b = center + halfU - halfV;
                Vector3 c = center + halfU + halfV;
                Vector3 d = center - halfU + halfV;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }

            if (drawAxesGizmo)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(center, center + tangent * Mathf.Min(widthMeters, 0.3f));
                Gizmos.color = Color.green;
                Gizmos.DrawLine(center, center + bitangent * Mathf.Min(heightMeters, 0.3f));
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(center, center + normal * 0.25f);
            }
        }
    }
}
