# G11 Free Surface Polish and Paint Splat Shading

## Scope

This stage improves the current dense GPU MLS/APIC-MPM path without changing solver architecture:

- Adds a lightweight free-surface polish inside `KG2PVelocityApic`.
- Infers exposed liquid surface normals from the MPM grid mass gradient.
- Damps velocity moving outward from the inferred surface.
- Adds a small inward cohesion acceleration to reduce lightweight flying surface particles.
- Improves the camera-facing splat shader with:
  - softer edge shading,
  - simple glossy specular,
  - small fresnel/rim contribution.
- Adds validation runner flags for A/B tuning:
  - `-paintValidationDisableFreeSurfacePolish`
  - `-paintValidationFreeSurfaceGradientScale`
  - `-paintValidationFreeSurfaceDamping`
  - `-paintValidationFreeSurfaceCohesion`
  - `-paintValidationMaxFreeSurfaceCorrection`

This is not a full physical surface-tension/curvature solve. It is a conservative, GPU-cheap stabilization pass that prepares the project for more realistic surface rendering and later sparse/tiled optimization.

## Dev settings

`GpuMpmSolverConfig_dev`:

```text
enableFreeSurfacePolish = true
freeSurfaceGradientScale = 0.08
freeSurfaceNormalDampingPerSecond = 2.5
freeSurfaceCohesionAcceleration = 0.75
maxFreeSurfaceVelocityCorrection = 0.35
```

`GpuParticleRenderConfig_dev`:

```text
visualMode = CameraFacingSplat
visualRadiusScale = 1.18
splatNormalStrength = 0.85
splatEdgeSoftness = 0.22
paintSpecularStrength = 0.18
paintFresnelStrength = 0.10
```

## Validation

Validation was run in:

`C:\Users\HP\demo_unity_validation`

### A/B sealed bucket, 120k target particles

Free-surface polish disabled:

```text
PASS
particles=104436
active=104436
jAvg=1.0418
fillAvg01=0.5505
fillMax01=0.9760
speedAvg=0.8198
speedMax=2.6600
nanInf=0
lost=0
```

Free-surface polish enabled:

```text
PASS
particles=104436
active=104436
jAvg=1.0442
fillAvg01=0.5514
fillMax01=0.9760
speedAvg=0.8072
speedMax=2.5800
nanInf=0
lost=0
```

Interpretation:

- The pass is stable.
- No particle loss or NaN/Inf appeared.
- Surface-related speed metrics moved in the desired direction.
- The change is intentionally conservative; stronger cohesion can be tuned with the new validation flags if visual testing shows surface particles still look too light.

### Outflow compatibility check

The outflow validation path also passed with free-surface polish enabled:

```text
PASS
particles=68554
holeOpen=True
maxObservedOutflow=3
maxObservedJet=3
maxObservedAirborne=3
nanInf=0
lost=0
```

The current outflow count is low under the present dev hole/test settings, but the transition path remains functional and stable.
