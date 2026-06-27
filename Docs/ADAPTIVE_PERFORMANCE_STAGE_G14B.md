# Adaptive Performance Stage G14-B

## هدف المرحلة

رفع أداء حلّ MLS/APIC-MPM الحالي بدون التضحية باستقرار الدلو المختوم أو منطق الثقوب. نتائج G14-A أظهرت أن قصّ `KClearGrid/KGridUpdate` عبر Active MPM Grid لم يعطِ ربحاً ثابتاً على D3D11، لذلك أصبح خياراً تجريبياً غير مفعّل افتراضياً.

## ما تغيّر

- `enableActiveMpmGridBounds` أصبح غير مفعّل افتراضياً في `GpuMpmSolverConfig_dev.asset`.
- `KBucketCollision` لا يفحص مناطق الثقوب لكل الجزيئات عندما تكون الثقوب مغلقة.
- `KStepAirborneParticles` لا يُشغّل عندما لا توجد ثقوب مفتوحة ولا توجد جسيمات air-domain متوقعة.
- `enablePostG2PBucketCollision` بقي موجوداً، لكن مع `enableAdaptivePostG2PBucketCollision`:
  - في الدلو المختوم: يتم تخطي post-collision pass.
  - عند outflow/open holes أو top spilling: يعمل post-collision pass تلقائياً.
- تقارير validation أصبحت تعرض:
  - `dispatches`
  - `activeMpmGrid`
  - `airborneDispatch`
  - `postCollision`

## لماذا لم نعتمد Active MPM Grid الآن؟

الاختبار أظهر أن عدد `GridNodes=512000` هو حجم الشبكة الكثيفة الكلي، وليس عدد العقد التي تعمل عليها projection فقط. G14-A خفّض عقد MPM النشطة إلى حوالي `172500`، لكن الكلفة الفعلية بقيت في:

- P2G/G2P particle kernels.
- collision kernels.
- projection/pressure kernels.
- diagnostics atomics أثناء التحقق.

لذلك أبقينا Active MPM Grid كخيار تجريبي، لكن أطفأناه افتراضياً إلى حين الانتقال إلى sparse/tiled grid فعلي.

## نتائج التحقق

كل الاختبارات التالية تمت على نسخة validation:

- `g14b-adaptive-default-sealed-120.log`
  - PASS
  - particles: `104436`
  - dispatches: `27`
  - activeMpmGrid: `False`
  - airborneDispatch: `False`
  - postCollision: `False`
  - noGridSupport: `0`
  - nanInf: `0`
  - lost: `0`
  - ms/substep: `36.686`

- `g14b-adaptive-default-outflow-120.log`
  - PASS
  - particles: `104436`
  - dispatches: `29`
  - activeMpmGrid: `False`
  - airborneDispatch: `True`
  - postCollision: `True`
  - noGridSupport: `0`
  - nanInf: `0`
  - lost: `0`
  - outflow observed: `433`
  - jet observed: `10737`
  - airborne observed: `26671`
  - ms/substep: `37.201`

## Flags مفيدة للاختبار

- تفعيل Active MPM Grid تجريبياً:
  - `-paintValidationEnableActiveMpmGridBounds`
- تعطيل Active MPM Grid صراحة:
  - `-paintValidationDisableActiveMpmGridBounds`
- تعطيل post collision تماماً:
  - `-paintValidationDisablePostBucketCollision`
- إجبار post collision دائماً عند عدم تعطيله:
  - `-paintValidationDisableAdaptivePostBucketCollision`
- تعطيل smart airborne dispatch:
  - `-paintValidationDisableSmartAirborneDispatch`

## المرحلة التالية المقترحة

التحسين الكبير القادم لن يأتي من قصّ grid كثيف فقط؛ يلزم الانتقال إلى مسار sparse/tiled فعلي أو تقليل كلفة particle kernels:

1. GPU particle bucketing / tiles.
2. معالجة P2G/G2P على tiles نشطة.
3. تقليل atomics أو تجميعها محلياً داخل tile.
4. فصل diagnostics الثقيلة عن وضع التشغيل الحقيقي.
