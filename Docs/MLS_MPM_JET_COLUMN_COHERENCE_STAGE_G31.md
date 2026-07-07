# G31 — Coherent Ballistic Jet Column

## Problem

After the short G19A MLS-MPM jet collar ends, an emitted particle becomes a
free-ballistic `Airborne` point advanced only by gravity and an isotropic air
drag (`KStepAirborneParticles`). It keeps whatever cross-stream velocity spread
it carried at the outlet — from APIC affine velocity, wall collisions, and the
divergence of the emission — and that spread grows over the fall. The result is
that the stream fans out into a sparse spray a short distance below the hole,
regardless of the selected paint. There was no mechanism that (a) held the
falling stream together over a longer distance, or (b) made the coherence
distance depend on the material (thin paint should atomise into droplets sooner;
heavy body paint should fall as a connected rope).

## Model

G31 adds a **coherent ballistic jet column**: a per-particle collimation applied
to air-domain particles, with no neighbour search and no per-particle emission
anchor. Each airborne particle re-derives its owning hole every substep from the
current (moving) bucket transform, so the model follows a swinging bucket.

For each hole the world-space emitter is reconstructed from the existing hole
buffers:

- origin `E = BucketLocalPositionToWorld(holeCenterLocal)`
- axis `dir = normalize(TransformVector(_BucketLocalToWorld, holeNormalLocal))`

The particle binds to the emitter with the smallest cross-stream distance among
holes it is ahead of and still within the coherence band of
(`0 <= axial <= L`). It is then pulled toward the **gravity-bent ballistic
centerline** launched from that emitter:

```text
tof        = axial / max(axialSpeed, minAxialSpeed)   // estimated time-of-flight
centerline = E + dir*axial + 0.5 * gravityPerp * tof^2
tangent    = normalize(dir*axialSpeed + gravity*tof)  // true local flight direction
```

Because the transverse frame is taken relative to the gravity-bent `tangent`
(not the fixed exit normal), the model collimates the spray **without fighting
the natural bending and stretching of the column under gravity**.

Two corrections, both weighted by a coherence factor `w = saturate(1 - axial/L)`
that fades from 1 at the hole to 0 at the coherence length `L`, and both bounded
by a per-substep clamp:

1. **Anti-spray** — damp the velocity component perpendicular to `tangent`. This
   is the dominant term; it removes the fan-out.
2. **Re-collimation** — a mild spring pulling the transverse position offset back
   toward the centerline, re-tightening a stream that has already spread.

Past the coherence length (`w = 0`) particles are left as free ballistic points,
so the far field disperses into droplets/spray naturally. A short `L` therefore
reads as early droplet break-up; a long `L` reads as a coherent rope reaching the
board.

Drag is made **coherence-aware**: inside a tight column air exposure is far lower
than for dispersed droplets, so drag is scaled by
`lerp(1, jetColumnDragScale, w)`. A coherent column keeps its momentum and lands
with realistic force; off-axis spray decelerates at the full airborne drag rate.

The whole pass runs only over the small air-domain particle set and only when the
airborne dispatch is active (open holes / existing airborne particles). Sealed,
resting, and in-bucket behaviour are untouched.

## Material coupling

The coherence length and collimation strength are derived from the **authoritative
`PaintMaterialConfig`** (per its own note that GPU-solver presets are numerical
packages, not the visual material source), bound in
`GpuMpmDenseLocalSolver.SetJetColumnCoherenceParameters`. Coherence resists
Rayleigh break-up, so it grows with low-shear viscosity and surface tension, with
a mild yield-stress bonus, normalised around a latex reference:

```text
lengthFactor = sqrt(viscosity/2.5) * sqrt(surfaceTension/0.035)
             * (1 + clamp01(yield/0.35) * 0.5)
L            = clamp(0.35 * lengthFactor, minMeters, maxMeters)
```

`viscosity` is sampled from the material's Carreau–Yasuda curve at a low shear
rate (`0.5 s^-1`), where the presets separate cleanly (water ≈ 0.001, thin ≈ 0.9,
latex ≈ 2.5, thick ≈ 3.8, heavy ≈ 5.0 Pa·s) — the high-shear regime collapses the
presets together and is a poor coherence signal.

Approximate resulting column length by material preset (before min/max clamps):

| PaintMaterialConfig preset | low-shear μ (Pa·s) | ~coherence length | reads as |
|---|---:|---:|---|
| WaterLike | 0.001 | ~0.06 m (min clamp) | immediate spray/mist |
| ThinPaint | 0.9 | ~0.21 m | short jet, early droplets |
| LatexPaint | 2.5 | ~0.35 m | connected stream |
| ThickPaint | 3.8 | ~0.45 m | long coherent stream |
| HeavyBodyPaint | 5.0 | ~0.55 m | coherent falling rope |

When `jetCoherenceMaterialScaling` is off (or `usePaintMaterialConfigRheology` is
off), the inspector base values on `GpuMpmSolverConfig` are used directly; the GPU
material presets also seed sensible per-material base values for that path.

## Exit → fall → impact

- **Exit**: the emitter axis is the hole's outward normal — the same direction the
  exit-velocity boost uses — so the column is collimated in the true throw
  direction from the first substep, including while the bucket swings.
- **Fall**: coherence is maintained over the material-dependent length; gravity
  bends and stretches the column; coherence-aware drag preserves momentum in the
  tight column and decelerates dispersed spray.
- **Impact**: the deposition subsystem
  (`PaintDepositor.DispatchFromMlsMpmBuffers` + `SurfaceImpact.compute`) already
  consumes the air-domain MLS-MPM particles and splashes them onto the paint film
  using the material rheology. G31 does not change that path; it improves the
  *stream it receives* — a coherent, material-consistent column deposits as a
  connected stroke with a focused splash, instead of scattered specks. Full
  film-side spread/splash-crown tuning remains future canvas work.

## Diagnostics

Two GPU diagnostic slots were added (`MPM_DIAGNOSTIC_COUNT` 33 → 35):

- `DIAG_JET_COLUMN_PARTICLES` — particles collimated this substep.
- `DIAG_JET_COLUMN_SPREAD_SUM` — summed transverse offset (fixed-point metres),
  read back as `gpuJetColumnAverageRadiusMeters` (mean column radius). This is the
  quantitative coherence/impact-tightness metric: a smaller radius over the same
  fall distance means a tighter column.

New `FluidSolverStats` fields: `gpuJetColumnCoherenceEnabled`,
`gpuJetCoherenceLengthMeters`, `gpuJetColumnParticleCount`,
`gpuJetColumnAverageRadiusMeters`. These are appended to the validation report
string (`jetColumnCoherence`, `jetCoherenceLengthM`, `jetColumn`,
`maxObservedJetColumn`, `jetColumnAvgRadiusM`).

## Validation switches

- `-paintValidationEnableJetColumnCoherence` / `-paintValidationDisableJetColumnCoherence`
- `-paintValidationEnableJetCoherenceMaterialScaling` / `-paintValidationDisableJetCoherenceMaterialScaling`
- `-paintValidationJetCoherenceLength <meters>`
- `-paintValidationJetTransverseDamping <perSecond>`
- `-paintValidationJetCenterlineAttraction <perSecond>`
- `-paintValidationJetColumnDragScale <0..1>`
- `-paintValidationMaxJetColumnCorrection <mPerSubstepClamp>`

When jet-column coherence is enabled, the outflow validator now also fails if no
airborne particle ever enters the coherent column (mirrors the G19A collar
assertion).

## Validation plan

To be recorded on the target D3D11 / NVIDIA MX110 machine (the GPU numbers cannot
be produced on the authoring machine). Suggested A/B over the outflow scenario:

```text
# Baseline (coherence off) vs G31 (coherence on), open holes, ~130k particles:
-batchmode -executeMethod PaintBucketSim.Editor.SimulationValidationRunner.RunBatch \
  -paintValidationOutflow -paintValidationSteps 120 -paintValidationTargetParticles 130000 \
  -paintValidationGpuStageProfile -paintValidationDisableJetColumnCoherence

-batchmode -executeMethod PaintBucketSim.Editor.SimulationValidationRunner.RunBatch \
  -paintValidationOutflow -paintValidationSteps 120 -paintValidationTargetParticles 130000 \
  -paintValidationGpuStageProfile -paintValidationEnableJetColumnCoherence

# Material sweep: repeat with -paintValidationMaterialPreset 1..5 and compare
# jetCoherenceLengthM and jetColumnAvgRadiusM.
```

Metrics to capture and the pass criteria:

| Metric | Expectation |
|---|---|
| `gpuProfileParticlePostMs` on/off | small delta (collimation is O(holes) over air-domain particles only) |
| `jetColumnAvgRadiusM` on vs off | materially lower with coherence on, over the same fall |
| `jetCoherenceLengthM` across presets | increases water → heavy body, matching the table above |
| NaN/Inf, lost particles | 0 (clamped correction must not inject bursts) |
| sealed-bucket regression | unchanged (kernel not dispatched without outflow) |

## Not in scope

- Explicit droplet/secondary-particle spawning at break-up (the free-ballistic far
  field already disperses; true atomisation is a later stage).
- Film-side splash-crown / spread response to impact momentum (canvas work).
- Any change to the in-bucket solver, collar (G19A), or pressure path.
