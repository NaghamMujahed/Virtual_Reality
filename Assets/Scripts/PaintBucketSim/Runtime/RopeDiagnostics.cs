namespace PaintBucketSim.Runtime
{
    public struct RopeDiagnostics
    {
        public int particleCount;
        public int segmentCount;

        public float currentLength;
        public float restLength;
        public float maxStretchError;
        public float averageStretchError;

        public float maxTensionEstimate;
        public float maxStrain;

        public int isBroken;
        public int brokenSegmentIndex;
        public float breakTension;
        public float breakStrain;
    }
}