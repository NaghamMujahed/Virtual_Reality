# PROJECT_MAP.md
# Paint Bucket Simulation — VR Now

---

## [TECH_STACK]

| Layer | Technology | Version | Notes |
|---|---|---|---|
| Engine | Unity | 6000.4.4f1 | LTS |
| Language | C# | 9.0 / .NET 4.7.1 | |
| Render | URP | 17.4.0 | |
| Input | com.unity.inputsystem | 1.19.0 | New Input System only — no legacy Input |
| GPU Compute | Unity Compute Shaders (HLSL) | Built-in | RWStructuredBuffer SoA layout |
| Physics | Custom PBD (Position-Orientation Based Dynamics) | — | NO Unity Physics on rope |
| Reference | Kugelstadt & Schömer 2016 (SCA/SIGGRAPH) | — | Paper/Position and Orientation Based Cosserat Rods.md |
| Reference | Adaptively Sampled Geometry | — | Paper/Adaptively Sampled.md — Part 3 |
| Reference | Signed Distance Fields | — | Paper/A double layer method... .md — Part 3 |

---

## [SYSTEM_FLOW]

```
[Part 1 — Active]

INPUT (New Input System)
  └─ PlayerInput.Player.Move → _topAnchor.position delta

SIMULATION LOOP (per FixedUpdate, dt=10ms)
  │
  ├─ 1. CPU: Pin anchors
  │        _predictions[0]  = topAnchor.position   (InvMass = 0)
  │        _predictions[N]  = bucket.AttachPoint    (InvMass = 0)
  │
  ├─ 2. GPU: Predict kernel [N+1 threads]
  │        v += dt * gravity
  │        p  = x + dt * v
  │        ω += dt * I⁻¹ * τ
  │        u  = normalize(q + 0.5*dt * q⊗ω)
  │
  ├─ 3. GPU: Solver iterations (default: 8)
  │        for iter in solverIterations:
  │          SolveStretchShear(offset=0)   [N/2 threads — even elements]
  │          SolveStretchShear(offset=1)   [N/2 threads — odd elements]
  │          SolveBendTwist(offset=0)      [(N-1)/2 threads — even joints]
  │          SolveBendTwist(offset=1)      [(N-1)/2 threads — odd joints]
  │          NormalizeQuaternions          [N threads]
  │
  ├─ 4. GPU: UpdateVelocities kernel
  │        v = (p - x) / dt
  │        x = p
  │        ω = Im(2 * conj(q) ⊗ u / dt)
  │        q = u
  │
  └─ 5. CPU: RopeRenderer reads _Positions → LineRenderer.SetPositions()
             BucketController.SetPosition(_positions[N])
```

---

## [ARCHITECTURE]

### Part 1 — Rope + Bucket + Movement (ACTIVE)

```
Assets/Simulation/
├── CosseratRod.compute          GPU: all PBD kernels
│     #pragma kernel Predict
│     #pragma kernel SolveStretchShear   (param: _Offset 0/1)
│     #pragma kernel SolveBendTwist      (param: _Offset 0/1)
│     #pragma kernel NormalizeQuats
│     #pragma kernel UpdateVelocities
│
├── RopeSimulation.cs            MonoBehaviour — owns ALL ComputeBuffers
│     SoA buffers: _Positions, _Predictions, _Velocities, _InvMasses
│                  _Quats, _QuatPreds, _AngVelocities, _RestQuats, _QuatInvW
│     Drives: AllocBuffers → InitState → [Update: PinAnchors + RunSimulation]
│     Config: [Serializable] struct Config { segments, segLen, stiffness, dt, ... }
│
├── RopeRenderer.cs              Reads _Positions buffer → LineRenderer
│
└── BucketController.cs          Exposes AttachPoint (world space)
                                 SetPosition(Vector3) drives transform
                                 Receives movement input (keyboard/VR controller)
```

### SoA Memory Layout (N segments → N+1 particles + N quaternions)

| Buffer | Type | Count | Purpose |
|---|---|---|---|
| _Positions | float3 | N+1 | Current positions x |
| _Predictions | float3 | N+1 | Predicted positions p |
| _Velocities | float3 | N+1 | Linear velocities v |
| _InvMasses | float | N+1 | 1/m (0 = pinned) |
| _Quats | float4 | N | Frame quaternions q |
| _QuatPreds | float4 | N | Predicted quaternions u |
| _AngVelocities | float3 | N | Angular velocities ω |
| _RestQuats | float4 | N | Rest pose quaternions q⁰ |
| _QuatInvW | float | N | Scalar inertia weight w_q |

### Constraint Even/Odd Split (GPU-safe parallel Gauss-Seidel)

```
Stretch-Shear, offset=0:  element 0 → particles(0,1)+quat(0)
                           element 2 → particles(2,3)+quat(2)   ← NO shared data
Stretch-Shear, offset=1:  element 1 → particles(1,2)+quat(1)
                           element 3 → particles(3,4)+quat(3)   ← NO shared data

Bend-Twist, offset=0:     joint 1 → quats(0,1)
                           joint 3 → quats(2,3)                 ← NO shared data
Bend-Twist, offset=1:     joint 2 → quats(1,2)
                           joint 4 → quats(3,4)                 ← NO shared data
```

### Rope-Bucket Coupling

- **Direction**: one-way (rope drives bucket).
- **Mechanism**: CPU sets `_predictions[N] = bucket.AttachPoint` before GPU solve.
  After solve: `bucket.SetPosition(_positions[N])`.
- **Bucket weight**: encoded as `_invMasses[N]` — heavier bucket = lower value = less correction.
- Two-way Rigidbody coupling deferred to Part 2 if needed for paint splash dynamics.

---

### Part 2 — Paint Inside Bucket (PENDING)

- Volume simulation of paint inside bucket cavity.
- Candidate: SPH (Smoothed Particle Hydrodynamics) on GPU, or texture-based fluid volume.
- Paper reference: Adaptively Sampled Geometry (mesh-adaptive sampling).
- Trigger condition: bucket tilt angle > threshold → paint begins to slosh.

### Part 3 — Paint Outside Bucket + Canvas Staining (PENDING)

- Paint exits bucket based on overflow/tilt physics from Part 2.
- Canvas staining: SDF-based splatter decal system.
- Paper reference: A double layer method for constructing signed distance fields.
- Candidate: RenderTexture stamping + SDF distance field for wet/dry blending.

---

## [MILESTONES — Part 1]

| # | Goal | Verifiable Check |
|---|---|---|
| M1 | GPU buffers init, gravity only | N+1 spheres/line falls under gravity, no constraint |
| M2 | Stretch-Shear constraints active | Rope holds shape, segment distances preserved ±5% |
| M3 | Bend-Twist constraints active | Rope curls when top is rotated, twist propagates visually |
| M4 | Bucket attached to rope end | Bucket swings from rope, follows rope-end position |
| M5 | Player input moves top anchor | Bucket oscillates with natural lag and inertia |

---

## [ORPHANS & PENDING]

- `BuildScript.cs` — CLI build entry point, does not exist yet
- `Assets/TutorialInfo/` — Unity scaffolding, can be deleted when ready
- XR plugin for specific headsets (Meta/HTC) — not in manifest.json yet
- Rope self-collision — explicitly out of scope for Part 1
- Two-way rope↔bucket force coupling — deferred to Part 2
- Collision with environment geometry — deferred post-Part 1
- Adaptive rope resolution (LOD) — future, ref: Adaptively Sampled paper
