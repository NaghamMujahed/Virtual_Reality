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
        private RenderTexture _surfaceDataTexture;
        private Color _canvasBaseColor = new Color(0.72f, 0.65f, 0.55f, 1.0f);
        private Material _runtimeFallbackMaterial;

        private readonly int _kernelIndex;

        private const int GroupSize = 8;

        private static readonly int ID_PaintCellBuffer = Shader.PropertyToID("_PaintCellBuffer");
        private static readonly int ID_OutputTexture   = Shader.PropertyToID("_OutputTexture");
        private static readonly int ID_SurfaceDataTexture =
            Shader.PropertyToID("_SurfaceDataTexture");
        private static readonly int ID_GridWidth       = Shader.PropertyToID("_GridWidth");
        private static readonly int ID_GridHeight      = Shader.PropertyToID("_GridHeight");
        private static readonly int ID_ThicknessScale  = Shader.PropertyToID("_ThicknessScale");
        private static readonly int ID_MaxThickness    = Shader.PropertyToID("_MaxThickness");
        private static readonly int ID_WetnessShine    = Shader.PropertyToID("_WetnessShine");
        private static readonly int ID_MainTex         = Shader.PropertyToID("_MainTex");
        private static readonly int ID_BaseMap         = Shader.PropertyToID("_BaseMap");
        private static readonly int ID_Color           = Shader.PropertyToID("_Color");
        private static readonly int ID_BaseColor       = Shader.PropertyToID("_BaseColor");
        private static readonly int ID_CanvasBaseColor = Shader.PropertyToID("_CanvasBaseColor");
        private static readonly int ID_PaintSurfaceData =
            Shader.PropertyToID("_PaintSurfaceData");

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
                RenderTextureFormat.ARGBHalf
            )
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            _paintTexture.Create();

            _surfaceDataTexture = new RenderTexture(
                _paintFilmGrid.GridWidth,
                _paintFilmGrid.GridHeight,
                0,
                RenderTextureFormat.ARGBHalf
            )
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            _surfaceDataTexture.Create();

            if (_surfaceRenderer == null)
            {
                Debug.LogError("[PaintSurfaceRenderer] _surfaceRenderer is NULL");
                return;
            }

            Material mat = ResolveSurfaceMaterial();
            if (mat == null)
            {
                Debug.LogError("[PaintSurfaceRenderer] Could not create or resolve a surface material.");
                return;
            }

            if (mat.HasProperty(ID_BaseColor))
                _canvasBaseColor = mat.GetColor(ID_BaseColor);
            else if (mat.HasProperty(ID_Color))
                _canvasBaseColor = mat.GetColor(ID_Color);

            if (mat.HasProperty(ID_MainTex))
                mat.SetTexture(ID_MainTex, _paintTexture);

            if (mat.HasProperty(ID_BaseMap))
                mat.SetTexture(ID_BaseMap, _paintTexture);

            if (mat.HasProperty(ID_PaintSurfaceData))
                mat.SetTexture(ID_PaintSurfaceData, _surfaceDataTexture);

            if (mat.HasProperty(ID_BaseColor))
                mat.SetColor(ID_BaseColor, Color.white);

            if (mat.HasProperty(ID_Color))
                mat.SetColor(ID_Color, Color.white);
        }

        private Material ResolveSurfaceMaterial()
        {
            if (_surfaceRenderer == null)
                return null;

            if (_surfaceRenderer.sharedMaterial == null ||
                _surfaceRenderer.sharedMaterials == null ||
                _surfaceRenderer.sharedMaterials.Length == 0)
            {
                Shader shader =
                    Shader.Find("PaintSim/Wet Paint Surface URP") ??
                    Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");

                if (shader == null)
                    return null;

                _runtimeFallbackMaterial = new Material(shader)
                {
                    name = "Runtime Paint Surface Material"
                };

                if (_runtimeFallbackMaterial.HasProperty(ID_BaseColor))
                    _runtimeFallbackMaterial.SetColor(ID_BaseColor, _canvasBaseColor);

                if (_runtimeFallbackMaterial.HasProperty(ID_Color))
                    _runtimeFallbackMaterial.SetColor(ID_Color, _canvasBaseColor);

                _surfaceRenderer.sharedMaterial = _runtimeFallbackMaterial;
            }

            return _surfaceRenderer.material;
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
            _bakerShader.SetTexture(
                _kernelIndex,
                ID_SurfaceDataTexture,
                _surfaceDataTexture
            );

            _bakerShader.SetInt(ID_GridWidth, _paintFilmGrid.GridWidth);
            _bakerShader.SetInt(ID_GridHeight, _paintFilmGrid.GridHeight);
            _bakerShader.SetInt(ID_ThicknessScale, PaintCellData.ThicknessScale);
            _bakerShader.SetFloat(ID_MaxThickness, MaxThickness);
            _bakerShader.SetFloat(ID_WetnessShine, WetnessShine);
            _bakerShader.SetVector(ID_CanvasBaseColor, _canvasBaseColor);

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

            if (_surfaceDataTexture != null)
            {
                _surfaceDataTexture.Release();
                if (Application.isPlaying)
                    Object.Destroy(_surfaceDataTexture);
                else
                    Object.DestroyImmediate(_surfaceDataTexture);

                _surfaceDataTexture = null;
            }

            if (_runtimeFallbackMaterial != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_runtimeFallbackMaterial);
                else
                    Object.DestroyImmediate(_runtimeFallbackMaterial);

                _runtimeFallbackMaterial = null;
            }
        }
    }
}
