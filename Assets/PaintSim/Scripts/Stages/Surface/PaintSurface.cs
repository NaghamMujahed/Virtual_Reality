using UnityEngine;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Core.Buffers;
using PaintSim.Scripts.Rendering;

namespace PaintSim.Scripts.Stages.Surface
{
    [RequireComponent(typeof(MeshRenderer))]
    public class PaintSurface : MonoBehaviour
    {
        [Header("Surface Type")]
        [SerializeField] private SurfaceType _surfaceType = SurfaceType.Wood;

        [Header("Grid Settings")]
        [SerializeField] private int   _gridWidth  = 256;
        [SerializeField] private int   _gridHeight = 256;
        [SerializeField] private float _cellSize   = 0.01f;

        [Header("Rendering")]
        [SerializeField] private ComputeShader _paintFilmBakerShader;
        [SerializeField] private float _maxThickness = 0.0001f;
        [SerializeField] private float _wetnessShine = 0.8f;

        [Header("Evolution (Drying)")]
        [SerializeField] private ComputeShader _evaporationShader; 
        [SerializeField] private float _evaporationRate = 0.05f;  
        [SerializeField] private float _diffusionRate = 30.0f;

        public PaintFilmGrid       PaintFilmGrid      { get; private set; }
        public SurfaceProperties   SurfaceProperties  { get; private set; }
        public SurfaceType         SurfaceType        => _surfaceType;

        private PaintSurfaceRenderer _renderer;
        private PaintEvolver         _evolver; 
        private MeshRenderer         _meshRenderer;

        private void Awake()
        {
            _meshRenderer     = GetComponent<MeshRenderer>();
            SurfaceProperties = SurfaceProperties.FromType(_surfaceType);

            InitGrid();
            InitEvolver(); 
            InitRenderer();
        }

        private void InitGrid()
        {
            float halfW = _gridWidth  * _cellSize * 0.5f;
            float halfH = _gridHeight * _cellSize * 0.5f;

            var gridOrigin = new Vector2(
                transform.position.x - halfW,
                transform.position.z - halfH
            );

            PaintFilmGrid = new PaintFilmGrid(
                gridWidth:  _gridWidth,
                gridHeight: _gridHeight,
                cellSize:   _cellSize,
                surfaceY:   transform.position.y,
                gridOrigin: gridOrigin
            );
            
            float worldW = _gridWidth  * _cellSize;
            float worldH = _gridHeight * _cellSize;
            transform.localScale = new Vector3(worldW / 10f, 1f, worldH / 10f);
        }

        private void InitEvolver()
        {
            if (_evaporationShader == null)
            {
                Debug.LogWarning("[PaintSurface] Evaporation Shader is missing!");
                return;
            }

            _evolver = new PaintEvolver(_evaporationShader, PaintFilmGrid);
            _evolver.EvaporationRate = _evaporationRate;
        }

        private void InitRenderer()
        {
            if (_paintFilmBakerShader == null)
            {
                Debug.LogError("[PaintSurface] PaintFilmBakerShader is null");
                return;
            }

            _renderer = new PaintSurfaceRenderer(
                bakerShader:     _paintFilmBakerShader,
                paintFilmGrid:   PaintFilmGrid,
                surfaceRenderer: _meshRenderer
            );

            _renderer.MaxThickness = _maxThickness;
            _renderer.WetnessShine = _wetnessShine;
        }

        public void Render()
        {
            if (_evolver != null)
            {
                _evolver.EvaporationRate = _evaporationRate; 
                _evolver.DiffusionRate = _diffusionRate;
                _evolver.Evolve(Time.deltaTime);
            }

            _renderer?.Render();
        }

        private void OnValidate()
        {
            SurfaceProperties = SurfaceProperties.FromType(_surfaceType);
        }

        public void Dispose()
        {
            PaintFilmGrid?.Dispose();
            _renderer?.Dispose();
        }

        private void OnDestroy() => Dispose();
    }
}