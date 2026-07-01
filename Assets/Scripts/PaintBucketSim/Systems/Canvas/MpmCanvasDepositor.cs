using System;
using PaintBucketSim.Configs;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Fluid.GPU;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintBucketSim.Systems.Canvas
{
    public struct MpmCanvasDepositorStats
    {
        public bool initialized;
        public bool enabled;
        public int particleCount;
        public int dispatchFrame;
        public int checkedParticles;
        public int airStateParticles;
        public int planeHits;
        public int boundsHits;
        public int depositedParticles;
        public int absorbedParticles;
        public int stickImpacts;
        public int spreadImpacts;
        public int splashImpacts;
        public int bounceImpacts;
        public int surfaceParticlesSpawned;
        public int dropletsSpawned;
        public int surfaceParticlesDeposited;
        public int dropletsDeposited;
        public int visibleDropletsReused;
        public int splashReusedParticles;
        public int bounceReusedParticles;
        public int spreadReusedParticles;
        public int surfaceParticlesActive;
        public int internalDropletsActive;
    }

    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public class MpmCanvasDepositor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MpmCanvasPaintSurface surface;
        [SerializeField] private GpuFluidBufferSet gpuBufferSet;
        [SerializeField] private PaintFluidSystem paintFluidSystem;
        [SerializeField] private ComputeShader canvasImpactCompute;

        [Header("Deposition")]
        [SerializeField] private bool enableDeposition = true;
        [SerializeField] private bool requireGpuMpmSolver = true;
        [Min(1)] [SerializeField] private int dispatchEveryNFrames = 1;
        [SerializeField] private bool doubleSidedCollision = true;
        [Min(0.0f)] [SerializeField] private float collisionSkinMeters = 0.0015f;
        [Min(0.0f)] [SerializeField] private float minimumImpactSpeed = 0.03f;
        [Range(0.0f, 1.5f)] [SerializeField] private float depositionEfficiency = 0.92f;

        [Header("Splat Shape")]
        [Range(0.25f, 8.0f)] [SerializeField] private float baseSplatRadiusCells = 1.2f;
        [Range(0.0f, 8.0f)] [SerializeField] private float impactSpreadMultiplier = 2.2f;
        [Range(0.0f, 10.0f)] [SerializeField] private float tangentialStretchMultiplier = 3.4f;
        [Range(1, 12)] [SerializeField] private int maxSplatRadiusCells = 8;
        [Min(1)] [SerializeField] private int maxDepositUnitsPerParticle = 600000;

        [Header("Particle After Hit")]
        [SerializeField] private bool shrinkDepositedParticles = true;
        [Min(0.000001f)] [SerializeField] private float depositedParticleRadiusMeters = 0.00005f;

        [Header("Hybrid Post-Impact Particles")]
        [SerializeField] private bool enableHybridParticles = true;
        [Min(1)] [SerializeField] private int surfaceParticleCapacity = 8192;
        [Min(1)] [SerializeField] private int dropletParticleCapacity = 4096;
        [Range(0.05f, 5.0f)] [SerializeField] private float surfaceParticleLifetimeSeconds = 1.35f;
        [Range(0.05f, 5.0f)] [SerializeField] private float dropletLifetimeSeconds = 0.9f;
        [Range(0.05f, 4.0f)] [SerializeField] private float surfaceParticleDepositRate = 0.9f;
        [Range(0.0f, 12.0f)] [SerializeField] private float surfaceParticleFrictionPerSecond = 4.0f;
        [Range(0.0f, 12.0f)] [SerializeField] private float dropletDragPerSecond = 2.2f;
        [Min(0.0f)] [SerializeField] private float splashNormalSpeedThreshold = 2.0f;
        [Min(0.0f)] [SerializeField] private float bounceNormalSpeedThreshold = 1.2f;
        [Range(0.0f, 0.8f)] [SerializeField] private float secondaryDropletMassFraction = 0.18f;
        [Range(0.0f, 8.0f)] [SerializeField] private float secondaryDropletVelocityBoost = 1.4f;
        [Range(0, 8)] [SerializeField] private int maxSecondaryDropletsPerImpact = 4;
        [Range(0.0f, 0.8f)] [SerializeField] private float bounceRestitution = 0.18f;
        [Min(0.0f)] [SerializeField] private float gravityAcceleration = 9.81f;
        [SerializeField] private Vector3 gravityDirectionWorld = Vector3.down;

        [Header("Visible MPM Droplets")]
        [SerializeField] private bool enableVisibleParticleReuse = true;
        [Range(0.0f, 1.0f)] [SerializeField] private float splashVisibleReuseProbability = 0.72f;
        [Range(0.0f, 1.0f)] [SerializeField] private float strongSpreadVisibleReuseProbability = 0.24f;
        [Range(0.0f, 1.0f)] [SerializeField] private float bounceVisibleReuseProbability = 0.88f;
        [Range(0.01f, 0.65f)] [SerializeField] private float visibleDropletMassFraction = 0.18f;
        [Range(0.10f, 0.95f)] [SerializeField] private float visibleDropletRadiusScale = 0.42f;
        [Range(0.0f, 1.0f)] [SerializeField] private float visibleDropletVelocityDamping = 0.68f;
        [Range(0.0f, 3.0f)] [SerializeField] private float visibleDropletNormalBoost = 0.55f;
        [Range(0.5f, 5.0f)] [SerializeField] private float visibleDropletSurfaceOffsetRadii = 1.8f;
        [Min(0.0f)] [SerializeField] private float strongSpreadVisibleSpeed = 1.15f;
        [Min(0.000001f)] [SerializeField] private float visibleDropletMinRadiusMeters = 0.00045f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugReadback = false;
        [Min(1)] [SerializeField] private int debugReadbackInterval = 15;
        [SerializeField] private bool showDebugOverlay = false;
        [SerializeField] private Vector2 debugOverlayPosition = new Vector2(470.0f, 15.0f);

        private GraphicsBuffer _previousPositionRadiusBuffer;
        private GraphicsBuffer _surfaceParticleBuffer;
        private GraphicsBuffer _dropletParticleBuffer;
        private GraphicsBuffer _hybridCountersBuffer;
        private GraphicsBuffer _debugCountersBuffer;
        private MpmCanvasHybridParticleGpu[] _zeroHybridParticles;
        private uint[] _hybridCounters;
        private uint[] _debugCounters;

        private int _previousCapacity;
        private int _surfaceCapacity;
        private int _dropletCapacity;
        private bool _needsInitialCapture = true;
        private bool _debugReadbackPending;
        private int _lastDebugReadbackFrame = -1;

        private int _kernelDeposit = -1;
        private int _kernelEvolveHybridParticles = -1;
        private int _kernelCapturePrevious = -1;
        private int _kernelClearCounters = -1;
        private MpmCanvasPaintSurface _subscribedSurface;

        private MpmCanvasDepositorStats _stats;

        private static readonly int ID_ParticlePositionRadius = Shader.PropertyToID("_ParticlePositionRadius");
        private static readonly int ID_ParticlePositionRadiusRead = Shader.PropertyToID("_ParticlePositionRadiusRead");
        private static readonly int ID_ParticleVelocityMass = Shader.PropertyToID("_ParticleVelocityMass");
        private static readonly int ID_ParticleColor = Shader.PropertyToID("_ParticleColor");
        private static readonly int ID_ParticleStateAgeId = Shader.PropertyToID("_ParticleStateAgeId");
        private static readonly int ID_PreviousPositionRadius = Shader.PropertyToID("_PreviousPositionRadius");
        private static readonly int ID_PreviousPositionRadiusWrite = Shader.PropertyToID("_PreviousPositionRadiusWrite");
        private static readonly int ID_FilmCells = Shader.PropertyToID("_FilmCells");
        private static readonly int ID_SurfaceParticles = Shader.PropertyToID("_SurfaceParticles");
        private static readonly int ID_DropletParticles = Shader.PropertyToID("_DropletParticles");
        private static readonly int ID_HybridCounters = Shader.PropertyToID("_HybridCounters");
        private static readonly int ID_DebugCounters = Shader.PropertyToID("_DebugCounters");
        private static readonly int ID_ParticleCount = Shader.PropertyToID("_ParticleCount");
        private static readonly int ID_SurfaceParticleCapacity = Shader.PropertyToID("_SurfaceParticleCapacity");
        private static readonly int ID_DropletParticleCapacity = Shader.PropertyToID("_DropletParticleCapacity");
        private static readonly int ID_GridWidth = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_ThicknessUnitsPerMeter = Shader.PropertyToID("_ThicknessUnitsPerMeter");
        private static readonly int ID_WetnessUnits = Shader.PropertyToID("_WetnessUnits");
        private static readonly int ID_ColorWeightScale = Shader.PropertyToID("_ColorWeightScale");
        private static readonly int ID_MaxDepositUnitsPerParticle = Shader.PropertyToID("_MaxDepositUnitsPerParticle");
        private static readonly int ID_CanvasCurrentPosition = Shader.PropertyToID("_CanvasCurrentPosition");
        private static readonly int ID_CanvasCurrentNormal = Shader.PropertyToID("_CanvasCurrentNormal");
        private static readonly int ID_CanvasCurrentTangent = Shader.PropertyToID("_CanvasCurrentTangent");
        private static readonly int ID_CanvasCurrentBitangent = Shader.PropertyToID("_CanvasCurrentBitangent");
        private static readonly int ID_CanvasPreviousPosition = Shader.PropertyToID("_CanvasPreviousPosition");
        private static readonly int ID_CanvasPreviousNormal = Shader.PropertyToID("_CanvasPreviousNormal");
        private static readonly int ID_CanvasLinearVelocity = Shader.PropertyToID("_CanvasLinearVelocity");
        private static readonly int ID_CanvasAngularVelocity = Shader.PropertyToID("_CanvasAngularVelocity");
        private static readonly int ID_FlipU = Shader.PropertyToID("_FlipU");
        private static readonly int ID_FlipV = Shader.PropertyToID("_FlipV");
        private static readonly int ID_CanvasWidth = Shader.PropertyToID("_CanvasWidth");
        private static readonly int ID_CanvasHeight = Shader.PropertyToID("_CanvasHeight");
        private static readonly int ID_CellSizeU = Shader.PropertyToID("_CellSizeU");
        private static readonly int ID_CellSizeV = Shader.PropertyToID("_CellSizeV");
        private static readonly int ID_DeltaTime = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_DoubleSidedCollision = Shader.PropertyToID("_DoubleSidedCollision");
        private static readonly int ID_CollisionSkin = Shader.PropertyToID("_CollisionSkin");
        private static readonly int ID_MinimumImpactSpeed = Shader.PropertyToID("_MinimumImpactSpeed");
        private static readonly int ID_DepositionEfficiency = Shader.PropertyToID("_DepositionEfficiency");
        private static readonly int ID_BaseSplatRadiusCells = Shader.PropertyToID("_BaseSplatRadiusCells");
        private static readonly int ID_ImpactSpreadMultiplier = Shader.PropertyToID("_ImpactSpreadMultiplier");
        private static readonly int ID_TangentialStretchMultiplier = Shader.PropertyToID("_TangentialStretchMultiplier");
        private static readonly int ID_MaxSplatRadiusCells = Shader.PropertyToID("_MaxSplatRadiusCells");
        private static readonly int ID_AbsorptionRate = Shader.PropertyToID("_AbsorptionRate");
        private static readonly int ID_SpreadFactor = Shader.PropertyToID("_SpreadFactor");
        private static readonly int ID_DripFactor = Shader.PropertyToID("_DripFactor");
        private static readonly int ID_Roughness = Shader.PropertyToID("_Roughness");
        private static readonly int ID_SplatSharpness = Shader.PropertyToID("_SplatSharpness");
        private static readonly int ID_PaintDensity = Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_PaintViscosity = Shader.PropertyToID("_PaintViscosity");
        private static readonly int ID_SurfaceTension = Shader.PropertyToID("_SurfaceTension");
        private static readonly int ID_YieldStress = Shader.PropertyToID("_YieldStress");
        private static readonly int ID_ShrinkDepositedParticles = Shader.PropertyToID("_ShrinkDepositedParticles");
        private static readonly int ID_DepositedParticleRadius = Shader.PropertyToID("_DepositedParticleRadius");
        private static readonly int ID_EnableHybridParticles = Shader.PropertyToID("_EnableHybridParticles");
        private static readonly int ID_SurfaceParticleLifetime = Shader.PropertyToID("_SurfaceParticleLifetime");
        private static readonly int ID_DropletLifetime = Shader.PropertyToID("_DropletLifetime");
        private static readonly int ID_SurfaceParticleDepositRate = Shader.PropertyToID("_SurfaceParticleDepositRate");
        private static readonly int ID_SurfaceParticleFriction = Shader.PropertyToID("_SurfaceParticleFriction");
        private static readonly int ID_DropletDrag = Shader.PropertyToID("_DropletDrag");
        private static readonly int ID_SplashNormalSpeedThreshold = Shader.PropertyToID("_SplashNormalSpeedThreshold");
        private static readonly int ID_BounceNormalSpeedThreshold = Shader.PropertyToID("_BounceNormalSpeedThreshold");
        private static readonly int ID_SecondaryDropletMassFraction = Shader.PropertyToID("_SecondaryDropletMassFraction");
        private static readonly int ID_SecondaryDropletVelocityBoost = Shader.PropertyToID("_SecondaryDropletVelocityBoost");
        private static readonly int ID_MaxSecondaryDroplets = Shader.PropertyToID("_MaxSecondaryDroplets");
        private static readonly int ID_BounceRestitution = Shader.PropertyToID("_BounceRestitution");
        private static readonly int ID_GravityAcceleration = Shader.PropertyToID("_GravityAcceleration");
        private static readonly int ID_GravityDirection = Shader.PropertyToID("_GravityDirection");
        private static readonly int ID_EnableVisibleParticleReuse = Shader.PropertyToID("_EnableVisibleParticleReuse");
        private static readonly int ID_SplashVisibleReuseProbability = Shader.PropertyToID("_SplashVisibleReuseProbability");
        private static readonly int ID_StrongSpreadVisibleReuseProbability = Shader.PropertyToID("_StrongSpreadVisibleReuseProbability");
        private static readonly int ID_BounceVisibleReuseProbability = Shader.PropertyToID("_BounceVisibleReuseProbability");
        private static readonly int ID_VisibleDropletMassFraction = Shader.PropertyToID("_VisibleDropletMassFraction");
        private static readonly int ID_VisibleDropletRadiusScale = Shader.PropertyToID("_VisibleDropletRadiusScale");
        private static readonly int ID_VisibleDropletVelocityDamping = Shader.PropertyToID("_VisibleDropletVelocityDamping");
        private static readonly int ID_VisibleDropletNormalBoost = Shader.PropertyToID("_VisibleDropletNormalBoost");
        private static readonly int ID_VisibleDropletSurfaceOffsetRadii = Shader.PropertyToID("_VisibleDropletSurfaceOffsetRadii");
        private static readonly int ID_StrongSpreadVisibleSpeed = Shader.PropertyToID("_StrongSpreadVisibleSpeed");
        private static readonly int ID_VisibleDropletMinRadius = Shader.PropertyToID("_VisibleDropletMinRadius");

        public MpmCanvasDepositorStats Stats => _stats;

        private void Awake()
        {
            ResolveReferences();
            ResolveKernels();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResolveKernels();
            SubscribeSurfaceReset();
            _needsInitialCapture = true;
        }

        private void OnDisable()
        {
            UnsubscribeSurfaceReset();
            ReleaseBuffers();
        }

        private void OnDestroy()
        {
            UnsubscribeSurfaceReset();
            ReleaseBuffers();
        }

        private void LateUpdate()
        {
            DispatchDeposition();
        }

        [ContextMenu("Capture Current MPM Positions")]
        public void CaptureCurrentPositions()
        {
            ResolveReferences();
            if (!CanUseParticleBuffers(out int particleCount))
                return;

            EnsureGpuBuffers();
            DispatchCapturePrevious(particleCount);
            _needsInitialCapture = false;
        }

        [ContextMenu("Reset MPM Canvas Hybrid Particles")]
        public void ResetHybridParticles()
        {
            if (_surfaceParticleBuffer != null &&
                _zeroHybridParticles != null &&
                _zeroHybridParticles.Length >= _surfaceCapacity)
            {
                _surfaceParticleBuffer.SetData(_zeroHybridParticles, 0, 0, _surfaceCapacity);
            }

            if (_dropletParticleBuffer != null &&
                _zeroHybridParticles != null &&
                _zeroHybridParticles.Length >= _dropletCapacity)
            {
                _dropletParticleBuffer.SetData(_zeroHybridParticles, 0, 0, _dropletCapacity);
            }

            if (_hybridCountersBuffer != null && _hybridCounters != null)
            {
                Array.Clear(_hybridCounters, 0, _hybridCounters.Length);
                _hybridCountersBuffer.SetData(_hybridCounters);
            }

            _stats.surfaceParticlesSpawned = 0;
            _stats.dropletsSpawned = 0;
            _stats.surfaceParticlesDeposited = 0;
            _stats.dropletsDeposited = 0;
            _stats.visibleDropletsReused = 0;
            _stats.splashReusedParticles = 0;
            _stats.bounceReusedParticles = 0;
            _stats.spreadReusedParticles = 0;
            _stats.surfaceParticlesActive = 0;
            _stats.internalDropletsActive = 0;
            _needsInitialCapture = true;
        }

        public void DispatchDeposition()
        {
            ResolveReferences();

            _stats.enabled = enableDeposition;
            if (!enableDeposition)
                return;

            if (canvasImpactCompute == null ||
                _kernelDeposit < 0 ||
                _kernelCapturePrevious < 0 ||
                surface == null ||
                !CanUseParticleBuffers(out int particleCount))
            {
                _stats.initialized = false;
                return;
            }

            if (requireGpuMpmSolver &&
                paintFluidSystem != null &&
                !paintFluidSystem.IsGpuSolverActive)
            {
                _stats.initialized = false;
                return;
            }

            surface.EnsureResources();
            if (!surface.FilmGrid.IsValid)
                return;

            EnsureGpuBuffers();
            if (_previousPositionRadiusBuffer == null)
                return;

            if (_needsInitialCapture)
            {
                DispatchCapturePrevious(particleCount);
                _needsInitialCapture = false;
                _stats.initialized = true;
                _stats.particleCount = particleCount;
                return;
            }

            if (Time.frameCount % Mathf.Max(1, dispatchEveryNFrames) != 0)
                return;

            surface.RefreshFrameState(Time.deltaTime);
            DispatchClearCounters();

            BindDepositKernel(particleCount);
            canvasImpactCompute.Dispatch(_kernelDeposit, Groups(particleCount, 256), 1, 1);

            DispatchHybridEvolution();
            DispatchCapturePrevious(particleCount);
            RequestDebugReadbackIfDue();

            _stats.initialized = true;
            _stats.particleCount = particleCount;
            _stats.dispatchFrame = Time.frameCount;

            surface.CommitFrameState();
        }

        private void BindDepositKernel(int particleCount)
        {
            MpmPaintFilmGrid grid = surface.FilmGrid;
            MpmCanvasSurfaceFrame frame = surface.Frame;
            MpmCanvasSurfaceMaterialSettings surfaceMaterial = surface.MaterialSettings;
            ResolvePaintMaterial(
                out float density,
                out float viscosity,
                out float surfaceTension,
                out float yieldStress
            );

            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_ParticlePositionRadius, gpuBufferSet.PositionRadiusBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_ParticleVelocityMass, gpuBufferSet.VelocityMassBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_ParticleColor, gpuBufferSet.ColorBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_ParticleStateAgeId, gpuBufferSet.StateAgeIdBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_PreviousPositionRadius, _previousPositionRadiusBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_FilmCells, grid.CellBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_SurfaceParticles, _surfaceParticleBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_DropletParticles, _dropletParticleBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_HybridCounters, _hybridCountersBuffer);
            canvasImpactCompute.SetBuffer(_kernelDeposit, ID_DebugCounters, _debugCountersBuffer);

            canvasImpactCompute.SetInt(ID_ParticleCount, particleCount);
            canvasImpactCompute.SetInt(ID_SurfaceParticleCapacity, _surfaceCapacity);
            canvasImpactCompute.SetInt(ID_DropletParticleCapacity, _dropletCapacity);
            canvasImpactCompute.SetInt(ID_GridWidth, grid.Width);
            canvasImpactCompute.SetInt(ID_GridHeight, grid.Height);
            canvasImpactCompute.SetInt(ID_ThicknessUnitsPerMeter, MpmPaintFilmGrid.ThicknessUnitsPerMeter);
            canvasImpactCompute.SetInt(ID_WetnessUnits, MpmPaintFilmGrid.WetnessUnits);
            canvasImpactCompute.SetInt(ID_ColorWeightScale, MpmPaintFilmGrid.ColorWeightScale);
            canvasImpactCompute.SetInt(ID_MaxDepositUnitsPerParticle, maxDepositUnitsPerParticle);

            canvasImpactCompute.SetVector(ID_CanvasCurrentPosition, frame.position);
            canvasImpactCompute.SetVector(ID_CanvasCurrentNormal, frame.normal);
            canvasImpactCompute.SetVector(ID_CanvasCurrentTangent, frame.tangent);
            canvasImpactCompute.SetVector(ID_CanvasCurrentBitangent, frame.bitangent);
            canvasImpactCompute.SetVector(ID_CanvasPreviousPosition, frame.previousPosition);
            canvasImpactCompute.SetVector(ID_CanvasPreviousNormal, frame.previousNormal);
            canvasImpactCompute.SetVector(ID_CanvasLinearVelocity, frame.linearVelocity);
            canvasImpactCompute.SetVector(ID_CanvasAngularVelocity, frame.angularVelocity);
            canvasImpactCompute.SetInt(ID_FlipU, surface.FlipU ? 1 : 0);
            canvasImpactCompute.SetInt(ID_FlipV, surface.FlipV ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_CanvasWidth, frame.widthMeters);
            canvasImpactCompute.SetFloat(ID_CanvasHeight, frame.heightMeters);
            canvasImpactCompute.SetFloat(ID_CellSizeU, frame.widthMeters / Mathf.Max(grid.Width, 1));
            canvasImpactCompute.SetFloat(ID_CellSizeV, frame.heightMeters / Mathf.Max(grid.Height, 1));
            float stepDt = Mathf.Max(Mathf.Max(Time.deltaTime, Time.fixedDeltaTime), 1e-5f);
            canvasImpactCompute.SetFloat(ID_DeltaTime, stepDt);
            canvasImpactCompute.SetInt(ID_DoubleSidedCollision, doubleSidedCollision ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_CollisionSkin, collisionSkinMeters);
            canvasImpactCompute.SetFloat(ID_MinimumImpactSpeed, minimumImpactSpeed);
            canvasImpactCompute.SetFloat(ID_DepositionEfficiency, depositionEfficiency);

            canvasImpactCompute.SetFloat(ID_BaseSplatRadiusCells, baseSplatRadiusCells);
            canvasImpactCompute.SetFloat(ID_ImpactSpreadMultiplier, impactSpreadMultiplier);
            canvasImpactCompute.SetFloat(ID_TangentialStretchMultiplier, tangentialStretchMultiplier);
            canvasImpactCompute.SetInt(ID_MaxSplatRadiusCells, maxSplatRadiusCells);

            canvasImpactCompute.SetFloat(ID_AbsorptionRate, surfaceMaterial.absorptionRate);
            canvasImpactCompute.SetFloat(ID_SpreadFactor, surfaceMaterial.spreadFactor);
            canvasImpactCompute.SetFloat(ID_DripFactor, surfaceMaterial.dripFactor);
            canvasImpactCompute.SetFloat(ID_Roughness, surfaceMaterial.roughness);
            canvasImpactCompute.SetFloat(ID_SplatSharpness, surfaceMaterial.splatSharpness);

            canvasImpactCompute.SetFloat(ID_PaintDensity, density);
            canvasImpactCompute.SetFloat(ID_PaintViscosity, viscosity);
            canvasImpactCompute.SetFloat(ID_SurfaceTension, surfaceTension);
            canvasImpactCompute.SetFloat(ID_YieldStress, yieldStress);
            canvasImpactCompute.SetInt(ID_ShrinkDepositedParticles, shrinkDepositedParticles ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_DepositedParticleRadius, depositedParticleRadiusMeters);
            canvasImpactCompute.SetInt(ID_EnableHybridParticles, enableHybridParticles ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_SurfaceParticleLifetime, surfaceParticleLifetimeSeconds);
            canvasImpactCompute.SetFloat(ID_DropletLifetime, dropletLifetimeSeconds);
            canvasImpactCompute.SetFloat(ID_SurfaceParticleDepositRate, surfaceParticleDepositRate);
            canvasImpactCompute.SetFloat(ID_SurfaceParticleFriction, surfaceParticleFrictionPerSecond);
            canvasImpactCompute.SetFloat(ID_DropletDrag, dropletDragPerSecond);
            canvasImpactCompute.SetFloat(ID_SplashNormalSpeedThreshold, splashNormalSpeedThreshold);
            canvasImpactCompute.SetFloat(ID_BounceNormalSpeedThreshold, bounceNormalSpeedThreshold);
            canvasImpactCompute.SetFloat(ID_SecondaryDropletMassFraction, secondaryDropletMassFraction);
            canvasImpactCompute.SetFloat(ID_SecondaryDropletVelocityBoost, secondaryDropletVelocityBoost);
            canvasImpactCompute.SetInt(ID_MaxSecondaryDroplets, maxSecondaryDropletsPerImpact);
            canvasImpactCompute.SetFloat(ID_BounceRestitution, bounceRestitution);
            canvasImpactCompute.SetFloat(ID_GravityAcceleration, gravityAcceleration);
            canvasImpactCompute.SetVector(ID_GravityDirection, gravityDirectionWorld.normalized);
            canvasImpactCompute.SetInt(ID_EnableVisibleParticleReuse, enableVisibleParticleReuse ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_SplashVisibleReuseProbability, splashVisibleReuseProbability);
            canvasImpactCompute.SetFloat(ID_StrongSpreadVisibleReuseProbability, strongSpreadVisibleReuseProbability);
            canvasImpactCompute.SetFloat(ID_BounceVisibleReuseProbability, bounceVisibleReuseProbability);
            canvasImpactCompute.SetFloat(ID_VisibleDropletMassFraction, visibleDropletMassFraction);
            canvasImpactCompute.SetFloat(ID_VisibleDropletRadiusScale, visibleDropletRadiusScale);
            canvasImpactCompute.SetFloat(ID_VisibleDropletVelocityDamping, visibleDropletVelocityDamping);
            canvasImpactCompute.SetFloat(ID_VisibleDropletNormalBoost, visibleDropletNormalBoost);
            canvasImpactCompute.SetFloat(ID_VisibleDropletSurfaceOffsetRadii, visibleDropletSurfaceOffsetRadii);
            canvasImpactCompute.SetFloat(ID_StrongSpreadVisibleSpeed, strongSpreadVisibleSpeed);
            canvasImpactCompute.SetFloat(ID_VisibleDropletMinRadius, visibleDropletMinRadiusMeters);
        }

        private void DispatchHybridEvolution()
        {
            if (!enableHybridParticles ||
                canvasImpactCompute == null ||
                _kernelEvolveHybridParticles < 0 ||
                surface == null ||
                !surface.FilmGrid.IsValid ||
                _surfaceParticleBuffer == null ||
                _dropletParticleBuffer == null ||
                _hybridCountersBuffer == null)
            {
                return;
            }

            MpmPaintFilmGrid grid = surface.FilmGrid;
            MpmCanvasSurfaceFrame frame = surface.Frame;
            MpmCanvasSurfaceMaterialSettings surfaceMaterial = surface.MaterialSettings;
            ResolvePaintMaterial(
                out float density,
                out float viscosity,
                out float surfaceTension,
                out float yieldStress
            );

            int dispatchCount = Mathf.Max(_surfaceCapacity, _dropletCapacity);
            float stepDt = Mathf.Max(Mathf.Max(Time.deltaTime, Time.fixedDeltaTime), 1e-5f);

            canvasImpactCompute.SetBuffer(_kernelEvolveHybridParticles, ID_FilmCells, grid.CellBuffer);
            canvasImpactCompute.SetBuffer(_kernelEvolveHybridParticles, ID_SurfaceParticles, _surfaceParticleBuffer);
            canvasImpactCompute.SetBuffer(_kernelEvolveHybridParticles, ID_DropletParticles, _dropletParticleBuffer);
            canvasImpactCompute.SetBuffer(_kernelEvolveHybridParticles, ID_HybridCounters, _hybridCountersBuffer);
            canvasImpactCompute.SetBuffer(_kernelEvolveHybridParticles, ID_DebugCounters, _debugCountersBuffer);
            canvasImpactCompute.SetInt(ID_ParticleCount, dispatchCount);
            canvasImpactCompute.SetInt(ID_SurfaceParticleCapacity, _surfaceCapacity);
            canvasImpactCompute.SetInt(ID_DropletParticleCapacity, _dropletCapacity);
            canvasImpactCompute.SetInt(ID_GridWidth, grid.Width);
            canvasImpactCompute.SetInt(ID_GridHeight, grid.Height);
            canvasImpactCompute.SetInt(ID_ThicknessUnitsPerMeter, MpmPaintFilmGrid.ThicknessUnitsPerMeter);
            canvasImpactCompute.SetInt(ID_WetnessUnits, MpmPaintFilmGrid.WetnessUnits);
            canvasImpactCompute.SetInt(ID_ColorWeightScale, MpmPaintFilmGrid.ColorWeightScale);
            canvasImpactCompute.SetVector(ID_CanvasCurrentPosition, frame.position);
            canvasImpactCompute.SetVector(ID_CanvasCurrentNormal, frame.normal);
            canvasImpactCompute.SetVector(ID_CanvasCurrentTangent, frame.tangent);
            canvasImpactCompute.SetVector(ID_CanvasCurrentBitangent, frame.bitangent);
            canvasImpactCompute.SetInt(ID_FlipU, surface.FlipU ? 1 : 0);
            canvasImpactCompute.SetInt(ID_FlipV, surface.FlipV ? 1 : 0);
            canvasImpactCompute.SetFloat(ID_CanvasWidth, frame.widthMeters);
            canvasImpactCompute.SetFloat(ID_CanvasHeight, frame.heightMeters);
            canvasImpactCompute.SetFloat(ID_CellSizeU, frame.widthMeters / Mathf.Max(grid.Width, 1));
            canvasImpactCompute.SetFloat(ID_CellSizeV, frame.heightMeters / Mathf.Max(grid.Height, 1));
            canvasImpactCompute.SetFloat(ID_DeltaTime, stepDt);
            canvasImpactCompute.SetFloat(ID_AbsorptionRate, surfaceMaterial.absorptionRate);
            canvasImpactCompute.SetFloat(ID_SpreadFactor, surfaceMaterial.spreadFactor);
            canvasImpactCompute.SetFloat(ID_DripFactor, surfaceMaterial.dripFactor);
            canvasImpactCompute.SetFloat(ID_Roughness, surfaceMaterial.roughness);
            canvasImpactCompute.SetFloat(ID_SplatSharpness, surfaceMaterial.splatSharpness);
            canvasImpactCompute.SetFloat(ID_PaintDensity, density);
            canvasImpactCompute.SetFloat(ID_PaintViscosity, viscosity);
            canvasImpactCompute.SetFloat(ID_SurfaceTension, surfaceTension);
            canvasImpactCompute.SetFloat(ID_YieldStress, yieldStress);
            canvasImpactCompute.SetFloat(ID_SurfaceParticleDepositRate, surfaceParticleDepositRate);
            canvasImpactCompute.SetFloat(ID_SurfaceParticleFriction, surfaceParticleFrictionPerSecond);
            canvasImpactCompute.SetFloat(ID_DropletDrag, dropletDragPerSecond);
            canvasImpactCompute.SetFloat(ID_GravityAcceleration, gravityAcceleration);
            canvasImpactCompute.SetVector(ID_GravityDirection, gravityDirectionWorld.normalized);

            canvasImpactCompute.Dispatch(
                _kernelEvolveHybridParticles,
                Groups(dispatchCount, 256),
                1,
                1
            );
        }

        private void DispatchCapturePrevious(int particleCount)
        {
            if (canvasImpactCompute == null ||
                _kernelCapturePrevious < 0 ||
                gpuBufferSet == null ||
                _previousPositionRadiusBuffer == null)
            {
                return;
            }

            canvasImpactCompute.SetBuffer(_kernelCapturePrevious, ID_ParticlePositionRadiusRead, gpuBufferSet.PositionRadiusBuffer);
            canvasImpactCompute.SetBuffer(_kernelCapturePrevious, ID_PreviousPositionRadiusWrite, _previousPositionRadiusBuffer);
            canvasImpactCompute.SetInt(ID_ParticleCount, particleCount);
            canvasImpactCompute.Dispatch(_kernelCapturePrevious, Groups(particleCount, 256), 1, 1);
        }

        private void DispatchClearCounters()
        {
            if (_debugCountersBuffer == null ||
                canvasImpactCompute == null ||
                _kernelClearCounters < 0)
            {
                return;
            }

            canvasImpactCompute.SetBuffer(_kernelClearCounters, ID_DebugCounters, _debugCountersBuffer);
            canvasImpactCompute.Dispatch(_kernelClearCounters, 1, 1, 1);
        }

        private void RequestDebugReadbackIfDue()
        {
            if (!enableDebugReadback ||
                _debugCountersBuffer == null ||
                _debugReadbackPending ||
                Time.frameCount - _lastDebugReadbackFrame < debugReadbackInterval)
            {
                return;
            }

            _debugReadbackPending = true;
            _lastDebugReadbackFrame = Time.frameCount;

            AsyncGPUReadback.Request(_debugCountersBuffer, request =>
            {
                _debugReadbackPending = false;
                if (request.hasError)
                    return;

                var data = request.GetData<uint>();
                int count = Mathf.Min(data.Length, _debugCounters.Length);
                for (int i = 0; i < count; i++)
                    _debugCounters[i] = data[i];

                _stats.checkedParticles = count > 0 ? (int)_debugCounters[0] : 0;
                _stats.airStateParticles = count > 1 ? (int)_debugCounters[1] : 0;
                _stats.planeHits = count > 2 ? (int)_debugCounters[2] : 0;
                _stats.boundsHits = count > 3 ? (int)_debugCounters[3] : 0;
                _stats.depositedParticles = count > 4 ? (int)_debugCounters[4] : 0;
                _stats.absorbedParticles = count > 5 ? (int)_debugCounters[5] : 0;
                _stats.stickImpacts = count > 6 ? (int)_debugCounters[6] : 0;
                _stats.spreadImpacts = count > 7 ? (int)_debugCounters[7] : 0;
                _stats.splashImpacts = count > 8 ? (int)_debugCounters[8] : 0;
                _stats.bounceImpacts = count > 9 ? (int)_debugCounters[9] : 0;
                _stats.surfaceParticlesSpawned = count > 10 ? (int)_debugCounters[10] : 0;
                _stats.dropletsSpawned = count > 11 ? (int)_debugCounters[11] : 0;
                _stats.surfaceParticlesDeposited = count > 12 ? (int)_debugCounters[12] : 0;
                _stats.dropletsDeposited = count > 13 ? (int)_debugCounters[13] : 0;
                _stats.visibleDropletsReused = count > 14 ? (int)_debugCounters[14] : 0;
                _stats.splashReusedParticles = count > 15 ? (int)_debugCounters[15] : 0;
                _stats.bounceReusedParticles = count > 16 ? (int)_debugCounters[16] : 0;
                _stats.spreadReusedParticles = count > 17 ? (int)_debugCounters[17] : 0;
                _stats.surfaceParticlesActive = count > 18 ? (int)_debugCounters[18] : 0;
                _stats.internalDropletsActive = count > 19 ? (int)_debugCounters[19] : 0;
            });
        }

        private void EnsureGpuBuffers()
        {
            int capacity = gpuBufferSet != null ? Mathf.Max(1, gpuBufferSet.Capacity) : 0;
            if (capacity <= 0)
                return;

            int requestedSurfaceCapacity = Mathf.Max(1, surfaceParticleCapacity);
            int requestedDropletCapacity = Mathf.Max(1, dropletParticleCapacity);

            if (_previousPositionRadiusBuffer != null &&
                _previousCapacity == capacity &&
                _surfaceCapacity == requestedSurfaceCapacity &&
                _dropletCapacity == requestedDropletCapacity &&
                _surfaceParticleBuffer != null &&
                _dropletParticleBuffer != null &&
                _hybridCountersBuffer != null &&
                _debugCountersBuffer != null)
            {
                return;
            }

            ReleaseBuffers();

            _previousCapacity = capacity;
            _surfaceCapacity = requestedSurfaceCapacity;
            _dropletCapacity = requestedDropletCapacity;
            _previousPositionRadiusBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                sizeof(float) * 4
            );

            _zeroHybridParticles = new MpmCanvasHybridParticleGpu[
                Mathf.Max(_surfaceCapacity, _dropletCapacity)
            ];
            _surfaceParticleBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _surfaceCapacity,
                MpmPaintFilmGrid.HybridParticleStrideBytes
            );
            _dropletParticleBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _dropletCapacity,
                MpmPaintFilmGrid.HybridParticleStrideBytes
            );
            _surfaceParticleBuffer.SetData(_zeroHybridParticles, 0, 0, _surfaceCapacity);
            _dropletParticleBuffer.SetData(_zeroHybridParticles, 0, 0, _dropletCapacity);

            _hybridCounters = new uint[8];
            _hybridCountersBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _hybridCounters.Length,
                sizeof(uint)
            );
            _hybridCountersBuffer.SetData(_hybridCounters);

            _debugCounters = new uint[32];
            _debugCountersBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _debugCounters.Length,
                sizeof(uint)
            );
            _debugCountersBuffer.SetData(_debugCounters);
            _needsInitialCapture = true;
        }

        private void ReleaseBuffers()
        {
            if (_previousPositionRadiusBuffer != null)
            {
                _previousPositionRadiusBuffer.Release();
                _previousPositionRadiusBuffer = null;
            }

            if (_surfaceParticleBuffer != null)
            {
                _surfaceParticleBuffer.Release();
                _surfaceParticleBuffer = null;
            }

            if (_dropletParticleBuffer != null)
            {
                _dropletParticleBuffer.Release();
                _dropletParticleBuffer = null;
            }

            if (_hybridCountersBuffer != null)
            {
                _hybridCountersBuffer.Release();
                _hybridCountersBuffer = null;
            }

            if (_debugCountersBuffer != null)
            {
                _debugCountersBuffer.Release();
                _debugCountersBuffer = null;
            }

            _zeroHybridParticles = null;
            _hybridCounters = null;
            _debugCounters = null;
            _previousCapacity = 0;
            _surfaceCapacity = 0;
            _dropletCapacity = 0;
            _debugReadbackPending = false;
            _needsInitialCapture = true;
        }

        private bool CanUseParticleBuffers(out int particleCount)
        {
            particleCount = 0;

            if (gpuBufferSet == null ||
                !gpuBufferSet.IsInitialized ||
                gpuBufferSet.PositionRadiusBuffer == null ||
                gpuBufferSet.VelocityMassBuffer == null ||
                gpuBufferSet.ColorBuffer == null ||
                gpuBufferSet.StateAgeIdBuffer == null)
            {
                return false;
            }

            particleCount = gpuBufferSet.UploadedParticleCount;
            return particleCount > 0;
        }

        private void ResolveReferences()
        {
            if (surface == null)
                surface = GetComponent<MpmCanvasPaintSurface>();

            if (surface == null)
                surface = FindAnyObjectByType<MpmCanvasPaintSurface>();

            SubscribeSurfaceReset();

            if (gpuBufferSet == null)
                gpuBufferSet = FindAnyObjectByType<GpuFluidBufferSet>();

            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();
        }

        private void SubscribeSurfaceReset()
        {
            if (_subscribedSurface == surface)
                return;

            UnsubscribeSurfaceReset();

            if (surface == null)
                return;

            _subscribedSurface = surface;
            _subscribedSurface.SurfaceReset += HandleSurfaceReset;
        }

        private void UnsubscribeSurfaceReset()
        {
            if (_subscribedSurface == null)
                return;

            _subscribedSurface.SurfaceReset -= HandleSurfaceReset;
            _subscribedSurface = null;
        }

        private void HandleSurfaceReset()
        {
            ResetHybridParticles();
        }

        private void ResolveKernels()
        {
            _kernelDeposit = FindKernelSafe(canvasImpactCompute, "KDepositImpacts");
            _kernelEvolveHybridParticles = FindKernelSafe(canvasImpactCompute, "KEvolveHybridParticles");
            _kernelCapturePrevious = FindKernelSafe(canvasImpactCompute, "KCapturePreviousPositions");
            _kernelClearCounters = FindKernelSafe(canvasImpactCompute, "KClearDebugCounters");
        }

        private void ResolvePaintMaterial(
            out float density,
            out float viscosity,
            out float surfaceTension,
            out float yieldStress)
        {
            density = 1050.0f;
            viscosity = 1.0f;
            surfaceTension = 0.035f;
            yieldStress = 0.0f;

            if (paintFluidSystem == null)
                return;

            PaintMaterialConfig material = paintFluidSystem.MaterialConfig;
            if (material != null)
            {
                density = Mathf.Max(material.densityKgPerM3, 1.0f);
                viscosity = Mathf.Max(material.EvaluateViscosity(1.0f, 20.0f), 0.0001f);
                surfaceTension = Mathf.Max(material.surfaceTensionNPerM, 0.0001f);
                yieldStress = Mathf.Max(material.yieldStressPa, 0.0f);
            }

            GpuMpmSolverConfig gpuConfig = paintFluidSystem.GpuMpmConfig;
            if (gpuConfig != null && gpuConfig.enablePaintRheology)
            {
                viscosity = Mathf.Max(viscosity, gpuConfig.lowShearViscosity);
                yieldStress = Mathf.Max(yieldStress, gpuConfig.yieldStress);
            }
        }

        private static int FindKernelSafe(ComputeShader shader, string kernelName)
        {
            if (shader == null)
                return -1;

            try
            {
                return shader.FindKernel(kernelName);
            }
            catch
            {
                return -1;
            }
        }

        private static int Groups(int count, int groupSize)
        {
            return Mathf.Max(1, Mathf.CeilToInt(count / (float)groupSize));
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
                return;

            GUILayout.BeginArea(new Rect(debugOverlayPosition.x, debugOverlayPosition.y, 360.0f, 300.0f), GUI.skin.box);
            GUILayout.Label("MPM Canvas Deposition");
            GUILayout.Label($"Initialized: {_stats.initialized} | Enabled: {_stats.enabled}");
            GUILayout.Label($"Particles: {_stats.particleCount} | Frame: {_stats.dispatchFrame}");
            GUILayout.Label($"Checked: {_stats.checkedParticles} | Air states: {_stats.airStateParticles}");
            GUILayout.Label($"Plane hits: {_stats.planeHits} | Bounds hits: {_stats.boundsHits}");
            GUILayout.Label($"Deposited: {_stats.depositedParticles} | Absorbed: {_stats.absorbedParticles}");
            GUILayout.Label($"Stick: {_stats.stickImpacts} | Spread: {_stats.spreadImpacts}");
            GUILayout.Label($"Splash: {_stats.splashImpacts} | Bounce: {_stats.bounceImpacts}");
            GUILayout.Label($"Visible reused: {_stats.visibleDropletsReused}");
            GUILayout.Label($"Reuse S/B/Sp: {_stats.splashReusedParticles}/{_stats.bounceReusedParticles}/{_stats.spreadReusedParticles}");
            GUILayout.Label($"Surface spawned/deposited: {_stats.surfaceParticlesSpawned}/{_stats.surfaceParticlesDeposited}");
            GUILayout.Label($"Droplets spawned/deposited: {_stats.dropletsSpawned}/{_stats.dropletsDeposited}");
            GUILayout.Label($"Active surface/internal: {_stats.surfaceParticlesActive}/{_stats.internalDropletsActive}");
            GUILayout.EndArea();
        }

        private void OnValidate()
        {
            dispatchEveryNFrames = Mathf.Max(1, dispatchEveryNFrames);
            collisionSkinMeters = Mathf.Max(0.0f, collisionSkinMeters);
            minimumImpactSpeed = Mathf.Max(0.0f, minimumImpactSpeed);
            depositionEfficiency = Mathf.Clamp(depositionEfficiency, 0.0f, 1.5f);
            baseSplatRadiusCells = Mathf.Clamp(baseSplatRadiusCells, 0.25f, 8.0f);
            impactSpreadMultiplier = Mathf.Clamp(impactSpreadMultiplier, 0.0f, 8.0f);
            tangentialStretchMultiplier = Mathf.Clamp(tangentialStretchMultiplier, 0.0f, 10.0f);
            maxSplatRadiusCells = Mathf.Clamp(maxSplatRadiusCells, 1, 12);
            maxDepositUnitsPerParticle = Mathf.Max(1, maxDepositUnitsPerParticle);
            depositedParticleRadiusMeters = Mathf.Max(depositedParticleRadiusMeters, 0.000001f);
            surfaceParticleCapacity = Mathf.Max(1, surfaceParticleCapacity);
            dropletParticleCapacity = Mathf.Max(1, dropletParticleCapacity);
            surfaceParticleLifetimeSeconds = Mathf.Clamp(surfaceParticleLifetimeSeconds, 0.05f, 5.0f);
            dropletLifetimeSeconds = Mathf.Clamp(dropletLifetimeSeconds, 0.05f, 5.0f);
            surfaceParticleDepositRate = Mathf.Clamp(surfaceParticleDepositRate, 0.05f, 4.0f);
            surfaceParticleFrictionPerSecond = Mathf.Clamp(surfaceParticleFrictionPerSecond, 0.0f, 12.0f);
            dropletDragPerSecond = Mathf.Clamp(dropletDragPerSecond, 0.0f, 12.0f);
            splashNormalSpeedThreshold = Mathf.Max(0.0f, splashNormalSpeedThreshold);
            bounceNormalSpeedThreshold = Mathf.Max(0.0f, bounceNormalSpeedThreshold);
            secondaryDropletMassFraction = Mathf.Clamp(secondaryDropletMassFraction, 0.0f, 0.8f);
            secondaryDropletVelocityBoost = Mathf.Clamp(secondaryDropletVelocityBoost, 0.0f, 8.0f);
            maxSecondaryDropletsPerImpact = Mathf.Clamp(maxSecondaryDropletsPerImpact, 0, 8);
            bounceRestitution = Mathf.Clamp(bounceRestitution, 0.0f, 0.8f);
            gravityAcceleration = Mathf.Max(0.0f, gravityAcceleration);
            splashVisibleReuseProbability = Mathf.Clamp01(splashVisibleReuseProbability);
            strongSpreadVisibleReuseProbability = Mathf.Clamp01(strongSpreadVisibleReuseProbability);
            bounceVisibleReuseProbability = Mathf.Clamp01(bounceVisibleReuseProbability);
            visibleDropletMassFraction = Mathf.Clamp(visibleDropletMassFraction, 0.01f, 0.65f);
            visibleDropletRadiusScale = Mathf.Clamp(visibleDropletRadiusScale, 0.10f, 0.95f);
            visibleDropletVelocityDamping = Mathf.Clamp01(visibleDropletVelocityDamping);
            visibleDropletNormalBoost = Mathf.Clamp(visibleDropletNormalBoost, 0.0f, 3.0f);
            visibleDropletSurfaceOffsetRadii = Mathf.Clamp(visibleDropletSurfaceOffsetRadii, 0.5f, 5.0f);
            strongSpreadVisibleSpeed = Mathf.Max(0.0f, strongSpreadVisibleSpeed);
            visibleDropletMinRadiusMeters = Mathf.Max(visibleDropletMinRadiusMeters, 0.000001f);
            debugReadbackInterval = Mathf.Max(1, debugReadbackInterval);
            ResolveKernels();
        }
    }
}
