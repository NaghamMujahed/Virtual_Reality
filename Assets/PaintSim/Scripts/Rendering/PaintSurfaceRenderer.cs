using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;

namespace PaintSim.Scripts.Rendering
{
    public class PaintSurfaceRenderer
    {
        private readonly ComputeShader _bakerShader;
        private readonly PaintFilmGrid _paintFilmGrid;
        private readonly MeshRenderer _surfaceRenderer;

        private RenderTexture _paintTexture;

        private readonly int _kernelIndex;

        private const int GroupSize = 8;

        private static readonly int ID_PaintCellBuffer = Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_OutputTexture   = Shader.PropertyToID("_OutputTexture");
        private static readonly int ID_GridWidth       = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight      = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_ThicknessScale  = Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_MaxThickness    = Shader.PropertyToID("_MaxThickness");
        private static readonly int ID_WetnessShine    = Shader.PropertyToID("_WetnessShine");
        private static readonly int ID_MainTex         = Shader.PropertyToID("_MainTex");

        public float MaxThickness = 0.0001f;
        public float WetnessShine = 0.8f;

        public PaintSurfaceRenderer(
            ComputeShader bakerShader,
            PaintFilmGrid paintFilmGrid,
            MeshRenderer  surfaceRenderer)
        {
            _bakerShader = bakerShader;
            _paintFilmGrid = paintFilmGrid;
            _surfaceRenderer = surfaceRenderer;

            _kernelIndex = _bakerShader.FindKernel("CSMain");

            if (_kernelIndex < 0)
            {
                Debug.LogError("[PaintSurfaceRenderer] CSMain kernel not found");
                return;
            }

            InitTexture();
        }

        private void InitTexture()
        {
            _paintTexture = new RenderTexture(
                _paintFilmGrid.GridWidth,
                _paintFilmGrid.GridHeight,
                0,
                RenderTextureFormat.ARGB32
            )
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            _paintTexture.Create();

            if (_surfaceRenderer == null)
            {
                Debug.LogError("[PaintSurfaceRenderer] _surfaceRenderer is NULL");
                return;
            }

            var mat = _surfaceRenderer.material;
            if (mat == null)
            {
                Debug.LogError("[PaintSurfaceRenderer] Material is NULL on Plane");
                return;
            }

            mat.SetTexture(ID_MainTex, _paintTexture);
            mat.color = Color.white;
        }

        public void Render()
        {
            if (_paintTexture == null) return;

            _bakerShader.SetBuffer(
                _kernelIndex,
                ID_PaintCellBuffer,
                _paintFilmGrid.PaintCellBuffer
            );

            _bakerShader.SetTexture(
                _kernelIndex,
                ID_OutputTexture,
                _paintTexture
            );

            _bakerShader.SetInt(ID_GridWidth, _paintFilmGrid.GridWidth);
            _bakerShader.SetInt(ID_GridHeight, _paintFilmGrid.GridHeight);
            _bakerShader.SetInt(ID_ThicknessScale, PaintCellData.ThicknessScale);
            _bakerShader.SetFloat(ID_MaxThickness, MaxThickness);
            _bakerShader.SetFloat(ID_WetnessShine, WetnessShine);

            int groupsX = Mathf.CeilToInt(_paintFilmGrid.GridWidth / (float)GroupSize);
            int groupsY = Mathf.CeilToInt(_paintFilmGrid.GridHeight / (float)GroupSize);

            _bakerShader.Dispatch(_kernelIndex, groupsX, groupsY, 1);
        }

        public void Dispose()
        {
            if (_paintTexture != null)
            {
                _paintTexture.Release();
                if (Application.isPlaying)
                    Object.Destroy(_paintTexture);
                else
                    Object.DestroyImmediate(_paintTexture);
            }
        }
    }
}
