using UnityEngine;

namespace PaintBucketSim.Systems.Fluid.GPU
{
    public sealed class GpuBoundaryParticleBufferSet
    {
        public GraphicsBuffer BoundaryPositionRadiusBuffer { get; private set; }
        public GraphicsBuffer BoundaryNormalTypeBuffer { get; private set; }
        public GraphicsBuffer BoundaryVelocityPsiBuffer { get; private set; }

        public GraphicsBuffer BoundaryCellCountBuffer { get; private set; }
        public GraphicsBuffer BoundaryCellIndicesBuffer { get; private set; }
        public GraphicsBuffer BoundaryGridStatsBuffer { get; private set; }

        public int BoundaryCapacity { get; private set; }
        public int GridCellCount { get; private set; }
        public int MaxBoundaryParticlesPerCell { get; private set; }
        public int BoundaryCellSlotCapacity { get; private set; }

        public bool IsInitialized { get; private set; }

        public void EnsureCapacity(
            int requiredBoundaryCapacity,
            int requiredGridCellCount,
            int requiredMaxBoundaryParticlesPerCell)
        {
            requiredBoundaryCapacity =
                Mathf.Max(1, requiredBoundaryCapacity);

            requiredGridCellCount =
                Mathf.Max(1, requiredGridCellCount);

            requiredMaxBoundaryParticlesPerCell =
                Mathf.Max(4, requiredMaxBoundaryParticlesPerCell);

            int requiredSlotCapacity =
                requiredGridCellCount *
                requiredMaxBoundaryParticlesPerCell;

            bool needsRecreate =
                !IsInitialized ||
                BoundaryCapacity < requiredBoundaryCapacity ||
                GridCellCount != requiredGridCellCount ||
                MaxBoundaryParticlesPerCell != requiredMaxBoundaryParticlesPerCell ||
                BoundaryCellSlotCapacity < requiredSlotCapacity;

            if (!needsRecreate)
                return;

            Dispose();

            BoundaryCapacity = requiredBoundaryCapacity;
            GridCellCount = requiredGridCellCount;
            MaxBoundaryParticlesPerCell = requiredMaxBoundaryParticlesPerCell;
            BoundaryCellSlotCapacity = requiredSlotCapacity;

            BoundaryPositionRadiusBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    BoundaryCapacity,
                    sizeof(float) * 4
                );

            BoundaryNormalTypeBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    BoundaryCapacity,
                    sizeof(float) * 4
                );

            BoundaryVelocityPsiBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    BoundaryCapacity,
                    sizeof(float) * 4
                );

            BoundaryCellCountBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    GridCellCount,
                    sizeof(int)
                );

            BoundaryCellIndicesBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    BoundaryCellSlotCapacity,
                    sizeof(int)
                );

            BoundaryGridStatsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    4,
                    sizeof(int)
                );

            IsInitialized = true;
        }

        public void Dispose()
        {
            Release(BoundaryPositionRadiusBuffer);
            Release(BoundaryNormalTypeBuffer);
            Release(BoundaryVelocityPsiBuffer);

            Release(BoundaryCellCountBuffer);
            Release(BoundaryCellIndicesBuffer);
            Release(BoundaryGridStatsBuffer);

            BoundaryPositionRadiusBuffer = null;
            BoundaryNormalTypeBuffer = null;
            BoundaryVelocityPsiBuffer = null;

            BoundaryCellCountBuffer = null;
            BoundaryCellIndicesBuffer = null;
            BoundaryGridStatsBuffer = null;

            BoundaryCapacity = 0;
            GridCellCount = 0;
            MaxBoundaryParticlesPerCell = 0;
            BoundaryCellSlotCapacity = 0;

            IsInitialized = false;
        }

        private static void Release(GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
        }
    }
}