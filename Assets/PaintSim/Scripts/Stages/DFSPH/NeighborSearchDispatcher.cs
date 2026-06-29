using UnityEngine;
using System.Collections.Generic;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Buffers;

namespace PaintSim.Scripts.Stages.DFSPH
{
    /// <summary>
    /// يبني جدول Spatial Hash على CPU.
    ///
    /// القواعد:
    /// 1. يقرأ ParticleBuffer من GPU
    /// 2. يحسب Hash لكل جسيمة نشطة
    /// 3. يرتب حسب Hash
    /// 4. يبني Offset Table
    /// 5. يكتب HashBuffer و HashOffsetBuffer للـ GPU
    ///
    /// ملاحظة: يجب أن يكون حجم HashOffsetBuffer = HashTableSize + 1
    /// </summary>
    public class NeighborSearchDispatcher
    {
        private readonly BufferManager _bufferManager;
        private readonly SimulationConfig _config;
        private readonly int _hashTableSize;

        public NeighborSearchDispatcher(BufferManager bufferManager, SimulationConfig config)
        {
            _bufferManager = bufferManager;
            _config = config;
            _hashTableSize = Mathf.NextPowerOfTwo(config.MaxParticles * 2);
        }

        public void BuildHashTable()
        {
            int maxParticles = _config.MaxParticles;
            float cellSize = _config.SmoothingLength;

            // ── 1. قراءة الجسيمات من GPU ──
            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            var particles = new SPHParticleData[maxParticles];
            particleBuffer.GetData(particles);

            // ── 2. بناء قائمة الـ Entries ──
            var entries = new List<(uint hash, uint index)>();

            for (int i = 0; i < maxParticles; i++)
            {
                if (particles[i].IsActive == 0) continue;

                int cx = Mathf.FloorToInt(particles[i].Position.x / cellSize);
                int cy = Mathf.FloorToInt(particles[i].Position.y / cellSize);
                int cz = Mathf.FloorToInt(particles[i].Position.z / cellSize);

                uint hash = HashCell(cx, cy, cz);
                entries.Add((hash, (uint)i));
            }

            // ── 3. ترتيب حسب Hash ──
            entries.Sort((a, b) => a.hash.CompareTo(b.hash));

            // ── 4. كتابة HashBuffer ──
            uint[] hashBufferData = new uint[_hashTableSize * 2];

            for (int i = 0; i < hashBufferData.Length; i++)
                hashBufferData[i] = 0xFFFFFFFF;

            for (int i = 0; i < entries.Count; i++)
            {
                hashBufferData[i * 2]     = entries[i].hash;
                hashBufferData[i * 2 + 1] = entries[i].index;
            }

            var hashBuffer = _bufferManager.GetBuffer(BufferType.SpatialHashBuffer);
            hashBuffer.SetData(hashBufferData);

            // ── 5. بناء Offset Table ──
            uint[] offsets = new uint[_hashTableSize + 1];

            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = 0xFFFFFFFF;

            offsets[_hashTableSize] = (uint)entries.Count;

            if (entries.Count > 0)
            {
                uint currentHash = entries[0].hash;
                offsets[currentHash] = 0;

                for (int i = 1; i < entries.Count; i++)
                {
                    uint h = entries[i].hash;
                    if (h != currentHash)
                    {
                        offsets[currentHash + 1] = (uint)i;
                        currentHash = h;
                        offsets[currentHash] = (uint)i;
                    }
                }
                offsets[currentHash + 1] = (uint)entries.Count;
            }

            // ── 6. تعبئة الفراغات (Backward Fill) ──
            for (int i = _hashTableSize - 1; i >= 0; i--)
            {
                if (offsets[i] == 0xFFFFFFFF)
                    offsets[i] = offsets[i + 1];
            }

            var offsetBuffer = _bufferManager.GetBuffer(BufferType.HashOffsetBuffer);
            offsetBuffer.SetData(offsets);
        }

        private uint HashCell(int x, int y, int z)
        {
            const uint p1 = 73856093u;
            const uint p2 = 19349663u;
            const uint p3 = 83492791u;
            return ((uint)x * p1 + (uint)y * p2 + (uint)z * p3) % (uint)_hashTableSize;
        }
    }
}