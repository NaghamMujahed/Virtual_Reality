using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;

public static class PaintSurfaceTiltedPlaneCalibration
{
    private const int GridSize = 512;
    private const float CellSize = 0.01f;
    private const float Density = 1150.0f;
    private const float ImpactDeltaTime = 1.0f / 60.0f;
    private const int ParticleCount = 32768;
    private const float RestVolumeMin = 1.8e-8f;
    private const float RestVolumeMax = 4.5e-8f;

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
                "G29B_TiltedPlaneCalibration"
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

            Quaternion surfaceRotation = Quaternion.Euler(28.0f, 0.0f, -9.0f);
            Vector3 axisU = (surfaceRotation * Vector3.right).normalized;
            Vector3 axisV = (surfaceRotation * Vector3.forward).normalized;
            Vector3 normal = (surfaceRotation * Vector3.up).normalized;
            float worldSize = GridSize * CellSize;
            Vector3 center = new Vector3(0.0f, 1.15f, 0.0f);
            Vector3 origin = center -
                axisU * (worldSize * 0.5f) -
                axisV * (worldSize * 0.5f);
            Vector2 projectedGravity = new Vector2(
                Vector3.Dot(Physics.gravity, axisU),
                Vector3.Dot(Physics.gravity, axisV)
            );

            Require(
                projectedGravity.magnitude > 1.0f,
                $"Tilted plane gravity projection is too small: {projectedGravity}."
            );

            grid = new PaintFilmGrid(
                GridSize,
                GridSize,
                CellSize,
                CellSize,
                origin.y,
                new Vector2(origin.x, origin.z),
                origin,
                axisU,
                axisV,
                normal,
                0.035f,
                true
            );

            BuildSweptImpactParticles(
                origin,
                axisU,
                axisV,
                normal,
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
                0.72f,
                0.035f,
                new Color(0.05f, 0.36f, 0.95f, 1.0f)
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

            depositor.DispatchFromMlsMpmBuffers(
                positions,
                velocities,
                colors,
                states,
                volumes,
                ParticleCount,
                SurfaceProperties.Glass,
                true,
                true,
                true,
                true,
                ImpactDeltaTime
            );

            SurfaceImpactDiagnostics diagnostics = depositor.ReadDiagnostics();
            PaintCellData[] impactCells = ReadCells(grid);
            FilmMetrics impactMetrics = MeasureFilm(impactCells);
            ParticleStateMetrics particleMetrics = ReadParticleStates(states, velocities);
            double depositedVolume = impactMetrics.ThicknessUnits /
                (double)PaintCellData.ThicknessScale *
                CellSize *
                CellSize;
            double depositError =
                Math.Abs(depositedVolume - expectedVolume) /
                Math.Max(expectedVolume, 1e-12);

            Require(
                diagnostics.Scanned == ParticleCount,
                $"Expected to scan {ParticleCount} particles, got {diagnostics.Scanned}."
            );
            Require(
                diagnostics.Impacted >= ParticleCount * 0.985f,
                $"Tilted swept impact missed too many particles: {diagnostics}."
            );
            Require(
                diagnostics.Settled >= diagnostics.Impacted * 0.98f,
                $"Too many particles survived the tilted receiver: {diagnostics}."
            );
            Require(
                particleMetrics.DepositedCount >= ParticleCount * 0.98f,
                $"Expected deposited particle states. deposited={particleMetrics.DepositedCount}."
            );
            Require(
                particleMetrics.RemainingMassFraction < 0.02,
                $"Too much mass remained below/through the plane: " +
                $"{particleMetrics.RemainingMassFraction:P2}."
            );
            Require(
                depositError < 0.035,
                $"Tilted impact deposited volume error is {depositError:P2}."
            );

            runtimeMaterial = new Material(sourceMaterial)
            {
                name = "G29B Tilted Plane Material"
            };
            ConfigureBoard(
                runtimeMaterial,
                center,
                axisV,
                normal,
                worldSize,
                worldSize,
                out board
            );
            ConfigureRenderEnvironment(
                center,
                axisU,
                axisV,
                normal,
                out cameraObject,
                out lightObject,
                out Camera camera,
                out cameraTarget
            );

            surfaceRenderer = new PaintSurfaceRenderer(
                baker,
                grid,
                board.GetComponent<MeshRenderer>()
            );
            surfaceRenderer.ConfigureSurfaceAppearance(SurfaceVisualProperties.Glass);
            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G29B_TiltedImpact_HighCount.png")
            );

            PaintEvolver evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.01f,
                DiffusionRate = 8.0f,
                RunoffRate = 0.42f,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = projectedGravity,
                PaintDensity = Density,
                DynamicViscosity = 0.72f,
                SurfaceTension = 0.035f,
                YieldStress = 0.0f,
                ContactLineThickness = 0.00002f,
                ContactAngleResistance = 0.32f,
                SubstrateFlowVariation = 0.18f,
                SurfaceRoughness = SurfaceProperties.Glass.Roughness,
                DripFingerInstability = 0.62f,
                ThinFilmCohesion = 0.42f,
                SurfaceAbsorptionRate = SurfaceProperties.Glass.AbsorptionRate
            };

            for (int step = 0; step < 360; step++)
                evolver.Evolve(1.0f / 60.0f);

            PaintCellData[] flowedCells = ReadCells(grid);
            FilmMetrics flowMetrics = MeasureFilm(flowedCells);
            Vector2 flowDelta = flowMetrics.Centroid - impactMetrics.Centroid;
            Vector2 expectedDirection = projectedGravity.normalized;
            float downhillShift = Vector2.Dot(flowDelta, expectedDirection);
            float crossShift = Math.Abs(
                flowDelta.x * expectedDirection.y -
                flowDelta.y * expectedDirection.x
            );
            double flowDrift = Math.Abs(
                flowMetrics.ThicknessUnits - impactMetrics.ThicknessUnits
            ) / (double)Math.Max(impactMetrics.ThicknessUnits, 1L);

            Require(
                downhillShift > 12.0f,
                $"Paint did not flow downhill on the tilted plane. " +
                $"shift={downhillShift:F2}, gravity={projectedGravity}."
            );
            Require(
                crossShift < Math.Max(18.0f, downhillShift * 0.75f),
                $"Tilted flow is not aligned with projected gravity. " +
                $"downhill={downhillShift:F2}, cross={crossShift:F2}."
            );
            Require(
                flowDrift < 0.035,
                $"Tilted flow volume drift is {flowDrift:P2}."
            );
            Require(
                flowMetrics.ActiveCellCount > impactMetrics.ActiveCellCount,
                "Tilted flow did not expand the paint footprint."
            );

            surfaceRenderer.Render();
            Capture(
                camera,
                cameraTarget,
                Path.Combine(outputDirectory, "G29B_TiltedFlow_6s.png")
            );

            Debug.Log(
                "[PaintSurfaceTiltedPlaneCalibration] PASS " +
                $"particles={ParticleCount}, diagnostics=({diagnostics}), " +
                $"depositedVolume={depositedVolume:E4} m3, " +
                $"depositError={depositError:P2}, " +
                $"remainingMass={particleMetrics.RemainingMassFraction:P2}, " +
                $"gravityUV=({projectedGravity.x:F3},{projectedGravity.y:F3}), " +
                $"downhillShift={downhillShift:F2} cells, " +
                $"crossShift={crossShift:F2} cells, " +
                $"flowDrift={flowDrift:P2}, " +
                $"cells={impactMetrics.ActiveCellCount}->{flowMetrics.ActiveCellCount}, " +
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

    private static void BuildSweptImpactParticles(
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
        positions = new Vector4[ParticleCount];
        velocities = new Vector4[ParticleCount];
        colors = new Vector4[ParticleCount];
        states = new Vector4[ParticleCount];
        volumes = new Vector4[ParticleCount];
        expectedVolume = 0.0;

        var random = new System.Random(90210);
        float centerU = GridSize * CellSize * 0.47f;
        float centerV = GridSize * CellSize * 0.58f;

        for (int i = 0; i < ParticleCount; i++)
        {
            float cluster = i / (float)ParticleCount;
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float radial =
                Mathf.Sqrt((float)random.NextDouble()) *
                Mathf.Lerp(0.20f, 0.82f, cluster);
            float lane =
                Mathf.Sin(i * 0.017f) * 0.13f +
                ((float)random.NextDouble() - 0.5f) * 0.055f;
            float u = centerU + Mathf.Cos(angle) * radial + lane;
            float v = centerV + Mathf.Sin(angle) * radial * 0.72f;
            u = Mathf.Clamp(u, 0.65f, GridSize * CellSize - 0.65f);
            v = Mathf.Clamp(v, 0.65f, GridSize * CellSize - 0.65f);

            Vector3 impactPoint = origin + axisU * u + axisV * v;
            float currentDepth =
                Mathf.Lerp(0.010f, 0.030f, (float)random.NextDouble());
            float normalSpeed =
                Mathf.Lerp(2.35f, 3.45f, (float)random.NextDouble());
            float tangentU =
                Mathf.Lerp(-0.34f, 0.34f, (float)random.NextDouble());
            float tangentV =
                Mathf.Lerp(0.02f, 0.52f, (float)random.NextDouble());
            Vector3 velocity =
                -normal * normalSpeed +
                axisU * tangentU +
                axisV * tangentV;
            Vector3 currentPosition = impactPoint - normal * currentDepth;

            float restVolume =
                Mathf.Lerp(
                    RestVolumeMin,
                    RestVolumeMax,
                    (float)random.NextDouble()
                );
            expectedVolume += restVolume;

            Color color =
                cluster < 0.52f
                    ? new Color(0.03f, 0.35f, 0.95f, 1.0f)
                    : new Color(0.02f, 0.72f, 0.46f, 1.0f);

            positions[i] = new Vector4(
                currentPosition.x,
                currentPosition.y,
                currentPosition.z,
                0.00145f
            );
            velocities[i] = new Vector4(
                velocity.x,
                velocity.y,
                velocity.z,
                restVolume * Density
            );
            colors[i] = color;
            states[i] = new Vector4(6.0f, 0.0f, 0.0f, i + 1);
            volumes[i] = new Vector4(restVolume, 1.0f, Density, 1.0f);
        }
    }

    private static void ConfigureBoard(
        Material material,
        Vector3 center,
        Vector3 axisV,
        Vector3 normal,
        float width,
        float height,
        out GameObject board)
    {
        board = GameObject.CreatePrimitive(PrimitiveType.Plane);
        board.name = "G29B Tilted Paint Receiver";
        board.transform.position = center;
        board.transform.rotation = Quaternion.LookRotation(axisV, normal);
        board.transform.localScale = new Vector3(width / 10.0f, 1.0f, height / 10.0f);
        board.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void ConfigureRenderEnvironment(
        Vector3 center,
        Vector3 axisU,
        Vector3 axisV,
        Vector3 normal,
        out GameObject cameraObject,
        out GameObject lightObject,
        out Camera camera,
        out RenderTexture cameraTarget)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.45f, 1.0f);
        RenderSettings.reflectionIntensity = 0.9f;

        cameraObject = new GameObject("G29B Tilted Plane Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = center +
            normal * 4.1f -
            axisV * 1.25f +
            axisU * 0.35f;
        camera.transform.LookAt(center + axisV * 0.22f);
        camera.fieldOfView = 31.0f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.045f, 0.052f, 0.068f, 1.0f);
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

        lightObject = new GameObject("G29B Tilted Plane Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1.0f, 0.92f, 0.82f, 1.0f);
        light.intensity = 1.7f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.LookRotation(
            (-normal * 0.75f + axisV * 0.35f - axisU * 0.2f).normalized,
            Vector3.up
        );
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

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[grid.GridWidth * grid.GridHeight];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
    }

    private static FilmMetrics MeasureFilm(PaintCellData[] cells)
    {
        long thicknessUnits = 0L;
        double weightedX = 0.0;
        double weightedY = 0.0;
        double wetness = 0.0;
        int activeCells = 0;

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
            wetness += Mathf.Clamp01(cells[index].Wetness) * (double)thickness;
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
                    : Vector2.zero,
            WeightedWetness =
                thicknessUnits > 0
                    ? (float)(wetness / thicknessUnits)
                    : 0.0f
        };
    }

    private static ParticleStateMetrics ReadParticleStates(
        GraphicsBuffer states,
        GraphicsBuffer velocities)
    {
        var stateData = new Vector4[ParticleCount];
        var velocityData = new Vector4[ParticleCount];
        states.GetData(stateData);
        velocities.GetData(velocityData);

        int deposited = 0;
        double initialMass = 0.0;
        double remainingMass = 0.0;

        for (int i = 0; i < ParticleCount; i++)
        {
            int state = Mathf.RoundToInt(stateData[i].x);
            if (state == 8 || state == 9)
                deposited++;

            float restVolume = Mathf.Lerp(
                RestVolumeMin,
                RestVolumeMax,
                0.5f
            );
            initialMass += restVolume * Density;
            remainingMass += Math.Max(velocityData[i].w, 0.0f);
        }

        return new ParticleStateMetrics
        {
            DepositedCount = deposited,
            RemainingMassFraction = remainingMass / Math.Max(initialMass, 1e-12)
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
        Require(asset != null, $"Missing tilted plane calibration asset: {path}");
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
        public Vector2 Centroid;
        public float WeightedWetness;
    }

    private struct ParticleStateMetrics
    {
        public int DepositedCount;
        public double RemainingMassFraction;
    }
}
