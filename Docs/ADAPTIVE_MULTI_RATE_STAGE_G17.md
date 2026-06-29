# G17 — Adaptive multi-rate simulation

## Outcome

G17 adds a conservative activity-driven scheduler around the G16 solver.
Mass transfer remains full-rate: every active MPM particle still contributes
to P2G on every substep.

The stage adds:

- GPU particle activity classification;
- thread-group reduction with one small asynchronous summary readback;
- priority flags for surface, boundary, hole, and fast particles;
- optional lower-rate deformation for calm interior particles;
- adaptive pressure-projection cadence with immediate safety exits;
- runtime and validation statistics for every adaptive decision.

## GPU activity sampling

Every `adaptiveActivitySampleInterval` substeps, the GPU classifies particles
and reduces:

- active, priority, calm-interior, and air-domain counts;
- average and maximum speed;
- average absolute deformation-volume deviation `|J - 1|`.

The reduction performs global atomics once per 256-thread group rather than
once per particle. Only seven integers are read back asynchronously.

Priority particles include:

- boundary and near-hole states;
- particles above the configured speed threshold;
- particles near bucket walls or the bottom;
- particles in the possible free-surface band.

## Adaptive projection

The active pressure interval remains the existing
`projectionSubstepInterval` (2 in the development asset).

The interval can rise to 3 only after sustained calm. It returns immediately
to the active interval when any of these is true:

- bucket linear or angular motion exceeds its threshold;
- holes are open;
- air, jet, or spilled particles exist;
- sampled liquid speed is too high;
- the priority-particle fraction is too high;
- average `|J - 1|` exceeds 0.04.

The `J` guard was added after validation showed that a less conservative
interval of 4 could allow compression to accumulate. The final defaults use:

- calm projection interval: 3;
- calm entry delay: 32 substeps;
- average `|J - 1|` limit: 0.04.

## Calm-interior deformation

The shader and priority buffer support lower-rate deformation updates with
accumulated time scaling. Validation showed that interval 2 changed the
evolution of `J` more than desired, so the production default remains 1.
The feature is retained as an explicit experimental quality control.

## Cost

Synchronized profiling at 104,436 particles measured the activity sample at
about 0.52 ms on its sampling substep. At the default interval of 8, this is
about 0.065 ms per substep on average.

When active, G17 preserves the G16 cadence and physics. When genuinely calm,
projection frequency changes from once per 2 substeps to once per 3
substeps, reducing projection executions by one third during that period.

## Validation

Passing coverage includes:

- active-path A/B comparison with identical physics diagnostics;
- forced calm entry followed by automatic `J`-guard exit;
- 104,436 particles;
- 152,833 generated particles;
- moving bucket with two open holes;
- 287 outflow transitions;
- 3,968 jet particles and 1,290 airborne particles;
- zero NaN/Inf, lost particles, no-grid-support particles, or tile-list
  overflow;
- no final shader errors or shader warnings on D3D11 / NVIDIA MX110.

## Superseded next-stage decision

G17.5 first compared the projected solver against a compact density-EOS
MLS-MPM path. The validated density path is now the default and makes
projection/deformation adaptive sampling unnecessary in production mode.
See `FAST_DENSITY_MLS_MPM_STAGE_G17_5.md`.

Persistent spatial ordering remains a candidate for G18 only if it reduces
the now-measured two-pass P2G bottleneck in an A/B benchmark.
