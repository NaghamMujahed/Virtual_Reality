using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// مدير الثقوب في الدلو - يسمح بإضافة/إزالة الثقوب ديناميكياً
    /// </summary>
    [RequireComponent(typeof(BucketController))]
    public class BucketHoleManager : MonoBehaviour
    {
        [Header("Hole Configuration")]
        [SerializeField] bool _enableDrainHoles = false;
        [SerializeField] int _drainHoleCount = 1;
        [SerializeField] bool _enableSideHoles = false;
        [SerializeField] int _sideHoleCount = 0;

        private BucketController _bucket;

        void Awake()
        {
            _bucket = GetComponent<BucketController>();
        }

        /// <summary>
        /// إضافة ثقب تصريف في القاع
        /// </summary>
        public void AddDrainHole()
        {
            _enableDrainHoles = true;
            _drainHoleCount++;
            RebuildBucket();
            Debug.Log($"[BucketHoleManager] Drain hole added. Total: {_drainHoleCount}");
        }

        /// <summary>
        /// إضافة ثقب جانبي
        /// </summary>
        public void AddSideHole()
        {
            _enableSideHoles = true;
            _sideHoleCount++;
            RebuildBucket();
            Debug.Log($"[BucketHoleManager] Side hole added. Total: {_sideHoleCount}");
        }

        /// <summary>
        /// إزالة جميع الثقوب
        /// </summary>
        public void RemoveAllHoles()
        {
            _enableDrainHoles = false;
            _enableSideHoles = false;
            _drainHoleCount = 0;
            _sideHoleCount = 0;
            RebuildBucket();
            Debug.Log("[BucketHoleManager] All holes removed.");
        }

        /// <summary>
        /// إعادة بناء الدلو بالثقوب الجديدة
        /// </summary>
        void RebuildBucket()
        {
            if (_bucket != null)
            {
                _bucket.RebuildMesh();
            }
        }

        /// <summary>
        /// التحقق من وجود ثقوب
        /// </summary>
        public bool HasHoles()
        {
            return _enableDrainHoles || _enableSideHoles;
        }

        /// <summary>
        /// الحصول على عدد الثقوب الكلي
        /// </summary>
        public int GetTotalHoleCount()
        {
            return (_enableDrainHoles ? _drainHoleCount : 0) + 
                   (_enableSideHoles ? _sideHoleCount : 0);
        }
    }
}