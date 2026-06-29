using UnityEngine;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;
using PaintMaterialConfig = PaintBucketSim.Configs.PaintMaterialConfig;

namespace PaintSim.Scripts.UnityBridge
{
    [DefaultExecutionOrder(2000)]
    public sealed class PaintSimulationHost : MonoBehaviour
    {
        [Header("MLS-MPM Source")]
        [SerializeField] private bool _autoFindMlsMpmSources = true;
        [SerializeField] private PaintFluidSystem _paintFluidSystem;
        [SerializeField] private GpuFluidBufferSet _gpuFluidBufferSet;

        [Header("Canvas")]
        [SerializeField] private PaintSurface _paintSurface;
        [SerializeField] private ComputeShader _surfaceImpactShader;
        [SerializeField] private bool _renderPaintSurface = true;

        [Header("Deposition Performance")]
        [SerializeField] private bool _depositInLateUpdate = true;
        [SerializeField] private bool _skipWhenNoAirDomainParticles = true;
        [SerializeField] private bool _depositOnlyAirDomainParticles = true;
        [SerializeField] private bool _markMlsMpmParticlesOnImpact = true;

        [Header("Paint Properties")]
        [SerializeField] private bool _syncPaintPropertiesFromMlsMpmMaterial = true;
        [SerializeField] private Color _paintColor = Color.red;
        [SerializeField] private float _paintDensity = 1200f;
        [SerializeField] private float _paintViscosity = 0.1f;
        [SerializeField] private float _paintSurfaceTension = 0.04f;
        [SerializeField] private float _materialViscositySampleShearRate = 20.0f;
        [SerializeField] private float _materialViscositySampleTemperature = 20.0f;

        private PaintProperties _paintProperties;
        private PaintDepositor _paintDepositor;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            SyncPaintPropertiesFromMlsMpmMaterial();
            InitPaintProperties();
            InitDepositor();
        }

        private void FixedUpdate()
        {
            if (!_depositInLateUpdate)
                DispatchMlsMpmDepositor();
        }

        private void LateUpdate()
        {
            if (_depositInLateUpdate)
                DispatchMlsMpmDepositor();
        }

        private void Update()
        {
            if (_renderPaintSurface)
                _paintSurface?.Render();
        }

        private void ResolveReferences()
        {
            if (!_autoFindMlsMpmSources)
                return;

            if (_paintFluidSystem == null)
                _paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

            if (_gpuFluidBufferSet == null)
                _gpuFluidBufferSet = FindAnyObjectByType<GpuFluidBufferSet>();

            if (_paintSurface == null)
                _paintSurface = FindAnyObjectByType<PaintSurface>();
        }

        private void SyncPaintPropertiesFromMlsMpmMaterial()
        {
            if (!_syncPaintPropertiesFromMlsMpmMaterial ||
                _paintFluidSystem == null ||
                _paintFluidSystem.MaterialConfig == null)
            {
                return;
            }

            PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;
            _paintColor = material.baseColor;
            _paintDensity = Mathf.Max(material.densityKgPerM3, 1.0f);
            _paintSurfaceTension = Mathf.Max(material.surfaceTensionNPerM, 0.0001f);
            _paintViscosity = Mathf.Max(
                material.EvaluateViscosity(
                    _materialViscositySampleShearRate,
                    _materialViscositySampleTemperature
                ),
                0.0001f
            );
        }

        private void InitPaintProperties()
        {
            _paintProperties = PaintProperties.Create(
                density: _paintDensity,
                dynamicViscosity: _paintViscosity,
                surfaceTension: _paintSurfaceTension,
                paintColor: _paintColor
            );
        }

        private void InitDepositor()
        {
            if (_surfaceImpactShader == null)
                _surfaceImpactShader =
                    Resources.Load<ComputeShader>("ComputeShaders/Impact/SurfaceImpact");

            if (_surfaceImpactShader == null)
            {
                Debug.LogError("[PaintSimulationHost] SurfaceImpact shader is missing.");
                return;
            }

            if (_paintSurface == null || _paintSurface.PaintFilmGrid == null)
            {
                Debug.LogError("[PaintSimulationHost] PaintSurface/PaintFilmGrid is missing.");
                return;
            }

            _paintDepositor = new PaintDepositor(
                _surfaceImpactShader,
                _paintSurface.PaintFilmGrid,
                _paintProperties
            );
        }

        private void DispatchMlsMpmDepositor()
        {
            if (_paintDepositor == null ||
                _paintSurface == null ||
                _gpuFluidBufferSet == null ||
                !_gpuFluidBufferSet.IsInitialized)
            {
                return;
            }

            int count = _gpuFluidBufferSet.UploadedParticleCount;
            if (count <= 0)
                return;

            if (_skipWhenNoAirDomainParticles &&
                _paintFluidSystem != null &&
                _paintFluidSystem.IsGpuSolverActive)
            {
                FluidSolverStats stats = _paintFluidSystem.SolverStats;
                int airDomainCount =
                    stats.gpuJetParticleCount +
                    stats.gpuAirborneParticleCount +
                    stats.gpuOutflowTransitionCount;

                if (airDomainCount <= 0)
                    return;
            }

            _paintDepositor.DispatchFromMlsMpmBuffers(
                _gpuFluidBufferSet.PositionRadiusBuffer,
                _gpuFluidBufferSet.VelocityMassBuffer,
                _gpuFluidBufferSet.ColorBuffer,
                _gpuFluidBufferSet.StateAgeIdBuffer,
                count,
                _paintSurface.SurfaceProperties,
                _markMlsMpmParticlesOnImpact,
                _depositOnlyAirDomainParticles
            );
        }

        private void OnValidate()
        {
            _paintDensity = Mathf.Max(_paintDensity, 1.0f);
            _paintViscosity = Mathf.Max(_paintViscosity, 0.0001f);
            _paintSurfaceTension = Mathf.Max(_paintSurfaceTension, 0.0001f);
            _materialViscositySampleShearRate =
                Mathf.Max(_materialViscositySampleShearRate, 0.0f);
        }
    }
}
