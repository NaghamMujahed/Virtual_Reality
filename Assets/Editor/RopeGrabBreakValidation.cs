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
    public static class RopeGrabBreakValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Paint Bucket Sim/Run Rope Grab Break Validation")]
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
                    Finish(batchMode, false, "Break validation prerequisites are missing.");
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

                float dt = timeController.GetSubstepDeltaTime();
                for (int i = 0; i < 90; i++)
                    Step(context, environment, timeController, rope, bucket, coupling, dt);

                if (rope.IsBroken)
                {
                    Finish(batchMode, false, "Rope broke before the overextension grab.");
                    return;
                }

                int segment = Mathf.Clamp(rope.ParticleCount / 2, 1, rope.ParticleCount - 2);
                Vector3 p0 = rope.GetParticlePosition(segment);
                Vector3 p1 = rope.GetParticlePosition(segment + 1);
                Vector3 grabbedPoint = Vector3.Lerp(p0, p1, 0.45f);

                if (!rope.BeginGrab(segment, 0.45f, grabbedPoint))
                {
                    Finish(batchMode, false, "BeginGrab rejected the break-validation segment.");
                    return;
                }

                Vector3 target = grabbedPoint + new Vector3(1.45f, 0.05f, 0.20f);
                int breakStep = -1;
                float maxPreBreakBucketSpeed = 0.0f;
                float maxPreBreakAttachmentError = 0.0f;

                for (int step = 0; step < 260; step++)
                {
                    float t = Mathf.Clamp01(step / 180.0f);
                    Vector3 smoothedTarget = Vector3.Lerp(
                        grabbedPoint,
                        target,
                        Mathf.SmoothStep(0.0f, 1.0f, t));
                    rope.MoveGrab(smoothedTarget);

                    Step(context, environment, timeController, rope, bucket, coupling, dt);

                    if (!rope.IsBroken)
                    {
                        maxPreBreakBucketSpeed = Mathf.Max(
                            maxPreBreakBucketSpeed,
                            ToVector3(bucket.State.velocity).magnitude);
                        maxPreBreakAttachmentError = Mathf.Max(
                            maxPreBreakAttachmentError,
                            coupling.Diagnostics.attachmentError);
                        continue;
                    }

                    breakStep = step;
                    break;
                }

                rope.EndGrab();

                RopeDiagnostics diagnostics = rope.Diagnostics;
                bool valid =
                    breakStep >= 0 &&
                    rope.IsBroken &&
                    rope.BrokenSegmentIndex >= 0 &&
                    diagnostics.breakStrain >= rope.Config.breakStrain &&
                    maxPreBreakBucketSpeed < 2.4f &&
                    maxPreBreakAttachmentError < 0.02f;

                string report =
                    $"breakStep={breakStep}, brokenSegment={rope.BrokenSegmentIndex}, " +
                    $"breakStrain={diagnostics.breakStrain:F3}, threshold={rope.Config.breakStrain:F3}, " +
                    $"maxPreBreakBucketSpeed={maxPreBreakBucketSpeed:F2}m/s, " +
                    $"maxPreBreakAttachmentError={maxPreBreakAttachmentError:E3}m";

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
                Debug.Log($"ROPE_GRAB_BREAK_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"ROPE_GRAB_BREAK_VALIDATION_FAIL: {report}");

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
