using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

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

    public bool IsFanBoost => string.Equals(Key, "fanboost", StringComparison.OrdinalIgnoreCase);
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
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFontFamily"];
        _maxSlots = Math.Max(1, maxSlots);
        List<QuickChoice> ordered = choices
            .OrderByDescending(static c => c.IsSelected)
            .ToList();
        _choices = new ObservableCollection<QuickChoice>(ordered);
        ChoicesList.ItemsSource = _choices;
        HintText.Text = "最多 " + _maxSlots + " 个（当前 " + SelectedChoices.Count + " 个）";
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        ChoicesList.PreviewMouseLeftButtonDown += ChoicesList_PreviewMouseLeftButtonDown;
        ChoicesList.PreviewMouseMove += ChoicesList_PreviewMouseMove;
        ChoicesList.DragOver += (_, e) =>
        {
            _ghost?.SetPosition(e.GetPosition(ChoicesList));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        ChoicesList.Drop += ChoicesList_Drop;
    }

    private CheckBox? _dragBox;
    private Point _dragStart;
    private AdornerLayer? _ghostLayer;
    private DragGhostAdorner? _ghost;

    private void ChoicesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragBox = FindAncestor<CheckBox>(e.OriginalSource as DependencyObject);
        if (_dragBox?.DataContext is not QuickChoice choice || !choice.IsSelected)
        {
            _dragBox = null;
            return;
        }
        _dragStart = e.GetPosition(ChoicesList);
    }

    private void ChoicesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBox == null || _dragBox.DataContext is not QuickChoice || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        Point now = e.GetPosition(ChoicesList);
        if (Math.Abs(now.X - _dragStart.X) < 6 && Math.Abs(now.Y - _dragStart.Y) < 6)
        {
            return;
        }
        CheckBox source = _dragBox;
        _dragBox = null;
        _ghostLayer = AdornerLayer.GetAdornerLayer(ChoicesList);
        _ghost = new DragGhostAdorner(ChoicesList, source);
        _ghostLayer?.Add(_ghost);
        _ghost.SetPosition(Mouse.GetPosition(ChoicesList));
        try
        {
            DragDrop.DoDragDrop(source, source, DragDropEffects.Move);
        }
        finally
        {
            _ghostLayer?.Remove(_ghost);
            _ghost = null;
            _ghostLayer = null;
        }
    }

    private void ChoicesList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CheckBox)) is not CheckBox sourceBox
            || sourceBox.DataContext is not QuickChoice source
            || !source.IsSelected)
        {
            return;
        }
        CheckBox? targetBox = FindAncestor<CheckBox>(e.OriginalSource as DependencyObject);
        if (targetBox?.DataContext is not QuickChoice target || !target.IsSelected || target == source)
        {
            return;
        }
        int from = _choices.IndexOf(source);
        int to = _choices.IndexOf(target);
        if (from < 0 || to < 0)
        {
            return;
        }
        _choices.Move(from, to);
        HintText.Text = "最多 " + _maxSlots + " 个（当前 " + SelectedChoices.Count + " 个）";
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T typed)
            {
                return typed;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
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
