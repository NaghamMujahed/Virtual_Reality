using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaintBucketSim.Runtime
{
    public struct GpuParticleRenderStats
    {
        public bool initialized;
        public bool enabled;

        public int uploadedParticles;
        public int maxRenderedParticles;
        public int renderStride;
        public int visualMode;
        public float visualRadiusScale;
        public int meshVertexCount;
        public int meshIndexCount;

        public int uploadFrame;
        public float cpuUploadMilliseconds;
        public float cpuRenderSubmitMilliseconds;

        public bool usingPerParticleColor;
        public bool usingCameraFacingSplat;
    }
}
