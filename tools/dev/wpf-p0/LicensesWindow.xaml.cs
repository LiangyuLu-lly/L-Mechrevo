using System.Diagnostics;
using System.IO;
using System.Windows;

namespace L_Mechrevo.WpfP0;

public partial class LicensesWindow : Window
{
    public LicensesWindow()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "LICENSES");
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(this, "程序目录下未找到 LICENSES 文件夹。", "L-Mechrevo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, "打开 LICENSES 文件夹失败：" + ex.Message, "L-Mechrevo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
