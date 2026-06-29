# Paint Surface / MLS-MPM Integration

هذا الملف يوثق مسار دمج الرسم على اللوحة مع solver المشروع الأساسي `GPU MLS-MPM`.

القرار الحالي: لا يوجد DFSPH يعمل بالتوازي. اللوحة تقرأ الجزيئات مباشرة من `GpuFluidBufferSet` الخاص بالـMLS-MPM.

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
SurfaceImpact.compute
        ↓
PaintFilmGrid
        ↓
PaintEvolver + PaintEvaporation.compute
        ↓
PaintSurfaceRenderer + PaintFilmBaker.compute
        ↓
Material _BaseMap / _MainTex
```

## الإصلاح الاحترافي الأخير

تمت معالجة مشكلة أن الرسم بعد الدمج فقد جزءاً من splash/spread/wetness/drying:

- `PaintSimulationHost` لم يعد يعتمد عملياً على diagnostics فقط لكي يسمح بالترسيب؛ يمكنه قبول الجزيئات التي وصلت إلى سطح اللوحة حتى لو لم تتحول حالتها إلى Airborne بعد.
- `SurfaceImpact.compute` يستخدم الآن `SpreadSpeed` فعلياً، ويضيف splatter ثانوي حول ضربة الجزيء عند الطاقة العالية.
- `PaintFilmGrid` أصبح يملك `ScratchCellBuffer` حتى يكون انتشار الطلاء ping-pong بدلاً من القراءة والكتابة في نفس buffer.
- `PaintEvaporation.compute` أعيد بناؤه إلى مرحلتين:
  - `Spread`: انتشار رطب مع ميل بسيط باتجاه flow velocity.
  - `Evaporate`: تقليل الرطوبة وزيادة العمر مع إبقاء الطلاء الجاف مرئياً.
- `PaintSurfaceRenderer` يربط texture على `_BaseMap` و`_MainTex`، ويصنع material runtime عند غياب material على اللوحة.
- `SampleScene` رُبطت فيها لوحة `PaintPlane` بمادة `PaintSurfaceMaterial`.

## الملفات الأساسية

- `Assets/PaintSim/Scripts/UnityBridge/PaintSimulationHost.cs`
- `Assets/PaintSim/Scripts/Stages/Surface/PaintSurface.cs`
- `Assets/PaintSim/Scripts/Stages/Surface/PaintFilmGrid.cs`
- `Assets/PaintSim/Scripts/Stages/Surface/PaintDepositor.cs`
- `Assets/PaintSim/Scripts/Stages/Surface/PaintEvolver.cs`
- `Assets/PaintSim/Scripts/Rendering/PaintSurfaceRenderer.cs`
- `Assets/Resources/ComputeShaders/Impact/SurfaceImpact.compute`
- `Assets/Resources/ComputeShaders/Surface/PaintEvaporation.compute`
- `Assets/Resources/ComputeShaders/Surface/PaintFilmBaker.compute`

## ملاحظات ضبط

- `PaintSurface > Diffusion Rate` يستخدم قيمة legacy، ويتم تحويله داخلياً إلى معدل انتشار محافظ أصغر.
- `Runoff Rate` يزيد امتداد الطلاء باتجاه flow velocity.
- `Minimum Wet Thickness` يؤثر على سرعة جفاف الطبقات الخفيفة.
- `SurfaceProperties.SpreadSpeed` أصبح مؤثراً في حجم البقعة والـsplash.

## التحقق

آخر اختبار Unity batch:

`Logs/PaintSurfaceProfessional_G26.log`

النتيجة:

- لا توجد أخطاء C#.
- لا توجد Shader errors.
- التحذيرات المتبقية تخص `DebugOverlay.cs` فقط، وهي تحذيرات obsolete قديمة وغير مرتبطة بنظام الرسم.
