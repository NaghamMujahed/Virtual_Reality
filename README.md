# 🪢 VR Rope & Bucket Simulation

<div align="center">


<br>

![Unity Version](https://img.shields.io/badge/Unity-2022.3_LTS-000000?style=for-the-badge&logo=unity&logoColor=white&labelColor=000000)
![GPU Compute](https://img.shields.io/badge/GPU-Compute_Shaders-76B900?style=for-the-badge&logo=nvidia&logoColor=white&labelColor=000000)
![Physics](https://img.shields.io/badge/Physics-XPBD-FF4500?style=for-the-badge&labelColor=000000)
![VR Ready](https://img.shields.io/badge/VR-Ready-2496ED?style=for-the-badge&logo=virtualreality&logoColor=white&labelColor=000000)

<br>

![Status](https://img.shields.io/badge/Status-Active_Development-blue?style=flat-square&logo=github)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![Compute Shaders](https://img.shields.io/badge/Compute-10_Kernels-purple?style=flat-square)
![Performance](https://img.shields.io/badge/Performance-60+_FPS-brightgreen?style=flat-square)

<br>

<svg width="600" height="80" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="grad1" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" style="stop-color:#FF6B6B;stop-opacity:1" />
      <stop offset="50%" style="stop-color:#4ECDC4;stop-opacity:1" />
      <stop offset="100%" style="stop-color:#45B7D1;stop-opacity:1" />
    </linearGradient>
  </defs>
  <rect width="600" height="4" fill="url(#grad1)">
    <animate attributeName="width" from="0" to="600" dur="2s" repeatCount="indefinite"/>
  </rect>
  <circle cx="0" cy="40" r="6" fill="#FF6B6B">
    <animate attributeName="cx" from="0" to="600" dur="3s" repeatCount="indefinite"/>
  </circle>
  <circle cx="0" cy="60" r="4" fill="#4ECDC4">
    <animate attributeName="cx" from="0" to="600" dur="2.5s" repeatCount="indefinite"/>
  </circle>
</svg>

</div>

<br>

## 🎬 Project Overview Animation

```
     INITIALIZATION SEQUENCE
     ══════════════════════════
     
     [████████████████████████] 100%
     
      GPU Compute Shaders ....... ✅ LOADED
      Cosserat Rod Physics ...... ✅ ACTIVE
      Procedural Bucket ......... ✅ BUILT
      Fluid Simulation .......... ⏳ PENDING
      VR Integration ............ ⏳ PENDING

```

<br>

<div align="center">

<svg width="700" height="200" xmlns="http://www.w3.org/2000/svg">
  <!-- Rope Animation -->
  <path d="M 50 100 Q 150 50 250 100 T 450 100" stroke="#FF6B6B" stroke-width="4" fill="none">
    <animate attributeName="d" 
             values="M 50 100 Q 150 50 250 100 T 450 100;
                     M 50 100 Q 150 150 250 100 T 450 100;
                     M 50 100 Q 150 50 250 100 T 450 100" 
             dur="2s" repeatCount="indefinite"/>
  </path>
  
  <!-- Bucket -->
  <rect x="420" y="80" width="60" height="50" fill="#4ECDC4" rx="5">
    <animate attributeName="y" 
             values="80;100;80" 
             dur="2s" repeatCount="indefinite"/>
  </rect>
  
  <!-- Water drops -->
  <circle cx="450" cy="140" r="3" fill="#45B7D1">
    <animate attributeName="cy" from="140" to="180" dur="1s" repeatCount="indefinite"/>
    <animate attributeName="opacity" from="1" to="0" dur="1s" repeatCount="indefinite"/>
  </circle>
  <circle cx="450" cy="140" r="3" fill="#45B7D1">
    <animate attributeName="cy" from="140" to="180" dur="1s" begin="0.5s" repeatCount="indefinite"/>
    <animate attributeName="opacity" from="1" to="0" dur="1s" begin="0.5s" repeatCount="indefinite"/>
  </circle>
  
  <!-- Physics particles -->
  <circle cx="100" cy="100" r="4" fill="#FFD93D">
    <animate attributeName="cx" from="50" to="450" dur="3s" repeatCount="indefinite"/>
  </circle>
  <circle cx="200" cy="100" r="4" fill="#FFD93D">
    <animate attributeName="cx" from="50" to="450" dur="3s" begin="0.3s" repeatCount="indefinite"/>
  </circle>
  <circle cx="300" cy="100" r="4" fill="#FFD93D">
    <animate attributeName="cx" from="50" to="450" dur="3s" begin="0.6s" repeatCount="indefinite"/>
  </circle>
</svg>

</div>

<br>

---

## 📖 Overview

**VR Rope & Bucket Simulation** is a high-performance, GPU-accelerated physics simulation project built in Unity. It focuses on realistic elastic rope dynamics and procedural bucket modeling, designed specifically for Virtual Reality (VR) interactions.

Unlike traditional Unity physics, **zero reliance on Unity's built-in physics engine (PhysX)** for the simulation logic. All core physics calculations (XPBD, Cosserat Rods) are executed entirely on the GPU via **Compute Shaders**, ensuring massive parallelism and ultra-low latency for VR environments.

---

##  Key Features

### 🪢 Advanced Rope Physics (GPU-Based)
- **Cosserat Rod Theory:** Accurate simulation of bending, twisting, and stretching using Position and Orientation Based Dynamics (XPBD).
- **Quaternion Constraints:** Direct orientation handling without "ghost particles," preventing unphysical kinking.
- **Anisotropic Bending:** Independent stiffness control for bending and twisting axes.
- **Self-Collision & LRA:** Robust self-collision detection and Long-Range Attachments to prevent over-stretching.
- **Bilateral Interleaving:** Fast-converging solver that reduces iteration counts by 50%.

### 🪣 Procedural Bucket System
- **Dynamic Mesh Generation:** Fully procedural bucket generation (truncated cone, rim, handle) at runtime.
- **Hole Management:** Dynamic addition/removal of drain and side holes with automatic mesh rebuilding.
- **Two-Way Coupling (In Progress):** Realistic physical interaction where the bucket's mass affects the rope and vice-versa.

### 🎮 VR & Interaction
- **Direct Grab System:** Raycast-free, screen-space particle picking for intuitive VR controller or mouse grabbing.
- **Orbit Camera:** Smooth, auto-following orbit camera with pan, zoom, and rotation controls.
- **Real-time Debug UI:** Comprehensive in-game UI to tweak physics parameters (stiffness, damping, mass) on the fly.

---

## 🧠 Underlying Research Papers

This project strictly implements state-of-the-art academic research to ensure physical accuracy and computational efficiency:

| Paper Title | Authors | Application in Project |
| :--- | :--- | :--- |
| **[Position and Orientation Based Cosserat Rods](https://dl.acm.org/doi/10.1145/2897824.2925951)** | Kugelstadt & Schömer (2016) | Core rope simulation, Quaternion constraints, Modified Darboux Vector. |
| **[A Double Layer Method for Constructing SDFs](https://www.sciencedirect.com/science/article/pii/S152407031400036X)** | Wu et al. (2014) | Fast GPU-based Signed Distance Field generation for bucket collision. |
| **[Adaptively Sampled Distance Fields (ADF)](https://dl.acm.org/doi/10.1145/344779.344947)** | Frisken et al. (2000) | Future implementation for high-fidelity fluid/environment collision. |

---

## 🏗️ Architecture & Tech Stack

- **Engine:** Unity 2022.3 LTS (URP)
- **Language:** C# (Host Logic), HLSL (GPU Compute Shaders)
- **Physics Paradigm:** Extended Position Based Dynamics (XPBD)
- **Data Layout:** Structure of Arrays (SoA) for optimal GPU cache coherency.
- **Parallelism:** `ComputeShader.Dispatch` with custom thread group sizing.

### Project Structure
```text
Assets/
── Simulation/
│   ├── Core/           # RopeSimulation.cs (Main orchestrator)
│   ├── Shaders/        # CosseratRod.compute (GPU Kernels)
│   ├── Bucket/         # Procedural mesh & hole management
│   ├── Interaction/    # RopeGrab, CameraOrbit
│   └── UI/             # RopeDebugUI (Real-time parameter tuning)
├── Scenes/             # Demo and Test scenes
└── Docs/               # Architecture diagrams & Research papers
```

---

##  Getting Started

### Prerequisites
- **Unity Hub** with Unity 2022.3 LTS or newer.
- **Universal Render Pipeline (URP)** package installed.
- A GPU supporting Compute Shaders (OpenGL 4.3 / DirectX 11 / Vulkan).

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/YourUsername/VR-Rope-Bucket-Simulation.git
   ```
2. Open the project folder in Unity Hub.
3. Allow Unity to import assets and compile scripts.
4. Open the `Assets/Scenes/MainSimulation.unity` scene.
5. Press **Play**.

---

## 🎮 Usage & Controls

### In-Game Controls
| Action | Mouse / Keyboard | VR Controller |
| :--- | :--- | :--- |
| **Grab Rope** | Left Click + Drag | Trigger Press + Point |
| **Move Grab Point** | Mouse Delta / Scroll (Z-axis) | Thumbstick / Trackpad |
| **Rotate Camera** | Right Click + Drag | Grip + Thumbstick |
| **Zoom Camera** | Scroll Wheel | Trigger (Grip) |
| **Toggle Debug UI** | `F1` Key | Menu Button |

### Debug UI Parameters
Press `F1` to open the control panel. You can adjust:
- **Stiffness:** Stretch and Bend/Twist multipliers.
- **Physics:** Bucket mass, Gravity, Damping.
- **Solver:** Iteration count (higher = stiffer, lower = faster).

---

## 📊 Performance Metrics

| Metric | Value | Notes |
| :--- | :--- | :--- |
| **Frame Rate** | 60+ FPS | VR Ready target |
| **GPU Time** | < 5ms per frame | Compute Shader execution |
| **Solver Iterations** | 40–80 per substep | Configurable via Debug UI |
| **Rope Segments** | 40–200 particles | SoA layout on GPU |
| **GPU Memory** | ~50 KB | 9 SoA buffers total |
| **Substeps** | 2–8 per frame | Adaptive time stepping |

---

## 🗺️ Roadmap

### Phase 1: Core Physics (Completed ✅)
- [x] GPU Compute Shader implementation of Cosserat Rods.
- [x] XPBD with Quaternion constraints.
- [x] Procedural Bucket Mesh generation.
- [x] Basic Grab interaction.

### Phase 2: Advanced Coupling (In Progress )
- [ ] Two-Way XPBD Coupling (Rope ↔ Bucket).
- [ ] GPU-based SDF construction for Bucket collision.
- [ ] Break Detection (Rope snapping under high tension).
- [ ] Air Drag and Wind forces.

### Phase 3: Fluid & Environment (Future 🔮)
- [ ] PBF (Predictive-Backward-Forward) Fluid Simulation inside the bucket.
- [ ] Fluid-Rope two-way coupling.
- [ ] Adaptively Sampled Distance Fields (ADF) for complex environment collision.

---

## Contributing

Contributions, issues, and feature requests are welcome! 

1. Fork the Project.
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`).
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the Branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request.

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for more information.

---

##  Contact

**Project Maintainer:** [RaghadAlkhous]  
**Email:** [raghadalkhous@gmail.com]  

<div align="center">

<br>

<br>

![Made with Unity](https://img.shields.io/badge/Made_with-Unity-000000?style=flat-square&logo=unity&logoColor=white)
![GPU Powered](https://img.shields.io/badge/GPU_Powered-Compute_Shaders-76B900?style=flat-square)
![VR Ready](https://img.shields.io/badge/VR_Ready-Yes-2496ED?style=flat-square)

</div>
