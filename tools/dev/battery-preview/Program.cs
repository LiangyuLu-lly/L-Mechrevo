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
        string outPath = Path.Combine(Environment.CurrentDirectory, "battery-card.png");
        int width = 368;
        double scale = 1.0;
        if (args.Length > 0) outPath = args[0];
        foreach (string a in args)
        {
            if (a.StartsWith("--w=", StringComparison.OrdinalIgnoreCase))
                width = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--scale=", StringComparison.OrdinalIgnoreCase))
                scale = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
        }

        Assembly asm = typeof(MechrevoLite.Hardware.MechrevoHw).Assembly;
        Type t = asm.GetType("MechrevoLite.BatteryLimitPanel", throwOnError: true);
        object panel = Activator.CreateInstance(t, nonPublic: true);
        Control c = (Control)panel;
        c.Width = width - 12;

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
        host.Text = "battery card preview (scale=" + scale.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        host.FormBorderStyle = FormBorderStyle.FixedSingle;
        host.StartPosition = FormStartPosition.Manual;
        host.Location = new Point(60, 60);
        host.BackColor = Color.FromArgb(32, 32, 34);
        host.ClientSize = new Size(width, 132);
        // —— 窗口上下文（模拟：上方「屏幕」卡片 + 下方「灯光」卡片，便于判断整窗观感）——
        Panel screenCard = new Panel();
        screenCard.BackColor = Color.FromArgb(24, 24, 26);
        screenCard.Height = 74;
        screenCard.Dock = DockStyle.Top;
        Label screenTitle = new Label();
        screenTitle.Text = "屏幕";
        screenTitle.AutoSize = false;
        screenTitle.Height = 24;
        screenTitle.Dock = DockStyle.Top;
        screenTitle.ForeColor = Color.FromArgb(225, 225, 228);
        screenTitle.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Label screenValue = new Label();
        screenValue.Text = "260 · 亮度 83%";
        screenValue.AutoSize = true;
        screenValue.ForeColor = Color.FromArgb(160, 160, 165);
        screenValue.Font = new Font("Microsoft YaHei UI", 8.5f);
        screenValue.Location = new Point(12, 40);
        screenCard.Controls.Add(screenValue);
        screenCard.Controls.Add(screenTitle);

        Panel lightCard = new Panel();
        lightCard.BackColor = Color.FromArgb(24, 24, 26);
        lightCard.Height = 96;
        lightCard.Dock = DockStyle.Top;
        Label lightTitle = new Label();
        lightTitle.Text = "灯光";
        lightTitle.AutoSize = false;
        lightTitle.Height = 24;
        lightTitle.Dock = DockStyle.Top;
        lightTitle.ForeColor = Color.FromArgb(225, 225, 228);
        lightTitle.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Label lightValue = new Label();
        lightValue.Text = "键盘：单色 · Logo 灯：开";
        lightValue.AutoSize = true;
        lightValue.ForeColor = Color.FromArgb(160, 160, 165);
        lightValue.Font = new Font("Microsoft YaHei UI", 8.5f);
        lightValue.Location = new Point(12, 40);
        lightCard.Controls.Add(lightValue);
        lightCard.Controls.Add(lightTitle);

        Panel stack = new Panel();
        stack.Dock = DockStyle.Fill;
        stack.Controls.Add(card);
        stack.Controls.Add(lightCard);
        stack.Controls.Add(screenCard);
        // Dock=Top 叠放顺序：后加入的更靠上
        host.Controls.Add(stack);
        stack.BringToFront();
        host.AutoScaleMode = AutoScaleMode.None;

        host.Shown += delegate
        {
            host.PerformLayout();
            c.PerformLayout();
            card.Height = title.Height + c.Height + 12;
            stack.Height = 74 + 96 + card.Height + 40;
            host.ClientSize = new Size(width, stack.Height + 24);
            if (scale > 1.0)
                host.Scale(new SizeF((float)scale, (float)scale));
            host.PerformLayout();
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            Bitmap bmp = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved: " + outPath + " scale=" + scale + " card=" + card.Width + "x" + card.Height + " panel=" + c.Width + "x" + c.Height);
            host.Close();
        };
        Application.Run(host);
    }
}
