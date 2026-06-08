using System;
using PaintBucketSim.Runtime;
using Unity.Collections;
using Unity.Mathematics;

namespace PaintBucketSim.Data
{
    public sealed class BoundaryData : IDisposable
    {
        public int Count { get; private set; }

        public NativeArray<float3> LocalPositions;
        public NativeArray<float3> LocalNormals;

        public NativeArray<float3> WorldPositions;
        public NativeArray<float3> WorldNormals;
        public NativeArray<float3> WorldVelocities;

        public NativeArray<int> Types;

        public NativeArray<BoundaryDiagnostics> Diagnostics;

        public bool IsCreated =>
            LocalPositions.IsCreated &&
            LocalNormals.IsCreated &&
            WorldPositions.IsCreated &&
            WorldNormals.IsCreated &&
            WorldVelocities.IsCreated &&
            Types.IsCreated;

        public void Allocate(int count, Allocator allocator)
        {
            Dispose();

            Count = math.max(0, count);

            LocalPositions = new NativeArray<float3>(Count, allocator);
            LocalNormals = new NativeArray<float3>(Count, allocator);

            WorldPositions = new NativeArray<float3>(Count, allocator);
            WorldNormals = new NativeArray<float3>(Count, allocator);
            WorldVelocities = new NativeArray<float3>(Count, allocator);

            Types = new NativeArray<int>(Count, allocator);

            Diagnostics = new NativeArray<BoundaryDiagnostics>(1, allocator);
        }

        public void Dispose()
        {
            if (LocalPositions.IsCreated) LocalPositions.Dispose();
            if (LocalNormals.IsCreated) LocalNormals.Dispose();

            if (WorldPositions.IsCreated) WorldPositions.Dispose();
            if (WorldNormals.IsCreated) WorldNormals.Dispose();
            if (WorldVelocities.IsCreated) WorldVelocities.Dispose();

            if (Types.IsCreated) Types.Dispose();

            if (Diagnostics.IsCreated) Diagnostics.Dispose();

            Count = 0;
        }
    }
}