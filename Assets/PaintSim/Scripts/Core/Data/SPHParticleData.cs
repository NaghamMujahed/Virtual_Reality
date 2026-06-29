using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// جزيئة SPH واحدة.
    ///
    /// تمثل حبة طلاء من لحظة خروجها من الفتحة
    /// حتى لحظة ترسبها على السطح.
    ///
    /// تحل محل:
    /// - JetSegmentData  (التيار المتصل)
    /// - DropletData     (القطرات في الهواء)
    ///
    /// دورة الحياة:
    /// تُنشأ  → ParticleSpawner (من OrificeExitState)
    /// تُحدَّث → DFSPHSimulator (كل فريم على GPU)
    /// تُرسَّب → SurfaceImpactSolver (عند لمس السطح)
    /// تُعاد  → SPHParticlePool
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SPHParticleData
    {
        // ─────────────────────────────────────────
        // الموضع في الفضاء
        // الوحدة : m (world space)
        // ─────────────────────────────────────────
        public Vector3 Position;
        private float _pad0;            // → 16 bytes

        // ─────────────────────────────────────────
        // السرعة
        // الوحدة : m/s
        // ─────────────────────────────────────────
        public Vector3 Velocity;
        private float _pad1;            // → 16 bytes

        // ─────────────────────────────────────────
        // تسارع الفريم الحالي
        // الوحدة : m/s²
        // يُصفَّر في بداية كل فريم
        // تتراكم فيه كل القوى: جاذبية، ضغط، لزوجة
        // ─────────────────────────────────────────
        public Vector3 Acceleration;
        private float _pad2;            // → 16 bytes

        // ─────────────────────────────────────────
        // الكثافة المحسوبة
        // الوحدة : kg/m³
        // ρᵢ = Σⱼ mⱼ · W(rᵢⱼ, h)
        // ─────────────────────────────────────────
        public float Density;

        // ─────────────────────────────────────────
        // الضغط
        // الوحدة : Pa
        // يُحسب من خطأ الكثافة في Loop 2
        // ─────────────────────────────────────────
        public float Pressure;

        // ─────────────────────────────────────────
        // معامل Alpha — خاص بـ DFSPH
        // بلا وحدة
        // αᵢ = ρᵢ / ( Σ|∇W|² + |Σ∇W|² )
        // يُحسب مرة واحدة بداية كل فريم
        // يدخل في كلا الـ loops
        // ─────────────────────────────────────────
        public float Alpha;

        // ─────────────────────────────────────────
        // تباعد السرعة
        // بلا وحدة (1/s)
        // ∇·vᵢ = Σⱼ mⱼ/ρⱼ · (vⱼ - vᵢ) · ∇W
        // Loop 1 يسعى لجعله = 0
        // ─────────────────────────────────────────
        public float VelocityDivergence;

        // ─────────────────────────────────────────
        // كتلة الجزيئة
        // الوحدة : kg
        // mᵢ = ρ₀ · (4/3 · π · r³)
        // ثابتة طوال عمر الجزيئة
        // ─────────────────────────────────────────
        public float Mass;

        // ─────────────────────────────────────────
        // نصف قطر التأثير (Smoothing Length)
        // الوحدة : m
        // يحدد نطاق تأثير الجزيئة على جيرانها
        // عادةً h = 2 * radius_particle
        // ─────────────────────────────────────────
        public float SmoothingLength;

        // ─────────────────────────────────────────
        // نصف قطر الجزيئة الفيزيائي
        // الوحدة : m
        // للرسم والتصادم مع السطح
        // ─────────────────────────────────────────
        public float Radius;

        // ─────────────────────────────────────────
        // عمر الجزيئة
        // الوحدة : s
        // للـ Debug وللتأثيرات البصرية
        // ─────────────────────────────────────────
        public float Age;

        // ─────────────────────────────────────────
        // لون الطلاء
        // x=R | y=G | z=B | w=A
        // ─────────────────────────────────────────
        public Vector4 Color;           // → 16 bytes

        // ─────────────────────────────────────────
        // Phase — في أي مرحلة هي الجزيئة؟
        // 0 = FreeFlight  (في الهواء)
        // 1 = Surface     (على السطح أو قريبة جداً)
        // 2 = Deposited   (رُسِّبت — غير نشطة)
        // ─────────────────────────────────────────
        public uint Phase;

        // ─────────────────────────────────────────
        // هل هي نشطة في المحاكاة؟
        // 0 = في الـ Pool (متاحة)
        // 1 = نشطة
        // ─────────────────────────────────────────
        public uint IsActive;

        // ─────────────────────────────────────────
        // فهرس الجزيئة في الـ Buffer
        // يُستخدم في Spatial Hash
        // ─────────────────────────────────────────
        public uint ParticleIndex;

        // padding
        private uint _pad3;

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // Position     (V3+pad) = 16 bytes
        // Velocity     (V3+pad) = 16 bytes
        // Acceleration (V3+pad) = 16 bytes
        // Density              =  4 bytes
        // Pressure             =  4 bytes
        // Alpha                =  4 bytes
        // VelocityDivergence   =  4 bytes
        // Mass                 =  4 bytes
        // SmoothingLength      =  4 bytes
        // Radius               =  4 bytes
        // Age                  =  4 bytes
        // Color      (V4)      = 16 bytes
        // Phase                =  4 bytes
        // IsActive             =  4 bytes
        // ParticleIndex        =  4 bytes
        // _pad3                =  4 bytes
        // المجموع              = 96 bytes ✓ (مضاعف 16)
        // ─────────────────────────────────────────


        // ─────────────────────────────────────────
        // Factory — ينشئ جزيئة من حالة الفتحة
        // ─────────────────────────────────────────
        public static SPHParticleData CreateFromExit(
            OrificeExitState exitState,
            PaintProperties  paint,
            uint             particleIndex,
            float            particleRadius)
        {
            // حجم الجزيئة الكروية
            float volume = (4f / 3f)
                         * Physics.PhysicsConstants.Pi
                         * particleRadius * particleRadius * particleRadius;

            // كتلة الجزيئة من الكثافة المستهدفة
            float mass   = paint.Density * volume;

            return new SPHParticleData
            {
                Position           = exitState.ExitPosition,
                Velocity = exitState.ExitDirection * exitState.ExitSpeed,
                Acceleration       = Vector3.zero,
                Density            = paint.Density,
                Pressure           = 0f,
                Alpha              = 0f,
                VelocityDivergence = 0f,
                Mass               = mass,
                SmoothingLength    = particleRadius * 2f,
                Radius             = particleRadius,
                Age                = 0f,
                Color = Vector4.zero,
                Phase              = 0u,    // FreeFlight
                IsActive           = 1u,
                ParticleIndex      = particleIndex
            };
        }

        // ─────────────────────────────────────────
        // جزيئة فارغة — جاهزة للـ Pool
        // ─────────────────────────────────────────
        public static SPHParticleData Empty => new SPHParticleData
        {
            IsActive = 0u,
            Phase    = 0u
        };
    }
}