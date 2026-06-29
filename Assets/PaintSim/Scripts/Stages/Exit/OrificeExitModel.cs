using UnityEngine;
using PaintSim.Scripts.Core.Interfaces;
using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Physics;

namespace PaintSim.Scripts.Stages.Exit
{
    /// <summary>
    /// Stage 1: نموذج الفتحة.
    ///
    /// يقرأ OrificeExitState من الـ Buffer (مكتوب سابقاً من BucketAdapter)
    /// ويُشغّل Compute Shader لحساب:
    ///   - VolumeFlowRate = π · r² · V
    ///   - MassFlowRate   = ρ · Q
    ///
    /// المخرج: ExitStateBuffer مُحدَّث — جاهز للـ ParticleSpawner.
    /// </summary>
    public class OrificeExitModel : ISimulationStage
    {
        private readonly BufferManager _bufferManager;
        private readonly PaintProperties _paintProperties;

        private ComputeShader _shader;
        private int _kernelIndex;

        // ─────────────────────────────────────────
        // Constants
        // ─────────────────────────────────────────
        private const string SHADER_NAME = "ExitVelocity";
        private const string KERNEL_NAME = "ExitVelocity";

        // Kernel IDs
        private static readonly int ID_ExitState      = Shader.PropertyToID("_ExitState");
        private static readonly int ID_PaintDensity   = Shader.PropertyToID("_PaintDensity");
        private static readonly int ID_PI             = Shader.PropertyToID("_PI");

        public OrificeExitModel(BufferManager bufferManager, PaintProperties paintProperties)
        {
            _bufferManager = bufferManager;
            _paintProperties = paintProperties;
        }

        // ─────────────────────────────────────────
        // ISimulationStage
        // ─────────────────────────────────────────

        public void Initialize()
        {
            // نحمل الـ ComputeShader من مجلد Resources
            // ملاحظة: يجب أن يكون Shader في: Resources/ComputeShaders/Exit/ExitVelocity
            // أو تستخدم AssetReference إذا كنت مع Addressables
            _shader = Resources.Load<ComputeShader>($"ComputeShaders/Exit/{SHADER_NAME}");
            
            if (_shader == null)
            {
                Debug.LogError($"[OrificeExitModel] ComputeShader '{SHADER_NAME}' not found in Resources/ComputeShaders/Exit/");
                return;
            }

            _kernelIndex = _shader.FindKernel(KERNEL_NAME);
        }

        public void Execute(float deltaTime )
        {
            if (_shader == null) return;

            var exitBuffer = _bufferManager.GetBuffer(BufferType.ExitStateBuffer);

            // ربط الـ Buffer
            _shader.SetBuffer(_kernelIndex, ID_ExitState, exitBuffer);

            // تمرير الثوابت
            _shader.SetFloat(ID_PaintDensity, _paintProperties.Density);
            _shader.SetFloat(ID_PI, PhysicsConstants.Pi);

            // Dispatch: thread واحد فقط (حالة فتحة واحدة)
            _shader.Dispatch(_kernelIndex, 1, 1, 1);
        }

        public void Cleanup()
        {
            if (_shader != null)
            {
                // Resources.UnloadAsset(_shader); // اختياري
                _shader = null;
            }
        }
    }
}