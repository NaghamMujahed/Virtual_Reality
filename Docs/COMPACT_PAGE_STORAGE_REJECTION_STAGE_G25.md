# Stage G25 - Compact Page Storage Rejection

## Purpose

Stage G25 evaluated whether the current tiled MLS-MPM pipeline should move from
the G24 support-mask topology with dense backing grid storage to compact resident
page storage.

The goal was to reduce the physical grid allocation and only store nodes that
belong to active MPM tile pages.

## Attempted implementation

- Added resident MPM page slots for active tiles.
- Added a page-neighbor lookup table for node access across tile/page borders.
- Tested a 256-page resident pool, equivalent to 131,072 resident grid nodes.
- Kept the existing support-mask topology, tiled P2G/G2P, sparse pressure, and
  active tile projection behavior.

## Measurements

Baseline from Stage G24:

- `Logs/support_mask_112k_30.log`
  - PASS
  - particles: 95,476
  - total GPU time: 10.385 ms
- `Logs/support_mask_439k_30.log`
  - PASS
  - particles: 382,711
  - total GPU time: 17.727 ms
  - active tiles: 78

Compact page storage results:

- `Logs/compact_pages_smoke_112k_8_fix2.log`
  - PASS
  - particles: 95,217
  - resident grid nodes: 131,072
  - total GPU time: 11.476 ms
- `Logs/compact_pages_112k_30.log`
  - PASS
  - total GPU time: 10.447 ms
  - slightly slower than G24
- `Logs/compact_pages_439k_30.log`
  - PASS
  - total GPU time: 19.559 ms
  - slower than G24

Neighbor-table optimization attempt:

- `Logs/compact_pages_neighbor_smoke_112k_8_fixbind.log`
  - PASS
  - total GPU time: 11.500 ms
- `Logs/compact_pages_neighbor_439k_30.log`
  - FAIL
  - total GPU time: 21.486 ms
  - worse projection residual behavior

Post-removal verification:

- `Logs/post_compact_revert_smoke_112k_8.log`
  - PASS
  - particles: 95,217
  - grid nodes: 512,000
  - dispatches: 35
  - total GPU time: 10.219 ms
- `Logs/post_compact_revert_439k_30.log`
  - PASS
  - particles: 382,711
  - grid nodes: 512,000
  - dispatches: 33
  - active tiles: 78
  - total GPU time: 17.337 ms

## Decision

Compact resident page storage is rejected for the current D3D11 compute path.

Although it reduced the theoretical grid node storage from 512,000 nodes to
131,072 resident nodes, the required page indirection and neighbor lookup work
made the measured runtime worse. The neighbor-table variant also failed the high
particle validation run.

Per the project rule that unused or unproven optimizations must not remain as
fallback complexity, the compact page storage code was removed completely.

## Production state after G25

The production solver remains the Stage G24 path:

- Mandatory tiled MLS-MPM dispatch.
- Active support-mask topology.
- Dense backing grid allocation.
- Tiled/sparse projection work only where tiles are active.
- Owner particle list reuse.
- Hybrid tiled P2G.
- Fused G2P/collision.
- Adaptive transfer stencil for high particle counts.

There is no compact-page fallback path left in the production code.

## Next recommended direction

The next performance work should avoid adding node-indirection cost to the hot
P2G/G2P path on D3D11. Better candidates are:

- reducing G2P and projection frequency for calm regions;
- improving projection adaptivity and residual targets;
- reducing particle/render cost through visual surface rendering instead of
  only increasing physical particle count;
- investigating a true GPU sparse data structure only on a backend where the
  memory model makes random sparse access cheaper than the current dense backing
  grid.
