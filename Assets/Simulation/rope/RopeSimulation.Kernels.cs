using UnityEngine;

namespace Simulation
{
    public sealed partial class RopeSimulation
    {
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
    }
}