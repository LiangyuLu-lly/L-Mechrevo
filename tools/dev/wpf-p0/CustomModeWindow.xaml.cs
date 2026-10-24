using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using MechrevoLite;

namespace L_Mechrevo.WpfP0;

public partial class CustomModeWindow : Window
{
    private readonly List<MechrevoLite.Mode.PerfModeDefinition> _modes = new();
    private MechrevoLite.Mode.PerfModeDefinition? _current;
    private MechrevoLite.Mode.PerfModeSettings _settings = MechrevoLite.Mode.PerfModeSettings.Default;

    public CustomModeWindow(string? initialModeId = null)
    {
        InitializeComponent();
        MechrevoLite.UI.UiVisualStyle.SetAuditNightMode(true);
        LoadModes(initialModeId);
    }

    public void SetDark(bool dark)
    {
        MechrevoLite.UI.UiVisualStyle.SetNightMode(dark);
        MechrevoLite.UI.UiVisualStyle.SetAuditNightMode(dark);
    }

    private void LoadModes(string? selectedId)
    {
        _modes.Clear();
        _modes.AddRange(MechrevoLite.Mode.PerfModeStore.Load());
        ModeCombo.Items.Clear();
        foreach (MechrevoLite.Mode.PerfModeDefinition mode in _modes)
        {
            ModeCombo.Items.Add(new ComboBoxItem
            {
                Content = ModeName(mode),
                Tag = mode.Id
            });
        }
        string target = selectedId ?? MechrevoLite.Mode.PerfModeService.Instance?.DisplayedModeId
            ?? MechrevoLite.Mode.PerfModeStore.ActiveModeId;
        int index = _modes.FindIndex(m => string.Equals(m.Id, target, StringComparison.Ordinal));
        if (index < 0)
        {
            index = _modes.FindIndex(m => m.IsCustom);
        }
        if (index < 0)
        {
            index = 0;
        }
        if (index >= 0 && ModeCombo.Items.Count > index)
        {
            ModeCombo.SelectedIndex = index;
        }
    }

    private static string ModeName(MechrevoLite.Mode.PerfModeDefinition mode)
    {
        if (!string.IsNullOrWhiteSpace(mode.UserName))
        {
            return mode.UserName!;
        }
        return mode.Kind switch
        {
            MechrevoLite.Mode.PerfModeKind.Silent => "静音",
            MechrevoLite.Mode.PerfModeKind.Balanced => "平衡",
            MechrevoLite.Mode.PerfModeKind.Turbo => "狂暴",
            MechrevoLite.Mode.PerfModeKind.SilentTurbo => "静音狂暴",
            _ => "自定义"
        };
    }

    public void BindMode(string id)
    {
        int index = _modes.FindIndex(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (index >= 0 && index < ModeCombo.Items.Count)
        {
            ModeCombo.SelectedIndex = index;
        }
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

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string id)
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
        StatusText.Text = "当前模式：" + ModeName(mode);
    }

    private void LoadSettingsToUi(MechrevoLite.Mode.PerfModeSettings s)
    {
        SetSlider(Pl1Slider, Pl1Text, s.Pl1 ?? 65);
        SetSlider(Pl2Slider, Pl2Text, s.Pl2 ?? 65);
        SetSlider(Pl4Slider, Pl4Text, s.Pl4 ?? 100);
        TccSwitch.IsChecked = s.TccOn == true;
        SetSlider(TccSlider, TccText, s.TccTarget ?? 95);
        DbSwitch.IsChecked = s.GpuDynamicBoostOn == true;
        SetSlider(DbSlider, DbText, s.GpuDynamicBoost ?? 25);
        FanSensitivitySwitch.IsChecked = s.FanSwitchSpeedOn == true;
        SetSlider(FanSensitivitySlider, FanSensitivityText, s.FanSwitchSpeedMs ?? 0);
        SetSlider(FanDelaySlider, FanDelayText, s.FanSwitchSpeedMs ?? 300);
        GpuOcSwitch.IsChecked = s.GpuOverclockOn == true;
        SetSlider(CoreOffsetSlider, CoreOffsetText, s.GpuCoreOffset ?? 0);
        SetSlider(MemoryOffsetSlider, MemoryOffsetText, s.GpuMemoryOffset ?? 0);
        SelectComboInt(PowerModeCombo, s.WindowsPowerMode);
        SelectComboInt(CpuBoostCombo, s.CpuBoost);
        SelectComboBool(FanBoostCombo, s.FanBoost);
        RefreshOptions(s.RefreshHz);
    }

    private void RefreshOptions(int? current)
    {
        RefreshCombo.Items.Clear();
        RefreshCombo.Items.Add(new ComboBoxItem { Content = "不改变", Tag = "" });
        try
        {
            foreach (int hz in (Program.hw?.HzList ?? Array.Empty<int>()).Distinct().OrderByDescending(v => v))
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
    private void FanSensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (FanSensitivityText != null) FanSensitivityText.Text = ((int)e.NewValue).ToString(); }
    private void FanDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (FanDelayText != null) FanDelayText.Text = ((int)e.NewValue).ToString(); }
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
            StatusText.Text = "PerfModeService 未初始化";
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
                TccOn = TccSwitch.IsChecked == true ? true : (bool?)null,
                TccTarget = TccSwitch.IsChecked == true ? (int)TccSlider.Value : (int?)null,
                GpuDynamicBoostOn = DbSwitch.IsChecked == true ? true : (bool?)null,
                GpuDynamicBoost = DbSwitch.IsChecked == true ? (int)DbSlider.Value : (int?)null,
                FanSwitchSpeedOn = FanSensitivitySwitch.IsChecked == true ? true : (bool?)null,
                FanSwitchSpeedMs = FanSensitivitySwitch.IsChecked == true ? (int)FanDelaySlider.Value : (int?)null,
                GpuOverclockOn = GpuOcSwitch.IsChecked == true ? true : (bool?)null,
                GpuCoreOffset = GpuOcSwitch.IsChecked == true ? (int)CoreOffsetSlider.Value : (int?)null,
                GpuMemoryOffset = GpuOcSwitch.IsChecked == true ? (int)MemoryOffsetSlider.Value : (int?)null,
                WindowsPowerMode = ReadInt(PowerModeCombo),
                CpuBoost = ReadInt(CpuBoostCombo),
                FanBoost = FanBoostCombo.SelectedItem is ComboBoxItem fb && fb.Tag is string fbText && int.TryParse(fbText, out int fbValue)
                    ? fbValue == 1 : (bool?)null,
                RefreshHz = ReadInt(RefreshCombo)
            };
            await perf.UpdateAsync(_current.Id, _ => edited, "wpf-editor");
            _settings = edited;
            StatusText.Text = "已应用：" + ModeName(_current);
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
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
        if (MessageBox.Show(this, "恢复该模式的出厂默认参数？", "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        MechrevoLite.Mode.PerfModeStore.ResetSettings(_current.Id);
        _settings = MechrevoLite.Mode.PerfModeStore.LoadSettings(_current.Id);
        LoadSettingsToUi(_settings);
        StatusText.Text = "已恢复默认：" + ModeName(_current);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        MechrevoLite.Mode.PerfModeDefinition? mode = MechrevoLite.Mode.PerfModeStore.AddCustom(_current?.Id);
        if (mode == null)
        {
            StatusText.Text = "新建自定义模式失败";
            return;
        }
        LoadModes(mode.Id);
        StatusText.Text = "已新建：" + ModeName(mode);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        {
            return;
        }
        TextPromptWindow prompt = new("重命名模式", "输入新的模式名称：", ModeName(_current)) { Owner = this };
        if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.Value))
        {
            MechrevoLite.Mode.PerfModeStore.SaveName(_current.Id, prompt.Value.Trim());
            LoadModes(_current.Id);
            StatusText.Text = "已重命名";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || !_current.IsCustom)
        {
            StatusText.Text = "内置模式不能删除";
            return;
        }
        if (MessageBox.Show(this, "删除模式「" + ModeName(_current) + "」？", "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (MechrevoLite.Mode.PerfModeStore.RemoveCustom(_current.Id))
        {
            LoadModes(null);
            StatusText.Text = "已删除";
        }
        else
        {
            StatusText.Text = "删除失败";
        }
    }

    private void FanCurve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new MechrevoLite.FanCurveForm().Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开风扇曲线失败：" + ex.Message;
        }
    }

    private void CpuTuning_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new MechrevoLite.CpuTuningForm().Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开 CPU 调节失败：" + ex.Message;
        }
    }
}
