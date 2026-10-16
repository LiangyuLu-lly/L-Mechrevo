using System;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Threading;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.Hardware;
using MechrevoLite.Properties;using MechrevoLite.Helpers;
using MechrevoLite.Properties;using MechrevoLite.UI;
using MechrevoLite.Properties;
namespace MechrevoLite;

public class LightForm : RForm
{
	public static readonly (string Effect, string Name)[] LightbarEffects = new (string, string)[5]
	{
		("Single", Strings.EffectSingle),
		("Breathing", Strings.EffectBreathing),
		("Wave", Strings.EffectWave),
		("Impact", Strings.EffectImpact),
		("Raindrop", Strings.EffectMeteor)
	};

	public static readonly (string Effect, string Name)[] LogoEffects = new (string, string)[3]
	{
		("Single", Strings.EffectSingle),
		("Breathing", Strings.EffectBreathing),
		("Mix", Strings.EffectMix)
	};

    // 2026-10-09（③，按作者 0.290.6 对齐）：铰链 = 作者 Lighbar4(biosProjectId) 的 5 项
    // （Single/Breathing/Wave/Impact/Raindrop，与灯条行同一张表；作者对 biosProjectId=="ID5" 会缩到 3 项，
    //  我们分支没有 ID5 模型，故固定 5 项 —— 见报告"未确定项"）。
    public static readonly (string Effect, string Name)[] HingeEffects = new (string, string)[5]
    {
        ("Single", Strings.EffectSingle),
        ("Breathing", Strings.EffectBreathing),
        ("Wave", Strings.EffectWave),
        ("Impact", Strings.EffectImpact),
        ("Raindrop", Strings.EffectMeteor)
    };

    // 2026-10-09（③，按作者 0.290.6 对齐）：同步 = 作者 Sync() 的 2 项（Single/Dawn，Dawn 新加资源串）。
    public static readonly (string Effect, string Name)[] SyncEffects = new (string, string)[2]
    {
        ("Single", Strings.EffectSingle),
        ("Dawn", Strings.LightEffectDawn)
    };

	private readonly string _topic;

	private readonly (string Effect, string Name)[] _effects;

	private string _effect;

	private int _light = 4;

	private int _speed = 1;

	private bool _powerOn = true;

	private Color _singleColor = Color.White;

	private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

	private readonly System.Windows.Forms.Timer _effectUpdateTimer = new System.Windows.Forms.Timer
	{
		Interval = 180
	};

	private int _sendGeneration;

	private int _disposed;

	internal static void NotifyApplyOutcome(bool accepted, bool cancelled)
	{
		if (!(accepted | cancelled))
		{
			ToastForm.ShowFailure(Strings.LightSettingFailed);
		}
	}

	public LightForm(string topic, string title, (string Effect, string Name)[]? effects = null)
	{
		_topic = topic;
		_effects = effects ?? LightbarEffects;
		_effect = _effects[0].Effect;
		LoadSettings();
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = title;
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		// 2026-10-13（用户原话「都改掉不要最小化」）：只留【关闭】—— 藏掉最小化按钮。
		// 本窗是 SettingsForm 的单例子窗（SettingsForm.cs:1433），"再打开"以 Visible 为闸（:1438/:1440）
		// ⇒ 最小化时 Visible 仍为 true，点一次只会 Close()，再点才重建 ⇒ 用户看到"点了没反应"。
		// MinimizeBox=false 会连 WS_MINIMIZEBOX 一起去掉（WinForms 由该属性决定该样式位）。
		// ⚠️ FormBorderStyle 保持 FixedSingle：用户只要求藏按钮，没要求改边框样式。
		MinimizeBox = false;
		StartPosition = FormStartPosition.Manual;
		ClientSize = new Size(420, 240);
		InitTheme(setDPI: true);
		TableLayoutPanel table = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = Padding.Empty,
			ColumnCount = 2,
			Padding = new Padding(20, 16, 20, 16),
			BackColor = UiVisualStyle.Surface
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		table.RowStyles.Clear();
		int row = 0;
		_effectUpdateTimer.Tick += async (object? _, EventArgs _) =>
		{
			_effectUpdateTimer.Stop();
			await SendAsync();
		};
		RSlider sliderLight = new RSlider
		{
			Minimum = 0,
			Maximum = 100,
			Value = _light * 25,
			MinimumSize = new Size(104, 20),
			Height = 28
		};
		sliderLight.ValueChanged += (object? _, EventArgs _) =>
		{
			_light = Math.Clamp(sliderLight.Value * 4 / 100, 0, Program.hw?.UsesBaolongda == true ? 3 : 4);
			ScheduleEffectUpdate();
		};
		AddRow(Strings.Brightness + (Program.hw?.UsesBaolongda == true ? "（设为 0 即关闭键盘灯）" : ""), sliderLight);
		RComboBox comboSpeed = new RComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		ComboBox.ObjectCollection items = comboSpeed.Items;
		object[] items2 = new string[3] { Strings.SpeedSlow, Strings.SpeedMedium, Strings.SpeedFast };
		items.AddRange(items2);
		comboSpeed.SelectedIndex = Math.Clamp(_speed - 1, 0, 2);
		comboSpeed.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			_speed = comboSpeed.SelectedIndex + 1;
			ScheduleEffectUpdate();
		};
		AddRow(Strings.Speed, comboSpeed);
		RColorButton colorBtn = new RColorButton
		{
			Size = new Size(36, 24),
			BackColor = UiVisualStyle.Input,
			BorderColor = UiVisualStyle.Border,
			SwatchColor = _singleColor,
			Cursor = Cursors.Hand,
			Anchor = AnchorStyles.Left
		};
		colorBtn.Click += (object? _, EventArgs _) =>
		{
			using RColorPicker rColorPicker = new RColorPicker(_singleColor);
			rColorPicker.ColorChanged += (Color c) =>
			{
				_singleColor = c;
				colorBtn.SwatchColor = c;
				// 2026-10-16（0.291.8.1）：⭐ 单色系灯效（含呼吸/波浪/冲击…）改颜色也要立刻重发 ——
				// 原来只在 Single 下重发 ⇒ 用户在呼吸档调色"没反应" ✅
				if (KeyboardFirmwareEffects.IsSingleColorEffect(_effect))
				{
					ScheduleEffectUpdate();
				}
			};
			rColorPicker.ShowDialog(this);
		};
		AddRow(Strings.SingleColor, colorBtn);
		UpdateChannelEnabled();
		Controls.Add(table);
		Shown += async (object? _, EventArgs _) =>
		{
			MechrevoHw hw = Program.hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：灯效下发走 GCU ⇒ 真正需要 GCU。
			if (hw != null && hw.IsGcuConnected && Program.service != null)
			{
				await Program.service.RequestLightStatus(_topic);
				if (!(await Program.service.SetLightPower(_topic, _powerOn)))
				{
					NotifyApplyOutcome(accepted: false, cancelled: false);
				}
				else if (_powerOn)
				{
					await Task.Delay(600);
					await SendAsync();
				}
			}
		};
		UiVisualStyle.ApplyWindow(this);
		UiVisualStyle.ApplySection(table);
		ResponsiveLayout.PerformLayoutTree(this);
		ResponsiveLayout.ScaleFrom96(this, this);
		ResponsiveLayout.PerformLayoutTree(this);
		ClientSize = new Size(Math.Max(420, ClientSize.Width), table.Height);
		void AddRow(string label, Control ctrl)
		{
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(new Label
			{
				Text = label,
				ForeColor = UiVisualStyle.Text,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill
			}, 0, row);
			if ((ctrl is ComboBox || ctrl is TrackBar || ctrl is RSlider) ? true : false)
			{
				ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				ctrl.Margin = new Padding(0, 3, 0, 3);
			}
			else if (ctrl is Button)
			{
				ctrl.Anchor = AnchorStyles.Left;
				ctrl.Margin = new Padding(0, 3, 0, 3);
			}
			else
			{
				ctrl.Dock = DockStyle.Fill;
			}
			table.Controls.Add(ctrl, 1, row);
			row++;
		}
		void LoadSettings()
		{
			LightChannelSettings lightChannelSettings = LightingSettingsStore.Load(_topic, _effects[0].Effect);
			_effect = lightChannelSettings.Effect;
			_light = lightChannelSettings.Light;
			_speed = lightChannelSettings.Speed;
			_singleColor = Color.FromArgb(lightChannelSettings.ColorArgb);
			_powerOn = lightChannelSettings.PowerOn;
		}
		void SaveSettings()
		{
			LightingSettingsStore.Save(_topic, new LightChannelSettings(_effect, _light, _speed, _singleColor.ToArgb(), _powerOn));
		}
		void ScheduleEffectUpdate()
		{
			SyncEffectFromDashboard();
			SaveSettings();
			if (_powerOn && Volatile.Read(in _disposed) == 0)
			{
				_effectUpdateTimer.Stop();
				_effectUpdateTimer.Start();
			}
		}
		async Task SendAsync()
		{
			SyncEffectFromDashboard();
			SaveSettings();
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				// 2026-10-15（C-1 第 2 步分类 ①）：灯效（GCU 通道）⇒ 真正需要 GCU。
				if (hw != null && hw.IsGcuConnected)
				{
					int generation = Interlocked.Increment(ref _sendGeneration);
					bool lockTaken = false;
					try
					{
						await _sendLock.WaitAsync(_disposeCts.Token);
						lockTaken = true;
						if (Volatile.Read(in _disposed) == 0 && generation == _sendGeneration)
						{
					// 2026-10-16（0.291.8.1 · 修「logo 灯/灯带效果错乱」）：⭐ 单色系灯效**都带用户选的颜色**
					//（原来是"只有 Single 带" ⇒ 呼吸/混合落到硬编码七彩 ⇒ 呼吸变红、混合直接把 logo 关掉 ❌）。
					bool flag = await Program.service.SetLightEffect(_topic, _effect, _light, _speed, "None", KeyboardFirmwareEffects.IsSingleColorEffect(_effect) ? new Color?(_singleColor) : ((Color?)null));
							if (!flag)
							{
								Logger.WriteLine("Light effect was not accepted: topic=" + _topic + ", effect=" + _effect);
							}
							NotifyApplyOutcome(flag, cancelled: false);
						}
					}
					catch (OperationCanceledException)
					{
						NotifyApplyOutcome(accepted: false, cancelled: true);
					}
					finally
					{
						if (lockTaken)
						{
							_sendLock.Release();
						}
					}
				}
			}
		}
		void SyncEffectFromDashboard()
		{
			_effect = LightingSettingsStore.Load(_topic, _effects[0].Effect).Effect;
		}
		void UpdateChannelEnabled()
		{
			bool powerOn = LightingSettingsStore.Load(_topic, _effects[0].Effect).PowerOn;
			sliderLight.Enabled = powerOn;
			comboSpeed.Enabled = powerOn;
			colorBtn.Enabled = powerOn;
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		TableLayoutPanel tableLayoutPanel = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
		if (tableLayoutPanel != null)
		{
			ClientSize = new Size(ClientSize.Width, tableLayoutPanel.Height);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			Interlocked.Increment(ref _sendGeneration);
			_effectUpdateTimer.Stop();
			_effectUpdateTimer.Dispose();
			_disposeCts.Cancel();
			if (_sendLock.Wait(1000))
			{
				_sendLock.Release();
				_sendLock.Dispose();
			}
			else
			{
				Logger.WriteLine("LightForm dispose timed out waiting for an in-flight command; lock left for safe completion.");
			}
			_disposeCts.Dispose();
		}
		base.Dispose(disposing);
	}
}
