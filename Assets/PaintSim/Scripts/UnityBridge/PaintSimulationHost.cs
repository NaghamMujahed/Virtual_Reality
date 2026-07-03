using UnityEngine;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;
using PaintMaterialConfig = PaintBucketSim.Configs.PaintMaterialConfig;
using PaintViscosityModel = PaintBucketSim.Configs.PaintViscosityModel;

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
        [SerializeField, Min(1)] private int _depositEveryNFrames = 1;
        [SerializeField] private bool _skipWhenNoAirDomainParticles = true;
        [SerializeField] private bool _depositOnlyAirDomainParticles = true;
        [SerializeField] private bool _acceptFluidDomainSurfaceHits = true;
        [SerializeField] private bool _markMlsMpmParticlesOnImpact = true;

        [Header("Surface Impact Diagnostics")]
        [SerializeField] private bool _enableSurfaceImpactDiagnostics = true;
        [SerializeField, Min(1)] private int _diagnosticLogEveryNFrames = 120;

        [Header("Paint Properties")]
        [SerializeField] private bool _syncPaintPropertiesFromMlsMpmMaterial = true;
        [SerializeField] private Color _paintColor = Color.red;
        [SerializeField] private float _paintDensity = 1200f;
        [SerializeField] private float _paintViscosity = 0.1f;
        [SerializeField] private float _paintSurfaceTension = 0.04f;
        [SerializeField] private float _materialViscositySampleShearRate = 20.0f;
        [SerializeField] private float _materialViscositySampleTemperature = 20.0f;

        [Header("Color Mixing / Pigments")]
        [SerializeField] private PaintColorMixingMode _colorMixingMode =
            PaintColorMixingMode.KubelkaMunkApprox;
        [SerializeField, Range(0.0f, 1.0f)] private float _pigmentMixStrength = 1.0f;
        [SerializeField, Range(0.001f, 0.35f)] private float _pigmentMinReflectance = 0.035f;
        [SerializeField, Range(1.0f, 64.0f)] private float _pigmentMaxKs = 18.0f;

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

            if (_paintFluidSystem != null &&
                _paintFluidSystem.MaterialConfig != null)
            {
                PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;
                _paintDepositor.ConfigureImpactRheology(
                    material.viscosityModel == PaintViscosityModel.CarreauYasuda,
                    material.zeroShearViscosityPaS,
                    material.infiniteShearViscosityPaS,
                    material.relaxationTimeSeconds,
                    material.yasudaExponent,
                    material.flowIndex,
                    material.yieldStressPa
                );

                _paintSurface.ConfigureFilmMaterial(
                    material.densityKgPerM3,
                    material.EvaluateViscosity(1.0f, _materialViscositySampleTemperature),
                    material.surfaceTensionNPerM,
                    material.yieldStressPa
                );
            }

            ApplyColorMixingConfiguration();
        }

        private void ApplyColorMixingConfiguration()
        {
            _paintDepositor?.ConfigureColorMixing(
                _colorMixingMode,
                _pigmentMixStrength,
                _pigmentMinReflectance,
                _pigmentMaxKs
            );
            _paintSurface?.ConfigureColorMixing(
                _colorMixingMode,
                _pigmentMixStrength,
                _pigmentMinReflectance,
                _pigmentMaxKs
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

            if (Time.frameCount % Mathf.Max(1, _depositEveryNFrames) != 0)
                return;

            _paintSurface.RefreshSurfaceFrameFromTransform();
            ApplyColorMixingConfiguration();

            if (_skipWhenNoAirDomainParticles &&
                !_acceptFluidDomainSurfaceHits &&
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
                _gpuFluidBufferSet.VolumeJBuffer,
                count,
                _paintSurface.SurfaceProperties,
                _markMlsMpmParticlesOnImpact,
                _depositOnlyAirDomainParticles,
                _acceptFluidDomainSurfaceHits,
                _enableSurfaceImpactDiagnostics,
                (_depositInLateUpdate ? Time.deltaTime : Time.fixedDeltaTime) *
                    Mathf.Max(1, _depositEveryNFrames)
            );

            if (_enableSurfaceImpactDiagnostics &&
                Time.frameCount % Mathf.Max(1, _diagnosticLogEveryNFrames) == 0)
            {
                SurfaceImpactDiagnostics diagnostics = _paintDepositor.ReadDiagnostics();
                Debug.Log($"[PaintSimulationHost] SurfaceImpact diagnostics: {diagnostics}");
            }
        }

        private void OnValidate()
        {
            _paintDensity = Mathf.Max(_paintDensity, 1.0f);
            _paintViscosity = Mathf.Max(_paintViscosity, 0.0001f);
            _paintSurfaceTension = Mathf.Max(_paintSurfaceTension, 0.0001f);
            _materialViscositySampleShearRate =
                Mathf.Max(_materialViscositySampleShearRate, 0.0f);
            _pigmentMixStrength = Mathf.Clamp01(_pigmentMixStrength);
            _pigmentMinReflectance = Mathf.Clamp(
                _pigmentMinReflectance,
                0.001f,
                0.35f
            );
            _pigmentMaxKs = Mathf.Clamp(_pigmentMaxKs, 1.0f, 64.0f);
            _depositEveryNFrames = Mathf.Max(1, _depositEveryNFrames);
            _diagnosticLogEveryNFrames = Mathf.Max(1, _diagnosticLogEveryNFrames);
        }

        private void OnDestroy()
        {
            _paintDepositor?.Dispose();
            _paintDepositor = null;
        }
    }
}
