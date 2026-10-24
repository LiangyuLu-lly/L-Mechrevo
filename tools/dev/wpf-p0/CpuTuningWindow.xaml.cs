using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;

namespace L_Mechrevo.WpfP0;

public partial class CpuTuningWindow : Window
{
    private readonly List<(CpuTuningOption Option, Slider Slider, TextBlock Value)> _rows = new();

    public CpuTuningWindow()
    {
        InitializeComponent();
        AutoApplyCheck.IsChecked = RuntimeCpuTuning.PerCoreAutoApplyEnabled;
        LoadProbe();
    }

    private static string Localized(string key)
    {
        try
        {
            return Strings.ResourceManager.GetString(key) ?? key;
        }
        catch
        {
            return key;
        }
    }

    private void LoadProbe()
    {
        try
        {
            CpuTuningProbe probe = RuntimeCpuTuning.Instance.Probe();
            CpuText.Text = probe.Cpu + " · " + Localized(probe.StatusKey);
            if (!string.IsNullOrWhiteSpace(probe.Detail))
            {
                StatusText.Text = probe.Detail;
            }
            OptionsPanel.Children.Clear();
            _rows.Clear();
            if (probe.Options.Count == 0)
            {
                OptionsPanel.Children.Add(new TextBlock
                {
                    Text = "该平台没有可调的 SMU/CO 选项。",
                    Style = (Style)FindResource("MutedText")
                });
                return;
            }
            foreach (CpuTuningOption option in probe.Options)
            {
                AddOptionRow(option);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "CPU 探针失败：" + ex.Message;
        }
    }

    private void AddOptionRow(CpuTuningOption option)
    {
        Grid row = new() { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock label = new()
        {
            Text = Localized(option.LabelKey),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        row.Children.Add(label);

        double minimum = (double)option.Minimum;
        double maximum = (double)option.Maximum;
        double value = (double)(option.Current ?? option.Minimum);
        Slider slider = new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            IsEnabled = option.CanWrite,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("RoundSlider")
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        TextBlock valueText = new()
        {
            Text = slider.Value.ToString("0") + option.Unit,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);
        slider.ValueChanged += (_, e) => valueText.Text = ((int)e.NewValue) + option.Unit;

        Button apply = new()
        {
            Content = "应用",
            IsEnabled = option.CanWrite,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("FlatButton")
        };
        Grid.SetColumn(apply, 3);
        apply.Click += async (_, _) => await ApplyOptionAsync(option, slider);
        row.Children.Add(apply);

        OptionsPanel.Children.Add(row);
        _rows.Add((option, slider, valueText));
    }

    private async Task ApplyOptionAsync(CpuTuningOption option, Slider slider)
    {
        try
        {
            decimal value = (decimal)slider.Value;
            CpuTuningResult result = await Task.Run(() => RuntimeCpuTuning.Instance.Apply(option.Id, value));
            StatusText.Text = result.Detail;
            if (!result.Success)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败：" + ex.Message;
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "重置所有 CPU 调节项？", "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            CpuTuningResult result = await Task.Run(() => RuntimeCpuTuning.Instance.ResetAll());
            StatusText.Text = result.Detail;
            LoadProbe();
        }
        catch (Exception ex)
        {
            StatusText.Text = "重置失败：" + ex.Message;
        }
    }

    private void AutoApply_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = AutoApplyCheck.IsChecked == true;
        if (enabled && MessageBox.Show(this,
                "开启后开机将按已保存的 CPU 调节值写入 SMU，是否继续？",
                "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            AutoApplyCheck.IsChecked = false;
            return;
        }
        RuntimeCpuTuning.SetAutoApply(enabled);
        StatusText.Text = enabled ? "已开启开机自动应用" : "已关闭开机自动应用";
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        new OriginalFormHostWindow(new MechrevoLite.CpuTuningForm(), "CPU 调节（高级）")
        {
            Owner = this
        }.ShowDialog();
        LoadProbe();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
