using PaintBucketSim.Configs;

namespace PaintBucketSim.Core
{
    /// <summary>
    /// Controls pause, single-step, fixed simulation time, and substep timing.
    /// This is our own simulation timing layer on top of Unity FixedUpdate.
    /// </summary>
    public sealed class TimeStepController
    {
        public bool IsPaused { get; private set; }

        public double SimulationTime { get; private set; }
        public long FixedStepIndex { get; private set; }
        public long SubstepIndex { get; private set; }

        private SimulationConfig _config;
        private bool _singleStepRequested;

        public void Initialize(SimulationConfig config)
        {
            _config = config;
            IsPaused = config != null && config.startPaused;

            SimulationTime = 0.0;
            FixedStepIndex = 0;
            SubstepIndex = 0;
            _singleStepRequested = false;
        }

        public void TogglePause()
        {
            IsPaused = !IsPaused;
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        public void RequestSingleStep()
        {
            _singleStepRequested = true;
        }

        public bool ShouldRunFixedStep()
        {
            if (!IsPaused)
                return true;

            if (_singleStepRequested)
            {
                _singleStepRequested = false;
                return true;
            }

            return false;
        }

        public float GetFixedDeltaTime()
        {
            if (_config == null)
                return 1.0f / 60.0f;

            return _config.fixedDeltaTime * _config.simulationTimeScale;
        }

        public int GetSubstepCount()
        {
            if (_config == null)
                return 1;

            return _config.substeps < 1 ? 1 : _config.substeps;
        }

        public float GetSubstepDeltaTime()
        {
            return GetFixedDeltaTime() / GetSubstepCount();
        }

        public void BeginFixedStep()
        {
            FixedStepIndex++;
        }

        public void AdvanceSubstep(float dt)
        {
            SimulationTime += dt;
            SubstepIndex++;
        }

        public void Reset()
        {
            SimulationTime = 0.0;
            FixedStepIndex = 0;
            SubstepIndex = 0;
            _singleStepRequested = false;
        }
    }
}