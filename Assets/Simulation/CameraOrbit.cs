using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation
{
    /// <summary>
    /// كاميرا مدارية تتحكم بالفأرة فقط (بدون لوحة مفاتيح)
    /// - زر الفأرة الأيمن + سحب: دوران حول الدلو
    /// - عجلة التمرير: تقريب/تبعيد
    /// - زر الفأرة الأوسط + سحب: تحريك النقطة المركزية
    /// - تتبع الدلو تلقائياً (اختياري)
    /// </summary>
    public sealed class CameraOrbit : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] Transform _target;  // الدلو أو أي هدف
        [SerializeField] bool _autoFollow = true;  // تتبع تلقائي للهدف
        [SerializeField, Range(0f, 1f)] float _followSmooth = 0.1f;  // سلاسة التتبع

        [Header("Orbit Settings")]
        [SerializeField, Min(0.1f)] float _distance = 3f;  // مسافة أقرب
        [SerializeField, Range(0.1f, 10f)] float _orbitSpeed = 5f;  // سرعة دوران أعلى
        [SerializeField, Range(0.1f, 10f)] float _zoomSpeed = 3f;
        [SerializeField, Range(0.1f, 10f)] float _panSpeed = 0.3f;
        [SerializeField] float _minDist = 1.5f;  // أقرب مسافة
        [SerializeField] float _maxDist = 15f;   // أبعد مسافة
        [SerializeField, Range(-89f, 89f)] float _minVertical = -60f;  // حد أدنى للدوران العمودي
        [SerializeField, Range(-89f, 89f)] float _maxVertical = 80f;   // حد أقصى للدوران العمودي

        [Header("Smoothing")]
        [SerializeField, Range(0f, 1f)] float _rotationSmooth = 0.15f;  // سلاسة الدوران
        [SerializeField, Range(0f, 1f)] float _zoomSmooth = 0.2f;      // سلاسة التكبير

        Vector2 _angles;
        Vector2 _targetAngles;
        float _currentDistance;
        float _targetDistance;
        Vector3 _currentPivot;
        Vector3 _targetPivot;

        void OnEnable()
        {
            // البحث عن الدلو تلقائياً إذا لم يتم تعيينه
            if (_target == null)
            {
                var bucket = FindObjectOfType<BucketController>();
                if (bucket != null)
                    _target = bucket.transform;
            }

            // إعداد النقطة المركزية
            if (_target != null)
            {
                _targetPivot = _target.position + Vector3.up * 0.2f;  // فوق الدلو قليلاً
            }
            else
            {
                _targetPivot = new Vector3(0f, 1.5f, 0f);
            }
            
            _currentPivot = _targetPivot;

            // حساب الزوايا من الموقع الحالي
            Vector3 dir = transform.position - _currentPivot;
            _targetDistance = _distance = Mathf.Clamp(dir.magnitude, _minDist, _maxDist);
            _currentDistance = _distance;
            
            _targetAngles = _angles = new Vector2(
                Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(dir.y / Mathf.Max(_distance, 0.01f), -1f, 1f)) * Mathf.Rad2Deg
            );
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // تحديث النقطة المركزية إذا كان التتبع التلقائي مفعلاً
            if (_autoFollow && _target != null)
            {
                _targetPivot = _target.position + Vector3.up * 0.2f;
            }

            // ── الدوران: زر الفأرة الأيمن + سحب ──
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * _orbitSpeed * 0.1f;
                _targetAngles.x += delta.x;
                _targetAngles.y = Mathf.Clamp(_targetAngles.y - delta.y, _minVertical, _maxVertical);
            }

            // ── التكبير: عجلة التمرير ──
            float scroll = mouse.scroll.ReadValue().y * _zoomSpeed * 0.1f;
            _targetDistance = Mathf.Clamp(_targetDistance - scroll, _minDist, _maxDist);

            // ── التحريك: زر الفأرة الأوسط + سحب ──
            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * _panSpeed * 0.01f;
                Vector3 right = transform.right;
                Vector3 up = transform.up;
                _targetPivot -= right * delta.x + up * delta.y;
                
                // إذا كان التتبع التلقائي مفعلاً، عطله مؤقتاً عند التحريك اليدوي
                if (_autoFollow)
                    _autoFollow = false;
            }

            // ── تطبيق السلاسة (Smoothing) ──
            _angles = Vector2.Lerp(_angles, _targetAngles, 1f - _rotationSmooth);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, 1f - _zoomSmooth);
            _currentPivot = Vector3.Lerp(_currentPivot, _targetPivot, 1f - _followSmooth);

            // ── حساب موقع الكاميرا ──
            float radV = _angles.y * Mathf.Deg2Rad;
            float radH = _angles.x * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(radV) * Mathf.Sin(radH),
                Mathf.Sin(radV),
                Mathf.Cos(radV) * Mathf.Cos(radH)
            ) * _currentDistance;

            transform.position = _currentPivot + offset;
            transform.LookAt(_currentPivot);
        }

        /// <summary>
        /// إعادة تعيين الكاميرا إلى الموقع الافتراضي
        /// </summary>
        public void ResetCamera()
        {
            if (_target != null)
            {
                _targetPivot = _target.position + Vector3.up * 0.2f;
                _targetDistance = 3f;
                _targetAngles = new Vector2(0f, 15f);  // زاوية مريحة
                _autoFollow = true;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (_target != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_target.position + Vector3.up * 0.2f, 0.1f);
            }
        }
    }
}