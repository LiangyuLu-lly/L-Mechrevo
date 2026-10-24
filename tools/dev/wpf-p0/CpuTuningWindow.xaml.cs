using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using PawnIO;

namespace L_Mechrevo.WpfP0;

public partial class CpuTuningWindow : Window
{
    private readonly List<(CpuTuningOption Option, Slider Slider, TextBlock Value)> _rows = new();
    private Slider? _fmaxSlider;
    private readonly List<Slider> _perCoreSliders = new();

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
            _perCoreSliders.Clear();
            _fmaxSlider = null;
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
            if (probe.FMax is { Capability: FMaxCapability.ReadOnly or FMaxCapability.ReadWrite or FMaxCapability.WriteNoReadback } fmax)
            {
                AddFMaxRow(fmax);
            }
            AddPerCoreSection(probe);
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

    private void AddPerCoreSection(CpuTuningProbe probe)
    {
        CpuTuningOption? option = probe.Options.FirstOrDefault(o => o.PerCoreWrite);
        if (option == null)
        {
            return;
        }
        int coreCount = RuntimeCpuTuning.PerCoreCapacity;
        if (coreCount <= 0)
        {
            return;
        }
        OptionsPanel.Children.Add(new TextBlock
        {
            Text = "每核 CO（Curve Optimizer）",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        });
        IReadOnlyList<int?> saved = RuntimeCpuTuning.LoadSavedPerCore(coreCount);
        List<Slider> sliders = _perCoreSliders;
        for (int core = 0; core < coreCount; core++)
        {
            Grid row = new() { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.Children.Add(new TextBlock { Text = "Core " + core, VerticalAlignment = VerticalAlignment.Center });
            decimal start = saved.Count > core && saved[core].HasValue
                ? saved[core]!.Value
                : (option.CoreReadback != null && option.CoreReadback.Count > core ? option.CoreReadback[core] : 0);
            Slider slider = new()
            {
                Minimum = (double)option.Minimum,
                Maximum = (double)option.Maximum,
                Value = Math.Clamp((double)start, (double)option.Minimum, (double)option.Maximum),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("RoundSlider")
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            TextBlock valueText = new()
            {
                Text = ((int)slider.Value).ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);
            slider.ValueChanged += (_, e) => valueText.Text = ((int)e.NewValue).ToString();
            sliders.Add(slider);
            OptionsPanel.Children.Add(row);
        }
        Button apply = new()
        {
            Content = "应用每核 CO",
            Style = (Style)FindResource("FlatButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 10)
        };
        apply.Click += async (_, _) =>
        {
            int[] values = sliders.Select(s => (int)Math.Round(s.Value)).ToArray();
            try
            {
                CpuTuningResult result = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyPerCore(values));
                StatusText.Text = result.Detail;
                if (result.Success)
                {
                    int allCore = RuntimeCpuTuning.LoadSavedAllCore() ?? values.FirstOrDefault();
                    RuntimeCpuTuning.SaveSettings(values, allCore);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "每核 CO 应用失败：" + ex.Message;
            }
        };
        OptionsPanel.Children.Add(apply);
    }

    private void AddFMaxRow(CpuFMaxProbe fmax)
    {
        Grid row = new() { Margin = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = "FMax (MHz)",
            VerticalAlignment = VerticalAlignment.Center
        });
        decimal start = fmax.Current ?? RuntimeCpuTuning.LoadSavedFMax() ?? 0m;
        double minimum = (double)RuntimeCpuTuning.FMaxMinimum;
        double maximum = (double)RuntimeCpuTuning.FMaxMaximum;
        Slider slider = new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp((double)start, minimum, maximum),
            IsEnabled = RuntimeCpuTuning.Instance.FMaxWritableCapability,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("RoundSlider")
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        _fmaxSlider = slider;
        TextBlock valueText = new()
        {
            Text = ((int)slider.Value).ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);
        slider.ValueChanged += (_, e) => valueText.Text = ((int)e.NewValue).ToString();
        Button apply = new()
        {
            Content = "应用",
            IsEnabled = RuntimeCpuTuning.Instance.FMaxWritableCapability,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("FlatButton")
        };
        Grid.SetColumn(apply, 3);
        apply.Click += async (_, _) =>
        {
            try
            {
                decimal mhz = (decimal)slider.Value;
                CpuTuningResult result = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyFMax(mhz));
                StatusText.Text = result.Detail;
            }
            catch (Exception ex)
            {
                StatusText.Text = "FMax 应用失败：" + ex.Message;
            }
        };
        row.Children.Add(apply);
        OptionsPanel.Children.Add(row);
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

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_perCoreSliders.Count > 0)
            {
                int[] values = _perCoreSliders.Select(s => (int)Math.Round(s.Value)).ToArray();
                int allCore = RuntimeCpuTuning.LoadSavedAllCore() ?? values.FirstOrDefault();
                RuntimeCpuTuning.SaveSettings(values, allCore);
            }
            if (_fmaxSlider != null && RuntimeCpuTuning.Instance.FMaxWritableCapability)
            {
                RuntimeCpuTuning.SaveFMax((int)_fmaxSlider.Value);
            }
            StatusText.Text = "快照已保存";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存快照失败：" + ex.Message;
        }
        await Task.CompletedTask;
    }

    private async void LoadSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int coreCount = RuntimeCpuTuning.PerCoreCapacity;
            IReadOnlyList<int?> saved = RuntimeCpuTuning.LoadSavedPerCore(coreCount);
            if (saved.Count == 0 || saved.All(v => !v.HasValue))
            {
                StatusText.Text = "没有可读取的每核 CO 快照";
                return;
            }
            int[] values = saved.Select((v, i) => v ?? 0).ToArray();
            CpuTuningResult co = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyPerCore(values));
            string detail = co.Detail;
            int? fmax = RuntimeCpuTuning.LoadSavedFMax();
            if (fmax.HasValue && RuntimeCpuTuning.Instance.FMaxWritableCapability)
            {
                CpuTuningResult fr = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyFMax(fmax.Value));
                detail += "；FMax：" + fr.Detail;
            }
            StatusText.Text = detail;
            LoadProbe();
        }
        catch (Exception ex)
        {
            StatusText.Text = "读取快照失败：" + ex.Message;
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
