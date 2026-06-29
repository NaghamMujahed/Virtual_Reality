using UnityEngine;

namespace Simulation
{
    public sealed partial class RopeSimulation
    {
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
    }
}