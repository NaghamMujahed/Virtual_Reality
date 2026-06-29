# G19B — High-performance incompressibility corrector

## Adopted runtime path

The production path is now a hybrid:

1. APIC/MLS-MPM P2G.
2. Grid-density EOS predictor.
3. Active-tile staggered pressure projection.
4. Density-drift target in the pressure RHS.
5. G2P/APIC and bucket/outflow handling.

The projection does not rebuild particle density. It reuses the mass already
stored on the MPM grid, removing the previous 27-node atomic particle scatter.
Dense projection setup/correction kernels dispatch indirectly over active
8x8x8 MPM tiles. Pressure iterations still use the compact fluid-cell list.

## Density-drift correction

A divergence-only projection can stop instantaneous compression while allowing
density error to accumulate. G19B stores grid/reference density per projection
cell and solves toward a controlled target divergence:

- over-dense cells receive positive target divergence;
- under-dense interior cells receive negative target divergence;
- under-dense free-surface cells are excluded from contraction.

Validated defaults:

- Grid EOS pressure scale: `4`
- projection interval: `1`
- Red/Black SOR iterations: `4`
- density drift strength: `0.2`
- density dead band: `1.02 / 0.98`
- maximum density target divergence: `12`

## D3D11 and boundary safety

- Projection tile indexing uses unsigned bit operations for the 8-cell tile.
- Tile indices and flags are read through SRVs, keeping UAV usage within D3D11
  limits.
- Neighbor reads reject inactive tiles, preventing stale pressure/cell data.
- Missing incoming free-surface faces reconstruct their pressure correction.
- The fallback AABB path remains functional when tile projection is disabled.
- Final validation reports no shader warnings, NaN/Inf, lost particles, or
  missing compute-buffer bindings.

## Validation results

All figures use an 80^3 grid and two simulation substeps.

| Scenario | Particles | Steps | J average | GPU total | Projection | Result |
|---|---:|---:|---:|---:|---:|---|
| Sealed, static | 130,651 | 120 | 0.9809 | 12.440 ms | 2.080 ms | Pass |
| Sealed, moving bucket | 130,651 | 120 | 0.9640 | 13.012 ms | 2.369 ms | Pass |
| Open hole/outflow | 130,651 | 120 | 0.9844 | 11.209 ms | 1.930 ms | Pass |
| Sealed scale test | 261,855 | 60 | 1.0447 | 22.114 ms | 2.511 ms | Pass |

The 261k result is close to the previous weakly-compressible 263k baseline
(`21.289 ms`) while adding the full incompressibility corrector.

At 131k, disabling tile projection increased the projection stage to
`4.531 ms`; active-tile dispatch cuts that stage by roughly half. Before grid
mass reuse, the first G19B prototype cost `10.366 ms` in projection because it
re-scattered every particle to 27 nodes.

## Runtime interpretation

`gpuGridNodeCount = 512000` is allocated capacity, not processed projection
work. When `projectionMpmTileDispatchUsed` is true,
`projectionActiveNodeCount` is updated from active tile count times 512 and is
the relevant work-domain metric.

This stage substantially closes practical volume drift for the tested static,
moving, outflow, and doubled-particle scenarios. It is still a finite-iteration
real-time projection, not an exact offline incompressible solve.
