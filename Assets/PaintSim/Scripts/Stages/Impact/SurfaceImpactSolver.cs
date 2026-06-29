// // using UnityEngine;
// // using PaintSim.Scripts.Core.Data;
// // using PaintSim.Scripts.Core.Buffers;

// // namespace PaintSim.Scripts.Stages.Impact
// // {
// //     public class SurfaceImpactSolver
// //     {
// //         private readonly ComputeShader _impactShader;
// //         private readonly BufferManager _bufferManager;
// //         private readonly SimulationConfig _config;
// //         private readonly int _kernelID;

// //         // كاش معرّفات المتغيرات لرفع الأداء الفوري للـ CPU (Shader Property IDs)
// //         private static readonly int MaxParticlesID = Shader.PropertyToID("_MaxParticles");
// //         private static readonly int GridWidthID = Shader.PropertyToID("_GridWidth");
// //         private static readonly int GridHeightID = Shader.PropertyToID("_GridHeight");
// //         private static readonly int CellSizeID = Shader.PropertyToID("_CellSize");
// //         private static readonly int SurfaceHeightID = Shader.PropertyToID("_SurfaceHeight");
// //         private static readonly int PaintDensityID = Shader.PropertyToID("_PaintDensity");
// //         private static readonly int DynamicViscosityID = Shader.PropertyToID("_DynamicViscosity");
// //         private static readonly int SurfaceTensionID = Shader.PropertyToID("_SurfaceTension");

// //         public SurfaceImpactSolver(ComputeShader impactShader, BufferManager bufferManager, SimulationConfig config)
// //         {
// //             _impactShader = impactShader;
// //             _bufferManager = bufferManager;
// //             _config = config;
// //             _kernelID = _impactShader.FindKernel("CSSurfaceImpact");
// //         }

// //         /// <summary>
// // /// معالجة وحساب اصطدام الجسيمات بالسطح وترسيبها.
// // /// </summary>
// // public void SolveImpact(PaintProperties paintProps, float surfaceHeight) // 💥 أضفنا المعامل هنا
// // {
// //     ComputeBuffer particleBuffer = _bufferManager.GetBuffer(BufferType.PaintCellBuffer); // تم تعديلها أيضاً لـ PaintCellBuffer لتطابق الـ Manager عندك
// //     ComputeBuffer paintGridBuffer = _bufferManager.GetBuffer(BufferType.PaintCellBuffer);

// //     if (particleBuffer == null || paintGridBuffer == null) return;

// //     _impactShader.SetBuffer(_kernelID, "_ParticleBuffer", particleBuffer);
// //     _impactShader.SetBuffer(_kernelID, "_PaintGridBuffer", paintGridBuffer);

// //     _impactShader.SetInt(MaxParticlesID, _config.MaxParticles);
// //     _impactShader.SetInt(GridWidthID, _config.GridWidth);
// //     _impactShader.SetInt(GridHeightID, _config.GridHeight);
// //     _impactShader.SetFloat(CellSizeID, _config.CellSize);
    
// //     // 💥 التعديل هنا: نمرر المتغير المباشر القادم من الدالة
// //     _impactShader.SetFloat(SurfaceHeightID, surfaceHeight); 

// //     _impactShader.SetFloat(PaintDensityID, paintProps.Density);
// //     _impactShader.SetFloat(DynamicViscosityID, paintProps.DynamicViscosity);
// //     _impactShader.SetFloat(SurfaceTensionID, paintProps.SurfaceTension);

// //     int threadGroups = Mathf.CeilToInt((float)_config.MaxParticles / 64f);
// //     if (threadGroups > 0)
// //     {
// //         _impactShader.Dispatch(_kernelID, threadGroups, 1, 1);
// //     }
// // }    }
// // }




// #pragma kernel CSMain

// // ─────────────────────────────────────────
// // Structs — يجب أن تطابق C# byte-for-byte
// // ─────────────────────────────────────────

// struct SPHParticleData
// {
//     float3 Position;     float _pad0;
//     float3 Velocity;     float _pad1;
//     float3 Acceleration; float _pad2;
//     float  Density;
//     float  Pressure;
//     float  Alpha;
//     float  VelocityDivergence;
//     float  Mass;
//     float  SmoothingLength;
//     float  Radius;
//     float  Age;
//     float4 Color;
//     uint   Phase;
//     uint   IsActive;
//     uint   ParticleIndex;
//     uint   _pad3;
// };

// struct PaintCellData
// {
//     int    ThicknessInt;
//     float  Wetness;
//     float  Age;
//     uint   IsActive;
//     float4 Color;
//     float2 FlowVelocity;
//     float  PaintDensity;
//     float  _pad0;
// };

// // ─────────────────────────────────────────
// // Buffers
// // ─────────────────────────────────────────
// RWStructuredBuffer<SPHParticleData> _ParticleBuffer;
// RWStructuredBuffer<PaintCellData>   _PaintCellBuffer;

// // ─────────────────────────────────────────
// // Constants
// // ─────────────────────────────────────────
// int   _GridWidth;
// int   _GridHeight;
// float _CellSize;
// float _GridOriginX;
// float _GridOriginZ;
// float _SurfaceY;

// // خصائص الطلاء
// float _PaintDensity;
// float _PaintViscosity;
// float _SurfaceTension;

// // تحويل السماكة
// int   _ThicknessScale;  // = 1,000,000

// // ─────────────────────────────────────────
// // ثوابت
// // ─────────────────────────────────────────
// #define PI          3.14159265f
// #define TWO_THIRDS  0.66666667f

// // حدود أنظمة الاصطدام
// #define WE_DEPOSITION   5.0f
// #define WE_SPREAD       30.0f
// #define K_SPLASH        57.7f

// // ─────────────────────────────────────────
// // دالة مساعدة: هل الـ index داخل الشبكة؟
// // ─────────────────────────────────────────
// bool InBounds(int i, int j)
// {
//     return i >= 0 && i < _GridWidth
//         && j >= 0 && j < _GridHeight;
// }

// // ─────────────────────────────────────────
// // دالة مساعدة: اكتب في خلية بـ Atomic
// // ─────────────────────────────────────────
// void WriteToCell(
//     int   i,
//     int   j,
//     float weight,
//     int   totalThicknessInt,
//     float wetness,
//     float4 color,
//     float density)
// {
//     if (!InBounds(i, j)) return;

//     int flatIndex = j * _GridWidth + i;

//     // سماكة مرجحة بالوزن
//     int weightedThickness = (int)(totalThicknessInt * weight);

//     if (weightedThickness <= 0) return;

//     // ── Atomic Add للسماكة ──────────────────
//     InterlockedAdd(
//         _PaintCellBuffer[flatIndex].ThicknessInt,
//         weightedThickness
//     );

//     // ── تفعيل الخلية ────────────────────────
//     InterlockedMax(
//         (int)_PaintCellBuffer[flatIndex].IsActive,
//         1
//     );

//     // ── Wetness ─────────────────────────────
//     // نأخذ الأعلى — لا يوجد Atomic float
//     // نستخدم تقريب بسيط
//     if (wetness > _PaintCellBuffer[flatIndex].Wetness)
//         _PaintCellBuffer[flatIndex].Wetness = wetness;

//     // ── اللون بالوزن ────────────────────────
//     // خلط تراكمي: نضيف اللون مرجحاً بالسماكة
//     // سيُقسم لاحقاً على السماكة الكلية في WetPaintEvolver
//     _PaintCellBuffer[flatIndex].Color     += color * weight;
//     _PaintCellBuffer[flatIndex].PaintDensity = density;
// }

// // ─────────────────────────────────────────
// // الـ Kernel الرئيسي
// // كل Thread = جسيمة واحدة
// // ─────────────────────────────────────────
// [numthreads(64, 1, 1)]
// void CSMain(uint3 id : SV_DispatchThreadID)
// {
//     uint particleIdx = id.x;

//     // تجاهل الـ Threads الزائدة
//     uint totalParticles, stride;
//     _ParticleBuffer.GetDimensions(totalParticles, stride);
//     if (particleIdx >= totalParticles) return;

//     SPHParticleData p = _ParticleBuffer[particleIdx];

//     // تجاهل الجسيمات غير النشطة
//     if (p.IsActive == 0) return;

//     // ── شرط الاصطدام ────────────────────────
//     // الجسيمة تصطدم عندما مركزها يصل لـ:
//     // SurfaceY + Radius
//     float impactThreshold = _SurfaceY + p.Radius;
//     if (p.Position.y > impactThreshold) return;

//     // ── حساب السرعة عند الاصطدام ────────────
//     float V = length(p.Velocity);
//     float D = p.Radius * 2.0f;

//     // تجنب القسمة على صفر
//     if (V < 0.001f || D < 0.000001f)
//     {
//         // سرعة صفر = ترسيب مباشر بدون حساب
//         // سنتعامل معها كـ Deposition
//     }

//     // ── الأرقام اللابعدية ───────────────────
//     float We = (_PaintDensity * V * V * D) / max(_SurfaceTension, 0.00001f);
//     float Re = (_PaintDensity * V * D)     / max(_PaintViscosity,  0.00001f);
//     float K  = We * sqrt(max(Re, 0.0f));

//     // ── تحديد نظام الاصطدام ─────────────────
//     // 0 = Deposition | 1 = Spread
//     // 2 = Splash     | 3 = Corona
//     uint regime = 0u;
//     if      (We < WE_DEPOSITION)              regime = 0u;
//     else if (We < WE_SPREAD)                  regime = 1u;
//     else if (K  < K_SPLASH)                   regime = 2u;
//     else                                      regime = 3u;

//     // ── حجم الجسيمة ─────────────────────────
//     float volume    = TWO_THIRDS * PI * p.Radius * p.Radius * p.Radius;
//     // سماكة الطلاء المترسب
//     float thickness = volume / (_CellSize * _CellSize);
//     int   thicknessInt = (int)(thickness * (float)_ThicknessScale);

//     // نسبة ما يُترسَّب حسب النظام
//     float depositionFraction = 1.0f;
//     if      (regime == 1u) depositionFraction = 0.95f; // Spread
//     else if (regime == 2u) depositionFraction = 0.70f; // Splash
//     else if (regime == 3u) depositionFraction = 0.50f; // Corona

//     int depositedThicknessInt = (int)(thicknessInt * depositionFraction);

//     // ── Bilinear Splat ───────────────────────
//     // الجسيمة تؤثر على 4 خلايا مجاورة

//     float localX = (p.Position.x - _GridOriginX) / _CellSize;
//     float localZ = (p.Position.z - _GridOriginZ) / _CellSize;

//     int   baseI  = (int)floor(localX);
//     int   baseJ  = (int)floor(localZ);

//     float fx     = localX - (float)baseI; // كسري 0→1
//     float fz     = localZ - (float)baseJ; // كسري 0→1

//     // أوزان الـ Bilinear
//     float w00 = (1.0f - fx) * (1.0f - fz);
//     float w10 = fx           * (1.0f - fz);
//     float w01 = (1.0f - fx) * fz;
//     float w11 = fx           * fz;

//     // رطوبة = دائماً 1 عند الترسيب
//     float wetness = 1.0f;

//     // اكتب في الخلايا الأربع
//     WriteToCell(baseI,     baseJ,     w00, depositedThicknessInt, wetness, p.Color, _PaintDensity);
//     WriteToCell(baseI + 1, baseJ,     w10, depositedThicknessInt, wetness, p.Color, _PaintDensity);
//     WriteToCell(baseI,     baseJ + 1, w01, depositedThicknessInt, wetness, p.Color, _PaintDensity);
//     WriteToCell(baseI + 1, baseJ + 1, w11, depositedThicknessInt, wetness, p.Color, _PaintDensity);

//     // ── تعطيل الجسيمة ───────────────────────
//     // Phase = 1 → على السطح (لا تتحرك)
//     // Integration.compute يتجاهل Phase == 1
//     _ParticleBuffer[particleIdx].Phase    = 1u;
//     _ParticleBuffer[particleIdx].IsActive = 0u;
// }