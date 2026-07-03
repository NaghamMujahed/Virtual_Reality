# G30E — Paint Material Source Cleanup / Texture Conflict Fix

هذه المرحلة تكمل تنظيف G30D بعد ملاحظة احتمال ظهور لونين أو تضارب textures على اللوحة.

## ما تم تغييره

- `PaintSurface` لم يعد يحتوي معاملات مادة الطلاء مثل:
  - viscosity
  - runoff
  - diffusion
  - evaporation
  - cohesion
  - contact resistance
  - max thickness / wetness shine

- `PaintSurface` الآن مسؤول فقط عن:
  - نوع السطح: Wood / Glass / Fabric / Metal
  - حجم ودقة grid
  - مكان اللوحة وحركتها
  - الرندر والتطور الزمني

- `PaintMaterialConfig` هو مصدر المادة الوحيد:
  - preset
  - density
  - baseColor
  - viscosity/rheology
  - yield stress
  - surface tension
  - drying/absorption

- سلوك فيلم الطلاء على اللوحة يشتق داخليًا من `PaintMaterialConfig` عبر:
  - `EvaluateSurfaceFilmProfile(...)`

## إصلاح تضارب اللون / textures

- تمت إزالة fallback paint color من `SurfaceImpact.compute`.
- لون الترسيب يأتي من جسيمات MLS-MPM نفسها، لا من `baseColor` أثناء الرسم.
- `baseColor` يبقى مفيدًا كمصدر لون مادي/افتراضي عند توليد الجزيئات، لكنه لا يُحقن فوق ألوان الجزيئات عند التصادم مع اللوحة.
- `PaintSurfaceRenderer` لم يعد يربط `_MainTex` مع `_BaseMap` معًا عندما تكون المادة تستخدم `_PaintSurfaceData`.

هذا يمنع مسارين بصريين من عرض نفس texture بطريقة قد تعطي إحساس “لونين فوق بعض”.

## الماء بعد التنظيف

تم تقوية `WaterLike` بصريًا/حركيًا:

- runoff أعلى.
- contact resistance أقل.
- thin-film cohesion أقل.
- inertia response أعلى.

نتيجة التحقق:

- waterShift = 76.65 cells
- latexShift = 15.37 cells
- edgeLoss = 99.95%

## الاختبارات

تم تشغيل:

- `PaintMaterialSurfaceCouplingValidation.Run` — PASS
- `PaintSurfaceMotionMaterialValidation.Run` — PASS
- `PaintSurfacePresetCalibration.Run` — PASS
- `PaintBucketMultiColorValidation.Run` — PASS

صور تمت مراجعتها بصريًا:

- `Logs/G29_SurfacePresetCalibration/G29_Wood_Flow_6s.png`
- `Logs/G30D_MaterialSurfaceCoupling/G30D_Water_InertialFlow.png`
- `Logs/G30B_MultiColorBucket/G30B_RedBlueTwoHoleJets_Flow_3s.png`

## أين أغير المادة الآن؟

غيّر فقط:

`Assets/Scripts/PaintBucketSim/Configs/PaintMaterialConfig_dev.asset`

ولا تضبط مادة الطلاء من:

- `PaintSurface`
- `PaintSimulationHost`
- `GpuMpmSolverConfig`

`GpuMpmSolverConfig` يبقى للاستقرار العددي للـGPU MLS-MPM، وليس كمصدر بصري/مادي للرسم على اللوحة.
