using UnityEngine;
using PaintSim.Scripts.Core.Data;

namespace PaintSim.Scripts.Stages.Surface
{
    public sealed class PaintFilmGrid
    {
        public int GridWidth { get; private set; }
        public int GridHeight { get; private set; }
        public float CellSize { get; private set; }
        public float CellSizeU { get; private set; }
        public float CellSizeV { get; private set; }
        public float SurfaceY { get; private set; }
        public Vector2 GridOrigin { get; private set; }
        public Vector3 SurfaceOriginWS { get; private set; }
        public Vector3 SurfaceAxisU { get; private set; }
        public Vector3 SurfaceAxisV { get; private set; }
        public Vector3 SurfaceNormalWS { get; private set; }
        public float ImpactCaptureDistance { get; private set; }
        public bool DoubleSidedImpact { get; private set; }

        public ComputeBuffer PaintCellBuffer { get; private set; }
        public ComputeBuffer ScratchCellBuffer { get; private set; }

        private static readonly int ID_PaintCellBuffer = Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_GridWidth = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_CellSize = Shader.PropertyToID("_CellSize");
        private static readonly int ID_CellSizeU = Shader.PropertyToID("_CellSizeU");
        private static readonly int ID_CellSizeV = Shader.PropertyToID("_CellSizeV");
        private static readonly int ID_GridOriginX = Shader.PropertyToID("_GridOriginX");
        private static readonly int ID_GridOriginZ = Shader.PropertyToID("_GridOriginZ");
        private static readonly int ID_SurfaceY = Shader.PropertyToID("_SurfaceY");
        private static readonly int ID_SurfaceOriginWS = Shader.PropertyToID("_SurfaceOriginWS");
        private static readonly int ID_SurfaceAxisU = Shader.PropertyToID("_SurfaceAxisU");
        private static readonly int ID_SurfaceAxisV = Shader.PropertyToID("_SurfaceAxisV");
        private static readonly int ID_SurfaceNormalWS = Shader.PropertyToID("_SurfaceNormalWS");
        private static readonly int ID_SurfaceImpactCaptureDistance =
            Shader.PropertyToID("_SurfaceImpactCaptureDistance");
        private static readonly int ID_SurfaceDoubleSidedImpact =
            Shader.PropertyToID("_SurfaceDoubleSidedImpact");

        public PaintFilmGrid(
            int gridWidth,
            int gridHeight,
            float cellSize,
            float surfaceY,
            Vector2 gridOrigin)
            : this(
                gridWidth,
                gridHeight,
                cellSize,
                cellSize,
                surfaceY,
                gridOrigin,
                new Vector3(gridOrigin.x, surfaceY, gridOrigin.y),
                Vector3.right,
                Vector3.forward,
                Vector3.up,
                cellSize * 2.0f,
                true)
        {
        }

        public PaintFilmGrid(
            int gridWidth,
            int gridHeight,
            float cellSizeU,
            float cellSizeV,
            float surfaceY,
            Vector2 gridOrigin,
            Vector3 surfaceOriginWS,
            Vector3 surfaceAxisU,
            Vector3 surfaceAxisV,
            Vector3 surfaceNormalWS,
            float impactCaptureDistance,
            bool doubleSidedImpact)
        {
            GridWidth = Mathf.Max(1, gridWidth);
            GridHeight = Mathf.Max(1, gridHeight);
            CellSizeU = Mathf.Max(cellSizeU, 0.0001f);
            CellSizeV = Mathf.Max(cellSizeV, 0.0001f);
            CellSize = Mathf.Sqrt(CellSizeU * CellSizeV);
            SurfaceY = surfaceY;
            GridOrigin = gridOrigin;
            SurfaceOriginWS = surfaceOriginWS;
            SurfaceAxisU = SafeNormalized(surfaceAxisU, Vector3.right);
            SurfaceAxisV = SafeNormalized(surfaceAxisV, Vector3.forward);
            SurfaceNormalWS = SafeNormalized(surfaceNormalWS, Vector3.up);
            ImpactCaptureDistance = Mathf.Max(impactCaptureDistance, CellSize);
            DoubleSidedImpact = doubleSidedImpact;

            InitBuffer();
        }

        private static Vector3 SafeNormalized(Vector3 value, Vector3 fallback)
        {
            float sqrMagnitude = value.sqrMagnitude;
            if (sqrMagnitude < 1e-10f)
                return fallback.normalized;

            return value / Mathf.Sqrt(sqrMagnitude);
        }

        private void InitBuffer()
        {
            int totalCells = GridWidth * GridHeight;

            PaintCellBuffer = new ComputeBuffer(
                totalCells,
                PaintCellData.Stride,
                ComputeBufferType.Default
            );

            ScratchCellBuffer = new ComputeBuffer(
                totalCells,
                PaintCellData.Stride,
                ComputeBufferType.Default
            );

            var emptyCells = new PaintCellData[totalCells];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);
            ScratchCellBuffer.SetData(emptyCells);
        }

        public void BindToShader(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(kernelIndex, ID_PaintCellBuffer, PaintCellBuffer);
            shader.SetInt(ID_GridWidth, GridWidth);
            shader.SetInt(ID_GridHeight, GridHeight);
            shader.SetFloat(ID_CellSize, CellSize);
            shader.SetFloat(ID_CellSizeU, CellSizeU);
            shader.SetFloat(ID_CellSizeV, CellSizeV);
            shader.SetFloat(ID_GridOriginX, GridOrigin.x);
            shader.SetFloat(ID_GridOriginZ, GridOrigin.y);
            shader.SetFloat(ID_SurfaceY, SurfaceY);
            shader.SetVector(ID_SurfaceOriginWS, SurfaceOriginWS);
            shader.SetVector(ID_SurfaceAxisU, SurfaceAxisU);
            shader.SetVector(ID_SurfaceAxisV, SurfaceAxisV);
            shader.SetVector(ID_SurfaceNormalWS, SurfaceNormalWS);
            shader.SetFloat(ID_SurfaceImpactCaptureDistance, ImpactCaptureDistance);
            shader.SetInt(ID_SurfaceDoubleSidedImpact, DoubleSidedImpact ? 1 : 0);
        }

        public bool WorldToGrid(
            Vector3 worldPos,
            out int indexI,
            out int indexJ,
            out int flatIndex)
        {
            Vector3 relative = worldPos - SurfaceOriginWS;
            float u = Vector3.Dot(relative, SurfaceAxisU) / CellSizeU;
            float v = Vector3.Dot(relative, SurfaceAxisV) / CellSizeV;

            indexI = Mathf.FloorToInt(u);
            indexJ = Mathf.FloorToInt(v);

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
            return SurfaceOriginWS
                 + SurfaceAxisU * ((indexI + 0.5f) * CellSizeU)
                 + SurfaceAxisV * ((indexJ + 0.5f) * CellSizeV);
        }

        public void Clear()
        {
            int totalCells = GridWidth * GridHeight;
            var emptyCells = new PaintCellData[totalCells];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);
            ScratchCellBuffer?.SetData(emptyCells);
        }

        public void Dispose()
        {
            PaintCellBuffer?.Release();
            ScratchCellBuffer?.Release();
            PaintCellBuffer = null;
            ScratchCellBuffer = null;
        }
    }
}
