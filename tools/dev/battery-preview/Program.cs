using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string outPath = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "battery-panel.png");
        Assembly asm = typeof(MechrevoLite.Hardware.MechrevoHw).Assembly;
        Type t = asm.GetType("MechrevoLite.BatteryLimitPanel", throwOnError: true);
        Form host = new Form();
        host.Text = "battery preview";
        host.FormBorderStyle = FormBorderStyle.FixedSingle;
        host.StartPosition = FormStartPosition.Manual;
        host.Location = new Point(60, 60);
        host.BackColor = Color.FromArgb(32, 32, 34);
        host.ClientSize = new Size(380, 130);
        object panel = Activator.CreateInstance(t, nonPublic: true);
        Control c = (Control)panel;
        c.Width = 360;
        host.Controls.Add(c);
        host.Shown += delegate
        {
            host.PerformLayout();
            c.PerformLayout();
            Application.DoEvents();
            Thread.Sleep(300);
            Bitmap bmp = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved: " + outPath + " panel=" + c.Width + "x" + c.Height);
            host.Close();
        };
        Application.Run(host);
    }
}
