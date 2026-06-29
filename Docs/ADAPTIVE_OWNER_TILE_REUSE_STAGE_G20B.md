# G20B — Adaptive Owner-Tile List Reuse

## Outcome

G20B reduces hybrid-P2G setup work only where measurement shows a net gain.
The owner-particle ordering can be reused for one substep, while a small tile
kernel keeps every previous owner tile active. Particle contributions remain
complete even when a particle crosses into a neighboring support tile.

Default policy:

- Fewer than 350,000 uploaded particles: rebuild every substep.
- 350,000 particles or more: alternate rebuild and reuse (`interval = 2`).
- Open holes/outflow: rebuild every substep.
- Support-reference lists or incompatible pipelines: rebuild every substep.

The runtime log reports `ownerListInterval`, `ownerListRebuilt`, and
`ownerListReused`.

## Implementation

- Added `KMarkMpmOwnerTilesActive`, dispatched over the small tile domain.
- Preserved owner counts, offsets, and ordered indices on reuse substeps.
- Disabled owner counting/filling on reuse substeps.
- Kept current support tiles and previous owner tiles in the active-tile union.
- Added particle-count and outflow safety gates.
- Added validation CLI controls:
  - `-paintValidationOwnerTileListInterval`
  - `-paintValidationOwnerTileListReuseMinParticles`

## Measured results

All measurements use D3D11, Grid-Density EOS, projection fallback, two
simulation substeps, and GPU stage profiling.

| Scenario | Mode sampled | GPU setup | GPU total | Result |
|---|---:|---:|---:|---|
| 261,855 sealed | rebuild | 3.055 ms | 18.472 ms | reuse rejected below threshold |
| 261,855 sealed | reuse | 2.341 ms | 18.873 ms | stale-owner P2G spill outweighed setup saving |
| 439,413 sealed | rebuild | 5.202 ms | 29.044 ms | reference |
| 439,413 sealed | reuse | 3.398 ms | 27.812 ms | reuse step faster |
| 439,413 sealed | reuse repeat | 3.406 ms | 28.211 ms | reuse step faster |

At 439k, reuse substeps are about 3–4% faster. Because rebuild and reuse
alternate, the expected sustained gain is approximately 1–2% on the tested
GPU. Physics diagnostics remained stable (`J avg = 1.0090`, no NaN, no lost
particles).

Open-hole validation automatically selected interval 1 and passed with active
outflow, jet, and airborne particles and zero lost particles.

## Rejected experiments removed from production code

- Full-support tiled P2G: physically correct but 21.5% slower at 262k because
  support references nearly doubled and list construction dominated.
- A deeper G2P/collision data fusion: no stable stage-level improvement.
- Owner-list interval 4: lower setup cost but higher stale-owner atomic spill
  in P2G, with no better sustained result.

## Next scalable step

The next material improvement should replace periodic full owner-list rebuilds
with persistent GPU bins and compact migration of only particles that cross a
tile boundary. That targets the remaining full-particle setup pass without
accepting stale-owner P2G spill.
