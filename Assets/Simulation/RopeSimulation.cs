using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// محرك محاكاة الحبل باستخدام Cosserat Rod + XPBD على GPU
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class RopeSimulation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ComputeShader    _shader;
        [SerializeField] Transform        _topAnchor;
        [SerializeField] BucketController _bucket;
        [SerializeField] RopeRenderer     _renderer;

        [Header("Rope")]
        [SerializeField, Range(4, 200)]    int   _segments      = 40;
        [SerializeField, Min(0.1f)]        float _totalLength   = 1f;
        [SerializeField, Range(0.01f, 1f)] float  _stretchK      = 1.0f;
        [SerializeField, Range(0f, 1f)]    float _bendTwistK    = 0.9f;
        [SerializeField, Range(1, 300)]    int   _solverIter    = 100;

        [Header("Physics")]
        [SerializeField] float   _simDt       = 0.01f;
        [SerializeField] float   _ropeDensity = 0.5f;
        [SerializeField] float   _bucketMass  = 10.0f;
        [SerializeField, Range(0.9f, 1f)] float _velocityDamping   = 0.95f;
        [SerializeField] float   _stretchCompliance = 1e-9f;
        [SerializeField] float   _bendCompliance    = 1e-9f;
        
        [SerializeField] Vector3 _bendStiffness = new Vector3(1.0f, 1.0f, 1.5f);
        
        [SerializeField, Min(0.01f)] float _selfCollisionRadius = 0.08f;
        
        [SerializeField, Range(0f, 1f)] float _lraSoftness = 0.1f;
        
        [SerializeField] Vector3 _gravity     = new Vector3(0, -9.81f, 0);

        [Header("Bucket Physics")]
        [SerializeField] bool _useBucketPhysics = true; 

        [Header("Spatial Hashing")]
        [SerializeField] float _cellSize = 0.1f;
        [SerializeField] int   _gridMaxParticles = 8;
        [SerializeField] int   _gridSize = 1000;

        [Header("SDF Collision")]
        [SerializeField] Texture3D _bucketSDF;
        [SerializeField] Vector3 _sdfGridMin = new Vector3(-2f, -2f, -2f);
        [SerializeField] float _sdfCellSize = 0.05f;
        [SerializeField] float _ropeRadius = 0.02f;
        [SerializeField] Vector3Int _gridResolution = new Vector3Int(64, 64, 64);

        [Header("Twist Constraint")]
        [SerializeField] float _twistStiffness = 3.0f;
        [SerializeField] float _twistCompliance = 1e-11f;

        [Header("Debug")]
        [SerializeField] LogLevel _logLevel = LogLevel.Error;

        // ─── Compute Buffers ─────────────────────────────────────────────
        ComputeBuffer _posBuf, _predBuf, _velBuf, _invMBuf;
        ComputeBuffer _qBuf, _qPredBuf, _angVelBuf, _restQBuf, _qInvWBuf;
        ComputeBuffer _lambdaSBuf, _lambdaBBuf, _lambdaSCBuf, _lraWeightsBuf;
        
        // Spatial Hashing Buffers
        ComputeBuffer _gridCellCountsBuf, _gridParticleIndicesBuf;

        // ─── Kernel IDs ───────────────────────────────────────────────────
        int _kApplyForces, _kPredict, _kSolveStretch, _kSolveBend, 
            _kSolveSelfCollision, _kSolveLRA, _kNormQ, _kUpdateVel, 
            _kApplyDamping, _kClearLambdas, _kBuildSpatialHash, 
            _kClearSpatialHash, _kSolveBucketCollision, _kSolveTwist;

        // ─── State ────────────────────────────────────────────────────────
        Vector3[] _pinTop    = new Vector3[1];
        Vector3[] _endPos    = new Vector3[1];
        float     _segLen;
        int       _numParticles;
        int       _lastSubstepCount;
        float     _accumulator;

        int       _grabIdx       = -1;
        float     _grabSavedInvM;
        Vector3   _grabTarget;
        Vector3[] _grabPosArr    = new Vector3[1];
        float[]   _invMReadArr   = new float[1];
        float[]   _invMWriteArr  = new float[1];
        Vector3[] _pickBuffer;

        // ─── Dynamic Grab Index ───────────────────────────────────────────
        int _grabIndex = -1;

        // ════════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        void Start()
        {
            _segLen       = _totalLength / _segments;
            _numParticles = _segments + 1;
            Time.fixedDeltaTime = _simDt;

            AllocBuffers();
            InitState();
            FindKernels();
            BindAllBuffers();
            SetConstantParams();

            SimLog(LogLevel.Verbose, $"Rope init: {_segments} segs, segLen={_segLen:F3}m, iter={_solverIter}");
        }

        void FixedUpdate()
        {
            if (_shader == null) return;

            _pinTop[0] = _topAnchor != null ? _topAnchor.position : transform.position;

            if (_grabIdx >= 0)
            {
                _grabPosArr[0] = _grabTarget;
                _predBuf.SetData(_grabPosArr, 0, _grabIdx, 1);
            }

            // ✅ فحص الانقطاع قبل كل خطوة محاكاة
            CheckForBreak();

            _accumulator += Time.fixedDeltaTime;
            int steps = 0;
            while (_accumulator >= _simDt && steps < 8)
            {
                _accumulator -= _simDt;
                SimulationStep();
                steps++;
            }
            _lastSubstepCount = steps;

            _posBuf.SetData(_pinTop, 0, 0, 1);
        }

        void Update()
        {
            if (_shader == null) return;

            _posBuf.GetData(_endPos, 0, _segments, 1);
            
            if (_bucket != null)
            {
                var bucketPhysics = _bucket.GetComponent<BucketPhysics>();
                
                if (_useBucketPhysics && bucketPhysics != null)
                {
                    bucketPhysics.SetAttachmentPoint(_endPos[0]);
                    bucketPhysics.SimulateStep(Time.deltaTime, _gravity);
                }
                else
                {
                    Vector3 ropeDir = Vector3.zero;
                    if (_segments >= 2)
                    {
                        Vector3[] positions = SnapshotPositions();
                        ropeDir = (positions[_segments] - positions[_segments - 1]).normalized;
                    }
                    _bucket.SetPose(_endPos[0], ropeDir);
                }
            }
            
            if (_renderer != null)
                _renderer.Refresh(_posBuf, _numParticles);
        }

        void OnDestroy() => ReleaseBuffers();

        // ════════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        public Vector3[] SnapshotPositions()
        {
            if (_posBuf == null) return System.Array.Empty<Vector3>();

            if (_pickBuffer == null || _pickBuffer.Length != _numParticles)
                _pickBuffer = new Vector3[_numParticles];

            _posBuf.GetData(_pickBuffer);

            return _pickBuffer;
        }

        /// <summary>
        /// بدء الإمساك بجسيم معين
        /// </summary>
        public void BeginGrab(int particleIdx, Vector3 startPos)
        {
            if (_grabIdx >= 0) EndGrab();
            
            _grabIdx    = particleIdx;
            _grabTarget = startPos;
            _grabIndex  = particleIdx;

            _invMBuf.GetData(_invMReadArr, 0, particleIdx, 1);
            _grabSavedInvM = _invMReadArr[0];
            _invMWriteArr[0] = 0f;
            _invMBuf.SetData(_invMWriteArr, 0, particleIdx, 1);

            _predBuf.GetData(_grabPosArr, 0, particleIdx, 1);
            _grabTarget = _grabPosArr[0];

            _velBuf.GetData(_grabPosArr, 0, particleIdx, 1);
            _grabPosArr[0] = Vector3.zero;
            _velBuf.SetData(_grabPosArr, 0, particleIdx, 1);
            
            SimLog(LogLevel.Verbose, $"Grabbed particle {particleIdx}");
        }

        public void MoveGrab(Vector3 worldPos) { if (_grabIdx >= 0) _grabTarget = worldPos; }

        /// <summary>
        /// إنهاء الإمساك
        /// </summary>
        public void EndGrab()
        {
            if (_grabIdx < 0) return;
            
            _invMWriteArr[0] = _grabSavedInvM;
            _invMBuf.SetData(_invMWriteArr, 0, _grabIdx, 1);
            
            _grabIdx = -1;
            _grabIndex = -1;
            
            SimLog(LogLevel.Verbose, "Released grab");
        }

        // ════════════════════════════════════════════════════════════════════
        //  ROPE BREAKING - انقطاع الحبل
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// فحص ما إذا كان يجب أن ينقطع الحبل بناءً على وزن الدلو
        /// </summary>
        void CheckForBreak()
        {
            if (!_enableBreaking || _isBroken) return;
            
            if (_bucketMass >= _maxBreakWeight)
            {
                BreakRope(_breakParticleIndex);
            }
        }

        /// <summary>
        /// كسر الحبل عند جسيم محدد
        /// </summary>
        public void BreakRope(int breakIdx)
        {
            if (_isBroken) return;
            if (breakIdx <= 0 || breakIdx >= _numParticles - 1)
            {
                SimLog(LogLevel.Warn, $"Invalid break index: {breakIdx}. Must be between 1 and {_numParticles - 2}");
                return;
            }
            
            _isBroken = true;
            _currentBreakIndex = breakIdx;
            
            _shader.SetInt("_BreakIndex", _currentBreakIndex);
            
            if (_breakImpulse > 0f)
            {
                Vector3[] velocities = new Vector3[_numParticles];
                _velBuf.GetData(velocities);
                
                for (int i = Mathf.Max(0, breakIdx - 2); i <= Mathf.Min(_numParticles - 1, breakIdx + 2); i++)
                {
                    Vector3 randomImpulse = new Vector3(
                        Random.Range(-1f, 1f),
                        Random.Range(-0.5f, 0.5f),
                        Random.Range(-1f, 1f)
                    ).normalized * _breakImpulse;
                    
                    velocities[i] += randomImpulse;
                }
                
                _velBuf.SetData(velocities);
            }
            
            SimLog(LogLevel.Warn, $"Rope broken at particle {breakIdx}! Bucket mass: {_bucketMass:F2}kg");
        }

        /// <summary>
        /// إعادة الحبل إلى حالته الطبيعية
        /// </summary>
        public void RepairRope()
        {
            if (!_isBroken) return;
            
            _isBroken = false;
            _currentBreakIndex = -1;
            _shader.SetInt("_BreakIndex", -1);
            
            SimLog(LogLevel.Verbose, "Rope repaired");
        }

        /// <summary>
        /// تعيين وزن الدلو ديناميكياً مع فحص الانقطاع
        /// </summary>
        public void SetBucketMassWithBreakCheck(float mass)
        {
            SetBucketMass(mass);
            CheckForBreak();
        }

        // ─── Properties ───────────────────────────────────────────────────

        public float   StretchK           { get => _stretchK;   set => _stretchK   = Mathf.Clamp(value, 0.01f, 1f); }
        public float   BendTwistK        { get => _bendTwistK; set => _bendTwistK = Mathf.Clamp01(value); }
        public int     SolverIter        { get => _solverIter; set => _solverIter  = Mathf.Clamp(value, 1, 300); }
        public float   BucketMass        => _bucketMass;
        public float   VelocityDamping   { get => _velocityDamping; set => _velocityDamping = Mathf.Clamp(value, 0.9f, 1f); }
        public float   StretchCompliance { get => _stretchCompliance; set => _stretchCompliance = Mathf.Max(0f, value); }
        public float   BendCompliance    { get => _bendCompliance; set => _bendCompliance = Mathf.Max(0f, value); }
        public Vector3 Gravity           { get => _gravity; set => _gravity = value; }
        public int     LastSubstepCount  => _lastSubstepCount;
        public int     Segments          => _segments;
        public float   TotalLength       => _totalLength;
        public float   SegLen            => _segLen;
        public int     GrabIndex         => _grabIndex;
        
        public float BucketMassWithBreakCheck 
        { 
            get => _bucketMass; 
            set 
            {
                SetBucketMass(value);
                CheckForBreak();
            }
        }
        
        public void SetBucketMass(float mass)
        {
            _bucketMass = Mathf.Max(0.1f, mass);
            if (_invMBuf == null) return;
            float segMass = _ropeDensity * _segLen;
            float[] w = { _bucketMass > 0f ? 1f / (_bucketMass + segMass) : 1f / Mathf.Max(segMass, 1e-6f) };
            _invMBuf.SetData(w, 0, _numParticles - 1, 1);
        }


        enum LogLevel { None, Error, Warn, Verbose }

    }
}