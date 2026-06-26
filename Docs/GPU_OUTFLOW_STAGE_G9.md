# GPU Outflow Stage G9

## Scope

This stage turns bucket holes into real GPU outflow openings:

- Multiple active holes are uploaded from `BucketConfig` to the GPU.
- Supported footprint shapes: circular, square, ellipse, rectangle, and slot.
- Bucket collision allows particles inside an open hole aperture to pass through.
- Projection solid marking leaves the hole channel open, so the pressure solve does not seal the hole.
- Particles crossing past a hole plane transition from MPM-domain states to `Jet`.
- `Jet`/`Airborne` particles are advanced by a lightweight GPU airborne step with gravity and drag.

Canvas collision/deposition is intentionally not part of this stage.

## Validation

Validation was run in the separate project clone:

`C:\Users\HP\demo_unity_validation`

### Outflow validation

Command profile:

- projection enabled
- outflow enabled
- 80 substeps
- 80k target particles
- projection interval 2
- 10 Jacobi iterations

Result:

- PASS
- particles: 63,349
- holes: 2
- holeOpen: true
- outflow transitions: 1,074
- jet particles: 15,323
- airborne particles: 7,585
- lost particles: 0
- NaN/Inf: 0
- no grid support: 0
- divergence average before/after: 15.46738 → 8.72727
- elapsed: 6.084 seconds

### Sealed regression validation

Command profile:

- projection enabled
- bucket shake enabled
- sealed bucket
- 40 substeps
- 80k target particles
- projection interval 2
- 10 Jacobi iterations

Result:

- PASS
- particles: 63,349
- holeOpen: false
- outflow transitions: 0
- jet particles: 0
- airborne particles: 0
- lost particles: 0
- NaN/Inf: 0
- no grid support: 0
- divergence average before/after: 6.75630 → 3.14550

No matching `GpuDenseMpmPrototype` shader warnings/errors were found in the validation logs.
