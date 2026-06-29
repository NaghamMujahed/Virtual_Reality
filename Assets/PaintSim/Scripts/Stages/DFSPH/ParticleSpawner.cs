using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Core.Physics;

namespace PaintSim.Scripts.Stages.DFSPH
{
    public class ParticleSpawner
    {
        private readonly BufferManager _bufferManager;
        private readonly PaintProperties _paintProperties;
        private readonly SimulationConfig _config;

        private int _activeCount;

        public ParticleSpawner(BufferManager bufferManager, PaintProperties paintProperties, SimulationConfig config)
        {
            _bufferManager = bufferManager;
            _paintProperties = paintProperties;
            _config = config;
        }

        public void Spawn(float deltaTime)
        {
            // ── 1. قراءة ExitState (4 bytes فقط — سريع) ──
            var exitBuffer = _bufferManager.GetBuffer(BufferType.ExitStateBuffer);
            OrificeExitState[] exitStateArray = new OrificeExitState[1];
            exitBuffer.GetData(exitStateArray);
            OrificeExitState exitState = exitStateArray[0];

            if (exitState.IsFlowing < 0.5f || exitState.MassFlowRate <= 0f)
                return;

            // ── 2. حساب عدد الجسيمات ──
            float particleVolume = (4f / 3f) * PhysicsConstants.Pi
                                   * _config.ParticleRadius
                                   * _config.ParticleRadius
                                   * _config.ParticleRadius;

            float particleMass = _paintProperties.Density * particleVolume;
            int particlesToSpawn = Mathf.FloorToInt(exitState.MassFlowRate * deltaTime / particleMass);

            if (particlesToSpawn <= 0) return;

            int remainingSlots = _config.MaxParticles - _activeCount;
            particlesToSpawn = Mathf.Min(particlesToSpawn, remainingSlots);
            if (particlesToSpawn <= 0) return;

            // ── 3. ننشئ بس الجسيمات الجديدة (ما بنقرأ القديم من GPU!) ──
            var newParticles = new SPHParticleData[particlesToSpawn];
            uint nextIndex = (uint)_activeCount;

            for (int i = 0; i < particlesToSpawn; i++)
            {
                uint particleIndex = nextIndex + (uint)i;

                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * exitState.ExitRadius;
                Vector3 spawnPos = exitState.ExitPosition
                    + exitState.ExitDirection * 0.001f
                    + new Vector3(randomCircle.x, 0f, randomCircle.y);

                SPHParticleData particle = SPHParticleData.CreateFromExit(
                    exitState: exitState,
                    paint: _paintProperties,
                    particleIndex: particleIndex,
                    particleRadius: _config.ParticleRadius
                );

              Color[] palette = new Color[]
{
    // Color.red,
    // Color.blue,
    // Color.yellow,
    // Color.green,
    // Color.magenta,
    Color.cyan
};

Color chosen = palette[UnityEngine.Random.Range(0, palette.Length)];
particle.Color = new Vector4(chosen.r, chosen.g, chosen.b, 1f);

                particle.Position = spawnPos;
                newParticles[i] = particle;
            }

            // ── 4. نكتب الجديد للـ GPU مباشرة (بدون قراءة القديم) ──
            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            particleBuffer.SetData(newParticles, 0, _activeCount, particlesToSpawn);

            _activeCount += particlesToSpawn;

            // ── 5. تحديث العداد ──
            var countBuffer = _bufferManager.GetBuffer(BufferType.ActiveCountBuffer);
            countBuffer.SetData(new uint[] { (uint)_activeCount });
        }

        public void DeactivateParticle(uint particleIndex)
        {
            var particleBuffer = _bufferManager.GetBuffer(BufferType.ParticleBuffer);
            SPHParticleData[] particles = new SPHParticleData[1];
            particleBuffer.GetData(particles, (int)particleIndex, 0, 1);
            particles[0].IsActive = 0;
            particles[0].Phase = 0;
            particleBuffer.SetData(particles, 0, (int)particleIndex, 1);
        }

        public void Reset()
        {
            _activeCount = 0;
            _bufferManager.ResetActiveCount();
        }
    }
}