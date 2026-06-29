# G19A — MLS-MPM Jet Collar

## Outcome

Particles that cross an open bucket hole no longer leave MLS-MPM
immediately. A short, bounded collar keeps `Jet` particles coupled to the
same tiled APIC/MLS-MPM grid used by the in-bucket paint.

The collar ends when any of these conditions is reached:

- configured collar duration;
- configured axial distance from the emitting hole;
- radial departure from the hole footprint plus collar padding;
- loss of valid MPM grid support.

After that, the particle transitions to the existing ballistic `Airborne`
path. Bucket collision remains restricted to in-bucket states, preventing a
post-G2P collision pass from resetting Jet age.

Development defaults:

- `enableJetMpmCollar = true`
- `jetMpmCollarDurationSeconds = 0.05`
- `jetMpmCollarMaxDistanceMeters = 0.06`
- `jetMpmCollarRadialPaddingMeters = 0.02`

## Validation

~130k particles, two simulation substeps, 120 solver steps, open hole:

| Metric | Immediate ballistic | G19A collar |
|---|---:|---:|
| GPU total | 9.044 ms | 9.722 ms |
| Active MLS-MPM particles | 92,112 | 112,622 |
| Airborne particles | 34,726 | 18,234 |
| Maximum collar particles | 0 | 1,175 |
| Lost particles | 0 | 0 |
| NaN/Inf | 0 | 0 |

The collar keeps roughly half of the particles that would have become
ballistic in the coupled liquid/jet domain, for about 7.5% cost in the
outflow test. A sealed-bucket regression remained at 11.272 ms with all
130,607 particles active and zero NaN/loss.

## Rejected incompressibility prototypes

Two active-tile prototypes were measured and rejected:

1. a two-dispatch local divergence/density corrector;
2. an active-tile red-black SOR pressure prototype with particle-J feedback.

The local corrector improved average J by less than one percent. The SOR
prototype increased P2G by about 2.3 ms and total GPU time to about 15.3 ms
without producing a useful persistent pressure field. Neither is enabled or
reachable as a runtime kernel.

Near-incompressibility therefore remains a separate G19B objective. It
requires a global sparse solve (multigrid or properly preconditioned CG) with
an MLS-consistent density constraint, not another local correction.

## Validation switches

- `-paintValidationEnableJetMpmCollar`
- `-paintValidationDisableJetMpmCollar`
- `-paintValidationJetMpmCollarDuration <seconds>`
- `-paintValidationJetMpmCollarMaxDistance <meters>`
- `-paintValidationJetMpmCollarRadialPadding <meters>`

The outflow validator fails when the collar is enabled but no Jet particle
actually enters it.
