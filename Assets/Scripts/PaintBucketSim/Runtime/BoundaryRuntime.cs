namespace PaintBucketSim.Runtime
{
    public enum BoundaryParticleType
    {
        Wall = 0,
        Bottom = 1,
        HoleEdge = 2
    }

    public struct BoundaryDiagnostics
    {
        public int totalCount;
        public int wallCount;
        public int bottomCount;
        public int holeEdgeCount;

        public float particleSpacing;
    }
}