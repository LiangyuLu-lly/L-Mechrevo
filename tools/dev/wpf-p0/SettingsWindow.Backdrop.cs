using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using MechrevoLite;
using L_Mechrevo.WpfP0.Services;

namespace L_Mechrevo.WpfP0;

public partial class SettingsWindow
{
    private bool _syncingTint = true;
    private bool _micaTintHookInstalled;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) =>
        {
            SyncBackdropSwitch();
            HookMicaTintFlush();
        };
    }

    private void SyncBackdropSwitch()
    {
        try
        {
            bool mica = WindowBackdrop.Mode == BackdropMode.Mica;
            MicaBackdropCheck.IsChecked = mica;
            int tint = WindowBackdrop.Tint;
            _syncingTint = true;
            MicaTintSlider.Value = tint;
            MicaTintText.Text = tint + "%";
            _syncingTint = false;
            MicaTintSlider.IsEnabled = mica;
            MicaTintSlider.ToolTip = mica ? null : "先开启云母材质背景，强度才会生效";
        }
        catch
        {
        }
    }

    private void MicaBackdrop_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        bool mica = MicaBackdropCheck.IsChecked == true;
        AppConfig.Set(WindowBackdrop.ConfigKey, mica ? "mica" : "solid");
        AppConfig.Flush();
        bool active = ApplyBackdropToOpenWindows();
        try
        {
            MicaTintSlider.IsEnabled = mica;
            MicaTintSlider.ToolTip = mica ? null : "先开启云母材质背景，强度才会生效";
        }
        catch
        {
        }
        if (mica && !active)
        {
            Status("云母材质背景：当前系统/窗口不支持（需 Win11 22H2+），已回退纯色");
            return;
        }
        Status(mica ? "云母材质背景：已开启" : "云母材质背景：已关闭");
    }

    private void MicaTint_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingTint || MicaTintText == null)
        {
            return;
        }
        int tint = (int)Math.Round(e.NewValue);
        MicaTintText.Text = tint + "%";
        if (_syncing)
        {
            return;
        }
        AppConfig.Set(WindowBackdrop.TintConfigKey, tint);
        if (WindowBackdrop.Mode == BackdropMode.Mica)
        {
            WindowBackdrop.RefreshTintForOpenWindows();
        }
    }

    private void MicaTint_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        try
        {
            AppConfig.Flush();
            int tint = (int)Math.Round(MicaTintSlider.Value);
            Status("云母强度：" + tint + "%" + (WindowBackdrop.Mode == BackdropMode.Mica ? "" : "（云母未开启，暂不生效）"));
        }
        catch
        {
        }
    }

    private void HookMicaTintFlush()
    {
        if (_micaTintHookInstalled || MicaTintSlider == null)
        {
            return;
        }
        _micaTintHookInstalled = true;
        try
        {
            MicaTintSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(MicaTint_DragCompleted));
        }
        catch
        {
        }
    }

    internal static bool ApplyBackdropToOpenWindows()
    {
        bool active = false;
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                try
                {
                    WindowBackdrop.Apply(window);
                    if (WindowBackdrop.IsActive(window))
                    {
                        active = true;
                    }
                    switch (window)
                    {
                        case MainWindow main:
                            main.ReloadWideBackground();
                            break;
                        case OriginalStyleWindow original:
                            original.ApplyBackdropBackgroundState();
                            break;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return active;
    }
}
