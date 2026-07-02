using System.Collections.Generic;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Data;
using PaintBucketSim.Jobs;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Systems.Boundary
{
    public class BoundarySystem : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private BoundaryConfig boundaryConfig;

        [Header("Systems")]
        [SerializeField] private BucketSystem bucketSystem;

        private BoundaryData _data;
        private bool _initialized;

        public BoundaryConfig Config => boundaryConfig;
        public bool IsInitialized => _initialized && _data != null && _data.IsCreated;
        public int Count => IsInitialized ? _data.Count : 0;

        public NativeArray<float3> WorldPositionsNative => _data.WorldPositions;
        public NativeArray<float3> WorldNormalsNative => _data.WorldNormals;
        public NativeArray<float3> WorldVelocitiesNative => _data.WorldVelocities;
        public NativeArray<int> TypesNative => _data.Types;

        public BoundaryDiagnostics Diagnostics
        {
            get
            {
                if (!IsInitialized || !_data.Diagnostics.IsCreated)
                    return default;

                return _data.Diagnostics[0];
            }
        }

        private void Awake()
        {
            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize(SimulationContext context)
        {
            if (boundaryConfig == null)
            {
                Debug.LogError("BoundarySystem: Missing BoundaryConfig.");
                _initialized = false;
                return;
            }

            if (bucketSystem == null)
                bucketSystem = FindAnyObjectByType<BucketSystem>();

            if (bucketSystem == null || !bucketSystem.IsInitialized)
            {
                Debug.LogError("BoundarySystem: Missing initialized BucketSystem.");
                _initialized = false;
                return;
            }

            GenerateBoundaryParticles();

            _initialized = true;

            Step(context, 0.0f);
        }

        public void ResetSystem(SimulationContext context)
        {
            Initialize(context);
        }

        public void Dispose()
        {
            if (_data != null)
            {
                _data.Dispose();
                _data = null;
            }

            _initialized = false;
        }

        public void Step(SimulationContext context, float dt)
        {
            if (!IsInitialized || bucketSystem == null || !bucketSystem.IsInitialized)
                return;

            var job = new BoundaryUpdateFromBucketJob
            {
                bucketState = bucketSystem.State,

                localPositions = _data.LocalPositions,
                localNormals = _data.LocalNormals,

                worldPositions = _data.WorldPositions,
                worldNormals = _data.WorldNormals,
                worldVelocities = _data.WorldVelocities
            };

            JobHandle handle = job.Schedule(_data.Count, 64);
            handle.Complete();
        }

        public Vector3 GetWorldPosition(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return Vector3.zero;

            float3 p = _data.WorldPositions[index];
            return new Vector3(p.x, p.y, p.z);
        }

        public Vector3 GetWorldNormal(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return Vector3.up;

            float3 n = _data.WorldNormals[index];
            return new Vector3(n.x, n.y, n.z);
        }

        public Vector3 GetWorldVelocity(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return Vector3.zero;

            float3 v = _data.WorldVelocities[index];
            return new Vector3(v.x, v.y, v.z);
        }

        public BoundaryParticleType GetParticleType(int index)
        {
            if (!IsInitialized || index < 0 || index >= _data.Count)
                return BoundaryParticleType.Wall;

            return (BoundaryParticleType)_data.Types[index];
        }

        private void GenerateBoundaryParticles()
        {
            BucketConfig bucketConfig = bucketSystem.Config;

            var localPositions = new List<float3>(2048);
            var localNormals = new List<float3>(2048);
            var types = new List<int>(2048);

            if (bucketConfig.shapeType == BucketShapeType.CustomMeshVisualOnly)
            {
                Debug.LogWarning(
                    "BoundarySystem: CustomMeshVisualOnly uses cylinder/tapered approximation for physical boundary particles in B2.");
            }

            if (boundaryConfig.generateWall)
                GenerateWall(bucketConfig, localPositions, localNormals, types);

            if (boundaryConfig.generateBottom)
                GenerateBottom(bucketConfig, localPositions, localNormals, types);

            if (boundaryConfig.generateHoleEdges)
                GenerateHoleEdges(bucketConfig, localPositions, localNormals, types);

            if (_data == null)
                _data = new BoundaryData();

            _data.Allocate(localPositions.Count, Allocator.Persistent);

            int wallCount = 0;
            int bottomCount = 0;
            int holeEdgeCount = 0;

            for (int i = 0; i < localPositions.Count; i++)
            {
                _data.LocalPositions[i] = localPositions[i];
                _data.LocalNormals[i] = math.normalize(localNormals[i]);
                _data.Types[i] = types[i];

                if (types[i] == (int)BoundaryParticleType.Wall)
                    wallCount++;
                else if (types[i] == (int)BoundaryParticleType.Bottom)
                    bottomCount++;
                else if (types[i] == (int)BoundaryParticleType.HoleEdge)
                    holeEdgeCount++;
            }

            _data.Diagnostics[0] = new BoundaryDiagnostics
            {
                totalCount = localPositions.Count,
                wallCount = wallCount,
                bottomCount = bottomCount,
                holeEdgeCount = holeEdgeCount,
                particleSpacing = boundaryConfig.particleSpacingMeters
            };
        }

        private void GenerateWall(
            BucketConfig bucketConfig, List<float3> positions, List<float3> normals, List<int> types
        ) {
            float height = bucketConfig.heightMeters;
            float halfHeight = height * 0.5f;
            float spacing = boundaryConfig.particleSpacingMeters;

            int verticalLayers = Mathf.Max(2, Mathf.CeilToInt(height / spacing) + 1);

            for (int yIndex = 0; yIndex < verticalLayers; yIndex++)
            {
                float t = verticalLayers == 1
                    ? 0.0f
                    : (float)yIndex / (verticalLayers - 1);

                float y = Mathf.Lerp(-halfHeight, halfHeight, t);
                float radius = GetInnerRadiusAtT(bucketConfig, t);

                int radialCount = Mathf.Max(
                    boundaryConfig.minRadialParticles,
                    Mathf.CeilToInt(2.0f * Mathf.PI * radius / spacing)
                );

                for (int rIndex = 0; rIndex < radialCount; rIndex++)
                {
                    float angle = (float)rIndex / radialCount * Mathf.PI * 2.0f;

                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    float3 localPos = new float3(
                        cos * radius,
                        y,
                        sin * radius
                    );

                    // Inward normal toward the fluid volume.
                    float3 localNormal = math.normalize(new float3(
                        -cos,
                        0.0f,
                        -sin
                    ));

                    positions.Add(localPos);
                    normals.Add(localNormal);
                    types.Add((int)BoundaryParticleType.Wall);
                }
            }
        }

        private void GenerateBottom(
            BucketConfig bucketConfig,
            List<float3> positions,
            List<float3> normals,
            List<int> types)
        {
            float spacing = boundaryConfig.particleSpacingMeters;

            float y = -bucketConfig.heightMeters * 0.5f;
            float radius = GetInnerBottomRadius(bucketConfig);

            for (float x = -radius; x <= radius; x += spacing)
            {
                for (float z = -radius; z <= radius; z += spacing)
                {
                    float radialSq = x * x + z * z;

                    if (radialSq > radius * radius)
                        continue;

                    float3 candidate = new float3(x, y, z);

                    if (IsInsideAnyBottomHole(bucketConfig, candidate))
                        continue;

                    positions.Add(candidate);
                    normals.Add(new float3(0.0f, 1.0f, 0.0f)); // upward, toward fluid
                    types.Add((int)BoundaryParticleType.Bottom);
                }
            }
        }

        private void GenerateHoleEdges(
            BucketConfig bucketConfig,
            List<float3> positions,
            List<float3> normals,
            List<int> types)
        {
            if (bucketConfig.holes == null)
                return;

            float spacing = boundaryConfig.particleSpacingMeters;

            for (int h = 0; h < bucketConfig.holes.Length; h++)
            {
                BucketHoleConfig hole = bucketConfig.holes[h];
                if (hole == null || !hole.active)
                    continue;

                Vector3 centerV = bucketConfig.GetResolvedHoleLocalCenter(hole);
                Vector3 normalV = bucketConfig.GetResolvedHoleLocalNormal(hole);
                Vector3 tangentV = bucketConfig.GetResolvedHoleLocalTangent(hole);
                Vector3 bitangentV = bucketConfig.GetResolvedHoleLocalBitangent(hole);

                float3 center = new float3(centerV.x, centerV.y, centerV.z);
                float3 normal = math.normalize(new float3(normalV.x, normalV.y, normalV.z));
                float3 tangentA = math.normalize(new float3(tangentV.x, tangentV.y, tangentV.z));
                float3 tangentB = math.normalize(new float3(bitangentV.x, bitangentV.y, bitangentV.z));

                Vector2 halfExtents = bucketConfig.GetResolvedHoleHalfExtents(hole);
                float estimatedPerimeter = EstimateHolePerimeter(hole, halfExtents);

                int radialCount = Mathf.Max(
                    8,
                    Mathf.CeilToInt(estimatedPerimeter / spacing)
                );

                int axialRings = Mathf.Max(1, boundaryConfig.holeEdgeAxialRings);
                float thickness = Mathf.Max(hole.wallThicknessMeters, bucketConfig.wallThicknessMeters);

                for (int a = 0; a < axialRings; a++)
                {
                    float axialT = axialRings == 1
                        ? 0.0f
                        : (float)a / (axialRings - 1);

                    // Move along the outlet normal to represent a short hole channel.
                    float3 ringCenter = center + normal * (axialT * thickness);

                    for (int i = 0; i < radialCount; i++)
                    {
                        float t = (float)i / radialCount;
                        Vector2 uv = SampleHoleBoundary(hole, halfExtents, t);

                        float3 localOffset = tangentA * uv.x + tangentB * uv.y;
                        float3 radial = math.normalizesafe(localOffset, tangentA);

                        float3 localPos = ringCenter + localOffset;

                        positions.Add(localPos);
                        normals.Add(radial);
                        types.Add((int)BoundaryParticleType.HoleEdge);
                    }
                }
            }
        }

        private bool IsInsideAnyBottomHole(BucketConfig bucketConfig, float3 localPoint)
        {
            if (bucketConfig.holes == null)
                return false;

            for (int i = 0; i < bucketConfig.holes.Length; i++)
            {
                BucketHoleConfig hole = bucketConfig.holes[i];

                if (hole == null || !hole.active)
                    continue;

                Vector3 centerV = bucketConfig.GetResolvedHoleLocalCenter(hole);
                Vector3 normalV = bucketConfig.GetResolvedHoleLocalNormal(hole);
                Vector3 tangentV = bucketConfig.GetResolvedHoleLocalTangent(hole);
                Vector3 bitangentV = bucketConfig.GetResolvedHoleLocalBitangent(hole);
                Vector2 halfExtents = bucketConfig.GetResolvedHoleHalfExtents(hole);

                // For B2 we only exclude bottom-like holes from bottom disk.
                if (Vector3.Dot(normalV.normalized, Vector3.down) < 0.8f)
                    continue;

                Vector3 deltaV = new Vector3(
                    localPoint.x - centerV.x,
                    localPoint.y - centerV.y,
                    localPoint.z - centerV.z
                );

                float u = Vector3.Dot(deltaV, tangentV);
                float v = Vector3.Dot(deltaV, bitangentV);

                float clearance =
                    boundaryConfig.holeClearanceMeters +
                    boundaryConfig.particleSpacingMeters * 0.5f;

                if (IsPointInsideHoleFootprint(
                        hole,
                        u,
                        v,
                        halfExtents,
                        clearance))
                    return true;
            }

            return false;
        }

        private static float EstimateHolePerimeter(
            BucketHoleConfig hole,
            Vector2 halfExtents)
        {
            float a = Mathf.Max(halfExtents.x, 0.001f);
            float b = Mathf.Max(halfExtents.y, 0.001f);

            switch (hole.shape)
            {
                case BucketHoleShape.Square:
                case BucketHoleShape.Rectangle:
                    return 4.0f * (a + b);

                case BucketHoleShape.Slot:
                {
                    float radius = Mathf.Min(a, b);
                    float halfLength = Mathf.Max(a, b);
                    float segmentHalfLength = Mathf.Max(halfLength - radius, 0.0f);
                    return 4.0f * segmentHalfLength + 2.0f * Mathf.PI * radius;
                }

                case BucketHoleShape.Ellipse:
                {
                    // Ramanujan approximation.
                    float h = Mathf.Pow(a - b, 2.0f) / Mathf.Pow(a + b, 2.0f);
                    return Mathf.PI * (a + b) * (1.0f + 3.0f * h / (10.0f + Mathf.Sqrt(4.0f - 3.0f * h)));
                }

                case BucketHoleShape.Circular:
                default:
                    return 2.0f * Mathf.PI * a;
            }
        }

        private static Vector2 SampleHoleBoundary(
            BucketHoleConfig hole,
            Vector2 halfExtents,
            float t)
        {
            float a = Mathf.Max(halfExtents.x, 0.001f);
            float b = Mathf.Max(halfExtents.y, 0.001f);

            switch (hole.shape)
            {
                case BucketHoleShape.Square:
                case BucketHoleShape.Rectangle:
                {
                    float u = Mathf.Repeat(t, 1.0f) * 4.0f;
                    if (u < 1.0f)
                        return new Vector2(Mathf.Lerp(-a, a, u), -b);
                    if (u < 2.0f)
                        return new Vector2(a, Mathf.Lerp(-b, b, u - 1.0f));
                    if (u < 3.0f)
                        return new Vector2(Mathf.Lerp(a, -a, u - 2.0f), b);

                    return new Vector2(-a, Mathf.Lerp(b, -b, u - 3.0f));
                }

                case BucketHoleShape.Slot:
                {
                    float radius = Mathf.Max(b, 0.001f);
                    float segmentHalfLength = Mathf.Max(a - radius, 0.0f);
                    float angle = Mathf.Repeat(t, 1.0f) * Mathf.PI * 2.0f;
                    float capCenter = Mathf.Cos(angle) >= 0.0f
                        ? segmentHalfLength
                        : -segmentHalfLength;

                    return new Vector2(
                        capCenter + Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius
                    );
                }

                case BucketHoleShape.Ellipse:
                case BucketHoleShape.Circular:
                default:
                {
                    float angle = Mathf.Repeat(t, 1.0f) * Mathf.PI * 2.0f;
                    return new Vector2(
                        Mathf.Cos(angle) * a,
                        Mathf.Sin(angle) * b
                    );
                }
            }
        }

        private static bool IsPointInsideHoleFootprint(
            BucketHoleConfig hole,
            float u,
            float v,
            Vector2 halfExtents,
            float padding)
        {
            float a = Mathf.Max(halfExtents.x + padding, 0.001f);
            float b = Mathf.Max(halfExtents.y + padding, 0.001f);

            switch (hole.shape)
            {
                case BucketHoleShape.Square:
                case BucketHoleShape.Rectangle:
                    return Mathf.Abs(u) <= a && Mathf.Abs(v) <= b;

                case BucketHoleShape.Slot:
                {
                    float halfLength = Mathf.Max(a, b);
                    float radius = Mathf.Min(a, b);
                    float segmentHalfLength = Mathf.Max(0.0f, halfLength - radius);
                    float du = Mathf.Max(Mathf.Abs(u) - segmentHalfLength, 0.0f);
                    return du * du + v * v <= radius * radius;
                }

                case BucketHoleShape.Ellipse:
                case BucketHoleShape.Circular:
                default:
                {
                    float nx = u / a;
                    float ny = v / b;
                    return nx * nx + ny * ny <= 1.0f;
                }
            }
        }

        private float GetInnerRadiusAtT(BucketConfig bucketConfig, float t)
        {
            float outerRadius = Mathf.Lerp(
                bucketConfig.bottomRadiusMeters,
                bucketConfig.topRadiusMeters,
                t
            );

            if (bucketConfig.shapeType == BucketShapeType.Cylinder)
                outerRadius = bucketConfig.topRadiusMeters;

            float innerRadius =
                outerRadius -
                Mathf.Max(bucketConfig.wallThicknessMeters, 0.0f) -
                boundaryConfig.surfaceOffsetMeters;

            return Mathf.Max(0.01f, innerRadius);
        }

        private float GetInnerBottomRadius(BucketConfig bucketConfig)
        {
            float radius = bucketConfig.shapeType == BucketShapeType.Cylinder
                ? bucketConfig.topRadiusMeters
                : bucketConfig.bottomRadiusMeters;

            radius -= Mathf.Max(bucketConfig.wallThicknessMeters, 0.0f);
            radius -= boundaryConfig.surfaceOffsetMeters;

            return Mathf.Max(0.01f, radius);
        }

        private void BuildBasis(float3 normal, out float3 tangentA, out float3 tangentB)
        {
            float3 helper = math.abs(normal.y) < 0.95f
                ? new float3(0.0f, 1.0f, 0.0f)
                : new float3(1.0f, 0.0f, 0.0f);

            tangentA = math.normalize(math.cross(helper, normal));
            tangentB = math.normalize(math.cross(normal, tangentA));
        }
    }
}
