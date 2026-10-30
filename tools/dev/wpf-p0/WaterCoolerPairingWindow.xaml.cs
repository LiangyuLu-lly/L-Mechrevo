using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using L_Mechrevo.WpfP0.Services;

namespace L_Mechrevo.WpfP0;

public partial class WaterCoolerPairingWindow : Window
{
    private readonly WaterCoolerRoutingService? _routing;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private string _lastSignature = "";
    private bool _busy;

    internal WaterCoolerPairingWindow(WaterCoolerRoutingService? routing)
    {
        InitializeComponent();
        _routing = routing;
        Loaded += (_, _) =>
        {
            StartScan();
            RefreshList(force: true);
            _poll.Tick += (_, _) => RefreshList(force: false);
            _poll.Start();
        };
        Closed += (_, _) =>
        {
            _poll.Stop();
            _routing?.StopPairingScan();
        };
    }

    private void StartScan()
    {
        if (_routing == null || !_routing.HasBleBackend)
        {
            BoundStatus.Text = "当前后端没有 BLE 水冷接口";
            return;
        }
        _routing.ClearDevices();
        _routing.StartPairingScan();
        StatusText.Text = "正在扫描周围 BLE 水冷...";
        _lastSignature = "";
    }

    private void RefreshList(bool force)
    {
        if (_routing == null)
        {
            return;
        }
        ulong bound = _routing.BoundAddress;
        BoundStatus.Text = bound != 0
            ? $"已绑定：{_routing.BoundDeviceName}  ·  MAC {bound:X12}  ·  只允许该设备接入"
            : "尚未绑定：首次连接成功后会自动绑定当前水冷";
        IReadOnlyList<(string Name, ulong Address, short Rssi)> devices = _routing.DevicesSnapshot();
        List<(string Name, ulong Address, short Rssi)> coolers = devices
            .Where(d => IsCoolerName(d.Name))
            .OrderByDescending(d => d.Address == bound)
            .ThenByDescending(d => d.Rssi)
            .ToList();
        string signature = string.Join("|", coolers.Select(d => d.Address.ToString("X12") + ":" + d.Name + ":" + d.Rssi));
        if (!force && signature == _lastSignature)
        {
            return;
        }
        _lastSignature = signature;
        DeviceList.Children.Clear();
        if (coolers.Count == 0)
        {
            DeviceList.Children.Add(new TextBlock
            {
                Text = "未发现水冷广播。请确认水冷魔盒已通电，并靠近电脑。",
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            StatusText.Text = "扫描中...";
            return;
        }
        foreach ((string name, ulong address, short rssi) in coolers)
        {
            DeviceList.Children.Add(BuildRow(name, address, rssi, address == bound));
        }
        StatusText.Text = $"发现水冷设备：{coolers.Count} 台";
    }

    private static bool IsCoolerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        return name.Contains("LCT", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Oasis", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AIO", StringComparison.OrdinalIgnoreCase)
            || name.Contains("水冷", StringComparison.Ordinal);
    }

    private Border BuildRow(string name, ulong address, short rssi, bool bound)
    {
        Border row = new()
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Background = bound
                ? new SolidColorBrush(Color.FromArgb(0x33, 0x22, 0xC5, 0x5E))
                : (Brush)FindResource("ControlBrush"),
            BorderBrush = bound
                ? new SolidColorBrush(Color.FromArgb(0x88, 0x22, 0xC5, 0x5E))
                : (Brush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1)
        };
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel info = new();
        info.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        info.Children.Add(new TextBlock
        {
            Text = $"MAC {address:X12}   RSSI {rssi} dBm",
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        grid.Children.Add(info);
        Button bind = new()
        {
            Content = bound ? "当前锁定" : "绑定并连接",
            Style = (Style)FindResource("FlatButton"),
            IsEnabled = !bound,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        bind.Click += async (_, _) => await BindAsync(name, address);
        Grid.SetColumn(bind, 1);
        grid.Children.Add(bind);
        row.Child = grid;
        return row;
    }

    private async System.Threading.Tasks.Task BindAsync(string name, ulong address)
    {
        if (_routing == null || _busy)
        {
            return;
        }
        _busy = true;
        try
        {
            StatusText.Text = "正在绑定并连接 " + name + "...";
            bool ok = await _routing.BindDeviceAsync(address, name);
            StatusText.Text = ok ? "已绑定并连接：" + name : "绑定失败：请确认设备通电且在附近";
            _lastSignature = "";
            RefreshList(force: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "绑定失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        StartScan();
        await System.Threading.Tasks.Task.Delay(300);
        RefreshList(force: true);
    }

    private void Unbind_Click(object sender, RoutedEventArgs e)
    {
        _routing?.UnbindDevice();
        StatusText.Text = "已解除绑定，重新连接后会允许选择其它水冷";
        _lastSignature = "";
        RefreshList(force: true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
