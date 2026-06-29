using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// حالة جزيئة SPH في لحظة اصطدامها بالسطح.
    ///
    /// يُنشأ في  : SurfaceImpactSolver
    /// يُقرأ في  : ImpactRegimeClassifier
    ///             PaintDepositor
    ///             SplashDropletSpawner
    ///
    /// لا أحد يعدّله بعد إنشائه — immutable بالاتفاق.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ImpactEventData
    {
        // ─────────────────────────────────────────
        // موضع الاصطدام على السطح
        // الوحدة : m (world space)
        // ─────────────────────────────────────────
        public Vector3 ImpactPosition;
        private float _pad0;            // → 16 bytes

        // ─────────────────────────────────────────
        // سرعة الجزيئة لحظة الاصطدام
        // الوحدة : m/s
        // ─────────────────────────────────────────
        public Vector3 ImpactVelocity;
        private float _pad1;            // → 16 bytes

        // ─────────────────────────────────────────
        // نظام السطح عند نقطة الاصطدام
        // ─────────────────────────────────────────
        public Vector3 SurfaceNormal;
        private float _pad2;            // → 16 bytes

        // ─────────────────────────────────────────
        // Weber Number لحظة الاصطدام
        // We = ρ · V² · D / σ
        // يحدد طاقة الاصطدام
        // ─────────────────────────────────────────
        public float WeberNumber;

        // ─────────────────────────────────────────
        // Reynolds Number لحظة الاصطدام
        // Re = ρ · V · D / μ
        // ─────────────────────────────────────────
        public float ReynoldsNumber;

        // ─────────────────────────────────────────
        // معامل K — يحدد النظام النهائي
        // K = We · sqrt(Re)
        // ─────────────────────────────────────────
        public float SplashParameter;

        // ─────────────────────────────────────────
        // زاوية الاصطدام مع السطح
        // الوحدة : degrees
        // 90° = عمودي تماماً
        // 0°  = مماسي تماماً
        // ─────────────────────────────────────────
        public float ImpactAngle;

        // ─────────────────────────────────────────
        // نصف قطر الجزيئة عند الاصطدام
        // الوحدة : m
        // ─────────────────────────────────────────
        public float ParticleRadius;

        // ─────────────────────────────────────────
        // كتلة الجزيئة عند الاصطدام
        // الوحدة : kg
        // ─────────────────────────────────────────
        public float ParticleMass;

        // ─────────────────────────────────────────
        // لون الطلاء
        // x=R | y=G | z=B | w=A
        // ─────────────────────────────────────────
        public Vector4 Color;           // → 16 bytes

        // ─────────────────────────────────────────
        // نظام الاصطدام المحدد
        // 0 = Deposition
        // 1 = Spread
        // 2 = Splash
        // 3 = Corona
        // ─────────────────────────────────────────
        public uint ImpactRegime;

        // ─────────────────────────────────────────
        // فهرس الجزيئة الأصلية في الـ Buffer
        // للرجوع إليها عند الحاجة
        // ─────────────────────────────────────────
        public uint SourceParticleIndex;

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // ImpactPosition   (V3+pad) = 16 bytes
        // ImpactVelocity   (V3+pad) = 16 bytes
        // SurfaceNormal    (V3+pad) = 16 bytes
        // WeberNumber              =  4 bytes
        // ReynoldsNumber           =  4 bytes
        // SplashParameter          =  4 bytes
        // ImpactAngle              =  4 bytes
        // ParticleRadius           =  4 bytes
        // ParticleMass             =  4 bytes
        // Color            (V4)    = 16 bytes
        // ImpactRegime             =  4 bytes
        // SourceParticleIndex      =  4 bytes
        // المجموع                  = 96 bytes ✓ (مضاعف 16)
        // ─────────────────────────────────────────


        // ─────────────────────────────────────────
        // Factory — ينشئ ImpactEventData من جزيئة
        // ويحسب كل الأرقام اللابعدية تلقائياً
        // ─────────────────────────────────────────
        public static ImpactEventData Create(
            SPHParticleData particle,
            Vector3         surfaceNormal,
            PaintProperties paint)
        {
            float speed    = particle.Velocity.magnitude;
            float diameter = particle.Radius * 2f;

            // Weber Number
            float we = paint.Density
                     * speed * speed
                     * diameter
                     / paint.SurfaceTension;

            // Reynolds Number
            float re = paint.Density
                     * speed
                     * diameter
                     / paint.DynamicViscosity;

            // معامل K
            float k = we * Mathf.Sqrt(re);

            // زاوية الاصطدام
            float cosAngle = Mathf.Abs(
                Vector3.Dot(particle.Velocity.normalized, -surfaceNormal)
            );
            float angle = Mathf.Acos(
                Mathf.Clamp(cosAngle, 0f, 1f)
            ) * Mathf.Rad2Deg;

            // تحديد النظام
            uint regime = ClassifyRegime(we, k);

            return new ImpactEventData
            {
                ImpactPosition      = particle.Position,
                ImpactVelocity      = particle.Velocity,
                SurfaceNormal       = surfaceNormal,
                WeberNumber         = we,
                ReynoldsNumber      = re,
                SplashParameter     = k,
                ImpactAngle         = angle,
                ParticleRadius      = particle.Radius,
                ParticleMass        = particle.Mass,
                Color               = particle.Color,
                ImpactRegime        = regime,
                SourceParticleIndex = particle.ParticleIndex
            };
        }

        // ─────────────────────────────────────────
        // تحديد نظام الاصطدام
        //
        // We < 5              → Deposition  (0)
        // 5  ≤ We < 30        → Spread      (1)
        // We ≥ 30, K < 57.7   → Splash      (2)
        // We ≥ 30, K ≥ 57.7   → Corona      (3)
        // ─────────────────────────────────────────
        private static uint ClassifyRegime(float we, float k)
        {
            if (we < 5f)                        return 0u; // Deposition
            if (we < 30f)                       return 1u; // Spread
            if (k  < 57.7f)                     return 2u; // Splash
                                                return 3u; // Corona
        }
    }
}