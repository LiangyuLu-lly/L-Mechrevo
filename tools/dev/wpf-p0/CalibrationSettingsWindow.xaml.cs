using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MechrevoLite;
using MechrevoLite.Display;
using Microsoft.Win32;

namespace L_Mechrevo.WpfP0;

public partial class CalibrationSettingsWindow : Window
{
    private bool _syncing;

    public CalibrationSettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        LoadState();
    }

    private void LoadState()
    {
        _syncing = true;
        try
        {
            StartupSwitch.IsChecked = AppConfig.Is("calib_apply_on_startup");
            CustomSwitch.IsChecked = AppConfig.Is("calib_custom_on");
            RefreshIccList();
            bool customOn = CustomSwitch.IsChecked == true;
            IccCombo.IsEnabled = customOn;
            ImportButton.IsEnabled = customOn;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshIccList()
    {
        IccCombo.Items.Clear();
        foreach (string name in IccHelper.ListCustomIccFiles())
        {
            IccCombo.Items.Add(name);
        }
        string saved = AppConfig.GetString("calib_custom_icc") ?? "";
        if (!string.IsNullOrEmpty(saved))
        {
            int index = IccCombo.Items.IndexOf(saved);
            if (index >= 0)
            {
                IccCombo.SelectedIndex = index;
            }
        }
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        AppConfig.Set("calib_apply_on_startup", StartupSwitch.IsChecked == true ? 1 : 0);
        AppConfig.Flush();
    }

    private void CustomSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        bool on = CustomSwitch.IsChecked == true;
        AppConfig.Set("calib_custom_on", on ? 1 : 0);
        AppConfig.Flush();
        IccCombo.IsEnabled = on;
        ImportButton.IsEnabled = on;
        if (!on)
        {
            _ = System.Threading.Tasks.Task.Run(() => IccHelper.ClearAllIcc());
            if (AppConfig.Get("calib_mode") == 999)
            {
                AppConfig.Set("calib_mode", 1);
                AppConfig.Flush();
            }
        }
    }

    private void IccCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncing || IccCombo.SelectedItem is not string name)
        {
            return;
        }
        AppConfig.Set("calib_custom_icc", name);
        AppConfig.Flush();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Filter = "ICC 配置文件|*.icc;*.icm|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        string? imported = IccHelper.ImportIcc(dialog.FileName);
        if (string.IsNullOrWhiteSpace(imported))
        {
            MessageBox.Show(this, "ICC 导入失败。", "屏幕校色", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        AppConfig.Set("calib_custom_icc", imported);
        AppConfig.Flush();
        _syncing = true;
        try
        {
            RefreshIccList();
            int index = IccCombo.Items.IndexOf(imported);
            if (index >= 0)
            {
                IccCombo.SelectedIndex = index;
            }
        }
        finally
        {
            _syncing = false;
        }
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
