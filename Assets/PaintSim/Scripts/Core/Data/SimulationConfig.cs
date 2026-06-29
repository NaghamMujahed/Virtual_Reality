using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// المصدر الوحيد لكل إعدادات المحاكاة.
    ///
    /// القواعد:
    /// - يُنشأ مرة واحدة في SimulationPipeline
    /// - يُحقن في كل مرحلة عند التهيئة
    /// - لا أحد يعدّله بعد Initialize()
    /// - يُرفع للـ GPU كـ ConstantBuffer
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SimulationConfig
    {
        // ═════════════════════════════════════════
        // القسم الأول — الفيزياء العامة
        // ═════════════════════════════════════════

        // ─────────────────────────────────────────
        // متجه الجاذبية
        // الوحدة : m/s²
        // عادةً (0, -9.81, 0)
        // ─────────────────────────────────────────
        public Vector3 Gravity;
        private float _pad0;            // → 16 bytes

        // ─────────────────────────────────────────
        // اتجاه الريح وسرعته
        // الوحدة : m/s
        // (0,0,0) = لا يوجد ريح
        // ─────────────────────────────────────────
        public Vector3 WindVelocity;
        private float _pad1;            // → 16 bytes

        // ─────────────────────────────────────────
        // كثافة الهواء
        // الوحدة : kg/m³
        // عند 20°C = 1.204
        // ─────────────────────────────────────────
        public float AirDensity;

        // ─────────────────────────────────────────
        // رطوبة الهواء
        // بلا وحدة : 0.0 → 1.0
        // تؤثر على معدل تبخر الطلاء
        // ─────────────────────────────────────────
        public float AirHumidity;

        // ─────────────────────────────────────────
        // درجة الحرارة
        // الوحدة : °C
        // تؤثر على اللزوجة والتبخر
        // ─────────────────────────────────────────
        public float Temperature;

        // padding
        private float _pad2;            // → 16 bytes

        // ═════════════════════════════════════════
        // القسم الثاني — إعدادات DFSPH
        // ═════════════════════════════════════════

        // ─────────────────────────────────────────
        // الحد الأقصى لعدد الجزيئات
        // يحدد حجم الـ GraphicsBuffer
        // 100,000 = متوسط | 500,000 = عالي
        // ─────────────────────────────────────────
        public int MaxParticles;

        // ─────────────────────────────────────────
        // نصف قطر الجزيئة
        // الوحدة : m
        // صغير = دقة أعلى + تكلفة أعلى
        // 0.005 = 5mm جيد للبداية
        // ─────────────────────────────────────────
        public float ParticleRadius;

        // ─────────────────────────────────────────
        // Smoothing Length
        // الوحدة : m
        // عادةً h = 2 * ParticleRadius
        // ─────────────────────────────────────────
        public float SmoothingLength;

        // ─────────────────────────────────────────
        // الكثافة المستهدفة
        // الوحدة : kg/m³
        // مأخوذة من PaintProperties.Density
        // محفوظة هنا لسرعة الوصول من GPU
        // ─────────────────────────────────────────
        public float RestDensity;

        // ─────────────────────────────────────────
        // الحد الأقصى لتكرار Loop 1 (Divergence-Free)
        // عادةً 1 → 3 تكرارات تكفي
        // ─────────────────────────────────────────
        public int MaxDivergenceIterations;

        // ─────────────────────────────────────────
        // الحد الأقصى لتكرار Loop 2 (Constant Density)
        // عادةً 2 → 5 تكرارات
        // ─────────────────────────────────────────
        public int MaxDensityIterations;

        // ─────────────────────────────────────────
        // حد الخطأ المقبول في Loop 1
        // بلا وحدة
        // عندما يصل الخطأ لهذا الرقم نوقف الـ Loop
        // عادةً 0.001
        // ─────────────────────────────────────────
        public float DivergenceThreshold;

        // ─────────────────────────────────────────
        // حد الخطأ المقبول في Loop 2
        // الوحدة : kg/m³
        // عادةً 0.1
        // ─────────────────────────────────────────
        public float DensityThreshold;   // → 32 bytes هذا القسم

        // ═════════════════════════════════════════
        // القسم الثالث — الشبكة السطحية
        // ═════════════════════════════════════════

        // ─────────────────────────────────────────
        // عدد خلايا الشبكة أفقياً
        // ─────────────────────────────────────────
        public int GridWidth;

        // ─────────────────────────────────────────
        // عدد خلايا الشبكة عمودياً
        // ─────────────────────────────────────────
        public int GridHeight;

        // ─────────────────────────────────────────
        // حجم الخلية الواحدة
        // الوحدة : m
        // عادةً يساوي ParticleRadius
        // ─────────────────────────────────────────
        public float CellSize;

        // ─────────────────────────────────────────
        // معامل انتشار الطلاء على السطح
        // الوحدة : m²/s
        // ─────────────────────────────────────────
        public float DiffusionCoefficient;

        // ─────────────────────────────────────────
        // معدل التبخر
        // بلا وحدة (1/s)
        // ─────────────────────────────────────────
        public float EvaporationRate;

        

        // padding
        private float _pad3;
        private float _pad4;
        private float _pad5;            // → 32 bytes هذا القسم

        // ═════════════════════════════════════════
        // القسم الرابع — الأداء
        // ═════════════════════════════════════════

        // ─────────────────────────────────────────
        // حجم Thread Group في Compute Shader
        // يجب أن يكون 64 أو 128 أو 256
        // ─────────────────────────────────────────
        public int ThreadGroupSize;

        // ─────────────────────────────────────────
        // الحد الأقصى لجيران كل جزيئة
        // في الـ Neighbor Buffer
        // عادةً 64
        // ─────────────────────────────────────────
        public int MaxNeighborsPerParticle;

        // ─────────────────────────────────────────
        // خطوة الزمن الثابتة للمحاكاة
        // الوحدة : s
        // DFSPH يعمل أفضل مع خطوة ثابتة
        // عادةً 0.002 (500 Hz)
        // ─────────────────────────────────────────
        public float FixedTimeStep;

        // padding
        private float _pad6;            // → 16 bytes هذا القسم

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // Gravity      (V3+pad) = 16 bytes
        // WindVelocity (V3+pad) = 16 bytes
        // AirDensity            =  4 bytes
        // AirHumidity           =  4 bytes
        // Temperature           =  4 bytes
        // _pad2                 =  4 bytes  → 16 bytes
        // MaxParticles          =  4 bytes
        // ParticleRadius        =  4 bytes
        // SmoothingLength       =  4 bytes
        // RestDensity           =  4 bytes
        // MaxDivergenceIter     =  4 bytes
        // MaxDensityIter        =  4 bytes
        // DivergenceThreshold   =  4 bytes
        // DensityThreshold      =  4 bytes  → 32 bytes
        // GridWidth             =  4 bytes
        // GridHeight            =  4 bytes
        // CellSize              =  4 bytes
        // DiffusionCoeff        =  4 bytes
        // EvaporationRate       =  4 bytes
        // _pad3,4,5             = 12 bytes  → 32 bytes
        // ThreadGroupSize       =  4 bytes
        // MaxNeighborsPerParticle=  4 bytes
        // FixedTimeStep         =  4 bytes
        // _pad6                 =  4 bytes  → 16 bytes
        // المجموع               = 128 bytes ✓ (مضاعف 16)
        // ─────────────────────────────────────────


        // ─────────────────────────────────────────
        // Default — إعدادات ابتدائية متوازنة
        // جيدة للاختبار الأول
        // ─────────────────────────────────────────
        public static SimulationConfig Default => new SimulationConfig
        {
            // الفيزياء
            Gravity                  = new Vector3(0f, -9.81f, 0f),
            WindVelocity             = Vector3.zero,
            AirDensity               = 1.204f,
            AirHumidity              = 0.5f,
            Temperature              = 20f,

            // DFSPH
            MaxParticles             = 100000,
            ParticleRadius           = 0.005f,
            SmoothingLength          = 0.01f,
            RestDensity              = 1200f,
            MaxDivergenceIterations  = 3,
            MaxDensityIterations     = 5,
            DivergenceThreshold      = 0.001f,
            DensityThreshold         = 0.1f,

            // الشبكة
            GridWidth                = 512,
            GridHeight               = 512,
            CellSize                 = 0.005f,
            DiffusionCoefficient     = 0.00001f,
            EvaporationRate          = 0.05f,

            // الأداء
            ThreadGroupSize          = 64,
            MaxNeighborsPerParticle  = 64,
            FixedTimeStep            = 0.002f
        };
    }
}