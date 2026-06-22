using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Rope;
using UnityEngine;
using PaintBucketSim.Systems.Bucket;
using UnityEngine.InputSystem;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Boundary;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;

namespace PaintBucketSim.Systems.Diagnostics
{
    /// <summary>
    /// Simple on-screen diagnostics.
    /// This is intentionally lightweight for Phase 1.
    /// Later we will replace or extend it with a proper UI.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private RopeSystem ropeSystem;
        [SerializeField] private BucketSystem bucketSystem;
        [SerializeField] private RopeBucketCouplingSystem ropeBucketCouplingSystem;
        [SerializeField] private BoundarySystem boundarySystem;
        [SerializeField] private PaintFluidSystem paintFluidSystem;
        [SerializeField] private GpuFluidBufferSet gpuFluidBufferSet;

        [SerializeField] private bool visible = true;

        [Header("Style")]
        [SerializeField] private int fontSize = 16;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Vector2 panelPosition = new Vector2(15, 15);
        [SerializeField] private Vector2 panelSize = new Vector2(430, 220);

        [SerializeField] private GpuParticleIndirectRenderer gpuParticleRenderer;

        private GUIStyle _labelStyle;
        private GUIStyle _boxStyle;
        private Vector2 _scrollPosition;
        private bool _stylesInitialized;

        private void Awake()
        {
            if (simulationManager == null)
                simulationManager = FindFirstObjectByType<SimulationManager>();

            if (ropeSystem == null)
                ropeSystem = FindFirstObjectByType<RopeSystem>();

            if (bucketSystem == null)
                bucketSystem = FindFirstObjectByType<BucketSystem>();

            if (ropeBucketCouplingSystem == null)
                ropeBucketCouplingSystem = FindFirstObjectByType<RopeBucketCouplingSystem>();

            if (boundarySystem == null)
                boundarySystem = FindFirstObjectByType<BoundarySystem>();

            if (paintFluidSystem == null)
                paintFluidSystem = FindFirstObjectByType<PaintFluidSystem>();

            if (gpuParticleRenderer == null)
                gpuParticleRenderer = FindFirstObjectByType<GpuParticleIndirectRenderer>();

            if (gpuFluidBufferSet == null)
                gpuFluidBufferSet = FindFirstObjectByType<GpuFluidBufferSet>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || simulationManager == null || !simulationManager.IsInitialized)
                return;

            EnsureStyles();

            DiagnosticsFrame d = simulationManager.Context.Diagnostics;

            GUILayout.BeginArea(
                new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y),
                _boxStyle);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Label("Paint Bucket Simulation", _labelStyle);
            GUILayout.Space(5);

            GUILayout.Label($"Frame: {d.unityFrame}", _labelStyle);
            GUILayout.Label($"Paused: {d.paused}", _labelStyle);
            GUILayout.Label($"Simulation Time: {d.simulationTime:F3} s", _labelStyle);
            GUILayout.Label($"Fixed Step: {d.fixedStepIndex}", _labelStyle);
            GUILayout.Label($"Substep Index: {d.substepIndex}", _labelStyle);
            GUILayout.Label($"Fixed dt: {d.fixedDeltaTime:F5} s", _labelStyle);
            GUILayout.Label($"Substep dt: {d.substepDeltaTime:F5} s", _labelStyle);
            GUILayout.Label($"Substeps: {d.substeps}", _labelStyle);
            GUILayout.Label($"FPS: {d.fps:F1}", _labelStyle);
            GUILayout.Space(5);
            GUILayout.Label($"Gravity: {d.gravity}", _labelStyle);
            GUILayout.Label($"Wind: {d.windVelocity}", _labelStyle);

            if (ropeSystem != null && ropeSystem.IsInitialized)
            {
                RopeDiagnostics r = ropeSystem.Diagnostics;

                GUILayout.Space(8);
                GUILayout.Label("Rope Diagnostics", _labelStyle);
                GUILayout.Label($"Particles: {r.particleCount}", _labelStyle);
                GUILayout.Label($"Segments: {r.segmentCount}", _labelStyle);
                GUILayout.Label($"Current Length: {r.currentLength:F4} m", _labelStyle);
                GUILayout.Label($"Rest Length: {r.restLength:F4} m", _labelStyle);
                GUILayout.Label($"Max Stretch Error: {r.maxStretchError:E3} m", _labelStyle);
                GUILayout.Label($"Average Stretch Error: {r.averageStretchError:E3} m", _labelStyle);
                GUILayout.Label($"Max Strain: {r.maxStrain:F4}", _labelStyle);
                GUILayout.Label($"Max Tension Estimate: {r.maxTensionEstimate:F3}", _labelStyle);
                GUILayout.Label($"Broken: {r.isBroken != 0}", _labelStyle);

                if (r.isBroken != 0)
                {
                    GUILayout.Label($"Broken Segment: {r.brokenSegmentIndex}", _labelStyle);
                    GUILayout.Label($"Break Tension: {r.breakTension:F3}", _labelStyle);
                    GUILayout.Label($"Break Strain: {r.breakStrain:F3}", _labelStyle);
                }
            }

            if (bucketSystem != null && bucketSystem.IsInitialized)
            {
                BucketDiagnostics b = bucketSystem.Diagnostics;

                GUILayout.Space(8);
                GUILayout.Label("Bucket Diagnostics", _labelStyle);
                GUILayout.Label($"Speed: {b.speed:F4} m/s", _labelStyle);
                GUILayout.Label($"Angular Speed: {b.angularSpeed:F4} rad/s", _labelStyle);
                GUILayout.Label($"Linear KE: {b.kineticEnergyLinear:F4}", _labelStyle);
                GUILayout.Label($"Angular KE: {b.kineticEnergyAngular:F4}", _labelStyle);
                GUILayout.Label($"Attachment World: {b.attachmentWorldPosition}", _labelStyle);
                GUILayout.Label($"Attachment Velocity: {b.attachmentWorldVelocity}", _labelStyle);
                GUILayout.Label($"Hole Count: {b.holeCount}", _labelStyle);

                BucketHoleWorldState[] holes = bucketSystem.Holes;
                if (holes != null && holes.Length > 0)
                {
                    BucketHoleWorldState h = holes[0];
                    GUILayout.Label($"Hole[0] Pos: {h.worldCenter}", _labelStyle);
                    GUILayout.Label($"Hole[0] Normal: {h.worldNormal}", _labelStyle);
                    GUILayout.Label($"Hole[0] Radius: {h.radius:F4} m | Area: {h.area:E3}", _labelStyle);
                }
            }

            if (ropeBucketCouplingSystem != null)
            {
                RopeBucketCouplingDiagnostics c = ropeBucketCouplingSystem.Diagnostics;

                GUILayout.Space(8);
                GUILayout.Label("Rope-Bucket Coupling Diagnostics", _labelStyle);
                GUILayout.Label($"Enabled: {c.enabled != 0} | Active: {c.active != 0}", _labelStyle);
                GUILayout.Label($"Iterations: {c.iterations}", _labelStyle);
                GUILayout.Label($"Attachment Error: {c.attachmentError:E3} m", _labelStyle);
                GUILayout.Label($"Max Attachment Error: {c.maxAttachmentError:E3} m", _labelStyle);
                GUILayout.Label($"Estimated Constraint Force: {c.estimatedConstraintForce:F3}", _labelStyle);
                GUILayout.Label($"Rope Correction: {c.totalRopeCorrection}", _labelStyle);
                GUILayout.Label($"Bucket Linear Correction: {c.totalBucketLinearCorrection}", _labelStyle);
                GUILayout.Label($"Bucket Angular Correction: {c.totalBucketAngularCorrection}", _labelStyle);
            }

            if (boundarySystem != null && boundarySystem.IsInitialized)
            {
                BoundaryDiagnostics bd = boundarySystem.Diagnostics;

                GUILayout.Space(8);
                GUILayout.Label("Boundary Diagnostics", _labelStyle);
                GUILayout.Label($"Total Boundary Particles: {bd.totalCount}", _labelStyle);
                GUILayout.Label($"Wall: {bd.wallCount}", _labelStyle);
                GUILayout.Label($"Bottom: {bd.bottomCount}", _labelStyle);
                GUILayout.Label($"Hole Edge: {bd.holeEdgeCount}", _labelStyle);
                GUILayout.Label($"Spacing: {bd.particleSpacing:F4} m", _labelStyle);
            }

            if (paintFluidSystem != null && paintFluidSystem.IsInitialized)
            {
                FluidSolverStats ss = paintFluidSystem.SolverStats;

                GUILayout.Space(8);
                GUILayout.Label("Fluid Solver Architecture", _labelStyle);
                GUILayout.Label($"Solver: {ss.solverType}", _labelStyle);
                GUILayout.Label($"Status: {ss.status}", _labelStyle);
                GUILayout.Label($"Particles: {ss.particleCount}", _labelStyle);
                GUILayout.Label($"Iterations: {ss.solverIterations}", _labelStyle);
                GUILayout.Label($"Step Time: {ss.lastStepMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Density: {ss.lastDensitySolveMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Correction: {ss.lastCorrectionMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Boundary: {ss.lastBoundaryMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"XSPH: {ss.lastViscosityMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Fluid Hash Builds: {ss.fluidHashBuilds}", _labelStyle);
                GUILayout.Label($"Boundary Hash Builds: {ss.boundaryHashBuilds}", _labelStyle);
                GUILayout.Label($"Boundary Particles: {ss.usedBoundaryParticles}", _labelStyle);
                GUILayout.Label($"Analytic Projection: {ss.usedAnalyticProjection}", _labelStyle);
                GUILayout.Label($"XSPH Viscosity: {ss.usedXsphViscosity}", _labelStyle);

                GUILayout.Label($"GPU Grid: {ss.gpuGridResolutionX} x {ss.gpuGridResolutionY} x {ss.gpuGridResolutionZ}", _labelStyle);
                GUILayout.Label($"GPU Grid Nodes: {ss.gpuGridNodeCount}", _labelStyle);
                GUILayout.Label($"GPU Cell Size: {ss.gpuCellSizeMeters:F4} m", _labelStyle);
                GUILayout.Label($"GPU Dispatch Count: {ss.gpuDispatchCount}", _labelStyle);

                FluidParticlePoolStats ps = paintFluidSystem.PoolStats;

                GUILayout.Space(8);
                GUILayout.Label("Particle Pool / States", _labelStyle);
                GUILayout.Label($"Active: {ps.activeCount} / {ps.capacity}", _labelStyle);
                GUILayout.Label($"Inactive: {ps.inactiveCount}", _labelStyle);
                GUILayout.Label($"Usage: {(ps.poolUsage01 * 100.0f):F1} %", _labelStyle);
                GUILayout.Label($"Next Particle ID: {ps.nextParticleId}", _labelStyle);

                GUILayout.Label($"Inside: {ps.insideFluidCount}", _labelStyle);
                GUILayout.Label($"Near Boundary: {ps.nearBoundaryCount}", _labelStyle);
                GUILayout.Label($"Near Hole: {ps.nearHoleCount}", _labelStyle);

                GUILayout.Label($"Jet: {ps.jetCount}", _labelStyle);
                GUILayout.Label($"Emitted: {ps.emittedCount}", _labelStyle);
                GUILayout.Label($"Airborne: {ps.airborneCount}", _labelStyle);
                GUILayout.Label($"Spilled: {ps.spilledCount}", _labelStyle);

                GUILayout.Label($"Deposited: {ps.depositedCount}", _labelStyle);
                GUILayout.Label($"Absorbed: {ps.absorbedCount}", _labelStyle);
                GUILayout.Label($"Lost: {ps.lostCount}", _labelStyle);
            }

            if (gpuFluidBufferSet != null)
            {
                GpuFluidBufferStats bs = gpuFluidBufferSet.Stats;

                GUILayout.Space(8);
                GUILayout.Label("GPU Fluid Buffers", _labelStyle);
                GUILayout.Label($"Initialized: {bs.initialized}", _labelStyle);
                GUILayout.Label($"Enabled: {bs.enabled}", _labelStyle);
                GUILayout.Label($"Capacity: {bs.capacity}", _labelStyle);
                GUILayout.Label($"Uploaded: {bs.uploadedParticles}", _labelStyle);
                GUILayout.Label($"Upload Stride: {bs.uploadStride}", _labelStyle);
                GUILayout.Label($"Upload Frame: {bs.uploadFrame}", _labelStyle);
                GUILayout.Label($"CPU Upload: {bs.cpuUploadMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Compute Post: {bs.computePostProcessMilliseconds:F3} ms", _labelStyle);

                GUILayout.Label($"PositionRadius Buffer: {bs.positionRadiusBufferReady}", _labelStyle);
                GUILayout.Label($"VelocityMass Buffer: {bs.velocityMassBufferReady}", _labelStyle);
                GUILayout.Label($"Color Buffer: {bs.colorBufferReady}", _labelStyle);
                GUILayout.Label($"StateAgeId Buffer: {bs.stateAgeIdBufferReady}", _labelStyle);

                GUILayout.Label($"Compute Post Enabled: {bs.computePostProcessEnabled}", _labelStyle);
                GUILayout.Label($"State Debug Colors: {bs.debugColorByStateEnabled}", _labelStyle);

                //////////////////      G5 Changes      //////////////////
                GUILayout.Label($"Affine C0 Buffer: {bs.affineC0BufferReady}", _labelStyle);
                GUILayout.Label($"Affine C1 Buffer: {bs.affineC1BufferReady}", _labelStyle);
                GUILayout.Label($"Affine C2 Buffer: {bs.affineC2BufferReady}", _labelStyle);
                //////////////////      End G5 Changes      //////////////////

                //////////////////      G6.A Changes      //////////////////
                GUILayout.Label($"Volume/J Buffer: {bs.volumeJBufferReady}", _labelStyle);
                GUILayout.Label($"F0 Buffer: {bs.deformationF0BufferReady}", _labelStyle);
                GUILayout.Label($"F1 Buffer: {bs.deformationF1BufferReady}", _labelStyle);
                GUILayout.Label($"F2 Buffer: {bs.deformationF2BufferReady}", _labelStyle);
                //////////////////      End G6.A Changes      //////////////////
            }

            if (gpuParticleRenderer != null)
            {
                GpuParticleRenderStats gs = gpuParticleRenderer.Stats;

                GUILayout.Space(8);
                GUILayout.Label("GPU Particle Rendering", _labelStyle);
                GUILayout.Label($"Initialized: {gs.initialized}", _labelStyle);
                GUILayout.Label($"Enabled: {gs.enabled}", _labelStyle);
                GUILayout.Label($"Uploaded: {gs.uploadedParticles} / {gs.maxRenderedParticles}", _labelStyle);
                GUILayout.Label($"Render Stride: {gs.renderStride}", _labelStyle);
                GUILayout.Label($"Upload Frame: {gs.uploadFrame}", _labelStyle);
                GUILayout.Label($"CPU Upload: {gs.cpuUploadMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"CPU Submit: {gs.cpuRenderSubmitMilliseconds:F3} ms", _labelStyle);
                GUILayout.Label($"Per Particle Color: {gs.usingPerParticleColor}", _labelStyle);
            }

            GUILayout.Space(5);
            GUILayout.Label("Controls: P = Pause | O = Single Step | R = Reset | F1 = Toggle Overlay", _labelStyle);

            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized)
                return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = textColor }
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            _stylesInitialized = true;
        }
    }
}