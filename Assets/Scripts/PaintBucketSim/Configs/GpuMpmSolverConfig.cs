using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum GpuBucketTopMode
    {
        Open = 0,
        TemporaryLid = 1,
        MarkSpilled = 2
    }

    [CreateAssetMenu(
        fileName = "GpuMpmSolverConfig",
        menuName = "Paint Bucket Sim/GPU MPM Solver Config")]
    public class GpuMpmSolverConfig : ScriptableObject
    {
        [Header("Compute")]
        public ComputeShader denseLocalMpmCompute;

        [Header("Bucket-Local Grid")]
        public Vector3 gridOriginLocal =
            new Vector3(-0.24f, -0.40f, -0.24f);

        public Vector3Int gridResolution = new Vector3Int(48, 72, 48);

        [Min(0.005f)]
        public float cellSizeMeters = 0.08f;

        [Tooltip("Required local-space margin between the bucket and the transfer-grid boundary.")]
        [Min(0.0f)]
        public float gridBoundaryMarginMeters = 0.02f;

        [Tooltip("Stop the GPU step when the configured grid cannot contain the rotating bucket plus transfer stencil.")]
        public bool rejectUndersizedGrid = true;

        [Tooltip("Automatically grow the grid resolution at initialization so the configured cell size always contains the bucket + transfer support + hole jet-collars. gridResolution above is then only a lower bound. Prevents the 'grid does not contain the bucket' error when the bucket is enlarged.")]
        public bool autoSizeBucketLocalGrid = true;

        [Tooltip("Upper clamp on the auto-sized resolution per axis, to bound GPU memory. If the bucket needs more than this, raise cellSizeMeters or this cap.")]
        [Range(16, 256)]
        public int maxAutoGridResolution = 128;

        [Tooltip("Extra cells of headroom added on each side when auto-sizing the grid.")]
        [Range(0, 8)]
        public int autoGridResolutionMargin = 2;

        [Header("Bucket-Local Non-Inertial Frame")]
        [Tooltip("Half-life used to suppress XPBD acceleration noise without damping physical fluid velocity.")]
        [Min(0.0f)]
        public float bucketFrameAccelerationFilterHalfLife = 0.025f;

        [Min(1.0f)]
        public float maxBucketFrameLinearAcceleration = 80.0f;

        [Min(1.0f)]
        public float maxBucketFrameAngularAcceleration = 240.0f;

        [Min(0.01f)]
        public float bucketFrameTeleportDistanceMeters = 0.35f;

        [Range(1.0f, 180.0f)]
        public float bucketFrameTeleportAngleDegrees = 55.0f;

        [Header("Simulation")]
        public bool enableGpuDenseMpm = true;

        [Tooltip("Gravity multiplier inside the GPU MPM solver.")]
        [Range(0.0f, 2.0f)]
        public float gravityScale = 1.0f;

        [Tooltip("Velocity damping applied inside the GPU MPM solver.")]
        [Range(0.0f, 5.0f)]
        public float velocityDampingPerSecond = 0.05f;

        [Tooltip("Maximum particle speed in m/s.")]
        [Min(0.1f)]
        public float maxParticleSpeed = 10.0f;

        [Header("Final Performance Transfer LOD")]
        [Tooltip("Use a cheaper 2x2x2 linear transfer stencil for calm interior particles while preserving the full 3x3x3 MLS/APIC stencil for priority particles near surfaces, walls, holes, or fast motion.")]
        public bool enableAdaptiveTransferStencil = true;

        [Tooltip("Also use the cheaper linear stencil in G2P. Off by default because full quadratic G2P better preserves APIC quality while P2G-only LOD captures most of the performance gain.")]
        public bool enableAdaptiveG2PTransferStencil = false;

        [Tooltip("Minimum uploaded particle count before adaptive transfer LOD is enabled. Smaller scenes keep the full-quality path because LOD overhead is not worthwhile there.")]
        [Min(0)]
        public int adaptiveTransferMinParticles = 200000;

        [Tooltip("Rebuild hybrid-P2G owner ordering every N substeps. Reused lists stay complete while previous owner tiles are explicitly kept active. Use 1 to rebuild every substep.")]
        [Range(1, 2)]
        public int ownerTileListRebuildInterval = 2;

        [Tooltip("Minimum uploaded particle count before owner-list reuse is worthwhile. Smaller simulations rebuild every substep to avoid stale-owner atomic spill.")]
        [Min(0)]
        public int ownerTileListReuseMinParticles = 350000;

        [Tooltip("Tile edge length in grid cells. Kept power-of-two for fast shader tile addressing.")]
        [Range(4, 8)]
        public int mpmTileSizeCells = 8;

        [Header("PIC/MPM Transfer")]
        [Tooltip("Fixed point scale for mass atomic adds.")]
        [Min(1000)]
        public int massFixedScale = 1000000;

        [Tooltip("Fixed point scale for momentum atomic adds.")]
        [Min(1000)]
        public int momentumFixedScale = 1000000;

        [Header("Debug")]
        public bool logLifecycle = true;

        [Tooltip("Enable compact GPU counters and asynchronous readback for solver validation.")]
        public bool enableGpuDiagnostics = true;

        [Tooltip("Validation only: synchronize the GPU at major solver boundaries to measure broad stage costs. Never enable for normal play.")]
        public bool enableGpuStageProfiling = false;

        [Min(1)]
        public int diagnosticsReadbackInterval = 15;

        [Header("MLS-MPM Affine Transfer")]
        [Tooltip("Damping applied to affine C each step for numerical stability.")]
        [Range(0.0f, 1.0f)]
        public float affineDamping = 0.02f;

        [Tooltip("Clamp affine C values for numerical stability.")]
        [Min(0.1f)]
        public float maxAffineMagnitude = 25.0f;

        [Tooltip("Approximate inverse D matrix scale. 4 / dx² is a useful starting approximation.")]
        public bool useAutomaticApicDInverse = true;

        [Min(0.0001f)]
        public float manualApicDInverse = 625.0f;

        [Header("MLS-MPM Projection Grid Infrastructure")]
        [Tooltip("Run the pressure projection every N MPM substeps. 1 = every substep, 2 = every second substep.")]
        [Min(1)]
        public int projectionSubstepInterval = 1;

        [Tooltip("Build a compact GPU list of fluid projection cells and run pressure iterations only on that list.")]
        public bool enableSparseProjectionPressureDispatch = true;

        [Tooltip("Dispatch projection setup and correction kernels only over active 8^3 MPM tiles. Sparse pressure iterations remain on the compact fluid-cell list.")]
        public bool enableMpmTileProjectionDispatch = true;

        [Tooltip("Add a positive target divergence in over-dense cells so projection removes accumulated compression instead of correcting instantaneous divergence only.")]
        public bool enableProjectionDensityDriftCorrection = true;

        [Tooltip("Fraction of excess grid density corrected per projection step.")]
        [Range(0.0f, 1.0f)]
        public float projectionDensityDriftStrength = 0.8f;

        [Tooltip("Density ratio below this threshold is left untouched. Values below one commonly belong to the free surface.")]
        [Min(1.0f)]
        public float projectionDensityDriftMinRatio = 1.005f;

        [Tooltip("Maximum expansion divergence requested by density-drift correction.")]
        [Min(0.0f)]
        public float projectionDensityDriftMaxDivergence = 40.0f;

        [Header("Adaptive Multi-Rate Simulation")]
        [Tooltip("Sample particle activity on the GPU and update calm interior deformation less often while keeping P2G mass transfer at full rate.")]
        public bool enableAdaptiveMultiRate = true;

        [Tooltip("GPU activity classification cadence in MPM substeps.")]
        [Range(1, 32)]
        public int adaptiveActivitySampleInterval = 8;

        [Tooltip("Particles at or above this speed remain high priority.")]
        [Min(0.0f)]
        public float adaptivePriorityParticleSpeed = 3.0f;

        [Tooltip("Particles whose local density/deformation deviates by at least this |J-1| remain high priority for full-quality transfer.")]
        [Range(0.0f, 1.0f)]
        public float adaptivePriorityJDeviation = 1.0f;

        [Tooltip("Distance from bucket walls/bottom that remains high priority.")]
        [Min(0.0f)]
        public float adaptivePriorityBoundaryBandMeters = 0.025f;

        [Tooltip("Upper normalized bucket region always treated as a possible free surface.")]
        [Range(0.0f, 1.0f)]
        public float adaptivePriorityTopFraction = 0.6f;

        [Tooltip("Allow projection cadence to increase only after the sampled liquid and bucket remain calm.")]
        public bool enableAdaptiveProjectionCadence = true;

        [Tooltip("Projection interval used after sustained calm. Must be at least projectionSubstepInterval.")]
        [Range(1, 8)]
        public int calmProjectionSubstepInterval = 3;

        [Tooltip("Consecutive calm substeps required before entering the slower projection cadence.")]
        [Min(1)]
        public int adaptiveProjectionCalmDelaySubsteps = 32;

        [Tooltip("Maximum sampled average particle speed allowed for calm projection cadence.")]
        [Min(0.0f)]
        public float adaptiveProjectionCalmAverageSpeed = 0.7f;

        [Tooltip("Maximum high-priority fraction allowed for calm projection cadence.")]
        [Range(0.0f, 1.0f)]
        public float adaptiveProjectionCalmPriorityFraction = 0.85f;

        [Tooltip("Maximum sampled average |J-1| allowed for calm projection cadence. Compression restores active pressure updates.")]
        [Range(0.0f, 1.0f)]
        public float adaptiveProjectionMaxAverageJDeviation = 0.04f;

        [Tooltip("Bucket linear speed that immediately restores active projection cadence.")]
        [Min(0.0f)]
        public float adaptiveProjectionBucketLinearSpeed = 0.08f;

        [Tooltip("Bucket angular speed in rad/s that immediately restores active projection cadence.")]
        [Min(0.0f)]
        public float adaptiveProjectionBucketAngularSpeed = 0.25f;

        [Tooltip("Reuse a damped version of the previous pressure field as the next Jacobi initial guess.")]
        public bool enablePressureWarmStart = true;

        [Tooltip("Pressure retained between projection solves. Lower values forget stale pressure faster when the bucket moves.")]
        [Range(0.0f, 1.0f)]
        public float pressureWarmStartFactor = 0.75f;

        [Tooltip("A grid cell is considered fluid if its accumulated mass is above this value.")]
        [Min(0.0f)]
        public float minFluidCellMass = 1e-7f;

        [Tooltip("Mark bucket walls and outside-bucket regions as solid cells for projection.")]
        public bool enableBucketProjectionSolidCells = true;

        [Tooltip("If true, cells above the bucket top are treated as air. If false, top can behave like a temporary lid.")]
        public bool projectionTopOpen = true;

        [Header("MLS/MPM Material Response")]
        public bool enableMaterialStress = true;

        [Header("Bucket-Local Grid Density Predictor")]
        [Tooltip("EOS exponent used by the grid-density compression predictor.")]
        [Range(1.0f, 8.0f)]
        public float referenceEosExponent = 5.0f;

        [Tooltip("Do not allow tensile/negative predictor pressure.")]
        public bool referenceClampNegativePressure = true;

        [Tooltip("Pressure scale for the grid-density predictor. Sparse SOR remains the authoritative incompressibility solve.")]
        [Min(0.0f)]
        public float gridDensityEosPressureScale = 2.8f;

        [Tooltip("Ignore tiny grid-density fluctuations below this compression ratio. This prevents rest-state pressure chatter while bounding the allowed density drift.")]
        [Range(1.0f, 1.1f)]
        public float gridDensityEosActivationRatio = 1.01f;

        [Tooltip("Safety clamp for the velocity correction contributed by the grid EOS during one substep.")]
        [Min(0.0f)]
        public float gridDensityEosMaxVelocityCorrection = 0.2f;

        [Tooltip("Bulk modulus used for weakly-compressible paint. Start low for stability.")]
        [Min(0.0f)]
        public float bulkModulus = 1500.0f;

        [Tooltip("Global multiplier for stress contribution during P2G.")]
        [Range(0.0f, 1.0f)]
        public float materialStressStrength = 0.25f;

        [Tooltip("Clamp stress components for numerical stability.")]
        [Min(1.0f)]
        public float maxStressMagnitude = 5000.0f;

        [Tooltip("Clamp determinant J for numerical stability.")]
        [Range(0.1f, 1.0f)]
        public float minJ = 0.4f;

        [Range(1.0f, 3.0f)]
        public float maxJ = 1.8f;

        [Header("Paint Rheology Solver Response")]
        public bool enablePaintRheology = true;

        [Tooltip("Papanastasiou-style regularization rate for yield stress. Higher values make yield behavior sharper; lower values are smoother and safer.")]
        [Min(0.0f)]
        public float yieldRegularizationRate = 25.0f;

        [Tooltip("Clamp for yield-derived viscosity to avoid explosion.")]
        [Min(0.0f)]
        public float maxYieldViscosityContribution = 5.0f;

        [Tooltip("Final clamp for effective viscosity.")]
        [Min(0.001f)]
        public float maxEffectiveViscosity = 8.0f;

        [Tooltip("Blend between old constant viscosity and advanced rheology. 0 = old viscosity, 1 = full rheology.")]
        [Range(0.0f, 1.0f)]
        public float rheologyStrength = 1.0f;

        [Header("Free Surface Polish / Cohesion")]
        [Tooltip("Lightweight GPU free-surface stabilization. Uses grid mass gradients to detect exposed liquid surfaces.")]
        public bool enableFreeSurfacePolish = true;

        [Tooltip("Grid-mass gradient magnitude that maps to a fully exposed surface mask. Tune with diagnostics; higher = weaker effect.")]
        [Min(1e-6f)]
        public float freeSurfaceGradientScale = 0.08f;

        [Tooltip("Damps velocity moving out of the inferred liquid surface normal. Helps reduce lightweight flying surface particles.")]
        [Min(0.0f)]
        public float freeSurfaceNormalDampingPerSecond = 2.5f;

        [Tooltip("Small acceleration pulling exposed surface particles back toward the liquid body.")]
        [Min(0.0f)]
        public float freeSurfaceCohesionAcceleration = 0.75f;

        [Tooltip("Clamp for the total velocity correction applied by the free-surface polish step per substep.")]
        [Min(0.0f)]
        public float maxFreeSurfaceVelocityCorrection = 0.35f;

        [Header("Jet / Droplet Cohesion")]
        [Tooltip("Apply extra surface-cohesion only to NearHole/Jet particles while they are still coupled to MLS-MPM. Helps the emitted stream read as a connected paint jet instead of immediately splitting into sparse dots.")]
        public bool enableJetCohesion = true;

        [Tooltip("Additional inward cohesion acceleration for NearHole/Jet particles inferred from the free-surface mass gradient.")]
        [Min(0.0f)]
        public float jetCohesionAcceleration = 0.65f;

        [Tooltip("Additional per-substep velocity-correction clamp available only to NearHole/Jet cohesion.")]
        [Min(0.0f)]
        public float maxJetCohesionVelocityCorrection = 0.25f;

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

        [Tooltip("Run a second bucket collision pass after G2P. Safer near fast walls, but expensive with many particles.")]
        public bool enablePostG2PBucketCollision = true;

        [Tooltip("When enabled, the post-G2P bucket collision pass runs only when open holes or top spilling can create air-domain particles.")]
        public bool enableAdaptivePostG2PBucketCollision = true;

        public GpuBucketTopMode topBoundaryMode = GpuBucketTopMode.TemporaryLid;

        [Min(0.0f)]
        public float topBoundaryPadding = 0.01f;

        [Header("GPU Hole Region")]
        [Tooltip("Maximum number of active BucketConfig holes uploaded to the GPU solver.")]
        [Range(1, 64)]
        public int maxGpuBucketHoles = 16;

        [Range(0.5f, 2.0f)]
        public float holeRadiusMultiplier = 1.0f;

        [Min(0.0f)]
        public float holeNearHeightMeters = 0.08f;

        [Min(0.0f)]
        public float holeRadialPaddingMeters = 0.02f;

        [Tooltip("Distance past the hole plane before an in-bucket MPM particle becomes a jet particle.")]
        [Min(0.0f)]
        public float holeOutflowExitDistanceMeters = 0.004f;

        [Header("GPU Outflow / Airborne")]
        public bool enableAirborneParticleAdvection = true;

        [Tooltip("Skip the airborne-particle pass while holes are closed and no airborne/spilled particles have been produced.")]
        public bool enableSmartAirborneDispatch = true;

        [Min(0.0f)]
        public float airborneDragPerSecond = 0.15f;

        [Min(0.0f)]
        public float jetStateDurationSeconds = 0.08f;

        [Header("MLS-MPM Jet Collar")]
        [Tooltip("Keep newly emitted Jet particles coupled to MLS-MPM for a short region outside each hole before switching to ballistic airborne advection.")]
        public bool enableJetMpmCollar = true;

        [Tooltip("Maximum time a Jet particle remains coupled to MLS-MPM.")]
        [Min(0.0f)]
        public float jetMpmCollarDurationSeconds = 0.05f;

        [Tooltip("Maximum axial distance from the hole plane covered by the MLS-MPM jet collar.")]
        [Min(0.0f)]
        public float jetMpmCollarMaxDistanceMeters = 0.06f;

        [Tooltip("Additional radial support around each hole footprint while deciding whether a Jet particle remains in the MLS-MPM collar.")]
        [Min(0.0f)]
        public float jetMpmCollarRadialPaddingMeters = 0.02f;

        [Min(0.1f)]
        public float airborneLifetimeSeconds = 8.0f;

        [Tooltip("Particles below this world-space Y become Lost until canvas collision/deposition is implemented.")]
        public float airborneKillBelowWorldY = -2.0f;

        public bool killAirborneBelowWorldY = false;

        [Header("MLS-MPM Coherent Ballistic Jet Column (G31)")]
        [Tooltip("Keep the emitted stream collimated for a material-dependent distance after the MLS-MPM collar instead of letting it fan out into a spray immediately. Off restores the pure ballistic G19A behavior.")]
        public bool enableJetColumnCoherence = true;

        [Tooltip("Derive the coherence length and collimation strength from the authoritative PaintMaterialConfig rheology (viscosity, surface tension, yield) so the jet reads as the selected paint. Off uses the raw base values below.")]
        public bool jetCoherenceMaterialScaling = true;

        [Tooltip("Base coherent column length in meters past each hole. Material scaling multiplies this by a viscosity/surface-tension factor. Thin materials break up sooner; heavy body paint stays a rope much longer.")]
        [Min(0.0f)]
        public float jetCoherenceLengthMeters = 0.35f;

        [Tooltip("Lower clamp for the material-scaled coherence length (meters).")]
        [Min(0.0f)]
        public float jetCoherenceLengthMinMeters = 0.06f;

        [Tooltip("Upper clamp for the material-scaled coherence length (meters).")]
        [Min(0.0f)]
        public float jetCoherenceLengthMaxMeters = 1.2f;

        [Tooltip("Rate at which cross-stream (fan-out) velocity is removed inside the column. Higher = tighter, less spray.")]
        [Min(0.0f)]
        public float jetTransverseDampingPerSecond = 14.0f;

        [Tooltip("Rate at which stray particles are pulled back toward the jet centerline. Re-collimates an already spread stream.")]
        [Min(0.0f)]
        public float jetCenterlineAttractionPerSecond = 28.0f;

        [Tooltip("Clamp on the total collimation velocity correction applied per substep. Prevents the column model from injecting a visible burst.")]
        [Min(0.0f)]
        public float maxJetColumnVelocityCorrectionPerSubstep = 0.5f;

        [Tooltip("Air-drag multiplier applied inside a fully coherent column. Below 1 lets the tight column keep momentum and land with realistic force, while dispersed spray decelerates at the full airborne drag rate.")]
        [Range(0.0f, 1.0f)]
        public float jetColumnDragScale = 0.35f;

        [Tooltip("Floor for the axial speed used in the column time-of-flight estimate. Avoids divide-by-zero for nearly stalled particles.")]
        [Min(0.01f)]
        public float jetColumnMinAxialSpeed = 0.5f;

        [Header("Outflow Physics (G33)")]
        [Tooltip("Drive hole exit speed from hydrostatic head (Torricelli v=sqrt(2 g h)) modulated by a viscosity discharge coefficient, instead of only the per-hole exitVelocityBoost. Deeper fluid and thinner paint jet faster; a hole higher on the wall (less head) jets slower.")]
        public bool enableTorricelliOutflow = true;

        [Tooltip("Reference viscosity (Pa.s) for the outflow discharge coefficient. Higher keeps thick paint flowing faster relative to thin paint.")]
        [Min(0.01f)]
        public float outflowDischargeReferenceViscosity = 3.0f;

        [Tooltip("Base orifice discharge coefficient for a thin (water-like) fluid. Real orifices are about 0.6-0.98.")]
        [Range(0.1f, 1.0f)]
        public float outflowBaseDischargeCoefficient = 0.85f;

        [Tooltip("Lower clamp for the viscosity-reduced discharge coefficient, so very thick paint still oozes out.")]
        [Range(0.02f, 1.0f)]
        public float outflowMinDischargeCoefficient = 0.2f;

        [Tooltip("Clamp on the Torricelli exit speed (m/s).")]
        [Min(0.0f)]
        public float outflowMaxExitSpeed = 6.0f;

        [Tooltip("Extra padding (metres) added to the hole half-extents when deciding whether a particle may pass through / exit. 0 = the outflow diameter matches the configured hole. Negative keeps the stream strictly inside the hole.")]
        public float outflowAperturePaddingMeters = 0.0f;

        [Tooltip("Center the fixed-size bucket-local MPM grid on the bucket (plus each hole's jet-collar region) each step instead of using the fixed gridOriginLocal. Gives symmetric coverage so side-wall holes get the same jet-collar support as bottom holes.")]
        public bool autoCenterBucketLocalGrid = true;

        [Header("MLS-MPM Projection Divergence")]
        public bool enableProjectionDivergenceComputation = true;

        [Tooltip("Multiplies computed divergence. Keep 1 for physical meaning, lower for debugging.")]
        [Range(0.0f, 2.0f)]
        public float projectionDivergenceScale = 1.0f;

        [Tooltip("Clamp divergence to avoid unstable pressure solve in early stages.")]
        [Min(0.01f)]
        public float maxAbsProjectionDivergence = 50.0f;

        [Tooltip("If true, solid neighbors are treated as no-flow boundaries during divergence computation.")]
        public bool projectionSolidNoFlux = true;


        [Header("MLS-MPM Sparse Red-Black SOR Pressure Solver")]
        [Tooltip("Red-Black SOR iterations. Each iteration dispatches red and black cell passes.")]
        [Min(1)]
        public int pressureRedBlackSorIterations = 10;

        [Tooltip("Scales the pressure equation right-hand side. Lower values are safer in early tests.")]
        [Min(0.0f)]
        public float pressureRhsScale = 1.0f;

        [Tooltip("Successive over-relaxation factor for Red-Black SOR. 1 = Gauss-Seidel, >1 usually converges faster.")]
        [Range(0.05f, 1.95f)]
        public float pressureRedBlackSorOmega = 1.75f;

        [Tooltip("Clamps pressure values to avoid early solver explosions.")]
        [Min(1.0f)]
        public float maxProjectionPressure = 1000.0f;

        [Tooltip("If true, air neighbors use pressure = 0. This is the free-surface boundary condition.")]
        public bool projectionAirPressureZero = true;

        [Tooltip("If true, solid neighbors use Neumann zero-gradient behavior.")]
        public bool projectionSolidPressureNeumann = true;


        [Header("MLS-MPM Projection Pressure Gradient")]
        public bool enablePressureGradientSubtraction = true;

        [Tooltip("Scales the pressure-gradient velocity correction. Start low for stability.")]
        [Min(0.0f)]
        public float pressureGradientScale = 0.5f;

        [Tooltip("Clamp the projection velocity correction per grid node.")]
        [Min(0.01f)]
        public float maxPressureVelocityCorrection = 2.0f;

        [Tooltip("If true, flips the sign of the pressure gradient correction. Use only if projection visibly increases compression.")]
        public bool invertPressureGradientSign = false;

        [Tooltip("Apply pressure correction only to projection fluid cells.")]
        public bool pressureCorrectionFluidCellsOnly = true;

        [Tooltip("Use a staggered face-velocity projection. This keeps divergence, Jacobi, and pressure gradients consistent.")]
        public bool useStaggeredFaceProjection = true;

        public int GridNodeCount =>
            gridResolution.x * gridResolution.y * gridResolution.z;

        public Vector3 GridSizeWorld =>
            new Vector3(
                gridResolution.x * cellSizeMeters,
                gridResolution.y * cellSizeMeters,
                gridResolution.z * cellSizeMeters
            );

        public Vector3 GridMaxLocal => gridOriginLocal + GridSizeWorld;

        private void OnValidate()
        {
            gridResolution.x = Mathf.Max(4, gridResolution.x);
            gridResolution.y = Mathf.Max(4, gridResolution.y);
            gridResolution.z = Mathf.Max(4, gridResolution.z);

            if (cellSizeMeters < 0.005f)
                cellSizeMeters = 0.005f;

            maxAutoGridResolution = Mathf.Clamp(maxAutoGridResolution, 16, 256);
            autoGridResolutionMargin = Mathf.Clamp(autoGridResolutionMargin, 0, 8);

            gridBoundaryMarginMeters = Mathf.Max(
                gridBoundaryMarginMeters,
                0.0f
            );
            bucketFrameAccelerationFilterHalfLife = Mathf.Max(
                bucketFrameAccelerationFilterHalfLife,
                0.0f
            );
            maxBucketFrameLinearAcceleration = Mathf.Max(
                maxBucketFrameLinearAcceleration,
                1.0f
            );
            maxBucketFrameAngularAcceleration = Mathf.Max(
                maxBucketFrameAngularAcceleration,
                1.0f
            );
            bucketFrameTeleportDistanceMeters = Mathf.Max(
                bucketFrameTeleportDistanceMeters,
                0.01f
            );
            bucketFrameTeleportAngleDegrees = Mathf.Clamp(
                bucketFrameTeleportAngleDegrees,
                1.0f,
                180.0f
            );

            if (diagnosticsReadbackInterval < 1)
                diagnosticsReadbackInterval = 1;

            if (projectionSubstepInterval < 1)
                projectionSubstepInterval = 1;

            adaptiveActivitySampleInterval = Mathf.Clamp(
                adaptiveActivitySampleInterval,
                1,
                32
            );
            calmProjectionSubstepInterval = Mathf.Clamp(
                calmProjectionSubstepInterval,
                projectionSubstepInterval,
                8
            );
            adaptiveProjectionCalmDelaySubsteps = Mathf.Max(
                1,
                adaptiveProjectionCalmDelaySubsteps
            );
            adaptivePriorityParticleSpeed = Mathf.Max(
                0.0f,
                adaptivePriorityParticleSpeed
            );
            adaptivePriorityJDeviation = Mathf.Clamp01(
                adaptivePriorityJDeviation
            );
            adaptivePriorityBoundaryBandMeters = Mathf.Max(
                0.0f,
                adaptivePriorityBoundaryBandMeters
            );
            adaptivePriorityTopFraction = Mathf.Clamp01(
                adaptivePriorityTopFraction
            );
            adaptiveTransferMinParticles = Mathf.Max(
                0,
                adaptiveTransferMinParticles
            );
            adaptiveProjectionCalmAverageSpeed = Mathf.Max(
                0.0f,
                adaptiveProjectionCalmAverageSpeed
            );
            adaptiveProjectionCalmPriorityFraction = Mathf.Clamp01(
                adaptiveProjectionCalmPriorityFraction
            );
            adaptiveProjectionMaxAverageJDeviation = Mathf.Clamp01(
                adaptiveProjectionMaxAverageJDeviation
            );
            adaptiveProjectionBucketLinearSpeed = Mathf.Max(
                0.0f,
                adaptiveProjectionBucketLinearSpeed
            );
            adaptiveProjectionBucketAngularSpeed = Mathf.Max(
                0.0f,
                adaptiveProjectionBucketAngularSpeed
            );

            pressureWarmStartFactor = Mathf.Clamp01(
                pressureWarmStartFactor
            );

            if (maxParticleSpeed < 0.1f)
                maxParticleSpeed = 0.1f;

            if (massFixedScale < 1000)
                massFixedScale = 1000;

            if (momentumFixedScale < 1000)
                momentumFixedScale = 1000;

            if (maxAffineMagnitude < 0.1f)
                maxAffineMagnitude = 0.1f;

            if (manualApicDInverse < 0.0001f)
                manualApicDInverse = 0.0001f;

            if (bulkModulus < 0.0f)
                bulkModulus = 0.0f;

            referenceEosExponent = Mathf.Clamp(
                referenceEosExponent,
                1.0f,
                8.0f
            );
            gridDensityEosMaxVelocityCorrection = Mathf.Max(
                gridDensityEosMaxVelocityCorrection,
                0.0f
            );
            gridDensityEosPressureScale = Mathf.Max(
                gridDensityEosPressureScale,
                0.0f
            );
            gridDensityEosActivationRatio = Mathf.Clamp(
                gridDensityEosActivationRatio,
                1.0f,
                1.1f
            );

            if (maxStressMagnitude < 1.0f)
                maxStressMagnitude = 1.0f;

            minJ = Mathf.Clamp(minJ, 0.1f, 1.0f);
            maxJ = Mathf.Clamp(maxJ, 1.0f, 3.0f);

            if (yieldRegularizationRate < 0.0f)
                yieldRegularizationRate = 0.0f;

            if (maxYieldViscosityContribution < 0.0f)
                maxYieldViscosityContribution = 0.0f;

            if (maxEffectiveViscosity < 0.001f)
                maxEffectiveViscosity = 0.001f;

            if (freeSurfaceGradientScale < 1e-6f)
                freeSurfaceGradientScale = 1e-6f;

            if (freeSurfaceNormalDampingPerSecond < 0.0f)
                freeSurfaceNormalDampingPerSecond = 0.0f;

            if (freeSurfaceCohesionAcceleration < 0.0f)
                freeSurfaceCohesionAcceleration = 0.0f;

            if (maxFreeSurfaceVelocityCorrection < 0.0f)
                maxFreeSurfaceVelocityCorrection = 0.0f;

            if (jetCohesionAcceleration < 0.0f)
                jetCohesionAcceleration = 0.0f;

            if (maxJetCohesionVelocityCorrection < 0.0f)
                maxJetCohesionVelocityCorrection = 0.0f;

            if (bucketCollisionPadding < 0.0f)
                bucketCollisionPadding = 0.0f;

            if (topBoundaryPadding < 0.0f)
                topBoundaryPadding = 0.0f;

            if (holeNearHeightMeters < 0.0f)
                holeNearHeightMeters = 0.0f;

            if (holeRadialPaddingMeters < 0.0f)
                holeRadialPaddingMeters = 0.0f;

            maxGpuBucketHoles = Mathf.Clamp(maxGpuBucketHoles, 1, 64);

            if (holeOutflowExitDistanceMeters < 0.0f)
                holeOutflowExitDistanceMeters = 0.0f;

            outflowDischargeReferenceViscosity = Mathf.Max(
                outflowDischargeReferenceViscosity, 0.01f);
            outflowBaseDischargeCoefficient = Mathf.Clamp(
                outflowBaseDischargeCoefficient, 0.1f, 1.0f);
            outflowMinDischargeCoefficient = Mathf.Clamp(
                outflowMinDischargeCoefficient,
                0.02f,
                outflowBaseDischargeCoefficient);
            outflowMaxExitSpeed = Mathf.Max(outflowMaxExitSpeed, 0.0f);

            jetMpmCollarDurationSeconds = Mathf.Max(
                jetMpmCollarDurationSeconds,
                0.0f
            );
            jetMpmCollarMaxDistanceMeters = Mathf.Max(
                jetMpmCollarMaxDistanceMeters,
                0.0f
            );
            jetMpmCollarRadialPaddingMeters = Mathf.Max(
                jetMpmCollarRadialPaddingMeters,
                0.0f
            );

            if (airborneDragPerSecond < 0.0f)
                airborneDragPerSecond = 0.0f;

            if (jetStateDurationSeconds < 0.0f)
                jetStateDurationSeconds = 0.0f;

            if (airborneLifetimeSeconds < 0.1f)
                airborneLifetimeSeconds = 0.1f;

            jetCoherenceLengthMeters = Mathf.Max(jetCoherenceLengthMeters, 0.0f);
            jetCoherenceLengthMinMeters = Mathf.Max(
                jetCoherenceLengthMinMeters,
                0.0f
            );
            jetCoherenceLengthMaxMeters = Mathf.Max(
                jetCoherenceLengthMaxMeters,
                jetCoherenceLengthMinMeters
            );
            jetTransverseDampingPerSecond = Mathf.Max(
                jetTransverseDampingPerSecond,
                0.0f
            );
            jetCenterlineAttractionPerSecond = Mathf.Max(
                jetCenterlineAttractionPerSecond,
                0.0f
            );
            maxJetColumnVelocityCorrectionPerSubstep = Mathf.Max(
                maxJetColumnVelocityCorrectionPerSubstep,
                0.0f
            );
            jetColumnDragScale = Mathf.Clamp01(jetColumnDragScale);
            jetColumnMinAxialSpeed = Mathf.Max(jetColumnMinAxialSpeed, 0.01f);

            if (minFluidCellMass < 0.0f)
                minFluidCellMass = 0.0f;

            mpmTileSizeCells = NormalizeTileSizeCells(mpmTileSizeCells);
            ownerTileListRebuildInterval = Mathf.Clamp(
                ownerTileListRebuildInterval,
                1,
                2
            );
            ownerTileListReuseMinParticles = Mathf.Max(
                0,
                ownerTileListReuseMinParticles
            );
            projectionDivergenceScale = Mathf.Clamp(projectionDivergenceScale, 0.0f, 2.0f);

            if (maxAbsProjectionDivergence < 0.01f)
                maxAbsProjectionDivergence = 0.01f;

            if (pressureRedBlackSorIterations < 1)
                pressureRedBlackSorIterations = 1;

            if (pressureRhsScale < 0.0f)
                pressureRhsScale = 0.0f;

            pressureRedBlackSorOmega = Mathf.Clamp(
                pressureRedBlackSorOmega,
                0.05f,
                1.95f
            );

            if (maxProjectionPressure < 1.0f)
                maxProjectionPressure = 1.0f;

            if (pressureGradientScale < 0.0f)
                pressureGradientScale = 0.0f;

            if (maxPressureVelocityCorrection < 0.01f)
                maxPressureVelocityCorrection = 0.01f;

        }

        private static int NormalizeTileSizeCells(int value)
        {
            if (value <= 4)
                return 4;

            return 8;
        }
    }
}
