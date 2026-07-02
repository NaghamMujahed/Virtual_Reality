using PaintBucketSim.Systems.Fluid;
using UnityEngine;

namespace PaintBucketSim.Systems.Canvas
{
    public enum MpmPaintFilmDebugView
    {
        Beauty = 0,
        ThicknessHeatmap = 1,
        WetnessHeatmap = 2,
        ImpactCount = 3,
        Uv = 4,
        Flow = 5,
        FlowActivationHeatmap = 6,
        ColorIntegrity = 7
    }

    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public class MpmPaintSurfaceRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MpmCanvasPaintSurface surface;
        [SerializeField] private PaintFluidSystem paintFluidSystem;
        [SerializeField] private ComputeShader paintFilmBakerCompute;
        [SerializeField] private ComputeShader paintFilmEvolverCompute;

        [Header("Baking")]
        [SerializeField] private bool bakeEveryFrame = true;
        [SerializeField] private MpmPaintFilmDebugView debugView = MpmPaintFilmDebugView.Beauty;
        [Min(0.000001f)] [SerializeField] private float maxVisibleThicknessMeters = 0.0015f;
        [Range(0.0f, 1.0f)] [SerializeField] private float wetDarkening = 0.18f;
        [Range(0.0f, 1.0f)] [SerializeField] private float wetGlossBoost = 0.20f;
        [Range(0.0f, 1.0f)] [SerializeField] private float pigmentContrast = 0.22f;
        [Range(0.0f, 8.0f)] [SerializeField] private float normalFromThicknessStrength = 2.4f;
        [Range(0.0f, 0.08f)] [SerializeField] private float visualHeightScaleMeters = 0.018f;

        [Header("Evolution")]
        [SerializeField] private bool enableEvolution = true;
        [SerializeField] private bool enableDiffusion = true;
        [SerializeField] private bool enableDrips = true;
        [Range(0.0f, 2.0f)] [SerializeField] private float evolutionRate = 1.0f;
        [Range(0.0f, 1.0f)] [SerializeField] private float viscosityDamping = 0.55f;
        [Range(0.0f, 4.0f)] [SerializeField] private float momentumFlowFactor = 1.25f;
        [Range(0.0f, 4.0f)] [SerializeField] private float heightGradientFlowFactor = 1.15f;
        [Range(0.0f, 4.0f)] [SerializeField] private float rivuletFactor = 1.2f;
        [Range(0.0f, 4.0f)] [SerializeField] private float edgeDrainFactor = 0.65f;
        [Range(0.0f, 0.01f)] [SerializeField] private float edgeDripThresholdMeters = 0.00065f;
        [Min(0.000001f)] [SerializeField] private float filmFlowStartThicknessMeters = 0.002f;
        [Range(0.0f, 1.0f)] [SerializeField] private float poolHoldFactor = 0.65f;
        [Range(0.0f, 1.0f)] [SerializeField] private float thinFilmMobilityBelowThreshold = 0.12f;
        [Range(0.0f, 2.0f)] [SerializeField] private float gravityBiasInFilmFlow = 0.65f;
        [Range(0.0f, 1.0f)] [SerializeField] private float flowStopThicknessFactor = 0.55f;
        [SerializeField] private Vector3 gravityDirectionWorld = Vector3.down;

        private int _bakeKernel = -1;
        private int _evolveKernel = -1;

        private static readonly int ID_FilmCells = Shader.PropertyToID("_FilmCells");
        private static readonly int ID_SourceFilmCells = Shader.PropertyToID("_SourceFilmCells");
        private static readonly int ID_TargetFilmCells = Shader.PropertyToID("_TargetFilmCells");
        private static readonly int ID_OutputTexture = Shader.PropertyToID("_OutputTexture");
        private static readonly int ID_NormalTexture = Shader.PropertyToID("_NormalTexture");
        private static readonly int ID_MaterialTexture = Shader.PropertyToID("_MaterialTexture");
        private static readonly int ID_HeightTexture = Shader.PropertyToID("_HeightTexture");
        private static readonly int ID_GridWidth = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_ThicknessUnitsPerMeter = Shader.PropertyToID("_ThicknessUnitsPerMeter");
        private static readonly int ID_WetnessUnits = Shader.PropertyToID("_WetnessUnits");
        private static readonly int ID_ColorWeightScale = Shader.PropertyToID("_ColorWeightScale");
        private static readonly int ID_MaxVisibleThickness = Shader.PropertyToID("_MaxVisibleThickness");
        private static readonly int ID_BackgroundColor = Shader.PropertyToID("_BackgroundColor");
        private static readonly int ID_WetDarkening = Shader.PropertyToID("_WetDarkening");
        private static readonly int ID_WetGlossBoost = Shader.PropertyToID("_WetGlossBoost");
        private static readonly int ID_PigmentContrast = Shader.PropertyToID("_PigmentContrast");
        private static readonly int ID_NormalFromThicknessStrength = Shader.PropertyToID("_NormalFromThicknessStrength");
        private static readonly int ID_VisualHeightScale = Shader.PropertyToID("_VisualHeightScale");
        private static readonly int ID_SurfaceRoughness = Shader.PropertyToID("_SurfaceRoughness");
        private static readonly int ID_GlossResponse = Shader.PropertyToID("_GlossResponse");
        private static readonly int ID_FilmFlowStartThicknessMeters = Shader.PropertyToID("_FilmFlowStartThicknessMeters");
        private static readonly int ID_PoolHoldFactor = Shader.PropertyToID("_PoolHoldFactor");
        private static readonly int ID_ThinFilmMobilityBelowThreshold = Shader.PropertyToID("_ThinFilmMobilityBelowThreshold");
        private static readonly int ID_GravityBiasInFilmFlow = Shader.PropertyToID("_GravityBiasInFilmFlow");
        private static readonly int ID_FlowStopThicknessFactor = Shader.PropertyToID("_FlowStopThicknessFactor");
        private static readonly int ID_DebugView = Shader.PropertyToID("_DebugView");
        private static readonly int ID_DeltaTime = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_EnableDiffusion = Shader.PropertyToID("_EnableDiffusion");
        private static readonly int ID_EnableDrips = Shader.PropertyToID("_EnableDrips");
        private static readonly int ID_AbsorptionRate = Shader.PropertyToID("_AbsorptionRate");
        private static readonly int ID_SpreadFactor = Shader.PropertyToID("_SpreadFactor");
        private static readonly int ID_DripFactor = Shader.PropertyToID("_DripFactor");
        private static readonly int ID_DryingRate = Shader.PropertyToID("_DryingRate");
        private static readonly int ID_WetnessRetention = Shader.PropertyToID("_WetnessRetention");
        private static readonly int ID_Roughness = Shader.PropertyToID("_Roughness");
        private static readonly int ID_PaintViscosity = Shader.PropertyToID("_PaintViscosity");
        private static readonly int ID_YieldStress = Shader.PropertyToID("_YieldStress");
        private static readonly int ID_ViscosityDamping = Shader.PropertyToID("_ViscosityDamping");
        private static readonly int ID_MomentumFlowFactor = Shader.PropertyToID("_MomentumFlowFactor");
        private static readonly int ID_HeightGradientFlowFactor = Shader.PropertyToID("_HeightGradientFlowFactor");
        private static readonly int ID_RivuletFactor = Shader.PropertyToID("_RivuletFactor");
        private static readonly int ID_EdgeDrainFactor = Shader.PropertyToID("_EdgeDrainFactor");
        private static readonly int ID_EdgeDripThreshold = Shader.PropertyToID("_EdgeDripThreshold");
        private static readonly int ID_CanvasTangent = Shader.PropertyToID("_CanvasTangent");
        private static readonly int ID_CanvasBitangent = Shader.PropertyToID("_CanvasBitangent");
        private static readonly int ID_CanvasNormal = Shader.PropertyToID("_CanvasNormal");
        private static readonly int ID_GravityDirection = Shader.PropertyToID("_GravityDirection");
        private void Awake()
        {
            ResolveReferences();
            ResolveKernels();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResolveKernels();
        }

        private void LateUpdate()
        {
            if (!bakeEveryFrame)
                return;

            RenderNow();
        }

        [ContextMenu("Render MPM Canvas Now")]
        public void RenderNow()
        {
            ResolveReferences();

            if (surface == null)
                return;

            surface.EnsureResources();
            surface.RefreshFrameState(Time.deltaTime);

            if (enableEvolution)
                DispatchEvolution();

            DispatchBake();
            surface.ApplyOutputTexture();
            surface.CommitFrameState();
        }

        [ContextMenu("Reset MPM Canvas")]
        public void ResetSurface()
        {
            ResolveReferences();
            surface?.ResetSurface();
        }

        private void DispatchBake()
        {
            if (paintFilmBakerCompute == null ||
                _bakeKernel < 0 ||
                surface == null ||
                surface.PaintTexture == null ||
                surface.NormalTexture == null ||
                surface.MaterialTexture == null ||
                surface.HeightTexture == null ||
                !surface.FilmGrid.IsValid)
            {
                return;
            }

            MpmPaintFilmGrid grid = surface.FilmGrid;
            MpmCanvasSurfaceMaterialSettings settings = surface.MaterialSettings;
            paintFilmBakerCompute.SetBuffer(_bakeKernel, ID_FilmCells, grid.CellBuffer);
            paintFilmBakerCompute.SetTexture(_bakeKernel, ID_OutputTexture, surface.PaintTexture);
            paintFilmBakerCompute.SetTexture(_bakeKernel, ID_NormalTexture, surface.NormalTexture);
            paintFilmBakerCompute.SetTexture(_bakeKernel, ID_MaterialTexture, surface.MaterialTexture);
            paintFilmBakerCompute.SetTexture(_bakeKernel, ID_HeightTexture, surface.HeightTexture);
            paintFilmBakerCompute.SetInt(ID_GridWidth, grid.Width);
            paintFilmBakerCompute.SetInt(ID_GridHeight, grid.Height);
            paintFilmBakerCompute.SetInt(ID_ThicknessUnitsPerMeter, MpmPaintFilmGrid.ThicknessUnitsPerMeter);
            paintFilmBakerCompute.SetInt(ID_WetnessUnits, MpmPaintFilmGrid.WetnessUnits);
            paintFilmBakerCompute.SetInt(ID_ColorWeightScale, MpmPaintFilmGrid.ColorWeightScale);
            paintFilmBakerCompute.SetFloat(ID_MaxVisibleThickness, maxVisibleThicknessMeters);
            paintFilmBakerCompute.SetVector(ID_BackgroundColor, surface.BackgroundColor);
            paintFilmBakerCompute.SetFloat(ID_WetDarkening, wetDarkening);
            paintFilmBakerCompute.SetFloat(ID_WetGlossBoost, wetGlossBoost);
            paintFilmBakerCompute.SetFloat(ID_PigmentContrast, pigmentContrast);
            paintFilmBakerCompute.SetFloat(ID_NormalFromThicknessStrength, normalFromThicknessStrength);
            paintFilmBakerCompute.SetFloat(ID_VisualHeightScale, visualHeightScaleMeters);
            paintFilmBakerCompute.SetFloat(ID_SurfaceRoughness, settings.roughness);
            paintFilmBakerCompute.SetFloat(ID_GlossResponse, settings.glossResponse);
            paintFilmBakerCompute.SetFloat(ID_FilmFlowStartThicknessMeters, filmFlowStartThicknessMeters);
            SetUvRemapParameters(paintFilmBakerCompute);
            paintFilmBakerCompute.SetInt(ID_DebugView, (int)debugView);

            paintFilmBakerCompute.Dispatch(
                _bakeKernel,
                Groups(grid.Width, 8),
                Groups(grid.Height, 8),
                1
            );
        }

        private void DispatchEvolution()
        {
            if (paintFilmEvolverCompute == null ||
                _evolveKernel < 0 ||
                surface == null ||
                !surface.FilmGrid.IsValid)
            {
                return;
            }

            float dt = Time.deltaTime * Mathf.Max(evolutionRate, 0.0f);
            if (dt <= 0.0f)
                return;

            MpmPaintFilmGrid grid = surface.FilmGrid;
            MpmCanvasSurfaceMaterialSettings settings = surface.MaterialSettings;
            MpmCanvasSurfaceFrame frame = surface.Frame;
            MpmCanvasPaintMaterialSample paintMaterial =
                MpmCanvasPaintMaterial.Resolve(paintFluidSystem);

            paintFilmEvolverCompute.SetBuffer(_evolveKernel, ID_SourceFilmCells, grid.CellBuffer);
            paintFilmEvolverCompute.SetBuffer(_evolveKernel, ID_TargetFilmCells, grid.ScratchBuffer);
            paintFilmEvolverCompute.SetInt(ID_GridWidth, grid.Width);
            paintFilmEvolverCompute.SetInt(ID_GridHeight, grid.Height);
            paintFilmEvolverCompute.SetInt(ID_ThicknessUnitsPerMeter, MpmPaintFilmGrid.ThicknessUnitsPerMeter);
            paintFilmEvolverCompute.SetInt(ID_WetnessUnits, MpmPaintFilmGrid.WetnessUnits);
            paintFilmEvolverCompute.SetInt(ID_ColorWeightScale, MpmPaintFilmGrid.ColorWeightScale);
            paintFilmEvolverCompute.SetFloat(ID_DeltaTime, dt);
            paintFilmEvolverCompute.SetInt(ID_EnableDiffusion, enableDiffusion ? 1 : 0);
            paintFilmEvolverCompute.SetInt(ID_EnableDrips, enableDrips ? 1 : 0);
            paintFilmEvolverCompute.SetFloat(ID_AbsorptionRate, settings.absorptionRate);
            paintFilmEvolverCompute.SetFloat(ID_SpreadFactor, settings.spreadFactor);
            paintFilmEvolverCompute.SetFloat(ID_DripFactor, settings.dripFactor);
            paintFilmEvolverCompute.SetFloat(ID_DryingRate, settings.dryingRate);
            paintFilmEvolverCompute.SetFloat(ID_WetnessRetention, settings.wetnessRetention);
            paintFilmEvolverCompute.SetFloat(ID_Roughness, settings.roughness);
            paintFilmEvolverCompute.SetFloat(ID_PaintViscosity, paintMaterial.Viscosity);
            paintFilmEvolverCompute.SetFloat(ID_YieldStress, paintMaterial.YieldStress);
            paintFilmEvolverCompute.SetFloat(ID_ViscosityDamping, viscosityDamping);
            paintFilmEvolverCompute.SetFloat(ID_MomentumFlowFactor, momentumFlowFactor);
            paintFilmEvolverCompute.SetFloat(ID_HeightGradientFlowFactor, heightGradientFlowFactor);
            paintFilmEvolverCompute.SetFloat(ID_RivuletFactor, rivuletFactor);
            paintFilmEvolverCompute.SetFloat(ID_EdgeDrainFactor, edgeDrainFactor);
            paintFilmEvolverCompute.SetFloat(ID_EdgeDripThreshold, edgeDripThresholdMeters);
            paintFilmEvolverCompute.SetFloat(ID_FilmFlowStartThicknessMeters, filmFlowStartThicknessMeters);
            paintFilmEvolverCompute.SetFloat(ID_PoolHoldFactor, poolHoldFactor);
            paintFilmEvolverCompute.SetFloat(ID_ThinFilmMobilityBelowThreshold, thinFilmMobilityBelowThreshold);
            paintFilmEvolverCompute.SetFloat(ID_GravityBiasInFilmFlow, gravityBiasInFilmFlow);
            paintFilmEvolverCompute.SetFloat(ID_FlowStopThicknessFactor, flowStopThicknessFactor);
            paintFilmEvolverCompute.SetVector(ID_CanvasTangent, frame.tangent);
            paintFilmEvolverCompute.SetVector(ID_CanvasBitangent, frame.bitangent);
            paintFilmEvolverCompute.SetVector(ID_CanvasNormal, frame.normal);
            paintFilmEvolverCompute.SetVector(ID_GravityDirection, gravityDirectionWorld.normalized);
            SetUvRemapParameters(paintFilmEvolverCompute);

            paintFilmEvolverCompute.Dispatch(
                _evolveKernel,
                Groups(grid.Width, 8),
                Groups(grid.Height, 8),
                1
            );

            grid.SwapBuffers();
        }

        private void ResolveReferences()
        {
            if (surface == null)
                surface = GetComponent<MpmCanvasPaintSurface>();

            if (surface == null)
                surface = FindAnyObjectByType<MpmCanvasPaintSurface>();

            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();
        }

        private void SetUvRemapParameters(ComputeShader shader)
        {
            MpmCanvasUvRemap.Apply(shader, surface);
        }

        private void ResolveKernels()
        {
            _bakeKernel = FindKernelSafe(paintFilmBakerCompute, "KBakeFilm");
            _evolveKernel = FindKernelSafe(paintFilmEvolverCompute, "KEvolveFilm");
        }

        private static int FindKernelSafe(ComputeShader shader, string kernelName)
        {
            if (shader == null)
                return -1;

            try
            {
                return shader.FindKernel(kernelName);
            }
            catch
            {
                return -1;
            }
        }

        private static int Groups(int count, int groupSize)
        {
            return Mathf.CeilToInt(count / (float)groupSize);
        }

        private void OnValidate()
        {
            maxVisibleThicknessMeters = Mathf.Max(maxVisibleThicknessMeters, 0.000001f);
            wetDarkening = Mathf.Clamp01(wetDarkening);
            wetGlossBoost = Mathf.Clamp01(wetGlossBoost);
            pigmentContrast = Mathf.Clamp01(pigmentContrast);
            normalFromThicknessStrength = Mathf.Clamp(normalFromThicknessStrength, 0.0f, 8.0f);
            visualHeightScaleMeters = Mathf.Clamp(visualHeightScaleMeters, 0.0f, 0.08f);
            evolutionRate = Mathf.Clamp(evolutionRate, 0.0f, 2.0f);
            viscosityDamping = Mathf.Clamp01(viscosityDamping);
            momentumFlowFactor = Mathf.Clamp(momentumFlowFactor, 0.0f, 4.0f);
            heightGradientFlowFactor = Mathf.Clamp(heightGradientFlowFactor, 0.0f, 4.0f);
            rivuletFactor = Mathf.Clamp(rivuletFactor, 0.0f, 4.0f);
            edgeDrainFactor = Mathf.Clamp(edgeDrainFactor, 0.0f, 4.0f);
            edgeDripThresholdMeters = Mathf.Clamp(edgeDripThresholdMeters, 0.0f, 0.01f);
            filmFlowStartThicknessMeters = Mathf.Max(filmFlowStartThicknessMeters, 0.000001f);
            poolHoldFactor = Mathf.Clamp01(poolHoldFactor);
            thinFilmMobilityBelowThreshold = Mathf.Clamp01(thinFilmMobilityBelowThreshold);
            gravityBiasInFilmFlow = Mathf.Clamp(gravityBiasInFilmFlow, 0.0f, 2.0f);
            flowStopThicknessFactor = Mathf.Clamp01(flowStopThicknessFactor);
            ResolveKernels();
        }
    }
}
