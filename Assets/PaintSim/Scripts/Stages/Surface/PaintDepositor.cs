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
        private readonly PaintProperties _paintProperties;
        private readonly int _kernelIndex = -1;
        private readonly uint[] _zeroDiagnostics = new uint[DiagnosticCounterCount];
        private readonly uint[] _diagnostics = new uint[DiagnosticCounterCount];

        private ComputeBuffer _diagnosticBuffer;

        private static readonly int ID_ThicknessScale =
            Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_PaintDensity =
            Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_PaintViscosity =
            Shader.PropertyToID("_PaintViscosity");
        private static readonly int ID_SurfaceTension =
            Shader.PropertyToID("_SurfaceTension");
        private static readonly int ID_FallbackPaintColor =
            Shader.PropertyToID("_FallbackPaintColor");

        private static readonly int ID_MlsParticlePositionRadius =
            Shader.PropertyToID("_MlsParticlePositionRadius");
        private static readonly int ID_MlsParticleVelocityMass =
            Shader.PropertyToID("_MlsParticleVelocityMass");
        private static readonly int ID_MlsParticleColor =
            Shader.PropertyToID("_MlsParticleColor");
        private static readonly int ID_MlsParticleStateAgeId =
            Shader.PropertyToID("_MlsParticleStateAgeId");
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
            EnsureDiagnosticBuffer();
        }

        public void DispatchFromMlsMpmBuffers(
            GraphicsBuffer positionRadiusBuffer,
            GraphicsBuffer velocityMassBuffer,
            GraphicsBuffer colorBuffer,
            GraphicsBuffer stateAgeIdBuffer,
            int activeParticleCount,
            SurfaceProperties surface,
            bool markParticlesOnImpact = true,
            bool depositOnlyAirDomainParticles = true,
            bool acceptFluidDomainSurfaceHits = true,
            bool enableDiagnostics = false)
        {
            if (_shader == null ||
                _kernelIndex < 0 ||
                _paintFilmGrid == null ||
                activeParticleCount <= 0 ||
                positionRadiusBuffer == null ||
                velocityMassBuffer == null ||
                colorBuffer == null ||
                stateAgeIdBuffer == null)
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

            if (_diagnosticBuffer != null)
            {
                if (enableDiagnostics)
                    _diagnosticBuffer.SetData(_zeroDiagnostics);

                _shader.SetBuffer(
                    _kernelIndex,
                    ID_SurfaceImpactDiagnostics,
                    _diagnosticBuffer
                );
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

            _paintFilmGrid.BindToShader(_shader, _kernelIndex);

            int threadGroups = Mathf.CeilToInt((float)particleCount / ThreadGroupSize);
            if (threadGroups > 0)
                _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);
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
            _shader.SetVector(ID_FallbackPaintColor, _paintProperties.Color);
            _shader.SetInt(ID_ThicknessScale, PaintCellData.ThicknessScale);

            _shader.SetFloat(ID_DepositionFraction, surface.DepositionFraction);
            _shader.SetFloat(ID_SplashMultiplier, surface.SplashMultiplier);
            _shader.SetFloat(ID_AbsorptionRate, surface.AbsorptionRate);
            _shader.SetFloat(ID_Roughness, surface.Roughness);
            _shader.SetFloat(ID_SpreadSpeed, surface.SpreadSpeed);
            _shader.SetInt(ID_SurfaceTypeID, (int)surface.SurfaceTypeID);
            _shader.SetInt(ID_MlsParticleCount, particleCount);
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
