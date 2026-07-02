using PaintBucketSim.Configs;
using PaintBucketSim.Systems.Fluid;
using UnityEngine;

namespace PaintBucketSim.Systems.Canvas
{
    internal readonly struct MpmCanvasPaintMaterialSample
    {
        public MpmCanvasPaintMaterialSample(
            float density,
            float viscosity,
            float surfaceTension,
            float yieldStress)
        {
            Density = density;
            Viscosity = viscosity;
            SurfaceTension = surfaceTension;
            YieldStress = yieldStress;
        }

        public float Density { get; }
        public float Viscosity { get; }
        public float SurfaceTension { get; }
        public float YieldStress { get; }
    }

    internal static class MpmCanvasPaintMaterial
    {
        public static MpmCanvasPaintMaterialSample Resolve(PaintFluidSystem paintFluidSystem)
        {
            float density = 1050.0f;
            float viscosity = 1.0f;
            float surfaceTension = 0.035f;
            float yieldStress = 0.0f;

            if (paintFluidSystem == null)
                return new MpmCanvasPaintMaterialSample(density, viscosity, surfaceTension, yieldStress);

            PaintMaterialConfig material = paintFluidSystem.MaterialConfig;
            if (material != null)
            {
                density = Mathf.Max(material.densityKgPerM3, 1.0f);
                viscosity = Mathf.Max(material.EvaluateViscosity(1.0f, 20.0f), 0.0001f);
                surfaceTension = Mathf.Max(material.surfaceTensionNPerM, 0.0001f);
                yieldStress = Mathf.Max(material.yieldStressPa, 0.0f);
            }

            GpuMpmSolverConfig gpuConfig = paintFluidSystem.GpuMpmConfig;
            if (gpuConfig != null && gpuConfig.enablePaintRheology)
            {
                viscosity = Mathf.Max(viscosity, gpuConfig.lowShearViscosity);
                yieldStress = Mathf.Max(yieldStress, gpuConfig.yieldStress);
            }

            return new MpmCanvasPaintMaterialSample(density, viscosity, surfaceTension, yieldStress);
        }
    }
}
