using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Threading;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.Hardware;
using MechrevoLite.Properties;using MechrevoLite.UI;
using MechrevoLite.Properties;
namespace MechrevoLite;

public class RgbForm : RForm
{
	internal static readonly (int Mode, string Name)[] HidEffects = new (int, string)[10]
	{
		(1, Strings.EffectFlowRainbow),
		(9, Strings.EffectStatic),
		(0, Strings.EffectBreathing),
		(2, Strings.EffectBlink),
		(3, Strings.EffectReactive),
		(4, Strings.EffectRainbowWheel),
		(5, Strings.EffectLightning),
		(6, Strings.EffectFlame),
		(7, Strings.EffectRaindrop),
		(8, Strings.EffectMatrix)
	};

	internal static readonly (int Mode, string Name)[] HidSingleZoneEffects = new (int, string)[2]
	{
		(9, Strings.EffectStatic),
		(0, Strings.EffectBreathing)
	};

	private readonly KeyboardRgb _rgb;

	private Panel _hidPanel;

	private Label _lblStatus;

	private RowStyle _statusRowStyle;

	private readonly List<string> _paramLabels = new List<string>();

	private int _paramLabelCol;

	private int _minContentWidth;

	private int _applyGen;

	private int _closeTimerCommandGen;

	private int _gcuBrightGen;

	private readonly SemaphoreSlim _gcuBrightLock = new SemaphoreSlim(1, 1);

	private int _teardownDone;

	private RSlider _brightSlider;

	private System.Windows.Forms.Timer? _modeSyncTimer;

	private int _activeHidMode = 1;

	private string _gcuSyncedEffect = "";

	private bool _updatingClientHeight;

	private Action? _updateClientHeight;

	private bool _hidBrightnessTookEffect = true;

	internal static (int Mode, string Name)[] VisibleHidEffects(int keyboardType)
	{
		if (!KeyboardFirmwareEffects.IsSingleZoneRgb(keyboardType))
		{
			return HidEffects;
		}
		return HidSingleZoneEffects;
	}

	private int D(int value)
	{
		return ResponsiveLayout.LogicalToDevice(this, value);
	}

	private void SetStatus(string text)
	{
		_lblStatus.Text = text;
		bool flag = text.Length > 0;
		_lblStatus.Visible = flag;
		_statusRowStyle.Height = (flag ? D(25) : 0);
	}

	public RgbForm(KeyboardRgb rgb)
	{
		_rgb = rgb;
		_rgb.LoadConfig();
		_activeHidMode = _rgb.KbHidMode;
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.KeyboardLighting;
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = false;
		// 2026-10-13（用户原话「都改掉不要最小化」）：只留【关闭】—— 藏掉最小化按钮。
		// 本窗是 SettingsForm 的单例子窗（SettingsForm.cs:1451），"再打开"以 Visible 为闸（:1454/:1456）；
		// 而它自己的 FormClosing（本文件 :219-227）对 UserClosing 是 Cancel+Hide ⇒ 最小化时 Visible 仍为
		// true，点一次只会 Hide，没人把 WindowState 置回 Normal = "唤不回来"。
		// MinimizeBox=false 会连 WS_MINIMIZEBOX 一起去掉（WinForms 由该属性决定该样式位）。
		// ⚠️ FormBorderStyle 保持 Sizable：用户只要求藏按钮（面板宽度自适应重排仍依赖可缩放宽度）。
		MinimizeBox = false;
		StartPosition = FormStartPosition.Manual;
		ClientSize = new Size(D(520), D(120));
		InitTheme(setDPI: true);
		TableLayoutPanel table = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			ColumnCount = 2,
			Padding = new Padding(D(12)),
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		int row = 0;
		_hidPanel = new Panel
		{
			Dock = DockStyle.Top,
			AutoScroll = false,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(D(8))
		};
		_hidPanel.Resize += (object? _, EventArgs _) =>
		{
			FitParameterTable(_hidPanel);
		};
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.Controls.Add(_hidPanel, 0, row);
		table.SetColumnSpan(_hidPanel, 2);
		row++;
		if (IsGcuKeyboardFallback())
		{
			BuildGcuFirmwareParams();
		}
		else
		{
			BuildHidParams();
		}
		_statusRowStyle = new RowStyle(SizeType.Absolute, 0f);
		table.RowStyles.Add(_statusRowStyle);
		_lblStatus = new Label
		{
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleLeft,
			Dock = DockStyle.Fill,
			ForeColor = UiVisualStyle.Muted,
			AutoEllipsis = true,
			Visible = false
		};
		SpanRow(_lblStatus);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = UiVisualStyle.Window
		};
		panel.Controls.Add(table);
		Controls.Add(panel);
		int num = 0;
		using (Graphics dc = CreateGraphics())
		{
			foreach (string paramLabel in _paramLabels)
			{
				num = Math.Max(num, TextRenderer.MeasureText(dc, paramLabel, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
			}
		}
		_paramLabelCol = num + D(8);
		int val = 5 * D(46) + 4 * D(4) + D(4);
		int num2 = Math.Max(Math.Max(D(104), D(100)), val);
		_minContentWidth = _paramLabelCol + num2 + 2 * D(8) + 2 * D(12);
		MinimumSize = new Size(_minContentWidth + D(24), D(280));
		ClientSize = new Size(_minContentWidth, ClientSize.Height);
		table.SizeChanged += (object? _, EventArgs _) =>
		{
			UpdateClientHeight();
		};
		UpdateClientHeight();
		_updateClientHeight = UpdateClientHeight;
		_modeSyncTimer = new System.Windows.Forms.Timer
		{
			Interval = 2000
		};
		_modeSyncTimer.Tick += (object? _, EventArgs _) =>
		{
			if (Visible && !IsDisposed)
			{
				if (IsGcuKeyboardFallback())
				{
					string effect = GcuSettings().Effect;
					if (effect != _gcuSyncedEffect)
					{
						_gcuSyncedEffect = effect;
						BuildGcuFirmwareParams();
					}
				}
				else
				{
					int num3 = Array.FindIndex(HidEffects, ((int Mode, string Name) e) => e.Mode == _rgb.KbHidMode);
					int num4 = Array.FindIndex(HidEffects, ((int Mode, string Name) e) => e.Mode == _activeHidMode);
					if (num3 >= 0 && num3 != num4)
					{
						ApplyModeSelection();
					}
				}
			}
		};
		if (!Program.UiAuditMode)
		{
			_modeSyncTimer.Start();
		}
		_rgb.DeviceLost += OnRgbDeviceLost;
		FormClosing += (object? _, FormClosingEventArgs e) =>
		{
			if (e.CloseReason == CloseReason.UserClosing)
			{
				_rgb.SaveConfig();
				e.Cancel = true;
				Hide();
			}
		};
		FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			if (Interlocked.Exchange(ref _teardownDone, 1) == 0)
			{
				_modeSyncTimer?.Stop();
				_modeSyncTimer?.Dispose();
				_rgb.DeviceLost -= OnRgbDeviceLost;
				Interlocked.Increment(ref _gcuBrightGen);
				if (_gcuBrightLock.Wait(1000))
				{
					_gcuBrightLock.Release();
					_gcuBrightLock.Dispose();
				}
				else
				{
					Logger.WriteLine("RGB brightness carrier dispose timed out waiting for an in-flight publish.");
				}
			}
		};
		Shown += async (object? _, EventArgs _) =>
		{
			SetDeviceUiEnabled(enabled: true);
			_lblStatus.ForeColor = UiVisualStyle.Muted;
			if (!_rgb.KbPowerOn)
			{
				SetStatus(Strings.LightingOff);
			}
			else
			{
				SetStatus(Strings.Connecting);
				await Task.Delay(1500);
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					SetStatus(string.Empty);
					ApplyModeSelection(force: true);
				}
				else
				{
					SetStatus(Strings.ServiceNotConnected);
				}
			}
		};
		UiVisualStyle.ApplyWindow(this);
		UiVisualStyle.ApplySection(_hidPanel);
		void SpanRow(Control ctrl)
		{
			ctrl.Dock = DockStyle.Top;
			table.Controls.Add(ctrl, 0, row);
			table.SetColumnSpan(ctrl, 2);
			row++;
		}
		void UpdateClientHeight()
		{
			if (IsDisposed || table.Height <= 0 || _updatingClientHeight)
			{
				return;
			}
			_updatingClientHeight = true;
			try
			{
				int height = table.Height;
				int num3 = Math.Max(0, Height - ClientSize.Height);
				MinimumSize = new Size(MinimumSize.Width, Math.Min(MinimumSize.Height, height + num3));
				if (ClientSize.Height != height)
				{
					ClientSize = new Size(ClientSize.Width, height);
				}
			}
			finally
			{
				_updatingClientHeight = false;
			}
		}
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		_updateClientHeight?.Invoke();
	}

	private void SetDeviceUiEnabled(bool enabled)
	{
		_hidPanel.Enabled = enabled;
	}

	private bool IsGcuKeyboardFallback()
	{
		// ⭐⭐ 2026-10-17（真机反馈「30 键盘灯光菜单里没有颜色选项」）：
		//   ⭐ 颜色行（预设 29 色板 + 自定义颜色）**只在 BuildGcuFirmwareParams() 里建** ⚠️
		//   ⭐ 而宝龙达 IsGcuConnected 恒 false ⇒ 下面两个判据都 false ⇒ 走 _hidPanel（HID 那套）
		//      ⇒ ⭐ **压根没有颜色行** ❌ —— 这就是"没有颜色选项"的根因 ✅
		//   ⇒ ⭐ 宝龙达下直接走固件参数面板：它建颜色行，且下发走 
		//      PublishGcuFirmwareEffect → SetKeyboardEffect → ⭐ TryApplyKeyboardViaBackend
		//      （那条**有宝龙达分派**，最终写 WMI 功能号 16/17/18 ✅）
		//   ⚠️ 非宝龙达 ⇒ 恒 false ⇒ ⭐ 下面判据逐位不变 ✅
		if (Program.hw?.UsesBaolongda ?? false)
		{
			return true;
		}
		// 2026-10-09（② 的配套）：这里原来用 IsSingleZoneRgb（旧判据 = KeyboardType∈{1,2}），
		// 判据收窄成「ordinal=1 才是单分区」后，4 分区机型(2)会掉进下面的策略判据、可能被切到
		// per-key HID 帧流。通道归属与灯效表判据解耦：这里改用 IsGcuZoneKeyboardPath（仍 {1,2}，
		// 与 0.291.2 逐字同行为），保证 4 分区机型照旧走 GCU/MQTT、只是 payload 换成正路分支。
		if (KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(Program.hw?.Capabilities.KeyboardType ?? 0))
		{
			return true;
		}
		// 2026-10-15（C-1 第 2 步分类 ①）：判断的是「GCU 键盘回退通道在不在」⇒ 真正需要 GCU。
		return KeyboardLightPathPolicy.ShouldUseGcuKeyboardFallback(_rgb.ControllerAvailability, _rgb.IsConnected, Program.service != null && (Program.hw?.IsGcuConnected ?? false), _hidBrightnessTookEffect);
	}

	private LightChannelSettings GcuSettings()
	{
		return LightingSettingsStore.Load("Keyboard/Ctrl", KeyboardFirmwareEffects.All[0].Id);
	}

	private string ResolveGcuEffect(LightChannelSettings stored)
	{
		if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var settings) && KeyboardFirmwareEffects.Contains(settings.Effect))
		{
			return settings.Effect;
		}
		string text = Program.hw?.KeyboardEffect ?? "";
		if (KeyboardFirmwareEffects.Contains(text))
		{
			return text;
		}
		if (!KeyboardFirmwareEffects.Contains(stored.Effect))
		{
			return KeyboardFirmwareEffects.All[0].Id;
		}
		return stored.Effect;
	}

	private void PersistGcuLightAndPublishBrightness(int light0to4)
	{
		LightChannelSettings lightChannelSettings = GcuSettings();
		LightingSettingsStore.Save("Keyboard/Ctrl", lightChannelSettings with
		{
			Light = light0to4,
			Effect = ResolveGcuEffect(lightChannelSettings)
		});
		_rgb.Brightness = KeyboardRgb.MapHardwareBrightnessLevel(light0to4);
		_rgb.QueueSaveConfig();
		PublishGcuBrightness();
	}

	private int ShowPalettePickerDialog()
	{
		int result = 0;
		using (Form pickerForm = new Form
		{
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			Text = Strings.SelectColor,
			ClientSize = new Size(D(8 * 30 + 32), D(4 * 30 + 48)),
			BackColor = UiVisualStyle.Surface
		})
		{
			TableLayoutPanel grid = new TableLayoutPanel
			{
				ColumnCount = 8,
				RowCount = 4,
				Dock = DockStyle.Fill,
				Padding = new Padding(D(10)),
				BackColor = UiVisualStyle.Surface
			};
			for (int pi = 0; pi < 29; pi++)
			{
				int idx = pi + 1;
				Button paletteBtn = new Button
				{
					BackColor = Color.FromArgb(LightChannelSettings.ManualPalette[pi]),
					Size = new Size(D(26), D(26)),
					FlatStyle = FlatStyle.Flat,
					Margin = new Padding(2),
					Cursor = Cursors.Hand
				};
				paletteBtn.Click += (object? _, EventArgs _) =>
				{
					result = idx;
					pickerForm.Close();
				};
				grid.Controls.Add(paletteBtn);
			}
			pickerForm.Controls.Add(grid);
			pickerForm.ShowDialog(this);
		}
		return result;
	}

	private void PublishGcuFirmwareEffect(LightChannelSettings settings)
	{
		// 2026-10-09（kbdctrl）：开关打开时**放宽**「HID 电源必须为开」这道门 —— 本机 Keyboard/Status 的
		// powerStatus 非 On / solution=UNKNOWN 会让 _rgb.KbPowerOn 恒 false ⇒ 老路径一条帧都不发；
		// 开关打开时允许照常调用（下游 SetKeyboardEffect 已改走 PublishKeyboardCtrlEffectAsync）。
		// ⚠️ 开关关闭（默认）时条件与改前逐字一致。
		// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：原条件 `_rgb.KbPowerOn` 把"硬件/GCU
		// 报的电源"和"LM 缓存的电源"绑成同一个布尔 —— 灯是第三方通道（Fn 热键 / 官方控制台）点亮的，
		// 硬件报 On 而 LM 缓存 false 时，两个 UI 写入点会**同时静默不发**。
		// 2026-10-12（根因①）：判据统一收在 KeyboardLightingDecision（= LightingState.ShouldWriteKeyboardLighting +
		// Program.hw 的电源快照）里，本处只消费它。
		// 现在改走 LightingState.ShouldWriteKeyboardLighting：硬件报 On ⇒ 放行；
		// ⚠️ 2026-10-12（用户拍板「键盘灯光也修复」）：判据换成 KeyboardLightingDecision（内部即
		// LightingState.DecideKeyboardLighting）⇒「用户明确关过」那条标记**只在硬件也报 Off 时才算数**
		// （硬件报 On 时它是陈旧的，由 Program.SyncKbPowerFromHardwareIfDiverged 清掉并回写成开）。
		// ⭐⭐ 2026-10-17（真机反馈「logo 和键盘开关都无效」）：⭐ 宝龙达**没有 GCU** ⇒ `IsGcuConnected` 恒 false
		//   ⇒ ⭐ 这条 HID/GCU 门控会**误挡**宝龙达的写入 ⇒ 界面点了没反应、只弹 `hid-notready` ❌
		//   ⭐ 而宝龙达走的是 **WMI 通道**（`TrySetKeyboardLightingViaBackend` → 功能号 16/17/18），
		//      **不需要 HID/GCU**，且它自己有"写后回读"的三层校验 ⇒ ⭐ 宝龙达下**跳过本门控** ✅
		//   ⚠️ 非宝龙达 ⇒ `UsesBaolongda` 恒 false ⇒ 门控与改动前**逐位相同** ✅
		bool baolongdaSkipGate = Program.hw?.UsesBaolongda ?? false;
		bool canWrite = !Program.UiAuditMode && (baolongdaSkipGate || KeyboardLightingDecision.ShouldWrite(Program.hw != null && Program.hw.IsGcuConnected, Program.hw != null && Program.hw.KeyboardPower));
		if (canWrite && Program.service != null)
		{
			MechrevoHw hw = Program.hw;
			if (hw != null && hw.IsConnected)
			{
				Color? singleColor = ((settings.Effect == "Single") ? new Color?(Color.FromArgb(settings.ColorArgb)) : ((Color?)null));
				Program.service.SetKeyboardEffect(settings.Effect, settings.Light, settings.Speed, "None", singleColor, save: true);
			}
		}
		else if (!canWrite)
		{
			// 2026-10-12（根因②：UI 不再静默）：被门控挡住时明确提示，⛔ 不再什么都不做。
			KeyboardLightingDecision.Notify(GcuKeyboardLightingRouted);
		}
	}

	/// <summary>
	/// 2026-10-12：本窗体当前这条键盘灯写入是否属于"被路由到 GCU/MQTT 回退通道"
	/// （= 提示文案该说"官方通道未启用"而不是"键盘设备未就绪"）。
	/// </summary>
	private bool GcuKeyboardLightingRouted => IsGcuKeyboardFallback();

	private void BuildGcuFirmwareParams()
	{
		SetDeviceUiEnabled(enabled: true);
		_hidPanel.Controls.Clear();
		LightChannelSettings lightChannelSettings = GcuSettings();
		_gcuSyncedEffect = lightChannelSettings.Effect;
		TableLayoutPanel table = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			ColumnCount = 2,
			Padding = new Padding(D(8)),
			BackColor = UiVisualStyle.Surface,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (_paramLabelCol > 0) ? _paramLabelCol : D(96)));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		int row = 0;
		// 2026-10-17 (真机: 4070 键盘灯亮度调不了): 宝龙达硬件亮度只有 0~3
		// (BaolongdaBrightnessFromUi 把 >3 夹到 3), 而滑块原来是 0~4 => 第4档和第3档完全相同
		// => 宝龙达下上限收到 3; 非宝龙达仍是 0~4 (逐位不变)
		// 40/50 系：亮度 0~4 五档（第三方 JiaoLong7.3 WMIRGBKeyboardBrightnessLevel.Level_4 可证）；
		// 30 系：目前只确认到 0~3；非宝龙达（GCU）仍 0~4 => 逐位不变
		bool bd40 = Program.hw?.UsesBaolongda == true
			&& (Program.hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40 || Program.hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen50);
		int brightMax = !(Program.hw?.UsesBaolongda ?? false) ? 4 : (bd40 ? 4 : 3);
		_brightSlider = new RSlider
		{
			Minimum = 0,
			Maximum = brightMax,
			Value = Math.Clamp(lightChannelSettings.Light, 0, brightMax),
			MinimumSize = new Size(D(104), D(20)),
			Height = D(24)
		};
		Label brightValueLabel = new Label
		{
			Text = Math.Clamp(lightChannelSettings.Light, 0, brightMax) + "/" + brightMax,
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(D(6), 0, 0, 0)
		};
		FlowLayoutPanel brightPanel = new FlowLayoutPanel
		{
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		brightPanel.Controls.Add(_brightSlider);
		brightPanel.Controls.Add(brightValueLabel);
		_brightSlider.ValueChanged += (object? _, EventArgs _) =>
		{
			int brightLevel = Math.Clamp(_brightSlider.Value, 0, brightMax);
			PersistGcuLightAndPublishBrightness(brightLevel);
			brightValueLabel.Text = brightLevel + "/" + brightMax;
		};
		AddRow(Strings.Brightness, brightPanel);
		if (string.Equals(lightChannelSettings.Effect, "Manual", StringComparison.Ordinal))
		{
			RSlider intervalSlider = new RSlider
			{
				Minimum = 2,
				Maximum = 10,
				Value = Math.Clamp(lightChannelSettings.Speed, 2, 10),
				MinimumSize = new Size(D(180), D(20)),
				Height = D(24)
			};
			Label intervalLabel = new Label
			{
				Text = Math.Clamp(lightChannelSettings.Speed, 2, 10) + "/10",
				AutoSize = true,
				ForeColor = UiVisualStyle.Muted,
				Margin = new Padding(D(6), 0, 0, 0)
			};
			FlowLayoutPanel intervalPanel = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink
			};
			intervalPanel.Controls.Add(intervalSlider);
			intervalPanel.Controls.Add(intervalLabel);
			intervalSlider.ValueChanged += (object? _, EventArgs _) =>
			{
				int interval = Math.Clamp(intervalSlider.Value, 2, 10);
				LightChannelSettings settings = GcuSettings()with
				{
					Speed = interval
				};
				LightingSettingsStore.Save("Keyboard/Ctrl", settings);
				PublishGcuFirmwareEffect(settings);
				intervalLabel.Text = interval + "/10";
			};
			AddRow(Strings.ColorInterval, intervalPanel);
		}
		if (string.Equals(lightChannelSettings.Effect, "Manual", StringComparison.Ordinal))
		{
			int[] manualIndexes = GcuSettings().ManualIndexes ?? LightChannelSettings.DefaultManualIndexes;
			FlowLayoutPanel manualColorPanel = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink
			};
			RColorButton[] manualColorButtons = new RColorButton[6];
			for (int i = 0; i < 6; i++)
			{
				int index = i;
				manualColorButtons[i] = new RColorButton
				{
					Size = new Size(D(30), D(24)),
					BackColor = UiVisualStyle.Input,
					BorderColor = UiVisualStyle.Border,
					SwatchColor = Color.FromArgb(LightChannelSettings.ManualPalette[Math.Clamp(manualIndexes[index], 1, 29) - 1]),
					Cursor = Cursors.Hand
				};
				manualColorButtons[i].Click += (object? _, EventArgs _) =>
				{
					int selected = ShowPalettePickerDialog();
					if (selected > 0)
					{
						LightChannelSettings settings = GcuSettings();
						int[] newIndexes = (int[])(settings.ManualIndexes ?? LightChannelSettings.DefaultManualIndexes).Clone();
						newIndexes[index] = selected;
						manualIndexes[index] = selected;
						manualColorButtons[index].SwatchColor = Color.FromArgb(LightChannelSettings.ManualPalette[Math.Clamp(selected, 1, 29) - 1]);
						LightChannelSettings saved = settings with { ManualIndexes = newIndexes };
						LightingSettingsStore.Save("Keyboard/Ctrl", saved);
						PublishGcuFirmwareEffect(saved);
					}
				};
				manualColorPanel.Controls.Add(manualColorButtons[i]);
			}
			AddRow(Strings.SwitchColor, manualColorPanel);
		}
		else
		{
			if (string.Equals(lightChannelSettings.Effect, "Breathing", StringComparison.Ordinal))
			{
				if (KeyboardFirmwareEffects.IsSingleZoneRgb(Program.hw?.Capabilities.KeyboardType ?? 0))
				{
					int breathIdx = Math.Clamp(lightChannelSettings.BreathingIndex, 1, 4);
					FlowLayoutPanel breathPanel = new FlowLayoutPanel
					{
						FlowDirection = FlowDirection.LeftToRight,
						WrapContents = false,
						AutoSize = true,
						AutoSizeMode = AutoSizeMode.GrowAndShrink
					};
					RColorButton[] breathButtons = new RColorButton[4];
					for (int bi = 0; bi < 4; bi++)
					{
						int bIdx = bi + 1;
						breathButtons[bi] = new RColorButton
						{
							Size = new Size(D(30), D(24)),
							BackColor = UiVisualStyle.Input,
							BorderColor = (bIdx == breathIdx) ? UiVisualStyle.Accent : UiVisualStyle.Border,
							SwatchColor = Color.FromArgb(LightChannelSettings.BreathingPalette[bi]),
							Cursor = Cursors.Hand
						};
						int captured = bIdx;
						breathButtons[bi].Click += (object? _, EventArgs _) =>
						{
							LightChannelSettings settings = GcuSettings() with { BreathingIndex = captured };
							LightingSettingsStore.Save("Keyboard/Ctrl", settings);
							for (int k = 0; k < 4; k++)
							{
								breathButtons[k].BorderColor = (k + 1 == captured) ? UiVisualStyle.Accent : UiVisualStyle.Border;
							}
							PublishGcuFirmwareEffect(settings);
						};
						breathPanel.Controls.Add(breathButtons[bi]);
					}
					AddRow(Strings.BreathColor, breathPanel);
				}
				else
				{
					RColorButton breathColorBtn = new RColorButton
					{
						Size = new Size(D(46), D(24)),
						BackColor = UiVisualStyle.Input,
						BorderColor = UiVisualStyle.Border,
						SwatchColor = Color.FromArgb(lightChannelSettings.ColorArgb),
						Cursor = Cursors.Hand
					};
					breathColorBtn.Click += (object? _, EventArgs _) =>
					{
						RColorPicker rColorPicker = new RColorPicker(Color.FromArgb(GcuSettings().ColorArgb));
						rColorPicker.ColorChanged += (Color c) =>
						{
							LightChannelSettings settings = GcuSettings() with { ColorArgb = c.ToArgb() };
							LightingSettingsStore.Save("Keyboard/Ctrl", settings);
							breathColorBtn.SwatchColor = c;
							PublishGcuFirmwareEffect(settings);
						};
						rColorPicker.ShowDialog(this);
					};
					FlowLayoutPanel breathPanel = new FlowLayoutPanel
					{
						FlowDirection = FlowDirection.LeftToRight,
						WrapContents = false,
						AutoSize = true,
						AutoSizeMode = AutoSizeMode.GrowAndShrink
					};
					breathPanel.Controls.Add(breathColorBtn);
					AddRow(Strings.BreathColor, breathPanel);
				}
			}
			else if (!(KeyboardFirmwareEffects.IsSingleZoneRgb(Program.hw?.Capabilities.KeyboardType ?? 0)
				&& string.Equals(lightChannelSettings.Effect, "Rainbow", StringComparison.Ordinal)))
			{
				// 2026-10-15（用户原话「因为彩虹灯效不能编辑颜色」「先就改单分区的」）：
				// 单分区键盘的**彩虹**灯效由固件按渐变自行取色，用户改不了颜色 ⇒ 这一档下
				// 「预设颜色」29 色板与「自定义颜色」两个入口都无意义，整块不建。
				// ⚠️ 只对**单分区**生效（IsSingleZoneRgb）；四分区机型保持原样（其彩虹行为未验证，不动）。
				// 恢复由 BuildGcuFirmwareParams() 重建保证：切离彩虹 ⇒ 两行自动回来（见 RgbForm :206-207）。
					RColorButton customColorBtn = new RColorButton
				{
					Size = new Size(D(46), D(24)),
					// 2026-10-09（UI 反馈①，仅夜间）：「自定义单色」那颗若正是当前选中（UsePresetColor=false），
					// 面也给同款浅色填充（与它的色块同色系）；日间/未选中恒为 Input，与原值逐像素相同。
					BackColor = ((lightChannelSettings.UsePresetColor || !UiVisualStyle.IsNightMode) ? UiVisualStyle.Input : UiVisualStyle.SwatchSelectionFill(Color.FromArgb(lightChannelSettings.ColorArgb))),
					BorderColor = (lightChannelSettings.UsePresetColor ? UiVisualStyle.Border : UiVisualStyle.Accent),
					SwatchColor = Color.FromArgb(lightChannelSettings.ColorArgb),
					Cursor = Cursors.Hand
				};
				// 2026-10-04：预设色板 1-29 色块（点选 → MonochromeIndex 直发索引，不走 ColorBuffer）
				FlowLayoutPanel presetPanel = new FlowLayoutPanel
				{
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = true,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					MaximumSize = new Size(D(268), 0),
					Margin = Padding.Empty
				};
				RColorButton[] presetButtons = new RColorButton[29];
				int activePreset = (lightChannelSettings.UsePresetColor ? Math.Clamp(lightChannelSettings.MonochromeIndex, 1, 29) : 0);
				// 2026-10-09（UI 反馈①，仅夜间；用户原话「预设灯光 高亮框不明显可以增加浅色填充」）：选中那一格的
				// 浅色填充＝与该色块自身颜色协调的浅调（朝白 42%）。日间返回 Input（＝原值）⇒ 日间像素不变。
				Color SwatchFill(int index)
				{
					return UiVisualStyle.IsNightMode ? UiVisualStyle.SwatchSelectionFill(Color.FromArgb(LightChannelSettings.ManualPalette[index - 1])) : UiVisualStyle.Input;
				}
				for (int pi = 0; pi < 29; pi++)
				{
					int pidx = pi + 1;
					presetButtons[pi] = new RColorButton
					{
						Size = new Size(D(22), D(22)),
						BackColor = ((pidx == activePreset) ? SwatchFill(pidx) : UiVisualStyle.Input),
						BorderColor = ((pidx == activePreset) ? UiVisualStyle.Accent : UiVisualStyle.Border),
						SwatchColor = Color.FromArgb(LightChannelSettings.ManualPalette[pi]),
						Cursor = Cursors.Hand
					};
					presetButtons[pi].Click += (object? _, EventArgs _) =>
					{
						LightChannelSettings presetSettings = GcuSettings() with
						{
							UsePresetColor = true,
							MonochromeIndex = pidx
						};
						LightingSettingsStore.Save("Keyboard/Ctrl", presetSettings);
						// 2026-10-09（UI 反馈①）：这里原来只改 BorderColor（而 RButton.BorderColor 的 setter
						// 当时**没有 Invalidate**）⇒ 上一颗的高亮留在屏幕上不消失。现在 ①setter 已补 Invalidate、
						// ②这一轮把 29 颗的**边色 + 面填充**一起重设 ⇒ 无论从哪一颗切到哪一颗，全阵列恒定只有一颗高亮。
						for (int k = 0; k < 29; k++)
						{
							presetButtons[k].BorderColor = ((k + 1 == pidx) ? UiVisualStyle.Accent : UiVisualStyle.Border);
							presetButtons[k].BackColor = ((k + 1 == pidx) ? SwatchFill(k + 1) : UiVisualStyle.Input);
						}
						customColorBtn.BorderColor = UiVisualStyle.Border;
						customColorBtn.BackColor = UiVisualStyle.Input;
						if (presetSettings.Effect == "Single")
						{
							PublishGcuFirmwareEffect(presetSettings);
						}
					};
					presetPanel.Controls.Add(presetButtons[pi]);
				}
				AddRow(Strings.PresetColor, presetPanel);
				customColorBtn.Click += (object? _, EventArgs _) =>
				{
					RColorPicker rColorPicker = new RColorPicker(Color.FromArgb(GcuSettings().ColorArgb));
					rColorPicker.ColorChanged += (Color c) =>
					{
						LightChannelSettings settings = GcuSettings()with
						{
							ColorArgb = c.ToArgb(),
							UsePresetColor = false
						};
						LightingSettingsStore.Save("Keyboard/Ctrl", settings);
						customColorBtn.SwatchColor = c;
						customColorBtn.BorderColor = UiVisualStyle.Accent;
						// 2026-10-09（UI 反馈①）：换成自定义色时，29 颗预设的**边色 + 浅填充**一并清掉
						//（原来只清边色 ⇒ 预设那格的高亮会赖着不走），保证同一时刻只有一颗高亮。
						customColorBtn.BackColor = (UiVisualStyle.IsNightMode ? UiVisualStyle.SwatchSelectionFill(c) : UiVisualStyle.Input);
						for (int k = 0; k < 29; k++)
						{
							presetButtons[k].BorderColor = UiVisualStyle.Border;
							presetButtons[k].BackColor = UiVisualStyle.Input;
						}
						if (settings.Effect == "Single")
						{
							PublishGcuFirmwareEffect(settings);
						}
					};
					rColorPicker.ShowDialog(this);
				};
				AddRow(Strings.SingleColor, customColorBtn);
			}
		}
		_hidPanel.Controls.Add(table);
		FitParameterTable(_hidPanel);
		UiVisualStyle.ApplySection(_hidPanel);
		void AddRow(string label, Control ctrl)
		{
			_paramLabels.Add(label);
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(new Label
			{
				Text = label,
				ForeColor = UiVisualStyle.Text,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill,
				Margin = Padding.Empty
			}, 0, row);
			if ((ctrl is ComboBox || ctrl is TrackBar || ctrl is RSlider) ? true : false)
			{
				ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				ctrl.Margin = new Padding(0, D(2), 0, D(2));
			}
			else if (ctrl is Button)
			{
				ctrl.Anchor = AnchorStyles.Left;
				ctrl.Margin = new Padding(0, D(2), 0, D(2));
			}
			else
			{
				ctrl.Dock = DockStyle.Fill;
			}
			table.Controls.Add(ctrl, 1, row);
			row++;
		}
	}

	private void PublishGcuBrightness()
	{
		// 2026-10-12（根因①：门控背离）：与 PublishGcuFirmwareEffect 同一处口径 —— 硬件报 On 且用户
		// 没明确关过 ⇒ 放行；用户明确关过 ⇒ 挡住并**明确提示**（⛔ 不再无声 return）。
		// 2026-10-09（kbdctrl）：开关打开时照旧放宽（下游 SetKeyboardBrightnessPreservingEffect 走新链路）。
		_gcuBrightGen++;
		// ⭐⭐ 2026-10-17：同 PublishGcuFirmwareEffect 那处 —— 宝龙达（无 GCU）跳过这条 HID/GCU 门控 ✅
		if (Program.UiAuditMode || (!(Program.hw?.UsesBaolongda ?? false) && !KeyboardLightingDecision.ShouldWrite(Program.hw != null && Program.hw.IsGcuConnected, Program.hw != null && Program.hw.KeyboardPower)))
		{
			KeyboardLightingDecision.Notify(GcuKeyboardLightingRouted);
			return;
		}
		int gen = ++_gcuBrightGen;
		Task.Run(async () =>
		{
			await Task.Delay(150);
			if (gen == Volatile.Read(in _gcuBrightGen))
			{
				await SendGcuBrightnessAsync(gen);
			}
		});
	}

	private async Task SendGcuBrightnessAsync(int gen)
	{
		if (Program.service == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected || !IsGcuKeyboardFallback())
		{
			return;
		}
		bool lockTaken = false;
		try
		{
			await _gcuBrightLock.WaitAsync();
			lockTaken = true;
			if (gen == Volatile.Read(in _gcuBrightGen))
			{
				await Program.service.SetKeyboardBrightnessPreservingEffect(KeyboardRgb.MapSoftwareBrightnessToHardwareLevel(_rgb.Brightness));
			}
		}
		finally
		{
			if (lockTaken)
			{
				_gcuBrightLock.Release();
			}
		}
	}

	private void OnRgbDeviceLost()
	{
		try
		{
			if (!IsDisposed)
			{
				if (InvokeRequired)
				{
					BeginInvoke(OnRgbDeviceLost);
					return;
				}
				SetDeviceUiEnabled(enabled: false);
				SetStatus(_rgb.LastError ?? Strings.KeyboardRgbNotConnected);
				_lblStatus.ForeColor = UiVisualStyle.Danger;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB device-lost UI update failed: " + ex.Message);
		}
	}

	private async Task SyncDeviceCloseTimerAsync()
	{
		int generation = Interlocked.Increment(ref _closeTimerCommandGen);
		await Task.Delay(150);
		if (generation != Volatile.Read(in _closeTimerCommandGen) || Program.service == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			return;
		}
		bool flag = await Program.service.SwitchCloseTimer(0);
		if ((generation != Volatile.Read(in _closeTimerCommandGen)) | flag)
		{
			return;
		}
		try
		{
			if (!IsDisposed)
			{
				SetStatus(Strings.SleepSavedRetryOnReconnect);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB close-timer status update failed: " + ex.Message);
		}
	}

	private void ApplyModeSelection(bool force = false)
	{
		(int, string)[] array = VisibleHidEffects(Program.hw?.Capabilities.KeyboardType ?? 0);
		int num = Array.FindIndex(array, ((int Mode, string Name) e) => e.Mode == _rgb.KbHidMode);
		if (num < 0)
		{
			num = 0;
		}
		_rgb.KbPowerOn = true;
		Program.NotifyLightingUserIntent();
		int gen = ++_applyGen;
		(int Mode, string Name) hid = array[num];
		_activeHidMode = hid.Mode;
		_rgb.KbHidMode = hid.Mode;
		Program.MarkKeyboardCustomStatusBaseline();
		BuildHidParams();
		if (IsGcuKeyboardFallback())
		{
			BuildGcuFirmwareParams();
			SetStatus(Strings.KeyboardControllerUnsupported);
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected && !Program.hw.KeyboardPower)
				{
					Task.Run(() => Program.service.SetLightPower("Keyboard/Ctrl", on: true));
				}
			}
			SyncDeviceCloseTimerAsync();
			_rgb.QueueSaveConfig();
			return;
		}
		if (!_rgb.IsConnected)
		{
			SetStatus(Strings.ConnectingKeyboardRgb);
			Task.Run(async () =>
			{
				if (_rgb.ControllerAvailability == FeatureAvailability.Unknown)
				{
					await _rgb.EnsureHidReadyAsync().ConfigureAwait(continueOnCapturedContext: false);
					if (_rgb.ControllerAvailability != FeatureAvailability.Unknown)
					{
						Program.settingsForm?.RefreshDeviceCapabilities();
					}
				}
				bool ok = _rgb.IsConnected || (_rgb.ControllerAvailability != FeatureAvailability.Unsupported && _rgb.Connect());
				try
				{
					Invoke(() =>
					{
						SetStatus(ok ? string.Empty : string.Format(Strings.ConnectFailedColon, _rgb.LastError));
						if (ok && gen == _applyGen)
						{
							Task.Run(() =>
							{
								_rgb.StartMode(hid.Mode);
								Program.MarkKeyboardCustomStatusBaseline();
							});
						}
						else if (!ok && gen == _applyGen)
						{
							// 2026-10-12（根因②：UI 不再静默 + HID 空窗）：句柄没连上时明确提示，
							// 并**补一次**延时重连重发 —— 本机实测从开机到 HID 句柄就绪存在约 50 秒空窗。
							KeyboardLightingDecision.Notify(gcuKeyboardRouted: false);
							RetryHidApplyAfterDelay(hid.Mode, gen);
						}
					});
				}
				catch (Exception ex)
				{
					Logger.WriteLine("RGB connect status UI update failed: " + ex.GetType().Name + " " + ex.Message);
				}
			});
		}
		else
		{
			Task.Run(async () =>
			{
				if (Program.service != null)
				{
					MechrevoHw hw2 = Program.hw;
					if (hw2 != null && hw2.IsConnected && !Program.hw.KeyboardPower)
					{
						await Program.service.SetLightPower("Keyboard/Ctrl", on: true);
						if (gen != _applyGen)
						{
							return;
						}
					}
				}
				_rgb.ReInitCustomMode();
				if (gen == _applyGen)
				{
					_rgb.StartMode(hid.Mode);
					Program.MarkKeyboardCustomStatusBaseline();
				}
			});
			SetStatus(string.Empty);
		}
		SyncDeviceCloseTimerAsync();
		_rgb.QueueSaveConfig();
	}

	/// <summary>
	/// 2026-10-12（根因②）：HID 句柄空窗补发一次 —— 与用户点开关时
	/// <c>SettingsForm</c> 那条「先 EnsureHidReadyAsync 再 Connect」同口径。
	/// 延迟只在后台线程睡（⛔ 不阻塞 UI）；<paramref name="gen"/> 与当前帧代数不一致就放弃，不覆盖用户后续操作。
	/// </summary>
	private void RetryHidApplyAfterDelay(int mode, int gen)
	{
		Task.Run(async () =>
		{
			try
			{
				if (_rgb.IsConnected || _rgb.ControllerAvailability == FeatureAvailability.Unsupported)
				{
					return;
				}
				await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
				if (_rgb.IsConnected || gen != _applyGen || _rgb.ControllerAvailability == FeatureAvailability.Unsupported)
				{
					return;
				}
				if (_rgb.ControllerAvailability == FeatureAvailability.Unknown)
				{
					await _rgb.EnsureHidReadyAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				if (!_rgb.IsConnected && !_rgb.Connect())
				{
					Logger.WriteLine("RGB HID 空窗补发失败：句柄仍不可用，已提示用户稍后重试");
					KeyboardLightingDecision.Notify(gcuKeyboardRouted: false);
					return;
				}
				if (gen != _applyGen)
				{
					return;
				}
				_rgb.ReInitCustomMode();
				_rgb.StartMode(mode);
				Program.MarkKeyboardCustomStatusBaseline();
				Logger.WriteLine("RGB HID 空窗补发成功：mode=" + mode);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("RGB HID 空窗补发异常: " + ex.GetType().Name + " " + ex.Message);
			}
		});
	}

	private void BuildHidParams()
	{
		_hidPanel.Controls.Clear();
		TableLayoutPanel table = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			ColumnCount = 2,
			Padding = new Padding(D(8)),
			BackColor = UiVisualStyle.Surface,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (_paramLabelCol > 0) ? _paramLabelCol : D(96)));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		table.RowStyles.Clear();
		int row = 0;
		_brightSlider = Slider(0, 100, _rgb.Brightness, (int v) =>
		{
			_rgb.Brightness = v;
			if (_rgb.IsConnected)
			{
				_hidBrightnessTookEffect = _rgb.ApplyBrightnessToDevice();
			}
			if (IsGcuKeyboardFallback())
			{
				PersistGcuLightAndPublishBrightness(KeyboardRgb.MapSoftwareBrightnessToHardwareLevel(v));
			}
		});
		AddRow(Strings.Brightness, _brightSlider);
		RComboBox fps = new RComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		string[] array = new string[4] { "15 FPS", "30 FPS", "45 FPS", "60 FPS" };
		foreach (string item in array)
		{
			fps.Items.Add(item);
		}
		int num2 = Array.IndexOf(new int[4] { 15, 30, 45, 60 }, _rgb.TargetFps);
		fps.SelectedIndex = ((num2 < 0) ? 1 : num2);
		fps.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			_rgb.TargetFps = (new int[4] { 15, 30, 45, 60 })[fps.SelectedIndex];
			_rgb.QueueSaveConfig();
		};
		AddRow(Strings.FrameRate, fps);
		switch (_activeHidMode)
		{
		case 1:
			AddRow(Strings.WaveAngle, Slider(0, 360, _rgb.WaveAngle, (int v) =>
			{
				_rgb.WaveAngle = v;
			}));
			AddRow(Strings.Speed, Slider(1, 20, _rgb.WaveSpeed, (int v) =>
			{
				_rgb.WaveSpeed = v;
			}));
			break;
		case 9:
		{
			int count = Math.Clamp(_rgb.StaticZones, 1, 10);
			Button[] swatches = Enumerable.Range(0, count).Select((int i) =>
			{
				int idx = i;
				return Swatch(_rgb.StaticColors[i], (Color c) =>
				{
					_rgb.StaticColors[idx] = c;
				});
			}).ToArray();
			AddColorRow(Strings.ZoneColors, swatches);
			AddRow(Strings.Layout, MakeStaticLayoutCombo());
			RComboBox ctrl = TierCombo(1, 10, _rgb.StaticZones, (int v) =>
			{
				_rgb.StaticZones = v;
				BuildHidParams();
			});
			AddRow(Strings.ZoneCount, ctrl);
			break;
		}
		case 0:
			AddRow(Strings.Color, Swatch(_rgb.BreathColor, (Color c) =>
			{
				_rgb.BreathColor = c;
			}));
			AddRow(Strings.Speed, Slider(1, 30, _rgb.BreathSpeed, (int v) =>
			{
				_rgb.BreathSpeed = v;
			}));
			AddRow(Strings.Smooth, Slider(2, 100, _rgb.BreathSmooth, (int v) =>
			{
				_rgb.BreathSmooth = v;
			}));
			break;
		case 2:
			AddRow(Strings.Speed, Slider(1, 20, _rgb.SparkleSpeed, (int v) =>
			{
				_rgb.SparkleSpeed = v;
			}));
			AddRow(Strings.Density, Slider(1, 20, _rgb.SparkleDensity, (int v) =>
			{
				_rgb.SparkleDensity = v;
			}));
			break;
		case 3:
			AddRow(Strings.Color, Swatch(_rgb.ReactiveColor, (Color c) =>
			{
				_rgb.ReactiveColor = c;
			}));
			AddRow("", Chk(Strings.RandomColor, _rgb.ReactiveRandom, (bool v) =>
			{
				_rgb.ReactiveRandom = v;
			}));
			AddRow(Strings.Duration, Slider(5, 60, _rgb.ReactiveDuration, (int v) =>
			{
				_rgb.ReactiveDuration = v;
			}));
			break;
		case 4:
			AddRow("", Chk(Strings.ReverseRotation, _rgb.WheelReverse, (bool v) =>
			{
				_rgb.WheelReverse = v;
			}));
			AddRow(Strings.Speed, Slider(1, 20, _rgb.WheelSpeed, (int v) =>
			{
				_rgb.WheelSpeed = v;
			}));
			break;
		case 5:
			AddRow(Strings.Speed, Slider(1, 20, _rgb.LightningSpeed, (int v) =>
			{
				_rgb.LightningSpeed = v;
			}));
			AddRow(Strings.Smooth, Slider(1, 40, _rgb.LightningSmooth, (int v) =>
			{
				_rgb.LightningSmooth = v;
			}));
			AddRow(Strings.Width, Slider(1, 4, _rgb.LightningWidth, (int v) =>
			{
				_rgb.LightningWidth = v;
			}));
			AddRow(Strings.Concurrent, Slider(1, 4, _rgb.LightningConcurrent, (int v) =>
			{
				_rgb.LightningConcurrent = v;
			}));
			break;
		case 6:
			AddRow(Strings.Speed, Slider(1, 20, _rgb.FlameSpeed, (int v) =>
			{
				_rgb.FlameSpeed = v;
			}));
			AddRow(Strings.Smooth, Slider(1, 40, _rgb.FlameSmooth, (int v) =>
			{
				_rgb.FlameSmooth = v;
			}));
			break;
		case 7:
		{
			AddRow(Strings.Speed, Slider(1, 20, _rgb.RainSpeed, (int v) =>
			{
				_rgb.RainSpeed = v;
			}));
			AddRow(Strings.Density, TierCombo(1, 5, _rgb.RainDensity, (int v) =>
			{
				_rgb.RainDensity = v;
			}));
			AddRow(Strings.ColorCount, TierCombo(1, 5, _rgb.RainColorCount, (int v) =>
			{
				_rgb.RainColorCount = v;
			}));
			Button[] swatches = Enumerable.Range(0, 5).Select((int i) =>
			{
				int idx = i;
				return Swatch(_rgb.RainColors[i], (Color c) =>
				{
					_rgb.RainColors[idx] = c;
				});
			}).ToArray();
			AddColorRow(Strings.Color, swatches);
			break;
		}
		case 8:
		{
			AddRow(Strings.Speed, Slider(1, 20, _rgb.MatrixSpeed, (int v) =>
			{
				_rgb.MatrixSpeed = v;
			}));
			RComboBox style = new RComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			ComboBox.ObjectCollection items = style.Items;
			object[] items2 = new string[4] { Strings.MatrixFlow, Strings.MatrixRain, Strings.MatrixLaser, Strings.MatrixRipple };
			items.AddRange(items2);
			style.SelectedIndex = _rgb.MatrixStyle;
			style.SelectedIndexChanged += (object? _, EventArgs _) =>
			{
				_rgb.MatrixStyle = style.SelectedIndex;
				_rgb.QueueSaveConfig();
			};
			AddRow(Strings.Style, style);
			AddRow(Strings.Density, TierCombo(1, 5, _rgb.MatrixDensity, (int v) =>
			{
				_rgb.MatrixDensity = v;
			}));
			AddRow(Strings.ColorCount, TierCombo(1, 3, _rgb.MatrixColorCount, (int v) =>
			{
				_rgb.MatrixColorCount = v;
			}));
			Button[] swatches = Enumerable.Range(0, 3).Select((int i) =>
			{
				int idx = i;
				return Swatch(_rgb.MatrixColors[i], (Color c) =>
				{
					_rgb.MatrixColors[idx] = c;
				});
			}).ToArray();
			AddColorRow(Strings.Color, swatches);
			break;
		}
		}
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		_hidPanel.Controls.Add(table);
		FitParameterTable(_hidPanel);
		UiVisualStyle.ApplySection(_hidPanel);
		void AddRow(string label, Control control)
		{
			_paramLabels.Add(label);
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(new Label
			{
				Text = label,
				ForeColor = UiVisualStyle.Text,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill,
				Margin = Padding.Empty
			}, 0, row);
			if ((control is ComboBox || control is TrackBar) ? true : false)
			{
				control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				control.Margin = new Padding(0, D(2), 0, D(2));
			}
			else if (control is Button)
			{
				control.Anchor = AnchorStyles.Left;
				control.Margin = new Padding(0, D(2), 0, D(2));
			}
			else
			{
				control.Dock = DockStyle.Fill;
			}
			table.Controls.Add(control, 1, row);
			row++;
		}
		void AddColorRow(string label, Button[] swatches)
		{
			FlowLayoutPanel panel = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = Color.Transparent
			};
			foreach (Button b in swatches)
			{
				panel.Controls.Add(b);
			}
			AddRow(label, panel);
		}
		CheckBox Chk(string text, bool state, Action<bool> set)
		{
			RCheckBox c = new RCheckBox
			{
				Text = text,
				Checked = state,
				ForeColor = UiVisualStyle.Text,
				AutoSize = true
			};
			c.CheckedChanged += (object? _, EventArgs _) =>
			{
				set(c.Checked);
				_rgb.QueueSaveConfig();
			};
			return c;
		}
		RSlider Slider(int min, int max, int value, Action<int> set)
		{
			RSlider s = new RSlider
			{
				Minimum = min,
				Maximum = max,
				Value = value,
				MinimumSize = new Size(D(104), D(20)),
				Height = D(24)
			};
			s.ValueChanged += (object? _, EventArgs _) =>
			{
				set(s.Value);
				_rgb.QueueSaveConfig();
			};
			return s;
		}
		RColorButton Swatch(Color color, Action<Color> apply)
		{
			RColorButton b = new RColorButton
			{
				Size = new Size(D(46), D(24)),
				BackColor = UiVisualStyle.Input,
				BorderColor = UiVisualStyle.Border,
				SwatchColor = color,
				Cursor = Cursors.Hand,
				Tag = "color-swatch",
				Margin = new Padding(D(2))
			};
			b.Click += (object? _, EventArgs _) =>
			{
				RColorPicker rColorPicker = new RColorPicker(color);
				rColorPicker.ColorChanged += (Color c) =>
				{
					apply(c);
					b.SwatchColor = c;
					_rgb.QueueSaveConfig();
				};
				rColorPicker.ShowDialog(this);
			};
			return b;
		}
		RComboBox TierCombo(int min, int max, int value, Action<int> set)
		{
			RComboBox c = new RComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			for (int i = min; i <= max; i++)
			{
				c.Items.Add(i.ToString());
			}
			c.SelectedIndex = Math.Clamp(value - min, 0, max - min);
			c.SelectedIndexChanged += (object? _, EventArgs _) =>
			{
				set(c.SelectedIndex + min);
				_rgb.QueueSaveConfig();
			};
			return c;
		}
	}

	private static void FitParameterTable(Panel panel)
	{
		TableLayoutPanel tableLayoutPanel = panel.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
		if (tableLayoutPanel != null)
		{
			int width = (tableLayoutPanel.Width = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal));
			tableLayoutPanel.MinimumSize = new Size(width, 0);
			tableLayoutPanel.MaximumSize = new Size(width, 0);
		}
	}

	private RComboBox MakeStaticLayoutCombo()
	{
		RComboBox c = new RComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = D(100)
		};
		ComboBox.ObjectCollection items = c.Items;
		object[] items2 = new string[2] { Strings.HorizontalZones, Strings.VerticalZones };
		items.AddRange(items2);
		c.SelectedIndex = _rgb.StaticLayout;
		c.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			_rgb.StaticLayout = c.SelectedIndex;
			_rgb.QueueSaveConfig();
		};
		return c;
	}
}
