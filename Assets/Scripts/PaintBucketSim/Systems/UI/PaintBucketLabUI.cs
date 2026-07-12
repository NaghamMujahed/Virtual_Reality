using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PaintBucketSim.Configs;
using PaintBucketSim.Core;
using PaintBucketSim.Runtime;
using PaintBucketSim.Systems.Bucket;
using PaintBucketSim.Systems.Coupling;
using PaintBucketSim.Systems.Diagnostics;
using PaintBucketSim.Systems.Fluid;
using PaintBucketSim.Systems.Rope;
using PaintBucketSim.Systems.Surface;
using PaintSim.Scripts.Core.Data;
using PaintSim.Scripts.Stages.Surface;
using PaintSim.Scripts.UnityBridge;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PaintBucketSim.Systems.UI
{
    [DefaultExecutionOrder(3200)]
    [DisallowMultipleComponent]
    public sealed class PaintBucketLabUI : MonoBehaviour
    {
        private enum LabTab
        {
            Run = 0,
            Board = 1,
            Fluid = 2,
            Material = 3,
            Rope = 4,
            Bucket = 5,
            View = 6,
            Stats = 7
        }

        private const float UiFontScale = 1.16f;
        private static readonly Color AppBg = new Color(0.035f, 0.040f, 0.045f, 0.0f);
        private static readonly Color TopBg = new Color(0.050f, 0.060f, 0.066f, 0.96f);
        private static readonly Color PanelBg = new Color(0.070f, 0.082f, 0.090f, 0.94f);
        private static readonly Color SectionBg = new Color(0.095f, 0.110f, 0.120f, 0.96f);
        private static readonly Color ControlBg = new Color(0.120f, 0.140f, 0.150f, 0.98f);
        private static readonly Color Accent = new Color(0.13f, 0.63f, 0.72f, 1.0f);
        private static readonly Color Accent2 = new Color(0.94f, 0.58f, 0.18f, 1.0f);
        private static readonly Color Danger = new Color(0.84f, 0.22f, 0.22f, 1.0f);
        private static readonly Color TextColor = new Color(0.92f, 0.95f, 0.96f, 1.0f);
        private static readonly Color MutedText = new Color(0.62f, 0.70f, 0.73f, 1.0f);
        private static readonly Color DisabledText = new Color(0.36f, 0.42f, 0.44f, 1.0f);

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _topBar;
        private RectTransform _nav;
        private RectTransform _panel;
        private RectTransform _content;
        private RectTransform _popupLayer;
        private ScrollRect _inspectorScroll;
        private Scrollbar _inspectorScrollbar;
        private GameObject _dropdownPopup;
        private Text _titleText;
        private Text _statusText;
        private Text _metricText;
        private Text _hintText;
        private readonly Dictionary<LabTab, Button> _tabButtons = new Dictionary<LabTab, Button>();
        private readonly List<Text> _dynamicTexts = new List<Text>();
        private readonly List<Action> _dynamicRefreshers = new List<Action>();

        private Font _font;
        private LabTab _activeTab = LabTab.Board;
        private bool _uiVisible = true;
        private bool _legacyDebugVisible;
        private bool _building;

        private SimulationManager _simulationManager;
        private LabBoardMotionController _boardController;
        private PaintFluidSystem _paintFluidSystem;
        private PaintFluidConfig _fluidConfig;
        private PaintMaterialConfig _materialConfig;
        private PaintSurface _paintSurface;
        private PaintSimulationHost _paintHost;
        private RopeSystem _ropeSystem;
        private RopeConfig _ropeConfig;
        private BucketSystem _bucketSystem;
        private BucketRenderer _bucketRenderer;
        private BucketConfig _bucketConfig;
        private RopeBucketCouplingSystem _couplingSystem;
        private RopeBucketCouplingConfig _couplingConfig;
        private GpuParticleIndirectRenderer _particleRenderer;
        private GpuParticleRenderConfig _renderConfig;
        private DebugOverlay _debugOverlay;
        private global::InteractiveCamera _interactiveCamera;

        private int _pendingParticleCount;
        private Slider _particleSlider;
        private Text _particleSliderText;
        private InputField _particleInput;
        private LabDropdown _materialPresetDropdown;
        private LabDropdown _renderModeDropdown;
        private LabDropdown _boardModeDropdown;
        private LabDropdown _colorAxisDropdown;
        private LabDropdown _compartmentColorDropdown;
        private LabDropdown _cameraSelectorDropdown;
        private LabDropdown _ropePivotDropdown;
        private LabDropdown _ropeDampingDropdown;
        private LabDropdown _bucketMotionDropdown;
        private LabDropdown _bucketHoleSelectorDropdown;
        private LabDropdown _bucketHoleShapeDropdown;
        private LabDropdown _surfaceTypeDropdown;
        private LabDropdown _colorMixingDropdown;
        private Toggle _boardMotionToggle;
        private Toggle _autoOrbitToggle;
        private Toggle _legacyDebugToggle;
        private Toggle _colorCompartmentsToggle;
        private Toggle _physicalDividersToggle;
        private Toggle _couplingToggle;
        private Toggle _bucketMeshToggle;
        private Toggle _breakByTensionToggle;
        private Toggle _breakByStrainToggle;
        private Toggle _primaryHoleToggle;
        private Toggle _holeAutoCenterToggle;
        private Toggle _surfaceEvolutionToggle;
        private Toggle _surfaceInertiaToggle;
        private Toggle _depositOnlyAirToggle;
        private Image _compartmentColorSwatch;
        private Vector2Int _lastLayoutSize;
        private int _selectedHoleIndex;
        private int _selectedCompartmentIndex;
        private float _userPanelWidth = -1.0f;
        private string _lastExportPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<PaintBucketLabUI>() != null)
                return;

            GameObject uiRoot = new GameObject("Paint Bucket Lab Control UI");
            uiRoot.hideFlags = HideFlags.DontSave;
            uiRoot.AddComponent<PaintBucketLabUI>();
        }

        private void Awake()
        {
            hideFlags = HideFlags.DontSave;
            ResolveReferences();
            EnsureEventSystem();
            DisableLegacyDebugOverlay();
            BuildUi();
        }

        private void Update()
        {
            ResolveReferences();
            HandleKeyboard();
            RefreshTopBar();
            RefreshDynamicContent();
            UpdateResponsiveLayout();
        }

        private void ResolveReferences()
        {
            if (_simulationManager == null)
                _simulationManager = FindAnyObjectByType<SimulationManager>();

            if (_boardController == null)
                _boardController = FindAnyObjectByType<LabBoardMotionController>();

            if (_paintFluidSystem == null)
                _paintFluidSystem = FindAnyObjectByType<PaintFluidSystem>();

            if (_paintFluidSystem != null)
            {
                _fluidConfig = _paintFluidSystem.FluidConfig;
                _materialConfig = _paintFluidSystem.MaterialConfig;
            }

            if (_paintSurface == null)
                _paintSurface = FindAnyObjectByType<PaintSurface>();

            if (_paintHost == null)
                _paintHost = FindAnyObjectByType<PaintSimulationHost>();

            if (_ropeSystem == null)
                _ropeSystem = FindAnyObjectByType<RopeSystem>();

            if (_ropeSystem != null)
                _ropeConfig = _ropeSystem.Config;

            if (_bucketSystem == null)
                _bucketSystem = FindAnyObjectByType<BucketSystem>();

            if (_bucketSystem != null)
                _bucketConfig = _bucketSystem.Config;

            if (_bucketRenderer == null)
                _bucketRenderer = FindAnyObjectByType<BucketRenderer>();

            if (_couplingSystem == null)
                _couplingSystem = FindAnyObjectByType<RopeBucketCouplingSystem>();

            if (_couplingSystem != null)
                _couplingConfig = _couplingSystem.Config;

            if (_particleRenderer == null)
                _particleRenderer = FindAnyObjectByType<GpuParticleIndirectRenderer>();

            if (_particleRenderer != null)
                _renderConfig = _particleRenderer.RenderConfig;

            if (_debugOverlay == null)
                _debugOverlay = FindAnyObjectByType<DebugOverlay>();

            if (_interactiveCamera == null || !_interactiveCamera.IsViewActive)
            {
                global::InteractiveCamera[] cameras =
                    FindObjectsByType<global::InteractiveCamera>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] != null && cameras[i].IsViewActive)
                    {
                        _interactiveCamera = cameras[i];
                        break;
                    }
                }

                if (_interactiveCamera == null && cameras.Length > 0)
                    _interactiveCamera = cameras[0];
            }
        }

        private void DisableLegacyDebugOverlay()
        {
            if (_debugOverlay == null)
                return;

            _legacyDebugVisible = false;
            _debugOverlay.enabled = false;
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _uiVisible = !_uiVisible;
                if (_canvas != null)
                    _canvas.enabled = _uiVisible;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                _legacyDebugVisible = !_legacyDebugVisible;
                if (_debugOverlay != null)
                    _debugOverlay.enabled = _legacyDebugVisible;
                if (_legacyDebugToggle != null)
                    _legacyDebugToggle.SetIsOnWithoutNotify(_legacyDebugVisible);
            }

            if (Input.GetMouseButtonDown(0))
            {
                // Popup dropdowns install their own transparent blocker. This
                // closes stale popups if focus changed through another route.
                if (_dropdownPopup != null &&
                    EventSystem.current != null &&
                    !EventSystem.current.IsPointerOverGameObject())
                {
                    HideDropdownPopup();
                }
            }

            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.H))
                ToggleAllHoles();
            if (Input.GetKeyDown(KeyCode.K))
                CycleCamera();

            if (Input.GetKeyDown(KeyCode.Alpha1))
                SelectTab(LabTab.Run);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SelectTab(LabTab.Board);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SelectTab(LabTab.Fluid);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                SelectTab(LabTab.Material);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                SelectTab(LabTab.Rope);
            if (Input.GetKeyDown(KeyCode.Alpha6))
                SelectTab(LabTab.Bucket);
            if (Input.GetKeyDown(KeyCode.Alpha7))
                SelectTab(LabTab.View);
            if (Input.GetKeyDown(KeyCode.Alpha8))
                SelectTab(LabTab.Stats);
        }

        private void BuildUi()
        {
            _building = true;
            _font = ResolveFont();

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;

            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            GameObject shade = CreateUiObject("ViewportShade", transform);
            RectTransform shadeRect = shade.GetComponent<RectTransform>();
            Stretch(shadeRect);
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = AppBg;
            shadeImage.raycastTarget = false;

            BuildTopBar();
            BuildNavigation();
            BuildInspectorPanel();
            BuildHintBar();
            BuildPopupLayer();

            SelectTab(LabTab.Board);
            UpdateResponsiveLayout();
            RefreshTopBar();
            _building = false;
        }

        private void BuildTopBar()
        {
            GameObject top = CreateUiObject("TopBar", transform);
            _topBar = top.GetComponent<RectTransform>();
            _topBar.anchorMin = new Vector2(0f, 1f);
            _topBar.anchorMax = new Vector2(1f, 1f);
            _topBar.pivot = new Vector2(0.5f, 1f);
            _topBar.sizeDelta = new Vector2(0f, 64f);
            _topBar.anchoredPosition = Vector2.zero;
            Image bg = top.AddComponent<Image>();
            bg.color = TopBg;

            HorizontalLayoutGroup layout = top.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 8, 8);
            layout.spacing = 18;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            GameObject titleBlock = CreateUiObject("TitleBlock", top.transform);
            VerticalLayoutGroup titleLayout = titleBlock.AddComponent<VerticalLayoutGroup>();
            titleLayout.spacing = 0;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = true;
            titleLayout.childForceExpandHeight = false;
            LayoutElement titleElement = titleBlock.AddComponent<LayoutElement>();
            titleElement.minWidth = 360f;
            titleElement.flexibleWidth = 1f;

            _titleText = CreateText("Paint Bucket Lab", titleBlock.transform, 22, FontStyle.Bold, TextColor);
            _titleText.alignment = TextAnchor.LowerLeft;
            _statusText = CreateText("Resolving systems", titleBlock.transform, 13, FontStyle.Bold, Accent);
            _statusText.alignment = TextAnchor.UpperLeft;

            _metricText = CreateText("Particles - | FPS - | Solver -", top.transform, 14, FontStyle.Bold, MutedText);
            _metricText.alignment = TextAnchor.MiddleRight;
            LayoutElement metricElement = _metricText.gameObject.AddComponent<LayoutElement>();
            metricElement.minWidth = 520f;
            metricElement.flexibleWidth = 1f;
        }

        private void BuildNavigation()
        {
            GameObject nav = CreateUiObject("Navigation", transform);
            _nav = nav.GetComponent<RectTransform>();
            Image bg = nav.AddComponent<Image>();
            bg.color = new Color(0.045f, 0.052f, 0.058f, 0.96f);

            VerticalLayoutGroup layout = nav.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 12, 12);
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateTabButton(LabTab.Run, "Run", "1");
            CreateTabButton(LabTab.Board, "Board", "2");
            CreateTabButton(LabTab.Fluid, "Fluid", "3");
            CreateTabButton(LabTab.Material, "Paint", "4");
            CreateTabButton(LabTab.Rope, "Rope", "5");
            CreateTabButton(LabTab.Bucket, "Bucket", "6");
            CreateTabButton(LabTab.View, "View", "7");
            CreateTabButton(LabTab.Stats, "Stats", "8");
        }

        private void BuildInspectorPanel()
        {
            GameObject panelObject = CreateUiObject("InspectorPanel", transform);
            _panel = panelObject.GetComponent<RectTransform>();
            Image image = panelObject.AddComponent<Image>();
            image.color = PanelBg;

            _inspectorScroll = panelObject.AddComponent<ScrollRect>();
            _inspectorScroll.horizontal = false;
            _inspectorScroll.vertical = true;
            _inspectorScroll.movementType = ScrollRect.MovementType.Clamped;
            _inspectorScroll.scrollSensitivity = 32f;

            GameObject viewportObject = CreateUiObject("Viewport", panelObject.transform);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-18f, 0f);
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = false;
            viewportObject.AddComponent<RectMask2D>();
            _inspectorScroll.viewport = viewport;

            GameObject contentObject = CreateUiObject("Content", viewportObject.transform);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 720f);
            _inspectorScroll.content = _content;

            VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject scrollbarObject = CreateUiObject("VerticalScrollbar", panelObject.transform);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.anchoredPosition = new Vector2(-4f, 0f);
            scrollbarRect.sizeDelta = new Vector2(12f, -8f);
            Image scrollbarTrack = scrollbarObject.AddComponent<Image>();
            scrollbarTrack.color = new Color(0.025f, 0.030f, 0.034f, 0.88f);

            _inspectorScrollbar = scrollbarObject.AddComponent<Scrollbar>();
            _inspectorScrollbar.direction = Scrollbar.Direction.BottomToTop;
            _inspectorScrollbar.numberOfSteps = 0;

            GameObject slidingArea = CreateUiObject("SlidingArea", scrollbarObject.transform);
            RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
            Stretch(slidingRect);
            slidingRect.offsetMin = new Vector2(2f, 2f);
            slidingRect.offsetMax = new Vector2(-2f, -2f);

            GameObject handleObject = CreateUiObject("Handle", slidingArea.transform);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            Stretch(handleRect);
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = Accent;
            _inspectorScrollbar.handleRect = handleRect;
            _inspectorScrollbar.targetGraphic = handleImage;

            _inspectorScroll.verticalScrollbar = _inspectorScrollbar;
            _inspectorScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            _inspectorScroll.verticalScrollbarSpacing = 4f;

            GameObject resizeObject = CreateUiObject("PanelResizeHandle", panelObject.transform);
            RectTransform resizeRect = resizeObject.GetComponent<RectTransform>();
            resizeRect.anchorMin = new Vector2(0f, 0f);
            resizeRect.anchorMax = new Vector2(0f, 1f);
            resizeRect.pivot = new Vector2(0.5f, 0.5f);
            resizeRect.anchoredPosition = new Vector2(1f, 0f);
            resizeRect.sizeDelta = new Vector2(14f, 0f);
            Image resizeImage = resizeObject.AddComponent<Image>();
            resizeImage.color = new Color(Accent.r, Accent.g, Accent.b, 0.08f);
            LabPanelResizeHandle resizeHandle =
                resizeObject.AddComponent<LabPanelResizeHandle>();
            resizeHandle.Initialize(this, resizeImage, Accent);
        }

        private void BuildHintBar()
        {
            GameObject hints = CreateUiObject("HintBar", transform);
            RectTransform rect = hints.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 38f);
            Image bg = hints.AddComponent<Image>();
            bg.color = new Color(0.045f, 0.050f, 0.054f, 0.90f);

            _hintText = CreateText(
                "Space/P Pause  |  O Step  |  R Reset  |  M Board  |  B/N Mode  |  C Clear  |  H Holes  |  K Camera  |  F Focus  |  V Orbit  |  F1 Debug  |  F10 UI  |  WASD Pan",
                hints.transform,
                12,
                FontStyle.Bold,
                MutedText);
            Stretch(_hintText.rectTransform);
            _hintText.alignment = TextAnchor.MiddleCenter;
        }

        private void BuildPopupLayer()
        {
            GameObject popupLayer = CreateUiObject("PopupLayer", transform);
            _popupLayer = popupLayer.GetComponent<RectTransform>();
            Stretch(_popupLayer);
            popupLayer.transform.SetAsLastSibling();
        }

        private void SelectTab(LabTab tab)
        {
            if (_activeTab == tab && !_building)
                return;

            _activeTab = tab;
            UpdateTabButtons();
            RebuildCurrentTab();
            ResetInspectorScroll();
        }

        private void UpdateTabButtons()
        {
            foreach (KeyValuePair<LabTab, Button> pair in _tabButtons)
            {
                Image image = pair.Value.GetComponent<Image>();
                Text text = pair.Value.GetComponentInChildren<Text>();
                bool selected = pair.Key == _activeTab;
                if (image != null)
                    image.color = selected ? Accent : ControlBg;
                if (text != null)
                    text.color = selected ? Color.white : TextColor;
            }
        }

        private void RebuildCurrentTab()
        {
            if (_content == null)
                return;

            _dynamicTexts.Clear();
            _dynamicRefreshers.Clear();
            _boardModeDropdown = null;
            _boardMotionToggle = null;
            _autoOrbitToggle = null;
            _legacyDebugToggle = null;
            _particleSlider = null;
            _particleSliderText = null;
            _particleInput = null;
            _materialPresetDropdown = null;
            _renderModeDropdown = null;
            _colorAxisDropdown = null;
            _compartmentColorDropdown = null;
            _cameraSelectorDropdown = null;
            _ropePivotDropdown = null;
            _ropeDampingDropdown = null;
            _bucketMotionDropdown = null;
            _bucketHoleSelectorDropdown = null;
            _bucketHoleShapeDropdown = null;
            _colorCompartmentsToggle = null;
            _physicalDividersToggle = null;
            _couplingToggle = null;
            _bucketMeshToggle = null;
            _breakByTensionToggle = null;
            _primaryHoleToggle = null;
            _holeAutoCenterToggle = null;
            _surfaceTypeDropdown = null;
            _colorMixingDropdown = null;
            _surfaceEvolutionToggle = null;
            _surfaceInertiaToggle = null;
            _depositOnlyAirToggle = null;
            _compartmentColorSwatch = null;

            HideDropdownPopup();

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            switch (_activeTab)
            {
                case LabTab.Board:
                    BuildBoardTab();
                    break;
                case LabTab.Fluid:
                    BuildFluidTab();
                    break;
                case LabTab.Material:
                    BuildMaterialTab();
                    break;
                case LabTab.Rope:
                    BuildRopeTab();
                    break;
                case LabTab.Bucket:
                    BuildBucketTab();
                    break;
                case LabTab.View:
                    BuildViewTab();
                    break;
                case LabTab.Stats:
                    BuildStatsTab();
                    break;
                case LabTab.Run:
                default:
                    BuildRunTab();
                    break;
            }

            RefreshDynamicContent();
            RefreshPanelLayout();
        }

        private void BuildRunTab()
        {
            AddTitle("Run Console", "Control the simulation loop and reset the complete lab state.");
            AddActionGrid(
                ("Pause / Resume", TogglePause, Accent),
                ("Single Step", StepOnce, ControlBg),
                ("Reset Lab", ResetAll, Danger),
                ("Clear Board Paint", ClearPaint, Accent2),
                ("Focus Camera", FocusCamera, ControlBg),
                ("Toggle Board Motion", ToggleBoardMotion, ControlBg),
                ("Export Board", ExportBoard, Accent));

            GameObject section = AddSection("Live State");
            Text state = AddReadonlyLine(section.transform, "Waiting for simulation data");
            _dynamicRefreshers.Add(() =>
            {
                if (state == null)
                    return;

                bool initialized = _simulationManager != null && _simulationManager.IsInitialized;
                bool paused = initialized &&
                              _simulationManager.TimeController != null &&
                              _simulationManager.TimeController.IsPaused;
                int particles = _paintFluidSystem != null && _paintFluidSystem.IsInitialized
                    ? _paintFluidSystem.ParticleCount
                    : 0;
                string solver = _paintFluidSystem != null && _paintFluidSystem.SolverStats.gpuDiagnosticsReady
                    ? "GPU MPM"
                    : (_paintFluidSystem != null ? _paintFluidSystem.SolverStats.solverType.ToString() : "-");

                state.text =
                    $"State: {(initialized ? (paused ? "Paused" : "Running") : "Not ready")}\n" +
                    $"Solver: {solver}\n" +
                    $"Particles: {particles:n0}\n" +
                    $"Board: {(_boardController != null ? _boardController.MotionModeLabel : "-")}";
            });
        }

        private void BuildBoardTab()
        {
            AddTitle("Moving Board", "Drive the canvas motion while liquid falls onto it.");

            GameObject section = AddSection("Motion");
            _boardMotionToggle = AddToggle(section.transform, "Enable board motion", false, value =>
            {
                if (_boardController != null)
                    _boardController.SetMotionEnabled(value);
            });

            _boardModeDropdown = AddDropdown(
                section.transform,
                "Motion profile",
                new[]
                {
                    "Static",
                    "Gentle Tilt",
                    "Cross-Axis Tilt",
                    "Tilted Orbit",
                    "Figure Eight",
                    "Rotary Sweep"
                },
                index =>
                {
                    if (_boardController != null)
                        _boardController.SetMotionMode(index);
                });

            AddSlider(section.transform, "Speed", 0.05f, 4.0f, 1.0f, value =>
            {
                if (_boardController != null)
                    _boardController.SetSpeedMultiplier(value);
            }, () => _boardController != null ? _boardController.SpeedMultiplier : 0.0f, "0.00x");

            AddSlider(section.transform, "Tilt amplitude", 0.0f, 35.0f, 14.0f, value =>
            {
                if (_boardController != null)
                    _boardController.SetTiltAmplitude(value);
            }, () => _boardController != null ? _boardController.TiltAmplitudeDegrees : 0.0f, "0 deg");

            AddSlider(section.transform, "Spin", 0.0f, 120.0f, 18.0f, value =>
            {
                if (_boardController != null)
                    _boardController.SetSpinDegreesPerSecond(value);
            }, () => _boardController != null ? _boardController.SpinDegreesPerSecond : 0.0f, "0 deg/s");

            AddSlider(section.transform, "Travel", 0.0f, 0.4f, 0.08f, value =>
            {
                if (_boardController != null)
                    _boardController.SetTravelAmplitude(value);
            }, () => _boardController != null ? _boardController.TravelAmplitudeMeters : 0.0f, "0.00 m");

            AddActionGrid(
                ("Reset Board Pose", () => _boardController?.ResetBoardPose(false), ControlBg),
                ("Clear Paint Film", ClearPaint, Accent2),
                ("Export Board", ExportBoard, Accent));

            GameObject surface = AddSection("Paint Surface");
            _surfaceTypeDropdown = AddDropdown(
                surface.transform,
                "Surface type",
                Enum.GetNames(typeof(SurfaceType)),
                index =>
                {
                    if (_paintSurface != null)
                        _paintSurface.SetSurfaceType((SurfaceType)index);
                });

            _surfaceEvolutionToggle = AddToggle(
                surface.transform,
                "Evolve paint film",
                _paintSurface != null && _paintSurface.EvolutionEnabled,
                value =>
                {
                    if (_paintSurface != null)
                        _paintSurface.EvolutionEnabled = value;
                });

            _surfaceInertiaToggle = AddToggle(
                surface.transform,
                "Moving board inertia",
                _paintSurface != null && _paintSurface.IncludeSurfaceInertiaInFlow,
                value =>
                {
                    if (_paintSurface != null)
                        _paintSurface.IncludeSurfaceInertiaInFlow = value;
                });

            _depositOnlyAirToggle = AddToggle(
                surface.transform,
                "Deposit airborne / jet only",
                _paintHost != null && _paintHost.DepositOnlyAirDomainParticles,
                value =>
                {
                    if (_paintHost != null)
                        _paintHost.DepositOnlyAirDomainParticles = value;
                });

            AddSlider(surface.transform, "Deposit interval", 1f, 8f, 1f, value =>
            {
                if (_paintHost != null)
                    _paintHost.DepositEveryNFrames = Mathf.RoundToInt(value);
            }, () => _paintHost != null ? _paintHost.DepositEveryNFrames : 1.0f, "0");

            AddSlider(surface.transform, "Render interval", 1f, 8f, 1f, value =>
            {
                if (_paintSurface != null)
                    _paintSurface.RenderEveryNFrames = Mathf.RoundToInt(value);
            }, () => _paintSurface != null ? _paintSurface.RenderEveryNFrames : 1.0f, "0");

            AddSlider(surface.transform, "Evolve interval", 1f, 12f, 2f, value =>
            {
                if (_paintSurface != null)
                    _paintSurface.EvolveEveryNFrames = Mathf.RoundToInt(value);
            }, () => _paintSurface != null ? _paintSurface.EvolveEveryNFrames : 1.0f, "0");

            GameObject mixing = AddSection("Paint Mixing");
            _colorMixingDropdown = AddDropdown(
                mixing.transform,
                "Mixing model",
                Enum.GetNames(typeof(PaintColorMixingMode)),
                index => ApplySurfaceColorMixing((PaintColorMixingMode)index));

            AddSlider(mixing.transform, "Pigment mix", 0.0f, 1.0f, 1.0f, value =>
            {
                ApplySurfaceColorMixing(mix: value);
            }, () => _paintHost != null ? _paintHost.PigmentMixStrength : (_paintSurface != null ? _paintSurface.PigmentMixStrength : 0.0f), "0%");

            AddSlider(mixing.transform, "Min reflectance", 0.001f, 0.35f, 0.035f, value =>
            {
                ApplySurfaceColorMixing(minReflectance: value);
            }, () => _paintHost != null ? _paintHost.PigmentMinReflectance : (_paintSurface != null ? _paintSurface.PigmentMinReflectance : 0.0f), "0%");

            AddSlider(mixing.transform, "Max K/S", 1.0f, 64.0f, 18.0f, value =>
            {
                ApplySurfaceColorMixing(maxKs: value);
            }, () => _paintHost != null ? _paintHost.PigmentMaxKs : (_paintSurface != null ? _paintSurface.PigmentMaxKs : 0.0f), "0.00");

            _dynamicRefreshers.Add(() =>
            {
                if (_boardMotionToggle != null && _boardController != null)
                    _boardMotionToggle.SetIsOnWithoutNotify(_boardController.MotionEnabled);
                if (_boardModeDropdown != null && _boardController != null)
                    _boardModeDropdown.SetValueWithoutNotify(_boardController.MotionModeIndex);
                RefreshBoardSurfaceControls();
            });
        }

        private void RefreshBoardSurfaceControls()
        {
            if (_surfaceTypeDropdown != null && _paintSurface != null)
                _surfaceTypeDropdown.SetValueWithoutNotify((int)_paintSurface.SurfaceType);

            if (_colorMixingDropdown != null)
            {
                PaintColorMixingMode mode = _paintHost != null
                    ? _paintHost.ColorMixingMode
                    : (_paintSurface != null
                        ? _paintSurface.ColorMixingMode
                        : PaintColorMixingMode.KubelkaMunkApprox);
                _colorMixingDropdown.SetValueWithoutNotify((int)mode);
            }

            if (_surfaceEvolutionToggle != null && _paintSurface != null)
                _surfaceEvolutionToggle.SetIsOnWithoutNotify(_paintSurface.EvolutionEnabled);
            if (_surfaceInertiaToggle != null && _paintSurface != null)
                _surfaceInertiaToggle.SetIsOnWithoutNotify(_paintSurface.IncludeSurfaceInertiaInFlow);
            if (_depositOnlyAirToggle != null && _paintHost != null)
                _depositOnlyAirToggle.SetIsOnWithoutNotify(_paintHost.DepositOnlyAirDomainParticles);
        }

        private void BuildFluidTab()
        {
            AddTitle("Fluid Setup", "Change particle budget and fill state. Applying particle count resets the fluid.");

            GameObject budget = AddSection("Particle Budget");
            int current = _fluidConfig != null ? _fluidConfig.targetParticleCount : 5000;
            _pendingParticleCount = Mathf.Clamp(current, 1000, 500000);

            _particleInput = AddInputField(
                budget.transform,
                "Target particles",
                _pendingParticleCount.ToString(),
                value =>
                {
                    if (int.TryParse(value, out int parsed))
                    {
                        _pendingParticleCount = Mathf.Clamp(parsed, 1000, 500000);
                        if (_particleSlider != null)
                            _particleSlider.SetValueWithoutNotify(_pendingParticleCount);
                    }
                });

            _particleSlider = AddSlider(
                budget.transform,
                "Budget",
                1000f,
                500000f,
                _pendingParticleCount,
                value =>
                {
                    _pendingParticleCount = Mathf.RoundToInt(value / 1000f) * 1000;
                    if (_particleInput != null)
                        _particleInput.SetTextWithoutNotify(_pendingParticleCount.ToString());
                },
                () => _pendingParticleCount,
                "0");
            _particleSlider.wholeNumbers = true;
            _particleSliderText = FindValueText(_particleSlider);

            AddActionGrid(
                ("Apply + Reset Fluid", ApplyParticleBudget, Accent),
                ("Reset Lab", ResetAll, Danger));

            GameObject fill = AddSection("Fill And Render Budget");
            AddSlider(fill.transform, "Fill fraction", 0.05f, 0.95f, 0.5f, value =>
            {
                if (_fluidConfig != null)
                    _fluidConfig.fillFraction01 = value;
            }, () => _fluidConfig != null ? _fluidConfig.fillFraction01 : 0.0f, "0%");

            AddSlider(fill.transform, "Rendered particles", 1000f, 500000f, 50000f, value =>
            {
                if (_fluidConfig != null)
                    _fluidConfig.maxRenderedParticles = Mathf.RoundToInt(value / 1000f) * 1000;
            }, () => _fluidConfig != null ? _fluidConfig.maxRenderedParticles : 0.0f, "0");

            AddSlider(fill.transform, "Render stride", 1f, 12f, 1f, value =>
            {
                if (_fluidConfig != null)
                    _fluidConfig.renderStride = Mathf.Max(1, Mathf.RoundToInt(value));
            }, () => _fluidConfig != null ? _fluidConfig.renderStride : 1.0f, "0");

            GameObject colors = AddSection("Multi-Color Compartments");
            _colorCompartmentsToggle = AddToggle(
                colors.transform,
                "Enable color compartments",
                _fluidConfig != null && _fluidConfig.enableColorCompartments,
                value =>
                {
                    if (_fluidConfig != null)
                        _fluidConfig.enableColorCompartments = value;
                });

            LabDropdown compartmentCountDropdown = AddDropdown(
                colors.transform,
                "Compartments",
                new[] { "1", "2", "3", "4", "5", "6", "7", "8" },
                index =>
                {
                    if (_fluidConfig == null)
                        return;
                    _fluidConfig.colorCompartmentCount = index + 1;
                    EnsureCompartmentColorCapacity();
                    _selectedCompartmentIndex = Mathf.Min(
                        _selectedCompartmentIndex,
                        index);
                    RebuildCurrentTab();
                });
            compartmentCountDropdown.SetValueWithoutNotify(
                GetColorCompartmentCount() - 1);

            _colorAxisDropdown = AddDropdown(
                colors.transform,
                "Divider axis",
                Enum.GetNames(typeof(FluidColorCompartmentAxis)),
                index =>
                {
                    if (_fluidConfig != null)
                        _fluidConfig.colorCompartmentAxis = (FluidColorCompartmentAxis)index;
                });

            EnsureCompartmentColorCapacity();
            _compartmentColorDropdown = AddDropdown(
                colors.transform,
                "Edit color",
                BuildCompartmentColorOptions(),
                index => _selectedCompartmentIndex = index);
            _selectedCompartmentIndex = Mathf.Clamp(
                _selectedCompartmentIndex,
                0,
                Mathf.Max(0, GetColorCompartmentCount() - 1));
            _compartmentColorDropdown.SetValueWithoutNotify(
                _selectedCompartmentIndex);

            AddCompartmentColorPreview(colors.transform);
            AddColorChannelSlider(colors.transform, "Red", 0);
            AddColorChannelSlider(colors.transform, "Green", 1);
            AddColorChannelSlider(colors.transform, "Blue", 2);

            AddSlider(colors.transform, "Divider gap", 0.0f, 0.35f, 0.04f, value =>
            {
                if (_fluidConfig != null)
                    _fluidConfig.colorDividerGapFraction = Mathf.Clamp(value, 0.0f, 0.35f);
            }, () => _fluidConfig != null ? _fluidConfig.colorDividerGapFraction : 0.0f, "0%");

            _physicalDividersToggle = AddToggle(
                colors.transform,
                "Physical divider collision",
                _fluidConfig != null && _fluidConfig.enablePhysicalColorDividers,
                value =>
                {
                    if (_fluidConfig != null)
                        _fluidConfig.enablePhysicalColorDividers = value;
                });

            AddSlider(colors.transform, "Divider thickness", 0.0f, 0.05f, 0.012f, value =>
            {
                if (_fluidConfig != null)
                    _fluidConfig.colorDividerThicknessMeters = Mathf.Max(value, 0.0f);
            }, () => _fluidConfig != null ? _fluidConfig.colorDividerThicknessMeters : 0.0f, "0.000 m");

            AddActionGrid(
                ("Blue / Red", () => SetCompartmentColors(new Color(0.1f, 0.35f, 1f, 1f), new Color(1f, 0.12f, 0.08f, 1f)), Accent),
                ("CMY Lab", () => SetCompartmentColors(Color.cyan, Color.magenta, Color.yellow), ControlBg),
                ("Apply Colors + Reset", ApplyParticleBudget, Accent2));

            _dynamicRefreshers.Add(() =>
            {
                if (_particleSliderText != null)
                    _particleSliderText.text = _pendingParticleCount.ToString("n0");
                if (_colorCompartmentsToggle != null && _fluidConfig != null)
                    _colorCompartmentsToggle.SetIsOnWithoutNotify(_fluidConfig.enableColorCompartments);
                if (_physicalDividersToggle != null && _fluidConfig != null)
                    _physicalDividersToggle.SetIsOnWithoutNotify(_fluidConfig.enablePhysicalColorDividers);
                if (_colorAxisDropdown != null && _fluidConfig != null)
                    _colorAxisDropdown.SetValueWithoutNotify((int)_fluidConfig.colorCompartmentAxis);
                if (_compartmentColorSwatch != null)
                    _compartmentColorSwatch.color = GetSelectedCompartmentColor();
            });
        }

        private void BuildRopeTab()
        {
            AddTitle("Rope And Coupling", "Tune rope material, pivot motion, grab behavior, and the bucket attachment constraint.");

            GameObject geometry = AddSection("Rope Geometry");
            AddSlider(geometry.transform, "Length", 0.4f, 5.0f, 2.5f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.lengthMeters = Mathf.Max(value, 0.05f);
            }, () => _ropeConfig != null ? _ropeConfig.lengthMeters : 0.0f, "0.00 m");

            AddSlider(geometry.transform, "Segments", 4f, 96f, 24f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.segmentCount = Mathf.Clamp(Mathf.RoundToInt(value), 2, 128);
            }, () => _ropeConfig != null ? _ropeConfig.segmentCount : 0.0f, "0");

            AddSlider(geometry.transform, "Rope mass", 0.02f, 2.0f, 0.12f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.ropeMassKg = Mathf.Max(value, 0.0001f);
            }, () => _ropeConfig != null ? _ropeConfig.ropeMassKg : 0.0f, "0.00 kg");

            AddSlider(geometry.transform, "Visual radius", 0.002f, 0.05f, 0.012f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.visualRadiusMeters = Mathf.Max(value, 0.001f);
            }, () => _ropeConfig != null ? _ropeConfig.visualRadiusMeters : 0.0f, "0.000 m");

            GameObject solver = AddSection("Rope Solver");
            AddSlider(solver.transform, "XPBD iterations", 1f, 48f, 12f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.solverIterations = Mathf.Clamp(Mathf.RoundToInt(value), 1, 64);
            }, () => _ropeConfig != null ? _ropeConfig.solverIterations : 0.0f, "0");

            AddSlider(solver.transform, "Damping", 0.0f, 2.0f, 0.08f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.segmentDampingRatio = Mathf.Clamp(value, 0.0f, 2.0f);
            }, () => _ropeConfig != null ? _ropeConfig.segmentDampingRatio : 0.0f, "0.00");

            _ropeDampingDropdown = AddDropdown(
                solver.transform,
                "Damping mode",
                Enum.GetNames(typeof(RopeDampingMode)),
                index =>
                {
                    if (_ropeConfig != null)
                        _ropeConfig.dampingMode = (RopeDampingMode)index;
                });

            GameObject pivot = AddSection("Pivot Motion");
            _ropePivotDropdown = AddDropdown(
                pivot.transform,
                "Pivot mode",
                Enum.GetNames(typeof(RopePivotMotionMode)),
                index =>
                {
                    if (_ropeConfig != null)
                        _ropeConfig.pivotMotionMode = (RopePivotMotionMode)index;
                });

            AddSlider(pivot.transform, "Pivot frequency", 0.0f, 3.0f, 0.5f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.pivotMotionFrequencyHz = Mathf.Max(value, 0.0f);
            }, () => _ropeConfig != null ? _ropeConfig.pivotMotionFrequencyHz : 0.0f, "0.00 Hz");

            AddSlider(pivot.transform, "Pivot X amplitude", 0.0f, 0.35f, 0.05f, value =>
            {
                if (_ropeConfig != null)
                    _ropeConfig.pivotMotionAmplitude.x = value;
            }, () => _ropeConfig != null ? _ropeConfig.pivotMotionAmplitude.x : 0.0f, "0.00 m");

            GameObject coupling = AddSection("Bucket Coupling");
            _couplingToggle = AddToggle(
                coupling.transform,
                "Enable rope/bucket coupling",
                _couplingConfig != null && _couplingConfig.enableCoupling,
                value =>
                {
                    if (_couplingConfig != null)
                        _couplingConfig.enableCoupling = value;
                });

            GameObject breakByTension = AddSection("Break Rope By Tension");
            _breakByTensionToggle = AddToggle(
                breakByTension.transform,
                "Enable Break By Tension",
                _ropeConfig != null && _ropeConfig.enableBreakByTension,
                value =>
                {
                    if (_ropeConfig != null)
                        _ropeConfig.enableBreakByTension = value;
                });

            _breakByStrainToggle = AddToggle(
                breakByTension.transform,
                "Enable Break By Strain",
                _ropeConfig != null && _ropeConfig.enableBreakByStrain,
                value =>
                {
                    if (_ropeConfig != null)
                        _ropeConfig.enableBreakByStrain = value;
                });

            AddSlider(coupling.transform, "Coupling iterations", 1f, 16f, 4f, value =>
            {
                if (_couplingConfig != null)
                    _couplingConfig.solverIterations = Mathf.Clamp(Mathf.RoundToInt(value), 1, 16);
            }, () => _couplingConfig != null ? _couplingConfig.solverIterations : 0.0f, "0");

            AddSlider(coupling.transform, "Attachment damping", 0.0f, 1.0f, 0.9f, value =>
            {
                if (_couplingConfig != null)
                    _couplingConfig.attachmentVelocityDamping = Mathf.Clamp01(value);
            }, () => _couplingConfig != null ? _couplingConfig.attachmentVelocityDamping : 0.0f, "0%");

            AddActionGrid(("Apply + Reset Rope", ResetAll, Accent));

            _dynamicRefreshers.Add(() =>
            {
                if (_ropeDampingDropdown != null && _ropeConfig != null)
                    _ropeDampingDropdown.SetValueWithoutNotify((int)_ropeConfig.dampingMode);
                if (_ropePivotDropdown != null && _ropeConfig != null)
                    _ropePivotDropdown.SetValueWithoutNotify((int)_ropeConfig.pivotMotionMode);
                if (_couplingToggle != null && _couplingConfig != null)
                    _couplingToggle.SetIsOnWithoutNotify(_couplingConfig.enableCoupling);
                if (_breakByTensionToggle != null && _ropeConfig != null)
                    _breakByTensionToggle.SetIsOnWithoutNotify(_ropeConfig.enableBreakByTension);
                if (_breakByStrainToggle != null && _ropeConfig != null)
                    _breakByStrainToggle.SetIsOnWithoutNotify(_ropeConfig.enableBreakByStrain);
            });
        }

        private void BuildBucketTab()
        {
            AddTitle("Bucket And Holes", "Tune the bucket body and manage every outlet used by the fluid solver.");

            GameObject body = AddSection("Bucket Body");
            _bucketMeshToggle = AddToggle(
                body.transform,
                "Show bucket mesh",
                _bucketRenderer == null || _bucketRenderer.BucketMeshVisible,
                value => _bucketRenderer?.SetBucketMeshVisible(value));

            _bucketMotionDropdown = AddDropdown(
                body.transform,
                "Motion mode",
                Enum.GetNames(typeof(BucketMotionMode)),
                index =>
                {
                    if (_bucketConfig != null)
                        _bucketConfig.motionMode = (BucketMotionMode)index;
                });

            AddSlider(body.transform, "Mass", 0.05f, 5.0f, 0.7f, value =>
            {
                if (_bucketConfig != null)
                    _bucketConfig.massKg = Mathf.Max(value, 0.01f);
            }, () => _bucketConfig != null ? _bucketConfig.massKg : 0.0f, "0.00 kg");

            AddSlider(body.transform, "Height", 0.2f, 1.2f, 0.55f, value =>
            {
                if (_bucketConfig != null)
                    _bucketConfig.heightMeters = Mathf.Max(value, 0.05f);
            }, () => _bucketConfig != null ? _bucketConfig.heightMeters : 0.0f, "0.00 m");

            AddSlider(body.transform, "Top radius", 0.06f, 0.45f, 0.18f, value =>
            {
                if (_bucketConfig != null)
                    _bucketConfig.topRadiusMeters = Mathf.Max(value, 0.02f);
            }, () => _bucketConfig != null ? _bucketConfig.topRadiusMeters : 0.0f, "0.00 m");

            AddSlider(body.transform, "Fluid load scale", 0.0f, 2.0f, 1.0f, value =>
            {
                if (_bucketConfig != null)
                    _bucketConfig.containedFluidMassScale = Mathf.Clamp(value, 0.0f, 2.0f);
            }, () => _bucketConfig != null ? _bucketConfig.containedFluidMassScale : 0.0f, "0.00x");

            ClampSelectedHoleIndex();
            GameObject hole = AddSection("Outlet Holes");
            _bucketHoleSelectorDropdown = AddDropdown(
                hole.transform,
                "Selected hole",
                BuildHoleOptions(),
                SelectBucketHole);
            _bucketHoleSelectorDropdown.SetValueWithoutNotify(_selectedHoleIndex);

            GameObject holeActions = CreateRow(hole.transform, 40f);
            CreateButton(
                holeActions.transform,
                "Add",
                AddBucketHole,
                Accent,
                38f,
                12);
            CreateButton(
                holeActions.transform,
                "Duplicate",
                DuplicateSelectedHole,
                ControlBg,
                38f,
                12);
            CreateButton(
                holeActions.transform,
                "Remove",
                RemoveSelectedHole,
                Danger,
                38f,
                12);

            Text holeSummary = AddReadonlyLine(hole.transform, "No holes configured");
            LayoutElement holeSummaryLayout = holeSummary.GetComponent<LayoutElement>();
            if (holeSummaryLayout != null)
                holeSummaryLayout.preferredHeight = 26f;

            _primaryHoleToggle = AddToggle(
                hole.transform,
                "Hole active",
                GetSelectedHole() != null && GetSelectedHole().active,
                value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.active = value;
                        NotifyHoleConfigurationChanged();
                        RebuildCurrentTab();
                    }
                });

            _holeAutoCenterToggle = AddToggle(
                hole.transform,
                "Auto bottom center",
                GetSelectedHole() != null && GetSelectedHole().autoPlaceAtBottomCenter,
                value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h == null)
                        return;

                    bool wasAutomatic = h.autoPlaceAtBottomCenter;
                    h.autoPlaceAtBottomCenter = value;
                    if (wasAutomatic && !value && _bucketConfig != null)
                    {
                        h.localCenter = new Vector3(
                            0f,
                            -_bucketConfig.heightMeters * 0.5f,
                            0f);
                    }

                    NotifyHoleConfigurationChanged();
                    RebuildCurrentTab();
                });

            _bucketHoleShapeDropdown = AddDropdown(
                hole.transform,
                "Shape",
                Enum.GetNames(typeof(BucketHoleShape)),
                index =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.shape = (BucketHoleShape)index;
                        NotifyHoleConfigurationChanged();
                        RebuildCurrentTab();
                    }
                });

            AddSlider(hole.transform, "Radius", 0.005f, 0.12f, 0.035f, value =>
            {
                BucketHoleConfig h = GetSelectedHole();
                if (h != null)
                {
                    h.radiusMeters = Mathf.Max(value, 0.0005f);
                    NotifyHoleConfigurationChanged();
                }
            }, () => GetSelectedHole() != null ? GetSelectedHole().radiusMeters : 0.0f, "0.000 m");

            BucketHoleConfig selectedHole = GetSelectedHole();
            if (selectedHole != null &&
                selectedHole.shape != BucketHoleShape.Circular &&
                selectedHole.shape != BucketHoleShape.Square)
            {
                AddSlider(hole.transform, "Opening size X", 0.002f, 0.24f, 0.07f, value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.sizeMeters.x = Mathf.Max(value, 0.001f);
                        NotifyHoleConfigurationChanged();
                    }
                }, () => GetSelectedHole() != null ? GetSelectedHole().sizeMeters.x : 0.0f, "0.000 m");

                AddSlider(hole.transform, "Opening size Y", 0.002f, 0.24f, 0.035f, value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.sizeMeters.y = Mathf.Max(value, 0.001f);
                        NotifyHoleConfigurationChanged();
                    }
                }, () => GetSelectedHole() != null ? GetSelectedHole().sizeMeters.y : 0.0f, "0.000 m");
            }

            if (selectedHole != null && !selectedHole.autoPlaceAtBottomCenter)
            {
                float radialLimit = _bucketConfig != null
                    ? Mathf.Max(_bucketConfig.GetRepresentativeRadius(), 0.05f)
                    : 0.25f;
                float verticalLimit = _bucketConfig != null
                    ? Mathf.Max(_bucketConfig.heightMeters * 0.65f, 0.1f)
                    : 0.4f;

                AddSlider(hole.transform, "Local position X", -radialLimit, radialLimit, 0f, value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.localCenter.x = value;
                        NotifyHoleConfigurationChanged();
                    }
                }, () => GetSelectedHole() != null ? GetSelectedHole().localCenter.x : 0.0f, "0.000 m");

                AddSlider(hole.transform, "Local position Y", -verticalLimit, verticalLimit, 0f, value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.localCenter.y = value;
                        NotifyHoleConfigurationChanged();
                    }
                }, () => GetSelectedHole() != null ? GetSelectedHole().localCenter.y : 0.0f, "0.000 m");

                AddSlider(hole.transform, "Local position Z", -radialLimit, radialLimit, 0f, value =>
                {
                    BucketHoleConfig h = GetSelectedHole();
                    if (h != null)
                    {
                        h.localCenter.z = value;
                        NotifyHoleConfigurationChanged();
                    }
                }, () => GetSelectedHole() != null ? GetSelectedHole().localCenter.z : 0.0f, "0.000 m");
            }

            AddSlider(hole.transform, "Flow multiplier", 0.0f, 4.0f, 1.0f, value =>
            {
                BucketHoleConfig h = GetSelectedHole();
                if (h != null)
                {
                    h.flowMultiplier = Mathf.Max(value, 0.0f);
                    NotifyHoleConfigurationChanged();
                }
            }, () => GetSelectedHole() != null ? GetSelectedHole().flowMultiplier : 0.0f, "0.00x");

            AddSlider(hole.transform, "Exit boost", 0.0f, 1.5f, 0.15f, value =>
            {
                BucketHoleConfig h = GetSelectedHole();
                if (h != null)
                {
                    h.exitVelocityBoostMetersPerSecond = Mathf.Max(value, 0.0f);
                    NotifyHoleConfigurationChanged();
                }
            }, () => GetSelectedHole() != null ? GetSelectedHole().exitVelocityBoostMetersPerSecond : 0.0f, "0.00 m/s");

            AddActionGrid(
                ("Toggle All Holes (H)", ToggleAllHoles, Accent2),
                ("Apply + Reset Bucket", ResetAll, Accent));

            _dynamicRefreshers.Add(() =>
            {
                if (_bucketMotionDropdown != null && _bucketConfig != null)
                    _bucketMotionDropdown.SetValueWithoutNotify((int)_bucketConfig.motionMode);
                if (_bucketMeshToggle != null && _bucketRenderer != null)
                    _bucketMeshToggle.SetIsOnWithoutNotify(_bucketRenderer.BucketMeshVisible);
                BucketHoleConfig h = GetSelectedHole();
                if (holeSummary != null)
                {
                    int count = GetHoleCount();
                    int activeCount = CountActiveHoles();
                    int maxCount = GetMaximumRuntimeHoleCount();
                    holeSummary.text =
                        $"Configured {count}/{maxCount}   Active {activeCount}";
                }
                if (_primaryHoleToggle != null && h != null)
                    _primaryHoleToggle.SetIsOnWithoutNotify(h.active);
                if (_holeAutoCenterToggle != null && h != null)
                    _holeAutoCenterToggle.SetIsOnWithoutNotify(h.autoPlaceAtBottomCenter);
                if (_bucketHoleSelectorDropdown != null)
                    _bucketHoleSelectorDropdown.SetValueWithoutNotify(_selectedHoleIndex);
                if (_bucketHoleShapeDropdown != null && h != null)
                    _bucketHoleShapeDropdown.SetValueWithoutNotify((int)h.shape);
            });
        }

        private void BuildMaterialTab()
        {
            AddTitle("Paint Material", "The material config is the single source for viscosity, yield, surface tension, and surface film response.");

            GameObject preset = AddSection("Preset");
            _materialPresetDropdown = AddDropdown(
                preset.transform,
                "Material preset",
                Enum.GetNames(typeof(PaintMaterialPreset)),
                index =>
                {
                    if (_materialConfig == null)
                        return;

                    _materialConfig.ApplyMaterialPreset((PaintMaterialPreset)index);
                    RefreshDynamicContent();
                });

            GameObject rheology = AddSection("Rheology");
            AddSlider(rheology.transform, "Zero-shear viscosity", 0.001f, 8.0f, 2.6f, value =>
            {
                if (_materialConfig != null)
                    _materialConfig.zeroShearViscosityPaS = Mathf.Max(value, 0.0001f);
            }, () => _materialConfig != null ? _materialConfig.zeroShearViscosityPaS : 0f, "0.000 Pa.s");

            AddSlider(rheology.transform, "High-shear viscosity", 0.001f, 1.0f, 0.14f, value =>
            {
                if (_materialConfig != null)
                    _materialConfig.infiniteShearViscosityPaS = Mathf.Max(value, 0.0001f);
            }, () => _materialConfig != null ? _materialConfig.infiniteShearViscosityPaS : 0f, "0.000 Pa.s");

            AddSlider(rheology.transform, "Flow index", 0.05f, 1.0f, 0.55f, value =>
            {
                if (_materialConfig != null)
                    _materialConfig.flowIndex = Mathf.Clamp(value, 0.05f, 1.0f);
            }, () => _materialConfig != null ? _materialConfig.flowIndex : 0f, "0.00");

            AddSlider(rheology.transform, "Yield stress", 0.0f, 0.6f, 0.0f, value =>
            {
                if (_materialConfig != null)
                    _materialConfig.yieldStressPa = Mathf.Max(value, 0.0f);
            }, () => _materialConfig != null ? _materialConfig.yieldStressPa : 0f, "0.000 Pa");

            AddSlider(rheology.transform, "Surface tension", 0.02f, 0.08f, 0.035f, value =>
            {
                if (_materialConfig != null)
                    _materialConfig.surfaceTensionNPerM = Mathf.Max(value, 0.0001f);
            }, () => _materialConfig != null ? _materialConfig.surfaceTensionNPerM : 0f, "0.000 N/m");

            AddActionGrid(
                ("Water", () => ApplyMaterialPreset(PaintMaterialPreset.WaterLike), ControlBg),
                ("Latex", () => ApplyMaterialPreset(PaintMaterialPreset.LatexPaint), Accent),
                ("Heavy Body", () => ApplyMaterialPreset(PaintMaterialPreset.HeavyBodyPaint), Accent2));

            _dynamicRefreshers.Add(() =>
            {
                if (_materialPresetDropdown != null && _materialConfig != null)
                    _materialPresetDropdown.SetValueWithoutNotify((int)_materialConfig.materialPreset);
            });
        }

        private void BuildViewTab()
        {
            AddTitle("View And Rendering", "Control camera behavior and particle/fluid visual presentation.");

            GameObject camera = AddSection("Camera");
            _cameraSelectorDropdown = AddDropdown(
                camera.transform,
                "Active camera",
                BuildCameraOptions(),
                ActivateCamera);
            _cameraSelectorDropdown.SetValueWithoutNotify(
                GetActiveCameraIndex(GetLabCameras()));
            _autoOrbitToggle = AddToggle(camera.transform, "Auto orbit", false, value =>
            {
                if (_interactiveCamera != null)
                    _interactiveCamera.AutoOrbitEnabled = value;
            });
            AddActionGrid(
                ("Next Camera (K)", CycleCamera, Accent2),
                ("Focus View", FocusCamera, Accent),
                ("Reset View", () => _interactiveCamera?.ResetView(), ControlBg));

            GameObject render = AddSection("Fluid Rendering");
            _renderModeDropdown = AddDropdown(
                render.transform,
                "Render mode",
                Enum.GetNames(typeof(FluidRenderMode)),
                index =>
                {
                    if (_renderConfig != null)
                        _renderConfig.fluidRenderMode = (FluidRenderMode)index;
                });

            AddSlider(render.transform, "Particle scale", 0.25f, 2.5f, 1.18f, value =>
            {
                if (_renderConfig != null)
                    _renderConfig.visualRadiusScale = value;
            }, () => _renderConfig != null ? _renderConfig.visualRadiusScale : 0f, "0.00x");

            AddSlider(render.transform, "Fluid surface scale", 0.5f, 4.0f, 1.6f, value =>
            {
                if (_renderConfig != null)
                    _renderConfig.fluidParticleScale = value;
            }, () => _renderConfig != null ? _renderConfig.fluidParticleScale : 0f, "0.00x");

            AddSlider(render.transform, "Smoothing passes", 0f, 8f, 3f, value =>
            {
                if (_renderConfig != null)
                    _renderConfig.fluidSmoothingIterations = Mathf.RoundToInt(value);
            }, () => _renderConfig != null ? _renderConfig.fluidSmoothingIterations : 0f, "0");

            AddToggle(render.transform, "Hide deposited/lost particles", _renderConfig != null && _renderConfig.hideCanvasAndLostParticles, value =>
            {
                if (_renderConfig != null)
                    _renderConfig.hideCanvasAndLostParticles = value;
            });

            _dynamicRefreshers.Add(() =>
            {
                if (_autoOrbitToggle != null && _interactiveCamera != null)
                    _autoOrbitToggle.SetIsOnWithoutNotify(_interactiveCamera.AutoOrbitEnabled);
                if (_renderModeDropdown != null && _renderConfig != null)
                    _renderModeDropdown.SetValueWithoutNotify((int)_renderConfig.fluidRenderMode);
            });
        }

        private void BuildStatsTab()
        {
            AddTitle("Diagnostics", "High-signal runtime data without covering the lab with the old overlay.");

            GameObject section = AddSection("Runtime");
            Text stats = AddReadonlyLine(section.transform, "Waiting for diagnostics");
            _dynamicRefreshers.Add(() =>
            {
                if (stats == null)
                    return;

                if (_simulationManager == null || !_simulationManager.IsInitialized)
                {
                    stats.text = "Simulation not initialized";
                    return;
                }

                DiagnosticsFrame d = _simulationManager.Context.Diagnostics;
                FluidSolverStats fs = _paintFluidSystem != null
                    ? _paintFluidSystem.SolverStats
                    : default;

                stats.text =
                    $"Frame: {d.unityFrame}\n" +
                    $"FPS: {d.fps:F1}\n" +
                    $"Time: {d.simulationTime:F2}s\n" +
                    $"Solver: {fs.solverType} / {fs.status}\n" +
                    $"Particles: {fs.particleCount:n0}\n" +
                    $"GPU active: {fs.gpuActiveParticleCount:n0}\n" +
                    $"GPU step: {fs.gpuProfileTotalMilliseconds:F2} ms\n" +
                    $"Board mode: {(_boardController != null ? _boardController.MotionModeLabel : "-")}\n" +
                    $"Last export: {(string.IsNullOrEmpty(_lastExportPath) ? "-" : _lastExportPath)}";
            });

            GameObject overlay = AddSection("Debug Overlay");
            _legacyDebugToggle = AddToggle(overlay.transform, "Show legacy debug overlay (F1)", _legacyDebugVisible, value =>
            {
                _legacyDebugVisible = value;
                if (_debugOverlay != null)
                    _debugOverlay.enabled = value;
            });
        }

        private void RefreshTopBar()
        {
            bool initialized = _simulationManager != null && _simulationManager.IsInitialized;
            bool paused = initialized &&
                          _simulationManager.TimeController != null &&
                          _simulationManager.TimeController.IsPaused;

            if (_statusText != null)
            {
                _statusText.text = initialized
                    ? (paused ? "Paused - lab controls live" : "Running - lab controls live")
                    : "Waiting for simulation systems";
                _statusText.color = initialized ? (paused ? Accent2 : Accent) : Danger;
            }

            if (_metricText != null)
            {
                int particles = _paintFluidSystem != null && _paintFluidSystem.IsInitialized
                    ? _paintFluidSystem.ParticleCount
                    : 0;
                float fps = initialized ? _simulationManager.Context.Diagnostics.fps : 0.0f;
                string solver = _paintFluidSystem != null
                    ? _paintFluidSystem.SolverStats.solverType.ToString()
                    : "-";
                _metricText.text = $"Particles {particles:n0}   FPS {fps:F0}   Solver {solver}";
            }
        }

        private void RefreshDynamicContent()
        {
            for (int i = 0; i < _dynamicRefreshers.Count; i++)
                _dynamicRefreshers[i]?.Invoke();
        }

        private void UpdateResponsiveLayout()
        {
            if (_topBar == null || _nav == null || _panel == null || _hintText == null)
                return;

            float screenW = Mathf.Max(Screen.width, 640);
            float screenH = Mathf.Max(Screen.height, 360);
            float topH = screenH < 700 ? 56f : 64f;
            float bottomH = screenH < 700 ? 30f : 38f;
            float navW = screenW < 1100 ? 92f : 124f;
            float panelW = Mathf.Clamp(screenW * 0.36f, 450f, 640f);

            if (screenW < 980)
            {
                navW = 78f;
                panelW = Mathf.Clamp(screenW - navW - 18f, 360f, 460f);
            }

            if (_userPanelWidth > 0.0f)
            {
                RectTransform canvasRect = _canvas != null
                    ? _canvas.transform as RectTransform
                    : null;
                float canvasWidth = canvasRect != null && canvasRect.rect.width > 1f
                    ? canvasRect.rect.width
                    : screenW;
                float minPanelWidth = screenW < 980f ? 340f : 420f;
                float maxPanelWidth = Mathf.Max(
                    minPanelWidth,
                    canvasWidth - navW - 120f);
                panelW = Mathf.Clamp(_userPanelWidth, minPanelWidth, maxPanelWidth);
                _userPanelWidth = panelW;
            }

            _topBar.sizeDelta = new Vector2(0f, topH);

            _nav.anchorMin = new Vector2(0f, 0f);
            _nav.anchorMax = new Vector2(0f, 1f);
            _nav.pivot = new Vector2(0f, 0.5f);
            _nav.anchoredPosition = new Vector2(0f, (bottomH - topH) * 0.5f);
            _nav.sizeDelta = new Vector2(navW, -topH - bottomH);

            _panel.anchorMin = new Vector2(1f, 0f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 0.5f);
            _panel.anchoredPosition = new Vector2(0f, (bottomH - topH) * 0.5f);
            _panel.sizeDelta = new Vector2(panelW, -topH - bottomH);

            _hintText.fontSize = screenW < 1100 ? 12 : 14;

            Vector2Int layoutSize = new Vector2Int(Screen.width, Screen.height);
            if (layoutSize != _lastLayoutSize)
            {
                _lastLayoutSize = layoutSize;
                RefreshPanelLayout();
            }
        }

        internal void ResizeInspectorPanel(Vector2 screenPosition, Camera eventCamera)
        {
            if (_canvas == null)
                return;

            RectTransform canvasRect = _canvas.transform as RectTransform;
            if (canvasRect == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPosition,
                    eventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            float navWidth = _nav != null ? _nav.rect.width : 100f;
            float minWidth = Screen.width < 980 ? 340f : 420f;
            float maxWidth = Mathf.Max(
                minWidth,
                canvasRect.rect.width - navWidth - 120f);
            _userPanelWidth = Mathf.Clamp(
                canvasRect.rect.xMax - localPoint.x,
                minWidth,
                maxWidth);

            HideDropdownPopup();
            UpdateResponsiveLayout();
            RefreshPanelLayout();
        }

        internal void ResetInspectorPanelWidth()
        {
            _userPanelWidth = -1.0f;
            HideDropdownPopup();
            UpdateResponsiveLayout();
            RefreshPanelLayout();
        }

        private void RefreshPanelLayout()
        {
            if (_content == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float preferredHeight = Mathf.Max(720f, LayoutUtility.GetPreferredHeight(_content));
            if (_panel != null && _panel.rect.height > 1f)
                preferredHeight = Mathf.Max(preferredHeight, _panel.rect.height);

            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
            if (_content.anchoredPosition.y < 0f)
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            Canvas.ForceUpdateCanvases();
        }

        private void ResetInspectorScroll()
        {
            if (_inspectorScroll == null || _content == null)
                return;

            _inspectorScroll.StopMovement();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            _inspectorScroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
        }

        private void TogglePause()
        {
            _simulationManager?.TimeController?.TogglePause();
        }

        private void StepOnce()
        {
            _simulationManager?.TimeController?.RequestSingleStep();
        }

        private void ResetAll()
        {
            _simulationManager?.ResetSimulationState();
        }

        private void ClearPaint()
        {
            if (_boardController != null)
                _boardController.ClearPaintFilm();
            else
                _paintSurface?.ClearPaintFilm();
        }

        private void FocusCamera()
        {
            _interactiveCamera?.FocusTarget();
        }

        private void CycleCamera()
        {
            global::InteractiveCamera[] cameras = GetLabCameras();
            if (cameras.Length > 0)
                ActivateCamera((GetActiveCameraIndex(cameras) + 1) % cameras.Length);
        }

        private void ActivateCamera(int index)
        {
            global::InteractiveCamera[] cameras = GetLabCameras();
            if (cameras.Length == 0)
                return;

            int selectedIndex = Mathf.Clamp(index, 0, cameras.Length - 1);
            for (int i = 0; i < cameras.Length; i++)
            {
                bool active = i == selectedIndex;
                Camera camera = cameras[i].GetComponent<Camera>();
                if (camera != null)
                    camera.enabled = active;

            }

            _interactiveCamera = cameras[selectedIndex];
            _interactiveCamera.enabled = true;
            _cameraSelectorDropdown?.SetValueWithoutNotify(selectedIndex);
            RefreshDynamicContent();
        }

        private string[] BuildCameraOptions()
        {
            global::InteractiveCamera[] cameras = GetLabCameras();
            if (cameras.Length == 0)
                return new[] { "No cameras" };

            return Array.ConvertAll(
                cameras,
                item => item.name.Replace("Camera_", string.Empty).Replace('_', ' '));
        }

        private static global::InteractiveCamera[] GetLabCameras()
        {
            global::InteractiveCamera[] cameras =
                UnityEngine.Object.FindObjectsByType<global::InteractiveCamera>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            Array.Sort(
                cameras,
                (left, right) => string.CompareOrdinal(left.name, right.name));
            return cameras;
        }

        private static int GetActiveCameraIndex(
            global::InteractiveCamera[] cameras)
        {
            int index = cameras != null
                ? Array.FindIndex(cameras, item => item.IsViewActive)
                : -1;
            return Mathf.Max(index, 0);
        }

        private void ToggleBoardMotion()
        {
            _boardController?.ToggleMotion();
        }

        private void ApplyParticleBudget()
        {
            if (_fluidConfig == null)
                return;

            _fluidConfig.targetParticleCount = Mathf.Clamp(_pendingParticleCount, 1000, 500000);
            ResetAll();
        }

        private void ApplyMaterialPreset(PaintMaterialPreset preset)
        {
            if (_materialConfig == null)
                return;

            _materialConfig.ApplyMaterialPreset(preset);
            if (_materialPresetDropdown != null)
                _materialPresetDropdown.SetValueWithoutNotify((int)preset);
            RefreshDynamicContent();
        }

        private void ApplySurfaceColorMixing(
            PaintColorMixingMode? mode = null,
            float? mix = null,
            float? minReflectance = null,
            float? maxKs = null)
        {
            PaintColorMixingMode resolvedMode = mode ??
                (_paintHost != null
                    ? _paintHost.ColorMixingMode
                    : (_paintSurface != null
                        ? _paintSurface.ColorMixingMode
                        : PaintColorMixingMode.KubelkaMunkApprox));

            float resolvedMix = mix ??
                (_paintHost != null
                    ? _paintHost.PigmentMixStrength
                    : (_paintSurface != null ? _paintSurface.PigmentMixStrength : 1.0f));

            float resolvedMinReflectance = minReflectance ??
                (_paintHost != null
                    ? _paintHost.PigmentMinReflectance
                    : (_paintSurface != null ? _paintSurface.PigmentMinReflectance : 0.035f));

            float resolvedMaxKs = maxKs ??
                (_paintHost != null
                    ? _paintHost.PigmentMaxKs
                    : (_paintSurface != null ? _paintSurface.PigmentMaxKs : 18.0f));

            if (_paintHost != null)
            {
                _paintHost.ConfigureColorMixingRuntime(
                    resolvedMode,
                    resolvedMix,
                    resolvedMinReflectance,
                    resolvedMaxKs);
            }

            if (_paintSurface != null)
            {
                _paintSurface.ConfigureColorMixing(
                    resolvedMode,
                    resolvedMix,
                    resolvedMinReflectance,
                    resolvedMaxKs);
            }

            RefreshDynamicContent();
        }

        private BucketHoleConfig GetPrimaryHole()
        {
            if (_bucketConfig == null)
                return null;

            if (_bucketConfig.holes == null || _bucketConfig.holes.Length == 0)
                _bucketConfig.holes = new[] { new BucketHoleConfig() };

            if (_bucketConfig.holes[0] == null)
                _bucketConfig.holes[0] = new BucketHoleConfig();

            return _bucketConfig.holes[0];
        }

        private BucketHoleConfig GetSelectedHole()
        {
            if (_bucketConfig == null)
                return null;

            if (_bucketConfig.holes == null || _bucketConfig.holes.Length == 0)
                _bucketConfig.holes = new[] { CreateDefaultHole(0) };

            ClampSelectedHoleIndex();
            if (_bucketConfig.holes[_selectedHoleIndex] == null)
                _bucketConfig.holes[_selectedHoleIndex] = CreateDefaultHole(_selectedHoleIndex);

            return _bucketConfig.holes[_selectedHoleIndex];
        }

        private void ClampSelectedHoleIndex()
        {
            int count = GetHoleCount();
            _selectedHoleIndex = count > 0
                ? Mathf.Clamp(_selectedHoleIndex, 0, count - 1)
                : 0;
        }

        private int GetHoleCount()
        {
            return _bucketConfig != null && _bucketConfig.holes != null
                ? _bucketConfig.holes.Length
                : 0;
        }

        private int CountActiveHoles()
        {
            if (_bucketConfig == null || _bucketConfig.holes == null)
                return 0;

            int activeCount = 0;
            for (int i = 0; i < _bucketConfig.holes.Length; i++)
            {
                if (_bucketConfig.holes[i] != null && _bucketConfig.holes[i].active)
                    activeCount++;
            }

            return activeCount;
        }

        private void ToggleAllHoles()
        {
            if (_bucketConfig == null || _bucketConfig.holes == null ||
                _bucketConfig.holes.Length == 0)
            {
                return;
            }

            bool open = CountActiveHoles() < _bucketConfig.holes.Length;
            for (int i = 0; i < _bucketConfig.holes.Length; i++)
            {
                if (_bucketConfig.holes[i] == null)
                    _bucketConfig.holes[i] = CreateDefaultHole(i);
                _bucketConfig.holes[i].active = open;
            }

            NotifyHoleConfigurationChanged();
            if (_activeTab == LabTab.Bucket)
                RebuildCurrentTab();
        }

        private int GetMaximumRuntimeHoleCount()
        {
            GpuMpmSolverConfig gpuConfig = _paintFluidSystem != null
                ? _paintFluidSystem.GpuMpmConfig
                : null;
            return gpuConfig != null
                ? Mathf.Clamp(gpuConfig.maxGpuBucketHoles, 1, 64)
                : 16;
        }

        private string[] BuildHoleOptions()
        {
            int count = GetHoleCount();
            if (count <= 0)
                return new[] { "Hole 1" };

            string[] options = new string[count];
            for (int i = 0; i < count; i++)
            {
                BucketHoleConfig hole = _bucketConfig.holes[i];
                string name = hole != null && !string.IsNullOrWhiteSpace(hole.name)
                    ? hole.name
                    : $"Hole {i + 1}";
                string state = hole != null && hole.active ? "open" : "closed";
                options[i] = $"{i + 1}. {name} ({state})";
            }

            return options;
        }

        private void SelectBucketHole(int index)
        {
            _selectedHoleIndex = Mathf.Clamp(index, 0, Mathf.Max(0, GetHoleCount() - 1));
            RebuildCurrentTab();
        }

        private void AddBucketHole()
        {
            if (_bucketConfig == null)
                return;

            int count = GetHoleCount();
            int maxCount = GetMaximumRuntimeHoleCount();
            if (count >= maxCount)
            {
                Debug.LogWarning($"Bucket hole limit reached ({maxCount}).");
                return;
            }

            BucketHoleConfig[] holes = new BucketHoleConfig[count + 1];
            if (_bucketConfig.holes != null && count > 0)
                Array.Copy(_bucketConfig.holes, holes, count);
            holes[count] = CreateDefaultHole(count);
            _bucketConfig.holes = holes;
            _selectedHoleIndex = count;
            NotifyHoleConfigurationChanged();
            RebuildCurrentTab();
        }

        private void DuplicateSelectedHole()
        {
            BucketHoleConfig source = GetSelectedHole();
            if (source == null || _bucketConfig == null)
                return;

            int count = GetHoleCount();
            int maxCount = GetMaximumRuntimeHoleCount();
            if (count >= maxCount)
            {
                Debug.LogWarning($"Bucket hole limit reached ({maxCount}).");
                return;
            }

            BucketHoleConfig copy = CloneHole(source);
            copy.name = string.IsNullOrWhiteSpace(source.name)
                ? $"Hole {count + 1}"
                : source.name + " Copy";
            copy.autoPlaceAtBottomCenter = false;
            if (_bucketConfig != null)
            {
                float offset = Mathf.Max(copy.radiusMeters * 2.5f, 0.015f);
                copy.localCenter = _bucketConfig.GetResolvedHoleLocalCenter(source) +
                                   new Vector3(offset, 0f, 0f);
            }

            BucketHoleConfig[] holes = new BucketHoleConfig[count + 1];
            Array.Copy(_bucketConfig.holes, holes, count);
            holes[count] = copy;
            _bucketConfig.holes = holes;
            _selectedHoleIndex = count;
            NotifyHoleConfigurationChanged();
            RebuildCurrentTab();
        }

        private void RemoveSelectedHole()
        {
            if (_bucketConfig == null || _bucketConfig.holes == null)
                return;

            int count = _bucketConfig.holes.Length;
            if (count <= 1)
            {
                BucketHoleConfig onlyHole = GetSelectedHole();
                if (onlyHole != null)
                    onlyHole.active = false;
                NotifyHoleConfigurationChanged();
                RebuildCurrentTab();
                return;
            }

            BucketHoleConfig[] holes = new BucketHoleConfig[count - 1];
            int destination = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == _selectedHoleIndex)
                    continue;
                holes[destination++] = _bucketConfig.holes[i];
            }

            _bucketConfig.holes = holes;
            _selectedHoleIndex = Mathf.Clamp(_selectedHoleIndex, 0, holes.Length - 1);
            NotifyHoleConfigurationChanged();
            RebuildCurrentTab();
        }

        private BucketHoleConfig CreateDefaultHole(int index)
        {
            BucketHoleConfig hole = new BucketHoleConfig
            {
                name = $"Hole {index + 1}",
                autoPlaceAtBottomCenter = index == 0
            };

            if (_bucketConfig == null)
                return hole;

            float radius = Mathf.Max(
                _bucketConfig.GetRepresentativeRadius() * 0.42f,
                0.02f);
            float angle = index * 137.50776f * Mathf.Deg2Rad;
            hole.localCenter = new Vector3(
                Mathf.Cos(angle) * radius,
                -_bucketConfig.heightMeters * 0.5f,
                Mathf.Sin(angle) * radius);
            return hole;
        }

        private static BucketHoleConfig CloneHole(BucketHoleConfig source)
        {
            return new BucketHoleConfig
            {
                name = source.name,
                shape = source.shape,
                autoPlaceAtBottomCenter = source.autoPlaceAtBottomCenter,
                localCenter = source.localCenter,
                localNormal = source.localNormal,
                radiusMeters = source.radiusMeters,
                sizeMeters = source.sizeMeters,
                localTangent = source.localTangent,
                edgeSoftnessMeters = source.edgeSoftnessMeters,
                flowMultiplier = source.flowMultiplier,
                exitVelocityBoostMetersPerSecond = source.exitVelocityBoostMetersPerSecond,
                wallThicknessMeters = source.wallThicknessMeters,
                active = source.active
            };
        }

        private void NotifyHoleConfigurationChanged()
        {
            _bucketSystem?.RefreshHoleConfiguration();
        }

        private int GetColorCompartmentCount()
        {
            return _fluidConfig != null
                ? Mathf.Clamp(_fluidConfig.colorCompartmentCount, 1, 8)
                : 1;
        }

        private void EnsureCompartmentColorCapacity()
        {
            if (_fluidConfig == null)
                return;

            int count = GetColorCompartmentCount();
            Color[] current = _fluidConfig.compartmentColors;
            if (current != null && current.Length == count)
                return;

            int copyCount = current != null
                ? Mathf.Min(current.Length, count)
                : 0;
            Color[] colors = new Color[count];
            if (copyCount > 0)
                Array.Copy(current, colors, copyCount);

            for (int i = copyCount; i < count; i++)
            {
                colors[i] = Color.HSVToRGB(
                    Mathf.Repeat(0.58f + i * 0.31f, 1f),
                    0.78f,
                    0.96f);
                colors[i].a = 1f;
            }
            _fluidConfig.compartmentColors = colors;
        }

        private string[] BuildCompartmentColorOptions()
        {
            int count = GetColorCompartmentCount();
            string[] options = new string[count];
            for (int i = 0; i < count; i++)
                options[i] = $"Compartment {i + 1}";
            return options;
        }

        private Color GetSelectedCompartmentColor()
        {
            EnsureCompartmentColorCapacity();
            if (_fluidConfig == null || _fluidConfig.compartmentColors == null ||
                _fluidConfig.compartmentColors.Length == 0)
            {
                return Color.white;
            }

            int index = Mathf.Clamp(
                _selectedCompartmentIndex,
                0,
                GetColorCompartmentCount() - 1);
            return _fluidConfig.compartmentColors[index];
        }

        private void AddCompartmentColorPreview(Transform parent)
        {
            GameObject row = CreateRow(parent, 38f);
            Text label = CreateText(
                "Selected color", row.transform, 12, FontStyle.Bold, TextColor);
            label.alignment = TextAnchor.MiddleLeft;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject swatch = CreateUiObject("ColorSwatch", row.transform);
            _compartmentColorSwatch = swatch.AddComponent<Image>();
            _compartmentColorSwatch.raycastTarget = false;
            LayoutElement layout = swatch.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 72f;
        }

        private void AddColorChannelSlider(
            Transform parent,
            string label,
            int channel)
        {
            AddSlider(parent, label, 0f, 1f, GetSelectedCompartmentColor()[channel], value =>
            {
                if (_fluidConfig == null)
                    return;
                Color color = GetSelectedCompartmentColor();
                color[channel] = value;
                color.a = 1f;
                _fluidConfig.compartmentColors[_selectedCompartmentIndex] = color;
                _fluidConfig.enableColorCompartments = true;
            }, () => GetSelectedCompartmentColor()[channel], "0%");
        }

        private void SetCompartmentColors(params Color[] colors)
        {
            if (_fluidConfig == null || colors == null || colors.Length == 0)
                return;

            _fluidConfig.compartmentColors = colors;
            _fluidConfig.colorCompartmentCount = Mathf.Clamp(colors.Length, 1, 8);
            _fluidConfig.enableColorCompartments = true;
            _selectedCompartmentIndex = Mathf.Clamp(
                _selectedCompartmentIndex,
                0,
                GetColorCompartmentCount() - 1);
            if (_activeTab == LabTab.Fluid)
                RebuildCurrentTab();
        }

        private void ExportBoard()
        {
            if (_paintSurface == null || _paintSurface.PaintFilmGrid == null)
            {
                _lastExportPath = "Export failed: paint surface is not ready.";
                return;
            }

            PaintFilmGrid grid = _paintSurface.PaintFilmGrid;
            if (grid.PaintCellBuffer == null)
            {
                _lastExportPath = "Export failed: paint film buffer is missing.";
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string defaultFileName = $"paint_board_{stamp}.png";
            string initialFolder = ResolveInitialExportFolder();
            Directory.CreateDirectory(initialFolder);
            string pngPath = NativeSaveFileDialog.SavePng(
                "Export Painted Board",
                initialFolder,
                defaultFileName);

            if (string.IsNullOrWhiteSpace(pngPath))
            {
                _lastExportPath = "Export cancelled.";
                RefreshDynamicContent();
                return;
            }

            int width = Mathf.Max(1, grid.GridWidth);
            int height = Mathf.Max(1, grid.GridHeight);
            int count = width * height;
            PaintCellData[] cells = new PaintCellData[count];
            grid.PaintCellBuffer.GetData(cells);
            float maxThickness = 0.0f;

            for (int i = 0; i < cells.Length; i++)
                maxThickness = Mathf.Max(maxThickness, cells[i].Thickness);

            _paintSurface.BakePaintTextureNow();
            RenderTexture source = _paintSurface.PaintTexture;
            if (source == null || !source.IsCreated())
            {
                _lastExportPath = "Export failed: rendered paint texture is missing.";
                RefreshDynamicContent();
                return;
            }

            Texture2D texture = null;
            RenderTexture staging = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                staging = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                staging.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, staging);

                RenderTexture.active = staging;
                texture = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    false,
                    false);
                texture.ReadPixels(
                    new Rect(0f, 0f, source.width, source.height),
                    0,
                    0,
                    false);
                texture.Apply(false, false);

                string exportDirectory = Path.GetDirectoryName(pngPath);
                if (!string.IsNullOrWhiteSpace(exportDirectory))
                    Directory.CreateDirectory(exportDirectory);

                string jsonPath = Path.ChangeExtension(pngPath, ".json");
                File.WriteAllBytes(pngPath, texture.EncodeToPNG());
                File.WriteAllText(
                    jsonPath,
                    BuildExperimentMetadataJson(
                        pngPath,
                        source.width,
                        source.height,
                        maxThickness));
                _lastExportPath = pngPath;
            }
            catch (Exception exception)
            {
                _lastExportPath = $"Export failed: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                RenderTexture.active = previous;
                if (staging != null)
                    RenderTexture.ReleaseTemporary(staging);
                if (texture != null)
                    Destroy(texture);
            }

            RefreshDynamicContent();
        }

        private string ResolveInitialExportFolder()
        {
            if (!string.IsNullOrWhiteSpace(_lastExportPath) &&
                Path.IsPathRooted(_lastExportPath))
            {
                string lastFolder = Path.GetDirectoryName(_lastExportPath);
                if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
                    return lastFolder;
            }

            return GetExportFolder();
        }

        private static string GetExportFolder()
        {
            if (Application.isEditor)
                return Path.Combine(Directory.GetCurrentDirectory(), "Exports");

            DirectoryInfo dataDir = Directory.GetParent(Application.dataPath);
            string root = dataDir != null ? dataDir.FullName : Application.persistentDataPath;
            return Path.Combine(root, "Exports");
        }

        private string BuildExperimentMetadataJson(
            string pngPath,
            int width,
            int height,
            float maxThickness)
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("{");
            AppendJson(sb, "timestamp", DateTime.Now.ToString("O"), true);
            AppendJson(sb, "image", pngPath.Replace("\\", "/"), true);
            AppendJson(sb, "gridWidth", width, true);
            AppendJson(sb, "gridHeight", height, true);
            AppendJson(sb, "maxPaintThicknessMeters", maxThickness, true);
            AppendJson(sb, "particleCount", _paintFluidSystem != null ? _paintFluidSystem.ParticleCount : 0, true);
            AppendJson(sb, "solver", _paintFluidSystem != null ? _paintFluidSystem.SolverStats.solverType.ToString() : "-", true);
            AppendJson(sb, "renderMode", _renderConfig != null ? _renderConfig.fluidRenderMode.ToString() : "-", true);
            AppendJson(sb, "materialPreset", _materialConfig != null ? _materialConfig.materialPreset.ToString() : "-", true);
            AppendJson(sb, "densityKgPerM3", _materialConfig != null ? _materialConfig.densityKgPerM3 : 0f, true);
            AppendJson(sb, "zeroShearViscosityPaS", _materialConfig != null ? _materialConfig.zeroShearViscosityPaS : 0f, true);
            AppendJson(sb, "highShearViscosityPaS", _materialConfig != null ? _materialConfig.infiniteShearViscosityPaS : 0f, true);
            AppendJson(sb, "yieldStressPa", _materialConfig != null ? _materialConfig.yieldStressPa : 0f, true);
            AppendJson(sb, "surfaceTensionNPerM", _materialConfig != null ? _materialConfig.surfaceTensionNPerM : 0f, true);
            AppendJson(sb, "targetParticles", _fluidConfig != null ? _fluidConfig.targetParticleCount : 0, true);
            AppendJson(sb, "fillFraction", _fluidConfig != null ? _fluidConfig.fillFraction01 : 0f, true);
            AppendJson(sb, "renderedParticleBudget", _fluidConfig != null ? _fluidConfig.maxRenderedParticles : 0, true);
            AppendJson(sb, "renderStride", _fluidConfig != null ? _fluidConfig.renderStride : 0, true);
            AppendJson(sb, "colorCompartments", _fluidConfig != null && _fluidConfig.enableColorCompartments, true);
            AppendJson(sb, "colorCompartmentCount", _fluidConfig != null ? _fluidConfig.colorCompartmentCount : 0, true);
            AppendJson(sb, "colorCompartmentAxis", _fluidConfig != null ? _fluidConfig.colorCompartmentAxis.ToString() : "-", true);
            AppendJson(sb, "physicalColorDividers", _fluidConfig != null && _fluidConfig.enablePhysicalColorDividers, true);
            AppendJson(sb, "colorDividerGapFraction", _fluidConfig != null ? _fluidConfig.colorDividerGapFraction : 0f, true);
            AppendJson(sb, "colorDividerThicknessMeters", _fluidConfig != null ? _fluidConfig.colorDividerThicknessMeters : 0f, true);
            AppendJson(sb, "boardMotion", _boardController != null ? _boardController.MotionModeLabel : "-", true);
            AppendJson(sb, "boardMotionEnabled", _boardController != null && _boardController.MotionEnabled, true);
            AppendJson(sb, "boardSpeedMultiplier", _boardController != null ? _boardController.SpeedMultiplier : 0f, true);
            AppendJson(sb, "boardTiltAmplitudeDegrees", _boardController != null ? _boardController.TiltAmplitudeDegrees : 0f, true);
            AppendJson(sb, "boardSpinDegreesPerSecond", _boardController != null ? _boardController.SpinDegreesPerSecond : 0f, true);
            AppendJson(sb, "boardTravelAmplitudeMeters", _boardController != null ? _boardController.TravelAmplitudeMeters : 0f, true);
            AppendJson(sb, "surfaceType", _paintSurface != null ? _paintSurface.SurfaceType.ToString() : "-", true);
            AppendJson(sb, "surfaceEvolutionEnabled", _paintSurface != null && _paintSurface.EvolutionEnabled, true);
            AppendJson(sb, "surfaceInertiaEnabled", _paintSurface != null && _paintSurface.IncludeSurfaceInertiaInFlow, true);
            AppendJson(sb, "paintSurfaceRenderInterval", _paintSurface != null ? _paintSurface.RenderEveryNFrames : 0, true);
            AppendJson(sb, "paintSurfaceEvolveInterval", _paintSurface != null ? _paintSurface.EvolveEveryNFrames : 0, true);
            AppendJson(sb, "depositOnlyAirDomainParticles", _paintHost != null && _paintHost.DepositOnlyAirDomainParticles, true);
            AppendJson(sb, "depositEveryNFrames", _paintHost != null ? _paintHost.DepositEveryNFrames : 0, true);
            AppendJson(sb, "paintColorMixingMode", _paintHost != null ? _paintHost.ColorMixingMode.ToString() : (_paintSurface != null ? _paintSurface.ColorMixingMode.ToString() : "-"), true);
            AppendJson(sb, "pigmentMixStrength", _paintHost != null ? _paintHost.PigmentMixStrength : (_paintSurface != null ? _paintSurface.PigmentMixStrength : 0f), true);
            AppendJson(sb, "pigmentMinReflectance", _paintHost != null ? _paintHost.PigmentMinReflectance : (_paintSurface != null ? _paintSurface.PigmentMinReflectance : 0f), true);
            AppendJson(sb, "pigmentMaxKs", _paintHost != null ? _paintHost.PigmentMaxKs : (_paintSurface != null ? _paintSurface.PigmentMaxKs : 0f), true);
            AppendJson(sb, "ropeLengthMeters", _ropeConfig != null ? _ropeConfig.lengthMeters : 0f, true);
            AppendJson(sb, "ropeSegments", _ropeConfig != null ? _ropeConfig.segmentCount : 0, true);
            AppendJson(sb, "ropeMassKg", _ropeConfig != null ? _ropeConfig.ropeMassKg : 0f, true);
            AppendJson(sb, "ropeVisualRadiusMeters", _ropeConfig != null ? _ropeConfig.visualRadiusMeters : 0f, true);
            AppendJson(sb, "ropeDampingMode", _ropeConfig != null ? _ropeConfig.dampingMode.ToString() : "-", true);
            AppendJson(sb, "ropePivotMotionMode", _ropeConfig != null ? _ropeConfig.pivotMotionMode.ToString() : "-", true);
            AppendJson(sb, "ropePivotFrequencyHz", _ropeConfig != null ? _ropeConfig.pivotMotionFrequencyHz : 0f, true);
            AppendJson(sb, "ropePivotAmplitudeX", _ropeConfig != null ? _ropeConfig.pivotMotionAmplitude.x : 0f, true);
            AppendJson(sb, "couplingEnabled", _couplingConfig != null && _couplingConfig.enableCoupling, true);
            AppendJson(sb, "couplingIterations", _couplingConfig != null ? _couplingConfig.solverIterations : 0, true);
            AppendJson(sb, "couplingAttachmentDamping", _couplingConfig != null ? _couplingConfig.attachmentVelocityDamping : 0f, true);
            AppendJson(sb, "bucketMassKg", _bucketConfig != null ? _bucketConfig.massKg : 0f, true);
            AppendJson(sb, "bucketMotionMode", _bucketConfig != null ? _bucketConfig.motionMode.ToString() : "-", true);
            AppendJson(sb, "bucketHeightMeters", _bucketConfig != null ? _bucketConfig.heightMeters : 0f, true);
            AppendJson(sb, "bucketTopRadiusMeters", _bucketConfig != null ? _bucketConfig.topRadiusMeters : 0f, true);
            AppendJson(sb, "bucketFluidLoadScale", _bucketConfig != null ? _bucketConfig.containedFluidMassScale : 0f, true);
            BucketHoleConfig hole = GetPrimaryHole();
            AppendJson(sb, "primaryHoleActive", hole != null && hole.active, true);
            AppendJson(sb, "primaryHoleShape", hole != null ? hole.shape.ToString() : "-", true);
            AppendJson(sb, "primaryHoleFlowMultiplier", hole != null ? hole.flowMultiplier : 0f, true);
            AppendJson(sb, "primaryHoleExitBoostMetersPerSecond", hole != null ? hole.exitVelocityBoostMetersPerSecond : 0f, true);
            AppendJson(sb, "primaryHoleRadiusMeters", hole != null ? hole.radiusMeters : 0f, true);
            AppendJson(sb, "holeCount", GetHoleCount(), true);
            AppendJson(sb, "activeHoleCount", CountActiveHoles(), true);
            AppendHoleMetadata(sb);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private void AppendHoleMetadata(StringBuilder sb)
        {
            BucketHoleConfig[] holes = _bucketConfig != null
                ? _bucketConfig.holes
                : null;
            sb.AppendLine("  \"holes\": [");
            int count = holes != null ? holes.Length : 0;
            for (int i = 0; i < count; i++)
            {
                BucketHoleConfig hole = holes[i];
                sb.AppendLine("    {");
                AppendJson(sb, "index", i, true);
                AppendJson(sb, "name", hole != null ? hole.name : $"Hole {i + 1}", true);
                AppendJson(sb, "active", hole != null && hole.active, true);
                AppendJson(sb, "shape", hole != null ? hole.shape.ToString() : "-", true);
                AppendJson(sb, "autoPlaceAtBottomCenter", hole != null && hole.autoPlaceAtBottomCenter, true);
                AppendJson(sb, "radiusMeters", hole != null ? hole.radiusMeters : 0f, true);
                AppendJson(sb, "flowMultiplier", hole != null ? hole.flowMultiplier : 0f, true);
                AppendJson(
                    sb,
                    "exitVelocityBoostMetersPerSecond",
                    hole != null ? hole.exitVelocityBoostMetersPerSecond : 0f,
                    true);
                AppendJson(sb, "localCenterX", hole != null ? hole.localCenter.x : 0f, true);
                AppendJson(sb, "localCenterY", hole != null ? hole.localCenter.y : 0f, true);
                AppendJson(sb, "localCenterZ", hole != null ? hole.localCenter.z : 0f, false);
                sb.AppendLine(i < count - 1 ? "    }," : "    }");
            }
            sb.AppendLine("  ]");
        }

        private static void AppendJson(StringBuilder sb, string key, string value, bool comma)
        {
            sb.Append("  \"").Append(EscapeJson(key)).Append("\": \"")
                .Append(EscapeJson(value)).Append("\"");
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendJson(StringBuilder sb, string key, int value, bool comma)
        {
            sb.Append("  \"").Append(EscapeJson(key)).Append("\": ").Append(value);
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendJson(StringBuilder sb, string key, float value, bool comma)
        {
            sb.Append("  \"").Append(EscapeJson(key)).Append("\": ")
                .Append(value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendJson(StringBuilder sb, string key, bool value, bool comma)
        {
            sb.Append("  \"").Append(EscapeJson(key)).Append("\": ")
                .Append(value ? "true" : "false");
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void CreateTabButton(LabTab tab, string label, string number)
        {
            Button button = CreateButton(_nav, $"{number}\n{label}", () => SelectTab(tab), ControlBg, 52f, 12);
            _tabButtons[tab] = button;
        }

        private void AddTitle(string title, string subtitle)
        {
            Text titleText = CreateText(title, _content, 22, FontStyle.Bold, TextColor);
            titleText.alignment = TextAnchor.MiddleLeft;
            LayoutElement titleLayout = titleText.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 30f;

            Text subtitleText = CreateText(subtitle, _content, 12, FontStyle.Normal, MutedText);
            subtitleText.alignment = TextAnchor.UpperLeft;
            LayoutElement subtitleLayout = subtitleText.gameObject.AddComponent<LayoutElement>();
            subtitleLayout.preferredHeight = 38f;
        }

        private GameObject AddSection(string title)
        {
            GameObject section = CreateUiObject(title + "Section", _content);
            Image image = section.AddComponent<Image>();
            image.color = SectionBg;
            VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 12);
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text heading = CreateText(title, section.transform, 14, FontStyle.Bold, TextColor);
            heading.alignment = TextAnchor.MiddleLeft;
            LayoutElement headingLayout = heading.gameObject.AddComponent<LayoutElement>();
            headingLayout.preferredHeight = 22f;
            return section;
        }

        private Text AddReadonlyLine(Transform parent, string text)
        {
            Text label = CreateText(text, parent, 13, FontStyle.Normal, MutedText);
            label.alignment = TextAnchor.UpperLeft;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 160f;
            return label;
        }

        private void AddActionGrid(params (string label, Action action, Color color)[] actions)
        {
            GameObject section = AddSection("Actions");
            for (int i = 0; i < actions.Length; i += 2)
            {
                GameObject row = CreateRow(section.transform, 42f);
                CreateButton(row.transform, actions[i].label, actions[i].action, actions[i].color, 40f, 12);
                if (i + 1 < actions.Length)
                    CreateButton(row.transform, actions[i + 1].label, actions[i + 1].action, actions[i + 1].color, 40f, 12);
                else
                    AddSpacer(row.transform);
            }
        }

        private Toggle AddToggle(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            GameObject row = CreateRow(parent, 34f);
            Toggle toggle = CreateToggle(row.transform, label, onChanged);
            toggle.SetIsOnWithoutNotify(value);
            return toggle;
        }

        private sealed class LabDropdown
        {
            private readonly PaintBucketLabUI _owner;
            private readonly string[] _options;
            private readonly Action<int> _onChanged;
            private int _value;

            public RectTransform ButtonRect { get; }
            public Text Caption { get; }
            public string[] Options => _options;
            public int Value => _value;

            public LabDropdown(
                PaintBucketLabUI owner,
                RectTransform buttonRect,
                Text caption,
                string[] options,
                Action<int> onChanged)
            {
                _owner = owner;
                ButtonRect = buttonRect;
                Caption = caption;
                _options = options ?? Array.Empty<string>();
                _onChanged = onChanged;
                SetValueWithoutNotify(0);
            }

            public void SetValueWithoutNotify(int value)
            {
                if (_options.Length == 0)
                {
                    _value = 0;
                    if (Caption != null)
                        Caption.text = "-";
                    return;
                }

                _value = Mathf.Clamp(value, 0, _options.Length - 1);
                if (Caption != null)
                    Caption.text = _options[_value];
            }

            public void SetValue(int value)
            {
                SetValueWithoutNotify(value);
                _onChanged?.Invoke(_value);
            }

            public void Show()
            {
                _owner.ShowDropdownPopup(this);
            }
        }

        private LabDropdown AddDropdown(Transform parent, string label, string[] options, Action<int> onChanged)
        {
            GameObject group = CreateUiObject(label + "DropdownGroup", parent);
            HorizontalLayoutGroup row = group.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            LayoutElement groupLayout = group.AddComponent<LayoutElement>();
            groupLayout.preferredHeight = 38f;

            Text title = CreateText(label, group.transform, 12, FontStyle.Bold, TextColor);
            title.alignment = TextAnchor.MiddleLeft;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minWidth = 132f;
            titleLayout.preferredWidth = 132f;

            GameObject dropdownObject = CreateUiObject("DropdownButton", group.transform);
            Image image = dropdownObject.AddComponent<Image>();
            image.color = ControlBg;
            Button button = dropdownObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = BuildSelectableColors(ControlBg);

            Text caption = CreateText(options != null && options.Length > 0 ? options[0] : "-", dropdownObject.transform, 13, FontStyle.Bold, TextColor);
            caption.alignment = TextAnchor.MiddleLeft;
            RectTransform captionRect = caption.rectTransform;
            Stretch(captionRect);
            captionRect.offsetMin = new Vector2(12f, 0f);
            captionRect.offsetMax = new Vector2(-28f, 0f);

            Text arrow = CreateText("v", dropdownObject.transform, 14, FontStyle.Bold, MutedText);
            RectTransform arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-10f, 0f);
            arrowRect.sizeDelta = new Vector2(16f, 0f);
            arrow.alignment = TextAnchor.MiddleRight;

            LayoutElement dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
            dropdownLayout.flexibleWidth = 1f;
            dropdownLayout.minWidth = 180f;

            LabDropdown dropdown = new LabDropdown(
                this,
                dropdownObject.GetComponent<RectTransform>(),
                caption,
                options,
                onChanged);
            button.onClick.AddListener(dropdown.Show);
            return dropdown;
        }

        private void ShowDropdownPopup(LabDropdown dropdown)
        {
            if (dropdown == null || _popupLayer == null)
                return;

            HideDropdownPopup();
            Canvas.ForceUpdateCanvases();

            _dropdownPopup = CreateUiObject("DropdownPopup", _popupLayer);
            RectTransform popupRoot = _dropdownPopup.GetComponent<RectTransform>();
            Stretch(popupRoot);
            _dropdownPopup.transform.SetAsLastSibling();

            Image blockerImage = _dropdownPopup.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.01f);
            Button blocker = _dropdownPopup.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.onClick.AddListener(HideDropdownPopup);

            GameObject listObject = CreateUiObject("DropdownList", _dropdownPopup.transform);
            RectTransform listRect = listObject.GetComponent<RectTransform>();
            Image listImage = listObject.AddComponent<Image>();
            listImage.color = new Color(0.035f, 0.045f, 0.050f, 0.99f);
            VerticalLayoutGroup layout = listObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            float rowHeight = 38f;
            float height = Mathf.Min(300f, Mathf.Max(rowHeight, dropdown.Options.Length * (rowHeight + 4f) + 12f));
            float width = Mathf.Max(dropdown.ButtonRect.rect.width, 220f);

            Vector3[] corners = new Vector3[4];
            dropdown.ButtonRect.GetWorldCorners(corners);
            Vector2 bottomLeft = _popupLayer.InverseTransformPoint(corners[0]);
            Vector2 topLeft = _popupLayer.InverseTransformPoint(corners[1]);

            Rect layerRect = _popupLayer.rect;
            bool hasRoomBelow = bottomLeft.y - height >= layerRect.yMin + 8f;
            float popupTop = hasRoomBelow
                ? bottomLeft.y - 4f
                : topLeft.y + height + 4f;
            Vector2 localPoint = new Vector2(bottomLeft.x, popupTop);

            localPoint.x = Mathf.Clamp(
                localPoint.x,
                layerRect.xMin + 8f,
                layerRect.xMax - width - 8f);
            localPoint.y = Mathf.Clamp(
                localPoint.y,
                layerRect.yMin + height + 8f,
                layerRect.yMax - 8f);

            listRect.anchorMin = new Vector2(0.5f, 0.5f);
            listRect.anchorMax = new Vector2(0.5f, 0.5f);
            listRect.pivot = new Vector2(0f, 1f);
            listRect.anchoredPosition = localPoint;
            listRect.sizeDelta = new Vector2(width, height);

            for (int i = 0; i < dropdown.Options.Length; i++)
            {
                int optionIndex = i;
                Color optionColor = optionIndex == dropdown.Value ? Accent : ControlBg;
                Button option = CreateButton(
                    listObject.transform,
                    dropdown.Options[i],
                    () =>
                    {
                        dropdown.SetValue(optionIndex);
                        HideDropdownPopup();
                    },
                    optionColor,
                    rowHeight,
                    12);
                Text label = option.GetComponentInChildren<Text>();
                if (label != null)
                    label.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void HideDropdownPopup()
        {
            if (_dropdownPopup == null)
                return;

            Destroy(_dropdownPopup);
            _dropdownPopup = null;
        }

        private InputField AddInputField(Transform parent, string label, string value, Action<string> onChanged)
        {
            GameObject group = CreateUiObject(label + "InputGroup", parent);
            HorizontalLayoutGroup row = group.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            LayoutElement groupLayout = group.AddComponent<LayoutElement>();
            groupLayout.preferredHeight = 38f;

            Text title = CreateText(label, group.transform, 12, FontStyle.Bold, TextColor);
            title.alignment = TextAnchor.MiddleLeft;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minWidth = 132f;
            titleLayout.preferredWidth = 132f;

            GameObject inputObject = CreateUiObject("Input", group.transform);
            Image image = inputObject.AddComponent<Image>();
            image.color = ControlBg;
            InputField input = inputObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.contentType = InputField.ContentType.IntegerNumber;

            Text text = CreateText(value, inputObject.transform, 13, FontStyle.Bold, TextColor);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(10f, 0f);
            text.rectTransform.offsetMax = new Vector2(-10f, 0f);
            text.alignment = TextAnchor.MiddleLeft;
            input.textComponent = text;
            input.text = value;
            input.onEndEdit.AddListener(v => onChanged?.Invoke(v));

            LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1f;
            inputLayout.minWidth = 140f;
            return input;
        }

        private Slider AddSlider(
            Transform parent,
            string label,
            float min,
            float max,
            float initial,
            Action<float> onChanged,
            Func<float> readValue,
            string format)
        {
            GameObject group = CreateUiObject(label + "SliderGroup", parent);
            VerticalLayoutGroup groupLayout = group.AddComponent<VerticalLayoutGroup>();
            groupLayout.spacing = 5;
            groupLayout.childControlWidth = true;
            groupLayout.childControlHeight = true;
            groupLayout.childForceExpandWidth = true;
            groupLayout.childForceExpandHeight = false;
            LayoutElement groupElement = group.AddComponent<LayoutElement>();
            groupElement.preferredHeight = 58f;

            GameObject header = CreateRow(group.transform, 18f);
            Text title = CreateText(label, header.transform, 12, FontStyle.Bold, TextColor);
            title.alignment = TextAnchor.MiddleLeft;
            Text valueText = CreateText("-", header.transform, 12, FontStyle.Bold, MutedText);
            valueText.alignment = TextAnchor.MiddleRight;
            valueText.name = "ValueText";

            Slider slider = CreateSlider(group.transform, min, max, initial);
            slider.onValueChanged.AddListener(value => onChanged?.Invoke(value));

            _dynamicRefreshers.Add(() =>
            {
                if (slider == null || valueText == null)
                    return;
                float value = readValue != null ? readValue() : slider.value;
                slider.SetValueWithoutNotify(value);
                valueText.text = FormatValue(value, format);
            });

            return slider;
        }

        private Slider CreateSlider(Transform parent, float min, float max, float value)
        {
            GameObject sliderObject = CreateUiObject("Slider", parent);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(0f, 28f);
            LayoutElement sliderLayout = sliderObject.AddComponent<LayoutElement>();
            sliderLayout.preferredHeight = 28f;

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            GameObject background = CreateUiObject("Background", sliderObject.transform);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.38f);
            backgroundRect.anchorMax = new Vector2(1f, 0.62f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = ControlBg;

            GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.38f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.62f);
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            GameObject fill = CreateUiObject("Fill", fillArea.transform);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            Stretch(fillRect);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = Accent;

            GameObject handleArea = CreateUiObject("Handle Slide Area", sliderObject.transform);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            GameObject handle = CreateUiObject("Handle", handleArea.transform);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            return slider;
        }

        private Text FindValueText(Slider slider)
        {
            if (slider == null)
                return null;

            Transform parent = slider.transform.parent;
            if (parent == null)
                return null;

            Text[] texts = parent.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == "ValueText")
                    return texts[i];
            }

            return null;
        }

        private Toggle CreateToggle(Transform parent, string label, Action<bool> onChanged)
        {
            GameObject toggleObject = CreateUiObject(label + "Toggle", parent);
            HorizontalLayoutGroup layout = toggleObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            LayoutElement rowElement = toggleObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 34f;

            GameObject boxObject = CreateUiObject("Box", toggleObject.transform);
            RectTransform boxRect = boxObject.GetComponent<RectTransform>();
            boxRect.sizeDelta = new Vector2(24f, 24f);
            Image boxImage = boxObject.AddComponent<Image>();
            boxImage.color = ControlBg;
            LayoutElement boxLayout = boxObject.AddComponent<LayoutElement>();
            boxLayout.minWidth = 24f;
            boxLayout.preferredWidth = 24f;

            GameObject checkObject = CreateUiObject("Check", boxObject.transform);
            RectTransform checkRect = checkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.22f, 0.22f);
            checkRect.anchorMax = new Vector2(0.78f, 0.78f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            Image checkImage = checkObject.AddComponent<Image>();
            checkImage.color = Accent;

            Text text = CreateText(label, toggleObject.transform, 12, FontStyle.Bold, TextColor);
            text.alignment = TextAnchor.MiddleLeft;
            LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
            textLayout.flexibleWidth = 1f;

            Toggle toggle = toggleObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.onValueChanged.AddListener(value => onChanged?.Invoke(value));
            return toggle;
        }

        private void BuildDropdownTemplate(Transform parent, Dropdown dropdown)
        {
            GameObject templateObject = CreateUiObject("Template", parent);
            RectTransform templateRect = templateObject.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, 220f);
            Image templateImage = templateObject.AddComponent<Image>();
            templateImage.color = new Color(0.045f, 0.055f, 0.060f, 0.99f);
            ScrollRect scrollRect = templateObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewportObject = CreateUiObject("Viewport", templateObject.transform);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = false;
            viewportObject.AddComponent<RectMask2D>();
            scrollRect.viewport = viewport;

            GameObject contentObject = CreateUiObject("Content", viewportObject.transform);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 220f);
            VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            scrollRect.content = content;

            GameObject itemObject = CreateUiObject("Item", contentObject.transform);
            RectTransform itemRect = itemObject.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0f, 32f);
            Image itemBackground = itemObject.AddComponent<Image>();
            itemBackground.color = ControlBg;
            Toggle itemToggle = itemObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;

            GameObject checkObject = CreateUiObject("Item Checkmark", itemObject.transform);
            RectTransform checkRect = checkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = new Vector2(8f, 0f);
            checkRect.sizeDelta = new Vector2(12f, 12f);
            Image checkImage = checkObject.AddComponent<Image>();
            checkImage.color = Accent;
            itemToggle.graphic = checkImage;

            Text itemLabel = CreateText("Option", itemObject.transform, 12, FontStyle.Normal, TextColor);
            itemLabel.alignment = TextAnchor.MiddleLeft;
            RectTransform itemLabelRect = itemLabel.rectTransform;
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(28f, 0f);
            itemLabelRect.offsetMax = new Vector2(-8f, 0f);

            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            templateObject.SetActive(false);
        }

        private GameObject CreateRow(Transform parent, float height)
        {
            GameObject row = CreateUiObject("Row", parent);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            return row;
        }

        private Button CreateButton(
            Transform parent,
            string text,
            Action action,
            Color color,
            float height,
            int fontSize)
        {
            GameObject buttonObject = CreateUiObject(text + "Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = BuildSelectableColors(color);
            button.onClick.AddListener(() => action?.Invoke());

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            Text label = CreateText(text, buttonObject.transform, fontSize, FontStyle.Bold, Color.white);
            RectTransform labelRect = label.rectTransform;
            Stretch(labelRect);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private Text CreateText(string text, Transform parent, int fontSize, FontStyle style, Color color)
        {
            GameObject textObject = CreateUiObject("Text", parent);
            Text label = textObject.AddComponent<Text>();
            label.font = _font;
            label.text = text;
            label.fontSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * UiFontScale));
            label.fontStyle = style;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static string FormatValue(float value, string format)
        {
            if (format.Contains("%"))
                return $"{value * 100.0f:F0}%";
            if (format.Contains("Pa.s"))
                return $"{value:F3} Pa.s";
            if (format.Contains("N/m"))
                return $"{value:F3} N/m";
            if (format.Contains("Pa"))
                return $"{value:F3} Pa";
            if (format.Contains("kg"))
                return $"{value:F2} kg";
            if (format.Contains("Hz"))
                return $"{value:F2} Hz";
            if (format.Contains("m/s"))
                return $"{value:F2} m/s";
            if (format.Contains("deg/s"))
                return $"{value:F0} deg/s";
            if (format.Contains("deg"))
                return $"{value:F0} deg";
            if (format.Contains("m"))
                return $"{value:F2} m";
            if (format.Contains("x"))
                return $"{value:F2}x";
            if (format == "0")
                return Mathf.RoundToInt(value).ToString("n0");
            return value.ToString("F2");
        }

        private static void AddSpacer(Transform parent)
        {
            GameObject spacer = CreateUiObject("Spacer", parent);
            LayoutElement layout = spacer.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = 40f;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static ColorBlock BuildSelectableColors(Color baseColor)
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.20f);
            colors.selectedColor = Color.Lerp(baseColor, Color.white, 0.16f);
            colors.disabledColor = new Color(0.20f, 0.20f, 0.20f, 0.45f);
            colors.colorMultiplier = 1.0f;
            return colors;
        }

        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.hideFlags = HideFlags.DontSave;
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}
