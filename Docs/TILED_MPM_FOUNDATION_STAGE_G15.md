# Tiled MPM Foundation Stage G15-A

## هدف المرحلة

هذه المرحلة تضيف أول بنية فعلية للانتقال من dense MPM إلى sparse/tiled MPM: بناء قائمة GPU للـ tiles النشطة من دعم انتقال الجزيئات `P2G/G2P`.

الهدف ليس استبدال solver كاملاً بعد، بل تأسيس معلومات صحيحة يمكن أن تعتمد عليها المراحل القادمة:

- أي tiles تحتوي فعلاً على دعم جسيمات؟
- ما نسبة tiles النشطة من كامل الشبكة؟
- هل يمكن تشغيل grid clear/update عبر active tiles؟

## ما تم تطبيقه

- إضافة إعدادات:
  - `enableMpmTileOccupancy`
  - `enableTiledMpmGridDispatch`
  - `mpmTileSizeCells`
- بناء GPU tile list:
  - `KClearMpmTileData`
  - `KMarkMpmActiveTiles`
- دعم dispatch غير مباشر للـ active tiles:
  - `KClearGridActiveTiles`
  - `KGridUpdateActiveTiles`
- إضافة buffers:
  - tile flags
  - active tile indices
  - indirect dispatch args
- إضافة diagnostics:
  - `mpmTileCount`
  - `mpmActiveTiles`
  - `mpmActiveTileFraction`
  - `tiledMpmDispatch`

## قرار الافتراضات

تم إبقاء الميزتين غير مفعّلتين افتراضياً:

- `enableMpmTileOccupancy = false`
- `enableTiledMpmGridDispatch = false`

السبب: بناء tile occupancy يعمل بشكل صحيح، لكن تشغيل clear/update عبر active tiles فقط لا يكفي وحده لإعطاء قفزة أداء ثابتة لأن الكلفة الكبرى لا تزال في:

- `P2G`
- `G2P`
- collision kernels
- projection/pressure
- diagnostics أثناء validation

مع ذلك، نتيجة `mpmActiveTileFraction` مهمة جداً لأنها أثبتت أن السائل يشغل تقريباً `13% - 15%` فقط من tiles الشبكة في اختباراتنا. هذا يعني أن sparse/tiled الحقيقي يستحق المتابعة.

## نتائج التحقق

### Default sealed 120

- Log: `g15a-default-sealed-120-rerun.log`
- PASS
- particles: `104436`
- mpmTiles: `False`
- tiledMpmDispatch: `False`
- noGridSupport: `0`
- nanInf: `0`
- lost: `0`
- ms/substep: `39.864`

### Tile occupancy sealed 120

- Log: `g15a-tile-occupancy-sealed-120.log`
- PASS
- particles: `104436`
- mpmTiles: `True`
- tiledMpmDispatch: `False`
- mpmTileCount: `1000`
- mpmActiveTiles: `131`
- mpmActiveTileFraction: `0.131`
- noGridSupport: `0`
- nanInf: `0`
- lost: `0`
- ms/substep: `44.650`

### Tiled grid dispatch sealed 120

- Log: `g15a-tiled-dispatch-sealed-120.log`
- PASS
- particles: `104436`
- mpmTiles: `True`
- tiledMpmDispatch: `True`
- mpmTileCount: `1000`
- mpmActiveTiles: `132`
- mpmActiveTileFraction: `0.132`
- noGridSupport: `0`
- nanInf: `0`
- lost: `0`
- ms/substep: `45.083`

### Tile occupancy outflow smoke

- Log: `g15a-tile-occupancy-outflow-smoke.log`
- PASS
- mpmActiveTiles: `152`
- mpmActiveTileFraction: `0.152`
- outflow observed: `115`
- jet observed: `686`
- airborne observed: `71`
- lost: `0`

## Flags مفيدة

- بناء tile occupancy فقط:
  - `-paintValidationEnableMpmTileOccupancy`
- تفعيل tiled clear/update التجريبي:
  - `-paintValidationEnableTiledMpmGridDispatch`
- تعطيل tile occupancy:
  - `-paintValidationDisableMpmTileOccupancy`
- تعطيل tiled dispatch:
  - `-paintValidationDisableTiledMpmGridDispatch`
- تغيير حجم tile:
  - `-paintValidationMpmTileSize 4`
  - `-paintValidationMpmTileSize 8`

## المرحلة التالية

الربح الحقيقي سيأتي عندما نستخدم هذه القائمة ليس فقط لـ `KClearGrid/KGridUpdate`، بل لـ:

1. بناء particle buckets لكل tile.
2. تشغيل `P2G` tile-local لتقليل atomics.
3. تشغيل `G2P` من particle buckets/active tiles.
4. لاحقاً ربط projection أيضاً بقائمة tiles بدل AABB فقط.

هذه هي النقلة التي ستحول المشروع من dense grid محسن إلى sparse/tiled solver فعلي.
