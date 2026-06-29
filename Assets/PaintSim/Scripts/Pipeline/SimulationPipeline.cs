using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Stages.DFSPH;
using PaintSim.Scripts.Stages.Exit;

namespace PaintSim.Scripts.Pipeline
{
    /// <summary>
    /// يملك التسلسل الكامل للمحاكاة.
    ///
    /// الترتيب كل فريم:
    ///   1. ExitModel        → يحسب Q و ṁ على GPU
    ///   2. ParticleSpawner  → يقرأ Exit ويولد جسيمات جديدة
    ///   3. DFSPHSimulator   → يشغل SpatialHash → Density → Divergence → Density → Integration
    ///
    /// لا يحتوي على أي فيزياء — فقط توجيه.
    /// </summary>
    public class SimulationPipeline
    {
        private readonly BufferManager _bufferManager;
        private readonly OrificeExitModel _exitModel;
        private readonly ParticleSpawner _spawner;
        private readonly DFSPHSimulator _dfsphSimulator;

        public SimulationPipeline(
            BufferManager bufferManager,
            OrificeExitModel exitModel,
            ParticleSpawner spawner,
            DFSPHSimulator dfsphSimulator)
        {
            _bufferManager = bufferManager;
            _exitModel = exitModel;
            _spawner = spawner;
            _dfsphSimulator = dfsphSimulator;
        }

        /// <summary>
        /// يُستدعى مرة واحدة في Start.
        /// </summary>
        public void Initialize()
        {
            _bufferManager.Initialize();
            _exitModel.Initialize();
            _dfsphSimulator.Initialize();
        }

        /// <summary>
        /// يُستدعى كل FixedUpdate.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // 1. حساب Exit Velocity (GPU)
            _exitModel.Execute(deltaTime);

            // 2. توليد جسيمات جديدة (CPU Readback + GPU Write)
            _spawner.Spawn(deltaTime);

            // 3. تشغيل DFSPH بالكامل (GPU)
            _dfsphSimulator.Execute(deltaTime);
        }

        /// <summary>
        /// يُستدعى عند تدمير الكائن.
        /// </summary>
        public void Cleanup()
        {
            _dfsphSimulator.Cleanup();
            _exitModel.Cleanup();
            _bufferManager.Cleanup();
        }
    }
}