using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// يتحكم بالدلو ويربطه بنهاية الحبل
    /// يُدار حركياً بواسطة RopeSimulation
    /// ExecuteAlways يسمح بالعمل في Editor Mode (قبل Play)
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
            // استخدام نفس الدالة في كل الأماكن
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

        /// <summary>
        /// نقطة التعلق بالدلو (في الفضاء العالمي)
        /// </summary>
        public Vector3 AttachPoint => transform.TransformPoint(_localHandleOffset);

        /// <summary>
        /// يُستدعى بواسطة RopeSimulation مع موقع آخر جسيم في الحبل
        /// يحرك الدلو بحيث تتطابق نقطة التعلق مع نهاية الحبل
        /// </summary>
        public void SetPosition(Vector3 ropeEnd)
        {
            transform.position = ropeEnd - transform.TransformVector(_localHandleOffset);
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
        }
    }
}