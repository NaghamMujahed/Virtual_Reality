# G30C — Moving/Tilted Paint Surface and Material Response Audit

## الهدف

إغلاق فجوة احترافية في نظام الرسم قبل الانتقال إلى G31:

- التأكد أن اللوحة لا تعمل فقط عندما تكون ثابتة، بل أيضاً عندما تتحرك أو تميل بين فريمين.
- التأكد أن الرذاذ/الارتداد ليس بصرياً فقط، بل يترك جزءاً من كتلة الجسيمات حياً على الأسطح قليلة الالتصاق عند الصدمات عالية الطاقة.
- التأكد أن مادة السطح تؤثر فعلاً في الالتقاط، الانتشار، الرذاذ، والجفاف.

## التعديل التقني

أضيفت ذاكرة للإطار السابق للوحة داخل `PaintFilmGrid`:

- `PreviousSurfaceOriginWS`
- `PreviousSurfaceAxisU`
- `PreviousSurfaceAxisV`
- `PreviousSurfaceNormalWS`
- `PreviousCellSizeU/V`

ويتم إرسالها إلى `SurfaceImpact.compute`.

أصبح التصادم يستخدم:

- إطار اللوحة الحالي.
- إطار اللوحة السابق.
- سرعة سطح تقريبية مشتقة من فرق الإطارين.
- سرعة نسبية بين الجسيم واللوحة بدلاً من سرعة الجسيم وحدها.

هذا يجعل اللوحة المتحركة/المائلة قادرة على التقاط الجسيمات التي تقطعها اللوحة بين فريمين حتى لو لم تكن الجسيمات قريبة من سطحها الحالي فقط.

## تحسين رد الفعل

كان المسار السابق يميل إلى ترسيب أي swept impact بالكامل، وهذا ممتاز لمنع الاختراق لكنه يضعف الارتداد على الزجاج/المعدن. أصبح السلوك الآن تكيفياً:

- الخشب/القماش/المواد الممتصة: تميل إلى التقاط الطلاء واستقراره.
- الزجاج/المعدن والصدمات عالية الطاقة مع لزوجة منخفضة: تسمح ببقاء كتلة جزئية حية كـ airborne residual / splash.

## التحقق

أضيف اختبار Editor:

`Assets/Editor/PaintSurfaceMotionMaterialValidation.cs`

يتحقق من:

1. لوحة مائلة ومتحركة بين فريمين تلتقط 16,384 جسيم.
2. الطلاء يجري بعد ذلك باتجاه الجاذبية المسقطة على اللوحة.
3. صدمة عالية الطاقة على الزجاج تترك جزءاً من الطلاء ككتلة حية/رذاذ.
4. نفس الصدمة على القماش تُلتقط بدرجة أعلى بكثير وتحتفظ بكتلة حية أقل.

الصور تحفظ في:

`Logs/G30C_SurfaceMotionMaterial`

## نتائج آخر تحقق

`Logs/G30C_surface_motion_material.log`

- Moving/tilted board:
  - `movingImpacted = 100.00%`
  - `movingDeposited = 100.00%`
  - `movingVolumeError = 0.05%`
  - `movingDownhill = 6.53 cells`
- Glass high-energy splash:
  - `glassDeposit = 19.93%`
  - `glassRetained = 80.00%`
  - `glassAirborne = 100.00%`
  - `glassCells = 32805`
- Fabric high-energy capture:
  - `fabricDeposit = 99.93%`
  - `fabricRetained = 0.00%`
  - `fabricCells = 7706`

هذا يؤكد أن الرذاذ/الارتداد يتغيران حسب مادة السطح: الزجاج يعطي انتشاراً ورذاذاً أوسع مع كتلة باقية، بينما القماش يمتص/يلتقط معظم الطلاء.

## Regression

بعد G30C تم تشغيل:

- `PaintSurfaceTiltedPlaneCalibration`
- `PaintSurfacePresetCalibration`
- `PaintPigmentMixingValidation`
- `PaintBucketMultiColorValidation`

وجميعها نجحت. أهم النتائج:

- Tilted plane:
  - `impact = 32768/32768`
  - `depositError = 0.02%`
  - `downhillShift = 109.23 cells`
- Surface presets:
  - الزجاج يجري أكثر بكثير من القماش.
  - القماش يجف/يمتص أسرع.
- Pigment mixing:
  - `rgbError = 0.01%`
  - `pigmentError = 0.01%`
  - `activeColorDifference = 0.302`
- Multi-color bucket:
  - `impact = 65536/65536`
  - `depositError = 0.02%`
  - الأحمر والأزرق بقيا منفصلين بصرياً.
