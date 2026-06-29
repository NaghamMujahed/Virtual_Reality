namespace PaintSim.Scripts.Core.Physics
{
    /// <summary>
    /// ثوابت فيزيائية كونية.
    /// هذه لا تتغير أبداً — const حقيقية.
    /// </summary>
    public static class PhysicsConstants
    {
        // ─────────────────────────────────────────
        public const float Pi           = 3.14159265f;
        public const float TwoPi        = 6.28318530f;
        public const float HalfPi       = 1.57079632f;

        // الجاذبية الأرضية (m/s²)
        public const float GravityEarth = 9.81f;

        // كثافة الهواء عند 20°C (kg/m³)
        public const float AirDensity   = 1.204f;
    }
}