using Unity.Mathematics;

namespace PaintBucketSim.Runtime
{
    public struct BucketState
    {
        public float3 position;
        public quaternion rotation;

        public float3 velocity;
        public float3 angularVelocity;

        public float mass;
        public float inverseMass;

        public float3 inertiaTensorBody;
        public float3 inverseInertiaTensorBody;
    }

    public struct BucketAttachmentWorldState
    {
        public float3 localPoint;
        public float3 worldPosition;
        public float3 worldVelocity;
    }

    public struct BucketHoleWorldState
    {
        public int active;
        public int shape;

        public float3 localCenter;
        public float3 localNormal;

        public float3 worldCenter;
        public float3 worldNormal;
        public float3 worldVelocity;

        public float radius;
        public float area;
        public float wallThickness;
    }

    public struct BucketDiagnostics
    {
        public float speed;
        public float angularSpeed;

        public float kineticEnergyLinear;
        public float kineticEnergyAngular;

        public float3 attachmentWorldPosition;
        public float3 attachmentWorldVelocity;

        public int holeCount;
    }
}