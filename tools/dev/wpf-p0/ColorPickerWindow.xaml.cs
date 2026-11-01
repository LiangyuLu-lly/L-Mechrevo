using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0;

public partial class ColorPickerWindow : Window
{
    public System.Drawing.Color? Result { get; private set; }

    public int? PresetIndex { get; private set; }

    public ColorPickerWindow(System.Drawing.Color initial)
    {
        InitializeComponent();
        System.Drawing.Color[] palette = new System.Drawing.Color[LightChannelSettings.ManualPalette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = System.Drawing.Color.FromArgb(LightChannelSettings.ManualPalette[i]);
            int index = i;
            Button button = new()
            {
                Height = 28,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    palette[i].A, palette[i].R, palette[i].G, palette[i].B)),
                ToolTip = $"#{palette[i].R:X2}{palette[i].G:X2}{palette[i].B:X2}"
            };
            button.Click += (_, _) =>
            {
                Result = palette[index];
                PresetIndex = index + 1;
                DialogResult = true;
            };
            PresetGrid.Children.Add(button);
        }
        StatusText.Text = WpfStrings.L("当前 ", "Current ") + $"#{initial.R:X2}{initial.G:X2}{initial.B:X2}";
    }

    private void Custom_Click(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.ColorDialog dialog = new()
        {
            FullOpen = true,
            Color = Result ?? System.Drawing.Color.White
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            Result = dialog.Color;
            PresetIndex = null;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
