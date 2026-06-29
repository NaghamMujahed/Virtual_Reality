# G22 Professional Paint Rheology and Jet Cohesion

## Goal

This stage turns the paint material from scattered tuning values into a controlled material system:

- Carreau-Yasuda shear-thinning viscosity.
- Regularized yield stress for thick/heavy materials.
- Surface/free-surface cohesion.
- Extra mild cohesion for NearHole/Jet particles while they are still inside the MLS-MPM jet collar.
- Selectable material presets.
- Validation logs for each preset so tuning is repeatable instead of visual guesswork.

## What changed

### Carreau-Yasuda viscosity

The GPU solver now evaluates:

```text
mu = muInf + (mu0 - muInf) * (1 + (lambda * gammaDot)^a)^((n - 1) / a)
```

Where:

- `mu0` = low-shear viscosity.
- `muInf` = high-shear viscosity.
- `lambda` = shear-thinning relaxation time.
- `n` = power/flow index.
- `a` = Carreau-Yasuda transition sharpness.

`PaintMaterialConfig` was also updated to use the same Carreau-Yasuda form.

### Regularized yield stress

Yield viscosity is now regularized with a Papanastasiou-style ramp:

```text
muYield = tauY * (1 - exp(-m * gammaDot)) / gammaDot
```

This avoids the harsh `tauY / gammaDot` singularity at very low shear and keeps thick materials stable.

### Jet cohesion

The free-surface polish now has an optional jet-specific boost for:

- `NearHole`
- `Jet` particles still coupled through the MLS-MPM collar

This is deliberately mild by default. Strong jet cohesion looked tempting visually but increased compression/divergence in outflow validation.

Latex default adopted:

- `jetCohesionAcceleration = 0.25`
- `maxJetCohesionVelocityCorrection = 0.12`

## Material presets

`GpuMpmSolverConfig` now has:

- `paintMaterialPreset`
- `autoApplyPaintMaterialPreset`

Presets:

| Preset | Intended use | mu0 | muInf | n | a | Yield |
|---|---:|---:|---:|---:|---:|---:|
| WaterLike | low-viscosity reference | 0.02 | 0.01 | 1.00 | 2.0 | 0.00 |
| ThinPaint | runny paint | 1.00 | 0.08 | 0.68 | 2.0 | 0.10 |
| LatexPaint | default balanced paint | 2.60 | 0.14 | 0.55 | 2.2 | 0.00 |
| ThickPaint | thicker brush-like paint | 4.00 | 0.18 | 0.48 | 2.4 | 0.25 |
| HeavyBodyPaint | heavy acrylic-like paint | 5.50 | 0.25 | 0.42 | 2.6 | 0.35 |

The original aggressive Thick/Heavy presets were rejected because projection residual increased instead of decreasing. The adopted versions keep the visual trend of thicker materials without destabilizing the solver.

## Validation

All selected G22 validation logs passed with:

- `nanInf=0`
- `lost=0`
- no shader errors
- no C# compile errors

### Sealed-bucket preset validation

All presets were validated with:

```text
-paintValidationProjection
-paintValidationSealBucket
-paintValidationTargetParticles 112494
-paintValidationSimulationSubsteps 2
-paintValidationPressureSolveMode 1
-paintValidationRedBlackSorIterations 4
```

Results:

| Log | Preset | Result | jClamps | divBefore | divAfter |
|---|---|---:|---:|---:|---:|
| `g22_preset_1_112k_20.log` | WaterLike | PASS | 0 | 10.49343 | 0.93087 |
| `g22_preset_2_112k_20.log` | ThinPaint | PASS | 0 | 10.23306 | 1.08768 |
| `g22_latex_default_final_112k_20.log` | LatexPaint | PASS | 0 | 10.21871 | 1.08472 |
| `g22_preset_4_final_112k_20.log` | ThickPaint | PASS | 0 | 9.11826 | 1.13551 |
| `g22_preset_5_final_112k_20.log` | HeavyBodyPaint | PASS | 0 | 8.79306 | 1.21784 |

### Latex outflow / jet cohesion validation

Validated with:

```text
-paintValidationProjection
-paintValidationOutflow
-paintValidationTargetParticles 112494
-paintValidationSimulationSubsteps 2
-paintValidationPressureSolveMode 1
-paintValidationRedBlackSorIterations 4
-paintValidationMaterialPreset 3
```

| Log | Jet cohesion | outflow | jet/collar | airborne | jClamps | speedMax | divAfter |
|---|---:|---:|---:|---:|---:|---:|---:|
| `g22_latex_outflow_jetcohesion_off_112k_90.log` | off | 2087 | 822 | 9029 | 8786 | 2.09 | 21.63762 |
| `g22_latex_outflow_jetcohesion_mild_112k_90.log` | 0.25 | 2081 | 868 | 8997 | 8456 | 1.97 | 21.84464 |
| `g22_latex_outflow_jetcohesion_on_112k_90.log` | 0.50 | 2057 | 827 | 9182 | 9362 | 1.97 | 22.33819 |

Adopted decision:

- `0.25` is the default for Latex.
- It keeps the outflow stable, slightly reduces J clamps versus off, keeps more particles in the collar, and avoids the extra compression seen with `0.50`.

## Validation CLI flags

New/expanded flags:

- `-paintValidationMaterialPreset <0..5>`
- `-paintValidationMaxStressMagnitude <value>`
- `-paintValidationLowShearViscosity <value>`
- `-paintValidationHighShearViscosity <value>`
- `-paintValidationShearRelaxation <value>`
- `-paintValidationShearPowerN <value>`
- `-paintValidationCarreauYasudaA <value>`
- `-paintValidationYieldStress <value>`
- `-paintValidationYieldRegularization <value>`
- `-paintValidationMaxYieldViscosity <value>`
- `-paintValidationMaxEffectiveViscosity <value>`
- `-paintValidationEnableJetCohesion`
- `-paintValidationDisableJetCohesion`
- `-paintValidationJetCohesion <value>`
- `-paintValidationMaxJetCohesionCorrection <value>`

## Current recommendation

Use `LatexPaint` as the default project material.

Use `ThinPaint` if the jet should feel runnier and less cohesive.

Use `ThickPaint` or `HeavyBodyPaint` when visual thickness is more important, but validate outflow again if the hole size, substeps, grid resolution, or particle count changes substantially.

