using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
        {
            Console.WriteLine("thread-ex: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("domain-ex: " + e.ExceptionObject);
        };
        string outPath = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "settings.png");
        bool injectBattery = true;
        foreach (string a in args)
            if (a == "--no-inject") injectBattery = false;

        Assembly asm = typeof(MechrevoLite.Hardware.MechrevoHw).Assembly;
        Type t = asm.GetType("MechrevoLite.SettingsForm", throwOnError: true);
        System.Diagnostics.Stopwatch ctorWatch = System.Diagnostics.Stopwatch.StartNew();
        Form f;
        try
        {
            f = (Form)Activator.CreateInstance(t);
        }
        catch (Exception ex)
        {
            Console.WriteLine("create failed: " + ex.GetType().Name + ": " + ex.Message + " INNER=" + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message + " STACK=" + ex.InnerException?.StackTrace);
            return;
        }
        Console.WriteLine("ctorMs=" + ctorWatch.ElapsedMilliseconds);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.Show();

        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        timer.Interval = 2500;
        timer.Tick += delegate
        {
            timer.Stop();
            try
            {
                if (injectBattery)
                {
                    Control battery = FindByName(f, "panelBattery");
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
                    }
                    else
                    {
                        f.Controls.Add(pc);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("inject failed: " + ex.Message);
            }
            Application.DoEvents();
            try
            {
                f.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height));
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("saved: " + outPath + " " + f.Width + "x" + f.Height);
            }
            catch (Exception ex)
            {
                Console.WriteLine("screenshot failed: " + ex.Message);
            }
            f.Close();
            Application.Exit();
            Environment.Exit(0);
        };
        timer.Start();
        Application.Run();
    }
}
