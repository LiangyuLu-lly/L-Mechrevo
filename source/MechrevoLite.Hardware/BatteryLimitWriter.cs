using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MechrevoLite.Hardware;

/// <summary>
/// 宝龙达（MRID6 / BM5235）充电阈值：按参考实现的收发顺序操作 EC。
/// <para>启用顺序：身份/AC/状态校验 → 必要时清理原生残留状态 → 写 0x8153=high、0x8154=low（回读一致）
/// → 写 0x8151=0x0D → 触发 0x8150=0x10 → 轮询 0x8150 归 0 → 校验 0x8152=0x55、0x83C0=0x48、
/// 0x83A1/0x83A2 与写入一致、0x83A0 已稳定。</para>
/// <para>关闭顺序：0x8151=0x0F → 触发 → 轮询 → 校验 0x8152=0x55、0x83C0=0x00。</para>
/// <para>失败不重试、不做清理写；只写 0x8150/0x8151/0x8153/0x8154 四个白名单地址。</para>
/// </summary>
internal static class BatteryLimitWriter
{
	internal const int HighRegister = 0x83A1;

	internal const int LowRegister = 0x83A2;

	internal const int ModeRegister = 0x83C0;

	internal const int ResultRegister = 0x8152;

	internal const int StateRegister = 0x8370;

	internal const int SubstateRegister = 0x83A0;

	internal const int FlagsRegister = 0x8394;

	internal const int StateFlagsRegister = 0x8395;

	internal const int SocRegister = 0x847C;

	internal const int ModeActive = 0x48;

	internal const int ModeNativeState = 0x32;

	internal const int TimeoutMs = 3000;

	internal static readonly int[] NormalStates = { 0x01, 0x02, 0x04, 0x05, 0x10, 0x11, 0x12, 0x20 };

	/// <summary>只读当前常驻状态：高/低/模式。</summary>
	internal static bool TryReadState(out int high, out int low, out int mode, out string detail)
	{
		high = -1;
		low = -1;
		mode = -1;
		if (!BatteryLimitPlatform.IsBaolongdaMachine())
		{
			detail = "非宝龙达机型 ⇒ 不读 EC（0 读 0 写）";
			return false;
		}
		bool highOk = ReadEc(HighRegister, out high);
		bool lowOk = ReadEc(LowRegister, out low);
		bool modeOk = ReadEc(ModeRegister, out mode);
		detail = "0x83A1=" + Fmt(high) + " · 0x83A2=" + Fmt(low) + " · 0x83C0=" + Fmt(mode);
		return highOk && lowOk && modeOk;
	}

	/// <summary>启用限充（默认要求 SOC &lt; high）。</summary>
	internal static bool TryEnable(int low, int high, out string detail)
	{
		return TryEnable(low, high, allowInitialDischarge: false, out detail);
	}

	/// <summary>
	/// 启用限充；<paramref name="allowInitialDischarge" /> = true 时允许 SOC ≥ high
	/// （初始化会先放电到阈值附近）。
	/// </summary>
	internal static bool TryEnable(int low, int high, bool allowInitialDischarge, out string detail)
	{
		StringBuilder log = new StringBuilder();
		try
		{
			if (!BatteryLimitPlatform.IsBaolongdaMachine())
			{
				detail = "非宝龙达机型 ⇒ 不执行限充写入（0 读 0 写）";
				return false;
			}
			if (!(low > 0 && low < high && high <= 100))
			{
				detail = "参数非法：要求 0 < low < high <= 100（收到 low=" + low + " high=" + high + "）";
				return false;
			}
			log.AppendLine("参数：low=" + low + "% · high=" + high + "%");

			if (!EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string idNote))
			{
				detail = log + "⛔ EC 身份未通过：" + idNote;
				return false;
			}
			log.AppendLine("EC 身份：" + idNote);

			if (PowerState() != PowerLineStatus.Online)
			{
				detail = log + "⛔ AC 未在线 ⇒ 不启用";
				return false;
			}
			log.AppendLine("AC = Online");

			Snapshot snapshot = ReadSnapshot();
			log.AppendLine(snapshot.Describe());
			if (!ValidateSnapshot(snapshot, log) || !GuardStart(snapshot, high, allowInitialDischarge, log))
			{
				detail = log.ToString();
				return false;
			}

			// 同一套配置且已稳定 ⇒ 复用，不重置迟滞
			// 2026-10-21：高电量 + 允许初始放电 ⇒ 必须重新触发一次 enable（固件才会放电到阈值）；
			// 复用分支会跳过触发 —— 表现就是 100% 开着限充却一直不放电。
			int socNow = Math.Max(snapshot.Soc,
				(int)Math.Round(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100.0));
			bool forceInitialDischarge = allowInitialDischarge && socNow >= high;
			if (!forceInitialDischarge && snapshot.Mode == ModeActive && snapshot.High == high && snapshot.Low == low
				&& (snapshot.Substate == 2 || snapshot.Substate == 3))
			{
				detail = log + "已是目标配置且已生效 ⇒ 无需重写";
				return true;
			}

			if (snapshot.Mode != 0)
			{
				log.AppendLine("当前非正常充电策略 ⇒ 先关闭（0x8151=0x0F → 触发 → 校验 mode=0）");
				if (!DisableCore(log))
				{
					detail = log.ToString();
					return false;
				}
				snapshot = ReadSnapshot();
				log.AppendLine("关闭后：" + snapshot.Describe());
			}
			else if (!WaitNormal(requireClean: false, log))
			{
				detail = log.ToString();
				return false;
			}

			snapshot = ReadSnapshot();
			if (!NativeStateClean(snapshot))
			{
				log.AppendLine("原生初始化状态有残留 ⇒ 按参考顺序清理（10/0E → 触发 → 停止态确认 → 10/0F → 触发）");
				if (!PrepareNativeState(snapshot, log))
				{
					detail = log.ToString();
					return false;
				}
			}

			snapshot = ReadSnapshot();
			if (!GuardStart(snapshot, high, allowInitialDischarge, log) || !CheckMailbox(0, log))
			{
				detail = log.ToString();
				return false;
			}

			// 数据先写、子命令后写、主命令最后（不写 0x83A0 / 0x83C0）
			log.AppendLine("写参数：0x8153=" + high + " → 0x8154=" + low);
			if (!WriteParam(EcPortIo.BatteryHighParamRegister, high, "0x8153=high", log)
				|| !WriteParam(EcPortIo.BatteryLowParamRegister, low, "0x8154=low", log))
			{
				detail = log.ToString();
				return false;
			}
			// 2026-10-22（宝龙达 MRID6：改阈值后弹回）：0x8153/0x8154 只写 mailbox 参数，
			// 常驻 0x83A1/0x83A2 由固件在**触发启用后**才更新 ⇒ 不能在触发前拿它做回读校验，
			// 否则改任何数值都会「参数回读不一致 ⇒ 不触发」，UI 看起来就是弹回原值。
			// 此处只依赖 WriteParam 的 mailbox 回读；常驻阈值在触发后统一校验。
			log.AppendLine("参数已写入 mailbox（0x83A1/0x83A2 由触发后固件更新，不在此处校验）");

			if (!CheckMailbox(0, log) || !WriteCommand(0x0D, "0x8151=0x0D(启用)", log))
			{
				detail = log.ToString();
				return false;
			}
			// 参数准备后再核对一次 SOC（参考实现同样二次核对）
			if (!GuardStart(ReadSnapshot(), high, allowInitialDischarge, log) || !CheckMailbox(0, log))
			{
				detail = log.ToString();
				return false;
			}
			if (!TriggerAndWait(ModeActive, log))
			{
				detail = log.ToString();
				return false;
			}

			// 触发后等固件把 mailbox 参数落到常驻阈值（最多 1 秒），再做一致性校验。
			Snapshot after = WaitForParams(high, low, 1000);
			log.AppendLine("启用后：" + after.Describe());
			bool paramsOk = after.High == high && after.Low == low;
			// 高电量开启时固件会先进入初始化放电子状态（substate=1）；这也算启用成功，稳定（2/3）会在放到阈值附近后达成。
			bool initializing = allowInitialDischarge && after.Substate == 1;
			bool settled = after.State == ModeNativeState && (after.Substate == 2 || after.Substate == 3 || initializing);
			if (!paramsOk || !settled)
			{
				log.AppendLine("⛔ 启用后校验未通过（阈值一致=" + paramsOk + " · 已稳定=" + settled + "）⇒ 如实报失败，不重试");
				detail = log.ToString();
				return false;
			}
			detail = log + "✅ 限充已启用（mode=0x48 · 高=" + high + "% · 低=" + low + "%）";
			return true;
		}
		catch (Exception ex)
		{
			detail = log + "异常：" + ex.GetType().Name + " " + ex.Message + "（不重试）";
			return false;
		}
	}

	/// <summary>关闭限充（恢复默认充电）；失败不重试。</summary>
	internal static bool TryDisable(out string detail)
	{
		StringBuilder log = new StringBuilder();
		try
		{
			if (!BatteryLimitPlatform.IsBaolongdaMachine())
			{
				detail = "非宝龙达机型 ⇒ 不执行限充写入（0 读 0 写）";
				return false;
			}
			if (!EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string idNote))
			{
				detail = "⛔ EC 身份未通过：" + idNote;
				return false;
			}
			log.AppendLine("EC 身份：" + idNote);
			Snapshot snapshot = ReadSnapshot();
			log.AppendLine("当前：" + snapshot.Describe());
			if (snapshot.Mode == 0)
			{
				detail = log + "当前已是正常充电策略（mode=0x00）⇒ 无需关闭";
				return true;
			}
			if (!DisableCore(log))
			{
				detail = log.ToString();
				return false;
			}
			detail = log.ToString();
			return true;
		}
		catch (Exception ex)
		{
			detail = log + "异常：" + ex.GetType().Name + " " + ex.Message + "（不重试）";
			return false;
		}
	}

	private static bool DisableCore(StringBuilder log)
	{
		Snapshot before = ReadSnapshot();
		if (before.Mode == 0)
		{
			log.AppendLine("已是正常模式 ⇒ 不写");
			return true;
		}
		if (!CheckMailbox(before.Mode, log) || !WriteCommand(0x0F, "0x8151=0x0F(关闭)", log))
		{
			return false;
		}
		if (!TriggerAndWait(0, log))
		{
			return false;
		}
		// EC 退出原生态有延迟（state/flags 会残留一会儿）⇒ 最多等 5s 再判失败，不立即读一次就下结论。
		Snapshot after = ReadSnapshot();
		long waitStart = Environment.TickCount64;
		while (Environment.TickCount64 - waitStart <= 5000)
		{
			after = ReadSnapshot();
			if (NormalPolicyActive(after))
			{
				break;
			}
			Thread.Sleep(50);
		}
		log.AppendLine("关闭后：" + after.Describe());
		if (!NormalPolicyActive(after))
		{
			log.AppendLine("⛔ 关闭结果校验未通过（应 mode=0x00 且 flags=0）⇒ 如实报失败，不重试");
			return false;
		}
		log.AppendLine("已恢复默认充电");
		return true;
	}

	/// <summary>清理原生初始化残留状态（参考实现：10/0E 停止态 → 10/0F 正常态）。</summary>
	private static bool PrepareNativeState(Snapshot snapshot, StringBuilder log)
	{
		if (NativeStateClean(snapshot))
		{
			return true;
		}
		if (!NormalPolicyActive(snapshot) || snapshot.Substate > 3)
		{
			log.AppendLine("⛔ 无法识别的残留状态（" + snapshot.Describe() + "）⇒ 不发清理命令");
			return false;
		}
		if (!CheckMailbox(0, log) || !WriteCommand(0x0E, "0x8151=0x0E(停止)", log))
		{
			return false;
		}
		if (!TriggerAndWait(2, log))
		{
			return false;
		}
		long deadline = Environment.TickCount64 + 8000;
		while (true)
		{
			Snapshot stopped = ReadSnapshot();
			if (stopped.Mode == 2 && stopped.State == 0x31)
			{
				break;
			}
			if (Environment.TickCount64 >= deadline)
			{
				log.AppendLine("⛔ 停止态未确认（" + stopped.Describe() + "）⇒ 不继续");
				return false;
			}
			Thread.Sleep(250);
		}
		if (!CheckMailbox(2, log) || !WriteCommand(0x0F, "0x8151=0x0F(恢复)", log))
		{
			return false;
		}
		if (!TriggerAndWait(0, log))
		{
			return false;
		}
		return WaitNormal(requireClean: true, log);
	}

	private static bool WaitNormal(bool requireClean, StringBuilder log)
	{
		long deadline = Environment.TickCount64 + 8000;
		while (true)
		{
			Snapshot snapshot = ReadSnapshot();
			if (NormalPolicyActive(snapshot) && (!requireClean || NativeStateClean(snapshot)))
			{
				log.AppendLine("正常充电策略已确认（" + snapshot.Describe() + "）");
				return true;
			}
			if (Environment.TickCount64 >= deadline)
			{
				log.AppendLine("⛔ 未确认正常充电策略（" + snapshot.Describe() + "）⇒ 不继续");
				return false;
			}
			Thread.Sleep(250);
		}
	}

	private static bool GuardStart(Snapshot snapshot, int high, bool allowInitialDischarge, StringBuilder log)
	{
		int soc = Math.Max(snapshot.Soc,
			(int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100.0));
		if (!allowInitialDischarge && soc >= high)
		{
			log.AppendLine("⛔ SOC(" + soc + "%) ≥ 停充阈值(" + high + "%)：默认要求 SOC < 阈值");
			log.AppendLine("（初始化可能触发固件放电分支；确认要允许时请打开「允许初始放电」）");
			return false;
		}
		if (allowInitialDischarge && soc >= high)
		{
			log.AppendLine("⚠️ SOC(" + soc + "%) ≥ 停充阈值(" + high + "%) 且已允许初始放电 ⇒ 固件会放电到阈值附近");
		}
		return true;
	}

	private static bool ValidateSnapshot(Snapshot snapshot, StringBuilder log)
	{
		if (snapshot.Mode == 0)
		{
			if (Array.IndexOf(NormalStates, snapshot.State) < 0)
			{
				log.AppendLine("⛔ 正常模式下 state=" + Fmt(snapshot.State) + " 不是已知状态");
				return false;
			}
			return true;
		}
		if (snapshot.Mode == 2)
		{
			return true;
		}
		if (snapshot.Mode == ModeActive)
		{
			if (snapshot.State != ModeNativeState)
			{
				log.AppendLine("⛔ 限充模式下 state=" + Fmt(snapshot.State) + " 异常");
				return false;
			}
			return true;
		}
		log.AppendLine("⛔ 当前模式=" + Fmt(snapshot.Mode) + " 不允许操作");
		return false;
	}

	private static bool NormalPolicyActive(Snapshot snapshot)
	{
		return snapshot.Mode == 0 && Array.IndexOf(NormalStates, snapshot.State) >= 0 && snapshot.Flags == 0;
	}

	private static bool NativeStateClean(Snapshot snapshot)
	{
		return snapshot.Substate == 0 && (snapshot.StateFlags & 7) == 0;
	}

	private static bool CheckMailbox(int expectedMode, StringBuilder log)
	{
		if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int trigger) || trigger != 0)
		{
			log.AppendLine("⛔ 0x8150=" + Fmt(trigger) + " ≠ 0 ⇒ 有未完成事务，不发命令");
			return false;
		}
		if (!ReadEc(ModeRegister, out int mode) || mode != expectedMode)
		{
			log.AppendLine("⛔ 0x83C0=" + Fmt(mode) + " ≠ 期望 " + Fmt(expectedMode) + " ⇒ 不发命令");
			return false;
		}
		return true;
	}

	private static bool TriggerAndWait(int expectedMode, StringBuilder log)
	{
		// 2026-10-21：触发前 mailbox 校验应针对“当前模式”，不是“目标模式”——
		// 目标模式要等触发执行完才会变（关闭时当前 0x48、目标 0x00）；拿目标去校验会永远判“不发命令”。
		if (!ReadEc(ModeRegister, out int currentMode))
		{
			log.AppendLine("⛔ 0x83C0 读不到 ⇒ 不发命令");
			return false;
		}
		if (!CheckMailbox(currentMode, log))
		{
			return false;
		}
		if (!EcPortIo.TryWriteBatteryTriggerPulse(out _))
		{
			log.AppendLine("⛔ 触发写 0x8150=0x10 失败");
			return false;
		}
		long start = Environment.TickCount64;
		int last = -1;
		while (Environment.TickCount64 - start <= TimeoutMs)
		{
			if (ReadEc(EcPortIo.BatteryTriggerRegister, out last) && last == 0)
			{
				break;
			}
			Thread.Sleep(20);
		}
		if (last != 0)
		{
			log.AppendLine("⛔ 等待 0x8150 归 0 超时（最后=" + Fmt(last) + "）⇒ 不重试");
			return false;
		}
		if (!ReadEc(ResultRegister, out int result) || result != 0x55)
		{
			log.AppendLine("⛔ 命令结果 0x8152=" + Fmt(result) + "（应 0x55）");
			return false;
		}
		if (!ReadEc(ModeRegister, out int mode) || mode != expectedMode)
		{
			log.AppendLine("⛔ 命令后 0x83C0=" + Fmt(mode) + "（应 " + Fmt(expectedMode) + "）");
			return false;
		}
		log.AppendLine("命令完成：0x8152=0x55 · 0x83C0=" + Fmt(mode));
		return true;
	}

	private static bool WriteParam(int address, int value, string label, StringBuilder log)
	{
		bool ok = EcPortIo.TryWriteBatteryByte(address, (byte)value, out int readBack);
		log.AppendLine(label + " ⇒ " + (ok ? "回读 0x" + readBack.ToString("X2") : "失败"));
		return ok;
	}

	private static bool WriteCommand(int value, string label, StringBuilder log)
	{
		bool ok = EcPortIo.TryWriteBatteryByte(EcPortIo.BatteryCommandRegister, (byte)value, out int readBack);
		log.AppendLine(label + " ⇒ " + (ok ? "回读 0x" + readBack.ToString("X2") : "失败"));
		return ok;
	}

	/// <summary>2026-10-22：触发后等待 0x83A1/0x83A2 落到目标值（固件异步更新），最多 timeoutMs。</summary>
	private static Snapshot WaitForParams(int high, int low, int timeoutMs)
	{
		long start = Environment.TickCount64;
		Snapshot snapshot = ReadSnapshot();
		while (Environment.TickCount64 - start < timeoutMs)
		{
			if (snapshot.High == high && snapshot.Low == low)
			{
				break;
			}
			Thread.Sleep(50);
			snapshot = ReadSnapshot();
		}
		return snapshot;
	}

	private static bool ReadEc(int address, out int value)
	{
		return EcPortIo.TryReadByteStable(address, out value, out _, attempts: 3);
	}

	private static Snapshot ReadSnapshot()
	{
		Snapshot snapshot = default(Snapshot);
		snapshot.High = ReadOrMinusOne(HighRegister);
		snapshot.Low = ReadOrMinusOne(LowRegister);
		snapshot.Mode = ReadOrMinusOne(ModeRegister);
		snapshot.State = ReadOrMinusOne(StateRegister);
		snapshot.Substate = ReadOrMinusOne(SubstateRegister);
		snapshot.Flags = ReadOrMinusOne(FlagsRegister);
		snapshot.StateFlags = ReadOrMinusOne(StateFlagsRegister);
		snapshot.Soc = ReadOrMinusOne(SocRegister);
		return snapshot;
	}

	private static int ReadOrMinusOne(int address)
	{
		return ReadEc(address, out int value) ? value : -1;
	}

	private static string Fmt(int value)
	{
		return value < 0 ? "(读不到)" : "0x" + value.ToString("X2");
	}

	private static PowerLineStatus PowerState()
	{
		return SystemInformation.PowerStatus.PowerLineStatus;
	}

	private struct Snapshot
	{
		internal int High;

		internal int Low;

		internal int Mode;

		internal int State;

		internal int Substate;

		internal int Flags;

		internal int StateFlags;

		internal int Soc;

		internal readonly string Describe()
		{
			return "mode=" + Fmt(Mode) + " · state=" + Fmt(State) + " · 83A0=" + Fmt(Substate)
				+ " · 8394=" + Fmt(Flags) + " · 8395=" + Fmt(StateFlags)
				+ " · 83A1=" + Fmt(High) + " · 83A2=" + Fmt(Low) + " · SOC=" + Fmt(Soc);
		}
	}
}
