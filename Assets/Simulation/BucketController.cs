using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// يتحكم بالدلو ويربطه بنهاية الحبل
    /// يُدار حركياً بواسطة RopeSimulation
    /// ExecuteAlways يسمح بالعمل في Editor Mode (قبل Play)
    /// 
    /// التحسينات المضافة:
    /// - SetPose() لحساب الدوران كبندول 3D
    /// - تنعيم الدوران باستخدام Quaternion.Slerp
    /// - دعم BucketPhysics للفيزياء المستقلة
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [ExecuteAlways]  // يعمل في Editor و Play Mode
    public sealed class BucketController : MonoBehaviour
    {
        [Header("Handle Offset")]
        [SerializeField] Vector3 _localHandleOffset = new Vector3(0f, 0.35f, 0f);

        [Header("Bucket Configuration")]
        [SerializeField] bool _hasDrainHoles = false;
        [SerializeField] int _drainHoleCount = 1;
        [SerializeField] bool _hasSideHoles = false;
        [SerializeField] int _sideHoleCount = 0;

        [Header("Pendulum Settings")]
        [SerializeField] float _rotationSmoothing = 5f;  // سرعة تنعيم الدوران
        [SerializeField] bool _enablePendulumRotation = true;  // تفعيل دوران البندول

        // ─── Mesh Management ──────────────────────────────────────────────

        /// <summary>
        /// يُستدعى عند إنشاء الـ Component أو عند Reset في Inspector
        /// يضمن وجود الـ Mesh الصحيح
        /// </summary>
        void Reset()
        {
            EnsureMesh();
        }

        /// <summary>
        /// يُستدعى عند تغيير أي قيمة في Inspector
        /// يضمن تحديث الـ Mesh إذا لزم الأمر
        /// </summary>
        void OnValidate()
        {
            EnsureMesh();
        }

        /// <summary>
        /// يُستدعى عند بدء التشغيل (Play Mode)
        /// </summary>
        void Awake()
        {
            EnsureMesh();

            // إضافة ShowVertices تلقائياً للتصحيح (فقط في Editor)
            #if UNITY_EDITOR
            if (GetComponent<Simulation.ShowVertices>() == null)
            {
                gameObject.AddComponent<Simulation.ShowVertices>();
            }
            #endif
        }

        /// <summary>
        /// يتحقق من وجود الـ Mesh الصحيح وينشئه إذا لزم الأمر
        /// </summary>
        void EnsureMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) return;

            // إذا لم يكن هناك Mesh أو كان اسمه خاطئاً (مثل Cube الافتراضي)
            if (mf.sharedMesh == null || mf.sharedMesh.name != "ProceduralBucket")
            {
                mf.sharedMesh = BucketMeshBuilder.BuildWithHoles(
                    _hasDrainHoles,
                    _drainHoleCount,
                    _hasSideHoles,
                    _sideHoleCount
                );
                Debug.Log("[BucketController] Procedural bucket mesh created.");
            }
        }

        // ─── Position & Rotation ──────────────────────────────────────────

        /// <summary>
        /// نقطة التعلق بالدلو (في الفضاء العالمي)
        /// </summary>
        public Vector3 AttachPoint => transform.TransformPoint(_localHandleOffset);

        /// <summary>
        /// يُستدعى بواسطة RopeSimulation مع موقع آخر جسيم في الحبل
        /// يحرك الدلو بحيث تتطابق نقطة التعلق مع نهاية الحبل
        /// (الطريقة القديمة - بدون دوران)
        /// </summary>
        public void SetPosition(Vector3 ropeEnd)
        {
            transform.position = ropeEnd - transform.TransformVector(_localHandleOffset);
        }

        /// <summary>
        /// ✅ جديد: يُستدعى بواسطة RopeSimulation مع موقع واتجاه الحبل
        /// يحرك الدلو ويحسب دورانه كبندول 3D
        /// 
        /// الهدف: جعل الدلو يتأرجح بشكل طبيعي مع الحبل
        /// - الموقع: يتبع نهاية الحبل
        /// - الدوران: يتبع اتجاه الحبل (up = -ropeDir)
        /// - التنعيم: Quaternion.Slerp لمنع القفزات المفاجئة
        /// </summary>
        /// <param name="ropeEnd">موقع نهاية الحبل (آخر جسيم)</param>
        /// <param name="ropeDir">اتجاه الحبل (من الجسيم قبل الأخير إلى الأخير)</param>
        public void SetPose(Vector3 ropeEnd, Vector3 ropeDir)
        {
            // 1. تحديث الموقع (نفس SetPosition)
            transform.position = ropeEnd - transform.TransformVector(_localHandleOffset);

            // 2. حساب الدوران إذا كان مفعلاً
            if (!_enablePendulumRotation) return;
            if (ropeDir.sqrMagnitude < 0.001f) return;

            // الاتجاه المستهدف للأعلى (عكس اتجاه الحبل)
            Vector3 targetUp = -ropeDir.normalized;

            // حساب الدوران المستهدف
            // نستخدم Vector3.ProjectOnPlane للحفاظ على اتجاه forward قدر الإمكان
            Vector3 currentForward = transform.forward;
            Vector3 targetForward = Vector3.ProjectOnPlane(currentForward, targetUp);
            
            // إذا كان forward صغيراً جداً (الحبل عمودي تماماً)، نستخدم forward افتراضي
            if (targetForward.sqrMagnitude < 0.01f)
            {
                targetForward = Vector3.ProjectOnPlane(Vector3.forward, targetUp);
            }
            
            if (targetForward.sqrMagnitude < 0.01f)
            {
                targetForward = Vector3.ProjectOnPlane(Vector3.right, targetUp);
            }

            targetForward = targetForward.normalized;

            // إنشاء الدوران المستهدف
            Quaternion targetRotation = Quaternion.LookRotation(targetForward, targetUp);

            // 3. تنعيم الدوران (Pendulum-like smoothing)
            float smoothFactor = _rotationSmoothing * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Mathf.Clamp01(smoothFactor)
            );
        }

        /// <summary>
        /// ✅ جديد: نسخة مبسطة من SetPose تستخدم فقط الموقع
        /// تحسب اتجاه الحبل تلقائياً من الموقع السابق
        /// </summary>
        private Vector3 _lastRopeEnd;
        private bool _hasLastPosition = false;

        public void SetPoseAuto(Vector3 ropeEnd)
        {
            if (_hasLastPosition)
            {
                Vector3 ropeDir = (ropeEnd - _lastRopeEnd).normalized;
                SetPose(ropeEnd, ropeDir);
            }
            else
            {
                SetPosition(ropeEnd);
            }

            _lastRopeEnd = ropeEnd;
            _hasLastPosition = true;
        }

        /// <summary>
        /// إعادة بناء الـ mesh ديناميكياً (للتغييرات في runtime)
        /// </summary>
        public void RebuildMesh()
        {
            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = BucketMeshBuilder.BuildWithHoles(
                _hasDrainHoles,
                _drainHoleCount,
                _hasSideHoles,
                _sideHoleCount
            );
            Debug.Log("[BucketController] Mesh rebuilt with new configuration.");
        }

        /// <summary>
        /// رسم نقطة التعلق في Scene View عند التحديد
        /// </summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(AttachPoint, 0.04f);
            
            // رسم اتجاه المقبض
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, AttachPoint);
        }

        // ─── Public Properties ────────────────────────────────────────────

        public Vector3 LocalHandleOffset => _localHandleOffset;
        public bool HasDrainHoles => _hasDrainHoles;
        public bool HasSideHoles => _hasSideHoles;
    }
}