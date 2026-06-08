using PaintBucketSim.Runtime;

namespace PaintBucketSim.Data
{
    public sealed class BucketData
    {
        public BucketState State;
        public BucketAttachmentWorldState Attachment;
        public BucketHoleWorldState[] Holes;
        public BucketDiagnostics Diagnostics;

        public void AllocateHoles(int count)
        {
            if (count < 0)
                count = 0;

            Holes = new BucketHoleWorldState[count];
        }
    }
}