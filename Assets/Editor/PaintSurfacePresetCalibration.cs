using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;

public static class PaintSurfacePresetCalibration
{
    private const int GridSize = 256;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const float BaseDynamicViscosity = 0.85f;
    private const float BaseSurfaceTension = 0.035f;

    public static void Run()
    {
        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G29_SurfacePresetCalibration"
            );
            Directory.CreateDirectory(outputDirectory);

            ComputeShader evolution = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintEvaporation.compute"
            );
            ComputeShader baker = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintFilmBaker.compute"
            );
            Material sourceMaterial = Load<Material>(
                "Assets/PaintSim/Materials/PaintSurfaceMaterial.mat"
            );

            ConfigureRenderEnvironment(
                out GameObject cameraObject,
                out GameObject lightObject,
                out Camera camera,
                out RenderTexture cameraTarget
            );

            SurfaceResult[] results = new SurfaceResult[4];
            int resultCount = 0;

            foreach (SurfaceType surfaceType in Enum.GetValues(typeof(SurfaceType)))
            {
                results[resultCount++] = RunSurfaceCase(
                    surfaceType,
                    evolution,
                    baker,
                    sourceMaterial,
                    camera,
                    cameraTarget,
                    outputDirectory
                );
            }

            SurfaceResult glass = Find(results, resultCount, SurfaceType.Glass);
            SurfaceResult fabric = Find(results, resultCount, SurfaceType.Fabric);

            Require(
                glass.DownwardShiftCells > fabric.DownwardShiftCells + 2.0f,
                $"Expected glass to run farther than fabric. " +
                $"glass={glass.DownwardShiftCells:F2}, fabric={fabric.DownwardShiftCells:F2}."
            );
            Require(
                fabric.FinalWetness < glass.FinalWetness - 0.05f,
                $"Expected fabric to absorb/dry faster than glass. " +
                $"fabric={fabric.FinalWetness:F3}, glass={glass.FinalWetness:F3}."
            );

            camera.targetTexture = null;
            cameraTarget.Release();
            UnityEngine.Object.DestroyImmediate(cameraTarget);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);

            Debug.Log(
                "[PaintSurfacePresetCalibration] PASS " +
                Summary(results, resultCount) +
                $" output={outputDirectory}"
            );
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static SurfaceResult RunSurfaceCase(
        SurfaceType surfaceType,
        ComputeShader evolution,
        ComputeShader baker,
        Material sourceMaterial,
        Camera camera,
        RenderTexture cameraTarget,
        string outputDirectory)
    {
        PaintFilmGrid grid = null;
        PaintSurfaceRenderer surfaceRenderer = null;
        GameObject board = null;
        Material material = null;

        try
        {
            SurfaceProperties surface = SurfaceProperties.FromType(surfaceType);
            SurfaceFilmInteraction filmPreset =
                SurfaceFilmInteraction.FromType(surfaceType);
            SurfaceVisualProperties visual =
                SurfaceVisualProperties.FromType(surfaceType);

            grid = new PaintFilmGrid(
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
                0.03f,
                true
            );

            PaintCellData[] wetCells = BuildSeedFilm(surfaceType);
            grid.PaintCellBuffer.SetData(wetCells);
            FilmMetrics wetMetrics = MeasureFilm(wetCells);

            board = GameObject.CreatePrimitive(PrimitiveType.Plane);
            board.name = $"G29 {surfaceType} Calibration Board";
            board.transform.position = Vector3.zero;
            board.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
            board.transform.localScale = new Vector3(0.23f, 1.0f, 0.23f);

            MeshRenderer meshRenderer = board.GetComponent<MeshRenderer>();
            material = new Material(sourceMaterial)
            {
                name = $"G29 {surfaceType} Surface Material"
            };
            meshRenderer.sharedMaterial = material;

            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                meshRenderer
            );
            surfaceRenderer.ConfigureSurfaceAppearance(visual);
            surfaceRenderer.Render();
            Color32[] wetPixels = Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, $"G29_{surfaceType}_Wet.png")
            );

            PaintCellData[] dryCells = (PaintCellData[])wetCells.Clone();
            for (int i = 0; i < dryCells.Length; i++)
            {
                if (dryCells[i].IsActive == 0u)
                    continue;

                dryCells[i].Wetness = 0.0f;
                dryCells[i].Age = 90.0f;
                dryCells[i].FlowVelocity = Vector2.zero;
            }

            grid.PaintCellBuffer.SetData(dryCells);
            surfaceRenderer.Render();
            Color32[] dryPixels = Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, $"G29_{surfaceType}_Dry.png")
            );
            float wetDryDifference =
                MeanAbsolutePixelDifference(wetPixels, dryPixels);

            grid.PaintCellBuffer.SetData(wetCells);
            var evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.02f * filmPreset.EvaporationMultiplier,
                DiffusionRate = 7.5f * filmPreset.DiffusionMultiplier,
                RunoffRate = 0.28f * filmPreset.RunoffMultiplier,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = new Vector2(0.0f, -9.81f),
                PaintDensity = Density,
                DynamicViscosity = BaseDynamicViscosity,
                SurfaceTension = BaseSurfaceTension,
                YieldStress = 0.0f,
                ContactLineThickness = filmPreset.ContactLineThickness,
                ContactAngleResistance = filmPreset.ContactAngleResistance,
                SubstrateFlowVariation = filmPreset.SubstrateFlowVariation,
                SurfaceRoughness = surface.Roughness,
                DripFingerInstability = filmPreset.DripFingerInstability,
                ThinFilmCohesion = filmPreset.ThinFilmCohesion,
                SurfaceAbsorptionRate = surface.AbsorptionRate
            };

            for (int step = 0; step < 360; step++)
                evolver.Evolve(1.0f / 60.0f);

            PaintCellData[] flowedCells = ReadCells(grid);
            FilmMetrics flowMetrics = MeasureFilm(flowedCells);
            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, $"G29_{surfaceType}_Flow_6s.png")
            );

            double volumeDrift = Math.Abs(
                flowMetrics.ThicknessUnits - wetMetrics.ThicknessUnits
            ) / (double)Math.Max(wetMetrics.ThicknessUnits, 1L);
            float downwardShiftCells =
                wetMetrics.CentroidY - flowMetrics.CentroidY;

            Require(
                wetDryDifference > 0.00045f,
                $"{surfaceType} wet/dry response is too weak: {wetDryDifference:F5}."
            );
            Require(
                volumeDrift < 0.025,
                $"{surfaceType} flow volume drift is {volumeDrift:P2}."
            );
            Require(
                flowMetrics.ActiveCellCount >= wetMetrics.ActiveCellCount,
                $"{surfaceType} flow did not preserve or expand the film footprint."
            );

            return new SurfaceResult
            {
                SurfaceType = surfaceType,
                WetDryDifference = wetDryDifference,
                DownwardShiftCells = downwardShiftCells,
                VolumeDrift = (float)volumeDrift,
                InitialActiveCells = wetMetrics.ActiveCellCount,
                FinalActiveCells = flowMetrics.ActiveCellCount,
                FinalWetness = flowMetrics.WeightedWetness
            };
        }
        finally
        {
            surfaceRenderer?.Dispose();
            grid?.Dispose();

            if (material != null)
                UnityEngine.Object.DestroyImmediate(material);
            if (board != null)
                UnityEngine.Object.DestroyImmediate(board);
        }
    }

    private static PaintCellData[] BuildSeedFilm(SurfaceType surfaceType)
    {
        var cells = new PaintCellData[GridSize * GridSize];
        Color swatchColor = surfaceType switch
        {
            SurfaceType.Glass => new Color(0.04f, 0.28f, 0.95f, 1.0f),
            SurfaceType.Fabric => new Color(0.95f, 0.06f, 0.12f, 1.0f),
            SurfaceType.Metal => new Color(1.0f, 0.62f, 0.02f, 1.0f),
            _ => new Color(0.03f, 0.62f, 0.42f, 1.0f)
        };

        AddIrregularSwatch(
            cells,
            new Vector2(128.0f, 174.0f),
            new Vector2(34.0f, 19.0f),
            swatchColor,
            new Vector2(0.006f, -0.016f)
        );
        AddIrregularSwatch(
            cells,
            new Vector2(116.0f, 122.0f),
            new Vector2(15.0f, 10.0f),
            swatchColor * 0.92f,
            new Vector2(0.002f, -0.010f)
        );

        return cells;
    }

    private static void AddIrregularSwatch(
        PaintCellData[] cells,
        Vector2 center,
        Vector2 radii,
        Color color,
        Vector2 flowVelocity)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radii.x * 1.45f));
        int maxX = Mathf.Min(GridSize - 1, Mathf.CeilToInt(center.x + radii.x * 1.45f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radii.y * 1.55f));
        int maxY = Mathf.Min(GridSize - 1, Mathf.CeilToInt(center.y + radii.y * 1.55f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float nx = (x - center.x) / Mathf.Max(radii.x, 1.0f);
                float ny = (y - center.y) / Mathf.Max(radii.y, 1.0f);
                float angle = Mathf.Atan2(ny, nx);
                float edgeShape =
                    1.0f +
                    Mathf.Sin(angle * 5.0f + 0.7f) * 0.10f +
                    Mathf.Sin(angle * 9.0f - 1.1f) * 0.055f +
                    Mathf.Sin(angle * 14.0f + 2.2f) * 0.030f;
                float radial = Mathf.Sqrt(nx * nx + ny * ny) / edgeShape;

                if (radial > 1.0f)
                    continue;

                float core = Mathf.Exp(-radial * radial * 2.35f);
                float rim = Mathf.Exp(-Mathf.Pow((radial - 0.83f) / 0.12f, 2.0f));
                float thickness = 0.000034f + core * 0.00013f + rim * 0.00008f;
                int index = y * GridSize + x;

                cells[index] = new PaintCellData
                {
                    ThicknessInt = PaintCellData.ToThicknessInt(thickness),
                    Wetness = 1.0f,
                    Age = 0.0f,
                    IsActive = 1u,
                    Color = color,
                    FlowVelocity = flowVelocity,
                    PaintDensity = Density
                };
            }
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

        cameraObject = new GameObject("G29 Surface Preset Calibration Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0.42f, 0.26f, -3.05f);
        camera.transform.LookAt(Vector3.zero);
        camera.fieldOfView = 27.0f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.065f, 0.085f, 1.0f);
        camera.allowHDR = true;

        cameraTarget = new RenderTexture(
            1024,
            768,
            24,
            RenderTextureFormat.ARGB32
        )
        {
            antiAliasing = 4
        };
        cameraTarget.Create();
        camera.targetTexture = cameraTarget;

        lightObject = new GameObject("G29 Surface Preset Calibration Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1.0f, 0.91f, 0.80f, 1.0f);
        light.intensity = 1.55f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(16.0f, -18.0f, 0.0f);
    }

    private static Color32[] Capture(
        Camera camera,
        RenderTexture target,
        string path)
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
        Color32[] pixels = image.GetPixels32();
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = previous;
        return pixels;
    }

    private static float MeanAbsolutePixelDifference(
        Color32[] first,
        Color32[] second)
    {
        int count = Mathf.Min(first.Length, second.Length);
        if (count <= 0)
            return 0.0f;

        double difference = 0.0;
        for (int i = 0; i < count; i++)
        {
            difference += Math.Abs(first[i].r - second[i].r);
            difference += Math.Abs(first[i].g - second[i].g);
            difference += Math.Abs(first[i].b - second[i].b);
        }

        return (float)(difference / (count * 3.0 * 255.0));
    }

    private static FilmMetrics MeasureFilm(PaintCellData[] cells)
    {
        long thicknessUnits = 0L;
        double weightedY = 0.0;
        double weightedWetness = 0.0;
        int activeCells = 0;

        for (int index = 0; index < cells.Length; index++)
        {
            int thickness = Math.Max(cells[index].ThicknessInt, 0);
            if (thickness <= 0)
                continue;

            int y = index / GridSize;
            thicknessUnits += thickness;
            weightedY += y * (double)thickness;
            weightedWetness += Mathf.Clamp01(cells[index].Wetness) * (double)thickness;
            activeCells++;
        }

        return new FilmMetrics
        {
            ThicknessUnits = thicknessUnits,
            ActiveCellCount = activeCells,
            CentroidY =
                thicknessUnits > 0
                    ? (float)(weightedY / thicknessUnits)
                    : 0.0f,
            WeightedWetness =
                thicknessUnits > 0
                    ? (float)(weightedWetness / thicknessUnits)
                    : 0.0f
        };
    }

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[grid.GridWidth * grid.GridHeight];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
    }

    private static SurfaceResult Find(
        SurfaceResult[] results,
        int resultCount,
        SurfaceType surfaceType)
    {
        for (int i = 0; i < resultCount; i++)
        {
            if (results[i].SurfaceType == surfaceType)
                return results[i];
        }

        throw new InvalidOperationException($"Missing result for {surfaceType}.");
    }

    private static string Summary(SurfaceResult[] results, int resultCount)
    {
        string summary = string.Empty;
        for (int i = 0; i < resultCount; i++)
        {
            SurfaceResult result = results[i];
            if (i > 0)
                summary += " | ";

            summary +=
                $"{result.SurfaceType}: wetDry={result.WetDryDifference:F4}, " +
                $"shift={result.DownwardShiftCells:F2}, " +
                $"drift={result.VolumeDrift:P2}, " +
                $"cells={result.InitialActiveCells}->{result.FinalActiveCells}, " +
                $"wet={result.FinalWetness:F3}";
        }

        return summary;
    }

    private static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, $"Missing calibration asset: {path}");
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
        public float CentroidY;
        public float WeightedWetness;
    }

    private struct SurfaceResult
    {
        public SurfaceType SurfaceType;
        public float WetDryDifference;
        public float DownwardShiftCells;
        public float VolumeDrift;
        public int InitialActiveCells;
        public int FinalActiveCells;
        public float FinalWetness;
    }
}
