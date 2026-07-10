using System;
using System.Collections.Generic;
using System.Reflection;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Environment;
using PaintBucketSim.Systems.Rope;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    public static class RopeBucketPhysicalResponseValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const float LoadedPaintMassKg = 28.71f;

        [MenuItem("Paint Bucket Sim/Run Physical Swing Response Validation")]
        public static void RunInteractive()
        {
            Run(false);
        }

        public static void RunBatch()
        {
            Run(true);
        }

        private static void Run(bool batchMode)
        {
            RopeSystem rope = null;

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                rope = UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<
                        RopeBucketCouplingSystem>();
                Transform pivot = GameObject.Find("RopePivot")?.transform;
                EnvironmentConfig environmentConfig =
                    ResolveEnvironmentConfig(manager);

                if (manager == null || manager.Config == null ||
                    environmentConfig == null || rope == null ||
                    bucket == null || coupling == null || pivot == null)
                {
                    Finish(
                        batchMode,
                        false,
                        "Physical-response prerequisites are missing.");
                    return;
                }

                var context = new SimulationContext();
                context.Initialize(manager.Config, environmentConfig);
                var environment = new EnvironmentSystem();
                environment.Initialize(context);
                var timeController = new TimeStepController();
                timeController.Initialize(manager.Config);

                bucket.Initialize(context);
                rope.Initialize(context);
                coupling.Initialize(context);
                if (!bucket.IsInitialized || !rope.IsInitialized ||
                    !coupling.IsReady)
                {
                    Finish(batchMode, false, "Initialization failed.");
                    return;
                }

                float dt = timeController.GetSubstepDeltaTime();
                UpdateContextDiagnostics(context, timeController, dt);
                StepForSeconds(
                    4.0f,
                    dt,
                    context,
                    timeController,
                    rope,
                    bucket,
                    coupling);

                int endSegment = rope.ParticleCount - 2;
                Vector3 initialEnd = rope.GetRopeEndPosition();
                if (!rope.BeginGrab(endSegment, 1.0f, initialEnd))
                {
                    Finish(batchMode, false, "Endpoint grab failed.");
                    return;
                }

                float targetAngle = 20.0f * Mathf.Deg2Rad;
                float ropeLength = rope.Config.lengthMeters;
                Vector3 targetEnd =
                    pivot.position +
                    new Vector3(
                        Mathf.Sin(targetAngle),
                        -Mathf.Cos(targetAngle),
                        0.0f) * ropeLength;
                int pullSteps = Mathf.CeilToInt(3.0f / dt);
                for (int step = 0; step < pullSteps; step++)
                {
                    float t = (step + 1.0f) / pullSteps;
                    t = Mathf.SmoothStep(0.0f, 1.0f, t);
                    rope.MoveGrab(Vector3.Lerp(initialEnd, targetEnd, t));
                    StepOnce(
                        dt,
                        context,
                        timeController,
                        rope,
                        bucket,
                        coupling);
                }

                rope.MoveGrab(targetEnd);
                StepForSeconds(
                    1.0f,
                    dt,
                    context,
                    timeController,
                    rope,
                    bucket,
                    coupling);

                float releaseAngle = SignedSwingAngle(
                    pivot.position,
                    rope.GetRopeEndPosition());
                float bucketReleaseAngle = SignedSwingAngle(
                    pivot.position,
                    bucket.GetCenterOfMassWorld());
                float effectiveLength = Vector3.Distance(
                    pivot.position,
                    rope.GetRopeEndPosition());
                rope.EndGrab();

                var downwardCrossings = new List<float>();
                var turningAmplitudes = new List<float>();
                float previousAngle = releaseAngle;
                float previousRate = 0.0f;
                float maxAttachmentError = 0.0f;
                float maxFrameStepDegrees = 0.0f;
                float earlyEnergyMaximum = float.NegativeInfinity;
                float lateEnergyMaximum = float.NegativeInfinity;
                Vector3 previousTangent = rope.GetRopeEndTangent();
                Vector3 previousNormal = rope.GetRopeEndMaterialNormal();
                int responseSteps = Mathf.CeilToInt(14.0f / dt);

                for (int step = 0; step < responseSteps; step++)
                {
                    StepOnce(
                        dt,
                        context,
                        timeController,
                        rope,
                        bucket,
                        coupling);

                    float time = (step + 1) * dt;
                    float angle = SignedSwingAngle(
                        pivot.position,
                        rope.GetRopeEndPosition());
                    float rate = (angle - previousAngle) /
                        Mathf.Max(dt, 1e-6f);

                    if (previousAngle > 0.0f && angle <= 0.0f &&
                        time > 0.25f)
                    {
                        float fraction = previousAngle /
                            Mathf.Max(previousAngle - angle, 1e-7f);
                        downwardCrossings.Add(time - dt + fraction * dt);
                    }

                    if (step > 1 && previousRate * rate < 0.0f &&
                        Mathf.Abs(previousAngle) > 0.01f)
                    {
                        turningAmplitudes.Add(Mathf.Abs(previousAngle));
                    }

                    Vector3 tangent = rope.GetRopeEndTangent();
                    Vector3 normal = rope.GetRopeEndMaterialNormal();
                    Quaternion transport = Quaternion.FromToRotation(
                        previousTangent,
                        tangent);
                    Vector3 transportedNormal = Vector3.ProjectOnPlane(
                        transport * previousNormal,
                        tangent).normalized;
                    Vector3 currentNormal = Vector3.ProjectOnPlane(
                        normal,
                        tangent).normalized;
                    if (transportedNormal.sqrMagnitude > 1e-8f &&
                        currentNormal.sqrMagnitude > 1e-8f)
                    {
                        maxFrameStepDegrees = Mathf.Max(
                            maxFrameStepDegrees,
                            Vector3.Angle(
                                transportedNormal,
                                currentNormal));
                    }

                    float speedAtArc = effectiveLength * rate;
                    float energy =
                        bucket.State.mass * GravityMagnitude(context) *
                        effectiveLength * (1.0f - Mathf.Cos(angle)) +
                        0.5f * bucket.State.mass *
                        speedAtArc * speedAtArc;
                    if (time <= 1.0f)
                        earlyEnergyMaximum = Mathf.Max(
                            earlyEnergyMaximum,
                            energy);
                    if (time >= 8.0f)
                        lateEnergyMaximum = Mathf.Max(
                            lateEnergyMaximum,
                            energy);

                    maxAttachmentError = Mathf.Max(
                        maxAttachmentError,
                        coupling.Diagnostics.attachmentError);
                    previousAngle = angle;
                    previousRate = rate;
                    previousTangent = tangent;
                    previousNormal = normal;
                }

                float measuredPeriod = AveragePeriod(downwardCrossings);
                float gravity = Mathf.Max(
                    context.EnvironmentState.gravity.magnitude,
                    0.01f);
                float amplitude = Mathf.Abs(releaseAngle);
                float amplitudeSquared = amplitude * amplitude;
                float theoreticalPeriod =
                    2.0f * Mathf.PI *
                    Mathf.Sqrt(effectiveLength / gravity) *
                    (1.0f + amplitudeSquared / 16.0f +
                     11.0f * amplitudeSquared * amplitudeSquared / 3072.0f);
                float periodRatio = measuredPeriod / theoreticalPeriod;
                float amplitudeRetention = turningAmplitudes.Count >= 5
                    ? turningAmplitudes[4] /
                      Mathf.Max(turningAmplitudes[0], 1e-6f)
                    : 0.0f;
                float lateEnergyGrowth =
                    lateEnergyMaximum - earlyEnergyMaximum;
                RopeDiagnostics ropeDiagnostics = rope.Diagnostics;

                bool valid =
                    Mathf.Abs(releaseAngle) >= 12.0f * Mathf.Deg2Rad &&
                    downwardCrossings.Count >= 3 &&
                    turningAmplitudes.Count >= 5 &&
                    periodRatio >= 0.85f && periodRatio <= 1.18f &&
                    amplitudeRetention >= 0.30f &&
                    amplitudeRetention <= 1.02f &&
                    lateEnergyGrowth <= 5.0f &&
                    maxAttachmentError <= 0.01f &&
                    maxFrameStepDegrees <= 15.0f &&
                    Mathf.Abs(ropeDiagnostics.endpointTwistRadians) <= 0.15f &&
                    Mathf.Abs(
                        ropeDiagnostics.endpointTwistAngularVelocity) <= 2.0f &&
                    ropeDiagnostics.isBroken == 0;

                string report =
                    $"release={releaseAngle * Mathf.Rad2Deg:F2}deg, " +
                    $"bucketComRelease={bucketReleaseAngle * Mathf.Rad2Deg:F2}deg, " +
                    $"effectiveLength={effectiveLength:F3}m, " +
                    $"periodMeasured={measuredPeriod:F3}s, " +
                    $"periodTheory={theoreticalPeriod:F3}s, " +
                    $"periodRatio={periodRatio:F3}, " +
                    $"crossings={downwardCrossings.Count}, " +
                    $"turningPoints={turningAmplitudes.Count}, " +
                    $"turningDeg=[{string.Join(",", turningAmplitudes.ConvertAll(value => (value * Mathf.Rad2Deg).ToString("F2")))}], " +
                    $"amplitudeRetention2Cycles={amplitudeRetention:F3}, " +
                    $"lateEnergyGrowth={lateEnergyGrowth:F3}J, " +
                    $"attachmentError={maxAttachmentError:E3}m, " +
                    $"frameStepMax={maxFrameStepDegrees:F2}deg, " +
                    $"finalTwist={ropeDiagnostics.endpointTwistRadians * Mathf.Rad2Deg:F2}deg, " +
                    $"finalTwistSpeed={ropeDiagnostics.endpointTwistAngularVelocity:F3}rad/s";
                Finish(batchMode, valid, report);
            }
            catch (Exception exception)
            {
                Finish(batchMode, false, exception.ToString());
            }
            finally
            {
                rope?.EndGrab();
                rope?.Dispose();
            }
        }

        private static void StepForSeconds(
            float seconds,
            float dt,
            SimulationContext context,
            TimeStepController timeController,
            RopeSystem rope,
            BucketSystem bucket,
            RopeBucketCouplingSystem coupling)
        {
            int count = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < count; i++)
            {
                StepOnce(
                    dt,
                    context,
                    timeController,
                    rope,
                    bucket,
                    coupling);
            }
        }

        private static void StepOnce(
            float dt,
            SimulationContext context,
            TimeStepController timeController,
            RopeSystem rope,
            BucketSystem bucket,
            RopeBucketCouplingSystem coupling)
        {
            rope.Step(context, dt);
            bucket.Step(context, dt);
            coupling.Step(context, dt);
            bucket.SetContainedFluidLoad(
                LoadedPaintMassKg,
                new Unity.Mathematics.float3(0.0f, -0.13f, 0.0f),
                0.29f,
                dt);
            timeController.AdvanceSubstep(dt);
            UpdateContextDiagnostics(context, timeController, dt);
        }

        private static float SignedSwingAngle(
            Vector3 pivot,
            Vector3 center)
        {
            Vector3 offset = center - pivot;
            return Mathf.Atan2(offset.x, -offset.y);
        }

        private static float GravityMagnitude(SimulationContext context)
        {
            return Mathf.Max(
                context.EnvironmentState.gravity.magnitude,
                0.01f);
        }

        private static float AveragePeriod(List<float> crossings)
        {
            if (crossings.Count < 2)
                return float.NaN;

            float sum = 0.0f;
            for (int i = 1; i < crossings.Count; i++)
                sum += crossings[i] - crossings[i - 1];
            return sum / (crossings.Count - 1);
        }

        private static EnvironmentConfig ResolveEnvironmentConfig(
            SimulationManager manager)
        {
            FieldInfo field = typeof(SimulationManager).GetField(
                "environmentConfig",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(manager) as EnvironmentConfig;
        }

        private static void UpdateContextDiagnostics(
            SimulationContext context,
            TimeStepController timeController,
            float dt)
        {
            DiagnosticsFrame diagnostics = context.Diagnostics;
            diagnostics.simulationTime = timeController.SimulationTime;
            diagnostics.substepIndex = timeController.SubstepIndex;
            diagnostics.substepDeltaTime = dt;
            diagnostics.fixedDeltaTime =
                timeController.GetFixedDeltaTime();
            diagnostics.substeps = timeController.GetSubstepCount();
            context.Diagnostics = diagnostics;
        }

        private static void Finish(
            bool batchMode,
            bool success,
            string report)
        {
            if (success)
            {
                Debug.Log(
                    $"ROPE_BUCKET_PHYSICAL_RESPONSE_VALIDATION_PASS: {report}");
            }
            else
            {
                Debug.LogError(
                    $"ROPE_BUCKET_PHYSICAL_RESPONSE_VALIDATION_FAIL: {report}");
            }

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
