using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;

namespace PaintBucketSim.Core
{
    /// <summary>
    /// Shared context passed to systems.
    /// It owns references to configs and runtime global states.
    /// Later: rope data, bucket data, fluid data, canvas data will be added here.
    /// </summary>
    public sealed class SimulationContext
    {
        public SimulationConfig SimulationConfig { get; private set; }
        public EnvironmentConfig EnvironmentConfig { get; private set; }

        public EnvironmentState EnvironmentState;
        public DiagnosticsFrame Diagnostics;

        public bool IsInitialized { get; private set; }

        public void Initialize(
            SimulationConfig simulationConfig,
            EnvironmentConfig environmentConfig)
        {
            SimulationConfig = simulationConfig;
            EnvironmentConfig = environmentConfig;

            EnvironmentState = default;
            Diagnostics = default;
            Diagnostics.initialized = true;

            IsInitialized = true;
        }
    }
}