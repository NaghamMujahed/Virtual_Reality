using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Simulation
{
    [DisallowMultipleComponent]
    public sealed class RopeDebugUI : MonoBehaviour
    {
        RopeSimulation _sim;
        RopeGrab       _grab;
        
        // UI State
        bool   _visible    = true;
        Rect   _windowRect = new Rect(20, 20, 920, 850); // ✅ حجم أكبر للمزيد من العناصر
        const int WinId    = 9822;
        
        // Performance caching
        FrameTiming[] _frameBuf = new FrameTiming[1];
        float         _gpuMs, _cpuMs, _smoothFps;
        float         _lastUpdate;
        const float   _updateInterval = 0.1f; // تحديث كل 0.1 ثانية بدلاً من كل فريم

        // Text field buffers
        readonly Dictionary<string, string> _tbuf = new Dictionary<string, string>();

        // Cached styles - لا تتغير
        GUIStyle _sSection, _sLabel, _sValue, _sBig, _sSmall, _sSliderVal, _sTF;
        Texture2D _white;
        bool      _stylesBuilt;

        // Cached values لتقليل العمليات
        string _fpsText, _gpuText, _cpuText, _ramText, _vramText;
        
        // ────────────────────────────────────────────────────────────────────

        void Awake()
        {
            _sim  = GetComponent<RopeSimulation>();
            _grab = GetComponent<RopeGrab>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
                _visible = !_visible;

            if (!_visible) return;

            // تحديث الإحصائيات بفواصل زمنية (ليس كل فريم)
            _lastUpdate += Time.unscaledDeltaTime;
            if (_lastUpdate >= _updateInterval)
            {
                _lastUpdate = 0f;
                UpdateStats();
            }
        }

        void UpdateStats()
        {
            // GPU/CPU timing
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameBuf) > 0)
            {
                _gpuMs = (float)_frameBuf[0].gpuFrameTime;
                _cpuMs = (float)_frameBuf[0].cpuFrameTime;
            }
            
            // Smoothed FPS
            _smoothFps = Mathf.Lerp(_smoothFps,
                                     1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.07f);
            
            // Cache texts
            float msPF = _smoothFps > 0f ? 1000f / _smoothFps : 0f;
            _fpsText = $"{_smoothFps:F0}";
            _gpuText = $"{_gpuMs:F2} ms";
            _cpuText = $"{_cpuMs:F2} ms";
        }

        void OnGUI()
        {
            if (!_visible || _sim == null) return;
            
            // بناء الـ Styles مرة واحدة فقط
            if (!_stylesBuilt) BuildStyles();
            
            _windowRect = GUILayout.Window(WinId, _windowRect, Draw,
                                            "   Rope Simulation   [F1] ",
                                           GUILayout.Width(920));
        }

        // ────────────────────────────────────────────────────────────────────
        //  Main draw
        // ────────────────────────────────────────────────────────────────────

        void Draw(int id)
        {
            GUILayout.Space(12);

            // ══════════════  PERFORMANCE  ══════════════
            SecTitle("PERFORMANCE  —  الأداء");

            float msPF = _smoothFps > 0f ? 1000f / _smoothFps : 0f;
            Color fpsC = _smoothFps >= 60f ? HC("44ff66")
                       : _smoothFps >= 30f ? HC("ffcc22") : HC("ff4444");
            
            BigRow("FPS", _fpsText, fpsC, $"وقت الإطار: {msPF:F1} ms");
            GUILayout.Space(5);

            // GPU
            float vrB  = 1000f / 90f;
            float gpuP = _gpuMs > 0f ? Mathf.Clamp01(_gpuMs / vrB) : 0f;
            Color gpuC = Color.Lerp(HC("3399ff"), HC("ff3333"), Mathf.Pow(gpuP, 1.8f));
            
            if (_gpuMs > 0f)
            {
                BigRow("GPU Time", _gpuText, gpuC,
                       $"{gpuP * 100f:F0}%  من ميزانية  {vrB:F1} ms  (VR 90fps)");
                Bar(gpuP, HC("3399ff"));
            }
            else
                ValRow("GPU Time", "<color=#666666>N/A — فعّل Frame Timing Stats في Player Settings</color>");

            GUILayout.Space(3);
            ValRow("CPU Time", _cpuText);
            Divider();

            // RAM
            long rU = Profiler.GetTotalAllocatedMemoryLong();
            long rR = Profiler.GetTotalReservedMemoryLong();
            float rP = rR > 0 ? (float)rU / rR : 0f;
            BigRow("RAM", $"{rU / 1048576f:F0} MB", HC("44cc55"),
                   $"محجوز: {rR / 1048576f:F0} MB   —   {rP * 100f:F0}% مستخدم");
            Bar(rP, HC("44cc55"));

            long vram = Profiler.GetAllocatedMemoryForGraphicsDriver();
            ValRow("VRAM", $"{vram / 1048576f:F1} MB  (graphics driver)");
            Divider();

            // GPU ops - ✅ محدث ليشمل kernels الجديدة
            int sub  = _sim.LastSubstepCount;
            int dpS  = 2 + _sim.SolverIter * 7; // ✅ من 5 إلى 7 (أضفنا SolveTwist + BuildSpatialHash)
            int tD   = sub * dpS;
            int sK   = sub * _sim.SolverIter * 6; // ✅ من 4 إلى 6 kernels
            BigRow("GPU Dispatches", $"{tD}", HC("ffcc44"),
                   $"{sub} substep  ×  {dpS}  kernel calls");
            BigRow("Solver Kernels", $"{sK}", HC("ffaa33"),
                   $"{_sim.SolverIter} iter  ×  6 kernels  ×  {sub} substep");
            
            int N = _sim.Segments;
            GUILayout.BeginHorizontal();
            GUILayout.Space(26);
            GUILayout.Label(
                $"Predict×{sub}   Stretch×{sub*_sim.SolverIter*2} " +
                $"   Bend×{sub*_sim.SolverIter*2}   Twist×{sub*_sim.SolverIter} " +
                $"   NormQ×{sub*_sim.SolverIter}   UpdateVel×{sub}",
                _sSmall);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            
            long bB = (long)(N+1)*40 + (long)N*64;
            ValRow("GPU Buffers", $"{bB/1024f:F1} KB   (9 SoA buffers — {N+1} particles + {N} quats)");

            GUILayout.Space(22);

            // ══════════════  STIFFNESS  ══════════════
            SecTitle("STIFFNESS  —  الصلابة");

            float sk = EditRow("sk", "Stretch K",    _sim.StretchK,   0.01f, 1f,   "F3");
            float bk = EditRow("bk", "Bend/Twist K", _sim.BendTwistK, 0f,    1f,   "F3");
            float si = EditRow("si", "Solver Iter",  _sim.SolverIter, 1,     300,  "F0"); // ✅ من 120 إلى 300
            _sim.StretchK   = sk;
            _sim.BendTwistK = bk;
            _sim.SolverIter = Mathf.RoundToInt(si);

            GUILayout.Space(15);

            // ✅ جديد: Compliance Sliders
            SecTitle("COMPLIANCE  —  المرونة");
            float sc = EditRow("sc", "Stretch Compliance", _sim.StretchCompliance, 0f, 1e-5f, "E2");
            float bc = EditRow("bc", "Bend Compliance",    _sim.BendCompliance,    0f, 1e-5f, "E2");
            _sim.StretchCompliance = sc;
            _sim.BendCompliance    = bc;

            GUILayout.Space(22);

            // ══════════════  PHYSICS  ══════════════
            SecTitle("PHYSICS  —  الفيزياء");

            float nm = EditRow("bm",  "Bucket Mass", _sim.BucketMass, 0.1f,  5f,    "F2");
            float gy = EditRow("gy",  "Gravity Y",   _sim.Gravity.y,  -30f,  0f,    "F1");
            float dm = EditRow("dm",  "Damping",     _sim.VelocityDamping, 0.9f, 1f,    "F3");
            
            if (!Mathf.Approximately(nm, _sim.BucketMass)) _sim.SetBucketMass(nm);
            if (!Mathf.Approximately(gy, _sim.Gravity.y))  _sim.Gravity = new Vector3(0f, gy, 0f);
            _sim.VelocityDamping = dm;

            GUILayout.Space(15);

            // ✅ محسّن: أزرار Presets مع setters كاملة
            SecTitle("PRESETS  —  الإعدادات الجاهزة");
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("🎯 Realistic Rope", GUILayout.Height(40)))
            {
                _sim.StretchK = 1.0f;
                _sim.BendTwistK = 0.95f;
                _sim.SolverIter = 100;
                _sim.VelocityDamping = 0.95f;
                _sim.StretchCompliance = 1e-9f;
                _sim.BendCompliance = 1e-9f;
                Debug.Log("[RopeDebugUI] Applied Realistic Rope preset");
            }

            if (GUILayout.Button("🎪 Soft Rope", GUILayout.Height(40)))
            {
                _sim.StretchK = 0.8f;
                _sim.BendTwistK = 0.6f;
                _sim.SolverIter = 60;
                _sim.VelocityDamping = 0.98f;
                _sim.StretchCompliance = 1e-6f;
                _sim.BendCompliance = 1e-6f;
                Debug.Log("[RopeDebugUI] Applied Soft Rope preset");
            }

            if (GUILayout.Button("🔒 Stiff Rope", GUILayout.Height(40)))
            {
                _sim.StretchK = 1.0f;
                _sim.BendTwistK = 1.0f;
                _sim.SolverIter = 150;
                _sim.VelocityDamping = 0.90f;
                _sim.StretchCompliance = 1e-10f;
                _sim.BendCompliance = 1e-10f;
                Debug.Log("[RopeDebugUI] Applied Stiff Rope preset");
            }
            
            GUILayout.EndHorizontal();
            GUILayout.Space(22);

            // ══════════════  ROPE INFO  ══════════════
            SecTitle("ROPE INFO  (read-only)");
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            InfoChip($"Segments:  {_sim.Segments}");
            GUILayout.Space(20);
            InfoChip($"Length:  {_sim.TotalLength:F2} m");
            GUILayout.Space(20);
            InfoChip($"Seg:  {_sim.SegLen * 100f:F1} cm");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ✅ جديد: عرض Grab Index
            if (_sim.GrabIndex >= 0)
            {
                GUILayout.Space(10);
                BigRow("Grab Index", $"{_sim.GrabIndex}", HC("ffaa00"), 
                       $"جسيم ممسوك حالياً — الجزء السفلي حر");
            }
            else
            {
                GUILayout.Space(10);
                ValRow("Grab Index", "<color=#666666>لا يوجد إمساك</color>");
            }

            // ══════════════  GRAB  ══════════════
            if (_grab != null)
            {
                GUILayout.Space(22);
                SecTitle("GRAB  —  الإمساك");
                float pr = EditRow("pr",  "Pick Radius",  _grab.PickRadiusPx,    5f,   200f,  "F0");
                float ds = EditRow("ds",  "Sensitivity",  _grab.DragSensitivity, 0.1f, 5f,    "F2");
                _grab.PickRadiusPx    = pr;
                _grab.DragSensitivity = ds;
            }

            GUILayout.Space(16);
            GUI.DragWindow(new Rect(0, 0, 10000, 26));
        }

        // ────────────────────────────────────────────────────────────────────
        //  Editable row
        // ────────────────────────────────────────────────────────────────────

        float EditRow(string id, string label, float val, float min, float max, string fmt)
        {
            bool hasFocus = GUI.GetNameOfFocusedControl() == id;
            if (!hasFocus || !_tbuf.ContainsKey(id))
                _tbuf[id] = val.ToString(fmt);

            GUILayout.BeginHorizontal();
            GUILayout.Label("   " + label, _sLabel, GUILayout.Width(230));

            float sv = GUILayout.HorizontalSlider(val, min, max,
                                                   GUILayout.Width(500),
                                                   GUILayout.Height(30));
            if (!Mathf.Approximately(sv, val))
            {
                val = sv;
                _tbuf[id] = sv.ToString(fmt);
            }

            GUI.SetNextControlName(id);
            string ns = GUILayout.TextField(_tbuf[id], _sTF, GUILayout.Width(130));
            if (ns != _tbuf[id])
            {
                _tbuf[id] = ns;
                if (ns.Length > 0 && char.IsDigit(ns[ns.Length - 1]))
                {
                    if (float.TryParse(ns,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float p))
                        val = Mathf.Clamp(p, min, max);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(7);
            return val;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Primitives
        // ────────────────────────────────────────────────────────────────────

        void SecTitle(string t)
        {
            GUILayout.Space(2);
            GUILayout.Label(t, _sSection);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                             GUILayout.Height(2), GUILayout.ExpandWidth(true));
            r.x += 6; r.width -= 12;
            GUI.color = new Color(1f, 0.75f, 0.18f, 0.5f);
            GUI.DrawTexture(r, _white);
            GUI.color = Color.white;
            GUILayout.Space(7);
        }

        void BigRow(string label, string num, Color c, string sub)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   " + label, _sLabel, GUILayout.Width(230));
            GUILayout.Label($"<color={ToHex(c)}><b>{num}</b></color>", _sBig, GUILayout.Width(200));
            GUILayout.Label($"<color=#aaaaaa>{sub}</color>", _sValue);
            GUILayout.EndHorizontal();
        }

        void ValRow(string label, string val)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   " + label, _sLabel, GUILayout.Width(230));
            GUILayout.Label(val, _sValue);
            GUILayout.EndHorizontal();
        }

        void Bar(float t, Color fill)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                               GUILayout.Height(28), GUILayout.Width(670));
            GUI.color = new Color(0.07f, 0.07f, 0.07f, 1f);
            GUI.DrawTexture(r, _white);
            if (t > 0f)
            {
                GUI.color = Color.Lerp(fill, HC("ff2222"), Mathf.Pow(t, 2f));
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * t, r.height), _white);
            }
            GUI.color = Color.white;
            GUILayout.Space(10);
            GUILayout.Label($"<b>{t*100f:F0}%</b>", _sBig, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        void Divider()
        {
            GUILayout.Space(9);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                             GUILayout.Height(1), GUILayout.ExpandWidth(true));
            r.x += 6; r.width -= 12;
            GUI.color = new Color(0.38f, 0.38f, 0.38f, 0.7f);
            GUI.DrawTexture(r, _white);
            GUI.color = Color.white;
            GUILayout.Space(9);
        }

        void InfoChip(string text)
        {
            GUILayout.Label(text, _sValue, GUILayout.Width(240));
        }

        // ────────────────────────────────────────────────────────────────────
        //  Styles - تُبنى مرة واحدة فقط
        // ────────────────────────────────────────────────────────────────────

        void BuildStyles()
        {
            if (_stylesBuilt) return;

            _sSection = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 28,
                normal    = { textColor = new Color(1f, 0.78f, 0.18f) },
            };
            
            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                normal   = { textColor = new Color(0.70f, 0.70f, 0.70f) },
            };
            
            _sValue = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 22,
                normal   = { textColor = new Color(0.92f, 0.92f, 0.92f) },
            };
            
            _sBig = new GUIStyle(GUI.skin.label)
            {
                richText  = true,
                fontStyle = FontStyle.Bold,
                fontSize  = 30,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white },
            };
            
            _sSmall = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 18,
                normal   = { textColor = new Color(0.50f, 0.50f, 0.50f) },
            };
            
            _sTF = new GUIStyle(GUI.skin.textField)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 24,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 0.92f, 0.45f),
                              background = MakeTex(2, 2, new Color(0.12f, 0.12f, 0.12f)) },
                focused   = { textColor = Color.white,
                              background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.28f)) },
            };

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            
            _stylesBuilt = true;
        }

        static Texture2D MakeTex(int w, int h, Color c)
        {
            var t = new Texture2D(w, h);
            var p = new Color[w * h];
            for (int i = 0; i < p.Length; i++) p[i] = c;
            t.SetPixels(p); t.Apply();
            return t;
        }

        static Color HC(string h) =>
            ColorUtility.TryParseHtmlString("#" + h, out Color c) ? c : Color.white;

        static string ToHex(Color c) =>
            $"#{CB(c.r):X2}{CB(c.g):X2}{CB(c.b):X2}";

        static int CB(float f) => Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255);
    }
}