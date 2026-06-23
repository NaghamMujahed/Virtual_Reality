using UnityEngine;
using System.Collections.Generic;

namespace Simulation
{
    /// <summary>
    /// يبني mesh الدلو إجرائياً (Procedural Mesh) مع دعم الثقوب
    /// يتكون من: جسم مخروطي مبتور + قاعدة + حافة علوية + مقبض + ثقوب اختيارية
    /// </summary>
    public static class BucketMeshBuilder
    {
        /// <summary>
        /// بناء الدلو الأساسي (بدون ثقوب)
        /// </summary>
        public static Mesh Build()
        {
            return BuildWithHoles(false, 0, false, 0);
        }

        /// <summary>
        /// بناء الدلو مع ثقوب اختيارية
        /// </summary>
        /// <param name="hasDrainHoles">هل يحتوي على ثقوب تصريف في القاع</param>
        /// <param name="drainHoleCount">عدد ثقوب التصريف</param>
        /// <param name="hasSideHoles">هل يحتوي على ثقوب جانبية</param>
        /// <param name="sideHoleCount">عدد الثقوب الجانبية</param>
        public static Mesh BuildWithHoles(
            bool hasDrainHoles = false, 
            int drainHoleCount = 1,
            bool hasSideHoles = false,
            int sideHoleCount = 0)
        {
            // ── أبعاد الدلو الأساسية ──
            const float topR = 0.20f;        // نصف قطر الأعلى
            const float botR = 0.13f;        // نصف قطر الأسفل
            const float height = 0.32f;      // الارتفاع الكلي
            const int radSegs = 64;          // عدد القطاعات الدائرية (دقة عالية)
            
            // ── أبعاد الحافة العلوية ──
            const float rimR = 0.225f;       // نصف قطر الحافة الخارجية
            const float rimH = 0.025f;       // ارتفاع الحافة
            
            // ── أبعاد المقبض ──
            const float archH = 0.19f;       // ارتفاع قوس المقبض
            const float thick = 0.025f;      // سمك المقبض
            const int hRadSegs = 12;         // قطاعات المقبض الدائرية
            const int hLonSegs = 20;         // قطاعات المقبض الطولية

            float halfH = height * 0.5f;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // ── بناء الأجزاء الأساسية ──
            BuildBody(verts, norms, uvs, tris, topR, botR, height, radSegs, halfH);
            
            if (hasDrainHoles && drainHoleCount > 0)
                BuildBottomCapWithHoles(verts, norms, uvs, tris, botR, halfH, radSegs, drainHoleCount);
            else
                BuildBottomCap(verts, norms, uvs, tris, botR, halfH, radSegs);
            
            BuildRim(verts, norms, uvs, tris, topR, rimR, rimH, halfH, radSegs);
            BuildHandle(verts, norms, uvs, tris, topR, halfH, archH, thick, hRadSegs, hLonSegs);
            
            if (hasSideHoles && sideHoleCount > 0)
                BuildSideHoles(verts, norms, uvs, tris, topR, botR, height, halfH, radSegs, sideHoleCount);

            // ── إنشاء الـ Mesh النهائي ──
            var mesh = new Mesh();
            mesh.name = "ProceduralBucket";  // مهم جداً: يجب تعيين الاسم ليتعرف عليه BucketController
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();      // إضافة Tangents للإضاءة الصحيحة

            return mesh;
        }

        /// <summary>
        /// بناء جسم الدلو (المخروط المبتور)
        /// </summary>
        static void BuildBody(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float topR, float botR, float height, int radSegs, float halfH)
        {
            // بناء نقاط الجسم (حلقتان: علوية وسفلية)
            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

                // نقطة علوية
                verts.Add(new Vector3(topR * ca, halfH, topR * sa));
                // نقطة سفلية
                verts.Add(new Vector3(botR * ca, -halfH, botR * sa));

                // حساب الـ Normal (عمودي على السطح المائل)
                float dr = topR - botR;
                float nx = ca * height, ny = -dr, nz = sa * height;
                float nl = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                norms.Add(new Vector3(nx / nl, ny / nl, nz / nl));
                norms.Add(new Vector3(nx / nl, ny / nl, nz / nl));

                // UV coordinates
                uvs.Add(new Vector2((float)i / radSegs, 1f));
                uvs.Add(new Vector2((float)i / radSegs, 0f));
            }

            // إنشاء المثلثات للجسم (كل قطاع = مثلثان)
            for (int i = 0; i < radSegs; i++)
            {
                int i0 = i * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }

        /// <summary>
        /// بناء القاعدة السفلية (بدون ثقوب)
        /// </summary>
        static void BuildBottomCap(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float botR, float halfH, int radSegs)
        {
            int btmC = verts.Count;
            
            // نقطة المركز
            verts.Add(new Vector3(0f, -halfH, 0f));
            norms.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // نقاط المحيط
            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                verts.Add(new Vector3(botR * Mathf.Cos(a), -halfH, botR * Mathf.Sin(a)));
                norms.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }

            // مثلثات القاعدة (Fan triangulation من المركز إلى المحيط)
            for (int i = 0; i < radSegs; i++)
            {
                tris.Add(btmC);
                tris.Add(btmC + i + 2);
                tris.Add(btmC + i + 1);
            }
        }

        /// <summary>
        /// بناء القاعدة السفلية مع ثقوب التصريف
        /// ملاحظة: الثقوب هنا تمثيل بصري (حلقات) وليست فراغات هندسية حقيقية
        /// للحصول على فراغات حقيقية، نحتاج Boolean operations
        /// </summary>
        static void BuildBottomCapWithHoles(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float botR, float halfH, int radSegs, int holeCount)
        {
            // ── إعدادات الثقوب ──
            const float holeRadius = 0.015f;     // نصف قطر الثقب
            float holeRingRadius = botR * 0.5f;  // موقع الثقوب من المركز (يعتمد على botR)
            const int holeSegs = 16;             // دقة الثقب

            // ── بناء القاعدة الخارجية الكاملة ──
            int btmC = verts.Count;
            verts.Add(new Vector3(0f, -halfH, 0f));
            norms.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                verts.Add(new Vector3(botR * Mathf.Cos(a), -halfH, botR * Mathf.Sin(a)));
                norms.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }

            // ── بناء حلقات الثقوب ──
            int holeBase = verts.Count;
            for (int h = 0; h < holeCount; h++)
            {
                float holeAngle = (float)h / holeCount * Mathf.PI * 2f;
                float hx = Mathf.Cos(holeAngle) * holeRingRadius;
                float hz = Mathf.Sin(holeAngle) * holeRingRadius;

                for (int i = 0; i <= holeSegs; i++)
                {
                    float a = (float)i / holeSegs * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

                    // الحلقة الخارجية للثقب
                    verts.Add(new Vector3(hx + holeRadius * ca, -halfH, hz + holeRadius * sa));
                    norms.Add(Vector3.down);
                    uvs.Add(new Vector2(0.5f + 0.5f * ca, 0.5f + 0.5f * sa));

                    // الحلقة الداخلية للثقب (الفجوة الفعلية)
                    verts.Add(new Vector3(hx + holeRadius * 0.7f * ca, -halfH, hz + holeRadius * 0.7f * sa));
                    norms.Add(Vector3.down);
                    uvs.Add(new Vector2(0.5f + 0.35f * ca, 0.5f + 0.35f * sa));
                }
            }

            // ── مثلثات القاعدة الخارجية ──
            for (int i = 0; i < radSegs; i++)
            {
                tris.Add(btmC);
                tris.Add(btmC + i + 2);
                tris.Add(btmC + i + 1);
            }

            // ── مثلثات حلقات الثقوب ──
            for (int h = 0; h < holeCount; h++)
            {
                int baseIdx = holeBase + h * (holeSegs + 1) * 2;
                for (int i = 0; i < holeSegs; i++)
                {
                    int i0 = baseIdx + i * 2;
                    int i1 = i0 + 1;
                    int i2 = i0 + 2;
                    int i3 = i0 + 3;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i2); tris.Add(i1); tris.Add(i3);
                }
            }
        }

        /// <summary>
        /// بناء الحافة العلوية (Rim) - تتكون من جدار خارجي + سطح علوي
        /// </summary>
        static void BuildRim(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float topR, float rimR, float rimH, float halfH, int radSegs)
        {
            // الحلقة السفلية الخارجية
            int rimOutBtm = verts.Count;
            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts.Add(new Vector3(rimR * ca, halfH, rimR * sa));
                norms.Add(new Vector3(ca, 0f, sa));
                uvs.Add(new Vector2((float)i / radSegs, 0f));
            }

            // الحلقة العلوية الخارجية
            int rimOutTop = verts.Count;
            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts.Add(new Vector3(rimR * ca, halfH + rimH, rimR * sa));
                norms.Add(new Vector3(ca, 0f, sa));
                uvs.Add(new Vector2((float)i / radSegs, 1f));
            }

            // الحلقة العلوية الداخلية
            int rimInTop = verts.Count;
            for (int i = 0; i <= radSegs; i++)
            {
                float a = (float)i / radSegs * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts.Add(new Vector3(topR * ca, halfH + rimH, topR * sa));
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + 0.5f * ca, 0.5f + 0.5f * sa));
            }

            // الجدار الخارجي: من الحلقة السفلية إلى العلوية
            for (int i = 0; i < radSegs; i++)
            {
                int a = rimOutBtm + i, b = rimOutBtm + i + 1;
                int c = rimOutTop + i, d = rimOutTop + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            // السطح العلوي: من الحلقة الخارجية إلى الداخلية
            for (int i = 0; i < radSegs; i++)
            {
                int a = rimOutTop + i, b = rimOutTop + i + 1;
                int c = rimInTop + i, d = rimInTop + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        /// <summary>
        /// بناء المقبض (أنبوب على شكل قوس نصف إهليلجي)
        /// </summary>
        static void BuildHandle(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float topR, float halfH, float archH, float thick, int hRadSegs, int hLonSegs)
        {
            int hBase = verts.Count;

            // بناء نقاط على طول القوس
            for (int j = 0; j <= hLonSegs; j++)
            {
                float t = (float)j / hLonSegs;
                float a = t * Mathf.PI;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

                // مركز الأنبوب عند هذه النقطة
                float cx = topR * ca;
                float cy = halfH + archH * sa;
                float cz = 0f;

                // المتجه العمودي على القوس (للـ cross-section)
                float nx1 = -topR * ca;
                float ny1 = -archH * sa;
                float nl1 = Mathf.Sqrt(nx1 * nx1 + ny1 * ny1);
                nx1 /= nl1; ny1 /= nl1;

                // بناء دائرة cross-section عند كل نقطة على القوس
                for (int i = 0; i <= hRadSegs; i++)
                {
                    float ra = (float)i / hRadSegs * Mathf.PI * 2f;
                    float rc = Mathf.Cos(ra), rs = Mathf.Sin(ra);

                    verts.Add(new Vector3(
                        cx + thick * (rc * nx1),
                        cy + thick * (rc * ny1),
                        cz + thick * rs
                    ));

                    // Normal يشير للخارج من مركز الأنبوب
                    float nnx = verts[verts.Count - 1].x - cx;
                    float nny = verts[verts.Count - 1].y - cy;
                    float nnz = verts[verts.Count - 1].z - cz;
                    float nnl = Mathf.Sqrt(nnx * nnx + nny * nny + nnz * nnz);
                    norms.Add(new Vector3(nnx / nnl, nny / nnl, nnz / nnl));

                    uvs.Add(new Vector2((float)i / hRadSegs, (float)j / hLonSegs));
                }
            }

            // إنشاء المثلثات للمقبض
            for (int j = 0; j < hLonSegs; j++)
            {
                for (int i = 0; i < hRadSegs; i++)
                {
                    int i0 = hBase + j * (hRadSegs + 1) + i;
                    int i1 = i0 + 1;
                    int i2 = i0 + (hRadSegs + 1);
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }
        }

        /// <summary>
        /// بناء الثقوب الجانبية (اختياري)
        /// ملاحظة: نفس ملاحظة BuildBottomCapWithHoles - تمثيل بصري وليس فراغات حقيقية
        /// </summary>
        static void BuildSideHoles(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            float topR, float botR, float height, float halfH, int radSegs, int holeCount)
        {
            const float holeRadius = 0.02f;
            const int holeSegs = 16;
            const float holeHeight = 0f; // ارتفاع الثقوب من المركز

            for (int h = 0; h < holeCount; h++)
            {
                float holeAngle = (float)h / holeCount * Mathf.PI * 2f;
                float hx = Mathf.Cos(holeAngle) * ((topR + botR) * 0.5f);
                float hz = Mathf.Sin(holeAngle) * ((topR + botR) * 0.5f);

                int holeBase = verts.Count;
                for (int i = 0; i <= holeSegs; i++)
                {
                    float a = (float)i / holeSegs * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

                    // الحلقة الخارجية للثقب
                    verts.Add(new Vector3(hx + holeRadius * ca, holeHeight, hz + holeRadius * sa));
                    norms.Add(new Vector3(ca, 0f, sa));
                    uvs.Add(new Vector2(0.5f + 0.5f * ca, 0.5f + 0.5f * sa));

                    // الحلقة الداخلية للثقب
                    verts.Add(new Vector3(hx + holeRadius * 0.7f * ca, holeHeight, hz + holeRadius * 0.7f * sa));
                    norms.Add(new Vector3(ca, 0f, sa));
                    uvs.Add(new Vector2(0.5f + 0.35f * ca, 0.5f + 0.35f * sa));
                }

                // مثلثات حلقة الثقب
                for (int i = 0; i < holeSegs; i++)
                {
                    int i0 = holeBase + i * 2;
                    int i1 = i0 + 1;
                    int i2 = i0 + 2;
                    int i3 = i0 + 3;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i2); tris.Add(i1); tris.Add(i3);
                }
            }
        }
    }
}