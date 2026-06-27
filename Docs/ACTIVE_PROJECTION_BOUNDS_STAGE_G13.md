# G13 - Active Projection Bounds

## Goal

Reduce the amount of projection-grid work before moving to a full sparse/tiled MPM architecture.

P2G/G2P are already particle-driven, but the projection infrastructure previously dispatched many kernels over the full dense grid. On an 80³ grid, that means `512000` cells for each projection clear/solid/finalize/divergence/pressure/gradient/diagnostics pass.

## Implemented path

G13 adds an active projection AABB around the rotated bucket:

- compute the bucket local AABB from height and max radius;
- transform its 8 corners to world space;
- add configurable padding;
- convert the world AABB to grid-cell bounds;
- clamp to the dense grid;
- dispatch projection grid kernels only over that active box.

The active box is conservative and can fall back to full-grid dispatch if it covers too much of the dense grid.

## Important implementation detail

A first linear active-index mapping was tested, but it was rejected because every thread had to reconstruct a 3D cell from a compact linear index. That added integer division overhead inside every projection kernel.

The final G13 path uses 3D compute dispatch for projection kernels:

- projection kernels use `[numthreads(8, 8, 8)]`;
- `SV_DispatchThreadID.xyz` maps directly to active local cell coordinates;
- no compact active-index unflattening is needed;
- particle kernels such as `KMarkProjectionFluidCells` remain 1D particle dispatches.

## Settings

`GpuMpmSolverConfig`:

- `enableActiveProjectionBounds`
- `activeProjectionBoundsPaddingMeters`
- `activeProjectionBoundsPaddingCells`
- `activeProjectionMaxFullGridFraction`

Current dev default:

- enabled: `true`
- padding meters: `0.04`
- padding cells: `2`
- fallback threshold: `0.95`

## Validation flags

`SimulationValidationRunner` accepts:

- `-paintValidationDisableActiveProjectionBounds`
- `-paintValidationActiveProjectionPadding <meters>`
- `-paintValidationActiveProjectionPaddingCells <cells>`
- `-paintValidationActiveProjectionMaxFraction <fraction>`

The validation report now prints:

- `activeProjection`
- `activeProjectionNodes`
- `activeProjectionFraction`
- `activeProjectionMin`
- `activeProjectionSize`

## Test results

Unity batch validation clone: `C:\Users\HP\demo_unity_validation`

### Sealed bucket, 120k target particles, 80³ grid

`g13-active-projection-tight-sealed-120k.log`

- PASS
- particles: `104436`
- active: `104436`
- lost: `0`
- nan/inf: `0`
- active projection: `True`
- active projection nodes: `172500`
- active projection fraction: `0.337`
- active projection min: `(15,6,15)`
- active projection size: `(50,69,50)`
- average divergence before: `45.34684`
- average divergence after: `37.69751`
- ms/substep: `39.582`

### Default smoke test

`g13-default-active-projection-smoke.log`

- PASS
- no explicit active-projection tuning flags were passed
- active projection: `True`
- active projection nodes: `172500`
- active projection fraction: `0.337`
- lost: `0`
- nan/inf: `0`

### Outflow, 120k target particles, 80³ grid

`g13-active-projection-tight-outflow.log`

- PASS
- particles: `104436`
- active: `67230`
- lost: `0`
- nan/inf: `0`
- hole open: `True`
- outflow: `445`
- max observed outflow: `666`
- jet: `10752`
- airborne: `26717`
- active projection: `True`
- active projection nodes: `172500`
- active projection fraction: `0.337`
- average divergence before: `45.42186`
- average divergence after: `37.95677`
- ms/substep: `43.331`

## Notes

- Cells outside the active projection bounds are not read as stale projection data. Neighbor reads treat out-of-active cells as safe air/free-surface boundaries unless the cell is outside the dense grid itself.
- The full dense MPM grid still exists. G13 reduces projection work only; it is a stepping stone toward the later true sparse/tiled pipeline.
- The old full-grid path remains available through `-paintValidationDisableActiveProjectionBounds`.
