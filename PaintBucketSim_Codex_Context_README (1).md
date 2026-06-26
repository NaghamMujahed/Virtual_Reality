# PaintBucketSim — Codex Agent Context / README

## 0. Role for Codex

You are continuing a Unity 6 URP 3D physics simulation project named **PaintBucketSim**.

Act as a senior Unity simulation engineer, GPU compute engineer, and numerical physics developer. The project is not a toy demo. The goal is a professional, extensible simulation of a paint bucket suspended by a rope, with paint moving inside the bucket, exiting through holes, becoming droplets/jet particles, and painting a canvas.

The user wants deep engineering quality, clean architecture, and strong physical reasoning. Do not add random patches without explaining where they belong in the simulation pipeline. Prefer systematic fixes, measurable diagnostics, and modular GPU-friendly architecture.

The user is Arabic-speaking; respond in Arabic when explaining decisions. Code should remain English and idiomatic C#/HLSL.

---

## 1. Project Goal

The final simulation should show:

1. A bucket suspended by a rope.
2. The bucket swings and rotates in 3D.
3. Paint inside the bucket behaves as a dense viscous liquid.
4. Paint interacts with bucket walls, bottom, moving boundaries, and holes.
5. Paint exits through bottom holes as a jet or stream.
6. Outside the bucket, paint transitions into airborne droplets/particles.
7. Droplets collide with a canvas.
8. The canvas accumulates color, wetness, thickness, absorption, spreading, drying, and color mixing.
9. The system should be real-time or near real-time inside Unity, using GPU compute where necessary.

The current focus is **only the in-bucket liquid solver**, not yet outflow/canvas.

---

## 2. Current Unity / Rendering Assumptions

- Unity version: Unity 6.
- Project type: 3D.
- Render pipeline: URP.
- GPU compute should be used from the beginning for heavy particle/grid operations.
- Initial target particle count started around 5,000 but should be scalable far beyond that later.
- The current solver is still dense-grid based, not sparse/tiled/adaptive yet.
- Current solver files are named with older names such as:
  - `GpuMpmDenseLocalSolver.cs`
  - `GpuDenseMpmPrototype.compute`
- Even if names say “MpmPrototype”, the intended direction is now **GPU MLS-MPM-oriented liquid solver**.

---

## 3. High-Level Architecture Built So Far

### 3.1 CPU/Unity Systems

The project contains systems for:

- `SimulationManager`
- `BucketSystem`
- `RopeSystem`
- `PaintFluidSystem`
- `GpuFluidBufferSet`
- `GpuMpmDenseLocalSolver`
- GPU particle rendering/debug overlay
- Config ScriptableObjects for solver, fluid, paint material, bucket, etc.

### 3.2 GPU Solver Core

The current GPU solver uses:

- Particle buffers:
  - position/radius
  - velocity/mass
  - color
  - state/age/id
  - affine C rows
  - volume/J/rest density
  - deformation gradient F rows
- Dense MPM grid buffers:
  - accumulated fixed-point mass/momentum
  - grid velocity/mass
- Projection grid buffers:
  - cell type
  - cell mass
  - cell data
  - pressure
  - pressure temp
  - divergence

The current compute shader has kernels approximately like:

```text
KClearGrid
KP2G
KGridUpdate
KG2PVelocityApic
KBucketCollision
KUpdateDeformation

KClearProjectionGrid
KMarkBucketProjectionSolids
KMarkProjectionFluidCells
KFinalizeProjectionGrid
KComputeProjectionDivergence
KJacobiProjectionPressure
KSubtractProjectionPressureGradient
```

---

## 4. Simulation Pipeline — Current State

The current C# solver pipeline is approximately:

```text
SetCommonParameters

KClearGrid
KP2G
KGridUpdate

RunProjectionGridInfrastructure:
    KClearProjectionGrid
    KMarkBucketProjectionSolids
    KMarkProjectionFluidCells
    KFinalizeProjectionGrid
    KComputeProjectionDivergence
    KJacobiProjectionPressure × N
    KSubtractProjectionPressureGradient

KG2PVelocityApic
KBucketCollision
KUpdateDeformation
```

Important observation:

`KBucketCollision` currently runs **after** G2P and after pressure projection. This means bucket collision can compress particles after pressure has already been solved for that frame. This is one of the major remaining problems.

---

## 5. Physics Direction

The project originally started with CPU/PBF-like particle fluid, boundary particles, and simpler rigid bucket/rope ideas.

The current direction is no longer to depend on boundary particles or old simple PBF. The intended direction is:

```text
GPU MLS-MPM-oriented liquid core
+ APIC affine transfer
+ quadratic B-spline 3×3×3 transfer
+ projection grid
+ pressure projection
+ moving bucket boundary coupling
+ viscosity/rheology/cohesion/wetting
+ later sparse/tiled/adaptive optimization
```

Boundary particles should eventually be removed from the main GPU MLS-MPM path once solid grid/SDF boundary representation is stable.

---

## 6. What Has Been Implemented / Attempted

### 6.1 GPU Dense MPM Prototype

Implemented:
- Dense grid.
- P2G.
- Grid update.
- G2P.
- Fixed-point atomic accumulation for grid mass/momentum.
- GPU-owned buffers after initial CPU upload.

### 6.2 APIC / Affine Transfer

Implemented:
- Affine C buffers:
  - `_ParticleAffineC0`
  - `_ParticleAffineC1`
  - `_ParticleAffineC2`
- APIC/affine transfer became part of the core, no longer an optional feature.
- Important bug encountered: at one point `b0/b1/b2` accumulation inside `KG2PVelocityApic` was commented out, causing affine C to collapse to zero. Make sure accumulation is active:

```hlsl
float3 nodeWorld = GridToWorld((float3)node);
float3 dpos = nodeWorld - p;

b0 += w * nodeVelocity.x * dpos;
b1 += w * nodeVelocity.y * dpos;
b2 += w * nodeVelocity.z * dpos;
```

### 6.3 Quadratic B-Spline Transfer

Implemented:
- 3×3×3 stencil.
- `QuadraticBSplineWeights`.
- `QuadraticWeightGradientWorld`.

This replaced the old 2×2×2 trilinear transfer. The old linear helper should not be used.

### 6.4 F / J / Material Stress

Implemented:
- Particle deformation gradient rows:
  - F0/F1/F2
- J = determinant(F)
- Weak compressible material stress prototype:
  - pressure from J
  - viscous stress from symmetric part of affine C

Important: This is not the final incompressibility model.

### 6.5 Paint Rheology Prototype

Implemented:
- Shear-thinning-like effective viscosity.
- Low-shear viscosity.
- High-shear viscosity.
- relaxation time.
- power-law exponent.
- yield-like contribution.

This is still prototype-level and must be calibrated after pressure behavior stabilizes.

### 6.6 Real Bucket Collision

Implemented:
- Analytic bucket collision:
  - cylinder/tapered cylinder
  - bottom
  - top lid/open/spilled modes
  - bottom hole region classification
- Moving bucket wall velocity:
  - linear velocity
  - angular velocity
  - wall velocity `v_wall = v_bucket + omega × r`
- Collision currently corrects particles directly in `KBucketCollision`.

Important issue:
- This collision is currently too hard/positional and can compress particles into walls, especially during strong bucket movement.

### 6.7 Projection Grid Infrastructure

Implemented:
- Projection cell types:
  - Air = 0
  - Fluid = 1
  - Solid = 2
- Bucket interior/outside classification.
- Outside bucket interior marked as solid.
- Fluid cell marking from particles.

Important fix already made/should exist:
- `KMarkProjectionFluidCells` should mark fluid using the same 3×3×3 quadratic stencil as P2G, not only `floor(gridPos)`. Otherwise pressure correction touches too few cells and has little visible effect.

### 6.8 Divergence / Jacobi / Pressure Gradient

Implemented:
- `KComputeProjectionDivergence`.
- `KJacobiProjectionPressure`.
- `KSubtractProjectionPressureGradient`.
- C# pressure buffers:
  - pressure
  - pressure temp
  - divergence
- Jacobi swap logic.
- Pressure correction currently affects grid velocities before G2P.

Important:
- Current projection is collocated-grid prototype, not a final MAC-grid projection.
- Jacobi is debug/reference solver, not final production solver.
- It is expensive and can slow performance significantly.

---

## 7. Important Problems Encountered and Fixes

### 7.1 UAV Limit on D3D11

Problem:
Unity/D3D11 complained:

```text
There are more UAVs than the maximum supported (8)
```

Cause:
Too many `RWStructuredBuffer` bindings in one kernel.

Fix:
- Split `KG2P` into:
  - `KG2PVelocityApic`
  - `KUpdateDeformation`
- Use `StructuredBuffer` for read-only aliases.
- Avoid read/write aliasing the same resource in the same kernel unless necessary.

### 7.2 `l-value specifies const object`

Cause:
Trying to write to a `StructuredBuffer`.

Fix:
- Use separate read and write aliases:
  - read: `StructuredBuffer<T>`
  - write: `RWStructuredBuffer<T>`
- Example:
  - `_ProjectionPressureRead`
  - `_ProjectionPressureWrite`

### 7.3 Kernel Invalid Due to Function Order

Problem:
HLSL compute kernels became invalid when helper functions were declared after first use.

Fix:
Order helper functions before kernels. Important order:

```text
1. constants
2. FlattenGridIndex
3. UnflattenGridIndex
4. IsValidCell
5. projection helper functions
6. general math helpers
7. bucket helpers
8. pressure helpers
9. kernels
```

### 7.4 ComputeShader.SetVector float3 Issue

Problem:
C# error when passing `Unity.Mathematics.float3` to `ComputeShader.SetVector`.

Fix:
Convert `float3` to `Vector4`.

```csharp
private static Vector4 ToVector4(float3 value, float w = 0.0f)
{
    return new Vector4(value.x, value.y, value.z, w);
}
```

### 7.5 Paint Initially Has Fill Volume Then Collapses

Observation:
`fillFraction` works at initial particle generation, then paint collapses to a thin layer.

Cause:
`fillFraction` only initializes particle distribution. It does not enforce volume conservation during simulation. Incompressibility/pressure/projection must maintain volume.

Partial steps taken:
- Mass/volume/density calibration planned/partially integrated.
- Projection grid and pressure projection added.

Still not solved completely.

### 7.6 Reducing `cellSizeMeters` Makes Volume Appear

Observation:
When `cellSizeMeters` was reduced, liquid volume became more visible.

Interpretation:
Projection grid resolution was too coarse. With smaller cells:
- More fluid cells exist.
- Divergence/pressure has spatial meaning.
- Volume is better represented.

But:
- Smaller cells amplify pressure gradients via `_InvCellSize`.
- Pressure tuning must be reduced after reducing cell size.
- Performance becomes worse.

### 7.7 Paint Looks Too Light / Rises Too Much

Observation:
Paint rises too high, appears lightweight, as if gravity is weak.

Likely causes:
- Pressure gradient correction too strong.
- Pressure RHS scale too high.
- `MaxPressureVelocityCorrection` too high.
- `cellSizeMeters` reduced without reducing pressure scaling.
- Pressure sign may need validation.
- Collision correction and moving wall velocity can add energy.
- Lack of hydrostatic balance and damping/viscosity stabilization.

### 7.8 Paint Compresses Against Walls When Bucket Moves

Observation:
When the bucket is moved strongly, paint compresses at the walls.

Main cause:
- Pressure projection runs before G2P.
- `KBucketCollision` runs after G2P and can hard-correct particle positions.
- Compression caused by collision is not included in the same-frame pressure solve.
- Projection does not yet fully incorporate moving wall boundary velocity.
- `KBucketCollision` is still a hard positional safety clamp, not a soft coupled boundary condition.

This is a major remaining issue.

---

## 8. Current Major Remaining Problem

### Problem: In-Bucket Liquid Behavior Still Not Satisfactory

Symptoms:
- Paint feels too light.
- Paint rises too much.
- Gravity does not visually dominate enough.
- Strong bucket motion compresses paint into walls.
- Pressure projection improves volume only at smaller cell sizes but introduces odd behavior/performance cost.
- Current Jacobi projection is too expensive relative to visible benefit.

Conclusion:
The solver is in a transitional state. It needs stabilization before moving to hole outflow.

---

## 9. Immediate Next Work for Codex

Do NOT jump to hole outflow yet.

Do NOT jump to canvas yet.

Do NOT immediately replace Jacobi with multigrid before stabilizing the model.

The next work should focus on **in-bucket liquid stabilization**.

Recommended next stages:

---

## 9.1 Stage A — Verify Current Code State

Codex should first inspect:

- `GpuMpmDenseLocalSolver.cs`
- `GpuDenseMpmPrototype.compute`
- `GpuMpmSolverConfig.cs`
- `PaintFluidSystem.cs`
- `GpuFluidBufferSet.cs`
- relevant runtime stats/debug files

Checklist:

1. Ensure all kernels in C# exist in the compute shader.
2. Ensure no kernel has more than 8 UAVs for D3D11 compatibility.
3. Ensure read-only buffers are `StructuredBuffer`.
4. Ensure write buffers are `RWStructuredBuffer`.
5. Ensure helper functions are ordered before use.
6. Ensure pressure buffers swap correctly.
7. Ensure `_projectionPressureBuffer` holds final pressure after Jacobi.
8. Ensure `KMarkProjectionFluidCells` uses 3×3×3 stencil.
9. Ensure `KG2PVelocityApic` accumulates b0/b1/b2.
10. Ensure `KBucketCollision` is conditional and acts only as safety when possible.

---

## 9.2 Stage B — Add Projection Diagnostics

Before more physics, add diagnostics.

Need GPU/CPU readback or reduced debug buffers to measure:

- fluid cell count
- solid cell count
- air cell count
- average divergence before pressure
- max absolute divergence before pressure
- average divergence after pressure
- max absolute divergence after pressure
- average pressure
- max pressure
- number of active pressure cells
- average pressure velocity correction
- max pressure velocity correction
- count of particles corrected by bucket collision
- approximate liquid height
- approximate represented volume

Goal:
Know whether pressure projection actually reduces divergence.

Without diagnostics, tuning is blind.

---

## 9.3 Stage C — Pressure / Gravity / Scale Stabilization

Current pressure correction may be too strong.

Recommended introduce/verify config:

```text
pressureRhsScale
pressureGradientScale
maxPressureVelocityCorrection
maxProjectionPressure
pressureJacobiRelaxation
pressureJacobiIterations
invertPressureGradientSign
```

Add tests:
1. Projection off.
2. Divergence only.
3. Jacobi pressure only.
4. Pressure gradient with very low scale.
5. Flip sign once if projection increases expansion/compression.
6. Compare divergence before/after.

Recommended safe initial values:

```text
pressureJacobiIterations = 10 to 20
pressureRhsScale = 0.3 to 0.7
pressureJacobiRelaxation = 0.5 to 0.7
maxProjectionPressure = 300 to 800
pressureGradientScale = 0.05 to 0.2
maxPressureVelocityCorrection = 0.2 to 0.6
```

If `cellSizeMeters` is reduced, reduce pressureGradientScale and max correction.

---

## 9.4 Stage D — Moving Bucket Boundary Coupled Projection

This is the most important fix for wall compression.

Goal:
The projection solver must know about moving bucket wall velocity before G2P.

Possible approach:

1. Add kernel:
   - `KApplyMovingBucketProjectionBoundaryVelocity`
2. Run it after:
   - `KFinalizeProjectionGrid`
3. Run it before:
   - `KComputeProjectionDivergence`
4. It should correct grid velocities near solid bucket cells using moving wall velocity.

Pseudo-pipeline:

```text
KGridUpdate
KClearProjectionGrid
KMarkBucketProjectionSolids
KMarkProjectionFluidCells
KFinalizeProjectionGrid
KApplyMovingBucketProjectionBoundaryVelocity
KComputeProjectionDivergence
KJacobiProjectionPressure
KSubtractProjectionPressureGradient
KG2PVelocityApic
KBucketCollision
KUpdateDeformation
```

Projection divergence should use moving wall velocity at fluid-solid faces instead of assuming solid neighbor has center velocity.

Expected effect:
- Less compression at walls during bucket motion.
- Bucket motion transfers momentum to grid before pressure solve.
- `KBucketCollision` becomes less active.

---

## 9.5 Stage E — Soften Bucket Collision

Current `KBucketCollision` performs hard positional correction.

Needed changes:
1. Count corrected particles for diagnostics.
2. Reduce energy injection:
   - lower restitution
   - friction tuned by viscosity
   - stronger velocity damping only on penetration
3. Avoid pushing many particles into identical boundary locations.
4. Add penetration-depth based correction rather than full snap when possible.
5. Treat `KBucketCollision` as safety clamp, not primary wall dynamics.

Potential future:
Move most wall handling to grid/projection and use particle collision only as final safety.

---

## 9.6 Stage F — Hydrostatic / Free-Surface Stabilization

The paint still feels too light. Pressure projection alone does not automatically give pleasing heavy paint behavior in this prototype.

Needed:
- Hydrostatic balance or gravity-pressure calibration.
- Free-surface handling:
  - air pressure = 0 boundary is currently simple.
  - surface cells may need better treatment.
- Liquid height/volume validation.

Possible future:
- Compute approximate liquid height.
- Compare current occupied volume to initial `fillFraction`.
- Add mild correction or diagnostics first, not aggressive forces.

---

## 9.7 Stage G — Viscosity, Cohesion, Wetting

After pressure is stable:
- Re-enable/retune paint rheology.
- Add cohesion/surface tension-like behavior.
- Add wall wetting / partial no-slip.
- Add near-wall damping.
- Tune for paint, not water.

The paint should feel:
- heavier than water,
- viscous,
- less splashy,
- coherent,
- not gas-like.

---

## 9.8 Stage H — Replace Jacobi Later

Jacobi is only debug/reference solver.

Do not replace it until:
- divergence before/after is measured,
- sign/scale are validated,
- moving boundary coupling is stable.

Later production options:
1. Red-Black Gauss-Seidel.
2. PCG.
3. Multigrid.
4. Sparse/tiled projection.

---

## 10. What Not To Do Yet

Do not implement:
- hole outflow,
- jet/droplet transition,
- canvas deposition,
- drying/spreading,
- sparse grid,
- multigrid,
until in-bucket liquid behavior is stable enough.

Reason:
Outflow from an unstable in-bucket liquid will be physically wrong and visually unstable.

---

## 11. Important Implementation Warnings

### 11.1 D3D11 UAV Limit

Keep each compute kernel at 8 UAVs or less.

Use:
- `StructuredBuffer` for read-only data.
- `RWStructuredBuffer` only where writing is needed.
- split kernels if needed.

### 11.2 HLSL Function Order

Always define helper functions before kernels that use them.

Important order:
```text
Flatten / Unflatten / IsValidCell
projection helpers
general math helpers
bucket helpers
pressure helpers
kernels
```

### 11.3 Buffer Alias Discipline

Use aliases like:
```hlsl
StructuredBuffer<float> _ProjectionPressureRead;
RWStructuredBuffer<float> _ProjectionPressureWrite;
```

Do not write into `StructuredBuffer`.

### 11.4 Pressure Buffer Swap

Jacobi requires:
```text
read pressure old
write pressure new
swap
```

After the loop, the C# variable `_projectionPressureBuffer` should point to the final buffer used by `KSubtractProjectionPressureGradient`.

### 11.5 Cell Size Sensitivity

When reducing `cellSizeMeters`, reduce pressure scaling:
- `pressureGradientScale`
- `maxPressureVelocityCorrection`
- sometimes `pressureRhsScale`

Because `_InvCellSize` increases.

### 11.6 Collision After Projection

Currently collision after projection is a known weakness. It must be softened or coupled with projection.

---

## 12. Suggested Baseline Debug Config

Start with low-energy stable settings:

```text
particle count: 1500–5000
cellSizeMeters: small enough to represent fluid volume, but not too small
pressureJacobiIterations: 10–20
pressureRhsScale: 0.3–0.7
pressureJacobiRelaxation: 0.5–0.7
maxProjectionPressure: 300–800
pressureGradientScale: 0.05–0.2
maxPressureVelocityCorrection: 0.2–0.6
invertPressureGradientSign: false initially
pressureCorrectionFluidCellsOnly: false for testing

moving bucket boundary velocity strength: 0.2–0.4
max bucket boundary velocity: 3–5
bucket restitution: 0
bucket friction: 0.25–0.4

paint rheology: disabled during pressure debugging
material stress: low
moving bucket motion: gentle during debugging
```

---

## 13. Final Intended Roadmap

After stabilizing in-bucket liquid:

```text
1. Projection diagnostics
2. Moving boundary coupled projection
3. Collision softening
4. Hydrostatic/free-surface stabilization
5. Viscosity/cohesion/wetting
6. Better pressure solver
7. Hole outflow / jet transition
8. Airborne droplets
9. Canvas deposition
10. Sparse/tiled/adaptive performance optimization
11. Final validation/report/demo scenes
```

---

## 14. Codex Immediate Task Recommendation

Start with this task:

> Inspect the current `GpuMpmDenseLocalSolver.cs` and `GpuDenseMpmPrototype.compute`. Add robust projection diagnostics and then implement moving bucket boundary coupling inside the projection pipeline. Do not implement outflow yet. Ensure D3D11 UAV limits are respected. Ensure all helper functions are ordered before use. Make `KBucketCollision` remain as safety but reduce its hard compression behavior.

Expected first concrete deliverable:
- diagnostics stats,
- no shader compile errors,
- no UAV warnings,
- pressure still optional,
- measurable divergence before/after,
- ability to tune pressure without guessing.

---

## 15. Short Summary for Codex

We are building a Unity 6 GPU MLS-MPM-oriented paint simulation. The current in-bucket solver has APIC, quadratic B-spline transfer, material stress, rheology prototype, real bucket collision, projection grid, Jacobi pressure, and pressure gradient subtraction. However, the in-bucket paint still behaves poorly: it feels too light, rises too much, and compresses against bucket walls during strong bucket motion. The core issue is that pressure projection is still prototype-level and not fully coupled with the moving bucket boundary; hard particle collision runs after projection and can create compression. The immediate next work is diagnostics, moving-boundary-coupled projection, pressure/gravity scale stabilization, and collision softening. Do not proceed to hole outflow until in-bucket liquid is stable.
