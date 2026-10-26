using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
                MountWindowContent(_customModeInlineWindow, CustomModeHost);
            }
            if (CpuFanCurveHost.Content == null)
            {
                _cpuFanInlineWindow = new FanCurveWindow("balanced", showCpu: true, showGpu: false);
                MountWindowContent(_cpuFanInlineWindow, CpuFanCurveHost);
            }
            if (GpuFanCurveHost.Content == null)
            {
                _gpuFanInlineWindow = new FanCurveWindow("balanced", showCpu: false, showGpu: true);
                MountWindowContent(_gpuFanInlineWindow, GpuFanCurveHost);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "自定义编辑器加载失败：" + ex.Message;
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

    private void WireCustomEvents()
    {
    }

}
