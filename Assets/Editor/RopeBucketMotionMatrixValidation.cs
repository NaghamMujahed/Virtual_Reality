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
    public static class RopeBucketMotionMatrixValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const float LoadedPaintMassKg = 28.71f;

        private enum MotionProfile
        {
            Static,
            SlowSweep,
            Reversals,
            Circle,
            VerticalPulse,
            AbruptStep,
            MassCycle,
            TwistPulse
        }

        private readonly struct Scenario
        {
            public readonly string Name;
            public readonly MotionProfile Profile;
            public readonly float PaintMassKg;
            public readonly float DriveSeconds;
            public readonly float SettleSeconds;
            public readonly bool Abrupt;
            public readonly int Substeps;

            public Scenario(
                string name,
                MotionProfile profile,
                float paintMassKg,
                float driveSeconds,
                float settleSeconds,
                bool abrupt = false,
                int substeps = 2)
            {
                Name = name;
                Profile = profile;
                PaintMassKg = paintMassKg;
                DriveSeconds = driveSeconds;
                SettleSeconds = settleSeconds;
                Abrupt = abrupt;
                Substeps = Mathf.Clamp(substeps, 1, 8);
            }
        }

        private readonly struct ScenarioResult
        {
            public readonly bool Valid;
            public readonly string Report;

            public ScenarioResult(bool valid, string report)
            {
                Valid = valid;
                Report = report;
            }
        }

        [MenuItem("Paint Bucket Sim/Run Rope Bucket Motion Matrix")]
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

                EnvironmentConfig environmentConfig =
                    ResolveEnvironmentConfig(manager);
                if (manager == null || manager.Config == null ||
                    environmentConfig == null || rope == null ||
                    bucket == null || coupling == null || pivot == null)
                {
                    Finish(
                        batchMode,
                        false,
                        "Motion matrix prerequisites are missing.");
                    return;
                }

                originalPivotPosition = pivot.position;
                Scenario[] scenarios =
                {
                    new Scenario(
                        "loaded-static",
                        MotionProfile.Static,
                        LoadedPaintMassKg,
                        0.0f,
                        10.0f),
                    new Scenario(
                        "loaded-slow-sweep",
                        MotionProfile.SlowSweep,
                        LoadedPaintMassKg,
                        12.0f,
                        8.0f),
                    new Scenario(
                        "loaded-reversals",
                        MotionProfile.Reversals,
                        LoadedPaintMassKg,
                        10.0f,
                        8.0f),
                    new Scenario(
                        "loaded-circle",
                        MotionProfile.Circle,
                        LoadedPaintMassKg,
                        10.0f,
                        8.0f),
                    new Scenario(
                        "empty-vertical-pulse",
                        MotionProfile.VerticalPulse,
                        0.0f,
                        8.0f,
                        8.0f),
                    new Scenario(
                        "loaded-abrupt-step",
                        MotionProfile.AbruptStep,
                        LoadedPaintMassKg,
                        2.0f,
                        8.0f,
                        true),
                    new Scenario(
                        "loaded-slow-sweep-substep1",
                        MotionProfile.SlowSweep,
                        LoadedPaintMassKg,
                        6.0f,
                        5.0f,
                        false,
                        1),
                    new Scenario(
                        "loaded-reversals-substep4",
                        MotionProfile.Reversals,
                        LoadedPaintMassKg,
                        6.0f,
                        5.0f,
                        false,
                        4),
                    new Scenario(
                        "fill-drain-transition",
                        MotionProfile.MassCycle,
                        0.0f,
                        10.0f,
                        8.0f),
                    new Scenario(
                        "loaded-axial-twist",
                        MotionProfile.TwistPulse,
                        LoadedPaintMassKg,
                        8.0f,
                        8.0f)
                };

                bool allValid = true;
                string combinedReport = string.Empty;

                foreach (Scenario scenario in scenarios)
                {
                    pivot.position = originalPivotPosition;
                    ScenarioResult result = RunScenario(
                        scenario,
                        manager.Config,
                        environmentConfig,
                        rope,
                        bucket,
                        coupling,
                        pivot,
                        originalPivotPosition);
                    allValid &= result.Valid;
                    combinedReport +=
                        (combinedReport.Length > 0 ? " | " : string.Empty) +
                        result.Report;
                    rope.Dispose();
                }

                Finish(batchMode, allValid, combinedReport);
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

        private static ScenarioResult RunScenario(
            Scenario scenario,
            SimulationConfig simulationConfig,
            EnvironmentConfig environmentConfig,
            RopeSystem rope,
            BucketSystem bucket,
            RopeBucketCouplingSystem coupling,
            Transform pivot,
            Vector3 originalPivotPosition)
        {
            SimulationConfig scenarioConfig =
                UnityEngine.Object.Instantiate(simulationConfig);
            scenarioConfig.substeps = scenario.Substeps;

            var context = new SimulationContext();
            context.Initialize(scenarioConfig, environmentConfig);
            var environment = new EnvironmentSystem();
            environment.Initialize(context);
            var timeController = new TimeStepController();
            timeController.Initialize(scenarioConfig);

            bucket.Initialize(context);
            rope.Initialize(context);
            coupling.Initialize(context);

            if (!bucket.IsInitialized || !rope.IsInitialized ||
                !coupling.IsReady)
            {
                UnityEngine.Object.DestroyImmediate(scenarioConfig);
                return new ScenarioResult(
                    false,
                    $"{scenario.Name}: initialization failed");
            }

            float dt = timeController.GetSubstepDeltaTime();
            UpdateContextDiagnostics(context, timeController, dt);
            int preDriveSteps = scenario.Profile == MotionProfile.Static
                ? 0
                : Mathf.CeilToInt(2.0f / dt);
            int driveSteps = Mathf.CeilToInt(scenario.DriveSeconds / dt);
            int settleSteps = Mathf.CeilToInt(scenario.SettleSeconds / dt);
            int totalSteps = preDriveSteps + driveSteps + settleSteps;
            int settleStartStep = preDriveSteps + driveSteps;
            int earlySettleEndStep =
                settleStartStep + Mathf.Min(settleSteps, Mathf.CeilToInt(1.0f / dt));

            Vector3 gravity = context.EnvironmentState.gravity;
            Vector3 initialCenter = bucket.GetCenterOfMassWorld();
            float baselineHeight = initialCenter.y;
            float maxSpeed = 0.0f;
            float maxAngularSpeed = 0.0f;
            float maxUpwardSpeed = 0.0f;
            float maxTilt = 0.0f;
            float maxHeightGain = 0.0f;
            float maxAttachmentError = 0.0f;
            float maxRopeSpeed = 0.0f;
            float maxEndpointTwist = 0.0f;
            float maxEndpointTwistSpeed = 0.0f;
            float maxAppliedTwistTorque = 0.0f;
            float earlySettleMaxEnergy = float.NegativeInfinity;
            float lateSettleMaxEnergy = float.NegativeInfinity;
            float finalEnergy = 0.0f;
            float finalSpeed = 0.0f;
            float finalAngularSpeed = 0.0f;

            for (int step = 0; step < totalSteps; step++)
            {
                if (step == preDriveSteps)
                    baselineHeight = bucket.GetCenterOfMassWorld().y;

                float driveTime = (step - preDriveSteps) * dt;
                pivot.position =
                    originalPivotPosition +
                    EvaluateMotion(
                        scenario.Profile,
                        driveTime,
                        scenario.DriveSeconds);

                rope.Step(context, dt);
                ApplyScenarioTorque(
                    scenario.Profile,
                    driveTime,
                    scenario.DriveSeconds,
                    rope,
                    bucket);
                bucket.Step(context, dt);
                coupling.Step(context, dt);
                float targetPaintMass = scenario.Profile == MotionProfile.MassCycle
                    ? EvaluatePaintMass(driveTime, scenario.DriveSeconds)
                    : scenario.PaintMassKg;
                bucket.SetContainedFluidLoad(
                    targetPaintMass,
                    new Unity.Mathematics.float3(0.0f, -0.13f, 0.0f),
                    targetPaintMass > 0.0f ? 0.29f : 0.01f,
                    dt);

                BucketState state = bucket.State;
                Quaternion rotation = new Quaternion(
                    state.rotation.value.x,
                    state.rotation.value.y,
                    state.rotation.value.z,
                    state.rotation.value.w);
                Vector3 velocity = new Vector3(
                    state.velocity.x,
                    state.velocity.y,
                    state.velocity.z);
                Vector3 center = bucket.GetCenterOfMassWorld();

                maxSpeed = Mathf.Max(maxSpeed, velocity.magnitude);
                maxAngularSpeed = Mathf.Max(
                    maxAngularSpeed,
                    bucket.Diagnostics.angularSpeed);
                maxUpwardSpeed = Mathf.Max(maxUpwardSpeed, velocity.y);
                maxTilt = Mathf.Max(
                    maxTilt,
                    Vector3.Angle(
                        rotation * Vector3.up,
                        -rope.GetRopeEndTangent()));
                maxHeightGain = Mathf.Max(
                    maxHeightGain,
                    center.y - baselineHeight);
                maxAttachmentError = Mathf.Max(
                    maxAttachmentError,
                    coupling.Diagnostics.attachmentError);
                maxRopeSpeed = Mathf.Max(
                    maxRopeSpeed,
                    rope.GetRopeEndVelocity().magnitude);
                maxEndpointTwist = Mathf.Max(
                    maxEndpointTwist,
                    Mathf.Abs(rope.Diagnostics.endpointTwistRadians));
                maxEndpointTwistSpeed = Mathf.Max(
                    maxEndpointTwistSpeed,
                    Mathf.Abs(
                        rope.Diagnostics.endpointTwistAngularVelocity));
                maxAppliedTwistTorque = Mathf.Max(
                    maxAppliedTwistTorque,
                    Mathf.Abs(coupling.Diagnostics.appliedTwistTorque));

                float mechanicalEnergy =
                    bucket.GetMechanicalEnergy(gravity) +
                    rope.GetMechanicalEnergy(gravity);
                if (step >= settleStartStep && step < earlySettleEndStep)
                    earlySettleMaxEnergy = Mathf.Max(
                        earlySettleMaxEnergy,
                        mechanicalEnergy);
                else if (step >= earlySettleEndStep)
                    lateSettleMaxEnergy = Mathf.Max(
                        lateSettleMaxEnergy,
                        mechanicalEnergy);
                finalEnergy = mechanicalEnergy;
                finalSpeed = velocity.magnitude;
                finalAngularSpeed = bucket.Diagnostics.angularSpeed;

                timeController.AdvanceSubstep(dt);
                UpdateContextDiagnostics(context, timeController, dt);
            }

            if (float.IsNegativeInfinity(earlySettleMaxEnergy))
                earlySettleMaxEnergy = finalEnergy;
            if (float.IsNegativeInfinity(lateSettleMaxEnergy))
                lateSettleMaxEnergy = finalEnergy;

            float lateEnergyGrowth =
                lateSettleMaxEnergy - earlySettleMaxEnergy;
            float speedLimit = scenario.Abrupt ? 8.0f : 4.5f;
            float angularSpeedLimit = scenario.Abrupt ? 16.0f : 9.0f;
            float upwardSpeedLimit = scenario.Abrupt ? 6.0f : 3.5f;
            float heightLimit = scenario.Abrupt ? 3.0f : 2.2f;
            float tiltLimit = scenario.Profile == MotionProfile.Static
                ? 35.0f
                : (scenario.Abrupt ? 150.0f : 95.0f);
            float finalSpeedLimit = scenario.Profile == MotionProfile.Static
                ? 0.75f
                : 2.0f;
            float finalAngularSpeedLimit =
                scenario.Profile == MotionProfile.Static ? 1.0f : 3.0f;
            float attachmentErrorLimit = scenario.Abrupt ? 0.06f : 0.015f;

            RopeDiagnostics ropeDiagnostics = rope.Diagnostics;
            bool valid =
                IsFinite(maxSpeed) &&
                IsFinite(maxAngularSpeed) &&
                IsFinite(lateEnergyGrowth) &&
                maxSpeed <= speedLimit &&
                maxAngularSpeed <= angularSpeedLimit &&
                maxUpwardSpeed <= upwardSpeedLimit &&
                maxHeightGain <= heightLimit &&
                maxTilt <= tiltLimit &&
                maxAttachmentError <= attachmentErrorLimit &&
                lateEnergyGrowth <= 75.0f &&
                finalSpeed <= finalSpeedLimit &&
                finalAngularSpeed <= finalAngularSpeedLimit &&
                maxEndpointTwistSpeed <=
                rope.Config.maxTwistAngularSpeedRadiansPerSecond + 0.1f &&
                maxAppliedTwistTorque <=
                coupling.Config.maxTwistTorque + 1e-4f &&
                ropeDiagnostics.isBroken == 0 &&
                ropeDiagnostics.currentLength <=
                ropeDiagnostics.restLength * 1.04f;

            string report =
                $"{scenario.Name}={(valid ? "PASS" : "FAIL")}" +
                $"[substeps={scenario.Substeps}, speed={maxSpeed:F2}, " +
                $"up={maxUpwardSpeed:F2}, " +
                $"angular={maxAngularSpeed:F2}, tilt={maxTilt:F1}, " +
                $"heightGain={maxHeightGain:F2}, " +
                $"ropeSpeed={maxRopeSpeed:F2}, " +
                $"twist={maxEndpointTwist * Mathf.Rad2Deg:F1}deg, " +
                $"twistSpeed={maxEndpointTwistSpeed:F2}, " +
                $"twistTorque={maxAppliedTwistTorque:F3}, " +
                $"attach={maxAttachmentError:E2}, " +
                $"lateEnergyGrowth={lateEnergyGrowth:F2}J, " +
                $"finalSpeed={finalSpeed:F2}, " +
                $"finalAngular={finalAngularSpeed:F2}, " +
                $"finalEnergy={finalEnergy:F1}J]";
            Debug.Log($"ROPE_BUCKET_MOTION_SCENARIO: {report}");
            UnityEngine.Object.DestroyImmediate(scenarioConfig);
            return new ScenarioResult(valid, report);
        }

        private static Vector3 EvaluateMotion(
            MotionProfile profile,
            float time,
            float duration)
        {
            if (profile == MotionProfile.Static ||
                time < 0.0f ||
                duration <= 0.0f ||
                time >= duration)
            {
                return Vector3.zero;
            }

            float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / duration));

            switch (profile)
            {
                case MotionProfile.SlowSweep:
                    return new Vector3(
                        0.0f,
                        0.0f,
                        0.13f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.35f * time) *
                        envelope);

                case MotionProfile.Reversals:
                    return new Vector3(
                        0.09f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.8f * time) *
                        envelope,
                        0.0f,
                        0.055f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.53f * time) *
                        envelope);

                case MotionProfile.Circle:
                    return new Vector3(
                        0.08f * Mathf.Sin(2.0f * Mathf.PI * 0.4f * time),
                        0.0f,
                        0.08f *
                        (1.0f - Mathf.Cos(2.0f * Mathf.PI * 0.4f * time)));

                case MotionProfile.VerticalPulse:
                    return new Vector3(
                        0.025f *
                        Mathf.Sin(2.0f * Mathf.PI * 1.1f * time) *
                        envelope,
                        0.04f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.5f * time) *
                        envelope,
                        0.0f);

                case MotionProfile.AbruptStep:
                    return time < duration * 0.5f
                        ? new Vector3(0.0f, 0.0f, 0.05f)
                        : Vector3.zero;

                case MotionProfile.MassCycle:
                    return new Vector3(
                        0.035f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.45f * time) *
                        envelope,
                        0.0f,
                        0.02f *
                        Mathf.Sin(2.0f * Mathf.PI * 0.31f * time) *
                        envelope);

                case MotionProfile.TwistPulse:
                    return Vector3.zero;

                default:
                    return Vector3.zero;
            }
        }

        private static void ApplyScenarioTorque(
            MotionProfile profile,
            float time,
            float duration,
            RopeSystem rope,
            BucketSystem bucket)
        {
            if (profile != MotionProfile.TwistPulse ||
                time < 0.0f ||
                duration <= 0.0f ||
                time >= duration)
            {
                return;
            }

            float envelope = Mathf.Sin(
                Mathf.PI * Mathf.Clamp01(time / duration));
            float magnitude =
                0.22f *
                Mathf.Sin(2.0f * Mathf.PI * 0.5f * time) *
                envelope;
            Vector3 tangent = rope.GetRopeEndTangent().normalized;
            Vector3 torque = tangent * magnitude;
            bucket.AddTorque(new Unity.Mathematics.float3(
                torque.x,
                torque.y,
                torque.z));
        }

        private static float EvaluatePaintMass(float time, float duration)
        {
            if (time < 0.0f || duration <= 0.0f || time >= duration)
                return 0.0f;

            float phase = Mathf.Clamp01(time / duration);
            if (phase < 0.3f)
                return LoadedPaintMassKg * Mathf.SmoothStep(0.0f, 1.0f, phase / 0.3f);
            if (phase < 0.65f)
                return LoadedPaintMassKg;

            return LoadedPaintMassKg *
                (1.0f - Mathf.SmoothStep(0.0f, 1.0f, (phase - 0.65f) / 0.35f));
        }

        private static EnvironmentConfig ResolveEnvironmentConfig(
            SimulationManager manager)
        {
            if (manager == null)
                return null;

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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Finish(bool batchMode, bool success, string report)
        {
            if (success)
                Debug.Log($"ROPE_BUCKET_MOTION_MATRIX_PASS: {report}");
            else
                Debug.LogError($"ROPE_BUCKET_MOTION_MATRIX_FAIL: {report}");

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
