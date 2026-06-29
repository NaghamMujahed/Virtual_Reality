# Paint Surface / MLS-MPM Integration

هذا الملف يوثق حالة الدمج النهائية بين نظام الرسم على اللوحة القادم من فرع `zain` وبين solver المشروع الأساسي المعتمد على GPU MLS-MPM.

القرار المعتمد الآن: لا يوجد تشغيل DFSPH داخل المشروع الحالي. نظام اللوحة يستقبل الجزيئات مباشرة من `GpuFluidBufferSet` الخاص بالـMLS-MPM، ثم يحول الاصطدام إلى طبقة طلاء على اللوحة.

## المسار الحالي

```text
PaintFluidSystem / GPU MLS-MPM
        ↓
GpuFluidBufferSet
  PositionRadius / VelocityMass / Color / StateAgeId
        ↓
PaintSimulationHost
        ↓
PaintDepositor.DispatchFromMlsMpmBuffers(...)
        ↓
SurfaceImpact.compute / CSMainMlsMpm
        ↓
PaintFilmGrid
        ↓
PaintSurfaceRenderer + PaintFilmBaker.compute
        ↓
Material _MainTex على اللوحة
```

## الملفات الأساسية

- `Assets/PaintSim/Scripts/UnityBridge/PaintSimulationHost.cs`
  - جسر الدمج الوحيد بين solver المشروع ونظام اللوحة.
  - لا يملك محاكاة مستقلة ولا DFSPH.
  - يتخطى dispatch عندما لا توجد جزيئات في مجال الهواء/النفث لتخفيف الكلفة.

- `Assets/PaintSim/Scripts/Stages/Surface/PaintSurface.cs`
  - ينشئ grid الطلاء من حجم اللوحة الحقيقي.
  - لا يغير Scale اللوحة افتراضياً، لذلك لا يحدث تصغير تلقائي للوحة.
  - يدعم اللوحات الأفقية والعمودية والمائلة عبر `PaintSurfacePlaneMode`.

- `Assets/PaintSim/Scripts/Stages/Surface/PaintFilmGrid.cs`
  - يخزن grid الطلاء ومحاور اللوحة في العالم:
    - `SurfaceOriginWS`
    - `SurfaceAxisU`
    - `SurfaceAxisV`
    - `SurfaceNormalWS`

- `Assets/Resources/ComputeShaders/Impact/SurfaceImpact.compute`
  - يقرأ buffers الخاصة بالـMLS-MPM.
  - يحسب الاصطدام على مستوى اللوحة الموجه، وليس على افتراض XZ/Y فقط.
  - يكتب الترسيب داخل `PaintCellBuffer`.
  - يحدث حالة الجزيء إلى:
    - `Deposited = 8`
    - `Absorbed = 9`
    - أو يبقيه `Airborne = 6` إذا ارتد/تناثر.

## ما تم حذفه من دمج zain

تم حذف الأجزاء التي كانت ستسبب تكراراً أو محاكاة ثانية غير مرغوبة:

- DFSPH stage.
- SPH/DFSPH particle data.
- BufferManager وSimulationPipeline المستوردان.
- Exit/Impact stages الخاصة بالمسار القديم.
- Particle renderer المستورد من zain.
- Shaders ومواد غير مستخدمة في المسار الحالي.

المتبقي من `PaintSim` هو فقط ما يخدم اللوحة/الترسيب/rendering.

## تركيب المشهد

1. ضع `PaintSurface` على جسم اللوحة.
2. ضع `PaintSimulationHost` مرة واحدة في المشهد.
3. اربط يدوياً أو اترك Auto Find مفعلاً:
   - `PaintFluidSystem`
   - `GpuFluidBufferSet`
   - `PaintSurface`
4. تأكد أن Material اللوحة يقبل `_MainTex`.
5. في `PaintSurface`:
   - `Sizing Mode = FitRendererBounds` غالباً هو الخيار الصحيح.
   - `Plane Mode = AutoFromMeshBounds` غالباً يكفي:
     - Unity Plane → XZ.
     - Unity Quad → XY.
     - Mesh مائل/عمودي → يختار المحور الأقل سماكة كـNormal.
   - إذا كانت اللوحة Mesh خاصاً وغريب المحاور، اختر `LocalXY` أو `LocalXZ` أو `LocalYZ` يدوياً.

## تعدد الألوان والحواجز داخل الدلو

الإعدادات موجودة في `PaintFluidConfig`:

- `enableColorCompartments`
- `colorCompartmentCount`
- `colorCompartmentAxis`
- `colorDividerGapFraction`
- `compartmentColors`
- `enablePhysicalColorDividers`
- `colorDividerThicknessMeters`
- `carveInitialParticlesAroundPhysicalDividers`

الحالة الحالية ليست مجرد فراغ تهيئة فقط؛ تمت إضافة حل تصادم GPU للحواجز داخل `GpuDenseMpmPrototype.compute` وربطه من `GpuMpmDenseLocalSolver`.

هذا يعني أن الفواصل اللونية يمكن أن تعمل كحواجز فيزيائية داخل الدلو، مع دعم:

- تقسيم على محور X.
- تقسيم على محور Z.
- تقسيم radial wedges.

## ملاحظات أداء مهمة

- لا يوجد DFSPH يعمل بالتوازي.
- `PaintSimulationHost` يتخطى الترسيب عندما لا توجد جزيئات Jet/Airborne/OutflowTransition.
- `PaintSurface` يسمح بتقليل كلفة اللوحة عبر:
  - `Render Every N Frames`
  - `Evolve Every N Frames`
- الـCPU `PaintParticleRenderer` الموجود في المشروع الأساسي لا يرسم عندما يكون GPU solver فعالاً، والـrenderer المعتمد للمسار الحالي هو `GpuParticleIndirectRenderer`.

## التحقق

تم تشغيل Unity batchmode بعد التنظيف ودعم اللوحة الموجهة:

- لا توجد أخطاء C#.
- لا توجد Shader errors.
- التحذيرات المتبقية هي تحذيرات Unity obsolete حول `FindFirstObjectByType` في سكربتات قديمة، وليست أخطاء تشغيلية في الدمج.

آخر log تحقق:

`Logs/PaintSimIntegration_G23_OrientedSurface.log`
