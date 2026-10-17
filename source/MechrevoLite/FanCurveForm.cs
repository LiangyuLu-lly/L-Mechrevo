using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.Drawing.Drawing2D;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.Hardware;
using MechrevoLite.Properties;using MechrevoLite.UI;
using MechrevoLite.Properties;
using MechrevoLite.Mode;
namespace MechrevoLite;

public class FanCurveForm : RForm
{
	private sealed class CurvePanel : Panel
	{
		internal const int MinLogicalWidth = 340;

		internal const int MinLogicalHeight = 170;

		private static readonly Font SmallFont = UiVisualStyle.Font(9.5f);

		private static readonly Font TitleFont = UiVisualStyle.Font(12f, FontStyle.Bold);

		private readonly string _label;

		private readonly Color _color;

		private readonly int[] _duties = new int[16];

		private readonly byte[] _temps = new byte[16];

		private int _valid = 16;

		private int _dragIdx = -1;

		private readonly Action _changed;

		public int[] Duties => _duties;

		public CurvePanel(string label, Color color, Action changed)
		{
			_label = label;
			_color = color;
			_changed = changed;
			BackColor = UiVisualStyle.Input;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			for (int i = 0; i < 16; i++)
			{
				_duties[i] = 0;
			}
		}

		public void Load(byte[] temps, byte[] duties)
		{
			int num = 16;
			for (int i = 0; i < 16; i++)
			{
				_temps[i] = (byte)((i < temps.Length) ? temps[i] : 0);
				int num2 = ((i < duties.Length) ? duties[i] : 0);
				_duties[i] = ((num2 != 255) ? Math.Clamp(num2, 0, 100) : 0);
				if (i > 0 && num == 16 && _temps[i] == byte.MaxValue)
				{
					num = i;
				}
			}
			_valid = ((num == 16) ? 16 : Math.Max(num, 1));
			Invalidate();
		}

		private (int left, int w, int h, int top, int bottom) Geo()
		{
			float num = UiDpi.Paint(this) / 96f;
			int num2 = (int)Math.Round(44f * num);
			int num3 = (int)Math.Round(12f * num);
			int num4 = (int)Math.Round(26f * num);
			int num5 = (int)Math.Round(22f * num);
			return (left: num2, w: Width - num2 - num3, h: Height - num4 - num5, top: num4, bottom: num4 + Height - num4 - num5);
		}

		private int X(int i, int left, int w)
		{
			return left + ((i != 0) ? (w * i / Math.Max(1, _valid - 1)) : 0);
		}

		private int Y(int duty, int h, int bottom)
		{
			return bottom - h * duty / 100;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using SolidBrush brush = new SolidBrush(UiVisualStyle.Muted);
			var (num, num2, h, y, num3) = Geo();
			// 2026-10-09（P0.5）：这里两处硬编码 `new Pen(color, 1f)` 收敛到唯一工厂
			// UiVisualStyle.OutlinePen —— 按本控件自己的 DPI（Control.DeviceDpi）+ 整数设备像素；
			// 100% 下仍是 1 设备像素（与改动前逐像素相同），150%/200% 下变成 2（不再"只有这张图上不变粗"）。
			// ⚠️ 只换笔的构造方式，Dashed/Dot 样式、线色、坐标、绘制顺序一字未动。
			using Pen pen = UiVisualStyle.OutlinePen(this, UiVisualStyle.Track, 1f, 1f, inset: false);
			pen.DashStyle = DashStyle.Dot;
			using Pen pen2 = UiVisualStyle.OutlinePen(this, UiVisualStyle.Border, 1f, 1f, inset: false);
			for (int i = 0; i <= 100; i += 25)
			{
				int num4 = Y(i, h, num3);
				graphics.DrawLine((i == 0 || i == 100) ? pen2 : pen, num, num4, num + num2, num4);
				SizeF sizeF = graphics.MeasureString(i + "%", SmallFont);
				graphics.DrawString(i + "%", SmallFont, brush, (float)num - sizeF.Width - 8f, (float)num4 - sizeF.Height / 2f);
			}
			for (int j = 0; j < _valid; j++)
			{
				int num5 = X(j, num, num2);
				graphics.DrawLine(pen, num5, y, num5, num3);
				if (j < _temps.Length && _temps[j] != byte.MaxValue && _temps[j] != 0)
				{
					SizeF sizeF2 = graphics.MeasureString(_temps[j].ToString(), SmallFont);
					graphics.DrawString(_temps[j].ToString(), SmallFont, brush, (float)num5 - sizeF2.Width / 2f, num3 + 8);
				}
			}
			graphics.DrawLine(pen2, num, num3, num + num2, num3);
			using SolidBrush brush2 = new SolidBrush(_color);
			graphics.DrawString(string.Format(Strings.FanCurveTitleHint, _label), TitleFont, brush2, num, 3f * UiDpi.Paint(this) / 96f);
			Point[] array = new Point[_valid];
			for (int k = 0; k < _valid; k++)
			{
				array[k] = new Point(X(k, num, num2), Y(_duties[k], h, num3));
			}
			using Pen pen3 = new Pen(_color, 2.5f);
			for (int l = 0; l < array.Length - 1; l++)
			{
				graphics.DrawLine(pen3, array[l], array[l + 1]);
			}
			using SolidBrush brush3 = new SolidBrush(_color);
			Point[] array2 = array;
			for (int m = 0; m < array2.Length; m++)
			{
				Point point = array2[m];
				graphics.FillEllipse(brush3, point.X - 6, point.Y - 6, 12, 12);
			}
			if (_dragIdx < 0)
			{
				return;
			}
			Point point2 = array[_dragIdx];
			using Pen pen4 = new Pen(UiVisualStyle.Text, 2.5f);
			graphics.DrawEllipse(pen4, point2.X - 10, point2.Y - 10, 20, 20);
			SizeF sizeF3 = graphics.MeasureString(_duties[_dragIdx] + "%", SmallFont);
			using SolidBrush brush4 = new SolidBrush(UiVisualStyle.SurfaceRaised);
			using Pen pen5 = UiVisualStyle.OutlinePen(this, UiVisualStyle.Border, 1f, 1f, inset: false);
			graphics.FillRectangle(brush4, (float)point2.X - sizeF3.Width / 2f - 4f, point2.Y - 26, sizeF3.Width + 8f, sizeF3.Height + 4f);
			graphics.DrawRectangle(pen5, (float)point2.X - sizeF3.Width / 2f - 4f, point2.Y - 26, sizeF3.Width + 8f, sizeF3.Height + 4f);
			using SolidBrush brush5 = new SolidBrush(UiVisualStyle.Text);
			graphics.DrawString(_duties[_dragIdx] + "%", SmallFont, brush5, (float)point2.X - sizeF3.Width / 2f, point2.Y - 25);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			(int left, int w, int h, int top, int bottom) tuple = Geo();
			int item = tuple.left;
			int item2 = tuple.w;
			int item3 = tuple.h;
			int item4 = tuple.bottom;
			double num = double.MaxValue;
			for (int i = 0; i < _valid; i++)
			{
				int num2 = X(i, item, item2);
				int num3 = Y(_duties[i], item3, item4);
				double num4 = Math.Sqrt((e.X - num2) * (e.X - num2) + (e.Y - num3) * (e.Y - num3));
				if (num4 < num)
				{
					num = num4;
					_dragIdx = i;
				}
			}
			if (num > (double)(12f * UiDpi.Paint(this) / 96f))
			{
				_dragIdx = -1;
			}
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_dragIdx >= 0)
			{
				(int left, int w, int h, int top, int bottom) tuple = Geo();
				int item = tuple.h;
				int num = Math.Clamp((tuple.bottom - e.Y) * 100 / item, 0, 100);
				if (num != _duties[_dragIdx])
				{
					_duties[_dragIdx] = num;
					Array.Copy(MechrevoHw.NormalizeFanCurve(_duties, _temps), _duties, _duties.Length);
					Invalidate();
					_changed();
				}
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (_dragIdx >= 0)
			{
				_dragIdx = -1;
				Invalidate();
				_changed();
			}
		}
	}

	private CurvePanel _cpuPanel;

	private CurvePanel _gpuPanel;

	private Label _status;

	private RCheckBox? _respectiveChk;

	// 2026-10-17（接手修复 · 待办 #1）：宝龙达软件闭环的启用开关
	//   原来保存曲线必然 Enabled=true 且没有关闭入口 ⇒ 机主只能手改 cfg 才能停掉
	private RCheckBox? _bdEnableChk;

	private bool _syncingBdEnable;

	// 2026-10-19（机主：双风扇 0.85 联动开关放进「风扇曲线」窗口）：开关见 OnCurveUpdated 的同步逻辑
	private RCheckBox _bdSyncChk;

	private bool _syncingBdSync;

	// 2026-10-19：双风扇联动复选框的正面解释（本窗第一个 ToolTip）
	private readonly ToolTip _toolTip = new ToolTip();

	private Label? _respectiveHint;

	private bool _syncingRespective;

	private bool _shown;

	private bool _initialIndependentControlRequested;

	private readonly Timer _saveTimer;

	private bool _cpuPending;

	private bool _gpuPending;

	private bool _saveInProgress;

	/// <summary>2026-10-10（用户反馈 ②「自定义模式风扇表不能保存」）：这条曲线要记进**哪一个档位**。
	/// null = 沿用旧口径（当时活动的档位）。从自定义模式打开时由 CustomModeForm 传入正在编辑的档位。</summary>
	internal string TargetModeId { get; set; }

	/// <summary>
	/// 2026-10-15（bugfix-fan-logo bug①，round2 用户改案「保存 = 保存 + 自动应用一次」）：
	/// 曲线窗口每轮「写 EC + 700ms 回读比对」结束后触发，<paramref name="confirmed"/> = 该轮回读是否一致
	///（一致 ⇒ RecordFanCurves 已落盘、曲线即最新落盘值）。CustomModeForm 订阅它做**自动应用**：
	/// 用最新落盘值强制再下发一次（ActivateAsync forceParameters），确保不被面板旧快照覆盖；
	/// confirmed=false 时 owner 给可见失败提示，⛔ 不静默。
	/// ⚠️ 引用关系说明：CustomModeForm 每次 OpenFanCurveAsync 都 new 一个 FanCurveForm 并 AddOwnedForm
	/// （CustomModeForm.cs:2354-2360），生命周期由 owner 管 ⇒ 用普通 .NET event（弱耦合，窗体销毁即断），
	/// 不需要静态注册表。
	/// </summary>
	// round2 fixup：本项目未开 #nullable（文件里既有 `T?` 都会产生 CS8632 基线噪音）⇒ 新代码不再加
	// `T?` 注解：用普通 event（Action<bool>，编译器自动生成 null 判空的 add/remove/raise 语义）。
	internal event Action<bool> FanCurveSaved;

	private void RaiseFanCurveSaved(bool confirmed)
	{
		try
		{
			FanCurveSaved?.Invoke(confirmed);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("FanCurveSaved handler failed: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	internal static bool ShouldRequestIndependentControlOnOpen(bool supported, bool alreadyEnabled)
	{
		if (supported)
		{
			return !alreadyEnabled;
		}
		return false;
	}

	private int D(int value)
	{
		return ResponsiveLayout.LogicalToDevice(this, value);
	}

	public FanCurveForm()
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.FanCurve;
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		// 2026-10-13（用户原话「都改掉不要最小化」）：只留【关闭】—— 藏掉最小化按钮。
		// CustomModeForm 每次都以**新实例** AddOwnedForm + ShowAdjacentTo 打开（CustomModeForm.cs:2354-2361），
		// 根本没有"把它叫回来"的入口 ⇒ 被最小化就等于丢了。MinimizeBox=false 连 WS_MINIMIZEBOX 一起去掉。
		// ⚠️ FormBorderStyle 保持 FixedSingle：用户只要求藏按钮，没要求改边框样式。
		MinimizeBox = false;
		StartPosition = FormStartPosition.Manual;
		InitTheme(setDPI: true);
		StartPosition = FormStartPosition.CenterScreen;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = UiVisualStyle.Window,
			Padding = new Padding(D(12), D(8), D(12), 0)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(new Label
		{
			Text = Strings.FanCurveCurrentProfile,
			Font = UiVisualStyle.Font(12f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, D(12), 0)
		}, 0, 0);
		_status = new Label
		{
			Text = "",
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, D(2), D(12), 0)
		};
		tableLayoutPanel2.Controls.Add(_status, 1, 0);
		Label label = new Label
		{
			Text = Strings.DragCurveHint,
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, D(2), 0, 0)
		};
		tableLayoutPanel2.Controls.Add(label, 2, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		_saveTimer = new Timer
		{
			Interval = 150
		};
		_saveTimer.Tick += async (object? _, EventArgs _) =>
		{
			_saveTimer.Stop();
			await FlushPendingAsync();
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = true,
			BackColor = UiVisualStyle.Window,
			Padding = new Padding(D(12), 0, D(12), D(8)),
			MinimumSize = new Size(0, D(40))
		};
		RButton rButton = new RButton
		{
			Text = Strings.Save,
			Width = D(88),
			Height = D(30),
			Margin = new Padding(0, 0, D(12), 0),
			Cursor = Cursors.Hand
		};
		rButton.Click += async (object? _, EventArgs _) =>
		{
			await SendBoth();
		};
		flowLayoutPanel.Controls.Add(rButton);
		_respectiveChk = new RCheckBox
		{
			Text = Strings.FanIndependentControl,
			Checked = (Program.hw?.FanRespective ?? false),
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			Margin = new Padding(0, D(4), D(12), 0),
			Cursor = Cursors.Hand
		};
		_respectiveChk.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingRespective && Program.service != null)
			{
				bool requested = _respectiveChk.Checked;
				_respectiveChk.Enabled = false;
				if (!(await Program.service.SwitchFanRespective(requested)))
				{
					_syncingRespective = true;
					_respectiveChk.Checked = Program.hw?.FanRespective ?? (!requested);
					_syncingRespective = false;
					_status.Text = Strings.FanIndepUnconfirmed;
				}
				_respectiveChk.Enabled = true;
			}
		};
		flowLayoutPanel.Controls.Add(_respectiveChk);
		_respectiveHint = new Label
		{
			Text = Strings.FanIndepHelp,
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Margin = new Padding(0, D(4), 0, 0),
			Font = UiVisualStyle.Font(8.5f)
		};
		flowLayoutPanel.Controls.Add(_respectiveHint);
		// 2026-10-17（接手修复 · 待办 #1）：宝龙达的「启用软件闭环」开关
		//   宝龙达没有固件曲线表 ⇒ 曲线只能由我们的程序按温度下发
		//   ⇒ 必须有个关得掉的入口（关 = 两通道归零 + 关手动开关 = 交回固件）
		_bdEnableChk = new RCheckBox
		{
			Text = Strings.FanCurveBaolongdaEnable,
			Checked = false,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			Visible = false,
			Margin = new Padding(D(12), D(4), 0, 0),
			Cursor = Cursors.Hand
		};
		_bdEnableChk.CheckedChanged += (object? _, EventArgs _) =>
		{
			if (_syncingBdEnable || !(Program.hw?.UsesBaolongda ?? false))
			{
				return;
			}
			ToggleBaolongdaCurveEnable(_bdEnableChk.Checked);
		};
		flowLayoutPanel.Controls.Add(_bdEnableChk);
		// 2026-10-19（机主确认位置 · 截图）：双风扇 0.85 联动开关放在「启用软件闭环」同一行、紧挨着它右边 ✅
		//   依据：开源蛟龙 `AutoFanControl` 的 `SharedHeatPipeSyncRatio` = 0.85 是**自定义风扇闭环内部**的固定系数（参考自 斗战者(GPL-3.0)/开源蛟龙(MIT)，仅取数值形状与算法思路，未复制代码）
		//   ⇒ 它属于「风扇曲线」这个功能域，⛔ 不是独立于曲线的系统开关
		//   ⚠️ 只在宝龙达显示（与「启用软件闭环」同一可见性判据）；默认勾选（SyncEnabled 默认 true）
		_bdSyncChk = new RCheckBox
		{
			Text = Strings.FanSyncSharedHeatPipe,
			Checked = true,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			Visible = false,
			Margin = new Padding(D(12), D(4), 0, 0),
			Cursor = Cursors.Hand
		};
		_toolTip.SetToolTip(_bdSyncChk, Strings.FanSyncSharedHeatPipeTip);
		_bdSyncChk.CheckedChanged += (object sender, EventArgs e) =>
		{
			if (_syncingBdSync || !(Program.hw?.UsesBaolongda ?? false))
			{
				return;
			}
			ToggleBaolongdaSyncEnable(_bdSyncChk.Checked);
		};
		flowLayoutPanel.Controls.Add(_bdSyncChk);
		_cpuPanel = new CurvePanel("CPU", UiVisualStyle.Accent, () =>
		{
			_cpuPending = true;
			_saveTimer.Stop();
			_saveTimer.Start();
		})
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(D(12), 0, D(12), 0)
		};
		_gpuPanel = new CurvePanel("GPU", UiVisualStyle.Danger, () =>
		{
			_gpuPending = true;
			_saveTimer.Stop();
			_saveTimer.Start();
		})
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(D(12), 0, D(12), 0)
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Controls.Add(_cpuPanel, 0, 0);
		tableLayoutPanel3.Controls.Add(_gpuPanel, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 1);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 2);
		if (Program.hw != null)
		{
			Program.hw.CurveUpdated += OnCurveUpdated;
			Program.hw.CustomModeChanged += OnCustomChanged;
			FormClosed += (object? _, FormClosedEventArgs _) =>
			{
				Program.hw.CurveUpdated -= OnCurveUpdated;
				Program.hw.CustomModeChanged -= OnCustomChanged;
				_saveTimer.Stop();
				_saveTimer.Dispose();
				_toolTip.Dispose();
			};
		}
		else
		{
			FormClosed += (object? _, FormClosedEventArgs _) =>
			{
				_saveTimer.Stop();
				_saveTimer.Dispose();
				_toolTip.Dispose();
			};
		}
		OnCurveUpdated();
		OnCustomChanged();
		Shown += async (object? _, EventArgs _) =>
		{
			_shown = true;
			MechrevoHw hw = Program.hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：下面直接 Publish(Fan/Control) ⇒ 真正需要 GCU。
			if (hw != null && hw.IsGcuConnected)
			{
				try
				{
					await Program.hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" });
					await EnableIndependentControlOnOpenAsync();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Fan curve initial refresh failed: " + ex.Message);
				}
			}
		};
		UiVisualStyle.ApplyWindow(this);
		UiVisualStyle.ApplyTitle((Label)tableLayoutPanel2.Controls[0], 12f);
		UiVisualStyle.ApplyMuted(_status);
		UiVisualStyle.ApplyPrimaryButton(rButton);
		Label label2 = (Label)tableLayoutPanel2.Controls[0];
		int width = Math.Max(label2.PreferredSize.Width + label2.Margin.Horizontal + _status.PreferredSize.Width + _status.Margin.Horizontal + label.PreferredSize.Width + label.Margin.Horizontal + tableLayoutPanel2.Padding.Horizontal + D(8), 2 * D(340) + 2 * _cpuPanel.Padding.Horizontal);
		if (!IsHandleCreated)
		{
			CreateControl();
		}
		ClientSize = new Size(width, ClientSize.Height);
		ResponsiveLayout.PerformLayoutTree(this);
		int height = tableLayoutPanel2.Height + D(170) + flowLayoutPanel.Height + D(2);
		ClientSize = new Size(width, height);
	}

	private void OnCurveUpdated()
	{
		try
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				BeginInvoke(OnCurveUpdated);
				return;
			}
			MechrevoHw hw = Program.hw;
			if (hw != null)
			{
				// ** 2026-10-17（风扇曲线接入）：** 宝龙达**没有固件曲线表** ⚠️
				//   => 它走的是**软件闭环**（读温度 -> 查曲线 -> 写功能号 21），数据存 `BaolongdaFanCurveStore`
				//   => 本窗复用同一套绘制控件，只是把 **RPM 换算成占空比 %** 显示
				//   ⚠️ 通道映射（owner 已确认）：`FanType 0`（大扇）= **GPU** <-> `_gpuPanel`
				//      · `FanType 1`（小扇）= **CPU** <-> `_cpuPanel`
				if (hw.UsesBaolongda)
				{
					BaolongdaFanCurveSettings s = hw.LoadBaolongdaFanCurve();
					_cpuPanel.Load(TempsToBytes(s.Channel1Temperatures), RpmsToPercent(s.Channel1Rpms, s.MinRpm, s.MaxRpm));
					_gpuPanel.Load(TempsToBytes(s.Channel0Temperatures), RpmsToPercent(s.Channel0Rpms, s.MinRpm, s.MaxRpm));
					_status.Text = "风扇曲线：软件闭环（宝龙达）"
						+ (s.Enabled ? " · 已启用" : " · 未启用")
						+ $" · 范围 {s.MinRpm}~{s.MaxRpm} RPM"
						+ (s.IsUnreadableArchive ? " · " + s.StatusNote : "");
					// ⭐⭐ 2026-10-19（机主：状态行/开关与 store 不一致的根因修复）：
					//   ⭐ 每次刷新都**以 store 为准**重新同步两个开关（⛔ 不被 GCU 能力位等外部判据改掉）
					//   ⚠️ 宝龙达下「启用软件闭环」**不依赖 GCU** ⇒ 这里不新增任何能力位判据 ✅
					SyncBaolongdaCurveSwitches(s);
					return;
				}
				_cpuPanel.Load(hw.CpuCurveUpT, hw.CpuCurveDuty);
				_gpuPanel.Load(hw.GpuCurveUpT, hw.GpuCurveDuty);
				_status.Text = Strings.FanTablePrefix + (string.IsNullOrEmpty(hw.TableName) ? Strings.Unknown : hw.TableName);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Fan curve refresh failed: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	// 2026-10-19（机主：开关状态与 store 不一致的根因修复）：
	//   ① 勾选/取消**立即落盘**（SaveBaolongdaFanCurve 会真的写 fan_curve_bd.cfg）✅
	//   ② 之后**重新从 store 读回**，状态行 / 开关 / 日志全部以读回值为准（⛔ 不拿「我们以为写进去的」当真）✅
	//   ③ 日志固定一行：`[fan] 软件闭环 ⇒ 开/关（已保存）` ✅
	private void ToggleBaolongdaCurveEnable(bool enabled)
	{
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return;
		}
		try
		{
			BaolongdaFanCurveSettings s = hw.LoadBaolongdaFanCurve();
			bool ok = hw.SaveBaolongdaFanCurve(s.WithEnabled(enabled));
			// ⭐ 以**落盘后重新读回**的 store 值为准（⛔ 不拿内存里的「想存的」当真）
			BaolongdaFanCurveSettings now = hw.LoadBaolongdaFanCurve();
			SyncBaolongdaCurveSwitches(now);
			Logger.WriteLine($"[fan] 软件闭环 ⇒ {(enabled ? "开" : "关")}（已保存）· 回读 enabled={now.Enabled} · ok={ok}");
			_status.Text = !ok
				? "风扇曲线：启用开关保存失败"
				: (now.Enabled ? Strings.FanCurveSavedAndEnabled : Strings.FanCurveSavedNotEnabled);
			RaiseFanCurveSaved(confirmed: ok && (now.Enabled == enabled));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线开关失败: " + ex.Message);
			_status.Text = "风扇曲线：开关失败";
			// 异常时按 store 真实值回滚开关，⛔ 不留「看起来已启用」的假象
			try
			{
				SyncBaolongdaCurveSwitches(hw.LoadBaolongdaFanCurve());
			}
			catch
			{
			}
		}
	}

	// 2026-10-19（机主：双风扇 0.85 联动开关）：随曲线存档一起保存（SyncEnabled）
	//   开 = 两路按 max(自己, 对方×0.85) 互相带动；关 = 两路各算各的
	//   ⚠️ 只改 SyncEnabled，**不动**软件闭环的启用态（Enabled 原样）✅
	private void ToggleBaolongdaSyncEnable(bool enabled)
	{
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return;
		}
		try
		{
			BaolongdaFanCurveSettings s = hw.LoadBaolongdaFanCurve();
			bool ok = hw.SaveBaolongdaFanCurve(s.WithSyncEnabled(enabled));
			BaolongdaFanCurveSettings now = hw.LoadBaolongdaFanCurve();
			SyncBaolongdaCurveSwitches(now);
			Logger.WriteLine($"[fan] 双风扇联动 ⇒ {(enabled ? "开" : "关")}（0.85 系数 · 已保存）· 回读 sync={now.SyncEnabled} · ok={ok}");
			_status.Text = !ok
				? "风扇曲线：双风扇联动保存失败"
				: (now.SyncEnabled
					? "风扇曲线：双风扇联动已开启（共用热管 · 0.85 系数）"
					: "风扇曲线：双风扇联动已关闭（两路各算各的）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达双风扇联动保存失败: " + ex.Message);
			_status.Text = "风扇曲线：双风扇联动保存失败";
			try
			{
				SyncBaolongdaCurveSwitches(hw.LoadBaolongdaFanCurve());
			}
			catch
			{
			}
		}
	}

	// 2026-10-19（机主：开关显示关 / 引擎读到的 Enabled=false 不一致的根因修复）：
	//   把「启用软件闭环」+「双风扇联动」两个开关**无条件**按 store 的当前值重新同步 ✅
	//   ⚠️ 宝龙达专用；⛔ 不读 GCU 能力位（这两个开关都不依赖 GCU）✅
	private void SyncBaolongdaCurveSwitches(BaolongdaFanCurveSettings s)
	{
		if (s == null)
		{
			return;
		}
		_syncingBdEnable = true;
		try
		{
			if (_bdEnableChk != null)
			{
				_bdEnableChk.Visible = true;
				// ⚠️ 宝龙达下这个开关**必须可勾选**（它不依赖 GCU）⇒ 只有「存档读不懂」才禁用
				_bdEnableChk.Enabled = !s.IsUnreadableArchive;
				_bdEnableChk.Checked = s.Enabled;
			}
		}
		finally
		{
			_syncingBdEnable = false;
		}
		_syncingBdSync = true;
		try
		{
			if (_bdSyncChk != null)
			{
				_bdSyncChk.Visible = true;
				_bdSyncChk.Enabled = !s.IsUnreadableArchive;
				_bdSyncChk.Checked = s.SyncEnabled;
			}
		}
		finally
		{
			_syncingBdSync = false;
		}
	}

	private void OnCustomChanged()
	{
		try
		{
			if (IsDisposed || _respectiveChk == null)
			{
				return;
			}
			if (InvokeRequired)
			{
				BeginInvoke(OnCustomChanged);
				return;
			}
			_syncingRespective = true;
			_respectiveChk.Checked = Program.hw?.FanRespective ?? false;
			_syncingRespective = false;
			bool visible = Program.UiAuditMode || (Program.hw?.SupportsFanRespective ?? false);
			_respectiveChk.Visible = visible;
			if (_respectiveHint != null)
			{
				_respectiveHint.Visible = visible;
			}
			if (!_shown || _initialIndependentControlRequested)
			{
				return;
			}
			MechrevoHw hw = Program.hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：分路风扇曲线是 GCU 专属 ⇒ 真正需要 GCU。
			if (hw != null && hw.IsGcuConnected && hw.SupportsFanRespective)
			{
				if (ShouldRequestIndependentControlOnOpen(supported: true, hw.FanRespective))
				{
					EnableIndependentControlOnOpenAsync();
				}
				else
				{
					_initialIndependentControlRequested = true;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Fan respective UI sync failed: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	private async Task EnableIndependentControlOnOpenAsync()
	{
		if (_initialIndependentControlRequested || _respectiveChk == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：下面走 service（MQTT 下发风扇独立控制）⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected || Program.service == null)
		{
			return;
		}
		_initialIndependentControlRequested = true;
		if (ShouldRequestIndependentControlOnOpen(hw.SupportsFanRespective, hw.FanRespective))
		{
			_respectiveChk.Enabled = false;
			_status.Text = Strings.EnablingFanIndep;
			bool flag = await Program.service.SwitchFanRespective(on: true);
			if (!IsDisposed && _respectiveChk != null)
			{
				_syncingRespective = true;
				_respectiveChk.Checked = Program.hw?.FanRespective ?? false;
				_syncingRespective = false;
				_respectiveChk.Enabled = true;
				_status.Text = (flag ? Strings.FanIndepDefaultOn : Strings.FanIndepUnconfirmed);
			}
		}
	}

	private async Task SendBoth()
	{
		_cpuPending = (_gpuPending = true);
		await FlushPendingAsync();
	}

	private async Task FlushPendingAsync()
	{
		if (_saveInProgress)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		// ** 2026-10-17（风扇曲线接入）：** 宝龙达走**软件闭环**，不经过 GCU/MQTT ⚠️
		//   => 把占空比 % 换算回 RPM 后存进 `BaolongdaFanCurveStore`，并**立即应用一次**
		//   ⚠️ 非宝龙达 => 下面的 GCU 逻辑**逐位不变**
		if (hw != null && hw.UsesBaolongda)
		{
			BaolongdaFanCurveSettings prev = hw.LoadBaolongdaFanCurve();
			_cpuPending = (_gpuPending = false);
			try
			{
				int[] cpuPercent = _cpuPanel.Duties.ToArray();
				int[] gpuPercent = _gpuPanel.Duties.ToArray();
				BaolongdaFanCurveSettings next = new BaolongdaFanCurveSettings
				{
					Enabled = prev.Enabled,
					// ⭐ 2026-10-19：双风扇联动开关随曲线一起存（⛔ 保存曲线不能把它改回默认）✅
					SyncEnabled = prev.SyncEnabled,
					HysteresisC = prev.HysteresisC,
					IntervalMs = prev.IntervalMs,
					MinRpm = prev.MinRpm,
					MaxRpm = prev.MaxRpm,
					Channel0Temperatures = prev.Channel0Temperatures,
					Channel0Rpms = PercentToRpms(gpuPercent, prev.MinRpm, prev.MaxRpm),
					Channel1Temperatures = prev.Channel1Temperatures,
					Channel1Rpms = PercentToRpms(cpuPercent, prev.MinRpm, prev.MaxRpm),
				};
				bool ok = hw.SaveBaolongdaFanCurve(next);
				// ⭐⭐ 2026-10-17（宝龙达 · 自定义档风扇曲线接入）：把本轮的 16 点占空比**同时记进该档配置** ✅
				//   ⇒ 自定义档「应用」时 `PerfModeApplyPlanner` 会生成 `WriteFanCurve` 步骤，
				//     再由 `PerfModeBackend` 换成 RPM 写回 `BaolongdaFanCurveStore`（本机软件闭环 20/21）✅
				//   ⚠️ `RecordFanCurves` 内部已按 route 判据过滤（非固件槽档只记日志、不落盘）✅
				if (ok)
				{
					try
					{
						MechrevoLite.Mode.PerfModeService.Instance?.RecordFanCurves(TargetModeId, cpuPercent, gpuPercent);
					}
					catch (Exception exRec)
					{
						Logger.WriteLine("宝龙达曲线记录到档位失败（已吞掉）：" + exRec.Message);
					}
				}
				// ⭐ 2026-10-19（机主：状态行说「已保存并启用」、开关却是关的）：
				//   ⭐ 保存后**重新从 store 读回**，状态行 + 两个开关全部以读回值为准 ✅
				//   ⚠️ 「保存」按钮的语义不变（仍是保存曲线点）✅
				BaolongdaFanCurveSettings now = ok ? hw.LoadBaolongdaFanCurve() : prev;
				SyncBaolongdaCurveSwitches(now);
				_status.Text = !ok
					? "风扇曲线保存失败"
					: ((now.Enabled ? Strings.FanCurveSavedAndEnabled : Strings.FanCurveSavedNotEnabled)
						+ " · " + DateTime.Now.ToString("HH:mm:ss"));
				RaiseFanCurveSaved(confirmed: ok);
			}
			catch (Exception exb)
			{
				Logger.WriteLine("宝龙达风扇曲线保存失败: " + exb.Message);
				_status.Text = "风扇曲线保存失败";
				RaiseFanCurveSaved(confirmed: false);
			}
			return;
		}
		// 2026-10-15（C-1 第 2 步分类 ①）：本函数是「保存风扇曲线到 GCU」⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected)
		{
			_status.Text = Strings.NotConnectedNotSaved;
			return;
		}
		_saveInProgress = true;
		try
		{
			while (_cpuPending || _gpuPending)
			{
				bool sendCpu = _cpuPending;
				bool sendGpu = _gpuPending;
				_cpuPending = (_gpuPending = false);
				int[] cpu = _cpuPanel.Duties.ToArray();
				int[] gpu = _gpuPanel.Duties.ToArray();
				if (sendCpu)
				{
					await Program.hw.SetFanCurve(0, cpu);
				}
				if (sendGpu)
				{
					await Program.hw.SetFanCurve(1, gpu);
				}
				await Program.hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" });
				await Task.Delay(700);
				bool flag = (!sendCpu || CurveMatches(cpu, Program.hw.CpuCurveDuty, Program.hw.CpuCurveUpT)) && (!sendGpu || CurveMatches(gpu, Program.hw.GpuCurveDuty, Program.hw.GpuCurveUpT));
				_status.Text = (flag ? string.Format(Strings.ConfirmedAt, DateTime.Now.ToString("HH:mm:ss")) : Strings.SaveUnconfirmedRetry);
				// 2026-10-09（75:00）：上游 beta21 的落盘那一行（_upstream\0.290.6\decomp\MechrevoLite\FanCurveForm.cs:629-632）
				// 在本树里整段丢了 —— 结果「写 EC 成功、回读确认成功」之后就到此为止，config 里的
				// perf_<档位id>_fancpu / _fangpu 永远还是旧曲线。之后任何 apply / 切档（forceParameters=true）
				// 都会用旧值**全量重写** EC，用户刚保存的曲线就被旧值覆盖；开机也不重推。
				// ⇒ 回读确认（flag）之后立刻把这一侧真正保存下的值记进 config。
				if (flag)
				{
					PerfModeService.Instance?.RecordFanCurves(TargetModeId, sendCpu ? cpu : null, sendGpu ? gpu : null);
					// 2026-10-15（bugfix-fan-logo bug①，round2）：落盘成功 ⇒ 通知 owner（CustomModeForm）
					// 自动应用一次（owner 会以最新落盘值为基线强制重发，确保生效且可见结果）。
					RaiseFanCurveSaved(confirmed: true);
				}
				else
				{
					// 回读不一致 ⇒ 曲线可能没写进去。同样通知 owner（confirmed=false），
					// 由它在面板状态行给出**可见**失败提示，⛔ 不静默。
					RaiseFanCurveSaved(confirmed: false);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Fan curve save failed: " + ex.Message);
			_status.Text = Strings.SaveFailed;
			// round2：写 EC 抛异常也是"保存失败" ⇒ 通知 owner 给可见提示（无条件，一发一收）。
			RaiseFanCurveSaved(confirmed: false);
		}
		finally
		{
			_saveInProgress = false;
			if (_cpuPending || _gpuPending)
			{
				_saveTimer.Start();
			}
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	// ** 2026-10-17（风扇曲线接入 · 宝龙达）：RPM 与占空比 % 的换算
	//   ⚠️ 为什么能这样换算：宝龙达**没有可靠的通道 max RPM**
	//      => 用存档里的 `MinRpm` / `MaxRpm` 当分母（**我们设的**边界，不是硬件上限）
	//   ⚠️ 精度：16 点 x 0~100% => RPM 步进 ≈ (Max-Min)/100，可接受
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>温度数组 int[] -> byte[]（绘制控件口径；超界夹到 0~254）。</summary>
	private static byte[] TempsToBytes(int[] temps)
	{
		byte[] r = new byte[temps.Length];
		for (int i = 0; i < temps.Length; i++)
		{
			r[i] = (byte)Math.Clamp(temps[i], 0, 254);
		}
		return r;
	}

	/// <summary>RPM 数组 -> 占空比 %（0~100）。⚠️ 分母是我们设的 Min/Max，不是硬件上限。</summary>
	private static byte[] RpmsToPercent(int[] rpms, int minRpm, int maxRpm)
	{
		byte[] r = new byte[rpms.Length];
		int span = Math.Max(1, maxRpm - minRpm);
		for (int i = 0; i < rpms.Length; i++)
		{
			int pct = (int)Math.Round((rpms[i] - minRpm) * 100.0 / span);
			r[i] = (byte)Math.Clamp(pct, 0, 100);
		}
		return r;
	}

	/// <summary>占空比 %（0~100）-> RPM 数组。</summary>
	private static int[] PercentToRpms(int[] percent, int minRpm, int maxRpm)
	{
		int[] r = new int[percent.Length];
		int span = Math.Max(1, maxRpm - minRpm);
		for (int i = 0; i < percent.Length; i++)
		{
			int pct = Math.Clamp(percent[i], 0, 100);
			r[i] = minRpm + (int)Math.Round(pct * span / 100.0);
		}
		return r;
	}

	private static bool CurveMatches(int[] requested, byte[] actual, byte[] temperatures)
	{
		int[] expected = MechrevoHw.NormalizeFanCurve(requested, temperatures);
		int num = Array.FindIndex(temperatures, (byte value) => value == byte.MaxValue);
		if (num < 2)
		{
			num = Math.Min(11, actual.Length);
		}
		return Enumerable.Range(0, num).All((int i) => actual[i] == expected[i]);
	}
}
