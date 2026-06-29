# Final Performance Stage: Adaptive Transfer LOD

## Goal

This stage targets the main remaining GPU bottleneck in the current dense-grid MLS/APIC-MPM paint solver: particle/grid transfers at high particle counts.

The adopted optimization is **Adaptive Transfer LOD**:

- Priority particles keep the full quadratic 3x3x3 MLS/APIC transfer.
- Calm interior particles use a cheaper 2x2x2 linear transfer for **P2G only**.
- G2P remains full quadratic by default to preserve velocity/APIC quality.
- Calm-linear P2G skips material stress scattering; pressure/incompressibility remains handled by Grid EOS + projection.
- The system auto-disables below a particle threshold so small scenes do not pay classification/readback overhead.

## Adopted defaults

`GpuMpmSolverConfig` now enables:

- `enableAdaptiveTransferStencil = true`
- `enableAdaptiveG2PTransferStencil = false`
- `adaptiveTransferMinParticles = 200000`
- `adaptivePriorityParticleSpeed = 3.0`
- `adaptivePriorityTopFraction = 0.6`
- `adaptivePriorityJDeviation = 1.0`

The J-deviation trigger is effectively disabled by default because testing showed it made too many particles priority and reduced the P2G benefit without producing a better stability/performance tradeoff.

## Why P2G-only

The rejected aggressive variant applied the linear transfer to both P2G and G2P. It reduced P2G time, but it increased post/G2P quality risk and produced more deformation clamps.

The adopted path only simplifies P2G for calm interior particles. This keeps the expensive transfer reduction where it matters most while preserving the higher-quality G2P sampling path.

## Validation

### High-load baseline with Adaptive Transfer disabled

Log:

- `final_perf_stage_final_off_439target_60.log`

Result:

- `particles=382707`
- `gpuTotalMs=26.547`
- `gpuP2GMs=11.977`
- `gpuMpmMs=15.896`
- `gpuProjectionMs=3.264`
- `gpuPostMs=7.386`
- `jClamps=7180`
- `nanInf=0`
- `lost=0`
- `msPerSubstep=126.090`

### High-load final adopted path

Log:

- `final_perf_stage_final3_439target_60.log`

Result:

- `particles=382707`
- `gpuTotalMs=21.065`
- `gpuP2GMs=7.709`
- `gpuMpmMs=11.161`
- `gpuProjectionMs=2.780`
- `gpuPostMs=7.124`
- `adaptiveTransferStencil=True`
- `adaptivePriority=149259`
- `adaptiveCalm=233448`
- `adaptivePriorityFraction=0.390`
- `jClamps=6759`
- `nanInf=0`
- `lost=0`
- `msPerSubstep=73.591`

Measured improvement against the same-code disabled baseline:

- GPU total: `26.547 -> 21.065 ms` = about **20.7% faster**
- P2G: `11.977 -> 7.709 ms` = about **35.6% faster**
- MPM section: `15.896 -> 11.161 ms` = about **29.8% faster**
- Projection section: `3.264 -> 2.780 ms` = about **14.8% faster**

The GPU profiling runs intentionally force stage readbacks, so their `msPerSubstep` field should not be used as the realtime wall-time metric.

### High-load wall-time check without GPU profiling

Logs:

- `final_perf_stage_wall_on_439target_120.log`
- `final_perf_stage_wall_off_439target_120.log`

Result:

- Adaptive Transfer on: `msPerSubstep=45.766`
- Adaptive Transfer off: `msPerSubstep=50.034`
- Wall-time improvement without profiler readback stalls: about **8.5% faster**
- Both runs kept `nanInf=0` and `lost=0`.

### Small-load auto-disable check

Log:

- `final_perf_stage_threshold3_112k_30.log`

Result:

- `particles=95479`
- `adaptiveMultiRate=False`
- `adaptiveSampleStep=0`
- `adaptivePriority=0`
- `adaptiveCalm=0`
- `adaptiveTransferStencil=False`
- `gpuTotalMs=11.083`
- `nanInf=0`
- `lost=0`

This confirms low-particle runs do not pay adaptive classification/readback overhead.

## Rejected variants

The following variants were tested and rejected:

- Linear G2P transfer: faster P2G, but worse quality/stability tradeoff and higher post-stage risk.
- Lower SOR iterations from 4 to 3: slightly faster but increased deformation clamps too much.
- J-deviation priority threshold around `0.25`: made too many particles priority and weakened the main P2G gain.
- Lower top priority fraction around `0.5`: slightly worse than `0.6` in the high-load scenario.

## Current performance position

This stage is the first adopted high-load optimization in this sequence that gives a clear, measurable improvement without increasing instability:

- The dense backing grid is still present (`activeMpmGridNodes=512000`), so this is not a full sparse-grid rewrite.
- Most of the gain now comes from reducing per-particle transfer work, not from reducing the dense grid allocation.
- To reach much larger particle counts comfortably in realtime, the next architectural wall is a true block/sparse grid or a deeper particle-bin/dispatch rewrite. This stage keeps the current solver stable and moves the bottleneck forward without committing to a risky full rewrite.
