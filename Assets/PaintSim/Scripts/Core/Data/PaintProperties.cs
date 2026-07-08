using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PaintProperties
    {
        public float Density;
        public float DynamicViscosity;
        public float SurfaceTension;
        public float KinematicViscosity;
        public Vector4 Color;  // للـ GPU — float4

        public PaintProperties(
            float density,
            float dynamicViscosity,
            float surfaceTension,
            float kinematicViscosity,
            Vector4 color)
        {
            Density = density;
            DynamicViscosity = dynamicViscosity;
            SurfaceTension = surfaceTension;
            KinematicViscosity = kinematicViscosity;
            Color = color;
        }

        public static PaintProperties Create(
            float density,
            float dynamicViscosity,
            float surfaceTension,
            Color paintColor)  // <-- غيرت اسم الـ parameter
        {
            return new PaintProperties(
                density: density,
                dynamicViscosity: dynamicViscosity,
                surfaceTension: surfaceTension,
                kinematicViscosity: dynamicViscosity / density,
                color: new Vector4(paintColor.r, paintColor.g, paintColor.b, paintColor.a)
            );
        }

        public static PaintProperties Default
        {
            get
            {
                return Create(
                    1200f,
                    0.1f,
                    0.04f,
                    UnityEngine.Color.red  // <-- صريح UnityEngine.Color
                );
            }
        }
    }
}