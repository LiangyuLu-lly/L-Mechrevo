using System;
using L_Mechrevo.WpfP0.Resources;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0;

public partial class DisplayEffectEditorWindow : Window
{
    private readonly string _mode;
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly Dictionary<string, TextBlock> _values = new();
    private readonly DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _syncing;

    public DisplayEffectEditorWindow(string mode)
    {
        InitializeComponent();
        _mode = mode ?? "";
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        BuildSliders();
        LoadProfile();
        _commitTimer.Tick += async (_, _) =>
        {
            _commitTimer.Stop();
            await CommitAsync();
        };
    }

    private void BuildSliders()
    {
        AddRow("brightness", WpfStrings.EffectBrightness, 0, 100, 50, v => v + "%");
        AddRow("colortemp", WpfStrings.EffectColorTemp, 30, 100, 65, v => (v * 100) + "K");
        AddRow("saturation", WpfStrings.EffectSaturation, 5, 25, 15, v => (v * 0.1).ToString("0.0"));
        AddRow("gamma", WpfStrings.EffectGamma, 50, 200, 100, v => (v * 0.01).ToString("0.00"));
        AddRow("r", WpfStrings.ColorRed, 0, 255, 128, v => v.ToString());
        AddRow("g", WpfStrings.ColorGreen, 0, 255, 128, v => v.ToString());
        AddRow("b", WpfStrings.ColorBlue, 0, 255, 128, v => v.ToString());
        AddRow("contrast", WpfStrings.EffectContrast, 5, 20, 10, v => (v * 0.1).ToString("0.0"));
    }

    private void AddRow(string key, string label, int min, int max, int value, Func<int, string> format)
    {
        Grid row = new() { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        TextBlock caption = new()
        {
            Text = label,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(caption);

        Slider slider = new()
        {
            Style = (Style)Application.Current.Resources["RoundSlider"],
            Minimum = min,
            Maximum = max,
            Value = value,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        TextBlock valueText = new()
        {
            Text = format(value),
            TextAlignment = TextAlignment.Right,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);

        int current = value;
        slider.ValueChanged += (_, e) =>
        {
            int newValue = (int)Math.Round(e.NewValue);
            valueText.Text = format(newValue);
            if (!_syncing)
            {
                _commitTimer.Stop();
                _commitTimer.Start();
            }
        };

        _sliders[key] = slider;
        _values[key] = valueText;
        SliderPanel.Children.Add(row);
    }

    private void LoadProfile()
    {
        DisplayEffectProfile? profile = null;
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Hw != null && L_Mechrevo.WpfP0.Services.WpfBackend.Hw.DisplayEffectProfiles.TryGetValue(_mode, out DisplayEffectProfile? found))
        {
            profile = found;
        }
        profile ??= MechrevoLite.Hardware.SoftwareColorEnhancement.DefaultProfile(_mode);
        _syncing = true;
        try
        {
            SetValue("brightness", Math.Clamp(profile.Brightness, 0, 100));
            SetValue("colortemp", Math.Clamp(profile.ColorTemp / 100, 30, 100));
            SetValue("saturation", Math.Clamp((int)Math.Round(profile.VibrantValue * 10), 5, 25));
            SetValue("gamma", Math.Clamp((int)Math.Round(profile.Gamma * 100), 50, 200));
            SetValue("r", Math.Clamp((int)Math.Round(profile.ColorR), 0, 255));
            SetValue("g", Math.Clamp((int)Math.Round(profile.ColorG), 0, 255));
            SetValue("b", Math.Clamp((int)Math.Round(profile.ColorB), 0, 255));
            SetValue("contrast", Math.Clamp((int)Math.Round(profile.Constrast * 10), 5, 20));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetValue(string key, int value)
    {
        if (_sliders.TryGetValue(key, out Slider? slider))
        {
            slider.Value = value;
        }
    }

    private async Task CommitAsync()
    {
        if (L_Mechrevo.WpfP0.Services.WpfBackend.Service == null || string.IsNullOrWhiteSpace(_mode))
        {
            return;
        }
        DisplayEffectProfile profile = new()
        {
            Name = _mode,
            Brightness = Get("brightness"),
            ColorTemp = Get("colortemp") * 100,
            ColorR = Get("r"),
            ColorG = Get("g"),
            ColorB = Get("b"),
            VibrantValue = Get("saturation") * 0.1f,
            Constrast = Get("contrast") * 0.1f,
            Gamma = Get("gamma") * 0.01f
        };
        MechrevoLite.Hardware.MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        bool hasGcuProfile = hw != null && hw.DisplayEffectProfiles.ContainsKey(_mode);
        if (hasGcuProfile)
        {
            await L_Mechrevo.WpfP0.Services.WpfBackend.Service.ApplyDisplayEffectProfile(profile);
        }
        else
        {
            MechrevoLite.Hardware.SoftwareColorEnhancement.ApplyProfile(profile.Name, profile.Brightness,
                profile.ColorTemp, profile.ColorR, profile.ColorG, profile.ColorB,
                profile.VibrantValue, profile.Constrast, profile.Gamma);
        }
    }

    private int Get(string key)
    {
        return _sliders.TryGetValue(key, out Slider? slider) ? (int)Math.Round(slider.Value) : 0;
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_mode)) return;
        MechrevoLite.Hardware.MechrevoHw? hw = L_Mechrevo.WpfP0.Services.WpfBackend.Hw;
        bool hasGcuProfile = hw != null && hw.DisplayEffectProfiles.ContainsKey(_mode);
        if (hasGcuProfile && L_Mechrevo.WpfP0.Services.WpfBackend.Service != null)
        {
            if (await L_Mechrevo.WpfP0.Services.WpfBackend.Service.ResetDisplayEffectProfile(_mode))
            {
                await L_Mechrevo.WpfP0.Services.WpfBackend.Service.RequestDisplayEffectStatus();
                await Task.Delay(500);
            }
        }
        else
        {
            // 没有 GCU 档位表 / NUC 本机软件路径：恢复当前档位的软件默认，而不是回多彩。
            MechrevoLite.Hardware.SoftwareColorEnhancement.Apply(_mode);
        }
        LoadProfile();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyDarkTitleBar()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        int enabled = 1;
        DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
