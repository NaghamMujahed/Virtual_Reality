using UnityEngine;
using UnityEngine.Rendering;
using PaintSim.Scripts.Core.Buffers;

namespace PaintSim.Scripts.Rendering
{
    public class ParticleRenderer
    {
        private readonly BufferManager _bufferManager;
        private readonly Material      _material;
        private readonly Bounds        _bounds;

        public ParticleRenderer(BufferManager bufferManager, Shader shader)
        {
            _bufferManager = bufferManager;
            _material      = new Material(shader);
            
            _bounds        = new Bounds(Vector3.zero, Vector3.one * 1000f);

            _material.SetFloat("_ParticleSize", 8.0f);
            _material.SetFloat("_Softness",     4.0f);
            _material.SetFloat("_Stretch",      2.0f);
            _material.SetFloat("_Glow",         0.5f);
        }

        public void Render()
        {
            if (_material == null) return;

            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            if (particleBuffer == null) return;

            int count = Mathf.Clamp(
                _bufferManager.GetActiveParticleCount(),
                0,
                particleBuffer.count
            );

            if (count <= 0) return;

            _material.SetBuffer("_ParticleBuffer", particleBuffer);

            Graphics.DrawProcedural(
                _material,
                _bounds,
                MeshTopology.Triangles,
                6,         
                count,      
                null,       
                null,       
                ShadowCastingMode.Off, 
                false,      
                0           
            );
        }

        public void SetParticleSize(float size)
            => _material.SetFloat("_ParticleSize", Mathf.Max(0.1f, size));

        public void SetSoftness(float softness)
            => _material.SetFloat("_Softness", Mathf.Max(0.1f, softness));

        public void SetStretch(float stretch)
            => _material.SetFloat("_Stretch", Mathf.Max(0f, stretch));

        public void SetGlow(float glow)
            => _material.SetFloat("_Glow", Mathf.Max(0f, glow));

        public void Cleanup()
        {
            if (_material != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_material);
                else
                    Object.DestroyImmediate(_material);
            }
        }
    }
}