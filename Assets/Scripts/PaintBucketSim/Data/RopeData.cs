using System;
using PaintBucketSim.Runtime;
using Unity.Collections;
using Unity.Mathematics;

namespace PaintBucketSim.Data
{
    public sealed class RopeData : IDisposable
    {
        public int ParticleCount { get; private set; }
        public int SegmentCount { get; private set; }
        public int BendConstraintCount { get; private set; }

        public NativeArray<float3> Positions;
        public NativeArray<float3> PreviousPositions;
        public NativeArray<float3> Velocities;
        public NativeArray<float> InverseMasses;

        public NativeArray<float> StretchRestLengths;
        public NativeArray<float> StretchLambdas;

        public NativeArray<float> BendRestAngles;
        public NativeArray<float> BendRestLengths;
        public NativeArray<float> BendLambdas;

        // Material frame state. SegmentFrames map local +Z to segment tangent and
        // local +Y to the rope material normal after twist.
        public NativeArray<quaternion> SegmentFrames;
        public NativeArray<quaternion> SegmentRestFrames;

        public NativeArray<float> SegmentTwistAngles;
        public NativeArray<float> SegmentPreviousTwistAngles;
        public NativeArray<float> SegmentTwistAngularVelocities;
        public NativeArray<float> SegmentRestTwistAngles;
        public NativeArray<float> SegmentInverseTwistInertias;
        public NativeArray<float> TwistLambdas;

        public NativeArray<RopeDiagnostics> Diagnostics;
        public NativeArray<RopeBreakState> BreakState;

        public bool IsCreated =>
            Positions.IsCreated &&
            PreviousPositions.IsCreated &&
            Velocities.IsCreated &&
            InverseMasses.IsCreated;

        public void Allocate(int segmentCount, Allocator allocator)
        {
            Dispose();

            SegmentCount = math.max(2, segmentCount);
            ParticleCount = SegmentCount + 1;
            BendConstraintCount = math.max(0, SegmentCount - 1);

            Positions = new NativeArray<float3>(ParticleCount, allocator);
            PreviousPositions = new NativeArray<float3>(ParticleCount, allocator);
            Velocities = new NativeArray<float3>(ParticleCount, allocator);
            InverseMasses = new NativeArray<float>(ParticleCount, allocator);

            StretchRestLengths = new NativeArray<float>(SegmentCount, allocator);
            StretchLambdas = new NativeArray<float>(SegmentCount, allocator);

            BendRestAngles = new NativeArray<float>(BendConstraintCount, allocator);
            BendRestLengths = new NativeArray<float>(BendConstraintCount, allocator);
            BendLambdas = new NativeArray<float>(BendConstraintCount, allocator);

            SegmentFrames = new NativeArray<quaternion>(SegmentCount, allocator);
            SegmentRestFrames = new NativeArray<quaternion>(SegmentCount, allocator);

            SegmentTwistAngles = new NativeArray<float>(SegmentCount, allocator);
            SegmentPreviousTwistAngles = new NativeArray<float>(SegmentCount, allocator);
            SegmentTwistAngularVelocities = new NativeArray<float>(SegmentCount, allocator);
            SegmentRestTwistAngles = new NativeArray<float>(SegmentCount, allocator);
            SegmentInverseTwistInertias = new NativeArray<float>(SegmentCount, allocator);
            TwistLambdas = new NativeArray<float>(SegmentCount, allocator);

            Diagnostics = new NativeArray<RopeDiagnostics>(1, allocator);
            BreakState = new NativeArray<RopeBreakState>(1, allocator);
        }

        public void Dispose()
        {
            if (Positions.IsCreated) Positions.Dispose();
            if (PreviousPositions.IsCreated) PreviousPositions.Dispose();
            if (Velocities.IsCreated) Velocities.Dispose();
            if (InverseMasses.IsCreated) InverseMasses.Dispose();

            if (StretchRestLengths.IsCreated) StretchRestLengths.Dispose();
            if (StretchLambdas.IsCreated) StretchLambdas.Dispose();

            if (BendRestAngles.IsCreated) BendRestAngles.Dispose();
            if (BendRestLengths.IsCreated) BendRestLengths.Dispose();
            if (BendLambdas.IsCreated) BendLambdas.Dispose();

            if (SegmentFrames.IsCreated) SegmentFrames.Dispose();
            if (SegmentRestFrames.IsCreated) SegmentRestFrames.Dispose();

            if (SegmentTwistAngles.IsCreated) SegmentTwistAngles.Dispose();
            if (SegmentPreviousTwistAngles.IsCreated) SegmentPreviousTwistAngles.Dispose();
            if (SegmentTwistAngularVelocities.IsCreated) SegmentTwistAngularVelocities.Dispose();
            if (SegmentRestTwistAngles.IsCreated) SegmentRestTwistAngles.Dispose();
            if (SegmentInverseTwistInertias.IsCreated) SegmentInverseTwistInertias.Dispose();
            if (TwistLambdas.IsCreated) TwistLambdas.Dispose();

            if (Diagnostics.IsCreated) Diagnostics.Dispose();
            if (BreakState.IsCreated) BreakState.Dispose();

            ParticleCount = 0;
            SegmentCount = 0;
            BendConstraintCount = 0;
        }
    }
}
