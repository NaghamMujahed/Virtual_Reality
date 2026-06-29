namespace PaintSim.Scripts.Core.Buffers
{
    /// <summary>
    /// أنواع البuffers المستخدمة في المحاكاة.
    /// نستخدم هذا الـ Enum لطلب Buffer من BufferManager.
    /// </summary>
    public enum BufferType
    {
        /// <summary>بيانات الجسيمات (SPHParticleData[])</summary>
        ParticleBuffer,

        /// <summary>بيانات الجيران (SPHNeighborData[])</summary>
        NeighborBuffer,

        /// <summary>جدول Spatial Hash (uint2[]: cell hash → start index)</summary>
        SpatialHashBuffer,

        /// <summary>جدول الـ Offsets للـ Hash (uint[])</summary>
        HashOffsetBuffer,

        /// <summary>بيانات خروج الفتحة (OrificeExitState)</summary>
        ExitStateBuffer,

        /// <summary>خلايا الطلاء على السطح (PaintCellData[])</summary>
        PaintCellBuffer,

        /// <summary>عداد الجسيمات النشطة (uint[] بحجم 1)</summary>
        ActiveCountBuffer
    }

    /// <summary>
    /// معلومات ثابتة عن كل نوع Buffer (الحجم، الـ Stride).
    /// </summary>
    public static class BufferDefinitions
    {
        public static int GetStride(BufferType type)
        {
            return type switch
            {
                BufferType.ParticleBuffer     => 112,   // SPHParticleData
                BufferType.NeighborBuffer     => 32,   // SPHNeighborData
                BufferType.SpatialHashBuffer  => 8,    // uint2
                BufferType.HashOffsetBuffer   => 4,    // uint
                BufferType.ExitStateBuffer    => 64,   // OrificeExitState
                BufferType.PaintCellBuffer    => 48,   // PaintCellData
                BufferType.ActiveCountBuffer  => 4,    // uint
                _ => throw new System.ArgumentException($"Unknown buffer type: {type}")
            };
        }
    }
}