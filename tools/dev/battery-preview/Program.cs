using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private static Control FindByText(Control root, string text)
    {
        if (root.Text == text) return root;
        foreach (Control c in root.Controls)
        {
            Control f = FindByText(c, text);
            if (f != null) return f;
        }
        return null;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        // ⭐ 2026-10-19（机主要求）：支持 --scale 1.25 / 1.5 模拟 DPI 缩放后布局 ✅
        double scale = 1.0;
        System.Collections.Generic.List<string> positional = new System.Collections.Generic.List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
            }
            else
            {
                positional.Add(args[i]);
            }
        }
        if (scale <= 0) scale = 1.0;
        int width = positional.Count > 1 ? int.Parse(positional[1]) : 368;
        int btnW = positional.Count > 2 ? int.Parse(positional[2]) : 0;
        int btnH = positional.Count > 3 ? int.Parse(positional[3]) : 0;
        string outPath = positional.Count > 0 ? positional[0] : Path.Combine(Environment.CurrentDirectory, "battery-card.png");

        Assembly asm = typeof(MechrevoLite.Hardware.MechrevoHw).Assembly;
        Type t = asm.GetType("MechrevoLite.BatteryLimitPanel", throwOnError: true);
        object panel = Activator.CreateInstance(t, nonPublic: true);
        Control c = (Control)panel;
        c.Width = width - 12;

        // —— 模拟真实卡片：标题行（电池 / 未限制充电）+ 面板 ——
        Panel card = new Panel();
        card.BackColor = Color.FromArgb(24, 24, 26);
        card.Padding = new Padding(6);
        Label title = new Label();
        title.Text = "电池";
        title.AutoSize = false;
        title.Height = 26;
        title.Dock = DockStyle.Top;
        title.ForeColor = Color.FromArgb(225, 225, 228);
        title.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Label status = new Label();
        status.Text = "未限制充电";
        status.AutoSize = true;
        status.ForeColor = Color.FromArgb(150, 150, 155);
        status.Font = new Font("Microsoft YaHei UI", 8.5f);
        status.Location = new Point(width - 100, 6);
        card.Controls.Add(status);
        card.Controls.Add(c);
        card.Controls.Add(title);
        card.Dock = DockStyle.Top;
        status.BringToFront();

        Form host = new Form();
        host.Text = "battery card preview";
        host.FormBorderStyle = FormBorderStyle.FixedSingle;
        host.StartPosition = FormStartPosition.Manual;
        host.Location = new Point(60, 60);
        host.BackColor = Color.FromArgb(32, 32, 34);
        host.ClientSize = new Size(width, 132);
        host.Controls.Add(card);
        if (scale != 1.0)
        {
            // ⭐ 逻辑值乘一次后整体缩放（模拟 125/150% DPI 布局）✅
            host.Scale(new SizeF((float)scale, (float)scale));
        }

        host.Shown += delegate
        {
            host.PerformLayout();
            c.PerformLayout();
            if (btnW > 0 && btnH > 0)
            {
                Control btn = FindByText(c, "保存阈值");
                if (btn != null)
                {
                    btn.Size = new Size(btnW, btnH);
                    btn.PerformLayout();
                }
            }
            card.Height = title.Height + c.Height + 12;
            host.ClientSize = new Size(width, card.Height + 16);
            host.PerformLayout();
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            Bitmap bmp = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved: " + outPath + " card=" + card.Width + "x" + card.Height + " panel=" + c.Width + "x" + c.Height);
            host.Close();
        };
        Application.Run(host);
    }
}
