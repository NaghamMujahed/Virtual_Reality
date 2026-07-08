using UnityEngine;

namespace PaintBucketSim.Runtime
{
    /// <summary>
    /// Lightweight diagnostics for the current simulation frame.
    /// Later we will extend this with mass error, density error, rope tension, etc.
    /// </summary>
    public struct DiagnosticsFrame
    {
        public bool initialized;
        public bool paused;

        public int unityFrame;
        public long fixedStepIndex;
        public long substepIndex;

        public double simulationTime;
        public float fixedDeltaTime;
        public float substepDeltaTime;
        public int substeps;

        public float fps;
        public Vector3 gravity;
        public Vector3 windVelocity;
    }
}