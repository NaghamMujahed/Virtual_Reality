using System.Collections.Generic;
using UnityEngine;

namespace PaintSim.Scripts.Core.Buffers
{
    /// <summary>
    /// يدير كل ComputeBuffers في المحاكاة.
    /// ننشئ Buffer مرة واحدة ونستخدمه طوال الـ Play.
    /// </summary>
    public class BufferManager
    {
        private readonly Dictionary<BufferType, ComputeBuffer> _buffers = new();
        private readonly int _maxParticles;
        private readonly int _maxNeighbors;
        private readonly int _gridWidth;
        private readonly int _gridHeight;

        public BufferManager(int maxParticles, int maxNeighbors, int gridWidth, int gridHeight)
        {
            _maxParticles = maxParticles;
            _maxNeighbors = maxNeighbors;
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
        }

        /// <summary>
        /// ينشئ كل البuffers ويصفّرها.
        /// التصفير ضروري لمنع الـ Shader من قراءة garbage data.
        /// </summary>
        public void Initialize()
        {
            // Buffer الجسيمات
            CreateBuffer(BufferType.ParticleBuffer, _maxParticles);
            ZeroBuffer(BufferType.ParticleBuffer);

            // Buffer الجيران
            CreateBuffer(BufferType.NeighborBuffer, _maxParticles * _maxNeighbors);
            ZeroBuffer(BufferType.NeighborBuffer);

            // Spatial Hash — نحتاج حجم كافٍ لجميع الجسيمات
            int hashSize = Mathf.NextPowerOfTwo(_maxParticles * 2);
            CreateBuffer(BufferType.SpatialHashBuffer, hashSize);
            ZeroBuffer(BufferType.SpatialHashBuffer);

            // HashOffsetBuffer — حجمه MUST يكون hashSize + 1
            // لأن الـ Shader يصل لـ _HashOffsetBuffer[hash + 1]
            // بدون +1، آخر hash رح يطلع برّا الحدود
            CreateBuffer(BufferType.HashOffsetBuffer, hashSize + 1);
            ZeroBuffer(BufferType.HashOffsetBuffer);

            // Exit State
            CreateBuffer(BufferType.ExitStateBuffer, 1);
            ZeroBuffer(BufferType.ExitStateBuffer);

            // Paint Cell Grid
            CreateBuffer(BufferType.PaintCellBuffer, _gridWidth * _gridHeight);
            ZeroBuffer(BufferType.PaintCellBuffer);

            // Active Count — يبدأ من صفر
            CreateBuffer(BufferType.ActiveCountBuffer, 1);
            ResetActiveCount();
        }

        public ComputeBuffer GetBuffer(BufferType type)
        {
            if (_buffers.TryGetValue(type, out var buffer))
                return buffer;

            throw new System.InvalidOperationException($"Buffer {type} not initialized. Call Initialize() first.");
        }

        public int GetActiveParticleCount()
        {
            var buffer = GetBuffer(BufferType.ActiveCountBuffer);
            uint[] count = new uint[1];
            buffer.GetData(count);
            return (int)count[0];
        }

        public void ResetActiveCount()
        {
            var buffer = GetBuffer(BufferType.ActiveCountBuffer);
            buffer.SetData(new uint[] { 0 });
        }

        public void Cleanup()
        {
            foreach (var buffer in _buffers.Values)
            {
                buffer?.Release();
            }
            _buffers.Clear();
        }

        private void CreateBuffer(BufferType type, int count)
        {
            int stride = BufferDefinitions.GetStride(type);
            var buffer = new ComputeBuffer(count, stride, ComputeBufferType.Default);
            _buffers[type] = buffer;
        }

        /// <summary>
        /// يصفّر Buffer بالكامل (يمليه بأصفار).
        /// هذا يضمن أن الـ Shader لا يقرأ garbage في أول إطار.
        /// </summary>
        private void ZeroBuffer(BufferType type)
        {
            var buffer = GetBuffer(type);
            int totalBytes = buffer.count * buffer.stride;
            byte[] zeros = new byte[totalBytes];
            buffer.SetData(zeros);
        }
    }
}