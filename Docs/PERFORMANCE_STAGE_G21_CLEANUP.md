# G21 Performance Cleanup

## هدف المرحلة

كان هدف G21 اختبار مسارات أقوى من G20/G20B لتحسين أداء GPU MLS/APIC-MPM عند أعداد جسيمات مرتفعة، خصوصًا بعد ملاحظة أن الأداء ما زال غير كافٍ للـ realtime المريح.

## القرار النهائي

لم نعتمد مساري G21 التجريبيين التاليين لأن القياس العملي أظهر أنهما لا يحققان هدف الأداء العالي:

- `cell-binned gather P2G`: أبطأ بكثير من hybrid tiled P2G بسبب إعادة قراءة الجسيمات لكل grid node.
- `persistent owner bins`: أعطى مكسبًا صغيرًا عند حمل متوسط، لكنه أصبح أبطأ عند الحمل الأعلى، لذلك لا يصلح كمسار افتراضي للمشروع.

تم تنظيف الكود منهما بدل إبقائهما كتعقيد خامد.

## المسار المعتمد بعد التنظيف

المسار المعتمد حاليًا هو:

- hybrid tiled P2G
- centered owner tile
- owner-list reuse من G20B
- fused G2P/post-collision
- fused pre-collision/tile-mark عند عدم طلب support particle refs
- tiled projection + sparse pressure
- Reference Density EOS + Grid Density EOS

## نتائج تحقق بعد التنظيف

آخر تحقق كبير:

- الملف: `g21_cleanup_439target_60.log`
- الجسيمات الفعلية: `382707`
- `gpuTotalMs=24.808`
- `gpuP2GMs=11.738`
- `gpuProjectionMs=2.724`
- `gpuPostMs=7.019`
- `ownerListInterval=2`
- `ownerListReused=True`
- `fusedPreCollisionMark=True`
- `mpmTileParticleRefs=0`
- `nanInf=0`
- `lost=0`

المقارنة مع baseline سابق قريب:

- `g21_surface_on_439target_60.log`: `gpuTotalMs=25.374`
- بعد التنظيف: `gpuTotalMs=24.808`

التحسن هنا صغير، لكنه نظيف ومستقر، والأهم أنه أزال مسارات كانت تزيد التعقيد أو تضر الأداء عند الحمل العالي.

## ملاحظات مهمة

- `activeMpmGridNodes=512000` ما زال يظهر لأن dense backing grid موجود، لكن العمل الفعلي في عدة مراحل أصبح tile-driven. هذا ليس sparse-grid كاملًا بعد.
- تحسين جذري حقيقي للأداء لن يأتي من إضافة branch جديد فوق هذا التصميم؛ المرحلة التالية يجب أن تستهدف architecture أعمق: ضغط/فرز/dispatch particle lists بكفاءة أعلى أو sparse/block grid حقيقي.
- تم إبقاء إصلاح احترام `enablePostG2PBucketCollision`: عند تعطيله لا يجب أن يظل post collision مفروضًا فقط لأن Reference EOS مفعّل.
