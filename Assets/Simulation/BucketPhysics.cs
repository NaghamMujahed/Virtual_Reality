using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// نظام فيزياء الدلو كبندول 3D مستقل
    /// يحسب الدوران والزخم الزاوي والقصور الذاتي
    /// يتفاعل ثنائي الاتجاه مع الحبل
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BucketController))]
    public sealed class BucketPhysics : MonoBehaviour
    {
        [Header("Physics Properties")]
        [SerializeField] float _mass = 2.5f;
        [SerializeField] float _handleOffset = 0.35f;
        [SerializeField] float _linearDamping = 0.98f;
        [SerializeField] float _angularDamping = 0.95f;
        [SerializeField] float _springStiffness = 100f;
        [SerializeField] float _springDamping = 10f;
        [SerializeField] float _rotationalStiffness = 50f;
        [SerializeField] float _rotationalDamping = 5f;

        // الحالة الفيزيائية
        private Vector3 _position;
        private Vector3 _velocity;
        private Quaternion _rotation;
        private Vector3 _angularVelocity;
        
        private Vector3 _attachmentPoint;
        private BucketController _bucket;
        private bool _initialized = false;

        // عزم القصور الذاتي (مبسط)
        private float _inertiaTensor;

        void Awake()
        {
            _bucket = GetComponent<BucketController>();
            InitializeInertiaTensor();
        }

        void Start()
        {
            Initialize();
        }

        void Initialize()
        {
            _position = transform.position;
            _rotation = transform.rotation;
            _velocity = Vector3.zero;
            _angularVelocity = Vector3.zero;
            _initialized = true;
        }

        void InitializeInertiaTensor()
        {
            // حساب عزم القصور الذاتي للدلو (تقريبي - أسطوانة)
            float radius = 0.20f; // نصف قطر الدلو
            float height = 0.32f; // ارتفاع الدلو
            
            // I = (1/12) * m * (3r² + h²) للأسطوانة
            _inertiaTensor = (1f / 12f) * _mass * (3f * radius * radius + height * height);
        }

        /// <summary>
        /// تحديث نقطة التعلق بالحبل
        /// </summary>
        public void SetAttachmentPoint(Vector3 ropeEnd)
        {
            _attachmentPoint = ropeEnd;
        }

        /// <summary>
        /// خطوة المحاكاة الفيزيائية
        /// </summary>
        public void SimulateStep(float dt, Vector3 gravity)
        {
            if (!_initialized) Initialize();
            if (_attachmentPoint == Vector3.zero) return;

            // 1. حساب نقطة المقبض (handle) في الفضاء العالمي
            Vector3 handleLocal = Vector3.up * _handleOffset;
            Vector3 handleWorld = transform.TransformPoint(handleLocal);

            // 2. قوة الزنبرك نحو نقطة التعلق (Spring Force)
            Vector3 displacement = _attachmentPoint - handleWorld;
            Vector3 springForce = displacement * _springStiffness;
            
            // 3. قوة التخميد (Damping Force)
            Vector3 handleVelocity = _velocity + Vector3.Cross(_angularVelocity, handleLocal);
            Vector3 dampingForce = -handleVelocity * _springDamping;

            // 4. تطبيق القوى الخطية
            Vector3 totalForce = springForce + dampingForce + gravity * _mass;
            _velocity += totalForce / _mass * dt;
            _velocity *= _linearDamping;

            // 5. حساب عزم الدوران (Torque)
            Vector3 torque = Vector3.Cross(handleLocal, springForce + dampingForce);
            
            // إضافة عزم تصحيحي للتوجيه
            Vector3 currentUp = transform.up;
            Vector3 targetUp = -displacement.normalized;
            if (targetUp != Vector3.zero)
            {
                Vector3 axis = Vector3.Cross(currentUp, targetUp);
                float angle = Mathf.Asin(Mathf.Clamp(axis.magnitude, -1f, 1f));
                if (angle > 0.01f)
                {
                    torque += axis.normalized * angle * _rotationalStiffness;
                }
            }
            
            // تطبيق التخميد الزاوي
            torque -= _angularVelocity * _rotationalDamping;

            // 6. تحديث السرعة الزاوية
            _angularVelocity += torque / _inertiaTensor * dt;
            _angularVelocity *= _angularDamping;

            // 7. تحديث الموقع والدوران
            _position += _velocity * dt;
            
            // تحديث الدوران باستخدام Angular Velocity
            if (_angularVelocity.sqrMagnitude > 0.001f)
            {
                float angle = _angularVelocity.magnitude * dt;
                Vector3 axis = _angularVelocity.normalized;
                Quaternion deltaRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
                _rotation = deltaRotation * _rotation;
            }

            // 8. تطبيق النتائج على Transform
            transform.position = _position;
            transform.rotation = _rotation;
        }

        /// <summary>
        /// تطبيق قوة خارجية (مثل اصطدام)
        /// </summary>
        public void ApplyImpulse(Vector3 impulse, Vector3 point)
        {
            _velocity += impulse / _mass;
            
            Vector3 localPoint = transform.InverseTransformPoint(point);
            _angularVelocity += Vector3.Cross(localPoint, impulse) / _inertiaTensor;
        }

        /// <summary>
        /// إعادة تعيين الحالة
        /// </summary>
        public void ResetState(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
            _velocity = Vector3.zero;
            _angularVelocity = Vector3.zero;
            transform.position = position;
            transform.rotation = rotation;
        }

        // Getters
        public Vector3 Position => _position;
        public Vector3 Velocity => _velocity;
        public Quaternion Rotation => _rotation;
        public float Mass => _mass;
    }
}