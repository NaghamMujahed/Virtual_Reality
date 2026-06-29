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
}