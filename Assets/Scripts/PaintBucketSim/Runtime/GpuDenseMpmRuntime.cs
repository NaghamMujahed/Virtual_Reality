namespace PaintBucketSim.Runtime
{
    public struct GpuDenseMpmStats
    {
        public bool initialized;
        public bool running;

        public int particleCount;

        public int gridResolutionX;
        public int gridResolutionY;
        public int gridResolutionZ;
        public int gridNodeCount;

        public float cellSizeMeters;

        public float lastStepCpuDispatchMilliseconds;
        public int dispatchCount;

        public bool gridAccumBufferReady;
        public bool gridVelocityMassBufferReady;
    }
}