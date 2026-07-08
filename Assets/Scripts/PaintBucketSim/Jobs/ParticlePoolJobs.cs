using PaintBucketSim.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace PaintBucketSim.Jobs
{
    [BurstCompile]
    public struct ParticleAgeUpdateJob : IJobParallelFor
    {
        public float dt;

        [ReadOnly] public NativeArray<int> states;

        public NativeArray<float> ages;
        public NativeArray<float> stateAges;

        public void Execute(int index)
        {
            FluidParticleState state = (FluidParticleState)states[index];

            if (state == FluidParticleState.Inactive)
                return;

            ages[index] += dt;
            stateAges[index] += dt;
        }
    }
}