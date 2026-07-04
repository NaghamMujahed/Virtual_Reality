using System;
using System.Globalization;
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
using PaintBucketSim.Systems.Rope;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Mathematics;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    public static class RopeBucketVisualValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string DefaultScreenshotPath =
            "Logs/RopeBucketVisualValidation.png";

        private static readonly Type[] RendererTypes =
        {
            typeof(RopeRenderer),
            typeof(BucketRenderer)
        };

        [MenuItem("Paint Bucket Sim/Run Rope Bucket Visual Validation")]
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
                ValidationOptions options =
                    ValidationOptions.FromCommandLine(Environment.GetCommandLineArgs());

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                SimulationManager manager =
                    UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
                RopeSystem rope =
                    UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
                BucketSystem bucket =
                    UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
                RopeBucketCouplingSystem coupling =
                    UnityEngine.Object.FindAnyObjectByType<RopeBucketCouplingSystem>();

                if (manager == null || rope == null || bucket == null ||
                    coupling == null)
                {
                    Finish(
                        batchMode,
                        false,
                        "SimulationManager, RopeSystem, BucketSystem, or " +
                        "RopeBucketCouplingSystem is missing."
                    );
                    return;
                }

                InvokeRendererLifecycle("Awake");

                if (!TryCreateRopeBucketContext(
                    manager,
                    out SimulationContext context,
                    out TimeStepController timeController,
                    out string contextError
                ))
                {
                    Finish(batchMode, false, contextError);
                    return;
                }

                bucket.Initialize(context);
                rope.Initialize(context);
                coupling.Initialize(context);

                if (!context.IsInitialized || !rope.IsInitialized ||
                    !bucket.IsInitialized || !coupling.IsReady)
                {
                    Finish(
                        batchMode,
                        false,
                        "Rope/bucket/coupling initialization failed."
                    );
                    return;
                }

                float dt = timeController.GetSubstepDeltaTime();
                int steps = Mathf.Max(1, options.StepCount);
                float maxObservedEndpointTwist = 0.0f;

                for (int step = 0; step < steps; step++)
                {
                    ApplySyntheticPaintLoad(bucket, step, dt);
                    ApplySwingAndTwist(bucket, step, dt);

                    rope.Step(context, dt);
                    bucket.Step(context, dt);
                    coupling.Step(context, dt);

                    maxObservedEndpointTwist = Mathf.Max(
                        maxObservedEndpointTwist,
                        Mathf.Abs(rope.Diagnostics.endpointTwistRadians));

                    timeController.AdvanceSubstep(dt);
                    UpdateContextDiagnostics(context, timeController, dt);
                }

                InvokeRendererLifecycle("Start");
                InvokeRendererLifecycle("LateUpdate");

                VisualStats visualStats = CaptureValidationScreenshot(
                    options.ScreenshotPath,
                    rope,
                    bucket
                );
                string detailScreenshotPath =
                    BuildDetailScreenshotPath(options.ScreenshotPath);
                VisualStats detailVisualStats =
                    CaptureBucketDetailScreenshot(
                        detailScreenshotPath,
                        bucket);

                MeshStats meshStats = ReadMeshStats();
                RopeDiagnostics ropeDiagnostics = rope.Diagnostics;
                BucketDiagnostics bucketDiagnostics = bucket.Diagnostics;
                RopeBucketCouplingDiagnostics couplingDiagnostics =
                    coupling.Diagnostics;

                bool valid =
                    ropeDiagnostics.isBroken == 0 &&
                    ropeDiagnostics.particleCount >= 3 &&
                    ropeDiagnostics.segmentCount >= 2 &&
                    IsFinite(ropeDiagnostics.currentLength) &&
                    IsFinite(ropeDiagnostics.maxStretchError) &&
                    IsFinite(ropeDiagnostics.endpointTwistRadians) &&
                    IsFinite(ropeDiagnostics.endpointTwistAngularVelocity) &&
                    IsFinite(ropeDiagnostics.twistKineticEnergy) &&
                    IsFinite(ropeDiagnostics.twistElasticEnergy) &&
                    ropeDiagnostics.twistKineticEnergy +
                    ropeDiagnostics.twistElasticEnergy <= 2.0f &&
                    Mathf.Abs(ropeDiagnostics.endpointTwistAngularVelocity) <=
                    rope.Config.maxTwistAngularSpeedRadiansPerSecond + 0.1f &&
                    IsFinite(ropeDiagnostics.maxTwistGradientRadians) &&
                    ropeDiagnostics.currentLength <=
                    ropeDiagnostics.restLength * 1.04f &&
                    ropeDiagnostics.maxStretchError <=
                    ropeDiagnostics.restLength /
                    Mathf.Max(ropeDiagnostics.segmentCount, 1) *
                    0.15f &&
                    IsFinite(bucketDiagnostics.speed) &&
                    IsFinite(bucketDiagnostics.angularSpeed) &&
                    bucketDiagnostics.containedFluidMass > 1.0f &&
                    bucketDiagnostics.totalMass > bucketDiagnostics.dryMass &&
                    couplingDiagnostics.enabled != 0 &&
                    couplingDiagnostics.active != 0 &&
                    couplingDiagnostics.twistEnabled != 0 &&
                    maxObservedEndpointTwist >
                    options.MinEndpointTwistRadians &&
                    ropeDiagnostics.maxTwistGradientRadians <=
                    MaxAllowedTwistGradient(rope) &&
                    couplingDiagnostics.attachmentError <=
                    options.MaxAttachmentErrorMeters &&
                    meshStats.RopeVertexCount > 0 &&
                    meshStats.BucketVertexCount > 0 &&
                    meshStats.RenderedHoleCount > 0 &&
                    visualStats.NonBackgroundRatio >=
                    options.MinNonBackgroundRatio;

                string report =
                    $"steps={steps}, " +
                    $"screenshot={Path.GetFullPath(options.ScreenshotPath)}, " +
                    $"detailScreenshot={Path.GetFullPath(detailScreenshotPath)}, " +
                    $"ropeVertices={meshStats.RopeVertexCount}, " +
                    $"bucketVertices={meshStats.BucketVertexCount}, " +
                    $"renderedHoles={meshStats.RenderedHoleCount}, " +
                    $"currentLength={ropeDiagnostics.currentLength:F3}m, " +
                    $"maxStretch={ropeDiagnostics.maxStretchError:E3}m, " +
                    $"maxBend={ropeDiagnostics.maxBendAngleRadians * Mathf.Rad2Deg:F2}deg, " +
                    $"endpointTwist={ropeDiagnostics.endpointTwistRadians * Mathf.Rad2Deg:F2}deg, " +
                    $"maxObservedTwist={maxObservedEndpointTwist * Mathf.Rad2Deg:F2}deg, " +
                    $"endpointTwistSpeed={ropeDiagnostics.endpointTwistAngularVelocity:F2}rad/s, " +
                    $"maxTwistGradient={ropeDiagnostics.maxTwistGradientRadians * Mathf.Rad2Deg:F2}deg, " +
                    $"twistEnergy={(ropeDiagnostics.twistKineticEnergy + ropeDiagnostics.twistElasticEnergy):F4}J, " +
                    $"attachmentError={couplingDiagnostics.attachmentError:E3}m, " +
                    $"attachmentImpulse={couplingDiagnostics.attachmentImpulse:F4}Ns, " +
                    $"twistError={couplingDiagnostics.twistErrorRadians * Mathf.Rad2Deg:F2}deg, " +
                    $"twistTorque={couplingDiagnostics.appliedTwistTorque:F3}Nm, " +
                    $"bucketSpeed={bucketDiagnostics.speed:F3}m/s, " +
                    $"bucketAngularSpeed={bucketDiagnostics.angularSpeed:F3}rad/s, " +
                    $"bucketMass={bucketDiagnostics.totalMass:F2}kg, " +
                    $"paintMass={bucketDiagnostics.containedFluidMass:F2}kg, " +
                    $"paintComOffset={new Vector3(bucketDiagnostics.centerOfMassLocal.x, 0.0f, bucketDiagnostics.centerOfMassLocal.z).magnitude:F3}m, " +
                    $"bailAngle={bucketDiagnostics.bailHingeAngleRadians * Mathf.Rad2Deg:F1}deg, " +
                    $"nonBackground={(visualStats.NonBackgroundRatio * 100.0f):F2}%, " +
                    $"detailNonBackground={(detailVisualStats.NonBackgroundRatio * 100.0f):F2}%, " +
                    $"lumaVariance={visualStats.LuminanceVariance:F5}";

                Finish(batchMode, valid, report);
            }
            catch (Exception exception)
            {
                Finish(batchMode, false, exception.ToString());
            }
        }

        private static void ApplySwingAndTwist(
            BucketSystem bucket,
            int step,
            float dt
        )
        {
            if (bucket == null || !bucket.IsInitialized)
                return;

            float time = step * Mathf.Max(dt, 1e-6f);
            float phase = 2.0f * Mathf.PI * time;

            Vector3 force = new Vector3(
                7.5f * Mathf.Sin(phase * 1.35f),
                0.0f,
                5.0f * Mathf.Sin(phase * 0.91f + 0.35f)
            );

            Vector3 torque = new Vector3(
                0.22f * Mathf.Sin(phase * 0.74f),
                1.05f * Mathf.Sin(phase * 1.12f + 0.25f),
                0.35f * Mathf.Cos(phase * 0.66f)
            );

            bucket.AddForce(force);
            bucket.AddTorque(torque);
        }

        private static void ApplySyntheticPaintLoad(
            BucketSystem bucket,
            int step,
            float dt)
        {
            if (bucket == null || !bucket.IsInitialized)
                return;

            float time = step * Mathf.Max(dt, 1e-6f);
            float3 fluidCenter = new float3(
                0.055f * Mathf.Sin(time * 2.1f),
                -0.12f,
                0.04f * Mathf.Cos(time * 1.7f));

            bucket.SetContainedFluidLoad(
                10.0f,
                fluidCenter,
                0.26f,
                dt);
        }

        private static bool TryCreateRopeBucketContext(
            SimulationManager manager,
            out SimulationContext context,
            out TimeStepController timeController,
            out string error)
        {
            context = null;
            timeController = null;
            error = string.Empty;

            if (manager == null || manager.Config == null)
            {
                error = "SimulationManager or SimulationConfig is missing.";
                return false;
            }

            EnvironmentConfig environmentConfig = ResolveEnvironmentConfig(manager);
            if (environmentConfig == null)
            {
                error = "EnvironmentConfig could not be resolved.";
                return false;
            }

            context = new SimulationContext();
            context.Initialize(manager.Config, environmentConfig);

            var environmentSystem = new EnvironmentSystem();
            environmentSystem.Initialize(context);

            timeController = new TimeStepController();
            timeController.Initialize(manager.Config);

            UpdateContextDiagnostics(
                context,
                timeController,
                timeController.GetSubstepDeltaTime()
            );

            return true;
        }

        private static EnvironmentConfig ResolveEnvironmentConfig(
            SimulationManager manager)
        {
            FieldInfo field = typeof(SimulationManager).GetField(
                "environmentConfig",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            return field?.GetValue(manager) as EnvironmentConfig;
        }

        private static void UpdateContextDiagnostics(
            SimulationContext context,
            TimeStepController timeController,
            float dt
        )
        {
            DiagnosticsFrame diagnostics = context.Diagnostics;

            diagnostics.initialized = true;
            diagnostics.paused = timeController.IsPaused;
            diagnostics.simulationTime = timeController.SimulationTime;
            diagnostics.substepIndex = timeController.SubstepIndex;
            diagnostics.substepDeltaTime = dt;
            diagnostics.fixedDeltaTime = timeController.GetFixedDeltaTime();
            diagnostics.substeps = timeController.GetSubstepCount();
            diagnostics.gravity = context.EnvironmentState.gravity;
            diagnostics.windVelocity = context.EnvironmentState.windVelocity;

            context.Diagnostics = diagnostics;
        }

        private static void InvokeRendererLifecycle(string methodName)
        {
            foreach (Type type in RendererTypes)
            {
                UnityEngine.Object[] renderers =
                    UnityEngine.Object.FindObjectsByType(
                        type,
                        FindObjectsSortMode.None
                    );

                foreach (UnityEngine.Object renderer in renderers)
                {
                    type.GetMethod(
                        methodName,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic
                    )?.Invoke(renderer, null);
                }
            }
        }

        private static MeshStats ReadMeshStats()
        {
            int ropeVertices = 0;
            int bucketVertices = 0;
            int renderedHoleCount = 0;

            RopeRenderer ropeRenderer =
                UnityEngine.Object.FindAnyObjectByType<RopeRenderer>();
            if (ropeRenderer != null &&
                ropeRenderer.TryGetComponent(out MeshFilter ropeMeshFilter) &&
                ropeMeshFilter.sharedMesh != null)
            {
                ropeVertices = ropeMeshFilter.sharedMesh.vertexCount;
            }

            BucketRenderer bucketRenderer =
                UnityEngine.Object.FindAnyObjectByType<BucketRenderer>();
            if (bucketRenderer != null &&
                bucketRenderer.TryGetComponent(out MeshFilter bucketMeshFilter) &&
                bucketMeshFilter.sharedMesh != null)
            {
                bucketVertices = bucketMeshFilter.sharedMesh.vertexCount;
                renderedHoleCount = bucketRenderer.RenderedHoleCount;
            }

            return new MeshStats(
                ropeVertices,
                bucketVertices,
                renderedHoleCount);
        }

        private static string BuildDetailScreenshotPath(string screenshotPath)
        {
            string directory = Path.GetDirectoryName(screenshotPath) ?? ".";
            string filename = Path.GetFileNameWithoutExtension(screenshotPath);
            string extension = Path.GetExtension(screenshotPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            return Path.Combine(directory, $"{filename}_detail{extension}");
        }

        private static VisualStats CaptureBucketDetailScreenshot(
            string screenshotPath,
            BucketSystem bucket)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath) ?? ".");

            Camera camera = Camera.main ??
                UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject(
                    "BucketDetailValidationCamera");
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
            Vector3 target =
                origin + rotation * new Vector3(0.0f, -0.22f, 0.0f);
            Vector3 localViewDirection =
                new Vector3(0.48f, -0.78f, -0.4f).normalized;
            camera.transform.position =
                origin + rotation * localViewDirection * 0.92f;
            camera.transform.rotation = Quaternion.LookRotation(
                target - camera.transform.position,
                rotation * Vector3.forward);
            camera.orthographic = true;
            camera.orthographicSize = 0.42f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 8.0f;

            Color background = new Color(0.035f, 0.04f, 0.045f, 1.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;

            RenderTexture renderTexture = new RenderTexture(960, 720, 24);
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
            VisualStats stats = AnalyzeTexture(texture, background);

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(texture);
            return stats;
        }

        private static VisualStats CaptureValidationScreenshot(
            string screenshotPath,
            RopeSystem rope,
            BucketSystem bucket
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath) ?? ".");

            Camera camera = Camera.main ??
                UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject(
                    "RopeBucketValidationCamera"
                );
                camera = cameraObject.AddComponent<Camera>();
            }

            Color background = new Color(0.035f, 0.04f, 0.045f, 1.0f);
            FrameCamera(camera, rope, bucket, background);

            RenderTexture renderTexture = new RenderTexture(1280, 720, 24);
            Texture2D texture = new Texture2D(
                renderTexture.width,
                renderTexture.height,
                TextureFormat.RGBA32,
                false
            );

            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            texture.ReadPixels(
                new Rect(0, 0, renderTexture.width, renderTexture.height),
                0,
                0
            );
            texture.Apply();

            File.WriteAllBytes(screenshotPath, texture.EncodeToPNG());

            VisualStats stats = AnalyzeTexture(texture, background);

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(texture);

            return stats;
        }

        private static void FrameCamera(
            Camera camera,
            RopeSystem rope,
            BucketSystem bucket,
            Color background
        )
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            bool initialized = false;

            if (rope != null && rope.IsInitialized)
            {
                int firstFocusedParticle = Mathf.Max(
                    0,
                    rope.ParticleCount - Mathf.Max(10, rope.ParticleCount / 3)
                );

                for (int i = firstFocusedParticle; i < rope.ParticleCount; i++)
                    Encapsulate(ref bounds, ref initialized, rope.GetParticlePosition(i));
            }

            if (bucket != null && bucket.IsInitialized)
            {
                Vector3 center = bucket.GetCenterOfMassWorld();
                Encapsulate(ref bounds, ref initialized, center + Vector3.one * 0.35f);
                Encapsulate(ref bounds, ref initialized, center - Vector3.one * 0.35f);
            }

            if (!initialized)
                bounds = new Bounds(Vector3.zero, Vector3.one * 4.0f);

            Vector3 direction = new Vector3(0.58f, 0.22f, -0.78f).normalized;
            float extent = Mathf.Max(bounds.extents.magnitude, 1.0f);

            camera.transform.position = bounds.center - direction * extent * 3.5f;
            camera.transform.rotation =
                Quaternion.LookRotation(bounds.center - camera.transform.position);
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = Mathf.Max(25.0f, extent * 8.0f);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(
                0.75f,
                Mathf.Max(bounds.extents.y * 1.22f, bounds.extents.x * 0.78f)
            );
            camera.fieldOfView = 34.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
        }

        private static void Encapsulate(
            ref Bounds bounds,
            ref bool initialized,
            Vector3 point
        )
        {
            if (!initialized)
            {
                bounds = new Bounds(point, Vector3.zero);
                initialized = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private static VisualStats AnalyzeTexture(Texture2D texture, Color background)
        {
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
                return default;

            float bgLuma = Luminance(background);
            double sum = 0.0;
            double sumSq = 0.0;
            int nonBackground = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color color = pixels[i];
                float luma = Luminance(color);
                sum += luma;
                sumSq += luma * luma;

                if (Mathf.Abs(luma - bgLuma) > 0.025f)
                    nonBackground++;
            }

            double mean = sum / pixels.Length;
            double variance = Math.Max(0.0, sumSq / pixels.Length - mean * mean);

            return new VisualStats(
                nonBackground / (float)pixels.Length,
                (float)variance
            );
        }

        private static float MaxAllowedTwistGradient(RopeSystem rope)
        {
            if (rope == null || rope.Config == null)
                return 0.55f;

            return Mathf.Max(rope.Config.maxTwistGradientRadians, 0.001f) + 0.035f;
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Finish(bool batchMode, bool success, string report)
        {
            if (success)
                Debug.Log($"ROPE_BUCKET_VISUAL_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"ROPE_BUCKET_VISUAL_VALIDATION_FAIL: {report}");

            DisposeRuntimeSystems();

            if (batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }

        private static void DisposeRuntimeSystems()
        {
            PaintFluidSystem fluid =
                UnityEngine.Object.FindAnyObjectByType<PaintFluidSystem>();
            if (fluid != null)
                fluid.Dispose();

            BoundarySystem boundary =
                UnityEngine.Object.FindAnyObjectByType<BoundarySystem>();
            if (boundary != null)
                boundary.Dispose();

            RopeSystem rope =
                UnityEngine.Object.FindAnyObjectByType<RopeSystem>();
            if (rope != null)
                rope.Dispose();
        }

        private readonly struct MeshStats
        {
            public readonly int RopeVertexCount;
            public readonly int BucketVertexCount;
            public readonly int RenderedHoleCount;

            public MeshStats(
                int ropeVertexCount,
                int bucketVertexCount,
                int renderedHoleCount)
            {
                RopeVertexCount = ropeVertexCount;
                BucketVertexCount = bucketVertexCount;
                RenderedHoleCount = renderedHoleCount;
            }
        }

        private readonly struct VisualStats
        {
            public readonly float NonBackgroundRatio;
            public readonly float LuminanceVariance;

            public VisualStats(float nonBackgroundRatio, float luminanceVariance)
            {
                NonBackgroundRatio = nonBackgroundRatio;
                LuminanceVariance = luminanceVariance;
            }
        }

        private readonly struct ValidationOptions
        {
            public readonly int StepCount;
            public readonly string ScreenshotPath;
            public readonly float MinEndpointTwistRadians;
            public readonly float MaxAttachmentErrorMeters;
            public readonly float MinNonBackgroundRatio;

            private ValidationOptions(
                int stepCount,
                string screenshotPath,
                float minEndpointTwistRadians,
                float maxAttachmentErrorMeters,
                float minNonBackgroundRatio
            )
            {
                StepCount = stepCount;
                ScreenshotPath = screenshotPath;
                MinEndpointTwistRadians = minEndpointTwistRadians;
                MaxAttachmentErrorMeters = maxAttachmentErrorMeters;
                MinNonBackgroundRatio = minNonBackgroundRatio;
            }

            public static ValidationOptions FromCommandLine(string[] args)
            {
                return new ValidationOptions(
                    GetIntArgument(args, "-ropeBucketValidationSteps", 160),
                    GetStringArgument(
                        args,
                        "-ropeBucketValidationScreenshot",
                        DefaultScreenshotPath
                    ),
                    GetFloatArgument(args, "-ropeBucketValidationMinTwist", 0.025f),
                    GetFloatArgument(
                        args,
                        "-ropeBucketValidationMaxAttachmentError",
                        0.12f
                    ),
                    GetFloatArgument(
                        args,
                        "-ropeBucketValidationMinVisiblePixels",
                        0.015f
                    )
                );
            }

            private static int GetIntArgument(
                string[] args,
                string name,
                int fallback
            )
            {
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (!string.Equals(
                        args[i],
                        name,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        continue;
                    }

                    if (int.TryParse(
                        args[i + 1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value
                    ))
                    {
                        return Mathf.Max(1, value);
                    }
                }

                return fallback;
            }

            private static float GetFloatArgument(
                string[] args,
                string name,
                float fallback
            )
            {
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (!string.Equals(
                        args[i],
                        name,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        continue;
                    }

                    if (float.TryParse(
                        args[i + 1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float value
                    ))
                    {
                        return value;
                    }
                }

                return fallback;
            }

            private static string GetStringArgument(
                string[] args,
                string name,
                string fallback
            )
            {
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (string.Equals(
                        args[i],
                        name,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        return args[i + 1];
                    }
                }

                return fallback;
            }
        }
    }
}
