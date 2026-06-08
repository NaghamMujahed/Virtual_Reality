using Unity.Mathematics;

namespace PaintBucketSim.Runtime
{
    public struct RopeBucketCouplingDiagnostics
    {
        public int enabled;
        public int active;
        public int iterations;

        public float attachmentError;
        public float maxAttachmentError;
        public float estimatedConstraintForce;

        public float3 ropeEndPosition;
        public float3 bucketAttachmentPosition;

        public float3 totalRopeCorrection;
        public float3 totalBucketLinearCorrection;
        public float3 totalBucketAngularCorrection;
    }
}