using System;
using System.Reflection;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Environment;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;
using PaintBucketSim.Systems.Rope;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Editor
{
    public static class BucketFluidTwoWayCouplingValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Paint Bucket Sim/Run Two-Way Fluid Coupling Validation")]
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
            PaintFluidSystem fluid = null;
            BoundarySystem boundary = null;
            RopeSystem rope = null;
            GpuFluidBufferSet gpuBuffers = null;

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                rope = UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<
                        RopeBucketCouplingSystem>();
                boundary = UnityEngine.Object.FindAnyObjectByType<
                    BoundarySystem>();
                fluid = UnityEngine.Object.FindAnyObjectByType<
                    PaintFluidSystem>();
                gpuBuffers = UnityEngine.Object.FindAnyObjectByType<
                    GpuFluidBufferSet>();
                EnvironmentConfig environmentConfig =
                    ResolveEnvironmentConfig(manager);

                if (manager == null || manager.Config == null ||
                    environmentConfig == null || bucket == null ||
                    rope == null || coupling == null || boundary == null ||
                    fluid == null || gpuBuffers == null)
                {
                    Finish(
                        batchMode,
                        false,
                        "Two-way fluid coupling prerequisites are missing.");
                    return;
                }

                var context = new SimulationContext();
                context.Initialize(manager.Config, environmentConfig);
                var environment = new EnvironmentSystem();
                environment.Initialize(context);
                var timeController = new TimeStepController();
                timeController.Initialize(manager.Config);
                float dt = timeController.GetSubstepDeltaTime();
                UpdateContextDiagnostics(context, timeController, dt);

                bucket.Initialize(context);
                rope.Initialize(context);
                coupling.Initialize(context);
                boundary.Initialize(context);
                fluid.Initialize(context);
                if (!bucket.IsInitialized || !rope.IsInitialized ||
                    !coupling.IsReady || !boundary.IsInitialized ||
                    !fluid.IsInitialized || !fluid.IsGpuSolverActive)
                {
                    Finish(
                        batchMode,
                        false,
                        "GPU fluid coupling initialization failed.");
                    return;
                }

                float maximumRelativeLinearMomentum = 0.0f;
                float maximumRelativeAngularMomentum = 0.0f;
                float maximumReactionLinearImpulse = 0.0f;
                float maximumReactionAngularImpulse = 0.0f;
                int maximumSampleStep = -1;
                const int stepCount = 72;

                for (int step = 0; step < stepCount; step++)
                {
                    rope.Step(context, dt);
                    if (step >= 12 && step < 42)
                    {
                        float phase = (step - 12) * dt;
                        bucket.AddForce(new float3(
                            140.0f * Mathf.Cos(phase * 9.0f),
                            0.0f,
                            85.0f * Mathf.Sin(phase * 7.0f)));
                        bucket.AddTorque(new float3(
                            0.0f,
                            2.5f * Mathf.Sin(phase * 8.0f),
                            0.7f * Mathf.Cos(phase * 6.0f)));
                    }

                    bucket.Step(context, dt);
                    coupling.Step(context, dt);
                    boundary.Step(context, dt);
                    fluid.Step(context, dt);

                    if ((step + 1) % 4 == 0)
                        AsyncGPUReadback.WaitAllRequests();

                    if (fluid.FluidCouplingReady)
                    {
                        BucketFluidCouplingSample sample =
                            fluid.LatestBucketCouplingSample;
                        maximumRelativeLinearMomentum = Mathf.Max(
                            maximumRelativeLinearMomentum,
                            math.length(
                                sample.relativeLinearMomentumLocal));
                        maximumRelativeAngularMomentum = Mathf.Max(
                            maximumRelativeAngularMomentum,
                            math.length(
                                sample.relativeAngularMomentumLocal));
                        maximumSampleStep = Mathf.Max(
                            maximumSampleStep,
                            sample.stepIndex);
                    }

                    BucketDiagnostics diagnostics = bucket.Diagnostics;
                    maximumReactionLinearImpulse = Mathf.Max(
                        maximumReactionLinearImpulse,
                        math.length(
                            diagnostics.fluidReactionLinearImpulse));
                    maximumReactionAngularImpulse = Mathf.Max(
                        maximumReactionAngularImpulse,
                        math.length(
                            diagnostics.fluidReactionAngularImpulse));

                    timeController.AdvanceSubstep(dt);
                    UpdateContextDiagnostics(context, timeController, dt);
                }

                AsyncGPUReadback.WaitAllRequests();

                BucketFluidCouplingSample latest =
                    fluid.LatestBucketCouplingSample;
                BucketDiagnostics finalBucket = bucket.Diagnostics;
                bool finite =
                    IsFinite(latest.massKg) &&
                    math.all(math.isfinite(latest.centerOfMassLocal)) &&
                    math.all(math.isfinite(
                        latest.relativeLinearMomentumLocal)) &&
                    math.all(math.isfinite(
                        latest.relativeAngularMomentumLocal));
                bool valid =
                    fluid.FluidCouplingReady &&
                    latest.valid &&
                    latest.containedParticleCount > 1000 &&
                    latest.massKg > 1.0f &&
                    latest.centerOfMassLocal.y >=
                        -bucket.Config.heightMeters * 0.55f &&
                    latest.centerOfMassLocal.y <=
                        bucket.Config.heightMeters * 0.55f &&
                    maximumSampleStep >= 4 &&
                    maximumRelativeLinearMomentum > 1e-4f &&
                    maximumRelativeAngularMomentum > 1e-5f &&
                    maximumReactionLinearImpulse > 1e-6f &&
                    maximumReactionAngularImpulse > 1e-7f &&
                    finalBucket.fluidCouplingSampleStep >= 0 &&
                    finite;

                string report =
                    $"steps={stepCount}, sampleStep={maximumSampleStep}, " +
                    $"particles={latest.containedParticleCount}, " +
                    $"sampleMass={latest.massKg:F3}kg, " +
                    $"bucketFluidMass={finalBucket.containedFluidMass:F3}kg, " +
                    $"comLocal=({latest.centerOfMassLocal.x:F4}," +
                    $"{latest.centerOfMassLocal.y:F4}," +
                    $"{latest.centerOfMassLocal.z:F4})m, " +
                    $"maxRelativeMomentum={maximumRelativeLinearMomentum:F5}kgm/s, " +
                    $"maxRelativeAngularMomentum={maximumRelativeAngularMomentum:F6}kgm2/s, " +
                    $"maxReactionImpulse={maximumReactionLinearImpulse:F6}Ns, " +
                    $"maxReactionAngularImpulse={maximumReactionAngularImpulse:F7}Nms, " +
                    $"appliedSampleStep={finalBucket.fluidCouplingSampleStep}";
                Finish(batchMode, valid, report);
            }
            catch (Exception exception)
            {
                Finish(batchMode, false, exception.ToString());
            }
            finally
            {
                AsyncGPUReadback.WaitAllRequests();
                fluid?.Dispose();
                gpuBuffers?.ReleaseBuffersPublic();
                boundary?.Dispose();
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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
                    $"BUCKET_FLUID_TWO_WAY_VALIDATION_PASS: {report}");
            }
            else
            {
                Debug.LogError(
                    $"BUCKET_FLUID_TWO_WAY_VALIDATION_FAIL: {report}");
            }

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
