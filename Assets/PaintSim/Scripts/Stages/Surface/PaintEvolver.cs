using UnityEngine;

namespace PaintSim.Scripts.Stages.Surface
{
    public class PaintEvolver
    {
        private readonly ComputeShader _shader;
        private readonly PaintFilmGrid _grid;
        private readonly int _evaporateKernel;
        private readonly int _diffuseKernel; 

        private static readonly int ID_PaintCellBuffer = Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_GridWidth       = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight      = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_DeltaTime       = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_EvaporationRate = Shader.PropertyToID("_EvaporationRate");
        private static readonly int ID_DiffusionRate   = Shader.PropertyToID("_DiffusionRate"); 

        public float EvaporationRate { get; set; } = 0.1f;
        public float DiffusionRate   { get; set; } = 15.0f; 

        public PaintEvolver(ComputeShader shader, PaintFilmGrid grid)
        {
            _shader = shader;
            _grid = grid;

            if (_shader != null)
            {
                _evaporateKernel = _shader.FindKernel("Evaporate");
                _diffuseKernel   = _shader.FindKernel("Diffuse"); // ربط الكيرنل
            }
        }

        public void Evolve(float deltaTime)
        {
            if (_shader == null || _grid.PaintCellBuffer == null) return;

            int threadGroupsX = Mathf.CeilToInt(_grid.GridWidth / 8f);
            int threadGroupsY = Mathf.CeilToInt(_grid.GridHeight / 8f);

            _shader.SetBuffer(_diffuseKernel, ID_PaintCellBuffer, _grid.PaintCellBuffer);
            _shader.SetInt(ID_GridWidth, _grid.GridWidth);
            _shader.SetInt(ID_GridHeight, _grid.GridHeight);
            _shader.SetFloat(ID_DeltaTime, deltaTime);
            _shader.SetFloat(ID_DiffusionRate, DiffusionRate);
            
            _shader.Dispatch(_diffuseKernel, threadGroupsX, threadGroupsY, 1);

            _shader.SetBuffer(_evaporateKernel, ID_PaintCellBuffer, _grid.PaintCellBuffer);
            _shader.SetFloat(ID_EvaporationRate, EvaporationRate);

            _shader.Dispatch(_evaporateKernel, threadGroupsX, threadGroupsY, 1);
        }
    }
}