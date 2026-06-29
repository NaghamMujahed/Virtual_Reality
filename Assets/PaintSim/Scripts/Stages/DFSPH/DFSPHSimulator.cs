using UnityEngine;
using PaintSim.Scripts.Core.Interfaces;
using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Core.Data; 
using PaintSim.Scripts.Core.Physics;
using PaintSim.Scripts.Stages.Surface;

namespace PaintSim.Scripts.Stages.DFSPH
{
    /// <summary>
    /// المدير الرئيسي لـ DFSPH.
    ///
    /// يشغل كل فريم:
    ///   1. NeighborSearch  (CPU → GPU)
    ///   2. DensityPressure (GPU)
    ///   3. Divergence-Free Loop (GPU, 1-3 تكرارات)
    ///   4. Constant Density Loop (GPU, 2-5 تكرارات)
    ///   5. Integration (GPU: Gravity + Drag + Position)
    /// </summary>
    public class DFSPHSimulator : ISimulationStage
    {
        private readonly BufferManager _bufferManager;
        private readonly SimulationConfig _config;
        private readonly NeighborSearchDispatcher _neighborSearch;

        // Compute Shaders
        private ComputeShader _densityPressureShader;
        private ComputeShader _divergenceShader;
        private ComputeShader _constantDensityShader;
        private ComputeShader _integrationShader;

        // Kernel IDs
        private int _kernelDensityPressure;
        private int _kernelComputeDivergence;
        private int _kernelApplyDivergence;
        private int _kernelComputeDensityError;
        private int _kernelApplyDensityCorrection;
        private int _kernelIntegrate;

        // Property IDs
        private static readonly int ID_Particles           = Shader.PropertyToID("_Particles");
        private static readonly int ID_HashBuffer          = Shader.PropertyToID("_HashBuffer");
        private static readonly int ID_HashOffsetBuffer    = Shader.PropertyToID("_HashOffsetBuffer");
        private static readonly int ID_CellSize            = Shader.PropertyToID("_CellSize");
        private static readonly int ID_HashTableSize       = Shader.PropertyToID("_HashTableSize");
        private static readonly int ID_MaxParticles        = Shader.PropertyToID("_MaxParticles");
        private static readonly int ID_SmoothingLength     = Shader.PropertyToID("_SmoothingLength");
        private static readonly int ID_RestDensity         = Shader.PropertyToID("_RestDensity");
        private static readonly int ID_DensityThreshold    = Shader.PropertyToID("_DensityThreshold");
        private static readonly int ID_DeltaTime           = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_PI                  = Shader.PropertyToID("_PI");
        private static readonly int ID_Gravity             = Shader.PropertyToID("_Gravity");
        private static readonly int ID_AirDensity          = Shader.PropertyToID("_AirDensity");
        private static readonly int ID_WindVelocity        = Shader.PropertyToID("_WindVelocity");
        private static readonly int ID_FloorY              = Shader.PropertyToID("_FloorY");
        private static readonly int ID_FloorDamping        = Shader.PropertyToID("_FloorDamping");
        private static readonly int ID_FloorFriction       = Shader.PropertyToID("_FloorFriction");

        public DFSPHSimulator(BufferManager bufferManager, SimulationConfig config )
        {
            _bufferManager = bufferManager;
            _config = config;
            _neighborSearch = new NeighborSearchDispatcher(bufferManager, config);
            
        }

        public void Initialize()
        {
            _densityPressureShader = LoadShader("DFSPH/DensityPressure");
            _divergenceShader      = LoadShader("DFSPH/DivergenceFree");
            _constantDensityShader = LoadShader("DFSPH/ConstantDensity");
            _integrationShader     = LoadShader("DFSPH/Integration");

              // ← أضف هذا الفحص
    if (_densityPressureShader == null || _divergenceShader == null || 
        _constantDensityShader == null || _integrationShader == null)
    {
        Debug.LogError("[DFSPHSimulator] One or more shaders failed to load. Simulation cannot start.");
        return;
    }

            _kernelDensityPressure        = _densityPressureShader.FindKernel("ComputeDensityPressure");
            _kernelComputeDivergence      = _divergenceShader.FindKernel("ComputeDivergence");
            _kernelApplyDivergence        = _divergenceShader.FindKernel("ApplyDivergenceCorrection");
            _kernelComputeDensityError    = _constantDensityShader.FindKernel("ComputeDensityError");
            _kernelApplyDensityCorrection = _constantDensityShader.FindKernel("ApplyDensityCorrection");
            _kernelIntegrate              = _integrationShader.FindKernel("Integrate");

            SetCommonConstants(_densityPressureShader);
            SetCommonConstants(_divergenceShader);
            SetCommonConstants(_constantDensityShader);
            SetCommonConstants(_integrationShader);

            _integrationShader.SetVector(ID_Gravity, new Vector4(_config.Gravity.x, _config.Gravity.y, _config.Gravity.z, 0f));
            _integrationShader.SetFloat(ID_AirDensity, _config.AirDensity);
            _integrationShader.SetVector(ID_WindVelocity, new Vector4(_config.WindVelocity.x, _config.WindVelocity.y, _config.WindVelocity.z, 0f));
            _integrationShader.SetFloat(ID_FloorY, 0f);
            _integrationShader.SetFloat(ID_FloorDamping, 0.3f);
            _integrationShader.SetFloat(ID_FloorFriction, 0.8f);
        }

        public void Execute(float deltaTime)
        {
            // 1. بناء Spatial Hash
            _neighborSearch.BuildHashTable();
            // _integrationShader.SetFloat(ID_FloorY, floorY);

            // 2. حساب الكثافة والضغط
            Dispatch(_densityPressureShader, _kernelDensityPressure);

            // 3. Loop 1: Divergence-Free
            for (int iter = 0; iter < _config.MaxDivergenceIterations; iter++)
            {
                _divergenceShader.SetFloat(ID_DeltaTime, deltaTime);
                Dispatch(_divergenceShader, _kernelComputeDivergence);
                Dispatch(_divergenceShader, _kernelApplyDivergence);
            }

            // 4. Loop 2: Constant Density
            for (int iter = 0; iter < _config.MaxDensityIterations; iter++)
            {
                _constantDensityShader.SetFloat(ID_DeltaTime, deltaTime);
                Dispatch(_constantDensityShader, _kernelComputeDensityError);
                Dispatch(_constantDensityShader, _kernelApplyDensityCorrection);
            }

            // 5. التحريك
            _integrationShader.SetFloat(ID_DeltaTime, deltaTime);
            Dispatch(_integrationShader, _kernelIntegrate);

        }

        public void Cleanup()
        {
            // Resources تديرها Unity
        }

        private ComputeShader LoadShader(string path)
        {
            var shader = Resources.Load<ComputeShader>($"ComputeShaders/{path}");
            if (shader == null)
                Debug.LogError($"[DFSPHSimulator] Failed to load: Resources/ComputeShaders/{path}.compute");
            return shader;
        }

        private void SetCommonConstants(ComputeShader shader)
        {
            shader.SetFloat(ID_CellSize, _config.SmoothingLength);
            shader.SetInt(ID_HashTableSize, Mathf.NextPowerOfTwo(_config.MaxParticles * 2));
            shader.SetInt(ID_MaxParticles, _config.MaxParticles);
            shader.SetFloat(ID_SmoothingLength, _config.SmoothingLength);
            shader.SetFloat(ID_RestDensity, _config.RestDensity);
            shader.SetFloat(ID_DensityThreshold, _config.DensityThreshold);
            shader.SetFloat(ID_PI, PhysicsConstants.Pi);
        }

        private void Dispatch(ComputeShader shader, int kernel)
        {
            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            var hashBuffer     = _bufferManager.GetBuffer(BufferType.SpatialHashBuffer);
            var offsetBuffer   = _bufferManager.GetBuffer(BufferType.HashOffsetBuffer);

            shader.SetBuffer(kernel, ID_Particles, particleBuffer);
            shader.SetBuffer(kernel, ID_HashBuffer, hashBuffer);
            shader.SetBuffer(kernel, ID_HashOffsetBuffer, offsetBuffer);

            int threadGroups = (_config.MaxParticles + 63) / 64;
            shader.Dispatch(kernel, threadGroups, 1, 1);
        }
    }
}