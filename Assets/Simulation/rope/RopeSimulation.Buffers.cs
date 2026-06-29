using UnityEngine;

namespace Simulation
{
    public sealed partial class RopeSimulation
    {
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
    }
}