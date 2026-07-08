using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using System;

namespace PaintBucketSim.Systems.Fluid.Solvers
{
    public interface IFluidSolver : IDisposable
    {
        FluidSolverType SolverType { get; }
        bool IsInitialized { get; }
        FluidSolverStats Stats { get; }

        void Initialize(FluidSolverContext context);

        void Step(
            FluidSolverContext solverContext,
            SimulationContext simulationContext,
            FluidSolverStepInput stepInput);

        void Reset(FluidSolverContext context);
    }
}