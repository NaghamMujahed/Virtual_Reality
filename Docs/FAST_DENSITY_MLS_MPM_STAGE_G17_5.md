# G17.5 — Fast density WC-MLS-MPM

## Decision

The production path now uses a compact two-pass weakly-compressible
MLS-MPM solver:

1. hybrid tiled P2G transfers particle mass, momentum, and APIC affine
   velocity;
2. a second hybrid tiled pass reconstructs particle density from grid mass,
   evaluates the density EOS and paint viscosity, and scatters stress;
3. the active MPM grid is updated, followed by APIC G2P and moving-bucket
   particle collision.

The deformation-J plus pressure-projection path remains available by
disabling `enableReferenceDensityEosMode`. It is retained as an A/B fallback,
not as the default.

## Important initialization correction

The requested particle count and the count that geometrically fit in the
bucket are not always equal. For example, a target of 104,000 produced
89,518 particles. Recalibrating the target paint mass over that smaller count
made the initial grid density about 16% higher than the material rest density.
An EOS then interpreted a valid initial lattice as compression and generated
an artificial pressure pulse.

`ParticleVolumeJ.w`, previously reserved, now stores the ratio between the
particle's represented rest volume and its initial lattice-cell volume. The
EOS uses this to derive a numerical rest density while preserving the
configured physical mass and material density. At 89,518 particles this
changed initial `Jmin` from 0.8456 to 0.9824 and removed 5,771 false initial
bucket corrections.

## Runtime behavior

- projection and deformation-gradient updates are omitted on the fast path;
- adaptive projection/deformation sampling is also omitted because it has no
  consumer in this mode;
- projection bounds, sparse-pressure selection, and projection shader
  parameter setup are skipped as well;
- post-G2P moving-bucket collision is always enabled as a containment safety
  pass;
- holes, jet transition, airborne advection, paint rheology, diagnostics,
  active tiles, and hybrid tiled P2G remain active;
- optional projection fallback remains available for controlled debugging.

The projection and deformation buffers remain allocated so the Inspector mode
can be switched at runtime for A/B testing, but those buffers are not dispatched
or traversed by the fast path.

When lifecycle logging is enabled, the Console prints the effective mode once:

`ExecutionMode=FastDensityEOS, Projection=False, Jacobi=False, Deformation=False, AdaptiveSampling=False`

## Matched GPU measurements

NVIDIA MX110 / D3D11, stage profiling enabled, adaptive cadence disabled for
the A/B measurements:

| Generated particles | Projected F/J path | Fast density path | Reduction |
|---:|---:|---:|---:|
| 89,518 | 18.713 ms | 11.785 ms | 37.0% |
| 157,710 | 26.821 ms | 19.072 ms | 28.9% |

At 263,887 generated particles, the fast path measured 29.585 ms GPU per
substep. It used 13 dispatches versus 34 in the projected path.

The density pass makes P2G more expensive because it requires a second
particle-neighborhood traversal, but removing the projection solve,
deformation update, and their associated grid passes produces the lower total
time.

## Validation

Passing coverage:

- sealed bucket at 89,518 particles for 30 substeps;
- strongly moving bucket for 120 substeps;
- two open holes for 120 substeps;
- 387 outflow transitions, 6,628 jet particles, and 13,687 airborne
  particles;
- 157,710 particles for 60 substeps;
- 263,887 particles for 30 substeps;
- zero NaN/Inf, lost particles, no-grid-support particles, or tile-list
  overflow;
- no D3D11 shader errors or warnings from the new kernels.

The moving-bucket 120-step A/B test measured 11.690 ms for the fast path and
18.056 ms for the projected path. The fast path also required fewer collision
corrections in that test.

## Remaining limits and next stage

This stage removes the largest fixed pressure cost, but the second
density/stress P2G pass is now the dominant cost: 19.952 ms of the 29.585 ms
total at 263,887 particles.

The current development scene requests 150,000 particles, generates about
131,000, and runs four fluid substeps per 60 Hz fixed step. Even a 15–17 ms
solver substep therefore costs roughly 60–68 ms per fixed step before
rendering. This explains why the measured improvement does not yet look like
smooth realtime motion.

Reducing the scene directly to one or two substeps is not a valid shortcut:
the one-substep stress test collapsed numerically, and two substeps produced
large compression and collision counts. Four substeps remain the safe default
until the per-substep P2G cost is reduced.

G18 should therefore optimize the measured bottleneck, not add another
general framework:

- persistent particle ordering or compact cell ranges only if they reduce
  the two P2G neighborhood traversals in an A/B benchmark;
- reuse the owner-tile ordering already built each substep;
- reduce global atomics at tile borders;
- investigate fusing density sampling and stress scatter where hardware
  synchronization permits;
- remove disabled experimental paths after the winning implementation is
  visually accepted in the Unity scene.
