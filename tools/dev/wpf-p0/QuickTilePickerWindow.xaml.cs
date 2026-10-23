using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<QuickChoice> _choices;
    private readonly int _maxSlots;

    public IReadOnlyList<QuickChoice> SelectedChoices =>
        _choices.Where(static c => c.IsSelected).ToList();

    public QuickTilePickerWindow(IEnumerable<QuickChoice> choices, int maxSlots = 5)
    {
        InitializeComponent();
        _maxSlots = Math.Max(1, maxSlots);
        _choices = new ObservableCollection<QuickChoice>(choices);
        ChoicesList.ItemsSource = _choices;
        HintText.Text = "最多 " + _maxSlots + " 个（当前 " + SelectedChoices.Count + " 个）";
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox box || box.DataContext is not QuickChoice choice)
        {
            return;
        }
        choice.IsSelected = box.IsChecked == true;
        if (SelectedChoices.Count > _maxSlots)
        {
            choice.IsSelected = false;
            box.IsChecked = false;
            HintText.Text = "最多只能选 " + _maxSlots + " 个";
            return;
        }
        int oldIndex = _choices.IndexOf(choice);
        if (oldIndex >= 0)
        {
            _choices.RemoveAt(oldIndex);
            int selectedCount = 0;
            foreach (QuickChoice item in _choices)
            {
                if (item.IsSelected)
                {
                    selectedCount++;
                }
            }
            _choices.Insert(Math.Min(selectedCount, _choices.Count), choice);
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
