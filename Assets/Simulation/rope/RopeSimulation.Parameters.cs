using UnityEngine;

namespace Simulation
{
    public sealed partial class RopeSimulation
    {
        // ════════════════════════════════════════════════════════════════════
        //  PARAMETERS - معاملات الفيزياء
        // ════════════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════════════
        //  Air Drag & Wind - مقاومة الهواء والرياح
        // ════════════════════════════════════════════════════════════════════
        [Header("Air Drag & Wind")]
        [SerializeField] float   _airDensity = 1.225f;
        [SerializeField] float   _dragCoefficient = 3.0f;
        [SerializeField] Vector3 _windVelocity = new Vector3(20f, 0f, 0f);
        [SerializeField] float   _windNoiseScale = 0.5f;
        [SerializeField] float   _windTimeScale = 1.0f;
        [SerializeField] float   _maxDragForce = 500f;
        [SerializeField] float   _ropeRadiusForDrag = 0.08f;

        // ════════════════════════════════════════════════════════════════════
        //  Stability - معاملات الاستقرار
        // ════════════════════════════════════════════════════════════════════
        [Header("Stability")]
        [SerializeField] float   _selfCollisionCompliance = 0.001f;
        [SerializeField] float   _maxAngularVelocity = 50f;

        // ════════════════════════════════════════════════════════════════════
        //  Break Detection - كشف الانقطاع بناءً على التوتر
        // ════════════════════════════════════════════════════════════════════
        [Header("Break Detection (Tension-Based)")]
        [Tooltip("تفعيل كشف الانقطاع التلقائي بناءً على قوة الشد")]
        [SerializeField] bool    _enableBreakDetection = true;
        
        [Tooltip("عتبة الشد التي تسبب الانقطاع (بالنيوتن)")]
        [SerializeField] float   _breakThreshold = 50.0f;
        
        [Tooltip("قوة تحمل الحبل الأساسية (0 = استخدام breakThreshold مباشرة)")]
        [SerializeField] float   _ropeStrength = 0f;

        // ════════════════════════════════════════════════════════════════════
        //  Rope Breaking - انقطاع الحبل بناءً على الوزن
        // ════════════════════════════════════════════════════════════════════
        [Header("Rope Breaking (Weight-Based)")]
        [Tooltip("تفعيل خاصية الانقطاع عند تجاوز وزن محدد")]
        [SerializeField] bool    _enableBreaking = true;
        
        [Tooltip("الوزن الذي يسبب انقطاع الحبل (كجم)")]
        [SerializeField] float   _maxBreakWeight = 4.0f;
        
        [Tooltip("رقم الجسيم الذي سينقطع عنده الحبل")]
        [SerializeField] int     _breakParticleIndex = 15;
        
        [Tooltip("قوة الدفع عند الانقطاع لمحاكاة السقوط الواقعي")]
        [SerializeField] float   _breakImpulse = 2.0f;

        // ════════════════════════════════════════════════════════════════════
        //  Breaking State - حالة الانقطاع (Runtime)
        // ════════════════════════════════════════════════════════════════════
        private bool _isBroken = false;
        private int  _currentBreakIndex = -1;  // -1 يعني لا يوجد انقطاع

        // ════════════════════════════════════════════════════════════════════
        //  Public Properties - خصائص عامة للوصول إلى حالة الانقطاع
        // ════════════════════════════════════════════════════════════════════
        public bool IsBroken => _isBroken;
        public int CurrentBreakIndex => _currentBreakIndex;
        public bool EnableBreaking 
        { 
            get => _enableBreaking; 
            set => _enableBreaking = value; 
        }
        public float MaxBreakWeight 
        { 
            get => _maxBreakWeight; 
            set => _maxBreakWeight = Mathf.Max(0.1f, value); 
        }
        public int BreakParticleIndex 
        { 
            get => _breakParticleIndex; 
            set => _breakParticleIndex = Mathf.Clamp(value, 1, _segments - 1); 
        }
    }
}