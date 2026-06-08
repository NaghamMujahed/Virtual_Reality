using UnityEngine;

namespace PaintBucketSim.Runtime
{
    /// <summary>
    /// Runtime environment values used by all simulation systems.
    /// This is produced by EnvironmentSystem from EnvironmentConfig.
    /// </summary>
    public struct EnvironmentState
    {
        public Vector3 gravity;
        public Vector3 windVelocity;

        public float airDensity;
        public float airViscosity;

        public float temperatureCelsius;
        public float relativeHumidity01;
        public float pressurePa;
    }
}