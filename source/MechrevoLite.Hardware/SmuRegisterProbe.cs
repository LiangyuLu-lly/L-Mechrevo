// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **SMU 寄存器只读扫描**（阶段 1）`--smu-reg-probe` ⭐⭐
//
// ⭐⭐ 背景（机主坚持 SMU 值得试，批准分阶段）：
//    `RyzenSmuService`（PawnIO RyzenSMU）暴露 `ioctl_read_smu_register(addr)` / `ioctl_write_smu_register(addr,value)`
//    （RSMU/APM 寄存器，非邮箱命令），以及 PM table 只读接口；**没有现成的“SMU-EC”命令**。
//    7.3 的 `Write_EC_EX` 已证明是 LPC 端口 I/O（见 docs\notes\风扇EC时序对照-20261018.md §9）。
//
// ⭐ 阶段 1（本命令，**纯只读**）：
//    1. 初始化 `RyzenSmuService`（只读代号/版本，不发邮箱命令）；
//    2. 对一小批候选 SMU 寄存器做**只读**读（每读之间 sleep 10ms）：
//       `0x0000 / 0x0018 / 0x0020 / 0x0030 / 0x0038 / 0x0040 / 0x0060 / 0x0064 / 0x0068 / 0x0070`；
//       打印：地址、返回码（rc）、值、`LastError`（Win32）；
//    3. 用 ACPI EC 只读读 `0x2000=0x55` / `0xC834` / `0xC835` / `0x1060` / `0xB20` 作对照；
//       若某个 SMU 寄存器值与这些 EC 值存在可解释对应（相等/低字节相等）⇒ 记录为“疑似映射”；
//    4. 结论：有/没有 EC 映射迹象；**⛔ 本阶段一个写都不发**；只有阶段 1 找到可信映射，才在阶段 2 做一次写实验。
//
// ⛔ 安全：只读、候选地址很少、不循环扫大范围；⛔ 不发邮箱命令、⛔ 不写 SMU/EC；
//    ⛔ 不打包、不 kill LM；⛔ 不抛出异常。
// ⭐ 输出：桌面 `lm-smu-reg-probe-<时间戳>.txt` + `log.txt`（前缀 `[smu-reg-probe]`）。
// ⭐ 用法：`L-Mechrevo.exe --smu-reg-probe`（需管理员）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using PawnIO;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ SMU 寄存器**只读**扫描（阶段 1）：候选寄存器 + EC 对照 + 映射迹象 ✅。</summary>
internal static class SmuRegisterProbe
{
	private static readonly uint[] Candidates =
	{
		0x0000u, 0x0018u, 0x0020u, 0x0030u, 0x0038u, 0x0040u, 0x0060u, 0x0064u, 0x0068u, 0x0070u
	};

	internal static int Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-smu-reg-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-smu-reg-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[smu-reg-probe] " + s); } catch { }
		}

		int exitCode = 1;
		try
		{
			L("=== SMU 寄存器只读扫描（阶段 1 · --smu-reg-probe）===");
			L("⛔ 纯只读：不发邮箱命令、不写任何 SMU/EC 寄存器");
			L("");

			L("── 1. RyzenSmuService 初始化 ──");
			RyzenSmuService smu = null;
			bool init = false;
			try
			{
				smu = new RyzenSmuService();
				init = smu.Initialize(Assembly.GetExecutingAssembly());
				L("SMU 初始化 = " + init);
				if (init)
				{
					L("CPU 代号 = " + smu.CpuCodeName + " · 家族 = " + smu.Family + " · SMU 版本 = 0x" + smu.SmuVersion.ToString("X"));
				}
			}
			catch (Exception ex)
			{
				L("SMU 初始化异常：" + ex.GetType().Name + " " + ex.Message);
			}
			L("");

			// ── 2. 候选寄存器只读 ──
			L("── 2. 候选 SMU 寄存器只读（每读间隔 10ms）──");
			uint[] values = new uint[Candidates.Length];
			bool[] okFlags = new bool[Candidates.Length];
			if (!init)
			{
				L("⛔ SMU 未初始化 ⇒ 不读取任何寄存器（no I/O）");
				for (int i = 0; i < values.Length; i++)
				{
					values[i] = 0;
					okFlags[i] = false;
				}
			}
			else
			{
				for (int i = 0; i < Candidates.Length; i++)
				{
					uint address = Candidates[i];
					bool ok = smu.TryReadSmuRegister(address, out uint value, out int lastError);
					okFlags[i] = ok;
					values[i] = ok ? value : 0;
					L("  SMU reg 0x" + address.ToString("X4") + " ⇒ rc=" + (ok ? "OK" : "FAIL")
						+ " · value=0x" + value.ToString("X8")
						+ " · LastError=" + lastError);
					Thread.Sleep(10);
				}
			}
			L("");

			// ── 3. EC 只读对照 ──
			L("── 3. EC 只读对照（ACPI EC 通道，稳定读）──");
			int[] ecAddresses = { 0x2000, 0x1060, 0xC834, 0xC835, 0xB20 };
			string[] ecNames = { "0x2000 芯片 ID", "0x1060 解锁", "0xC834 Fan1 转速字节", "0xC835 Fan2 转速字节", "0xB20 接管掩码" };
			int[] ecValues = new int[ecAddresses.Length];
			for (int i = 0; i < ecAddresses.Length; i++)
			{
				bool ok = EcPortIo.TryReadByteStable(ecAddresses[i], out int value, out string note);
				ecValues[i] = ok ? value : -1;
				L("  " + ecNames[i] + " = " + (ok ? "0x" + value.ToString("X2") : "读不到") + "（" + note + "）");
			}
			L("");

			// ── 4. 映射迹象 ──
			L("── 4. 映射迹象（SMU 值 vs EC 值）──");
			bool anyHint = false;
			for (int i = 0; i < Candidates.Length; i++)
			{
				if (!okFlags[i])
				{
					continue;
				}
				for (int j = 0; j < ecValues.Length; j++)
				{
					if (ecValues[j] < 0)
					{
						continue;
					}
					uint ec = (uint)(ecValues[j] & 0xFF);
					if (values[i] == ec || (values[i] & 0xFF) == ec)
					{
						anyHint = true;
						L("  ⭐ 疑似：SMU reg 0x" + Candidates[i].ToString("X4") + " = 0x" + values[i].ToString("X8")
							+ " 与 " + ecNames[j] + "（0x" + ec.ToString("X2") + "）相等/低字节相等");
					}
				}
			}
			if (!anyHint)
			{
				L("  （没有发现 SMU 值与 EC 值相等/低字节相等的对应）");
			}
			L("");

			// ── 5. 结论 ──
			L("── 5. 结论 ──");
			if (!init)
			{
				L("⛔ 不可用 —— SMU 初始化失败，无法读取寄存器（阶段 1 结束，未发任何写）");
			}
			else if (anyHint)
			{
				L("⚠️ 发现疑似对应 ⇒ ⭐ 需要人工确认（可能只是巧合；⛔ 阶段 2 写入前必须先确认映射）");
				L("⛔ 本命令**未发任何写**；阶段 2 需负责人再确认");
				exitCode = 2; // 2 = 有线索待确认（非“可用”）
			}
			else
			{
				L("⛔ **没有发现 SMU 暴露 EC 窗口的迹象** ⇒ 结论「SMU 不提供 EC 通道」");
				L("⛔ 本命令**未发任何写**；⛔ 阶段 2 不做写实验");
				exitCode = 1;
			}
			L("");
			L("⭐ 说明：`ioctl_read_smu_register` 读的是 RSMU/APM 寄存器（非邮箱命令）；");
			L("   与 EC `0xC83C/0x1060` 没有已知硬件映射 —— 7.3 的 EC 路径是 LPC 端口 I/O（见 §9）。");
		}
		catch (Exception ex)
		{
			L("扫描异常：" + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
		}
		return exitCode;
	}
}
