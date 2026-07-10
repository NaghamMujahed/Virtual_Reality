using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "GpuFluidBufferConfig",
        menuName = "Paint Bucket Sim/GPU Fluid Buffer Config")]
    public class GpuFluidBufferConfig : ScriptableObject
    {
        [Header("Ownership")]
        public bool enableGpuFluidBuffers = true;

        [Tooltip("During CPU/PBF phase, upload CPU particle snapshot to GPU. Later GPU solvers will write buffers directly.")]
        public bool uploadFromCpuWhileCpuSolverActive = true;

        [Tooltip("Upload CPU snapshot every N frames.")]
        [Min(1)]
        public int uploadEveryNFrames = 1;

        [Header("Compute Preparation")]
        public bool enableComputePostProcess = false;

        [Tooltip("Debug only: color particles on GPU based on particle state.")]
        public bool debugColorByStateOnGpu = false;

        [Header("State Debug Colors")]
        public Color insideColor = new Color(0.1f, 0.35f, 1.0f, 1.0f);
        public Color nearHoleColor = new Color(1.0f, 0.25f, 0.05f, 1.0f);
        public Color airborneColor = new Color(0.7f, 0.9f, 1.0f, 1.0f);
        public Color depositedColor = new Color(0.15f, 0.9f, 0.25f, 1.0f);
        public Color defaultStateColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        [Header("Debug")]
        public bool logLifecycle = true;

        private void OnValidate()
        {
            if (uploadEveryNFrames < 1)
                uploadEveryNFrames = 1;
        }
    }
}
