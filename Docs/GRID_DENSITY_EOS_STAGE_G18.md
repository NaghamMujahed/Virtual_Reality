# G18 — Grid-Centric Density EOS

## Outcome

The production fast-density path now supports a grid-centric EOS pressure
update. It removes the second particle density/stress scatter dispatch and
evaluates the pressure gradient once per active grid node.

The retained path keeps:

- MLS/APIC particle-to-grid and grid-to-particle transfers.
- Paint viscosity/rheology stress in the primary P2G pass.
- Per-particle reference-density `J` diagnostics, now accumulated during G2P
  from grid masses already being read.
- Sparse active-tile grid dispatch.
- The previous particle-density EOS as a one-toggle comparison/fallback.

Default development settings:

- `enableReferenceDensityEosMode = true`
- `enableGridDensityEos = true`
- `gridDensityEosPressureScale = 5`
- `gridDensityEosMaxVelocityCorrection = 1.5 m/s/substep`
- `bulkModulus = 1000`
- `maxStressMagnitude = 8000`
- simulation substeps = `2`

## Measured results (D3D11)

Sealed bucket, user material settings, two simulation substeps:

| Case | Particle EOS | Grid EOS | GPU reduction |
|---|---:|---:|---:|
| ~131k, 60 solver steps | 16.028 ms | 11.061 ms | 31.0% |
| ~264k, 40 solver steps | 30.611 ms | 20.158 ms | 34.1% |
| ~131k, shake, 120 steps | 17.048 ms | 13.030 ms | 23.6% |

The 120-step shake test completed with all particles active, zero lost
particles, and zero NaN/Inf diagnostics. At that aggressive motion,
grid EOS ended at `J=0.7756` versus `J=0.7515` for particle EOS. Both modes
remain weakly compressible; grid EOS did not introduce that limitation.
An additional ~264k/60-step grid run completed at 21.289 ms with
`J=0.9275` and zero NaN/Inf diagnostics.

## Rejected experiments

The following prototypes were removed rather than left as disabled code:

- Direct owner-tile bins: dispatch reduction without meaningful GPU-time gain.
- Lagged particle-density stress: fast but physically divergent.
- Group-shared mass cache: identical physics, but only noise-level total gain.

## Validation switches

- `-paintValidationEnableGridDensityEos`
- `-paintValidationDisableGridDensityEos`
- `-paintValidationGridDensityEosPressureScale <value>`
- `-paintValidationGridDensityEosMaxCorrection <value>`

The sealed validation also fails if fewer than 95% of particles remain active,
preventing a disappearing-fluid run from reporting a false pass.
