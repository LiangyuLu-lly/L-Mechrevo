using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;

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
    private int[] _mixColors = (int[])LightChannelSettings.DefaultMixColors.Clone();
    private bool _gradeBrightness;
    private readonly bool _keyboard;
    private bool _gradeColors;
    private bool _logoGrade;
    private int _lightMax = 4;
    private readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _suppressAutoApply;
    private bool _applying;

    public LightEditorWindow(string topic, string title, (string Effect, string Name)[] effects)
    {
        InitializeComponent();
        _topic = topic;
        _effects = effects;
        _keyboard = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
        Title = title;
        TitleText.Text = title;
        MechrevoHw? hw = Program.hw;
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
            bool wujie = hw?.WujieSmuApu == true;
            _lightMax = wujie ? 2 : (bd ? Math.Max(1, MechrevoHw.EffectiveKeyboardBrightnessMax()) : 4);
            LightSlider.Minimum = bd || wujie ? 1 : 0;
            LightSlider.Maximum = _lightMax;
        }
        _settings = LightingSettingsStore.Load(topic, effects.Length > 0 ? effects[0].Effect : "Single");
        _light = Math.Clamp(_settings.Light, (int)LightSlider.Minimum, (int)LightSlider.Maximum);
        _speed = Math.Clamp(_settings.Speed, 0, 4);
        _colorArgb = _settings.EffectiveColorArgb();
        if (_settings.ManualIndexes is { Length: >= 6 } manual)
        {
            _manualIndexes = (int[])manual.Clone();
        }
        _manualInterval = Math.Clamp(_settings.Speed, 2, 10);
        _breathingIndex = Math.Clamp(_settings.BreathingIndex, 1, 4);
        if (_settings.MixColors is { Length: 7 } mix)
        {
            _mixColors = (int[])mix.Clone();
        }
        PowerCheck.IsChecked = _settings.PowerOn;
        if (_keyboard)
        {
            HintText.Text = "单分区键盘：呼吸四色、彩虹不可改色、亮度为 x/4、Manual 每键时间 2~10。";
            StatusText.Text = "单分区键盘模式";
        }
        _suppressAutoApply = true;
        NetSliderValues();
        LoadEffects();
        RefreshColorUi();
        BuildKeyboardExtra();
        _suppressAutoApply = false;
        PowerCheck.Checked += (_, _) => ScheduleAutoApply();
        PowerCheck.Unchecked += (_, _) => ScheduleAutoApply();
        _applyTimer.Tick += async (_, _) =>
        {
            _applyTimer.Stop();
            await ApplyCoreAsync(fromAuto: true);
        };
    }

    private void ScheduleAutoApply()
    {
        if (_suppressAutoApply || !IsLoaded)
        {
            return;
        }
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void NetSliderValues()
    {
        if (_keyboard)
        {
            LightSlider.Value = _light;
            LightText.Text = _light + "/" + _lightMax;
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
        string effect = SelectedEffect;
        if (_keyboard)
        {
            MixRow.Visibility = Visibility.Collapsed;
            bool wujie = Program.hw?.WujieSmuApu == true;
            bool keyboardManual = string.Equals(effect, "Manual", StringComparison.Ordinal);
            bool keyboardBreathing = string.Equals(effect, "Breathing", StringComparison.Ordinal);
            bool keyboardRainbow = string.Equals(effect, "Rainbow", StringComparison.Ordinal);
            ColorRow.Visibility = !wujie && !keyboardManual && !keyboardBreathing && !keyboardRainbow
                && KeyboardFirmwareEffects.IsSingleColorEffect(effect)
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateColorButton();
            RefreshKeyboardUi();
            return;
        }
        if (_gradeColors)
        {
            int mode = LightForm.LightEffectColorMode(effect);
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
                ToolTip = "呼吸颜色 " + (index + 1)
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
        bool breathing = string.Equals(effect, "Breathing", StringComparison.Ordinal);
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
                LightText.Text = _light + "/" + _lightMax;
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
        ScheduleAutoApply();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerWindow picker = new(System.Drawing.Color.FromArgb(_colorArgb)) { Owner = this };
        if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
        {
            _colorArgb = picked.ToArgb();
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
            return false;
        }
        _applying = true;
        try
        {
            string effect = SelectedEffect;
            bool power = PowerCheck.IsChecked == true;
            LightChannelSettings next = _settings with
            {
                Effect = effect,
                Light = _light,
                Speed = string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal)
                    && string.Equals(effect, "Manual", StringComparison.Ordinal)
                    ? _manualInterval
                    : _speed,
                ColorArgb = _colorArgb,
                MixColors = (int[])_mixColors.Clone(),
                ManualIndexes = (int[])_manualIndexes.Clone(),
                BreathingIndex = _breathingIndex,
                PowerOn = power
            };
            LightingSettingsStore.Save(_topic, next);
            _settings = next;
            if (Program.service == null)
            {
                if (!fromAuto)
                {
                    StatusText.Text = "保存成功，但后端未连接，未下发";
                }
                return false;
            }
            bool ok = true;
            if (string.Equals(_topic, "Keyboard/Ctrl", StringComparison.Ordinal))
            {
                if (power)
                {
                    int colorMode = LightForm.LightEffectColorMode(effect);
                    System.Drawing.Color? color = colorMode == 1
                        ? System.Drawing.Color.FromArgb(_colorArgb)
                        : null;
                    ok = await Program.service.SetKeyboardEffect(effect, _light, _speed, "None", color, save: true);
                }
                else
                {
                    ok = await Program.service.SetKeyboardPower(false);
                }
            }
            else if (power)
            {
                int colorMode = LightForm.LightEffectColorMode(effect);
                System.Drawing.Color? color = colorMode == 1
                    ? System.Drawing.Color.FromArgb(_colorArgb)
                    : null;
                ok = await Program.service.SetLightEffect(_topic, effect, _light, _speed, "None", color);
            }
            else
            {
                ok = await Program.service.SetLightPower(_topic, false);
            }
            if (!fromAuto || !ok)
            {
                StatusText.Text = ok ? "已保存并下发" : "已保存，但设备未确认";
            }
            return ok;
        }
        catch (Exception ex)
        {
            if (!fromAuto)
            {
                StatusText.Text = "应用失败：" + ex.Message;
            }
            return false;
        }
        finally
        {
            _applying = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
