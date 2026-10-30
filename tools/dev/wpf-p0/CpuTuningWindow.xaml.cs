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

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class CpuTuningWindow : Window
{
    private readonly List<(CpuTuningOption Option, Slider Slider, TextBlock Value)> _rows = new();
    private TextBox? _fmaxBox;
    private Slider? _allCoreCoSlider;
    private bool _syncingPerCore;
    private readonly List<TextBox> _perCoreInputs = new();

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

    private static string ShortStatus(string key)
    {
        return key switch
        {
            "CpuTuneSmuReady" => "可调",
            "CpuTuneSmuPartial" => "部分选项被 SKU 锁定",
            "CpuTuneUnsupported" => "本机暂不支持",
            "CpuTuneDriverMissing" => "缺少 PawnIO 驱动",
            "CpuTuneDisabled" => "Intel 调节已屏蔽（XTU 独占 MSR）",
            _ => "检测完成"
        };
    }

    private void LoadProbe()
    {
        try
        {
            CpuTuningProbe probe = RuntimeCpuTuning.Instance.Probe();
            // 只显示一句摘要；完整探针细节放进 ToolTip，不再铺满窗口。
            CpuText.Text = probe.Cpu + " · " + ShortStatus(probe.StatusKey);
            CpuText.ToolTip = string.IsNullOrWhiteSpace(probe.Detail) ? null : probe.Detail;
            StatusText.Text = "";
            OptionsPanel.Children.Clear();
            _rows.Clear();
            _perCoreInputs.Clear();
            _fmaxBox = null;
            if (probe.Options.Count == 0)
            {
                OptionsPanel.Children.Add(new TextBlock
                {
                    Text = WpfStrings.CpuTuningWindow_xaml_Text,
                    Style = (Style)FindResource("MutedText")
                });
                return;
            }
            foreach (CpuTuningOption option in probe.Options)
            {
                // 机主口径：CPU 调节不再提供「固定全核频率」（amd-clock）。
                if (string.Equals(option.Id, "amd-clock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
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
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_2 + ex.Message;
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
        if (string.Equals(option.Id, "amd-co", StringComparison.OrdinalIgnoreCase))
        {
            _allCoreCoSlider = slider;
        }

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
            Content = WpfStrings.CpuTuningWindow_xaml_Content,
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
        // 即使本机 SKU 锁了 CO 写入，也把每核输入框铺出来（置灰但可见）；
        // 不再用 PerCoreWrite 作为“是否显示”的唯一条件。
        CpuTuningOption? option = probe.Options.FirstOrDefault(o => o.Id == "amd-co")
            ?? probe.Options.FirstOrDefault(o => o.PerCoreWrite);
        if (option == null)
        {
            return;
        }
        int coreCount = Math.Max(RuntimeCpuTuning.PerCoreCapacity,
            option.CoreReadback?.Count ?? 0);
        if (coreCount <= 0)
        {
            return;
        }
        bool editable = option.CanWrite && option.PerCoreWrite;
        OptionsPanel.Children.Add(new TextBlock
        {
            Text = WpfStrings.CpuTuningWindow_xaml_Text_3,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        });
        IReadOnlyList<int?> saved = RuntimeCpuTuning.LoadSavedPerCore(coreCount);
        List<TextBox> boxes = _perCoreInputs;
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        int rows = Math.Max(1, (coreCount + 1) / 2);
        for (int rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int side = 0; side < 2; side++)
            {
                int core = rowIndex + side * rows;
                if (core >= coreCount)
                {
                    continue;
                }
                decimal start = saved.Count > core && saved[core].HasValue
                    ? saved[core]!.Value
                    : (option.CoreReadback != null && option.CoreReadback.Count > core ? option.CoreReadback[core] : 0);
                TextBlock label = new()
                {
                    Text = "Core " + core,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(side == 0 ? 0 : 12, 2, 6, 2)
                };
                TextBox box = CreateValueBox(start.ToString("0"), editable);
                box.TextChanged += (_, _) =>
                {
                    if (_syncingPerCore || _allCoreCoSlider == null)
                    {
                        return;
                    }
                    int[] parsed = boxes.Select(b => int.TryParse(b.Text, out int v) ? v : int.MinValue).ToArray();
                    if (parsed.Length > 0 && parsed.All(v => v == parsed[0]) && parsed[0] != int.MinValue)
                    {
                        _syncingPerCore = true;
                        try { _allCoreCoSlider.Value = Math.Clamp(parsed[0], (int)_allCoreCoSlider.Minimum, (int)_allCoreCoSlider.Maximum); }
                        finally { _syncingPerCore = false; }
                    }
                };
                boxes.Add(box);
                Grid.SetRow(label, rowIndex);
                Grid.SetColumn(label, side == 0 ? 0 : 3);
                Grid.SetRow(box, rowIndex);
                Grid.SetColumn(box, side == 0 ? 1 : 4);
                grid.Children.Add(label);
                grid.Children.Add(box);
            }
        }
        OptionsPanel.Children.Add(grid);
        if (_allCoreCoSlider != null)
        {
            _allCoreCoSlider.ValueChanged += (_, e) =>
            {
                if (_syncingPerCore) return;
                _syncingPerCore = true;
                try
                {
                    string value = ((int)Math.Round(e.NewValue)).ToString();
                    foreach (TextBox box in boxes) box.Text = value;
                }
                finally
                {
                    _syncingPerCore = false;
                }
            };
        }
        if (!editable)
        {
            OptionsPanel.Children.Add(new TextBlock
            {
                Text = WpfStrings.L("每核 CO 当前被 SKU 锁定，仅显示回读值。", "Per-core CO is locked by the current SKU; readback only."),
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        Button apply = new()
        {
            Content = WpfStrings.CpuTuningWindow_xaml_Content_2,
            IsEnabled = editable,
            Style = (Style)FindResource("FlatButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 10)
        };
        apply.Click += async (_, _) =>
        {
            int[] values = boxes.Select(b => int.TryParse(b.Text, out int v) ? v : 0).ToArray();
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
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_4 + ex.Message;
            }
        };
        OptionsPanel.Children.Add(apply);
    }

    private TextBox CreateValueBox(string text, bool enabled)
    {
        TextBox box = new()
        {
            Text = text,
            Width = 74,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 2),
            TextAlignment = TextAlignment.Right,
            IsEnabled = enabled,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 4, 0)
        };
        try
        {
            box.Background = (Brush)FindResource("ControlBrush");
            box.Foreground = (Brush)FindResource("TextBrush");
            box.BorderBrush = (Brush)FindResource("CardBorderBrush");
            box.BorderThickness = new Thickness(1);
        }
        catch
        {
        }
        return box;
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
        TextBox box = CreateValueBox(((int)start).ToString(), RuntimeCpuTuning.Instance.FMaxWritableCapability);
        box.Width = 110;
        box.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        _fmaxBox = box;
        Button apply = new()
        {
            Content = WpfStrings.CpuTuningWindow_xaml_Content,
            IsEnabled = RuntimeCpuTuning.Instance.FMaxWritableCapability,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("FlatButton")
        };
        Grid.SetColumn(apply, 3);
        apply.Click += async (_, _) =>
        {
            if (!int.TryParse(box.Text, out int mhz))
            {
                StatusText.Text = WpfStrings.L("FMax 必须是整数 MHz", "FMax must be an integer MHz");
                return;
            }
            mhz = Math.Clamp(mhz, (int)RuntimeCpuTuning.FMaxMinimum, (int)RuntimeCpuTuning.FMaxMaximum);
            box.Text = mhz.ToString();
            try
            {
                CpuTuningResult result = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyFMax(mhz));
                StatusText.Text = result.Detail;
            }
            catch (Exception ex)
            {
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_5 + ex.Message;
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
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_6 + ex.Message;
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, WpfStrings.CpuTuningWindow_xaml_MessageBoxShowthis, "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_7 + ex.Message;
        }
    }

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_perCoreInputs.Count > 0)
            {
                int[] values = _perCoreInputs.Select(b => int.TryParse(b.Text, out int v) ? v : 0).ToArray();
                int allCore = RuntimeCpuTuning.LoadSavedAllCore() ?? values.FirstOrDefault();
                RuntimeCpuTuning.SaveSettings(values, allCore);
            }
            if (_fmaxBox != null && RuntimeCpuTuning.Instance.FMaxWritableCapability
                && int.TryParse(_fmaxBox.Text, out int fmaxValue))
            {
                RuntimeCpuTuning.SaveFMax(fmaxValue);
            }
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_8;
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_9 + ex.Message;
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
                StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_10;
                return;
            }
            int[] values = saved.Select((v, i) => v ?? 0).ToArray();
            CpuTuningResult co = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyPerCore(values));
            string detail = co.Detail;
            int? fmax = RuntimeCpuTuning.LoadSavedFMax();
            if (fmax.HasValue && RuntimeCpuTuning.Instance.FMaxWritableCapability)
            {
                CpuTuningResult fr = await Task.Run(() => RuntimeCpuTuning.Instance.ApplyFMax(fmax.Value));
                detail += WpfStrings.CpuFmaxPrefix + fr.Detail;
            }
            StatusText.Text = detail;
            LoadProbe();
        }
        catch (Exception ex)
        {
            StatusText.Text = WpfStrings.CpuTuningWindow_xaml_Text_11 + ex.Message;
        }
    }

    private void AutoApply_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = AutoApplyCheck.IsChecked == true;
        if (enabled && MessageBox.Show(this,
                WpfStrings.CpuAutoApplyConfirm,
                "L-Mechrevo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            AutoApplyCheck.IsChecked = false;
            return;
        }
        RuntimeCpuTuning.SetAutoApply(enabled);
        StatusText.Text = enabled ? WpfStrings.CpuAutoApplyOn : WpfStrings.CpuAutoApplyOff;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
