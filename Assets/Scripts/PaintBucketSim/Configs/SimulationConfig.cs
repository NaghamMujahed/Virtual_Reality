using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "SimulationConfig",
        menuName = "Paint Bucket Sim/Simulation Config")]
    public class SimulationConfig : ScriptableObject
    {
        [Header("Time")]
        [Tooltip("Main fixed simulation timestep in seconds. Example: 1/60 = 0.0166667")]
        [Min(0.001f)]
        public float fixedDeltaTime = 1.0f / 60.0f;

        [Tooltip("Internal substeps per fixed simulation step. Coupled rope, bucket, and MPM require at least two.")]
        [Range(2, 8)]
        public int substeps = 2;

        [Tooltip("Internal simulation speed multiplier. Keep 1 for real-time.")]
        [Range(0.05f, 4.0f)]
        public float simulationTimeScale = 1.0f;

        [Tooltip("Start simulation paused.")]
        public bool startPaused = false;

        [Tooltip("Apply this config's fixedDeltaTime to Unity Time.fixedDeltaTime on start.")]
        public bool applyToUnityFixedDeltaTime = true;

        [Tooltip("Unity maximum allowed timestep to avoid very long catch-up physics frames.")]
        [Min(0.01f)]
        public float maximumAllowedTimestep = 0.1f;

        [Header("Project Rules")]
        [Tooltip("Official unit convention: 1 Unity Unit = 1 meter.")]
        public bool oneUnityUnitEqualsOneMeter = true;

        [Header("Diagnostics")]
        public bool enableDiagnostics = true;

        [Tooltip("Seed used later for deterministic emission/noise experiments.")]
        public int randomSeed = 12345;

        private void OnValidate()
        {
            if (fixedDeltaTime < 0.001f)
                fixedDeltaTime = 0.001f;

            if (maximumAllowedTimestep < fixedDeltaTime)
                maximumAllowedTimestep = fixedDeltaTime;

            if (substeps < 2)
                substeps = 2;

            if (!oneUnityUnitEqualsOneMeter)
            {
                Debug.LogWarning(
                    "This project is designed around 1 Unity Unit = 1 meter. " +
                    "Changing this will require unit conversion everywhere.");
            }
        }
    }
}
