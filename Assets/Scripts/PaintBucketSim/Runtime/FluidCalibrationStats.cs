namespace PaintBucketSim.Runtime
{
    [System.Serializable]
    public struct FluidCalibrationStats
    {
        public bool valid;

        public float bucketInnerVolumeM3;
        public float targetPaintVolumeM3;
        public int targetParticleCount;
        public int actualParticleCount;

        public float restDensityKgPerM3;
        public float restVolumePerParticleM3;
        public float massPerParticleKg;

        public float estimatedParticleSpacingM;
        public float particleRadiusM;

        public float totalMassKg;
        public float actualRepresentedVolumeM3;
        public float fillVolumeErrorPercent;

        public float gridCellSizeM;
        public float particleSpacingToCellSizeRatio;

        public float correctFillVolumeM3;
        public float configuredFillFraction;
        public float dryBucketMassKg;
        public float initialPaintMassKg;
    }
}
