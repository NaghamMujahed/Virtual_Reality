using UnityEngine;
using PaintSim.Scripts.Core.Data;

namespace PaintSim.Scripts.Stages.Surface
{
    public sealed class PaintEvolver
    {
        private readonly ComputeShader _shader;
        private readonly PaintFilmGrid _grid;
        private readonly int _evaporateKernel;
        private readonly int _spreadKernel;

        private static readonly int ID_PaintCellBuffer =
            Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_PaintCellReadBuffer =
            Shader.PropertyToID("_PaintCellReadBuffer");
        private static readonly int ID_PaintCellWriteBuffer =
            Shader.PropertyToID("_PaintCellWriteBuffer");
        private static readonly int ID_GridWidth =
            Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight =
            Shader.PropertyToID("_GridHeight");
        private static readonly int ID_DeltaTime =
            Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_EvaporationRate =
            Shader.PropertyToID("_EvaporationRate");
        private static readonly int ID_DiffusionRate =
            Shader.PropertyToID("_DiffusionRate");
        private static readonly int ID_RunoffRate =
            Shader.PropertyToID("_RunoffRate");
        private static readonly int ID_MinimumWetThickness =
            Shader.PropertyToID("_MinimumWetThickness");
        private static readonly int ID_ThicknessScale =
            Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_CellSizeU =
            Shader.PropertyToID("_CellSizeU");
        private static readonly int ID_CellSizeV =
            Shader.PropertyToID("_CellSizeV");
        private static readonly int ID_SurfaceGravity =
            Shader.PropertyToID("_SurfaceGravity");
        private static readonly int ID_PaintDensity =
            Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_DynamicViscosity =
            Shader.PropertyToID("_DynamicViscosity");
        private static readonly int ID_SurfaceTension =
            Shader.PropertyToID("_SurfaceTension");
        private static readonly int ID_YieldStress =
            Shader.PropertyToID("_YieldStress");
        private static readonly int ID_ContactLineThickness =
            Shader.PropertyToID("_ContactLineThickness");
        private static readonly int ID_ContactAngleResistance =
            Shader.PropertyToID("_ContactAngleResistance");
        private static readonly int ID_SubstrateFlowVariation =
            Shader.PropertyToID("_SubstrateFlowVariation");
        private static readonly int ID_SurfaceRoughness =
            Shader.PropertyToID("_SurfaceRoughness");
        private static readonly int ID_DripFingerInstability =
            Shader.PropertyToID("_DripFingerInstability");
        private static readonly int ID_ThinFilmCohesion =
            Shader.PropertyToID("_ThinFilmCohesion");
        private static readonly int ID_SurfaceAbsorptionRate =
            Shader.PropertyToID("_SurfaceAbsorptionRate");
        private static readonly int ID_ColorMixingMode =
            Shader.PropertyToID("_ColorMixingMode");
        private static readonly int ID_PigmentMixStrength =
            Shader.PropertyToID("_PigmentMixStrength");
        private static readonly int ID_PigmentMinReflectance =
            Shader.PropertyToID("_PigmentMinReflectance");
        private static readonly int ID_PigmentMaxKs =
            Shader.PropertyToID("_PigmentMaxKs");

        public float EvaporationRate { get; set; } = 0.05f;
        public float DiffusionRate { get; set; } = 0.35f;
        public float RunoffRate { get; set; } = 0.18f;
        public float MinimumWetThickness { get; set; } = 0.000015f;
        public Vector2 SurfaceGravity { get; set; }
        public float PaintDensity { get; set; } = 1200.0f;
        public float DynamicViscosity { get; set; } = 0.5f;
        public float SurfaceTension { get; set; } = 0.04f;
        public float YieldStress { get; set; }
        public float ContactLineThickness { get; set; } = 0.000025f;
        public float ContactAngleResistance { get; set; } = 0.72f;
        public float SubstrateFlowVariation { get; set; } = 0.28f;
        public float SurfaceRoughness { get; set; } = 0.5f;
        public float DripFingerInstability { get; set; } = 0.36f;
        public float ThinFilmCohesion { get; set; } = 0.58f;
        public float SurfaceAbsorptionRate { get; set; } = 0.6f;
        public PaintColorMixingMode ColorMixingMode { get; set; } =
            PaintColorMixingMode.Rgb;
        public float PigmentMixStrength { get; set; } = 1.0f;
        public float PigmentMinReflectance { get; set; } = 0.035f;
        public float PigmentMaxKs { get; set; } = 18.0f;

        public PaintEvolver(ComputeShader shader, PaintFilmGrid grid)
        {
            _shader = shader;
            _grid = grid;

            if (_shader == null)
                return;

            _evaporateKernel = _shader.FindKernel("Evaporate");
            _spreadKernel = _shader.FindKernel("Spread");
        }

        public void Evolve(float deltaTime)
        {
            if (_shader == null ||
                _grid.PaintCellBuffer == null ||
                _grid.ScratchCellBuffer == null)
            {
                return;
            }

            int threadGroupsX = Mathf.CeilToInt(_grid.GridWidth / 8f);
            int threadGroupsY = Mathf.CeilToInt(_grid.GridHeight / 8f);

            ApplyCommon(deltaTime);

            _shader.SetBuffer(_spreadKernel, ID_PaintCellReadBuffer, _grid.PaintCellBuffer);
            _shader.SetBuffer(_spreadKernel, ID_PaintCellWriteBuffer, _grid.ScratchCellBuffer);
            _shader.Dispatch(_spreadKernel, threadGroupsX, threadGroupsY, 1);

            _shader.SetBuffer(_evaporateKernel, ID_PaintCellWriteBuffer, _grid.ScratchCellBuffer);
            _shader.SetBuffer(_evaporateKernel, ID_PaintCellBuffer, _grid.PaintCellBuffer);
            _shader.Dispatch(_evaporateKernel, threadGroupsX, threadGroupsY, 1);
        }

        private void ApplyCommon(float deltaTime)
        {
            _shader.SetInt(ID_GridWidth, _grid.GridWidth);
            _shader.SetInt(ID_GridHeight, _grid.GridHeight);
            _shader.SetFloat(ID_DeltaTime, Mathf.Max(deltaTime, 0.0f));
            _shader.SetFloat(ID_EvaporationRate, Mathf.Max(EvaporationRate, 0.0f));
            _shader.SetFloat(ID_DiffusionRate, Mathf.Max(DiffusionRate, 0.0f));
            _shader.SetFloat(ID_RunoffRate, Mathf.Max(RunoffRate, 0.0f));
            _shader.SetFloat(ID_MinimumWetThickness, Mathf.Max(MinimumWetThickness, 1e-7f));
            _shader.SetInt(ID_ThicknessScale, PaintCellData.ThicknessScale);
            _shader.SetFloat(ID_CellSizeU, _grid.CellSizeU);
            _shader.SetFloat(ID_CellSizeV, _grid.CellSizeV);
            _shader.SetVector(ID_SurfaceGravity, SurfaceGravity);
            _shader.SetFloat(ID_PaintDensity, Mathf.Max(PaintDensity, 1.0f));
            _shader.SetFloat(ID_DynamicViscosity, Mathf.Max(DynamicViscosity, 0.0001f));
            _shader.SetFloat(ID_SurfaceTension, Mathf.Max(SurfaceTension, 0.0001f));
            _shader.SetFloat(ID_YieldStress, Mathf.Max(YieldStress, 0.0f));
            _shader.SetFloat(
                ID_ContactLineThickness,
                Mathf.Max(ContactLineThickness, 1e-7f)
            );
            _shader.SetFloat(
                ID_ContactAngleResistance,
                Mathf.Clamp01(ContactAngleResistance)
            );
            _shader.SetFloat(
                ID_SubstrateFlowVariation,
                Mathf.Clamp01(SubstrateFlowVariation)
            );
            _shader.SetFloat(
                ID_SurfaceRoughness,
                Mathf.Clamp01(SurfaceRoughness)
            );
            _shader.SetFloat(
                ID_DripFingerInstability,
                Mathf.Clamp01(DripFingerInstability)
            );
            _shader.SetFloat(
                ID_ThinFilmCohesion,
                Mathf.Clamp01(ThinFilmCohesion)
            );
            _shader.SetFloat(
                ID_SurfaceAbsorptionRate,
                Mathf.Clamp01(SurfaceAbsorptionRate)
            );
            _shader.SetInt(ID_ColorMixingMode, (int)ColorMixingMode);
            _shader.SetFloat(
                ID_PigmentMixStrength,
                Mathf.Clamp01(PigmentMixStrength)
            );
            _shader.SetFloat(
                ID_PigmentMinReflectance,
                Mathf.Clamp(PigmentMinReflectance, 0.001f, 0.35f)
            );
            _shader.SetFloat(
                ID_PigmentMaxKs,
                Mathf.Clamp(PigmentMaxKs, 1.0f, 64.0f)
            );
        }
    }
}
