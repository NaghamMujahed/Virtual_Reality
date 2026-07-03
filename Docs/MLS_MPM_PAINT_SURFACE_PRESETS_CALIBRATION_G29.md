# G29 — Surface Interaction Presets & Visual Calibration

هذه المرحلة تجعل `SurfaceType` مؤثرًا فعليًا على الرسم، لا مجرد اسم في الـInspector. الهدف هو أن يختلف سلوك الطلاء ومظهره على الخشب، الزجاج، القماش، والمعدن بصورة قابلة للقياس والمعايرة.

## ما أُضيف

- Presets فيزيائية للجريان لكل سطح عبر `SurfaceFilmInteraction`:
  - contact line thickness
  - contact angle resistance
  - substrate flow variation
  - drip finger instability
  - thin film cohesion
  - evaporation/runoff/diffusion multipliers
- Presets بصرية لكل سطح عبر `SurfaceVisualProperties`:
  - canvas/base color
  - canvas/dry/wet smoothness
  - clear-coat/specular/environment/fresnel
  - grain/micro normal
  - pigment saturation/wet darkening
  - visual max thickness/wetness shine
- `PaintSurface` أصبح يطبق preset السطح تلقائيًا:
  - `_useSurfacePreset = true`
  - `_surfacePresetStrength = 1`
- `PaintEvolver` أصبح يستقبل `SurfaceAbsorptionRate`.
- `PaintEvaporation.compute` أصبح يجعل الأسطح المسامية:
  - تجف أسرع.
  - توقف الجريان أسرع.
  - تقلل حركة الفيلم الرقيق.
- `SurfaceImpact.compute` أصبح يستخدم الامتصاص أثناء التصادم:
  - السطوح المسامية تلتقط الطلاء أكثر.
  - تقلل secondary splash.
  - تقلل نصف قطر الانتشار الأولي.
- `PaintSurfaceRenderer` أصبح يستطيع تطبيق `SurfaceVisualProperties` مباشرة على مادة URP runtime.

## السلوك المتوقع لكل سطح

- Wood:
  - امتصاص متوسط.
  - جريان متوسط مع حواف عضوية.
  - grain واضح.
- Glass:
  - امتصاص شبه معدوم.
  - انزلاق طويل ولمعان قوي.
  - سطح أنظف وأقل roughness.
- Fabric:
  - امتصاص قوي.
  - جريان قصير جدًا.
  - مظهر مطفي وخشن.
- Metal:
  - امتصاص ضعيف.
  - جريان جيد ولمعان قوي، لكن أقل من الزجاج.

## نتائج معايرة G29

Log:

`Logs/G29_surfacepreset_calibration_final.log`

النتيجة:

```text
PASS
Wood:   wetDry=0.0020, shift=18.57,  drift=0.00 %, cells=2518->5071,  wet=0.753
Glass:  wetDry=0.0021, shift=100.49, drift=0.05 %, cells=2518->11076, wet=0.957
Fabric: wetDry=0.0013, shift=5.11,   drift=0.00 %, cells=2518->3500,  wet=0.639
Metal:  wetDry=0.0018, shift=67.32,  drift=0.03 %, cells=2518->8785,  wet=0.919
```

الاستنتاج:

- الزجاج يجري أبعد من القماش بوضوح.
- القماش يجف/يمتص أسرع من الزجاج.
- حفظ الحجم بقي مستقرًا جدًا.
- كل سطح يملك فرق wet/dry قابل للقياس.

## صور المعايرة

المجلد:

`Logs/G29_SurfacePresetCalibration`

الصور:

- `G29_Wood_Wet.png`
- `G29_Wood_Dry.png`
- `G29_Wood_Flow_6s.png`
- `G29_Glass_Wet.png`
- `G29_Glass_Dry.png`
- `G29_Glass_Flow_6s.png`
- `G29_Fabric_Wet.png`
- `G29_Fabric_Dry.png`
- `G29_Fabric_Flow_6s.png`
- `G29_Metal_Wet.png`
- `G29_Metal_Dry.png`
- `G29_Metal_Flow_6s.png`

## تحقق عدم كسر النظام السابق

### GPU validation

Log:

`Logs/G29_gpuvalidation_final.log`

```text
PASS
depositedVolume = 9.9998E-008 m3
depositError = 0.00 %
evolutionDrift = 0.00 %
maxCoverage = 0.734
maxWetness = 0.994
mixedColor = (0.54, 0.05, 0.51, 1.00)
```

### Unity final compile/import

Log:

`Logs/G29_final_compile2.log`

```text
Exit code 0
No C# errors
No shader errors
```

## ضبط سريع داخل Unity

- إن أردت إلغاء presets والعودة للضبط اليدوي:
  - أطفئ `_useSurfacePreset` على `PaintSurface`.
- إن أردت تخفيف تأثير السطح:
  - خفّض `_surfacePresetStrength`.
- إن كان الزجاج يجري أكثر من اللازم:
  - خفّض `RunoffMultiplier` في `SurfaceFilmInteraction.Glass`.
- إن كان القماش يمسك الطلاء بقوة مبالغ فيها:
  - خفّض `AbsorptionRate` في `SurfaceProperties.Fabric`
  - أو خفّض `ContactAngleResistance` في `SurfaceFilmInteraction.Fabric`.
