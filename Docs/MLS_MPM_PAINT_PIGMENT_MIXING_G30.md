# G30 — GPU Pigment Mixing / Kubelka–Munk Approximation

## الهدف

رفع واقعية مزج ألوان الطلاء. قبل هذه المرحلة كان اللون يمزج كـ RGB عادي، وهذا ينتج مزج شاشة:

- الأصفر + الأزرق يميل إلى رمادي/باهت بدل أخضر طلاء.
- الأحمر + الأزرق يبقى بنفسجيًا مشبعًا أكثر من اللازم.
- الأسود/الألوان القاتمة لا تطين الخليط بصريًا كما يفعل الطلاء الحقيقي.

## التنفيذ

أُضيف وضع مزج جديد:

```csharp
PaintColorMixingMode.KubelkaMunkApprox
```

وهو نموذج Kubelka–Munk تقريبي مناسب للـ GPU. لا يخزن K و S منفصلين لكل خلية، بل يستخدم `K/S` كتمثيل بصري خفيف:

```text
K/S = (1 - R)^2 / (2R)
R   = 1 + K/S - sqrt((K/S)^2 + 2K/S)
```

هذا يحافظ على الأداء ولا يضيف buffers جديدة، لأننا أعدنا استخدام:

```text
_DepositColorAccumulator
```

عند وضع RGB يبقى accumulator كما كان. عند وضع Kubelka–Munk تُخزن قيم `K/S` الموزونة بالسماكة ثم تُحوّل للون النهائي داخل `PaintCellData.Color`.

## أين يطبق المزج؟

- أثناء deposition من جسيمات MLS-MPM في `SurfaceImpact.compute`.
- أثناء جريان/انتشار الفيلم في `PaintEvaporation.compute`.
- في `PaintSimulationHost` و `PaintSurface` كإعدادات قابلة للضبط:
  - `_colorMixingMode`
  - `_pigmentMixStrength`
  - `_pigmentMinReflectance`
  - `_pigmentMaxKs`

المشهد الحالي `SampleScene.unity` مضبوط صراحةً على:

```text
_colorMixingMode: KubelkaMunkApprox
_pigmentMixStrength: 1
_pigmentMinReflectance: 0.035
_pigmentMaxKs: 18
```

## ملاحظات مهمة

هذا ليس نموذجًا طيفيًا كاملًا ولا يخزن scattering منفصلًا لكل pigment، لذلك هو “Kubelka–Munk inspired / approximate”. ميزته أنه يعطي قفزة بصرية واضحة بدون زيادة كبيرة في memory أو kernels.

إذا ظهر المزج داكنًا جدًا في لقطة فنية معينة، أول قيمة تُخفّض هي:

```text
Pigment Mix Strength: 0.75 – 0.90
```

أما إذا كانت الألوان السوداء/الغامقة تسيطر أكثر من اللازم:

```text
Pigment Max Ks: 10 – 16
```

## التحقق العالي

أُضيف اختبار:

```text
Assets/Editor/PaintPigmentMixingValidation.cs
```

يبني 4 مناطق مزج:

- Red + Blue
- Yellow + Blue
- Red + White
- Yellow + Black

ويشغل نفس الترسيب مرتين:

```text
32768 particles RGB
32768 particles Kubelka–Munk
65536 total high-count depositions
```

نتيجة الاختبار:

```text
[PaintPigmentMixingValidation] PASS
particlesPerMode=32768
totalDepositions=65536
rgbError=0.01 %
pigmentError=0.01 %
redBlueRgb=(0.493,0.067,0.490)
redBluePigment=(0.067,0.052,0.067)
yellowBlueRgb=(0.517,0.459,0.493)
yellowBluePigment=(0.067,0.170,0.067)
yellowBlackRgb=(0.512,0.421,0.027)
yellowBlackPigment=(0.067,0.066,0.035)
activeColorDifference=0.302
flowDrift=0.00 %
flowGrowth=6766
```

## صور المعايرة

صور الكاميرا:

- `Logs/G30_PigmentMixing/G30_RGB_HighCount.png`
- `Logs/G30_PigmentMixing/G30_KubelkaMunk_HighCount.png`
- `Logs/G30_PigmentMixing/G30_KubelkaMunk_Flow_4s.png`

صور atlas المباشرة والأوضح:

- `Logs/G30_PigmentMixing/G30_RGB_HighCount_Atlas.png`
- `Logs/G30_PigmentMixing/G30_KubelkaMunk_HighCount_Atlas.png`
- `Logs/G30_PigmentMixing/G30_KubelkaMunk_Flow_4s_Atlas.png`

## اختبارات الرجوع

بقيت الاختبارات السابقة ناجحة:

```text
PaintSurfaceGpuValidation PASS
depositError=0.00 %
evolutionDrift=0.00 %

PaintSurfaceTiltedPlaneCalibration PASS
impacted=32768/32768
remainingMass=0.00 %
downhillShift=109.23 cells

PaintSurfacePresetCalibration PASS
Wood / Glass / Fabric / Metal all preserved
```
