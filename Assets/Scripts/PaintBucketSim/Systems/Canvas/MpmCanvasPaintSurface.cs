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
                        absorptionRate = 0.82f,
                        spreadFactor = 0.72f,
                        dripFactor = 0.10f,
                        dryingRate = 0.45f,
                        wetnessRetention = 0.45f,
                        roughness = 0.75f,
                        splatSharpness = 3.2f,
                        glossResponse = 0.08f
                    };

                case MpmCanvasSurfaceMaterialPreset.Fabric:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.8f,
                        spreadFactor = 0.7f,
                        dripFactor = 0.12f,
                        dryingRate = 0.32f,
                        wetnessRetention = 0.50f,
                        roughness = 0.88f,
                        splatSharpness = 4f,
                        glossResponse = 0.05f
                    };

                case MpmCanvasSurfaceMaterialPreset.Wood:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.38f,
                        spreadFactor = 0.86f,
                        dripFactor = 0.28f,
                        dryingRate = 0.22f,
                        wetnessRetention = 0.82f,
                        roughness = 0.55f,
                        splatSharpness = 3.8f,
                        glossResponse = 0.18f
                    };

                case MpmCanvasSurfaceMaterialPreset.Glass:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.03f,
                        spreadFactor = 1.55f,
                        dripFactor = 1.35f,
                        dryingRate = 0.08f,
                        wetnessRetention = 1.45f,
                        roughness = 0.06f,
                        splatSharpness = 5.8f,
                        glossResponse = 0.82f
                    };

                case MpmCanvasSurfaceMaterialPreset.Metal:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.04f,
                        spreadFactor = 1.28f,
                        dripFactor = 1.05f,
                        dryingRate = 0.10f,
                        wetnessRetention = 1.25f,
                        roughness = 0.18f,
                        splatSharpness = 5.0f,
                        glossResponse = 0.70f
                    };

                case MpmCanvasSurfaceMaterialPreset.Plastic:
                    return new MpmCanvasSurfaceMaterialSettings
                    {
                        absorptionRate = 0.08f,
                        spreadFactor = 1.18f,
                        dripFactor = 0.80f,
                        dryingRate = 0.12f,
                        wetnessRetention = 1.05f,
                        roughness = 0.24f,
                        splatSharpness = 4.6f,
                        glossResponse = 0.48f
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

        [Header("Debug")]
        [SerializeField] private bool drawBoundsGizmo = true;
        [SerializeField] private bool drawAxesGizmo = true;
        [SerializeField] private Color boundsGizmoColor = new Color(0.1f, 0.85f, 1.0f, 1.0f);

        private readonly MpmPaintFilmGrid _filmGrid = new MpmPaintFilmGrid();
        private RenderTexture _paintTexture;
        private MaterialPropertyBlock _propertyBlock;

        private bool _hasCommittedTransform;
        private Vector3 _committedPosition;
        private Quaternion _committedRotation;

        private int _frameStateFrame = -1;
        private int _committedFrame = -1;
        private MpmCanvasSurfaceFrame _frame;

        public MpmPaintFilmGrid FilmGrid => _filmGrid;
        public RenderTexture PaintTexture => _paintTexture;
        public Color BackgroundColor => backgroundColor;
        public Vector2Int Resolution => resolution;
        public float WidthMeters => widthMeters;
        public float HeightMeters => heightMeters;
        public bool FlipU => flipU;
        public bool FlipV => flipV;
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
            ClearRenderTexture();
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

            _propertyBlock.SetColor("_BaseColor", Color.white);
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
                _paintTexture.width == resolution.x &&
                _paintTexture.height == resolution.y)
            {
                return;
            }

            ReleaseRenderTexture();

            _paintTexture = new RenderTexture(
                resolution.x,
                resolution.y,
                0,
                RenderTextureFormat.ARGB32
            )
            {
                name = $"{name}_MpmCanvasPaintTexture",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            _paintTexture.Create();
            ClearRenderTexture();
        }

        private void ClearRenderTexture()
        {
            if (_paintTexture == null)
                return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _paintTexture;
            GL.Clear(false, true, backgroundColor);
            RenderTexture.active = previous;
        }

        private void ReleaseResources()
        {
            _filmGrid.Release();
            ReleaseRenderTexture();
        }

        private void ReleaseRenderTexture()
        {
            if (_paintTexture == null)
                return;

            _paintTexture.Release();

            if (Application.isPlaying)
                Destroy(_paintTexture);
            else
                DestroyImmediate(_paintTexture);

            _paintTexture = null;
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
