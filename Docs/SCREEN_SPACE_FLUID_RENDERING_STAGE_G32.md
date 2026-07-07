# G32 — Screen-Space Fluid Rendering (realistic liquid surface)

## Goal

Add the WebGPU-Ocean style screen-space fluid visualization as a toggleable
alternative to the discrete particle view, reusing the existing MLS-MPM GPU
particle buffers. The switch lives in `GpuParticleRenderConfig`:

```text
GpuParticleRenderConfig.fluidRenderMode = Particles | ScreenSpaceFluid
```

`Particles` keeps the existing splat/mesh renderer. `ScreenSpaceFluid` renders a
continuous, shaded liquid surface.

## Technique

The classic screen-space fluid pipeline (Simon Green / van der Laan), the same
one WebGPU-Ocean uses:

1. **Sphere-imposter depth** — each particle is drawn as a camera-facing sphere;
   the fragment writes the nearest-surface *linear eye depth*. A `Min` blend keeps
   the closest surface, so no hardware depth buffer / `SV_Depth` is needed (immune
   to reversed-Z target differences).
2. **Bilateral depth smoothing** — a separable edge-preserving blur turns the
   bumpy sphere depth into a smooth surface while keeping silhouettes crisp.
3. **Thickness** — particles are additively accumulated into a thickness target
   (also thickness-weighted colour) for absorption and pigment tint.
4. **Composite** — from the smoothed depth the surface normal is reconstructed;
   the surface is shaded with Fresnel, ambient-sky reflection, screen-space
   refraction of the scene behind, Beer-Lambert absorption from thickness, and a
   specular highlight, then blended over the scene.

## Why this architecture (URP 17 / RenderGraph, no RendererFeature)

The project's URP (17.4, Unity 6) uses RenderGraph and has no ScriptableRenderer
features. To stay robust and self-contained (config-only control, no renderer-asset
surgery):

- Passes 1–3 render into the renderer's own `RenderTexture`s via a `CommandBuffer`
  executed with `Graphics.ExecuteCommandBuffer` inside `beginCameraRendering`.
  These touch only private targets, so they are independent of RenderGraph.
- Pass 4 is a **transparent fullscreen quad** (`MeshRenderer`, queue `Transparent`)
  drawn by URP's normal transparent pass. That is what lets it sample
  `_CameraOpaqueTexture` (refraction) and `_CameraDepthTexture` (occlusion) and
  composite over the opaque scene, with zero RenderGraph plumbing.

`GpuParticleIndirectRenderer` auto-attaches `ScreenSpaceFluidRenderer` and injects
the shared references, and skips its own particle draw while
`fluidRenderMode == ScreenSpaceFluid`, so the user only flips the config enum.

## Files

- `Assets/Scripts/PaintBucketSim/Configs/GpuParticleRenderConfig.cs` — `FluidRenderMode`
  enum + `Screen-Space Fluid Surface` parameter block.
- `Assets/Scripts/PaintBucketSim/Systems/Fluid/ScreenSpaceFluidRenderer.cs` — the
  driver (camera callback, off-screen passes, composite quad).
- `Assets/Shaders/PaintBucketSim/FluidParticleImposter.shader` (+ `.hlsl`) —
  depth (Pass 0, Min blend) and thickness (Pass 1, additive) imposter passes.
- `Assets/Shaders/PaintBucketSim/FluidDepthBlur.shader` — separable bilateral blur.
- `Assets/Shaders/PaintBucketSim/FluidComposite.shader` — transparent surface composite.
- `Assets/Scripts/PaintBucketSim/Systems/Fluid/GpuParticleIndirectRenderer.cs` —
  skips the particle draw in fluid mode; auto-attaches the fluid renderer.

## Project requirements

The composite reads URP's opaque + depth textures. Both are enabled on
`Assets/Settings/PC_RPAsset.asset` (already on) and were enabled on
`Assets/Settings/Mobile_RPAsset.asset` by this change. If a different URP asset is
active, enable **Depth Texture** and **Opaque Texture** on it.

## How to use / verify

1. Open `Assets/Scenes/SampleScene.unity`, enter Play mode (particles render as
   before by default).
2. On the `GpuParticleRenderConfig` asset set `Fluid Render Mode = ScreenSpaceFluid`.
   The discrete particles are replaced by a continuous liquid surface. Toggling
   back to `Particles` restores the splats live.
3. Tune on the same config:
   - `Fluid Particle Scale` — raise until the surface is gap-free (start ~1.6).
   - `Fluid Smoothing Iterations` / `Fluid Blur Radius Pixels` / `Fluid Blur Depth
     Falloff` — surface smoothness vs edge sharpness.
   - `Fluid Thickness Per Particle` + `Fluid Absorption` — how quickly the liquid
     becomes opaque/pigment-coloured with depth.
   - `Fluid Refraction Strength`, `Fluid Fresnel F0`, `Fluid Reflection Strength`,
     `Fluid Specular Strength/Power` — glassy-liquid look.
   - `Fluid Use Particle Color` — tint by MLS-MPM pigment vs the flat `Fluid Deep
     Color`.
   - `Fluid Resolution Divisor` — 2 for performance (half-res targets).

## Post-test fixes (first play-test feedback)

The first in-editor test surfaced four issues, all now addressed:

1. **Surface was camera-locked** — the imposter passes relied on `UNITY_MATRIX_V/VP`,
   which are not reliably set when rendering via `Graphics.ExecuteCommandBuffer`
   outside URP's loop. The driver now passes the view and view-projection matrices
   as **explicit uniforms** (`_FluidView`, `_FluidViewProj`); the surface tracks the
   world correctly.
2. **Surface appeared mirrored/upside-down** — the off-screen targets can be stored
   vertically flipped relative to the scene depending on the graphics API. Added a
   `Fluid Flip Vertical` config toggle (default on); the composite samples the fluid
   targets with a flipped V while sampling the scene un-flipped. Toggle it if the
   liquid still looks mirrored.
3. **Not visible in the Scene view** — the renderer now also processes
   `CameraType.SceneView`, so the liquid shows in both Game and Scene views.
4. **Too transparent / wrong colour depth** — added a `Fluid Opacity` control
   (0 = physically-based, thin edges refract the scene; 1 = solid selected colour
   everywhere). Raise it for opaque paint.

Compile-verified via Unity batchmode (no C# or shader errors; shaders import
cleanly). Still pending a **visual** confirmation pass, since that needs an
interactive GPU session — the four fixes above are logic/orientation corrections
that should be checked on screen, and the `Fluid Flip Vertical` toggle is the
first thing to try if orientation is still off.

Falling back to `fluidRenderMode = Particles` (the default) always restores the
known-good particle view, so the feature is safe to leave in.

## Not in scope

- Foam/spray secondary particles and caustics.
- Temporal reprojection / TAA-aware denoise of the depth.
- A ScriptableRendererFeature port (would allow compositing before other
  transparents and per-camera control); the transparent-quad path was chosen for
  robustness under RenderGraph without asset edits.
