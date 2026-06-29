using UnityEngine;

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

        public float EvaporationRate { get; set; } = 0.05f;
        public float DiffusionRate { get; set; } = 0.35f;
        public float RunoffRate { get; set; } = 0.18f;
        public float MinimumWetThickness { get; set; } = 0.000015f;

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
        }
    }
}
