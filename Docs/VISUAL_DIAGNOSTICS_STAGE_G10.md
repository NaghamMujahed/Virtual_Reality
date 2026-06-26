# G10 Visual Diagnostics and Particle Splat Rendering

## Scope

This stage starts the professional polish track without changing the fluid physics:

- Adds GPU diagnostics for in-bucket liquid height and active particle speed.
- Reports normalized bucket-space fill metrics:
  - `fillAvg01`
  - `fillMin01`
  - `fillMax01`
  - `fillSpan01`
- Reports active MPM particle speed:
  - `speedAvg`
  - `speedMax`
- Adds an optional visual-only `CameraFacingSplat` GPU particle render mode.
- Keeps the physical particle radius unchanged; `visualRadiusScale` only affects rendering.
- Fixes the validation runner so diagnostics waits do not deadlock when diagnostics and projection intervals are not aligned.

## Visual Mode

`GpuParticleRenderConfig.visualMode`:

- `OctahedronMesh`: previous cheap 3D particle mesh.
- `CameraFacingSplat`: cheaper quad impostor with round clipping and dome-like lighting.

The dev render config currently uses:

```text
visualMode        = CameraFacingSplat
visualRadiusScale = 1.18
splatNormalStrength = 0.85
```

## Validation

Validation was run in:

`C:\Users\HP\demo_unity_validation`

### 120k sealed bucket validation

```text
PASS
particles=104436
active=104436
outflow=0
jet=0
airborne=0
lost=0
nanInf=0
jAvg=1.0418
jMin=0.6500
jMax=1.3500
fillAvg01=0.5505
fillMin01=0.0160
fillMax01=0.9760
fillSpan01=0.9600
speedAvg=0.8198
speedMax=2.6600
elapsedSeconds=2.002
msPerSubstep=66.748
```

No shader errors, C# errors, invalid kernels, or line-ending shader warnings were reported in the final validation log.

## Notes

- This is not full surface reconstruction yet.
- The splat renderer is a fast visual continuity improvement, useful before implementing screen-space fluid rendering or metaball/surface reconstruction.
- The new fill/speed diagnostics now let us distinguish visual particle spacing from actual solver expansion, compression, or surface-speed instability.
