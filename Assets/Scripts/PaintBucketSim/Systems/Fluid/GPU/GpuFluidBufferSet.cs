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
        public PaintFluidConfig FluidConfig =>
            paintFluidSystem != null ? paintFluidSystem.FluidConfig : null;
        public ComputeShader UtilityCompute => utilityCompute;

        public GraphicsBuffer PositionRadiusBuffer => _positionRadiusBuffer;
        public GraphicsBuffer VelocityMassBuffer => _velocityMassBuffer;
        public GraphicsBuffer ColorBuffer => _colorBuffer;
        public GraphicsBuffer StateAgeIdBuffer => _stateAgeIdBuffer;

        private GraphicsBuffer _affineC0Buffer;
        private GraphicsBuffer _affineC1Buffer;
        private GraphicsBuffer _affineC2Buffer;

        private Vector4[] _cpuAffineC0;
        private Vector4[] _cpuAffineC1;
        private Vector4[] _cpuAffineC2;

        public GraphicsBuffer AffineC0Buffer => _affineC0Buffer;
        public GraphicsBuffer AffineC1Buffer => _affineC1Buffer;
        public GraphicsBuffer AffineC2Buffer => _affineC2Buffer;

        private GraphicsBuffer _volumeJBuffer;

        private Vector4[] _cpuVolumeJ;

        public GraphicsBuffer VolumeJBuffer => _volumeJBuffer;

        public int UploadedParticleCount => _uploadedCount;
        public int Capacity => _capacity;
        public bool MpmParticlesUseBucketLocalSpace { get; private set; }

        public bool IsInitialized =>
            _positionRadiusBuffer != null &&
            _velocityMassBuffer != null &&
            _colorBuffer != null &&
            _stateAgeIdBuffer != null &&
            _affineC0Buffer != null &&
            _affineC1Buffer != null &&
            _affineC2Buffer != null &&
            _volumeJBuffer != null
            ;

        public GpuFluidBufferStats Stats => _stats;

        private bool _externalGpuSimulationMode;

        private void Awake()
        {
            if (paintFluidSystem == null)
                paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

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

            int requestedCapacity = FluidConfig != null
                ? FluidConfig.ParticleCapacity
                : 1;

            if (IsInitialized && _capacity == requestedCapacity)
                return;

            ReleaseBuffers();

            _capacity = requestedCapacity;

            _cpuPositionRadius = new Vector4[_capacity];
            _cpuVelocityMass = new Vector4[_capacity];
            _cpuColor = new Vector4[_capacity];
            _cpuStateAgeId = new Vector4[_capacity];

            _cpuAffineC0 = new Vector4[_capacity];
            _cpuAffineC1 = new Vector4[_capacity];
            _cpuAffineC2 = new Vector4[_capacity];

            _cpuVolumeJ = new Vector4[_capacity];

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

            _volumeJBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _capacity,
                sizeof(float) * 4
            );

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
            MpmParticlesUseBucketLocalSpace = false;

            _uploadedCount = paintFluidSystem.CopyParticleGpuData(
                _cpuPositionRadius,
                _cpuVelocityMass,
                _cpuColor,
                _cpuStateAgeId,
                _cpuVolumeJ,
                _capacity,
                1
            );

            if (_uploadedCount > 0)
            {
                _positionRadiusBuffer.SetData(_cpuPositionRadius, 0, 0, _uploadedCount);

                _velocityMassBuffer.SetData(_cpuVelocityMass, 0, 0, _uploadedCount);

                _colorBuffer.SetData(_cpuColor, 0, 0, _uploadedCount);

                _stateAgeIdBuffer.SetData(_cpuStateAgeId, 0, 0, _uploadedCount);

                _volumeJBuffer.SetData(_cpuVolumeJ, 0, 0, _uploadedCount);
            }

            // During CPU → GPU bootstrap, APIC affine C starts as zero.
            // The GPU solver will update these buffers after simulation begins.
            if (_uploadedCount > 0 && !_externalGpuSimulationMode)
            {
                _affineC0Buffer.SetData(_cpuAffineC0, 0, 0, _uploadedCount);
                _affineC1Buffer.SetData(_cpuAffineC1, 0, 0, _uploadedCount);
                _affineC2Buffer.SetData(_cpuAffineC2, 0, 0, _uploadedCount);
            }

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
            utilityCompute.SetVector("_FallbackColor", bufferConfig.defaultStateColor);

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
            _stats.uploadStride = 1;
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

            _stats.affineC0BufferReady = _affineC0Buffer != null;
            _stats.affineC1BufferReady = _affineC1Buffer != null;
            _stats.affineC2BufferReady = _affineC2Buffer != null;

            _stats.volumeJBufferReady = _volumeJBuffer != null;

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

            if (_volumeJBuffer != null)
            {
                _volumeJBuffer.Release();
                _volumeJBuffer = null;
            }

            _cpuVolumeJ = null;

            _cpuPositionRadius = null;
            _cpuVelocityMass = null;
            _cpuColor = null;
            _cpuStateAgeId = null;

            _capacity = 0;
            _uploadedCount = 0;
            _lastUploadFrame = -1;
            MpmParticlesUseBucketLocalSpace = false;

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

        public void ReleaseBuffersPublic()
        {
            ReleaseBuffers();
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

        public void SetMpmParticlesUseBucketLocalSpace(bool enabled)
        {
            MpmParticlesUseBucketLocalSpace = enabled;
        }
    }
}
