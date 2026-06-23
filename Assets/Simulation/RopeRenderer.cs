using UnityEngine;

namespace Simulation
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class RopeRenderer : MonoBehaviour
    {
        LineRenderer _lr;
        Vector3[]    _posCache;

        void Awake() => _lr = GetComponent<LineRenderer>();

        // Called by RopeSimulation each frame after GPU step completes.
        public void Refresh(ComputeBuffer posBuffer, int count)
        {
            if (_posCache == null || _posCache.Length != count)
                _posCache = new Vector3[count];

            posBuffer.GetData(_posCache);
            _lr.positionCount = count;
            _lr.SetPositions(_posCache);
        }
    }
}
