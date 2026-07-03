# G30D — Material/Surface Coupling Cleanup

هذه المرحلة تعالج ثلاث نقاط كانت تسبب التباسًا بصريًا وفيزيائيًا:

1. `PaintMaterialConfig` أصبح مصدر الحقيقة لسلوك المادة.
2. الفيلم على اللوحة صار يتبع مادة السائل فعليًا، وليس معاملات منفصلة في `PaintSurface` أو `PaintSimulationHost`.
3. الماء/المواد الخفيفة تستجيب لحركة وتسارع اللوحة بعد الترسيب، بينما الطلاء الأثقل يقاوم الجريان.

## مصدر الإعدادات الصحيح

- غيّر نوع المادة من:
  - `Assets/Scripts/PaintBucketSim/Configs/PaintMaterialConfig_dev.asset`
- استخدم `materialPreset`:
  - `WaterLike`
  - `ThinPaint`
  - `LatexPaint`
  - `ThickPaint`
  - `HeavyBodyPaint`
  - `Custom`

`GpuMpmSolverConfig.paintMaterialPreset` لم يعد مصدر المادة البصرية. هو الآن package عددي لاستقرار GPU MLS-MPM، مثل الضغط، projection، cohesion، وحدود السرعات. إذا اختلف preset السولفر عن preset المادة، سيظهر تحذير من `PaintSimulationHost`.

## ما تم ربطه بالمادة

- Density / color fallback.
- Constant / Carreau-Yasuda viscosity.
- Yield stress.
- Surface tension.
- Board film runoff.
- Capillary diffusion scale.
- Contact-line resistance.
- Thin-film cohesion.
- Moving-board inertial response.

## سلوك اللوحة المتحركة

`PaintSurface` يقيس حركة مركز اللوحة بين الفريمات ويضيف pseudo-force للفيلم:

`effective film acceleration = gravity - boardAcceleration * materialInertiaResponse`

لذلك:

- الماء يتحرك بوضوح عند تحريك اللوحة أو إمالتها.
- Latex يتحرك لكن أقل بكثير.
- الطلاء السميك و HeavyBody يقاومان الحركة.

## سلوك الحافة

حواف اللوحة أصبحت open boundary: إذا وصل الفيلم إلى طرف اللوحة واتجاه الجريان خارج اللوحة، يتم تصريف السماكة خارج atlas بدل أن تتكدس كأن هناك جدارًا غير مرئي.

المرحلة الحالية لا تولّد droplets جديدة خارج اللوحة من edge-drips؛ هي تصرف الفيلم خارج سطح الرسم. إذا أردنا لاحقًا رذاذًا/قطرات عند الحواف، نحتاج مرحلة منفصلة تربط edge runoff بجسيمات airborne جديدة.

## التحقق

تم تشغيل:

- `PaintMaterialSurfaceCouplingValidation.Run`
  - waterShift = 76.61 cells
  - latexShift = 15.37 cells
  - edgeLoss = 99.92%
- `PaintSurfaceMotionMaterialValidation.Run`
  - PASS
- `PaintSurfacePresetCalibration.Run`
  - PASS
- `PaintBucketMultiColorValidation.Run`
  - PASS

الصور الناتجة:

- `Logs/G30D_MaterialSurfaceCoupling/G30D_Water_InertialFlow.png`
- `Logs/G30D_MaterialSurfaceCoupling/G30D_Latex_InertialFlow.png`
- `Logs/G30D_MaterialSurfaceCoupling/G30D_Water_OpenEdgeRunoff.png`
