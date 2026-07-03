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
        private const float MaterialViscositySampleShearRate = 20.0f;
        private const float MaterialViscositySampleTemperature = 20.0f;

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

        [Header("Material Source")]
        [SerializeField] private bool _syncPaintMaterialEveryFrame = true;
        [SerializeField] private bool _warnWhenGpuSolverPresetDiffersFromMaterial = true;

        [Header("Color Mixing / Pigments")]
        [SerializeField] private PaintColorMixingMode _colorMixingMode =
            PaintColorMixingMode.KubelkaMunkApprox;
        [SerializeField, Range(0.0f, 1.0f)] private float _pigmentMixStrength = 1.0f;
        [SerializeField, Range(0.001f, 0.35f)] private float _pigmentMinReflectance = 0.035f;
        [SerializeField, Range(1.0f, 64.0f)] private float _pigmentMaxKs = 18.0f;

        private PaintProperties _paintProperties;
        private PaintDepositor _paintDepositor;
        private float _paintDensity = 1200f;
        private float _paintViscosity = 0.1f;
        private float _paintSurfaceTension = 0.04f;
        private int _lastPaintMaterialSignature = int.MinValue;
        private bool _warnedMaterialPresetMismatch;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            SyncPaintPropertiesFromMlsMpmMaterial(true);
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

            if (_renderPaintSurface)
                _paintSurface?.Render();
        }

        private void Update()
        {
            if (_syncPaintMaterialEveryFrame)
                SyncPaintPropertiesFromMlsMpmMaterial(false);
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

        private bool SyncPaintPropertiesFromMlsMpmMaterial(bool force)
        {
            if (_paintFluidSystem == null ||
                _paintFluidSystem.MaterialConfig == null)
            {
                return false;
            }

            PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;
            int signature = ComputeMaterialSignature(material);
            if (!force && signature == _lastPaintMaterialSignature)
                return false;

            _lastPaintMaterialSignature = signature;
            _paintDensity = Mathf.Max(material.densityKgPerM3, 1.0f);
            _paintSurfaceTension = Mathf.Max(material.surfaceTensionNPerM, 0.0001f);
            _paintViscosity = Mathf.Max(
                material.EvaluateViscosity(
                    MaterialViscositySampleShearRate,
                    MaterialViscositySampleTemperature
                ),
                0.0001f
            );

            InitPaintProperties();
            _paintDepositor?.ConfigurePaintProperties(_paintProperties);
            ApplyMaterialRheologyToDepositor(material);
            ApplyMaterialFilmProfileToSurface(material);
            WarnIfGpuSolverPresetDiffersFromMaterial(material);
            return true;
        }

        private void InitPaintProperties()
        {
            _paintProperties = PaintProperties.Create(
                density: _paintDensity,
                dynamicViscosity: _paintViscosity,
                surfaceTension: _paintSurfaceTension,
                paintColor: Color.white
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
                ApplyMaterialRheologyToDepositor(material);
                ApplyMaterialFilmProfileToSurface(material);
            }

            ApplyColorMixingConfiguration();
        }

        private void ApplyMaterialRheologyToDepositor(PaintMaterialConfig material)
        {
            if (_paintDepositor == null || material == null)
                return;

            bool useCarreauYasuda =
                material.viscosityModel == PaintViscosityModel.CarreauYasuda ||
                material.viscosityModel == PaintViscosityModel.ShearThinningCarreau;

            _paintDepositor.ConfigureImpactRheology(
                useCarreauYasuda,
                material.zeroShearViscosityPaS,
                material.infiniteShearViscosityPaS,
                material.relaxationTimeSeconds,
                material.yasudaExponent,
                material.flowIndex,
                material.yieldStressPa
            );
        }

        private void ApplyMaterialFilmProfileToSurface(PaintMaterialConfig material)
        {
            if (_paintSurface == null || material == null)
                return;

            _paintSurface.ConfigureFilmMaterial(
                material.EvaluateSurfaceFilmProfile(
                    1.0f,
                    MaterialViscositySampleTemperature
                )
            );
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

            SyncPaintPropertiesFromMlsMpmMaterial(false);
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

        private int ComputeMaterialSignature(PaintMaterialConfig material)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + material.GetInstanceID();
                hash = hash * 31 + (int)material.materialPreset;
                hash = hash * 31 + (material.autoApplyMaterialPreset ? 1 : 0);
                hash = hash * 31 + Quantize(material.densityKgPerM3);
                hash = hash * 31 + (int)material.viscosityModel;
                hash = hash * 31 + Quantize(material.constantViscosityPaS);
                hash = hash * 31 + Quantize(material.zeroShearViscosityPaS);
                hash = hash * 31 + Quantize(material.infiniteShearViscosityPaS);
                hash = hash * 31 + Quantize(material.relaxationTimeSeconds);
                hash = hash * 31 + Quantize(material.yasudaExponent);
                hash = hash * 31 + Quantize(material.flowIndex);
                hash = hash * 31 + Quantize(material.yieldStressPa);
                hash = hash * 31 + Quantize(material.surfaceTensionNPerM);
                hash = hash * 31 + Quantize(material.dryingRatePerSecond);
                hash = hash * 31 + Quantize(material.absorptionRate);
                return hash;
            }
        }

        private static int Quantize(float value)
        {
            return Mathf.RoundToInt(value * 100000.0f);
        }

        private void WarnIfGpuSolverPresetDiffersFromMaterial(PaintMaterialConfig material)
        {
            if (!_warnWhenGpuSolverPresetDiffersFromMaterial ||
                _warnedMaterialPresetMismatch ||
                _paintFluidSystem == null ||
                _paintFluidSystem.GpuMpmConfig == null ||
                material == null)
            {
                return;
            }

            string materialPreset = material.materialPreset.ToString();
            string gpuPreset =
                _paintFluidSystem.GpuMpmConfig.paintMaterialPreset.ToString();

            if (materialPreset == "Custom" || gpuPreset == "Custom" ||
                materialPreset == gpuPreset)
            {
                return;
            }

            _warnedMaterialPresetMismatch = true;
            Debug.LogWarning(
                "[PaintSimulationHost] Material preset mismatch: " +
                $"PaintMaterialConfig={materialPreset}, " +
                $"GpuMpmSolverConfig={gpuPreset}. " +
                "PaintMaterialConfig is now the authoritative visual/rheology " +
                "source for deposition and board-film flow. Use the GPU preset " +
                "only as a solver-stability package, or match both presets."
            );
        }

        private void OnDestroy()
        {
            _paintDepositor?.Dispose();
            _paintDepositor = null;
        }
    }
}
