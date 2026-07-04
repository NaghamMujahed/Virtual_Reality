using System;
using System.IO;
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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    public static class RopeBucketFluidLoadValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        public static void RunBatch()
        {
            bool success = false;
            string report;

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                RopeSystem rope =
                    UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<RopeBucketCouplingSystem>();
                BoundarySystem boundary =
                    UnityEngine.Object.FindAnyObjectByType<BoundarySystem>();
                PaintFluidSystem fluid =
                    UnityEngine.Object.FindAnyObjectByType<PaintFluidSystem>();
                GpuParticleIndirectRenderer gpuRenderer =
                    UnityEngine.Object.FindAnyObjectByType<GpuParticleIndirectRenderer>();

                if (manager == null || bucket == null || rope == null ||
                    coupling == null || boundary == null || fluid == null ||
                    gpuRenderer == null)
                {
                    throw new InvalidOperationException(
                        "Full rope/bucket/fluid scene systems are missing.");
                }

                EnvironmentConfig environmentConfig =
                    ResolveEnvironmentConfig(manager);
                if (manager.Config == null || environmentConfig == null)
                    throw new InvalidOperationException("Simulation configs are missing.");

                var context = new SimulationContext();
                context.Initialize(manager.Config, environmentConfig);

                var environment = new EnvironmentSystem();
                environment.Initialize(context);

                var timeController = new TimeStepController();
                timeController.Initialize(manager.Config);
                float dt = timeController.GetSubstepDeltaTime();
                DiagnosticsFrame initialDiagnostics = context.Diagnostics;
                initialDiagnostics.substepDeltaTime = dt;
                initialDiagnostics.fixedDeltaTime =
                    timeController.GetFixedDeltaTime();
                initialDiagnostics.substeps =
                    timeController.GetSubstepCount();
                context.Diagnostics = initialDiagnostics;

                InvokePrivate(bucket.GetComponent<BucketRenderer>(), "Awake");
                InvokePrivate(rope.GetComponent<RopeRenderer>(), "Awake");
                InvokePrivate(gpuRenderer, "Awake");
                InvokePrivate(gpuRenderer, "OnEnable");

                bucket.Initialize(context);
                rope.Initialize(context);
                coupling.Initialize(context);
                boundary.Initialize(context);
                fluid.Initialize(context);

                if (!bucket.IsInitialized || !rope.IsInitialized ||
                    !coupling.IsReady || !boundary.IsInitialized ||
                    !fluid.IsInitialized)
                {
                    throw new InvalidOperationException(
                        "A full-scene runtime system failed to initialize.");
                }

                int stepCount = GetIntArgument(
                    Environment.GetCommandLineArgs(),
                    "-ropeBucketFluidSteps",
                    16);
                float maxBucketTiltDegrees = 0.0f;
                float maxBucketAngularSpeed = 0.0f;
                float maxBucketSpeed = 0.0f;
                float maxAttachmentError = 0.0f;
                for (int step = 0; step < stepCount; step++)
                {
                    rope.Step(context, dt);
                    bucket.Step(context, dt);
                    coupling.Step(context, dt);
                    boundary.Step(context, dt);
                    fluid.Step(context, dt);

                    BucketState state = bucket.State;
                    Quaternion rotation = new Quaternion(
                        state.rotation.value.x,
                        state.rotation.value.y,
                        state.rotation.value.z,
                        state.rotation.value.w);
                    float tilt = Vector3.Angle(
                        rotation * Vector3.up,
                        -rope.GetRopeEndTangent());
                    maxBucketTiltDegrees = Mathf.Max(
                        maxBucketTiltDegrees,
                        tilt);
                    maxBucketAngularSpeed = Mathf.Max(
                        maxBucketAngularSpeed,
                        bucket.Diagnostics.angularSpeed);
                    maxBucketSpeed = Mathf.Max(
                        maxBucketSpeed,
                        bucket.Diagnostics.speed);
                    maxAttachmentError = Mathf.Max(
                        maxAttachmentError,
                        coupling.Diagnostics.attachmentError);

                    timeController.AdvanceSubstep(dt);
                    DiagnosticsFrame diagnostics = context.Diagnostics;
                    diagnostics.simulationTime = timeController.SimulationTime;
                    diagnostics.substepIndex = timeController.SubstepIndex;
                    diagnostics.substepDeltaTime = dt;
                    diagnostics.fixedDeltaTime =
                        timeController.GetFixedDeltaTime();
                    diagnostics.substeps =
                        timeController.GetSubstepCount();
                    context.Diagnostics = diagnostics;
                }

                FluidCalibrationStats calibration = fluid.CalibrationStats;
                FluidSolverStats solver = fluid.SolverStats;
                BucketDiagnostics bucketDiagnostics = bucket.Diagnostics;
                RopeDiagnostics ropeDiagnostics = rope.Diagnostics;

                InvokePrivate(bucket.GetComponent<BucketRenderer>(), "Start");
                InvokePrivate(bucket.GetComponent<BucketRenderer>(), "LateUpdate");
                InvokePrivate(rope.GetComponent<RopeRenderer>(), "LateUpdate");
                InvokePrivate(gpuRenderer, "LateUpdate");

                const string screenshotPath =
                    "Logs/RopeBucketFullFluidVisual.png";
                CaptureFullFluidScreenshot(
                    screenshotPath,
                    bucket);
                GpuParticleRenderStats renderStats = gpuRenderer.Stats;
                BucketRenderer bucketRenderer =
                    bucket.GetComponent<BucketRenderer>();

                success =
                    calibration.valid &&
                    calibration.initialPaintMassKg > 0.1f &&
                    bucketDiagnostics.containedFluidMass > 0.1f &&
                    bucketDiagnostics.totalMass > bucketDiagnostics.dryMass &&
                    IsFinite(bucketDiagnostics.centerOfMassWorld.x) &&
                    IsFinite(bucketDiagnostics.centerOfMassWorld.y) &&
                    IsFinite(bucketDiagnostics.centerOfMassWorld.z) &&
                    IsFinite(ropeDiagnostics.currentLength) &&
                    ropeDiagnostics.currentLength <=
                    ropeDiagnostics.restLength * 1.04f &&
                    maxBucketTiltDegrees <= 45.0f &&
                    maxBucketAngularSpeed <= 5.0f &&
                    maxAttachmentError <= 0.01f &&
                    renderStats.enabled &&
                    renderStats.uploadedParticles > 0 &&
                    bucketRenderer != null &&
                    bucketRenderer.FluidSurfaceVisible &&
                    File.Exists(screenshotPath) &&
                    solver.status != FluidSolverStatus.Error;

                report =
                    $"steps={stepCount}, solver={solver.solverType}, " +
                    $"status={solver.status}, particles={fluid.ParticleCount}, " +
                    $"initialPaintMass={calibration.initialPaintMassKg:F2}kg, " +
                    $"coupledPaintMass={bucketDiagnostics.containedFluidMass:F2}kg, " +
                    $"totalBucketMass={bucketDiagnostics.totalMass:F2}kg, " +
                    $"bucketComLocal=({bucketDiagnostics.centerOfMassLocal.x:F4}," +
                    $"{bucketDiagnostics.centerOfMassLocal.y:F4}," +
                    $"{bucketDiagnostics.centerOfMassLocal.z:F4})m, " +
                    $"gpuAverageLocalXZ=({solver.gpuAverageLocalX01:F4}," +
                    $"{solver.gpuAverageLocalZ01:F4}), " +
                    $"gpuAverageFillY={solver.gpuAverageFillHeight01:F4}, " +
                    $"maxBucketTilt={maxBucketTiltDegrees:F2}deg, " +
                    $"maxBucketSpeed={maxBucketSpeed:F3}m/s, " +
                    $"maxBucketAngularSpeed={maxBucketAngularSpeed:F3}rad/s, " +
                    $"maxAttachmentError={maxAttachmentError:E3}m, " +
                    $"ropeLength={ropeDiagnostics.currentLength:F3}m, " +
                    $"ropeStretch={ropeDiagnostics.maxStretchError:E3}m, " +
                    $"renderedParticles={renderStats.uploadedParticles}, " +
                    $"fluidSurfaceVisible={bucketRenderer.FluidSurfaceVisible}, " +
                    $"fluidSurfaceRadius={bucketRenderer.FluidSurfaceRadius:F3}m, " +
                    $"fluidSurfaceWorld={bucketRenderer.FluidSurfaceWorldPosition}, " +
                    $"screenshot={Path.GetFullPath(screenshotPath)}";
            }
            catch (Exception exception)
            {
                report = exception.ToString();
            }
            finally
            {
                PaintFluidSystem fluid =
                    UnityEngine.Object.FindAnyObjectByType<PaintFluidSystem>();
                fluid?.Dispose();

                GpuParticleIndirectRenderer gpuRenderer =
                    UnityEngine.Object.FindAnyObjectByType<GpuParticleIndirectRenderer>();
                InvokePrivate(gpuRenderer, "OnDisable");

                GpuFluidBufferSet gpuBuffers =
                    UnityEngine.Object.FindAnyObjectByType<GpuFluidBufferSet>();
                gpuBuffers?.ReleaseBuffersPublic();

                BoundarySystem boundary =
                    UnityEngine.Object.FindAnyObjectByType<BoundarySystem>();
                boundary?.Dispose();

                RopeSystem rope =
                    UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                rope?.Dispose();
            }

            if (success)
                Debug.Log($"ROPE_BUCKET_FLUID_LOAD_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"ROPE_BUCKET_FLUID_LOAD_VALIDATION_FAIL: {report}");

            EditorApplication.Exit(success ? 0 : 2);
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

        private static int GetIntArgument(
            string[] args,
            string name,
            int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(
                    args[i],
                    name,
                    StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int value))
                {
                    return Mathf.Max(1, value);
                }
            }

            return fallback;
        }

        private static void InvokePrivate(object target, string methodName)
        {
            if (target == null)
                return;

            target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            )?.Invoke(target, null);
        }

        private static void CaptureFullFluidScreenshot(
            string screenshotPath,
            BucketSystem bucket)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath) ?? ".");

            Camera camera = Camera.main ??
                UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject(
                    "RopeBucketFullFluidCamera");
                camera = cameraObject.AddComponent<Camera>();
            }

            BucketState state = bucket.State;
            Vector3 origin = new Vector3(
                state.position.x,
                state.position.y,
                state.position.z);
            Quaternion rotation = new Quaternion(
                state.rotation.value.x,
                state.rotation.value.y,
                state.rotation.value.z,
                state.rotation.value.w);
            Vector3 localView =
                new Vector3(0.52f, 1.18f, -0.72f).normalized;
            camera.transform.position = origin + rotation * localView * 1.2f;
            Vector3 target = origin + rotation * new Vector3(0.0f, 0.06f, 0.0f);
            camera.transform.rotation = Quaternion.LookRotation(
                target - camera.transform.position,
                rotation * Vector3.up);
            camera.orthographic = true;
            camera.orthographicSize = 0.62f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 12.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.035f, 1.0f);

            RenderTexture renderTexture = new RenderTexture(1280, 960, 24);
            Texture2D texture = new Texture2D(
                renderTexture.width,
                renderTexture.height,
                TextureFormat.RGBA32,
                false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            texture.ReadPixels(
                new Rect(0, 0, renderTexture.width, renderTexture.height),
                0,
                0);
            texture.Apply();
            File.WriteAllBytes(screenshotPath, texture.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
