# G12 - Projection Pressure Solver Performance

## Goal

Improve the cost of the pressure projection stage without removing the stable Jacobi reference path.

The previous production path used ping-pong Jacobi pressure iterations:

- read pressure from one buffer;
- write pressure to a temporary buffer;
- swap buffers every iteration.

That path is simple and safe, but expensive when the dense grid is large.

## Implemented path

G12 adds a selectable Red-Black SOR pressure solver:

- `ProjectionPressureSolveMode.Jacobi`
- `ProjectionPressureSolveMode.RedBlackSor`

Red-Black SOR updates the pressure field in-place in two color passes:

1. update red cells;
2. update black cells;
3. repeat for the configured iteration count.

This keeps the neighbor stencil race-safe while avoiding the Jacobi ping-pong pressure write path as the preferred runtime mode.

## Main settings

`GpuMpmSolverConfig`:

- `pressureSolveMode`
- `pressureJacobiIterations`
- `pressureRedBlackSorIterations`
- `pressureRedBlackSorOmega`

Current dev default:

- mode: `RedBlackSor`
- iterations: `4`
- omega: `1.35`
- Jacobi reference iterations: `12`

Jacobi remains available for debugging and comparison.

## Validation flags

`SimulationValidationRunner` now accepts:

- `-paintValidationPressureSolveMode 0` for Jacobi
- `-paintValidationPressureSolveMode 1` for Red-Black SOR
- `-paintValidationRedBlackSorIterations <n>`
- `-paintValidationRedBlackSorOmega <value>`

The validation report now prints:

- `pressureMode`
- `jacobiIter`
- `rbSorIter`
- `rbSorOmega`

## Test results

Unity batch validation clone: `C:\Users\HP\demo_unity_validation`

### Sealed bucket, 120k target particles, 80³ grid

`g12-rbsor4-sealed-120k.log`

- PASS
- particles: `104436`
- active: `104436`
- lost: `0`
- nan/inf: `0`
- pressure mode: `RedBlackSor`
- RB-SOR iterations: `4`
- omega: `1.35`
- average divergence before: `45.38892`
- average divergence after: `37.69448`
- ms/substep: `51.238`

### Outflow, 120k target particles, 80³ grid

`g12-rbsor4-outflow.log`

- PASS
- particles: `104436`
- active: `67433`
- lost: `0`
- nan/inf: `0`
- hole open: `True`
- outflow: `508`
- max observed outflow: `645`
- jet: `10399`
- airborne: `26860`
- pressure mode: `RedBlackSor`
- RB-SOR iterations: `4`
- average divergence before: `45.44588`
- average divergence after: `37.90417`
- ms/substep: `40.190`

### Default smoke test

`g12-default-rbsor4-smoke.log`

- PASS
- no explicit pressure mode was passed
- pressure mode: `RedBlackSor`
- RB-SOR iterations: `4`
- particles: `104436`
- active: `104436`
- lost: `0`
- nan/inf: `0`
- average divergence before: `38.12201`
- average divergence after: `20.79093`

### Jacobi reference

`g12-jacobi12-sealed-120k.log`

- PASS
- pressure mode: `Jacobi`
- Jacobi iterations: `12`
- average divergence before: `45.35473`
- average divergence after: `37.11853`
- ms/substep: `254.711`

Jacobi reduced divergence slightly more in this sealed test, but Red-Black SOR 4 stayed stable, preserved particles, passed outflow, and was much faster in validation.

## Notes

- The old Jacobi enable field is still named `enableJacobiPressureSolve` for serialization compatibility. It currently acts as the global pressure-solve enable gate.
- Future cleanup can rename it to a generic pressure-solve enable field with migration support.
- The validation parser now uses a raw integer parser for enum flags, because the existing positive-int parser clamps values to `>= 1`.
