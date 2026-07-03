# G29B — Tilted Surface Capture & Downhill Film Flow

## الهدف

إغلاق مشكلة أن الطلاء لا يستجيب بوضوح لميل `PaintPlane`، ومعالجة مرور نسبة من الجسيمات عبر الأرضية/اللوحة عند السرعات العالية.

## سبب المشكلة

كان هناك سببان عمليان:

1. في وضع `LocalXZ` كانت محاولة جعل frame اللوحة right-handed تقلب `normalWS` إلى الأسفل، لأن `cross(right, forward)` في Unity يشير إلى `-up`. هذا كان ينقل سطح الالتقاط إلى الوجه السفلي للأرضية بدل الوجه المرئي/العلوي.
2. تصادم الجسيمات مع اللوحة كان يعتمد أساسًا على موضع الجسيم الحالي. إذا عبر الجسيم اللوحة بين فريمين، يمكن أن يصبح بعيدًا أسفل السطح قبل أن يراه kernel كـ near-surface.

## التعديلات

- أبقينا normal الوجه المرئي كما يحدده transform:
  - `LocalXZ` يستخدم `transform.up`.
  - لم نعد نقلب normal بسبب `cross(axisU, axisV)`.
- أضفنا تحديثًا ديناميكيًا لـ frame اللوحة:
  - `PaintSurface.RefreshSurfaceFrameFromTransform()`
  - `PaintFilmGrid.ReconfigureSurfaceFrame(...)`
  - `PaintSimulationHost` يستدعي التحديث قبل deposition.
  - `PaintSurface.Render()` يستدعي التحديث قبل evolution/render.
- أضفنا swept impact في `SurfaceImpact.compute`:
  - kernel يستقبل `_SurfaceImpactDeltaTime`.
  - يحسب موضع الجسيم السابق تقريبياً من `position - velocity * dt`.
  - إذا قطع segment مستوى اللوحة، يتم الترسيب عند نقطة التقاطع بدل فقدان الجسيم تحت السطح.
  - الجسيمات التي تعبر السطح تستقر وتترسب بدل أن تكمل أسفل اللوحة.

## التحقق

أضيف اختبار:

`Assets/Editor/PaintSurfaceTiltedPlaneCalibration.cs`

الاختبار يبني سطحًا مائلًا، يطلق `32768` جسيمًا تعبر اللوحة بين العينات، ثم يشغل جريان الفيلم 6 ثوانٍ باتجاه الجاذبية المسقطة على محوري اللوحة.

نتيجة التشغيل:

```text
[PaintSurfaceTiltedPlaneCalibration] PASS
particles=32768
scanned=32768
impacted=32768
settled=32768
cellWrites=39943
depositedVolume=1.0330E-003 m3
depositError=0.02 %
remainingMass=0.00 %
gravityUV=(1.355,4.606)
downhillShift=109.23 cells
crossShift=3.00 cells
flowDrift=0.01 %
cells=39943->75249
```

صور المعايرة:

- `Logs/G29B_TiltedPlaneCalibration/G29B_TiltedImpact_HighCount.png`
- `Logs/G29B_TiltedPlaneCalibration/G29B_TiltedFlow_6s.png`

كما بقي اختبار السطح العام ناجحًا:

```text
[PaintSurfaceGpuValidation] PASS
depositedVolume=9.9998E-008 m3
depositError=0.00 %
evolutionDrift=0.00 %
maxCoverage=0.734
maxWetness=0.994
```

## ملاحظات تشغيل

- إذا تم تدوير `PaintPlane` أثناء Play Mode، سيُعاد تحديث frame اللوحة قبل deposition/render.
- إذا كان الميل حول محور `Y` فقط فهذا ليس slope؛ هو تدوير أفقي للسطح ولن يولد جريانًا بالجاذبية. يجب الميل حول `X` أو `Z`.
- إذا ما زال هناك تسرب واضح بعد هذا الإصلاح، فعادة سيكون السبب أن الجسيمات خارج حدود اللوحة نفسها، لا أنها عبرت مستوى السطح داخل حدود grid.
