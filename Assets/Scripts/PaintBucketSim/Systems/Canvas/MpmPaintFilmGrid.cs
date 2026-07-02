using System;
using UnityEngine;

namespace PaintBucketSim.Systems.Canvas
{
    public sealed class MpmPaintFilmGrid : IDisposable
    {
        public const int ThicknessUnitsPerMeter = 1000000;
        public const int WetnessUnits = 65535;
        public const int ColorWeightScale = 256;
        public const int CellStrideBytes = 48;
        public const int HybridParticleStrideBytes = 64;

        private GraphicsBuffer _cellBuffer;
        private GraphicsBuffer _scratchBuffer;
        private MpmPaintFilmCellGpu[] _zeroCells;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CellCount => Width * Height;

        public GraphicsBuffer CellBuffer => _cellBuffer;
        public GraphicsBuffer ScratchBuffer => _scratchBuffer;

        public bool IsValid =>
            Width > 0 &&
            Height > 0 &&
            _cellBuffer != null &&
            _scratchBuffer != null;

        public void Ensure(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            if (IsValid && Width == width && Height == height)
                return;

            Release();

            Width = width;
            Height = height;

            int count = CellCount;
            _cellBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                CellStrideBytes
            );
            _scratchBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                CellStrideBytes
            );

            _zeroCells = new MpmPaintFilmCellGpu[count];
            Clear();
        }

        public void Clear()
        {
            if (_cellBuffer == null || _scratchBuffer == null)
                return;

            int count = CellCount;
            if (_zeroCells == null || _zeroCells.Length != count)
                _zeroCells = new MpmPaintFilmCellGpu[count];

            _cellBuffer.SetData(_zeroCells);
            _scratchBuffer.SetData(_zeroCells);
        }

        public void SwapBuffers()
        {
            GraphicsBuffer temp = _cellBuffer;
            _cellBuffer = _scratchBuffer;
            _scratchBuffer = temp;
        }

        public void Dispose()
        {
            Release();
        }

        public void Release()
        {
            if (_cellBuffer != null)
            {
                _cellBuffer.Release();
                _cellBuffer = null;
            }

            if (_scratchBuffer != null)
            {
                _scratchBuffer.Release();
                _scratchBuffer = null;
            }

            _zeroCells = null;
            Width = 0;
            Height = 0;
        }
    }

    public struct MpmPaintFilmCellGpu
    {
        public uint thickness;
        public uint wetness;
        public uint colorR;
        public uint colorG;
        public uint colorB;
        public uint deposited;
        public uint absorbed;
        public uint impactCount;
        public int flowU;
        public int flowV;
        public uint flags;
        // Stores pigment averaging weight, independent from physical thickness.
        public uint reserved;
    }

    public struct MpmCanvasHybridParticleGpu
    {
        public Vector4 data0;
        public Vector4 data1;
        public Vector4 data2;
        public Vector4 data3;
    }
}
