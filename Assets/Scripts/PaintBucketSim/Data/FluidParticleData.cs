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

        public int NextParticleId { get; private set; }

        public NativeArray<int> ParticleIds;

        public NativeArray<float3> LocalPositions;

        public NativeArray<float3> Positions;
        public NativeArray<float3> PreviousPositions;
        public NativeArray<float3> Velocities;
        public NativeArray<float3> TempVelocities;
        public NativeArray<float3> DeltaPositions;

        public NativeArray<float> Masses;
        public NativeArray<float> Radii;

        public NativeArray<float> Densities;
        public NativeArray<float> Lambdas;

        public NativeArray<float4> Colors;
        public NativeArray<int> States;

        public NativeArray<float> Ages;
        public NativeArray<float> StateAges;

        public NativeArray<FluidDiagnostics> Diagnostics;

        public bool IsCreated =>
            ParticleIds.IsCreated &&
            Positions.IsCreated &&
            PreviousPositions.IsCreated &&
            Velocities.IsCreated &&
            TempVelocities.IsCreated &&
            DeltaPositions.IsCreated &&
            Masses.IsCreated &&
            States.IsCreated &&
            Ages.IsCreated &&
            StateAges.IsCreated;

        public void Allocate(int capacity, Allocator allocator)
        {
            Dispose();

            Capacity = math.max(1, capacity);
            Count = 0;
            NextParticleId = 1;

            ParticleIds = new NativeArray<int>(Capacity, allocator);

            LocalPositions = new NativeArray<float3>(Capacity, allocator);

            Positions = new NativeArray<float3>(Capacity, allocator);
            PreviousPositions = new NativeArray<float3>(Capacity, allocator);
            Velocities = new NativeArray<float3>(Capacity, allocator);
            TempVelocities = new NativeArray<float3>(Capacity, allocator);
            DeltaPositions = new NativeArray<float3>(Capacity, allocator);

            Masses = new NativeArray<float>(Capacity, allocator);
            Radii = new NativeArray<float>(Capacity, allocator);

            Densities = new NativeArray<float>(Capacity, allocator);
            Lambdas = new NativeArray<float>(Capacity, allocator);

            Colors = new NativeArray<float4>(Capacity, allocator);
            States = new NativeArray<int>(Capacity, allocator);

            Ages = new NativeArray<float>(Capacity, allocator);
            StateAges = new NativeArray<float>(Capacity, allocator);

            Diagnostics = new NativeArray<FluidDiagnostics>(1, allocator);

            ClearAllSlots();
        }

        public void ClearActive()
        {
            Count = 0;
        }

        public void ClearAllSlots()
        {
            for (int i = 0; i < Capacity; i++)
            {
                ParticleIds[i] = 0;

                LocalPositions[i] = float3.zero;

                Positions[i] = float3.zero;
                PreviousPositions[i] = float3.zero;
                Velocities[i] = float3.zero;
                TempVelocities[i] = float3.zero;
                DeltaPositions[i] = float3.zero;

                Masses[i] = 0.0f;
                Radii[i] = 0.0f;

                Densities[i] = 0.0f;
                Lambdas[i] = 0.0f;

                Colors[i] = float4.zero;
                States[i] = (int)FluidParticleState.Inactive;

                Ages[i] = 0.0f;
                StateAges[i] = 0.0f;
            }

            Count = 0;
        }

        public void SetCount(int count)
        {
            Count = math.clamp(count, 0, Capacity);
        }

        public bool HasFreeSlot()
        {
            return Count < Capacity;
        }

        public int SpawnParticle(
            float3 localPosition,
            float3 worldPosition,
            float3 velocity,
            float mass,
            float radius,
            float restDensity,
            float4 color,
            FluidParticleState state)
        {
            if (Count >= Capacity)
                return -1;

            int index = Count;
            Count++;

            ParticleIds[index] = NextParticleId;
            NextParticleId++;

            LocalPositions[index] = localPosition;

            Positions[index] = worldPosition;
            PreviousPositions[index] = worldPosition;
            Velocities[index] = velocity;
            TempVelocities[index] = float3.zero;
            DeltaPositions[index] = float3.zero;

            Masses[index] = mass;
            Radii[index] = radius;

            Densities[index] = restDensity;
            Lambdas[index] = 0.0f;

            Colors[index] = color;
            States[index] = (int)state;

            Ages[index] = 0.0f;
            StateAges[index] = 0.0f;

            return index;
        }

        public void SetState(int index, FluidParticleState newState)
        {
            if (index < 0 || index >= Count)
                return;

            FluidParticleState oldState = (FluidParticleState)States[index];

            if (oldState == newState)
                return;

            States[index] = (int)newState;
            StateAges[index] = 0.0f;
        }

        public void DeactivateSwapBack(int index)
        {
            if (index < 0 || index >= Count)
                return;

            int last = Count - 1;

            if (index != last)
                SwapParticles(index, last);

            ClearSlot(last);
            Count--;
        }

        public void SwapParticles(int a, int b)
        {
            if (a == b)
                return;

            Swap(ref ParticleIds, a, b);

            Swap(ref LocalPositions, a, b);

            Swap(ref Positions, a, b);
            Swap(ref PreviousPositions, a, b);
            Swap(ref Velocities, a, b);
            Swap(ref TempVelocities, a, b);
            Swap(ref DeltaPositions, a, b);

            Swap(ref Masses, a, b);
            Swap(ref Radii, a, b);

            Swap(ref Densities, a, b);
            Swap(ref Lambdas, a, b);

            Swap(ref Colors, a, b);
            Swap(ref States, a, b);

            Swap(ref Ages, a, b);
            Swap(ref StateAges, a, b);
        }

        private void ClearSlot(int index)
        {
            ParticleIds[index] = 0;

            LocalPositions[index] = float3.zero;

            Positions[index] = float3.zero;
            PreviousPositions[index] = float3.zero;
            Velocities[index] = float3.zero;
            TempVelocities[index] = float3.zero;
            DeltaPositions[index] = float3.zero;

            Masses[index] = 0.0f;
            Radii[index] = 0.0f;

            Densities[index] = 0.0f;
            Lambdas[index] = 0.0f;

            Colors[index] = float4.zero;
            States[index] = (int)FluidParticleState.Inactive;

            Ages[index] = 0.0f;
            StateAges[index] = 0.0f;
        }

        private static void Swap<T>(ref NativeArray<T> array, int a, int b)
            where T : struct
        {
            T temp = array[a];
            array[a] = array[b];
            array[b] = temp;
        }

        public void Dispose()
        {
            if (ParticleIds.IsCreated) ParticleIds.Dispose();

            if (LocalPositions.IsCreated) LocalPositions.Dispose();

            if (Positions.IsCreated) Positions.Dispose();
            if (PreviousPositions.IsCreated) PreviousPositions.Dispose();
            if (Velocities.IsCreated) Velocities.Dispose();
            if (TempVelocities.IsCreated) TempVelocities.Dispose();
            if (DeltaPositions.IsCreated) DeltaPositions.Dispose();

            if (Masses.IsCreated) Masses.Dispose();
            if (Radii.IsCreated) Radii.Dispose();

            if (Densities.IsCreated) Densities.Dispose();
            if (Lambdas.IsCreated) Lambdas.Dispose();

            if (Colors.IsCreated) Colors.Dispose();
            if (States.IsCreated) States.Dispose();

            if (Ages.IsCreated) Ages.Dispose();
            if (StateAges.IsCreated) StateAges.Dispose();

            if (Diagnostics.IsCreated) Diagnostics.Dispose();

            Count = 0;
            Capacity = 0;
            NextParticleId = 1;
        }
    }
}