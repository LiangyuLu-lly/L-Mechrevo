using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private int _colorArgb;
    private int[] _mixColors = (int[])LightChannelSettings.DefaultMixColors.Clone();

    public LightEditorWindow(string topic, string title, (string Effect, string Name)[] effects)
    {
        InitializeComponent();
        _topic = topic;
        _effects = effects;
        Title = title;
        TitleText.Text = title;
        _settings = LightingSettingsStore.Load(topic, effects.Length > 0 ? effects[0].Effect : "Single");
        _light = Math.Clamp(_settings.Light, 0, 4);
        _speed = Math.Clamp(_settings.Speed, 0, 4);
        _colorArgb = _settings.EffectiveColorArgb();
        if (_settings.MixColors is { Length: 7 } mix)
        {
            _mixColors = (int[])mix.Clone();
        }
        PowerCheck.IsChecked = _settings.PowerOn;
        NetSliderValues();
        LoadEffects();
        RefreshColorUi();
    }

    private void NetSliderValues()
    {
        LightSlider.Value = _light * 25;
        LightText.Text = ((int)LightSlider.Value).ToString();
        SpeedSlider.Value = _speed * 25;
        SpeedText.Text = ((int)SpeedSlider.Value).ToString();
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
        int mode = LightForm.LightEffectColorMode(SelectedEffect);
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
        UpdateColorButton();
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
    }

    private void Light_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _light = Math.Clamp((int)Math.Round(e.NewValue / 25.0), 0, 4);
        if (LightText != null)
        {
            LightText.Text = ((int)e.NewValue).ToString();
        }
    }

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _speed = Math.Clamp((int)Math.Round(e.NewValue / 25.0), 0, 4);
        if (SpeedText != null)
        {
            SpeedText.Text = ((int)e.NewValue).ToString();
        }
    }

    private void Mix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index } button || index < 0 || index >= _mixColors.Length)
        {
            return;
        }
        using System.Windows.Forms.ColorDialog dialog = new()
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_mixColors[index])
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        _mixColors[index] = dialog.Color.ToArgb();
        System.Drawing.Color c = dialog.Color;
        button.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        button.ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.ColorDialog dialog = new()
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_colorArgb)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _colorArgb = dialog.Color.ToArgb();
            UpdateColorButton();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string effect = SelectedEffect;
            bool power = PowerCheck.IsChecked == true;
            LightChannelSettings next = _settings with
            {
                Effect = effect,
                Light = _light,
                Speed = _speed,
                ColorArgb = _colorArgb,
                MixColors = (int[])_mixColors.Clone(),
                PowerOn = power
            };
            LightingSettingsStore.Save(_topic, next);
            _settings = next;
            if (Program.service == null)
            {
                StatusText.Text = "保存成功，但后端未连接，未下发";
                return;
            }
            bool ok = true;
            if (power)
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
            StatusText.Text = ok ? "已保存并下发" : "已保存，但设备未确认";
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
