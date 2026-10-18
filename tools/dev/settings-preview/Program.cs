using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private static void DumpNames(Control root, int depth)
    {
        if (depth > 6) return;
        if (!string.IsNullOrEmpty(root.Name) && root.Name.ToLowerInvariant().Contains("batt"))
            Console.WriteLine("battery-ish: " + new string(' ', depth) + root.Name + " " + root.GetType().Name + " " + root.Bounds + " visible=" + root.Visible);
        foreach (Control c in root.Controls) DumpNames(c, depth + 1);
    }

    private static Control FindByName(Control root, string name)
    {
        if (root.Name == name) return root;
        foreach (Control c in root.Controls)
        {
            Control f = FindByName(c, name);
            if (f != null) return f;
        }
        return null;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string outPath = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "settings.png");
        Assembly asm = typeof(MechrevoLite.Hardware.MechrevoHw).Assembly;
        Type t = asm.GetType("MechrevoLite.SettingsForm", throwOnError: true);
        Form f;
        try
        {
            f = (Form)Activator.CreateInstance(t);
        }
        catch (Exception ex)
        {
            Console.WriteLine("create failed: " + ex.GetType().Name + ": " + ex.Message);
            return;
        }
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.Show();
        f.Shown += delegate
        {
            // 把真实的宝龙达限充面板注入「电池」卡片，模拟宝龙达机型的整窗观感
            try
            {
                DumpNames(f, 0);
                Control battery = FindByName(f, "panelBattery");
                Console.WriteLine("panelBattery=" + (battery == null ? "null" : battery.Name + " " + battery.Bounds));
                Type pt = asm.GetType("MechrevoLite.BatteryLimitPanel", throwOnError: true);
                object panel = Activator.CreateInstance(pt, nonPublic: true);
                Control pc = (Control)panel;
                pc.Width = Math.Max(300, (battery ?? f).Width - 24);
                if (battery != null)
                {
                    foreach (Control child in battery.Controls) child.Visible = false;
                    pc.Dock = DockStyle.Top;
                    battery.Controls.Add(pc);
                    pc.BringToFront();
                    pc.PerformLayout();
                }
                else
                {
                    f.Controls.Add(pc);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("inject failed: " + ex.Message);
            }
        };
        f.Shown += delegate
        {
            Application.DoEvents();
            Thread.Sleep(1500);
            Application.DoEvents();
            Bitmap bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height));
            f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved: " + outPath + " " + f.Width + "x" + f.Height);
            f.Close();
            Application.Exit();
        };
        Application.Run();
    }
}
