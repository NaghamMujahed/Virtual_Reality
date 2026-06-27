# G15-B — GPU Tile Particle Lists and Tile-Ordered P2G

## Goal

Move beyond active-tile detection and build GPU particle lists that can drive
later sparse/tiled MPM kernels.

## Implemented

- Compact per-tile support-reference counts, offsets, and indices.
- A separate one-entry-per-particle list ordered by an owner tile.
- GPU-only count, prefix-offset, fill, and indirect-dispatch preparation.
- D3D11-safe kernel splitting so no dispatched kernel exceeds the 8-UAV limit.
- Tile-ordered P2G that executes the existing P2G equations without changing
  the material model.
- Diagnostics for:
  - active tiles;
  - support-reference count;
  - maximum references in one tile;
  - compact-list capacity;
  - overflow count.
- Validation command-line switches for particle tile lists and tile-ordered
  P2G.

## Safety

The compact support-list capacity is:

`uploadedParticleCount * 8`

A quadratic 3x3x3 transfer stencil can overlap at most two tiles per axis for
the supported 4- or 8-cell tiles, so the maximum is eight support tiles per
particle.

The tile-ordered P2G list contains one index per active MPM particle.

## Validation

All final tests completed without NaN/Inf, missing grid support, lost
particles, or list overflow.

- Default sealed smoke: PASS.
- Tile-ordered P2G sealed, 60 steps: PASS.
- Tile-ordered P2G sealed, 120 steps: PASS.
- Tile-ordered P2G outflow: PASS.
- Higher-particle test: PASS with 175,214 particles.

The physical diagnostics closely match the regular P2G path.

## Performance decision

Tile ordering improved some short runs, but the gain was not consistent over
longer runs on the current NVIDIA MX110 / D3D11 test system. At 104k particles,
the list-building overhead can cancel the cache-locality benefit. At 175k
particles the measured result was approximately neutral.

Therefore:

- `enableMpmParticleTileLists` remains disabled by default.
- `enableTiledP2G` remains disabled by default.
- The implementation remains available for profiling on stronger GPUs and as
  the data foundation for later sparse kernels.

## Next performance stage

The next stage should target work reduction rather than dispatch reordering:

1. Build cell/Morton keys and perform a GPU radix sort or a persistent
   tile-local particle layout.
2. Use sorted particle ranges for fused P2G/material-stress processing.
3. Add GPU timestamp profiling per kernel before enabling a new path by
   default.
4. Evaluate a D3D12 path with wave-level reductions where supported.
5. Add multi-rate updates for interior fluid, surface particles, jets, and
   distant droplets.

