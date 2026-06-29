using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// حالة الطلاء لحظة خروجه من الفتحة.
    ///
    /// هذا الملف هو:
    /// - المخرج الوحيد من Stage: Exit
    /// - المدخل الوحيد لـ Stage: Jet
    ///
    /// القواعد:
    /// - struct لأنها تُرفع للـ GPU
    /// - كل المتجهات Vector3 → على GPU تصبح float3
    /// - الحجم يجب أن يكون مضاعف 16
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct OrificeExitState
    {
        // ─────────────────────────────────────────
        // موضع الفتحة في الفضاء
        // الوحدة : m
        // يتغير كل فريم إذا كان الدلو يتحرك
        // ─────────────────────────────────────────
        public Vector3 ExitPosition;

        // ─────────────────────────────────────────
        // padding إجباري بعد Vector3
        // Vector3 = 12 byte → GPU يتوقع 16
        // نضيف float واحد = 4 byte لنكمل 16
        // ─────────────────────────────────────────
        private float _pad0;

        // ─────────────────────────────────────────
        // اتجاه خروج الطلاء (unit vector)
        // الوحدة : بلا وحدة (متجه طول 1)
        // عادةً يكون للأسفل (0, -1, 0)
        // لكن قد يتغير إذا كان الدلو مائلاً
        // ─────────────────────────────────────────
        public Vector3 ExitDirection;

        // padding
        private float _pad1;

        // ─────────────────────────────────────────
        // سرعة الخروج
        // الوحدة : m/s
        // تُحسب من معادلة برنولي أو الاستمرارية
        // V = Cd · √(2·ΔP / ρ)
        // ─────────────────────────────────────────
        public float ExitSpeed;

        // ─────────────────────────────────────────
        // نصف قطر الفتحة
        // الوحدة : m
        // ─────────────────────────────────────────
        public float ExitRadius;

        // ─────────────────────────────────────────
        // معدل التدفق الحجمي
        // الوحدة : m³/s
        // Q = A · V = π·r² · V
        // ─────────────────────────────────────────
        public float VolumeFlowRate;

        // ─────────────────────────────────────────
        // معدل التدفق الكتلي
        // الوحدة : kg/s
        // ṁ = ρ · Q
        // ─────────────────────────────────────────
        public float MassFlowRate;

        // ─────────────────────────────────────────
        // معامل التصريف | Discharge Coefficient
        // بلا وحدة | نطاق: 0.6 - 0.99
        // يمثل نسبة التدفق الحقيقي للنظري
        // فتحة حادة الحواف : Cd ≈ 0.61
        // فتحة ناعمة       : Cd ≈ 0.98
        // ─────────────────────────────────────────
        public float DischargeCoefficient;

        // ─────────────────────────────────────────
        // هل الطلاء يتدفق الآن؟
        // 1.0 = يتدفق | 0.0 = متوقف
        // float بدل bool لأن GPU لا يدعم bool في buffer
        // ─────────────────────────────────────────
        public float IsFlowing;

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // ExitPosition       = 12 bytes
        // _pad0              =  4 bytes  → 16 ✓
        // ExitDirection      = 12 bytes
        // _pad1              =  4 bytes  → 16 ✓
        // ExitSpeed          =  4 bytes
        // ExitRadius         =  4 bytes
        // VolumeFlowRate     =  4 bytes
        // MassFlowRate       =  4 bytes  → 16 ✓
        // DischargeCoeff     =  4 bytes
        // IsFlowing          =  4 bytes
        // padding            =  8 bytes
        // المجموع            = 64 bytes ✓ (مضاعف 16)
        // ─────────────────────────────────────────
        private float _pad2;
        private float _pad3;


        // ─────────────────────────────────────────
        // Factory
        // ─────────────────────────────────────────
        public static OrificeExitState Create(
            Vector3 exitPosition,
            Vector3 exitDirection,
            float   exitSpeed,
            float   exitRadius,
            float   dischargeCoefficient,
            float   density)
        {
            float area           = Mathf.PI * exitRadius * exitRadius;
            float volumeFlowRate = area * exitSpeed;
            float massFlowRate   = density * volumeFlowRate;

            return new OrificeExitState
            {
                ExitPosition          = exitPosition,
                ExitDirection         = exitDirection.normalized,
                ExitSpeed             = exitSpeed,
                ExitRadius            = exitRadius,
                VolumeFlowRate        = volumeFlowRate,
                MassFlowRate          = massFlowRate,
                DischargeCoefficient  = dischargeCoefficient,
                IsFlowing             = 1.0f
            };
        }

        // ─────────────────────────────────────────
        // حالة فارغة — لا تدفق
        // ─────────────────────────────────────────
        public static OrificeExitState Empty => new OrificeExitState
        {
            IsFlowing = 0.0f
        };
    }
}