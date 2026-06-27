# G16 — Hybrid tiled P2G and sparse pressure

## Outcome

G16 targets measured GPU bottlenecks:

- synchronized GPU stage profiling for validation;
- owner-tile particle lists built on the GPU;
- hybrid tiled quadratic MLS-MPM P2G;
- compact projection-fluid cell lists;
- sparse indirect Jacobi and Red-Black SOR pressure dispatch.

Production defaults enable `enableHybridTiledP2G` and
`enableSparseProjectionPressureDispatch`. Stage profiling stays disabled.

## Hybrid P2G

Each particle is assigned once to an owner tile. One compute group:

1. clears an 8×8×8 shared-memory accumulator;
2. evaluates every particle's full quadratic 3×3×3 MLS-MPM stencil once;
3. accumulates owner-tile nodes in group-shared memory;
4. uses global atomics only across tile edges;
5. flushes each shared node once to the global grid.

This preserves APIC/MLS-MPM transfer, material stress, paint rheology,
diagnostics, and the dense fallback.

## Sparse pressure

`KFinalizeProjectionGrid` compacts fluid cells into a GPU list. Jacobi or
Red-Black SOR iterations use an indirect 1D dispatch over that list instead
of scanning the active projection AABB.

At the tested state, about 31.8k fluid cells were solved instead of 172.5k
active-bound nodes, or roughly 18.4% of that domain.

## NVIDIA MX110 / D3D11 results

Synchronized profiling at 104,436 particles:

- regular P2G: 12.159 ms;
- hybrid P2G: 4.699 ms (61.4% lower);
- regular MPM core: 14.049 ms;
- hybrid MPM core: 7.165 ms (49.0% lower).

Alternating 120-substep validation without profiling:

- regular path average: 41.617 ms/substep;
- optimized path average: 29.283 ms/substep;
- end-to-end reduction: 29.6%.

At 152,833 generated particles:

- regular path: 46.999 ms/substep;
- optimized path: 37.997 ms/substep;
- reduction: 19.2%.

## Validation

Passing coverage includes sealed and moving/open-hole buckets, outflow,
jet and airborne states, both pressure solvers, 104k/152k particles, and
zero NaN/Inf, lost, list-overflow, or no-grid-support particles.

The older tile-ordered pipeline remains disabled because it was 7–10%
slower on this GPU. It remains available as a reversible experiment.

`gpuGridNodeCount=512000` is allocated dense capacity, not work executed by
every kernel. Active-tile and sparse-pressure counters describe actual work.
