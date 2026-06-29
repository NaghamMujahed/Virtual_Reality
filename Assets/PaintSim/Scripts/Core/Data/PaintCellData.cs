using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// خلية واحدة من شبكة الطلاء على السطح.
    ///
    /// التغييرات عن النسخة السابقة:
    /// - Thickness → ThicknessInt (للـ Atomic Operations على GPU)
    /// - أضفنا Stride ثابت صريح
    /// - الحجم 64 bytes (مضاعف 16)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PaintCellData
    {
        // ─────────────────────────────────────────
        // سماكة الطلاء — مخزنة كـ int للـ Atomic
        // الوحدة : micrometers (μm)
        // 1μm = 0.000001 m
        // نضرب float × THICKNESS_SCALE عند الكتابة
        // نقسم على THICKNESS_SCALE عند القراءة
        //
        // السبب: GPU لا يدعم InterlockedAdd على float
        // فنحول: thickness_m × 1,000,000 → int
        // ─────────────────────────────────────────
        public int ThicknessInt;

        // ─────────────────────────────────────────
        // الرطوبة
        // بلا وحدة : 0.0 → 1.0
        // 0.0 = جاف | 1.0 = رطب تماماً
        // ─────────────────────────────────────────
        public float Wetness;

        // ─────────────────────────────────────────
        // عمر الطلاء
        // الوحدة : s
        // ─────────────────────────────────────────
        public float Age;

        // ─────────────────────────────────────────
        // هل هذه الخلية نشطة؟
        // 0 = فارغة | 1 = فيها طلاء
        // ─────────────────────────────────────────
        public uint IsActive;           // → 16 bytes

        // ─────────────────────────────────────────
        // لون الطلاء المخلوط
        // x=R | y=G | z=B | w=A
        // ─────────────────────────────────────────
        public Vector4 Color;           // → 16 bytes

        // ─────────────────────────────────────────
        // سرعة تدفق الطلاء على السطح (2D)
        // الوحدة : m/s
        // x = أفقي | y = عمودي (على السطح)
        // ─────────────────────────────────────────
        public Vector2 FlowVelocity;

        // ─────────────────────────────────────────
        // كثافة الطلاء
        // الوحدة : kg/m³
        // ─────────────────────────────────────────
        public float PaintDensity;

        // ─────────────────────────────────────────
        // padding لإكمال الـ alignment
        // ─────────────────────────────────────────
        private float _pad0;            // → 16 bytes

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // ThicknessInt    =  4 bytes
        // Wetness         =  4 bytes
        // Age             =  4 bytes
        // IsActive        =  4 bytes  → 16 bytes
        // Color   (V4)    = 16 bytes  → 32 bytes
        // FlowVelocity(V2)=  8 bytes
        // PaintDensity    =  4 bytes
        // _pad0           =  4 bytes  → 48 bytes... 
        // مجموع           = 48 bytes ✓
        // ─────────────────────────────────────────

        // ─────────────────────────────────────────
        // ثوابت
        // ─────────────────────────────────────────

        // حجم الـ struct بالـ bytes — لـ GraphicsBuffer
        public const int Stride = 48;

        // مقياس تحويل float → int
        // 1 m = 1,000,000 μm
        public const int ThicknessScale = 1000000;

        // ─────────────────────────────────────────
        // Thickness كـ float للقراءة
        // يُستخدم في Rendering و WetPaint
        // ─────────────────────────────────────────
        public float Thickness => ThicknessInt / (float)ThicknessScale;

        // ─────────────────────────────────────────
        // تحويل float → int للكتابة
        // ─────────────────────────────────────────
        public static int ToThicknessInt(float thicknessMeters)
            => (int)(thicknessMeters * ThicknessScale);

        // ─────────────────────────────────────────
        // Factory — ينشئ خلية من حدث الاصطدام
        // ─────────────────────────────────────────
        public static PaintCellData CreateFromImpact(
            ImpactEventData impact,
            PaintProperties paint,
            float           cellSize)
        {
            // حجم الجزيئة الكروية
            float volume = (4f / 3f)
                         * Core.Physics.PhysicsConstants.Pi
                         * impact.ParticleRadius
                         * impact.ParticleRadius
                         * impact.ParticleRadius;

            // سماكة = حجم / مساحة الخلية
            float thickness = volume / (cellSize * cellSize);

            return new PaintCellData
            {
                ThicknessInt = ToThicknessInt(thickness),
                Wetness      = 1f,
                Age          = 0f,
                IsActive     = 1u,
                Color        = impact.Color,
                FlowVelocity = Vector2.zero,
                PaintDensity = paint.Density
            };
        }

        // ─────────────────────────────────────────
        // دمج خلية جديدة مع خلية موجودة
        // يُستخدم على CPU فقط
        // GPU يستخدم InterlockedAdd مباشرة
        // ─────────────────────────────────────────
        public PaintCellData MergeWith(PaintCellData incoming)
        {
            int totalThicknessInt = ThicknessInt + incoming.ThicknessInt;

            if (totalThicknessInt <= 0) return this;

            float weightOld = ThicknessInt / (float)totalThicknessInt;
            float weightNew = incoming.ThicknessInt / (float)totalThicknessInt;

            return new PaintCellData
            {
                ThicknessInt = totalThicknessInt,
                Wetness      = Mathf.Max(Wetness, incoming.Wetness),
                Age          = Mathf.Min(Age, incoming.Age),
                IsActive     = 1u,
                Color        = Color * weightOld + incoming.Color * weightNew,
                FlowVelocity = FlowVelocity + incoming.FlowVelocity,
                PaintDensity = PaintDensity * weightOld
                             + incoming.PaintDensity * weightNew
            };
        }

        // ─────────────────────────────────────────
        // خلية فارغة
        // ─────────────────────────────────────────
        public static PaintCellData Empty => new PaintCellData
        {
            ThicknessInt = 0,
            IsActive     = 0u,
            Color        = Vector4.zero
        };
    }
}