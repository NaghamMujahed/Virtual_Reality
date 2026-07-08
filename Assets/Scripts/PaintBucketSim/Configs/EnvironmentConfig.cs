using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "EnvironmentConfig",
        menuName = "Paint Bucket Sim/Environment Config")]
    public class EnvironmentConfig : ScriptableObject
    {
        [Header("Mechanical Environment")]
        public Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f);

        [Header("Air")]
        public Vector3 windVelocity = Vector3.zero;

        [Tooltip("Air density at common room conditions, approximately kg/m^3.")]
        [Min(0.01f)]
        public float airDensity = 1.225f;

        [Tooltip("Dynamic viscosity of air in Pa.s, approximate room value.")]
        [Min(0.000001f)]
        public float airViscosity = 1.8e-5f;

        [Header("Thermal / Humidity")]
        public float temperatureCelsius = 20.0f;

        [Range(0.0f, 1.0f)]
        public float relativeHumidity01 = 0.5f;

        [Tooltip("Atmospheric pressure in Pascal.")]
        [Min(1000.0f)]
        public float pressurePa = 101325.0f;

        private void OnValidate()
        {
            relativeHumidity01 = Mathf.Clamp01(relativeHumidity01);

            if (airDensity <= 0.0f)
                airDensity = 1.225f;

            if (airViscosity <= 0.0f)
                airViscosity = 1.8e-5f;
        }
    }
}