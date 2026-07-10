using PaintBucketSim.Runtime;
using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "FluidSolverArchitectureConfig",
        menuName = "Paint Bucket Sim/Fluid Solver Architecture Config")]
    public class FluidSolverArchitectureConfig : ScriptableObject
    {
        [Header("Active Solver")]
        public FluidSolverType activeSolver = FluidSolverType.CpuPbf;

        [Header("Educational / Debug Architecture")]
        public bool enableSolverStats = true;

        [Tooltip("Print high-level architecture messages when solvers are created/switched.")]
        public bool logSolverLifecycle = true;

        [Header("Long-Term Target")]
        [TextArea(4, 10)]
        public string architectureGoal =
            "The paint system is solver-independent. CPU PBF remains as a baseline solver. " +
            "GPU Sparse APIC/MLS-MPM will be added as an advanced solver using the same material, particle pool, states, and diagnostics.";
    }
}
