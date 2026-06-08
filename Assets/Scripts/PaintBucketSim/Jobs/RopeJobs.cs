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

        public int brokenSegmentIndex;

        public NativeArray<float3> positions;

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
                SolveStretchConstraints(dt2);

                if (enableBending)
                {
                    if (bendingModel == 1)
                        SolveAngleBendConstraints(dt2);
                    else
                        SolveDistanceBendConstraints(dt2);
                }
            }
        }

        private void SolveStretchConstraints(float dt2)
        {
            float alphaTilde = stretchCompliance / dt2;

            for (int c = 0; c < stretchRestLengths.Length; c++)
            {
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
            if (index == 0 || inverseMasses[index] <= 0.0f)
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

            float maxTension = 0.0f;
            float maxStrain = 0.0f;
            int selectedSegment = -1;

            for (int c = 0; c < stretchRestLengths.Length; c++)
            {
                float3 p0 = positions[c];
                float3 p1 = positions[c + 1];

                float rest = math.max(stretchRestLengths[c], 1e-8f);
                float len = math.length(p1 - p0);

                float strain = math.max(0.0f, (len - rest) / rest);
                float tension = math.abs(stretchLambdas[c]) / dt2;

                if (tension > maxTension)
                    maxTension = tension;

                if (strain > maxStrain)
                {
                    maxStrain = strain;
                    selectedSegment = c;
                }

                bool breaksByTension = enableBreakByTension && tension > breakTension;
                bool breaksByStrain = enableBreakByStrain && strain > breakStrain;

                if (breaksByTension || breaksByStrain)
                {
                    state.isBroken = 1;
                    state.brokenSegmentIndex = c;
                    state.breakTime = simulationTime;
                    state.breakTension = tension;
                    state.breakStrain = strain;

                    breakState[0] = state;
                    return;
                }
            }

            if (selectedSegment < 0)
                selectedSegment = 0;
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
                isBroken = bs.isBroken,
                brokenSegmentIndex = bs.brokenSegmentIndex,
                breakTension = bs.breakTension,
                breakStrain = bs.breakStrain
            };

            diagnostics[0] = d;
        }
    }
}