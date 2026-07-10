using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum FluidInitializationMode
    {
        TargetParticleCount = 0,
        ManualSpacing = 1
    }

    public enum FluidColorCompartmentAxis
    {
        BucketLocalX = 0,
        BucketLocalZ = 1,
        RadialWedges = 2
    }

    [CreateAssetMenu(
        fileName = "PaintFluidConfig",
        menuName = "Paint Bucket Sim/Paint Fluid Config")]
    public class PaintFluidConfig : ScriptableObject
    {
        private const float ParticleCapacityHeadroomMultiplier = 1.5f;

        [Header("Initialization")]
        public FluidInitializationMode initializationMode = FluidInitializationMode.TargetParticleCount;

        [Tooltip("Target number of particles. Actual count may differ slightly because particles are placed on a lattice.")]
        [Min(1)]
        public int targetParticleCount = 5000;

        [Tooltip("Manual spacing used only when Initialization Mode = ManualSpacing.")]
        [Min(0.005f)]
        public float manualParticleSpacingMeters = 0.035f;

        [Tooltip("Fill fraction of the bucket interior height.")]
        [Range(0.01f, 0.95f)]
        public float fillFraction01 = 0.5f;

        [Tooltip("Particle radius relative to spacing.")]
        [Range(0.25f, 0.65f)]
        public float particleRadiusToSpacing = 0.45f;

        [Tooltip("Extra clearance from walls and bottom.")]
        [Min(0.0f)]
        public float wallClearanceMeters = 0.01f;

        [Tooltip("Extra clearance around the bottom hole.")]
        [Min(0.0f)]
        public float holeClearanceMeters = 0.02f;

        [Header("Multi-Color Compartments")]
        [Tooltip("Initialize the bucket with multiple paint colors separated into virtual compartments.")]
        public bool enableColorCompartments = false;

        [Tooltip("How many color compartments to create from the selected color list.")]
        [Range(1, 8)]
        public int colorCompartmentCount = 2;

        [Tooltip("How the bucket is divided into color compartments.")]
        public FluidColorCompartmentAxis colorCompartmentAxis =
            FluidColorCompartmentAxis.BucketLocalX;

        [Tooltip("Optional empty separator gap around compartment borders, as a fraction of one compartment width.")]
        [Range(0.0f, 0.35f)]
        public float colorDividerGapFraction = 0.04f;

        [Tooltip("Colors used by the compartments. If fewer colors are provided than compartments, colors repeat.")]
        public Color[] compartmentColors =
        {
            new Color(0.1f, 0.35f, 1.0f, 1.0f),
            new Color(1.0f, 0.12f, 0.08f, 1.0f)
        };

        [Header("Physical Color Dividers")]
        [Tooltip("Add actual GPU collision planes between color compartments. This keeps paint regions separated while the bucket swings.")]
        public bool enablePhysicalColorDividers = true;

        [Tooltip("Physical thickness of each internal divider wall in bucket-local meters.")]
        [Min(0.0f)]
        public float colorDividerThicknessMeters = 0.012f;

        [Tooltip("Use physical divider thickness when removing initial particles from divider volume.")]
        public bool carveInitialParticlesAroundPhysicalDividers = true;

        [Header("Rendering")]
        public bool renderParticles = true;

        [Min(0.001f)]
        public float visualParticleSizeScale = 1.0f;

        [Header("Render LOD")]
        public bool enableRenderLod = true;

        [Tooltip("Maximum number of particles drawn by the debug renderer.")]
        [Min(100)]
        public int maxRenderedParticles = 3000;

        [Tooltip("Draw every Nth particle. 1 means draw all particles.")]
        [Min(1)]
        public int renderStride = 1;

        public int ParticleCapacity =>
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Max(1, targetParticleCount) *
                    ParticleCapacityHeadroomMultiplier
                )
            );

        public int GetRenderParticleBudget(int availableParticleCount)
        {
            int available = Mathf.Max(0, availableParticleCount);
            if (available == 0)
                return 0;

            if (!enableRenderLod)
                return available;

            return Mathf.Min(available, Mathf.Max(1, maxRenderedParticles));
        }

        public int GetRenderStride(int availableParticleCount)
        {
            int stride = Mathf.Max(1, renderStride);

            if (!enableRenderLod)
                return stride;

            int available = Mathf.Max(0, availableParticleCount);
            int budget = Mathf.Max(1, maxRenderedParticles);
            if (available > budget)
                stride = Mathf.Max(stride, Mathf.CeilToInt(available / (float)budget));

            return stride;
        }

        private void OnValidate()
        {
            if (targetParticleCount < 1)
                targetParticleCount = 1;

            if (manualParticleSpacingMeters < 0.005f)
                manualParticleSpacingMeters = 0.005f;

            fillFraction01 = Mathf.Clamp(fillFraction01, 0.01f, 0.95f);

            if (wallClearanceMeters < 0.0f)
                wallClearanceMeters = 0.0f;

            if (colorCompartmentCount < 1)
                colorCompartmentCount = 1;

            if (colorDividerThicknessMeters < 0.0f)
                colorDividerThicknessMeters = 0.0f;

            if (compartmentColors == null || compartmentColors.Length == 0)
            {
                compartmentColors = new[]
                {
                    new Color(0.1f, 0.35f, 1.0f, 1.0f),
                    new Color(1.0f, 0.12f, 0.08f, 1.0f)
                };
            }

            if (maxRenderedParticles < 100)
                maxRenderedParticles = 100;

            if (renderStride < 1)
                renderStride = 1;
        }
    }
}
