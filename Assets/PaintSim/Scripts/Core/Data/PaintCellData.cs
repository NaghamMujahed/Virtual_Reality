using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintSim.Scripts.Core.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PaintCellData
    {
        public int ThicknessInt;
        public float Wetness;
        public float Age;
        public uint IsActive;
        public Vector4 Color;
        public Vector2 FlowVelocity;
        public float PaintDensity;
        private float _pad0;

        public const int Stride = 48;
        // 10 nm fixed-point units. Micrometre precision was too coarse for
        // physically calibrated MLS-MPM particles and caused visible mass loss
        // when a thin film spread across multiple cells.
        public const int ThicknessScale = 100000000;

        public float Thickness => ThicknessInt / (float)ThicknessScale;

        public static int ToThicknessInt(float thicknessMeters)
        {
            return Mathf.RoundToInt(thicknessMeters * ThicknessScale);
        }

        public PaintCellData MergeWith(PaintCellData incoming)
        {
            int totalThicknessInt = ThicknessInt + incoming.ThicknessInt;
            if (totalThicknessInt <= 0)
                return this;

            float oldWeight = ThicknessInt / (float)totalThicknessInt;
            float newWeight = incoming.ThicknessInt / (float)totalThicknessInt;

            return new PaintCellData
            {
                ThicknessInt = totalThicknessInt,
                Wetness = Mathf.Max(Wetness, incoming.Wetness),
                Age = Mathf.Min(Age, incoming.Age),
                IsActive = 1u,
                Color = Color * oldWeight + incoming.Color * newWeight,
                FlowVelocity = FlowVelocity + incoming.FlowVelocity,
                PaintDensity =
                    PaintDensity * oldWeight +
                    incoming.PaintDensity * newWeight
            };
        }

        public static PaintCellData Empty => new PaintCellData
        {
            ThicknessInt = 0,
            Wetness = 0.0f,
            Age = 0.0f,
            IsActive = 0u,
            Color = Vector4.zero,
            FlowVelocity = Vector2.zero,
            PaintDensity = 0.0f
        };
    }
}
