using System.Runtime.InteropServices;
using UnityEngine;
using PaintSim.Scripts.Core.Data;

namespace PaintSim.Scripts.Stages.Surface
{
    public sealed class PaintFilmGrid
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Int4
        {
            public int X;
            public int Y;
            public int Z;
            public int W;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Int2
        {
            public int X;
            public int Y;
        }

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
        public Vector3 PreviousSurfaceOriginWS { get; private set; }
        public Vector3 PreviousSurfaceAxisU { get; private set; }
        public Vector3 PreviousSurfaceAxisV { get; private set; }
        public Vector3 PreviousSurfaceNormalWS { get; private set; }
        public float PreviousCellSizeU { get; private set; }
        public float PreviousCellSizeV { get; private set; }
        public float ImpactCaptureDistance { get; private set; }
        public bool DoubleSidedImpact { get; private set; }

        public ComputeBuffer PaintCellBuffer { get; private set; }
        public ComputeBuffer ScratchCellBuffer { get; private set; }
        public ComputeBuffer DepositColorAccumulatorBuffer { get; private set; }
        public ComputeBuffer DepositFlowAccumulatorBuffer { get; private set; }
        public ComputeBuffer DepositTouchedFlagsBuffer { get; private set; }
        public ComputeBuffer DepositTouchedIndicesBuffer { get; private set; }
        public ComputeBuffer DepositResolveDispatchArgsBuffer { get; private set; }

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
        private static readonly int ID_PreviousSurfaceOriginWS =
            Shader.PropertyToID("_PreviousSurfaceOriginWS");
        private static readonly int ID_PreviousSurfaceAxisU =
            Shader.PropertyToID("_PreviousSurfaceAxisU");
        private static readonly int ID_PreviousSurfaceAxisV =
            Shader.PropertyToID("_PreviousSurfaceAxisV");
        private static readonly int ID_PreviousSurfaceNormalWS =
            Shader.PropertyToID("_PreviousSurfaceNormalWS");
        private static readonly int ID_PreviousCellSizeU =
            Shader.PropertyToID("_PreviousCellSizeU");
        private static readonly int ID_PreviousCellSizeV =
            Shader.PropertyToID("_PreviousCellSizeV");
        private static readonly int ID_SurfaceImpactCaptureDistance =
            Shader.PropertyToID("_SurfaceImpactCaptureDistance");
        private static readonly int ID_SurfaceDoubleSidedImpact =
            Shader.PropertyToID("_SurfaceDoubleSidedImpact");
        private static readonly int ID_DepositColorAccumulator =
            Shader.PropertyToID("_DepositColorAccumulator");
        private static readonly int ID_DepositFlowAccumulator =
            Shader.PropertyToID("_DepositFlowAccumulator");
        private static readonly int ID_DepositTouchedFlags =
            Shader.PropertyToID("_DepositTouchedFlags");
        private static readonly int ID_DepositTouchedList =
            Shader.PropertyToID("_DepositTouchedList");
        private static readonly int ID_DepositResolveDispatchArgs =
            Shader.PropertyToID("_DepositResolveDispatchArgs");

        private int _lastSurfaceFrameRefreshFrame = int.MinValue;

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
            PreviousSurfaceOriginWS = SurfaceOriginWS;
            PreviousSurfaceAxisU = SurfaceAxisU;
            PreviousSurfaceAxisV = SurfaceAxisV;
            PreviousSurfaceNormalWS = SurfaceNormalWS;
            PreviousCellSizeU = CellSizeU;
            PreviousCellSizeV = CellSizeV;
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

            DepositColorAccumulatorBuffer = new ComputeBuffer(
                totalCells,
                sizeof(int) * 4,
                ComputeBufferType.Default
            );

            DepositFlowAccumulatorBuffer = new ComputeBuffer(
                totalCells,
                sizeof(int) * 2,
                ComputeBufferType.Default
            );

            DepositTouchedFlagsBuffer = new ComputeBuffer(
                totalCells,
                sizeof(uint),
                ComputeBufferType.Default
            );

            DepositTouchedIndicesBuffer = new ComputeBuffer(
                totalCells + 1,
                sizeof(uint),
                ComputeBufferType.Default
            );

            DepositResolveDispatchArgsBuffer = new ComputeBuffer(
                3,
                sizeof(uint),
                ComputeBufferType.IndirectArguments
            );

            var emptyCells = new PaintCellData[totalCells];
            var emptyColorAccumulator = new Int4[totalCells];
            var emptyFlowAccumulator = new Int2[totalCells];
            var emptyTouchedFlags = new uint[totalCells];
            var emptyTouchedList = new uint[totalCells + 1];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);
            ScratchCellBuffer.SetData(emptyCells);
            DepositColorAccumulatorBuffer.SetData(emptyColorAccumulator);
            DepositFlowAccumulatorBuffer.SetData(emptyFlowAccumulator);
            DepositTouchedFlagsBuffer.SetData(emptyTouchedFlags);
            DepositTouchedIndicesBuffer.SetData(emptyTouchedList);
            DepositResolveDispatchArgsBuffer.SetData(new uint[] { 0u, 1u, 1u });
        }

        public void BindToShader(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(kernelIndex, ID_PaintCellBuffer, PaintCellBuffer);
            ApplyGridParameters(shader);
        }

        public void ApplyGridParameters(ComputeShader shader)
        {
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
            shader.SetVector(ID_PreviousSurfaceOriginWS, PreviousSurfaceOriginWS);
            shader.SetVector(ID_PreviousSurfaceAxisU, PreviousSurfaceAxisU);
            shader.SetVector(ID_PreviousSurfaceAxisV, PreviousSurfaceAxisV);
            shader.SetVector(ID_PreviousSurfaceNormalWS, PreviousSurfaceNormalWS);
            shader.SetFloat(ID_PreviousCellSizeU, PreviousCellSizeU);
            shader.SetFloat(ID_PreviousCellSizeV, PreviousCellSizeV);
            shader.SetFloat(ID_SurfaceImpactCaptureDistance, ImpactCaptureDistance);
            shader.SetInt(ID_SurfaceDoubleSidedImpact, DoubleSidedImpact ? 1 : 0);
        }

        public void ReconfigureSurfaceFrame(
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
            int refreshFrame = Application.isPlaying
                ? Time.frameCount
                : _lastSurfaceFrameRefreshFrame + 1;

            if (refreshFrame != _lastSurfaceFrameRefreshFrame)
            {
                PreviousSurfaceOriginWS = SurfaceOriginWS;
                PreviousSurfaceAxisU = SurfaceAxisU;
                PreviousSurfaceAxisV = SurfaceAxisV;
                PreviousSurfaceNormalWS = SurfaceNormalWS;
                PreviousCellSizeU = CellSizeU;
                PreviousCellSizeV = CellSizeV;
                _lastSurfaceFrameRefreshFrame = refreshFrame;
            }

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
        }

        public void BindDepositWriteBuffers(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(
                kernelIndex,
                ID_DepositColorAccumulator,
                DepositColorAccumulatorBuffer
            );
            shader.SetBuffer(
                kernelIndex,
                ID_DepositFlowAccumulator,
                DepositFlowAccumulatorBuffer
            );
            shader.SetBuffer(
                kernelIndex,
                ID_DepositTouchedFlags,
                DepositTouchedFlagsBuffer
            );
            shader.SetBuffer(
                kernelIndex,
                ID_DepositTouchedList,
                DepositTouchedIndicesBuffer
            );
        }

        public void BindDepositResolveBuffers(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(kernelIndex, ID_PaintCellBuffer, PaintCellBuffer);
            BindDepositWriteBuffers(shader, kernelIndex);
        }

        public void BindDepositDispatchControlBuffers(
            ComputeShader shader,
            int kernelIndex)
        {
            shader.SetBuffer(
                kernelIndex,
                ID_DepositTouchedList,
                DepositTouchedIndicesBuffer
            );
            shader.SetBuffer(
                kernelIndex,
                ID_DepositResolveDispatchArgs,
                DepositResolveDispatchArgsBuffer
            );
        }

        public void BindDepositTouchedList(ComputeShader shader, int kernelIndex)
        {
            shader.SetBuffer(
                kernelIndex,
                ID_DepositTouchedList,
                DepositTouchedIndicesBuffer
            );
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
            var emptyColorAccumulator = new Int4[totalCells];
            var emptyFlowAccumulator = new Int2[totalCells];
            var emptyTouchedFlags = new uint[totalCells];
            var emptyTouchedList = new uint[totalCells + 1];
            for (int i = 0; i < totalCells; i++)
                emptyCells[i] = PaintCellData.Empty;

            PaintCellBuffer.SetData(emptyCells);
            ScratchCellBuffer?.SetData(emptyCells);
            DepositColorAccumulatorBuffer?.SetData(emptyColorAccumulator);
            DepositFlowAccumulatorBuffer?.SetData(emptyFlowAccumulator);
            DepositTouchedFlagsBuffer?.SetData(emptyTouchedFlags);
            DepositTouchedIndicesBuffer?.SetData(emptyTouchedList);
            DepositResolveDispatchArgsBuffer?.SetData(new uint[] { 0u, 1u, 1u });
        }

        public void Dispose()
        {
            PaintCellBuffer?.Release();
            ScratchCellBuffer?.Release();
            DepositColorAccumulatorBuffer?.Release();
            DepositFlowAccumulatorBuffer?.Release();
            DepositTouchedFlagsBuffer?.Release();
            DepositTouchedIndicesBuffer?.Release();
            DepositResolveDispatchArgsBuffer?.Release();
            PaintCellBuffer = null;
            ScratchCellBuffer = null;
            DepositColorAccumulatorBuffer = null;
            DepositFlowAccumulatorBuffer = null;
            DepositTouchedFlagsBuffer = null;
            DepositTouchedIndicesBuffer = null;
            DepositResolveDispatchArgsBuffer = null;
        }
    }
}
