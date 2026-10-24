using System;
using System.Windows;
using System.Windows.Controls;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

public partial class HidKeyboardWindow : Window
{
    private readonly KeyboardRgb _rgb;
    private readonly Action _openAdvanced;
    private readonly (int Mode, string Name)[] _effects;

    public HidKeyboardWindow(KeyboardRgb rgb, Action openAdvanced)
    {
        InitializeComponent();
        _rgb = rgb;
        _openAdvanced = openAdvanced;
        _effects = MechrevoLite.RgbForm.VisibleHidEffects(Program.hw?.Capabilities.KeyboardType ?? 0);
        PowerCheck.IsChecked = _rgb.KbPowerOn;
        BrightnessSlider.Value = Math.Clamp(_rgb.Brightness, 1, 100);
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

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _openAdvanced();
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开高级参数失败：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
