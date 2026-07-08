using PaintBucketSim.Core;
using PaintBucketSim.Runtime;

namespace PaintBucketSim.Systems.Environment
{
    /// <summary>
    /// Produces the current environment state.
    /// Later this can support time-varying wind, pivot vibration, temperature changes, etc.
    /// </summary>
    public sealed class EnvironmentSystem
    {
        public void Initialize(SimulationContext context)
        {
            Step(context, 0.0f);
        }

        public void Step(SimulationContext context, float dt)
        {
            if (context == null || context.EnvironmentConfig == null)
                return;

            var config = context.EnvironmentConfig;

            EnvironmentState state = new EnvironmentState
            {
                gravity = config.gravity,
                windVelocity = config.windVelocity,

                airDensity = config.airDensity,
                airViscosity = config.airViscosity,

                temperatureCelsius = config.temperatureCelsius,
                relativeHumidity01 = config.relativeHumidity01,
                pressurePa = config.pressurePa
            };

            context.EnvironmentState = state;
        }
    }
}