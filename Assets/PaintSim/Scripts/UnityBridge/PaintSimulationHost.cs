using UnityEngine;
using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Pipeline;
using PaintSim.Scripts.Stages.DFSPH;
using PaintSim.Scripts.Stages.Exit;
using PaintSim.Scripts.Stages.Surface;
using PaintSim.Scripts.Rendering;

namespace PaintSim.Scripts.UnityBridge
{
    public class PaintSimulationHost : MonoBehaviour
    {
        // ─────────────────────────────────────────
        [Header("Paint Configuration")]
        [SerializeField] private Color _paintColor          = Color.red;
        [SerializeField] private float _paintDensity        = 1200f;
        [SerializeField] private float _paintViscosity      = 0.1f;
        [SerializeField] private float _paintSurfaceTension = 0.04f;

        // ─────────────────────────────────────────
        [Header("Simulation Configuration")]
        [SerializeField] private SimulationConfig _simulationConfig;

        // ─────────────────────────────────────────
        [Header("Orifice Settings")]
        [SerializeField] private float _exitSpeed            = 1.0f;
        [SerializeField] private float _exitRadius           = 0.01f;
        [SerializeField] private float _dischargeCoefficient = 0.8f;

        // ─────────────────────────────────────────
        [Header("Particle Rendering")]
        [SerializeField] private Shader _particleShader;
        [SerializeField] private float  _particleSize = 8.0f;
        [SerializeField] private float  _softness     = 4.0f;
        [SerializeField] private float  _stretch      = 2.0f;
        [SerializeField] private float  _glow         = 0.5f;

        // ─────────────────────────────────────────
        [Header("Surface")]
        [SerializeField] private PaintSurface      _paintSurface;        // ✅ اسحب الـ Plane هنا
        [SerializeField] private ComputeShader     _surfaceImpactShader;

        // ─────────────────────────────────────────
        // Private fields
        // ─────────────────────────────────────────
        private SimulationPipeline _pipeline;
        private BufferManager      _bufferManager;
        private PaintProperties    _paintProperties;
        private ParticleRenderer   _particleRenderer;
        private PaintDepositor     _paintDepositor;

        // ─────────────────────────────────────────
        private void Start()
        {
            InitPaintProperties();
            InitBuffers();
            InitPipeline();
            InitParticleRenderer();
            InitDepositor();
            WriteExitState();
        }

        // ─────────────────────────────────────────
        private void InitPaintProperties()
        {
            _paintProperties = PaintProperties.Create(
                density:          _paintDensity,
                dynamicViscosity: _paintViscosity,
                surfaceTension:   _paintSurfaceTension,
                paintColor:       _paintColor
            );
        }

        // ─────────────────────────────────────────
        private void InitBuffers()
        {
            _bufferManager = new BufferManager(
                maxParticles: _simulationConfig.MaxParticles,
                maxNeighbors: _simulationConfig.MaxNeighborsPerParticle,
                gridWidth:    _simulationConfig.GridWidth,
                gridHeight:   _simulationConfig.GridHeight
            );
        }

        // ─────────────────────────────────────────
        private void InitPipeline()
        {
            var exitModel = new OrificeExitModel(
                _bufferManager,
                _paintProperties
            );

            var spawner = new ParticleSpawner(
                _bufferManager,
                _paintProperties,
                _simulationConfig
            );

            var dfsph = new DFSPHSimulator(
                _bufferManager,
                _simulationConfig
            );

            _pipeline = new SimulationPipeline(
                _bufferManager,
                exitModel,
                spawner,
                dfsph
            );

            _pipeline.Initialize();
        }

        // ─────────────────────────────────────────
        private void InitParticleRenderer()
        {
            if (_particleShader == null)
            {
                Debug.LogError("[PaintSimulationHost] ParticleShader is null");
                return;
            }

            _particleRenderer = new ParticleRenderer(
                _bufferManager,
                _particleShader
            );

            _particleRenderer.SetParticleSize(_particleSize);
            _particleRenderer.SetSoftness(_softness);
            _particleRenderer.SetStretch(_stretch);
            _particleRenderer.SetGlow(_glow);
        }

        // ─────────────────────────────────────────
        private void InitDepositor()
        {
            if (_surfaceImpactShader == null)
            {
                Debug.LogError("[PaintSimulationHost] SurfaceImpactShader is null");
                return;
            }

            if (_paintSurface == null)
            {
                Debug.LogError("[PaintSimulationHost] PaintSurface is null — " +
                               "اسحب الـ Plane للـ _paintSurface");
                return;
            }

            _paintDepositor = new PaintDepositor(
                shader:          _surfaceImpactShader,
                bufferManager:   _bufferManager,
                paintFilmGrid:   _paintSurface.PaintFilmGrid,
                paintProperties: _paintProperties,
                config:          _simulationConfig
            );

            Debug.Log("[PaintSimulationHost] Depositor initialized ✓");
        }

        // ─────────────────────────────────────────
        private void FixedUpdate()
        {
            WriteExitState();
            _pipeline?.Tick(Time.fixedDeltaTime);

            // ✅ يمرر نوع السطح للـ Depositor
            if (_paintDepositor != null && _paintSurface != null)
            {
                _paintDepositor.Dispatch(
                    _bufferManager.GetActiveParticleCount(),
                    _paintSurface.SurfaceProperties
                );
            }
        }

        // ─────────────────────────────────────────
        private void Update()
        {
            _particleRenderer?.Render();

            // ✅ PaintSurface يدير الـ Rendering الخاص به
            _paintSurface?.Render();
        }

        // ─────────────────────────────────────────
        private void OnDestroy()
        {
            _particleRenderer?.Cleanup();
            _pipeline?.Cleanup();
        }

        // ─────────────────────────────────────────
        private void WriteExitState()
        {
            var exitBuffer = _bufferManager.GetBuffer(BufferType.ExitStateBuffer);
            var state = OrificeExitState.Create(
                exitPosition:         transform.position,
                exitDirection:        -transform.up,
                exitSpeed:            _exitSpeed,
                exitRadius:           _exitRadius,
                dischargeCoefficient: _dischargeCoefficient,
                density:              _paintProperties.Density
            );
            exitBuffer.SetData(new OrificeExitState[] { state });
        }

        // ─────────────────────────────────────────
        private void OnValidate()
        {
            if (_particleRenderer != null)
            {
                _particleRenderer.SetParticleSize(_particleSize);
                _particleRenderer.SetSoftness(_softness);
                _particleRenderer.SetStretch(_stretch);
                _particleRenderer.SetGlow(_glow);
            }
        }
    }
}