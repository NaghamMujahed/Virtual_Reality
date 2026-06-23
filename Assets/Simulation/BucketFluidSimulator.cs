using UnityEngine;
using System.Collections.Generic;

namespace Simulation
{
    /// <summary>
    /// محاكاة بسيطة للسائل داخل الدلو
    /// يستخدم Particle System للتمثيل المرئي
    /// </summary>
    [RequireComponent(typeof(BucketController))]
    public class BucketFluidSimulator : MonoBehaviour
    {
        [Header("Fluid Configuration")]
        [SerializeField] float _fluidLevel = 0.5f; // مستوى السائل (0-1)
        [SerializeField] float _maxFluidLevel = 0.8f; // الحد الأقصى
        [SerializeField] Color _fluidColor = new Color(0.8f, 0.6f, 0.2f, 0.9f);
        [SerializeField] int _particleCount = 100;
        [SerializeField] float _particleSize = 0.02f;

        [Header("Physics")]
        [SerializeField] float _gravity = -9.81f;
        [SerializeField] float _viscosity = 0.95f;
        [SerializeField] float _sloshSpeed = 2f;

        private BucketController _bucket;
        private List<FluidParticle> _particles = new List<FluidParticle>();
        private MeshRenderer _fluidRenderer;
        private Material _fluidMaterial;

        [System.Serializable]
        private class FluidParticle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float mass;
        }

        void Awake()
        {
            _bucket = GetComponent<BucketController>();
            InitializeFluid();
        }

        void InitializeFluid()
        {
            // إنشاء Material للسائل
            _fluidMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _fluidMaterial.color = _fluidColor;
            _fluidMaterial.SetFloat("_Smoothness", 0.3f);
            _fluidMaterial.SetFloat("_Metallic", 0.1f);

            // إنشاء جسيمات السائل
            float bucketRadius = 0.13f; // نصف قطر قاعدة الدلو
            float bucketHeight = 0.32f;
            float fluidHeight = bucketHeight * _fluidLevel;

            for (int i = 0; i < _particleCount; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(0f, bucketRadius * 0.9f);
                float height = Random.Range(-bucketHeight * 0.5f, -bucketHeight * 0.5f + fluidHeight);

                _particles.Add(new FluidParticle
                {
                    position = new Vector3(
                        Mathf.Cos(angle) * radius,
                        height,
                        Mathf.Sin(angle) * radius
                    ),
                    velocity = Vector3.zero,
                    mass = 1f
                });
            }
        }

        void Update()
        {
            if (_bucket == null) return;

            // محاكاة بسيطة للسائل
            SimulateFluid();
        }

        void SimulateFluid()
        {
            float bucketRadius = 0.13f;
            float bucketHeight = 0.32f;
            float bottomY = -bucketHeight * 0.5f;

            foreach (var particle in _particles)
            {
                // تطبيق الجاذبية
                particle.velocity.y += _gravity * Time.deltaTime;

                // تطبيق اللزوجة
                particle.velocity *= _viscosity;

                // تحديث الموقع
                particle.position += particle.velocity * Time.deltaTime;

                // اصطدام بالقاع
                if (particle.position.y < bottomY + _particleSize)
                {
                    particle.position.y = bottomY + _particleSize;
                    particle.velocity.y *= -0.3f; // ارتداد
                }

                // اصطدام بالجدران
                float distFromCenter = Mathf.Sqrt(
                    particle.position.x * particle.position.x + 
                    particle.position.z * particle.position.z
                );

                if (distFromCenter > bucketRadius - _particleSize)
                {
                    float angle = Mathf.Atan2(particle.position.z, particle.position.x);
                    particle.position.x = Mathf.Cos(angle) * (bucketRadius - _particleSize);
                    particle.position.z = Mathf.Sin(angle) * (bucketRadius - _particleSize);
                    
                    // ارتداد من الجدران
                    Vector3 normal = new Vector3(
                        Mathf.Cos(angle), 0f, Mathf.Sin(angle)
                    );
                    particle.velocity = Vector3.Reflect(particle.velocity, normal) * 0.5f;
                }

                // Sloshing effect (تأرجح السائل)
                float sloshForce = Mathf.Sin(Time.time * _sloshSpeed) * 0.1f;
                particle.velocity.x += sloshForce * Time.deltaTime;
            }
        }

        void OnDrawGizmos()
        {
            if (_particles == null || _particles.Count == 0) return;

            Gizmos.color = _fluidColor;
            foreach (var particle in _particles)
            {
                Vector3 worldPos = transform.TransformPoint(particle.position);
                Gizmos.DrawSphere(worldPos, _particleSize);
            }
        }
    }
}