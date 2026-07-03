using System;
using System.IO;
using PaintBucketSim.Configs;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PaintBucketMultiColorValidation
{
    private const int GridSize = 512;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const int ParticleCount = 65536;
    private const float ImpactDeltaTime = 1.0f / 60.0f;

    private static readonly Vector2Int RedJetCenter = new Vector2Int(214, 316);
    private static readonly Vector2Int BlueJetCenter = new Vector2Int(300, 316);

    public static void Run()
    {
        PaintFilmGrid grid = null;
        PaintFilmGrid flowGrid = null;
        PaintDepositor depositor = null;
        GraphicsBuffer positions = null;
        GraphicsBuffer velocities = null;
        GraphicsBuffer colors = null;
        GraphicsBuffer states = null;
        GraphicsBuffer volumes = null;

        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G30B_MultiColorBucket"
            );
            Directory.CreateDirectory(outputDirectory);

            PaintFluidConfig fluidConfig = Load<PaintFluidConfig>(
                "Assets/Scripts/PaintBucketSim/Configs/PaintFluidConfig_dev.asset"
            );
            BucketConfig bucketConfig = Load<BucketConfig>(
                "Assets/Scripts/PaintBucketSim/Configs/BucketConfig_dev.asset"
            );

            ValidateBucketSetup(fluidConfig, bucketConfig);
            SaveBucketTopPreview(
                bucketConfig,
                fluidConfig,
                Path.Combine(outputDirectory, "G30B_BucketCompartments_TopView.png")
            );

            ComputeShader impact = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Impact/SurfaceImpact.compute"
            );
            ComputeShader evolution = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintEvaporation.compute"
            );
            ComputeShader baker = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintFilmBaker.compute"
            );
            Material sourceMaterial = Load<Material>(
                "Assets/PaintSim/Materials/PaintSurfaceMaterial.mat"
            );

            BuildTwoHoleJetParticles(
                out Vector4[] positionData,
                out Vector4[] velocityData,
                out Vector4[] colorData,
                out Vector4[] stateData,
                out Vector4[] volumeData,
                out double expectedVolume
            );

            grid = CreateGrid();
            positions = NewBuffer(positionData);
            velocities = NewBuffer(velocityData);
            colors = NewBuffer(colorData);
            states = NewBuffer(stateData);
            volumes = NewBuffer(volumeData);

            PaintProperties paint = PaintProperties.Create(
                Density,
                0.86f,
                0.036f,
                Color.white
            );
            depositor = new PaintDepositor(impact, grid, paint);
            depositor.ConfigureImpactRheology(
                true,
                2.55f,
                0.13f,
                0.78f,
                2.15f,
                0.57f,
                0.0f
            );
            depositor.ConfigureColorMixing(
                PaintColorMixingMode.KubelkaMunkApprox,
                1.0f,
                0.035f,
                18.0f
            );
            depositor.DispatchFromMlsMpmBuffers(
                positions,
                velocities,
                colors,
                states,
                volumes,
                ParticleCount,
                SurfaceProperties.Wood,
                true,
                true,
                true,
                true,
                ImpactDeltaTime
            );

            SurfaceImpactDiagnostics diagnostics = depositor.ReadDiagnostics();
            PaintCellData[] impactCells = ReadCells(grid);
            FilmMetrics impactMetrics = MeasureFilm(impactCells);
            double depositedVolume =
                impactMetrics.ThicknessUnits /
                (double)PaintCellData.ThicknessScale *
                CellSize *
                CellSize;
            double depositError =
                Math.Abs(depositedVolume - expectedVolume) /
                Math.Max(expectedVolume, 1e-12);

            Color redSample = SampleAverageColor(impactCells, RedJetCenter, 36);
            Color blueSample = SampleAverageColor(impactCells, BlueJetCenter, 36);

            Require(
                diagnostics.Scanned == ParticleCount &&
                diagnostics.Impacted >= ParticleCount * 0.985f &&
                diagnostics.CellWrites > 0,
                $"Unexpected two-color jet diagnostics: {diagnostics}."
            );
            Require(
                depositError < 0.05,
                $"Two-color jet deposited-volume error is too high: {depositError:P2}."
            );
            Require(
                impactMetrics.ActiveCellCount > 1200,
                $"Expected a broad two-jet paint footprint, got {impactMetrics.ActiveCellCount} active cells."
            );
            Require(
                redSample.r > redSample.b * 1.35f && redSample.r > 0.25f,
                $"Left jet did not remain visually red enough: {ColorSummary(redSample)}."
            );
            Require(
                blueSample.b > blueSample.r * 1.35f && blueSample.b > 0.25f,
                $"Right jet did not remain visually blue enough: {ColorSummary(blueSample)}."
            );

            SaveBakedAtlas(
                baker,
                grid,
                Path.Combine(outputDirectory, "G30B_RedBlueTwoHoleJets_Impact_Atlas.png")
            );
            RenderBoardPreview(
                baker,
                sourceMaterial,
                grid,
                Path.Combine(outputDirectory, "G30B_RedBlueTwoHoleJets_Impact.png")
            );

            flowGrid = CreateGrid();
            flowGrid.PaintCellBuffer.SetData(impactCells);
            RunSurfaceFlow(evolution, flowGrid);
            PaintCellData[] flowCells = ReadCells(flowGrid);
            FilmMetrics flowMetrics = MeasureFilm(flowCells);
            double flowDrift =
                Math.Abs(flowMetrics.ThicknessUnits - impactMetrics.ThicknessUnits) /
                (double)Math.Max(impactMetrics.ThicknessUnits, 1L);

            Require(
                flowDrift < 0.035,
                $"Two-color flow drift is too high: {flowDrift:P2}."
            );
            Require(
                flowMetrics.ActiveCellCount > impactMetrics.ActiveCellCount,
                "Expected gravity/runoff evolution to grow the two-color footprint."
            );

            SaveBakedAtlas(
                baker,
                flowGrid,
                Path.Combine(outputDirectory, "G30B_RedBlueTwoHoleJets_Flow_3s_Atlas.png")
            );
            RenderBoardPreview(
                baker,
                sourceMaterial,
                flowGrid,
                Path.Combine(outputDirectory, "G30B_RedBlueTwoHoleJets_Flow_3s.png")
            );

            Debug.Log(
                "[PaintBucketMultiColorValidation] PASS " +
                $"particles={ParticleCount}, diagnostics=({diagnostics}), " +
                $"depositError={depositError:P2}, flowDrift={flowDrift:P2}, " +
                $"impactCells={impactMetrics.ActiveCellCount}, " +
                $"flowCells={flowMetrics.ActiveCellCount}, " +
                $"red={ColorSummary(redSample)}, blue={ColorSummary(blueSample)}, " +
                $"output={outputDirectory}"
            );
        }
        finally
        {
            depositor?.Dispose();
            grid?.Dispose();
            flowGrid?.Dispose();
            positions?.Release();
            velocities?.Release();
            colors?.Release();
            states?.Release();
            volumes?.Release();
        }
    }

    private static void ValidateBucketSetup(
        PaintFluidConfig fluidConfig,
        BucketConfig bucketConfig)
    {
        Require(fluidConfig != null, "PaintFluidConfig is missing.");
        Require(bucketConfig != null, "BucketConfig is missing.");
        Require(fluidConfig.enableColorCompartments, "Color compartments are disabled.");
        Require(fluidConfig.colorCompartmentCount == 2, "Expected exactly two color compartments.");
        Require(
            fluidConfig.colorCompartmentAxis == FluidColorCompartmentAxis.BucketLocalX,
            "Expected bucket-local X color split for left/right red-blue compartments."
        );
        Require(
            fluidConfig.enablePhysicalColorDividers,
            "Physical GPU color dividers are disabled."
        );
        Require(
            fluidConfig.carveInitialParticlesAroundPhysicalDividers,
            "Initial particles must be carved around the physical divider."
        );
        Require(
            fluidConfig.colorDividerThicknessMeters >= 0.008f &&
            fluidConfig.colorDividerThicknessMeters <= 0.025f,
            $"Unexpected divider thickness: {fluidConfig.colorDividerThicknessMeters:F4}m."
        );
        Require(
            fluidConfig.compartmentColors != null &&
            fluidConfig.compartmentColors.Length >= 2,
            "At least two compartment colors are required."
        );
        Require(IsRedDominant(fluidConfig.compartmentColors[0]), "First compartment color should be red.");
        Require(IsBlueDominant(fluidConfig.compartmentColors[1]), "Second compartment color should be blue.");

        BucketHoleConfig[] holes = bucketConfig.holes;
        Require(holes != null && holes.Length >= 3, "Expected three configured bucket holes.");

        int activeCircular = 0;
        int inactiveCircular = 0;
        for (int i = 0; i < holes.Length; i++)
        {
            BucketHoleConfig hole = holes[i];
            Require(hole != null, $"Hole {i} is null.");
            Require(hole.shape == BucketHoleShape.Circular, $"Hole {i} must be circular.");
            Require(
                hole.radiusMeters >= 0.0015f &&
                hole.radiusMeters <= 0.0045f,
                $"Hole {i} radius should stay small/controlled, got {hole.radiusMeters:F4}m."
            );
            Require(
                Vector3.Dot(hole.localNormal.normalized, Vector3.down) > 0.95f,
                $"Hole {i} normal should point through the bucket bottom."
            );
            Require(
                Mathf.Abs(hole.localCenter.y + bucketConfig.heightMeters * 0.5f) < 0.004f,
                $"Hole {i} should sit on the bottom plane."
            );

            if (hole.active)
                activeCircular++;
            else
                inactiveCircular++;
        }

        Require(activeCircular == 2, $"Expected two active circular holes, got {activeCircular}.");
        Require(inactiveCircular >= 1, "Expected one inactive circular bottom-edge hole.");
        Require(holes[0].localCenter.x < -0.01f, "First active hole should sit under the red/left compartment.");
        Require(holes[1].localCenter.x > 0.01f, "Second active hole should sit under the blue/right compartment.");
        Require(
            new Vector2(holes[2].localCenter.x, holes[2].localCenter.z).magnitude >
            bucketConfig.bottomRadiusMeters * 0.55f,
            "Inactive third hole should be near the bottom edge."
        );
    }

    private static void BuildTwoHoleJetParticles(
        out Vector4[] positions,
        out Vector4[] velocities,
        out Vector4[] colors,
        out Vector4[] states,
        out Vector4[] volumes,
        out double expectedVolume)
    {
        positions = new Vector4[ParticleCount];
        velocities = new Vector4[ParticleCount];
        colors = new Vector4[ParticleCount];
        states = new Vector4[ParticleCount];
        volumes = new Vector4[ParticleCount];
        expectedVolume = 0.0;

        var random = new System.Random(30031);
        int half = ParticleCount / 2;
        Color red = new Color(0.95f, 0.04f, 0.03f, 1.0f);
        Color blue = new Color(0.03f, 0.22f, 0.95f, 1.0f);

        for (int i = 0; i < ParticleCount; i++)
        {
            bool redSide = i < half;
            int localIndex = redSide ? i : i - half;
            Vector2Int center = redSide ? RedJetCenter : BlueJetCenter;
            Color color = redSide ? red : blue;

            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) *
                Mathf.Lerp(0.010f, 0.125f, (float)random.NextDouble());
            float streak =
                Mathf.Lerp(-0.34f, 0.46f, (float)random.NextDouble()) *
                Mathf.Lerp(0.25f, 1.0f, localIndex / (float)Mathf.Max(half - 1, 1));
            float u =
                center.x * CellSize +
                Mathf.Cos(angle) * radial * 0.62f +
                (redSide ? 0.018f : -0.018f) * Mathf.Sin(streak * 5.0f);
            float v =
                center.y * CellSize +
                Mathf.Sin(angle) * radial +
                streak;
            float currentDepth =
                Mathf.Lerp(0.004f, 0.020f, (float)random.NextDouble());
            float normalSpeed =
                Mathf.Lerp(1.25f, 2.25f, (float)random.NextDouble());
            float tangentU =
                (redSide ? 0.14f : -0.14f) +
                Mathf.Lerp(-0.18f, 0.18f, (float)random.NextDouble());
            float tangentV =
                Mathf.Lerp(0.18f, 0.55f, (float)random.NextDouble());
            float restVolume =
                Mathf.Lerp(0.9e-8f, 1.8e-8f, (float)random.NextDouble());

            expectedVolume += restVolume;
            positions[i] = new Vector4(u, -currentDepth, v, 0.00135f);
            velocities[i] = new Vector4(
                tangentU,
                -normalSpeed,
                tangentV,
                restVolume * Density
            );
            colors[i] = new Vector4(color.r, color.g, color.b, color.a);
            states[i] = new Vector4(6.0f, 0.0f, 0.0f, i + 1);
            volumes[i] = new Vector4(restVolume, 1.0f, Density, 1.0f);
        }
    }

    private static void RunSurfaceFlow(ComputeShader evolution, PaintFilmGrid grid)
    {
        var evolver = new PaintEvolver(evolution, grid)
        {
            EvaporationRate = 0.008f,
            DiffusionRate = 7.2f,
            RunoffRate = 0.36f,
            MinimumWetThickness = 0.000014f,
            SurfaceGravity = new Vector2(0.0f, -5.4f),
            PaintDensity = Density,
            DynamicViscosity = 0.86f,
            SurfaceTension = 0.036f,
            YieldStress = 0.0f,
            ContactLineThickness = 0.000024f,
            ContactAngleResistance = 0.66f,
            SubstrateFlowVariation = 0.32f,
            SurfaceRoughness = SurfaceProperties.Wood.Roughness,
            DripFingerInstability = 0.48f,
            ThinFilmCohesion = 0.62f,
            SurfaceAbsorptionRate = SurfaceProperties.Wood.AbsorptionRate,
            ColorMixingMode = PaintColorMixingMode.KubelkaMunkApprox,
            PigmentMixStrength = 1.0f,
            PigmentMinReflectance = 0.035f,
            PigmentMaxKs = 18.0f
        };

        for (int step = 0; step < 180; step++)
            evolver.Evolve(1.0f / 60.0f);
    }

    private static PaintFilmGrid CreateGrid()
    {
        return new PaintFilmGrid(
            GridSize,
            GridSize,
            CellSize,
            CellSize,
            0.0f,
            Vector2.zero,
            Vector3.zero,
            Vector3.right,
            Vector3.forward,
            Vector3.up,
            0.035f,
            true
        );
    }

    private static void SaveBucketTopPreview(
        BucketConfig bucketConfig,
        PaintFluidConfig fluidConfig,
        string path)
    {
        const int size = 768;
        const float margin = 0.86f;
        Texture2D image = new Texture2D(size, size, TextureFormat.RGB24, false, false);
        float center = size * 0.5f;
        float radiusPixels = size * 0.5f * margin;
        float bottomRadius = bucketConfig.shapeType == BucketShapeType.Cylinder
            ? bucketConfig.topRadiusMeters
            : bucketConfig.bottomRadiusMeters;
        Color background = new Color(0.045f, 0.050f, 0.064f, 1.0f);
        Color wall = new Color(0.52f, 0.53f, 0.57f, 1.0f);
        Color divider = new Color(0.82f, 0.82f, 0.86f, 1.0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = (x - center) / radiusPixels;
                float pz = (y - center) / radiusPixels;
                float r = Mathf.Sqrt(px * px + pz * pz);

                if (r > 1.0f)
                {
                    image.SetPixel(x, y, background);
                    continue;
                }

                Color compartment = px < 0.0f
                    ? fluidConfig.compartmentColors[0]
                    : fluidConfig.compartmentColors[1];
                Color baseColor = Color.Lerp(
                    new Color(0.15f, 0.15f, 0.17f, 1.0f),
                    compartment,
                    0.78f
                );

                if (Mathf.Abs(px) < 0.035f)
                    baseColor = divider;

                if (r > 0.965f)
                    baseColor = wall;

                image.SetPixel(x, y, baseColor);
            }
        }

        BucketHoleConfig[] holes = bucketConfig.holes ?? Array.Empty<BucketHoleConfig>();
        for (int i = 0; i < holes.Length; i++)
        {
            BucketHoleConfig hole = holes[i];
            Vector3 c = bucketConfig.GetResolvedHoleLocalCenter(hole);
            float hx = center + c.x / bottomRadius * radiusPixels;
            float hy = center + c.z / bottomRadius * radiusPixels;
            float hr = Mathf.Max(hole.radiusMeters / bottomRadius * radiusPixels, 4.0f);
            Color color = hole.active
                ? new Color(0.015f, 0.015f, 0.018f, 1.0f)
                : new Color(0.36f, 0.36f, 0.38f, 1.0f);
            DrawFilledCircle(image, hx, hy, hr, color);
            DrawCircleOutline(
                image,
                hx,
                hy,
                hr + 3.0f,
                hole.active ? Color.white : new Color(0.62f, 0.62f, 0.66f, 1.0f)
            );
        }

        image.Apply(false, false);
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
    }

    private static void DrawFilledCircle(
        Texture2D image,
        float cx,
        float cy,
        float radius,
        Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
        int maxX = Mathf.Min(image.width - 1, Mathf.CeilToInt(cx + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
        int maxY = Mathf.Min(image.height - 1, Mathf.CeilToInt(cy + radius));
        float r2 = radius * radius;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    image.SetPixel(x, y, color);
            }
        }
    }

    private static void DrawCircleOutline(
        Texture2D image,
        float cx,
        float cy,
        float radius,
        Color color)
    {
        int samples = 192;
        for (int i = 0; i < samples; i++)
        {
            float angle = Mathf.PI * 2.0f * i / samples;
            int x = Mathf.RoundToInt(cx + Mathf.Cos(angle) * radius);
            int y = Mathf.RoundToInt(cy + Mathf.Sin(angle) * radius);
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int px = x + ox;
                    int py = y + oy;
                    if (px >= 0 && px < image.width && py >= 0 && py < image.height)
                        image.SetPixel(px, py, color);
                }
            }
        }
    }

    private static void SaveBakedAtlas(
        ComputeShader baker,
        PaintFilmGrid grid,
        string path)
    {
        RenderTexture albedo = null;
        RenderTexture surfaceData = null;
        Texture2D image = null;

        try
        {
            albedo = NewRenderTexture(GridSize, GridSize);
            surfaceData = NewRenderTexture(GridSize, GridSize);

            int kernel = baker.FindKernel("CSMain");
            baker.SetBuffer(kernel, "_PaintCellBuffer", grid.PaintCellBuffer);
            baker.SetTexture(kernel, "_OutputTexture", albedo);
            baker.SetTexture(kernel, "_SurfaceDataTexture", surfaceData);
            baker.SetInt("_GridWidth", GridSize);
            baker.SetInt("_GridHeight", GridSize);
            baker.SetInt("_ThicknessScale", PaintCellData.ThicknessScale);
            baker.SetFloat("_MaxThickness", SurfaceVisualProperties.Wood.MaxThickness);
            baker.SetFloat("_WetnessShine", SurfaceVisualProperties.Wood.WetnessShine);
            baker.SetVector("_CanvasBaseColor", SurfaceVisualProperties.Wood.CanvasBaseColor);
            baker.Dispatch(
                kernel,
                Mathf.CeilToInt(GridSize / 8.0f),
                Mathf.CeilToInt(GridSize / 8.0f),
                1
            );

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = albedo;
            image = new Texture2D(GridSize, GridSize, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, GridSize, GridSize), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = previous;
        }
        finally
        {
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
            if (albedo != null)
            {
                albedo.Release();
                UnityEngine.Object.DestroyImmediate(albedo);
            }
            if (surfaceData != null)
            {
                surfaceData.Release();
                UnityEngine.Object.DestroyImmediate(surfaceData);
            }
        }
    }

    private static void RenderBoardPreview(
        ComputeShader baker,
        Material sourceMaterial,
        PaintFilmGrid grid,
        string path)
    {
        GameObject cameraObject = null;
        GameObject lightObject = null;
        GameObject board = null;
        RenderTexture cameraTarget = null;
        PaintSurfaceRenderer surfaceRenderer = null;
        Material runtimeMaterial = null;

        try
        {
            ConfigureRenderEnvironment(
                out cameraObject,
                out lightObject,
                out Camera camera,
                out cameraTarget
            );

            runtimeMaterial = new Material(sourceMaterial)
            {
                name = "G30B Multi Color Board Material"
            };
            board = GameObject.CreatePrimitive(PrimitiveType.Plane);
            board.name = "G30B Multi Color Board";
            float center = GridSize * CellSize * 0.5f;
            board.transform.position = new Vector3(center, center, 0.0f);
            board.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
            board.transform.localScale = new Vector3(0.54f, 1.0f, 0.54f);
            board.GetComponent<MeshRenderer>().sharedMaterial = runtimeMaterial;

            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                board.GetComponent<MeshRenderer>()
            );
            surfaceRenderer.ConfigureSurfaceAppearance(SurfaceVisualProperties.Wood);
            surfaceRenderer.Render();
            Capture(camera, cameraTarget, path);
        }
        finally
        {
            surfaceRenderer?.Dispose();
            if (runtimeMaterial != null)
                UnityEngine.Object.DestroyImmediate(runtimeMaterial);
            if (board != null)
                UnityEngine.Object.DestroyImmediate(board);
            if (cameraObject != null)
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                if (camera != null && camera.targetTexture == cameraTarget)
                    camera.targetTexture = null;
            }
            if (cameraTarget != null)
            {
                cameraTarget.Release();
                UnityEngine.Object.DestroyImmediate(cameraTarget);
            }
            if (lightObject != null)
                UnityEngine.Object.DestroyImmediate(lightObject);
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void ConfigureRenderEnvironment(
        out GameObject cameraObject,
        out GameObject lightObject,
        out Camera camera,
        out RenderTexture cameraTarget)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f, 1.0f);
        RenderSettings.reflectionIntensity = 0.85f;

        cameraObject = new GameObject("G30B Multi Color Paint Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(2.56f, 2.56f, -5.35f);
        camera.transform.LookAt(new Vector3(2.56f, 2.56f, 0.0f));
        camera.fieldOfView = 29.0f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.058f, 0.075f, 1.0f);
        camera.allowHDR = true;

        cameraTarget = new RenderTexture(1280, 900, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4
        };
        cameraTarget.Create();
        camera.targetTexture = cameraTarget;

        lightObject = new GameObject("G30B Multi Color Paint Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1.0f, 0.91f, 0.80f, 1.0f);
        light.intensity = 1.55f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(18.0f, -22.0f, 0.0f);
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        RenderTexture previous = RenderTexture.active;
        camera.Render();
        camera.Render();
        RenderTexture.active = target;

        Texture2D image = new Texture2D(
            target.width,
            target.height,
            TextureFormat.RGB24,
            false,
            false
        );
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply(false, false);
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = previous;
    }

    private static RenderTexture NewRenderTexture(int width, int height)
    {
        RenderTexture texture = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGBHalf
        )
        {
            enableRandomWrite = true
        };
        texture.Create();
        return texture;
    }

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[grid.GridWidth * grid.GridHeight];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
    }

    private static FilmMetrics MeasureFilm(PaintCellData[] cells)
    {
        long thicknessUnits = 0L;
        int activeCells = 0;

        foreach (PaintCellData cell in cells)
        {
            int thickness = Math.Max(cell.ThicknessInt, 0);
            if (thickness <= 0)
                continue;

            thicknessUnits += thickness;
            activeCells++;
        }

        return new FilmMetrics
        {
            ThicknessUnits = thicknessUnits,
            ActiveCellCount = activeCells
        };
    }

    private static Color SampleAverageColor(
        PaintCellData[] cells,
        Vector2Int center,
        int radius)
    {
        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        double weight = 0.0;

        int minX = Mathf.Max(0, center.x - radius);
        int maxX = Mathf.Min(GridSize - 1, center.x + radius);
        int minY = Mathf.Max(0, center.y - radius);
        int maxY = Mathf.Min(GridSize - 1, center.y + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                PaintCellData cell = cells[y * GridSize + x];
                int thickness = Math.Max(cell.ThicknessInt, 0);
                if (thickness <= 0)
                    continue;

                float dx = x - center.x;
                float dy = y - center.y;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                r += cell.Color.x * thickness;
                g += cell.Color.y * thickness;
                b += cell.Color.z * thickness;
                weight += thickness;
            }
        }

        if (weight <= 0.0)
            return Color.black;

        return new Color(
            (float)(r / weight),
            (float)(g / weight),
            (float)(b / weight),
            1.0f
        );
    }

    private static GraphicsBuffer NewBuffer(Vector4[] values)
    {
        var buffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            values.Length,
            sizeof(float) * 4
        );
        buffer.SetData(values);
        return buffer;
    }

    private static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, $"Missing multi-color validation asset: {path}");
        return asset;
    }

    private static bool IsRedDominant(Color color)
    {
        return color.r > color.g * 2.0f && color.r > color.b * 2.0f;
    }

    private static bool IsBlueDominant(Color color)
    {
        return color.b > color.r * 2.0f && color.b > color.g * 2.0f;
    }

    private static string ColorSummary(Color color)
    {
        return $"({color.r:F3},{color.g:F3},{color.b:F3})";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private struct FilmMetrics
    {
        public long ThicknessUnits;
        public int ActiveCellCount;
    }
}
