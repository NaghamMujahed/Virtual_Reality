using PaintBucketSim.Core;
using PaintBucketSim.Configs;
using UnityEngine;

namespace PaintBucketSim.Demo
{
    public class FluidTankWaveMotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SimulationManager simulationManager;

        [Header("Wave Motion")]
        [SerializeField] private bool enableWaveMotion = true;

        [Tooltip("قوة دفع الطلاء يمين ويسار. ابدأ بين 2 و 5.")]
        [SerializeField] private float horizontalAcceleration = 3.5f;

        [Tooltip("سرعة تبدل الحركة. ابدأ بين 0.3 و 0.8.")]
        [SerializeField] private float frequency = 0.45f;

        [Tooltip("اتجاه الموجة داخل الحوض.")]
        [SerializeField] private Vector3 waveDirection = Vector3.right;

        private EnvironmentConfig _environmentConfig;
        private Vector3 _originalGravity;
        private float _time;

        private void Start()
        {
            if (simulationManager == null)
                simulationManager = FindAnyObjectByType<SimulationManager>();

            if (simulationManager == null || simulationManager.Context == null)
            {
                Debug.LogError("FluidTankWaveMotion: SimulationManager or Context not found.");
                enabled = false;
                return;
            }

            _environmentConfig = simulationManager.Context.EnvironmentConfig;

            if (_environmentConfig == null)
            {
                Debug.LogError("FluidTankWaveMotion: EnvironmentConfig not found.");
                enabled = false;
                return;
            }

            _originalGravity = _environmentConfig.gravity;
            waveDirection.y = 0.0f;
            waveDirection.Normalize();

            if (waveDirection.sqrMagnitude < 0.001f)
                waveDirection = Vector3.right;
        }

        private void Update()
        {
            if (_environmentConfig == null)
                return;

            if (!enableWaveMotion)
            {
                _environmentConfig.gravity = _originalGravity;
                return;
            }

            _time += Time.deltaTime;

            float wave =
                Mathf.Sin(_time * frequency * Mathf.PI * 2.0f) *
                horizontalAcceleration;

            Vector3 sideGravity = waveDirection * wave;

            _environmentConfig.gravity = _originalGravity + sideGravity;
        }

        private void OnDisable()
        {
            if (_environmentConfig != null)
                _environmentConfig.gravity = _originalGravity;
        }
    }
}