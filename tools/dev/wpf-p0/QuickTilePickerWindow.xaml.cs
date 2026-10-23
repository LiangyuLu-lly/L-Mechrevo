using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace L_Mechrevo.WpfP0;

public sealed class QuickChoice
{
    public QuickChoice(string key, string label, string glyph, bool isSelected = false)
    {
        Key = key;
        Label = label;
        Glyph = glyph;
        IsSelected = isSelected;
    }

    public string Key { get; }

    public string Label { get; }

    public string Glyph { get; }

    public bool IsSelected { get; set; }
}

public partial class QuickTilePickerWindow : Window
{
    private readonly List<QuickChoice> _choices;
    private readonly int _maxSlots;

    public IReadOnlyList<QuickChoice> SelectedChoices =>
        _choices.Where(static c => c.IsSelected).ToList();

    public QuickTilePickerWindow(IEnumerable<QuickChoice> choices, int maxSlots = 5)
    {
        InitializeComponent();
        _maxSlots = Math.Max(1, maxSlots);
        _choices = choices.ToList();
        ChoicesList.ItemsSource = _choices;
        HintText.Text = "最多 " + _maxSlots + " 个（当前 " + SelectedChoices.Count + " 个）";
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedChoices.Count > _maxSlots && sender is System.Windows.Controls.CheckBox box)
        {
            box.IsChecked = false;
            HintText.Text = "最多只能选 " + _maxSlots + " 个";
            return;
        }
        HintText.Text = "最多 " + _maxSlots + " 个（当前 " + SelectedChoices.Count + " 个）";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedChoices.Count > _maxSlots)
        {
            HintText.Text = "最多只能选 " + _maxSlots + " 个";
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplyDarkTitleBar()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        int enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
