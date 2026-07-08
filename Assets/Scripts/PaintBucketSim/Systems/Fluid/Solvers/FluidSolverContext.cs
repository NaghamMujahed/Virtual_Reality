using PaintBucketSim.Configs;
using PaintBucketSim.Data;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Fluid.GPU;

namespace PaintBucketSim.Systems.Fluid.Solvers
{
    public sealed class FluidSolverContext
    {
        public PaintMaterialConfig MaterialConfig;
        public PaintFluidConfig FluidConfig;
        public PbfSolverConfig PbfConfig;
        public FluidSolverArchitectureConfig ArchitectureConfig;

        public BucketSystem BucketSystem;
        public BoundarySystem BoundarySystem;

        public GpuMpmSolverConfig GpuMpmConfig;
        public GpuFluidBufferSet GpuBufferSet;

        public FluidParticleData Particles;
        public float ReferenceDensityScale = 1.0f;

        public bool IsValid =>
            MaterialConfig != null &&
            FluidConfig != null &&
            PbfConfig != null &&
            BucketSystem != null &&
            Particles != null &&
            Particles.IsCreated;
    }
}
