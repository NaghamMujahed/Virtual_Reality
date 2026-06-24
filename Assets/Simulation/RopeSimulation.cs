using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// محرك محاكاة الحبل باستخدام Cosserat Rod + XPBD على GPU
    /// يدعم:
    /// - Dynamic Grab Anchor (الإمساك الديناميكي من أي نقطة)
    /// - Two-Way Coupling مع الدلو
    /// - Separate Twist Constraint
    /// - Spatial Hashing للتصادم الذاتي
    /// - SDF Collision مع الدلو
    /// - LRA Constraints ديناميكية
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RopeSimulation : MonoBehaviour
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
        [SerializeField] float   _bucketMass  = 2.5f;
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
        Vector3[] _endPos   = new Vector3[1];
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
        // ✅ جديد: مؤشر الجسيم الممسوك حالياً (-1 يعني لا يوجد إمساك)
        // يُستخدم في LRA Kernel كمرساة ديناميكية
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
                // ✅ التحقق من وجود BucketPhysics
                var bucketPhysics = _bucket.GetComponent<BucketPhysics>();
                
                if (_useBucketPhysics && bucketPhysics != null)
                {
                    // ✅ استخدام BucketPhysics للبندول الحقيقي
                    bucketPhysics.SetAttachmentPoint(_endPos[0]);
                    bucketPhysics.SimulateStep(Time.deltaTime, _gravity);
                }
                else
                {
                    // ✅ Fallback للوضع القديم
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
        //  SIMULATION STEP
        // ════════════════════════════════════════════════════════════════════

        void SimulationStep()
        {
            float dampingRate = _velocityDamping > 0f
                ? -Mathf.Log(_velocityDamping) / _simDt : 0f;
            float angularDamping = 0.5f;
            
            // ── Set dynamic parameters ──
            _shader.SetFloat("_StretchK",   _stretchK);
            _shader.SetFloat("_BucketMass", _bucketMass);
            _shader.SetFloat("_BendTwistK", _bendTwistK);
            _shader.SetFloat("_Damping",    dampingRate);
            _shader.SetFloat("_AngularDamping", angularDamping); 
            _shader.SetFloat("_StretchCompliance", _stretchCompliance);
            _shader.SetFloat("_BendCompliance",    _bendCompliance);
            _shader.SetVector("_BendStiffness", _bendStiffness);
            _shader.SetFloat("_SelfCollisionRadius", _selfCollisionRadius);
            _shader.SetFloat("_LRASoftness", _lraSoftness);
            _shader.SetVector("_Gravity", new Vector4(_gravity.x, _gravity.y, _gravity.z, 0f));
            
            // ✅ تمرير Twist Parameters
            _shader.SetFloat("_TwistStiffness", _twistStiffness);
            _shader.SetFloat("_TwistCompliance", _twistCompliance);
            
            // ✅ تمرير Grab Index للـ LRA الديناميكي
            _shader.SetInt("_GrabIndex", _grabIndex);
            
            // Spatial Hashing parameters
            _shader.SetFloat("_CellSize", _cellSize);
            _shader.SetInt("_GridMaxParticles", _gridMaxParticles);
            _shader.SetInt("_GridSize", _gridSize);
            
            // SDF parameters
            if (_bucketSDF != null)
            {
                _shader.SetTexture(_kSolveBucketCollision, "_BucketSDF", _bucketSDF);
                _shader.SetVector("_SDFGridMin", _sdfGridMin);
                _shader.SetFloat("_SDFCellSize", _sdfCellSize);
                _shader.SetFloat("_RopeRadius", _ropeRadius);
                _shader.SetVector("_GridResolution", new Vector4(_gridResolution.x, _gridResolution.y, _gridResolution.z, 0f));
            }

            int pGroups = Groups(_numParticles);
            int qGroups = Groups(_segments);
            int sGroups = Groups(_segments / 2 + 1);
            int bGroups = Groups((_segments - 1) / 2 + 1);
            int scGroups = Groups(_numParticles);
            int lraGroups = Groups(_numParticles);
            int gridGroups = Groups(_gridSize);
            int twistGroups = bGroups; // نفس حجم Bend

            _predBuf.SetData(_pinTop, 0, 0, 1);

            // 0) Clear lambdas and spatial hash
            _shader.Dispatch(_kClearLambdas, Mathf.Max(qGroups, pGroups), 1, 1);
            _shader.Dispatch(_kClearSpatialHash, gridGroups, 1, 1);

            // 1) Apply explicit forces (gravity + bucket mass)
            _shader.Dispatch(_kApplyForces, pGroups, 1, 1);
            
            // 2) Predict positions and orientations
            _shader.Dispatch(_kPredict, pGroups, 1, 1);

            // 3) Constraint solving with Bilateral Interleaving
            for (int i = 0; i < _solverIter; i++)
            {
                // Forward pass
                _shader.SetInt("_Offset", 0);
                _shader.Dispatch(_kSolveStretch, sGroups, 1, 1);
                _shader.Dispatch(_kSolveBend, bGroups, 1, 1);
                
                // Backward pass
                _shader.SetInt("_Offset", 1);
                _shader.Dispatch(_kSolveStretch, sGroups, 1, 1);
                _shader.Dispatch(_kSolveBend, bGroups, 1, 1);

                // Build spatial hash before self-collision
                _shader.Dispatch(_kBuildSpatialHash, pGroups, 1, 1);
                
                // ✅ Twist Constraint منفصل
                _shader.Dispatch(_kSolveTwist, twistGroups, 1, 1);
                
                // Self-collision (every 2 iterations)
                if (i % 2 == 0)
                    _shader.Dispatch(_kSolveSelfCollision, scGroups, 1, 1);
                
                // LRA constraints (every 4 iterations)
                if (i % 4 == 0)
                    _shader.Dispatch(_kSolveLRA, lraGroups, 1, 1);

                // SDF collision with bucket (every iteration if SDF exists)
                if (_bucketSDF != null)
                    _shader.Dispatch(_kSolveBucketCollision, pGroups, 1, 1);

                _shader.Dispatch(_kNormQ, qGroups, 1, 1);
            }

            // 4) Re-pin top anchor, compute velocities, apply damping
            _predBuf.SetData(_pinTop, 0, 0, 1);
            _shader.Dispatch(_kUpdateVel, pGroups, 1, 1);
            _shader.Dispatch(_kApplyDamping, pGroups, 1, 1);
        }

        // ════════════════════════════════════════════════════════════════════
        //  BUFFER MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════

        void AllocBuffers()
        {
            int N  = _numParticles;
            int NQ = _segments;
            
            _posBuf     = new ComputeBuffer(N,  12);
            _predBuf    = new ComputeBuffer(N,  12);
            _velBuf     = new ComputeBuffer(N,  12);
            _invMBuf    = new ComputeBuffer(N,  4);
            _qBuf       = new ComputeBuffer(NQ, 16);
            _qPredBuf   = new ComputeBuffer(NQ, 16);
            _angVelBuf  = new ComputeBuffer(NQ, 12);
            _restQBuf   = new ComputeBuffer(NQ, 16);
            _qInvWBuf   = new ComputeBuffer(NQ, 4);
            _lambdaSBuf = new ComputeBuffer(NQ, 12);
            _lambdaBBuf = new ComputeBuffer(NQ, 12);
            _lambdaSCBuf = new ComputeBuffer(NQ, 12);
            _lraWeightsBuf = new ComputeBuffer(N, 4);
            
            // Spatial Hashing Buffers
            _gridCellCountsBuf = new ComputeBuffer(_gridSize, 4);
            _gridParticleIndicesBuf = new ComputeBuffer(_gridSize * _gridMaxParticles, 4);
        }

        void InitState()
        {
            int N  = _numParticles;
            int NQ = _segments;

            Vector3[] pos   = new Vector3[N];
            Vector3[] vel   = new Vector3[N];
            float[]   invM  = new float[N];
            Vector4[] quats = new Vector4[NQ];
            Vector3[] angV  = new Vector3[NQ];
            Vector4[] rest  = new Vector4[NQ];
            float[]   qInvW = new float[NQ];
            var       lambS = new Vector3[NQ];
            var       lambB = new Vector3[NQ];
            var       lambSC = new Vector3[NQ];
            float[]   lraW = new float[N];

            Vector3 origin = _topAnchor != null ? _topAnchor.position : transform.position;

            float segMass    = _ropeDensity * _segLen;
            float invSegMass = segMass > 0f ? 1f / segMass : 0f;

            for (int i = 0; i < N; i++)
            {
                pos[i] = origin + Vector3.down * (i * _segLen);
                vel[i] = Vector3.zero;
                invM[i] = i == 0 ? 0f : i == N - 1
                    ? (_bucketMass > 0f ? 1f / (_bucketMass + segMass) : invSegMass)
                    : invSegMass;
            }

            InitializeQuaternionsParallelTransport(quats, rest, pos, NQ, origin);

            float segRadius  = 0.01f;
            float inertia    = 0.5f * segMass * segRadius * segRadius;

            for (int j = 0; j < NQ; j++)
            {
                angV[j]  = Vector3.zero;
                qInvW[j] = inertia > 0f ? 1f / inertia : 1f;
            }
            qInvW[0] = 0f;

            _posBuf.SetData(pos);
            _predBuf.SetData(pos);
            _velBuf.SetData(vel);
            _invMBuf.SetData(invM);
            _qBuf.SetData(quats);
            _qPredBuf.SetData(quats);
            _angVelBuf.SetData(angV);
            _restQBuf.SetData(rest);
            _qInvWBuf.SetData(qInvW);
            _lambdaSBuf.SetData(lambS);
            _lambdaBBuf.SetData(lambB);
            _lambdaSCBuf.SetData(lambSC);
            _lraWeightsBuf.SetData(lraW);
        }

        void InitializeQuaternionsParallelTransport(Vector4[] quats, Vector4[] rest, Vector3[] positions, int NQ, Vector3 origin)
        {
            float sq2 = Mathf.Sqrt(2f) * 0.5f;
            Vector4 prevQ = new Vector4(sq2, 0f, 0f, sq2);
            
            for (int j = 0; j < NQ; j++)
            {
                if (j > 0)
                {
                    Vector3 prevTangent = (positions[j] - positions[j-1]).normalized;
                    Vector3 currTangent = (positions[j+1] - positions[j]).normalized;
                    
                    float dot = Vector3.Dot(prevTangent, currTangent);
                    if (dot < 0.999f)
                    {
                        Vector3 axis = Vector3.Cross(prevTangent, currTangent).normalized;
                        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                        
                        float halfAngle = angle * 0.5f;
                        Vector4 rotQ = new Vector4(
                            axis.x * Mathf.Sin(halfAngle),
                            axis.y * Mathf.Sin(halfAngle),
                            axis.z * Mathf.Sin(halfAngle),
                            Mathf.Cos(halfAngle)
                        );
                        
                        prevQ = QMul(rotQ, prevQ);
                    }
                }
                
                quats[j] = prevQ;
                rest[j]  = prevQ;
            }
        }

        Vector4 QMul(Vector4 a, Vector4 b)
        {
            return new Vector4(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
            );
        }

        // ═══════════════════════════════════════════════════════════════════
        //  KERNEL & BUFFER BINDING
        // ════════════════════════════════════════════════════════════════════

        void FindKernels()
        {
            _kApplyForces  = _shader.FindKernel("ApplyForces");
            _kPredict      = _shader.FindKernel("Predict");
            _kSolveStretch = _shader.FindKernel("SolveStretch");
            _kSolveBend    = _shader.FindKernel("SolveBendTwist");
            _kSolveSelfCollision = _shader.FindKernel("SolveSelfCollision");
            _kSolveLRA     = _shader.FindKernel("SolveLRA");
            _kNormQ        = _shader.FindKernel("NormalizeQuats");
            _kUpdateVel    = _shader.FindKernel("UpdateVelocities");
            _kApplyDamping = _shader.FindKernel("ApplyDamping");
            _kClearLambdas = _shader.FindKernel("ClearLambdas");
            
            // New kernels
            _kBuildSpatialHash = _shader.FindKernel("BuildSpatialHash");
            _kClearSpatialHash = _shader.FindKernel("ClearSpatialHash");
            _kSolveBucketCollision = _shader.FindKernel("SolveBucketCollision");
            _kSolveTwist = _shader.FindKernel("SolveTwist");  // ✅ جديد
        }

        void BindAllBuffers()
        {
            // ✅ جميع kernels بما فيها الجديدة
            int[] kernels = { 
                _kApplyForces, _kPredict, _kSolveStretch, _kSolveBend, 
                _kSolveSelfCollision, _kSolveLRA, _kNormQ, _kUpdateVel, 
                _kApplyDamping, _kClearLambdas,
                _kBuildSpatialHash,
                _kClearSpatialHash,
                _kSolveBucketCollision,
                _kSolveTwist  // ✅ جديد: مهم جداً لربط _QuatPreds
            };
            
            (string name, ComputeBuffer buf)[] bindings =
            {
                ("_Positions",     _posBuf),
                ("_Predictions",   _predBuf),
                ("_Velocities",    _velBuf),
                ("_InvMasses",     _invMBuf),
                ("_Quats",         _qBuf),
                ("_QuatPreds",     _qPredBuf),
                ("_AngVelocities", _angVelBuf),
                ("_RestQuats",     _restQBuf),
                ("_QuatInvW",      _qInvWBuf),
                ("_LambdaS",       _lambdaSBuf),
                ("_LambdaB",       _lambdaBBuf),
                ("_LambdaSC",      _lambdaSCBuf),
                ("_LRAWeights",    _lraWeightsBuf),
            };
            
            foreach (var (name, buf) in bindings)
                foreach (int k in kernels)
                    _shader.SetBuffer(k, name, buf);
            
            // ربط Spatial Hashing buffers بالكernels المحددة
            _shader.SetBuffer(_kBuildSpatialHash, "_GridCellCounts", _gridCellCountsBuf);
            _shader.SetBuffer(_kBuildSpatialHash, "_GridParticleIndices", _gridParticleIndicesBuf);
            _shader.SetBuffer(_kClearSpatialHash, "_GridCellCounts", _gridCellCountsBuf);
            _shader.SetBuffer(_kSolveSelfCollision, "_GridCellCounts", _gridCellCountsBuf);
            _shader.SetBuffer(_kSolveSelfCollision, "_GridParticleIndices", _gridParticleIndicesBuf);
        }
            
        void SetConstantParams()
        {
            _shader.SetFloat("_Dt",       _simDt);
            _shader.SetFloat("_SegLen",   _segLen);
            _shader.SetInt("_NumParticles", _numParticles);
            _shader.SetInt("_NumQuats",     _segments);
            _shader.SetVector("_Gravity",   _gravity);
            _shader.SetFloat("_StretchK",   _stretchK);
            _shader.SetFloat("_BendTwistK", _bendTwistK);
            _shader.SetVector("_BendStiffness", _bendStiffness);
            _shader.SetFloat("_SelfCollisionRadius", _selfCollisionRadius);
            _shader.SetFloat("_LRASoftness", _lraSoftness);
            float initRate = _velocityDamping > 0f ? -Mathf.Log(_velocityDamping) / _simDt : 0f;
            _shader.SetFloat("_Damping",    initRate);
            _shader.SetFloat("_StretchCompliance", _stretchCompliance);
            _shader.SetFloat("_BendCompliance",    _bendCompliance);
            
            // Spatial hashing constants
            _shader.SetFloat("_CellSize", _cellSize);
            _shader.SetInt("_GridMaxParticles", _gridMaxParticles);
            _shader.SetInt("_GridSize", _gridSize);
        }

        void ReleaseBuffers()
        {
            _posBuf?.Release();    _predBuf?.Release();    _velBuf?.Release();
            _invMBuf?.Release();   _qBuf?.Release();       _qPredBuf?.Release();
            _angVelBuf?.Release(); _restQBuf?.Release();   _qInvWBuf?.Release();
            _lambdaSBuf?.Release(); _lambdaBBuf?.Release();
            _lambdaSCBuf?.Release(); _lraWeightsBuf?.Release();
            _gridCellCountsBuf?.Release();
            _gridParticleIndicesBuf?.Release();
        }

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
        /// ✅ يحدث _grabIndex للـ LRA الديناميكي
        /// </summary>
        public void BeginGrab(int particleIdx, Vector3 startPos)
        {
            if (_grabIdx >= 0) EndGrab();
            
            _grabIdx    = particleIdx;
            _grabTarget = startPos;
            _grabIndex  = particleIdx;  // ✅ جديد: تحديث grabIndex للـ LRA

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
        /// ✅ يعيد _grabIndex إلى -1
        /// </summary>
        public void EndGrab()
        {
            if (_grabIdx < 0) return;
            
            _invMWriteArr[0] = _grabSavedInvM;
            _invMBuf.SetData(_invMWriteArr, 0, _grabIdx, 1);
            
            _grabIdx = -1;
            _grabIndex = -1;  // ✅ جديد: إعادة تعيين grabIndex
            
            SimLog(LogLevel.Verbose, "Released grab");
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
        public int     GrabIndex         => _grabIndex;  // ✅ جديد: getter للـ grabIndex
        
        public void SetBucketMass(float mass)
        {
            _bucketMass = Mathf.Max(0.1f, mass);
            if (_invMBuf == null) return;
            float segMass = _ropeDensity * _segLen;
            float[] w = { _bucketMass > 0f ? 1f / (_bucketMass + segMass) : 1f / Mathf.Max(segMass, 1e-6f) };
            _invMBuf.SetData(w, 0, _numParticles - 1, 1);
        }

        static int Groups(int count) => Mathf.Max(1, (count + 63) / 64);

        enum LogLevel { None, Error, Warn, Verbose }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void SimLog(LogLevel lvl, string msg)
        {
            if (lvl > _logLevel) return;
            switch (lvl)
            {
                case LogLevel.Error:   Debug.LogError($"[Rope] {msg}");   break;
                case LogLevel.Warn:    Debug.LogWarning($"[Rope] {msg}"); break;
                default:               Debug.Log($"[Rope] {msg}");        break;
            }
        }
    }
}