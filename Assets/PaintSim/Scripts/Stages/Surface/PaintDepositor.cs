using UnityEngine;
using PaintSim.Scripts.Core.Data;

namespace PaintSim.Scripts.Stages.Surface
{
    public sealed class PaintDepositor
    {
        private const int ThreadGroupSize = 64;
        private const int DiagnosticCounterCount = 8;

        private readonly ComputeShader _shader;
        private readonly PaintFilmGrid _paintFilmGrid;
        private PaintProperties _paintProperties;
        private readonly int _kernelIndex = -1;
        private readonly int _diagnosticsKernelIndex = -1;
        private readonly int _buildResolveArgsKernelIndex = -1;
        private readonly int _resolveKernelIndex = -1;
        private readonly int _resetDepositTrackingKernelIndex = -1;
        private readonly uint[] _zeroDiagnostics = new uint[DiagnosticCounterCount];
        private readonly uint[] _diagnostics = new uint[DiagnosticCounterCount];

        private ComputeBuffer _diagnosticBuffer;
        private bool _useCarreauYasuda;
        private float _zeroShearViscosity = 1.0f;
        private float _infiniteShearViscosity = 0.1f;
        private float _relaxationTime = 1.0f;
        private float _yasudaExponent = 2.0f;
        private float _flowIndex = 0.7f;
        private float _yieldStress;
        private PaintColorMixingMode _colorMixingMode = PaintColorMixingMode.Rgb;
        private float _pigmentMixStrength = 1.0f;
        private float _pigmentMinReflectance = 0.035f;
        private float _pigmentMaxKs = 18.0f;

        private static readonly int ID_ThicknessScale =
            Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_PaintDensity =
            Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_PaintViscosity =
            Shader.PropertyToID("_PaintViscosity");
        private static readonly int ID_SurfaceTension =
            Shader.PropertyToID("_SurfaceTension");

        private static readonly int ID_MlsParticlePositionRadius =
            Shader.PropertyToID("_MlsParticlePositionRadius");
        private static readonly int ID_MlsParticleVelocityMass =
            Shader.PropertyToID("_MlsParticleVelocityMass");
        private static readonly int ID_MlsParticleColor =
            Shader.PropertyToID("_MlsParticleColor");
        private static readonly int ID_MlsParticleStateAgeId =
            Shader.PropertyToID("_MlsParticleStateAgeId");
        private static readonly int ID_MlsParticleVolumeJ =
            Shader.PropertyToID("_MlsParticleVolumeJ");
        private static readonly int ID_UseMlsParticleVolumeJ =
            Shader.PropertyToID("_UseMlsParticleVolumeJ");
        private static readonly int ID_MlsParticleCount =
            Shader.PropertyToID("_MlsParticleCount");
        private static readonly int ID_DepositOnlyMlsAirDomainParticles =
            Shader.PropertyToID("_DepositOnlyMlsAirDomainParticles");
        private static readonly int ID_MarkMlsParticlesDeposited =
            Shader.PropertyToID("_MarkMlsParticlesDeposited");
        private static readonly int ID_AcceptFluidDomainSurfaceHits =
            Shader.PropertyToID("_AcceptFluidDomainSurfaceHits");
        private static readonly int ID_EnableSurfaceImpactDiagnostics =
            Shader.PropertyToID("_EnableSurfaceImpactDiagnostics");
        private static readonly int ID_SurfaceImpactDiagnostics =
            Shader.PropertyToID("_SurfaceImpactDiagnostics");
        private static readonly int ID_SurfaceImpactDeltaTime =
            Shader.PropertyToID("_SurfaceImpactDeltaTime");
        private static readonly int ID_UseCarreauYasuda =
            Shader.PropertyToID("_UseCarreauYasuda");
        private static readonly int ID_ZeroShearViscosity =
            Shader.PropertyToID("_ZeroShearViscosity");
        private static readonly int ID_InfiniteShearViscosity =
            Shader.PropertyToID("_InfiniteShearViscosity");
        private static readonly int ID_RelaxationTime =
            Shader.PropertyToID("_RelaxationTime");
        private static readonly int ID_YasudaExponent =
            Shader.PropertyToID("_YasudaExponent");
        private static readonly int ID_FlowIndex =
            Shader.PropertyToID("_FlowIndex");
        private static readonly int ID_YieldStress =
            Shader.PropertyToID("_YieldStress");
        private static readonly int ID_ColorMixingMode =
            Shader.PropertyToID("_ColorMixingMode");
        private static readonly int ID_PigmentMixStrength =
            Shader.PropertyToID("_PigmentMixStrength");
        private static readonly int ID_PigmentMinReflectance =
            Shader.PropertyToID("_PigmentMinReflectance");
        private static readonly int ID_PigmentMaxKs =
            Shader.PropertyToID("_PigmentMaxKs");

        private static readonly int ID_DepositionFraction =
            Shader.PropertyToID("_DepositionFraction");
        private static readonly int ID_SplashMultiplier =
            Shader.PropertyToID("_SplashMultiplier");
        private static readonly int ID_AbsorptionRate =
            Shader.PropertyToID("_AbsorptionRate");
        private static readonly int ID_Roughness =
            Shader.PropertyToID("_Roughness");
        private static readonly int ID_SpreadSpeed =
            Shader.PropertyToID("_SpreadSpeed");
        private static readonly int ID_SurfaceTypeID =
            Shader.PropertyToID("_SurfaceTypeID");

        public PaintDepositor(
            ComputeShader shader,
            PaintFilmGrid paintFilmGrid,
            PaintProperties paintProperties)
        {
            _shader = shader;
            _paintFilmGrid = paintFilmGrid;
            _paintProperties = paintProperties;

            if (_shader == null)
                return;

            _kernelIndex = _shader.FindKernel("CSMainMlsMpm");
            _diagnosticsKernelIndex =
                _shader.FindKernel("CollectSurfaceImpactDiagnostics");
            _buildResolveArgsKernelIndex =
                _shader.FindKernel("BuildResolveDispatchArgs");
            _resolveKernelIndex = _shader.FindKernel("ResolveDeposits");
            _resetDepositTrackingKernelIndex =
                _shader.FindKernel("ResetDepositTracking");
            EnsureDiagnosticBuffer();
        }

        public void ConfigureImpactRheology(
            bool useCarreauYasuda,
            float zeroShearViscosity,
            float infiniteShearViscosity,
            float relaxationTime,
            float yasudaExponent,
            float flowIndex,
            float yieldStress)
        {
            _useCarreauYasuda = useCarreauYasuda;
            _zeroShearViscosity = Mathf.Max(zeroShearViscosity, 0.0001f);
            _infiniteShearViscosity = Mathf.Max(infiniteShearViscosity, 0.0001f);
            _relaxationTime = Mathf.Max(relaxationTime, 0.0f);
            _yasudaExponent = Mathf.Clamp(yasudaExponent, 0.25f, 8.0f);
            _flowIndex = Mathf.Clamp(flowIndex, 0.05f, 2.0f);
            _yieldStress = Mathf.Max(yieldStress, 0.0f);
        }

        public void ConfigurePaintProperties(PaintProperties paintProperties)
        {
            _paintProperties = paintProperties;
        }

        public void ConfigureColorMixing(
            PaintColorMixingMode mode,
            float pigmentMixStrength,
            float pigmentMinReflectance,
            float pigmentMaxKs)
        {
            _colorMixingMode = mode;
            _pigmentMixStrength = Mathf.Clamp01(pigmentMixStrength);
            _pigmentMinReflectance = Mathf.Clamp(
                pigmentMinReflectance,
                0.001f,
                0.35f
            );
            _pigmentMaxKs = Mathf.Clamp(pigmentMaxKs, 1.0f, 64.0f);
        }

        public void DispatchFromMlsMpmBuffers(
            GraphicsBuffer positionRadiusBuffer,
            GraphicsBuffer velocityMassBuffer,
            GraphicsBuffer colorBuffer,
            GraphicsBuffer stateAgeIdBuffer,
            GraphicsBuffer volumeJBuffer,
            int activeParticleCount,
            SurfaceProperties surface,
            bool markParticlesOnImpact = true,
            bool depositOnlyAirDomainParticles = true,
            bool acceptFluidDomainSurfaceHits = true,
            bool enableDiagnostics = false,
            float impactDeltaTime = 0.0f)
        {
            if (_shader == null ||
                _kernelIndex < 0 ||
                _paintFilmGrid == null ||
                activeParticleCount <= 0 ||
                positionRadiusBuffer == null ||
                velocityMassBuffer == null ||
                colorBuffer == null ||
                stateAgeIdBuffer == null ||
                _paintFilmGrid.DepositColorAccumulatorBuffer == null ||
                _paintFilmGrid.DepositFlowAccumulatorBuffer == null)
            {
                return;
            }

            int particleCount = Mathf.Min(
                activeParticleCount,
                Mathf.Min(
                    Mathf.Min(positionRadiusBuffer.count, velocityMassBuffer.count),
                    Mathf.Min(colorBuffer.count, stateAgeIdBuffer.count)
                )
            );

            if (particleCount <= 0)
                return;

            ApplyCommonShaderParameters(particleCount, surface);
            EnsureDiagnosticBuffer();

            _shader.SetInt(ID_MlsParticleCount, particleCount);
            _shader.SetInt(
                ID_DepositOnlyMlsAirDomainParticles,
                depositOnlyAirDomainParticles ? 1 : 0
            );
            _shader.SetInt(
                ID_AcceptFluidDomainSurfaceHits,
                acceptFluidDomainSurfaceHits ? 1 : 0
            );
            _shader.SetInt(
                ID_MarkMlsParticlesDeposited,
                markParticlesOnImpact ? 1 : 0
            );
            _shader.SetInt(
                ID_EnableSurfaceImpactDiagnostics,
                enableDiagnostics ? 1 : 0
            );
            _shader.SetFloat(
                ID_SurfaceImpactDeltaTime,
                Mathf.Max(impactDeltaTime, 0.0f)
            );
            _shader.SetInt(
                ID_UseMlsParticleVolumeJ,
                volumeJBuffer != null ? 1 : 0
            );

            if (_diagnosticBuffer != null)
            {
                if (enableDiagnostics)
                    _diagnosticBuffer.SetData(_zeroDiagnostics);
            }

            _shader.SetBuffer(
                _kernelIndex,
                ID_MlsParticlePositionRadius,
                positionRadiusBuffer
            );
            _shader.SetBuffer(
                _kernelIndex,
                ID_MlsParticleVelocityMass,
                velocityMassBuffer
            );
            _shader.SetBuffer(
                _kernelIndex,
                ID_MlsParticleColor,
                colorBuffer
            );
            _shader.SetBuffer(
                _kernelIndex,
                ID_MlsParticleStateAgeId,
                stateAgeIdBuffer
            );
            _shader.SetBuffer(
                _kernelIndex,
                ID_MlsParticleVolumeJ,
                volumeJBuffer != null ? volumeJBuffer : positionRadiusBuffer
            );

            _paintFilmGrid.BindToShader(_shader, _kernelIndex);
            _paintFilmGrid.BindDepositWriteBuffers(_shader, _kernelIndex);

            int threadGroups = Mathf.CeilToInt((float)particleCount / ThreadGroupSize);
            if (threadGroups > 0)
                _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);

            if (enableDiagnostics)
            {
                DispatchDiagnostics(
                    positionRadiusBuffer,
                    stateAgeIdBuffer,
                    particleCount,
                    threadGroups
                );
            }

            ResolveAtomicDeposits();
        }

        public SurfaceImpactDiagnostics ReadDiagnostics()
        {
            if (_diagnosticBuffer == null)
                return default;

            _diagnosticBuffer.GetData(_diagnostics);
            return new SurfaceImpactDiagnostics(_diagnostics);
        }

        public void Dispose()
        {
            _diagnosticBuffer?.Release();
            _diagnosticBuffer = null;
        }

        private void EnsureDiagnosticBuffer()
        {
            if (_diagnosticBuffer != null)
                return;

            _diagnosticBuffer = new ComputeBuffer(
                DiagnosticCounterCount,
                sizeof(uint),
                ComputeBufferType.Default
            );
            _diagnosticBuffer.SetData(_zeroDiagnostics);
        }

        private void ApplyCommonShaderParameters(
            int particleCount,
            SurfaceProperties surface)
        {
            _shader.SetFloat(ID_PaintDensity, _paintProperties.Density);
            _shader.SetFloat(ID_PaintViscosity, _paintProperties.DynamicViscosity);
            _shader.SetFloat(ID_SurfaceTension, _paintProperties.SurfaceTension);
            _shader.SetInt(ID_ThicknessScale, PaintCellData.ThicknessScale);

            _shader.SetFloat(ID_DepositionFraction, surface.DepositionFraction);
            _shader.SetFloat(ID_SplashMultiplier, surface.SplashMultiplier);
            _shader.SetFloat(ID_AbsorptionRate, surface.AbsorptionRate);
            _shader.SetFloat(ID_Roughness, surface.Roughness);
            _shader.SetFloat(ID_SpreadSpeed, surface.SpreadSpeed);
            _shader.SetInt(ID_SurfaceTypeID, (int)surface.SurfaceTypeID);
            _shader.SetInt(ID_MlsParticleCount, particleCount);
            _shader.SetInt(ID_UseCarreauYasuda, _useCarreauYasuda ? 1 : 0);
            _shader.SetFloat(ID_ZeroShearViscosity, _zeroShearViscosity);
            _shader.SetFloat(ID_InfiniteShearViscosity, _infiniteShearViscosity);
            _shader.SetFloat(ID_RelaxationTime, _relaxationTime);
            _shader.SetFloat(ID_YasudaExponent, _yasudaExponent);
            _shader.SetFloat(ID_FlowIndex, _flowIndex);
            _shader.SetFloat(ID_YieldStress, _yieldStress);
            _shader.SetInt(ID_ColorMixingMode, (int)_colorMixingMode);
            _shader.SetFloat(ID_PigmentMixStrength, _pigmentMixStrength);
            _shader.SetFloat(ID_PigmentMinReflectance, _pigmentMinReflectance);
            _shader.SetFloat(ID_PigmentMaxKs, _pigmentMaxKs);
        }

        private void ResolveAtomicDeposits()
        {
            if (_buildResolveArgsKernelIndex < 0 ||
                _resolveKernelIndex < 0 ||
                _resetDepositTrackingKernelIndex < 0 ||
                _paintFilmGrid.DepositResolveDispatchArgsBuffer == null)
            {
                return;
            }

            _paintFilmGrid.ApplyGridParameters(_shader);
            _paintFilmGrid.BindDepositDispatchControlBuffers(
                _shader,
                _buildResolveArgsKernelIndex
            );
            _paintFilmGrid.BindDepositResolveBuffers(
                _shader,
                _resolveKernelIndex
            );
            _paintFilmGrid.BindDepositDispatchControlBuffers(
                _shader,
                _resetDepositTrackingKernelIndex
            );
            _shader.SetFloat(ID_PaintDensity, _paintProperties.Density);

            _shader.Dispatch(_buildResolveArgsKernelIndex, 1, 1, 1);
            _shader.DispatchIndirect(
                _resolveKernelIndex,
                _paintFilmGrid.DepositResolveDispatchArgsBuffer
            );
            _shader.Dispatch(_resetDepositTrackingKernelIndex, 1, 1, 1);
        }

        private void DispatchDiagnostics(
            GraphicsBuffer positionRadiusBuffer,
            GraphicsBuffer stateAgeIdBuffer,
            int particleCount,
            int threadGroups)
        {
            if (_diagnosticsKernelIndex < 0 ||
                _diagnosticBuffer == null ||
                threadGroups <= 0)
            {
                return;
            }

            _paintFilmGrid.ApplyGridParameters(_shader);
            _paintFilmGrid.BindDepositTouchedList(
                _shader,
                _diagnosticsKernelIndex
            );
            _shader.SetInt(ID_MlsParticleCount, particleCount);
            _shader.SetBuffer(
                _diagnosticsKernelIndex,
                ID_MlsParticlePositionRadius,
                positionRadiusBuffer
            );
            _shader.SetBuffer(
                _diagnosticsKernelIndex,
                ID_MlsParticleStateAgeId,
                stateAgeIdBuffer
            );
            _shader.SetBuffer(
                _diagnosticsKernelIndex,
                ID_SurfaceImpactDiagnostics,
                _diagnosticBuffer
            );
            _shader.Dispatch(_diagnosticsKernelIndex, threadGroups, 1, 1);
        }
    }

    public readonly struct SurfaceImpactDiagnostics
    {
        public readonly uint Scanned;
        public readonly uint StateAccepted;
        public readonly uint NearSurface;
        public readonly uint InGrid;
        public readonly uint Impacted;
        public readonly uint Settled;
        public readonly uint StateSkipped;
        public readonly uint CellWrites;

        public SurfaceImpactDiagnostics(uint[] counters)
        {
            Scanned = Get(counters, 0);
            StateAccepted = Get(counters, 1);
            NearSurface = Get(counters, 2);
            InGrid = Get(counters, 3);
            Impacted = Get(counters, 4);
            Settled = Get(counters, 5);
            StateSkipped = Get(counters, 6);
            CellWrites = Get(counters, 7);
        }

        private static uint Get(uint[] counters, int index)
        {
            return counters != null && index >= 0 && index < counters.Length
                ? counters[index]
                : 0u;
        }

        public override string ToString()
        {
            return
                $"scanned={Scanned}, stateAccepted={StateAccepted}, " +
                $"nearSurface={NearSurface}, inGrid={InGrid}, " +
                $"impacted={Impacted}, settled={Settled}, " +
                $"stateSkipped={StateSkipped}, cellWrites={CellWrites}";
        }
    }
}
