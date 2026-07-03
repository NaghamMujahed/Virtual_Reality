using System;
using System.IO;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;
using UnityEditor;
using UnityEngine;

public static class PaintSurfaceMotionMaterialValidation
{
    private const int GridSize = 256;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const float ImpactDeltaTime = 1.0f / 60.0f;
    private const int MovingParticleCount = 16384;
    private const int SplashParticleCount = 8192;

    public static void Run()
    {
        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G30C_SurfaceMotionMaterial"
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

            MovingSurfaceResult moving = RunMovingTiltedSurfaceCase(
                impact,
                evolution,
                baker,
                outputDirectory
            );
            SplashResult glass = RunSplashMaterialCase(
                SurfaceType.Glass,
                impact,
                baker,
                outputDirectory
            );
            SplashResult fabric = RunSplashMaterialCase(
                SurfaceType.Fabric,
                impact,
                baker,
                outputDirectory
            );

            Require(
                moving.ImpactedFraction > 0.985f &&
                moving.DepositedFraction > 0.975f,
                $"Moving tilted board did not capture enough paint. " +
                $"impacted={moving.ImpactedFraction:P2}, deposited={moving.DepositedFraction:P2}."
            );
            Require(
                moving.VolumeError < 0.04f,
                $"Moving tilted board volume error is {moving.VolumeError:P2}."
            );
            Require(
                moving.DownhillShiftCells > 5.0f,
                $"Paint did not flow downhill after moving-board impact. " +
                $"shift={moving.DownhillShiftCells:F2} cells."
            );

            Require(
                glass.RetainedMassFraction > 0.25f &&
                glass.AirborneFraction > 0.20f,
                $"Glass high-energy splash should retain airborne residual paint. " +
                $"retained={glass.RetainedMassFraction:P2}, airborne={glass.AirborneFraction:P2}."
            );
            Require(
                fabric.RetainedMassFraction < 0.08f &&
                fabric.DepositedVolumeFraction > glass.DepositedVolumeFraction + 0.35f,
                $"Fabric should capture much more paint than glass. " +
                $"fabricDeposit={fabric.DepositedVolumeFraction:P2}, " +
                $"glassDeposit={glass.DepositedVolumeFraction:P2}, " +
                $"fabricRetained={fabric.RetainedMassFraction:P2}."
            );
            Require(
                glass.ActiveCells > fabric.ActiveCells,
                $"Glass should create a broader splash footprint than fabric. " +
                $"glassCells={glass.ActiveCells}, fabricCells={fabric.ActiveCells}."
            );

            Debug.Log(
                "[PaintSurfaceMotionMaterialValidation] PASS " +
                $"movingImpacted={moving.ImpactedFraction:P2}, " +
                $"movingDeposited={moving.DepositedFraction:P2}, " +
                $"movingVolumeError={moving.VolumeError:P2}, " +
                $"movingDownhill={moving.DownhillShiftCells:F2}, " +
                $"glassDeposit={glass.DepositedVolumeFraction:P2}, " +
                $"glassRetained={glass.RetainedMassFraction:P2}, " +
                $"glassAirborne={glass.AirborneFraction:P2}, " +
                $"fabricDeposit={fabric.DepositedVolumeFraction:P2}, " +
                $"fabricRetained={fabric.RetainedMassFraction:P2}, " +
                $"cells glass/fabric={glass.ActiveCells}/{fabric.ActiveCells}, " +
                $"output={outputDirectory}"
            );
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static MovingSurfaceResult RunMovingTiltedSurfaceCase(
        ComputeShader impact,
        ComputeShader evolution,
        ComputeShader baker,
        string outputDirectory)
    {
        PaintFilmGrid grid = null;
        PaintDepositor depositor = null;
        GraphicsBuffer positions = null;
        GraphicsBuffer velocities = null;
        GraphicsBuffer colors = null;
        GraphicsBuffer states = null;
        GraphicsBuffer volumes = null;

        try
        {
            float worldSize = GridSize * CellSize;
            Quaternion previousRotation = Quaternion.Euler(8.0f, 0.0f, -4.0f);
            Quaternion currentRotation = Quaternion.Euler(24.0f, 0.0f, -11.0f);
            Vector3 previousAxisU = (previousRotation * Vector3.right).normalized;
            Vector3 previousAxisV = (previousRotation * Vector3.forward).normalized;
            Vector3 previousNormal = (previousRotation * Vector3.up).normalized;
            Vector3 currentAxisU = (currentRotation * Vector3.right).normalized;
            Vector3 currentAxisV = (currentRotation * Vector3.forward).normalized;
            Vector3 currentNormal = (currentRotation * Vector3.up).normalized;
            Vector3 currentCenter = new Vector3(0.0f, 1.15f, 0.0f);
            Vector3 previousCenter = currentCenter - currentNormal * 0.18f;
            Vector3 previousOrigin =
                previousCenter -
                previousAxisU * (worldSize * 0.5f) -
                previousAxisV * (worldSize * 0.5f);
            Vector3 currentOrigin =
                currentCenter -
                currentAxisU * (worldSize * 0.5f) -
                currentAxisV * (worldSize * 0.5f);

            grid = new PaintFilmGrid(
                GridSize,
                GridSize,
                CellSize,
                CellSize,
                previousOrigin.y,
                new Vector2(previousOrigin.x, previousOrigin.z),
                previousOrigin,
                previousAxisU,
                previousAxisV,
                previousNormal,
                0.03f,
                true
            );
            grid.ReconfigureSurfaceFrame(
                CellSize,
                CellSize,
                currentOrigin.y,
                new Vector2(currentOrigin.x, currentOrigin.z),
                currentOrigin,
                currentAxisU,
                currentAxisV,
                currentNormal,
                0.03f,
                true
            );

            BuildMovingBoardParticles(
                currentOrigin,
                currentAxisU,
                currentAxisV,
                currentNormal,
                out Vector4[] positionData,
                out Vector4[] velocityData,
                out Vector4[] colorData,
                out Vector4[] stateData,
                out Vector4[] volumeData,
                out double expectedVolume
            );

            positions = NewBuffer(positionData);
            velocities = NewBuffer(velocityData);
            colors = NewBuffer(colorData);
            states = NewBuffer(stateData);
            volumes = NewBuffer(volumeData);

            PaintProperties paint = PaintProperties.Create(
                Density,
                0.9f,
                0.036f,
                new Color(0.95f, 0.06f, 0.03f, 1.0f)
            );
            depositor = new PaintDepositor(impact, grid, paint);
            depositor.ConfigureImpactRheology(
                true,
                2.6f,
                0.14f,
                0.8f,
                2.2f,
                0.58f,
                0.0f
            );

            depositor.DispatchFromMlsMpmBuffers(
                positions,
                velocities,
                colors,
                states,
                volumes,
                MovingParticleCount,
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
            ParticleMetrics particleMetrics = ReadParticleMetrics(
                states,
                velocities,
                MovingParticleCount,
                expectedVolume * Density
            );
            double depositedVolume =
                impactMetrics.ThicknessUnits /
                (double)PaintCellData.ThicknessScale *
                CellSize *
                CellSize;
            float volumeError = (float)(
                Math.Abs(depositedVolume - expectedVolume) /
                Math.Max(expectedVolume, 1e-12)
            );

            SaveBakedAtlas(
                baker,
                grid,
                SurfaceVisualProperties.Wood,
                Path.Combine(
                    outputDirectory,
                    "G30C_MovingTiltedSurface_Impact_Atlas.png"
                )
            );

            Vector2 projectedGravity = new Vector2(
                Vector3.Dot(Physics.gravity, currentAxisU),
                Vector3.Dot(Physics.gravity, currentAxisV)
            );
            var evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.01f,
                DiffusionRate = 7.5f,
                RunoffRate = 0.34f,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = projectedGravity,
                PaintDensity = Density,
                DynamicViscosity = 0.9f,
                SurfaceTension = 0.036f,
                YieldStress = 0.0f,
                ContactLineThickness = SurfaceFilmInteraction.Wood.ContactLineThickness,
                ContactAngleResistance = SurfaceFilmInteraction.Wood.ContactAngleResistance,
                SubstrateFlowVariation = SurfaceFilmInteraction.Wood.SubstrateFlowVariation,
                SurfaceRoughness = SurfaceProperties.Wood.Roughness,
                DripFingerInstability = SurfaceFilmInteraction.Wood.DripFingerInstability,
                ThinFilmCohesion = SurfaceFilmInteraction.Wood.ThinFilmCohesion,
                SurfaceAbsorptionRate = SurfaceProperties.Wood.AbsorptionRate
            };

            for (int step = 0; step < 240; step++)
                evolver.Evolve(1.0f / 60.0f);

            PaintCellData[] flowCells = ReadCells(grid);
            FilmMetrics flowMetrics = MeasureFilm(flowCells);
            Vector2 flowDelta = flowMetrics.Centroid - impactMetrics.Centroid;
            float downhillShift = Vector2.Dot(
                flowDelta,
                projectedGravity.normalized
            );

            SaveBakedAtlas(
                baker,
                grid,
                SurfaceVisualProperties.Wood,
                Path.Combine(
                    outputDirectory,
                    "G30C_MovingTiltedSurface_Flow_4s_Atlas.png"
                )
            );

            return new MovingSurfaceResult
            {
                ImpactedFraction = diagnostics.Impacted / (float)MovingParticleCount,
                DepositedFraction =
                    particleMetrics.DepositedCount / (float)MovingParticleCount,
                VolumeError = volumeError,
                DownhillShiftCells = downhillShift,
                ActiveCells = flowMetrics.ActiveCellCount
            };
        }
        finally
        {
            depositor?.Dispose();
            grid?.Dispose();
            positions?.Release();
            velocities?.Release();
            colors?.Release();
            states?.Release();
            volumes?.Release();
        }
    }

    private static SplashResult RunSplashMaterialCase(
        SurfaceType surfaceType,
        ComputeShader impact,
        ComputeShader baker,
        string outputDirectory)
    {
        PaintFilmGrid grid = null;
        PaintDepositor depositor = null;
        GraphicsBuffer positions = null;
        GraphicsBuffer velocities = null;
        GraphicsBuffer colors = null;
        GraphicsBuffer states = null;
        GraphicsBuffer volumes = null;

        try
        {
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

            BuildHighEnergySplashParticles(
                out Vector4[] positionData,
                out Vector4[] velocityData,
                out Vector4[] colorData,
                out Vector4[] stateData,
                out Vector4[] volumeData,
                out double expectedVolume,
                out double expectedMass
            );

            positions = NewBuffer(positionData);
            velocities = NewBuffer(velocityData);
            colors = NewBuffer(colorData);
            states = NewBuffer(stateData);
            volumes = NewBuffer(volumeData);

            PaintProperties paint = PaintProperties.Create(
                Density,
                0.06f,
                0.035f,
                new Color(0.04f, 0.22f, 0.95f, 1.0f)
            );
            depositor = new PaintDepositor(impact, grid, paint);
            depositor.ConfigureImpactRheology(
                false,
                0.06f,
                0.06f,
                0.0f,
                2.0f,
                1.0f,
                0.0f
            );

            SurfaceProperties surface = SurfaceProperties.FromType(surfaceType);
            depositor.DispatchFromMlsMpmBuffers(
                positions,
                velocities,
                colors,
                states,
                volumes,
                SplashParticleCount,
                surface,
                true,
                true,
                true,
                true,
                ImpactDeltaTime
            );

            PaintCellData[] cells = ReadCells(grid);
            FilmMetrics metrics = MeasureFilm(cells);
            ParticleMetrics particleMetrics = ReadParticleMetrics(
                states,
                velocities,
                SplashParticleCount,
                expectedMass
            );
            double depositedVolume =
                metrics.ThicknessUnits /
                (double)PaintCellData.ThicknessScale *
                CellSize *
                CellSize;

            SaveBakedAtlas(
                baker,
                grid,
                SurfaceVisualProperties.FromType(surfaceType),
                Path.Combine(
                    outputDirectory,
                    $"G30C_{surfaceType}_HighEnergySplash_Atlas.png"
                )
            );

            return new SplashResult
            {
                SurfaceType = surfaceType,
                DepositedVolumeFraction = (float)(
                    depositedVolume / Math.Max(expectedVolume, 1e-12)
                ),
                RetainedMassFraction = particleMetrics.RetainedMassFraction,
                AirborneFraction =
                    particleMetrics.AirborneCount / (float)SplashParticleCount,
                ActiveCells = metrics.ActiveCellCount
            };
        }
        finally
        {
            depositor?.Dispose();
            grid?.Dispose();
            positions?.Release();
            velocities?.Release();
            colors?.Release();
            states?.Release();
            volumes?.Release();
        }
    }

    private static void BuildMovingBoardParticles(
        Vector3 origin,
        Vector3 axisU,
        Vector3 axisV,
        Vector3 normal,
        out Vector4[] positions,
        out Vector4[] velocities,
        out Vector4[] colors,
        out Vector4[] states,
        out Vector4[] volumes,
        out double expectedVolume)
    {
        positions = new Vector4[MovingParticleCount];
        velocities = new Vector4[MovingParticleCount];
        colors = new Vector4[MovingParticleCount];
        states = new Vector4[MovingParticleCount];
        volumes = new Vector4[MovingParticleCount];
        expectedVolume = 0.0;

        var random = new System.Random(30032);
        float centerU = GridSize * CellSize * 0.50f;
        float centerV = GridSize * CellSize * 0.54f;

        for (int i = 0; i < MovingParticleCount; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) *
                Mathf.Lerp(0.025f, 0.28f, (float)random.NextDouble());
            float u = centerU + Mathf.Cos(angle) * radial;
            float v = centerV + Mathf.Sin(angle) * radial * 0.72f;
            Vector3 surfacePoint = origin + axisU * u + axisV * v;
            float depth =
                Mathf.Lerp(0.042f, 0.058f, (float)random.NextDouble());
            Vector3 position = surfacePoint - normal * depth;
            float restVolume =
                Mathf.Lerp(1.6e-8f, 3.8e-8f, (float)random.NextDouble());

            expectedVolume += restVolume;

            positions[i] = new Vector4(position.x, position.y, position.z, 0.00135f);
            velocities[i] = new Vector4(0.0f, 0.0f, 0.0f, restVolume * Density);
            colors[i] = new Vector4(0.95f, 0.06f, 0.03f, 1.0f);
            states[i] = new Vector4(6.0f, 0.0f, 0.0f, i + 1);
            volumes[i] = new Vector4(restVolume, 1.0f, Density, 1.0f);
        }
    }

    private static void BuildHighEnergySplashParticles(
        out Vector4[] positions,
        out Vector4[] velocities,
        out Vector4[] colors,
        out Vector4[] states,
        out Vector4[] volumes,
        out double expectedVolume,
        out double expectedMass)
    {
        positions = new Vector4[SplashParticleCount];
        velocities = new Vector4[SplashParticleCount];
        colors = new Vector4[SplashParticleCount];
        states = new Vector4[SplashParticleCount];
        volumes = new Vector4[SplashParticleCount];
        expectedVolume = 0.0;
        expectedMass = 0.0;

        var random = new System.Random(30033);
        float centerU = GridSize * CellSize * 0.50f;
        float centerV = GridSize * CellSize * 0.53f;

        for (int i = 0; i < SplashParticleCount; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) *
                Mathf.Lerp(0.02f, 0.20f, (float)random.NextDouble());
            float u = centerU + Mathf.Cos(angle) * radial;
            float v = centerV + Mathf.Sin(angle) * radial * 0.65f;
            float restVolume =
                Mathf.Lerp(1.2e-8f, 2.6e-8f, (float)random.NextDouble());
            float normalSpeed =
                Mathf.Lerp(7.8f, 10.6f, (float)random.NextDouble());
            float tangentU =
                Mathf.Lerp(-1.1f, 1.1f, (float)random.NextDouble());
            float tangentV =
                Mathf.Lerp(0.2f, 1.7f, (float)random.NextDouble());

            expectedVolume += restVolume;
            expectedMass += restVolume * Density;

            positions[i] = new Vector4(u, -0.018f, v, 0.00135f);
            velocities[i] = new Vector4(
                tangentU,
                -normalSpeed,
                tangentV,
                restVolume * Density
            );
            colors[i] = new Vector4(0.04f, 0.22f, 0.95f, 1.0f);
            states[i] = new Vector4(6.0f, 0.0f, 0.0f, i + 1);
            volumes[i] = new Vector4(restVolume, 1.0f, Density, 1.0f);
        }
    }

    private static void SaveBakedAtlas(
        ComputeShader baker,
        PaintFilmGrid grid,
        SurfaceVisualProperties visual,
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
            baker.SetFloat("_MaxThickness", visual.MaxThickness);
            baker.SetFloat("_WetnessShine", visual.WetnessShine);
            baker.SetVector("_CanvasBaseColor", visual.CanvasBaseColor);
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
        double weightedX = 0.0;
        double weightedY = 0.0;

        for (int index = 0; index < cells.Length; index++)
        {
            int thickness = Math.Max(cells[index].ThicknessInt, 0);
            if (thickness <= 0)
                continue;

            int x = index % GridSize;
            int y = index / GridSize;
            thicknessUnits += thickness;
            weightedX += x * (double)thickness;
            weightedY += y * (double)thickness;
            activeCells++;
        }

        return new FilmMetrics
        {
            ThicknessUnits = thicknessUnits,
            ActiveCellCount = activeCells,
            Centroid =
                thicknessUnits > 0
                    ? new Vector2(
                        (float)(weightedX / thicknessUnits),
                        (float)(weightedY / thicknessUnits)
                    )
                    : Vector2.zero
        };
    }

    private static ParticleMetrics ReadParticleMetrics(
        GraphicsBuffer states,
        GraphicsBuffer velocities,
        int particleCount,
        double initialMass)
    {
        var stateData = new Vector4[particleCount];
        var velocityData = new Vector4[particleCount];
        states.GetData(stateData);
        velocities.GetData(velocityData);

        int deposited = 0;
        int airborne = 0;
        double retainedMass = 0.0;

        for (int i = 0; i < particleCount; i++)
        {
            int state = Mathf.RoundToInt(stateData[i].x);
            if (state == 8 || state == 9)
                deposited++;
            if (state == 6)
                airborne++;

            retainedMass += Math.Max(velocityData[i].w, 0.0f);
        }

        return new ParticleMetrics
        {
            DepositedCount = deposited,
            AirborneCount = airborne,
            RetainedMassFraction = (float)(retainedMass / Math.Max(initialMass, 1e-12))
        };
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
        Require(asset != null, $"Missing surface motion/material validation asset: {path}");
        return asset;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private struct MovingSurfaceResult
    {
        public float ImpactedFraction;
        public float DepositedFraction;
        public float VolumeError;
        public float DownhillShiftCells;
        public int ActiveCells;
    }

    private struct SplashResult
    {
        public SurfaceType SurfaceType;
        public float DepositedVolumeFraction;
        public float RetainedMassFraction;
        public float AirborneFraction;
        public int ActiveCells;
    }

    private struct FilmMetrics
    {
        public long ThicknessUnits;
        public int ActiveCellCount;
        public Vector2 Centroid;
    }

    private struct ParticleMetrics
    {
        public int DepositedCount;
        public int AirborneCount;
        public float RetainedMassFraction;
    }
}
