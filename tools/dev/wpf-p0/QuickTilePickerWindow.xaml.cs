using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace L_Mechrevo.WpfP0;

public sealed record QuickChoice(string Key, string Label, string Glyph);

public partial class QuickTilePickerWindow : Window
{
    public QuickChoice? SelectedChoice { get; private set; }

    public QuickTilePickerWindow(IEnumerable<QuickChoice> choices)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        List<QuickChoice> items = choices.ToList();
        ChoicesList.ItemsSource = items;
        if (items.Count > 0)
        {
            ChoicesList.SelectedIndex = 0;
        }
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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = ChoicesList.SelectedItem as QuickChoice;
        DialogResult = SelectedChoice != null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
