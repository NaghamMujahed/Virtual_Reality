namespace PaintSim.Scripts.Core.Interfaces
{
    /// <summary>
    /// كل Stage في الـ Pipeline يجب أن ينفذ هذه الواجهة.
    /// هذا يضمن أن الـ Pipeline يتعامل مع كل المراحل بنفس الطريقة.
    /// </summary>
    public interface ISimulationStage
    {
        /// <summary>
        /// يُستدعى مرة واحدة عند بدء المحاكاة.
        /// ننشئ الـ Buffers ونحمّل الـ Shaders هنا.
        /// </summary>
        void Initialize();

        /// <summary>
        /// يُستدعى كل فريم.
        /// كل Stage يقرأ من Buffer ويكتب لـ Buffer آخر.
        /// </summary>
        void Execute(float deltaTime);

        /// <summary>
        /// يُستدعى عند إيقاف/تدمير المحاكاة.
        /// نحرر الـ ComputeBuffers هنا.
        /// </summary>
        void Cleanup();
    }
}