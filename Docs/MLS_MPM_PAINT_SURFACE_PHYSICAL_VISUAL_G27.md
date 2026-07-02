# G27 — Physical / Visual Paint Surface

هذه المرحلة تعيد معايرة منظومة `zain` بعد ربطها بـGPU MLS-MPM، مع الحفاظ على
الكلاسات الرئيسية مثل `PaintSurface`, `PaintDepositor`, `PaintEvolver` و
`PaintSurfaceRenderer`.

## ما تغير

- حجم الطلاء المترسب يأتي من `VolumeJBuffer`:
  `currentVolume = restVolume * J * remainingMassFraction`.
- fallback آمن عند غياب Volume/J: `mass / density`.
- بصمة الضربة والـsatellite splashes تقسم حجم الجسيم ولا تنسخه، ولذلك أصبحت
  الكتلة محفوظة.
- الجسيم المستقر يودع كامل كتلته، والجسيم المرتد يحتفظ فقط بالجزء غير المترسب.
- مزج RGB واتجاه الجريان يتم عبر atomic integer accumulators، وهو آمن عند
  اصطدام ألوان متعددة بالخلية نفسها.
- الخلايا التي لمستها الضربة تجمع في قائمة sparse ويجري حلها بواسطة
  `DispatchIndirect` بدل مسح شبكة اللوحة كلها بعد كل impact.
- الانتشار أصبح ping-pong conservative flux:
  - capillary spreading مرتبط بـsurface tension.
  - advection محافظ تقريبًا.
  - gravity runoff مسقط على محوري اللوحة.
  - viscosity وyield stress يقللان mobility.
- دقة السماكة أصبحت 10 nm بدل 1 µm لتجنب فقدان الفيلم الرقيق بسبب التقريب.
- الـBaker يخرج خريطتين:
  - albedo النهائي.
  - coverage / wetness / normalized thickness / age.
- مادة `PaintSim/Wet Paint Surface URP` تستخدم:
  - wet/dry smoothness.
  - thickness-derived normals.
  - specular وFresnel للطلاء الرطب.
- دقة لوحة `SampleScene` أصبحت `768 x 768`، أي قرابة `10.4 mm` لكل خلية
  على لوحة بعرض 8 m، وهي قريبة من دقة مرجع zain.

## إعدادات SampleScene

- Grid: `768 x 768`
- Opacity depth (`Max Thickness`): `0.00008 m`
- Evaporation: `0.02 / s`
- Legacy Diffusion: `30`، وتتحول داخليًا إلى `7.5 / s`
- Evolve every N frames: `2`
- Runtime impact diagnostics: مغلقة افتراضيًا لتجنب GPU readback stall.

## التحقق الآلي

أداة التحقق:

`Assets/Editor/PaintSurfaceGpuValidation.cs`

تشغل جسيمين بلونين مختلفين على GPU، ثم تتحقق من:

- حفظ حجم الترسيب.
- انتقال الجسيمات إلى `Deposited`.
- تفريغ كتلة الجسيم المستقر.
- atomic color mixing.
- حفظ سماكة الفيلم خلال 12 خطوة انتشار.
- خرائط coverage وwetness.
- دعم شيدر URP.
- diagnostics kernel المنفصل المتوافق مع حد D3D11 ذي 8 UAVs.

آخر نتيجة:

```text
PASS
depositedVolume = 9.9979E-008 m3
depositError = 0.02 %
evolutionDrift = 0.23 %
maxCoverage = 0.789
maxWetness = 1.000
mixedColor = (0.53, 0.05, 0.53, 1.00)
```

اللوج:

`Logs/PaintSurfacePhysicalVisual_G27_gpuvalidation5.log`

فحص Unity النهائي:

`Logs/PaintSurfacePhysicalVisual_G27_final2.log` — return code 0، بلا أخطاء C# أو Shader.
