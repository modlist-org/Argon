using System;
using System.Collections.Generic;
using System.Linq;
using Argon.Api;
using Argon.Appearance;
using Argon.Compat;
using Argon.Hud;
using Argon.Integration;
using Argon.KeyViewer;
using Argon.Storage;
using O5Kit.Behaviour;
using O5Kit.Control;
using O5Kit.Core;
using O5Kit.Factory;
using O5Kit.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Argon.Localization;

namespace Argon.UI;

internal sealed class ArgonHost : MonoBehaviour
{
    private const string ToggleWindowShortcutId = "argon.toggle-settings-window";
    private const float KeyEditorPreviewScale = 1f;
    private static readonly int[] HandKeyOptions = { 10, 12, 16, 20 };
    private static readonly int[] FootKeyOptions = { 0, 2, 4, 6, 8, 16 };

    private O5WindowManager? _windowManager;
    private O5Window? _window;
    private HudRuntime? _hudRuntime;
    private KeyViewerRuntime? _keyViewer;
    private GameEventBridge? _eventBridge;
    private FontFallback? _fontFallback;
    private AppearanceCustomizer? _appearanceCustomizer;
    private ArgonStore? _store;
    private readonly List<O5Object> _controls = new List<O5Object>();
    private readonly Dictionary<string, RectTransform> _pages = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, O5Button> _navigationButtons = new Dictionary<string, O5Button>();
    private readonly List<KeyPreviewCell> _keyPreviewCells = new List<KeyPreviewCell>();
    private RectTransform? _keyEditorCanvas;
    private TMP_Text? _editorKpsText;
    private TMP_Text? _editorTotalText;
    private TMP_Text? _editorSelectedName;
    private TMP_Text? _editorZoomText;
    private string _activePage = "overview";
    private string? _selectedElementId;
    private int _selectedKeySlot;
    private bool _selectedKeyFoot;
    private bool _editingHud;
    private bool _editingGhostBindings;
    private int _keyViewerEditorTab;
    private UIScrollController? _inspectorScroll;
    private RectTransform? _inspectorRoot;
    private readonly List<O5Object> _inspectorControls = new List<O5Object>();
    private int _builtInspectorTab;
    private readonly float[] _inspectorOffsets = new float[4];
    private float _keyEditorZoom = 1f;
    private Vector2 _editorPan;
    private RectTransform? _editorGrid;
    private bool _editorSnap;
    private GeometryEdit? _pendingGeometry;
    private Vector2 _gestureDelta;
    private readonly List<GeometryEdit> _undoGeometry = new List<GeometryEdit>();
    private readonly List<GeometryEdit> _redoGeometry = new List<GeometryEdit>();
    private string _keyEditorTool = "select";
    private bool _savedCursorVisible;
    private CursorLockMode _savedCursorLock;
    private string _lastCaptureMessage = string.Empty;
    private TMP_Text? _captureStatus;
    private O5Toggle? _hudEditToggle;
    private O5Dropdown<KeyBindingChoice>? _keyBindingDropdown;
    private O5InputField? _keyBindingLabelInput;
    private KeyBindingChoice? _selectedBindingChoice;
    private TMP_Text? _pageTitle;
    private TMP_Text? _pageSubtitle;
    private RectTransform? _pagesHost;
    private int _apiRegistryRevision;
    private O5Theme? _previousTheme;
    private O5Theme? _argonTheme;
    private int _keyPreviewPaletteRevision;

    internal static ArgonHost Create()
    {
        var hostObject = new GameObject("Argon");
        Object.DontDestroyOnLoad(hostObject);
        var host = hostObject.AddComponent<ArgonHost>();
        try
        {
            host.Initialize();
            return host;
        }
        catch
        {
            Object.Destroy(hostObject);
            throw;
        }
    }

    private void Initialize()
    {
        _previousTheme = O5Boot.Theme;
        _argonTheme = _previousTheme with
        {
            PanelBG = new Color32(18, 19, 27, 255),
            TopBar = new Color32(24, 25, 35, 255),
            MenuBG = new Color32(22, 23, 33, 255),
            ObjectBG = new Color32(31, 33, 44, 255),
            ObjectButton = new Color32(40, 43, 57, 255),
            ObjectActive = new Color32(151, 133, 255, 255),
            ObjectActiveBright = new Color32(190, 179, 255, 255),
            ObjectInactive = new Color32(151, 133, 255, 110),
            MenuHover = new Color32(151, 133, 255, 45),
            CardHeader = new Color32(39, 40, 55, 255),
            CardPanel = new Color32(28, 29, 40, 255),
            CornerRadius = 13f,
            OutlineWidth = 1f,
            ControlHeight = 44f,
            FontSizeBody = 15f,
            FontSizeH1 = 24f,
        };
        O5Boot.EnsureDefaults(new O5Config { UIScale = 1f });
        O5Boot.SetTheme(_argonTheme);
        try
        {
            _fontFallback = new FontFallback(O5Boot.Fonts.Regular, O5Boot.Fonts.Medium, O5Boot.Fonts.Monospace);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Font fallback unavailable: {exception.Message}");
        }
        L.SetLanguage(L.Auto);
        _store = ArgonStore.Load();
        L.SetLanguage(_store.Document.Preferences.Language);
        L.Changed += OnLanguageChanged;
        GameApi.LogDetectedVersion();
        _hudRuntime = HudRuntime.Create(transform, _store);
        _keyViewer = new KeyViewerRuntime(_hudRuntime.CanvasTransform, _store);
        _appearanceCustomizer = new AppearanceCustomizer(_store);
        try
        {
            _eventBridge = new GameEventBridge(_store.Document.Preferences.Hud);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Game event bridge could not be initialized: {exception}");
        }

        _windowManager = O5WindowManager.Create(transform, "ArgonO5Windows");
        _window = _windowManager.Create(new O5WindowOptions
        {
            Id = "argon.settings",
            Title = "ARGON  /  HUD STUDIO",
            Size = new Vector2(1360f, 860f),
            Resizable = true,
            ClipContent = true,
            ShowOutline = false,
            Padding = new RectOffset(0, 0, 0, 0),
            TopBarHeight = 54f,
            TitleFontSize = 18f,
            PanelColor = new Color32(18, 19, 27, 255),
            TopBarColor = new Color32(25, 26, 37, 255),
        });
        _window.CloseRequested += OnCloseRequested;
        BuildWindowContent();
        _apiRegistryRevision = ArgonApi.RegistryRevision;

        O5ShortcutManager.Register(
            ToggleWindowShortcutId,
            new O5KeyCombo(KeyCode.O, KeyCode.LeftControl, KeyCode.LeftShift),
            onPressed: _ => ToggleWindow());
    }

    private void BuildWindowContent()
    {
        if (_window == null)
        {
            return;
        }

        if (_inspectorScroll != null && _inspectorScroll.content != null)
            _inspectorOffsets[_builtInspectorTab] = _inspectorScroll.content.anchoredPosition.y;
        _inspectorScroll = null;
        _inspectorRoot = null;
        _inspectorControls.Clear();
        DisposeControls();
        _pages.Clear();
        _navigationButtons.Clear();
        _keyPreviewCells.Clear();
        _keyEditorCanvas = null;
        _editorKpsText = null;
        _editorTotalText = null;
        _editorSelectedName = null;
        _editorZoomText = null;
        _captureStatus = null;
        _keyBindingDropdown = null;
        _keyBindingLabelInput = null;
        _selectedBindingChoice = null;
        _pageTitle = null;
        _pageSubtitle = null;
        for (var i = _window.Content.childCount - 1; i >= 0; i--)
        {
            var child = _window.Content.GetChild(i);
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Object.Destroy(child.gameObject);
        }

        var pageHost = BuildAppShell();
        _pagesHost = pageHost;
        BuildOverviewPage(CreateScrollPage("overview"));
        var hudPage = CreateScrollPage("hud");
        BuildHudPage(hudPage);
        BuildHudDetailsPage(hudPage);
        BuildHudColorsPage(hudPage);
        var keyViewerPage = CreateEditorPage("keyviewer");
        BuildKeyViewerPage(keyViewerPage);
        BuildLayoutPage(CreateScrollPage("layout"));
        BuildAppearancePage(CreateScrollPage("appearance"));
        BuildGeneralPage(CreateScrollPage("general"));
        ShowPage(_activePage);
    }

    private RectTransform BuildAppShell()
    {
        var content = _window!.Content;
        var sidebarObject = new GameObject("ArgonSidebar");
        sidebarObject.transform.SetParent(content, false);
        var sidebarRect = sidebarObject.AddComponent<RectTransform>();
        sidebarRect.anchorMin = new Vector2(0f, 0f);
        sidebarRect.anchorMax = new Vector2(0f, 1f);
        sidebarRect.pivot = new Vector2(0f, 0.5f);
        sidebarRect.sizeDelta = new Vector2(208f, 0f);
        var sidebarImage = sidebarObject.AddComponent<Image>();
        sidebarImage.color = O5Boot.Theme.MenuBG;
        sidebarImage.raycastTarget = false;
        var sidebarLayout = sidebarObject.AddComponent<VerticalLayoutGroup>();
        sidebarLayout.padding = new RectOffset(16, 16, 20, 16);
        sidebarLayout.spacing = 6f;
        sidebarLayout.childAlignment = TextAnchor.UpperLeft;
        sidebarLayout.childControlWidth = true;
        sidebarLayout.childControlHeight = true;
        sidebarLayout.childForceExpandWidth = true;
        sidebarLayout.childForceExpandHeight = false;

        var brand = O5Factory.Row(sidebarObject.transform, 58f);
        var brandTitle = O5Factory.ControlTextH1(brand);
        brandTitle.text = "ARGON";
        brandTitle.fontSize = 28f;
        brandTitle.color = new Color32(187, 170, 255, 255);
        brandTitle.characterSpacing = 1f;
        brandTitle.rectTransform.anchorMin = new Vector2(0f, 0.43f);
        brandTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        brandTitle.rectTransform.offsetMin = new Vector2(2f, 0f);
        brandTitle.rectTransform.offsetMax = Vector2.zero;
        var brandSubtitle = O5Factory.ControlText(brand, 11f, true);
        brandSubtitle.text = "ADOFAI  ·  HUD STUDIO";
        brandSubtitle.color = new Color32(147, 149, 169, 255);
        brandSubtitle.characterSpacing = 0f;
        brandSubtitle.rectTransform.anchorMin = new Vector2(0f, 0f);
        brandSubtitle.rectTransform.anchorMax = new Vector2(1f, 0.43f);
        brandSubtitle.rectTransform.offsetMin = new Vector2(2f, 0f);
        brandSubtitle.rectTransform.offsetMax = Vector2.zero;

        AddSidebarGroupLabel(sidebarObject.transform, L.T("nav.group.start"));
        AddNavigationButton(sidebarObject.transform, L.T("nav.overview"), "overview");
        AddSidebarGroupLabel(sidebarObject.transform, L.T("nav.group.modules"));
        AddNavigationButton(sidebarObject.transform, "HUD", "hud");
        AddNavigationButton(sidebarObject.transform, L.T("nav.keyviewer"), "keyviewer");
        AddSidebarGroupLabel(sidebarObject.transform, L.T("nav.group.studio"));
        AddNavigationButton(sidebarObject.transform, L.T("nav.layout"), "layout");
        AddNavigationButton(sidebarObject.transform, L.T("nav.appearance"), "appearance");
        AddSidebarGroupLabel(sidebarObject.transform, L.T("nav.group.settings"));
        AddNavigationButton(sidebarObject.transform, L.T("nav.general"), "general");

        var spacer = new GameObject("SidebarSpacer");
        spacer.transform.SetParent(sidebarObject.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var shortcut = O5Factory.Row(sidebarObject.transform, 54f);
        var shortcutBg = shortcut.gameObject.AddComponent<Image>();
        shortcutBg.sprite = O5Boot.Sprites.RoundedControl;
        shortcutBg.type = Image.Type.Sliced;
        shortcutBg.color = new Color32(39, 38, 57, 255);
        var shortcutText = O5Factory.ControlText(shortcut, 13f);
        shortcutText.text = L.T("sidebar.shortcut");
        shortcutText.characterSpacing = 0f;
        shortcutText.textWrappingMode = TextWrappingModes.NoWrap;

        var mainObject = new GameObject("ArgonMainContent");
        mainObject.transform.SetParent(content, false);
        var mainRect = mainObject.AddComponent<RectTransform>();
        mainRect.anchorMin = Vector2.zero;
        mainRect.anchorMax = Vector2.one;
        mainRect.offsetMin = new Vector2(224f, 0f);
        mainRect.offsetMax = Vector2.zero;

        var headerObject = new GameObject("ArgonPageHeader");
        headerObject.transform.SetParent(mainRect, false);
        var headerRect = headerObject.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, 78f);
        var title = O5Factory.ControlTextH1(headerRect);
        title.fontSize = 24f;
        title.characterSpacing = 0f;
        title.rectTransform.anchorMin = new Vector2(0f, 0.42f);
        title.rectTransform.anchorMax = new Vector2(0.72f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;
        _pageTitle = title;
        var subtitle = O5Factory.ControlText(headerRect, 13f, true);
        subtitle.color = new Color32(155, 157, 177, 255);
        subtitle.characterSpacing = 0f;
        subtitle.rectTransform.anchorMin = new Vector2(0f, 0f);
        subtitle.rectTransform.anchorMax = new Vector2(1f, 0.46f);
        subtitle.rectTransform.offsetMin = Vector2.zero;
        subtitle.rectTransform.offsetMax = new Vector2(-164f, 0f);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.enableAutoSizing = true;
        subtitle.fontSizeMin = 11f;
        subtitle.fontSizeMax = 13f;
        _pageSubtitle = subtitle;

        var status = new GameObject("LiveStatus");
        status.transform.SetParent(headerRect, false);
        var statusRect = status.AddComponent<RectTransform>();
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.anchoredPosition = new Vector2(-4f, 0f);
        statusRect.sizeDelta = new Vector2(150f, 30f);
        var statusImage = status.AddComponent<Image>();
        statusImage.sprite = O5Boot.Sprites.RoundedControl;
        statusImage.type = Image.Type.Sliced;
        statusImage.color = new Color32(39, 51, 54, 255);
        statusImage.raycastTarget = false;
        var statusText = O5Factory.ControlText(statusRect, 12f, true);
        statusText.text = L.T("preview.live");
        statusText.color = new Color32(125, 222, 174, 255);
        statusText.characterSpacing = 0.3f;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.rectTransform.offsetMin = Vector2.zero;
        statusText.rectTransform.offsetMax = Vector2.zero;

        var dividerObject = new GameObject("HeaderDivider");
        dividerObject.transform.SetParent(mainRect, false);
        var dividerRect = dividerObject.AddComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -78f);
        dividerRect.sizeDelta = new Vector2(0f, 1f);
        dividerObject.AddComponent<Image>().color = new Color32(52, 53, 69, 255);

        var pageHost = new GameObject("ArgonPages");
        pageHost.transform.SetParent(mainRect, false);
        var pageHostRect = pageHost.AddComponent<RectTransform>();
        pageHostRect.anchorMin = Vector2.zero;
        pageHostRect.anchorMax = Vector2.one;
        pageHostRect.offsetMin = Vector2.zero;
        pageHostRect.offsetMax = new Vector2(0f, -88f);
        return pageHostRect;
    }

    private void AddSidebarGroupLabel(Transform parent, string text)
    {
        var row = O5Factory.Row(parent, 22f);
        var label = O5Factory.ControlText(row, 11f, true);
        label.text = text;
        label.font = O5Boot.Fonts.Medium;
        label.color = new Color32(119, 121, 143, 255);
        label.characterSpacing = 1.1f;
        label.rectTransform.offsetMin = new Vector2(3f, 0f);
    }

    private void BuildOverviewPage(RectTransform page)
    {
        var welcome = CreateSettingsCard(page, L.T("overview.welcome.title"), L.T("overview.welcome.subtitle"));
        var welcomeTextRow = O5Factory.Row(welcome, 58f);
        var welcomeText = O5Factory.ControlText(welcomeTextRow, 14f, true);
        welcomeText.text = L.T("overview.welcome.body");
        welcomeText.color = new Color32(183, 185, 202, 255);
        welcomeText.characterSpacing = 0f;
        welcomeText.textWrappingMode = TextWrappingModes.Normal;
        welcomeText.alignment = TextAlignmentOptions.TopLeft;
        AddButtonRow(welcome, L.T("overview.open-hud"), () => ShowPage("hud"), "overview.open-hud");

        AddSection(page, L.T("overview.workspace.title"), L.T("overview.workspace.subtitle"));
        var statRow = O5Factory.Row(page, 104f);
        var statLayout = statRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        statLayout.spacing = 12f;
        statLayout.childControlWidth = true;
        statLayout.childControlHeight = true;
        statLayout.childForceExpandWidth = true;
        statLayout.childForceExpandHeight = true;
        var enabledModules = _hudRuntime?.ActiveElements.Count(element => element.Enabled) ?? 0;
        var layoutCount = _hudRuntime?.Layouts.Count ?? 0;
        var keySettings = _store!.Document.Preferences.KeyViewer;
        AddStatCard(statRow, L.T("overview.stat.hud"), enabledModules.ToString(), L.T("overview.stat.hud-caption"));
        AddStatCard(statRow, L.T("overview.stat.lanes"), keySettings.HandKeyCount + " + " + keySettings.FootKeyCount, L.T("overview.stat.lanes-caption"));
        AddStatCard(statRow, L.T("nav.layout"), layoutCount.ToString(), L.T("overview.stat.layouts-caption"));

        AddSection(page, L.T("overview.quick.title"), L.T("overview.quick.subtitle"));
        var quickRow = O5Factory.Row(page, 48f);
        var quickLayout = quickRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        quickLayout.spacing = 10f;
        quickLayout.childControlWidth = true;
        quickLayout.childControlHeight = true;
        quickLayout.childForceExpandWidth = true;
        quickLayout.childForceExpandHeight = false;
        AddButtonRow(quickRow, L.T("overview.open-keyviewer"), () => ShowPage("keyviewer"), "overview.open-keyviewer");
        AddButtonRow(quickRow, L.T("overview.open-layouts"), () => ShowPage("layout"), "overview.open-layouts");
        AddButtonRow(quickRow, L.T("nav.appearance"), () => ShowPage("appearance"), "overview.open-appearance");
    }

    private RectTransform CreateSettingsCard(Transform parent, string title, string description)
    {
        var card = O5Factory.Card(parent, title, true, null, null, showDeleteButton: false, showActiveToggle: false);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descriptionRow = O5Factory.Row(card.contentRect, 46f);
            ConfigureFlowRow(descriptionRow, 10);
            var descriptionText = O5Factory.ControlText(descriptionRow, 13f, true);
            descriptionText.text = description;
            descriptionText.color = new Color32(157, 159, 177, 255);
            descriptionText.characterSpacing = 0f;
            descriptionText.textWrappingMode = TextWrappingModes.Normal;
        }

        return card.contentRect;
    }

    private void AddStatCard(Transform parent, string labelText, string valueText, string detail)
    {
        var card = new GameObject("WorkspaceStat");
        card.transform.SetParent(parent, false);
        var rect = card.AddComponent<RectTransform>();
        var layout = card.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.preferredHeight = 96f;
        var image = card.AddComponent<Image>();
        image.sprite = O5Boot.Sprites.RoundedControl;
        image.type = Image.Type.Sliced;
        image.color = O5Boot.Theme.CardPanel;

        var label = O5Factory.ControlText(rect, 11f, true);
        label.text = labelText;
        label.color = new Color32(146, 148, 168, 255);
        label.characterSpacing = 0.8f;
        label.rectTransform.anchorMin = new Vector2(0f, 0.66f);
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(14f, 0f);
        label.rectTransform.offsetMax = new Vector2(-8f, -8f);
        var value = O5Factory.ControlTextH1(rect);
        value.text = valueText;
        value.fontSize = 25f;
        value.color = new Color32(227, 222, 255, 255);
        value.characterSpacing = 0f;
        value.rectTransform.anchorMin = new Vector2(0f, 0.28f);
        value.rectTransform.anchorMax = new Vector2(1f, 0.68f);
        value.rectTransform.offsetMin = new Vector2(14f, 0f);
        value.rectTransform.offsetMax = new Vector2(-8f, 0f);
        var hint = O5Factory.ControlText(rect, 11f, true);
        hint.text = detail;
        hint.color = new Color32(132, 134, 153, 255);
        hint.characterSpacing = 0f;
        hint.rectTransform.anchorMin = Vector2.zero;
        hint.rectTransform.anchorMax = new Vector2(1f, 0.28f);
        hint.rectTransform.offsetMin = new Vector2(14f, 5f);
        hint.rectTransform.offsetMax = new Vector2(-8f, 0f);
    }

    private void AddButtonRow(Transform parent, string text, Action action, string id)
    {
        var button = O5Factory.Button(parent, action, text, id);
        button.NormalColor = O5Boot.Theme.ObjectButton;
        button.UpdateVisual(true);
        Track(button);
    }

    private void BuildHudPage(RectTransform page)
    {
        var moduleCard = CreateSettingsCard(page, L.T("hud.modules.title"), L.T("hud.modules.subtitle"));
        var editToggle = O5Factory.Toggle(
            moduleCard,
            false,
            _editingHud,
            SetHudEditMode,
            L.T("hud.edit-mode"),
            "argon.hud.edit-mode");
        _hudEditToggle = editToggle;
        Track(editToggle);

        var elements = _hudRuntime?.ActiveElements.OrderBy(element => element.Order).ToArray()
                       ?? Array.Empty<HudElementLayoutData>();
        if (elements.Length == 0)
        {
            AddSection(moduleCard, L.T("hud.modules.empty.title"), L.T("hud.modules.empty.body"));
        }
        else
        {
            const int columns = 3;
            const float cellHeight = 194f;
            const float rowSpacing = 12f;
            var rows = Mathf.CeilToInt((float)elements.Length / columns);
            var gridObject = new GameObject("HudModuleGrid");
            gridObject.transform.SetParent(moduleCard, false);
            var gridRect = gridObject.AddComponent<RectTransform>();
            var gridLayoutElement = gridObject.AddComponent<LayoutElement>();
            gridLayoutElement.minHeight = gridLayoutElement.preferredHeight = rows * cellHeight + (rows - 1) * rowSpacing;
            var grid = gridObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(256f, cellHeight);
            grid.spacing = new Vector2(12f, rowSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;

            foreach (var element in elements)
            {
                AddHudModuleCard(gridRect, element);
            }
        }

        var metricsCard = CreateSettingsCard(page, L.T("hud.metrics.title"), L.T("hud.metrics.subtitle"));
        var preferences = _store!.Document.Preferences.Hud;
        AddPreferenceToggle(metricsCard, L.T("hud.potential"), preferences.ShowPotentialValues, value => preferences.ShowPotentialValues = value, "hud.potential");
        AddPreferenceToggle(metricsCard, L.T("hud.absolute-accuracy"), preferences.ShowAbsoluteAccuracy, value => preferences.ShowAbsoluteAccuracy = value, "hud.absolute-accuracy");
        AddPreferenceToggle(metricsCard, "X-Score", preferences.ShowXScore, value => preferences.ShowXScore = value, "hud.xscore");
        AddPreferenceToggle(metricsCard, L.T("hud.music-time"), preferences.ShowMusicTime, value => preferences.ShowMusicTime = value, "hud.music-time");
        AddPreferenceToggle(metricsCard, L.T("hud.map-time"), preferences.ShowMapTime, value => preferences.ShowMapTime = value, "hud.map-time");
        AddPreferenceToggle(metricsCard, L.T("hud.checkpoint"), preferences.ShowCheckpoint, value => preferences.ShowCheckpoint = value, "hud.checkpoint");
        AddPreferenceToggle(metricsCard, L.T("hud.best"), preferences.ShowBestProgress, value => preferences.ShowBestProgress = value, "hud.best");
        AddPreferenceToggle(metricsCard, L.T("hud.attempts"), preferences.ShowAttempts, value => preferences.ShowAttempts = value, "hud.attempts");
        AddPreferenceToggle(metricsCard, L.T("hud.author"), preferences.ShowAuthor, value => preferences.ShowAuthor = value, "hud.author");
        AddPreferenceToggle(metricsCard, L.T("hud.state"), preferences.ShowState, value => preferences.ShowState = value, "hud.state");
        AddPreferenceToggle(metricsCard, L.T("hud.death"), preferences.ShowDeath, value => preferences.ShowDeath = value, "hud.death");
        AddPreferenceToggle(metricsCard, L.T("hud.start"), preferences.ShowStart, value => preferences.ShowStart = value, "hud.start");
        AddPreferenceToggle(metricsCard, L.T("hud.hide-auto"), preferences.HideHudDuringAuto, value => preferences.HideHudDuringAuto = value, "hud.hide-auto");
        AddPreferenceToggle(metricsCard, L.T("hud.tile-info"), preferences.ShowTileInfo, value => preferences.ShowTileInfo = value, "hud.tile-info");
        AddPreferenceToggle(metricsCard, L.T("hud.hide-debug"), preferences.HideDebugText, value => preferences.HideDebugText = value, "hud.hide-debug");
    }

    private void AddHudModuleCard(Transform parent, HudElementLayoutData element)
    {
        var tileObject = new GameObject("HudModule_" + element.InstanceId);
        tileObject.transform.SetParent(parent, false);
        var tileRect = tileObject.AddComponent<RectTransform>();
        var tileImage = tileObject.AddComponent<Image>();
        tileImage.sprite = O5Boot.Sprites.RoundedControl;
        tileImage.type = Image.Type.Sliced;
        tileImage.color = O5Boot.Theme.CardPanel;

        var layout = tileObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 7f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var iconRow = O5Factory.Row(tileRect, 44f);
        var iconObject = new GameObject("ModuleIcon");
        iconObject.transform.SetParent(iconRow, false);
        var iconRect = iconObject.AddComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(40f, 40f);
        var iconImage = iconObject.AddComponent<Image>();
        iconImage.sprite = O5Boot.Sprites.Circle;
        iconImage.color = element.Enabled
            ? new Color32(112, 94, 197, 255)
            : new Color32(66, 67, 83, 255);
        var iconText = O5Factory.ControlText(iconRect, 15f, true);
        iconText.text = GetModuleMonogram(_hudRuntime?.GetElementName(element.ElementId) ?? element.ElementId);
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.characterSpacing = 0f;
        iconText.color = new Color32(244, 242, 255, 255);
        iconText.raycastTarget = false;

        var titleRow = O5Factory.Row(tileRect, 25f);
        var title = O5Factory.ControlText(titleRow, 15f, true);
        title.text = _hudRuntime?.GetElementName(element.ElementId) ?? element.ElementId;
        title.alignment = TextAlignmentOptions.Center;
        title.characterSpacing = 0f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.font = O5Boot.Fonts.Medium;

        var spacer = new GameObject("ModuleCardSpacer");
        spacer.transform.SetParent(tileRect, false);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        var capturedId = element.InstanceId;
        var options = O5Factory.Button(tileRect, () =>
        {
            _selectedElementId = capturedId;
            _activePage = "layout";
            BuildWindowContent();
        }, L.T("hud.placement-options"), "argon.hud.options." + capturedId);
        SetCompactButton(options, 34f, new Color32(54, 55, 72, 255));

        var enabled = element.Enabled;
        O5Button? stateButton = null;
        stateButton = O5Factory.Button(tileRect, () =>
        {
            enabled = !enabled;
            _hudRuntime?.SetEnabled(capturedId, enabled);
            if (stateButton?.Label != null)
            {
                stateButton.Label.text = enabled ? L.T("common.on") : L.T("common.off");
            }
            if (stateButton != null)
            {
                stateButton.NormalColor = enabled
                    ? new Color32(40, 133, 91, 255)
                    : new Color32(84, 48, 61, 255);
                stateButton.UpdateVisual(true);
            }
            iconImage.color = enabled
                ? new Color32(112, 94, 197, 255)
                : new Color32(66, 67, 83, 255);
        }, enabled ? L.T("common.on") : L.T("common.off"), "argon.hud.enabled." + capturedId);
        SetCompactButton(stateButton, 34f, enabled
            ? new Color32(40, 133, 91, 255)
            : new Color32(84, 48, 61, 255));

        Track(options);
        Track(stateButton);
    }

    private static void SetCompactButton(O5Button button, float height, Color color)
    {
        var layout = button.Rect.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minHeight = height;
            layout.preferredHeight = height;
        }
        if (button.Label != null)
        {
            button.Label.fontSize = 12f;
            button.Label.characterSpacing = 0.4f;
        }
        button.NormalColor = color;
        button.UpdateVisual(true);
    }

    private static string GetModuleMonogram(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length == 0) return "H";
        return trimmed.Substring(0, Math.Min(2, trimmed.Length)).ToUpperInvariant();
    }

    private void BuildHudDetailsPage(RectTransform page)
    {
        var preferences = _store!.Document.Preferences.Hud;
        var judgementCard = CreateSettingsCard(page, L.T("hud.judgement.title"), L.T("hud.judgement.subtitle"));
        AddPreferenceToggle(judgementCard, L.T("hud.combo"), preferences.ShowCombo, value => preferences.ShowCombo = value, "hud.combo");
        AddPreferenceToggle(judgementCard, L.T("hud.pure-combo"), preferences.ShowPurePerfectCombo, value => preferences.ShowPurePerfectCombo = value, "hud.pure-combo");
        AddPreferenceToggle(judgementCard, L.T("hud.auto-combo"), preferences.CountAutoInCombo, value => preferences.CountAutoInCombo = value, "hud.auto-combo");
        AddDropdown(judgementCard, 1, preferences.ComboMinimumTier, new[] { 0, 1, 2 }, value => value switch { 0 => L.T("hud.combo-tier.0"), 1 => L.T("hud.combo-tier.1"), _ => L.T("hud.combo-tier.2") }, value => preferences.ComboMinimumTier = value, "hud.combo-tier");
        AddPreferenceToggle(judgementCard, L.T("hud.judgement"), preferences.ShowJudgement, value => preferences.ShowJudgement = value, "hud.judgement");
        AddPreferenceToggle(judgementCard, L.T("hud.timing"), preferences.ShowTiming, value => preferences.ShowTiming = value, "hud.timing");
        AddPreferenceToggle(judgementCard, L.T("hud.timing-scale"), preferences.ShowTimingScale, value => preferences.ShowTimingScale = value, "hud.timing-scale");
        AddPreferenceToggle(judgementCard, L.T("hud.pseudo-bpm"), preferences.UsePseudoBpm, value => preferences.UsePseudoBpm = value, "hud.pseudo-bpm");

        var formatCard = CreateSettingsCard(page, L.T("hud.format.title"), L.T("hud.format.subtitle"));
        AddSlider(formatCard, L.T("hud.accuracy-decimals"), 2f, 0f, 4f, preferences.AccuracyDecimals, value => preferences.AccuracyDecimals = Mathf.RoundToInt(value), "hud.accuracy-decimals", "F0");
        AddSlider(formatCard, L.T("hud.progress-decimals"), 2f, 0f, 4f, preferences.ProgressDecimals, value => preferences.ProgressDecimals = Mathf.RoundToInt(value), "hud.progress-decimals", "F0");
        AddSlider(formatCard, L.T("hud.bpm-decimals"), 2f, 0f, 4f, preferences.BpmDecimals, value => preferences.BpmDecimals = Mathf.RoundToInt(value), "hud.bpm-decimals", "F0");
        AddSlider(formatCard, L.T("hud.timing-decimals"), 2f, 0f, 5f, preferences.TimingDecimals, value => preferences.TimingDecimals = Mathf.RoundToInt(value), "hud.timing-decimals", "F0");
        AddSlider(formatCard, L.T("hud.timing-range"), 150f, 10f, 1000f, preferences.TimingScaleMilliseconds, value => preferences.TimingScaleMilliseconds = value, "hud.timing-range", "F0");
        AddSlider(formatCard, L.T("hud.combo-color-max"), 1000f, 1f, 10000f, preferences.ComboColorMax, value => preferences.ComboColorMax = Mathf.RoundToInt(value), "hud.combo-color-max", "F0");
    }

    private void BuildHudColorsPage(RectTransform page)
    {
        var preferences = _store!.Document.Preferences.Hud;
        var colorCard = CreateSettingsCard(page, L.T("hud.colors.title"), L.T("hud.colors.subtitle"));
        AddPreferenceToggle(colorCard, L.T("hud.gradient-enabled"), preferences.UseColorGradients, value => preferences.UseColorGradients = value, "hud.gradient-enabled");
        AddColorPicker(colorCard, L.T("hud.progress-low"), preferences.ProgressLowColor, value => preferences.ProgressLowColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progress-low");
        AddColorPicker(colorCard, L.T("hud.progress-mid"), preferences.ProgressMidColor, value => preferences.ProgressMidColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progress-mid");
        AddColorPicker(colorCard, L.T("hud.progress-high"), preferences.ProgressHighColor, value => preferences.ProgressHighColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progress-high");
        AddColorPicker(colorCard, L.T("hud.bpm-low"), preferences.BpmLowColor, value => preferences.BpmLowColor = ColorUtility.ToHtmlStringRGBA(value), "hud.bpm-low");
        AddColorPicker(colorCard, L.T("hud.bpm-mid"), preferences.BpmMidColor, value => preferences.BpmMidColor = ColorUtility.ToHtmlStringRGBA(value), "hud.bpm-mid");
        AddColorPicker(colorCard, L.T("hud.bpm-high"), preferences.BpmHighColor, value => preferences.BpmHighColor = ColorUtility.ToHtmlStringRGBA(value), "hud.bpm-high");
        AddColorPicker(colorCard, L.T("hud.timing-good"), preferences.TimingGoodColor, value => preferences.TimingGoodColor = ColorUtility.ToHtmlStringRGBA(value), "hud.timing-good");
        AddColorPicker(colorCard, L.T("hud.timing-bad"), preferences.TimingBadColor, value => preferences.TimingBadColor = ColorUtility.ToHtmlStringRGBA(value), "hud.timing-bad");
        AddColorPicker(colorCard, L.T("hud.combo-low"), preferences.ComboLowColor, value => preferences.ComboLowColor = ColorUtility.ToHtmlStringRGBA(value), "hud.combo-low");
        AddColorPicker(colorCard, L.T("hud.combo-high"), preferences.ComboHighColor, value => preferences.ComboHighColor = ColorUtility.ToHtmlStringRGBA(value), "hud.combo-high");
        AddColorPicker(colorCard, L.T("hud.combo-perfect"), preferences.ComboPerfectColor, value => preferences.ComboPerfectColor = ColorUtility.ToHtmlStringRGBA(value), "hud.combo-perfect");
        AddColorPicker(colorCard, L.T("hud.combo-early-late"), preferences.ComboEarlyLateColor, value => preferences.ComboEarlyLateColor = ColorUtility.ToHtmlStringRGBA(value), "hud.combo-early-late");
        AddColorPicker(colorCard, L.T("hud.pure-perfect-color"), preferences.PurePerfectColor, value => preferences.PurePerfectColor = ColorUtility.ToHtmlStringRGBA(value), "hud.pure-perfect-color");
        AddColorPicker(colorCard, L.T("hud.progressbar-fill"), preferences.ProgressBarFillColor, value => preferences.ProgressBarFillColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progressbar-fill");
        AddColorPicker(colorCard, L.T("hud.progressbar-background"), preferences.ProgressBarBackgroundColor, value => preferences.ProgressBarBackgroundColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progressbar-background");
        AddColorPicker(colorCard, L.T("hud.progressbar-border"), preferences.ProgressBarBorderColor, value => preferences.ProgressBarBorderColor = ColorUtility.ToHtmlStringRGBA(value), "hud.progressbar-border");
        AddColorPicker(colorCard, L.T("hud.text-color"), preferences.TextColor, value => preferences.TextColor = ColorUtility.ToHtmlStringRGBA(value), "hud.text-color");
    }

    private void BuildKeyViewerPage(RectTransform page)
    {
        var settings = _store!.Document.Preferences.KeyViewer;
        BuildKeyViewerEditorStage(page, settings);
        BuildKeyViewerEditorToolbar(page, settings);
    }

    private void BuildKeyViewerEditorStage(RectTransform page, KeyViewerPreferences settings)
    {
        var stageObject = new GameObject("DmNoteStyleEditor");
        stageObject.transform.SetParent(page, false);
        var stageRect = stageObject.AddComponent<RectTransform>();
        stageRect.anchorMin = Vector2.zero;
        stageRect.anchorMax = Vector2.one;
        stageRect.offsetMin = new Vector2(0f, 76f);
        stageRect.offsetMax = Vector2.zero;
        var stageLayout = stageObject.AddComponent<HorizontalLayoutGroup>();
        stageLayout.spacing = 1f;
        stageLayout.childControlWidth = true;
        stageLayout.childControlHeight = true;
        stageLayout.childForceExpandWidth = false;
        stageLayout.childForceExpandHeight = true;

        var canvas = CreateEditorPanel(stageRect, "EditorCanvas", 0f, 1f);
        canvas.GetComponent<LayoutElement>().minWidth = 180f;
        var canvasLayout = canvas.gameObject.AddComponent<VerticalLayoutGroup>();
        canvasLayout.padding = new RectOffset(0, 0, 0, 0);
        canvasLayout.spacing = 0f;
        canvasLayout.childControlWidth = true;
        canvasLayout.childControlHeight = true;
        canvasLayout.childForceExpandWidth = true;
        canvasLayout.childForceExpandHeight = false;

        var titleRow = O5Factory.Row(canvas, 40f);
        var canvasTitle = O5Factory.ControlText(titleRow, 12f, true);
        canvasTitle.text = L.T("keyviewer.canvas.title");
        canvasTitle.rectTransform.offsetMax = new Vector2(-150f, 0f);
        canvasTitle.font = O5Boot.Fonts.Medium;
        canvasTitle.color = new Color32(170, 169, 190, 255);
        canvasTitle.characterSpacing = 1f;
        CreateEditorZoomButton(titleRow, "−", -0.1f, -112f);
        _editorZoomText = O5Factory.ControlText(titleRow, 12f, true);
        _editorZoomText.text = Mathf.RoundToInt(_keyEditorZoom * 100f) + "%";
        _editorZoomText.alignment = TextAlignmentOptions.Center;
        _editorZoomText.color = new Color32(170, 169, 190, 255);
        _editorZoomText.rectTransform.anchorMin = _editorZoomText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        _editorZoomText.rectTransform.pivot = new Vector2(1f, 0.5f);
        _editorZoomText.rectTransform.anchoredPosition = new Vector2(-42f, 0f);
        _editorZoomText.rectTransform.sizeDelta = new Vector2(64f, 28f);
        CreateEditorZoomButton(titleRow, "+", 0.1f, -6f);

        var viewportObject = new GameObject("EditorGridViewport");
        viewportObject.transform.SetParent(canvas, false);
        var viewportRect = viewportObject.AddComponent<RectTransform>();
        var viewportSize = viewportObject.AddComponent<LayoutElement>();
        viewportSize.flexibleHeight = 1f;
        viewportSize.minHeight = 100f;
        viewportSize.preferredHeight = 380f;
        var viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.sprite = O5Boot.Sprites.RoundedControl;
        viewportImage.type = Image.Type.Sliced;
        viewportImage.color = new Color32(24, 24, 26, 255);
        viewportImage.raycastTarget = true;
        viewportObject.AddComponent<KeyEditorCanvasHandler>().Initialize(this);
        viewportObject.AddComponent<RectMask2D>();
        _keyEditorCanvas = viewportRect;
        var gridObject = new GameObject("CanvasGrid");
        gridObject.transform.SetParent(viewportRect, false);
        _editorGrid = gridObject.AddComponent<RectTransform>();
        _editorGrid.anchorMin = Vector2.zero;
        _editorGrid.anchorMax = Vector2.one;
        _editorGrid.sizeDelta = Vector2.zero;
        CreateEditorGrid(_editorGrid);
        CreateEditorStats(viewportRect);
        BuildEditorPreviewKeys(viewportRect, settings.HandKeyCount, false);
        if (settings.FootKeyCount > 0) BuildEditorPreviewKeys(viewportRect, settings.FootKeyCount, true);
        var canvasFooter = O5Factory.Row(canvas, 24f);
        var hint = O5Factory.ControlText(canvasFooter, 11f, true);
        hint.text = L.T("keyviewer.canvas.hint");
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.color = new Color32(137, 139, 159, 255);
        hint.characterSpacing = 0f;

        var inspector = CreateEditorPanel(stageRect, "PropertyInspector", 340f, 0f);
        var inspectorElement = inspector.GetComponent<LayoutElement>();
        inspectorElement.minWidth = 300f;
        inspectorElement.preferredWidth = 340f;
        inspectorElement.flexibleWidth = 0f;
        BuildKeyViewerInspector(inspector, settings);
        RefreshEditorPreviewLayout();
        RefreshKeyPreview(true);
    }

    private RectTransform CreateEditorPanel(Transform parent, string name, float preferredWidth, float flexibleWidth)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        var layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = flexibleWidth;
        var image = root.AddComponent<Image>();
        image.sprite = O5Boot.Sprites.RoundedControl;
        image.type = Image.Type.Sliced;
        image.color = new Color32(26, 26, 28, 255);
        image.raycastTarget = false;
        return rect;
    }

    private static void CreateEditorGrid(RectTransform viewport)
    {
        for (var x = -1200f; x <= 1200f; x += 10f)
        {
            var line = CreateEditorGridLine(viewport, true);
            line.anchoredPosition = new Vector2(x, 0f);
        }

        for (var y = -800f; y <= 800f; y += 10f)
        {
            var line = CreateEditorGridLine(viewport, false);
            line.anchoredPosition = new Vector2(0f, y);
        }
    }

    private static RectTransform CreateEditorGridLine(RectTransform parent, bool vertical)
    {
        var lineObject = new GameObject(vertical ? "GridColumn" : "GridRow");
        lineObject.transform.SetParent(parent, false);
        var rect = lineObject.AddComponent<RectTransform>();
        if (vertical)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(1f, 0f);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 1f);
        }

        var image = lineObject.AddComponent<Image>();
        image.color = new Color32(255, 255, 255, 10);
        image.raycastTarget = false;
        return rect;
    }

    private void CreateEditorZoomButton(RectTransform parent, string label, float delta, float rightOffset)
    {
        var button = O5Factory.Button(parent, () =>
        {
            ZoomEditor(Vector2.zero, _keyEditorZoom + delta);
        }, label, "keyviewer.editor.zoom." + label);
        button.Rect.anchorMin = button.Rect.anchorMax = button.Rect.pivot = new Vector2(1f, 0.5f);
        button.Rect.anchoredPosition = new Vector2(rightOffset, 0f);
        button.Rect.sizeDelta = new Vector2(30f, 28f);
        if (button.Label != null) button.Label.fontSize = 12f;
        Track(button);
    }

    private void CreateEditorStats(RectTransform parent)
    {
        _editorKpsText = CreateEditorStat(parent, "KPS", new Vector2(-96f, -142f));
        _editorTotalText = CreateEditorStat(parent, "TOTAL", new Vector2(96f, -142f));
        _editorKpsShown = _editorTotalShown = -1;
    }

    private static TMP_Text CreateEditorStat(RectTransform parent, string title, Vector2 position)
    {
        var root = new GameObject("Preview" + title);
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(178f, 30f);
        var image = root.AddComponent<KeycapGraphic>();
        image.sprite = O5Boot.Sprites.RoundedControl;
        image.type = Image.Type.Sliced;
        image.color = new Color32(14, 14, 17, 184);
        image.raycastTarget = false;
        var label = O5Factory.ControlText(rect, 12f, true);
        label.text = title;
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color32(196, 195, 211, 255);
        label.rectTransform.offsetMin = new Vector2(10f, 0f);
        var value = O5Factory.ControlText(rect, 12f, true);
        value.text = "0";
        value.alignment = TextAlignmentOptions.Right;
        value.color = new Color32(224, 221, 241, 255);
        value.rectTransform.offsetMin = new Vector2(0f, 0f);
        value.rectTransform.offsetMax = new Vector2(-10f, 0f);
        return value;
    }

    private void BuildEditorPreviewKeys(RectTransform parent, int count, bool foot)
    {
        for (var i = 0; i < count; i++)
        {
            var slot = i;
            // Native pointer-click selects on release; mouse-down polling would destroy a drag target.
            var button = O5Factory.Button(parent, null,
                _keyViewer?.GetSlotLabel(slot, foot, false) ?? "—",
                $"keyviewer.editor.{(foot ? "foot" : "hand")}.{slot}");
            button.Rect.anchorMin = button.Rect.anchorMax = button.Rect.pivot = new Vector2(0.5f, 0.5f);
            button.Rect.sizeDelta = new Vector2(32f, 32f);
            button.Label!.fontSize = 11f;
            button.Label.characterSpacing = 0f;
            button.Label.textWrappingMode = TextWrappingModes.NoWrap;
            button.Label.overflowMode = TextOverflowModes.Ellipsis;
            button.Label.raycastTarget = false;
            button.NormalColor = O5Boot.Theme.ObjectBG;
            var outline = button.Background.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            outline.useGraphicAlpha = true;
            var handles = CreateEditorSelectionHandles(button.Rect);
            foreach (var handle in handles)
            {
                handle.GetComponent<Image>().raycastTarget = true;
                handle.AddComponent<KeyPreviewResizeHandler>().Initialize(this, slot, foot,
                    ((RectTransform)handle.transform).anchorMin);
            }
            var cell = new KeyPreviewCell(button, outline, slot, foot, handles);
            cell.Counter = O5Factory.ControlText(button.Rect, 11f, true);
            cell.Counter.characterSpacing = 6f;
            cell.Counter.alignment = TextAlignmentOptions.Center;
            cell.Counter.rectTransform.anchorMin = new Vector2(0f, 0.05f);
            cell.Counter.rectTransform.anchorMax = new Vector2(1f, 0.35f);
            cell.Counter.rectTransform.offsetMin = Vector2.zero;
            cell.Counter.rectTransform.offsetMax = Vector2.zero;
            cell.Counter.raycastTarget = false;
            button.Label.rectTransform.offsetMin = new Vector2(3f, 0f);
            button.Label.rectTransform.offsetMax = new Vector2(-3f, 0f);
            _keyPreviewCells.Add(cell);
            var drag = button.Rect.gameObject.AddComponent<KeyPreviewDragHandler>();
            button.Rect.GetComponent<OventHandler>().enabled = false;
            drag.Initialize(this, slot, foot);
            Track(button);
        }
    }

    private static List<GameObject> CreateEditorSelectionHandles(RectTransform parent)
    {
        var handles = new List<GameObject>(8);
        var anchors = new[]
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 1f),
        };
        foreach (var anchor in anchors)
        {
            var handle = new GameObject("SelectionHandle");
            handle.transform.SetParent(parent, false);
            var rect = handle.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = new Vector2(anchor.x == 0.5f || anchor.y == 0.5f ? 7f : 8f,
                anchor.x == 0.5f || anchor.y == 0.5f ? 7f : 8f);
            var image = handle.AddComponent<Image>();
            image.sprite = O5Boot.Sprites.Circle;
            image.color = new Color32(244, 239, 255, 255);
            image.raycastTarget = false;
            handle.SetActive(false);
            handles.Add(handle);
        }

        return handles;
    }

    private void BuildKeyViewerEditorToolbar(RectTransform page, KeyViewerPreferences settings)
    {
        var toolbar = O5Factory.Row(page, 54f);
        var toolbarRect = toolbar;
        toolbarRect.anchorMin = new Vector2(0f, 0f);
        toolbarRect.anchorMax = new Vector2(1f, 0f);
        toolbarRect.pivot = new Vector2(0.5f, 0f);
        toolbarRect.anchoredPosition = new Vector2(0f, 12f);
        toolbarRect.sizeDelta = new Vector2(-28f, 54f);
        var layout = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var caption = O5Factory.ControlText(toolbar, 12f, true);
        caption.text = L.T("keyviewer.toolbar.layout");
        caption.font = O5Boot.Fonts.Medium;
        caption.color = new Color32(158, 159, 180, 255);
        var captionLayout = caption.gameObject.AddComponent<LayoutElement>();
        captionLayout.minWidth = captionLayout.preferredWidth = 52f;
        var back = O5Factory.Button(toolbar, () => ShowPage("overview"), L.T("keyviewer.toolbar.back"), "keyviewer.editor.back");
        back.Rect.SetAsFirstSibling();
        back.Rect.GetComponent<LayoutElement>().preferredWidth = 80f;
        FitEditorButtonText(back, 12f);
        Track(back);
        foreach (var count in HandKeyOptions)
        {
            var selected = count == settings.HandKeyCount;
            var button = O5Factory.Button(toolbar, () =>
            {
                _keyViewer?.SetHandKeyCount(count);
                _undoGeometry.Clear();
                _redoGeometry.Clear();
                _selectedKeySlot = Mathf.Clamp(_selectedKeySlot, 0, count - 1);
                _selectedKeyFoot = false;
                BuildWindowContent();
                FitEditorToKeys();
            }, L.F("keyviewer.key-count", count), "keyviewer.editor.layout." + count);
            button.NormalColor = selected ? new Color32(75, 63, 114, 255) : O5Boot.Theme.ObjectButton;
            FitEditorButtonText(button, 13f);
            button.UpdateVisual(true);
            var element = button.Rect.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minWidth = 42f;
                element.preferredWidth = 48f;
            }
            Track(button);
        }

        var spacer = new GameObject("ToolbarSpacer");
        spacer.transform.SetParent(toolbar, false);
        spacer.AddComponent<RectTransform>();
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1f;

        var reset = O5Factory.Button(toolbar, () =>
        {
            BeginEditorGeometry(_selectedKeySlot, _selectedKeyFoot);
            _keyViewer?.ResetSlotLayout(_selectedKeySlot, _selectedKeyFoot);
            CommitEditorGeometry();
            RefreshEditorPreviewLayout();
            RefreshKeyInspector();
        },
            L.T("keyviewer.toolbar.reset"), "keyviewer.editor.reset-slot");
        reset.NormalColor = O5Boot.Theme.ObjectButton;
        FitEditorButtonText(reset, 12f);
        reset.UpdateVisual(true);
        var resetElement = reset.Rect.GetComponent<LayoutElement>();
        if (resetElement != null)
        {
            resetElement.minWidth = 58f;
            resetElement.preferredWidth = 64f;
        }
        Track(reset);

        AddEditorToolButton(toolbar, L.T("keyviewer.tool.select"), "select", () => SetKeyViewerEditorTab(0));
        AddEditorToolButton(toolbar, L.T("keyviewer.tool.pan"), "pan", null);
        AddEditorToolButton(toolbar, L.T("keyviewer.tool.fit"), "fit", FitEditorToKeys);
        AddEditorToolButton(toolbar, _editorSnap ? L.T("keyviewer.tool.snap-on") : L.T("keyviewer.tool.snap-off"), "snap", () => { _editorSnap = !_editorSnap; BuildWindowContent(); });
        AddEditorToolButton(toolbar, L.T("keyviewer.tool.undo"), "undo", () => RestoreEditorGeometry(false));
        AddEditorToolButton(toolbar, L.T("keyviewer.tool.redo"), "redo", () => RestoreEditorGeometry(true));
        AddEditorToolButton(toolbar, L.T("nav.group.settings"), "settings", () => SetKeyViewerEditorTab(3));
        var enabled = O5Factory.Toggle(toolbar, true, _store!.Document.Preferences.KeyViewerEnabled,
            value => _keyViewer?.SetEnabled(value), L.T("keyviewer.toolbar.viewer"), "argon.keyviewer.enabled");
        enabled.Label.characterSpacing = 0f;
        enabled.Label.textWrappingMode = TextWrappingModes.NoWrap;
        enabled.Label.overflowMode = TextOverflowModes.Ellipsis;
        enabled.Label.fontSize = 12f;
        enabled.Label.enableAutoSizing = true;
        enabled.Label.fontSizeMin = 10f;
        enabled.Label.fontSizeMax = 12f;
        enabled.Label.rectTransform.offsetMin = new Vector2(8f, 0f);
        enabled.Label.rectTransform.offsetMax = new Vector2(-38f, 0f);
        var enabledLayout = enabled.Rect.GetComponent<LayoutElement>();
        if (enabledLayout != null)
        {
            enabledLayout.minWidth = 88f;
            enabledLayout.preferredWidth = 94f;
        }
        Track(enabled);
    }

    private void AddEditorToolButton(RectTransform parent, string label, string tool, Action? action)
    {
        var button = O5Factory.Button(parent, () =>
        {
            if (tool == "select" || tool == "pan") _keyEditorTool = tool;
            action?.Invoke();
            if (action == null) BuildWindowContent();
        }, label, "keyviewer.editor.tool." + tool);
        button.NormalColor = (tool == "settings" ? _keyViewerEditorTab == 3 : tool == "snap" ? _editorSnap : _keyEditorTool == tool)
            ? new Color32(75, 63, 114, 255)
            : O5Boot.Theme.ObjectButton;
        FitEditorButtonText(button, 12f);
        button.UpdateVisual(true);
        var element = button.Rect.GetComponent<LayoutElement>();
        if (element != null)
        {
            element.minWidth = 52f;
            element.preferredWidth = 60f;
        }
        Track(button);
    }

    private static void FitEditorButtonText(O5Button button, float fontSize)
    {
        if (button.Label == null) return;
        button.Label.characterSpacing = 0f;
        button.Label.fontSize = fontSize;
        button.Label.enableAutoSizing = true;
        button.Label.fontSizeMin = 10f;
        button.Label.fontSizeMax = fontSize;
        button.Label.textWrappingMode = TextWrappingModes.NoWrap;
        button.Label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void BuildKeyViewerInspector(RectTransform inspector, KeyViewerPreferences settings)
    {
        _inspectorRoot = inspector;
        var firstControl = _controls.Count;
        var headerObject = new GameObject("InspectorHeader");
        headerObject.transform.SetParent(inspector, false);
        var header = headerObject.AddComponent<RectTransform>();
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, 108f);
        var headerLayout = headerObject.AddComponent<VerticalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 12, 12, 10);
        headerLayout.spacing = 4f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = true;
        headerLayout.childForceExpandHeight = false;

        var viewportObject = new GameObject("InspectorViewport");
        viewportObject.transform.SetParent(inspector, false);
        var viewport = viewportObject.AddComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(0f, -108f);
        var viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        viewportObject.AddComponent<RectMask2D>();

        var contentObject = new GameObject("InspectorContent");
        contentObject.transform.SetParent(viewport, false);
        var content = contentObject.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(12, 12, 12, 12);
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = inspector.GetComponent<UIScrollController>() ?? inspector.gameObject.AddComponent<UIScrollController>();
        scroll.wheelStrength = 34f;
        scroll.SetContent(content, viewport);
        _inspectorScroll = scroll;
        _builtInspectorTab = _keyViewerEditorTab;

        var choices = BuildBindingChoices();
        var selectedChoice = choices.FirstOrDefault(choice => choice.Index == _selectedKeySlot && choice.IsFoot == _selectedKeyFoot)
                             ?? choices.FirstOrDefault();
        _selectedBindingChoice = selectedChoice;
        if (selectedChoice != null)
        {
            _selectedKeySlot = selectedChoice.Index;
            _selectedKeyFoot = selectedChoice.IsFoot;
        }

        var selectedName = selectedChoice == null
            ? L.T("keyviewer.select-key")
            : _keyViewer?.GetSlotLabel(selectedChoice.Index, selectedChoice.IsFoot, _editingGhostBindings) ?? L.T("keyviewer.select-key");
        var selectedRow = O5Factory.Row(header, 42f);
        _editorSelectedName = O5Factory.ControlText(selectedRow, 18f, true);
        _editorSelectedName.text = selectedName;
        _editorSelectedName.font = O5Boot.Fonts.Medium;
        _editorSelectedName.color = new Color32(231, 228, 243, 255);
        _editorSelectedName.characterSpacing = 0f;
        _editorSelectedName.enableAutoSizing = true;
        _editorSelectedName.fontSizeMin = 12f;
        _editorSelectedName.fontSizeMax = 18f;
        _editorSelectedName.textWrappingMode = TextWrappingModes.NoWrap;
        _editorSelectedName.overflowMode = TextOverflowModes.Ellipsis;
        var tabRow = O5Factory.Row(header, 40f);
        var tabsLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 4f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;
        tabsLayout.childForceExpandHeight = true;
        var tabLabels = new[] { L.T("keyviewer.tab.key"), L.T("keyviewer.tab.notes"), L.T("keyviewer.tab.counter"), L.T("nav.group.settings") };
        for (var index = 0; index < tabLabels.Length; index++)
        {
            var tabIndex = index;
            var button = O5Factory.Button(tabRow, () => SetKeyViewerEditorTab(tabIndex), tabLabels[index],
                "keyviewer.editor.tab." + tabIndex);
            button.NormalColor = _keyViewerEditorTab == index
                ? new Color32(83, 69, 132, 255)
                : new Color32(29, 31, 42, 255);
            if (button.Label != null)
            {
                button.Label.fontSize = 14f;
                button.Label.characterSpacing = 0f;
                button.Label.enableAutoSizing = true;
                button.Label.fontSizeMin = 10f;
                button.Label.fontSizeMax = 14f;
                button.Label.textWrappingMode = TextWrappingModes.NoWrap;
                button.Label.overflowMode = TextOverflowModes.Ellipsis;
            }
            button.UpdateVisual(true);
            Track(button);
        }

        if (_keyViewerEditorTab == 0) BuildKeyViewerKeyInspector(content, choices, selectedChoice, settings);
        else if (_keyViewerEditorTab == 1) BuildKeyViewerNoteInspector(content, settings, selectedChoice);
        else if (_keyViewerEditorTab == 2) BuildKeyViewerCounterInspector(content, settings, selectedChoice);
        else BuildKeyViewerGlobalSettings(content, settings);
        _inspectorControls.AddRange(_controls.Skip(firstControl));
    }

    private void RefreshKeyInspector()
    {
        if (_inspectorRoot == null || _store == null) return;
        if (_inspectorScroll != null && _inspectorScroll.content != null)
            _inspectorOffsets[_builtInspectorTab] = _inspectorScroll.content.anchoredPosition.y;
        foreach (var control in _inspectorControls)
        {
            control.Dispose();
            _controls.Remove(control);
        }
        _inspectorControls.Clear();
        for (var i = _inspectorRoot.childCount - 1; i >= 0; i--)
        {
            var child = _inspectorRoot.GetChild(i);
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Object.Destroy(child.gameObject);
        }
        BuildKeyViewerInspector(_inspectorRoot, _store.Document.Preferences.KeyViewer);
        SettleEditorLayout();
    }

    private void SettleEditorLayout()
    {
        if (_activePage != "keyviewer" || _keyEditorCanvas == null || !_keyEditorCanvas.gameObject.activeInHierarchy) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_pages["keyviewer"]);
        if (_inspectorScroll == null || _inspectorScroll.content == null || _inspectorScroll.viewport == null) return;
        var content = _inspectorScroll.content;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(_inspectorOffsets[_builtInspectorTab], 0f,
            Mathf.Max(0f, content.rect.height - _inspectorScroll.viewport.rect.height)));
        _inspectorScroll.SetContent(content, _inspectorScroll.viewport);
    }

    private void BuildKeyViewerKeyInspector(RectTransform parent, List<KeyBindingChoice> choices,
        KeyBindingChoice? selectedChoice, KeyViewerPreferences settings)
    {
        var inspectorContent = parent;
        parent = CreateInspectorCard(inspectorContent, "Mapping");
        var mappingTitle = O5Factory.Row(parent, 28f);
        var title = O5Factory.ControlText(mappingTitle, 13f, true);
        title.text = L.T("keyviewer.mapping.title");
        title.color = new Color32(156, 157, 177, 255);
        title.font = O5Boot.Fonts.Medium;

        var ghostToggle = O5Factory.Toggle(parent, false, _editingGhostBindings, value =>
        {
            _editingGhostBindings = value;
            BuildWindowContent();
        }, L.T("keyviewer.edit-ghost"), "keyviewer.editor.ghost-binding");
        ConfigureToggleLabel(ghostToggle.Label, 13f, 48f);
        Track(ghostToggle);

        O5Dropdown<KeyBindingChoice>? keyDropdown = null;
        if (selectedChoice != null && _keyViewer != null)
        {
            var bindingDropdownRow = O5Factory.Row(parent, 46f);
            keyDropdown = O5Factory.DropDown(bindingDropdownRow, selectedChoice, selectedChoice, choices,
                FormatBindingChoice, choice =>
                {
                    if (choice != null) SelectKeySlot(choice.Index, choice.IsFoot);
                }, "keyviewer.editor.binding");
            Track(keyDropdown);
            _keyBindingDropdown = keyDropdown;
        }

        var capture = O5Factory.Button(parent, () =>
        {
            if (selectedChoice == null) return;
            _keyViewer?.BeginCapture(selectedChoice.Index, selectedChoice.IsFoot, _editingGhostBindings);
        }, L.T("keyviewer.capture"), "keyviewer.editor.capture");
        FitEditorButtonText(capture, 13f);
        Track(capture);
        var labelInput = O5Factory.Input(parent, null,
            selectedChoice == null ? string.Empty : _keyViewer?.GetCustomSlotLabel(selectedChoice.Index, selectedChoice.IsFoot, _editingGhostBindings) ?? string.Empty,
            value =>
            {
                if (selectedChoice == null) return;
                _keyViewer?.SetSlotLabel(selectedChoice.Index, selectedChoice.IsFoot, _editingGhostBindings, value);
                keyDropdown?.Set(selectedChoice, false);
            }, L.T("keyviewer.display-name"), null, "keyviewer.editor.display-name");
        Track(labelInput);
        _keyBindingLabelInput = labelInput;
        var captureRow = O5Factory.Row(parent, 30f);
        _captureStatus = O5Factory.ControlText(captureRow, 11f, true);
        _captureStatus.text = _keyViewer?.CaptureMessage ?? string.Empty;
        _captureStatus.color = new Color32(157, 156, 180, 255);
        _captureStatus.characterSpacing = 0f;

        if (selectedChoice == null || _keyViewer == null) return;
        var identity = (selectedChoice.IsFoot ? "foot" : "hand") + "." + selectedChoice.Index;
        var offset = _keyViewer.GetSlotOffset(selectedChoice.Index, selectedChoice.IsFoot);
        var size = _keyViewer.GetSlotSize(selectedChoice.Index, selectedChoice.IsFoot);
        parent = CreateInspectorCard(inspectorContent, "Geometry");
        AddInspectorSection(parent, L.T("keyviewer.geometry.title"), L.T("keyviewer.geometry.subtitle"));
        AddEditorNumber(parent, L.T("keyviewer.x"), 0f, -1200f, 1200f, offset.x, value =>
        {
            _keyViewer.SetSlotOffset(selectedChoice.Index, selectedChoice.IsFoot,
                new Vector2(value, _keyViewer.GetSlotOffset(selectedChoice.Index, selectedChoice.IsFoot).y), false);
            RefreshEditorPreviewLayout();
        }, "keyviewer.editor.x." + identity, "F0");
        AddEditorNumber(parent, L.T("keyviewer.y"), 0f, -600f, 600f, offset.y, value =>
        {
            _keyViewer.SetSlotOffset(selectedChoice.Index, selectedChoice.IsFoot,
                new Vector2(_keyViewer.GetSlotOffset(selectedChoice.Index, selectedChoice.IsFoot).x, value), false);
            RefreshEditorPreviewLayout();
        }, "keyviewer.editor.y." + identity, "F0");
        AddEditorNumber(parent, L.T("common.width"), size.x, 24f, 240f, size.x, value =>
        {
            _keyViewer.SetSlotSize(selectedChoice.Index, selectedChoice.IsFoot,
                new Vector2(value, _keyViewer.GetSlotSize(selectedChoice.Index, selectedChoice.IsFoot).y), false);
            RefreshEditorPreviewLayout();
        }, "keyviewer.editor.width." + identity, "F0");
        AddEditorNumber(parent, L.T("common.height"), size.y, 24f, 240f, size.y, value =>
        {
            _keyViewer.SetSlotSize(selectedChoice.Index, selectedChoice.IsFoot,
                new Vector2(_keyViewer.GetSlotSize(selectedChoice.Index, selectedChoice.IsFoot).x, value), false);
            RefreshEditorPreviewLayout();
        }, "keyviewer.editor.height." + identity, "F0");
        var reset = O5Factory.Button(parent, () =>
        {
            BeginEditorGeometry(selectedChoice.Index, selectedChoice.IsFoot);
            _keyViewer.ResetSlotLayout(selectedChoice.Index, selectedChoice.IsFoot);
            CommitEditorGeometry();
            RefreshEditorPreviewLayout();
            RefreshKeyInspector();
        }, L.T("keyviewer.reset-geometry"), "keyviewer.editor.reset-geometry." + identity);
        Track(reset);
        parent = CreateInspectorCard(inspectorContent, "Appearance");
        AddSlider(parent, L.T("keyviewer.border-width"), 1.5f, 0f, 12f,
            _keyViewer.GetSlotBorderWidth(selectedChoice.Index, selectedChoice.IsFoot),
            value =>
            {
                _keyViewer.SetSlotBorderWidth(selectedChoice.Index, selectedChoice.IsFoot, value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.border-width." + identity, "F1");
        AddSlider(parent, L.T("keyviewer.font-size"), 15f, 8f, 48f,
            _keyViewer.GetSlotFontSize(selectedChoice.Index, selectedChoice.IsFoot),
            value =>
            {
                _keyViewer.SetSlotFontSize(selectedChoice.Index, selectedChoice.IsFoot, value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.font-size." + identity, "F0");
        AddKeyColorPicker(parent, L.T("keyviewer.slot.background"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "background"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "background", value), "keyviewer.editor.background." + identity);
        AddKeyColorPicker(parent, L.T("keyviewer.slot.pressed-background"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-background"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-background", value), "keyviewer.editor.pressed-background." + identity);
        AddKeyColorPicker(parent, L.T("keyviewer.slot.outline"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "outline"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "outline", value), "keyviewer.editor.outline." + identity);
        AddKeyColorPicker(parent, L.T("keyviewer.slot.pressed-outline"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-outline"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-outline", value), "keyviewer.editor.pressed-outline." + identity);
        AddKeyColorPicker(parent, L.T("keyviewer.slot.text"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "text"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "text", value), "keyviewer.editor.text." + identity);
        AddKeyColorPicker(parent, L.T("keyviewer.slot.pressed-text"), _keyViewer.GetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-text"),
            value => _keyViewer.SetSlotColor(selectedChoice.Index, selectedChoice.IsFoot, "pressed-text", value), "keyviewer.editor.pressed-text." + identity);
    }

    private void BuildKeyViewerNoteInspector(RectTransform parent, KeyViewerPreferences settings, KeyBindingChoice? selectedChoice)
    {
        parent = CreateInspectorCard(parent, "Note");
        AddInspectorSection(parent, L.T("keyviewer.notes.title"), L.T("keyviewer.notes.subtitle"));
        AddPreferenceToggle(parent, L.T("keyviewer.rain"), settings.ShowRain, value => _keyViewer?.SetRain(value), "keyviewer.editor.rain");
        AddPreferenceToggle(parent, L.T("keyviewer.ghost-rain"), settings.ShowGhostRain, value => _keyViewer?.SetGhostRain(value), "keyviewer.editor.ghost-rain");
        if (selectedChoice != null && _keyViewer != null)
        {
            AddPreferenceToggle(parent, L.T("keyviewer.slot-note-effect"), _keyViewer.GetSlotNoteEffect(selectedChoice.Index, selectedChoice.IsFoot),
                value => _keyViewer.SetSlotNoteEffect(selectedChoice.Index, selectedChoice.IsFoot, value),
                "keyviewer.editor.slot-note-effect." + (selectedChoice.IsFoot ? "foot" : "hand") + "." + selectedChoice.Index);
        }
        AddSlider(parent, L.T("keyviewer.rain-speed"), 100f, 10f, 600f, settings.RainSpeed,
            value => _keyViewer?.SetRainSpeed(value, false), "keyviewer.editor.rain-speed", "F0", value => _keyViewer?.SetRainSpeed(value, false));
        AddSlider(parent, L.T("keyviewer.rain-height"), 200f, 20f, 1200f, settings.RainHeight,
            value => _keyViewer?.SetRainHeight(value, false), "keyviewer.editor.rain-height", "F0", value => _keyViewer?.SetRainHeight(value, false));
    }

    private void BuildKeyViewerCounterInspector(RectTransform parent, KeyViewerPreferences settings, KeyBindingChoice? selectedChoice)
    {
        parent = CreateInspectorCard(parent, "Counter");
        AddInspectorSection(parent, L.T("keyviewer.counter.title"), L.T("keyviewer.counter.subtitle"));
        AddPreferenceToggle(parent, L.T("keyviewer.show-kps"), settings.ShowTotalKps,
            value => settings.ShowTotalKps = value, "keyviewer.editor.total-kps");
        if (selectedChoice != null && _keyViewer != null)
        {
            AddPreferenceToggle(parent, L.T("keyviewer.slot-counter"),
                _keyViewer.GetSlotCounterVisible(selectedChoice.Index, selectedChoice.IsFoot),
                value => _keyViewer.SetSlotCounterVisible(selectedChoice.Index, selectedChoice.IsFoot, value),
                "keyviewer.editor.slot-counter." + (selectedChoice.IsFoot ? "foot" : "hand") + "." + selectedChoice.Index);
        }
        AddInspectorSection(parent, L.T("keyviewer.records.title"), L.T("keyviewer.records.subtitle"));
        var reset = O5Factory.Button(parent, () => _keyViewer?.ResetCounts(), L.T("keyviewer.reset-counts"), "keyviewer.editor.reset-counts");
        Track(reset);
    }

    private void SetKeyViewerEditorTab(int tab)
    {
        _keyViewerEditorTab = Mathf.Clamp(tab, 0, 3);
        RefreshKeyInspector();
    }

    private void BuildKeyViewerGlobalSettings(RectTransform parent, KeyViewerPreferences settings)
    {
        var inspectorContent = parent;
        parent = CreateInspectorCard(inspectorContent, "Layout");
        AddInspectorSection(parent, L.T("keyviewer.default-layout.title"), L.T("keyviewer.default-layout.subtitle"));
        var restore = O5Factory.Button(parent, () =>
        {
            if (_pendingGeometry != null) return;
            _keyViewer?.RestoreJrpLayout();
            _undoGeometry.Clear();
            _redoGeometry.Clear();
            RefreshEditorPreviewLayout();
            FitEditorToKeys();
            RefreshKeyInspector();
        }, L.T("keyviewer.restore-default"), "keyviewer.editor.restore-jrp");
        FitEditorButtonText(restore, 13f);
        Track(restore);
        AddInspectorSection(parent, L.T("keyviewer.screen.title"), L.T("keyviewer.screen.subtitle"));
        AddDropdown(parent, 4, settings.FootKeyCount, FootKeyOptions,
            value => value == 0 ? L.T("keyviewer.no-foot-keys") : L.F("keyviewer.key-count", value), value =>
            {
                _keyViewer?.SetFootKeyCount(value);
                _undoGeometry.Clear();
                _redoGeometry.Clear();
                if (_selectedKeyFoot && _selectedKeySlot >= value) _selectedKeyFoot = false;
                _selectedKeySlot = Mathf.Clamp(_selectedKeySlot, 0, settings.HandKeyCount - 1);
                BuildWindowContent();
            }, "keyviewer.editor.foot-count");
        AddSlider(parent, L.T("keyviewer.scale"), 1f, 0.25f, 3f, settings.Scale,
            value => _keyViewer?.SetScale(value, false), "keyviewer.editor.scale", "F2",
            value => _keyViewer?.SetScale(value, false));
        AddSlider(parent, L.T("keyviewer.vertical-offset"), 200f, 0f, 600f, settings.VerticalOffset,
            value => _keyViewer?.SetVerticalOffset(value, false), "keyviewer.editor.vertical-offset", "F0",
            value => _keyViewer?.SetVerticalOffset(value, false));
        AddSlider(parent, L.T("keyviewer.key-size"), 50f, 32f, 96f, settings.KeySize,
            value =>
            {
                _keyViewer?.SetKeySize(value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.key-size", "F0", value => _keyViewer?.SetKeySize(value, false));
        AddSlider(parent, L.T("keyviewer.hand-x"), 0f, -1000f, 1000f, settings.HandOffsetX,
            value =>
            {
                _keyViewer?.SetHandOffsetX(value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.hand-x", "F0", value => _keyViewer?.SetHandOffsetX(value, false));
        AddSlider(parent, L.T("keyviewer.hand-y"), 0f, -300f, 300f, settings.HandOffsetY,
            value =>
            {
                _keyViewer?.SetHandOffsetY(value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.hand-y", "F0", value => _keyViewer?.SetHandOffsetY(value, false));
        AddSlider(parent, L.T("keyviewer.foot-x"), 0f, -1000f, 1000f, settings.FootOffsetX,
            value =>
            {
                _keyViewer?.SetFootOffsetX(value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.foot-x", "F0", value => _keyViewer?.SetFootOffsetX(value, false));
        AddSlider(parent, L.T("keyviewer.foot-y"), 0f, -300f, 300f, settings.FootOffsetY,
            value =>
            {
                _keyViewer?.SetFootOffsetY(value, false);
                RefreshEditorPreviewLayout();
            }, "keyviewer.editor.foot-y", "F0", value => _keyViewer?.SetFootOffsetY(value, false));
        AddInspectorSection(parent, L.T("keyviewer.input.title"), L.T("keyviewer.input.subtitle"));

        parent = CreateInspectorCard(inspectorContent, "Palette");
        AddInspectorSection(parent, L.T("keyviewer.palette.title"), L.T("keyviewer.palette.subtitle"));
        AddKeyColorPicker(parent, L.T("keyviewer.palette.background"), settings.BackgroundColor,
            value => settings.BackgroundColor = value, "keyviewer.editor.global.background");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.pressed-background"), settings.PressedBackgroundColor,
            value => settings.PressedBackgroundColor = value, "keyviewer.editor.global.pressed-background");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.outline"), settings.OutlineColor,
            value => settings.OutlineColor = value, "keyviewer.editor.global.outline");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.pressed-outline"), settings.PressedOutlineColor,
            value => settings.PressedOutlineColor = value, "keyviewer.editor.global.pressed-outline");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.text"), settings.TextColor,
            value => settings.TextColor = value, "keyviewer.editor.global.text");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.pressed-text"), settings.PressedTextColor,
            value => settings.PressedTextColor = value, "keyviewer.editor.global.pressed-text");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.rain"), settings.RainColor,
            value => settings.RainColor = value, "keyviewer.editor.global.rain");
        AddKeyColorPicker(parent, L.T("keyviewer.palette.ghost-rain"), settings.GhostRainColor,
            value => settings.GhostRainColor = value, "keyviewer.editor.global.ghost-rain");
    }

    private void AddEditorNumber(Transform parent, string label, float defaultValue, float min, float max,
        float value, Action<float> onChanged, string id, string format)
    {
        var row = O5Factory.Row(parent, 38f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        var caption = O5Factory.ControlText(row, 13f, true);
        caption.text = label;
        caption.characterSpacing = 0f;
        caption.color = new Color32(175, 175, 181, 255);
        caption.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        O5InputField? input = null;
        input = O5Factory.Input(row, defaultValue.ToString(format, culture), value.ToString(format, culture),
            null, label, null, id, text =>
            {
                if (float.TryParse(text, System.Globalization.NumberStyles.Float, culture, out var parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    value = Mathf.Clamp(parsed, min, max);
                    BeginEditorGeometry(_selectedKeySlot, _selectedKeyFoot);
                    onChanged(value);
                    CommitEditorGeometry();
                    _store?.Save();
                }
                input?.Set(value.ToString(format, culture), false);
            });
        input.Rect.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Track(input);
    }

    private static RectTransform CreateInspectorCard(Transform parent, string name)
    {
        var root = new GameObject(name + "Card");
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        var background = root.AddComponent<Image>();
        background.sprite = O5Boot.Sprites.RoundedControl;
        background.type = Image.Type.Sliced;
        background.color = new Color32(34, 34, 36, 255);
        background.raycastTarget = false;
        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    private static void AddInspectorSection(Transform parent, string title, string description)
    {
        var row = O5Factory.Row(parent, 76f);
        ConfigureFlowRow(row, 8);
        var titleText = O5Factory.ControlText(row, 14f, true);
        titleText.text = title;
        titleText.font = O5Boot.Fonts.Medium;
        titleText.color = new Color32(225, 223, 239, 255);
        titleText.characterSpacing = 0f;
        titleText.textWrappingMode = TextWrappingModes.Normal;
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.rectTransform.anchorMin = new Vector2(0f, 0.55f);
        titleText.rectTransform.anchorMax = Vector2.one;
        titleText.rectTransform.offsetMin = Vector2.zero;
        titleText.rectTransform.offsetMax = Vector2.zero;

        var descriptionText = O5Factory.ControlText(row, 12f, true);
        descriptionText.text = description;
        descriptionText.color = new Color32(148, 150, 169, 255);
        descriptionText.characterSpacing = 0f;
        descriptionText.textWrappingMode = TextWrappingModes.Normal;
        descriptionText.overflowMode = TextOverflowModes.Overflow;
        descriptionText.rectTransform.anchorMin = Vector2.zero;
        descriptionText.rectTransform.anchorMax = new Vector2(1f, 0.54f);
        descriptionText.rectTransform.offsetMin = Vector2.zero;
        descriptionText.rectTransform.offsetMax = Vector2.zero;
    }

    private static void ConfigureFlowRow(RectTransform row, int verticalPadding)
    {
        // Let TMP preferred heights determine the row height after wrapping.
        var element = row.GetComponent<LayoutElement>();
        element.minHeight = -1f;
        element.preferredHeight = -1f;
        var layout = row.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, verticalPadding, verticalPadding);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void RefreshEditorPreviewLayout()
    {
        if (_keyEditorCanvas == null || _keyViewer == null) return;
        if (_editorGrid != null)
        {
            _editorGrid.anchoredPosition = _editorPan;
            _editorGrid.localScale = Vector3.one * _keyEditorZoom;
        }
        if (_editorZoomText != null) _editorZoomText.text = Mathf.RoundToInt(_keyEditorZoom * 100f) + "%";
        if (_editorKpsText != null)
        {
            var rect = (RectTransform)_editorKpsText.transform.parent;
            RefreshEditorStat(rect, false);
        }
        if (_editorTotalText != null)
        {
            var rect = (RectTransform)_editorTotalText.transform.parent;
            RefreshEditorStat(rect, true);
        }
        foreach (var cell in _keyPreviewCells)
        {
            var position = _keyViewer.GetSlotPosition(cell.Slot, cell.Foot);
            var size = _keyViewer.GetSlotSize(cell.Slot, cell.Foot);
            var settings = _store!.Document.Preferences.KeyViewer;
            var laneOffsetX = cell.Foot ? settings.FootOffsetX : settings.HandOffsetX;
            var laneOffsetY = cell.Foot ? settings.FootOffsetY : settings.HandOffsetY;
            var laneY = cell.Foot ? KeyLayoutGeometry.FootY : KeyLayoutGeometry.HandY;
            var previewScale = KeyEditorPreviewScale * _keyEditorZoom;
            cell.Button.Rect.anchoredPosition = _editorPan + new Vector2((position.x + laneOffsetX) * previewScale,
                (laneY + position.y + laneOffsetY) * previewScale);
            cell.Button.Rect.sizeDelta = size * previewScale;
            var border = _keyViewer.GetSlotBorderWidth(cell.Slot, cell.Foot);
            cell.Outline.effectDistance = new Vector2(border, -border) * previewScale;
            cell.Surface.BorderWidth = border * previewScale;
            cell.Surface.VisualScale = previewScale;
            cell.Surface.SetVerticesDirty();
            if (cell.Button.Label != null) cell.Button.Label.fontSize = _keyViewer.GetSlotFontSize(cell.Slot, cell.Foot) * previewScale;
        }

        RefreshKeyPreview(true);
    }

    internal void MoveEditorPreviewKey(int slot, bool foot, Vector2 screenDelta)
    {
        if (_keyViewer == null || _keyEditorTool != "select") return;
        if (_pendingGeometry == null) BeginEditorGeometry(slot, foot);
        _selectedKeySlot = slot;
        _selectedKeyFoot = foot;
        var canvas = _keyEditorCanvas != null ? _keyEditorCanvas.GetComponentInParent<Canvas>() : null;
        var scale = Mathf.Max(0.01f, canvas != null ? canvas.scaleFactor : 1f);
        _gestureDelta += screenDelta / (scale * KeyEditorPreviewScale * _keyEditorZoom);
        var offset = _pendingGeometry!.BeforeOffset + _gestureDelta;
        if (screenDelta == Vector2.zero) return;
        if (_editorSnap && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
        {
            var origin = EditorSlotOrigin(slot, foot);
            var half = _pendingGeometry.BeforeSize * 0.5f;
            // Match the top-left edge to the displayed grid, rather than the key center.
            offset = new Vector2(EditorViewportMath.SnapOffset(offset.x, origin.x - half.x, 10f),
                EditorViewportMath.SnapOffset(offset.y, origin.y + half.y, 10f));
        }
        _keyViewer.SetSlotOffset(slot, foot, offset, false);
        RefreshEditorPreviewLayout();
    }

    private void RefreshEditorStat(RectTransform rect, bool total)
    {
        var settings = _store!.Document.Preferences.KeyViewer;
        JrpKeyLayout.Stat(settings.HandKeyCount, total, out var x, out var y, out var width, out var height);
        var scale = settings.KeySize / 50f;
        rect.anchoredPosition = _editorPan + (new Vector2(x, y) * scale +
            new Vector2(settings.HandOffsetX, KeyLayoutGeometry.HandY + settings.HandOffsetY)) * _keyEditorZoom;
        rect.sizeDelta = new Vector2(width, height) * scale;
        rect.localScale = Vector3.one * _keyEditorZoom;
        KeyViewerRuntime.LayoutStatText(rect, height > 30f);
    }

    internal void FinishEditorPreviewDrag()
    {
        if (_pendingGeometry == null) return;
        CommitEditorGeometry();
        _store?.Save();
        RefreshKeyInspector();
    }

    internal void ResizeEditorPreviewKey(int slot, bool foot, Vector2 anchor, Vector2 delta)
    {
        if (_keyViewer == null || _keyEditorTool != "select") return;
        if (_pendingGeometry == null) BeginEditorGeometry(slot, foot);
        _selectedKeySlot = slot;
        _selectedKeyFoot = foot;
        var canvas = _keyEditorCanvas!.GetComponentInParent<Canvas>();
        delta /= Mathf.Max(0.01f, canvas.scaleFactor * KeyEditorPreviewScale * _keyEditorZoom);
        _gestureDelta += delta;
        delta = _gestureDelta;
        var snap = _editorSnap && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
        var oldSize = _pendingGeometry!.BeforeSize;
        var direction = anchor * 2f - Vector2.one;
        var center = EditorSlotOrigin(slot, foot) + _pendingGeometry.BeforeOffset;
        var size = new Vector2(EditorViewportMath.ResizeAxis(center.x, oldSize.x, direction.x, delta.x, snap, out var shiftX),
            EditorViewportMath.ResizeAxis(center.y, oldSize.y, direction.y, delta.y, snap, out var shiftY));
        var offset = _pendingGeometry.BeforeOffset + new Vector2(shiftX, shiftY);
        _keyViewer.SetSlotSize(slot, foot, size, false);
        _keyViewer.SetSlotOffset(slot, foot, offset, false);
        RefreshEditorPreviewLayout();
    }

    internal bool EditorPanTool => _keyEditorTool == "pan";

    private Vector2 EditorSlotOrigin(int slot, bool foot)
    {
        var settings = _store!.Document.Preferences.KeyViewer;
        return _keyViewer!.GetSlotPosition(slot, foot) - _keyViewer.GetSlotOffset(slot, foot) +
            new Vector2(foot ? settings.FootOffsetX : settings.HandOffsetX,
                (foot ? KeyLayoutGeometry.FootY : KeyLayoutGeometry.HandY) + (foot ? settings.FootOffsetY : settings.HandOffsetY));
    }

    internal void PanEditor(Vector2 screenDelta)
    {
        if (_keyEditorCanvas == null) return;
        var canvas = _keyEditorCanvas.GetComponentInParent<Canvas>();
        _editorPan += screenDelta / Mathf.Max(0.01f, canvas.scaleFactor);
        RefreshEditorPreviewLayout();
    }

    internal void ScrollEditor(PointerEventData data)
    {
        if (_keyEditorCanvas == null || _pendingGeometry != null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyEditorCanvas, data.position,
            data.pressEventCamera, out var point);
        ZoomEditor(point, _keyEditorZoom * Mathf.Pow(1.1f, data.scrollDelta.y));
    }

    private void ZoomEditor(Vector2 point, float zoom)
    {
        zoom = Mathf.Clamp(zoom, 0.25f, 2f);
        _editorPan = new Vector2(EditorViewportMath.ZoomPan(point.x, _editorPan.x, _keyEditorZoom, zoom),
            EditorViewportMath.ZoomPan(point.y, _editorPan.y, _keyEditorZoom, zoom));
        _keyEditorZoom = zoom;
        RefreshEditorPreviewLayout();
    }

    private void FitEditorToKeys()
    {
        if (_keyViewer == null || _keyEditorCanvas == null || _keyPreviewCells.Count == 0) return;
        var min = new Vector2(-185f, -157f);
        var max = new Vector2(185f, -127f);
        var settings = _store!.Document.Preferences.KeyViewer;
        foreach (var cell in _keyPreviewCells)
        {
            var center = _keyViewer.GetSlotPosition(cell.Slot, cell.Foot) +
                new Vector2(cell.Foot ? settings.FootOffsetX : settings.HandOffsetX,
                    (cell.Foot ? KeyLayoutGeometry.FootY : KeyLayoutGeometry.HandY) + (cell.Foot ? settings.FootOffsetY : settings.HandOffsetY));
            var half = _keyViewer.GetSlotSize(cell.Slot, cell.Foot) * 0.5f;
            min = Vector2.Min(min, center - half);
            max = Vector2.Max(max, center + half);
        }
        var available = _keyEditorCanvas.rect.size - new Vector2(64f, 64f);
        _keyEditorZoom = Mathf.Clamp(Mathf.Min(available.x / (max.x - min.x), available.y / (max.y - min.y)), 0.25f, 2f);
        _editorPan = -(min + max) * 0.5f * _keyEditorZoom;
        RefreshEditorPreviewLayout();
    }

    internal void BeginEditorGeometry(int slot, bool foot)
    {
        if (_keyViewer == null || _pendingGeometry != null) return;
        _pendingGeometry = new GeometryEdit { Slot = slot, Foot = foot,
            BeforeOffset = _keyViewer.GetSlotOffset(slot, foot), BeforeSize = _keyViewer.GetSlotSize(slot, foot),
            BeforeOverride = _keyViewer.GetSlotSizeOverride(slot, foot) };
        _gestureDelta = Vector2.zero;
    }

    private void CommitEditorGeometry()
    {
        var edit = _pendingGeometry;
        _pendingGeometry = null;
        if (edit == null || _keyViewer == null) return;
        edit.AfterOffset = _keyViewer.GetSlotOffset(edit.Slot, edit.Foot);
        edit.AfterSize = _keyViewer.GetSlotSize(edit.Slot, edit.Foot);
        edit.AfterOverride = _keyViewer.GetSlotSizeOverride(edit.Slot, edit.Foot);
        if (edit.BeforeOffset == edit.AfterOffset && edit.BeforeOverride == edit.AfterOverride) return;
        _undoGeometry.Add(edit);
        if (_undoGeometry.Count > 100) _undoGeometry.RemoveAt(0);
        _redoGeometry.Clear();
    }

    private void RestoreEditorGeometry(bool redo)
    {
        if (_pendingGeometry != null || _keyViewer == null) return;
        var source = redo ? _redoGeometry : _undoGeometry;
        var target = redo ? _undoGeometry : _redoGeometry;
        if (source.Count == 0) return;
        var edit = source[source.Count - 1];
        source.RemoveAt(source.Count - 1);
        target.Add(edit);
        _keyViewer.RestoreSlotGeometry(edit.Slot, edit.Foot, redo ? edit.AfterOffset : edit.BeforeOffset,
            redo ? edit.AfterOverride : edit.BeforeOverride);
        _selectedKeySlot = edit.Slot;
        _selectedKeyFoot = edit.Foot;
        _store?.Save();
        RefreshEditorPreviewLayout();
        RefreshKeyInspector();
    }

    private sealed class GeometryEdit
    {
        internal int Slot;
        internal bool Foot;
        internal Vector2 BeforeOffset, BeforeSize, AfterOffset, AfterSize, BeforeOverride, AfterOverride;
    }

    private void BuildKeyViewerStylePage(RectTransform page)
    {
        var settings = _store!.Document.Preferences.KeyViewer;
        var effectsCard = CreateSettingsCard(page, L.T("keyviewer.style.title"), L.T("keyviewer.style.subtitle"));
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.background"), settings.BackgroundColor, value => settings.BackgroundColor = value, "keyviewer.background");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.pressed-background"), settings.PressedBackgroundColor, value => settings.PressedBackgroundColor = value, "keyviewer.pressed-background");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.outline"), settings.OutlineColor, value => settings.OutlineColor = value, "keyviewer.outline");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.pressed-outline"), settings.PressedOutlineColor, value => settings.PressedOutlineColor = value, "keyviewer.pressed-outline");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.slot.text"), settings.TextColor, value => settings.TextColor = value, "keyviewer.text");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.slot.pressed-text"), settings.PressedTextColor, value => settings.PressedTextColor = value, "keyviewer.pressed-text");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.rain"), settings.RainColor, value => settings.RainColor = value, "keyviewer.rain-color");
        AddKeyColorPicker(effectsCard, L.T("keyviewer.style.ghost-rain"), settings.GhostRainColor, value => settings.GhostRainColor = value, "keyviewer.ghost-color");
    }

    private void BuildKeyViewerMappingPage(RectTransform page)
    {
        var settings = _store!.Document.Preferences.KeyViewer;
        var mappingCard = CreateSettingsCard(page, L.T("keyviewer.mapping-editor.title"), L.T("keyviewer.mapping-editor.subtitle"));
        var choices = BuildBindingChoices();
        var selectedBindingChoice = choices.FirstOrDefault(choice => choice.Index == _selectedKeySlot && choice.IsFoot == _selectedKeyFoot)
                                    ?? choices.FirstOrDefault();
        if (selectedBindingChoice != null)
        {
            _selectedKeySlot = selectedBindingChoice.Index;
            _selectedKeyFoot = selectedBindingChoice.IsFoot;
        }
        _selectedBindingChoice = selectedBindingChoice;
        O5InputField? labelInput = null;
        O5Dropdown<KeyBindingChoice>? keyDropdown = null;
        var ghostToggle = O5Factory.Toggle(
            mappingCard,
            false,
            _editingGhostBindings,
            value =>
            {
                _editingGhostBindings = value;
                if (_selectedBindingChoice != null)
                {
                    labelInput?.Set(_keyViewer?.GetCustomSlotLabel(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, value) ?? string.Empty, false);
                }
                keyDropdown?.SetValues(choices);
                if (_selectedBindingChoice != null) keyDropdown?.Set(_selectedBindingChoice, false);
            },
            L.T("keyviewer.edit-ghost-keys"),
            "keyviewer.edit-ghost");
        Track(ghostToggle);

        if (selectedBindingChoice != null && _keyViewer != null)
        {
            var bindingDropdownRow = O5Factory.Row(mappingCard, 50f);
            keyDropdown = O5Factory.DropDown(
                bindingDropdownRow,
                selectedBindingChoice,
                selectedBindingChoice,
                choices,
                choice => FormatBindingChoice(choice),
                choice =>
                {
                    if (choice != null)
                    {
                        _selectedKeySlot = choice.Index;
                        _selectedKeyFoot = choice.IsFoot;
                        _selectedBindingChoice = choice;
                        labelInput?.Set(_keyViewer.GetCustomSlotLabel(choice.Index, choice.IsFoot, _editingGhostBindings), false);
                    }
                },
                "keyviewer.binding-slot");
            Track(keyDropdown);
            _keyBindingDropdown = keyDropdown;
        }

        var captureButton = O5Factory.Button(mappingCard, () =>
        {
            if (_selectedBindingChoice == null) return;
            _keyViewer?.BeginCapture(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings);
            _lastCaptureMessage = _keyViewer?.CaptureMessage ?? string.Empty;
        }, L.T("keyviewer.rebind"), "keyviewer.capture");
        Track(captureButton);
        labelInput = O5Factory.Input(mappingCard, null,
            _selectedBindingChoice == null ? string.Empty : _keyViewer?.GetCustomSlotLabel(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings) ?? string.Empty,
            value =>
            {
                if (_selectedBindingChoice != null)
                {
                    _keyViewer?.SetSlotLabel(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings, value);
                    keyDropdown?.Set(_selectedBindingChoice, false);
                }
            }, L.T("keyviewer.slot-label"), null, "keyviewer.binding-label");
        Track(labelInput);
        _keyBindingLabelInput = labelInput;
        var captureText = O5Factory.Row(mappingCard, 38f);
        _captureStatus = O5Factory.ControlText(captureText, O5Boot.Theme.FontSizeBody);
        _captureStatus.text = _keyViewer?.CaptureMessage ?? string.Empty;
        var resetLabel = O5Factory.Button(mappingCard, () =>
        {
            if (_selectedBindingChoice == null) return;
            _keyViewer?.SetSlotLabel(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings, string.Empty);
            labelInput?.Set(string.Empty, false);
            keyDropdown?.Set(_selectedBindingChoice, false);
        }, L.T("keyviewer.reset-label"), "keyviewer.reset-label");
        Track(resetLabel);
        var resetCounts = O5Factory.Button(mappingCard, () => _keyViewer?.ResetCounts(), L.T("keyviewer.reset-counts"), "keyviewer.reset-counts");
        Track(resetCounts);

        CreateSettingsCard(page, L.T("keyviewer.compat.title"), L.T("keyviewer.compat.subtitle"));
    }

    private void BuildKeyPreviewLane(Transform parent, bool foot, int count, string title)
    {
        var heading = O5Factory.Row(parent, 26f);
        var headingText = O5Factory.ControlText(heading, 13f, true);
        headingText.text = title;
        headingText.font = O5Boot.Fonts.Medium;
        headingText.color = new Color32(192, 190, 214, 255);
        headingText.characterSpacing = 0f;

        var columns = foot ? Math.Min(8, count) : Math.Min(10, count);
        var rows = Mathf.CeilToInt((float)count / Math.Max(1, columns));
        var gridObject = new GameObject(foot ? "FootKeyPreview" : "HandKeyPreview");
        gridObject.transform.SetParent(parent, false);
        var gridRect = gridObject.AddComponent<RectTransform>();
        var gridElement = gridObject.AddComponent<LayoutElement>();
        gridElement.preferredHeight = rows * 50f + Math.Max(0, rows - 1) * 6f;
        var grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(62f, 46f);
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.MiddleLeft;

        for (var i = 0; i < count; i++)
        {
            var slot = i;
            var button = O5Factory.Button(gridRect, () =>
            {
                SelectKeySlot(slot, foot);
            }, _keyViewer?.GetSlotLabel(slot, foot, false) ?? "—", $"keyviewer.preview.{(foot ? "foot" : "hand")}.{slot}");
            button.Label!.fontSize = 13f;
            button.Label.characterSpacing = 0f;
            button.Label.textWrappingMode = TextWrappingModes.NoWrap;
            button.Label.overflowMode = TextOverflowModes.Ellipsis;
            button.NormalColor = slot == _selectedKeySlot && foot == _selectedKeyFoot
                ? new Color32(73, 64, 112, 255)
                : O5Boot.Theme.ObjectBG;
            button.UpdateVisual(true);
            var outline = button.Background.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            outline.useGraphicAlpha = true;
            Track(button);
            _keyPreviewCells.Add(new KeyPreviewCell(button, outline, slot, foot, new List<GameObject>()));
        }
    }

    internal void SelectKeySlot(int slot, bool foot)
    {
        if (_keyEditorTool == "pan" || _pendingGeometry != null) return;
        var choice = _keyBindingDropdown?.Values.FirstOrDefault(item => item.Index == slot && item.IsFoot == foot)
                     ?? BuildBindingChoices().FirstOrDefault(item => item.Index == slot && item.IsFoot == foot);
        if (choice == null) return;
        _selectedKeySlot = slot;
        _selectedKeyFoot = foot;
        _selectedBindingChoice = choice;
        _keyBindingDropdown?.Set(choice, false);
        _keyBindingLabelInput?.Set(_keyViewer?.GetCustomSlotLabel(slot, foot, _editingGhostBindings) ?? string.Empty, false);
        RefreshKeyPreview(true);
        RefreshKeyInspector();
    }

    private void BuildLayoutPage(RectTransform page)
    {
        var profileCard = CreateSettingsCard(page, L.T("layout.profiles.title"), L.T("layout.profiles.subtitle"));
        if (_hudRuntime == null || _store == null) return;

        var profiles = _hudRuntime.LayoutProfiles.ToArray();
        var activeId = _hudRuntime.ActiveLayoutId;
        if (profiles.Length > 0)
        {
            var profileIds = profiles.Select(profile => profile.Id).ToArray();
            var profileDropdownRow = O5Factory.Row(profileCard, 50f);
            var dropdown = O5Factory.DropDown(
                profileDropdownRow,
                activeId,
                activeId,
                profileIds,
                id => profiles.FirstOrDefault(profile => profile.Id == id)?.Name ?? id,
                id =>
                {
                    if (id != null && _hudRuntime.ActivateLayout(id)) BuildWindowContent();
                },
                "layout.active");
            Track(dropdown);
        }

        var nameInput = O5Factory.Input(profileCard, null, string.Empty, null, L.T("layout.name"), null, "layout.name");
        Track(nameInput);
        Track(O5Factory.Button(profileCard, () =>
        {
            _hudRuntime.CreateLayout(nameInput.Value);
            BuildWindowContent();
        }, L.T("layout.duplicate"), "layout.create"));
        Track(O5Factory.Button(profileCard, () =>
        {
            _hudRuntime.RenameLayout(_hudRuntime.ActiveLayoutId, nameInput.Value);
            BuildWindowContent();
        }, L.T("layout.rename"), "layout.rename"));
        Track(O5Factory.Button(profileCard, () =>
        {
            _hudRuntime.DeleteLayout(_hudRuntime.ActiveLayoutId);
            BuildWindowContent();
        }, L.T("layout.delete"), "layout.delete"));

        var elementCard = CreateSettingsCard(page, L.T("layout.elements.title"), L.T("layout.elements.subtitle"));
        var elements = _hudRuntime.ActiveElements.OrderBy(element => element.Order).ToArray();
        var availableDefinitions = _hudRuntime.AvailableDefinitions;
        if (availableDefinitions.Count > 0)
        {
            AddSection(elementCard, L.T("layout.add.title"), L.T("layout.add.subtitle"));
            var selectedAvailableId = availableDefinitions[0].Id;
            var availableIds = availableDefinitions.Select(definition => definition.Id).ToArray();
            var availableDropdownRow = O5Factory.Row(elementCard, 50f);
            var availableDropdown = O5Factory.DropDown(
                availableDropdownRow,
                selectedAvailableId,
                selectedAvailableId,
                availableIds,
                id => availableDefinitions.First(definition => definition.Id == id).DisplayName,
                id => selectedAvailableId = id ?? selectedAvailableId,
                "layout.add-element");
            Track(availableDropdown);
            Track(O5Factory.Button(elementCard, () =>
            {
                if (_hudRuntime.CreateInstance(selectedAvailableId, out var instanceId))
                {
                    _selectedElementId = instanceId;
                    BuildWindowContent();
                }
            }, L.T("layout.add"), "layout.add-element-button"));
        }

        if (elements.Length == 0)
        {
            AddSection(elementCard, L.T("layout.empty.title"), L.T("layout.empty.body"));
            return;
        }

        if (elements.All(element => element.InstanceId != _selectedElementId)) _selectedElementId = elements[0].InstanceId;
        var elementIds = elements.Select(element => element.InstanceId).ToArray();
        var elementDropdownRow = O5Factory.Row(elementCard, 50f);
        var elementDropdown = O5Factory.DropDown(
            elementDropdownRow,
            _selectedElementId,
            _selectedElementId,
            elementIds,
            id =>
            {
                var item = elements.FirstOrDefault(element => element.InstanceId == id);
                return item == null ? id ?? string.Empty : _hudRuntime.GetElementName(item.ElementId);
            },
            id =>
            {
                _selectedElementId = id;
                BuildWindowContent();
            },
            "layout.element");
        Track(elementDropdown);

        var selectedElement = elements.FirstOrDefault(element => element.InstanceId == _selectedElementId);
        if (selectedElement == null) return;
        var selectedId = selectedElement.InstanceId;
        var anchors = (HudAnchor[])Enum.GetValues(typeof(HudAnchor));
        var selectedAnchor = Enum.TryParse(selectedElement.Anchor, true, out HudAnchor parsed) ? parsed : HudAnchor.Center;
        var anchorDropdownRow = O5Factory.Row(elementCard, 50f);
        var anchorDropdown = O5Factory.DropDown(
            anchorDropdownRow,
            selectedAnchor,
            selectedAnchor,
            anchors,
            FormatAnchor,
            anchor =>
            {
                if (_hudRuntime.TryGetElement(selectedId, out var data))
                {
                    _hudRuntime.SetBounds(selectedId, anchor, new Vector2(data.X, data.Y), new Vector2(data.Width, data.Height));
                }
            },
            "layout.anchor");
        Track(anchorDropdown);

        AddElementSlider(elementCard, selectedElement, L.T("layout.x"), -1600f, 1600f, selectedElement.X, value => new Vector2(value, selectedElement.Y), "layout.x");
        AddElementSlider(elementCard, selectedElement, L.T("layout.y"), -1000f, 1000f, selectedElement.Y, value => new Vector2(selectedElement.X, value), "layout.y");
        AddElementSlider(elementCard, selectedElement, L.T("common.width"), 32f, 1600f, selectedElement.Width, value => new Vector2(value, selectedElement.Height), "layout.width", true);
        AddElementSlider(elementCard, selectedElement, L.T("common.height"), 24f, 1000f, selectedElement.Height, value => new Vector2(selectedElement.Width, value), "layout.height", true);
        AddSlider(elementCard, L.T("layout.scale"), 1f, 0.1f, 4f, selectedElement.Scale,
            value => _hudRuntime.SetStyle(selectedId, value, selectedElement.Opacity, false), "layout.scale", "F2",
            value => _hudRuntime.SetStyle(selectedId, value, selectedElement.Opacity));
        AddSlider(elementCard, L.T("layout.opacity"), 1f, 0f, 1f, selectedElement.Opacity,
            value => _hudRuntime.SetStyle(selectedId, selectedElement.Scale, value, false), "layout.opacity", "F2",
            value => _hudRuntime.SetStyle(selectedId, selectedElement.Scale, value));
        Track(O5Factory.Button(elementCard, () => _hudRuntime.SetOrder(selectedId, selectedElement.Order + 1), L.T("layout.order-up"), "layout.order-up"));
        Track(O5Factory.Button(elementCard, () => _hudRuntime.SetOrder(selectedId, selectedElement.Order - 1), L.T("layout.order-down"), "layout.order-down"));
        Track(O5Factory.Button(elementCard, () =>
        {
            _hudRuntime.Remove(selectedId);
            _selectedElementId = null;
            BuildWindowContent();
        }, L.T("layout.remove"), "layout.remove"));
    }

    private void BuildAppearancePage(RectTransform page)
    {
        var featureCard = CreateSettingsCard(page, L.T("appearance.features.title"), L.T("appearance.features.subtitle"));
        var appearance = _store!.Document.Preferences.Appearance;
        AddPreferenceToggle(featureCard, L.T("appearance.planet-enabled"), appearance.ChangePlanetColor, value => { appearance.ChangePlanetColor = value; AppearanceCustomizer.Apply(appearance); }, "appearance.planet-enabled");
        AddPreferenceToggle(featureCard, L.T("appearance.tile-enabled"), appearance.ChangeTileColor, value => { appearance.ChangeTileColor = value; AppearanceCustomizer.Apply(appearance); }, "appearance.tile-enabled");
        AddPreferenceToggle(featureCard, L.T("appearance.auto-enabled"), appearance.ChangeAutoIcon, value => { appearance.ChangeAutoIcon = value; AppearanceCustomizer.Apply(appearance); }, "appearance.auto-enabled");
        AddPreferenceToggle(featureCard, L.T("appearance.logo-enabled"), appearance.ChangeLogoText, value => { appearance.ChangeLogoText = value; AppearanceCustomizer.Apply(appearance); }, "appearance.logo-enabled");

        var colorCard = CreateSettingsCard(page, L.T("appearance.colors.title"), L.T("appearance.colors.subtitle"));
        AddColorPicker(colorCard, L.T("appearance.planet-color"), appearance.PlanetColor, value => { appearance.PlanetColor = ColorUtility.ToHtmlStringRGBA(value); AppearanceCustomizer.Apply(appearance); }, "appearance.planet-color");
        AddColorPicker(colorCard, L.T("appearance.tile-color"), appearance.TileColor, value => { appearance.TileColor = ColorUtility.ToHtmlStringRGBA(value); AppearanceCustomizer.Apply(appearance); }, "appearance.tile-color");
        AddColorPicker(colorCard, L.T("appearance.logo-color"), appearance.LogoColor, value => { appearance.LogoColor = ColorUtility.ToHtmlStringRGBA(value); AppearanceCustomizer.Apply(appearance); }, "appearance.logo-color");

        var resourcesCard = CreateSettingsCard(page, L.T("appearance.resources.title"), L.T("appearance.resources.subtitle"));
        var logoInput = O5Factory.Input(resourcesCard, null, appearance.LogoTitle, value => { appearance.LogoTitle = value; _store.Save(); AppearanceCustomizer.Apply(appearance); }, L.T("appearance.logo-title"), null, "appearance.logo-title");
        Track(logoInput);
        var iconInput = O5Factory.Input(resourcesCard, null, appearance.AutoIconResourcePath, value => { appearance.AutoIconResourcePath = value; _store.Save(); AppearanceCustomizer.Apply(appearance); }, L.T("appearance.auto-path"), null, "appearance.auto-path");
        Track(iconInput);
        Track(O5Factory.Button(resourcesCard, () => AppearanceCustomizer.RestoreOriginals(), L.T("appearance.restore"), "appearance.restore"));
    }

    // Rebuild on the next frame: the change usually comes from a dropdown that the rebuild destroys.
    private bool _languageRebuildPending;

    private void OnLanguageChanged() => _languageRebuildPending = true;

    private void BuildGeneralPage(RectTransform page)
    {
        var languageCard = CreateSettingsCard(page, L.T("general.language"), string.Empty);
        var languageCodes = new List<string> { L.Auto };
        languageCodes.AddRange(L.Available);
        var currentLanguage = languageCodes.FindIndex(code =>
            string.Equals(code, _store!.Document.Preferences.Language, StringComparison.OrdinalIgnoreCase));
        AddDropdown(languageCard, 0, Math.Max(0, currentLanguage), Enumerable.Range(0, languageCodes.Count).ToArray(),
            index => index == 0 ? L.T("general.language.auto") : L.NativeName(languageCodes[index]),
            index =>
            {
                _store!.Document.Preferences.Language = languageCodes[index];
                _store.Save();
                L.SetLanguage(languageCodes[index]);
            }, "general.language");
        var displayCard = CreateSettingsCard(page, L.T("general.display.title"), L.T("general.display.subtitle"));
        var preferences = _store!.Document.Preferences;
        AddSlider(displayCard, L.T("general.hud-scale"), 1f, 0.25f, 3f, preferences.HudScale,
            value => _hudRuntime?.SetGlobalStyle(value, preferences.HudOpacity, false), "general.hud-scale", "F2",
            value => _hudRuntime?.SetGlobalStyle(value, preferences.HudOpacity, false));
        AddSlider(displayCard, L.T("general.hud-opacity"), 1f, 0f, 1f, preferences.HudOpacity,
            value => _hudRuntime?.SetGlobalStyle(preferences.HudScale, value, false), "general.hud-opacity", "F2",
            value => _hudRuntime?.SetGlobalStyle(preferences.HudScale, value, false));
        AddSlider(displayCard, L.T("general.font-size"), 28f, 12f, 96f, preferences.Hud.FontSize,
            value => preferences.Hud.FontSize = value, "general.font-size", "F0");
        var helpCard = CreateSettingsCard(page, L.T("general.help.title"), L.T("general.help.subtitle"));
        AddSection(helpCard, L.T("general.config-file"), System.IO.Path.Combine(Application.persistentDataPath, "Argon", "config.json"));
        Track(O5Factory.Button(helpCard, () => { _store.Save(); _store.Flush(); }, L.T("general.save"), "general.save"));
    }

    private RectTransform CreateEditorPage(string id)
    {
        var pageObject = new GameObject("Page." + id);
        pageObject.transform.SetParent(_pagesHost!, false);
        var pageRect = pageObject.AddComponent<RectTransform>();
        pageRect.anchorMin = Vector2.zero;
        pageRect.anchorMax = Vector2.one;
        pageRect.offsetMin = Vector2.zero;
        pageRect.offsetMax = Vector2.zero;
        _pages.Add(id, pageRect);
        return pageRect;
    }

    private RectTransform CreateScrollPage(string id)
    {
        var pageObject = new GameObject("Page." + id);
        pageObject.transform.SetParent(_pagesHost!, false);
        var pageRect = pageObject.AddComponent<RectTransform>();
        pageRect.anchorMin = Vector2.zero;
        pageRect.anchorMax = Vector2.one;
        pageRect.offsetMin = Vector2.zero;
        pageRect.offsetMax = Vector2.zero;

        var viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(pageRect, false);
        var viewportRect = viewportObject.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        var viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        viewportObject.AddComponent<RectMask2D>();

        var contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewportRect, false);
        var contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        var vertical = contentObject.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(16, 16, 16, 20);
        vertical.spacing = 12f;
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        var fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = pageObject.AddComponent<UIScrollController>();
        scroll.wheelStrength = 42f;
        scroll.SetContent(contentRect, viewportRect);
        _pages.Add(id, pageRect);
        return contentRect;
    }

    private void AddNavigationButton(Transform parent, string label, string page)
    {
        var button = O5Factory.Button(parent, () => ShowPage(page), label, "argon.page." + page);
        var layout = button.Rect.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minHeight = 38f;
            layout.preferredHeight = 38f;
        }
        if (button.Label != null)
        {
            button.Label.alignment = TextAlignmentOptions.Left;
            button.Label.characterSpacing = 0f;
            button.Label.fontSize = 14f;
            button.Label.rectTransform.offsetMin = new Vector2(14f, 0f);
            button.Label.rectTransform.offsetMax = new Vector2(-5f, 0f);
        }
        button.NormalColor = new Color(0f, 0f, 0f, 0f);
        button.UpdateVisual(true);
        _navigationButtons[page] = button;
        Track(button);
    }

    private void AddSection(Transform parent, string title, string description)
    {
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        var row = O5Factory.Row(parent, hasDescription ? 64f : 37f);
        var accentObject = new GameObject("SectionAccent");
        accentObject.transform.SetParent(row, false);
        var accentRect = accentObject.AddComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0.5f);
        accentRect.anchorMax = new Vector2(0f, 0.5f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(3f, 26f);
        accentObject.AddComponent<Image>().color = O5Boot.Theme.ObjectActive;

        var titleText = O5Factory.ControlText(row, 19f, true);
        titleText.text = title;
        titleText.font = O5Boot.Fonts.Medium;
        titleText.characterSpacing = 0f;
        titleText.rectTransform.anchorMin = new Vector2(0f, hasDescription ? 0.46f : 0f);
        titleText.rectTransform.anchorMax = Vector2.one;
        titleText.rectTransform.offsetMin = new Vector2(14f, 0f);
        titleText.rectTransform.offsetMax = Vector2.zero;

        if (hasDescription)
        {
            var descriptionText = O5Factory.ControlText(row, 12f, true);
            descriptionText.text = description;
            descriptionText.color = new Color32(148, 150, 169, 255);
            descriptionText.characterSpacing = 0f;
            descriptionText.textWrappingMode = TextWrappingModes.Normal;
            descriptionText.rectTransform.anchorMin = Vector2.zero;
            descriptionText.rectTransform.anchorMax = new Vector2(1f, 0.48f);
            descriptionText.rectTransform.offsetMin = new Vector2(14f, 1f);
            descriptionText.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void AddPreferenceToggle(Transform parent, string label, bool value, Action<bool> onChanged, string id)
    {
        var toggle = O5Factory.Toggle(parent, value, value, next =>
        {
            onChanged(next);
            _store?.Save();
        }, label, "argon.option." + id);
        ConfigureToggleLabel(toggle.Label, 14f, 50f);
        Track(toggle);
    }

    private static void ConfigureToggleLabel(TMP_Text label, float fontSize, float rightInset)
    {
        label.characterSpacing = 0f;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = fontSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.offsetMin = new Vector2(12f, 0f);
        label.rectTransform.offsetMax = new Vector2(-rightInset, 0f);
    }

    private void AddSlider(Transform parent, string label, float defaultValue, float min, float max, float value,
        Action<float> onChanged, string id, string format = "F2", Action<float>? onComplete = null)
    {
        var slider = O5Factory.Slider(
            parent,
            defaultValue,
            min,
            max,
            value,
            format,
            ClampMode.All,
            null,
            onChanged,
            completed =>
            {
                onComplete?.Invoke(completed);
                _store?.Save();
            },
            label,
            "argon.slider." + id);
        slider.Label.characterSpacing = 0f;
        slider.Label.fontSize = 13f;
        slider.Label.textWrappingMode = TextWrappingModes.Normal;
        slider.Label.rectTransform.offsetMin = new Vector2(12f, 0f);
        slider.Label.rectTransform.offsetMax = new Vector2(-12f, 0f);
        Track(slider);
    }

    private void AddDropdown(Transform parent, int defaultValue, int value, IReadOnlyList<int> options, Func<int, string> display, Action<int> onChanged, string id)
    {
        var dropdownRow = O5Factory.Row(parent, 50f);
        var dropdown = O5Factory.DropDown(dropdownRow, defaultValue, value, options, display, selected =>
        {
            onChanged(selected);
        }, "argon.dropdown." + id);
        Track(dropdown);
    }

    private void AddElementSlider(RectTransform parent, HudElementLayoutData element, string label, float min, float max,
        float value, Func<float, Vector2> positionOrSize, string id, bool isSize = false)
    {
        var runtime = _hudRuntime;
        if (runtime == null) return;
        AddSlider(parent, label, value, min, max, value, next =>
        {
            if (runtime.TryGetElement(element.InstanceId, out var current))
            {
                var pair = positionOrSize(next);
                var anchor = Enum.TryParse(current.Anchor, true, out HudAnchor parsed) ? parsed : HudAnchor.Center;
                var position = isSize ? new Vector2(current.X, current.Y) : pair;
                var size = isSize ? pair : new Vector2(current.Width, current.Height);
                runtime.SetBounds(element.InstanceId, anchor, position, size, false);
            }
        }, id, "F0", _ => runtime.SaveLayout());
    }

    private void AddColorPicker(Transform parent, string label, string hex, Action<Color> onChanged, string id)
    {
        if (!ColorUtility.TryParseHtmlString(hex.StartsWith("#", StringComparison.Ordinal) ? hex : "#" + hex, out var current)) current = Color.white;
        var canvasRect = _windowManager?.Canvas.Root.GetComponent<RectTransform>();
        if (canvasRect == null) return;
        var picker = O5Factory.ColorPicker(parent, canvasRect, null, current, current, color =>
        {
            onChanged(color);
        }, color =>
        {
            onChanged(color);
            _store?.Save();
        }, "argon.color." + id, label);
        Track(picker);
    }

    private void AddKeyColorPicker(Transform parent, string label, string hex, Action<string> onChanged, string id)
    {
        AddColorPicker(parent, label, hex, color =>
        {
            onChanged(ColorUtility.ToHtmlStringRGBA(color));
            _keyViewer?.RefreshColors();
            _keyPreviewPaletteRevision++;
            RefreshKeyPreview(true);
        }, id);
    }

    private List<KeyBindingChoice> BuildBindingChoices()
    {
        var result = new List<KeyBindingChoice>();
        var settings = _store!.Document.Preferences.KeyViewer;
        for (var i = 0; i < settings.HandKeyCount; i++) result.Add(new KeyBindingChoice(i, false));
        for (var i = 0; i < settings.FootKeyCount; i++) result.Add(new KeyBindingChoice(i, true));
        return result;
    }

    private string FormatBindingChoice(KeyBindingChoice choice)
    {
        var name = _keyViewer?.GetSlotLabel(choice.Index, choice.IsFoot, _editingGhostBindings) ?? L.T("common.none");
        return L.F("keyviewer.slot-name", L.T(choice.IsFoot ? "keyviewer.foot" : "keyviewer.hand"), choice.Index + 1, name);
    }

    private void ShowPage(string id)
    {
        if (!_pages.ContainsKey(id)) return;
        _activePage = id;
        var editor = id == "keyviewer";
        var shell = _window!.Content;
        shell.Find("ArgonSidebar").gameObject.SetActive(!editor);
        var main = (RectTransform)shell.Find("ArgonMainContent");
        main.offsetMin = new Vector2(editor ? 0f : 224f, 0f);
        main.Find("ArgonPageHeader").gameObject.SetActive(!editor);
        main.Find("HeaderDivider").gameObject.SetActive(!editor);
        _pagesHost!.offsetMax = new Vector2(0f, editor ? 0f : -88f);
        foreach (var pair in _pages) pair.Value.gameObject.SetActive(pair.Key == id);
        foreach (var pair in _navigationButtons)
        {
            var active = pair.Key == id;
            pair.Value.NormalColor = active
                ? new Color32(68, 60, 103, 255)
                : new Color(0f, 0f, 0f, 0f);
            if (pair.Value.Label != null)
            {
                pair.Value.Label.color = active
                    ? new Color32(228, 222, 255, 255)
                    : new Color32(170, 171, 190, 255);
            }
            pair.Value.UpdateVisual(true);
        }

        if (_pageTitle != null && _pageSubtitle != null)
        {
            (_pageTitle.text, _pageSubtitle.text) = id switch
            {
                "overview" => (L.T("nav.overview"), L.T("page.overview.subtitle")),
                "hud" => ("HUD", L.T("page.hud.subtitle")),
                "keyviewer" => (L.T("nav.keyviewer"), L.T("page.keyviewer.subtitle")),
                "layout" => (L.T("page.layout.title"), L.T("page.layout.subtitle")),
                "appearance" => (L.T("nav.appearance"), L.T("page.appearance.subtitle")),
                _ => (L.T("nav.general"), L.T("page.general.subtitle")),
            };
        }
        SettleEditorLayout();
    }

    private void SetHudEditMode(bool enabled)
    {
        if (_editingHud == enabled)
        {
            if (_hudEditToggle != null && _hudEditToggle.Value != enabled) _hudEditToggle.Set(enabled, false);
            return;
        }
        _editingHud = enabled;
        if (_hudEditToggle != null && _hudEditToggle.Value != enabled) _hudEditToggle.Set(enabled, false);
        _hudRuntime?.SetEditMode(enabled);
        if (enabled)
        {
            _savedCursorVisible = Cursor.visible;
            _savedCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            HideWindow();
        }
        else
        {
            Cursor.lockState = _savedCursorLock;
            Cursor.visible = _savedCursorVisible;
        }
    }

    private void UpdateEditorShortcuts()
    {
        if (!Application.isFocused || _activePage != "keyviewer" || _keyEditorCanvas == null ||
            !_keyEditorCanvas.gameObject.activeInHierarchy || _keyViewer == null || _keyViewer.IsCapturing) return;
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused != null && (focused.GetComponentInParent<TMP_InputField>() != null ||
                                focused.GetComponentInParent<InputField>() != null)) return;
        if (Input.GetKeyDown(KeyCode.Escape) && _pendingGeometry != null)
        {
            var edit = _pendingGeometry;
            _pendingGeometry = null;
            _keyViewer.RestoreSlotGeometry(edit.Slot, edit.Foot, edit.BeforeOffset, edit.BeforeOverride);
            RefreshEditorPreviewLayout();
            RefreshKeyInspector();
            return;
        }
        if (_pendingGeometry != null) return;
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            if (Input.GetKeyDown(KeyCode.Z)) RestoreEditorGeometry(shift);
            else if (Input.GetKeyDown(KeyCode.Y)) RestoreEditorGeometry(true);
            return;
        }
        var step = shift ? 10f : 1f;
        var delta = Vector2.zero;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) delta.x -= step;
        if (Input.GetKeyDown(KeyCode.RightArrow)) delta.x += step;
        if (Input.GetKeyDown(KeyCode.DownArrow)) delta.y -= step;
        if (Input.GetKeyDown(KeyCode.UpArrow)) delta.y += step;
        if (delta != Vector2.zero)
        {
            BeginEditorGeometry(_selectedKeySlot, _selectedKeyFoot);
            _keyViewer.SetSlotOffset(_selectedKeySlot, _selectedKeyFoot,
                _keyViewer.GetSlotOffset(_selectedKeySlot, _selectedKeyFoot) + delta, false);
            CommitEditorGeometry();
            _store?.Save();
            RefreshEditorPreviewLayout();
            RefreshKeyInspector();
        }
    }

    private int _editorKpsShown = -1;
    private int _editorTotalShown = -1;

    private void Update()
    {
        if (_languageRebuildPending)
        {
            _languageRebuildPending = false;
            BuildWindowContent();
        }

        UpdateEditorShortcuts();
        _hudRuntime?.Tick(Time.unscaledDeltaTime);
        _keyViewer?.UpdateRuntime();
        if (_editorKpsText != null && _keyViewer != null && _keyViewer.CurrentKps != _editorKpsShown)
        {
            _editorKpsShown = _keyViewer.CurrentKps;
            NumberText.Set(_editorKpsText, _editorKpsShown);
        }
        if (_editorTotalText != null && _keyViewer != null && _keyViewer.TotalCount != _editorTotalShown)
        {
            _editorTotalShown = _keyViewer.TotalCount;
            NumberText.Set(_editorTotalText, _editorTotalShown);
        }
        if (_editorSelectedName != null && _selectedBindingChoice != null && _keyViewer != null)
        {
            var value = _keyViewer.GetSlotLabel(
                _selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings);
            if (_editorSelectedName.text != value) _editorSelectedName.text = value;
        }
        RefreshKeyPreview();
        _appearanceCustomizer?.Tick();
        _fontFallback?.Tick();
        _store?.Tick(GameStateSource.Current.InGame && GameStateSource.Current.State == "PlayerControl");
        O5Object.TickAll();
        O5Tooltip.Tick();
        O5ShortcutManager.HandleUpdate();

        if (_editingHud && Input.GetKeyDown(KeyCode.Escape))
        {
            SetHudEditMode(false);
        }

        if (_captureStatus != null && _keyViewer != null)
        {
            _captureStatus.text = _keyViewer.CaptureMessage;
            var message = _keyViewer.CaptureMessage;
            if (!string.IsNullOrEmpty(message) && message != _lastCaptureMessage && !_keyViewer.IsCapturing)
            {
                _lastCaptureMessage = message;
                if (_selectedBindingChoice != null)
                {
                    _keyBindingDropdown?.Set(_selectedBindingChoice, false);
                    _keyBindingLabelInput?.Set(
                        _keyViewer.GetCustomSlotLabel(_selectedBindingChoice.Index, _selectedBindingChoice.IsFoot, _editingGhostBindings),
                        false);
                }
            }
        }

        if (_apiRegistryRevision != ArgonApi.RegistryRevision)
        {
            _apiRegistryRevision = ArgonApi.RegistryRevision;
            BuildWindowContent();
        }
    }

    internal void ShowWindow()
    {
        if (_window != null)
        {
            _window.Rect.gameObject.SetActive(true);
            SettleEditorLayout();
        }
    }

    internal void HideWindow()
    {
        if (_window != null)
        {
            _window.Rect.gameObject.SetActive(false);
        }
    }

    private void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Rect.gameObject.SetActive(!_window.Rect.gameObject.activeSelf);
            if (_window.Rect.gameObject.activeSelf) SettleEditorLayout();
        }
    }

    private void OnCloseRequested(O5Window window)
    {
        if (_editingHud) SetHudEditMode(false);
        window.Rect.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        DisposeUi();
    }

    private bool _uiDisposed;

    private void DisposeUi()
    {
        // Runs synchronously from Destroy(); the deferred OnDestroy must not tear down a newer host's
        // shortcut, patches or API binding after a same-frame off/on toggle.
        if (_uiDisposed) return;
        _uiDisposed = true;
        if (_editingHud)
        {
            _hudRuntime?.SetEditMode(false);
            Cursor.lockState = _savedCursorLock;
            Cursor.visible = _savedCursorVisible;
            _editingHud = false;
        }

        L.Changed -= OnLanguageChanged;
        O5ShortcutManager.Unregister(ToggleWindowShortcutId);
        if (_window != null) _window.CloseRequested -= OnCloseRequested;
        DisposeControls();
        _windowManager?.Dispose();
        _windowManager = null;
        _eventBridge?.Dispose();
        _eventBridge = null;
        _fontFallback?.Dispose();
        _fontFallback = null;
        _appearanceCustomizer?.Dispose();
        _appearanceCustomizer = null;
        _keyViewer?.Dispose();
        _keyViewer = null;
        _hudRuntime?.Dispose();
        _hudRuntime = null;
        _store?.Flush();
        _store = null;
        _window = null;
        if (_argonTheme != null && ReferenceEquals(O5Boot.Theme, _argonTheme) && _previousTheme != null)
        {
            O5Boot.SetTheme(_previousTheme);
        }
        _argonTheme = null;
        _previousTheme = null;
    }

    private void DisposeControls()
    {
        foreach (var control in _controls)
        {
            control.Dispose();
        }

        _controls.Clear();
        _hudEditToggle = null;
    }

    private void Track<T>(T control) where T : O5Object
    {
        _controls.Add(control);
    }

    private void RefreshKeyPreview(bool force = false)
    {
        if (_activePage != "keyviewer" || _keyViewer == null || _store == null) return;
        // The settings window is hidden during play; nothing here is visible then.
        if (!force && (_window == null || !_window.Rect.gameObject.activeInHierarchy)) return;
        foreach (var cell in _keyPreviewCells)
        {
            var label = _keyViewer.GetSlotLabel(cell.Slot, cell.Foot, false);
            var pressed = _keyViewer.IsSlotPressed(cell.Slot, cell.Foot);
            var selected = cell.Slot == _selectedKeySlot && cell.Foot == _selectedKeyFoot;
            var showCounter = _keyViewer.GetSlotCounterVisible(cell.Slot, cell.Foot);
            var count = _keyViewer.GetSlotCount(cell.Slot, cell.Foot);
            if (force || cell.LastShowCounter != showCounter || !Mathf.Approximately(cell.LastZoom, _keyEditorZoom))
            {
                cell.LastShowCounter = showCounter;
                cell.LastZoom = _keyEditorZoom;
                cell.LastCount = -1;
                cell.Counter!.gameObject.SetActive(showCounter);
                cell.Counter.fontSize = 11f * _keyEditorZoom;
                if (cell.Button.Label != null)
                    cell.Button.Label.rectTransform.anchorMin = new Vector2(0f, showCounter ? 0.3f : 0f);
            }

            if (showCounter && count != cell.LastCount)
            {
                cell.LastCount = count;
                NumberText.Set(cell.Counter!, count);
            }
            if (!force && cell.LastLabel == label && cell.LastPressed == pressed && cell.LastSelected == selected &&
                cell.LastPaletteRevision == _keyPreviewPaletteRevision) continue;

            cell.LastLabel = label;
            cell.LastPressed = pressed;
            cell.LastSelected = selected;
            cell.LastPaletteRevision = _keyPreviewPaletteRevision;
            foreach (var handle in cell.Handles) handle.SetActive(selected);
            var background = ParseColor(_keyViewer.GetSlotColor(cell.Slot, cell.Foot,
                pressed ? "pressed-background" : "background"), O5Boot.Theme.ObjectBG);
            var outline = selected
                ? (Color)new Color32(151, 115, 255, 255)
                : ParseColor(_keyViewer.GetSlotColor(cell.Slot, cell.Foot,
                    pressed ? "pressed-outline" : "outline"), Color.white);
            var textColor = ParseColor(_keyViewer.GetSlotColor(cell.Slot, cell.Foot,
                pressed ? "pressed-text" : "text"), pressed ? Color.black : Color.white);
            cell.Button.NormalColor = background;
            cell.Button.UpdateVisual(true);
            cell.Surface.color = background;
            cell.Surface.BorderColor = outline;
            cell.Surface.SetVerticesDirty();
            cell.Outline.effectColor = outline;
            cell.Counter!.color = textColor;
            if (cell.Button.Label != null)
            {
                cell.Button.Label.text = label;
                cell.Button.Label.color = textColor;
            }
        }
    }

    private static Color ParseColor(string value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return ColorUtility.TryParseHtmlString(value.StartsWith("#", StringComparison.Ordinal) ? value : "#" + value, out var color)
            ? color
            : fallback;
    }

    private static string FormatAnchor(HudAnchor anchor)
    {
        return anchor switch
        {
            HudAnchor.TopLeft => L.T("anchor.top-left"),
            HudAnchor.TopCenter => L.T("anchor.top-center"),
            HudAnchor.TopRight => L.T("anchor.top-right"),
            HudAnchor.MiddleLeft => L.T("anchor.middle-left"),
            HudAnchor.Center => L.T("anchor.center"),
            HudAnchor.MiddleRight => L.T("anchor.middle-right"),
            HudAnchor.BottomLeft => L.T("anchor.bottom-left"),
            HudAnchor.BottomCenter => L.T("anchor.bottom-center"),
            HudAnchor.BottomRight => L.T("anchor.bottom-right"),
            _ => anchor.ToString(),
        };
    }

    internal static void Destroy(ArgonHost host)
    {
        if (host == null) return;
        host.DisposeUi();
        Object.Destroy(host.gameObject);
    }

    private sealed class KeyBindingChoice
    {
        internal int Index { get; }
        internal bool IsFoot { get; }

        internal KeyBindingChoice(int index, bool foot)
        {
            Index = index;
            IsFoot = foot;
        }
    }

    private sealed class KeyPreviewCell
    {
        internal O5Button Button { get; }
        internal Outline Outline { get; }
        internal KeycapGraphic Surface { get; }
        internal int Slot { get; }
        internal bool Foot { get; }
        internal List<GameObject> Handles { get; }
        internal TMP_Text? Counter { get; set; }
        internal string LastLabel { get; set; } = string.Empty;
        internal bool LastPressed { get; set; }
        internal bool LastSelected { get; set; }
        internal int LastPaletteRevision { get; set; } = -1;
        internal bool? LastShowCounter { get; set; }
        internal float LastZoom { get; set; } = -1f;
        internal int LastCount { get; set; } = -1;

        internal KeyPreviewCell(O5Button button, Outline outline, int slot, bool foot, List<GameObject> handles)
        {
            Button = button;
            Outline = outline;
            outline.enabled = false;
            button.Background.enabled = false;
            var surfaceObject = new GameObject("KeycapSurface");
            surfaceObject.transform.SetParent(button.Rect, false);
            surfaceObject.transform.SetAsFirstSibling();
            var surfaceRect = surfaceObject.AddComponent<RectTransform>();
            surfaceRect.anchorMin = Vector2.zero;
            surfaceRect.anchorMax = Vector2.one;
            surfaceRect.offsetMin = surfaceRect.offsetMax = Vector2.zero;
            Surface = surfaceObject.AddComponent<KeycapGraphic>();
            if (button.Label != null) button.Label.fontStyle = FontStyles.Bold;
            Slot = slot;
            Foot = foot;
            Handles = handles;
        }
    }
}

internal sealed class KeyEditorCanvasHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    private ArgonHost? _host;
    internal void Initialize(ArgonHost host) => _host = host;
    public void OnBeginDrag(PointerEventData data) { }
    public void OnDrag(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Middle ||
            (data.button == PointerEventData.InputButton.Left && _host?.EditorPanTool == true))
            _host?.PanEditor(data.delta);
    }
    public void OnScroll(PointerEventData data) => _host?.ScrollEditor(data);
}

internal sealed class KeyPreviewResizeHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IScrollHandler
{
    private ArgonHost? _host;
    private int _slot;
    private bool _foot;
    private Vector2 _anchor;

    internal void Initialize(ArgonHost host, int slot, bool foot, Vector2 anchor)
    {
        _host = host;
        _slot = slot;
        _foot = foot;
        _anchor = anchor;
    }

    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData) { }
    public void OnScroll(PointerEventData eventData) => _host?.ScrollEditor(eventData);
    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Middle ||
            (eventData.button == PointerEventData.InputButton.Left && _host?.EditorPanTool == true))
            _host?.PanEditor(eventData.delta);
        else if (eventData.button == PointerEventData.InputButton.Left)
            _host?.ResizeEditorPreviewKey(_slot, _foot, _anchor, eventData.delta);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) _host?.FinishEditorPreviewDrag();
    }
}

internal sealed class KeyPreviewDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerDownHandler, IPointerClickHandler
{
    private ArgonHost? _host;
    private int _slot;
    private bool _foot;
    private bool _dragged;

    public void OnScroll(PointerEventData eventData) => _host?.ScrollEditor(eventData);
    public void OnPointerDown(PointerEventData eventData) => _dragged = false;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !_dragged)
            _host?.SelectKeySlot(_slot, _foot);
    }

    internal void Initialize(ArgonHost host, int slot, bool foot)
    {
        _host = host;
        _slot = slot;
        _foot = foot;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragged = true;
        if (eventData.button == PointerEventData.InputButton.Left && _host?.EditorPanTool == false)
            _host?.MoveEditorPreviewKey(_slot, _foot, Vector2.zero);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Middle ||
            (eventData.button == PointerEventData.InputButton.Left && _host?.EditorPanTool == true))
            _host?.PanEditor(eventData.delta);
        else if (eventData.button == PointerEventData.InputButton.Left)
            _host?.MoveEditorPreviewKey(_slot, _foot, eventData.delta);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && _host?.EditorPanTool == false)
            _host?.FinishEditorPreviewDrag();
    }
}
