using PaintBucketSim.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PaintBucketSim.Jobs
{
    [BurstCompile]
    public struct BoundaryUpdateFromBucketJob : IJobParallelFor
    {
        [ReadOnly] public BucketState bucketState;

        [ReadOnly] public NativeArray<float3> localPositions;
        [ReadOnly] public NativeArray<float3> localNormals;

        public NativeArray<float3> worldPositions;
        public NativeArray<float3> worldNormals;
        public NativeArray<float3> worldVelocities;

        public void Execute(int index)
        {
            float3 localPosition = localPositions[index];
            float3 localNormal = localNormals[index];

            float3 rotatedPosition = math.rotate(bucketState.rotation, localPosition);
            float3 rotatedNormal = math.normalize(math.rotate(bucketState.rotation, localNormal));

            float3 worldPosition = bucketState.position + rotatedPosition;
            float3 worldVelocity =
                bucketState.velocity +
                math.cross(bucketState.angularVelocity, rotatedPosition);

            worldPositions[index] = worldPosition;
            worldNormals[index] = rotatedNormal;
            worldVelocities[index] = worldVelocity;
        }
    }
}