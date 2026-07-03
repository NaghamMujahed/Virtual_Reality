using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;

public static class PaintPigmentMixingValidation
{
    private const int GridSize = 512;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const int ParticleCount = 32768;
    private const int PairCount = 4;
    private const float ImpactDeltaTime = 1.0f / 60.0f;

    private static readonly Vector2Int RedBlueCenter = new Vector2Int(150, 360);
    private static readonly Vector2Int YellowBlueCenter = new Vector2Int(360, 360);
    private static readonly Vector2Int RedWhiteCenter = new Vector2Int(150, 150);
    private static readonly Vector2Int YellowBlackCenter = new Vector2Int(360, 150);

    public static void Run()
    {
        GameObject cameraObject = null;
        GameObject lightObject = null;
        RenderTexture cameraTarget = null;

        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G30_PigmentMixing"
            );
            Directory.CreateDirectory(outputDirectory);

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

            BuildHighCountMixParticles(
                out Vector4[] positionData,
                out Vector4[] velocityData,
                out Vector4[] colorData,
                out Vector4[] stateData,
                out Vector4[] volumeData,
                out double expectedVolume
            );

            ConfigureRenderEnvironment(
                out cameraObject,
                out lightObject,
                out Camera camera,
                out cameraTarget
            );

            ModeResult rgb = RunMode(
                PaintColorMixingMode.Rgb,
                "G30_RGB_HighCount.png",
                impact,
                baker,
                sourceMaterial,
                camera,
                cameraTarget,
                outputDirectory,
                positionData,
                velocityData,
                colorData,
                stateData,
                volumeData,
                expectedVolume
            );

            ModeResult pigment = RunMode(
                PaintColorMixingMode.KubelkaMunkApprox,
                "G30_KubelkaMunk_HighCount.png",
                impact,
                baker,
                sourceMaterial,
                camera,
                cameraTarget,
                outputDirectory,
                positionData,
                velocityData,
                colorData,
                stateData,
                volumeData,
                expectedVolume
            );

            FlowResult pigmentFlow = RunPigmentFlowCase(
                evolution,
                baker,
                sourceMaterial,
                camera,
                cameraTarget,
                outputDirectory,
                pigment.Cells
            );

            float rbRgbLuma = Luminance(rgb.RedBlue);
            float rbPigmentLuma = Luminance(pigment.RedBlue);
            float ybRgbGreenDominance =
                rgb.YellowBlue.g - Mathf.Max(rgb.YellowBlue.r, rgb.YellowBlue.b);
            float ybPigmentGreenDominance =
                pigment.YellowBlue.g -
                Mathf.Max(pigment.YellowBlue.r, pigment.YellowBlue.b);
            float yellowBlackRgbLuma = Luminance(rgb.YellowBlack);
            float yellowBlackPigmentLuma = Luminance(pigment.YellowBlack);
            float activeColorDifference =
                ActiveWeightedColorDifference(rgb.Cells, pigment.Cells);

            Require(
                pigment.Diagnostics.Impacted >= ParticleCount * 0.985f,
                $"Pigment pass missed impacts: {pigment.Diagnostics}."
            );
            Require(
                rgb.Diagnostics.Impacted >= ParticleCount * 0.985f,
                $"RGB pass missed impacts: {rgb.Diagnostics}."
            );
            Require(
                rgb.DepositError < 0.035 && pigment.DepositError < 0.035,
                $"Unexpected deposit drift. rgb={rgb.DepositError:P2}, " +
                $"pigment={pigment.DepositError:P2}."
            );
            Require(
                Math.Abs(rgb.DepositedVolume - pigment.DepositedVolume) /
                Math.Max(rgb.DepositedVolume, 1e-12) < 0.01,
                "Color mixing changed deposited volume."
            );
            Require(
                rbPigmentLuma < rbRgbLuma * 0.70f,
                $"Red+Blue pigment mix is not optically darker. " +
                $"rgb={rgb.RedBlue}, pigment={pigment.RedBlue}."
            );
            Require(
                pigment.RedBlue.r > pigment.RedBlue.g * 1.25f &&
                pigment.RedBlue.b > pigment.RedBlue.g * 1.25f,
                $"Red+Blue pigment mix lost purple hue: {pigment.RedBlue}."
            );
            Require(
                ybPigmentGreenDominance > ybRgbGreenDominance + 0.08f,
                $"Yellow+Blue did not become visibly greener. " +
                $"rgbDominance={ybRgbGreenDominance:F3}, " +
                $"pigmentDominance={ybPigmentGreenDominance:F3}, " +
                $"pigment={pigment.YellowBlue}."
            );
            Require(
                yellowBlackPigmentLuma < yellowBlackRgbLuma * 0.82f,
                $"Yellow+Black pigment mix should be darker/earthier. " +
                $"rgb={rgb.YellowBlack}, pigment={pigment.YellowBlack}."
            );
            Require(
                activeColorDifference > 0.075f,
                $"RGB and pigment images are too similar: {activeColorDifference:F3}."
            );
            Require(
                pigmentFlow.VolumeDrift < 0.025,
                $"Pigment flow volume drift is {pigmentFlow.VolumeDrift:P2}."
            );
            Require(
                pigmentFlow.ActiveCellGrowth > 1500,
                $"Pigment flow did not expand enough: {pigmentFlow.ActiveCellGrowth} cells."
            );

            camera.targetTexture = null;
            cameraTarget.Release();
            UnityEngine.Object.DestroyImmediate(cameraTarget);
            cameraTarget = null;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            cameraObject = null;
            UnityEngine.Object.DestroyImmediate(lightObject);
            lightObject = null;

            Debug.Log(
                "[PaintPigmentMixingValidation] PASS " +
                $"particlesPerMode={ParticleCount}, totalDepositions={ParticleCount * 2}, " +
                $"rgbError={rgb.DepositError:P2}, pigmentError={pigment.DepositError:P2}, " +
                $"redBlueRgb={ColorSummary(rgb.RedBlue)}, " +
                $"redBluePigment={ColorSummary(pigment.RedBlue)}, " +
                $"yellowBlueRgb={ColorSummary(rgb.YellowBlue)}, " +
                $"yellowBluePigment={ColorSummary(pigment.YellowBlue)}, " +
                $"yellowBlackRgb={ColorSummary(rgb.YellowBlack)}, " +
                $"yellowBlackPigment={ColorSummary(pigment.YellowBlack)}, " +
                $"activeColorDifference={activeColorDifference:F3}, " +
                $"flowDrift={pigmentFlow.VolumeDrift:P2}, " +
                $"flowGrowth={pigmentFlow.ActiveCellGrowth}, " +
                $"output={outputDirectory}"
            );
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (RenderTexture.active != null)
                RenderTexture.active = null;

            if (cameraTarget != null)
            {
                cameraTarget.Release();
                UnityEngine.Object.DestroyImmediate(cameraTarget);
            }
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (lightObject != null)
                UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }

    private static ModeResult RunMode(
        PaintColorMixingMode mode,
        string imageName,
        ComputeShader impact,
        ComputeShader baker,
        Material sourceMaterial,
        Camera camera,
        RenderTexture cameraTarget,
        string outputDirectory,
        Vector4[] positionData,
        Vector4[] velocityData,
        Vector4[] colorData,
        Vector4[] stateData,
        Vector4[] volumeData,
        double expectedVolume)
    {
        PaintFilmGrid grid = null;
        PaintDepositor depositor = null;
        PaintSurfaceRenderer surfaceRenderer = null;
        GraphicsBuffer positions = null;
        GraphicsBuffer velocities = null;
        GraphicsBuffer colors = null;
        GraphicsBuffer states = null;
        GraphicsBuffer volumes = null;
        GameObject board = null;
        Material runtimeMaterial = null;

        try
        {
            grid = CreateGrid();
            positions = NewBuffer((Vector4[])positionData.Clone());
            velocities = NewBuffer((Vector4[])velocityData.Clone());
            colors = NewBuffer((Vector4[])colorData.Clone());
            states = NewBuffer((Vector4[])stateData.Clone());
            volumes = NewBuffer((Vector4[])volumeData.Clone());

            PaintProperties paint = PaintProperties.Create(
                Density,
                0.78f,
                0.035f,
                Color.white
            );
            depositor = new PaintDepositor(impact, grid, paint);
            depositor.ConfigureImpactRheology(
                true,
                2.4f,
                0.12f,
                0.75f,
                2.1f,
                0.58f,
                0.0f
            );
            depositor.ConfigureColorMixing(
                mode,
                mode == PaintColorMixingMode.KubelkaMunkApprox ? 1.0f : 0.0f,
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
            PaintCellData[] cells = ReadCells(grid);
            FilmMetrics metrics = MeasureFilm(cells);
            double depositedVolume =
                metrics.ThicknessUnits /
                (double)PaintCellData.ThicknessScale *
                CellSize *
                CellSize;
            double depositError =
                Math.Abs(depositedVolume - expectedVolume) /
                Math.Max(expectedVolume, 1e-12);

            runtimeMaterial = new Material(sourceMaterial)
            {
                name = $"G30 {mode} Material"
            };
            board = CreateBoard(runtimeMaterial);
            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                board.GetComponent<MeshRenderer>()
            );
            surfaceRenderer.ConfigureSurfaceAppearance(SurfaceVisualProperties.Wood);
            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, imageName)
            );
            SaveBakedAtlas(
                baker,
                grid,
                Path.Combine(
                    outputDirectory,
                    Path.GetFileNameWithoutExtension(imageName) + "_Atlas.png"
                )
            );

            return new ModeResult
            {
                Mode = mode,
                Cells = cells,
                Diagnostics = diagnostics,
                DepositError = (float)depositError,
                DepositedVolume = depositedVolume,
                ActiveCells = metrics.ActiveCellCount,
                RedBlue = SampleAverageColor(cells, RedBlueCenter, 42),
                YellowBlue = SampleAverageColor(cells, YellowBlueCenter, 42),
                RedWhite = SampleAverageColor(cells, RedWhiteCenter, 42),
                YellowBlack = SampleAverageColor(cells, YellowBlackCenter, 42)
            };
        }
        finally
        {
            surfaceRenderer?.Dispose();
            depositor?.Dispose();
            grid?.Dispose();
            positions?.Release();
            velocities?.Release();
            colors?.Release();
            states?.Release();
            volumes?.Release();

            if (runtimeMaterial != null)
                UnityEngine.Object.DestroyImmediate(runtimeMaterial);
            if (board != null)
                UnityEngine.Object.DestroyImmediate(board);
        }
    }

    private static FlowResult RunPigmentFlowCase(
        ComputeShader evolution,
        ComputeShader baker,
        Material sourceMaterial,
        Camera camera,
        RenderTexture cameraTarget,
        string outputDirectory,
        PaintCellData[] pigmentCells)
    {
        PaintFilmGrid grid = null;
        PaintSurfaceRenderer surfaceRenderer = null;
        GameObject board = null;
        Material runtimeMaterial = null;

        try
        {
            grid = CreateGrid();
            grid.PaintCellBuffer.SetData(pigmentCells);
            FilmMetrics before = MeasureFilm(pigmentCells);

            PaintEvolver evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.01f,
                DiffusionRate = 8.0f,
                RunoffRate = 0.34f,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = new Vector2(0.0f, -5.2f),
                PaintDensity = Density,
                DynamicViscosity = 0.78f,
                SurfaceTension = 0.035f,
                YieldStress = 0.0f,
                ContactLineThickness = 0.000022f,
                ContactAngleResistance = 0.62f,
                SubstrateFlowVariation = 0.28f,
                SurfaceRoughness = SurfaceProperties.Wood.Roughness,
                DripFingerInstability = 0.44f,
                ThinFilmCohesion = 0.56f,
                SurfaceAbsorptionRate = SurfaceProperties.Wood.AbsorptionRate,
                ColorMixingMode = PaintColorMixingMode.KubelkaMunkApprox,
                PigmentMixStrength = 1.0f,
                PigmentMinReflectance = 0.035f,
                PigmentMaxKs = 18.0f
            };

            for (int step = 0; step < 240; step++)
                evolver.Evolve(1.0f / 60.0f);

            PaintCellData[] afterCells = ReadCells(grid);
            FilmMetrics after = MeasureFilm(afterCells);
            double drift = Math.Abs(after.ThicknessUnits - before.ThicknessUnits) /
                (double)Math.Max(before.ThicknessUnits, 1L);

            runtimeMaterial = new Material(sourceMaterial)
            {
                name = "G30 Pigment Flow Material"
            };
            board = CreateBoard(runtimeMaterial);
            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                board.GetComponent<MeshRenderer>()
            );
            surfaceRenderer.ConfigureSurfaceAppearance(SurfaceVisualProperties.Wood);
            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G30_KubelkaMunk_Flow_4s.png")
            );
            SaveBakedAtlas(
                baker,
                grid,
                Path.Combine(outputDirectory, "G30_KubelkaMunk_Flow_4s_Atlas.png")
            );

            return new FlowResult
            {
                VolumeDrift = (float)drift,
                ActiveCellGrowth = after.ActiveCellCount - before.ActiveCellCount
            };
        }
        finally
        {
            surfaceRenderer?.Dispose();
            grid?.Dispose();

            if (runtimeMaterial != null)
                UnityEngine.Object.DestroyImmediate(runtimeMaterial);
            if (board != null)
                UnityEngine.Object.DestroyImmediate(board);
        }
    }

    private static void BuildHighCountMixParticles(
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

        var random = new System.Random(30030);
        int perPair = ParticleCount / PairCount;

        for (int i = 0; i < ParticleCount; i++)
        {
            int pair = Mathf.Min(i / perPair, PairCount - 1);
            int localIndex = i - pair * perPair;
            Vector2Int center = pair switch
            {
                0 => RedBlueCenter,
                1 => YellowBlueCenter,
                2 => RedWhiteCenter,
                _ => YellowBlackCenter
            };
            Color color = ResolvePairColor(pair, localIndex);

            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) *
                Mathf.Lerp(0.035f, 0.18f, (float)random.NextDouble());
            float u = center.x * CellSize + Mathf.Cos(angle) * radial;
            float v = center.y * CellSize + Mathf.Sin(angle) * radial * 0.82f;
            float currentDepth =
                Mathf.Lerp(0.006f, 0.018f, (float)random.NextDouble());
            float normalSpeed =
                Mathf.Lerp(1.35f, 2.15f, (float)random.NextDouble());
            float tangentU =
                Mathf.Lerp(-0.28f, 0.28f, (float)random.NextDouble());
            float tangentV =
                Mathf.Lerp(-0.18f, 0.24f, (float)random.NextDouble());
            float restVolume =
                Mathf.Lerp(2.2e-8f, 4.6e-8f, (float)random.NextDouble());

            expectedVolume += restVolume;
            positions[i] = new Vector4(u, -currentDepth, v, 0.00145f);
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

    private static Color ResolvePairColor(int pair, int localIndex)
    {
        bool first = (localIndex & 1) == 0;
        Color red = new Color(0.95f, 0.035f, 0.03f, 1.0f);
        Color blue = new Color(0.035f, 0.10f, 0.95f, 1.0f);
        Color yellow = new Color(1.0f, 0.82f, 0.035f, 1.0f);
        Color white = new Color(0.94f, 0.92f, 0.84f, 1.0f);
        Color black = new Color(0.025f, 0.023f, 0.020f, 1.0f);

        return pair switch
        {
            0 => first ? red : blue,
            1 => first ? yellow : blue,
            2 => first ? red : white,
            _ => first ? yellow : black
        };
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

    private static GameObject CreateBoard(Material material)
    {
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Plane);
        board.name = "G30 Pigment Mixing Board";
        float center = GridSize * CellSize * 0.5f;
        board.transform.position = new Vector3(center, center, 0.0f);
        board.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
        board.transform.localScale = new Vector3(0.54f, 1.0f, 0.54f);
        board.GetComponent<MeshRenderer>().sharedMaterial = material;
        return board;
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

        cameraObject = new GameObject("G30 Pigment Mixing Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(2.56f, 2.46f, -5.25f);
        camera.transform.LookAt(new Vector3(2.56f, 2.56f, 0.0f));
        camera.fieldOfView = 30.0f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.058f, 0.075f, 1.0f);
        camera.allowHDR = true;

        cameraTarget = new RenderTexture(
            1280,
            900,
            24,
            RenderTextureFormat.ARGB32
        )
        {
            antiAliasing = 4
        };
        cameraTarget.Create();
        camera.targetTexture = cameraTarget;

        lightObject = new GameObject("G30 Pigment Mixing Key Light");
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

        var image = new Texture2D(
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
            image = new Texture2D(
                GridSize,
                GridSize,
                TextureFormat.RGB24,
                false,
                false
            );
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

    private static RenderTexture NewRenderTexture(int width, int height)
    {
        var texture = new RenderTexture(
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

    private static float ActiveWeightedColorDifference(
        PaintCellData[] rgb,
        PaintCellData[] pigment)
    {
        int count = Math.Min(rgb.Length, pigment.Length);
        double difference = 0.0;
        double weight = 0.0;

        for (int i = 0; i < count; i++)
        {
            int thickness =
                Math.Max(rgb[i].ThicknessInt, 0) +
                Math.Max(pigment[i].ThicknessInt, 0);
            if (thickness <= 0)
                continue;

            Vector4 a = rgb[i].Color;
            Vector4 b = pigment[i].Color;
            difference +=
                (
                    Math.Abs(a.x - b.x) +
                    Math.Abs(a.y - b.y) +
                    Math.Abs(a.z - b.z)
                ) / 3.0 * thickness;
            weight += thickness;
        }

        return weight > 0.0 ? (float)(difference / weight) : 0.0f;
    }

    private static float Luminance(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    private static string ColorSummary(Color color)
    {
        return $"({color.r:F3},{color.g:F3},{color.b:F3})";
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
        Require(asset != null, $"Missing pigment validation asset: {path}");
        return asset;
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

    private struct ModeResult
    {
        public PaintColorMixingMode Mode;
        public PaintCellData[] Cells;
        public SurfaceImpactDiagnostics Diagnostics;
        public float DepositError;
        public double DepositedVolume;
        public int ActiveCells;
        public Color RedBlue;
        public Color YellowBlue;
        public Color RedWhite;
        public Color YellowBlack;
    }

    private struct FlowResult
    {
        public float VolumeDrift;
        public int ActiveCellGrowth;
    }
}
