using UnityEngine;

namespace PaintBucketSim.Configs
{
    [CreateAssetMenu(
        fileName = "BoundaryConfig",
        menuName = "Paint Bucket Sim/Boundary Config")]
    public class BoundaryConfig : ScriptableObject
    {
        [Header("Particle Generation")]
        [Min(0.005f)]
        public float particleSpacingMeters = 0.035f;

        [Tooltip("Small offset used to avoid placing boundary particles exactly on ambiguous surfaces.")]
        [Min(0.0f)]
        public float surfaceOffsetMeters = 0.0f;

        public bool generateWall = true;
        public bool generateBottom = true;
        public bool generateHoleEdges = true;

        [Header("Wall")]
        [Tooltip("Minimum radial particles per wall layer.")]
        [Range(8, 128)]
        public int minRadialParticles = 16;

        [Header("Hole Edge")]
        [Tooltip("Number of rings along the hole channel direction.")]
        [Range(1, 8)]
        public int holeEdgeAxialRings = 2;

        [Tooltip("Extra clearance around the hole so bottom particles do not close the outlet.")]
        [Min(0.0f)]
        public float holeClearanceMeters = 0.01f;

        [Header("Rendering")]
        public bool renderBoundaryParticles = true;

        [Min(0.001f)]
        public float visualParticleSizeMeters = 0.018f;

        public Color wallColor = new Color(0.25f, 0.65f, 1.0f, 1.0f);
        public Color bottomColor = new Color(0.2f, 1.0f, 0.55f, 1.0f);
        public Color holeEdgeColor = new Color(1.0f, 0.25f, 0.15f, 1.0f);

        private void OnValidate()
        {
            if (particleSpacingMeters < 0.005f)
                particleSpacingMeters = 0.005f;

            if (minRadialParticles < 8)
                minRadialParticles = 8;

            if (holeEdgeAxialRings < 1)
                holeEdgeAxialRings = 1;

            if (visualParticleSizeMeters < 0.001f)
                visualParticleSizeMeters = 0.001f;
        }
    }
}