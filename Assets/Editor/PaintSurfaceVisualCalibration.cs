using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;

public static class PaintSurfaceVisualCalibration
{
    private const int GridSize = 256;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const float OpacityDepth = 0.00003f;

    public static void Run()
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
        GameObject cameraObject = null;
        GameObject lightObject = null;
        Material runtimeMaterial = null;
        RenderTexture cameraTarget = null;

        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G28_VisualCalibration"
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

            BuildImpactParticles(
                out Vector4[] positionData,
                out Vector4[] velocityData,
                out Vector4[] colorData,
                out Vector4[] stateData,
                out Vector4[] volumeData
            );

            positions = NewBuffer(positionData);
            velocities = NewBuffer(velocityData);
            colors = NewBuffer(colorData);
            states = NewBuffer(stateData);
            volumes = NewBuffer(volumeData);

            PaintProperties paint = PaintProperties.Create(
                Density,
                0.85f,
                0.035f,
                new Color(0.08f, 0.32f, 0.95f, 1.0f)
            );
            depositor = new PaintDepositor(impact, grid, paint);
            depositor.ConfigureImpactRheology(
                true,
                2.6f,
                0.14f,
                0.8f,
                2.2f,
                0.55f,
                0.0f
            );
            depositor.DispatchFromMlsMpmBuffers(
                positions,
                velocities,
                colors,
                states,
                volumes,
                positionData.Length,
                SurfaceProperties.Wood,
                true,
                true,
                true,
                false
            );

            PaintCellData[] wetCells = ReadCells(grid);
            AddWetMaterialSwatch(wetCells);
            grid.PaintCellBuffer.SetData(wetCells);
            FilmMetrics wetMetrics = MeasureFilm(wetCells);

            board = GameObject.CreatePrimitive(PrimitiveType.Plane);
            board.name = "G28 Visual Calibration Board";
            board.transform.position = Vector3.zero;
            board.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
            board.transform.localScale = new Vector3(0.23f, 1.0f, 0.23f);

            MeshRenderer meshRenderer = board.GetComponent<MeshRenderer>();
            runtimeMaterial = new Material(sourceMaterial)
            {
                name = "G28 Visual Calibration Material"
            };
            meshRenderer.sharedMaterial = runtimeMaterial;

            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                meshRenderer
            )
            {
                MaxThickness = OpacityDepth,
                WetnessShine = 0.8f
            };

            ConfigureMaterial(runtimeMaterial);
            ConfigureRenderEnvironment(
                out cameraObject,
                out lightObject,
                out Camera camera,
                out cameraTarget
            );

            surfaceRenderer.Render();
            Color32[] wetPixels = Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G28_WetImpact.png")
            );

            PaintCellData[] dryCells = (PaintCellData[])wetCells.Clone();
            for (int i = 0; i < dryCells.Length; i++)
            {
                if (dryCells[i].IsActive == 0u)
                    continue;

                dryCells[i].Wetness = 0.0f;
                dryCells[i].Age = 60.0f;
                dryCells[i].FlowVelocity = Vector2.zero;
            }

            grid.PaintCellBuffer.SetData(dryCells);
            surfaceRenderer.Render();
            Color32[] dryPixels = Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G28_DryImpact.png")
            );
            float wetDryVisualDifference =
                MeanAbsolutePixelDifference(wetPixels, dryPixels);
            Require(
                wetDryVisualDifference > 0.001f,
                $"Wet/dry visual response is too weak: {wetDryVisualDifference:F5}."
            );

            grid.PaintCellBuffer.SetData(wetCells);
            PaintEvolver evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.02f,
                DiffusionRate = 7.5f,
                RunoffRate = 0.28f,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = new Vector2(0.0f, -9.81f),
                PaintDensity = Density,
                DynamicViscosity = 0.85f,
                SurfaceTension = 0.035f,
                YieldStress = 0.0f,
                ContactLineThickness = 0.000025f,
                ContactAngleResistance = 0.72f,
                SubstrateFlowVariation = 0.32f,
                SurfaceRoughness = SurfaceProperties.Wood.Roughness,
                DripFingerInstability = 0.42f,
                ThinFilmCohesion = 0.58f
            };

            for (int step = 0; step < 360; step++)
                evolver.Evolve(1.0f / 60.0f);

            PaintCellData[] flowedCells = ReadCells(grid);
            FilmMetrics flowMetrics = MeasureFilm(flowedCells);
            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G28_VerticalFlow_6s.png")
            );

            double volumeDrift = Math.Abs(
                flowMetrics.ThicknessUnits - wetMetrics.ThicknessUnits
            ) / (double)Math.Max(wetMetrics.ThicknessUnits, 1L);
            float downwardShiftCells =
                wetMetrics.CentroidY - flowMetrics.CentroidY;

            Require(volumeDrift < 0.015, $"Visual flow volume drift is {volumeDrift:P2}.");
            Require(
                downwardShiftCells > 0.2f,
                $"Expected downward paint motion, measured {downwardShiftCells:F3} cells."
            );
            Require(
                flowMetrics.ActiveCellCount > wetMetrics.ActiveCellCount,
                "The flow did not create a wider connected film footprint."
            );

            Debug.Log(
                "[PaintSurfaceVisualCalibration] PASS " +
                $"wetCells={wetMetrics.ActiveCellCount}, " +
                $"flowCells={flowMetrics.ActiveCellCount}, " +
                $"downwardShift={downwardShiftCells:F2} cells, " +
                $"volumeDrift={volumeDrift:P2}, " +
                $"wetDryDifference={wetDryVisualDifference:F4}, " +
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
            if (cameraTarget != null)
            {
                cameraTarget.Release();
                UnityEngine.Object.DestroyImmediate(cameraTarget);
            }

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
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (lightObject != null)
                UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }

    private static void BuildImpactParticles(
        out Vector4[] positions,
        out Vector4[] velocities,
        out Vector4[] colors,
        out Vector4[] states,
        out Vector4[] volumes)
    {
        const int count = 132;
        positions = new Vector4[count];
        velocities = new Vector4[count];
        colors = new Vector4[count];
        states = new Vector4[count];
        volumes = new Vector4[count];

        var random = new System.Random(281104);

        for (int i = 0; i < count; i++)
        {
            int group = i < 72 ? 0 : (i < 108 ? 1 : 2);
            Vector2 center =
                group == 0
                    ? new Vector2(1.10f, 1.65f)
                    : group == 1
                        ? new Vector2(1.42f, 1.34f)
                        : new Vector2(0.78f, 1.06f);
            float clusterRadius =
                group == 0 ? 0.055f : (group == 1 ? 0.04f : 0.03f);
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) * clusterRadius;
            float px = center.x + Mathf.Cos(angle) * radial;
            float pz = center.y + Mathf.Sin(angle) * radial;
            float tangential =
                group == 0 ? 0.65f : (group == 1 ? -0.35f : 0.18f);
            float speed = 1.4f + (float)random.NextDouble() * 1.8f;
            float restVolume =
                3.5e-8f + (float)random.NextDouble() * 3.5e-8f;
            Color color =
                group == 0
                    ? new Color(0.04f, 0.24f, 0.95f, 1.0f)
                    : group == 1
                        ? new Color(0.95f, 0.06f, 0.12f, 1.0f)
                        : new Color(1.0f, 0.62f, 0.02f, 1.0f);

            positions[i] = new Vector4(px, 0.001f, pz, 0.0016f);
            velocities[i] = new Vector4(
                tangential,
                -speed,
                group == 2 ? -0.2f : 0.08f,
                restVolume * Density
            );
            colors[i] = color;
            states[i] = new Vector4(6.0f, 0.0f, 0.0f, i + 1);
            volumes[i] = new Vector4(restVolume, 1.0f, Density, 1.0f);
        }
    }

    private static void ConfigureMaterial(Material material)
    {
        material.SetFloat("_CanvasSmoothness", 0.22f);
        material.SetFloat("_DryPaintSmoothness", 0.46f);
        material.SetFloat("_WetPaintSmoothness", 0.93f);
        material.SetFloat("_PaintNormalStrength", 3.2f);
        material.SetFloat("_ParallaxStrength", 0.005f);
        material.SetFloat("_EdgeRidgeStrength", 5.0f);
        material.SetFloat("_EdgeDarkening", 0.06f);
        material.SetFloat("_MicroNormalStrength", 0.02f);
        material.SetFloat("_CanvasGrainStrength", 0.035f);
        material.SetFloat("_CanvasGrainScale", 190.0f);
        material.SetFloat("_PigmentSaturation", 1.12f);
        material.SetFloat("_WetDarkening", 0.06f);
        material.SetFloat("_EdgeHighlightStrength", 0.34f);
        material.SetFloat("_WetSpecularStrength", 1.05f);
        material.SetFloat("_ClearCoatStrength", 1.0f);
        material.SetFloat("_EnvironmentReflection", 0.55f);
        material.SetFloat("_FresnelStrength", 0.2f);
    }

    private static void AddWetMaterialSwatch(PaintCellData[] cells)
    {
        const int centerX = 188;
        const int centerY = 76;
        const float radiusX = 34.0f;
        const float radiusY = 17.0f;
        Color swatchColor = new Color(0.03f, 0.62f, 0.42f, 1.0f);

        for (int y = centerY - 22; y <= centerY + 22; y++)
        {
            for (int x = centerX - 40; x <= centerX + 40; x++)
            {
                if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
                    continue;

                float nx = (x - centerX) / radiusX;
                float ny = (y - centerY) / radiusY;
                float angle = Mathf.Atan2(ny, nx);
                float edgeShape =
                    1.0f +
                    Mathf.Sin(angle * 5.0f + 0.7f) * 0.09f +
                    Mathf.Sin(angle * 9.0f - 1.1f) * 0.04f;
                float radial = Mathf.Sqrt(nx * nx + ny * ny) / edgeShape;

                if (radial > 1.0f)
                    continue;

                float core = Mathf.Exp(-radial * radial * 2.4f);
                float rim = Mathf.Exp(
                    -Mathf.Pow((radial - 0.82f) / 0.12f, 2.0f)
                );
                float thickness = 0.000035f +
                    core * 0.00014f +
                    rim * 0.00009f;
                int index = y * GridSize + x;

                cells[index] = new PaintCellData
                {
                    ThicknessInt = PaintCellData.ToThicknessInt(thickness),
                    Wetness = 1.0f,
                    Age = 0.0f,
                    IsActive = 1u,
                    Color = swatchColor,
                    FlowVelocity = new Vector2(0.004f, -0.008f),
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
        RenderSettings.reflectionIntensity = 0.75f;

        cameraObject = new GameObject("G28 Calibration Camera");
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

        lightObject = new GameObject("G28 Calibration Key Light");
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
        image.ReadPixels(
            new Rect(0, 0, target.width, target.height),
            0,
            0
        );
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
        int activeCells = 0;

        for (int index = 0; index < cells.Length; index++)
        {
            int thickness = Math.Max(cells[index].ThicknessInt, 0);
            if (thickness <= 0)
                continue;

            int y = index / GridSize;
            thicknessUnits += thickness;
            weightedY += y * (double)thickness;
            activeCells++;
        }

        return new FilmMetrics
        {
            ThicknessUnits = thicknessUnits,
            ActiveCellCount = activeCells,
            CentroidY =
                thicknessUnits > 0
                    ? (float)(weightedY / thicknessUnits)
                    : 0.0f
        };
    }

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[grid.GridWidth * grid.GridHeight];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
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
    }
}
