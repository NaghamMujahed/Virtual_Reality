namespace PaintBucketSim.Core
{
    /// <summary>
    /// Central unit contract for the whole simulation.
    /// Physics convention:
    /// 1 Unity Unit = 1 meter
    /// mass = kilogram
    /// time = second
    /// </summary>
    public static class Units
    {
        public const float UnityUnitsPerMeter = 1.0f;
        public const float MetersPerUnityUnit = 1.0f;

        public const float Epsilon = 1e-6f;

        public static float MetersToUnity(float meters)
        {
            return meters * UnityUnitsPerMeter;
        }

        public static float UnityToMeters(float unityUnits)
        {
            return unityUnits * MetersPerUnityUnit;
        }
    }
}