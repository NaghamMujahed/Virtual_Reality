using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Diagnostics;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Rope;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PaintBucketSim.Systems.UI
{
    
    public sealed class AutoPaintSimRuntimeUI : MonoBehaviour

    {


        private GameObject _cameraPanel;

private readonly List<Camera> _runtimeCameras = new List<Camera>();
private int _activeCameraIndex;

private FloatFieldControl _cameraPosX;
private FloatFieldControl _cameraPosY;
private FloatFieldControl _cameraPosZ;

private FloatFieldControl _cameraRotX;
private FloatFieldControl _cameraRotY;
private FloatFieldControl _cameraRotZ;

private SliderControl _cameraFov;

private float _pendingCameraPosX;
private float _pendingCameraPosY;
private float _pendingCameraPosZ;

private float _pendingCameraRotX;
private float _pendingCameraRotY;
private float _pendingCameraRotZ;

private float _pendingCameraFov = 60.0f;



        private const int SortingOrder = 5000;

        private const float PanelWidth = 780.0f;
        private const float PanelHeight = 900.0f;
        private const int TitleFontSize = 32;
        private const int HeaderFontSize = 22;
        private const int BodyFontSize = 18;
        private const int SmallFontSize = 15;
        private const int TabFontSize = 21;
        private const int ButtonFontSize = 18;
        private const int SliderLabelFontSize = 18;

        private static readonly Color RootPanelColor = new Color(0.035f, 0.050f, 0.073f, 0.94f);
        private static readonly Color CardColor = new Color(0.075f, 0.105f, 0.150f, 0.96f);
        private static readonly Color CardColorSoft = new Color(0.095f, 0.130f, 0.185f, 0.96f);
        private static readonly Color FieldColor = new Color(0.120f, 0.160f, 0.220f, 1.0f);
        private static readonly Color FieldColorLight = new Color(0.170f, 0.215f, 0.285f, 1.0f);
        private static readonly Color AccentColor = new Color(0.150f, 0.820f, 0.760f, 1.0f);
        private static readonly Color AccentColorPressed = new Color(0.085f, 0.560f, 0.540f, 1.0f);
        private static readonly Color SectionColor = new Color(1.000f, 0.760f, 0.300f, 1.0f);
        private static readonly Color DangerColor = new Color(0.950f, 0.360f, 0.340f, 1.0f);
        private static readonly Color TextColor = new Color(0.930f, 0.960f, 0.985f, 1.0f);
        private static readonly Color MutedTextColor = new Color(0.630f, 0.710f, 0.800f, 1.0f);
        private static readonly Color DarkTextColor = new Color(0.035f, 0.050f, 0.070f, 1.0f);
        private static readonly Color TransparentColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

        private SimulationManager _simulationManager;
        private RopeSystem _ropeSystem;
        private BucketSystem _bucketSystem;
        private PaintFluidSystem _paintFluidSystem;
        private PaintSurface _paintSurface;

        private Canvas _canvas;
        private GameObject _rootPanel;
        private GameObject _ropePanel;
        private GameObject _bucketPanel;
        private GameObject _paintPanel;
        private GameObject _surfacePanel;

        private Text _titleText;
        private Text _statusText;
        private Text _pauseButtonText;
        private ScrollRect _settingsScrollRect;
        private RectTransform _settingsContentRect;

        private readonly List<GameObject> _tabPanels = new List<GameObject>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly Dictionary<GameObject, Button> _panelToTabButton = new Dictionary<GameObject, Button>();
        private DefaultControls.Resources _uiResources;
        private Font _font;

        // Rope pending values
        private SliderControl _ropeLength;
        private SliderControl _ropeSegments;
        private SliderControl _ropeMass;
        private SliderControl _ropeVisualRadius;
        private SliderControl _ropeGravityScale;
        private SliderControl _ropeDamping;
        private SliderControl _ropeSolverIterations;
        private SliderControl _ropeBreakTension;
        private Toggle _ropeGrabToggle;
        private Toggle _ropeBreakToggle;

        private float _pendingRopeLength;
        private int _pendingRopeSegments;
        private float _pendingRopeMass;
        private float _pendingRopeVisualRadius;
        private float _pendingRopeGravityScale;
        private float _pendingRopeDamping;
        private int _pendingRopeSolverIterations;
        private float _pendingRopeBreakTension;
        private bool _pendingRopeGrab;
        private bool _pendingRopeBreak;

        // Bucket pending values
        private SliderControl _bucketHeight;
        private SliderControl _bucketTopRadius;
        private SliderControl _bucketBottomRadius;
        private SliderControl _bucketMass;
        private SliderControl _bucketWallThickness;
        private SliderControl _bucketHoleRadius;
        private SliderControl _bucketFlow;
        private SliderControl _bucketExitVelocity;
        private Toggle _bucketHoleActiveToggle;

        private float _pendingBucketHeight;
        private float _pendingBucketTopRadius;
        private float _pendingBucketBottomRadius;
        private float _pendingBucketMass;
        private float _pendingBucketWallThickness;
        private float _pendingBucketHoleRadius;
        private float _pendingBucketFlow;
        private float _pendingBucketExitVelocity;
        private bool _pendingBucketHoleActive;

        // Paint pending values
        private IntFieldControl _paintParticles;

        private SliderControl _paintFill;
        private SliderControl _paintParticleRadius;
        private SliderControl _paintVisualSize;
        private IntFieldControl _paintMaxRendered;
        private SliderControl _paintDensity;
        private SliderControl _paintViscosity;
        private SliderControl _paintSurfaceTension;
        private SliderControl _paintDrying;
        private SliderControl _paintAbsorption;
        private SliderControl _paintColorCompartments;
        private Toggle _paintRenderToggle;
        private Toggle _paintMultiColorToggle;

        private int _pendingPaintParticles;
        private float _pendingPaintFill;
        private float _pendingPaintParticleRadius;
        private float _pendingPaintVisualSize;
        private int _pendingPaintMaxRendered;
        private float _pendingPaintDensity;
        private float _pendingPaintViscosity;
        private float _pendingPaintSurfaceTension;
        private float _pendingPaintDrying;
        private float _pendingPaintAbsorption;
        private int _pendingPaintColorCompartments;
        private bool _pendingPaintRenderParticles;
        private bool _pendingPaintMultiColor;
        private PaintMaterialPreset _pendingPaintPreset;
        private Color _pendingPaintColor;

        // Surface pending values
        private FloatFieldControl _surfacePositionX;
        private FloatFieldControl _surfacePositionY;
        private FloatFieldControl _surfacePositionZ;
        private FloatFieldControl _surfaceRotationX;
        private FloatFieldControl _surfaceRotationY;
        private FloatFieldControl _surfaceRotationZ;
        private SliderControl _surfaceDiffusionRate;
        private SliderControl _surfaceDryingRate;

        private SurfaceType _pendingSurfaceType;
        private Vector3 _pendingSurfacePosition;
        private Vector3 _pendingSurfaceRotationEuler;
        private float _pendingSurfaceDiffusionRate;
        private float _pendingSurfaceDryingRate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAutomatically()
        {
            if (FindAnyObjectByType<AutoPaintSimRuntimeUI>() != null)
                return;

            GameObject go = new GameObject("AutoPaintSimRuntimeUI");
            go.AddComponent<AutoPaintSimRuntimeUI>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            _uiResources = new DefaultControls.Resources();

            FindProjectSystems();
            DisableOldDebugOverlay();
            HideManualSettingsPanelIfExists();
            EnsureEventSystem();
            BuildUI();
            LoadAllValuesFromProject();
            ShowPanel(_ropePanel);
        }

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ToggleUIVisibility();
            }

            UpdateStatusText();
            UpdatePauseText();
        }

        private void FindProjectSystems()
        {
            _simulationManager = FindAnyObjectByType<SimulationManager>();
            _ropeSystem = FindAnyObjectByType<RopeSystem>();
            _bucketSystem = FindAnyObjectByType<BucketSystem>();
            _paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();
            _paintSurface = FindAnyObjectByType<PaintSurface>();
        }

        private void DisableOldDebugOverlay()
        {
            DebugOverlay[] overlays = FindObjectsByType<DebugOverlay>(FindObjectsInactive.Exclude);
            foreach (DebugOverlay overlay in overlays)
            {
                if (overlay != null)
                    overlay.enabled = false;
            }
        }

        private void HideManualSettingsPanelIfExists()
        {
            GameObject oldPanel = GameObject.Find("SettingsPanel");
            if (oldPanel != null)
                oldPanel.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
            DontDestroyOnLoad(eventSystemObject);
        }

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject(
                "AutoPaintSimUICanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);

            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _rootPanel = CreatePanel("AutoSettingsPanel", canvasObject.transform, RootPanelColor);
            RectTransform rootRect = _rootPanel.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.0f, 1.0f);
            rootRect.anchorMax = new Vector2(0.0f, 1.0f);
            rootRect.pivot = new Vector2(0.0f, 1.0f);
            rootRect.anchoredPosition = new Vector2(22.0f, -22.0f);
            rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            VerticalLayoutGroup rootLayout = _rootPanel.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(18, 18, 18, 18);
            rootLayout.spacing = 12.0f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            _titleText = CreateText("Title", _rootPanel.transform, "Paint Bucket Control Panel", TitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.color = TextColor;
            AddLayoutElement(_titleText.gameObject, 50.0f);

            _statusText = CreateText("Status", _rootPanel.transform, "Status", SmallFontSize, FontStyle.Normal, TextAnchor.MiddleCenter);
            _statusText.color = MutedTextColor;
            AddLayoutElement(_statusText.gameObject, 38.0f);

            GameObject tabRow = CreateHorizontalGroup("Tabs", _rootPanel.transform, 10.0f, 58.0f);

            RectTransform content;
            ScrollRect scrollRect = CreateStableScrollView(_rootPanel.transform, out content);
            _settingsScrollRect = scrollRect;
            _settingsContentRect = content;
            ClearChildren(content);

            VerticalLayoutGroup contentLayout = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (contentLayout == null)
                contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 10.0f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _ropePanel = CreateTabPanel("RopePanel", content);
_bucketPanel = CreateTabPanel("BucketPanel", content);
_paintPanel = CreateTabPanel("PaintPanel", content);
_surfacePanel = CreateTabPanel("SurfacePanel", content);
_cameraPanel = CreateTabPanel("CameraPanel", content);

            RegisterTab(_ropePanel, AddTabButton(tabRow.transform, "Rope", () => ShowPanel(_ropePanel)));
            RegisterTab(_bucketPanel, AddTabButton(tabRow.transform, "Bucket", () => ShowPanel(_bucketPanel)));
            RegisterTab(_paintPanel, AddTabButton(tabRow.transform, "Paint", () => ShowPanel(_paintPanel)));
            RegisterTab(_surfacePanel, AddTabButton(tabRow.transform, "Canvas", () => ShowPanel(_surfacePanel)));
            RegisterTab(_cameraPanel, AddTabButton(tabRow.transform, "Camera", () => ShowPanel(_cameraPanel)));

            BuildRopeTab(_ropePanel.transform);
            BuildBucketTab(_bucketPanel.transform);
            BuildPaintTab(_paintPanel.transform);
            BuildSurfaceTab(_surfacePanel.transform);
            BuildCameraTab(_cameraPanel.transform);
            CreateRuntimeCameras();
            LoadActiveCameraValues();

            ForceActivePanelGeometry(_ropePanel);
            RefreshLayoutNow();

            GameObject controlsRow = CreateHorizontalGroup("Controls", _rootPanel.transform, 10.0f, 58.0f);
            Button pauseButton = AddButton(controlsRow.transform, "Pause", OnPauseClicked);
            _pauseButtonText = pauseButton.GetComponentInChildren<Text>();
            AddButton(controlsRow.transform, "Step", OnStepClicked);
            AddButton(controlsRow.transform, "Reset", OnResetClicked);
            AddButton(controlsRow.transform, "Hide", ToggleUIVisibility);

            RefreshLayoutNow();
        }

        private void BuildRopeTab(Transform parent)
        {
            CreateSection(parent, "Rope Settings");

            _ropeLength = AddSlider(parent, "Length", 0.5f, 5.0f, false, "m", value => _pendingRopeLength = value);
            _ropeSegments = AddSlider(parent, "Segment Count", 4.0f, 128.0f, true, "", value => _pendingRopeSegments = Mathf.RoundToInt(value));
            _ropeMass = AddSlider(parent, "Rope Mass", 0.01f, 2.0f, false, "kg", value => _pendingRopeMass = value);
            _ropeVisualRadius = AddSlider(parent, "Visual Radius", 0.001f, 0.06f, false, "m", value => _pendingRopeVisualRadius = value);
            _ropeGravityScale = AddSlider(parent, "Gravity Scale", 0.0f, 2.0f, false, "", value => _pendingRopeGravityScale = value);
            _ropeDamping = AddSlider(parent, "Damping", 0.0f, 2.0f, false, "", value => _pendingRopeDamping = value);
            _ropeSolverIterations = AddSlider(parent, "Solver Iterations", 1.0f, 64.0f, true, "", value => _pendingRopeSolverIterations = Mathf.RoundToInt(value));
            _ropeBreakTension = AddSlider(parent, "Break Tension", 1.0f, 300.0f, false, "N", value => _pendingRopeBreakTension = value);

            _ropeGrabToggle = AddToggle(parent, "Enable Grab", value => _pendingRopeGrab = value);
            _ropeBreakToggle = AddToggle(parent, "Enable Break", value => _pendingRopeBreak = value);

            AddButton(parent, "Apply Rope + Reset", ApplyRopeSettings);
        }

        private void BuildBucketTab(Transform parent)
        {
            CreateSection(parent, "Bucket Settings");

            _bucketHeight = AddSlider(parent, "Height", 0.10f, 1.50f, false, "m", value => _pendingBucketHeight = value);
            _bucketTopRadius = AddSlider(parent, "Top Radius", 0.03f, 0.50f, false, "m", value => _pendingBucketTopRadius = value);
            _bucketBottomRadius = AddSlider(parent, "Bottom Radius", 0.03f, 0.50f, false, "m", value => _pendingBucketBottomRadius = value);
            _bucketMass = AddSlider(parent, "Bucket Mass", 0.10f, 10.0f, false, "kg", value => _pendingBucketMass = value);
            _bucketWallThickness = AddSlider(parent, "Wall Thickness", 0.001f, 0.05f, false, "m", value => _pendingBucketWallThickness = value);

            CreateSection(parent, "Hole Settings");

            _bucketHoleRadius = AddSlider(parent, "Hole Radius", 0.001f, 0.15f, false, "m", value => _pendingBucketHoleRadius = value);
            _bucketFlow = AddSlider(parent, "Flow Multiplier", 0.0f, 5.0f, false, "", value => _pendingBucketFlow = value);
            _bucketExitVelocity = AddSlider(parent, "Exit Velocity", 0.0f, 2.0f, false, "m/s", value => _pendingBucketExitVelocity = value);
            _bucketHoleActiveToggle = AddToggle(parent, "Open Hole / Enable Outflow", value => _pendingBucketHoleActive = value);

            CreateSection(parent, "Bucket Color");
            GameObject colors = CreateHorizontalGroup("BucketColors", parent, 5.0f, 34.0f);
            AddColorButton(colors.transform, "Gray", new Color(0.55f, 0.55f, 0.58f), color =>
            {
                if (_bucketSystem != null && _bucketSystem.Config != null)
                    _bucketSystem.Config.bucketColor = color;
            });
            AddColorButton(colors.transform, "Red", Color.red, color =>
            {
                if (_bucketSystem != null && _bucketSystem.Config != null)
                    _bucketSystem.Config.bucketColor = color;
            });
            AddColorButton(colors.transform, "Blue", Color.blue, color =>
            {
                if (_bucketSystem != null && _bucketSystem.Config != null)
                    _bucketSystem.Config.bucketColor = color;
            });
            AddColorButton(colors.transform, "Black", Color.black, color =>
            {
                if (_bucketSystem != null && _bucketSystem.Config != null)
                    _bucketSystem.Config.bucketColor = color;
            });

            AddButton(parent, "Apply Bucket + Reset", ApplyBucketSettings);
        }

        private void BuildPaintTab(Transform parent)
        {
            CreateSection(parent, "Paint Fluid Settings");

            _paintParticles = AddIntField(parent, "Particle Count", value =>
{
    _pendingPaintParticles = value;
});
            _paintFill = AddSlider(parent, "Fill Fraction", 0.05f, 0.95f, false, "", value => _pendingPaintFill = value);
            _paintParticleRadius = AddSlider(parent, "Particle Radius Ratio", 0.25f, 0.65f, false, "", value => _pendingPaintParticleRadius = value);
            _paintVisualSize = AddSlider(parent, "Visual Particle Size", 0.10f, 3.0f, false, "", value => _pendingPaintVisualSize = value);
          _paintMaxRendered = AddIntField(parent, "Max Rendered", value =>
{
    _pendingPaintMaxRendered = value;
});
            _paintRenderToggle = AddToggle(parent, "Render Particles", value => _pendingPaintRenderParticles = value);

            CreateSection(parent, "Paint Material");

            _paintDensity = AddSlider(parent, "Density", 800.0f, 1600.0f, false, "kg/m3", value => _pendingPaintDensity = value);
            _paintViscosity = AddSlider(parent, "Viscosity", 0.001f, 1.0f, false, "Pa.s", value => _pendingPaintViscosity = value);
            _paintSurfaceTension = AddSlider(parent, "Surface Tension", 0.001f, 0.12f, false, "N/m", value => _pendingPaintSurfaceTension = value);
            _paintDrying = AddSlider(parent, "Drying Rate", 0.0f, 0.10f, false, "", value => _pendingPaintDrying = value);
            _paintAbsorption = AddSlider(parent, "Absorption", 0.0f, 1.0f, false, "", value => _pendingPaintAbsorption = value);

            CreateSection(parent, "Material Preset");
            GameObject presetsA = CreateHorizontalGroup("PaintPresetsA", parent, 5.0f, 34.0f);
            AddPresetButton(presetsA.transform, "Water", PaintMaterialPreset.WaterLike);
            AddPresetButton(presetsA.transform, "Thin", PaintMaterialPreset.ThinPaint);
            AddPresetButton(presetsA.transform, "Latex", PaintMaterialPreset.LatexPaint);
            GameObject presetsB = CreateHorizontalGroup("PaintPresetsB", parent, 5.0f, 34.0f);
            AddPresetButton(presetsB.transform, "Thick", PaintMaterialPreset.ThickPaint);
            AddPresetButton(presetsB.transform, "Heavy", PaintMaterialPreset.HeavyBodyPaint);
            AddPresetButton(presetsB.transform, "Custom", PaintMaterialPreset.Custom);

            CreateSection(parent, "Paint Color");
            GameObject paintColors = CreateHorizontalGroup("PaintColors", parent, 5.0f, 34.0f);
            AddColorButton(paintColors.transform, "Blue", new Color(0.1f, 0.35f, 1.0f), color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Red", new Color(1.0f, 0.12f, 0.08f), color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Yellow", Color.yellow, color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Green", Color.green, color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Purple", new Color(0.55f, 0.15f, 0.95f), color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Orange", new Color(1.0f, 0.45f, 0.05f), color => _pendingPaintColor = color);
            AddColorButton(paintColors.transform, "Cyan", Color.cyan, color => _pendingPaintColor = color);
AddColorButton(paintColors.transform, "Magenta", Color.magenta, color => _pendingPaintColor = color);

            CreateSection(parent, "Multi Color");
            _paintMultiColorToggle = AddToggle(parent, "Enable Color Compartments", value => _pendingPaintMultiColor = value);
            _paintColorCompartments = AddSlider(parent, "Color Compartments", 1.0f, 8.0f, true, "", value => _pendingPaintColorCompartments = Mathf.RoundToInt(value));

            AddButton(parent, "Apply Paint + Reset", ApplyPaintSettings);
        }

        private void BuildSurfaceTab(Transform parent)
        {
            CreateSection(parent, "Surface Type");
            GameObject surfaceButtons = CreateHorizontalGroup("SurfaceTypes", parent, 5.0f, 34.0f);
            AddSurfaceButton(surfaceButtons.transform, "Wood", SurfaceType.Wood);
            AddSurfaceButton(surfaceButtons.transform, "Glass", SurfaceType.Glass);
            AddSurfaceButton(surfaceButtons.transform, "Fabric", SurfaceType.Fabric);
            AddSurfaceButton(surfaceButtons.transform, "Metal", SurfaceType.Metal);

            CreateSection(parent, "Canvas Position X / Y / Z");
            _surfacePositionX = AddFloatField(parent, "Position X", value =>
            {
                _pendingSurfacePosition.x = value;
            });
            _surfacePositionY = AddFloatField(parent, "Position Y", value =>
            {
                _pendingSurfacePosition.y = value;
            });
            _surfacePositionZ = AddFloatField(parent, "Position Z", value =>
            {
                _pendingSurfacePosition.z = value;
            });

            CreateSection(parent, "Canvas Rotation X / Y / Z");
            _surfaceRotationX = AddFloatField(parent, "Rotation X", value =>
            {
                _pendingSurfaceRotationEuler.x = value;
            });
            _surfaceRotationY = AddFloatField(parent, "Rotation Y", value =>
            {
                _pendingSurfaceRotationEuler.y = value;
            });
            _surfaceRotationZ = AddFloatField(parent, "Rotation Z", value =>
            {
                _pendingSurfaceRotationEuler.z = value;
            });

            CreateSection(parent, "Paint Film Evolution");
            _surfaceDiffusionRate = AddSlider(parent, "Diffusion Rate", 0.0f, 60.0f, false, "", value =>
            {
                _pendingSurfaceDiffusionRate = value;
            });
            _surfaceDryingRate = AddSlider(parent, "Drying Rate", 0.0f, 0.10f, false, "", value =>
            {
                _pendingSurfaceDryingRate = value;
            });

            GameObject filmButtons = CreateHorizontalGroup("PaintFilmActions", parent, 10.0f, 50.0f);
            AddButton(filmButtons.transform, "Clear Canvas", ClearCanvasOnly);
            AddButton(filmButtons.transform, "Reset Paint Film", ResetPaintFilm);

            AddButton(parent, "Apply Canvas", ApplySurfaceSettings);
        }


        private void BuildCameraTab(Transform parent)
        {
            CreateSection(parent, "Camera Views");

            GameObject camerasA = CreateHorizontalGroup("CameraButtonsA", parent, 6.0f, 44.0f);
            AddButton(camerasA.transform, "Front", () => SelectCamera(0));
            AddButton(camerasA.transform, "Side", () => SelectCamera(1));

            GameObject camerasB = CreateHorizontalGroup("CameraButtonsB", parent, 6.0f, 44.0f);
            AddButton(camerasB.transform, "Top", () => SelectCamera(2));
            AddButton(camerasB.transform, "Canvas", () => SelectCamera(3));

            CreateSection(parent, "Camera Position X / Y / Z");

            _cameraPosX = AddFloatField(parent, "Position X", value => _pendingCameraPosX = value);
            _cameraPosY = AddFloatField(parent, "Position Y", value => _pendingCameraPosY = value);
            _cameraPosZ = AddFloatField(parent, "Position Z", value => _pendingCameraPosZ = value);

            CreateSection(parent, "Camera Rotation X / Y / Z");

            _cameraRotX = AddFloatField(parent, "Rotation X", value => _pendingCameraRotX = value);
            _cameraRotY = AddFloatField(parent, "Rotation Y", value => _pendingCameraRotY = value);
            _cameraRotZ = AddFloatField(parent, "Rotation Z", value => _pendingCameraRotZ = value);

            CreateSection(parent, "Camera Lens");

            _cameraFov = AddSlider(parent, "Field Of View", 20.0f, 100.0f, false, "", value => _pendingCameraFov = value);

            AddButton(parent, "Apply Camera", ApplyActiveCameraPose);
        }

        private void CreateRuntimeCameras()
        {
            if (_runtimeCameras.Count > 0)
                return;

            CreateCameraSlot(
                "RuntimeCamera_Front",
                new Vector3(0.0f, 7.0f, -10.0f),
                new Vector3(15.0f, 0.0f, 0.0f));

            CreateCameraSlot(
                "RuntimeCamera_Side",
                new Vector3(4.5f, 1.4f, 0.0f),
                new Vector3(15.0f, -90.0f, 0.0f));

            CreateCameraSlot(
                "RuntimeCamera_Top",
                new Vector3(0.0f, 8.5f, -1.0f),
                new Vector3(90.0f, 0.0f, 0.0f));

            CreateCameraSlot(
                "RuntimeCamera_Canvas",
                new Vector3(0.0f, 3.0f, -2.5f),
                new Vector3(60.0f, 0.0f, 0.0f));

            SelectCamera(0);
        }

        private void CreateCameraSlot(string name, Vector3 position, Vector3 rotation)
        {
            GameObject cameraObject = new GameObject(name);
            DontDestroyOnLoad(cameraObject);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.eulerAngles = rotation;
            camera.fieldOfView = 60.0f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000.0f;
            camera.enabled = false;

            AudioListener listener = cameraObject.AddComponent<AudioListener>();
            listener.enabled = false;

            _runtimeCameras.Add(camera);
        }

        private void SelectCamera(int index)
        {
            if (index < 0 || index >= _runtimeCameras.Count)
                return;

            _activeCameraIndex = index;

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (camera == null)
                    continue;

                int runtimeIndex = _runtimeCameras.IndexOf(camera);
                bool activeRuntimeCamera = runtimeIndex == _activeCameraIndex;
                camera.enabled = activeRuntimeCamera;

                if (activeRuntimeCamera)
                    camera.gameObject.tag = "MainCamera";
                else if (camera.gameObject.CompareTag("MainCamera"))
                    camera.gameObject.tag = "Untagged";

                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = activeRuntimeCamera;
            }

            LoadActiveCameraValues();
        }

        private void LoadActiveCameraValues()
        {
            Camera camera = GetActiveRuntimeCamera();
            if (camera == null)
                return;

            Vector3 position = camera.transform.position;
            Vector3 rotation = camera.transform.eulerAngles;

            _pendingCameraPosX = position.x;
            _pendingCameraPosY = position.y;
            _pendingCameraPosZ = position.z;

            _pendingCameraRotX = NormalizeAngle(rotation.x);
            _pendingCameraRotY = NormalizeAngle(rotation.y);
            _pendingCameraRotZ = NormalizeAngle(rotation.z);

            _pendingCameraFov = camera.fieldOfView;

            SetFloatField(_cameraPosX, _pendingCameraPosX);
            SetFloatField(_cameraPosY, _pendingCameraPosY);
            SetFloatField(_cameraPosZ, _pendingCameraPosZ);

            SetFloatField(_cameraRotX, _pendingCameraRotX);
            SetFloatField(_cameraRotY, _pendingCameraRotY);
            SetFloatField(_cameraRotZ, _pendingCameraRotZ);

            SetSlider(_cameraFov, _pendingCameraFov);
        }

        private void ApplyActiveCameraPose()
        {
            Camera camera = GetActiveRuntimeCamera();
            if (camera == null)
                return;

            camera.transform.position = new Vector3(
                _pendingCameraPosX,
                _pendingCameraPosY,
                _pendingCameraPosZ);

            camera.transform.eulerAngles = new Vector3(
                _pendingCameraRotX,
                _pendingCameraRotY,
                _pendingCameraRotZ);

            camera.fieldOfView = Mathf.Clamp(_pendingCameraFov, 20.0f, 100.0f);
            LoadActiveCameraValues();
        }

        private Camera GetActiveRuntimeCamera()
        {
            if (_activeCameraIndex < 0 || _activeCameraIndex >= _runtimeCameras.Count)
                return null;

            return _runtimeCameras[_activeCameraIndex];
        }

        private float NormalizeAngle(float angle)
        {
            while (angle > 180.0f)
                angle -= 360.0f;

            while (angle < -180.0f)
                angle += 360.0f;

            return angle;
        }

        private void LoadAllValuesFromProject()
        {
            LoadRopeValues();
            LoadBucketValues();
            LoadPaintValues();
            LoadSurfaceValues();
            LoadActiveCameraValues();
            UpdateStatusText();
            UpdatePauseText();
        }

        private void LoadRopeValues()
        {
            if (_ropeSystem == null || _ropeSystem.Config == null)
                return;

            RopeConfig config = _ropeSystem.Config;
            _pendingRopeLength = config.lengthMeters;
            _pendingRopeSegments = config.segmentCount;
            _pendingRopeMass = config.ropeMassKg;
            _pendingRopeVisualRadius = config.visualRadiusMeters;
            _pendingRopeGravityScale = config.gravityScale;
            _pendingRopeDamping = config.exponentialDampingPerSecond;
            _pendingRopeSolverIterations = config.solverIterations;
            _pendingRopeBreakTension = config.breakTensionNewton;
            _pendingRopeGrab = config.enableInteractiveGrab;
            _pendingRopeBreak = config.enableBreakByTension || config.enableBreakByStrain;

            SetSlider(_ropeLength, _pendingRopeLength);
            SetSlider(_ropeSegments, _pendingRopeSegments);
            SetSlider(_ropeMass, _pendingRopeMass);
            SetSlider(_ropeVisualRadius, _pendingRopeVisualRadius);
            SetSlider(_ropeGravityScale, _pendingRopeGravityScale);
            SetSlider(_ropeDamping, _pendingRopeDamping);
            SetSlider(_ropeSolverIterations, _pendingRopeSolverIterations);
            SetSlider(_ropeBreakTension, _pendingRopeBreakTension);
            SetToggle(_ropeGrabToggle, _pendingRopeGrab);
            SetToggle(_ropeBreakToggle, _pendingRopeBreak);
        }

        private void LoadBucketValues()
        {
            if (_bucketSystem == null || _bucketSystem.Config == null)
                return;

            BucketConfig config = _bucketSystem.Config;
            _pendingBucketHeight = config.heightMeters;
            _pendingBucketTopRadius = config.topRadiusMeters;
            _pendingBucketBottomRadius = config.bottomRadiusMeters;
            _pendingBucketMass = config.massKg;
            _pendingBucketWallThickness = config.wallThicknessMeters;

            BucketHoleConfig hole = GetPrimaryHole(config);

_pendingBucketHoleRadius = hole != null ? hole.radiusMeters : 0.02f;
_pendingBucketFlow = hole != null ? hole.flowMultiplier : 1.0f;
_pendingBucketExitVelocity = hole != null ? hole.exitVelocityBoostMetersPerSecond : 0.2f;

bool anyHoleActive = HasAnyActiveHole(config);
bool gpuHoleOpeningEnabled =
    _paintFluidSystem != null &&
    _paintFluidSystem.GpuMpmConfig != null &&
    _paintFluidSystem.GpuMpmConfig.enableBottomHoleOpening;

_pendingBucketHoleActive = anyHoleActive && gpuHoleOpeningEnabled;

            SetSlider(_bucketHeight, _pendingBucketHeight);
            SetSlider(_bucketTopRadius, _pendingBucketTopRadius);
            SetSlider(_bucketBottomRadius, _pendingBucketBottomRadius);
            SetSlider(_bucketMass, _pendingBucketMass);
            SetSlider(_bucketWallThickness, _pendingBucketWallThickness);
            SetSlider(_bucketHoleRadius, _pendingBucketHoleRadius);
            SetSlider(_bucketFlow, _pendingBucketFlow);
            SetSlider(_bucketExitVelocity, _pendingBucketExitVelocity);
            SetToggle(_bucketHoleActiveToggle, _pendingBucketHoleActive);
        }

        private void LoadPaintValues()
        {
            if (_paintFluidSystem == null)
                return;

            PaintFluidConfig fluid = _paintFluidSystem.FluidConfig;
            PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;

            if (fluid != null)
            {
                _pendingPaintParticles = fluid.targetParticleCount;
                _pendingPaintFill = fluid.fillFraction01;
                _pendingPaintParticleRadius = fluid.particleRadiusToSpacing;
                _pendingPaintVisualSize = fluid.visualParticleSizeScale;
                _pendingPaintMaxRendered = fluid.maxRenderedParticles;
                _pendingPaintRenderParticles = fluid.renderParticles;
                _pendingPaintMultiColor = fluid.enableColorCompartments;
                _pendingPaintColorCompartments = fluid.colorCompartmentCount;

                SetIntField(_paintParticles, _pendingPaintParticles);
                SetSlider(_paintFill, _pendingPaintFill);
                SetSlider(_paintParticleRadius, _pendingPaintParticleRadius);
                SetSlider(_paintVisualSize, _pendingPaintVisualSize);
                SetIntField(_paintMaxRendered, _pendingPaintMaxRendered);
                SetToggle(_paintRenderToggle, _pendingPaintRenderParticles);
                SetToggle(_paintMultiColorToggle, _pendingPaintMultiColor);
                SetSlider(_paintColorCompartments, _pendingPaintColorCompartments);
            }

            if (material != null)
            {
                _pendingPaintPreset = material.materialPreset;
                _pendingPaintColor = material.baseColor;
                _pendingPaintDensity = material.densityKgPerM3;
                _pendingPaintViscosity = material.constantViscosityPaS;
                _pendingPaintSurfaceTension = material.surfaceTensionNPerM;
                _pendingPaintDrying = material.dryingRatePerSecond;
                _pendingPaintAbsorption = material.absorptionRate;

                SetSlider(_paintDensity, _pendingPaintDensity);
                SetSlider(_paintViscosity, _pendingPaintViscosity);
                SetSlider(_paintSurfaceTension, _pendingPaintSurfaceTension);
                SetSlider(_paintDrying, _pendingPaintDrying);
                SetSlider(_paintAbsorption, _pendingPaintAbsorption);
            }
        }

        private void LoadSurfaceValues()
        {
            if (_paintSurface == null)
                return;

            _pendingSurfaceType = _paintSurface.SurfaceType;
            _pendingSurfacePosition = _paintSurface.transform.position;
            _pendingSurfaceRotationEuler = _paintSurface.transform.eulerAngles;

            PaintSurfaceFilmProfile profile = ResolveCurrentSurfaceFilmProfile();
            _pendingSurfaceDiffusionRate = Mathf.Max(profile.diffusionRate, 0.0f);
            _pendingSurfaceDryingRate = Mathf.Max(profile.evaporationRate, 0.0f);

            SetFloatField(_surfacePositionX, _pendingSurfacePosition.x);
            SetFloatField(_surfacePositionY, _pendingSurfacePosition.y);
            SetFloatField(_surfacePositionZ, _pendingSurfacePosition.z);
            SetFloatField(_surfaceRotationX, _pendingSurfaceRotationEuler.x);
            SetFloatField(_surfaceRotationY, _pendingSurfaceRotationEuler.y);
            SetFloatField(_surfaceRotationZ, _pendingSurfaceRotationEuler.z);
            SetSlider(_surfaceDiffusionRate, _pendingSurfaceDiffusionRate);
            SetSlider(_surfaceDryingRate, _pendingSurfaceDryingRate);
        }

        private void ApplyRopeSettings()
        {
            if (_ropeSystem == null || _ropeSystem.Config == null)
                return;

            RopeConfig config = _ropeSystem.Config;
            config.lengthMeters = Mathf.Max(0.05f, _pendingRopeLength);
            config.segmentCount = Mathf.Clamp(_pendingRopeSegments, 2, 128);
            config.ropeMassKg = Mathf.Max(0.0001f, _pendingRopeMass);
            config.visualRadiusMeters = Mathf.Max(0.001f, _pendingRopeVisualRadius);
            config.gravityScale = Mathf.Clamp(_pendingRopeGravityScale, 0.0f, 2.0f);
            config.exponentialDampingPerSecond = Mathf.Clamp(_pendingRopeDamping, 0.0f, 10.0f);
            config.solverIterations = Mathf.Clamp(_pendingRopeSolverIterations, 1, 64);
            config.enableInteractiveGrab = _pendingRopeGrab;
            config.enableBreakByTension = _pendingRopeBreak;
config.enableBreakByStrain = _pendingRopeBreak;
config.breakTensionNewton = Mathf.Max(0.01f, _pendingRopeBreakTension);
            ResetSimulation();
            LoadRopeValues();
        }

        private void ApplyBucketSettings()
        {
            if (_bucketSystem == null || _bucketSystem.Config == null)
                return;

            BucketConfig config = _bucketSystem.Config;
            config.heightMeters = Mathf.Max(0.05f, _pendingBucketHeight);
            config.topRadiusMeters = Mathf.Max(0.02f, _pendingBucketTopRadius);
            config.bottomRadiusMeters = Mathf.Max(0.02f, _pendingBucketBottomRadius);
            config.massKg = Mathf.Max(0.01f, _pendingBucketMass);
            config.wallThicknessMeters = Mathf.Max(0.001f, _pendingBucketWallThickness);

           ApplyHoleSettingsToAllHoles(config);

if (_paintFluidSystem != null && _paintFluidSystem.GpuMpmConfig != null)
{
    _paintFluidSystem.GpuMpmConfig.enableBottomHoleOpening = _pendingBucketHoleActive;
    _paintFluidSystem.GpuMpmConfig.classifyBottomHoleRegion = _pendingBucketHoleActive;
}

            ResetSimulation();
            LoadBucketValues();
        }

        private void ApplyPaintSettings()
        {
            if (_paintFluidSystem == null)
                return;

            PaintFluidConfig fluid = _paintFluidSystem.FluidConfig;
            PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;

            if (fluid != null)
            {
                fluid.targetParticleCount = Mathf.Max(1, _pendingPaintParticles);
                fluid.maxParticleCapacity = Mathf.Max(fluid.maxParticleCapacity, fluid.targetParticleCount);
                fluid.fillFraction01 = Mathf.Clamp(_pendingPaintFill, 0.01f, 0.95f);
                fluid.particleRadiusToSpacing = Mathf.Clamp(_pendingPaintParticleRadius, 0.25f, 0.65f);
                fluid.visualParticleSizeScale = Mathf.Max(0.001f, _pendingPaintVisualSize);
                fluid.maxRenderedParticles = Mathf.Max(0, _pendingPaintMaxRendered);
                fluid.renderParticles = _pendingPaintRenderParticles;
                fluid.enableColorCompartments = _pendingPaintMultiColor;
                fluid.colorCompartmentCount = Mathf.Clamp(_pendingPaintColorCompartments, 1, 8);

                if (fluid.compartmentColors == null || fluid.compartmentColors.Length < 2)
                {
                    fluid.compartmentColors = new[]
                    {
                        _pendingPaintColor,
                        Color.red
                    };
                }
                else
                {
                    fluid.compartmentColors[0] = _pendingPaintColor;
                }
            }

            if (material != null)
            {
                material.materialPreset = _pendingPaintPreset;
                material.autoApplyMaterialPreset = false;
                material.baseColor = _pendingPaintColor;
                material.densityKgPerM3 = Mathf.Max(1.0f, _pendingPaintDensity);
                material.constantViscosityPaS = Mathf.Max(0.0001f, _pendingPaintViscosity);
                material.surfaceTensionNPerM = Mathf.Max(0.0001f, _pendingPaintSurfaceTension);
                material.dryingRatePerSecond = Mathf.Max(0.0f, _pendingPaintDrying);
                material.absorptionRate = Mathf.Max(0.0f, _pendingPaintAbsorption);
            }

            ResetSimulation();
            LoadPaintValues();
        }

        private void ApplySurfaceSettings()
        {
            if (_paintSurface == null)
                return;

            _paintSurface.transform.position = _pendingSurfacePosition;
            _paintSurface.transform.rotation = Quaternion.Euler(_pendingSurfaceRotationEuler);

            SetPrivateField(_paintSurface, "_surfaceType", _pendingSurfaceType);

            PaintSurfaceFilmProfile profile = ResolveCurrentSurfaceFilmProfile();
            profile.diffusionRate = Mathf.Max(_pendingSurfaceDiffusionRate, 0.0f);
            profile.evaporationRate = Mathf.Max(_pendingSurfaceDryingRate, 0.0f);
            _paintSurface.ConfigureFilmMaterial(profile);

            InvokePrivateMethod(_paintSurface, "OnValidate");
            _paintSurface.RefreshSurfaceFrameFromTransform();
            InvokePrivateMethod(_paintSurface, "ApplySurfaceAppearanceToRenderer");
            InvokePrivateMethod(_paintSurface, "ApplyFilmMaterialToEvolver");
            InvokePrivateMethod(_paintSurface, "ApplyColorMixingToEvolver");

            LoadSurfaceValues();
        }

        private void ClearCanvasOnly()
        {
            ClearPaintFilmBuffers(false);
        }

        private void ResetPaintFilm()
        {
            ClearPaintFilmBuffers(true);
        }

        private void ClearPaintFilmBuffers(bool refreshSurfaceFrame)
        {
            if (_paintSurface == null || _paintSurface.PaintFilmGrid == null)
                return;

            _paintSurface.PaintFilmGrid.Clear();

            if (refreshSurfaceFrame)
                _paintSurface.RefreshSurfaceFrameFromTransform();

            InvokePrivateMethod(_paintSurface, "ApplySurfaceAppearanceToRenderer");
            _paintSurface.Render();
        }

        private PaintSurfaceFilmProfile ResolveCurrentSurfaceFilmProfile()
        {
            if (_paintSurface == null)
                return DefaultSurfaceFilmProfile();

            bool hasConfiguredFilmMaterial = GetPrivateField(
                _paintSurface,
                "_hasConfiguredFilmMaterial",
                false
            );

            PaintSurfaceFilmProfile fallback = DefaultSurfaceFilmProfile();
            if (!hasConfiguredFilmMaterial)
                return fallback;

            PaintSurfaceFilmProfile profile = GetPrivateField(
                _paintSurface,
                "_filmProfile",
                fallback
            );

            return profile.Sanitized();
        }

        private PaintSurfaceFilmProfile DefaultSurfaceFilmProfile()
        {
            if (_paintFluidSystem != null && _paintFluidSystem.MaterialConfig != null)
            {
                return _paintFluidSystem.MaterialConfig
                    .EvaluateSurfaceFilmProfile(20.0f, 20.0f)
                    .Sanitized();
            }

            return PaintSurfaceFilmProfile.LatexPaint(
                1150.0f,
                0.16f,
                0.035f,
                0.0f
            );
        }

        private void OnPauseClicked()
        {
            if (_simulationManager == null || _simulationManager.TimeController == null)
                return;

            _simulationManager.TimeController.TogglePause();
            UpdatePauseText();
        }

        private void OnStepClicked()
        {
            if (_simulationManager == null || _simulationManager.TimeController == null)
                return;

            _simulationManager.TimeController.SetPaused(true);
            _simulationManager.TimeController.RequestSingleStep();
            UpdatePauseText();
        }

        private void OnResetClicked()
        {
            ResetSimulation();
            LoadAllValuesFromProject();
        }

        private void ResetSimulation()
        {
            if (_simulationManager != null)
                _simulationManager.ResetSimulationState();
        }

        private void ToggleUIVisibility()
        {
            if (_rootPanel != null)
                _rootPanel.SetActive(!_rootPanel.activeSelf);
        }

        private void UpdatePauseText()
        {
            if (_pauseButtonText == null)
                return;

            bool paused = _simulationManager != null &&
                          _simulationManager.TimeController != null &&
                          _simulationManager.TimeController.IsPaused;

            _pauseButtonText.text = paused ? "Resume" : "Pause";
        }

        private void UpdateStatusText()
        {
            if (_statusText == null)
                return;

            int ropeParticles = _ropeSystem != null ? _ropeSystem.ParticleCount : 0;
            int paintParticles = _paintFluidSystem != null ? _paintFluidSystem.ParticleCount : 0;
            bool paused = _simulationManager != null &&
                          _simulationManager.TimeController != null &&
                          _simulationManager.TimeController.IsPaused;

            _statusText.text =
                $"State: {(paused ? "Paused" : "Running")} | Rope: {ropeParticles} | Paint: {paintParticles}";
        }

        private void ShowPanel(GameObject panel)
        {
            foreach (GameObject tabPanel in _tabPanels)
            {
                if (tabPanel != null)
                    tabPanel.SetActive(false);
            }

            if (panel != null)
            {
                panel.SetActive(true);
                ForceActivePanelGeometry(panel);
            }

            if (_settingsScrollRect != null)
                _settingsScrollRect.verticalNormalizedPosition = 1.0f;

            UpdateTabVisuals(panel);
            RefreshLayoutNow();
        }

        private ScrollRect CreateStableScrollView(Transform parent, out RectTransform content)
        {
            GameObject scrollView = new GameObject(
                "SettingsScrollView",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));

            scrollView.transform.SetParent(parent, false);
            AddLayoutElement(scrollView, 720.0f, flexibleHeight: 1.0f);

            Image background = scrollView.GetComponent<Image>();
            background.color = new Color(0.145f, 0.180f, 0.235f, 0.98f);

            RectTransform scrollRectTransform = scrollView.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
            scrollRectTransform.anchoredPosition = Vector2.zero;
            scrollRectTransform.sizeDelta = Vector2.zero;

            GameObject viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(Mask));

            viewportObject.transform.SetParent(scrollView.transform, false);

            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            viewport.offsetMin = new Vector2(0.0f, 0.0f);
            viewport.offsetMax = new Vector2(-30.0f, 0.0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0.120f, 0.155f, 0.205f, 0.96f);

            Mask mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = true;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);

            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0.0f, 1.0f);
            content.anchorMax = new Vector2(1.0f, 1.0f);
            content.pivot = new Vector2(0.5f, 1.0f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0.0f, 1400.0f);

            GameObject scrollbarObject = new GameObject(
                "VerticalScrollbar",
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));

            scrollbarObject.transform.SetParent(scrollView.transform, false);

            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1.0f, 0.0f);
            scrollbarRect.anchorMax = new Vector2(1.0f, 1.0f);
            scrollbarRect.pivot = new Vector2(1.0f, 0.5f);
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollbarRect.sizeDelta = new Vector2(24.0f, 0.0f);

            Image scrollbarBackground = scrollbarObject.GetComponent<Image>();
            scrollbarBackground.color = new Color(0.210f, 0.260f, 0.335f, 1.0f);

            Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            GameObject slidingAreaObject = new GameObject("Sliding Area", typeof(RectTransform));
            slidingAreaObject.transform.SetParent(scrollbarObject.transform, false);

            RectTransform slidingArea = slidingAreaObject.GetComponent<RectTransform>();
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.offsetMin = new Vector2(4.0f, 4.0f);
            slidingArea.offsetMax = new Vector2(-4.0f, -4.0f);

            GameObject handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(Image));

            handleObject.transform.SetParent(slidingAreaObject.transform, false);

            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = AccentColor;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;

            ColorBlock scrollbarColors = scrollbar.colors;
            scrollbarColors.normalColor = AccentColor;
            scrollbarColors.highlightedColor = Color.Lerp(AccentColor, Color.white, 0.18f);
            scrollbarColors.pressedColor = AccentColorPressed;
            scrollbarColors.selectedColor = AccentColor;
            scrollbarColors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.45f);
            scrollbarColors.colorMultiplier = 1.0f;
            scrollbarColors.fadeDuration = 0.08f;
            scrollbar.colors = scrollbarColors;

            ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 34.0f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.verticalScrollbarSpacing = 6.0f;

            return scrollRect;
        }

        private void ForceActivePanelGeometry(GameObject panel)
        {
            if (panel == null)
                return;

            float height = GetPanelHeight(panel);

            RectTransform rect = panel.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.0f, 1.0f);
                rect.anchorMax = new Vector2(1.0f, 1.0f);
                rect.pivot = new Vector2(0.5f, 1.0f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(0.0f, height);
            }

            LayoutElement panelLayout = panel.GetComponent<LayoutElement>();
            if (panelLayout == null)
                panelLayout = panel.AddComponent<LayoutElement>();

            panelLayout.minHeight = height;
            panelLayout.preferredHeight = height;
            panelLayout.flexibleHeight = 0.0f;

            if (_settingsContentRect != null)
            {
                _settingsContentRect.anchorMin = new Vector2(0.0f, 1.0f);
                _settingsContentRect.anchorMax = new Vector2(1.0f, 1.0f);
                _settingsContentRect.pivot = new Vector2(0.5f, 1.0f);
                _settingsContentRect.anchoredPosition = Vector2.zero;
                _settingsContentRect.sizeDelta = new Vector2(0.0f, height + 24.0f);
            }
        }

        private float GetPanelHeight(GameObject panel)
        {
            if (panel == _paintPanel)
                return 1660.0f;

            if (panel == _bucketPanel)
                return 1160.0f;

            if (panel == _surfacePanel)
                return 1120.0f;

            if (panel == _cameraPanel)
                return 1120.0f;

            return 1180.0f;
        }

        private void NormalizeScrollViewLayout(ScrollRect scrollRect)
        {
            if (scrollRect == null)
                return;

            RectTransform scrollRectTransform = scrollRect.GetComponent<RectTransform>();
            if (scrollRectTransform != null)
            {
                scrollRectTransform.anchorMin = Vector2.zero;
                scrollRectTransform.anchorMax = Vector2.one;
                scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
                scrollRectTransform.anchoredPosition = Vector2.zero;
                scrollRectTransform.sizeDelta = Vector2.zero;
            }

            if (scrollRect.viewport != null)
            {
                RectTransform viewport = scrollRect.viewport;
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.anchoredPosition = Vector2.zero;
                viewport.offsetMin = new Vector2(0.0f, 0.0f);
                viewport.offsetMax = new Vector2(-30.0f, 0.0f);
            }

            if (scrollRect.content != null)
            {
                RectTransform scrollContent = scrollRect.content;
                scrollContent.anchorMin = new Vector2(0.0f, 1.0f);
                scrollContent.anchorMax = new Vector2(1.0f, 1.0f);
                scrollContent.pivot = new Vector2(0.5f, 1.0f);
                scrollContent.anchoredPosition = Vector2.zero;
            }

            if (scrollRect.verticalScrollbar != null)
            {
                RectTransform scrollbar = scrollRect.verticalScrollbar.GetComponent<RectTransform>();
                if (scrollbar != null)
                {
                    scrollbar.anchorMin = new Vector2(1.0f, 0.0f);
                    scrollbar.anchorMax = new Vector2(1.0f, 1.0f);
                    scrollbar.pivot = new Vector2(1.0f, 0.5f);
                    scrollbar.sizeDelta = new Vector2(24.0f, 0.0f);
                    scrollbar.anchoredPosition = Vector2.zero;
                }
            }
        }

        private void RefreshLayoutNow()
        {
            NormalizeScrollViewLayout(_settingsScrollRect);
            Canvas.ForceUpdateCanvases();

            if (_settingsContentRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsContentRect);

            if (_rootPanel != null)
            {
                RectTransform rootRect = _rootPanel.GetComponent<RectTransform>();
                if (rootRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }

            Canvas.ForceUpdateCanvases();
        }

        private GameObject CreateTabPanel(string name, Transform parent)
        {
            GameObject panel = CreatePanel(name, parent, TransparentColor);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 10.0f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            LayoutElement panelLayout = panel.AddComponent<LayoutElement>();
            panelLayout.minHeight = 900.0f;
            panelLayout.preferredHeight = 1200.0f;
            panelLayout.flexibleHeight = 0.0f;

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0.0f, 1200.0f);

            _tabPanels.Add(panel);
            return panel;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.color = color;
            return panel;
        }

        private void CreateSection(Transform parent, string text)
        {
            GameObject sectionObject = new GameObject("Section_" + text, typeof(RectTransform), typeof(Image));
            sectionObject.transform.SetParent(parent, false);

            Image sectionImage = sectionObject.GetComponent<Image>();
            sectionImage.color = FieldColor;

            HorizontalLayoutGroup layout = sectionObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 0, 0);
            layout.spacing = 0.0f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            Text section = CreateText("SectionText_" + text, sectionObject.transform, text, HeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            section.color = SectionColor;
            AddLayoutElement(section.gameObject, 40.0f);

            AddLayoutElement(sectionObject, 44.0f);
        }

        private GameObject CreateHorizontalGroup(string name, Transform parent, float spacing, float height)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            AddLayoutElement(row, height);
            return row;
        }

        private Text CreateText(
            string name,
            Transform parent,
            string value,
            int size,
            FontStyle style,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            Text text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = TextColor;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = DefaultControls.CreateButton(_uiResources);
            buttonObject.name = label + "Button";
            buttonObject.transform.SetParent(parent, false);
            AddLayoutElement(buttonObject, 46.0f);

            Text text = buttonObject.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.font = _font;
                text.fontSize = ButtonFontSize;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
            }

            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(action);

            bool primary = label.StartsWith("Apply", StringComparison.OrdinalIgnoreCase);
            bool danger = string.Equals(label, "Reset", StringComparison.OrdinalIgnoreCase);

            if (primary)
                ApplyButtonTheme(button, AccentColor, Color.Lerp(AccentColor, Color.white, 0.12f), AccentColorPressed, DarkTextColor);
            else if (danger)
                ApplyButtonTheme(button, DangerColor, Color.Lerp(DangerColor, Color.white, 0.12f), Color.Lerp(DangerColor, Color.black, 0.18f), TextColor);
            else
                ApplyButtonTheme(button, FieldColorLight, Color.Lerp(FieldColorLight, Color.white, 0.10f), FieldColor, TextColor);

            return button;
        }

        private Button AddTabButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = DefaultControls.CreateButton(_uiResources);
            buttonObject.name = label + "TabButton";
            buttonObject.transform.SetParent(parent, false);

            LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = buttonObject.AddComponent<LayoutElement>();

            layoutElement.preferredHeight = 56.0f;
            layoutElement.flexibleWidth = 1.0f;

            Text text = buttonObject.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.font = _font;
                text.fontSize = TabFontSize;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
            }

            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(action);
            _tabButtons.Add(button);
            UpdateTabButtonStyle(button, false);

            return button;
        }

        private void RegisterTab(GameObject panel, Button button)
        {
            if (panel == null || button == null)
                return;

            _panelToTabButton[panel] = button;
        }

        private void UpdateTabVisuals(GameObject activePanel)
        {
            foreach (Button button in _tabButtons)
                UpdateTabButtonStyle(button, false);

            if (activePanel != null && _panelToTabButton.TryGetValue(activePanel, out Button activeButton))
                UpdateTabButtonStyle(activeButton, true);
        }

        private void UpdateTabButtonStyle(Button button, bool active)
        {
            if (button == null)
                return;

            Color normal = active ? AccentColor : FieldColor;
            Color highlighted = active ? Color.Lerp(AccentColor, Color.white, 0.16f) : FieldColorLight;
            Color pressed = active ? AccentColorPressed : Color.Lerp(FieldColor, Color.black, 0.16f);
            Color textColor = active ? DarkTextColor : TextColor;

            ApplyButtonTheme(button, normal, highlighted, pressed, textColor);
        }

        private void AddColorButton(Transform parent, string label, Color color, Action<Color> onSelected)
        {
            Button button = AddButton(parent, label, () => onSelected(color));
            Color normal = Color.Lerp(color, Color.white, 0.25f);
            Color highlighted = Color.Lerp(color, Color.white, 0.42f);
            Color pressed = Color.Lerp(color, Color.black, 0.18f);
            ApplyButtonTheme(button, normal, highlighted, pressed, GetReadableTextColor(normal));
        }

        private Toggle AddToggle(Transform parent, string label, UnityEngine.Events.UnityAction<bool> onChanged)
        {
            GameObject toggleObject = DefaultControls.CreateToggle(_uiResources);
            toggleObject.name = label + "Toggle";
            toggleObject.transform.SetParent(parent, false);
            AddLayoutElement(toggleObject, 44.0f);

            Text text = toggleObject.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.font = _font;
                text.fontSize = BodyFontSize;
                text.fontStyle = FontStyle.Normal;
                text.color = TextColor;
            }

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(onChanged);

            Image background = FindChildImage(toggleObject.transform, "Background");
            if (background != null)
                background.color = FieldColorLight;

            Image checkmark = FindChildImage(toggleObject.transform, "Checkmark");
            if (checkmark != null)
                checkmark.color = AccentColor;

            return toggle;
        }

        private SliderControl AddSlider(
            Transform parent,
            string label,
            float min,
            float max,
            bool wholeNumbers,
            string suffix,
            Action<float> onChanged)
        {
            GameObject container = new GameObject(label + "Container", typeof(RectTransform), typeof(Image));
            container.transform.SetParent(parent, false);

            Image containerImage = container.GetComponent<Image>();
            containerImage.color = CardColorSoft;

            VerticalLayoutGroup containerLayout = container.AddComponent<VerticalLayoutGroup>();
            containerLayout.padding = new RectOffset(12, 12, 9, 9);
            containerLayout.spacing = 5.0f;
            containerLayout.childControlWidth = true;
            containerLayout.childControlHeight = true;
            containerLayout.childForceExpandWidth = true;
            containerLayout.childForceExpandHeight = false;
            AddLayoutElement(container, 94.0f);

            GameObject textRow = CreateHorizontalGroup(label + "TextRow", container.transform, 8.0f, 28.0f);
            Text labelText = CreateText(label + "Label", textRow.transform, label, SliderLabelFontSize, FontStyle.Normal, TextAnchor.MiddleLeft);
            labelText.color = TextColor;
            Text valueText = CreateText(label + "Value", textRow.transform, "", SliderLabelFontSize, FontStyle.Bold, TextAnchor.MiddleRight);
            valueText.color = AccentColor;
            AddLayoutElement(labelText.gameObject, 26.0f);
            AddLayoutElement(valueText.gameObject, 26.0f);

            GameObject sliderObject = DefaultControls.CreateSlider(_uiResources);
            sliderObject.name = label + "Slider";
            sliderObject.transform.SetParent(container.transform, false);
            AddLayoutElement(sliderObject, 40.0f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            StyleSlider(slider);

            slider.onValueChanged.AddListener(value =>
            {
                valueText.text = FormatValue(value, wholeNumbers, suffix);
                onChanged(value);
            });

            return new SliderControl(slider, valueText, wholeNumbers, suffix);
        }


        private IntFieldControl AddIntField(
            Transform parent,
            string label,
            Action<int> onChanged)
        {
            InputField inputField = AddInputFieldBase(
                parent,
                label,
                "Enter number",
                TextAnchor.MiddleRight,
                out _
            );

            inputField.contentType = InputField.ContentType.IntegerNumber;
            inputField.characterValidation = InputField.CharacterValidation.Integer;
            inputField.lineType = InputField.LineType.SingleLine;

            inputField.onValueChanged.AddListener(text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    onChanged(parsed);
            });

            inputField.onEndEdit.AddListener(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    inputField.SetTextWithoutNotify(parsed.ToString(CultureInfo.InvariantCulture));
                    onChanged(parsed);
                }
            });

            return new IntFieldControl(inputField);
        }

        private FloatFieldControl AddFloatField(
            Transform parent,
            string label,
            Action<float> onChanged)
        {
            InputField inputField = AddInputFieldBase(
                parent,
                label,
                "Enter value",
                TextAnchor.MiddleRight,
                out _
            );

            inputField.contentType = InputField.ContentType.Standard;
            inputField.characterValidation = InputField.CharacterValidation.None;
            inputField.lineType = InputField.LineType.SingleLine;

            inputField.onValueChanged.AddListener(text =>
            {
                if (TryParseFloat(text, out float parsed))
                    onChanged(parsed);
            });

            inputField.onEndEdit.AddListener(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                if (TryParseFloat(text, out float parsed))
                {
                    inputField.SetTextWithoutNotify(FormatFloat(parsed));
                    onChanged(parsed);
                }
            });

            return new FloatFieldControl(inputField);
        }

        private InputField AddInputFieldBase(
            Transform parent,
            string label,
            string placeholder,
            TextAnchor inputAlignment,
            out Text inputText)
        {
            GameObject container = new GameObject(label + "Container", typeof(RectTransform), typeof(Image));
            container.transform.SetParent(parent, false);

            Image containerImage = container.GetComponent<Image>();
            containerImage.color = CardColorSoft;

            HorizontalLayoutGroup containerLayout = container.AddComponent<HorizontalLayoutGroup>();
            containerLayout.padding = new RectOffset(12, 12, 9, 9);
            containerLayout.spacing = 12.0f;
            containerLayout.childControlWidth = true;
            containerLayout.childControlHeight = true;
            containerLayout.childForceExpandWidth = true;
            containerLayout.childForceExpandHeight = true;

            AddLayoutElement(container, 70.0f);

            Text labelText = CreateText(
                label + "Label",
                container.transform,
                label,
                SliderLabelFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            labelText.color = TextColor;

            LayoutElement labelLayout = labelText.gameObject.GetComponent<LayoutElement>();
            if (labelLayout == null)
                labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1.0f;
            labelLayout.preferredHeight = 48.0f;

            GameObject inputObject = new GameObject(
                label + "InputField",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField));
            inputObject.transform.SetParent(container.transform, false);

            Image inputBackground = inputObject.GetComponent<Image>();
            inputBackground.color = FieldColor;

            LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
            inputLayout.preferredWidth = 260.0f;
            inputLayout.preferredHeight = 48.0f;
            inputLayout.flexibleWidth = 0.0f;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(inputObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12.0f, 6.0f);
            textRect.offsetMax = new Vector2(-12.0f, -6.0f);

            inputText = textObject.GetComponent<Text>();
            inputText.font = _font;
            inputText.fontSize = BodyFontSize;
            inputText.fontStyle = FontStyle.Bold;
            inputText.color = AccentColor;
            inputText.alignment = inputAlignment;
            inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            inputText.verticalOverflow = VerticalWrapMode.Overflow;

            GameObject placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderObject.transform.SetParent(inputObject.transform, false);

            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(12.0f, 6.0f);
            placeholderRect.offsetMax = new Vector2(-12.0f, -6.0f);

            Text placeholderText = placeholderObject.GetComponent<Text>();
            placeholderText.font = _font;
            placeholderText.fontSize = BodyFontSize;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = MutedTextColor;
            placeholderText.alignment = inputAlignment;
            placeholderText.text = placeholder;

            InputField inputField = inputObject.GetComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;

            ColorBlock colors = inputField.colors;
            colors.normalColor = FieldColor;
            colors.highlightedColor = FieldColorLight;
            colors.pressedColor = FieldColorLight;
            colors.selectedColor = FieldColorLight;
            colors.disabledColor = new Color(0.18f, 0.20f, 0.23f, 0.55f);
            colors.colorMultiplier = 1.0f;
            colors.fadeDuration = 0.08f;
            inputField.colors = colors;

            return inputField;
        }

        private void StyleScrollView(GameObject scrollView)
        {
            Image background = scrollView.GetComponent<Image>();
            if (background != null)
                background.color = CardColor;

            ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();
            if (scrollRect == null)
                return;

            if (scrollRect.viewport != null)
            {
                Image viewportImage = scrollRect.viewport.GetComponent<Image>();
                if (viewportImage != null)
                    viewportImage.color = TransparentColor;
            }

            if (scrollRect.verticalScrollbar != null)
            {
                Image scrollbarImage = scrollRect.verticalScrollbar.GetComponent<Image>();
                if (scrollbarImage != null)
                    scrollbarImage.color = FieldColor;

                Image handle = FindChildImage(scrollRect.verticalScrollbar.transform, "Handle");
                if (handle != null)
                    handle.color = FieldColorLight;
            }

            if (scrollRect.horizontalScrollbar != null)
                scrollRect.horizontalScrollbar.gameObject.SetActive(false);
        }

        private void StyleSlider(Slider slider)
        {
            if (slider == null)
                return;

            Image background = FindChildImage(slider.transform, "Background");
            if (background != null)
                background.color = FieldColor;

            Image fill = FindChildImage(slider.transform, "Fill");
            if (fill != null)
                fill.color = AccentColor;

            Image handle = FindChildImage(slider.transform, "Handle");
            if (handle != null)
                handle.color = TextColor;

            RectTransform handleRect = slider.handleRect;
            if (handleRect != null)
                handleRect.sizeDelta = new Vector2(24.0f, 24.0f);
        }

        private void ApplyButtonTheme(Button button, Color normal, Color highlighted, Color pressed, Color textColor)
        {
            if (button == null)
                return;

            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = normal;

            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = new Color(0.18f, 0.20f, 0.23f, 0.55f);
            colors.colorMultiplier = 1.0f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.color = textColor;
        }

        private Image FindChildImage(Transform root, string childName)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                    return child.GetComponent<Image>();
            }

            return null;
        }

        private Color GetReadableTextColor(Color background)
        {
            float luminance = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            return luminance > 0.58f ? DarkTextColor : TextColor;
        }

        private void AddPresetButton(Transform parent, string label, PaintMaterialPreset preset)
        {
            AddButton(parent, label, () =>
            {
                _pendingPaintPreset = preset;

                if (_paintFluidSystem != null && _paintFluidSystem.MaterialConfig != null)
                {
                    PaintMaterialConfig material = _paintFluidSystem.MaterialConfig;
                    material.autoApplyMaterialPreset = true;
                    material.ApplyMaterialPreset(preset);
                    _pendingPaintColor = material.baseColor;
                    LoadPaintValues();
                }
            });
        }

        private void AddSurfaceButton(Transform parent, string label, SurfaceType surfaceType)
        {
            AddButton(parent, label, () =>
            {
                _pendingSurfaceType = surfaceType;
                ApplySurfaceSettings();
            });
        }

        private void SetIntField(IntFieldControl control, int value)
        {
            if (control == null || control.InputField == null)
                return;

            control.InputField.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        }

        private void SetFloatField(FloatFieldControl control, float value)
        {
            if (control == null || control.InputField == null)
                return;

            control.InputField.SetTextWithoutNotify(FormatFloat(value));
        }

        private bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            ) || float.TryParse(text, out value);
        }

        private string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void SetSlider(SliderControl control, float value)
        {
            if (control == null || control.Slider == null)
                return;

            control.Slider.SetValueWithoutNotify(value);
            if (control.ValueText != null)
                control.ValueText.text = FormatValue(value, control.WholeNumbers, control.Suffix);
        }

        private void SetToggle(Toggle toggle, bool value)
        {
            if (toggle != null)
                toggle.SetIsOnWithoutNotify(value);
        }

        private string FormatValue(float value, bool wholeNumbers, string suffix)
        {
            string number = wholeNumbers ? Mathf.RoundToInt(value).ToString() : value.ToString("0.###");
            return string.IsNullOrEmpty(suffix) ? number : number + " " + suffix;
        }

        private void AddLayoutElement(GameObject target, float preferredHeight, float flexibleHeight = 0.0f)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            if (element == null)
                element = target.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.flexibleHeight = flexibleHeight;
        }

        private void ClearChildren(Transform target)
        {
            for (int i = target.childCount - 1; i >= 0; i--)
                Destroy(target.GetChild(i).gameObject);
        }

        private BucketHoleConfig GetPrimaryHole(BucketConfig config)
        {
            if (config == null)
                return null;

            if (config.holes == null || config.holes.Length == 0)
                config.holes = new[] { new BucketHoleConfig() };

            if (config.holes[0] == null)
                config.holes[0] = new BucketHoleConfig();

            return config.holes[0];
        }

        private bool HasAnyActiveHole(BucketConfig config)
{
    if (config == null || config.holes == null || config.holes.Length == 0)
        return false;

    for (int i = 0; i < config.holes.Length; i++)
    {
        BucketHoleConfig hole = config.holes[i];
        if (hole != null && hole.active)
            return true;
    }

    return false;
}

private void ApplyHoleSettingsToAllHoles(BucketConfig config)
{
    if (config == null)
        return;

    BucketHoleConfig primaryHole = GetPrimaryHole(config);
    if (primaryHole == null)
        return;

    float radius = Mathf.Max(0.001f, _pendingBucketHoleRadius);
    float flow = Mathf.Max(0.0f, _pendingBucketFlow);
    float exitVelocity = Mathf.Max(0.0f, _pendingBucketExitVelocity);

    if (config.holes == null || config.holes.Length == 0)
    {
        primaryHole.radiusMeters = radius;
        primaryHole.flowMultiplier = flow;
        primaryHole.exitVelocityBoostMetersPerSecond = exitVelocity;
        primaryHole.active = _pendingBucketHoleActive;
        return;
    }

    for (int i = 0; i < config.holes.Length; i++)
    {
        BucketHoleConfig hole = config.holes[i];
        if (hole == null)
            continue;

        hole.radiusMeters = radius;
        hole.flowMultiplier = flow;
        hole.exitVelocityBoostMetersPerSecond = exitVelocity;
        hole.active = _pendingBucketHoleActive;
    }
}

        private T GetPrivateField<T>(object target, string fieldName, T fallback)
        {
            if (target == null)
                return fallback;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return fallback;

            object value = field.GetValue(target);
            if (value is T typedValue)
                return typedValue;

            return fallback;
        }

        private void SetPrivateField<T>(object target, string fieldName, T value)
        {
            if (target == null)
                return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
                field.SetValue(target, value);
        }

        private void InvokePrivateMethod(object target, string methodName)
        {
            if (target == null)
                return;

            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null)
                method.Invoke(target, null);
        }

        private sealed class SliderControl
        {
            public readonly Slider Slider;
            public readonly Text ValueText;
            public readonly bool WholeNumbers;
            public readonly string Suffix;

            public SliderControl(Slider slider, Text valueText, bool wholeNumbers, string suffix)
            {
                Slider = slider;
                ValueText = valueText;
                WholeNumbers = wholeNumbers;
                Suffix = suffix;
            }
        }

        private sealed class IntFieldControl
        {
            public readonly InputField InputField;

            public IntFieldControl(InputField inputField)
            {
                InputField = inputField;
            }
        }

        private sealed class FloatFieldControl
        {
            public readonly InputField InputField;

            public FloatFieldControl(InputField inputField)
            {
                InputField = inputField;
            }
        }
    }
}
