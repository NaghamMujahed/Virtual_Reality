using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    /// <summary>
    /// يخزن العلاقة بين جزيئتين جارتين.
    ///
    /// يُنشأ في NeighborSearchDispatcher كل فريم.
    /// يُقرأ في كل خطوة من DFSPH:
    ///   - DensityPressure
    ///   - DivergenceFree Loop
    ///   - ConstantDensity Loop
    ///   - Viscosity
    ///   - SurfaceTension
    ///
    /// لماذا نخزن ∇W مسبقاً؟
    /// لأن كل Loop يحتاجه — نحسبه مرة واحدة فقط.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SPHNeighborData
    {
        // ─────────────────────────────────────────
        // فهرس الجزيئة i (المركزية)
        // ─────────────────────────────────────────
        public uint ParticleIndexI;

        // ─────────────────────────────────────────
        // فهرس الجزيئة j (الجارة)
        // ─────────────────────────────────────────
        public uint ParticleIndexJ;

        // ─────────────────────────────────────────
        // المسافة بين i و j
        // الوحدة : m
        // rᵢⱼ = |pᵢ - pⱼ|
        // ─────────────────────────────────────────
        public float Distance;

        // ─────────────────────────────────────────
        // قيمة دالة النواة
        // بلا وحدة
        // W(rᵢⱼ, h)
        // تُستخدم في حساب الكثافة
        // ─────────────────────────────────────────
        public float KernelValue;

        // ─────────────────────────────────────────
        // تدرج دالة النواة
        // الوحدة : 1/m
        // ∇W(rᵢⱼ, h) — متجه من j نحو i
        // يُستخدم في حساب القوى
        // ─────────────────────────────────────────
        public Vector3 KernelGradient;

        // ─────────────────────────────────────────
        // متجه الاتجاه من j إلى i (unit vector)
        // r̂ᵢⱼ = (pᵢ - pⱼ) / |pᵢ - pⱼ|
        // ─────────────────────────────────────────
        // padding بعد Vector3
        private float _pad0;            // → 16 bytes

        // ─────────────────────────────────────────
        // الحجم الكلي:
        // ParticleIndexI   =  4 bytes
        // ParticleIndexJ   =  4 bytes
        // Distance         =  4 bytes
        // KernelValue      =  4 bytes
        // KernelGradient   = 12 bytes
        // _pad0            =  4 bytes
        // المجموع          = 32 bytes ✓ (مضاعف 16)
        // ─────────────────────────────────────────


        // ─────────────────────────────────────────
        // Factory — ينشئ زوج جيران مع حساب الـ Kernel
        //
        // Cubic Spline Kernel:
        // q = r / h
        // W = (1/πh³) × f(q)
        // ─────────────────────────────────────────
        public static SPHNeighborData Create(
            uint    indexI,
            uint    indexJ,
            Vector3 positionI,
            Vector3 positionJ,
            float   smoothingLength)
        {
            Vector3 diff     = positionI - positionJ;
            float   distance = diff.magnitude;

            // تجنب القسمة على صفر
            Vector3 direction = distance > 0.00001f
                              ? diff / distance
                              : Vector3.zero;

            float kernelValue    = ComputeKernel(distance, smoothingLength);
            Vector3 kernelGrad   = ComputeKernelGradient(distance,
                                                          direction,
                                                          smoothingLength);

            return new SPHNeighborData
            {
                ParticleIndexI = indexI,
                ParticleIndexJ = indexJ,
                Distance       = distance,
                KernelValue    = kernelValue,
                KernelGradient = kernelGrad
            };
        }

        // ─────────────────────────────────────────
        // Cubic Spline Kernel
        //
        // q = r / h
        //
        // W(q) = (1 / π·h³) ×
        //   1 - 1.5q² + 0.75q³    (0 ≤ q ≤ 1)
        //   0.25(2 - q)³           (1 < q ≤ 2)
        //   0                      (q > 2)
        // ─────────────────────────────────────────
        public static float ComputeKernel(float r, float h)
        {
            if (h <= 0f) return 0f;

            float q      = r / h;
            float factor = 1f / (Physics.PhysicsConstants.Pi * h * h * h);

            if (q <= 1f)
            {
                return factor * (1f - 1.5f * q * q + 0.75f * q * q * q);
            }
            else if (q <= 2f)
            {
                float twoMinusQ = 2f - q;
                return factor * (0.25f * twoMinusQ * twoMinusQ * twoMinusQ);
            }

            return 0f;
        }

        // ─────────────────────────────────────────
        // تدرج Cubic Spline Kernel
        //
        // dW/dr = (1 / π·h⁴) ×
        //   -3q + 2.25q²           (0 ≤ q ≤ 1)
        //   -0.75(2 - q)²          (1 < q ≤ 2)
        //   0                      (q > 2)
        //
        // ∇W = (dW/dr) × r̂
        // ─────────────────────────────────────────
        public static Vector3 ComputeKernelGradient(
            float   r,
            Vector3 direction,
            float   h)
        {
            if (h <= 0f || r <= 0.00001f) return Vector3.zero;

            float q      = r / h;
            float factor = 1f / (Physics.PhysicsConstants.Pi * h * h * h * h);
            float dWdq   = 0f;

            if (q <= 1f)
            {
                dWdq = factor * (-3f * q + 2.25f * q * q);
            }
            else if (q <= 2f)
            {
                float twoMinusQ = 2f - q;
                dWdq = factor * (-0.75f * twoMinusQ * twoMinusQ);
            }

            return dWdq * direction;
        }
    }
}