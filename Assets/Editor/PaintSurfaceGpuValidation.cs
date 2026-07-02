using System;
using UnityEditor;
using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Rendering;
using PaintSim.Scripts.Stages.Surface;

public static class PaintSurfaceGpuValidation
{
    public static void Run()
    {
        const int width = 64;
        const int height = 64;
        const float cellSize = 0.01f;
        const float density = 1150.0f;
        const float restVolume = 5.0e-8f;
        const int particleCount = 2;
        const float thicknessScale = PaintCellData.ThicknessScale;

        PaintFilmGrid grid = null;
        PaintDepositor depositor = null;
        GraphicsBuffer positions = null;
        GraphicsBuffer velocities = null;
        GraphicsBuffer colors = null;
        GraphicsBuffer states = null;
        GraphicsBuffer volumes = null;
        RenderTexture albedo = null;
        RenderTexture surfaceData = null;
        Texture2D readback = null;

        try
        {
            ComputeShader impact = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Resources/ComputeShaders/Impact/SurfaceImpact.compute"
            );
            ComputeShader evolution = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintEvaporation.compute"
            );
            ComputeShader baker = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Resources/ComputeShaders/Surface/PaintFilmBaker.compute"
            );
            Shader wetShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/PaintSim/Shaders/WetPaintSurfaceURP.shader"
            );

            Require(impact != null, "Impact compute shader is missing.");
            Require(evolution != null, "Evolution compute shader is missing.");
            Require(baker != null, "Baker compute shader is missing.");
            Require(wetShader != null && wetShader.isSupported, "Wet paint shader is unsupported.");

            grid = new PaintFilmGrid(
                width,
                height,
                cellSize,
                cellSize,
                0.0f,
                Vector2.zero,
                Vector3.zero,
                Vector3.right,
                Vector3.forward,
                Vector3.up,
                0.03f,
                true
            );

            positions = NewFloat4Buffer(
                new Vector4(0.323f, 0.001f, 0.325f, 0.0015f),
                new Vector4(0.327f, 0.001f, 0.325f, 0.0015f)
            );
            velocities = NewFloat4Buffer(
                new Vector4(0.0f, -1.0f, 0.0f, restVolume * density),
                new Vector4(0.0f, -1.0f, 0.0f, restVolume * density)
            );
            colors = NewFloat4Buffer(
                new Vector4(1.0f, 0.05f, 0.05f, 1.0f),
                new Vector4(0.05f, 0.05f, 1.0f, 1.0f)
            );
            states = NewFloat4Buffer(
                new Vector4(6.0f, 0.0f, 0.0f, 1.0f),
                new Vector4(6.0f, 0.0f, 0.0f, 2.0f)
            );
            volumes = NewFloat4Buffer(
                new Vector4(restVolume, 1.0f, density, 1.0f),
                new Vector4(restVolume, 1.0f, density, 1.0f)
            );

            PaintProperties paint = PaintProperties.Create(
                density,
                0.85f,
                0.035f,
                new Color(0.1f, 0.35f, 1.0f, 1.0f)
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
                particleCount,
                SurfaceProperties.Wood,
                true,
                true,
                true,
                true
            );

            SurfaceImpactDiagnostics diagnostics = depositor.ReadDiagnostics();
            Require(
                diagnostics.Scanned == particleCount &&
                diagnostics.Impacted == particleCount &&
                diagnostics.Settled == particleCount &&
                diagnostics.CellWrites > 0,
                $"Unexpected diagnostics: {diagnostics}."
            );

            PaintCellData[] cells = ReadCells(grid);
            long depositedThicknessUnits = SumThickness(cells);
            double depositedVolume =
                depositedThicknessUnits / thicknessScale *
                cellSize * cellSize;
            double relativeVolumeError =
                Math.Abs(depositedVolume - restVolume * particleCount) /
                (restVolume * particleCount);

            Require(depositedThicknessUnits > 0, "No paint was deposited.");
            Require(
                relativeVolumeError < 0.08,
                $"Deposited volume error is {relativeVolumeError:P2}."
            );

            PaintCellData strongestCell = FindStrongestCell(cells);
            Require(
                strongestCell.Color.x > 0.25f &&
                strongestCell.Color.z > 0.25f &&
                strongestCell.Color.y < 0.2f,
                $"Atomic color blend failed: {strongestCell.Color}."
            );

            Vector4[] stateOutput = new Vector4[particleCount];
            Vector4[] massOutput = new Vector4[particleCount];
            states.GetData(stateOutput);
            velocities.GetData(massOutput);
            for (int i = 0; i < particleCount; i++)
            {
                Require(
                    Mathf.RoundToInt(stateOutput[i].x) == 8,
                    $"Expected Deposited state (8), got {stateOutput[i].x}."
                );
                Require(
                    massOutput[i].w <= 1e-8f,
                    $"Settled particle retained mass {massOutput[i].w}."
                );
            }

            PaintEvolver evolver = new PaintEvolver(evolution, grid)
            {
                EvaporationRate = 0.0f,
                DiffusionRate = 7.5f,
                RunoffRate = 0.18f,
                MinimumWetThickness = 0.000015f,
                SurfaceGravity = Vector2.zero,
                PaintDensity = density,
                DynamicViscosity = 0.85f,
                SurfaceTension = 0.035f,
                YieldStress = 0.0f
            };

            long thicknessBeforeEvolution = depositedThicknessUnits;
            for (int i = 0; i < 12; i++)
                evolver.Evolve(1.0f / 60.0f);

            cells = ReadCells(grid);
            long thicknessAfterEvolution = SumThickness(cells);
            double evolutionDrift =
                Math.Abs(thicknessAfterEvolution - thicknessBeforeEvolution) /
                (double)Math.Max(thicknessBeforeEvolution, 1L);

            Require(
                evolutionDrift < 0.05,
                $"Film evolution volume drift is {evolutionDrift:P2}."
            );

            albedo = NewRenderTexture(width, height);
            surfaceData = NewRenderTexture(width, height);
            int bakerKernel = baker.FindKernel("CSMain");
            baker.SetBuffer(bakerKernel, "_PaintCellBuffer", grid.PaintCellBuffer);
            baker.SetTexture(bakerKernel, "_OutputTexture", albedo);
            baker.SetTexture(bakerKernel, "_SurfaceDataTexture", surfaceData);
            baker.SetInt("_GridWidth", width);
            baker.SetInt("_GridHeight", height);
            baker.SetInt("_ThicknessScale", PaintCellData.ThicknessScale);
            baker.SetFloat("_MaxThickness", 0.00008f);
            baker.SetFloat("_WetnessShine", 0.8f);
            baker.SetVector("_CanvasBaseColor", Color.white);
            baker.Dispatch(bakerKernel, width / 8, height / 8, 1);

            readback = ReadRenderTexture(surfaceData);
            Color[] pixels = readback.GetPixels();
            float maxCoverage = 0.0f;
            float maxWetness = 0.0f;

            foreach (Color pixel in pixels)
            {
                maxCoverage = Mathf.Max(maxCoverage, pixel.r);
                maxWetness = Mathf.Max(maxWetness, pixel.g);
            }

            Require(maxCoverage > 0.1f, $"Coverage map is too weak: {maxCoverage}.");
            Require(maxWetness > 0.5f, $"Wetness map is too weak: {maxWetness}.");

            Debug.Log(
                "[PaintSurfaceGpuValidation] PASS " +
                $"depositedVolume={depositedVolume:E4} m3, " +
                $"depositError={relativeVolumeError:P2}, " +
                $"evolutionDrift={evolutionDrift:P2}, " +
                $"maxCoverage={maxCoverage:F3}, maxWetness={maxWetness:F3}, " +
                $"mixedColor={strongestCell.Color}"
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

            if (readback != null)
                UnityEngine.Object.DestroyImmediate(readback);

            Release(ref albedo);
            Release(ref surfaceData);
            depositor?.Dispose();
            grid?.Dispose();
            positions?.Release();
            velocities?.Release();
            colors?.Release();
            states?.Release();
            volumes?.Release();
        }
    }

    private static GraphicsBuffer NewFloat4Buffer(params Vector4[] values)
    {
        var buffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            values.Length,
            sizeof(float) * 4
        );
        buffer.SetData(values);
        return buffer;
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

    private static Texture2D ReadRenderTexture(RenderTexture texture)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        var result = new Texture2D(
            texture.width,
            texture.height,
            TextureFormat.RGBAFloat,
            false,
            true
        );
        result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        result.Apply(false, false);
        RenderTexture.active = previous;
        return result;
    }

    private static PaintCellData[] ReadCells(PaintFilmGrid grid)
    {
        var cells = new PaintCellData[grid.GridWidth * grid.GridHeight];
        grid.PaintCellBuffer.GetData(cells);
        return cells;
    }

    private static long SumThickness(PaintCellData[] cells)
    {
        long sum = 0;
        foreach (PaintCellData cell in cells)
            sum += Math.Max(cell.ThicknessInt, 0);
        return sum;
    }

    private static PaintCellData FindStrongestCell(PaintCellData[] cells)
    {
        PaintCellData strongest = default;
        foreach (PaintCellData cell in cells)
        {
            if (cell.ThicknessInt > strongest.ThicknessInt)
                strongest = cell;
        }

        return strongest;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Release(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        UnityEngine.Object.DestroyImmediate(texture);
        texture = null;
    }
}
