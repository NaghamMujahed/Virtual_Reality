using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum GpuParticleVisualMode
    {
        OctahedronMesh = 0,
        CameraFacingSplat = 1
    }

    /// <summary>
    /// Selects how the GPU fluid particles are visualized. Particles renders the
    /// discrete splats/meshes; ScreenSpaceFluid renders a continuous liquid
    /// surface using screen-space depth smoothing + thickness (the MLS-MPM ocean
    /// technique).
    /// </summary>
    public enum FluidRenderMode
    {
        Particles = 0,
        ScreenSpaceFluid = 1
    }

    [CreateAssetMenu(
        fileName = "GpuParticleRenderConfig",
        menuName = "Paint Bucket Sim/GPU Particle Render Config")]
    public class GpuParticleRenderConfig : ScriptableObject
    {
        [Header("General")]
        public bool enableGpuIndirectRendering = true;

        [Tooltip("Particles = discrete splats/meshes. ScreenSpaceFluid = continuous liquid surface via screen-space depth smoothing + thickness. This is the enable/disable switch for realistic fluid rendering.")]
        public FluidRenderMode fluidRenderMode = FluidRenderMode.Particles;

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

        [Range(0.0f, 0.75f)]
        [Tooltip("Softens the visual edge of camera-facing splats without changing physical particle radius.")]
        public float splatEdgeSoftness = 0.22f;

        [Range(0.0f, 1.0f)]
        [Tooltip("Simple specular highlight strength for paint splats.")]
        public float paintSpecularStrength = 0.18f;

        [Range(0.0f, 1.0f)]
        [Tooltip("Rim/fresnel tint strength. Helps the particle cloud read as a glossy fluid surface.")]
        public float paintFresnelStrength = 0.10f;

        public bool usePerParticleColor = true;

        public Color fallbackColor = new Color(0.1f, 0.35f, 1.0f, 1.0f);

        [Header("Screen-Space Fluid Surface")]
        [Tooltip("Particle sphere radius multiplier used only by the screen-space fluid pass. Larger values fuse neighbouring particles into a smoother, gap-free surface.")]
        [Range(0.25f, 4.0f)]
        public float fluidParticleScale = 1.6f;

        [Tooltip("Number of separable bilateral smoothing passes applied to the fluid depth. More passes give a smoother surface at some cost.")]
        [Range(0, 8)]
        public int fluidSmoothingIterations = 3;

        [Tooltip("Bilateral blur radius in screen texels (taps per side, clamped to 16). Larger reaches further across the surface for a smoother look.")]
        [Range(0.5f, 24.0f)]
        public float fluidBlurRadiusPixels = 8.0f;

        [Tooltip("Bilateral depth falloff. Higher values preserve silhouette edges more strongly (less bleeding across depth discontinuities).")]
        [Min(0.0f)]
        public float fluidBlurDepthFalloff = 12.0f;

        [Tooltip("Downsample factor for the fluid depth/thickness targets. 1 = full resolution, 2 = half (faster, slightly softer).")]
        [Range(1, 4)]
        public int fluidResolutionDivisor = 1;

        [Tooltip("Thickness accumulated per particle. Drives how quickly the fluid becomes opaque/absorbing along the view ray.")]
        [Range(0.0f, 4.0f)]
        public float fluidThicknessPerParticle = 0.55f;

        [Tooltip("Tint the fluid takes on from its own body (deep colour). Used with per-particle colour and absorption.")]
        [ColorUsage(false, false)]
        public Color fluidDeepColor = new Color(0.06f, 0.22f, 0.46f, 1.0f);

        [Tooltip("Beer-Lambert absorption strength: how strongly thickness darkens/tints transmitted light. 0 = clear, high = quickly opaque.")]
        [Range(0.0f, 8.0f)]
        public float fluidAbsorption = 2.4f;

        [Tooltip("Overall surface opacity. 0 = fully physically-based (thin edges see-through via refraction); 1 = solid paint colour everywhere regardless of depth. Raise this if the liquid looks too transparent.")]
        [Range(0.0f, 1.0f)]
        public float fluidOpacity = 0.7f;

        [Tooltip("Flip the fluid surface vertically. Screen-space fluid targets can be stored upside-down relative to the scene depending on the graphics API; toggle this if the liquid appears mirrored/upside-down.")]
        public bool fluidFlipVertical = true;

        [Tooltip("When on, the fluid surface is tinted by the average per-particle colour (paint pigment). Off uses the deep colour only.")]
        public bool fluidUseParticleColor = true;

        [Tooltip("Screen-space refraction distortion strength (how much the scene behind the fluid is bent by the surface normal).")]
        [Range(0.0f, 0.15f)]
        public float fluidRefractionStrength = 0.035f;

        [Tooltip("Index-of-refraction-like Fresnel base reflectance at normal incidence (F0). Water ~0.02.")]
        [Range(0.0f, 0.2f)]
        public float fluidFresnelF0 = 0.02f;

        [Tooltip("Environment/reflection-probe reflection strength on the fluid surface.")]
        [Range(0.0f, 1.0f)]
        public float fluidReflectionStrength = 0.6f;

        [Tooltip("Specular highlight strength on the fluid surface.")]
        [Range(0.0f, 4.0f)]
        public float fluidSpecularStrength = 1.4f;

        [Tooltip("Specular highlight sharpness (higher = tighter highlight).")]
        [Range(8.0f, 512.0f)]
        public float fluidSpecularPower = 180.0f;

        [Tooltip("Main light direction used for the fluid specular/diffuse shading, if no scene directional light is bound.")]
        public Vector3 fluidLightDirection = new Vector3(0.35f, 0.85f, 0.25f);

        [Header("State Filtering")]
        [Tooltip("Hide particles that have already been deposited/absorbed on the canvas, or marked lost/inactive.")]
        public bool hideCanvasAndLostParticles = true;

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
            splatEdgeSoftness = Mathf.Clamp01(splatEdgeSoftness);
            paintSpecularStrength = Mathf.Clamp01(paintSpecularStrength);
            paintFresnelStrength = Mathf.Clamp01(paintFresnelStrength);

            worldBoundsSize.x = Mathf.Max(0.1f, worldBoundsSize.x);
            worldBoundsSize.y = Mathf.Max(0.1f, worldBoundsSize.y);
            worldBoundsSize.z = Mathf.Max(0.1f, worldBoundsSize.z);

            fluidParticleScale = Mathf.Clamp(fluidParticleScale, 0.25f, 4.0f);
            fluidSmoothingIterations = Mathf.Clamp(fluidSmoothingIterations, 0, 8);
            fluidBlurRadiusPixels = Mathf.Clamp(fluidBlurRadiusPixels, 0.5f, 24.0f);
            fluidBlurDepthFalloff = Mathf.Max(0.0f, fluidBlurDepthFalloff);
            fluidResolutionDivisor = Mathf.Clamp(fluidResolutionDivisor, 1, 4);
            fluidThicknessPerParticle =
                Mathf.Clamp(fluidThicknessPerParticle, 0.0f, 4.0f);
            fluidAbsorption = Mathf.Clamp(fluidAbsorption, 0.0f, 8.0f);
            fluidOpacity = Mathf.Clamp01(fluidOpacity);
            fluidRefractionStrength =
                Mathf.Clamp(fluidRefractionStrength, 0.0f, 0.15f);
            fluidFresnelF0 = Mathf.Clamp(fluidFresnelF0, 0.0f, 0.2f);
            fluidReflectionStrength =
                Mathf.Clamp01(fluidReflectionStrength);
            fluidSpecularStrength =
                Mathf.Clamp(fluidSpecularStrength, 0.0f, 4.0f);
            fluidSpecularPower =
                Mathf.Clamp(fluidSpecularPower, 8.0f, 512.0f);
            if (fluidLightDirection.sqrMagnitude < 1e-4f)
                fluidLightDirection = new Vector3(0.35f, 0.85f, 0.25f);
        }
    }
}
