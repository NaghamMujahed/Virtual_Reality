using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum GpuParticleVisualMode
    {
        OctahedronMesh = 0,
        CameraFacingSplat = 1
    }

    [CreateAssetMenu(
        fileName = "GpuParticleRenderConfig",
        menuName = "Paint Bucket Sim/GPU Particle Render Config")]
    public class GpuParticleRenderConfig : ScriptableObject
    {
        [Header("General")]
        public bool enableGpuIndirectRendering = true;

        [Tooltip("Maximum number of particles uploaded to GPU renderer.")]
        [Min(1)]
        public int maxRenderedParticles = 50000;

        [Tooltip("Draw every Nth particle. 1 means draw all selected particles.")]
        [Min(1)]
        public int renderStride = 1;

        [Tooltip("Upload particle buffers every N frames. 1 means every frame.")]
        [Min(1)]
        public int uploadEveryNFrames = 1;

        [Header("Visual")]
        public GpuParticleVisualMode visualMode = GpuParticleVisualMode.CameraFacingSplat;

        [Min(0.001f)]
        [Tooltip("Visual-only particle scale. Values above 1 help adjacent particles read as a connected liquid surface.")]
        public float visualRadiusScale = 1.18f;

        [Range(0.05f, 2.0f)]
        [Tooltip("Only used by CameraFacingSplat. Higher values make each splat shade more like a rounded droplet.")]
        public float splatNormalStrength = 0.85f;

        public bool usePerParticleColor = true;

        public Color fallbackColor = new Color(0.1f, 0.35f, 1.0f, 1.0f);

        [Header("Bounds / Culling")]
        [Tooltip("RenderMeshIndirect culls the whole batch using this world bounds.")]
        public Vector3 worldBoundsCenter = Vector3.zero;

        public Vector3 worldBoundsSize = new Vector3(20, 20, 20);

        [Header("Debug")]
        public bool showWarnings = true;

        private void OnValidate()
        {
            if (maxRenderedParticles < 1)
                maxRenderedParticles = 1;

            if (maxRenderedParticles > 2000000)
                maxRenderedParticles = 2000000;

            if (renderStride < 1)
                renderStride = 1;

            if (uploadEveryNFrames < 1)
                uploadEveryNFrames = 1;

            if (visualRadiusScale < 0.001f)
                visualRadiusScale = 0.001f;

            splatNormalStrength = Mathf.Clamp(splatNormalStrength, 0.05f, 2.0f);

            worldBoundsSize.x = Mathf.Max(0.1f, worldBoundsSize.x);
            worldBoundsSize.y = Mathf.Max(0.1f, worldBoundsSize.y);
            worldBoundsSize.z = Mathf.Max(0.1f, worldBoundsSize.z);
        }
    }
}
