# G28 — Professional Wet Paint Appearance & Flow

هذه المرحلة تكمل G27 من جهة الإخراج البصري على اللوحة، لا من جهة إصلاح التصادم الأساسي. الهدف كان جعل الرسم يبدو أكثر إقناعًا: لطخة لها جسم وحافة، فرق بين الطلاء الرطب والجاف، وجريان نازل فيه أصابع/مسارات بدل انتشار ناعم ومسطح.

## ما أُضيف

- شيدر `PaintSim/Wet Paint Surface URP` أصبح يدعم:
  - grain خفيف للسطح حتى لا تبدو اللوحة مسطحة تمامًا.
  - تشبع صبغة قابل للضبط.
  - تغميق بسيط للطلاء الرطب.
  - highlight عند contact rim.
  - normal/parallax من خريطة السماكة الموجودة أصلًا.
- `PaintEvaporation.compute` أصبح يحسب:
  - `DripFingerInstability` لصنع تفرعات ومسارات نزول غير مثالية.
  - `ThinFilmCohesion` لتقليل انتشار الفيلم الرقيق جدًا كضباب.
  - جريان متأثر بخشونة السطح، الجاذبية، wetness، اللزوجة، yield stress، وcontact line.
- `PaintSurface` أصبح يعرّض هذه القيم في Inspector:
  - `_dripFingerInstability = 0.36`
  - `_thinFilmCohesion = 0.58`
- إصلاح تحذير معايرة Editor:
  - `PaintSurfaceRenderer` لم يعد يستخدم `renderer.material` أثناء اختبارات Editor، لذلك لا يخلق material leaks في المعايرة البصرية.
- `PaintSurfaceVisualCalibration` أصبح اختبارًا دائمًا للجانب البصري:
  - يولّد صور wet/dry/flow.
  - يتحقق من فرق الرطب/الجاف.
  - يتحقق من حركة الجريان للأسفل.
  - يتحقق من حفظ الحجم أثناء الجريان.

## القيم الافتراضية الحالية

في `SampleScene.unity`:

- `maxThickness = 0.00003`
- `diffusionRate = 30`
- `runoffRate = 0.18`
- `minimumWetThickness = 0.000015`
- `contactLineThickness = 0.000025`
- `contactAngleResistance = 0.72`
- `substrateFlowVariation = 0.28`
- `dripFingerInstability = 0.36`
- `thinFilmCohesion = 0.58`

في `PaintSurfaceMaterial.mat`:

- `CanvasGrainStrength = 0.045`
- `CanvasGrainScale = 180`
- `PigmentSaturation = 1.10`
- `WetDarkening = 0.055`
- `EdgeHighlightStrength = 0.28`
- `WetPaintSmoothness = 0.94`
- `WetSpecularStrength = 0.85`
- `ClearCoatStrength = 0.75`

## نتائج التحقق

### GPU physical validation

Log:

`Logs/G28_gpuvalidation_final2.log`

النتيجة:

```text
PASS
depositedVolume = 9.9997E-008 m3
depositError = 0.00 %
evolutionDrift = 0.00 %
maxCoverage = 0.653
maxWetness = 1.000
mixedColor = (0.54, 0.05, 0.51, 1.00)
```

### Visual calibration

Log:

`Logs/G28_visualcalibration_final.log`

النتيجة:

```text
PASS
wetCells = 4160
flowCells = 20839
downwardShift = 56.99 cells
volumeDrift = 0.02 %
wetDryDifference = 0.0018
```

Generated images:

- `Logs/G28_VisualCalibration/G28_WetImpact.png`
- `Logs/G28_VisualCalibration/G28_DryImpact.png`
- `Logs/G28_VisualCalibration/G28_VerticalFlow_6s.png`

### Final Unity import/compile

Log:

`Logs/G28_final_compile.log`

النتيجة:

```text
Exit code 0
No C# errors
No shader errors
```

ملاحظة: تشغيل `G28_gpuvalidation_final.log` الأول كان باستخدام `-nographics`، وهذا جعل Unity يستخدم `NullGfxDevice` فلم تُحمّل compute kernels. لذلك المرجع الصحيح هو `G28_gpuvalidation_final2.log` بدون `-nographics`.

## ملاحظات ضبط سريعة

- إذا بدا الطلاء منتشرًا جدًا:
  - ارفع `Thin Film Cohesion` قليلًا.
  - ارفع `Contact Angle Resistance`.
- إذا بدت الخطوط ناعمة جدًا:
  - ارفع `Drip Finger Instability`.
  - ارفع `Substrate Flow Variation`.
- إذا أصبح الرسم باهتًا:
  - خفّض `Max Thickness`.
  - ارفع `Pigment Saturation`.
- إذا أصبح مشبعًا زيادة:
  - ارفع `Max Thickness`.
  - خفّض `Wet Darkening` أو `Pigment Saturation`.

