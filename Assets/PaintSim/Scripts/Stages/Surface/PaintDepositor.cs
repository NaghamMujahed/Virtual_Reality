using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Buffers;

namespace PaintSim.Scripts.Stages.Surface
{
    public class PaintDepositor
    {
        private readonly ComputeShader   _shader;
        private readonly BufferManager   _bufferManager;
        private readonly PaintFilmGrid   _paintFilmGrid;
        private readonly PaintProperties _paintProperties;
        private readonly SimulationConfig _config;

        private readonly int _kernelIndex;
        private const int ThreadGroupSize = 64;

        // Property IDs
        private static readonly int ID_ParticleBuffer     = Shader.PropertyToID("_ParticleBuffer");
        private static readonly int ID_ThicknessScale     = Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_ParticleCount      = Shader.PropertyToID("_ParticleCount");
        private static readonly int ID_PaintDensity       = Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_PaintViscosity     = Shader.PropertyToID("_PaintViscosity");
        private static readonly int ID_SurfaceTension     = Shader.PropertyToID("_SurfaceTension");

        //  خصائص السطح
        private static readonly int ID_DepositionFraction = Shader.PropertyToID("_DepositionFraction");
        private static readonly int ID_SplashMultiplier   = Shader.PropertyToID("_SplashMultiplier");
        private static readonly int ID_AbsorptionRate     = Shader.PropertyToID("_AbsorptionRate");
        private static readonly int ID_Roughness          = Shader.PropertyToID("_Roughness");
        private static readonly int ID_SurfaceTypeID      = Shader.PropertyToID("_SurfaceTypeID");

        public PaintDepositor(
            ComputeShader    shader,
            BufferManager    bufferManager,
            PaintFilmGrid    paintFilmGrid,
            PaintProperties  paintProperties,
            SimulationConfig config)
        {
            _shader          = shader;
            _bufferManager   = bufferManager;
            _paintFilmGrid   = paintFilmGrid;
            _paintProperties = paintProperties;
            _config          = config;

            if (_shader != null)
                _kernelIndex = _shader.FindKernel("CSMain");
        }

        public void Dispatch(
            int activeParticleCount,
            SurfaceProperties? surfaceProps = null)
        {
            if (_shader == null || activeParticleCount <= 0) return;
            

            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            if (particleBuffer == null || particleBuffer.count == 0) return;

            _shader.SetFloat(ID_PaintDensity,   _paintProperties.Density);
            _shader.SetFloat(ID_PaintViscosity, _paintProperties.DynamicViscosity);
            _shader.SetFloat(ID_SurfaceTension, _paintProperties.SurfaceTension);
            _shader.SetInt(ID_ThicknessScale,   PaintCellData.ThicknessScale);
            _shader.SetInt(ID_ParticleCount,    activeParticleCount);

            var surface = surfaceProps ?? SurfaceProperties.Wood;

            Debug.Log($"[Depositor] Surface: " +
              $"Deposition={surface.DepositionFraction} " +
              $"Splash={surface.SplashMultiplier} " +
              $"Absorption={surface.AbsorptionRate} " +
              $"Roughness={surface.Roughness}");

            _shader.SetFloat(ID_DepositionFraction, surface.DepositionFraction);
            _shader.SetFloat(ID_SplashMultiplier,   surface.SplashMultiplier);
            _shader.SetFloat(ID_AbsorptionRate,     surface.AbsorptionRate);
            _shader.SetFloat(ID_Roughness,          surface.Roughness);
            _shader.SetInt(ID_SurfaceTypeID,   (int)surface.SurfaceTypeID);

            _shader.SetBuffer(_kernelIndex, ID_ParticleBuffer, particleBuffer);
            _paintFilmGrid.BindToShader(_shader, _kernelIndex);

            int threadGroups = Mathf.CeilToInt(
                (float)particleBuffer.count / ThreadGroupSize
            );

            if (threadGroups > 0)
                _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);
        }
    }
}