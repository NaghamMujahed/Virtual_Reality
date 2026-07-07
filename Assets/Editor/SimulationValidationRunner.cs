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
        private static int _maxJetMpmCollarParticlesObserved;
        private static int _maxJetColumnParticlesObserved;
        private static int _maxAirborneParticlesObserved;
        private static bool _projectionTest;
        private static bool _shakeBucket;
        private static bool _sealBucket;
        private static bool _outflowTest;
        private static bool _restingTest;
        private static float _restingMaxAverageSpeed;
        private static float _restingMaxMaximumSpeed;
        private static float _restingMaxAverageJError;
        private static bool _waitAdaptiveActivity;
        private static bool _disableCheckpointWaits;
        private static bool _forceDiagnosticCheckpointWaits;
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
            _maxJetMpmCollarParticlesObserved = 0;
            _maxJetColumnParticlesObserved = 0;
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
                int expectedActivityStep = _waitAdaptiveActivity
                    ? GetExpectedAdaptiveActivityStepAtOrBefore(
                        _executedSubsteps
                    )
                    : 0;
                if (_waitAdaptiveActivity &&
                    expectedActivityStep > 0 &&
                    checkpointStats.gpuAdaptiveActivityStepIndex <
                        expectedActivityStep)
                {
                    return;
                }

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

            if (_sealBucket && !_outflowTest)
            {
                valid &=
                    stats.gpuActiveParticleCount >=
                    Mathf.FloorToInt(stats.particleCount * 0.95f);
            }

            if (_restingTest)
            {
                valid &=
                    stats.gpuAverageParticleSpeed <=
                        _restingMaxAverageSpeed &&
                    stats.gpuMaximumParticleSpeed <=
                        _restingMaxMaximumSpeed &&
                    Mathf.Abs(stats.gpuAverageDeformationJ - 1.0f) <=
                        _restingMaxAverageJError;
            }

            if (_outflowTest)
            {
                valid &=
                    _maxOutflowTransitionsObserved > 0 ||
                    _maxJetParticlesObserved > 0 ||
                    _maxAirborneParticlesObserved > 0;

                if (_fluid != null &&
                    _fluid.GpuMpmConfig != null &&
                    _fluid.GpuMpmConfig.enableJetMpmCollar)
                {
                    valid &=
                        _maxJetMpmCollarParticlesObserved > 0;
                }

                if (_fluid != null &&
                    _fluid.GpuMpmConfig != null &&
                    _fluid.GpuMpmConfig.enableJetColumnCoherence)
                {
                    valid &=
                        _maxJetColumnParticlesObserved > 0;
                }
            }

            if (stats.pressureSolveEnabled &&
                stats.projectionFluidCellCount > 0)
            {
                valid &=
                    stats.projectionAverageAbsDivergenceAfter <=
                    stats.projectionAverageAbsDivergenceBefore * 1.05f + 1e-4f;
            }

            GpuMpmSolverConfig gpuConfig = _fluid.GpuMpmConfig;

            string report =
                $"mode={(_projectionTest ? "staggered-projection" : "baseline")}" +
                $"{(_shakeBucket ? "+bucket-shake" : string.Empty)}, " +
                $"sealedBucket={_sealBucket}, " +
                $"outflowTest={_outflowTest}, " +
                $"restingTest={_restingTest}, " +
                $"status={stats.status}, " +
                $"substeps={_executedSubsteps}, " +
                $"diagnosticsStep={stats.gpuDiagnosticsStepIndex}, " +
                $"particles={stats.particleCount}, " +
                $"dispatches={stats.gpuDispatchCount}, " +
                $"gpuProfile={stats.gpuStageProfilingEnabled}, " +
                $"gpuSetupMs={stats.gpuProfileMpmSetupMilliseconds:F3}, " +
                $"gpuP2GMs={stats.gpuProfileP2GMilliseconds:F3}, " +
                $"gpuGridMs={stats.gpuProfileGridUpdateMilliseconds:F3}, " +
                $"gpuMpmMs={stats.gpuProfileMpmCoreMilliseconds:F3}, " +
                $"gpuProjectionMs={stats.gpuProfileProjectionMilliseconds:F3}, " +
                $"gpuPostMs={stats.gpuProfileParticlePostMilliseconds:F3}, " +
                $"gpuTotalMs={stats.gpuProfileTotalMilliseconds:F3}, " +
                $"materialPreset={(gpuConfig != null ? gpuConfig.paintMaterialPreset.ToString() : "None")}, " +
                $"bulkModulus={(gpuConfig != null ? gpuConfig.bulkModulus : 0.0f):F3}, " +
                $"maxStress={(gpuConfig != null ? gpuConfig.maxStressMagnitude : 0.0f):F3}, " +
                $"mu0={(gpuConfig != null ? gpuConfig.lowShearViscosity : 0.0f):F3}, " +
                $"muInf={(gpuConfig != null ? gpuConfig.highShearViscosity : 0.0f):F3}, " +
                $"carreauA={(gpuConfig != null ? gpuConfig.carreauYasudaExponent : 0.0f):F3}, " +
                $"powerN={(gpuConfig != null ? gpuConfig.shearThinningPowerN : 0.0f):F3}, " +
                $"yieldStress={(gpuConfig != null ? gpuConfig.yieldStress : 0.0f):F3}, " +
                $"jetCohesion={(gpuConfig != null && gpuConfig.enableJetCohesion)}, " +
                $"jetCohesionAccel={(gpuConfig != null ? gpuConfig.jetCohesionAcceleration : 0.0f):F3}, " +
                $"adaptiveMultiRate={stats.gpuAdaptiveMultiRateEnabled}, " +
                $"adaptiveSampleStep={stats.gpuAdaptiveActivityStepIndex}, " +
                $"adaptivePriority={stats.gpuAdaptivePriorityParticleCount}, " +
                $"adaptiveCalm={stats.gpuAdaptiveCalmInteriorParticleCount}, " +
                $"adaptivePriorityFraction={stats.gpuAdaptivePriorityParticleFraction:F3}, " +
                $"adaptiveAvgSpeed={stats.gpuAdaptiveAverageParticleSpeed:F3}, " +
                $"adaptiveMaxSpeed={stats.gpuAdaptiveMaximumParticleSpeed:F3}, " +
                $"adaptiveAvgJDeviation={stats.gpuAdaptiveAverageJDeviation:F3}, " +
                $"gridDensityPredictor={stats.gpuGridDensityPredictorUsed}, " +
                $"gridDensityEosPressureScale={stats.gpuGridDensityEosPressureScale:F3}, " +
                $"gridDensityEosActivationRatio={stats.gpuGridDensityEosActivationRatio:F4}, " +
                $"referenceGridRestDensity={stats.gpuReferenceGridRestDensity:F3}, " +
                $"gridContainsBucket={stats.gpuGridContainsBucket}, " +
                $"bucketLocal={stats.gpuBucketLocalSimulation}, " +
                $"frameLinearAccel={stats.gpuBucketFrameLinearAcceleration:F3}, " +
                $"frameAngularSpeed={stats.gpuBucketFrameAngularVelocity:F3}, " +
                $"frameAngularAccel={stats.gpuBucketFrameAngularAcceleration:F3}, " +
                $"mpmTiles={stats.gpuMpmTileOccupancyUsed}, " +
                $"tiledMpmDispatch={stats.gpuTiledMpmGridDispatchUsed}, " +
                $"mpmTileSize={stats.gpuMpmTileSizeCells}, " +
                $"mpmTileCount={stats.gpuMpmTileCount}, " +
                $"mpmActiveTiles={stats.gpuMpmActiveTileCount}, " +
                $"mpmActiveTileFraction={stats.gpuMpmActiveTileFraction:F3}, " +
                $"mpmParticleLists={stats.gpuMpmParticleTileListsUsed}, " +
                $"supportMaskTopology={stats.gpuMpmSupportMaskTopologyUsed}, " +
                $"ownerListInterval={stats.gpuOwnerTileListRebuildInterval}, " +
                $"ownerListRebuilt={stats.gpuOwnerTileListRebuilt}, " +
                $"ownerListReused={stats.gpuOwnerTileListReused}, " +
                $"hybridTiledP2G={stats.gpuHybridTiledP2GUsed}, " +
                $"fusedG2PCollision={stats.gpuFusedG2PPostCollisionUsed}, " +
                $"fusedPreCollisionMark={stats.gpuFusedPreCollisionTileMarkUsed}, " +
                $"adaptiveTransferStencil={stats.gpuAdaptiveTransferStencilUsed}, " +
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
                $"freeSurface={_fluid.GpuMpmConfig != null && _fluid.GpuMpmConfig.enableFreeSurfacePolish}, " +
                $"fillAvg01={stats.gpuAverageFillHeight01:F4}, " +
                $"fillMin01={stats.gpuMinimumFillHeight01:F4}, " +
                $"fillMax01={stats.gpuMaximumFillHeight01:F4}, " +
                $"fillSpan01={stats.gpuEstimatedFillSpan01:F4}, " +
                $"speedAvg={stats.gpuAverageParticleSpeed:F4}, " +
                $"speedMax={stats.gpuMaximumParticleSpeed:F4}, " +
                $"nanInf={stats.gpuNanInfCount}, " +
                $"holes={stats.gpuConfiguredHoleCount}, " +
                $"holeOpen={stats.gpuHoleOpeningEnabled}, " +
                $"airborneDispatch={stats.gpuAirborneDispatchRan}, " +
                $"postCollision={stats.gpuPostBucketCollisionRan}, " +
                $"outflow={stats.gpuOutflowTransitionCount}, " +
                $"maxObservedOutflow={_maxOutflowTransitionsObserved}, " +
                $"jet={stats.gpuJetParticleCount}, " +
                $"maxObservedJet={_maxJetParticlesObserved}, " +
                $"jetMpmCollar={stats.gpuJetMpmCollarParticleCount}, " +
                $"maxObservedJetMpmCollar={_maxJetMpmCollarParticlesObserved}, " +
                $"jetColumnCoherence={stats.gpuJetColumnCoherenceEnabled}, " +
                $"jetCoherenceLengthM={stats.gpuJetCoherenceLengthMeters:F3}, " +
                $"jetColumn={stats.gpuJetColumnParticleCount}, " +
                $"maxObservedJetColumn={_maxJetColumnParticlesObserved}, " +
                $"jetColumnAvgRadiusM={stats.gpuJetColumnAverageRadiusMeters:F4}, " +
                $"airborne={stats.gpuAirborneParticleCount}, " +
                $"maxObservedAirborne={_maxAirborneParticlesObserved}, " +
                $"lost={stats.gpuLostParticleCount}, " +
                $"fluidCells={stats.projectionFluidCellCount}, " +
                $"activeProjection={stats.projectionActiveBoundsUsed}, " +
                $"tileProjection={stats.projectionMpmTileDispatchUsed}, " +
                $"densityDrift={stats.projectionDensityDriftCorrectionEnabled}, " +
                $"densityDriftStrength={stats.projectionDensityDriftStrength:F3}, " +
                $"activeProjectionNodes={stats.projectionActiveNodeCount}, " +
                $"activeProjectionFraction={stats.projectionActiveNodeFraction:F3}, " +
                $"activeProjectionMin=({stats.projectionActiveMinX},{stats.projectionActiveMinY},{stats.projectionActiveMinZ}), " +
                $"activeProjectionSize=({stats.projectionActiveSizeX},{stats.projectionActiveSizeY},{stats.projectionActiveSizeZ}), " +
                $"sparsePressure={stats.projectionSparsePressureDispatchUsed}, " +
                $"sparsePressureCells={stats.projectionSparsePressureCellCount}, " +
                $"sparsePressureFraction={stats.projectionSparsePressureCellFraction:F3}, " +
                $"adaptiveProjection={stats.projectionAdaptiveCadenceEnabled}, " +
                $"adaptiveProjectionCalm={stats.projectionAdaptiveCalmMode}, " +
                $"effectiveProjectionInterval={stats.projectionEffectiveSubstepInterval}, " +
                $"projectionCalmCounter={stats.projectionCalmCounter}, " +
                $"projectionCalmSubsteps={stats.projectionAdaptiveCalmSubstepCount}, " +
                $"projectionCalmEntries={stats.projectionAdaptiveCalmEntryCount}, " +
                $"projectionCalmExits={stats.projectionAdaptiveCalmExitCount}, " +
                $"divBeforeAvg={stats.projectionAverageAbsDivergenceBefore:F5}, " +
                $"divResidualAfterAvg={stats.projectionAverageAbsDivergenceAfter:F5}, " +
                "pressureMode=RedBlackSor, " +
                $"rbSorIter={stats.pressureRedBlackSorIterations}, " +
                $"rbSorOmega={stats.pressureRedBlackSorOmega:F2}, " +
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
            _maxJetMpmCollarParticlesObserved = Mathf.Max(
                _maxJetMpmCollarParticlesObserved,
                stats.gpuJetMpmCollarParticleCount
            );
            _maxJetColumnParticlesObserved = Mathf.Max(
                _maxJetColumnParticlesObserved,
                stats.gpuJetColumnParticleCount
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

            if (_disableCheckpointWaits)
                return false;

            if (!_forceDiagnosticCheckpointWaits &&
                _fluid != null &&
                _fluid.GpuMpmConfig != null &&
                _fluid.GpuMpmConfig.enableAdaptiveMultiRate &&
                _fluid.GpuMpmConfig.enableAdaptiveProjectionCadence)
            {
                return false;
            }

            return completedSubsteps ==
                   GetExpectedDiagnosticsStepAtOrBefore(completedSubsteps);
        }

        private static int GetExpectedDiagnosticsStepAtOrBefore(int completedSubsteps)
        {
            if (completedSubsteps <= 0 ||
                _diagnosticsInterval <= 0)
                return 0;

            if (!_forceDiagnosticCheckpointWaits &&
                _fluid != null &&
                _fluid.GpuMpmConfig != null &&
                _fluid.GpuMpmConfig.enableAdaptiveMultiRate &&
                _fluid.GpuMpmConfig.enableAdaptiveProjectionCadence)
            {
                return Mathf.Max(0, _lastRecordedDiagnosticsStep);
            }

            if (_fluid == null ||
                _fluid.GpuMpmConfig == null)
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

        private static int GetExpectedAdaptiveActivityStepAtOrBefore(
            int completedSubsteps)
        {
            if (completedSubsteps <= 0 ||
                _fluid == null ||
                _fluid.GpuMpmConfig == null ||
                !_fluid.GpuMpmConfig.enableAdaptiveMultiRate)
            {
                return 0;
            }

            int interval = Mathf.Clamp(
                _fluid.GpuMpmConfig.adaptiveActivitySampleInterval,
                1,
                32
            );

            return
                1 +
                ((completedSubsteps - 1) / interval) *
                interval;
        }

        private static void ConfigureValidationMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            _projectionTest = HasFlag(args, "-paintValidationProjection");
            _shakeBucket = HasFlag(args, "-paintValidationShakeBucket");
            _sealBucket = HasFlag(args, "-paintValidationSealBucket");
            _outflowTest = HasFlag(args, "-paintValidationOutflow");
            _restingTest = HasFlag(
                args,
                "-paintValidationResting"
            );
            _restingMaxAverageSpeed = GetFloatArgument(
                args,
                "-paintValidationRestingMaxAverageSpeed",
                0.06f
            );
            _restingMaxMaximumSpeed = GetFloatArgument(
                args,
                "-paintValidationRestingMaxMaximumSpeed",
                0.50f
            );
            _restingMaxAverageJError = GetFloatArgument(
                args,
                "-paintValidationRestingMaxAverageJError",
                0.05f
            );
            if (_restingTest)
            {
                _shakeBucket = false;
                _outflowTest = false;
                _sealBucket = true;
            }
            _waitAdaptiveActivity =
                HasFlag(args, "-paintValidationWaitAdaptiveActivity");
            _disableCheckpointWaits =
                HasFlag(args, "-paintValidationNoCheckpointWaits");
            _forceDiagnosticCheckpointWaits =
                HasFlag(args, "-paintValidationForceDiagnosticWaits");
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
                _fluid.GpuMpmConfig.enableGpuStageProfiling =
                    HasFlag(args, "-paintValidationGpuStageProfile");
                _fluid.GpuMpmConfig.diagnosticsReadbackInterval =
                    _diagnosticsInterval;
                _fluid.GpuMpmConfig.ownerTileListRebuildInterval =
                    Mathf.Clamp(
                        GetIntArgument(
                            args,
                            "-paintValidationOwnerTileListInterval",
                            _fluid.GpuMpmConfig
                                .ownerTileListRebuildInterval
                        ),
                        1,
                        2
                    );
                _fluid.GpuMpmConfig.ownerTileListReuseMinParticles =
                    Mathf.Max(
                        0,
                        GetIntArgument(
                            args,
                            "-paintValidationOwnerTileListReuseMinParticles",
                            _fluid.GpuMpmConfig
                                .ownerTileListReuseMinParticles
                        )
                    );
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
                int materialPreset = GetRawIntArgument(
                    args,
                    "-paintValidationMaterialPreset",
                    -1
                );
                if (materialPreset >= 0)
                {
                    materialPreset = Mathf.Clamp(
                        materialPreset,
                        (int)GpuPaintMaterialPreset.Custom,
                        (int)GpuPaintMaterialPreset.HeavyBodyPaint
                    );
                    baseConfig.ApplyPaintMaterialPreset(
                        (GpuPaintMaterialPreset)materialPreset
                    );
                }

                int gridResolutionOverride = GetRawIntArgument(
                    args,
                    "-paintValidationGridResolution",
                    -1
                );
                if (gridResolutionOverride > 0)
                {
                    baseConfig.gridResolution = new Vector3Int(
                        gridResolutionOverride,
                        gridResolutionOverride,
                        gridResolutionOverride
                    );
                }
                baseConfig.cellSizeMeters = GetFloatArgument(
                    args,
                    "-paintValidationCellSize",
                    baseConfig.cellSizeMeters
                );
                if (HasFlag(args, "-paintValidationEnableAdaptiveTransferStencil"))
                {
                    baseConfig.enableAdaptiveTransferStencil = true;
                    baseConfig.enableAdaptiveMultiRate = true;
                }
                else if (HasFlag(
                    args,
                    "-paintValidationDisableAdaptiveTransferStencil"))
                {
                    baseConfig.enableAdaptiveTransferStencil = false;
                }
                if (HasFlag(args, "-paintValidationEnableAdaptiveG2PTransferStencil"))
                {
                    baseConfig.enableAdaptiveG2PTransferStencil = true;
                }
                else if (HasFlag(
                    args,
                    "-paintValidationDisableAdaptiveG2PTransferStencil"))
                {
                    baseConfig.enableAdaptiveG2PTransferStencil = false;
                }
                baseConfig.adaptiveTransferMinParticles = GetIntArgument(
                    args,
                    "-paintValidationAdaptiveTransferMinParticles",
                    baseConfig.adaptiveTransferMinParticles
                );
                baseConfig.mpmTileSizeCells = Mathf.Clamp(
                    GetRawIntArgument(
                        args,
                        "-paintValidationMpmTileSize",
                        baseConfig.mpmTileSizeCells
                    ),
                    4,
                    8
                );
                baseConfig.enablePostG2PBucketCollision =
                    !HasFlag(args, "-paintValidationDisablePostBucketCollision");
                baseConfig.enableAdaptivePostG2PBucketCollision =
                    !HasFlag(args, "-paintValidationDisableAdaptivePostBucketCollision");
                baseConfig.enableSmartAirborneDispatch =
                    !HasFlag(args, "-paintValidationDisableSmartAirborneDispatch");
                baseConfig.projectionSubstepInterval = GetIntArgument(
                    args,
                    "-paintValidationProjectionInterval",
                    baseConfig.projectionSubstepInterval
                );
                if (HasFlag(args, "-paintValidationEnableSparseProjectionPressure"))
                {
                    baseConfig.enableSparseProjectionPressureDispatch = true;
                }
                else if (HasFlag(args, "-paintValidationDisableSparseProjectionPressure"))
                {
                    baseConfig.enableSparseProjectionPressureDispatch = false;
                }
                if (HasFlag(args, "-paintValidationEnableTileProjection"))
                {
                    baseConfig.enableMpmTileProjectionDispatch = true;
                }
                else if (HasFlag(args, "-paintValidationDisableTileProjection"))
                {
                    baseConfig.enableMpmTileProjectionDispatch = false;
                }
                if (HasFlag(args, "-paintValidationDisableProjectionDensityDrift"))
                {
                    baseConfig.enableProjectionDensityDriftCorrection = false;
                }
                else if (HasFlag(args, "-paintValidationEnableProjectionDensityDrift"))
                {
                    baseConfig.enableProjectionDensityDriftCorrection = true;
                }
                baseConfig.projectionDensityDriftStrength =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionDensityDriftStrength",
                        baseConfig.projectionDensityDriftStrength
                    );
                baseConfig.projectionDensityDriftMinRatio =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionDensityDriftMinRatio",
                        baseConfig.projectionDensityDriftMinRatio
                    );
                baseConfig.projectionDensityDriftMaxDivergence =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionDensityDriftMaxDivergence",
                        baseConfig.projectionDensityDriftMaxDivergence
                    );
                if (HasFlag(args, "-paintValidationEnableAdaptiveMultiRate"))
                {
                    baseConfig.enableAdaptiveMultiRate = true;
                }
                else if (HasFlag(args, "-paintValidationDisableAdaptiveMultiRate"))
                {
                    baseConfig.enableAdaptiveMultiRate = false;
                }
                if (HasFlag(args, "-paintValidationEnableAdaptiveProjection"))
                {
                    baseConfig.enableAdaptiveProjectionCadence = true;
                }
                else if (HasFlag(args, "-paintValidationDisableAdaptiveProjection"))
                {
                    baseConfig.enableAdaptiveProjectionCadence = false;
                }
                baseConfig.adaptiveActivitySampleInterval = GetIntArgument(
                    args,
                    "-paintValidationAdaptiveSampleInterval",
                    baseConfig.adaptiveActivitySampleInterval
                );
                baseConfig.adaptivePriorityParticleSpeed = GetFloatArgument(
                    args,
                    "-paintValidationAdaptivePrioritySpeed",
                    baseConfig.adaptivePriorityParticleSpeed
                );
                baseConfig.adaptivePriorityJDeviation = GetFloatArgument(
                    args,
                    "-paintValidationAdaptivePriorityJDeviation",
                    baseConfig.adaptivePriorityJDeviation
                );
                baseConfig.adaptivePriorityTopFraction = GetFloatArgument(
                    args,
                    "-paintValidationAdaptivePriorityTopFraction",
                    baseConfig.adaptivePriorityTopFraction
                );
                baseConfig.adaptivePriorityBoundaryBandMeters =
                    GetFloatArgument(
                        args,
                        "-paintValidationAdaptivePriorityBoundaryBand",
                        baseConfig.adaptivePriorityBoundaryBandMeters
                    );
                baseConfig.calmProjectionSubstepInterval = Mathf.Clamp(
                    GetIntArgument(
                        args,
                        "-paintValidationCalmProjectionInterval",
                        baseConfig.calmProjectionSubstepInterval
                    ),
                    1,
                    8
                );
                baseConfig.adaptiveProjectionCalmDelaySubsteps =
                    GetIntArgument(
                        args,
                        "-paintValidationProjectionCalmDelay",
                        baseConfig.adaptiveProjectionCalmDelaySubsteps
                    );
                baseConfig.adaptiveProjectionCalmAverageSpeed =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionCalmSpeed",
                        baseConfig.adaptiveProjectionCalmAverageSpeed
                    );
                baseConfig.adaptiveProjectionCalmPriorityFraction =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionCalmPriorityFraction",
                        baseConfig.adaptiveProjectionCalmPriorityFraction
                    );
                baseConfig.adaptiveProjectionMaxAverageJDeviation =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionMaxAverageJDeviation",
                        baseConfig.adaptiveProjectionMaxAverageJDeviation
                    );
                baseConfig.adaptiveProjectionBucketLinearSpeed =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionBucketLinearThreshold",
                        baseConfig.adaptiveProjectionBucketLinearSpeed
                    );
                baseConfig.adaptiveProjectionBucketAngularSpeed =
                    GetFloatArgument(
                        args,
                        "-paintValidationProjectionBucketAngularThreshold",
                        baseConfig.adaptiveProjectionBucketAngularSpeed
                    );
                baseConfig.enableFreeSurfacePolish =
                    !HasFlag(args, "-paintValidationDisableFreeSurfacePolish");
                if (HasFlag(args, "-paintValidationDisableMaterialStress"))
                    baseConfig.enableMaterialStress = false;
                else if (HasFlag(args, "-paintValidationEnableMaterialStress"))
                    baseConfig.enableMaterialStress = true;

                if (HasFlag(args, "-paintValidationDisablePaintRheology"))
                    baseConfig.enablePaintRheology = false;
                else if (HasFlag(args, "-paintValidationEnablePaintRheology"))
                    baseConfig.enablePaintRheology = true;

                baseConfig.gridDensityEosMaxVelocityCorrection =
                    GetFloatArgument(
                        args,
                        "-paintValidationGridDensityEosMaxCorrection",
                        baseConfig.gridDensityEosMaxVelocityCorrection
                    );
                baseConfig.gridDensityEosPressureScale =
                    GetFloatArgument(
                        args,
                        "-paintValidationGridDensityEosPressureScale",
                        baseConfig.gridDensityEosPressureScale
                    );
                baseConfig.gridDensityEosActivationRatio =
                    GetFloatArgument(
                        args,
                        "-paintValidationGridDensityEosActivationRatio",
                        baseConfig.gridDensityEosActivationRatio
                    );

                if (HasFlag(args, "-paintValidationEnableJetMpmCollar"))
                    baseConfig.enableJetMpmCollar = true;
                else if (HasFlag(args, "-paintValidationDisableJetMpmCollar"))
                    baseConfig.enableJetMpmCollar = false;
                baseConfig.jetMpmCollarDurationSeconds =
                    GetFloatArgument(
                        args,
                        "-paintValidationJetMpmCollarDuration",
                        baseConfig.jetMpmCollarDurationSeconds
                    );
                baseConfig.jetMpmCollarMaxDistanceMeters =
                    GetFloatArgument(
                        args,
                        "-paintValidationJetMpmCollarMaxDistance",
                        baseConfig.jetMpmCollarMaxDistanceMeters
                    );
                baseConfig.jetMpmCollarRadialPaddingMeters =
                    GetFloatArgument(
                        args,
                        "-paintValidationJetMpmCollarRadialPadding",
                        baseConfig.jetMpmCollarRadialPaddingMeters
                    );

                if (HasFlag(args, "-paintValidationEnableJetColumnCoherence"))
                    baseConfig.enableJetColumnCoherence = true;
                else if (HasFlag(args, "-paintValidationDisableJetColumnCoherence"))
                    baseConfig.enableJetColumnCoherence = false;
                if (HasFlag(
                    args,
                    "-paintValidationDisableJetCoherenceMaterialScaling"))
                {
                    baseConfig.jetCoherenceMaterialScaling = false;
                }
                else if (HasFlag(
                    args,
                    "-paintValidationEnableJetCoherenceMaterialScaling"))
                {
                    baseConfig.jetCoherenceMaterialScaling = true;
                }
                baseConfig.jetCoherenceLengthMeters = GetFloatArgument(
                    args,
                    "-paintValidationJetCoherenceLength",
                    baseConfig.jetCoherenceLengthMeters
                );
                baseConfig.jetTransverseDampingPerSecond = GetFloatArgument(
                    args,
                    "-paintValidationJetTransverseDamping",
                    baseConfig.jetTransverseDampingPerSecond
                );
                baseConfig.jetCenterlineAttractionPerSecond = GetFloatArgument(
                    args,
                    "-paintValidationJetCenterlineAttraction",
                    baseConfig.jetCenterlineAttractionPerSecond
                );
                baseConfig.jetColumnDragScale = GetFloatArgument(
                    args,
                    "-paintValidationJetColumnDragScale",
                    baseConfig.jetColumnDragScale
                );
                baseConfig.maxJetColumnVelocityCorrectionPerSubstep =
                    GetFloatArgument(
                        args,
                        "-paintValidationMaxJetColumnCorrection",
                        baseConfig.maxJetColumnVelocityCorrectionPerSubstep
                    );

                baseConfig.referenceEosExponent = GetFloatArgument(
                    args,
                    "-paintValidationReferenceEosExponent",
                    baseConfig.referenceEosExponent
                );
                baseConfig.freeSurfaceGradientScale = GetFloatArgument(
                    args,
                    "-paintValidationFreeSurfaceGradientScale",
                    baseConfig.freeSurfaceGradientScale
                );
                baseConfig.freeSurfaceNormalDampingPerSecond = GetFloatArgument(
                    args,
                    "-paintValidationFreeSurfaceDamping",
                    baseConfig.freeSurfaceNormalDampingPerSecond
                );
                baseConfig.freeSurfaceCohesionAcceleration = GetFloatArgument(
                    args,
                    "-paintValidationFreeSurfaceCohesion",
                    baseConfig.freeSurfaceCohesionAcceleration
                );
                baseConfig.maxFreeSurfaceVelocityCorrection = GetFloatArgument(
                    args,
                    "-paintValidationMaxFreeSurfaceCorrection",
                    baseConfig.maxFreeSurfaceVelocityCorrection
                );
                baseConfig.enablePressureWarmStart =
                    !HasFlag(args, "-paintValidationDisableWarmStart");
                baseConfig.pressureWarmStartFactor = GetFloatArgument(
                    args,
                    "-paintValidationWarmStartFactor",
                    baseConfig.pressureWarmStartFactor
                );
            }

            var config = _fluid.GpuMpmConfig;
            if (config != null)
            {
                if (_sealBucket)
                {
                    config.topBoundaryMode =
                        GpuBucketTopMode.TemporaryLid;
                    config.projectionTopOpen = false;
                    config.enableBottomHoleOpening = false;
                }

                config.bulkModulus = GetFloatArgument(
                    args,
                    "-paintValidationBulkModulus",
                    config.bulkModulus
                );
                config.maxStressMagnitude = GetFloatArgument(
                    args,
                    "-paintValidationMaxStressMagnitude",
                    config.maxStressMagnitude
                );
                config.materialStressStrength = GetFloatArgument(
                    args,
                    "-paintValidationStressStrength",
                    config.materialStressStrength
                );
                config.lowShearViscosity = GetFloatArgument(
                    args,
                    "-paintValidationLowShearViscosity",
                    config.lowShearViscosity
                );
                config.highShearViscosity = GetFloatArgument(
                    args,
                    "-paintValidationHighShearViscosity",
                    config.highShearViscosity
                );
                config.shearThinningRelaxationTime = GetFloatArgument(
                    args,
                    "-paintValidationShearRelaxation",
                    config.shearThinningRelaxationTime
                );
                config.shearThinningPowerN = GetFloatArgument(
                    args,
                    "-paintValidationShearPowerN",
                    config.shearThinningPowerN
                );
                config.carreauYasudaExponent = GetFloatArgument(
                    args,
                    "-paintValidationCarreauYasudaA",
                    config.carreauYasudaExponent
                );
                config.yieldStress = GetFloatArgument(
                    args,
                    "-paintValidationYieldStress",
                    config.yieldStress
                );
                config.yieldRegularizationRate = GetFloatArgument(
                    args,
                    "-paintValidationYieldRegularization",
                    config.yieldRegularizationRate
                );
                config.maxYieldViscosityContribution = GetFloatArgument(
                    args,
                    "-paintValidationMaxYieldViscosity",
                    config.maxYieldViscosityContribution
                );
                config.maxEffectiveViscosity = GetFloatArgument(
                    args,
                    "-paintValidationMaxEffectiveViscosity",
                    config.maxEffectiveViscosity
                );
                if (HasFlag(args, "-paintValidationDisableJetCohesion"))
                    config.enableJetCohesion = false;
                else if (HasFlag(args, "-paintValidationEnableJetCohesion"))
                    config.enableJetCohesion = true;
                config.jetCohesionAcceleration = GetFloatArgument(
                    args,
                    "-paintValidationJetCohesion",
                    config.jetCohesionAcceleration
                );
                config.maxJetCohesionVelocityCorrection = GetFloatArgument(
                    args,
                    "-paintValidationMaxJetCohesionCorrection",
                    config.maxJetCohesionVelocityCorrection
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
            }

            if (!_projectionTest || config == null)
            {
                if (_outflowTest && config != null)
                    ConfigureOutflowValidation(args);

                return;
            }

            config.enableProjectionDivergenceComputation = true;
            config.enablePressureGradientSubtraction = true;
            config.useStaggeredFaceProjection = true;
            if (HasFlag(args, "-paintValidationDisableStaggeredProjection"))
                config.useStaggeredFaceProjection = false;
            config.pressureRedBlackSorIterations = GetIntArgument(
                args,
                "-paintValidationRedBlackSorIterations",
                config.pressureRedBlackSorIterations
            );
            config.pressureRhsScale = GetFloatArgument(
                args,
                "-paintValidationPressureRhs",
                1.0f
            );
            config.pressureRedBlackSorOmega = GetFloatArgument(
                args,
                "-paintValidationRedBlackSorOmega",
                config.pressureRedBlackSorOmega
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

        private static int GetRawIntArgument(
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
                    return value;
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
