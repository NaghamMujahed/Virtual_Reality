using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum FluidInitializationMode
    {
        TargetParticleCount = 0,
        ManualSpacing = 1
    }

    public enum FluidPreviewMode
    {
        FollowBucketKinematically = 0,
        FixedWorldPositions = 1
    }

    [CreateAssetMenu(
        fileName = "PaintFluidConfig",
        menuName = "Paint Bucket Sim/Paint Fluid Config")]
    public class PaintFluidConfig : ScriptableObject
    {
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

        [Header("Runtime Preview")]
        [Tooltip("Before the real PBF solver is added, particles can follow the bucket for visualization.")]
        public FluidPreviewMode previewMode = FluidPreviewMode.FollowBucketKinematically;

        [Header("Capacity")]
        [Tooltip("Hard capacity for NativeArrays. Keep higher than the target count if you plan to increase particles.")]
        [Min(1)]
        public int maxParticleCapacity = 25000;

        [Header("Rendering")]
        public bool renderParticles = true;

        [Min(0.001f)]
        public float visualParticleSizeScale = 1.0f;

        [Tooltip("Warning threshold for high particle count in current renderer.")]
        [Min(1000)]
        public int highParticleWarningThreshold = 50000;

        [Header("Render LOD")]
        public bool enableRenderLod = true;

        [Tooltip("Maximum number of particles drawn by the debug renderer.")]
        [Min(100)]
        public int maxRenderedParticles = 3000;

        [Tooltip("Draw every Nth particle. 1 means draw all particles.")]
        [Min(1)]
        public int renderStride = 1;

        private void OnValidate()
        {
            if (targetParticleCount < 1)
                targetParticleCount = 1;

            if (maxParticleCapacity < targetParticleCount)
                maxParticleCapacity = targetParticleCount;

            if (manualParticleSpacingMeters < 0.005f)
                manualParticleSpacingMeters = 0.005f;

            fillFraction01 = Mathf.Clamp(fillFraction01, 0.01f, 0.95f);

            if (wallClearanceMeters < 0.0f)
                wallClearanceMeters = 0.0f;

            if (maxRenderedParticles < 100)
                maxRenderedParticles = 100;

            if (renderStride < 1)
                renderStride = 1;
        }
    }
}