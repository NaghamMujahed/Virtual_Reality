using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum PaintMaterialPreset
    {
        Custom = 0,
        WaterLike = 1,
        ThinPaint = 2,
        LatexPaint = 3,
        ThickPaint = 4,
        HeavyBodyPaint = 5
    }

    public enum PaintViscosityModel
    {
        Constant = 0,
        CarreauYasuda = 1,
        ShearThinningCarreau = 1,
        HerschelBulkley = 2
    }

    public struct PaintRheologyProfile
    {
        public float mpmViscosityPaS;
        public float lowShearViscosityPaS;
        public float highShearViscosityPaS;
        public float relaxationTimeSeconds;
        public float flowIndex;
        public float yasudaExponent;
        public float yieldStressPa;
    }

    [System.Serializable]
    public struct PaintSurfaceFilmProfile
    {
        [Header("Physical")]
        [Min(1.0f)] public float densityKgPerM3;
        [Min(0.0001f)] public float dynamicViscosityPaS;
        [Min(0.0001f)] public float surfaceTensionNPerM;
        [Min(0.0f)] public float yieldStressPa;

        [Header("Surface Evolution")]
        [Min(0.0f)] public float evaporationRate;
        [Min(0.0f)] public float diffusionRate;
        [Min(0.0f)] public float runoffRate;
        [Min(0.000001f)] public float minimumWetThickness;
        [Min(0.000001f)] public float contactLineThickness;
        [Range(0.0f, 1.0f)] public float contactAngleResistance;
        [Range(0.0f, 1.0f)] public float substrateFlowVariation;
        [Range(0.0f, 1.0f)] public float dripFingerInstability;
        [Range(0.0f, 1.0f)] public float thinFilmCohesion;

        [Header("Moving Board Response")]
        [Range(0.0f, 1.5f)] public float surfaceInertiaResponse;
        [Min(0.0f)] public float maxSurfaceInertialAcceleration;

        public PaintSurfaceFilmProfile Sanitized()
        {
            densityKgPerM3 = Mathf.Max(densityKgPerM3, 1.0f);
            dynamicViscosityPaS = Mathf.Max(dynamicViscosityPaS, 0.0001f);
            surfaceTensionNPerM = Mathf.Max(surfaceTensionNPerM, 0.0001f);
            yieldStressPa = Mathf.Max(yieldStressPa, 0.0f);
            evaporationRate = Mathf.Max(evaporationRate, 0.0f);
            diffusionRate = Mathf.Max(diffusionRate, 0.0f);
            runoffRate = Mathf.Max(runoffRate, 0.0f);
            minimumWetThickness = Mathf.Max(minimumWetThickness, 0.000001f);
            contactLineThickness = Mathf.Max(contactLineThickness, 0.000001f);
            contactAngleResistance = Mathf.Clamp01(contactAngleResistance);
            substrateFlowVariation = Mathf.Clamp01(substrateFlowVariation);
            dripFingerInstability = Mathf.Clamp01(dripFingerInstability);
            thinFilmCohesion = Mathf.Clamp01(thinFilmCohesion);
            surfaceInertiaResponse = Mathf.Clamp(surfaceInertiaResponse, 0.0f, 1.5f);
            maxSurfaceInertialAcceleration =
                Mathf.Max(maxSurfaceInertialAcceleration, 0.0f);
            return this;
        }

        public static PaintSurfaceFilmProfile WaterLike(
            float density,
            float viscosity,
            float surfaceTension)
        {
            return new PaintSurfaceFilmProfile
            {
                densityKgPerM3 = density,
                dynamicViscosityPaS = viscosity,
                surfaceTensionNPerM = surfaceTension,
                yieldStressPa = 0.0f,
                evaporationRate = 0.006f,
                diffusionRate = 55.0f,
                runoffRate = 2.20f,
                minimumWetThickness = 0.000004f,
                contactLineThickness = 0.000006f,
                contactAngleResistance = 0.01f,
                substrateFlowVariation = 0.12f,
                dripFingerInstability = 0.22f,
                thinFilmCohesion = 0.02f,
                surfaceInertiaResponse = 1.20f,
                maxSurfaceInertialAcceleration = 55.0f
            }.Sanitized();
        }

        public static PaintSurfaceFilmProfile ThinPaint(
            float density,
            float viscosity,
            float surfaceTension,
            float yieldStress)
        {
            return new PaintSurfaceFilmProfile
            {
                densityKgPerM3 = density,
                dynamicViscosityPaS = viscosity,
                surfaceTensionNPerM = surfaceTension,
                yieldStressPa = yieldStress,
                evaporationRate = 0.018f,
                diffusionRate = 34.0f,
                runoffRate = 0.52f,
                minimumWetThickness = 0.000010f,
                contactLineThickness = 0.000017f,
                contactAngleResistance = 0.38f,
                substrateFlowVariation = 0.24f,
                dripFingerInstability = 0.48f,
                thinFilmCohesion = 0.32f,
                surfaceInertiaResponse = 0.78f,
                maxSurfaceInertialAcceleration = 38.0f
            }.Sanitized();
        }

        public static PaintSurfaceFilmProfile LatexPaint(
            float density,
            float viscosity,
            float surfaceTension,
            float yieldStress)
        {
            return new PaintSurfaceFilmProfile
            {
                densityKgPerM3 = density,
                dynamicViscosityPaS = viscosity,
                surfaceTensionNPerM = surfaceTension,
                yieldStressPa = yieldStress,
                evaporationRate = 0.020f,
                diffusionRate = 30.0f,
                runoffRate = 0.26f,
                minimumWetThickness = 0.000015f,
                contactLineThickness = 0.000025f,
                contactAngleResistance = 0.72f,
                substrateFlowVariation = 0.28f,
                dripFingerInstability = 0.36f,
                thinFilmCohesion = 0.62f,
                surfaceInertiaResponse = 0.35f,
                maxSurfaceInertialAcceleration = 32.0f
            }.Sanitized();
        }

        public static PaintSurfaceFilmProfile ThickPaint(
            float density,
            float viscosity,
            float surfaceTension,
            float yieldStress)
        {
            return new PaintSurfaceFilmProfile
            {
                densityKgPerM3 = density,
                dynamicViscosityPaS = viscosity,
                surfaceTensionNPerM = surfaceTension,
                yieldStressPa = yieldStress,
                evaporationRate = 0.016f,
                diffusionRate = 18.0f,
                runoffRate = 0.14f,
                minimumWetThickness = 0.000020f,
                contactLineThickness = 0.000036f,
                contactAngleResistance = 0.82f,
                substrateFlowVariation = 0.34f,
                dripFingerInstability = 0.24f,
                thinFilmCohesion = 0.78f,
                surfaceInertiaResponse = 0.25f,
                maxSurfaceInertialAcceleration = 26.0f
            }.Sanitized();
        }

        public static PaintSurfaceFilmProfile HeavyBodyPaint(
            float density,
            float viscosity,
            float surfaceTension,
            float yieldStress)
        {
            return new PaintSurfaceFilmProfile
            {
                densityKgPerM3 = density,
                dynamicViscosityPaS = viscosity,
                surfaceTensionNPerM = surfaceTension,
                yieldStressPa = yieldStress,
                evaporationRate = 0.012f,
                diffusionRate = 12.0f,
                runoffRate = 0.075f,
                minimumWetThickness = 0.000026f,
                contactLineThickness = 0.000050f,
                contactAngleResistance = 0.90f,
                substrateFlowVariation = 0.40f,
                dripFingerInstability = 0.16f,
                thinFilmCohesion = 0.88f,
                surfaceInertiaResponse = 0.12f,
                maxSurfaceInertialAcceleration = 20.0f
            }.Sanitized();
        }
    }

    [CreateAssetMenu(
        fileName = "PaintMaterialConfig",
        menuName = "Paint Bucket Sim/Paint Material Config")]
    public class PaintMaterialConfig : ScriptableObject
    {
        [Header("Preset / Single Material Source")]
        [Tooltip("Authoritative material preset for the bucket fluid, impact deposition, and paint film on the board. GPU solver presets should be treated as numerical-stability packages, not as the visual material source.")]
        public PaintMaterialPreset materialPreset = PaintMaterialPreset.LatexPaint;

        [Tooltip("When enabled, the selected material preset writes the physical/rheology values below. Disable for hand-authored material experiments.")]
        public bool autoApplyMaterialPreset = true;

        [Header("Basic Material")]
            [Min(1.0f)]
            public float densityKgPerM3 = 1050.0f;

        public Color baseColor = new Color(0.1f, 0.35f, 1.0f, 1.0f);

        [Header("Viscosity / Rheology")]
        public PaintViscosityModel viscosityModel = PaintViscosityModel.Constant;

        [Tooltip("Constant dynamic viscosity in Pa.s.")]
        [Min(0.0001f)]
        public float constantViscosityPaS = 1.5f;

        [Tooltip("Zero-shear viscosity for Carreau-Yasuda shear thinning.")]
        [Min(0.0001f)]
        public float zeroShearViscosityPaS = 5.0f;

        [Tooltip("Infinite-shear viscosity for Carreau-Yasuda shear thinning.")]
        [Min(0.0001f)]
        public float infiniteShearViscosityPaS = 0.5f;

        [Tooltip("Relaxation time for shear-thinning model.")]
        [Min(0.0001f)]
        public float relaxationTimeSeconds = 0.8f;

        [Tooltip("Carreau-Yasuda transition sharpness a. a=2 behaves like the classic Carreau model.")]
        [Range(0.25f, 8.0f)]
        public float yasudaExponent = 2.0f;

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
            if (autoApplyMaterialPreset &&
                materialPreset != PaintMaterialPreset.Custom)
            {
                ApplyMaterialPreset(materialPreset);
            }

            if (densityKgPerM3 < 1.0f)
                densityKgPerM3 = 1.0f;

            if (constantViscosityPaS <= 0.0f)
                constantViscosityPaS = 0.0001f;

            if (zeroShearViscosityPaS <= 0.0f)
                zeroShearViscosityPaS = 0.0001f;

            if (infiniteShearViscosityPaS <= 0.0f)
                infiniteShearViscosityPaS = 0.0001f;

            if (infiniteShearViscosityPaS > zeroShearViscosityPaS)
                infiniteShearViscosityPaS = zeroShearViscosityPaS;

            if (relaxationTimeSeconds <= 0.0f)
                relaxationTimeSeconds = 0.0001f;

            yasudaExponent = Mathf.Clamp(yasudaExponent, 0.25f, 8.0f);
            flowIndex = Mathf.Clamp(flowIndex, 0.05f, 2.0f);

            if (yieldStressPa < 0.0f)
                yieldStressPa = 0.0f;

            if (surfaceTensionNPerM <= 0.0f)
                surfaceTensionNPerM = 0.0001f;

        }

        public void ApplyMaterialPreset()
        {
            ApplyMaterialPreset(materialPreset);
        }

        public void ApplyMaterialPreset(PaintMaterialPreset preset)
        {
            materialPreset = preset;

            if (preset == PaintMaterialPreset.Custom)
                return;

            switch (preset)
            {
                case PaintMaterialPreset.WaterLike:
                    densityKgPerM3 = 1000.0f;
                    // baseColor = new Color(0.62f, 0.86f, 1.0f, 0.55f);
                    viscosityModel = PaintViscosityModel.Constant;
                    constantViscosityPaS = 0.0012f;
                    zeroShearViscosityPaS = 0.0012f;
                    infiniteShearViscosityPaS = 0.0010f;
                    relaxationTimeSeconds = 0.02f;
                    yasudaExponent = 2.0f;
                    flowIndex = 1.0f;
                    yieldStressPa = 0.0f;
                    surfaceTensionNPerM = 0.072f;
                    dryingRatePerSecond = 0.004f;
                    absorptionRate = 0.18f;
                    break;

                case PaintMaterialPreset.ThinPaint:
                    densityKgPerM3 = 1080.0f;
                    viscosityModel = PaintViscosityModel.CarreauYasuda;
                    constantViscosityPaS = 0.08f;
                    zeroShearViscosityPaS = 0.9f;
                    infiniteShearViscosityPaS = 0.06f;
                    relaxationTimeSeconds = 0.42f;
                    yasudaExponent = 2.0f;
                    flowIndex = 0.68f;
                    yieldStressPa = 0.04f;
                    surfaceTensionNPerM = 0.037f;
                    dryingRatePerSecond = 0.018f;
                    absorptionRate = 0.42f;
                    break;

                case PaintMaterialPreset.LatexPaint:
                    densityKgPerM3 = 1150.0f;
                    viscosityModel = PaintViscosityModel.CarreauYasuda;
                    constantViscosityPaS = 0.16f;
                    zeroShearViscosityPaS = 2.6f;
                    infiniteShearViscosityPaS = 0.14f;
                    relaxationTimeSeconds = 0.8f;
                    yasudaExponent = 2.2f;
                    flowIndex = 0.55f;
                    yieldStressPa = 0.0f;
                    surfaceTensionNPerM = 0.035f;
                    dryingRatePerSecond = 0.020f;
                    absorptionRate = 0.50f;
                    break;

                case PaintMaterialPreset.ThickPaint:
                    densityKgPerM3 = 1220.0f;
                    viscosityModel = PaintViscosityModel.CarreauYasuda;
                    constantViscosityPaS = 0.22f;
                    zeroShearViscosityPaS = 4.0f;
                    infiniteShearViscosityPaS = 0.18f;
                    relaxationTimeSeconds = 1.15f;
                    yasudaExponent = 2.4f;
                    flowIndex = 0.48f;
                    yieldStressPa = 0.25f;
                    surfaceTensionNPerM = 0.034f;
                    dryingRatePerSecond = 0.016f;
                    absorptionRate = 0.58f;
                    break;

                case PaintMaterialPreset.HeavyBodyPaint:
                    densityKgPerM3 = 1280.0f;
                    viscosityModel = PaintViscosityModel.CarreauYasuda;
                    constantViscosityPaS = 0.30f;
                    zeroShearViscosityPaS = 5.5f;
                    infiniteShearViscosityPaS = 0.25f;
                    relaxationTimeSeconds = 1.6f;
                    yasudaExponent = 2.6f;
                    flowIndex = 0.42f;
                    yieldStressPa = 0.35f;
                    surfaceTensionNPerM = 0.033f;
                    dryingRatePerSecond = 0.012f;
                    absorptionRate = 0.65f;
                    break;
            }
        }

        public float EvaluateViscosity(float shearRate, float temperatureCelsius)
        {
            shearRate = Mathf.Max(shearRate, 0.0f);

            if (viscosityModel == PaintViscosityModel.Constant)
                return constantViscosityPaS;

            if (viscosityModel == PaintViscosityModel.CarreauYasuda)
            {
                float lambdaGamma = relaxationTimeSeconds * shearRate;
                float a = Mathf.Max(yasudaExponent, 0.25f);
                float exponent = (flowIndex - 1.0f) / a;
                float factor = Mathf.Pow(
                    1.0f + Mathf.Pow(lambdaGamma, a),
                    exponent
                );

                return infiniteShearViscosityPaS +
                       (zeroShearViscosityPaS - infiniteShearViscosityPaS) * factor;
            }

            // For Herschel-Bulkley we return an apparent viscosity approximation.
            // A full yield-stress flow model will be handled in later solver/outflow stages.
            float safeShear = Mathf.Max(shearRate, 0.01f);
            float consistency = constantViscosityPaS;
            return yieldStressPa / safeShear + consistency * Mathf.Pow(safeShear, flowIndex - 1.0f);
        }

        public PaintRheologyProfile EvaluateRheologyProfile(float temperatureCelsius)
        {
            float lowShear = Mathf.Max(
                EvaluateViscosity(0.05f, temperatureCelsius),
                0.0001f
            );
            float highShear = Mathf.Max(
                EvaluateViscosity(80.0f, temperatureCelsius),
                0.0001f
            );

            if (highShear > lowShear)
                highShear = lowShear;

            return new PaintRheologyProfile
            {
                mpmViscosityPaS = Mathf.Max(
                    EvaluateViscosity(20.0f, temperatureCelsius),
                    0.0001f
                ),
                lowShearViscosityPaS = lowShear,
                highShearViscosityPaS = highShear,
                relaxationTimeSeconds = Mathf.Max(
                    relaxationTimeSeconds,
                    0.0001f
                ),
                flowIndex = Mathf.Clamp(flowIndex, 0.05f, 1.0f),
                yasudaExponent = Mathf.Clamp(yasudaExponent, 0.25f, 8.0f),
                yieldStressPa = Mathf.Max(yieldStressPa, 0.0f)
            };
        }

        public PaintSurfaceFilmProfile EvaluateSurfaceFilmProfile(
            float shearRate,
            float temperatureCelsius)
        {
            float viscosity = Mathf.Max(
                EvaluateViscosity(shearRate, temperatureCelsius),
                0.0001f
            );
            float density = Mathf.Max(densityKgPerM3, 1.0f);
            float surfaceTension = Mathf.Max(surfaceTensionNPerM, 0.0001f);
            float yieldStress = Mathf.Max(yieldStressPa, 0.0f);

            PaintMaterialPreset behavior = ResolveSurfaceFilmBehavior(viscosity);

            switch (behavior)
            {
                case PaintMaterialPreset.WaterLike:
                    return PaintSurfaceFilmProfile.WaterLike(
                        density,
                        viscosity,
                        surfaceTension
                    );

                case PaintMaterialPreset.ThinPaint:
                    return PaintSurfaceFilmProfile.ThinPaint(
                        density,
                        viscosity,
                        surfaceTension,
                        yieldStress
                    );

                case PaintMaterialPreset.ThickPaint:
                    return PaintSurfaceFilmProfile.ThickPaint(
                        density,
                        viscosity,
                        surfaceTension,
                        yieldStress
                    );

                case PaintMaterialPreset.HeavyBodyPaint:
                    return PaintSurfaceFilmProfile.HeavyBodyPaint(
                        density,
                        viscosity,
                        surfaceTension,
                        yieldStress
                    );

                case PaintMaterialPreset.LatexPaint:
                case PaintMaterialPreset.Custom:
                default:
                    return PaintSurfaceFilmProfile.LatexPaint(
                        density,
                        viscosity,
                        surfaceTension,
                        yieldStress
                    );
            }
        }

        private PaintMaterialPreset ResolveSurfaceFilmBehavior(float viscosity)
        {
            switch (materialPreset)
            {
                case PaintMaterialPreset.WaterLike:
                    return PaintMaterialPreset.WaterLike;
                case PaintMaterialPreset.ThinPaint:
                    return PaintMaterialPreset.ThinPaint;
                case PaintMaterialPreset.ThickPaint:
                    return PaintMaterialPreset.ThickPaint;
                case PaintMaterialPreset.HeavyBodyPaint:
                    return PaintMaterialPreset.HeavyBodyPaint;
                case PaintMaterialPreset.LatexPaint:
                    return PaintMaterialPreset.LatexPaint;
            }

            if (yieldStressPa <= 0.015f && viscosity <= 0.035f)
                return PaintMaterialPreset.WaterLike;

            if (yieldStressPa <= 0.08f && viscosity <= 0.35f)
                return PaintMaterialPreset.ThinPaint;

            if (yieldStressPa >= 0.30f || viscosity >= 3.5f)
                return PaintMaterialPreset.HeavyBodyPaint;

            if (yieldStressPa >= 0.15f || viscosity >= 1.8f)
                return PaintMaterialPreset.ThickPaint;

            return PaintMaterialPreset.LatexPaint;
        }
    }
}
