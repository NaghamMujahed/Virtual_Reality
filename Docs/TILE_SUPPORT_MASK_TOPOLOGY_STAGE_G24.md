# G24 Tile Support-Mask Topology

## Goal

G24 reduces the remaining owner-bin setup cost without reintroducing compact
grid indirection or a second particle ordering pipeline.

The previous active-tile path made every particle perform one or more global
`InterlockedCompareExchange` operations directly against the active-tile
flags. At high particle counts, thousands of particles contend on the same
small set of tiles.

The adopted topology now performs:

1. each particle determines its owner tile and exact transfer-support range;
2. it atomically ORs a 27-bit neighborhood mask into that owner tile;
3. one lightweight 64-thread kernel expands the masks for all 1,000 tiles;
4. only the expansion kernel updates active flags, indices, and indirect
   dispatch arguments.

The resulting active tiles are identical to the previous implementation.
The particle collision/marking kernel no longer writes the three active-list
UAVs, reducing both contention and D3D11 UAV pressure.

## Additional cleanup

Owner count/write/offset clearing was merged into `KClearMpmTileData`.
The standalone owner-clear kernel and dispatch were removed.

The old owner-list reuse behavior remains correct: the expansion kernel also
marks any owner tile referenced by the reused list, even if current particles
have moved away from that owner tile. Contributions outside the owner tile
continue through the existing global-atomic spill path.

## Rejected owner-key cache

An intermediate experiment cached one owner key per particle so the scatter
pass would not recompute the key.

- A dedicated key buffer exceeded the guaranteed D3D11 limit of eight UAVs
  in the fused collision/marking kernel and was removed.
- Packing the key into unused GPU metadata passed validation, but measured
  `18.912 ms` and `19.057 ms` against a pre-stage average near `19.030 ms`.
- The approximately `0.24%` difference was measurement noise and did not
  justify changing particle metadata semantics.

No owner-key cache or fallback remains in the production path.

## High-load performance

Comparable pre-stage measurements at 382,711 particles:

- `mandatory_tiled_final_439k_30.log`: setup `3.054 ms`
- `mandatory_tiled_warningfix_final_439k_30.log`: setup `2.968 ms`
- average setup: `3.011 ms`
- last clean GPU total: `19.214 ms`

G24 measurements:

- `support_mask_439k_30.log`: setup `1.897 ms`, total `17.727 ms`
- `support_mask_repeat_439k_30.log`: setup `1.989 ms`
- average setup: `1.943 ms`

Measured setup improvement:

- `3.011 -> 1.943 ms`
- approximately `35.5%` faster

The clean total comparison is:

- `19.214 -> 17.727 ms`
- approximately `7.7%` faster

The second total sample was affected by an unrelated projection-time spike,
but setup remained within `0.092 ms` of the first G24 sample.

## Low-load validation

Log: `support_mask_112k_30.log`

- particles: `95,476`
- setup: `1.504 ms`
- GPU total: `10.385 ms`
- active tiles: `80 / 1000`
- `nanInf=0`
- `lost=0`

The historical same-workload baseline was `11.682 ms`, so the new topology
does not introduce a low-particle-count penalty.

## Outflow validation

Log: `support_mask_outflow_forced_112k_90.log`

- diagnostics step: `90`
- outflow transitions: `1218`
- jet particles: `504`
- jet-collar particles: `504`
- airborne particles: `2505`
- active tiles: `73`
- `nanInf=0`
- `lost=0`

The shorter asynchronous outflow run completed before its final diagnostics
readback and was therefore not used as a correctness result.

## Architectural status

G24 is useful preparation for a future block pool because transfer support is
now represented as explicit tile topology rather than particle contention on
the global active list.

The solver still uses dense backing storage for grid nodes. The next
full-sparse step should introduce block-local neighbor addressing or a page
table only if it beats the G24 setup and total-time gates.
