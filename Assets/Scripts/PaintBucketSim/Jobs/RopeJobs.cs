using PaintBucketSim.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PaintBucketSim.Jobs
{
    [BurstCompile]
    public struct RopeApplyRelativeDampingJob : IJob
    {
        public float dt;
        public float stretchCompliance;
        public float dampingRatio;
        public int brokenSegmentIndex;

        public NativeArray<float3> positions;
        public NativeArray<float3> velocities;

        [ReadOnly]
        public NativeArray<float> inverseMasses;

        public void Execute()
        {
            if (dampingRatio <= 0.0f)
                return;

            float stiffness = stretchCompliance > 1e-12f
                ? 1.0f / stretchCompliance
                : 1.0e12f;

            for (int c = 0; c < positions.Length - 1; c++)
            {
                if (c == brokenSegmentIndex)
                    continue;

                int i0 = c;
                int i1 = c + 1;

                float w0 = inverseMasses[i0];
                float w1 = inverseMasses[i1];
                float wSum = w0 + w1;

                if (wSum <= 0.0f)
                    continue;

                float3 p0 = positions[i0];
                float3 p1 = positions[i1];

                float3 dir = p1 - p0;
                float len = math.length(dir);

                if (len < 1e-7f)
                    continue;

                float3 n = dir / len;

                float3 v0 = velocities[i0];
                float3 v1 = velocities[i1];

                float relVel = math.dot(v1 - v0, n);

                float effectiveMass = 1.0f / wSum;
                float cDamp = 2.0f * dampingRatio * math.sqrt(math.max(stiffness * effectiveMass, 0.0f));

                float factor = math.saturate(cDamp * dt);
                float impulseScalar = -factor * relVel / wSum;
                float3 impulse = impulseScalar * n;

                v0 -= w0 * impulse;
                v1 += w1 * impulse;

                velocities[i0] = v0;
                velocities[i1] = v1;
            }
        }
    }

    [BurstCompile]
    public struct RopePredictJob : IJobParallelFor
    {
        public float dt;
        public int integrationMode;

        public float3 gravity;
        public float3 pivotPosition;

        public float3 windVelocity;
        public int airDragMode;
        public float airDensity;
        public float airViscosity;
        public float ropeRadius;
        public float linearAirDragKgPerSecond;
        public float quadraticDragCoefficient;

        public NativeArray<float3> positions;
        public NativeArray<float3> previousPositions;
        public NativeArray<float3> velocities;

        [ReadOnly]
        public NativeArray<float> inverseMasses;

        public void Execute(int index)
        {
            float3 current = positions[index];

            if (index == 0)
            {
                positions[index] = pivotPosition;
                previousPositions[index] = pivotPosition;
                velocities[index] = float3.zero;
                return;
            }

            float invMass = inverseMasses[index];
            if (invMass <= 0.0f)
            {
                previousPositions[index] = current;
                velocities[index] = float3.zero;
                return;
            }

            float3 v = velocities[index];
            float3 acceleration = gravity;

            acceleration += ComputeAirAcceleration(v, invMass);

            if (integrationMode == 1)
            {
                // Position Verlet:
                // x_new = x + (x - x_prev) + a dt^2
                float3 previous = previousPositions[index];
                float3 verletVelocityLike = current - previous;

                float3 next = current + verletVelocityLike + acceleration * dt * dt;

                previousPositions[index] = current;
                positions[index] = next;
                velocities[index] = (next - current) / math.max(dt, 1e-8f);
            }
            else
            {
                // Semi-Implicit Euler:
                // v += a dt; x += v dt
                previousPositions[index] = current;

                v += acceleration * dt;
                current += v * dt;

                velocities[index] = v;
                positions[index] = current;
            }
        }

        private float3 ComputeAirAcceleration(float3 velocity, float invMass)
        {
            if (airDragMode == 0)
                return float3.zero;

            float3 relativeVelocity = velocity - windVelocity;

            if (math.lengthsq(relativeVelocity) < 1e-10f)
                return float3.zero;

            if (airDragMode == 1)
            {
                // F = -c v_rel
                float3 force = -linearAirDragKgPerSecond * relativeVelocity;
                return force * invMass;
            }

            // Quadratic drag:
            // F = -0.5 rho Cd A |v| v
            float speed = math.length(relativeVelocity);
            float area = math.PI * ropeRadius * ropeRadius;

            float3 forceQuadratic =
                -0.5f *
                airDensity *
                quadraticDragCoefficient *
                area *
                speed *
                relativeVelocity;

            return forceQuadratic * invMass;
        }
    }

    [BurstCompile]
    public struct RopeResetLambdasJob : IJob
    {
        public NativeArray<float> stretchLambdas;
        public NativeArray<float> bendLambdas;

        public void Execute()
        {
            for (int i = 0; i < stretchLambdas.Length; i++)
                stretchLambdas[i] = 0.0f;

            for (int i = 0; i < bendLambdas.Length; i++)
                bendLambdas[i] = 0.0f;
        }
    }

    [BurstCompile]
    public struct RopeSolveXpbdJob : IJob
    {
        public float dt;
        public int solverIterations;

        public float stretchCompliance;
        public float bendCompliance;

        public bool enableBending;
        public int bendingModel;
        public bool enforceMaximumSegmentStrain;
        public float maximumSegmentStrain;

        public bool enableGrabConstraint;
        public int grabSegmentIndex;
        public float grabSegmentT;
        public float3 grabTarget;
        public float grabCompliance;
        public float maxGrabCorrectionPerIteration;

        public int brokenSegmentIndex;

        public NativeArray<float3> positions;
        public NativeArray<float3> previousPositions;

        [ReadOnly]
        public NativeArray<float> inverseMasses;

        [ReadOnly]
        public NativeArray<float> stretchRestLengths;

        public NativeArray<float> stretchLambdas;

        [ReadOnly]
        public NativeArray<float> bendRestAngles;

        [ReadOnly]
        public NativeArray<float> bendRestLengths;

        public NativeArray<float> bendLambdas;

        public void Execute()
        {
            float dt2 = math.max(dt * dt, 1e-8f);

            for (int iteration = 0; iteration < solverIterations; iteration++)
            {
                SolveStretchConstraints(dt2, false);
                SolveStretchConstraints(dt2, true);

                if (enableBending)
                {
                    if (bendingModel == 1)
                        SolveAngleBendConstraints(dt2);
                    else
                        SolveDistanceBendConstraints(dt2);
                }

                if (enableGrabConstraint)
                    SolveGrabConstraint(dt2);
            }

            if (enforceMaximumSegmentStrain)
                EnforceMaximumStretch();
        }

        private void SolveGrabConstraint(float dt2)
        {
            if (grabSegmentIndex < 0 ||
                grabSegmentIndex >= stretchRestLengths.Length ||
                grabSegmentIndex == brokenSegmentIndex)
            {
                return;
            }

            int i0 = grabSegmentIndex;
            int i1 = grabSegmentIndex + 1;
            float t = math.saturate(grabSegmentT);
            float b0 = 1.0f - t;
            float b1 = t;
            float w0 = inverseMasses[i0];
            float w1 = inverseMasses[i1];
            float weightedInverseMass =
                b0 * b0 * w0 +
                b1 * b1 * w1;

            float alphaTilde = math.max(grabCompliance, 0.0f) / dt2;
            float denominator = weightedInverseMass + alphaTilde;
            if (denominator <= 1e-8f)
                return;

            float3 p0 = positions[i0];
            float3 p1 = positions[i1];
            float3 point = p0 * b0 + p1 * b1;
            float3 error = point - grabTarget;
            float errorLength = math.length(error);
            if (errorLength < 1e-6f)
                return;

            float maxCorrection = math.max(maxGrabCorrectionPerIteration, 0.001f);
            if (errorLength > maxCorrection)
                error *= maxCorrection / errorLength;

            float3 deltaLambda = -error / denominator;
            positions[i0] = p0 + w0 * b0 * deltaLambda;
            positions[i1] = p1 + w1 * b1 * deltaLambda;
        }

        private void EnforceMaximumStretch()
        {
            float strain = math.clamp(maximumSegmentStrain, 0.0f, 0.6f);

            if (enableGrabConstraint &&
                grabSegmentIndex >= 0 &&
                grabSegmentIndex < stretchRestLengths.Length)
            {
                // A grabbed point behaves like a temporary internal handle.
                // Clamp each side independently so the upper section cannot
                // push correction through the grab into the bucket side.
                EnforceMaximumStretchRange(0, grabSegmentIndex, strain);
                EnforceMaximumStretchRange(
                    grabSegmentIndex + 1,
                    stretchRestLengths.Length,
                    strain);
                return;
            }

            EnforceMaximumStretchRange(0, stretchRestLengths.Length, strain);
        }

        private void EnforceMaximumStretchRange(
            int startInclusive,
            int endExclusive,
            float strain)
        {
            int start = math.clamp(startInclusive, 0, stretchRestLengths.Length);
            int end = math.clamp(endExclusive, start, stretchRestLengths.Length);

            for (int c = start; c < end; c++)
            {
                if (c == brokenSegmentIndex)
                    continue;

                int i0 = c;
                int i1 = c + 1;
                float3 p0 = positions[i0];
                float3 p1 = positions[i1];
                float3 delta = p1 - p0;
                float length = math.length(delta);
                float maximumLength = stretchRestLengths[c] * (1.0f + strain);

                if (length <= maximumLength || length < 1e-7f)
                    continue;

                // Propagate the hard material limit within this anchored span.
                // XPBD still handles elastic motion before this safety pass.
                float3 corrected = p0 + delta / length * maximumLength;
                positions[i1] = corrected;

                // Preserve tangential motion, but remove outward relative
                // velocity at the unilateral material limit. This keeps the
                // projection from injecting anchor-teleport velocity without
                // allowing hidden free-fall velocity to accumulate.
                float safeDt = math.max(dt, 1e-8f);
                float3 v0 = (p0 - previousPositions[i0]) / safeDt;
                float3 v1 = (p1 - previousPositions[i1]) / safeDt;
                float3 n = delta / length;
                float separatingSpeed = math.dot(v1 - v0, n);
                if (separatingSpeed > 0.0f)
                    v1 -= n * separatingSpeed;

                previousPositions[i1] = corrected - v1 * safeDt;
            }
        }

        private void SolveStretchConstraints(float dt2, bool reverse)
        {
            float alphaTilde = stretchCompliance / dt2;
            int constraintCount = stretchRestLengths.Length;

            for (int passIndex = 0; passIndex < constraintCount; passIndex++)
            {
                int c = reverse
                    ? constraintCount - 1 - passIndex
                    : passIndex;

                if (c == brokenSegmentIndex)
                    continue;

                int i0 = c;
                int i1 = c + 1;

                SolveDistanceConstraint(
                    i0,
                    i1,
                    stretchRestLengths[c],
                    alphaTilde,
                    c,
                    stretchLambdas);
            }
        }

        private void SolveDistanceBendConstraints(float dt2)
        {
            float alphaTilde = bendCompliance / dt2;

            for (int c = 0; c < bendRestLengths.Length; c++)
            {
                if (c == brokenSegmentIndex || c + 1 == brokenSegmentIndex)
                    continue;

                int i0 = c;
                int i1 = c + 2;

                SolveDistanceConstraint(
                    i0,
                    i1,
                    bendRestLengths[c],
                    alphaTilde,
                    c,
                    bendLambdas);
            }
        }

        private void SolveAngleBendConstraints(float dt2)
        {
            float alphaTilde = bendCompliance / dt2;

            for (int c = 0; c < bendRestAngles.Length; c++)
            {
                if (c == brokenSegmentIndex || c + 1 == brokenSegmentIndex)
                    continue;

                SolveArccosAngleConstraint(c, alphaTilde);
            }
        }

        private void SolveDistanceConstraint(
            int i0,
            int i1,
            float restLength,
            float alphaTilde,
            int lambdaIndex,
            NativeArray<float> lambdas)
        {
            float w0 = inverseMasses[i0];
            float w1 = inverseMasses[i1];

            float wSum = w0 + w1;
            if (wSum <= 0.0f)
                return;

            float3 p0 = positions[i0];
            float3 p1 = positions[i1];

            float3 delta = p1 - p0;
            float length = math.length(delta);

            if (length < 1e-7f)
                return;

            float3 n = delta / length;
            float c = length - restLength;

            float lambda = lambdas[lambdaIndex];

            float denominator = wSum + alphaTilde;
            if (denominator <= 1e-8f)
                return;

            float deltaLambda = (-c - alphaTilde * lambda) / denominator;
            lambda += deltaLambda;

            float3 correction = deltaLambda * n;

            p0 += -w0 * correction;
            p1 += w1 * correction;

            positions[i0] = p0;
            positions[i1] = p1;
            lambdas[lambdaIndex] = lambda;
        }

        private void SolveArccosAngleConstraint(int c, float alphaTilde)
        {
            int i0 = c;
            int i1 = c + 1;
            int i2 = c + 2;

            float w0 = inverseMasses[i0];
            float w1 = inverseMasses[i1];
            float w2 = inverseMasses[i2];

            float wSum = w0 + w1 + w2;
            if (wSum <= 0.0f)
                return;

            float3 p0 = positions[i0];
            float3 p1 = positions[i1];
            float3 p2 = positions[i2];

            float3 s0 = p1 - p0;
            float3 s1 = p2 - p1;

            float l0 = math.length(s0);
            float l1 = math.length(s1);

            if (l0 < 1e-7f || l1 < 1e-7f)
                return;

            float3 d0 = s0 / l0;
            float3 d1 = s1 / l1;

            float dot = math.clamp(math.dot(d0, d1), -0.9999f, 0.9999f);
            float theta = math.acos(dot);
            float restTheta = bendRestAngles[c];

            float constraint = theta - restTheta;
            if (math.abs(constraint) < 1e-5f)
                return;

            // Approximate stable correction:
            // move middle point toward the line p0-p2 when angle is too large.
            float3 line = p2 - p0;
            float lineLenSq = math.lengthsq(line);
            if (lineLenSq < 1e-8f)
                return;

            float t = math.clamp(math.dot(p1 - p0, line) / lineLenSq, 0.0f, 1.0f);
            float3 closest = p0 + t * line;
            float3 toLine = closest - p1;

            float toLineLen = math.length(toLine);
            if (toLineLen < 1e-7f)
                return;

            float3 dir = toLine / toLineLen;

            float lambda = bendLambdas[c];
            float denominator = wSum + alphaTilde;
            if (denominator <= 1e-8f)
                return;

            float deltaLambda = (-constraint - alphaTilde * lambda) / denominator;
            lambda += deltaLambda;

            float averageLength = 0.5f * (l0 + l1);
            float3 correction = (-deltaLambda * averageLength) * dir;

            p1 += w1 * correction;
            p0 -= 0.5f * w0 * correction;
            p2 -= 0.5f * w2 * correction;

            positions[i0] = p0;
            positions[i1] = p1;
            positions[i2] = p2;

            bendLambdas[c] = lambda;
        }
    }

    [BurstCompile]
    public struct RopeVelocityUpdateJob : IJobParallelFor
    {
        public float dt;
        public float exponentialDampingPerSecond;

        public NativeArray<float3> positions;

        [ReadOnly]
        public NativeArray<float3> previousPositions;

        public NativeArray<float3> velocities;

        [ReadOnly]
        public NativeArray<float> inverseMasses;

        public void Execute(int index)
        {
            if (index == 0)
            {
                velocities[index] = float3.zero;
                return;
            }

            if (inverseMasses[index] <= 0.0f)
            {
                velocities[index] = float3.zero;
                return;
            }

            float invDt = 1.0f / math.max(dt, 1e-8f);
            float3 v = (positions[index] - previousPositions[index]) * invDt;

            if (exponentialDampingPerSecond > 0.0f)
            {
                float damping = math.exp(-exponentialDampingPerSecond * dt);
                v *= damping;
            }

            velocities[index] = v;
        }
    }

    [BurstCompile]
    public struct RopeMaterialFrameUpdateJob : IJob
    {
        public float dt;
        public int solverIterations;
        public float torsionCompliance;
        public float torsionPropagationStrength;
        public float topTwistAnchorStrength;
        public float maxTwistGradientRadians;
        public float maxTwistAngularSpeed;
        public float twistDamping;
        public float externalEndpointTorque;
        public int brokenSegmentIndex;

        [ReadOnly]
        public NativeArray<float3> positions;

        [ReadOnly]
        public NativeArray<float> segmentRestTwistAngles;

        public NativeArray<float> segmentTwistAngles;
        public NativeArray<float> segmentPreviousTwistAngles;
        public NativeArray<float> segmentTwistAngularVelocities;
        [ReadOnly]
        public NativeArray<float> segmentInverseTwistInertias;
        public NativeArray<float> twistLambdas;
        public NativeArray<quaternion> segmentFrames;

        public void Execute()
        {
            int segmentCount = segmentTwistAngles.Length;
            if (segmentCount <= 0 || positions.Length < 2)
                return;

            float safeDt = math.max(dt, 1e-6f);
            float dt2 = math.max(safeDt * safeDt, 1e-8f);
            int iterations = math.max(solverIterations, 0);
            float propagation = math.max(math.saturate(torsionPropagationStrength), 0.01f);
            float topAnchor = math.saturate(topTwistAnchorStrength);
            float maxGradient = math.max(maxTwistGradientRadians, 0.001f);
            float angularSpeedLimit = math.max(maxTwistAngularSpeed, 1.0f);
            float alpha = math.max(torsionCompliance, 0.0f) / propagation / dt2;

            for (int i = 0; i < segmentCount; i++)
            {
                segmentPreviousTwistAngles[i] = segmentTwistAngles[i];
                twistLambdas[i] = 0.0f;
            }

            int end = segmentCount - 1;
            if (brokenSegmentIndex < 0 && math.abs(externalEndpointTorque) > 1e-8f)
            {
                float inverseInertia = segmentInverseTwistInertias[end];
                segmentTwistAngularVelocities[end] +=
                    externalEndpointTorque * inverseInertia * safeDt;
            }

            for (int i = 0; i < segmentCount; i++)
            {
                float omega = math.clamp(
                    segmentTwistAngularVelocities[i],
                    -angularSpeedLimit,
                    angularSpeedLimit);
                segmentTwistAngles[i] = NormalizeAngleRadians(
                    segmentTwistAngles[i] + omega * safeDt);
            }

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if (topAnchor > 0.0f)
                {
                    float rest = segmentRestTwistAngles.Length > 0
                        ? segmentRestTwistAngles[0]
                        : 0.0f;
                    float c = NormalizeAngleRadians(segmentTwistAngles[0] - rest);
                    float inverseInertia = segmentInverseTwistInertias[0];
                    float topAlpha = math.max(torsionCompliance, 0.0f) /
                        math.max(topAnchor, 0.01f) / dt2;
                    float denominator = inverseInertia + topAlpha;

                    if (denominator > 1e-8f)
                    {
                        float deltaLambda =
                            (-c - topAlpha * twistLambdas[0]) / denominator;
                        twistLambdas[0] += deltaLambda;
                        segmentTwistAngles[0] = NormalizeAngleRadians(
                            segmentTwistAngles[0] + inverseInertia * deltaLambda);
                    }
                }

                for (int i = 1; i < segmentCount; i++)
                {
                    if (brokenSegmentIndex >= 0 &&
                        (i == brokenSegmentIndex || i - 1 == brokenSegmentIndex))
                    {
                        continue;
                    }

                    float restDelta = NormalizeAngleRadians(
                        segmentRestTwistAngles[i] -
                        segmentRestTwistAngles[i - 1]);
                    float c = NormalizeAngleRadians(
                        segmentTwistAngles[i] -
                        segmentTwistAngles[i - 1] -
                        restDelta);

                    float inverseInertia0 = segmentInverseTwistInertias[i - 1];
                    float inverseInertia1 = segmentInverseTwistInertias[i];
                    float denominator = inverseInertia0 + inverseInertia1 + alpha;
                    if (denominator <= 1e-8f)
                        continue;

                    float deltaLambda =
                        (-c - alpha * twistLambdas[i]) / denominator;
                    twistLambdas[i] += deltaLambda;

                    segmentTwistAngles[i - 1] = NormalizeAngleRadians(
                        segmentTwistAngles[i - 1] -
                        inverseInertia0 * deltaLambda);
                    segmentTwistAngles[i] = NormalizeAngleRadians(
                        segmentTwistAngles[i] +
                        inverseInertia1 * deltaLambda);
                }
            }

            float damping = math.exp(-math.max(twistDamping, 0.0f) * math.max(dt, 0.0f));
            for (int i = 0; i < segmentTwistAngularVelocities.Length; i++)
            {
                if (i > 0 && !(brokenSegmentIndex >= 0 &&
                    (i == brokenSegmentIndex || i - 1 == brokenSegmentIndex)))
                {
                    float delta = NormalizeAngleRadians(
                        segmentTwistAngles[i] - segmentTwistAngles[i - 1]);
                    if (math.abs(delta) > maxGradient)
                    {
                        segmentTwistAngles[i] = NormalizeAngleRadians(
                            segmentTwistAngles[i - 1] +
                            math.clamp(delta, -maxGradient, maxGradient));
                    }
                }

                float angleDelta = NormalizeAngleRadians(
                    segmentTwistAngles[i] - segmentPreviousTwistAngles[i]);
                segmentTwistAngularVelocities[i] = math.clamp(
                    angleDelta / safeDt * damping,
                    -angularSpeedLimit,
                    angularSpeedLimit);
            }

            RebuildFrames();
        }

        private void RebuildFrames()
        {
            float3 baseNormal = ChooseInitialNormal(GetSegmentTangent(0));

            for (int i = 0; i < segmentFrames.Length; i++)
            {
                float3 tangent = GetSegmentTangent(i);

                float3 transportedNormal =
                    baseNormal - tangent * math.dot(baseNormal, tangent);

                if (math.lengthsq(transportedNormal) < 1e-8f)
                    transportedNormal = ChooseInitialNormal(tangent);
                else
                    transportedNormal = math.normalize(transportedNormal);

                float3 binormal = math.normalize(math.cross(tangent, transportedNormal));
                float twist = segmentTwistAngles[i];
                float s;
                float c;
                math.sincos(twist, out s, out c);

                float3 materialNormal =
                    math.normalize(transportedNormal * c + binormal * s);

                segmentFrames[i] = quaternion.LookRotationSafe(tangent, materialNormal);

                baseNormal = transportedNormal;
            }
        }

        private float3 GetSegmentTangent(int segmentIndex)
        {
            int i0 = math.clamp(segmentIndex, 0, positions.Length - 2);
            int i1 = i0 + 1;

            float3 tangent = positions[i1] - positions[i0];
            if (math.lengthsq(tangent) < 1e-10f)
                return new float3(0.0f, -1.0f, 0.0f);

            return math.normalize(tangent);
        }

        private static float3 ChooseInitialNormal(float3 tangent)
        {
            float3 up = new float3(0.0f, 1.0f, 0.0f);
            float3 projected = up - tangent * math.dot(up, tangent);
            if (math.lengthsq(projected) > 1e-8f)
                return math.normalize(projected);

            float3 right = new float3(1.0f, 0.0f, 0.0f);
            projected = right - tangent * math.dot(right, tangent);
            if (math.lengthsq(projected) > 1e-8f)
                return math.normalize(projected);

            return new float3(0.0f, 0.0f, 1.0f);
        }

        private static float NormalizeAngleRadians(float angle)
        {
            const float TwoPi = math.PI * 2.0f;

            angle += math.PI;
            angle -= TwoPi * math.floor(angle / TwoPi);
            return angle - math.PI;
        }
    }

    [BurstCompile]
    public struct RopeBreakDetectionJob : IJob
    {
        public float dt;
        public float simulationTime;

        public bool enableBreakByTension;
        public bool enableBreakByStrain;
        public float breakTension;
        public float breakStrain;

        [ReadOnly]
        public NativeArray<float3> positions;

        [ReadOnly]
        public NativeArray<float> stretchRestLengths;

        [ReadOnly]
        public NativeArray<float> stretchLambdas;

        public NativeArray<RopeBreakState> breakState;

        public void Execute()
        {
            RopeBreakState state = breakState[0];
            if (state.isBroken != 0)
                return;

            float dt2 = math.max(dt * dt, 1e-8f);

            float strongestFailureRatio = 1.0f;
            int failingSegment = -1;

            for (int c = 0; c < stretchRestLengths.Length; c++)
            {
                float3 p0 = positions[c];
                float3 p1 = positions[c + 1];

                float rest = math.max(stretchRestLengths[c], 1e-8f);
                float len = math.length(p1 - p0);

                float strain = math.max(0.0f, (len - rest) / rest);
                float tension = math.abs(stretchLambdas[c]) / dt2;

                bool breaksByTension = enableBreakByTension && tension > breakTension;
                bool breaksByStrain = enableBreakByStrain && strain > breakStrain;

                if (breaksByTension || breaksByStrain)
                {
                    float tensionRatio = breaksByTension
                        ? tension / math.max(breakTension, 1e-6f)
                        : 0.0f;
                    float strainRatio = breaksByStrain
                        ? strain / math.max(breakStrain, 1e-6f)
                        : 0.0f;
                    float failureRatio = math.max(tensionRatio, strainRatio);

                    if (failureRatio >= strongestFailureRatio)
                    {
                        strongestFailureRatio = failureRatio;
                        failingSegment = c;
                    }
                }
            }

            if (failingSegment >= 0)
            {
                float3 p0 = positions[failingSegment];
                float3 p1 = positions[failingSegment + 1];
                float rest = math.max(stretchRestLengths[failingSegment], 1e-8f);
                float len = math.length(p1 - p0);

                state.isBroken = 1;
                state.brokenSegmentIndex = failingSegment;
                state.breakTime = simulationTime;
                state.breakTension = math.abs(stretchLambdas[failingSegment]) / dt2;
                state.breakStrain = math.max(0.0f, (len - rest) / rest);

                breakState[0] = state;
            }
        }
    }

    [BurstCompile]
    public struct RopeDiagnosticsJob : IJob
    {
        public float dt;

        [ReadOnly]
        public NativeArray<float3> positions;

        [ReadOnly]
        public NativeArray<float> stretchRestLengths;

        [ReadOnly]
        public NativeArray<float> stretchLambdas;

        [ReadOnly]
        public NativeArray<float> segmentTwistAngles;

        [ReadOnly]
        public NativeArray<float> segmentTwistAngularVelocities;

        [ReadOnly]
        public NativeArray<float> segmentInverseTwistInertias;

        [ReadOnly]
        public NativeArray<float> segmentRestTwistAngles;

        public float torsionStiffness;

        [ReadOnly]
        public NativeArray<RopeBreakState> breakState;

        public NativeArray<RopeDiagnostics> diagnostics;

        public void Execute()
        {
            float currentLength = 0.0f;
            float restLength = 0.0f;
            float maxError = 0.0f;
            float errorSum = 0.0f;
            float maxTension = 0.0f;
            float maxStrain = 0.0f;
            float maxBendAngle = 0.0f;
            float bendAngleSum = 0.0f;
            int bendAngleCount = 0;
            float endpointTwist = 0.0f;
            float endpointTwistAngularVelocity = 0.0f;
            float maxTwistGradient = 0.0f;
            float twistKineticEnergy = 0.0f;
            float twistElasticEnergy = 0.0f;

            float dt2 = math.max(dt * dt, 1e-8f);

            for (int c = 0; c < stretchRestLengths.Length; c++)
            {
                float3 p0 = positions[c];
                float3 p1 = positions[c + 1];

                float length = math.length(p1 - p0);
                float rest = math.max(stretchRestLengths[c], 1e-8f);

                float error = math.abs(length - rest);
                float strain = math.max(0.0f, (length - rest) / rest);

                currentLength += length;
                restLength += rest;
                errorSum += error;
                maxError = math.max(maxError, error);
                maxStrain = math.max(maxStrain, strain);

                float tensionEstimate = math.abs(stretchLambdas[c]) / dt2;
                maxTension = math.max(maxTension, tensionEstimate);
            }

            for (int i = 1; i < positions.Length - 1; i++)
            {
                float3 prev = positions[i] - positions[i - 1];
                float3 next = positions[i + 1] - positions[i];

                float prevLength = math.length(prev);
                float nextLength = math.length(next);

                if (prevLength < 1e-7f || nextLength < 1e-7f)
                    continue;

                float dot = math.clamp(math.dot(prev / prevLength, next / nextLength), -1.0f, 1.0f);
                float angle = math.acos(dot);

                maxBendAngle = math.max(maxBendAngle, angle);
                bendAngleSum += angle;
                bendAngleCount++;
            }

            if (segmentTwistAngles.Length > 0)
            {
                endpointTwist = segmentTwistAngles[segmentTwistAngles.Length - 1];
                if (segmentTwistAngularVelocities.Length > 0)
                {
                    endpointTwistAngularVelocity =
                        segmentTwistAngularVelocities[
                            segmentTwistAngularVelocities.Length - 1];
                }

                for (int i = 0; i < segmentTwistAngles.Length; i++)
                {
                    if (i < segmentInverseTwistInertias.Length)
                    {
                        float inverseInertia = segmentInverseTwistInertias[i];
                        float inertia = inverseInertia > 1e-8f
                            ? 1.0f / inverseInertia
                            : 0.0f;
                        float omega = i < segmentTwistAngularVelocities.Length
                            ? segmentTwistAngularVelocities[i]
                            : 0.0f;
                        twistKineticEnergy += 0.5f * inertia * omega * omega;
                    }

                    if (i == 0)
                        continue;

                    float restDelta = i < segmentRestTwistAngles.Length
                        ? segmentRestTwistAngles[i] -
                          segmentRestTwistAngles[i - 1]
                        : 0.0f;
                    float gradient = NormalizeAngleRadians(
                        segmentTwistAngles[i] -
                        segmentTwistAngles[i - 1] -
                        restDelta);
                    maxTwistGradient = math.max(
                        maxTwistGradient,
                        math.abs(gradient));
                    twistElasticEnergy +=
                        0.5f *
                        math.max(torsionStiffness, 0.0f) *
                        gradient *
                        gradient;
                }
            }

            RopeBreakState bs = breakState[0];

            RopeDiagnostics d = new RopeDiagnostics
            {
                particleCount = positions.Length,
                segmentCount = stretchRestLengths.Length,
                currentLength = currentLength,
                restLength = restLength,
                maxStretchError = maxError,
                averageStretchError = stretchRestLengths.Length > 0
                    ? errorSum / stretchRestLengths.Length
                    : 0.0f,
                maxTensionEstimate = maxTension,
                maxStrain = maxStrain,
                maxBendAngleRadians = maxBendAngle,
                averageBendAngleRadians = bendAngleCount > 0
                    ? bendAngleSum / bendAngleCount
                    : 0.0f,
                endpointTwistRadians = endpointTwist,
                endpointTwistAngularVelocity = endpointTwistAngularVelocity,
                maxTwistGradientRadians = maxTwistGradient,
                twistKineticEnergy = twistKineticEnergy,
                twistElasticEnergy = twistElasticEnergy,
                isBroken = bs.isBroken,
                brokenSegmentIndex = bs.brokenSegmentIndex,
                breakTension = bs.breakTension,
                breakStrain = bs.breakStrain
            };

            diagnostics[0] = d;
        }

        private static float NormalizeAngleRadians(float angle)
        {
            const float TwoPi = math.PI * 2.0f;

            angle += math.PI;
            angle -= TwoPi * math.floor(angle / TwoPi);
            return angle - math.PI;
        }
    }
}
