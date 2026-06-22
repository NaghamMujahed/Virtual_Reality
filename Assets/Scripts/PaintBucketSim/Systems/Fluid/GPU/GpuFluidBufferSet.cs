using System.Diagnostics;
using PaintBucketSim.Configs;
using PaintBucketSim.Runtime;
using UnityEngine;

namespace PaintBucketSim.Systems.Fluid.GPU
{
    public class GpuFluidBufferSet : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PaintFluidSystem paintFluidSystem;
        [SerializeField] private GpuFluidBufferConfig bufferConfig;
        [SerializeField] private ComputeShader utilityCompute;

        private GraphicsBuffer _positionRadiusBuffer;
        private GraphicsBuffer _velocityMassBuffer;
        private GraphicsBuffer _colorBuffer;
        private GraphicsBuffer _stateAgeIdBuffer;

        private Vector4[] _cpuPositionRadius;
        private Vector4[] _cpuVelocityMass;
        private Vector4[] _cpuColor;
        private Vector4[] _cpuStateAgeId;

        private int _capacity;
        private int _uploadedCount;
        private int _lastUploadFrame = -1;

        private int _kernelDebugColorByState = -1;

        private readonly Stopwatch _uploadWatch = new Stopwatch();
        private readonly Stopwatch _computeWatch = new Stopwatch();

        private GpuFluidBufferStats _stats;

        public GpuFluidBufferConfig Config => bufferConfig;

        public GraphicsBuffer PositionRadiusBuffer => _positionRadiusBuffer;
        public GraphicsBuffer VelocityMassBuffer => _velocityMassBuffer;
        public GraphicsBuffer ColorBuffer => _colorBuffer;
        public GraphicsBuffer StateAgeIdBuffer => _stateAgeIdBuffer;

        // G5 Changes //
        private GraphicsBuffer _affineC0Buffer;
        private GraphicsBuffer _affineC1Buffer;
        private GraphicsBuffer _affineC2Buffer;

        private Vector4[] _cpuAffineC0;
        private Vector4[] _cpuAffineC1;
        private Vector4[] _cpuAffineC2;

        public GraphicsBuffer AffineC0Buffer => _affineC0Buffer;
        public GraphicsBuffer AffineC1Buffer => _affineC1Buffer;
        public GraphicsBuffer AffineC2Buffer => _affineC2Buffer;

        // End G5 Changes //

        ////////////////    G6.A Changes   //////////////////

        private GraphicsBuffer _volumeJBuffer;
        private GraphicsBuffer _deformationF0Buffer;
        private GraphicsBuffer _deformationF1Buffer;
        private GraphicsBuffer _deformationF2Buffer;

        private Vector4[] _cpuVolumeJ;
        private Vector4[] _cpuDeformationF0;
        private Vector4[] _cpuDeformationF1;
        private Vector4[] _cpuDeformationF2;

        public GraphicsBuffer VolumeJBuffer => _volumeJBuffer;
        public GraphicsBuffer DeformationF0Buffer => _deformationF0Buffer;
        public GraphicsBuffer DeformationF1Buffer => _deformationF1Buffer;
        public GraphicsBuffer DeformationF2Buffer => _deformationF2Buffer;

        ////////////////    End G6.A Changes   //////////////////

        public int UploadedParticleCount => _uploadedCount;
        public int Capacity => _capacity;

        public bool IsInitialized =>
            _positionRadiusBuffer != null &&
            _velocityMassBuffer != null &&
            _colorBuffer != null &&
            _stateAgeIdBuffer != null &&
            // G5 Changes //
            _affineC0Buffer != null &&
            _affineC1Buffer != null &&
            _affineC2Buffer != null &&
            // End G5 Changes //

            // G6.A Changes //
            _volumeJBuffer != null &&
            _deformationF0Buffer != null &&
            _deformationF1Buffer != null &&
            _deformationF2Buffer != null
            // End G6.A Changes //
            ;

        public GpuFluidBufferStats Stats => _stats;

        private bool _externalGpuSimulationMode;

        private void Awake()
        {
            if (paintFluidSystem == null)
                paintFluidSystem = FindFirstObjectByType<PaintFluidSystem>();

            ResolveComputeKernels();
        }

        private void OnEnable()
        {
            EnsureBuffers();
        }

        private void OnDisable()
        {
            ReleaseBuffers();
        }

        private void OnDestroy()
        {
            ReleaseBuffers();
        }

        private void LateUpdate()
        {
            if (bufferConfig == null ||
                !bufferConfig.enableGpuFluidBuffers ||
                paintFluidSystem == null ||
                !paintFluidSystem.IsInitialized)
            {
                _stats.enabled = false;
                return;
            }

            EnsureBuffers();

            if (!_externalGpuSimulationMode && bufferConfig.uploadFromCpuWhileCpuSolverActive)
            {
                bool shouldUpload =
                    Time.frameCount % bufferConfig.uploadEveryNFrames == 0 ||
                    _lastUploadFrame < 0 ||
                    _uploadedCount <= 0;

                if (shouldUpload)
                    UploadFromCpuParticles();
            }

            if (bufferConfig.enableComputePostProcess &&
                bufferConfig.debugColorByStateOnGpu)
            {
                DispatchDebugColorByState();
            }

            UpdateStats();
        }

        private void ResolveComputeKernels()
        {
            _kernelDebugColorByState = -1;

            if (utilityCompute == null)
                return;

            try
            {
                _kernelDebugColorByState = utilityCompute.FindKernel("KDebugColorByState");
            }
            catch
            {
                _kernelDebugColorByState = -1;
            }
        }

        private void EnsureBuffers()
        {
            if (bufferConfig == null)
                return;

            int requestedCapacity = Mathf.Max(1, bufferConfig.maxGpuParticles);

            if (IsInitialized && _capacity == requestedCapacity)
                return;

            ReleaseBuffers();

            _capacity = requestedCapacity;

            _cpuPositionRadius = new Vector4[_capacity];
            _cpuVelocityMass = new Vector4[_capacity];
            _cpuColor = new Vector4[_capacity];
            _cpuStateAgeId = new Vector4[_capacity];

            // G5 Changes //
            _cpuAffineC0 = new Vector4[_capacity];
            _cpuAffineC1 = new Vector4[_capacity];
            _cpuAffineC2 = new Vector4[_capacity];
            // End G5 Changes //

            // G6.A Changes //
            _cpuVolumeJ = new Vector4[_capacity];
            _cpuDeformationF0 = new Vector4[_capacity];
            _cpuDeformationF1 = new Vector4[_capacity];
            _cpuDeformationF2 = new Vector4[_capacity];
            // End G6.A Changes //

            // All buffers use Vector4/float4 => stride 16 bytes.
            _positionRadiusBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _velocityMassBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _colorBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _stateAgeIdBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            // G5 Changes //
            _affineC0Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _affineC1Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _affineC2Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );
            // End G5 Changes //

            // G6.A Changes //
            _volumeJBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _deformationF0Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _deformationF1Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

            _deformationF2Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );
            // End G6.A Changes //

            _uploadedCount = 0;
            _lastUploadFrame = -1;

            if (bufferConfig.logLifecycle)
            {
                UnityEngine.Debug.Log(
                    $"GpuFluidBufferSet: Allocated GPU buffers with capacity {_capacity}."
                );
            }

            UpdateStats();
        }

        private void UploadFromCpuParticles()
        {
            if (!IsInitialized)
                return;

            _uploadWatch.Restart();

            int stride = Mathf.Max(1, bufferConfig.uploadStride);

            _uploadedCount = paintFluidSystem.CopyParticleGpuData(
                _cpuPositionRadius,
                _cpuVelocityMass,
                _cpuColor,
                _cpuStateAgeId,
                _cpuVolumeJ,
                _cpuDeformationF0,
                _cpuDeformationF1,
                _cpuDeformationF2,
                _capacity,
                stride
            );

            if (_uploadedCount > 0)
            {
                _positionRadiusBuffer.SetData(_cpuPositionRadius, 0, 0, _uploadedCount);

                _velocityMassBuffer.SetData(_cpuVelocityMass, 0, 0, _uploadedCount);

                _colorBuffer.SetData(_cpuColor, 0, 0, _uploadedCount);

                _stateAgeIdBuffer.SetData(_cpuStateAgeId, 0, 0, _uploadedCount);

                ////////// G6.A Changes //////////

                _volumeJBuffer.SetData(_cpuVolumeJ, 0, 0, _uploadedCount);

                _deformationF0Buffer.SetData(_cpuDeformationF0, 0, 0, _uploadedCount);

                _deformationF1Buffer.SetData(_cpuDeformationF1, 0, 0, _uploadedCount);

                _deformationF2Buffer.SetData(_cpuDeformationF2, 0, 0, _uploadedCount);

                ////////// End G6.A Changes //////////
            }

            ////////// G5 Changes //////////
            // During CPU → GPU bootstrap, APIC affine C starts as zero.
            // The GPU solver will update these buffers after simulation begins.
            if (_uploadedCount > 0 && !_externalGpuSimulationMode)
            {
                _affineC0Buffer.SetData(_cpuAffineC0, 0, 0, _uploadedCount);
                _affineC1Buffer.SetData(_cpuAffineC1, 0, 0, _uploadedCount);
                _affineC2Buffer.SetData(_cpuAffineC2, 0, 0, _uploadedCount);
            }
            ////////// End G5 Changes //////////

            _lastUploadFrame = Time.frameCount;

            _uploadWatch.Stop();
        }

        private void DispatchDebugColorByState()
        {
            if (!IsInitialized ||
                utilityCompute == null ||
                _kernelDebugColorByState < 0 ||
                _uploadedCount <= 0)
            {
                return;
            }

            _computeWatch.Restart();

            utilityCompute.SetInt("_ParticleCount", _uploadedCount);

            utilityCompute.SetVector("_InsideColor", bufferConfig.insideColor);
            utilityCompute.SetVector("_NearHoleColor", bufferConfig.nearHoleColor);
            utilityCompute.SetVector("_AirborneColor", bufferConfig.airborneColor);
            utilityCompute.SetVector("_DepositedColor", bufferConfig.depositedColor);
            utilityCompute.SetVector("_FallbackColor", bufferConfig.fallbackColor);

            utilityCompute.SetBuffer(
                _kernelDebugColorByState,
                "_ParticleColor",
                _colorBuffer
            );

            utilityCompute.SetBuffer(
                _kernelDebugColorByState,
                "_ParticleStateAgeId",
                _stateAgeIdBuffer
            );

            int groups = Mathf.CeilToInt(_uploadedCount / 256.0f);
            utilityCompute.Dispatch(_kernelDebugColorByState, groups, 1, 1);

            _computeWatch.Stop();
        }

        private void UpdateStats()
        {
            _stats.initialized = IsInitialized;
            _stats.enabled = bufferConfig != null && bufferConfig.enableGpuFluidBuffers;

            _stats.capacity = _capacity;
            _stats.uploadedParticles = _uploadedCount;
            _stats.uploadStride = bufferConfig != null ? bufferConfig.uploadStride : 1;
            _stats.uploadFrame = _lastUploadFrame;

            _stats.cpuUploadMilliseconds = (float)_uploadWatch.Elapsed.TotalMilliseconds;
            _stats.computePostProcessMilliseconds = (float)_computeWatch.Elapsed.TotalMilliseconds;

            _stats.positionRadiusBufferReady = _positionRadiusBuffer != null;
            _stats.velocityMassBufferReady = _velocityMassBuffer != null;
            _stats.colorBufferReady = _colorBuffer != null;
            _stats.stateAgeIdBufferReady = _stateAgeIdBuffer != null;

            _stats.computePostProcessEnabled =
                bufferConfig != null && bufferConfig.enableComputePostProcess;

            _stats.debugColorByStateEnabled =
                bufferConfig != null && bufferConfig.debugColorByStateOnGpu;

            //////////// G5 Changes //////////
            _stats.affineC0BufferReady = _affineC0Buffer != null;
            _stats.affineC1BufferReady = _affineC1Buffer != null;
            _stats.affineC2BufferReady = _affineC2Buffer != null;
            //////////// End G5 Changes //////////

            //////////// G6.A Changes //////////
            _stats.volumeJBufferReady = _volumeJBuffer != null;
            _stats.deformationF0BufferReady = _deformationF0Buffer != null;
            _stats.deformationF1BufferReady = _deformationF1Buffer != null;
            _stats.deformationF2BufferReady = _deformationF2Buffer != null;
            //////////// End G6.A Changes //////////

        }

        private void ReleaseBuffers()
        {
            if (_positionRadiusBuffer != null)
            {
                _positionRadiusBuffer.Release();
                _positionRadiusBuffer = null;
            }

            if (_velocityMassBuffer != null)
            {
                _velocityMassBuffer.Release();
                _velocityMassBuffer = null;
            }

            if (_colorBuffer != null)
            {
                _colorBuffer.Release();
                _colorBuffer = null;
            }

            if (_stateAgeIdBuffer != null)
            {
                _stateAgeIdBuffer.Release();
                _stateAgeIdBuffer = null;
            }

            ////////////// G5 Changes ////////////
            if (_affineC0Buffer != null)
            {
                _affineC0Buffer.Release();
                _affineC0Buffer = null;
            }

            if (_affineC1Buffer != null)
            {
                _affineC1Buffer.Release();
                _affineC1Buffer = null;
            }

            if (_affineC2Buffer != null)
            {
                _affineC2Buffer.Release();
                _affineC2Buffer = null;
            }

            _cpuAffineC0 = null;
            _cpuAffineC1 = null;
            _cpuAffineC2 = null;

            ////////////// End G5 Changes ////////////

            ////////////// G6.A Changes ////////////

            if (_volumeJBuffer != null)
            {
                _volumeJBuffer.Release();
                _volumeJBuffer = null;
            }

            if (_deformationF0Buffer != null)
            {
                _deformationF0Buffer.Release();
                _deformationF0Buffer = null;
            }

            if (_deformationF1Buffer != null)
            {
                _deformationF1Buffer.Release();
                _deformationF1Buffer = null;
            }

            if (_deformationF2Buffer != null)
            {
                _deformationF2Buffer.Release();
                _deformationF2Buffer = null;
            }

            _cpuVolumeJ = null;
            _cpuDeformationF0 = null;
            _cpuDeformationF1 = null;
            _cpuDeformationF2 = null;

            ////////////// End G6.A Changes ////////////

            _cpuPositionRadius = null;
            _cpuVelocityMass = null;
            _cpuColor = null;
            _cpuStateAgeId = null;

            _capacity = 0;
            _uploadedCount = 0;
            _lastUploadFrame = -1;

            _stats = default;
        }

        public void SetExternalGpuSimulationMode(bool enabled)
        {
            _externalGpuSimulationMode = enabled;
        }

        public void EnsureBuffersPublic()
        {
            EnsureBuffers();
        }

        public void UploadFromCpuParticlesNow()
        {
            EnsureBuffers();
            UploadFromCpuParticles();
        }

        public void SetUploadedParticleCount(int count)
        {
            _uploadedCount = Mathf.Clamp(count, 0, _capacity);
            _lastUploadFrame = Time.frameCount;
            UpdateStats();
        }
    }
}