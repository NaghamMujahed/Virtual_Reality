using UnityEngine;

namespace PaintBucketSim.Configs
{
    /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// 

    public enum GpuBucketTopMode
    {
        Open = 0,
        TemporaryLid = 1,
        MarkSpilled = 2
    }
    /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// 

    [CreateAssetMenu(
        fileName = "GpuMpmSolverConfig",
        menuName = "Paint Bucket Sim/GPU MPM Solver Config")]
    public class GpuMpmSolverConfig : ScriptableObject
    {
        [Header("Compute")]
        public ComputeShader denseLocalMpmCompute;

        [Header("Dense Local Grid")]
        public Vector3 gridOriginWorld = new Vector3(-2.0f, -2.0f, -2.0f);

        public Vector3Int gridResolution = new Vector3Int(32, 32, 32);

        [Min(0.005f)]
        public float cellSizeMeters = 0.08f;

        [Header("Simulation")]
        public bool enableGpuDenseMpm = true;

        [Tooltip("Gravity multiplier inside GPU MPM prototype.")]
        [Range(0.0f, 2.0f)]
        public float gravityScale = 1.0f;

        [Tooltip("Velocity damping used only for this early prototype.")]
        [Range(0.0f, 5.0f)]
        public float velocityDampingPerSecond = 0.05f;

        [Tooltip("Maximum particle speed in m/s.")]
        [Min(0.1f)]
        public float maxParticleSpeed = 10.0f;

        [Header("PIC/MPM Transfer")]
        [Tooltip("0 = keep particle velocity more, 1 = pure PIC grid velocity. For first prototype use 1.")]
        [Range(0.0f, 1.0f)]
        public float picBlend = 1.0f;

        [Tooltip("Fixed point scale for mass atomic adds.")]
        [Min(1000)]
        public int massFixedScale = 1000000;

        [Tooltip("Fixed point scale for momentum atomic adds.")]
        [Min(1000)]
        public int momentumFixedScale = 1000000;

        [Header("Box Boundary Prototype")]
        public bool enableBoxBoundary = true;

        [Range(0.0f, 1.0f)]
        public float boundaryDamping = 0.2f;

        [Header("Debug")]
        public bool logLifecycle = true;

        // G5 Changes //
        [Header("APIC Transfer")]
        public bool enableApicTransfer = true;

        [Tooltip("How strongly APIC affine velocity contributes during P2G. 0 = PIC only, 1 = full APIC contribution.")]
        [Range(0.0f, 1.0f)]
        public float apicP2GStrength = 1.0f;

        [Tooltip("How strongly the reconstructed affine C matrix is written back in G2P.")]
        [Range(0.0f, 1.0f)]
        public float apicG2PStrength = 1.0f;

        [Tooltip("Damping applied to affine C each step to prevent early prototype instability.")]
        [Range(0.0f, 1.0f)]
        public float affineDamping = 0.02f;

        [Tooltip("Clamp affine C values to avoid explosion in early prototype.")]
        [Min(0.1f)]
        public float maxAffineMagnitude = 25.0f;

        [Tooltip("Approximate inverse D matrix scale. For linear prototype, 4 / dx² is a useful starting approximation.")]
        public bool useAutomaticApicDInverse = true;

        [Min(0.0001f)]
        public float manualApicDInverse = 625.0f;
        // End G5 Changes //

        ////////////////    G6.A Changes   //////////////////

        [Header("MLS/MPM Material Prototype")]
        public bool enableMaterialStress = true;

        [Tooltip("Simple bulk modulus used for weakly-compressible paint prototype. Start low for stability.")]
        [Min(0.0f)]
        public float bulkModulus = 1500.0f;

        [Tooltip("Simple viscous stress coefficient. This is not the final paint rheology yet.")]
        [Min(0.0f)]
        public float mpmViscosity = 0.2f;

        [Tooltip("Global multiplier for stress contribution during P2G.")]
        [Range(0.0f, 1.0f)]
        public float materialStressStrength = 0.25f;

        [Tooltip("Clamp stress components to prevent early prototype explosions.")]
        [Min(1.0f)]
        public float maxStressMagnitude = 5000.0f;

        [Tooltip("Clamp determinant J to keep the prototype stable.")]
        [Range(0.1f, 1.0f)]
        public float minJ = 0.4f;

        [Range(1.0f, 3.0f)]
        public float maxJ = 1.8f;

        [Tooltip("Clamp deformation gradient entries.")]
        [Min(1.0f)]
        public float maxDeformationGradientValue = 5.0f;

        ////////////////    End G6.A Changes   //////////////////

        ////////////////    G7 Changes   //////////////////
        [Header("Paint Rheology / Non-Newtonian Prototype")]
        public bool enablePaintRheology = true;

        [Tooltip("Low-shear viscosity. Higher means the paint resists slow motion more.")]
        [Min(0.0f)]
        public float lowShearViscosity = 2.0f;

        [Tooltip("High-shear viscosity. Lower than lowShearViscosity for shear-thinning paint.")]
        [Min(0.0f)]
        public float highShearViscosity = 0.15f;

        [Tooltip("Controls when shear-thinning becomes visible. Higher = stronger dependence on shear rate.")]
        [Min(0.0f)]
        public float shearThinningRelaxationTime = 0.8f;

        [Tooltip("Flow index. n < 1 means shear-thinning. Start around 0.45 to 0.8.")]
        [Range(0.05f, 1.0f)]
        public float shearThinningPowerN = 0.55f;

        [Tooltip("Yield-like stress. Higher means the paint behaves more resistant at very low shear.")]
        [Min(0.0f)]
        public float yieldStress = 0.0f;

        [Tooltip("Clamp for yield-derived viscosity to avoid explosion.")]
        [Min(0.0f)]
        public float maxYieldViscosityContribution = 5.0f;

        [Tooltip("Final clamp for effective viscosity.")]
        [Min(0.001f)]
        public float maxEffectiveViscosity = 8.0f;

        [Tooltip("Blend between old constant viscosity and advanced rheology. 0 = old viscosity, 1 = full rheology.")]
        [Range(0.0f, 1.0f)]
        public float rheologyStrength = 1.0f;
        ////////////////    End G7 Changes   //////////////////

        /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// 
        [Header("GPU Bucket Collision")]
        public bool enableGpuBucketCollision = true;

        [Tooltip("Use the real BucketSystem transform and dimensions instead of the simple box boundary.")]
        public bool useRealBucketCollision = true;

        [Tooltip("Small inward padding to reduce jitter at walls.")]
        [Min(0.0f)]
        public float bucketCollisionPadding = 0.005f;

        [Tooltip("Velocity bounce after collision. 0 = no bounce, 1 = fully elastic.")]
        [Range(0.0f, 1.0f)]
        public float bucketRestitution = 0.05f;

        [Tooltip("Tangential damping/friction at bucket walls. 0 = no friction, 1 = remove tangential velocity.")]
        [Range(0.0f, 1.0f)]
        public float bucketFriction = 0.25f;

        public GpuBucketTopMode topBoundaryMode = GpuBucketTopMode.TemporaryLid;

        [Min(0.0f)]
        public float topBoundaryPadding = 0.01f;

        [Header("GPU Hole Region - Prototype")]
        public bool classifyBottomHoleRegion = true;

        [Tooltip("If true, particles inside the bottom hole are not blocked by the bottom. Keep false until G9 outflow.")]
        public bool enableBottomHoleOpening = false;

        [Range(0.5f, 2.0f)]
        public float holeRadiusMultiplier = 1.0f;

        [Min(0.0f)]
        public float holeNearHeightMeters = 0.08f;

        [Min(0.0f)]
        public float holeRadialPaddingMeters = 0.02f;
        /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// 

        /// /// /// /// /// /// /// <G8.B Changes> /// /// /// /// /// /// 
        [Header("Moving Bucket Boundary Velocity")]
        public bool enableMovingBucketBoundaryVelocity = true;

        [Tooltip("How strongly the moving bucket wall velocity affects particles. 1 = full physical wall velocity.")]
        [Range(0.0f, 1.0f)]
        public float bucketBoundaryVelocityStrength = 1.0f;

        [Tooltip("Clamp wall velocity contribution to avoid extreme impulses during early testing.")]
        [Min(0.1f)]
        public float maxBucketBoundaryVelocity = 8.0f;
        /// /// /// /// /// /// /// <End G8.B Changes> /// /// /// /// /// /// 
        public int GridNodeCount =>
            gridResolution.x * gridResolution.y * gridResolution.z;

        public Vector3 GridSizeWorld =>
            new Vector3(
                gridResolution.x * cellSizeMeters,
                gridResolution.y * cellSizeMeters,
                gridResolution.z * cellSizeMeters
            );

        public Vector3 GridMaxWorld => gridOriginWorld + GridSizeWorld;

        private void OnValidate()
        {
            gridResolution.x = Mathf.Max(4, gridResolution.x);
            gridResolution.y = Mathf.Max(4, gridResolution.y);
            gridResolution.z = Mathf.Max(4, gridResolution.z);

            if (cellSizeMeters < 0.005f)
                cellSizeMeters = 0.005f;

            if (maxParticleSpeed < 0.1f)
                maxParticleSpeed = 0.1f;

            if (massFixedScale < 1000)
                massFixedScale = 1000;

            if (momentumFixedScale < 1000)
                momentumFixedScale = 1000;

            // G5 Changes //
            if (maxAffineMagnitude < 0.1f)
                maxAffineMagnitude = 0.1f;

            if (manualApicDInverse < 0.0001f)
                manualApicDInverse = 0.0001f;
            // End G5 Changes //

            ////////////////    G6.A Changes   //////////////////

            if (bulkModulus < 0.0f)
                bulkModulus = 0.0f;

            if (mpmViscosity < 0.0f)
                mpmViscosity = 0.0f;

            if (maxStressMagnitude < 1.0f)
                maxStressMagnitude = 1.0f;

            if (maxDeformationGradientValue < 1.0f)
                maxDeformationGradientValue = 1.0f;

            minJ = Mathf.Clamp(minJ, 0.1f, 1.0f);
            maxJ = Mathf.Clamp(maxJ, 1.0f, 3.0f);

            ////////////////    End G6.A Changes   //////////////////

            ////////////////    G7 Changes   //////////////////

            if (lowShearViscosity < 0.0f)
                lowShearViscosity = 0.0f;

            if (highShearViscosity < 0.0f)
                highShearViscosity = 0.0f;

            if (shearThinningRelaxationTime < 0.0f)
                shearThinningRelaxationTime = 0.0f;

            shearThinningPowerN = Mathf.Clamp(shearThinningPowerN, 0.05f, 1.0f);

            if (yieldStress < 0.0f)
                yieldStress = 0.0f;

            if (maxYieldViscosityContribution < 0.0f)
                maxYieldViscosityContribution = 0.0f;

            if (maxEffectiveViscosity < 0.001f)
                maxEffectiveViscosity = 0.001f;

            if (highShearViscosity > lowShearViscosity)
                highShearViscosity = lowShearViscosity;

            ////////////////    End G7 Changes   //////////////////

            /// /// /// /// /// /// /// <G8.A Changes> /// /// /// /// /// /// 

            if (bucketCollisionPadding < 0.0f)
                bucketCollisionPadding = 0.0f;

            if (topBoundaryPadding < 0.0f)
                topBoundaryPadding = 0.0f;

            if (holeNearHeightMeters < 0.0f)
                holeNearHeightMeters = 0.0f;

            if (holeRadialPaddingMeters < 0.0f)
                holeRadialPaddingMeters = 0.0f;
            /// /// /// /// /// /// /// <End G8.A Changes> /// /// /// /// /// /// 

            /// /// /// /// /// /// /// <G8.B Changes> /// /// /// /// /// /// 
            if (maxBucketBoundaryVelocity < 0.1f)
                maxBucketBoundaryVelocity = 0.1f;
            /// /// /// /// /// /// /// <End G8.B Changes> /// /// /// /// /// /// 
        }
    }
}