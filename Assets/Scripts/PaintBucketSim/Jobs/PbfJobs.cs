using PaintBucketSim.Runtime;
using PaintBucketSim.Utilities.SpatialHash;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PaintBucketSim.Jobs
{
    [BurstCompile]
    public struct BuildFluidHashJob : IJobParallelFor
    {
        public float cellSize;

        [ReadOnly] public NativeArray<float3> positions;

        public NativeParallelMultiHashMap<int, int>.ParallelWriter hashMap;

        public void Execute(int index)
        {
            int hash = SpatialHashUtility.HashCell(
                SpatialHashUtility.PositionToCell(positions[index], cellSize)
            );

            hashMap.Add(hash, index);
        }
    }

    [BurstCompile]
    public struct BuildBoundaryHashJob : IJobParallelFor
    {
        public float cellSize;

        [ReadOnly] public NativeArray<float3> boundaryPositions;

        public NativeParallelMultiHashMap<int, int>.ParallelWriter hashMap;

        public void Execute(int index)
        {
            int hash = SpatialHashUtility.HashCell(
                SpatialHashUtility.PositionToCell(boundaryPositions[index], cellSize)
            );

            hashMap.Add(hash, index);
        }
    }

    [BurstCompile]
    public struct PbfPredictJob : IJobParallelFor
    {
        public float dt;
        public float3 gravity;

        public NativeArray<float3> positions;
        public NativeArray<float3> previousPositions;
        public NativeArray<float3> velocities;
        public NativeArray<float3> deltaPositions;

        public void Execute(int index)
        {
            float3 p = positions[index];
            previousPositions[index] = p;

            float3 v = velocities[index];
            v += gravity * dt;

            p += v * dt;

            velocities[index] = v;
            positions[index] = p;
            deltaPositions[index] = float3.zero;
        }
    }

    [BurstCompile]
    public struct PbfDensityLambdaJob : IJobParallelFor
    {
        public float smoothingRadius;
        public float restDensity;
        public float lambdaEpsilon;
        public float cellSize;

        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> fluidHashMap;

        public NativeArray<float> densities;
        public NativeArray<float> lambdas;

        public void Execute(int index)
        {
            float3 xi = positions[index];

            float density = 0.0f;
            float3 gradI = float3.zero;
            float sumGradSq = 0.0f;

            int3 baseCell = SpatialHashUtility.PositionToCell(xi, cellSize);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 cell = baseCell + new int3(x, y, z);
                        int hash = SpatialHashUtility.HashCell(cell);

                        NativeParallelMultiHashMapIterator<int> iterator;
                        int j;

                        if (fluidHashMap.TryGetFirstValue(hash, out j, out iterator))
                        {
                            do
                            {
                                float3 xj = positions[j];
                                float3 rij = xi - xj;

                                float r2 = math.lengthsq(rij);

                                if (r2 <= smoothingRadius * smoothingRadius)
                                {
                                    density += masses[j] * Poly6(r2, smoothingRadius);

                                    if (j != index)
                                    {
                                        float3 grad = SpikyGradient(rij, smoothingRadius);
                                        float3 gradJ = -(masses[j] / restDensity) * grad;

                                        sumGradSq += math.lengthsq(gradJ);
                                        gradI -= gradJ;
                                    }
                                }
                            }
                            while (fluidHashMap.TryGetNextValue(out j, ref iterator));
                        }
                    }
                }
            }

            sumGradSq += math.lengthsq(gradI);

            float constraint = density / restDensity - 1.0f;
            float lambda = -constraint / (sumGradSq + lambdaEpsilon);

            densities[index] = density;
            lambdas[index] = lambda;
        }

        private static float Poly6(float r2, float h)
        {
            float h2 = h * h;

            if (r2 >= h2)
                return 0.0f;

            float x = h2 - r2;
            float coeff = 315.0f / (64.0f * math.PI * math.pow(h, 9.0f));

            return coeff * x * x * x;
        }

        private static float3 SpikyGradient(float3 r, float h)
        {
            float len = math.length(r);
            if (len <= 1e-7f || len >= h)
                return float3.zero;

            float coeff = -45.0f / (math.PI * math.pow(h, 6.0f));
            float x = h - len;

            return coeff * x * x * (r / len);
        }
    }

    [BurstCompile]
    public struct PbfPositionCorrectionJob : IJobParallelFor
    {
        public float smoothingRadius;
        public float restDensity;
        public float cellSize;

        public bool enableArtificialPressure;
        public float artificialPressureK;
        public int artificialPressureN;
        public float artificialPressureDeltaQRatio;

        public float maxPositionCorrection;

        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float> masses;
        [ReadOnly] public NativeArray<float> lambdas;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> fluidHashMap;

        public NativeArray<float3> deltaPositions;

        public void Execute(int index)
        {
            float3 xi = positions[index];
            float lambdaI = lambdas[index];

            float3 delta = float3.zero;

            float dq = artificialPressureDeltaQRatio * smoothingRadius;
            float wDeltaQ = Poly6(dq * dq, smoothingRadius);

            int3 baseCell = SpatialHashUtility.PositionToCell(xi, cellSize);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 cell = baseCell + new int3(x, y, z);
                        int hash = SpatialHashUtility.HashCell(cell);

                        NativeParallelMultiHashMapIterator<int> iterator;
                        int j;

                        if (fluidHashMap.TryGetFirstValue(hash, out j, out iterator))
                        {
                            do
                            {
                                if (j == index)
                                    continue;

                                float3 rij = xi - positions[j];
                                float r2 = math.lengthsq(rij);

                                if (r2 <= smoothingRadius * smoothingRadius)
                                {
                                    float scorr = 0.0f;

                                    if (enableArtificialPressure && wDeltaQ > 1e-8f)
                                    {
                                        float w = Poly6(r2, smoothingRadius);
                                        scorr =
                                            -artificialPressureK *
                                            math.pow(w / wDeltaQ, artificialPressureN);
                                    }

                                    float lambdaSum = lambdaI + lambdas[j] + scorr;
                                    float3 grad = SpikyGradient(rij, smoothingRadius);

                                    delta += (masses[j] / restDensity) * lambdaSum * grad;
                                }
                            }
                            while (fluidHashMap.TryGetNextValue(out j, ref iterator));
                        }
                    }
                }
            }

            float len = math.length(delta);
            if (len > maxPositionCorrection && len > 1e-8f)
            {
                delta = delta / len * maxPositionCorrection;
            }

            deltaPositions[index] = delta;
        }

        private static float Poly6(float r2, float h)
        {
            float h2 = h * h;

            if (r2 >= h2)
                return 0.0f;

            float x = h2 - r2;
            float coeff = 315.0f / (64.0f * math.PI * math.pow(h, 9.0f));

            return coeff * x * x * x;
        }

        private static float3 SpikyGradient(float3 r, float h)
        {
            float len = math.length(r);
            if (len <= 1e-7f || len >= h)
                return float3.zero;

            float coeff = -45.0f / (math.PI * math.pow(h, 6.0f));
            float x = h - len;

            return coeff * x * x * (r / len);
        }
    }

    [BurstCompile]
    public struct PbfApplyDeltaJob : IJobParallelFor
    {
        public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> deltaPositions;

        public void Execute(int index)
        {
            positions[index] += deltaPositions[index];
        }
    }

    [BurstCompile]
    public struct BoundaryParticleCollisionJob : IJobParallelFor
    {
        public float cellSize;
        public float smoothingRadius;
        public float boundaryRadiusMultiplier;
        public float strength;

        [ReadOnly] public NativeArray<float3> boundaryPositions;
        [ReadOnly] public NativeArray<float3> boundaryNormals;
        [ReadOnly] public NativeArray<float3> boundaryVelocities;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> boundaryHashMap;

        public NativeArray<float3> positions;
        public NativeArray<float3> velocities;
        [ReadOnly] public NativeArray<float> radii;

        public bool preventCollisionEnergyInjection;
        public NativeArray<float3> previousPositions;

        public void Execute(int index)
        {
            float3 p = positions[index];
            float rParticle = radii[index];

            float minDistance =
                rParticle +
                smoothingRadius * boundaryRadiusMultiplier;

            int3 baseCell = SpatialHashUtility.PositionToCell(p, cellSize);

            float3 correctionSum = float3.zero;
            int correctionCount = 0;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 cell = baseCell + new int3(x, y, z);
                        int hash = SpatialHashUtility.HashCell(cell);

                        NativeParallelMultiHashMapIterator<int> iterator;
                        int b;

                        if (boundaryHashMap.TryGetFirstValue(hash, out b, out iterator))
                        {
                            do
                            {
                                float3 bp = boundaryPositions[b];
                                float3 bn = math.normalizesafe(boundaryNormals[b], new float3(0, 1, 0));

                                float dist = math.length(p - bp);

                                if (dist < minDistance)
                                {
                                    float penetration = minDistance - dist;
                                    correctionSum += bn * penetration * strength;
                                    correctionCount++;

                                    // Lightly inherit boundary velocity when very close to moving walls.
                                    velocities[index] = math.lerp(
                                        velocities[index],
                                        boundaryVelocities[b],
                                        0.02f * strength
                                    );
                                }
                            }
                            while (boundaryHashMap.TryGetNextValue(out b, ref iterator));
                        }
                    }
                }
            }

            if (correctionCount > 0)
            {
                float3 correction = correctionSum / correctionCount;
                positions[index] += correction;

                if (preventCollisionEnergyInjection)
                {
                    previousPositions[index] += correction;
                }
            }
        }
    }

    [BurstCompile]
    public struct AnalyticBucketProjectionJob : IJobParallelFor
    {
        public float3 bucketPosition;
        public quaternion bucketRotation;
        public quaternion inverseBucketRotation;

        public float height;
        public float topRadius;
        public float bottomRadius;
        public float wallThickness;

        public int shapeType;

        public float projectionStrength;

        public float holeRadius;
        public float nearHoleHeight;
        public float nearHolePadding;

        public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float> radii;
        public NativeArray<int> states;

        public int topBoundaryMode;
        public float topBoundaryPadding;
        public bool preventProjectionEnergyInjection;

        public NativeArray<float3> previousPositions;

        public void Execute(int index)
        {
            float3 originalWorld = positions[index];
            float3 world = positions[index];
            float3 local = math.rotate(inverseBucketRotation, world - bucketPosition);

            float particleRadius = radii[index];

            float halfHeight = height * 0.5f;
            float bottomY = -halfHeight;
            float topY = halfHeight;

            if (topBoundaryMode == 1)
            {
                float maxY = topY - particleRadius - topBoundaryPadding;
                if (local.y > maxY)
                    local.y = math.lerp(local.y, maxY, projectionStrength);
            }
            else if (topBoundaryMode == 2)
            {
                if (local.y > topY + topBoundaryPadding)
                    states[index] = (int)FluidParticleState.Lost;
            }

            bool nearHole = false;

            float2 radial = new float2(local.x, local.z);
            float radialDistance = math.length(radial);

            if (local.y < bottomY + nearHoleHeight)
            {
                if (radialDistance <= holeRadius + nearHolePadding)
                    nearHole = true;
            }

            // Bottom projection. In F1 we keep liquid inside;
            // organized emission through the hole belongs to O1.
            float minY = bottomY + particleRadius;
            if (local.y < minY)
                local.y = math.lerp(local.y, minY, projectionStrength);

            // Wall projection only while inside bucket height.
            if (local.y <= topY)
            {
                float t = math.saturate((local.y + halfHeight) / height);

                float outerRadius = shapeType == 0
                    ? topRadius
                    : math.lerp(bottomRadius, topRadius, t);

                float innerRadius = math.max(outerRadius - wallThickness - particleRadius, 0.001f);

                if (radialDistance > innerRadius)
                {
                    float2 dir = radialDistance > 1e-7f
                        ? radial / radialDistance
                        : new float2(1, 0);

                    float2 projected = dir * math.lerp(radialDistance, innerRadius, projectionStrength);
                    local.x = projected.x;
                    local.z = projected.y;
                }
            }

            float3 projectedWorld = bucketPosition + math.rotate(bucketRotation, local);
            
            float3 correction = projectedWorld - originalWorld;

            positions[index] = projectedWorld;

            if (preventProjectionEnergyInjection)
            {
                previousPositions[index] += correction;
            }

            states[index] = nearHole ? (int)FluidParticleState.NearHole : (int)FluidParticleState.InsideFluid;
        }
    }

    [BurstCompile]
    public struct PbfVelocityUpdateJob : IJobParallelFor
    {
        public float dt;
        public float dampingPerSecond;
        public float maxSpeed;

        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> previousPositions;

        public NativeArray<float3> velocities;

        public void Execute(int index)
        {
            float invDt = 1.0f / math.max(dt, 1e-8f);

            float3 v = (positions[index] - previousPositions[index]) * invDt;

            if (dampingPerSecond > 0.0f)
            {
                float damping = math.exp(-dampingPerSecond * dt);
                v *= damping;
            }

            float speed = math.length(v);
            if (speed > maxSpeed)
                v = v / speed * maxSpeed;

            velocities[index] = v;
        }
    }

    [BurstCompile]
    public struct XsphViscosityJob : IJobParallelFor
    {
        public float smoothingRadius;
        public float cellSize;
        public float xsphStrength;
        public float maxVelocityChange;

        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> velocities;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> fluidHashMap;

        public NativeArray<float3> outputVelocities;

        public void Execute(int index)
        {
            float3 xi = positions[index];
            float3 vi = velocities[index];

            float3 weightedDelta = float3.zero;
            float weightSum = 0.0f;

            int3 baseCell = SpatialHashUtility.PositionToCell(xi, cellSize);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 cell = baseCell + new int3(x, y, z);
                        int hash = SpatialHashUtility.HashCell(cell);

                        NativeParallelMultiHashMapIterator<int> iterator;
                        int j;

                        if (fluidHashMap.TryGetFirstValue(hash, out j, out iterator))
                        {
                            do
                            {
                                if (j == index)
                                    continue;

                                float3 rij = xi - positions[j];
                                float r2 = math.lengthsq(rij);

                                if (r2 <= smoothingRadius * smoothingRadius)
                                {
                                    float w = Poly6(r2, smoothingRadius);
                                    weightedDelta += (velocities[j] - vi) * w;
                                    weightSum += w;
                                }
                            }
                            while (fluidHashMap.TryGetNextValue(out j, ref iterator));
                        }
                    }
                }
            }

            float3 dv = float3.zero;

            if (weightSum > 1e-8f)
                dv = xsphStrength * (weightedDelta / weightSum);

            float dvLen = math.length(dv);
            if (dvLen > maxVelocityChange && dvLen > 1e-8f)
                dv = dv / dvLen * maxVelocityChange;

            outputVelocities[index] = vi + dv;
        }

        private static float Poly6(float r2, float h)
        {
            float h2 = h * h;

            if (r2 >= h2)
                return 0.0f;

            float x = h2 - r2;
            float coeff = 315.0f / (64.0f * math.PI * math.pow(h, 9.0f));

            return coeff * x * x * x;
        }
    }

    [BurstCompile]
    public struct CopyFloat3ArrayJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> source;
        public NativeArray<float3> destination;

        public void Execute(int index)
        {
            destination[index] = source[index];
        }
    }
}