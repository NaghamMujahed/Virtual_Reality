using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum PaintViscosityModel
    {
        Constant = 0,
        ShearThinningCarreau = 1,
        HerschelBulkley = 2
    }

    [CreateAssetMenu(
        fileName = "PaintMaterialConfig",
        menuName = "Paint Bucket Sim/Paint Material Config")]
    public class PaintMaterialConfig : ScriptableObject
    {
        [Header("Basic Material")]
            [Min(1.0f)]
            public float densityKgPerM3 = 1050.0f;

        public Color baseColor = new Color(0.1f, 0.35f, 1.0f, 1.0f);

        [Header("Viscosity / Rheology")]
        public PaintViscosityModel viscosityModel = PaintViscosityModel.Constant;

        [Tooltip("Constant dynamic viscosity in Pa.s.")]
        [Min(0.0001f)]
        public float constantViscosityPaS = 1.5f;

        [Tooltip("Zero-shear viscosity for Carreau-like shear thinning.")]
        [Min(0.0001f)]
        public float zeroShearViscosityPaS = 5.0f;

        [Tooltip("Infinite-shear viscosity for Carreau-like shear thinning.")]
        [Min(0.0001f)]
        public float infiniteShearViscosityPaS = 0.5f;

        [Tooltip("Relaxation time for shear-thinning model.")]
        [Min(0.0001f)]
        public float relaxationTimeSeconds = 0.8f;

        [Tooltip("Flow index. Less than 1 gives shear-thinning behavior.")]
        [Range(0.05f, 2.0f)]
        public float flowIndex = 0.55f;

        [Tooltip("Yield stress for yield-stress materials.")]
        [Min(0.0f)]
        public float yieldStressPa = 0.0f;

        [Header("Surface / Wetting")]
        [Tooltip("Surface tension in N/m. Paint values vary widely and should be calibrated.")]
        [Min(0.0001f)]
        public float surfaceTensionNPerM = 0.035f;

        [Header("Drying / Canvas Interaction")]
        [Tooltip("Base drying rate used later by the canvas system.")]
        [Min(0.0f)]
        public float dryingRatePerSecond = 0.02f;

        [Tooltip("Base absorption rate used later by the canvas system.")]
        [Min(0.0f)]
        public float absorptionRate = 0.5f;

        [Header("Solver-Independent Notes")]
        [TextArea(3, 8)]
        public string notes =
            "This material is solver-independent. It can be used by PBF, SPH, DFSPH, MPM, or another fluid model.";

        private void OnValidate()
        {
            if (densityKgPerM3 < 1.0f)
                densityKgPerM3 = 1.0f;

            if (constantViscosityPaS <= 0.0f)
                constantViscosityPaS = 0.0001f;

            if (surfaceTensionNPerM <= 0.0f)
                surfaceTensionNPerM = 0.0001f;
        }

        public float EvaluateViscosity(float shearRate, float temperatureCelsius)
        {
            shearRate = Mathf.Max(shearRate, 0.0f);

            if (viscosityModel == PaintViscosityModel.Constant)
                return constantViscosityPaS;

            if (viscosityModel == PaintViscosityModel.ShearThinningCarreau)
            {
                float lambdaGamma = relaxationTimeSeconds * shearRate;
                float exponent = 0.5f * (flowIndex - 1.0f);
                float factor = Mathf.Pow(1.0f + lambdaGamma * lambdaGamma, exponent);

                return infiniteShearViscosityPaS +
                       (zeroShearViscosityPaS - infiniteShearViscosityPaS) * factor;
            }

            // For Herschel-Bulkley we return an apparent viscosity approximation.
            // A full yield-stress flow model will be handled in later solver/outflow stages.
            float safeShear = Mathf.Max(shearRate, 0.01f);
            float consistency = constantViscosityPaS;
            return yieldStressPa / safeShear + consistency * Mathf.Pow(safeShear, flowIndex - 1.0f);
        }
    }
}