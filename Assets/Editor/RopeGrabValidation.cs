using System;
using System.Reflection;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Environment;
using PaintBucketSim.Systems.Rope;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    public static class RopeGrabValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Paint Bucket Sim/Run Rope Grab Validation")]
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
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                RopeSystem rope =
                    UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<RopeBucketCouplingSystem>();

                EnvironmentConfig environmentConfig = ResolveEnvironmentConfig(manager);
                if (manager == null || manager.Config == null ||
                    environmentConfig == null || rope == null ||
                    bucket == null || coupling == null)
                {
                    Finish(batchMode, false, "Grab validation prerequisites are missing.");
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

                if (!rope.IsInitialized || !bucket.IsInitialized || !coupling.IsReady)
                {
                    Finish(batchMode, false, "Rope/bucket/coupling initialization failed.");
                    return;
                }

                float dt = timeController.GetSubstepDeltaTime();
                Vector3 initialBucketPosition = ToVector3(bucket.State.position);

                for (int i = 0; i < 90; i++)
                    Step(context, environment, timeController, rope, bucket, coupling, dt);

                int segment = Mathf.Clamp(rope.ParticleCount / 2, 1, rope.ParticleCount - 2);
                Vector3 p0 = rope.GetParticlePosition(segment);
                Vector3 p1 = rope.GetParticlePosition(segment + 1);
                Vector3 pickPoint = Vector3.Lerp(p0, p1, 0.45f);
                Ray pickRay = new Ray(pickPoint + Vector3.back * 1.25f, Vector3.forward);

                bool picked = rope.TryFindClosestSegment(
                    pickRay,
                    0.12f,
                    out int pickedSegment,
                    out float pickedT,
                    out Vector3 grabbedPoint,
                    out float pickDistance);

                if (!picked || pickedSegment < 1 || pickedSegment >= rope.ParticleCount - 1)
                {
                    Finish(batchMode, false, $"Failed to pick middle rope segment. distance={pickDistance:F3}m");
                    return;
                }

                if (!rope.BeginGrab(pickedSegment, pickedT, grabbedPoint))
                {
                    Finish(batchMode, false, "BeginGrab rejected the picked segment.");
                    return;
                }

                Vector3 target = grabbedPoint + new Vector3(0.22f, 0.04f, 0.09f);
                float maxBucketSpeed = 0.0f;
                float maxUpSpeed = 0.0f;
                float maxHeightGain = 0.0f;
                float maxGrabError = 0.0f;
                float maxAttachmentError = 0.0f;

                for (int step = 0; step < 180; step++)
                {
                    float t = Mathf.Clamp01(step / 90.0f);
                    Vector3 smoothedTarget = Vector3.Lerp(
                        grabbedPoint,
                        target,
                        Mathf.SmoothStep(0.0f, 1.0f, t));
                    rope.MoveGrab(smoothedTarget);

                    Step(context, environment, timeController, rope, bucket, coupling, dt);

                    Vector3 grabbedNow = Vector3.Lerp(
                        rope.GetParticlePosition(pickedSegment),
                        rope.GetParticlePosition(pickedSegment + 1),
                        pickedT);
                    float grabError = Vector3.Distance(
                        grabbedNow,
                        rope.GetGrabTargetPosition());
                    maxGrabError = Mathf.Max(maxGrabError, grabError);

                    Vector3 bucketVelocity = ToVector3(bucket.State.velocity);
                    maxBucketSpeed = Mathf.Max(maxBucketSpeed, bucketVelocity.magnitude);
                    maxUpSpeed = Mathf.Max(maxUpSpeed, bucketVelocity.y);
                    maxHeightGain = Mathf.Max(
                        maxHeightGain,
                        ToVector3(bucket.State.position).y - initialBucketPosition.y);

                    maxAttachmentError = Mathf.Max(
                        maxAttachmentError,
                        coupling.Diagnostics.attachmentError);
                }

                rope.EndGrab();

                for (int i = 0; i < 120; i++)
                    Step(context, environment, timeController, rope, bucket, coupling, dt);

                bool valid =
                    maxBucketSpeed < 2.4f &&
                    maxUpSpeed < 0.65f &&
                    maxHeightGain < 0.45f &&
                    maxGrabError < 0.18f &&
                    maxAttachmentError < 0.02f &&
                    !rope.IsGrabActive;

                string report =
                    $"pickedSegment={pickedSegment}, t={pickedT:F2}, pickDistance={pickDistance:F3}m, " +
                    $"maxBucketSpeed={maxBucketSpeed:F2}m/s, maxUp={maxUpSpeed:F2}m/s, " +
                    $"heightGain={maxHeightGain:F2}m, maxGrabError={maxGrabError:F3}m, " +
                    $"maxAttachmentError={maxAttachmentError:E3}m, activeAfterRelease={rope.IsGrabActive}";

                Finish(batchMode, valid, report);
            }
            catch (Exception exception)
            {
                Finish(batchMode, false, exception.ToString());
            }
        }

        private static void Step(
            SimulationContext context,
            EnvironmentSystem environment,
            TimeStepController timeController,
            RopeSystem rope,
            BucketSystem bucket,
            RopeBucketCouplingSystem coupling,
            float dt)
        {
            UpdateContextDiagnostics(context, timeController, dt);
            environment.Step(context, dt);
            rope.Step(context, dt);
            bucket.Step(context, dt);
            coupling.Step(context, dt);
            timeController.AdvanceSubstep(dt);
        }

        private static void UpdateContextDiagnostics(
            SimulationContext context,
            TimeStepController timeController,
            float dt)
        {
            var diagnostics = context.Diagnostics;
            diagnostics.initialized = true;
            diagnostics.fixedStepIndex = timeController.FixedStepIndex;
            diagnostics.substepIndex = timeController.SubstepIndex;
            diagnostics.simulationTime = timeController.SimulationTime;
            diagnostics.fixedDeltaTime = timeController.GetFixedDeltaTime();
            diagnostics.substepDeltaTime = dt;
            diagnostics.substeps = timeController.GetSubstepCount();
            context.Diagnostics = diagnostics;
        }

        private static EnvironmentConfig ResolveEnvironmentConfig(SimulationManager manager)
        {
            if (manager == null)
                return null;

            FieldInfo field = typeof(SimulationManager).GetField(
                "environmentConfig",
                BindingFlags.Instance | BindingFlags.NonPublic);

            return field?.GetValue(manager) as EnvironmentConfig;
        }

        private static Vector3 ToVector3(Unity.Mathematics.float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static void Finish(bool batchMode, bool success, string report)
        {
            if (success)
                Debug.Log($"ROPE_GRAB_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"ROPE_GRAB_VALIDATION_FAIL: {report}");

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
