namespace PaintBucketSim.Runtime
{
    public struct GpuFluidBufferStats
    {
        public bool initialized;
        public bool enabled;

        public int capacity;
        public int uploadedParticles;
        public int uploadStride;
        public int uploadFrame;

        public float cpuUploadMilliseconds;
        public float computePostProcessMilliseconds;

        public bool positionRadiusBufferReady;
        public bool velocityMassBufferReady;
        public bool colorBufferReady;
        public bool stateAgeIdBufferReady;

        public bool computePostProcessEnabled;
        public bool debugColorByStateEnabled;

        // G5 Changes //
        public bool affineC0BufferReady;
        public bool affineC1BufferReady;
        public bool affineC2BufferReady;
        // End G5 Changes //

        ////////////////    G6.A Changes   //////////////////

        public bool volumeJBufferReady;
        public bool deformationF0BufferReady;
        public bool deformationF1BufferReady;
        public bool deformationF2BufferReady;

        ////////////////    End G6.A Changes   //////////////////
    }
}