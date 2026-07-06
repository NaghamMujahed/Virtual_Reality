# Bucket-Local MLS-MPM: Final Pressure Decision

## Final architecture

The in-bucket liquid uses one production path:

1. bucket-local APIC/MLS-MPM transfers;
2. grid-density compression predictor;
3. sparse staggered Red-Black SOR projection;
4. local-to-world transition only at the hole, spill, or jet-collar exit.

The solver no longer contains production fallbacks for particle-density EOS,
F/J deformation pressure, Jacobi pressure, particle-built projection mass, or
moving-world-bucket boundary velocity.

## Why pressure scale zero was rejected

`gridDensityEosPressureScale=0` looked calm, but the 480-substep sealed-bucket
test produced:

- average speed: `0.0003 m/s`;
- average J: `0.8901`;
- thousands of particles at the `J=0.65` clamp.

This was a quiet compressed state, not incompressibility. Sparse SOR controls
instantaneous divergence, while the bounded grid-density predictor restores
long-term density drift.

## Adopted tuning

- `gridDensityEosPressureScale = 2.8`
- `gridDensityEosActivationRatio = 1.01`
- `gridDensityEosMaxVelocityCorrection = 0.20 m/s/substep`
- `pressureRedBlackSorIterations = 10`
- configured SOR omega: `1.75`
- strong bucket-frame acceleration omega cap: `1.65`
- open-hole omega cap: `1.35`
- `velocityDampingPerSecond = 0.15`
- `affineDamping = 0.04`

The EOS activation band suppresses tiny rest-density chatter. The correction
clamp prevents one substep from injecting a visible pressure burst. SOR omega
is reduced for strong non-inertial forcing and open boundaries without
changing pressure models.

## Removed work and memory

- Removed the second 27-node particle density/stress pass.
- Removed three deformation `float4` GPU buffers and their CPU staging arrays.
  This saves 48 bytes per particle: about 18.8 MB of GPU memory and 18.8 MB of
  CPU staging memory at 391k particles.
- Removed Jacobi kernels, dispatch logic, temporary pressure grid, and mode
  switches.
- Removed particle projection-mass accumulation and its integer grid buffer.
- Removed dead world-space active-projection AABB configuration.

## Validation

All listed tests ran on D3D11 / NVIDIA MX110.

| Test | Result | Key measurements |
|---|---|---|
| Sealed resting, 96,776 particles, 480 substeps | PASS | `Javg=0.9905`, drift `0.95%`, speed avg `0.0344`, max `0.22`, no clamps/NaN/lost |
| Bucket shake, 96,776 particles, 120 substeps | PASS | stable collision response, no NaN/lost/no-grid |
| Open holes, 96,776 particles, 80 substeps | PASS | 101 outflow transitions, 358 jet/collar particles, no lost particles |
| High count, 390,894 particles, 30 substeps | PASS | `20.409 ms/substep`, P2G `8.266 ms`, projection `3.566 ms`, no NaN/lost/no-grid |

The previous best recorded 391k result was `20.530 ms/substep`. The final
path is slightly faster (`20.409 ms`) while meeting the sealed-bucket density
and rest-stability criteria.

## Final decision

Keep Bucket-Local grid-density predictor + sparse Red-Black SOR. Delete and do
not restore EOS-only, projection-only, particle-EOS, F/J pressure, or Jacobi
fallbacks.

The automated shake test proves numerical response but uses only about
`0.13 m/s²` frame acceleration. Final visual approval of large-amplitude
sloshing should therefore be performed in the real swinging scene; it is not
honest to infer cinematic wave quality from that mild validation excitation.
