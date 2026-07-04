using System;
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
    public static class RopeBucketPivotMotionValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Paint Bucket Sim/Run Pivot Motion Validation")]
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
            Transform pivot = null;
            Vector3 originalPivotPosition = Vector3.zero;

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                rope = UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<RopeBucketCouplingSystem>();
                pivot = GameObject.Find("RopePivot")?.transform;

                if (manager == null || rope == null || bucket == null ||
                    coupling == null || pivot == null)
                {
                    Finish(
                        batchMode,
                        false,
                        "Required rope, bucket, coupling, manager, or RopePivot is missing.");
                    return;
                }

                EnvironmentConfig environmentConfig =
                    ResolveEnvironmentConfig(manager);
                if (manager.Config == null || environmentConfig == null)
                {
                    Finish(batchMode, false, "Simulation configs are missing.");
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

                if (!rope.IsInitialized || !bucket.IsInitialized ||
                    !coupling.IsReady)
                {
                    Finish(batchMode, false, "Rope/bucket initialization failed.");
                    return;
                }

                originalPivotPosition = pivot.position;
                float dt = timeController.GetSubstepDeltaTime();
                UpdateContextDiagnostics(context, timeController, dt);
                const int stepCount = 720;
                const int firstMoveStep = 360;
                const int returnMoveStep = 540;
                const int pivotMoveDurationSteps = 24;
                Vector3 movedPivotPosition =
                    originalPivotPosition +
                    new Vector3(0.18f, 0.035f, 0.07f);

                Vector3 previousBucketPosition = ToVector3(bucket.State.position);
                Quaternion previousBucketRotation = ToQuaternion(bucket.State.rotation);
                float maxPivotSpeed = 0.0f;
                float maxBucketStepSpeed = 0.0f;
                float maxBucketAngularStepSpeed = 0.0f;
                float maxReportedBucketSpeed = 0.0f;
                float maxReportedBucketAngularSpeed = 0.0f;
                float maxAttachmentError = 0.0f;
                float maxRopeEndSpeed = 0.0f;
                float maxRestRopeEndSpeed = 0.0f;
                float maxAttachmentImpulse = 0.0f;
                int maxReportedBucketSpeedStep = -1;
                int maxRopeEndSpeedStep = -1;
                float maxRopeCorrection = 0.0f;
                float maxRestTiltDegrees = 0.0f;
                float maxSettledRestTiltDegrees = 0.0f;
                float finalRestTiltDegrees = 0.0f;
                float maxRestAngularSpeed = 0.0f;

                for (int step = 0; step < stepCount; step++)
                {
                    if (step >= firstMoveStep &&
                        step < firstMoveStep + pivotMoveDurationSteps)
                    {
                        float t =
                            (step - firstMoveStep + 1.0f) /
                            pivotMoveDurationSteps;
                        pivot.position = Vector3.Lerp(
                            originalPivotPosition,
                            movedPivotPosition,
                            t);
                    }
                    else if (step >= firstMoveStep + pivotMoveDurationSteps &&
                        step < returnMoveStep)
                    {
                        pivot.position = movedPivotPosition;
                    }
                    else if (step >= returnMoveStep &&
                        step < returnMoveStep + pivotMoveDurationSteps)
                    {
                        float t =
                            (step - returnMoveStep + 1.0f) /
                            pivotMoveDurationSteps;
                        pivot.position = Vector3.Lerp(
                            movedPivotPosition,
                            originalPivotPosition,
                            t);
                    }

                    rope.Step(context, dt);
                    bucket.Step(context, dt);
                    coupling.Step(context, dt);
                    bucket.SetContainedFluidLoad(
                        28.71f,
                        new Unity.Mathematics.float3(0.0f, -0.13f, 0.0f),
                        0.29f,
                        dt);

                    Vector3 bucketPosition = ToVector3(bucket.State.position);
                    Quaternion bucketRotation = ToQuaternion(bucket.State.rotation);
                    float bucketStepSpeed =
                        Vector3.Distance(bucketPosition, previousBucketPosition) /
                        Mathf.Max(dt, 1e-6f);
                    float bucketAngularStepSpeed =
                        Quaternion.Angle(previousBucketRotation, bucketRotation) *
                        Mathf.Deg2Rad /
                        Mathf.Max(dt, 1e-6f);

                    maxPivotSpeed = Mathf.Max(
                        maxPivotSpeed,
                        rope.GetSimulatedPivotVelocity().magnitude);
                    maxBucketStepSpeed = Mathf.Max(
                        maxBucketStepSpeed,
                        bucketStepSpeed);
                    maxBucketAngularStepSpeed = Mathf.Max(
                        maxBucketAngularStepSpeed,
                        bucketAngularStepSpeed);
                    if (bucket.Diagnostics.speed > maxReportedBucketSpeed)
                    {
                        maxReportedBucketSpeed = bucket.Diagnostics.speed;
                        maxReportedBucketSpeedStep = step;
                    }
                    maxReportedBucketAngularSpeed = Mathf.Max(
                        maxReportedBucketAngularSpeed,
                        bucket.Diagnostics.angularSpeed);
                    maxAttachmentError = Mathf.Max(
                        maxAttachmentError,
                        coupling.Diagnostics.attachmentError);
                    float ropeEndSpeed = rope.GetRopeEndVelocity().magnitude;
                    if (ropeEndSpeed > maxRopeEndSpeed)
                    {
                        maxRopeEndSpeed = ropeEndSpeed;
                        maxRopeEndSpeedStep = step;
                    }
                    maxRopeCorrection = Mathf.Max(
                        maxRopeCorrection,
                        ToVector3(
                            coupling.Diagnostics.totalRopeCorrection).magnitude);
                    maxAttachmentImpulse = Mathf.Max(
                        maxAttachmentImpulse,
                        coupling.Diagnostics.attachmentImpulse);
                    if (step < firstMoveStep)
                    {
                        Vector3 bucketUp = bucketRotation * Vector3.up;
                        Vector3 supportUp = -rope.GetRopeEndTangent();
                        maxRestTiltDegrees = Mathf.Max(
                            maxRestTiltDegrees,
                            Vector3.Angle(bucketUp, supportUp));
                        float tiltDegrees = Vector3.Angle(bucketUp, supportUp);
                        if (step >= 120)
                        {
                            maxSettledRestTiltDegrees = Mathf.Max(
                                maxSettledRestTiltDegrees,
                                tiltDegrees);
                        }
                        finalRestTiltDegrees = tiltDegrees;
                        maxRestAngularSpeed = Mathf.Max(
                            maxRestAngularSpeed,
                            bucket.Diagnostics.angularSpeed);
                        maxRestRopeEndSpeed = Mathf.Max(
                            maxRestRopeEndSpeed,
                            rope.GetRopeEndVelocity().magnitude);
                    }

                    previousBucketPosition = bucketPosition;
                    previousBucketRotation = bucketRotation;
                    timeController.AdvanceSubstep(dt);
                    UpdateContextDiagnostics(context, timeController, dt);
                }

                RopeDiagnostics ropeDiagnostics = rope.Diagnostics;
                bool valid =
                    IsFinite(maxPivotSpeed) &&
                    IsFinite(maxBucketStepSpeed) &&
                    IsFinite(maxBucketAngularStepSpeed) &&
                    maxPivotSpeed <=
                    rope.Config.maxPivotSpeedMetersPerSecond + 1e-3f &&
                    maxBucketStepSpeed <= 4.0f &&
                    maxReportedBucketSpeed <= 4.0f &&
                    maxRestRopeEndSpeed <= 4.0f &&
                    maxBucketAngularStepSpeed <= 15.0f &&
                    maxRestTiltDegrees <= 25.0f &&
                    maxAttachmentError <= 0.01f &&
                    ropeDiagnostics.isBroken == 0 &&
                    ropeDiagnostics.currentLength <=
                    ropeDiagnostics.restLength * 1.04f;

                string report =
                    $"steps={stepCount}, dt={dt:F6}s, " +
                    $"maxPivotSpeed={maxPivotSpeed:F3}m/s, " +
                    $"maxBucketStepSpeed={maxBucketStepSpeed:F3}m/s, " +
                    $"maxBucketSpeed={maxReportedBucketSpeed:F3}m/s@{maxReportedBucketSpeedStep}, " +
                    $"maxBucketAngularStepSpeed={maxBucketAngularStepSpeed:F3}rad/s, " +
                    $"maxBucketAngularSpeed={maxReportedBucketAngularSpeed:F3}rad/s, " +
                    $"maxRopeEndSpeed={maxRopeEndSpeed:F3}m/s@{maxRopeEndSpeedStep}, " +
                    $"maxRestRopeEndSpeed={maxRestRopeEndSpeed:F3}m/s, " +
                    $"maxRopeCorrection={maxRopeCorrection:F4}m, " +
                    $"maxAttachmentImpulse={maxAttachmentImpulse:F3}Ns, " +
                    $"maxRestTilt={maxRestTiltDegrees:F2}deg, " +
                    $"maxSettledRestTilt={maxSettledRestTiltDegrees:F2}deg, " +
                    $"finalRestTilt={finalRestTiltDegrees:F2}deg, " +
                    $"maxRestAngularSpeed={maxRestAngularSpeed:F3}rad/s, " +
                    $"maxAttachmentError={maxAttachmentError:E3}m, " +
                    $"ropeLength={ropeDiagnostics.currentLength:F3}m, " +
                    $"maxStretch={ropeDiagnostics.maxStretchError:E3}m";

                Finish(batchMode, valid, report);
            }
            catch (Exception exception)
            {
                Finish(batchMode, false, exception.ToString());
            }
            finally
            {
                if (pivot != null)
                    pivot.position = originalPivotPosition;
                rope?.Dispose();
            }
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
            diagnostics.fixedDeltaTime = timeController.GetFixedDeltaTime();
            diagnostics.substeps = timeController.GetSubstepCount();
            context.Diagnostics = diagnostics;
        }

        private static Vector3 ToVector3(Unity.Mathematics.float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Quaternion ToQuaternion(Unity.Mathematics.quaternion value)
        {
            return new Quaternion(
                value.value.x,
                value.value.y,
                value.value.z,
                value.value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Finish(bool batchMode, bool success, string report)
        {
            if (success)
                Debug.Log($"ROPE_BUCKET_PIVOT_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"ROPE_BUCKET_PIVOT_VALIDATION_FAIL: {report}");

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
