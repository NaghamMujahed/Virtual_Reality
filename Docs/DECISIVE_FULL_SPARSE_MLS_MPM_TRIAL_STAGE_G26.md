# Stage G26 - Decisive Full-Sparse MLS-MPM Trial

## Goal

Stage G26 was a decisive performance gate for the remaining "full sparse
MLS-MPM" direction. The rule was strict:

- keep a change only if it produces a measured performance win;
- remove any path that does not help;
- do not leave fallback complexity in production.

The production baseline before this trial was the Stage G24/G25 path:

- mandatory tiled MLS-MPM dispatch;
- support-mask tile topology;
- dense backing grid storage;
- owner particle lists;
- hybrid shared-memory tiled P2G;
- tiled grid update and tiled projection;
- sparse pressure cell list;
- adaptive transfer stencil for high particle counts.

## Baseline

Reference production run after G25 cleanup:

- `Logs/post_compact_revert_439k_30.log`
  - PASS
  - particles: 382,711
  - active tiles: 78
  - grid nodes: 512,000
  - total GPU time: 17.337 ms

Final post-G26 production verification:

- `Logs/g26_final_baseline_439k_30.log`
  - PASS
  - particles: 382,711
  - active tiles: 78
  - tile size: 8
  - tile projection: enabled
  - total GPU time: 18.018 ms

The difference between these two baseline numbers is treated as normal profiling
variance from Unity/editor/GPU scheduling. Both are materially better than the
rejected G26 variants below.

## Trial 1: tiled G2P dispatch by owner-tile lists

Hypothesis:

- Dispatching G2P by active owner tiles could improve locality and reduce
  linear particle-dispatch overhead.

Implementation:

- Added temporary `KG2PVelocityApicTiled`.
- Added temporary `KG2PVelocityApicBucketCollisionTiled`.
- Added a validation-only config flag and report field.
- Dispatched G2P over active MPM tiles and processed each tile's owner-particle
  list.

Results:

- `Logs/g26_tiled_g2p_smoke_112k_8.log`
  - PASS
  - tiledG2P: true
  - total GPU time: 11.581 ms
  - worse than the 112k baseline `10.219 ms`
- `Logs/g26_tiled_g2p_439k_30.log`
  - PASS
  - tiledG2P: true
  - particles: 382,711
  - total GPU time: 18.920 ms
  - worse than the production baseline

Decision:

- Rejected.
- Removed completely from shader, config, runtime stats, validation flags, and
  solver binding code.

Reason:

- G2P still touches every active particle.
- Grouping by tile increased long per-tile loops and did not reduce the hot
  grid sampling cost.
- It improved neither total time nor post/G2P stage time.

## Trial 2: adaptive G2P transfer stencil

Hypothesis:

- Reusing the existing adaptive transfer classification for G2P could reduce
  the 3x3x3 sampling cost for calm interior particles.

Result:

- `Logs/g26_adaptive_g2p_439k_30.log`
  - PASS
  - total GPU time: 18.511 ms
  - worse than the production baseline

Decision:

- Not adopted.
- The existing feature remains off by default.

Reason:

- The reduced G2P stencil did not offset the branch/classification cost enough
  in the high-particle validation scene.

## Trial 3: smaller MPM tile size

Hypothesis:

- `mpmTileSize=4` could increase parallelism and improve tile locality.

Result:

- `Logs/g26_tile4_439k_30.log`
  - PASS
  - tile size: 4
  - active tiles: 487
  - total GPU time: 22.364 ms
  - projection fell back to a wider active-bounds path instead of the preferred
    tile projection path

Decision:

- Rejected.
- Keep production tile size at 8.

Reason:

- More active tiles increased overhead.
- Tile projection efficiency regressed.
- P2G and projection both became slower.

## Relation to G25 compact page storage

G25 already tested compact resident grid pages:

- lower resident node count;
- page-slot lookup;
- neighbor page lookup.

That path also lost against the baseline and was removed. Combined with G26,
the conclusion is now stronger:

- reducing storage alone is not enough;
- adding indirection inside P2G/G2P hot loops is harmful on the current D3D11
  compute path;
- the current hybrid strategy is the best measured option in this project:
  dense backing storage with sparse/tiled execution.

## Final decision

The full-sparse MLS-MPM direction is closed for the current Unity/D3D11 solver
architecture.

The project should continue with the Stage G24/G25 production path. Future
performance work should not add sparse grid indirection unless the solver is
rebuilt around a different backend or a fundamentally different GPU data model.

## Recommended next performance directions

The remaining high-value directions are not "more sparse storage" in the current
solver. Better candidates are:

- reduce projection cost through stricter adaptive projection cadence;
- improve rendering so visual quality does not require multiplying physical
  particle count;
- profile normal Play Mode without validation readback/stage profiling;
- investigate DX12/Vulkan or a separate compute backend before attempting true
  prefix-sum compact sparse storage again;
- reduce unnecessary visual/paint-surface work during heavy fluid simulation.

No G26 experimental code remains in production.
