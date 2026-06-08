using System;
using PaintBucketSim.Runtime;
using Unity.Collections;
using Unity.Mathematics;

namespace PaintBucketSim.Data
{
    public sealed class FluidParticleData : IDisposable
    {
        public int Count { get; private set; }
        public int Capacity { get; private set; }

        public NativeArray<float3> LocalPositions;

        public NativeArray<float3> Positions;
        public NativeArray<float3> PreviousPositions;
        public NativeArray<float3> Velocities;
        public NativeArray<float3> DeltaPositions;

        public NativeArray<float> Masses;
        public NativeArray<float> Radii;

        public NativeArray<float> Densities;
        public NativeArray<float> Lambdas;

        public NativeArray<float4> Colors;
        public NativeArray<int> States;

        public NativeArray<FluidDiagnostics> Diagnostics;

        public bool IsCreated =>
            Positions.IsCreated &&
            PreviousPositions.IsCreated &&
            Velocities.IsCreated &&
            DeltaPositions.IsCreated &&
            Masses.IsCreated &&
            States.IsCreated;

        public void Allocate(int capacity, Allocator allocator)
        {
            Dispose();

            Capacity = math.max(1, capacity);
            Count = 0;

            LocalPositions = new NativeArray<float3>(Capacity, allocator);

            Positions = new NativeArray<float3>(Capacity, allocator);
            PreviousPositions = new NativeArray<float3>(Capacity, allocator);
            Velocities = new NativeArray<float3>(Capacity, allocator);
            DeltaPositions = new NativeArray<float3>(Capacity, allocator);

            Masses = new NativeArray<float>(Capacity, allocator);
            Radii = new NativeArray<float>(Capacity, allocator);

            Densities = new NativeArray<float>(Capacity, allocator);
            Lambdas = new NativeArray<float>(Capacity, allocator);

            Colors = new NativeArray<float4>(Capacity, allocator);
            States = new NativeArray<int>(Capacity, allocator);

            Diagnostics = new NativeArray<FluidDiagnostics>(1, allocator);
        }

        public void SetCount(int count)
        {
            Count = math.clamp(count, 0, Capacity);
        }

        public void Dispose()
        {
            if (LocalPositions.IsCreated) LocalPositions.Dispose();

            if (Positions.IsCreated) Positions.Dispose();
            if (PreviousPositions.IsCreated) PreviousPositions.Dispose();
            if (Velocities.IsCreated) Velocities.Dispose();
            if (DeltaPositions.IsCreated) DeltaPositions.Dispose();

            if (Masses.IsCreated) Masses.Dispose();
            if (Radii.IsCreated) Radii.Dispose();

            if (Densities.IsCreated) Densities.Dispose();
            if (Lambdas.IsCreated) Lambdas.Dispose();

            if (Colors.IsCreated) Colors.Dispose();
            if (States.IsCreated) States.Dispose();

            if (Diagnostics.IsCreated) Diagnostics.Dispose();

            Count = 0;
            Capacity = 0;
        }
    }
}