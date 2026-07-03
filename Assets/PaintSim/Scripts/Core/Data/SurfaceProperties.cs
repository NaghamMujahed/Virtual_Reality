using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    public enum SurfaceType
    {
        Wood   = 0,  
        Glass  = 1,  
        Fabric = 2,  
        Metal  = 3,  
    }

   
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SurfaceProperties
    {
       
        public float DepositionFraction;
        public float SplashMultiplier;
        public float AbsorptionRate;
        public float SpreadSpeed;
        public float Roughness;
        public uint SurfaceTypeID;

        private float _pad0;
        private float _pad1;
       
        public const int Stride = 32;

        public static SurfaceProperties Wood => new SurfaceProperties
        {
            DepositionFraction = 0.90f,
            SplashMultiplier   = 0.80f,
            AbsorptionRate     = 0.60f,
            SpreadSpeed        = 0.40f,
            Roughness          = 0.70f,
            SurfaceTypeID      = 0u
        };

        public static SurfaceProperties Glass => new SurfaceProperties
        {
            DepositionFraction = 0.40f,
            SplashMultiplier   = 2.00f,
            AbsorptionRate     = 0.00f,
            SpreadSpeed        = 0.10f,
            Roughness          = 0.05f,
            SurfaceTypeID      = 1u
        };

        public static SurfaceProperties Fabric => new SurfaceProperties
        {
            DepositionFraction = 0.99f,
            SplashMultiplier   = 0.10f,
            AbsorptionRate     = 0.90f,
            SpreadSpeed        = 0.80f,
            Roughness          = 0.90f,
            SurfaceTypeID      = 2u
        };

        public static SurfaceProperties Metal => new SurfaceProperties
        {
            DepositionFraction = 0.60f,
            SplashMultiplier   = 1.50f,
            AbsorptionRate     = 0.10f,
            SpreadSpeed        = 0.20f,
            Roughness          = 0.30f,
            SurfaceTypeID      = 3u
        };
        
        public static SurfaceProperties FromType(SurfaceType type)
        {
            switch (type)
            {
                case SurfaceType.Wood:   return Wood;
                case SurfaceType.Glass:  return Glass;
                case SurfaceType.Fabric: return Fabric;
                case SurfaceType.Metal:  return Metal;
                default:                 return Wood;
            }
        }
    }

    [System.Serializable]
    public struct SurfaceFilmInteraction
    {
        public float ContactLineThickness;
        public float ContactAngleResistance;
        public float SubstrateFlowVariation;
        public float DripFingerInstability;
        public float ThinFilmCohesion;
        public float EvaporationMultiplier;
        public float RunoffMultiplier;
        public float DiffusionMultiplier;

        public static SurfaceFilmInteraction Wood => new SurfaceFilmInteraction
        {
            ContactLineThickness = 0.000026f,
            ContactAngleResistance = 0.74f,
            SubstrateFlowVariation = 0.34f,
            DripFingerInstability = 0.46f,
            ThinFilmCohesion = 0.64f,
            EvaporationMultiplier = 1.18f,
            RunoffMultiplier = 0.82f,
            DiffusionMultiplier = 0.92f
        };

        public static SurfaceFilmInteraction Glass => new SurfaceFilmInteraction
        {
            ContactLineThickness = 0.000014f,
            ContactAngleResistance = 0.18f,
            SubstrateFlowVariation = 0.08f,
            DripFingerInstability = 0.20f,
            ThinFilmCohesion = 0.36f,
            EvaporationMultiplier = 0.62f,
            RunoffMultiplier = 1.48f,
            DiffusionMultiplier = 0.68f
        };

        public static SurfaceFilmInteraction Fabric => new SurfaceFilmInteraction
        {
            ContactLineThickness = 0.000035f,
            ContactAngleResistance = 0.88f,
            SubstrateFlowVariation = 0.55f,
            DripFingerInstability = 0.62f,
            ThinFilmCohesion = 0.84f,
            EvaporationMultiplier = 1.75f,
            RunoffMultiplier = 0.32f,
            DiffusionMultiplier = 1.10f
        };

        public static SurfaceFilmInteraction Metal => new SurfaceFilmInteraction
        {
            ContactLineThickness = 0.000018f,
            ContactAngleResistance = 0.38f,
            SubstrateFlowVariation = 0.16f,
            DripFingerInstability = 0.30f,
            ThinFilmCohesion = 0.44f,
            EvaporationMultiplier = 0.76f,
            RunoffMultiplier = 1.20f,
            DiffusionMultiplier = 0.74f
        };

        public static SurfaceFilmInteraction FromType(SurfaceType type)
        {
            switch (type)
            {
                case SurfaceType.Glass:  return Glass;
                case SurfaceType.Fabric: return Fabric;
                case SurfaceType.Metal:  return Metal;
                case SurfaceType.Wood:
                default:                 return Wood;
            }
        }
    }

    [System.Serializable]
    public struct SurfaceVisualProperties
    {
        public Color CanvasBaseColor;
        public float CanvasSmoothness;
        public float DryPaintSmoothness;
        public float WetPaintSmoothness;
        public float PaintNormalStrength;
        public float ParallaxStrength;
        public float EdgeRidgeStrength;
        public float EdgeDarkening;
        public float MicroNormalStrength;
        public float CanvasGrainStrength;
        public float CanvasGrainScale;
        public float PigmentSaturation;
        public float WetDarkening;
        public float EdgeHighlightStrength;
        public float WetSpecularStrength;
        public float ClearCoatStrength;
        public float EnvironmentReflection;
        public float FresnelStrength;
        public float MaxThickness;
        public float WetnessShine;

        public static SurfaceVisualProperties Wood => new SurfaceVisualProperties
        {
            CanvasBaseColor = new Color(0.72f, 0.65f, 0.55f, 1.0f),
            CanvasSmoothness = 0.22f,
            DryPaintSmoothness = 0.46f,
            WetPaintSmoothness = 0.92f,
            PaintNormalStrength = 3.25f,
            ParallaxStrength = 0.006f,
            EdgeRidgeStrength = 5.0f,
            EdgeDarkening = 0.070f,
            MicroNormalStrength = 0.045f,
            CanvasGrainStrength = 0.070f,
            CanvasGrainScale = 155.0f,
            PigmentSaturation = 1.08f,
            WetDarkening = 0.065f,
            EdgeHighlightStrength = 0.30f,
            WetSpecularStrength = 0.88f,
            ClearCoatStrength = 0.72f,
            EnvironmentReflection = 0.46f,
            FresnelStrength = 0.18f,
            MaxThickness = 0.000032f,
            WetnessShine = 0.78f
        };

        public static SurfaceVisualProperties Glass => new SurfaceVisualProperties
        {
            CanvasBaseColor = new Color(0.82f, 0.86f, 0.90f, 1.0f),
            CanvasSmoothness = 0.66f,
            DryPaintSmoothness = 0.54f,
            WetPaintSmoothness = 0.98f,
            PaintNormalStrength = 2.25f,
            ParallaxStrength = 0.004f,
            EdgeRidgeStrength = 4.2f,
            EdgeDarkening = 0.035f,
            MicroNormalStrength = 0.010f,
            CanvasGrainStrength = 0.012f,
            CanvasGrainScale = 270.0f,
            PigmentSaturation = 1.16f,
            WetDarkening = 0.035f,
            EdgeHighlightStrength = 0.46f,
            WetSpecularStrength = 1.32f,
            ClearCoatStrength = 1.20f,
            EnvironmentReflection = 0.92f,
            FresnelStrength = 0.30f,
            MaxThickness = 0.000026f,
            WetnessShine = 0.95f
        };

        public static SurfaceVisualProperties Fabric => new SurfaceVisualProperties
        {
            CanvasBaseColor = new Color(0.58f, 0.55f, 0.50f, 1.0f),
            CanvasSmoothness = 0.10f,
            DryPaintSmoothness = 0.32f,
            WetPaintSmoothness = 0.74f,
            PaintNormalStrength = 4.1f,
            ParallaxStrength = 0.005f,
            EdgeRidgeStrength = 6.2f,
            EdgeDarkening = 0.12f,
            MicroNormalStrength = 0.095f,
            CanvasGrainStrength = 0.125f,
            CanvasGrainScale = 115.0f,
            PigmentSaturation = 0.96f,
            WetDarkening = 0.095f,
            EdgeHighlightStrength = 0.16f,
            WetSpecularStrength = 0.36f,
            ClearCoatStrength = 0.26f,
            EnvironmentReflection = 0.16f,
            FresnelStrength = 0.08f,
            MaxThickness = 0.000042f,
            WetnessShine = 0.46f
        };

        public static SurfaceVisualProperties Metal => new SurfaceVisualProperties
        {
            CanvasBaseColor = new Color(0.68f, 0.69f, 0.70f, 1.0f),
            CanvasSmoothness = 0.48f,
            DryPaintSmoothness = 0.50f,
            WetPaintSmoothness = 0.96f,
            PaintNormalStrength = 2.45f,
            ParallaxStrength = 0.0045f,
            EdgeRidgeStrength = 4.5f,
            EdgeDarkening = 0.045f,
            MicroNormalStrength = 0.020f,
            CanvasGrainStrength = 0.026f,
            CanvasGrainScale = 240.0f,
            PigmentSaturation = 1.10f,
            WetDarkening = 0.050f,
            EdgeHighlightStrength = 0.40f,
            WetSpecularStrength = 1.18f,
            ClearCoatStrength = 1.05f,
            EnvironmentReflection = 0.82f,
            FresnelStrength = 0.24f,
            MaxThickness = 0.000028f,
            WetnessShine = 0.88f
        };

        public static SurfaceVisualProperties FromType(SurfaceType type)
        {
            switch (type)
            {
                case SurfaceType.Glass:  return Glass;
                case SurfaceType.Fabric: return Fabric;
                case SurfaceType.Metal:  return Metal;
                case SurfaceType.Wood:
                default:                 return Wood;
            }
        }
    }
}
