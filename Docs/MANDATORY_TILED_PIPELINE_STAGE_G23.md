# G23 Mandatory Tiled MLS-MPM Pipeline

## Goal

G23 closes the experimental performance branch with one strict rule:
an implementation that does not show a measured benefit is removed instead
of remaining as a fallback.

The production path is now:

- active-tile occupancy;
- compact owner-particle bins;
- centered-owner Hybrid Tiled P2G;
- active-tile grid clear and update;
- tiled projection with sparse pressure cells;
- fused pre-collision/tile marking;
- fused G2P/post-collision;
- Adaptive Transfer LOD at high particle counts.

The old dense and tile-ordered alternatives are no longer selectable at
runtime. If the mandatory tiled resources or kernels are unavailable, the
solver reports an error rather than silently executing an unmeasured path.

## Removed implementations

The following implementations were measured and then removed:

### Compact block storage

Compact block storage reduced MPM grid memory, but the address translation
and indirection cost made it slower on the current 80x80x80 local grid and
D3D11 target.

- 95,476-particle dense baseline: `11.682 ms`
- optimized compact-block version: `12.535 ms`
- 382,711-particle compact/block version: `24.852 ms`
- 382,711-particle dense owner-bin version: `22.051 ms`

Compact storage is therefore not retained as a fallback.

### Tiled G2P cache

The cache added shared-memory setup and synchronization without recovering
enough global-memory traffic:

- cached G2P pipeline: `22.283 ms`
- original direct G2P pipeline: `22.051 ms`

It was removed.

### Support-particle reference lists

Hybrid P2G only needs one owner entry per particle. The old support list kept
up to eight tile references per particle and performed extra count, prefix,
and fill passes.

At 382,711 particles, its index buffer alone reserved about 11.68 MiB.
The support counters, offsets, metadata, diagnostics, kernels, and runtime
configuration were all removed.

### Obsolete paths and controls

G23 also removed:

- dense clear/P2G/grid-update kernels;
- tile-ordered G2P, collision, projection marking, and deformation kernels;
- the ineffective Active MPM Grid Bounds system;
- runtime switches for rejected tiled/dense alternatives;
- switches that could disable the adopted fused collision passes;
- obsolete diagnostics and validation CLI arguments.

Active Projection Bounds remains independent and useful. It is not the
removed Active MPM Grid Bounds system.

## Final measured result

Final DX11 validation:

- log: `mandatory_tiled_warningfix_final_439k_30.log`
- particles: `382711`
- active tiles: `78 / 1000`
- tile-driven projection nodes: `39936 / 512000`
- `gpuSetupMs=2.968`
- `gpuP2GMs=6.814`
- `gpuGridMs=0.442`
- `gpuProjectionMs=2.964`
- `gpuPostMs=6.026`
- `gpuTotalMs=19.214`
- `nanInf=0`
- `lost=0`
- shader warnings/errors: none

The comparable pre-cleanup direct-G2P measurement was:

- log: `dense_original_g2p_ab_439k_30.log`
- `gpuTotalMs=22.051`

Measured GPU-total improvement:

- `22.051 -> 19.214 ms`
- approximately `12.9%` faster

An immediately preceding run measured `18.845 ms`; the documented final
number uses the last warning-free run rather than the best observed sample.

Outflow validation:

- log: `mandatory_tiled_final_outflow_112k_60.log`
- particles: `95476`
- outflow transitions: `549`
- jet particles: `317`
- jet-collar particles: `317`
- airborne particles: `20`
- `nanInf=0`
- `lost=0`

## Current architectural status

This is a mandatory sparse-execution pipeline, but it is not yet a fully
compact sparse-storage MLS-MPM solver:

- compute work for clear/update/projection is tile-driven;
- particles are binned into owner tiles;
- the backing grid still reserves 512,000 nodes.

The attempted compact-storage conversion was intentionally rejected because
it was slower on the current target. A future storage conversion must use a
different page-table/addressing design and must beat the G23 measurements
before adoption.

## Performance gate for future work

A future optimization is accepted only when it:

1. passes sealed-bucket and outflow validation;
2. keeps `nanInf=0` and `lost=0`;
3. improves repeatable GPU time at the 382k-particle workload;
4. does not degrade material or incompressibility diagnostics;
5. replaces the old path instead of becoming another permanent fallback.
