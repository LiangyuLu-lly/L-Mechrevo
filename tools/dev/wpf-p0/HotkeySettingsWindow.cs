using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MechrevoLite;

namespace L_Mechrevo.WpfP0;

/// <summary>全局热键配置窗口。</summary>
internal sealed class HotkeySettingsWindow : Window
{
    private readonly CheckBox _enabled;
    private readonly TextBlock _status;
    private readonly Dictionary<int, TextBlock> _valueTexts = new();
    private readonly Dictionary<int, Button> _recordButtons = new();
    private int _capturingId;

    internal HotkeySettingsWindow()
    {
        Title = "全局热键";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["WindowBgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        FontFamily = (FontFamily)Application.Current.Resources["AppFontFamily"];

        Grid root = new() { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock { Text = "全局热键", FontSize = 18, FontWeight = FontWeights.Bold });
        _enabled = new CheckBox
        {
            Content = "启用全局热键",
            IsChecked = AppConfig.Get("hotkey_enabled", 1) != 0,
            Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Application.Current.Resources["HeaderSwitch"]
        };
        _enabled.Click += (_, _) =>
        {
            AppConfig.Set("hotkey_enabled", _enabled.IsChecked == true ? 1 : 0);
            AppConfig.Flush();
            HotkeyService.Current?.Reload();
            _status.Text = _enabled.IsChecked == true ? "全局热键已启用" : "全局热键已停用";
        };
        header.Children.Add(_enabled);
        _status = new TextBlock
        {
            Text = "点“录制”后按下组合键；Esc 取消。",
            Style = (Style)Application.Current.Resources["MutedText"],
            Margin = new Thickness(0, 6, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        header.Children.Add(_status);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        StackPanel list = new();
        foreach (HotkeyService.HotkeyAction action in HotkeyService.Actions)
        {
            Grid row = new() { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center });
            TextBlock value = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            (uint mods, uint key) = HotkeyService.GetGesture(action);
            value.Text = HotkeyService.Describe(mods, key);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            _valueTexts[action.Id] = value;
            Button record = new()
            {
                Content = "录制",
                Style = (Style)Application.Current.Resources["FlatButton"],
                Tag = action.Id
            };
            record.Click += (_, _) => BeginCapture(action.Id);
            Grid.SetColumn(record, 2);
            row.Children.Add(record);
            _recordButtons[action.Id] = record;
            list.Children.Add(row);
        }
        ScrollViewer scroll = new() { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        Button reset = new() { Content = "恢复默认", Style = (Style)Application.Current.Resources["FlatButton"], Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) =>
        {
            HotkeyService.ResetGestures();
            RefreshValues();
            _status.Text = "已恢复默认热键";
        };
        buttons.Children.Add(reset);
        Button close = new() { Content = "关闭", Style = (Style)Application.Current.Resources["FlatButton"] };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void BeginCapture(int id)
    {
        _capturingId = id;
        _status.Text = "请按下新的组合键，Esc 取消";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingId == 0)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            _capturingId = 0;
            _status.Text = "已取消";
            e.Handled = true;
            return;
        }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }
        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= HotkeyService.ModControl;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= HotkeyService.ModAlt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= HotkeyService.ModShift;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= HotkeyService.ModWin;
        if (modifiers == 0)
        {
            _status.Text = "热键至少需要 Ctrl / Alt / Shift / Win 之一";
            e.Handled = true;
            return;
        }
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        foreach (HotkeyService.HotkeyAction other in HotkeyService.Actions)
        {
            if (other.Id == _capturingId)
            {
                continue;
            }
            (uint otherMods, uint otherKey) = HotkeyService.GetGesture(other);
            if (otherMods == modifiers && otherKey == vk)
            {
                _status.Text = "该组合键已被「" + other.Label + "」占用";
                e.Handled = true;
                return;
            }
        }
        HotkeyService.HotkeyAction action = Array.Find(HotkeyService.Actions, a => a.Id == _capturingId);
        HotkeyService.SetGesture(action, modifiers, vk);
        RefreshValues();
        _status.Text = "已设置：" + action.Label + " = " + HotkeyService.Describe(modifiers, vk);
        _capturingId = 0;
        e.Handled = true;
    }

    private void RefreshValues()
    {
        foreach (HotkeyService.HotkeyAction action in HotkeyService.Actions)
        {
            (uint mods, uint key) = HotkeyService.GetGesture(action);
            if (_valueTexts.TryGetValue(action.Id, out TextBlock? text))
            {
                text.Text = HotkeyService.Describe(mods, key);
            }
        }
    }
}
