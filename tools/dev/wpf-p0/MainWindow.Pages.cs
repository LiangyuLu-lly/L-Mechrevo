using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class MainWindow
{
    private Pages.HomePageView? _homeView;
    private ScrollViewer HomePage = null!;
    private UniformGrid SensorBlocksGrid = null!;
    private ColumnDefinition HomePerfColumn = null!;
    private ColumnDefinition HomeMidColumn = null!;
    private ColumnDefinition HomeGpuColumn = null!;
    private Border HomePerfCard = null!;
    private TextBlock PerfCurrentText = null!;
    private ToggleButton HomePerf静音 = null!;
    private ToggleButton HomePerf平衡 = null!;
    private ToggleButton? HomePerf静音狂暴;
    private ToggleButton? HomePerf极致;
    private CheckBox HomeGpuOcToggle = null!;
    private ToggleButton HomePerf狂暴 = null!;
    private ToggleButton HomePerf自定义 = null!;
    private Border HomeGpuCard = null!;
    private TextBlock GpuCurrentText = null!;
    private ToggleButton HomeGpu混合 = null!;
    private ToggleButton HomeGpu独显 = null!;
    private ToggleButton HomeGpu集显 = null!;
    private StackPanel HomeIgpuSavingPanel = null!;
    private ColumnDefinition HomeDisplayColumn = null!;
    private ColumnDefinition HomeDisplayGap = null!;
    private ColumnDefinition HomeBatteryColumn = null!;
    private Border HomeDisplayCard = null!;
    private Grid DisplayInfoRow = null!;
    private TextBlock DisplayInfoText = null!;
    private StackPanel DisplayRefreshPanel = null!;
    private StackPanel DisplayDetailsPanel = null!;
    private ContentControl BatteryCardHost = null!;

    private Pages.ScreenPageView? _screenView;
    private ScrollViewer ScreenPage = null!;
    private Border ScreenCalibrationCard = null!;
    private ToggleButton ScreenCalibrationToggle = null!;
    private WrapPanel ScreenCalibrationOptionsPanel = null!;
    private Button ScreenCalibrationSettingsButton = null!;
    private StackPanel ScreenCalibrationSettingsPanel = null!;
    private CheckBox CalibrationStartupSwitch = null!;
    private CheckBox CalibrationCustomSwitch = null!;
    private ComboBox CalibrationIccCombo = null!;
    private Button ScreenPageEventControl1 = null!;
    private Border ScreenFeaturesCard = null!;
    private StackPanel ScreenFeaturesPanel = null!;
    private Border ScreenEffectCard = null!;
    private ToggleButton ScreenEffectToggle = null!;
    private WrapPanel ScreenEffectOptionsPanel = null!;
    private Button ScreenEffectEditButton = null!;
    private StackPanel ScreenEffectEditorPanel = null!;
    private Button ScreenPageEventControl2 = null!;
    private StackPanel ScreenEffectSliderPanel = null!;

    private Pages.LightingPageView? _lightingView;
    private ScrollViewer LightingPage = null!;
    private ToggleButton LightingOffBatteryToggle = null!;
    private WrapPanel LightingIdlePanel = null!;
    private StackPanel LightingCardsGrid = null!;

    private Pages.CustomPageView? _customView;
    private ScrollViewer CustomPage = null!;
    private ContentControl CustomModeHost = null!;
    private ContentControl CpuFanCurveHost = null!;
    private ContentControl GpuFanCurveHost = null!;

    private void EnsureHomeView()
    {
        if (_homeView != null) return;
        _homeView = new Pages.HomePageView();
        HomePage = _homeView.HomePage;
        SensorBlocksGrid = _homeView.SensorBlocksGrid;
        HomePerfColumn = _homeView.HomePerfColumn;
        HomeMidColumn = _homeView.HomeMidColumn;
        HomeGpuColumn = _homeView.HomeGpuColumn;
        HomePerfCard = _homeView.HomePerfCard;
        PerfCurrentText = _homeView.PerfCurrentText;
        HomePerf静音 = _homeView.HomePerf静音;
        HomePerf平衡 = _homeView.HomePerf平衡;
        HomePerf狂暴 = _homeView.HomePerf狂暴;
        HomePerf自定义 = _homeView.HomePerf自定义;
        HomeGpuCard = _homeView.HomeGpuCard;
        GpuCurrentText = _homeView.GpuCurrentText;
        HomeGpu混合 = _homeView.HomeGpu混合;
        HomeGpu独显 = _homeView.HomeGpu独显;
        HomeGpu集显 = _homeView.HomeGpu集显;
        HomeGpuOcToggle = _homeView.HomeGpuOcToggle;
        HomeIgpuSavingPanel = _homeView.HomeIgpuSavingPanel;
        HomeDisplayColumn = _homeView.HomeDisplayColumn;
        HomeDisplayGap = _homeView.HomeDisplayGap;
        HomeBatteryColumn = _homeView.HomeBatteryColumn;
        HomeDisplayCard = _homeView.HomeDisplayCard;
        DisplayInfoRow = _homeView.DisplayInfoRow;
        DisplayInfoText = _homeView.DisplayInfoText;
        DisplayRefreshPanel = _homeView.DisplayRefreshPanel;
        DisplayDetailsPanel = _homeView.DisplayDetailsPanel;
        BatteryCardHost = _homeView.BatteryCardHost;
        HomeHost.Content = _homeView;
        WireHomeEvents();
    }

    private void EnsureScreenView()
    {
        if (_screenView != null) return;
        _screenView = new Pages.ScreenPageView();
        ScreenPage = _screenView.ScreenPage;
        ScreenCalibrationCard = _screenView.ScreenCalibrationCard;
        ScreenCalibrationToggle = _screenView.ScreenCalibrationToggle;
        ScreenCalibrationOptionsPanel = _screenView.ScreenCalibrationOptionsPanel;
        ScreenCalibrationSettingsButton = _screenView.ScreenCalibrationSettingsButton;
        ScreenCalibrationSettingsPanel = _screenView.ScreenCalibrationSettingsPanel;
        CalibrationStartupSwitch = _screenView.CalibrationStartupSwitch;
        CalibrationCustomSwitch = _screenView.CalibrationCustomSwitch;
        CalibrationIccCombo = _screenView.CalibrationIccCombo;
        ScreenPageEventControl1 = _screenView.ScreenPageEventControl1;
        ScreenFeaturesCard = _screenView.ScreenFeaturesCard;
        ScreenFeaturesPanel = _screenView.ScreenFeaturesPanel;
        ScreenEffectCard = _screenView.ScreenEffectCard;
        ScreenEffectToggle = _screenView.ScreenEffectToggle;
        ScreenEffectOptionsPanel = _screenView.ScreenEffectOptionsPanel;
        ScreenEffectEditButton = _screenView.ScreenEffectEditButton;
        ScreenEffectEditorPanel = _screenView.ScreenEffectEditorPanel;
        ScreenPageEventControl2 = _screenView.ScreenPageEventControl2;
        ScreenEffectSliderPanel = _screenView.ScreenEffectSliderPanel;
        ScreenHost.Content = _screenView;
        BuildScreenControls();
        WireScreenEvents();
    }

    private void EnsureLightingView()
    {
        if (_lightingView != null) return;
        _lightingView = new Pages.LightingPageView();
        LightingPage = _lightingView.LightingPage;
        LightingOffBatteryToggle = _lightingView.LightingOffBatteryToggle;
        LightingIdlePanel = _lightingView.LightingIdlePanel;
        LightingCardsGrid = _lightingView.LightingCardsGrid;
        LightingHost.Content = _lightingView;
        BuildLightingCards();
        WireLightingEvents();
    }

    private void EnsureCustomView()
    {
        if (_customView != null) return;
        _customView = new Pages.CustomPageView();
        CustomPage = _customView.CustomPage;
        CustomModeHost = _customView.CustomModeHost;
        CpuFanCurveHost = _customView.CpuFanCurveHost;
        GpuFanCurveHost = _customView.GpuFanCurveHost;
        CustomHost.Content = _customView;
        EmbedCustomEditors();
        WireCustomEvents();
    }

    private void WireHomeEvents()
    {
        HomePerf静音.Click += Mode_Click;
        HomePerf平衡.Click += Mode_Click;
        HomePerf狂暴.Click += Mode_Click;
        HomePerf自定义.Click += Mode_Click;
        HomeGpu混合.Click += Gpu_Click;
        HomeGpu独显.Click += Gpu_Click;
        HomeGpu集显.Click += Gpu_Click;
        HomeGpuOcToggle.Click += HomeGpuOcToggle_Click;
    }

    private void WireScreenEvents()
    {
        ScreenCalibrationToggle.Click += ScreenCalibrationToggle_Click;
        ScreenCalibrationSettingsButton.Click += ScreenCalibrationSettingsButton_Click;
        CalibrationStartupSwitch.Click += CalibrationStartupSwitch_Click;
        CalibrationCustomSwitch.Click += CalibrationCustomSwitch_Click;
        CalibrationIccCombo.SelectionChanged += CalibrationIccCombo_SelectionChanged;
        ScreenPageEventControl1.Click += CalibrationImportIcc_Click;
        ScreenEffectToggle.Click += ScreenEffectToggle_Click;
        ScreenEffectEditButton.Click += ScreenEffectEditButton_Click;
        ScreenPageEventControl2.Click += ScreenEffectReset_Click;
    }

    private void WireLightingEvents()
    {
        LightingOffBatteryToggle.Click += LightingOffBatteryToggle_Click;
    }

    private Window? _customModeInlineWindow;
    private Window? _cpuFanInlineWindow;
    private Window? _gpuFanInlineWindow;

    private void EmbedCustomEditors()
    {
        try
        {
            if (CustomModeHost.Content == null)
            {
                _customModeInlineWindow = new CustomModeWindow();
                if (_customModeInlineWindow is CustomModeWindow customEditor)
                {
                    customEditor.PrepareWideInline();
                    if (_owner != null)
                    {
                        customEditor.BindMode(_owner.WideCustomModeId);
                    }
                }
                MountWindowContent(_customModeInlineWindow, CustomModeHost);
            }
            string fanModeId = _owner?.WideCustomModeId ?? "balanced";
            if (CpuFanCurveHost.Content == null)
            {
                _cpuFanInlineWindow = new FanCurveWindow(fanModeId, showCpu: true, showGpu: false);
                ((FanCurveWindow)_cpuFanInlineWindow).PrepareInline();
                MountWindowContent(_cpuFanInlineWindow, CpuFanCurveHost);
            }
            if (GpuFanCurveHost.Content == null)
            {
                _gpuFanInlineWindow = new FanCurveWindow(fanModeId, showCpu: false, showGpu: true);
                ((FanCurveWindow)_gpuFanInlineWindow).PrepareInline();
                MountWindowContent(_gpuFanInlineWindow, GpuFanCurveHost);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.L("自定义编辑器加载失败：", "Failed to load custom editor: ") + ex.Message;
        }
    }

    private static void MountWindowContent(Window window, ContentControl host)
    {
        if (window.Content is UIElement content)
        {
            window.Content = null;
            host.Content = content;
        }
    }

    private static void UnwrapInnerScrollViewer(DependencyObject root)
    {
        ScrollViewer? scroll = FindVisualChild<ScrollViewer>(root);
        if (scroll?.Parent is not Grid grid || scroll.Content is not UIElement inner)
        {
            return;
        }
        grid.Children.Remove(scroll);
        if (inner is FrameworkElement element)
        {
            Grid.SetRow(element, 1);
        }
        grid.Children.Add(inner);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }
            T? nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private void WireCustomEvents()
    {
    }
    private static int WidePageCacheSeconds()
    {
        int seconds = MechrevoLite.AppConfig.Get("wide_page_cache_seconds", 300);
        string? env = Environment.GetEnvironmentVariable("LMECHREVO_PAGE_CACHE_SECONDS");
        if (int.TryParse(env, out int overrideSeconds))
        {
            return Math.Clamp(overrideSeconds, 2, 1800);
        }
        return Math.Clamp(seconds, 30, 1800);
    }

    private bool IsPageViewLoaded(string page) => page switch
    {
        "screen" => _screenView != null,
        "lighting" => _lightingView != null,
        "custom" => _customView != null,
        "switches" => _switchesView != null,
        _ => false
    };

    private void EvictIdlePageViews()
    {
        long now = Environment.TickCount64;
        long ttl = WidePageCacheSeconds() * 1000L;
        foreach (string page in new[] { "screen", "lighting", "custom", "switches" })
        {
            if (string.Equals(page, _activePage, StringComparison.Ordinal) || !IsPageViewLoaded(page))
            {
                continue;
            }
            if (now - _pageLastAccess.GetValueOrDefault(page, 0) < ttl)
            {
                continue;
            }
            Logger.WriteLine("[wide-mem] 页面超过 " + (ttl / 1000) + "s 未打开，释放视图：" + page);
            ReleasePageView(page);
        }
    }

    /// <summary>超过 TTL 未被打开的页面释放视觉树与字段；TTL 内保留以保住滚动/展开状态。首页常驻。</summary>
    private void ReleasePageView(string page)
    {
        switch (page)
        {
            case "screen":
                _effectCommitTimer?.Stop();
                _screenFeatures.Clear();
                _effectSliders.Clear();
                ScreenHost.Content = null;
                _screenView = null;
                ScreenPage = null!;
                ScreenCalibrationCard = null!;
                ScreenCalibrationToggle = null!;
                ScreenCalibrationOptionsPanel = null!;
                ScreenCalibrationSettingsButton = null!;
                ScreenCalibrationSettingsPanel = null!;
                CalibrationStartupSwitch = null!;
                CalibrationCustomSwitch = null!;
                CalibrationIccCombo = null!;
                ScreenPageEventControl1 = null!;
                ScreenFeaturesCard = null!;
                ScreenFeaturesPanel = null!;
                ScreenEffectCard = null!;
                ScreenEffectToggle = null!;
                ScreenEffectOptionsPanel = null!;
                ScreenEffectEditButton = null!;
                ScreenEffectEditorPanel = null!;
                ScreenPageEventControl2 = null!;
                ScreenEffectSliderPanel = null!;
                break;
            case "lighting":
                foreach (DispatcherTimer timer in _lightCardApplyTimers) timer.Stop();
                _lightCardApplyTimers.Clear();
                foreach (LightEditorWindow editor in _inlineLightEditors) editor.Close();
                _inlineLightEditors.Clear();
                if (_inlineLightEditorWindow != null) { _inlineLightEditorWindow.Close(); _inlineLightEditorWindow = null; }
                LightingHost.Content = null;
                _lightingView = null;
                LightingPage = null!;
                LightingOffBatteryToggle = null!;
                LightingIdlePanel = null!;
                LightingCardsGrid = null!;
                break;
            case "custom":
                if (_customModeInlineWindow != null) { _customModeInlineWindow.Close(); _customModeInlineWindow = null; }
                if (_cpuFanInlineWindow != null) { _cpuFanInlineWindow.Close(); _cpuFanInlineWindow = null; }
                if (_gpuFanInlineWindow != null) { _gpuFanInlineWindow.Close(); _gpuFanInlineWindow = null; }
                CustomHost.Content = null;
                _customView = null;
                CustomPage = null!;
                CustomModeHost = null!;
                CpuFanCurveHost = null!;
                GpuFanCurveHost = null!;
                break;
            case "switches":
                SwitchesHost.Content = null;
                _switchesView = null;
                SwitchesPage = null;
                MoreSwitchGrid = null;
                break;
        }
    }


}
