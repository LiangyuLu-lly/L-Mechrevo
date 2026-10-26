using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using MechrevoLite;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Services;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class CustomModeWindow : Window
{
    private readonly List<MechrevoLite.Mode.PerfModeDefinition> _modes = new();
    private bool _suppressPlanApply;
    private bool _firmwareMode;
    private int _modeComboSyncDepth;
    private bool _loadingUi;
    private bool _dirty;
    private readonly DispatcherTimer _powerWallTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private static string UnsetMark => WpfStrings.CustomUnsetMark;
    private static string PowerWallUnsetHint => WpfStrings.CustomPowerWallUnsetHint;
    private int _firmwareVisualMode;
    private int _firmwareProfileIndex;
    private MechrevoLite.Mode.PerfModeDefinition? _current;
    private MechrevoLite.Mode.PerfModeSettings _settings = MechrevoLite.Mode.PerfModeSettings.Default;

    public CustomModeWindow(string? initialModeId = null)
    {
        InitializeComponent();
        AddHandler(Slider.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>((_, _) => MarkDirty()), true);
        AddHandler(CheckBox.CheckedEvent,
            new RoutedEventHandler((_, _) => MarkDirty()), true);
        AddHandler(CheckBox.UncheckedEvent,
            new RoutedEventHandler((_, _) => MarkDirty()), true);
        AddHandler(ComboBox.SelectionChangedEvent,
            new SelectionChangedEventHandler((_, _) => MarkDirty()), true);
        _powerWallTimer.Tick += (_, _) => UpdatePowerWallStatus();
        Loaded += (_, _) => _powerWallTimer.Start();
        Closed += (_, _) => _powerWallTimer.Stop();
        LoadModes(initialModeId);
    }

    internal void PrepareInline()
    {
        if (ContentScroll.Parent is not Grid grid || ContentScroll.Content is not UIElement inner)
        {
            return;
        }
        grid.Children.Remove(ContentScroll);
        if (inner is FrameworkElement element)
        {
            Grid.SetRow(element, 1);
        }
        grid.Children.Add(inner);
    }

    public void SetDark(bool dark)
    {
    }

    private void LoadModes(string? selectedId)
    {
        _modeComboSyncDepth++;
        try
        {
        _firmwareMode = false;
        ModeCombo.IsEnabled = true;
        NewButton.IsEnabled = true;
        RenameButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
        ApplyButton.IsEnabled = true;
        _modes.Clear();
        _modes.AddRange(MechrevoLite.Mode.PerfModeStore.Load());
        string target = selectedId ?? MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId
            ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId;
        MechrevoLite.Mode.PerfModeDefinition? targetMode = _modes.FirstOrDefault(m => string.Equals(m.Id, target, StringComparison.Ordinal));
        if (targetMode == null)
        {
            targetMode = _modes.FirstOrDefault(m => m.IsCustom) ?? _modes.FirstOrDefault();
        }
        if (targetMode == null)
        {
            return;
        }
        target = targetMode.Id;
        bool customOnly = targetMode.IsCustom;
        ModeCombo.Items.Clear();
        foreach (MechrevoLite.Mode.PerfModeDefinition mode in _modes)
        {
            if (customOnly)
            {
                if (!mode.IsCustom)
                {
                    continue;
                }
            }
            else if (mode.IsCustom)
            {
                continue;
            }
            else if (!string.Equals(mode.Id, target, StringComparison.Ordinal)
                && !PerfModeCapabilities.IsOffered(mode, L_Mechrevo.WpfP0.Services.WpfBackend.Hw))
            {
                continue;
            }
            ModeCombo.Items.Add(new ComboBoxItem
            {
                Content = ModeName(mode),
                Tag = mode.Id
            });
        }
        for (int i = 0; i < ModeCombo.Items.Count; i++)
        {
            if (ModeCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, target, StringComparison.Ordinal))
            {
                ModeCombo.SelectedIndex = i;
                ApplyMode(target);
                return;
            }
        }
    
        }
        finally
        {
            _modeComboSyncDepth--;
        }
}

    private void MarkDirty()
    {
        if (_loadingUi || _firmwareMode || _current == null)
        {
            return;
        }
        if (_dirty)
        {
            return;
        }
        _dirty = true;
        ApplyButton.Content = WpfStrings.CustomModeWindow_xaml_Content;
        StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text;
    }

    private void UpdatePowerWallStatus()
    {
        try
        {
            L_Mechrevo.WpfP0.Services.PowerStatusService.SampleLocalPower();
            string text = L_Mechrevo.WpfP0.Services.PowerStatusService.ReadPowerWallText();
            PowerWallStatusText.Text = text;
            PowerWallStatusText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch
        {
            PowerWallStatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateRouteHint(MechrevoLite.Mode.PerfModeDefinition mode)
    {
        try
        {
            MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
            if (hw?.UsesBaolongda == true || hw?.WujieSmuApu == true)
            {
                RouteHintText.Text = MechrevoLite.Properties.Strings.PerfModeHintLocalApply;
                return;
            }
            int? slot = MechrevoLite.Mode.PerfModeService.Instance?.SlotOf(mode.Id);
            if (!mode.IsCustom)
            {
                RouteHintText.Text = mode.IsEmulatedBuiltIn
                    ? string.Format(MechrevoLite.Properties.Strings.PerfModeHintEmulated, slot.HasValue ? (slot.Value + 1).ToString() : "?")
                    : MechrevoLite.Properties.Strings.PerfModeHintVendor;
            }
            else
            {
                RouteHintText.Text = slot.HasValue
                    ? string.Format(MechrevoLite.Properties.Strings.PerfModeHintCustom, slot.Value + 1)
                    : MechrevoLite.Properties.Strings.PerfModeHintCustomUnassigned;
            }
        }
        catch
        {
            RouteHintText.Text = "";
        }
    }

    private static string ModeName(MechrevoLite.Mode.PerfModeDefinition mode)
    {
        return MechrevoLite.Mode.PerfModeText.Name(mode);
    }

    public void BindMode(string id)
    {
        LoadModes(id);
    }

    public async Task<bool> SelectModeAsync(string id)
    {
        BindMode(id);
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        if (perf == null)
        {
            return false;
        }
        try
        {
            MechrevoLite.Mode.PerfApplyOutcome? outcome = await perf.ActivateAsync(id, "wpf-editor", forceParameters: true);
            return outcome?.ModeSwitched == true;
        }
        catch
        {
            return false;
        }
    }

    private async void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_firmwareMode || _modeComboSyncDepth > 0)
        {
            return;
        }
        if (ModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string id)
        {
            return;
        }
        if (_current != null && string.Equals(_current.Id, id, StringComparison.Ordinal))
        {
            return;
        }
        _modeComboSyncDepth++;
        try
        {
            await SelectModeAsync(id);
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_2 + ex.Message;
        }
        finally
        {
            _modeComboSyncDepth--;
        }
    }

    public void BindFirmwareProfile(int visualMode, int profileIndex)
    {
        try
        {
            _firmwareMode = true;
            _firmwareVisualMode = visualMode;
            _firmwareProfileIndex = Math.Clamp(profileIndex, 0, MechrevoLite.Mode.VendorModeDefaults.MaxProfileCount - 1);
            MechrevoLite.Mode.PerfModeKind kind = visualMode switch
            {
                2 => MechrevoLite.Mode.PerfModeKind.Silent,
                1 => MechrevoLite.Mode.PerfModeKind.Turbo,
                _ => MechrevoLite.Mode.PerfModeKind.Balanced
            };
            string storeId = "fw" + visualMode + "p" + _firmwareProfileIndex;
            MechrevoLite.Mode.PerfModeSettings stored = MechrevoLite.Mode.PerfModeStore.LoadSettings(storeId);
            if (stored.IsUntouched)
            {
                stored = MechrevoLite.Mode.VendorModeDefaults.ComposeProfile(kind, L_Mechrevo.WpfP0.Services.WpfBackend.Hw, _firmwareProfileIndex);
            }
            MechrevoLite.Mode.PerfModeDefinition mode = new(
                MechrevoLite.Mode.PerfModeDefinition.BuiltInId(kind), kind, null, stored);
            _modes.Clear();
            _modes.Add(mode);
            _current = mode;
            _settings = stored;
            ModeCombo.Items.Clear();
            ModeCombo.Items.Add(new ComboBoxItem
            {
                Content = ModeName(mode) + string.Format(WpfStrings.CustomConfigSuffixFormat, _firmwareProfileIndex + 1),
                Tag = mode.Id
            });
            ModeCombo.SelectedIndex = 0;
            ModeCombo.IsEnabled = false;
            NewButton.IsEnabled = false;
            RenameButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            LoadSettingsToUi(stored);
            Title = WpfStrings.CustomModeWindow_xaml_Title + ModeName(mode) + string.Format(WpfStrings.CustomConfigSuffixFormat, _firmwareProfileIndex + 1);
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_3;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_4 + ex.Message;
        }
    }

    private void ApplyMode(string id)
    {
        if (_firmwareMode)
        {
            return;
        }
        MechrevoLite.Mode.PerfModeDefinition? mode = _modes.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (mode == null)
        {
            return;
        }
        _current = mode;
        _settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(id);
        LoadSettingsToUi(_settings);
        Title = WpfStrings.CustomModeWindow_xaml_Title + ModeName(mode);
        StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_5 + ModeName(mode);
        UpdateRouteHint(mode);
    }

    private void LoadSettingsToUi(MechrevoLite.Mode.PerfModeSettings s)
    {
        _loadingUi = true;
        try
        {
        SetSlider(Pl1Slider, Pl1Text, s.Pl1 ?? 65);
        SetSlider(Pl2Slider, Pl2Text, s.Pl2 ?? 65);
        SetSlider(Pl4Slider, Pl4Text, s.Pl4 ?? 100);
        TccSwitch.IsChecked = s.TccOn == true;
        SetSlider(TccSlider, TccText, s.TccTarget ?? 95);
        DbSwitch.IsChecked = s.GpuDynamicBoostOn == true;
        SetSlider(DbSlider, DbText, s.GpuDynamicBoost ?? 25);
        SetSlider(GpuTgpSlider, GpuTgpText, s.GpuTgp ?? 100);
        FanSensitivitySwitch.IsChecked = s.FanSwitchSpeedOn == true;
        DbSlider.IsEnabled = DbSwitch.IsChecked == true;
        TccSlider.IsEnabled = TccSwitch.IsChecked == true;
        FanDelaySlider.IsEnabled = FanSensitivitySwitch.IsChecked == true;
        CoreOffsetSlider.IsEnabled = GpuOcSwitch.IsChecked == true;
        MemoryOffsetSlider.IsEnabled = GpuOcSwitch.IsChecked == true;
        SetSlider(FanDelaySlider, FanDelayText, s.FanSwitchSpeedMs ?? 300);
        GpuOcSwitch.IsChecked = s.GpuOverclockOn == true;
        SetSlider(CoreOffsetSlider, CoreOffsetText, s.GpuCoreOffset ?? 0);
        SetSlider(MemoryOffsetSlider, MemoryOffsetText, s.GpuMemoryOffset ?? 0);
        SelectComboInt(PowerModeCombo, s.WindowsPowerMode);
        SelectComboInt(CpuBoostCombo, s.CpuBoost);
        SelectComboBool(FanBoostCombo, s.FanBoost);
        RefreshOptions(s.RefreshHz);
        LoadPowerPlans(s.PowerPlanGuid);
        SetSlider(StapmTimeSlider, StapmTimeText, s.StapmTimeSeconds ?? 28);
        SetSlider(SmuSlowLimitSlider, SmuSlowLimitText, s.SmuSlowLimit ?? 65);
        SetSlider(SmuSlowTimeSlider, SmuSlowTimeText, s.SmuSlowTimeSeconds ?? 28);
        SetSlider(SmuFastTimeSlider, SmuFastTimeText, s.SmuFastTimeSeconds ?? 5);
        if (!_firmwareMode && _current?.IsCustom != true && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is MechrevoHw liveHw)
        {
            LoadLiveHardware(liveHw);
        }
        RefreshGpuPowerLimit(s.GpuPowerLimitW);
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw is MechrevoHw limitsHw)
        {
            ApplyHardwareControlLimits(limitsHw);
            bool smuPower = MechrevoLite.Mode.PerfModeCapabilities.UsesSmuForCustomPower(
                limitsHw.UsesBaolongda,
                limitsHw.IsBaolongda30Series,
                MechrevoLite.Hardware.SmuAvailability.IsReady,
                limitsHw.WujieSmuApu);
            StapmTimeRow.Visibility = smuPower ? Visibility.Visible : Visibility.Collapsed;
            SmuSlowLimitRow.Visibility = smuPower ? Visibility.Visible : Visibility.Collapsed;
            SmuSlowTimeRow.Visibility = smuPower ? Visibility.Visible : Visibility.Collapsed;
            SmuFastTimeRow.Visibility = smuPower ? Visibility.Visible : Visibility.Collapsed;
        }
        bool locked = L_Mechrevo.WpfP0.Services.WpfBackend.Hw is MechrevoHw lockHw
            && lockHw.IsGcuConnected
            && PerfModeCapabilities.IsGen40Plus(lockHw)
            && _current?.IsCustom != true;
        CpuPowerCard.IsEnabled = !locked;
        TempGpuCard.IsEnabled = !locked;
        FanCard.IsEnabled = !locked;
        CpuPowerCard.Opacity = locked ? 0.45 : 1.0;
        TempGpuCard.Opacity = locked ? 0.45 : 1.0;
        FanCard.Opacity = locked ? 0.45 : 1.0;
        MechrevoHw? hwForRows = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        bool apuOrNoDgpu = hwForRows != null
            && (hwForRows.WujieSmuApu || hwForRows.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.NoDgpu);
        bool wujieApu = hwForRows?.WujieSmuApu == true;
        bool smuPowerRows = hwForRows != null && MechrevoLite.Mode.PerfModeCapabilities.UsesSmuForCustomPower(
            hwForRows.UsesBaolongda,
            hwForRows.IsBaolongda30Series,
            MechrevoLite.Hardware.SmuAvailability.IsReady,
            wujieApu);
        bool pl4Supported = hwForRows != null && hwForRows.Pl4Minimum >= 0 && hwForRows.Pl4Maximum > hwForRows.Pl4Minimum;
        Pl4Row.Visibility = !apuOrNoDgpu && pl4Supported ? Visibility.Visible : Visibility.Collapsed;
        FanBoostRow.Visibility = apuOrNoDgpu ? Visibility.Collapsed : Visibility.Visible;
        FanCard.Visibility = apuOrNoDgpu ? Visibility.Collapsed : Visibility.Visible;
        StapmTimeRow.Visibility = smuPowerRows && !wujieApu ? Visibility.Visible : Visibility.Collapsed;
        SmuSlowLimitRow.Visibility = smuPowerRows && !wujieApu ? Visibility.Visible : Visibility.Collapsed;
        SmuSlowTimeRow.Visibility = smuPowerRows && !wujieApu ? Visibility.Visible : Visibility.Collapsed;
        SmuFastTimeRow.Visibility = Visibility.Collapsed;
    
        }
        finally
        {
            _loadingUi = false;
        }
}

    private void LoadLiveHardware(MechrevoHw hw)
    {
        if (hw.Pl1 > 0) SetSlider(Pl1Slider, Pl1Text, hw.Pl1);
        if (hw.Pl2 > 0) SetSlider(Pl2Slider, Pl2Text, hw.Pl2);
        if (hw.Pl4 > 0) SetSlider(Pl4Slider, Pl4Text, hw.Pl4);
        if (hw.TccTarget > 0) SetSlider(TccSlider, TccText, hw.TccTarget);
        TccSwitch.IsChecked = hw.TccSwitch;
        if (hw.GpuTgp > 0) SetSlider(GpuTgpSlider, GpuTgpText, hw.GpuTgp);
        if (hw.UsesBaolongda && NvmlHelper.TryGetPowerLimitBounds(out _, out _, out int nvmlCurrent) && nvmlCurrent > 0)
        {
            SetSlider(GpuPowerLimitSlider, GpuPowerLimitText, nvmlCurrent);
        }
        DbSwitch.IsChecked = hw.GpuDbSwitch;
        if (hw.GpuDb > 0) SetSlider(DbSlider, DbText, hw.GpuDb);
        FanSensitivitySwitch.IsChecked = hw.FanSwitchSpeedEnabled;
        if (hw.FanSwitchSpeed > 0) SetSlider(FanDelaySlider, FanDelayText, hw.FanSwitchSpeed);
        SelectComboBool(FanBoostCombo, hw.FanBoost);
        GpuOcSwitch.IsChecked = hw.GpuOverclockEnabled;
        SetSlider(CoreOffsetSlider, CoreOffsetText, hw.EffectiveGpuCoreClockOffset);
        SetSlider(MemoryOffsetSlider, MemoryOffsetText, hw.EffectiveGpuMemoryClockOffset);
        DbSlider.IsEnabled = DbSwitch.IsChecked == true;
        TccSlider.IsEnabled = TccSwitch.IsChecked == true;
        FanDelaySlider.IsEnabled = FanSensitivitySwitch.IsChecked == true;
        CoreOffsetSlider.IsEnabled = GpuOcSwitch.IsChecked == true;
        MemoryOffsetSlider.IsEnabled = GpuOcSwitch.IsChecked == true;
    }

    private void LoadPowerPlans(string? selectedGuid)
    {
        _suppressPlanApply = true;
        try
        {
            PowerPlanCombo.Items.Clear();
            PowerPlanCombo.Items.Add(new ComboBoxItem { Content = WpfStrings.CustomModeWindow_Content_4, Tag = "" });
            foreach ((string guid, string name) in MechrevoLite.Hardware.WinPowerPlan.GetPlans())
            {
                PowerPlanCombo.Items.Add(new ComboBoxItem { Content = name, Tag = guid });
            }
            int index = 0;
            if (!string.IsNullOrWhiteSpace(selectedGuid))
            {
                for (int i = 1; i < PowerPlanCombo.Items.Count; i++)
                {
                    if (PowerPlanCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, selectedGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            PowerPlanCombo.SelectedIndex = index;
        }
        finally
        {
            _suppressPlanApply = false;
        }
    }

    private async void PowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlanApply || _current == null)
        {
            return;
        }
        if (PowerPlanCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        if (item.Tag is not string guid)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(guid))
        {
            return;
        }
        try
        {
            bool ok = MechrevoLite.Hardware.WinPowerPlan.SetActivePlan(guid);
            if (ok)
            {
                _settings = _settings with { PowerPlanGuid = guid };
                MechrevoLite.Mode.PerfModeStore.SaveSettings(_current.Id, _settings);
                StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_6;
            }
            else
            {
                StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_7;
                LoadPowerPlans(_settings.PowerPlanGuid);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_8 + ex.Message;
        }
        await Task.CompletedTask;
    }

    private void ApplyPowerWallVisuals(MechrevoHw? hw)
    {
        if (hw == null || (!hw.UsesBaolongda && !hw.WujieSmuApu))
        {
            Pl1Label.Text = WpfStrings.CustomModeWindow_xaml_Text_9;
            Pl2Label.Text = WpfStrings.CustomModeWindow_xaml_Text_10;
            TccLabel.Text = WpfStrings.CustomModeWindow_xaml_Text_11;
            return;
        }
        bool pl1Unset = hw.BaolongdaPl1Unset;
        bool pl2Unset = hw.BaolongdaPl2Unset;
        bool tccUnset = hw.BaolongdaTempWallUnset;
        bool smu = MechrevoLite.Mode.PerfModeCapabilities.UsesSmuForCustomPower(
            hw.UsesBaolongda,
            hw.IsBaolongda30Series,
            MechrevoLite.Hardware.SmuAvailability.IsReady,
            hw.WujieSmuApu);
        string pl1Base = smu ? MechrevoLite.Properties.Strings.SmuStapmLimitLabel : WpfStrings.CustomPowerPl1Label;
        string pl2Base = smu ? MechrevoLite.Properties.Strings.SmuFastLimitLabel : WpfStrings.CustomPowerPl2Label;
        Pl1Label.Text = pl1Base + (pl1Unset ? UnsetMark : "");
        Pl2Label.Text = pl2Base + (pl2Unset ? UnsetMark : "");
        TccLabel.Text = WpfStrings.CustomModeWindow_xaml_Text_11 + (tccUnset ? UnsetMark : "");
        Pl1Label.ToolTip = pl1Unset ? PowerWallUnsetHint : null;
        Pl2Label.ToolTip = pl2Unset ? PowerWallUnsetHint : null;
        TccLabel.ToolTip = tccUnset ? PowerWallUnsetHint : null;
        if (_current?.IsCustom == true && _settings.IsUntouched)
        {
            Pl1Slider.Value = Pl1Slider.Minimum;
            Pl2Slider.Value = Pl2Slider.Minimum;
            TccSwitch.IsChecked = false;
            TccSlider.Value = TccSlider.Minimum;
            GpuOcSwitch.IsChecked = false;
            CoreOffsetSlider.Value = 0;
            MemoryOffsetSlider.Value = 0;
        }
    }

    private void RefreshGpuPowerLimit(int? savedWatts)
    {
        int minW = 0;
        int maxW = 0;
        int currentW = 0;
        bool visible = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true
            && NvmlHelper.TryGetPowerLimitBounds(out minW, out maxW, out currentW)
            && maxW > minW;
        GpuPowerLimitRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }
        GpuPowerLimitSlider.Minimum = minW;
        GpuPowerLimitSlider.Maximum = maxW;
        int value = savedWatts is >= 0 && savedWatts <= maxW && savedWatts >= minW ? savedWatts.Value : currentW;
        SetSlider(GpuPowerLimitSlider, GpuPowerLimitText, value);
    }

    private static void SetSliderRange(System.Windows.Controls.Slider slider, int min, int max)
    {
        if (min >= 0 && max > min)
        {
            slider.Minimum = min;
            slider.Maximum = max;
        }
    }

    private void ApplyHardwareControlLimits(MechrevoHw hw)
    {
        if (hw.WujieSmuApu)
        {
            Pl1Slider.Minimum = MechrevoLite.Hardware.WujieSmuGate.PlMinWatts;
            Pl1Slider.Maximum = MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts;
            Pl2Slider.Minimum = MechrevoLite.Hardware.WujieSmuGate.PlMinWatts;
            Pl2Slider.Maximum = MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts;
            TccSlider.Minimum = MechrevoLite.Hardware.WujieSmuGate.TccMinCelsius;
            TccSlider.Maximum = MechrevoLite.Hardware.WujieSmuGate.TccMaxCelsius;
        }
        else
        {
            SetSliderRange(Pl1Slider, hw.Pl1Minimum, hw.Pl1Maximum);
            SetSliderRange(Pl2Slider, hw.Pl2Minimum, hw.Pl2Maximum);
        }
        SetSliderRange(Pl4Slider, hw.Pl4Minimum, hw.Pl4Maximum);
        SetSliderRange(TccSlider, hw.TccMinimum, hw.TccMaximum);
        SetSliderRange(GpuTgpSlider, hw.GpuTgpMinimum, hw.GpuTgpMaximum);
        SetSliderRange(DbSlider, hw.GpuDbMinimum, hw.GpuDbMaximum);
        SetSliderRange(FanDelaySlider, hw.FanSwitchSpeedMinimum, hw.FanSwitchSpeedMaximum);
        TccSwitch.IsEnabled = hw.TccAdjustable;
        DbSwitch.IsEnabled = hw.GpuDynamicBoostAdjustable;
        GpuTgpSlider.IsEnabled = hw.GpuTgpAdjustable;
        FanSensitivitySwitch.IsEnabled = hw.SupportsFanSwitchSpeed;
        GpuOcSwitch.IsEnabled = hw.SupportsGpuOverclock;
        if (!hw.GpuTgpAdjustable)
        {
            GpuTgpSlider.Opacity = 0.45;
        }
        if (!hw.GpuDynamicBoostAdjustable)
        {
            DbSwitch.Opacity = 0.45;
            DbSlider.Opacity = 0.45;
        }
    }

    private void RefreshOptions(int? current)
    {
        RefreshCombo.Items.Clear();
        RefreshCombo.Items.Add(new ComboBoxItem { Content = WpfStrings.CustomModeWindow_Content_4, Tag = "" });
        try
        {
            foreach (int hz in (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.HzList ?? Array.Empty<int>()).Distinct().OrderByDescending(v => v))
            {
                RefreshCombo.Items.Add(new ComboBoxItem { Content = hz + "Hz", Tag = hz.ToString() });
            }
        }
        catch
        {
        }
        int selected = 0;
        for (int i = 0; i < RefreshCombo.Items.Count; i++)
        {
            if (RefreshCombo.Items[i] is ComboBoxItem item && item.Tag is string text
                && int.TryParse(text, out int hz) && hz == current)
            {
                selected = i;
                break;
            }
        }
        RefreshCombo.SelectedIndex = selected;
    }

    private static void SetSlider(Slider slider, TextBlock text, int value)
    {
        value = (int)Math.Clamp(value, slider.Minimum, slider.Maximum);
        slider.Value = value;
        text.Text = value.ToString();
    }

    private static void SelectComboInt(ComboBox combo, int? value)
    {
        combo.SelectedIndex = 0;
        if (value.HasValue)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag is string text
                    && int.TryParse(text, out int parsed) && parsed == value.Value)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }
    }

    private static void SelectComboBool(ComboBox combo, bool? value)
    {
        combo.SelectedIndex = 0;
        if (value.HasValue)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag is string text
                    && int.TryParse(text, out int parsed) && (parsed == 1) == value.Value)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }
    }

    private void Pl1_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Pl1Text != null) Pl1Text.Text = ((int)e.NewValue).ToString(); }
    private void Pl2_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Pl2Text != null) Pl2Text.Text = ((int)e.NewValue).ToString(); }
    private void Pl4_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Pl4Text != null) Pl4Text.Text = ((int)e.NewValue).ToString(); }
    private void Tcc_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (TccText != null) TccText.Text = ((int)e.NewValue).ToString(); }
    private void Db_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (DbText != null) DbText.Text = ((int)e.NewValue).ToString(); }
    private void GpuTgp_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (GpuTgpText != null) GpuTgpText.Text = ((int)e.NewValue).ToString(); }
    private void StapmTime_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (StapmTimeText != null) StapmTimeText.Text = ((int)e.NewValue).ToString(); }
    private void SmuSlowLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (SmuSlowLimitText != null) SmuSlowLimitText.Text = ((int)e.NewValue).ToString(); }
    private void SmuSlowTime_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (SmuSlowTimeText != null) SmuSlowTimeText.Text = ((int)e.NewValue).ToString(); }
    private void SmuFastTime_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (SmuFastTimeText != null) SmuFastTimeText.Text = ((int)e.NewValue).ToString(); }
    private void DbSwitch_Changed(object sender, RoutedEventArgs e) { if (DbSlider != null && DbSwitch != null) DbSlider.IsEnabled = DbSwitch.IsChecked == true; }
    private void GpuPowerLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (GpuPowerLimitText != null) GpuPowerLimitText.Text = ((int)e.NewValue).ToString(); }
    private void FanDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (FanDelayText != null) FanDelayText.Text = ((int)e.NewValue).ToString(); }
    private void FanSensitivitySwitch_Changed(object sender, RoutedEventArgs e) { if (FanDelaySlider != null && FanSensitivitySwitch != null) FanDelaySlider.IsEnabled = FanSensitivitySwitch.IsChecked == true; }
    private void CoreOffset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (CoreOffsetText != null) CoreOffsetText.Text = ((int)e.NewValue).ToString(); }
    private void MemoryOffset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (MemoryOffsetText != null) MemoryOffsetText.Text = ((int)e.NewValue).ToString(); }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        {
            return;
        }
        MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
        if (perf == null)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_12;
            return;
        }
        ApplyButton.IsEnabled = false;
        try
        {
            int? ReadInt(ComboBox combo)
            {
                return combo.SelectedItem is ComboBoxItem item && item.Tag is string text && int.TryParse(text, out int value)
                    ? value : (int?)null;
            }
            MechrevoLite.Mode.PerfModeSettings edited = _settings with
            {
                Pl1 = (int)Pl1Slider.Value,
                Pl2 = (int)Pl2Slider.Value,
                Pl4 = (int)Pl4Slider.Value,
                TccOn = TccSwitch.IsChecked == true,
                TccTarget = (int)TccSlider.Value,
                GpuDynamicBoostOn = DbSwitch.IsChecked == true,
                GpuDynamicBoost = (int)DbSlider.Value,
                GpuTgp = (int)GpuTgpSlider.Value,
                StapmTimeSeconds = StapmTimeRow.Visibility == Visibility.Visible ? (int)StapmTimeSlider.Value : (int?)null,
                SmuSlowLimit = SmuSlowLimitRow.Visibility == Visibility.Visible ? (int)SmuSlowLimitSlider.Value : (int?)null,
                SmuSlowTimeSeconds = SmuSlowTimeRow.Visibility == Visibility.Visible ? (int)SmuSlowTimeSlider.Value : (int?)null,
                SmuFastTimeSeconds = SmuFastTimeRow.Visibility == Visibility.Visible ? (int)SmuFastTimeSlider.Value : (int?)null,
                FanSwitchSpeedOn = FanSensitivitySwitch.IsChecked == true,
                FanSwitchSpeedMs = (int)FanDelaySlider.Value,
                GpuOverclockOn = GpuOcSwitch.IsChecked == true,
                GpuCoreOffset = (int)CoreOffsetSlider.Value,
                GpuMemoryOffset = (int)MemoryOffsetSlider.Value,
                WindowsPowerMode = ReadInt(PowerModeCombo),
                PowerPlanGuid = PowerPlanCombo.SelectedItem is ComboBoxItem plan && plan.Tag is string planTag && !string.IsNullOrWhiteSpace(planTag) ? planTag : null,
                GpuPowerLimitW = GpuPowerLimitRow.Visibility == Visibility.Visible ? (int)GpuPowerLimitSlider.Value : (int?)null,
                CpuBoost = ReadInt(CpuBoostCombo),
                FanBoost = FanBoostCombo.SelectedItem is ComboBoxItem fb && fb.Tag is string fbText && int.TryParse(fbText, out int fbValue)
                    ? fbValue == 1 : (bool?)null,
                RefreshHz = ReadInt(RefreshCombo)
            };
            MechrevoLite.Mode.PerfApplyOutcome? outcome = await perf.UpdateAsync(_current.Id, _ => edited, "wpf-editor");
            if (outcome == null)
            {
                StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_13;
                return;
            }
            _settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(_current.Id);
            bool amd = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesAmdPowerFields == true;
            string summary = MechrevoLite.Mode.PerfModeText.Summary(outcome);
            string failure = outcome.AnyFailed
                ? WpfStrings.CustomFailedSuffix + MechrevoLite.Mode.PerfModeText.FailedItems(outcome, amd)
                : "";
            string stale = MechrevoLite.Mode.PerfModeText.StaleNote(outcome);
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_14 + ModeName(_current) + " · " + summary + failure + stale;
            ToolTip = MechrevoLite.Mode.PerfModeText.Details(outcome, amd);
            if (!outcome.AnyFailed)
            {
                _dirty = false;
                ApplyButton.Content = WpfStrings.CpuTuningWindow_xaml_Content;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        {
            return;
        }
        if (MessageBox.Show(this, WpfStrings.CustomModeWindow_xaml_MessageBoxShowthis, "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        MechrevoLite.Mode.PerfModeStore.ResetSettings(_current.Id);
        _settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(_current.Id);
        LoadSettingsToUi(_settings);
        StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_15 + ModeName(_current);
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? seedFrom = (L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true) ? null : _current?.Id;
            MechrevoLite.Mode.PerfModeService? perf = MechrevoLite.Mode.PerfModeService.Instance;
            MechrevoLite.Mode.PerfModeDefinition? mode = perf?.AddCustom(seedFrom)
                ?? MechrevoLite.Mode.PerfModeStore.AddCustom(seedFrom);
            if (mode == null)
            {
                StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_16;
                return;
            }
            MechrevoHw? newModeHw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
            bool gcuMachine = newModeHw != null
                && newModeHw.IsGcuConnected
                && !newModeHw.UsesBaolongda
                && !newModeHw.WujieSmuApu;
            if (gcuMachine)
            {
                MechrevoLite.Mode.PerfModeStore.SaveSettings(mode.Id, new MechrevoLite.Mode.PerfModeSettings
                {
                    Pl1 = 65,
                    Pl2 = 65,
                    Pl4 = 100,
                    TccOn = false,
                    TccTarget = 95,
                    GpuTgp = 115,
                    GpuDynamicBoostOn = true,
                    GpuDynamicBoost = 25,
                    FanSwitchSpeedOn = false,
                    FanSwitchSpeedMs = 300,
                    GpuOverclockOn = false,
                    GpuCoreOffset = 0,
                    GpuMemoryOffset = 0,
                    CpuFanDuty = FanCurveDefaults.CpuDuties.ToArray(),
                    GpuFanDuty = FanCurveDefaults.GpuDuties.ToArray()
                });
            }
            LoadModes(mode.Id);
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_17 + ModeName(mode);
            if (perf != null)
            {
                await perf.ActivateAsync(mode.Id, "wpf-editor-new", forceParameters: true);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_18 + ex.Message;
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        {
            return;
        }
        TextPromptWindow prompt = new(WpfStrings.CustomRenameTitle, WpfStrings.CustomRenamePrompt, ModeName(_current)) { Owner = this };
        if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.Value))
        {
            MechrevoLite.Mode.PerfModeStore.SaveName(_current.Id, prompt.Value.Trim());
            LoadModes(_current.Id);
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_19;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || !_current.IsCustom)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_20;
            return;
        }
        if (MessageBox.Show(this, WpfStrings.CustomModeWindow_xaml_MessageBoxShowthis_2 + ModeName(_current) + WpfStrings.CustomDeleteConfirmSuffix, "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (MechrevoLite.Mode.PerfModeStore.RemoveCustom(_current.Id))
        {
            MechrevoLite.Mode.PerfModeDefinition? next = MechrevoLite.Mode.PerfModeStore.Load()
                .FirstOrDefault(m => m.IsCustom);
            if (next != null)
            {
                LoadModes(next.Id);
            }
            else
            {
                LoadModes(null);
            }
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_21;
        }
        else
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_22;
        }
    }

    private void FanCurve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
            if (hw == null)
            {
                StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_23;
                return;
            }
            string targetId = _firmwareMode
                ? "fw" + _firmwareVisualMode + "p" + _firmwareProfileIndex
                : (_current?.Id ?? "balanced");
            FanCurveWindow fanWindow = new(targetId);
            Window? fanOwner = ResolveDialogOwner();
            if (fanOwner != null)
            {
                fanWindow.Owner = fanOwner;
            }
            fanWindow.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_24 + ex.Message;
        }
    }

    private Window? ResolveDialogOwner()
    {
        if (IsVisible && PresentationSource.FromVisual(this) != null)
        {
            return this;
        }
        Window? main = Application.Current?.MainWindow;
        return main != null && main.IsVisible && !ReferenceEquals(main, this) ? main : null;
    }

    private void CpuTuning_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CpuTuningWindow tuningWindow = new();
            Window? tuningOwner = ResolveDialogOwner();
            if (tuningOwner != null)
            {
                tuningWindow.Owner = tuningOwner;
            }
            tuningWindow.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CustomModeWindow_xaml_Text_25 + ex.Message;
        }
    }
}
