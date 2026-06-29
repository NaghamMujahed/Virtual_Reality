using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Buffers;

namespace PaintSim.Scripts.Stages.Surface
{
    public class PaintFilmGrid
    {
        public int     GridWidth  { get; private set; }
        public int     GridHeight { get; private set; }
        public float   CellSize   { get; private set; }
        public float   SurfaceY   { get; private set; }
        public Vector2 GridOrigin { get; private set; }

        public ComputeBuffer PaintCellBuffer { get; private set; }

        private static readonly int ID_PaintCellBuffer = Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_GridWidth       = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight      = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_CellSize        = Shader.PropertyToID("_CellSize");
        private static readonly int ID_GridOriginX     = Shader.PropertyToID("_GridOriginX");
        private static readonly int ID_GridOriginZ     = Shader.PropertyToID("_GridOriginZ");
        private static readonly int ID_SurfaceY        = Shader.PropertyToID("_SurfaceY");

        public PaintFilmGrid(
            int     gridWidth,
            int     gridHeight,
            float   cellSize,
            float   surfaceY,
            Vector2 gridOrigin)
        {
            GridWidth  = gridWidth;
            GridHeight = gridHeight;
            CellSize   = cellSize;
            SurfaceY   = surfaceY;
            GridOrigin = gridOrigin;

            InitBuffer();
        }

        private void InitBuffer()
        {
            int totalCells = GridWidth * GridHeight;

            PaintCellBuffer = new ComputeBuffer(
                totalCells,
                PaintCellData.Stride,
                ComputeBufferType.Default
            );

            var emptyCells = new PaintCellData[totalCells];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);

            Debug.Log($"[PaintFilmGrid] Created {GridWidth}x{GridHeight} " +
                      $"= {totalCells} cells " +
                      $"({totalCells * PaintCellData.Stride / 1024} KB)");
        }

        public void BindToShader(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(kernelIndex, ID_PaintCellBuffer, PaintCellBuffer);
            shader.SetInt(ID_GridWidth,     GridWidth);
            shader.SetInt(ID_GridHeight,    GridHeight);
            shader.SetFloat(ID_CellSize,    CellSize);
            shader.SetFloat(ID_GridOriginX, GridOrigin.x);
            shader.SetFloat(ID_GridOriginZ, GridOrigin.y);
            shader.SetFloat(ID_SurfaceY,    SurfaceY);
        }

        public bool WorldToGrid(
            Vector3 worldPos,
            out int indexI,
            out int indexJ,
            out int flatIndex)
        {
            indexI = Mathf.FloorToInt((worldPos.x - GridOrigin.x) / CellSize);
            indexJ = Mathf.FloorToInt((worldPos.z - GridOrigin.y) / CellSize);

            bool inBounds = indexI >= 0 && indexI < GridWidth
                         && indexJ >= 0 && indexJ < GridHeight;

            if (!inBounds)
            {
                flatIndex = -1;
                return false;
            }

            flatIndex = indexJ * GridWidth + indexI;
            return true;
        }

        public Vector3 GridToWorld(int indexI, int indexJ)
        {
            float x = GridOrigin.x + (indexI + 0.5f) * CellSize;
            float z = GridOrigin.y + (indexJ + 0.5f) * CellSize;
            return new Vector3(x, SurfaceY, z);
        }

        // ─────────────────────────────────────────
        public void Clear()
        {
            int totalCells = GridWidth * GridHeight;
            var emptyCells = new PaintCellData[totalCells];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);
        }

        // ─────────────────────────────────────────
        public void Dispose()
        {
            PaintCellBuffer?.Release();
        }
    }
}