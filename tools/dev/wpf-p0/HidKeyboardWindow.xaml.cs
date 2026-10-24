using System;
using System.Windows;
using System.Windows.Controls;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

public partial class HidKeyboardWindow : Window
{
    private readonly KeyboardRgb _rgb;

    private readonly (int Mode, string Name)[] _effects;
    private System.Drawing.Color _color = System.Drawing.Color.FromArgb(0, 120, 255);

    public HidKeyboardWindow(KeyboardRgb rgb)
    {
        InitializeComponent();
        _rgb = rgb;
        _effects = MechrevoLite.RgbForm.VisibleHidEffects(Program.hw?.Capabilities.KeyboardType ?? 0);
        PowerCheck.IsChecked = _rgb.KbPowerOn;
        BrightnessSlider.Value = Math.Clamp(_rgb.Brightness, 0, 100);
        BrightnessText.Text = ((int)BrightnessSlider.Value) + "%";
        BrightnessSlider.ValueChanged += (_, e) => BrightnessText.Text = ((int)e.NewValue) + "%";
        int selected = 0;
        for (int i = 0; i < _effects.Length; i++)
        {
            EffectCombo.Items.Add(new ComboBoxItem { Content = _effects[i].Name, Tag = _effects[i].Mode });
            if (_effects[i].Mode == _rgb.KbHidMode)
            {
                selected = i;
            }
        }
        EffectCombo.SelectedIndex = selected;
        RefreshParams();
    }

    private static bool HasColor(int mode) => mode is 0 or 3 or 9;
    private static bool HasSpeed(int mode) => mode is 0 or 1 or 2 or 4 or 5 or 6 or 7 or 8;

    private void Effect_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshParams();
    }

    private void RefreshParams()
    {
        int mode = SelectedMode;
        ColorRow.Visibility = HasColor(mode) ? Visibility.Visible : Visibility.Collapsed;
        SpeedRow.Visibility = HasSpeed(mode) ? Visibility.Visible : Visibility.Collapsed;
        AngleRow.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (HasColor(mode))
        {
            _color = mode switch
            {
                0 => _rgb.BreathColor,
                3 => _rgb.ReactiveColor,
                _ => _rgb.StaticColor
            };
            UpdateColorButton();
        }
        int speed = mode switch
        {
            0 => _rgb.BreathSpeed,
            1 => _rgb.WaveSpeed,
            2 => _rgb.SparkleSpeed,
            4 => _rgb.WheelSpeed,
            5 => _rgb.LightningSpeed,
            6 => _rgb.FlameSpeed,
            7 => _rgb.RainSpeed,
            8 => _rgb.MatrixSpeed,
            _ => 5
        };
        (double speedMin, double speedMax) = mode switch
        {
            0 => (1, 30),
            1 => (1, 20),
            2 => (1, 20),
            3 => (5, 60),
            4 => (1, 20),
            5 => (1, 20),
            6 => (1, 20),
            7 => (1, 5),
            8 => (1, 5),
            _ => (1, 10)
        };
        SpeedSlider.Minimum = speedMin;
        SpeedSlider.Maximum = speedMax;
        SpeedSlider.Value = Math.Clamp(speed, speedMin, speedMax);
        SpeedText.Text = ((int)SpeedSlider.Value).ToString();
        AngleSlider.Value = Math.Clamp(_rgb.WaveAngle, 0, 360);
        AngleText.Text = ((int)AngleSlider.Value) + "°";
        RefreshExtras(mode);
    }

    private void ConfigureExtra(Slider slider, TextBlock label, TextBlock value, string text, double min, double max, double current)
    {
        label.Text = text;
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = Math.Clamp(current, min, max);
        value.Text = ((int)slider.Value).ToString();
    }

    private void RefreshExtras(int mode)
    {
        Extra1Row.Visibility = Visibility.Collapsed;
        Extra2Row.Visibility = Visibility.Collapsed;
        Extra3Row.Visibility = Visibility.Collapsed;
        ExtraCheck.Visibility = Visibility.Collapsed;
        ExtraCheck.IsChecked = false;
        switch (mode)
        {
            case 0:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "平滑", 0, 100, _rgb.BreathSmooth);
                break;
            case 2:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "密度", 1, 20, _rgb.SparkleDensity);
                break;
            case 3:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "时长", 5, 60, _rgb.ReactiveDuration);
                ExtraCheck.Visibility = Visibility.Visible;
                ExtraCheck.Content = "随机颜色";
                ExtraCheck.IsChecked = _rgb.ReactiveRandom;
                break;
            case 4:
                ExtraCheck.Visibility = Visibility.Visible;
                ExtraCheck.Content = "反向";
                ExtraCheck.IsChecked = _rgb.WheelReverse;
                break;
            case 5:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "宽度", 1, 4, _rgb.LightningWidth);
                Extra2Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra2Slider, Extra2Label, Extra2Text, "并发", 1, 4, _rgb.LightningConcurrent);
                Extra3Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra3Slider, Extra3Label, Extra3Text, "平滑", 1, 40, _rgb.LightningSmooth);
                break;
            case 6:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "平滑", 1, 40, _rgb.FlameSmooth);
                break;
            case 7:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "密度", 1, 5, _rgb.RainDensity);
                break;
            case 8:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "密度", 1, 5, _rgb.MatrixDensity);
                Extra2Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra2Slider, Extra2Label, Extra2Text, "样式", 0, 3, _rgb.MatrixStyle);
                break;
            case 9:
                Extra1Row.Visibility = Visibility.Visible;
                ConfigureExtra(Extra1Slider, Extra1Label, Extra1Text, "布局", 0, 1, _rgb.StaticLayout);
                break;
        }
        RefreshMultiColors(mode);
    }

    private void RefreshMultiColors(int mode)
    {
        ColorCountRow.Visibility = Visibility.Collapsed;
        MultiColorPanel.Visibility = Visibility.Collapsed;
        if (mode == 7)
        {
            ColorCountRow.Visibility = Visibility.Visible;
            MultiColorPanel.Visibility = Visibility.Visible;
            ColorCountLabel.Text = "雨滴颜色数";
            ColorCountSlider.Minimum = 1;
            ColorCountSlider.Maximum = 5;
            ColorCountSlider.Value = Math.Clamp(_rgb.RainColorCount, 1, 5);
            MultiColorLabel.Text = "雨滴颜色";
            BuildMultiColors(_rgb.RainColors, 5);
        }
        else if (mode == 8)
        {
            ColorCountRow.Visibility = Visibility.Visible;
            MultiColorPanel.Visibility = Visibility.Visible;
            ColorCountLabel.Text = "矩阵颜色数";
            ColorCountSlider.Minimum = 1;
            ColorCountSlider.Maximum = 3;
            ColorCountSlider.Value = Math.Clamp(_rgb.MatrixColorCount, 1, 3);
            MultiColorLabel.Text = "矩阵颜色";
            BuildMultiColors(_rgb.MatrixColors, 3);
        }
        else if (mode == 9)
        {
            ColorCountRow.Visibility = Visibility.Visible;
            MultiColorPanel.Visibility = Visibility.Visible;
            ColorCountLabel.Text = "静态分区数";
            ColorCountSlider.Minimum = 1;
            ColorCountSlider.Maximum = 10;
            ColorCountSlider.Value = Math.Clamp(_rgb.StaticZones, 1, 10);
            MultiColorLabel.Text = "分区颜色";
            BuildMultiColors(_rgb.StaticColors, 10);
        }
    }

    private void BuildMultiColors(System.Drawing.Color[] colors, int max)
    {
        MultiColorGrid.Children.Clear();
        int count = Math.Clamp((int)ColorCountSlider.Value, 1, max);
        for (int i = 0; i < count; i++)
        {
            int index = i;
            System.Drawing.Color c = colors[Math.Clamp(index, 0, colors.Length - 1)];
            Button button = new()
            {
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Tag = index,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            };
            button.Click += MultiColor_Click;
            MultiColorGrid.Children.Add(button);
        }
    }

    private void ColorCount_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ColorCountText != null)
        {
            ColorCountText.Text = ((int)e.NewValue).ToString();
        }
        if (MultiColorGrid != null)
        {
            int mode = SelectedMode;
            if (mode == 7)
            {
                BuildMultiColors(_rgb.RainColors, 5);
            }
            else if (mode == 8)
            {
                BuildMultiColors(_rgb.MatrixColors, 3);
            }
            else if (mode == 9)
            {
                BuildMultiColors(_rgb.StaticColors, 10);
            }
        }
    }

    private void MultiColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index } button)
        {
            return;
        }
        int mode = SelectedMode;
        System.Drawing.Color current = mode switch
        {
            7 => _rgb.RainColors[Math.Clamp(index, 0, _rgb.RainColors.Length - 1)],
            8 => _rgb.MatrixColors[Math.Clamp(index, 0, _rgb.MatrixColors.Length - 1)],
            _ => _rgb.StaticColors[Math.Clamp(index, 0, _rgb.StaticColors.Length - 1)]
        };
        ColorPickerWindow picker = new(current) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not System.Drawing.Color picked)
        {
            return;
        }
        if (mode == 7)
        {
            _rgb.RainColors[Math.Clamp(index, 0, _rgb.RainColors.Length - 1)] = picked;
        }
        else if (mode == 8)
        {
            _rgb.MatrixColors[Math.Clamp(index, 0, _rgb.MatrixColors.Length - 1)] = picked;
        }
        else
        {
            _rgb.StaticColors[Math.Clamp(index, 0, _rgb.StaticColors.Length - 1)] = picked;
        }
        button.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(picked.A, picked.R, picked.G, picked.B));
    }

    private void Extra1_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Extra1Text != null)
        {
            Extra1Text.Text = ((int)e.NewValue).ToString();
        }
    }

    private void Extra2_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Extra2Text != null)
        {
            Extra2Text.Text = ((int)e.NewValue).ToString();
        }
    }

    private void Extra3_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Extra3Text != null)
        {
            Extra3Text.Text = ((int)e.NewValue).ToString();
        }
    }

    private void UpdateColorButton()
    {
        ColorButton.Content = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        ColorButton.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(_color.A, _color.R, _color.G, _color.B));
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerWindow picker = new(_color) { Owner = this };
        if (picker.ShowDialog() == true && picker.Result is System.Drawing.Color picked)
        {
            _color = picked;
            UpdateColorButton();
        }
    }

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedText != null)
        {
            SpeedText.Text = ((int)e.NewValue).ToString();
        }
    }

    private void Angle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AngleText != null)
        {
            AngleText.Text = ((int)e.NewValue) + "°";
        }
    }

    private void ApplyEffectParams(int mode)
    {
        int speed = Math.Clamp((int)Math.Round(SpeedSlider.Value), 1, 10);
        switch (mode)
        {
            case 0:
                _rgb.BreathColor = _color;
                _rgb.BreathSpeed = speed;
                break;
            case 1:
                _rgb.WaveAngle = Math.Clamp((int)Math.Round(AngleSlider.Value), 0, 360);
                _rgb.WaveSpeed = speed;
                break;
            case 2:
                _rgb.SparkleSpeed = speed;
                break;
            case 3:
                _rgb.ReactiveColor = _color;
                _rgb.ReactiveDuration = Math.Clamp(speed * 4, 4, 100);
                break;
            case 4:
                _rgb.WheelSpeed = speed;
                break;
            case 5:
                _rgb.LightningSpeed = speed;
                break;
            case 6:
                _rgb.FlameSpeed = speed;
                break;
            case 7:
                _rgb.RainSpeed = speed;
                break;
            case 8:
                _rgb.MatrixSpeed = speed;
                break;
            case 9:
                _rgb.StaticColor = _color;
                break;
        }
        ApplyExtras(mode);
    }

    private void ApplyExtras(int mode)
    {
        int extra1 = (int)Math.Round(Extra1Slider.Value);
        int extra2 = (int)Math.Round(Extra2Slider.Value);
        bool check = ExtraCheck.IsChecked == true;
        switch (mode)
        {
            case 0: _rgb.BreathSmooth = extra1; break;
            case 2: _rgb.SparkleDensity = extra1; break;
            case 3:
                _rgb.ReactiveDuration = Math.Clamp(extra1, 4, 100);
                _rgb.ReactiveRandom = check;
                break;
            case 4: _rgb.WheelReverse = check; break;
            case 5:
                _rgb.LightningWidth = Math.Clamp(extra1, 1, 4);
                _rgb.LightningConcurrent = Math.Clamp(extra2, 1, 4);
                _rgb.LightningSmooth = Math.Clamp((int)Math.Round(Extra3Slider.Value), 1, 40);
                break;
            case 6: _rgb.FlameSmooth = extra1; break;
            case 7:
                _rgb.RainDensity = extra1;
                _rgb.RainColorCount = Math.Clamp((int)ColorCountSlider.Value, 1, 5);
                break;
            case 8:
                _rgb.MatrixDensity = extra1;
                _rgb.MatrixStyle = extra2;
                _rgb.MatrixColorCount = Math.Clamp((int)ColorCountSlider.Value, 1, 3);
                break;
            case 9:
                _rgb.StaticZones = Math.Clamp((int)ColorCountSlider.Value, 1, 10);
                _rgb.StaticLayout = Math.Clamp(extra1, 0, 1);
                break;
        }
    }

    private int SelectedMode =>
        EffectCombo.SelectedItem is ComboBoxItem { Tag: int mode } ? mode : _effects[0].Mode;

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool power = PowerCheck.IsChecked == true;
            _rgb.KbPowerOn = power;
            if (!power)
            {
                _rgb.StopCurrentEffect();
                _rgb.ApplyBrightnessToDevice();
                _rgb.SaveConfig();
                StatusText.Text = "HID 键盘灯已关闭";
                return;
            }
            int brightness = Math.Clamp((int)Math.Round(BrightnessSlider.Value), 1, 100);
            _rgb.Brightness = brightness;
            bool ready = _rgb.IsConnected || await _rgb.EnsureHidReadyAsync() || _rgb.Connect();
            if (!ready)
            {
                StatusText.Text = "HID 连接失败：" + _rgb.LastError;
                return;
            }
            ApplyEffectParams(SelectedMode);
            _rgb.KbHidMode = SelectedMode;
            _rgb.StartMode(SelectedMode);
            _rgb.ApplyBrightnessToDevice();
            _rgb.SaveConfig();
            StatusText.Text = "HID 键盘效果已应用";
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
