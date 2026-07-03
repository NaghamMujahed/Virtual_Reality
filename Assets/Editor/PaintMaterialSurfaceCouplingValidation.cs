using System;
using System.IO;
using PaintBucketSim.Configs;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;
using UnityEditor;
using UnityEngine;

public static class PaintMaterialSurfaceCouplingValidation
{
    private const int GridSize = 256;
    private const float CellSize = 0.01f;
    private const float Dt = 1.0f / 60.0f;

    public static void Run()
    {
        PaintMaterialConfig water = null;
        PaintMaterialConfig latex = null;

        try
        {
            string outputDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs",
                "G30D_MaterialSurfaceCoupling"
            );
            Directory.CreateDirectory(outputDirectory);

            ComputeShader evolution = Load<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintEvaporation.compute"
            );

            water = ScriptableObject.CreateInstance<PaintMaterialConfig>();
            water.ApplyMaterialPreset(PaintMaterialPreset.WaterLike);
            latex = ScriptableObject.CreateInstance<PaintMaterialConfig>();
            latex.ApplyMaterialPreset(PaintMaterialPreset.LatexPaint);

            PaintSurfaceFilmProfile waterProfile =
                water.EvaluateSurfaceFilmProfile(1.0f, 20.0f);
            PaintSurfaceFilmProfile latexProfile =
                latex.EvaluateSurfaceFilmProfile(1.0f, 20.0f);

            Require(
                waterProfile.dynamicViscosityPaS <
                latexProfile.dynamicViscosityPaS * 0.02f,
                $"Water film viscosity is not clearly lower than latex. " +
                $"water={waterProfile.dynamicViscosityPaS:F5}, " +
                $"latex={latexProfile.dynamicViscosityPaS:F5}."
            );
            Require(
                waterProfile.runoffRate > latexProfile.runoffRate * 3.0f &&
                waterProfile.contactAngleResistance < 0.15f &&
                waterProfile.surfaceInertiaResponse > latexProfile.surfaceInertiaResponse,
                "Water surface-film profile is not mobile enough relative to latex."
            );

            FlowResult waterInertia = RunFlowCase(
                evolution,
                waterProfile,
                new Vector2(24.0f, 0.0f),
                240,
                new Color(0.45f, 0.82f, 1.0f, 1.0f),
                Path.Combine(outputDirectory, "G30D_Water_InertialFlow.png"),
                seedNearRightEdge: false
            );
            FlowResult latexInertia = RunFlowCase(
                evolution,
                latexProfile,
                new Vector2(24.0f, 0.0f),
                240,
                new Color(0.08f, 0.28f, 1.0f, 1.0f),
                Path.Combine(outputDirectory, "G30D_Latex_InertialFlow.png"),
                seedNearRightEdge: false
            );
            FlowResult waterEdge = RunFlowCase(
                evolution,
                waterProfile,
                new Vector2(24.0f, 0.0f),
                180,
                new Color(0.45f, 0.82f, 1.0f, 1.0f),
                Path.Combine(outputDirectory, "G30D_Water_OpenEdgeRunoff.png"),
                seedNearRightEdge: true
            );

            Require(
                waterInertia.CentroidShiftXCells > 18.0f,
                $"Water did not respond strongly to board acceleration. " +
                $"shift={waterInertia.CentroidShiftXCells:F2} cells."
            );
            Require(
                waterInertia.CentroidShiftXCells >
                latexInertia.CentroidShiftXCells * 2.8f,
                $"Water should move much more than latex. " +
                $"water={waterInertia.CentroidShiftXCells:F2}, " +
                $"latex={latexInertia.CentroidShiftXCells:F2}."
            );
            Require(
                waterEdge.MassLossFraction > 0.08f,
                $"Open edge did not drain enough fluid. " +
                $"loss={waterEdge.MassLossFraction:P2}, " +
                $"signedChange={waterEdge.SignedMassChangeFraction:P2}, " +
                $"initialEdge={waterEdge.InitialRightEdgeMassFraction:P2}, " +
                $"finalEdge={waterEdge.FinalRightEdgeMassFraction:P2}, " +
                $"shift={waterEdge.CentroidShiftXCells:F2}."
            );

            Debug.Log(
                "[PaintMaterialSurfaceCouplingValidation] PASS " +
                $"waterShift={waterInertia.CentroidShiftXCells:F2} cells, " +
                $"latexShift={latexInertia.CentroidShiftXCells:F2} cells, " +
                $"edgeLoss={waterEdge.MassLossFraction:P2}, " +
                $"waterRunoff={waterProfile.runoffRate:F2}, " +
                $"latexRunoff={latexProfile.runoffRate:F2}, " +
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
            if (water != null)
                UnityEngine.Object.DestroyImmediate(water);
            if (latex != null)
                UnityEngine.Object.DestroyImmediate(latex);
        }
    }

    private static FlowResult RunFlowCase(
        ComputeShader evolution,
        PaintSurfaceFilmProfile profile,
        Vector2 surfaceAcceleration,
        int steps,
        Color color,
        string outputPath,
        bool seedNearRightEdge)
    {
        PaintFilmGrid grid = null;

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

            PaintCellData[] initial = BuildSeedFilm(
                profile,
                color,
                seedNearRightEdge
            );
            grid.PaintCellBuffer.SetData(initial);
            FilmMetrics before = Measure(initial);

            var evolver = new PaintEvolver(evolution, grid);
            ApplyProfile(evolver, profile);
            evolver.SurfaceGravity =
                surfaceAcceleration * profile.surfaceInertiaResponse;
            evolver.SurfaceRoughness = 0.18f;
            evolver.SurfaceAbsorptionRate = 0.0f;

            for (int step = 0; step < steps; step++)
                evolver.Evolve(Dt);

            PaintCellData[] final = ReadCells(grid);
            FilmMetrics after = Measure(final);
            SaveFilmImage(final, outputPath);

            return new FlowResult
            {
                CentroidShiftXCells = after.Centroid.x - before.Centroid.x,
                MassLossFraction =
                    before.ThicknessUnits > 0L
                        ? (float)Math.Max(
                            0.0,
                            (before.ThicknessUnits - after.ThicknessUnits) /
                            (double)before.ThicknessUnits
                        )
                        : 0.0f
                        ,
                SignedMassChangeFraction =
                    before.ThicknessUnits > 0L
                        ? (float)(
                            (after.ThicknessUnits - before.ThicknessUnits) /
                            (double)before.ThicknessUnits
                        )
                        : 0.0f,
                InitialRightEdgeMassFraction =
                    before.RightEdgeThicknessUnits /
                    (float)Math.Max(before.ThicknessUnits, 1L),
                FinalRightEdgeMassFraction =
                    after.RightEdgeThicknessUnits /
                    (float)Math.Max(after.ThicknessUnits, 1L)
            };
        }
        finally
        {
            grid?.Dispose();
        }
    }

    private static void ApplyProfile(
        PaintEvolver evolver,
        PaintSurfaceFilmProfile profile)
    {
        profile = profile.Sanitized();
        evolver.EvaporationRate = profile.evaporationRate;
        evolver.DiffusionRate = Mathf.Clamp(profile.diffusionRate * 0.25f, 0.0f, 12.0f);
        evolver.RunoffRate = profile.runoffRate;
        evolver.MinimumWetThickness = profile.minimumWetThickness;
        evolver.PaintDensity = profile.densityKgPerM3;
        evolver.DynamicViscosity = profile.dynamicViscosityPaS;
        evolver.SurfaceTension = profile.surfaceTensionNPerM;
        evolver.YieldStress = profile.yieldStressPa;
        evolver.ContactLineThickness = profile.contactLineThickness;
        evolver.ContactAngleResistance = profile.contactAngleResistance;
        evolver.SubstrateFlowVariation = profile.substrateFlowVariation;
        evolver.DripFingerInstability = profile.dripFingerInstability;
        evolver.ThinFilmCohesion = profile.thinFilmCohesion;
    }

    private static PaintCellData[] BuildSeedFilm(
        PaintSurfaceFilmProfile profile,
        Color color,
        bool seedNearRightEdge)
    {
        var cells = new PaintCellData[GridSize * GridSize];
        Vector2 center = seedNearRightEdge
            ? new Vector2(GridSize - 11.0f, GridSize * 0.50f)
            : new Vector2(GridSize * 0.38f, GridSize * 0.50f);
        Vector2 radii = seedNearRightEdge
            ? new Vector2(18.0f, 24.0f)
            : new Vector2(26.0f, 20.0f);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                Vector2 p = new Vector2(
                    (x - center.x) / radii.x,
                    (y - center.y) / radii.y
                );
                float r2 = p.sqrMagnitude;
                if (r2 > 1.0f)
                    continue;

                float falloff = Mathf.Exp(-r2 * 2.35f);
                float irregular =
                    0.82f +
                    0.18f * Mathf.Sin(x * 0.37f + y * 0.19f) *
                    Mathf.Cos(x * 0.13f - y * 0.31f);
                float thickness = Mathf.Lerp(
                    profile.minimumWetThickness * 2.4f,
                    profile.minimumWetThickness * 7.5f,
                    falloff * irregular
                );

                int index = y * GridSize + x;
                cells[index] = new PaintCellData
                {
                    ThicknessInt = PaintCellData.ToThicknessInt(thickness),
                    Wetness = 1.0f,
                    Age = 0.0f,
                    IsActive = 1u,
                    Color = color,
                    FlowVelocity = Vector2.zero,
                    PaintDensity = profile.densityKgPerM3
                };
            }
        }

        return cells;
    }

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[GridSize * GridSize];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
    }

    private static FilmMetrics Measure(PaintCellData[] cells)
    {
        long total = 0L;
        long rightEdgeTotal = 0L;
        double sx = 0.0;
        double sy = 0.0;

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int thickness = Mathf.Max(cells[y * GridSize + x].ThicknessInt, 0);
                if (thickness <= 0)
                    continue;

                total += thickness;
                if (x >= GridSize - 2)
                    rightEdgeTotal += thickness;
                sx += x * (double)thickness;
                sy += y * (double)thickness;
            }
        }

        return new FilmMetrics
        {
            ThicknessUnits = total,
            RightEdgeThicknessUnits = rightEdgeTotal,
            Centroid =
                total > 0
                    ? new Vector2((float)(sx / total), (float)(sy / total))
                    : Vector2.zero
        };
    }

    private static void SaveFilmImage(PaintCellData[] cells, string path)
    {
        Texture2D image = null;

        try
        {
            image = new Texture2D(GridSize, GridSize, TextureFormat.RGB24, false, false);
            Color32[] pixels = new Color32[GridSize * GridSize];

            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    PaintCellData cell = cells[y * GridSize + x];
                    float thickness = cell.Thickness;
                    float alpha = Mathf.Clamp01(thickness / 0.00006f);
                    Color paint = new Color(cell.Color.x, cell.Color.y, cell.Color.z, 1.0f);
                    Color canvas = new Color(0.09f, 0.075f, 0.055f, 1.0f);
                    Color final = Color.Lerp(canvas, paint, alpha);
                    pixels[(GridSize - 1 - y) * GridSize + x] = final;
                }
            }

            image.SetPixels32(pixels);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static T Load<T>(string path)
        where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new FileNotFoundException($"Missing asset: {path}");

        return asset;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private struct FlowResult
    {
        public float CentroidShiftXCells;
        public float MassLossFraction;
        public float SignedMassChangeFraction;
        public float InitialRightEdgeMassFraction;
        public float FinalRightEdgeMassFraction;
    }

    private struct FilmMetrics
    {
        public long ThicknessUnits;
        public long RightEdgeThicknessUnits;
        public Vector2 Centroid;
    }
}
