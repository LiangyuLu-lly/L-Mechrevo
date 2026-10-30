using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;
using L_Mechrevo.WpfP0.Services;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class LightEditorWindow : Window
{
    private readonly string _topic;
    private readonly (string Effect, string Name)[] _effects;
    private LightChannelSettings _settings;
    private int _light;
    private int _speed;
    private int _manualInterval = 5;
    private int _breathingIndex = 1;
    private int[] _manualIndexes = (int[])LightChannelSettings.DefaultManualIndexes.Clone();
    private int _colorArgb;
    private int _monochromeIndex = 4;
    private bool _usePresetColor;
    private int[] _mixColors = (int[])LightChannelSettings.DefaultMixColors.Clone();
    private bool _gradeBrightness;
    private readonly bool _keyboard;
    private bool _gradeColors;
    private bool _wujieWhite;
    private bool _logoGrade;
    /// <summary>⭐ 2026-10-26（task-17 · 宽屏白色背光入口）：白色背光键盘只有 电源 + 2 档亮度，没有灯效/颜色/速度 ✅。</summary>
    private readonly bool _whiteBacklightOnly;

    /// <summary>NUC/UMI 原厂灯带没有速度寄存器，编辑器隐藏速度行；其它机型不受影响。</summary>
    private readonly bool _hideSpeedForNucLightbar;
    private int _lightMax = 4;
    private readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _suppressAutoApply;
    private bool _applying;
    private bool _inline;
    private bool _pendingAutoApply;

    public LightEditorWindow(string topic, string title, (string Effect, string Name)[] effects, bool whiteBacklightOnly = false)
    {
        InitializeComponent();
        _topic = topic;
        _effects = effects;
        _whiteBacklightOnly = whiteBacklightOnly && string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
        _keyboard = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
        _hideSpeedForNucLightbar = string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
            && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is { } lightbarHw
            && (lightbarHw.IsIntelNucMachine || lightbarHw.IsUmiMachine);
        Title = title;
        TitleText.Text = title;
        MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        bool gen40 = hw != null
            && !hw.UsesBaolongda
            && (hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30
                || hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40);
        _logoGrade = gen40 && string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal);
        bool lightbarGrade = gen40 && string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal);
        _gradeColors = _logoGrade || lightbarGrade;
        _gradeBrightness = _keyboard || _logoGrade;
        LightSlider.Minimum = 0;
        LightSlider.Maximum = _gradeBrightness ? 4 : 100;
        SpeedSlider.Minimum = 0;
        SpeedSlider.Maximum = 4;
        if (_keyboard)
        {
            bool bd = hw?.UsesBaolongda == true;
            if (bd)
            {
                try
                {
                    hw!.EnsureKeyboardLightingCalibration();
                }
                catch
                {
                }
            }
            _wujieWhite = hw?.WujieSmuApu == true
                && hw.KeyboardStatusSeen
                && string.Equals(hw.KeyboardSolution, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
            // task-17 白色背光实际只有 2 档（暗/亮），不是 4 档；
            // 旧注释/旧范围写成 0..4 会让 UI 出现第 3/4 档但下发无效。
            if (_whiteBacklightOnly)
            {
                _lightMax = 2;
                LightSlider.Minimum = 1;
                LightSlider.Maximum = 2;
            }
            else
            {
                _lightMax = _wujieWhite ? 2 : (bd ? Math.Max(1, MechrevoHw.EffectiveKeyboardBrightnessMax()) : 4);
                LightSlider.Minimum = bd || _wujieWhite ? 1 : 0;
                LightSlider.Maximum = _lightMax;
            }
        }
        _settings = LightingSettingsStore.Load(topic, effects.Length > 0 ? effects[0].Effect : "Single");
        _light = Math.Clamp(_settings.Light, (int)LightSlider.Minimum, (int)LightSlider.Maximum);
        if (_keyboard && _wujieWhite)
        {
            _light = Math.Clamp(MechrevoLite.AppConfig.Get("wujie_kbd_level", 2), 1, 2);
            if (MechrevoLite.Hardware.EcKeyboardLight.TryGet(out bool ecOn, out int ecLevel) && ecOn && ecLevel >= 1 && ecLevel <= 2)
            {
                _light = ecLevel;
            }
        }
        _speed = Math.Clamp(_settings.Speed, 0, 4);
        _monochromeIndex = Math.Clamp(_settings.MonochromeIndex, 1, 29);
        _usePresetColor = _settings.UsePresetColor;
        _colorArgb = _settings.EffectiveColorArgb();
        if (_settings.ManualIndexes is { Length: >= 6 } manual)
        {
            _manualIndexes = (int[])manual.Clone();
        }
        _manualInterval = Math.Clamp(_settings.Speed, 2, 10);
        _breathingIndex = Math.Clamp(_settings.BreathingIndex, 1, 4);
        LightChannelSettings mixSettings = LightingSettingsStore.Load(topic, "ColorMarquee");
        if (mixSettings.MixColors is { Length: 7 } mix)
        {
            _mixColors = (int[])mix.Clone();
        }
        PowerCheck.IsChecked = _settings.PowerOn;
        if (_keyboard)
        {
            HintText.Text = WpfStrings.LightEditorWindow_xaml_Text;
            StatusText.Text = WpfStrings.LightEditorWindow_xaml_Text_2;
        }
        _suppressAutoApply = true;
        NetSliderValues();
        LoadEffects();
        RefreshColorUi();
        BuildKeyboardExtra();
        if (_whiteBacklightOnly)
        {
            // ⭐⭐ task-17（宽屏白色背光入口）：白色背光键盘按能力只保留「电源 + 2 档亮度」，
            //    ⛔ 不显示不支持的 RGB 效果/颜色/速度/七色等控件 ✅
            HintText.Text = WpfStrings.LightWhiteKeyboardHint;
            EffectRow.Visibility = Visibility.Collapsed;
            ColorRow.Visibility = Visibility.Collapsed;
            MixRow.Visibility = Visibility.Collapsed;
            SpeedRow.Visibility = Visibility.Collapsed;
            KeyboardExtraRow.Visibility = Visibility.Collapsed;
        }
        if (_hideSpeedForNucLightbar)
        {
            // NUC/UMI 原厂只有单色/彩色，没有速度通道。
            SpeedRow.Visibility = Visibility.Collapsed;
        }
        _suppressAutoApply = false;
        PowerCheck.Checked += (_, _) =>
        {
            UpdateEditorPowerUi();
            ScheduleAutoApply();
        };
        PowerCheck.Unchecked += (_, _) =>
        {
            UpdateEditorPowerUi();
            ScheduleAutoApply();
        };
        UpdateEditorPowerUi();
        _applyTimer.Tick += async (_, _) =>
        {
            _applyTimer.Stop();
            await ApplyCoreAsync(fromAuto: true);
        };
        Closed += (_, _) => _applyTimer.Stop();
    }

    /// <summary>宽屏卡内联：隐藏标题/提示/电源行，保留效果和完整二级菜单；控件变化直接自动应用。</summary>
    internal void PrepareWideInline()
    {
        _inline = true;
        TitleText.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        PowerRow.Visibility = Visibility.Collapsed;
        if (Content is FrameworkElement root)
        {
            root.Margin = new Thickness(0);
        }
    }

    internal bool PowerOn => PowerCheck.IsChecked == true;

    internal void SetPower(bool on)
    {
        PowerCheck.IsChecked = on;
    }

    private void ScheduleAutoApply()
    {
        if (_suppressAutoApply || (!IsLoaded && !_inline))
        {
            return;
        }
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void UpdateEditorPowerUi()
    {
        bool on = PowerCheck.IsChecked == true;
        LightSlider.IsEnabled = on;
        SpeedSlider.IsEnabled = on;
        ColorRow.IsEnabled = on;
        MixRow.IsEnabled = on;
        KeyboardExtraRow.IsEnabled = on;
    }

    private void ResetMix_Click(object sender, RoutedEventArgs e)
    {
        _mixColors = (int[])LightChannelSettings.DefaultMixColors.Clone();
        try
        {
            LightChannelSettings mixSettings = LightingSettingsStore.Load(_topic, "ColorMarquee");
            LightingSettingsStore.Save(_topic, mixSettings with { MixColors = (int[])_mixColors.Clone() });
        }
        catch
        {
        }
        RefreshColorUi();
        ScheduleAutoApply();
    }

    private void NetSliderValues()
    {
        if (_keyboard)
        {
            LightSlider.Value = _light;
            LightText.Text = (_wujieWhite || _whiteBacklightOnly) ? (_light >= 2 ? WpfStrings.LightLevelBright : WpfStrings.LightLevelDim) : (_light + "/" + _lightMax);
            SpeedSlider.Value = _speed;
            SpeedText.Text = _speed + "/4";
            return;
        }
        LightSlider.Value = _gradeBrightness ? _light : _light * 25;
        LightText.Text = ((int)LightSlider.Value).ToString();
        SpeedSlider.Value = _speed;
        SpeedText.Text = _speed.ToString();
    }

    private void LoadEffects()
    {
        EffectCombo.Items.Clear();
        string selected = _settings.Effect;
        int selectedIndex = 0;
        for (int i = 0; i < _effects.Length; i++)
        {
            EffectCombo.Items.Add(new ComboBoxItem { Content = _effects[i].Name, Tag = _effects[i].Effect });
            if (string.Equals(_effects[i].Effect, selected, StringComparison.Ordinal))
            {
                selectedIndex = i;
            }
        }
        EffectCombo.SelectedIndex = selectedIndex;
    }

    private string SelectedEffect =>
        EffectCombo.SelectedItem is ComboBoxItem item && item.Tag is string effect
            ? effect
            : (_effects.Length > 0 ? _effects[0].Effect : "Single");

    private void RefreshColorUi()
    {
        if (_whiteBacklightOnly)
        {
            // ⭐ task-17：白色背光只有开/关 + 2 档亮度，没有任何 RGB 颜色/效果行 ✅
            ColorRow.Visibility = Visibility.Collapsed;
            MixRow.Visibility = Visibility.Collapsed;
            SpeedRow.Visibility = Visibility.Collapsed;
            KeyboardExtraRow.Visibility = Visibility.Collapsed;
            return;
        }
        string effect = SelectedEffect;
        if (_keyboard)
        {
            MixRow.Visibility = Visibility.Collapsed;
            bool wujie = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.WujieSmuApu == true;
            bool keyboardManual = string.Equals(effect, "Manual", StringComparison.Ordinal);
            bool keyboardBreathing = string.Equals(effect, "Breathing", StringComparison.Ordinal);
            bool keyboardRainbow = string.Equals(effect, "Rainbow", StringComparison.Ordinal);
            bool singleZone = L_Mechrevo.WpfP0.Services.WpfBackend.Hw != null
                && KeyboardFirmwareEffects.IsSingleZoneRgb(L_Mechrevo.WpfP0.Services.WpfBackend.Hw.Capabilities.KeyboardType);
            bool colorVisible = !wujie && !keyboardManual && !keyboardRainbow
                && (!keyboardBreathing || !singleZone)
                && KeyboardFirmwareEffects.IsSingleColorEffect(effect);
            ColorRow.Visibility = colorVisible ? Visibility.Visible : Visibility.Collapsed;
            UpdateColorButton();
            RefreshKeyboardUi();
            return;
        }
        if (_gradeColors)
        {
            int mode = LightingCatalog.LightEffectColorMode(effect);
            ColorRow.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            MixRow.Visibility = mode == 7 ? Visibility.Visible : Visibility.Collapsed;
            if (mode == 7)
            {
                MixGrid.Children.Clear();
                for (int i = 0; i < 7; i++)
                {
                    System.Drawing.Color c = System.Drawing.Color.FromArgb(_mixColors[i]);
                    Button swatch = new()
                    {
                        Height = 26,
                        Margin = new Thickness(2),
                        Padding = new Thickness(0),
                        Tag = i,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                        ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                    };
                    swatch.Click += Mix_Click;
                    MixGrid.Children.Add(swatch);
                }
            }
        }
        else
        {
            // 非 Gen40 普通灯带/Logo/铰链/同步：颜色按单色系效果判断，没有七色混色。
            ColorRow.Visibility = KeyboardFirmwareEffects.IsSingleColorEffect(effect)
                ? Visibility.Visible
                : Visibility.Collapsed;
            MixRow.Visibility = Visibility.Collapsed;
        }
        UpdateColorButton();
        RefreshKeyboardUi();
    }

    private void BuildKeyboardExtra()
    {
        if (!string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal))
        {
            return;
        }
        ManualPaletteGrid.Children.Clear();
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            System.Drawing.Color c = System.Drawing.Color.FromArgb(
                LightChannelSettings.ManualPalette[Math.Clamp(_manualIndexes[i], 1, 29) - 1]);
            Button button = new()
            {
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Tag = index,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            };
            button.Click += ManualColor_Click;
            ManualPaletteGrid.Children.Add(button);
        }
        BreathGrid.Children.Clear();
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            System.Drawing.Color c = System.Drawing.Color.FromArgb(LightChannelSettings.BreathingPalette[i]);
            Button button = new()
            {
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Tag = index + 1,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                ToolTip = WpfStrings.LightEditorWindow_xaml_ToolTip + (index + 1)
            };
            button.Click += BreathColor_Click;
            BreathGrid.Children.Add(button);
        }
    }

    private void RefreshKeyboardUi()
    {
        if (KeyboardExtraRow == null)
        {
            return;
        }
        bool keyboard = string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal);
        KeyboardExtraRow.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        if (!keyboard)
        {
            return;
        }
        string effect = SelectedEffect;
        bool manual = string.Equals(effect, "Manual", StringComparison.Ordinal);
        bool breathing = string.Equals(effect, "Breathing", StringComparison.Ordinal)
            && L_Mechrevo.WpfP0.Services.WpfBackend.Hw != null
            && MechrevoLite.Hardware.KeyboardFirmwareEffects.IsSingleZoneRgb(L_Mechrevo.WpfP0.Services.WpfBackend.Hw.Capabilities.KeyboardType);
        SpeedRow.Visibility = Visibility.Collapsed;
        PerKeyPanel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        ManualPalettePanel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        BreathPanel.Visibility = breathing ? Visibility.Visible : Visibility.Collapsed;
        if (manual && PerKeySlider.Value != _manualInterval)
        {
            PerKeySlider.Value = _manualInterval;
        }
        PerKeyText.Text = _manualInterval + "/10";
        for (int i = 0; i < ManualPaletteGrid.Children.Count; i++)
        {
            if (ManualPaletteGrid.Children[i] is Button button)
            {
                System.Drawing.Color c = System.Drawing.Color.FromArgb(
                    LightChannelSettings.ManualPalette[Math.Clamp(_manualIndexes[i], 1, 29) - 1]);
                button.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                button.ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }
        for (int i = 0; i < BreathGrid.Children.Count; i++)
        {
            if (BreathGrid.Children[i] is Button button)
            {
                button.BorderThickness = new Thickness(i + 1 == _breathingIndex ? 2 : 0);
            }
        }
    }

    private void PerKey_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _manualInterval = Math.Clamp((int)Math.Round(e.NewValue), 2, 10);
        if (PerKeyText != null)
        {
            PerKeyText.Text = _manualInterval + "/10";
        }
        ScheduleAutoApply();
    }

    private void BreathColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            _breathingIndex = Math.Clamp(index, 1, 4);
            RefreshKeyboardUi();
            ScheduleAutoApply();
        }
    }

    private void ManualColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index })
        {
            return;
        }
        System.Drawing.Color current = System.Drawing.Color.FromArgb(
            LightChannelSettings.ManualPalette[Math.Clamp(_manualIndexes[index], 1, 29) - 1]);
        ColorPickerWindow picker = new(current) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not System.Drawing.Color picked)
        {
            return;
        }
        _manualIndexes[index] = NearestPaletteIndex(picked);
        RefreshKeyboardUi();
        ScheduleAutoApply();
    }

    private static int NearestPaletteIndex(System.Drawing.Color color)
    {
        int best = 1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < LightChannelSettings.ManualPalette.Length; i++)
        {
            System.Drawing.Color c = System.Drawing.Color.FromArgb(LightChannelSettings.ManualPalette[i]);
            int distance = (c.R - color.R) * (c.R - color.R) + (c.G - color.G) * (c.G - color.G) + (c.B - color.B) * (c.B - color.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i + 1;
            }
        }
        return best;
    }

    private void UpdateColorButton()
    {
        System.Drawing.Color c = System.Drawing.Color.FromArgb(_colorArgb);
        ColorButton.Content = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        ColorButton.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
    }

    private void Effect_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshColorUi();
        ScheduleAutoApply();
    }

    private void Light_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_keyboard)
        {
            _light = Math.Clamp((int)Math.Round(e.NewValue), (int)LightSlider.Minimum, _lightMax);
            if (LightText != null)
            {
                LightText.Text = (_wujieWhite || _whiteBacklightOnly) ? (_light >= 2 ? WpfStrings.LightLevelBright : WpfStrings.LightLevelDim) : (_light + "/" + _lightMax);
            }
            ScheduleAutoApply();
            return;
        }
        _light = _gradeBrightness
            ? Math.Clamp((int)Math.Round(e.NewValue), 0, 4)
            : Math.Clamp((int)Math.Round(e.NewValue * 4 / 100.0), 0, 4);
        if (LightText != null)
        {
            LightText.Text = ((int)e.NewValue).ToString();
        }
        ScheduleAutoApply();
    }

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_keyboard)
        {
            _speed = Math.Clamp((int)Math.Round(e.NewValue), 0, 4);
            if (SpeedText != null)
            {
                SpeedText.Text = _speed + "/4";
            }
            ScheduleAutoApply();
            return;
        }
        _speed = Math.Clamp((int)Math.Round(e.NewValue), 0, 4);
        if (SpeedText != null)
        {
            SpeedText.Text = _speed.ToString();
        }
        ScheduleAutoApply();
    }

    private void Mix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index } button || index < 0 || index >= _mixColors.Length)
        {
            return;
        }
        ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(_mixColors[index])) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not System.Drawing.Color picked)
        {
            return;
        }
        _mixColors[index] = picked.ToArgb();
        System.Drawing.Color c = picked;
        button.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        button.ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        try
        {
            LightChannelSettings mixSettings = LightingSettingsStore.Load(_topic, "ColorMarquee");
            LightingSettingsStore.Save(_topic, mixSettings with { MixColors = (int[])_mixColors.Clone() });
        }
        catch
        {
        }
        ScheduleAutoApply();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(_colorArgb)) { Owner = this };
        if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
        {
            _colorArgb = picked.ToArgb();
            _usePresetColor = picker.PresetIndex.HasValue;
            _monochromeIndex = picker.PresetIndex ?? 0;
            UpdateColorButton();
            ScheduleAutoApply();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        _applyTimer.Stop();
        await ApplyCoreAsync(fromAuto: false);
    }

    private async Task<bool> ApplyCoreAsync(bool fromAuto)
    {
        if (_applying)
        {
            if (fromAuto)
            {
                _pendingAutoApply = true;
            }
            return false;
        }
        _applying = true;
        try
        {
            // 单键 RGB 的 HID/固件写可能较慢；先让出一次 UI 线程，避免拖动时窗口被判无响应。
            await Task.Yield();
            string effect = SelectedEffect;
            bool power = PowerCheck.IsChecked == true;
            bool powerWasOn = _settings.PowerOn;
            if (power && !powerWasOn && _keyboard && _light <= 0)
            {
                _light = 1;
                LightSlider.Value = 1;
                LightText.Text = (_wujieWhite || _whiteBacklightOnly) ? WpfStrings.LightLevelDim : (_light + "/" + _lightMax);
            }
            bool manualKeyboard = string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal)
                && string.Equals(effect, "Manual", StringComparison.Ordinal);
            int applySpeed = _keyboard ? (manualKeyboard ? _manualInterval : 0) : _speed;
            LightChannelSettings next = _settings with
            {
                Effect = effect,
                Light = (_wujieWhite || _whiteBacklightOnly) ? (_light >= 2 ? 4 : 2) : _light,
                Speed = manualKeyboard ? _manualInterval : (_keyboard ? _settings.Speed : _speed),
                ColorArgb = _colorArgb,
                UsePresetColor = _usePresetColor,
                MonochromeIndex = _monochromeIndex,
                MixColors = (int[])_mixColors.Clone(),
                ManualIndexes = (int[])_manualIndexes.Clone(),
                BreathingIndex = _breathingIndex,
                PowerOn = power
            };
            bool powerChanged = _settings.PowerOn != power;
            LightingSettingsStore.Save(_topic, next);
            if (powerChanged)
            {
                LightingSettingsStore.SavePower(_topic, power, "用户编辑灯光电源");
            }
            _settings = next;
            if (L_Mechrevo.WpfP0.Services.WpfBackend.Service == null)
            {
                if (!fromAuto)
                {
                    StatusText.Text = WpfStrings.LightEditorWindow_xaml_Text_3;
                }
                return false;
            }
            bool ok = true;
            if (string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal))
            {
                if (power)
                {
                    await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardPower(true);
                    if (!powerWasOn)
                    {
                        await Task.Delay(300);
                    }
                    if (_wujieWhite)
                    {
                        ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardBrightnessPreservingEffect(_light >= 2 ? 4 : 2);
                        return ok;
                    }
                    if (_whiteBacklightOnly)
                    {
                        // task-17：白色背光只有 2 档亮度，硬件协议用 2/4 两档；
                        // 不下发效果/颜色。
                        ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardBrightnessPreservingEffect(_light >= 2 ? 4 : 2);
                        return ok;
                    }
                    bool baolongda = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda == true;
                    System.Drawing.Color? color = baolongda
                        ? System.Drawing.Color.FromArgb(L_Mechrevo.WpfP0.Services.WpfBackend.Hw != null ? _settings.EffectiveColorArgb() : _colorArgb)
                        : (string.Equals(effect, "Single", StringComparison.Ordinal)
                            ? System.Drawing.Color.FromArgb(_colorArgb)
                            : null);
                    ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardEffect(effect, _light, applySpeed, "None", color, save: true);
                    if (ok)
                    {
                        ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardBrightnessPreservingEffect(_light);
                    }
                }
                else
                {
                    ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetKeyboardPower(false);
                }
            }
            else if (power)
            {
                bool nucLightbar = string.Equals(_topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
                    && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is { } lbHw
                    && (lbHw.IsIntelNucMachine || lbHw.IsUmiMachine);
                if (nucLightbar)
                {
                    // NUC/UMI 灯带走本地 ACPI 通道；XAML 编辑器原来走 GCU service，
                    // 彩虹/混色的亮度和颜色根本没下发。
                    MechrevoLite.Hardware.UmiLightbar.SetPower(true);
                    if (!powerWasOn)
                    {
                        await Task.Delay(600);
                    }
                    System.Drawing.Color nucColor = System.Drawing.Color.FromArgb(_colorArgb);
                    ok = MechrevoLite.Hardware.UmiLightbar.SetEffect(effect, nucColor, _light);
                }
                else
                {
                    await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetLightPower(_topic, true);
                    if (!powerWasOn)
                    {
                        await Task.Delay(600);
                    }
                    System.Drawing.Color? color = KeyboardFirmwareEffects.IsSingleColorEffect(effect)
                        ? System.Drawing.Color.FromArgb(_colorArgb)
                        : null;
                    ok = await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetLightEffect(_topic, effect, _light, _speed, "None", color);
                }
            }
            else
            {
                bool nucLightbarOff = string.Equals(_topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
                    && L_Mechrevo.WpfP0.Services.WpfBackend.Hw is { } lbHwOff
                    && (lbHwOff.IsIntelNucMachine || lbHwOff.IsUmiMachine);
                ok = nucLightbarOff
                    ? MechrevoLite.Hardware.UmiLightbar.SetPower(false)
                    : await L_Mechrevo.WpfP0.Services.WpfBackend.Service.SetLightPower(_topic, false);
            }
            if (ok)
            {
                if (powerChanged)
                {
                    L_Mechrevo.WpfP0.OsdService.Show(OriginalStyleWindow.LightChannelGlyph(_topic),
                        OriginalStyleWindow.LightChannelTitle(_topic), power ? WpfStrings.StateOn : WpfStrings.StateOff);
                }
                else
                {
                    bool white = _wujieWhite || _whiteBacklightOnly;
                    L_Mechrevo.WpfP0.OsdService.Show(OriginalStyleWindow.LightChannelGlyph(_topic), WpfStrings.OsdLighting,
                        OriginalStyleWindow.LightLevelText(_topic, _light, white));
                }
            }
            if (!fromAuto || !ok)
            {
                StatusText.Text = ok ? WpfStrings.LightSavedApplied : WpfStrings.LightSavedUnconfirmed;
            }
            return ok;
        }
        catch (Exception ex)
        {
            if (!fromAuto)
            {
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
            }
            return false;
        }
        finally
        {
            _applying = false;
            if (_pendingAutoApply)
            {
                _pendingAutoApply = false;
                _applyTimer.Start();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
