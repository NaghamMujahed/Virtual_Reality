using UnityEngine;

namespace PaintBucketSim.Systems.Fluid.GPU
{
    public sealed class GpuDfsphBufferSet
    {
        public GraphicsBuffer DensityBuffer { get; private set; }
        public GraphicsBuffer PredictedDensityBuffer { get; private set; }

        public GraphicsBuffer AlphaBuffer { get; private set; }

        public GraphicsBuffer DensityErrorBuffer { get; private set; }
        public GraphicsBuffer DivergenceErrorBuffer { get; private set; }

        public GraphicsBuffer NeighborCountBuffer { get; private set; }

        public GraphicsBuffer TempVectorBuffer { get; private set; }
        public GraphicsBuffer TempScalarBuffer { get; private set; }

        public int Capacity { get; private set; }
        public bool IsInitialized { get; private set; }

        public GraphicsBuffer ParticleCellIndexBuffer { get; private set; }

        public GraphicsBuffer CellParticleCountBuffer { get; private set; }
        public GraphicsBuffer CellParticleIndicesBuffer { get; private set; }

        public GraphicsBuffer SpatialGridStatsBuffer { get; private set; }

        public GraphicsBuffer BoundaryFactorBuffer { get; private set; }

        public int GridCellCount { get; private set; }
        public int MaxParticlesPerCell { get; private set; }
        public int CellParticleSlotCapacity { get; private set; }

        public GraphicsBuffer DivergencePressureBuffer { get; private set; }


        public GraphicsBuffer BoundaryNeighborCountBuffer { get; private set; }
        public GraphicsBuffer BoundaryDensityContributionBuffer { get; private set; }

        // سنستخدمه في D6 لاحقًا، لكن نضيفه الآن حتى لا نعيد تعديل layout فورًا.
        public GraphicsBuffer DensityPressureBuffer { get; private set; }

        public void EnsureCapacity(
            int requiredParticleCapacity,
            int requiredGridCellCount,
            int requiredMaxParticlesPerCell)
        {
            requiredParticleCapacity = Mathf.Max(1, requiredParticleCapacity);
            requiredGridCellCount = Mathf.Max(1, requiredGridCellCount);
            requiredMaxParticlesPerCell = Mathf.Max(4, requiredMaxParticlesPerCell);

            int requiredSlotCapacity =
                requiredGridCellCount * requiredMaxParticlesPerCell;

            bool hasEnoughCapacity =
                IsInitialized &&
                Capacity >= requiredParticleCapacity &&
                GridCellCount >= requiredGridCellCount &&
                MaxParticlesPerCell >= requiredMaxParticlesPerCell &&
                CellParticleSlotCapacity >= requiredSlotCapacity;

            if (hasEnoughCapacity)
                return;

            Dispose();

            Capacity = requiredParticleCapacity;
            GridCellCount = requiredGridCellCount;
            MaxParticlesPerCell = requiredMaxParticlesPerCell;
            CellParticleSlotCapacity = requiredSlotCapacity;

            DensityBuffer = CreateFloatBuffer(Capacity);
            PredictedDensityBuffer = CreateFloatBuffer(Capacity);

            AlphaBuffer = CreateFloatBuffer(Capacity);

            DensityErrorBuffer = CreateFloatBuffer(Capacity);
            DivergenceErrorBuffer = CreateFloatBuffer(Capacity);

            DivergencePressureBuffer = CreateFloatBuffer(Capacity);
            DensityPressureBuffer = CreateFloatBuffer(Capacity);

            NeighborCountBuffer = CreateIntBuffer(Capacity);

            TempVectorBuffer = CreateFloat4Buffer(Capacity);
            TempScalarBuffer = CreateFloatBuffer(Capacity);

            ParticleCellIndexBuffer = CreateIntBuffer(Capacity);

            CellParticleCountBuffer = CreateIntBuffer(GridCellCount);
            CellParticleIndicesBuffer = CreateIntBuffer(CellParticleSlotCapacity);

            // x = inserted particles
            // y = overflow particles
            // z = out-of-grid particles
            // w = max observed cell count
            SpatialGridStatsBuffer = CreateIntBuffer(4);

            BoundaryFactorBuffer = CreateFloatBuffer(Capacity);

            BoundaryNeighborCountBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    Capacity,
                    sizeof(int)
                );
            BoundaryDensityContributionBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    Capacity,
                    sizeof(float)
                );

            IsInitialized = true;
        }

        public void Dispose()
        {
            Release(DensityBuffer);
            DensityBuffer = null;

            Release(PredictedDensityBuffer);
            PredictedDensityBuffer = null;

            Release(AlphaBuffer);
            AlphaBuffer = null;

            Release(DensityErrorBuffer);
            DensityErrorBuffer = null;

            Release(DivergenceErrorBuffer);
            DivergenceErrorBuffer = null;

            Release(DivergencePressureBuffer);
            DivergencePressureBuffer = null;

            Release(DensityPressureBuffer);
            DensityPressureBuffer = null;

            Release(NeighborCountBuffer);
            NeighborCountBuffer = null;

            Release(TempVectorBuffer);
            TempVectorBuffer = null;

            Release(TempScalarBuffer);
            TempScalarBuffer = null;

            Release(ParticleCellIndexBuffer);
            ParticleCellIndexBuffer = null;

            Release(CellParticleCountBuffer);
            CellParticleCountBuffer = null;

            Release(CellParticleIndicesBuffer);
            CellParticleIndicesBuffer = null;

            Release(SpatialGridStatsBuffer);
            SpatialGridStatsBuffer = null;

            Release(BoundaryFactorBuffer);
            BoundaryFactorBuffer = null;

            Release(BoundaryNeighborCountBuffer);
            Release(BoundaryDensityContributionBuffer);
            BoundaryNeighborCountBuffer = null;
            BoundaryDensityContributionBuffer = null;

            Capacity = 0;
            GridCellCount = 0;
            MaxParticlesPerCell = 0;
            CellParticleSlotCapacity = 0;

            IsInitialized = false;
        }

        private static void Release(GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
        }

        private static GraphicsBuffer CreateFloatBuffer(int count)
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                sizeof(float)
            );
        }

        private static GraphicsBuffer CreateIntBuffer(int count)
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                sizeof(int)
            );
        }

        private static GraphicsBuffer CreateFloat4Buffer(int count)
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                sizeof(float) * 4
            );
        }
    }
}