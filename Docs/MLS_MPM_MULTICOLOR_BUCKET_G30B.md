# G30B — Multi-Color Bucket Compartments & Two-Hole Paint Validation

## الهدف

دمج وتفعيل عمل تعدد الألوان داخل الدلو مع فاصل فيزيائي بين اللونين، ثم ضبط الثقوب بحيث يوجد ثقب دائري نشط تحت كل قسم لوني، وثقب دائري ثالث عند طرف قاع الدلو لكنه غير نشط حالياً.

## ما تم تفعيله

- تم تفعيل `enableColorCompartments` في `PaintFluidConfig_dev`.
- عدد الأقسام: 2.
- محور الفصل: `BucketLocalX`، أي أحمر يسار / أزرق يمين.
- تم تفعيل `enablePhysicalColorDividers` بحيث لا يكون الفصل بصرياً فقط، بل يدخل في اصطدام GPU داخل `GpuDenseMpmPrototype.compute`.
- سماكة الفاصل: `0.014m`، مع carving للجسيمات الابتدائية حوله حتى لا يبدأ السائل متداخلًا مع الجدار الداخلي.

## الثقوب الحالية

- `Red Compartment Round Hole`
  - نشط.
  - دائري.
  - تحت القسم الأحمر.
  - نصف القطر: `0.003m`.
- `Blue Compartment Round Hole`
  - نشط.
  - دائري.
  - تحت القسم الأزرق.
  - نصف القطر: `0.003m`.
- `Inactive Bottom Edge Round Hole`
  - غير نشط حالياً.
  - دائري.
  - عند طرف قاع الدلو لاختباره لاحقاً.
  - نصف القطر: `0.003m`.

تم استخدام نصف قطر صغير مع `flowMultiplier = 0.62` حتى لا يفرغ الدلو بسرعة كبيرة، مع بقاء خروج السائل مقروءاً بصرياً.

## تحسين بصري داخل الدلو

تم تحديث `BucketRenderer` ليعرض فاصلًا داخلياً مرئياً بين الأقسام اللونية. هذا الفاصل لا يضيف فيزياء جديدة؛ الفيزياء موجودة في GPU solver، لكنه يجعل المشهد مفهوماً أثناء التجريب ويمنع الإحساس بأن اللونين “مختلطان بلا حاجز”.

كما تم جعل حلقات الثقوب النشطة أوضح من الثقب غير النشط:

- النشطة: خط أكثر سماكة/وضوحاً.
- غير النشط: خط أخف حتى نعرف أنه موجود للتجربة لاحقاً فقط.

## التحقق

أضيف اختبار Editor مستقل:

`Assets/Editor/PaintBucketMultiColorValidation.cs`

يشغّل:

- فحص إعدادات الدلو والألوان والفاصل.
- معاينة علوية للدلو.
- ترسيب GPU عالي العدد من ثقبين/لونين باستخدام 65,536 جسيم.
- تطور/جريان على اللوحة لمدة 3 ثواني.
- حفظ صور تحقق داخل:

`Logs/G30B_MultiColorBucket`

نتيجة آخر تشغيل:

- `particles = 65536`
- `impacted = 65536`
- `settled = 65536`
- `cellWrites = 17116`
- `depositError = 0.02%`
- `flowDrift = 0.00%`
- `impactCells = 17116`
- `flowCells = 20623`

الملف:

`Logs/G30B_multicolor_validation.log`

## الصور الناتجة

- `G30B_BucketCompartments_TopView.png`
- `G30B_RedBlueTwoHoleJets_Impact.png`
- `G30B_RedBlueTwoHoleJets_Impact_Atlas.png`
- `G30B_RedBlueTwoHoleJets_Flow_3s.png`
- `G30B_RedBlueTwoHoleJets_Flow_3s_Atlas.png`

