# G20 — Scalable particle core

## Adopted changes

G20 optimizes the measured particle bottlenecks without changing the
APIC/MLS-MPM equations:

1. `Centered Hybrid P2G Owner`
   - A particle is owned by the tile containing the middle of its quadratic
     3x3x3 stencil instead of always using the minimum support tile.
   - More stencil nodes are accumulated in group-shared memory and fewer use
     global atomics across tile edges.
   - Support-only active tiles return before clearing/flushing a 512-node
     shared block.
   - Tile-local index reconstruction uses power-of-two bit addressing.

2. `Fused G2P + Post Collision`
   - APIC G2P/advection and mandatory post-G2P bucket collision run in one
     particle dispatch.
   - This removes one full dispatch and reduces particle-buffer round trips.

3. `Fused Pre Collision + Tile Mark`
   - Pre-P2G bucket collision and active/owner tile classification run in one
     dispatch when support-reference lists are not requested.
   - The fallback separate path remains available.

4. Scalable diagnostics
   - J accumulation uses 1e-3 fixed-point precision, preventing the previous
     32-bit sum overflow above roughly 430k particles.

## Rejected path

The existing tile-ordered particle pipeline was re-tested at 261,855
particles. It increased total GPU time to `23.248 ms/substep`, so it remains
disabled. The hybrid shared-memory P2G path is still the production path.

## A/B results at 261,855 particles

All runs use an 80^3 grid, two simulation substeps, Grid EOS plus active-tile
projection, and four Red/Black SOR iterations.

| Configuration | P2G | Particle post | GPU total |
|---|---:|---:|---:|
| G19B baseline | 10.226 ms | 5.417 ms | 22.114 ms |
| Centered owner off, post fusion on | 10.404 ms | 5.181 ms | 22.543 ms |
| Centered owner on, post fusion off | 7.867 ms | 5.462 ms | 20.779 ms |
| All G20 optimizations | 7.730 ms | 5.024 ms | 19.219 ms |

The adopted result is about 13.1% faster end to end and about 24.4% faster in
P2G. Dispatch count falls from 33 to 31. Physics diagnostics remain equivalent:
`Javg=1.0447`, no NaN/Inf, no lost particles, no unsupported particles, and no
tile-list overflow.

## Scenario validation

| Scenario | Particles | Steps | J average | GPU total | Result |
|---|---:|---:|---:|---:|---|
| Sealed, moving bucket | 130,651 | 120 | 0.9706 | 11.641 ms | Pass |
| Open hole/outflow | 130,651 | 120 | 1.0183 | 10.886 ms | Pass |
| Sealed scale test | 439,413 | 60 | 1.0090 | 28.624 ms | Pass |

The 439k test used 74 active tiles (37,888 projection nodes) out of the
allocated 512,000-node capacity. It completed with zero shader warnings,
binding errors, NaN/Inf, lost particles, or list overflow.

## Current bottlenecks

At 439k particles:

- setup/tile building and pre-collision: `5.331 ms`;
- P2G: `12.364 ms`;
- projection: `2.520 ms`;
- G2P/post-collision: `7.989 ms`.

Projection is no longer the dominant cost. The next performance stage should
target a full support-tile P2G path with no cross-tile global atomics and
boundary-aware collision culling/compaction before increasing visual
reconstruction cost.
