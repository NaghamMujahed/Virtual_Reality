using System.Reflection;
using System;
using System.Globalization;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    public static class SimulationValidationRunner
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int DefaultTargetSubsteps = 30;
        private const double ValidationTimeoutSeconds = 180.0;

        private static readonly MethodInfo StepSubstepMethod =
            typeof(SimulationManager).GetMethod(
                "StepSimulationSubstep",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        private static bool _batchMode;
        private static double _startTime;
        private static int _executedSubsteps;
        private static int _readbackWaitUpdates;
        private static int _targetSubsteps;
        private static int _diagnosticsInterval;
        private static int _lastRecordedDiagnosticsStep;
        private static int _maxCollisionCorrectionsObserved;
        private static int _totalJClampsObserved;
        private static int _maxNoGridSupportObserved;
        private static int _maxNanInfObserved;
        private static int _maxOutflowTransitionsObserved;
        private static int _maxJetParticlesObserved;
        private static int _maxAirborneParticlesObserved;
        private static bool _projectionTest;
        private static bool _shakeBucket;
        private static bool _sealBucket;
        private static bool _outflowTest;
        private static SimulationManager _manager;
        private static PaintFluidSystem _fluid;
        private static BucketSystem _bucket;
        private static GpuFluidBufferSet _gpuBuffers;

        public static void RunBatch()
        {
            _batchMode = true;
            StartValidation();
        }

        [MenuItem("Paint Bucket Sim/Run Simulation Validation")]
        public static void RunInteractive()
        {
            _batchMode = false;
            StartValidation();
        }

        private static void StartValidation()
        {
            if (StepSubstepMethod == null)
            {
                Finish(false, "Could not resolve SimulationManager.StepSimulationSubstep.");
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            _manager = UnityEngine.Object.FindAnyObjectByType<SimulationManager>();
            _fluid = UnityEngine.Object.FindAnyObjectByType<PaintFluidSystem>();
            _bucket = UnityEngine.Object.FindAnyObjectByType<BucketSystem>();
            _gpuBuffers =
                UnityEngine.Object.FindAnyObjectByType<GpuFluidBufferSet>();

            if (_manager == null ||
                _fluid == null ||
                _bucket == null ||
                _gpuBuffers == null)
            {
                Finish(
                    false,
                    "SimulationManager, PaintFluidSystem, BucketSystem, " +
                    "or GpuFluidBufferSet is missing."
                );
                return;
            }

            ConfigureValidationMode();
            _manager.Initialize();

            if (!_manager.IsInitialized || !_fluid.IsInitialized)
            {
                Finish(false, "Simulation initialization failed.");
                return;
            }

            _startTime = EditorApplication.timeSinceStartup;
            _executedSubsteps = 0;
            _readbackWaitUpdates = 0;
            _lastRecordedDiagnosticsStep = 0;
            _maxCollisionCorrectionsObserved = 0;
            _totalJClampsObserved = 0;
            _maxNoGridSupportObserved = 0;
            _maxNanInfObserved = 0;
            _maxOutflowTransitionsObserved = 0;
            _maxJetParticlesObserved = 0;
            _maxAirborneParticlesObserved = 0;

            EditorApplication.update -= UpdateValidation;
            EditorApplication.update += UpdateValidation;
        }

        private static void UpdateValidation()
        {
            RecordLatestDiagnostics();

            if (EditorApplication.timeSinceStartup - _startTime >
                ValidationTimeoutSeconds)
            {
                Finish(
                    false,
                    $"Timed out after {ValidationTimeoutSeconds:F0} seconds; " +
                    $"completedSubsteps={_executedSubsteps}."
                );
                return;
            }

            if (_executedSubsteps < _targetSubsteps)
            {
                FluidSolverStats checkpointStats = _fluid.SolverStats;
                if (ShouldWaitForDiagnosticsAtSubstep(_executedSubsteps) &&
                    checkpointStats.gpuDiagnosticsStepIndex <
                    _executedSubsteps)
                {
                    return;
                }

                float dt = _manager.TimeController.GetSubstepDeltaTime();

                try
                {
                    ApplyValidationBucketMotion(dt);
                    StepSubstepMethod.Invoke(_manager, new object[] { dt });
                }
                catch (TargetInvocationException exception)
                {
                    Finish(false, exception.InnerException?.ToString() ?? exception.ToString());
                    return;
                }

                _manager.TimeController.AdvanceSubstep(dt);
                _executedSubsteps++;

                FluidSolverStats current = _fluid.SolverStats;
                if (current.status == FluidSolverStatus.Error)
                {
                    Finish(
                        false,
                        $"Solver entered Error state; " +
                        $"gridContainsBucket={current.gpuGridContainsBucket}, " +
                        $"requiredExtent={current.gpuRequiredGridExtent:F4}."
                    );
                }

                return;
            }

            FluidSolverStats stats = _fluid.SolverStats;
            int acceptableDiagnosticsStep =
                GetExpectedDiagnosticsStepAtOrBefore(_targetSubsteps);

            if ((!stats.gpuDiagnosticsReady ||
                 stats.gpuDiagnosticsStepIndex < acceptableDiagnosticsStep) &&
                _readbackWaitUpdates < 300)
            {
                _readbackWaitUpdates++;
                return;
            }

            bool valid =
                stats.status == FluidSolverStatus.Running &&
                stats.gpuGridContainsBucket &&
                stats.gpuDiagnosticsReady &&
                stats.gpuDiagnosticsStepIndex >= acceptableDiagnosticsStep &&
                _maxNoGridSupportObserved == 0 &&
                _maxNanInfObserved == 0;

            if (_shakeBucket)
                valid &= _maxCollisionCorrectionsObserved > 0;

            if (_outflowTest)
            {
                valid &=
                    _maxOutflowTransitionsObserved > 0 ||
                    _maxJetParticlesObserved > 0 ||
                    _maxAirborneParticlesObserved > 0;
            }

            if (stats.pressureSolveEnabled &&
                stats.projectionFluidCellCount > 0)
            {
                valid &=
                    stats.projectionAverageAbsDivergenceAfter <=
                    stats.projectionAverageAbsDivergenceBefore * 1.05f + 1e-4f;
            }

            string report =
                $"mode={(_projectionTest ? "staggered-projection" : "baseline")}" +
                $"{(_shakeBucket ? "+bucket-shake" : string.Empty)}, " +
                $"sealedBucket={_sealBucket}, " +
                $"outflowTest={_outflowTest}, " +
                $"status={stats.status}, " +
                $"substeps={_executedSubsteps}, " +
                $"diagnosticsStep={stats.gpuDiagnosticsStepIndex}, " +
                $"particles={stats.particleCount}, " +
                $"gridContainsBucket={stats.gpuGridContainsBucket}, " +
                $"active={stats.gpuActiveParticleCount}, " +
                $"partialGridSupport={stats.gpuOutOfGridParticleCount}, " +
                $"noGridSupport={stats.gpuNoGridSupportParticleCount}, " +
                $"collisions={stats.gpuCollisionCorrectionCount}, " +
                $"maxObservedCollisions={_maxCollisionCorrectionsObserved}, " +
                $"jClamps={stats.gpuDeformationJClampCount}, " +
                $"totalObservedJClamps={_totalJClampsObserved}, " +
                $"jAvg={stats.gpuAverageDeformationJ:F4}, " +
                $"jMin={stats.gpuMinimumDeformationJ:F4}, " +
                $"jMax={stats.gpuMaximumDeformationJ:F4}, " +
                $"fillAvg01={stats.gpuAverageFillHeight01:F4}, " +
                $"fillMin01={stats.gpuMinimumFillHeight01:F4}, " +
                $"fillMax01={stats.gpuMaximumFillHeight01:F4}, " +
                $"fillSpan01={stats.gpuEstimatedFillSpan01:F4}, " +
                $"speedAvg={stats.gpuAverageParticleSpeed:F4}, " +
                $"speedMax={stats.gpuMaximumParticleSpeed:F4}, " +
                $"nanInf={stats.gpuNanInfCount}, " +
                $"holes={stats.gpuConfiguredHoleCount}, " +
                $"holeOpen={stats.gpuHoleOpeningEnabled}, " +
                $"outflow={stats.gpuOutflowTransitionCount}, " +
                $"maxObservedOutflow={_maxOutflowTransitionsObserved}, " +
                $"jet={stats.gpuJetParticleCount}, " +
                $"maxObservedJet={_maxJetParticlesObserved}, " +
                $"airborne={stats.gpuAirborneParticleCount}, " +
                $"maxObservedAirborne={_maxAirborneParticlesObserved}, " +
                $"lost={stats.gpuLostParticleCount}, " +
                $"fluidCells={stats.projectionFluidCellCount}, " +
                $"divBeforeAvg={stats.projectionAverageAbsDivergenceBefore:F5}, " +
                $"divAfterAvg={stats.projectionAverageAbsDivergenceAfter:F5}, " +
                $"pressureMax={stats.projectionMeasuredMaxAbsPressure:F3}";

            double elapsedSeconds =
                EditorApplication.timeSinceStartup - _startTime;
            report +=
                $", elapsedSeconds={elapsedSeconds:F3}" +
                $", msPerSubstep={elapsedSeconds * 1000.0 / Mathf.Max(_executedSubsteps, 1):F3}";

            Finish(valid, report);
        }

        private static void RecordLatestDiagnostics()
        {
            if (_fluid == null)
                return;

            FluidSolverStats stats = _fluid.SolverStats;
            if (!stats.gpuDiagnosticsReady ||
                stats.gpuDiagnosticsStepIndex <=
                _lastRecordedDiagnosticsStep)
            {
                return;
            }

            _lastRecordedDiagnosticsStep =
                stats.gpuDiagnosticsStepIndex;
            _maxCollisionCorrectionsObserved = Mathf.Max(
                _maxCollisionCorrectionsObserved,
                stats.gpuCollisionCorrectionCount
            );
            _totalJClampsObserved +=
                stats.gpuDeformationJClampCount;
            _maxNoGridSupportObserved = Mathf.Max(
                _maxNoGridSupportObserved,
                stats.gpuNoGridSupportParticleCount
            );
            _maxNanInfObserved = Mathf.Max(
                _maxNanInfObserved,
                stats.gpuNanInfCount
            );
            _maxOutflowTransitionsObserved = Mathf.Max(
                _maxOutflowTransitionsObserved,
                stats.gpuOutflowTransitionCount
            );
            _maxJetParticlesObserved = Mathf.Max(
                _maxJetParticlesObserved,
                stats.gpuJetParticleCount
            );
            _maxAirborneParticlesObserved = Mathf.Max(
                _maxAirborneParticlesObserved,
                stats.gpuAirborneParticleCount
            );
        }

        private static bool ShouldWaitForDiagnosticsAtSubstep(int completedSubsteps)
        {
            if (completedSubsteps <= 0)
                return false;

            return completedSubsteps ==
                   GetExpectedDiagnosticsStepAtOrBefore(completedSubsteps);
        }

        private static int GetExpectedDiagnosticsStepAtOrBefore(int completedSubsteps)
        {
            if (completedSubsteps <= 0 ||
                _diagnosticsInterval <= 0)
                return 0;

            if (_fluid == null ||
                _fluid.GpuMpmConfig == null ||
                !_fluid.GpuMpmConfig.enableProjectionGridInfrastructure)
            {
                return completedSubsteps -
                       completedSubsteps % _diagnosticsInterval;
            }

            int projectionInterval = Mathf.Max(
                1,
                _fluid.GpuMpmConfig.projectionSubstepInterval
            );

            int lastExpectedDiagnosticsStep = 0;

            for (int step = 1; step <= completedSubsteps; step++)
            {
                bool projectionRan =
                    step == 1 ||
                    step % projectionInterval == 0;

                if (!projectionRan)
                    continue;

                if (step - lastExpectedDiagnosticsStep <
                    _diagnosticsInterval)
                {
                    continue;
                }

                lastExpectedDiagnosticsStep = step;
            }

            return lastExpectedDiagnosticsStep;
        }

        private static void ConfigureValidationMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            _projectionTest = HasFlag(args, "-paintValidationProjection");
            _shakeBucket = HasFlag(args, "-paintValidationShakeBucket");
            _sealBucket = HasFlag(args, "-paintValidationSealBucket");
            _outflowTest = HasFlag(args, "-paintValidationOutflow");
            _targetSubsteps = GetIntArgument(
                args,
                "-paintValidationSteps",
                DefaultTargetSubsteps
            );
            _diagnosticsInterval = GetIntArgument(
                args,
                "-paintValidationDiagnosticsInterval",
                Mathf.Min(15, Mathf.Max(1, _targetSubsteps))
            );

            int targetParticles = GetIntArgument(
                args,
                "-paintValidationTargetParticles",
                _fluid.FluidConfig != null
                    ? _fluid.FluidConfig.targetParticleCount
                    : 5000
            );

            if (_fluid.FluidConfig != null)
            {
                _fluid.FluidConfig.targetParticleCount = targetParticles;
                _fluid.FluidConfig.maxParticleCapacity = Mathf.Max(
                    _fluid.FluidConfig.maxParticleCapacity,
                    targetParticles
                );
            }

            if (_gpuBuffers.Config != null)
            {
                _gpuBuffers.Config.maxGpuParticles = Mathf.Max(
                    _gpuBuffers.Config.maxGpuParticles,
                    targetParticles
                );
            }

            if (_fluid.GpuMpmConfig != null)
            {
                _fluid.GpuMpmConfig.enableGpuDiagnostics = true;
                _fluid.GpuMpmConfig.diagnosticsReadbackInterval =
                    _diagnosticsInterval;
            }

            if (_manager.Config != null)
            {
                _manager.Config.substeps = GetIntArgument(
                    args,
                    "-paintValidationSimulationSubsteps",
                    _manager.Config.substeps
                );
            }

            if (_fluid.GpuMpmConfig != null)
            {
                var baseConfig = _fluid.GpuMpmConfig;
                int gridResolution = GetIntArgument(
                    args,
                    "-paintValidationGridResolution",
                    baseConfig.gridResolution.x
                );
                baseConfig.gridResolution = new Vector3Int(
                    gridResolution,
                    gridResolution,
                    gridResolution
                );
                baseConfig.cellSizeMeters = GetFloatArgument(
                    args,
                    "-paintValidationCellSize",
                    baseConfig.cellSizeMeters
                );
                baseConfig.projectionSubstepInterval = GetIntArgument(
                    args,
                    "-paintValidationProjectionInterval",
                    baseConfig.projectionSubstepInterval
                );
                baseConfig.enablePressureWarmStart =
                    !HasFlag(args, "-paintValidationDisableWarmStart");
                baseConfig.pressureWarmStartFactor = GetFloatArgument(
                    args,
                    "-paintValidationWarmStartFactor",
                    baseConfig.pressureWarmStartFactor
                );
            }

            if (!_projectionTest || _fluid.GpuMpmConfig == null)
            {
                if (_outflowTest && _fluid.GpuMpmConfig != null)
                    ConfigureOutflowValidation(args);

                return;
            }

            var config = _fluid.GpuMpmConfig;
            if (_sealBucket)
            {
                config.topBoundaryMode = GpuBucketTopMode.TemporaryLid;
                config.projectionTopOpen = false;
                config.enableBottomHoleOpening = false;
            }

            config.enableProjectionGridInfrastructure = true;
            config.enableProjectionDivergenceComputation = true;
            config.enableJacobiPressureSolve = true;
            config.enablePressureGradientSubtraction = true;
            config.useStaggeredFaceProjection = true;
            config.pressureJacobiIterations = GetIntArgument(
                args,
                "-paintValidationJacobiIterations",
                16
            );
            config.pressureRhsScale = GetFloatArgument(
                args,
                "-paintValidationPressureRhs",
                1.0f
            );
            config.pressureJacobiRelaxation = GetFloatArgument(
                args,
                "-paintValidationPressureRelaxation",
                0.8f
            );
            config.pressureGradientScale = GetFloatArgument(
                args,
                "-paintValidationPressureGradient",
                1.0f
            );
            config.maxPressureVelocityCorrection = GetFloatArgument(
                args,
                "-paintValidationMaxPressureCorrection",
                2.0f
            );
            config.bulkModulus = GetFloatArgument(
                args,
                "-paintValidationBulkModulus",
                config.bulkModulus
            );
            config.materialStressStrength = GetFloatArgument(
                args,
                "-paintValidationStressStrength",
                config.materialStressStrength
            );
            config.minJ = GetFloatArgument(
                args,
                "-paintValidationMinJ",
                config.minJ
            );
            config.maxJ = GetFloatArgument(
                args,
                "-paintValidationMaxJ",
                config.maxJ
            );

            if (_outflowTest)
                ConfigureOutflowValidation(args);
        }

        private static void ConfigureOutflowValidation(string[] args)
        {
            if (_fluid == null || _fluid.GpuMpmConfig == null)
                return;

            GpuMpmSolverConfig config = _fluid.GpuMpmConfig;

            config.classifyBottomHoleRegion = true;
            config.enableBottomHoleOpening = true;
            config.enableAirborneParticleAdvection = true;
            config.topBoundaryMode = GpuBucketTopMode.TemporaryLid;
            config.projectionTopOpen = false;
            config.holeOutflowExitDistanceMeters = GetFloatArgument(
                args,
                "-paintValidationHoleExitDistance",
                config.holeOutflowExitDistanceMeters
            );
            config.jetStateDurationSeconds = GetFloatArgument(
                args,
                "-paintValidationJetDuration",
                config.jetStateDurationSeconds
            );
            config.airborneLifetimeSeconds = GetFloatArgument(
                args,
                "-paintValidationAirborneLifetime",
                config.airborneLifetimeSeconds
            );
        }

        private static void ApplyValidationBucketMotion(float dt)
        {
            if (!_shakeBucket || _bucket == null || !_bucket.IsInitialized)
                return;

            float time =
                _executedSubsteps *
                Mathf.Max(dt, 1e-6f);

            float phase = 2.0f * Mathf.PI * 3.0f * time;
            Vector3 force = new Vector3(
                8.0f * Mathf.Sin(phase),
                0.0f,
                4.0f * Mathf.Sin(phase * 0.73f)
            );
            Vector3 torque = new Vector3(
                0.25f * Mathf.Sin(phase * 0.61f),
                0.10f * Mathf.Sin(phase * 0.43f),
                0.45f * Mathf.Sin(phase * 0.91f)
            );

            _bucket.AddForce(force);
            _bucket.AddTorque(torque);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
            {
                if (string.Equals(
                    arg,
                    flag,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetIntArgument(
            string[] args,
            string name,
            int fallback)
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
            float fallback)
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

        private static void Finish(bool success, string report)
        {
            EditorApplication.update -= UpdateValidation;

            if (success)
                Debug.Log($"PAINT_BUCKET_VALIDATION_PASS: {report}");
            else
                Debug.LogError($"PAINT_BUCKET_VALIDATION_FAIL: {report}");

            _manager = null;
            _fluid = null;
            _bucket = null;
            _gpuBuffers = null;

            if (_batchMode)
                EditorApplication.Exit(success ? 0 : 2);
        }
    }
}
